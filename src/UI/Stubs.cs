using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace TaskManagerDark
{

    public sealed partial class MainForm
    {
        // a filter typed on one tab would hide the row being jumped to on the other
        public void GoToDetails(int pid)
        {
            ClearFilter();
            ShowPage(5);
            (Pages[5] as DetailsPage)?.SelectPid(pid);
        }

        public void GoToServices(int pid)
        {
            ClearFilter();
            ShowPage(6);
            (Pages[6] as ServicesPage)?.SelectForPid(pid);
        }
    }
}
