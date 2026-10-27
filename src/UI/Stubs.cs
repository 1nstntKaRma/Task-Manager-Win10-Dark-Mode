using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace TaskManagerDark
{

    public sealed partial class MainForm
    {
        public void GoToDetails(int pid)
        {
            ShowPage(5);
            (Pages[5] as DetailsPage)?.SelectPid(pid);
        }

        public void GoToServices(int pid)
        {
            ShowPage(6);
            (Pages[6] as ServicesPage)?.SelectForPid(pid);
        }
    }
}
