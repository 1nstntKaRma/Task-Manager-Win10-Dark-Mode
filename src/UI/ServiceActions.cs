using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TaskManagerDark
{
    /// <summary>Start / Stop / Restart of services and the related shortcuts (Services tab and service rows).</summary>
    public static class ServiceActions
    {
        public static void OpenServices() => ShellLaunch.RunUnelevated("services.msc", null);

        public static void Start(MainForm main, string name) => Run(main, name, Strings.UnableStart, svc => StartAndWait(svc));
        public static void Stop(MainForm main, string name) => Run(main, name, Strings.UnableStop, svc => StopAndWait(svc));
        public static void Restart(MainForm main, string name) => Run(main, name, Strings.UnableRestart, svc => { int e = StopAndWait(svc); return e != 0 ? e : StartAndWait(svc); });

        static void Run(MainForm main, string name, string title, Func<IntPtr, int> op)
        {
            // runs off the UI thread (services can take seconds to change state); errors come back to the UI
            ThreadPool.QueueUserWorkItem(_ =>
            {
                int err;
                IntPtr scm = Sys.OpenSCManager(null, null, Sys.SC_MANAGER_CONNECT);
                if (scm == IntPtr.Zero) err = Marshal.GetLastWin32Error();
                else
                {
                    IntPtr svc = Sys.OpenService(scm, name, Sys.SERVICE_START | Sys.SERVICE_STOP | Sys.SERVICE_QUERY_STATUS);
                    if (svc == IntPtr.Zero) err = Marshal.GetLastWin32Error();
                    else { try { err = op(svc); } finally { Sys.CloseServiceHandle(svc); } }
                    Sys.CloseServiceHandle(scm);
                }
                try
                {
                    main.BeginInvoke(new Action(() =>
                    {
                        main.Data.RefreshNow();
                        if (err != 0) Actions.ShowFailure(main, title, err == 5, Sys.Win32Message(err));
                    }));
                }
                catch (InvalidOperationException) { }
            });
        }

        static int StartAndWait(IntPtr svc)
        {
            if (!Sys.StartService(svc, 0, IntPtr.Zero)) { int e = Marshal.GetLastWin32Error(); if (e != 1056 /*already running*/) return e; }
            return WaitFor(svc, 4);
        }

        static int StopAndWait(IntPtr svc)
        {
            if (!Sys.ControlService(svc, Sys.SERVICE_CONTROL_STOP, out _)) { int e = Marshal.GetLastWin32Error(); if (e != 1062 /*not active*/) return e; }
            return WaitFor(svc, 1);
        }

        static int WaitFor(IntPtr svc, int state)
        {
            for (int i = 0; i < 300; i++)
            {
                if (!Sys.QueryServiceStatusEx(svc, 0, out var st, Marshal.SizeOf(typeof(Sys.SERVICE_STATUS_PROCESS)), out _)) return Marshal.GetLastWin32Error();
                if (st.dwCurrentState == state) return 0;
                Thread.Sleep(100);
            }
            return 1053; // ERROR_SERVICE_REQUEST_TIMEOUT
        }
    }

    /// <summary>Restarting as administrator: through the installed scheduled task when present (no prompt), else UAC.</summary>
    public static class Elevation
    {
        public const string TaskName = "TaskManagerDark";

        public static void RestartElevated(MainForm main)
        {
            main.SaveSettings();
            string args = "--replace --tab=" + main.CurrentTab;
            bool ok = TaskScheduler.RunTask(TaskName, args);
            if (!ok)
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo(Program.ExePath, args) { UseShellExecute = true, Verb = "runas" };
                    System.Diagnostics.Process.Start(psi);
                    ok = true;
                }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { return; }   // the user said No
                catch (Exception ex) { ErrorLog.Write("RestartElevated", ex); MessageDialog.Error(main, Strings.OperationFailed, ex.Message); return; }
            }
            if (ok) main.Close();
        }
    }

    /// <summary>Starts the installed scheduled task (Task Scheduler 2.0 COM, late bound) with $(Arg0) parameters.</summary>
    public static class TaskScheduler
    {
        public static bool RunTask(string name, string args)
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
                try { task = Invoke(folder, "GetTask", name); }
                catch (System.Reflection.TargetInvocationException) { return false; }
                if (task == null) return false;
                Sys.AllowSetForegroundWindow(-1);
                Invoke(task, "Run", args ?? "");
                return true;
            }
            catch (Exception ex) { ErrorLog.Write("TaskScheduler.RunTask", ex); return false; }
            finally { if (svc != null) Marshal.ReleaseComObject(svc); }
        }

        public static bool Exists(string name)
        {
            object svc = null;
            try
            {
                var t = Type.GetTypeFromProgID("Schedule.Service");
                if (t == null) return false;
                svc = Activator.CreateInstance(t);
                Invoke(svc, "Connect", null, null, null, null);
                var folder = Invoke(svc, "GetFolder", "\\");
                try { return Invoke(folder, "GetTask", name) != null; }
                catch (System.Reflection.TargetInvocationException) { return false; }
            }
            catch (Exception ex) { ErrorLog.Write("Task Scheduler", ex); return false; }
            finally { if (svc != null) Marshal.ReleaseComObject(svc); }
        }

        static object Invoke(object o, string method, params object[] args) =>
            o.GetType().InvokeMember(method, System.Reflection.BindingFlags.InvokeMethod, null, o, args);
    }
}
