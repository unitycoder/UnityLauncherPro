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
                        if (showMissingFolders == false && Directory.Exists(projectPath) == false)
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

                        string csprojFile = Path.Combine(projectPath, projectName + ".csproj");

                        // solution only
                        if (File.Exists(csprojFile) == false)
                        {
                            csprojFile = Path.Combine(projectPath, projectName + ".sln");
                        }

                        // editor only project
                        if (File.Exists(csprojFile) == false)
                        {
                            csprojFile = Path.Combine(projectPath, projectName + ".Editor.csproj");
                        }

                        // maybe 4.x project, NOTE recent versions also have this as default
                        if (File.Exists(csprojFile) == false)
                        {
                            csprojFile = Path.Combine(projectPath, "Assembly-CSharp.csproj");
                        }

                        // get last modified date
                        DateTime? lastUpdated = Tools.GetLastModifiedTime(csprojFile);

                        // get project version
                        string projectVersion = Tools.GetProjectVersion(projectPath);

                        // get custom launch arguments, only if column in enabled
                        string customArgs = "";
                        if (getArguments == true)
                        {
                            customArgs = Tools.ReadCustomLaunchArguments(projectPath, MainWindow.launcherArgumentsFile);
                        }

                        // get git branchinfo, only if column in enabled
                        string gitBranch = "";
                        if (getGitBranch == true)
                        {
                            gitBranch = Tools.ReadGitBranchInfo(projectPath);
                        }

                        // TODO add option to disable check
                        string targetPlatform = "";
                        if (showTargetPlatform == true)
                        {
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
                                        Console.WriteLine("build target: " + csprojsplit[1].Substring(0, endrow));
                                        // 5.6 : win32, win64, osx, linux, linux64, ios, android, web, webstreamed, webgl, xboxone, ps4, psp2, wsaplayer, tizen, samsungtv
                                        // 2017: standalone, Win, Win64, OSXUniversal, Linux, Linux64, LinuxUniversal, iOS, Android, Web, WebStreamed, WebGL, XboxOne, PS4, PSP2, WindowsStoreApps, Switch, WiiU, N3DS, tvOS, PSM
                                        // 2018: standalone, Win, Win64, OSXUniversal, Linux, Linux64, LinuxUniversal, iOS, Android, Web, WebStreamed, WebGL, XboxOne, PS4, WindowsStoreApps, Switch, N3DS, tvOS
                                        // 2019: Standalone, Win, Win64, OSXUniversal, Linux64, iOS, Android, WebGL, XboxOne, PS4, WindowsStoreApps, Switch, tvOS
                                        // 2020: Standalone, Win, Win64, OSXUniversal, Linux64, iOS, Android, WebGL, XboxOne, PS4, WindowsStoreApps, Switch, tvOS
                                        targetPlatform = csprojsplit[1].Substring(0, endrow);
                                    }
                                }
                            }
                        }

                        var p = new Project();
                        p.Title = projectName;
                        p.Version = projectVersion;
                        p.Path = projectPath;
                        p.Modified = lastUpdated;
                        p.Arguments = customArgs;
                        p.GITBranch = gitBranch;
                        p.TargetPlatform = targetPlatform;

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
