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

        static Dictionary<string, string> unofficialReleaseURLs = new Dictionary<string, string>();

        public static async Task<List<UnityVersion>> FetchAll(bool useUnofficialList = false)
        {
            var cachedVersions = LoadCachedVersions();
            var newVersions = await FetchNewVersions(cachedVersions);

            var allVersions = newVersions.Concat(cachedVersions).ToList();

            if (useUnofficialList == true)
            {
                var unofficialVersions = await FetchUnofficialVersions(cachedVersions);
                unofficialReleaseURLs.Clear();
                // TODO modify FetchUnofficialVersions to put items in this dictionary directlys
                foreach (var version in unofficialVersions)
                {
                    //Console.WriteLine("unofficial: " + version.Version + " , " + version.directURL);
                    if (unofficialReleaseURLs.ContainsKey(version.Version) == false)
                    {
                        unofficialReleaseURLs.Add(version.Version, version.directURL);
                    }
                }
                allVersions = unofficialVersions.Concat(allVersions).ToList();
            }

            if (newVersions.Count > 0)
            {
                SaveCachedVersions(allVersions);
            }

            return allVersions;
        }

        public static async Task<List<UnityVersion>> FetchUnofficialVersions(List<UnityVersion> cachedVersions)
        {
            var unofficialVersions = new List<UnityVersion>();
            var existingVersions = new HashSet<string>(cachedVersions.Select(v => v.Version));

            try
            {
                string url = "https://raw.githubusercontent.com/unitycoder/UnofficialUnityReleasesWatcher/refs/heads/main/unity-releases.md";

                var content = await Client.GetStringAsync(url);

                // Parse the Markdown content
                var lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.StartsWith("- ")) // Identify Markdown list items
                    {
                        var urlPart = line.Substring(2).Trim();
                        var version = ExtractVersionFromUrl(urlPart);

                        if (!string.IsNullOrEmpty(version) && !existingVersions.Contains(version))
                        {
                            var stream = InferStreamFromVersion(version);

                            unofficialVersions.Add(new UnityVersion
                            {
                                Version = version,
                                Stream = stream,
                                ReleaseDate = DateTime.Now, // NOTE not correct, but we don't have known release date for unofficial versions (its only when they are found..)
                                //ReleaseDate = DateTime.MinValue // Release date is unavailable in the MD format, TODO add to md as #2021-01-01 ?
                                directURL = urlPart, // this is available only for unofficial releases
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching unofficial versions: {ex.Message}");
            }

            return unofficialVersions;
        }

        // TODO fixme, f is not always LTS
        private static UnityVersionStream InferStreamFromVersion(string version)
        {
            if (Tools.IsAlpha(version)) return UnityVersionStream.Alpha;
            if (Tools.IsBeta(version)) return UnityVersionStream.Beta;
            if (Tools.IsLTS(version)) return UnityVersionStream.LTS;

            //if (version.Contains("a")) return UnityVersionStream.Alpha;
            //if (version.Contains("b")) return UnityVersionStream.Beta;
            //if (version.Contains("f")) return UnityVersionStream.LTS;
            return UnityVersionStream.Tech; // Default to Tech if no identifier is found
        }

        /// <summary>
        /// Extracts the Unity version from the given URL.
        /// </summary>
        /// <param name="url">The URL to parse.</param>
        /// <returns>The Unity version string.</returns>
        private static string ExtractVersionFromUrl(string url)
        {
            try
            {
                var versionStart = url.LastIndexOf('#') + 1;
                return versionStart > 0 && versionStart < url.Length ? url.Substring(versionStart) : null;
            }
            catch
            {
                return null;
            }
        }

        public static async Task<string> FetchDownloadUrl(string unityVersion)
        {
            if (string.IsNullOrEmpty(unityVersion))
            {
                return null;
            }

            // unity release api
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

        static string ParseHashCodeFromURL(string url)
        {
            // https://beta.unity3d.com/download/330fbefc18b7/download.html#6000.1.0a8 > 330fbefc18b7

            int hashStart = url.IndexOf("download/") + 9;
            int hashEnd = url.IndexOf("/download.html", hashStart);
            return url.Substring(hashStart, hashEnd - hashStart);
        }

        private static async Task<string> ExtractDownloadUrlAsync(string json, string unityVersion)
        {
            //Console.WriteLine("json: " + json + " vers: " + unityVersion);

            if (json.Contains("\"results\":[]"))
            {
                Console.WriteLine("No results found from releases API, checking unofficial list (if enabled)");

                if (unofficialReleaseURLs.ContainsKey(unityVersion))
                {
                    Console.WriteLine("Unofficial release found in the list.");

                    string unityHash = ParseHashCodeFromURL(unofficialReleaseURLs[unityVersion]);
                    // Console.WriteLine(unityHash);
                    string downloadURL = Tools.ParseDownloadURLFromWebpage(unityVersion, unityHash, false, true);
                    // Console.WriteLine("direct download url: "+downloadURL);
                    return downloadURL;
                }

                return null;
            }

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
                    //Console.WriteLine("ExtractDownloadUrlAsync: Assistant download URL found.");
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

        private static async Task<List<UnityVersion>> FetchNewVersions(List<UnityVersion> cachedVersions)
        {
            var newVersions = new List<UnityVersion>();
            var cachedVersionSet = new HashSet<string>(cachedVersions.Select(v => v.Version));
            int offset = 0;
            int total = int.MaxValue;
            bool foundNewVersionInBatch;

            while (offset < total)
            {
                var batchUpdates = await FetchBatch(offset);
                if (batchUpdates == null || batchUpdates.Count == 0) break;

                foundNewVersionInBatch = false;

                foreach (var version in batchUpdates)
                {
                    if (!cachedVersionSet.Contains(version.Version))
                    {
                        newVersions.Add(version);
                        foundNewVersionInBatch = true;
                    }
                }

                if (!foundNewVersionInBatch)
                {
                    // Exit if no new versions are found in the current batch
                    break;
                }

                offset += batchUpdates.Count;

                // Apply delay if reaching batch limit
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
                    //Console.WriteLine(version.Version);
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
            //Console.WriteLine("Saving cachedrelease: " + cacheFilePath);
            File.WriteAllText(cacheFilePath, json);
        }
    }
}
