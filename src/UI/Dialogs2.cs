using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TaskManagerDark
{
    // ===================================================================== File > Run new task ("Create new task")

    public sealed class RunDialog : DarkDialog
    {
        readonly MainForm _main;
        readonly DarkEditCombo _open = new DarkEditCombo();
        readonly DarkCheck _admin = new DarkCheck();

        RunDialog(MainForm main) : base(main, Strings.CreateNewTask)
        {
            _main = main;
            SetClientSizeScaled(412, 196);
            Controls.Add(new GlyphBox { Bounds = R(14, 18, 34, 34), Paint = (g, r) => DrawRunIcon(g, r) });
            AddLabel(Strings.RunPrompt, 62, 16, 336, 40).Flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
            var lbl = AddLabel(Strings.RunOpen, 12, 70, 48, 24);
            _open.Bounds = R(62, 68, 336, 25); _open.Font = Font;
            lbl.MnemonicTarget = _open;
            Controls.Add(_open);
            _admin.Text = Strings.RunAdmin; _admin.Bounds = R(62, 104, 336, 22); _admin.Font = Font;
            Controls.Add(_admin);
            var ok = AddButton("OK", 152, 156, 78, DialogResult.None, true);
            AddButton("Cancel", 236, 156, 78, DialogResult.Cancel);
            var browse = AddButton(Strings.Browse, 320, 156, 78, DialogResult.None);
            ok.Click += (s, e) => Run();
            browse.Click += (s, e) => Browse();
            _open.TextChanged += (s, e) => ok.Enabled = _open.Text.Trim().Length > 0;
            foreach (var m in Mru()) _open.Items.Add(m);
            if (_open.Items.Count > 0) _open.Text = _open.Items[0];
            ok.Enabled = _open.Text.Trim().Length > 0;
            ActiveControl = _open;
        }

        static void DrawRunIcon(Graphics g, Rectangle r)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var w = new RectangleF(r.X + 2, r.Y + 4, r.Width - 4, r.Height - 8);
            using (var p = Draw.RoundRect(w, 3)) { g.FillPath(Theme.Brush(Theme.C(0x23394F)), p); g.DrawPath(Theme.Pen(Theme.C(0x5A8FC8), 1.2f), p); }
            g.FillRectangle(Theme.Brush(Theme.C(0x3C6EA8)), w.X + 1, w.Y + 1, w.Width - 2, Math.Max(2, w.Height / 5));
            Draw.Chevron(g, w.X + w.Width * 0.38f, w.Y + w.Height * 0.62f, w.Height * 0.45f, Theme.C(0x8CC4F5), 2, 2f);
            g.DrawLine(Theme.Pen(Theme.C(0x8CC4F5), 2f), w.X + w.Width * 0.52f, w.Y + w.Height * 0.78f, w.X + w.Width * 0.78f, w.Y + w.Height * 0.78f);
        }

        static List<string> Mru()
        {
            var l = new List<string>();
            for (int i = 0; i < 26; i++) { var v = Settings.Get("Run.MRU" + i); if (!string.IsNullOrEmpty(v)) l.Add(v); }
            // the Win+R history (read only)
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU"))
                {
                    var order = k?.GetValue("MRUList") as string;
                    if (order != null)
                        foreach (var ch in order)
                        {
                            var v = k.GetValue(ch.ToString()) as string;
                            if (string.IsNullOrEmpty(v)) continue;
                            if (v.EndsWith("\\1")) v = v.Substring(0, v.Length - 2);
                            if (!l.Exists(x => x.Equals(v, StringComparison.OrdinalIgnoreCase))) l.Add(v);
                        }
                }
            }
            catch (Exception ex) { ErrorLog.Write("RunMRU", ex); }
            return l;
        }

        static void Remember(string cmd)
        {
            var l = new List<string> { cmd };
            for (int i = 0; i < 26; i++) { var v = Settings.Get("Run.MRU" + i); if (!string.IsNullOrEmpty(v) && !v.Equals(cmd, StringComparison.OrdinalIgnoreCase)) l.Add(v); }
            for (int i = 0; i < 26; i++) Settings.Set("Run.MRU" + i, i < l.Count ? l[i] : null);
            Settings.Save();
        }

        void Browse()
        {
            using (var d = new OpenFileDialog { Title = "Browse", Filter = "Programs (*.exe;*.pif;*.com;*.bat;*.cmd)|*.exe;*.pif;*.com;*.bat;*.cmd|All Files (*.*)|*.*" })
                if (d.ShowDialog(this) == DialogResult.OK) _open.Text = d.FileName.Contains(" ") ? "\"" + d.FileName + "\"" : d.FileName;
        }

        void Run()
        {
            string cmd = _open.Text.Trim();
            if (cmd.Length == 0) return;
            SplitCommand(cmd, out var file, out var args);
            bool admin = _admin.Checked;
            try
            {
                if (!Sys.IsElevated && admin)
                {
                    var psi = new System.Diagnostics.ProcessStartInfo(file, args) { UseShellExecute = true, Verb = "runas" };
                    System.Diagnostics.Process.Start(psi);
                }
                else if (Sys.IsElevated && !admin) ShellLaunch.RunUnelevated(file, args);
                else System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file, args) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { return; }   // UAC: No
            catch (Exception ex)
            {
                MessageDialog.Show(this, Strings.CreateNewTask, cmd, Strings.CommandFailed + "\n\n" + ex.Message, new[] { "OK" }, 0, MessageDialog.Kind.Error, null, out _);
                return;
            }
            Remember(cmd);
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>"notepad c:\x.txt" -> notepad + c:\x.txt; quoted paths and existing paths with spaces are kept whole.</summary>
        public static void SplitCommand(string cmd, out string file, out string args)
        {
            cmd = cmd.Trim();
            if (cmd.StartsWith("\"")) { Actions.SplitCommand(cmd, out file, out args); return; }
            string expanded = Environment.ExpandEnvironmentVariables(cmd);
            if (File.Exists(expanded) || Directory.Exists(expanded) || cmd.Contains("://")) { file = cmd; args = ""; return; }
            Actions.SplitCommand(cmd, out file, out args);
        }

        public static void ShowAsync(MainForm main) => DialogRunner.Show(main, () => new RunDialog(main));
    }

    /// <summary>A static control that draws a glyph (dialog icons).</summary>
    public sealed class GlyphBox : Control
    {
        public Action<Graphics, Rectangle> Paint;
        public GlyphBox() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); TabStop = false; }
        protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(Parent?.BackColor ?? Theme.Dialog); Paint?.Invoke(e.Graphics, ClientRectangle); }
    }

    // ===================================================================== Create dump file

    public static class DumpDialog
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate bool DumpCallback(IntPtr param, IntPtr input, IntPtr output);

        sealed class Progress : DarkDialog
        {
            public volatile bool Cancelled;
            public Progress(MainForm main) : base(main, Strings.DumpTitle)
            {
                SetClientSizeScaled(360, 96);
                AddLabel(Strings.DumpWait, 12, 14, 336, 22);
                var c = AddButton("Cancel", 270, 58, 78, DialogResult.None);
                c.Click += (s, e) => { Cancelled = true; c.Enabled = false; };
                ControlBox = false;
            }
        }

        sealed class Done : DarkDialog
        {
            public Done(MainForm main, string path) : base(main, Strings.DumpTitle)
            {
                SetClientSizeScaled(380, 176);
                AddLabel(Strings.DumpDone, 12, 12, 356, 56).Flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
                var box = new DarkTextBox(true) { Bounds = R(12, 72, 356, 48), Font = Font, Text = path };
                box.ReadOnly = true;
                Controls.Add(box);
                AddButton("OK", 136, 136, 78, DialogResult.OK, true);
                var open = AddButton("Open file location", 220, 136, 148, DialogResult.OK);
                open.Click += (s, e) => Actions.OpenFileLocation(main, path);
            }
        }

        public static void Run(MainForm main, ProcInfo p)
        {
            Action run = () =>
            {
                string dir = Path.GetTempPath();
                string baseName = Path.GetFileNameWithoutExtension(p.Name);
                string path = Path.Combine(dir, baseName + ".DMP");
                for (int i = 2; File.Exists(path); i++) path = Path.Combine(dir, baseName + " (" + i + ").DMP");
                var prog = new Progress(main);
                string error = null;
                bool ok = false;
                var t = new Thread(() =>
                {
                    IntPtr h = Sys.OpenProcess(Sys.PROCESS_QUERY_INFORMATION | Sys.PROCESS_VM_READ | Sys.PROCESS_DUP_HANDLE, false, p.Pid);
                    if (h == IntPtr.Zero) { error = Win32(Marshal.GetLastWin32Error()); return; }
                    try
                    {
                        using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite))
                        {
                            DumpCallback cb = (param, input, output) =>
                            {
                                if (Marshal.ReadInt32(input, 16) == 6 /*CancelCallback*/) { Marshal.WriteInt32(output, 0, 1); Marshal.WriteInt32(output, 4, prog.Cancelled ? 1 : 0); }
                                return true;
                            };
                            IntPtr info = Marshal.AllocHGlobal(IntPtr.Size * 2);
                            try
                            {
                                Marshal.WriteIntPtr(info, 0, Marshal.GetFunctionPointerForDelegate(cb));
                                Marshal.WriteIntPtr(info, IntPtr.Size, IntPtr.Zero);
                                ok = Sys.MiniDumpWriteDump(h, p.Pid, fs.SafeFileHandle.DangerousGetHandle(), 0x1826, IntPtr.Zero, IntPtr.Zero, info);
                                if (!ok) error = prog.Cancelled ? null : Win32(Marshal.GetLastWin32Error());
                            }
                            finally { Marshal.FreeHGlobal(info); GC.KeepAlive(cb); }
                        }
                    }
                    catch (Exception ex) { error = ex.Message; }
                    finally { Sys.CloseHandle(h); }
                    if (!ok) { try { File.Delete(path); } catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { ErrorLog.Write("Dump cleanup", ex); } }
                }) { IsBackground = true, Name = "Dump" };
                t.Start();
                prog.Shown += (s, e) => { var timer = new System.Windows.Forms.Timer { Interval = 100 }; timer.Tick += (s2, e2) => { if (!t.IsAlive) { timer.Stop(); prog.Close(); } }; timer.Start(); };
                prog.ShowDialog(main);
                prog.Dispose();
                t.Join();
                if (ok) using (var d = new Done(main, path)) d.ShowDialog(main);
                else if (!prog.Cancelled) MessageDialog.Show(main, Strings.AppTitle, Strings.OperationFailed, error ?? "", new[] { "OK" }, 0, MessageDialog.Kind.Error, null, out _);
            };
            if (TestHook.InCall) TestHook.Deferred.Add(run); else main.BeginInvoke(run);
        }

        static string Win32(int e) => e == 5 && !Sys.IsElevated ? Strings.NeedAdmin : Sys.Win32Message(e);
    }

    // ===================================================================== Properties (dark property sheet)

    public sealed class PropertiesDialog : DarkDialog
    {
        readonly string _path;
        readonly TabStrip _tabs = new TabStrip();
        readonly Panel[] _pages;

        PropertiesDialog(MainForm main, string path) : base(main, Path.GetFileName(path) + " Properties")
        {
            _path = path;
            SetClientSizeScaled(390, 470);
            var fi = new FileInfo(path);
            var ver = StaticLoader.Version(path);
            string signer = Signer(path);
            _tabs.Tabs.Add("General");
            if (signer != null) _tabs.Tabs.Add("Digital Signatures");
            _tabs.Tabs.Add("Details");
            _tabs.Bounds = R(8, 8, 374, 26);
            _tabs.SetDpi(D.Value);
            Controls.Add(_tabs);
            _pages = new Panel[_tabs.Tabs.Count];
            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i] = new Panel { Bounds = R(8, 34, 374, 386), BackColor = Theme.Surface, Visible = i == 0 };
                Controls.Add(_pages[i]);
            }
            _tabs.SelectedChanged += i => { for (int k = 0; k < _pages.Length; k++) _pages[k].Visible = k == i; };

            // General
            var gp = _pages[0];
            int y = 14;
            var icon = IconCache.ForFile(path, D.S(32));
            gp.Controls.Add(new GlyphBox { Bounds = R(14, y, 34, 34), BackColor = Theme.Surface, Paint = (g, r) => { IntPtr hdc = g.GetHdc(); Sys.DrawIconEx(hdc, r.X + 1, r.Y + 1, icon, D.S(32), D.S(32), 0, IntPtr.Zero, 3); g.ReleaseHdc(hdc); } });
            var name = new DarkTextBox { Bounds = R(80, y + 4, 280, 24), Font = Font, Text = fi.Name }; name.ReadOnly = true; gp.Controls.Add(name);
            y += 48; Sep(gp, y); y += 10;
            Row(gp, ref y, "Type of file:", "Application (" + fi.Extension.ToLowerInvariant() + ")");
            Row(gp, ref y, "Description:", ver?.FileDescription ?? "");
            Sep(gp, y); y += 10;
            Row(gp, ref y, "Location:", fi.DirectoryName);
            Row(gp, ref y, "Size:", SizeText(fi.Length));
            Row(gp, ref y, "Size on disk:", SizeText(SizeOnDisk(path, fi.Length)));
            Sep(gp, y); y += 10;
            Row(gp, ref y, "Created:", fi.CreationTime.ToString("D") + ", " + fi.CreationTime.ToString("T"));
            Row(gp, ref y, "Modified:", fi.LastWriteTime.ToString("D") + ", " + fi.LastWriteTime.ToString("T"));
            Row(gp, ref y, "Accessed:", fi.LastAccessTime.ToString("D") + ", " + fi.LastAccessTime.ToString("T"));
            Sep(gp, y); y += 10;
            var attrs = new List<string>();
            if ((fi.Attributes & FileAttributes.ReadOnly) != 0) attrs.Add("Read-only");
            if ((fi.Attributes & FileAttributes.Hidden) != 0) attrs.Add("Hidden");
            if ((fi.Attributes & FileAttributes.Archive) != 0) attrs.Add("Archive");
            if ((fi.Attributes & FileAttributes.System) != 0) attrs.Add("System");
            Row(gp, ref y, "Attributes:", attrs.Count == 0 ? "Normal" : string.Join(", ", attrs));

            int page = 1;
            if (signer != null)
            {
                var sp = _pages[page++];
                int sy = 14;
                Row(sp, ref sy, "Name of signer:", signer);
                Row(sp, ref sy, "Signature:", "Embedded Authenticode signature");
            }
            // Details
            var dp = _pages[page];
            int dy = 14;
            Header(dp, ref dy, "Description");
            Row(dp, ref dy, "File description", ver?.FileDescription ?? "");
            Row(dp, ref dy, "Type", "Application");
            Row(dp, ref dy, "File version", ver?.FileVersion ?? "");
            Row(dp, ref dy, "Product name", ver?.ProductName ?? "");
            Row(dp, ref dy, "Product version", ver?.ProductVersion ?? "");
            Row(dp, ref dy, "Copyright", ver?.LegalCopyright ?? "");
            Row(dp, ref dy, "Size", SizeText(fi.Length, false));
            Row(dp, ref dy, "Date modified", fi.LastWriteTime.ToString("g"));
            Row(dp, ref dy, "Language", ver?.Language ?? "");
            Row(dp, ref dy, "Original filename", ver?.OriginalFilename ?? "");

            AddButton("OK", 222, 432, 78, DialogResult.OK, true);
            AddButton("Cancel", 304, 432, 78, DialogResult.Cancel);
        }

        void Sep(Control p, int y) => p.Controls.Add(new Panel { Bounds = R(12, y, 350, 1), BackColor = Theme.Border });
        void Header(Control p, ref int y, string text) { var l = new DarkLabel { Text = text, Font = Theme.UiFontBold(D), Bounds = R(12, y, 340, 20) }; p.Controls.Add(l); y += 24; }
        void Row(Control p, ref int y, string label, string value)
        {
            p.Controls.Add(new DarkLabel { Text = label, Font = Font, Bounds = R(12, y, 110, 20), Dim = true, Flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix });
            var v = new DarkLabel { Text = value ?? "", Font = Font, Bounds = R(124, y, 236, 36), Flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis };
            int h = TextRenderer.MeasureText(value ?? "", Font, new Size(D.S(236), 1000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            v.Height = Math.Max(D.S(20), h + D.S(2));
            p.Controls.Add(v);
            y += Math.Max(24, (int)(v.Height / D.Scale) + 6);
        }

        static string SizeText(long bytes, bool withBytes = true)
        {
            string s = bytes >= 1L << 30 ? (bytes / (double)(1L << 30)).ToString("0.00") + " GB" : bytes >= 1L << 20 ? (bytes / 1048576.0).ToString("0.00") + " MB" : (bytes / 1024.0).ToString("0") + " KB";
            return withBytes ? s + " (" + bytes.ToString("#,##0") + " bytes)" : s;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetCompressedFileSizeW")] static extern uint GetCompressedFileSize(string file, out uint high);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetDiskFreeSpaceW")] static extern bool GetDiskFreeSpace(string root, out uint spc, out uint bps, out uint free, out uint total);

        static long SizeOnDisk(string path, long size)
        {
            try
            {
                uint lo = GetCompressedFileSize(path, out uint hi);
                long on = ((long)hi << 32) | lo;
                if (GetDiskFreeSpace(Path.GetPathRoot(path), out uint spc, out uint bps, out _, out _))
                {
                    long cluster = (long)spc * bps;
                    if (cluster > 0) on = (on + cluster - 1) / cluster * cluster;
                }
                return on;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) { return size; }
        }

        static string Signer(string path)
        {
            try
            {
                var cert = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path);
                var subject = cert.Subject;
                foreach (var part in subject.Split(','))
                {
                    var t = part.Trim();
                    if (t.StartsWith("CN=")) return t.Substring(3).Trim('"');
                }
                return subject;
            }
            catch (System.Security.Cryptography.CryptographicException) { return null; }   // not signed
        }

        public static void ShowAsync(MainForm main, string path) => DialogRunner.Show(main, () => new PropertiesDialog(main, path));
    }

    // ===================================================================== View network details

    public sealed class NetworkDetailsDialog : DarkDialog
    {
        static NetworkDetailsDialog _open;
        readonly MainForm _main;
        readonly TreeList _list = new TreeList { RowHeightLogical = 20, ShowExpandos = false };
        readonly Dictionary<long, NetSample> _prev = new Dictionary<long, NetSample>();

        NetworkDetailsDialog(MainForm main) : base(main, "Network Details")
        {
            _main = main;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true; MaximizeBox = true;
            SetClientSizeScaled(640, 260);
            string[] cols = { "Adapter name", "Network utilization", "Link speed", "State", "Bytes sent throughput", "Bytes received throughput", "Bytes throughput",
                              "Bytes sent", "Bytes received", "Bytes", "Bytes sent per interval", "Bytes received per interval", "Bytes per interval",
                              "Unicasts sent", "Unicasts received", "Unicasts", "Unicasts sent per interval", "Unicasts received per interval", "Unicasts per interval",
                              "Nonunicasts sent", "Nonunicasts received", "Nonunicasts", "Nonunicasts sent per interval", "Nonunicasts received per interval", "Nonunicasts per interval" };
            for (int i = 0; i < cols.Length; i++) _list.Columns.Add(new ListColumn { Id = "c" + i, Title = cols[i], Width = i == 0 ? 160 : 120, Right = i > 3, Index = i, Fixed = i == 0 });
            _list.CellOf = CellOf;
            _list.SetDpi(D.Value);
            Controls.Add(_list);
            _main.Data.Updated += Refill;
            Refill();
        }

        protected override void OnLayout(LayoutEventArgs levent) { base.OnLayout(levent); _list.Bounds = ClientRectangle; }

        void Refill()
        {
            var rows = new List<ListRow>();
            foreach (var n in _main.Data.Nets) rows.Add(new ListRow { Key = n.Static.Luid.ToString(), Tag = n });
            _list.SetRoots(rows);
        }

        Cell CellOf(ListRow r, ListColumn c)
        {
            var n = (NetState)r.Tag;
            var s = n.Last; var f = n.First;
            if (s == null) return Cell.Of("");
            int i = c.Index;
            double interval = Math.Max(0.5, _main.Data.Interval / 1000.0);
            long bs = s.BytesSent - f.BytesSent, br = s.BytesRecv - f.BytesRecv;
            switch (i)
            {
                case 0: return Cell.Of(n.Static.Alias);
                case 1: return Cell.Of(s.LinkBps > 0 ? (Math.Max(s.SendBps, s.RecvBps) * 100.0 / s.LinkBps).ToString("0.00") + " %" : "0 %");
                case 2: return Cell.Of(Graph.BitScaleText(s.LinkBps));
                case 3: return Cell.Of(s.Connected ? "Connected" : "Disconnected");
                case 4: return Cell.Of((s.SendBps * 100.0 / Math.Max(1, s.LinkBps)).ToString("0.00") + " %");
                case 5: return Cell.Of((s.RecvBps * 100.0 / Math.Max(1, s.LinkBps)).ToString("0.00") + " %");
                case 6: return Cell.Of(((s.SendBps + s.RecvBps) * 100.0 / Math.Max(1, s.LinkBps)).ToString("0.00") + " %");
                case 7: return Cell.Of(Fmt.Num(bs));
                case 8: return Cell.Of(Fmt.Num(br));
                case 9: return Cell.Of(Fmt.Num(bs + br));
                case 10: return Cell.Of(Fmt.Num((long)(s.SendBps / 8 * interval)));
                case 11: return Cell.Of(Fmt.Num((long)(s.RecvBps / 8 * interval)));
                case 12: return Cell.Of(Fmt.Num((long)((s.SendBps + s.RecvBps) / 8 * interval)));
                case 13: return Cell.Of(Fmt.Num(s.UcastSent - f.UcastSent));
                case 14: return Cell.Of(Fmt.Num(s.UcastRecv - f.UcastRecv));
                case 15: return Cell.Of(Fmt.Num(s.UcastSent - f.UcastSent + s.UcastRecv - f.UcastRecv));
                case 19: return Cell.Of(Fmt.Num(s.NUcastSent - f.NUcastSent));
                case 20: return Cell.Of(Fmt.Num(s.NUcastRecv - f.NUcastRecv));
                case 21: return Cell.Of(Fmt.Num(s.NUcastSent - f.NUcastSent + s.NUcastRecv - f.NUcastRecv));
            }
            return Cell.Of("0");
        }

        protected override void OnFormClosed(FormClosedEventArgs e) { _main.Data.Updated -= Refill; _open = null; base.OnFormClosed(e); }

        public static void ShowAsync(MainForm main)
        {
            if (_open != null && !_open.IsDisposed) { _open.Activate(); return; }
            _open = new NetworkDetailsDialog(main);
            _open.Show(main);
        }
    }
}
