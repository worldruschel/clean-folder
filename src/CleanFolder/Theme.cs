using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CleanFolder
{
    public enum ThemeMode { System, Light, Dark }

    public sealed class Palette
    {
        public bool Dark;
        public Color Bg, Surface, Sidebar, Border, Text, Muted, Accent, AccentHover, AccentSoft, OnAccent,
            Danger, DangerHover, DangerSoft, Selection, GridLine, Hover, InputBg, Warning, WarningSoft;
    }

    /// <summary>Элемент, которому нужно перерисоваться или обновить цвета после смены темы.</summary>
    public interface IThemed { void ThemeChanged(); }

    public static class Theme
    {
        static Color H(int rgb) => Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);

        public static readonly Palette Light = new Palette
        {
            Dark = false,
            Bg = H(0xF4F6F8), Surface = H(0xFFFFFF), Sidebar = H(0xEBEEF2), Border = H(0xDDE1E7),
            Text = H(0x1B1F24), Muted = H(0x687280), Accent = H(0x0F7B6C), AccentHover = H(0x0B675A),
            AccentSoft = H(0xDDF1EE), OnAccent = Color.White, Danger = H(0xC42B1C), DangerHover = H(0xA62417),
            DangerSoft = H(0xFCE9E7), Selection = H(0xD9EFEB), GridLine = H(0xEEF0F3), Hover = H(0xF0F2F5),
            InputBg = H(0xFFFFFF), Warning = H(0x9A6100), WarningSoft = H(0xFFF4DC),
        };

        public static readonly Palette DarkPalette = new Palette
        {
            Dark = true,
            Bg = H(0x1B1E23), Surface = H(0x24282E), Sidebar = H(0x16191D), Border = H(0x363C44),
            Text = H(0xE6E8EB), Muted = H(0x9AA3AE), Accent = H(0x1FA592), AccentHover = H(0x29B8A4),
            AccentSoft = H(0x1D3A36), OnAccent = Color.White, Danger = H(0xD8463C), DangerHover = H(0xE85C52),
            DangerSoft = H(0x3D2426), Selection = H(0x21403B), GridLine = H(0x2F343B), Hover = H(0x2D3239),
            InputBg = H(0x1E2126), Warning = H(0xE0A84A), WarningSoft = H(0x3A3020),
        };

        public static Palette P = Light;

        public static void Apply(ThemeMode mode)
        {
            bool dark = mode == ThemeMode.Dark || (mode == ThemeMode.System && SystemIsDark());
            P = dark ? DarkPalette : Light;
        }

        public static bool SystemIsDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch { return false; }
        }

        /// <summary>Перерисовать всё дерево элементов после смены темы.</summary>
        public static void Refresh(Control root)
        {
            if (root is IThemed t) t.ThemeChanged();
            foreach (Control c in root.Controls) Refresh(c);
            if (root is Form f) TitleBar(f);
            root.Invalidate();
        }

        // ---------- Масштаб (DPI) ----------

        public static float Scale = 1f;
        public static int D(int px) => (int)Math.Round(px * Scale);

        // ---------- Шрифты ----------

        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        public static Font Ui(float size, bool semibold = false)
        {
            var key = (semibold ? "s" : "r") + size;
            if (!fonts.TryGetValue(key, out var f))
                fonts[key] = f = new Font(semibold ? "Segoe UI Semibold" : "Segoe UI", size);
            return f;
        }

        static string iconFamily;
        public static Font Icon(float size)
        {
            if (iconFamily == null)
            {
                try
                {
                    using (var fc = new InstalledFontCollection())
                        iconFamily = fc.Families.Any(x => x.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
                }
                catch { iconFamily = "Segoe MDL2 Assets"; }
            }
            var key = "i" + size;
            if (!fonts.TryGetValue(key, out var f)) fonts[key] = f = new Font(iconFamily, size);
            return f;
        }

        // ---------- Значки (Segoe MDL2 Assets / Segoe Fluent Icons) ----------
        public const string GFolder = "", GFolderOpen = "", GDelete = "", GSettings = "",
            GSearch = "", GRefresh = "", GHome = "", GList = "", GPage = "",
            GPie = "", GDownload = "", GInfo = "", GClose = "", GPlay = "",
            GCheck = "", GChevronDown = "", GChevronUp = "", GCopy = "", GWarning = "",
            GStop = "", GOpenFile = "", GHistory = "", GBlock = "", GFilter = "";

        public static void DrawGlyph(Graphics g, string glyph, float size, Color color, Rectangle r)
        {
            TextRenderer.DrawText(g, glyph, Icon(size), r, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }

        // ---------- Цвета категорий ----------

        public static Color CategoryColor(Category c)
        {
            bool d = P.Dark;
            switch (c)
            {
                case Category.Temp: return d ? H(0x3CC2AE) : H(0x0F7B6C);
                case Category.Duplicate: return d ? H(0x5B9BF0) : H(0x1F63C6);
                case Category.EmptyFile: return d ? H(0xA0A8B2) : H(0x6B7280);
                case Category.EmptyFolder: return d ? H(0x8E99A6) : H(0x8A94A3);
                case Category.Installer: return d ? H(0xE0A84A) : H(0xB06A00);
                default: return d ? H(0xC77DD8) : H(0x9333A8);
            }
        }

        // ---------- Рисование ----------

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color fill, Color? border = null)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Round(r, radius))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (border.HasValue) using (var pen = new Pen(border.Value)) g.DrawPath(pen, path);
            }
            g.SmoothingMode = old;
        }

        /// <summary>Фактический фон под элементом: первый непрозрачный фон среди родителей.</summary>
        public static Color BgOf(Control c)
        {
            for (var p = c?.Parent; p != null; p = p.Parent)
            {
                var b = p.BackColor;
                if (b.A == 255) return b;
            }
            return P.Bg;
        }

        public static Color Blend(Color a, Color b, float t) =>
            Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        // ---------- Системное оформление ----------

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        public static void TitleBar(Form f)
        {
            if (!f.IsHandleCreated) return;
            int v = P.Dark ? 1 : 0;
            try
            {
                if (DwmSetWindowAttribute(f.Handle, 20, ref v, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref v, 4);
            }
            catch { }
        }

        /// <summary>Тёмные полосы прокрутки у стандартных элементов (Windows 10 1809+).</summary>
        public static void Scrollbars(Control c)
        {
            if (!c.IsHandleCreated) return;
            try { SetWindowTheme(c.Handle, P.Dark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
        }
    }
}
