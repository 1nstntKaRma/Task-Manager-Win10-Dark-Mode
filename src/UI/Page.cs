using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TaskManagerDark
{
    /// <summary>One tab of the main window.</summary>
    public abstract class Page : Control
    {
        protected readonly MainForm Main;
        protected DataEngine Data => Main.Data;
        protected Dpi D = new Dpi(96);
        public bool IsActive { get; private set; }

        protected Page(MainForm main)
        {
            Main = main;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
        }

        public virtual void SetDpi(int dpi)
        {
            D = new Dpi(dpi);
            foreach (Control c in Controls)
            {
                if (c is DarkControl dc) dc.SetDpi(dpi);
                else if (c is TreeList tl) tl.SetDpi(dpi);
            }
            PerformLayout();
            Invalidate(true);
        }

        public void SetActive(bool on)
        {
            IsActive = on;
            if (on) Activated(); else Deactivated();
        }

        protected virtual void Activated() { }
        protected virtual void Deactivated() { }
        public virtual void DataUpdated() { }
        public virtual void StaticUpdated() { }
        public virtual void UpdateCommandBar(CommandBar bar) { bar.ButtonText = null; bar.LinkText = null; bar.LinkIcon = null; }
        public virtual void CommandButton() { }
        public virtual void CommandLink() { }
        /// <summary>Items for the View menu between "Update speed" and the end (null: none).</summary>
        public virtual List<MenuItem> ViewMenuItems() => null;
        public virtual bool HandleKey(Keys k) => false;
        public virtual void LoadSettings() { }
        public virtual void SaveSettings() { }
        public virtual void FocusContent() { }
        public virtual void ExpandAll(bool on) { }
        public virtual TreeList List => null;

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { e.Graphics.FillRectangle(Theme.Brush(Theme.Surface), e.ClipRectangle); }

        // ------------------------------------------------------------ column persistence shared by all list pages
        protected static void SaveColumns(string prefix, TreeList list)
        {
            var order = new List<string>();
            foreach (var c in list.Columns)
            {
                order.Add(c.Id);
                Settings.SetInt(prefix + ".w." + c.Id, c.Width);
                Settings.SetBool(prefix + ".v." + c.Id, c.Visible);
            }
            Settings.Set(prefix + ".order", string.Join(",", order));
            Settings.Set(prefix + ".sort", list.SortColumn ?? "");
            Settings.SetBool(prefix + ".desc", list.SortDescending);
        }

        protected static void LoadColumns(string prefix, TreeList list)
        {
            foreach (var c in list.Columns)
            {
                c.Width = Math.Max(c.MinWidth, Settings.GetInt(prefix + ".w." + c.Id, c.Width));
                if (!c.Fixed) c.Visible = Settings.GetBool(prefix + ".v." + c.Id, c.Visible);
            }
            var order = Settings.Get(prefix + ".order");
            if (!string.IsNullOrEmpty(order))
            {
                var ids = order.Split(',');
                var sorted = new List<ListColumn>();
                foreach (var id in ids) { var c = list.Columns.Find(x => x.Id == id); if (c != null) sorted.Add(c); }
                foreach (var c in list.Columns) if (!sorted.Contains(c)) sorted.Add(c);
                // fixed columns stay first; everything else keeps the saved order (a stable partition, not Sort)
                list.Columns.Clear();
                list.Columns.AddRange(sorted.FindAll(c => c.Fixed));
                list.Columns.AddRange(sorted.FindAll(c => !c.Fixed));
            }
            var sort = Settings.Get(prefix + ".sort");
            if (sort != null) { list.SortColumn = sort.Length == 0 ? null : sort; list.SortDescending = Settings.GetBool(prefix + ".desc", false); }
        }

        /// <summary>Standard header behavior: click toggles the sort (numbers sort largest first), a third click on Processes removes it.</summary>
        protected static void ToggleSort(TreeList list, ListColumn c, bool allowNone)
        {
            if (list.SortColumn != c.Id) { list.SortColumn = c.Id; list.SortDescending = c.DescendingFirst; }
            else if (allowNone && list.SortDescending != c.DescendingFirst) { list.SortColumn = null; }
            else list.SortDescending = !list.SortDescending;
        }
    }
}
