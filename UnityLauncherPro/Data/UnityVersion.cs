using System;
using System.Collections.Generic;

namespace UnityLauncherPro
{
    public class UnityVersion
    {
        public string Version { get; set; }
        public UnityVersionStream Stream { get; set; }
        public DateTime ReleaseDate { get; set; }

        public static UnityVersion FromJson(string json)
        {
            var values = ParseJsonToDictionary(json);

            return new UnityVersion
            {
                Version = values.ContainsKey("version") ? values["version"] : null,
                Stream = ParseStream(values.ContainsKey("stream") ? values["stream"] : null),
                ReleaseDate = DateTime.TryParse(values.ContainsKey("releaseDate") ? values["releaseDate"] : null, out var date)
                    ? date
                    : default
            };
        }

        public string ToJson()
        {
            return $"{{ \"version\": \"{Version}\", \"stream\": \"{Stream}\", \"releaseDate\": \"{ReleaseDate:yyyy-MM-ddTHH:mm:ss}\" }}";
        }

        private static Dictionary<string, string> ParseJsonToDictionary(string json)
        {
            var result = new Dictionary<string, string>();
            json = json.Trim(new char[] { '{', '}', ' ' });
            var keyValuePairs = json.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var pair in keyValuePairs)
            {
                var keyValue = pair.Split(new[] { ':' }, 2);
                if (keyValue.Length == 2)
                {
                    var key = keyValue[0].Trim(new char[] { ' ', '"' });
                    var value = keyValue[1].Trim(new char[] { ' ', '"' });
                    result[key] = value;
                }
            }

            return result;
        }

        private static UnityVersionStream ParseStream(string stream)
        {
            return Enum.TryParse(stream, true, out UnityVersionStream result) ? result : UnityVersionStream.Tech;
        }
    }
}
