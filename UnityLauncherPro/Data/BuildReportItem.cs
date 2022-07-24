namespace UnityLauncherPro
{
    public class BuildReportItem
    {
        // TODO use real values, so can sort and convert kb/mb
        public string Category { set; get; } // for category list
        public string Size { set; get; }
        public string Percentage { set; get; }
        public string Path { set; get; }
        public string Format { set; get; }
    }
}
