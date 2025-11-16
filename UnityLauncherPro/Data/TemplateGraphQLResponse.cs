namespace UnityLauncherPro.Data
{
    public class TemplateGraphQLResponse
    {
        public TemplateData data { get; set; }
    }

    public class TemplateData
    {
        public GetTemplates getTemplates { get; set; }
    }

    public class GetTemplates
    {
        public TemplateEdge[] edges { get; set; }
    }

    public class TemplateEdge
    {
        public TemplateNode node { get; set; }
    }

    public class TemplateNode
    {
        public string name { get; set; }
        public string packageName { get; set; }
        public string description { get; set; }
        public string type { get; set; }
        public string renderPipeline { get; set; }
        public PreviewImage previewImage { get; set; }
        public TemplateVersion[] versions { get; set; }
    }

    public class PreviewImage
    {
        public string url { get; set; }
    }

    public class TemplateVersion
    {
        public string name { get; set; }
        public Tarball tarball { get; set; }
    }

    public class Tarball
    {
        public string url { get; set; }
    }
}