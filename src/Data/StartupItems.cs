using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using Microsoft.Win32;

namespace TaskManagerDark
{
    public sealed class StartupItem
    {
        public string Key, Name, Publisher, Command, ImagePath, SourceName;
        public bool IsFolder, Enabled = true;
        public DateTime? DisabledTime;
        public RegistryHive ApprovedHive;
        public string ApprovedKey, ApprovedValue;
        public string Impact = Strings.ImpactNotMeasured;
        public long DiskBytes = -1, CpuMs = -1;
        public IntPtr Icon;
    }

    /// <summary>
    /// Startup programs exactly where Task Manager looks: the Run keys and the Startup folders, with the
    /// enabled/disabled state from ...\Explorer\StartupApproved (12-byte value: 0x02 enabled, 0x03 disabled, then a FILETIME).
    /// </summary>
    public static class StartupItems
    {
        const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
        public static int IconSize = 16;

        public static List<StartupItem> Load()
        {
            var list = new List<StartupItem>();
            AddRun(list, RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "Run");
            AddRun(list, RegistryHive.CurrentUser, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Run32");
            AddRun(list, RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "Run");
            AddRun(list, RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Run32");
            AddFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), RegistryHive.CurrentUser);
            AddFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), RegistryHive.LocalMachine);
            ApplyImpact(list);
            return list;
        }

        static void AddRun(List<StartupItem> list, RegistryHive hive, string path, string approvedSub)
        {
            try
            {
                using (var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                using (var k = root.OpenSubKey(path))
                {
                    if (k == null) return;
                    foreach (var name in k.GetValueNames())
                    {
                        if (string.IsNullOrEmpty(name)) continue;
                        var cmd = k.GetValue(name) as string;
                        if (string.IsNullOrWhiteSpace(cmd)) continue;
                        var it = new StartupItem
                        {
                            Key = hive + "|" + path + "|" + name, SourceName = name, Command = cmd,
                            ApprovedHive = hive, ApprovedKey = Approved + approvedSub, ApprovedValue = name,
                        };
                        it.ImagePath = ExecutableOf(cmd);
                        Finish(it, name);
                        list.Add(it);
                    }
                }
            }
            catch (Exception ex) { ErrorLog.Write("Startup Run " + path, ex); }
        }

        static void AddFolder(List<StartupItem> list, string dir, RegistryHive hive)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                foreach (var f in Directory.GetFiles(dir))
                {
                    var file = Path.GetFileName(f);
                    if (file.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    if ((File.GetAttributes(f) & FileAttributes.Hidden) != 0) continue;
                    var it = new StartupItem
                    {
                        Key = "folder|" + f, SourceName = file, IsFolder = true,
                        ApprovedHive = hive, ApprovedKey = Approved + "StartupFolder", ApprovedValue = file,
                    };
                    if (f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && Shortcut.Resolve(f, out var target, out var args))
                    {
                        it.ImagePath = target;
                        it.Command = "\"" + target + "\"" + (string.IsNullOrEmpty(args) ? "" : " " + args);
                    }
                    else { it.ImagePath = f; it.Command = "\"" + f + "\""; }
                    Finish(it, Path.GetFileNameWithoutExtension(file));
                    list.Add(it);
                }
            }
            catch (Exception ex) { ErrorLog.Write("Startup folder " + dir, ex); }
        }

        static void Finish(StartupItem it, string fallbackName)
        {
            var v = StaticLoader.Version(it.ImagePath);
            var desc = v?.FileDescription?.Trim();
            it.Name = !string.IsNullOrEmpty(desc) ? desc : fallbackName;
            it.Publisher = v?.CompanyName?.Trim() ?? "";
            it.Icon = it.ImagePath != null && File.Exists(it.ImagePath) ? IconCache.ForFile(it.ImagePath, IconSize) : IconCache.DefaultApp(IconSize);
            ReadApproved(it);
        }

        static void ReadApproved(StartupItem it)
        {
            try
            {
                using (var root = RegistryKey.OpenBaseKey(it.ApprovedHive, RegistryView.Registry64))
                using (var k = root.OpenSubKey(it.ApprovedKey))
                {
                    var b = k?.GetValue(it.ApprovedValue) as byte[];
                    if (b == null || b.Length < 1) { it.Enabled = true; return; }
                    it.Enabled = (b[0] & 1) == 0;
                    if (!it.Enabled && b.Length >= 12)
                    {
                        long ft = BitConverter.ToInt64(b, 4);
                        if (ft > 0) try { it.DisabledTime = DateTime.FromFileTime(ft); } catch (ArgumentOutOfRangeException) { /* not a valid time: no date shown */ }
                    }
                }
            }
            catch (Exception ex) { ErrorLog.Write("StartupApproved read", ex); }
        }

        /// <summary>Writes the StartupApproved value the way Task Manager does. Returns a Win32 error code (0 = success).</summary>
        public static int SetEnabled(StartupItem it, bool enable)
        {
            try
            {
                using (var root = RegistryKey.OpenBaseKey(it.ApprovedHive, RegistryView.Registry64))
                using (var k = root.CreateSubKey(it.ApprovedKey))
                {
                    var b = new byte[12];
                    b[0] = (byte)(enable ? 0x02 : 0x03);
                    if (!enable) BitConverter.GetBytes(DateTime.Now.ToFileTime()).CopyTo(b, 4);
                    k.SetValue(it.ApprovedValue, b, RegistryValueKind.Binary);
                }
                it.Enabled = enable;
                it.DisabledTime = enable ? (DateTime?)null : DateTime.Now;
                return 0;
            }
            catch (UnauthorizedAccessException) { return 5; }
            catch (System.Security.SecurityException) { return 5; }
            catch (Exception ex) { ErrorLog.Write("StartupApproved write", ex); return 1; }
        }

        /// <summary>The program a command line starts: quoted path, the longest existing prefix, or a PATH lookup.</summary>
        public static string ExecutableOf(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return null;
            cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
            string candidate;
            if (cmd.StartsWith("\""))
            {
                int q = cmd.IndexOf('"', 1);
                candidate = q > 0 ? cmd.Substring(1, q - 1) : cmd.Trim('"');
                return Resolve(candidate);
            }
            // unquoted: try growing prefixes at each space ("C:\Program Files\X\x.exe /arg")
            int idx = 0;
            while (true)
            {
                int sp = cmd.IndexOf(' ', idx);
                candidate = sp < 0 ? cmd : cmd.Substring(0, sp);
                var r = Resolve(candidate);
                if (r != null && File.Exists(r)) return r;
                if (sp < 0) break;
                idx = sp + 1;
            }
            int first = cmd.IndexOf(' ');
            return Resolve(first > 0 ? cmd.Substring(0, first) : cmd);
        }

        static string Resolve(string p)
        {
            if (string.IsNullOrEmpty(p)) return null;
            try
            {
                if (File.Exists(p)) return Path.GetFullPath(p);
                if (!p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(p + ".exe")) return Path.GetFullPath(p + ".exe");
                if (p.IndexOf('\\') < 0)
                {
                    var sb = new StringBuilder(p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p : p + ".exe", 1024);
                    if (Sys.PathFindOnPath(sb, IntPtr.Zero)) return sb.ToString();
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException || ex is System.Security.SecurityException)
            {
                // part of a command line that is not a usable path: keep the text as it is
            }
            return p;
        }

        // ------------------------------------------------------------ startup impact (WDI StartupInfo, admin only)
        public static bool ImpactReadable;
        static volatile Tuple<string, Dictionary<string, long[]>> _startupInfo;   // last parsed file (name|time) and its data

        static void ApplyImpact(List<StartupItem> list)
        {
            var data = ReadStartupInfo();
            ImpactReadable = data != null;
            foreach (var it in list)
            {
                it.Impact = it.Enabled ? Strings.ImpactNotMeasured : Strings.ImpactNone;
                if (data == null || it.ImagePath == null) continue;
                if (data.TryGetValue(it.ImagePath, out var v))
                {
                    it.CpuMs = v[0] / 1000; it.DiskBytes = v[1];
                    if (!it.Enabled) { it.Impact = Strings.ImpactNone; continue; }
                    // Microsoft's published thresholds: High > 1 s CPU or > 3 MB disk; Medium 300 ms-1 s or 300 KB-3 MB; Low below
                    it.Impact = it.CpuMs > 1000 || it.DiskBytes > 3 * 1024 * 1024 ? Strings.ImpactHigh
                              : it.CpuMs >= 300 || it.DiskBytes >= 300 * 1024 ? Strings.ImpactMedium : Strings.ImpactLow;
                }
            }
        }

        /// <summary>image path -> [cpu microseconds, disk bytes] from the newest StartupInfo file of the current user.</summary>
        static Dictionary<string, long[]> ReadStartupInfo()
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"wdi\LogFiles\StartupInfo");
                if (!Directory.Exists(dir)) return null;
                string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
                string[] files;
                try { files = Directory.GetFiles(dir, (sid ?? "*") + "_StartupInfo*.xml"); }
                catch (UnauthorizedAccessException) { return null; }
                if (files.Length == 0) return new Dictionary<string, long[]>(StringComparer.OrdinalIgnoreCase);
                Array.Sort(files, (a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
                var stamp = files[0] + "|" + File.GetLastWriteTimeUtc(files[0]).Ticks;
                var cached = _startupInfo;
                if (cached != null && cached.Item1 == stamp) return cached.Item2;
                var result = new Dictionary<string, long[]>(StringComparer.OrdinalIgnoreCase);
                var doc = new XmlDocument();
                doc.Load(files[0]);
                foreach (XmlElement proc in doc.GetElementsByTagName("Process"))
                {
                    string name = proc.GetAttribute("Name");
                    if (string.IsNullOrEmpty(name)) name = (proc["Name"] ?? proc["ImageName"])?.InnerText;
                    if (string.IsNullOrEmpty(name)) continue;
                    long cpu = Units(proc["CpuUsage"], true), disk = Units(proc["DiskUsage"], false);
                    if (!result.TryGetValue(name, out var v)) { v = new long[2]; result[name] = v; }
                    v[0] += Math.Max(0, cpu); v[1] += Math.Max(0, disk);
                }
                _startupInfo = Tuple.Create(stamp, result);
                return result;
            }
            catch (UnauthorizedAccessException) { return null; }
            catch (Exception ex) { ErrorLog.Write("StartupInfo", ex); return null; }
        }

        static long Units(XmlElement e, bool cpu)
        {
            if (e == null || !long.TryParse(e.InnerText.Trim(), out long v)) return -1;
            var unit = e.GetAttribute("Unit").ToLowerInvariant();
            if (cpu) return unit == "ms" ? v * 1000 : unit == "s" ? v * 1000000 : v;               // microseconds
            return unit == "kb" ? v * 1024 : unit == "mb" ? v * 1048576 : v;                       // bytes
        }

        public static double? LastBiosSeconds()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power"))
                {
                    var v = k?.GetValue("FwPOSTTime");
                    if (v is int ms && ms > 0) return ms / 1000.0;
                }
            }
            catch (Exception ex) { ErrorLog.Write("FwPOSTTime", ex); }
            return null;
        }
    }

    /// <summary>.lnk resolution through IShellLinkW.</summary>
    public static class Shortcut
    {
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")] class ShellLink { }
        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr fd, int flags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
        }
        [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPersistFile
        {
            void GetClassID(out Guid id);
            [PreserveSig] int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string file, int mode);
        }

        public static bool Resolve(string lnk, out string target, out string args)
        {
            target = args = null;
            object o = null;
            try
            {
                o = new ShellLink();
                ((IPersistFile)o).Load(lnk, 0);
                var sl = (IShellLinkW)o;
                var sb = new StringBuilder(1024);
                sl.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
                target = sb.ToString();
                sb.Clear();
                sl.GetArguments(sb, sb.Capacity);
                args = sb.ToString();
                return !string.IsNullOrEmpty(target);
            }
            catch (Exception ex) { ErrorLog.Write("Shortcut " + lnk, ex); return false; }
            finally { if (o != null) Marshal.ReleaseComObject(o); }
        }
    }
}
