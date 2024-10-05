using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace UnityLauncherPro
{
    public class UnityVersionResponse
    {
        [JsonPropertyName("offset")]
        public int Offset { get; set; }
        [JsonPropertyName("limit")]
        public int Limit { get; set; }
        [JsonPropertyName("total")]
        public int Total { get; set; }
        [JsonPropertyName("results")]
        public List<UnityVersion> Results { get; set; }
    }
}