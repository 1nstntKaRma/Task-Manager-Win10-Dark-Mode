using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TaskManagerDark
{
    /// <summary>Process commands shared by the Processes, Details, Users and Fewer details views.</summary>
    public static class Actions
    {
        // ------------------------------------------------------------ windows
        public static void SwitchTo(MainForm main, WinInfo w)
        {
            if (w == null || !Sys.IsWindow(w.Hwnd)) return;
            if (Native.IsIconic(w.Hwnd)) Native.ShowWindow(w.Hwnd, Native.SW_RESTORE);
            Sys.SwitchToThisWindow(w.Hwnd, true);
            Native.SetForegroundWindow(w.Hwnd);
            main.UsedToSwitch();
        }

        public static void BringToFront(WinInfo w)
        {
            if (w == null || !Sys.IsWindow(w.Hwnd)) return;
            if (Native.IsIconic(w.Hwnd)) Native.ShowWindow(w.Hwnd, Native.SW_RESTORE);
            Native.SetForegroundWindow(w.Hwnd);
        }

        public static void EndTaskWindow(MainForm main, WinInfo w, ProcInfo p)
        {
            if (w == null) return;
            if (w.Hung || !Sys.IsWindow(w.Hwnd)) { if (p != null) Kill(main, new List<ProcInfo> { p }, false, Strings.UnableTerminate); return; }
            Native.PostMessage(w.Hwnd, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        // ------------------------------------------------------------ ending processes
        /// <summary>Processes tab "End task": ends every process of the row, asking only for system processes.</summary>
        public static void EndTask(MainForm main, List<ProcInfo> procs, string displayName)
        {
            if (procs == null || procs.Count == 0) return;
            foreach (var p in procs)
                if (p.Static != null && p.Static.Critical || p.Pid == 4)
                {
                    if (!ConfirmSystem(main, p)) return;
                    break;
                }
            Kill(main, procs, false, Strings.UnableTerminate);
        }

        /// <summary>Details tab "End task" (with Taskmgr's confirmation).</summary>
        public static void EndProcess(MainForm main, List<ProcInfo> procs)
        {
            if (procs.Count == 0) return;
            foreach (var p in procs) if ((p.Static != null && p.Static.Critical) || p.Pid == 4) { if (!ConfirmSystem(main, p)) return; Kill(main, procs, false, Strings.UnableTerminate); return; }
            string q = procs.Count == 1 ? string.Format(Strings.EndQuestion, procs[0].Name) : Strings.EndSelectedQuestion;
            string w = procs.Count == 1 ? Strings.EndWarning : Strings.EndSelectedWarning;
            if (MessageDialog.Show(main, Strings.AppTitle, q, w, new[] { Strings.EndProcess, "Cancel" }, 1, MessageDialog.Kind.None, null, out _) != 0) return;
            Kill(main, procs, false, Strings.UnableTerminate);
        }

        public static void EndProcessTree(MainForm main, List<ProcInfo> procs)
        {
            if (procs.Count == 0) return;
            string q = procs.Count == 1 ? string.Format(Strings.EndTreeQuestion, procs[0].Name) : Strings.EndSelectedTreesQuestion;
            string w = procs.Count == 1 ? Strings.EndTreeWarning : Strings.EndSelectedTreesWarning;
            if (MessageDialog.Show(main, Strings.AppTitle, q, w, new[] { Strings.EndProcessTree, "Cancel" }, 1, MessageDialog.Kind.None, null, out _) != 0) return;
            var all = new List<ProcInfo>();
            foreach (var p in procs) CollectTree(main.Data, p, all);
            Kill(main, all, true, Strings.UnableTerminateTree);
        }

        static void CollectTree(DataEngine d, ProcInfo root, List<ProcInfo> into)
        {
            // children first, so they cannot respawn from a dying parent
            foreach (var p in d.Procs)
                if (p.Cur.ParentPid == root.Pid && p.CreateTime >= root.CreateTime && p.Pid != root.Pid && !into.Contains(p)) CollectTree(d, p, into);
            if (!into.Contains(root)) into.Add(root);
        }

        static bool ConfirmSystem(MainForm main, ProcInfo p)
        {
            // Taskmgr 32443-32446: the "Shut down" button only works once "Abandon unsaved data and shut down." is checked
            while (true)
            {
                int r = MessageDialog.Show(main, Strings.AppTitle, string.Format(Strings.EndSystemQuestion, p.Name), Strings.EndSystemWarning,
                    new[] { Strings.ShutDown, "Cancel" }, 1, MessageDialog.Kind.Warning, Strings.AbandonAndShutDown, out bool abandon);
                if (r != 0) return false;
                if (abandon) return true;
            }
        }

        static void Kill(MainForm main, List<ProcInfo> procs, bool tree, string failTitle)
        {
            string error = null;
            bool denied = false;
            foreach (var p in procs)
            {
                if (p.Pid == 0) continue;
                IntPtr h = Sys.OpenProcess(Sys.PROCESS_TERMINATE, false, p.Pid);
                if (h == IntPtr.Zero) { int e = Marshal.GetLastWin32Error(); if (e == 5) denied = true; error = error ?? Sys.Win32Message(e); continue; }
                try { if (!Sys.TerminateProcess(h, 1)) { int e = Marshal.GetLastWin32Error(); if (e == 5) denied = true; error = error ?? Sys.Win32Message(e); } }
                finally { Sys.CloseHandle(h); }
            }
            main.Data.RefreshNow();
            if (error != null) ShowFailure(main, failTitle, denied, error);
        }

        public static void ShowFailure(MainForm main, string title, bool denied, string error)
        {
            string text = denied ? (Sys.IsElevated ? Strings.AccessDenied : Strings.NeedAdmin) : error;
            MessageDialog.Show(main, Strings.AppTitle, title, text, new[] { "OK" }, 0, MessageDialog.Kind.Error, null, out _);
        }

        public static void RestartExplorer(MainForm main)
        {
            var mine = new List<ProcInfo>();
            Sys.ProcessIdToSessionId(Sys.GetCurrentProcessId(), out int session);
            foreach (var p in main.Data.Procs) if (p.Name.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) && p.Cur.SessionId == session) mine.Add(p);
            foreach (var p in mine)
            {
                IntPtr h = Sys.OpenProcess(Sys.PROCESS_TERMINATE | Sys.SYNCHRONIZE, false, p.Pid);
                if (h == IntPtr.Zero) continue;
                Sys.TerminateProcess(h, 1);
                Sys.WaitForSingleObject(h, 3000);
                Sys.CloseHandle(h);
            }
            // Windows restarts the shell by itself (AutoRestartShell); start it if it did not
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(3000);
                if (Sys.GetShellWindow() == IntPtr.Zero) ShellLaunch.RunUnelevated(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), null);
            });
            main.Data.RefreshNow();
        }

        // ------------------------------------------------------------ shell
        public static void OpenFileLocation(MainForm main, string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                IntPtr pidl = Sys.ILCreateFromPath(path);
                if (pidl == IntPtr.Zero) { ShellLaunch.RunUnelevated("explorer.exe", "/select,\"" + path + "\""); return; }
                try { Sys.SHOpenFolderAndSelectItems(pidl, 0, null, 0); }
                finally { Sys.ILFree(pidl); }
                main.UsedToSwitch();
            }
            catch (Exception ex) { ErrorLog.Write("OpenFileLocation", ex); }
        }

        public static void SearchOnline(ProcInfo p)
        {
            string q = p.Name;
            var d = p.Static?.Description;
            if (!string.IsNullOrEmpty(d) && !d.Equals(p.Name, StringComparison.OrdinalIgnoreCase)) q += " " + d;
            SearchOnline(q);
        }

        public static void SearchOnline(string query)
        {
            ShellLaunch.RunUnelevated("https://www.bing.com/search?q=" + Uri.EscapeDataString(query), null);
        }

        public static void Properties(MainForm main, string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            PropertiesDialog.ShowAsync(main, path);
        }

        // ------------------------------------------------------------ priority / affinity / virtualization
        public static void SetPriority(MainForm main, ProcInfo p, int cls)
        {
            if (MessageDialog.Show(main, Strings.AppTitle, string.Format(Strings.PriorityQuestion, p.Name), Strings.PriorityWarning,
                new[] { Strings.ChangePriority, "Cancel" }, 1, MessageDialog.Kind.None, null, out _) != 0) return;
            IntPtr h = Sys.OpenProcess(Sys.PROCESS_SET_INFORMATION | Sys.PROCESS_QUERY_LIMITED_INFORMATION, false, p.Pid);
            if (h == IntPtr.Zero) { int e = Marshal.GetLastWin32Error(); ShowFailure(main, Strings.UnablePriority, e == 5, Sys.Win32Message(e)); return; }
            try
            {
                if (!Sys.SetPriorityClass(h, cls)) { int e = Marshal.GetLastWin32Error(); ShowFailure(main, Strings.UnablePriority, e == 5, Sys.Win32Message(e)); return; }
                if (cls == 0x100 && Sys.GetPriorityClass(h) != 0x100)
                    MessageDialog.Show(main, Strings.AppTitle, Strings.UnablePriority, Strings.PriorityRealtimeFailed, new[] { "OK" }, 0, MessageDialog.Kind.Warning, null, out _);
            }
            finally { Sys.CloseHandle(h); }
            main.Data.RefreshNow();
        }

        public static int GetPriorityClass(ProcInfo p)
        {
            IntPtr h = Sys.OpenProcess(Sys.PROCESS_QUERY_LIMITED_INFORMATION, false, p.Pid);
            if (h == IntPtr.Zero) return 0;
            try { return Sys.GetPriorityClass(h); } finally { Sys.CloseHandle(h); }
        }

        public static void ToggleVirtualization(MainForm main, ProcInfo p)
        {
            if (p.Static == null || p.Static.Virtualization <= 0) return;
            if (MessageDialog.Show(main, Strings.AppTitle, string.Format(Strings.VirtQuestion, p.Name), Strings.VirtWarning,
                new[] { Strings.ChangeVirt, "Cancel" }, 1, MessageDialog.Kind.None, null, out _) != 0) return;
            IntPtr h = Sys.OpenProcess(Sys.PROCESS_QUERY_INFORMATION, false, p.Pid);
            if (h == IntPtr.Zero) h = Sys.OpenProcess(Sys.PROCESS_QUERY_LIMITED_INFORMATION, false, p.Pid);
            if (h == IntPtr.Zero) { int e = Marshal.GetLastWin32Error(); ShowFailure(main, Strings.UnableVirt, e == 5, Sys.Win32Message(e)); return; }
            try
            {
                if (!Sys.OpenProcessToken(h, Sys.TOKEN_ADJUST_DEFAULT | Sys.TOKEN_QUERY, out var tok)) { int e = Marshal.GetLastWin32Error(); ShowFailure(main, Strings.UnableVirt, e == 5, Sys.Win32Message(e)); return; }
                try
                {
                    int v = p.Static.Virtualization == 2 ? 0 : 1;
                    if (!Sys.SetTokenInformation(tok, Sys.TokenVirtualizationEnabled, ref v, 4)) { int e = Marshal.GetLastWin32Error(); ShowFailure(main, Strings.UnableVirt, e == 5, Sys.Win32Message(e)); return; }
                    p.Static.Virtualization = v == 1 ? 2 : 1;
                }
                finally { Sys.CloseHandle(tok); }
            }
            finally { Sys.CloseHandle(h); }
        }

        // ------------------------------------------------------------ debugger
        static string AeDebugger
        {
            get
            {
                try
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AeDebug"))
                    {
                        var s = k?.GetValue("Debugger") as string;
                        return string.IsNullOrWhiteSpace(s) ? null : s;
                    }
                }
                catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is System.IO.IOException) { return null; }
            }
        }
        public static bool HasDebugger => AeDebugger != null;

        public static void Debug(MainForm main, ProcInfo p)
        {
            var cmd = AeDebugger;
            if (cmd == null) return;
            int i = cmd.IndexOf("%ld", StringComparison.OrdinalIgnoreCase);
            if (i >= 0) cmd = cmd.Substring(0, i) + p.Pid + cmd.Substring(i + 3);
            i = cmd.IndexOf("%ld", StringComparison.OrdinalIgnoreCase);
            if (i >= 0) cmd = cmd.Substring(0, i) + "0" + cmd.Substring(i + 3);
            try
            {
                string exe, args;
                SplitCommand(cmd, out exe, out args);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, args) { UseShellExecute = false });
            }
            catch (Exception ex) { MessageDialog.Show(main, Strings.AppTitle, Strings.UnableAttachDebugger, ex.Message, new[] { "OK" }, 0, MessageDialog.Kind.Error, null, out _); }
        }

        public static void SplitCommand(string cmd, out string exe, out string args)
        {
            cmd = cmd.Trim();
            if (cmd.StartsWith("\"")) { int q = cmd.IndexOf('"', 1); exe = q > 0 ? cmd.Substring(1, q - 1) : cmd.Trim('"'); args = q > 0 ? cmd.Substring(q + 1).Trim() : ""; }
            else { int sp = cmd.IndexOf(' '); exe = sp > 0 ? cmd.Substring(0, sp) : cmd; args = sp > 0 ? cmd.Substring(sp + 1) : ""; }
        }

        // ------------------------------------------------------------ dump
        public static void CreateDump(MainForm main, ProcInfo p) => DumpDialog.Run(main, p);
    }

    /// <summary>
    /// Starting programs from an elevated Task Manager without elevating them: the command is handed to the
    /// desktop's Explorer (IShellDispatch2::ShellExecute on the desktop view), exactly like Taskmgr does for links.
    /// </summary>
    public static class ShellLaunch
    {
        [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        interface IShellWindows
        {
            int Count { get; }
            [return: MarshalAs(UnmanagedType.IDispatch)] object Item(object index);
            [return: MarshalAs(UnmanagedType.IUnknown)] object _NewEnum();
            int Register([MarshalAs(UnmanagedType.IDispatch)] object pid, int hwnd, int swClass);
            int RegisterPending(int threadId, ref object loc, ref object locRoot, int swClass);
            void Revoke(int cookie);
            void OnNavigate(int cookie, ref object loc);
            void OnActivated(int cookie, bool active);
            [return: MarshalAs(UnmanagedType.IDispatch)] object FindWindowSW(ref object loc, ref object locRoot, int swClass, out int hwnd, int options);
        }

        [ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IServiceProvider { [PreserveSig] int QueryService(ref Guid service, ref Guid riid, out IntPtr obj); }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int QueryActiveShellViewFn(IntPtr self, out IntPtr view);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetItemObjectFn(IntPtr self, int item, ref Guid riid, out IntPtr obj);

        public static void RunUnelevated(string file, string args)
        {
            if (!Sys.IsElevated || !TryDesktopShellExecute(file, args))
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file, args ?? "") { UseShellExecute = true }); }
                catch (Exception ex) { ErrorLog.Write("RunUnelevated " + file, ex); }
            }
        }

        static unsafe bool TryDesktopShellExecute(string file, string args)
        {
            object windows = null;
            try
            {
                windows = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39")));
                var sw = (IShellWindows)windows;
                object loc = 0 /*CSIDL_DESKTOP*/, root = null;
                object disp = sw.FindWindowSW(ref loc, ref root, 8 /*SWC_DESKTOP*/, out _, 1 /*SWFO_NEEDDISPATCH*/);
                if (disp == null) return false;
                var sp = (IServiceProvider)disp;
                var sidTop = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
                var iidBrowser = new Guid("000214E2-0000-0000-C000-000000000046");
                if (sp.QueryService(ref sidTop, ref iidBrowser, out var browser) != 0 || browser == IntPtr.Zero) return false;
                try
                {
                    var qasv = (QueryActiveShellViewFn)Marshal.GetDelegateForFunctionPointer((*(IntPtr**)browser)[15], typeof(QueryActiveShellViewFn));
                    if (qasv(browser, out var view) != 0 || view == IntPtr.Zero) return false;
                    try
                    {
                        var gio = (GetItemObjectFn)Marshal.GetDelegateForFunctionPointer((*(IntPtr**)view)[15], typeof(GetItemObjectFn));
                        var iidDispatch = new Guid("00020400-0000-0000-C000-000000000046");
                        if (gio(view, 0 /*SVGIO_BACKGROUND*/, ref iidDispatch, out var folderView) != 0 || folderView == IntPtr.Zero) return false;
                        try
                        {
                            var fv = Marshal.GetObjectForIUnknown(folderView);
                            var app = fv.GetType().InvokeMember("Application", System.Reflection.BindingFlags.GetProperty, null, fv, null);
                            app.GetType().InvokeMember("ShellExecute", System.Reflection.BindingFlags.InvokeMethod, null, app,
                                new object[] { file, args ?? "", "", "", 1 });
                            Marshal.ReleaseComObject(app);
                            Marshal.ReleaseComObject(fv);
                            return true;
                        }
                        finally { Marshal.Release(folderView); }
                    }
                    finally { Marshal.Release(view); }
                }
                finally { Marshal.Release(browser); }
            }
            catch (Exception ex) { ErrorLog.Write("DesktopShellExecute", ex); return false; }
            finally { if (windows != null) Marshal.ReleaseComObject(windows); }
        }
    }
}
