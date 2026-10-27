using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TaskManagerDark
{
    /// <summary>The dark palette. Every surface of the app pulls its colors from here.</summary>
    public static class Theme
    {
        // window chrome
        public static readonly Color Window = C(0x202020);          // menu bar, tab row, command bar
        public static readonly Color Surface = C(0x191919);         // lists and pages
        public static readonly Color SurfaceAlt = C(0x1E1E1E);
        public static readonly Color Border = C(0x3A3A3A);
        public static readonly Color BorderSoft = C(0x2C2C2C);
        public static readonly Color Text = C(0xE6E6E6);
        public static readonly Color TextDim = C(0xA3A3A3);
        public static readonly Color TextFaint = C(0x7E7E7E);
        public static readonly Color TextDisabled = C(0x6A6A6A);
        public static readonly Color Link = C(0x5DADF2);
        public static readonly Color LinkHover = C(0x8AC4F7);

        // menus / tabs
        public static readonly Color MenuBarHover = C(0x353535);
        public static readonly Color MenuBarOpen = C(0x404040);
        public static readonly Color TabSelected = C(0x191919);
        public static readonly Color TabHover = C(0x2D2D2D);
        public static readonly Color TabText = C(0xBDBDBD);

        // lists
        public static readonly Color RowHover = C(0x262D33);
        public static readonly Color RowSelected = C(0x173A57);
        public static readonly Color RowSelectedBorder = C(0x2B6A9E);
        public static readonly Color RowSelectedInactive = C(0x2C3238);
        public static readonly Color GroupText = C(0x7FB5EC);
        public static readonly Color HeaderText = C(0xC8C8C8);
        public static readonly Color HeaderHover = C(0x262626);
        public static readonly Color HeaderPressed = C(0x303030);
        public static readonly Color HeaderDivider = C(0x343434);
        public static readonly Color GridLine = C(0x262626);
        public static readonly Color StatusSuspended = C(0x58B94A);

        // buttons / inputs (dialogs)
        public static readonly Color Hover = C(0x3A3A3A);
        public static readonly Color HoverBorder = C(0x505050);
        public static readonly Color Pressed = C(0x474747);
        public static readonly Color Checked = C(0x1E3A5A);
        public static readonly Color CheckedBorder = C(0x3B7BC4);
        public static readonly Color CheckedHover = C(0x25476D);
        public static readonly Color Accent = C(0x2B6FC2);
        public static readonly Color AccentHover = C(0x3A80D4);
        public static readonly Color AccentDown = C(0x1F5BA3);
        public static readonly Color AccentText = C(0xFFFFFF);
        public static readonly Color Focus = C(0x4C9AEE);

        public static readonly Color Dialog = C(0x202020);
        public static readonly Color DialogPanel = C(0x2B2B2B);
        public static readonly Color Input = C(0x161616);
        public static readonly Color InputBorder = C(0x555555);
        public static readonly Color InputBorderHover = C(0x707070);
        public static readonly Color Button = C(0x333333);
        public static readonly Color ButtonBorder = C(0x4D4D4D);

        public static readonly Color ScrollTrack = C(0x1C1C1C);
        public static readonly Color ScrollThumb = C(0x4A4A4A);
        public static readonly Color ScrollThumbHover = C(0x666666);
        public static readonly Color ScrollThumbDown = C(0x7A7A7A);
        public static readonly Color ScrollArrow = C(0x9A9A9A);

        public static readonly Color Popup = C(0x2B2B2B);
        public static readonly Color PopupBorder = C(0x4A4A4A);
        public static readonly Color PopupHover = C(0x3D3D3D);
        public static readonly Color PopupHeader = C(0x333333);
        public static readonly Color MenuSep = C(0x444444);
        public static readonly Color Tooltip = C(0x2B2B2B);
        public static readonly Color TooltipBorder = C(0x555555);

        // performance graphs: line, fill, grid (dark adapted versions of Task Manager's hues)
        public static readonly GraphColors CpuGraph = new GraphColors(0x2E9BE6, 0x0F2A3D, 0x15354B, 0x16324A);
        public static readonly GraphColors MemGraph = new GraphColors(0xB45BDB, 0x2D1838, 0x3A2347, 0x33203F);
        public static readonly GraphColors DiskGraph = new GraphColors(0x5CB82A, 0x182B10, 0x22391A, 0x1E3317);
        public static readonly GraphColors NetGraph = new GraphColors(0xD67A2C, 0x33200F, 0x452C17, 0x3B2614);
        public static readonly GraphColors GpuGraph = new GraphColors(0x2E9BE6, 0x0F2A3D, 0x15354B, 0x16324A);
        public static readonly Color DiskWrite = C(0x8BDD3A);
        public static readonly Color NetSend = C(0xE98A47);
        public static readonly Color KernelFill = C(0x1A4B6E);
        public static readonly Color GraphBack = C(0x141414);

        // memory composition bar
        public static readonly Color MemInUse = C(0x6E3A86);
        public static readonly Color MemModified = C(0x8A5A2E);
        public static readonly Color MemStandby = C(0x3B2A45);
        public static readonly Color MemFree = C(0x191919);

        /// <summary>Heat-map ramp for resource cells: a faint amber base deepening to orange and red.</summary>
        static readonly Color[] _heat =
        {
            C(0x26221A), C(0x2F2918), C(0x3A3016), C(0x473713), C(0x564011), C(0x664610),
            C(0x744611), C(0x7F4114), C(0x883818), C(0x8E2E1C), C(0x93251F),
        };

        /// <summary>Heat color for a level in [0, 1].</summary>
        public static Color Heat(double level)
        {
            if (double.IsNaN(level) || level <= 0) return _heat[0];
            if (level >= 1) return _heat[_heat.Length - 1];
            double p = level * (_heat.Length - 1);
            int i = (int)p;
            return Blend(_heat[i], _heat[i + 1], (float)(p - i));
        }

        /// <summary>Header tint for overall utilization: none until it gets busy, then amber to red.</summary>
        public static Color HeaderHeat(double fraction)
        {
            if (fraction < 0.7) return Surface;
            if (fraction < 0.8) return C(0x4A3A14);
            if (fraction < 0.9) return C(0x6B4413);
            return C(0x8A2E1C);
        }

        public static Color C(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

        // ---------- fonts (pixel based so they are DPI independent) ----------
        static readonly Dictionary<long, Font> _fonts = new Dictionary<long, Font>();
        public static Font UiFont(int dpi) => GetFont(dpi, 12f, FontStyle.Regular);
        public static Font UiFontBold(int dpi) => GetFont(dpi, 12f, FontStyle.Bold);
        public static Font SmallFont(int dpi) => GetFont(dpi, 11f, FontStyle.Regular);
        public static Font GetFont(int dpi, float px, FontStyle style, string face = "Segoe UI")
        {
            long key = ((long)dpi << 40) | ((long)(px * 10) << 24) | ((long)style << 16) | (long)(face.GetHashCode() & 0xFFFF);
            lock (_fonts)
            {
                if (!_fonts.TryGetValue(key, out var f))
                {
                    f = new Font(face, px * dpi / 96f, style, GraphicsUnit.Pixel);
                    _fonts[key] = f;
                }
                return f;
            }
        }
        public static Font Light(int dpi, float px) => GetFont(dpi, px, FontStyle.Regular, "Segoe UI Light");
        public static Font Semilight(int dpi, float px) => GetFont(dpi, px, FontStyle.Regular, "Segoe UI Semilight");

        // ---------- cached GDI+ objects ----------
        static readonly Dictionary<int, SolidBrush> _brushes = new Dictionary<int, SolidBrush>();
        static readonly Dictionary<long, Pen> _pens = new Dictionary<long, Pen>();
        public static SolidBrush Brush(Color c)
        {
            int k = c.ToArgb();
            if (!_brushes.TryGetValue(k, out var b)) { b = new SolidBrush(c); _brushes[k] = b; }
            return b;
        }
        public static Pen Pen(Color c, float w = 1f)
        {
            long k = ((long)c.ToArgb() << 16) ^ (long)(w * 64);
            if (!_pens.TryGetValue(k, out var p)) { p = new Pen(c, w); _pens[k] = p; }
            return p;
        }

        public static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
    }

    public sealed class GraphColors
    {
        public readonly Color Line, Fill, Grid, Border;
        public GraphColors(int line, int fill, int grid, int border)
        {
            Line = Theme.C(line); Fill = Theme.C(fill); Grid = Theme.C(grid); Border = Theme.Blend(Theme.C(border), Theme.C(line), 0.55f);
        }
    }

    /// <summary>Scaling helper bound to a DPI value.</summary>
    public struct Dpi
    {
        public readonly int Value;
        public Dpi(int v) { Value = v <= 0 ? 96 : v; }
        public int S(int v) => (int)Math.Round(v * Value / 96.0);
        public float F(float v) => v * Value / 96f;
        public float Scale => Value / 96f;
        public static implicit operator int(Dpi d) => d.Value;
    }

    /// <summary>Small drawing helpers shared by every custom-drawn surface.</summary>
    public static class Draw
    {
        public const TextFormatFlags TextLeft = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        public const TextFormatFlags TextCenter = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        public const TextFormatFlags TextRight = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        public const TextFormatFlags Ellipsis = TextFormatFlags.EndEllipsis;

        public static void Fill(Graphics g, Rectangle r, Color c) => g.FillRectangle(Theme.Brush(c), r);
        public static void Rect(Graphics g, Rectangle r, Color c)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            g.DrawRectangle(Theme.Pen(c), r.X, r.Y, r.Width - 1, r.Height - 1);
        }
        public static void HLine(Graphics g, int x0, int x1, int y, Color c) => g.FillRectangle(Theme.Brush(c), x0, y, x1 - x0, 1);
        public static void VLine(Graphics g, int x, int y0, int y1, Color c) => g.FillRectangle(Theme.Brush(c), x, y0, 1, y1 - y0);

        public static void Text(Graphics g, string s, Font f, Rectangle r, Color c, TextFormatFlags flags = TextLeft)
        {
            if (string.IsNullOrEmpty(s) || r.Width <= 0) return;
            TextRenderer.DrawText(g, s, f, r, c, flags | TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.PreserveGraphicsClipping);
        }

        public static Size Measure(string s, Font f, TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine)
        {
            if (string.IsNullOrEmpty(s)) return new Size(0, f.Height);
            return TextRenderer.MeasureText(s, f, new Size(int.MaxValue, int.MaxValue), flags);
        }

        public static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            if (rad <= 0.01f) { p.AddRectangle(r); return p; }
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>Thin chevron (Task Manager's expando / sort arrows / "fewer details" button).</summary>
        public static void Chevron(Graphics g, float cx, float cy, float size, Color c, int dir, float width = 1.2f)
        {
            // dir: 0 down, 1 up, 2 right, 3 left
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float h = size / 2f, q = size / 4f;
            PointF[] pts;
            switch (dir)
            {
                case 1: pts = new[] { new PointF(cx - h, cy + q), new PointF(cx, cy - q), new PointF(cx + h, cy + q) }; break;
                case 2: pts = new[] { new PointF(cx - q, cy - h), new PointF(cx + q, cy), new PointF(cx - q, cy + h) }; break;
                case 3: pts = new[] { new PointF(cx + q, cy - h), new PointF(cx - q, cy), new PointF(cx + q, cy + h) }; break;
                default: pts = new[] { new PointF(cx - h, cy - q), new PointF(cx, cy + q), new PointF(cx + h, cy - q) }; break;
            }
            using (var p = new Pen(c, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }) g.DrawLines(p, pts);
            g.SmoothingMode = old;
        }

        /// <summary>Small filled triangle (menu submenu arrow).</summary>
        public static void RightArrow(Graphics g, int cx, int cy, Color c, Dpi d)
        {
            int h = Math.Max(7, d.S(7)) | 1;
            int w = (h + 1) / 2;
            for (int i = 0; i < w; i++)
                g.FillRectangle(Theme.Brush(c), cx - w / 2 + i, cy - h / 2 + i, 1, h - 2 * i);
        }

        public static void CheckMark(Graphics g, Rectangle r, Color c, Dpi d)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Math.Min(r.Width, r.Height);
            float x = r.X + (r.Width - s) / 2f, y = r.Y + (r.Height - s) / 2f;
            using (var p = new Pen(c, Math.Max(1.4f, d.F(1.6f))))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                g.DrawLines(p, new[] { new PointF(x + s * 0.18f, y + s * 0.52f), new PointF(x + s * 0.42f, y + s * 0.75f), new PointF(x + s * 0.84f, y + s * 0.26f) });
            }
            g.SmoothingMode = old;
        }

        public static void ButtonFace(Graphics g, Rectangle r, bool hover, bool pressed, bool isChecked, bool enabled = true)
        {
            if (!enabled && !isChecked) return;
            Color bg, border;
            if (isChecked)
            {
                bg = pressed ? Theme.Pressed : hover ? Theme.CheckedHover : Theme.Checked;
                border = Theme.CheckedBorder;
            }
            else if (pressed) { bg = Theme.Pressed; border = Theme.HoverBorder; }
            else if (hover) { bg = Theme.Hover; border = Theme.HoverBorder; }
            else return;
            Fill(g, r, bg);
            Rect(g, r, border);
        }
    }
}
