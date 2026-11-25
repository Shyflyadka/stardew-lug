using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Lug
{
    public sealed class ModPreset
    {
        public string Name { get; set; }
        public Dictionary<string, bool> States { get; set; }
        public override string ToString() { return Name; }
    }

    public sealed class BuildPreferences
    {
        public string Name { get; set; }
        public List<string> Categories { get; set; }
        public List<ModPreset> Presets { get; set; }
        public AssetOrigin Origin { get; set; }
        public string PublishRepository { get; set; }
    }

    internal sealed class BuildProfile
    {
        internal string Id, Name;
        internal AssetOrigin Origin;
        public override string ToString() { return Name; }
    }

    internal sealed partial class Store
    {
        private const string PreferencesFile = "build-settings.json";
        internal const string Uncategorized = "Без категории";
        private string BuildShelf { get { return Path.Combine(Root, "Profiles"); } }
        private string BuildRoot
        {
            get { return string.IsNullOrEmpty(Settings.SelectedBuild) ? Root : Path.Combine(BuildShelf, Settings.SelectedBuild); }
        }

        internal void EnsureBuildFolders()
        {
            foreach (var folder in new[] { BuildRoot, Active, Disabled, Backups })
            {
                Directory.CreateDirectory(folder);
                if (IsLink(folder)) throw new IOException("Папка сборки является ссылкой.");
            }
        }

        internal List<BuildProfile> Builds()
        {
            var main = PreferencesAt(Root);
            var all = new List<BuildProfile> { new BuildProfile { Id = "", Name = main.Name ?? "Основная сборка", Origin = main.Origin } };
            foreach (var path in Directory.EnumerateDirectories(BuildShelf))
            {
                string id = Path.GetFileName(path);
                Guid guid;
                if (!Guid.TryParseExact(id, "N", out guid) || IsLink(path)) continue;
                var preferences = PreferencesAt(path);
                if (!string.IsNullOrEmpty(preferences.Name)) all.Add(new BuildProfile { Id = id, Name = preferences.Name, Origin = preferences.Origin });
            }
            return all.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static BuildPreferences PreferencesAt(string root)
        {
            var value = Json.Load<BuildPreferences>(Path.Combine(root, PreferencesFile)) ?? new BuildPreferences();
            value.Categories = value.Categories ?? new List<string>();
            value.Presets = value.Presets ?? new List<ModPreset>();
            return value;
        }

        internal BuildPreferences Preferences() { return PreferencesAt(BuildRoot); }
        internal string BuildName() { return Preferences().Name ?? "Основная сборка"; }
        internal string SelectedBuild { get { return Settings.SelectedBuild ?? ""; } }

        internal void SelectBuild(string id)
        {
            if (!Builds().Any(build => build.Id == id)) throw new IOException("Сборка не найдена.");
            Settings.SelectedBuild = id;
            EnsureBuildFolders();
            SaveSettings();
        }

        internal static string CleanName(string text)
        {
            return new string((text ?? "").Trim().Where(ch => !char.IsControl(ch)).Take(64).ToArray()).Trim();
        }

        internal string CreateBuild(string name, bool clone, Action<string> status)
        {
            name = CleanName(name);
            if (name.Length == 0 || Builds().Any(build => Names.Equals(build.Name, name)))
                throw new IOException("Введи новое имя сборки.");
            var stage = Work();
            try
            {
                var prefs = clone ? Preferences() : new BuildPreferences();
                prefs.Name = name;
                prefs.Origin = null;
                prefs.PublishRepository = null;
                foreach (var folder in new[] { "Mods", "Disabled" })
                {
                    var destination = Path.Combine(stage, folder);
                    Directory.CreateDirectory(destination);
                    if (!clone) continue;
                    status("Копирую файлы сборки…");
                    var root = Path.Combine(BuildRoot, folder);
                    foreach (var source in Files(root)) Copy(source, Path.Combine(destination, Relative(root, source)));
                }
                Json.Save(Path.Combine(stage, PreferencesFile), prefs);
                return PublishLocalProfile(stage);
            }
            finally { Cleanup(stage); }
        }

        private string PublishLocalProfile(string path)
        {
            var id = Guid.NewGuid().ToString("N");
            Directory.Move(path, Path.Combine(BuildShelf, id));
            return id;
        }

        internal void RenameBuild(string name)
        {
            name = CleanName(name);
            if (name.Length == 0 || Builds().Any(build => build.Id != SelectedBuild && Names.Equals(build.Name, name)))
                throw new IOException("Введи уникальное имя сборки.");
            var prefs = Preferences();
            prefs.Name = name;
            Json.Save(Path.Combine(BuildRoot, PreferencesFile), prefs);
        }

        internal void SavePublishRepository(string repo)
        {
            foreach (var build in Builds().Where(build => build.Id != SelectedBuild))
            {
                string root = build.Id.Length == 0 ? Root : Path.Combine(BuildShelf, build.Id);
                if (Names.Equals(PreferencesAt(root).PublishRepository, repo))
                    throw new IOException("Этот репозиторий уже связан с другой сборкой. Для публикации выбери другой.");
            }
            var prefs = Preferences();
            prefs.PublishRepository = repo;
            Json.Save(Path.Combine(BuildRoot, PreferencesFile), prefs);
        }

        internal void AddCategory(string category)
        {
            category = CleanName(category);
            if (category.Length == 0 || Names.Equals(category, Uncategorized)) throw new IOException("Введи имя категории.");
            var prefs = Preferences();
            if (prefs.Categories.Contains(category, Names)) return;
            prefs.Categories.Add(category);
            Json.Save(Path.Combine(BuildRoot, PreferencesFile), prefs);
        }

        internal void SetCategory(Mod mod, string category)
        {
            if (!string.IsNullOrEmpty(category)) AddCategory(category);
            mod.Meta.Category = string.IsNullOrEmpty(category) ? null : CleanName(category);
            Json.Save(Path.Combine(mod.Path, MetaName), mod.Meta);
        }

        internal void SavePreset(string name, IEnumerable<Mod> mods)
        {
            name = CleanName(name);
            if (name.Length == 0) throw new IOException("Введи имя пресета.");
            var prefs = Preferences();
            if (prefs.Presets.Any(item => Names.Equals(item.Name, name)))
                throw new IOException("Такое имя пресета уже используется.");
            prefs.Presets.Add(new ModPreset
            {
                Name = name,
                States = mods.ToDictionary(item => Path.GetFileName(item.Path), item => item.Enabled, Names)
            });
            Json.Save(Path.Combine(BuildRoot, PreferencesFile), prefs);
        }

        internal int ApplyPreset(ModPreset preset)
        {
            var moves = new List<Transfer>();
            var enabled = PresetStates(preset);
            foreach (var mod in ListMods())
            {
                string slot = Path.GetFileName(mod.Path);
                bool active = enabled(mod);
                if (active == mod.Enabled) continue;
                moves.Add(new Transfer { From = mod.Path, To = Path.Combine(active ? Active : Disabled, slot) });
            }
            if (moves.Count > 0) Commit(moves);
            return moves.Count;
        }
    }
}
