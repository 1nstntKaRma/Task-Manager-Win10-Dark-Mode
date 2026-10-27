@echo off
rem Makes Task Manager Dark the system Task Manager (Ctrl+Shift+Esc, Ctrl+Alt+Del, taskbar, "taskmgr").
rem Builds first if needed. Undo with "Revert to Windows Task Manager.cmd".
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Shell Apply