using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TaskManagerDark
{
    /// <summary>Non-activating, shadowed, custom drawn popup window (menus, tooltips).</summary>
    public class PopupWindow : Form
    {
        protected Dpi D;
        readonly BackBuffer _buf = new BackBuffer();
        public Rectangle AnchorScreen;          // the button that opened us (click on it again toggles)
        public PopupWindow ParentPopup;
        public PopupWindow ChildPopup;
        public Action Dismissed;
        public bool IsTooltip;
        /// <summary>Activatable popups (the tray menu) take focus so a click anywhere else on the desktop closes them.</summary>
        public bool Activatable;
        protected bool Shadow = true;

        public PopupWindow(int dpi)
        {
            D = new Dpi(dpi);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            ControlBox = false;
            MinimizeBox = MaximizeBox = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Popup;
        }

        public int DpiValue => D.Value;
        protected override bool ShowWithoutActivation => !Activatable;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style = unchecked((int)0x80000000);            // WS_POPUP
                cp.ExStyle |= 0x80 | 0x8;                         // WS_EX_TOOLWINDOW | WS_EX_TOPMOST
                if (!Activatable) cp.ExStyle |= 0x08000000;       // WS_EX_NOACTIVATE
                if (Shadow) cp.ClassStyle |= 0x00020000;          // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE && !Activatable) { m.Result = (IntPtr)3; return; } // MA_NOACTIVATE
            if (m.Msg == Native.WM_NCHITTEST && IsTooltip) { m.Result = (IntPtr)Native.HTTRANSPARENT; return; }
            base.WndProc(ref m);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (Activatable) BeginInvoke(new Action(() => { if (!Popups.OwnsForeground()) Popups.CloseAll(); }));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = _buf.Begin(ClientSize, e.ClipRectangle);
            try { Render(g); } catch (Exception ex) { ErrorLog.Write("Popup.Render", ex); }
            _buf.End(g, e.Graphics, e.ClipRectangle);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected virtual void Render(Graphics g)
        {
            Draw.Fill(g, ClientRectangle, Theme.Popup);
            Draw.Rect(g, ClientRectangle, Theme.PopupBorder);
        }

        /// <summary>Keyboard routed from the message filter. Return true when consumed.</summary>
        public virtual bool HandleKey(Keys k) => false;
        public virtual bool HandleChar(char c) => false;
        public virtual bool HandleWheel(int delta) => false;
        /// <summary>Test hook: render the popup into a bitmap.</summary>
        public void RenderTo(Graphics g) => Render(g);

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            Dismissed?.Invoke();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _buf.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Owns the stack of open popups and implements menu-like dismissal.</summary>
    public sealed class Popups : IMessageFilter
    {
        static readonly List<PopupWindow> _stack = new List<PopupWindow>();
        static Popups _filter;
        public static Form MainWindow;
        public static bool AnyOpen => _stack.Count > 0;
        public static PopupWindow Top => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;
        public static IEnumerable<PopupWindow> All => _stack;
        /// <summary>Raised when the last popup closes (menu bar uses it to drop its highlight).</summary>
        public static event Action AllClosed;

        public static void Install()
        {
            if (_filter != null) return;
            _filter = new Popups();
            Application.AddMessageFilter(_filter);
        }

        public static void Open(PopupWindow p, PopupWindow parent = null)
        {
            if (parent == null) CloseAll();
            else { CloseChildren(parent); parent.ChildPopup = p; p.ParentPopup = parent; }
            _stack.Add(p);
            Tips.Hide();
            if (p.Activatable)
            {
                p.Show();
                Native.SetForegroundWindow(p.Handle);
            }
            else if (MainWindow != null && MainWindow.Visible) p.Show(MainWindow);
            else p.Show();
        }

        public static bool OwnsForeground()
        {
            var fg = Native.GetForegroundWindow();
            foreach (var p in _stack) if (p.IsHandleCreated && p.Handle == fg) return true;
            return false;
        }

        public static void CloseChildren(PopupWindow parent)
        {
            int i = _stack.IndexOf(parent);
            if (i < 0) return;
            for (int k = _stack.Count - 1; k > i; k--) CloseAt(k);
            parent.ChildPopup = null;
        }

        public static void Close(PopupWindow p)
        {
            int i = _stack.IndexOf(p);
            if (i < 0) return;
            for (int k = _stack.Count - 1; k >= i; k--) CloseAt(k);
            if (_stack.Count == 0) AllClosed?.Invoke();
        }

        static void CloseAt(int k)
        {
            var p = _stack[k];
            _stack.RemoveAt(k);
            if (p.ParentPopup != null && p.ParentPopup.ChildPopup == p) p.ParentPopup.ChildPopup = null;
            try { p.Close(); p.Dispose(); } catch (Exception ex) { ErrorLog.Write("Popups.Close", ex); }
        }

        public static void CloseAll()
        {
            bool any = _stack.Count > 0;
            for (int k = _stack.Count - 1; k >= 0; k--) CloseAt(k);
            Tips.Hide();
            if (any) AllClosed?.Invoke();
        }

        static PopupWindow FindByHandle(IntPtr h)
        {
            while (h != IntPtr.Zero)
            {
                foreach (var p in _stack) if (p.IsHandleCreated && p.Handle == h) return p;
                h = Native.GetAncestor(h, 1); // GA_PARENT
                if (h == IntPtr.Zero) break;
            }
            return null;
        }

        /// <summary>Lets the menu bar decide what a click on itself means while a menu is open.</summary>
        public static Func<IntPtr, Point, bool> ClickInterceptor;

        public bool PreFilterMessage(ref Message m)
        {
            switch (m.Msg)
            {
                case Native.WM_LBUTTONDOWN: case Native.WM_RBUTTONDOWN: case Native.WM_MBUTTONDOWN:
                case Native.WM_NCLBUTTONDOWN: case Native.WM_NCRBUTTONDOWN:
                case Native.WM_LBUTTONDBLCLK: case Native.WM_RBUTTONDBLCLK:
                    {
                        Tips.Hide();
                        if (_stack.Count == 0) return false;
                        if (FindByHandle(m.HWnd) != null) return false;
                        Native.GetCursorPos(out var pt);
                        if (ClickInterceptor != null && ClickInterceptor(m.HWnd, new Point(pt.X, pt.Y))) return true;
                        CloseAll();
                        // the dismissing click is eaten (like real menus)
                        return true;
                    }
                case Native.WM_KEYDOWN: case Native.WM_SYSKEYDOWN:
                    {
                        if (_stack.Count == 0) { Tips.Hide(); return false; }
                        var k = (Keys)(int)m.WParam | Control.ModifierKeys;
                        var top = Top;
                        if (top.HandleKey(k)) return true;
                        if ((k & Keys.KeyCode) == Keys.Escape) { Close(top); return true; }
                        if ((k & Keys.KeyCode) == Keys.Menu || (k & Keys.KeyCode) == Keys.F10) { CloseAll(); return true; }
                        return top is MenuPopup;   // menus swallow everything else
                    }
                case Native.WM_CHAR: case Native.WM_SYSCHAR:
                    if (_stack.Count == 0) return false;
                    if (Top.HandleChar((char)(int)m.WParam)) return true;
                    return Top is MenuPopup;
                case Native.WM_MOUSEWHEEL:
                    if (_stack.Count == 0) return false;
                    {
                        Native.GetCursorPos(out var pt);
                        for (int i = _stack.Count - 1; i >= 0; i--)
                            if (_stack[i].Bounds.Contains(pt.X, pt.Y)) { _stack[i].HandleWheel(Native.HiWord(m.WParam)); return true; }
                        return true;
                    }
            }
            return false;
        }
    }

    // ===================================================================== menus

    public sealed class MenuItem
    {
        public string Text;
        public string Shortcut;
        public Func<bool> Enabled;
        public Func<bool> Checked;
        public bool Radio;                      // checked shown as a dot
        public bool Bold;                       // the default command
        public Action Click;
        public List<MenuItem> Sub;
        public bool Separator;
        public int Id;                          // the original Task Manager command id (for tests)

        public MenuItem() { }
        public MenuItem(string text, Action click, Func<bool> enabled = null) { SetText(text); Click = click; Enabled = enabled; }

        void SetText(string t)
        {
            int tab = t?.IndexOf('\t') ?? -1;
            if (tab >= 0) { Text = t.Substring(0, tab); Shortcut = t.Substring(tab + 1); } else Text = t;
        }

        public static MenuItem Sep() => new MenuItem { Separator = true };
        public static MenuItem Check(string text, Func<bool> isChecked, Action click) { var m = new MenuItem(text, click); m.Checked = isChecked; return m; }
        public static MenuItem RadioItem(string text, Func<bool> isChecked, Action click) { var m = new MenuItem(text, click); m.Checked = isChecked; m.Radio = true; return m; }
        public static MenuItem Popup(string text, List<MenuItem> sub) { var m = new MenuItem(text, null); m.Sub = sub; return m; }
        public bool IsEnabled => Enabled == null || Enabled();
        public bool IsChecked => Checked != null && Checked();
        public bool Selectable => !Separator && IsEnabled;
        public string Plain => Clean(Text);
        public char Mnemonic
        {
            get
            {
                if (Text == null) return '\0';
                for (int i = 0; i < Text.Length - 1; i++)
                {
                    if (Text[i] != '&') continue;
                    if (Text[i + 1] == '&') { i++; continue; }
                    return char.ToUpperInvariant(Text[i + 1]);
                }
                return '\0';
            }
        }
        public static string Clean(string s) => s?.Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&");
    }

    public sealed class MenuPopup : PopupWindow
    {
        readonly List<MenuItem> _items;
        readonly List<Rectangle> _rects = new List<Rectangle>();
        int _hot = -1;
        readonly Timer _subTimer = new Timer { Interval = 350 };
        int _subFor = -1;
        public int MinWidth;
        bool _keyboardMode;
        public bool ShowAccelerators;
        /// <summary>Left/Right at the top level move along the menu bar.</summary>
        public Action<int> NavigateBar;

        public MenuPopup(int dpi, List<MenuItem> items) : base(dpi)
        {
            _items = items;
            _subTimer.Tick += (s, e) => { _subTimer.Stop(); if (_hot >= 0 && _items[_hot].Sub != null) OpenSub(_hot, false); };
        }

        public IReadOnlyList<MenuItem> Items => _items;
        public int Hot => _hot;

        int RowH(MenuItem it) => it.Separator ? D.S(9) : D.S(22);
        int CheckCol => D.S(28);

        Size LayoutItems()
        {
            var f = Theme.UiFont(D);
            var fb = Theme.UiFontBold(D);
            int textW = 0, shortW = 0;
            foreach (var it in _items)
            {
                if (it.Separator) continue;
                textW = Math.Max(textW, Draw.Measure(it.Plain, it.Bold ? fb : f).Width);
                if (!string.IsNullOrEmpty(it.Shortcut)) shortW = Math.Max(shortW, Draw.Measure(it.Shortcut, f).Width);
            }
            int w = D.S(3) + CheckCol + textW + (shortW > 0 ? D.S(28) + shortW : 0) + D.S(30) + D.S(3);
            w = Math.Max(w, MinWidth > 0 ? MinWidth : D.S(120));
            _rects.Clear();
            int y = D.S(3);
            foreach (var it in _items)
            {
                int h = RowH(it);
                _rects.Add(new Rectangle(D.S(3), y, w - D.S(6), h));
                y += h;
            }
            return new Size(w, y + D.S(3));
        }

        public void ShowBelow(Rectangle anchorScreen, PopupWindow parent = null, bool beside = false)
        {
            var sz = LayoutItems();
            AnchorScreen = parent == null ? anchorScreen : Rectangle.Empty;
            var wa = Screen.FromRectangle(anchorScreen).WorkingArea;
            int x, y;
            if (beside || parent != null)
            {
                x = anchorScreen.Right - D.S(2); y = anchorScreen.Top - D.S(3);
                if (x + sz.Width > wa.Right) x = anchorScreen.Left - sz.Width + D.S(2);
                if (y + sz.Height > wa.Bottom) y = wa.Bottom - sz.Height;
            }
            else
            {
                x = anchorScreen.Left; y = anchorScreen.Bottom;
                if (y + sz.Height > wa.Bottom && anchorScreen.Top - sz.Height >= wa.Top) y = anchorScreen.Top - sz.Height;
                if (x + sz.Width > wa.Right) x = wa.Right - sz.Width;
            }
            x = Math.Max(wa.Left, x); y = Math.Max(wa.Top, Math.Min(y, wa.Bottom - sz.Height));
            Bounds = new Rectangle(x, y, sz.Width, sz.Height);
            Popups.Open(this, parent);
        }

        public void ShowAtPoint(Point screen)
        {
            var sz = LayoutItems();
            var wa = Screen.FromPoint(screen).WorkingArea;
            int x = screen.X, y = screen.Y;
            if (x + sz.Width > wa.Right) x = Math.Max(wa.Left, screen.X - sz.Width);
            if (y + sz.Height > wa.Bottom) y = Math.Max(wa.Top, screen.Y - sz.Height);
            Bounds = new Rectangle(x, y, sz.Width, sz.Height);
            Popups.Open(this);
        }

        protected override void Render(Graphics g)
        {
            Draw.Fill(g, ClientRectangle, Theme.Popup);
            Draw.Rect(g, ClientRectangle, Theme.PopupBorder);
            var f = Theme.UiFont(D);
            var fb = Theme.UiFontBold(D);
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i]; var r = _rects[i];
                if (it.Separator) { Draw.HLine(g, r.Left + CheckCol, r.Right - D.S(2), r.Top + r.Height / 2, Theme.MenuSep); continue; }
                bool en = it.IsEnabled, chk = it.IsChecked;
                if (i == _hot) Draw.Fill(g, r, en ? Theme.PopupHover : Theme.C(0x333333));
                var tc = en ? Theme.Text : Theme.TextDisabled;
                if (chk)
                {
                    var cr = new Rectangle(r.X, r.Y, CheckCol, r.Height);
                    if (it.Radio) { int s = D.S(6); g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.FillEllipse(Theme.Brush(tc), cr.X + (cr.Width - s) / 2f, cr.Y + (cr.Height - s) / 2f, s, s); g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None; }
                    else Draw.CheckMark(g, new Rectangle(cr.X + (cr.Width - D.S(12)) / 2, cr.Y + (cr.Height - D.S(12)) / 2, D.S(12), D.S(12)), tc, D);
                }
                var tr = new Rectangle(r.X + CheckCol, r.Y, r.Width - CheckCol - D.S(4), r.Height);
                var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | ((_keyboardMode || ShowAccelerators) ? 0 : TextFormatFlags.HidePrefix);
                TextRenderer.DrawText(g, it.Text ?? "", it.Bold ? fb : f, tr, tc, flags);
                if (!string.IsNullOrEmpty(it.Shortcut)) Draw.Text(g, it.Shortcut, f, new Rectangle(tr.X, tr.Y, tr.Width - D.S(14), tr.Height), tc, Draw.TextRight);
                if (it.Sub != null) Draw.RightArrow(g, r.Right - D.S(11), r.Y + r.Height / 2, tc, D);
            }
        }

        int HitTest(Point p)
        {
            for (int i = 0; i < _rects.Count; i++) if (_rects[i].Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = HitTest(e.Location);
            if (h >= 0 && _items[h].Separator) h = -1;
            if (h != _hot && h >= 0)
            {
                _hot = h; _keyboardMode = false; Invalidate();
                _subTimer.Stop();
                if (_items[h].Sub != null && _items[h].IsEnabled) _subTimer.Start();
                else if (ChildPopup != null) { Popups.CloseChildren(this); _subFor = -1; }
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (ChildPopup == null) { _hot = -1; Invalidate(); }
            _subTimer.Stop();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int h = HitTest(e.Location);
            if (h >= 0) Activate(h);
        }

        public void Activate(int i)
        {
            var it = _items[i];
            if (!it.Selectable) return;
            if (it.Sub != null) { OpenSub(i, true); return; }
            Popups.CloseAll();
            try { it.Click?.Invoke(); }
            catch (Exception ex) { Program.ReportError(ex, false); }
        }

        /// <summary>Test hook: activate an item by its visible text.</summary>
        public bool ActivateByText(string text)
        {
            for (int i = 0; i < _items.Count; i++)
                if (!_items[i].Separator && string.Equals(_items[i].Plain, text, StringComparison.OrdinalIgnoreCase)) { Activate(i); return true; }
            return false;
        }

        void OpenSub(int i, bool selectFirst)
        {
            if (_subFor == i && ChildPopup != null) return;
            _subFor = i;
            var sub = new MenuPopup(D.Value, _items[i].Sub) { ShowAccelerators = ShowAccelerators || _keyboardMode };
            var r = _rects[i];
            sub.ShowBelow(RectangleToScreen(r), this);
            sub.Dismissed += () => { if (_subFor == i) _subFor = -1; };
            if (selectFirst) sub.SelectFirst();
        }

        public void SelectFirst() { _keyboardMode = true; MoveHot(1, -1); }

        void MoveHot(int dir, int from)
        {
            int n = _items.Count;
            int i = from;
            for (int k = 0; k < n; k++)
            {
                i = (i + dir + n) % n;
                if (!_items[i].Separator) { _hot = i; Invalidate(); return; }
            }
        }

        public override bool HandleKey(Keys k)
        {
            _keyboardMode = true;
            switch (k & Keys.KeyCode)
            {
                case Keys.Down: MoveHot(1, _hot < 0 ? -1 : _hot); return true;
                case Keys.Up: MoveHot(-1, _hot < 0 ? 0 : _hot); return true;
                case Keys.Right:
                    if (_hot >= 0 && _items[_hot].Sub != null && _items[_hot].IsEnabled) { OpenSub(_hot, true); return true; }
                    if (ParentPopup == null && NavigateBar != null) { NavigateBar(1); return true; }
                    { var root = RootMenu(); root?.NavigateBar?.Invoke(1); }
                    return true;
                case Keys.Left:
                    if (ParentPopup != null) { Popups.Close(this); return true; }
                    NavigateBar?.Invoke(-1);
                    return true;
                case Keys.Enter: case Keys.Space: if (_hot >= 0) Activate(_hot); return true;
                case Keys.Escape: Popups.Close(this); return true;
                case Keys.Home: MoveHot(1, -1); return true;
                case Keys.End: MoveHot(-1, 0); return true;
            }
            return false;
        }

        MenuPopup RootMenu()
        {
            PopupWindow p = this;
            while (p.ParentPopup != null) p = p.ParentPopup;
            return p as MenuPopup;
        }

        public override bool HandleChar(char c)
        {
            c = char.ToUpperInvariant(c);
            var matches = new List<int>();
            for (int i = 0; i < _items.Count; i++) if (_items[i].Selectable && _items[i].Mnemonic == c) matches.Add(i);
            if (matches.Count == 1) { Activate(matches[0]); return true; }
            if (matches.Count > 1)
            {
                int next = matches.Find(x => x > _hot);
                _hot = next > 0 ? next : matches[0];
                _keyboardMode = true; Invalidate();
            }
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _subTimer.Dispose();
            base.Dispose(disposing);
        }
    }

    // ===================================================================== tooltips

    public sealed class TipWindow : PopupWindow
    {
        string _title, _text;
        public TipWindow(int dpi) : base(dpi) { IsTooltip = true; }

        public void Set(string title, string text, Point screenPt)
        {
            _title = title; _text = text;
            var f = Theme.UiFont(D); var fb = Theme.UiFontBold(D);
            int maxW = D.S(320);
            bool both = !string.IsNullOrEmpty(text);
            var ts = TextRenderer.MeasureText(title ?? "", both ? fb : f, new Size(maxW, 1000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            Size ds = Size.Empty;
            if (both) ds = TextRenderer.MeasureText(text, f, new Size(maxW, 1000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            int w = Math.Max(ts.Width, ds.Width) + D.S(14);
            int h = ts.Height + (both ? ds.Height + D.S(4) : 0) + D.S(8);
            var wa = Screen.FromPoint(screenPt).WorkingArea;
            int x = Math.Min(screenPt.X, wa.Right - w), y = screenPt.Y;
            if (y + h > wa.Bottom) y = screenPt.Y - h - D.S(24);
            Bounds = new Rectangle(Math.Max(wa.Left, x), Math.Max(wa.Top, y), w, h);
            Invalidate();
        }

        protected override void Render(Graphics g)
        {
            Draw.Fill(g, ClientRectangle, Theme.Tooltip);
            Draw.Rect(g, ClientRectangle, Theme.TooltipBorder);
            var f = Theme.UiFont(D); var fb = Theme.UiFontBold(D);
            bool both = !string.IsNullOrEmpty(_text);
            var ts = TextRenderer.MeasureText(_title ?? "", both ? fb : f, new Size(Width - D.S(14), 1000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, _title ?? "", both ? fb : f, new Rectangle(D.S(7), D.S(4), Width - D.S(14), ts.Height), Theme.Text, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            if (both)
                TextRenderer.DrawText(g, _text, f, new Rectangle(D.S(7), D.S(4) + ts.Height + D.S(4), Width - D.S(14), Height), Theme.TextDim, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }

    public static class Tips
    {
        static TipWindow _tip;
        static readonly Timer _timer = new Timer { Interval = 500 };
        static readonly Timer _hideTimer = new Timer { Interval = 8000 };
        static object _pendingOwner;
        static string _t, _d;
        static Point _pt;
        static Dpi _dpi;
        public static bool Visible => _tip != null && !_tip.IsDisposed && _tip.Visible;
        public static TipWindow Window => _tip;

        static Tips()
        {
            _timer.Tick += (s, e) =>
            {
                _timer.Stop();
                if (_pendingOwner == null || Popups.AnyOpen) return;
                if (_tip == null || _tip.IsDisposed || _tip.DpiValue != _dpi.Value) { _tip?.Dispose(); _tip = new TipWindow(_dpi.Value); }
                _tip.Set(_t, _d, _pt);
                if (!_tip.Visible) _tip.Show();
                _hideTimer.Stop(); _hideTimer.Start();
            };
            _hideTimer.Tick += (s, e) => { _hideTimer.Stop(); if (_tip != null && !_tip.IsDisposed) _tip.Hide(); };
        }

        /// <summary>Request a tip for 'owner' (any object identity). Re-requests for the same owner are ignored.</summary>
        public static void Request(object owner, string title, string text, Point screenPt, int dpi)
        {
            if (Equals(owner, _pendingOwner)) return;
            Hide();
            if (string.IsNullOrEmpty(title)) return;
            _pendingOwner = owner; _t = title; _d = text; _pt = screenPt; _dpi = new Dpi(dpi);
            _timer.Start();
        }

        public static void Hide()
        {
            _timer.Stop(); _hideTimer.Stop();
            _pendingOwner = null;
            if (_tip != null && !_tip.IsDisposed && _tip.Visible) _tip.Hide();
        }
    }
}
