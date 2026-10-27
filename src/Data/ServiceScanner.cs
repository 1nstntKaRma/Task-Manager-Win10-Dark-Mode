using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TaskManagerDark
{
    /// <summary>Enumerates Win32 services with their process ids; configuration (group, description) is loaded lazily.</summary>
    public sealed unsafe class ServiceScanner : IDisposable
    {
        IntPtr _scm;
        IntPtr _buf; int _bufSize = 256 * 1024;
        readonly Dictionary<string, SvcInfo> _byName = new Dictionary<string, SvcInfo>(StringComparer.OrdinalIgnoreCase);
        public List<SvcInfo> Current = new List<SvcInfo>();
        public bool Changed;

        public ServiceScanner()
        {
            _scm = Sys.OpenSCManager(null, null, Sys.SC_MANAGER_CONNECT | Sys.SC_MANAGER_ENUMERATE_SERVICE);
            _buf = Marshal.AllocHGlobal(_bufSize);
        }

        public bool Scan()
        {
            Changed = false;
            if (_scm == IntPtr.Zero) return false;
            int resume = 0, needed, count;
            bool ok;
            while (true)
            {
                resume = 0;
                ok = Sys.EnumServicesStatusEx(_scm, Sys.SC_ENUM_PROCESS_INFO, Sys.SERVICE_WIN32, Sys.SERVICE_STATE_ALL, _buf, _bufSize, out needed, out count, ref resume, null);
                if (!ok && Marshal.GetLastWin32Error() == 234 /*ERROR_MORE_DATA*/)
                {
                    Marshal.FreeHGlobal(_buf);
                    _bufSize = needed + 64 * 1024;
                    _buf = Marshal.AllocHGlobal(_bufSize);
                    continue;
                }
                break;
            }
            if (!ok) return false;
            var list = new List<SvcInfo>(count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int stride = Marshal.SizeOf(typeof(Sys.ENUM_SERVICE_STATUS_PROCESS));
            for (int i = 0; i < count; i++)
            {
                byte* e = (byte*)_buf + i * stride;
                string name = new string(*(char**)e);
                if (!_byName.TryGetValue(name, out var s))
                {
                    s = new SvcInfo { Name = name, DisplayName = new string(*(char**)(e + IntPtr.Size)) };
                    _byName[name] = s;
                    Changed = true;
                }
                var st = (Sys.SERVICE_STATUS_PROCESS*)(e + IntPtr.Size * 2);
                if (s.State != st->dwCurrentState || s.Pid != st->dwProcessId) Changed = true;
                s.State = st->dwCurrentState; s.Pid = st->dwProcessId; s.ServiceType = st->dwServiceType;
                seen.Add(name);
                list.Add(s);
            }
            if (seen.Count != _byName.Count)
            {
                var dead = new List<string>();
                foreach (var k in _byName.Keys) if (!seen.Contains(k)) dead.Add(k);
                foreach (var k in dead) _byName.Remove(k);
                Changed = true;
            }
            // per-user service instances ("AarSvc_199cf5") show the template's name plus the suffix, like Task Manager
            if (Changed)
                foreach (var s in list)
                {
                    if ((s.ServiceType & 0x80) == 0 || s.DisplayName != s.Name) continue;
                    int u = s.Name.LastIndexOf('_');
                    if (u <= 0) continue;
                    var name = TemplateDisplayName(s.Name.Substring(0, u));
                    if (!string.IsNullOrEmpty(name)) s.DisplayName = name + s.Name.Substring(u);
                }
            Current = list;
            return true;
        }

        readonly Dictionary<string, string> _templateNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Display name of a per-user service template (templates are not enumerated, so read the registry).</summary>
        string TemplateDisplayName(string template)
        {
            if (_templateNames.TryGetValue(template, out var n)) return n;
            if (_byName.TryGetValue(template, out var t) && !string.IsNullOrEmpty(t.DisplayName) && t.DisplayName != template) n = t.DisplayName;
            else
            {
                try
                {
                    using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + template))
                        n = Sys.LoadIndirect(k?.GetValue("DisplayName") as string);
                }
                catch (Exception ex) { ErrorLog.Write("Service template " + template, ex); }
            }
            if (n != null && n.StartsWith("@")) n = null;
            _templateNames[template] = n;
            return n;
        }

        /// <summary>Loads group/description for up to 'max' services that do not have them yet.</summary>
        public int LoadConfigs(int max)
        {
            int n = 0;
            foreach (var s in Current)
            {
                if (s.ConfigLoaded) continue;
                if (n++ >= max) break;
                LoadConfig(s);
                Changed = true;
            }
            return n;
        }

        void LoadConfig(SvcInfo s)
        {
            s.ConfigLoaded = true;
            IntPtr h = Sys.OpenService(_scm, s.Name, Sys.SERVICE_QUERY_CONFIG);
            if (h == IntPtr.Zero) return;
            try
            {
                Sys.QueryServiceConfig(h, IntPtr.Zero, 0, out int needed);
                if (needed > 0)
                {
                    IntPtr b = Marshal.AllocHGlobal(needed);
                    try
                    {
                        if (Sys.QueryServiceConfig(h, b, needed, out needed))
                        {
                            var c = (Sys.QUERY_SERVICE_CONFIG*)b;
                            s.StartType = c->dwStartType;
                            s.ImagePath = c->lpBinaryPathName != IntPtr.Zero ? Marshal.PtrToStringUni(c->lpBinaryPathName) : null;
                            s.Group = GroupFromPath(s.ImagePath);
                        }
                    }
                    finally { Marshal.FreeHGlobal(b); }
                }
                Sys.QueryServiceConfig2(h, Sys.SERVICE_CONFIG_DESCRIPTION, IntPtr.Zero, 0, out needed);
                if (needed > 0)
                {
                    IntPtr b = Marshal.AllocHGlobal(needed);
                    try
                    {
                        if (Sys.QueryServiceConfig2(h, Sys.SERVICE_CONFIG_DESCRIPTION, b, needed, out needed))
                        {
                            IntPtr d = *(IntPtr*)b;
                            if (d != IntPtr.Zero) s.Description = Sys.LoadIndirect(Marshal.PtrToStringUni(d));
                        }
                    }
                    finally { Marshal.FreeHGlobal(b); }
                }
            }
            finally { Sys.CloseServiceHandle(h); }
        }

        /// <summary>svchost groups come from the "-k &lt;group&gt;" argument of the service's command line.</summary>
        public static string GroupFromPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            int k = path.IndexOf(" -k ", StringComparison.OrdinalIgnoreCase);
            if (k < 0) k = path.IndexOf(" /k ", StringComparison.OrdinalIgnoreCase);
            if (k < 0) return null;
            var rest = path.Substring(k + 4).Trim();
            int sp = rest.IndexOf(' ');
            return (sp > 0 ? rest.Substring(0, sp) : rest).Trim('"');
        }

        public void Dispose()
        {
            if (_scm != IntPtr.Zero) { Sys.CloseServiceHandle(_scm); _scm = IntPtr.Zero; }
            if (_buf != IntPtr.Zero) { Marshal.FreeHGlobal(_buf); _buf = IntPtr.Zero; }
        }
    }
}
