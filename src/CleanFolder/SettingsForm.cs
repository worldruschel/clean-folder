using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CleanFolder
{
    /// <summary>Окно настроек: что искать, пороги, исключения, удаление, вид, обновления, Проводник.</summary>
    public sealed class SettingsForm : Form
    {
        readonly AppSettings s;
        readonly Func<Task<string>> checkUpdates;
        readonly FlowLayoutPanel flow = new FlowLayoutPanel();
        readonly List<Action> apply = new List<Action>();
        ThemeMode theme;

        public SettingsForm(AppSettings settings, Func<Task<string>> checkUpdates)
        {
            s = settings;
            this.checkUpdates = checkUpdates;
            theme = s.Theme;
            Text = "Настройки – Чистая папка";
            Font = Theme.Ui(9.75f);
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(Theme.D(640), Theme.D(720));
            MinimumSize = new Size(Theme.D(520), Theme.D(420));
            KeyPreview = true;
            Icon = Owner?.Icon;

            var bottom = new TPanel(Role.Surface) { Dock = DockStyle.Bottom, Height = Theme.D(64), BorderTop = true };
            var ok = new FlatButton("Сохранить", BtnKind.Primary);
            var cancel = new FlatButton("Отмена", BtnKind.Secondary);
            var reset = new FlatButton("По умолчанию", BtnKind.Ghost);
            ok.Click += (o, e) => { foreach (var a in apply) a(); s.Theme = theme; DialogResult = DialogResult.OK; };
            cancel.Click += (o, e) => DialogResult = DialogResult.Cancel;
            reset.Click += (o, e) => ResetDefaults();
            bottom.Controls.AddRange(new Control[] { ok, cancel, reset });
            bottom.Layout += (o, e) =>
            {
                int y = (bottom.Height - ok.Height) / 2, r = bottom.Width - Theme.D(20);
                cancel.Location = new Point(r - cancel.Width, y);
                ok.Location = new Point(cancel.Left - Theme.D(8) - ok.Width, y);
                reset.Location = new Point(Theme.D(16), y);
            };

            flow.Dock = DockStyle.Fill;
            flow.FlowDirection = FlowDirection.TopDown;
            flow.WrapContents = false;
            flow.AutoScroll = true;
            flow.Padding = new Padding(Theme.D(24), Theme.D(12), Theme.D(24), Theme.D(16));
            flow.Resize += (o, e) => FitWidths();

            Controls.Add(flow);
            Controls.Add(bottom);
            Build();
            AcceptButton = null;
            KeyDown += (o, e) => { if (e.KeyCode == Keys.Escape) DialogResult = DialogResult.Cancel; };
        }

        public override Color BackColor { get => Theme.P.Bg; set { } }
        public override Color ForeColor { get => Theme.P.Text; set { } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.TitleBar(this);
            Theme.Scrollbars(flow);
        }

        int ContentWidth => flow.ClientSize.Width - flow.Padding.Horizontal - Theme.D(4);

        void FitWidths()
        {
            foreach (Control c in flow.Controls)
                if (c.Tag as string == "wide") c.Width = Math.Max(Theme.D(200), ContentWidth);
        }

        // ---------- Построение ----------

        void Section(string title, string hint = null)
        {
            var l = new TLabel(title, Role.Text, 11.5f, true) { Margin = new Padding(0, Theme.D(16), 0, Theme.D(2)) };
            flow.Controls.Add(l);
            if (hint != null) Hint(hint);
        }

        void Hint(string text)
        {
            var l = new TLabel(text, Role.Muted, 9f) { AutoSize = false, Tag = "wide", Margin = new Padding(0, 0, 0, Theme.D(6)) };
            l.Width = Math.Max(Theme.D(200), ContentWidth);
            l.Height = TextRenderer.MeasureText(text, l.Font, new Size(l.Width, 0), TextFormatFlags.WordBreak).Height + Theme.D(2);
            l.SizeChanged += (o, e) =>
            {
                var h = TextRenderer.MeasureText(text, l.Font, new Size(Math.Max(1, l.Width), 0), TextFormatFlags.WordBreak).Height + Theme.D(2);
                if (l.Height != h) l.Height = h;
            };
            flow.Controls.Add(l);
        }

        ToggleSwitch Switch(string text, bool value, Action<bool> save)
        {
            var t = new ToggleSwitch(text, value) { Margin = new Padding(0, Theme.D(3), 0, Theme.D(3)) };
            apply.Add(() => save(t.Checked));
            flow.Controls.Add(t);
            return t;
        }

        NumericUpDown Number(string before, int value, int min, int max, string after, Action<int> save)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, Theme.D(3), 0, Theme.D(3)), BackColor = Color.Transparent };
            var l1 = new TLabel(before) { Margin = new Padding(0, Theme.D(5), Theme.D(8), 0), MinimumSize = new Size(Theme.D(300), 0) };
            var n = new NumericUpDown
            {
                Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)), Width = Theme.D(90),
                BackColor = Theme.P.InputBg, ForeColor = Theme.P.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Ui(9.75f),
                TextAlign = HorizontalAlignment.Right, ThousandsSeparator = true,
            };
            var l2 = new TLabel(after, Role.Muted) { Margin = new Padding(Theme.D(8), Theme.D(5), 0, 0) };
            row.Controls.AddRange(new Control[] { l1, n, l2 });
            apply.Add(() => save((int)n.Value));
            flow.Controls.Add(row);
            return n;
        }

        readonly Dictionary<string, Control> controls = new Dictionary<string, Control>();

        void Build()
        {
            flow.SuspendLayout();

            Section("Что искать");
            controls["temp"] = Switch("Временные и резервные файлы, старые журналы", s.FindTemp, v => s.FindTemp = v);
            controls["dup"] = Switch("Дубликаты (одинаковое содержимое)", s.FindDuplicates, v => s.FindDuplicates = v);
            controls["emptyf"] = Switch("Пустые файлы", s.FindEmptyFiles, v => s.FindEmptyFiles = v);
            controls["emptyd"] = Switch("Пустые папки", s.FindEmptyFolders, v => s.FindEmptyFolders = v);
            controls["inst"] = Switch("Установщики и образы дисков", s.FindInstallers, v => s.FindInstallers = v);
            controls["large"] = Switch("Большие и давно не менявшиеся файлы", s.FindOldLarge, v => s.FindOldLarge = v);

            Section("Пороги");
            controls["largeMb"] = Number("Большой файл – от", s.LargeMb, 1, 1000000, "МБ", v => s.LargeMb = v);
            controls["largeAge"] = Number("Старый – не менялся больше", s.LargeAgeDays, 0, 36500, "дней", v => s.LargeAgeDays = v);
            controls["instAge"] = Number("Установщик предлагать, если лежит больше", s.InstallerAgeDays, 0, 36500, "дней", v => s.InstallerAgeDays = v);
            controls["logAge"] = Number("Журналы (.log) предлагать старше", s.LogAgeDays, 0, 36500, "дней", v => s.LogAgeDays = v);
            controls["dupMin"] = Number("Дубликаты искать среди файлов от", s.DupMinKb, 0, 10000000, "КБ (0 – все)", v => s.DupMinKb = v);

            Section("Исключения", "Не предлагать к удалению. По одному на строку: полный путь (C:\\Фото\\Архив), имя папки или файла (Backup) или маска (*.psd). Системные файлы, .git и node_modules не трогаются всегда.");
            var excl = new ThemedTextBox
            {
                Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, Height = Theme.D(110), Tag = "wide",
                Font = Theme.Ui(9.75f), BorderStyle = BorderStyle.FixedSingle, Text = string.Join(Environment.NewLine, s.Exclusions),
                Margin = new Padding(0, Theme.D(2), 0, Theme.D(4)),
            };
            excl.HandleCreated += (o, e) => Theme.Scrollbars(excl);
            apply.Add(() => s.Exclusions = excl.Lines.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
            flow.Controls.Add(excl);
            controls["excl"] = excl;

            Section("Удаление");
            controls["recycle"] = Switch("По умолчанию удалять в Корзину (можно восстановить)", s.RecycleBin, v => s.RecycleBin = v);
            controls["confirm"] = Switch("Спрашивать подтверждение и при удалении одного файла", s.ConfirmSingle, v => s.ConfirmSingle = v);

            Section("Оформление");
            var themeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, Theme.D(4), 0, 0) };
            var chips = new List<ChipButton>();
            foreach (var (mode, title) in new[] { (ThemeMode.System, "Как в Windows"), (ThemeMode.Light, "Светлая"), (ThemeMode.Dark, "Тёмная") })
            {
                var c = new ChipButton(title) { Value = mode, Checked = theme == mode };
                c.Click += (o, e) => { theme = (ThemeMode)c.Value; foreach (var x in chips) x.Checked = x == c; };
                chips.Add(c);
                themeRow.Controls.Add(c);
            }
            controls["theme"] = themeRow;
            flow.Controls.Add(themeRow);

            Section("Обновления", $"Установлена версия {Updater.Current}. Новые версии берутся из релизов на GitHub, установщик проверяется по контрольной сумме.");
            controls["upd"] = Switch("Проверять обновления при запуске", s.AutoCheckUpdates, v => s.AutoCheckUpdates = v);
            controls["updInst"] = Switch("Скачивать и устанавливать автоматически (при закрытии программы)", s.AutoInstallUpdates, v => s.AutoInstallUpdates = v);
            var updRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, Theme.D(6), 0, 0) };
            var btnCheck = new FlatButton("Проверить сейчас", BtnKind.Secondary, Theme.GRefresh, compact: true);
            var lblCheck = new TLabel("", Role.Muted) { Margin = new Padding(Theme.D(10), Theme.D(7), 0, 0) };
            var lnkRepo = new LinkLabel { Text = "Страница программы на GitHub", AutoSize = true, Margin = new Padding(Theme.D(10), Theme.D(7), 0, 0), LinkColor = Theme.P.Accent, ActiveLinkColor = Theme.P.AccentHover, BackColor = Color.Transparent };
            lnkRepo.LinkClicked += (o, e) => { try { System.Diagnostics.Process.Start(Updater.RepoUrl); } catch { } };
            btnCheck.Click += async (o, e) =>
            {
                btnCheck.Enabled = false;
                lblCheck.Text = "Проверяю…";
                try { lblCheck.Text = await checkUpdates(); }
                finally { btnCheck.Enabled = true; }
            };
            updRow.Controls.AddRange(new Control[] { btnCheck, lblCheck });
            flow.Controls.Add(updRow);
            flow.Controls.Add(lnkRepo);

            Section("Проводник");
            controls["shell"] = Switch("Пункт «Навести порядок» в меню папок (правая кнопка мыши)", s.ContextMenu, v => s.ContextMenu = v);

            flow.ResumeLayout();
            FitWidths();
        }

        void ResetDefaults()
        {
            var d = new AppSettings();
            ((ToggleSwitch)controls["temp"]).Checked = d.FindTemp;
            ((ToggleSwitch)controls["dup"]).Checked = d.FindDuplicates;
            ((ToggleSwitch)controls["emptyf"]).Checked = d.FindEmptyFiles;
            ((ToggleSwitch)controls["emptyd"]).Checked = d.FindEmptyFolders;
            ((ToggleSwitch)controls["inst"]).Checked = d.FindInstallers;
            ((ToggleSwitch)controls["large"]).Checked = d.FindOldLarge;
            ((NumericUpDown)controls["largeMb"]).Value = d.LargeMb;
            ((NumericUpDown)controls["largeAge"]).Value = d.LargeAgeDays;
            ((NumericUpDown)controls["instAge"]).Value = d.InstallerAgeDays;
            ((NumericUpDown)controls["logAge"]).Value = d.LogAgeDays;
            ((NumericUpDown)controls["dupMin"]).Value = d.DupMinKb;
            ((ToggleSwitch)controls["recycle"]).Checked = d.RecycleBin;
            ((ToggleSwitch)controls["confirm"]).Checked = d.ConfirmSingle;
            ((ToggleSwitch)controls["upd"]).Checked = d.AutoCheckUpdates;
            ((ToggleSwitch)controls["updInst"]).Checked = d.AutoInstallUpdates;
            ((ToggleSwitch)controls["shell"]).Checked = d.ContextMenu;
            theme = d.Theme;
            foreach (ChipButton c in controls["theme"].Controls) c.Checked = (ThemeMode)c.Value == theme;
            // исключения не сбрасываем – это ваши данные
        }
    }
}
