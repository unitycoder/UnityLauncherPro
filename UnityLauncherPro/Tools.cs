using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
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

        // NOTE holding alt key (when using alt+o) brings up unity project selector
        public static void LaunchProject(Project proj)
        {
            // validate
            if (proj == null) return;
            if (Directory.Exists(proj.Path) == false) return;

            Console.WriteLine("launch project " + proj.Title + " " + proj.Version);

            // there is no assets path, probably we want to create new project then
            var assetsFolder = Path.Combine(proj.Path, "Assets");
            if (Directory.Exists(assetsFolder) == false)
            {
                // TODO could ask if want to create project..?
                Directory.CreateDirectory(assetsFolder);
            }

            // when opening project, check for crashed backup scene first
            var cancelLaunch = CheckCrashBackupScene(proj.Path);
            if (cancelLaunch == true)
            {
                return;
            }

            var unityExePath = GetUnityExePath(proj.Version);
            if (unityExePath == null)
            {
                DisplayUpgradeDialog(proj, null);
                return;
            }

            // SetStatus("Launching project in Unity " + version);

            try
            {
                Process myProcess = new Process();
                var cmd = "\"" + unityExePath + "\"";
                myProcess.StartInfo.FileName = cmd;

                var unitycommandlineparameters = " -projectPath " + "\"" + proj.Path + "\"";

                string customArguments = proj.Arguments;
                if (string.IsNullOrEmpty(customArguments) == false)
                {
                    unitycommandlineparameters += " " + customArguments;
                }

                myProcess.StartInfo.Arguments = unitycommandlineparameters;
                myProcess.Start();

                if (Properties.Settings.Default.closeAfterProject)
                {
                    Environment.Exit(0);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        static bool CheckCrashBackupScene(string projectPath)
        {
            var cancelRunningUnity = false;
            var recoveryFile = Path.Combine(projectPath, "Temp", "__Backupscenes", "0.backup");
            if (File.Exists(recoveryFile))
            {
                var result = MessageBox.Show("Crash recovery scene found, do you want to copy it into Assets/_Recovery/-folder?", "UnityLauncherPro - Scene Recovery", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    var restoreFolder = Path.Combine(projectPath, "Assets", "_Recovery");
                    if (Directory.Exists(restoreFolder) == false)
                    {
                        Directory.CreateDirectory(restoreFolder);
                    }
                    if (Directory.Exists(restoreFolder) == true)
                    {
                        Int32 unixTimestamp = (Int32)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                        var uniqueFileName = "Recovered_Scene" + unixTimestamp + ".unity";
                        File.Copy(recoveryFile, Path.Combine(restoreFolder, uniqueFileName));
                        Console.WriteLine("Recovered crashed scene into: " + restoreFolder);
                    }
                    else
                    {
                        Console.WriteLine("Error: Failed to create restore folder: " + restoreFolder);
                        cancelRunningUnity = true;
                    }
                }
                else if (result == MessageBoxResult.Cancel) // dont do restore, but run Unity
                {
                    cancelRunningUnity = true;
                }
            }
            return cancelRunningUnity;
        }

        public static string GetUnityExePath(string version)
        {
            return MainWindow.unityInstalledVersions.ContainsKey(version) ? MainWindow.unityInstalledVersions[version] : null;
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
            if (string.IsNullOrEmpty(version)) return null;

            string url = "";
            if (VersionIsArchived(version) == true)
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
                if (version.Contains("2020")) padding = "";
                url = "https://unity3d.com/unity/" + whatsnew + "/" + padding + version;
            }
            else
            if (VersionIsPatch(version) == true)
            {
                url = "https://unity3d.com/unity/qa/patch-releases/" + version;
            }
            else
            if (VersionIsBeta(version) == true)
            {
                url = "https://unity3d.com/unity/beta/" + version;
            }
            else
            if (VersionIsAlpha(version) == true)
            {
                url = "https://unity3d.com/unity/alpha/" + version;
            }
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
            if (string.IsNullOrEmpty(version)) return false;

            var url = Tools.GetUnityReleaseURL(version);
            if (string.IsNullOrEmpty(url)) return false;

            Process.Start(url);
            result = true;
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

        public static void DisplayUpgradeDialog(Project proj, MainWindow owner)
        {
            UpgradeWindow modalWindow = new UpgradeWindow(proj.Version, proj.Path, proj.Arguments);

            modalWindow.ShowInTaskbar = owner == null;
            modalWindow.WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            //modalWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            modalWindow.Topmost = owner == null;
            //modalWindow.Topmost = true;
            modalWindow.ShowActivated = true;
            modalWindow.Owner = owner;
            modalWindow.ShowDialog();
            var results = modalWindow.DialogResult.HasValue && modalWindow.DialogResult.Value;

            if (results == true)
            {
                var upgradeToVersion = UpgradeWindow.upgradeVersion;
                if (string.IsNullOrEmpty(upgradeToVersion)) return;

                // get selected version to upgrade for
                Console.WriteLine("Upgrade to " + upgradeToVersion);

                // inject new version for this item
                proj.Version = upgradeToVersion;
                Tools.LaunchProject(proj);
            }
            else
            {
                Console.WriteLine("results = " + results);
            }
        }

        /// <summary>
        /// install context menu item to registry
        /// </summary>
        /// <param name="contextRegRoot"></param>
        public static void AddContextMenuRegistry(string contextRegRoot)
        {
            RegistryKey key = Registry.CurrentUser.OpenSubKey(contextRegRoot, true);

            // add folder if missing
            if (key == null)
            {
                key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Directory\Background\Shell");
            }

            if (key != null)
            {
                var appName = "UnityLauncherPro";
                key.CreateSubKey(appName);

                key = key.OpenSubKey(appName, true);
                key.SetValue("", "Open with " + appName);
                key.SetValue("Icon", "\"" + Process.GetCurrentProcess().MainModule.FileName + "\"");

                key.CreateSubKey("command");
                key = key.OpenSubKey("command", true);
                var executeString = "\"" + Process.GetCurrentProcess().MainModule.FileName + "\"";
                executeString += " -projectPath \"%V\"";
                key.SetValue("", executeString);
            }
            else
            {
                Console.WriteLine("Error> Cannot find registry key: " + contextRegRoot);
            }
        }

        /// <summary>
        /// uninstall context menu item from registry
        /// </summary>
        /// <param name="contextRegRoot"></param>
        public static void RemoveContextMenuRegistry(string contextRegRoot)
        {
            RegistryKey key = Registry.CurrentUser.OpenSubKey(contextRegRoot, true);
            if (key != null)
            {
                var appName = "UnityLauncher";
                RegistryKey appKey = Registry.CurrentUser.OpenSubKey(contextRegRoot + "\\" + appName, false);
                if (appKey != null)
                {
                    key.DeleteSubKeyTree(appName);
                    //SetStatus("Removed context menu registry items");
                }
                else
                {
                    //SetStatus("Nothing to uninstall..");
                }
            }
            else
            {
                //SetStatus("Error> Cannot find registry key: " + contextRegRoot);
            }
        }

        /// <summary>
        /// reads .git/HEAD file from the project to get current branch name
        /// </summary>
        /// <param name="projectPath"></param>
        /// <returns></returns>
        public static string ReadGitBranchInfo(string projectPath)
        {
            string results = null;
            DirectoryInfo gitDirectory = FindDir(".git", projectPath);
            if (gitDirectory != null)
            {
                string branchFile = Path.Combine(gitDirectory.FullName, "HEAD");
                if (File.Exists(branchFile))
                {
                    // removes extra end of line
                    results = string.Join(" ", File.ReadAllLines(branchFile));
                    // get branch only
                    int pos = results.LastIndexOf("/") + 1;
                    results = results.Substring(pos, results.Length - pos);
                }
            }
            return results;
        }

        /// <summary>
        /// Searches for a directory beginning with "startPath".
        /// If the directory is not found, then parent folders are searched until
        /// either it is found or the root folder has been reached.
        /// Null is returned if the directory was not found.
        /// </summary>
        /// <param name="dirName"></param>
        /// <param name="startPath"></param>
        /// <returns></returns>
        public static DirectoryInfo FindDir(string dirName, string startPath)
        {
            DirectoryInfo dirInfo = new DirectoryInfo(Path.Combine(startPath, dirName));
            while (!dirInfo.Exists)
            {
                if (dirInfo.Parent.Parent == null)
                {
                    return null;
                }
                dirInfo = new DirectoryInfo(Path.Combine(dirInfo.Parent.Parent.FullName, dirName));
            }
            return dirInfo;
        }

        /// <summary>
        /// reads LauncherArguments.txt file from ProjectSettings-folder
        /// </summary>
        /// <param name="projectPath">full project root path</param>
        /// <param name="launcherArgumentsFile">default filename is "LauncherArguments.txt"</param>
        /// <returns></returns>
        public static string ReadCustomLaunchArguments(string projectPath, string launcherArgumentsFile)
        {
            string results = null;
            string argumentsFile = Path.Combine(projectPath, "ProjectSettings", launcherArgumentsFile);
            if (File.Exists(argumentsFile) == true)
            {
                results = string.Join(" ", File.ReadAllLines(argumentsFile));
            }
            return results;
        }

        public static void SetFocusToGrid(DataGrid targetGrid, int index = -1)
        {
            if (targetGrid.Items.Count < 1) return;
            if (index == -1 && targetGrid.SelectedIndex > -1) index = targetGrid.SelectedIndex; // keep current row selected
            if (index == -1) index = 0;

            targetGrid.Focus();
            DataGridRow row = (DataGridRow)targetGrid.ItemContainerGenerator.ContainerFromIndex(index);
            if (row == null)
            {
                targetGrid.UpdateLayout();
                targetGrid.ScrollIntoView(targetGrid.Items[index]);
                row = (DataGridRow)targetGrid.ItemContainerGenerator.ContainerFromIndex(index);
            }
            row.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            targetGrid.SelectedIndex = index;
        }

    } // class
} // namespace
