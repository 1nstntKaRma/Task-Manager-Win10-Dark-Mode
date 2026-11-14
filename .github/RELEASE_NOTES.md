## Task Manager - Dark Mode v1.37

🛠️⚙️ Windows 10 - Task Manager Dark Mode ⚡ Full replacement.

### What's New in v1.37
- **Options > About:** shows the version you are running (so you can confirm an update arrived), which copy runs (the installed one or a local build), whether it runs as administrator, and whether `Ctrl+Shift+Esc` opens it. Links to the project page and to the latest release; `Ctrl+C` copies the details.
- `SHA256SUMS.txt` now uses LF line endings, so `sha256sum -c SHA256SUMS.txt` works everywhere, and the launcher is published as its own asset.

Updating from v1.35? v1.36 added the `Ctrl+F` filter, Suspend / Resume, Efficiency mode, `Ctrl+C` row copy, longer graph history and a security fix for the elevated launch. See the [v1.36 notes](https://github.com/1nstntKaRma/Task-Manager-Win10-Dark-Mode/releases/tag/v1.36).

### Update
1. Download **TaskManagerDark-v1.37.zip** and extract all files (or use the PowerShell one-liner from the README).
2. Double-click **Activate Task Manager Dark.cmd** (or run `build.ps1` and choose **[1] Apply Dark Mode**).
3. Open **Options > About**: it should say **Version 1.37**.
4. To go back at any time, double-click **Revert to Windows Task Manager.cmd**.
