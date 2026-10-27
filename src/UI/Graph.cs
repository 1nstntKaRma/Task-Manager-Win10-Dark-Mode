using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace TaskManagerDark
{
    /// <summary>Task Manager style history graph: scrolling grid, filled area, crisp line, optional second (dashed) line.</summary>
    public static class Graph
    {
        public sealed class Line
        {
            public Series Data;
            public Color Color;
            public bool Fill = true, Dashed;
            public Color FillColor;
        }

        /// <param name="scale">value that maps to the top of the graph</param>
        /// <param name="tick">update counter (the vertical grid lines move with it)</param>
        public static void Draw(Graphics g, Rectangle r, GraphColors colors, double scale, int tick, bool inactive, params Line[] lines)
        {
            if (r.Width < 4 || r.Height < 4) return;
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            g.FillRectangle(Theme.Brush(Theme.GraphBack), r);
            var inner = new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
            var grid = inactive ? Theme.C(0x2A2A2A) : colors.Grid;
            // grid: 10 rows, columns 1/10 of the width that scroll left with every update
            for (int i = 1; i < 10; i++) g.FillRectangle(Theme.Brush(grid), inner.X, inner.Y + inner.Height * i / 10, inner.Width, 1);
            float step = inner.Width / 59f;
            int shift = tick % 6;
            for (int k = 0; k <= 11; k++)
            {
                float x = inner.Right - (k * 6 + shift) * step;
                if (x > inner.X && x < inner.Right) g.FillRectangle(Theme.Brush(grid), (int)x, inner.Y, 1, inner.Height);
            }
            if (!inactive && scale > 0)
            {
                g.SetClip(inner);
                foreach (var l in lines) if (l?.Data != null && l.Fill && !l.Dashed) FillArea(g, inner, l, scale, step);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                foreach (var l in lines) if (l?.Data != null) DrawLine(g, inner, l, scale, step);
                g.ResetClip();
            }
            g.SmoothingMode = SmoothingMode.None;
            g.DrawRectangle(Theme.Pen(inactive ? Theme.C(0x444444) : colors.Border), r.X, r.Y, r.Width - 1, r.Height - 1);
            g.SmoothingMode = old;
        }

        static PointF[] Points(Rectangle inner, Series s, double scale, float step)
        {
            int n = s.Count;
            var pts = new PointF[n];
            for (int i = 0; i < n; i++)
            {
                double v = Math.Max(0, Math.Min(scale, s[i])) / scale;
                pts[i] = new PointF(inner.Right - 1 - (n - 1 - i) * step, (float)(inner.Bottom - 1 - v * (inner.Height - 1)));
            }
            return pts;
        }

        static void FillArea(Graphics g, Rectangle inner, Line l, double scale, float step)
        {
            if (l.Data.Count < 2) return;
            var pts = Points(inner, l.Data, scale, step);
            var poly = new PointF[pts.Length + 2];
            Array.Copy(pts, poly, pts.Length);
            poly[pts.Length] = new PointF(pts[pts.Length - 1].X, inner.Bottom);
            poly[pts.Length + 1] = new PointF(pts[0].X, inner.Bottom);
            g.FillPolygon(Theme.Brush(l.FillColor.IsEmpty ? Color.FromArgb(70, l.Color) : l.FillColor), poly);
        }

        static void DrawLine(Graphics g, Rectangle inner, Line l, double scale, float step)
        {
            if (l.Data.Count < 2) return;
            var pts = Points(inner, l.Data, scale, step);
            if (l.Dashed) using (var p = new Pen(l.Color, 1.4f) { DashStyle = DashStyle.Dash }) g.DrawLines(p, pts);
            else using (var p = new Pen(l.Color, 1.4f)) g.DrawLines(p, pts);
        }

        /// <summary>Legend marker drawn before "Read speed" / "Send" labels: a short solid or dashed bar.</summary>
        public static void Legend(Graphics g, Rectangle r, Color c, bool dashed)
        {
            if (!dashed) { g.FillRectangle(Theme.Brush(c), r.X, r.Y, Math.Max(2, r.Width / 6), r.Height); return; }
            int seg = Math.Max(2, r.Height / 5);
            for (int y = r.Y; y < r.Bottom; y += seg * 2) g.FillRectangle(Theme.Brush(c), r.X, y, Math.Max(2, r.Width / 6), Math.Min(seg, r.Bottom - y));
        }

        /// <summary>Picks the graph scale for byte rates (Taskmgr strings 32840-32858).</summary>
        public static double ByteScale(double max)
        {
            double[] steps = { 100e3, 500e3, 1e6, 10e6, 100e6, 250e6, 500e6, 1e9, 10e9, 100e9 };
            foreach (var s in steps) if (max * 1.0 <= s * 1.024) return s * 1.024;
            return steps[steps.Length - 1] * 1.024;
        }

        public static string ByteScaleText(double scale)
        {
            double s = scale / 1.024;
            if (s >= 1e9) return (s / 1e9).ToString("0") + " GB/s";
            if (s >= 1e6) return (s / 1e6).ToString("0") + " MB/s";
            return (s / 1e3).ToString("0") + " KB/s";
        }

        /// <summary>Network graph scale in bits (Taskmgr strings 32820-32838).</summary>
        public static double BitScale(double max)
        {
            double[] steps = { 100e3, 500e3, 1e6, 10e6, 50e6, 100e6, 500e6, 1e9, 10e9, 100e9 };
            foreach (var s in steps) if (max <= s) return s;
            return steps[steps.Length - 1];
        }

        public static string BitScaleText(double s)
        {
            if (s >= 1e9) return (s / 1e9).ToString("0") + " Gbps";
            if (s >= 1e6) return (s / 1e6).ToString("0") + " Mbps";
            return (s / 1e3).ToString("0") + " Kbps";
        }
    }
}
