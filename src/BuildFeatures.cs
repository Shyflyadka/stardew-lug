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
        private void DetailTabs(FlowLayoutPanel panel, Mod mod)
        {
            var tabs = new FlowLayoutPanel
            {
                Size = new Size(DetailWidth, 44), BackColor = Theme.Panel, WrapContents = false, Margin = new Padding(0, 2, 0, 10)
            };
            foreach (var item in new[] { Tuple.Create("about", "Описание"), Tuple.Create("compat", "Проверка"), Tuple.Create("addons", "Аддоны") })
            {
                string key = item.Item1;
                var button = Theme.Button(item.Item2, () => { sideTab = key; ModDetails(mod); });
                button.Padding = new Padding(4, 0, 4, 0);
                button.Margin = new Padding(0, 0, 6, 0);
                if (key == sideTab)
                {
                    button.BackColor = Theme.Selected;
                    button.ForeColor = Theme.Green;
                    button.FlatAppearance.BorderColor = Theme.Selected;
                }
                tabs.Controls.Add(button);
            }
            panel.Controls.Add(tabs);
        }

        private void DetailDescription(FlowLayoutPanel panel, Mod mod)
        {
            string text, source;
            bool fromNexus = sources.CachedDescription(mod.NexusId, out text) && !string.IsNullOrEmpty(text);
            if (fromNexus) source = "Описание с Nexus Mods";
            else { text = mod.Description; source = "Описание автора в файлах мода"; }
            if (string.IsNullOrWhiteSpace(text)) SideText(panel, "В файлах мода нет описания.", 10, Theme.Muted);
            else
            {
                string excerpt = text.Length > 520 ? text.Substring(0, 520).TrimEnd() + "…" : text;
                if (excerpt.Count(ch => ch == '\n') > 10) excerpt = string.Join("\n", excerpt.Split('\n').Take(10)).TrimEnd() + "…";
                SideText(panel, excerpt);
                SideText(panel, source, 8.5f, Theme.Muted);
                if (excerpt != text) SideAction(panel, "Читать полностью", () => ShowText(mod.Name, source, text));
            }
            if (mod.NexusId > 0 && !fromNexus)
            {
                if (store.Settings.Key().Length > 0) SideAction(panel, "Описание с Nexus", () => LoadDescription(mod));
                else SideAction(panel, "Страница мода", () => Native.Open(Sources.Page(mod.NexusId)));
            }
        }

        private async void LoadDescription(Mod mod)
        {
            await Run("Получаю описание…", async () =>
            {
                string result = await sources.NexusDescription(mod.NexusId, store.Settings.Key());
                if (result.Length == 0) { Status("На Nexus нет описания для этой страницы."); return; }
                var current = grid == null || grid.IsDisposed || grid.CurrentRow == null ? null : grid.CurrentRow.Tag as Mod;
                if (current != null && current.Path == mod.Path && sideTab == "about") ModDetails(current);
                Status("Описание загружено.", Theme.Green);
            });
        }

        private void DetailCompatibility(FlowLayoutPanel panel, Mod mod)
        {
            SideText(panel, "Локальная проверка", 10, Theme.Text, true);
            if (!mod.Enabled) SideText(panel, "Мод выключен.", 10, Theme.Muted);
            else if (string.IsNullOrEmpty(mod.Problem)) SideText(panel, "Требования из файла мода соблюдены.", 10, Theme.Green);
            else SideText(panel, mod.Problem, 10, Theme.Gold);
            var requirements = new List<string>();
            if (!string.IsNullOrEmpty(mod.MinimumGame)) requirements.Add("Stardew Valley от " + mod.MinimumGame);
            if (!string.IsNullOrEmpty(mod.MinimumSmapi)) requirements.Add("SMAPI от " + mod.MinimumSmapi);
            requirements.AddRange(mod.Dependencies.Select(dep =>
            {
                var local = mods.FirstOrDefault(m => string.Equals(m.Id, dep.Item1, StringComparison.OrdinalIgnoreCase));
                return (local == null ? dep.Item1 : local.Name) + (dep.Item2.Length > 0 ? " от " + dep.Item2 : "");
            }));
            if (requirements.Count > 0) SideText(panel, string.Join("\n", requirements.Distinct()), 9, Theme.Muted);
            SideText(panel, "База SMAPI", 10, Theme.Text, true);
            if (mod.OnlineStatus == null)
                SideAction(panel, "Сверить с базой SMAPI", CheckUpdates);
            else
            {
                SideText(panel, Compatibility.OnlineLabel(mod), 10, Compatibility.HasWarning(mod) ? Theme.Gold : Theme.Muted);
                if (!string.IsNullOrEmpty(mod.OnlineNote)) SideText(panel, mod.OnlineNote, 9, Theme.Muted);
                if (!string.IsNullOrEmpty(mod.BrokeIn)) SideText(panel, "Несовместимость с версии " + mod.BrokeIn, 9, Theme.Muted);
                SideAction(panel, "Список совместимости SMAPI", () => Native.Open("https://smapi.io/mods"));
            }
        }

        private void DetailAddons(FlowLayoutPanel panel, Mod mod)
        {
            if (mod.ContentPackFor.Length > 0)
            {
                var parent = mods.FirstOrDefault(item => string.Equals(item.Id, mod.ContentPackFor, StringComparison.OrdinalIgnoreCase));
                SideText(panel, "Дополнение для " + (parent == null ? mod.ContentPackFor : parent.Name), 9, Theme.Muted);
                if (parent != null) SideAction(panel, "Открыть основной мод", () => FocusMod(parent));
            }
            var related = mods.Where(item => item != mod && (string.Equals(item.ContentPackFor, mod.Id, StringComparison.OrdinalIgnoreCase) ||
                item.DependencyIds.Contains(mod.Id, StringComparer.OrdinalIgnoreCase))).Select(item => new LocalRelation(item, mod.Id)).ToList();
            SideText(panel, "В этой сборке", 10, Theme.Text, true);
            if (related.Count == 0) SideText(panel, "Нет установленных паков и модов, требующих этот мод.", 9, Theme.Muted);
            else
            {
                SideText(panel, string.Join("\n", related.Take(3).Select(item => item.ToString())), 9, Theme.Muted);
                SideAction(panel, "Все связанные моды (" + related.Count + ")", () =>
                {
                    using (var choose = new ChoiceDialog(mod.Name, "Контент-паки и зависимости по файлам установленных модов.",
                        related.Cast<object>().ToArray(), "Показать мод"))
                        if (choose.ShowDialog(this) == DialogResult.OK) FocusMod(((LocalRelation)choose.Selected).Mod);
                });
            }
            if (mod.NexusId > 0)
            {
                SideText(panel, "На Nexus Mods", 10, Theme.Text, true);
                SideAction(panel, "Моды, которым нужен этот мод", () => BrowseRelated(mod.NexusId, mod.Name));
                SideAction(panel, "Дополнительные файлы", () => SelectNexusFiles(mod.NexusId, mod.Name, null, optionalOnly: true));
            }
        }

        private sealed class LocalRelation
        {
            internal readonly Mod Mod;
            private readonly string relation;
            internal LocalRelation(Mod mod, string id)
            {
                Mod = mod;
                relation = string.Equals(mod.ContentPackFor, id, StringComparison.OrdinalIgnoreCase) ? "контент-пак" : "зависит от мода";
            }
            public override string ToString() { return Mod.Name + " · " + relation + (Mod.Enabled ? "" : " · выключен"); }
        }

        private void FocusMod(Mod mod)
        {
            selectedSlot = Path.GetFileName(mod.Path);
            filter = "";
            stateFilter = 0;
            sideTab = "about";
            categoryFilter = null;
            ShowView("mods");
        }

        private async void BrowseRelated(int id, string name)
        {
            if (busy) return;
            string key = store.Settings.Key();
            if (key.Length == 0)
            {
                Native.Open(Sources.Page(id) + "?tab=description");
                Status("Нужен ключ Nexus для просмотра в Луге. На сайте открой Requirements.");
                return;
            }
            const string next = "Следующая страница →", previous = "← Предыдущая страница";
            int page = 0;
            while (true)
            {
                object selected = null;
                await Run("Ищу связанные моды на Nexus…", async () =>
                {
                    var result = await sources.NexusRelated(id, page, key);
                    var options = result.Item1.Cast<object>().ToList();
                    if (result.Item2) options.Add(next);
                    if (page > 0) options.Add(previous);
                    if (options.Count == 0) { Status("На Nexus нет модов, указавших эту зависимость."); return; }
                    using (var dialog = new ChoiceDialog(name, "Моды, которые указали этот мод в требованиях на Nexus. Страница " + (page + 1) + ".",
                        options.ToArray(), "Выбрать"))
                        if (dialog.ShowDialog(this) == DialogResult.OK) selected = dialog.Selected;
                    Status("Готово");
                });
                if (selected == null) return;
                if (Equals(selected, next)) { page++; continue; }
                if (Equals(selected, previous)) { page--; continue; }
                var relation = (RelatedMod)selected;
                var local = mods.FirstOrDefault(mod => mod.NexusId == relation.Id);
                if (local != null) FocusMod(local);
                else SelectNexusFiles(relation.Id, relation.Name, null);
                return;
            }
        }

        private async void ViewLastRun()
        {
            string log = Path.Combine(SmapiLogFolder(), "SMAPI-latest.txt");
            await Run("Читаю журнал SMAPI…", async () =>
            {
                var text = await Task.Run(() => Compatibility.LastSkipped(log, store.Active));
                ShowText("Последний запуск SMAPI", File.GetLastWriteTime(log).ToString("g") + "  ·  Пропущенные моды", text);
                Status("Готово");
            });
        }

        private bool ShowText(string heading, string caption, string body, string action = null)
        {
            using (var dialog = new Form
            {
                Text = heading, StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false,
                ClientSize = new Size(675, 460), MinimumSize = new Size(525, 360), BackColor = Theme.Bg, Icon = Icon
            })
            {
                var label = Theme.Label(caption, 10, Theme.Muted);
                label.SetBounds(16, 10, 630, 32);
                label.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                dialog.Controls.Add(label);
                var text = new TextBox
                {
                    Text = body, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical,
                    BackColor = Theme.Panel, ForeColor = Theme.Text, Font = Theme.Font(11),
                    Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom
                };
                text.SetBounds(16, 45, 643, 351);
                dialog.Controls.Add(text);
                var close = Theme.Button(action == null ? "Готово" : "Отмена", () => dialog.DialogResult = DialogResult.Cancel, action == null);
                close.Location = new Point(16, 409);
                close.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
                dialog.Controls.Add(close);
                dialog.CancelButton = close;
                if (action != null)
                {
                    var accept = Theme.Button(action, () => dialog.DialogResult = DialogResult.OK, true);
                    accept.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                    accept.SetBounds(478, 409, 181, 36);
                    accept.AutoSize = false;
                    dialog.Controls.Add(accept);
                }
                dialog.Shown += (sender, args) => { text.Select(0, 0); close.Focus(); };
                return dialog.ShowDialog(this) == DialogResult.OK;
            }
        }

        private void SharePack() { ExportCurrent(false); }

        private async void ExportCurrent(bool publish)
        {
            if (busy || mods.Count == 0) return;
            var options = new object[] { "С настройками модов (config.json)", "Без файлов config.json" };
            bool configs;
            using (var choice = new ChoiceDialog("Поделиться сборкой",
                "В ZIP войдут все моды и Луг. Перед отправкой проверь разрешения авторов и личные данные в настройках модов.",
                options, "Выбрать папку"))
            {
                if (choice.ShowDialog(this) != DialogResult.OK) return;
                configs = Equals(choice.Selected, options[0]);
            }
            using (var file = new SaveFileDialog
            {
                Title = "Сохранить сборку", Filter = "Сборка Луга (*.lugpack.zip)|*.lugpack.zip|ZIP-архив (*.zip)|*.zip",
                FileName = publish ? "Stardew.lugpack.zip" : string.Concat(store.BuildName().Split(Path.GetInvalidFileNameChars())) + ".lugpack.zip",
                AddExtension = true, DefaultExt = "zip", OverwritePrompt = true
            })
            {
                if (file.ShowDialog(this) != DialogResult.OK) return;
                await Run("Готовлю сборку…", async () =>
                {
                    GameClosed();
                    await Task.Run(() => store.ExportPack(file.FileName, Application.ExecutablePath, configs, Progress));
                    Status("Сборка сохранена: " + file.FileName, Theme.Green);
                    if (publish) ShowPublishInstructions(file.FileName);
                    else if (MessageBox.Show(this, "Сборка сохранена. Открыть папку с архивом?", "Поделиться сборкой",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                        Native.Open(Path.GetDirectoryName(file.FileName));
                });
            }
        }

        private void OpenPack()
        {
            if (busy) return;
            using (var file = new OpenFileDialog { Title = "Импорт сборки", Filter = "Сборка Луга (*.zip)|*.zip" })
                if (file.ShowDialog(this) == DialogResult.OK) ImportPack(file.FileName);
        }

        private void OfferExtractedPack()
        {
            var folder = Path.GetDirectoryName(Application.ExecutablePath);
            string stamp = PackStamp(folder);
            if (stamp != null && store.Settings.OpenedPack != stamp) ImportPack(folder);
        }

        private static string PackStamp(string folder)
        {
            var marker = Path.Combine(folder, Store.PackMarker);
            return File.Exists(marker) ? folder + "|" + File.GetLastWriteTimeUtc(marker).Ticks : null;
        }

        private async void ImportPack(string source)
        {
            if (busy) return;
            await Run("Читаю список сборки…", () => InstallProfileFrom(source));
        }

        private async Task InstallProfileFrom(string source, AssetOrigin origin = null)
        {
            var pack = await Task.Run(() => Store.ReadPack(source));
            string summary = pack.Name + "\nМодов: " + pack.Mods.Count + " (" + pack.Mods.Count(mod => mod.Enabled) + " включено)";
            if (!string.IsNullOrEmpty(pack.GameVersion) || !string.IsNullOrEmpty(pack.SmapiVersion))
                summary += "\nВерсии при экспорте: Stardew Valley " + pack.GameVersion + " · SMAPI " + pack.SmapiVersion;
            var incoming = await Task.Run(() => Store.PreviewPack(source, pack));
            var changes = Store.PreviewSwitch(mods, incoming);
            var warnings = store.Review(incoming, mod => mod.Enabled);
            string body = summary + "\r\n\r\nПри переключении с «" + store.BuildName() + "»\r\n" +
                (changes.Length > 0 ? changes : "Версии и включённость совпадают.") + ReviewText(warnings);
            if (!ShowText("Предпросмотр импорта", "«" + pack.Name + "» будет добавлена как отдельная сборка.", body, "Добавить сборку"))
            { Status("Импорт отменён."); return; }
            GameClosed();
            string id = await Task.Run(() => store.ImportProfile(source, Progress, origin));
            if (Directory.Exists(source)) store.Settings.OpenedPack = PackStamp(source);
            await SwitchBuild(id);
            if (origin != null)
            {
                buildNotices.RemoveAll(item => SameReleaseAsset(item.Asset.Origin, origin) && !item.Asset.Origin.IsNewerThan(origin));
                RefreshUpdateIndicators();
            }
            Status("Установлено модов из сборки: " + pack.Mods.Count, Theme.Green);
        }

        private async void RestorePreviousPack()
        {
            if (busy) return;
            string backup = store.PreviousPack();
            if (backup == null) return;
            if (MessageBox.Show(this, "Восстановить предыдущую сборку? Текущая станет резервной копией.", "Восстановление сборки",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            await Run("Восстанавливаю сборку…", async () =>
            {
                GameClosed();
                await Task.Run(() => store.RestorePack(backup));
                updatesChecked = false;
                ShowView("mods");
                await RefreshMods();
                Status("Предыдущая сборка восстановлена.", Theme.Green);
            });
        }
    }
}
