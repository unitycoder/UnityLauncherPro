using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

        // reference to already running webgl server processes and ports
        static Dictionary<int, Process> webglServerProcesses = new Dictionary<int, Process>();

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
                        // check string
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
                        // if still nothing, TODO probably could find closer version info, if know what features were added to playersettings.assets and checking serializedVersion: .. number
                    }
                }
            }

            return version;
        }

        internal static string ReadProjectName(string projectPath)
        {
            string results = null;
            var versionPath = Path.Combine(projectPath, "ProjectSettings", "ProjectSettings.asset");
            if (File.Exists(versionPath) == true) // 5.x and later
            {
                var data = File.ReadAllLines(versionPath);

                if (data != null && data.Length > 0)
                {
                    for (int i = 0; i < data.Length; i++)
                    {
                        // check row
                        if (data[i].IndexOf("productName: ") > -1)
                        {
                            var t = data[i].Split(new string[] { "productName: " }, StringSplitOptions.None);
                            if (t != null && t.Length > 0)
                            {
                                results = t[1].Trim();
                                break;
                            }
                            else
                            {
                                throw new InvalidDataException("invalid productName data:" + data);
                            }
                        }
                    }
                }
            }
            return results;
        }

        // returns unity version number string from file
        public static string GetFileVersionData(string path)
        {
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(path);
            var res = fvi.ProductName.Replace("(64-bit)", "").Replace("(32-bit)", "").Replace("Unity", "").Trim();
            return res;
        }

        public static void ExploreFolder(string path)
        {
            if (path != null)
            {
                if (LaunchExplorer(path) == false)
                {
                    //SetStatus("Error> Directory not found: " + folder);
                }
            }
        }

        // this runs before unity editor starts, so the project is not yet in registry (unless it already was there)
        public static void AddProjectToHistory(string projectPath)
        {
            // fix backslashes
            projectPath = projectPath.Replace('\\', '/');

            if (Properties.Settings.Default.projectPaths.Contains(projectPath) == false)
            {
                // TODO do we need to add as first?
                Properties.Settings.Default.projectPaths.Insert(0, projectPath);

                // remove last item, if too many
                if (Properties.Settings.Default.projectPaths.Count > MainWindow.maxProjectCount)
                {
                    Properties.Settings.Default.projectPaths.RemoveAt(Properties.Settings.Default.projectPaths.Count - 1);
                }

                //Console.WriteLine("AddProjectToHistory, count: " + Properties.Settings.Default.projectPaths.Count);

                // TODO no need to save everytime?
                Properties.Settings.Default.Save();

                // TODO need to add into recent grid also? if old items disappear?
            }
        }

        // NOTE holding alt key (when using alt+o) brings up unity project selector
        public static Process LaunchProject(Project proj, DataGrid dataGridRef = null, bool useInitScript = false, bool upgrade = false)
        {
            if (proj == null) return null;

            Console.WriteLine("Launching project " + proj?.Title + " at " + proj?.Path);

            if (Directory.Exists(proj.Path) == false) return null;

            // add this project to recent projects in preferences TODO only if enabled +40 projecs
            AddProjectToHistory(proj.Path);

            // check if this project path has unity already running? (from process) 
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

            // if its upgrade, we dont want to check current version
            if (upgrade == false)
            {
                // check if project version has changed? (list is not updated, for example pulled new version from git)
                var version = GetProjectVersion(proj.Path);
                if (string.IsNullOrEmpty(version) == false && version != proj.Version)
                {
                    Console.WriteLine("Project version has changed from " + proj.Version + " to " + version);
                    proj.Version = version;
                }
            }

            // check if we have this unity version installed
            var unityExePath = GetUnityExePath(proj.Version);
            if (unityExePath == null)
            {
                DisplayUpgradeDialog(proj, null, useInitScript);
                return null;
            }

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

                if (useInitScript == true)
                {
                    unitycommandlineparameters += " -executeMethod UnityLauncherProTools.InitializeProject.Init";
                }

                Console.WriteLine("Start process: " + cmd + " " + unitycommandlineparameters);

                // TODO load custom settings per project
                //string userSettingsFolder = Path.Combine(proj.Path, "UserSettings");
                //string userSettingsPath = Path.Combine(userSettingsFolder, "ULPSettings.txt");
                //if (File.Exists(userSettingsPath))
                //{
                //    var rawSettings = File.ReadAllLines(userSettingsPath);
                //    // needed for env vars.
                //    newProcess.StartInfo.UseShellExecute = false;
                //    foreach (var row in rawSettings)
                //    {
                //        var split = row.Split('=');
                //        if (split.Length == 2)
                //        {
                //            var key = split[0].Trim();
                //            var value = split[1].Trim();
                //            if (string.IsNullOrEmpty(key) == false && string.IsNullOrEmpty(value) == false)
                //            {
                //                //Console.WriteLine("key: " + key + " value: " + value);
                //                //newProcess.StartInfo.EnvironmentVariables[key] = value;
                //                //System.Environment.SetEnvironmentVariable(key, value, EnvironmentVariableTarget.Machine);
                //                var dict = newProcess.StartInfo.EnvironmentVariables;
                //                // print all
                //                foreach (System.Collections.DictionaryEntry de in dict)
                //                {
                //                    Console.WriteLine("  {0} = {1}", de.Key, de.Value);
                //                }
                //                // check if key exists
                //                if (dict.ContainsKey(key) == true)
                //                {
                //                    // modify existing
                //                    //dict[key] = value;
                //                    newProcess.StartInfo.EnvironmentVariables.Remove(key);
                //                    newProcess.StartInfo.EnvironmentVariables.Add(key, value);
                //                }
                //                else
                //                {
                //                    // add new
                //                    dict.Add(key, value);
                //                }
                //                //newProcess.StartInfo.EnvironmentVariables.
                //                //if (newProcess.StartInfo.EnvironmentVariables.ContainsKey(key))
                //                //{
                //                //    Console.WriteLine("exists: "+key);
                //                //    // Test Modify the existing environment variable
                //                //    newProcess.StartInfo.EnvironmentVariables[key] = "...";
                //                // this works, maybe because its not a system variable?
                //                //newProcess.StartInfo.EnvironmentVariables["TESTTEST"] = "...";
                //                //}
                //                //else
                //                //{
                //                //    Console.WriteLine("add new: "+ value);
                //                //    // Optionally, add the environment variable if it does not exist
                //                //    newProcess.StartInfo.EnvironmentVariables.Add(key, value);
                //                //}
                //                Console.WriteLine("custom row: " + row + " key=" + key + " value:" + value);
                //            }
                //        }
                //    }
                //}

                newProcess.StartInfo.Arguments = unitycommandlineparameters;
                newProcess.EnableRaisingEvents = true;
                //newProcess.StartInfo.WindowStyle = ProcessWindowStyle.Hidden; // needed for unity 2023 for some reason? (otherwise console popups briefly), Cannot use this, whole Editor is invisible then
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
                    // TODO path.separator
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

        // run any exe, return process
        public static Process LaunchExe(string path, string param = null)
        {
            if (string.IsNullOrEmpty(path)) return null;

            // not needed for exe's in PATH
            //if (File.Exists(path) == true)
            {
                Process newProcess = null;
                if (string.IsNullOrEmpty(param) == true)
                {
                    Console.WriteLine("LaunchExe= " + path);
                    newProcess = Process.Start(path);
                }
                else
                {
                    Console.WriteLine("LaunchExe= " + path + " param=" + param);

                    try
                    {
                        newProcess = new Process();
                        newProcess.StartInfo.FileName = "\"" + path + "\"";
                        newProcess.StartInfo.Arguments = param;
                        newProcess.EnableRaisingEvents = true; // needed to get Exited event
                        newProcess.Start();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
                return newProcess;
            }
            Console.WriteLine("Failed to run exe: " + path + " " + param);
            return null;
        }


        public static string GetUnityReleaseURL(string version)
        {
            if (string.IsNullOrEmpty(version)) return null;

            string url = "";
            if (VersionIsArchived(version) == true)
            {
                // remove f#, TODO should remove c# from china version ?
                version = Regex.Replace(version, @"f[0-9]{1,2}", "", RegexOptions.IgnoreCase);

                string padding = "unity-";
                string whatsnew = "whats-new";

                if (version.Contains("5.6")) padding = "";
                if (version.Contains("2018.2")) whatsnew = "whatsnew";
                if (version.Contains("2018.3")) padding = "";
                if (version.Contains("2018.1")) whatsnew = "whatsnew";
                if (version.Contains("2017.4.")) padding = "";
                if (version.Contains("2018.4.")) padding = "";

                // later versions seem to follow this
                var year = int.Parse(version.Split('.')[0]);
                if (year >= 2019) padding = "";

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

        public static void DownloadInBrowser(string url, string version, bool preferFullInstaller = false)
        {
            string exeURL = ParseDownloadURLFromWebpage(version, preferFullInstaller);

            Console.WriteLine("download exeURL= (" + exeURL + ")");

            if (string.IsNullOrEmpty(exeURL) == false && exeURL.StartsWith("https") == true)
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

        public static void DownloadAndInstall(string url, string version)
        {
            string exeURL = ParseDownloadURLFromWebpage(version);

            Console.WriteLine("download exeURL= (" + exeURL + ")");

            if (string.IsNullOrEmpty(exeURL) == false && exeURL.StartsWith("https") == true)
            {
                //SetStatus("Download installer in browser: " + exeURL);
                // download url file to temp
                string tempFile = Path.GetTempPath() + "UnityDownloadAssistant-" + version.Replace(".", "_") + ".exe";
                //Console.WriteLine("download tempFile= (" + tempFile + ")");
                if (File.Exists(tempFile) == true) File.Delete(tempFile);

                // TODO make async
                if (DownloadFile(exeURL, tempFile) == true)
                {
                    // get base version, to use for install path
                    string outputVersionFolder = "\\" + version.Split('.')[0] + "_" + version.Split('.')[1];
                    string targetPathArgs = " /D=" + Properties.Settings.Default.rootFolders[Properties.Settings.Default.rootFolders.Count - 1] + outputVersionFolder; ;

                    // if user clicks NO to UAC, this fails (so added try-catch)
                    try
                    {
                        Process process = new Process();
                        process.StartInfo.FileName = tempFile;
                        process.StartInfo.Arguments = targetPathArgs;
                        process.EnableRaisingEvents = true;
                        process.Exited += (sender, e) => DeleteTempFile(tempFile);
                        process.Start();
                    }
                    catch (Exception)
                    {
                        Console.WriteLine("Failed to run exe: " + tempFile);
                        DeleteTempFile(tempFile);
                    }
                    // TODO refresh upgrade dialog after installer finished
                }
            }
            else // not found
            {
                //SetStatus("Error> Cannot find installer executable ... opening website instead");
                url = "https://unity3d.com/get-unity/download/archive";
                Process.Start(url + "#installer-not-found---version-" + version);
            }
        }

        static readonly string initFileDefaultURL = "https://raw.githubusercontent.com/unitycoder/UnityInitializeProject/main/Assets/Editor/InitializeProject.cs";

        public static void DownloadInitScript(string currentInitScriptFullPath, string currentInitScriptLocationOrURL)
        {
            string currentInitScriptFolder = Path.GetDirectoryName(currentInitScriptFullPath);
            string currentInitScriptFile = Path.GetFileName(currentInitScriptFullPath);
            string tempFile = Path.Combine(Path.GetTempPath(), currentInitScriptFile);
            bool isLocalFile = false;

            if (string.IsNullOrEmpty(currentInitScriptLocationOrURL) == true) currentInitScriptLocationOrURL = initFileDefaultURL;

            // check if its URL or local file
            if (currentInitScriptLocationOrURL.ToLower().StartsWith("http") == true)
            {
                // download into temp first
                if (DownloadFile(currentInitScriptLocationOrURL, tempFile) == false) return;
            }
            else // file is in local folders/drives/projects
            {
                // check if file exists
                if (File.Exists(currentInitScriptLocationOrURL) == false) return;

                tempFile = currentInitScriptLocationOrURL;
                isLocalFile = true;
            }

            // if got file
            if (File.Exists(tempFile) == true)
            {
                // just in case file is locked
                try
                {
                    // small validation to check if its valid editor script
                    var tempContent = File.ReadAllText(tempFile);
                    if (tempContent.IndexOf("public class InitializeProject") > 0 && tempContent.IndexOf("namespace UnityLauncherProTools") > 0 && tempContent.IndexOf("public static void Init()") > 0)
                    {
                        // create scripts folder if missing
                        if (Directory.Exists(currentInitScriptFolder) == false) Directory.CreateDirectory(currentInitScriptFolder);

                        // move old file as backup
                        if (File.Exists(currentInitScriptFullPath))
                        {
                            string oldScriptFullPath = Path.Combine(currentInitScriptFolder, currentInitScriptFile + ".bak");
                            if (File.Exists(oldScriptFullPath)) File.Delete(oldScriptFullPath);
                            File.Move(currentInitScriptFullPath, oldScriptFullPath);
                        }
                        // move new file here (need to delete old to overwrite)
                        if (File.Exists(currentInitScriptFullPath)) File.Delete(currentInitScriptFullPath);

                        // local file copy, not move
                        if (isLocalFile == true)
                        {
                            File.Copy(tempFile, currentInitScriptFullPath);
                        }
                        else
                        {
                            File.Move(tempFile, currentInitScriptFullPath);
                        }
                    }
                    else
                    {
                        Console.WriteLine("Invalid c# init file..(missing correct Namespace, Class or Method)");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("File exception: " + e.Message);
                }
            }
            else
            {
                Console.WriteLine("Failed to download init script from: " + currentInitScriptLocationOrURL);
            }
        }

        static void DeleteTempFile(string path)
        {
            if (File.Exists(path) == true)
            {
                Console.WriteLine("DeleteTempFile: " + path);
                File.Delete(path);
            }
        }

        static bool DownloadFile(string url, string tempFile)
        {
            bool result = false;
            try
            {
                using (WebClient client = new WebClient())
                {
                    client.DownloadFile(url, tempFile);
                    // TODO check if actually exists
                    result = true;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Error> DownloadFile: " + e);
            }
            return result;
        }

        // parse Unity installer exe from release page
        // thanks to https://github.com/softfruit
        public static string ParseDownloadURLFromWebpage(string version, bool preferFullInstaller = false)
        {
            string url = "";

            if (string.IsNullOrEmpty(version)) return null;

            using (WebClient client = new WebClient())
            {
                // get correct page url
                string website = "https://unity3d.com/get-unity/download/archive";
                if (VersionIsPatch(version)) website = "https://unity3d.com/unity/qa/patch-releases";
                if (VersionIsBeta(version)) website = "https://unity.com/releases/editor/beta/" + version;
                if (VersionIsAlpha(version)) website = "https://unity.com/releases/editor/alpha/" + version;

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
                if (VersionIsPatch(version))
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
                else if (VersionIsArchived(version))
                {
                    // archived version download assistant finder
                    for (int i = 0; i < lines.Length; i++)
                    {
                        // find line where full installer is (from archive page)
                        if (lines[i].Contains("UnitySetup64-" + version))
                        {
                            // take full exe installer line, to have changeset hash, then replace with download assistant filepath
                            string line = lines[i];
                            int start = line.IndexOf('"') + 1;
                            int end = line.IndexOf('"', start);
                            url = line.Substring(start, end - start);
                            url = url.Replace("Windows64EditorInstaller/UnitySetup64-", "UnityDownloadAssistant-");
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
                            // parse download url from this html line
                            string pattern = @"https://beta\.unity3d\.com/download/[a-zA-Z0-9]+/UnityDownloadAssistant\.exe";
                            Match match = Regex.Match(lines[i], pattern);
                            if (match.Success)
                            {
                                url = match.Value;
                                //Console.WriteLine("url= " + url);
                            }
                            break;
                        }
                    }
                }
            }

            // download full installer instead
            if (preferFullInstaller)
            {
                url = url.Replace("UnityDownloadAssistant-" + version + ".exe", "Windows64EditorInstaller/UnitySetup64-" + version + ".exe");
                // handle alpha/beta
                url = url.Replace("UnityDownloadAssistant.exe", "Windows64EditorInstaller/UnitySetup64-" + version + ".exe");
            }

            // didnt find installer
            if (string.IsNullOrEmpty(url))
            {
                //SetStatus("Cannot find UnityDownloadAssistant.exe for this version.");
                Console.WriteLine("Installer not found from URL..");
            }
            return url;
        }

        public static string FindNearestVersion(string currentVersion, List<string> allAvailable)
        {
            string result = null;

            // add current version to list, to sort it with others
            allAvailable.Add(currentVersion);

            // sort list
            allAvailable.Sort((s1, s2) => VersionAsLong(s2).CompareTo(VersionAsLong(s1)));

            // check version above our current version
            int currentIndex = allAvailable.IndexOf(currentVersion);
            // if its index 0, we select that row anyways later
            if (currentIndex > 0 && currentIndex < allAvailable.Count)
            {
                result = allAvailable[currentIndex - 1];
            }

            return result;
        }

        // returns version as integer, for easier sorting between versions: 2019.4.19f1 = 2019041901
        public static long VersionAsLong(string version)
        {
            long result = 0;

            // cleanup 32bit version name, TODO is this needed anymore?
            string cleanVersion = version.Replace("(32-bit)", "");

            // remove a (alpha),b (beta),f (final?),p (path),c (china final)
            cleanVersion = cleanVersion.Replace("a", ".1.");
            cleanVersion = cleanVersion.Replace("b", ".2.");
            cleanVersion = cleanVersion.Replace("c", ".3."); // NOTE this was 'c1'
            cleanVersion = cleanVersion.Replace("f", ".4.");
            cleanVersion = cleanVersion.Replace("p", ".5.");

            // split values
            string[] splitted = cleanVersion.Split('.');
            if (splitted.Length > 1)
            {
                long multiplier = 1;
                for (long i = 0, length = splitted.Length; i < length; i++)
                {
                    long n = int.Parse(splitted[length - 1 - i]);
                    result += n * multiplier;
                    multiplier *= 50;
                }
            }
            return result;
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

        // NOTE this doesnt modify the 2nd line in ProjectVersion.txt
        static void SaveProjectVersion(Project proj)
        {
            var settingsPath = Path.Combine(proj.Path, "ProjectSettings", "ProjectVersion.txt");
            if (File.Exists(settingsPath))
            {
                var versionRows = File.ReadAllLines(settingsPath);
                versionRows[0] = "m_EditorVersion: " + proj.Version;
                File.WriteAllLines(settingsPath, versionRows);
            }
        }

        public static void DisplayUpgradeDialog(Project proj, MainWindow owner, bool useInitScript = false)
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

                // inject new version for this item, TODO inject version to ProjectSettings file, so then no alert from unity wrong version dialog
                proj.Version = upgradeToVersion;
                SaveProjectVersion(proj);
                var proc = LaunchProject(proj, dataGridRef: null, useInitScript: false, upgrade: true);

                // TODO update datagrid row for new version
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
            string dirName = Path.Combine(projectPath, ".git");
            if (Directory.Exists(dirName))
            {
                string branchFile = Path.Combine(dirName, "HEAD");
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

        public static string ReadPlasticBranchInfo(string projectPath)
        {
            string results = null;
            string dirName = Path.Combine(projectPath, ".plastic");
            if (Directory.Exists(dirName))
            {
                string branchFile = Path.Combine(dirName, "plastic.selector");
                if (File.Exists(branchFile))
                {
                    // removes extra end of line
                    results = string.Join(" ", File.ReadAllText(branchFile));
                    // get branch only
                    int pos = results.LastIndexOf("\"/") + 1;
                    // -1 to remove last "
                    results = results.Substring(pos, results.Length - pos - 1);
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

        public static string ReadCustomProjectData(string projectPath, string customFile)
        {
            string results = null;
            customFile = Path.Combine(projectPath, "ProjectSettings", customFile);
            if (File.Exists(customFile) == true)
            {
                results = string.Join(" ", File.ReadAllLines(customFile));
            }
            return results;
        }

        public static bool SaveCustomProjectData(string projectPath, string customFile, string data)
        {
            customFile = Path.Combine(projectPath, "ProjectSettings", customFile);

            try
            {
                File.WriteAllText(customFile, data);
                return true;
            }
            catch (Exception)
            {
            }

            return false;
        }

        public static bool HasFocus(DependencyObject obj, Control control, bool checkChildren)
        {
            var oFocused = FocusManager.GetFocusedElement(obj) as DependencyObject;
            if (!checkChildren)
                return oFocused == control;
            while (oFocused != null)
            {
                if (oFocused == control)
                    return true;
                oFocused = VisualTreeHelper.GetParent(oFocused);
            }
            return false;
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
                if (index < targetGrid.Items.Count)
                {
                    // scroll selected into view
                    targetGrid.ScrollIntoView(targetGrid.Items[index]);
                    row = (DataGridRow)targetGrid.ItemContainerGenerator.ContainerFromIndex(index);
                }
                else
                {
                    Console.WriteLine("selected row out of bounds: " + index);
                }
            }
            // NOTE does this causes move below?
            //row.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            if (row != null)
            {
                row.MoveFocus(new TraversalRequest(FocusNavigationDirection.Up)); // works better than Up
                row.Focus();
                Keyboard.Focus(row);
            }
        }

        public static string BrowseForOutputFolder(string title, string initialDirectory = null)
        {
            // https://stackoverflow.com/a/50261723/5452781
            // Create a "Save As" dialog for selecting a directory (HACK)
            var dialog = new SaveFileDialog();
            if (initialDirectory != null) dialog.InitialDirectory = initialDirectory;
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

        // TODO too many params..
        public static Project FastCreateProject(string version, string baseFolder, string projectName = null, string templateZipPath = null, string[] platformsForThisUnity = null, string platform = null, bool useInitScript = false, string initScriptPath = null)
        {
            // check for base folders in settings tab
            if (string.IsNullOrEmpty(baseFolder) == true)
            {
                Console.WriteLine("Missing baseFolder value");
                return null;
            }

            // check if base folder exists
            if (Directory.Exists(baseFolder) == false)
            {
                Console.WriteLine("Missing baseFolder: " + baseFolder);
                return null;
            }

            // check selected unity version
            if (string.IsNullOrEmpty(version) == true)
            {
                Console.WriteLine("Missing unity version string");
                return null;
            }

            string newPath = null;
            // if we didnt have name yet
            if (string.IsNullOrEmpty(projectName) == true)
            {
                projectName = GetSuggestedProjectName(version, baseFolder);
                // failed getting new path a-z
                if (projectName == null) return null;
            }
            newPath = Path.Combine(baseFolder, projectName);

            // create folder
            CreateEmptyProjectFolder(newPath, version);

            // unzip template, if any
            if (templateZipPath != null)
            {
                TarLib.Tar.ExtractTarGz(templateZipPath, newPath);
            }

            // copy init file into project
            if (useInitScript == true)
            {
                if (File.Exists(initScriptPath) == true)
                {
                    var editorTargetFolder = Path.Combine(baseFolder, projectName, "Assets", "Editor");
                    if (Directory.Exists(editorTargetFolder) == false) Directory.CreateDirectory(editorTargetFolder);
                    var targetScriptFile = Path.Combine(editorTargetFolder, Path.GetFileName(initScriptPath));
                    // TODO overwrite old file, there shouldnt be anything here
                    if (File.Exists(targetScriptFile) == false) File.Copy(initScriptPath, targetScriptFile);
                }
            }

            // launch empty project
            var proj = new Project();
            proj.Title = projectName;
            proj.Path = Path.Combine(baseFolder, newPath).Replace("\\", "/");
            proj.Version = version;
            proj.TargetPlatforms = platformsForThisUnity;
            proj.TargetPlatform = platform;
            proj.Modified = DateTime.Now;
            proj.folderExists = true; // have to set this value, so item is green on list

            var proc = LaunchProject(proj, null, useInitScript);
            ProcessHandler.Add(proj, proc);

            return proj;
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
            var unityBaseVersion = version.Substring(0, version.LastIndexOf('.'));
            unityBaseVersion = unityBaseVersion.Replace(".", "_");
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
            // couldnt find free letter to use, lets add timestamp then
            return unityBaseVersion + "_" + DateTime.Now.ToString("ddMMyyyy_HHmmss");
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
            for (int i = 0; i < MainWindow.unityInstallationsSource.Count; i++)
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

        // chatgpt
        public static string GetElapsedTime(DateTime datetime)
        {
            TimeSpan ts = DateTime.Now - datetime;

            if (ts.TotalSeconds < 60)
            {
                return ts.TotalSeconds < 2 ? "Right now" : $"{(int)ts.TotalSeconds} seconds ago";
            }
            else if (ts.TotalMinutes < 60)
            {
                return ts.TotalMinutes < 2 ? "1 minute ago" : $"{(int)ts.TotalMinutes} minutes ago";
            }
            else if (ts.TotalHours < 24)
            {
                return ts.TotalHours < 2 ? "1 hour ago" : $"{(int)ts.TotalHours} hours ago";
            }
            else if (ts.TotalDays < 30)
            {
                return ts.TotalDays < 2 ? "1 day ago" : $"{(int)ts.TotalDays} days ago";
            }
            else if (ts.TotalDays < 365)
            {
                if (ts.TotalDays < 60)
                {
                    return "1 month ago";
                }
                else
                {
                    return $"{(int)(ts.TotalDays / 30)} months ago";
                }
            }
            else
            {
                return ts.TotalDays < 730 ? "1 year ago" : $"{(int)(ts.TotalDays / 365)} years ago";
            }
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

        public static void DownloadAdditionalModules(string UnityExePath, string unityVersion, string moduleName)
        {
            var editorFolder = Path.GetDirectoryName(UnityExePath);

            string hash = null;

            // get from unity exe (only for 2018.4 and later?)
            var versionInfo = FileVersionInfo.GetVersionInfo(UnityExePath);
            var versionRaw = versionInfo.ProductVersion.Split('_');
            if (versionRaw.Length == 2)
            {
                hash = versionRaw[1];
            }
            else // try other files
            {
                var changeSetFile = Path.Combine(editorFolder, @"Data\PlaybackEngines\windowsstandalonesupport\Source\WindowsPlayer\WindowsPlayer\UnityConfigureRevision.gen.h");
                if (File.Exists(changeSetFile) == true)
                {
                    var allText = File.ReadAllText(changeSetFile);
                    var hashRaw = allText.Split(new string[] { "#define UNITY_VERSION_HASH \"" }, StringSplitOptions.None);
                    if (hashRaw.Length > 1)
                    {
                        hash = hashRaw[1].Replace("\"", "");
                    }
                    else
                    {
                        Console.WriteLine("Unable to parse UNITY_VERSION_HASH from " + changeSetFile);
                    }
                }
                else
                {
                    Console.WriteLine("Changeset hash file not found: " + changeSetFile);
                }
            }

            if (hash == null) return;

            var moduleURL = "https://download.unity3d.com/download_unity/" + hash + "/TargetSupportInstaller/UnitySetup-" + moduleName + "-Support-for-Editor-" + unityVersion + ".exe";
            OpenURL(moduleURL);
        }

        public static void OpenAppdataSpecialFolder(string subfolder)
        {
            var logfolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), subfolder);
            if (Directory.Exists(logfolder) == true)
            {
                if (Tools.LaunchExplorer(logfolder) == false)
                {
                    Console.WriteLine("Cannot open folder.." + logfolder);
                }
            }
        }

        // NOTE android only at the moment
        public static void BuildProject(Project proj, Platform platform)
        {
            Console.WriteLine("Building " + proj.Title + " for " + platform);
            SetStatus("Build process started: " + DateTime.Now.ToString("HH:mm:ss"));

            // TODO use theme colors, keep list of multiple builds, if click status button show list of builds, if click for single build (show output folder)
            SetBuildStatus(Colors.Red);

            if (string.IsNullOrEmpty(proj.Path)) return;

            // create builder script template (with template string, that can be replaced with project related paths or names?)
            // copy editor build script to Assets/Editor/ folder (if already exists then what? Use UnityLauncherBuildSomething.cs name, so can overwrite..)
            var editorScriptFolder = Path.Combine(proj.Path, "Assets", "Editor");
            if (Directory.Exists(editorScriptFolder) == false) Directory.CreateDirectory(editorScriptFolder);
            // TODO check if creation failed

            // create output file for editor script
            var editorScriptFile = Path.Combine(editorScriptFolder, "UnityLauncherProBuilder.cs");

            // check build folder and create if missing
            var outputFolder = Path.Combine(proj.Path, "Builds/" + platform + "/");
            outputFolder = outputFolder.Replace('\\', '/'); // fix backslashes
            Console.WriteLine("outputFolder= " + outputFolder);
            if (Directory.Exists(outputFolder) == false) Directory.CreateDirectory(outputFolder);
            // TODO check if creation failed

            // cleanup filename from project name
            var invalidChars = Path.GetInvalidFileNameChars();
            var outputFile = String.Join("_", proj.Title.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
            // replace spaces also, for old time(r)s
            outputFile = outputFile.Replace(' ', '_');
            outputFile = Path.Combine(outputFolder, outputFile + ".apk");
            Console.WriteLine("outputFile= " + outputFile);

            // TODO move to txt resource? and later load from local custom file if exists, and later open window or add settings for build options
            // TODO different unity versions? wont work in older unitys right now
            var builderScript = @"using System.Linq;
using UnityEditor;
using UnityEngine;
public static class UnityLauncherProTools
{
    public static void BuildAndroid()
    {
        EditorUserBuildSettings.buildAppBundle = false;
        EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        var settings = new BuildPlayerOptions();
        settings.scenes = GetScenes();
        settings.locationPathName = ""###OUTPUTFILE###"";
        settings.target = BuildTarget.Android;
        settings.options = BuildOptions.None;
        var report = BuildPipeline.BuildPlayer(settings);
    }
    public static void BuildiOS() // Note need to match platform name
    {
        PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;
        var settings = new BuildPlayerOptions();
        settings.scenes = GetScenes();
        settings.locationPathName = ""###OUTPUTFOLDER###"";
        settings.target = BuildTarget.iOS;
        settings.options = BuildOptions.None;
        var report = BuildPipeline.BuildPlayer(settings);
    }
    static string[] GetScenes()
    {
        return EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
    }
}";

            // fill in project specific data
            builderScript = builderScript.Replace("###OUTPUTFILE###", outputFile); // android
            builderScript = builderScript.Replace("###OUTPUTFOLDER###", outputFolder); // ios
            Console.WriteLine("builderScript=" + builderScript);

            File.WriteAllText(editorScriptFile, builderScript);
            // TODO check if write failed

            // get selected project unity exe path
            var unityExePath = Tools.GetUnityExePath(proj.Version);
            if (unityExePath == null) return;

            // create commandline string for building and launch it
            //var buildcmd = $"\"{unityExePath}\" -quit -batchmode -nographics -projectPath \"{proj.Path}\" -executeMethod \"Builder.BuildAndroid\" -buildTarget android -logFile -";
            // TODO test without nographics : https://forum.unity.com/threads/batch-build-one-scene-is-black-works-in-normal-file-build.1282823/#post-9456524
            var buildParams = $" -quit -batchmode -nographics -projectPath \"{proj.Path}\" -executeMethod \"UnityLauncherProTools.Build{platform}\" -buildTarget {platform} -logFile \"{outputFolder}/../build.log\"";
            Console.WriteLine("buildcmd= " + buildParams);

            // launch build
            var proc = Tools.LaunchExe(unityExePath, buildParams);

            // wait for process exit then open output folder
            proc.Exited += (o, i) =>
            {
                Console.WriteLine("Build process exited: " + outputFolder);
                Tools.ExploreFolder(outputFolder);
                SetStatus("Build process finished: " + DateTime.Now.ToString("HH:mm:ss"));
                // TODO set color based on results
                SetBuildStatus(Colors.Green);
            };

        }

        // runs unity SimpleWebServer.exe and launches default Browser into project build/ folder'
        public static void LaunchWebGL(Project proj, string relativeFolder)
        {
            var projPath = proj?.Path.Replace('/', '\\');
            if (string.IsNullOrEmpty(projPath) == true) return;

            var buildPath = Path.Combine(projPath, "Builds", relativeFolder);
            if (Directory.Exists(buildPath) == false) return;

            if (MainWindow.unityInstalledVersions.ContainsKey(proj.Version) == false) return;

            // get mono and server exe paths
            var editorPath = Path.GetDirectoryName(MainWindow.unityInstalledVersions[proj.Version]);

            var monoToolsPath = Path.Combine(editorPath, "Data/MonoBleedingEdge/bin");
            if (Directory.Exists(monoToolsPath) == false) return;

            var webglToolsPath = Path.Combine(editorPath, "Data/PlaybackEngines/WebGLSupport/BuildTools");
            if (Directory.Exists(webglToolsPath) == false) return;

            var monoExe = Path.Combine(monoToolsPath, "mono.exe");
            if (File.Exists(monoExe) == false) return;

            var webExe = Path.Combine(webglToolsPath, "SimpleWebServer.exe");
            if (File.Exists(webExe) == false) return;

            int port = MainWindow.webglPort;
            if (port < 50000) port = 50000;
            if (port > 65534) port = 65534;

            // check if this project already has server running and process is not closed
            if (webglServerProcesses.ContainsKey(port) && webglServerProcesses[port].HasExited == false)
            {
                Console.WriteLine("Port found in cache: " + port + " process=" + webglServerProcesses[port]);

                // check if project matches
                if (webglServerProcesses[port].StartInfo.Arguments.IndexOf("\"" + buildPath + "\"") > -1)
                {
                    Console.WriteLine("this project already has webgl server running.. lets open browser url only");
                    // then open browser url only
                    Tools.OpenURL("http://localhost:" + port);
                    return;

                }
                else
                {
                    Console.WriteLine("Port in use, but its different project: " + port);
                    Console.WriteLine(webglServerProcesses[port].StartInfo.Arguments + " == " + "\"" + buildPath + "\"");

                    // then open new port and process
                    // -----------------------------------------------------------
                    // check if port is available https://stackoverflow.com/a/2793289
                    bool isAvailable = true;
                    IPGlobalProperties ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
                    IPEndPoint[] objEndPoints = ipGlobalProperties.GetActiveTcpListeners();

                    // NOTE instead of iterating all ports, just try to open port, if fails, open next one
                    // compare with existing ports, if available
                    for (int i = 0; i < objEndPoints.Length; i++)
                    {
                        if (objEndPoints[i].Port == port)
                        {
                            port++;
                            if (port > 65534)
                            {
                                Console.WriteLine("Failed to find open port..");
                                isAvailable = false;
                                return;
                            }
                        }
                    }

                    Console.WriteLine("Found available port: " + port);

                    if (isAvailable == false)
                    {
                        Console.WriteLine("failed to open port " + port + " (should be open already, or something else is using it?)");
                    }
                    else
                    {
                        // take process id from unity, if have it (then webserver closes automatically when unity is closed)
                        var proc = ProcessHandler.Get(proj.Path);
                        int pid = proc == null ? -1 : proc.Id;
                        var param = "\"" + webExe + "\" \"" + buildPath + "\" " + port + (pid == -1 ? "" : " " + pid); // server exe path, build folder and port

                        var webglServerProcess = Tools.LaunchExe(monoExe, param);

                        if (webglServerProcesses.ContainsKey(port))
                        {
                            Console.WriteLine("Error> Should not happen - this port is already in dictionary! port: " + port);
                        }
                        else // keep reference to this process on this port
                        {
                            // TODO how to remove process once its closed? (or unlikely to have many processes in total? can also remove during check, if process already null)
                            webglServerProcesses.Add(port, webglServerProcess);
                            Console.WriteLine("Added port " + port);
                        }

                        Tools.OpenURL("http://localhost:" + port);
                    }
                    // -----------------------------------------------------------

                }
            }
            else
            {
                Console.WriteLine("Port not running in cache or process already closed, remove it from cache: " + port);
                if (webglServerProcesses.ContainsKey(port)) webglServerProcesses.Remove(port);

                // TODO remove duplicate code
                // then open new process
                // -----------------------------------------------------------
                // check if port is available https://stackoverflow.com/a/2793289
                bool isAvailable = true;
                IPGlobalProperties ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
                IPEndPoint[] objEndPoints = ipGlobalProperties.GetActiveTcpListeners();

                // compare with existing ports, if available
                for (int i = 0; i < objEndPoints.Length; i++)
                {
                    if (objEndPoints[i].Port == port)
                    {
                        if (port > 65535)
                        {
                            Console.WriteLine("Failed to find open port..");
                            isAvailable = false;
                            return;
                        }
                        port++;
                    }
                }

                Console.WriteLine("Found available port: " + port);

                if (isAvailable == false)
                {
                    Console.WriteLine("failed to open port " + port + " (should be open already, or something else is using it?)");
                }
                else
                {
                    // take process id from unity, if have it(then webserver closes automatically when unity is closed)
                    var proc = ProcessHandler.Get(proj.Path);
                    int pid = proc == null ? -1 : proc.Id;
                    var param = "\"" + webExe + "\" \"" + buildPath + "\" " + port + (pid == -1 ? "" : " " + pid); // server exe path, build folder and port

                    var webglServerProcess = Tools.LaunchExe(monoExe, param);

                    if (webglServerProcess == null)
                    {
                        Console.WriteLine("Failed to start exe..");
                    }

                    if (webglServerProcesses.ContainsKey(port))
                    {
                        Console.WriteLine("Error> Should not happen - this port is already in dictionary! port: " + port);
                    }
                    else // keep reference to this process on this port
                    {
                        // TODO how to remove process once its closed? (or unlikely to have many processes in total? can also remove during check, if process already null)
                        webglServerProcesses.Add(port, webglServerProcess);
                        Console.WriteLine("Added port " + port);
                    }

                    Tools.OpenURL("http://localhost:" + port);
                }
                // -----------------------------------------------------------

            }
        } // LaunchWebGL()

        // creates .bat file to launch UnityLauncherPro and then .url link file on desktop, into that .bat file
        public static bool CreateDesktopShortCut(Project proj, string batchFolder)
        {
            string lnkFileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));

            if (string.IsNullOrEmpty(batchFolder)) return false;

            //string batchFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UnityLauncherPro");
            if (Directory.Exists(batchFolder) == false) Directory.CreateDirectory(batchFolder);
            string batFileName = Path.Combine(batchFolder, proj.Title + ".bat");
            string launcherExe = Process.GetCurrentProcess().MainModule.FileName;
            string args = "-projectPath " + "\"" + proj.Path + "\" " + proj.Arguments;
            string description = "Unity Project: " + proj.Title;

            // create .bat file
            var batLauncherData = "start \"\" \"" + launcherExe + "\"" + " " + args;
            File.WriteAllText(batFileName, batLauncherData);

            // create desktop link file
            using (StreamWriter writer = new StreamWriter(lnkFileName + "\\" + proj.Title + ".url"))
            {
                writer.WriteLine("[InternetShortcut]");
                writer.WriteLine("URL=file:///" + batFileName);
                //writer.WriteLine("ShowCommand=7"); // doesnt work for minimized
                writer.WriteLine("IconIndex=0");
                writer.WriteLine("Arguments=-projectPath " + proj.Path);
                // TODO maybe could take icon from project (but then need to convert into .ico)
                string iconExe = GetUnityExePath(proj.Version);
                if (iconExe == null) iconExe = launcherExe;
                string icon = iconExe.Replace('\\', '/');
                writer.WriteLine("IconFile=" + icon);
            }

            // TODO check for streamwriter and file write success

            return true;
        }


        internal static long GetFolderSizeInBytes(string currentBuildReportProjectPath)
        {
            // FIXME: 0 is not really correct for missing folder..
            if (Directory.Exists(currentBuildReportProjectPath) == false) return 0;

            return DirSize(new DirectoryInfo(currentBuildReportProjectPath));
        }

        // https://stackoverflow.com/a/468131/5452781
        static long DirSize(DirectoryInfo d)
        {
            long size = 0;
            // Add file sizes.
            FileInfo[] fis = d.GetFiles();
            foreach (FileInfo fi in fis)
            {
                size += fi.Length;
            }
            // Add subdirectory sizes.
            DirectoryInfo[] dis = d.GetDirectories();
            foreach (DirectoryInfo di in dis)
            {
                size += DirSize(di);
            }
            return size;
        }

        // Returns the human-readable file size for an arbitrary, 64-bit file size 
        // The default format is "0.### XB", e.g. "4.2 KB" or "1.434 GB"
        internal static string GetBytesReadable(long i)
        {
            // Get absolute value
            long absolute_i = (i < 0 ? -i : i);
            // Determine the suffix and readable value
            string suffix;
            double readable;
            if (absolute_i >= 0x1000000000000000) // Exabyte
            {
                suffix = "EB";
                readable = (i >> 50);
            }
            else if (absolute_i >= 0x4000000000000) // Petabyte
            {
                suffix = "PB";
                readable = (i >> 40);
            }
            else if (absolute_i >= 0x10000000000) // Terabyte
            {
                suffix = "TB";
                readable = (i >> 30);
            }
            else if (absolute_i >= 0x40000000) // Gigabyte
            {
                suffix = "GB";
                readable = (i >> 20);
            }
            else if (absolute_i >= 0x100000) // Megabyte
            {
                suffix = "MB";
                readable = (i >> 10);
            }
            else if (absolute_i >= 0x400) // Kilobyte
            {
                suffix = "KB";
                readable = i;
            }
            else
            {
                return i.ToString("0 B"); // Byte
            }
            // Divide by 1024 to get fractional value
            readable = (readable / 1024);
            // Return formatted number with suffix
            return readable.ToString("0.### ") + suffix;
        }

        public static MainWindow mainWindow;

        // set status bar in main thread
        public static void SetStatus(string text)
        {
            mainWindow.Dispatcher.Invoke(() => { mainWindow.SetStatus(text); });
        }

        public static void SetBuildStatus(Color color)
        {
            mainWindow.Dispatcher.Invoke(() => { mainWindow.SetBuildStatus(color); });
        }

        // https://unity3d.com/unity/alpha
        public static bool IsAlpha(string version)
        {
            if (string.IsNullOrEmpty(version)) return false;
            return version.IndexOf("a", 0, StringComparison.CurrentCultureIgnoreCase) > -1;
        }

        // https://unity3d.com/beta/
        public static bool IsBeta(string version)
        {
            if (string.IsNullOrEmpty(version)) return false;
            return version.IndexOf("b", 0, StringComparison.CurrentCultureIgnoreCase) > -1;
        }

        // https://unity3d.com/unity/qa/lts-releases
        public static bool IsLTS(string versionRaw)
        {
            if (string.IsNullOrEmpty(versionRaw)) return false;
            var version = versionRaw.Split('.');
            var versionInt = int.Parse(version[0]);
            var versionMinor = int.Parse(version[1]);
            return (versionInt >= 2017 && versionMinor == 4) || (versionInt > 2019 && versionMinor == 3);
        }

        internal static void UninstallEditor(string path, string version)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (string.IsNullOrEmpty(version)) return;

            // run uninstaller from path
            var installFolder = Path.GetDirectoryName(path);
            var uninstaller = Path.Combine(installFolder, "Uninstall.exe");
            // TODO could be optional setting for non-silent uninstall
            LaunchExe(uninstaller, "/S");
            // remove firewall settings
            var cmd = "netsh advfirewall firewall delete rule name=all program=\"" + path + "\"";
            Console.WriteLine("Cleanup firewall: " + cmd);
            LaunchExe("cmd.exe", "/c " + cmd);

            int year;
            string[] parts = version.Split('.');
            // TODO handle unity 6.x
            if (parts.Length >= 1 && int.TryParse(parts[0], out year) && year <= 2017)
            {
                var nodeFolder = Path.Combine(installFolder, "Editor", "Data", "Tools", "nodejs", "node.exe");
                cmd = "netsh advfirewall firewall delete rule name=all program=\"" + nodeFolder + "\"";
                Console.WriteLine("Cleanup firewall <= 2017: " + cmd);
                LaunchExe("cmd.exe", "/c " + cmd);
            }

            // remove registry entries
            var unityKeyName = "HKEY_CURRENT_USER\\Software\\Unity Technologies\\Installer\\Unity " + version;
            cmd = "reg delete " + unityKeyName + " /f";
            Console.WriteLine("Removing registry key: " + cmd);
            LaunchExe("cmd.exe", "/c " + cmd);

            // remove startmenu item
            var startMenuFolder = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
            var unityIcon = Path.Combine(startMenuFolder, "Unity " + version + "(64-bit)");
            if (Directory.Exists(unityIcon))
            {
                Console.WriteLine("Removing startmenu folder: " + unityIcon);
                Directory.Delete(unityIcon, true);
            }

            // remove desktop icon
            var desktopFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            unityIcon = Path.Combine(startMenuFolder, "Unity " + version + ".lnk");
            if (File.Exists(unityIcon))
            {
                Console.WriteLine("Removing desktop icon: " + unityIcon);
                File.Delete(unityIcon);
            }
        } // UninstallEditor

        public static void DisplayProjectProperties(Project proj, MainWindow owner)
        {
            var modalWindow = new ProjectProperties(proj);
            modalWindow.ShowInTaskbar = owner == null;
            modalWindow.WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            modalWindow.Topmost = owner == null;
            modalWindow.ShowActivated = true;
            modalWindow.Owner = owner;
            modalWindow.ShowDialog();
            var results = modalWindow.DialogResult.HasValue && modalWindow.DialogResult.Value;

            if (results == true)
            {
            }
            else
            {
            }
        }

        // TODO save custom env to proj settings?
        internal static void SaveProjectSettings(Project proj, string customEnvVars)
        {
            string userSettingsFolder = Path.Combine(proj.Path, "UserSettings");

            // save custom env file
            if (string.IsNullOrEmpty(customEnvVars) == false)
            {
                // check if UserSettings exists

                if (Directory.Exists(userSettingsFolder) == false) Directory.CreateDirectory(userSettingsFolder);

                // TODO think about settings format (other values will be added later)

                string fullPath = Path.Combine(userSettingsFolder, "ULPSettings.txt");
                File.WriteAllText(fullPath, customEnvVars);
                Console.WriteLine(fullPath);
            }
        }
    } // class

} // namespace
