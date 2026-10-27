using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TaskManagerDark
{
    public sealed class ListColumn
    {
        public string Id, Title, Tooltip;
        public int Width;                 // logical px at 96 DPI
        public int MinWidth = 30;
        public bool Visible = true;
        public bool Right;                // right-aligned values
        public bool Resource;             // heat-map column; tall headers show a total above the title
        public bool DescendingFirst;      // numbers sort largest-first on the first click
        public bool Fixed;                // cannot be hidden or moved (Name)
        public string HeaderTotal;        // "12%" shown above the title in tall headers
        public double HeaderHeat;         // 0..1, tints the header when the resource is busy
        public string HeaderTooltip;      // tooltip for the total
        public int Index;                 // position in the page's column definition table
        public ListColumn Clone() => (ListColumn)MemberwiseClone();
    }

    public class ListRow
    {
        public string Key;
        public int Level;
        public bool IsGroup;
        public List<ListRow> Children;
        public object Tag;
        public IntPtr Icon;
        public bool Dim;
        public string GroupText;           // for group rows: "Apps (5)"
        public bool Expandable => Children != null && Children.Count > 0;
        public ListRow Parent;
    }

    public struct Cell
    {
        public string Text;
        public double Heat;                // NaN = no heat background
        public Color Fore;                 // Color.Empty = default
        public int Glyph;                  // 1 = suspended leaf
        public static Cell Of(string t) => new Cell { Text = t, Heat = double.NaN };
        public static Cell Hot(string t, double heat) => new Cell { Text = t, Heat = heat };
    }

    /// <summary>
    /// Virtualized, custom-drawn tree list used by every table in the app. Rows are rebuilt by the owning page on
    /// every update; selection, focus, expansion and scroll position are kept by row key so nothing jumps.
    /// </summary>
    public sealed class TreeList : Control
    {
        public readonly List<ListColumn> Columns = new List<ListColumn>();
        public Func<ListRow, ListColumn, Cell> CellOf;
        public Func<ListRow, string> SearchTextOf;
        public bool TallHeader;            // Processes / Users / Startup / App history style
        public bool MultiSelect;
        public bool ShowExpandos = true;
        public int RowHeightLogical = 24;
        public int GroupHeightLogical = 33;
        public int ChildIndentLogical = 22;
        public string EmptyText;
        public bool HeatSelected;          // keep heat colors on the selected row
        public string SortColumn;
        public bool SortDescending;
        public bool HeaderVisible = true;

        public event Action SelectionChanged;
        public event Action<ListRow> RowActivated;
        public event Action<ListRow, Point> RowContextMenu;          // row may be null (empty area)
        public event Action<ListColumn, Point> HeaderContextMenu;
        public event Action<ListColumn> SortRequested;
        public event Action ColumnsChanged;
        public event Func<Keys, bool> KeyCommand;                    // Delete, Enter etc.
        public event Action ExpansionChanged;

        Dpi D = new Dpi(96);
        readonly BackBuffer _buf = new BackBuffer();
        List<ListRow> _roots = new List<ListRow>();
        readonly List<ListRow> _flat = new List<ListRow>();
        readonly List<int> _rowY = new List<int>();
        int _contentH;
        readonly HashSet<string> _expanded = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> _selected = new HashSet<string>(StringComparer.Ordinal);
        string _focus, _anchor;
        int _scrollY, _scrollX;
        readonly ScrollBarModel _vs = new ScrollBarModel { Vertical = true };
        readonly ScrollBarModel _hs = new ScrollBarModel { Vertical = false };
        int _hotRow = -1, _hotHeader = -1, _pressedHeader = -1;
        bool _active;

        // mouse state (initialized on every button press)
        enum Drag { None, Resize, Reorder, HeaderPress, VThumb, HThumb, Rows }
        Drag _drag;
        Point _downPt;
        int _dragCol, _dragStartWidth, _dragThumbOffset, _dropIndex = -1;
        bool _dragMoved;
        readonly Timer _scrollRepeat = new Timer { Interval = 60 };
        int _repeatPart;
        string _typeAhead = "";
        long _typeAheadTick;

        public TreeList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
            TabStop = true;
            _scrollRepeat.Tick += (s, e) => { _scrollRepeat.Interval = 50; ScrollPart(_repeatPart); };
        }

        // ================================================================== metrics
        public void SetDpi(int dpi) { D = new Dpi(dpi); RebuildFlat(); Invalidate(); }
        public int RowH => D.S(RowHeightLogical);
        int GroupH => D.S(GroupHeightLogical);
        public int HeaderH => !HeaderVisible ? 0 : TallHeader ? D.S(46) : D.S(24);
        int SbW => Native.MetricForDpi(2 /*SM_CXVSCROLL*/, D.Value);
        int ColW(ListColumn c) => D.S(c.Width);
        Font RowFont => Theme.UiFont(D);
        Font GroupFont => Theme.GetFont(D, 13f, FontStyle.Regular);
        Font TotalFont => Theme.GetFont(D, 16f, FontStyle.Regular);
        public IEnumerable<ListColumn> VisibleColumns { get { foreach (var c in Columns) if (c.Visible) yield return c; } }

        Rectangle ViewRect
        {
            get
            {
                int w = ClientSize.Width - (_vs.Visible ? SbW : 0);
                int h = ClientSize.Height - HeaderH - (_hs.Visible ? SbW : 0);
                return new Rectangle(0, HeaderH, Math.Max(0, w), Math.Max(0, h));
            }
        }

        int TotalColumnsWidth { get { int w = 0; foreach (var c in VisibleColumns) w += ColW(c); return w; } }

        // ================================================================== data
        public IReadOnlyList<ListRow> Roots => _roots;
        public IReadOnlyList<ListRow> FlatRows => _flat;

        public void SetRoots(List<ListRow> roots)
        {
            _roots = roots ?? new List<ListRow>();
            RebuildFlat();
            // keep selection/focus only for rows that still exist
            if (_selected.Count > 0)
            {
                var alive = new HashSet<string>(StringComparer.Ordinal);
                foreach (var r in _flat) if (_selected.Contains(r.Key)) alive.Add(r.Key);
                if (alive.Count != _selected.Count)
                {
                    _selected.Clear(); foreach (var k in alive) _selected.Add(k);
                    SelectionChanged?.Invoke();
                }
            }
            Invalidate();
        }

        void RebuildFlat()
        {
            _flat.Clear(); _rowY.Clear();
            int y = 0;
            foreach (var r in _roots) AddFlat(r, null, ref y);
            _contentH = y;
            UpdateScrollBars();
        }

        void AddFlat(ListRow r, ListRow parent, ref int y)
        {
            r.Parent = parent;
            _flat.Add(r); _rowY.Add(y);
            y += r.IsGroup ? GroupH : RowH;
            if (r.Children == null) return;
            if (r.IsGroup || _expanded.Contains(r.Key))
                foreach (var c in r.Children) AddFlat(c, r, ref y);
            else foreach (var c in r.Children) c.Parent = r;
        }

        public bool IsExpanded(ListRow r) => r.IsGroup || _expanded.Contains(r.Key);
        public void SetExpanded(ListRow r, bool on)
        {
            if (r == null || r.IsGroup || !r.Expandable) return;
            if (on ? _expanded.Add(r.Key) : _expanded.Remove(r.Key))
            {
                RebuildFlat(); Invalidate(); ExpansionChanged?.Invoke();
            }
        }
        public void ExpandAll(bool on)
        {
            void Walk(List<ListRow> rows) { foreach (var r in rows) { if (r.Expandable && !r.IsGroup) { if (on) _expanded.Add(r.Key); else _expanded.Remove(r.Key); } if (r.Children != null) Walk(r.Children); } }
            Walk(_roots);
            RebuildFlat(); Invalidate(); ExpansionChanged?.Invoke();
        }

        public ListRow FocusedRow => FindRow(_focus);
        public ListRow FindRow(string key)
        {
            if (key == null) return null;
            foreach (var r in _flat) if (r.Key == key) return r;
            return null;
        }
        public List<ListRow> SelectedRows
        {
            get
            {
                var l = new List<ListRow>();
                foreach (var r in _flat) if (_selected.Contains(r.Key)) l.Add(r);
                return l;
            }
        }
        public ListRow SelectedRow { get { foreach (var r in _flat) if (_selected.Contains(r.Key)) return r; return null; } }
        public bool IsSelected(ListRow r) => r != null && _selected.Contains(r.Key);

        public void Select(ListRow r, bool ensureVisible = true)
        {
            _selected.Clear();
            if (r != null) { _selected.Add(r.Key); _focus = _anchor = r.Key; }
            if (ensureVisible && r != null) EnsureVisible(r);
            Invalidate();
            SelectionChanged?.Invoke();
        }

        public void SelectKey(string key)
        {
            // the row may sit inside a collapsed parent: expand the parents first
            ListRow target = null;
            void Walk(List<ListRow> rows, List<ListRow> path)
            {
                foreach (var r in rows)
                {
                    if (target != null) return;
                    if (r.Key == key) { target = r; foreach (var p in path) if (!p.IsGroup) _expanded.Add(p.Key); return; }
                    if (r.Children != null) { path.Add(r); Walk(r.Children, path); path.RemoveAt(path.Count - 1); }
                }
            }
            Walk(_roots, new List<ListRow>());
            if (target == null) return;
            RebuildFlat();
            Select(target);
        }

        public void EnsureVisible(ListRow r)
        {
            int i = _flat.IndexOf(r);
            if (i < 0) return;
            int top = _rowY[i], h = r.IsGroup ? GroupH : RowH, view = ViewRect.Height;
            if (top < _scrollY) _scrollY = top;
            else if (top + h > _scrollY + view) _scrollY = top + h - view;
            ClampScroll();
            Invalidate();
        }

        // ================================================================== scrolling
        void UpdateScrollBars()
        {
            int w = ClientSize.Width, h = ClientSize.Height - HeaderH;
            int colsW = TotalColumnsWidth;
            bool needV = _contentH > h;
            bool needH = colsW > w - (needV ? SbW : 0);
            if (needH && !needV) needV = _contentH > h - SbW;
            _vs.Max = _contentH; _vs.Page = h - (needH ? SbW : 0);
            _hs.Max = colsW; _hs.Page = w - (needV ? SbW : 0);
            if (!needV) _vs.Page = Math.Max(_vs.Page, _contentH + 1);
            if (!needH) _hs.Page = Math.Max(_hs.Page, colsW + 1);
            _vs.Bounds = new Rectangle(w - SbW, HeaderH, SbW, Math.Max(0, h - (needH ? SbW : 0)));
            _hs.Bounds = new Rectangle(0, ClientSize.Height - SbW, Math.Max(0, w - (needV ? SbW : 0)), SbW);
            ClampScroll();
        }

        void ClampScroll()
        {
            _scrollY = Math.Max(0, Math.Min(_scrollY, Math.Max(0, _contentH - ViewRect.Height)));
            _scrollX = Math.Max(0, Math.Min(_scrollX, Math.Max(0, TotalColumnsWidth - ViewRect.Width)));
            _vs.Value = _scrollY; _hs.Value = _scrollX;
        }

        public void ScrollBy(int dy) { _scrollY += dy; ClampScroll(); Invalidate(); }

        void ScrollPart(int part)
        {
            bool v = part < 10; int p = part % 10;
            int line = v ? RowH : D.S(20), page = v ? Math.Max(RowH, ViewRect.Height - RowH) : ViewRect.Width;
            int delta = p == 1 ? -line : p == 5 ? line : p == 2 ? -page : p == 4 ? page : 0;
            if (v) _scrollY += delta; else _scrollX += delta;
            ClampScroll(); Invalidate();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateScrollBars(); Invalidate(); }

        // ================================================================== painting
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            var clip = e.ClipRectangle;
            if (clip.Width <= 0 || clip.Height <= 0) return;
            var g = _buf.Begin(ClientSize, clip);
            try { Render(g, clip); }
            catch (Exception ex) { ErrorLog.Write("TreeList.Render", ex); }
            _buf.End(g, e.Graphics, clip);
        }

        /// <summary>Test hook / screenshots: render the whole control into a bitmap graphics.</summary>
        public void RenderTo(Graphics g) => Render(g, ClientRectangle);

        void Render(Graphics g, Rectangle clip)
        {
            UpdateScrollBars();
            var view = ViewRect;
            IntPtr hdc = g.GetHdc();
            try
            {
                Gdi.Fill(hdc, new Rectangle(0, 0, ClientSize.Width, ClientSize.Height), Theme.Surface);
                PaintRows(hdc, view);
                if (HeaderVisible) PaintHeader(hdc);
            }
            finally { g.ReleaseHdc(hdc); }
            // anti-aliased glyphs on top
            PaintGlyphs(g, view);
            _vs.Paint(g, D); _hs.Paint(g, D);
            if (_vs.Visible && _hs.Visible) Draw.Fill(g, new Rectangle(_vs.Bounds.X, _hs.Bounds.Y, SbW, SbW), Theme.ScrollTrack);
            if (_drag == Drag.Reorder && _dragMoved && _dropIndex >= 0)
            {
                int x = ColumnLeft(_dropIndex) - _scrollX;
                Draw.Fill(g, new Rectangle(x - 1, 0, D.S(2), HeaderH), Theme.Focus);
            }
        }

        int ColumnLeft(int visibleIndex)
        {
            int x = 0, i = 0;
            foreach (var c in VisibleColumns) { if (i++ == visibleIndex) return x; x += ColW(c); }
            return x;
        }

        int FirstVisibleIndex(int top)
        {
            int lo = 0, hi = _rowY.Count - 1, ans = 0;
            while (lo <= hi) { int m = (lo + hi) / 2; if (_rowY[m] <= top) { ans = m; lo = m + 1; } else hi = m - 1; }
            return ans;
        }

        void PaintRows(IntPtr hdc, Rectangle view)
        {
            if (_flat.Count == 0)
            {
                if (!string.IsNullOrEmpty(EmptyText))
                    Gdi.Text(hdc, EmptyText, RowFont, new Rectangle(view.X, view.Y + D.S(20), view.Width, D.S(24)), Theme.TextDim, Gdi.Center);
                return;
            }
            Native.SaveDC(hdc);
            Native.IntersectClipRect(hdc, view.Left, view.Top, view.Right, view.Bottom);
            int first = FirstVisibleIndex(_scrollY);
            var f = RowFont;
            for (int i = first; i < _flat.Count; i++)
            {
                int y = view.Y + _rowY[i] - _scrollY;
                if (y > view.Bottom) break;
                var r = _flat[i];
                int h = r.IsGroup ? GroupH : RowH;
                var rowRect = new Rectangle(0, y, Math.Max(view.Width, TotalColumnsWidth), h);
                if (r.IsGroup) { PaintGroupRow(hdc, r, rowRect); continue; }
                bool sel = _selected.Contains(r.Key);
                bool hot = i == _hotRow && _drag == Drag.None;
                if (sel || hot)
                {
                    var full = new Rectangle(0, y, Math.Max(view.Width, TotalColumnsWidth - _scrollX), h);
                    Gdi.Fill(hdc, full, sel ? (_active || Focused ? Theme.RowSelected : Theme.RowSelectedInactive) : Theme.RowHover);
                    if (sel && (_active || Focused)) Gdi.Frame(hdc, full, Theme.RowSelectedBorder);
                }
                int x = -_scrollX;
                bool firstCol = true;
                foreach (var c in VisibleColumns)
                {
                    int w = ColW(c);
                    var cr = new Rectangle(x, y, w, h);
                    if (cr.Right >= 0 && cr.Left <= view.Right)
                    {
                        var cell = CellOf != null ? CellOf(r, c) : Cell.Of("");
                        if (!double.IsNaN(cell.Heat) && (!sel || HeatSelected) && !hot)
                            Gdi.Fill(hdc, new Rectangle(cr.X, cr.Y, cr.Width - 1, cr.Height), Theme.Heat(cell.Heat));
                        if (firstCol) PaintFirstCell(hdc, r, cr, cell, sel, hot);
                        else
                        {
                            var tc = cell.Fore.IsEmpty ? (r.Dim ? Theme.TextDim : Theme.Text) : cell.Fore;
                            int pad = D.S(6);
                            var tr = new Rectangle(cr.X + pad, cr.Y, cr.Width - pad * 2, cr.Height);
                            if (cell.Glyph == 1) tr = new Rectangle(tr.X + D.S(18), tr.Y, tr.Width - D.S(18), tr.Height);
                            Gdi.Text(hdc, cell.Text, f, tr, tc, c.Right ? Gdi.Right : Gdi.Left);
                        }
                    }
                    firstCol = false;
                    x += w;
                }
            }
            Native.RestoreDC(hdc, -1);
        }

        void PaintFirstCell(IntPtr hdc, ListRow r, Rectangle cr, Cell cell, bool sel, bool hot)
        {
            int x = cr.X + D.S(6) + r.Level * D.S(ChildIndentLogical);
            if (ShowExpandos) x += D.S(18);
            int isz = D.S(16);
            if (r.Icon != IntPtr.Zero) { Gdi.Icon(hdc, r.Icon, x, cr.Y + (cr.Height - isz) / 2, isz); x += isz + D.S(8); }
            else if (r.Level == 0 && !ShowExpandos) { }
            var tc = cell.Fore.IsEmpty ? (r.Dim ? Theme.TextDim : Theme.Text) : cell.Fore;
            Gdi.Text(hdc, cell.Text, RowFont, new Rectangle(x, cr.Y, cr.Right - x - D.S(4), cr.Height), tc, Gdi.Left);
        }

        void PaintGroupRow(IntPtr hdc, ListRow r, Rectangle rr)
        {
            bool sel = _selected.Contains(r.Key);
            if (sel) { Gdi.Fill(hdc, new Rectangle(0, rr.Y, ViewRect.Width, rr.Height), _active || Focused ? Theme.RowSelected : Theme.RowSelectedInactive); }
            var f = GroupFont;
            var tr = new Rectangle(D.S(8) - _scrollX, rr.Y + D.S(10), rr.Width, rr.Height - D.S(17));
            Gdi.Text(hdc, r.GroupText, f, new Rectangle(tr.X, rr.Y + D.S(6), D.S(600), rr.Height - D.S(6)), Theme.GroupText, Gdi.Left);
        }

        void PaintHeader(IntPtr hdc)
        {
            int hh = HeaderH;
            Gdi.Fill(hdc, new Rectangle(0, 0, ClientSize.Width, hh), Theme.Surface);
            int x = -_scrollX, i = 0;
            var f = RowFont;
            foreach (var c in VisibleColumns)
            {
                int w = ColW(c);
                var r = new Rectangle(x, 0, w, hh);
                if (r.Right >= 0 && r.Left <= ClientSize.Width)
                {
                    Color bg = TallHeader && c.Resource ? Theme.HeaderHeat(c.HeaderHeat) : Theme.Surface;
                    if (i == _pressedHeader && _drag == Drag.HeaderPress) bg = Theme.HeaderPressed;
                    else if (i == _hotHeader && _drag == Drag.None) bg = Theme.Blend(bg, Theme.HeaderHover, 0.6f);
                    Gdi.Fill(hdc, new Rectangle(r.X, r.Y, r.Width - 1, r.Height), bg);
                    int pad = D.S(6);
                    if (TallHeader)
                    {
                        var titleR = new Rectangle(r.X + pad, r.Bottom - D.S(22), r.Width - pad * 2, D.S(18));
                        if (c.Resource && !string.IsNullOrEmpty(c.HeaderTotal))
                            Gdi.Text(hdc, c.HeaderTotal, TotalFont, new Rectangle(r.X + pad, D.S(6), r.Width - pad * 2, D.S(22)), Theme.Text, c.Right ? Gdi.Right : Gdi.Left);
                        Gdi.Text(hdc, c.Title, f, titleR, Theme.HeaderText, c.Right ? Gdi.Right : Gdi.Left);
                    }
                    else
                    {
                        Gdi.Text(hdc, c.Title, f, new Rectangle(r.X + pad, 0, r.Width - pad * 2, hh), Theme.HeaderText, c.Right ? Gdi.Right : Gdi.Left);
                    }
                    Gdi.Fill(hdc, new Rectangle(r.Right - 1, TallHeader ? D.S(4) : D.S(3), 1, hh - (TallHeader ? D.S(8) : D.S(6))), Theme.HeaderDivider);
                }
                x += w; i++;
            }
            Gdi.Fill(hdc, new Rectangle(0, hh - 1, ClientSize.Width, 1), Theme.HeaderDivider);
        }

        void PaintGlyphs(Graphics g, Rectangle view)
        {
            g.SetClip(view);
            if (ShowExpandos && _flat.Count > 0)
            {
                int first = FirstVisibleIndex(_scrollY);
                for (int i = first; i < _flat.Count; i++)
                {
                    int y = view.Y + _rowY[i] - _scrollY;
                    if (y > view.Bottom) break;
                    var r = _flat[i];
                    if (r.IsGroup || !r.Expandable) continue;
                    var er = ExpandoRect(r, y);
                    bool open = _expanded.Contains(r.Key);
                    Draw.Chevron(g, er.X + er.Width / 2f, er.Y + er.Height / 2f, D.F(8), Theme.TextDim, open ? 0 : 2, D.F(1.1f));
                }
            }
            // status glyphs (leaf for suspended processes)
            if (CellOf != null && _flat.Count > 0)
            {
                int first = FirstVisibleIndex(_scrollY);
                for (int i = first; i < _flat.Count; i++)
                {
                    int y = view.Y + _rowY[i] - _scrollY;
                    if (y > view.Bottom) break;
                    var r = _flat[i];
                    if (r.IsGroup) continue;
                    int x = -_scrollX;
                    foreach (var c in VisibleColumns)
                    {
                        int w = ColW(c);
                        if (c.Id == "status")
                        {
                            var cell = CellOf(r, c);
                            if (cell.Glyph == 1) Glyphs.Leaf(g, new RectangleF(x + D.S(6), y + (RowH - D.S(14)) / 2f, D.S(14), D.S(14)));
                        }
                        x += w;
                    }
                }
            }
            g.ResetClip();
            if (!HeaderVisible) return;
            // sort indicator: a small chevron centered at the top of the sorted column's header
            int hx = -_scrollX;
            foreach (var c in VisibleColumns)
            {
                int w = ColW(c);
                if (c.Id == SortColumn)
                    Draw.Chevron(g, hx + w / 2f, D.F(TallHeader ? 4.5f : 4f), D.F(8), Theme.TextDim, SortDescending ? 0 : 1, D.F(1f));
                hx += w;
            }
        }

        Rectangle ExpandoRect(ListRow r, int y)
        {
            int x = -_scrollX + D.S(6) + r.Level * D.S(ChildIndentLogical);
            int h = r.IsGroup ? GroupH : RowH;
            return new Rectangle(x, y, D.S(16), h);
        }

        // ================================================================== hit testing
        int RowAt(int y)
        {
            var view = ViewRect;
            if (y < view.Top || y >= view.Bottom) return -1;
            int cy = y - view.Top + _scrollY;
            if (cy >= _contentH) return -1;
            int i = FirstVisibleIndex(cy);
            return i < _flat.Count ? i : -1;
        }

        int HeaderAt(int x, out bool onEdge)
        {
            onEdge = false;
            int cx = x + _scrollX, left = 0, i = 0, grip = D.S(4);
            ListColumn prev = null;
            foreach (var c in VisibleColumns)
            {
                int w = ColW(c);
                if (Math.Abs(cx - (left + w)) <= grip) { onEdge = true; return i; }
                if (cx >= left && cx < left + w) return i;
                left += w; i++; prev = c;
            }
            return -1;
        }

        ListColumn VisibleColumnAt(int vi) { int i = 0; foreach (var c in VisibleColumns) if (i++ == vi) return c; return null; }

        public ListColumn ColumnAtPoint(Point p) { int i = HeaderAt(p.X, out _); return i >= 0 ? VisibleColumnAt(i) : null; }

        // ================================================================== mouse
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            // every press starts from a clean drag state
            _drag = Drag.None; _dragMoved = false; _downPt = e.Location; _dropIndex = -1; _pressedHeader = -1;
            Tips.Hide();
            int part = _vs.HitTest(e.Location);
            if (part > 0) { HandleScrollDown(_vs, part, e.Location, 0); return; }
            part = _hs.HitTest(e.Location);
            if (part > 0) { HandleScrollDown(_hs, part, e.Location, 10); return; }

            if (e.Y < HeaderH)
            {
                int hi = HeaderAt(e.X, out bool edge);
                if (e.Button != MouseButtons.Left) return;
                if (edge && hi >= 0)
                {
                    _drag = Drag.Resize; _dragCol = hi; _dragStartWidth = VisibleColumnAt(hi).Width; Capture = true; return;
                }
                if (hi >= 0) { _drag = Drag.HeaderPress; _pressedHeader = hi; _dragCol = hi; Capture = true; Invalidate(); }
                return;
            }

            int ri = RowAt(e.Y);
            if (ri < 0)
            {
                if (e.Button == MouseButtons.Left && !Native.CtrlDown) { _selected.Clear(); _focus = null; Invalidate(); SelectionChanged?.Invoke(); }
                return;
            }
            var row = _flat[ri];
            if (e.Button == MouseButtons.Left && ShowExpandos && row.Expandable && !row.IsGroup && ExpandoRect(row, ViewRect.Y + _rowY[ri] - _scrollY).Contains(e.Location))
            {
                SetExpanded(row, !_expanded.Contains(row.Key));
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                if (!_selected.Contains(row.Key)) ClickSelect(row, false, false);
                return;
            }
            if (e.Button == MouseButtons.Left)
            {
                ClickSelect(row, Native.CtrlDown && MultiSelect, Native.ShiftDown && MultiSelect);
                _drag = Drag.Rows; Capture = true;
            }
        }

        void HandleScrollDown(ScrollBarModel sb, int part, Point p, int offset)
        {
            sb.Pressed = part;
            if (part == 3)
            {
                _drag = sb.Vertical ? Drag.VThumb : Drag.HThumb;
                var th = sb.Thumb;
                _dragThumbOffset = sb.Vertical ? p.Y - th.Y : p.X - th.X;
                Capture = true;
            }
            else
            {
                _repeatPart = offset + part;
                ScrollPart(_repeatPart);
                _scrollRepeat.Interval = 350; _scrollRepeat.Start();
                Capture = true;
            }
            Invalidate();
        }

        void ClickSelect(ListRow row, bool toggle, bool range)
        {
            if (range && _anchor != null)
            {
                int a = _flat.FindIndex(x => x.Key == _anchor), b = _flat.IndexOf(row);
                if (a >= 0 && b >= 0)
                {
                    if (!toggle) _selected.Clear();
                    for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++) if (!_flat[i].IsGroup) _selected.Add(_flat[i].Key);
                }
                _focus = row.Key;
            }
            else if (toggle)
            {
                if (!_selected.Remove(row.Key)) _selected.Add(row.Key);
                _focus = _anchor = row.Key;
            }
            else
            {
                _selected.Clear(); _selected.Add(row.Key); _focus = _anchor = row.Key;
            }
            Invalidate();
            SelectionChanged?.Invoke();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            switch (_drag)
            {
                case Drag.Resize:
                    {
                        var c = VisibleColumnAt(_dragCol);
                        int w = _dragStartWidth + (int)Math.Round((e.X - _downPt.X) / D.Scale);
                        w = Math.Max(c.MinWidth, w);
                        if (w != c.Width) { c.Width = w; UpdateScrollBars(); Invalidate(); }
                        return;
                    }
                case Drag.HeaderPress:
                case Drag.Reorder:
                    {
                        if (!_dragMoved && (Math.Abs(e.X - _downPt.X) > Native.GetSystemMetrics(Native.SM_CXDRAG)))
                        {
                            var c = VisibleColumnAt(_dragCol);
                            if (c != null && !c.Fixed) { _drag = Drag.Reorder; _dragMoved = true; }
                        }
                        if (_drag == Drag.Reorder)
                        {
                            int cx = e.X + _scrollX, left = 0, i = 0, drop = 0;
                            foreach (var c in VisibleColumns) { int w = ColW(c); if (cx > left + w / 2) drop = i + 1; left += w; i++; }
                            if (VisibleColumnAt(0)?.Fixed == true) drop = Math.Max(1, drop);
                            if (drop != _dropIndex) { _dropIndex = drop; Invalidate(); }
                        }
                        return;
                    }
                case Drag.VThumb:
                    _scrollY = _vs.ValueFromThumb(e.Y - _dragThumbOffset); ClampScroll(); Invalidate(); return;
                case Drag.HThumb:
                    _scrollX = _hs.ValueFromThumb(e.X - _dragThumbOffset); ClampScroll(); Invalidate(); return;
                case Drag.Rows:
                    return;
            }
            // hover tracking
            int vh = _vs.HitTest(e.Location), hh2 = _hs.HitTest(e.Location);
            if (vh != _vs.Hot || hh2 != _hs.Hot) { _vs.Hot = vh; _hs.Hot = hh2; Invalidate(); }
            int hot = vh > 0 || hh2 > 0 ? -1 : RowAt(e.Y);
            if (hot >= 0 && _flat[hot].IsGroup) hot = -1;
            if (hot != _hotRow) { _hotRow = hot; Invalidate(); }
            int hdr = -1; bool edge = false;
            if (e.Y < HeaderH) hdr = HeaderAt(e.X, out edge);
            Cursor = e.Y < HeaderH && edge ? Cursors.SizeWE : Cursors.Default;
            if (hdr != _hotHeader) { _hotHeader = hdr; Invalidate(new Rectangle(0, 0, ClientSize.Width, HeaderH)); }
            if (hdr >= 0 && !edge)
            {
                var c = VisibleColumnAt(hdr);
                string tip = c.Resource && TallHeader && e.Y < D.S(28) && c.HeaderTooltip != null ? c.HeaderTooltip : c.Tooltip;
                if (!string.IsNullOrEmpty(tip)) Tips.Request(c.Id + (tip == c.HeaderTooltip ? "#t" : ""), tip, null, PointToScreen(new Point(e.X, HeaderH + D.S(2))), D.Value);
                else Tips.Hide();
            }
            else if (e.Y < HeaderH) Tips.Hide();
            else Tips.Hide();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var drag = _drag;
            _drag = Drag.None; Capture = false; _scrollRepeat.Stop(); _vs.Pressed = _hs.Pressed = 0;
            if (drag == Drag.Resize) { ColumnsChanged?.Invoke(); Invalidate(); return; }
            if (drag == Drag.Reorder && _dragMoved)
            {
                var c = VisibleColumnAt(_dragCol);
                var target = VisibleColumnAt(_dropIndex);
                Columns.Remove(c);
                int idx = target == null ? Columns.Count : Columns.IndexOf(target);
                Columns.Insert(Math.Max(0, idx), c);
                _dropIndex = -1; _pressedHeader = -1;
                ColumnsChanged?.Invoke(); Invalidate(); return;
            }
            if (drag == Drag.HeaderPress)
            {
                _pressedHeader = -1; Invalidate();
                int hi = HeaderAt(e.X, out bool edge);
                if (hi == _dragCol && e.Y < HeaderH && e.Y >= 0) SortRequested?.Invoke(VisibleColumnAt(hi));
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                if (e.Y < HeaderH) { var c = ColumnAtPoint(e.Location); HeaderContextMenu?.Invoke(c, PointToScreen(e.Location)); return; }
                if (_vs.HitTest(e.Location) > 0 || _hs.HitTest(e.Location) > 0) return;
                int ri = RowAt(e.Y);
                RowContextMenu?.Invoke(ri >= 0 ? _flat[ri] : null, PointToScreen(e.Location));
            }
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left || e.Y < HeaderH) return;
            if (_vs.HitTest(e.Location) > 0 || _hs.HitTest(e.Location) > 0) return;
            int ri = RowAt(e.Y);
            if (ri < 0) return;
            var row = _flat[ri];
            if (ShowExpandos && row.Expandable && ExpandoRect(row, ViewRect.Y + _rowY[ri] - _scrollY).Contains(e.Location)) return;
            RowActivated?.Invoke(row);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hotRow = -1; _hotHeader = -1; _vs.Hot = _hs.Hot = 0;
            Tips.Hide();
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int lines = SystemInformation.MouseWheelScrollLines;
            if (lines < 0) lines = Math.Max(1, ViewRect.Height / RowH);
            if (Native.ShiftDown) { _scrollX -= Math.Sign(e.Delta) * D.S(40); }
            else _scrollY -= e.Delta * lines * RowH / 120;
            ClampScroll(); Invalidate();
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        public void SetActive(bool active) { _active = active; Invalidate(); }

        // ================================================================== keyboard
        protected override bool IsInputKey(Keys k)
        {
            switch (k & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.Left: case Keys.Right: case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End: case Keys.Enter: case Keys.Space:
                    return true;
            }
            return base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (HandleKey(e.KeyData)) e.Handled = true;
        }

        public bool HandleKey(Keys k)
        {
            if (KeyCommand != null && KeyCommand(k)) return true;
            var code = k & Keys.KeyCode;
            bool shift = (k & Keys.Shift) != 0 && MultiSelect, ctrl = (k & Keys.Control) != 0;
            int idx = _focus == null ? -1 : _flat.FindIndex(r => r.Key == _focus);
            int target = idx;
            int page = Math.Max(1, ViewRect.Height / RowH - 1);
            switch (code)
            {
                case Keys.Down: target = NextSelectable(idx, 1); break;
                case Keys.Up: target = idx < 0 ? NextSelectable(-1, 1) : NextSelectable(idx, -1); break;
                case Keys.PageDown: target = Math.Min(_flat.Count - 1, Math.Max(0, idx) + page); break;
                case Keys.PageUp: target = Math.Max(0, idx - page); break;
                case Keys.Home: target = NextSelectable(-1, 1); break;
                case Keys.End: target = NextSelectable(_flat.Count, -1); break;
                case Keys.Right:
                    if (idx >= 0)
                    {
                        var r = _flat[idx];
                        if (r.Expandable && !r.IsGroup && !_expanded.Contains(r.Key)) { SetExpanded(r, true); return true; }
                        if (r.Expandable && _expanded.Contains(r.Key)) target = idx + 1;
                    }
                    else { _scrollX += D.S(40); ClampScroll(); Invalidate(); return true; }
                    break;
                case Keys.Left:
                    if (idx >= 0)
                    {
                        var r = _flat[idx];
                        if (r.Expandable && !r.IsGroup && _expanded.Contains(r.Key)) { SetExpanded(r, false); return true; }
                        if (r.Parent != null && !r.Parent.IsGroup) target = _flat.IndexOf(r.Parent);
                    }
                    else { _scrollX -= D.S(40); ClampScroll(); Invalidate(); return true; }
                    break;
                case Keys.Enter:
                    if (idx >= 0) RowActivated?.Invoke(_flat[idx]);
                    return true;
                case Keys.Apps:
                    ShowContextForFocus(); return true;
                case Keys.F10:
                    if ((k & Keys.Shift) != 0) { ShowContextForFocus(); return true; }
                    return false;
                case Keys.A:
                    if (ctrl && MultiSelect) { _selected.Clear(); foreach (var r in _flat) if (!r.IsGroup) _selected.Add(r.Key); Invalidate(); SelectionChanged?.Invoke(); return true; }
                    return false;
                default: return false;
            }
            if (target < 0 || target >= _flat.Count) return true;
            if (_flat[target].IsGroup) { int t2 = NextSelectable(target, 1); target = t2 != target ? t2 : NextSelectable(target, -1); }
            if (target < 0 || target >= _flat.Count || _flat[target].IsGroup) return true;
            var row = _flat[target];
            if (shift) ClickSelect(row, false, true);
            else if (ctrl && MultiSelect) { _focus = row.Key; Invalidate(); }
            else ClickSelect(row, false, false);
            EnsureVisible(row);
            return true;
        }

        int NextSelectable(int from, int dir)
        {
            for (int i = from + dir; i >= 0 && i < _flat.Count; i += dir) if (!_flat[i].IsGroup) return i;
            return from;
        }

        void ShowContextForFocus()
        {
            var r = FocusedRow ?? SelectedRow;
            Point p;
            if (r != null)
            {
                int i = _flat.IndexOf(r);
                p = PointToScreen(new Point(D.S(40), ViewRect.Y + _rowY[i] - _scrollY + RowH / 2));
            }
            else p = PointToScreen(new Point(D.S(40), ViewRect.Y + D.S(10)));
            RowContextMenu?.Invoke(r, p);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (char.IsControl(e.KeyChar) || SearchTextOf == null) return;
            long now = Environment.TickCount;
            bool fresh = now - _typeAheadTick > 1000;
            _typeAheadTick = now;
            _typeAhead = fresh ? e.KeyChar.ToString() : _typeAhead + e.KeyChar;
            int start = _focus == null ? 0 : Math.Max(0, _flat.FindIndex(r => r.Key == _focus));
            // repeating the same letter cycles through rows starting with it
            bool cycle = _typeAhead.Length > 1 && _typeAhead.Trim(_typeAhead[0]).Length == 0;
            string needle = cycle ? _typeAhead.Substring(0, 1) : _typeAhead;
            int n = _flat.Count;
            for (int k = 0; k < n; k++)
            {
                int i = (start + k + (fresh || cycle ? 1 : 0)) % n;
                var r = _flat[i];
                if (r.IsGroup) continue;
                var s = SearchTextOf(r);
                if (s != null && s.StartsWith(needle, StringComparison.CurrentCultureIgnoreCase)) { ClickSelect(r, false, false); EnsureVisible(r); break; }
            }
            e.Handled = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _buf.Dispose(); _scrollRepeat.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
