using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace TaskManagerDark
{
    /// <summary>Fixed-size history of samples for a graph (the graphs show the last 60 updates).</summary>
    public sealed class Series
    {
        public const int Capacity = 60;
        readonly float[] _v = new float[Capacity];
        int _count, _head;
        public int Count => _count;
        public void Push(double v) { _v[_head] = (float)v; _head = (_head + 1) % Capacity; if (_count < Capacity) _count++; }
        /// <summary>i = 0 is the oldest kept sample, Count-1 the newest.</summary>
        public float this[int i] => _v[(_head - _count + i + Capacity * 2) % Capacity];
        public float Last => _count == 0 ? 0 : this[_count - 1];
        public float Max { get { float m = 0; for (int i = 0; i < _count; i++) if (this[i] > m) m = this[i]; return m; } }
        public void Clear() { _count = 0; _head = 0; }
    }

    public sealed class DiskState { public DiskStatic Static; public DiskSample Last; public readonly Series Active = new Series(), Read = new Series(), Write = new Series(); public bool Alive; }
    public sealed class NetState { public NetStatic Static; public NetSample Last; public readonly Series Send = new Series(), Recv = new Series(); public bool Alive; public NetSample First; }
    public sealed class GpuState
    {
        public GpuStatic Static; public GpuSample Last;
        public readonly List<Series> Engines = new List<Series>();
        public readonly Series Max = new Series(), Dedicated = new Series(), Shared = new Series();
    }

    /// <summary>
    /// Samples the system on a background thread at the update speed and publishes each sample on the UI thread.
    /// Everything the UI reads (ProcInfo.Cur, histories, lists) is written only on the UI thread.
    /// </summary>
    public sealed class DataEngine : IDisposable
    {
        sealed class Packet
        {
            public List<ProcInfo> Procs; public List<ProcInfo> Added, Removed;
            public Dictionary<int, List<WinInfo>> Windows;
            public List<SvcInfo> Services;
            public SysSample Sys;
            public double Ms;
        }

        Control _ui;
        readonly Thread _thread;
        readonly AutoResetEvent _wake = new AutoResetEvent(false), _applied = new AutoResetEvent(false);
        volatile bool _stop, _refreshRequested;
        volatile int _interval = 1000;
        readonly StaticLoader _static;
        readonly ConcurrentQueue<KeyValuePair<ProcInfo, ProcStatic>> _staticDone = new ConcurrentQueue<KeyValuePair<ProcInfo, ProcStatic>>();
        int _staticPosted;
        volatile bool _uiReady;             // the window handle exists: results can be posted to it
        // the first sample goes straight to the window while it is being created (no message round trip)
        readonly object _firstLock = new object();
        Packet _firstPk;
        bool _firstHandoff = true;

        // published (UI thread)
        public List<ProcInfo> Procs = new List<ProcInfo>();
        public Dictionary<int, ProcInfo> ProcByPid = new Dictionary<int, ProcInfo>();
        public Dictionary<int, List<WinInfo>> Windows = new Dictionary<int, List<WinInfo>>();
        public List<SvcInfo> Services = new List<SvcInfo>();
        public Dictionary<int, List<SvcInfo>> ServicesByPid = new Dictionary<int, List<SvcInfo>>();
        public SysSample Sys = new SysSample { PerCpu = new double[0], PerCpuKernel = new double[0], Disks = new List<DiskSample>(), Nets = new List<NetSample>(), Gpus = new List<GpuSample>() };
        public CpuStatic Cpu;
        public MemStatic Mem = new MemStatic();
        public readonly Series CpuTotal = new Series(), CpuKernel = new Series(), MemUsed = new Series();
        public readonly List<Series> PerCpu = new List<Series>(), PerCpuKernel = new List<Series>();
        public readonly List<DiskState> Disks = new List<DiskState>();
        public readonly List<NetState> Nets = new List<NetState>();
        public readonly List<GpuState> Gpus = new List<GpuState>();
        /// <summary>Per-process disk and network figures come from ETW (elevated). Set before the session is up, so the
        /// columns do not flash "N/A"; cleared when the session cannot start.</summary>
        public volatile bool EtwActive; public string EtwError;
        public int Tick;
        public double LastSampleMs, LastApplyMs;
        public string SampleBreakdown = "";
        public int ProcessCount, ThreadCount, HandleCount;

        public event Action Updated;        // after a sample was applied
        public event Action StaticUpdated;  // static info of some processes arrived

        // sampler-thread objects
        ProcessSampler _procs;
        CpuMemSampler _cpuMem;
        CounterSampler _counters;
        DiskSampler _disk;
        NetSampler _net;
        WindowScanner _windows;
        ServiceScanner _services;
        volatile EtwCollector _etw;
        readonly object _etwLock = new object();
        bool _shutDown;
        bool _memLists;                     // SystemMemoryListInformation works (elevated)
        long _prevEtwTick;
        int _serviceTick;
        public volatile bool WantGuiResources, WantIoPriority, WantServicesLive;
        public IntPtr OwnWindow;
        public int IconSize = 16;
        /// <summary>Test switch: delay before the first sample is taken (proves the window does not wait forever).</summary>
        public static int DebugFirstDelayMs;

        /// <summary>
        /// Sampling starts right here, before the window is built, and runs in parallel with it. Icons are loaded at
        /// <paramref name="iconSize"/>, the small-icon size for the monitor the window will open on.
        /// </summary>
        public DataEngine(int iconSize = 16)
        {
            IconSize = iconSize;
            Cpu = CpuStatic.Load();
            for (int i = 0; i < Cpu.Logical; i++) { PerCpu.Add(new Series()); PerCpuKernel.Add(new Series()); }
            _static = new StaticLoader(OnStaticLoaded);
            _static.Begin(iconSize);
            _thread = new Thread(Loop) { IsBackground = true, Name = "Sampler", Priority = ThreadPriority.AboveNormal };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        /// <summary>The window that receives the results (it only has to exist when <see cref="Start"/> is called).</summary>
        public void Attach(Control ui) => _ui = ui;

        /// <summary>Called once the window handle exists: results may now be posted to it.</summary>
        public void Start()
        {
            _static.IconSize = IconSize;
            _uiReady = true;
            Thread.MemoryBarrier();   // pairs with the loaders' enqueue-then-check: one side always sees the other
            if (!_staticDone.IsEmpty) OnStaticLoaded(null, null);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var m = MemStatic.Load();
                StartupLog.Mark("memory hardware");
                try { _ui.BeginInvoke(new Action(() => { Mem = m; Updated?.Invoke(); })); } catch (InvalidOperationException) { }
            });
        }

        /// <summary>
        /// UI thread, before the window is shown: waits up to <paramref name="ms"/> for the first sample and applies it,
        /// so the window opens with real figures. Later samples arrive through the message loop.
        /// </summary>
        public bool WaitFirstSample(int ms, int staticsMs)
        {
            Packet pk;
            lock (_firstLock)
            {
                long until = Environment.TickCount + ms;
                while (_firstPk == null && !_stop)
                {
                    int left = (int)(until - Environment.TickCount);
                    if (left <= 0) break;
                    Monitor.Wait(_firstLock, left);
                }
                pk = _firstPk; _firstPk = null; _firstHandoff = false;
            }
            StartupLog.Mark(pk != null ? "first sample taken by the window" : "first sample not ready, window shown without it");
            // names and icons of the running processes: usually done by now, so the first paint has them too
            bool all = _static.WaitInitial(staticsMs);
            StartupLog.Mark(all ? "process names and icons on the first paint" : "first paint without all process names");
            DrainStatic();
            if (pk == null) return false;
            Apply(pk);
            return true;
        }

        /// <summary>Update interval in ms; 0 pauses updates (Refresh now still works).</summary>
        public int Interval
        {
            get => _interval;
            set { if (_interval == value) return; _interval = value; _wake.Set(); }
        }

        public void RefreshNow() { _refreshRequested = true; _wake.Set(); }

        // ================================================================== sampler thread
        void Loop()
        {
            StartupLog.Mark("sampler thread");
            try { Init(); }
            catch (Exception ex) { ErrorLog.Write("DataEngine.Init", ex); }
            StartupLog.Mark("samplers created");
            long primed = TaskManagerDark.Sys.Qpc();
            try { Prime(); }
            catch (Exception ex) { ErrorLog.Write("DataEngine.Prime", ex); }
            StartupLog.Mark("baseline taken");
            // a short interval after the baseline, so the very first sample already has CPU and transfer rates
            int sinceBaseline = (int)((TaskManagerDark.Sys.Qpc() - primed) * 1000 / TaskManagerDark.Sys.QpcFrequency);
            if (sinceBaseline < 120) Thread.Sleep(120 - sinceBaseline);
            if (DebugFirstDelayMs > 0) Thread.Sleep(DebugFirstDelayMs);
            bool first = true;
            while (!_stop)
            {
                if (!first)
                {
                    int iv = _interval;
                    _wake.WaitOne(iv <= 0 ? Timeout.Infinite : iv);
                    if (_stop) break;
                    if (_interval <= 0 && !_refreshRequested) continue;
                }
                _refreshRequested = false;
                Packet pk = null;
                try { pk = Collect(first); }
                catch (Exception ex) { ErrorLog.Write("DataEngine.Collect", ex); }
                if (pk == null) { first = false; continue; }
                if (first)
                {
                    StartupLog.Mark("first sample ready (" + pk.Ms.ToString("0.0") + " ms)");
                    first = false;
                    if (!HandOff(pk))   // HandOff returns once the window applied it
                    {
                        if (!Post(pk)) break;
                        _applied.WaitOne(3000);
                    }
                    StartEtw();
                    continue;
                }
                if (!Post(pk)) break;
                _applied.WaitOne(3000);
            }
            Shutdown();
        }

        /// <summary>Gives the first sample to <see cref="WaitFirstSample"/> if the window has not given up on it.</summary>
        bool HandOff(Packet pk)
        {
            lock (_firstLock)
            {
                if (!_firstHandoff) return false;
                _firstPk = pk;
                Monitor.PulseAll(_firstLock);
            }
            // the window takes it while it is being created; if it never asks (startup failed), post it normally
            for (int i = 0; i < 20 && !_stop; i++) if (_applied.WaitOne(250)) return true;
            lock (_firstLock)
            {
                _firstHandoff = false;
                if (_firstPk == null) { _applied.WaitOne(3000); return true; }   // taken just now: let it be applied first
                _firstPk = null;
            }
            return false;
        }

        bool Post(Packet pk)
        {
            try { _ui.BeginInvoke(new Action(() => Apply(pk))); return true; }
            catch (InvalidOperationException) { return false; }   // window gone
        }

        void Init()
        {
            _procs = new ProcessSampler();
            _cpuMem = new CpuMemSampler();
            _memLists = MemoryLists.Read(out _, out _, out _);
            _counters = new CounterSampler(!_memLists);
            ThreadPool.QueueUserWorkItem(_ => _counters.Gpu.LoadAdapters());
            _disk = new DiskSampler();
            _net = new NetSampler();
            _windows = new WindowScanner();
            _services = new ServiceScanner();
            EtwActive = TaskManagerDark.Sys.IsElevated && !Program.NoEtw;
        }

        /// <summary>Baseline for every rate (CPU, per-process CPU and I/O, disks, network); starts the static info loading.</summary>
        void Prime()
        {
            _procs.Sample();
            foreach (var p in _procs.Added) _static.Enqueue(p);
            _static.EndInitialBatch();
            _cpuMem.SampleCpu(new SysSample());
            _disk.Sample();
            _net.Sample();
        }

        /// <summary>Per-process disk and network through a kernel trace session (elevated only), started off the update path.</summary>
        void StartEtw()
        {
            if (!EtwActive) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var etw = new EtwCollector();
                    if (etw.Start())
                    {
                        // a kernel trace session outlives its process, so one that comes up while closing is stopped here
                        lock (_etwLock)
                        {
                            if (_shutDown) { etw.Dispose(); return; }
                            _etw = etw;
                        }
                        StartupLog.Mark("ETW session running (flush " + etw.FlushMs + " ms)");
                    }
                    else { EtwError = etw.Error; EtwActive = false; StartupLog.Mark("ETW failed: " + etw.Error); }
                }
                catch (Exception ex) { EtwActive = false; EtwError = ex.Message; ErrorLog.Write("ETW start", ex); }
            });
        }

        Packet Collect(bool first)
        {
            long t0 = TaskManagerDark.Sys.Qpc();
            var stages = new System.Text.StringBuilder();
            long ts = t0;
            Action<string> mark = name => { long now2 = TaskManagerDark.Sys.Qpc(); stages.Append(name).Append('=').Append(((now2 - ts) * 1000.0 / TaskManagerDark.Sys.QpcFrequency).ToString("0.0")).Append(' '); ts = now2; };
            int kick = _counters.Kick();
            var pk = new Packet();
            var etw = _etw;
            if (etw != null) _procs.WantThreadMap = true;
            _procs.Sample();
            mark("procs");
            foreach (var p in _procs.Added) _static.Enqueue(p);
            foreach (var p in _procs.Removed) StaticLoader.Release(p);
            pk.Procs = _procs.Current; pk.Added = new List<ProcInfo>(_procs.Added); pk.Removed = new List<ProcInfo>(_procs.Removed);

            // per-process disk / network from ETW
            if (etw != null && etw.Running)
            {
                long now = TaskManagerDark.Sys.Qpc();
                double dt = _prevEtwTick == 0 ? 0 : (now - _prevEtwTick) / (double)TaskManagerDark.Sys.QpcFrequency;
                _prevEtwTick = now;
                etw.Drain(out var disk, out var net);
                if (dt > 0)   // the first drain only starts the interval
                {
                    var diskByPid = new Dictionary<int, long>();
                    foreach (var kv in disk)
                        if (_procs.ThreadToPid.TryGetValue(kv.Key, out int pid)) { diskByPid.TryGetValue(pid, out long v); diskByPid[pid] = v + kv.Value; }
                    foreach (var p in pk.Procs)
                    {
                        diskByPid.TryGetValue(p.Pid, out long db); net.TryGetValue(p.Pid, out long nb);
                        p.Next.DiskBps = db / dt;
                        p.Next.NetBps = nb * 8 / dt;
                    }
                }
            }

            mark("etw");
            var s = new SysSample();
            _cpuMem.SampleCpu(s);
            _cpuMem.SampleMemory(s);
            mark("cpumem");
            s.Processes = _procs.ProcessCount; s.Threads = _procs.TotalThreads; s.Handles = _procs.TotalHandles;
            s.Disks = _disk.Sample(); mark("disk");
            s.Nets = _net.Sample(); mark("net");
            foreach (var p in pk.Procs) if (p.Name == "Memory Compression") s.Compressed = p.Next.WorkingSet;
            foreach (var d in s.Disks) s.DiskMaxActive = Math.Max(s.DiskMaxActive, d.Active);
            foreach (var n in s.Nets)
                if (n.Connected && n.LinkBps > 0) { s.NetPrimaryUtil = Math.Min(100, Math.Max(n.SendBps, n.RecvBps) * 100.0 / n.LinkBps); break; }

            _windows.OwnMainWindow = OwnWindow;
            pk.Windows = _windows.Scan();
            mark("windows");
            // services change rarely: every update while the Services tab is open, otherwise every 5th
            bool scanServices = WantServicesLive || _serviceTick++ % 5 == 0;
            if (scanServices && _services.Scan()) _services.LoadConfigs(Tick < 2 ? 0 : WantServicesLive ? 60 : 25);
            mark("services");
            if (scanServices && (_services.Changed || Tick == 0))
            {
                var copy = new List<SvcInfo>(_services.Current.Count);
                foreach (var sv in _services.Current) copy.Add(sv.Clone());
                pk.Services = copy;
            }
            if (WantGuiResources || WantIoPriority)
                foreach (var p in pk.Procs)
                    lock (p)
                    {
                        if (p.Handle == IntPtr.Zero || p.Exited) continue;
                        if (WantGuiResources) { p.Next.GuiGdi = TaskManagerDark.Sys.GetGuiResources(p.Handle, 0); p.Next.GuiUser = TaskManagerDark.Sys.GetGuiResources(p.Handle, 1); }
                        if (WantIoPriority && TaskManagerDark.Sys.NtQueryInformationProcess(p.Handle, TaskManagerDark.Sys.ProcessIoPriority, out int iop, 4, out _) == 0) p.Next.IoPriority = iop;
                    }
            mark("extra");

            // performance counters: this update's collection if it is done in time, otherwise the previous one
            var cs = _counters.Wait(kick, first ? 0 : 40);
            mark("counters");
            MergeCounters(s, cs, pk.Procs);
            SampleBreakdown = stages.ToString() + (cs != null ? "(pdh=" + cs.Ms.ToString("0.0") + " seq=" + cs.Seq + "/" + kick + ")" : "(pdh pending)");
            pk.Sys = s;
            pk.Ms = s.SampleMs = (TaskManagerDark.Sys.Qpc() - t0) * 1000.0 / TaskManagerDark.Sys.QpcFrequency;
            return pk;
        }

        void MergeCounters(SysSample s, CounterSnapshot cs, List<ProcInfo> procs)
        {
            double perf = cs?.PerfPct ?? double.NaN;
            s.SpeedMhz = !double.IsNaN(perf) && Cpu.BaseMhz > 0 ? perf / 100.0 * Cpu.BaseMhz : CpuStatic.CurrentMhz(Cpu);

            // memory lists: straight from the kernel when elevated, otherwise from the counters
            bool lists = false;
            if (_memLists && MemoryLists.Read(out s.Free, out s.Modified, out s.Standby)) lists = true;
            else if (cs != null && cs.MemValid) { s.Free = cs.Free; s.Modified = cs.Modified; s.Standby = cs.Standby; lists = true; }
            if (lists)
            {
                s.Cached = s.Standby + s.Modified;
                s.MemInUse = Math.Max(0, s.MemTotal - s.Free - s.Standby - s.Modified);
            }
            else
            {
                // counters not ready yet (first moments of a cold start): the system cache stands in for standby
                s.MemInUse = Math.Max(0, s.MemTotal - s.MemAvail);
                s.Standby = Math.Min(s.Cached, s.MemAvail);
                s.Free = Math.Max(0, s.MemAvail - s.Standby);
                s.Modified = 0;
            }

            s.Gpus = cs?.Gpus ?? _counters.Gpu.Placeholder() ?? new List<GpuSample>();
            foreach (var g in s.Gpus) s.GpuMax = Math.Max(s.GpuMax, g.Max);
            var pg = cs?.ProcGpu;
            if (pg != null)
                foreach (var p in procs)
                {
                    if (pg.TryGetValue(p.Pid, out var gp))
                    {
                        p.Next.Gpu = gp.Max; p.Next.GpuEngine = gp.EngineKey;
                        p.Next.GpuDedicated = gp.Dedicated; p.Next.GpuShared = gp.Shared;
                    }
                    else { p.Next.Gpu = 0; p.Next.GpuEngine = -1; p.Next.GpuDedicated = 0; p.Next.GpuShared = 0; }
                }
            else
                foreach (var p in procs) if (p.Next.Gpu == 0) p.Next.GpuEngine = -1;
        }

        void Shutdown()
        {
            lock (_etwLock)
            {
                _shutDown = true;
                try { _etw?.Dispose(); } catch (Exception ex) { ErrorLog.Write("ETW shutdown", ex); }
            }
            try { _counters?.Dispose(); _procs?.Dispose(); _services?.Dispose(); _net?.Dispose(); _disk?.Dispose(); } catch (Exception ex) { ErrorLog.Write("Sampler shutdown", ex); }
        }

        // ================================================================== UI thread
        void Apply(Packet pk)
        {
            long t0 = TaskManagerDark.Sys.Qpc();
            try
            {
                foreach (var p in pk.Procs) p.Cur = p.Next;
                Procs = pk.Procs;
                var byPid = new Dictionary<int, ProcInfo>(Procs.Count);
                foreach (var p in Procs) byPid[p.Pid] = p;
                ProcByPid = byPid;
                Windows = pk.Windows;
                if (pk.Services != null)
                {
                    Services = pk.Services;
                    var bp = new Dictionary<int, List<SvcInfo>>();
                    foreach (var s in Services)
                        if (s.Pid != 0) { if (!bp.TryGetValue(s.Pid, out var l)) { l = new List<SvcInfo>(); bp[s.Pid] = l; } l.Add(s); }
                    ServicesByPid = bp;
                }
                var sys = pk.Sys;
                Sys = sys;
                ProcessCount = sys.Processes; ThreadCount = sys.Threads; HandleCount = sys.Handles;
                CpuTotal.Push(sys.Cpu); CpuKernel.Push(sys.CpuKernel);
                for (int i = 0; i < PerCpu.Count && i < sys.PerCpu.Length; i++) { PerCpu[i].Push(sys.PerCpu[i]); PerCpuKernel[i].Push(sys.PerCpuKernel[i]); }
                MemUsed.Push(sys.MemInUse);
                ApplyDisks(sys); ApplyNets(sys); ApplyGpus(sys);
                ApplyPower();
                Tick++;
                LastSampleMs = pk.Ms;
            }
            catch (Exception ex) { ErrorLog.Write("DataEngine.Apply", ex); }
            finally { _applied.Set(); }
            LastApplyMs = (TaskManagerDark.Sys.Qpc() - t0) * 1000.0 / TaskManagerDark.Sys.QpcFrequency;
            try { Updated?.Invoke(); }
            catch (Exception ex) { Program.ReportError(ex, false); }
        }

        void ApplyDisks(SysSample sys)
        {
            foreach (var d in Disks) d.Alive = false;
            foreach (var smp in sys.Disks)
            {
                var st = Disks.Find(x => x.Static.Number == smp.Number);
                if (st == null)
                {
                    if (smp.Static == null) continue;
                    st = new DiskState { Static = smp.Static };
                    Disks.Add(st);
                    Disks.Sort((a, b) => a.Static.Number.CompareTo(b.Static.Number));
                }
                st.Alive = true; st.Last = smp;
                st.Active.Push(smp.Active); st.Read.Push(smp.ReadBps); st.Write.Push(smp.WriteBps);
            }
            Disks.RemoveAll(d => !d.Alive);
        }

        void ApplyNets(SysSample sys)
        {
            foreach (var n in Nets) n.Alive = false;
            foreach (var smp in sys.Nets)
            {
                var st = Nets.Find(x => x.Static.Luid == smp.Luid);
                if (st == null)
                {
                    if (smp.Static == null) continue;
                    st = new NetState { Static = smp.Static, First = smp };
                    Nets.Add(st);
                }
                st.Alive = true; st.Last = smp;
                st.Send.Push(smp.SendBps); st.Recv.Push(smp.RecvBps);
            }
            Nets.RemoveAll(n => !n.Alive);
            Nets.Sort((a, b) => a.Static.Order.CompareTo(b.Static.Order));
        }

        void ApplyGpus(SysSample sys)
        {
            foreach (var smp in sys.Gpus)
            {
                var st = Gpus.Find(x => x.Static.Luid == smp.Luid);
                if (st == null)
                {
                    var gs = Array.Find(_counters.Gpu.Adapters, a => a.Luid == smp.Luid);
                    if (gs == null) continue;
                    st = new GpuState { Static = gs };
                    Gpus.Add(st);
                }
                st.Last = smp;
                while (st.Engines.Count < smp.Engines.Length) st.Engines.Add(new Series());
                for (int i = 0; i < smp.Engines.Length; i++) st.Engines[i].Push(smp.Engines[i]);
                st.Max.Push(smp.Max); st.Dedicated.Push(smp.Dedicated); st.Shared.Push(smp.Shared);
            }
        }

        /// <summary>Power usage estimate per process from its CPU, disk and GPU load (and a 2 minute trend).</summary>
        void ApplyPower()
        {
            double alpha = Math.Min(1.0, Math.Max(500, _interval <= 0 ? 1000 : _interval) / 120000.0);
            foreach (var p in Procs)
            {
                double e = PowerScore(p.Cur);
                if (!p.PowerEmaInit) { p.PowerEma = e; p.PowerEmaInit = true; }
                else p.PowerEma += (e - p.PowerEma) * alpha;
            }
        }

        public static double PowerScore(ProcDyn d) => d.Cpu + d.Gpu * 0.25 + d.DiskBps / (1024.0 * 1024.0) * 0.5;

        public static int PowerLevel(double score) => score < 0.5 ? 0 : score < 3 ? 1 : score < 10 ? 2 : score < 30 ? 3 : 4;

        public string GpuEngineName(int key) => _counters?.Gpu.EngineName(key) ?? "";

        void OnStaticLoaded(ProcInfo p, ProcStatic s)
        {
            if (p != null) _staticDone.Enqueue(new KeyValuePair<ProcInfo, ProcStatic>(p, s));
            // before the window exists the results wait in the queue; Start and WaitFirstSample pick them up
            if (_uiReady && Interlocked.Exchange(ref _staticPosted, 1) == 0)
            {
                try { _ui.BeginInvoke(new Action(DrainStatic)); }
                catch (InvalidOperationException) { Interlocked.Exchange(ref _staticPosted, 0); }
            }
        }

        void DrainStatic()
        {
            Interlocked.Exchange(ref _staticPosted, 0);
            int n = 0;
            while (_staticDone.TryDequeue(out var kv)) { kv.Key.Static = kv.Value; n++; }
            if (n > 0) { try { StaticUpdated?.Invoke(); } catch (Exception ex) { Program.ReportError(ex, false); } }
        }

        public void Dispose()
        {
            _stop = true;
            _wake.Set();
            _applied.Set();
            if (!_thread.Join(3000))
            {
                // the sampler is stuck in a call: still stop the trace session, it would outlive this process
                lock (_etwLock)
                {
                    if (!_shutDown) { _shutDown = true; try { _etw?.Dispose(); } catch (Exception ex) { ErrorLog.Write("ETW shutdown", ex); } }
                }
            }
            _static?.Dispose();
        }
    }
}
