using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TaskManagerDark
{
    /// <summary>Static CPU facts shown on the Performance tab.</summary>
    public sealed class CpuStatic
    {
        public string Name = "CPU";
        public int Sockets, Cores, Logical, NumaNodes;
        public long L1, L2, L3;
        public double BaseMhz;
        public string Virtualization;       // Enabled / Disabled / Not capable
        public bool VirtualMachine;
        public List<int>[] NodeCpus;        // logical processors per NUMA node

        public static CpuStatic Load()
        {
            var s = new CpuStatic { Logical = Environment.ProcessorCount };
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    var n = k?.GetValue("ProcessorNameString") as string;
                    if (!string.IsNullOrWhiteSpace(n)) s.Name = n.Trim();
                }
            }
            catch (Exception ex) { ErrorLog.Write("CpuStatic name", ex); }
            ReadTopology(s);
            try
            {
                int n = s.Logical;
                int size = 24 * n;
                IntPtr buf = Marshal.AllocHGlobal(size);
                try
                {
                    if (CallNtPowerInformation(11 /*ProcessorInformation*/, IntPtr.Zero, 0, buf, size) == 0)
                        s.BaseMhz = Marshal.ReadInt32(buf, 4);
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            catch (Exception ex) { ErrorLog.Write("CpuStatic speed", ex); }
            s.Virtualization = Sys.IsProcessorFeaturePresent(21 /*PF_VIRT_FIRMWARE_ENABLED*/) ? Strings.Enabled : Strings.Disabled;
            return s;
        }

        [DllImport("powrprof.dll")] static extern int CallNtPowerInformation(int level, IntPtr inBuf, int inSize, IntPtr outBuf, int outSize);

        static IntPtr _powerBuf;

        /// <summary>
        /// Average current clock of all processors from the power manager. Stand-in for the "% Processor Performance"
        /// counter until the counters thread has its first rate (sampler thread only).
        /// </summary>
        public static unsafe double CurrentMhz(CpuStatic c)
        {
            int n = Math.Max(1, c.Logical);
            if (_powerBuf == IntPtr.Zero) _powerBuf = Marshal.AllocHGlobal(24 * n);
            if (CallNtPowerInformation(11 /*ProcessorInformation*/, IntPtr.Zero, 0, _powerBuf, 24 * n) != 0) return c.BaseMhz;
            double sum = 0;
            for (int i = 0; i < n; i++) sum += *(uint*)((byte*)_powerBuf + i * 24 + 8);   // PROCESSOR_POWER_INFORMATION.CurrentMhz
            return sum > 0 ? sum / n : c.BaseMhz;
        }

        static unsafe void ReadTopology(CpuStatic s)
        {
            int len = 0;
            Sys.GetLogicalProcessorInformationEx(0xFFFF, IntPtr.Zero, ref len);
            if (len <= 0) return;
            IntPtr buf = Marshal.AllocHGlobal(len);
            var nodes = new List<List<int>>();
            try
            {
                if (!Sys.GetLogicalProcessorInformationEx(0xFFFF, buf, ref len)) return;
                byte* p = (byte*)buf, end = p + len;
                while (p < end)
                {
                    int rel = *(int*)p, size = *(int*)(p + 4);
                    switch (rel)
                    {
                        case 0: s.Cores++; break;                       // RelationProcessorCore
                        case 3: s.Sockets++; break;                     // RelationProcessorPackage
                        case 1:                                         // RelationNumaNode
                            {
                                ulong mask = *(ulong*)(p + 32); int group = *(ushort*)(p + 40);
                                var l = new List<int>();
                                for (int b = 0; b < 64; b++) if ((mask & (1UL << b)) != 0) l.Add(group * 64 + b);
                                nodes.Add(l);
                                break;
                            }
                        case 2:                                         // RelationCache
                            {
                                int level = p[8]; long cs = *(uint*)(p + 12);
                                if (level == 1) s.L1 += cs; else if (level == 2) s.L2 += cs; else if (level == 3) s.L3 += cs;
                                break;
                            }
                    }
                    if (size <= 0) break;
                    p += size;
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            s.NumaNodes = Math.Max(1, nodes.Count);
            s.NodeCpus = nodes.Count > 0 ? nodes.ToArray() : new[] { AllCpus(s.Logical) };
        }

        static List<int> AllCpus(int n) { var l = new List<int>(); for (int i = 0; i < n; i++) l.Add(i); return l; }
    }

    /// <summary>Per-update CPU, memory and system counts.</summary>
    public sealed class SysSample
    {
        public double Cpu, CpuKernel, Interrupts;          // % of all logical processors
        public double[] PerCpu, PerCpuKernel;
        public double SpeedMhz;
        public int Processes, Threads, Handles;
        public long UptimeSeconds;
        public long MemTotal, MemAvail, MemInUse, Modified, Standby, Free, Cached, CommitTotal, CommitLimit, PagedPool, NonPagedPool, Compressed;
        public long InstalledKB;
        public List<DiskSample> Disks;
        public List<NetSample> Nets;
        public List<GpuSample> Gpus;
        public double DiskMaxActive;                       // highest active time of all disks (Processes header)
        public double NetPrimaryUtil;                      // utilization of the primary adapter (Processes header)
        public double GpuMax;                              // busiest engine of all GPUs
        public double SampleMs;                            // cost of producing this sample
    }

    /// <summary>Per-processor times and memory. Runs on the sampler thread.</summary>
    public sealed unsafe class CpuMemSampler
    {
        readonly int _n;
        readonly long[] _prevIdle, _prevKernel, _prevUser, _prevDpc, _prevIntr;
        IntPtr _buf;
        bool _hasPrev;
        public readonly long BootTime;

        public CpuMemSampler()
        {
            _n = Environment.ProcessorCount;
            _prevIdle = new long[_n]; _prevKernel = new long[_n]; _prevUser = new long[_n]; _prevDpc = new long[_n]; _prevIntr = new long[_n];
            _buf = Marshal.AllocHGlobal(48 * _n + 64);
            IntPtr tod = Marshal.AllocHGlobal(48);
            if (Sys.NtQuerySystemInformation(Sys.SystemTimeOfDayInformation, tod, 48, out _) == 0) BootTime = *(long*)tod;
            Marshal.FreeHGlobal(tod);
        }

        public void SampleCpu(SysSample s)
        {
            s.PerCpu = new double[_n]; s.PerCpuKernel = new double[_n];
            if (Sys.NtQuerySystemInformation(Sys.SystemProcessorPerformanceInformation, _buf, 48 * _n, out int ret) != 0) return;
            int count = Math.Min(_n, ret / 48);
            long tBusy = 0, tTotal = 0, tKernelBusy = 0, tDpcIntr = 0;
            for (int i = 0; i < count; i++)
            {
                long* e = (long*)((byte*)_buf + i * 48);
                long idle = e[0], kernel = e[1], user = e[2], dpc = e[3], intr = e[4];
                if (_hasPrev)
                {
                    long di = idle - _prevIdle[i], dk = kernel - _prevKernel[i], du = user - _prevUser[i];
                    long total = dk + du, busy = total - di;
                    if (busy < 0) busy = 0;
                    s.PerCpu[i] = total > 0 ? Clamp(busy * 100.0 / total) : 0;
                    s.PerCpuKernel[i] = total > 0 ? Clamp((dk - di) * 100.0 / total) : 0;
                    tBusy += busy; tTotal += total; tKernelBusy += Math.Max(0, dk - di);
                    tDpcIntr += (dpc - _prevDpc[i]) + (intr - _prevIntr[i]);
                }
                _prevIdle[i] = idle; _prevKernel[i] = kernel; _prevUser[i] = user; _prevDpc[i] = dpc; _prevIntr[i] = intr;
            }
            if (_hasPrev && tTotal > 0)
            {
                s.Cpu = Clamp(tBusy * 100.0 / tTotal);
                s.CpuKernel = Clamp(tKernelBusy * 100.0 / tTotal);
                s.Interrupts = Clamp(tDpcIntr * 100.0 / tTotal);
            }
            _hasPrev = true;
            long now = DateTime.UtcNow.ToFileTimeUtc();
            s.UptimeSeconds = BootTime > 0 ? (now - BootTime) / 10000000 : (long)(Sys.GetTickCount64() / 1000);
        }

        public void SampleMemory(SysSample s)
        {
            var m = new Native.MEMORYSTATUSEX { dwLength = Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX)) };
            if (Sys.GlobalMemoryStatusEx(ref m)) { s.MemTotal = (long)m.ullTotalPhys; s.MemAvail = (long)m.ullAvailPhys; }
            if (Sys.K32GetPerformanceInfo(out var pi, Marshal.SizeOf(typeof(Sys.PERFORMANCE_INFORMATION))))
            {
                long page = (long)pi.PageSize;
                s.CommitTotal = (long)pi.CommitTotal * page; s.CommitLimit = (long)pi.CommitLimit * page;
                s.PagedPool = (long)pi.KernelPaged * page; s.NonPagedPool = (long)pi.KernelNonpaged * page;
                s.Cached = (long)pi.SystemCache * page;
                s.Handles = pi.HandleCount; s.Processes = pi.ProcessCount; s.Threads = pi.ThreadCount;
            }
        }

        static double Clamp(double v) => v < 0 ? 0 : v > 100 ? 100 : v;
    }

    /// <summary>
    /// Standby / modified / free page lists straight from the memory manager (SystemMemoryListInformation). Needs
    /// SeProfileSingleProcessPrivilege, so it works when elevated; otherwise the counters thread supplies them.
    /// </summary>
    public static unsafe class MemoryLists
    {
        static IntPtr _buf;
        static readonly long Page = Environment.SystemPageSize;

        public static bool Read(out long free, out long modified, out long standby)
        {
            free = modified = standby = 0;
            if (_buf == IntPtr.Zero) _buf = Marshal.AllocHGlobal(176);
            if (Sys.NtQuerySystemInformation(80 /*SystemMemoryListInformation*/, _buf, 176, out _) != 0) return false;
            long* v = (long*)_buf;
            // ZeroPageCount, FreePageCount, ModifiedPageCount, ModifiedNoWritePageCount, BadPageCount, PageCountByPriority[8], ...
            free = (v[0] + v[1]) * Page;
            modified = (v[2] + v[3]) * Page;
            long sb = 0; for (int i = 0; i < 8; i++) sb += v[5 + i];
            standby = sb * Page;
            return true;
        }
    }

    /// <summary>Memory hardware from the SMBIOS tables (instant); WMI only when the firmware tables say nothing.</summary>
    public sealed class MemStatic
    {
        public int SpeedMhz, SlotsUsed, SlotsTotal;
        public string FormFactor;
        public long InstalledBytes;
        public bool Loaded;

        [DllImport("kernel32.dll", SetLastError = true)] static extern uint GetSystemFirmwareTable(uint provider, uint id, IntPtr buf, uint size);

        public static MemStatic Load()
        {
            var m = new MemStatic();
            try { if (Sys.GetPhysicallyInstalledSystemMemory(out ulong kb)) m.InstalledBytes = (long)kb * 1024; }
            catch (Exception ex) { ErrorLog.Write("GetPhysicallyInstalledSystemMemory", ex); }
            try { FromSmbios(m); }
            catch (Exception ex) { ErrorLog.Write("MemStatic SMBIOS", ex); }
            if (m.SlotsUsed == 0) FromWmi(m);
            if (m.SlotsTotal < m.SlotsUsed) m.SlotsTotal = m.SlotsUsed;
            m.Loaded = true;
            return m;
        }

        /// <summary>SMBIOS type 16 (memory array: slot count) and type 17 (memory device: speed, form factor).</summary>
        public static unsafe void FromSmbios(MemStatic m)
        {
            const uint RSMB = 0x52534D42;
            uint size = GetSystemFirmwareTable(RSMB, 0, IntPtr.Zero, 0);
            if (size < 8) return;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetSystemFirmwareTable(RSMB, 0, buf, size) != size) return;
                byte* p = (byte*)buf + 8, end = p + Math.Min(*(uint*)((byte*)buf + 4), size - 8);
                int slotsSystem = 0, slotsAll = 0;
                while (p + 4 <= end)
                {
                    int type = p[0], len = p[1];
                    if (len < 4 || type == 127 || p + len > end) break;
                    if (type == 16 && len >= 0x0F)
                    {
                        int devices = *(ushort*)(p + 0x0D);
                        slotsAll += devices;
                        if (p[5] == 3 /*system memory*/) slotsSystem += devices;
                    }
                    else if (type == 17 && len >= 0x15)
                    {
                        int sz = *(ushort*)(p + 0x0C);
                        if (sz != 0)
                        {
                            m.SlotsUsed++;
                            int speed = len >= 0x17 ? *(ushort*)(p + 0x15) : 0;
                            if (speed == 0xFFFF && len >= 0x58) speed = (int)*(uint*)(p + 0x54);
                            int conf = len >= 0x22 ? *(ushort*)(p + 0x20) : 0;
                            if (conf == 0xFFFF && len >= 0x5C) conf = (int)*(uint*)(p + 0x58);
                            int sp = conf > 0 ? conf : speed;
                            if (sp > m.SpeedMhz) m.SpeedMhz = sp;
                            if (m.FormFactor == null) m.FormFactor = SmbiosFormFactor(p[0x0E]);
                        }
                    }
                    // skip the formatted part and the string set (ends with two NULs)
                    byte* q = p + len;
                    while (q + 1 < end && (q[0] != 0 || q[1] != 0)) q++;
                    p = q + 2;
                }
                m.SlotsTotal = slotsSystem > 0 ? slotsSystem : slotsAll;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        static string SmbiosFormFactor(int f)
        {
            switch (f)
            {
                case 3: return "SIMM"; case 4: return "SIP"; case 5: return "Chip"; case 6: return "DIP"; case 7: return "ZIP";
                case 8: return "Proprietary"; case 9: return "DIMM"; case 10: return "TSOP"; case 11: return "Row of chips";
                case 12: return "RIMM"; case 13: return "SODIMM"; case 14: return "SRIMM"; case 15: return "FB-DIMM"; case 16: return "Die";
            }
            return "Other";
        }

        public static void FromWmi(MemStatic m)
        {
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher("SELECT Speed, ConfiguredClockSpeed, FormFactor, Capacity FROM Win32_PhysicalMemory"))
                foreach (System.Management.ManagementObject o in searcher.Get())
                {
                    m.SlotsUsed++;
                    int sp = ToInt(o["ConfiguredClockSpeed"]); if (sp <= 0) sp = ToInt(o["Speed"]);
                    if (sp > m.SpeedMhz) m.SpeedMhz = sp;
                    if (m.FormFactor == null) m.FormFactor = FormFactorName(ToInt(o["FormFactor"]));
                    o.Dispose();
                }
                using (var searcher = new System.Management.ManagementObjectSearcher("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray"))
                foreach (System.Management.ManagementObject o in searcher.Get()) { m.SlotsTotal += ToInt(o["MemoryDevices"]); o.Dispose(); }
            }
            catch (Exception ex) { ErrorLog.Write("MemStatic WMI", ex); }
        }

        static int ToInt(object o)
        {
            if (o == null) return 0;
            try { return Convert.ToInt32(o); }
            catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException) { return 0; }
        }

        static string FormFactorName(int f)
        {
            switch (f)
            {
                case 2: return "SIP"; case 3: return "DIP"; case 4: return "ZIP"; case 5: return "SOJ"; case 6: return "Proprietary";
                case 7: return "SIMM"; case 8: return "DIMM"; case 9: return "TSOP"; case 10: return "PGA"; case 11: return "RIMM";
                case 12: return "SODIMM"; case 13: return "SRIMM"; case 14: return "SMD"; case 15: return "SSMP"; case 16: return "QFP";
                case 17: return "TQFP"; case 18: return "SOIC"; case 19: return "LCC"; case 20: return "PLCC"; case 21: return "BGA";
                case 22: return "FPBGA"; case 23: return "LGA";
            }
            return "Other";
        }
    }
}
