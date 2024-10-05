using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
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
            var latestCachedVersion = cachedVersions.FirstOrDefault();

            var newVersions = await FetchNewVersions(latestCachedVersion);

            var allVersions = newVersions.Concat(cachedVersions).ToList();

            if (newVersions.Count > 0)
            {
                SaveCachedVersions(allVersions);
            }

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
            JsonDocument doc = JsonDocument.Parse(responseString);
            try
            {
                var root = doc.RootElement;
                var results = root.GetProperty("results");

                if (results.GetArrayLength() > 0)
                {
                    var entry = results[0];
                    string downloadUrl = null;
                    string shortRevision = null;

                    if (entry.TryGetProperty("downloads", out var downloads) && 
                        downloads.GetArrayLength() > 0 &&
                        downloads[0].TryGetProperty("url", out var urlProperty))
                    {
                        downloadUrl = urlProperty.GetString();
                    }

                    if (entry.TryGetProperty("shortRevision", out var revisionProperty))
                    {
                        shortRevision = revisionProperty.GetString();
                    }

                    if (!string.IsNullOrEmpty(downloadUrl))
                    {
                        if (!string.IsNullOrEmpty(shortRevision))
                        {
                            var startIndex = downloadUrl.LastIndexOf(shortRevision, StringComparison.Ordinal) + shortRevision.Length + 1;
                            var endIndex = downloadUrl.Length - startIndex;
                            var assistantUrl = downloadUrl.Replace(downloadUrl.Substring(startIndex, endIndex), 
                                $"UnityDownloadAssistant-{unityVersion}.exe");
                            using (var assistantResponse = await Client.GetAsync(assistantUrl))
                            {
                                if (assistantResponse.IsSuccessStatusCode)
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
                        }
                        else
                        {
                            Console.WriteLine("ShortRevision not found, returning original download URL.");
                            return downloadUrl;
                        }
                    }
                    else
                    {
                        Console.WriteLine("No download URL found.");
                        return downloadUrl;
                    }
                }

                Console.WriteLine($"No download URL found for version {unityVersion}");
                return null;
            }
            finally
            {
                doc.Dispose();
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error fetching download URL: {e.Message}");
            return null;
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
                if (batchUpdates?.Results == null || batchUpdates.Results.Count == 0)
                    break;

                foreach (var version in batchUpdates.Results)
                {
                    if (version.Version == latestCachedVersion?.Version)
                        return newVersions;

                    newVersions.Add(version);
                }

                total = batchUpdates.Total;
                offset += batchUpdates.Results.Count;

                if (offset % (BatchSize * RequestsPerBatch) == 0)
                {
                    await Task.Delay(DelayBetweenBatches);
                }
            }

            return newVersions;
        }

        private static async Task<UnityVersionResponse> FetchBatch(int offset)
        {
            string url = $"{BaseApiUrl}?limit={BatchSize}&offset={offset}&architecture=X86_64&platform=WINDOWS";

            try
            {
                var response = await Client.GetStringAsync(url);
                return JsonSerializer.Deserialize<UnityVersionResponse>(response);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error fetching batch: {e.Message}");
                return null;
            }
        }

        private static List<UnityVersion> LoadCachedVersions()
        {
            // Check if the file is locally saved
            string configFilePath = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal).FilePath;
            string configDirectory = Path.GetDirectoryName(configFilePath);
            
            if (configDirectory != null && Path.Combine(configDirectory, CacheFileName) is string cacheFilePath)
            {
                if (File.Exists(cacheFilePath))
                {
                    var json = File.ReadAllText(cacheFilePath);
                    return JsonSerializer.Deserialize<List<UnityVersion>>(json) ?? new List<UnityVersion>();
                }
            }
            else
            {
                return new List<UnityVersion>();
            }
            
            // Take the embedded file and save it locally, then rerun this method when that is successful
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream($"{assembly.GetName().Name}.Resources.{CacheFileName}"))
            {
                if (stream == null)
                    return new List<UnityVersion>();

                using (var reader = new StreamReader(stream))
                {
                    var json = reader.ReadToEnd();
                    File.WriteAllText(cacheFilePath, json);
                    return JsonSerializer.Deserialize<List<UnityVersion>>(json) ?? new List<UnityVersion>();
                }
            }
        }
        
        private static void SaveCachedVersions(List<UnityVersion> versions)
        {
            var json = JsonSerializer.Serialize(versions);
            
            string configFilePath = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal).FilePath;
            string configDirectory = Path.GetDirectoryName(configFilePath);

            if (configDirectory != null && Path.Combine(configDirectory, CacheFileName) is string cacheFilePath)
            {
                File.WriteAllText(cacheFilePath, json);
            }
        }
    }
}