using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Lug
{
    internal sealed class BuildIssue
    {
        internal Mod Mod, Candidate;
        internal string Message, Dependency;
        internal bool Smapi;
        public override string ToString() { return Mod.Name + "   —   " + Message; }
    }

    internal sealed class BuildSnapshot
    {
        internal string Path, Name;
        public override string ToString() { return Name; }
    }

    internal sealed class BatchItem
    {
        internal Mod Mod;
        internal ModMeta Metadata;
        internal string Archive;
    }

    internal sealed partial class Store
    {
        private static readonly Regex KnownVersion = new Regex(@"^v?\d+(?:\.\d+){0,3}(?:-[\w.-]+)?(?:\+.*)?\s*$", RegexOptions.IgnoreCase);

        internal static bool SameVersion(string left, string right)
        {
            return KnownVersion.IsMatch(left ?? "") && KnownVersion.IsMatch(right ?? "") && CompareVersion(left, right) == 0;
        }

        private static bool MeetsMinimum(string version, string required)
        {
            return string.IsNullOrWhiteSpace(required) || KnownVersion.IsMatch(version ?? "") &&
                KnownVersion.IsMatch(required) && CompareVersion(version, required) >= 0;
        }

        internal List<BuildIssue> Review(IEnumerable<Mod> inventory, Func<Mod, bool> isEnabled)
        {
            var all = inventory.ToList();
            var enabled = all.Where(isEnabled).ToLookup(mod => mod.Id, Names);
            var installed = all.ToLookup(mod => mod.Id, Names);
            string smapi = GameVersion("StardewModdingAPI.dll");
            string game = GameVersion("Stardew Valley.dll");
            var issues = new List<BuildIssue>();
            foreach (var mod in all.Where(isEnabled))
            {
                if (enabled[mod.Id].Count() > 1)
                    issues.Add(new BuildIssue { Mod = mod, Message = "Несколько включённых модов с ID " + mod.Id });
                if (!MeetsMinimum(smapi, mod.MinimumSmapi))
                    issues.Add(new BuildIssue { Mod = mod, Message = "Требуется SMAPI " + mod.MinimumSmapi + " или новее", Smapi = true });
                if (game.Length > 0 && !MeetsMinimum(game, mod.MinimumGame))
                    issues.Add(new BuildIssue { Mod = mod, Message = "Требуется Stardew Valley " + mod.MinimumGame + " или новее" });
                foreach (var dependency in mod.Dependencies.Distinct())
                {
                    string id = dependency.Item1;
                    var present = enabled[id].ToList();
                    if (present.Any(item => MeetsMinimum(item.Version, dependency.Item2))) continue;
                    var matching = installed[id].Where(item => MeetsMinimum(item.Version, dependency.Item2)).ToList();
                    var sample = installed[id].FirstOrDefault();
                    string minimum = string.IsNullOrEmpty(dependency.Item2) ? "" : " от " + dependency.Item2;
                    if (matching.Count == 1 && present.Count == 0)
                        issues.Add(new BuildIssue { Mod = mod, Candidate = matching[0], Message = "Зависимость выключена: " + matching[0].Name });
                    else if (matching.Count > 1 && present.Count == 0)
                        issues.Add(new BuildIssue { Mod = mod, Message = "Выключено несколько копий: " + matching[0].Name });
                    else
                        issues.Add(new BuildIssue { Mod = mod, Dependency = id,
                            Message = "Нужен " + (sample == null ? id : sample.Name) + minimum });
                }
            }
            return issues;
        }

        internal static Func<Mod, bool> PresetStates(ModPreset preset)
        {
            return mod =>
            {
                bool enabled;
                return preset.States != null && preset.States.TryGetValue(Path.GetFileName(mod.Path), out enabled) ? enabled : mod.Enabled;
            };
        }

        internal static string PreviewSwitch(IEnumerable<Mod> current, IEnumerable<Mod> incoming)
        {
            var now = current.ToLookup(mod => mod.Id, Names);
            var next = incoming.ToLookup(mod => mod.Id, Names);
            var lines = new List<string>();
            foreach (var id in now.Select(set => set.Key).Union(next.Select(set => set.Key), Names))
            {
                var old = now[id].ToList();
                var received = next[id].ToList();
                var name = (received.FirstOrDefault() ?? old[0]).Name;
                if (old.Count == 0) lines.Add("+ " + name + " " + Versions(received));
                else if (received.Count == 0) lines.Add("− " + name);
                else if (!string.Equals(Versions(old), Versions(received), StringComparison.OrdinalIgnoreCase))
                    lines.Add("↔ " + name + "   " + Versions(old) + " → " + Versions(received));
            }
            return string.Join("\r\n", lines);
        }

        private static string Versions(IEnumerable<Mod> mods)
        {
            return string.Join(", ", mods.Select(mod => mod.Version + (mod.Enabled ? " (вкл.)" : " (выкл.)")).OrderBy(item => item, Names));
        }

        private string SnapshotsRoot { get { return Path.Combine(Backups, "Snapshots"); } }

        internal List<BuildSnapshot> Snapshots()
        {
            if (!Directory.Exists(SnapshotsRoot) || IsLink(SnapshotsRoot)) return new List<BuildSnapshot>();
            return Directory.EnumerateDirectories(SnapshotsRoot).Where(path => !IsLink(path) &&
                File.Exists(Path.Combine(path, PreferencesFile)) && Directory.Exists(Path.Combine(path, "Mods")) &&
                Directory.Exists(Path.Combine(path, "Disabled"))).OrderByDescending(path => path, Names)
                .Select(path => new BuildSnapshot { Path = path,
                    Name = Directory.GetCreationTime(path).ToString("dd.MM.yyyy HH:mm:ss") + " · Перед обновлением" }).ToList();
        }

        private static void CopyBuildFolders(string source, string target, Action<string> progress)
        {
            foreach (var name in new[] { "Mods", "Disabled" })
            {
                string origin = Path.Combine(source, name);
                string output = Path.Combine(target, name);
                if (!Directory.Exists(origin) || IsLink(origin)) throw new IOException("Папка модов отсутствует или является ссылкой.");
                Directory.CreateDirectory(output);
                int fileCount = 0;
                foreach (var file in Files(origin, includeHidden: true))
                {
                    Copy(file, Path.Combine(output, Relative(origin, file)));
                    if (++fileCount % 100 == 0) progress("Копирую файлы снимка · " + fileCount);
                }
            }
        }

        internal void CreateSnapshot(Action<string> progress)
        {
            string work = Work();
            try
            {
                progress("Сохраняю снимок сборки…");
                CopyBuildFolders(BuildRoot, work, progress);
                Json.Save(Path.Combine(work, PreferencesFile), Preferences());
                Directory.CreateDirectory(SnapshotsRoot);
                Directory.Move(work, Path.Combine(SnapshotsRoot, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")));
            }
            finally { Cleanup(work); }
        }

        internal void RestoreSnapshot(BuildSnapshot snapshot, Action<string> progress)
        {
            if (!Snapshots().Any(item => Names.Equals(item.Path, snapshot.Path))) throw new IOException("Снимок не найден.");
            string work = Work();
            try
            {
                CopyBuildFolders(snapshot.Path, work, progress);
                var preferences = Path.Combine(BuildRoot, PreferencesFile);
                var copies = new List<Transfer>();
                if (File.Exists(preferences))
                    copies.Add(new Transfer { From = preferences, To = Path.Combine(Backups, "PreviousSettings",
                        Guid.NewGuid().ToString("N") + ".json"), IsFile = true });
                var staged = Path.Combine(work, PreferencesFile);
                Copy(Path.Combine(snapshot.Path, PreferencesFile), staged);
                copies.Add(new Transfer { From = staged, To = preferences, IsFile = true });
                progress("Возвращаю снимок сборки…");
                SwapPack(Path.Combine(work, "Mods"), Path.Combine(work, "Disabled"), copies);
            }
            finally { Cleanup(work); }
        }

        internal int InstallBatch(List<BatchItem> batch, List<string> skipped, Action<string> progress)
        {
            string work = Work();
            try
            {
                var changes = new List<Transfer>();
                int count = 0;
                for (int i = 0; i < batch.Count; i++)
                {
                    var item = batch[i];
                    var proposed = new List<Transfer>();
                    try
                    {
                        PrepareInstall(item.Archive, item.Metadata, item.Mod, Path.Combine(work, i.ToString()),
                            item.Mod.NewVersion, proposed, progress);
                        if (proposed.Any(candidate => changes.Any(prior => Names.Equals(prior.To, candidate.To))))
                            throw new IOException("Другое обновление уже меняет этот мод.");
                        changes.AddRange(proposed);
                        count++;
                    }
                    catch (IOException error) { skipped.Add(item.Mod.Name + " · " + error.Message); }
                    catch (InvalidDataException error) { skipped.Add(item.Mod.Name + " · " + error.Message); }
                }
                if (count == 0) return 0;
                CreateSnapshot(progress);
                progress("Применяю обновления…");
                Commit(changes);
                return count;
            }
            finally { Cleanup(work); }
        }
    }
}
