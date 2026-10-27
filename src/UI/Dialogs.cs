using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TaskManagerDark
{
    /// <summary>Shows a dialog after the current message (context menu clicks) so callers and the test hook never block.</summary>
    public static class DialogRunner
    {
        public static void Show(MainForm main, Func<Form> create)
        {
            Action show = () =>
            {
                try
                {
                    using (var d = create())
                    {
                        if (d == null) return;
                        if (main.Visible && main.WindowState != FormWindowState.Minimized) d.ShowDialog(main); else d.ShowDialog();
                    }
                }
                catch (Exception ex) { Program.ReportError(ex, false); }
            };
            if (TestHook.InCall) TestHook.Deferred.Add(show); else main.BeginInvoke(show);
        }
    }

    // ===================================================================== Select columns (Details)

    public sealed class SelectColumnsDialog : DarkDialog
    {
        readonly TreeList _list;
        readonly CheckList _check = new CheckList();

        SelectColumnsDialog(MainForm owner, TreeList list) : base(owner, Strings.SelectColumns)
        {
            _list = list;
            SetClientSizeScaled(300, 400);
            AddLabel(Strings.SelectColumnsPrompt, 12, 10, 276, 36);
            _check.Bounds = R(12, 50, 276, 300);
            _check.Font = Font;
            foreach (var c in list.Columns)
                if (!c.Fixed) _check.Items.Add(new CheckList.Item { Text = c.Title, Checked = c.Visible, Tag = c });
            Controls.Add(_check);
            var ok = AddButton("OK", 132, 364, 75, DialogResult.OK, true);
            AddButton("Cancel", 213, 364, 75, DialogResult.Cancel);
            ok.Click += (s, e) =>
            {
                foreach (var it in _check.Items) ((ListColumn)it.Tag).Visible = it.Checked;
                Applied?.Invoke();
            };
        }

        Action Applied;

        public static void ShowAsync(MainForm main, TreeList list, Action applied) =>
            DialogRunner.Show(main, () => new SelectColumnsDialog(main, list) { Applied = applied });
    }

    // ===================================================================== Processor affinity (Details)

    public sealed class AffinityDialog : DarkDialog
    {
        readonly CheckList _check = new CheckList();
        readonly ProcInfo _p;
        readonly MainForm _main;
        readonly DarkButton _ok;
        bool _syncing;

        AffinityDialog(MainForm main, ProcInfo p, ulong mask, ulong sysMask) : base(main, Strings.AffinityTitle)
        {
            _main = main; _p = p;
            SetClientSizeScaled(300, 300);
            AddLabel(string.Format(Strings.AffinityPrompt, p.Name), 12, 8, 276, 32);
            _check.Bounds = R(12, 44, 276, 208);
            _check.Font = Font;
            _check.Items.Add(new CheckList.Item { Text = Strings.AllProcessors, Tag = -1 });
            int n = Environment.ProcessorCount;
            for (int i = 0; i < n && i < 64; i++)
                if ((sysMask & (1UL << i)) != 0) _check.Items.Add(new CheckList.Item { Text = "CPU " + i, Tag = i, Checked = (mask & (1UL << i)) != 0 });
            SyncAll();
            _check.ItemChecked += it =>
            {
                if (_syncing) return;
                if ((int)it.Tag == -1) { _syncing = true; foreach (var x in _check.Items) x.Checked = it.Checked; _syncing = false; _check.Invalidate(); }
                else SyncAll();
                _ok.Enabled = Mask() != 0;
            };
            Controls.Add(_check);
            _ok = AddButton("OK", 132, 264, 75, DialogResult.None, true);
            AddButton("Cancel", 213, 264, 75, DialogResult.Cancel);
            _ok.Click += (s, e) => Apply();
        }

        void SyncAll()
        {
            bool all = true;
            for (int i = 1; i < _check.Items.Count; i++) if (!_check.Items[i].Checked) all = false;
            _check.Items[0].Checked = all;
            _check.Invalidate();
        }

        ulong Mask()
        {
            ulong m = 0;
            foreach (var it in _check.Items) if ((int)it.Tag >= 0 && it.Checked) m |= 1UL << (int)it.Tag;
            return m;
        }

        void Apply()
        {
            IntPtr h = Sys.OpenProcess(Sys.PROCESS_SET_INFORMATION | Sys.PROCESS_QUERY_LIMITED_INFORMATION, false, _p.Pid);
            if (h == IntPtr.Zero) { int e = Marshal.GetLastWin32Error(); Actions.ShowFailure(_main, Strings.UnableAffinity, e == 5, Sys.Win32Message(e)); return; }
            try
            {
                if (!Sys.SetProcessAffinityMask(h, (UIntPtr)Mask())) { int e = Marshal.GetLastWin32Error(); Actions.ShowFailure(_main, Strings.UnableAffinity, e == 5, Sys.Win32Message(e)); return; }
            }
            finally { Sys.CloseHandle(h); }
            DialogResult = DialogResult.OK;
            Close();
        }

        public static void ShowAsync(MainForm main, ProcInfo p)
        {
            DialogRunner.Show(main, () =>
            {
                IntPtr h = Sys.OpenProcess(Sys.PROCESS_QUERY_LIMITED_INFORMATION, false, p.Pid);
                if (h == IntPtr.Zero) { int e = Marshal.GetLastWin32Error(); Actions.ShowFailure(main, Strings.UnableAffinity, e == 5, Sys.Win32Message(e)); return null; }
                try
                {
                    if (!Sys.GetProcessAffinityMask(h, out var pm, out var sm)) { int e = Marshal.GetLastWin32Error(); Actions.ShowFailure(main, Strings.UnableAffinity, e == 5, Sys.Win32Message(e)); return null; }
                    return new AffinityDialog(main, p, (ulong)pm, (ulong)sm);
                }
                finally { Sys.CloseHandle(h); }
            });
        }
    }

    // ===================================================================== Analyze wait chain (Details)

    public sealed class WaitChainDialog : DarkDialog
    {
        readonly MainForm _main;
        readonly ProcInfo _p;
        readonly TreeList _tree = new TreeList { HeaderVisible = false, RowHeightLogical = 20 };
        readonly DarkLabel _status;
        readonly DarkButton _end;

        sealed class Node : ListRow { public int Pid; public string Text; }

        WaitChainDialog(MainForm main, ProcInfo p) : base(main, Strings.WaitChainTitle)
        {
            _main = main; _p = p;
            SetClientSizeScaled(420, 330);
            _status = AddLabel("Analyzing", 12, 10, 396, 40);
            _tree.Columns.Add(new ListColumn { Id = "name", Title = "", Width = 2000, Fixed = true });
            _tree.CellOf = (r, c) => Cell.Of(((Node)r).Text);
            _tree.Bounds = R(12, 56, 396, 140);
            _tree.SetDpi(D.Value);
            _tree.SelectionChanged += () => { var n = _tree.SelectedRow as Node; _end.Enabled = n != null && n.Level > 0 && n.Pid > 4; };
            Controls.Add(_tree);
            AddLabel(Strings.WaitChainHelp, 12, 204, 396, 64, true).Flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
            var link = new DarkLink { Text = Strings.WaitChainLink, Bounds = R(12, 292, 200, 20), Font = Font };
            link.Click += (s, e) => ShellLaunch.RunUnelevated(Strings.WaitChainLinkUrl, null);
            Controls.Add(link);
            _end = AddButton("&End process", 236, 290, 90, DialogResult.None);
            _end.Enabled = false;
            _end.Click += (s, e) =>
            {
                var n = _tree.SelectedRow as Node;
                if (n == null || !_main.Data.ProcByPid.TryGetValue(n.Pid, out var victim)) return;
                Actions.EndProcess(_main, new List<ProcInfo> { victim });
                Analyze();
            };
            AddButton("Cancel", 332, 290, 76, DialogResult.Cancel);
            Analyze();
        }

        void Analyze()
        {
            var root = new Node { Key = "root", Pid = _p.Pid, Text = _p.Name, Children = new List<ListRow>() };
            var result = WaitChain.Analyze(_p.Pid, _main.Data);
            foreach (var t in result.Threads)
            {
                var tn = new Node { Key = "t" + t.ThreadId, Pid = _p.Pid, Level = 1, Text = _p.Name + " (PID: " + _p.Pid + ") Thread: " + t.ThreadId, Children = new List<ListRow>() };
                foreach (var other in t.Others)
                    tn.Children.Add(new Node { Key = "o" + t.ThreadId + "_" + other.Key, Pid = other.Key, Level = 2, Text = other.Value + " (PID: " + other.Key + ")" });
                root.Children.Add(tn);
            }
            _tree.SetRoots(new List<ListRow> { root });
            _tree.SetExpanded(root, true);
            foreach (var c in root.Children) _tree.SetExpanded(c, true);
            string name = _p.Name;
            if (result.Deadlock) _status.Text = string.Format("{0} is in deadlock. You can end the process to resolve the deadlock immediately, or you can wait to see if the issue resolves itself.", name);
            else if (result.WaitingFor != null) _status.Text = string.Format("{0} is waiting for another process ({1}).", name, result.WaitingFor);
            else if (result.Hung) _status.Text = string.Format("{0} is not responding.", name);
            else if (result.Threads.Count > 0) _status.Text = string.Format("One or more threads of {0} are in waiting status.", name);
            else _status.Text = string.Format("{0} is running normally.", name);
        }

        public static void ShowAsync(MainForm main, ProcInfo p) => DialogRunner.Show(main, () => new WaitChainDialog(main, p));
    }

    /// <summary>Wait Chain Traversal over every thread of a process.</summary>
    public static class WaitChain
    {
        public sealed class ThreadChain { public int ThreadId; public List<KeyValuePair<int, string>> Others = new List<KeyValuePair<int, string>>(); }
        public sealed class Result { public List<ThreadChain> Threads = new List<ThreadChain>(); public bool Deadlock, Hung; public string WaitingFor; }

        public static unsafe Result Analyze(int pid, DataEngine d)
        {
            var res = new Result();
            if (d.Windows.TryGetValue(pid, out var wl)) foreach (var w in wl) if (w.Hung) res.Hung = true;
            System.Diagnostics.ProcessThreadCollection threads;
            try { threads = System.Diagnostics.Process.GetProcessById(pid).Threads; }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception) { return res; }   // exited or not accessible
            IntPtr session = Sys.OpenThreadWaitChainSession(0, IntPtr.Zero);
            if (session == IntPtr.Zero) return res;
            const int NodeSize = 280, MaxNodes = 16;
            IntPtr nodes = Marshal.AllocHGlobal(NodeSize * MaxNodes);
            try
            {
                foreach (System.Diagnostics.ProcessThread t in threads)
                {
                    int count = MaxNodes;
                    if (!Sys.GetThreadWaitChain(session, IntPtr.Zero, 1 | 2 | 4 /*out-of-proc, COM, critical sections*/, t.Id, ref count, nodes, out bool cycle)) continue;
                    if (cycle) res.Deadlock = true;
                    var tc = new ThreadChain { ThreadId = t.Id };
                    for (int i = 0; i < count; i++)
                    {
                        byte* n = (byte*)nodes + i * NodeSize;
                        int type = *(int*)n;
                        if (type != 8 /*WctThreadType*/) continue;
                        int opid = *(int*)(n + 8);
                        if (opid == pid || opid == 0) continue;
                        string name = d.ProcByPid.TryGetValue(opid, out var op) ? op.Name : "PID " + opid;
                        if (!tc.Others.Exists(x => x.Key == opid)) tc.Others.Add(new KeyValuePair<int, string>(opid, name));
                        if (res.WaitingFor == null) res.WaitingFor = name;
                    }
                    if (tc.Others.Count > 0 || cycle) res.Threads.Add(tc);
                }
            }
            finally { Marshal.FreeHGlobal(nodes); Sys.CloseThreadWaitChainSession(session); }
            return res;
        }
    }
}
