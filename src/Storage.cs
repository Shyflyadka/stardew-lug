using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Lug
{
    internal static class Json
    {
        internal static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024, RecursionLimit = 100 };
        }

        internal static Dictionary<string, object> Object(object obj)
        {
            return obj as Dictionary<string, object> ?? new Dictionary<string, object>();
        }

        internal static object Get(object obj, string key)
        {
            object value;
            return Object(obj).TryGetValue(key, out value) ? value : null;
        }

        internal static string Str(object obj, string key)
        {
            return Convert.ToString(Get(obj, key), CultureInfo.InvariantCulture) ?? "";
        }

        internal static int Num(object obj, string key)
        {
            int n;
            return int.TryParse(Str(obj, key), out n) ? n : 0;
        }

        internal static IEnumerable<object> List(object obj)
        {
            var values = obj as IEnumerable;
            return values == null || obj is string || obj is IDictionary ? Enumerable.Empty<object>() : values.Cast<object>();
        }

        internal static T Load<T>(string path) where T : new()
        {
            return File.Exists(path) ? Serializer().Deserialize<T>(File.ReadAllText(path)) : new T();
        }

        internal static void Save(string path, object value)
        {
            string temporary = path + ".new";
            File.WriteAllText(temporary, Serializer().Serialize(value), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }

    public sealed class Settings
    {
        public string Game { get; set; }
        public string Mo2 { get; set; }
        public string NexusSecret { get; set; }
        public string NexusName { get; set; }
        public bool Premium { get; set; }
        public string SelectedBuild { get; set; }
        public string OpenedPack { get; set; }
        public bool ReduceMotion { get; set; }
        public bool CompactRows { get; set; }
        public bool MinimizeOnLaunch { get; set; }
        public bool SkipLaunchWarnings { get; set; }
        public string LauncherUpdateSource { get; set; }

        internal string Key()
        {
            if (string.IsNullOrEmpty(NexusSecret)) return "";
            try
            {
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(NexusSecret), null, DataProtectionScope.CurrentUser));
            }
            catch (CryptographicException) { return ""; }
            catch (FormatException) { return ""; }
        }

        internal void SetKey(string key, string name, bool premium)
        {
            NexusSecret = key.Length == 0 ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));
            NexusName = name;
            Premium = premium;
        }
    }

    public sealed class ModMeta
    {
        public string Source { get; set; }
        public int NexusId { get; set; }
        public int FileId { get; set; }
        public string GitRepo { get; set; }
        public string Asset { get; set; }
        public string Category { get; set; }
    }

    internal sealed class Mod
    {
        internal string Id, Name, Version, Author, Description, Path, Problem, NewVersion, UpdateUrl, GitRepo;
        internal string MinimumSmapi, MinimumGame, ContentPackFor, OnlineStatus, OnlineNote, BrokeIn;
        internal string[] Keys;
        internal readonly List<Tuple<string, string>> Dependencies = new List<Tuple<string, string>>();
        internal readonly List<string> DependencyIds = new List<string>();
        internal bool Enabled;
        internal ModMeta Meta;
        internal int NexusId
        {
            get
            {
                if (Meta.NexusId > 0) return Meta.NexusId;
                var value = Keys.FirstOrDefault(key => key.StartsWith("Nexus:", StringComparison.OrdinalIgnoreCase));
                int id;
                return value != null && int.TryParse(value.Substring(6), out id) ? id : 0;
            }
        }
    }

    public sealed class Transfer
    {
        public string From { get; set; }
        public string To { get; set; }
        public bool IsFile { get; set; }
    }

    internal sealed partial class Store
    {
        private const string MetaName = ".lug.json";
        private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
        private static readonly Regex JsonTrivia = new Regex(@"""(?:[^""\\]|\\.)*""|//[^\r\n]*|/\*[\s\S]*?\*/|,(?=\s*[}\]])", RegexOptions.Compiled);
        private static readonly Regex TrailingComma = new Regex(@"""(?:[^""\\]|\\.)*""|,(?=\s*[}\]])", RegexOptions.Compiled);
        internal readonly string Root, Staging;
        internal string Active { get { return Path.Combine(BuildRoot, "Mods"); } }
        internal string Disabled { get { return Path.Combine(BuildRoot, "Disabled"); } }
        internal string Backups { get { return Path.Combine(BuildRoot, "Backups"); } }
        private readonly string journal;
        internal Settings Settings;

        internal Store(string root)
        {
            Root = Path.GetFullPath(root);
            Staging = Path.Combine(Root, "Work");
            journal = Path.Combine(Root, "transaction.json");
            foreach (var folder in new[] { Root, Staging, BuildShelf })
                if (Directory.Exists(folder) && IsLink(folder)) throw new IOException("Папка данных является ссылкой. Выбери обычную папку.");
            foreach (var folder in new[] { Root, Staging, BuildShelf }) Directory.CreateDirectory(folder);
            Recover();
            Settings = Json.Load<Settings>(Path.Combine(Root, "settings.json"));
            if (!Builds().Any(build => build.Id == Settings.SelectedBuild)) Settings.SelectedBuild = "";
            EnsureBuildFolders();
            if (!IsGame(Settings.Game)) Settings.Game = DetectGame();
            if (string.IsNullOrWhiteSpace(Settings.Mo2) && !string.IsNullOrEmpty(Settings.Game))
            {
                string mo = Path.Combine(Settings.Game, "SDV MO2");
                if (Directory.Exists(Path.Combine(mo, "profiles"))) Settings.Mo2 = mo;
            }
        }

        internal void SaveSettings() { Json.Save(Path.Combine(Root, "settings.json"), Settings); }

        internal static bool IsGame(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, "Stardew Valley.exe"));
        }

        internal string GameVersion(string filename)
        {
            if (string.IsNullOrEmpty(Settings.Game)) return "";
            string path = Path.Combine(Settings.Game, filename);
            if (!File.Exists(path)) return "";
            var info = FileVersionInfo.GetVersionInfo(path);
            return string.Format("{0}.{1}.{2}", info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
        }

        private static string DetectGame()
        {
            var steam = new List<string>();
            steam.Add(Native.ReadRegistry(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"));
            steam.Add(Native.ReadRegistry(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"));
            steam.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
            {
                steam.Add(Path.Combine(drive.Name, "Steam"));
                steam.Add(Path.Combine(drive.Name, "SteamLibrary"));
            }
            var libraries = new HashSet<string>(Names);
            foreach (var dir in steam.Where(Directory.Exists))
            {
                libraries.Add(dir);
                string vdf = Path.Combine(dir, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                try
                {
                    foreach (Match match in Regex.Matches(File.ReadAllText(vdf), @"""path""\s+""([^""]+)"""))
                        libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return libraries.Select(dir => Path.Combine(dir, "steamapps", "common", "Stardew Valley")).FirstOrDefault(IsGame) ?? "";
        }

        internal static int CompareVersion(string left, string right)
        {
            var pattern = @"^v?(\d+(?:\.\d+){0,3})(?:-([\w.-]+))?(?:\+.*)?\s*$";
            var a = Regex.Match(left ?? "", pattern, RegexOptions.IgnoreCase);
            var b = Regex.Match(right ?? "", pattern, RegexOptions.IgnoreCase);
            if (!a.Success || !b.Success) return 0;
            var an = a.Groups[1].Value.Split('.');
            var bn = b.Groups[1].Value.Split('.');
            for (int i = 0; i < Math.Max(an.Length, bn.Length); i++)
            {
                long av = 0, bv = 0;
                if (i < an.Length) long.TryParse(an[i], out av);
                if (i < bn.Length) long.TryParse(bn[i], out bv);
                if (av != bv) return av.CompareTo(bv);
            }
            var ap = a.Groups[2].Value;
            var bp = b.Groups[2].Value;
            if (ap.Length == 0 || bp.Length == 0) return (ap.Length == 0).CompareTo(bp.Length == 0);
            var leftParts = ap.Split('.');
            var rightParts = bp.Split('.');
            for (int i = 0; i < Math.Min(leftParts.Length, rightParts.Length); i++)
            {
                long av, bv;
                bool numericA = long.TryParse(leftParts[i], NumberStyles.None, CultureInfo.InvariantCulture, out av);
                bool numericB = long.TryParse(rightParts[i], NumberStyles.None, CultureInfo.InvariantCulture, out bv);
                int compared = numericA && numericB ? av.CompareTo(bv) : numericA != numericB ? (numericA ? -1 : 1) :
                    StringComparer.OrdinalIgnoreCase.Compare(leftParts[i], rightParts[i]);
                if (compared != 0) return compared;
            }
            return leftParts.Length.CompareTo(rightParts.Length);
        }

        internal static Mod ReadManifest(string manifest, bool readMetadata = true)
        {
            var folder = Path.GetDirectoryName(manifest);
            return ReadManifestText(File.ReadAllText(manifest), folder,
                readMetadata ? Json.Load<ModMeta>(Path.Combine(folder, MetaName)) ?? new ModMeta() : new ModMeta());
        }

        internal static Mod ReadManifestText(string text, string folder, ModMeta metadata)
        {
            object doc;
            try
            {
                string clean = JsonTrivia.Replace(text, m => m.Value[0] == '"' ? m.Value : "");
                clean = TrailingComma.Replace(clean, m => m.Value[0] == '"' ? m.Value : "");
                doc = Json.Serializer().DeserializeObject(clean);
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            var id = Json.Str(doc, "UniqueID").Trim();
            if (id.Length == 0) return null;
            var mod = new Mod
            {
                Path = folder, Id = id, Name = Json.Str(doc, "Name").Trim(), Version = Json.Str(doc, "Version").Trim(),
                Author = Json.Str(doc, "Author"), Description = Json.Str(doc, "Description").Trim(),
                MinimumSmapi = Json.Str(doc, "MinimumApiVersion"), MinimumGame = Json.Str(doc, "MinimumGameVersion"),
                Keys = Json.List(Json.Get(doc, "UpdateKeys")).OfType<string>().ToArray(),
                Meta = metadata ?? new ModMeta()
            };
            if (mod.Name.Length == 0) mod.Name = id;
            foreach (var dep in Json.List(Json.Get(doc, "Dependencies")))
            {
                string depId = Json.Str(dep, "UniqueID");
                if (depId.Length == 0) continue;
                mod.DependencyIds.Add(depId);
                if (!string.Equals(Json.Str(dep, "IsRequired"), "false", StringComparison.OrdinalIgnoreCase))
                    mod.Dependencies.Add(Tuple.Create(depId, Json.Str(dep, "MinimumVersion")));
            }
            var contentFor = Json.Get(doc, "ContentPackFor");
            string parent = Json.Str(contentFor, "UniqueID");
            mod.ContentPackFor = parent;
            if (parent.Length > 0) mod.Dependencies.Add(Tuple.Create(parent, Json.Str(contentFor, "MinimumVersion")));
            mod.GitRepo = mod.Meta.GitRepo ?? mod.Keys.FirstOrDefault(k => k.StartsWith("GitHub:", StringComparison.OrdinalIgnoreCase));
            if (mod.GitRepo != null && mod.GitRepo.StartsWith("GitHub:", StringComparison.OrdinalIgnoreCase))
                mod.GitRepo = mod.GitRepo.Substring(7).Split('@')[0];
            return mod;
        }

        internal List<Mod> ListMods()
        {
            var all = new List<Mod>();
            foreach (var root in new[] { Active, Disabled })
                foreach (string dir in Directory.EnumerateDirectories(root).Where(dir => !IsLink(dir)))
                {
                    string file = Path.Combine(dir, "manifest.json");
                    if (!File.Exists(file) || IsLink(file)) continue;
                    var mod = ReadManifest(file);
                    if (mod == null) continue;
                    mod.Enabled = root == Active;
                    all.Add(mod);
                }
            foreach (var group in Review(all, item => item.Enabled).GroupBy(issue => issue.Mod))
                group.Key.Problem = string.Join("\n", group.Select(issue => issue.Message).Distinct(Names));
            return all.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        internal void Toggle(Mod mod)
        {
            var destination = Path.Combine(mod.Enabled ? Disabled : Active, Path.GetFileName(mod.Path));
            Directory.Move(mod.Path, destination);
        }

        internal static bool IsLink(string path) { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }

        internal static IEnumerable<string> Files(string root, bool includeHidden = false)
        {
            if (!Directory.Exists(root) || IsLink(root)) yield break;
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                foreach (var file in Directory.EnumerateFiles(directory))
                    if (!IsLink(file)) yield return file;
                foreach (var folder in Directory.EnumerateDirectories(directory))
                {
                    string name = Path.GetFileName(folder);
                    if ((!includeHidden && (name.StartsWith(".") || name.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase))) || IsLink(folder)) continue;
                    pending.Push(folder);
                }
            }
        }

        private static string Relative(string root, string path) { return path.Substring(root.Length + 1); }

        private static string Slug(string value)
        {
            var result = Regex.Replace(value, @"[^a-zA-Z0-9_-]", "-").Trim('-');
            return result.Length > 28 ? result.Substring(0, 28) : result;
        }

        private static string Slot(Mod mod)
        {
            return (Slug(mod.Name) + "-" + Guid.NewGuid().ToString("N").Substring(0, 10)).TrimStart('-');
        }

        private void Recover()
        {
            if (!File.Exists(journal)) return;
            var plan = Json.Load<List<Transfer>>(journal);
            foreach (var move in plan)
            {
                var root = Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!Path.GetFullPath(move.From).StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                    !Path.GetFullPath(move.To).StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Не удалось проверить пути незавершённой операции.");
            }
            Apply(plan);
        }

        private void Commit(List<Transfer> plan)
        {
            if (File.Exists(journal)) throw new IOException("Перезапусти приложение, чтобы завершить предыдущую операцию.");
            if (plan.Select(x => x.To).Distinct(Names).Count() != plan.Count)
                throw new IOException("У двух модов совпадает папка назначения.");
            var freed = new HashSet<string>(Names);
            foreach (var move in plan)
            {
                if (!(move.IsFile ? File.Exists(move.From) : Directory.Exists(move.From)) || IsLink(move.From) ||
                    ((Directory.Exists(move.To) || File.Exists(move.To)) && !freed.Contains(move.To)))
                    throw new IOException("Папки модов изменились. Обнови список и повтори.");
                freed.Add(move.From);
            }
            Json.Save(journal, plan);
            Apply(plan);
        }

        private void Apply(List<Transfer> plan)
        {
            // A present target marks a completed rename. Targets are unique within the plan.
            foreach (var move in plan)
            {
                if (move.IsFile ? File.Exists(move.To) : Directory.Exists(move.To)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(move.To));
                if (move.IsFile) File.Move(move.From, move.To);
                else Directory.Move(move.From, move.To);
            }
            File.Delete(journal);
        }

        private string Work() { return Path.Combine(Staging, Guid.NewGuid().ToString("N")); }

        private void Cleanup(string path)
        {
            if (File.Exists(journal) || !Directory.Exists(path)) return;
            try { Directory.Delete(path, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal string[] Profiles(string mo2)
        {
            var root = Path.Combine(mo2, "profiles");
            return Directory.Exists(root) ? Directory.EnumerateDirectories(root)
                .Where(dir => File.Exists(Path.Combine(dir, "modlist.txt")))
                .Select(Path.GetFileName).OrderBy(name => name).ToArray() : new string[0];
        }

        private static void AddOverlay(Dictionary<string, string> index, string folder)
        {
            foreach (var file in Files(folder))
            {
                string filename = Path.GetFileName(file);
                if (Names.Equals(filename, "meta.ini") || Names.Equals(filename, MetaName)) continue;
                index[Relative(folder, file)] = file;
            }
        }

        private static void Copy(string source, string dest)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            File.Copy(source, dest, true);
        }

        internal int Import(string mo2, string profile, bool stock, Action<string> status)
        {
            if (ListMods().Count > 0) throw new IOException("Импорт сборки доступен, пока список модов пуст.");
            string work = Work();
            Directory.CreateDirectory(work);
            try
            {
                var plan = new List<Transfer>();
                var overlay = new Dictionary<string, string>(Names);
                var sources = new Dictionary<string, ModMeta>(Names);
                if (IsGame(Settings.Game)) AddOverlay(overlay, Path.Combine(Settings.Game, "Mods"));
                if (!stock)
                {
                    if (!Profiles(mo2).Contains(profile, Names)) throw new IOException("Укажи папку MO2 и выбери профиль.");
                    string modsRoot = Path.Combine(mo2, "mods");
                    var available = Directory.EnumerateDirectories(modsRoot).Where(dir => !IsLink(dir))
                        .ToDictionary(Path.GetFileName, dir => dir, Names);
                    var entries = File.ReadLines(Path.Combine(mo2, "profiles", profile, "modlist.txt")).Reverse().ToList();
                    foreach (var line in entries)
                    {
                        if (line.Length < 2 || (line[0] != '+' && line[0] != '-')) continue;
                        string folder;
                        if (!available.TryGetValue(line.Substring(1), out folder)) continue;
                        string metaFile = Path.Combine(folder, "meta.ini");
                        int nexusId = 0, fileId = 0;
                        if (File.Exists(metaFile) && !IsLink(metaFile))
                        {
                            foreach (var column in File.ReadLines(metaFile))
                            {
                                if (column.StartsWith("modid=", StringComparison.OrdinalIgnoreCase)) int.TryParse(column.Substring(6), out nexusId);
                                if (column.StartsWith("fileid=", StringComparison.OrdinalIgnoreCase)) int.TryParse(column.Substring(7), out fileId);
                            }
                        }
                        sources[folder + Path.DirectorySeparatorChar] = new ModMeta { Source = Path.GetFileName(folder), NexusId = nexusId, FileId = fileId };
                        if (line[0] == '+') AddOverlay(overlay, folder);
                        else
                        {
                            var asleep = new Dictionary<string, string>(Names);
                            AddOverlay(asleep, folder);
                            StageOverlay(asleep, sources, work, Disabled, plan, status);
                        }
                    }
                    AddOverlay(overlay, Path.Combine(mo2, "overwrite"));
                }
                StageOverlay(overlay, sources, work, Active, plan, status);
                if (plan.Count == 0) throw new IOException("В этой сборке не найдено модов SMAPI.");
                Commit(plan);
                return plan.Count;
            }
            finally { Cleanup(work); }
        }

        private static void StageOverlay(Dictionary<string, string> overlay, Dictionary<string, ModMeta> sources,
            string work, string destination, List<Transfer> plan, Action<string> status)
        {
            var manifests = overlay.Where(row => Names.Equals(Path.GetFileName(row.Key), "manifest.json"))
                .OrderByDescending(row => row.Key.Length).ToList();
            foreach (var row in manifests)
            {
                var mod = ReadManifest(row.Value);
                if (mod == null) continue;
                string prefix = Path.GetDirectoryName(row.Key);
                prefix = string.IsNullOrEmpty(prefix) ? "" : prefix + Path.DirectorySeparatorChar;
                string slot = Slot(mod);
                string stage = Path.Combine(work, slot);
                status("Импорт: " + mod.Name);
                var modFiles = overlay.Where(item => item.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var file in modFiles)
                {
                    Copy(file.Value, Path.Combine(stage, file.Key.Substring(prefix.Length)));
                    overlay.Remove(file.Key);
                }
                var source = sources.FirstOrDefault(pair => row.Value.StartsWith(pair.Key, StringComparison.OrdinalIgnoreCase)).Value;
                if (source != null) Json.Save(Path.Combine(stage, MetaName), source);
                plan.Add(new Transfer { From = stage, To = Path.Combine(destination, slot) });
            }
        }

        private static void ExtractZip(string archive, string root)
        {
            using (var zip = ZipFile.OpenRead(archive))
            {
                if (zip.Entries.Count > 150000) throw new IOException("В архиве слишком много файлов.");
                long sum = 0;
                foreach (var entry in zip.Entries)
                {
                    string raw = entry.FullName.Replace('\\', '/').TrimEnd('/');
                    var parts = ArchiveParts(raw);
                    if (parts.Any(part => Names.Equals(part, MetaName)) || parts[0] == "__MACOSX") continue;
                    string destination = Path.GetFullPath(Path.Combine(root, string.Join(Path.DirectorySeparatorChar.ToString(), parts)));
                    if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Архив содержит недопустимый путь.");
                    if (entry.Name.Length == 0) continue;
                    sum = checked(sum + entry.Length);
                    if (sum > 8L * 1024 * 1024 * 1024) throw new IOException("После распаковки архив превышает 8 ГБ.");
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    using (var input = entry.Open())
                    using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        input.CopyTo(output);
                }
            }
        }

        private static string[] ArchiveParts(string raw)
        {
            var parts = raw.Split('/');
            if (parts.Any(part => part.Length == 0 || part == "." || part == ".." || part.EndsWith(".") ||
                part.EndsWith(" ") || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])(?:\.|$)", RegexOptions.IgnoreCase)))
                throw new IOException("Архив содержит недопустимый путь.");
            return parts;
        }

        private static void Extract(string archive, string root)
        {
            byte[] header = new byte[8];
            using (var stream = File.OpenRead(archive)) stream.Read(header, 0, header.Length);
            if (header[0] == 'P' && header[1] == 'K')
            {
                ExtractZip(archive, root);
                return;
            }
            // The Windows libarchive utility supports 7z and RAR. Reject links and other special entries before extracting.
            if (!(header[0] == '7' && header[1] == 'z' && header[2] == 0xbc) &&
                !(header[0] == 'R' && header[1] == 'a' && header[2] == 'r' && header[3] == '!'))
                throw new IOException("Выбери архив ZIP, 7z или RAR с модом SMAPI.");
            string work = Path.GetDirectoryName(root);
            string input = "payload-" + Guid.NewGuid().ToString("N");
            File.Copy(archive, Path.Combine(work, input));
            var listing = RunTar("-tvf " + input, work);
            var entries = listing.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (entries.Length > 150000 || entries.Any(line => line[0] != '-' && line[0] != 'd' || line.Contains(" link to ")))
                throw new IOException("В архиве есть ссылки или недопустимые пути.");
            long sum = 0;
            foreach (var entry in entries)
            {
                var size = Regex.Match(entry, @"^\S+\s+\d+\s+\S+\s+\S+\s+(\d+)\s");
                long length;
                if (!size.Success || !long.TryParse(size.Groups[1].Value, out length))
                    throw new IOException("Не удалось проверить размер файлов в архиве.");
                sum = checked(sum + length);
                if (sum > 8L * 1024 * 1024 * 1024) throw new IOException("После распаковки архив превышает 8 ГБ.");
            }
            var paths = RunTar("-tf " + input, work).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (paths.Length != entries.Length) throw new IOException("Не удалось проверить пути файлов в архиве.");
            foreach (string path in paths) ArchiveParts(path.Replace('\\', '/').TrimEnd('/'));
            RunTar("--no-same-owner --no-same-permissions -xf " + input + " -C " + Path.GetFileName(root), work);
        }

        private static string RunTar(string arguments, string work)
        {
            string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
            if (!File.Exists(exe)) throw new IOException("Для RAR и 7z нужен tar.exe из Windows 10/11. ZIP можно установить сразу.");
            var info = new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = work
            };
            using (var process = Process.Start(info))
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(180000))
                {
                    process.Kill();
                    throw new IOException("Распаковка заняла слишком много времени.");
                }
                if (process.ExitCode != 0)
                    throw new IOException("Windows не смогла распаковать архив. Попробуй ZIP или скачай файл заново.");
                stderr.GetAwaiter().GetResult();
                return stdout.GetAwaiter().GetResult();
            }
        }

        internal string Install(string archive, ModMeta meta, Mod chosen, Action<string> status)
        {
            string work = Work();
            try
            {
                var operations = new List<Transfer>();
                string added = PrepareInstall(archive, meta, chosen, work, null, operations, status);
                status("Применяю изменения…");
                Commit(operations);
                return added;
            }
            finally { Cleanup(work); }
        }

        private string PrepareInstall(string archive, ModMeta meta, Mod chosen, string work, string exact,
            List<Transfer> operations, Action<string> status)
        {
            string extracted = Path.Combine(work, "archive");
            Directory.CreateDirectory(extracted);
            {
                status("Распаковываю архив…");
                Extract(archive, extracted);
                var overlay = new Dictionary<string, string>(Names);
                AddOverlay(overlay, extracted);
                var files = overlay.Where(row => Names.Equals(Path.GetFileName(row.Key), "manifest.json"))
                    .Select(row => ReadManifest(row.Value)).Where(mod => mod != null).ToList();
                if (files.Count == 0) throw new IOException("В архиве нет корректного manifest.json для SMAPI.");
                if (files.GroupBy(mod => mod.Id, Names).Any(group => group.Count() > 1))
                    throw new IOException("В архиве несколько вариантов одного мода. Выбери нужную папку и упакуй её отдельно.");
                if (chosen != null && !files.Any(mod => Names.Equals(mod.Id, chosen.Id)))
                    throw new IOException("В архиве нет мода «" + chosen.Name + "». Выбери другой файл.");
                if (exact != null && (files.Count != 1 || !SameVersion(files[0].Version, exact)))
                    throw new IOException("Архив содержит несколько модов или другую версию. Проверь его вручную.");
                var plan = new List<Transfer>();
                StageOverlay(overlay, new Dictionary<string, ModMeta>(Names), work, Active, plan, status);
                var installed = ListMods().ToLookup(mod => mod.Id, Names);
                foreach (var item in plan)
                {
                    var incoming = ReadManifest(Path.Combine(item.From, "manifest.json"));
                    var matches = installed[incoming.Id].ToList();
                    var existing = chosen != null && Names.Equals(chosen.Id, incoming.Id) ? chosen :
                        matches.Count == 1 ? matches[0] : null;
                    if (matches.Count > 1 && existing == null)
                        throw new IOException("Есть несколько копий «" + incoming.Name + "». Открой нужную копию в списке обновлений.");
                    if (existing != null)
                    {
                        if (meta.NexusId > 0 && existing.NexusId > 0 && meta.NexusId != existing.NexusId)
                            throw new IOException("«" + existing.Name + "» установлена с другой страницы Nexus. Для ручной замены используй локальный архив.");
                        if (CompareVersion(incoming.Version, existing.Version) < 0)
                            throw new IOException("Архив содержит более старую версию «" + existing.Name + "». Для возврата используй резервную копию.");
                        item.To = existing.Path;
                        foreach (var config in Files(existing.Path).Where(file => Names.Equals(Path.GetFileName(file), "config.json")))
                            Copy(config, Path.Combine(item.From, Relative(existing.Path, config)));
                        string backup = Path.Combine(Backups, Path.GetFileName(existing.Path), DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 5));
                        operations.Add(new Transfer { From = item.To, To = backup });
                    }
                    var next = new ModMeta
                    {
                        Source = meta.Source,
                        NexusId = meta.NexusId > 0 ? meta.NexusId : existing == null ? 0 : existing.Meta.NexusId,
                        FileId = meta.FileId,
                        GitRepo = meta.GitRepo ?? (existing == null ? null : existing.Meta.GitRepo),
                        Asset = meta.Asset ?? (existing == null ? null : existing.Meta.Asset),
                        Category = existing == null ? null : existing.Meta.Category
                    };
                    Json.Save(Path.Combine(item.From, MetaName), next);
                    operations.Add(item);
                }
                return string.Join(", ", files.Select(mod => mod.Name).Take(4)) + (files.Count > 4 ? " и ещё " + (files.Count - 4) : "");
            }
        }

        internal string NewDownload() { return Path.Combine(Staging, "download-" + Guid.NewGuid().ToString("N")); }

        internal string LatestBackup(Mod mod)
        {
            string root = Path.Combine(Backups, Path.GetFileName(mod.Path));
            return Directory.Exists(root) ? Directory.EnumerateDirectories(root).Where(dir => !IsLink(dir))
                .OrderByDescending(dir => dir, StringComparer.Ordinal).FirstOrDefault() : null;
        }

        internal void Restore(Mod mod, string backup)
        {
            var plan = new List<Transfer>
            {
                new Transfer { From = mod.Path, To = Path.Combine(Backups, "Replaced", Path.GetFileName(mod.Path) + "-" + Guid.NewGuid().ToString("N")) },
                new Transfer { From = backup, To = mod.Path }
            };
            Commit(plan);
        }
    }
}
