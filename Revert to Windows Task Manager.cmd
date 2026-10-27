@echo off
rem Puts the original Windows Task Manager back (removes only what build.ps1 added).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -NoBuild -Shell Revert
echo.
pause
