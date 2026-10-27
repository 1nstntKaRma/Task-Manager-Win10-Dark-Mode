using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TaskManagerDark
{
    /// <summary>Notification area CPU meter with Task Manager's tooltip and right-click menu (Restore, Close, Always on top).</summary>
    public sealed class TrayIcon : IDisposable
    {
        readonly MainForm _main;
        readonly NotifyIcon _icon;
        Icon _current;
        int _lastLevel = -1, _size = 16;
        public string LastTooltip = "";
        public int LastLevel => _lastLevel;

        public TrayIcon(MainForm main)
        {
            _main = main;
            _icon = new NotifyIcon { Text = Strings.AppTitle, Visible = true };
            SetLevel(0);
            _icon.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) ShowMenu(Cursor.Position); };
            _icon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) _main.RestoreFromTray(); };
            _icon.DoubleClick += (s, e) => _main.RestoreFromTray();
        }

        public void SetDpi(int dpi)
        {
            int sys = Native.GetDpiForSystem();
            _size = Native.MetricForDpi(Native.SM_CXSMICON, sys > 0 ? sys : dpi);
            int l = _lastLevel; _lastLevel = -1; SetLevel(Math.Max(0, l));
        }

        public void Update(DataEngine d)
        {
            var s = d.Sys;
            int cpu = (int)Math.Round(s.Cpu);
            int mem = s.MemTotal > 0 ? (int)Math.Round(s.MemInUse * 100.0 / s.MemTotal) : 0;
            int disk = (int)Math.Round(s.DiskMaxActive), net = (int)Math.Round(s.NetPrimaryUtil);
            // Taskmgr string 38612: "CPU %1!d!%2!s!\nMemory %3!d!%4!s!\nDisk %5!d!%6!s!\nNetwork %7!d!%8!s!"
            var tip = "CPU " + cpu + "%\nMemory " + mem + "%\nDisk " + disk + "%\nNetwork " + net + "%";
            if (tip != LastTooltip) { LastTooltip = tip; try { _icon.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip; } catch (Exception ex) { ErrorLog.Write("Tray text", ex); } }
            SetLevel(s.Cpu);
        }

        void SetLevel(double cpu)
        {
            int inner = _size - 4;
            int level = (int)Math.Round(Math.Max(0, Math.Min(100, cpu)) / 100.0 * inner);
            if (cpu > 0 && level == 0) level = 1;
            if (level == _lastLevel) return;
            _lastLevel = level;
            var icon = Render(_size, level);
            _icon.Icon = icon;
            _current?.Dispose();
            _current = icon;
        }

        public static Icon Render(int size, int level)
        {
            using (var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    var outer = new Rectangle(1, 0, size - 2, size);
                    g.FillRectangle(Theme.Brush(Theme.C(0x101010)), outer);
                    g.DrawRectangle(Theme.Pen(Theme.C(0x7A7A7A)), outer.X, outer.Y, outer.Width - 1, outer.Height - 1);
                    int inner = size - 4;
                    for (int i = 1; i < 4; i++) g.FillRectangle(Theme.Brush(Theme.C(0x1F3A1F)), 3, 2 + inner * i / 4, size - 6, 1);
                    if (level > 0)
                    {
                        var bar = new Rectangle(3, 2 + inner - level, size - 6, level);
                        using (var b = new LinearGradientBrush(new Rectangle(bar.X, 2, bar.Width, inner), Theme.C(0x8BF06B), Theme.C(0x2FA83A), 90f)) g.FillRectangle(b, bar);
                    }
                }
                IntPtr h = bmp.GetHicon();
                var icon = (Icon)Icon.FromHandle(h).Clone();
                Sys.DestroyIcon(h);
                return icon;
            }
        }

        public void ShowMenu(Point screen)
        {
            var items = new List<MenuItem>
            {
                new MenuItem("&Restore", _main.RestoreFromTray) { Bold = true, Id = 30481 },
                new MenuItem("&Close", _main.Close) { Id = 30435 },
                MenuItem.Sep(),
                MenuItem.Check("&Always on top", () => _main.AlwaysOnTop, _main.ToggleAlwaysOnTop),
            };
            var m = new MenuPopup(Native.GetDpiForSystem(), items) { Activatable = true };
            m.ShowAtPoint(screen);
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
            _current?.Dispose();
        }
    }
}
