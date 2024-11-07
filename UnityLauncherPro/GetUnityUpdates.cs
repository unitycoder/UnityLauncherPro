using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace UnityLauncherPro
{
    public static class GetUnityUpdates
    {
        private const string BaseApiUrl = "https://services.api.unity.com/unity/editor/release/v1/releases";
        private const int BatchSize = 25;
        private const int RequestsPerBatch = 10;
        private const int DelayBetweenBatches = 1000; // 1 second in milliseconds
        private const string CacheFileName = "UnityVersionCache.json";
        private static readonly HttpClient Client = new HttpClient();

        public static async Task<List<UnityVersion>> FetchAll()
        {
            var cachedVersions = LoadCachedVersions();
            Console.WriteLine("cachedVersions: "+ cachedVersions);
            var latestCachedVersion = cachedVersions.FirstOrDefault();

            Console.WriteLine("FetchAll "+ latestCachedVersion);
            var newVersions = await FetchNewVersions(latestCachedVersion);
            Console.WriteLine("newVersions " + newVersions);

            var allVersions = newVersions.Concat(cachedVersions).ToList();

            if (newVersions.Count > 0)
            {
                SaveCachedVersions(allVersions);
            }

            Console.WriteLine("all "+ allVersions);

            return allVersions;
        }

        public static async Task<string> FetchDownloadUrl(string unityVersion)
        {
            if (string.IsNullOrEmpty(unityVersion))
            {
                return null;
            }

            string apiUrl = $"{BaseApiUrl}?limit=1&version={unityVersion}&architecture=X86_64&platform=WINDOWS";

            try
            {
                string responseString = await Client.GetStringAsync(apiUrl);
                return await ExtractDownloadUrlAsync(responseString, unityVersion);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error fetching download URL: {e.Message}");
                return null;
            }
        }

        private static async Task<string> ExtractDownloadUrlAsync(string json, string unityVersion)
        {

            int resultsIndex = json.IndexOf("\"results\":");
            if (resultsIndex == -1) return null;

            int downloadsIndex = json.IndexOf("\"downloads\":", resultsIndex);
            if (downloadsIndex == -1) return null;

            int urlIndex = json.IndexOf("\"url\":", downloadsIndex);
            if (urlIndex == -1) return null;

            int urlStart = json.IndexOf('"', urlIndex + 6) + 1;
            int urlEnd = json.IndexOf('"', urlStart);
            string downloadUrl = json.Substring(urlStart, urlEnd - urlStart);

            int revisionIndex = json.IndexOf("\"shortRevision\":", resultsIndex);
            string shortRevision = null;
            if (revisionIndex != -1)
            {
                int revisionStart = json.IndexOf('"', revisionIndex + 16) + 1;
                int revisionEnd = json.IndexOf('"', revisionStart);
                shortRevision = json.Substring(revisionStart, revisionEnd - revisionStart);
            }

            if (!string.IsNullOrEmpty(downloadUrl) && !string.IsNullOrEmpty(shortRevision))
            {
                int revisionPosition = downloadUrl.LastIndexOf(shortRevision, StringComparison.Ordinal) + shortRevision.Length + 1;
                string assistantUrl = downloadUrl.Substring(0, revisionPosition) + $"UnityDownloadAssistant-{unityVersion}.exe";

                if (await CheckAssistantUrl(assistantUrl))
                {
                    Console.WriteLine("Assistant download URL found.");
                    return assistantUrl;
                }
                else
                {
                    Console.WriteLine("Assistant download URL not found, returning original download URL.");
                    return downloadUrl;
                }
            }

            Console.WriteLine("Returning original download URL.");
            return downloadUrl;
        }

        private static async Task<bool> CheckAssistantUrl(string assistantUrl)
        {
            try
            {
                using (HttpResponseMessage response = await Client.GetAsync(assistantUrl))
                {
                    return response.IsSuccessStatusCode;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Request failed: {ex.Message}");
                return false;
            }
        }

        private static async Task<List<UnityVersion>> FetchNewVersions(UnityVersion latestCachedVersion)
        {
            var newVersions = new List<UnityVersion>();
            int offset = 0;
            int total = int.MaxValue;

            while (offset < total)
            {
                var batchUpdates = await FetchBatch(offset);
                if (batchUpdates == null || batchUpdates.Count == 0)
                    break;

                foreach (var version in batchUpdates)
                {
                    if (version.Version == latestCachedVersion?.Version)
                        return newVersions;

                    newVersions.Add(version);
                }

                offset += batchUpdates.Count;

                if (offset % (BatchSize * RequestsPerBatch) == 0)
                {
                    await Task.Delay(DelayBetweenBatches);
                }
            }

            return newVersions;
        }

        private static async Task<List<UnityVersion>> FetchBatch(int offset)
        {
            string url = $"{BaseApiUrl}?limit={BatchSize}&offset={offset}&architecture=X86_64&platform=WINDOWS";

            try
            {
                string response = await Client.GetStringAsync(url);
                return ParseUnityVersions(response);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error fetching batch: {e.Message}");
                return null;
            }
        }

        private static List<UnityVersion> ParseUnityVersions(string json)
        {
            var versions = new List<UnityVersion>();
            int resultsIndex = json.IndexOf("\"results\":");
            if (resultsIndex == -1) return versions;

            string[] items = json.Substring(resultsIndex).Split(new[] { "{" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string item in items)
            {
                if (item.Contains("\"version\""))
                {
                    var version = new UnityVersion
                    {
                        Version = GetStringValue(item, "version"),
                        ReleaseDate = DateTime.TryParse(GetStringValue(item, "releaseDate"), out var date) ? date : default,
                        Stream = Enum.TryParse<UnityVersionStream>(GetStringValue(item, "stream"), true, out var stream) ? stream : UnityVersionStream.Tech
                    };
                    versions.Add(version);
                }
            }

            return versions;
        }

        private static List<UnityVersion> ParseCachedUnityVersions(string json)
        {
            var versions = new List<UnityVersion>();

            // Remove square brackets at the beginning and end of the array
            json = json.Trim(new[] { '[', ']' });

            // Split each item based on the closing bracket and opening bracket of consecutive objects
            string[] items = json.Split(new[] { "},{" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string item in items)
            {
                // Ensure each item is properly enclosed in braces to handle edge cases
                string cleanItem = "{" + item.Trim(new[] { '{', '}' }) + "}";

                // Parse each UnityVersion object
                if (cleanItem.Contains("\"version\""))
                {
                    var version = new UnityVersion
                    {
                        Version = GetStringValue(cleanItem, "version"),
                        ReleaseDate = DateTime.TryParse(GetStringValue(cleanItem, "releaseDate"), out var date) ? date : default,
                        Stream = Enum.TryParse<UnityVersionStream>(GetStringValue(cleanItem, "stream"), true, out var stream) ? stream : UnityVersionStream.Tech
                    };
                    versions.Add(version);
                }
            }

            return versions;
        }

        //private static string GetStringValue(string source, string propertyName)
        //{
        //    int propertyIndex = source.IndexOf($"\"{propertyName}\":");
        //    if (propertyIndex == -1) return null;

        //    int valueStart = source.IndexOf('"', propertyIndex + propertyName.Length + 2) + 1;
        //    int valueEnd = source.IndexOf('"', valueStart);
        //    return source.Substring(valueStart, valueEnd - valueStart);
        //}


        private static string GetStringValue(string source, string propertyName)
        {
            int propertyIndex = source.IndexOf($"\"{propertyName}\":");
            if (propertyIndex == -1) return null;

            int valueStart = source.IndexOf('"', propertyIndex + propertyName.Length + 2) + 1;
            int valueEnd = source.IndexOf('"', valueStart);
            return source.Substring(valueStart, valueEnd - valueStart);
        }

        private static List<UnityVersion> LoadCachedVersions()
        {
            string configFilePath = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal).FilePath;
            string configDirectory = Path.GetDirectoryName(configFilePath);
            if (configDirectory == null) return new List<UnityVersion>();

            string cacheFilePath = Path.Combine(configDirectory, CacheFileName);
            if (!File.Exists(cacheFilePath)) return new List<UnityVersion>();

            string json = File.ReadAllText(cacheFilePath);
            return ParseCachedUnityVersions(json);
        }

        private static void SaveCachedVersions(List<UnityVersion> versions)
        {
            string json = "[";
            foreach (var version in versions)
            {
                json += $"{{\"version\":\"{version.Version}\",\"releaseDate\":\"{version.ReleaseDate:O}\",\"stream\":\"{version.Stream}\"}},";
            }
            json = json.TrimEnd(',') + "]";

            string configFilePath = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal).FilePath;
            string configDirectory = Path.GetDirectoryName(configFilePath);
            if (configDirectory == null) return;

            string cacheFilePath = Path.Combine(configDirectory, CacheFileName);
            File.WriteAllText(cacheFilePath, json);
        }
    }
}
