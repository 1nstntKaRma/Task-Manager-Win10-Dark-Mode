using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TaskManagerDark
{
    /// <summary>"Fewer details": just the running apps, End task and the "More details" toggle.</summary>
    public sealed class FewerDetailsView : Control
    {
        readonly MainForm _main;
        readonly TreeList _list = new TreeList { HeaderVisible = false, ShowExpandos = false, RowHeightLogical = 28, EmptyText = Strings.NoRunningApps };
        readonly CommandBar _bar = new CommandBar { ToggleText = Strings.MoreDetails, ToggleUp = false, ButtonText = Strings.EndTaskButton };
        readonly ProcessTree _tree = new ProcessTree();
        Dpi D = new Dpi(96);

        public FewerDetailsView(MainForm main)
        {
            _main = main;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
            _list.Columns.Add(new ListColumn { Id = "name", Title = Strings.ColName, Width = 2000, Fixed = true });
            _list.CellOf = (r, c) =>
            {
                // the compact view lists plain app names (no process counts)
                var n = (ProcNode)r;
                var name = n.Proc != null ? ProcessTree.ProcessName(n.Proc, _main.Data) : n.Name;
                return Cell.Of(n.Hung ? name + " (" + Strings.NotResponding + ")" : name);
            };
            _list.SearchTextOf = r => (r as ProcNode)?.Name;
            _list.SelectionChanged += UpdateBar;
            _list.RowActivated += r => SwitchTo(r as ProcNode);
            _list.RowContextMenu += OnMenu;
            _list.KeyCommand += k => { if (k == Keys.Delete) { EndTask(); return true; } if (k == Keys.Enter) { SwitchTo(_list.SelectedRow as ProcNode); return true; } return false; };
            _bar.ToggleClicked += () => _main.SetFewerDetails(false);
            _bar.ButtonClicked += EndTask;
            Controls.Add(_list); Controls.Add(_bar);
        }

        public TreeList List => _list;
        public void SetDpi(int dpi) { D = new Dpi(dpi); _list.SetDpi(dpi); _bar.SetDpi(dpi); PerformLayout(); Invalidate(true); }
        public void SetActive(bool on) => _list.SetActive(on);
        public void FocusList() => _list.Focus();

        protected override void OnLayout(LayoutEventArgs e)
        {
            int bh = _bar.PreferredHeight;
            _list.Bounds = new Rectangle(0, 0, Width, Math.Max(0, Height - bh));
            _bar.Bounds = new Rectangle(0, Height - bh, Width, bh);
            _list.Columns[0].Width = Math.Max(100, (int)(Width / D.Scale));
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { e.Graphics.FillRectangle(Theme.Brush(Theme.Surface), e.ClipRectangle); }

        public void DataUpdated()
        {
            if (!Visible) return;
            _tree.Build(_main.Data);
            var rows = new List<ListRow>();
            foreach (var n in _tree.Apps) { n.Children = null; rows.Add(n); }
            rows.Sort((a, b) => string.Compare(((ProcNode)a).Name, ((ProcNode)b).Name, StringComparison.CurrentCultureIgnoreCase));
            _list.SetRoots(rows);
            UpdateBar();
        }

        public override void Refresh() { DataUpdated(); base.Refresh(); }

        void UpdateBar()
        {
            _bar.ButtonEnabled = _list.SelectedRow != null;
            _bar.Invalidate();
        }

        void SwitchTo(ProcNode n)
        {
            if (n == null) return;
            foreach (var m in n.Members)
                if (_main.Data.Windows.TryGetValue(m.Pid, out var l) && l.Count > 0) { Actions.SwitchTo(_main, l[0]); return; }
        }

        void EndTask()
        {
            var n = _list.SelectedRow as ProcNode;
            if (n != null) Actions.EndTask(_main, n.Members, n.Name);
        }

        void OnMenu(ListRow row, Point screen)
        {
            var n = row as ProcNode;
            var items = new List<MenuItem>();
            if (n != null)
            {
                var p = n.Proc;
                items.Add(new MenuItem("Switch &to", () => SwitchTo(n)) { Bold = true, Id = 30458 });
                items.Add(new MenuItem("&End task", EndTask) { Id = 30451 });
                items.Add(MenuItem.Sep());
            }
            items.Add(new MenuItem("Run &new task", () => RunDialog.ShowAsync(_main)) { Id = 30467 });
            items.Add(MenuItem.Check("&Always on top", () => _main.AlwaysOnTop, _main.ToggleAlwaysOnTop));
            if (n != null)
            {
                var p = n.Proc;
                items.Add(MenuItem.Sep());
                items.Add(new MenuItem("&Open file location", () => Actions.OpenFileLocation(_main, p.Static?.ImagePath), () => !string.IsNullOrEmpty(p.Static?.ImagePath)) { Id = 30455 });
                items.Add(new MenuItem("&Search online", () => Actions.SearchOnline(p)) { Id = 30456 });
                items.Add(new MenuItem("Propert&ies", () => Actions.Properties(_main, p.Static?.ImagePath), () => !string.IsNullOrEmpty(p.Static?.ImagePath)) { Id = 30457 });
            }
            _main.ShowContextMenu(items, screen);
        }

        public bool HandleKey(Keys k) => false;
        public void PressEndTask() { if (_bar.ButtonEnabled) EndTask(); }
        public bool Mnemonic(char c)
        {
            if (char.ToUpperInvariant(c) == 'E' && _bar.ButtonEnabled) { EndTask(); return true; }
            if (char.ToUpperInvariant(c) == 'D') { _main.SetFewerDetails(false); return true; }   // More &details
            return false;
        }
    }
}
