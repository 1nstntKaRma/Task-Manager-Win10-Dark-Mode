using System;
using System.Collections.Generic;
using System.Text;

namespace TaskManagerDark
{
    /// <summary>
    /// Finds the windows that make a process an "App": visible, not cloaked, unowned (or app) top-level windows
    /// with a title, the same set Alt+Tab shows. UWP frames are attributed to the process of their CoreWindow.
    /// </summary>
    public sealed class WindowScanner
    {
        readonly StringBuilder _sb = new StringBuilder(512);
        readonly StringBuilder _cls = new StringBuilder(128);
        Dictionary<int, List<WinInfo>> _result;
        readonly Sys.EnumWindowsProc _proc, _childProc;
        int _coreWindowPid;
        public IntPtr OwnMainWindow;

        public WindowScanner() { _proc = Visit; _childProc = VisitChild; }

        public Dictionary<int, List<WinInfo>> Scan()
        {
            _result = new Dictionary<int, List<WinInfo>>();
            Sys.EnumWindows(_proc, IntPtr.Zero);
            return _result;
        }

        bool Visit(IntPtr hwnd, IntPtr l)
        {
            if (!Native.IsWindowVisible(hwnd)) return true;
            int ex = Sys.GetWindowLong(hwnd, Sys.GWL_EXSTYLE);
            bool appWindow = (ex & Sys.WS_EX_APPWINDOW) != 0;
            if (!appWindow)
            {
                if ((ex & Sys.WS_EX_TOOLWINDOW) != 0) return true;
                if ((ex & Sys.WS_EX_NOACTIVATE) != 0) return true;
                if (Sys.GetWindow(hwnd, Sys.GW_OWNER) != IntPtr.Zero) return true;
            }
            _cls.Clear();
            Sys.GetClassName(hwnd, _cls, _cls.Capacity);
            string cls = _cls.ToString();
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd" || cls == "Windows.UI.Core.CoreWindow") return true;
            int cloaked = 0;
            Sys.DwmGetWindowAttribute(hwnd, Sys.DWMWA_CLOAKED, out cloaked, 4);
            Sys.GetWindowThreadProcessId(hwnd, out int pid);
            if (cls == "ApplicationFrameWindow")
            {
                _coreWindowPid = 0;
                Sys.EnumChildWindows(hwnd, _childProc, IntPtr.Zero);
                if (_coreWindowPid == 0 || _coreWindowPid == pid) return true;   // an empty frame: the app is suspended or closed
                pid = _coreWindowPid;
                if ((cloaked & 1) != 0) return true;
            }
            else if (cloaked != 0 && (cloaked & 2) == 0) return true;            // cloaked by the app itself
            _sb.Clear();
            int n = Sys.InternalGetWindowText(hwnd, _sb, _sb.Capacity);
            if (n <= 0) return true;
            var w = new WinInfo { Hwnd = hwnd, Pid = pid, Title = _sb.ToString(), Hung = Sys.IsHungAppWindow(hwnd), Minimized = Native.IsIconic(hwnd) };
            if (!_result.TryGetValue(pid, out var list)) { list = new List<WinInfo>(2); _result[pid] = list; }
            list.Add(w);
            return true;
        }

        bool VisitChild(IntPtr hwnd, IntPtr l)
        {
            _cls.Clear();
            Sys.GetClassName(hwnd, _cls, _cls.Capacity);
            if (_cls.ToString() == "Windows.UI.Core.CoreWindow")
            {
                Sys.GetWindowThreadProcessId(hwnd, out int pid);
                _coreWindowPid = pid;
                return false;
            }
            return true;
        }
    }
}
