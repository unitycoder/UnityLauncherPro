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
        static Dictionary<string, string> remapPlatformNames = new Dictionary<string, string> { { "StandaloneWindows64", "Standalone" }, { "StandaloneWindows", "Standalone" }, { "Android", "Android" }, { "WebGL", "WebGL" } };

        // TODO separate scan and folders
        public static List<Project> Scan(bool getGitBranch = false, bool getArguments = false, bool showMissingFolders = false, bool showTargetPlatform = false)
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

                        //string csprojFile = Path.Combine(projectPath, projectName + ".csproj");

                        //// maybe 4.x or 2019 or later project
                        //if (folderExists == true && File.Exists(csprojFile) == false)
                        //{
                        //    csprojFile = Path.Combine(projectPath, "Assembly-CSharp.csproj");
                        //}
                        //else if (folderExists == true && File.Exists(csprojFile) == false) // editor only project
                        //{
                        //    csprojFile = Path.Combine(projectPath, projectName + ".Editor.csproj");
                        //}
                        //else if (folderExists == true && File.Exists(csprojFile) == false) // solution only
                        //{
                        //    csprojFile = Path.Combine(projectPath, projectName + ".sln");
                        //}

                        // get last modified date from folder
                        DateTime? lastUpdated = folderExists ? Tools.GetLastModifiedTime(projectPath) : null;
                        
                        // get project version
                        string projectVersion = folderExists ? Tools.GetProjectVersion(projectPath) : null;

                        // get custom launch arguments, only if column in enabled
                        string customArgs = "";
                        if (getArguments == true)
                        {
                            customArgs = folderExists ? Tools.ReadCustomLaunchArguments(projectPath, MainWindow.launcherArgumentsFile) : null;
                        }

                        // get git branchinfo, only if column in enabled
                        string gitBranch = "";
                        if (getGitBranch == true)
                        {
                            gitBranch = folderExists ? Tools.ReadGitBranchInfo(projectPath) : null;
                        }

                        // TODO add option to disable check
                        string targetPlatform = "";
                        //Platform targetPlatform = Platform.Unknown;
                        if (showTargetPlatform == true)
                        {
                            targetPlatform = folderExists ? Tools.GetTargetPlatform(projectPath) : null;
                            //targetPlatform = folderExists ? Tools.GetTargetPlatform(projectPath) : Platform.Unknown;
                        }

                        var p = new Project();
                        p.Title = projectName;
                        p.Version = projectVersion;
                        p.Path = projectPath;
                        p.Modified = lastUpdated;
                        p.Arguments = customArgs;
                        p.GITBranch = gitBranch;
                        //p.TargetPlatform = targetPlatform;
                        //Console.WriteLine("targetPlatform="+ targetPlatform);
                        // get matching buildtarget platform name from dictionary
                        if (string.IsNullOrEmpty(targetPlatform) == false && remapPlatformNames.ContainsKey(targetPlatform))
                        {
                            p.TargetPlatform = remapPlatformNames[targetPlatform];
                        }
                        else
                        {
                            if (string.IsNullOrEmpty(targetPlatform) == false) Console.WriteLine("Missing buildTarget remap name for: " + targetPlatform);
                            p.TargetPlatform = null;
                        }

                        // bubblegum(TM) solution, fill available platforms for this unity version, for this project
                        p.TargetPlatforms = Tools.GetPlatformsForUnityVersion(projectVersion);

                        p.folderExists = folderExists;

                        // if want to hide project and folder path for screenshot
                        //p.Title = "Hidden Project";
                        //p.Path = "C:/Hidden Path/";

                        projectsFound.Add(p);
                    } // valid key
                } // each key
            } // for each registry root

            return projectsFound;
        } // Scan()
    } // class
} // namespace
