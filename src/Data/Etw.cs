using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace TaskManagerDark
{
    /// <summary>
    /// Real-time kernel trace (private "system logger" session, Windows 8+) for per-process disk and network bytes.
    /// Needs administrator rights; when it cannot start, the app falls back to I/O counters.
    /// </summary>
    public sealed unsafe class EtwCollector : IDisposable
    {
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "StartTraceW")] static extern int StartTrace(out long handle, string name, IntPtr props);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "ControlTraceW")] static extern int ControlTrace(long handle, string name, IntPtr props, int code);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "OpenTraceW")] static extern long OpenTrace(IntPtr logfile);
        [DllImport("advapi32.dll")] static extern int ProcessTrace(long[] handles, int count, IntPtr start, IntPtr end);
        [DllImport("advapi32.dll")] static extern int CloseTrace(long handle);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void EventRecordCallback(IntPtr record);

        public const string SessionName = "TaskManagerDark Kernel Trace";
        static readonly Guid DiskIoGuid = new Guid("3d6fa8d4-fe05-11d0-9dda-00c04fd7ba7c");
        static readonly Guid TcpIpGuid = new Guid("9a280ac0-c8e0-11d1-84e2-00c04fb998a2");
        static readonly Guid UdpIpGuid = new Guid("bf3a50c5-a9c9-4988-a005-2df0b7c80f80");

        readonly object _lock = new object();
        Dictionary<int, long> _diskByThread = new Dictionary<int, long>(), _netByPid = new Dictionary<int, long>();
        Dictionary<int, long> _spareDisk = new Dictionary<int, long>(), _spareNet = new Dictionary<int, long>();
        long _session, _trace;
        Thread _thread;
        EventRecordCallback _callback;   // kept alive for the native side
        IntPtr _logfile, _namePtr;
        public bool Running { get; private set; }
        public string Error;
        public long EventCount;
        public int FlushMs;

        public bool Start()
        {
            // the kernel hands events over in per-processor buffers when they fill or when the flush timer fires; a
            // 1 s timer beats against the 1 s update and makes per-process rates swing between 0 and 2-5x, so flush
            // every 200 ms (millisecond timer, Windows 8+), falling back to whole seconds where it is refused
            return Start(true) || Start(false);
        }

        bool Start(bool msFlush)
        {
            try
            {
                Stop(SessionName);   // a session left behind by a crash (or the first attempt)
                int size = 120 + (SessionName.Length + 1) * 2 + 16;
                IntPtr props = Marshal.AllocHGlobal(size);
                try
                {
                    for (int i = 0; i < size; i++) ((byte*)props)[i] = 0;
                    byte* p = (byte*)props;
                    *(int*)(p + 0) = size;                         // Wnode.BufferSize
                    *(int*)(p + 40) = 1;                           // ClientContext: QPC timestamps
                    *(int*)(p + 44) = 0x20000;                     // WNODE_FLAG_TRACED_GUID
                    *(Guid*)(p + 24) = Guid.NewGuid();
                    *(int*)(p + 48) = 16;                          // BufferSize (KB): flushed often, so small
                    *(int*)(p + 52) = 4; *(int*)(p + 56) = 64;     // Min / Max buffers
                    *(int*)(p + 64) = 0x100 | 0x2000000 | (msFlush ? 0x10 : 0);   // REAL_TIME | SYSTEM_LOGGER | USE_MS_FLUSH_TIMER
                    *(int*)(p + 68) = msFlush ? 200 : 1;           // FlushTimer: 200 ms (or 1 s)
                    *(int*)(p + 72) = 0x100 | 0x10000;             // EVENT_TRACE_FLAG_DISK_IO | EVENT_TRACE_FLAG_NETWORK_TCPIP
                    *(int*)(p + 116) = 120;                        // LoggerNameOffset
                    int r = StartTrace(out _session, SessionName, props);
                    if (r != 0) { Error = Sys.Win32Message(r); if (msFlush) ErrorLog.Write("ETW", new Exception("millisecond flush timer refused: " + Error)); return false; }
                    FlushMs = msFlush ? 200 : 1000;
                }
                finally { Marshal.FreeHGlobal(props); }

                _callback = OnEvent;
                _namePtr = Marshal.StringToHGlobalUni(SessionName);
                _logfile = Marshal.AllocHGlobal(448);
                for (int i = 0; i < 448; i++) ((byte*)_logfile)[i] = 0;
                *(IntPtr*)((byte*)_logfile + 8) = _namePtr;                                            // LoggerName
                *(int*)((byte*)_logfile + 28) = 0x100 | 0x10000000;                                    // REAL_TIME | EVENT_RECORD
                *(IntPtr*)((byte*)_logfile + 424) = Marshal.GetFunctionPointerForDelegate(_callback);  // EventRecordCallback
                _trace = OpenTrace(_logfile);
                if (_trace == -1 || _trace == 0) { Error = Sys.Win32Message(Marshal.GetLastWin32Error()); Stop(SessionName); return false; }
                _thread = new Thread(() =>
                {
                    try { ProcessTrace(new[] { _trace }, 1, IntPtr.Zero, IntPtr.Zero); }
                    catch (Exception ex) { ErrorLog.Write("ETW ProcessTrace", ex); }
                    Running = false;
                }) { IsBackground = true, Name = "ETW" };
                _thread.Start();
                Running = true;
                return true;
            }
            catch (Exception ex) { Error = ex.Message; ErrorLog.Write("ETW Start", ex); return false; }
        }

        void OnEvent(IntPtr rec)
        {
            byte* r = (byte*)rec;
            Guid provider = *(Guid*)(r + 24);
            int opcode = r[45];
            byte* data = *(byte**)(r + 96);
            int len = *(ushort*)(r + 86);
            if (data == null) return;
            EventCount++;
            if (provider == DiskIoGuid)
            {
                if ((opcode == 10 || opcode == 11) && len >= 52)
                {
                    int size = *(int*)(data + 8);
                    int tid = *(int*)(data + 48);
                    lock (_lock) { _diskByThread.TryGetValue(tid, out long v); _diskByThread[tid] = v + size; }
                }
            }
            else if (provider == TcpIpGuid || provider == UdpIpGuid)
            {
                if ((opcode == 10 || opcode == 11 || opcode == 26 || opcode == 27) && len >= 8)
                {
                    int pid = *(int*)data, size = *(int*)(data + 4);
                    lock (_lock) { _netByPid.TryGetValue(pid, out long v); _netByPid[pid] = v + size; }
                }
            }
        }

        /// <summary>Returns and resets the byte counts gathered since the last call.</summary>
        public void Drain(out Dictionary<int, long> diskByThread, out Dictionary<int, long> netByPid)
        {
            lock (_lock)
            {
                diskByThread = _diskByThread; netByPid = _netByPid;
                _spareDisk.Clear(); _spareNet.Clear();
                _diskByThread = _spareDisk; _netByPid = _spareNet;
                _spareDisk = diskByThread; _spareNet = netByPid;
            }
        }

        static void Stop(string name)
        {
            int size = 120 + (name.Length + 1) * 2 + 16;
            IntPtr props = Marshal.AllocHGlobal(size);
            try
            {
                for (int i = 0; i < size; i++) ((byte*)props)[i] = 0;
                *(int*)props = size; *(int*)((byte*)props + 116) = 120;
                ControlTrace(0, name, props, 1 /*EVENT_TRACE_CONTROL_STOP*/);
            }
            finally { Marshal.FreeHGlobal(props); }
        }

        public void Dispose()
        {
            try
            {
                if (_session != 0 || Running) Stop(SessionName);
                if (_trace != 0 && _trace != -1) CloseTrace(_trace);
                _thread?.Join(2000);
            }
            catch (Exception ex) { ErrorLog.Write("ETW Dispose", ex); }
            Running = false;
            if (_logfile != IntPtr.Zero) { Marshal.FreeHGlobal(_logfile); _logfile = IntPtr.Zero; }
            if (_namePtr != IntPtr.Zero) { Marshal.FreeHGlobal(_namePtr); _namePtr = IntPtr.Zero; }
        }
    }
}
