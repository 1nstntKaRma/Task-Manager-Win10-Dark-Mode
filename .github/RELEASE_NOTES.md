## Task Manager - Dark Mode v1.38

🛠️⚙️ Windows 10 - Task Manager Dark Mode ⚡ Full replacement.

### What's New in v1.38
- **`Ctrl+Shift+Esc` works again after signing out and back in.** When Windows kept the session you signed out of alive (a leftover background process can do that), the elevated task started Task Manager Dark in that old session, where nothing can open, so `Ctrl+Shift+Esc` did nothing. The task now always starts in the session you pressed the keys in.
- **File > Run as administrator** no longer closes the window when the elevated copy cannot start; it asks through UAC instead.
- If the elevated start still fails, `Ctrl+Shift+Esc` opens Task Manager Dark without administrator rights and writes the reason to `%APPDATA%\TaskManagerDark\errors.log`.

Updating from v1.36 or earlier? v1.37 added **Options > About**, which shows the version you are running. See the [v1.37 notes](https://github.com/1nstntKaRma/Task-Manager-Win10-Dark-Mode/releases/tag/v1.37).

### Update
1. Download **TaskManagerDark-v1.38.zip** and extract all files (or use the PowerShell one-liner from the README).
2. Double-click **Activate Task Manager Dark.cmd** (or run `build.ps1` and choose **[1] Apply Dark Mode**).
3. Open **Options > About**: it should say **Version 1.38**.
4. To go back at any time, double-click **Revert to Windows Task Manager.cmd**.
