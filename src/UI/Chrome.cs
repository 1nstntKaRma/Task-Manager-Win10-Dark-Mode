using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TaskManagerDark
{
    /// <summary>Dark menu bar (File / Options / View) with Win32 behavior: hover-switching, Alt/F10 keyboard mode, mnemonics.</summary>
    public sealed class MenuBar : DarkControl
    {
        public sealed class Entry { public string Text; public Func<List<MenuItem>> Build; public Rectangle Bounds; }
        public readonly List<Entry> Entries = new List<Entry>();
        int _hot = -1, _open = -1;
        bool _keyboard;          // Alt / F10 mode: mnemonics underlined, a title highlighted
        MenuPopup _popup;

        public MenuBar()
        {
            TabStop = false;
            Popups.AllClosed += () => { if (_open >= 0) { _open = -1; if (!_keyboard) _hot = -1; Invalidate(); } };
            Popups.ClickInterceptor = (hwnd, screenPt) =>
            {
                if (_open < 0 || !IsHandleCreated || !Visible) return false;
                var p = PointToClient(screenPt);
                if (!ClientRectangle.Contains(p)) return false;
                int i = HitTest(p);
                if (i == _open || i < 0) { Popups.CloseAll(); ExitKeyboard(); return true; }
                OpenMenu(i, false);
                return true;
            };
        }

        public int PreferredHeight => D.S(20);
        public bool KeyboardMode => _keyboard;
        public bool MenuOpen => _open >= 0;

        void LayoutEntries()
        {
            var f = Theme.UiFont(D);
            int x = 0;
            foreach (var e in Entries)
            {
                int w = Draw.Measure(MenuItem.Clean(e.Text), f).Width + D.S(14);
                e.Bounds = new Rectangle(x, 0, w, Height);
                x += w;
            }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutEntries(); }
        protected override void OnDpiApplied() { LayoutEntries(); }

        protected override void Render(Graphics g, Rectangle clip)
        {
            LayoutEntries();
            Draw.Fill(g, ClientRectangle, Theme.Window);
            var f = Theme.UiFont(D);
            for (int i = 0; i < Entries.Count; i++)
            {
                var e = Entries[i];
                if (i == _open) Draw.Fill(g, e.Bounds, Theme.MenuBarOpen);
                else if (i == _hot) Draw.Fill(g, e.Bounds, Theme.MenuBarHover);
                TextRenderer.DrawText(g, e.Text, f, e.Bounds, Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | (_keyboard ? 0 : TextFormatFlags.HidePrefix));
            }
        }

        int HitTest(Point p) { for (int i = 0; i < Entries.Count; i++) if (Entries[i].Bounds.Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = HitTest(e.Location);
            if (_open >= 0) { if (h >= 0 && h != _open) OpenMenu(h, false); return; }
            if (h != _hot && !_keyboard) { _hot = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_open < 0 && !_keyboard) { _hot = -1; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int h = HitTest(e.Location);
            if (h < 0) return;
            if (_open == h) { Popups.CloseAll(); return; }
            OpenMenu(h, false);
        }

        public void OpenMenu(int i, bool selectFirst)
        {
            if (i < 0 || i >= Entries.Count) return;
            Tips.Hide();
            var items = Entries[i].Build();
            _popup = new MenuPopup(D.Value, items) { ShowAccelerators = _keyboard || selectFirst };
            _popup.NavigateBar = dir => OpenMenu((i + dir + Entries.Count) % Entries.Count, true);
            _open = i; _hot = i;
            Invalidate();
            var b = Entries[i].Bounds;
            _popup.ShowBelow(RectangleToScreen(b));
            _open = i; _hot = i;    // ShowBelow closed the previous popup (AllClosed reset the state)
            if (selectFirst) _popup.SelectFirst();
            Invalidate();
        }

        public MenuPopup OpenPopup => _open >= 0 ? _popup : null;

        // ---------------- keyboard mode (driven by the main form)
        public void EnterKeyboard() { _keyboard = true; _hot = Math.Max(0, _hot); Invalidate(); }
        public void ExitKeyboard() { _keyboard = false; if (_open < 0) _hot = -1; Invalidate(); }

        public bool HandleKeyboardKey(Keys k)
        {
            if (!_keyboard || _open >= 0) return false;
            switch (k & Keys.KeyCode)
            {
                case Keys.Left: _hot = (_hot - 1 + Entries.Count) % Entries.Count; Invalidate(); return true;
                case Keys.Right: _hot = (_hot + 1) % Entries.Count; Invalidate(); return true;
                case Keys.Enter: case Keys.Down: case Keys.Up: case Keys.Space: OpenMenu(_hot, true); return true;
                case Keys.Escape: ExitKeyboard(); return true;
            }
            return false;
        }

        public bool HandleMnemonic(char c)
        {
            c = char.ToUpperInvariant(c);
            for (int i = 0; i < Entries.Count; i++)
            {
                var m = new MenuItem { Text = Entries[i].Text }.Mnemonic;
                if (m == c) { _keyboard = true; OpenMenu(i, true); return true; }
            }
            return false;
        }
    }

    /// <summary>The row of tabs (Processes, Performance, ...).</summary>
    public sealed class TabStrip : DarkControl
    {
        public readonly List<string> Tabs = new List<string>();
        readonly List<Rectangle> _rects = new List<Rectangle>();
        int _selected, _defaultTab, _hot = -1;
        bool _hotDefault, _downDefault;
        public event Action<int> SelectedChanged;
        public event Action<int> DefaultTabChanged;
        public int PreferredHeight => D.S(25);

        public int Selected
        {
            get => _selected;
            set { if (value == _selected || value < 0 || value >= Tabs.Count) return; _selected = value; Invalidate(); SelectedChanged?.Invoke(value); }
        }

        public int DefaultTab
        {
            get => _defaultTab;
            set { if (value == _defaultTab || value < 0 || value >= Tabs.Count) return; _defaultTab = value; Invalidate(); }
        }

        Rectangle DefaultBtnRect => new Rectangle(Width - D.S(23), D.S(2), D.S(19), Height - D.S(4));
        bool ShowDefaultBtn => _rects.Count == 0 || DefaultBtnRect.Left > _rects[_rects.Count - 1].Right + D.S(4);

        void LayoutTabs()
        {
            _rects.Clear();
            var f = Theme.UiFont(D);
            int x = D.S(2);
            foreach (var t in Tabs)
            {
                int w = Draw.Measure(t, f).Width + D.S(14);
                _rects.Add(new Rectangle(x, D.S(3), w, Height - D.S(3)));
                x += w;
            }
        }

        public Rectangle TabRect(int i) { LayoutTabs(); return i >= 0 && i < _rects.Count ? _rects[i] : Rectangle.Empty; }

        protected override void Render(Graphics g, Rectangle clip)
        {
            LayoutTabs();
            Draw.Fill(g, ClientRectangle, Theme.Window);
            Draw.HLine(g, 0, Width, Height - 1, Theme.Border);
            var f = Theme.UiFont(D);
            for (int i = 0; i < Tabs.Count; i++)
            {
                var r = _rects[i];
                bool sel = i == _selected;
                if (sel)
                {
                    var sr = new Rectangle(r.X - D.S(2), r.Y - D.S(2), r.Width + D.S(4), r.Height + D.S(2));
                    Draw.Fill(g, sr, Theme.TabSelected);
                    Draw.HLine(g, sr.Left, sr.Right, sr.Top, Theme.Border);
                    Draw.VLine(g, sr.Left, sr.Top, sr.Bottom, Theme.Border);
                    Draw.VLine(g, sr.Right - 1, sr.Top, sr.Bottom, Theme.Border);
                    Draw.Text(g, Tabs[i], f, new Rectangle(sr.X, sr.Y, sr.Width, sr.Height - D.S(1)), Theme.Text, Draw.TextCenter);
                }
                else
                {
                    if (i == _hot) Draw.Fill(g, new Rectangle(r.X, r.Y + D.S(1), r.Width, r.Height - D.S(2)), Theme.TabHover);
                    Draw.Text(g, Tabs[i], f, new Rectangle(r.X, r.Y + D.S(1), r.Width, r.Height - D.S(1)), i == _hot ? Theme.Text : Theme.TabText, Draw.TextCenter);
                }
            }

            if (ShowDefaultBtn)
            {
                var br = DefaultBtnRect;
                bool isDef = _selected == _defaultTab;
                if (_hotDefault || isDef)
                {
                    Color bg = _downDefault ? Theme.Pressed : _hotDefault ? (isDef ? Theme.CheckedHover : Theme.TabHover) : Theme.Checked;
                    Color bd = isDef ? Theme.CheckedBorder : Theme.Border;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (var p = Draw.RoundRect(new RectangleF(br.X + 0.5f, br.Y + 0.5f, br.Width - 1, br.Height - 1), D.F(3)))
                    {
                        g.FillPath(Theme.Brush(bg), p);
                        g.DrawPath(Theme.Pen(bd), p);
                    }
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                }
                float isz = D.S(13);
                var ir = new RectangleF(br.X + (br.Width - isz) / 2f, br.Y + (br.Height - isz) / 2f, isz, isz);
                Color ic = isDef ? Theme.Link : (_hotDefault ? Theme.Text : Theme.TabText);
                Glyphs.DefaultTabStar(g, ir, isDef, ic);
            }
        }

        int Hit(Point p) { for (int i = 0; i < _rects.Count; i++) if (_rects[i].Contains(p)) return i; return -1; }
        bool HitDefaultBtn(Point p) => ShowDefaultBtn && DefaultBtnRect.Contains(p);

        void ShowDefaultTip()
        {
            if (_selected < 0 || _selected >= Tabs.Count) return;
            bool isDef = _selected == _defaultTab;
            string tabName = Tabs[_selected];
            string title = isDef ? "Default startup tab (" + tabName + ")" : "Set \"" + tabName + "\" as default tab";
            string desc = isDef ? "Task Manager opens to this tab on startup." : "Click to open Task Manager to \"" + tabName + "\" on startup.";
            var br = DefaultBtnRect;
            Tips.Request("deftab:" + _selected + ":" + _defaultTab, title, desc, PointToScreen(new Point(br.Left, br.Bottom + D.S(4))), D.Value);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hd = HitDefaultBtn(e.Location);
            int h = hd ? -1 : Hit(e.Location);
            Cursor = hd ? Cursors.Hand : Cursors.Default;
            if (hd != _hotDefault || h != _hot)
            {
                _hotDefault = hd;
                _hot = h;
                Invalidate();
            }
            if (hd) ShowDefaultTip();
            else Tips.Hide();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hot = -1;
            _hotDefault = false;
            _downDefault = false;
            Cursor = Cursors.Default;
            Tips.Hide();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (HitDefaultBtn(e.Location))
            {
                _downDefault = true;
                Invalidate();
                return;
            }
            int h = Hit(e.Location);
            if (h >= 0) Selected = h;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            bool wasDown = _downDefault;
            _downDefault = false;
            if (wasDown)
            {
                Invalidate();
                if (e.Button == MouseButtons.Left && HitDefaultBtn(e.Location))
                {
                    _defaultTab = _selected;
                    Invalidate();
                    DefaultTabChanged?.Invoke(_defaultTab);
                    Tips.Hide();
                    ShowDefaultTip();
                }
            }
        }
    }

    /// <summary>Bottom bar: "Fewer details" / "More details" on the left, the page's command button or link on the right.</summary>
    public sealed class CommandBar : DarkControl
    {
        public string ToggleText = Strings.FewerDetails;
        public bool ToggleUp = true;
        public string ButtonText;               // e.g. "&End task"
        public bool ButtonEnabled;
        public string LinkText;                 // e.g. "Open Resource Monitor"
        public Action<Graphics, RectangleF> LinkIcon;
        public event Action ToggleClicked, ButtonClicked, LinkClicked;
        int _hot, _pressed;                     // 1 toggle, 2 button, 3 link
        public bool ShowAccelerators;
        public int PreferredHeight => D.S(44);

        Rectangle ToggleRect
        {
            get
            {
                var f = Theme.UiFont(D);
                int w = D.S(22) + D.S(6) + Draw.Measure(ToggleText, f).Width + D.S(6);
                return new Rectangle(D.S(8), (Height - D.S(26)) / 2, w, D.S(26));
            }
        }

        Rectangle ButtonRect
        {
            get
            {
                if (ButtonText == null) return Rectangle.Empty;
                var f = Theme.UiFont(D);
                int w = Math.Max(D.S(85), Draw.Measure(MenuItem.Clean(ButtonText), f).Width + D.S(24));
                return new Rectangle(Width - D.S(12) - w, (Height - D.S(23)) / 2, w, D.S(23));
            }
        }

        Rectangle LinkRect
        {
            get
            {
                if (LinkText == null) return Rectangle.Empty;
                var f = Theme.UiFont(D);
                int w = D.S(20) + Draw.Measure(LinkText, f).Width + D.S(4);
                int right = ButtonText != null ? ButtonRect.Left - D.S(16) : Width - D.S(12);
                return new Rectangle(right - w, (Height - D.S(22)) / 2, w, D.S(22));
            }
        }

        protected override void Render(Graphics g, Rectangle clip)
        {
            Draw.Fill(g, ClientRectangle, Theme.Window);
            var f = Theme.UiFont(D);
            var tr = ToggleRect;
            if (_hot == 1) Draw.Fill(g, tr, _pressed == 1 ? Theme.Pressed : Theme.Hover);
            Glyphs.DetailsToggle(g, new RectangleF(tr.X + D.S(3), tr.Y + (tr.Height - D.S(18)) / 2f, D.S(18), D.S(18)), ToggleUp, Theme.TextDim);
            TextRenderer.DrawText(g, ToggleText, f, new Rectangle(tr.X + D.S(26), tr.Y, tr.Width - D.S(26), tr.Height), Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (LinkText != null)
            {
                var lr = LinkRect;
                LinkIcon?.Invoke(g, new RectangleF(lr.X, lr.Y + (lr.Height - D.S(16)) / 2f, D.S(16), D.S(16)));
                var lf = _hot == 3 ? Theme.GetFont(D, 12f, FontStyle.Underline) : f;
                TextRenderer.DrawText(g, LinkText, lf, new Rectangle(lr.X + D.S(20), lr.Y, lr.Width - D.S(20), lr.Height), _hot == 3 ? Theme.LinkHover : Theme.Link, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
            if (ButtonText != null)
            {
                var br = ButtonRect;
                Color bg = !ButtonEnabled ? Theme.C(0x2A2A2A) : _pressed == 2 ? Theme.Pressed : _hot == 2 ? Theme.Hover : Theme.Button;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var p = Draw.RoundRect(new RectangleF(br.X + 0.5f, br.Y + 0.5f, br.Width - 1, br.Height - 1), D.F(3)))
                {
                    g.FillPath(Theme.Brush(bg), p);
                    g.DrawPath(Theme.Pen(ButtonEnabled && _hot == 2 ? Theme.InputBorderHover : Theme.ButtonBorder), p);
                }
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                TextRenderer.DrawText(g, ButtonText, f, br, ButtonEnabled ? Theme.Text : Theme.TextDisabled,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | (ShowAccelerators ? 0 : TextFormatFlags.HidePrefix));
            }
        }

        int Hit(Point p)
        {
            if (ToggleRect.Contains(p)) return 1;
            if (ButtonText != null && ButtonRect.Contains(p)) return 2;
            if (LinkText != null && LinkRect.Contains(p)) return 3;
            return 0;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = Hit(e.Location);
            Cursor = h == 3 ? Cursors.Hand : Cursors.Default;
            if (h != _hot) { _hot = h; Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = 0; _pressed = 0; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { _pressed = Hit(e.Location); Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int h = Hit(e.Location), p = _pressed;
            _pressed = 0; Invalidate();
            if (e.Button != MouseButtons.Left || h != p) return;
            if (h == 1) ToggleClicked?.Invoke();
            else if (h == 2 && ButtonEnabled) ButtonClicked?.Invoke();
            else if (h == 3) LinkClicked?.Invoke();
        }

        public void PressButton() { if (ButtonText != null && ButtonEnabled) ButtonClicked?.Invoke(); }
        public bool ButtonMnemonic(char c)
        {
            if (ButtonText == null || !ButtonEnabled) return false;
            var m = new MenuItem { Text = ButtonText }.Mnemonic;
            if (m != char.ToUpperInvariant(c)) return false;
            ButtonClicked?.Invoke(); return true;
        }
    }
}
