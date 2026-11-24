using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Lug
{
    internal sealed class BuildNotice
    {
        internal BuildProfile Profile;
        internal RemoteAsset Asset;
        public override string ToString() { return Profile.Name + "   " + Profile.Origin.Tag + " → " + Asset.Origin.Tag; }
    }

    internal sealed partial class Sources
    {
        internal async Task<string> DependencyPage(string id)
        {
            var response = await Request("https://smapi.io/api/v4.0.0/mods", new
            {
                mods = new[] { new { id = id } }, includeExtendedMetadata = true
            }).ConfigureAwait(false);
            var row = Json.List(response).FirstOrDefault(item => string.Equals(Json.Str(item, "id"), id, StringComparison.OrdinalIgnoreCase));
            var metadata = Json.Get(row, "metadata");
            int nexus = Json.Num(metadata, "nexusID");
            if (nexus > 0) return Page(nexus);
            string repo = GitRepo(Json.Str(metadata, "gitHubRepo"));
            if (repo != null) return "https://github.com/" + repo + "/releases";
            Uri url;
            string address = Json.Str(Json.Get(metadata, "main"), "url");
            return Uri.TryCreate(address, UriKind.Absolute, out url) && url.Scheme == "https" ? url.AbsoluteUri : null;
        }
    }

    internal sealed partial class Window
    {
        private RemoteAsset knownSmapi;
        private readonly List<BuildNotice> buildNotices = new List<BuildNotice>();
        private int updateProbe;

        private bool SmapiUpdateAvailable
        {
            get
            {
                var installed = store.InstalledSmapi();
                return knownSmapi != null && installed.Length > 0 && Store.CompareVersion(installed, knownSmapi.Version) < 0;
            }
        }

        internal static bool SameReleaseAsset(AssetOrigin left, AssetOrigin right)
        {
            var ignoreCase = StringComparison.OrdinalIgnoreCase;
            return string.Equals(left.Repository, right.Repository, ignoreCase) &&
                (left.OnlyPackage && right.OnlyPackage || string.Equals(left.Asset, right.Asset, ignoreCase));
        }

        private void RefreshUpdateIndicators()
        {
            int count = mods.Count(UpdateAvailable) + buildNotices.Count + (SmapiUpdateAvailable ? 1 : 0);
            nav["updates"].Text = "Обновления" + (count > 0 ? "  ·  " + count : "");
        }

        private static BuildProfile LatestTracked(IEnumerable<BuildProfile> profiles)
        {
            return profiles.Aggregate((prior, next) => next.Origin.IsNewerThan(prior.Origin) ? next : prior);
        }

        private async Task<int> CheckRemoteVersions()
        {
            int token = ++updateProbe;
            int errors = 0;
            var updates = new List<BuildNotice>();
            var failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Task<RemoteAsset> smapiTask = sources.SmapiRelease();
            var tracked = store.Builds().Where(profile => profile.Origin != null && Sources.GitRepo(profile.Origin.Repository) != null &&
                !string.IsNullOrWhiteSpace(profile.Origin.Asset)).ToList();
            foreach (var repo in tracked.GroupBy(profile => profile.Origin.Repository, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var releases = await sources.PublicBuildFiles(repo.Key);
                    foreach (var files in repo.GroupBy(profile => profile.Origin.OnlyPackage ? "" : profile.Origin.Asset, StringComparer.OrdinalIgnoreCase))
                    {
                        var previous = LatestTracked(files);
                        var next = releases.FirstOrDefault(item => SameReleaseAsset(item.Origin, previous.Origin));
                        if (next != null && next.Origin.IsNewerThan(previous.Origin))
                            updates.Add(new BuildNotice { Profile = previous, Asset = next });
                    }
                }
                catch (IOException) { errors++; failed.Add(repo.Key); }
            }
            RemoteAsset smapi = null;
            try { smapi = await smapiTask; }
            catch (IOException) { errors++; }
            if (IsDisposed || token != updateProbe) return errors;
            if (smapi != null) knownSmapi = smapi;
            var installed = store.Builds().Where(profile => profile.Origin != null).ToList();
            updates.AddRange(buildNotices.Where(notice => failed.Contains(notice.Asset.Origin.Repository)));
            buildNotices.Clear();
            buildNotices.AddRange(updates.Where(notice => !installed.Any(profile =>
                SameReleaseAsset(notice.Asset.Origin, profile.Origin) && !notice.Asset.Origin.IsNewerThan(profile.Origin))));
            RefreshUpdateIndicators();
            UpdateGameState();
            if (view == "updates" && !busy) ShowView(view);
            return errors;
        }

        private void UpdateActions(FlowLayoutPanel actions)
        {
            actions.Controls.Add(Theme.Button("Проверить обновления", CheckUpdates, true));
            actions.Controls.Add(Theme.Button("Обновить Луг", UpdateLauncher));
            var mass = Theme.Button("Обновить пакетом", BatchUpdate);
            mass.Enabled = mods.Any(UpdateAvailable);
            actions.Controls.Add(mass);
            actions.Controls.Add(Theme.Button(SmapiUpdateAvailable ? "SMAPI " + knownSmapi.Version : "Управление SMAPI", ManageSmapi));
            var builds = Theme.Button("Сборки" + (buildNotices.Count > 0 ? "  ·  " + buildNotices.Count : ""), ShowBuildNotices);
            actions.Controls.Add(builds);
            if (store.Snapshots().Count > 0) actions.Controls.Add(Theme.Button("Снимки сборки", RestoreBuildSnapshot));
        }

        private void ShowBuildNotices()
        {
            if (busy) return;
            if (buildNotices.Count == 0) { Status("Новых версий отслеживаемых сборок пока нет."); return; }
            using (var choose = new ChoiceDialog("Обновления сборок", "Новый выпуск добавится как отдельная сборка.",
                buildNotices.Cast<object>().ToArray(), "Скачать выпуск"))
            {
                if (choose.ShowDialog(this) != DialogResult.OK) return;
                var notice = (BuildNotice)choose.Selected;
                FetchPublicBuild(notice.Asset.Url.AbsoluteUri, notice.Profile.Name);
            }
        }

        private static string ReviewText(IEnumerable<BuildIssue> issues)
        {
            var lines = issues.Select(issue => issue.ToString()).Distinct().ToList();
            return lines.Count == 0 ? "\r\n\r\nЗамечаний к требованиям нет." :
                "\r\n\r\nЗамечания к требованиям · " + lines.Count + "\r\n" + string.Join("\r\n", lines);
        }

        private async void DiagnoseBuild()
        {
            if (busy) return;
            var issues = store.Review(mods, mod => mod.Enabled);
            var explained = new HashSet<Mod>(issues.Select(item => item.Mod));
            issues.AddRange(mods.Where(mod => Compatibility.HasWarning(mod) && !explained.Contains(mod)).Select(mod =>
                new BuildIssue { Mod = mod, Message = Compatibility.OnlineLabel(mod) }));
            bool hasLog = File.Exists(Path.Combine(SmapiLogFolder(), "SMAPI-latest.txt"));
            if (issues.Count == 0 && !hasLog) { Status("По проверенным требованиям замечаний нет.", Theme.Green); return; }
            var options = issues.Cast<object>().ToList();
            if (hasLog) options.Add("Пропущенные моды в журнале последнего запуска");
            BuildIssue issue;
            using (var picker = new ChoiceDialog("Проблемы сборки", "Выбери замечание, чтобы открыть решение.",
                options.ToArray(), "Открыть решение"))
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                issue = picker.Selected as BuildIssue;
            }
            if (issue == null) { ViewLastRun(); return; }
            if (issue.Smapi) { ManageSmapi(); return; }
            if (issue.Candidate != null)
            {
                var candidate = issue.Candidate;
                var warnings = store.Review(mods, mod => mod.Enabled || mod == candidate).Where(item => item.Mod == candidate);
                if (ShowText("Включить зависимость", candidate.Name, issue.Mod.Name + " использует этот мод." + ReviewText(warnings), "Включить"))
                    Toggle(candidate);
                return;
            }
            if (issue.Dependency == null) { FocusMod(issue.Mod); return; }
            var installed = mods.FirstOrDefault(mod => string.Equals(mod.Id, issue.Dependency, StringComparison.OrdinalIgnoreCase));
            if (installed != null && installed.NexusId > 0) { Native.Open(Sources.Page(installed.NexusId)); return; }
            var repo = installed == null ? null : Sources.GitRepo(installed.GitRepo);
            if (repo != null) { Native.Open("https://github.com/" + repo + "/releases"); return; }
            await Run("Ищу страницу зависимости…", async () =>
            {
                string href = await sources.DependencyPage(issue.Dependency);
                if (href == null)
                {
                    ShowText("Страница зависимости", issue.Mod.Name, issue.Message +
                        "\r\n\r\nВ базе SMAPI нет ссылки для этого ID. Скопируй ID и проверь требования на странице мода.");
                    FocusMod(issue.Mod);
                    return;
                }
                Native.Open(href);
                Status("Открыта страница зависимости.");
            });
        }

        private async void RestoreBuildSnapshot()
        {
            if (busy) return;
            var versions = store.Snapshots();
            if (versions.Count == 0) return;
            BuildSnapshot selected;
            using (var choose = new ChoiceDialog("Снимки сборки", "Вернутся файлы, версии, настройки и включённость модов.",
                versions.Cast<object>().ToArray(), "Восстановить"))
            {
                if (choose.ShowDialog(this) != DialogResult.OK) return;
                selected = (BuildSnapshot)choose.Selected;
            }
            await Run("Восстанавливаю сборку…", async () =>
            {
                GameClosed();
                await Task.Run(() => store.RestoreSnapshot(selected, Progress));
                mods.Clear();
                updatesChecked = false;
                await RefreshMods();
                Status("Сборка восстановлена.", Theme.Green);
            });
        }

        private async Task<BatchItem> PrepareBatchDownload(Mod mod, string key, bool premium)
        {
            int id = Sources.NexusId(mod.UpdateUrl);
            if (id == 0 && string.IsNullOrEmpty(mod.UpdateUrl)) id = mod.NexusId;
            Uri url;
            ModMeta metadata;
            if (id > 0)
            {
                if (key.Length == 0 || !premium) throw new IOException("Для пакетного скачивания с Nexus нужен подключённый Premium.");
                var files = (await sources.NexusFiles(id, key, mod)).Where(item => item.Recommended &&
                    Store.SameVersion(item.Version, mod.NewVersion) && (mod.Meta.FileId > 0 || item.Category == "MAIN")).ToList();
                if (files.Count != 1) throw new IOException("Несколько вариантов или нет точного файла обновления.");
                var file = files[0];
                url = await sources.NexusDownload(new NxmLink { ModId = id, FileId = file.Id }, key);
                metadata = new ModMeta { Source = "Nexus Mods", NexusId = id, FileId = file.Id };
            }
            else
            {
                string repo = Sources.GitRepo(mod.UpdateUrl);
                if (repo == null) throw new IOException("Источник требует ручного скачивания.");
                var files = (await sources.GitFiles(repo, mod.Meta.Asset, mod.NewVersion))
                    .Where(item => Store.SameVersion(item.Version, mod.NewVersion)).ToList();
                var file = files.FirstOrDefault(item => item.Recommended) ?? (files.Count == 1 ? files[0] : null);
                if (file == null || !Uri.TryCreate(file.AssetUrl, UriKind.Absolute, out url) || url.Host != "github.com" ||
                    !url.AbsolutePath.StartsWith("/" + repo + "/releases/download/", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Нужен ручной выбор архива GitHub.");
                metadata = new ModMeta { Source = "GitHub", GitRepo = repo, Asset = file.Name };
            }
            string path = store.NewDownload();
            try { await sources.Download(url, path, Progress); }
            catch { if (File.Exists(path)) File.Delete(path); throw; }
            return new BatchItem { Mod = mod, Archive = path, Metadata = metadata };
        }

        private async void BatchUpdate()
        {
            if (busy) return;
            var candidates = mods.Where(UpdateAvailable).ToList();
            if (candidates.Count == 0) return;
            string key = store.Settings.Key();
            bool premium = store.Settings.Premium;
            if (!ShowText("Пакетное обновление", "Перед применением обновлений Луг сохранит снимок сборки.",
                string.Join("\r\n", candidates.Select(mod => mod.Name + "   " + mod.Version + " → " + mod.NewVersion)) +
                "\r\n\r\nФайлы с неоднозначным вариантом останутся для ручного обновления.", "Начать обновление")) return;
            await Run("Подготавливаю обновления…", async () =>
            {
                GameClosed();
                var items = new List<BatchItem>();
                var skipped = new List<string>();
                try
                {
                    foreach (var mod in candidates)
                    {
                        Status("Проверяю " + mod.Name);
                        try { items.Add(await PrepareBatchDownload(mod, key, premium)); }
                        catch (IOException error) { skipped.Add(mod.Name + " · " + error.Message); }
                        catch (System.Net.Http.HttpRequestException) { skipped.Add(mod.Name + " · Скачивание прервалось."); }
                        catch (OperationCanceledException) { skipped.Add(mod.Name + " · Скачивание не ответило вовремя."); }
                    }
                    GameClosed();
                    int updated = await Task.Run(() => store.InstallBatch(items, skipped, Progress));
                    await RefreshMods();
                    Status("Обновлено: " + updated + " · Вручную: " + skipped.Count, skipped.Count > 0 ? Theme.Gold : Theme.Green);
                    if (skipped.Count > 0) ShowText("Итог обновления", "Установлено обновлений: " + updated,
                        "Проверь эти моды вручную.\r\n\r\n" + string.Join("\r\n", skipped));
                }
                finally
                {
                    foreach (var item in items) if (File.Exists(item.Archive)) File.Delete(item.Archive);
                }
            });
        }
    }
}
