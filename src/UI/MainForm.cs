using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TaskManagerDark
{
    public sealed partial class MainForm : Form, IMessageFilter
    {
        public readonly DataEngine Data;
        public readonly MenuBar Menu;
        public readonly TabStrip Tabs;
        public readonly CommandBar Bar;
        public readonly Page[] Pages = new Page[7];
        public FewerDetailsView Fewer;
        public TrayIcon Tray;
        Dpi D = new Dpi(96);
        int _page = -1;
        bool _fewerDetails, _chromeHidden, _altPending, _loaded, _closing;
        Rectangle _moreBounds, _fewerBounds;
        bool _moreMaximized;

        // ------------------------------------------------------------ options (Options / View menus)
        public bool AlwaysOnTop, MinimizeOnUse, HideWhenMinimized, ShowFullAccountName, ShowHistoryAllProcesses = false;
        public int DefaultTab;            // 0..6
        public int UpdateSpeed = 1;       // 0 High, 1 Normal, 2 Low, 3 Paused
        public bool ShowSuspendedStatus = true;
        public static readonly int[] SpeedMs = { 500, 1000, 4000, 0 };
        public static readonly string[] TabNames = { Strings.TabProcesses, Strings.TabPerformance, Strings.TabAppHistory, Strings.TabStartup, Strings.TabUsers, Strings.TabDetails, Strings.TabServices };

        public MainForm()
        {
            StartupLog.Mark("window constructor");
            Text = Strings.AppTitle;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
            try { Icon = MakeAppIcon(32); } catch (Exception ex) { ErrorLog.Write("App icon", ex); }
            if (TestHook.Enabled) ShowInTaskbar = false;   // automated tests: no taskbar button on the user's desktop

            Data = Program.Engine ?? new DataEngine();
            Data.Attach(this);
            Menu = new MenuBar();
            Menu.Entries.Add(new MenuBar.Entry { Text = "&File", Build = BuildFileMenu });
            Menu.Entries.Add(new MenuBar.Entry { Text = "&Options", Build = BuildOptionsMenu });
            Menu.Entries.Add(new MenuBar.Entry { Text = "&View", Build = BuildViewMenu });
            Tabs = new TabStrip();
            Tabs.Tabs.AddRange(TabNames);
            Tabs.SelectedChanged += i => ShowPage(i);
            Bar = new CommandBar();
            Bar.ToggleClicked += () => SetFewerDetails(true);
            Bar.ButtonClicked += () => CurrentPage?.CommandButton();
            Bar.LinkClicked += () => CurrentPage?.CommandLink();
            Controls.Add(Menu); Controls.Add(Tabs); Controls.Add(Bar);

            LoadSettings();
            StartupLog.Mark("window chrome built");
            Popups.MainWindow = this;
            Popups.Install();
            Application.AddMessageFilter(this);
            Data.Updated += OnDataUpdated;
            Data.StaticUpdated += OnStaticUpdated;
        }

        public Page CurrentPage => _page >= 0 ? Pages[_page] : null;
        public int CurrentTab => _page;
        public bool FewerDetailsMode => _fewerDetails;
        public int DpiValue => D.Value;

        Page CreatePage(int i)
        {
            switch (i)
            {
                case 0: return new ProcessesPage(this);
                case 1: return new PerformancePage(this);
                case 2: return new AppHistoryPage(this);
                case 3: return new StartupPage(this);
                case 4: return new UsersPage(this);
                case 5: return new DetailsPage(this);
                default: return new ServicesPage(this);
            }
        }

        public Page GetPage(int i)
        {
            if (Pages[i] == null)
            {
                var p = CreatePage(i);
                p.Visible = false;
                Pages[i] = p;
                p.LoadSettings();
                Controls.Add(p);
                p.SetDpi(D.Value);
            }
            return Pages[i];
        }

        public void ShowPage(int i)
        {
            if (i < 0 || i > 6) return;
            var old = CurrentPage;
            var p = GetPage(i);
            if (old == p && p.Visible) return;
            _page = i;
            if (Tabs.Selected != i) Tabs.Selected = i;
            DoLayout();
            p.Visible = !_fewerDetails;
            if (old != null && old != p) { old.Visible = false; old.SetActive(false); }
            p.SetActive(true);
            p.DataUpdated();
            UpdateCommandBar();
            if (!_fewerDetails) p.FocusContent();
            UpdateWantFlags();
        }

        public void UpdateCommandBar()
        {
            Bar.ToggleText = Strings.FewerDetails; Bar.ToggleUp = true;
            CurrentPage?.UpdateCommandBar(Bar);
            Bar.Invalidate();
        }

        void UpdateWantFlags()
        {
            var details = Pages[5] as DetailsPage;
            Data.WantGuiResources = details != null && details.Visible && details.WantsGui;
            Data.WantIoPriority = details != null && details.Visible && details.WantsIoPriority;
            Data.WantServicesLive = !_fewerDetails && _page == 6;
        }

        // ================================================================== layout
        protected override void OnLayout(LayoutEventArgs levent) { DoLayout(); }

        void DoLayout()
        {
            if (Menu == null) return;
            int w = ClientSize.Width, h = ClientSize.Height;
            if (_fewerDetails)
            {
                Menu.Visible = Tabs.Visible = Bar.Visible = false;
                foreach (var p in Pages) if (p != null) p.Visible = false;
                if (Fewer != null) { Fewer.Visible = true; Fewer.Bounds = new Rectangle(0, 0, w, h); }
                return;
            }
            if (Fewer != null) Fewer.Visible = false;
            bool chrome = !_chromeHidden;
            Menu.Visible = Tabs.Visible = Bar.Visible = chrome;
            int y = 0;
            if (chrome)
            {
                Menu.SetBounds(0, 0, w, Menu.PreferredHeight); y += Menu.PreferredHeight;
                Tabs.SetBounds(0, y, w, Tabs.PreferredHeight); y += Tabs.PreferredHeight;
            }
            int bh = chrome ? Bar.PreferredHeight : 0;
            if (chrome) Bar.SetBounds(0, h - bh, w, bh);
            var page = CurrentPage;
            if (page != null) { page.Bounds = new Rectangle(0, y, w, Math.Max(0, h - y - bh)); page.Visible = true; }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { e.Graphics.FillRectangle(Theme.Brush(Theme.Window), e.ClipRectangle); }

        /// <summary>Graph summary view: hide menu, tabs and the command bar.</summary>
        public void SetChromeHidden(bool hidden)
        {
            if (_chromeHidden == hidden) return;
            _chromeHidden = hidden;
            DoLayout(); Invalidate(true);
        }
        public bool ChromeHidden => _chromeHidden;

        // ================================================================== fewer / more details
        public void SetFewerDetails(bool fewer)
        {
            if (_fewerDetails == fewer && _loaded) return;
            Popups.CloseAll();
            var cur = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            if (_loaded) { if (_fewerDetails) _fewerBounds = cur; else { _moreBounds = cur; _moreMaximized = WindowState == FormWindowState.Maximized; } }
            _fewerDetails = fewer;
            if (fewer && Fewer == null) { Fewer = new FewerDetailsView(this); Controls.Add(Fewer); Fewer.SetDpi(D.Value); }
            if (_loaded)
            {
                SuspendLayout();
                if (WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal;
                var target = fewer ? _fewerBounds : _moreBounds;
                if (target.Width <= 0) target = DefaultBounds(fewer, cur);
                // keep the top-left corner where the user had the window
                Bounds = new Rectangle(cur.X, cur.Y, target.Width, target.Height);
                EnsureOnScreen();
                if (!fewer && _moreMaximized) WindowState = FormWindowState.Maximized;
                ResumeLayout();
            }
            MinimumSize = fewer ? new Size(D.S(300), D.S(240)) : new Size(D.S(400), D.S(320));
            DoLayout();
            if (fewer) { Fewer.Refresh(); Fewer.FocusList(); }
            else { var p = CurrentPage; if (p != null) { p.Visible = true; p.DataUpdated(); p.FocusContent(); } UpdateCommandBar(); }
            UpdateWantFlags();
        }

        Rectangle DefaultBounds(bool fewer, Rectangle around)
        {
            var wa = Screen.FromRectangle(around.Width > 0 ? around : new Rectangle(Cursor.Position, new Size(1, 1))).WorkingArea;
            int w = fewer ? D.S(370) : D.S(740), h = fewer ? D.S(380) : D.S(640);
            w = Math.Min(w, wa.Width); h = Math.Min(h, wa.Height);
            return new Rectangle(wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2, w, h);
        }

        void EnsureOnScreen()
        {
            var b = Bounds;
            var wa = Screen.FromRectangle(b).WorkingArea;
            if (!wa.IntersectsWith(b) || b.Top < wa.Top - D.S(4))
            {
                int x = Math.Max(wa.Left, Math.Min(b.X, wa.Right - b.Width)), y = Math.Max(wa.Top, Math.Min(b.Y, wa.Bottom - b.Height));
                Bounds = new Rectangle(x, y, Math.Min(b.Width, wa.Width), Math.Min(b.Height, wa.Height));
            }
        }

        // ================================================================== handle / DPI
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            StartupLog.Mark("window handle");
            DarkMode.ApplyToWindow(Handle);
            SetProp(Handle, Program.WindowProp, (IntPtr)1);
            Sys.ChangeWindowMessageFilterEx(Handle, Program.ActivateMessage, 1 /*MSGFLT_ALLOW*/, IntPtr.Zero);
            if (TestHook.Enabled) Sys.ChangeWindowMessageFilterEx(Handle, 0x004A /*WM_COPYDATA*/, 1, IntPtr.Zero);
            ApplyDpi(Native.DpiOf(Handle));
            Data.OwnWindow = Handle;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SetPropW")] static extern bool SetProp(IntPtr hwnd, string name, IntPtr data);

        void ApplyDpi(int dpi)
        {
            D = new Dpi(dpi);
            Menu.SetDpi(dpi); Tabs.SetDpi(dpi); Bar.SetDpi(dpi);
            foreach (var p in Pages) p?.SetDpi(dpi);
            Fewer?.SetDpi(dpi);
            MinimumSize = _fewerDetails ? new Size(D.S(300), D.S(240)) : new Size(D.S(400), D.S(320));
            Tray?.SetDpi(dpi);
            DoLayout();
        }

        public void TestDpi(int dpi) => ApplyDpi(dpi);

        // ================================================================== startup / shutdown
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            StartupLog.Mark("window load");
            Data.IconSize = D.S(16);
            ApplyPlacement();
            if (Program.Offscreen)
            {
                // automated tests render offscreen (WM_PRINTCLIENT), so the window is parked outside every monitor
                var vs = SystemInformation.VirtualScreen;
                WindowState = FormWindowState.Normal;
                Location = new Point(vs.Left - Width - 400, vs.Top + 50);
            }
            TopMost = AlwaysOnTop && !TestHook.Enabled;
            int tab = Program.StartTab >= 0 ? Program.StartTab : DefaultTab;
            _loaded = false;
            SetFewerDetails(_fewerDetails);
            ShowPage(Math.Max(0, Math.Min(6, tab)));
            StartupLog.Mark("page " + tab + " created");
            _loaded = true;
            Data.Interval = SpeedMs[UpdateSpeed];
            Data.Start();
            // open with real figures: the first sample is usually ready by now (sampling started with the window),
            // and so are the names and icons of the running processes
            Data.WaitFirstSample(700, 250);
            (CurrentPage as StartupPage)?.WaitFirstLoad(250);
            if (!TestHook.Enabled || Program.TestTray) { Tray = new TrayIcon(this); Tray.SetDpi(D.Value); }
        }

        void ApplyPlacement()
        {
            var b = _fewerDetails ? _fewerBounds : _moreBounds;
            if (b.Width <= 0) b = DefaultBounds(_fewerDetails, Rectangle.Empty);
            Bounds = b;
            EnsureOnScreen();
            if (!_fewerDetails && _moreMaximized) WindowState = FormWindowState.Maximized;
        }

        /// <summary>Automated tests never take the focus from whatever the user is doing.</summary>
        protected override bool ShowWithoutActivation => TestHook.Enabled;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            StartupLog.Mark("window shown");
            BeginInvoke(new Action(() => { StartupLog.Mark("first paint done"); StartupLog.Flush(false); }));
            var t = new System.Windows.Forms.Timer { Interval = 10000 };
            t.Tick += (s, a) => { t.Dispose(); StartupLog.Flush(true); };
            t.Start();
            if (!TestHook.Enabled)
            {
                Activate();
                Native.SetForegroundWindow(Handle);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel) return;
            _closing = true;
            Popups.CloseAll(); Tips.Hide();
            SaveSettings();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Application.RemoveMessageFilter(this);
            Tray?.Dispose();
            Data.Dispose();
            base.OnFormClosed(e);
        }

        // ================================================================== data updates
        void OnDataUpdated()
        {
            if (_closing) return;
            Tray?.Update(Data);
            // holding Ctrl freezes the display (like Task Manager) so rows stop moving while you aim
            if (Native.KeyDown(Native.VK_CONTROL) && ActiveForm == this && !TestHook.Enabled) return;
            if (WindowState == FormWindowState.Minimized || !Visible) return;
            if (_fewerDetails) Fewer?.DataUpdated();
            else CurrentPage?.DataUpdated();
        }

        void OnStaticUpdated()
        {
            if (_closing) return;
            if (_fewerDetails) Fewer?.DataUpdated();
            else CurrentPage?.StaticUpdated();
        }

        public void SetUpdateSpeed(int s)
        {
            UpdateSpeed = s;
            Data.Interval = SpeedMs[s];
            foreach (var p in Pages) (p as PerformancePage)?.SpeedChanged();
        }

        // ================================================================== window messages
        protected override void WndProc(ref Message m)
        {
            if (TestHook.TryHandle(this, ref m)) return;
            if (m.Msg == Program.ActivateMessage && Program.ActivateMessage != 0)
            {
                // a second Task Manager start: bring this one forward (tests only count it, so focus is never taken)
                if (TestHook.Enabled) { TestHook.Activations++; m.Result = (IntPtr)1; return; }
                RestoreFromTray();
                int tab = (int)m.WParam - 1;
                if (tab >= 0 && tab <= 6) { if (_fewerDetails) SetFewerDetails(false); ShowPage(tab); }
                m.Result = (IntPtr)1;
                return;
            }
            switch (m.Msg)
            {
                case Native.WM_DPICHANGED:
                    {
                        int dpi = Native.HiWord(m.WParam);
                        var r = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                        Native.SetWindowPos(Handle, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                        ApplyDpi(dpi);
                        Invalidate(true);
                        m.Result = IntPtr.Zero;
                        return;
                    }
                case Native.WM_SYSKEYDOWN:
                    if ((int)m.WParam == Native.VK_MENU) { if (((long)m.LParam & 0x40000000) == 0) _altPending = true; }
                    else _altPending = false;
                    if ((int)m.WParam == 0x79) { m.Result = IntPtr.Zero; return; }   // F10 handled on key up
                    break;
                case Native.WM_KEYDOWN:
                    _altPending = false;
                    break;
                case Native.WM_SYSKEYUP:
                    if (((int)m.WParam == Native.VK_MENU && _altPending) || (int)m.WParam == 0x79)
                    {
                        _altPending = false;
                        if (Popups.AnyOpen) { Popups.CloseAll(); Menu.ExitKeyboard(); m.Result = IntPtr.Zero; return; }
                        if (!_fewerDetails && !_chromeHidden)
                        {
                            if (Menu.KeyboardMode) Menu.ExitKeyboard(); else Menu.EnterKeyboard();
                            Bar.ShowAccelerators = Menu.KeyboardMode; Bar.Invalidate();
                        }
                        m.Result = IntPtr.Zero;
                        return;
                    }
                    break;
                case Native.WM_SYSCHAR:
                    {
                        char c = (char)(int)m.WParam;
                        if (c == ' ') break;   // Alt+Space: system menu
                        if (!_fewerDetails && !_chromeHidden && Menu.HandleMnemonic(c)) { m.Result = IntPtr.Zero; return; }
                        if (!_fewerDetails && Bar.ButtonMnemonic(c)) { m.Result = IntPtr.Zero; return; }
                        if (_fewerDetails && Fewer != null && Fewer.Mnemonic(c)) { m.Result = IntPtr.Zero; return; }
                        m.Result = IntPtr.Zero;   // no beep for unknown Alt+letter
                        return;
                    }
                case Native.WM_SYSCOMMAND:
                    {
                        int sc = ((int)m.WParam) & 0xFFF0;
                        if (sc == Native.SC_KEYMENU && m.LParam == IntPtr.Zero) { m.Result = IntPtr.Zero; return; }
                        if (sc == Native.SC_MINIMIZE && HideWhenMinimized)
                        {
                            base.WndProc(ref m);
                            Hide();
                            return;
                        }
                        break;
                    }
                case Native.WM_ACTIVATEAPP:
                    if (m.WParam == IntPtr.Zero) { Popups.CloseAll(); Tips.Hide(); Menu.ExitKeyboard(); Bar.ShowAccelerators = false; }
                    break;
                case Native.WM_ACTIVATE:
                    {
                        bool active = ((int)m.WParam & 0xFFFF) != 0;
                        foreach (var p in Pages) p?.List?.SetActive(active);
                        Fewer?.SetActive(active);
                        break;
                    }
                case Native.WM_ENTERSIZEMOVE:
                    Popups.CloseAll(); Tips.Hide();
                    break;
                case Native.WM_SETTINGCHANGE:
                case Native.WM_THEMECHANGED:
                    DarkMode.ApplyToWindow(Handle);
                    break;
            }
            base.WndProc(ref m);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (Menu.KeyboardMode && !Popups.AnyOpen)
            {
                var code = keyData & Keys.KeyCode;
                if (Menu.HandleKeyboardKey(keyData)) { Bar.ShowAccelerators = Menu.KeyboardMode; Bar.Invalidate(); return true; }
                if (code >= Keys.A && code <= Keys.Z && (keyData & (Keys.Control | Keys.Alt)) == 0) { if (Menu.HandleMnemonic((char)code)) return true; }
                Menu.ExitKeyboard(); Bar.ShowAccelerators = false; Bar.Invalidate();
            }
            switch (keyData)
            {
                case Keys.F5: Data.RefreshNow(); return true;
                case Keys.Control | Keys.Tab:
                    if (!_fewerDetails && !_chromeHidden) ShowPage((_page + 1) % 7);
                    return true;
                case Keys.Control | Keys.Shift | Keys.Tab:
                    if (!_fewerDetails && !_chromeHidden) ShowPage((_page + 6) % 7);
                    return true;
            }
            if (_fewerDetails) { if (Fewer != null && Fewer.HandleKey(keyData)) return true; }
            else if (CurrentPage != null && CurrentPage.HandleKey(keyData)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public bool PreFilterMessage(ref Message m)
        {
            // automated tests replay input directly; ignore the physical mouse so it cannot interleave
            if (TestHook.Enabled && ((m.Msg >= 0x200 && m.Msg <= 0x20E) || (m.Msg >= 0xA0 && m.Msg <= 0xAD) || m.Msg == 0x2A3 || m.Msg == 0x2A2)) return true;
            if (Menu.KeyboardMode && (m.Msg == Native.WM_LBUTTONDOWN || m.Msg == Native.WM_RBUTTONDOWN) && !Popups.AnyOpen) { Menu.ExitKeyboard(); Bar.ShowAccelerators = false; Bar.Invalidate(); }
            return false;
        }

        /// <summary>Test hook: behave as if a key was pressed in the app.</summary>
        public void TestKey(Keys k)
        {
            if (Popups.AnyOpen)
            {
                if (!Popups.Top.HandleKey(k) && (k & Keys.KeyCode) == Keys.Escape) Popups.Close(Popups.Top);
                return;
            }
            var target = ActiveControl ?? (Control)this;
            var msg = Message.Create(target.Handle, Native.WM_KEYDOWN, (IntPtr)(int)(k & Keys.KeyCode), IntPtr.Zero);
            if (ProcessCmdKey(ref msg, k)) return;
            Native.SendMessage(target.Handle, Native.WM_KEYDOWN, (IntPtr)(int)(k & Keys.KeyCode), IntPtr.Zero);
            Native.SendMessage(target.Handle, Native.WM_KEYUP, (IntPtr)(int)(k & Keys.KeyCode), (IntPtr)unchecked((int)0xC0000001));
        }

        public void Bar_PressForTest() { if (_fewerDetails) Fewer?.PressEndTask(); else Bar.PressButton(); }

        // ================================================================== tray / minimize
        public void RestoreFromTray()
        {
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            Native.SetForegroundWindow(Handle);
            BringToFront();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Popups.CloseAll();
            if (WindowState == FormWindowState.Minimized && HideWhenMinimized && Visible) Hide();
        }

        /// <summary>Called after "Switch to" etc.: Options > Minimize on use.</summary>
        public void UsedToSwitch()
        {
            if (MinimizeOnUse) WindowState = FormWindowState.Minimized;
        }

        // ================================================================== settings
        void LoadSettings()
        {
            AlwaysOnTop = Settings.GetBool("AlwaysOnTop", false);
            MinimizeOnUse = Settings.GetBool("MinimizeOnUse", true);
            HideWhenMinimized = Settings.GetBool("HideWhenMinimized", false);
            ShowFullAccountName = Settings.GetBool("ShowFullAccountName", false);
            ShowHistoryAllProcesses = Settings.GetBool("ShowHistoryAllProcesses", false);
            DefaultTab = Math.Max(0, Math.Min(6, Settings.GetInt("DefaultTab", 0)));
            UpdateSpeed = Math.Max(0, Math.Min(3, Settings.GetInt("UpdateSpeed", 1)));
            ShowSuspendedStatus = Settings.GetBool("ShowSuspendedStatus", true);
            _fewerDetails = Settings.GetBool("FewerDetails", false);
            _moreBounds = ParseRect(Settings.Get("Window.More"));
            _fewerBounds = ParseRect(Settings.Get("Window.Fewer"));
            _moreMaximized = Settings.GetBool("Window.MoreMaximized", false);
        }

        public void SaveSettings()
        {
            var cur = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            if (_fewerDetails) _fewerBounds = cur; else { _moreBounds = cur; _moreMaximized = WindowState == FormWindowState.Maximized; }
            Settings.SetBool("AlwaysOnTop", AlwaysOnTop);
            Settings.SetBool("MinimizeOnUse", MinimizeOnUse);
            Settings.SetBool("HideWhenMinimized", HideWhenMinimized);
            Settings.SetBool("ShowFullAccountName", ShowFullAccountName);
            Settings.SetBool("ShowHistoryAllProcesses", ShowHistoryAllProcesses);
            Settings.SetInt("DefaultTab", DefaultTab);
            Settings.SetInt("UpdateSpeed", UpdateSpeed);
            Settings.SetBool("ShowSuspendedStatus", ShowSuspendedStatus);
            Settings.SetBool("FewerDetails", _fewerDetails);
            if (_moreBounds.Width > 0) Settings.Set("Window.More", FormatRect(_moreBounds));
            if (_fewerBounds.Width > 0) Settings.Set("Window.Fewer", FormatRect(_fewerBounds));
            Settings.SetBool("Window.MoreMaximized", _moreMaximized);
            foreach (var p in Pages) p?.SaveSettings();
            Settings.Save();
        }

        [DllImport("user32.dll")] static extern IntPtr MonitorFromRect(ref RECT r, int flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out int dpiX, out int dpiY);

        /// <summary>DPI of the monitor the window will open on (from the saved position), before the window exists.</summary>
        public static int ExpectedDpi()
        {
            try
            {
                var b = ParseRect(Settings.Get(Settings.GetBool("FewerDetails", false) ? "Window.Fewer" : "Window.More"));
                var r = b.Width > 0 ? new RECT { Left = b.Left, Top = b.Top, Right = b.Right, Bottom = b.Bottom } : new RECT { Left = 0, Top = 0, Right = 1, Bottom = 1 };
                IntPtr mon = MonitorFromRect(ref r, 1 /*MONITOR_DEFAULTTOPRIMARY*/);
                if (mon != IntPtr.Zero && GetDpiForMonitor(mon, 0 /*MDT_EFFECTIVE_DPI*/, out int dx, out _) == 0 && dx > 0) return dx;
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException) { ErrorLog.Write("ExpectedDpi", ex); }
            return 96;
        }

        static Rectangle ParseRect(string s)
        {
            if (string.IsNullOrEmpty(s)) return Rectangle.Empty;
            var a = s.Split(',');
            if (a.Length != 4) return Rectangle.Empty;
            int x, y, w, h;
            return int.TryParse(a[0], out x) && int.TryParse(a[1], out y) && int.TryParse(a[2], out w) && int.TryParse(a[3], out h) && w > 50 && h > 50 ? new Rectangle(x, y, w, h) : Rectangle.Empty;
        }
        static string FormatRect(Rectangle r) => r.X + "," + r.Y + "," + r.Width + "," + r.Height;

        public static Icon MakeAppIcon(int size)
        {
            using (var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp)) Glyphs.AppIcon(g, size);
                IntPtr h = bmp.GetHicon();
                var icon = (Icon)Icon.FromHandle(h).Clone();
                Sys.DestroyIcon(h);
                return icon;
            }
        }
    }
}
