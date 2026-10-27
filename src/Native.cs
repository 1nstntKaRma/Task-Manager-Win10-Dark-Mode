using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TaskManagerDark
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public RECT(int l, int t, int r, int b) { Left = l; Top = t; Right = r; Bottom = b; }
        public int Width => Right - Left;
        public int Height => Bottom - Top;
        public System.Drawing.Rectangle ToRectangle() => System.Drawing.Rectangle.FromLTRB(Left, Top, Right, Bottom);
        public static RECT From(System.Drawing.Rectangle r) => new RECT(r.Left, r.Top, r.Right, r.Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS { public int Left, Right, Top, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct NCCALCSIZE_PARAMS { public RECT rgrc0, rgrc1, rgrc2; public IntPtr lppos; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MINMAXINFO { public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public int dwFlags; }

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }

    [StructLayout(LayoutKind.Sequential)]
    public struct TRACKMOUSEEVENT { public int cbSize; public uint dwFlags; public IntPtr hwndTrack; public uint dwHoverTime; }

    [StructLayout(LayoutKind.Sequential)]
    public struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT { public int length, flags, showCmd; public POINT ptMinPosition, ptMaxPosition; public RECT rcNormalPosition; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct LOGFONT
    {
        public int lfHeight, lfWidth, lfEscapement, lfOrientation, lfWeight;
        public byte lfItalic, lfUnderline, lfStrikeOut, lfCharSet, lfOutPrecision, lfClipPrecision, lfQuality, lfPitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string lfFaceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TEXTMETRIC
    {
        public int tmHeight, tmAscent, tmDescent, tmInternalLeading, tmExternalLeading, tmAveCharWidth, tmMaxCharWidth, tmWeight, tmOverhang, tmDigitizedAspectX, tmDigitizedAspectY;
        public char tmFirstChar, tmLastChar, tmDefaultChar, tmBreakChar;
        public byte tmItalic, tmUnderlined, tmStruckOut, tmPitchAndFamily, tmCharSet;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RTL_OSVERSIONINFOW
    {
        public int dwOSVersionInfoSize, dwMajorVersion, dwMinorVersion, dwBuildNumber, dwPlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szCSDVersion;
    }

    public static class Native
    {
        // ---- messages ----
        public const int WM_CREATE = 0x0001, WM_DESTROY = 0x0002, WM_MOVE = 0x0003, WM_SIZE = 0x0005, WM_ACTIVATE = 0x0006, WM_SETFOCUS = 0x0007, WM_KILLFOCUS = 0x0008;
        public const int WM_PAINT = 0x000F, WM_CLOSE = 0x0010, WM_ERASEBKGND = 0x0014, WM_SETTINGCHANGE = 0x001A, WM_ACTIVATEAPP = 0x001C, WM_SETCURSOR = 0x0020, WM_MOUSEACTIVATE = 0x0021;
        public const int WM_GETMINMAXINFO = 0x0024, WM_WINDOWPOSCHANGING = 0x0046, WM_WINDOWPOSCHANGED = 0x0047, WM_NCCALCSIZE = 0x0083, WM_NCHITTEST = 0x0084, WM_NCPAINT = 0x0085, WM_NCACTIVATE = 0x0086;
        public const int WM_NCMOUSEMOVE = 0x00A0, WM_NCLBUTTONDOWN = 0x00A1, WM_NCLBUTTONUP = 0x00A2, WM_NCLBUTTONDBLCLK = 0x00A3, WM_NCRBUTTONDOWN = 0x00A4, WM_NCRBUTTONUP = 0x00A5;
        public const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_CHAR = 0x0102, WM_DEADCHAR = 0x0103, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105, WM_SYSCHAR = 0x0106, WM_UNICHAR = 0x0109;
        public const int WM_IME_STARTCOMPOSITION = 0x010D, WM_IME_ENDCOMPOSITION = 0x010E, WM_IME_COMPOSITION = 0x010F, WM_IME_SETCONTEXT = 0x0281, WM_IME_NOTIFY = 0x0282, WM_IME_CHAR = 0x0286, WM_IME_REQUEST = 0x0288;
        public const int WM_SYSCOMMAND = 0x0112, WM_TIMER = 0x0113, WM_HSCROLL = 0x0114, WM_VSCROLL = 0x0115, WM_INITMENUPOPUP = 0x0117, WM_MENUSELECT = 0x011F, WM_ENTERIDLE = 0x0121;
        public const int WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203, WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205, WM_RBUTTONDBLCLK = 0x0206;
        public const int WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208, WM_MOUSEWHEEL = 0x020A, WM_XBUTTONDOWN = 0x020B, WM_MOUSEHWHEEL = 0x020E, WM_CAPTURECHANGED = 0x0215;
        public const int WM_ENTERSIZEMOVE = 0x0231, WM_EXITSIZEMOVE = 0x0232, WM_DROPFILES = 0x0233, WM_MOUSELEAVE = 0x02A3, WM_NCMOUSELEAVE = 0x02A2, WM_DPICHANGED = 0x02E0;
        public const int WM_CUT = 0x0300, WM_COPY = 0x0301, WM_PASTE = 0x0302, WM_CLEAR = 0x0303, WM_UNDO = 0x0304, WM_THEMECHANGED = 0x031A, WM_DWMCOMPOSITIONCHANGED = 0x031E;
        public const int WM_USER = 0x0400, WM_APP = 0x8000, WM_SETTEXT = 0x000C, WM_SETICON = 0x0080, WM_GETDLGCODE = 0x0087;

        // ---- hit test ----
        public const int HTNOWHERE = 0, HTCLIENT = 1, HTCAPTION = 2, HTSYSMENU = 3, HTMINBUTTON = 8, HTMAXBUTTON = 9, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;
        public const int HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17, HTCLOSE = 20, HTTRANSPARENT = -1;

        public const int SC_MINIMIZE = 0xF020, SC_MAXIMIZE = 0xF030, SC_RESTORE = 0xF120, SC_CLOSE = 0xF060, SC_KEYMENU = 0xF100, SC_MOUSEMENU = 0xF090;
        public const int SM_CXFRAME = 32, SM_CYFRAME = 33, SM_CXPADDEDBORDER = 92, SM_CXSMICON = 49, SM_CYSMICON = 50, SM_CXDRAG = 68, SM_CYDRAG = 69, SM_CXDOUBLECLK = 36, SM_CYDOUBLECLK = 37;
        public const int SW_HIDE = 0, SW_SHOWNORMAL = 1, SW_SHOWMINIMIZED = 2, SW_SHOWMAXIMIZED = 3, SW_SHOWNOACTIVATE = 4, SW_SHOW = 5, SW_RESTORE = 9;

        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOREDRAW = 0x8, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20, SWP_SHOWWINDOW = 0x40, SWP_HIDEWINDOW = 0x80, SWP_NOOWNERZORDER = 0x200;
        public const uint TME_LEAVE = 0x2, TME_NONCLIENT = 0x10;
        public const int MK_LBUTTON = 1, MK_RBUTTON = 2, MK_SHIFT = 4, MK_CONTROL = 8, MK_MBUTTON = 0x10;
        public const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LBUTTON = 1, VK_RBUTTON = 2, VK_ESCAPE = 0x1B;

        public const int SRCCOPY = 0x00CC0020, HALFTONE = 4, COLORONCOLOR = 3, TRANSPARENT = 1, OPAQUE = 2;
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19, DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34, DWMWA_CAPTION_COLOR = 35, DWMWA_TEXT_COLOR = 36;
        public const int MONITOR_DEFAULTTONEAREST = 2;
        public const uint ABM_GETSTATE = 4, ABM_GETTASKBARPOS = 5, ABM_GETAUTOHIDEBAREX = 11, ABS_AUTOHIDE = 1;
        public const uint ABE_LEFT = 0, ABE_TOP = 1, ABE_RIGHT = 2, ABE_BOTTOM = 3;

        // ---- user32 ----
        [DllImport("user32.dll", EntryPoint = "DefWindowProcW")] public static extern IntPtr DefWindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", EntryPoint = "SendMessageW")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", EntryPoint = "PostMessageW")] public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern int GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern int GetDpiForSystem();
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int nIndex);
        [DllImport("user32.dll")] public static extern int GetSystemMetricsForDpi(int nIndex, int dpi);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr hWnd, ref POINT p);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT p);
        [DllImport("user32.dll")] public static extern IntPtr SetCapture(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr GetCapture();
        [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT tme);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, int flags);
        [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO mi);
        [DllImport("user32.dll")] public static extern IntPtr SetCursor(IntPtr hCursor);
        [DllImport("user32.dll")] public static extern IntPtr LoadCursor(IntPtr hInst, int id);
        [DllImport("user32.dll")] public static extern IntPtr CreateIconIndirect(ref ICONINFO ii);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll")] public static extern bool DestroyCursor(IntPtr h);
        [DllImport("user32.dll")] public static extern int GetCaretBlinkTime();
        [DllImport("user32.dll")] public static extern int GetDoubleClickTime();
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr GetSystemMenu(IntPtr hWnd, bool revert);
        [DllImport("user32.dll")] public static extern int TrackPopupMenuEx(IntPtr hMenu, uint flags, int x, int y, IntPtr hWnd, IntPtr tpm);
        [DllImport("user32.dll")] public static extern bool EnableMenuItem(IntPtr hMenu, uint id, uint flags);
        [DllImport("user32.dll")] public static extern bool SetMenuDefaultItem(IntPtr hMenu, uint id, uint byPos);
        [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT wp);
        [DllImport("user32.dll")] public static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT wp);
        [DllImport("user32.dll")] public static extern bool InvalidateRect(IntPtr hWnd, IntPtr rect, bool erase);
        [DllImport("user32.dll")] public static extern bool InvalidateRect(IntPtr hWnd, ref RECT rect, bool erase);
        [DllImport("user32.dll")] public static extern bool UpdateWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool SystemParametersInfo(int action, int param, IntPtr vparam, int winIni);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SystemParametersInfo(int action, int param, string vparam, int winIni);
        [DllImport("user32.dll")] public static extern bool SystemParametersInfo(int action, int param, ref int vparam, int winIni);
        [DllImport("user32.dll")] public static extern int MapWindowPoints(IntPtr from, IntPtr to, ref RECT r, int n);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, int flags);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
        [DllImport("user32.dll")] public static extern int FillRect(IntPtr hdc, ref RECT r, IntPtr brush);
        [DllImport("user32.dll")] public static extern bool MessageBeep(uint type);
        [DllImport("user32.dll")] public static extern bool FlashWindow(IntPtr hwnd, bool invert);
        [DllImport("user32.dll")] public static extern uint GetMessagePos();
        [DllImport("user32.dll")] public static extern int GetMessageTime();
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] public static extern int GetMouseMovePointsEx(uint cbSize, ref MOUSEMOVEPOINT pointsIn, [Out] MOUSEMOVEPOINT[] buf, int nBufPoints, uint resolution);
        [DllImport("user32.dll")] public static extern bool LockWindowUpdate(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr GetFocus();
        [DllImport("user32.dll")] public static extern int GetSysColor(int index);
        [DllImport("user32.dll")] public static extern bool CreateCaret(IntPtr hwnd, IntPtr hbmp, int w, int h);
        [DllImport("user32.dll")] public static extern bool DestroyCaret();
        [DllImport("user32.dll")] public static extern bool SetCaretPos(int x, int y);
        [DllImport("user32.dll")] public static extern bool ShowCaret(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool HideCaret(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr SetTimer(IntPtr hwnd, IntPtr id, uint ms, IntPtr proc);
        [DllImport("user32.dll")] public static extern bool KillTimer(IntPtr hwnd, IntPtr id);
        [DllImport("user32.dll")] public static extern bool ScrollWindowEx(IntPtr hwnd, int dx, int dy, IntPtr scroll, IntPtr clip, IntPtr rgn, IntPtr update, uint flags);
        [DllImport("user32.dll")] public static extern int MapVirtualKey(uint code, uint type);
        [DllImport("user32.dll")] public static extern bool OpenClipboard(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool CloseClipboard();
        [DllImport("user32.dll")] public static extern bool IsClipboardFormatAvailable(uint fmt);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterClipboardFormat(string name);

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEMOVEPOINT { public int x, y; public uint time; public IntPtr dwExtraInfo; }

        // ---- gdi32 ----
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr hSection, uint offset);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateBitmap(int w, int h, uint planes, uint bpp, IntPtr bits);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr hdc, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
        [DllImport("gdi32.dll")] public static extern bool StretchBlt(IntPtr hdc, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, int rop);
        [DllImport("gdi32.dll")] public static extern int StretchDIBits(IntPtr hdc, int x, int y, int w, int h, int sx, int sy, int sw, int sh, IntPtr bits, ref BITMAPINFOHEADER bmi, uint usage, int rop);
        [DllImport("gdi32.dll")] public static extern int SetDIBitsToDevice(IntPtr hdc, int x, int y, int w, int h, int sx, int sy, int startScan, int lines, IntPtr bits, ref BITMAPINFOHEADER bmi, uint usage);
        [DllImport("gdi32.dll")] public static extern int SetStretchBltMode(IntPtr hdc, int mode);
        [DllImport("gdi32.dll")] public static extern bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr old);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateSolidBrush(int color);
        [DllImport("gdi32.dll")] public static extern IntPtr CreatePen(int style, int width, int color);
        [DllImport("gdi32.dll")] public static extern IntPtr GetStockObject(int obj);
        [DllImport("gdi32.dll")] public static extern bool Rectangle(IntPtr hdc, int l, int t, int r, int b);
        [DllImport("gdi32.dll")] public static extern bool Ellipse(IntPtr hdc, int l, int t, int r, int b);
        [DllImport("gdi32.dll")] public static extern bool MoveToEx(IntPtr hdc, int x, int y, IntPtr old);
        [DllImport("gdi32.dll")] public static extern bool LineTo(IntPtr hdc, int x, int y);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateFontIndirect(ref LOGFONT lf);
        [DllImport("gdi32.dll")] public static extern int SetTextColor(IntPtr hdc, int color);
        [DllImport("gdi32.dll")] public static extern int SetBkColor(IntPtr hdc, int color);
        [DllImport("gdi32.dll")] public static extern int SetBkMode(IntPtr hdc, int mode);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern bool TextOut(IntPtr hdc, int x, int y, string s, int n);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern bool GetTextMetrics(IntPtr hdc, out TEXTMETRIC tm);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern bool GetTextExtentPoint32(IntPtr hdc, string s, int n, out SIZE sz);
        [DllImport("gdi32.dll")] public static extern bool GdiFlush();
        [DllImport("gdi32.dll")] public static extern int GetDeviceCaps(IntPtr hdc, int index);
        [DllImport("gdi32.dll")] public static extern int SetGraphicsMode(IntPtr hdc, int mode);
        [DllImport("gdi32.dll")] public static extern int IntersectClipRect(IntPtr hdc, int l, int t, int r, int b);
        [DllImport("gdi32.dll")] public static extern int SelectClipRgn(IntPtr hdc, IntPtr rgn);
        [DllImport("gdi32.dll")] public static extern int SaveDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool RestoreDC(IntPtr hdc, int saved);
        [DllImport("gdi32.dll")] public static extern bool SetViewportOrgEx(IntPtr hdc, int x, int y, IntPtr old);
        [DllImport("gdi32.dll")] public static extern bool PatBlt(IntPtr hdc, int x, int y, int w, int h, int rop);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern int EnumFontFamiliesEx(IntPtr hdc, ref LOGFONT lf, EnumFontExProc proc, IntPtr lParam, uint flags);
        public delegate int EnumFontExProc(IntPtr lpelfe, IntPtr lpntme, int fontType, IntPtr lParam);

        [DllImport("msimg32.dll")] public static extern bool AlphaBlend(IntPtr hdcDest, int x, int y, int w, int h, IntPtr hdcSrc, int sx, int sy, int sw, int sh, BLENDFUNCTION bf);

        // ---- dwm ----
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS m);
        [DllImport("dwmapi.dll")] public static extern int DwmFlush();

        // ---- shell ----
        [DllImport("shell32.dll")] public static extern IntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int DragQueryFile(IntPtr hDrop, uint i, StringBuilder sb, int cch);
        [DllImport("shell32.dll")] public static extern void DragFinish(IntPtr hDrop);
        [DllImport("shell32.dll")] public static extern void DragAcceptFiles(IntPtr hwnd, bool accept);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern void SHAddToRecentDocs(uint flags, string path);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SetCurrentProcessExplicitAppUserModelID(string id);

        // ---- kernel32 / ntdll ----
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr LoadLibrary(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] public static extern IntPtr GetProcAddress(IntPtr h, string name);
        [DllImport("kernel32.dll")] public static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32.dll")] public static extern IntPtr GlobalLock(IntPtr h);
        [DllImport("kernel32.dll")] public static extern bool GlobalUnlock(IntPtr h);
        [DllImport("kernel32.dll")] public static extern UIntPtr GlobalSize(IntPtr h);
        [DllImport("kernel32.dll")] public static extern IntPtr GlobalFree(IntPtr h);
        [DllImport("ntdll.dll")] public static extern int RtlGetVersion(ref RTL_OSVERSIONINFOW v);
        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX { public int dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }

        // ---- imm32 ----
        [DllImport("imm32.dll")] public static extern IntPtr ImmGetContext(IntPtr hwnd);
        [DllImport("imm32.dll")] public static extern bool ImmReleaseContext(IntPtr hwnd, IntPtr himc);
        [DllImport("imm32.dll")] public static extern bool ImmSetCompositionWindow(IntPtr himc, ref COMPOSITIONFORM cf);
        [StructLayout(LayoutKind.Sequential)]
        public struct COMPOSITIONFORM { public int dwStyle; public POINT ptCurrentPos; public RECT rcArea; }

        // ---- uxtheme (dark mode) ----
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        // ---- msvcrt ----
        [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)] public static extern unsafe int memcmp(void* a, void* b, UIntPtr n);

        // ---- helpers ----
        public static int LoWord(IntPtr p) => unchecked((short)(long)p);
        public static int HiWord(IntPtr p) => unchecked((short)((long)p >> 16));
        public static IntPtr MakeLParam(int lo, int hi) => (IntPtr)unchecked((int)(((ushort)hi << 16) | (ushort)lo));
        public static int Rgb(System.Drawing.Color c) => c.R | (c.G << 8) | (c.B << 16);
        public static int Rgb(uint argb) => (int)(((argb >> 16) & 0xFF) | (argb & 0xFF00) | ((argb & 0xFF) << 16));

        static int _build = -1;
        public static int OSBuild
        {
            get
            {
                if (_build < 0)
                {
                    var v = new RTL_OSVERSIONINFOW { dwOSVersionInfoSize = Marshal.SizeOf(typeof(RTL_OSVERSIONINFOW)) };
                    try { RtlGetVersion(ref v); _build = v.dwBuildNumber; } catch (EntryPointNotFoundException) { _build = Environment.OSVersion.Version.Build; }
                }
                return _build;
            }
        }
        public static bool IsWin11 => OSBuild >= 22000;

        public static bool KeyDown(int vk) => (GetKeyState(vk) & 0x8000) != 0;
        public static bool CtrlDown => KeyDown(VK_CONTROL) || TestHook.Ctrl;
        public static bool ShiftDown => KeyDown(VK_SHIFT) || TestHook.Shift;
        public static bool AltDown => KeyDown(VK_MENU) || TestHook.Alt;

        public static int DpiOf(IntPtr hwnd)
        {
            try { int d = GetDpiForWindow(hwnd); if (d > 0) return d; } catch (EntryPointNotFoundException) { /* before Windows 10 1607 */ }
            return 96;
        }

        public static int MetricForDpi(int index, int dpi)
        {
            try { return GetSystemMetricsForDpi(index, dpi); } catch (EntryPointNotFoundException) { return GetSystemMetrics(index) * dpi / 96; }
        }
    }

    /// <summary>Undocumented-but-stable Windows 10 1809+/11 dark mode switches for native parts (menus, scrollbars, file dialogs).</summary>
    public static class DarkMode
    {
        delegate int SetPreferredAppModeFn(int mode);
        delegate bool AllowDarkModeForWindowFn(IntPtr hwnd, bool allow);
        delegate void VoidFn();
        static AllowDarkModeForWindowFn _allowForWindow;

        public static void InitProcess()
        {
            try
            {
                if (Native.OSBuild < 17763) return;
                IntPtr ux = Native.LoadLibrary("uxtheme.dll");
                if (ux == IntPtr.Zero) return;
                IntPtr p135 = Native.GetProcAddress(ux, "#135");
                IntPtr p133 = Native.GetProcAddress(ux, "#133");
                IntPtr p104 = Native.GetProcAddress(ux, "#104");
                IntPtr p136 = Native.GetProcAddress(ux, "#136");
                if (p135 != IntPtr.Zero)
                {
                    var f = (SetPreferredAppModeFn)Marshal.GetDelegateForFunctionPointer(p135, typeof(SetPreferredAppModeFn));
                    // 1903+: SetPreferredAppMode(ForceDark=2). 1809: AllowDarkModeForApp(TRUE) has the same slot.
                    f(Native.OSBuild >= 18362 ? 2 : 1);
                }
                if (p133 != IntPtr.Zero) _allowForWindow = (AllowDarkModeForWindowFn)Marshal.GetDelegateForFunctionPointer(p133, typeof(AllowDarkModeForWindowFn));
                if (p104 != IntPtr.Zero) ((VoidFn)Marshal.GetDelegateForFunctionPointer(p104, typeof(VoidFn)))();
                if (p136 != IntPtr.Zero) ((VoidFn)Marshal.GetDelegateForFunctionPointer(p136, typeof(VoidFn)))();
            }
            catch (Exception ex) { ErrorLog.Write("DarkMode.InitProcess", ex); }
        }

        /// <summary>Dark title bar + dark native parts for a top-level window.</summary>
        public static void ApplyToWindow(IntPtr hwnd)
        {
            try { _allowForWindow?.Invoke(hwnd, true); } catch (Exception ex) { ErrorLog.Write("AllowDarkModeForWindow", ex); }
            int on = 1;
            try
            {
                if (Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, 4) != 0)
                    Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, 4);
            }
            catch (Exception ex) { ErrorLog.Write("Dark title bar", ex); }
        }

        /// <summary>Dark scrollbars etc. on a native child control.</summary>
        public static void ApplyToControl(IntPtr hwnd, string theme = "DarkMode_Explorer")
        {
            try { _allowForWindow?.Invoke(hwnd, true); Native.SetWindowTheme(hwnd, theme, null); } catch (Exception ex) { ErrorLog.Write("Dark control theme", ex); }
        }
    }
}
