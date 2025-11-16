namespace UnityLauncherPro.Data
{
    public class OnlineTemplateItem
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string RenderPipeline { get; set; }
        public string Type { get; set; } // Core, Learning, Sample, 
        public string PreviewImageURL { get; set; }
        public string TarBallURL { get; set; }
    }
}