using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace TaskManagerDark
{
    public sealed class DiskSample
    {
        public int Number;
        public DiskStatic Static;
        public double Active, ReadBps, WriteBps, ResponseMs;
    }

    /// <summary>Facts about a physical disk. Filled in on a background thread; never blocks sampling.</summary>
    public sealed class DiskStatic
    {
        public int Number;
        public volatile string Letters = "";
        public volatile string Model = "";
        public volatile string Type = "";
        public long Capacity, Formatted = -1;
        public bool SystemDisk, PageFile, FactsLoaded, FormattedRequested;
        public string Title { get { var l = Letters; return l.Length > 0 ? "Disk " + Number + " (" + l + ")" : "Disk " + Number; } }
    }

    /// <summary>
    /// Physical disk activity straight from the disk driver's counters (IOCTL_DISK_PERFORMANCE, the same numbers the
    /// PhysicalDisk performance counters are built from). Opening a disk device and reading its counters never touches
    /// the media, so sleeping hard disks stay asleep and nothing here can stall the update loop.
    /// </summary>
    public sealed unsafe class DiskSampler : IDisposable
    {
        const uint IOCTL_DISK_PERFORMANCE = 0x70020, IOCTL_STORAGE_QUERY_PROPERTY = 0x2D1400, IOCTL_DISK_GET_DRIVE_GEOMETRY_EX = 0x700A0, IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x560000;

        sealed class Drive
        {
            public int Number; public IntPtr Handle; public DiskStatic Static;
            public long Read, Written, ReadTime, WriteTime, Idle, Query; public uint ReadCount, WriteCount;
            public bool HasPrev;
        }

        readonly Dictionary<int, Drive> _drives = new Dictionary<int, Drive>();
        readonly IntPtr _buf = Marshal.AllocHGlobal(256);
        int _ticks;
        /// <summary>Test switch: slows down the facts loading (model, letters...) to prove sampling does not wait for it.</summary>
        public static int DebugFactsDelayMs;

        public DiskSampler() { Enumerate(); }

        /// <summary>Opens \\.\PhysicalDriveN for every present disk (no access rights needed, no media access).</summary>
        void Enumerate()
        {
            int misses = 0;
            for (int n = 0; n < 64 && misses < 8; n++)
            {
                if (_drives.ContainsKey(n)) { misses = 0; continue; }
                IntPtr h = Sys.CreateFile(@"\\.\PhysicalDrive" + n, 0, Sys.FILE_SHARE_READ | Sys.FILE_SHARE_WRITE, IntPtr.Zero, Sys.OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == Sys.INVALID_HANDLE_VALUE) { misses++; continue; }
                misses = 0;
                var st = new DiskStatic { Number = n };
                _drives[n] = new Drive { Number = n, Handle = h, Static = st };
                // model, type, capacity and drive letters come from in-memory driver data: quick, but kept off this thread anyway
                ThreadPool.QueueUserWorkItem(_ => { try { LoadFacts(st); } catch (Exception ex) { ErrorLog.Write("Disk facts " + n, ex); } });
            }
        }

        public List<DiskSample> Sample()
        {
            if (++_ticks % 20 == 0) Enumerate();   // new USB disks
            var list = new List<DiskSample>(_drives.Count);
            List<int> gone = null;
            foreach (var d in _drives.Values)
            {
                if (!Sys.DeviceIoControl(d.Handle, IOCTL_DISK_PERFORMANCE, IntPtr.Zero, 0, _buf, 256, out int got, IntPtr.Zero) || got < 64)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == 2 || err == 21 || err == 1167 || err == 6) (gone ?? (gone = new List<int>())).Add(d.Number);   // removed / not ready
                    continue;
                }
                byte* b = (byte*)_buf;
                long read = *(long*)b, written = *(long*)(b + 8), rt = *(long*)(b + 16), wt = *(long*)(b + 24), idle = *(long*)(b + 32), query = *(long*)(b + 56);
                uint rc = *(uint*)(b + 40), wc = *(uint*)(b + 44);
                var s = new DiskSample { Number = d.Number, Static = d.Static };
                if (d.HasPrev && query > d.Query)
                {
                    double dt = query - d.Query;                                  // 100 ns
                    double di = Math.Max(0, idle - d.Idle);
                    s.Active = Math.Max(0, Math.Min(100, 100.0 - di * 100.0 / dt));
                    s.ReadBps = Math.Max(0, (read - d.Read) * 1e7 / dt);
                    s.WriteBps = Math.Max(0, (written - d.Written) * 1e7 / dt);
                    long ios = (long)(rc - d.ReadCount) + (wc - d.WriteCount);
                    s.ResponseMs = ios > 0 ? Math.Max(0, (rt - d.ReadTime) + (wt - d.WriteTime)) / (double)ios / 10000.0 : 0;
                }
                d.Read = read; d.Written = written; d.ReadTime = rt; d.WriteTime = wt; d.Idle = idle; d.Query = query; d.ReadCount = rc; d.WriteCount = wc; d.HasPrev = true;
                list.Add(s);
            }
            if (gone != null) foreach (var n in gone) { Sys.CloseHandle(_drives[n].Handle); _drives.Remove(n); }
            list.Sort((a, b) => a.Number.CompareTo(b.Number));
            return list;
        }

        // ---------------------------------------------------------------- facts (background)
        static void LoadFacts(DiskStatic s)
        {
            if (DebugFactsDelayMs > 0) Thread.Sleep(DebugFactsDelayMs);
            IntPtr h = Sys.CreateFile(@"\\.\PhysicalDrive" + s.Number, 0, Sys.FILE_SHARE_READ | Sys.FILE_SHARE_WRITE, IntPtr.Zero, Sys.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h != Sys.INVALID_HANDLE_VALUE)
            {
                IntPtr q = Marshal.AllocHGlobal(12), outBuf = Marshal.AllocHGlobal(4096);
                try
                {
                    *(int*)q = 0; *(int*)((byte*)q + 4) = 0; *(int*)((byte*)q + 8) = 0;
                    bool removable = false; int bus = 0;
                    if (Sys.DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, q, 12, outBuf, 4096, out _, IntPtr.Zero))
                    {
                        byte* b = (byte*)outBuf;
                        removable = b[10] != 0;
                        int vendorOff = *(int*)(b + 12), productOff = *(int*)(b + 16);
                        bus = *(int*)(b + 28);
                        string vendor = vendorOff > 0 ? Ansi(b + vendorOff) : "", product = productOff > 0 ? Ansi(b + productOff) : "";
                        s.Model = (product.StartsWith(vendor) || vendor.Length == 0 ? product : vendor + " " + product).Trim();
                    }
                    *(int*)q = 7;   // StorageDeviceSeekPenaltyProperty: SSD vs HDD
                    int type = -1;
                    if (Sys.DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, q, 12, outBuf, 4096, out int got, IntPtr.Zero) && got >= 9)
                        type = ((byte*)outBuf)[8] != 0 ? 1 : 0;
                    s.Type = removable ? "Removable" : bus == 7 /*USB*/ && type != 0 ? "Removable" : bus == 12 ? "SD" : type == 0 ? "SSD" : type == 1 ? "HDD" : "Unknown";
                    if (Sys.DeviceIoControl(h, IOCTL_DISK_GET_DRIVE_GEOMETRY_EX, IntPtr.Zero, 0, outBuf, 4096, out _, IntPtr.Zero))
                        s.Capacity = *(long*)((byte*)outBuf + 24);
                }
                finally { Marshal.FreeHGlobal(q); Marshal.FreeHGlobal(outBuf); Sys.CloseHandle(h); }
            }
            // drive letters: the volume manager answers from memory (no file system access)
            string sysRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)).Substring(0, 2).ToUpperInvariant();
            var pageFileDrives = PageFileDrives();
            var letters = new List<string>();
            foreach (var drive in Environment.GetLogicalDrives())
            {
                string root = drive.Substring(0, 2);
                if (!VolumeOnDisk(root, s.Number)) continue;
                letters.Add(root);
                if (root.Equals(sysRoot, StringComparison.OrdinalIgnoreCase)) s.SystemDisk = true;
                if (pageFileDrives.Contains(root.ToUpperInvariant())) s.PageFile = true;
            }
            s.Letters = string.Join(" ", letters);
            if (s.Model.Length == 0) s.Model = "Disk " + s.Number;
            s.FactsLoaded = true;
        }

        /// <summary>
        /// "Formatted": the file systems' sizes. Asking a file system may spin up a sleeping hard disk, so this runs
        /// only when that disk's page is shown, in the background.
        /// </summary>
        public static void RequestFormatted(DiskStatic s, Action done)
        {
            if (s.FormattedRequested) return;
            s.FormattedRequested = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                long total = 0;
                try
                {
                    foreach (var root in (s.Letters ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                        if (Sys.GetDiskFreeSpaceEx(root + "\\", out ulong _, out ulong t, out ulong _)) total += (long)t;
                }
                catch (Exception ex) { ErrorLog.Write("Disk formatted " + s.Number, ex); }
                s.Formatted = total;
                done?.Invoke();
            });
        }

        static string Ansi(byte* p)
        {
            int n = 0; while (p[n] != 0 && n < 256) n++;
            return Encoding.ASCII.GetString(p, n).Trim();
        }

        static bool VolumeOnDisk(string root, int disk)
        {
            IntPtr h = Sys.CreateFile(@"\\.\" + root, 0, Sys.FILE_SHARE_READ | Sys.FILE_SHARE_WRITE, IntPtr.Zero, Sys.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == Sys.INVALID_HANDLE_VALUE) return false;
            IntPtr buf = Marshal.AllocHGlobal(1024);
            try
            {
                if (!Sys.DeviceIoControl(h, IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, IntPtr.Zero, 0, buf, 1024, out _, IntPtr.Zero)) return false;
                int n = *(int*)buf;
                for (int i = 0; i < n; i++) if (*(int*)((byte*)buf + 8 + i * 24) == disk) return true;
                return false;
            }
            finally { Marshal.FreeHGlobal(buf); Sys.CloseHandle(h); }
        }

        static HashSet<string> PageFileDrives()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management"))
                {
                    if (k?.GetValue("ExistingPageFiles") is string[] files)
                        foreach (var f in files)
                        {
                            var s = f.StartsWith(@"\??\") ? f.Substring(4) : f;
                            if (s.Length >= 2 && s[1] == ':') set.Add(s.Substring(0, 2));
                        }
                }
            }
            catch (Exception ex) { ErrorLog.Write("PageFileDrives", ex); }
            return set;
        }

        public void Dispose()
        {
            foreach (var d in _drives.Values) Sys.CloseHandle(d.Handle);
            _drives.Clear();
            Marshal.FreeHGlobal(_buf);
        }
    }
}
