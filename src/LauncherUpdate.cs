using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Lug
{
    public sealed class LauncherUpdateTicket
    {
        public string Target { get; set; }
        public string OldHash { get; set; }
        public string NewHash { get; set; }
        public int Parent { get; set; }
        public long Started { get; set; }
    }

    internal static class LauncherUpdate
    {
        internal const string PackageName = "Stardew-Lug.zip";
        internal const string ExeName = "StardewLug.exe";
        internal const string ApplyFlag = "--lug-update";
        internal const string ReadyFlag = "--lug-update-ready";
        internal const string FailedFlag = "--lug-update-failed";
        private const string WorkerName = "LugUpdate.exe";
        internal const long SizeLimit = 32L * 1024 * 1024;
        private static string Jobs { get { return Path.Combine(Path.GetTempPath(), "StardewLug-Updates"); } }

        internal static Version Current { get { return Assembly.GetExecutingAssembly().GetName().Version; } }

        internal static string Format(Version version)
        {
            if (version.Revision > 0) return version.ToString();
            if (version.Build > 0) return version.ToString(3);
            return version.ToString(2);
        }

        internal static Version NormalizeTag(string value)
        {
            Version version;
            if (!Version.TryParse((value ?? "").TrimStart('v', 'V'), out version) || version.Major < 1)
                throw new IOException("Выпуск должен иметь номер версии Луга, например v1.5.1.");
            return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
        }

        internal static Version ReadVersion(string file)
        {
            try
            {
                if (new FileInfo(file).Length > SizeLimit) throw new IOException("EXE слишком большой для обновления Луга.");
                var identity = AssemblyName.GetAssemblyName(file);
                if (!string.Equals(identity.Name, "StardewLug", StringComparison.OrdinalIgnoreCase) || identity.Version == null)
                    throw new IOException("Выбранный EXE не относится к приложению «Луг».");
                return identity.Version;
            }
            catch (BadImageFormatException) { throw new IOException("В пакете нет корректного Windows-приложения «Луг»."); }
            catch (FileLoadException) { throw new IOException("Не удалось прочитать информацию о версии Луга."); }
        }

        internal static Version ReadPackage(string package, bool zip, string candidate)
        {
            if (new FileInfo(package).Length > SizeLimit) throw new IOException("Пакет обновления превышает 32 МБ.");
            if (zip)
            {
                try
                {
                    using (var archive = ZipFile.OpenRead(package))
                    {
                        var hits = archive.Entries.Where(entry => string.Equals(entry.FullName, ExeName, StringComparison.OrdinalIgnoreCase)).ToList();
                        if (hits.Count != 1 || hits[0].Length > SizeLimit ||
                            archive.Entries.Any(entry => string.Equals(entry.FullName, Store.PackMarker, StringComparison.OrdinalIgnoreCase)))
                            throw new IOException("Выбери ZIP с приложением «Луг». В корне архива должен быть " + ExeName + ".");
                        using (var source = hits[0].Open())
                        using (var output = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write))
                        {
                            var buffer = new byte[64 * 1024];
                            int n;
                            long size = 0;
                            while ((n = source.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                size += n;
                                if (size > SizeLimit) throw new IOException("EXE слишком большой для обновления.");
                                output.Write(buffer, 0, n);
                            }
                            if (size != hits[0].Length) throw new IOException("Пакет обновления повреждён.");
                        }
                    }
                }
                catch (InvalidDataException) { throw new IOException("Не удалось открыть ZIP обновления."); }
            }
            else File.Copy(package, candidate);
            return ReadVersion(candidate);
        }

        internal static string CreateJob()
        {
            string id = Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(Job(id));
            return id;
        }

        internal static string Job(string id)
        {
            Guid valid;
            if (id == null || !Guid.TryParseExact(id, "N", out valid)) throw new IOException("Некорректный идентификатор обновления.");
            if (Directory.Exists(Jobs) && Store.IsLink(Jobs)) throw new IOException("Временная папка обновления является ссылкой.");
            string path = Path.Combine(Jobs, valid.ToString("N"));
            if (Directory.Exists(path) && Store.IsLink(path)) throw new IOException("Папка обновления является ссылкой.");
            return path;
        }

        private static string Ticket(string folder) { return Path.Combine(folder, "ticket.json"); }
        internal static string Candidate(string folder) { return Path.Combine(folder, "next.exe"); }
        private static string Ready(string folder) { return Path.Combine(folder, "ready"); }
        private static string Failed(string folder) { return Path.Combine(folder, "failure.txt"); }
        private static string Done(string folder) { return Path.Combine(folder, "done"); }

        private static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var input = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "");
        }

        internal static void Stage(string id, string target, Process parent)
        {
            string folder = Job(id);
            target = Path.GetFullPath(target);
            string original = Hash(target);
            if (ReadVersion(Candidate(folder)) <= ReadVersion(target)) throw new IOException("Пакет не новее установленного Луга.");
            File.Copy(target, Path.Combine(folder, WorkerName));
            Json.Save(Ticket(folder), new LauncherUpdateTicket
            {
                Target = target, OldHash = original, NewHash = Hash(Candidate(folder)),
                Parent = parent.Id, Started = parent.StartTime.ToUniversalTime().Ticks
            });
        }

        internal static void StartWorker(string id)
        {
            string folder = Job(id);
            using (Process.Start(new ProcessStartInfo(Path.Combine(folder, WorkerName), ApplyFlag + " " + id)
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = folder
            })) { }
        }

        internal static void MarkReady(string id, string executable)
        {
            string folder = Job(id);
            var ticket = Json.Load<LauncherUpdateTicket>(Ticket(folder));
            if (!string.Equals(ticket.Target, Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(ticket.NewHash, Hash(executable), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Версия при перезапуске не совпадает с пакетом обновления.");
            File.WriteAllText(Ready(folder), "");
        }

        internal static string FailureNote(string id)
        {
            string path = Failed(Job(id));
            return File.Exists(path) ? File.ReadAllText(path) : "Не удалось завершить обновление. Открыта предыдущая версия.";
        }

        private static void WaitForParent(LauncherUpdateTicket ticket)
        {
            Process parent;
            try { parent = Process.GetProcessById(ticket.Parent); }
            catch (ArgumentException) { return; }
            using (parent)
            {
                try
                {
                    if (parent.StartTime.ToUniversalTime().Ticks != ticket.Started) return;
                }
                catch (InvalidOperationException) { return; }
                if (!parent.WaitForExit(30000)) throw new IOException("Закрой старую версию Луга и повтори обновление.");
            }
        }

        internal static void Apply(string id, string worker, Action<string> tell)
        {
            string folder = Job(id);
            string backup = null, next = null, target = null;
            bool changed = false, parentClosed = false;
            LauncherUpdateTicket ticket = null;
            try
            {
                if (!string.Equals(Path.GetFullPath(worker), Path.Combine(folder, WorkerName), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Обновление запущено из другой папки.");
                ticket = Json.Load<LauncherUpdateTicket>(Ticket(folder));
                if (string.IsNullOrEmpty(ticket.Target) || ticket.Parent <= 0 ||
                    !string.Equals(ticket.OldHash, Hash(worker), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Пакет обновления повреждён.");
                WaitForParent(ticket);
                parentClosed = true;
                target = Path.GetFullPath(ticket.Target);
                if (Store.IsLink(target) || !string.Equals(ticket.OldHash, Hash(target), StringComparison.OrdinalIgnoreCase) ||
                    ReadVersion(Candidate(folder)) <= ReadVersion(target) ||
                    !string.Equals(ticket.NewHash, Hash(Candidate(folder)), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Проверка версии или файлов обновления не прошла.");

                next = target + "." + id + ".next";
                backup = target + "." + id + ".bak";
                File.Copy(Candidate(folder), next);
                File.Replace(next, target, backup, true);
                changed = true;
                using (var process = Restart(target, ReadyFlag, id))
                {
                    var watch = Stopwatch.StartNew();
                    while (!File.Exists(Ready(folder)) && watch.ElapsedMilliseconds < 30000 && !process.WaitForExit(100)) { }
                    if (!File.Exists(Ready(folder)))
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                            if (!process.WaitForExit(5000)) throw new IOException("Новая версия ещё занята. Закрой её и восстанови EXE из копии " + backup + ".");
                        }
                        throw new IOException("Новая версия не завершила запуск.");
                    }
                }

                changed = false;
                try
                {
                    File.Copy(backup, target + ".previous", true);
                    File.Delete(backup);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            catch (Exception problem)
            {
                try
                {
                    if (changed) File.Replace(backup, target, null, true);
                    else if (backup != null && File.Exists(backup) && string.Equals(ticket.OldHash, Hash(backup), StringComparison.OrdinalIgnoreCase))
                    {
                        if (!File.Exists(target)) File.Move(backup, target);
                        else if (string.Equals(ticket.NewHash, Hash(target), StringComparison.OrdinalIgnoreCase))
                            File.Replace(backup, target, null, true);
                    }
                    if (!parentClosed || target == null) { tell(problem.Message); return; }
                    File.WriteAllText(Failed(folder), "Луг не обновился. " + problem.Message);
                    if (string.Equals(ticket.OldHash, Hash(target), StringComparison.OrdinalIgnoreCase))
                    {
                        using (Restart(target, FailedFlag, id)) { }
                    }
                    else tell(problem.Message);
                }
                catch (Exception recovery)
                {
                    tell(problem.Message + "\r\n" + recovery.Message +
                        (backup != null && File.Exists(backup) ? "\r\nРезервная копия: " + backup : ""));
                }
            }
            finally
            {
                DeleteFile(next);
                DeleteFile(Candidate(folder));
                try { File.WriteAllText(Done(folder), ""); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static Process Restart(string target, string flag, string id)
        {
            return Process.Start(new ProcessStartInfo(target, flag + " " + id)
            {
                WorkingDirectory = Path.GetDirectoryName(target), UseShellExecute = false
            });
        }

        private static void DeleteFile(string file)
        {
            if (file == null) return;
            try { File.Delete(file); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal static void Discard(string id)
        {
            var folder = Job(id);
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal static void Prune(string keep)
        {
            if (!Directory.Exists(Jobs) || Store.IsLink(Jobs)) return;
            foreach (var folder in Directory.EnumerateDirectories(Jobs))
            {
                string id = Path.GetFileName(folder);
                Guid guid;
                if (id == keep || !Guid.TryParseExact(id, "N", out guid) || Store.IsLink(folder)) continue;
                if (File.Exists(Done(folder)) && File.GetLastWriteTimeUtc(Done(folder)) < DateTime.UtcNow.AddMinutes(-2)) Discard(id);
            }
        }
    }

    internal sealed partial class Sources
    {
        internal async System.Threading.Tasks.Task<RemoteAsset> LatestLauncher(string repository)
        {
            var repo = GitRepo(repository);
            if (repo == null) throw new IOException("Укажи репозиторий автора Луга на GitHub.");
            var response = await Request("https://api.github.com/repos/" + repo + "/releases/latest").ConfigureAwait(false);
            var release = ReleaseAssets(response, repo).Where(asset => asset.Name == LauncherUpdate.ExeName ||
                asset.Name == LauncherUpdate.PackageName).OrderBy(asset => asset.Name != LauncherUpdate.ExeName).ToList();
            var candidate = release.FirstOrDefault(asset => Regex.IsMatch(asset.Digest ?? "", @"\Asha256:[a-f0-9]{64}\z", RegexOptions.IgnoreCase));
            if (candidate == null) throw new IOException("В последнем релизе нужен EXE или ZIP Луга с контрольной суммой SHA-256.");
            return candidate;
        }
    }
}
