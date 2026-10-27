using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TaskManagerDark
{
    public sealed class ProcessesPage : Page
    {
        readonly TreeList _list = new TreeList { TallHeader = true, RowHeightLogical = 24, EmptyText = "" };
        readonly ProcessTree _tree = new ProcessTree();
        public bool GroupByType = true;
        public bool MemPercent, DiskPercent, NetPercent;          // View > Resource values
        IntPtr _serviceIcon; int _serviceIconSize;
        public override TreeList List => _list;

        public ProcessesPage(MainForm main) : base(main)
        {
            _list.Columns.AddRange(new[]
            {
                new ListColumn { Id = "name", Title = Strings.ColName, Width = 230, MinWidth = 120, Fixed = true },
                new ListColumn { Id = "type", Title = Strings.ColType, Width = 120, Visible = false },
                new ListColumn { Id = "status", Title = Strings.ColStatus, Width = 85 },
                new ListColumn { Id = "publisher", Title = Strings.ColPublisher, Width = 150, Visible = false },
                new ListColumn { Id = "pid", Title = Strings.ColPid, Width = 60, Visible = false, Right = true },
                new ListColumn { Id = "procname", Title = Strings.ColProcessName, Width = 130, Visible = false },
                new ListColumn { Id = "cmdline", Title = Strings.ColCommandLine, Width = 260, Visible = false },
                new ListColumn { Id = "cpu", Title = Strings.ColCpu, Width = 62, Right = true, Resource = true, DescendingFirst = true, HeaderTooltip = Strings.TipCpuTotal },
                new ListColumn { Id = "memory", Title = Strings.ColMemory, Width = 82, Right = true, Resource = true, DescendingFirst = true, HeaderTooltip = Strings.TipMemTotal },
                new ListColumn { Id = "disk", Title = Strings.ColDisk, Width = 72, Right = true, Resource = true, DescendingFirst = true, HeaderTooltip = Strings.TipDiskTotal },
                new ListColumn { Id = "network", Title = Strings.ColNetwork, Width = 72, Right = true, Resource = true, DescendingFirst = true, HeaderTooltip = Strings.TipNetTotal },
                new ListColumn { Id = "gpu", Title = Strings.ColGpu, Width = 62, Right = true, Resource = true, DescendingFirst = true, HeaderTooltip = Strings.TipGpuTotal },
                new ListColumn { Id = "gpuengine", Title = Strings.ColGpuEngine, Width = 100 },
                new ListColumn { Id = "power", Title = Strings.ColPower, Width = 95, Resource = true, DescendingFirst = true, Tooltip = Strings.TipPower },
                new ListColumn { Id = "powertrend", Title = Strings.ColPowerTrend, Width = 110, Resource = true, DescendingFirst = true, Tooltip = Strings.TipPowerTrend },
            });
            for (int i = 0; i < _list.Columns.Count; i++) _list.Columns[i].Index = i;
            _list.CellOf = CellOf;
            _list.SearchTextOf = r => (r as ProcNode)?.Name;
            _list.SortRequested += c => { ToggleSort(_list, c, true); Rebuild(); SaveSettings(); };
            _list.ColumnsChanged += () => SaveSettings();
            _list.SelectionChanged += () => Main.UpdateCommandBar();
            _list.RowContextMenu += OnRowMenu;
            _list.HeaderContextMenu += OnHeaderMenu;
            _list.RowActivated += r =>
            {
                var n = r as ProcNode;
                if (n == null) return;
                if (n.Kind == NodeKind.Window) { Actions.SwitchTo(Main, n.Window); return; }
                if (n.Expandable) _list.SetExpanded(n, !_list.IsExpanded(n));
            };
            _list.KeyCommand += k =>
            {
                if (k == Keys.Delete) { EndSelected(); return true; }
                if (k == Keys.Enter) { var n = _list.SelectedRow as ProcNode; if (n != null && n.IsApp && n.Level == 0 && n.Proc != null) { SwitchToApp(n); return true; } }
                return false;
            };
            Controls.Add(_list);
        }

        protected override void OnLayout(LayoutEventArgs levent) { _list.Bounds = new Rectangle(0, 0, Width, Height); }
        public override void FocusContent() => _list.Focus();
        public override void ExpandAll(bool on) => _list.ExpandAll(on);

        public override void SetDpi(int dpi)
        {
            base.SetDpi(dpi);
            int sz = D.S(16);
            if (_serviceIconSize != sz) { _serviceIcon = MakeGlyphIcon(sz, (g, r) => Glyphs.Gear(g, r, Theme.C(0x9CB4CC))); _serviceIconSize = sz; }
            _tree.ServiceIcon = _serviceIcon;
        }

        public static IntPtr MakeGlyphIcon(int size, Action<Graphics, RectangleF> draw)
        {
            using (var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.Transparent); draw(g, new RectangleF(1, 1, size - 2, size - 2)); }
                return bmp.GetHicon();
            }
        }

        public override void DataUpdated() { if (Visible) Rebuild(); }
        public override void StaticUpdated() { if (Visible) Rebuild(); }

        // ================================================================== rows
        void Rebuild()
        {
            _tree.Build(Data);
            foreach (var c in _list.Columns)
            {
                // without administrator rights there is no kernel trace: say how the columns are measured
                if (c.Id == "disk") c.Tooltip = Data.EtwActive ? null : "Without administrator rights, Disk shows each process's total I/O (file, network and device). Run as administrator for exact per-process disk activity.";
                if (c.Id == "network") c.Tooltip = Data.EtwActive ? null : "Per-process network activity needs administrator rights (File > Run as administrator).";
            }
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
            var roots = new List<ListRow>();
            Comparison<ListRow> cmp = Compare;
            if (GroupByType)
            {
                AddGroup(roots, "g:apps", Strings.GroupApps, _tree.Apps, cmp);
                AddGroup(roots, "g:background", Strings.GroupBackground, _tree.Background, cmp);
                AddGroup(roots, "g:windows", Strings.GroupWindows, _tree.Windows, cmp);
            }
            else
            {
                var all = new List<ListRow>();
                all.AddRange(_tree.Apps); all.AddRange(_tree.Background); all.AddRange(_tree.Windows);
                SortRecursive(all, cmp);
                roots = all;
            }
            _list.SetRoots(roots);
            Main.UpdateCommandBar();
        }

        void AddGroup(List<ListRow> roots, string key, string title, List<ProcNode> nodes, Comparison<ListRow> cmp)
        {
            if (nodes.Count == 0) return;
            var g = new ProcNode { Key = key, IsGroup = true, Kind = NodeKind.Group, GroupText = title + " (" + nodes.Count + ")", Name = title };
            g.Children = new List<ListRow>(nodes);
            SortRecursive(g.Children, cmp);
            roots.Add(g);
        }

        static void SortRecursive(List<ListRow> rows, Comparison<ListRow> cmp)
        {
            rows.Sort(cmp);
            foreach (var r in rows) if (r.Children != null && r.Children.Count > 1) SortRecursive(r.Children, cmp);
        }

        int Compare(ListRow a, ListRow b)
        {
            var x = (ProcNode)a; var y = (ProcNode)b;
            // windows and services stay below child processes in their natural order
            if (x.Kind != y.Kind) return KindOrder(x.Kind).CompareTo(KindOrder(y.Kind));
            int r = 0;
            switch (_list.SortColumn)
            {
                case "cpu": r = x.Cpu.CompareTo(y.Cpu); break;
                case "memory": r = x.Memory.CompareTo(y.Memory); break;
                case "disk": r = x.Disk.CompareTo(y.Disk); break;
                case "network": r = x.Net.CompareTo(y.Net); break;
                case "gpu": r = x.Gpu.CompareTo(y.Gpu); break;
                case "power": r = x.Power.CompareTo(y.Power); break;
                case "powertrend": r = x.PowerTrend.CompareTo(y.PowerTrend); break;
                case "pid": r = (x.Proc?.Pid ?? -1).CompareTo(y.Proc?.Pid ?? -1); break;
                case "status": r = string.Compare(StatusText(x), StatusText(y), StringComparison.CurrentCultureIgnoreCase); break;
                case "type": r = x.Group.CompareTo(y.Group); break;
                case "publisher": r = string.Compare(x.Proc?.Static?.Company, y.Proc?.Static?.Company, StringComparison.CurrentCultureIgnoreCase); break;
                case "procname": r = string.Compare(x.Proc?.Name, y.Proc?.Name, StringComparison.CurrentCultureIgnoreCase); break;
                case "cmdline": r = string.Compare(x.Proc?.Static?.CommandLine, y.Proc?.Static?.CommandLine, StringComparison.CurrentCultureIgnoreCase); break;
                case "gpuengine": r = string.Compare(Data.GpuEngineName(x.GpuEngine), Data.GpuEngineName(y.GpuEngine), StringComparison.CurrentCultureIgnoreCase); break;
                case "name": r = string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase); break;
            }
            if (_list.SortDescending) r = -r;
            if (r == 0 && x.Kind != NodeKind.Window && x.Kind != NodeKind.Service) r = string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase);
            if (r == 0) r = string.CompareOrdinal(x.Key, y.Key);
            return r;
        }

        static int KindOrder(NodeKind k) => k == NodeKind.Window ? 2 : k == NodeKind.Service ? 3 : 1;

        string StatusText(ProcNode n)
        {
            if (n.Kind != NodeKind.Process) return n.Kind == NodeKind.Window && n.Hung ? Strings.NotResponding : "";
            if (n.Hung) return Strings.NotResponding;
            if (n.Suspended && Main.ShowSuspendedStatus) return Strings.Suspended;
            return "";
        }

        Cell CellOf(ListRow row, ListColumn c)
        {
            var n = (ProcNode)row;
            bool values = n.Kind == NodeKind.Process || n.Kind == NodeKind.Interrupts;
            switch (c.Id)
            {
                case "name": return Cell.Of(n.Name);
                case "type": return Cell.Of(n.Level == 0 && n.Kind == NodeKind.Process ? (n.Group == ProcGroup.Apps ? Strings.TypeApp : n.Group == ProcGroup.Windows ? Strings.TypeWindows : Strings.TypeBackground) : "");
                case "status":
                    {
                        var s = StatusText(n);
                        var cell = Cell.Of(s);
                        if (s == Strings.Suspended) cell.Glyph = 1;
                        if (s == Strings.NotResponding) cell.Fore = Theme.C(0xF0A35E);
                        return cell;
                    }
                case "publisher": return Cell.Of(n.Kind == NodeKind.Process ? n.Proc?.Static?.Company : "");
                case "pid": return Cell.Of(n.Kind == NodeKind.Process && n.Proc != null && (n.Members.Count == 1 || n.Level > 0) ? n.Proc.Pid.ToString() : "");
                case "procname": return Cell.Of(n.Kind == NodeKind.Process ? n.Proc?.Name : "");
                case "cmdline": return Cell.Of(n.Kind == NodeKind.Process ? n.Proc?.Static?.CommandLine : "");
                case "cpu": return values ? Cell.Hot(Fmt.Pct1(n.Cpu), HeatCpu(n.Cpu)) : Cell.Of("");
                case "memory":
                    if (n.Kind == NodeKind.Interrupts) return Cell.Hot(MemPercent ? "0%" : "0 MB", 0);
                    if (!values) return Cell.Of("");
                    return Cell.Hot(MemPercent ? Fmt.Pct1(Data.Sys.MemTotal > 0 ? n.Memory * 100.0 / Data.Sys.MemTotal : 0) : Fmt.MBytes(n.Memory), HeatMem(n.Memory));
                case "disk":
                    if (!values) return Cell.Of("");
                    return Cell.Hot(DiskPercent ? Fmt.Pct1(DiskShare(n.Disk)) : Fmt.MBps(n.Disk), HeatRate(n.Disk / (1024.0 * 1024.0)));
                case "network":
                    if (!values) return Cell.Of("");
                    if (!Data.EtwActive && n.Kind == NodeKind.Process) return Cell.Hot("N/A", 0);
                    return Cell.Hot(NetPercent ? Fmt.Pct1(NetShare(n.Net)) : Fmt.Mbps(n.Net), HeatRate(n.Net / 1e6));
                case "gpu": return values ? Cell.Hot(Fmt.Pct1(n.Gpu), HeatCpu(n.Gpu)) : Cell.Of("");
                case "gpuengine": return Cell.Of(values && n.Gpu > 0 ? Data.GpuEngineName(n.GpuEngine) : "");
                case "power":
                    {
                        if (!values) return Cell.Of("");
                        int lvl = DataEngine.PowerLevel(n.Power);
                        return Cell.Hot(Strings.PowerLevels[lvl], lvl * 0.22);
                    }
                case "powertrend":
                    {
                        if (!values) return Cell.Of("");
                        int lvl = DataEngine.PowerLevel(n.PowerTrend);
                        return Cell.Hot(Strings.PowerLevels[lvl], lvl * 0.22);
                    }
            }
            return Cell.Of("");
        }

        double DiskShare(double bps)
        {
            double total = 0; foreach (var d in Data.Sys.Disks) total += d.ReadBps + d.WriteBps;
            return total > 0 ? Math.Min(100, bps * 100.0 / total * Math.Max(0.01, Data.Sys.DiskMaxActive) / 100.0) : 0;
        }

        double NetShare(double bits)
        {
            foreach (var n in Data.Sys.Nets) if (n.Connected && n.LinkBps > 0) return Math.Min(100, bits * 100.0 / n.LinkBps);
            return 0;
        }

        public static double HeatCpu(double pct) => pct < 0.05 ? 0 : Math.Min(1, Math.Sqrt(pct / 100.0) * 1.05);
        public double HeatMem(long bytes) => Data.Sys.MemTotal <= 0 ? 0 : Math.Min(1, Math.Sqrt((double)bytes / Data.Sys.MemTotal) * 1.4);
        public static double HeatRate(double mb) => mb <= 0.0001 ? 0 : Math.Min(1, Math.Log10(1 + mb * 10) / Math.Log10(1 + 1000));

        // ================================================================== commands
        public override void UpdateCommandBar(CommandBar bar)
        {
            var n = _list.SelectedRow as ProcNode;
            bool explorer = n != null && n.Kind == NodeKind.Process && n.Level == 0 && n.Proc != null && n.Proc.Name.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase);
            bar.ButtonText = explorer ? Strings.RestartButton : Strings.EndTaskButton;
            bar.ButtonEnabled = n != null && (n.Kind == NodeKind.Process || n.Kind == NodeKind.Window) && n.Proc != null;
            bar.LinkText = null; bar.LinkIcon = null;
        }

        public override void CommandButton() => EndSelected();

        void EndSelected()
        {
            var n = _list.SelectedRow as ProcNode;
            if (n == null || n.Proc == null) return;
            if (n.Kind == NodeKind.Process && n.Level == 0 && n.Proc.Name.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)) { Actions.RestartExplorer(Main); return; }
            if (n.Kind == NodeKind.Window) { Actions.EndTaskWindow(Main, n.Window, n.Proc); return; }
            Actions.EndTask(Main, n.Level == 0 ? n.Members : new List<ProcInfo> { n.Proc }, n.Name);
        }

        void SwitchToApp(ProcNode n)
        {
            WinInfo w = null;
            foreach (var m in n.Members) if (Data.Windows.TryGetValue(m.Pid, out var l) && l.Count > 0) { w = l[0]; break; }
            if (w != null) Actions.SwitchTo(Main, w);
        }

        public override List<MenuItem> ViewMenuItems()
        {
            var l = new List<MenuItem>
            {
                MenuItem.Check("&Group by type", () => GroupByType, () => { GroupByType = !GroupByType; Rebuild(); SaveSettings(); }),
            };
            l.AddRange(Main.ExpandCollapseItems(this));
            l.AddRange(Main.StatusValuesMenu());
            return l;
        }

        List<MenuItem> ResourceValuesMenu() => new List<MenuItem>
        {
            MenuItem.Popup("&Memory", new List<MenuItem> { MenuItem.RadioItem("Percents", () => MemPercent, () => { MemPercent = true; Rebuild(); SaveSettings(); }), MenuItem.RadioItem("Values", () => !MemPercent, () => { MemPercent = false; Rebuild(); SaveSettings(); }) }),
            MenuItem.Popup("Dis&k", new List<MenuItem> { MenuItem.RadioItem("Percents", () => DiskPercent, () => { DiskPercent = true; Rebuild(); SaveSettings(); }), MenuItem.RadioItem("Values", () => !DiskPercent, () => { DiskPercent = false; Rebuild(); SaveSettings(); }) }),
            MenuItem.Popup("&Network", new List<MenuItem> { MenuItem.RadioItem("Percents", () => NetPercent, () => { NetPercent = true; Rebuild(); SaveSettings(); }), MenuItem.RadioItem("Values", () => !NetPercent, () => { NetPercent = false; Rebuild(); SaveSettings(); }) }),
        };

        void OnRowMenu(ListRow row, Point screen)
        {
            var n = row as ProcNode;
            if (n == null || n.IsGroup) return;
            var items = new List<MenuItem>();
            if (n.Kind == NodeKind.Window)
            {
                // Taskmgr menu #30209 (Window Context)
                var w = n.Window;
                items.Add(new MenuItem("Switch &to", () => Actions.SwitchTo(Main, w)) { Bold = true, Id = 30458 });
                items.Add(new MenuItem("&Bring to front", () => Actions.BringToFront(w)) { Id = 30495 });
                items.Add(new MenuItem("&Minimize", () => Native.ShowWindow(w.Hwnd, 6)) { Id = 30496 });
                items.Add(new MenuItem("Ma&ximize", () => Native.ShowWindow(w.Hwnd, Native.SW_SHOWMAXIMIZED)) { Id = 30497 });
                items.Add(new MenuItem("&End task", () => Actions.EndTaskWindow(Main, w, n.Proc)) { Id = 30451 });
                Main.ShowContextMenu(items, screen);
                return;
            }
            if (n.Kind == NodeKind.Service)
            {
                // Taskmgr menu #30210 (Service Context)
                var s = n.Service;
                items.Add(new MenuItem("&Stop", () => ServiceActions.Stop(Main, s.Name), () => s.State == 4) { Id = 30459 });
                items.Add(new MenuItem("&Open Services", ServiceActions.OpenServices) { Id = 30460 });
                items.Add(new MenuItem("&Search online", () => Actions.SearchOnline(s.Name + " " + s.DisplayName)) { Id = 30456 });
                Main.ShowContextMenu(items, screen);
                return;
            }
            if (n.Kind == NodeKind.Interrupts)
            {
                items.Add(MenuItem.Popup("Resource &values", ResourceValuesMenu()));
                items.Add(new MenuItem("&Search online", () => Actions.SearchOnline("System interrupts")) { Id = 30456 });
                Main.ShowContextMenu(items, screen);
                return;
            }
            // Taskmgr menu #30208 (Process Context)
            var p = n.Proc;
            bool explorer = n.Level == 0 && p.Name.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase);
            if (n.Expandable)
            {
                bool open = _list.IsExpanded(n);
                items.Add(open ? new MenuItem("Colla&pse", () => _list.SetExpanded(n, false)) { Id = 30438 } : new MenuItem("Ex&pand", () => _list.SetExpanded(n, true)) { Id = 30437, Bold = !n.IsApp });
            }
            if (n.IsApp && n.Level == 0) items.Add(new MenuItem("Switch &to", () => SwitchToApp(n)) { Bold = true, Id = 30458 });
            if (explorer) items.Add(new MenuItem("&Restart", () => Actions.RestartExplorer(Main)) { Id = 30450 });
            else items.Add(new MenuItem("&End task", EndSelected) { Id = 30451 });
            items.Add(MenuItem.Popup("Resource &values", ResourceValuesMenu()));
            items.Add(MenuItem.Sep());
            if (Actions.HasDebugger) items.Add(new MenuItem("&Debug", () => Actions.Debug(Main, p)) { Id = 30452 });
            items.Add(new MenuItem("&Create dump file", () => Actions.CreateDump(Main, p)) { Id = 30453 });
            items.Add(MenuItem.Sep());
            items.Add(new MenuItem("&Go to details", () => Main.GoToDetails(p.Pid)) { Id = 30432 });
            items.Add(new MenuItem("&Open file location", () => Actions.OpenFileLocation(Main, p.Static?.ImagePath), () => !string.IsNullOrEmpty(p.Static?.ImagePath)) { Id = 30455 });
            items.Add(new MenuItem("&Search online", () => Actions.SearchOnline(p)) { Id = 30456 });
            items.Add(new MenuItem("Propert&ies", () => Actions.Properties(Main, p.Static?.ImagePath), () => !string.IsNullOrEmpty(p.Static?.ImagePath)) { Id = 30457 });
            Main.ShowContextMenu(items, screen);
        }

        void OnHeaderMenu(ListColumn col, Point screen)
        {
            var items = new List<MenuItem>();
            foreach (var c in _list.Columns)
            {
                if (c.Fixed) continue;
                var cc = c;
                items.Add(MenuItem.Check(c.Title, () => cc.Visible, () => { cc.Visible = !cc.Visible; _list.Invalidate(); _list.SetRoots(new List<ListRow>(_list.Roots)); SaveSettings(); }));
            }
            items.Add(MenuItem.Sep());
            items.Add(MenuItem.Popup("Resource &values", ResourceValuesMenu()));
            Main.ShowContextMenu(items, screen);
        }

        public void SelectPid(int pid)
        {
            Rebuild();
            if (_tree.NodeOfPid.TryGetValue(pid, out var n)) _list.SelectKey(n.Key);
        }

        // ================================================================== settings
        public override void LoadSettings()
        {
            LoadColumns("Processes", _list);
            GroupByType = Settings.GetBool("Processes.GroupByType", true);
            MemPercent = Settings.GetBool("Processes.MemPercent", false);
            DiskPercent = Settings.GetBool("Processes.DiskPercent", false);
            NetPercent = Settings.GetBool("Processes.NetPercent", false);
        }

        public override void SaveSettings()
        {
            SaveColumns("Processes", _list);
            Settings.SetBool("Processes.GroupByType", GroupByType);
            Settings.SetBool("Processes.MemPercent", MemPercent);
            Settings.SetBool("Processes.DiskPercent", DiskPercent);
            Settings.SetBool("Processes.NetPercent", NetPercent);
        }
    }
}
