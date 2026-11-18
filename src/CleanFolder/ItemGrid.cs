using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CleanFolder
{
    /// <summary>Таблица в цветах темы: свои заголовки, без рамок, тёмные полосы прокрутки.</summary>
    public class ThemedGrid : DataGridView, IThemed
    {
        public ThemedGrid()
        {
            DoubleBuffered = true;
            AllowUserToAddRows = false;
            AllowUserToDeleteRows = false;
            AllowUserToResizeRows = false;
            AllowUserToOrderColumns = false;
            RowHeadersVisible = false;
            SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            MultiSelect = true;
            ReadOnly = true;
            BorderStyle = BorderStyle.None;
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            EnableHeadersVisualStyles = false;
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            ColumnHeadersHeight = Theme.D(36);
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            RowTemplate.Height = Theme.D(34);
            DefaultCellStyle.Font = Theme.Ui(9.75f);
            DefaultCellStyle.Padding = new Padding(Theme.D(6), 0, Theme.D(6), 0);
            ColumnHeadersDefaultCellStyle.Font = Theme.Ui(9f, true);
            ShowCellToolTips = true;
            StandardTab = true;
            DataError += (s, e) => { e.ThrowException = false; };
            ThemeChanged();
        }

        public virtual void ThemeChanged()
        {
            var p = Theme.P;
            BackgroundColor = p.Surface;
            GridColor = p.GridLine;
            DefaultCellStyle.BackColor = p.Surface;
            DefaultCellStyle.ForeColor = p.Text;
            DefaultCellStyle.SelectionBackColor = p.Selection;
            DefaultCellStyle.SelectionForeColor = p.Text;
            ColumnHeadersDefaultCellStyle.BackColor = p.Surface;
            ColumnHeadersDefaultCellStyle.ForeColor = p.Muted;
            ColumnHeadersDefaultCellStyle.SelectionBackColor = p.Surface;
            Theme.Scrollbars(this);
            foreach (Control c in Controls) Theme.Scrollbars(c);
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Таблицы создаются раньше, чем выбрана тема из настроек, – берём цвета заново
            ThemeChanged();
            foreach (Control c in Controls) { if (!c.IsHandleCreated) c.HandleCreated += (s, a) => Theme.Scrollbars((Control)s); else Theme.Scrollbars(c); }
        }

        protected override void OnCellPainting(DataGridViewCellPaintingEventArgs e)
        {
            base.OnCellPainting(e);
            if (e.Handled || e.RowIndex != -1 || e.ColumnIndex < 0) return;
            // Заголовки столбцов рисуем сами: фон, текст, стрелка сортировки, линия снизу
            var p = Theme.P;
            var g = e.Graphics;
            using (var b = new SolidBrush(p.Surface)) g.FillRectangle(b, e.CellBounds);
            using (var pen = new Pen(p.Border)) g.DrawLine(pen, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
            if (!PaintHeader(e))
            {
                var col = Columns[e.ColumnIndex];
                var r = Rectangle.Inflate(e.CellBounds, -Theme.D(10), 0);
                var align = col.DefaultCellStyle.Alignment == DataGridViewContentAlignment.MiddleRight ? TextFormatFlags.Right : TextFormatFlags.Left;
                var glyph = col.HeaderCell.SortGlyphDirection;
                if (glyph != SortOrder.None)
                {
                    var gr = new Rectangle(align == TextFormatFlags.Right ? r.Left : r.Right - Theme.D(14), r.Top, Theme.D(14), r.Height);
                    Theme.DrawGlyph(g, glyph == SortOrder.Ascending ? Theme.GChevronUp : Theme.GChevronDown, 7.5f, p.Accent, gr);
                    if (align == TextFormatFlags.Right) r = new Rectangle(r.Left + Theme.D(16), r.Top, r.Width - Theme.D(16), r.Height);
                    else r.Width -= Theme.D(16);
                }
                TextRenderer.DrawText(g, col.HeaderText, ColumnHeadersDefaultCellStyle.Font, r, glyph != SortOrder.None ? p.Text : p.Muted,
                    align | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }
            e.Handled = true;
        }

        /// <summary>Дополнительное рисование заголовка (например, общая галочка). true – нарисовано.</summary>
        protected virtual bool PaintHeader(DataGridViewCellPaintingEventArgs e) => false;

        public static DataGridViewTextBoxColumn Col(string header, int width, bool fill = false, bool right = false, int fillWeight = 100)
        {
            var c = new DataGridViewTextBoxColumn { HeaderText = header, Width = Theme.D(width), ReadOnly = true, SortMode = DataGridViewColumnSortMode.Programmatic };
            if (fill) { c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; c.MinimumWidth = Theme.D(width); c.FillWeight = fillWeight; }
            if (right) c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            else c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            return c;
        }
    }

    /// <summary>
    /// Список файлов с отметками (виртуальный режим – быстро и на десятках тысяч строк).
    /// Отметка – квадратик слева (щелчок, Shift+щелчок – диапазон, протягивание мышью, Пробел для выделенных строк).
    /// Выделение строк – обычное, с Ctrl и Shift. Справа значки «показать в Проводнике» и «удалить».
    /// </summary>
    public sealed class ItemGrid : ThemedGrid
    {
        public enum Kind { Suggestions, Large }

        public const int CCheck = 0, CCat = 1, CName = 2, CFolder = 3, CSize = 4, CDate = 5, CReason = 6, CShow = 7, CDel = 8;

        readonly Kind kind;
        List<Suggestion> all = new List<Suggestion>();
        List<Suggestion> view = new List<Suggestion>();
        public readonly HashSet<Suggestion> Checked = new HashSet<Suggestion>();
        public readonly Dictionary<Suggestion, string> Errors = new Dictionary<Suggestion, string>();
        public Func<Suggestion, bool> Filter;
        public string Root;
        public string EmptyText = "Здесь пока пусто";

        int sortCol = -1;
        bool sortAsc = true;
        int hoverRow = -1, hoverCol = -1;
        int anchorRow = -1;
        bool dragChecking, dragState;
        int dragLast = -1;

        public event Action CheckedChanged;
        public event Action<Suggestion> ShowRequested;
        public event Action<List<Suggestion>> DeleteRequested;
        public event Action<string> ExcludeRequested;

        public ItemGrid(Kind kind)
        {
            this.kind = kind;
            VirtualMode = true;
            Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "", Width = Theme.D(42), ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable, Resizable = DataGridViewTriState.False });
            Columns.Add(Col(kind == Kind.Suggestions ? "Категория" : "Тип", 140));
            Columns.Add(Col("Имя", 120, fill: true, fillWeight: 130));
            Columns.Add(Col("Папка", 90, fill: true, fillWeight: 90));
            Columns.Add(Col("Размер", 88, right: true));
            Columns.Add(Col("Изменён", 92));
            Columns.Add(Col(kind == Kind.Suggestions ? "Почему предлагаем" : "Подсказка", 110, fill: true, fillWeight: 120));
            Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "", Width = Theme.D(40), ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable, Resizable = DataGridViewTriState.False });
            Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "", Width = Theme.D(44), ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable, Resizable = DataGridViewTriState.False });
            Columns[CShow].ToolTipText = "Показать в Проводнике";
            Columns[CDel].ToolTipText = "Удалить";
            Columns[CCheck].ToolTipText = "Отметить всё видимое / снять отметки";
            ContextMenuStrip = Menus.Create();
            ContextMenuStrip.Opening += (s, e) => BuildMenu(e);
        }

        // ---------- Данные ----------

        public IReadOnlyList<Suggestion> All => all;
        public IReadOnlyList<Suggestion> View => view;
        public List<Suggestion> CheckedItems() => all.Where(Checked.Contains).ToList();

        public void SetItems(IEnumerable<Suggestion> items, bool checkRecommended)
        {
            all = items.ToList();
            Checked.Clear();
            Errors.Clear();
            if (checkRecommended) foreach (var s in all) if (s.Recommended) Checked.Add(s);
            anchorRow = -1;
            Refilter();
            CheckedChanged?.Invoke();
        }

        public void Remove(ICollection<string> paths)
        {
            var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
            var gone = all.Where(s => set.Contains(s.Path) || (s.Path.Length > 0 && IsInsideAny(s.Path, set))).ToList();
            if (gone.Count == 0) return;
            foreach (var s in gone) { Checked.Remove(s); Errors.Remove(s); }
            all = all.Except(gone).ToList();
            Refilter(keepPosition: true);
            CheckedChanged?.Invoke();
        }

        static bool IsInsideAny(string path, HashSet<string> folders)
        {
            var dir = Path.GetDirectoryName(path);
            while (!string.IsNullOrEmpty(dir)) { if (folders.Contains(dir)) return true; dir = Path.GetDirectoryName(dir); }
            return false;
        }

        public void Refilter(bool keepPosition = false)
        {
            int first = keepPosition && RowCount > 0 ? FirstDisplayedScrollingRowIndex : 0;
            var f = Filter;
            view = f == null ? all.ToList() : all.Where(f).ToList();
            ApplySort();
            CurrentCell = null;
            RowCount = 0;
            RowCount = view.Count;
            ClearSelection();
            anchorRow = -1;
            if (keepPosition && view.Count > 0) try { FirstDisplayedScrollingRowIndex = Math.Min(first, view.Count - 1); } catch { }
            Invalidate();
        }

        void ApplySort()
        {
            if (sortCol < 0) return;
            Func<Suggestion, IComparable> key;
            switch (sortCol)
            {
                case CCat: key = s => CategoryText(s); break;
                case CName: key = s => Path.GetFileName(s.Path); break;
                case CFolder: key = s => Scanner.RelativeFolder(Root ?? "", s.Path); break;
                case CSize: key = s => s.Size; break;
                case CDate: key = s => s.Modified; break;
                default: key = s => s.Reason ?? ""; break;
            }
            var cmp = StringComparer.CurrentCultureIgnoreCase;
            Comparison<Suggestion> c = (a, b) =>
            {
                var ka = key(a); var kb = key(b);
                int r = ka is string sa && kb is string sb ? cmp.Compare(sa, sb) : ka.CompareTo(kb);
                return sortAsc ? r : -r;
            };
            view.Sort(c);
            foreach (DataGridViewColumn col in Columns) col.HeaderCell.SortGlyphDirection = SortOrder.None;
            Columns[sortCol].HeaderCell.SortGlyphDirection = sortAsc ? SortOrder.Ascending : SortOrder.Descending;
        }

        string CategoryText(Suggestion s) => kind == Kind.Suggestions ? Categories.Title(s.Category) : Scanner.TypeOf(Path.GetExtension(s.Path));

        void SetChecked(Suggestion s, bool on) { if (on) Checked.Add(s); else Checked.Remove(s); }

        public void CheckWhere(Func<Suggestion, bool> rule, bool on, bool visibleOnly = true)
        {
            foreach (var s in visibleOnly ? view : all) if (rule(s)) SetChecked(s, on);
            InvalidateColumn(CCheck); Invalidate();
            CheckedChanged?.Invoke();
        }

        public List<Suggestion> SelectedItems()
        {
            var list = new List<Suggestion>();
            for (int i = 0; i < view.Count && i < RowCount; i++) if ((Rows.GetRowState(i) & DataGridViewElementStates.Selected) != 0) list.Add(view[i]);
            return list;
        }

        // ---------- Значения ячеек ----------

        protected override void OnCellValueNeeded(DataGridViewCellValueEventArgs e)
        {
            base.OnCellValueNeeded(e);
            if (e.RowIndex < 0 || e.RowIndex >= view.Count) return;
            var s = view[e.RowIndex];
            switch (e.ColumnIndex)
            {
                case CCat: e.Value = CategoryText(s); break;
                case CName: e.Value = Path.GetFileName(s.Path) + (s.IsFolder ? "\\" : ""); break;
                case CFolder: e.Value = Scanner.RelativeFolder(Root ?? "", s.Path); break;
                case CSize: e.Value = s.IsFolder && s.Size == 0 ? "–" : Format.Size(s.Size); break;
                case CDate: e.Value = s.Modified == DateTime.MinValue ? "" : s.Modified.ToString("dd.MM.yyyy"); break;
                case CReason: e.Value = Errors.TryGetValue(s, out var err) ? err : s.Reason; break;
                default: e.Value = ""; break;
            }
        }

        protected override void OnCellToolTipTextNeeded(DataGridViewCellToolTipTextNeededEventArgs e)
        {
            base.OnCellToolTipTextNeeded(e);
            if (e.RowIndex < 0 || e.RowIndex >= view.Count) return;
            var s = view[e.RowIndex];
            if (e.ColumnIndex == CShow) e.ToolTipText = "Показать в Проводнике";
            else if (e.ColumnIndex == CDel) e.ToolTipText = "Удалить";
            else if (e.ColumnIndex == CCheck) e.ToolTipText = "Отметить (Shift – диапазон, можно протянуть мышью)";
            else if (e.ColumnIndex == CName || e.ColumnIndex == CFolder) e.ToolTipText = s.Path;
            else if (e.ColumnIndex == CReason) e.ToolTipText = Errors.TryGetValue(s, out var err) ? err : s.Reason;
        }

        protected override void OnCellFormatting(DataGridViewCellFormattingEventArgs e)
        {
            base.OnCellFormatting(e);
            if (e.RowIndex < 0 || e.RowIndex >= view.Count) return;
            var p = Theme.P;
            var s = view[e.RowIndex];
            bool hover = e.RowIndex == hoverRow;
            e.CellStyle.BackColor = hover ? p.Hover : p.Surface;
            e.CellStyle.ForeColor = p.Text;
            e.CellStyle.SelectionForeColor = p.Text;
            if (e.ColumnIndex == CFolder || e.ColumnIndex == CDate) e.CellStyle.ForeColor = p.Muted;
            if (e.ColumnIndex == CReason) e.CellStyle.ForeColor = Errors.ContainsKey(s) ? p.Danger : p.Muted;
            e.CellStyle.SelectionBackColor = hover ? Theme.Blend(p.Selection, p.Accent, 0.08f) : p.Selection;
        }

        // ---------- Рисование ----------

        protected override bool PaintHeader(DataGridViewCellPaintingEventArgs e)
        {
            if (e.ColumnIndex != CCheck) return e.ColumnIndex == CShow || e.ColumnIndex == CDel;
            int n = view.Count(Checked.Contains);
            DrawCheck(e.Graphics, e.CellBounds, n == 0 ? 0 : n == view.Count ? 1 : 2, false);
            return true;
        }

        protected override void OnCellPainting(DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= view.Count || e.ColumnIndex < 0)
            {
                base.OnCellPainting(e);
                return;
            }
            var p = Theme.P;
            var g = e.Graphics;
            var s = view[e.RowIndex];
            int c = e.ColumnIndex;
            if (c == CCheck || c == CShow || c == CDel || c == CCat)
            {
                e.PaintBackground(e.ClipBounds, (e.State & DataGridViewElementStates.Selected) != 0);
                var b = e.CellBounds;
                if (c == CCheck) DrawCheck(g, b, Checked.Contains(s) ? 1 : 0, e.RowIndex == hoverRow && hoverCol == CCheck);
                else if (c == CCat)
                {
                    var color = kind == Kind.Suggestions ? Theme.CategoryColor(s.Category) : p.Muted;
                    int d = Theme.D(8);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var br = new SolidBrush(color)) g.FillEllipse(br, b.X + Theme.D(8), b.Y + (b.Height - d) / 2, d, d);
                    g.SmoothingMode = SmoothingMode.None;
                    var tr = new Rectangle(b.X + Theme.D(22), b.Y, b.Width - Theme.D(24), b.Height);
                    TextRenderer.DrawText(g, CategoryText(s), e.CellStyle.Font, tr, kind == Kind.Suggestions ? color : p.Text,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                }
                else
                {
                    bool hot = e.RowIndex == hoverRow && hoverCol == c;
                    bool del = c == CDel;
                    int sz = Theme.D(28);
                    var ir = new Rectangle(b.X + (b.Width - sz) / 2, b.Y + (b.Height - sz) / 2, sz, sz);
                    if (hot) Theme.FillRound(g, ir, Theme.D(6), del ? p.DangerSoft : p.AccentSoft);
                    var col = hot ? (del ? p.Danger : p.Accent) : (e.RowIndex == hoverRow || (e.State & DataGridViewElementStates.Selected) != 0 ? (del ? Theme.Blend(p.Danger, p.Muted, 0.4f) : p.Accent) : p.Muted);
                    Theme.DrawGlyph(g, del ? Theme.GDelete : Theme.GFolderOpen, 10.5f, col, ir);
                }
                e.Handled = true;
                return;
            }
            base.OnCellPainting(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (view.Count > 0 || string.IsNullOrEmpty(EmptyText)) return;
            var r = new Rectangle(Theme.D(20), ColumnHeadersHeight + Theme.D(30), Math.Max(10, Width - Theme.D(40)), Theme.D(60));
            TextRenderer.DrawText(e.Graphics, EmptyText, Theme.Ui(10f), r, Theme.P.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak);
        }

        static void DrawCheck(Graphics g, Rectangle cell, int state, bool hot)
        {
            var p = Theme.P;
            int sz = Theme.D(18);
            var r = new Rectangle(cell.X + (cell.Width - sz) / 2, cell.Y + (cell.Height - sz) / 2, sz - 1, sz - 1);
            if (state == 0)
                Theme.FillRound(g, r, Theme.D(4), hot ? p.Hover : p.InputBg, hot ? p.Accent : p.Muted);
            else
            {
                Theme.FillRound(g, r, Theme.D(4), hot ? p.AccentHover : p.Accent);
                if (state == 1) Theme.DrawGlyph(g, Theme.GCheck, 8.5f, Color.White, new Rectangle(r.X, r.Y + 1, r.Width + 1, r.Height));
                else using (var pen = new Pen(Color.White, Math.Max(2, Theme.D(2))))
                        g.DrawLine(pen, r.X + Theme.D(4), r.Y + r.Height / 2, r.Right - Theme.D(4), r.Y + r.Height / 2);
            }
        }

        // ---------- Мышь ----------

        protected override void OnCellMouseEnter(DataGridViewCellEventArgs e)
        {
            base.OnCellMouseEnter(e);
            int oldRow = hoverRow;
            hoverRow = e.RowIndex; hoverCol = e.ColumnIndex;
            if (oldRow != hoverRow)
            {
                if (oldRow >= 0 && oldRow < RowCount) InvalidateRow(oldRow);
                if (hoverRow >= 0) InvalidateRow(hoverRow);
            }
            else if (hoverRow >= 0) InvalidateRow(hoverRow);
            Cursor = e.RowIndex >= 0 && (e.ColumnIndex == CShow || e.ColumnIndex == CDel || e.ColumnIndex == CCheck) ? Cursors.Hand
                   : e.RowIndex == -1 && e.ColumnIndex == CCheck ? Cursors.Hand : Cursors.Default;

            if (dragChecking && (MouseButtons & MouseButtons.Left) != 0 && e.RowIndex >= 0 && e.RowIndex < view.Count && dragLast >= 0)
            {
                int a = Math.Min(dragLast, e.RowIndex), b = Math.Max(dragLast, e.RowIndex);
                for (int i = a; i <= b; i++) SetChecked(view[i], dragState);
                dragLast = e.RowIndex;
                InvalidateColumn(CCheck);
                CheckedChanged?.Invoke();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            int old = hoverRow;
            hoverRow = hoverCol = -1;
            if (old >= 0 && old < RowCount) InvalidateRow(old);
            Cursor = Cursors.Default;
        }

        protected override void OnCellMouseDown(DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex >= 0 && e.RowIndex < view.Count)
            {
                if (e.Button == MouseButtons.Left && e.ColumnIndex == CCheck)
                {
                    var s = view[e.RowIndex];
                    if ((ModifierKeys & Keys.Shift) != 0 && anchorRow >= 0 && anchorRow < view.Count)
                    {
                        bool st = Checked.Contains(view[anchorRow]);
                        int a = Math.Min(anchorRow, e.RowIndex), b = Math.Max(anchorRow, e.RowIndex);
                        for (int i = a; i <= b; i++) SetChecked(view[i], st);
                    }
                    else
                    {
                        bool st = !Checked.Contains(s);
                        SetChecked(s, st);
                        dragChecking = true; dragState = st; dragLast = e.RowIndex;
                        anchorRow = e.RowIndex;
                    }
                    InvalidateColumn(CCheck);
                    CheckedChanged?.Invoke();
                }
                else if (e.Button == MouseButtons.Right && !Rows[e.RowIndex].Selected)
                {
                    ClearSelection();
                    CurrentCell = this[CName, e.RowIndex];
                    Rows[e.RowIndex].Selected = true;
                }
                else if (e.Button == MouseButtons.Left && (e.ColumnIndex == CShow || e.ColumnIndex == CDel))
                {
                    // значки не меняют выделение
                }
            }
            base.OnCellMouseDown(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            var hit = HitTest(e.X, e.Y);
            // щелчок по значку не должен сбрасывать выделение нескольких строк
            if (e.Button == MouseButtons.Left && hit.RowIndex >= 0 && (hit.ColumnIndex == CShow || hit.ColumnIndex == CDel)) return;
            if (e.Button == MouseButtons.Left && hit.Type == DataGridViewHitTestType.ColumnHeader && hit.ColumnIndex == CCheck)
            {
                bool allOn = view.Count > 0 && view.All(Checked.Contains);
                CheckWhere(_ => true, !allOn);
                return;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            var hit = HitTest(e.X, e.Y);
            if (e.Button == MouseButtons.Left && hit.RowIndex >= 0 && hit.RowIndex < view.Count && !dragChecking)
            {
                if (hit.ColumnIndex == CShow) ShowRequested?.Invoke(view[hit.RowIndex]);
                else if (hit.ColumnIndex == CDel) DeleteRequested?.Invoke(new List<Suggestion> { view[hit.RowIndex] });
            }
            dragChecking = false; dragLast = -1;
            base.OnMouseUp(e);
        }

        protected override void OnCellDoubleClick(DataGridViewCellEventArgs e)
        {
            base.OnCellDoubleClick(e);
            if (e.RowIndex >= 0 && e.RowIndex < view.Count && e.ColumnIndex != CCheck && e.ColumnIndex != CShow && e.ColumnIndex != CDel)
                ShowRequested?.Invoke(view[e.RowIndex]);
        }

        protected override void OnColumnHeaderMouseClick(DataGridViewCellMouseEventArgs e)
        {
            base.OnColumnHeaderMouseClick(e);
            if (e.Button != MouseButtons.Left || e.ColumnIndex < CCat || e.ColumnIndex > CReason) return;
            if (sortCol == e.ColumnIndex) sortAsc = !sortAsc;
            else { sortCol = e.ColumnIndex; sortAsc = e.ColumnIndex != CSize && e.ColumnIndex != CDate; }
            Refilter();
        }

        // ---------- Клавиатура ----------

        protected override bool ProcessDataGridViewKey(KeyEventArgs e)
        {
            var sel = SelectedItems();
            switch (e.KeyCode)
            {
                case Keys.Space:
                    if (sel.Count > 0)
                    {
                        bool on = !sel.All(Checked.Contains);
                        foreach (var s in sel) SetChecked(s, on);
                        InvalidateColumn(CCheck); CheckedChanged?.Invoke();
                    }
                    return true;
                case Keys.Delete:
                    if (sel.Count > 0) DeleteRequested?.Invoke(sel);
                    return true;
                case Keys.Enter:
                    if (CurrentCell != null && CurrentCell.RowIndex < view.Count) ShowRequested?.Invoke(view[CurrentCell.RowIndex]);
                    return true;
                case Keys.C when e.Control:
                    if (sel.Count > 0) CopyPaths(sel);
                    return true;
            }
            return base.ProcessDataGridViewKey(e);
        }

        static void CopyPaths(List<Suggestion> items)
        {
            try { Clipboard.SetText(string.Join(Environment.NewLine, items.Select(s => s.Path))); } catch { }
        }

        // ---------- Контекстное меню ----------

        void BuildMenu(System.ComponentModel.CancelEventArgs e)
        {
            var m = ContextMenuStrip;
            m.Items.Clear();
            var sel = SelectedItems();
            if (sel.Count == 0) { e.Cancel = true; return; }
            var first = sel[0];
            int n = sel.Count;
            string many = n > 1 ? $" ({n:N0})" : "";

            Menus.Item(m, "Показать в Проводнике", () => ShowRequested?.Invoke(first), keys: "Enter");
            if (!first.IsFolder) Menus.Item(m, "Открыть файл", () => { try { System.Diagnostics.Process.Start(first.Path); } catch { } }, enabled: n == 1);
            Menus.Item(m, "Копировать путь" + many, () => CopyPaths(sel), keys: "Ctrl+C");
            m.Items.Add(new ToolStripSeparator());
            Menus.Item(m, "Отметить выделенные" + many, () => { foreach (var s in sel) SetChecked(s, true); InvalidateColumn(CCheck); CheckedChanged?.Invoke(); }, keys: "Пробел");
            Menus.Item(m, "Снять отметку с выделенных" + many, () => { foreach (var s in sel) SetChecked(s, false); InvalidateColumn(CCheck); CheckedChanged?.Invoke(); });
            var folder = Path.GetDirectoryName(first.Path);
            Menus.Item(m, "Отметить всё в этой папке", () => CheckWhere(s => string.Equals(Path.GetDirectoryName(s.Path), folder, StringComparison.OrdinalIgnoreCase), true));
            if (kind == Kind.Suggestions)
                Menus.Item(m, $"Отметить всё: «{Categories.Title(first.Category)}»", () => CheckWhere(s => s.Category == first.Category, true));
            else
            {
                var type = Scanner.TypeOf(Path.GetExtension(first.Path));
                Menus.Item(m, $"Отметить всё: «{type}»", () => CheckWhere(s => Scanner.TypeOf(Path.GetExtension(s.Path)) == type, true));
            }
            m.Items.Add(new ToolStripSeparator());
            if (!string.IsNullOrEmpty(folder) && !string.Equals(folder, Root?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                Menus.Item(m, $"Больше не предлагать папку «{Path.GetFileName(folder)}»", () => ExcludeRequested?.Invoke(folder));
            if (n == 1) Menus.Item(m, first.IsFolder ? "Больше не предлагать эту папку" : "Больше не предлагать этот файл", () => ExcludeRequested?.Invoke(first.Path));
            m.Items.Add(new ToolStripSeparator());
            Menus.Item(m, "Удалить" + many + "…", () => DeleteRequested?.Invoke(sel), danger: true, keys: "Del");
        }
    }
}
