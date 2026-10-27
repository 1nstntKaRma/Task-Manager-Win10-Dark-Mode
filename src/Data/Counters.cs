using System;
using System.Collections.Generic;
using System.Threading;

namespace TaskManagerDark
{
    /// <summary>One immutable result of the counters thread.</summary>
    public sealed class CounterSnapshot
    {
        public int Seq;
        public double PerfPct = double.NaN;               // \Processor Information(_Total)\% Processor Performance
        public bool MemValid; public long Modified, Standby, Free;
        public List<GpuSample> Gpus;                      // null until the GPU counters produced data
        public Dictionary<int, GpuProc> ProcGpu;
        public double Ms;
    }

    /// <summary>
    /// The performance counters (PDH) on their own thread. Opening PDH and the first collections can take seconds on a
    /// cold system (counter providers are loaded on first use), so the update loop never waits for them: it kicks this
    /// thread at the start of every update and uses whatever finished in time, at most a short moment later.
    /// </summary>
    public sealed class CounterSampler : IDisposable
    {
        readonly Thread _thread;
        readonly AutoResetEvent _kick = new AutoResetEvent(false);
        readonly object _lock = new object();
        readonly bool _wantMemory;
        volatile bool _stop;
        volatile CounterSnapshot _latest;
        int _kicks;
        public readonly GpuSampler Gpu = new GpuSampler();
        /// <summary>Test switch: extra delay before the counters start, to prove nothing else waits for them.</summary>
        public static int DebugDelayMs;

        public CounterSampler(bool wantMemory)
        {
            _wantMemory = wantMemory;
            _thread = new Thread(Run) { IsBackground = true, Name = "Counters", Priority = ThreadPriority.BelowNormal };
            _thread.Start();
        }

        public CounterSnapshot Latest => _latest;

        /// <summary>Asks for a fresh collection; returns the sequence number that will satisfy it.</summary>
        public int Kick()
        {
            int k = Interlocked.Increment(ref _kicks);
            _kick.Set();
            return k;
        }

        /// <summary>The newest snapshot, waiting up to <paramref name="ms"/> for the one answering kick <paramref name="seq"/>.</summary>
        public CounterSnapshot Wait(int seq, int ms)
        {
            var s = _latest;
            if (s != null && s.Seq >= seq || ms <= 0) return s;
            long until = Environment.TickCount + ms;
            lock (_lock)
            {
                while (!_stop)
                {
                    s = _latest;
                    if (s != null && s.Seq >= seq) break;
                    int left = (int)(until - Environment.TickCount);
                    if (left <= 0) break;
                    Monitor.Wait(_lock, left);
                }
            }
            return _latest;
        }

        void Run()
        {
            PdhQuery sysQ = null, gpuQ = null;
            try
            {
                if (DebugDelayMs > 0) Thread.Sleep(DebugDelayMs);
                // CPU speed and the memory lists first: few counters, ready quickly
                sysQ = new PdhQuery();
                IntPtr perf = sysQ.Add(@"\Processor Information(_Total)\% Processor Performance");
                IntPtr mod = IntPtr.Zero, sCore = IntPtr.Zero, sNorm = IntPtr.Zero, sRes = IntPtr.Zero, free = IntPtr.Zero;
                if (_wantMemory)
                {
                    mod = sysQ.Add(@"\Memory\Modified Page List Bytes");
                    sCore = sysQ.Add(@"\Memory\Standby Cache Core Bytes");
                    sNorm = sysQ.Add(@"\Memory\Standby Cache Normal Priority Bytes");
                    sRes = sysQ.Add(@"\Memory\Standby Cache Reserve Bytes");
                    free = sysQ.Add(@"\Memory\Free & Zero Page List Bytes");
                }
                sysQ.Collect();
                StartupLog.Mark("counters: system query open");
                Publish(Build(sysQ, perf, mod, sCore, sNorm, sRes, free, null, 0, 0));
                // GPU counters in their own query: they are the slow ones
                gpuQ = new PdhQuery();
                Gpu.AddCounters(gpuQ);
                gpuQ.Collect();
                StartupLog.Mark("counters: GPU query open");
                // a quick second collection so rates (speed, GPU engines) exist right away
                Thread.Sleep(100);
                bool primed = false;
                while (!_stop)
                {
                    if (primed) _kick.WaitOne();
                    if (_stop) break;
                    int seq = Volatile.Read(ref _kicks);
                    long t0 = Sys.Qpc();
                    sysQ.Collect();
                    gpuQ.Collect();
                    Publish(Build(sysQ, perf, mod, sCore, sNorm, sRes, free, Gpu, seq, t0));
                    if (!primed) { primed = true; StartupLog.Mark("counters: first rates"); }
                }
            }
            catch (Exception ex) { ErrorLog.Write("Counters thread", ex); }
            finally
            {
                try { gpuQ?.Dispose(); sysQ?.Dispose(); }
                catch (Exception ex) { ErrorLog.Write("Counters shutdown", ex); }
            }
        }

        CounterSnapshot Build(PdhQuery q, IntPtr perf, IntPtr mod, IntPtr sCore, IntPtr sNorm, IntPtr sRes, IntPtr free, GpuSampler gpu, int seq, long t0)
        {
            var s = new CounterSnapshot { Seq = seq };
            s.PerfPct = q.Value(perf);
            if (_wantMemory)
            {
                double m = q.Value(mod), c = q.Value(sCore), n = q.Value(sNorm), r = q.Value(sRes), f = q.Value(free);
                if (!double.IsNaN(m) && !double.IsNaN(f))
                {
                    s.MemValid = true;
                    s.Modified = (long)m; s.Free = (long)f;
                    s.Standby = (long)(Zero(c) + Zero(n) + Zero(r));
                }
            }
            if (gpu != null && gpu.AdaptersKnown)
            {
                s.Gpus = gpu.Sample(out var procs);
                s.ProcGpu = procs;
            }
            if (t0 != 0) s.Ms = (Sys.Qpc() - t0) * 1000.0 / Sys.QpcFrequency;
            return s;
        }

        static double Zero(double v) => double.IsNaN(v) ? 0 : v;

        void Publish(CounterSnapshot s)
        {
            // keep the GPU figures of the previous snapshot while the GPU query is not ready yet
            var prev = _latest;
            if (s.Gpus == null && prev != null) { s.Gpus = prev.Gpus; s.ProcGpu = prev.ProcGpu; }
            lock (_lock) { _latest = s; Monitor.PulseAll(_lock); }
        }

        public void Dispose()
        {
            _stop = true;
            _kick.Set();
            lock (_lock) Monitor.PulseAll(_lock);
            _thread.Join(2000);
        }
    }

    /// <summary>
    /// Startup timeline (milliseconds since the process was created), written to startup.log next to the settings.
    /// Overwritten at each start; it shows where the time goes before the first real data is on screen.
    /// </summary>
    public static class StartupLog
    {
        static readonly long _t0 = Sys.Qpc();
        static double _createdToMain = -1;
        static readonly List<string> _lines = new List<string>();
        static bool _done;

        /// <summary>Called first thing in Main: the time from process creation to here (loader, CLR start, JIT).</summary>
        public static void Begin()
        {
            try
            {
                if (Sys.GetProcessTimes(Sys.GetCurrentProcess(), out long created, out _, out _, out _))
                    _createdToMain = (DateTime.UtcNow.ToFileTimeUtc() - created) / 10000.0;
            }
            catch (Exception ex) { ErrorLog.Write("StartupLog.Begin", ex); }
            Mark("main");
        }

        public static double Now => (Sys.Qpc() - _t0) * 1000.0 / Sys.QpcFrequency + Math.Max(0, _createdToMain);

        public static void Mark(string what)
        {
            if (_done) return;
            string line = Now.ToString("0.0").PadLeft(8) + " ms  [" + Thread.CurrentThread.Name + "] " + what;
            lock (_lines) _lines.Add(line);
        }

        public static string Text { get { lock (_lines) return string.Join("\r\n", _lines); } }

        /// <summary>Writes the log; the last call stops recording.</summary>
        public static void Flush(bool final)
        {
            try
            {
                string text;
                lock (_lines)
                {
                    if (_done) return;
                    if (final) _done = true;
                    text = "Task Manager Dark startup timeline (ms since process creation)\r\n" + string.Join("\r\n", _lines) + "\r\n";
                }
                var dir = Settings.OverrideDir ?? Settings.DataDir;
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "startup.log"), text);
            }
            catch (Exception ex) { ErrorLog.Write("StartupLog.Flush", ex); }
        }
    }
}
