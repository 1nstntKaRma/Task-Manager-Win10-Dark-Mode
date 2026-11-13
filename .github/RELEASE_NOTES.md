## Task Manager - Dark Mode v1.36

🛠️⚙️ Windows 10 - Task Manager Dark Mode ⚡ Full replacement.

### What's New in v1.36
- **Filter (Ctrl+F):** a magnifier next to the ★ opens a slim filter field in the tab row. Typing filters Processes, Details, Services, Startup, Users and App history by name, PID, publisher or description. Matching child processes and services are revealed, group counts follow, `Esc` clears, `↓` / `Enter` jump to the first match.
- **Suspend / Resume:** right-click a process on Processes or Details.
- **Efficiency mode:** EcoQoS plus Low priority, as in Windows 11 (asks once; turning it off restores the earlier priority). Suspend and Efficiency mode are never offered for Windows itself, the desktop shell or Task Manager, so they cannot freeze or slow them.
- **Copy rows (Ctrl+C):** copies the selected rows of any list as tab-separated text with column titles (pastes straight into Excel).
- **Graph time span:** right-click a Performance graph for the standard 60 updates, 5 minutes or 10 minutes. Hover any graph to read the exact value and how long ago it was measured.

### Security & Reliability
- The elevated copy started by the `\TaskManagerDark` scheduled task now accepts only Task Manager's own switches, so no other program can start it with testing or debugging options. **Choose Apply Dark Mode again after updating** so the installed copy and the scheduled task are refreshed.
- `build.ps1` verifies everything it downloads: the Roslyn compiler against the hash NuGet publishes, the pre-built release against its `SHA256SUMS.txt`.
- The pre-built download asks for the release that matches the script's version (one `VERSION` file now drives both programs, the build and the release).
- Building from a folder with spaces in its path (for example a Windows user name with a space) no longer hangs or skips the icon.
- Releases are now built by GitHub Actions with build provenance.

### Update
1. Download **TaskManagerDark-v1.36.zip** and extract all files (or use the PowerShell one-liner from the README).
2. Double-click **Activate Task Manager Dark.cmd** (or run `build.ps1` and choose **[1] Apply Dark Mode**).
3. To go back at any time, double-click **Revert to Windows Task Manager.cmd**.
