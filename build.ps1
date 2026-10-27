# Builds Task Manager - Dark Mode -> bin\TaskManagerDark.exe (+ bin\TaskManagerDarkLauncher.exe),
# then offers to make it the system's Task Manager.
#
# Uses the Roslyn compiler shipped with any installed .NET SDK and targets .NET Framework 4.8,
# which is part of every Windows 10 / 11 installation (nothing extra to install for users).
#
#   .\build.ps1                         build, then ask about system integration
#   .\build.ps1 -Shell Yes              build, then Ctrl+Shift+Esc etc. open Task Manager Dark
#   .\build.ps1 -Shell Revert           build, then the original Windows Task Manager again
#   .\build.ps1 -Shell No               build only
#   .\build.ps1 -NoBuild -Shell ...     only change the system integration
#   .\build.ps1 -NoBuild -Shell Clean   remove every change this script ever made (factory state)
#
# "Yes" needs administrator rights once (Windows asks): it installs the two programs into
# %ProgramFiles%\TaskManagerDark, registers an elevated scheduled task and sets the Image File
# Execution Options "Debugger" of taskmgr.exe to the launcher. Nothing is patched or replaced.
param(
    [switch]$Debug,
    [ValidateSet('Ask', 'Yes', 'Revert', 'No', 'Clean')][string]$Shell = 'Ask',
    [switch]$NoBuild,
    [switch]$Elevated,      # internal: set when the script relaunched itself as administrator
    [string]$UserSid,       # internal: the user the scheduled task runs for
    [string]$LogFile        # internal: output of the elevated step, shown by the original window
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$src = Join-Path $root 'src'
$bin = Join-Path $root 'bin'
$obj = Join-Path $root 'obj'
$exe = Join-Path $bin 'TaskManagerDark.exe'
$launcherExe = Join-Path $bin 'TaskManagerDarkLauncher.exe'

$Marker = 'TaskManagerDark'
$TaskName = 'TaskManagerDark'
$InstallDir = Join-Path $env:ProgramFiles 'TaskManagerDark'
$InstalledExe = Join-Path $InstallDir 'TaskManagerDark.exe'
$InstalledLauncher = Join-Path $InstallDir 'TaskManagerDarkLauncher.exe'
$InstallMarker = Join-Path $InstallDir 'installed-by-build.ps1.txt'
$IfeoPath = 'SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskmgr.exe'
$EtwSession = 'TaskManagerDark Kernel Trace'

function Say([string]$text, [string]$color = '') {
    if ($color) { Write-Host $text -ForegroundColor $color } else { Write-Host $text }
    if ($LogFile) { Add-Content -Path $LogFile -Value $text -Encoding UTF8 }
}

# ======================================================================================= build
function Invoke-Build {
    New-Item -ItemType Directory -Force $bin, $obj | Out-Null
    $csc = Get-ChildItem "$env:ProgramFiles\dotnet\sdk\*\Roslyn\bincore\csc.dll" -ErrorAction SilentlyContinue |
        Sort-Object { [version]($_.FullName -replace '.*\\sdk\\([\d\.]+)[^\\]*\\.*', '$1') } -Descending | Select-Object -First 1
    if (-not $csc) { throw 'No .NET SDK found (needed only to build).' }

    $fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
    if (-not (Test-Path $fw)) { $fw = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319" }
    $refs = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Xml.dll', 'System.Management.dll', 'System.Configuration.dll') |
        ForEach-Object { "-r:$fw\$_" }
    $files = Get-ChildItem $src -Filter *.cs -Recurse | ForEach-Object { $_.FullName }
    $common = @('-nologo', '-noconfig', '-nostdlib', '-target:winexe', '-platform:anycpu', '-unsafe', '-langversion:latest',
                '-deterministic', '-nowarn:CS0169,CS0414,CS0649,CS0168,CS0219,CS1998,CS0108,CS1690')
    if ($Debug) { $common += @('-debug:portable', '-optimize-', '-define:DEBUG') } else { $common += @('-optimize+', '-debug-') }

    function Compile($out, $manifest, $sources, $references, $extra) {
        # (an empty array passed positionally arrives as $null; splatting $null would hand csc an empty argument)
        $argv = @($common) + @("-win32manifest:$manifest") + @($extra | Where-Object { $_ }) + @("-out:$out") + @($references) + @($sources)
        & dotnet $csc.FullName @argv
        if ($LASTEXITCODE -ne 0) { throw "Compilation failed ($LASTEXITCODE)" }
    }

    foreach ($target in @($exe, $launcherExe)) {
        $running = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $target }
        if ($running) { throw "$(Split-Path $target -Leaf) is running from $bin. Close it and build again." }
    }

    $ico = Join-Path $obj 'app.ico'
    $tmp = Join-Path $obj 'TaskManagerDark.noicon.exe'
    # first pass without an icon, then let the app draw its own icon (always redrawn so icon changes apply)
    Compile $tmp "$src\app.manifest" $files $refs @()
    & $tmp --export-icon $ico | Out-Null
    Start-Sleep -Milliseconds 200
    if (-not (Test-Path $ico)) { throw 'Icon export failed.' }
    Compile $exe "$src\app.manifest" $files $refs @("-win32icon:$ico")

    # the tiny launcher that Windows starts instead of taskmgr.exe (no WinForms, so it starts fast)
    $launcherSrc = Get-ChildItem (Join-Path $root 'launcher') -Filter *.cs | ForEach-Object { $_.FullName }
    $launcherRefs = @('mscorlib.dll', 'System.dll') | ForEach-Object { "-r:$fw\$_" }
    Compile $launcherExe (Join-Path $root 'launcher\launcher.manifest') $launcherSrc $launcherRefs @("-win32icon:$ico")

    $size = [math]::Round((Get-Item $exe).Length / 1KB)
    $lsize = [math]::Round((Get-Item $launcherExe).Length / 1KB)
    Write-Host "Built $exe ($size KB) and $(Split-Path $launcherExe -Leaf) ($lsize KB) with $($csc.Directory.Parent.Parent.Name)" -ForegroundColor Green
    if ((Test-Path $InstallMarker) -and (Test-Path $InstalledExe)) {
        $same = (Get-FileHash $exe).Hash -eq (Get-FileHash $InstalledExe).Hash
        if (-not $same) { Write-Host "  The installed copy in $InstallDir is older. Choose Yes (or -Shell Yes) to update it." -ForegroundColor Yellow }
    }
}

# ======================================================================================= helpers
function Test-Admin {
    $p = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-OsBuild {
    try { return [int](Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -Name CurrentBuildNumber).CurrentBuildNumber } catch { return [Environment]::OSVersion.Version.Build }
}

function Test-OurDebugger($value) {
    return $value -and ($value -like '*TaskManagerDarkLauncher.exe*' -or $value -like '*TaskManagerDark.exe*')
}

function Get-RegistryViews {
    # the IFEO key exists in the 64-bit and the 32-bit registry view (they may be the same key)
    $views = @([Microsoft.Win32.RegistryView]::Registry64)
    if ([Environment]::Is64BitOperatingSystem) { $views += [Microsoft.Win32.RegistryView]::Registry32 }
    return $views
}

function Show-Windows11Note {
    $build = Get-OsBuild
    if ($build -ge 22000) {
        Say "  Windows 11 detected (build $build). Image File Execution Options still applies, so Yes works here too." 'Yellow'
        if ($build -ge 22621) { Say '  Note: Windows 11 22H2 and later already include a dark Task Manager.' 'Yellow' }
    }
}

# ======================================================================================= integration (admin)
function Install-Files {
    if (-not (Test-Path $exe) -or -not (Test-Path $launcherExe)) { throw "Build first: $exe was not found." }
    $running = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -and ($_.Path -ieq $InstalledExe -or $_.Path -ieq $InstalledLauncher) }
    if ($running) { throw "Task Manager Dark is running from $InstallDir. Close it (File > Exit) and try again." }
    if ((Test-Path $InstallDir) -and -not (Test-Path $InstallMarker)) { throw "$InstallDir exists but was not created by this script; it was left alone." }
    New-Item -ItemType Directory -Force $InstallDir | Out-Null
    Copy-Item -LiteralPath $exe, $launcherExe -Destination $InstallDir -Force
    Set-Content -Path $InstallMarker -Encoding ASCII -Value @(
        "Installed by $PSCommandPath ($Marker) on $(Get-Date -Format s).",
        'Remove with: build.ps1 -NoBuild -Shell Revert (or -Shell Clean).')
    Say "  Installed to $InstallDir (an administrator-only folder, so nothing unelevated can swap the elevated program)."
}

function Remove-Files {
    if (-not (Test-Path $InstallDir)) { return }
    if (-not (Test-Path $InstallMarker)) { Say "  $InstallDir was not created by this script; left alone." 'Yellow'; return }
    $running = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -and ($_.Path -ieq $InstalledExe -or $_.Path -ieq $InstalledLauncher) }
    if ($running) { throw "Task Manager Dark is running from $InstallDir. Close it (File > Exit) and try again." }
    Remove-Item -LiteralPath $InstallDir -Recurse -Force
    Say "  Removed $InstallDir"
}

function Install-Task([string]$sid) {
    $esc = { param($s) [Security.SecurityElement]::Escape($s) }
    $desc = "Starts Task Manager - Dark Mode elevated without a UAC prompt (used by Ctrl+Shift+Esc through $InstalledLauncher). Created by build.ps1 [$Marker]."
    $xml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>$(& $esc $desc)</Description>
    <URI>\$TaskName</URI>
  </RegistrationInfo>
  <Triggers />
  <Principals>
    <Principal id="Author">
      <UserId>$sid</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>Parallel</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>false</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>4</Priority>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>"$(& $esc $InstalledExe)"</Command>
      <Arguments>`$(Arg0)</Arguments>
      <WorkingDirectory>$(& $esc $InstallDir)</WorkingDirectory>
    </Exec>
  </Actions>
</Task>
"@
    $existing = Get-ScheduledTask -TaskName $TaskName -TaskPath '\' -ErrorAction SilentlyContinue
    if ($existing -and $existing.Description -notlike "*[$Marker]*") { throw "A scheduled task named '$TaskName' exists that this script did not create; it was left alone." }
    Register-ScheduledTask -TaskName $TaskName -TaskPath '\' -Xml $xml -Force | Out-Null
    Say "  Scheduled task '\$TaskName' (Run with highest privileges, for $sid)"
}

function Remove-Task {
    $t = Get-ScheduledTask -TaskName $TaskName -TaskPath '\' -ErrorAction SilentlyContinue
    if (-not $t) { return }
    if ($t.Description -notlike "*[$Marker]*") { Say "  The scheduled task '$TaskName' was not created by this script; left alone." 'Yellow'; return }
    Unregister-ScheduledTask -TaskName $TaskName -TaskPath '\' -Confirm:$false
    Say "  Removed scheduled task '\$TaskName'"
}

function Set-Ifeo {
    $cmd = '"' + $InstalledLauncher + '"'
    foreach ($view in Get-RegistryViews) {
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $view)
        try {
            $k = $base.OpenSubKey($IfeoPath, $true)
            $created = $false
            if (-not $k) { $k = $base.CreateSubKey($IfeoPath); $created = $true }
            try {
                $prev = $k.GetValue('Debugger')
                $ours = $null -ne $k.GetValue($Marker)
                if ($prev -and -not (Test-OurDebugger $prev) -and -not $ours) {
                    # another tool (e.g. Process Explorer) owned the hook: remember it so Revert can put it back
                    $k.SetValue("$Marker.PreviousDebugger", [string]$prev)
                }
                $k.SetValue('Debugger', $cmd)
                if (-not $ours) { $k.SetValue($Marker, $(if ($created) { 'created' } else { 'value' })) }
            }
            finally { $k.Close() }
        }
        finally { $base.Close() }
    }
    Say "  Image File Execution Options: taskmgr.exe -> $cmd"
}

function Remove-Ifeo {
    foreach ($view in Get-RegistryViews) {
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $view)
        try {
            $k = $base.OpenSubKey($IfeoPath, $true)
            if (-not $k) { continue }
            $deleteKey = $false
            try {
                $dbg = $k.GetValue('Debugger')
                $mark = $k.GetValue($Marker)
                $prev = $k.GetValue("$Marker.PreviousDebugger")
                if (Test-OurDebugger $dbg) {
                    if ($prev) { $k.SetValue('Debugger', [string]$prev); Say "  Debugger restored to the previous value ($prev)" }
                    else { $k.DeleteValue('Debugger', $false); Say '  Debugger value removed (taskmgr.exe starts normally again)' }
                }
                elseif ($dbg) { Say "  The Debugger value points to another program ($dbg); left alone." 'Yellow' }
                $k.DeleteValue("$Marker.PreviousDebugger", $false)
                $k.DeleteValue($Marker, $false)
                $left = @($k.GetValueNames() | Where-Object { $_ -ne '' }).Count + $k.SubKeyCount
                $deleteKey = ($mark -eq 'created') -and ($left -eq 0)
            }
            finally { $k.Close() }
            if ($deleteKey) { $base.DeleteSubKey($IfeoPath, $false) }
        }
        finally { $base.Close() }
    }
}

function Stop-EtwSession {
    # a session left behind if Task Manager Dark ever crashed while elevated
    $q = & logman query $EtwSession -ets 2>$null
    if ($LASTEXITCODE -eq 0) { & logman stop $EtwSession -ets | Out-Null; Say "  Stopped the leftover trace session '$EtwSession'" }
}

function Invoke-Integration([string]$mode) {
    switch ($mode) {
        'Yes' {
            Say 'System integration -> Task Manager Dark' 'Cyan'
            Show-Windows11Note
            $sid = if ($UserSid) { $UserSid } else { [Security.Principal.WindowsIdentity]::GetCurrent().User.Value }
            Install-Files
            Install-Task $sid
            Set-Ifeo
            Say '  Done: Ctrl+Shift+Esc, Ctrl+Alt+Del > Task Manager, the taskbar menu and "taskmgr" now open Task Manager Dark (elevated, no prompt).' 'Green'
        }
        'Revert' {
            Say 'System integration -> the original Windows Task Manager' 'Cyan'
            Remove-Ifeo
            Remove-Task
            Remove-Files
            Say '  Done: Windows Task Manager is back.' 'Green'
        }
        'Clean' {
            Say 'System integration -> removing every change made by this script' 'Cyan'
            Remove-Ifeo
            Remove-Task
            Remove-Files
            Stop-EtwSession
            Say '  Done: the system is back to the Windows defaults.' 'Green'
        }
    }
}

# Image File Execution Options and scheduled tasks are machine settings: only that step runs as administrator.
function Invoke-IntegrationElevated([string]$mode) {
    if (Test-Admin) { Invoke-Integration $mode; return }
    Write-Host ''
    Write-Host 'This step changes machine-wide settings (Image File Execution Options for taskmgr.exe, an elevated' -ForegroundColor Cyan
    Write-Host 'scheduled task and %ProgramFiles%\TaskManagerDark), so Windows will ask once for administrator permission.' -ForegroundColor Cyan
    $log = Join-Path $env:TEMP ('TaskManagerDark-build-' + [guid]::NewGuid().ToString('N') + '.log')
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $argList = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -NoBuild -Shell $mode -Elevated -UserSid $sid -LogFile `"$log`""
    try { $p = Start-Process -FilePath 'powershell.exe' -ArgumentList $argList -Verb RunAs -WindowStyle Hidden -Wait -PassThru }
    catch { Write-Host 'Administrator permission was not given, so nothing was changed.' -ForegroundColor Yellow; return }
    if (Test-Path $log) { Get-Content $log -Encoding UTF8 | ForEach-Object { Write-Host $_ }; Remove-Item $log -Force }
    if ($p.ExitCode -ne 0) { throw "The administrator step failed (exit code $($p.ExitCode))." }
}

function Test-Interactive {
    try { return [Environment]::UserInteractive -and -not [Console]::IsInputRedirected -and $Host.Name -eq 'ConsoleHost' } catch { return $false }
}

function Test-OwnConsole {
    # true when this script got its own console window (e.g. "Run with PowerShell"), so we pause before it closes
    try {
        if (-not ('TaskManagerDarkBuild.Console' -as [type])) {
            Add-Type -Namespace 'TaskManagerDarkBuild' -Name 'Console' -MemberDefinition '[DllImport("kernel32.dll")] public static extern uint GetConsoleProcessList(uint[] list, uint count);'
        }
        $list = New-Object 'uint[]' 8
        return [TaskManagerDarkBuild.Console]::GetConsoleProcessList($list, 8) -le 1
    }
    catch { return $false }
}

# ======================================================================================= main
if ($Elevated) {
    # the relaunched administrator copy: do the work, report through the log file
    try { Invoke-Integration $Shell; exit 0 }
    catch { Say "ERROR: $($_.Exception.Message)" 'Red'; exit 1 }
}

$ownConsole = Test-OwnConsole
$asked = $false
try {
    if (-not $NoBuild) { Invoke-Build }

    if ($Shell -eq 'Ask') {
        if (Test-Interactive) {
            Show-Windows11Note
            $choices = [System.Management.Automation.Host.ChoiceDescription[]]@(
                (New-Object System.Management.Automation.Host.ChoiceDescription '&Yes', 'Ctrl+Shift+Esc and every "Task Manager" entry point open Task Manager Dark (asks for administrator permission once).'),
                (New-Object System.Management.Automation.Host.ChoiceDescription '&Revert', 'The original Windows Task Manager again (removes only what this script added).'),
                (New-Object System.Management.Automation.Host.ChoiceDescription '&No', 'Change nothing.'))
            $msg = "`n  Yes    - Ctrl+Shift+Esc, Ctrl+Alt+Del and the taskbar open Task Manager Dark (elevated, no prompt)`n" +
                   "  Revert - the original Windows Task Manager again`n" +
                   "  No     - change nothing`n"
            $pick = $Host.UI.PromptForChoice('Task Manager', $msg, $choices, 2)
            $asked = $true
            $Shell = @('Yes', 'Revert', 'No')[$pick]
        }
        else { $Shell = 'No' }
    }

    if ($Shell -ne 'No') { Invoke-IntegrationElevated $Shell }
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    if ($ownConsole -and (Test-Interactive) -and $Shell -eq 'Ask') { Read-Host 'Press Enter to close' | Out-Null }
    exit 1
}
if ($ownConsole -and $asked) { Read-Host "`nDone. Press Enter to close" | Out-Null }
