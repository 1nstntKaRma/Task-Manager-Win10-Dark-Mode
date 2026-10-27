using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TaskManagerDark
{
    public sealed class GpuSample
    {
        public int Index;
        public long Luid;
        public double[] Engines;                // utilization per engine ordinal
        public double Max;                      // busiest engine
        public long Dedicated, Shared;          // bytes in use
        public int TemperatureC = -1;
    }

    public sealed class GpuStatic
    {
        public int Index;
        public long Luid;
        public string Name = "", DriverVersion, DriverDate, DirectX, Location;
        public long DedicatedLimit, SharedLimit, HwReserved;
        public int VendorId, DeviceId;
        public string[] EngineNames = new string[0];
        public bool PerfDataSupported, NamesFromDriver;
        public string Title => "GPU " + Index;
    }

    /// <summary>Per-process GPU figures of one counter sample.</summary>
    public struct GpuProc
    {
        public double Max; public int EngineKey;   // busiest engine (adapter*64+engine), -1 when none
        public long Dedicated, Shared;
    }

    /// <summary>
    /// GPU engines and memory from the PDH "GPU Engine" / "GPU Adapter Memory" / "GPU Process Memory" counters.
    /// Adapters come from a quick DXGI enumeration on a worker thread; the slower facts (driver, DirectX level)
    /// follow later. Sampling runs on the counters thread only.
    /// </summary>
    public sealed unsafe class GpuSampler
    {
        sealed class AdapterSet
        {
            public readonly GpuStatic[] List; public readonly Dictionary<long, GpuStatic> ByLuid = new Dictionary<long, GpuStatic>();
            public AdapterSet(GpuStatic[] list) { List = list; foreach (var a in list) ByLuid[a.Luid] = a; }
        }

        volatile AdapterSet _set = new AdapterSet(new GpuStatic[0]);
        /// <summary>True once the adapters are known (their facts may still be loading).</summary>
        public volatile bool AdaptersKnown;
        public GpuStatic[] Adapters => _set.List;

        PdhQuery _q;
        IntPtr _engine, _adDed, _adShared, _procDed, _procShared;
        PdhQuery.ItemVisitor _visitEngine, _visitAdDed, _visitAdShared, _visitProcDed, _visitProcShared;
        // per-sample scratch (counters thread)
        AdapterSet _cs;
        List<GpuSample> _cur;
        readonly Dictionary<int, double[]> _procEngines = new Dictionary<int, double[]>();   // pid -> per (adapter*64+engine) utilization
        Dictionary<int, GpuProc> _procs;

        /// <summary>Adds the counters to a query owned by the counters thread.</summary>
        public void AddCounters(PdhQuery q)
        {
            _q = q;
            _engine = q.Add(@"\GPU Engine(*)\Utilization Percentage");
            _adDed = q.Add(@"\GPU Adapter Memory(*)\Dedicated Usage");
            _adShared = q.Add(@"\GPU Adapter Memory(*)\Shared Usage");
            _procDed = q.Add(@"\GPU Process Memory(*)\Dedicated Usage");
            _procShared = q.Add(@"\GPU Process Memory(*)\Shared Usage");
            _visitEngine = VisitEngine; _visitAdDed = (n, v) => VisitAdapterMem(n, v, true); _visitAdShared = (n, v) => VisitAdapterMem(n, v, false);
            _visitProcDed = (n, v) => VisitProcMem(n, v, true); _visitProcShared = (n, v) => VisitProcMem(n, v, false);
        }

        /// <summary>Finds the adapters (fast), publishes them, then fills in their slower facts. Worker thread.</summary>
        public void LoadAdapters()
        {
            GpuStatic[] list = null;
            if (DiskSampler.DebugFactsDelayMs > 0) System.Threading.Thread.Sleep(DiskSampler.DebugFactsDelayMs);
            try
            {
                var l = Dxgi.Enumerate();
                for (int i = 0; i < l.Count; i++) l[i].Index = i;
                list = l.ToArray();
            }
            catch (Exception ex) { ErrorLog.Write("GpuSampler.Enumerate", ex); }
            if (list != null) _set = new AdapterSet(list);
            AdaptersKnown = true;
            StartupLog.Mark("gpu adapters (" + (list?.Length ?? 0) + ")");
            if (list == null) return;
            foreach (var a in list)
            {
                try { GpuFacts.Fill(a); } catch (Exception ex) { ErrorLog.Write("GpuFacts.Fill", ex); }
            }
            StartupLog.Mark("gpu facts");
            try { Dxgi.FillDirectX(list); } catch (Exception ex) { ErrorLog.Write("GpuSampler.DirectX", ex); }
            StartupLog.Mark("gpu directx");
        }

        /// <summary>One sample from the last collected query data; per-process figures go to <paramref name="procs"/>.</summary>
        public List<GpuSample> Sample(out Dictionary<int, GpuProc> procs)
        {
            procs = null;
            var set = _set;
            if (_q == null || set.List.Length == 0) return null;
            _cs = set;
            _cur = new List<GpuSample>(set.List.Length);
            foreach (var a in set.List) _cur.Add(new GpuSample { Index = a.Index, Luid = a.Luid, Engines = new double[Math.Max(1, a.EngineNames.Length)] });
            foreach (var arr in _procEngines.Values) Array.Clear(arr, 0, arr.Length);
            _procs = new Dictionary<int, GpuProc>();
            _q.ForEach(_engine, _visitEngine);
            _q.ForEach(_adDed, _visitAdDed);
            _q.ForEach(_adShared, _visitAdShared);
            _q.ForEach(_procDed, _visitProcDed);
            _q.ForEach(_procShared, _visitProcShared);
            foreach (var s in _cur)
            {
                double m = 0;
                for (int i = 0; i < s.Engines.Length; i++) { if (s.Engines[i] > 100) s.Engines[i] = 100; if (s.Engines[i] > m) m = s.Engines[i]; }
                s.Max = m;
                var a = set.List[s.Index];
                if (a.PerfDataSupported) s.TemperatureC = GpuFacts.Temperature(a);
            }
            // busiest engine per process
            List<int> idle = null;
            foreach (var kv in _procEngines)
            {
                var arr = kv.Value;
                double m = 0; int key = -1;
                for (int i = 0; i < arr.Length; i++) if (arr[i] > m) { m = arr[i]; key = i; }
                if (key < 0) { (idle ?? (idle = new List<int>())).Add(kv.Key); continue; }
                if (!_procs.TryGetValue(kv.Key, out var gp)) gp.EngineKey = -1;
                gp.Max = Math.Min(100, m); gp.EngineKey = key;
                _procs[kv.Key] = gp;
            }
            if (idle != null && _procEngines.Count > 512) foreach (var pid in idle) _procEngines.Remove(pid);   // keep the scratch small
            procs = _procs;
            _procs = null;
            return _cur;
        }

        /// <summary>Zero samples for the known adapters, so they are listed before the counters have data.</summary>
        public List<GpuSample> Placeholder()
        {
            var set = _set;
            if (set.List.Length == 0) return null;
            var l = new List<GpuSample>(set.List.Length);
            foreach (var a in set.List) l.Add(new GpuSample { Index = a.Index, Luid = a.Luid, Engines = new double[Math.Max(1, a.EngineNames.Length)] });
            return l;
        }

        // pid_1234_luid_0x00000000_0x0000C2E7_phys_0_eng_0_engtype_3D
        void VisitEngine(char* n, double v)
        {
            if (!PdhQuery.StartsWith(n, "pid_")) return;
            int i = 4;
            int pid = (int)PdhQuery.ParseLong(n, ref i);
            int k = PdhQuery.IndexAfter(n, "luid_", i); if (k < 0) return;
            long hi = PdhQuery.ParseHex(n, ref k); k++;
            long lo = PdhQuery.ParseHex(n, ref k);
            int e = PdhQuery.IndexAfter(n, "_eng_", k); if (e < 0) return;
            int eng = (int)PdhQuery.ParseLong(n, ref e);
            long luid = (hi << 32) | (lo & 0xFFFFFFFF);
            if (!_cs.ByLuid.TryGetValue(luid, out var a)) return;
            var s = _cur[a.Index];
            if (eng >= s.Engines.Length)
            {
                var bigger = new double[eng + 1]; Array.Copy(s.Engines, bigger, s.Engines.Length); s.Engines = bigger;
            }
            if (!a.NamesFromDriver && (a.EngineNames.Length <= eng || a.EngineNames[eng] == null))
            {
                // the driver did not report node names: use the engine type in the counter instance ("engtype_VideoDecode")
                var names = new string[Math.Max(eng + 1, a.EngineNames.Length)]; Array.Copy(a.EngineNames, names, a.EngineNames.Length);
                int t = PdhQuery.IndexAfter(n, "engtype_", e);
                names[eng] = t >= 0 ? GpuFacts.Spaced(new string(n + t)) : "Engine " + eng;
                a.EngineNames = names;
            }
            if (v <= 0) return;
            s.Engines[eng] += v;
            int slots = _cs.List.Length * 64;
            if (!_procEngines.TryGetValue(pid, out var arr) || arr.Length < slots) { arr = new double[slots]; _procEngines[pid] = arr; }
            int slot = a.Index * 64 + Math.Min(63, eng);
            if (slot < arr.Length) arr[slot] += v;
        }

        // luid_0x00000000_0x0000C2E7_phys_0
        void VisitAdapterMem(char* n, double v, bool dedicated)
        {
            int k = PdhQuery.IndexAfter(n, "luid_"); if (k < 0) return;
            long hi = PdhQuery.ParseHex(n, ref k); k++;
            long lo = PdhQuery.ParseHex(n, ref k);
            if (!_cs.ByLuid.TryGetValue((hi << 32) | (lo & 0xFFFFFFFF), out var a)) return;
            if (dedicated) _cur[a.Index].Dedicated += (long)v; else _cur[a.Index].Shared += (long)v;
        }

        void VisitProcMem(char* n, double v, bool dedicated)
        {
            if (!PdhQuery.StartsWith(n, "pid_")) return;
            int i = 4;
            int pid = (int)PdhQuery.ParseLong(n, ref i);
            if (!_procs.TryGetValue(pid, out var gp)) gp.EngineKey = -1;
            if (dedicated) gp.Dedicated += (long)v; else gp.Shared += (long)v;
            _procs[pid] = gp;
        }

        public string EngineName(int key)
        {
            if (key < 0) return "";
            int ai = key / 64, e = key % 64;
            var list = _set.List;
            if (ai >= list.Length) return "";
            var names = list[ai].EngineNames;
            return "GPU " + ai + " - " + (e < names.Length && names[e] != null ? names[e] : "Engine " + e);
        }
    }

    /// <summary>Raw DXGI enumeration through the COM vtables (no interop assemblies needed).</summary>
    static unsafe class Dxgi
    {
        [DllImport("dxgi.dll")] static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumAdapters1Fn(IntPtr self, int index, out IntPtr adapter);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetDesc1Fn(IntPtr self, IntPtr desc);

        delegate void AdapterVisitor(IntPtr adapter, byte* desc);

        /// <summary>Calls <paramref name="visit"/> for every hardware adapter with its DXGI_ADAPTER_DESC1.</summary>
        static void ForEachAdapter(AdapterVisitor visit)
        {
            var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); // IDXGIFactory1
            if (CreateDXGIFactory1(ref iid, out var factory) != 0 || factory == IntPtr.Zero) return;
            try
            {
                var enumFn = (EnumAdapters1Fn)Marshal.GetDelegateForFunctionPointer((*(IntPtr**)factory)[12], typeof(EnumAdapters1Fn));
                IntPtr desc = Marshal.AllocHGlobal(320);
                try
                {
                    for (int i = 0; i < 16; i++)
                    {
                        if (enumFn(factory, i, out var adapter) != 0 || adapter == IntPtr.Zero) break;
                        try
                        {
                            var getDesc = (GetDesc1Fn)Marshal.GetDelegateForFunctionPointer((*(IntPtr**)adapter)[10], typeof(GetDesc1Fn));
                            if (getDesc(adapter, desc) != 0) continue;
                            byte* d = (byte*)desc;
                            if ((*(int*)(d + 304) & 2) != 0) continue;   // software adapter (Microsoft Basic Render Driver)
                            visit(adapter, d);
                        }
                        finally { Marshal.Release(adapter); }
                    }
                }
                finally { Marshal.FreeHGlobal(desc); }
            }
            finally { Marshal.Release(factory); }
        }

        static long Luid(byte* d) => ((long)*(int*)(d + 300) << 32) | *(uint*)(d + 296);

        /// <summary>Adapters with their names and memory sizes (DXGI only: fast, no driver is loaded).</summary>
        public static List<GpuStatic> Enumerate()
        {
            var list = new List<GpuStatic>();
            ForEachAdapter((adapter, d) => list.Add(new GpuStatic
            {
                Name = new string((char*)d).Trim(),
                VendorId = *(int*)(d + 256), DeviceId = *(int*)(d + 260),
                DedicatedLimit = (long)*(ulong*)(d + 272), SharedLimit = (long)*(ulong*)(d + 288),
                Luid = Luid(d),
            }));
            return list;
        }

        /// <summary>DirectX version per adapter. Creating a device loads the display driver, which can take a while.</summary>
        public static void FillDirectX(GpuStatic[] list)
        {
            ForEachAdapter((adapter, d) =>
            {
                long luid = Luid(d);
                foreach (var a in list) if (a.Luid == luid && a.DirectX == null) a.DirectX = GpuFacts.DirectXVersion(adapter);
            });
        }
    }

    /// <summary>Driver, location, engines, temperature and DirectX level of a GPU.</summary>
    static unsafe class GpuFacts
    {
        [DllImport("gdi32.dll")] static extern int D3DKMTOpenAdapterFromLuid(IntPtr p);
        [DllImport("gdi32.dll")] static extern int D3DKMTCloseAdapter(IntPtr p);
        [DllImport("gdi32.dll")] static extern int D3DKMTQueryAdapterInfo(IntPtr p);
        [DllImport("d3d11.dll")] static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, int flags, int[] levels, int n, int sdk, IntPtr device, out int level, IntPtr context);
        [DllImport("d3d12.dll")] static extern int D3D12CreateDevice(IntPtr adapter, int minLevel, ref Guid riid, IntPtr device);

        public static string DirectXVersion(IntPtr adapter)
        {
            try
            {
                int[] levels = { 0xc100, 0xc000, 0xb100, 0xb000, 0xa100, 0xa000, 0x9300, 0x9200, 0x9100 };
                if (D3D11CreateDevice(adapter, 0, IntPtr.Zero, 0, levels, levels.Length, 7, IntPtr.Zero, out int fl, IntPtr.Zero) < 0) return null;
                string fls = "FL " + ((fl >> 12) & 0xF) + "." + ((fl >> 8) & 0xF);
                bool dx12 = false;
                try { var iid = new Guid("189819f1-1db6-4b57-be54-1821339b85f7"); dx12 = D3D12CreateDevice(adapter, 0xb000, ref iid, IntPtr.Zero) >= 0; }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
                return (dx12 ? "12" : fl >= 0xb000 ? "11" : "10") + " (" + fls + ")";
            }
            catch (Exception ex) { ErrorLog.Write("DirectXVersion", ex); return null; }
        }

        static int Open(long luid)
        {
            IntPtr p = Marshal.AllocHGlobal(12);
            try
            {
                *(long*)p = luid; *(int*)((byte*)p + 8) = 0;
                return D3DKMTOpenAdapterFromLuid(p) == 0 ? *(int*)((byte*)p + 8) : 0;
            }
            finally { Marshal.FreeHGlobal(p); }
        }

        static void Close(int h)
        {
            IntPtr p = Marshal.AllocHGlobal(4);
            *(int*)p = h; D3DKMTCloseAdapter(p); Marshal.FreeHGlobal(p);
        }

        static bool Query(int h, int type, IntPtr data, int size)
        {
            IntPtr q = Marshal.AllocHGlobal(24);
            try
            {
                *(int*)q = h; *(int*)((byte*)q + 4) = type; *(IntPtr*)((byte*)q + 8) = data; *(int*)((byte*)q + 16) = size;
                return D3DKMTQueryAdapterInfo(q) == 0;
            }
            finally { Marshal.FreeHGlobal(q); }
        }

        public static void Fill(GpuStatic a)
        {
            int h = 0;
            try { h = Open(a.Luid); } catch (Exception ex) { ErrorLog.Write("D3DKMTOpen", ex); }
            if (h != 0)
            {
                IntPtr buf = Marshal.AllocHGlobal(512);
                try
                {
                    if (Query(h, 6 /*KMTQAITYPE_ADAPTERADDRESS*/, buf, 12))
                        a.Location = "PCI bus " + *(int*)buf + ", device " + *(int*)((byte*)buf + 4) + ", function " + *(int*)((byte*)buf + 8);
                    // engine friendly names from node metadata
                    var names = new List<string>();
                    for (int node = 0; node < 64; node++)
                    {
                        for (int i = 0; i < 512; i += 8) *(long*)((byte*)buf + i) = 0;
                        *(int*)buf = node;   // NodeOrdinalAndAdapterIndex (adapter 0)
                        if (!Query(h, 25 /*KMTQAITYPE_NODEMETADATA*/, buf, 4 + 4 + 64 + 8)) break;
                        var n = new string((char*)((byte*)buf + 8)).Trim();
                        if (n.Length == 0)
                        {
                            // no friendly name: fall back to the engine type (DXGK_ENGINE_TYPE)
                            string[] types = { "Other", "3D", "Video Decode", "Video Encode", "Video Processing", "Scene Assembly", "Copy", "Overlay", "Crypto" };
                            int et = *(int*)((byte*)buf + 4);
                            n = et >= 0 && et < types.Length ? types[et] : "Engine " + node;
                        }
                        names.Add(n);
                    }
                    if (names.Count > 0) { a.EngineNames = names.ToArray(); a.NamesFromDriver = true; }
                    // temperature support
                    *(int*)buf = 0;
                    if (Query(h, 62 /*KMTQAITYPE_ADAPTERPERFDATA*/, buf, 64) && *(int*)((byte*)buf + 56) > 0) a.PerfDataSupported = true;
                }
                finally { Marshal.FreeHGlobal(buf); Close(h); }
            }
            ReadDriverRegistry(a);
        }

        /// <summary>"VideoDecode" -> "Video Decode", "Compute_0" -> "Compute_0".</summary>
        public static string Spaced(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length + 4);
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && char.IsUpper(s[i]) && char.IsLower(s[i - 1])) sb.Append(' ');
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        public static int Temperature(GpuStatic a)
        {
            int h = Open(a.Luid);
            if (h == 0) return -1;
            IntPtr buf = Marshal.AllocHGlobal(64);
            try
            {
                for (int i = 0; i < 64; i += 8) *(long*)((byte*)buf + i) = 0;
                if (!Query(h, 62, buf, 64)) return -1;
                int t = *(int*)((byte*)buf + 56);
                return t > 0 ? (t + 5) / 10 : -1;
            }
            finally { Marshal.FreeHGlobal(buf); Close(h); }
        }

        static void ReadDriverRegistry(GpuStatic a)
        {
            try
            {
                string id = "ven_" + a.VendorId.ToString("x4") + "&dev_" + a.DeviceId.ToString("x4");
                using (var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (cls == null) return;
                    foreach (var sub in cls.GetSubKeyNames())
                    {
                        if (sub.Length != 4) continue;
                        using (var k = cls.OpenSubKey(sub))
                        {
                            var match = (k?.GetValue("MatchingDeviceId") as string)?.ToLowerInvariant();
                            if (match == null || !match.Contains(id)) continue;
                            a.DriverVersion = k.GetValue("DriverVersion") as string;
                            var date = k.GetValue("DriverDate") as string;
                            if (date != null && DateTime.TryParseExact(date, new[] { "M-d-yyyy", "MM-dd-yyyy" }, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt))
                                a.DriverDate = dt.ToShortDateString();
                            else a.DriverDate = date;
                            long mem = 0;
                            var q = k.GetValue("HardwareInformation.qwMemorySize");
                            if (q is long l) mem = l; else if (q is byte[] qb && qb.Length >= 8) mem = BitConverter.ToInt64(qb, 0);
                            if (mem == 0)
                            {
                                var d = k.GetValue("HardwareInformation.MemorySize");
                                if (d is int di) mem = (uint)di; else if (d is byte[] db && db.Length >= 4) mem = BitConverter.ToUInt32(db, 0);
                            }
                            if (mem > a.DedicatedLimit) a.HwReserved = mem - a.DedicatedLimit;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex) { ErrorLog.Write("GPU driver registry", ex); }
        }
    }
}
