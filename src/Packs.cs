using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Lug
{
    public sealed class PackEntry
    {
        public string Folder { get; set; }
        public string Id { get; set; }
        public string Version { get; set; }
        public bool Enabled { get; set; }
        public ModMeta Source { get; set; }
    }

    public sealed class PackManifest
    {
        public int Format { get; set; }
        public string Name { get; set; }
        public string GameVersion { get; set; }
        public string SmapiVersion { get; set; }
        public List<PackEntry> Mods { get; set; }
        public List<string> Categories { get; set; }
        public List<ModPreset> Presets { get; set; }
    }

    internal sealed partial class Store
    {
        internal const string PackMarker = "lug-pack.json";
        private const string PackFiles = "packages";
        private const int MaxPackMods = 5000;
        private const long MaxPackIndex = 4 * 1024 * 1024;

        internal void ExportPack(string destination, string launcher, bool configs, Action<string> progress)
        {
            destination = Path.GetFullPath(destination);
            if (destination.StartsWith(Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Сохрани архив вне рабочей папки Луга.");
            WritePack(destination, launcher, null, configs, progress);
        }

        internal void ExportPublicPack(string destination, bool configs, Action<string> progress)
        {
            WritePack(destination, null, BuildName(), configs, progress);
        }

        private void WritePack(string destination, string launcher, string publicName, bool configs, Action<string> progress)
        {
            var mods = ListMods();
            if (mods.Count == 0) throw new IOException("В сборке пока нет модов.");
            if (mods.Count > MaxPackMods) throw new IOException("Сборка слишком большая для формата Луга.");
            string filename = Path.GetFileName(destination);
            string name = Regex.Replace(filename, @"(?:\.lugpack)?\.zip$", "", RegexOptions.IgnoreCase);
            var index = new PackManifest
            {
                Format = 1, Name = publicName ?? name, GameVersion = GameVersion("Stardew Valley.dll"),
                SmapiVersion = GameVersion("StardewModdingAPI.dll"), Mods = new List<PackEntry>()
            };
            var prefs = Preferences();
            if (!string.IsNullOrWhiteSpace(prefs.Name)) index.Name = prefs.Name;
            index.Categories = prefs.Categories;
            var keys = mods.Select((mod, i) => new { Slot = Path.GetFileName(mod.Path), Key = PackSlot(i) })
                .ToDictionary(row => row.Slot, row => row.Key, Names);
            index.Presets = MapPresets(prefs.Presets, keys);
            string temp = Path.Combine(Path.GetDirectoryName(destination), "lug-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                int files = launcher == null ? 1 : 3;
                long total = launcher == null ? 0 : new FileInfo(launcher).Length;
                using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
                {
                    if (launcher != null) zip.CreateEntryFromFile(launcher, "StardewLug.exe", CompressionLevel.Fastest);
                    for (int i = 0; i < mods.Count; i++)
                    {
                        var mod = mods[i];
                        string folder = PackSlot(i);
                        index.Mods.Add(new PackEntry { Folder = folder, Id = mod.Id, Version = mod.Version,
                            Enabled = mod.Enabled, Source = ExportSource(mod.Meta) });
                        if (i == 0 || (i + 1) % 10 == 0 || i + 1 == mods.Count)
                            progress("Сохраняю сборку: " + (i + 1) + " из " + mods.Count);
                        foreach (string file in Files(mod.Path))
                        {
                            string leaf = Path.GetFileName(file);
                            if (Names.Equals(leaf, MetaName) || (!configs && Names.Equals(leaf, "config.json"))) continue;
                            var relative = Relative(mod.Path, file).Replace('\\', '/');
                            ArchiveParts(relative);
                            total = checked(total + new FileInfo(file).Length);
                            if (++files > 150000 || total > 8L * 1024 * 1024 * 1024 - MaxPackIndex)
                                throw new IOException("Сборка превышает лимит формата: 150 000 файлов или 8 ГБ после распаковки.");
                            string suffix = Path.GetExtension(file).ToLowerInvariant();
                            var compression = suffix == ".png" || suffix == ".jpg" || suffix == ".zip" || suffix == ".ogg" ?
                                CompressionLevel.NoCompression : CompressionLevel.Fastest;
                            zip.CreateEntryFromFile(file, PackFiles + "/" + folder + "/" + relative, compression);
                        }
                    }
                    string metadata = Json.Serializer().Serialize(index);
                    if (Encoding.UTF8.GetByteCount(metadata) > MaxPackIndex) throw new IOException("Список модов слишком большой.");
                    WritePackText(zip, PackMarker, metadata);
                    if (launcher != null)
                        WritePackText(zip, "README.txt", "Распакуй весь архив в одну папку и открой StardewLug.exe. " +
                            "Луг предложит установить сборку. Если Луг уже установлен, выбери этот ZIP через «Импорт сборки». " +
                            "Для запуска нужна Stardew Valley. Луг может установить SMAPI сам.");
                }
                if (File.Exists(destination)) File.Replace(temp, destination, null);
                else File.Move(temp, destination);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        private static ModMeta ExportSource(ModMeta source)
        {
            return new ModMeta { NexusId = source.NexusId, FileId = source.FileId,
                GitRepo = Sources.GitRepo(source.GitRepo), Asset = source.Asset == null ? null : Path.GetFileName(source.Asset),
                Category = CleanName(source.Category) };
        }

        private static string PackSlot(int index) { return "mod-" + (index + 1).ToString("D4"); }

        private static List<ModPreset> MapPresets(IEnumerable<ModPreset> presets, Dictionary<string, string> keys)
        {
            return (presets ?? Enumerable.Empty<ModPreset>()).Where(item => item != null && item.States != null && CleanName(item.Name).Length > 0)
                .GroupBy(item => CleanName(item.Name), Names).Take(100).Select(group => new ModPreset
                {
                    Name = group.Key, States = group.First().States.Where(state => keys.ContainsKey(state.Key))
                        .GroupBy(state => keys[state.Key], Names).ToDictionary(row => row.Key, row => row.First().Value, Names)
                }).ToList();
        }

        private static void WritePackText(ZipArchive zip, string filename, string text)
        {
            using (var writer = new StreamWriter(zip.CreateEntry(filename).Open(), new UTF8Encoding(true))) writer.Write(text);
        }

        internal static PackManifest ReadPack(string source)
        {
            string json;
            if (Directory.Exists(source))
            {
                string file = Path.Combine(source, PackMarker);
                if (!File.Exists(file) || IsLink(source) || IsLink(file) || new FileInfo(file).Length > MaxPackIndex)
                    throw new IOException("В папке нет корректного списка модов сборки.");
                json = File.ReadAllText(file);
            }
            else
            {
                using (var zip = ZipFile.OpenRead(source))
                {
                    var entries = zip.Entries.Where(entry => Names.Equals(entry.FullName, PackMarker)).ToList();
                    if (entries.Count != 1 || entries[0].Length > MaxPackIndex)
                        throw new IOException("В архиве нет корректного списка модов Луга. Одиночный мод можно добавить через «Установить мод».");
                    using (var reader = new StreamReader(entries[0].Open())) json = reader.ReadToEnd();
                }
            }
            var pack = Json.Serializer().Deserialize<PackManifest>(json);
            if (pack == null || pack.Format != 1 || pack.Mods == null || pack.Mods.Count < 1 || pack.Mods.Count > MaxPackMods ||
                pack.Mods.Any(entry => entry == null || string.IsNullOrWhiteSpace(entry.Id) ||
                    !Regex.IsMatch(entry.Folder ?? "", @"^mod-[0-9]{4}$")) ||
                pack.Mods.Select(entry => entry.Folder).Distinct(Names).Count() != pack.Mods.Count)
                throw new IOException("Список модов повреждён или создан в более новой версии Луга.");
            return pack;
        }

        internal static List<Mod> PreviewPack(string source, PackManifest pack)
        {
            if (Directory.Exists(source))
                return pack.Mods.Select(entry =>
                {
                    string packages = Path.Combine(source, PackFiles);
                    string folder = Path.Combine(packages, entry.Folder);
                    string manifest = Path.Combine(folder, "manifest.json");
                    if (!Directory.Exists(packages) || IsLink(packages) || !Directory.Exists(folder) || IsLink(folder) ||
                        !File.Exists(manifest) || IsLink(manifest))
                        throw new IOException("Отсутствует manifest.json для " + entry.Id + ".");
                    return ValidatePackMod(entry, ReadManifest(manifest, readMetadata: false));
                }).ToList();
            using (var zip = ZipFile.OpenRead(source))
            {
                var manifests = zip.Entries.Where(entry => entry.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                    .ToLookup(entry => entry.FullName.Replace('\\', '/'), Names);
                return pack.Mods.Select(entry =>
                {
                    string path = PackFiles + "/" + entry.Folder + "/manifest.json";
                    var files = manifests[path].ToList();
                    if (files.Count != 1 || files[0].Length > MaxPackIndex)
                        throw new IOException("В архиве нет корректного manifest.json для " + entry.Id + ".");
                    using (var reader = new StreamReader(files[0].Open()))
                        return ValidatePackMod(entry, ReadManifestText(reader.ReadToEnd(), entry.Folder, entry.Source));
                }).ToList();
            }
        }

        private static Mod ValidatePackMod(PackEntry entry, Mod mod)
        {
            if (mod == null || !Names.Equals(mod.Id, entry.Id) || mod.Version != entry.Version)
                throw new IOException("Список сборки и файлы мода «" + entry.Id + "» не совпадают.");
            mod.Enabled = entry.Enabled;
            return mod;
        }

        internal string ImportProfile(string source, Action<string> progress, AssetOrigin origin = null)
        {
            string work = Work();
            string profile = Path.Combine(work, "profile");
            string stagedMods = Path.Combine(profile, "Mods");
            string stagedDisabled = Path.Combine(profile, "Disabled");
            Directory.CreateDirectory(stagedMods);
            Directory.CreateDirectory(stagedDisabled);
            try
            {
                var pack = ReadPack(source);
                bool direct = Directory.Exists(source);
                if (!direct)
                {
                    string extracted = Path.Combine(work, "unpacked");
                    Directory.CreateDirectory(extracted);
                    progress("Распаковываю сборку…");
                    ExtractZip(source, extracted);
                    pack = ReadPack(extracted);
                    source = extracted;
                }
                string packages = Path.Combine(source, PackFiles);
                if (!Directory.Exists(packages) || IsLink(packages)) throw new IOException("В сборке отсутствуют папки модов.");
                var keys = new Dictionary<string, string>(Names);
                for (int i = 0; i < pack.Mods.Count; i++)
                {
                    var entry = pack.Mods[i];
                    string folder = Path.Combine(packages, entry.Folder);
                    string manifest = Path.Combine(folder, "manifest.json");
                    if (!Directory.Exists(folder) || IsLink(folder) || !File.Exists(manifest) || IsLink(manifest))
                        throw new IOException("В сборке отсутствует manifest.json для " + entry.Id + ".");
                    var mod = ValidatePackMod(entry, ReadManifest(manifest, readMetadata: false));
                    string slot = Slot(mod);
                    keys.Add(entry.Folder, slot);
                    string destination = Path.Combine(entry.Enabled ? stagedMods : stagedDisabled, slot);
                    if (i == 0 || (i + 1) % 10 == 0 || i + 1 == pack.Mods.Count)
                        progress("Устанавливаю сборку: " + (i + 1) + " из " + pack.Mods.Count);
                    // Extracted ZIP folders are on the same volume and can be moved without a second copy.
                    if (!direct) Directory.Move(folder, destination);
                    else foreach (string file in Files(folder))
                    {
                        if (Names.Equals(Path.GetFileName(file), MetaName)) continue;
                        Copy(file, Path.Combine(destination, Relative(folder, file)));
                    }
                    if (entry.Source != null) Json.Save(Path.Combine(destination, MetaName), ExportSource(entry.Source));
                }
                var buildNames = new HashSet<string>(Builds().Select(build => build.Name), Names);
                string basis = CleanName(pack.Name);
                if (basis.Length == 0) basis = "Импортированная сборка";
                string name = basis;
                for (int i = 2; buildNames.Contains(name); i++) name = basis + " (" + i + ")";
                Json.Save(Path.Combine(profile, PreferencesFile), new BuildPreferences
                {
                    Name = name,
                    Categories = (pack.Categories ?? new List<string>()).Select(CleanName).Where(category => category.Length > 0)
                        .Distinct(Names).Take(200).ToList(),
                    Presets = MapPresets(pack.Presets, keys),
                    Origin = origin
                });
                return PublishLocalProfile(profile);
            }
            finally { Cleanup(work); }
        }

        private void SwapPack(string mods, string disabled, IEnumerable<Transfer> additional = null)
        {
            string backup = Path.Combine(Backups, "Builds", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 5));
            var plan = new List<Transfer>
            {
                new Transfer { From = Active, To = Path.Combine(backup, "Mods") },
                new Transfer { From = Disabled, To = Path.Combine(backup, "Disabled") },
                new Transfer { From = mods, To = Active },
                new Transfer { From = disabled, To = Disabled }
            };
            if (additional != null) plan.AddRange(additional);
            Commit(plan);
        }

        internal string PreviousPack()
        {
            var root = Path.Combine(Backups, "Builds");
            return Directory.Exists(root) && !IsLink(root) ? Directory.EnumerateDirectories(root)
                .Where(dir => !IsLink(dir) && Directory.Exists(Path.Combine(dir, "Mods")) && Directory.Exists(Path.Combine(dir, "Disabled")))
                .OrderByDescending(dir => dir, StringComparer.Ordinal).FirstOrDefault() : null;
        }

        internal void RestorePack(string backup)
        {
            SwapPack(Path.Combine(backup, "Mods"), Path.Combine(backup, "Disabled"));
        }
    }
}
