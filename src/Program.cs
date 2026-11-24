using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Луг · Stardew Valley")]
[assembly: AssemblyDescription("Менеджер модов Stardew Valley")]
[assembly: AssemblyVersion("1.5.0.0")]
[assembly: AssemblyFileVersion("1.5.0.0")]

namespace Lug
{
    internal static class Native
    {
        private const string Protocol = @"Software\Classes\nxm";
        private const string Command = @"\shell\open\command";
        private const string History = @"Software\StardewLug\NexusHandler";
        private static string OurCommand { get { return "\"" + Application.ExecutablePath + "\" \"%1\""; } }

        [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr ptr);
        [DllImport("shell32.dll")] private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
        [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CommandLineToArgvW(string argument, out int count);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr handle);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, string lParam);
        [DllImport("dwmapi.dll")]
        internal static extern int DwmSetWindowAttribute(IntPtr hwnd, uint attribute, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        internal static extern int SetWindowTheme(IntPtr hwnd, string theme, string part);

        internal static void Open(string destination)
        {
            Uri uri;
            bool isUri = Uri.TryCreate(destination, UriKind.Absolute, out uri);
            if (!Directory.Exists(destination) && (!isUri || uri.Scheme != "https")) return;
            try { using (Process.Start(new ProcessStartInfo(destination) { UseShellExecute = true })) { } }
            catch (System.ComponentModel.Win32Exception error)
            {
                MessageBox.Show(error.Message, "Луг", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        internal static ProcessStartInfo GameStart(string game, string mods)
        {
            if (!Store.IsGame(game) || !File.Exists(Path.Combine(game, "StardewModdingAPI.exe")))
                throw new IOException("SMAPI не найден. Проверь папку игры.");
            mods = Path.GetFullPath(mods).TrimEnd(Path.DirectorySeparatorChar);
            if (!Directory.Exists(mods)) throw new IOException("Папка модов не найдена.");
            return new ProcessStartInfo(Path.Combine(game, "StardewModdingAPI.exe"), "--mods-path \"" + mods + "\"")
            {
                UseShellExecute = false, WorkingDirectory = game
            };
        }

        internal static string ReadRegistry(RegistryKey hive, string path, string name = "")
        {
            try
            {
                using (var key = hive.OpenSubKey(path)) return key == null ? null : key.GetValue(name, null) as string;
            }
            catch (System.Security.SecurityException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        internal static bool IsHandler()
        {
            return string.Equals(ReadRegistry(Registry.CurrentUser, Protocol + Command), OurCommand, StringComparison.OrdinalIgnoreCase);
        }

        internal static void SetHandler(bool enabled)
        {
            if (enabled)
            {
                if (IsHandler()) return;
                string oldUser = ReadRegistry(Registry.CurrentUser, Protocol + Command);
                string previous = oldUser ?? ReadRegistry(Registry.LocalMachine, Protocol + Command);
                string description = ReadRegistry(Registry.CurrentUser, Protocol);
                string marker = ReadRegistry(Registry.CurrentUser, Protocol, "URL Protocol");
                using (var history = Registry.CurrentUser.CreateSubKey(History))
                {
                    SetOptional(history, "SavedUserCommand", oldUser);
                    SetOptional(history, "PreviousHandler", previous);
                    SetOptional(history, "SavedDescription", description);
                    SetOptional(history, "SavedProtocolMarker", marker);
                }
                using (var root = Registry.CurrentUser.CreateSubKey(Protocol))
                {
                    if (description == null) root.SetValue("", "URL:Nexus Mods");
                    if (marker == null) root.SetValue("URL Protocol", "");
                }
                using (var open = Registry.CurrentUser.CreateSubKey(Protocol + Command)) open.SetValue("", OurCommand);
            }
            else if (IsHandler())
            {
                using (var open = Registry.CurrentUser.CreateSubKey(Protocol + Command))
                    SetOptional(open, "", ReadRegistry(Registry.CurrentUser, History, "SavedUserCommand"));
                using (var root = Registry.CurrentUser.CreateSubKey(Protocol))
                {
                    SetOptional(root, "", ReadRegistry(Registry.CurrentUser, History, "SavedDescription"));
                    SetOptional(root, "URL Protocol", ReadRegistry(Registry.CurrentUser, History, "SavedProtocolMarker"));
                }
            }
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        }

        private static void SetOptional(RegistryKey key, string name, string value)
        {
            if (value == null) key.DeleteValue(name, false);
            else key.SetValue(name, value);
        }

        internal static void ForwardOtherGame(string uri)
        {
            string previous = ReadRegistry(Registry.CurrentUser, History, "PreviousHandler");
            if (string.IsNullOrEmpty(previous)) throw new IOException("Для этой игры не найден прежний менеджер модов.");
            int count;
            var argv = CommandLineToArgvW(previous, out count);
            if (argv == IntPtr.Zero || count == 0) throw new IOException("Не удалось открыть прежний менеджер модов.");
            try
            {
                string exe = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv));
                if (string.Equals(Application.ExecutablePath, exe, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("В Windows не найден другой менеджер для этого мода.");
                string[] args = Enumerable.Range(1, count - 1).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))).ToArray();
                bool supplied = args.Any(arg => arg.Contains("%1") || arg.Contains("%L") || arg.Contains("%l"));
                var command = string.Join(" ", args.Select(arg => Quoted(arg.Replace("%1", uri).Replace("%L", uri).Replace("%l", uri))));
                if (!supplied) command += " " + Quoted(uri);
                using (Process.Start(new ProcessStartInfo(exe, command) { UseShellExecute = false })) { }
            }
            finally { LocalFree(argv); }
        }

        private static string Quoted(string value)
        {
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char ch in value)
            {
                if (ch == '\\') { slashes++; continue; }
                result.Append('\\', ch == '"' ? slashes * 2 + 1 : slashes);
                result.Append(ch);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
    }

    internal static class Program
    {
        [STAThread]
        internal static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            try
            {
                string link = args.FirstOrDefault();
                if (link == LauncherUpdate.ApplyFlag && args.Length == 2)
                {
                    LauncherUpdate.Apply(args[1], Application.ExecutablePath,
                        problem => MessageBox.Show(problem, "Обновление Луга", MessageBoxButtons.OK, MessageBoxIcon.Warning));
                    return;
                }
                string ready = link == LauncherUpdate.ReadyFlag && args.Length == 2 ? args[1] : null;
                string failed = link == LauncherUpdate.FailedFlag && args.Length == 2 ? args[1] : null;
                if (ready != null || failed != null) link = null;
                if (!string.IsNullOrEmpty(link) && link.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase) && NxmLink.Parse(link) == null)
                {
                    Uri other;
                    if (Uri.TryCreate(link, UriKind.Absolute, out other) && !other.Host.Equals("stardewvalley", StringComparison.OrdinalIgnoreCase))
                    {
                        Native.ForwardOtherGame(link);
                        return;
                    }
                    throw new IOException("Некорректная ссылка Nexus для Stardew Valley.");
                }
                string sid = WindowsIdentity.GetCurrent().User.Value;
                string name = "StardewLug-1-" + sid;
                using (var mutex = new Mutex(false, @"Local\" + name))
                {
                    bool owner;
                    try { owner = mutex.WaitOne(0); }
                    catch (AbandonedMutexException) { owner = true; }
                    if (!owner)
                    {
                        if (ready == null) Send(name, link ?? "");
                        return;
                    }
                    try
                    {
                        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StardewLug");
                        var store = new Store(root);
                        string startupMessage = ready == null ? (failed == null ? null : LauncherUpdate.FailureNote(failed)) :
                            "Луг обновлён до " + LauncherUpdate.Format(LauncherUpdate.Current) + ".";
                        var window = new Window(store, new Sources(), ready == null ? (Action)null :
                            () => LauncherUpdate.MarkReady(ready, Application.ExecutablePath), startupMessage, failed != null);
                        window.HandleCreated += (sender, e) =>
                        {
                            int dark = 1;
                            try { Native.DwmSetWindowAttribute(window.Handle, 20, ref dark, sizeof(int)); }
                            catch (DllNotFoundException) { }
                        };
                        window.Shown += (sender, e) =>
                        {
                            Task.Run(() => Listen(name, new SecurityIdentifier(sid), window));
                            Task.Run(() => LauncherUpdate.Prune(ready ?? failed));
                            if (!string.IsNullOrEmpty(link)) window.Receive(link);
                        };
                        Application.Run(window);
                    }
                    finally { mutex.ReleaseMutex(); }
                }
            }
            catch (Exception error)
            {
                MessageBox.Show(error.Message, "Луг", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static void Send(string name, string value)
        {
            byte[] payload = Encoding.UTF8.GetBytes(value);
            if (payload.Length > 4096) throw new IOException("Ссылка слишком длинная.");
            using (var stream = new NamedPipeClientStream(".", name, PipeDirection.Out))
            {
                stream.Connect(6000);
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(payload.Length);
                    writer.Write(payload);
                }
            }
        }

        private static void Listen(string name, SecurityIdentifier sid, Window window)
        {
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.FullControl, AccessControlType.Allow));
            while (!window.IsDisposed)
            {
                try
                {
                    using (var stream = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None, 4096, 4096, security))
                    {
                        stream.WaitForConnection();
                        using (var reader = new BinaryReader(stream))
                        {
                            int length = reader.ReadInt32();
                            if (length >= 0 && length <= 4096) window.Receive(Encoding.UTF8.GetString(reader.ReadBytes(length)));
                        }
                    }
                }
                catch (IOException) { }
                catch (ObjectDisposedException) { return; }
            }
        }
    }
}
