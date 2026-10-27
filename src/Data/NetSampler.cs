using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace TaskManagerDark
{
    public sealed class NetSample
    {
        public long Luid;
        public NetStatic Static;
        public double SendBps, RecvBps;          // bits per second
        public long LinkBps;
        public bool Connected;
        public long BytesSent, BytesRecv, UcastSent, UcastRecv, NUcastSent, NUcastRecv;
    }

    public sealed class NetStatic
    {
        public long Luid;
        public int IfIndex;
        public Guid IfGuid;
        public string Alias = "", Description = "";
        public string Kind = "Ethernet";         // Ethernet / Wi-Fi / Bluetooth PAN / Mobile
        public string ConnectionType = "Ethernet";
        public string Ssid, Ipv4, Ipv6, DnsSuffix;
        public int SignalQuality = -1;           // 0..100 (Wi-Fi)
        public int Order;
    }

    /// <summary>
    /// Per-adapter throughput from GetIfTable2 deltas (in-memory counters, never blocks). Addresses and Wi-Fi details
    /// are refreshed every few seconds on a worker thread, because the WLAN service can take seconds to answer.
    /// </summary>
    public sealed unsafe class NetSampler : IDisposable
    {
        [DllImport("iphlpapi.dll")] static extern int GetIfTable2(out IntPtr table);
        [DllImport("iphlpapi.dll")] static extern void FreeMibTable(IntPtr table);
        [DllImport("iphlpapi.dll")] static extern int GetAdaptersAddresses(int family, int flags, IntPtr reserved, IntPtr buf, ref int size);
        [DllImport("ntdll.dll", CharSet = CharSet.Unicode, EntryPoint = "RtlIpv6AddressToStringExW")] static extern int RtlIpv6AddressToStringEx(byte* addr, uint scope, ushort port, StringBuilder sb, ref int len);
        [DllImport("ntdll.dll", CharSet = CharSet.Unicode, EntryPoint = "RtlIpv4AddressToStringExW")] static extern int RtlIpv4AddressToStringEx(byte* addr, ushort port, StringBuilder sb, ref int len);
        [DllImport("wlanapi.dll")] static extern int WlanOpenHandle(int ver, IntPtr reserved, out int negotiated, out IntPtr handle);
        [DllImport("wlanapi.dll")] static extern int WlanCloseHandle(IntPtr h, IntPtr reserved);
        [DllImport("wlanapi.dll")] static extern int WlanEnumInterfaces(IntPtr h, IntPtr reserved, out IntPtr list);
        [DllImport("wlanapi.dll")] static extern int WlanQueryInterface(IntPtr h, ref Guid iface, int opcode, IntPtr reserved, out int size, out IntPtr data, out int valueType);
        [DllImport("wlanapi.dll")] static extern void WlanFreeMemory(IntPtr p);

        const int RowSize = 1352;
        readonly Dictionary<long, long[]> _prev = new Dictionary<long, long[]>();
        long _prevTick;
        readonly Dictionary<long, NetStatic> _statics = new Dictionary<long, NetStatic>();   // sampler thread only
        int _ticks, _refreshing;
        IntPtr _wlan;
        bool _wlanTried;
        volatile bool _disposed;

        public List<NetSample> Sample()
        {
            var list = new List<NetSample>();
            if (GetIfTable2(out var table) != 0) return list;
            long now = Sys.Qpc();
            double dt = _prevTick == 0 ? 0 : (now - _prevTick) / (double)Sys.QpcFrequency;
            _prevTick = now;
            bool refresh = _ticks++ % 5 == 0;
            try
            {
                int n = *(int*)table;
                byte* rows = (byte*)table + 8;
                int order = 0;
                for (int i = 0; i < n; i++)
                {
                    byte* r = rows + (long)i * RowSize;
                    int type = *(int*)(r + 1128);
                    int medium = *(int*)(r + 1140);
                    byte flags = r[1152];
                    bool hardware = (flags & 1) != 0, filter = (flags & 2) != 0, connector = (flags & 4) != 0;
                    int oper = *(int*)(r + 1156), admin = *(int*)(r + 1160), media = *(int*)(r + 1164);
                    if (filter) continue;
                    string desc = new string((char*)(r + 542));
                    bool hyperV = type == 6 && desc.StartsWith("Hyper-V Virtual Ethernet Adapter", StringComparison.OrdinalIgnoreCase);
                    if (!(hardware || hyperV)) continue;
                    if (type != 6 && type != 71 && type != 243 && type != 244 && type != 23) continue;
                    if (oper == 6 /*NotPresent*/ || admin != 1) continue;
                    long luid = *(long*)r;
                    if (!_statics.TryGetValue(luid, out var st))
                    {
                        st = new NetStatic
                        {
                            Luid = luid, IfIndex = *(int*)(r + 8), IfGuid = *(Guid*)(r + 12),
                            Alias = new string((char*)(r + 28)), Description = desc,
                        };
                        st.Kind = type == 71 ? "Wi-Fi" : type == 243 || type == 244 ? "Mobile" : medium == 10 ? "Bluetooth PAN" : "Ethernet";
                        st.ConnectionType = type == 71 ? "802.11" : st.Kind;
                        _statics[luid] = st;
                        refresh = true;
                    }
                    st.Order = order++;
                    var s = new NetSample
                    {
                        Luid = luid, Static = st,
                        Connected = media == 1 && oper == 1,
                        LinkBps = Math.Max(*(long*)(r + 1192), *(long*)(r + 1200)),
                        BytesRecv = *(long*)(r + 1208), BytesSent = *(long*)(r + 1280),
                        UcastRecv = *(long*)(r + 1216), NUcastRecv = *(long*)(r + 1224),
                        UcastSent = *(long*)(r + 1288), NUcastSent = *(long*)(r + 1296),
                    };
                    if (_prev.TryGetValue(luid, out var p) && dt > 0)
                    {
                        s.RecvBps = Math.Max(0, (s.BytesRecv - p[0]) * 8 / dt);
                        s.SendBps = Math.Max(0, (s.BytesSent - p[1]) * 8 / dt);
                    }
                    _prev[luid] = new[] { s.BytesRecv, s.BytesSent };
                    list.Add(s);
                }
            }
            finally { FreeMibTable(table); }
            if (refresh) StartRefresh();
            return list;
        }

        void StartRefresh()
        {
            if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0) return;   // the previous refresh is still running
            var adapters = new List<NetStatic>(_statics.Values).ToArray();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { RefreshAddresses(adapters); RefreshWifi(adapters); }
                catch (Exception ex) { ErrorLog.Write("Network details", ex); }
                finally { Interlocked.Exchange(ref _refreshing, 0); }
            });
        }

        static void RefreshAddresses(NetStatic[] adapters)
        {
            var byLuid = new Dictionary<long, NetStatic>();
            foreach (var a in adapters) byLuid[a.Luid] = a;
            int size = 32 * 1024;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                IntPtr buf = Marshal.AllocHGlobal(size);
                try
                {
                    int r = GetAdaptersAddresses(0 /*AF_UNSPEC*/, 0x2 | 0x4 | 0x8 /*skip anycast, multicast, dns server*/, IntPtr.Zero, buf, ref size);
                    if (r == 111 /*ERROR_BUFFER_OVERFLOW*/) continue;
                    if (r != 0) return;
                    for (byte* a = (byte*)buf; a != null; a = *(byte**)(a + 8))
                    {
                        // IP_ADAPTER_ADDRESSES_LH (x64): Next @8, FirstUnicastAddress @24, DnsSuffix @56, Luid @224
                        long luid = *(long*)(a + 224);
                        if (!byLuid.TryGetValue(luid, out var st)) continue;
                        string v4 = null, v6 = null, v6ll = null;
                        for (byte* u = *(byte**)(a + 24); u != null; u = *(byte**)(u + 8))
                        {
                            byte* sa = *(byte**)(u + 16);           // SOCKET_ADDRESS.lpSockaddr
                            if (sa == null) continue;
                            short fam = *(short*)sa;
                            var sb = new StringBuilder(64); int len = 64;
                            if (fam == 2) { if (RtlIpv4AddressToStringEx(sa + 4, 0, sb, ref len) == 0 && v4 == null) v4 = sb.ToString(); }
                            else if (fam == 23)
                            {
                                byte* addr = sa + 8; uint scope = *(uint*)(sa + 24);
                                if (RtlIpv6AddressToStringEx(addr, scope, 0, sb, ref len) == 0)
                                {
                                    bool ll = addr[0] == 0xFE && (addr[1] & 0xC0) == 0x80;
                                    if (ll) { if (v6ll == null) v6ll = sb.ToString(); }
                                    else if (v6 == null) v6 = sb.ToString();
                                }
                            }
                        }
                        st.Ipv4 = v4; st.Ipv6 = v6 ?? v6ll;
                        char* dns = *(char**)(a + 56);
                        st.DnsSuffix = dns != null ? new string(dns) : null;
                    }
                    return;
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }

        void RefreshWifi(NetStatic[] adapters)
        {
            bool anyWifi = false;
            foreach (var st in adapters) if (st.Kind == "Wi-Fi") { anyWifi = true; break; }
            if (!anyWifi || _disposed) return;
            if (!_wlanTried) { _wlanTried = true; try { if (WlanOpenHandle(2, IntPtr.Zero, out _, out _wlan) != 0) _wlan = IntPtr.Zero; } catch (Exception ex) { _wlan = IntPtr.Zero; ErrorLog.Write("WlanOpenHandle", ex); } }
            if (_wlan == IntPtr.Zero) return;
            foreach (var st in adapters)
            {
                if (st.Kind != "Wi-Fi") continue;
                var g = st.IfGuid;
                if (WlanQueryInterface(_wlan, ref g, 7 /*wlan_intf_opcode_current_connection*/, IntPtr.Zero, out int size, out var data, out _) != 0) { st.Ssid = null; st.SignalQuality = -1; continue; }
                try
                {
                    byte* d = (byte*)data;
                    int state = *(int*)d;
                    if (state != 1 /*connected*/) { st.Ssid = null; st.SignalQuality = -1; continue; }
                    int ssidLen = *(int*)(d + 520);
                    st.Ssid = Encoding.UTF8.GetString(d + 524, Math.Min(32, Math.Max(0, ssidLen)));
                    int phy = *(int*)(d + 568);
                    st.SignalQuality = *(int*)(d + 576);
                    st.ConnectionType = phy == 4 ? "802.11a" : phy == 5 ? "802.11b" : phy == 6 ? "802.11g" : phy == 7 ? "802.11n" : phy == 8 ? "802.11ac" : phy == 9 ? "802.11ad" : phy == 10 ? "802.11ax" : "802.11";
                }
                finally { WlanFreeMemory(data); }
            }
        }

        public void Dispose()
        {
            _disposed = true;
            // a refresh may still be using the WLAN handle; then the process exit closes it
            if (_wlan != IntPtr.Zero && Interlocked.CompareExchange(ref _refreshing, 1, 0) == 0) { WlanCloseHandle(_wlan, IntPtr.Zero); _wlan = IntPtr.Zero; }
        }
    }
}
