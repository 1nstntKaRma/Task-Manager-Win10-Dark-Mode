using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace TaskManagerDark
{
    /// <summary>
    /// Background loader for per-process facts that never change (path, command line, user, version info, icon...).
    /// Several workers share the queue, so a cold start (nothing in the file cache yet) fills the list quickly.
    /// Names and icons seen before come from the <see cref="LookCache"/> first and are re-checked afterwards at a
    /// lower priority. Results are handed back to the UI thread, which assigns <see cref="ProcInfo.Static"/>.
    /// </summary>
    public sealed unsafe class StaticLoader : IDisposable
    {
        struct Work { public ProcInfo P; public ProcStatic Cached; public string LookKey; }

        readonly BlockingCollection<Work> _queue = new BlockingCollection<Work>(), _refresh = new BlockingCollection<Work>();
        readonly BlockingCollection<Work>[] _queues;
        readonly Thread[] _threads;
        readonly ManualResetEvent _go = new ManualResetEvent(false), _initialDone = new ManualResetEvent(false);
        readonly Action<ProcInfo, ProcStatic> _done;
        volatile bool _stop, _initialArmed;
        int _busy;
        public volatile int IconSize = 16;
        static readonly Dictionary<string, string> _sidNames = new Dictionary<string, string>();
        static readonly ConcurrentDictionary<string, Lazy<FileVersionInfo>> _versions = new ConcurrentDictionary<string, Lazy<FileVersionInfo>>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> _appNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly string SystemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        public static string NtoskrnlPath => Path.Combine(SystemRoot, "System32", "ntoskrnl.exe");

        [DllImport("user32.dll")] static extern IntPtr GetDpiAwarenessContextForProcess(IntPtr h);
        [DllImport("user32.dll")] static extern int GetAwarenessFromDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")] static extern bool AreDpiAwarenessContextsEqual(IntPtr a, IntPtr b);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHCreateItemFromParsingName(string path, IntPtr bc, ref Guid iid, out IShellItem item);

        [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            [PreserveSig] int GetDisplayName(int sigdn, out IntPtr name);
        }

        /// <summary>The workers start when <see cref="Begin"/> is called (the window exists and the icon size is known).</summary>
        public StaticLoader(Action<ProcInfo, ProcStatic> done)
        {
            _done = done;
            _queues = new[] { _queue, _refresh };   // new processes first, re-checks when nothing else waits
            _threads = new Thread[Math.Max(1, Math.Min(3, Environment.ProcessorCount))];
            for (int i = 0; i < _threads.Length; i++)
            {
                _threads[i] = new Thread(Run) { IsBackground = true, Name = "StaticLoader" + (i + 1), Priority = ThreadPriority.BelowNormal };
                _threads[i].SetApartmentState(ApartmentState.STA);   // shell items / icons
                _threads[i].Start();
            }
        }

        public void Begin(int iconSize)
        {
            IconSize = iconSize;
            _go.Set();
        }

        public void Enqueue(ProcInfo p) { if (!_stop) _queue.Add(new Work { P = p }); }

        /// <summary>All processes running at startup are queued; <see cref="WaitInitial"/> returns once they are loaded.</summary>
        public void EndInitialBatch()
        {
            _initialArmed = true;
            CheckInitialDone();
        }

        void CheckInitialDone()
        {
            if (_initialArmed && Volatile.Read(ref _busy) == 0 && _queue.Count == 0 && !_initialDone.WaitOne(0))
            {
                StartupLog.Mark("process names and icons loaded");
                _initialDone.Set();
            }
        }

        /// <summary>Waits until the processes that were running at startup all have their static info.</summary>
        public bool WaitInitial(int ms) => ms > 0 && _initialDone.WaitOne(ms);

        void Run()
        {
            _go.WaitOne();
            while (!_stop)
            {
                Work w;
                Interlocked.Increment(ref _busy);
                int which = BlockingCollection<Work>.TryTakeFromAny(_queues, out w, 0);
                if (which < 0)
                {
                    Interlocked.Decrement(ref _busy);
                    CheckInitialDone();
                    which = BlockingCollection<Work>.TryTakeFromAny(_queues, out w, 5000);
                    if (which < 0) { LookCache.SaveIfDirty(); continue; }   // idle: keep what was learned for the next start
                    Interlocked.Increment(ref _busy);
                }
                try
                {
                    if (w.P == null || _stop) break;
                    if (w.P.Exited) continue;
                    if (which == 0) { var s = Load(w.P, out bool recheck, out string key); _done(w.P, s); if (recheck) _refresh.Add(new Work { P = w.P, Cached = s, LookKey = key }); }
                    else Recheck(w);
                }
                catch (Exception ex) { ErrorLog.Write("StaticLoader " + w.P?.Name, ex); }
                finally { Interlocked.Decrement(ref _busy); }
            }
        }

        /// <summary>A name and icon that came from the cache: replaced when the program file changed since.</summary>
        void Recheck(Work w)
        {
            var c = w.Cached;
            var look = LookCache.Get(w.LookKey);
            long stamp = FileStamp(c.ImagePath);
            if (look != null && stamp == look.Stamp) return;
            var s = c.Clone();
            s.Description = null; s.Company = null;
            FillLook(w.P, s);
            LookCache.Put(w.LookKey, s.Description, s.Company, stamp, s.IconSmall);
            if (!LookCache.SameLook(look, s.Description, s.Company) || s.IconSmall != c.IconSmall) _done(w.P, s);
        }

        static long FileStamp(string path)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            try { return File.GetLastWriteTimeUtc(path).Ticks; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException) { return 0; }
        }

        /// <summary>
        /// Everything about a process that does not change. Name and icon come from the look cache when the program
        /// was seen before (<paramref name="recheck"/> then asks for a later check against the file).
        /// </summary>
        ProcStatic Load(ProcInfo p, out bool recheck, out string lookKey)
        {
            recheck = false; lookKey = null;
            var s = new ProcStatic { Virtualization = -1, Dep = -1 };
            if (p.Pid == 0)
            {
                s.Description = "Percentage of time the processor is idle"; s.UserName = "SYSTEM"; s.Critical = true; s.Platform = IntPtr.Size == 8 ? 64 : 32;
                s.IconSmall = IconCache.DefaultApp(IconSize); return s;
            }
            if (p.Pid == 4)
            {
                s.ImagePath = NtoskrnlPath; s.UserName = "SYSTEM"; s.Critical = true; s.Platform = IntPtr.Size == 8 ? 64 : 32; s.Elevated = true;
                FillVersion(s); s.IconSmall = IconCache.ForFile(s.ImagePath, IconSize); return s;
            }

            IntPtr h = Sys.OpenProcess(Sys.PROCESS_QUERY_INFORMATION | Sys.PROCESS_VM_READ, false, p.Pid);
            if (h == IntPtr.Zero) h = Sys.OpenProcess(Sys.PROCESS_QUERY_LIMITED_INFORMATION, false, p.Pid);
            if (h == IntPtr.Zero) s.AccessDenied = true;
            else
            {
                lock (p)
                {
                    if (p.Exited) { Sys.CloseHandle(h); return s; }
                    p.Handle = h;
                }
                s.ImagePath = ImagePath(h);
                s.CommandLine = CommandLine(h);
                ReadToken(h, s);
                ReadPlatform(h, s);
                s.PackageFullName = PackageName(h);
                if (s.PackageFullName != null) s.AppUserModelId = AppId(h);
                s.Critical = IsCritical(h, p.Pid);
                s.DpiAwareness = DpiAwareness(h);
                s.StackProtection = StackProtection(h);
                s.PowerThrottling = PowerThrottling(h);
            }
            if (s.ImagePath == null) s.ImagePath = ImagePathById(p.Pid);
            if (s.UserName == null) UserFromWinStation(p, s);
            if (s.ImagePath == null)
            {
                // Secure System, Registry, Memory Compression and protected processes without a readable path
                if (p.Name.Equals("Registry", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("Memory Compression", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("Secure System", StringComparison.OrdinalIgnoreCase))
                { s.UserName = "SYSTEM"; s.Critical = true; }
                else if (p.Name.Contains("."))
                {
                    var guess = Path.Combine(SystemRoot, "System32", p.Name);
                    if (File.Exists(guess)) s.ImagePath = guess;
                }
            }
            if (s.AppUserModelId != null) s.IsImmersive = true;
            bool app = UsesAppLook(p, s);
            lookKey = app ? LookCache.KeyForApp(s.AppUserModelId, s.PackageFullName, IconSize) : LookCache.KeyForFile(s.ImagePath, IconSize);
            var look = LookCache.Get(lookKey);
            if (look != null)
            {
                s.Description = look.Description; s.Company = look.Company;
                s.IconSmall = LookCache.IconOf(look);
                if (s.IconSmall == IntPtr.Zero) s.IconSmall = IconCache.DefaultApp(IconSize);
                recheck = !app;   // a packaged app's key holds its version: nothing to re-check
                return s;
            }
            FillLook(p, s);
            if (lookKey != null) LookCache.Put(lookKey, s.Description, s.Company, app ? 0 : FileStamp(s.ImagePath), s.IconSmall);
            return s;
        }

        static bool UsesAppLook(ProcInfo p, ProcStatic s) =>
            s.AppUserModelId != null && !p.Name.Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);

        /// <summary>Description, publisher and small icon, read from the program file (or the app package).</summary>
        void FillLook(ProcInfo p, ProcStatic s)
        {
            FillVersion(s);
            if (s.AppUserModelId != null)
            {
                var n = AppDisplayName(s.AppUserModelId);
                if (!string.IsNullOrEmpty(n)) s.Description = n;
            }
            if (UsesAppLook(p, s))
            {
                var ic = IconCache.ForShellItem("shell:AppsFolder\\" + s.AppUserModelId, IconSize);
                s.IconSmall = ic != IntPtr.Zero ? ic : IconCache.ForFile(s.ImagePath, IconSize);
            }
            else s.IconSmall = s.ImagePath != null ? IconCache.ForFile(s.ImagePath, IconSize) : IconCache.DefaultApp(IconSize);
        }

        static string ImagePath(IntPtr h)
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return Sys.QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString(0, size) : null;
        }

        /// <summary>Image path of any process, without opening it (SystemProcessIdInformation), as a DOS path.</summary>
        public static string ImagePathById(int pid)
        {
            const int max = 1024;
            IntPtr info = Marshal.AllocHGlobal(24), name = Marshal.AllocHGlobal(max * 2);
            try
            {
                byte* b = (byte*)info;
                *(long*)b = pid; *(ushort*)(b + 8) = 0; *(ushort*)(b + 10) = (ushort)(max * 2); *(int*)(b + 12) = 0; *(IntPtr*)(b + 16) = name;
                if (Sys.NtQuerySystemInformation(88 /*SystemProcessIdInformation*/, info, 24, out _) != 0) return null;
                int len = *(ushort*)(b + 8) / 2;
                if (len <= 0) return null;
                return DosPath(new string((char*)name, 0, len));
            }
            finally { Marshal.FreeHGlobal(info); Marshal.FreeHGlobal(name); }
        }

        static Dictionary<string, string> _devices;
        /// <summary>NT device path ("\Device\HarddiskVolume3\Windows\x.exe") to a DOS path ("C:\Windows\x.exe").</summary>
        public static string DosPath(string nt)
        {
            if (nt == null || !nt.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase)) return nt;
            if (_devices == null)
            {
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var buf = new char[1024];
                foreach (var drive in Environment.GetLogicalDrives())
                {
                    var d = drive.Substring(0, 2);
                    int n = Sys.QueryDosDevice(d, buf, buf.Length);
                    if (n <= 0) continue;
                    int z = Array.IndexOf(buf, '\0');
                    var dev = new string(buf, 0, z >= 0 ? z : n);
                    if (!map.ContainsKey(dev)) map[dev] = d;
                }
                _devices = map;
            }
            foreach (var kv in _devices)
                if (nt.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase) && nt.Length > kv.Key.Length && nt[kv.Key.Length] == '\\')
                    return kv.Value + nt.Substring(kv.Key.Length);
            return nt;
        }

        public static string CommandLine(IntPtr h)
        {
            int len = 4096;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                IntPtr buf = Marshal.AllocHGlobal(len);
                try
                {
                    int st = Sys.NtQueryInformationProcess(h, Sys.ProcessCommandLineInformation, buf, len, out int ret);
                    if (st == Sys.STATUS_INFO_LENGTH_MISMATCH || st == unchecked((int)0xC0000023) || st == unchecked((int)0x80000005)) { len = Math.Max(ret, len * 2); continue; }
                    if (st != 0) return null;
                    ushort l = *(ushort*)buf;
                    char* p = *(char**)((byte*)buf + 8);
                    return l > 0 && p != null ? new string(p, 0, l / 2) : "";
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            return null;
        }

        static void ReadToken(IntPtr h, ProcStatic s)
        {
            if (!Sys.OpenProcessToken(h, Sys.TOKEN_QUERY, out var tok)) return;
            try
            {
                IntPtr buf = Marshal.AllocHGlobal(512);
                try
                {
                    if (Sys.GetTokenInformation(tok, Sys.TokenUser, buf, 512, out _))
                    {
                        IntPtr sid = *(IntPtr*)buf;
                        if (Sys.ConvertSidToStringSid(sid, out var str)) { s.UserSid = Marshal.PtrToStringUni(str); Sys.LocalFree(str); }
                        s.UserName = SidName(sid, s.UserSid, out s.UserDomain);
                    }
                    if (Sys.GetTokenInformation(tok, Sys.TokenIntegrityLevel, buf, 512, out _))
                    {
                        IntPtr sid = *(IntPtr*)buf;
                        int n = *(byte*)Sys.GetSidSubAuthorityCount(sid);
                        int rid = *(int*)Sys.GetSidSubAuthority(sid, n - 1);
                        s.IntegrityLevel = rid >= 0x4000 ? "System" : rid >= 0x3000 ? "High" : rid >= 0x2000 ? "Medium" : rid >= 0x1000 ? "Low" : "Untrusted";
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
                if (Sys.GetTokenInformation(tok, Sys.TokenElevation, out int elev, 4, out _)) s.Elevated = elev != 0;
                if (Sys.GetTokenInformation(tok, Sys.TokenVirtualizationAllowed, out int allowed, 4, out _))
                {
                    if (allowed == 0) s.Virtualization = 0;
                    else if (Sys.GetTokenInformation(tok, Sys.TokenVirtualizationEnabled, out int en, 4, out _)) s.Virtualization = en != 0 ? 2 : 1;
                }
            }
            finally { Sys.CloseHandle(tok); }
        }

        [DllImport("winsta.dll", SetLastError = true)] static extern bool WinStationGetProcessSid(IntPtr server, int pid, long createTime, IntPtr sid, ref int size);

        /// <summary>Owner of a process that cannot be opened (asks the terminal server, like Task Manager).</summary>
        static void UserFromWinStation(ProcInfo p, ProcStatic s)
        {
            try
            {
                int size = 256;
                IntPtr sid = Marshal.AllocHGlobal(size);
                try
                {
                    if (!WinStationGetProcessSid(IntPtr.Zero, p.Pid, p.CreateTime, sid, ref size)) return;
                    if (Sys.ConvertSidToStringSid(sid, out var str)) { s.UserSid = Marshal.PtrToStringUni(str); Sys.LocalFree(str); }
                    s.UserName = SidName(sid, s.UserSid, out s.UserDomain);
                }
                finally { Marshal.FreeHGlobal(sid); }
            }
            catch (Exception ex) { ErrorLog.Write("WinStationGetProcessSid", ex); }
        }

        static string SidName(IntPtr sid, string sidString, out string domain)
        {
            domain = null;
            if (sidString != null) lock (_sidNames) if (_sidNames.TryGetValue(sidString, out var cached)) { int i = cached.IndexOf('\\'); domain = i > 0 ? cached.Substring(0, i) : null; return i >= 0 ? cached.Substring(i + 1) : cached; }
            var name = new StringBuilder(256); var dom = new StringBuilder(256);
            int nl = 256, dl = 256;
            string result = sidString;
            if (Sys.LookupAccountSid(null, sid, name, ref nl, dom, ref dl, out _)) { result = name.ToString(); domain = dom.ToString(); }
            if (sidString != null) lock (_sidNames) _sidNames[sidString] = (domain ?? "") + "\\" + result;
            return result;
        }

        static void ReadPlatform(IntPtr h, ProcStatic s)
        {
            try
            {
                if (Sys.IsWow64Process2(h, out ushort pm, out ushort nm)) { s.IsWow64 = pm != 0; s.Platform = pm == 0 ? (nm == 0x14c ? 32 : 64) : 32; }
                else if (Sys.IsWow64Process(h, out bool wow)) { s.IsWow64 = wow; s.Platform = wow || !Environment.Is64BitOperatingSystem ? 32 : 64; }
            }
            catch (EntryPointNotFoundException)
            {
                if (Sys.IsWow64Process(h, out bool wow)) { s.IsWow64 = wow; s.Platform = wow ? 32 : 64; }
            }
            if (s.Platform == 64) s.Dep = 2;
            else if (Sys.GetProcessDEPPolicy(h, out int flags, out bool perm)) s.Dep = (flags & 1) != 0 ? (perm ? 2 : 1) : 0;
        }

        static string PackageName(IntPtr h)
        {
            try
            {
                int len = 0;
                if (Sys.GetPackageFullName(h, ref len, null) != 122 /*ERROR_INSUFFICIENT_BUFFER*/ || len <= 0) return null;
                var sb = new StringBuilder(len);
                return Sys.GetPackageFullName(h, ref len, sb) == 0 ? sb.ToString() : null;
            }
            catch (EntryPointNotFoundException) { return null; }
        }

        static string AppId(IntPtr h)
        {
            try
            {
                int len = 0;
                if (Sys.GetApplicationUserModelId(h, ref len, null) != 122 || len <= 0) return null;
                var sb = new StringBuilder(len);
                return Sys.GetApplicationUserModelId(h, ref len, sb) == 0 ? sb.ToString() : null;
            }
            catch (EntryPointNotFoundException) { return null; }
        }

        static bool IsCritical(IntPtr h, int pid)
        {
            int st = Sys.NtQueryInformationProcess(h, Sys.ProcessBreakOnTermination, out int v, 4, out _);
            return st == 0 && v != 0;
        }

        static string DpiAwareness(IntPtr h)
        {
            try
            {
                var ctx = GetDpiAwarenessContextForProcess(h);
                if (ctx == IntPtr.Zero) return "Unavailable";
                if (AreDpiAwarenessContextsEqual(ctx, (IntPtr)(-4))) return "Per-Monitor (v2)";
                if (AreDpiAwarenessContextsEqual(ctx, (IntPtr)(-5))) return "Unaware (GDI Scaled)";
                switch (GetAwarenessFromDpiAwarenessContext(ctx))
                {
                    case 0: return "Unaware";
                    case 1: return "System";
                    case 2: return "Per-Monitor";
                }
            }
            catch (EntryPointNotFoundException) { }
            return "Unavailable";
        }

        static string StackProtection(IntPtr h)
        {
            if (!Sys.GetProcessMitigationPolicy(h, 15 /*ProcessUserShadowStackPolicy*/, out long v, 4)) return "N/A";
            int f = (int)v;
            bool en = (f & 1) != 0, audit = (f & 2) != 0, strict = (f & 0x10) != 0;
            if (!en && !audit) return "Disabled";
            if (audit && !en) return strict ? "All modules (audit mode)" : "Compatible modules only (audit mode)";
            return strict ? "All modules" : "Compatible modules only";
        }

        static string PowerThrottling(IntPtr h)
        {
            IntPtr buf = Marshal.AllocHGlobal(12);
            try
            {
                *(int*)buf = 1; *(int*)((byte*)buf + 4) = 0; *(int*)((byte*)buf + 8) = 0;
                if (!Sys.GetProcessInformation(h, 4 /*ProcessPowerThrottling*/, buf, 12)) return "N/A";
                int control = *(int*)((byte*)buf + 4), state = *(int*)((byte*)buf + 8);
                if ((control & 1) == 0) return "Enabled";            // not overridden: the system decides (throttling allowed)
                return (state & 1) != 0 ? "Enabled" : "Disabled";
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        static void FillVersion(ProcStatic s)
        {
            var v = Version(s.ImagePath);
            if (v == null) return;
            var d = v.FileDescription?.Trim();
            if (!string.IsNullOrEmpty(d)) s.Description = d;
            var c = v.CompanyName?.Trim();
            if (!string.IsNullOrEmpty(c)) s.Company = c;
        }

        /// <summary>Version resource of a file, read once per path (shared by all loaders).</summary>
        public static FileVersionInfo Version(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return _versions.GetOrAdd(path, k => new Lazy<FileVersionInfo>(() =>
            {
                try { return File.Exists(k) ? FileVersionInfo.GetVersionInfo(k) : null; }
                catch (Exception ex) { ErrorLog.Write("Version " + k, ex); return null; }
            }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }

        public static string AppDisplayName(string aumid)
        {
            lock (_appNames) if (_appNames.TryGetValue(aumid, out var n)) return n;
            string result = null;
            try
            {
                var iid = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
                if (SHCreateItemFromParsingName("shell:AppsFolder\\" + aumid, IntPtr.Zero, ref iid, out var item) == 0 && item != null)
                {
                    if (item.GetDisplayName(0 /*SIGDN_NORMALDISPLAY*/, out var p) == 0) { result = Marshal.PtrToStringUni(p); Marshal.FreeCoTaskMem(p); }
                    Marshal.ReleaseComObject(item);
                }
            }
            catch (Exception ex) { ErrorLog.Write("AppDisplayName", ex); }
            lock (_appNames) _appNames[aumid] = result;
            return result;
        }

        /// <summary>Called on removal: closes the process handle (the static thread may still be using it, so lock).</summary>
        public static void Release(ProcInfo p)
        {
            lock (p)
            {
                p.Exited = true;
                if (p.Handle != IntPtr.Zero) { Sys.CloseHandle(p.Handle); p.Handle = IntPtr.Zero; }
            }
        }

        public void Dispose()
        {
            _stop = true;
            foreach (var t in _threads) _queue.Add(new Work());   // wakes every worker
            _go.Set();
            LookCache.SaveIfDirty();
        }
    }
}
