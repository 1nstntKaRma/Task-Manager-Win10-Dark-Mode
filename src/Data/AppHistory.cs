using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;

namespace TaskManagerDark
{
    public sealed class AppUsage
    {
        public string Key, Name, Path, Aumid, PackageFullName, Service;
        public int Kind;                      // 0 desktop program, 1 service, 2 Store app, 3 grouped (uninstalled / remote / system)
        public long Cycles, Network, Metered, NonMetered, Tile, Downloads, Uploads;
        public IntPtr Icon;
        public double CpuSeconds(double mhz) => mhz > 0 ? Cycles / (mhz * 1e6) : 0;
    }

    /// <summary>
    /// App history from the System Resource Usage Monitor through srumapi.dll, the same source Task Manager uses:
    ///   SruQueryStats(DWORD stat, SYSTEMTIME* fromUtc, SYSTEMTIME* toUtc, DWORD flags = 1, RECORDSET** out)
    ///   stat 0 = network (fields: 2 metered flag, 3 bytes sent, 4 bytes received)
    ///   stat 1 = CPU (fields: 0 foreground cycles, 1 background cycles)
    ///   stat 2 = notifications / tile updates (fields: 0 type, 1 bytes, 2 metered flag)
    /// Record (64 bytes): +0x10 kind (0 exe path, 1 service, 2 package), +0x18 app id, +0x28 user SID, +0x30 field count, +0x38 fields (24 bytes: WORD id, DWORD type, value).
    /// The layout was taken from how Taskmgr.exe and srumapi.dll use it; these are undocumented Windows APIs.
    /// </summary>
    public static unsafe class AppHistory
    {
        [StructLayout(LayoutKind.Sequential)] struct SYSTEMTIME { public ushort y, mo, dow, d, h, mi, s, ms; }
        [DllImport("srumapi.dll")] static extern int SruQueryStats(int id, ref SYSTEMTIME from, ref SYSTEMTIME to, int flags, out IntPtr rs);
        [DllImport("srumapi.dll")] static extern void SruFreeRecordSet(IntPtr rs);
        [DllImport("kernel32.dll")] static extern bool FileTimeToSystemTime(ref long ft, out SYSTEMTIME st);
        [DllImport("advapi32.dll")] static extern bool IsValidSid(IntPtr sid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetPackagePathByFullName(string fullName, ref int len, StringBuilder path);

        public const long Day = 864000000000L;          // 100 ns units
        public const long Window = 30 * Day;           // usage older than 30 days is retired, a day at a time
        public static string Error;

        /// <summary>The start of the reported period (UTC FILETIME), kept within the last 30 days.</summary>
        public static long Since
        {
            get
            {
                long now = DateTime.UtcNow.ToFileTimeUtc();
                long since = Settings.GetLong("AppHistory.Since", 0);
                if (since <= 0 || since > now) since = now - Window;
                if (now - since > Window) { since = now - Window; since = since - since % Day + Day; }
                return since;
            }
        }

        /// <summary>"Delete usage history": the period restarts now.</summary>
        public static void Reset() { Settings.SetLong("AppHistory.Since", DateTime.UtcNow.ToFileTimeUtc()); Settings.Save(); }

        public static List<AppUsage> Load(bool allProcesses, int iconSize, Func<string, string> serviceName)
        {
            Error = null;
            var map = new Dictionary<string, AppUsage>(StringComparer.OrdinalIgnoreCase);
            long since = Since, now = DateTime.UtcNow.ToFileTimeUtc();
            if (!FileTimeToSystemTime(ref since, out var from) || !FileTimeToSystemTime(ref now, out var to)) return new List<AppUsage>();
            string me = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
            for (int stat = 0; stat < 3; stat++)
            {
                IntPtr rs;
                int r;
                try { r = SruQueryStats(stat, ref from, ref to, 1, out rs); }
                catch (DllNotFoundException) { Error = "srumapi.dll not found"; break; }
                catch (EntryPointNotFoundException) { Error = "SruQueryStats not found"; break; }
                if (r != 0) { Error = Sys.Win32Message(r); continue; }
                if (rs == IntPtr.Zero) continue;
                try
                {
                    int n = *(int*)rs;
                    byte* recs = *(byte**)((byte*)rs + 8);
                    for (int i = 0; i < n; i++)
                    {
                        byte* rec = recs + (long)i * 64;
                        int kind = *(int*)(rec + 0x10);
                        char* idp = *(char**)(rec + 0x18);
                        if (idp == null) continue;
                        IntPtr sid = *(IntPtr*)(rec + 0x28);
                        string sidStr = SidString(sid);
                        bool system = sidStr == "S-1-5-18" || sidStr == "S-1-5-19" || sidStr == "S-1-5-20";
                        if (sidStr != null && sidStr != me && !(allProcesses && system)) continue;
                        if (!allProcesses && kind != 2) continue;          // by default: Store apps only
                        var app = Get(map, kind, new string(idp));
                        int nf = *(ushort*)(rec + 0x30);
                        byte* f = *(byte**)(rec + 0x38);
                        if (f == null) continue;
                        Accumulate(app, stat, f, nf);
                    }
                }
                finally { SruFreeRecordSet(rs); }
            }
            var list = new List<AppUsage>(map.Values);
            foreach (var a in list) Describe(a, iconSize, serviceName);
            // merge rows that resolve to the same group (uninstalled / remote programs)
            var merged = new Dictionary<string, AppUsage>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in list)
            {
                string k = a.Kind == 3 ? "group:" + a.Name : a.Key;
                if (!merged.TryGetValue(k, out var m)) { merged[k] = a; continue; }
                m.Cycles += a.Cycles; m.Network += a.Network; m.Metered += a.Metered; m.NonMetered += a.NonMetered; m.Tile += a.Tile; m.Downloads += a.Downloads; m.Uploads += a.Uploads;
            }
            return new List<AppUsage>(merged.Values);
        }

        static string SidString(IntPtr sid)
        {
            if (sid == IntPtr.Zero || !IsValidSid(sid)) return null;
            if (!Sys.ConvertSidToStringSid(sid, out var p)) return null;
            try { return Marshal.PtrToStringUni(p); } finally { Sys.LocalFree(p); }
        }

        static AppUsage Get(Dictionary<string, AppUsage> map, int kind, string id)
        {
            string key = kind + "|" + id;
            if (!map.TryGetValue(key, out var a)) { a = new AppUsage { Key = key, Kind = kind }; map[key] = a; if (kind == 0) a.Path = id; else if (kind == 1) a.Service = id; else a.PackageFullName = id; }
            return a;
        }

        static long Add(long a, long b) { long c = a + b; return c < a ? long.MaxValue : c; }

        static void Accumulate(AppUsage a, int stat, byte* f, int nf)
        {
            switch (stat)
            {
                case 0:   // network
                    {
                        bool metered = false; long sent = 0, recv = 0;
                        for (int k = 0; k < nf; k++)
                        {
                            byte* e = f + k * 24; int id = *(ushort*)e;
                            if (id == 2) metered = (*(byte*)(e + 8) & 1) != 0;
                            else if (id == 3) sent = *(long*)(e + 8);
                            else if (id == 4) recv = *(long*)(e + 8);
                        }
                        long total = Add(sent, recv);
                        a.Network = Add(a.Network, total); a.Uploads = Add(a.Uploads, sent); a.Downloads = Add(a.Downloads, recv);
                        if (metered) a.Metered = Add(a.Metered, total); else a.NonMetered = Add(a.NonMetered, total);
                        break;
                    }
                case 1:   // CPU cycles (foreground + background)
                    for (int k = 0; k < nf; k++)
                    {
                        byte* e = f + k * 24; int id = *(ushort*)e;
                        if (id == 0 || id == 1) a.Cycles = Add(a.Cycles, *(long*)(e + 8));
                    }
                    break;
                case 2:   // notifications (tile updates)
                    {
                        int type = 0; long bytes = 0; bool metered = false;
                        for (int k = 0; k < nf; k++)
                        {
                            byte* e = f + k * 24; int id = *(ushort*)e;
                            if (id == 0) type = *(int*)(e + 8);
                            else if (id == 1) bytes = *(int*)(e + 8);
                            else if (id == 2) metered = *(int*)(e + 8) == 1;
                        }
                        if (!(type >= 1 && type <= 3) && type != 0x80) break;
                        a.Tile = Add(a.Tile, bytes); a.Network = Add(a.Network, bytes); a.Downloads = Add(a.Downloads, bytes);
                        if (metered) a.Metered = Add(a.Metered, bytes); else a.NonMetered = Add(a.NonMetered, bytes);
                        break;
                    }
            }
        }

        // ------------------------------------------------------------ naming
        static void Describe(AppUsage a, int iconSize, Func<string, string> serviceName)
        {
            try
            {
                if (a.Kind == 0)
                {
                    string id = a.Path;
                    if (id.StartsWith(@"\device\mup\", StringComparison.OrdinalIgnoreCase) || id.StartsWith(@"\\")) { a.Kind = 3; a.Name = Strings.RemoteProcesses; a.Icon = IconCache.DefaultApp(iconSize); return; }
                    if (id.IndexOf('\\') < 0) { a.Kind = 3; a.Name = id.Equals("System", StringComparison.OrdinalIgnoreCase) ? Strings.SystemName : id; a.Icon = IconCache.DefaultApp(iconSize); return; }
                    var path = StaticLoader.DosPath(PrettyDevice(id));
                    a.Path = path;
                    if (!File.Exists(path)) { a.Kind = 3; a.Name = Strings.UninstalledProcesses; a.Icon = IconCache.DefaultApp(iconSize); return; }
                    var v = StaticLoader.Version(path);
                    var d = v?.FileDescription?.Trim();
                    a.Name = !string.IsNullOrEmpty(d) ? d : Path.GetFileName(path);
                    a.Icon = IconCache.ForFile(path, iconSize);
                }
                else if (a.Kind == 1)
                {
                    a.Name = serviceName?.Invoke(a.Service) ?? a.Service;
                    a.Icon = IconCache.DefaultApp(iconSize);
                }
                else
                {
                    var full = a.PackageFullName;
                    int len = 1024; var sb = new StringBuilder(len);
                    if (GetPackagePathByFullName(full, ref len, sb) != 0 || !Directory.Exists(sb.ToString()))
                    {
                        a.Kind = 3; a.Name = Strings.UninstalledProcesses; a.Icon = IconCache.DefaultApp(iconSize); return;
                    }
                    a.Path = sb.ToString();
                    PackageInfo(a, a.Path, full);
                    a.Icon = a.Aumid != null ? IconCache.ForShellItem("shell:AppsFolder\\" + a.Aumid, iconSize) : IntPtr.Zero;
                    if (a.Icon == IntPtr.Zero) a.Icon = IconCache.DefaultApp(iconSize);
                }
            }
            catch (Exception ex) { ErrorLog.Write("AppHistory.Describe " + a.Key, ex); if (a.Name == null) a.Name = a.Key; }
            if (string.IsNullOrEmpty(a.Name)) a.Name = a.Key.Substring(a.Key.IndexOf('|') + 1);
        }

        /// <summary>SRUM stores paths lower-case with the device prefix in lower case: restore "\Device\".</summary>
        static string PrettyDevice(string p) => p.StartsWith(@"\device\", StringComparison.OrdinalIgnoreCase) ? @"\Device\" + p.Substring(8) : p;

        static void PackageInfo(AppUsage a, string dir, string fullName)
        {
            var parts = fullName.Split('_');
            string name = parts[0], family = parts.Length >= 5 ? parts[0] + "_" + parts[parts.Length - 1] : fullName;
            string display = null, appId = null;
            var manifest = Path.Combine(dir, "AppxManifest.xml");
            if (File.Exists(manifest))
            {
                var doc = new XmlDocument();
                doc.Load(manifest);
                foreach (XmlNode n in doc.GetElementsByTagName("*"))
                {
                    if (n.LocalName == "Application" && appId == null) appId = (n as XmlElement)?.GetAttribute("Id");
                    if (n.LocalName == "VisualElements" && display == null) display = (n as XmlElement)?.GetAttribute("DisplayName");
                }
                if (string.IsNullOrEmpty(display))
                    foreach (XmlNode n in doc.GetElementsByTagName("*"))
                        if (n.LocalName == "DisplayName" && n.ParentNode?.LocalName == "Properties") { display = n.InnerText; break; }
            }
            if (!string.IsNullOrEmpty(appId)) a.Aumid = family + "!" + appId;
            string resolved = a.Aumid != null ? StaticLoader.AppDisplayName(a.Aumid) : null;
            if (string.IsNullOrEmpty(resolved)) resolved = Resource(display, name, fullName);
            a.Name = !string.IsNullOrEmpty(resolved) ? resolved : name;
        }

        static string Resource(string value, string name, string fullName)
        {
            if (string.IsNullOrEmpty(value) || !value.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) return value;
            string res = value.Substring("ms-resource:".Length);
            var tries = new List<string>();
            if (res.StartsWith("//")) tries.Add("ms-resource:" + res);
            else if (res.StartsWith("/")) tries.Add("ms-resource://" + name + res);
            else { tries.Add("ms-resource://" + name + "/resources/" + res); tries.Add("ms-resource://" + name + "/" + res); }
            foreach (var uri in tries)
            {
                var sb = new StringBuilder(1024);
                if (Sys.SHLoadIndirectString("@{" + fullName + "? " + uri + "}", sb, sb.Capacity, IntPtr.Zero) == 0 && sb.Length > 0) return sb.ToString();
            }
            return null;
        }
    }
}
