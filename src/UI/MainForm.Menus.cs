using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace TaskManagerDark
{
    public sealed partial class MainForm
    {
        // Labels and order follow Taskmgr's menu resource #30205.
        List<MenuItem> BuildFileMenu()
        {
            var l = new List<MenuItem>
            {
                new MenuItem("Run &new task", () => RunDialog.ShowAsync(this)) { Id = 30467 },
            };
            if (!Sys.IsElevated) l.Add(new MenuItem("Run as a&dministrator", () => Elevation.RestartElevated(this)));
            l.Add(new MenuItem("E&xit", Close) { Id = 30435 });
            return l;
        }

        List<MenuItem> BuildOptionsMenu()
        {
            var tabs = new List<MenuItem>();
            string[] labels = { "P&rocesses", "P&erformance", "App h&istory", "S&tartup", "&Users", "Detai&ls", "Ser&vices" };
            for (int i = 0; i < 7; i++)
            {
                int t = i;
                tabs.Add(MenuItem.RadioItem(labels[i], () => DefaultTab == t, () => { DefaultTab = t; Tabs.DefaultTab = t; Settings.SetInt("DefaultTab", t); Settings.Save(); }));
            }
            return new List<MenuItem>
            {
                MenuItem.Check("&Always on top", () => AlwaysOnTop, ToggleAlwaysOnTop),
                MenuItem.Check("&Minimize on use", () => MinimizeOnUse, () => { MinimizeOnUse = !MinimizeOnUse; SaveSettings(); }),
                MenuItem.Check("&Hide when minimized", () => HideWhenMinimized, () => { HideWhenMinimized = !HideWhenMinimized; SaveSettings(); }),
                MenuItem.Popup("Set &default tab", tabs),
                MenuItem.Sep(),
                MenuItem.Check("&Show full account name", () => ShowFullAccountName, () => { ShowFullAccountName = !ShowFullAccountName; SaveSettings(); foreach (var p in Pages) p?.DataUpdated(); }),
                MenuItem.Sep(),
                MenuItem.Check("Show history for all &processes", () => ShowHistoryAllProcesses, () => { ShowHistoryAllProcesses = !ShowHistoryAllProcesses; SaveSettings(); (Pages[2] as AppHistoryPage)?.Reload(); }),
            };
        }

        public void ToggleAlwaysOnTop()
        {
            AlwaysOnTop = !AlwaysOnTop;
            TopMost = AlwaysOnTop && !TestHook.Enabled;
            SaveSettings();
        }

        List<MenuItem> BuildViewMenu()
        {
            var speeds = new List<MenuItem>();
            string[] labels = { "&High", "&Normal", "&Low", "&Paused" };
            for (int i = 0; i < 4; i++)
            {
                int s = i;
                speeds.Add(MenuItem.RadioItem(labels[i], () => UpdateSpeed == s, () => { SetUpdateSpeed(s); SaveSettings(); }));
            }
            var l = new List<MenuItem>
            {
                new MenuItem("&Refresh now\tF5", Data.RefreshNow) { Id = 30476 },
                MenuItem.Popup("&Update speed", speeds),
            };
            var extra = CurrentPage?.ViewMenuItems();
            if (extra != null && extra.Count > 0) { l.Add(MenuItem.Sep()); l.AddRange(extra); }
            return l;
        }

        /// <summary>The View menu items shared by the tabs with expandable rows.</summary>
        public List<MenuItem> ExpandCollapseItems(Page p) => new List<MenuItem>
        {
            new MenuItem("&Expand all", () => p.ExpandAll(true)) { Id = 30479 },
            new MenuItem("&Collapse all", () => p.ExpandAll(false)) { Id = 30480 },
        };

        public List<MenuItem> StatusValuesMenu() => new List<MenuItem>
        {
            MenuItem.Popup("&Status values", new List<MenuItem>
            {
                MenuItem.RadioItem("&Show suspended status", () => ShowSuspendedStatus, () => { ShowSuspendedStatus = true; SaveSettings(); CurrentPage?.DataUpdated(); }),
                MenuItem.RadioItem("&Hide suspended status", () => !ShowSuspendedStatus, () => { ShowSuspendedStatus = false; SaveSettings(); CurrentPage?.DataUpdated(); }),
            }),
        };

        /// <summary>Shows a context menu at a screen point.</summary>
        public void ShowContextMenu(List<MenuItem> items, System.Drawing.Point screen)
        {
            if (items == null || items.Count == 0) return;
            // drop leading/trailing/duplicate separators left by hidden items
            var clean = new List<MenuItem>();
            foreach (var it in items)
            {
                if (it == null) continue;
                if (it.Separator && (clean.Count == 0 || clean[clean.Count - 1].Separator)) continue;
                clean.Add(it);
            }
            while (clean.Count > 0 && clean[clean.Count - 1].Separator) clean.RemoveAt(clean.Count - 1);
            if (clean.Count == 0) return;
            var m = new MenuPopup(D.Value, clean);
            m.ShowAtPoint(screen);
        }
    }
}
