using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TaskManagerDark
{
    /// <summary>System information P/Invoke: processes, tokens, services, sessions, windows, devices, network.</summary>
    public static unsafe class Sys
    {
        // ---------------------------------------------------------------- ntdll
        public const int SystemBasicInformation = 0, SystemPerformanceInformation = 2, SystemTimeOfDayInformation = 3, SystemProcessInformation = 5;
        public const int SystemProcessorPerformanceInformation = 8, SystemInterruptInformation = 23, SystemProcessorIdleCycleTimeInformation = 83;
        public const int SystemMemoryListInformation = 80, SystemFileCacheInformationEx = 81, SystemStoreInformation = 109;
        public const int ProcessBasicInformation = 0, ProcessIoPriority = 33, ProcessBreakOnTermination = 29, ProcessCommandLineInformation = 60;
        public const int ProcessWow64Information = 26, ProcessImageFileNameWin32 = 43, ProcessProtectionInformation = 61;
        public const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004), STATUS_BUFFER_TOO_SMALL = unchecked((int)0xC0000023);

        [DllImport("ntdll.dll")] public static extern int NtQuerySystemInformation(int cls, IntPtr buf, int len, out int retLen);
        [DllImport("ntdll.dll")] public static extern int NtQueryInformationProcess(IntPtr h, int cls, IntPtr buf, int len, out int retLen);
        [DllImport("ntdll.dll")] public static extern int NtQueryInformationProcess(IntPtr h, int cls, out int value, int len, out int retLen);
        [DllImport("ntdll.dll")] public static extern int NtSetInformationProcess(IntPtr h, int cls, ref int value, int len);
        [DllImport("ntdll.dll")] public static extern int RtlNtStatusToDosError(int status);

        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_BASIC_INFORMATION { public IntPtr ExitStatus, PebBaseAddress, AffinityMask, BasePriority, UniqueProcessId, InheritedFromUniqueProcessId; }

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION { public long IdleTime, KernelTime, UserTime, DpcTime, InterruptTime; public int InterruptCount; }

        // ---------------------------------------------------------------- kernel32
        public const uint PROCESS_TERMINATE = 0x1, PROCESS_VM_READ = 0x10, PROCESS_DUP_HANDLE = 0x40, PROCESS_SET_INFORMATION = 0x200, PROCESS_QUERY_INFORMATION = 0x400;
        public const uint PROCESS_SUSPEND_RESUME = 0x800, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, SYNCHRONIZE = 0x100000, PROCESS_ALL_ACCESS = 0x1FFFFF;

        [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr OpenThread(uint access, bool inherit, int tid);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "QueryFullProcessImageNameW")]
        public static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool IsWow64Process(IntPtr h, out bool wow);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool IsWow64Process2(IntPtr h, out ushort processMachine, out ushort nativeMachine);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool TerminateProcess(IntPtr h, uint code);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern int GetPriorityClass(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool SetPriorityClass(IntPtr h, int cls);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetProcessAffinityMask(IntPtr h, out UIntPtr proc, out UIntPtr sys);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool SetProcessAffinityMask(IntPtr h, UIntPtr mask);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetProcessDEPPolicy(IntPtr h, out int flags, out bool permanent);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern int GetPackageFullName(IntPtr h, ref int len, StringBuilder sb);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern int GetApplicationUserModelId(IntPtr h, ref int len, StringBuilder sb);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetProcessMitigationPolicy(IntPtr h, int policy, out long buffer, int size);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetProcessInformation(IntPtr h, int cls, IntPtr buf, int size);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetProcessTimes(IntPtr h, out long create, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetExitCodeProcess(IntPtr h, out uint code);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetLogicalProcessorInformationEx(int relation, IntPtr buf, ref int len);
        [DllImport("kernel32.dll")] public static extern ulong GetTickCount64();
        [DllImport("kernel32.dll")] public static extern bool QueryPerformanceCounter(out long v);
        [DllImport("kernel32.dll")] public static extern bool QueryPerformanceFrequency(out long v);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetPhysicallyInstalledSystemMemory(out ulong kb);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool K32GetPerformanceInfo(out PERFORMANCE_INFORMATION pi, int size);
        [DllImport("kernel32.dll")] public static extern int GetActiveProcessorCount(ushort group);
        [DllImport("kernel32.dll")] public static extern ushort GetActiveProcessorGroupCount();
        [DllImport("kernel32.dll")] public static extern bool IsProcessorFeaturePresent(int feature);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool ProcessIdToSessionId(int pid, out int session);
        [DllImport("kernel32.dll")] public static extern int WTSGetActiveConsoleSessionId();
        [DllImport("kernel32.dll")] public static extern int GetCurrentProcessId();
        [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
        public static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool DeviceIoControl(IntPtr h, uint code, IntPtr inBuf, int inSize, IntPtr outBuf, int outSize, out int returned, IntPtr overlapped);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetDiskFreeSpaceExW")]
        public static extern bool GetDiskFreeSpaceEx(string dir, out ulong freeToCaller, out ulong total, out ulong totalFree);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetDriveTypeW")] public static extern int GetDriveType(string root);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetLogicalDriveStringsW")] public static extern int GetLogicalDriveStrings(int len, char[] buf);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "QueryDosDeviceW")] public static extern int QueryDosDevice(string name, char[] buf, int max);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetSystemDirectoryW")] public static extern int GetSystemDirectory(StringBuilder sb, int size);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryExW", SetLastError = true)] public static extern IntPtr LoadLibraryEx(string name, IntPtr file, uint flags);
        [DllImport("kernel32.dll")] public static extern bool FreeLibrary(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadStringW")] public static extern int LoadString(IntPtr h, int id, StringBuilder sb, int max);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "FormatMessageW")]
        public static extern int FormatMessage(int flags, IntPtr source, int id, int lang, StringBuilder sb, int size, IntPtr args);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, IntPtr buf, IntPtr size, out IntPtr read);
        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx(ref Native.MEMORYSTATUSEX m);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [DllImport("kernel32.dll")] public static extern bool FileTimeToSystemTime(ref long ft, out SYSTEMTIME st);
        [DllImport("kernel32.dll")] public static extern bool SystemTimeToTzSpecificLocalTime(IntPtr tz, ref SYSTEMTIME utc, out SYSTEMTIME local);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateMutexW")] public static extern IntPtr CreateMutex(IntPtr sec, bool owner, string name);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr CreateToolhelp32Snapshot(uint flags, int pid);

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEMTIME { public ushort wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds; }

        [StructLayout(LayoutKind.Sequential)]
        public struct PERFORMANCE_INFORMATION
        {
            public int cb;
            public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
            public int HandleCount, ProcessCount, ThreadCount;
        }

        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        public const uint GENERIC_READ = 0x80000000, FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, FILE_SHARE_DELETE = 4, OPEN_EXISTING = 3, CREATE_ALWAYS = 2;

        // ---------------------------------------------------------------- advapi32 (tokens)
        public const uint TOKEN_QUERY = 0x8, TOKEN_ADJUST_PRIVILEGES = 0x20, TOKEN_ADJUST_DEFAULT = 0x80, TOKEN_DUPLICATE = 0x2;
        public const int TokenUser = 1, TokenElevation = 20, TokenElevationType = 18, TokenVirtualizationAllowed = 23, TokenVirtualizationEnabled = 24;
        public const int TokenIntegrityLevel = 25, TokenSessionId = 12, TokenIsAppContainer = 29;

        [DllImport("advapi32.dll", SetLastError = true)] public static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)] public static extern bool GetTokenInformation(IntPtr token, int cls, IntPtr buf, int len, out int ret);
        [DllImport("advapi32.dll", SetLastError = true)] public static extern bool GetTokenInformation(IntPtr token, int cls, out int value, int len, out int ret);
        [DllImport("advapi32.dll", SetLastError = true)] public static extern bool SetTokenInformation(IntPtr token, int cls, ref int value, int len);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "LookupAccountSidW")]
        public static extern bool LookupAccountSid(string system, IntPtr sid, StringBuilder name, ref int nameLen, StringBuilder domain, ref int domainLen, out int use);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "ConvertSidToStringSidW")]
        public static extern bool ConvertSidToStringSid(IntPtr sid, out IntPtr str);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "LookupPrivilegeValueW")]
        public static extern bool LookupPrivilegeValue(string system, string name, out long luid);
        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES newState, int len, IntPtr prev, IntPtr retLen);
        [DllImport("advapi32.dll")] public static extern int GetLengthSid(IntPtr sid);
        [DllImport("advapi32.dll")] public static extern IntPtr GetSidSubAuthority(IntPtr sid, int n);
        [DllImport("advapi32.dll")] public static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
        [DllImport("kernel32.dll")] public static extern IntPtr LocalFree(IntPtr h);

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct TOKEN_PRIVILEGES { public int PrivilegeCount; public long Luid; public int Attributes; }

        // ---------------------------------------------------------------- services
        public const uint SC_MANAGER_CONNECT = 1, SC_MANAGER_ENUMERATE_SERVICE = 4;
        public const uint SERVICE_QUERY_CONFIG = 1, SERVICE_QUERY_STATUS = 4, SERVICE_START = 0x10, SERVICE_STOP = 0x20, SERVICE_PAUSE_CONTINUE = 0x40;
        public const int SERVICE_WIN32 = 0x30, SERVICE_DRIVER = 0xB, SERVICE_STATE_ALL = 3, SC_ENUM_PROCESS_INFO = 0;
        public const int SERVICE_CONFIG_DESCRIPTION = 1, SERVICE_CONTROL_STOP = 1;

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "OpenSCManagerW")] public static extern IntPtr OpenSCManager(string machine, string db, uint access);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "OpenServiceW")] public static extern IntPtr OpenService(IntPtr scm, string name, uint access);
        [DllImport("advapi32.dll", SetLastError = true)] public static extern bool CloseServiceHandle(IntPtr h);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "EnumServicesStatusExW")]
        public static extern bool EnumServicesStatusEx(IntPtr scm, int level, int type, int state, IntPtr buf, int size, out int needed, out int returned, ref int resume, string group);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "QueryServiceConfigW")]
        public static extern bool QueryServiceConfig(IntPtr svc, IntPtr buf, int size, out int needed);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "QueryServiceConfig2W")]
        public static extern bool QueryServiceConfig2(IntPtr svc, int level, IntPtr buf, int size, out int needed);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "StartServiceW")] public static extern bool StartService(IntPtr svc, int n, IntPtr args);
        [DllImport("advapi32.dll", SetLastError = true)] public static extern bool ControlService(IntPtr svc, int control, out SERVICE_STATUS status);
        [DllImport("advapi32.dll", SetLastError = true)] public static extern bool QueryServiceStatusEx(IntPtr svc, int level, out SERVICE_STATUS_PROCESS st, int size, out int needed);

        [StructLayout(LayoutKind.Sequential)]
        public struct SERVICE_STATUS { public int dwServiceType, dwCurrentState, dwControlsAccepted, dwWin32ExitCode, dwServiceSpecificExitCode, dwCheckPoint, dwWaitHint; }
        [StructLayout(LayoutKind.Sequential)]
        public struct SERVICE_STATUS_PROCESS { public int dwServiceType, dwCurrentState, dwControlsAccepted, dwWin32ExitCode, dwServiceSpecificExitCode, dwCheckPoint, dwWaitHint, dwProcessId, dwServiceFlags; }
        [StructLayout(LayoutKind.Sequential)]
        public struct ENUM_SERVICE_STATUS_PROCESS { public IntPtr lpServiceName, lpDisplayName; public SERVICE_STATUS_PROCESS Status; }
        [StructLayout(LayoutKind.Sequential)]
        public struct QUERY_SERVICE_CONFIG { public int dwServiceType, dwStartType, dwErrorControl; public IntPtr lpBinaryPathName, lpLoadOrderGroup; public int dwTagId; public IntPtr lpDependencies, lpServiceStartName, lpDisplayName; }

        // ---------------------------------------------------------------- user32 (windows)
        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr l);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr l);
        [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc proc, IntPtr l);
        [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr hwnd, out int pid);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] public static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int InternalGetWindowText(IntPtr hwnd, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")] public static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern bool IsHungAppWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern void SwitchToThisWindow(IntPtr hwnd, bool altTab);
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("user32.dll")] public static extern int GetGuiResources(IntPtr h, int flags);
        [DllImport("user32.dll")] public static extern bool EndTask(IntPtr hwnd, bool shutdown, bool force);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW")] public static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll")] public static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, int msg, int action, IntPtr info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowExW")] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] public static extern IntPtr SendMessageTimeout(IntPtr hwnd, int msg, IntPtr w, IntPtr l, int flags, int timeout, out IntPtr result);
        [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO lii);
        [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
        [DllImport("user32.dll", SetLastError = true)] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll")] public static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr icon, int cx, int cy, int step, IntPtr brush, int flags);
        [DllImport("user32.dll")] public static extern bool LockSetForegroundWindow(uint code);
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(int a, int b, bool attach);

        [StructLayout(LayoutKind.Sequential)]
        public struct LASTINPUTINFO { public int cbSize; public int dwTime; }

        public const int GWL_STYLE = -16, GWL_EXSTYLE = -20, GW_OWNER = 4, GW_CHILD = 5, GW_HWNDNEXT = 2;
        public const int WS_VISIBLE = 0x10000000, WS_CHILD = 0x40000000, WS_DISABLED = 0x08000000;
        public const int WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8;
        public const int DWMWA_CLOAKED = 14;
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

        // ---------------------------------------------------------------- shell
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "ExtractIconExW")]
        public static extern int ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, int n);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfoW")]
        public static extern IntPtr SHGetFileInfo(string path, int attr, ref SHFILEINFO fi, int size, int flags);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "ShellExecuteExW", SetLastError = true)]
        public static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "ILCreateFromPathW")] public static extern IntPtr ILCreateFromPath(string path);
        [DllImport("shell32.dll")] public static extern void ILFree(IntPtr pidl);
        [DllImport("shell32.dll")] public static extern int SHOpenFolderAndSelectItems(IntPtr folder, int n, IntPtr[] items, int flags);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern bool SHObjectProperties(IntPtr hwnd, int type, string obj, string page);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "CommandLineToArgvW")] public static extern IntPtr CommandLineToArgv(string cmd, out int n);
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] public static extern int SHLoadIndirectString(string src, StringBuilder sb, int size, IntPtr reserved);
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, EntryPoint = "PathFindOnPathW")] public static extern bool PathFindOnPath(StringBuilder path, IntPtr dirs);
        [DllImport("shell32.dll")] public static extern int SHChangeNotify(int e, uint f, IntPtr a, IntPtr b);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "ShellExecuteW")]
        public static extern IntPtr ShellExecute(IntPtr hwnd, string verb, string file, string args, string dir, int show);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon; public int iIcon; public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHELLEXECUTEINFO
        {
            public int cbSize; public uint fMask; public IntPtr hwnd;
            public string lpVerb, lpFile, lpParameters, lpDirectory;
            public int nShow; public IntPtr hInstApp, lpIDList; public string lpClass; public IntPtr hkeyClass; public uint dwHotKey; public IntPtr hIcon, hProcess;
        }
        public const uint SEE_MASK_NOCLOSEPROCESS = 0x40, SEE_MASK_INVOKEIDLIST = 0xC, SEE_MASK_FLAG_NO_UI = 0x400, SEE_MASK_NOASYNC = 0x100, SEE_MASK_UNICODE = 0x4000;

        // ---------------------------------------------------------------- wtsapi32
        [DllImport("wtsapi32.dll", SetLastError = true, EntryPoint = "WTSEnumerateSessionsW")]
        public static extern bool WTSEnumerateSessions(IntPtr server, int reserved, int version, out IntPtr info, out int count);
        [DllImport("wtsapi32.dll", SetLastError = true, EntryPoint = "WTSQuerySessionInformationW")]
        public static extern bool WTSQuerySessionInformation(IntPtr server, int session, int cls, out IntPtr buf, out int bytes);
        [DllImport("wtsapi32.dll")] public static extern void WTSFreeMemory(IntPtr p);
        [DllImport("wtsapi32.dll", SetLastError = true)] public static extern bool WTSDisconnectSession(IntPtr server, int session, bool wait);
        [DllImport("wtsapi32.dll", SetLastError = true)] public static extern bool WTSLogoffSession(IntPtr server, int session, bool wait);
        [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "WTSSendMessageW")]
        public static extern bool WTSSendMessage(IntPtr server, int session, string title, int titleLen, string msg, int msgLen, int style, int timeout, out int response, bool wait);
        [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "WTSConnectSessionW")]
        public static extern bool WTSConnectSession(int logonId, int targetLogonId, string password, bool wait);
        [DllImport("wtsapi32.dll", SetLastError = true)] public static extern bool WTSRegisterSessionNotification(IntPtr hwnd, int flags);
        [DllImport("wtsapi32.dll", SetLastError = true)] public static extern bool WTSUnRegisterSessionNotification(IntPtr hwnd);

        [StructLayout(LayoutKind.Sequential)]
        public struct WTS_SESSION_INFO { public int SessionId; public IntPtr pWinStationName; public int State; }
        public const int WTSUserName = 5, WTSDomainName = 7, WTSConnectState = 8, WTSClientName = 10, WTSSessionInfo = 24;

        // ---------------------------------------------------------------- dbghelp
        [DllImport("dbghelp.dll", SetLastError = true)]
        public static extern bool MiniDumpWriteDump(IntPtr process, int pid, IntPtr file, int type, IntPtr exception, IntPtr user, IntPtr callback);

        // ---------------------------------------------------------------- wait chain traversal
        [DllImport("advapi32.dll", SetLastError = true)] public static extern IntPtr OpenThreadWaitChainSession(int flags, IntPtr callback);
        [DllImport("advapi32.dll", SetLastError = true)] public static extern void CloseThreadWaitChainSession(IntPtr session);
        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool GetThreadWaitChain(IntPtr session, IntPtr context, int flags, int tid, ref int count, IntPtr nodes, out bool cycle);

        // ---------------------------------------------------------------- misc helpers
        public static string Win32Message(int code)
        {
            var sb = new StringBuilder(512);
            int n = FormatMessage(0x1000 | 0x200, IntPtr.Zero, code, 0, sb, sb.Capacity, IntPtr.Zero);
            return n > 0 ? sb.ToString().Trim() : "Error " + code;
        }

        public static string LoadIndirect(string s)
        {
            if (string.IsNullOrEmpty(s) || s[0] != '@') return s;
            var sb = new StringBuilder(1024);
            try { if (SHLoadIndirectString(s, sb, sb.Capacity, IntPtr.Zero) == 0) return sb.ToString(); } catch (Exception ex) { ErrorLog.Write("SHLoadIndirectString", ex); }
            return s;
        }

        static int _elevated = -1;
        public static bool IsElevated
        {
            get
            {
                if (_elevated < 0)
                {
                    _elevated = 0;
                    if (OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out var tok))
                    {
                        if (GetTokenInformation(tok, TokenElevation, out int e, 4, out _) && e != 0) _elevated = 1;
                        CloseHandle(tok);
                    }
                }
                return _elevated == 1;
            }
        }

        /// <summary>True when the user is an administrator whose token is split (UAC), i.e. elevation is possible.</summary>
        public static bool CanElevate
        {
            get
            {
                if (IsElevated) return false;
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out var tok)) return false;
                try { return GetTokenInformation(tok, TokenElevationType, out int t, 4, out _) && t == 3; } // TokenElevationTypeLimited
                finally { CloseHandle(tok); }
            }
        }

        public static bool EnablePrivilege(string name)
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var tok)) return false;
            try
            {
                if (!LookupPrivilegeValue(null, name, out long luid)) return false;
                var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = 2 };
                return AdjustTokenPrivileges(tok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero) && Marshal.GetLastWin32Error() == 0;
            }
            finally { CloseHandle(tok); }
        }

        public static long Qpc() { QueryPerformanceCounter(out long v); return v; }
        static long _qpf;
        public static long QpcFrequency { get { if (_qpf == 0) QueryPerformanceFrequency(out _qpf); return _qpf; } }
    }
}
