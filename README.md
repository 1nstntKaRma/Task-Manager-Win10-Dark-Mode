# Task Manager (Win10) - Dark Mode

> **The Windows 10 Task Manager, in dark mode: same features, a drop-in replacement, one-click install and revert.**

![Version](https://img.shields.io/badge/version-v1.25-blue)
![Platform](https://img.shields.io/badge/platform-Windows%2010-0078D6)
![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8%20(built--in)-512BD4)
![License](https://img.shields.io/badge/license-MIT-green)

**Task Manager – Dark Mode** is a dark, feature-for-feature recreation of the Windows 10 (22H2) Task Manager. It has every tab (**Processes**, **Performance**, **App history**, **Startup**, **Users**, **Details**, **Services**), the same menus and notification-area tray icon, and the same keyboard behavior.

Windows 10's built-in Task Manager has no dark theme. This one takes its place system-wide: `Ctrl+Shift+Esc`, `Ctrl+Alt+Del` -> *Task Manager*, the taskbar right-click menu, and `taskmgr` all open it, **elevated, with no UAC prompt**. It uses Windows' own supported hook (`Image File Execution Options`, the same mechanism Sysinternals Process Explorer uses), with **no system files patched, replaced, or re-signed**. One click turns it on; one click reverts to the original Windows Task Manager.

---

## Highlights

- **Opens in ~0.3 s with live data:** CPU, memory, disks, network adapters, GPUs, and process names/icons are already populated on the very first paint—never a screen of zeros.
- **Uses ~0.1% CPU:** lightweight native GDI/WinForms rendering and background sampling.
- **Never wakes sleeping hard disks:** disk activity is read from the disk drivers' own counters without touching spun-down drives.
- **Single small native `.exe`:** `TaskManagerDark.exe` (~458 KB) plus a tiny fast launcher (`TaskManagerDarkLauncher.exe`, ~25 KB). Both target **.NET Framework 4.8**, which is built into every Windows 10 / 11 installation—nothing extra to install.

---

## Download & Quick Start

### Option 1: Download Pre-Built Release (No SDK Required)

1. Download **`TaskManagerDark-v1.25.zip`** from the **[Releases](https://github.com/1nstntKaRma/Task-Manager-Win10-Dark-Mode/releases/latest)** page and extract it anywhere.
2. Double-click **`Activate Task Manager Dark.cmd`** to make it your system-wide Task Manager (Windows will ask for administrator permission once).
3. To restore the original Windows Task Manager at any time, double-click **`Revert to Windows Task Manager.cmd`**.
4. Or simply double-click `bin\TaskManagerDark.exe` to run it standalone without installing.

> **Note on Windows SmartScreen / Unsigned Binaries:** The pre-built `.exe` files in Releases are not signed with a commercial Authenticode certificate, so Windows SmartScreen or some antivirus tools may show an *"Windows protected your PC"* prompt on first download (*More info* -> *Run anyway*). If you prefer not to run a downloaded executable, you can compile it yourself from the C# source in ~2 seconds using Option 2 below.

### Option 2: Build from Source

```powershell
git clone https://github.com/1nstntKaRma/Task-Manager-Win10-Dark-Mode.git
cd Task-Manager-Win10-Dark-Mode
.\build.ps1
```

Building requires any installed **.NET SDK** (used only for its Roslyn C# compiler `csc.dll`; the compiled output targets `.NET Framework 4.8`).

After building `bin\TaskManagerDark.exe` and `bin\TaskManagerDarkLauncher.exe`, `build.ps1` asks:

| Choice | Result |
|---|---|
| **Yes** | `Ctrl+Shift+Esc`, `Ctrl+Alt+Del` -> *Task Manager*, the taskbar menu, and `taskmgr` open Task Manager Dark, elevated, with no UAC prompt. |
| **Revert** | Restores the original Windows Task Manager. |
| **No** | Builds only; changes nothing on the system (default). |

You can also pass flags directly for non-interactive or scripted runs:

- `.\build.ps1 -Shell No` — build only, without prompting (ideal for CI or automated builds).
- `.\build.ps1 -Shell Yes` — build and activate system-wide replacement.
- `.\build.ps1 -NoBuild -Shell Revert` — revert to the original Windows Task Manager without rebuilding.
- `.\build.ps1 -NoBuild -Shell Clean` — remove every change the script ever made and return to factory defaults.

---

## How System-Wide Replacement & Elevation Work

When you choose **Yes** (or run `Activate Task Manager Dark.cmd`), Windows asks **once** for administrator permission (only this integration step runs elevated). The script then:

1. Copies `TaskManagerDark.exe` and `TaskManagerDarkLauncher.exe` to `%ProgramFiles%\TaskManagerDark` (an administrator-only folder, ensuring no unprivileged process can tamper with or swap the binary that runs elevated).
2. Registers the scheduled task `\TaskManagerDark` (*"Run with highest privileges"*, normal priority, scoped to your user account).
3. Sets `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskmgr.exe` -> `Debugger = "%ProgramFiles%\TaskManagerDark\TaskManagerDarkLauncher.exe"`.

When Windows launches Task Manager, it starts `TaskManagerDarkLauncher.exe` instead. The launcher brings an already-open Task Manager Dark window to the front, or triggers the `\TaskManagerDark` scheduled task (launching elevated with zero UAC prompt) and forwards command-line switches (such as `/startup` to open the Startup tab). If the scheduled task is not present, it starts `TaskManagerDark.exe` unelevated, and **File > Run as administrator** can elevate it on demand.

Everything created by `build.ps1` is tagged so **Revert** and **Clean** only remove their own entries. If another tool (such as Process Explorer) previously owned the `Debugger` value, `build.ps1` remembers it and restores it on **Revert**.

**Target OS:** Built and tested for **Windows 10 (22H2)**. The same `Image File Execution Options` mechanism also works on Windows 11, though Windows 11 22H2 and later already include a built-in dark Task Manager.

---

## Run Without Installing

Double-click `bin\TaskManagerDark.exe`. Without administrator rights it works as a portable application, except that:

- The per-process **Network** column shows `N/A` and **Disk** shows each process's total I/O (noted in the column tooltips).
- Starting/stopping system services, managing other users' processes, and reading Startup impact require administrator rights.

---

## Features

- **Fewer details / More details** views, remembered across launches.
- **Processes**: Apps, Background processes, and Windows processes; heat-map shading and per-column totals; sorting; type-to-search; *"Service Host: ..."* rows expandable to their hosted services; apps expandable to their windows and child processes; Resource values (Memory/Disk/Network as values or percentages); optional Type, Publisher, PID, Process name, and Command line columns.
- **Performance**: CPU (overall, logical processors, NUMA nodes, kernel times), Memory (with composition bar), every disk (SSD/HDD), every network adapter (including Wi-Fi SSID and signal strength), every GPU (engine graphs, dedicated/shared memory, driver version/date, DirectX version, physical location, temperature); graph summary view (double-click), sidebar summary view, Copy (`Ctrl+C`), and View network details.
- **App history** from SRUM (Windows' System Resource Usage Monitor database): CPU time, Network, Metered network, Tile updates, plus Non-metered network, Downloads, and Uploads; *Delete usage history*; *Show history for all processes*.
- **Startup**: Run keys (`HKCU`/`HKLM`, 64-bit and 32-bit `WOW6432Node`) and both Startup folders; Enable/Disable via `StartupApproved` exactly like Windows; Startup impact; Last BIOS time.
- **Users**: Signed-in sessions with their aggregated resource usage, expandable to per-session processes; Disconnect, Sign off, Send message, Connect.
- **Details**: Every *"Select columns"* column; End task, End process tree, Set priority, Set affinity, Analyze wait chain, UAC virtualization, Create dump file, Open file location, Search online, Properties, Go to service(s).
- **Services**: Start, Stop, Restart, Open Services (`services.msc`), Search online, Go to details.
- **Menus, tray icon, and keyboard shortcuts**: *Run new task* (with *"Create this task with administrative privileges"*), *Always on top*, *Minimize on use*, *Hide when minimized*, *Set default tab*, *Show full account name*, *Update speed* (High / Normal / Low / Paused), *Group by type*, *Expand/Collapse all*, *Status values*; live CPU usage meter in the system tray; `Ctrl+Tab` / `Ctrl+Shift+Tab`, holding `Ctrl` pauses updates, `Delete` ends selected task, `Alt` mnemonics, `F5` refresh.

Settings are stored in `%APPDATA%\TaskManagerDark\settings.ini`; unexpected errors are logged to `%APPDATA%\TaskManagerDark\errors.log` and reported in a dark dialog.

---

## Fast Startup & Disk-Sleep Architecture

The window opens with real figures in ~0.2–0.3 s:

- Sampling begins before the window is constructed and runs concurrently; the first sample is handed directly to the UI on first paint.
- Nothing that can block or stall sits on the UI or update path. Disk activity comes from the disk drivers' own counters (`IOCTL_DISK_PERFORMANCE`), which **never wake a sleeping hard disk**. Performance counters (CPU frequency, GPU engines) run on a dedicated background thread. Disk models, GPU driver details, network addresses, and Wi-Fi metadata load asynchronously. Disk *"Formatted"* capacity is queried only when that specific disk's page is selected.
- Process display names and icons load on first paint from `looks.cache` (stored alongside `settings.ini`), then revalidate in the background against each executable's file metadata.
- The **Startup** tab enumerates its entries on a background thread so a startup binary residing on a spun-down HDD cannot freeze the window.
- Each launch records a millisecond timeline in `%APPDATA%\TaskManagerDark\startup.log`.

---

## Where It Differs from the Original

- **Properties** opens a native dark property sheet (*General*, *Digital Signatures*, *Details*) rather than Explorer's light-themed dialog; the *Compatibility*, *Security*, and *Previous Versions* tabs are not reproduced.
- **Power usage / Power usage trend** are estimated from each process's CPU, GPU, and disk load (Windows' internal energy estimator has no public API). The trend reflects a 2-minute rolling average.
- **In use (Compressed)** displays the working set of the `Memory Compression` process in parentheses.
- **Operating system context** is read from the executable's embedded manifest; **Job object ID** is left blank and **Enterprise context** displays `N/A`.
- **GPU engine names** come from Windows' performance counters (*"3D"*, *"Copy"*, *"Video Decode"*, etc.) when the display driver does not expose custom engine names.
- **Provide feedback** (Feedback Hub) is omitted. **File > Run as administrator** is added (shown only when running unelevated).
- Colors are dark-adapted (performance graphs keep Task Manager's familiar hues; heat-map cells deepen from amber to orange and red).

---

## Testing Hook

`TaskManagerDark.exe --test-hook` accepts scripted commands over `WM_COPYDATA` for automated UI testing (synthetic input straight into controls and off-screen screenshots). It is completely disabled unless `--test-hook` is passed on the command line, and in that mode the physical mouse is ignored, the clipboard is untouched, and no tray icon or taskbar button is created.

---

## License & Disclaimer

Licensed under the [MIT License](LICENSE).

**Disclaimer:** This is an independent open-source project and is not affiliated with, endorsed by, or sponsored by Microsoft Corporation. *"Windows"* and *"Task Manager"* are registered trademarks of Microsoft Corporation. All icons and UI rendering in this repository are generated by original code, with no Microsoft binaries or assets copied.