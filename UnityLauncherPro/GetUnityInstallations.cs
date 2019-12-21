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
        //public static UnityInstallation[] Scan(string[] rootFolders)
        public static UnityInstallation[] Scan()
        {
            // convert settings list to string array
            var rootFolders = Properties.Settings.Default.rootFolders;

            // unityversion, exe_path
            //Dictionary<string, UnityInstallation> results = new ictionary<string, UnityInstallation>();
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

                    // add to list, if not there yet NOTE should notify that there are 2 same versions..? this might happen with preview builds..
                    if (results.Contains(unity) == true)
                    {
                        Console.WriteLine("Warning: 2 same versions found for " + version);
                        continue;
                    }

                    results.Add(unity);

                } // got folders
            } // all root folders

            // sort by unity version NOTE we might want to sort by install date also..
            results.Sort((s1, s2) => VersionAsFloat(s2.Version).CompareTo(VersionAsFloat(s1.Version)));

            return results.ToArray();
        } // scan()

        // string to float 2017.4.1f1 > 2017.411
        static float VersionAsFloat(string version)
        {
            float result = 0;
            if (string.IsNullOrEmpty(version)) return result;

            // remove a,b,f,p
            string cleanVersion = version.Replace("a", ".");
            cleanVersion = cleanVersion.Replace("b", ".");
            cleanVersion = cleanVersion.Replace("f", ".");
            cleanVersion = cleanVersion.Replace("p", ".");

            // split values
            string[] splitted = cleanVersion.Split('.');
            if (splitted != null && splitted.Length > 0)
            {
                // get float
                float multiplier = 1;
                for (int i = 0, length = splitted.Length; i < length; i++)
                {
                    int n = int.Parse(splitted[i]);
                    result += n * multiplier;
                    multiplier /= 10;
                }
            }
            return result;
        }

    } // class
} // namespace
