using System;
using System.Collections.Generic;

namespace TaskManagerDark
{
    /// <summary>Per-update numbers of a process. The sampler fills <see cref="ProcInfo.Next"/>, the UI thread copies it to Cur.</summary>
    public struct ProcDyn
    {
        public int ParentPid, SessionId, BasePriority, Threads, Handles, PageFaults, HardFaults;
        public long KernelTime, UserTime, CycleTime;
        public long WorkingSet, PeakWorkingSet, PrivateWS, Commit, PagedPool, NonPagedPool, VirtualSize;
        public long IoReadOps, IoWriteOps, IoOtherOps, IoReadBytes, IoWriteBytes, IoOtherBytes;
        public double Cpu;                // % of all logical processors
        public long WsDelta; public int PfDelta;
        public double DiskBps;            // bytes/s (ETW disk, or all I/O when not elevated)
        public double NetBps;             // bits/s (ETW only)
        public double Gpu;                // % of the busiest engine
        public int GpuEngine;             // engine key (GpuSampler.EngineName), -1 none
        public long GpuDedicated, GpuShared;
        public bool Suspended;            // every thread suspended
        public int GuiUser, GuiGdi;
        public int IoPriority;            // -1 unknown
        public long CpuTimeTotal => KernelTime + UserTime;
    }

    /// <summary>Things that do not change during a process's life. Built off the UI thread, assigned on it.</summary>
    public sealed class ProcStatic
    {
        public string ImagePath, CommandLine, Description, Company, UserName, UserDomain, UserSid, PackageFullName, AppUserModelId;
        public bool Elevated, Critical, IsImmersive, IsWow64, AccessDenied;
        public int Platform;              // 32 or 64, 0 unknown
        public int Virtualization;        // 0 not allowed, 1 disabled, 2 enabled, -1 unknown
        public int Dep;                   // 0 disabled, 1 enabled, 2 enabled (permanent), -1 unknown
        public string DpiAwareness, StackProtection, PowerThrottling, IntegrityLevel;
        public IntPtr IconSmall;          // owned by IconCache / LookCache
        public ProcStatic Clone() => (ProcStatic)MemberwiseClone();
    }

    public sealed class ProcInfo
    {
        public readonly int Pid;
        public readonly long CreateTime;
        public readonly string Name;      // image file name as the kernel reports it ("chrome.exe")
        public ProcDyn Cur, Next;
        public ProcStatic Static;         // null until loaded
        public IntPtr Handle;             // PROCESS_QUERY_LIMITED_INFORMATION handle kept for the process's life (sampler/static thread)
        public bool Exited;
        public long FirstSeenTick;
        // power usage trend: 2 minute moving average of the power score (UI thread)
        public double PowerEma; public bool PowerEmaInit;

        // sampler-thread private state for deltas
        internal long PrevCpu, PrevWs, PrevIo, PrevReadOps; internal int PrevPf; internal bool HasPrev;
        internal int SeenGeneration;

        public ProcInfo(int pid, long createTime, string name) { Pid = pid; CreateTime = createTime; Name = name; Cur.GpuEngine = Next.GpuEngine = -1; Cur.IoPriority = Next.IoPriority = -1; }

        public string Key => Pid + ":" + CreateTime;
        public string DisplayName
        {
            get
            {
                var d = Static?.Description;
                if (!string.IsNullOrEmpty(d)) return d;
                return FriendlyBaseName;
            }
        }
        public string FriendlyBaseName => Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Name.Substring(0, Name.Length - 4) : Name;
        public override string ToString() => Name + " (" + Pid + ")";
    }

    /// <summary>A visible top-level window (what makes a process an "App").</summary>
    public sealed class WinInfo
    {
        public IntPtr Hwnd;
        public int Pid;
        public string Title;
        public bool Hung, Minimized;
    }

    /// <summary>A Windows service as listed in the Services tab.</summary>
    public sealed class SvcInfo
    {
        public string Name, DisplayName, Description, Group, ImagePath;
        public int State, Pid, StartType = -1, ServiceType;
        public bool ConfigLoaded;
        public SvcInfo Clone() => (SvcInfo)MemberwiseClone();
        public string StatusText
        {
            get
            {
                switch (State)
                {
                    case 1: return "Stopped"; case 2: return "Starting"; case 3: return "Stopping"; case 4: return "Running";
                    case 5: return "Continuing"; case 6: return "Pausing"; case 7: return "Paused";
                }
                return "";
            }
        }
    }
}
