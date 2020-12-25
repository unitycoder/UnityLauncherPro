using System;
using System.Diagnostics;

namespace UnityLauncherPro
{
    public class Project
    {
        public string Title { set; get; }
        public string Version { set; get; }
        public string Path { set; get; }
        public DateTime? Modified { set; get; }
        public string Arguments { set; get; }
        public string GITBranch { set; get; }
        public string TargetPlatform { set; get; }
        public Process Process; // launched unity exe

        public override string ToString()
        {
            return $"{Title} {Version} {Path} {Modified} {Arguments} {GITBranch} {TargetPlatform}";
        }

    }
}