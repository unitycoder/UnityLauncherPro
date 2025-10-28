using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
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

        public static List<Project> Scan(bool getGitBranch = false, bool getPlasticBranch = false, bool getArguments = false, bool showMissingFolders = false, bool showTargetPlatform = false, StringCollection AllProjectPaths = null, bool searchGitbranchRecursively = false, bool showSRP = false)
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

                        var p = GetProjectInfo(projectPath, getGitBranch, getPlasticBranch, getArguments, showMissingFolders, showTargetPlatform, searchGitbranchRecursively, showSRP);
                        //Console.WriteLine(projectPath+" "+p.TargetPlatform);

                        // if want to hide project and folder path for screenshot
                        //p.Title = "Example Project";
                        //p.Path = "C:/Projects/MyProj";

                        if (p != null)
                        {
                            projectsFound.Add(p);

                            // TODO FIXME, this gets called everytime for same projects?
                            // add found projects to history also (gets added only if its not already there)
                            Tools.AddProjectToHistory(p.Path);
                        }
                    } // valid key
                } // each key
            } // for each registry root

            // NOTE those 40 projects should be added to custom list, otherwise they will disappear (since last item is not yet added to our list, until its launched once, so need to launch many projects, to start collecting history..)
            // but then we would have to loop here again..? or add in the loop above..if doesnt exists on list, and the remove extra items from the end

            // scan info for custom folders (if not already on the list)
            if (AllProjectPaths != null)
            {
                // iterate custom full projects history
                foreach (var projectPath in AllProjectPaths)
                {
                    // check if registry list contains this path already, then skip it
                    bool found = false;
                    for (int i = 0, len = projectsFound.Count; i < len; i++)
                    {
                        if (projectsFound[i].Path == projectPath)
                        {
                            found = true;
                            break;
                        }
                    }

                    // if not found from registry, add to recent projects list
                    if (found == false)
                    {
                        var p = GetProjectInfo(projectPath, getGitBranch, getPlasticBranch, getArguments, showMissingFolders, showTargetPlatform, searchGitbranchRecursively, showSRP);
                        if (p != null) projectsFound.Add(p);
                    }
                }
            }

            // sometimes projects are in wrong order, seems to be related to messing up your unity registry, the keys are received in created order (so if you had removed/modified them manually, it might return wrong order instead of 0 - 40)
            // sort by modified date, projects without modified date are put to last, NOTE: this would remove projects without modified date (if they become last items, before trimming list on next step)
            projectsFound.Sort((x, y) =>
            {
                if (x.Modified == null && y.Modified == null) return 0; // cannot be -1, https://stackoverflow.com/a/42821992/5452781
                if (x.Modified == null) return 1;
                if (y.Modified == null) return -1;
                return y.Modified.Value.CompareTo(x.Modified.Value);
                //return x.Modified.Value.CompareTo(y.Modified.Value); // BUG this breaks something, so that last item platform is wrong (for project that is missing from disk) ?
            });

            // trim list to max amount (older ones are dropped)
            if (projectsFound.Count > MainWindow.maxProjectCount)
            {
                //Console.WriteLine("Trimming projects list to " + MainWindow.maxProjectCount + " projects");
                projectsFound.RemoveRange(MainWindow.maxProjectCount, projectsFound.Count - MainWindow.maxProjectCount);
            }
            return projectsFound;
        } // Scan()

        static Project GetProjectInfo(string projectPath, bool getGitBranch = false, bool getPlasticBranch = false, bool getArguments = false, bool showMissingFolders = false, bool showTargetPlatform = false, bool searchGitbranchRecursively = false, bool showSRP = false)
        {
            bool folderExists = Directory.Exists(projectPath);

            // if displaying missing folders are disabled, and folder doesnt exists, skip this project
            if (showMissingFolders == false && folderExists == false) return null;
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

            // get last modified date from most recent date of solution or folder
            string solutionPath = Path.Combine(projectPath, $"{projectName}.sln");
            bool solutionExists = File.Exists(solutionPath);
            DateTime? solutionLastUpdated = solutionExists ? Tools.GetLastModifiedTime(solutionPath) : null;
            DateTime? folderLastUpdated = folderExists ? Tools.GetLastModifiedTime(projectPath) : null;
            DateTime? lastUpdated = solutionLastUpdated > folderLastUpdated ? solutionLastUpdated : folderLastUpdated;

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
                gitBranch = folderExists ? Tools.ReadGitBranchInfo(projectPath, searchGitbranchRecursively) : null;
                
                // check for plastic, if enabled
                if (getPlasticBranch == true && gitBranch == null)
                {
                    gitBranch = folderExists ? Tools.ReadPlasticBranchInfo(projectPath, searchGitbranchRecursively) : null;
                }
            }

            string targetPlatform = null;
            if (showTargetPlatform == true)
            {
                targetPlatform = folderExists ? Tools.GetTargetPlatform(projectPath) : null;
                //if (projectPath.Contains("Shader")) Console.WriteLine(projectPath + " targetPlatform=" + targetPlatform);
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

            if (showSRP == true) p.SRP = Tools.GetSRP(projectPath);

            return p;
        }

        // removes project from unity registry recent projects, OR if its our custom list, remove from there
        public static bool RemoveRecentProject(string projectPathToRemove)
        {
            bool result = false;

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
                            //return true;
                            result = true;
                            // TODO should break both loops
                            //break;
                            goto jumpCustomHistory;
                        }

                    } // valid key
                } // each key
            } // for each registry root

        jumpCustomHistory:
            // check if its in our custom list, NOTE should only do if custom list is enabled
            if (Properties.Settings.Default.projectPaths.Contains(projectPathToRemove))
            {
                Properties.Settings.Default.projectPaths.Remove(projectPathToRemove);
                Properties.Settings.Default.Save();
                Console.WriteLine("Deleted recent history item: " + projectPathToRemove + " from custom list");
                //return true;
                result = true;
            }


            return result;
        } // RemoveRecentProject()

    } // class
} // namespace

