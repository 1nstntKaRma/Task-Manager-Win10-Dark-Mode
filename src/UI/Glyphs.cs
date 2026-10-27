using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace TaskManagerDark
{
    /// <summary>Vector glyphs drawn by the app itself (no artwork is taken from Windows).</summary>
    public static class Glyphs
    {
        /// <summary>The application icon: a dark window frame with a rising activity graph.</summary>
        public static void AppIcon(Graphics g, int s)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float m = Math.Max(0.5f, s / 32f);
            var outer = new RectangleF(1.5f * m, 3.5f * m, s - 3f * m, s - 7f * m);
            float rad = Math.Max(1f, 3f * m);
            using (var p = Draw.RoundRect(outer, rad))
            {
                using (var b = new LinearGradientBrush(outer, Color.FromArgb(0x2A, 0x3A, 0x4E), Color.FromArgb(0x16, 0x1E, 0x2A), 90f)) g.FillPath(b, p);
                using (var pen = new Pen(Color.FromArgb(0x5A, 0x8F, 0xC8), Math.Max(1f, 1.2f * m))) g.DrawPath(pen, p);
            }
            // title strip
            var title = new RectangleF(outer.X + 1.2f * m, outer.Y + 1.2f * m, outer.Width - 2.4f * m, Math.Max(1.5f, 4f * m));
            using (var b = new SolidBrush(Color.FromArgb(0x3C, 0x6E, 0xA8))) g.FillRectangle(b, title);
            // graph
            float gx = outer.X + 3f * m, gy = title.Bottom + 2f * m, gw = outer.Width - 6f * m, gh = outer.Bottom - gy - 3f * m;
            var pts = new[] { new PointF(gx, gy + gh * 0.80f), new PointF(gx + gw * 0.22f, gy + gh * 0.62f), new PointF(gx + gw * 0.40f, gy + gh * 0.78f),
                              new PointF(gx + gw * 0.62f, gy + gh * 0.30f), new PointF(gx + gw * 0.80f, gy + gh * 0.48f), new PointF(gx + gw, gy + gh * 0.08f) };
            using (var path = new GraphicsPath())
            {
                path.AddLines(pts);
                path.AddLine(gx + gw, gy + gh, gx, gy + gh);
                path.CloseFigure();
                using (var b = new SolidBrush(Color.FromArgb(0x70, 0x3F, 0xC1, 0x6F))) g.FillPath(b, path);
            }
            using (var pen = new Pen(Color.FromArgb(0x6C, 0xE0, 0x8C), Math.Max(1f, 1.6f * m)) { LineJoin = LineJoin.Round }) g.DrawLines(pen, pts);
        }

        /// <summary>Green leaf shown in the Status column for suspended (UWP) processes.</summary>
        public static void Leaf(Graphics g, RectangleF r)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new GraphicsPath())
            {
                float x = r.X, y = r.Y, w = r.Width, h = r.Height;
                p.AddBezier(x + w * 0.10f, y + h * 0.90f, x + w * 0.05f, y + h * 0.30f, x + w * 0.45f, y + h * 0.05f, x + w * 0.92f, y + h * 0.06f);
                p.AddBezier(x + w * 0.92f, y + h * 0.06f, x + w * 0.95f, y + h * 0.55f, x + w * 0.65f, y + h * 0.92f, x + w * 0.10f, y + h * 0.90f);
                p.CloseFigure();
                using (var b = new SolidBrush(Theme.StatusSuspended)) g.FillPath(b, p);
                using (var pen = new Pen(Theme.Surface, Math.Max(1f, w / 12f))) g.DrawLine(pen, x + w * 0.18f, y + h * 0.82f, x + w * 0.70f, y + h * 0.30f);
            }
            g.SmoothingMode = old;
        }

        public static void Gear(Graphics g, RectangleF r, Color c)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, ro = r.Width * 0.47f, ri = r.Width * 0.34f;
            using (var p = new GraphicsPath())
            {
                int teeth = 8;
                var pts = new PointF[teeth * 4];
                for (int i = 0; i < teeth; i++)
                {
                    double a0 = (i - 0.22) * Math.PI * 2 / teeth, a1 = (i + 0.22) * Math.PI * 2 / teeth, a2 = (i + 0.5 - 0.25) * Math.PI * 2 / teeth, a3 = (i + 0.5 + 0.25) * Math.PI * 2 / teeth;
                    pts[i * 4] = new PointF(cx + (float)Math.Cos(a0) * ro, cy + (float)Math.Sin(a0) * ro);
                    pts[i * 4 + 1] = new PointF(cx + (float)Math.Cos(a1) * ro, cy + (float)Math.Sin(a1) * ro);
                    pts[i * 4 + 2] = new PointF(cx + (float)Math.Cos(a2) * ri, cy + (float)Math.Sin(a2) * ri);
                    pts[i * 4 + 3] = new PointF(cx + (float)Math.Cos(a3) * ri, cy + (float)Math.Sin(a3) * ri);
                }
                p.AddPolygon(pts);
                float hr = r.Width * 0.15f;
                p.AddEllipse(cx - hr, cy - hr, hr * 2, hr * 2);
                p.FillMode = FillMode.Alternate;
                using (var b = new SolidBrush(c)) g.FillPath(b, p);
            }
            g.SmoothingMode = old;
        }

        public static void User(Graphics g, RectangleF r, Color c)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = r.Width, h = r.Height;
            using (var b = new SolidBrush(c))
            {
                g.FillEllipse(b, r.X + w * 0.30f, r.Y + h * 0.06f, w * 0.40f, h * 0.42f);
                using (var p = new GraphicsPath())
                {
                    p.AddArc(r.X + w * 0.10f, r.Y + h * 0.52f, w * 0.80f, h * 0.80f, 180, 180);
                    p.CloseFigure();
                    g.FillPath(b, p);
                }
            }
            g.SmoothingMode = old;
        }

        /// <summary>Generic window glyph (fallback icon for apps and window rows).</summary>
        public static void Window(Graphics g, RectangleF r, Color c)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            using (var pen = new Pen(c)) g.DrawRectangle(pen, r.X + 1, r.Y + 2, r.Width - 3, r.Height - 5);
            using (var b = new SolidBrush(c)) g.FillRectangle(b, r.X + 1, r.Y + 2, r.Width - 2, Math.Max(2, r.Height / 5));
            g.SmoothingMode = old;
        }

        /// <summary>Wi-Fi style signal bars; 'level' 0..4.</summary>
        public static void SignalBars(Graphics g, RectangleF r, int level, Color on, Color off)
        {
            float bw = r.Width / 5.5f;
            for (int i = 0; i < 4; i++)
            {
                float bh = r.Height * (i + 1) / 4f;
                var br = new RectangleF(r.X + i * bw * 1.4f, r.Bottom - bh, bw, bh);
                using (var b = new SolidBrush(i < level ? on : off)) g.FillRectangle(b, br);
            }
        }

        /// <summary>Small round chevron button used by "Fewer details" / "More details".</summary>
        public static void DetailsToggle(Graphics g, RectangleF r, bool up, Color c)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(c, Math.Max(1f, r.Width / 14f))) g.DrawEllipse(pen, r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
            Draw.Chevron(g, r.X + r.Width / 2f, r.Y + r.Height / 2f + (up ? -0.5f : 0.5f), r.Width * 0.45f, c, up ? 1 : 0, Math.Max(1f, r.Width / 12f));
            g.SmoothingMode = old;
        }

        /// <summary>Resource Monitor link icon: a tiny gauge.</summary>
        public static void Gauge(Graphics g, RectangleF r, Color c)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(c, Math.Max(1f, r.Width / 10f)))
            {
                g.DrawArc(pen, r.X + 1, r.Y + 2, r.Width - 2, r.Width - 2, 180, 180);
                g.DrawLine(pen, r.X + r.Width / 2, r.Y + r.Width / 2 + 1, r.X + r.Width * 0.78f, r.Y + r.Height * 0.28f);
                g.DrawLine(pen, r.X + 1, r.Bottom - 2, r.Right - 1, r.Bottom - 2);
            }
            g.SmoothingMode = old;
        }
        /// <summary>5-pointed star used by the "Set default tab" button on the right side of the tab bar.</summary>
        public static void DefaultTabStar(Graphics g, RectangleF r, bool filled, Color c)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f + r.Height * 0.03f;
            float ro = r.Width * 0.48f, ri = ro * 0.40f;
            var pts = new PointF[10];
            for (int i = 0; i < 10; i++)
            {
                double a = -Math.PI / 2.0 + i * Math.PI / 5.0;
                float rad = (i % 2 == 0) ? ro : ri;
                pts[i] = new PointF(cx + (float)(Math.Cos(a) * rad), cy + (float)(Math.Sin(a) * rad));
            }
            if (filled)
            {
                using (var b = new SolidBrush(c)) g.FillPolygon(b, pts);
            }
            else
            {
                using (var pen = new Pen(c, Math.Max(1.1f, r.Width / 10f)) { LineJoin = LineJoin.Round })
                    g.DrawPolygon(pen, pts);
            }
            g.SmoothingMode = old;
        }
    }
}