using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Lug
{
    internal sealed partial class Window
    {
        private string categoryFilter;
        private bool onlineBuilds;
        private string onlineTerm = "";
        private int onlinePage;
        private List<PublicBuild> publicResults;

        private static string ModCategory(Mod mod)
        {
            return string.IsNullOrWhiteSpace(mod.Meta.Category) ? Store.Uncategorized : mod.Meta.Category;
        }

        private List<string> Categories()
        {
            return store.Preferences().Categories.Concat(mods.Select(ModCategory)).Where(item => item != Store.Uncategorized)
                .Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private Panel CategoryRail()
        {
            var rail = Theme.Surface(Theme.Rail);
            rail.Dock = DockStyle.Fill;
            rail.Margin = new Padding(0, 0, 11, 0);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(9, 0, 6, 7) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));
            rail.Controls.Add(layout);
            var heading = Theme.Label("Категории", 9.5f, Theme.Muted, true);
            heading.Dock = DockStyle.Fill;
            heading.Padding = new Padding(7, 0, 0, 0);
            layout.Controls.Add(heading, 0, 0);
            var list = new ListBox
            {
                Dock = DockStyle.Fill, AutoSize = false, IntegralHeight = false, HorizontalScrollbar = false,
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 35, BorderStyle = BorderStyle.None, BackColor = Theme.Rail,
                ForeColor = Theme.Text, Font = Theme.Font(9.25f, true), Margin = Padding.Empty, AccessibleName = "Категории модов"
            };
            layout.Controls.Add(list, 0, 1);
            var entries = new[] { (string)null, Store.Uncategorized }.Concat(Categories()).ToList();
            var counts = mods.ToLookup(ModCategory, StringComparer.CurrentCultureIgnoreCase);
            foreach (string entry in entries) list.Items.Add(entry ?? "Все моды");
            list.DrawItem += (sender, args) =>
            {
                if (args.Index < 0) return;
                string name = (string)list.Items[args.Index];
                bool selected = (args.State & DrawItemState.Selected) != 0;
                using (var brush = new SolidBrush(selected ? Theme.Selected : Theme.Rail))
                    args.Graphics.FillRectangle(brush, args.Bounds);
                string count = (args.Index == 0 ? mods.Count : counts[entries[args.Index]].Count()).ToString();
                var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
                var bounds = args.Bounds;
                int countWidth = TextRenderer.MeasureText(args.Graphics, count, list.Font).Width;
                int countLeft = bounds.Right - countWidth - 5;
                TextRenderer.DrawText(args.Graphics, name, list.Font, new Rectangle(bounds.X + 6, bounds.Y,
                    Math.Max(1, countLeft - bounds.X - 8), bounds.Height), selected ? Theme.Green : Theme.Text, flags | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(args.Graphics, count, list.Font, new Rectangle(countLeft, bounds.Y,
                    countWidth, bounds.Height), Theme.Muted, flags | TextFormatFlags.Right);
                if (list.Focused && (args.State & DrawItemState.Focus) != 0) args.DrawFocusRectangle();
            };
            list.HandleCreated += (sender, args) => Native.SetWindowTheme(list.Handle, "DarkMode_Explorer", null);
            list.SelectedIndex = Math.Max(0, entries.FindIndex(item => string.Equals(item, categoryFilter, StringComparison.CurrentCultureIgnoreCase)));
            list.SelectedIndexChanged += (sender, args) =>
            {
                if (list.SelectedIndex < 0 || grid == null || grid.IsDisposed) return;
                categoryFilter = entries[list.SelectedIndex];
                FillMods(false, stateFilter);
            };
            var add = Theme.Button("+  Категория", NewCategory);
            add.AccessibleName = "Добавить категорию";
            add.AutoSize = false;
            add.MinimumSize = Size.Empty;
            add.Margin = Padding.Empty;
            add.Dock = DockStyle.Fill;
            layout.Controls.Add(add, 0, 2);
            return rail;
        }

        private string ReadName(string title, string prompt)
        {
            using (var input = new TextDialog(title, prompt, false))
                return input.ShowDialog(this) == DialogResult.OK ? input.Value.Trim() : null;
        }

        private async void NewCategory()
        {
            if (busy) return;
            string category = ReadName("Новая категория", "Например, «Интерфейс», «Ферма» или «Переводы».");
            if (category == null) return;
            await Run("Добавляю категорию…", () =>
            {
                store.AddCategory(category);
                ShowView("mods");
                Status("Категория добавлена.", Theme.Green);
                return Task.FromResult(0);
            });
        }

        private async void AssignCategory(Mod selected)
        {
            if (busy) return;
            const string create = "Новая категория…";
            var rowSelection = view == "mods" ? grid.SelectedRows.Cast<DataGridViewRow>().Select(row => row.Tag as Mod)
                .Where(item => item != null).ToList() : new List<Mod>();
            if (!rowSelection.Contains(selected)) rowSelection = new List<Mod> { selected };
            string category;
            using (var choose = new ChoiceDialog("Категория", rowSelection.Count == 1 ? selected.Name : "Выбрано модов: " + rowSelection.Count,
                new[] { Store.Uncategorized }.Concat(Categories()).Concat(new[] { create }).ToArray(), "Назначить"))
            {
                if (choose.ShowDialog(this) != DialogResult.OK) return;
                category = (string)choose.Selected;
            }
            if (category == create) category = ReadName("Новая категория", "Название категории");
            if (string.IsNullOrWhiteSpace(category)) return;
            if (category == Store.Uncategorized) category = null;
            await Run("Назначаю категорию…", async () =>
            {
                await Task.Run(() =>
                {
                    foreach (var mod in rowSelection) store.SetCategory(mod, category);
                });
                await RefreshMods();
                Status("Категория сохранена.", Theme.Green);
            });
        }

        private async void ModPresets()
        {
            if (busy || mods.Count == 0) return;
            const string save = "Сохранить текущий набор…";
            object result;
            using (var choose = new ChoiceDialog("Пресеты модов", "Пресет запоминает, какие моды включены в этой сборке.",
                new object[] { save }.Concat(store.Preferences().Presets.Cast<object>()).ToArray(), "Продолжить"))
            {
                if (choose.ShowDialog(this) != DialogResult.OK) return;
                result = choose.Selected;
            }
            if (Equals(result, save))
            {
                string name = ReadName("Новый пресет", "Название пресета модов");
                if (name == null) return;
                await Run("Сохраняю пресет…", () =>
                {
                    store.SavePreset(name, mods);
                    Status("Пресет сохранён.", Theme.Green);
                    return Task.FromResult(0);
                });
                return;
            }
            await Run("Переключаю моды…", async () =>
            {
                GameClosed();
                var preset = (ModPreset)result;
                var enabled = Store.PresetStates(preset);
                var changes = mods.Where(mod => enabled(mod) != mod.Enabled).ToList();
                if (changes.Count == 0) { Status("Этот набор уже активен."); return; }
                var body = string.Join("\r\n", changes.Select(mod => (enabled(mod) ? "+ Включить " : "− Выключить ") + mod.Name)) +
                    ReviewText(store.Review(mods, enabled));
                if (!ShowText("Предпросмотр пресета", preset.Name + " · Изменений: " + changes.Count, body, "Применить пресет"))
                { Status("Пресет не изменён."); return; }
                GameClosed();
                int changed = await Task.Run(() => store.ApplyPreset(preset));
                await RefreshMods();
                Status("Пресет «" + result + "» применён. Изменено модов: " + changed, Theme.Green);
            });
        }

        private async Task SwitchBuild(string id)
        {
            store.SelectBuild(id);
            mods.Clear();
            updatesChecked = false;
            selectedSlot = null;
            filter = "";
            stateFilter = 0;
            categoryFilter = null;
            sideTab = "about";
            view = "mods";
            await RefreshMods();
        }

        private void RenderBuilds()
        {
            title.Text = "Сборки";
            subhead.Text = onlineBuilds ? "Найди сборку и скачай её прямо в Луге." : "Активная сборка · " + store.BuildName();
            var toolbar = Actions();
            intro.Controls.Add(toolbar);
            toolbar.Controls.Add(Theme.Button("Мои сборки", () => { onlineBuilds = false; ShowView("builds"); }, !onlineBuilds));
            toolbar.Controls.Add(Theme.Button("Публичные сборки", () =>
            {
                onlineBuilds = true;
                ShowView("builds");
                if (publicResults == null && !busy) SearchPublicBuilds("", 0);
            }, onlineBuilds));
            if (!onlineBuilds)
            {
                toolbar.Controls.Add(Theme.Button("Новая / копия", NewBuild));
                toolbar.Controls.Add(Theme.Button("Импорт ZIP", OpenPack));
                var publish = Theme.Button("Опубликовать сборку", PublishBuild);
                publish.Enabled = mods.Count > 0;
                toolbar.Controls.Add(publish);
                DrawLocalBuilds();
            }
            else DrawPublicBuilds();
        }

        private TableLayoutPanel BuildListLayout(bool search)
        {
            var basePanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = search ? 2 : 1, ColumnCount = 1, Margin = Padding.Empty };
            if (search) basePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            basePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            center.Controls.Add(basePanel);
            return basePanel;
        }

        private void DrawLocalBuilds()
        {
            var basePanel = BuildListLayout(false);
            var split = Split(basePanel, 0);
            var table = new ModGrid(store.Settings.CompactRows);
            table.AddColumn("Сборка", 70, 180);
            table.AddColumn("Состояние", 30, 120);
            split.Controls.Add(table, 0, 0);
            foreach (var profile in store.Builds())
            {
                int index = table.Rows.Add(profile.Name, profile.Id == store.SelectedBuild ? "Активная" : "");
                table.Rows[index].Tag = profile;
                table.Rows[index].Cells[1].Style.ForeColor = Theme.Green;
            }
            Action draw = () =>
            {
                var profile = table.CurrentRow == null ? null : table.CurrentRow.Tag as BuildProfile;
                if (profile == null) return;
                bool active = profile.Id == store.SelectedBuild;
                var panel = Side(profile.Name, active ? mods.Count + " модов · " + mods.Count(mod => mod.Enabled) + " включено" : "", "");
                if (active)
                {
                    SideAction(panel, "Открыть моды", () => ShowView("mods"), true);
                    SideAction(panel, "Переименовать", RenameBuild);
                    if (mods.Count > 0) SideAction(panel, "Поделиться ZIP", SharePack);
                    if (!string.IsNullOrEmpty(store.Preferences().PublishRepository))
                        SideAction(panel, "Сменить репозиторий сборки", ChangePackRepository);
                    if (store.Snapshots().Count > 0) SideAction(panel, "Снимки сборки", RestoreBuildSnapshot);
                }
                else SideAction(panel, "Выбрать сборку", () => ActivateBuild(profile), true);
                if (profile.Origin != null)
                    SideText(panel, "Версия GitHub: " + profile.Origin.Tag, 9, Theme.Muted);
            };
            table.CurrentCellChanged += (sender, args) => draw();
            var current = table.Rows.Cast<DataGridViewRow>().First(row => ((BuildProfile)row.Tag).Id == store.SelectedBuild);
            table.CurrentCell = current.Cells[0];
            draw();
        }

        private async void ActivateBuild(BuildProfile profile)
        {
            await Run("Открываю сборку…", async () =>
            {
                await SwitchBuild(profile.Id);
                Status("Активная сборка: " + profile.Name, Theme.Green);
            });
        }

        private async void NewBuild()
        {
            if (busy) return;
            var options = new[] { "Пустая сборка", "Копия текущей сборки" };
            bool clone;
            using (var choose = new ChoiceDialog("Новая сборка", "Копия включает моды, категории, пресеты и настройки модов.", options, "Продолжить"))
            {
                if (choose.ShowDialog(this) != DialogResult.OK) return;
                clone = Equals(choose.Selected, options[1]);
            }
            string name = ReadName("Название сборки", "Введи название новой сборки.");
            if (name == null) return;
            await Run("Создаю сборку…", async () =>
            {
                if (clone) GameClosed();
                string id = await Task.Run(() => store.CreateBuild(name, clone, Progress));
                await SwitchBuild(id);
                Status("Сборка создана.", Theme.Green);
            });
        }

        private async void RenameBuild()
        {
            if (busy) return;
            string name = ReadName("Переименовать сборку", "Новое название");
            if (name == null) return;
            await Run("Сохраняю название…", () =>
            {
                store.RenameBuild(name);
                ShowView("builds");
                Status("Название сохранено.", Theme.Green);
                return Task.FromResult(0);
            });
        }

        private void DrawPublicBuilds()
        {
            var basePanel = BuildListLayout(true);
            var toolbar = Actions();
            basePanel.Controls.Add(toolbar, 0, 0);
            var input = Input(onlineTerm, "Название сборки или ссылка");
            input.Width = 335;
            input.Margin = new Padding(0, 5, 8, 0);
            toolbar.Controls.Add(input);
            Action search = () =>
            {
                string value = input.Text.Trim();
                if (Sources.GitRepo(value) != null) FetchPublicBuild(value, "");
                else SearchPublicBuilds(value, 0);
            };
            toolbar.Controls.Add(Theme.Button("Найти / открыть ссылку", search, true));
            toolbar.Controls.Add(Theme.Button("Все публикации", () => SearchPublicBuilds("", 0)));
            if (onlinePage > 0) toolbar.Controls.Add(Theme.Button("Назад", () => SearchPublicBuilds(onlineTerm, onlinePage - 1)));
            if (publicResults != null && publicResults.Count == 24)
                toolbar.Controls.Add(Theme.Button("Дальше", () => SearchPublicBuilds(onlineTerm, onlinePage + 1)));
            input.KeyDown += (sender, args) => { if (args.KeyCode == Keys.Enter) { args.SuppressKeyPress = true; search(); } };
            var split = Split(basePanel, 1);
            var table = new ModGrid(store.Settings.CompactRows);
            table.AddColumn("Сборка", 50, 160);
            table.AddColumn("Автор / репозиторий", 50, 140);
            split.Controls.Add(table, 0, 0);
            foreach (var result in publicResults ?? Enumerable.Empty<PublicBuild>())
            {
                int index = table.Rows.Add(result.Name, result.Repo);
                table.Rows[index].Tag = result;
            }
            Action draw = () =>
            {
                var build = table.CurrentRow == null ? null : table.CurrentRow.Tag as PublicBuild;
                if (build == null)
                {
                    Side(publicResults == null ? "Публичные сборки" : "Ничего не найдено",
                        "Попробуй другое название или вставь ссылку на сборку.", "");
                    return;
                }
                var panel = Side(build.Name, build.Repo, "");
                if (!string.IsNullOrWhiteSpace(build.Description)) SideText(panel, build.Description);
                SideAction(panel, "Скачать сборку", () => FetchPublicBuild(build.Repo, build.Name), true);
                SideAction(panel, "Страница автора", () => Native.Open("https://github.com/" + build.Repo + "/releases"));
            };
            table.CurrentCellChanged += (sender, args) => draw();
            draw();
        }

        private async void SearchPublicBuilds(string term, int page)
        {
            await Run("Ищу публичные сборки…", async () =>
            {
                var results = await sources.DiscoverBuilds(term, page);
                if (results.Count == 0 && page > 0) { Status("Дальше сборок нет."); return; }
                onlineTerm = term;
                onlinePage = page;
                publicResults = results;
                ShowView("builds");
                Status("Найдено на странице: " + results.Count, Theme.Green);
            });
        }

        private async void FetchPublicBuild(string target, string name)
        {
            await Run("Проверяю сборку…", async () =>
            {
                var files = await sources.PublicBuildFiles(target);
                if (files.Count == 0) throw new IOException("В релизе нет архива .lugpack.zip.");
                RemoteAsset selected = files[0];
                if (files.Count > 1)
                {
                    using (var choose = new ChoiceDialog(name.Length == 0 ? "Публичная сборка" : name, "Выбери архив выпуска.",
                        files.Select(item => item.Name).ToArray(), "Скачать"))
                    {
                        if (choose.ShowDialog(this) != DialogResult.OK) { Status("Скачивание отменено."); return; }
                        selected = files.First(item => item.Name == (string)choose.Selected);
                    }
                }
                string download = store.NewDownload();
                try
                {
                    await sources.Download(selected.Url, download, Progress);
                    await Task.Run(() => selected.Verify(download));
                    await InstallProfileFrom(download, selected.Origin);
                }
                finally { if (File.Exists(download)) File.Delete(download); }
            });
        }

        private async void ManageSmapi()
        {
            if (busy) return;
            if (!Store.IsGame(store.Settings.Game) && !ChooseGame()) return;
            await Run("Проверяю последнюю версию SMAPI…", async () =>
            {
                GameClosed();
                var latest = await sources.SmapiRelease();
                knownSmapi = latest;
                string installed = store.InstalledSmapi();
                if (installed.Length > 0 && Store.CompareVersion(installed, latest.Version) >= 0)
                {
                    Status("SMAPI " + installed + " уже актуален.", Theme.Green);
                    return;
                }
                string download = store.NewDownload();
                try
                {
                    await sources.Download(latest.Url, download, Progress);
                    await Task.Run(() => latest.Verify(download));
                    GameClosed();
                    await Task.Run(() => store.InstallSmapi(download, latest.Version, latest.MinimumGame, Progress));
                    mods.Clear();
                    updatesChecked = false;
                    await RefreshMods();
                    Status("SMAPI " + latest.Version + (installed.Length == 0 ? " установлен." : " обновлён."), Theme.Green);
                }
                finally { if (File.Exists(download)) File.Delete(download); }
            });
        }
    }
}
