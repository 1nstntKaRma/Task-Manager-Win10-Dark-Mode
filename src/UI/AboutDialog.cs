using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TaskManagerDark
{
    /// <summary>
    /// Options > About: the version that is running (to check an update arrived), which copy it is, whether it runs
    /// as administrator and replaces the Windows Task Manager, and where the project lives. Ctrl+C copies it all.
    /// </summary>
    public sealed class AboutDialog : DarkDialog
    {
        const string ProjectUrl = "https://github.com/1nstntKaRma/Task-Manager-Win10-Dark-Mode";
        readonly string _summary;

        AboutDialog(MainForm main) : base(main, Strings.AboutTitle)
        {
            SetClientSizeScaled(420, 214);
            string version = Version;
            string where = Where(out bool installed), status = Status(installed);

            var title = AddLabel(Strings.ProductName, 72, 14, 332, 26);
            title.Font = Theme.GetFont(D, 17f, FontStyle.Regular);
            var ver = AddLabel("Version " + version, 72, 42, 332, 22);
            ver.Font = Theme.GetFont(D, 13f, FontStyle.Bold);
            AddLabel(Strings.AboutDescription, 72, 66, 332, 20, true);
            AddLabel(where, 72, 92, 332, 20, true).Flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.PathEllipsis;
            AddLabel(status, 72, 110, 332, 20, true).Flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

            var project = new DarkLink { Text = Strings.AboutProjectPage, Bounds = R(72, 136, 90, 20), Font = Font };
            project.Click += (s, e) => ShellLaunch.RunUnelevated(ProjectUrl, null);
            Controls.Add(project);
            var updates = new DarkLink { Text = Strings.AboutCheckUpdates, Bounds = R(172, 136, 120, 20), Font = Font };
            updates.Click += (s, e) => ShellLaunch.RunUnelevated(ProjectUrl + "/releases/latest", null);
            Controls.Add(updates);

            AddLabel(Strings.AboutFooter, 16, 176, 300, 32, true).Flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
            AddButton("OK", 328, 181, 76, DialogResult.OK, true);
            CancelButton = AcceptButton;

            _summary = Strings.ProductName + " " + version + "\r\n" + where + "\r\n" + status + "\r\n" + ProjectUrl;
        }

        public static void ShowAsync(MainForm main) => DialogRunner.Show(main, () => new AboutDialog(main));

        /// <summary>"1.36" (or "1.36.1"): the version stamped into this program from the VERSION file.</summary>
        public static string Version
        {
            get
            {
                var v = typeof(AboutDialog).Assembly.GetName().Version;
                return v.Major + "." + v.Minor + (v.Build > 0 ? "." + v.Build : "");
            }
        }

        /// <summary>Which copy runs: the installed one, or a build somewhere else.</summary>
        static string Where(out bool installed)
        {
            string dir = Path.GetDirectoryName(Program.ExePath).TrimEnd('\\');
            string installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "TaskManagerDark");
            installed = string.Equals(dir, installDir, StringComparison.OrdinalIgnoreCase);
            return (installed ? "Installed in " : "Running from ") + dir;
        }

        /// <summary>Its rights, and whether Ctrl+Shift+Esc opens Task Manager Dark (this copy, or the installed one).</summary>
        static string Status(bool installed)
        {
            bool replaces = false;
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\taskmgr.exe"))
                    replaces = (k?.GetValue("Debugger") as string)?.IndexOf("TaskManagerDarkLauncher.exe", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch (Exception ex) { ErrorLog.Write("About", ex); }
            return (Sys.IsElevated ? "Administrator" : "Standard user") + "  \u00B7  " +
                   (!replaces ? "Windows Task Manager not replaced" : installed ? "Opens with Ctrl+Shift+Esc" : "Ctrl+Shift+Esc opens the installed copy");
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int s = D.S(40);
            var state = e.Graphics.Save();
            e.Graphics.TranslateTransform(D.S(18), D.S(18));
            Glyphs.AppIcon(e.Graphics, s);
            e.Graphics.Restore(state);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.C))
            {
                LastCopied = _summary;
                if (!TestHook.Enabled) { try { Clipboard.SetText(_summary); } catch (Exception ex) { ErrorLog.Write("About copy", ex); } }
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>What Ctrl+C copied last (tests read it here).</summary>
        public static string LastCopied;
    }
}
