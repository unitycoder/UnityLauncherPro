﻿using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
            var ver = fvi.ProductName;
            if (string.IsNullOrEmpty(ver) == true)
            {
                ver = fvi.FileDescription;
                if (string.IsNullOrEmpty(ver) == true) return null;
                ver = ver.Replace("Installer", "").Trim();
            }
            var res = ver.Replace("(64-bit)", "").Replace("(32-bit)", "").Replace("Unity", "").Trim();
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

            // when opening project, check for crashed backup scene first
            var cancelLaunch = CheckCrashBackupScene(proj.Path);
            if (cancelLaunch == true)
            {
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
                var result = MessageBox.Show("Crash recovery scene found, do you want to MOVE it into Assets/_Recovery/-folder?", "UnityLauncherPro - Scene Recovery", MessageBoxButton.YesNo, MessageBoxImage.Question);
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

                        try
                        {
                            File.Move(recoveryFile, Path.Combine(restoreFolder, uniqueFileName));
                            // remove folder, otherwise unity 6000.2 asks for recovery
                            Directory.Delete(Path.Combine(projectPath, "Temp", "__Backupscenes"), true);

                            Console.WriteLine("moved file to " + uniqueFileName);
                        }
                        catch (IOException)
                        {
                            // if move failed, try copy
                            File.Copy(recoveryFile, Path.Combine(restoreFolder, uniqueFileName));
                            Console.WriteLine("copied file");
                        }

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
        public static Process LaunchExe(string path, string param = null, bool captureOutput = false)
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
                        if (captureOutput)
                        {
                            newProcess.StartInfo.RedirectStandardError = true;
                            newProcess.StartInfo.RedirectStandardOutput = true;
                            newProcess.StartInfo.UseShellExecute = false;
                        }
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
            // Console.WriteLine("Failed to run exe: " + path + " " + param);
            // return null;
        }


        public static string GetUnityReleaseURL(string version)
        {
            if (string.IsNullOrEmpty(version)) return null;

            var cleanVersion = CleanVersionNumber(version);
            string url = $"https://unity.com/releases/editor/whats-new/{cleanVersion}#notes";

            //if (VersionIsArchived(version) == true)
            //{
            //    // remove f#, TODO should remove c# from china version ?
            //    version = Regex.Replace(version, @"f[0-9]{1,2}", "", RegexOptions.IgnoreCase);

            //    string padding = "unity-";
            //    string whatsnew = "whats-new";

            //    if (version.Contains("5.6")) padding = "";
            //    if (version.Contains("2018.2")) whatsnew = "whatsnew";
            //    if (version.Contains("2018.3")) padding = "";
            //    if (version.Contains("2018.1")) whatsnew = "whatsnew";
            //    if (version.Contains("2017.4.")) padding = "";
            //    if (version.Contains("2018.4.")) padding = "";

            //    // later versions seem to follow this
            //    var year = int.Parse(version.Split('.')[0]);
            //    if (year >= 2019) padding = "";

            //    url = "https://unity3d.com/unity/" + whatsnew + "/" + padding + version;
            //}
            //else
            //if (VersionIsPatch(version) == true)
            //{
            //    url = "https://unity3d.com/unity/qa/patch-releases/" + version;
            //}
            //else
            //if (VersionIsBeta(version) == true)
            //{
            //    url = "https://unity3d.com/unity/beta/" + version;
            //}
            //else
            //if (VersionIsAlpha(version) == true)
            //{
            //    url = "https://unity3d.com/unity/alpha/" + version;
            //}
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


        //as of 21 May 2021, only final 'f' versions are now available on the alpha release notes for Unity 2018 and newer. 2017 and 5 still have patch 'p' versions as well.
        public static bool HasAlphaReleaseNotes(string version) => VersionIsArchived(version) || VersionIsPatch(version);

        public static string GetAlphaReleaseNotesURL(string fromVersion, string toVersion = null)
            => "https://alpha.release-notes.ds.unity3d.com/search?fromVersion=" + fromVersion + "&toVersion=" + (toVersion != null ? toVersion : fromVersion);

        // open release notes page in browser
        public static bool OpenReleaseNotes(string version)
        {
            bool result = false;
            if (string.IsNullOrEmpty(version)) return false;

            string url = null;
            if (Properties.Settings.Default.useAlphaReleaseNotes && HasAlphaReleaseNotes(version))
            {
                url = GetAlphaReleaseNotesURL(version);
            }
            else
            {
                url = GetUnityReleaseURL(version);
            }

            if (string.IsNullOrEmpty(url)) return false;

            OpenURL(url);
            result = true;
            return result;
        }

        public static bool OpenReleaseNotes_Cumulative(string version)
        {
            bool result = false;
            if (string.IsNullOrEmpty(version)) return false;

            string url = null;
            var comparisonVersion = version;
            //with the alpha release notes, we want a diff between an installed version and the one selected, but the site just shows all the changes inclusive of "fromVersion=vers"
            //so if we find a good installed candidate, we need the version just above it (installed or not) that has release notes page
            var closestInstalledVersion = Tools.FindNearestVersion(version, MainWindow.unityInstalledVersions.Keys.ToList(), true);
            if (closestInstalledVersion != null)
            {
                comparisonVersion = closestInstalledVersion;
                string nextFinalVersionAfterInstalled = closestInstalledVersion;

                //wwe need a loop here, to find the nearest final version. It might be better to warn the user about this before opening the page.
                do
                    nextFinalVersionAfterInstalled = Tools.FindNearestVersion(nextFinalVersionAfterInstalled, MainWindow.updatesAsStrings);
                while (nextFinalVersionAfterInstalled != null && !HasAlphaReleaseNotes(nextFinalVersionAfterInstalled));

                if (nextFinalVersionAfterInstalled != null) comparisonVersion = nextFinalVersionAfterInstalled;

            }
            url = GetAlphaReleaseNotesURL(comparisonVersion, version);

            OpenURL(url);
            result = true;
            return result;
        }

        public static void OpenURL(string url)
        {
            Process.Start(url);
        }

        public static async void DownloadInBrowser(string version, bool preferFullInstaller = false)
        {
            if (version == null) return;
            string exeURL = await GetUnityUpdates.FetchDownloadUrl(version);

            // null from unity api? then try direct download
            // https://beta.unity3d.com/download/330fbefc18b7/UnityDownloadAssistant-6000.1.0a8.exe
            if (exeURL == null)
            {
                Console.WriteLine("TODO DownloadInBrowser");
            }

            if (preferFullInstaller == true)
            {
                exeURL = exeURL.Replace("UnityDownloadAssistant-" + version + ".exe", "Windows64EditorInstaller/UnitySetup64-" + version + ".exe");
            }

            Console.WriteLine("DownloadInBrowser exeURL= '" + exeURL + "'");

            if (string.IsNullOrEmpty(exeURL) == false && exeURL.StartsWith("https"))
            {
                //SetStatus("Download installer in browser: " + exeURL);
                Process.Start(exeURL);
            }
            else // not found
            {
                //SetStatus("Error> Cannot find installer executable ... opening website instead");
                const string url = "https://unity3d.com/get-unity/download/archive";
                Process.Start(url + "#installer-not-found-for-version-" + version);
            }
        }

        public static async void DownloadAndInstall(string version)
        {
            if (version == null)
            {
                Console.WriteLine("Error> Cannot download and install null version");
                return;
            }
            string exeURL = await GetUnityUpdates.FetchDownloadUrl(version);

            Console.WriteLine("download exeURL= (" + exeURL + ")");

            if (string.IsNullOrEmpty(exeURL) == false && exeURL.StartsWith("https") == true)
            {
                //SetStatus("Download installer in browser: " + exeURL);
                // download url file to temp
                string tempFile = Path.GetTempPath() + "UnityDownloadAssistant-" + version.Replace(".", "_") + ".exe";
                //Console.WriteLine("download tempFile= (" + tempFile + ")");
                if (File.Exists(tempFile) == true) File.Delete(tempFile);

                // TODO make async
                if (await DownloadFileAsync(exeURL, tempFile))
                {
                    // get base version, to use for install path
                    // FIXME check if have any paths?
                    string lastRootFolder = Properties.Settings.Default.rootFolders[Properties.Settings.Default.rootFolders.Count - 1];

                    // check if ends with / or \
                    if (lastRootFolder.EndsWith("/") == false && lastRootFolder.EndsWith("\\") == false) lastRootFolder += "\\";

                    string outputVersionFolder = version.Split('.')[0] + "_" + version.Split('.')[1];
                    string targetPathArgs = " /D=" + lastRootFolder + outputVersionFolder; ;

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
                var url = "https://unity3d.com/get-unity/download/archive";
                Process.Start(url + "#installer-not-found---version-" + version);
            }
        }

        static readonly string initFileDefaultURL = "https://raw.githubusercontent.com/unitycoder/UnityInitializeProject/main/Assets/Editor/InitializeProject.cs";

        public static async Task DownloadInitScript(string currentInitScriptFullPath, string currentInitScriptLocationOrURL)
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
                if (await DownloadFileAsync(currentInitScriptLocationOrURL, tempFile) == false) return;
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
                        SetStatus("Downloaded latest init script.");
                    }
                    else
                    {
                        Console.WriteLine("Invalid c# init file..(missing correct Namespace, Class or Method)");
                        SetStatus("Invalid c# init file..(missing correct Namespace, Class or Method)");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("File exception: " + e.Message);
                    SetStatus("File exception: " + e.Message);
                }
            }
            else
            {
                Console.WriteLine("Failed to download init script from: " + currentInitScriptLocationOrURL);
                SetStatus("Failed to download init script from: " + currentInitScriptLocationOrURL);
            }
        }

        public static string GetInitScriptFolder()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;

            // Check for MSIX install
            if (exeDir.Contains(@"\WindowsApps\"))
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(localAppData, "UnityLauncherPro", "Scripts");
            }
            else
            {
                return Path.Combine(exeDir, "Scripts");
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

        public static string DownloadHTML(string url)
        {
            Console.WriteLine("DownloadHTML: " + url);

            if (string.IsNullOrEmpty(url) == true) return null;
            using (WebClient client = new WebClient())
            {
                try
                {
                    // download page html
                    return client.DownloadString(url);
                }
                catch (WebException e)
                {
                    Console.WriteLine("DownloadHTML: " + e.Message);
                    return null;
                }
            }
        }

        public static string CleanVersionNumber(string version)
        {
            if (string.IsNullOrEmpty(version)) return null;

            var split = version.Split('.');
            float parsedVersion = float.Parse($"{split[0]}.{split[1]}");

            // For 2023.3 and newer pre-release (alpha or beta) versions, do not clean.
            if ((IsAlpha(version) || version.Contains("b")) && parsedVersion >= 2023.3)
            {
                // Do nothing; leave version unchanged.
            }
            else
            {
                // Remove the trailing patch/build indicator.
                version = Regex.Replace(version, @"[fab][0-9]{1,2}", "", RegexOptions.IgnoreCase);
            }
            return version;
        }


        // TODO only hash version is used, cleanup the rest
        public static string ParseDownloadURLFromWebpage(string version, string hash = null, bool preferFullInstaller = false, bool useHash = false)
        {
            string exeURL = "";

            //Console.WriteLine("ParseDownloadURLFromWebpage: " + version + ", hash: " + useHash);

            if (string.IsNullOrEmpty(version)) return null;

            // NOTE no longer uses f# in the end

            string url = null;
            if (useHash == false)
            {
                var cleanVersion = CleanVersionNumber(version);
                // NOTE 2024 June, installs are now located on separate pages, like https://unity.com/releases/editor/whats-new/6000.0.5#installs

                // get correct page url
                //url = "https://unity3d.com/get-unity/download/archive";
                // fix unity server problem, some pages says 404 found if no url params
                url = "https://unity.com/releases/editor/whats-new/" + cleanVersion + "?unitylauncherpro#installs";
                //if (VersionIsPatch(version)) url = "https://unity3d.com/unity/qa/patch-releases";
                if (VersionIsBeta(version)) url = "https://unity.com/releases/editor/beta/" + version;
                if (VersionIsAlpha(version)) url = "https://unity.com/releases/editor/alpha/" + version;
                //url += "?unitylauncherpro";
            }
            else
            {
                // NOTE version here is actually VERSION|HASH
                //string hash = version;
                url = $"https://beta.unity3d.com/download/{hash}/download.html";

                //Console.WriteLine("hashurl: " + url);

                //version = FetchUnityVersionNumberFromHTML(url);
                //Console.WriteLine(url);
                //Console.WriteLine("got "+version);
                if (string.IsNullOrEmpty(version))
                {
                    SetStatus("Failed to get version (" + version + ") number from hash: " + hash);
                    return null;
                }
            }

            //Console.WriteLine("scanning installers from url: " + url);

            //string sourceHTML = DownloadHTML(url);

            //if (string.IsNullOrEmpty(sourceHTML) == true)
            //{
            //    Console.WriteLine("Failed to download html from: " + url);
            //    return null;
            //}

            //// parse changeset hash from html
            //string pattern = $@"href=""unityhub://{version}/([^""]+)""";
            //Regex regex = new Regex(pattern);
            //Match match = regex.Match(sourceHTML);

            //if (match.Success == true)
            //{
            //    string changeSet = match.Groups[1].Value;
            //    Console.WriteLine("changeSet: " + changeSet);
            //}

            exeURL = $"https://beta.unity3d.com/download/{hash}/UnityDownloadAssistant-{version}.exe";
            //string[] lines = sourceHTML.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            // patch version download assistant finder
            //if (useHash == false && VersionIsPatch(version))
            //{
            //    for (int i = 0; i < lines.Length; i++)
            //    {
            //        //if (lines[i].Contains("UnityDownloadAssistant-" + version + ".exe"))
            //        if (lines[i].Contains("UnitySetup64-" + version + ".exe"))
            //        {
            //            int start = lines[i].IndexOf('"') + 1;
            //            int end = lines[i].IndexOf('"', start);
            //            exeURL = lines[i].Substring(start, end - start);
            //            break;
            //        }
            //    }
            //}
            //else if (useHash == false && VersionIsArchived(version))
            //{
            //    // archived version download assistant finder
            //    for (int i = 0; i < lines.Length; i++)
            //    {
            //        // find line where full installer is (from archive page)
            //        if (lines[i].Contains("UnitySetup64-" + version))
            //        {

            //            Console.WriteLine(lines[i]);

            //            // take full exe installer line, to have changeset hash, then replace with download assistant filepath
            //            string line = lines[i];
            //            int start = line.IndexOf('"') + 1;
            //            int end = line.IndexOf('"', start);
            //            exeURL = line.Substring(start, end - start);
            //            exeURL = exeURL.Replace("Windows64EditorInstaller/UnitySetup64-", "UnityDownloadAssistant-");
            //            break;
            //        }
            //    }
            //}
            //else // alpha or beta version download assistant finder
            //{
            //    // regular beta
            //    // <a href="https://beta.unity3d.com/download/21aeb48b6ed2/UnityDownloadAssistant.exe">
            //    // https://beta.unity3d.com/download/21aeb48b6ed2/UnityDownloadAssistant.exe

            //    // hidden beta
            //    // <a href='UnityDownloadAssistant-6000.0.0b15.exe'>
            //    // https://beta.unity3d.com/download/8008bc0c1b74/UnityDownloadAssistant-6000.0.0b15.exe

            //    // new 10.06.2024, no more downloadassistant.exe in html

            //    // check html lines
            //    for (int i = 0; i < lines.Length; i++)
            //    {
            //        //Console.WriteLine(lines[i]);
            //        //if (lines[i].Contains("UnityDownloadAssistant"))
            //        if (lines[i].Contains("UnityDownloadAssistant"))
            //        {
            //            if (useHash == false)
            //            {
            //                string pattern = @"https://beta\.unity3d\.com/download/[a-zA-Z0-9]+/UnityDownloadAssistant\.exe";
            //                Match match = Regex.Match(lines[i], pattern);
            //                if (match.Success)
            //                {
            //                    exeURL = match.Value;
            //                }
            //                else
            //                {
            //                    Console.WriteLine("No match found for download base url..");
            //                }
            //            }
            //            else // hidden download page
            //            {
            //                string pattern = @"UnityDownloadAssistant(?:-\d+\.\d+\.\d+[bf]\d*)?\.exe";
            //                Match match = Regex.Match(lines[i], pattern);
            //                if (match.Success)
            //                {
            //                    // append base url
            //                    Regex regex = new Regex(@"(https://beta\.unity3d\.com/download/[a-zA-Z0-9]+/)");
            //                    Match match2 = regex.Match(url);

            //                    //Console.WriteLine("source url: " + url);

            //                    if (match2.Success)
            //                    {
            //                        string capturedUrl = match2.Groups[1].Value;
            //                        exeURL = capturedUrl + match.Value;
            //                    }
            //                    else
            //                    {
            //                        Console.WriteLine("No match found for download base url..");
            //                    }
            //                }
            //                break;
            //            }
            //        }
            //    } // for lines
            //} // alpha or beta

            // download full installer instead, TODO probably not needed anymore?
            if (useHash == false && preferFullInstaller == true)
            {
                exeURL = exeURL.Replace("UnityDownloadAssistant-" + version + ".exe", "Windows64EditorInstaller/UnitySetup64-" + version + ".exe");
                // handle alpha/beta
                exeURL = exeURL.Replace("UnityDownloadAssistant.exe", "Windows64EditorInstaller/UnitySetup64-" + version + ".exe");
            }

            // didnt find installer
            if (string.IsNullOrEmpty(exeURL))
            {
                //SetStatus("Cannot find UnityDownloadAssistant.exe for this version.");
                Console.WriteLine("Installer not found from URL: " + url);
            }
            return exeURL;
        }

        private static string FetchUnityVersionNumberFromHTML(string url)
        {
            string sourceHTML = DownloadHTML(url);

            if (string.IsNullOrEmpty(sourceHTML)) return null;

            string pattern = @"\d+\.\d+\.\d+[bf]\d+";
            MatchCollection matches = Regex.Matches(sourceHTML, pattern);
            if (matches.Count > 0)
            {
                foreach (Match match in matches)
                {
                    Console.WriteLine("Extracted number: " + match.Value);
                    return match.Value;
                    //break;
                }
            }
            else
            {
                Console.WriteLine("FetchUnityVersionNumberFromHTML: No match found.");
            }

            return null;

            //if (string.IsNullOrEmpty(sourceHTML) == false)
            //{
            //    // find version number from html
            //    string pattern = @"UnityDownloadAssistant-[0-9]+\.[0-9]+\.[0-9]+[a-z]?\.exe";
            //    Match match = Regex.Match(sourceHTML, pattern);
            //    if (match.Success)
            //    {
            //        version = match.Value.Replace("UnityDownloadAssistant-", "").Replace(".exe", "");
            //    }
            //}
            //return version;
        }

        public static string FindNearestVersion(string currentVersion, List<string> allAvailable, bool checkBelow = false)
        {
            if (allAvailable == null)
                return null;

            string result = null;

            // add current version to list, to sort it with others
            if (!allAvailable.Contains(currentVersion)) allAvailable.Add(currentVersion);

            // sort list
            if (checkBelow)
            {
                allAvailable.Sort((s1, s2) => VersionAsLong(s1).CompareTo(VersionAsLong(s2)));
            }
            else
            {
                allAvailable.Sort((s1, s2) => VersionAsLong(s2).CompareTo(VersionAsLong(s1)));
            }

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

        public static void AddContextMenuRegistryAPKInstall(string contextRegRoot)
        {
            // Define the registry key path for .apk file association
            string apkFileTypeRegPath = @"Software\Classes\.apk";

            // Open or create the registry key for .apk files
            RegistryKey apkKey = Registry.CurrentUser.OpenSubKey(apkFileTypeRegPath, true);

            if (apkKey == null)
            {
                apkKey = Registry.CurrentUser.CreateSubKey(apkFileTypeRegPath);
            }

            if (apkKey != null)
            {
                // Create or open the Shell subkey for context menu options
                RegistryKey shellKey = apkKey.CreateSubKey("shell", true);

                if (shellKey != null)
                {
                    var appName = "UnityLauncherPro";
                    // Create a subkey for the app's context menu item
                    RegistryKey appKey = shellKey.CreateSubKey(appName, true);

                    if (appKey != null)
                    {
                        appKey.SetValue("", "Install with " + appName); // Display name in context menu
                        appKey.SetValue("Icon", "\"" + Process.GetCurrentProcess().MainModule.FileName + "\"");
                        appKey.SetValue("Position", "Bottom"); // Set position to adjust order

                        // Create the command subkey to specify the action
                        RegistryKey commandKey = appKey.CreateSubKey("command", true);

                        if (commandKey != null)
                        {
                            // Build the command string to launch with -install argument
                            var executeString = "\"" + Process.GetCurrentProcess().MainModule.FileName + "\" -install \"%1\"";
                            commandKey.SetValue("", executeString);
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine("Error> Cannot create or access registry key for .apk file association: " + apkFileTypeRegPath);
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

        public static void RemoveContextMenuRegistryAPKInstall(string contextRegRoot)
        {
            // Define the registry key path for .apk file association
            string apkFileTypeRegPath = @"Software\Classes\.apk\shell";

            // Open the registry key for the shell context menu
            RegistryKey shellKey = Registry.CurrentUser.OpenSubKey(apkFileTypeRegPath, true);

            if (shellKey != null)
            {
                var appName = "UnityLauncherPro";

                // Check if the app's context menu key exists
                RegistryKey appKey = shellKey.OpenSubKey(appName, false);
                if (appKey != null)
                {
                    // Delete the app's context menu key
                    shellKey.DeleteSubKeyTree(appName);
                    Console.WriteLine("Removed context menu for .apk files.");
                }
                else
                {
                    Console.WriteLine("No context menu found for .apk files.");
                }
            }
            else
            {
                Console.WriteLine("Error> Cannot find registry key for .apk shell context: " + apkFileTypeRegPath);
            }
        }

        /// <summary>
        /// reads .git/HEAD file from the project to get current branch name
        /// </summary>
        /// <param name="projectPath"></param>
        /// <returns></returns>
        public static string ReadGitBranchInfo(string projectPath, bool searchParentFolders)
        {
            DirectoryInfo directoryInfo = new DirectoryInfo(projectPath);
            while (directoryInfo != null)
            {
                string gitDir = Path.Combine(directoryInfo.FullName, ".git");
                string headFile = Path.Combine(gitDir, "HEAD");

                if (Directory.Exists(gitDir) && File.Exists(headFile))
                {
                    string headContent = File.ReadAllText(headFile).Trim();
                    int pos = headContent.LastIndexOf('/') + 1;
                    return (pos < headContent.Length) ? headContent.Substring(pos) : headContent;
                }

                if (!searchParentFolders)
                {
                    break;
                }
                directoryInfo = directoryInfo.Parent;
            }

            return null;
        }


        public static string ReadPlasticBranchInfo(string projectPath, bool searchParentFolders)
        {
            string branchName = null;
            DirectoryInfo directoryInfo = new DirectoryInfo(projectPath);

            while (directoryInfo != null)
            {
                string plasticSelectorPath = Path.Combine(directoryInfo.FullName, ".plastic", "plastic.selector");
                if (File.Exists(plasticSelectorPath))
                {
                    branchName = ExtractPlasticBranch(plasticSelectorPath);
                    if (!string.IsNullOrEmpty(branchName))
                    {
                        return branchName;
                    }
                }

                if (!searchParentFolders)
                {
                    break;
                }

                directoryInfo = directoryInfo.Parent;
            }

            return branchName;
        }

        private static string ExtractPlasticBranch(string plasticSelectorPath)
        {
            string[] lines = File.ReadAllLines(plasticSelectorPath);
            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();
                if (trimmedLine.StartsWith("br ") || trimmedLine.StartsWith("smartbranch "))
                {
                    // Extract the first quoted string
                    var match = Regex.Match(trimmedLine, "\"([^\"]+)\"");
                    if (match.Success)
                    {
                        string branchName = match.Groups[1].Value;
                        // Remove leading slash if present (e.g., "/main" becomes "main")
                        if (branchName.StartsWith("/"))
                        {
                            branchName = branchName.Substring(1);
                        }
                        return branchName;
                    }
                }
            }
            return null;
        }

        static string GetTargetPlatformRaw(string projectPath)
        {
            string results = null;

            // get buildtarget from .csproj
            // <UnityBuildTarget>StandaloneWindows64:19</UnityBuildTarget>
            // get main csproj file
            var csproj = Path.Combine(projectPath, "Assembly-CSharp.csproj");

            // TODO check projname also, if no assembly-.., NOTE already checked above
            // var csproj = Path.Combine(projectPath, projectName + ".csproj");

            if (File.Exists(csproj))
            {
                // Read the file line by line for performance
                using (var reader = new StreamReader(csproj))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        const string tagStart = "<UnityBuildTarget>";
                        const string tagEnd = "</UnityBuildTarget>";

                        int startIdx = line.IndexOf(tagStart);
                        if (startIdx >= 0)
                        {
                            int endIdx = line.IndexOf(tagEnd, startIdx);
                            if (endIdx > startIdx)
                            {
                                string inner = line.Substring(startIdx + tagStart.Length, endIdx - startIdx - tagStart.Length);
                                int colonIndex = inner.IndexOf(':');
                                if (colonIndex > -1)
                                {
                                    //Console.WriteLine("build target: " + inner.Substring(0, colonIndex));
                                    // 5.6 : win32, win64, osx, linux, linux64, ios, android, web, webstreamed, webgl, xboxone, ps4, psp2, wsaplayer, tizen, samsungtv
                                    // 2017: standalone, Win, Win64, OSXUniversal, Linux, Linux64, LinuxUniversal, iOS, Android, Web, WebStreamed, WebGL, XboxOne, PS4, PSP2, WindowsStoreApps, Switch, WiiU, N3DS, tvOS, PSM
                                    // 2018: standalone, Win, Win64, OSXUniversal, Linux, Linux64, LinuxUniversal, iOS, Android, Web, WebStreamed, WebGL, XboxOne, PS4, WindowsStoreApps, Switch, N3DS, tvOS
                                    // 2019: Standalone, Win, Win64, OSXUniversal, Linux64, iOS, Android, WebGL, XboxOne, PS4, WindowsStoreApps, Switch, tvOS
                                    // 2020: Standalone, Win, Win64, OSXUniversal, Linux64, iOS, Android, WebGL, XboxOne, PS4, WindowsStoreApps, Switch, tvOS
                                    // 2021: Standalone, Win, Win64, OSXUniversal, Linux64, iOS, Android, WebGL, XboxOne, PS4, WindowsStoreApps, Switch, tvOS
                                    results = inner.Substring(0, colonIndex);
                                    //results = (Platform)Enum.Parse(typeof(Platform), inner.Substring(0, colonIndex));
                                    break; // we found it, exit early
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                //Console.WriteLine("Missing csproj, cannot parse target platform: " + projectPath);
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
            if (targetGrid.Items.Count < 1) return;

            if (index == -1 && targetGrid.SelectedIndex > -1) index = targetGrid.SelectedIndex;
            if (index == -1) index = 0;

            targetGrid.SelectedIndex = index;

            // Try get the row, if not realized yet, defer
            DataGridRow row = (DataGridRow)targetGrid.ItemContainerGenerator.ContainerFromIndex(index);
            if (row == null)
            {
                targetGrid.ScrollIntoView(targetGrid.Items[index]);
                // Defer the focus once row is generated
                targetGrid.Dispatcher.InvokeAsync(() =>
                {
                    var newRow = (DataGridRow)targetGrid.ItemContainerGenerator.ContainerFromIndex(index);
                    if (newRow != null)
                    {
                        newRow.MoveFocus(new TraversalRequest(FocusNavigationDirection.Up));
                        newRow.Focus();
                        Keyboard.Focus(newRow);
                    }
                }, DispatcherPriority.Background);
            }
            else
            {
                row.MoveFocus(new TraversalRequest(FocusNavigationDirection.Up));
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
        public static Project FastCreateProject(string version, string baseFolder, string projectName = null, string templateZipPath = null, string[] platformsForThisUnity = null, string platform = null, bool useInitScript = false, string initScriptPath = null, bool forceDX11 = false)
        {
            // check for base folders in settings tab
            if (string.IsNullOrEmpty(baseFolder) == true)
            {
                SetStatus("Missing baseFolder value");
                return null;
            }

            // check if base folder exists
            if (Directory.Exists(baseFolder) == false)
            {
                // TODO add streaming filter
                SetStatus("Missing baseFolder: " + baseFolder);
                return null;
            }

            // check selected unity version
            if (string.IsNullOrEmpty(version) == true)
            {
                SetStatus("Missing unity version string");
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
            proj.Arguments = version.Contains("6000") ? (forceDX11 ? "-force-d3d11" : null) : null; // this gets erased later, since its not saved? would be nice to not add it at all though
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
            // TODO add streaming filter
            SetStatus("Creating new project folder: " + path);
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
                        string param = null;

                        // parse proj version year as number 2019.4.1f1 -> 2019
                        int year = 0;
                        var versionParts = proj.Version.Split('.');
                        bool parsedYear = int.TryParse(versionParts[0], out year);

                        if (parsedYear && year >= 6000)
                        {
                            param = "\"" + webExe + "\" \"" + buildPath + "\" " + "http://localhost:" + port + "/" + (pid == -1 ? "" : " " + pid);
                        }
                        else // older versions or failed to parse
                        {
                            param = "\"" + webExe + "\" \"" + buildPath + "\" " + port + (pid == -1 ? "" : " " + pid); // server exe path, build folder and port
                        }

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

                    // parse proj version year as number 2019.4.1f1 -> 2019
                    string param = null;
                    int year = 0;
                    var versionParts = proj.Version.Split('.');
                    bool parsedYear = int.TryParse(versionParts[0], out year);

                    if (parsedYear && year >= 6000)
                    {
                        param = "\"" + webExe + "\" \"" + buildPath + "\" " + "\"http://localhost:" + port + "/\"" + (pid == -1 ? "" : " " + pid);
                    }
                    else // older versions or failed to parse
                    {
                        param = "\"" + webExe + "\" \"" + buildPath + "\" " + port + (pid == -1 ? "" : " " + pid); // server exe path, build folder and port
                    }

                    //var param = "\"" + webExe + "\" \"" + buildPath + "\" " + port + (pid == -1 ? "" : " " + pid); // server exe path, build folder and port

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

        internal static void OpenCustomAssetPath()
        {
            // check if custom asset folder is used, then open both *since older versions might have assets in old folder
            string keyPath = @"SOFTWARE\Unity Technologies\Unity Editor 5.x";
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath))
            {
                if (key == null) return;
                // Enumerate subkeys
                foreach (string valueName in key.GetValueNames())
                {
                    // Check if the subkey matches the desired pattern
                    if (Regex.IsMatch(valueName, @"AssetStoreCacheRootPath_h\d+") == false) continue;

                    string customAssetPath = "";
                    var valueKind = key.GetValueKind(valueName);

                    if (valueKind == RegistryValueKind.Binary)
                    {
                        byte[] bytes = (byte[])key.GetValue(valueName);
                        customAssetPath = Encoding.UTF8.GetString(bytes, 0, bytes.Length - 1);
                    }
                    else // should be string then
                    {
                        customAssetPath = (string)key.GetValue(valueName);
                    }

                    if (string.IsNullOrEmpty(customAssetPath) == false && Directory.Exists(customAssetPath))
                    {
                        Tools.LaunchExplorer(Path.Combine(customAssetPath, "Asset Store-5.x"));
                    }
                }
            }
        }

        private static async Task<bool> DownloadFileAsync(string fileUrl, string destinationPath)
        {
            var cancellationTokenSource = new CancellationTokenSource();
            var fileName = Path.GetFileName(fileUrl);
            var progressWindow = new DownloadProgressWindow(fileName, () => cancellationTokenSource.Cancel());
            progressWindow.Show();
            var result = false;
            try
            {
                using (var client = new HttpClient())
                using (var response = await client.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead, cancellationTokenSource.Token))
                {
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? 1;
                    var buffer = new byte[8192];
                    var totalRead = 0;

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write,
                               FileShare.None, buffer.Length, true))
                    {
                        int bytesRead;
                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationTokenSource.Token)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationTokenSource.Token);
                            totalRead += bytesRead;
                            progressWindow.UpdateProgress(new DownloadProgress(totalRead, totalBytes));
                        }
                        result = true;
                    }
                }
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("Download cancelled");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                if (!result)
                {
                    DeleteTempFile(destinationPath);
                }
                progressWindow.Close();
            }
            return result;
        }

        internal static string GetSRP(string projectPath)
        {
            // read projectsettings/graphicsettings file, look for m_SRPDefaultSettings: value
            var settingsFile = Path.Combine(projectPath, "ProjectSettings", "GraphicsSettings.asset");
            if (File.Exists(settingsFile) == false) return null;

            var allText = File.ReadAllText(settingsFile);
            var srpIndex = allText.IndexOf("m_SRPDefaultSettings:");
            if (srpIndex == -1)
            {
                srpIndex = allText.IndexOf("m_RenderPipelineGlobalSettingsMap:"); // unity 6000.2- ?
                if (srpIndex == -1) return null; // BIRP
            }

            // urp = UnityEngine.Rendering.Universal.UniversalRenderPipeline
            // hdrp = UnityEngine.Rendering.HighDefinition.HDRenderPipeline

            if (allText.IndexOf("UnityEngine.Rendering.Universal.UniversalRenderPipeline", srpIndex) > -1)
            {
                return "URP";
            }
            else if (allText.IndexOf("UnityEngine.Rendering.HighDefinition.HDRenderPipeline", srpIndex) > -1)
            {
                return "HDRP";
            }
            else
            {
                return null; // BIRP
            }

        }

        internal static void InstallAPK(string ApkPath)
        {
            try
            {
                var cmd = "cmd.exe";
                var pars = $"/C adb install -r \"{ApkPath}\""; // Use /C to execute and close the window after finishing

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = cmd,
                    Arguments = pars,
                    RedirectStandardOutput = true, // Capture output to wait for completion
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = false
                };

                string installOutput = null;
                string installError = null;

                using (var installProcess = Process.Start(processStartInfo))
                {
                    installOutput = installProcess.StandardOutput.ReadToEnd();
                    installError = installProcess.StandardError.ReadToEnd();
                    installProcess.WaitForExit();

                    if (installProcess.ExitCode != 0 || !string.IsNullOrEmpty(installError))
                    {
                        SetStatus($"Error installing APK: {installError.Trim()}\n{installOutput.Trim()}");
                        return;
                    }
                }

                // Attempt to extract package name using aapt
                var aaptCmd = $"aapt dump badging \"{ApkPath}\" | findstr package:";
                var aaptProcessStartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/C {aaptCmd}",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                string packageName = null;
                using (var process = Process.Start(aaptProcessStartInfo))
                {
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    var match = System.Text.RegularExpressions.Regex.Match(output, "package: name='(.*?)'");
                    if (match.Success)
                    {
                        packageName = match.Groups[1].Value;
                    }
                }

                if (!string.IsNullOrEmpty(packageName))
                {
                    // Run the application using adb
                    var runPars = $"/C adb shell monkey -p {packageName} 1";
                    var runProcessStartInfo = new ProcessStartInfo
                    {
                        FileName = cmd,
                        Arguments = runPars,
                        UseShellExecute = true,
                        CreateNoWindow = false,
                        WindowStyle = ProcessWindowStyle.Normal
                    };
                    Process.Start(runProcessStartInfo);

                    SetStatus($"Installed and launched APK with package name: {packageName}");
                }
                else
                {
                    SetStatus("ADB install completed, but failed to extract package name. Application not launched.");
                }
            }
            catch (Win32Exception ex)
            {
                // Handle case where adb or aapt is not found
                SetStatus($"Error: Required tool not found. Ensure adb and aapt are installed and added to PATH. Details: {ex.Message}");
            }
            catch (Exception ex)
            {
                // Handle other unexpected exceptions
                SetStatus($"An unexpected error occurred: {ex.Message}");
            }
        }

        // exclude folders from windows defender
        internal static bool RunExclusionElevated(IEnumerable<string> paths, bool silent = false)
        {
            var escapedPaths = new List<string>();
            foreach (var rawPath in paths)
            {
                var path = rawPath.Trim();
                string safePath = path.Replace("'", "''");
                escapedPaths.Add($"'{safePath}'");
            }

            string joinedPaths = string.Join(", ", escapedPaths);
            string psCommand = $"Add-MpPreference -ExclusionPath {joinedPaths}";

            string fullCommand;

            if (silent)
            {
                // No output, just run the command silently
                fullCommand = psCommand;
            }
            else
            {
                // Show command and keep window open
                var quotedPathsForDisplay = string.Join(", ", escapedPaths.ConvertAll(p => $"'{p.Trim('\'')}'"));
                string displayCommand = $"Add-MpPreference -ExclusionPath {quotedPathsForDisplay}";
                fullCommand = $"Write-Host 'Running: {displayCommand}'; {psCommand}; Write-Host ''; Write-Host 'Done. Press any key to exit...'; pause";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = silent
                    ? $"-WindowStyle Hidden -Command \"{fullCommand}\""
                    : $"-NoExit -Command \"{fullCommand}\"",
                UseShellExecute = true,
                Verb = "runas", // Requires admin rights
                WindowStyle = silent ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
            };

            try
            {
                Process.Start(startInfo);
            }
            catch (Win32Exception)
            {
                if (!silent)
                {
                    MessageBox.Show("Operation cancelled or failed due to insufficient privileges.", "Cancelled", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return false;
            }

            return true;
        }







    } // class

} // namespace
