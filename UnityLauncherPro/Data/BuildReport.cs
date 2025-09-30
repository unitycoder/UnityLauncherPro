using System.Collections.Generic;

namespace UnityLauncherPro
{
    public class BuildReport
    {
        public long ElapsedTimeMS { set; get; }
        public List<BuildReportItem> Stats { set; get; } // overal per category sizes
        public List<BuildReportItem> Items { set; get; } // report rows
    }
}
