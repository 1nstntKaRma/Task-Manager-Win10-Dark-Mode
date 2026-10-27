namespace TaskManagerDark
{
    /// <summary>
    /// User-visible text, taken verbatim from the Windows 10 22H2 Task Manager resources
    /// (Taskmgr.exe.mui string tables, menus and dialogs, plus shell32's "Create new task" dialog).
    /// The number after each constant is the original resource id.
    /// </summary>
    public static class Strings
    {
        public const string AppTitle = "Task Manager";                                         // 32420

        // tabs (32400-32406)
        public const string TabProcesses = "Processes", TabPerformance = "Performance", TabAppHistory = "App history";
        public const string TabStartup = "Startup", TabUsers = "Users", TabDetails = "Details", TabServices = "Services";

        // groups
        public const string GroupApps = "Apps", GroupBackground = "Background processes", GroupWindows = "Windows processes"; // 37153-37155
        public const string TypeApp = "App", TypeBackground = "Background process", TypeWindows = "Windows process";         // 37165-37167

        // details toggle (37199/37200, 37210/37211)
        public const string MoreDetails = "More details", FewerDetails = "Fewer details";
        public const string NoRunningApps = "There are no running apps";                         // 37224
        public const string NoStartupItems = "There are no startup items to display";           // 39000
        public const string NotResponding = "Not responding";                                   // 32210
        public const string Suspended = "Suspended";                                            // 32212
        public const string Running = "Running", Stopped = "Stopped";                           // 32203, 32205

        // command buttons
        public const string EndTaskButton = "&End task";                                        // 38001
        public const string RestartButton = "R&estart";                                         // 38002
        public const string DisconnectButton = "Dis&connect";                                   // 38005
        public const string EnableButton = "E&nable", DisableButton = "Dis&able";              // 37600, 37601
        public const string OpenServices = "Open Services";                                     // 38006
        public const string OpenResourceMonitor = "Open Resource Monitor";                      // 34283
        public const string DeleteUsageHistory = "Delete usage history";                        // 38504
        public const string LastBiosTime = "Last BIOS time: ";                                  // 37604
        public const string Seconds = "{0} seconds";                                            // 37605

        // column titles (Processes 37001-37017, Details 32003-32055, Services 32046/32047, App history 37040-37047)
        public const string ColName = "Name", ColPid = "PID", ColType = "Type", ColStatus = "Status", ColPublisher = "Publisher";
        public const string ColProcessName = "Process name", ColCommandLine = "Command line", ColCpu = "CPU", ColMemory = "Memory";
        public const string ColDisk = "Disk", ColNetwork = "Network", ColGpu = "GPU", ColGpuEngine = "GPU engine";
        public const string ColPower = "Power usage", ColPowerTrend = "Power usage trend";
        public const string ColStartupImpact = "Startup impact", ColStartupType = "Startup type", ColDiskAtStartup = "Disk I/O at startup";
        public const string ColCpuAtStartup = "CPU at startup", ColRunningNow = "Running now", ColDisabledTime = "Disabled time";
        public const string ColId = "ID", ColClientName = "Client name", ColSession = "Session", ColUser = "User";
        public const string ColCpuTime = "CPU time", ColTileUpdates = "Tile updates", ColMetered = "Metered network";
        public const string ColNonMetered = "Non-metered network", ColDownloads = "Downloads", ColUploads = "Uploads";
        public const string ColDescription = "Description", ColGroup = "Group";

        // tooltips of the Processes header totals
        public const string TipCpuTotal = "Total processor utilization across all cores";       // 39001
        public const string TipMemTotal = "Physical memory in use by active processes";         // 39002
        public const string TipDiskTotal = "Total utilization across all physical drives";     // 39003
        public const string TipNetTotal = "Network utilization on the current primary network"; // 39004
        public const string TipGpuTotal = "Highest utilization across all GPU engines";         // 39009
        public const string TipPower = "CPU, Disk, and GPU impact on power consumption";        // 41038
        public const string TipPowerTrend = "CPU, Disk, and GPU impact on power consumption over time"; // 41039
        public const string TipStartupImpact = "Degree of impact from CPU and disk activity, measured during startup and updated with each restart"; // 39005
        public const string TipDiskAtStartup = "Disk activity, measured during startup and updated with each restart"; // 39006
        public const string TipCpuAtStartup = "CPU activity, measured during startup and updated with each restart";   // 39007
        public const string NotMeasuredTip = "Impact hasn't been measured yet. Restart your PC to measure startup impact."; // 39008

        // power levels (36011-36015), startup impact (37498-37502)
        public static readonly string[] PowerLevels = { "Very low", "Low", "Moderate", "High", "Very high" };
        public const string ImpactNone = "None", ImpactNotMeasured = "Not measured", ImpactLow = "Low", ImpactMedium = "Medium", ImpactHigh = "High";
        public const string StartupRegistry = "Registry", StartupFolder = "Folder";             // 37503, 37504
        public const string Enabled = "Enabled", Disabled = "Disabled";                         // 37160, 37159

        // dialogs
        public const string CreateNewTask = "Create new task";                                  // 37212
        public const string RunPrompt = "Type the name of a program, folder, document, or Internet resource, and Windows will open it for you."; // 37213
        public const string RunOpen = "&Open:";                                                 // shell32 #1011
        public const string RunAdmin = "Create this task with administrative privileges.";      // shell32 #1011
        public const string RunAdminAlready = "This task will be created with administrative privileges."; // shell32 #1005
        public const string Browse = "&Browse...";
        public const string SelectColumns = "Select columns";                                   // 32431
        public const string SelectColumnsPrompt = "Select the columns that will appear in the table."; // 32432
        public const string AffinityTitle = "Processor affinity";                               // dialog 34700
        public const string AffinityPrompt = "Which processors are allowed to run \"{0}\"?";
        public const string AllProcessors = "<All Processors>";                                 // 34707
        public const string WaitChainTitle = "Analyze wait chain";                              // dialog 30120
        public const string WaitChainHelp = "The analyze wait chain tree shows which processes (root nodes in the tree) are using, or waiting to use, a resource that is being used by another process (child nodes in the tree) and is required for the selected process to continue.";
        public const string WaitChainLink = "More about wait chains";
        public const string WaitChainLinkUrl = "https://go.microsoft.com/fwlink/?LinkID=136333";
        public const string DumpTitle = "Dumping process";                                      // dialogs 34801/34802
        public const string DumpWait = "Please wait while the process is written to the file.";
        public const string DumpDone = "The file has been successfully created.\n\nThe file is located at:";
        public const string SendMessageTitle = "Send Message";                                  // dialog 34850
        public const string MessageTitleLabel = "&Message title:", MessageLabel = "Me&ssage:";
        public const string MessageFrom = "Message from {0} - {1}";                              // 37220

        // confirmations (32421-32451, 37215-37245)
        public const string EndQuestion = "Do you want to end {0}?";
        public const string EndWarning = "If an open program is associated with this process, it will close and you will lose any unsaved data. If you end a system process, it might result in system instability. Are you sure you want to continue?";
        public const string EndProcess = "End process";
        public const string EndTreeQuestion = "Do you want to end the process tree of {0}?";
        public const string EndTreeWarning = "If open programs or processes are associated with this process tree, they will close and you will lose any unsaved data. If you end a system process, it might result in system instability. Are you sure you want to continue?";
        public const string EndProcessTree = "End process tree";
        public const string EndSelectedQuestion = "Do you want to end the selected processes?";
        public const string EndSelectedWarning = "If open programs are associated with the selected processes, they will close and you will lose any unsaved data. If you end a system process, it might result in system instability. Are you sure you want to continue?";
        public const string EndSelectedTreesQuestion = "Do you want to end the selected process trees?";
        public const string EndSelectedTreesWarning = "If open programs or processes are associated with the selected process trees, they will close and you will lose any unsaved data. If you end a system process, it might result in system instability. Are you sure you want to continue?";
        public const string EndSystemQuestion = "Do you want to end the system process '{0}'?";
        public const string EndSystemWarning = "Ending this process will cause Windows to become unusable or shut down, causing you to lose any data hasn't been saved. Are you sure you want to continue?";
        public const string ShutDown = "Shut down";
        public const string AbandonAndShutDown = "Abandon unsaved data and shut down.";
        public const string PriorityQuestion = "Do you want to change the priority of '{0}'?";
        public const string PriorityWarning = "Changing the priority of certain processes could cause system instability.";
        public const string ChangePriority = "Change priority";
        public const string PriorityRealtimeFailed = "Unable to set priority to Realtime. The priority was set to High instead.";
        public const string VirtQuestion = "Do you want to change virtualization for '{0}'?";
        public const string VirtWarning = "Changing virtualization for a process may lead to undesired results including loss of data. You should do this only for debugging.";
        public const string ChangeVirt = "Change virtualization";
        public const string SignOutWarning = "If you sign out a user, the user's unsaved data might be lost.";
        public const string ContinueQuestion = "Do you want to continue?";
        public const string SignOutUser = "Sign out user";
        public const string DisconnectQuestion = "Are you sure you want to disconnect the selected user(s)?";
        public const string DisconnectUser = "Disconnect user";
        public const string OperationFailed = "The operation could not be completed.";
        public const string OperationInvalid = "The operation is not valid for this process.";
        public const string UnableTerminate = "Unable to terminate process", UnableTerminateTree = "Unable to terminate process tree";
        public const string UnableAttachDebugger = "Unable to attach debugger";
        public const string UnablePriority = "Unable to change priority", UnableAffinity = "Unable to access or set process affinity";
        public const string UnableStart = "Unable to start service", UnableStop = "Unable to stop service", UnableRestart = "Unable to restart service";
        public const string UnableVirt = "Unable to set virtualization";
        public const string NeedAdmin = "You need administrator privileges to complete this task. Close Task Manager, then run it as an administrator and try again.";
        public const string AccessDenied = "Access denied.";
        public const string CommandFailed = "When attempting to execute the command, the following system error occurred: ";

        // App history
        public const string UsageSinceAll = "Resource usage since {0} for current user and system accounts.";  // 38508
        public const string UsageSinceUser = "Resource usage since {0} for current user account.";              // 38513
        public const string NoData = "No data available";                                                     // 38506
        public const string UninstalledProcesses = "Uninstalled processes", RemoteProcesses = "Remote processes", SystemName = "System";

        // Performance
        public const string Utilization = "Utilization", Speed = "Speed", UpTime = "Up time", Processes = "Processes", Threads = "Threads", Handles = "Handles";
        public const string BaseSpeed = "Base speed:", Sockets = "Sockets:", Cores = "Cores:", LogicalProcessors = "Logical processors:";
        public const string Virtualization = "Virtualization:", HyperV = "Hyper-V support:", VirtualMachine = "Virtual machine:", VirtualProcessors = "Virtual processors:";
        public const string L1 = "L1 cache:", L2 = "L2 cache:", L3 = "L3 cache:", NotCapable = "Not capable";
        public const string PctUtilization = "% Utilization", PctUtilizationPaused = "% Utilization (Paused)";
        public const string LogicalProcessorsTitle = "Logical processors";
        public const string MemoryUsage = "Memory usage", MemoryComposition = "Memory composition";
        public const string InUseCompressed = "In use (Compressed)", Available = "Available", Committed = "Committed", Cached = "Cached";
        public const string PagedPool = "Paged pool", NonPagedPool = "Non-paged pool";
        public const string MemSpeed = "Speed:", SlotsUsed = "Slots used:", FormFactor = "Form factor:", HwReserved = "Hardware reserved:";
        public const string ActiveTime = "Active time", AvgResponse = "Average response time", ReadSpeed = "Read speed", WriteSpeed = "Write speed";
        public const string DiskTransferRate = "Disk transfer rate";
        public const string Capacity = "Capacity:", Formatted = "Formatted:", SystemDisk = "System disk:", PageFile = "Page file:", DiskType = "Type:";
        public const string Throughput = "Throughput", Send = "Send", Receive = "Receive", NotConnected = "Not connected";
        public const string AdapterName = "Adapter name:", Ssid = "SSID:", ConnectionType = "Connection type:", Ipv4 = "IPv4 address:", Ipv6 = "IPv6 address:";
        public const string DnsName = "DNS name:", SignalStrength = "Signal strength:";
        public const string DriverVersion = "Driver version:", DriverDate = "Driver date:", DirectXVersion = "DirectX version:";
        public const string PhysicalLocation = "Physical location:", GpuHwReserved = "Hardware reserved memory:";
        public const string DedicatedGpuMem = "Dedicated GPU memory", SharedGpuMem = "Shared GPU memory", GpuMemory = "GPU Memory", GpuTemperature = "GPU Temperature";
        public const string DedicatedGpuMemUsage = "Dedicated GPU memory usage", SharedGpuMemUsage = "Shared GPU memory usage";
        public const string Seconds60 = "60 seconds", Seconds30 = "30 seconds", Minutes4 = "4 minutes", Paused = "Paused";
        public const string Yes = "Yes", No = "No";
    }
}
