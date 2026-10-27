using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace TaskManagerDark
{
    /// <summary>
    /// Startup tab: Run keys and Startup folders, enabled/disabled through StartupApproved like Windows does. The list
    /// is read on a background thread: it opens every startup program's file, and a program on a hard disk that has
    /// spun down takes seconds to answer.
    /// </summary>
    public sealed class StartupPage : Page
    {
        readonly TreeList _list = new TreeList { RowHeightLogical = 24, EmptyText = "" };
        List<StartupItem> _items = new List<StartupItem>();
        int _loadTick = -100;
        bool _loading, _reloadAgain;
        List<StartupItem> _loaded;                       // handed over by the loader thread
        readonly object _lock = new object();
        readonly ManualResetEvent _firstLoad = new ManualResetEvent(false);
        public override TreeList List => _list;

        sealed class ProcRow : ListRow { public ProcInfo Proc; }

        public StartupPage(MainForm main) : base(main)
        {
            _list.Columns.AddRange(new[]
            {
                new ListColumn { Id = "name", Title = Strings.ColName, Width = 220, Fixed = true, MinWidth = 120 },
                new ListColumn { Id = "publisher", Title = Strings.ColPublisher, Width = 170 },
                new ListColumn { Id = "status", Title = Strings.ColStatus, Width = 80 },
                new ListColumn { Id = "impact", Title = Strings.ColStartupImpact, Width = 110, Tooltip = Strings.TipStartupImpact },
                new ListColumn { Id = "type", Title = Strings.ColStartupType, Width = 90, Visible = false },
                new ListColumn { Id = "disk", Title = Strings.ColDiskAtStartup, Width = 120, Visible = false, Right = true, DescendingFirst = true, Tooltip = Strings.TipDiskAtStartup },
                new ListColumn { Id = "cpu", Title = Strings.ColCpuAtStartup, Width = 100, Visible = false, Right = true, DescendingFirst = true, Tooltip = Strings.TipCpuAtStartup },
                new ListColumn { Id = "running", Title = Strings.ColRunningNow, Width = 90, Visible = false },
                new ListColumn { Id = "disabled", Title = Strings.ColDisabledTime, Width = 140, Visible = false },
                new ListColumn { Id = "cmdline", Title = Strings.ColCommandLine, Width = 300, Visible = false },
            });
            for (int i = 0; i < _list.Columns.Count; i++) _list.Columns[i].Index = i;
            _list.CellOf = CellOf;
            _list.SearchTextOf = r => r.Tag is StartupItem it ? it.Name : (r as ProcRow)?.Proc.DisplayName;
            _list.SortRequested += c => { ToggleSort(_list, c, false); Rebuild(); SaveSettings(); };
            _list.ColumnsChanged += SaveSettings;
            _list.SelectionChanged += () => Main.UpdateCommandBar();
            _list.RowContextMenu += OnRowMenu;
            _list.HeaderContextMenu += (c, p) =>
            {
                var items = new List<MenuItem>();
                foreach (var col in _list.Columns) { if (col.Fixed) continue; var cc = col; items.Add(MenuItem.Check(col.Title, () => cc.Visible, () => { cc.Visible = !cc.Visible; _list.SetRoots(new List<ListRow>(_list.Roots)); SaveSettings(); })); }
                Main.ShowContextMenu(items, p);
            };
            _list.KeyCommand += k => { if (k == Keys.Delete) { Toggle(); return true; } return false; };
            Controls.Add(_list);
        }

        int HeaderStrip => D.S(30);

        protected override void OnLayout(LayoutEventArgs levent) { _list.Bounds = new Rectangle(0, HeaderStrip, Width, Math.Max(0, Height - HeaderStrip)); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.FillRectangle(Theme.Brush(Theme.Surface), new Rectangle(0, 0, Width, HeaderStrip));
            var secs = StartupItems.LastBiosSeconds();
            if (secs.HasValue)
            {
                var f = Theme.UiFont(D);
                string value = string.Format(Strings.Seconds, secs.Value.ToString("0.0"));
                int vw = Draw.Measure(value, Theme.UiFontBold(D)).Width;
                var r = new Rectangle(0, 0, Width - D.S(14), HeaderStrip);
                Draw.Text(g, value, Theme.UiFontBold(D), r, Theme.Text, Draw.TextRight);
                Draw.Text(g, Strings.LastBiosTime, f, new Rectangle(0, 0, r.Right - vw, HeaderStrip), Theme.TextDim, Draw.TextRight);
            }
        }

        public override void FocusContent() => _list.Focus();
        public override void ExpandAll(bool on) => _list.ExpandAll(on);
        protected override void Activated() { Reload(); }

        public override void DataUpdated()
        {
            if (!Visible) return;
            if (Data.Tick - _loadTick >= 5) Reload(); else Rebuild();
        }

        public void Reload()
        {
            _loadTick = Data.Tick;
            if (_loading) { _reloadAgain = true; return; }
            _loading = true;
            int iconSize = D.S(16);
            var t = new Thread(() =>
            {
                List<StartupItem> items = null;
                try { StartupItems.IconSize = iconSize; items = StartupItems.Load(); }
                catch (Exception ex) { ErrorLog.Write("Startup.Load", ex); }
                lock (_lock) _loaded = items;
                _firstLoad.Set();
                try { BeginInvoke(new Action(TakeLoaded)); }
                catch (InvalidOperationException) { }   // window closed meanwhile
            }) { IsBackground = true, Name = "StartupItems" };
            t.SetApartmentState(ApartmentState.STA);   // shortcuts and icons go through the shell
            t.Start();
        }

        void TakeLoaded()
        {
            List<StartupItem> items;
            lock (_lock) { items = _loaded; _loaded = null; }
            if (items == null) return;   // already taken (WaitFirstLoad), or the load failed
            _loading = false;
            _items = items;
            _list.EmptyText = Strings.NoStartupItems;
            Rebuild();
            Invalidate(new Rectangle(0, 0, Width, HeaderStrip));
            if (_reloadAgain) { _reloadAgain = false; Reload(); }
        }

        /// <summary>Before the window first shows on this tab: give the list a moment so it is not painted empty.</summary>
        public void WaitFirstLoad(int ms)
        {
            if (_firstLoad.WaitOne(ms)) TakeLoaded();
        }

        void Rebuild()
        {
            var rows = new List<ListRow>();
            foreach (var it in _items)
            {
                var r = new ListRow { Key = it.Key, Tag = it, Icon = it.Icon };
                if (it.ImagePath != null)
                    foreach (var p in Data.Procs)
                        if (p.Static?.ImagePath != null && string.Equals(p.Static.ImagePath, it.ImagePath, StringComparison.OrdinalIgnoreCase))
                        {
                            (r.Children ?? (r.Children = new List<ListRow>())).Add(new ProcRow { Key = it.Key + "|" + p.Key, Tag = null, Proc = p, Level = 1, Icon = p.Static.IconSmall });
                        }
                rows.Add(r);
            }
            string sort = _list.SortColumn; bool desc = _list.SortDescending;
            rows.Sort((a, b) =>
            {
                var x = (StartupItem)a.Tag; var y = (StartupItem)b.Tag;
                int c;
                switch (sort)
                {
                    case "publisher": c = string.Compare(x.Publisher, y.Publisher, StringComparison.CurrentCultureIgnoreCase); break;
                    case "status": c = x.Enabled.CompareTo(y.Enabled); break;
                    case "impact": c = ImpactRank(x.Impact).CompareTo(ImpactRank(y.Impact)); break;
                    case "type": c = x.IsFolder.CompareTo(y.IsFolder); break;
                    case "disk": c = x.DiskBytes.CompareTo(y.DiskBytes); break;
                    case "cpu": c = x.CpuMs.CompareTo(y.CpuMs); break;
                    case "running": c = (a.Children != null).CompareTo(b.Children != null); break;
                    case "disabled": c = Nullable.Compare(x.DisabledTime, y.DisabledTime); break;
                    case "cmdline": c = string.Compare(x.Command, y.Command, StringComparison.CurrentCultureIgnoreCase); break;
                    default: c = string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase); break;
                }
                if (desc) c = -c;
                return c != 0 ? c : string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            _list.SetRoots(rows);
            Main.UpdateCommandBar();
        }

        static int ImpactRank(string s) => s == Strings.ImpactHigh ? 4 : s == Strings.ImpactMedium ? 3 : s == Strings.ImpactLow ? 2 : s == Strings.ImpactNotMeasured ? 1 : 0;

        Cell CellOf(ListRow r, ListColumn c)
        {
            if (r is ProcRow pr)
            {
                return c.Id == "name" ? Cell.Of(pr.Proc.DisplayName) : Cell.Of("");
            }
            var it = (StartupItem)r.Tag;
            switch (c.Id)
            {
                case "name": return Cell.Of(it.Name);
                case "publisher": return Cell.Of(it.Publisher);
                case "status": return Cell.Of(it.Enabled ? Strings.Enabled : Strings.Disabled);
                case "impact": return Cell.Of(it.Impact);
                case "type": return Cell.Of(it.IsFolder ? Strings.StartupFolder : Strings.StartupRegistry);
                case "disk": return Cell.Of(it.DiskBytes >= 0 ? (it.DiskBytes / 1048576.0).ToString("0.0") + " MB" : "");
                case "cpu": return Cell.Of(it.CpuMs >= 0 ? Fmt.Num(it.CpuMs) + " ms" : "");
                case "running": return Cell.Of(r.Children != null ? Strings.Running : "");
                case "disabled": return Cell.Of(it.DisabledTime.HasValue ? it.DisabledTime.Value.ToString("g") : "");
                case "cmdline": return Cell.Of(it.Command);
            }
            return Cell.Of("");
        }

        StartupItem SelectedItem => (_list.SelectedRow?.Tag as StartupItem) ?? ((_list.SelectedRow as ProcRow) != null ? _list.SelectedRow.Parent?.Tag as StartupItem : null);

        public override void UpdateCommandBar(CommandBar bar)
        {
            var it = _list.SelectedRow?.Tag as StartupItem;
            bar.ButtonText = it != null && !it.Enabled ? Strings.EnableButton : Strings.DisableButton;
            bar.ButtonEnabled = it != null;
            bar.LinkText = null; bar.LinkIcon = null;
        }

        public override void CommandButton() => Toggle();

        void Toggle()
        {
            var it = _list.SelectedRow?.Tag as StartupItem;
            if (it != null) SetEnabled(it, !it.Enabled);
        }

        void SetEnabled(StartupItem it, bool enable)
        {
            int err = StartupItems.SetEnabled(it, enable);
            if (err != 0) { Actions.ShowFailure(Main, Strings.OperationFailed, err == 5, Sys.Win32Message(err)); return; }
            Rebuild();   // the item already shows its new state; the reload confirms it
            Reload();
        }

        void OnRowMenu(ListRow row, Point screen)
        {
            if (row == null) return;
            var items = new List<MenuItem>();
            if (row is ProcRow pr)
            {
                // Taskmgr menu #30217 (Startup Process Context)
                var p = pr.Proc;
                items.Add(new MenuItem("&Open file location", () => Actions.OpenFileLocation(Main, p.Static?.ImagePath), () => !string.IsNullOrEmpty(p.Static?.ImagePath)) { Id = 30455 });
                items.Add(new MenuItem("&Search online", () => Actions.SearchOnline(p)) { Id = 30456 });
                items.Add(new MenuItem("Propert&ies", () => Actions.Properties(Main, p.Static?.ImagePath), () => !string.IsNullOrEmpty(p.Static?.ImagePath)) { Id = 30457 });
                Main.ShowContextMenu(items, screen);
                return;
            }
            // Taskmgr menu #30213 (Startup Item Context)
            var it = (StartupItem)row.Tag;
            if (row.Expandable)
                items.Add(_list.IsExpanded(row) ? new MenuItem("Colla&pse", () => _list.SetExpanded(row, false)) { Id = 30438 } : new MenuItem("Ex&pand", () => _list.SetExpanded(row, true)) { Id = 30437 });
            items.Add(it.Enabled ? new MenuItem("&Disable", () => SetEnabled(it, false)) { Id = 37602 } : new MenuItem("&Enable", () => SetEnabled(it, true)) { Id = 37603 });
            items.Add(MenuItem.Sep());
            items.Add(new MenuItem("&Open file location", () => Actions.OpenFileLocation(Main, it.ImagePath), () => it.ImagePath != null && System.IO.File.Exists(it.ImagePath)) { Id = 30455 });
            items.Add(new MenuItem("&Search online", () => Actions.SearchOnline(System.IO.Path.GetFileName(it.ImagePath ?? it.SourceName) + " " + it.Name)) { Id = 30456 });
            items.Add(new MenuItem("Propert&ies", () => Actions.Properties(Main, it.ImagePath), () => it.ImagePath != null && System.IO.File.Exists(it.ImagePath)) { Id = 30457 });
            Main.ShowContextMenu(items, screen);
        }

        public override List<MenuItem> ViewMenuItems() => Main.ExpandCollapseItems(this);
        public override void LoadSettings() => LoadColumns("Startup", _list);
        public override void SaveSettings() => SaveColumns("Startup", _list);
    }
}
