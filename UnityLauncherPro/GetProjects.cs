using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UnityLauncherPro
{
    public static class GetProjects
    {
        // which registries we want to scan for projects
        static readonly string[] registryPathsToCheck = new string[] { @"SOFTWARE\Unity Technologies\Unity Editor 5.x", @"SOFTWARE\Unity Technologies\Unity Editor 4.x" };

        // convert target platform name into valid buildtarget platform name, NOTE this depends on unity version, now only 2019 and later are supported
        public static Dictionary<string, string> remapPlatformNames = new Dictionary<string, string> { { "StandaloneWindows64", "Win64" }, { "StandaloneWindows", "Win" }, { "Android", "Android" }, { "WebGL", "WebGL" } };

        // TODO separate scan and folders
        public static List<Project> Scan(bool getGitBranch = false, bool getPlasticBranch = false, bool getArguments = false, bool showMissingFolders = false, bool showTargetPlatform = false)
        {
            List<Project> projectsFound = new List<Project>();

            var hklm = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);

            // check each version path
            for (int i = 0, len = registryPathsToCheck.Length; i < len; i++)
            {
                RegistryKey key = hklm.OpenSubKey(registryPathsToCheck[i]);

                if (key == null)
                {
                    continue;
                }
                else
                {
                    //Console.WriteLine("Null registry key at " + registryPathsToCheck[i]);
                }

                // parse recent project path
                foreach (var valueName in key.GetValueNames())
                {
                    if (valueName.IndexOf("RecentlyUsedProjectPaths-") == 0)
                    {
                        bool folderExists = false;
                        string projectPath = "";
                        // check if binary or not
                        var valueKind = key.GetValueKind(valueName);
                        if (valueKind == RegistryValueKind.Binary)
                        {
                            byte[] projectPathBytes = (byte[])key.GetValue(valueName);
                            projectPath = Encoding.UTF8.GetString(projectPathBytes, 0, projectPathBytes.Length - 1);
                        }
                        else // should be string then
                        {
                            projectPath = (string)key.GetValue(valueName);
                        }

                        // first check if whole folder exists, if not, skip
                        folderExists = Directory.Exists(projectPath);
                        if (showMissingFolders == false && folderExists == false)
                        {
                            //Console.WriteLine("Recent project directory not found, skipping: " + projectPath);
                            continue;
                        }

                        string projectName = "";

                        // get project name from full path
                        if (projectPath.IndexOf(Path.DirectorySeparatorChar) > -1)
                        {
                            projectName = projectPath.Substring(projectPath.LastIndexOf(Path.DirectorySeparatorChar) + 1);
                        }
                        else if (projectPath.IndexOf(Path.AltDirectorySeparatorChar) > -1)
                        {
                            projectName = projectPath.Substring(projectPath.LastIndexOf(Path.AltDirectorySeparatorChar) + 1);
                        }
                        else // no path separator found
                        {
                            projectName = projectPath;
                        }

                        //Console.WriteLine("valueName="+ valueName+"  , projectName =" + projectName);

                        // get last modified date from folder
                        DateTime? lastUpdated = folderExists ? Tools.GetLastModifiedTime(projectPath) : null;

                        // get project version
                        string projectVersion = folderExists ? Tools.GetProjectVersion(projectPath) : null;

                        // get custom launch arguments, only if column in enabled
                        string customArgs = "";
                        if (getArguments == true)
                        {
                            customArgs = folderExists ? Tools.ReadCustomProjectData(projectPath, MainWindow.launcherArgumentsFile) : null;
                        }

                        // get git branchinfo, only if column in enabled
                        string gitBranch = "";
                        if (getGitBranch == true)
                        {
                            gitBranch = folderExists ? Tools.ReadGitBranchInfo(projectPath) : null;
                            // check for plastic, if enabled
                            if (getPlasticBranch == true && gitBranch == null)
                            {
                                gitBranch = folderExists ? Tools.ReadPlasticBranchInfo(projectPath) : null;
                            }
                        }

                        string targetPlatform = "";
                        if (showTargetPlatform == true)
                        {
                            targetPlatform = folderExists ? Tools.GetTargetPlatform(projectPath) : null;
                        }

                        var p = new Project();

                        switch (MainWindow.projectNameSetting)
                        {
                            case 0:
                                p.Title = Tools.ReadCustomProjectData(projectPath, MainWindow.projectNameFile);
                                break;
                            case 1:
                                p.Title = Tools.ReadProjectName(projectPath);
                                break;
                            default:
                                p.Title = projectName;
                                break;
                        }

                        // if no custom data or no product name found
                        if (string.IsNullOrEmpty(p.Title)) p.Title = projectName;

                        p.Version = projectVersion;
                        p.Path = projectPath;
                        p.Modified = lastUpdated;
                        p.Arguments = customArgs;
                        p.GITBranch = gitBranch;
                        //Console.WriteLine("targetPlatform " + targetPlatform + " projectPath:" + projectPath);
                        p.TargetPlatform = targetPlatform;

                        // bubblegum(TM) solution, fill available platforms for this unity version, for this project
                        p.TargetPlatforms = Tools.GetPlatformsForUnityVersion(projectVersion);

                        p.folderExists = folderExists;

                        // if want to hide project and folder path for screenshot
                        //p.Title = "Example Project ";
                        //p.Path = "C:/Projects/ExamplePath/MyProj";

                        projectsFound.Add(p);
                    } // valid key
                } // each key
            } // for each registry root

            // NOTE sometimes projects are in wrong order, seems to be related to messing up your unity registry, the keys are received in created order (so if you had removed/modified them manually, it might return wrong order instead of 0 - 40)

            return projectsFound;
        } // Scan()

        public static bool RemoveRecentProject(string projectPathToRemove)
        {
            var hklm = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);

            // check each version path
            for (int i = 0, len = registryPathsToCheck.Length; i < len; i++)
            {
                RegistryKey key = hklm.OpenSubKey(registryPathsToCheck[i], true);

                if (key == null)
                {
                    continue;
                }
                else
                {
                    //Console.WriteLine("Null registry key at " + registryPathsToCheck[i]);
                }

                // parse recent project paths
                foreach (var valueName in key.GetValueNames())
                {
                    if (valueName.IndexOf("RecentlyUsedProjectPaths-") == 0)
                    {
                        string projectPath = "";
                        // check if binary or not
                        var valueKind = key.GetValueKind(valueName);
                        if (valueKind == RegistryValueKind.Binary)
                        {
                            byte[] projectPathBytes = (byte[])key.GetValue(valueName);
                            projectPath = Encoding.UTF8.GetString(projectPathBytes, 0, projectPathBytes.Length - 1);
                        }
                        else // should be string then
                        {
                            projectPath = (string)key.GetValue(valueName);
                        }

                        // if our project folder, remove registry item
                        if (projectPath == projectPathToRemove)
                        {
                            Console.WriteLine("Deleted registery item: " + valueName + " projectPath=" + projectPath);
                            key.DeleteValue(valueName);
                            return true;
                        }

                    } // valid key
                } // each key
            } // for each registry root
            return false;
        } // RemoveRecentProject()

    } // class
} // namespace

