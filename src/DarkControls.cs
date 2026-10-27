using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TaskManagerDark
{
    /// <summary>Base for every dialog: dark title bar, dark surface, DPI from the owner, Enter/Esc handling.</summary>
    public class DarkDialog : Form
    {
        protected Dpi D;
        public static event Action<Form> DialogShown;

        public DarkDialog(Form owner, string title)
        {
            D = new Dpi(owner != null && owner.IsHandleCreated ? Native.DpiOf(owner.Handle) : Native.GetDpiForSystem());
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = owner != null && owner.Visible && owner.WindowState != FormWindowState.Minimized ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Dialog;
            ForeColor = Theme.Text;
            Font = Theme.UiFont(D);
            KeyPreview = true;
            ShowIcon = false;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }

        public int DpiValue => D.Value;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkMode.ApplyToWindow(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            DialogShown?.Invoke(this);
        }

        protected void SetClientSizeScaled(int w, int h) => ClientSize = new Size(D.S(w), D.S(h));
        protected Rectangle R(int x, int y, int w, int h) => new Rectangle(D.S(x), D.S(y), D.S(w), D.S(h));

        protected DarkButton AddButton(string text, int x, int y, int w, DialogResult result, bool isDefault = false, Control parent = null)
        {
            var b = new DarkButton { Text = text, Bounds = R(x, y, w, 23), DialogResult = result, Font = Font };
            (parent ?? this).Controls.Add(b);
            if (isDefault) AcceptButton = b;
            if (result == DialogResult.Cancel) CancelButton = b;
            return b;
        }

        protected DarkLabel AddLabel(string text, int x, int y, int w, int h = 20, bool dim = false)
        {
            var l = new DarkLabel { Text = text, Bounds = R(x, y, w, h), Dim = dim, Font = Font };
            Controls.Add(l);
            return l;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Dialog);
            base.OnPaint(e);
        }
    }

    public class DarkButton : Control, IButtonControl
    {
        bool _hover, _down, _default;
        public DialogResult DialogResult { get; set; }

        public DarkButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            TabStop = true;
            Cursor = Cursors.Default;
        }

        public void NotifyDefault(bool value) { _default = value; Invalidate(); }

        public void PerformClick() { if (Enabled) OnClick(EventArgs.Empty); }

        protected override void OnClick(EventArgs e)
        {
            var f = FindForm();
            if (f != null && DialogResult != DialogResult.None) f.DialogResult = DialogResult;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ClientRectangle;
            g.Clear(Parent?.BackColor ?? Theme.Dialog);
            Color bg = !Enabled ? Theme.C(0x2A2A2A) : _down ? Theme.Pressed : _hover ? Theme.Hover : Theme.Button;
            Color border = (_default || Focused) && Enabled ? Theme.Focus : Theme.ButtonBorder;
            if (_default && Enabled && !_hover && !_down) bg = Theme.C(0x2E3E52);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Draw.RoundRect(new RectangleF(0.5f, 0.5f, r.Width - 1, r.Height - 1), 3f))
            {
                using (var b = new SolidBrush(bg)) g.FillPath(b, p);
                using (var pen = new Pen(border)) g.DrawPath(pen, p);
            }
            TextRenderer.DrawText(g, Text, Font, r, Enabled ? Theme.Text : Theme.TextDisabled, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | (ShowKeyboardCues ? 0 : TextFormatFlags.HidePrefix));
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { _down = true; Focus(); Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _down = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) == Keys.Space || base.IsInputKey(keyData);
        protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if (e.KeyCode == Keys.Space) PerformClick(); }

        protected override bool ProcessMnemonic(char charCode)
        {
            if (CanSelect && IsMnemonic(charCode, Text)) { PerformClick(); return true; }
            return base.ProcessMnemonic(charCode);
        }
    }

    public class DarkLabel : Control
    {
        public bool Dim;
        public Color? Color;
        public TextFormatFlags Flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak;
        public Control MnemonicTarget;
        public DarkLabel() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true); TabStop = false; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Theme.Dialog);
            var c = !Enabled ? Theme.TextDisabled : Color ?? (Dim ? Theme.TextDim : Theme.Text);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, c, Flags | (ShowKeyboardCues ? 0 : TextFormatFlags.HidePrefix));
        }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override bool ProcessMnemonic(char charCode)
        {
            if (MnemonicTarget != null && IsMnemonic(charCode, Text)) { MnemonicTarget.Focus(); if (MnemonicTarget is DarkTextBox tb) tb.SelectAll(); return true; }
            return false;
        }
    }

    /// <summary>Clickable text link (e.g. "More about wait chains").</summary>
    public class DarkLink : Control
    {
        bool _hover;
        public DarkLink() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable | ControlStyles.StandardClick, true); TabStop = true; Cursor = Cursors.Hand; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Theme.Dialog);
            var f = _hover ? new Font(Font, FontStyle.Underline) : Font;
            TextRenderer.DrawText(e.Graphics, Text, f, ClientRectangle, _hover ? Theme.LinkHover : Theme.Link, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (_hover) f.Dispose();
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
        protected override bool IsInputKey(Keys k) => (k & Keys.KeyCode) == Keys.Space || (k & Keys.KeyCode) == Keys.Enter || base.IsInputKey(k);
        protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty); }
    }

    /// <summary>Check box and radio button in one class.</summary>
    public class DarkCheck : Control
    {
        bool _checked, _hover;
        public bool Radio;
        public event EventHandler CheckedChanged;

        public DarkCheck()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            TabStop = true;
        }

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value;
                if (value && Radio && Parent != null)
                    foreach (Control c in Parent.Controls) if (c != this && c is DarkCheck dc && dc.Radio) dc.Checked = false;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Dialog);
            int s = Math.Min(Height - 4, (int)(Font.Height * 0.95f));
            var box = new Rectangle(1, (Height - s) / 2, s, s);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var border = !Enabled ? Theme.TextDisabled : (_hover || Focused) ? Theme.Focus : Theme.InputBorder;
            if (Radio)
            {
                using (var b = new SolidBrush(Theme.Input)) g.FillEllipse(b, box);
                using (var p = new Pen(border)) g.DrawEllipse(p, box);
                if (_checked) using (var b = new SolidBrush(Enabled ? Theme.Focus : Theme.TextDisabled)) g.FillEllipse(b, Rectangle.Inflate(box, -s / 4, -s / 4));
            }
            else
            {
                using (var b = new SolidBrush(_checked && Enabled ? Theme.Accent : Theme.Input)) g.FillRectangle(b, box);
                using (var p = new Pen(_checked && Enabled ? Theme.Accent : border)) g.DrawRectangle(p, box.X, box.Y, box.Width - 1, box.Height - 1);
                if (_checked) Draw.CheckMark(g, Rectangle.Inflate(box, -1, -1), Color.White, new Dpi(96));
            }
            var tr = new Rectangle(box.Right + 6, 0, Width - box.Right - 6, Height);
            TextRenderer.DrawText(g, Text, Font, tr, Enabled ? Theme.Text : Theme.TextDisabled, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | (ShowKeyboardCues ? 0 : TextFormatFlags.HidePrefix));
            if (Focused && ShowFocusCues)
            {
                var ts = TextRenderer.MeasureText(Text.Replace("&", ""), Font);
                using (var p = new Pen(Theme.TextDim) { DashStyle = DashStyle.Dot }) g.DrawRectangle(p, tr.X - 2, (Height - ts.Height) / 2, Math.Min(tr.Width - 1, ts.Width + 2), ts.Height - 1);
            }
        }

        protected override void OnClick(EventArgs e) { base.OnClick(e); if (Radio) Checked = true; else Checked = !Checked; Focus(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override bool IsInputKey(Keys k) => (k & Keys.KeyCode) == Keys.Space || base.IsInputKey(k);
        protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if (e.KeyCode == Keys.Space) { if (Radio) Checked = true; else Checked = !Checked; } }
        protected override bool ProcessMnemonic(char charCode)
        {
            if (CanSelect && IsMnemonic(charCode, Text)) { Focus(); if (Radio) Checked = true; else Checked = !Checked; return true; }
            return false;
        }
    }

    /// <summary>Single-line (or multi-line) dark edit box wrapping a borderless TextBox.</summary>
    public class DarkTextBox : Control
    {
        public readonly TextBox Box;
        public bool NumericOnly;
        bool _hover;
        public int RightInset;

        public DarkTextBox(bool multiline = false)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Box = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Text, Multiline = multiline };
            if (multiline) Box.ScrollBars = ScrollBars.Vertical;
            Controls.Add(Box);
            Box.GotFocus += (s, e) => Invalidate();
            Box.LostFocus += (s, e) => Invalidate();
            Box.MouseEnter += (s, e) => { _hover = true; Invalidate(); };
            Box.MouseLeave += (s, e) => { _hover = false; Invalidate(); };
            Box.TextChanged += (s, e) => OnTextChanged(e);
            Box.KeyPress += (s, e) => { if (NumericOnly && !char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
            Box.HandleCreated += (s, e) => { if (multiline) DarkMode.ApplyToControl(Box.Handle); };
            TabStop = false;
        }

        public override string Text { get => Box.Text; set => Box.Text = value; }
        public bool ReadOnly { get => Box.ReadOnly; set { Box.ReadOnly = value; Box.BackColor = Theme.Input; } }
        public void SelectAll() { Box.Focus(); Box.SelectAll(); }

        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Box.Font = Font; LayoutBox(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutBox(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Box.Focus(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Box.Enabled = Enabled; Box.BackColor = Enabled ? Theme.Input : Theme.C(0x242424); Invalidate(); }

        void LayoutBox()
        {
            if (Box.Multiline) { Box.Bounds = new Rectangle(4, 3, Math.Max(4, Width - 8 - RightInset), Math.Max(4, Height - 6)); return; }
            int h = Box.PreferredHeight;
            Box.Bounds = new Rectangle(5, Math.Max(2, (Height - h) / 2), Math.Max(4, Width - 10 - RightInset), h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Enabled ? Theme.Input : Theme.C(0x242424));
            var border = Box.Focused ? Theme.Focus : _hover ? Theme.InputBorderHover : Theme.InputBorder;
            using (var p = new Pen(border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }

        public int IntValue(int def) => int.TryParse(Box.Text, out int v) ? v : def;
    }

    /// <summary>Editable combo box: a text box plus a drop-down history list (Run new task "Open:").</summary>
    public class DarkEditCombo : DarkTextBox
    {
        public readonly List<string> Items = new List<string>();
        bool _btnHover;
        Form _drop;

        public DarkEditCombo()
        {
            Box.KeyDown += (s, e) => { if ((e.KeyCode == Keys.Down && e.Alt) || e.KeyCode == Keys.F4) { e.Handled = true; ToggleDrop(); } };
            Box.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            Box.AutoCompleteSource = AutoCompleteSource.FileSystem;
        }

        int ButtonW => Height;
        protected override void OnResize(EventArgs e) { RightInset = ButtonW; base.OnResize(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            var br = new Rectangle(Width - ButtonW, 1, ButtonW - 1, Height - 2);
            if (_btnHover) using (var b = new SolidBrush(Theme.Hover)) g.FillRectangle(b, br);
            Draw.Chevron(g, br.X + br.Width / 2f, br.Y + br.Height / 2f, Height * 0.32f, Theme.TextDim, 0);
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); bool h = e.X >= Width - ButtonW; if (h != _btnHover) { _btnHover = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _btnHover = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.X >= Width - ButtonW) ToggleDrop(); }

        void ToggleDrop()
        {
            if (_drop != null && !_drop.IsDisposed) { _drop.Close(); _drop = null; return; }
            if (Items.Count == 0) return;
            _drop = new DarkList.DropForm(this, Items, s => { Text = s; Box.SelectionStart = Box.TextLength; Box.Focus(); });
            _drop.Show(FindForm());
        }
    }

    /// <summary>Static helpers for small popup lists used inside modal dialogs.</summary>
    public static class DarkList
    {
        public sealed class DropForm : Form
        {
            readonly Control _anchor;
            readonly List<string> _items;
            readonly Action<string> _pick;
            int _hot, _top;
            readonly int _rowH, _visible;

            public DropForm(Control anchor, List<string> items, Action<string> pick)
            {
                _anchor = anchor; _items = items; _pick = pick;
                FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                Font = anchor.Font;
                _rowH = Font.Height + 6;
                _visible = Math.Min(10, Math.Max(1, items.Count));
                var sp = anchor.PointToScreen(new Point(0, anchor.Height));
                var wa = Screen.FromControl(anchor).WorkingArea;
                int h = _rowH * _visible + 2;
                if (sp.Y + h > wa.Bottom) sp.Y = anchor.PointToScreen(Point.Empty).Y - h;
                Bounds = new Rectangle(sp.X, sp.Y, anchor.Width, h);
                KeyPreview = true;
                _hot = -1;
            }
            protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; cp.ExStyle |= 0x80; return cp; } }
            protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Close(); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.Popup);
                for (int i = 0; i < _visible && _top + i < _items.Count; i++)
                {
                    var r = new Rectangle(1, 1 + i * _rowH, Width - 2, _rowH);
                    if (_top + i == _hot) using (var b = new SolidBrush(Theme.PopupHover)) g.FillRectangle(b, r);
                    TextRenderer.DrawText(g, _items[_top + i], Font, new Rectangle(r.X + 6, r.Y, r.Width - 8, r.Height), Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
                using (var p = new Pen(Theme.PopupBorder)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            }
            int Hit(Point p) { int i = (p.Y - 1) / _rowH; return i >= 0 && i < _visible && _top + i < _items.Count ? _top + i : -1; }
            protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = Hit(e.Location); if (h >= 0 && h != _hot) { _hot = h; Invalidate(); } }
            protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); int h = Hit(e.Location); if (h >= 0) { _pick(_items[h]); Close(); } }
            protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); _top = Math.Max(0, Math.Min(_items.Count - _visible, _top - Math.Sign(e.Delta) * 3)); Invalidate(); }
            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (e.KeyCode == Keys.Escape) Close();
                else if (e.KeyCode == Keys.Enter) { if (_hot >= 0) _pick(_items[_hot]); Close(); }
                else if (e.KeyCode == Keys.Down) { _hot = Math.Min(_items.Count - 1, _hot + 1); if (_hot >= _top + _visible) _top++; Invalidate(); }
                else if (e.KeyCode == Keys.Up) { _hot = Math.Max(0, _hot - 1); if (_hot < _top) _top--; Invalidate(); }
            }
        }
    }

    /// <summary>Dropdown list (affinity dialog processor group, GPU engine selectors in dialogs).</summary>
    public class DarkCombo : Control
    {
        public List<string> Items = new List<string>();
        int _index = -1;
        bool _hover;
        public event EventHandler SelectedIndexChanged;

        public DarkCombo()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            TabStop = true;
        }

        public int SelectedIndex
        {
            get => _index;
            set { if (value == _index) return; _index = value; Invalidate(); SelectedIndexChanged?.Invoke(this, EventArgs.Empty); }
        }
        public string SelectedItem => _index >= 0 && _index < Items.Count ? Items[_index] : null;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Input);
            using (var p = new Pen(Focused ? Theme.Focus : _hover ? Theme.InputBorderHover : Theme.InputBorder)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            Draw.Chevron(g, Width - Height / 2f, Height / 2f, Height * 0.32f, Enabled ? Theme.TextDim : Theme.TextDisabled, 0);
            TextRenderer.DrawText(g, SelectedItem ?? "", Font, new Rectangle(6, 0, Width - Height - 6, Height), Enabled ? Theme.Text : Theme.TextDisabled, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); Open(); }
        protected override bool IsInputKey(Keys k) { var c = k & Keys.KeyCode; return c == Keys.Up || c == Keys.Down || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Down && e.Alt) { Open(); return; }
            if (e.KeyCode == Keys.Down && _index < Items.Count - 1) SelectedIndex = _index + 1;
            if (e.KeyCode == Keys.Up && _index > 0) SelectedIndex = _index - 1;
            if (e.KeyCode == Keys.F4) Open();
        }

        void Open()
        {
            if (Items.Count == 0) return;
            var f = new DarkList.DropForm(this, Items, s => SelectedIndex = Items.IndexOf(s));
            f.Show(FindForm());
        }
    }

    /// <summary>Scrollable list of check boxes (Select columns, Processor affinity).</summary>
    public class CheckList : Control
    {
        public sealed class Item { public string Text; public bool Checked; public bool Enabled = true; public object Tag; }
        public readonly List<Item> Items = new List<Item>();
        int _top, _focus;
        bool _hoverThumb;
        public event Action<Item> ItemChecked;

        public CheckList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
            TabStop = true;
        }

        int RowH => Font.Height + 6;
        int Visible => Math.Max(1, (Height - 2) / RowH);
        int MaxTop => Math.Max(0, Items.Count - Visible);
        int ScrollW => Items.Count > Visible ? SystemInformation.VerticalScrollBarWidth * Font.Height / 15 : 0;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Input);
            int rh = RowH, s = Font.Height - 3;
            for (int i = 0; i < Visible + 1 && _top + i < Items.Count; i++)
            {
                var it = Items[_top + i];
                var r = new Rectangle(1, 1 + i * rh, Width - 2 - ScrollW, rh);
                if (_top + i == _focus && Focused) using (var p = new Pen(Theme.Focus) { DashStyle = DashStyle.Dot }) g.DrawRectangle(p, r.X + 1, r.Y, r.Width - 3, r.Height - 1);
                var box = new Rectangle(r.X + 5, r.Y + (rh - s) / 2, s, s);
                bool en = it.Enabled && Enabled;
                using (var b = new SolidBrush(it.Checked && en ? Theme.Accent : Theme.Input)) g.FillRectangle(b, box);
                using (var p = new Pen(it.Checked && en ? Theme.Accent : en ? Theme.InputBorder : Theme.TextDisabled)) g.DrawRectangle(p, box.X, box.Y, box.Width - 1, box.Height - 1);
                if (it.Checked) Draw.CheckMark(g, Rectangle.Inflate(box, -1, -1), en ? Color.White : Theme.TextDisabled, new Dpi(96));
                TextRenderer.DrawText(g, it.Text, Font, new Rectangle(box.Right + 6, r.Y, r.Width - box.Right - 8, r.Height), en ? Theme.Text : Theme.TextDisabled, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
            if (ScrollW > 0)
            {
                var track = new Rectangle(Width - 1 - ScrollW, 1, ScrollW, Height - 2);
                using (var b = new SolidBrush(Theme.ScrollTrack)) g.FillRectangle(b, track);
                int th = Math.Max(16, track.Height * Visible / Items.Count);
                int ty = track.Y + (track.Height - th) * _top / Math.Max(1, MaxTop);
                using (var b = new SolidBrush(_hoverThumb ? Theme.ScrollThumbHover : Theme.ScrollThumb)) g.FillRectangle(b, track.X + 3, ty + 2, track.Width - 6, th - 4);
            }
            using (var p = new Pen(Focused ? Theme.Focus : Theme.InputBorder)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }

        int Hit(Point p) { int i = _top + (p.Y - 1) / RowH; return p.X < Width - 1 - ScrollW && i >= 0 && i < Items.Count ? i : -1; }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (ScrollW > 0 && e.X >= Width - 1 - ScrollW)
            {
                _top = Math.Max(0, Math.Min(MaxTop, (e.Y - 1) * Items.Count / Math.Max(1, Height - 2) - Visible / 2));
                Invalidate(); return;
            }
            int h = Hit(e.Location);
            if (h >= 0) { _focus = h; Toggle(h); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool t = ScrollW > 0 && e.X >= Width - 1 - ScrollW;
            if (t != _hoverThumb) { _hoverThumb = t; Invalidate(); }
            if (t && e.Button == MouseButtons.Left) { _top = Math.Max(0, Math.Min(MaxTop, (e.Y - 1) * Items.Count / Math.Max(1, Height - 2) - Visible / 2)); Invalidate(); }
        }

        public void Toggle(int i)
        {
            var it = Items[i];
            if (!it.Enabled || !Enabled) return;
            it.Checked = !it.Checked;
            Invalidate();
            ItemChecked?.Invoke(it);
        }

        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); _top = Math.Max(0, Math.Min(MaxTop, _top - Math.Sign(e.Delta) * 3)); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override bool IsInputKey(Keys k) { var c = k & Keys.KeyCode; return c == Keys.Up || c == Keys.Down || c == Keys.Space || c == Keys.PageUp || c == Keys.PageDown || c == Keys.Home || c == Keys.End || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int f = _focus;
            switch (e.KeyCode)
            {
                case Keys.Down: f++; break;
                case Keys.Up: f--; break;
                case Keys.PageDown: f += Visible; break;
                case Keys.PageUp: f -= Visible; break;
                case Keys.Home: f = 0; break;
                case Keys.End: f = Items.Count - 1; break;
                case Keys.Space: if (_focus >= 0 && _focus < Items.Count) Toggle(_focus); return;
                default: return;
            }
            _focus = Math.Max(0, Math.Min(Items.Count - 1, f));
            if (_focus < _top) _top = _focus;
            if (_focus >= _top + Visible) _top = _focus - Visible + 1;
            Invalidate();
        }
    }

    /// <summary>
    /// Dark replacement for the task dialogs Task Manager uses for confirmations: a main instruction,
    /// content text, an optional check box and a row of buttons. Returns the clicked index (Esc = last).
    /// </summary>
    public sealed class MessageDialog : DarkDialog
    {
        public enum Kind { None, Info, Question, Warning, Error }
        int _result = -1;
        readonly int _cancelIndex;
        DarkCheck _check;
        public bool CheckValue => _check != null && _check.Checked;

        MessageDialog(Form owner, string title, string instruction, string text, string[] buttons, int def, Kind kind, string checkText) : base(owner, title)
        {
            _cancelIndex = buttons.Length - 1;
            var f = Font;
            var fi = Theme.GetFont(D, 16f, FontStyle.Regular);
            int maxTextW = D.S(400);
            bool hasIcon = kind != Kind.None;
            int iconSz = hasIcon ? D.S(32) : 0;
            int textX = D.S(16) + (hasIcon ? iconSz + D.S(12) : 0);
            int y = D.S(16);
            var ins = string.IsNullOrEmpty(instruction) ? Size.Empty : TextRenderer.MeasureText(instruction, fi, new Size(maxTextW, 2000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            var ts = string.IsNullOrEmpty(text) ? Size.Empty : TextRenderer.MeasureText(text, f, new Size(maxTextW, 2000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            int btnW = D.S(75), gap = D.S(8);
            foreach (var b in buttons) btnW = Math.Max(btnW, TextRenderer.MeasureText(b.Replace("&", ""), f).Width + D.S(24));
            int buttonsW = buttons.Length * btnW + (buttons.Length - 1) * gap;
            int contentW = Math.Max(Math.Max(ins.Width, ts.Width), D.S(260));
            int w = Math.Max(textX + contentW + D.S(20), buttonsW + D.S(32));
            if (hasIcon) Controls.Add(new MessageIcon { Kind = kind, Bounds = new Rectangle(D.S(16), y, iconSz, iconSz) });
            if (!ins.IsEmpty)
            {
                Controls.Add(new DarkLabel { Text = instruction, Font = fi, Color = Theme.C(0x8CC4F5), Bounds = new Rectangle(textX, y, ins.Width + D.S(4), ins.Height + D.S(2)), Flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix });
                y += ins.Height + D.S(10);
            }
            if (!ts.IsEmpty)
            {
                Controls.Add(new DarkLabel { Text = text, Font = f, Bounds = new Rectangle(textX, y, ts.Width + D.S(4), ts.Height + D.S(4)), Flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix });
                y += ts.Height + D.S(4);
            }
            y = Math.Max(y, D.S(16) + iconSz) + D.S(16);
            if (checkText != null)
            {
                _check = new DarkCheck { Text = checkText, Font = f, Bounds = new Rectangle(textX, y, w - textX - D.S(16), D.S(22)) };
                Controls.Add(_check);
                y += D.S(30);
            }
            int barY = y;
            ClientSize = new Size(w, barY + D.S(42));
            var bar = new Panel { Bounds = new Rectangle(0, barY, w, ClientSize.Height - barY), BackColor = Theme.DialogPanel };
            Controls.Add(bar);
            int x = w - D.S(12) - buttonsW;
            for (int i = 0; i < buttons.Length; i++)
            {
                int idx = i;
                var b = new DarkButton { Text = buttons[i], Bounds = new Rectangle(x, D.S(10), btnW, D.S(23)), Font = f, TabIndex = i };
                b.Click += (s, e) => { _result = idx; DialogResult = DialogResult.OK; Close(); };
                bar.Controls.Add(b);
                if (i == def) { AcceptButton = b; ActiveControl = b; }
                x += btnW + gap;
            }
            _kindSound = kind == Kind.Warning || kind == Kind.Error;
        }

        readonly bool _kindSound;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_kindSound) Native.MessageBeep(0x30);
            (AcceptButton as Control)?.Focus();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { _result = _cancelIndex; Close(); return true; }
            if (keyData == (Keys.Control | Keys.C)) { if (!TestHook.Enabled) { try { Clipboard.SetText(Text); } catch (System.Runtime.InteropServices.ExternalException ex) { ErrorLog.Write("Clipboard", ex); } } return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public static int Show(Form owner, string title, string text, string[] buttons, int def, Kind kind) => Show(owner, title, null, text, buttons, def, kind, null, out _);

        public static int Show(Form owner, string title, string instruction, string text, string[] buttons, int def, Kind kind, string checkText, out bool checkedValue)
        {
            using (var d = new MessageDialog(owner, title, instruction, text, buttons, def, kind, checkText))
            {
                if (owner != null && owner.Visible) d.ShowDialog(owner); else d.ShowDialog();
                checkedValue = d.CheckValue;
                return d._result < 0 ? d._cancelIndex : d._result;
            }
        }

        public static void Error(Form owner, string instruction, string text) => Show(owner, Strings.AppTitle, instruction, text, new[] { "OK" }, 0, Kind.Error, null, out _);

        sealed class MessageIcon : Control
        {
            public Kind Kind;
            public MessageIcon() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.Dialog);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new RectangleF(1, 1, Width - 2, Height - 2);
                Color c = Kind == Kind.Error ? Theme.C(0xD9433A) : Kind == Kind.Warning ? Theme.C(0xE8A317) : Theme.C(0x2B79D0);
                string glyph = Kind == Kind.Error ? "✕" : Kind == Kind.Warning ? "!" : Kind == Kind.Question ? "?" : "i";
                if (Kind == Kind.Warning)
                    using (var b = new SolidBrush(c)) g.FillPolygon(b, new[] { new PointF(r.X + r.Width / 2, r.Y + 1), new PointF(r.Right, r.Bottom - 1), new PointF(r.X, r.Bottom - 1) });
                else using (var b = new SolidBrush(c)) g.FillEllipse(b, r);
                using (var f = new Font("Segoe UI", Height * 0.5f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(glyph, f, Brushes.White, new RectangleF(r.X, r.Y + (Kind == Kind.Warning ? Height * 0.1f : 0), r.Width, r.Height), sf);
            }
        }
    }
}
