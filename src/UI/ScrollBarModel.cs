using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace TaskManagerDark
{
    /// <summary>Geometry, hit testing and painting of a dark scroll bar drawn inside a custom control.</summary>
    public sealed class ScrollBarModel
    {
        public bool Vertical;
        public Rectangle Bounds;
        public int Value, Max, Page;          // Max = total content extent, Page = visible extent
        public int Hot;                       // 0 none, 1 arrow-, 2 track-, 3 thumb, 4 track+, 5 arrow+
        public int Pressed;
        public bool Visible => Max > Page && Page > 0;
        public int MaxValue => Math.Max(0, Max - Page);

        int ArrowLen => Vertical ? Bounds.Width : Bounds.Height;
        Rectangle Track => Vertical ? new Rectangle(Bounds.X, Bounds.Y + ArrowLen, Bounds.Width, Bounds.Height - 2 * ArrowLen)
                                    : new Rectangle(Bounds.X + ArrowLen, Bounds.Y, Bounds.Width - 2 * ArrowLen, Bounds.Height);

        public Rectangle Thumb
        {
            get
            {
                var t = Track;
                int len = Vertical ? t.Height : t.Width;
                if (Max <= 0 || len <= 0) return Rectangle.Empty;
                int tl = Math.Max(Math.Min(len, ArrowLen), (int)((long)len * Page / Max));
                int pos = MaxValue == 0 ? 0 : (int)((long)(len - tl) * Value / MaxValue);
                return Vertical ? new Rectangle(t.X, t.Y + pos, t.Width, tl) : new Rectangle(t.X + pos, t.Y, tl, t.Height);
            }
        }

        public int HitTest(Point p)
        {
            if (!Visible || !Bounds.Contains(p)) return 0;
            int a = ArrowLen;
            int along = Vertical ? p.Y - Bounds.Y : p.X - Bounds.X;
            int total = Vertical ? Bounds.Height : Bounds.Width;
            if (along < a) return 1;
            if (along >= total - a) return 5;
            var th = Thumb;
            if (th.Contains(p)) return 3;
            int tpos = Vertical ? th.Y - Bounds.Y : th.X - Bounds.X;
            return along < tpos ? 2 : 4;
        }

        /// <summary>Value for a thumb dragged so that its start is at 'thumbStart' (control coordinates along the axis).</summary>
        public int ValueFromThumb(int thumbStart)
        {
            var t = Track; var th = Thumb;
            int len = Vertical ? t.Height - th.Height : t.Width - th.Width;
            if (len <= 0) return 0;
            int off = thumbStart - (Vertical ? t.Y : t.X);
            return Math.Max(0, Math.Min(MaxValue, (int)((long)off * MaxValue / len)));
        }

        public void Paint(Graphics g, Dpi d)
        {
            if (!Visible) return;
            Draw.Fill(g, Bounds, Theme.ScrollTrack);
            var th = Thumb;
            var tc = Pressed == 3 ? Theme.ScrollThumbDown : Hot == 3 ? Theme.ScrollThumbHover : Theme.ScrollThumb;
            var inner = Vertical ? new Rectangle(th.X + d.S(4), th.Y + 1, th.Width - d.S(8), th.Height - 2) : new Rectangle(th.X + 1, th.Y + d.S(4), th.Width - 2, th.Height - d.S(8));
            Draw.Fill(g, inner, tc);
            int a = ArrowLen;
            Rectangle r1 = Vertical ? new Rectangle(Bounds.X, Bounds.Y, Bounds.Width, a) : new Rectangle(Bounds.X, Bounds.Y, a, Bounds.Height);
            Rectangle r2 = Vertical ? new Rectangle(Bounds.X, Bounds.Bottom - a, Bounds.Width, a) : new Rectangle(Bounds.Right - a, Bounds.Y, a, Bounds.Height);
            if (Hot == 1 || Pressed == 1) Draw.Fill(g, r1, Pressed == 1 ? Theme.Pressed : Theme.Hover);
            if (Hot == 5 || Pressed == 5) Draw.Fill(g, r2, Pressed == 5 ? Theme.Pressed : Theme.Hover);
            float s = a * 0.36f;
            var ac = Hot == 1 || Hot == 5 ? Theme.Text : Theme.ScrollArrow;
            FillArrow(g, r1, Vertical ? 1 : 3, s, Hot == 1 ? Theme.Text : Theme.ScrollArrow);
            FillArrow(g, r2, Vertical ? 0 : 2, s, Hot == 5 ? Theme.Text : Theme.ScrollArrow);
        }

        static void FillArrow(Graphics g, Rectangle r, int dir, float s, Color c)
        {
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, h = s / 2f, q = s / 3.5f;
            PointF[] p;
            switch (dir)
            {
                case 1: p = new[] { new PointF(cx - h, cy + q), new PointF(cx, cy - q), new PointF(cx + h, cy + q) }; break;
                case 2: p = new[] { new PointF(cx - q, cy - h), new PointF(cx + q, cy), new PointF(cx - q, cy + h) }; break;
                case 3: p = new[] { new PointF(cx + q, cy - h), new PointF(cx - q, cy), new PointF(cx + q, cy + h) }; break;
                default: p = new[] { new PointF(cx - h, cy - q), new PointF(cx, cy + q), new PointF(cx + h, cy - q) }; break;
            }
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillPolygon(Theme.Brush(c), p);
            g.SmoothingMode = old;
        }
    }
}
