using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace TaskManagerDark
{
    /// <summary>App history tab: per-app CPU time and network use from SRUM (Windows' own usage database).</summary>
    public sealed class AppHistoryPage : Page
    {
        readonly TreeList _list = new TreeList { TallHeader = true, RowHeightLogical = 24, ShowExpandos = false };
        List<AppUsage> _apps = new List<AppUsage>();
        int _loading;
        long _loadedTick = -1000;
        Rectangle _linkRect;
        bool _linkHot;
        public override TreeList List => _list;

        public AppHistoryPage(MainForm main) : base(main)
        {
            _list.Columns.AddRange(new[]
            {
                new ListColumn { Id = "name", Title = Strings.ColName, Width = 260, Fixed = true, MinWidth = 120 },
                new ListColumn { Id = "cpu", Title = Strings.ColCpuTime, Width = 90, Right = true, Resource = true, DescendingFirst = true, Tooltip = "Amount of time spent by the CPU executing instructions" },
                new ListColumn { Id = "net", Title = Strings.ColNetwork, Width = 95, Right = true, Resource = true, DescendingFirst = true, Tooltip = "Amount of network activity, including downloads and uploads" },
                new ListColumn { Id = "metered", Title = Strings.ColMetered, Width = 115, Right = true, Resource = true, DescendingFirst = true, Tooltip = "Network activity over a metered network connection" },
                new ListColumn { Id = "tile", Title = Strings.ColTileUpdates, Width = 95, Right = true, Resource = true, DescendingFirst = true, Tooltip = "Total network usage for tile updates and notifications" },
                new ListColumn { Id = "nonmetered", Title = Strings.ColNonMetered, Width = 140, Visible = false, Right = true, Resource = true, DescendingFirst = true, Tooltip = "Amount of network activity over a non-metered connection" },
                new ListColumn { Id = "down", Title = Strings.ColDownloads, Width = 95, Visible = false, Right = true, Resource = true, DescendingFirst = true, Tooltip = "Download activity by the application" },
                new ListColumn { Id = "up", Title = Strings.ColUploads, Width = 95, Visible = false, Right = true, Resource = true, DescendingFirst = true, Tooltip = "Upload activity by the application" },
            });
            for (int i = 0; i < _list.Columns.Count; i++) _list.Columns[i].Index = i;
            _list.CellOf = CellOf;
            _list.SearchTextOf = r => ((AppUsage)r.Tag).Name;
            _list.SortRequested += c => { ToggleSort(_list, c, false); Rebuild(); SaveSettings(); };
            _list.ColumnsChanged += SaveSettings;
            _list.RowContextMenu += OnRowMenu;
            _list.HeaderContextMenu += (c, p) =>
            {
                var items = new List<MenuItem>();
                foreach (var col in _list.Columns) { if (col.Fixed) continue; var cc = col; items.Add(MenuItem.Check(col.Title, () => cc.Visible, () => { cc.Visible = !cc.Visible; _list.SetRoots(new List<ListRow>(_list.Roots)); SaveSettings(); })); }
                Main.ShowContextMenu(items, p);
            };
            _list.RowActivated += r => SwitchTo((AppUsage)r.Tag);
            _list.EmptyText = Strings.NoData;
            Controls.Add(_list);
        }

        int HeaderStrip => D.S(30);
        protected override void OnLayout(LayoutEventArgs levent) { _list.Bounds = new Rectangle(0, HeaderStrip, Width, Math.Max(0, Height - HeaderStrip)); }
        public override void FocusContent() => _list.Focus();
        protected override void Activated() { Reload(); }

        public override void DataUpdated()
        {
            // SRUM records arrive in batches; refreshing once a minute is plenty
            if (Visible && Data.Tick - _loadedTick >= 60) Reload();
        }

        public void Reload()
        {
            if (Interlocked.Exchange(ref _loading, 1) == 1) return;
            _loadedTick = Data.Tick;
            bool all = Main.ShowHistoryAllProcesses;
            int iconSize = D.S(16);
            var services = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in Data.Services) services[s.Name] = s.DisplayName;
            if (_apps.Count == 0) { _list.EmptyText = "Loading..."; _list.Invalidate(); }
            var t = new Thread(() =>
            {
                List<AppUsage> apps = null;
                try { apps = AppHistory.Load(all, iconSize, n => services.TryGetValue(n, out var d) ? d : null); }
                catch (Exception ex) { ErrorLog.Write("AppHistory.Load", ex); }
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        Interlocked.Exchange(ref _loading, 0);
                        if (apps != null) _apps = apps;
                        _list.EmptyText = AppHistory.Error != null && _apps.Count == 0 ? AppHistory.Error : Strings.NoData;
                        Rebuild();
                        Invalidate(new Rectangle(0, 0, Width, HeaderStrip));
                    }));
                }
                catch (InvalidOperationException) { }
            }) { IsBackground = true, Name = "AppHistory" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        void Rebuild()
        {
            var rows = new List<ListRow>(_apps.Count);
            double mhz = Data.Cpu.BaseMhz;
            long totCpu = 0, totNet = 0, totMet = 0, totTile = 0, totNon = 0, totDown = 0, totUp = 0;
            foreach (var a in _apps)
            {
                rows.Add(new ListRow { Key = a.Key, Tag = a, Icon = a.Icon });
                totCpu += a.Cycles; totNet += a.Network; totMet += a.Metered; totTile += a.Tile; totNon += a.NonMetered; totDown += a.Downloads; totUp += a.Uploads;
            }
            foreach (var c in _list.Columns)
                switch (c.Id)
                {
                    case "cpu": c.HeaderTotal = CpuText(totCpu, mhz); break;
                    case "net": c.HeaderTotal = MB(totNet); break;
                    case "metered": c.HeaderTotal = MB(totMet); break;
                    case "tile": c.HeaderTotal = MB(totTile); break;
                    case "nonmetered": c.HeaderTotal = MB(totNon); break;
                    case "down": c.HeaderTotal = MB(totDown); break;
                    case "up": c.HeaderTotal = MB(totUp); break;
                }
            _maxCpu = 1; _maxNet = 1;
            foreach (var a in _apps) { _maxCpu = Math.Max(_maxCpu, a.Cycles); _maxNet = Math.Max(_maxNet, Math.Max(a.Network, a.Tile)); }
            string sort = _list.SortColumn; bool desc = _list.SortDescending;
            rows.Sort((x, y) =>
            {
                var a = (AppUsage)x.Tag; var b = (AppUsage)y.Tag;
                int r;
                switch (sort)
                {
                    case "cpu": r = a.Cycles.CompareTo(b.Cycles); break;
                    case "net": r = a.Network.CompareTo(b.Network); break;
                    case "metered": r = a.Metered.CompareTo(b.Metered); break;
                    case "tile": r = a.Tile.CompareTo(b.Tile); break;
                    case "nonmetered": r = a.NonMetered.CompareTo(b.NonMetered); break;
                    case "down": r = a.Downloads.CompareTo(b.Downloads); break;
                    case "up": r = a.Uploads.CompareTo(b.Uploads); break;
                    default: r = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); break;
                }
                if (desc) r = -r;
                return r != 0 ? r : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            _list.SetRoots(rows);
        }

        long _maxCpu = 1, _maxNet = 1;

        static string MB(long bytes) => (bytes / 1048576.0).ToString("#,##0.0") + " MB";

        static string CpuText(long cycles, double mhz)
        {
            // Taskmgr: cycles / (base MHz * 10^6) seconds, at least 1 second when there was any use, as h:mm:ss
            double secs = mhz > 0 ? cycles / (mhz * 1e6) : 0;
            long s = (long)secs;
            if (cycles > 0 && s < 1) s = 1;
            return (s / 3600) + ":" + (s / 60 % 60).ToString("00") + ":" + (s % 60).ToString("00");
        }

        double HeatOf(long v, long max) => v <= 0 ? 0 : Math.Min(1, Math.Sqrt((double)v / max));

        Cell CellOf(ListRow r, ListColumn c)
        {
            var a = (AppUsage)r.Tag;
            switch (c.Id)
            {
                case "name": return Cell.Of(a.Name);
                case "cpu": return Cell.Hot(CpuText(a.Cycles, Data.Cpu.BaseMhz), HeatOf(a.Cycles, _maxCpu));
                case "net": return Cell.Hot(MB(a.Network), HeatOf(a.Network, _maxNet));
                case "metered": return Cell.Hot(MB(a.Metered), HeatOf(a.Metered, _maxNet));
                case "tile": return Cell.Hot(MB(a.Tile), HeatOf(a.Tile, _maxNet));
                case "nonmetered": return Cell.Hot(MB(a.NonMetered), HeatOf(a.NonMetered, _maxNet));
                case "down": return Cell.Hot(MB(a.Downloads), HeatOf(a.Downloads, _maxNet));
                case "up": return Cell.Hot(MB(a.Uploads), HeatOf(a.Uploads, _maxNet));
            }
            return Cell.Of("");
        }

        // ------------------------------------------------------------ header strip
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.FillRectangle(Theme.Brush(Theme.Surface), new Rectangle(0, 0, Width, HeaderStrip));
            var f = Theme.UiFont(D);
            var since = DateTime.FromFileTimeUtc(AppHistory.Since).ToLocalTime().ToShortDateString();
            string text = string.Format(Main.ShowHistoryAllProcesses ? Strings.UsageSinceAll : Strings.UsageSinceUser, since);
            int tw = Draw.Measure(text, f).Width;
            Draw.Text(g, text, f, new Rectangle(D.S(10), 0, Width - D.S(20), HeaderStrip), Theme.Text, Draw.TextLeft | Draw.Ellipsis);
            int lw = Draw.Measure(Strings.DeleteUsageHistory, f).Width;
            _linkRect = new Rectangle(D.S(10) + tw + D.S(16), (HeaderStrip - D.S(20)) / 2, lw, D.S(20));
            var lf = _linkHot ? Theme.GetFont(D, 12f, FontStyle.Underline) : f;
            Draw.Text(g, Strings.DeleteUsageHistory, lf, _linkRect, _linkHot ? Theme.LinkHover : Theme.Link, Draw.TextLeft);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool h = _linkRect.Contains(e.Location);
            Cursor = h ? Cursors.Hand : Cursors.Default;
            if (h != _linkHot) { _linkHot = h; Invalidate(new Rectangle(0, 0, Width, HeaderStrip)); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (_linkHot) { _linkHot = false; Invalidate(new Rectangle(0, 0, Width, HeaderStrip)); } }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && _linkRect.Contains(e.Location)) DeleteHistory();
        }

        public void DeleteHistory()
        {
            AppHistory.Reset();
            _apps = new List<AppUsage>();
            Rebuild();
            Invalidate(new Rectangle(0, 0, Width, HeaderStrip));
            Reload();
        }

        // ------------------------------------------------------------ menu (Taskmgr #30214)
        ProcInfo RunningProcess(AppUsage a)
        {
            foreach (var p in Data.Procs)
            {
                if (a.PackageFullName != null && p.Static?.PackageFullName == a.PackageFullName && Data.Windows.ContainsKey(p.Pid)) return p;
                if (a.Kind == 0 && a.Path != null && string.Equals(p.Static?.ImagePath, a.Path, StringComparison.OrdinalIgnoreCase) && Data.Windows.ContainsKey(p.Pid)) return p;
            }
            return null;
        }

        void SwitchTo(AppUsage a)
        {
            var p = RunningProcess(a);
            if (p != null && Data.Windows.TryGetValue(p.Pid, out var wl) && wl.Count > 0) Actions.SwitchTo(Main, wl[0]);
        }

        void OnRowMenu(ListRow row, Point screen)
        {
            if (row == null) return;
            var a = (AppUsage)row.Tag;
            bool exe = a.Kind == 0 && a.Path != null && System.IO.File.Exists(a.Path);
            var items = new List<MenuItem>
            {
                new MenuItem("Switch &to", () => SwitchTo(a), () => RunningProcess(a) != null) { Bold = true, Id = 30458 },
                new MenuItem("&Search online", () => Actions.SearchOnline(a.Name), () => a.Kind != 3) { Id = 30456 },
                new MenuItem("&Properties", () => Actions.Properties(Main, a.Path), () => exe) { Id = 30457 },
            };
            Main.ShowContextMenu(items, screen);
        }

        public void Test(string what) { if (what == "reload") Reload(); }

        public override void LoadSettings() { LoadColumns("AppHistory", _list); }
        public override void SaveSettings() => SaveColumns("AppHistory", _list);
    }
}
