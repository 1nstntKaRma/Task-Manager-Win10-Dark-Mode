using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

[assembly: AssemblyTitle("Task Manager")]
[assembly: AssemblyDescription("Starts Task Manager - Dark Mode")]
[assembly: AssemblyProduct("Task Manager - Dark Mode")]
[assembly: AssemblyVersion("1.25.0.0")]
[assembly: AssemblyFileVersion("1.25.0.0")]

namespace TaskManagerDarkLauncher
{
    /// <summary>
    /// Started by Windows instead of taskmgr.exe (Image File Execution Options "Debugger"):
    ///   TaskManagerDarkLauncher.exe C:\Windows\system32\taskmgr.exe [/args]
    /// Activates a running Task Manager Dark, or starts the elevated scheduled task (no UAC prompt),
    /// or, when the task is missing, starts Task Manager Dark without elevation.
    /// </summary>
    static class Launcher
    {
        const string TaskName = "TaskManagerDark";
        const string WindowProp = "TaskManagerDark.Main";

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW")] static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll", EntryPoint = "PostMessageW")] static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr hwnd, out int pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetPropW")] static extern IntPtr GetProp(IntPtr hwnd, string name);
        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc proc, IntPtr l);

        [STAThread]
        static int Main(string[] args)
        {
            var rest = new StringBuilder();
            int tab = -1;
            for (int i = 0; i < args.Length; i++)
            {
                if (i == 0 && args[i].EndsWith("taskmgr.exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (args[i].Equals("/startup", StringComparison.OrdinalIgnoreCase)) tab = 3;
                if (rest.Length > 0) rest.Append(' ');
                rest.Append(Quote(args[i]));
            }

            // already running: bring it forward (it allows this message through UIPI even when elevated)
            IntPtr existing = FindRunning();
            if (existing != IntPtr.Zero)
            {
                GetWindowThreadProcessId(existing, out int pid);
                AllowSetForegroundWindow(pid);
                PostMessage(existing, RegisterWindowMessage("TaskManagerDark.Activate"), (IntPtr)(tab + 1), IntPtr.Zero);
                return 0;
            }

            if (RunTask(rest.ToString())) return 0;

            // no scheduled task: run unelevated
            string exe = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "TaskManagerDark.exe");
            try
            {
                Process.Start(new ProcessStartInfo(exe, (rest.Length > 0 ? rest + " " : "") + "--from-launcher") { UseShellExecute = false });
                return 0;
            }
            catch (Exception ex) { Log(ex.ToString()); return 1; }
        }

        static void Log(string text)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskManagerDark");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "errors.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [Launcher] " + text + Environment.NewLine + Environment.NewLine);
            }
            catch { /* nowhere left to report to */ }
        }

        static string Quote(string a) => a.IndexOf(' ') >= 0 && !a.StartsWith("\"") ? "\"" + a + "\"" : a;

        static IntPtr FindRunning()
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((h, l) => { if (GetProp(h, WindowProp) != IntPtr.Zero) { found = h; return false; } return true; }, IntPtr.Zero);
            return found;
        }

        /// <summary>Runs the task created by build.ps1 (Run with highest privileges); its action passes $(Arg0) on.</summary>
        static bool RunTask(string args)
        {
            object svc = null;
            try
            {
                var t = Type.GetTypeFromProgID("Schedule.Service");
                if (t == null) return false;
                svc = Activator.CreateInstance(t);
                Invoke(svc, "Connect", null, null, null, null);
                var folder = Invoke(svc, "GetFolder", "\\");
                object task;
                try { task = Invoke(folder, "GetTask", TaskName); }
                catch (TargetInvocationException) { return false; }
                if (task == null) return false;
                if (!(bool)task.GetType().InvokeMember("Enabled", BindingFlags.GetProperty, null, task, null)) return false;
                AllowSetForegroundWindow(-1);   // ASFW_ANY: let the elevated instance take the foreground
                Invoke(task, "Run", args.Length > 0 ? args : " ");
                return true;
            }
            catch (Exception ex) { Log("RunTask: " + ex.Message); return false; }
            finally { if (svc != null) Marshal.ReleaseComObject(svc); }
        }

        static object Invoke(object o, string method, params object[] args) => o.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, o, args);
    }
}
