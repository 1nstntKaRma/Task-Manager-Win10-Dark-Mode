using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TaskManagerDark
{
    /// <summary>
    /// Reads NtQuerySystemInformation(SystemProcessInformation) into a reusable buffer and maintains the
    /// ProcInfo objects. Runs on the sampler thread only.
    /// </summary>
    public sealed unsafe class ProcessSampler : IDisposable
    {
        IntPtr _buf;
        int _bufSize = 1 << 20;
        readonly Dictionary<int, ProcInfo> _byPid = new Dictionary<int, ProcInfo>();
        int _generation;
        long _prevTime;
        public readonly int CpuCount;
        public List<ProcInfo> Current = new List<ProcInfo>();
        public readonly List<ProcInfo> Added = new List<ProcInfo>();
        public readonly List<ProcInfo> Removed = new List<ProcInfo>();
        public int TotalThreads, TotalHandles, ProcessCount;
        /// <summary>Thread id -> process id, filled when <see cref="WantThreadMap"/> is set (ETW disk attribution).</summary>
        public readonly Dictionary<int, int> ThreadToPid = new Dictionary<int, int>();
        public bool WantThreadMap;
        public double LastMs;

        public ProcessSampler()
        {
            CpuCount = Math.Max(1, Environment.ProcessorCount);
            _buf = Marshal.AllocHGlobal(_bufSize);
        }

        public ProcInfo Find(int pid) => _byPid.TryGetValue(pid, out var p) ? p : null;

        public bool Sample()
        {
            long t0 = Sys.Qpc();
            int status;
            while (true)
            {
                status = Sys.NtQuerySystemInformation(Sys.SystemProcessInformation, _buf, _bufSize, out int needed);
                if (status == Sys.STATUS_INFO_LENGTH_MISMATCH || status == Sys.STATUS_BUFFER_TOO_SMALL)
                {
                    Marshal.FreeHGlobal(_buf);
                    _bufSize = Math.Max(_bufSize * 2, needed + (1 << 16));
                    _buf = Marshal.AllocHGlobal(_bufSize);
                    continue;
                }
                break;
            }
            if (status != 0) return false;

            long now = Sys.Qpc();   // precise: the first interval after startup is short
            double elapsed = _prevTime == 0 ? 0 : (now - _prevTime) * 1e7 / Sys.QpcFrequency; // 100 ns
            _prevTime = now;
            _generation++;
            Added.Clear(); Removed.Clear();
            var list = new List<ProcInfo>(Current.Count + 16);
            int threads = 0, handles = 0;
            if (WantThreadMap) ThreadToPid.Clear();

            byte* p = (byte*)_buf;
            while (true)
            {
                int next = *(int*)p;
                int nThreads = *(int*)(p + 0x04);
                long privateWs = *(long*)(p + 0x08);
                int hardFaults = *(int*)(p + 0x10);
                long cycle = *(long*)(p + 0x18);
                long create = *(long*)(p + 0x20);
                long user = *(long*)(p + 0x28);
                long kernel = *(long*)(p + 0x30);
                ushort nameLen = *(ushort*)(p + 0x38);
                char* namePtr = *(char**)(p + 0x40);
                int basePri = *(int*)(p + 0x48);
                int pid = (int)*(long*)(p + 0x50);
                int ppid = (int)*(long*)(p + 0x58);
                int handleCount = *(int*)(p + 0x60);
                int session = *(int*)(p + 0x64);
                long virt = *(long*)(p + 0x78);
                int pageFaults = *(int*)(p + 0x80);
                long peakWs = *(long*)(p + 0x88);
                long ws = *(long*)(p + 0x90);
                long paged = *(long*)(p + 0xA0);
                long nonPaged = *(long*)(p + 0xB0);
                long pagefile = *(long*)(p + 0xB8);
                long readOps = *(long*)(p + 0xD0), writeOps = *(long*)(p + 0xD8), otherOps = *(long*)(p + 0xE0);
                long readBytes = *(long*)(p + 0xE8), writeBytes = *(long*)(p + 0xF0), otherBytes = *(long*)(p + 0xF8);

                if (!_byPid.TryGetValue(pid, out var info) || info.CreateTime != create)
                {
                    if (info != null) { Removed.Add(info); info.Exited = true; }
                    string name = pid == 0 ? "System Idle Process" : nameLen > 0 && namePtr != null ? new string(namePtr, 0, nameLen / 2) : (pid == 4 ? "System" : "?");
                    info = new ProcInfo(pid, create, name) { FirstSeenTick = Environment.TickCount };
                    _byPid[pid] = info;
                    Added.Add(info);
                }
                info.SeenGeneration = _generation;

                ref ProcDyn d = ref info.Next;
                d.ParentPid = ppid; d.SessionId = session; d.BasePriority = basePri; d.Threads = nThreads; d.Handles = handleCount;
                d.KernelTime = kernel; d.UserTime = user; d.CycleTime = cycle;
                d.WorkingSet = ws; d.PeakWorkingSet = peakWs; d.PrivateWS = privateWs; d.Commit = pagefile;
                d.PagedPool = paged; d.NonPagedPool = nonPaged; d.VirtualSize = virt; d.PageFaults = pageFaults; d.HardFaults = hardFaults;
                d.IoReadOps = readOps; d.IoWriteOps = writeOps; d.IoOtherOps = otherOps;
                d.IoReadBytes = readBytes; d.IoWriteBytes = writeBytes; d.IoOtherBytes = otherBytes;

                long cpu = kernel + user;
                long io = readBytes + writeBytes + otherBytes;
                if (info.HasPrev && elapsed > 0)
                {
                    double c = (cpu - info.PrevCpu) * 100.0 / (elapsed * CpuCount);
                    d.Cpu = c < 0 ? 0 : c > 100 ? 100 : c;
                    d.WsDelta = ws - info.PrevWs;
                    d.PfDelta = pageFaults - info.PrevPf;
                    d.DiskBps = Math.Max(0, (io - info.PrevIo) * 1e7 / elapsed);
                }
                else { d.Cpu = 0; d.WsDelta = 0; d.PfDelta = 0; d.DiskBps = 0; }
                info.PrevCpu = cpu; info.PrevWs = ws; info.PrevPf = pageFaults; info.PrevIo = io; info.HasPrev = true;

                // threads: suspended detection (+ optional tid -> pid map)
                byte* th = p + 0x100;
                int suspended = 0;
                for (int i = 0; i < nThreads; i++, th += 0x50)
                {
                    int state = *(int*)(th + 0x44), reason = *(int*)(th + 0x48);
                    if (state == 5 && reason == 5) suspended++;
                    if (WantThreadMap) ThreadToPid[(int)*(long*)(th + 0x30)] = pid;
                }
                d.Suspended = nThreads > 0 && suspended == nThreads && pid > 4;

                threads += nThreads; handles += handleCount;
                list.Add(info);
                if (next == 0) break;
                p += next;
            }

            // processes that disappeared
            {
                var dead = new List<int>();
                foreach (var kv in _byPid) if (kv.Value.SeenGeneration != _generation) dead.Add(kv.Key);
                foreach (var pid in dead) { var info = _byPid[pid]; info.Exited = true; Removed.Add(info); _byPid.Remove(pid); }
            }
            Current = list;
            TotalThreads = threads; TotalHandles = handles; ProcessCount = list.Count - 1; // without the Idle process
            LastMs = (Sys.Qpc() - t0) * 1000.0 / Sys.QpcFrequency;
            return true;
        }

        public void Dispose()
        {
            if (_buf != IntPtr.Zero) { Marshal.FreeHGlobal(_buf); _buf = IntPtr.Zero; }
        }
    }
}
