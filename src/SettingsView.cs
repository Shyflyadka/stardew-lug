using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Lug
{
    internal sealed partial class Window
    {
        private void RenderSettings()
        {
            title.Text = "Настройки";
            subhead.Text = "Запуск игры, интерфейс, Nexus Mods и диагностика.";
            var settingsBoard = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty
            };
            settingsBoard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            settingsBoard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            settingsBoard.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            settingsBoard.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            center.Controls.Add(settingsBoard);

            var game = SettingsCard(settingsBoard, "Игра и запуск", 0, 0);
            string gamePath = store.Settings.Game;
            string smapi = store.InstalledSmapi();
            SettingsLine(game, string.IsNullOrWhiteSpace(gamePath) ? "Папка Stardew Valley не указана" : gamePath, Theme.Muted);
            SettingsButtons(game, Theme.Button("Выбрать папку игры", SetGame),
                Theme.Button(smapi.Length == 0 ? "Поставить SMAPI" : "Обновить SMAPI", ManageSmapi));
            SettingToggle(game, "Подтверждать запуск при замечаниях", () => !store.Settings.SkipLaunchWarnings,
                value => store.Settings.SkipLaunchWarnings = !value);
            SettingToggle(game, "Сворачивать Луг при запуске игры", () => store.Settings.MinimizeOnLaunch,
                value => store.Settings.MinimizeOnLaunch = value);

            var display = SettingsCard(settingsBoard, "Интерфейс", 1, 0);
            SettingToggle(display, "Анимации кнопок", () => !store.Settings.ReduceMotion, value =>
            {
                store.Settings.ReduceMotion = !value;
                MotionButton.AnimationsEnabled = value;
            });
            SettingToggle(display, "Компактные списки", () => store.Settings.CompactRows,
                value => store.Settings.CompactRows = value);
            SettingsLine(display, "Компактный режим вмещает больше строк на экране.", Theme.Muted);
            SettingsButtons(display, Theme.Button("Источник обновлений Луга", () => ConfigureLauncherSource()));

            var account = SettingsCard(settingsBoard, "Nexus Mods", 0, 1);
            bool linked = store.Settings.Key().Length > 0;
            SettingsLine(account, linked ? "Подключён " + store.Settings.NexusName + "  ·  " +
                (store.Settings.Premium ? "Premium" : "Бесплатный аккаунт") : "Аккаунт не подключён", linked ? Theme.Green : Theme.Muted);
            if (linked) SettingsButtons(account, Theme.Button("Сменить ключ", Connect),
                Theme.Button("Получить API-ключ", () => Native.Open(Sources.NexusAccount)), Theme.Button("Отключить", Disconnect));
            else SettingsButtons(account, Theme.Button("Подключить аккаунт", Connect),
                Theme.Button("Получить API-ключ", () => Native.Open(Sources.NexusAccount)));
            var handler = Native.IsHandler();
            SettingsButtons(account, Theme.Button(handler ? "Вернуть прежний обработчик" : "Принимать ссылки Nexus", Register));

            var files = SettingsCard(settingsBoard, "Файлы и диагностика", 1, 1);
            SettingsLine(files, "Активная сборка: " + store.BuildName(), Theme.Muted);
            SettingsButtons(files, Theme.Button("Папка модов", () => Native.Open(store.Active)),
                Theme.Button("Папка данных", () => Native.Open(store.Root)), Theme.Button("Импорт из MO2", OpenImport));
            SettingsButtons(files, Theme.Button("Последний запуск", ViewLastRun),
                Theme.Button("Папка логов SMAPI", OpenSmapiLogs));
            if (store.PreviousPack() != null)
                SettingsButtons(files, Theme.Button("Вернуть прежнюю сборку", RestorePreviousPack));
            foreach (var card in new[] { game, display, account, files })
            {
                card.RowCount++;
                card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            }
        }

        private static TableLayoutPanel SettingsCard(TableLayoutPanel board, string name, int column, int row)
        {
            var card = new TableLayoutPanel
            {
                ColumnCount = 1, RowCount = 0, Dock = DockStyle.Fill, AutoScroll = true,
                BackColor = Theme.Panel, Padding = new Padding(14, 9, 12, 8),
                Margin = new Padding(column == 0 ? 0 : 6, row == 0 ? 0 : 6, column == 0 ? 6 : 0, row == 0 ? 6 : 0)
            };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            board.Controls.Add(card, column, row);
            var heading = Theme.Label(name, 14, Theme.Text, true);
            AddSettingsRow(card, heading, 37);
            return card;
        }

        private static void AddSettingsRow(TableLayoutPanel card, Control item, int height)
        {
            int next = card.RowCount++;
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            item.Dock = DockStyle.Fill;
            card.Controls.Add(item, 0, next);
        }

        private static void SettingsLine(TableLayoutPanel card, string text, Color color)
        {
            var line = Theme.Label(text, 9, color);
            line.AccessibleDescription = text;
            AddSettingsRow(card, line, 27);
        }

        private static void SettingsButtons(TableLayoutPanel card, params Button[] buttons)
        {
            var actions = new FlowLayoutPanel { Margin = Padding.Empty, WrapContents = false, BackColor = Theme.Panel };
            foreach (var button in buttons) actions.Controls.Add(button);
            AddSettingsRow(card, actions, 42);
        }

        private void SettingToggle(TableLayoutPanel card, string text, Func<bool> read, Action<bool> write)
        {
            var toggle = new CheckBox
            {
                Text = text, Checked = read(), Font = Theme.Font(10), ForeColor = Theme.Text, Margin = Padding.Empty,
                BackColor = Theme.Panel, Cursor = Cursors.Hand, FlatStyle = FlatStyle.Flat
            };
            toggle.FlatAppearance.BorderColor = Theme.Line;
            toggle.FlatAppearance.CheckedBackColor = Theme.Selected;
            bool restoring = false;
            toggle.CheckedChanged += (sender, args) =>
            {
                if (restoring) return;
                bool previous = read();
                try
                {
                    write(toggle.Checked);
                    store.SaveSettings();
                    Status("Настройка сохранена.", Theme.Green);
                }
                catch (IOException error)
                {
                    write(previous);
                    restoring = true;
                    toggle.Checked = previous;
                    restoring = false;
                    Status(error.Message, Theme.Red);
                }
                catch (UnauthorizedAccessException)
                {
                    write(previous);
                    restoring = true;
                    toggle.Checked = previous;
                    restoring = false;
                    Status("Windows не разрешила сохранить настройки.", Theme.Red);
                }
            };
            AddSettingsRow(card, toggle, 34);
        }

        private static string SmapiLogFolder()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "ErrorLogs");
        }

        private void OpenSmapiLogs()
        {
            string folder = SmapiLogFolder();
            if (Directory.Exists(folder)) Native.Open(folder);
            else Status("Папка журналов появится после запуска игры через SMAPI.", Theme.Muted);
        }
    }
}
