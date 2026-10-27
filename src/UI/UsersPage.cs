using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TaskManagerDark
{
    /// <summary>Users tab: signed-in sessions with their aggregated resource use, expandable to their apps and processes.</summary>
    public sealed class UsersPage : Page
    {
        sealed class UserRow : ListRow
        {
            public int Session; public string Account, Domain, Display, Station, Client; public int State;
            public double Cpu, Disk, Net, Gpu; public long Memory; public int ProcCount;
            public bool InitialExpanded;
        }

        readonly TreeList _list = new TreeList { TallHeader = true, RowHeightLogical = 24 };
        readonly Dictionary<int, ProcessTree> _trees = new Dictionary<int, ProcessTree>();
        readonly Dictionary<int, UserRow> _users = new Dictionary<int, UserRow>();
        readonly Dictionary<string, string> _fullNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        IntPtr _userIcon, _serviceIcon; int _iconSize;
        int _ownSession;
        public override TreeList List => _list;

        public UsersPage(MainForm main) : base(main)
        {
            Sys.ProcessIdToSessionId(Sys.GetCurrentProcessId(), out _ownSession);
            _list.Columns.AddRange(new[]
            {
                new ListColumn { Id = "user", Title = Strings.ColUser, Width = 230, Fixed = true, MinWidth = 120 },
                new ListColumn { Id = "id", Title = Strings.ColId, Width = 50, Visible = false, Right = true },
                new ListColumn { Id = "session", Title = Strings.ColSession, Width = 90, Visible = false },
                new ListColumn { Id = "client", Title = Strings.ColClientName, Width = 100, Visible = false },
                new ListColumn { Id = "status", Title = Strings.ColStatus, Width = 95 },
                new ListColumn { Id = "cpu", Title = Strings.ColCpu, Width = 62, Right = true, Resource = true, DescendingFirst = true, HeaderTooltip = Strings.TipCpuTotal },
                new ListColumn { Id = "memory", Title = Strings.ColMemory, Width = 82, Right = true, Resource = true, DescendingFirst = true, HeaderTooltip = Strings.TipMemTotal },
                new ListColumn { Id = "disk", Title = Strings.ColDisk, Width = 72, Right = true, Resource = true, DescendingFirst = true, HeaderTooltip = Strings.TipDiskTotal },
                new ListColumn { Id = "network", Title = Strings.ColNetwork, Width = 72, Right = true, Resource = true, DescendingFirst = true, HeaderTooltip = Strings.TipNetTotal },
                new ListColumn { Id = "gpu", Title = Strings.ColGpu, Width = 62, Right = true, Resource = true, DescendingFirst = true, HeaderTooltip = Strings.TipGpuTotal },
            });
            for (int i = 0; i < _list.Columns.Count; i++) _list.Columns[i].Index = i;
            _list.CellOf = CellOf;
            _list.SearchTextOf = r => r is UserRow u ? Name(u) : (r as ProcNode)?.Name;
            _list.SortRequested += c => { ToggleSort(_list, c, true); Rebuild(); SaveSettings(); };
            _list.ColumnsChanged += SaveSettings;
            _list.SelectionChanged += () => Main.UpdateCommandBar();
            _list.RowContextMenu += OnRowMenu;
            _list.HeaderContextMenu += (c, p) =>
            {
                var items = new List<MenuItem>();
                foreach (var col in _list.Columns) { if (col.Fixed) continue; var cc = col; items.Add(MenuItem.Check(col.Title, () => cc.Visible, () => { cc.Visible = !cc.Visible; _list.SetRoots(new List<ListRow>(_list.Roots)); SaveSettings(); })); }
                Main.ShowContextMenu(items, p);
            };
            _list.RowActivated += r => { if (r.Expandable) _list.SetExpanded(r, !_list.IsExpanded(r)); };
            Controls.Add(_list);
        }

        protected override void OnLayout(LayoutEventArgs levent) { _list.Bounds = new Rectangle(0, 0, Width, Height); }
        public override void FocusContent() => _list.Focus();
        public override void ExpandAll(bool on) => _list.ExpandAll(on);
        public override List<MenuItem> ViewMenuItems() => Main.ExpandCollapseItems(this);
        public override void DataUpdated() { if (Visible) Rebuild(); }
        public override void StaticUpdated() { if (Visible) Rebuild(); }

        public override void SetDpi(int dpi)
        {
            base.SetDpi(dpi);
            int sz = D.S(16);
            if (sz != _iconSize)
            {
                _userIcon = ProcessesPage.MakeGlyphIcon(sz, (g, r) => Glyphs.User(g, r, Theme.C(0xB8C8D8)));
                _serviceIcon = ProcessesPage.MakeGlyphIcon(sz, (g, r) => Glyphs.Gear(g, r, Theme.C(0x9CB4CC)));
                _iconSize = sz;
            }
        }

        // ================================================================== sessions
        unsafe List<UserRow> Sessions()
        {
            var list = new List<UserRow>();
            if (!Sys.WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var info, out int count)) return list;
            try
            {
                int size = Marshal.SizeOf(typeof(Sys.WTS_SESSION_INFO));
                for (int i = 0; i < count; i++)
                {
                    var si = (Sys.WTS_SESSION_INFO)Marshal.PtrToStructure(info + i * size, typeof(Sys.WTS_SESSION_INFO));
                    string user = Query(si.SessionId, Sys.WTSUserName);
                    if (string.IsNullOrEmpty(user)) continue;
                    if (!_users.TryGetValue(si.SessionId, out var u)) { u = new UserRow { Key = "u:" + si.SessionId, Session = si.SessionId }; _users[si.SessionId] = u; }
                    u.Account = user; u.Domain = Query(si.SessionId, Sys.WTSDomainName);
                    u.Station = Marshal.PtrToStringUni(si.pWinStationName); u.State = si.State;
                    u.Client = Query(si.SessionId, Sys.WTSClientName);
                    u.Display = FullName(user);
                    list.Add(u);
                }
            }
            finally { Sys.WTSFreeMemory(info); }
            return list;
        }

        static string Query(int session, int cls)
        {
            if (!Sys.WTSQuerySessionInformation(IntPtr.Zero, session, cls, out var buf, out _)) return null;
            try { return Marshal.PtrToStringUni(buf); } finally { Sys.WTSFreeMemory(buf); }
        }

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)] static extern int NetUserGetInfo(string server, string user, int level, out IntPtr buf);
        [DllImport("netapi32.dll")] static extern int NetApiBufferFree(IntPtr buf);

        string FullName(string account)
        {
            if (_fullNames.TryGetValue(account, out var n)) return n;
            string result = null;
            try
            {
                if (NetUserGetInfo(null, account, 10, out var buf) == 0)
                {
                    try { result = Marshal.PtrToStringUni(Marshal.ReadIntPtr(buf, IntPtr.Size * 3)); }
                    finally { NetApiBufferFree(buf); }
                }
            }
            catch (Exception ex) { ErrorLog.Write("NetUserGetInfo", ex); }
            if (string.IsNullOrWhiteSpace(result)) result = null;
            _fullNames[account] = result;
            return result;
        }

        string Name(UserRow u)
        {
            string disp = u.Display ?? u.Account;
            if (Main.ShowFullAccountName) disp += " (" + (string.IsNullOrEmpty(u.Domain) ? "" : u.Domain + "\\") + u.Account + ")";
            return disp + (u.ProcCount > 0 ? " (" + u.ProcCount + ")" : "");
        }

        // ================================================================== rows
        void Rebuild()
        {
            var sessions = Sessions();
            var roots = new List<ListRow>();
            foreach (var u in sessions)
            {
                if (!_trees.TryGetValue(u.Session, out var tree)) { tree = new ProcessTree(); _trees[u.Session] = tree; }
                tree.ServiceIcon = _serviceIcon;
                int sid = u.Session; string acct = u.Account;
                tree.Build(Data, p => p.Cur.SessionId == sid && p.Static != null && string.Equals(p.Static.UserName, acct, StringComparison.OrdinalIgnoreCase));
                var kids = new List<ListRow>();
                kids.AddRange(tree.Apps); kids.AddRange(tree.Background); kids.AddRange(tree.Windows);
                u.Cpu = u.Disk = u.Net = u.Gpu = 0; u.Memory = 0; u.ProcCount = 0;
                foreach (ProcNode k in kids)
                {
                    u.Cpu += k.Cpu; u.Memory += k.Memory; u.Disk += k.Disk; u.Net += k.Net; u.Gpu = Math.Max(u.Gpu, k.Gpu); u.ProcCount += k.Members.Count;
                    BumpLevels(k, 1);
                }
                if (u.Cpu > 100) u.Cpu = 100;
                u.Icon = _userIcon;
                u.Children = kids;
                if (!u.InitialExpanded && u.Expandable) { u.InitialExpanded = true; _list.SetExpanded(u, true); }
                SortRows(kids);
                roots.Add(u);
            }
            SortRows(roots);
            var sys = Data.Sys;
            foreach (var c in _list.Columns)
            {
                switch (c.Id)
                {
                    case "cpu": c.HeaderTotal = Fmt.Pct0(sys.Cpu); c.HeaderHeat = sys.Cpu / 100; break;
                    case "memory": { double f = sys.MemTotal > 0 ? sys.MemInUse * 100.0 / sys.MemTotal : 0; c.HeaderTotal = Fmt.Pct0(f); c.HeaderHeat = f / 100; break; }
                    case "disk": c.HeaderTotal = Fmt.Pct0(sys.DiskMaxActive); c.HeaderHeat = sys.DiskMaxActive / 100; break;
                    case "network": c.HeaderTotal = Fmt.Pct0(sys.NetPrimaryUtil); c.HeaderHeat = sys.NetPrimaryUtil / 100; break;
                    case "gpu": c.HeaderTotal = Fmt.Pct0(sys.GpuMax); c.HeaderHeat = sys.GpuMax / 100; break;
                }
            }
            _list.SetRoots(roots);
            Main.UpdateCommandBar();
        }

        static void BumpLevels(ListRow r, int level)
        {
            r.Level = level;
            if (r.Children != null) foreach (var c in r.Children) BumpLevels(c, level + 1);
        }

        void SortRows(List<ListRow> rows)
        {
            string sort = _list.SortColumn; bool desc = _list.SortDescending;
            rows.Sort((a, b) =>
            {
                double x = 0, y = 0; string sx = Text(a), sy = Text(b);
                switch (sort)
                {
                    case "cpu": x = Val(a, 0); y = Val(b, 0); break;
                    case "memory": x = Val(a, 1); y = Val(b, 1); break;
                    case "disk": x = Val(a, 2); y = Val(b, 2); break;
                    case "network": x = Val(a, 3); y = Val(b, 3); break;
                    case "gpu": x = Val(a, 4); y = Val(b, 4); break;
                }
                int c = sort == null || sort == "user" || sort == "status" || sort == "session" || sort == "client" || sort == "id"
                    ? string.Compare(sx, sy, StringComparison.CurrentCultureIgnoreCase) : x.CompareTo(y);
                if (desc) c = -c;
                return c != 0 ? c : string.Compare(sx, sy, StringComparison.CurrentCultureIgnoreCase);
            });
            foreach (var r in rows) if (r.Children != null && r.Children.Count > 1 && !(r is UserRow)) SortRows(r.Children);
        }

        string Text(ListRow r) => r is UserRow u ? Name(u) : (r as ProcNode)?.Name ?? "";

        static double Val(ListRow r, int k)
        {
            if (r is UserRow u) return k == 0 ? u.Cpu : k == 1 ? u.Memory : k == 2 ? u.Disk : k == 3 ? u.Net : u.Gpu;
            var n = r as ProcNode;
            if (n == null) return 0;
            return k == 0 ? n.Cpu : k == 1 ? n.Memory : k == 2 ? n.Disk : k == 3 ? n.Net : n.Gpu;
        }

        Cell CellOf(ListRow r, ListColumn c)
        {
            if (r is UserRow u)
            {
                switch (c.Id)
                {
                    case "user": return Cell.Of(Name(u));
                    case "id": return Cell.Of(u.Session.ToString());
                    case "session": return Cell.Of(u.Station ?? "");
                    case "client": return Cell.Of(u.Client ?? "");
                    case "status": return Cell.Of(u.State == 4 ? "Disconnected" : "");
                    case "cpu": return Cell.Hot(Fmt.Pct1(u.Cpu), ProcessesPage.HeatCpu(u.Cpu));
                    case "memory": return Cell.Hot(Fmt.MBytes(u.Memory), Data.Sys.MemTotal > 0 ? Math.Min(1, Math.Sqrt((double)u.Memory / Data.Sys.MemTotal) * 1.4) : 0);
                    case "disk": return Cell.Hot(Fmt.MBps(u.Disk), ProcessesPage.HeatRate(u.Disk / 1048576.0));
                    case "network": return Cell.Hot(Fmt.Mbps(u.Net), ProcessesPage.HeatRate(u.Net / 1e6));
                    case "gpu": return Cell.Hot(Fmt.Pct1(u.Gpu), ProcessesPage.HeatCpu(u.Gpu));
                }
                return Cell.Of("");
            }
            var n = r as ProcNode;
            if (n == null) return Cell.Of("");
            bool values = n.Kind == NodeKind.Process;
            switch (c.Id)
            {
                case "user": return Cell.Of(n.Name);
                case "status": return Cell.Of(n.Hung ? Strings.NotResponding : n.Suspended && Main.ShowSuspendedStatus && values ? Strings.Suspended : "");
                case "cpu": return values ? Cell.Hot(Fmt.Pct1(n.Cpu), ProcessesPage.HeatCpu(n.Cpu)) : Cell.Of("");
                case "memory": return values ? Cell.Hot(Fmt.MBytes(n.Memory), Data.Sys.MemTotal > 0 ? Math.Min(1, Math.Sqrt((double)n.Memory / Data.Sys.MemTotal) * 1.4) : 0) : Cell.Of("");
                case "disk": return values ? Cell.Hot(Fmt.MBps(n.Disk), ProcessesPage.HeatRate(n.Disk / 1048576.0)) : Cell.Of("");
                case "network": return values ? Cell.Hot(Fmt.Mbps(n.Net), ProcessesPage.HeatRate(n.Net / 1e6)) : Cell.Of("");
                case "gpu": return values ? Cell.Hot(Fmt.Pct1(n.Gpu), ProcessesPage.HeatCpu(n.Gpu)) : Cell.Of("");
            }
            return Cell.Of("");
        }

        // ================================================================== commands
        public override void UpdateCommandBar(CommandBar bar)
        {
            var r = _list.SelectedRow;
            if (r is ProcNode n) { bar.ButtonText = Strings.EndTaskButton; bar.ButtonEnabled = n.Kind == NodeKind.Process; }
            else { bar.ButtonText = Strings.DisconnectButton; bar.ButtonEnabled = r is UserRow; }
            bar.LinkText = null; bar.LinkIcon = null;
        }

        public override void CommandButton()
        {
            var r = _list.SelectedRow;
            if (r is UserRow u) Disconnect(u);
            else if (r is ProcNode n && n.Kind == NodeKind.Process) Actions.EndTask(Main, n.Members, n.Name);
        }

        void Disconnect(UserRow u)
        {
            if (MessageDialog.Show(Main, Strings.AppTitle, Strings.DisconnectQuestion, null, new[] { Strings.DisconnectUser, "Cancel" }, 1, MessageDialog.Kind.None, null, out _) != 0) return;
            if (!Sys.WTSDisconnectSession(IntPtr.Zero, u.Session, false))
            {
                int e = Marshal.GetLastWin32Error();
                Actions.ShowFailure(Main, string.Format("User {0} (SessionId={1}) could not be disconnected.", u.Account, u.Session), e == 5, Sys.Win32Message(e));
            }
        }

        void SignOff(UserRow u)
        {
            if (MessageDialog.Show(Main, Strings.AppTitle, Strings.SignOutWarning, Strings.ContinueQuestion, new[] { Strings.SignOutUser, "Cancel" }, 1, MessageDialog.Kind.Warning, null, out _) != 0) return;
            if (!Sys.WTSLogoffSession(IntPtr.Zero, u.Session, false))
            {
                int e = Marshal.GetLastWin32Error();
                Actions.ShowFailure(Main, Strings.OperationFailed, e == 5, Sys.Win32Message(e));
            }
        }

        void OnRowMenu(ListRow row, Point screen)
        {
            if (row is UserRow u)
            {
                bool own = u.Session == _ownSession;
                bool open = _list.IsExpanded(u);
                var items = new List<MenuItem>
                {
                    // Taskmgr menu #30211 (User Context)
                    open ? new MenuItem("Colla&pse", () => _list.SetExpanded(u, false)) { Id = 30438 } : new MenuItem("Ex&pand", () => _list.SetExpanded(u, true)) { Id = 30437 },
                    new MenuItem("&Connect", () => ConnectDialog.ShowAsync(Main, u.Session, _ownSession, u.Account), () => !own) { Id = 30498 },
                    new MenuItem("&Disconnect", () => Disconnect(u)) { Id = 30464 },
                    new MenuItem("S&ign off", () => SignOff(u)) { Id = 30465 },
                    new MenuItem("&Send message", () => SendMessageDialog.ShowAsync(Main, u.Session)) { Id = 30462 },
                    new MenuItem("S&witch user account", () => Sys.WTSDisconnectSession(IntPtr.Zero, _ownSession, false), () => own) { Id = 30463 },
                    MenuItem.Sep(),
                    new MenuItem("&Manage user accounts", () => ShellLaunch.RunUnelevated("control.exe", "/name Microsoft.UserAccounts")) { Id = 30466 },
                };
                Main.ShowContextMenu(items, screen);
                return;
            }
            if (row is ProcNode n && n.Proc != null && n.Kind == NodeKind.Process)
            {
                var p = n.Proc;
                var items = new List<MenuItem>
                {
                    new MenuItem("&End task", () => Actions.EndTask(Main, n.Members, n.Name)) { Id = 30451 },
                    MenuItem.Sep(),
                    new MenuItem("&Create dump file", () => Actions.CreateDump(Main, p)) { Id = 30453 },
                    new MenuItem("&Go to details", () => Main.GoToDetails(p.Pid)) { Id = 30432 },
                    new MenuItem("&Open file location", () => Actions.OpenFileLocation(Main, p.Static?.ImagePath), () => !string.IsNullOrEmpty(p.Static?.ImagePath)) { Id = 30455 },
                    new MenuItem("&Search online", () => Actions.SearchOnline(p)) { Id = 30456 },
                    new MenuItem("Propert&ies", () => Actions.Properties(Main, p.Static?.ImagePath), () => !string.IsNullOrEmpty(p.Static?.ImagePath)) { Id = 30457 },
                };
                Main.ShowContextMenu(items, screen);
            }
        }

        public override void LoadSettings() => LoadColumns("Users", _list);
        public override void SaveSettings() => SaveColumns("Users", _list);
    }

    // ===================================================================== Send message / Connect

    public sealed class SendMessageDialog : DarkDialog
    {
        readonly DarkTextBox _title = new DarkTextBox(true), _msg = new DarkTextBox(true);

        SendMessageDialog(MainForm main, int session) : base(main, Strings.SendMessageTitle)
        {
            SetClientSizeScaled(330, 210);
            var l1 = AddLabel(Strings.MessageTitleLabel, 12, 10, 300, 18); l1.MnemonicTarget = _title;
            _title.Bounds = R(12, 30, 306, 46); _title.Font = Font; Controls.Add(_title);
            var l2 = AddLabel(Strings.MessageLabel, 12, 84, 300, 18); l2.MnemonicTarget = _msg;
            _msg.Bounds = R(12, 104, 306, 60); _msg.Font = Font; Controls.Add(_msg);
            _title.Text = string.Format(Strings.MessageFrom, Environment.UserName, DateTime.Now.ToString("g"));
            var ok = AddButton("OK", 162, 176, 75, DialogResult.None, true);
            AddButton("Cancel", 243, 176, 75, DialogResult.Cancel);
            ok.Click += (s, e) =>
            {
                if (!Sys.WTSSendMessage(IntPtr.Zero, session, _title.Text, _title.Text.Length * 2, _msg.Text, _msg.Text.Length * 2, 0 /*MB_OK*/, 0, out _, false))
                {
                    int err = Marshal.GetLastWin32Error();
                    MessageDialog.Error(this, string.Format("Your message to user with SessionId={0} could not be sent.", session), Sys.Win32Message(err));
                    return;
                }
                DialogResult = DialogResult.OK; Close();
            };
            ActiveControl = _msg;
        }

        public static void ShowAsync(MainForm main, int session) => DialogRunner.Show(main, () => new SendMessageDialog(main, session));
    }

    public sealed class ConnectDialog : DarkDialog
    {
        readonly DarkTextBox _pw = new DarkTextBox();

        ConnectDialog(MainForm main, int target, int own, string account) : base(main, Strings.AppTitle)
        {
            SetClientSizeScaled(320, 130);
            AddLabel("Enter the selected user's password", 12, 12, 296, 20);
            AddLabel(account, 12, 34, 296, 20, true);
            _pw.Bounds = R(12, 58, 296, 24); _pw.Font = Font; _pw.Box.UseSystemPasswordChar = true; Controls.Add(_pw);
            var ok = AddButton("OK", 152, 96, 75, DialogResult.None, true);
            AddButton("Cancel", 233, 96, 75, DialogResult.Cancel);
            ok.Click += (s, e) =>
            {
                if (!Sys.WTSConnectSession(target, own, _pw.Text, false))
                {
                    int err = Marshal.GetLastWin32Error();
                    MessageDialog.Error(this, string.Format("Unhandled error occurred while connecting.\n\n#{0} {1}", err, Sys.Win32Message(err)), null);
                    return;
                }
                DialogResult = DialogResult.OK; Close();
            };
            ActiveControl = _pw;
        }

        public static void ShowAsync(MainForm main, int target, int own, string account) => DialogRunner.Show(main, () => new ConnectDialog(main, target, own, account));
    }
}
