using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace TaskManagerDark
{
    /// <summary>
    /// Remembers what each program looks like in the lists (description, publisher, small icon) between runs, so the
    /// first paint after a start has real names and icons even when nothing is in the file cache yet (reading them
    /// fresh means opening every program file, and packaged apps cost ~10 ms each). Program entries are checked
    /// against the file's last-write time in the background afterwards; packaged apps are keyed by their package
    /// version. Kept in looks.cache next to the settings.
    /// </summary>
    public static unsafe class LookCache
    {
        public sealed class Look
        {
            public string Description, Company;
            public long Stamp;                  // program file's last write (UTC ticks); 0 for packaged apps
            public int Width, Height;
            public byte[] Pixels;               // 32 bpp BGRA, top-down; null: no icon
            public volatile bool Used;
            internal IntPtr Icon;
        }

        const int Magic = 0x4B4F4C54, FormatVersion = 1, MaxEntries = 800;
        static readonly ConcurrentDictionary<string, Look> _map = new ConcurrentDictionary<string, Look>(StringComparer.Ordinal);
        static readonly object _iconLock = new object(), _saveLock = new object();
        static readonly Lazy<bool> _loaded = new Lazy<bool>(LoadFile, LazyThreadSafetyMode.ExecutionAndPublication);
        static volatile bool _dirty;

        static string FilePath => Path.Combine(Settings.OverrideDir ?? Settings.DataDir, "looks.cache");

        public static string KeyForFile(string path, int size) => path == null ? null : "f|" + size + "|" + path.ToLowerInvariant();
        public static string KeyForApp(string aumid, string package, int size) => "a|" + size + "|" + aumid + "|" + package;

        public static Look Get(string key)
        {
            if (key == null || !_loaded.Value) return null;
            if (!_map.TryGetValue(key, out var l)) return null;
            l.Used = true;
            return l;
        }

        /// <summary>The cached icon as an HICON (created once; lives for the session like the IconCache icons).</summary>
        public static IntPtr IconOf(Look l)
        {
            if (l.Pixels == null) return IntPtr.Zero;
            lock (_iconLock)
            {
                if (l.Icon == IntPtr.Zero) l.Icon = CreateIcon(l.Pixels, l.Width, l.Height);
                return l.Icon;
            }
        }

        public static void Put(string key, string description, string company, long stamp, IntPtr icon)
        {
            if (key == null) return;
            var l = new Look { Description = description, Company = company, Stamp = stamp, Used = true };
            if (icon != IntPtr.Zero && ReadIcon(icon, out var px, out int w, out int h)) { l.Pixels = px; l.Width = w; l.Height = h; l.Icon = icon; }
            _map[key] = l;
            _dirty = true;
        }

        public static bool SameLook(Look l, string description, string company) =>
            l != null && l.Description == description && l.Company == company;

        // ------------------------------------------------------------------ file
        static bool LoadFile()
        {
            try
            {
                var file = FilePath;
                if (!File.Exists(file)) { StartupLog.Mark("look cache: none yet"); return true; }
                using (var r = new BinaryReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16)))
                {
                    if (r.ReadInt32() != Magic || r.ReadInt32() != FormatVersion) return true;
                    int n = r.ReadInt32();
                    for (int i = 0; i < n && i < MaxEntries * 2; i++)
                    {
                        string key = r.ReadString();
                        var l = new Look { Description = Null(r.ReadString()), Company = Null(r.ReadString()), Stamp = r.ReadInt64(), Width = r.ReadInt32(), Height = r.ReadInt32() };
                        int len = r.ReadInt32();
                        if (len > 0)
                        {
                            if (len != l.Width * l.Height * 4 || l.Width <= 0 || l.Width > 256 || l.Height <= 0 || l.Height > 256) return true;   // damaged: start over
                            l.Pixels = r.ReadBytes(len);
                        }
                        _map[key] = l;
                    }
                }
                StartupLog.Mark("look cache: " + _map.Count + " programs");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is EndOfStreamException || ex is FormatException)
            {
                _map.Clear();
                ErrorLog.Write("Look cache (rebuilt)", ex);
            }
            return true;
        }

        static string Null(string s) => s.Length == 0 ? null : s;

        /// <summary>Writes the cache when something changed. Programs seen in this session go first.</summary>
        public static void SaveIfDirty()
        {
            if (!_dirty) return;
            lock (_saveLock)
            {
                if (!_dirty) return;
                _dirty = false;
                try
                {
                    var entries = new List<KeyValuePair<string, Look>>(_map);
                    entries.Sort((a, b) => b.Value.Used.CompareTo(a.Value.Used));
                    if (entries.Count > MaxEntries) entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
                    var file = FilePath;
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    var tmp = file + ".tmp";
                    using (var w = new BinaryWriter(new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16)))
                    {
                        w.Write(Magic); w.Write(FormatVersion); w.Write(entries.Count);
                        foreach (var kv in entries)
                        {
                            var l = kv.Value;
                            w.Write(kv.Key); w.Write(l.Description ?? ""); w.Write(l.Company ?? ""); w.Write(l.Stamp);
                            w.Write(l.Width); w.Write(l.Height);
                            w.Write(l.Pixels?.Length ?? 0); if (l.Pixels != null) w.Write(l.Pixels);
                        }
                    }
                    if (File.Exists(file)) File.Replace(tmp, file, null); else File.Move(tmp, file);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    ErrorLog.Write("Look cache save", ex);
                }
            }
        }

        // ------------------------------------------------------------------ icons <-> pixels
        [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);
        [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bi, uint usage);
        [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int size, out BITMAP bm);
        [DllImport("gdi32.dll")] static extern IntPtr CreateBitmap(int w, int h, int planes, int bpp, byte[] bits);

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public short bmPlanes, bmBitsPixel; public IntPtr bmBits; }

        static bool ReadIcon(IntPtr icon, out byte[] px, out int w, out int h)
        {
            px = null; w = h = 0;
            if (!GetIconInfo(icon, out var ii)) return false;
            IntPtr dc = Native.CreateCompatibleDC(IntPtr.Zero);
            try
            {
                if (ii.hbmColor == IntPtr.Zero || GetObject(ii.hbmColor, Marshal.SizeOf(typeof(BITMAP)), out var bm) == 0) return false;   // monochrome icon: not cached
                w = bm.bmWidth; h = bm.bmHeight;
                if (w <= 0 || h <= 0 || w > 256 || h > 256) return false;
                var bi = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
                px = new byte[w * h * 4];
                if (GetDIBits(dc, ii.hbmColor, 0, (uint)h, px, ref bi, 0) != h) { px = null; return false; }
                bool alpha = false;
                for (int i = 3; i < px.Length; i += 4) if (px[i] != 0) { alpha = true; break; }
                if (!alpha)
                {
                    // old-style icon: transparency is in the mask (white = transparent)
                    var mask = new byte[w * h * 4];
                    var mbi = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
                    bool haveMask = ii.hbmMask != IntPtr.Zero && GetDIBits(dc, ii.hbmMask, 0, (uint)h, mask, ref mbi, 0) == h;
                    for (int i = 0; i < px.Length; i += 4) px[i + 3] = haveMask && mask[i] != 0 ? (byte)0 : (byte)255;
                }
                return true;
            }
            finally
            {
                Native.DeleteDC(dc);
                if (ii.hbmColor != IntPtr.Zero) Native.DeleteObject(ii.hbmColor);
                if (ii.hbmMask != IntPtr.Zero) Native.DeleteObject(ii.hbmMask);
            }
        }

        static IntPtr CreateIcon(byte[] px, int w, int h)
        {
            var bi = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            IntPtr dib = Native.CreateDIBSection(IntPtr.Zero, ref bi, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero) return IntPtr.Zero;
            Marshal.Copy(px, 0, bits, px.Length);
            IntPtr mask = CreateBitmap(w, h, 1, 1, new byte[((w + 15) / 16) * 2 * h]);   // all zero: the alpha channel decides
            var ii = new ICONINFO { fIcon = true, hbmColor = dib, hbmMask = mask };
            IntPtr icon = Native.CreateIconIndirect(ref ii);
            Native.DeleteObject(dib); Native.DeleteObject(mask);
            return icon;
        }
    }
}
