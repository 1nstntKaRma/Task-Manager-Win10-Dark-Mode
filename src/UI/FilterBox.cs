using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TaskManagerDark
{
    /// <summary>
    /// The filter field in the tab row (Ctrl+F): magnifier, text and a clear button in a slim rounded box.
    /// Esc clears it; Down / Enter hand the keyboard to the list.
    /// </summary>
    public sealed class FilterBox : DarkControl
    {
        sealed class Input : TextBox
        {
            protected override bool IsInputKey(Keys k)
            {
                switch (k & Keys.KeyCode)
                {
                    case Keys.Escape: case Keys.Enter: case Keys.Up: case Keys.Down: case Keys.PageUp: case Keys.PageDown: return true;
                }
                return base.IsInputKey(k);
            }
        }

        public readonly TextBox Box = new Input { BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Text };
        bool _hoverClear, _downClear, _hover;
        /// <summary>The text changed.</summary>
        public event Action FilterChanged;
        /// <summary>Esc: clear and close.</summary>
        public event Action Dismissed;
        /// <summary>Down / Enter / Page Down: continue in the list.</summary>
        public event Action ToList;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        public FilterBox()
        {
            TabStop = false;
            Controls.Add(Box);
            Box.TextChanged += (s, e) => { Invalidate(); FilterChanged?.Invoke(); };
            Box.GotFocus += (s, e) => Invalidate();
            Box.LostFocus += (s, e) => Invalidate();
            Box.MouseEnter += (s, e) => { _hover = true; Invalidate(); };
            Box.MouseLeave += (s, e) => { _hover = false; Invalidate(); };
            Box.HandleCreated += (s, e) => SendMessage(Box.Handle, 0x1501 /*EM_SETCUEBANNER*/, (IntPtr)1, "Filter");
            Box.KeyDown += (s, e) =>
            {
                switch (e.KeyData)
                {
                    case Keys.Escape: e.SuppressKeyPress = true; Dismissed?.Invoke(); break;
                    case Keys.Enter: case Keys.Down: case Keys.PageDown: e.SuppressKeyPress = true; ToList?.Invoke(); break;
                    case Keys.Up: case Keys.PageUp: e.SuppressKeyPress = true; break;
                    case Keys.Control | Keys.A: e.SuppressKeyPress = true; Box.SelectAll(); break;
                }
            };
        }

        public override string Text { get => Box.Text; set => Box.Text = value ?? ""; }
        public bool HasText => Box.TextLength > 0;
        public void FocusText() { Box.Focus(); Box.SelectAll(); }

        Rectangle ClearRect => new Rectangle(Width - D.S(21), (Height - D.S(16)) / 2, D.S(16), D.S(16));

        protected override void OnDpiApplied() { Box.Font = Theme.UiFont(D); LayoutBox(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutBox(); }

        void LayoutBox()
        {
            int h = Box.PreferredHeight, left = D.S(24);
            Box.Bounds = new Rectangle(left, Math.Max(1, (Height - h) / 2), Math.Max(4, Width - left - D.S(24)), h);
        }

        protected override void Render(Graphics g, Rectangle clip)
        {
            Draw.Fill(g, ClientRectangle, Theme.Window);
            var old = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Color border = Box.Focused ? Theme.Focus : _hover ? Theme.InputBorderHover : Theme.InputBorder;
            using (var p = Draw.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), D.F(3)))
            {
                g.FillPath(Theme.Brush(Theme.Input), p);
                g.DrawPath(Theme.Pen(border), p);
            }
            g.SmoothingMode = old;
            float isz = D.S(13);
            Glyphs.Magnifier(g, new RectangleF(D.S(7), (Height - isz) / 2f, isz, isz), HasText || Box.Focused ? Theme.Text : Theme.TextDim);
            if (HasText)
            {
                var cr = ClearRect;
                if (_hoverClear) Draw.Fill(g, cr, _downClear ? Theme.Pressed : Theme.Hover);
                float x = D.S(4);
                Glyphs.Cross(g, new RectangleF(cr.X + x, cr.Y + x, cr.Width - 2 * x, cr.Height - 2 * x), _hoverClear ? Theme.Text : Theme.TextDim);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool h = HasText && ClearRect.Contains(e.Location);
            Cursor = h ? Cursors.Default : Cursors.IBeam;
            if (h != _hoverClear) { _hoverClear = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hoverClear = _downClear = false; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (HasText && ClearRect.Contains(e.Location)) { _downClear = true; Invalidate(); return; }
            Box.Focus();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            bool was = _downClear;
            _downClear = false;
            if (was && ClearRect.Contains(e.Location)) { Box.Text = ""; Box.Focus(); }
            Invalidate();
        }
    }
}
