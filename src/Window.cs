using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Lug
{
    internal static class Theme
    {
        internal static readonly Color Bg = Color.FromArgb(24, 26, 27);
        internal static readonly Color Rail = Color.FromArgb(29, 32, 33);
        internal static readonly Color Panel = Color.FromArgb(35, 38, 39);
        internal static readonly Color Raised = Color.FromArgb(42, 46, 47);
        internal static readonly Color Line = Color.FromArgb(60, 66, 66);
        internal static readonly Color Text = Color.FromArgb(231, 233, 225);
        internal static readonly Color Muted = Color.FromArgb(166, 175, 168);
        internal static readonly Color Green = Color.FromArgb(170, 204, 159);
        internal static readonly Color Selected = Color.FromArgb(52, 66, 58);
        internal static readonly Color Gold = Color.FromArgb(225, 177, 120);
        internal static readonly Color Red = Color.FromArgb(237, 154, 143);
        private static readonly Font DisplayFont = new Font("Segoe UI", 24, FontStyle.Bold, GraphicsUnit.Point);
        private static readonly Dictionary<Tuple<float, bool>, Font> Fonts = new Dictionary<Tuple<float, bool>, Font>();

        internal static Font Font(float size, bool bold = false)
        {
            var key = Tuple.Create(size, bold);
            Font font;
            if (!Fonts.TryGetValue(key, out font))
            {
                font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
                Fonts.Add(key, font);
            }
            return font;
        }

        internal static Label Label(string text, float size = 10, Color? color = null, bool bold = false)
        {
            return new Label { Text = text, ForeColor = color ?? Text, Font = Font(size, bold), AutoEllipsis = true, UseMnemonic = false,
                TextAlign = ContentAlignment.MiddleLeft, AutoSize = false, Margin = Padding.Empty };
        }

        internal static Label Title()
        {
            var label = Label("");
            label.Font = DisplayFont;
            return label;
        }

        internal static Button Button(string text, Action click, bool primary = false)
        {
            var button = new MotionButton
            {
                Text = text, Height = 36, FlatStyle = FlatStyle.Flat, Font = Font(9.25f, true), BackColor = primary ? Green : Panel,
                ForeColor = primary ? Color.FromArgb(31, 44, 33) : Text, Cursor = Cursors.Hand, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(9, 0, 9, 0), MinimumSize = new Size(0, 36), Margin = new Padding(0, 0, 8, 0)
            };
            button.FlatAppearance.BorderColor = primary ? Green : Line;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(188, 220, 177) : Selected;
            if (click != null) button.Click += (sender, args) => click();
            return button;
        }

        internal static Panel Surface(Color color)
        {
            return new Panel { BackColor = color, Margin = Padding.Empty };
        }

        internal static Icon Icon()
        {
            using (var bitmap = new Bitmap(48, 48))
            {
                using (var graphics = Graphics.FromImage(bitmap)) DrawSprout(graphics, new RectangleF(2, 2, 44, 44));
                var handle = bitmap.GetHicon();
                try { return (Icon)System.Drawing.Icon.FromHandle(handle).Clone(); }
                finally { Native.DestroyIcon(handle); }
            }
        }

        internal static void DrawSprout(Graphics graphics, RectangleF rect)
        {
            var state = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TranslateTransform(rect.X, rect.Y);
            graphics.ScaleTransform(rect.Width / 48, rect.Height / 48);
            using (var disk = new SolidBrush(Color.FromArgb(58, 83, 64))) graphics.FillEllipse(disk, 0, 0, 48, 48);
            using (var light = new SolidBrush(Color.FromArgb(238, 244, 220)))
            using (var warm = new SolidBrush(Color.FromArgb(225, 176, 111)))
            using (var stem = new Pen(Color.FromArgb(238, 244, 220), 2.4f))
            using (var left = new GraphicsPath())
            using (var right = new GraphicsPath())
            {
                left.AddBezier(24, 25, 13, 25, 11, 19, 10, 14);
                left.AddBezier(10, 14, 19, 13, 23, 17, 24, 25);
                left.CloseFigure();
                right.AddBezier(24, 24, 23, 15, 27, 10, 37, 10);
                right.AddBezier(37, 10, 38, 21, 31, 25, 24, 24);
                right.CloseFigure();
                graphics.DrawBezier(stem, 25, 36, 24, 33, 24, 27, 24, 21);
                graphics.FillPath(light, left);
                graphics.FillPath(light, right);
                graphics.FillEllipse(warm, 14, 35, 20, 4);
            }
            graphics.Restore(state);
        }
    }

    internal sealed class ModGrid : DataGridView
    {
        internal ModGrid(bool compact = false)
        {
            DoubleBuffered = true;
            Dock = DockStyle.Fill;
            BackgroundColor = Theme.Panel;
            BorderStyle = BorderStyle.None;
            GridColor = Theme.Line;
            EnableHeadersVisualStyles = false;
            RowHeadersVisible = false;
            ReadOnly = true;
            AllowUserToAddRows = false;
            AllowUserToDeleteRows = false;
            AllowUserToResizeRows = false;
            AllowUserToResizeColumns = false;
            MultiSelect = false;
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            CellBorderStyle = DataGridViewCellBorderStyle.None;
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            ColumnHeadersHeight = 35;
            RowTemplate.Height = compact ? 30 : 38;
            SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Theme.Panel, ForeColor = Theme.Text, SelectionBackColor = Theme.Selected,
                SelectionForeColor = Theme.Text, Font = Theme.Font(9.5f), Padding = new Padding(10, 0, 3, 0)
            };
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Theme.Raised, ForeColor = Theme.Muted, SelectionBackColor = Theme.Raised,
                SelectionForeColor = Theme.Muted, Font = Theme.Font(9, true), Padding = new Padding(10, 0, 0, 0)
            };
            AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(37, 41, 42);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            foreach (var bar in Controls.OfType<ScrollBar>())
                Native.SetWindowTheme(bar.Handle, "DarkMode_Explorer", null);
        }

        internal void AddColumn(string name, int weight, int minimum)
        {
            Columns.Add(new DataGridViewTextBoxColumn { HeaderText = name, FillWeight = weight, MinimumWidth = minimum,
                SortMode = DataGridViewColumnSortMode.NotSortable });
        }
    }

    internal sealed partial class Window : Form
    {
        private readonly Store store;
        private readonly Sources sources;
        private readonly Panel center, detail, intro;
        private readonly Label footer, gameState, title, subhead;
        private readonly Button play, smapiButton;
        private readonly Dictionary<string, Button> nav = new Dictionary<string, Button>();
        private readonly Dictionary<string, List<PendingNexusDownload>> browserUpdates = new Dictionary<string, List<PendingNexusDownload>>();
        private readonly Queue<string> inbox = new Queue<string>();
        private bool processingInbox;
        private List<Mod> mods = new List<Mod>();
        private List<CatalogMod> catalog = new List<CatalogMod>();
        private bool busy, updatesChecked, buildingRows, compatOnlyWarnings = true;
        private string view = "mods", filter = "", searchTerm = "", sideTab = "about";
        private string selectedSlot;
        private int searchPage, stateFilter;
        private ModGrid grid;
        private Action<double> meter;

        internal Window(Store store, Sources source, Action started = null, string startupMessage = null, bool startupError = false)
        {
            this.store = store;
            sources = source;
            MotionButton.AnimationsEnabled = !store.Settings.ReduceMotion;
            Text = "Луг · Stardew Valley";
            Icon = Theme.Icon();
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Font(10);
            DoubleBuffered = true;
            ClientSize = new Size(1210, 760);
            MinimumSize = new Size(1090, 667);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;

            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(shell);
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Rail, ColumnCount = 3, RowCount = 1,
                Padding = new Padding(18, 0, 16, 0), Margin = Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 152));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 262));
            shell.Controls.Add(header, 0, 0);
            var brand = Theme.Surface(Theme.Rail);
            brand.Dock = DockStyle.Fill;
            brand.Paint += (sender, args) => Theme.DrawSprout(args.Graphics, new RectangleF(0, 15, 34, 34));
            header.Controls.Add(brand, 0, 0);
            Place(brand, Theme.Label("Луг", 14, Theme.Text, true), 43, 10, 106, 24);
            Place(brand, Theme.Label("Stardew Valley", 8.3f, Theme.Muted), 44, 34, 103, 18);

            var sections = Actions();
            sections.BackColor = Theme.Rail;
            sections.Padding = new Padding(0, 15, 0, 0);
            header.Controls.Add(sections, 1, 0);
            foreach (var item in new[] { Tuple.Create("mods", "Моды"), Tuple.Create("builds", "Сборки"), Tuple.Create("catalog", "Каталог модов"),
                Tuple.Create("updates", "Обновления"), Tuple.Create("compat", "Совместимость"), Tuple.Create("settings", "Настройки") })
            {
                string key = item.Item1;
                var button = Theme.Button(item.Item2, () => ShowView(key));
                button.FlatAppearance.BorderSize = 0;
                button.Padding = new Padding(5, 0, 5, 0);
                button.Margin = new Padding(0, 0, 2, 0);
                sections.Controls.Add(button);
                nav[key] = button;
            }

            var launch = Theme.Surface(Theme.Rail);
            launch.Dock = DockStyle.Fill;
            header.Controls.Add(launch, 2, 0);
            smapiButton = Theme.Button("SMAPI", ManageSmapi);
            smapiButton.AutoSize = false;
            Place(launch, smapiButton, 2, 15, 149, 36);
            play = Theme.Button("Играть", Launch, true);
            play.AutoSize = false;
            Place(launch, play, 159, 15, 100, 36);

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(18, 9, 18, 5),
                ColumnCount = 1, RowCount = 5, Margin = Padding.Empty };
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            shell.Controls.Add(body, 0, 1);
            title = Theme.Title();
            title.Dock = DockStyle.Fill;
            subhead = Theme.Label("", 10, Theme.Muted);
            subhead.Dock = DockStyle.Fill;
            body.Controls.Add(title, 0, 0);
            body.Controls.Add(subhead, 0, 1);
            intro = Theme.Surface(Theme.Bg);
            intro.Dock = DockStyle.Fill;
            body.Controls.Add(intro, 0, 2);
            center = Theme.Surface(Theme.Bg);
            center.Dock = DockStyle.Fill;
            body.Controls.Add(center, 0, 3);
            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Theme.Bg, Margin = Padding.Empty };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 152));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
            footer = Theme.Label("", 9, Theme.Muted);
            footer.Dock = DockStyle.Fill;
            gameState = Theme.Label("", 8.7f, Theme.Muted);
            gameState.Dock = DockStyle.Fill;
            gameState.TextAlign = ContentAlignment.MiddleRight;
            bottom.Controls.Add(footer, 0, 0);
            bottom.Controls.Add(gameState, 1, 0);
            var credit = Theme.Label("Сделано Шуфлядкой", 8.7f, Theme.Muted);
            credit.Dock = DockStyle.Fill;
            credit.TextAlign = ContentAlignment.MiddleRight;
            bottom.Controls.Add(credit, 2, 0);
            body.Controls.Add(bottom, 0, 4);
            detail = Theme.Surface(Theme.Panel);
            detail.Dock = DockStyle.Fill;
            ShowView("mods");
            Shown += async (sender, args) =>
            {
                await Run("Читаю установленные моды…", async () =>
                {
                    await RefreshMods();
                    if (started != null) started();
                    Status(startupMessage ?? "Готово", startupError ? Theme.Red : startupMessage == null ? (Color?)null : Theme.Green);
                });
                OfferExtractedPack();
                try { await CheckRemoteVersions(); }
                catch (IOException) { }
            };
            FormClosing += (sender, args) =>
            {
                if (busy && args.CloseReason == CloseReason.UserClosing)
                {
                    args.Cancel = true;
                    Status("Дождись завершения текущей операции.");
                }
            };
        }

        private static void Place(Control parent, Control child, int x, int y, int w, int h)
        {
            child.SetBounds(x, y, w, h);
            parent.Controls.Add(child);
        }

        private static void Clear(Control parent)
        {
            foreach (var child in parent.Controls.Cast<Control>().ToArray()) child.Dispose();
        }

        private static TextBox Input(string text, string hint)
        {
            var input = new TextBox { Text = text, BackColor = Theme.Raised, ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle, Font = Theme.Font(11) };
            input.HandleCreated += (sender, args) => Native.SendMessage(input.Handle, 0x1501, IntPtr.Zero, hint);
            return input;
        }

        private static FlowLayoutPanel Actions()
        {
            return new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Bg, WrapContents = false, Margin = Padding.Empty };
        }

        private void Status(string value, Color? color = null)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                if (IsHandleCreated) BeginInvoke(new Action(() => Status(value, color)));
                return;
            }
            footer.Text = value;
            footer.ForeColor = color ?? Theme.Muted;
        }

        private void Progress(string message)
        {
            Status(message);
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(() =>
            {
                if (meter == null) return;
                int value;
                var trailing = message.Split(' ').Last().TrimEnd('%');
                meter(message.EndsWith("%") && int.TryParse(trailing, out value) ? value / 100d : 0);
            }));
        }

        private async Task Run(string starting, Func<Task> work)
        {
            if (busy) { Status("Идёт другая операция."); return; }
            busy = true;
            UseWaitCursor = true;
            play.Enabled = false;
            smapiButton.Enabled = false;
            Status(starting);
            try { await work(); }
            catch (Exception exc)
            {
                string message = exc is UnauthorizedAccessException ? "Windows запретила доступ к папке. Проверь права на неё." :
                    exc is System.Net.Http.HttpRequestException ? "Скачивание прервалось. Проверь подключение и повтори." :
                    exc is OperationCanceledException ? "Операция заняла слишком много времени. Повтори попытку." : exc.Message;
                Status(message, Theme.Red);
                MessageBox.Show(this, message, "Луг", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                busy = false;
                UseWaitCursor = false;
                UpdateGameState();
                if (meter != null) meter(0);
                if (!restartingLauncher) ProcessInbox();
            }
        }

        private async Task RefreshMods()
        {
            var previous = mods.ToLookup(m => Path.GetFileName(m.Path), StringComparer.OrdinalIgnoreCase);
            mods = await Task.Run(() => store.ListMods());
            foreach (var mod in mods)
            {
                Mod prior = previous[Path.GetFileName(mod.Path)].FirstOrDefault(item =>
                    item.Version == mod.Version && item.Id == mod.Id && item.NexusId == mod.NexusId);
                if (prior == null) continue;
                mod.NewVersion = prior.NewVersion;
                mod.UpdateUrl = prior.UpdateUrl;
                mod.OnlineStatus = prior.OnlineStatus;
                mod.OnlineNote = prior.OnlineNote;
                mod.BrokeIn = prior.BrokeIn;
            }
            ShowView(view);
        }

        private void UpdateGameState()
        {
            string smapi = store.InstalledSmapi();
            gameState.Text = smapi.Length == 0 ? "○  SMAPI не найден" : "●  SMAPI " + smapi;
            gameState.ForeColor = smapi.Length == 0 ? Theme.Gold : Theme.Green;
            smapiButton.Text = smapi.Length == 0 ? "Поставить SMAPI" : "Обновить SMAPI";
            smapiButton.Visible = smapi.Length == 0 || SmapiUpdateAvailable;
            smapiButton.Enabled = !busy;
            play.Enabled = !busy;
        }

        private void ShowView(string tab)
        {
            if (view != tab) sideTab = tab == "compat" ? "compat" : "about";
            view = tab;
            if (detail.Parent != null) detail.Parent.Controls.Remove(detail);
            Clear(detail);
            Clear(intro);
            Clear(center);
            ((TableLayoutPanel)intro.Parent).RowStyles[2].Height = tab == "settings" ? 15 : 49;
            meter = null;
            foreach (var item in nav)
            {
                item.Value.BackColor = item.Key == tab ? Theme.Selected : Theme.Rail;
                item.Value.ForeColor = item.Key == tab ? Theme.Green : Theme.Muted;
            }
            RefreshUpdateIndicators();
            UpdateGameState();
            if (tab == "mods") RenderMods(false);
            else if (tab == "updates") RenderMods(true);
            else if (tab == "compat") RenderMods(false);
            else if (tab == "catalog") RenderCatalog();
            else if (tab == "builds") RenderBuilds();
            else RenderSettings();
        }

        private static bool UpdateAvailable(Mod mod) { return !string.IsNullOrEmpty(mod.NewVersion); }

        private void Empty(string heading, string body, Action action, string button)
        {
            var card = Theme.Surface(Theme.Panel);
            card.Dock = DockStyle.Fill;
            center.Controls.Add(card);
            var content = new TableLayoutPanel { ColumnCount = 1, RowCount = 4, AutoSize = false, Width = 560, Height = 195,
                BackColor = Theme.Panel, Margin = Padding.Empty, Padding = new Padding(15, 0, 15, 0) };
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
            var logo = Theme.Surface(Theme.Panel);
            logo.Dock = DockStyle.Fill;
            logo.Paint += (sender, args) => Theme.DrawSprout(args.Graphics, new RectangleF((logo.Width - 45) / 2f, 0, 45, 45));
            content.Controls.Add(logo, 0, 0);
            var headline = Theme.Label(heading, 19, Theme.Text, true);
            headline.Dock = DockStyle.Fill;
            headline.TextAlign = ContentAlignment.MiddleCenter;
            content.Controls.Add(headline, 0, 1);
            var info = Theme.Label(body, 9.5f, Theme.Muted);
            info.Dock = DockStyle.Fill;
            info.TextAlign = ContentAlignment.TopCenter;
            content.Controls.Add(info, 0, 2);
            if (action != null)
            {
                var cta = Theme.Button(button, action, true);
                cta.Anchor = AnchorStyles.Top;
                content.Controls.Add(cta, 0, 3);
            }
            card.Controls.Add(content);
            Action position = () => { content.Width = Math.Min(570, card.ClientSize.Width - 22);
                content.Location = new Point((card.Width - content.Width) / 2, Math.Max(20, (card.Height - content.Height) / 2)); };
            card.Resize += (sender, args) => position();
            position();
        }

        private void RenderMods(bool updates)
        {
            bool compat = view == "compat";
            int warnings = mods.Count(Compatibility.HasWarning);
            title.Text = updates ? "Обновления" : compat ? "Совместимость" : store.BuildName();
            subhead.Text = updates
                ? (updatesChecked ? "Доступно обновлений: " + mods.Count(UpdateAvailable) : "Рекомендации SMAPI для установленных версий.")
                : compat ? "Замечаний к включённым модам: " + warnings + (updatesChecked ? "  ·  База SMAPI проверена" : "  ·  Локальная проверка")
                : mods.Count(mod => mod.Enabled) + " включено  /  " + mods.Count + " модов";
            var actions = Actions();
            actions.Padding = new Padding(0, 4, 0, 0);
            intro.Controls.Add(actions);
            if (!updates && !compat)
            {
                actions.Controls.Add(Theme.Button("Установить мод", LocalArchive));
                actions.Controls.Add(Theme.Button("Пресеты модов", ModPresets));
                var diagnose = Theme.Button("Разобрать проблемы", DiagnoseBuild);
                diagnose.Enabled = mods.Count > 0;
                actions.Controls.Add(diagnose);
            }
            if (compat)
            {
                actions.Controls.Add(Theme.Button("Сверить с базой SMAPI", CheckUpdates, true));
                actions.Controls.Add(Theme.Button("Разобрать проблемы", DiagnoseBuild));
                actions.Controls.Add(Theme.Button("Последний запуск", ViewLastRun));
                var toggle = Theme.Button(compatOnlyWarnings ? "Показать все" : "Только замечания", () =>
                {
                    compatOnlyWarnings = !compatOnlyWarnings;
                    ShowView("compat");
                });
                toggle.Enabled = mods.Count > 0;
                actions.Controls.Add(toggle);
            }
            if (updates) UpdateActions(actions);
            if (mods.Count == 0)
            {
                bool mo2 = !string.IsNullOrEmpty(store.Settings.Mo2);
                Empty(updates || compat ? "В сборке пока нет модов" : "Твоя библиотека пока пуста",
                    updates ? "Здесь будут обновления установленных модов." : compat ? "Импортируй сборку или установи мод." :
                    mo2 ? "Можно перенести моды из найденного профиля SDV MO2." : "Добавь мод или установи готовую сборку из ZIP.",
                    updates || compat ? (Action)(() => ShowView("mods")) : mo2 ? OpenImport : (Action)OpenPack,
                    updates || compat ? "К моим модам" : mo2 ? "Импорт из MO2" : "Выбрать сборку");
                return;
            }

            if (compat && compatOnlyWarnings && warnings == 0)
            {
                Empty("Замечаний пока нет", "По проверенным требованиям проблем не найдено. Итоги последнего запуска можно посмотреть отдельно.",
                    () => { compatOnlyWarnings = false; ShowView("compat"); }, "Все моды");
                return;
            }

            bool library = !updates && !compat;
            var layout = new TableLayoutPanel { RowCount = library ? 2 : 1, ColumnCount = 1, Dock = DockStyle.Fill, Margin = Padding.Empty };
            if (library) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            center.Controls.Add(layout);
            TextBox input = null;
            if (library)
            {
                var filters = Actions();
                layout.Controls.Add(filters, 0, 0);
                input = Input(filter, "Поиск по моим модам");
                input.Width = 225;
                input.Margin = new Padding(0, 4, 8, 0);
                input.AccessibleName = "Поиск по списку модов";
                filters.Controls.Add(input);
                string[] states = { "Состояние", "Включены", "Выключены", "Замечания" };
                var choose = Theme.Button(states[stateFilter] + "  ▾", null);
                choose.AutoSize = false;
                choose.Width = 113;
                var menu = new ContextMenuStrip { BackColor = Theme.Raised, ForeColor = Theme.Text, Font = Theme.Font(9.5f), ShowImageMargin = false };
                for (int i = 0; i < states.Length; i++)
                {
                    int state = i;
                    menu.Items.Add(states[i], null, (sender, args) =>
                    {
                        stateFilter = state;
                        choose.Text = states[state] + "  ▾";
                        FillMods(false, state);
                    });
                }
                choose.Click += (sender, args) => menu.Show(choose, new Point(0, choose.Height));
                choose.Disposed += (sender, args) => menu.Dispose();
                filters.Controls.Add(choose);
                filters.Controls.Add(Theme.Button("Обновить список", Reload));
            }
            var split = Split(layout, library ? 1 : 0, library);
            grid = new ModGrid(store.Settings.CompactRows);
            grid.MultiSelect = library;
            grid.AddColumn("Мод", 52, 145);
            grid.AddColumn(updates ? "Текущая → новая" : "Версия", 21, 83);
            grid.AddColumn(compat ? "Проверка" : "Состояние", 27, 114);
            split.Controls.Add(grid, library ? 1 : 0, 0);
            grid.CurrentCellChanged += (sender, args) =>
            {
                if (!buildingRows) ModDetails(grid.CurrentRow == null ? null : grid.CurrentRow.Tag as Mod);
            };
            Action fill = () => FillMods(updates, library ? stateFilter : 0);
            if (input != null) input.TextChanged += (sender, args) => { filter = input.Text; fill(); };
            fill();
        }

        private TableLayoutPanel Split(TableLayoutPanel layout, int row, bool categories = false)
        {
            var split = new TableLayoutPanel { ColumnCount = categories ? 3 : 2, RowCount = 1, Dock = DockStyle.Fill, Margin = Padding.Empty };
            if (categories) split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 181));
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 338));
            layout.Controls.Add(split, 0, row);
            if (categories) split.Controls.Add(CategoryRail(), 0, 0);
            detail.Margin = new Padding(13, 0, 0, 0);
            split.Controls.Add(detail, categories ? 2 : 1, 0);
            return split;
        }

        private void FillMods(bool updates, int state)
        {
            string desired = selectedSlot;
            int selected = -1;
            buildingRows = true;
            grid.SuspendLayout();
            grid.Rows.Clear();
            bool compat = view == "compat";
            var items = mods.Where(mod => (!updates || UpdateAvailable(mod)) &&
                (!compat || mod.Enabled && (!compatOnlyWarnings || Compatibility.HasWarning(mod))) &&
                (updates || compat || categoryFilter == null || string.Equals(ModCategory(mod), categoryFilter, StringComparison.CurrentCultureIgnoreCase)) &&
                (updates || compat || filter.Length == 0 || (mod.Name + " " + mod.Id + " " + mod.Author).IndexOf(filter.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0) &&
                (state == 0 || state == 1 && mod.Enabled || state == 2 && !mod.Enabled || state == 3 && Compatibility.HasWarning(mod)));
            foreach (var item in items)
            {
                var issue = Compatibility.HasWarning(item);
                int row = grid.Rows.Add(item.Name, updates ? item.Version + " → " + item.NewVersion : item.Version,
                    updates ? "Доступно" : issue ? "Есть замечания" : compat ? "Без замечаний" : item.Enabled ? "Включён" : "Выключен");
                grid.Rows[row].Tag = item;
                grid.Rows[row].Cells[2].Style.ForeColor = issue ? Theme.Gold : item.Enabled || updates ? Theme.Green : Theme.Muted;
                if (desired == Path.GetFileName(item.Path)) selected = row;
            }
            if (selected >= 0) grid.CurrentCell = grid.Rows[selected].Cells[0];
            grid.ResumeLayout();
            buildingRows = false;
            ModDetails(grid.CurrentRow == null ? null : grid.CurrentRow.Tag as Mod);
        }

        private const int DetailWidth = 288;

        private static Label SideText(Control panel, string text, float size = 10, Color? color = null, bool bold = false)
        {
            var label = Theme.Label(text, size, color, bold);
            label.TextAlign = ContentAlignment.TopLeft;
            var measured = TextRenderer.MeasureText(text, label.Font, new Size(DetailWidth, 0),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);
            label.Size = new Size(DetailWidth, Math.Max(21, measured.Height + 4));
            label.Margin = new Padding(0, 0, 0, 8);
            panel.Controls.Add(label);
            return label;
        }

        private FlowLayoutPanel Side(string heading, string meta, string notes)
        {
            Clear(detail);
            var block = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                AutoScroll = true, Padding = new Padding(17, 17, 14, 8), BackColor = Theme.Panel };
            block.HandleCreated += (sender, args) => Native.SetWindowTheme(block.Handle, "DarkMode_Explorer", null);
            detail.Controls.Add(block);
            SideText(block, heading, 15, Theme.Text, true);
            if (meta.Length > 0) SideText(block, meta, 9, Theme.Muted);
            if (notes.Length > 0) SideText(block, notes, 9, Theme.Gold);
            return block;
        }

        private static void SideAction(Control panel, string text, Action handler, bool primary = false)
        {
            var button = Theme.Button(text, handler, primary);
            button.AutoSize = false;
            button.Size = new Size(DetailWidth, 37);
            button.Margin = new Padding(0, 4, 0, 2);
            panel.Controls.Add(button);
        }

        private void ModDetails(Mod mod)
        {
            if (mod == null)
            {
                Side(view == "updates" ? (updatesChecked ? "Всё актуально" : "Проверка обновлений") : "Выбери мод",
                    view == "updates" ? (updatesChecked ? "SMAPI не предложил новых версий." : "Нажми «Проверить обновления».") : "Здесь появятся действия и зависимости.", "");
                return;
            }
            selectedSlot = Path.GetFileName(mod.Path);
            var panel = Side(mod.Name, mod.Version + (mod.Author.Length > 0 ? "  ·  " + mod.Author : ""), "");
            DetailTabs(panel, mod);
            if (sideTab == "compat") { DetailCompatibility(panel, mod); return; }
            if (sideTab == "addons") { DetailAddons(panel, mod); return; }
            DetailDescription(panel, mod);
            if (UpdateAvailable(mod)) SideAction(panel, "Обновить до " + mod.NewVersion, () => UpdateMod(mod), true);
            SideAction(panel, mod.Enabled ? "Выключить" : "Включить", () => Toggle(mod), !UpdateAvailable(mod));
            SideAction(panel, "Категория: " + ModCategory(mod), () => AssignCategory(mod));
            if (mod.NexusId > 0) SideAction(panel, "Файлы на Nexus", () => SelectNexusFiles(mod.NexusId, mod.Name, mod));
            else if (mod.GitRepo != null) SideAction(panel, "Файлы на GitHub", () => SelectGitFiles(mod.GitRepo, mod, false));
            SideAction(panel, "Открыть папку", () => Native.Open(mod.Path));
            string backup = store.LatestBackup(mod);
            if (backup != null) SideAction(panel, "Вернуть прошлую версию", () => Restore(mod, backup));
        }

        private async void Reload()
        {
            await Run("Читаю список…", async () => { await RefreshMods(); Status("Список обновлён."); });
        }

        private void RenderCatalog()
        {
            title.Text = "Каталог";
            bool linked = store.Settings.Key().Length > 0;
            subhead.Text = linked ? "Поиск по Nexus Mods  ·  " + (store.Settings.Premium ? "Premium" : "Бесплатный аккаунт") :
                "Найди мод на Nexus или скачай выпуск с GitHub.";
            var actions = Actions();
            actions.Padding = new Padding(0, 4, 0, 0);
            intro.Controls.Add(actions);
            var input = Input(searchTerm, "Название мода, ссылка или ID");
            input.Width = 290;
            input.AccessibleName = "Название мода, ссылка или ID Nexus";
            input.Margin = new Padding(0, 5, 9, 0);
            actions.Controls.Add(input);
            actions.Controls.Add(Theme.Button("Найти", () => SearchCatalog(input.Text, 0), true));
            actions.Controls.Add(Theme.Button("Популярное", () => SearchCatalog("", 0)));
            actions.Controls.Add(Theme.Button("GitHub…", OpenGit));
            input.KeyDown += (sender, args) =>
            {
                if (args.KeyCode != Keys.Enter) return;
                args.SuppressKeyPress = true;
                SearchCatalog(input.Text, 0);
            };
            var progress = Theme.Surface(Theme.Line);
            progress.SetBounds(0, 46, intro.Width, 3);
            progress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            progress.Visible = false;
            intro.Controls.Add(progress);
            progress.BringToFront();
            double fraction = 0;
            progress.Paint += (sender, args) =>
            {
                using (var brush = new SolidBrush(Theme.Green)) args.Graphics.FillRectangle(brush, 0, 0, (int)(progress.Width * fraction), 3);
            };
            meter = value => { fraction = value; progress.Visible = value > 0; progress.Invalidate(); };

            if (catalog.Count == 0)
            {
                Empty(linked ? "Найди мод" : "Подключи Nexus Mods", linked ?
                    "Введи название, номер или ссылку на мод. Можно также открыть популярные моды." :
                    "Для поиска и скачивания нужен личный API-ключ аккаунта Nexus.",
                    () => { if (linked) input.Focus(); else ShowView("settings"); }, linked ? "К поиску" : "Настроить Nexus");
                return;
            }
            var layout = new TableLayoutPanel { RowCount = 2, ColumnCount = 1, Dock = DockStyle.Fill, Margin = Padding.Empty };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
            center.Controls.Add(layout);
            var split = Split(layout, 0);
            grid = new ModGrid(store.Settings.CompactRows);
            grid.AddColumn("Мод", 53, 150);
            grid.AddColumn("Автор", 30, 110);
            grid.AddColumn("Версия", 17, 80);
            split.Controls.Add(grid, 0, 0);
            foreach (var item in catalog)
            {
                int index = grid.Rows.Add(item.Name, item.Author, item.Version);
                grid.Rows[index].Tag = item;
            }
            Action<CatalogMod> select = item =>
            {
                if (item == null) return;
                var panel = Side(item.Name, item.Author + (item.Version.Length > 0 ? " · " + item.Version : ""), "");
                if (item.Summary.Length > 0) SideText(panel, item.Summary, 10, Theme.Muted);
                SideAction(panel, "Выбрать файл", () => SelectNexusFiles(item.Id, item.Name, null), true);
                SideAction(panel, "Моды, которым он нужен", () => BrowseRelated(item.Id, item.Name));
                SideAction(panel, "Дополнительные файлы", () => SelectNexusFiles(item.Id, item.Name, null, optionalOnly: true));
                SideAction(panel, "Страница мода", () => Native.Open(Sources.Page(item.Id)));
            };
            grid.SelectionChanged += (sender, args) => select(grid.CurrentRow == null ? null : grid.CurrentRow.Tag as CatalogMod);
            grid.CellDoubleClick += (sender, args) =>
            {
                if (args.RowIndex < 0) return;
                var item = grid.Rows[args.RowIndex].Tag as CatalogMod;
                SelectNexusFiles(item.Id, item.Name, null);
            };
            select(catalog[0]);
            var pager = Actions();
            pager.Padding = new Padding(0, 8, 0, 0);
            layout.Controls.Add(pager, 0, 1);
            if (searchPage > 0) pager.Controls.Add(Theme.Button("← Назад", () => SearchCatalog(searchTerm, searchPage - 1)));
            if (catalog.Count == 30) pager.Controls.Add(Theme.Button("Дальше →", () => SearchCatalog(searchTerm, searchPage + 1)));
        }

        private static bool GameOpen()
        {
            foreach (var name in new[] { "StardewModdingAPI", "Stardew Valley" })
            {
                var processes = Process.GetProcessesByName(name);
                if (processes.Length == 0) continue;
                foreach (var process in processes) process.Dispose();
                return true;
            }
            return false;
        }

        private static void GameClosed()
        {
            if (GameOpen()) throw new IOException("Закрой Stardew Valley перед изменением сборки.");
        }

        private void Launch()
        {
            if (busy) return;
            if (store.InstalledSmapi().Length == 0) { ManageSmapi(); return; }
            try
            {
                GameClosed();
                bool confirm = !store.Settings.SkipLaunchWarnings;
                if (confirm && mods.Count == 0 && MessageBox.Show(this, "Список модов пуст. Запустить игру через SMAPI с пустой сборкой?",
                    "Запуск Stardew Valley", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                int issues = mods.Count(mod => !string.IsNullOrEmpty(mod.Problem));
                if (confirm && issues > 0 && MessageBox.Show(this, "Модов с замечаниями: " + issues + ". SMAPI может пропустить часть сборки. Всё равно запустить?",
                    "Проверка сборки", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                var info = Native.GameStart(store.Settings.Game, store.Active);
                using (Process.Start(info)) { }
                if (store.Settings.MinimizeOnLaunch) WindowState = FormWindowState.Minimized;
                Status("Stardew Valley запускается…", Theme.Green);
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "Запуск Stardew Valley", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private async void SetGame()
        {
            if (busy) return;
            if (!ChooseGame()) return;
            mods.Clear();
            updatesChecked = false;
            await Run("Проверяю папку игры…", async () => { await RefreshMods(); Status("Папка игры сохранена."); });
        }

        private bool ChooseGame()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Папка, где находится Stardew Valley.exe", SelectedPath = store.Settings.Game ?? "",
                ShowNewFolderButton = false })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return false;
                if (!Store.IsGame(dlg.SelectedPath))
                {
                    Status("В этой папке нет Stardew Valley.exe.", Theme.Red);
                    return false;
                }
                store.Settings.Game = dlg.SelectedPath;
                string candidate = Path.Combine(dlg.SelectedPath, "SDV MO2");
                if (Directory.Exists(candidate)) store.Settings.Mo2 = candidate;
                store.SaveSettings();
                return true;
            }
        }

        private async void OpenImport()
        {
            if (busy) return;
            if (mods.Count > 0)
            {
                MessageBox.Show(this, "Сборка уже импортирована. Новые моды можно добавить через каталог или архив.", "Импорт", MessageBoxButtons.OK);
                return;
            }
            string mo = store.Settings.Mo2 ?? "";
            string[] profiles = mo.Length == 0 ? new string[0] : store.Profiles(mo);
            using (var dialog = new ChoiceDialog("Импорт сборки", "Скопировать профиль MO2 или обычную папку Mods.",
                profiles.Cast<object>().Concat(new object[] { "Обычная папка Mods", "Выбрать папку MO2…" }).ToArray(), "Импортировать"))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var profile = dialog.Selected as string;
                bool stock = profile == "Обычная папка Mods";
                if (profile == "Выбрать папку MO2…")
                {
                    using (var folder = new FolderBrowserDialog { Description = "Папка MO2, где лежат profiles и mods", ShowNewFolderButton = false })
                    {
                        if (folder.ShowDialog(this) != DialogResult.OK) return;
                        mo = folder.SelectedPath;
                    }
                    profiles = store.Profiles(mo);
                    if (profiles.Length == 0) { Status("В этой папке не найдено профилей MO2.", Theme.Red); return; }
                    using (var choose = new ChoiceDialog("Профиль MO2", "Выбери профиль для импорта.", profiles, "Импортировать"))
                    {
                        if (choose.ShowDialog(this) != DialogResult.OK) return;
                        profile = (string)choose.Selected;
                    }
                }
                await Run("Копирую сборку…", async () =>
                {
                    GameClosed();
                    var total = await Task.Run(() => store.Import(mo, profile, stock, Progress));
                    if (!stock) store.Settings.Mo2 = mo;
                    store.SaveSettings();
                    view = "mods";
                    await RefreshMods();
                    Status("Импортировано модов SMAPI: " + total, Theme.Green);
                });
            }
        }

        private async void LocalArchive()
        {
            if (busy) return;
            using (var dlg = new OpenFileDialog { Title = "Выбери архив с модом", Filter = "Архивы модов|*.zip;*.7z;*.rar|Все файлы|*.*" })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    if (dlg.FileName.EndsWith(".lugpack.zip", StringComparison.OrdinalIgnoreCase)) ImportPack(dlg.FileName);
                    else await InstallArchive(dlg.FileName, new ModMeta { Source = "Локальный архив" }, null, null);
                }
            }
        }

        private async Task InstallArchive(string local, ModMeta meta, Mod mod, Func<Task<Uri>> remote, Action installed = null)
        {
            await Run(remote == null ? "Готовлю установку…" : "Получаю ссылку на скачивание…", async () =>
            {
                GameClosed();
                string download = remote == null ? local : store.NewDownload();
                try
                {
                    if (remote != null) await sources.Download(await remote(), download, Progress);
                    GameClosed();
                    var name = await Task.Run(() => store.Install(download, meta, mod, Progress));
                    if (installed != null) installed();
                    if (view == "catalog" || view == "settings") view = "mods";
                    await RefreshMods();
                    Status("Установлено: " + name, Theme.Green);
                }
                finally
                {
                    if (remote != null && File.Exists(download))
                        try { File.Delete(download); } catch (IOException) { }
                }
            });
        }

        private async void Toggle(Mod mod)
        {
            await Run(mod.Enabled ? "Выключаю мод…" : "Включаю мод…", async () =>
            {
                GameClosed();
                await Task.Run(() => store.Toggle(mod));
                await RefreshMods();
                Status(mod.Enabled ? "Мод выключен." : "Мод включён.", Theme.Green);
            });
        }

        private async void Restore(Mod mod, string backup)
        {
            if (busy || MessageBox.Show(this, "Вернуть последнюю резервную версию «" + mod.Name + "»?", "Восстановление",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            await Run("Восстанавливаю мод…", async () =>
            {
                GameClosed();
                await Task.Run(() => store.Restore(mod, backup));
                await RefreshMods();
                Status("Версия восстановлена.", Theme.Green);
            });
        }

        private async void Connect()
        {
            if (busy) return;
            using (var prompt = new TextDialog("Подключить Nexus", "Вставь личный API-ключ из вкладки API Access.", true))
            {
                if (prompt.ShowDialog(this) != DialogResult.OK) return;
                string key = prompt.Value.Trim();
                await Run("Проверяю ключ Nexus…", async () =>
                {
                    var account = await sources.Validate(key);
                    store.Settings.SetKey(key, account.Item1, account.Item2);
                    store.SaveSettings();
                    ShowView(view);
                    Status("Аккаунт Nexus подключён.", Theme.Green);
                });
            }
        }

        private void Disconnect()
        {
            if (busy) return;
            store.Settings.SetKey("", "", false);
            store.SaveSettings();
            catalog.Clear();
            ShowView(view);
            Status("Аккаунт Nexus отключён.");
        }

        private void Register()
        {
            if (busy) return;
            bool registered = Native.IsHandler();
            if (!registered && MessageBox.Show(this, "Ссылки Mod Manager Download будут открываться в «Луге». Вернуть прежний обработчик можно здесь же.",
                "Ссылки Nexus", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            try
            {
                Native.SetHandler(!registered);
                ShowView(view);
                Status(registered ? "Прежний обработчик ссылок восстановлен." : "Обработчик Nexus подключён.", Theme.Green);
            }
            catch (Exception error) { Status(error.Message, Theme.Red); }
        }

        private async void CheckUpdates()
        {
            bool remote = view == "updates";
            if (mods.Count == 0 && !remote) { Status("Пока нет установленных модов."); return; }
            await Run("Сверяю данные SMAPI…", async () =>
            {
                var remoteCheck = remote ? CheckRemoteVersions() : Task.FromResult(0);
                int count = 0;
                string problem = null;
                try
                {
                    if (mods.Count > 0)
                    {
                        count = await sources.CheckUpdates(store, mods, Progress);
                        updatesChecked = true;
                    }
                }
                catch (IOException error) { problem = error.Message; }
                var disconnected = await remoteCheck;
                ShowView(view);
                if (!string.IsNullOrEmpty(problem)) { Status(problem, Theme.Gold); return; }
                Status(view == "compat" || sideTab == "compat" ? "Замечаний: " + mods.Count(Compatibility.HasWarning) :
                    "Моды: " + count + " · Сборки: " + buildNotices.Count + (SmapiUpdateAvailable ? " · Доступен SMAPI" : "") +
                    (disconnected > 0 ? " · GitHub ответил не на все запросы" : ""), disconnected > 0 ? Theme.Gold : Theme.Green);
            });
        }

        private async void SearchCatalog(string text, int page)
        {
            if (busy) return;
            string key = store.Settings.Key();
            if (key.Length == 0) { ShowView("settings"); Status("Подключи Nexus, чтобы открыть каталог."); return; }
            await Run("Ищу моды на Nexus…", async () =>
            {
                var result = await sources.Catalog(text.Trim(), page, key);
                if (result.Count == 0 && page > 0) { Status("Следующей страницы нет."); return; }
                catalog = result;
                searchTerm = text.Trim();
                searchPage = page;
                ShowView("catalog");
                Status(result.Count == 0 ? "Ничего не найдено по запросу «" + searchTerm + "»." : "Показано модов: " + result.Count);
            });
        }

        private void UpdateMod(Mod mod)
        {
            if (busy) return;
            int id = Sources.NexusId(mod.UpdateUrl);
            if (id > 0) { SelectNexusFiles(id, mod.Name, mod); return; }
            string git = Sources.GitRepo(mod.UpdateUrl);
            if (git != null) { SelectGitFiles(git, mod, true); return; }
            if (mod.NexusId > 0 && string.IsNullOrEmpty(mod.UpdateUrl)) { SelectNexusFiles(mod.NexusId, mod.Name, mod); return; }
            if (!string.IsNullOrEmpty(mod.UpdateUrl)) Native.Open(mod.UpdateUrl);
        }

        private async void SelectNexusFiles(int id, string name, Mod mod, bool optionalOnly = false)
        {
            if (busy) return;
            string key = store.Settings.Key();
            if (key.Length == 0)
            {
                var result = MessageBox.Show(this, "Подключить Nexus для скачивания? «Нет» откроет страницу мода.",
                    "Nexus Mods", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (result == DialogResult.Yes) ShowView("settings");
                else if (result == DialogResult.No) Native.Open(Sources.Page(id) + "?tab=files");
                return;
            }
            ReleaseFile file = null;
            await Run("Загружаю список файлов Nexus…", async () =>
            {
                var releases = await sources.NexusFiles(id, key, mod);
                if (optionalOnly) releases = releases.Where(item => item.Category == "OPTIONAL").ToList();
                if (releases.Count == 0) throw new IOException(optionalOnly ? "На Nexus у этого мода нет файлов в категории Optional." : "На странице нет доступных файлов.");
                using (var choose = new ChoiceDialog(name, optionalOnly ? "Дополнительные файлы, опубликованные автором." : mod == null ? "Выбери основной файл или нужный вариант мода." :
                    "Установлена версия " + mod.Version + ". Сверь файл и вариант мода.", releases.Cast<object>().ToArray(),
                    store.Settings.Premium ? "Скачать и установить" : "Продолжить на Nexus"))
                    if (choose.ShowDialog(this) == DialogResult.OK) file = (ReleaseFile)choose.Selected;
                Status("Готово");
            });
            if (file == null) return;
            if (store.Settings.Premium)
            {
                await InstallArchive(null, new ModMeta { Source = "Nexus Mods", NexusId = id, FileId = file.Id }, mod,
                    () => sources.NexusDownload(new NxmLink { ModId = id, FileId = file.Id }, key));
                return;
            }
            if (!Native.IsHandler())
            {
                var answer = MessageBox.Show(this, "Связать ссылки Mod Manager Download с «Лугом»? Да — браузер передаст архив сюда. Нет — можно скачать файл вручную.",
                    "Ссылки Nexus", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) return;
                if (answer == DialogResult.Yes)
                {
                    try { Native.SetHandler(true); }
                    catch (Exception error) { Status(error.Message, Theme.Red); return; }
                }
            }
            bool handler = Native.IsHandler();
            if (handler)
            {
                string fileKey = id + "/" + file.Id;
                List<PendingNexusDownload> requests;
                if (!browserUpdates.TryGetValue(fileKey, out requests))
                    browserUpdates[fileKey] = requests = new List<PendingNexusDownload>();
                requests.RemoveAll(item => item.BuildId == store.SelectedBuild);
                requests.Add(new PendingNexusDownload { BuildId = store.SelectedBuild, BuildName = store.BuildName(), Mod = mod });
            }
            Native.Open(Sources.FilePage(id, file.Id) + (handler ? "&nmm=1" : ""));
            Status(handler ? "Продолжи скачивание на Nexus. Установка завершится здесь." : "Скачай архив на Nexus и установи его через «+ Архив».");
        }

        private void OpenGit()
        {
            if (busy) return;
            using (var prompt = new TextDialog("Выпуск GitHub", "Вставь ссылку на репозиторий или введи автор/репозиторий.", false))
            {
                if (prompt.ShowDialog(this) != DialogResult.OK) return;
                var repo = Sources.GitRepo(prompt.Value.Trim());
                if (repo == null) { Status("Нужна ссылка на репозиторий GitHub.", Theme.Red); return; }
                SelectGitFiles(repo, null, false);
            }
        }

        private async void SelectGitFiles(string repo, Mod mod, bool exactVersion)
        {
            if (busy) return;
            ReleaseFile file = null;
            await Run("Загружаю список файлов GitHub…", async () =>
            {
                var results = await sources.GitFiles(repo, mod == null ? null : mod.Meta.Asset, exactVersion && mod != null ? mod.NewVersion : null);
                if (results.Count == 0) throw new IOException("В последнем выпуске GitHub нет архивов ZIP, 7z или RAR.");
                using (var choose = new ChoiceDialog(mod == null ? repo : mod.Name, "Выбери архив мода для Windows.", results.Cast<object>().ToArray(), "Скачать и установить"))
                    if (choose.ShowDialog(this) == DialogResult.OK) file = (ReleaseFile)choose.Selected;
                Status("Готово");
            });
            if (file == null) return;
            await InstallArchive(null, new ModMeta { Source = "GitHub", GitRepo = repo, Asset = file.Name }, mod,
                () => Task.FromResult(new Uri(file.AssetUrl)));
        }

        internal void Receive(string value)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action(() => Receive(value))); return; }
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            if (string.IsNullOrEmpty(value)) return;
            inbox.Enqueue(value);
            ProcessInbox();
        }

        private async void ProcessInbox()
        {
            if (busy || processingInbox || restartingLauncher || inbox.Count == 0) return;
            processingInbox = true;
            try { await HandleNexusLink(inbox.Dequeue()); }
            finally { processingInbox = false; ProcessInbox(); }
        }

        private async Task HandleNexusLink(string raw)
        {
            var item = NxmLink.Parse(raw);
            if (item == null) { Status("Эта ссылка Nexus не относится к Stardew Valley.", Theme.Red); return; }
            string key = store.Settings.Key();
            if (key.Length == 0)
            {
                ShowView("settings");
                Status("Подключи Nexus и повтори Mod Manager Download.");
                return;
            }
            string fileKey = item.ModId + "/" + item.FileId;
            Mod mod = null;
            PendingNexusDownload pending = null;
            List<PendingNexusDownload> requests;
            if (browserUpdates.TryGetValue(fileKey, out requests))
            {
                pending = requests[0];
                if (requests.Count > 1)
                {
                    using (var choose = new ChoiceDialog("Загрузка Nexus", "Этот файл был открыт для нескольких сборок. Выбери назначение.",
                        requests.Cast<object>().ToArray(), "Продолжить"))
                    {
                        if (choose.ShowDialog(this) != DialogResult.OK) return;
                        pending = (PendingNexusDownload)choose.Selected;
                    }
                }
                if (pending.BuildId != store.SelectedBuild)
                {
                    Status("Загрузка предназначена для сборки «" + pending.BuildName + "».", Theme.Gold);
                    MessageBox.Show(this, "Выбери сборку «" + pending.BuildName + "» и повтори открытие ссылки Nexus.", "Загрузка мода",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (pending.Mod != null)
                {
                    mod = mods.FirstOrDefault(current => current.Path == pending.Mod.Path && current.Id == pending.Mod.Id &&
                        current.Version == pending.Mod.Version);
                    if (mod == null)
                    {
                        Status("Мод изменился. Открой его обновление в Луге заново.", Theme.Gold);
                        return;
                    }
                }
            }
            await InstallArchive(null, new ModMeta { Source = "Nexus Mods", NexusId = item.ModId, FileId = item.FileId }, mod,
                () => sources.NexusDownload(item, key), () =>
                {
                    if (pending == null) return;
                    requests.Remove(pending);
                    if (requests.Count == 0) browserUpdates.Remove(fileKey);
                });
        }
    }

    internal sealed class ChoiceDialog : Form
    {
        private readonly ListBox list;
        internal object Selected { get { return list.SelectedItem; } }

        internal ChoiceDialog(string heading, string message, object[] items, string action)
        {
            Text = heading;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ClientSize = new Size(665, 329);
            BackColor = Theme.Bg;
            var tip = Theme.Label(message, 10, Theme.Muted);
            tip.SetBounds(15, 11, 635, 47);
            Controls.Add(tip);
            list = new ListBox { Font = Theme.Font(10), BackColor = Theme.Panel, ForeColor = Theme.Text, IntegralHeight = false,
                HorizontalScrollbar = true, BorderStyle = BorderStyle.FixedSingle };
            list.SetBounds(16, 59, 630, 204);
            list.Items.AddRange(items);
            if (items.Length > 0) list.SelectedIndex = 0;
            Controls.Add(list);
            var apply = Theme.Button(action, () => { if (Selected != null) DialogResult = DialogResult.OK; }, true);
            apply.Location = new Point(16, 275);
            Controls.Add(apply);
            var cancel = Theme.Button("Отмена", () => DialogResult = DialogResult.Cancel);
            cancel.Location = new Point(542, 275);
            Controls.Add(cancel);
            AcceptButton = apply;
            CancelButton = cancel;
            list.DoubleClick += (sender, args) => { if (Selected != null) DialogResult = DialogResult.OK; };
        }
    }

    internal sealed class TextDialog : Form
    {
        private readonly TextBox input;
        internal string Value { get { return input.Text; } }

        internal TextDialog(string heading, string message, bool secret)
        {
            Text = heading;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ClientSize = new Size(495, 160);
            MaximizeBox = MinimizeBox = false;
            BackColor = Theme.Bg;
            var label = Theme.Label(message, 9.5f, Theme.Muted);
            label.SetBounds(15, 9, 465, 43);
            Controls.Add(label);
            input = new TextBox { Font = Theme.Font(11), UseSystemPasswordChar = secret, BackColor = Theme.Raised, ForeColor = Theme.Text };
            input.SetBounds(15, 56, 465, 30);
            Controls.Add(input);
            var save = Theme.Button("Продолжить", () => DialogResult = DialogResult.OK, true);
            save.Location = new Point(15, 105);
            Controls.Add(save);
            var cancel = Theme.Button("Отмена", () => DialogResult = DialogResult.Cancel);
            cancel.Location = new Point(375, 105);
            Controls.Add(cancel);
            AcceptButton = save;
            CancelButton = cancel;
        }
    }
}
