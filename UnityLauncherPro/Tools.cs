using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UnityLauncherPro.Helpers;

namespace UnityLauncherPro
{
    public static class Tools
    {
        const int SW_RESTORE = 9;

        [DllImport("user32", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindow(string cls, string win);
        [DllImport("user32")]
        static extern IntPtr SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32")]
        static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32")]
        static extern bool OpenIcon(IntPtr hWnd);
        [DllImport("user32")]
        private static extern bool ShowWindow(IntPtr handle, int nCmdShow);

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
            string version = null;

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
                else // maybe its 4.x?
                {
                    versionPath = Path.Combine(path, "ProjectSettings", "ProjectSettings.asset");
                    if (File.Exists(versionPath) == true)
                    {
                        // first try if its ascii format
                        var data = File.ReadAllLines(versionPath);
                        if (data != null && data.Length > 0 && data[0].IndexOf("YAML") > -1) // we have ascii
                        {
                            // check library if available
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
                            var vertemp = Encoding.UTF8.GetString(bytes);
                            // probably failed if no dots
                            if (vertemp.IndexOf(".") > -1) version = vertemp;

                        }
                        // if still nothing, take a quess based on yaml year info, lets say 2011 is unity 3.5
                        if (string.IsNullOrEmpty(version) == true && data[1].ToLower().IndexOf("unity3d.com,2011") > -1)
                        {
                            version = "3.5.7f1";
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
            var res = fvi.ProductName.Replace("(64-bit)", "").Replace("(32-bit)", "").Replace("Unity", "").Trim();
            return res;
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
        public static Process LaunchProject(Project proj, DataGrid dataGridRef = null)
        {
            // validate
            if (proj == null) return null;
            if (Directory.Exists(proj.Path) == false) return null;

            Console.WriteLine("Launching project " + proj.Title + " at " + proj.Path);

            // check if this project path has unity already running? (from lock file or process) 
            // NOTE this check only works if previous unity instance was started while we were running
            if (ProcessHandler.IsRunning(proj.Path))
            {
                Console.WriteLine("Project is already running, lets not launch unity.. because it opens Hub");
                BringProcessToFront(ProcessHandler.Get(proj.Path));
                return null;
            }
            else
            {
                // TODO check lock file?
            }

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
                return null;
            }

            var unityExePath = GetUnityExePath(proj.Version);
            if (unityExePath == null)
            {
                DisplayUpgradeDialog(proj, null);
                return null;
            }

            // SetStatus("Launching project in Unity " + version);

            Process newProcess = new Process();

            try
            {
                var cmd = "\"" + unityExePath + "\"";
                newProcess.StartInfo.FileName = cmd;

                var unitycommandlineparameters = " -projectPath " + "\"" + proj.Path + "\"";

                string customArguments = proj.Arguments;
                if (string.IsNullOrEmpty(customArguments) == false)
                {
                    unitycommandlineparameters += " " + customArguments;
                }

                string projTargetPlatform = proj.TargetPlatform;
                if (string.IsNullOrEmpty(projTargetPlatform) == false)
                {
                    unitycommandlineparameters += " -buildTarget " + projTargetPlatform;
                }

                Console.WriteLine("Start process: " + cmd + " " + unitycommandlineparameters);

                newProcess.StartInfo.Arguments = unitycommandlineparameters;
                newProcess.EnableRaisingEvents = true;
                newProcess.Start();

                if (Properties.Settings.Default.closeAfterProject)
                {
                    Environment.Exit(0);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            // NOTE move project as first, since its opened, disabled for now, since its too jumpy..
            //MainWindow wnd = (MainWindow)Application.Current.MainWindow;
            //wnd.MoveRecentGridItem(0);

            ProcessHandler.Add(proj, newProcess);
            return newProcess;
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
            if (string.IsNullOrEmpty(version) == true) return null;
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
            else // original folder is missing, try to find parent folder that we can go into
            {
                for (int i = folder.Length - 1; i > -1; i--)
                {
                    if (folder[i] == '/')
                    {
                        if (Directory.Exists(folder.Substring(0, i)))
                        {
                            Process.Start(folder.Substring(0, i) + "/");
                            break;
                        }
                    }
                }
            }
            return false;
        }

        public static bool LaunchExplorerSelectFile(string fileName)
        {
            if (File.Exists(fileName) == true)
            {

                fileName = Path.GetFullPath(fileName);
                Process.Start("explorer.exe", string.Format("/select,\"{0}\"", fileName));
                return true;
            }
            else // file is missing, try to find parent folder that we can go into
            {
                for (int i = fileName.Length - 1; i > -1; i--)
                {
                    if (fileName[i] == '/')
                    {
                        if (Directory.Exists(fileName.Substring(0, i)))
                        {
                            Process.Start(fileName.Substring(0, i) + "/");
                            break;
                        }
                    }
                }
            }
            return false;
        }

        // run any exe
        public static bool LaunchExe(string path, string param = null)
        {
            if (string.IsNullOrEmpty(path)) return false;

            if (File.Exists(path) == true)
            {
                if (string.IsNullOrEmpty(param) == true)
                {
                    Console.WriteLine(path);
                    Process.Start(path);
                }
                else
                {
                    var newProcess = new Process();
                    try
                    {
                        newProcess.StartInfo.FileName = "\"" + path + "\"";
                        newProcess.StartInfo.Arguments = param;
                        newProcess.Start();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
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
                if (version.Contains("2018.1")) whatsnew = "whatsnew";
                if (version.Contains("2017.4.")) padding = "";
                if (version.Contains("2018.4.")) padding = "";
                if (version.Contains("2019")) padding = "";
                if (version.Contains("2020")) padding = "";
                if (version.Contains("2021")) padding = "";
                if (version.Contains("2022")) padding = "";
                if (version.Contains("2023")) padding = "";
                if (version.Contains("2024")) padding = "";
                if (version.Contains("2025")) padding = "";
                if (version.Contains("2026")) padding = "";
                if (version.Contains("2027")) padding = "";
                if (version.Contains("2028")) padding = "";
                if (version.Contains("2029")) padding = "";
                if (version.Contains("2030")) padding = "";
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

        public static bool VersionIsChinese(string version)
        {
            return version.Contains("c1");
        }

        // open release notes page in browser
        public static bool OpenReleaseNotes(string version)
        {
            bool result = false;
            if (string.IsNullOrEmpty(version)) return false;

            var url = Tools.GetUnityReleaseURL(version);
            if (string.IsNullOrEmpty(url)) return false;

            OpenURL(url);
            result = true;
            return result;
        }

        public static void OpenURL(string url)
        {
            Process.Start(url);
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
        public static string ParseDownloadURLFromWebpage(string version)
        {
            string url = "";

            using (WebClient client = new WebClient())
            {
                // get correct page url
                string website = "https://unity3d.com/get-unity/download/archive";
                if (Tools.VersionIsPatch(version)) website = "https://unity3d.com/unity/qa/patch-releases";
                if (Tools.VersionIsBeta(version)) website = "https://unity3d.com/unity/beta/" + version;
                if (Tools.VersionIsAlpha(version)) website = "https://unity3d.com/unity/alpha/" + version;

                // fix unity server problem, some pages says 404 found if no url params
                website += "?unitylauncherpro";

                string sourceHTML = null;
                // need to catch 404 error
                try
                {
                    // download page html
                    sourceHTML = client.DownloadString(website);
                }
                catch (WebException e)
                {
                    Console.WriteLine(e.Message);
                    return null;
                }

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
            string result = null;

            // add current version to list
            allAvailable.Add(currentVersion);

            // sort list
            allAvailable.Sort((s1, s2) => VersionAsInt(s2).CompareTo(VersionAsInt(s1)));

            // check version above our current version
            int currentIndex = allAvailable.IndexOf(currentVersion);
            // if its index 0, we select that row anyways later
            if (currentIndex > 0 && currentIndex < allAvailable.Count)
            {
                result = allAvailable[currentIndex - 1];
            }

            return result;
        }

        // string to integer for sorting by version 2017.1.5f1 > 2017010501
        public static int VersionAsInt(string version)
        {
            int result = 0;
            if (string.IsNullOrEmpty(version)) return result;

            // cleanup 32bit version name
            string cleanVersion = version.Replace("(32-bit)", "");

            // remove a,b,f,p
            cleanVersion = cleanVersion.Replace("a", ".");
            cleanVersion = cleanVersion.Replace("b", ".");
            cleanVersion = cleanVersion.Replace("c1", "");
            cleanVersion = cleanVersion.Replace("f", ".");
            cleanVersion = cleanVersion.Replace("p", ".");

            // split values
            string[] splitted = cleanVersion.Split('.');
            if (splitted != null && splitted.Length > 0)
            {
                int multiplier = 1;
                for (int i = 0, length = splitted.Length; i < length; i++)
                {
                    int n = int.Parse(splitted[splitted.Length - 1 - i]);
                    result += n * multiplier;
                    multiplier *= 100;
                }
            }
            return result;
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
            modalWindow.Topmost = owner == null;
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
                var proc = LaunchProject(proj);
            }
            else
            {
                //Console.WriteLine("results = " + results);
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
                var appName = "UnityLauncherPro";
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

        //public static Platform GetTargetPlatform(string projectPath)
        static string GetTargetPlatformRaw(string projectPath)
        {
            string results = null;
            //Platform results = Platform.Unknown;

            // get buildtarget from .csproj
            // <UnityBuildTarget>StandaloneWindows64:19</UnityBuildTarget>
            // get main csproj file
            var csproj = Path.Combine(projectPath, "Assembly-CSharp.csproj");
            // TODO check projname also, if no assembly-.., NOTE already checked above
            //var csproj = Path.Combine(projectPath, projectName + ".csproj");
            if (File.Exists(csproj))
            {
                var csprojtxt = File.ReadAllText(csproj);
                var csprojsplit = csprojtxt.Split(new[] { "<UnityBuildTarget>" }, StringSplitOptions.None);
                if (csprojsplit != null && csprojsplit.Length > 1)
                {
                    var endrow = csprojsplit[1].IndexOf(":");
                    if (endrow > -1)
                    {
                        //Console.WriteLine("build target: " + csprojsplit[1].Substring(0, endrow));
                        // 5.6 : win32, win64, osx, linux, linux64, ios, android, web, webstreamed, webgl, xboxone, ps4, psp2, wsaplayer, tizen, samsungtv
                        // 2017: standalone, Win, Win64, OSXUniversal, Linux, Linux64, LinuxUniversal, iOS, Android, Web, WebStreamed, WebGL, XboxOne, PS4, PSP2, WindowsStoreApps, Switch, WiiU, N3DS, tvOS, PSM
                        // 2018: standalone, Win, Win64, OSXUniversal, Linux, Linux64, LinuxUniversal, iOS, Android, Web, WebStreamed, WebGL, XboxOne, PS4, WindowsStoreApps, Switch, N3DS, tvOS
                        // 2019: Standalone, Win, Win64, OSXUniversal, Linux64, iOS, Android, WebGL, XboxOne, PS4, WindowsStoreApps, Switch, tvOS
                        // 2020: Standalone, Win, Win64, OSXUniversal, Linux64, iOS, Android, WebGL, XboxOne, PS4, WindowsStoreApps, Switch, tvOS
                        // 2021: Standalone, Win, Win64, OSXUniversal, Linux64, iOS, Android, WebGL, XboxOne, PS4, WindowsStoreApps, Switch, tvOS
                        results = csprojsplit[1].Substring(0, endrow);
                        //results = (Platform)Enum.Parse(typeof(Platform), csprojsplit[1].Substring(0, endrow));
                    }
                }
            }
            else
            {
                //Console.WriteLine("Missing csproj, cannot parse target platform: "+ projectPath);
            }

            return results;
        }

        public static string GetTargetPlatform(string projectPath)
        {
            var rawPlatformName = GetTargetPlatformRaw(projectPath);

            if (string.IsNullOrEmpty(rawPlatformName) == false && GetProjects.remapPlatformNames.ContainsKey(rawPlatformName))
            {
                return GetProjects.remapPlatformNames[rawPlatformName];
            }
            else
            {
                if (string.IsNullOrEmpty(rawPlatformName) == false) Console.WriteLine("Missing buildTarget remap name for: " + rawPlatformName);
                return null;
            }
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
            // set main component focus
            //targetGrid.Focus();
            //Keyboard.Focus(targetGrid);

            // no items
            if (targetGrid.Items.Count < 1) return;

            // keep current row selected
            if (index == -1 && targetGrid.SelectedIndex > -1) index = targetGrid.SelectedIndex;

            // if no item selected, pick first
            if (index == -1) index = 0;

            targetGrid.SelectedIndex = index;

            // set full focus
            DataGridRow row = (DataGridRow)targetGrid.ItemContainerGenerator.ContainerFromIndex(index);
            if (row == null)
            {
                targetGrid.UpdateLayout();
                // scroll to view if outside
                targetGrid.ScrollIntoView(targetGrid.Items[index]);
                row = (DataGridRow)targetGrid.ItemContainerGenerator.ContainerFromIndex(index);
            }
            // NOTE does this causes move below?
            //row.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            row.MoveFocus(new TraversalRequest(FocusNavigationDirection.Up)); // works better than Up

            row.Focus();
            Keyboard.Focus(row);
        }

        public static string BrowseForOutputFolder(string title)
        {
            // https://stackoverflow.com/a/50261723/5452781
            // Create a "Save As" dialog for selecting a directory (HACK)
            var dialog = new Microsoft.Win32.SaveFileDialog();
            //dialog.InitialDirectory = "c:"; // Use current value for initial dir
            dialog.Title = title;
            dialog.Filter = "Project Folder|*.Folder"; // Prevents displaying files
            dialog.FileName = "Project"; // Filename will then be "select.this.directory"
            if (dialog.ShowDialog() == true)
            {
                string path = dialog.FileName;
                // Remove fake filename from resulting path
                path = path.Replace("\\Project.Folder", "");
                path = path.Replace("Project.Folder", "");
                // If user has changed the filename, create the new directory
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                return path;
            }
            return null;
        }

        public static void FastCreateProject(string version, string baseFolder, string projectName = null, string templateZipPath = null)
        {
            // check for base folders in settings tab
            if (string.IsNullOrEmpty(baseFolder) == true)
            {
                Console.WriteLine("Missing baseFolder value");
                return;
            }

            // check if base folder exists
            if (Directory.Exists(baseFolder) == false)
            {
                Console.WriteLine("Missing baseFolder: " + baseFolder);
                return;
            }

            // check selected unity version
            if (string.IsNullOrEmpty(version) == true)
            {
                Console.WriteLine("Missing unity version");
                return;
            }

            string newPath = null;
            // if we didnt have name yet
            if (string.IsNullOrEmpty(projectName) == true)
            {
                Console.WriteLine(version);
                Console.WriteLine(baseFolder);
                projectName = GetSuggestedProjectName(version, baseFolder);
                // failed getting new path a-z
                if (projectName == null) return;
            }
            newPath = Path.Combine(baseFolder, projectName);

            // create folder
            CreateEmptyProjectFolder(newPath, version);

            // unzip template, if any
            if (templateZipPath != null)
            {
                TarLib.Tar.ExtractTarGz(templateZipPath, newPath);
            }

            // launch empty project
            var proj = new Project();
            proj.Path = Path.Combine(baseFolder, newPath);
            proj.Version = version;
            var proc = LaunchProject(proj);
            ProcessHandler.Add(proj, proc);
        } // FastCreateProject


        public static string GetSuggestedProjectName(string version, string baseFolder)
        {
            // check for base folders in settings tab, could use currently selected project folder parent as base?
            if (string.IsNullOrEmpty(baseFolder))
            {
                Console.WriteLine("Missing txtRootFolderForNewProjects");
                return null;
            }

            // find next free folder checking all "unityversion_a-z" characters
            var unityBaseVersion = version.Substring(0, version.IndexOf('.'));
            for (int i = 97; i < 122; i++)
            {
                var newProject = unityBaseVersion + "_" + ((char)i);
                var path = Path.Combine(baseFolder, newProject);

                if (Directory.Exists(path))
                {
                    //Console.WriteLine("directory exists..trying again");
                }
                else // its available
                {
                    return newProject;
                }
            }
            return null;
        }

        static void CreateEmptyProjectFolder(string path, string version)
        {
            Console.WriteLine("Create new project folder: " + path);
            Directory.CreateDirectory(path);

            // create project version file, to avoid wrong version warning
            var settingsPath = Path.Combine(path, "ProjectSettings");
            Directory.CreateDirectory(settingsPath);
            var settingsFile = Path.Combine(settingsPath, "ProjectVersion.txt");
            File.WriteAllText(settingsFile, "m_EditorVersion: " + version);
        }

        public static string GetEditorLogsFolder()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Unity", "Editor");
        }

        public static string[] GetPlatformsForUnityVersion(string version)
        {
            // get platforms array for this unity version
            // TODO use dictionary instead of looping versions
            for (int i = 0; i < MainWindow.unityInstallationsSource.Length; i++)
            {
                if (MainWindow.unityInstallationsSource[i].Version == version)
                {
                    return MainWindow.unityInstallationsSource[i].Platforms;
                }
            }
            return null;
        }

        // https://stackoverflow.com/a/675347/5452781
        public static void SetStartupRegistry(bool state)
        {
            RegistryKey rk = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            if (state == true)
            {
                rk.SetValue(MainWindow.appName, "\"" + Process.GetCurrentProcess().MainModule.FileName + "\"");
            }
            else
            {
                rk.DeleteValue(MainWindow.appName, false);
            }
        }

        public static Dictionary<string, string> ScanTemplates(string unityInstallPath)
        {
            var items = new Dictionary<string, string>();

            // add none as default item
            items.Add("None", null);

            // get list of existing packages
            var unityPath = Path.GetDirectoryName(unityInstallPath);
            var templateFolder = Path.Combine(unityPath, "Data/Resources/PackageManager/ProjectTemplates/");
            if (Directory.Exists(templateFolder) == false) return items;

            var fileEntries = Directory.GetFiles(templateFolder).ToList();

            // process found files
            for (int i = fileEntries.Count - 1; i > -1; i--)
            {
                // check if its tgz
                if (fileEntries[i].IndexOf(".tgz") == -1)
                {
                    fileEntries.RemoveAt(i);
                }
                else
                {
                    // cleanup name
                    var name = Path.GetFileName(fileEntries[i]).Replace("com.unity.template.", "").Replace(".tgz", "");
                    items.Add(name, fileEntries[i]);
                }
            }

            return items;
        }

        // https://codereview.stackexchange.com/a/93247
        public static string GetElapsedTime(DateTime datetime)
        {
            TimeSpan ts = DateTime.Now.Subtract(datetime);

            // The trick: make variable contain date and time representing the desired timespan,
            // having +1 in each date component.
            DateTime date = DateTime.MinValue + ts;

            return ProcessPeriod(date.Year - 1, date.Month - 1, "year")
                   ?? ProcessPeriod(date.Month - 1, date.Day - 1, "month")
                   ?? ProcessPeriod(date.Day - 1, date.Hour, "day", "Yesterday")
                   ?? ProcessPeriod(date.Hour, date.Minute, "hour")
                   ?? ProcessPeriod(date.Minute, date.Second, "minute")
                   ?? ProcessPeriod(date.Second, 0, "second")
                   ?? "Right now";
        }

        private static string ProcessPeriod(int value, int subValue, string name, string singularName = null)
        {
            if (value == 0)
            {
                return null;
            }
            if (value == 1)
            {
                if (!String.IsNullOrEmpty(singularName))
                {
                    return singularName;
                }
                string articleSuffix = name[0] == 'h' ? "n" : String.Empty;
                return subValue == 0
                    ? String.Format("A{0} {1} ago", articleSuffix, name)
                    : String.Format("a{0} {1} ago", articleSuffix, name);
            }
            return subValue == 0
                ? String.Format("{0} {1}s ago", value, name)
                : String.Format("{0} {1}s ago", value, name);
        }

        public static bool ValidateDateFormat(string format)
        {
            try
            {
                String formattedDate = DateTime.Now.ToString(format);
                DateTime.Parse(formattedDate);
                return true;
            }
            catch (Exception)
            {
                //Console.WriteLine("Invalid custom datetime format: " + format);
                return false;
            }
        }

        // https://stackoverflow.com/a/37724335/5452781
        public static void BringProcessToFront(Process process)
        {
            IntPtr handle = process.MainWindowHandle;
            if (IsIconic(handle))
            {
                ShowWindow(handle, SW_RESTORE);
            }

            SetForegroundWindow(handle);
        }

    } // class
} // namespace
