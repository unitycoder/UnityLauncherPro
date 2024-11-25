using System;
using System.Collections.Generic;

namespace UnityLauncherPro
{
    public enum UnityVersionStream
    {
        Alpha,
        Beta,
        LTS,
        Tech
    }

    public class UnityVersionJSON
    {
        public string Version { get; set; }
        public DateTime ReleaseDate { get; set; }
        public UnityVersionStream Stream { get; set; }
        public List<Download> Downloads { get; set; }
        public string ShortRevision { get; set; }
    }

    public class Download
    {
        public string Url { get; set; }
        public string Type { get; set; }
        public string Platform { get; set; }
        public string Architecture { get; set; }
        public DownloadSize DownloadSize { get; set; }
        public List<Module> Modules { get; set; }
    }

    public class DownloadSize
    {
        public long Value { get; set; }
        public string Unit { get; set; }
    }

    public class Module
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Url { get; set; }
    }
}