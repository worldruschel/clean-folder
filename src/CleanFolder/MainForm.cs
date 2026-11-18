using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CleanFolder
{
    public sealed class MainForm : Form
    {
        static int D(int v) => Theme.D(v);
        enum Page { Overview, Suggestions, Large, Types }

        readonly AppSettings cfg;
        readonly string startFolder;
        ScanResult result;
        string resultSignature;
        CancellationTokenSource cts;
        bool busy;
        Page page = Page.Overview;

        Updater.UpdateInfo update;
        string updateFile;
        bool updateStarted;

        /// <summary>Служебный режим: после анализа сохранить снимки страниц в обеих темах и закрыться.</summary>
        public string ShotPrefix;

        async void TakeShots()
        {
            await Task.Delay(300);
            foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })
            {
                Theme.Apply(mode);
                ApplyTheme();
                var name = mode == ThemeMode.Light ? "light" : "dark";
                foreach (var p in new[] { Page.Overview, Page.Suggestions, Page.Large, Page.Types })
                {
                    SetPage(p);
                    if (p == Page.Suggestions && gridSug.RowCount > 3)
                    {
                        gridSug.Rows[1].Selected = true; gridSug.Rows[2].Selected = true;
                    }
                    await Task.Delay(250);
                    using (var bmp = new Bitmap(Width, Height))
                    {
                        DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                        bmp.Save($"{ShotPrefix}-{name}-{p}.png", System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
            }
            SetPage(Page.Overview);
            OpenSettingsShot();
            Close();
        }

        void OpenSettingsShot()
        {
            using (var f = new SettingsForm(cfg, () => Task.FromResult("")) { Icon = Icon })
            {
                f.Shown += async (s, e) =>
                {
                    await Task.Delay(300);
                    using (var bmp = new Bitmap(f.Width, f.Height))
                    {
                        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                        bmp.Save($"{ShotPrefix}-settings.png", System.Drawing.Imaging.ImageFormat.Png);
                    }
                    f.Close();
                };
                f.ShowDialog(this);
            }
        }

        // Боковое меню
        readonly TPanel sidebar = new TPanel(Role.Sidebar);
        readonly PictureBox logo = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
        readonly TLabel lblLogo = new TLabel("Чистая папка", Role.Text, 13f, true);
        readonly NavItem navOverview = new NavItem("Обзор", Theme.GHome);
        readonly NavItem navSug = new NavItem("К удалению", Theme.GList);
        readonly NavItem navLarge = new NavItem("Большие файлы", Theme.GPage);
        readonly NavItem navTypes = new NavItem("Типы файлов", Theme.GPie);
        readonly NavItem navSettings = new NavItem("Настройки", Theme.GSettings);
        readonly TLabel lblVersion = new TLabel("", Role.Muted, 8.5f);

        // Верх
        readonly TPanel main = new TPanel(Role.Bg);
        readonly TPanel header = new TPanel(Role.Bg);
        readonly InputBox pathBox = new InputBox("Папка для анализа – выберите, вставьте путь или перетащите в окно", Theme.GFolder);
        readonly FlatButton btnRecent = new FlatButton("", BtnKind.Ghost, Theme.GChevronDown, compact: true);
        readonly FlatButton btnBrowse = new FlatButton("Выбрать…", BtnKind.Secondary, Theme.GFolderOpen);
        readonly FlatButton btnStart = new FlatButton("Анализировать", BtnKind.Primary, Theme.GPlay);
        readonly FlatButton btnStop = new FlatButton("Остановить", BtnKind.Secondary, Theme.GStop);

        // Баннер (обновление, подсказки)
        readonly TPanel bannerWrap = new TPanel(Role.Bg);
        readonly LayoutCard banner = new LayoutCard();
        readonly TLabel lblBannerIcon = new TLabel(Theme.GInfo, Role.Accent);
        readonly TLabel lblBanner = new TLabel("", Role.Text, 9.75f);
        readonly FlatButton btnBannerAction = new FlatButton("Обновить", BtnKind.Primary, Theme.GDownload, compact: true);
        readonly FlatButton btnBannerClose = new FlatButton("", BtnKind.Ghost, Theme.GClose, compact: true);
        Action bannerAction;

        // Строка состояния
        readonly TPanel statusBar = new TPanel(Role.Bg);
        readonly ThinProgress progress = new ThinProgress();
        readonly TLabel lblStatus = new TLabel("", Role.Muted, 9.25f) { AutoSize = false, AutoEllipsis = true };
        readonly LinkLabel lnkStatus = new LinkLabel { AutoSize = true, Visible = false, BackColor = Color.Transparent };
        Action statusLinkAction;

        // Страницы
        readonly TPanel host = new TPanel(Role.Bg);
        readonly TPanel pgOverview = new TPanel(Role.Bg), pgSug = new TPanel(Role.Bg), pgLarge = new TPanel(Role.Bg), pgTypes = new TPanel(Role.Bg);
        readonly FlowLayoutPanel overviewFlow = new FlowLayoutPanel();

        readonly TPanel sugBar = new TPanel(Role.Bg);
        readonly InputBox sugSearch = new InputBox("Поиск по имени, папке или причине", Theme.GSearch);
        readonly FlowLayoutPanel sugChips = new FlowLayoutPanel();
        readonly FlatButton btnMarkRec = new FlatButton("Отметить рекомендуемое", BtnKind.Ghost, Theme.GCheck, compact: true);
        readonly FlatButton btnMarkAll = new FlatButton("Отметить видимые", BtnKind.Ghost, null, compact: true);
        readonly FlatButton btnMarkNone = new FlatButton("Снять отметки", BtnKind.Ghost, null, compact: true);
        readonly ItemGrid gridSug = new ItemGrid(ItemGrid.Kind.Suggestions);
        Category? sugCategory;
        bool sugOnlyChecked;

        readonly TPanel largeBar = new TPanel(Role.Bg);
        readonly InputBox largeSearch = new InputBox("Поиск по имени или папке", Theme.GSearch);
        readonly FlowLayoutPanel largeChips = new FlowLayoutPanel();
        readonly FlatButton btnLargeNone = new FlatButton("Снять отметки", BtnKind.Ghost, null, compact: true);
        readonly TLabel lblLargeHint = new TLabel("1000 самых больших файлов в папке", Role.Muted, 9f);
        readonly ItemGrid gridLarge = new ItemGrid(ItemGrid.Kind.Large);
        string largeType;

        readonly TLabel lblTypesHint = new TLabel("Двойной щелчок по строке – показать самые большие файлы этого типа", Role.Muted, 9f);
        readonly ThemedGrid gridTypes = new ThemedGrid();

        // Панель удаления
        readonly TPanel actionBar = new TPanel(Role.Bg) { BorderTop = true };
        readonly ToggleSwitch swRecycle = new ToggleSwitch("В Корзину", true);
        readonly TLabel lblSelected = new TLabel("", Role.Text, 10f, true);
        readonly TLabel lblHint = new TLabel("", Role.Muted, 9f) { AutoSize = false, AutoEllipsis = true };
        readonly FlatButton btnDelete = new FlatButton("Удалить отмеченные", BtnKind.Danger, Theme.GDelete);

        readonly System.Windows.Forms.Timer searchTimer = new System.Windows.Forms.Timer { Interval = 250 };
        readonly System.Windows.Forms.Timer fitTimer = new System.Windows.Forms.Timer { Interval = 60 };

        public MainForm(string folder)
        {
            cfg = AppSettings.Load();
            Theme.Apply(cfg.Theme);
            startFolder = folder;

            Text = "Чистая папка";
            Font = Theme.Ui(9.75f);
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(D(1280), D(800));
            MinimumSize = new Size(D(960), D(620));
            KeyPreview = true;
            DoubleBuffered = true;
            try
            {
                using (var s = typeof(MainForm).Assembly.GetManifestResourceStream("CleanFolder.app.ico"))
                    if (s != null) Icon = new Icon(s);
                using (var big = new Icon(Icon, 64, 64)) logo.Image = big.ToBitmap();
            }
            catch { }

            BuildUi();
            LoadWindowBounds();
            swRecycle.Checked = cfg.RecycleBin;
            lblVersion.Text = "Версия " + Updater.Current;
            BuildOverview();
            SetPage(Page.Overview);
            UpdateState();
            EnableDrop(this);
        }

        public override Color BackColor { get => Theme.P.Bg; set { } }
        public override Color ForeColor { get => Theme.P.Text; set { } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.TitleBar(this);
            Theme.Scrollbars(overviewFlow);
        }

        // =====================================================================
        // Разметка
        // =====================================================================

        void BuildUi()
        {
            SuspendLayout();

            // ----- Боковое меню -----
            sidebar.Dock = DockStyle.Left;
            sidebar.Width = D(236);
            sidebar.Controls.AddRange(new Control[] { logo, lblLogo, navOverview, navSug, navLarge, navTypes, navSettings, lblVersion });
            sidebar.Layout += (s, e) =>
            {
                int x = D(12), w = sidebar.Width - D(24), y = D(20);
                logo.SetBounds(x + D(6), y, D(30), D(30));
                lblLogo.Location = new Point(logo.Right + D(10), y + (D(30) - lblLogo.Height) / 2);
                y += D(60);
                foreach (var n in new[] { navOverview, navSug, navLarge, navTypes }) { n.SetBounds(x, y, w, D(40)); y += D(44); }
                lblVersion.Location = new Point(x + D(14), sidebar.Height - D(16) - lblVersion.Height);
                navSettings.SetBounds(x, lblVersion.Top - D(10) - D(40), w, D(40));
            };
            navOverview.Click += (s, e) => SetPage(Page.Overview);
            navSug.Click += (s, e) => { if (navSug.Enabled) SetPage(Page.Suggestions); };
            navLarge.Click += (s, e) => { if (navLarge.Enabled) SetPage(Page.Large); };
            navTypes.Click += (s, e) => { if (navTypes.Enabled) SetPage(Page.Types); };
            navSettings.Click += (s, e) => OpenSettings();
            lblVersion.Cursor = Cursors.Hand;
            lblVersion.Click += (s, e) => { try { Process.Start(Updater.RepoUrl); } catch { } };

            // ----- Основная область -----
            main.Dock = DockStyle.Fill;
            main.Padding = new Padding(D(24), D(16), D(24), 0);

            // Верх: путь и кнопки
            header.Dock = DockStyle.Top;
            header.Height = D(44);
            pathBox.Controls.Add(btnRecent);
            pathBox.RightReserve = D(36);
            pathBox.Layout += (s, e) => btnRecent.SetBounds(pathBox.Width - D(34), D(4), D(30), pathBox.Height - D(8));
            pathBox.Box.TextChanged += (s, e) => UpdateState();
            pathBox.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; StartScan(); } };
            btnRecent.Click += (s, e) => ShowRecentMenu();
            btnBrowse.Click += (s, e) => ChooseFolder();
            btnStart.Click += (s, e) => StartScan();
            btnStop.Click += (s, e) => cts?.Cancel();
            header.Controls.AddRange(new Control[] { pathBox, btnBrowse, btnStart, btnStop });
            header.Layout += (s, e) =>
            {
                int h = D(40), y = (header.Height - h) / 2, r = header.Width;
                btnStart.SetBounds(r - btnStart.Width, y, btnStart.Width, h);
                btnStop.SetBounds(r - btnStop.Width, y, btnStop.Width, h);
                int ar = (btnStop.Visible ? btnStop.Left : btnStart.Left) - D(8);
                btnBrowse.SetBounds(ar - btnBrowse.Width, y, btnBrowse.Width, h);
                pathBox.SetBounds(0, y, Math.Max(D(100), btnBrowse.Left - D(8)), h);
            };

            // Баннер
            bannerWrap.Dock = DockStyle.Top;
            bannerWrap.Height = D(66);
            bannerWrap.Padding = new Padding(0, D(12), 0, 0);
            bannerWrap.Visible = false;
            lblBannerIcon.Font = Theme.Icon(12f);
            banner.Dock = DockStyle.Fill;
            banner.Controls.AddRange(new Control[] { lblBannerIcon, lblBanner, btnBannerAction, btnBannerClose });
            banner.Layout += (s, e) =>
            {
                int h = banner.Height;
                btnBannerClose.SetBounds(banner.Width - D(40), (h - D(30)) / 2, D(30), D(30));
                btnBannerAction.Location = new Point(btnBannerClose.Left - D(8) - btnBannerAction.Width, (h - btnBannerAction.Height) / 2);
                lblBannerIcon.Location = new Point(D(16), (h - lblBannerIcon.Height) / 2);
                lblBanner.AutoSize = false;
                lblBanner.AutoEllipsis = true;
                lblBanner.SetBounds(D(46), (h - D(22)) / 2, Math.Max(D(50), (btnBannerAction.Visible ? btnBannerAction.Left : btnBannerClose.Left) - D(56)), D(22));
            };
            btnBannerAction.Click += (s, e) => bannerAction?.Invoke();
            btnBannerClose.Click += (s, e) => HideBanner();
            bannerWrap.Controls.Add(banner);

            // Строка состояния
            statusBar.Dock = DockStyle.Top;
            statusBar.Height = D(34);
            progress.Dock = DockStyle.Top;
            progress.Height = D(3);
            lnkStatus.Font = Theme.Ui(9.25f);
            lnkStatus.LinkClicked += (s, e) => statusLinkAction?.Invoke();
            statusBar.Controls.AddRange(new Control[] { lblStatus, lnkStatus, progress });
            statusBar.Layout += (s, e) =>
            {
                int y = D(10);
                int lw = lnkStatus.Visible ? lnkStatus.PreferredWidth : 0;
                lnkStatus.Location = new Point(statusBar.Width - lw, y);
                lblStatus.SetBounds(0, y, Math.Max(D(50), statusBar.Width - lw - (lw > 0 ? D(12) : 0)), D(20));
            };

            // Панель удаления
            actionBar.Dock = DockStyle.Bottom;
            actionBar.Height = D(68);
            actionBar.Controls.AddRange(new Control[] { swRecycle, lblSelected, lblHint, btnDelete });
            actionBar.Layout += (s, e) =>
            {
                int h = D(40);
                btnDelete.SetBounds(actionBar.Width - btnDelete.Width, (actionBar.Height - h) / 2 + 1, btnDelete.Width, h);
                swRecycle.Location = new Point(0, (actionBar.Height - swRecycle.Height) / 2 + 1);
                int x = swRecycle.Right + D(28);
                lblSelected.Location = new Point(x, D(14));
                lblHint.SetBounds(x, lblSelected.Bottom + D(2), Math.Max(D(50), btnDelete.Left - x - D(16)), D(18));
            };
            swRecycle.CheckedChanged += (s, e) => UpdateSelection();
            btnDelete.Click += (s, e) => DeleteChecked();

            // Страницы
            host.Dock = DockStyle.Fill;
            host.Padding = new Padding(0, D(4), 0, D(8));
            foreach (var p in new[] { pgOverview, pgSug, pgLarge, pgTypes }) { p.Dock = DockStyle.Fill; p.Visible = false; host.Controls.Add(p); }

            // Обзор
            overviewFlow.Dock = DockStyle.Fill;
            overviewFlow.FlowDirection = FlowDirection.TopDown;
            overviewFlow.WrapContents = false;
            overviewFlow.AutoScroll = true;
            overviewFlow.Padding = new Padding(0, D(4), D(4), D(12));
            overviewFlow.Resize += (s, e) => { fitTimer.Stop(); fitTimer.Start(); };
            fitTimer.Tick += (s, e) => { fitTimer.Stop(); FitOverview(); };
            pgOverview.Controls.Add(overviewFlow);

            // К удалению
            BuildListPage(pgSug, sugBar, sugSearch, sugChips, gridSug, new Control[] { btnMarkRec, btnMarkAll, btnMarkNone }, null);
            btnMarkRec.Click += (s, e) => gridSug.CheckWhere(x => x.Recommended, true);
            btnMarkAll.Click += (s, e) => gridSug.CheckWhere(x => true, true);
            btnMarkNone.Click += (s, e) => gridSug.CheckWhere(x => true, false, visibleOnly: false);
            gridSug.Filter = s => (sugCategory == null || s.Category == sugCategory) && (!sugOnlyChecked || gridSug.Checked.Contains(s)) && Matches(s, sugSearch.Text, true);

            // Большие файлы
            BuildListPage(pgLarge, largeBar, largeSearch, largeChips, gridLarge, new Control[] { lblLargeHint, btnLargeNone }, null);
            btnLargeNone.Click += (s, e) => gridLarge.CheckWhere(x => true, false, visibleOnly: false);
            gridLarge.Filter = s => (largeType == null || Scanner.TypeOf(Path.GetExtension(s.Path)) == largeType) && Matches(s, largeSearch.Text, false);

            foreach (var g in new[] { gridSug, gridLarge })
            {
                g.CheckedChanged += () => { UpdateSelection(); if (g == gridSug) navSug.Invalidate(); };
                g.ShowRequested += s => ShowInExplorer(s.Path);
                g.DeleteRequested += list => DeleteItems(list, null, list.Count > 1);
                g.ExcludeRequested += Exclude;
            }
            searchTimer.Tick += (s, e) => { searchTimer.Stop(); ApplyFilters(); };
            sugSearch.Box.TextChanged += (s, e) => { searchTimer.Stop(); searchTimer.Start(); };
            largeSearch.Box.TextChanged += (s, e) => { searchTimer.Stop(); searchTimer.Start(); };

            // Типы файлов
            SetupTypesGrid();
            var typesCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(D(6)) };
            typesCard.Controls.Add(gridTypes);
            var typesTop = new TPanel(Role.Bg) { Dock = DockStyle.Top, Height = D(32) };
            typesTop.Controls.Add(lblTypesHint);
            lblTypesHint.Location = new Point(0, D(4));
            pgTypes.Controls.Add(typesCard);
            pgTypes.Controls.Add(typesTop);

            main.Controls.Add(host);
            main.Controls.Add(actionBar);
            main.Controls.Add(statusBar);
            main.Controls.Add(bannerWrap);
            main.Controls.Add(header);

            Controls.Add(main);
            Controls.Add(sidebar);
            ResumeLayout();
        }

        void BuildListPage(TPanel pg, TPanel bar, InputBox search, FlowLayoutPanel chips, ItemGrid grid, Control[] rightControls, object unused)
        {
            bar.Dock = DockStyle.Top;
            bar.Height = D(88);
            chips.WrapContents = true;
            chips.BackColor = Color.Transparent;
            chips.Margin = Padding.Empty;
            chips.Padding = Padding.Empty;
            bar.Controls.Add(search);
            bar.Controls.Add(chips);
            bar.Controls.AddRange(rightControls);
            bar.Layout += (s, e) =>
            {
                int w = bar.Width;
                search.SetBounds(0, 0, Math.Min(D(380), Math.Max(D(200), w / 2 - D(40))), D(36));
                int r = w;
                for (int i = rightControls.Length - 1; i >= 0; i--)
                {
                    var c = rightControls[i];
                    if (!c.Visible) continue;
                    var y = c is FlatButton ? D(3) : (D(36) - c.Height) / 2;
                    c.Location = new Point(r - c.Width, y);
                    r = c.Left - D(4);
                }
                int ch = chips.Controls.Count == 0 ? 0 : chips.GetPreferredSize(new Size(w, 0)).Height;
                chips.SetBounds(0, D(48), w, ch);
                int h = D(48) + ch + D(4);
                if (bar.Height != h) bar.Height = h;
            };
            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(D(6)) };
            grid.Dock = DockStyle.Fill;
            card.Controls.Add(grid);
            pg.Controls.Add(card);
            pg.Controls.Add(bar);
        }

        void SetupTypesGrid()
        {
            gridTypes.Dock = DockStyle.Fill;
            gridTypes.Columns.Add(ThemedGrid.Col("Тип", 220));
            gridTypes.Columns.Add(ThemedGrid.Col("Файлов", 110, right: true));
            gridTypes.Columns.Add(ThemedGrid.Col("Размер", 120, right: true));
            gridTypes.Columns.Add(ThemedGrid.Col("Доля от общего размера", 300, fill: true));
            foreach (DataGridViewColumn c in gridTypes.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            gridTypes.CellPainting += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex != 3) return;
                e.PaintBackground(e.ClipBounds, (e.State & DataGridViewElementStates.Selected) != 0);
                double share = gridTypes.Rows[e.RowIndex].Tag is double d ? d : 0;
                var b = e.CellBounds;
                int tw = D(56);
                int bw = Math.Max(0, b.Width - tw - D(24));
                var track = new Rectangle(b.X + D(10), b.Y + (b.Height - D(10)) / 2, bw, D(10));
                Theme.FillRound(e.Graphics, track, D(5), Theme.P.GridLine);
                int fw = (int)Math.Round(bw * share);
                if (fw > 0) Theme.FillRound(e.Graphics, new Rectangle(track.X, track.Y, Math.Max(fw, D(10)), track.Height), D(5), Theme.P.Accent);
                TextRenderer.DrawText(e.Graphics, share.ToString("P0"), Theme.Ui(9.25f), new Rectangle(track.Right + D(8), b.Y, tw, b.Height), Theme.P.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
                e.Handled = true;
            };
            gridTypes.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                largeType = gridTypes.Rows[e.RowIndex].Cells[0].Value as string;
                if (largeType == "Прочее" && !gridLarge.All.Any(x => Scanner.TypeOf(Path.GetExtension(x.Path)) == "Прочее")) largeType = null;
                RebuildLargeChips();
                ApplyFilters();
                SetPage(Page.Large);
            };
        }

        // =====================================================================
        // Страницы и состояние
        // =====================================================================

        void SetPage(Page p)
        {
            page = p;
            pgOverview.Visible = p == Page.Overview;
            pgSug.Visible = p == Page.Suggestions;
            pgLarge.Visible = p == Page.Large;
            pgTypes.Visible = p == Page.Types;
            navOverview.Selected = p == Page.Overview;
            navSug.Selected = p == Page.Suggestions;
            navLarge.Selected = p == Page.Large;
            navTypes.Selected = p == Page.Types;
            if (p == Page.Overview) BuildOverview();
            UpdateSelection();
            if (p == Page.Suggestions) gridSug.Focus();
            else if (p == Page.Large) gridLarge.Focus();
        }

        ItemGrid CurrentGrid => page == Page.Suggestions ? gridSug : page == Page.Large ? gridLarge : null;

        void UpdateState()
        {
            bool scanning = busy && cts != null;
            btnStart.Visible = !scanning;
            btnStop.Visible = scanning;
            btnStart.Enabled = !busy && pathBox.Text.Trim().Length > 0;
            btnBrowse.Enabled = !busy;
            btnRecent.Enabled = !busy && cfg.Recent.Count > 0;
            pathBox.Box.ReadOnly = busy;
            bool has = result != null && !scanning;
            navSug.Enabled = navLarge.Enabled = navTypes.Enabled = has;
            navSug.Badge = has && gridSug.All.Count > 0 ? gridSug.All.Count.ToString("N0") : null;
            header.PerformLayout();
            UpdateSelection();
        }

        void UpdateSelection()
        {
            var g = CurrentGrid;
            bool show = g != null && result != null;
            if (actionBar.Visible != show) actionBar.Visible = show;
            if (!show) return;
            var items = g.CheckedItems();
            long size = items.Sum(s => s.Size);
            var visible = new HashSet<Suggestion>(g.View);
            int hidden = items.Count(s => !visible.Contains(s));
            lblSelected.Text = items.Count == 0 ? "Ничего не отмечено" : $"Отмечено: {Format.Items(items.Count)} · {Format.Size(size)}";
            lblHint.Text = items.Count == 0
                ? "Щёлкните по квадратику слева или протяните по ним мышью. Строки выделяются с Ctrl и Shift, Пробел отмечает выделенные."
                : (hidden > 0 ? $"Из них {hidden:N0} скрыто фильтром – они тоже будут удалены. " : "") +
                  (swRecycle.Checked ? "Файлы попадут в Корзину." : "Удаление насовсем, без Корзины!");
            lblHint.Role = items.Count > 0 && !swRecycle.Checked ? Role.Danger : Role.Muted;
            lblHint.Invalidate();
            btnDelete.Text = items.Count == 0 ? "Удалить отмеченные" : $"Удалить отмеченные ({items.Count:N0})";
            btnDelete.Enabled = !busy && items.Count > 0;
            actionBar.PerformLayout();
        }

        void SetStatus(string text, string link = null, Action linkAction = null)
        {
            lblStatus.Text = text;
            lnkStatus.Text = link ?? "";
            lnkStatus.Visible = link != null;
            lnkStatus.LinkColor = Theme.P.Accent;
            lnkStatus.ActiveLinkColor = Theme.P.AccentHover;
            statusLinkAction = linkAction;
            statusBar.PerformLayout();
        }

        void ShowBanner(string text, string actionText, Action action, string glyph = null, bool warning = false)
        {
            lblBanner.Text = text;
            lblBannerIcon.Text = warning ? Theme.GWarning : Theme.GInfo;
            lblBannerIcon.Role = warning ? Role.Warning : Role.Accent;
            btnBannerAction.Visible = actionText != null;
            if (actionText != null) { btnBannerAction.Text = actionText; btnBannerAction.Glyph = glyph; btnBannerAction.Enabled = true; }
            bannerAction = action;
            bannerWrap.Visible = true;
            banner.PerformLayout();
            banner.Invalidate(true);
        }

        void HideBanner() { bannerWrap.Visible = false; bannerAction = null; }

        // =====================================================================
        // Обзор
        // =====================================================================

        void BuildOverview()
        {
            overviewFlow.SuspendLayout();
            var old = overviewFlow.Controls.Cast<Control>().ToList();
            overviewFlow.Controls.Clear();
            foreach (var c in old) c.Dispose();

            if (result == null) overviewFlow.Controls.Add(WelcomeCard());
            else
            {
                var all = gridSug.All.ToList();
                overviewFlow.Controls.Add(TitleBlock());
                overviewFlow.Controls.Add(StatsBlock(all));
                if (all.Count > 0)
                {
                    overviewFlow.Controls.Add(ActionsBlock(all));
                    overviewFlow.Controls.Add(SectionTitle("Что найдено"));
                    foreach (var g in all.GroupBy(s => s.Category).OrderBy(g => g.Key))
                        overviewFlow.Controls.Add(CategoryCard(g.Key, g.ToList()));
                }
                else overviewFlow.Controls.Add(CleanCard());
                if (result.Types.Count > 0) overviewFlow.Controls.Add(SpaceCard());
            }
            overviewFlow.ResumeLayout();
            FitOverview();
        }

        void FitOverview()
        {
            int w = overviewFlow.ClientSize.Width - overviewFlow.Padding.Horizontal;
            if (w < D(200)) return;
            overviewFlow.SuspendLayout();
            foreach (Control c in overviewFlow.Controls) if (c is LayoutCard lc) lc.Fit(w);
            overviewFlow.ResumeLayout();
            int w2 = overviewFlow.ClientSize.Width - overviewFlow.Padding.Horizontal;
            if (w2 != w && w2 >= D(200)) foreach (Control c in overviewFlow.Controls) if (c is LayoutCard lc) lc.Fit(w2);
        }

        LayoutCard WelcomeCard()
        {
            var c = new LayoutCard();
            var t = new TLabel("Наведите порядок в папке", Role.Text, 17f, true);
            var sub = TLabel.Wrap("Программа просмотрит все файлы и вложенные папки и подскажет, что можно убрать: временные файлы, дубликаты, пустые файлы и папки, старые установщики и забытые большие файлы. Ничего не удаляется без вашей команды, по умолчанию – в Корзину.", Role.Muted, 10.5f);
            var steps = TLabel.Wrap("1.  Выберите папку кнопкой ниже, вставьте путь сверху или перетащите папку в окно.\n2.  Нажмите «Анализировать».\n3.  Посмотрите итоги, отметьте лишнее и удалите – целыми разделами или по одному файлу.", Role.Text, 10.5f);
            var choose = new FlatButton("Выбрать папку", BtnKind.Primary, Theme.GFolderOpen);
            choose.Click += (s, e) => ChooseFolder();
            c.Controls.AddRange(new Control[] { t, sub, steps, choose });
            var recent = cfg.Recent.Where(Directory.Exists).Take(3).ToList();
            var recentLbl = new TLabel("Недавние:", Role.Muted, 9.25f);
            var recentBtns = recent.Select(r =>
            {
                var b = new FlatButton(ShortName(r), BtnKind.Secondary, Theme.GHistory, compact: true);
                b.Click += (s, e) => { pathBox.Text = r; StartScan(); };
                return b;
            }).ToList();
            if (recent.Count > 0) { c.Controls.Add(recentLbl); c.Controls.AddRange(recentBtns.ToArray()); }
            var tip = TLabel.Wrap("Совет: в Проводнике можно щёлкнуть по папке правой кнопкой мыши и выбрать «Навести порядок».", Role.Muted, 9.25f);
            c.Controls.Add(tip);
            c.Arrange = w =>
            {
                int x = D(32), iw = w - 2 * x, y = D(30);
                t.Location = new Point(x, y);
                y = sub.Place(x, t.Bottom + D(8), iw) + D(16);
                y = steps.Place(x, y, iw) + D(22);
                choose.Location = new Point(x, y);
                y = choose.Bottom + D(18);
                if (recent.Count > 0)
                {
                    recentLbl.Location = new Point(x, y + D(6));
                    int bx = recentLbl.Right + D(8);
                    foreach (var b in recentBtns)
                    {
                        if (bx + b.Width > w - x && bx > recentLbl.Right + D(8)) { bx = recentLbl.Right + D(8); y += b.Height + D(6); }
                        b.Location = new Point(bx, y);
                        bx = b.Right + D(6);
                    }
                    y += recentBtns[0].Height + D(18);
                }
                y = tip.Place(x, y, iw);
                return y + D(30);
            };
            return c;
        }

        static string ShortName(string path)
        {
            var name = Path.GetFileName(path.TrimEnd('\\'));
            if (string.IsNullOrEmpty(name)) name = path;
            return name.Length > 40 ? name.Substring(0, 38) + "…" : name;
        }

        LayoutCard TitleBlock()
        {
            var c = new LayoutCard(plain: true);
            var t = new TLabel("Итоги анализа", Role.Text, 16f, true);
            var p = new TLabel(result.Root, Role.Muted, 9.5f) { AutoSize = false, AutoEllipsis = true };
            c.Controls.AddRange(new Control[] { t, p });
            c.Arrange = w =>
            {
                t.Location = new Point(0, 0);
                p.SetBounds(0, t.Bottom + D(2), w, D(20));
                return p.Bottom + D(4);
            };
            return c;
        }

        LayoutCard SectionTitle(string text)
        {
            var c = new LayoutCard(plain: true) { Margin = new Padding(0, D(6), 0, D(8)) };
            var t = new TLabel(text, Role.Text, 12.5f, true);
            c.Controls.Add(t);
            c.Arrange = w => { t.Location = Point.Empty; return t.Height; };
            return c;
        }

        LayoutCard StatsBlock(List<Suggestion> all)
        {
            var c = new LayoutCard(plain: true);
            long possible = all.Sum(s => s.Size);
            long safe = all.Where(s => s.Recommended).Sum(s => s.Size);
            var tiles = new[]
            {
                Tile("Проанализировано", Format.Files(result.Files), $"{Format.Count(result.Folders, "папка", "папки", "папок")} · {Format.Size(result.TotalSize)}", null),
                Tile("Предложено к удалению", all.Count == 0 ? "Ничего" : Format.Items(all.Count), all.Count == 0 ? "здесь порядок" : Format.Size(possible), null),
                Tile("Можно освободить безопасно", Format.Size(safe), "временные, дубликаты, пустые", () => Theme.P.Accent),
                Tile("Время анализа", Format.Duration(result.Elapsed), result.Errors > 0 ? $"нет доступа: {Format.Items(result.Errors)}" : "все папки доступны", null),
            };
            c.Controls.AddRange(tiles);
            c.Arrange = w =>
            {
                int gap = D(12);
                int cols = w < D(760) ? 2 : 4;
                int tw = (w - gap * (cols - 1)) / cols, th = D(104);
                for (int i = 0; i < tiles.Length; i++)
                {
                    tiles[i].SetBounds((i % cols) * (tw + gap), (i / cols) * (th + gap), tw, th);
                    tiles[i].Fit(tw);
                }
                return ((tiles.Length + cols - 1) / cols) * (th + gap) - gap;
            };
            return c;
        }

        LayoutCard Tile(string caption, string value, string sub, Func<Color> valueColor)
        {
            var c = new LayoutCard { Margin = Padding.Empty };
            var l1 = new TLabel(caption, Role.Muted, 9.25f);
            var l2 = new TLabel(value, Role.Text, 17f, true) { Custom = valueColor };
            var l3 = new TLabel(sub, Role.Muted, 9f) { AutoSize = false, AutoEllipsis = true };
            c.Controls.AddRange(new Control[] { l1, l2, l3 });
            c.Arrange = w =>
            {
                l1.Location = new Point(D(18), D(16));
                l2.Location = new Point(D(16), l1.Bottom + D(4));
                l3.SetBounds(D(18), l2.Bottom + D(2), w - D(30), D(18));
                return D(104);
            };
            return c;
        }

        LayoutCard ActionsBlock(List<Suggestion> all)
        {
            var c = new LayoutCard(plain: true) { Margin = new Padding(0, 0, 0, D(18)) };
            var safe = all.Where(s => s.Recommended).ToList();
            var del = new FlatButton(safe.Count > 0 ? $"Удалить всё безопасное · {Format.Size(safe.Sum(s => s.Size))}" : "Безопасного к удалению нет", BtnKind.Danger, Theme.GDelete);
            del.Enabled = safe.Count > 0 && !busy;
            del.Click += (s, e) => DeleteItems(gridSug.All.Where(x => x.Recommended).ToList(), "Удалить всё безопасное: временные файлы, копии-дубликаты, пустые файлы и папки.", true);
            var open = new FlatButton("Открыть список", BtnKind.Secondary, Theme.GList);
            open.Click += (s, e) => { sugCategory = null; RebuildSugChips(); ApplyFilters(); SetPage(Page.Suggestions); };
            var hint = TLabel.Wrap("Установщики и большие старые файлы в «безопасное» не входят – их удаляйте сами, если уверены, что они не нужны.", Role.Muted, 9.25f);
            c.Controls.AddRange(new Control[] { del, open, hint });
            c.Arrange = w =>
            {
                del.Location = Point.Empty;
                open.Location = new Point(del.Right + D(10), 0);
                return hint.Place(0, del.Bottom + D(10), w);
            };
            return c;
        }

        LayoutCard CategoryCard(Category cat, List<Suggestion> items)
        {
            var c = new LayoutCard { Stripe = Theme.CategoryColor(cat) };
            long size = items.Sum(s => s.Size);
            int marked = items.Count(gridSug.Checked.Contains);
            var title = new TLabel(Categories.Title(cat), Role.Text, 12f, true) { Custom = () => Theme.CategoryColor(cat) };
            var meta = new TLabel(Format.Items(items.Count) + (cat == Category.EmptyFolder ? "" : " · " + Format.Size(size)) +
                                  (marked == items.Count ? " · все отмечены" : marked == 0 ? " · не отмечены" : $" · отмечено {marked:N0}"), Role.Muted, 9.25f);
            var adv = TLabel.Wrap(Categories.Advice(cat), Role.Text, 9.75f);
            var bList = new FlatButton("Открыть список", BtnKind.Secondary, Theme.GList, compact: true);
            var bDel = new FlatButton("Удалить раздел", BtnKind.DangerOutline, Theme.GDelete, compact: true);
            bDel.Enabled = !busy;
            bList.Click += (s, e) => { sugCategory = cat; RebuildSugChips(); ApplyFilters(); SetPage(Page.Suggestions); };
            bDel.Click += (s, e) => DeleteItems(gridSug.All.Where(x => x.Category == cat).ToList(),
                $"Раздел «{Categories.Title(cat)}»" + (cat == Category.Installer || cat == Category.OldLarge ? " – эти файлы не отмечены как безопасные, проверьте список перед удалением." : "."), true);
            c.Controls.AddRange(new Control[] { title, meta, adv, bList, bDel });
            c.Arrange = w =>
            {
                int x = D(26);
                int bw = Math.Max(bList.PreferredWidth, bDel.PreferredWidth);
                int bx = w - D(18) - bw;
                bList.SetBounds(bx, D(18), bw, bList.Height);
                bDel.SetBounds(bx, bList.Bottom + D(8), bw, bDel.Height);
                title.Location = new Point(x, D(16));
                meta.Location = new Point(x + D(1), title.Bottom + D(2));
                int y = adv.Place(x + D(1), meta.Bottom + D(8), bx - x - D(24));
                return Math.Max(y + D(18), bDel.Bottom + D(18));
            };
            return c;
        }

        LayoutCard CleanCard()
        {
            var c = new LayoutCard { Stripe = Theme.P.Accent };
            var t = new TLabel("Мусора не найдено – здесь порядок", Role.Accent, 12f, true);
            var s = TLabel.Wrap("Если места всё равно мало, посмотрите «Большие файлы» и «Типы файлов»: возможно, что-то стоит перенести в архив или на внешний диск.", Role.Text, 9.75f);
            c.Controls.AddRange(new Control[] { t, s });
            c.Arrange = w =>
            {
                t.Location = new Point(D(26), D(18));
                return s.Place(D(27), t.Bottom + D(8), w - D(50)) + D(20);
            };
            return c;
        }

        LayoutCard SpaceCard()
        {
            var c = new LayoutCard { Margin = new Padding(0, D(6), 0, D(12)) };
            var top = result.Types.First();
            var t = new TLabel("Где больше всего места", Role.Text, 11.5f, true);
            var s = TLabel.Wrap(string.Join("   ·   ", result.Types.Take(4).Select(x => $"{x.Name}: {Format.Size(x.Size)}")), Role.Muted, 9.75f);
            var b1 = new FlatButton("Большие файлы", BtnKind.Secondary, Theme.GPage, compact: true);
            var b2 = new FlatButton("Типы файлов", BtnKind.Ghost, Theme.GPie, compact: true);
            b1.Click += (o, e) => SetPage(Page.Large);
            b2.Click += (o, e) => SetPage(Page.Types);
            c.Controls.AddRange(new Control[] { t, s, b1, b2 });
            c.Arrange = w =>
            {
                b2.Location = new Point(w - D(18) - b2.Width, D(18));
                b1.Location = new Point(b2.Left - D(6) - b1.Width, D(18));
                t.Location = new Point(D(24), D(16));
                int y = s.Place(D(25), t.Bottom + D(6), b1.Left - D(50));
                return Math.Max(y, b1.Bottom) + D(18);
            };
            return c;
        }

        // =====================================================================
        // Фильтры
        // =====================================================================

        static bool Matches(Suggestion s, string query, bool withReason)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            foreach (var word in query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                bool hit = s.Path.IndexOf(word, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                           (withReason && (s.Reason ?? "").IndexOf(word, StringComparison.CurrentCultureIgnoreCase) >= 0);
                if (!hit) return false;
            }
            return true;
        }

        void ApplyFilters()
        {
            gridSug.EmptyText = gridSug.All.Count == 0 ? (result == null ? "" : "Мусора не найдено – здесь порядок") : "Ничего не подходит под фильтр";
            gridLarge.EmptyText = gridLarge.All.Count == 0 ? "Файлов нет" : "Ничего не подходит под фильтр";
            gridSug.Refilter();
            gridLarge.Refilter();
            UpdateSelection();
        }

        void RebuildSugChips()
        {
            sugChips.SuspendLayout();
            foreach (Control c in sugChips.Controls.Cast<Control>().ToList()) { sugChips.Controls.Remove(c); c.Dispose(); }
            var all = gridSug.All;
            if (sugCategory != null && !all.Any(s => s.Category == sugCategory)) sugCategory = null;
            var chipAll = new ChipButton($"Все  {all.Count:N0}") { Checked = sugCategory == null };
            chipAll.Click += (s, e) => { sugCategory = null; RebuildSugChips(); ApplyFilters(); };
            sugChips.Controls.Add(chipAll);
            foreach (var g in all.GroupBy(s => s.Category).OrderBy(g => g.Key))
            {
                var cat = g.Key;
                var chip = new ChipButton($"{Categories.Title(cat)}  {g.Count():N0}", Theme.CategoryColor(cat)) { Checked = sugCategory == cat };
                chip.Click += (s, e) => { sugCategory = sugCategory == cat ? (Category?)null : cat; RebuildSugChips(); ApplyFilters(); };
                sugChips.Controls.Add(chip);
            }
            var onlyChecked = new ChipButton("Только отмеченные") { Checked = sugOnlyChecked, Margin = new Padding(D(14), 0, D(6), D(6)) };
            onlyChecked.Click += (s, e) => { sugOnlyChecked = !sugOnlyChecked; onlyChecked.Checked = sugOnlyChecked; ApplyFilters(); };
            sugChips.Controls.Add(onlyChecked);
            sugChips.ResumeLayout();
            sugBar.PerformLayout();
        }

        void RebuildLargeChips()
        {
            largeChips.SuspendLayout();
            foreach (Control c in largeChips.Controls.Cast<Control>().ToList()) { largeChips.Controls.Remove(c); c.Dispose(); }
            var groups = gridLarge.All.GroupBy(s => Scanner.TypeOf(Path.GetExtension(s.Path))).OrderByDescending(g => g.Sum(x => x.Size)).ToList();
            if (largeType != null && !groups.Any(g => g.Key == largeType)) largeType = null;
            var chipAll = new ChipButton($"Все  {gridLarge.All.Count:N0}") { Checked = largeType == null };
            chipAll.Click += (s, e) => { largeType = null; RebuildLargeChips(); ApplyFilters(); };
            largeChips.Controls.Add(chipAll);
            foreach (var g in groups)
            {
                var type = g.Key;
                var chip = new ChipButton($"{type}  {g.Count():N0}") { Checked = largeType == type };
                chip.Click += (s, e) => { largeType = largeType == type ? null : type; RebuildLargeChips(); ApplyFilters(); };
                largeChips.Controls.Add(chip);
            }
            largeChips.ResumeLayout();
            largeBar.PerformLayout();
        }

        // =====================================================================
        // Анализ
        // =====================================================================

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (ShotPrefix != null) { pathBox.Text = startFolder; StartScan(); return; }
            if (!cfg.ContextMenuInitialized)
            {
                cfg.ContextMenuInitialized = true;
                cfg.Save();
            }
            if (cfg.ContextMenu) ShellMenu.Set(true);   // обновляет путь к программе после установки/обновления

            if (!string.IsNullOrEmpty(startFolder) && Directory.Exists(startFolder))
            {
                pathBox.Text = startFolder;
                StartScan();
            }
            else
            {
                if (!string.IsNullOrEmpty(cfg.LastFolder) && Directory.Exists(cfg.LastFolder)) pathBox.Text = cfg.LastFolder;
                SetStatus(pathBox.Text.Length > 0 ? "Нажмите «Анализировать» или выберите другую папку." : "Выберите папку, в которой нужно навести порядок.");
                btnStart.Focus();
            }
            CheckUpdatesAuto();
        }

        void ChooseFolder()
        {
            if (busy) return;
            var path = FolderPicker.Pick(this, "Выберите папку, в которой нужно навести порядок", pathBox.Text.Length > 0 ? pathBox.Text : cfg.LastFolder);
            if (string.IsNullOrEmpty(path)) return;
            pathBox.Text = path;
            StartScan();
        }

        void ShowRecentMenu()
        {
            var m = Menus.Create();
            foreach (var r in cfg.Recent)
            {
                var path = r;
                var it = Menus.Item(m, path, () => { pathBox.Text = path; StartScan(); }, enabled: Directory.Exists(path));
            }
            if (cfg.Recent.Count > 0)
            {
                m.Items.Add(new ToolStripSeparator());
                Menus.Item(m, "Очистить историю", () => { cfg.Recent.Clear(); cfg.Save(); UpdateState(); });
            }
            m.Closed += (s, e) => BeginInvoke(new Action(m.Dispose));
            m.Show(pathBox, new Point(0, pathBox.Height + D(2)));
        }

        static string Normalize(string raw)
        {
            var p = (raw ?? "").Trim().Trim('"').Trim();
            if (p.Length == 2 && p[1] == ':') p += "\\";
            return p;
        }

        async void StartScan()
        {
            if (busy) return;
            var folder = Normalize(pathBox.Text);
            if (folder.Length == 0) { ChooseFolder(); return; }
            if (!Directory.Exists(folder))
            {
                MessageBox.Show(this, "Такой папки нет. Выберите существующую папку.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            folder = Path.GetFullPath(folder);
            if (IsSystemFolder(folder) &&
                MessageBox.Show(this, "Это системная папка или корень диска. Анализ может занять много времени, а удаление файлов здесь может навредить Windows и программам.\n\nВсё равно продолжить?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            pathBox.Text = folder;
            cfg.AddRecent(folder);
            cfg.Save();
            if (bannerAction == (Action)StartScan) HideBanner();

            cts = new CancellationTokenSource();
            busy = true;
            progress.Value = -1;
            progress.Running = true;
            SetStatus("Идёт анализ…");
            UpdateState();

            var prog = new Progress<string>(t => lblStatus.Text = t);
            try
            {
                var token = cts.Token;
                var opts = cfg.ToScanOptions();
                var r = await Task.Run(() => new Scanner().Scan(folder, opts, prog, token));
                result = r;
                resultSignature = cfg.ScanSignature();
                LoadResult();
                if (ShotPrefix != null) BeginInvoke(new Action(TakeShots));
                SetStatus((result.Elapsed.TotalSeconds < 1 ? "Готово." : "Готово за " + Format.Duration(result.Elapsed) + ".") + (result.Errors > 0 ? $" Нет доступа к {Format.Items(result.Errors)} – они пропущены." : ""));
            }
            catch (OperationCanceledException) { SetStatus("Анализ остановлен."); }
            catch (Exception ex) { SetStatus("Ошибка: " + ex.Message); }
            finally
            {
                busy = false;
                progress.Running = false;
                cts?.Dispose();
                cts = null;
                UpdateState();
                if (page == Page.Overview) BuildOverview();
            }
        }

        void LoadResult()
        {
            gridSug.Root = gridLarge.Root = result.Root;
            sugCategory = null; sugOnlyChecked = false; largeType = null;
            sugSearch.Text = ""; largeSearch.Text = "";
            searchTimer.Stop();
            gridSug.SetItems(result.Suggestions, checkRecommended: true);

            var bySug = result.Suggestions.Where(s => !s.IsFolder).GroupBy(s => s.Path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var now = DateTime.Now;
            var large = result.Largest.Select(f =>
            {
                string hint;
                if (bySug.TryGetValue(f.Path, out var s)) hint = $"Есть в списке «К удалению»: {Categories.Title(s.Category)}";
                else
                {
                    int age = (int)(now - f.Modified).TotalDays;
                    hint = age > 365 ? $"Не менялся {age / 365} г. {age % 365 / 30} мес." : age > 60 ? $"Не менялся {age / 30} мес." : "";
                }
                return new Suggestion { Path = f.Path, Size = f.Size, Modified = f.Modified, Category = Category.OldLarge, Recommended = false, Reason = hint };
            });
            gridLarge.SetItems(large, checkRecommended: false);

            gridTypes.Rows.Clear();
            foreach (var t in result.Types)
            {
                double share = result.TotalSize > 0 ? (double)t.Size / result.TotalSize : 0;
                int i = gridTypes.Rows.Add(t.Name, t.Count.ToString("N0"), Format.Size(t.Size), "");
                gridTypes.Rows[i].Tag = share;
            }
            gridTypes.ClearSelection();

            RebuildSugChips();
            RebuildLargeChips();
            ApplyFilters();
            SetPage(Page.Overview);
        }

        static bool IsSystemFolder(string path)
        {
            var p = path.TrimEnd('\\') + "\\";
            if (Path.GetPathRoot(path).TrimEnd('\\') + "\\" == p) return true;
            string[] sys =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            };
            foreach (var s in sys.Where(x => !string.IsNullOrEmpty(x)))
            {
                var sp = s.TrimEnd('\\') + "\\";
                // сама папка профиля – да, а её подпапки (Загрузки, Документы) – обычные
                if (s == sys[4] ? p.Equals(sp, StringComparison.OrdinalIgnoreCase) : p.StartsWith(sp, StringComparison.OrdinalIgnoreCase)) return true;
            }
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData") + "\\";
            return p.StartsWith(appData, StringComparison.OrdinalIgnoreCase) && !p.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase);
        }

        // =====================================================================
        // Удаление и исключения
        // =====================================================================

        void DeleteChecked()
        {
            var g = CurrentGrid;
            if (g == null) return;
            var items = g.CheckedItems();
            if (items.Count == 0) return;
            DeleteItems(items, null, true);
        }

        async void DeleteItems(List<Suggestion> items, string title, bool forceConfirm)
        {
            if (busy || items == null || items.Count == 0) return;
            bool recycle = swRecycle.Checked;
            long size = items.Sum(s => s.Size);
            string what = items.Count == 1
                ? $"«{Path.GetFileName(items[0].Path)}»" + (items[0].IsFolder ? " (папка)" : $" ({Format.Size(size)})")
                : $"{Format.Items(items.Count)} общим размером {Format.Size(size)}";
            if (forceConfirm || items.Count > 1 || !recycle || cfg.ConfirmSingle)
            {
                var msg = (title != null ? title + "\n\n" : "") + $"Удалить {what}?\n\n" +
                          (recycle ? "Всё попадёт в Корзину – при необходимости можно восстановить." : "Внимание: удаление без Корзины, восстановить будет нельзя!");
                if (MessageBox.Show(this, msg, Text, MessageBoxButtons.YesNo, recycle ? MessageBoxIcon.Question : MessageBoxIcon.Warning,
                        recycle ? MessageBoxDefaultButton.Button1 : MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
            }

            busy = true;
            UpdateState();
            progress.Running = false;
            progress.Value = 0;
            SetStatus("Удаляю…");
            Deleter.Outcome o;
            try
            {
                var prog = new Progress<int>(p => { progress.Value = Math.Max(0, Math.Min(100, p)); lblStatus.Text = $"Удаляю… {p}%"; });
                o = await Task.Run(() => Deleter.Delete(items, recycle, prog));
            }
            catch (Exception ex)
            {
                busy = false; progress.Value = -1; UpdateState();
                SetStatus("Ошибка удаления: " + ex.Message);
                return;
            }
            busy = false;
            progress.Value = -1;

            var gone = o.Failed.Where(kv => kv.Value.StartsWith("Файла уже нет") || kv.Value.StartsWith("Папки уже нет")).Select(kv => kv.Key).ToList();
            var removed = o.Deleted.Concat(gone).Select(s => s.Path).ToList();
            var failed = o.Failed.Where(kv => !gone.Contains(kv.Key)).ToList();
            foreach (var kv in failed)
                foreach (var g in new[] { gridSug, gridLarge })
                {
                    var same = g.All.Where(x => string.Equals(x.Path, kv.Key.Path, StringComparison.OrdinalIgnoreCase)).ToList();
                    foreach (var s in same) { g.Errors[s] = kv.Value; g.Checked.Remove(s); }
                }
            gridSug.Remove(removed);
            gridLarge.Remove(removed);
            RebuildSugChips();
            RebuildLargeChips();
            ApplyFilters();
            UpdateState();
            if (page == Page.Overview) BuildOverview();

            var text = $"Удалено: {Format.Items(o.Deleted.Count)}, освобождено {Format.Size(o.Freed)}" + (recycle ? " (в Корзине)." : ".") +
                       (failed.Count > 0 ? $" Не получилось: {failed.Count:N0} – причина в списке, строка выделена красным." : "");
            if (recycle && o.Deleted.Count > 0) SetStatus(text, "Открыть Корзину", () => { try { Process.Start("explorer.exe", "shell:RecycleBinFolder"); } catch { } });
            else SetStatus(text);
            if (failed.Count > 0)
                MessageBox.Show(this, text + "\n\n" + string.Join("\n", failed.Take(5).Select(kv => $"• {Path.GetFileName(kv.Key.Path)} – {kv.Value}")) + (failed.Count > 5 ? "\n…" : ""),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        void Exclude(string path)
        {
            if (busy) return;
            if (!cfg.Exclusions.Any(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase))) cfg.Exclusions.Add(path);
            cfg.Save();
            resultSignature = cfg.ScanSignature();
            gridSug.Remove(new[] { path });
            gridLarge.Remove(new[] { path });
            RebuildSugChips();
            RebuildLargeChips();
            ApplyFilters();
            UpdateState();
            SetStatus($"«{Path.GetFileName(path)}» больше не предлагается. Список исключений – в настройках.", "Настройки", OpenSettings);
        }

        static void ShowInExplorer(string path)
        {
            try
            {
                if (File.Exists(path) || Directory.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
                else Process.Start("explorer.exe", $"\"{Path.GetDirectoryName(path)}\"");
            }
            catch { }
        }

        // =====================================================================
        // Настройки, тема, обновления
        // =====================================================================

        void OpenSettings()
        {
            var oldTheme = cfg.Theme;
            var oldShell = cfg.ContextMenu;
            var oldRecycle = cfg.RecycleBin;
            using (var f = new SettingsForm(cfg, CheckUpdatesManual) { Icon = Icon })
                if (f.ShowDialog(this) != DialogResult.OK) return;
            cfg.Save();
            if (cfg.Theme != oldTheme) { Theme.Apply(cfg.Theme); ApplyTheme(); }
            if (cfg.ContextMenu != oldShell) ShellMenu.Set(cfg.ContextMenu);
            if (cfg.RecycleBin != oldRecycle) swRecycle.Checked = cfg.RecycleBin;
            if (result != null && cfg.ScanSignature() != resultSignature)
                ShowBanner("Настройки поиска изменились. Запустите анализ заново, чтобы их применить.", "Анализировать", StartScan, Theme.GRefresh);
        }

        void ApplyTheme()
        {
            Theme.Refresh(this);
            lnkStatus.LinkColor = Theme.P.Accent;
            lnkStatus.ActiveLinkColor = Theme.P.AccentHover;
            Theme.Scrollbars(overviewFlow);
            BuildOverview();
            RebuildSugChips();
            RebuildLargeChips();
            Invalidate(true);
        }

        async void CheckUpdatesAuto()
        {
            if (!cfg.AutoCheckUpdates) return;
            await Task.Delay(2000);
            var (info, _) = await Updater.CheckAsync();
            if (info == null || IsDisposed) return;
            update = info;
            if (cfg.AutoInstallUpdates)
            {
                var file = await Updater.DownloadAsync(info);
                if (IsDisposed) return;
                if (file != null)
                {
                    updateFile = file;
                    ShowBanner($"Загружена новая версия {info.Version}. Она установится, когда вы закроете программу.", "Обновить сейчас", () => InstallNow(), Theme.GRefresh);
                    return;
                }
            }
            ShowBanner($"Доступна новая версия «Чистой папки» – {info.Version}.", "Обновить", () => InstallNow(), Theme.GDownload);
        }

        async Task<string> CheckUpdatesManual()
        {
            var (info, err) = await Updater.CheckAsync();
            if (err != null) return "Не удалось проверить: " + err;
            if (info == null) return $"Установлена последняя версия ({Updater.Current}).";
            update = info;
            updateFile = null;
            ShowBanner($"Доступна новая версия «Чистой папки» – {info.Version}.", "Обновить", () => InstallNow(), Theme.GDownload);
            return $"Доступна версия {info.Version}. Кнопка «Обновить» – в главном окне.";
        }

        async void InstallNow()
        {
            if (update == null) return;
            if (busy)
            {
                MessageBox.Show(this, "Дождитесь окончания анализа или удаления, затем обновите программу.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (updateFile == null || !File.Exists(updateFile))
            {
                btnBannerAction.Enabled = false;
                lblBanner.Text = $"Скачиваю версию {update.Version}…";
                updateFile = await Updater.DownloadAsync(update);
                btnBannerAction.Enabled = true;
                if (updateFile == null)
                {
                    lblBanner.Text = "Не удалось скачать обновление. Попробуйте позже.";
                    return;
                }
            }
            if (Updater.RunInstaller(updateFile, update.Version, relaunch: true))
            {
                updateStarted = true;
                Close();
            }
        }

        // =====================================================================
        // Окно: клавиши, перетаскивание, размеры
        // =====================================================================

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F5: if (!busy) StartScan(); return true;
                case Keys.Control | Keys.O: ChooseFolder(); return true;
                case Keys.Control | Keys.F:
                    if (page == Page.Suggestions) { sugSearch.Box.Focus(); return true; }
                    if (page == Page.Large) { largeSearch.Box.Focus(); return true; }
                    break;
                case Keys.Control | Keys.D1: SetPage(Page.Overview); return true;
                case Keys.Control | Keys.D2: if (navSug.Enabled) SetPage(Page.Suggestions); return true;
                case Keys.Control | Keys.D3: if (navLarge.Enabled) SetPage(Page.Large); return true;
                case Keys.Control | Keys.D4: if (navTypes.Enabled) SetPage(Page.Types); return true;
                case Keys.Escape:
                    if (cts != null) { cts.Cancel(); return true; }
                    if (sugSearch.Box.Focused && sugSearch.Text.Length > 0) { sugSearch.Text = ""; return true; }
                    if (largeSearch.Box.Focused && largeSearch.Text.Length > 0) { largeSearch.Text = ""; return true; }
                    break;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void EnableDrop(Control c)
        {
            c.AllowDrop = true;
            c.DragEnter += OnDragEnterAny;
            c.DragDrop += OnDragDropAny;
            foreach (Control ch in c.Controls) EnableDrop(ch);
            c.ControlAdded += (s, e) => EnableDrop(e.Control);
        }

        static string DroppedFolder(DragEventArgs e)
        {
            if (!(e.Data?.GetData(DataFormats.FileDrop) is string[] files) || files.Length == 0) return null;
            var p = files[0];
            if (Directory.Exists(p)) return p;
            if (File.Exists(p)) return Path.GetDirectoryName(p);
            return null;
        }

        void OnDragEnterAny(object sender, DragEventArgs e) => e.Effect = !busy && DroppedFolder(e) != null ? DragDropEffects.Copy : DragDropEffects.None;

        void OnDragDropAny(object sender, DragEventArgs e)
        {
            var p = DroppedFolder(e);
            if (p == null || busy) return;
            pathBox.Text = p;
            Activate();
            BeginInvoke(new Action(StartScan));
        }

        void LoadWindowBounds()
        {
            var b = cfg.Bounds;
            if (b.Width >= MinimumSize.Width && b.Height >= MinimumSize.Height && Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(b)))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = b;
            }
            if (cfg.Maximized) WindowState = FormWindowState.Maximized;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            cts?.Cancel();
            cfg.Maximized = WindowState == FormWindowState.Maximized;
            cfg.Bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            cfg.Save();
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (!updateStarted && updateFile != null && update != null && cfg.AutoInstallUpdates && File.Exists(updateFile))
                Updater.RunInstaller(updateFile, update.Version, relaunch: false);
        }
    }
}
