using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Lug
{
    internal sealed partial class Window
    {
        private string ReadPackRepository(bool firstTime)
        {
            if (firstTime && !ShowText("Публикация сборки", "Подключи GitHub один раз",
                "Создай публичный репозиторий для этой сборки и добавь README.\r\n" +
                "Добавь тему " + Sources.BuildTopic + " в разделе About. По ней Луг найдёт сборку.\r\n\r\n" +
                "В настройках GitHub создай fine-grained personal access token для этого репозитория с правом Contents: Read and write.\r\n\r\n" +
                "https://github.com/new\r\n" +
                "https://github.com/settings/personal-access-tokens/new", "Ввести репозиторий"))
                return null;
            string input = ReadName("Репозиторий сборки", "Вставь ссылку на публичный репозиторий сборки.");
            if (input == null) return null;
            string repo = Sources.GitRepo(input);
            if (repo == null) Status("Укажи ссылку на репозиторий GitHub.", Theme.Red);
            return repo;
        }

        private string ReadPublishKey(bool allowClear)
        {
            using (var input = new TextDialog("Токен публикации сборок", allowClear
                ? "Вставь новый токен GitHub. Пустое поле удалит сохранённый токен."
                : "Вставь токен GitHub с правом Contents: Read and write для репозитория сборки.", true))
            {
                if (input.ShowDialog(this) != DialogResult.OK) return null;
                string key = input.Value.Trim();
                if ((allowClear && key.Length == 0) || Sources.IsPublishKey(key)) return key;
                Status("Проверь токен GitHub.", Theme.Red);
                return null;
            }
        }

        private async void ChangePublishKey()
        {
            if (busy) return;
            string key = ReadPublishKey(true);
            if (key == null) return;
            await Run("Сохраняю настройки…", () =>
            {
                SavePublishKey(key);
                Status(key.Length == 0 ? "Токен удалён." : "Токен сохранён.", Theme.Green);
                return Task.FromResult(0);
            });
        }

        private void SavePublishKey(string key)
        {
            string prior = store.Settings.PublishSecret;
            try
            {
                store.Settings.SetPublishKey(key);
                store.SaveSettings();
            }
            catch
            {
                store.Settings.PublishSecret = prior;
                throw;
            }
        }

        private async void ChangePackRepository()
        {
            if (busy) return;
            string repo = ReadPackRepository(false);
            if (repo == null) return;
            string key = store.Settings.PublishKey();
            if (key.Length == 0) key = ReadPublishKey(false);
            if (key == null) return;
            await Run("Проверяю репозиторий…", async () =>
            {
                string repository = await sources.CheckPackRepository(repo, key);
                store.SavePublishRepository(repository);
                SavePublishKey(key);
                Status("Репозиторий сборки сохранён.", Theme.Green);
            });
        }

        private async void PublishBuild()
        {
            await Run("Проверяю активную сборку…", async () =>
            {
                string name = store.BuildName();
                if (await Task.Run(() => store.ListMods().Count) == 0)
                {
                    string message = "Добавь хотя бы один мод в сборку «" + name + "» через вкладку «Моды» и попробуй снова.";
                    Status(message, Theme.Gold);
                    MessageBox.Show(this, message, "Сборка пуста", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                string repo = store.Preferences().PublishRepository ?? ReadPackRepository(true);
                if (repo == null) { Status("Публикация отменена."); return; }
                string key = store.Settings.PublishKey();
                if (key.Length == 0) key = ReadPublishKey(false);
                if (key == null) { Status("Публикация отменена."); return; }
                var options = new object[] { "Без настроек модов", "Включить config.json" };
                bool configs;
                using (var choice = new ChoiceDialog("Опубликовать сборку",
                    "«" + name + "» станет доступна всем в " + repo + ". Перед публикацией проверь, можно ли делиться файлами модов.",
                    options, "Опубликовать"))
                {
                    if (choice.ShowDialog(this) != DialogResult.OK) { Status("Публикация отменена."); return; }
                    configs = Equals(choice.Selected, options[1]);
                }
                GameClosed();
                Status("Проверяю репозиторий…");
                string repository = await sources.CheckPackRepository(repo, key);
                store.SavePublishRepository(repository);
                if (key != store.Settings.PublishKey()) SavePublishKey(key);
                string path = store.NewDownload();
                string link;
                try
                {
                    await Task.Run(() => store.ExportPublicPack(path, configs, Progress));
                    link = await sources.PublishPack(repository, name, path, key, Progress);
                }
                finally { if (File.Exists(path)) File.Delete(path); }
                publicResults = null;
                Status("Сборка опубликована.", Theme.Green);
                if (ShowText("Сборка опубликована", name,
                    "По этой ссылке сборка уже скачивается в «Публичных сборках». В поиске она появится после индексации GitHub.\r\n\r\n" + link,
                    "Скопировать ссылку"))
                {
                    try { Clipboard.SetText(link); }
                    catch (ExternalException) { Status("Не удалось скопировать ссылку.", Theme.Red); }
                }
            });
        }
    }
}
