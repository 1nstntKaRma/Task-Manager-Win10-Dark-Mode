using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

namespace TaskManagerDark
{
    /// <summary>
    /// Direct GDI drawing with cached fonts and brushes. The lists repaint every update with hundreds of
    /// cells, so they skip GDI+/TextRenderer and draw straight into the back buffer's DC.
    /// </summary>
    public static class Gdi
    {
        public const int DT_LEFT = 0, DT_CENTER = 1, DT_RIGHT = 2, DT_VCENTER = 4, DT_BOTTOM = 8, DT_WORDBREAK = 0x10, DT_SINGLELINE = 0x20;
        public const int DT_NOCLIP = 0x100, DT_CALCRECT = 0x400, DT_NOPREFIX = 0x800, DT_END_ELLIPSIS = 0x8000, DT_HIDEPREFIX = 0x100000;
        public const int Left = DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX | DT_END_ELLIPSIS;
        public const int Right = DT_RIGHT | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX | DT_END_ELLIPSIS;
        public const int Center = DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX | DT_END_ELLIPSIS;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DrawTextW")] static extern int DrawText(IntPtr hdc, string s, int n, ref RECT r, int flags);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetTextExtentPoint32W")] static extern bool GetTextExtentPoint32(IntPtr hdc, string s, int n, out SIZE sz);

        static readonly Dictionary<Font, IntPtr> _fonts = new Dictionary<Font, IntPtr>();
        static readonly Dictionary<int, IntPtr> _brushes = new Dictionary<int, IntPtr>();
        static IntPtr _measureDc;

        public static IntPtr HFont(Font f)
        {
            if (!_fonts.TryGetValue(f, out var h)) { h = f.ToHfont(); _fonts[f] = h; }
            return h;
        }

        public static IntPtr Brush(Color c)
        {
            int k = c.ToArgb();
            if (!_brushes.TryGetValue(k, out var b)) { b = Native.CreateSolidBrush(Native.Rgb(c)); _brushes[k] = b; }
            return b;
        }

        public static void Fill(IntPtr hdc, Rectangle r, Color c)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            var rc = new RECT(r.Left, r.Top, r.Right, r.Bottom);
            Native.FillRect(hdc, ref rc, Brush(c));
        }

        public static void Frame(IntPtr hdc, Rectangle r, Color c)
        {
            Fill(hdc, new Rectangle(r.X, r.Y, r.Width, 1), c);
            Fill(hdc, new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
            Fill(hdc, new Rectangle(r.X, r.Y, 1, r.Height), c);
            Fill(hdc, new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
        }

        public static void Text(IntPtr hdc, string s, Font f, Rectangle r, Color c, int flags = Left)
        {
            if (string.IsNullOrEmpty(s) || r.Width <= 0) return;
            Native.SelectObject(hdc, HFont(f));
            Native.SetTextColor(hdc, Native.Rgb(c));
            Native.SetBkMode(hdc, Native.TRANSPARENT);
            var rc = new RECT(r.Left, r.Top, r.Right, r.Bottom);
            DrawText(hdc, s, s.Length, ref rc, flags);
        }

        public static int Width(string s, Font f)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            if (_measureDc == IntPtr.Zero) _measureDc = Native.CreateCompatibleDC(IntPtr.Zero);
            Native.SelectObject(_measureDc, HFont(f));
            GetTextExtentPoint32(_measureDc, s, s.Length, out var sz);
            return sz.cx;
        }

        public static void Icon(IntPtr hdc, IntPtr hIcon, int x, int y, int size)
        {
            if (hIcon == IntPtr.Zero) return;
            Sys.DrawIconEx(hdc, x, y, hIcon, size, size, 0, IntPtr.Zero, 3);
        }
    }
}
