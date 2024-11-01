using System.Collections.Generic;

namespace UnityLauncherPro
{
    public class UnityVersionResponse
    {
        public int Offset { get; set; }
        public int Limit { get; set; }
        public int Total { get; set; }
        public List<UnityVersion> Results { get; set; }
    }
}