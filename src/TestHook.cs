using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TaskManagerDark
{
    /// <summary>
    /// Automation hook used for testing (only active with --test-hook). Commands arrive through WM_COPYDATA and are
    /// replayed straight into the app's own controls, so the real mouse, keyboard and window focus are never touched.
    /// Results are written to the file named by an "out:" line (the test driver's scratch folder).
    /// </summary>
    public static class TestHook
    {
        public static bool Enabled;
        public static bool Ctrl, Shift, Alt;
        public static string LastError;
        /// <summary>True while a hook script runs; dialogs opened meanwhile are deferred until the hook has replied.</summary>
        public static bool InCall;
        public static int Activations;
        public static readonly System.Collections.Generic.List<Action> Deferred = new System.Collections.Generic.List<Action>();
        static readonly Stopwatch _sw = Stopwatch.StartNew();
        static string _out = Path.Combine(Path.GetTempPath(), "taskmanagerdark_hook.txt");

        [StructLayout(LayoutKind.Sequential)]
        struct COPYDATASTRUCT { public IntPtr dwData; public int cbData; public IntPtr lpData; }

        public static bool TryHandle(MainForm f, ref Message m)
        {
            if (!Enabled || m.Msg != 0x004A) return false;
            var cds = (COPYDATASTRUCT)Marshal.PtrToStructure(m.LParam, typeof(COPYDATASTRUCT));
            string text = Marshal.PtrToStringUni(cds.lpData, cds.cbData / 2).TrimEnd('\0');
            string result;
            InCall = true;
            try { result = Run(f, text); }
            catch (Exception ex) { result = "ERROR " + ex; }
            finally { InCall = false; }
            try { File.WriteAllText(_out, result ?? "OK", Encoding.UTF8); } catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { ErrorLog.Write("TestHook output", ex); }
            // modal dialogs requested during the script open only after this synchronous call has returned
            foreach (var a in Deferred) f.BeginInvoke(a);
            Deferred.Clear();
            m.Result = (IntPtr)1;
            return true;
        }

        static Control FindTarget(Control root, Point p, out Point local)
        {
            Control c = root;
            local = p;
            while (true)
            {
                Control child = null;
                for (int i = 0; i < c.Controls.Count; i++)
                {
                    var k = c.Controls[i];
                    if (k.Visible && k.Bounds.Contains(local)) { child = k; break; }
                }
                if (child == null) return c;
                local = new Point(local.X - child.Left, local.Y - child.Top);
                c = child;
            }
        }

        static Form TopModal(MainForm f)
        {
            Form r = null;
            foreach (Form of in Application.OpenForms) if (of.Visible && of != f && !(of is PopupWindow) && of.GetType().Name != "DropForm" && (of.Modal || of is DarkDialog)) r = of;
            return r;
        }

        static void Send(Control c, int msg, Point p, int keys) => Native.SendMessage(c.Handle, msg, (IntPtr)keys, Native.MakeLParam(p.X, p.Y));
        static int Mods() => (Ctrl ? Native.MK_CONTROL : 0) | (Shift ? Native.MK_SHIFT : 0);

        static void Mouse(Control root, string kind, int x, int y, string btn)
        {
            var c = FindTarget(root, new Point(x, y), out var lp);
            int down = btn == "r" ? Native.WM_RBUTTONDOWN : Native.WM_LBUTTONDOWN;
            int up = btn == "r" ? Native.WM_RBUTTONUP : Native.WM_LBUTTONUP;
            int mk = btn == "r" ? Native.MK_RBUTTON : Native.MK_LBUTTON;
            switch (kind)
            {
                case "move": Send(c, Native.WM_MOUSEMOVE, lp, Mods()); break;
                case "down": Send(c, Native.WM_MOUSEMOVE, lp, Mods()); Send(c, down, lp, mk | Mods()); break;
                case "up": Send(c, up, lp, Mods()); break;
                case "click": Send(c, Native.WM_MOUSEMOVE, lp, Mods()); Send(c, down, lp, mk | Mods()); Send(c, up, lp, Mods()); break;
                case "dbl":
                    Send(c, Native.WM_MOUSEMOVE, lp, Mods()); Send(c, down, lp, mk | Mods()); Send(c, up, lp, Mods());
                    Send(c, btn == "r" ? Native.WM_RBUTTONDBLCLK : Native.WM_LBUTTONDBLCLK, lp, mk | Mods()); Send(c, up, lp, Mods()); break;
            }
            Application.DoEvents();
        }

        public static string Run(MainForm f, string script)
        {
            var sb = new StringBuilder();
            foreach (var raw in script.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                int k = line.IndexOf(':');
                string cmd = k < 0 ? line : line.Substring(0, k), arg = k < 0 ? "" : line.Substring(k + 1);
                var a = arg.Split(',');
                switch (cmd)
                {
                    case "out": _out = arg; break;
                    case "mods": Ctrl = arg.Contains("c"); Shift = arg.Contains("s"); Alt = arg.Contains("a"); break;
                    case "move": case "down": case "up": case "click": case "dbl":
                        Mouse(f, cmd, int.Parse(a[0]), int.Parse(a[1]), a.Length > 2 ? a[2] : "l"); break;
                    case "dclick": case "dmove":   // inside the top modal dialog (client coordinates)
                        { var d = TopModal(f); if (d != null) Mouse(d, cmd.Substring(1), int.Parse(a[0]), int.Parse(a[1]), a.Length > 2 ? a[2] : "l"); break; }
                    case "pclick": case "pmove":   // inside the top popup (client coordinates)
                        { var p = Popups.Top; if (p != null) Mouse(p, cmd.Substring(1), int.Parse(a[0]), int.Parse(a[1]), "l"); break; }
                    case "key":
                        {
                            Keys keys = 0;
                            foreach (var part in arg.Split('+'))
                            {
                                var p = part.Trim().ToLowerInvariant();
                                if (p == "ctrl") keys |= Keys.Control; else if (p == "shift") keys |= Keys.Shift; else if (p == "alt") keys |= Keys.Alt;
                                else keys |= (Keys)Enum.Parse(typeof(Keys), part.Trim(), true);
                            }
                            Ctrl = (keys & Keys.Control) != 0; Shift = (keys & Keys.Shift) != 0; Alt = (keys & Keys.Alt) != 0;
                            try
                            {
                                var d = TopModal(f);
                                if (d != null) { var t = d.ActiveControl ?? d; Native.SendMessage(t.Handle, Native.WM_KEYDOWN, (IntPtr)(int)(keys & Keys.KeyCode), IntPtr.Zero); Native.SendMessage(t.Handle, Native.WM_KEYUP, (IntPtr)(int)(keys & Keys.KeyCode), (IntPtr)unchecked((int)0xC0000001)); }
                                else f.TestKey(keys);
                            }
                            finally { Ctrl = Shift = Alt = false; }
                            Application.DoEvents();
                            break;
                        }
                    case "type":
                        {
                            var d = TopModal(f);
                            Control target = d != null ? (d.ActiveControl ?? d) : (Popups.Top == null ? (f.ActiveControl ?? f) : null);
                            if (target is DarkTextBox tb) target = tb.Box;
                            foreach (var ch in arg.Replace("\\n", "\r"))
                            {
                                if (target != null) Native.SendMessage(target.Handle, Native.WM_CHAR, (IntPtr)ch, IntPtr.Zero);
                                else Popups.Top.HandleChar(ch);
                            }
                            Application.DoEvents();
                            break;
                        }
                    case "tab": f.ShowPage(int.Parse(arg)); Application.DoEvents(); break;
                    case "fewer": f.SetFewerDetails(arg == "1"); Application.DoEvents(); break;
                    case "speed": f.SetUpdateSpeed(int.Parse(arg)); break;
                    case "refresh": f.Data.RefreshNow(); break;
                    case "menu": f.Menu.OpenMenu(int.Parse(arg), false); Application.DoEvents(); break;
                    case "menuitem":
                        {
                            var p = Popups.Top as MenuPopup;
                            bool ok = p != null && p.ActivateByText(arg);
                            if (!ok) sb.AppendLine("menuitem not found: " + arg);
                            Application.DoEvents();
                            break;
                        }
                    case "amenuitem":   // activate later (for items that open a modal box directly)
                        {
                            var p = Popups.Top as MenuPopup;
                            string text = arg;
                            if (p == null) sb.AppendLine("no menu"); else Deferred.Add(() => p.ActivateByText(text));
                            break;
                        }
                    case "abutton": Deferred.Add(() => f.Bar_PressForTest()); break;
                    case "menudump":
                        {
                            var p = Popups.Top as MenuPopup;
                            if (p == null) { sb.AppendLine("no menu"); break; }
                            foreach (var it in p.Items) sb.AppendLine(it.Separator ? "----" : (it.IsChecked ? "[x] " : "    ") + it.Text + (it.Shortcut != null ? "\t" + it.Shortcut : "") + (it.Sub != null ? " >" : "") + (it.IsEnabled ? "" : " (disabled)") + (it.Bold ? " (default)" : ""));
                            break;
                        }
                    case "rowmenu":   // context menu of the first row whose text starts with arg
                        {
                            var list = CurrentList(f);
                            var row = FindRow(list, arg);
                            if (row == null) { sb.AppendLine("row not found: " + arg); break; }
                            list.Select(row);
                            var m = typeof(TreeList).GetField("RowContextMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(list) as Action<ListRow, Point>;
                            m?.Invoke(row, list.PointToScreen(new Point(60, list.HeaderH + 40)));
                            Application.DoEvents();
                            break;
                        }
                    case "select":
                        {
                            var list = CurrentList(f);
                            var row = FindRow(list, arg);
                            if (row == null) sb.AppendLine("row not found: " + arg); else list.Select(row);
                            Application.DoEvents();
                            break;
                        }
                    case "expand": { var list = CurrentList(f); var row = FindRow(list, arg); if (row != null) list.SetExpanded(row, true); Application.DoEvents(); break; }
                    case "rows":
                        {
                            var list = CurrentList(f);
                            int n = arg.Length > 0 ? int.Parse(arg) : 30;
                            if (list == null) { sb.AppendLine("no list"); break; }
                            var hdr = new StringBuilder();
                            foreach (var c in list.VisibleColumns) hdr.Append(c.Title).Append(c.HeaderTotal != null ? "[" + c.HeaderTotal + "]" : "").Append(" | ");
                            sb.AppendLine(hdr.ToString());
                            int i = 0;
                            foreach (var r in list.FlatRows)
                            {
                                if (i++ >= n) break;
                                var line2 = new StringBuilder(new string(' ', r.Level * 2));
                                if (r.IsGroup) line2.Append("## ").Append(r.GroupText);
                                else foreach (var c in list.VisibleColumns) line2.Append(list.CellOf(r, c).Text).Append(" | ");
                                sb.AppendLine(line2.ToString());
                            }
                            sb.AppendLine("total rows " + list.FlatRows.Count);
                            break;
                        }
                    case "dclose": { var d = TopModal(f); if (d != null) { d.DialogResult = DialogResult.Cancel; d.Close(); } Application.DoEvents(); break; }
                    case "dok": { var d = TopModal(f); (d?.AcceptButton as IButtonControl)?.PerformClick(); Application.DoEvents(); break; }
                    case "dbutton":   // click a dialog button by its text
                        {
                            var d = TopModal(f);
                            var b = d == null ? null : FindButton(d, arg);
                            if (b == null) sb.AppendLine("button not found: " + arg); else b.PerformClick();
                            Application.DoEvents();
                            break;
                        }
                    case "dtext": { var d = TopModal(f); sb.AppendLine(d == null ? "no dialog" : d.Text + " :: " + DialogText(d)); break; }
                    case "size": f.WindowState = FormWindowState.Normal; f.Size = new Size(int.Parse(a[0]), int.Parse(a[1])); Application.DoEvents(); break;
                    case "dpi": f.TestDpi(int.Parse(a[0])); Application.DoEvents(); break;
                    case "sleep": System.Threading.Thread.Sleep(int.Parse(arg)); Application.DoEvents(); break;
                    case "pump": for (int i = 0; i < 20; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(10); } break;
                    case "closepopups": Popups.CloseAll(); break;
                    case "tray": if (f.Tray == null) break; f.Tray.ShowMenu(f.PointToScreen(new Point(100, 100))); Application.DoEvents(); break;
                    case "trayicon":
                        using (var ico = TrayIcon.Render(int.Parse(a[0]), int.Parse(a[1]))) using (var bmp = ico.ToBitmap()) bmp.Save(a[2], ImageFormat.Png);
                        break;
                    case "state":
                        sb.AppendLine("elevated=" + Sys.IsElevated + " exe=" + Program.ExePath + " priority=" + System.Diagnostics.Process.GetCurrentProcess().PriorityClass + " parent=" + ParentName());
                        sb.AppendLine("tab=" + f.CurrentTab + " fewer=" + f.FewerDetailsMode + " procs=" + f.Data.Procs.Count + " tick=" + f.Data.Tick + " etw=" + f.Data.EtwActive + " (" + f.Data.EtwError + ")"
                            + " popups=" + (Popups.Top?.GetType().Name ?? "none") + " forms=" + Application.OpenForms.Count + " modal=" + (TopModal(f)?.Text ?? "none")
                            + " size=" + f.ClientSize + " dpi=" + f.DpiValue + " errors=" + ErrorLog.Count + " lastError=" + (LastError ?? "none") + " tray=" + f.Tray?.LastTooltip?.Replace("\n", "/"));
                        break;
                    case "perf":
                        {
                            var pr = Process.GetCurrentProcess();
                            sb.AppendLine("stages: " + f.Data.SampleBreakdown);
                            sb.AppendLine("sampleMs=" + f.Data.LastSampleMs.ToString("0.00") + " applyMs=" + f.Data.LastApplyMs.ToString("0.00") + " cpuTime=" + pr.TotalProcessorTime.TotalMilliseconds.ToString("0")
                                + "ms uptime=" + (DateTime.Now - pr.StartTime).TotalMilliseconds.ToString("0") + "ms ws=" + (pr.WorkingSet64 >> 20) + "MB private=" + (pr.PrivateMemorySize64 >> 20) + "MB managed=" + (GC.GetTotalMemory(false) >> 20)
                                + "MB gc0=" + GC.CollectionCount(0) + " gc1=" + GC.CollectionCount(1) + " gc2=" + GC.CollectionCount(2) + " handles=" + pr.HandleCount + " gdi=" + Sys.GetGuiResources(Sys.GetCurrentProcess(), 0) + " user=" + Sys.GetGuiResources(Sys.GetCurrentProcess(), 1));
                            break;
                        }
                    case "repaint":
                        {
                            var sw = Stopwatch.StartNew();
                            int n = arg.Length > 0 ? int.Parse(arg) : 10;
                            var page = f.CurrentPage;
                            for (int i = 0; i < n; i++) { if (f.FewerDetailsMode) f.Fewer.List.Refresh(); else if (page?.List != null) { page.List.Invalidate(); page.List.Update(); } else { page?.Invalidate(true); page?.Update(); } }
                            sb.AppendLine("repaint avg " + (sw.Elapsed.TotalMilliseconds / n).ToString("0.00") + "ms");
                            break;
                        }
                    case "tick": sb.AppendLine("tick " + _sw.ElapsedMilliseconds + "ms " + arg); _sw.Restart(); break;
                    case "listbench":   // TreeList.Render alone on a reused bitmap
                        {
                            int n = arg.Length > 0 ? int.Parse(arg) : 50;
                            var list = CurrentList(f);
                            if (list == null) { sb.AppendLine("no list"); break; }
                            using (var bmp = new Bitmap(list.Width, list.Height))
                            using (var g = Graphics.FromImage(bmp))
                            {
                                list.RenderTo(g);
                                var sw = Stopwatch.StartNew();
                                for (int i = 0; i < n; i++) list.RenderTo(g);
                                sb.AppendLine("list render avg " + (sw.Elapsed.TotalMilliseconds / n).ToString("0.00") + "ms, " + list.FlatRows.Count + " rows");
                            }
                            break;
                        }
                    case "renderbench":   // full paint of the current page into a bitmap (offscreen windows get no WM_PAINT)
                        {
                            int n = arg.Length > 0 ? int.Parse(arg) : 20;
                            var page = (Control)f.CurrentPage;
                            var sz = page.ClientSize;
                            using (var bmp = new Bitmap(sz.Width, sz.Height))
                            {
                                var sw = Stopwatch.StartNew();
                                for (int i = 0; i < n; i++) RenderInto(page, bmp, Point.Empty);
                                sb.AppendLine("full page render avg " + (sw.Elapsed.TotalMilliseconds / n).ToString("0.00") + "ms (" + sz.Width + "x" + sz.Height + ")");
                            }
                            break;
                        }
                    case "shot": Shot(f, arg); break;
                    case "dshot": { var d = TopModal(f); if (d != null) ShotForm(d, arg); else sb.AppendLine("no dialog"); break; }
                    case "quit": f.Close(); break;
                    case "pview": (f.GetPage(1) as PerformancePage)?.Test(arg); Application.DoEvents(); break;
                    case "clip": sb.AppendLine((f.GetPage(1) as PerformancePage)?.LastCopied ?? "(none)"); break;
                    case "listclip": sb.AppendLine(CurrentList(f)?.LastCopied ?? "(none)"); break;
                    case "eval": sb.AppendLine(Eval(f, arg)); break;
                    case "proc":   // live numbers of processes whose name starts with arg
                        foreach (var p in f.Data.Procs)
                            if (p.Name.StartsWith(arg, StringComparison.OrdinalIgnoreCase))
                                sb.AppendLine(p.Name + " pid=" + p.Pid + " cpu=" + p.Cur.Cpu.ToString("0.0") + " disk=" + (p.Cur.DiskBps / 1048576).ToString("0.00") + "MB/s net=" + (p.Cur.NetBps / 1e6).ToString("0.000") + "Mbps ioRead=" + (p.Cur.IoReadBytes >> 20) + "MB ioWrite=" + (p.Cur.IoWriteBytes >> 20) + "MB");
                        break;
                }
            }
            return sb.Length == 0 ? "OK" : sb.ToString();
        }

        static string Eval(MainForm f, string what)
        {
            switch (what)
            {
                case "cpu": return f.Data.Cpu.Name + " base=" + f.Data.Cpu.BaseMhz + " cores=" + f.Data.Cpu.Cores + " logical=" + f.Data.Cpu.Logical + " sockets=" + f.Data.Cpu.Sockets + " L1=" + f.Data.Cpu.L1 + " L2=" + f.Data.Cpu.L2 + " L3=" + f.Data.Cpu.L3 + " numa=" + f.Data.Cpu.NumaNodes + " virt=" + f.Data.Cpu.Virtualization;
                case "mem":
                    {
                        var s = f.Data.Sys; var m = f.Data.Mem;
                        return "total=" + (s.MemTotal >> 20) + "MB inuse=" + (s.MemInUse >> 20) + " avail=" + (s.MemAvail >> 20) + " modified=" + (s.Modified >> 20) + " standby=" + (s.Standby >> 20) + " free=" + (s.Free >> 20)
                            + " commit=" + (s.CommitTotal >> 20) + "/" + (s.CommitLimit >> 20) + " paged=" + (s.PagedPool >> 20) + " nonpaged=" + (s.NonPagedPool >> 20) + " compressed=" + (s.Compressed >> 20)
                            + " installed=" + (m.InstalledBytes >> 20) + " speed=" + m.SpeedMhz + " slots=" + m.SlotsUsed + "/" + m.SlotsTotal + " ff=" + m.FormFactor;
                    }
                case "disks":
                    {
                        var sb = new StringBuilder();
                        foreach (var d in f.Data.Disks) sb.Append(d.Static.Title + " model=" + d.Static.Model + " type=" + d.Static.Type + " cap=" + (d.Static.Capacity >> 30) + "GB fmt=" + (d.Static.Formatted >> 30) + "GB sys=" + d.Static.SystemDisk + " pf=" + d.Static.PageFile + " active=" + d.Last?.Active.ToString("0.0") + " r=" + d.Last?.ReadBps.ToString("0") + " w=" + d.Last?.WriteBps.ToString("0") + "; ");
                        return sb.ToString();
                    }
                case "nets":
                    {
                        var sb = new StringBuilder();
                        foreach (var n in f.Data.Nets) sb.Append(n.Static.Kind + " alias=" + n.Static.Alias + " desc=" + n.Static.Description + " ip4=" + n.Static.Ipv4 + " ip6=" + n.Static.Ipv6 + " conn=" + n.Last?.Connected + " link=" + n.Last?.LinkBps + " s=" + n.Last?.SendBps.ToString("0") + " r=" + n.Last?.RecvBps.ToString("0") + " ssid=" + n.Static.Ssid + "; ");
                        return sb.ToString();
                    }
                case "gpus":
                    {
                        var sb = new StringBuilder();
                        foreach (var g in f.Data.Gpus) sb.Append(g.Static.Title + " " + g.Static.Name + " drv=" + g.Static.DriverVersion + " date=" + g.Static.DriverDate + " dx=" + g.Static.DirectX + " loc=" + g.Static.Location + " ded=" + (g.Static.DedicatedLimit >> 20) + "MB shared=" + (g.Static.SharedLimit >> 20) + "MB hwres=" + (g.Static.HwReserved >> 20) + "MB engines=" + string.Join("/", g.Static.EngineNames) + " max=" + g.Last?.Max.ToString("0.0") + " dedUse=" + ((g.Last?.Dedicated ?? 0) >> 20) + "MB temp=" + g.Last?.TemperatureC + "; ");
                        return sb.ToString();
                    }
                case "services": return "services=" + f.Data.Services.Count + " byPid=" + f.Data.ServicesByPid.Count;
                case "activations": return "activations=" + Activations;
                case "startupload": { var sw = System.Diagnostics.Stopwatch.StartNew(); var l1 = StartupItems.Load(); double a = sw.Elapsed.TotalMilliseconds; sw.Restart(); var l2 = StartupItems.Load(); return "items=" + l1.Count + " first=" + a.ToString("0.0") + "ms again=" + sw.Elapsed.TotalMilliseconds.ToString("0.0") + "ms impactReadable=" + StartupItems.ImpactReadable; }
                case "startup": return StartupLog.Text + "\nbreakdown: " + f.Data.SampleBreakdown;
                case "config": { var net = System.Configuration.ConfigurationManager.GetSection("system.net/settings"); var wf = System.Configuration.ConfigurationManager.GetSection("system.windows.forms"); var app = System.Configuration.ConfigurationManager.AppSettings; return "system.net/settings=" + (net?.GetType().Name ?? "null") + " system.windows.forms=" + (wf?.GetType().Name ?? "null") + " appSettings=" + app.GetType().Name + "/" + app.Count; }
                case "memhw":
                    {
                        var a = new MemStatic(); MemStatic.FromSmbios(a);
                        var b = new MemStatic(); System.Threading.Tasks.Task.Run(() => MemStatic.FromWmi(b)).Wait();   // WMI from the thread pool (MTA), as the app does
                        return "smbios speed=" + a.SpeedMhz + " slots=" + a.SlotsUsed + "/" + a.SlotsTotal + " ff=" + a.FormFactor + " | wmi speed=" + b.SpeedMhz + " slots=" + b.SlotsUsed + "/" + b.SlotsTotal + " ff=" + b.FormFactor;
                    }
                case "memlists":
                    {
                        bool ok = MemoryLists.Read(out long fr, out long md, out long sb);
                        using (var q = new PdhQuery())
                        {
                            IntPtr cm = q.Add(@"\Memory\Modified Page List Bytes"), c1 = q.Add(@"\Memory\Standby Cache Core Bytes"), c2 = q.Add(@"\Memory\Standby Cache Normal Priority Bytes"), c3 = q.Add(@"\Memory\Standby Cache Reserve Bytes"), cf = q.Add(@"\Memory\Free & Zero Page List Bytes");
                            q.Collect();
                            double pm = q.Value(cm), ps = q.Value(c1) + q.Value(c2) + q.Value(c3), pf = q.Value(cf);
                            return "kernel ok=" + ok + " free=" + (fr >> 20) + " modified=" + (md >> 20) + " standby=" + (sb >> 20) + " | pdh free=" + ((long)pf >> 20) + " modified=" + ((long)pm >> 20) + " standby=" + ((long)ps >> 20) + " (MB)";
                        }
                    }
                case "windows": { var sb = new StringBuilder(); foreach (var kv in f.Data.Windows) foreach (var w in kv.Value) sb.Append(kv.Key + ":" + w.Title + (w.Hung ? "(hung)" : "") + "; "); return sb.ToString(); }
            }
            return "unknown eval " + what;
        }

        static string ParentName()
        {
            try
            {
                var me = Program.Window?.Data.ProcByPid;
                int self = Sys.GetCurrentProcessId();
                if (me != null && me.TryGetValue(self, out var p) && me.TryGetValue(p.Cur.ParentPid, out var parent)) return parent.Name + " (" + parent.Pid + ")";
            }
            catch (Exception ex) { return "? (" + ex.GetType().Name + ")"; }
            return "?";
        }

        static TreeList CurrentList(MainForm f) => f.FewerDetailsMode ? f.Fewer?.List : f.CurrentPage?.List;

        static ListRow FindRow(TreeList list, string prefix)
        {
            if (list == null) return null;
            foreach (var r in list.FlatRows)
            {
                if (r.IsGroup) continue;
                var t = list.SearchTextOf?.Invoke(r) ?? "";
                if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return r;
            }
            return null;
        }

        static IButtonControl FindButton(Control root, string text)
        {
            foreach (Control c in root.Controls)
            {
                if (c is DarkButton b && MenuItem.Clean(b.Text).Equals(text, StringComparison.OrdinalIgnoreCase)) return b;
                var r = FindButton(c, text);
                if (r != null) return r;
            }
            return null;
        }

        static string DialogText(Control root)
        {
            var sb = new StringBuilder();
            foreach (Control c in root.Controls)
            {
                if (c is DarkLabel || c is DarkButton || c is DarkCheck) sb.Append("[" + c.GetType().Name.Replace("Dark", "") + ":" + c.Text.Replace("\n", " ") + "] ");
                else if (c is DarkTextBox t) sb.Append("[Text:" + t.Text + "] ");
                sb.Append(DialogText(c));
            }
            return sb.ToString();
        }

        /// <summary>Composite screenshot: main window client + every open popup / dialog at its real position.</summary>
        static void Shot(MainForm f, string path)
        {
            Application.DoEvents();
            var origin = f.PointToScreen(Point.Empty);
            var size = f.ClientSize;
            using (var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp)) g.Clear(Color.Magenta);
                RenderInto(f, bmp, Point.Empty);
                foreach (Form of in Application.OpenForms)
                {
                    if (of == f || !of.Visible) continue;
                    var p = of.PointToScreen(Point.Empty);
                    RenderInto(of, bmp, new Point(p.X - origin.X, p.Y - origin.Y));
                }
                bmp.Save(path, ImageFormat.Png);
            }
        }

        static void ShotForm(Form d, string path)
        {
            Application.DoEvents();
            using (var bmp = new Bitmap(d.ClientSize.Width, d.ClientSize.Height, PixelFormat.Format32bppArgb))
            {
                RenderInto(d, bmp, Point.Empty);
                bmp.Save(path, ImageFormat.Png);
            }
        }

        static void RenderInto(Control c, Bitmap target, Point at)
        {
            var cs = c.ClientSize;
            if (cs.Width <= 0 || cs.Height <= 0) return;
            using (var b = new Bitmap(cs.Width, cs.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(b))
                {
                    IntPtr hdc = g.GetHdc();
                    try { Native.SendMessage(c.Handle, 0x0318 /*WM_PRINTCLIENT*/, hdc, (IntPtr)(0x4 | 0x10)); }
                    finally { g.ReleaseHdc(hdc); }
                }
                using (var g = Graphics.FromImage(target)) g.DrawImageUnscaled(b, at);
                foreach (Control child in c.Controls)
                {
                    if (!child.Visible) continue;
                    RenderInto(child, target, new Point(at.X + child.Left, at.Y + child.Top));
                }
            }
        }
    }
}
