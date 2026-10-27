using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TaskManagerDark
{
    /// <summary>Services tab: Name, PID, Description, Status, Group (the svchost group from the service's command line).</summary>
    public sealed class ServicesPage : Page
    {
        readonly TreeList _list = new TreeList { RowHeightLogical = 20, ShowExpandos = false, MultiSelect = true };
        readonly Dictionary<string, ListRow> _rows = new Dictionary<string, ListRow>(StringComparer.OrdinalIgnoreCase);
        IntPtr _icon; int _iconSize;
        public override TreeList List => _list;

        public ServicesPage(MainForm main) : base(main)
        {
            _list.Columns.AddRange(new[]
            {
                new ListColumn { Id = "name", Title = Strings.ColName, Width = 170, Fixed = true, Tooltip = "Service name" },
                new ListColumn { Id = "pid", Title = Strings.ColPid, Width = 60, Right = true, Tooltip = "Service process ID" },
                new ListColumn { Id = "desc", Title = Strings.ColDescription, Width = 280, Tooltip = "Description of the service" },
                new ListColumn { Id = "status", Title = Strings.ColStatus, Width = 80, Tooltip = "Service status" },
                new ListColumn { Id = "group", Title = Strings.ColGroup, Width = 160, Tooltip = "Service group" },
            });
            for (int i = 0; i < _list.Columns.Count; i++) _list.Columns[i].Index = i;
            _list.SortColumn = "name";
            _list.CellOf = CellOf;
            _list.SearchTextOf = r => ((SvcInfo)r.Tag).Name;
            _list.SortRequested += c => { ToggleSort(_list, c, false); Rebuild(); SaveSettings(); };
            _list.ColumnsChanged += SaveSettings;
            _list.SelectionChanged += () => Main.UpdateCommandBar();
            _list.RowContextMenu += OnRowMenu;
            _list.HeaderContextMenu += (c, p) => Main.ShowContextMenu(new List<MenuItem>
            {
                new MenuItem("Hi&de column", () => { c.Visible = false; _list.SetRoots(new List<ListRow>(_list.Roots)); SaveSettings(); }, () => c != null && !c.Fixed),
                new MenuItem("&Select columns", () => SelectColumnsDialog.ShowAsync(Main, _list, () => { SaveSettings(); Rebuild(); })),
            }, p);
            Controls.Add(_list);
        }

        protected override void OnLayout(LayoutEventArgs levent) { _list.Bounds = new Rectangle(0, 0, Width, Height); }
        public override void FocusContent() => _list.Focus();
        public override void DataUpdated() { if (Visible) Rebuild(); }

        public override void SetDpi(int dpi)
        {
            base.SetDpi(dpi);
            int sz = D.S(16);
            if (sz != _iconSize) { _icon = ProcessesPage.MakeGlyphIcon(sz, (g, r) => Glyphs.Gear(g, r, Theme.C(0x9CB4CC))); _iconSize = sz; }
        }

        Cell CellOf(ListRow r, ListColumn c)
        {
            var s = (SvcInfo)r.Tag;
            switch (c.Id)
            {
                case "name": return Cell.Of(s.Name);
                case "pid": return Cell.Of(s.Pid > 0 ? s.Pid.ToString() : "");
                case "desc": return Cell.Of(s.DisplayName);
                case "status": return Cell.Of(s.StatusText);
                case "group": return Cell.Of(s.Group ?? "");
            }
            return Cell.Of("");
        }

        void Rebuild()
        {
            var rows = new List<ListRow>(Data.Services.Count);
            foreach (var s in Data.Services)
            {
                if (!_rows.TryGetValue(s.Name, out var r)) { r = new ListRow { Key = "svc:" + s.Name }; _rows[s.Name] = r; }
                r.Tag = s; r.Icon = _icon;
                rows.Add(r);
            }
            string sort = _list.SortColumn; bool desc = _list.SortDescending;
            rows.Sort((a, b) =>
            {
                var x = (SvcInfo)a.Tag; var y = (SvcInfo)b.Tag;
                int c;
                switch (sort)
                {
                    case "pid": c = x.Pid.CompareTo(y.Pid); break;
                    case "desc": c = string.Compare(x.DisplayName, y.DisplayName, StringComparison.CurrentCultureIgnoreCase); break;
                    case "status": c = string.Compare(x.StatusText, y.StatusText, StringComparison.CurrentCultureIgnoreCase); break;
                    case "group": c = string.Compare(x.Group, y.Group, StringComparison.CurrentCultureIgnoreCase); break;
                    default: c = string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase); break;
                }
                if (desc) c = -c;
                return c != 0 ? c : string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
            });
            _list.SetRoots(rows);
        }

        public void SelectForPid(int pid)
        {
            Rebuild();
            bool first = true;
            foreach (var r in _list.FlatRows)
                if (((SvcInfo)r.Tag).Pid == pid) { if (first) { _list.Select(r); first = false; } }
        }

        public override void UpdateCommandBar(CommandBar bar)
        {
            bar.ButtonText = null;
            bar.LinkText = Strings.OpenServices;
            bar.LinkIcon = (g, r) => Glyphs.Gear(g, r, Theme.Link);
        }

        public override void CommandLink() => ServiceActions.OpenServices();

        void OnRowMenu(ListRow row, Point screen)
        {
            if (row == null) return;
            var sel = _list.SelectedRows;
            if (sel.Count == 0) return;
            var s = (SvcInfo)sel[0].Tag;
            bool single = sel.Count == 1;
            var items = new List<MenuItem>
            {
                // Taskmgr menu #30202 (Service Context)
                new MenuItem("&Start", () => { foreach (var r in sel) ServiceActions.Start(Main, ((SvcInfo)r.Tag).Name); }, () => sel.Exists(r => ((SvcInfo)r.Tag).State == 1)) { Id = 30415 },
                new MenuItem("S&top", () => { foreach (var r in sel) ServiceActions.Stop(Main, ((SvcInfo)r.Tag).Name); }, () => sel.Exists(r => ((SvcInfo)r.Tag).State == 4)) { Id = 30416 },
                new MenuItem("&Restart", () => { foreach (var r in sel) ServiceActions.Restart(Main, ((SvcInfo)r.Tag).Name); }, () => sel.Exists(r => ((SvcInfo)r.Tag).State == 4)) { Id = 30417 },
                MenuItem.Sep(),
                new MenuItem("Open Ser&vices", ServiceActions.OpenServices) { Id = 30460 },
                new MenuItem("Search &online", () => Actions.SearchOnline(s.Name + " " + s.DisplayName), () => single) { Id = 30422 },
                new MenuItem("Go to &details", () => Main.GoToDetails(s.Pid), () => single && s.Pid > 0) { Id = 30432 },
            };
            Main.ShowContextMenu(items, screen);
        }

        public override void LoadSettings() { LoadColumns("Services", _list); if (string.IsNullOrEmpty(_list.SortColumn)) _list.SortColumn = "name"; }
        public override void SaveSettings() => SaveColumns("Services", _list);
    }
}
