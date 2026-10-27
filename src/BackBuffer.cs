using System;
using System.Drawing;
using System.Windows.Forms;

namespace TaskManagerDark
{
    /// <summary>32-bit top-down DIB section with a lazily created memory DC.</summary>
    public unsafe sealed class Dib : IDisposable
    {
        public readonly int W, H;
        public readonly uint* P;
        public readonly IntPtr HBitmap;
        IntPtr _dc, _oldBmp;
        bool _disposed;

        public Dib(int w, int h)
        {
            if (w < 1 || h < 1) throw new ArgumentException("Bitmaps must be greater than one pixel on a side.");
            var bi = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            HBitmap = Native.CreateDIBSection(IntPtr.Zero, ref bi, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (HBitmap == IntPtr.Zero || bits == IntPtr.Zero) throw new OutOfMemoryException("There is not enough memory or resources to complete operation.");
            P = (uint*)bits; W = w; H = h;
            GC.AddMemoryPressure((long)w * h * 4);
        }

        public IntPtr DC
        {
            get
            {
                if (_dc == IntPtr.Zero)
                {
                    _dc = Native.CreateCompatibleDC(IntPtr.Zero);
                    _oldBmp = Native.SelectObject(_dc, HBitmap);
                }
                return _dc;
            }
        }

        public void Dispose() { DisposeCore(); GC.SuppressFinalize(this); }
        ~Dib() { DisposeCore(); }
        void DisposeCore()
        {
            if (_disposed) return;
            _disposed = true;
            if (_dc != IntPtr.Zero) { Native.SelectObject(_dc, _oldBmp); Native.DeleteDC(_dc); _dc = IntPtr.Zero; }
            if (HBitmap != IntPtr.Zero) Native.DeleteObject(HBitmap);
            GC.RemoveMemoryPressure((long)W * H * 4);
        }
    }

    /// <summary>
    /// Persistent GDI back buffer. Painting goes into a DIB the size of the control and only the
    /// invalid rectangle is blitted to the screen, so there is never any flicker.
    /// </summary>
    public sealed class BackBuffer : IDisposable
    {
        Dib _dib;
        public Dib Dib => _dib;

        public Graphics Begin(Size size, Rectangle clip)
        {
            int w = Math.Max(1, size.Width), h = Math.Max(1, size.Height);
            if (_dib == null || _dib.W < w || _dib.H < h || _dib.W > w * 2 + 256 || _dib.H > h * 2 + 256)
            {
                _dib?.Dispose();
                _dib = new Dib(w + 64, h + 64);
            }
            var g = Graphics.FromHdc(_dib.DC);
            g.SetClip(clip);
            return g;
        }

        public void End(Graphics g, Graphics target, Rectangle clip)
        {
            g.Dispose();
            Native.GdiFlush();
            IntPtr hdc = target.GetHdc();
            try { Native.BitBlt(hdc, clip.X, clip.Y, clip.Width, clip.Height, _dib.DC, clip.X, clip.Y, Native.SRCCOPY); }
            finally { target.ReleaseHdc(hdc); }
        }

        public void Dispose() { _dib?.Dispose(); _dib = null; }
    }

    /// <summary>Base for custom drawn controls: opaque, flicker free, DPI aware.</summary>
    public class DarkControl : Control
    {
        readonly BackBuffer _buf = new BackBuffer();
        protected Dpi D = new Dpi(96);

        public DarkControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.DoubleBuffer | ControlStyles.SupportsTransparentBackColor, false);
        }

        public int DpiValue => D.Value;
        public virtual void SetDpi(int dpi) { D = new Dpi(dpi); OnDpiApplied(); Invalidate(); }
        protected virtual void OnDpiApplied() { }

        protected override void OnPaint(PaintEventArgs e)
        {
            var clip = e.ClipRectangle;
            if (clip.Width <= 0 || clip.Height <= 0) return;
            var g = _buf.Begin(ClientSize, clip);
            try { Render(g, clip); }
            catch (Exception ex) { ErrorLog.Write("Render " + GetType().Name, ex); }
            _buf.End(g, e.Graphics, clip);
        }

        protected virtual void Render(Graphics g, Rectangle clip) { }

        protected override void OnPaintBackground(PaintEventArgs pevent) { }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _buf.Dispose();
            base.Dispose(disposing);
        }
    }
}
