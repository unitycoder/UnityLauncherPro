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

        public static List<Project> Scan(bool getGitBranch = false, bool getArguments = false, bool showMissingFolders = false)
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

                        // maybe 4.x project
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

                        var p = new Project();
                        p.Title = projectName;
                        p.Version = projectVersion;
                        p.Path = projectPath;
                        p.Modified = lastUpdated;
                        p.Arguments = customArgs;
                        p.GITBranch = gitBranch;

                        projectsFound.Add(p);
                    } // valid key
                } // each key
            } // for each registry root

            return projectsFound;
        } // Scan()

    } // class
} // namespace
