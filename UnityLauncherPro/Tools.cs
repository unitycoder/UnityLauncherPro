using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Input;

namespace UnityLauncherPro
{
    public static class Tools
    {
        // returns last modified date for file (or null if cannot get it)
        public static DateTime? GetLastModifiedTime(string path)
        {
            if (File.Exists(path) == true || Directory.Exists(path) == true)
            {
                DateTime modification = File.GetLastWriteTime(path);
                return modification;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// parse project version from ProjectSettings/ data
        /// </summary>
        /// <param name="path">project base path</param>
        /// <returns></returns>
        public static string GetProjectVersion(string path)
        {
            var version = "";

            if (File.Exists(Path.Combine(path, "ProjectVersionOverride.txt")))
            {
                version = File.ReadAllText(Path.Combine(path, "ProjectVersionOverride.txt"));
            }
            else if (Directory.Exists(Path.Combine(path, "ProjectSettings")))
            {
                var versionPath = Path.Combine(path, "ProjectSettings", "ProjectVersion.txt");
                if (File.Exists(versionPath) == true) // 5.x and later
                {
                    var data = File.ReadAllLines(versionPath);

                    if (data != null && data.Length > 0)
                    {
                        var dd = data[0];
                        // check first line
                        if (dd.Contains("m_EditorVersion"))
                        {
                            var t = dd.Split(new string[] { "m_EditorVersion: " }, StringSplitOptions.None);
                            if (t != null && t.Length > 0)
                            {
                                version = t[1].Trim();
                            }
                            else
                            {
                                throw new InvalidDataException("invalid version data:" + data);
                            }
                        }
                        else
                        {
                            Console.WriteLine("Cannot find m_EditorVersion in '" + versionPath + "'.\n\nFile Content:\n" + string.Join("\n", data).ToString());
                        }
                    }
                    else
                    {
                        Console.WriteLine("Invalid projectversion data found in '" + versionPath + "'.\n\nFile Content:\n" + string.Join("\n", data).ToString());
                    }
                }
                else // maybe its 4.x
                {
                    versionPath = Path.Combine(path, "ProjectSettings", "ProjectSettings.asset");
                    if (File.Exists(versionPath) == true)
                    {
                        // first try if its ascii format
                        var data = File.ReadAllLines(versionPath);
                        if (data != null && data.Length > 0 && data[0].IndexOf("YAML") > -1)
                        {
                            // in text format, then we need to try library file instead
                            var newVersionPath = Path.Combine(path, "Library", "AnnotationManager");
                            if (File.Exists(newVersionPath) == true)
                            {
                                versionPath = newVersionPath;
                            }
                        }

                        // try to get version data out from binary asset
                        var binData = File.ReadAllBytes(versionPath);
                        if (binData != null && binData.Length > 0)
                        {
                            int dataLen = 7;
                            int startIndex = 20;
                            var bytes = new byte[dataLen];
                            for (int i = 0; i < dataLen; i++)
                            {
                                bytes[i] = binData[startIndex + i];
                            }
                            version = Encoding.UTF8.GetString(bytes);
                        }
                    }
                }
            }
            return version;
        }

        // returns unity version number string from file
        public static string GetFileVersionData(string path)
        {
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(path);
            return fvi.ProductName.Replace("(64-bit)", "").Replace("Unity", "").Trim();
        }

        public static void ExploreProjectFolder(Project proj)
        {
            if (proj != null)
            {
                if (proj.Path != null)
                {
                    if (LaunchExplorer(proj.Path) == false)
                    {
                        //SetStatus("Error> Directory not found: " + folder);
                    }
                }
            }
        }

        // opens Explorer to target folder
        public static bool LaunchExplorer(string folder)
        {
            if (Directory.Exists(folder) == true)
            {
                Process.Start(folder);
                return true;
            }
            return false;
        }

        // run any exe
        public static bool LaunchExe(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            if (File.Exists(path) == true)
            {
                Process.Start(path);
                return true;
            }
            return false;
        }


        public static string GetUnityReleaseURL(string version)
        {
            string url = "";
            if (VersionIsArchived(version))
            {
                // remove f#
                version = Regex.Replace(version, @"f.", "", RegexOptions.IgnoreCase);

                string padding = "unity-";
                string whatsnew = "whats-new";

                if (version.Contains("5.6")) padding = "";
                if (version.Contains("2017.1")) whatsnew = "whatsnew";
                if (version.Contains("2018.2")) whatsnew = "whatsnew";
                if (version.Contains("2018.3")) padding = "";
                if (version.Contains("2018.1")) whatsnew = "whatsnew"; // doesnt work
                if (version.Contains("2017.4.")) padding = ""; //  doesnt work for all versions
                if (version.Contains("2018.4.")) padding = "";
                if (version.Contains("2019")) padding = "";
                url = "https://unity3d.com/unity/" + whatsnew + "/" + padding + version;
            }
            else
            if (VersionIsPatch(version))
            {
                url = "https://unity3d.com/unity/qa/patch-releases/" + version;
            }
            else
            if (VersionIsBeta(version))
            {
                url = "https://unity3d.com/unity/beta/" + version;
            }
            else
            if (VersionIsAlpha(version))
            {
                url = "https://unity3d.com/unity/alpha/" + version;
            }

            Console.WriteLine(url);

            return url;
        }

        // if version contains *f* its archived version
        public static bool VersionIsArchived(string version)
        {
            return version.Contains("f");
        }

        public static bool VersionIsPatch(string version)
        {
            return version.Contains("p");
        }

        public static bool VersionIsBeta(string version)
        {
            return version.Contains("b");
        }

        public static bool VersionIsAlpha(string version)
        {
            return version.Contains("a");
        }

        // open release notes page in browser
        public static bool OpenReleaseNotes(string version)
        {
            bool result = false;
            var url = Tools.GetUnityReleaseURL(version);
            if (string.IsNullOrEmpty(url) == false)
            {
                Process.Start(url);
                result = true;
            }
            else
            {
            }
            return result;
        }

        public static void DownloadInBrowser(string url, string version)
        {
            string exeURL = ParseDownloadURLFromWebpage(version);

            if (string.IsNullOrEmpty(exeURL) == false)
            {
                //SetStatus("Download installer in browser: " + exeURL);
                Process.Start(exeURL);
            }
            else // not found
            {
                //SetStatus("Error> Cannot find installer executable ... opening website instead");
                url = "https://unity3d.com/get-unity/download/archive";
                Process.Start(url + "#installer-not-found---version-" + version);
            }
        }

        // parse Unity installer exe from release page
        // thanks to https://github.com/softfruit
        static string ParseDownloadURLFromWebpage(string version)
        {
            string url = "";

            using (WebClient client = new WebClient())
            {
                // get correct page url
                string website = "https://unity3d.com/get-unity/download/archive";
                if (Tools.VersionIsPatch(version)) website = "https://unity3d.com/unity/qa/patch-releases";
                if (Tools.VersionIsBeta(version)) website = "https://unity3d.com/unity/beta/" + version;
                if (Tools.VersionIsAlpha(version)) website = "https://unity3d.com/unity/alpha/" + version;

                // download html
                string sourceHTML = client.DownloadString(website);
                string[] lines = sourceHTML.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                // patch version download assistant finder
                if (Tools.VersionIsPatch(version))
                {
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].Contains("UnityDownloadAssistant-" + version + ".exe"))
                        {
                            int start = lines[i].IndexOf('"') + 1;
                            int end = lines[i].IndexOf('"', start);
                            url = lines[i].Substring(start, end - start);
                            break;
                        }
                    }
                }
                else if (Tools.VersionIsArchived(version))
                {
                    // archived version download assistant finder
                    for (int i = 0; i < lines.Length; i++)
                    {
                        // find line where full installer is (from archive page)
                        if (lines[i].Contains("UnitySetup64-" + version))
                        {
                            // take previous line, which contains download assistant url
                            string line = lines[i - 1];
                            int start = line.IndexOf('"') + 1;
                            int end = line.IndexOf('"', start);
                            url = @"https://unity3d.com" + line.Substring(start, end - start);
                            break;
                        }
                    }
                }
                else // alpha or beta version download assistant finder
                {
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].Contains("UnityDownloadAssistant.exe"))
                        {
                            int start = lines[i].IndexOf('"') + 1;
                            int end = lines[i].IndexOf('"', start);
                            url = lines[i].Substring(start, end - start) + "#version=" + version;
                            break;
                        }
                    }
                }
            }

            // didnt find installer
            if (string.IsNullOrEmpty(url))
            {
                //SetStatus("Cannot find UnityDownloadAssistant.exe for this version.");
            }
            return url;
        }


        public static string FindNearestVersion(string currentVersion, List<string> allAvailable)
        {
            if (currentVersion.Contains("2019"))
            {
                return FindNearestVersionFromSimilarVersions(currentVersion, allAvailable.Where(x => x.Contains("2019")));
            }
            if (currentVersion.Contains("2018"))
            {
                return FindNearestVersionFromSimilarVersions(currentVersion, allAvailable.Where(x => x.Contains("2018")));
            }
            if (currentVersion.Contains("2017"))
            {
                return FindNearestVersionFromSimilarVersions(currentVersion, allAvailable.Where(x => x.Contains("2017")));
            }
            return FindNearestVersionFromSimilarVersions(currentVersion, allAvailable.Where(x => !x.Contains("2017")));
        }

        private static string FindNearestVersionFromSimilarVersions(string version, IEnumerable<string> allAvailable)
        {
            Dictionary<string, string> stripped = new Dictionary<string, string>();
            var enumerable = allAvailable as string[] ?? allAvailable.ToArray();

            foreach (var t in enumerable)
            {
                stripped.Add(new Regex("[a-zA-z]").Replace(t, "."), t);
            }

            var comparableVersion = new Regex("[a-zA-z]").Replace(version, ".");
            if (!stripped.ContainsKey(comparableVersion))
            {
                stripped.Add(comparableVersion, version);
            }

            var comparables = stripped.Keys.OrderBy(x => x).ToList();
            var actualIndex = comparables.IndexOf(comparableVersion);

            if (actualIndex < stripped.Count - 1) return stripped[comparables[actualIndex + 1]];
            return null;
        }

        // https://stackoverflow.com/a/1619103/5452781
        public static KeyValuePair<TKey, TValue> GetEntry<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key)
        {
            return new KeyValuePair<TKey, TValue>(key, dictionary[key]);
        }

        public static void HandleDataGridScrollKeys(object sender, KeyEventArgs e)
        {
            /*
            DataGrid grid = sender as DataGrid;
            switch (e.Key)
            {
                case Key.Up:
                    if (grid.SelectedIndex > 0)
                    {
                        grid.SelectedIndex--;
                    }
                    // disable wrap around
                    
                    //else
                    //{
                     //   grid.SelectedIndex = grid.Items.Count - 1;
                    //}
            e.Handled = true;
            break;
                case Key.Down:
                    if (grid.SelectedIndex < grid.Items.Count)
            {
                grid.SelectedIndex++;
            }
            //grid.SelectedIndex = ++grid.SelectedIndex % grid.Items.Count;
            e.Handled = true;
            break;
        }
        grid.ScrollIntoView(grid.Items[grid.SelectedIndex]);
    */
        }
    } // class
} // namespace
