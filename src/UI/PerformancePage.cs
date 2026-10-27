using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;

namespace TaskManagerDark
{
    public enum PerfKind { Cpu, Memory, Disk, Net, Gpu }

    /// <summary>One resource in the Performance sidebar.</summary>
    public sealed class PerfItem
    {
        public PerfKind Kind;
        public string Key;
        public DiskState Disk;
        public NetState Net;
        public GpuState Gpu;
        public GraphColors Colors => Kind == PerfKind.Cpu ? Theme.CpuGraph : Kind == PerfKind.Memory ? Theme.MemGraph : Kind == PerfKind.Disk ? Theme.DiskGraph : Kind == PerfKind.Net ? Theme.NetGraph : Theme.GpuGraph;
    }

    public sealed class PerformancePage : Page
    {
        readonly PerfSidebar _side;
        readonly PerfDetail _detail;
        readonly List<PerfItem> _items = new List<PerfItem>();
        string _selectedKey = "cpu";
        public int CpuView;            // 0 overall, 1 logical processors, 2 NUMA nodes
        public bool KernelTimes, ShowGraphs = true, GpuMulti = true;
        public readonly int[] GpuEngineSel = { -1, -1, -1, -1 };   // -1: Task Manager's default for that slot
        bool _graphSummary, _sideSummary;

        public PerformancePage(MainForm main) : base(main)
        {
            _side = new PerfSidebar(this);
            _detail = new PerfDetail(this);
            Controls.Add(_side); Controls.Add(_detail);
        }

        public DataEngine D2 => Data;
        public MainForm MainWindow => Main;
        public IReadOnlyList<PerfItem> Items => _items;
        public PerfItem Selected => _items.Find(i => i.Key == _selectedKey) ?? (_items.Count > 0 ? _items[0] : null);
        public bool GraphSummary => _graphSummary;
        public bool SideSummary => _sideSummary;

        public void Select(PerfItem it)
        {
            if (it == null) return;
            _selectedKey = it.Key;
            _side.Invalidate(); _detail.Invalidate();
            Settings.Set("Performance.Selected", _selectedKey);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            int sw = D.S(212);
            if (_graphSummary) { _side.Visible = false; _detail.Visible = true; _detail.Bounds = new Rectangle(0, 0, Width, Height); return; }
            if (_sideSummary) { _detail.Visible = false; _side.Visible = true; _side.Bounds = new Rectangle(0, 0, Width, Height); return; }
            _side.Visible = _detail.Visible = true;
            _side.Bounds = new Rectangle(0, 0, Math.Min(sw, Width / 2), Height);
            _detail.Bounds = new Rectangle(_side.Right, 0, Math.Max(0, Width - _side.Right), Height);
        }

        public override void DataUpdated()
        {
            SyncItems();
            if (!Visible) return;
            _side.Invalidate(); _detail.Invalidate();
        }

        void SyncItems()
        {
            var keys = new List<string> { "cpu", "mem" };
            foreach (var d in Data.Disks) keys.Add("disk" + d.Static.Number);
            foreach (var n in Data.Nets) keys.Add("net" + n.Static.Luid);
            foreach (var g in Data.Gpus) keys.Add("gpu" + g.Static.Index);
            bool same = keys.Count == _items.Count;
            for (int i = 0; same && i < keys.Count; i++) same = _items[i].Key == keys[i];
            if (same)
            {
                // states can be replaced when a device reappears: refresh references
                int k = 2;
                foreach (var d in Data.Disks) _items[k++].Disk = d;
                foreach (var n in Data.Nets) _items[k++].Net = n;
                foreach (var g in Data.Gpus) _items[k++].Gpu = g;
                return;
            }
            _items.Clear();
            _items.Add(new PerfItem { Kind = PerfKind.Cpu, Key = "cpu" });
            _items.Add(new PerfItem { Kind = PerfKind.Memory, Key = "mem" });
            foreach (var d in Data.Disks) _items.Add(new PerfItem { Kind = PerfKind.Disk, Key = "disk" + d.Static.Number, Disk = d });
            foreach (var n in Data.Nets) _items.Add(new PerfItem { Kind = PerfKind.Net, Key = "net" + n.Static.Luid, Net = n });
            foreach (var g in Data.Gpus) _items.Add(new PerfItem { Kind = PerfKind.Gpu, Key = "gpu" + g.Static.Index, Gpu = g });
            _side.Invalidate();
        }

        public void SpeedChanged() { _detail.Invalidate(); }

        /// <summary>Engine shown in a GPU chart slot: the user's pick, else 3D / Copy / Video Encode / Video Decode.</summary>
        public int EngineForSlot(int slot, string[] names, int count)
        {
            int sel = GpuEngineSel[slot];
            if (sel >= 0) return Math.Min(Math.Max(0, count - 1), sel);
            string[] preferred = { "3D", "Copy", "Video Encode", "Video Decode" };
            int idx = Array.FindIndex(names, n => string.Equals(n, preferred[slot], StringComparison.OrdinalIgnoreCase));
            return idx >= 0 ? idx : Math.Min(Math.Max(0, count - 1), slot);
        }

        public string TimeLabel => Main.UpdateSpeed == 0 ? Strings.Seconds30 : Main.UpdateSpeed == 2 ? Strings.Minutes4 : Main.UpdateSpeed == 3 ? Strings.Paused : Strings.Seconds60;

        public void ToggleGraphSummary()
        {
            _graphSummary = !_graphSummary; _sideSummary = false;
            Main.SetChromeHidden(_graphSummary);
            PerformLayout(); Invalidate(true);
        }

        public void ToggleSideSummary()
        {
            _sideSummary = !_sideSummary; _graphSummary = false;
            Main.SetChromeHidden(_sideSummary);
            PerformLayout(); Invalidate(true);
        }

        protected override void Deactivated()
        {
            if (_graphSummary || _sideSummary) { _graphSummary = _sideSummary = false; Main.SetChromeHidden(false); PerformLayout(); }
        }

        public override void UpdateCommandBar(CommandBar bar)
        {
            bar.ButtonText = null;
            bar.LinkText = Strings.OpenResourceMonitor;
            bar.LinkIcon = (g, r) => Glyphs.Gauge(g, r, Theme.Link);
        }

        public override void CommandLink()
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("resmon.exe") { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { }
            catch (Exception ex) { ErrorLog.Write("resmon", ex); }
        }

        public override bool HandleKey(Keys k)
        {
            if (k == (Keys.Control | Keys.C)) { CopyDetails(); return true; }
            if (k == Keys.Up || k == Keys.Down)
            {
                int i = _items.IndexOf(Selected) + (k == Keys.Up ? -1 : 1);
                if (i >= 0 && i < _items.Count) Select(_items[i]);
                return true;
            }
            return false;
        }

        public override void FocusContent() => _side.Focus();

        // ================================================================== menus (Taskmgr #30206 / #30207)
        public void ShowDetailMenu(Point screen)
        {
            var it = Selected;
            if (it == null) return;
            var items = new List<MenuItem>();
            if (it.Kind == PerfKind.Cpu)
            {
                items.Add(MenuItem.Popup("C&hange graph to", new List<MenuItem>
                {
                    MenuItem.RadioItem("&Overall utilization", () => CpuView == 0, () => SetCpuView(0)),
                    MenuItem.RadioItem("&Logical processors", () => CpuView == 1, () => SetCpuView(1)),
                    MenuItem.RadioItem("&NUMA nodes", () => CpuView == 2, () => SetCpuView(2)),
                }));
                items.Add(MenuItem.Check("Show &kernel times", () => KernelTimes, () => { KernelTimes = !KernelTimes; SaveSettings(); _detail.Invalidate(); }));
                items.Add(MenuItem.Sep());
            }
            else if (it.Kind == PerfKind.Gpu)
            {
                items.Add(MenuItem.Popup("C&hange graph to", new List<MenuItem>
                {
                    MenuItem.RadioItem("&Single engine", () => !GpuMulti, () => { GpuMulti = false; SaveSettings(); _detail.Invalidate(); }),
                    MenuItem.RadioItem("Multiple &engines", () => GpuMulti, () => { GpuMulti = true; SaveSettings(); _detail.Invalidate(); }),
                }));
                items.Add(MenuItem.Sep());
            }
            items.Add(MenuItem.Check("Graph summary vie&w", () => _graphSummary, ToggleGraphSummary));
            var view = new List<MenuItem>();
            foreach (var x in _items)
            {
                var xx = x;
                view.Add(MenuItem.RadioItem(SideTitle(x), () => Selected == xx, () => Select(xx)));
            }
            items.Add(MenuItem.Popup("&View", view));
            if (it.Kind == PerfKind.Net) items.Add(new MenuItem("View network &details", () => NetworkDetailsDialog.ShowAsync(Main)));
            items.Add(MenuItem.Sep());
            items.Add(new MenuItem("&Copy\tCtrl+C", CopyDetails));
            Main.ShowContextMenu(items, screen);
        }

        public void ShowSideMenu(Point screen)
        {
            var items = new List<MenuItem>
            {
                MenuItem.Check("Summary vie&w", () => _sideSummary, ToggleSideSummary),
                ShowGraphs ? new MenuItem("&Hide graphs", () => { ShowGraphs = false; SaveSettings(); _side.Invalidate(); }) : new MenuItem("&Show graphs", () => { ShowGraphs = true; SaveSettings(); _side.Invalidate(); }),
                MenuItem.Sep(),
                new MenuItem("&Copy\tCtrl+C", CopyDetails),
            };
            Main.ShowContextMenu(items, screen);
        }

        void SetCpuView(int v) { CpuView = v; SaveSettings(); _detail.Invalidate(); }

        public string SideTitle(PerfItem it)
        {
            switch (it.Kind)
            {
                case PerfKind.Cpu: return "CPU";
                case PerfKind.Memory: return "Memory";
                case PerfKind.Disk: return it.Disk.Static.Title;
                case PerfKind.Net: return it.Net.Static.Kind;
                default: return it.Gpu.Static.Title;
            }
        }

        // ================================================================== copy (Ctrl+C)
        public void CopyDetails()
        {
            var it = Selected;
            if (it == null) return;
            var text = _detail.DetailsText(it);
            // automated tests never touch the user's clipboard
            if (!TestHook.Enabled) { try { Clipboard.SetText(text); } catch (Exception ex) { ErrorLog.Write("Copy", ex); } }
            LastCopied = text;
        }
        public string LastCopied;

        // ================================================================== settings
        public override void LoadSettings()
        {
            _selectedKey = Settings.Get("Performance.Selected", "cpu");
            CpuView = Settings.GetInt("Performance.CpuView", 0);
            KernelTimes = Settings.GetBool("Performance.KernelTimes", false);
            ShowGraphs = Settings.GetBool("Performance.ShowGraphs", true);
            GpuMulti = Settings.GetBool("Performance.GpuMulti", true);
            for (int i = 0; i < 4; i++) GpuEngineSel[i] = Settings.GetInt("Performance.GpuEngine" + i, -1);
        }

        public override void SaveSettings()
        {
            Settings.Set("Performance.Selected", _selectedKey);
            Settings.SetInt("Performance.CpuView", CpuView);
            Settings.SetBool("Performance.KernelTimes", KernelTimes);
            Settings.SetBool("Performance.ShowGraphs", ShowGraphs);
            Settings.SetBool("Performance.GpuMulti", GpuMulti);
            for (int i = 0; i < 4; i++) Settings.SetInt("Performance.GpuEngine" + i, GpuEngineSel[i]);
        }

        public void Test(string what)
        {
            switch (what)
            {
                case "graphsummary": ToggleGraphSummary(); break;
                case "sidesummary": ToggleSideSummary(); break;
                case "logical": SetCpuView(1); break;
                case "overall": SetCpuView(0); break;
                case "numa": SetCpuView(2); break;
                case "kernel": KernelTimes = !KernelTimes; _detail.Invalidate(); break;
                case "hidegraphs": ShowGraphs = !ShowGraphs; _side.Invalidate(); break;
                case "gpusingle": GpuMulti = !GpuMulti; _detail.Invalidate(); break;
                case "detailmenu": ShowDetailMenu(PointToScreen(new Point(Width / 2, Height / 2))); break;
                case "sidemenu": ShowSideMenu(PointToScreen(new Point(40, 40))); break;
                case "copy": CopyDetails(); break;
                default:
                    foreach (var it in _items) if (it.Key == what) Select(it);
                    break;
            }
        }
    }

    // ===================================================================== sidebar

    sealed class PerfSidebar : DarkControl
    {
        readonly PerformancePage _page;
        int _hot = -1, _scroll;
        public PerfSidebar(PerformancePage page) { _page = page; SetStyle(ControlStyles.Selectable, true); TabStop = true; }

        int EntryH => _page.ShowGraphs ? D.S(66) : D.S(52);

        Rectangle EntryRect(int i) => new Rectangle(D.S(4), D.S(6) + i * EntryH - _scroll, Width - D.S(10), EntryH - D.S(2));

        protected override void Render(Graphics g, Rectangle clip)
        {
            Draw.Fill(g, ClientRectangle, Theme.Surface);
            Draw.VLine(g, Width - 1, 0, Height, Theme.BorderSoft);
            var items = _page.Items;
            var data = _page.D2;
            var fTitle = Theme.GetFont(D, 15f, FontStyle.Regular);
            var fLine = Theme.UiFont(D);
            bool focused = Focused;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                var r = EntryRect(i);
                if (r.Bottom < 0 || r.Top > Height) continue;
                bool sel = it == _page.Selected;
                if (sel) { Draw.Fill(g, r, Theme.RowSelected); Draw.Rect(g, r, Theme.RowSelectedBorder); }
                else if (i == _hot) Draw.Fill(g, r, Theme.RowHover);
                int x = r.X + D.S(8);
                if (_page.ShowGraphs)
                {
                    var tr = new Rectangle(x, r.Y + D.S(8), D.S(64), D.S(46));
                    DrawThumb(g, tr, it, data);
                    x = tr.Right + D.S(10);
                }
                else
                {
                    var ic = new Rectangle(x, r.Y + D.S(10), D.S(12), D.S(12));
                    Draw.Fill(g, ic, it.Colors.Line);
                    x = ic.Right + D.S(10);
                }
                int w = r.Right - x - D.S(4);
                Draw.Text(g, _page.SideTitle(it), fTitle, new Rectangle(x, r.Y + D.S(6), w, D.S(22)), Theme.Text, Draw.TextLeft | Draw.Ellipsis);
                var lines = Lines(it, data);
                for (int k = 0; k < lines.Length; k++)
                    Draw.Text(g, lines[k], fLine, new Rectangle(x, r.Y + D.S(28) + k * D.S(15), w, D.S(16)), Theme.TextDim, Draw.TextLeft | Draw.Ellipsis);
            }
        }

        static void DrawThumb(Graphics g, Rectangle r, PerfItem it, DataEngine d)
        {
            int tick = d.Tick;
            switch (it.Kind)
            {
                case PerfKind.Cpu: Graph.Draw(g, r, it.Colors, 100, tick, false, new Graph.Line { Data = d.CpuTotal, Color = it.Colors.Line }); break;
                case PerfKind.Memory: Graph.Draw(g, r, it.Colors, Math.Max(1, d.Sys.MemTotal), tick, false, new Graph.Line { Data = d.MemUsed, Color = it.Colors.Line }); break;
                case PerfKind.Disk: Graph.Draw(g, r, it.Colors, 100, tick, false, new Graph.Line { Data = it.Disk.Active, Color = it.Colors.Line }); break;
                case PerfKind.Net:
                    {
                        double max = Math.Max(it.Net.Send.Max, it.Net.Recv.Max);
                        bool inactive = it.Net.Last != null && !it.Net.Last.Connected;
                        Graph.Draw(g, r, it.Colors, Graph.BitScale(max), tick, inactive,
                            new Graph.Line { Data = it.Net.Recv, Color = it.Colors.Line },
                            new Graph.Line { Data = it.Net.Send, Color = Theme.NetSend, Dashed = true });
                        break;
                    }
                case PerfKind.Gpu: Graph.Draw(g, r, it.Colors, 100, tick, false, new Graph.Line { Data = it.Gpu.Max, Color = it.Colors.Line }); break;
            }
        }

        static string[] Lines(PerfItem it, DataEngine d)
        {
            var s = d.Sys;
            switch (it.Kind)
            {
                case PerfKind.Cpu: return new[] { Fmt.Pct0(s.Cpu) + "  " + Fmt.Ghz(s.SpeedMhz) };
                case PerfKind.Memory: return new[] { Fmt.Pair(s.MemInUse, s.MemTotal) + " (" + Fmt.Pct0(s.MemTotal > 0 ? s.MemInUse * 100.0 / s.MemTotal : 0) + ")" };
                case PerfKind.Disk: return new[] { it.Disk.Static.Type, Fmt.Pct0(it.Disk.Last?.Active ?? 0) };
                case PerfKind.Net:
                    {
                        var n = it.Net;
                        if (n.Last != null && !n.Last.Connected) return new[] { n.Static.Alias, Strings.NotConnected };
                        string send = Fmt.Bits(n.Last?.SendBps ?? 0), recv = Fmt.Bits(n.Last?.RecvBps ?? 0);
                        return new[] { n.Static.Kind == "Wi-Fi" && n.Static.Ssid != null ? n.Static.Ssid : n.Static.Alias, "S: " + Short(send, recv) + " R: " + recv };
                    }
                default:
                    {
                        var g = it.Gpu;
                        string t = g.Last != null && g.Last.TemperatureC > 0 ? " (" + g.Last.TemperatureC + " °C)" : "";
                        return new[] { g.Static.Name, Fmt.Pct0(g.Last?.Max ?? 0) + t };
                    }
            }
        }

        static string Short(string send, string recv)
        {
            // the send value drops its unit when it matches the receive unit, like Task Manager ("S: 0 R: 8.0 Kbps")
            int a = send.LastIndexOf(' '), b = recv.LastIndexOf(' ');
            if (a > 0 && b > 0 && send.Substring(a) == recv.Substring(b)) return send.Substring(0, a);
            return send;
        }

        int Hit(Point p) { var items = _page.Items; for (int i = 0; i < items.Count; i++) if (EntryRect(i).Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = Hit(e.Location); if (h != _hot) { _hot = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = -1; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); Focus();
            int h = Hit(e.Location);
            if (h >= 0) _page.Select(_page.Items[h]);
        }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (e.Button == MouseButtons.Right) _page.ShowSideMenu(PointToScreen(e.Location)); }
        protected override void OnMouseDoubleClick(MouseEventArgs e) { base.OnMouseDoubleClick(e); if (e.Button == MouseButtons.Left) _page.ToggleSideSummary(); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int total = _page.Items.Count * EntryH + D.S(12);
            _scroll = Math.Max(0, Math.Min(Math.Max(0, total - Height), _scroll - Math.Sign(e.Delta) * EntryH));
            Invalidate();
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override bool IsInputKey(Keys k) => (k & Keys.KeyCode) == Keys.Up || (k & Keys.KeyCode) == Keys.Down || base.IsInputKey(k);
    }

    // ===================================================================== detail pane

    sealed class PerfDetail : DarkControl
    {
        readonly PerformancePage _page;
        readonly List<KeyValuePair<Rectangle, int>> _engineSelectors = new List<KeyValuePair<Rectangle, int>>();
        Rectangle _graphArea;
        public PerfDetail(PerformancePage page) { _page = page; }

        DataEngine Data => _page.D2;
        Font LabelFont => Theme.UiFont(D);
        Font ValueFont => Theme.GetFont(D, 20f, FontStyle.Regular);
        Font TitleFont => Theme.Light(D, 28f);
        Font NameFont => Theme.GetFont(D, 16f, FontStyle.Regular);

        protected override void Render(Graphics g, Rectangle clip)
        {
            Draw.Fill(g, ClientRectangle, Theme.Surface);
            _engineSelectors.Clear();
            var it = _page.Selected;
            if (it == null) return;
            int left = D.S(16), right = Width - D.S(24), top = D.S(8), bottom = Height - D.S(14);
            if (_page.GraphSummary)
            {
                _graphArea = new Rectangle(D.S(6), D.S(6), Width - D.S(12), Height - D.S(12));
                DrawGraphs(g, it, _graphArea, true);
                return;
            }
            // title row
            Draw.Text(g, Title(it), TitleFont, new Rectangle(left, top, right - left, D.S(40)), Theme.Text, Draw.TextLeft);
            var sub = Subtitle(it);
            if (!string.IsNullOrEmpty(sub))
            {
                int tw = Draw.Measure(Title(it), TitleFont).Width;
                Draw.Text(g, sub, NameFont, new Rectangle(left + tw + D.S(20), top + D.S(10), right - left - tw - D.S(20), D.S(30)), Theme.Text, Draw.TextRight | Draw.Ellipsis);
            }
            int y = top + D.S(46);
            int infoH = InfoHeight(it);
            var graphs = new Rectangle(left, y, right - left, Math.Max(D.S(60), bottom - infoH - D.S(12) - y));
            _graphArea = graphs;
            DrawGraphs(g, it, graphs, false);
            DrawInfo(g, it, new Rectangle(left, graphs.Bottom + D.S(12), right - left, infoH));
        }

        string Title(PerfItem it)
        {
            switch (it.Kind)
            {
                case PerfKind.Cpu: return "CPU";
                case PerfKind.Memory: return "Memory";
                case PerfKind.Disk: return it.Disk.Static.Title;
                case PerfKind.Net: return it.Net.Static.Kind;
                default: return it.Gpu.Static.Title;
            }
        }

        string Subtitle(PerfItem it)
        {
            switch (it.Kind)
            {
                case PerfKind.Cpu: return Data.Cpu.Name;
                case PerfKind.Memory: return Data.Mem.InstalledBytes > 0 ? Fmt.Size(Data.Mem.InstalledBytes) : Fmt.Size(Data.Sys.MemTotal);
                case PerfKind.Disk: return it.Disk.Static.Model;
                case PerfKind.Net: return it.Net.Static.Description;
                default: return it.Gpu.Static.Name;
            }
        }

        // ------------------------------------------------------------ graphs
        void LabelRow(Graphics g, Rectangle r, string left, string right)
        {
            var f = LabelFont;
            Draw.Text(g, left, f, r, Theme.TextDim, Draw.TextLeft | Draw.Ellipsis);
            Draw.Text(g, right, f, r, Theme.TextDim, Draw.TextRight);
        }

        void DrawGraphs(Graphics g, PerfItem it, Rectangle area, bool summary)
        {
            int lh = summary ? 0 : D.S(18);
            int tick = Data.Tick;
            var time = _page.TimeLabel;
            var c = it.Colors;
            switch (it.Kind)
            {
                case PerfKind.Cpu:
                    {
                        string title = _page.MainWindow.UpdateSpeed == 3 ? Strings.PctUtilizationPaused : _page.CpuView == 0 ? Strings.PctUtilization : "% Utilization over " + time;
                        if (!summary) LabelRow(g, new Rectangle(area.X, area.Y, area.Width, lh), title, "100%");
                        var gr = new Rectangle(area.X, area.Y + lh, area.Width, area.Height - lh - (_page.CpuView == 0 ? lh : 0));
                        if (_page.CpuView == 1) CpuGrid(g, gr, tick, c, false);
                        else if (_page.CpuView == 2) CpuGrid(g, gr, tick, c, true);
                        else
                        {
                            Graph.Draw(g, gr, c, 100, tick, false,
                                new Graph.Line { Data = Data.CpuTotal, Color = c.Line },
                                _page.KernelTimes ? new Graph.Line { Data = Data.CpuKernel, Color = Theme.Blend(c.Line, Theme.Surface, 0.35f), FillColor = Theme.KernelFill } : null);
                            if (!summary) LabelRow(g, new Rectangle(area.X, gr.Bottom, area.Width, lh), time, "0");
                        }
                        break;
                    }
                case PerfKind.Memory:
                    {
                        int barH = summary ? 0 : D.S(40), compH = summary ? 0 : lh + barH + D.S(10);
                        if (!summary) LabelRow(g, new Rectangle(area.X, area.Y, area.Width, lh), Strings.MemoryUsage, Fmt.Size(Data.Sys.MemTotal));
                        var gr = new Rectangle(area.X, area.Y + lh, area.Width, area.Height - lh * 2 - compH);
                        Graph.Draw(g, gr, c, Math.Max(1, Data.Sys.MemTotal), tick, false, new Graph.Line { Data = Data.MemUsed, Color = c.Line });
                        if (summary) break;
                        LabelRow(g, new Rectangle(area.X, gr.Bottom, area.Width, lh), time, "0");
                        int y = gr.Bottom + lh + D.S(10);
                        LabelRow(g, new Rectangle(area.X, y, area.Width, lh), Strings.MemoryComposition, "");
                        MemoryBar(g, new Rectangle(area.X, y + lh, area.Width, barH));
                        break;
                    }
                case PerfKind.Disk:
                    {
                        var d = it.Disk;
                        int h1 = (int)((area.Height - lh * 4 - D.S(10)) * 0.62), h2 = area.Height - lh * 4 - D.S(10) - h1;
                        if (summary) { Graph.Draw(g, area, c, 100, tick, false, new Graph.Line { Data = d.Active, Color = c.Line }); break; }
                        LabelRow(g, new Rectangle(area.X, area.Y, area.Width, lh), Strings.ActiveTime, "100%");
                        var g1 = new Rectangle(area.X, area.Y + lh, area.Width, h1);
                        Graph.Draw(g, g1, c, 100, tick, false, new Graph.Line { Data = d.Active, Color = c.Line });
                        LabelRow(g, new Rectangle(area.X, g1.Bottom, area.Width, lh), time, "0");
                        double scale = Graph.ByteScale(Math.Max(d.Read.Max, d.Write.Max));
                        int y2 = g1.Bottom + lh + D.S(10);
                        LabelRow(g, new Rectangle(area.X, y2, area.Width, lh), Strings.DiskTransferRate, Graph.ByteScaleText(scale));
                        var g2 = new Rectangle(area.X, y2 + lh, area.Width, h2);
                        Graph.Draw(g, g2, c, scale, tick, false,
                            new Graph.Line { Data = d.Read, Color = c.Line },
                            new Graph.Line { Data = d.Write, Color = Theme.DiskWrite, Dashed = true });
                        LabelRow(g, new Rectangle(area.X, g2.Bottom, area.Width, lh), time, "0");
                        break;
                    }
                case PerfKind.Net:
                    {
                        var n = it.Net;
                        double scale = Graph.BitScale(Math.Max(n.Send.Max, n.Recv.Max));
                        bool inactive = n.Last != null && !n.Last.Connected;
                        if (!summary) LabelRow(g, new Rectangle(area.X, area.Y, area.Width, lh), Strings.Throughput, Graph.BitScaleText(scale));
                        var gr = new Rectangle(area.X, area.Y + lh, area.Width, area.Height - lh * 2);
                        Graph.Draw(g, gr, c, scale, tick, inactive,
                            new Graph.Line { Data = n.Recv, Color = c.Line },
                            new Graph.Line { Data = n.Send, Color = Theme.NetSend, Dashed = true });
                        if (!summary) LabelRow(g, new Rectangle(area.X, gr.Bottom, area.Width, lh), time, "0");
                        break;
                    }
                case PerfKind.Gpu: GpuGraphs(g, it.Gpu, area, summary, lh, tick, time); break;
            }
        }

        void CpuGrid(Graphics g, Rectangle area, int tick, GraphColors c, bool numa)
        {
            var cpu = Data.Cpu;
            int n = numa ? cpu.NumaNodes : Data.PerCpu.Count;
            if (n <= 0) return;
            int cols = (int)Math.Ceiling(Math.Sqrt(n * (double)area.Width / Math.Max(1, area.Height)));
            cols = Math.Max(1, Math.Min(n, cols));
            int rows = (n + cols - 1) / cols;
            int gap = D.S(4);
            int cw = (area.Width - gap * (cols - 1)) / cols, ch = (area.Height - gap * (rows - 1)) / rows;
            for (int i = 0; i < n; i++)
            {
                var r = new Rectangle(area.X + (i % cols) * (cw + gap), area.Y + (i / cols) * (ch + gap), cw, ch);
                if (numa)
                {
                    var tmp = NodeSeries(i);
                    Graph.Draw(g, new Rectangle(r.X, r.Y + D.S(16), r.Width, r.Height - D.S(16)), c, 100, tick, false, new Graph.Line { Data = tmp, Color = c.Line });
                    Draw.Text(g, "NUMA node " + i, LabelFont, new Rectangle(r.X, r.Y, r.Width, D.S(16)), Theme.TextDim, Draw.TextLeft);
                }
                else
                    Graph.Draw(g, r, c, 100, tick, false,
                        new Graph.Line { Data = Data.PerCpu[i], Color = c.Line },
                        _page.KernelTimes ? new Graph.Line { Data = Data.PerCpuKernel[i], Color = Theme.Blend(c.Line, Theme.Surface, 0.35f), FillColor = Theme.KernelFill } : null);
            }
        }

        Series NodeSeries(int node)
        {
            var cpus = Data.Cpu.NodeCpus != null && node < Data.Cpu.NodeCpus.Length ? Data.Cpu.NodeCpus[node] : null;
            if (cpus == null || Data.Cpu.NumaNodes <= 1) return Data.CpuTotal;
            var s = new Series();
            int count = Data.PerCpu.Count > 0 ? Data.PerCpu[0].Count : 0;
            for (int k = 0; k < count; k++)
            {
                double sum = 0; int n = 0;
                foreach (var c in cpus) if (c < Data.PerCpu.Count) { sum += Data.PerCpu[c][k]; n++; }
                s.Push(n > 0 ? sum / n : 0);
            }
            return s;
        }

        void MemoryBar(Graphics g, Rectangle r)
        {
            var s = Data.Sys;
            long total = Math.Max(1, s.MemTotal);
            long hw = Math.Max(0, Data.Mem.InstalledBytes - s.MemTotal);
            double all = total + hw;
            g.FillRectangle(Theme.Brush(Theme.GraphBack), r);
            int x = r.X + 1, w = r.Width - 2;
            int Seg(long bytes) => (int)Math.Round(w * bytes / all);
            int xHw = x, wHw = Seg(hw);
            int wUse = Seg(s.MemInUse), wMod = Seg(s.Modified), wStb = Seg(s.Standby);
            var c = Theme.MemGraph;
            if (wHw > 0) g.FillRectangle(Theme.Brush(Theme.C(0x3A3A3A)), xHw, r.Y + 1, wHw, r.Height - 2);
            int xu = x + wHw;
            g.FillRectangle(Theme.Brush(Theme.MemInUse), xu, r.Y + 1, wUse, r.Height - 2);
            g.FillRectangle(Theme.Brush(Theme.MemModified), xu + wUse, r.Y + 1, wMod, r.Height - 2);
            g.FillRectangle(Theme.Brush(Theme.MemStandby), xu + wUse + wMod, r.Y + 1, wStb, r.Height - 2);
            // separators like Task Manager's composition bar
            foreach (var sx in new[] { xu, xu + wUse, xu + wUse + wMod, xu + wUse + wMod + wStb })
                if (sx > r.X + 1 && sx < r.Right - 1) g.FillRectangle(Theme.Brush(c.Border), sx, r.Y, 1, r.Height);
            g.DrawRectangle(Theme.Pen(c.Border), r.X, r.Y, r.Width - 1, r.Height - 1);
            _memBar = r; _memSegs = new[] { xu, xu + wUse, xu + wUse + wMod, xu + wUse + wMod + wStb, r.Right };
            _memHw = new Rectangle(xHw, r.Y, wHw, r.Height);
        }
        Rectangle _memBar, _memHw; int[] _memSegs;

        void GpuGraphs(Graphics g, GpuState gpu, Rectangle area, bool summary, int lh, int tick, string time)
        {
            var c = Theme.GpuGraph;
            var names = gpu.Static.EngineNames;
            int engines = Math.Max(1, gpu.Engines.Count);
            int memH = summary ? 0 : (int)(area.Height * 0.36);
            var top = new Rectangle(area.X, area.Y, area.Width, area.Height - memH);
            int count = _page.GpuMulti ? Math.Min(4, engines) : 1;
            int cols = count >= 2 ? 2 : 1, rows = (count + 1) / 2;
            int gap = D.S(12);
            int cw = (top.Width - gap * (cols - 1)) / cols, ch = (top.Height - gap * (rows - 1)) / rows;
            for (int i = 0; i < count; i++)
            {
                int sel = _page.EngineForSlot(i, names, engines);
                var cell = new Rectangle(top.X + (i % cols) * (cw + gap), top.Y + (i / cols) * (ch + gap), cw, ch);
                string name = sel < names.Length ? names[sel] : "Engine " + sel;
                var series = sel < gpu.Engines.Count ? gpu.Engines[sel] : null;
                if (!summary)
                {
                    var lr = new Rectangle(cell.X, cell.Y, cell.Width, lh);
                    Draw.Chevron(g, lr.X + D.S(5), lr.Y + lh / 2f, D.F(8), Theme.TextDim, 0);
                    Draw.Text(g, name, LabelFont, new Rectangle(lr.X + D.S(14), lr.Y, lr.Width - D.S(60), lh), Theme.TextDim, Draw.TextLeft | Draw.Ellipsis);
                    Draw.Text(g, Fmt.Pct0(series?.Last ?? 0), LabelFont, lr, Theme.TextDim, Draw.TextRight);
                    _engineSelectors.Add(new KeyValuePair<Rectangle, int>(new Rectangle(lr.X, lr.Y, Math.Min(lr.Width - D.S(50), D.S(14) + Draw.Measure(name, LabelFont).Width + D.S(6)), lh), i));
                }
                var gr = new Rectangle(cell.X, cell.Y + lh, cell.Width, cell.Height - lh);
                Graph.Draw(g, gr, c, 100, tick, false, series == null ? null : new Graph.Line { Data = series, Color = c.Line });
            }
            if (summary) return;
            int y = top.Bottom + D.S(10);
            int mh = (memH - D.S(10) - lh * 2 - D.S(8)) / 2;
            LabelRow(g, new Rectangle(area.X, y, area.Width, lh), Strings.DedicatedGpuMemUsage, Fmt.Size(gpu.Static.DedicatedLimit));
            var m1 = new Rectangle(area.X, y + lh, area.Width, mh);
            Graph.Draw(g, m1, c, Math.Max(1, gpu.Static.DedicatedLimit), tick, false, new Graph.Line { Data = gpu.Dedicated, Color = c.Line });
            int y2 = m1.Bottom + D.S(8);
            LabelRow(g, new Rectangle(area.X, y2, area.Width, lh), Strings.SharedGpuMemUsage, Fmt.Size(gpu.Static.SharedLimit));
            var m2 = new Rectangle(area.X, y2 + lh, area.Width, mh);
            Graph.Draw(g, m2, c, Math.Max(1, gpu.Static.SharedLimit), tick, false, new Graph.Line { Data = gpu.Shared, Color = c.Line });
        }

        // ------------------------------------------------------------ info blocks
        struct Pair { public string Label, Value; public Color Legend; public bool Dashed; }
        static Pair P(string l, string v) => new Pair { Label = l, Value = v };
        static Pair PL(string l, string v, Color legend, bool dashed) => new Pair { Label = l, Value = v, Legend = legend, Dashed = dashed };

        struct InfoLayout { public int DynRight, StaticX, StaticY, LabelW, Height; }

        /// <summary>Live values on the left; the static table beside them when it fits, otherwise below them.</summary>
        InfoLayout LayoutInfo(PerfItem it, int width)
        {
            var lf = LabelFont; var vf = ValueFont;
            var L = new InfoLayout();
            var dyn = DynamicRows(it);
            foreach (var row in dyn)
            {
                int w = 0;
                foreach (var p in row) w += ItemWidth(p, lf, vf);
                L.DynRight = Math.Max(L.DynRight, w);
            }
            var st = StaticRows(it);
            int valueW = 0;
            foreach (var p in st)
            {
                L.LabelW = Math.Max(L.LabelW, Draw.Measure(p.Label, lf).Width);
                valueW = Math.Max(valueW, p.Value != null && p.Value.StartsWith("\u0001") ? D.S(20) : Draw.Measure(p.Value, lf).Width);
            }
            int need = L.LabelW + D.S(14) + valueW + D.S(4);
            int dynH = dyn.Count * D.S(46), statH = st.Count * D.S(19);
            if (st.Count == 0) { L.Height = dynH; return L; }
            if (L.DynRight + D.S(16) + need <= width)
            {
                L.StaticX = Math.Min(Math.Max(L.DynRight + D.S(24), D.S(330)), width - need);
                L.StaticY = D.S(2);
                L.Height = Math.Max(dynH, statH + D.S(2));
            }
            else
            {
                L.StaticX = 0;
                L.StaticY = dynH + D.S(4);
                L.Height = dynH + D.S(4) + statH;
            }
            L.Height += D.S(4);
            return L;
        }

        int ItemWidth(Pair p, Font lf, Font vf) => Math.Max(D.S(82), Math.Max(Draw.Measure(p.Label, lf).Width, Draw.Measure(p.Value, vf).Width) + D.S(18));

        int InfoHeight(PerfItem it) => LayoutInfo(it, Width - D.S(40)).Height;

        List<Pair[]> DynamicRows(PerfItem it)
        {
            var s = Data.Sys;
            var rows = new List<Pair[]>();
            switch (it.Kind)
            {
                case PerfKind.Cpu:
                    rows.Add(new[] { P(Strings.Utilization, Fmt.Pct0(s.Cpu)), P(Strings.Speed, Fmt.Ghz(s.SpeedMhz)) });
                    rows.Add(new[] { P(Strings.Processes, Data.ProcessCount.ToString()), P(Strings.Threads, Data.ThreadCount.ToString()), P(Strings.Handles, Data.HandleCount.ToString()) });
                    rows.Add(new[] { P(Strings.UpTime, Fmt.Uptime(s.UptimeSeconds)) });
                    break;
                case PerfKind.Memory:
                    rows.Add(new[] { P(Strings.InUseCompressed, Fmt.Size(s.MemInUse) + " (" + Fmt.Size(s.Compressed) + ")"), P(Strings.Available, Fmt.Size(s.MemAvail)) });
                    rows.Add(new[] { P(Strings.Committed, Fmt.Pair(s.CommitTotal, s.CommitLimit)), P(Strings.Cached, Fmt.Size(s.Cached)) });
                    rows.Add(new[] { P(Strings.PagedPool, Fmt.Size(s.PagedPool)), P(Strings.NonPagedPool, Fmt.Size(s.NonPagedPool)) });
                    break;
                case PerfKind.Disk:
                    {
                        var d = it.Disk.Last;
                        rows.Add(new[] { P(Strings.ActiveTime, Fmt.Pct0(d?.Active ?? 0)), P(Strings.AvgResponse, (d?.ResponseMs ?? 0).ToString("0.0") + " ms") });
                        rows.Add(new[] { PL(Strings.ReadSpeed, Fmt.Rate(d?.ReadBps ?? 0), Theme.DiskGraph.Line, false), PL(Strings.WriteSpeed, Fmt.Rate(d?.WriteBps ?? 0), Theme.DiskWrite, true) });
                        break;
                    }
                case PerfKind.Net:
                    {
                        var n = it.Net.Last;
                        rows.Add(new[] { PL(Strings.Send, Fmt.Bits(n?.SendBps ?? 0), Theme.NetSend, true) });
                        rows.Add(new[] { PL(Strings.Receive, Fmt.Bits(n?.RecvBps ?? 0), Theme.NetGraph.Line, false) });
                        break;
                    }
                case PerfKind.Gpu:
                    {
                        var g = it.Gpu; var l = g.Last;
                        long ded = l?.Dedicated ?? 0, sh = l?.Shared ?? 0;
                        rows.Add(new[] { P(Strings.Utilization, Fmt.Pct0(l?.Max ?? 0)), P(Strings.DedicatedGpuMem, Fmt.Pair(ded, g.Static.DedicatedLimit)) });
                        rows.Add(new[] { P(Strings.GpuMemory, Fmt.Pair(ded + sh, g.Static.DedicatedLimit + g.Static.SharedLimit)), P(Strings.SharedGpuMem, Fmt.Pair(sh, g.Static.SharedLimit)) });
                        if (l != null && l.TemperatureC > 0) rows.Add(new[] { P(Strings.GpuTemperature, l.TemperatureC + " °C") });
                        break;
                    }
            }
            return rows;
        }

        List<Pair> StaticRows(PerfItem it)
        {
            var l = new List<Pair>();
            switch (it.Kind)
            {
                case PerfKind.Cpu:
                    {
                        var c = Data.Cpu;
                        l.Add(P(Strings.BaseSpeed, Fmt.Ghz(c.BaseMhz)));
                        l.Add(P(Strings.Sockets, c.Sockets.ToString()));
                        l.Add(P(Strings.Cores, c.Cores.ToString()));
                        l.Add(P(Strings.LogicalProcessors, c.Logical.ToString()));
                        l.Add(P(Strings.Virtualization, c.Virtualization));
                        l.Add(P(Strings.L1, Fmt.Cache(c.L1)));
                        l.Add(P(Strings.L2, Fmt.Cache(c.L2)));
                        l.Add(P(Strings.L3, Fmt.Cache(c.L3)));
                        break;
                    }
                case PerfKind.Memory:
                    {
                        var m = Data.Mem;
                        if (m.SpeedMhz > 0) l.Add(P(Strings.MemSpeed, m.SpeedMhz + " MHz"));
                        if (m.SlotsTotal > 0) l.Add(P(Strings.SlotsUsed, m.SlotsUsed + " of " + m.SlotsTotal));
                        if (m.FormFactor != null) l.Add(P(Strings.FormFactor, m.FormFactor));
                        long hw = m.InstalledBytes - Data.Sys.MemTotal;
                        if (m.InstalledBytes > 0 && hw > 0) l.Add(P(Strings.HwReserved, (hw / 1048576.0).ToString("0.0") + " MB"));
                        break;
                    }
                case PerfKind.Disk:
                    {
                        var s = it.Disk.Static;
                        if (!s.FormattedRequested && s.FactsLoaded && IsHandleCreated)
                            DiskSampler.RequestFormatted(s, () => { try { BeginInvoke(new Action(Invalidate)); } catch (InvalidOperationException) { } });
                        l.Add(P(Strings.Capacity, s.Capacity > 0 ? Fmt.Capacity(s.Capacity) : ""));
                        l.Add(P(Strings.Formatted, s.Formatted >= 0 ? Fmt.Capacity(s.Formatted) : ""));
                        l.Add(P(Strings.SystemDisk, s.SystemDisk ? Strings.Yes : Strings.No));
                        l.Add(P(Strings.PageFile, s.PageFile ? Strings.Yes : Strings.No));
                        l.Add(P(Strings.DiskType, s.Type));
                        break;
                    }
                case PerfKind.Net:
                    {
                        var s = it.Net.Static;
                        l.Add(P(Strings.AdapterName, s.Alias));
                        if (s.Kind == "Wi-Fi" && s.Ssid != null) l.Add(P(Strings.Ssid, s.Ssid));
                        if (!string.IsNullOrEmpty(s.DnsSuffix)) l.Add(P(Strings.DnsName, s.DnsSuffix));
                        l.Add(P(Strings.ConnectionType, s.ConnectionType));
                        if (s.Ipv4 != null) l.Add(P(Strings.Ipv4, s.Ipv4));
                        if (s.Ipv6 != null) l.Add(P(Strings.Ipv6, s.Ipv6));
                        if (s.Kind == "Wi-Fi" && s.SignalQuality >= 0) l.Add(P(Strings.SignalStrength, "\u0001" + s.SignalQuality));
                        break;
                    }
                case PerfKind.Gpu:
                    {
                        var s = it.Gpu.Static;
                        if (s.DriverVersion != null) l.Add(P(Strings.DriverVersion, s.DriverVersion));
                        if (s.DriverDate != null) l.Add(P(Strings.DriverDate, s.DriverDate));
                        if (s.DirectX != null) l.Add(P(Strings.DirectXVersion, s.DirectX));
                        if (s.Location != null) l.Add(P(Strings.PhysicalLocation, s.Location));
                        if (s.HwReserved > 0) l.Add(P(Strings.GpuHwReserved, Fmt.Size(s.HwReserved)));
                        break;
                    }
            }
            return l;
        }

        void DrawInfo(Graphics g, PerfItem it, Rectangle r)
        {
            var lf = LabelFont; var vf = ValueFont;
            var L = LayoutInfo(it, r.Width);
            int y = r.Y;
            foreach (var row in DynamicRows(it))
            {
                int x = r.X;
                foreach (var p in row)
                {
                    int lw = ItemWidth(p, lf, vf);
                    int lx = x;
                    if (!p.Legend.IsEmpty) { Graph.Legend(g, new Rectangle(x, y + D.S(2), D.S(12), D.S(14)), p.Legend, p.Dashed); lx = x + D.S(6); }
                    Draw.Text(g, p.Label, lf, new Rectangle(lx, y, lw, D.S(17)), Theme.TextDim, Draw.TextLeft);
                    Draw.Text(g, p.Value, vf, new Rectangle(lx, y + D.S(16), lw + D.S(40), D.S(28)), Theme.Text, Draw.TextLeft);
                    x += lw;
                }
                y += D.S(46);
            }
            int sx = r.X + L.StaticX, vy = r.Y + L.StaticY;
            foreach (var p in StaticRows(it))
            {
                Draw.Text(g, p.Label, lf, new Rectangle(sx, vy, L.LabelW + D.S(4), D.S(18)), Theme.TextDim, Draw.TextLeft);
                var vr = new Rectangle(sx + L.LabelW + D.S(14), vy, Math.Max(0, r.Right - sx - L.LabelW - D.S(14)), D.S(18));
                if (p.Value != null && p.Value.Length > 0 && p.Value[0] == '\u0001')
                {
                    int q = int.Parse(p.Value.Substring(1));
                    Glyphs.SignalBars(g, new RectangleF(vr.X, vr.Y + D.S(3), D.S(18), D.S(13)), q >= 80 ? 4 : q >= 60 ? 3 : q >= 40 ? 2 : q > 0 ? 1 : 0, Theme.Text, Theme.C(0x4A4A4A));
                }
                else Draw.Text(g, p.Value, lf, vr, Theme.Text, Draw.TextLeft | Draw.Ellipsis);
                vy += D.S(19);
            }
        }

        // ------------------------------------------------------------ copy text
        public string DetailsText(PerfItem it)
        {
            var sb = new StringBuilder();
            sb.Append(Title(it)).Append("\r\n\r\n");
            var sub = Subtitle(it);
            if (!string.IsNullOrEmpty(sub)) sb.Append('\t').Append(sub).Append("\r\n\r\n");
            foreach (var p in StaticRows(it)) sb.Append('\t').Append(p.Label).Append('\t').Append(p.Value != null && p.Value.StartsWith("\u0001") ? p.Value.Substring(1) : p.Value).Append("\r\n");
            sb.Append("\r\n");
            foreach (var row in DynamicRows(it)) foreach (var p in row) sb.Append('\t').Append(p.Label).Append('\t').Append(p.Value).Append("\r\n");
            return sb.ToString();
        }

        // ------------------------------------------------------------ mouse
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right) { _page.ShowDetailMenu(PointToScreen(e.Location)); return; }
            if (e.Button != MouseButtons.Left) return;
            foreach (var kv in _engineSelectors)
                if (kv.Key.Contains(e.Location)) { ShowEngineMenu(kv.Value, RectangleToScreen(kv.Key)); return; }
        }

        void ShowEngineMenu(int slot, Rectangle anchor)
        {
            var gpu = _page.Selected?.Gpu;
            if (gpu == null) return;
            var items = new List<MenuItem>();
            var names = gpu.Static.EngineNames;
            for (int i = 0; i < Math.Max(names.Length, gpu.Engines.Count); i++)
            {
                int e = i;
                items.Add(MenuItem.RadioItem(i < names.Length ? names[i] : "Engine " + i, () => _page.EngineForSlot(slot, names, gpu.Engines.Count) == e, () => { _page.GpuEngineSel[slot] = e; _page.SaveSettings(); Invalidate(); }));
            }
            new MenuPopup(D.Value, items).ShowBelow(anchor);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left && (_graphArea.Contains(e.Location) || _page.GraphSummary)) _page.ToggleGraphSummary();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            // memory composition tooltips (Taskmgr strings 33610-33617)
            if (_memSegs != null && _memBar.Contains(e.Location) && _page.Selected?.Kind == PerfKind.Memory)
            {
                var s = Data.Sys;
                string mb(long b) => (b / 1048576).ToString("#,##0");
                string tip;
                if (_memHw.Width > 0 && e.X < _memHw.Right) tip = "Hardware reserved (" + mb(Data.Mem.InstalledBytes - s.MemTotal) + " MB)\nMemory that is reserved for use by the BIOS and some drivers for other peripherals";
                else if (e.X < _memSegs[1]) tip = "In use (" + mb(s.MemInUse) + " MB)\nMemory used by processes, drivers, or the operating system\n\nIn use compressed (" + mb(s.Compressed) + " MB)";
                else if (e.X < _memSegs[2]) tip = "Modified (" + mb(s.Modified) + " MB)\nMemory whose contents must be written to disk before it can be used for another purpose";
                else if (e.X < _memSegs[3]) tip = "Standby (" + mb(s.Standby) + " MB)\nMemory that contains cached data and code that is not actively in use";
                else tip = "Free (" + mb(s.Free) + " MB)\nMemory that is not currently in use, and that will be repurposed first when processes, drivers, or the operating system need more memory";
                int nl = tip.IndexOf('\n');
                Tips.Request("mem" + tip.Substring(0, 4), tip.Substring(0, nl), tip.Substring(nl + 1), PointToScreen(new Point(e.X, _memBar.Bottom + D.S(4))), D.Value);
            }
            else Tips.Hide();
        }
    }
}
