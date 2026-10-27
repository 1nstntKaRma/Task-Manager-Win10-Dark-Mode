@echo off
rem Makes Task Manager Dark the system Task Manager (Ctrl+Shift+Esc, Ctrl+Alt+Del, taskbar, "taskmgr").
rem Builds first if needed. Undo with "Revert to Windows Task Manager.cmd".
if exist "%~dp0bin\TaskManagerDark.exe" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -NoBuild -Shell Yes
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Shell Yes
)
echo.
pause
