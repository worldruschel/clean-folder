using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CleanFolder
{
    public enum Role { Bg, Surface, Sidebar, Text, Muted, Accent, Danger, Warning }

    static class R
    {
        public static Color Of(Role r)
        {
            var p = Theme.P;
            switch (r)
            {
                case Role.Bg: return p.Bg;
                case Role.Surface: return p.Surface;
                case Role.Sidebar: return p.Sidebar;
                case Role.Muted: return p.Muted;
                case Role.Accent: return p.Accent;
                case Role.Danger: return p.Danger;
                case Role.Warning: return p.Warning;
                default: return p.Text;
            }
        }
    }

    /// <summary>Панель с цветом фона из темы.</summary>
    public class TPanel : Panel, IThemed
    {
        public Role Role;
        public bool BorderTop, BorderBottom;
        public TPanel(Role role = Role.Bg) { Role = role; DoubleBuffered = true; }
        public override Color BackColor { get => R.Of(Role); set { } }
        public void ThemeChanged() => OnBackColorChanged(EventArgs.Empty);
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.P.Border))
            {
                if (BorderTop) e.Graphics.DrawLine(pen, 0, 0, Width, 0);
                if (BorderBottom) e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            }
        }
    }

    /// <summary>Надпись с цветом текста из темы. Фон прозрачный (берётся у родителя).</summary>
    public class TLabel : Label, IThemed
    {
        public Role Role;
        public TLabel(string text = "", Role role = Role.Text, float size = 9.75f, bool semibold = false)
        {
            Text = text; Role = role; Font = Theme.Ui(size, semibold); AutoSize = true; BackColor = Color.Transparent;
            UseMnemonic = false;
        }
        public Func<Color> Custom;
        public override Color ForeColor { get => Custom?.Invoke() ?? R.Of(Role); set { } }
        public void ThemeChanged() => OnForeColorChanged(EventArgs.Empty);

        /// <summary>Многострочная надпись фиксированной ширины.</summary>
        public static TLabel Wrap(string text, Role role = Role.Text, float size = 9.75f, bool semibold = false)
        {
            var l = new TLabel(text, role, size, semibold) { AutoSize = false, Padding = Padding.Empty, Margin = Padding.Empty };
            return l;
        }

        /// <summary>Ставит надпись с переносом строк в (x, y) шириной w. Возвращает нижнюю границу.</summary>
        public int Place(int x, int y, int w)
        {
            w = Math.Max(Theme.D(40), w);
            int h = TextRenderer.MeasureText(Text, Font, new Size(w - Theme.D(6), 0), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + Theme.D(2);
            SetBounds(x, y, w, h);
            return y + h;
        }
    }

    /// <summary>Блок с ручной раскладкой: Arrange(ширина) расставляет детей и возвращает высоту.</summary>
    public class LayoutCard : Card
    {
        public Func<int, int> Arrange;
        public bool Plain;
        public LayoutCard(bool plain = false) { Plain = plain; Margin = new Padding(0, 0, 0, Theme.D(12)); Padding = Padding.Empty; }
        public override Color BackColor { get => Plain ? (Theme.BgOf(this)) : Theme.P.Surface; set { } }
        public void Fit(int width)
        {
            if (Arrange == null) return;
            SuspendLayout();
            int h = Arrange(width);
            ResumeLayout(false);
            Size = new Size(width, h);
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (Plain) { using (var b = new SolidBrush(BackColor)) e.Graphics.FillRectangle(b, ClientRectangle); return; }
            base.OnPaintBackground(e);
        }
    }

    /// <summary>Карточка: скруглённая подложка с рамкой.</summary>
    public class Card : Panel, IThemed
    {
        public Color? Stripe;
        public int Radius = Theme.D(8);
        public Card() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); Padding = new Padding(Theme.D(16)); }
        public override Color BackColor { get => Theme.P.Surface; set { } }
        public void ThemeChanged() => OnBackColorChanged(EventArgs.Empty);
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(Theme.BgOf(this))) g.FillRectangle(b, ClientRectangle);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRound(g, r, Radius, Theme.P.Surface, Theme.P.Border);
            if (Stripe.HasValue)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var clip = Theme.Round(r, Radius))
                using (var b = new SolidBrush(Stripe.Value))
                {
                    g.SetClip(clip);
                    g.FillRectangle(b, 0, 0, Theme.D(4), Height);
                    g.ResetClip();
                }
            }
        }
    }

    public enum BtnKind { Primary, Secondary, Danger, DangerOutline, Ghost, GhostDanger }

    /// <summary>Плоская кнопка со скруглением, необязательным значком и состояниями наведения/нажатия.</summary>
    public class FlatButton : Control, IThemed
    {
        BtnKind kind;
        string glyph;
        bool hover, down;
        public bool AutoWidth = true;
        public bool Compact;

        public FlatButton(string text, BtnKind kind, string glyph = null, bool compact = false)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            this.kind = kind; this.glyph = glyph; Compact = compact;
            Font = Theme.Ui(compact ? 9.25f : 9.75f, kind == BtnKind.Primary || kind == BtnKind.Danger);
            Cursor = Cursors.Hand;
            Height = Theme.D(compact ? 30 : 36);
            TabStop = true;
            Text = text;
            BackColor = Color.Transparent;
            Fit();
        }

        public BtnKind Kind { get => kind; set { kind = value; Font = Theme.Ui(Compact ? 9.25f : 9.75f, kind == BtnKind.Primary || kind == BtnKind.Danger); Fit(); Invalidate(); } }
        public string Glyph { get => glyph; set { glyph = value; Fit(); Invalidate(); } }

        public int PreferredWidth
        {
            get
            {
                int pad = Theme.D(Compact ? 10 : 14);
                int w = pad * 2;
                bool hasText = !string.IsNullOrEmpty(Text);
                if (hasText) w += TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
                if (glyph != null) w += Theme.D(16) + (hasText ? Theme.D(8) : 0);
                if (!hasText && glyph != null) w = Height;
                return w;
            }
        }

        void Fit() { if (AutoWidth) Width = PreferredWidth; }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Fit(); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Focus(); Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { e.Handled = true; OnClick(EventArgs.Empty); }
            base.OnKeyDown(e);
        }
        protected override bool IsInputKey(Keys k) => k == Keys.Enter || k == Keys.Space || base.IsInputKey(k);
        public void ThemeChanged() => Invalidate();

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var p = Theme.P;
            using (var b = new SolidBrush(Theme.BgOf(this))) g.FillRectangle(b, ClientRectangle);

            Color fill, fore; Color? border = null;
            switch (kind)
            {
                case BtnKind.Primary:
                    fill = down ? Theme.Blend(p.AccentHover, Color.Black, 0.1f) : hover ? p.AccentHover : p.Accent; fore = p.OnAccent; break;
                case BtnKind.Danger:
                    fill = down ? Theme.Blend(p.DangerHover, Color.Black, 0.1f) : hover ? p.DangerHover : p.Danger; fore = Color.White; break;
                case BtnKind.DangerOutline:
                    fill = down ? Theme.Blend(p.DangerSoft, p.Danger, 0.15f) : hover ? p.DangerSoft : p.Surface; fore = p.Danger; border = Theme.Blend(p.Danger, p.Surface, 0.45f); break;
                case BtnKind.Ghost:
                    fill = down ? Theme.Blend(p.Hover, p.Accent, 0.12f) : hover ? p.Hover : (Theme.BgOf(this)); fore = p.Accent; break;
                case BtnKind.GhostDanger:
                    fill = down ? Theme.Blend(p.DangerSoft, p.Danger, 0.12f) : hover ? p.DangerSoft : (Theme.BgOf(this)); fore = p.Danger; break;
                default:
                    fill = down ? Theme.Blend(p.Hover, p.Border, 0.5f) : hover ? p.Hover : p.Surface; fore = p.Text; border = p.Border; break;
            }
            if (!Enabled)
            {
                var bg = Theme.BgOf(this);
                fill = kind == BtnKind.Ghost || kind == BtnKind.GhostDanger ? bg : Theme.Blend(fill, bg, 0.55f);
                fore = Theme.Blend(fore, bg, 0.5f);
                if (border.HasValue) border = Theme.Blend(border.Value, bg, 0.5f);
            }

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRound(g, r, Theme.D(6), fill, border);
            if (Focused && ShowFocusCues)
                using (var path = Theme.Round(Rectangle.Inflate(r, -2, -2), Theme.D(5)))
                using (var pen = new Pen(Theme.Blend(fore, fill, 0.4f)) { DashStyle = DashStyle.Dot })
                    g.DrawPath(pen, path);

            bool hasText = !string.IsNullOrEmpty(Text);
            int gw = glyph != null ? Theme.D(16) : 0;
            int tw = hasText ? TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width : 0;
            int total = gw + tw + (glyph != null && hasText ? Theme.D(8) : 0);
            int x = Math.Max(Theme.D(6), (Width - total) / 2);
            if (glyph != null)
            {
                Theme.DrawGlyph(g, glyph, Compact ? 9.5f : 10.5f, fore, new Rectangle(x, 0, gw, Height));
                x += gw + Theme.D(8);
            }
            if (hasText)
                TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, Width - x - Theme.D(4), Height), fore,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Переключаемая «пилюля» для фильтров.</summary>
    public class ChipButton : Control, IThemed
    {
        bool hover, isChecked;
        public Color? Dot;
        public object Value;

        public ChipButton(string text, Color? dot = null)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Theme.Ui(9.25f);
            Dot = dot;
            Cursor = Cursors.Hand;
            Height = Theme.D(30);
            Margin = new Padding(0, 0, Theme.D(6), Theme.D(6));
            Text = text;
        }

        public bool Checked { get => isChecked; set { isChecked = value; Invalidate(); } }
        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Width = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width + Theme.D(28) + (Dot.HasValue ? Theme.D(14) : 0);
            Invalidate();
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        public void ThemeChanged() => Invalidate();

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; var p = Theme.P;
            using (var b = new SolidBrush(Theme.BgOf(this))) g.FillRectangle(b, ClientRectangle);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = isChecked ? p.AccentSoft : hover ? p.Hover : p.Surface;
            Color border = isChecked ? Theme.Blend(p.Accent, p.Surface, 0.35f) : p.Border;
            Color fore = isChecked ? (p.Dark ? Theme.Blend(p.Accent, Color.White, 0.35f) : p.AccentHover) : p.Text;
            Theme.FillRound(g, r, Height / 2, fill, border);
            int x = Theme.D(14);
            if (Dot.HasValue)
            {
                int d = Theme.D(8);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(Dot.Value)) g.FillEllipse(b, x, (Height - d) / 2, d, d);
                x += Theme.D(14);
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, Width - x, Height), fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>Пункт бокового меню.</summary>
    public class NavItem : Control, IThemed
    {
        bool hover, selected;
        readonly string glyph;
        string badge;

        public NavItem(string text, string glyph)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Text = text; this.glyph = glyph;
            Font = Theme.Ui(10f);
            Height = Theme.D(40);
            Cursor = Cursors.Hand;
        }

        public bool Selected { get => selected; set { selected = value; Font = Theme.Ui(10f, value); Invalidate(); } }
        public string Badge { get => badge; set { badge = value; Invalidate(); } }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        public void ThemeChanged() => Invalidate();

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; var p = Theme.P;
            using (var b = new SolidBrush(p.Sidebar)) g.FillRectangle(b, ClientRectangle);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            if (selected) Theme.FillRound(g, r, Theme.D(6), p.Dark ? p.Hover : p.Surface);
            else if (hover && Enabled) Theme.FillRound(g, r, Theme.D(6), Theme.Blend(p.Sidebar, p.Hover, p.Dark ? 0.8f : 0.6f));
            if (selected)
            {
                int bh = Theme.D(16);
                Theme.FillRound(g, new Rectangle(0, (Height - bh) / 2, Theme.D(3), bh), Theme.D(2), p.Accent);
            }
            var fore = !Enabled ? Theme.Blend(p.Muted, p.Sidebar, 0.4f) : selected ? p.Text : Theme.Blend(p.Text, p.Muted, 0.35f);
            Theme.DrawGlyph(g, glyph, 11f, selected ? p.Accent : fore, new Rectangle(Theme.D(12), 0, Theme.D(20), Height));
            int right = Width - Theme.D(10);
            if (!string.IsNullOrEmpty(badge))
            {
                var bf = Theme.Ui(8.5f, true);
                int bw = TextRenderer.MeasureText(badge, bf, Size.Empty, TextFormatFlags.NoPadding).Width + Theme.D(12);
                int bh = Theme.D(20);
                var br = new Rectangle(right - bw, (Height - bh) / 2, bw, bh);
                Theme.FillRound(g, br, bh / 2, selected ? p.AccentSoft : (p.Dark ? p.Surface : p.Border));
                TextRenderer.DrawText(g, badge, bf, br, selected ? p.Accent : p.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                right = br.X - Theme.D(6);
            }
            int x = Theme.D(42);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, right - x, Height), fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Переключатель «вкл/выкл» с подписью справа.</summary>
    public class ToggleSwitch : Control, IThemed
    {
        bool on, hover;
        public event EventHandler CheckedChanged;

        public ToggleSwitch(string text, bool value = false)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Font = Theme.Ui(9.75f);
            Cursor = Cursors.Hand;
            Height = Theme.D(28);
            on = value;
            TabStop = true;
            Text = text;
        }

        public bool Checked
        {
            get => on;
            set { if (on == value) return; on = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Width = Theme.D(52) + TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width + Theme.D(4);
            Invalidate();
        }
        protected override void OnClick(EventArgs e) { Checked = !Checked; Focus(); base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { Checked = !Checked; e.Handled = true; } base.OnKeyDown(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        public void ThemeChanged() => Invalidate();

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; var p = Theme.P;
            var bg = Theme.BgOf(this);
            using (var b = new SolidBrush(bg)) g.FillRectangle(b, ClientRectangle);
            int tw = Theme.D(40), th = Theme.D(20);
            var track = new Rectangle(0, (Height - th) / 2, tw, th);
            Color fill = on ? (hover ? p.AccentHover : p.Accent) : (hover ? Theme.Blend(bg, p.Muted, 0.25f) : bg);
            Color border = on ? fill : p.Muted;
            if (!Enabled) { fill = Theme.Blend(fill, bg, 0.5f); border = Theme.Blend(border, bg, 0.5f); }
            Theme.FillRound(g, track, th / 2, fill, border);
            int k = Theme.D(12);
            int kx = on ? track.Right - k - Theme.D(4) : track.X + Theme.D(4);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(on ? Color.White : p.Muted)) g.FillEllipse(b, kx, track.Y + (th - k) / 2, k, k);
            if (Focused && ShowFocusCues)
                using (var path = Theme.Round(Rectangle.Inflate(track, 2, 2), th / 2 + 2))
                using (var pen = new Pen(p.Accent)) g.DrawPath(pen, path);
            var fore = Enabled ? p.Text : p.Muted;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(tw + Theme.D(10), 0, Width - tw - Theme.D(10), Height), fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>Тонкая полоса прогресса: бегущая (Marquee) или с процентом.</summary>
    public class ThinProgress : Control, IThemed
    {
        readonly Timer timer = new Timer { Interval = 16 };
        float phase;
        int value = -1;

        public ThinProgress()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Theme.D(3);
            timer.Tick += (s, e) => { phase = (phase + 0.012f) % 1.4f; Invalidate(); };
        }

        /// <summary>-1 – бегущая полоса, 0..100 – процент.</summary>
        public int Value { get => value; set { this.value = value; Invalidate(); } }
        public bool Running
        {
            get => timer.Enabled;
            set { timer.Enabled = value; Invalidate(); }
        }
        public void ThemeChanged() => Invalidate();
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; var p = Theme.P;
            using (var b = new SolidBrush(Theme.BgOf(this))) g.FillRectangle(b, ClientRectangle);
            if (!Running && value < 0) return;
            using (var b = new SolidBrush(Theme.Blend(p.Accent, Theme.BgOf(this), 0.8f))) g.FillRectangle(b, ClientRectangle);
            using (var b = new SolidBrush(p.Accent))
            {
                if (value >= 0) g.FillRectangle(b, 0, 0, Width * Math.Min(100, value) / 100, Height);
                else
                {
                    float w = Width * 0.3f;
                    float x = (phase - 0.3f) * Width;
                    g.FillRectangle(b, x, 0, w, Height);
                }
            }
        }
    }

    /// <summary>Поле ввода в скруглённой рамке: значок слева, подсказка-заглушка, кнопки справа.</summary>
    public class InputBox : Panel, IThemed
    {
        public readonly TextBox Box = new ThemedTextBox();
        readonly string glyph;
        public InputBox(string cue, string glyph)
        {
            this.glyph = glyph;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            Height = Theme.D(36);
            Box.BorderStyle = BorderStyle.None;
            Box.Font = Theme.Ui(10f);
            Controls.Add(Box);
            Box.GotFocus += (s, e) => Invalidate();
            Box.LostFocus += (s, e) => Invalidate();
            Box.HandleCreated += (s, e) => SendMessage(Box.Handle, 0x1501, (IntPtr)1, cue);
            Click += (s, e) => Box.Focus();
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        public override Color BackColor { get => Theme.P.InputBg; set { } }
        public void ThemeChanged() => OnBackColorChanged(EventArgs.Empty);
        public override string Text { get => Box.Text; set => Box.Text = value; }

        /// <summary>Правая часть (кнопки), не перекрываемая текстом.</summary>
        public int RightReserve;

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int left = glyph != null ? Theme.D(34) : Theme.D(12);
            Box.SetBounds(left, (Height - Box.PreferredHeight) / 2, Math.Max(10, Width - left - Theme.D(10) - RightReserve), Box.PreferredHeight);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics; var p = Theme.P;
            using (var b = new SolidBrush(Theme.BgOf(this))) g.FillRectangle(b, ClientRectangle);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRound(g, r, Theme.D(6), p.InputBg, Box.Focused ? p.Accent : p.Border);
            if (Box.Focused)
            {
                using (var pen = new Pen(p.Accent))
                    g.DrawLine(pen, Theme.D(4), Height - 1, Width - Theme.D(5), Height - 1);
            }
            if (glyph != null) Theme.DrawGlyph(g, glyph, 10.5f, p.Muted, new Rectangle(Theme.D(10), 0, Theme.D(18), Height));
        }
    }

    public class ThemedTextBox : TextBox, IThemed
    {
        public override Color BackColor { get => Theme.P.InputBg; set { } }
        public override Color ForeColor { get => Theme.P.Text; set { } }
        public void ThemeChanged() { OnBackColorChanged(EventArgs.Empty); OnForeColorChanged(EventArgs.Empty); }
    }

    // ---------- Контекстные меню в цветах темы ----------

    public sealed class MenuColors : ProfessionalColorTable
    {
        static Palette P => Theme.P;
        public override Color ToolStripDropDownBackground => P.Surface;
        public override Color ImageMarginGradientBegin => P.Surface;
        public override Color ImageMarginGradientMiddle => P.Surface;
        public override Color ImageMarginGradientEnd => P.Surface;
        public override Color MenuBorder => P.Border;
        public override Color MenuItemBorder => P.Hover;
        public override Color MenuItemSelected => P.Hover;
        public override Color MenuItemSelectedGradientBegin => P.Hover;
        public override Color MenuItemSelectedGradientEnd => P.Hover;
        public override Color MenuItemPressedGradientBegin => P.Hover;
        public override Color MenuItemPressedGradientEnd => P.Hover;
        public override Color SeparatorDark => P.Border;
        public override Color SeparatorLight => P.Surface;
    }

    public sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var p = Theme.P;
            e.TextColor = !e.Item.Enabled ? p.Muted : (e.Item.Tag as string) == "danger" ? p.Danger : p.Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e) { }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.P.Muted;
            base.OnRenderArrow(e);
        }
    }

    public static class Menus
    {
        public static ContextMenuStrip Create()
        {
            var m = new ContextMenuStrip { Renderer = new MenuRenderer(), ShowImageMargin = false, Font = Theme.Ui(9.75f) };
            m.Padding = new Padding(Theme.D(2));
            return m;
        }

        public static ToolStripMenuItem Item(ToolStrip m, string text, Action action, bool enabled = true, bool danger = false, string keys = null)
        {
            var it = new ToolStripMenuItem(text) { Enabled = enabled, Tag = danger ? "danger" : null, Padding = new Padding(Theme.D(4), Theme.D(3), Theme.D(4), Theme.D(3)) };
            if (keys != null) it.ShortcutKeyDisplayString = keys;
            it.Click += (s, e) => action();
            m.Items.Add(it);
            return it;
        }
    }
}
