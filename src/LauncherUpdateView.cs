using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Lug
{
    internal sealed class PendingNexusDownload
    {
        internal string BuildId, BuildName;
        internal Mod Mod;
        public override string ToString() { return BuildName; }
    }

    internal sealed partial class Window
    {
        private bool restartingLauncher;

        private string LauncherSource()
        {
            if (!string.IsNullOrWhiteSpace(store.Settings.LauncherUpdateSource)) return store.Settings.LauncherUpdateSource;
            var folder = new DirectoryInfo(Path.GetDirectoryName(Application.ExecutablePath));
            string nextToExe = Path.Combine(folder.FullName, LauncherUpdate.PackageName);
            if (File.Exists(nextToExe)) return nextToExe;
            if (folder.Parent != null && folder.Name.Equals("Stardew-Lug", StringComparison.OrdinalIgnoreCase))
            {
                string parent = Path.Combine(folder.Parent.FullName, LauncherUpdate.PackageName);
                if (File.Exists(parent)) return parent;
            }
            return null;
        }

        private bool ConfigureLauncherSource()
        {
            if (busy) return false;
            const string local = "ZIP на диске или в общей папке";
            const string github = "Публичные релизы автора на GitHub";
            const string automatic = "Искать ZIP рядом с приложением";
            string chosen;
            string existing = LauncherSource();
            using (var dialog = new ChoiceDialog("Источник обновлений Луга",
                existing == null ? "Выбери папку с архивом или репозиторий автора." : "Текущий источник: " + existing,
                new object[] { local, github, automatic }, "Продолжить"))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                chosen = (string)dialog.Selected;
            }
            string value = null;
            if (chosen == local)
            {
                using (var picker = new OpenFileDialog
                {
                    Title = "Архив обновления Луга", Filter = "Архив Луга (*.zip)|*.zip",
                    FileName = LauncherUpdate.PackageName, CheckFileExists = true
                })
                {
                    if (picker.ShowDialog(this) != DialogResult.OK) return false;
                    value = picker.FileName;
                }
            }
            else if (chosen == github)
            {
                string address = ReadName("Репозиторий автора", "Вставь адрес репозитория, которому доверяешь устанавливать новые версии EXE.");
                if (address == null) return false;
                value = Sources.GitRepo(address);
                if (value == null) { Status("Укажи адрес репозитория GitHub.", Theme.Red); return false; }
            }
            string prior = store.Settings.LauncherUpdateSource;
            try
            {
                store.Settings.LauncherUpdateSource = value;
                store.SaveSettings();
                string source = LauncherSource();
                Status(source == null ? "ZIP не найден рядом с приложением." : "Источник обновлений: " + source, Theme.Muted);
                return source != null;
            }
            catch (IOException error) { Status(error.Message, Theme.Red); }
            catch (UnauthorizedAccessException) { Status("Windows не разрешила сохранить источник обновлений.", Theme.Red); }
            store.Settings.LauncherUpdateSource = prior;
            return false;
        }

        private async void UpdateLauncher()
        {
            if (busy) return;
            string source = LauncherSource();
            if (source == null)
            {
                if (!ConfigureLauncherSource()) return;
                source = LauncherSource();
            }
            await Run("Проверяю версию Луга…", async () =>
            {
                string id = LauncherUpdate.CreateJob();
                string folder = LauncherUpdate.Job(id);
                try
                {
                    string candidate = LauncherUpdate.Candidate(folder);
                    Version version;
                    if (Path.IsPathRooted(source))
                    {
                        if (!File.Exists(source)) throw new IOException("Архив канала обновлений не найден. Проверь его путь в настройках.");
                        version = await Task.Run(() => LauncherUpdate.ReadPackage(source, true, candidate));
                        if (version <= LauncherUpdate.Current)
                        {
                            Status("Установленная версия Луга " + LauncherUpdate.Format(LauncherUpdate.Current) + " уже актуальна для этого источника.", Theme.Green);
                            return;
                        }
                    }
                    else
                    {
                        var release = await sources.LatestLauncher(source);
                        version = LauncherUpdate.NormalizeTag(release.Origin.Tag);
                        if (version <= LauncherUpdate.Current)
                        {
                            Status("Установлена актуальная версия Луга.", Theme.Green);
                            return;
                        }
                        if (!ConfirmLauncherUpdate(source, version)) { Status("Обновление отменено."); return; }
                        string package = Path.Combine(folder, "download");
                        await sources.Download(release.Url, package, Progress, LauncherUpdate.SizeLimit);
                        await Task.Run(() =>
                        {
                            release.Verify(package);
                            if (LauncherUpdate.ReadPackage(package, release.Name.EndsWith(".zip"), candidate) != version)
                                throw new IOException("Версия EXE не совпадает с номером релиза GitHub.");
                        });
                    }
                    if (Path.IsPathRooted(source) && !ConfirmLauncherUpdate(source, version))
                    { Status("Обновление отменено."); return; }
                    Status("Готовлю перезапуск Луга…");
                    await Task.Run(() =>
                    {
                        using (var parent = Process.GetCurrentProcess()) LauncherUpdate.Stage(id, Application.ExecutablePath, parent);
                    });
                    LauncherUpdate.StartWorker(id);
                    restartingLauncher = true;
                }
                finally { if (!restartingLauncher) LauncherUpdate.Discard(id); }
            });
            if (restartingLauncher) Application.Exit();
        }

        private bool ConfirmLauncherUpdate(string source, Version version)
        {
            return ShowText("Обновление Луга", "Версия " + LauncherUpdate.Format(LauncherUpdate.Current) + " → " + LauncherUpdate.Format(version),
                "Источник: " + source + "\r\n\r\nПосле установки Луг перезапустится. При проблеме с запуском откроется предыдущая версия.",
                "Установить и открыть");
        }
    }
}
