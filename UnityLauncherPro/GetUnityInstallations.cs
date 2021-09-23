using System;
using System.Collections.Generic;
using System.IO;

namespace UnityLauncherPro
{
    /// <summary>
    /// returns unity installations under given root folders
    /// </summary>
    public static class GetUnityInstallations
    {
        //static Dictionary<string, string> platformNames = new Dictionary<string, string> { { "androidplayer", "Android" }, { "windowsstandalonesupport", "Windows" }, { "linuxstandalonesupport", "Linux" }, { "LinuxStandalone", "Linux" }, { "OSXStandalone", "Mac" }, { "webglsupport", "WebGL" }, { "metrosupport", "UWP" } };
        static Dictionary<string, string> platformNames = new Dictionary<string, string> { { "androidplayer", "Android" }, { "windowsstandalonesupport", "Standalone" }, { "linuxstandalonesupport", "Standalone" }, { "LinuxStandalone", "Standalone" }, { "OSXStandalone", "Standalone" }, { "webglsupport", "WebGL" }, { "metrosupport", "UWP" } };


        // returns unity installations
        public static UnityInstallation[] Scan()
        {
            // convert settings list to string array
            var rootFolders = Properties.Settings.Default.rootFolders;

            // unityversion, exe_path
            List<UnityInstallation> results = new List<UnityInstallation>();

            // iterate all root folders
            foreach (string rootFolder in rootFolders)
            {
                // if folder exists
                if (String.IsNullOrWhiteSpace(rootFolder) == true || Directory.Exists(rootFolder) == false) continue;

                // get all folders
                var directories = Directory.GetDirectories(rootFolder);
                // parse all folders under root, and search for unity editor files
                for (int i = 0, length = directories.Length; i < length; i++)
                {
                    // check if uninstaller is there, sure sign for unity
                    var uninstallExe = Path.Combine(directories[i], "Editor", "Uninstall.exe");
                    var haveUninstaller = File.Exists(uninstallExe);

                    var exePath = Path.Combine(directories[i], "Editor", "Unity.exe");
                    if (File.Exists(exePath) == false) continue;

                    // get full version number from uninstaller (or try exe, if no uninstaller)
                    var version = Tools.GetFileVersionData(haveUninstaller ? uninstallExe : exePath);

                    // we got new version to add
                    var dataFolder = Path.Combine(directories[i], "Editor", "Data");
                    DateTime? installDate = Tools.GetLastModifiedTime(dataFolder);
                    UnityInstallation unity = new UnityInstallation();
                    unity.Version = version;
                    unity.Path = exePath;
                    unity.Installed = installDate;
                    unity.IsPreferred = (version == MainWindow.preferredVersion);
                    unity.ProjectCount = GetProjectCountForUnityVersion(version);

                    // get platforms, NOTE if this is slow, do it later, or skip for commandline
                    var platforms = GetPlatforms(dataFolder);
                    // this is for editor tab, show list of all platforms in cell
                    if (platforms != null) unity.PlatformsCombined = string.Join(", ", platforms);
                    // this is for keeping array of platforms for platform combobox
                    if (platforms != null) unity.Platforms = platforms;

                    // add to list, if not there yet NOTE should notify that there are 2 same versions..? this might happen with preview builds..
                    if (results.Contains(unity) == true)
                    {
                        Console.WriteLine("Warning: 2 same versions found for " + version);
                        continue;
                    }

                    results.Add(unity);
                } // got folders
            } // all root folders

            results.Sort((s1, s2) => Tools.VersionAsInt(s2.Version).CompareTo(Tools.VersionAsInt(s1.Version)));

            return results.ToArray();
        } // scan()

        // scans unity installation folder for installed platforms
        static string[] GetPlatforms(string dataFolder)
        {
            // get all folders inside
            var platformFolder = Path.Combine(dataFolder, "PlaybackEngines");
            if (Directory.Exists(platformFolder) == false) return null;
            var directories = Directory.GetDirectories(platformFolder);
            for (int i = 0; i < directories.Length; i++)
            {
                var foldername = Path.GetFileName(directories[i]).ToLower();
                // check if have better name in dictionary
                if (platformNames.ContainsKey(foldername))
                {
                    directories[i] = platformNames[foldername];
                }
                else
                {
                    directories[i] = foldername;
                }
            }

            return directories;
        }

        static int GetProjectCountForUnityVersion(string version)
        {
            if (MainWindow.projectsSource == null) return 0;
            //Console.WriteLine("xxx "+(MainWindow.projectsSource==null));
            int count = 0;
            // count projects using this exact version
            for (int i = 0, len = MainWindow.projectsSource.Count; i < len; i++)
            {
                if (MainWindow.projectsSource[i].Version == version) count++;
            }
            return count;
        }

    } // class
} // namespace
