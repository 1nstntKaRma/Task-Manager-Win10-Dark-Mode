using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;

namespace TaskManagerDark
{
    /// <summary>
    /// HICONs cached per (path, size). Thread safe, and each icon is extracted once even when several loaders ask
    /// for it at the same time (dozens of svchost.exe processes share one file). Icons live for the whole session.
    /// </summary>
    public static class IconCache
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHDefExtractIconW")]
        static extern int SHDefExtractIcon(string file, int index, int flags, out IntPtr large, out IntPtr small, int sizes);
        [DllImport("shell32.dll")] static extern int SHGetStockIconInfo(int id, int flags, ref STOCKICONINFO info);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHCreateItemFromParsingName(string path, IntPtr bc, ref Guid iid, out IntPtr item);
        [DllImport("user32.dll")] static extern IntPtr CreateIconIndirect(ref ICONINFO ii);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
        [DllImport("gdi32.dll")] static extern IntPtr CreateBitmap(int w, int h, int planes, int bpp, IntPtr bits);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct STOCKICONINFO { public int cbSize; public IntPtr hIcon; public int iSysImageIndex, iIcon; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szPath; }

        static readonly ConcurrentDictionary<string, Lazy<IntPtr>> _cache = new ConcurrentDictionary<string, Lazy<IntPtr>>(StringComparer.OrdinalIgnoreCase);

        static IntPtr Once(string key, Func<IntPtr> load) =>
            _cache.GetOrAdd(key, k => new Lazy<IntPtr>(load, LazyThreadSafetyMode.ExecutionAndPublication)).Value;

        public static IntPtr ForFile(string path, int size)
        {
            if (string.IsNullOrEmpty(path)) return DefaultApp(size);
            return Once(size + "|" + path, () => LoadFile(path, size));
        }

        static IntPtr LoadFile(string path, int size)
        {
            IntPtr icon = IntPtr.Zero;
            try
            {
                if (SHDefExtractIcon(path, 0, 0, out var large, out var small, size | (size << 16)) == 0)
                {
                    icon = large;
                    if (small != IntPtr.Zero && small != large) Sys.DestroyIcon(small);
                }
            }
            catch (Exception ex) { ErrorLog.Write("IconCache.ForFile", ex); }
            if (icon == IntPtr.Zero) icon = DefaultApp(size);
            return icon;
        }

        /// <summary>Icon of a shell item such as "shell:AppsFolder\&lt;AUMID&gt;" (UWP apps).</summary>
        public static IntPtr ForShellItem(string parsingName, int size) => Once("shell|" + size + "|" + parsingName, () => LoadShellItem(parsingName, size));

        static IntPtr LoadShellItem(string parsingName, int size)
        {
            IntPtr icon = IntPtr.Zero;
            try
            {
                var iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"); // IShellItemImageFactory
                if (SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out var factory) == 0 && factory != IntPtr.Zero)
                {
                    try
                    {
                        var f = (IShellItemImageFactory)Marshal.GetObjectForIUnknown(factory);
                        if (f.GetImage(new SIZE { cx = size, cy = size }, 0x0 /*SIIGBF_RESIZETOFIT*/, out var hbmp) == 0 && hbmp != IntPtr.Zero)
                        {
                            var mask = CreateBitmap(size, size, 1, 1, IntPtr.Zero);
                            var ii = new ICONINFO { fIcon = true, hbmColor = hbmp, hbmMask = mask };
                            icon = CreateIconIndirect(ref ii);
                            DeleteObject(mask); DeleteObject(hbmp);
                        }
                        Marshal.ReleaseComObject(f);
                    }
                    finally { Marshal.Release(factory); }
                }
            }
            catch (Exception ex) { ErrorLog.Write("IconCache.ForShellItem", ex); }
            return icon;
        }

        public static IntPtr Stock(int siid, int size) => Once("stock|" + siid + "|" + size, () => LoadStock(siid, size));

        static IntPtr LoadStock(int siid, int size)
        {
            IntPtr icon = IntPtr.Zero;
            try
            {
                var info = new STOCKICONINFO { cbSize = Marshal.SizeOf(typeof(STOCKICONINFO)) };
                if (SHGetStockIconInfo(siid, 0, ref info) == 0 && !string.IsNullOrEmpty(info.szPath))
                {
                    if (SHDefExtractIcon(info.szPath, info.iIcon, 0, out var large, out var small, size | (size << 16)) == 0)
                    {
                        icon = large;
                        if (small != IntPtr.Zero && small != large) Sys.DestroyIcon(small);
                    }
                }
            }
            catch (Exception ex) { ErrorLog.Write("IconCache.Stock", ex); }
            return icon;
        }

        public static IntPtr DefaultApp(int size) => Stock(2 /*SIID_APPLICATION*/, size);

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm); }
    }
}
