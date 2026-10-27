using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Task Manager")]
[assembly: AssemblyDescription("Task Manager - Dark Mode")]
[assembly: AssemblyProduct("Task Manager - Dark Mode")]
[assembly: AssemblyCompany("Task Manager Dark Mode")]
[assembly: AssemblyCopyright("Task Manager - Dark Mode")]
[assembly: AssemblyVersion("1.25.0.0")]
[assembly: AssemblyFileVersion("1.25.0.0")]
[assembly: ComVisible(false)]

namespace TaskManagerDark
{
    static class Program
    {
        public const string WindowProp = "TaskManagerDark.Main";
        public const string ActivateMessageName = "TaskManagerDark.Activate";
        public static int ActivateMessage;
        public static MainForm Window;
        /// <summary>Created before the window so the first sample is taken while the window is being built.</summary>
        public static DataEngine Engine;
        public static bool NoEtw, Replace, FromLauncher, Offscreen, TestTray;
        public static int StartTab = -1;
        public static string ExePath => Assembly.GetExecutingAssembly().Location;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetPropW")] static extern IntPtr GetProp(IntPtr hwnd, string name);

        [STAThread]
        static int Main(string[] args)
        {
            Thread.CurrentThread.Name = "UI";
            StartupLog.Begin();
            FastConfig.Install();
            if (args.Length == 2 && args[0] == "--export-icon") return ExportIcon(args[1]);
            var rest = ParseArgs(args);
            ActivateMessage = Sys.RegisterWindowMessage(ActivateMessageName);

            if (!TestHook.Enabled && !Replace)
            {
                // one Task Manager per session: hand over to the running one
                var existing = FindRunningWindow();
                if (existing != IntPtr.Zero)
                {
                    Sys.GetWindowThreadProcessId(existing, out int pid);
                    Sys.AllowSetForegroundWindow(pid);
                    Native.PostMessage(existing, ActivateMessage, (IntPtr)(StartTab + 1), IntPtr.Zero);
                    return 0;
                }
            }
            if (Replace) WaitForOldWindow();

            try
            {
                var dir = Settings.OverrideDir ?? Settings.DataDir;
                Directory.CreateDirectory(dir);
                System.Runtime.ProfileOptimization.SetProfileRoot(dir);
                System.Runtime.ProfileOptimization.StartProfile("startup.profile");
            }
            catch (Exception ex) { ErrorLog.Write("ProfileOptimization", ex); }
            try { Native.SetCurrentProcessExplicitAppUserModelID("1nstntKaRma.TaskManagerDark"); }
            catch (Exception ex) { ErrorLog.Write("SetCurrentProcessExplicitAppUserModelID", ex); }
            if (Sys.IsElevated) { Sys.EnablePrivilege("SeDebugPrivilege"); Sys.EnablePrivilege("SeProfileSingleProcessPrivilege"); }
            Sys.SetPriorityClass(Sys.GetCurrentProcess(), 0x80);   // HIGH_PRIORITY_CLASS, like Taskmgr: stays responsive when the system is busy
            // sampling starts now, while the window is being built
            Settings.Load();
            try { Engine = new DataEngine(new Dpi(MainForm.ExpectedDpi()).S(16)); }
            catch (Exception ex) { ErrorLog.Write("DataEngine", ex); }

            DarkMode.InitProcess();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ReportError(e.Exception, false);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ReportError(e.ExceptionObject as Exception, true);

            StartupLog.Mark("creating the window");
            Window = new MainForm();
            Application.Run(Window);
            return 0;
        }

        /// <summary>
        /// Windows starts "Debugger" programs as: "debugger.exe" "C:\Windows\system32\taskmgr.exe" /args...
        /// The taskmgr path is dropped; Task Manager's own switches (/startup) are honored.
        /// </summary>
        static List<string> ParseArgs(string[] args)
        {
            var rest = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (i == 0 && a.EndsWith("taskmgr.exe", StringComparison.OrdinalIgnoreCase)) continue;
                switch (a.ToLowerInvariant())
                {
                    case "--test-hook": TestHook.Enabled = true; continue;
                    case "--no-etw": NoEtw = true; continue;
                    case "--offscreen": Offscreen = true; continue;
                    case "--tray": TestTray = true; continue;
                    case "--replace": Replace = true; continue;
                    case "--from-launcher": FromLauncher = true; continue;
                    case "--settings-dir": if (i + 1 < args.Length) Settings.OverrideDir = args[++i]; continue;
                    case "/startup": StartTab = 3; continue;
                }
                if (a.StartsWith("--tab=", StringComparison.OrdinalIgnoreCase) && int.TryParse(a.Substring(6), out int t)) { StartTab = t; continue; }
                // startup tests: make the slow sources slower, to prove nothing on screen waits for them
                if (a.StartsWith("--debug-slow-counters=", StringComparison.OrdinalIgnoreCase) && int.TryParse(a.Substring(22), out int dc)) { CounterSampler.DebugDelayMs = dc; continue; }
                if (a.StartsWith("--debug-slow-first=", StringComparison.OrdinalIgnoreCase) && int.TryParse(a.Substring(19), out int df)) { DataEngine.DebugFirstDelayMs = df; continue; }
                if (a.StartsWith("--debug-slow-facts=", StringComparison.OrdinalIgnoreCase) && int.TryParse(a.Substring(19), out int dd)) { DiskSampler.DebugFactsDelayMs = dd; continue; }
                rest.Add(a);
            }
            return rest;
        }

        public static IntPtr FindRunningWindow()
        {
            IntPtr found = IntPtr.Zero;
            int me = Sys.GetCurrentProcessId();
            Sys.EnumWindows((h, l) =>
            {
                if (GetProp(h, WindowProp) == IntPtr.Zero) return true;
                Sys.GetWindowThreadProcessId(h, out int pid);
                if (pid == me) return true;
                found = h;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        static void WaitForOldWindow()
        {
            for (int i = 0; i < 60 && FindRunningWindow() != IntPtr.Zero; i++) Thread.Sleep(50);
        }

        /// <summary>Build helper: writes the multi-resolution application icon (PNG entries).</summary>
        static int ExportIcon(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            var images = new List<byte[]>();
            foreach (var s in sizes)
            {
                using (var bmp = new System.Drawing.Bitmap(s, s, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (var g = System.Drawing.Graphics.FromImage(bmp))
                    {
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                        Glyphs.AppIcon(g, s);
                    }
                    using (var ms = new MemoryStream()) { bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png); images.Add(ms.ToArray()); }
                }
            }
            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                    w.Write(images[i].Length); w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (var img in images) w.Write(img);
            }
            return 0;
        }

        static int _reporting;

        /// <summary>Logs an unexpected error and shows it in a dark dialog.</summary>
        public static void ReportError(Exception ex, bool fatal)
        {
            if (ex == null) return;
            ErrorLog.Write(fatal ? "FATAL" : "Unhandled", ex);
            if (TestHook.Enabled) { TestHook.LastError = ex.ToString(); return; }
            if (Interlocked.Exchange(ref _reporting, 1) == 1) return;
            try
            {
                string msg = (fatal ? "Task Manager needs to close." : "You can keep working.") + "\n\n" + ex.Message +
                             "\n\nDetails were written to:\n" + Path.Combine(Settings.OverrideDir ?? Settings.DataDir, "errors.log");
                if (Window != null && !Window.IsDisposed)
                    MessageDialog.Show(Window, Strings.AppTitle, "Task Manager ran into a problem", msg, new[] { "OK" }, 0, MessageDialog.Kind.Error, null, out _);
                else MessageBox.Show(msg, Strings.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception inner) { ErrorLog.Write("Error dialog", inner); }
            finally { Interlocked.Exchange(ref _reporting, 0); }
        }
    }
}
