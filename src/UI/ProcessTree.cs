using System;
using System.Collections.Generic;

namespace TaskManagerDark
{
    public enum NodeKind { Group, Process, Window, Service, Interrupts }
    public enum ProcGroup { Apps, Background, Windows }

    /// <summary>A row of the Processes / Users / Fewer details tables.</summary>
    public sealed class ProcNode : ListRow
    {
        public NodeKind Kind;
        public ProcGroup Group;
        public ProcInfo Proc;                       // root process of the row (Process rows)
        public List<ProcInfo> Members = new List<ProcInfo>();   // every process aggregated in this row
        public WinInfo Window;
        public SvcInfo Service;
        public string Name;
        public int ServiceCount, WindowCount;
        public bool Hung, Suspended, IsApp;
        public double Cpu, Disk, Net, Gpu, Power, PowerTrend;
        public long Memory, GpuDedicated;
        public int GpuEngine = -1;
        public int Generation;
    }

    /// <summary>
    /// Builds the Processes tab hierarchy: Apps (processes with windows, plus their children), Background
    /// processes and Windows processes; service hosts list their services, apps list their windows.
    /// </summary>
    public sealed class ProcessTree
    {
        static readonly HashSet<string> WindowsNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Registry", "smss.exe", "csrss.exe", "wininit.exe", "winlogon.exe", "services.exe", "lsass.exe", "LsaIso.exe",
            "svchost.exe", "fontdrvhost.exe", "dwm.exe", "sihost.exe", "explorer.exe", "lsm.exe", "MsMpEngCP.exe",
        };
        static readonly HashSet<string> Hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "System Idle Process", "Memory Compression", "Secure System" };

        // Taskmgr strings 33556-33587: friendly names of the svchost groups
        static readonly Dictionary<string, string> GroupNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "AxInstSVGroup", "ActiveX Installer" }, { "bthsvcs", "Bluetooth Support" }, { "DcomLaunch", "DCOM Server Process Launcher" },
            { "defragsvc", "Disk Defragmenter" }, { "imgsvc", "Windows Image Acquisition (WIA)" }, { "LocalService", "Local Service" },
            { "LocalServiceAndNoImpersonation", "Local Service (No Impersonation)" }, { "LocalServiceNetworkRestricted", "Local Service (Network Restricted)" },
            { "LocalServiceNoNetwork", "Local Service (No Network)" }, { "LocalServiceNoNetworkFirewall", "Local Service (No Network)" },
            { "LocalServicePeerNet", "Local Service (Peer Networking)" }, { "LocalSystemNetworkRestricted", "Local System (Network Restricted)" },
            { "netsvcs", "Local System" }, { "NetworkService", "Network Service" }, { "NetworkServiceAndNoImpersonation", "Network Service (No Impersonation)" },
            { "NetworkServiceNetworkRestricted", "Network Service (Network Restricted)" }, { "PeerDist", "BranchCache" }, { "regsvc", "Remote Registry" },
            { "rpcss", "Remote Procedure Call" }, { "SDRSVC", "Windows Backup" }, { "secsvcs", "Windows Defender" }, { "swprv", "Microsoft Software Shadow Copy Provider" },
            { "WbioSvcGroup", "Windows Biometric" }, { "wcssvc", "Windows Color System" }, { "WerSvcGroup", "Windows Error Reporting" },
            { "ICService", "Virtual Machine Heartbeat" }, { "apphost", "IIS Application Host Helper Service" }, { "iissvcs", "Internet Information Service" },
            { "print", "Printer Services" }, { "smbsvcs", "File Server Resource Manager" }, { "GPSvcGroup", "Group Policy" },
            { "UnistackSvcGroup", "Unistack Service Group" }, { "utcsvc", "UtcSvc" },
        };

        readonly Dictionary<string, ProcNode> _cache = new Dictionary<string, ProcNode>(StringComparer.Ordinal);
        int _gen;
        public readonly List<ProcNode> Apps = new List<ProcNode>(), Background = new List<ProcNode>(), Windows = new List<ProcNode>();
        public readonly Dictionary<int, ProcNode> NodeOfPid = new Dictionary<int, ProcNode>();
        public IntPtr ServiceIcon, WindowIcon;

        ProcNode Node(string key)
        {
            if (!_cache.TryGetValue(key, out var n)) { n = new ProcNode { Key = key }; _cache[key] = n; }
            n.Generation = _gen;
            n.Children = null; n.Members.Clear(); n.Level = 0; n.Parent = null; n.IsGroup = false;
            n.ServiceCount = n.WindowCount = 0; n.Hung = n.Suspended = n.IsApp = false; n.Window = null; n.Service = null; n.Proc = null;
            n.Cpu = n.Disk = n.Net = n.Gpu = n.Power = n.PowerTrend = 0; n.Memory = n.GpuDedicated = 0; n.GpuEngine = -1; n.Dim = false;
            return n;
        }

        public static bool IsWindowsProcess(ProcInfo p, ProcInfo parent)
        {
            if (p.Pid == 4) return true;
            if (WindowsNames.Contains(p.Name)) return true;
            if (p.Static != null && p.Static.Critical) return true;
            if (p.Name.Equals("conhost.exe", StringComparison.OrdinalIgnoreCase) && (parent == null || IsWindowsProcess(parent, null))) return true;
            return false;
        }

        /// <summary>Rebuilds the tree. 'include' keeps only some processes (the Users tab shows one user's processes).</summary>
        public void Build(DataEngine d, Func<ProcInfo, bool> include = null)
        {
            _gen++;
            Apps.Clear(); Background.Clear(); Windows.Clear(); NodeOfPid.Clear();
            var procs = d.Procs;
            var byPid = d.ProcByPid;
            var wins = d.Windows;
            int ownPid = Sys.GetCurrentProcessId();

            // parent of each process (ignoring parents that started later: the pid was reused)
            ProcInfo ParentOf(ProcInfo p)
            {
                if (p.Pid <= 4) return null;
                return byPid.TryGetValue(p.Cur.ParentPid, out var par) && par.CreateTime <= p.CreateTime && par.Pid != p.Pid ? par : null;
            }

            bool IsApp(ProcInfo p) => wins.ContainsKey(p.Pid);

            // attach each process to a root: the process owning windows, or the top of a same-image chain
            var root = new Dictionary<int, ProcInfo>();
            ProcInfo RootOf(ProcInfo p)
            {
                if (root.TryGetValue(p.Pid, out var r)) return r;
                r = p;
                if (!IsApp(p))
                {
                    var par = ParentOf(p);
                    if (par != null && !Hidden.Contains(par.Name) && par.Cur.SessionId == p.Cur.SessionId)
                    {
                        bool attach = par.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase)
                                   || p.Name.Equals("conhost.exe", StringComparison.OrdinalIgnoreCase) && !IsWindowsProcess(par, null)
                                   || (!IsWindowsProcess(p, par) && !IsWindowsProcess(par, null) && IsAppTree(par));
                        if (attach) r = RootOf(par);
                    }
                }
                root[p.Pid] = r;
                return r;
            }
            bool IsAppTree(ProcInfo p)
            {
                for (int depth = 0; p != null && depth < 32; depth++) { if (IsApp(p)) return true; p = ParentOf(p); if (p != null && IsWindowsProcess(p, null)) return false; }
                return false;
            }

            var rootNodes = new Dictionary<int, ProcNode>();
            foreach (var p in procs)
            {
                if (Hidden.Contains(p.Name) || p.Pid == 0) continue;
                if (include != null && !include(p)) continue;
                var r = RootOf(p);
                if (include != null && !include(r)) r = p;
                if (!rootNodes.TryGetValue(r.Pid, out var n))
                {
                    n = Node("p:" + r.Key);
                    n.Kind = NodeKind.Process; n.Proc = r; n.IsApp = IsApp(r);
                    n.Group = n.IsApp ? ProcGroup.Apps : IsWindowsProcess(r, ParentOf(r)) ? ProcGroup.Windows : ProcGroup.Background;
                    rootNodes[r.Pid] = n;
                }
                n.Members.Add(p);
                NodeOfPid[p.Pid] = n;
            }

            foreach (var n in rootNodes.Values)
            {
                var r = n.Proc;
                Aggregate(n, n.Members, d);
                List<ListRow> children = null;
                // child processes
                if (n.Members.Count > 1)
                {
                    children = new List<ListRow>();
                    foreach (var m in n.Members)
                    {
                        var c = Node("c:" + m.Key);
                        c.Kind = NodeKind.Process; c.Proc = m; c.Group = n.Group; c.Level = 1;
                        c.Members.Add(m);
                        Aggregate(c, c.Members, d);
                        c.Name = ProcessName(m, d);
                        c.Icon = m.Static?.IconSmall ?? IntPtr.Zero;
                        AddWindows(c, m, wins, d, 2);
                        AddServices(c, m, d, 2);
                        children.Add(c);
                    }
                }
                else
                {
                    // a single process: its windows and services are its children
                    var tmp = new ProcNode();
                    AddWindows(tmp, r, wins, d, 1);
                    AddServices(tmp, r, d, 1);
                    if (tmp.Children != null) children = tmp.Children;
                    n.WindowCount = tmp.WindowCount; n.ServiceCount = tmp.ServiceCount;
                }
                if (n.Members.Count > 1) foreach (var m in n.Members) { if (wins.TryGetValue(m.Pid, out var wl)) n.WindowCount += wl.Count; if (d.ServicesByPid.TryGetValue(m.Pid, out var sl)) n.ServiceCount += sl.Count; }
                n.Children = children;
                n.Name = RowName(n, d);
                n.Icon = r.Static?.IconSmall ?? IntPtr.Zero;
                (n.Group == ProcGroup.Apps ? Apps : n.Group == ProcGroup.Windows ? Windows : Background).Add(n);
            }

            // "System interrupts" (deferred procedure calls and interrupt service routines)
            if (include == null)
            {
                var si = Node("p:interrupts");
                si.Kind = NodeKind.Interrupts; si.Group = ProcGroup.Windows; si.Name = "System interrupts";
                si.Cpu = d.Sys.Interrupts;
                si.Icon = d.ProcByPid.TryGetValue(4, out var sysProc) ? sysProc.Static?.IconSmall ?? IntPtr.Zero : IntPtr.Zero;
                Windows.Add(si);
            }

            // forget rows that disappeared
            if (_cache.Count > 4 * (procs.Count + 64))
            {
                var dead = new List<string>();
                foreach (var kv in _cache) if (kv.Value.Generation != _gen) dead.Add(kv.Key);
                foreach (var k in dead) _cache.Remove(k);
            }
        }

        void AddWindows(ProcNode parent, ProcInfo p, Dictionary<int, List<WinInfo>> wins, DataEngine d, int level)
        {
            if (!wins.TryGetValue(p.Pid, out var list)) return;
            foreach (var w in list)
            {
                var c = Node("w:" + w.Hwnd.ToInt64());
                c.Kind = NodeKind.Window; c.Window = w; c.Proc = p; c.Level = level; c.Name = w.Title; c.Hung = w.Hung;
                c.Icon = p.Static?.IconSmall ?? IntPtr.Zero;
                (parent.Children ?? (parent.Children = new List<ListRow>())).Add(c);
                parent.WindowCount++;
                if (w.Hung) parent.Hung = true;
            }
        }

        void AddServices(ProcNode parent, ProcInfo p, DataEngine d, int level)
        {
            if (!d.ServicesByPid.TryGetValue(p.Pid, out var list)) return;
            foreach (var s in list)
            {
                var c = Node("s:" + s.Name);
                c.Kind = NodeKind.Service; c.Service = s; c.Proc = p; c.Level = level; c.Name = s.DisplayName; c.Icon = ServiceIcon;
                (parent.Children ?? (parent.Children = new List<ListRow>())).Add(c);
                parent.ServiceCount++;
            }
        }

        static void Aggregate(ProcNode n, List<ProcInfo> members, DataEngine d)
        {
            bool allSuspended = members.Count > 0;
            foreach (var m in members)
            {
                n.Cpu += m.Cur.Cpu; n.Memory += m.Cur.PrivateWS; n.Disk += m.Cur.DiskBps; n.Net += m.Cur.NetBps;
                n.GpuDedicated += m.Cur.GpuDedicated;
                if (m.Cur.Gpu > n.Gpu) { n.Gpu = m.Cur.Gpu; n.GpuEngine = m.Cur.GpuEngine; }
                n.Power += DataEngine.PowerScore(m.Cur); n.PowerTrend += m.PowerEma;
                if (!m.Cur.Suspended) allSuspended = false;
            }
            if (n.Cpu > 100) n.Cpu = 100;
            n.Suspended = allSuspended;
            if (d.Windows.TryGetValue(n.Proc?.Pid ?? -1, out var wl)) foreach (var w in wl) if (w.Hung) n.Hung = true;
        }

        public static string ProcessName(ProcInfo p, DataEngine d)
        {
            if (p.Name.Equals("svchost.exe", StringComparison.OrdinalIgnoreCase)) return ServiceHostName(p, d);
            var desc = p.Static?.Description;
            if (!string.IsNullOrEmpty(desc)) return desc;
            if (p.Pid == 4) return "System";
            return p.Name;
        }

        public static string ServiceHostName(ProcInfo p, DataEngine d)
        {
            if (!d.ServicesByPid.TryGetValue(p.Pid, out var list) || list.Count == 0) return "Service Host: " + (p.Static?.Description ?? "svchost.exe").Replace("Host Process for Windows Services", "Unknown");
            if (list.Count == 1) return "Service Host: " + list[0].DisplayName;
            var g = list[0].Group;
            if (g != null && GroupNames.TryGetValue(g, out var name)) return "Service Host: " + name;
            return "Service Host: " + (g ?? list[0].DisplayName);
        }

        static string RowName(ProcNode n, DataEngine d)
        {
            string name = ProcessName(n.Proc, d);
            int count = n.Members.Count > 1 ? n.Members.Count
                      : n.Proc.Name.Equals("svchost.exe", StringComparison.OrdinalIgnoreCase) && n.ServiceCount > 1 ? n.ServiceCount
                      : n.WindowCount > 1 ? n.WindowCount : 0;
            return count > 1 ? name + " (" + count + ")" : name;
        }
    }
}
