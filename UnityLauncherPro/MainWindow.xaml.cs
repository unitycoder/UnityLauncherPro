// Unity Project Launcher by https://unitycoder.com
// https://github.com/unitycoder/UnityLauncherPro

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.Drawing; // for notifyicon
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using UnityLauncherPro.Data;
using UnityLauncherPro.Helpers;
using UnityLauncherPro.Properties;

namespace UnityLauncherPro
{
    public partial class MainWindow : Window
    {
        public const string appName = "UnityLauncherPro";
        public static string currentDateFormat = null;
        public static bool useHumanFriendlyDateFormat = false;
        public static bool searchProjectPathAlso = false;
        public static List<Project> projectsSource;
        public static List<UnityInstallation> unityInstallationsSource;
        public static ObservableDictionary<string, string> unityInstalledVersions = new ObservableDictionary<string, string>(); // versionID and installation folder
        public static readonly string launcherArgumentsFile = "LauncherArguments.txt";
        public static readonly string projectNameFile = "ProjectName.txt";
        public static string preferredVersion = null;
        public static int projectNameSetting = 0; // 0 = folder or ProjectName.txt if exists, 1=ProductName
        public static string initScriptFileFullPath;

        const string contextRegRoot = "Software\\Classes\\Directory\\Background\\shell";
        const string githubURL = "https://github.com/unitycoder/UnityLauncherPro";
        const string resourcesURL = "https://github.com/unitycoder/UnityResources";
        const string defaultAdbLogCatArgs = "-s Unity ActivityManager PackageManager dalvikvm DEBUG -v color";
        System.Windows.Forms.NotifyIcon notifyIcon = new System.Windows.Forms.NotifyIcon();

        UnityVersion[] updatesSource;
        public static List<string> updatesAsStrings = new List<string>();

        string _filterString = null;
        bool multiWordSearch = false;
        string[] searchWords;
        bool isDirtyCell = false;

        int lastSelectedProjectIndex = 0;
        Mutex myMutex;
        ThemeEditor themeEditorWindow;
        internal static int webglPort = 50000;
        internal static int maxProjectCount = 40;

        string defaultDateFormat = "dd/MM/yyyy HH:mm:ss";
        string adbLogCatArgs = defaultAdbLogCatArgs;

        string currentBuildReportProjectPath = null;
        string currentBuildPluginsRelativePath = null;
        //List<List<string>> buildReports = new List<List<string>>();
        List<BuildReport> buildReports = new List<BuildReport>(); // multiple reports, each contains their own stats and items
        int currentBuildReport = 0;

        private NamedPipeServerStream launcherPipeServer;
        private const string launcherPipeName = appName;
        readonly string unityHubPipeName = "Unity-hubIPCService";

        [DllImport("user32", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindow(string cls, string win);
        [DllImport("user32")]
        static extern IntPtr SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32")]
        static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32")]
        static extern bool OpenIcon(IntPtr hWnd);

        public MainWindow()
        {
            InitializeComponent();
            Init();
        }

        void Init()
        {
            // disable accesskeys without alt
            CoreCompatibilityPreferences.IsAltKeyRequiredInAccessKeyDefaultScope = true;

            // make window resizable (this didnt work when used pure xaml to do this)
            WindowChrome Resizable_BorderLess_Chrome = new WindowChrome();
            Resizable_BorderLess_Chrome.CornerRadius = new CornerRadius(0);
            Resizable_BorderLess_Chrome.CaptionHeight = 1.0;
            WindowChrome.SetWindowChrome(this, Resizable_BorderLess_Chrome);

            Tools.mainWindow = this;

            // need to load here to get correct window size early
            LoadSettings();

            // set version number
            if (string.IsNullOrEmpty(Version.Stamp) == false)
            {
                lblVersion.Content = "Build: " + Version.Stamp;
            }
        }

        void Start()
        {
            // get unity installations
            dataGridUnitys.Items.Clear();
            UpdateUnityInstallationsList();
            HandleCommandLineLaunch();

            // check for duplicate instance, and activate that instead
            if (chkAllowSingleInstanceOnly.IsChecked == true)
            {
                bool isNewInstance;
                myMutex = new Mutex(true, appName, out isNewInstance);

                if (!isNewInstance)
                {

                    // Send a wake-up message to the running instance
                    ActivateRunningInstance();

                    // Exit the current instance (if not coming from Explorer launch)
                    if (Directory.GetCurrentDirectory().ToLower() != @"c:\windows\system32")
                    {
                        App.Current.Shutdown();
                    }
                }
                else
                {
                    // Start pipe server in the first instance
                    StartPipeServer();
                }
            }

            // TEST erase custom history data
            //Properties.Settings.Default.projectPaths = null;
            //Properties.Settings.Default.Save();

            projectsSource = GetProjects.Scan(getGitBranch: (bool)chkShowGitBranchColumn.IsChecked, getPlasticBranch: (bool)chkCheckPlasticBranch.IsChecked, getArguments: (bool)chkShowLauncherArgumentsColumn.IsChecked, showMissingFolders: (bool)chkShowMissingFolderProjects.IsChecked, showTargetPlatform: (bool)chkShowPlatform.IsChecked, AllProjectPaths: Properties.Settings.Default.projectPaths, searchGitbranchRecursively: (bool)chkGetGitBranchRecursively.IsChecked, showSRP: (bool)chkCheckSRP.IsChecked);

            //Console.WriteLine("projectsSource.Count: " + projectsSource.Count);

            //gridRecent.Items.Clear(); // not needed?
            gridRecent.ItemsSource = projectsSource;

            // clear updates grid
            dataGridUpdates.Items.Clear();
            dataGridUpdates.SelectionChanged += DataGridUpdates_SelectionChanged;

            // clear buildreport grids
            gridBuildReport.Items.Clear();
            gridBuildReportData.Items.Clear();

            // build notifyicon (using windows.forms)
            notifyIcon.MouseClick += new System.Windows.Forms.MouseEventHandler(NotifyIcon_MouseClick);

            ApplyTheme(txtCustomThemeFile.Text);

            // for autostart with minimized
            if (Settings.Default.runAutomatically == true && Settings.Default.runAutomaticallyMinimized == true)
            {
                // if application was system started, then hide, otherwise dont hide (when user started it)
                if (Directory.GetCurrentDirectory().ToLower() == @"c:\windows\system32")
                {
                    this.ShowInTaskbar = false; // for some reason, otherwise it will show in taskbar only at start
                    notifyIcon.Visible = true;
                    this.Hide();
                }
            }

            // TEST
            //themeEditorWindow = new ThemeEditor();
            //themeEditorWindow.Show();

            // test override IPC so that unityhub doesnt start
            // open "Unity-hubIPCService" pipe, if not already open

            if (Settings.Default.disableUnityHubLaunch == true) StartHubPipe();

            CheckCustomIcon();

            if (chkFetchAdditionalInfo.IsChecked == true) Tools.FetchAdditionalInfoForEditors();

            isInitializing = false;
        } // Start()

        private static NamedPipeServerStream hubPipeServer;
        private CancellationTokenSource _hubCancellationTokenSource;

        private async Task StartPipeServerAsync(string pipeName, Action<string> onMessageReceived, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    using (var pipeServer = new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                    {
                        await pipeServer.WaitForConnectionAsync(cancellationToken);
                        Console.WriteLine($"Client connected to pipe '{pipeName}'!");

                        using (var reader = new StreamReader(pipeServer))
                        {
                            while (!cancellationToken.IsCancellationRequested)
                            {
                                string message = await reader.ReadLineAsync();
                                if (message == null) break; // End of stream
                                onMessageReceived(message);
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when the cancellation token is triggered
                Console.WriteLine("Pipe server operation canceled.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in pipe server: {ex.Message}");
            }
            finally
            {
                Console.WriteLine("Named pipe server stopped.");
            }
        }


        private void OnHubMessageReceived(string message)
        {
            //Console.WriteLine(message);
        }

        private void DataGridUpdates_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedUp = GetSelectedUpdate();
            bool showCumulative = false;
            if (selectedUp != null)
            {
                var unityVer = GetSelectedUpdate().Version;
                showCumulative = Tools.HasAlphaReleaseNotes(unityVer);
            }
            btnShowCumulatedReleaseNotes.IsEnabled = showCumulative;
        }

        // bring old window to front, but needs matching appname.. https://stackoverflow.com/a/36804161/5452781
        private static void ActivateOtherWindow()
        {
            var other = FindWindow(null, appName);
            if (other != IntPtr.Zero)
            {
                SetForegroundWindow(other);
                if (IsIconic(other)) OpenIcon(other);
            }
        }

        void HandleCommandLineLaunch()
        {
            // check if received -projectPath argument (that means opening from explorer / cmdline)
            string[] args = Environment.GetCommandLineArgs();
            if (args != null && args.Length > 2)
            {
                // first argument needs to be -projectPath
                var commandLineArgs = args[1];

                // if install argument, then just try to install this file (APK)
                if (commandLineArgs == "-install")
                {
                    Console.WriteLine("Launching from commandline...");

                    // path
                    var apkPath = args[2];

                    // resolve full path if path parameter isn't a rooted path
                    //if (!Path.IsPathRooted(apkPath))
                    //{
                    //    apkPath = Directory.GetCurrentDirectory() + apkPath;
                    //}
                    //MessageBox.Show("APK install not implemented yet: " + apkPath);
                    // try installing it
                    Tools.InstallAPK(apkPath);
                    Environment.Exit(0);
                }
                else if (commandLineArgs == "-projectPath")
                {
                    Console.WriteLine("Launching project from commandline...");

                    // path
                    var projectPathArgument = args[2];

                    // resolve full path if path parameter isn't a rooted path
                    if (!Path.IsPathRooted(projectPathArgument))
                    {
                        projectPathArgument = Directory.GetCurrentDirectory() + projectPathArgument;
                    }

                    var version = Tools.GetProjectVersion(projectPathArgument);

                    // take extra arguments also
                    var commandLineArguments = "";
                    for (int i = 3, len = args.Length; i < len; i++)
                    {
                        commandLineArguments += " " + args[i];
                    }

                    var proj = new Project();
                    proj.Version = version;
                    proj.Path = projectPathArgument;
                    proj.Arguments = commandLineArguments;

                    // no version info or check if force-update button is down
                    // NOTE if keydown, window doesnt become active and focused
                    if (string.IsNullOrEmpty(version) || (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                    {
                        // if Assets folder exists, then its existing project
                        if (Directory.Exists(Path.Combine(proj.Path, "Assets")) == true && (Directory.GetFiles(Path.Combine(proj.Path, "Assets")).Length > 0))
                        {
                            bool useInitScript = (bool)chkUseInitScript.IsChecked;

                            // if not editors found, then dont open commandline?
                            if (unityInstallationsSource.Count > 0)
                            {
                                Tools.DisplayUpgradeDialog(proj, null, useInitScript);
                            }
                            else
                            {
                                MessageBox.Show("No Unity installations found. Please setup Unity Editor root folders first by running UnityLauncherPro.", "No Unity Installations found", MessageBoxButton.OK, MessageBoxImage.Warning);
                                // TODO display setup tab
                            }

                        }
                        else // no Assets folder here OR Assets folder is empty, then its new project
                        {
                            CreateNewEmptyProject(proj.Path);
                        }
                    }
                    else // has version info, just launch it
                    {
                        // try launching it through
                        bool launchedViaPipe = LaunchProjectViaPipe(proj);

                        if (launchedViaPipe == false)
                        {
                            var proc = Tools.LaunchProject(proj);
                            //proj.Process = proc;
                            //ProcessHandler.Add(proj, proc);
                        }
                    }

                    // quit after launch if enabled in settings
                    if (Settings.Default.closeAfterExplorer == true)
                    {
                        Environment.Exit(0);
                    }
                    //SetStatus("Ready");
                }
                else
                {
                    Console.WriteLine("Error> Invalid arguments:" + args[1]);
                }
            }
        } // HandleCommandLineLaunch()

        private bool LaunchProjectViaPipe(Project proj)
        {
            string sep = "<|>";
            try
            {
                using (var client = new NamedPipeClientStream(".", launcherPipeName, PipeDirection.Out))
                {
                    client.Connect(500);
                    using (var writer = new StreamWriter(client))
                    {
                        writer.AutoFlush = true;
                        writer.WriteLine("OpenProject:" + sep + proj.Version + sep + proj.Path + sep + proj.Arguments);
                    }
                }
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error launching via pipe: " + ex.Message);
                return false;
            }
        }

        // main search
        void FilterRecentProjects()
        {
            // https://www.wpftutorial.net/DataViews.html
            _filterString = txtSearchBox.Text;

            if (_filterString.IndexOf(' ') > -1)
            {
                multiWordSearch = true;
                searchWords = _filterString.Split(' ');
            }
            else
            {
                multiWordSearch = false;
            }


            ICollectionView collection = CollectionViewSource.GetDefaultView(projectsSource);
            collection.Filter = ProjectFilter;
            // set first row selected, if only 1 row
            if (gridRecent.Items.Count == 1)
            {
                gridRecent.SelectedIndex = 0;
            }
        }

        void FilterUpdates()
        {
            _filterString = txtSearchBoxUpdates.Text.Trim();
            ICollectionView collection = CollectionViewSource.GetDefaultView(dataGridUpdates.ItemsSource);
            if (collection == null) return;

            collection.Filter = UpdatesFilter;
            if (dataGridUpdates.Items.Count > 0)
            {
                dataGridUpdates.SelectedIndex = 0;
            }
        }

        void FilterUnitys()
        {
            _filterString = txtSearchBoxUnity.Text.Trim();
            ICollectionView collection = CollectionViewSource.GetDefaultView(dataGridUnitys.ItemsSource);
            collection.Filter = UnitysFilter;
            if (dataGridUnitys.Items.Count > 0)
            {
                dataGridUnitys.SelectedIndex = 0;
            }
        }

        void FilterBuildReport()
        {
            _filterString = txtSearchBoxBuildReport.Text;
            ICollectionView collection = CollectionViewSource.GetDefaultView(gridBuildReport.ItemsSource);
            collection.Filter = BuildReportFilter;
            //if (gridBuildReport.Items.Count > 0)
            //{
            //    gridBuildReport.SelectedIndex = 0;
            //}
        }

        private bool ProjectFilter(object item)
        {
            Project proj = item as Project;

            // split search string by space, if it contains space
            if (multiWordSearch == true)
            {
                bool found = true;
                foreach (var word in searchWords)
                {
                    bool titleMatched = proj.Title.IndexOf(word, 0, StringComparison.OrdinalIgnoreCase) != -1;
                    bool pathMatched = searchProjectPathAlso && proj.Path.IndexOf(word, 0, StringComparison.OrdinalIgnoreCase) != -1;
                    found = found && (titleMatched || pathMatched);
                }
                return found;
            }
            else // single word search
            {
                bool titleMatched = proj.Title.IndexOf(_filterString, 0, StringComparison.OrdinalIgnoreCase) != -1;
                bool pathMatched = searchProjectPathAlso && proj.Path.IndexOf(_filterString, 0, StringComparison.OrdinalIgnoreCase) != -1;

                return titleMatched || pathMatched;
            }
        }

        private bool UpdatesFilter(object item)
        {
            if (!(item is UnityVersion unityVersion))
            {
                return false;
            }

            bool haveSearchString = string.IsNullOrEmpty(_filterString) == false;
            bool matchString = haveSearchString && unityVersion.Version.IndexOf(_filterString, 0, StringComparison.CurrentCultureIgnoreCase) > -1;

            bool checkedAlls = (bool)rdoAll.IsChecked;
            bool checkedLTSs = (bool)rdoLTS.IsChecked;
            bool checkedTechs = (bool)rdoTech.IsChecked;
            bool checkedAlphas = (bool)rdoAlphas.IsChecked;
            bool checkedBetas = (bool)rdoBetas.IsChecked;

            bool matchLTS = checkedLTSs && unityVersion.Stream == UnityVersionStream.LTS;
            bool matchTech = checkedTechs && unityVersion.Stream == UnityVersionStream.Tech;
            bool matchAlphas = checkedAlphas && unityVersion.Stream == UnityVersionStream.Alpha;
            bool matchBetas = checkedBetas && unityVersion.Stream == UnityVersionStream.Beta;

            // match search string and some radiobutton
            if (haveSearchString == true)
            {
                if (checkedAlls) return matchString;
                if (checkedLTSs) return matchString && matchLTS;
                if (checkedTechs) return matchString && matchTech;
                if (checkedAlphas) return matchString && matchAlphas;
                if (checkedBetas) return matchString && matchBetas;
            }
            else // no search text, filter by radiobuttons
            {
                if (checkedAlls || matchLTS || matchTech || matchAlphas || matchBetas) return true;
            }

            // fallback
            return matchString;
        }

        private bool UnitysFilter(object item)
        {
            UnityInstallation unity = item as UnityInstallation;
            return (unity.Version?.IndexOf(_filterString, 0, StringComparison.CurrentCultureIgnoreCase) != -1) || (unity.ReleaseType?.IndexOf(_filterString, 0, StringComparison.CurrentCultureIgnoreCase) != -1) || (unity.PlatformsCombined?.IndexOf(_filterString, 0, StringComparison.CurrentCultureIgnoreCase) != -1);
        }

        private bool BuildReportFilter(object item)
        {
            BuildReportItem reportItem = item as BuildReportItem;
            return (reportItem.Path.IndexOf(_filterString, 0, StringComparison.CurrentCultureIgnoreCase) != -1);
        }

        void LoadSettings()
        {
            // debug, print filename
            //Console.WriteLine(ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal).FilePath);

            // catch corrupted config file
            try
            {
                Settings.Default.Reload();
                // form size
                this.Width = Settings.Default.windowWidth;
                this.Height = Settings.Default.windowHeight;

                chkMinimizeToTaskbar.IsChecked = Settings.Default.minimizeToTaskbar;
                chkRegisterExplorerMenu.IsChecked = Settings.Default.registerExplorerMenu;
                chkRegisterInstallAPKMenu.IsChecked = Settings.Default.registerExplorerMenuAPK;

                // update settings window
                chkQuitAfterCommandline.IsChecked = Settings.Default.closeAfterExplorer;
                chkQuitAfterOpen.IsChecked = Settings.Default.closeAfterProject;
                chkShowLauncherArgumentsColumn.IsChecked = Settings.Default.showArgumentsColumn;
                chkShowGitBranchColumn.IsChecked = Settings.Default.showGitBranchColumn;
                chkGetGitBranchRecursively.IsChecked = Settings.Default.searchGitFolderRecursivly; // FIXME typo
                chkCheckPlasticBranch.IsChecked = Settings.Default.checkPlasticBranch;
                chkShowMissingFolderProjects.IsChecked = Settings.Default.showProjectsMissingFolder;
                chkAllowSingleInstanceOnly.IsChecked = Settings.Default.AllowSingleInstanceOnly;
                chkAskNameForQuickProject.IsChecked = Settings.Default.askNameForQuickProject;
                chkEnableProjectRename.IsChecked = Settings.Default.enableProjectRename;
                chkStreamerMode.IsChecked = Settings.Default.streamerMode;
                chkShowPlatform.IsChecked = Settings.Default.showTargetPlatform;
                chkCheckSRP.IsChecked = Settings.Default.checkSRP;
                chkUseCustomTheme.IsChecked = Settings.Default.useCustomTheme;
                txtRootFolderForNewProjects.Text = Settings.Default.newProjectsRoot;
                txtWebglRelativePath.Text = Settings.Default.webglBuildPath;
                txtCustomThemeFile.Text = Settings.Default.themeFile;
                useAlphaReleaseNotesSite.IsChecked = Settings.Default.useAlphaReleaseNotes;
                useUnofficialReleaseList.IsChecked = Settings.Default.useUnofficialReleaseList;
                chkDisableUnityHubLaunch.IsChecked = Settings.Default.disableUnityHubLaunch;
                chkFetchAdditionalInfo.IsChecked = Settings.Default.fetchAdditionalInfo;
                chkFetchOnlineTemplates.IsChecked = Settings.Default.fetchOnlineTemplates;

                chkEnablePlatformSelection.IsChecked = Settings.Default.enablePlatformSelection;
                chkRunAutomatically.IsChecked = Settings.Default.runAutomatically;
                chkRunAutomaticallyMinimized.IsChecked = Settings.Default.runAutomaticallyMinimized;

                // update optional grid columns, hidden or visible
                gridRecent.Columns[4].Visibility = (bool)chkShowLauncherArgumentsColumn.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                gridRecent.Columns[5].Visibility = (bool)chkShowGitBranchColumn.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                gridRecent.Columns[6].Visibility = (bool)chkShowPlatform.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                gridRecent.Columns[7].Visibility = (bool)chkCheckSRP.IsChecked ? Visibility.Visible : Visibility.Collapsed;

                // update installations folder listbox
                lstRootFolders.Items.Clear();

                // check if no installation root folders are added, then add default folder(s), this usually happens only on first run (or if user has not added any folders)
                if (Settings.Default.rootFolders.Count == 0)
                {
                    // default hub installation folder
                    string baseFolder = "\\Program Files\\Unity\\Hub\\Editor";
                    string baseFolder2 = "\\Program Files\\";
                    string defaultFolder1 = "C:" + baseFolder;
                    string defaultFolder2 = "D:" + baseFolder;
                    string defaultFolder3 = "C:" + baseFolder2;
                    string defaultFolder4 = "D:" + baseFolder2;
                    if (Directory.Exists(defaultFolder1))
                    {
                        Properties.Settings.Default.rootFolders.Add(defaultFolder1);
                    }
                    else if (Directory.Exists(defaultFolder2))
                    {
                        Properties.Settings.Default.rootFolders.Add(defaultFolder2);
                    }
                    else if (Directory.Exists(defaultFolder3))
                    {
                        if (GetUnityInstallations.HasUnityInstallations(defaultFolder3))
                        {
                            Properties.Settings.Default.rootFolders.Add(defaultFolder3);
                        }
                        else if (GetUnityInstallations.HasUnityInstallations(defaultFolder4))
                        {
                            Properties.Settings.Default.rootFolders.Add(defaultFolder4);
                        }
                    }
                }

                lstRootFolders.ItemsSource = Properties.Settings.Default.rootFolders;

                // restore recent project datagrid column widths
                int[] gridColumnWidths = Properties.Settings.Default.gridColumnWidths;
                if (gridColumnWidths != null)
                {
                    for (int i = 0; i < gridColumnWidths.Length; ++i)
                    {
                        if (i >= gridRecent.Columns.Count) break; // too many columns were saved, probably some test columns
                        gridRecent.Columns[i].Width = gridColumnWidths[i];
                    }
                }

                // restore buildreport datagrid column widths
                gridColumnWidths = Properties.Settings.Default.gridColumnWidthsBuildReport;
                if (gridColumnWidths != null)
                {
                    for (int i = 0; i < gridColumnWidths.Length; ++i)
                    {
                        if (i >= gridBuildReport.Columns.Count) break; // too many columns were saved, probably some test columns
                        gridBuildReport.Columns[i].Width = gridColumnWidths[i];
                    }
                }

                // other setting vars
                preferredVersion = Settings.Default.preferredVersion;

                // get last modified date format
                chkUseCustomLastModified.IsChecked = Settings.Default.useCustomLastModified;
                txtCustomDateTimeFormat.Text = Settings.Default.customDateFormat;

                if (Settings.Default.useCustomLastModified)
                {
                    currentDateFormat = Settings.Default.customDateFormat;
                }
                else // use default
                {
                    currentDateFormat = defaultDateFormat;
                }

                chkHumanFriendlyDateTime.IsChecked = Settings.Default.useHumandFriendlyLastModified;
                // if both enabled, then disable custom
                if (chkHumanFriendlyDateTime.IsChecked == true && chkUseCustomLastModified.IsChecked == true)
                {
                    chkUseCustomLastModified.IsChecked = false;
                }

                useHumanFriendlyDateFormat = Settings.Default.useHumandFriendlyLastModified;
                searchProjectPathAlso = Settings.Default.searchProjectPathAlso;
                chkSearchProjectPath.IsChecked = searchProjectPathAlso;

                // recent grid column display index order
                var order = Settings.Default.recentColumnsOrder;

                // if we dont have any values, get & set them now
                // also, if user has disabled optional columns, saved order must be reset to default
                if (order == null || gridRecent.Columns.Count != Settings.Default.recentColumnsOrder.Length)
                {
                    Settings.Default.recentColumnsOrder = new Int32[gridRecent.Columns.Count];
                    for (int i = 0; i < gridRecent.Columns.Count; i++)
                    {
                        Settings.Default.recentColumnsOrder[i] = gridRecent.Columns[i].DisplayIndex;
                    }
                    Settings.Default.Save();
                }
                else // load existing order
                {
                    for (int i = 0; i < gridRecent.Columns.Count; i++)
                    {
                        if (Settings.Default.recentColumnsOrder[i] > -1)
                        {
                            gridRecent.Columns[i].DisplayIndex = Settings.Default.recentColumnsOrder[i];
                        }
                    }
                }

                adbLogCatArgs = Settings.Default.adbLogCatArgs;
                txtLogCatArgs.Text = adbLogCatArgs;

                projectNameSetting = Settings.Default.projectName;
                switch (projectNameSetting)
                {
                    case 0:
                        radioProjNameFolder.IsChecked = true;
                        break;
                    case 1:
                        radioProjNameProductName.IsChecked = true;
                        break;
                    default:
                        radioProjNameFolder.IsChecked = true;
                        break;
                }

                // set default .bat folder location to appdata/.., if nothing is set, or current one is invalid
                if (string.IsNullOrEmpty(txtShortcutBatchFileFolder.Text) || Directory.Exists(txtShortcutBatchFileFolder.Text) == false)
                {
                    Settings.Default.shortcutBatchFileFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appName);
                    txtShortcutBatchFileFolder.Text = Settings.Default.shortcutBatchFileFolder;
                }

                chkUseInitScript.IsChecked = Settings.Default.useInitScript;
                txtCustomInitFileURL.Text = Settings.Default.customInitFileURL;

                // load webgl port
                txtWebglPort.Text = "" + Settings.Default.webglPort;
                webglPort = Settings.Default.webglPort;

                txtMaxProjectCount.Text = Settings.Default.maxProjectCount.ToString();
                chkOverride40ProjectCount.IsChecked = Settings.Default.override40ProjectCount;
                if ((bool)chkOverride40ProjectCount.IsChecked)
                {
                    maxProjectCount = Settings.Default.maxProjectCount;
                }
                else
                {
                    maxProjectCount = 40;
                }
            }
            catch (ConfigurationErrorsException ex)
            {
                string filename = ((ConfigurationErrorsException)ex.InnerException).Filename;

                var res = MessageBox.Show("This may be due to a Windows crash/BSOD.\n" +
                                      "Click 'Yes' to use automatic backup (if exists, otherwise settings are reset), then start application again.\n\n" +
                                      "Click 'No' to reset config file (you'll need to setup settings again)\n\n" +
                                      "Click 'Cancel' to exit now (and delete user.config manually)\n\nCorrupted file: " + filename,
                                      appName + " - Corrupt user settings",
                                      MessageBoxButton.YesNoCancel,
                                      MessageBoxImage.Error);

                if (res == MessageBoxResult.Yes)
                {
                    // try to use backup
                    string backupFilename = filename + ".bak";
                    if (File.Exists(backupFilename))
                    {
                        File.Copy(backupFilename, filename, true);
                    }
                    else
                    {
                        File.Delete(filename);
                    }
                }
                else if (res == MessageBoxResult.No)
                {
                    File.Delete(filename);
                }
                else if (res == MessageBoxResult.Cancel)
                {
                    Tools.ExploreFolder(Path.GetDirectoryName(filename));
                }

                // need to restart, otherwise settings not loaded
                Process.GetCurrentProcess().Kill();
            }
        } // LoadSettings()

        private void SaveSettingsOnExit()
        {
            // save recent project column widths
            List<int> gridWidths;

            // if we dont have any settings yet
            if (Settings.Default.gridColumnWidths != null)
            {
                gridWidths = new List<int>(Settings.Default.gridColumnWidths);
            }
            else
            {
                gridWidths = new List<int>();
            }

            // get data grid view widths
            var column = gridRecent.Columns[0];
            for (int i = 0; i < gridRecent.Columns.Count; ++i)
            {
                if (Settings.Default.gridColumnWidths != null && Settings.Default.gridColumnWidths.Length > i)
                {
                    gridWidths[i] = (int)gridRecent.Columns[i].Width.Value;
                }
                else
                {
                    gridWidths.Add((int)gridRecent.Columns[i].Width.Value);
                }
            }
            Settings.Default.gridColumnWidths = gridWidths.ToArray();
            Settings.Default.Save();


            // save buildrepot column widths
            gridWidths.Clear();

            // if we dont have any settings yet
            if (Settings.Default.gridColumnWidthsBuildReport != null)
            {
                gridWidths = new List<int>(Settings.Default.gridColumnWidthsBuildReport);
            }
            else
            {
                gridWidths = new List<int>();
            }

            // get data grid view widths
            column = gridBuildReport.Columns[0];
            for (int i = 0; i < gridBuildReport.Columns.Count; ++i)
            {
                if (Settings.Default.gridColumnWidthsBuildReport != null && Settings.Default.gridColumnWidthsBuildReport.Length > i)
                {
                    gridWidths[i] = (int)gridBuildReport.Columns[i].Width.Value;
                }
                else
                {
                    gridWidths.Add((int)gridBuildReport.Columns[i].Width.Value);
                }
            }
            Settings.Default.gridColumnWidthsBuildReport = gridWidths.ToArray();
            Settings.Default.projectName = projectNameSetting;
            Settings.Default.Save();

            // make backup
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal);
            var FullFileName = config.FilePath;
            File.Copy(FullFileName, FullFileName + ".bak", true);
        }

        void UpdateUnityInstallationsList()
        {
            // Reset preferred string, if user changed it
            //preferredVersion = "none";

            unityInstallationsSource = GetUnityInstallations.Scan();
            dataGridUnitys.ItemsSource = unityInstallationsSource;

            // Also make dictionary of installed unitys, to search faster
            unityInstalledVersions.Clear();

            for (int i = 0, len = unityInstallationsSource.Count; i < len; i++)
            {
                var version = unityInstallationsSource[i].Version;
                // NOTE cannot have same version id in 2 places with this
                if (string.IsNullOrEmpty(version) == false && unityInstalledVersions.ContainsKey(version) == false)
                {
                    unityInstalledVersions.Add(version, unityInstallationsSource[i].Path);
                }
            }

            lblFoundXInstallations.Content = "Found " + unityInstallationsSource.Count + " installations";
        }

        Project GetSelectedProject()
        {
            return (Project)gridRecent.SelectedItem;
        }

        UnityInstallation GetSelectedUnity()
        {
            return (UnityInstallation)dataGridUnitys.SelectedItem;
        }

        UnityVersion GetSelectedUpdate()
        {
            return (UnityVersion)dataGridUpdates.SelectedItem;
        }

        BuildReportItem GetSelectedBuildItem()
        {
            return (BuildReportItem)gridBuildReport.SelectedItem;
        }

        void AddUnityInstallationRootFolder()
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "Select Unity installations root folder";

            var result = dialog.ShowDialog();
            var newRoot = dialog.SelectedPath;

            if (lstRootFolders.Items.Contains(newRoot) == true)
            {
                SetStatus("Folder already exists in the list!", MessageType.Error);
                return;
            }

            if (String.IsNullOrWhiteSpace(newRoot) == false && Directory.Exists(newRoot) == true)
            {
                Settings.Default.rootFolders.Add(newRoot);
                lstRootFolders.Items.Refresh();
                Settings.Default.Save();
                UpdateUnityInstallationsList();
                RefreshRecentProjects();
            }
        }

        // waits for unity update results and assigns to datagrid
        async Task CallGetUnityUpdates()
        {
            dataGridUpdates.ItemsSource = null;
            var task = GetUnityUpdates.FetchAll((bool)useUnofficialReleaseList.IsChecked);
            var items = await task;
            updatesSource = items.ToArray();
            if (updatesSource == null) return;
            dataGridUpdates.ItemsSource = updatesSource;
            // if search string is set, then filter it (after data is loaded)
            if (string.IsNullOrEmpty(txtSearchBoxUpdates.Text) == false)
            {
                FilterUpdates();
            }
        }

        async void GoLookForUpdatesForThisUnity()
        {
            // call for updates list fetch
            await CallGetUnityUpdates();

            var unity = GetSelectedUnity();
            if (unity == null || string.IsNullOrEmpty(unity.Version) == true) return;

            if (dataGridUpdates.ItemsSource != null)
            {
                tabControl.SelectedIndex = 2;
                // need to clear old results first
                txtSearchBoxUpdates.Text = "";
                // reset filter
                rdoAll.IsChecked = true;

                // NOTE for now, just set filter to current version, minus patch version "2021.1.7f1" > "2021.1"
                txtSearchBoxUpdates.Text = unity.Version.Substring(0, unity.Version.LastIndexOf('.'));
            }
        }

        public void RefreshRecentProjects()
        {
            // clear search
            txtSearchBox.Text = "";
            // take currently selected project row
            lastSelectedProjectIndex = gridRecent.SelectedIndex;
            // rescan recent projects
            projectsSource = GetProjects.Scan(getGitBranch: (bool)chkShowGitBranchColumn.IsChecked, getPlasticBranch: (bool)chkCheckPlasticBranch.IsChecked, getArguments: (bool)chkShowLauncherArgumentsColumn.IsChecked, showMissingFolders: (bool)chkShowMissingFolderProjects.IsChecked, showTargetPlatform: (bool)chkShowPlatform.IsChecked, AllProjectPaths: Settings.Default.projectPaths, searchGitbranchRecursively: (bool)chkGetGitBranchRecursively.IsChecked, showSRP: (bool)chkCheckSRP.IsChecked);
            gridRecent.ItemsSource = projectsSource;

            // fix sorting on refresh
            foreach (DataGridColumn column in gridRecent.Columns)
            {
                if (column.Header.ToString() == Settings.Default.currentSortColumn)
                {
                    // TODO FIXME, hack for correct direction on refresh only
                    Settings.Default.currentSortDirectionAscending = !Settings.Default.currentSortDirectionAscending;
                    var g = new DataGridSortingEventArgs(column);
                    SortHandlerRecentProjects(gridRecent, g);
                    break;
                }
            }

            // focus back
            Tools.SetFocusToGrid(gridRecent, lastSelectedProjectIndex);
            SetStatus("Ready (" + projectsSource.Count + " projects)");
        }

        //
        //
        // EVENTS
        //
        //

        // maximize window
        void NotifyIcon_MouseClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            RestoreFromTray();
        }

        void RestoreFromTray()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.ShowInTaskbar = true;
            notifyIcon.Visible = false;
            // NOTE workaround for grid not focused when coming back from minimized window
            Tools.SetFocusToGrid(gridRecent, gridRecent.SelectedIndex);
        }

        private void OnRectangleMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) this.DragMove();
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            FilterRecentProjects();

            // if nothing selected, select first item
            if (gridRecent.SelectedIndex < 0) gridRecent.SelectedIndex = 0;
        }

        private void BtnAddProjectFolder_Click(object sender, RoutedEventArgs e)
        {
            var folder = Tools.BrowseForOutputFolder("Select Project Folder to Add it Into Projects List");
            if (string.IsNullOrEmpty(folder) == false)
            {
                var proj = GetNewProjectData(folder);
                //AddNewProjectToList(proj);
                Tools.AddProjectToHistory(proj.Path);
                // clear search, so can see added project
                txtSearchBox.Text = "";
                RefreshRecentProjects();
                // NOTE 0 works for sort-by-date only
                Tools.SetFocusToGrid(gridRecent, 0);
            }
        }

        Project GetNewProjectData(string folder)
        {
            var p = new Project();
            p.Path = folder;
            p.Title = Path.GetFileName(folder);
            p.Version = Tools.GetProjectVersion(folder);
            p.Arguments = Tools.ReadCustomProjectData(folder, launcherArgumentsFile);
            if ((bool)chkShowPlatform.IsChecked == true) p.TargetPlatform = Tools.GetTargetPlatform(folder);
            if ((bool)chkShowGitBranchColumn.IsChecked == true) p.GITBranch = Tools.ReadGitBranchInfo(folder, (bool)chkGetGitBranchRecursively.IsChecked);
            return p;
        }

        void AddNewProjectToList(Project proj)
        {
            projectsSource.Insert(0, proj);
            gridRecent.SelectedIndex = 0;
            Tools.SetFocusToGrid(gridRecent);
            // force refresh
            txtSearchBox.Text = proj.Title;
            txtSearchBox.Text = "";
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            CloseThemeEditor();

            // remove focus from minimize button
            gridRecent.Focus();
            if (chkMinimizeToTaskbar.IsChecked == true)
            {
                notifyIcon.Visible = true;
                this.Hide();
            }
            else
            {
                this.WindowState = WindowState.Minimized;
            }
        }

        private void OnGetUnityUpdatesClick(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            button.IsEnabled = false;

            // refresh installations, if already added some new ones
            UpdateUnityInstallationsList();
            txtSearchBoxUpdates.Text = "";
            // clear filters, since right now they are not used after updates are loaded
            rdoAll.IsChecked = true;
            CallGetUnityUpdates();

            button.IsEnabled = true;
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            // disable alt key?
            /*
            if (Keyboard.Modifiers == ModifierKeys.Alt)
            {
                e.Handled = true;
            }*/

            switch (tabControl.SelectedIndex)
            {
                case 0: // Projects

                    switch (e.Key)
                    {
                        case Key.LeftCtrl: // used for ctrl+c
                            break;

                        case Key.Escape: // clear project search
                            if (txtSearchBox.Text == "")
                            {
                                // its already clear
                            }
                            else // we have text in searchbox, clear it
                            {
                                txtSearchBox.Text = "";
                            }
                            // try to keep selected row selected and in view
                            Tools.SetFocusToGrid(gridRecent);
                            break;
                        case Key.F5:
                            txtSearchBox.Text = "";
                            break;
                        case Key.Up:
                        case Key.Down:
                        case Key.Left:
                        case Key.Right:
                            break;
                        case Key.F2: // edit arguments or project name
                            if (chkEnableProjectRename.IsChecked == false) return; //if rename not enabled

                            // if not inside datagrid, cancel
                            if (Tools.HasFocus(this, gridRecent, true) == false) return;

                            var cell = gridRecent.CurrentCell;
                            // if in first cell (or no cell)
                            if (cell.Column.DisplayIndex == 0)
                            {
                                // enable cell edit
                                cell.Column.IsReadOnly = false;
                                // start editing that cell
                                gridRecent.CurrentCell = new DataGridCellInfo(gridRecent.Items[gridRecent.SelectedIndex], gridRecent.Columns[cell.Column.DisplayIndex]);
                                gridRecent.BeginEdit();
                            }
                            break;
                        default: // any key anykey
                            // cancel if editing cell
                            IEditableCollectionView itemsView = gridRecent.Items;
                            if (itemsView.IsAddingNew || itemsView.IsEditingItem) return;

                            // skip these keys
                            if (Keyboard.Modifiers == ModifierKeys.Alt) return;
                            if (Keyboard.Modifiers == ModifierKeys.Control) return;

                            // activate searchbar if not active and we are in tab#1
                            if (txtSearchBox.IsFocused == false)
                            {
                                txtSearchBox.Focus();
                                txtSearchBox.Select(txtSearchBox.Text.Length, 0);
                            }
                            break;
                    }

                    break;
                case 1: // Unitys/Editors

                    switch (e.Key)
                    {
                        case Key.F5: // refresh unitys
                            UpdateUnityInstallationsList();
                            break;
                        case Key.Escape: // clear project search
                            txtSearchBoxUnity.Text = "";
                            break;
                        default:
                            if (txtSearchBoxUnity.IsFocused == false)
                            {
                                txtSearchBoxUnity.Focus();
                                txtSearchBoxUnity.Select(txtSearchBoxUnity.Text.Length, 0);
                            }
                            break;
                    }
                    break;

                case 2: // Updates

                    switch (e.Key)
                    {
                        case Key.F5: // refresh unitys
                            CallGetUnityUpdates();
                            break;
                        case Key.Escape: // clear project search
                            txtSearchBoxUpdates.Text = "";
                            break;
                    }
                    break;

                case 3: // Tools

                    switch (e.Key)
                    {
                        case Key.Escape: // clear search
                            txtSearchBoxBuildReport.Text = "";
                            break;
                    }
                    break;
                default:
                    break;
            }

        }

        private void RemoveProjectFromList(bool confirm = false)
        {
            var proj = GetSelectedProject();
            if (proj == null) return;

            if (confirm == true)
            {
                // streamer mode, show first char and last 3 chars, rest as *
                var cleantitle = proj.Title[0] + new string('*', proj.Title.Length - 1);
                var title = chkStreamerMode.IsChecked == true ? cleantitle : proj.Title;
                var result = MessageBox.Show("Are you sure you want to remove project from list?\n\n" + title, "Remove project", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.No) return;
            }

            if (GetProjects.RemoveRecentProject(proj.Path))
            {
                RefreshRecentProjects();
            }
            else
            {
                // we had added this project manually, without opening yet, just remove item
                projectsSource.Remove(proj);
                gridRecent.Items.Refresh();
                Tools.SetFocusToGrid(gridRecent);
                gridRecent.SelectedIndex = 0;
            }

            // NOTE this doesnt remove from settings list?
        }

        private async void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // if going into updates tab, fetch list (first time only)
            if (tabControl.SelectedIndex == (int)Tabs.Updates)
            {
                // if we dont have previous results yet, TODO scan again if previous was 24hrs ago
                if (updatesSource == null)
                {
                    var task = GetUnityUpdates.FetchAll((bool)useUnofficialReleaseList.IsChecked);
                    var items = await task;
                    if (task.IsCompleted == false || task.IsFaulted == true) return;
                    if (items == null) return;
                    updatesSource = items.ToArray();
                    if (updatesSource == null) return;
                    dataGridUpdates.ItemsSource = updatesSource;
                    // if search string is set, then filter it (after data is loaded)
                    if (string.IsNullOrEmpty(txtSearchBoxUpdates.Text) == false)
                    {
                        FilterUpdates();
                    }
                }
            }
        }

        private void OnClearProjectSearchClick(object sender, RoutedEventArgs e)
        {
            txtSearchBox.Text = "";
        }

        private void OnClearUnitySearchClick(object sender, RoutedEventArgs e)
        {
            txtSearchBoxUnity.Text = "";
        }

        private void OnClearUpdateSearchClick(object sender, RoutedEventArgs e)
        {
            // FIXME doesnt hide button, becaus button should have opposite of Text.IsEmpty, or custom style to hide when not empty
            txtSearchBoxUpdates.Text = "";
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            // (TODO force) close theme editor, if still open, TODO NEED to cancel all changes
            CloseThemeEditor();
            SaveSettingsOnExit();
            CloseHubPipeAsync();
        }

        private void CloseThemeEditor()
        {
            if (themeEditorWindow != null) themeEditorWindow.Close();
        }


        // save window size after resize
        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            var win = (Window)sender;
            // save new size instead, to fix DPI scaling issue
            Properties.Settings.Default.windowWidth = (int)e.NewSize.Width;
            Properties.Settings.Default.windowHeight = (int)e.NewSize.Height;
            Properties.Settings.Default.Save();
        }

        private void BtnLaunchProject_Click(object sender, RoutedEventArgs e)
        {
            var proj = GetSelectedProject();
            var proc = Tools.LaunchProject(proj, gridRecent);
            //ProcessHandler.Add(proj, proc);
            Tools.SetFocusToGrid(gridRecent);
        }

        private void BtnExplore_Click(object sender, RoutedEventArgs e)
        {
            var proj = GetSelectedProject();
            Tools.ExploreFolder(proj?.Path);
            Tools.SetFocusToGrid(gridRecent);
        }

        // copy selected row unity version to clipboard
        private void MenuItemCopyVersion_Click(object sender, RoutedEventArgs e)
        {
            string copy = null;
            if (tabControl.SelectedIndex == 0)
            {
                var proj = GetSelectedProject();
                copy = proj?.Version;
            }
            else if (tabControl.SelectedIndex == 1)
            {
                var unity = GetSelectedUnity();
                copy = unity?.Version;
            }
            else if (tabControl.SelectedIndex == 2)
            {
                var unity = GetSelectedUpdate();
                copy = unity?.Version;
            }

            if (copy != null) Clipboard.SetText(copy);
        }

        // get download url for selected update version
        private async void MenuItemCopyUpdateDownloadURL_Click(object sender, RoutedEventArgs e)
        {
            var unity = GetSelectedUpdate();
            string exeURL = await GetUnityUpdates.FetchDownloadUrl(unity?.Version);
            if (exeURL != null) Clipboard.SetText(exeURL);
        }

        private void BtnRefreshProjectList_Click(object sender, RoutedEventArgs e)
        {
            // we want to refresh unity installs also, to make sure version colors are correct
            UpdateUnityInstallationsList();
            RefreshRecentProjects();
        }

        // run unity only
        private void BtnLaunchUnity_Click(object sender, RoutedEventArgs e)
        {
            var proj = GetSelectedProject();
            var unitypath = Tools.GetUnityExePath(proj?.Version);
            Tools.LaunchExe(unitypath);
        }

        private void BtnUpgradeProject_Click(object sender, RoutedEventArgs e)
        {
            var proj = GetSelectedProject();
            if (proj == null) return;

            Tools.DisplayUpgradeDialog(proj: proj, owner: this, useInitScript: false);
        }

        private void GridRecent_Loaded(object sender, RoutedEventArgs e)
        {
            // if coming from explorer launch, and missing unity version, projectsource is still null?
            if (projectsSource != null) SetStatus("Ready (" + projectsSource.Count + " projects)");
            RefreshSorting();
            //Tools.SetFocusToGrid(gridRecent);
            Dispatcher.InvokeAsync(() => Tools.SetFocusToGrid(gridRecent), DispatcherPriority.ApplicationIdle);

        }

        void RefreshSorting()
        {
            // use saved sort columns
            if (string.IsNullOrEmpty(Settings.Default.currentSortColumn) == false)
            {
                // check if that column exists in headers
                foreach (DataGridColumn column in gridRecent.Columns)
                {
                    if (column.Header.ToString() == Settings.Default.currentSortColumn)
                    {
                        // TODO FIXME Project binding is to Title, not projects
                        Settings.Default.currentSortColumn = Settings.Default.currentSortColumn.Replace("Project", "Title");

                        // TODO FIXME, hack for correct direction on this refresh
                        Settings.Default.currentSortDirectionAscending = !Settings.Default.currentSortDirectionAscending;
                        var g = new DataGridSortingEventArgs(column);
                        SortHandlerRecentProjects(gridRecent, g);
                        break;
                    }
                }
            }
        }

        private void BtnExploreUnity_Click(object sender, RoutedEventArgs e)
        {
            var unity = GetSelectedUnity();
            if (unity == null) return;
            var path = Path.GetDirectoryName(unity.Path);
            Tools.LaunchExplorer(path);
        }

        private void BtnRunUnity_Click(object sender, RoutedEventArgs e)
        {
            var unity = GetSelectedUnity();
            if (unity == null) return;
            Tools.LaunchExe(unity.Path);
        }


        private void BtnReleaseNotes_Click(object sender, RoutedEventArgs e)
        {
            var unity = GetSelectedUnity();
            if (unity == null) return;
            Tools.OpenReleaseNotes(unity.Version);
        }

        private void BtnUpdateUnity_Click(object sender, RoutedEventArgs e)
        {
            GoLookForUpdatesForThisUnity();
        }

        // if press up/down in search box, move to first item in results
        private void TxtSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Return: // open selected project
                    var proj = GetSelectedProject();
                    var proc = Tools.LaunchProject(proj);
                    //ProcessHandler.Add(proj, proc);
                    break;
                case Key.Tab:
                case Key.Up:
                    //Tools.SetFocusToGrid(gridRecent);
                    var currentIndex = gridRecent.SelectedIndex - 1;
                    //Console.WriteLine(currentIndex);
                    if (currentIndex < 0) currentIndex = gridRecent.Items.Count - 1;
                    gridRecent.SelectedIndex = currentIndex;
                    e.Handled = true;
                    break;
                case Key.Down:
                    // TODO move to 2nd row if first is already selected
                    //if (GetSelectedProjectIndex() == 0)
                    //{
                    //    Tools.SetFocusToGrid(gridRecent, 1);
                    //}
                    //else
                    //{
                    //Tools.SetFocusToGrid(gridRecent);
                    //                    }

                    // if in searchbox, then move selected index up or down
                    gridRecent.SelectedIndex = ++gridRecent.SelectedIndex % gridRecent.Items.Count;
                    e.Handled = true; // to stay in first row
                    break;
                default:
                    break;
            }
        }

        private void TxtSearchBoxUnity_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Up:
                case Key.Down:
                    Tools.SetFocusToGrid(dataGridUnitys);
                    break;
                default:
                    break;
            }
        }

        private void BtnAddInstallationFolder_Click(object sender, RoutedEventArgs e)
        {
            AddUnityInstallationRootFolder();
        }

        private void BtnRemoveInstallationFolder_Click(object sender, RoutedEventArgs e)
        {
            if (lstRootFolders.SelectedIndex > -1)
            {
                Properties.Settings.Default.rootFolders.Remove(lstRootFolders.Items[lstRootFolders.SelectedIndex].ToString());
                Properties.Settings.Default.Save();
                lstRootFolders.Items.Refresh();
                UpdateUnityInstallationsList();
            }
        }

        // need to manually move into next/prev rows? Not using https://stackoverflow.com/a/11652175/5452781
        private void GridRecent_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Home: // override home
                    // if edit mode, dont override keys
                    if (IsEditingCell(gridRecent) == true) return;
                    gridRecent.SelectedIndex = 0;
                    gridRecent.ScrollIntoView(gridRecent.SelectedItem);
                    e.Handled = true;
                    break;
                case Key.End: // override end
                    // if edit mode, dont override keys
                    if (IsEditingCell(gridRecent) == true) return;
                    // if in args column, dont jump to end of list, but end of this field
                    if (gridRecent.CurrentCell.Column.DisplayIndex == 4)
                    {
                        // start editing this cell
                        gridRecent.BeginEdit();
                        return;
                    }
                    gridRecent.SelectedIndex = gridRecent.Items.Count - 1;
                    gridRecent.ScrollIntoView(gridRecent.SelectedItem);
                    e.Handled = true;
                    break;
                case Key.F5: // refresh projects
                    // if edit mode, dont override keys
                    if (IsEditingCell(gridRecent) == true) return;
                    UpdateUnityInstallationsList();
                    RefreshRecentProjects();
                    break;
                case Key.Tab:
                    // if edit mode, dont override keys
                    if (IsEditingCell(gridRecent) == true) return;
                    if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                    {
                        e.Handled = true;
                    }
                    break;
                case Key.Return:
                    // if edit mode, dont override keys
                    if (IsEditingCell(gridRecent) == true) return;
                    e.Handled = true;
                    var proj = GetSelectedProject();
                    var proc = Tools.LaunchProject(proj);
                    //ProcessHandler.Add(proj, proc);
                    break;
                case Key.Delete:
                    //    // TODO if have enabled deleting projects in settings, NOTE: this would then require undo also (if accidentally delete projects from list with few keypresses, the context menu is less accident prone)
                    //    // if edit mode, dont override keys
                    if (IsEditingCell(gridRecent) == true) return;
                    e.Handled = true;
                    RemoveProjectFromList(confirm: true);
                    //    MenuRemoveProject_Click(null, null);
                    break;
                default:
                    break;
            }
        }

        bool IsEditingCell(DataGrid targetGrid)
        {
            IEditableCollectionView itemsView = targetGrid.Items;
            var res = itemsView.IsAddingNew || itemsView.IsEditingItem;
            return res;
        }

        private void DataGridUnitys_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                e.Handled = true;
                // TODO launch unity?
            }
        }

        private void DataGridUpdates_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                e.Handled = true;
                // TODO open release page?
            }
        }

        private void TxtSearchBoxUpdates_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Up:
                case Key.Down:
                    Tools.SetFocusToGrid(dataGridUpdates);
                    e.Handled = true;
                    break;
                default:
                    break;
            }
        }

        private void BtnOpenEditorLogsFolder_Click(object sender, RoutedEventArgs e)
        {
            var logfolder = Tools.GetEditorLogsFolder();
            if (Directory.Exists(logfolder) == true)
            {
                if (Tools.LaunchExplorer(logfolder) == false)
                {
                    Console.WriteLine("Cannot open folder.." + logfolder);
                    SetStatus("Cannot open folder: " + logfolder);
                }
            }
            else
            {
                SetStatus("Folder does not exist: " + logfolder);
            }
        }

        private void BtnOpenPlayerLogs_Click(object sender, RoutedEventArgs e)
        {
            Tools.OpenAppdataSpecialFolder("../LocalLow");
        }

        private void BtnOpenADBLogCat_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(adbLogCatArgs))
            {
                SetStatus("ADB logcat args not set in Settings tab");
                return;
            }

            try
            {
                Process myProcess = new Process();
                var cmd = "cmd.exe";
                myProcess.StartInfo.FileName = cmd;
                var pars = " /c adb logcat " + adbLogCatArgs;
                myProcess.StartInfo.Arguments = pars;
                myProcess.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                SetStatus("Cannot launch ADB logcat..");
            }
        }

        private void BtnAdbBindWifi_Click(object sender, RoutedEventArgs e)
        {
            // TODO
            //// check if your device is present
            //adb devices
            //// get device ip address (see inet row)
            //adb shell ip addr show wlan0
            //// enable tcpip and port
            //adb tcpip 5555
            //// connect device (use your device ip address)
            //adb connect IP_HERE:5555

            // get device ip address
            //Process process = new Process();
            //process.StartInfo.FileName = "adb";
            //process.StartInfo.Arguments = "shell ip route";
            //process.StartInfo.UseShellExecute = false;
            //process.StartInfo.RedirectStandardOutput = true;
            //process.StartInfo.RedirectStandardError = true;
            //process.OutputDataReceived += new DataReceivedEventHandler(OutputHandler1);
            //process.ErrorDataReceived += new DataReceivedEventHandler(OutputHandler1);
            //process.Start();
            //process.BeginOutputReadLine();
            //process.BeginErrorReadLine();
            //process.WaitForExit();
        }

        //static void OutputHandler1(object sendingProcess, DataReceivedEventArgs outLine)
        //{
        //    string outputData = outLine.Data;
        //    //Console.WriteLine("adboutput=" + outputData);
        //    if (string.IsNullOrEmpty(outputData)) return;

        //    // check if its wlan row
        //    if (outputData.IndexOf("wlan0") > -1)
        //    {
        //        // parse ip address
        //        var getip = outputData.Trim().Split(' ');
        //        if (getip == null || getip.Length < 1) return;

        //        Console.WriteLine("device ip=" + getip[getip.Length - 1]);

        //        // next, call adb connect to that ip address
        //        Process process = new Process();
        //        process.StartInfo.FileName = "adb";
        //        process.StartInfo.Arguments = "connet " + getip[getip.Length - 1] + ":5555";
        //        process.StartInfo.UseShellExecute = false;
        //        process.StartInfo.RedirectStandardOutput = true;
        //        process.StartInfo.RedirectStandardError = true;
        //        process.OutputDataReceived += new DataReceivedEventHandler(OutputHandler1);
        //        process.ErrorDataReceived += new DataReceivedEventHandler(OutputHandler1);
        //        process.Start();
        //        process.BeginOutputReadLine();
        //        process.BeginErrorReadLine();
        //        process.WaitForExit();
        //    }
        //}

        private void BtnRefreshUnityList_Click(object sender, RoutedEventArgs e)
        {
            UpdateUnityInstallationsList();
        }

        private void BtnDownloadInBrowser_Click(object sender, RoutedEventArgs e)
        {
            var unity = GetSelectedUpdate();
            Tools.DownloadInBrowser(unity?.Version);
        }

        private void BtnDownloadInBrowserFull_Click(object sender, RoutedEventArgs e)
        {
            var unity = GetSelectedUpdate();
            Tools.DownloadInBrowser(unity?.Version, true);
        }

        private void btnDownloadInstallUpdate_Click(object sender, RoutedEventArgs e)
        {
            var unity = GetSelectedUpdate();
            Tools.DownloadAndInstall(unity?.Version);
        }

        private void BtnOpenWebsite_Click(object sender, RoutedEventArgs e)
        {
            var unity = GetSelectedUpdate();
            Tools.OpenReleaseNotes(unity?.Version);
        }

        private void BtnShowCumulatedReleaseNotes_Click(object sender, RoutedEventArgs e)
        {
            var unity = GetSelectedUpdate();
            Tools.OpenReleaseNotes_Cumulative(unity?.Version);
        }

        private void ChkMinimizeToTaskbar_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            Settings.Default.minimizeToTaskbar = (bool)chkMinimizeToTaskbar.IsChecked;
            Settings.Default.Save();
        }

        private void ChkRegisterExplorerMenu_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            if ((bool)chkRegisterExplorerMenu.IsChecked)
            {
                Tools.AddContextMenuRegistry(contextRegRoot);
            }
            else // remove
            {
                Tools.RemoveContextMenuRegistry(contextRegRoot);
            }

            Settings.Default.registerExplorerMenu = (bool)chkRegisterExplorerMenu.IsChecked;
            Settings.Default.Save();
        }

        private void ChkShowLauncherArgumentsColumn_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            Settings.Default.showArgumentsColumn = (bool)chkShowLauncherArgumentsColumn.IsChecked;
            Settings.Default.Save();
            gridRecent.Columns[4].Visibility = (bool)chkShowLauncherArgumentsColumn.IsChecked ? Visibility.Visible : Visibility.Collapsed;
            RefreshRecentProjects();
        }

        private void ChkShowGitBranchColumn_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            Settings.Default.showGitBranchColumn = (bool)chkShowGitBranchColumn.IsChecked;
            Settings.Default.Save();
            gridRecent.Columns[5].Visibility = (bool)chkShowGitBranchColumn.IsChecked ? Visibility.Visible : Visibility.Collapsed;
            RefreshRecentProjects();
        }

        private void ChkGetGitBranchRecursively_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false)
                return; // dont run code on window init

            Settings.Default.searchGitFolderRecursivly = (bool)chkGetGitBranchRecursively.IsChecked;
            Settings.Default.Save();
            RefreshRecentProjects();
        }


        private void ChkQuitAfterOpen_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            Settings.Default.closeAfterProject = (bool)chkQuitAfterOpen.IsChecked;
            Settings.Default.Save();
        }

        private void ChkQuitAfterCommandline_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            Settings.Default.closeAfterExplorer = (bool)chkQuitAfterCommandline.IsChecked;
            Settings.Default.Save();
        }

        private void MenuItemShowProjectInExplorer_Click(object sender, RoutedEventArgs e)
        {
            string folder = null;
            if (tabControl.SelectedIndex == 0)
            {
                var proj = GetSelectedProject();
                folder = proj.Path;
            }
            else if (tabControl.SelectedIndex == 1)
            {
                var unity = GetSelectedUnity();
                if (unity == null) return;
                folder = Path.GetDirectoryName(unity.Path);
            }
            Tools.LaunchExplorer(folder);
        }

        private void BtnOpenGithub_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(githubURL);
        }

        // finished editing project name cell or launcher argument cell or platform cells
        private void GridRecent_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            // avoid ending event running twice
            if (isDirtyCell == false) return;
            isDirtyCell = false;

            // get selected row data
            var proj = GetSelectedProject();

            // check that folder exists
            string projectPath = proj.Path;
            if (string.IsNullOrEmpty(projectPath))
            {
                return;
            }

            // check if we edited project name, or launcher arguments
            if (e.Column.DisplayIndex == 0)
            {
                // NOTE cannot rename folder anymore, too dangerous, rename creates custom ProjectName.txt to keep track of projectname

                // get current arguments, after editing
                TextBox t = e.EditingElement as TextBox;
                string newProjectNameString = t.Text.ToString();

                // restore read only
                e.Column.IsReadOnly = true;

                // entered empty name, then restore original name, if custom name was used. NOTE we could just remove custom projectname file then..
                if (string.IsNullOrEmpty(newProjectNameString))
                {
                    newProjectNameString = Path.GetFileName(projectPath);
                }

                //// cannot allow / or \ or . as last character (otherwise might have issues going parent folder?)
                //if (newProjectNameString.EndsWith("\\") || newProjectNameString.EndsWith("/") || newProjectNameString.EndsWith("."))
                //{
                //    Console.WriteLine("Project name cannot end with / or \\ or . ");
                //    return;
                //}

                //// get new path
                //var newPath = Path.Combine(Directory.GetParent(path).ToString(), newProjectNameString);

                //// check if has invalid characters for full path
                //if (newPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                //{
                //    Console.WriteLine("Invalid project path: " + newPath);
                //    return;
                //}

                //// check if same as before (need to replace mismatch slashes)
                //if (path.Replace('/', '\\') == newPath.Replace('/', '\\'))
                //{
                //    Console.WriteLine("Rename cancelled..");
                //    return;
                //}

                //// check if new folder already exists
                //if (Directory.Exists(newPath))
                //{
                //    Console.WriteLine("Directory already exists: " + newPath);
                //    return;
                //}

                //// try rename project folder by moving directory to new name
                //Directory.Move(path, newPath);

                //// check if move was success
                //if (Directory.Exists(newPath))
                //{
                //    // force ending edit (otherwise only ends on enter or esc)
                //    gridRecent.CommitEdit(DataGridEditingUnit.Row, true);

                //    // TODO save to registry (otherwise not listed in recent projects, unless opened)
                //}
                //else
                //{
                //    Console.WriteLine("Failed to rename directory..");
                //}

                var write = Tools.SaveCustomProjectData(projectPath, projectNameFile, newProjectNameString);
                // write success, otherwise keep old name
                if (write == true)
                {
                    gridRecent.CommitEdit(DataGridEditingUnit.Row, true);
                    proj.Title = newProjectNameString;
                    Console.WriteLine("Project title renamed to: " + newProjectNameString);
                    gridRecent.Items.Refresh();
                }


            }
            else if (e.Column.DisplayIndex == 4) // edit launcher arguments
            {
                // get current arguments, after editing
                TextBox t = e.EditingElement as TextBox;
                string newcellValue = t.Text.ToString();

                string projSettingsFolder = "ProjectSettings";

                // check if projectsettings folder exists, if not then add
                string outputFolder = Path.Combine(projectPath, projSettingsFolder);
                if (Directory.Exists(outputFolder) == false)
                {
                    Directory.CreateDirectory(outputFolder);
                }

                // save arguments to projectsettings folder
                string outputFile = Path.Combine(projectPath, projSettingsFolder, launcherArgumentsFile);

                if (string.IsNullOrEmpty(newcellValue.Trim()) == true)
                {
                    // its empty value, so we remove the file (to avoid wasting time reading empty file)
                    if (File.Exists(outputFile))
                    {
                        File.Delete(outputFile);
                    }
                }
                else
                {
                    try
                    {
                        StreamWriter sw = new StreamWriter(outputFile);
                        sw.WriteLine(newcellValue);
                        sw.Close();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error saving launcher arguments: " + ex);
                        SetStatus("Error saving launcher arguments: " + ex.Message);
                    }
                }
            }
            else if (e.Column.DisplayIndex == 6) // platform dropdown
            {
                // get current arguments, after editing
                var t = e.EditingElement as ComboBox;
                if (t == null) return;
                string newcellValue = t.SelectedItem.ToString();

                Console.WriteLine("Modified platform target: " + newcellValue);
            }

            gridRecent.CommitEdit();
            gridRecent.CommitEdit();

            // TODO add esc to cancel edit
            //gridRecent.CancelEdit();

            // TODO select the same row again
        }

        private void TxtSearchBoxUpdates_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterUpdates();
        }

        private void TxtSearchBoxUnity_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterUnitys();
        }

        private void GridRecent_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // cancel if editing cell, because often try to double click to edit instead
            if (IsEditingCell(gridRecent)) return;

            // check if not clicked on the row
            if (e.OriginalSource.GetType() != typeof(TextBlock)) return;

            // cancel run if double clicked Arguments or Platform editable field
            var currentColumnCell = gridRecent.CurrentCell.Column.DisplayIndex;
            if (currentColumnCell == 4 || currentColumnCell == 6) return;

            var proj = GetSelectedProject();
            var proc = Tools.LaunchProject(proj);
            //ProcessHandler.Add(proj, proc);
        }

        private void DataGridUnitys_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var unity = GetSelectedUnity();
            var unitypath = Tools.GetUnityExePath(unity?.Version);
            Tools.LaunchExe(unitypath);
        }

        private void DataGridUpdates_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var unity = GetSelectedUpdate();
            Tools.OpenReleaseNotes(unity?.Version);
        }

        private void ChkShowMissingFolderProjects_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            Settings.Default.showProjectsMissingFolder = (bool)chkShowMissingFolderProjects.IsChecked;
            Settings.Default.Save();
        }

        private void ChkAllowSingleInstanceOnly_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            Settings.Default.AllowSingleInstanceOnly = (bool)chkAllowSingleInstanceOnly.IsChecked;
            Settings.Default.Save();
        }

        private void BtnAssetPackages_Click(object sender, RoutedEventArgs e)
        {
            Tools.OpenCustomAssetPath();
            Tools.OpenAppdataSpecialFolder("../Roaming/Unity/Asset Store-5.x");
        }

        // sets selected unity version as preferred main unity version (to be preselected in case of unknown version projects, when creating new empty project, etc)
        private void MenuItemSetPreferredUnityVersion_Click(object sender, RoutedEventArgs e)
        {
            var ver = GetSelectedUnity().Version;
            Settings.Default.preferredVersion = ver;
            Settings.Default.Save();

            preferredVersion = ver;
            // TODO update unity list or just set value?
            UpdateUnityInstallationsList();

        }

        private void MenuItemCopyPath_Click(object sender, RoutedEventArgs e)
        {
            string copy = null;
            if (tabControl.SelectedIndex == 0)
            {
                var proj = GetSelectedProject();
                // fix slashes so that it works in save dialogs 
                copy = proj?.Path.Replace('/', '\\');
            }
            else if (tabControl.SelectedIndex == 1)
            {
                var proj = GetSelectedUnity();
                // fix slashes so that it works in save dialogs, and remove editor part
                copy = proj?.Path.Replace('/', '\\').Replace("\\Editor", "");
                // remove exe
                copy = Path.GetDirectoryName(copy);
            }
            if (copy != null) Clipboard.SetText(copy);
        }

        // creates empty project into default project root with selected unity version
        private void BtnCreateEmptyProject_Click(object sender, RoutedEventArgs e)
        {
            CreateNewEmptyProject();
        }

        private void BtnBrowseProjectRootFolder_Click(object sender, RoutedEventArgs e)
        {
            var folder = Tools.BrowseForOutputFolder("Select root folder for New Projects");
            if (string.IsNullOrEmpty(folder) == false)
            {
                txtRootFolderForNewProjects.Text = folder;
                Properties.Settings.Default.newProjectsRoot = folder;
                Properties.Settings.Default.Save();
            }
        }

        private void TxtRootFolderForNewProjects_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            Settings.Default.newProjectsRoot = txtRootFolderForNewProjects.Text;
            Settings.Default.Save();
        }


        private void BtnCreateEmptyProjectUnity_Click(object sender, RoutedEventArgs e)
        {
            CreateNewEmptyProject();
        }

        void CreateNewEmptyProject(string targetFolder = null)
        {
            string rootFolder = txtRootFolderForNewProjects.Text;

            // if have targetfolder, then use that instead of quick folder
            if (targetFolder != null)
            {
                if (Directory.Exists(targetFolder))
                {
                    rootFolder = Directory.GetParent(targetFolder).FullName;
                }
            }
            else
            {
                // check if quick root folder is set correctly
                if (Directory.Exists(rootFolder) == false)
                {
                    tabControl.SelectedIndex = 4;
                    this.UpdateLayout();
                    txtRootFolderForNewProjects.Focus();
                    SetStatus("Root folder for new projects is missing or doesn't exist: " + rootFolder, MessageType.Error);
                    return;
                }
            }

            if (string.IsNullOrEmpty(initScriptFileFullPath) == true)
            {
                initScriptFileFullPath = Tools.GetSafeFilePath("Scripts", "InitializeProject.cs");
            }

            // for new projects created from explorer, always ask for name
            if (chkAskNameForQuickProject.IsChecked == true || targetFolder != null)
            {
                string newVersion = null;

                // if in maintab
                if (tabControl.SelectedIndex == 0)
                {
                    newVersion = GetSelectedProject()?.Version == null ? preferredVersion : GetSelectedProject().Version;
                }
                else // unity tab
                {
                    newVersion = (GetSelectedUnity() == null || GetSelectedUnity().Version == null) ? preferredVersion : GetSelectedUnity().Version;
                }

                if (string.IsNullOrEmpty(newVersion))
                {
                    Console.WriteLine("Missing selected Unity version, probably launching from context menu");
                    newVersion = preferredVersion;
                    // if no preferred version, use latest
                    if (string.IsNullOrEmpty(newVersion)) newVersion = unityInstallationsSource[0].Version;

                }

                string suggestedName = targetFolder != null ? Path.GetFileName(targetFolder) : Tools.GetSuggestedProjectName(newVersion, rootFolder);
                bool fetchOnlineTemplates = chkFetchOnlineTemplates.IsChecked == true;

                NewProject modalWindow = new NewProject(newVersion, suggestedName, rootFolder, targetFolder != null, fetchOnlineTemplates);
                modalWindow.ShowInTaskbar = this == null;
                modalWindow.WindowStartupLocation = this == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
                modalWindow.Topmost = this == null;
                modalWindow.ShowActivated = true;
                modalWindow.Owner = this;
                modalWindow.ShowDialog();
                var results = modalWindow.DialogResult.HasValue && modalWindow.DialogResult.Value;

                if (results == true)
                {
                    // TODO check if that folder already exists (automatic naming avoids it, but manual naming could cause it)
                    rootFolder = NewProject.targetFolder;
                    Console.WriteLine("Create project " + NewProject.newVersion + " : " + rootFolder);
                    if (string.IsNullOrEmpty(rootFolder)) return;

                    var p = Tools.FastCreateProject(NewProject.newVersion, rootFolder, NewProject.newProjectName, NewProject.templateZipPath, NewProject.platformsForThisUnity, NewProject.selectedPlatform, (bool)chkUseInitScript.IsChecked, initScriptFileFullPath, NewProject.forceDX11);

                    // add to list (just in case new project fails to start, then folder is already generated..)
                    if (p != null) AddNewProjectToList(p);
                }
                else // false, cancel
                {
                    Console.WriteLine("Cancelled project creation..");
                }

            }
            else // use automatic name (project is instantly created, without asking anything)
            {
                string newVersion = null;
                // if in maintab
                if (tabControl.SelectedIndex == 0)
                {
                    newVersion = GetSelectedProject().Version == null ? preferredVersion : GetSelectedProject().Version;
                }
                else // unity tab
                {
                    newVersion = GetSelectedUnity().Version == null ? preferredVersion : GetSelectedUnity().Version;
                }
                var p = Tools.FastCreateProject(newVersion, rootFolder, null, null, null, null, (bool)chkUseInitScript.IsChecked, initScriptFileFullPath);

                if (p != null) AddNewProjectToList(p);
            }

        }

        private void ChkAskNameForQuickProject_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            Settings.Default.askNameForQuickProject = (bool)chkAskNameForQuickProject.IsChecked;
            Settings.Default.Save();
        }

        bool isInitializing = true; // used to avoid doing things while still starting up
        private void ChkStreamerMode_Checked(object sender, RoutedEventArgs e)
        {
            var isChecked = (bool)((CheckBox)sender).IsChecked;

            Settings.Default.streamerMode = isChecked;
            Settings.Default.Save();

            // Create cellstyle and assign if streamermode is enabled
            Style cellStyle = new Style(typeof(DataGridCell));
            cellStyle.Setters.Add(new Setter(FontSizeProperty, 1.0));
            txtColumnTitle.CellStyle = isChecked ? cellStyle : null;
            txtColumnPath.CellStyle = isChecked ? cellStyle : null;
            txtColumnGitBranch.CellStyle = isChecked ? cellStyle : null;

            Style txtBoxStyle = new Style(typeof(TextBox));
            txtBoxStyle.Setters.Add(new Setter(FontSizeProperty, 1.0));
            txtShortcutBatchFileFolder.Style = isChecked ? txtBoxStyle : null;
            txtRootFolderForNewProjects.Style = isChecked ? txtBoxStyle : null;

            // need to reload list if user changed setting
            if (isInitializing == false)
            {
                RefreshRecentProjects();
            }

            SetStatus("Streamer mode " + (isChecked ? "enabled" : "disabled"), MessageType.Info);
        }

        private void ChkShowPlatform_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            var isChecked = (bool)((CheckBox)sender).IsChecked;

            Settings.Default.showTargetPlatform = isChecked;
            Settings.Default.Save();

            gridRecent.Columns[6].Visibility = (bool)chkShowPlatform.IsChecked ? Visibility.Visible : Visibility.Collapsed;

            // need to reload list if user changed setting
            if (isInitializing == false)
            {
                RefreshRecentProjects();
            }
        }

        // copies project folder, or unity exe folder, or unity version from current datagrid
        public void CopyRowFolderToClipBoard(object sender, ExecutedRoutedEventArgs e)
        {
            string path = null;

            if (tabControl.SelectedIndex == 0) // projects
            {
                path = GetSelectedProject()?.Path;
            }
            else if (tabControl.SelectedIndex == 1) // installed unitys
            {
                path = Path.GetDirectoryName(GetSelectedUnity()?.Path);
            }
            else if (tabControl.SelectedIndex == 2) // updates
            {
                path = GetSelectedUpdate()?.Version; // TODO copy url instead
            }
            else if (tabControl.SelectedIndex == 3) // tools
            {
                path = GetSelectedBuildItem().Path;
                if (path != null) path = Path.Combine(currentBuildReportProjectPath, path);
            }

            if (string.IsNullOrEmpty(path) == false)
            {
                // fix backslashes
                path = path.Replace('\\', '/');
                Clipboard.SetText(path);
            }
        }

        public void CanExecute_Copy(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }

        private void GridRecent_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            isDirtyCell = true;
        }

        private void BtnOpenCrashLogs_Click(object sender, RoutedEventArgs e)
        {
            Tools.OpenAppdataSpecialFolder("Temp/Unity/Editor/Crashes/");
        }

        // reorder grid item by index
        public void MoveRecentGridItem(int to)
        {
            var source = (Project)gridRecent.Items[gridRecent.SelectedIndex];
            projectsSource.RemoveAt(gridRecent.SelectedIndex);
            projectsSource.Insert(to, source);
            gridRecent.Items.Refresh();
            gridRecent.SelectedIndex = to;
        }

        private void ChkEnableProjectRename_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            Settings.Default.enableProjectRename = (bool)chkEnableProjectRename.IsChecked;
            Settings.Default.Save();
        }

        private void MenuItemKillProcess_Click(object sender, RoutedEventArgs e)
        {
            if (tabControl.SelectedIndex == 0)
            {
                KillSelectedProcess(null, null);
            }
        }

        void KillSelectedProcess(object sender, ExecutedRoutedEventArgs e)
        {
            var proj = GetSelectedProject();
            var proc = ProcessHandler.Get(proj.Path);
            if (proj != null && proc != null)
            {
                try
                {
                    proc.Kill();
                }
                catch (Exception)
                {
                }
                //proc.Dispose(); // NOTE cannot dispose, otherwise process.Exited event is not called
                proj = null;
            }
        }

        private void GridRecent_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (tabControl.SelectedIndex == 0)
            {
                var proj = GetSelectedProject();
                var proc = ProcessHandler.Get(proj.Path);
                menuItemKillProcess.IsEnabled = proc != null;
            }
        }

        // add alt+Q shortcut for killing process
        // https://stackoverflow.com/a/29817712/5452781
        public static readonly RoutedCommand KillProcessCommand = new RoutedUICommand("None", "KillProcessCommand", typeof(MainWindow), new InputGestureCollection(new InputGesture[]
        {
            new KeyGesture(Key.Q, ModifierKeys.Alt)
        }));

        private void BtnRefreshBuildReport_Click(object sender, RoutedEventArgs e)
        {
            RefreshBuildReports();
        }

        void RefreshBuildReports()
        {
            currentBuildReport = 0;
            // delete all reports
            buildReports.Clear();
            UpdateBuildReportLabelAndButtons();

            btnPrevBuildReport.IsEnabled = false;
            btnNextBuildReport.IsEnabled = false;

            var logFile = Path.Combine(Tools.GetEditorLogsFolder(), "Editor.log");
            if (File.Exists(logFile) == false) return;

            BuildReport singleReport = null;// new BuildReport();

            try
            {
                // read editor.log file
                using (FileStream fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    using (StreamReader sr = new StreamReader(fs))
                    {
                        bool collectRows = false; // actual log rows
                        bool collectStats = false; // category stat rows
                        bool collectedBuildTime = false;

                        bool gotProjectPath = false;
                        bool hasTimeStamps = false;
                        bool gotOutputPluginsPath = false;

                        // TODO cleanup here
                        while (!sr.EndOfStream)
                        {
                            var line = sr.ReadLine();

                            // get current projectpath
                            if (gotProjectPath == true)
                            {
                                currentBuildReportProjectPath = line;
                                gotProjectPath = false;
                            }

                            if (gotOutputPluginsPath == true)
                            {
                                // get output plugins folder relative path, this might not always work, if there is not copyfiles line in log
                                string pattern = @"CopyFiles\s+(.+?)/\w+\.\w+$";
                                Match match = Regex.Match(line, pattern);
                                if (match.Success)
                                {
                                    string folder = match.Groups[1].Value;//.Replace("CopyFiles ", "");
                                    if (folder.IndexOf("_Data/Plugins") > -1)
                                    {
                                        currentBuildPluginsRelativePath = folder;
                                    }
                                }

                                gotOutputPluginsPath = false;
                            }

                            // if have timestamps, trim until 2nd | char at start

                            // check arguments
                            if (line.IndexOf("-projectPath") > -1) gotProjectPath = true;

                            // NOTE only works if your build path is inside Assets/Builds/ folder
                            if (line.IndexOf("CopyFiles") > -1 && line.ToLower().IndexOf("builds/") > -1) gotOutputPluginsPath = true;

                            if (hasTimeStamps == false && line.IndexOf("-timestamps") > -1)
                            {
                                hasTimeStamps = true;
                                // need to fix projectpath then
                                currentBuildReportProjectPath = currentBuildReportProjectPath.Substring(currentBuildReportProjectPath.IndexOf("|", currentBuildReportProjectPath.IndexOf("|") + 1) + 1);
                            }

                            // remove timestamp from line, NOTE if | character exists somewhere else than timestamp, causes issue
                            if (hasTimeStamps && line.IndexOf("|") > -1)
                            {
                                line = line.Substring(line.IndexOf("|", line.IndexOf("|") + 1) + 1).Trim();
                            }

                            // build report starts
                            if (collectRows == false && line.IndexOf("Used Assets and files from the Resources folder, sorted by uncompressed size:") == 0)
                            {
                                // init new list for this build report
                                singleReport.Items = new List<BuildReportItem>();
                                collectRows = true;

                                // category report ends
                                if (collectRows == true)
                                {
                                    collectStats = false;
                                }

                                continue;
                            }

                            // build report category stats starts
                            if (collectStats == false && line.IndexOf("Uncompressed usage by category (Percentages based on user generated assets only):") == 0)
                            {
                                // start new
                                singleReport = new BuildReport();

                                // init new list for this build report
                                singleReport.Stats = new List<BuildReportItem>();
                                collectStats = true;
                                continue;
                            }

                            if (collectStats == false && line.IndexOf("Do a clean build to view the Asset build report information.") == 0)
                            {
                                // dont start collecting, no build report
                                collectStats = false;
                                continue;
                            }

                            // build report ends with elapsed time
                            if (collectedBuildTime == false && line.IndexOf("Build completed with a result of 'Succeeded' in ") == 0)
                            {
                                // it wasnt clean build, no report
                                if (singleReport == null) continue;
                                int start = line.IndexOf("(") + 1;
                                int end = line.IndexOf(" ms)", start);

                                if (start > 0 && end > start)
                                {
                                    string numberString = line.Substring(start, end - start);
                                    singleReport.ElapsedTimeMS = long.Parse(numberString);
                                    collectedBuildTime = true;
                                }

                                if (string.IsNullOrEmpty(currentBuildReportProjectPath) == false)
                                {
                                    // get streamingassets folder size and add as last item to report, NOTE need to recalculate sizes then?
                                    string streamingAssetPath = Path.Combine(currentBuildReportProjectPath, "Assets", "StreamingAssets");
                                    var streamingAssetFolderSize = Tools.GetFolderSizeInBytes(streamingAssetPath);
                                    singleReport.Stats.Insert(singleReport.Stats.Count - 1, new BuildReportItem() { Category = "StreamingAssets", Size = Tools.GetBytesReadable(streamingAssetFolderSize) });


                                    // get total Plugins/ folder size from build! (but how to get last build output folder, its not mentioned in editor log (except some lines for CopyFiles/ but are those always there?)
                                    // Library\PlayerDataCache\Linux641\ScriptsOnlyCache.yaml also contains output path
                                    if (string.IsNullOrEmpty(currentBuildPluginsRelativePath) == false)
                                    {
                                        //Console.WriteLine("Getting output plugins folder size: "+ currentBuildPluginsRelativePath);
                                        string pluginFolder = Path.Combine(currentBuildReportProjectPath, currentBuildPluginsRelativePath);
                                        long totalPluginFolderSize = Tools.GetFolderSizeInBytes(pluginFolder);
                                        singleReport.Stats.Insert(singleReport.Stats.Count - 1, new BuildReportItem() { Category = "Plugins", Size = Tools.GetBytesReadable(totalPluginFolderSize) });
                                    }
                                    else // then show plugin folders from project (with * to mark this is not accurate)
                                    {
                                        // get plugin folder sizes (they are not included in build report!), NOTE need to iterate all subfolders, as they might contain Plugins/ folders
                                        // find all plugin folders inside Assets/
                                        var pluginFolders = Directory.GetDirectories(Path.Combine(currentBuildReportProjectPath, "Assets"), "Plugins", SearchOption.AllDirectories);
                                        long totalPluginFolderSize = 0;
                                        foreach (var pluginFolder in pluginFolders)
                                        {
                                            totalPluginFolderSize += Tools.GetFolderSizeInBytes(pluginFolder);
                                        }
                                        singleReport.Stats.Insert(singleReport.Stats.Count - 1, new BuildReportItem() { Category = "Plugins *in proj!", Size = Tools.GetBytesReadable(totalPluginFolderSize) });
                                    }

                                }
                                else
                                {
                                    // this can happen if editor log file was overwritten with another running editor? (so that start of the log file doesnt contain project path)
                                    Console.WriteLine("Failed to get project path from build report..");
                                }

                                // add all rows and stat rows for this build report
                                buildReports.Add(singleReport);

                                // erase old
                                singleReport = null;
                                continue;
                            }

                            // build report ends for rows
                            if (collectRows == true && line.IndexOf("-------------------------------------------------------------------------------") == 0)
                            {
                                collectRows = false;
                                collectedBuildTime = false;
                                continue;
                            }

                            // parse and add this line to current build report
                            if (collectRows == true)
                            {
                                var line2 = line.Trim();
                                // get tab after kb
                                var space1 = line2.IndexOf('\t');
                                // get % between % and path
                                var space2 = line2.IndexOf('%');

                                if (space1 == -1 || space2 == -1)
                                {
                                    Console.WriteLine(("Failed to parse build report row: " + line2));
                                    SetStatus("Failed to parse build report row: " + line2);
                                    continue;
                                }

                                // create single row
                                var r = new BuildReportItem();
                                r.Size = line2.Substring(0, space1);
                                r.Percentage = line2.Substring(space1 + 2, space2 - space1 - 1);
                                r.Path = line2.Substring(space2 + 2, line2.Length - space2 - 2);
                                r.Format = Path.GetExtension(r.Path);
                                singleReport.Items.Add(r);
                            }

                            if (collectStats == true)
                            {

                                //if (hasTimeStamps) 
                                var line2 = line.Trim();
                                // get 2x space after category name
                                var space1 = line2.IndexOf("   ");
                                // get tab after first size
                                var space2 = line2.IndexOf('\t');
                                // last row didnt contain tab "Complete build size"
                                bool lastRow = false;
                                if (space2 == -1)
                                {
                                    space2 = line2.Length - 1;
                                    lastRow = true;
                                }

                                if (space1 == -1 || space2 == -1)
                                {
                                    Console.WriteLine(("(2) Failed to parse build report row: " + line2));
                                    SetStatus("(2) Failed to parse build report row: " + line2);
                                    continue;
                                }

                                // create single row
                                var r = new BuildReportItem();
                                r.Category = line2.Substring(0, space1).Trim();
                                r.Size = line2.Substring(space1 + 2, space2 - space1 - 1).Trim();
                                if (lastRow == false) r.Percentage = line2.Substring(space2 + 2, line2.Length - space2 - 2).Trim();

                                singleReport.Stats.Add(r);
                            }

                        } // while endofstream
                    } // streamreader
                } // filestream
            }
            catch (Exception e)
            {
                gridBuildReport.ItemsSource = null;
                gridBuildReport.Items.Clear();

                gridBuildReportData.ItemsSource = null;
                gridBuildReportData.Items.Clear();

                txtBuildTime.Text = "";

                Console.WriteLine("Failed to open editor log or other error in parsing: " + logFile);
                Console.WriteLine(e);
                SetStatus("Failed to open editor log or other parsing error..");
                return;
            }

            // no build reports found
            if (buildReports.Count == 0)
            {
                gridBuildReport.ItemsSource = null;
                gridBuildReport.Items.Clear();

                gridBuildReportData.ItemsSource = null;
                gridBuildReportData.Items.Clear();

                txtBuildTime.Text = "";

                Console.WriteLine("Failed to parse Editor.Log (probably no build reports there)");
                SetStatus("Failed to parse Editor.Log (probably no build reports there)");
                return;
            }

            // remove streaming assets info, keep only for last build (because older builds might have had different files there, we dont know)
            for (int i = 0; i < buildReports.Count - 1; i++)
            {
                buildReports[i].Stats[buildReports[i].Stats.Count - 2].Size = "???";
            }

            // reverse build reports, so that latest is first
            buildReports.Reverse();

            DisplayBuildReport(currentBuildReport);
        }

        private void BtnPrevBuildReport_Click(object sender, RoutedEventArgs e)
        {
            DisplayBuildReport(--currentBuildReport);
        }

        private void BtnNextBuildReport_Click(object sender, RoutedEventArgs e)
        {
            DisplayBuildReport(++currentBuildReport);
        }

        void DisplayBuildReport(int index)
        {
            if (currentBuildReport < 0)
            {
                currentBuildReport = 0;
            }

            if (currentBuildReport > buildReports.Count)
            {
                currentBuildReport = buildReports.Count - 1;
            }

            gridBuildReport.ItemsSource = null;
            gridBuildReport.Items.Clear();
            gridBuildReport.ItemsSource = buildReports[currentBuildReport].Items;

            gridBuildReportData.ItemsSource = null;
            gridBuildReportData.Items.Clear();
            gridBuildReportData.ItemsSource = buildReports[currentBuildReport].Stats;

            var time = TimeSpan.FromMilliseconds(buildReports[currentBuildReport].ElapsedTimeMS);
            var dt = new DateTime(time.Ticks);
            txtBuildTime.Text = dt.ToString("HH 'hours' mm 'minutes' ss 'seconds'");

            UpdateBuildReportLabelAndButtons();
        }

        void UpdateBuildReportLabelAndButtons()
        {
            btnPrevBuildReport.IsEnabled = currentBuildReport > 0;
            btnNextBuildReport.IsEnabled = currentBuildReport < buildReports.Count - 1;
            lblBuildReportIndex.Content = (buildReports.Count == 0 ? 0 : (currentBuildReport + 1)) + "/" + (buildReports.Count);
        }

        private void BtnClearBuildReport_Click(object sender, RoutedEventArgs e)
        {
            gridBuildReport.ItemsSource = null;
            gridBuildReportData.ItemsSource = null;
            txtBuildTime.Text = "";
            currentBuildReport = 0;
            buildReports.Clear();
            UpdateBuildReportLabelAndButtons();
        }

        private void MenuStartWebGLServer_Click(object sender, RoutedEventArgs e)
        {
            var proj = GetSelectedProject();
            Tools.LaunchWebGL(proj, txtWebglRelativePath.Text);
        }

        private void TxtWebglRelativePath_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            Settings.Default.webglBuildPath = txtWebglRelativePath.Text;
            Settings.Default.Save();
        }

        private void MenuItemBrowsePersistentDataPath_Click(object sender, RoutedEventArgs e)
        {
            var proj = GetSelectedProject();
            var projPath = proj?.Path.Replace('/', '\\');
            if (string.IsNullOrEmpty(projPath) == true) return;

            var psPath = Path.Combine(projPath, "ProjectSettings", "ProjectSettings.asset");
            if (File.Exists(psPath) == false)
            {
                Console.WriteLine("Project settings not found: " + psPath);
                return;
            }
            // read project settings
            var rows = File.ReadAllLines(psPath);

            // NOTE old projects have binary version of this file, so cannot parse it, check if first line contains YAML
            if (rows[0].IndexOf("YAML") == -1)
            {
                Console.WriteLine("Project settings file is binary, cannot parse: " + psPath);
                return;
            }

            // search company and product name rows
            for (int i = 0, len = rows.Length; i < len; i++)
            {
                // skip rows until companyname
                if (rows[i].IndexOf("companyName:") == -1) continue;

                var companyName = rows[i].Split(new[] { "companyName: " }, StringSplitOptions.None)[1];
                var productName = rows[i + 1].Split(new[] { "productName: " }, StringSplitOptions.None)[1];

                // open folder from %userprofile%\AppData\LocalLow\<companyname>\<productname>
                var dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "/../LocalLow");
                var psFullPath = Path.Combine(dataFolder, companyName, productName);
                Tools.LaunchExplorer(psFullPath);
                break;
            }
        }

        private void ApplyTheme(string themeFileName)
        {
            if (chkUseCustomTheme.IsChecked != true)
                return;

            // 1) Compute the full, safe path to the INI
            string themePath = Tools.GetSafeFilePath("Themes", themeFileName);

            // 2) Try to load it
            if (File.Exists(themePath))
            {
                var lines = File.ReadAllLines(themePath);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    // skip empty or comment
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                        continue;

                    var parts = line.Split(new[] { '=' }, 2);
                    if (parts.Length != 2)
                        continue;

                    string key = parts[0].Trim();
                    string value = parts[1].Trim();

                    try
                    {
                        var brush = (SolidColorBrush)new BrushConverter().ConvertFrom(value);
                        Application.Current.Resources[key] = brush;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                        SetStatus($"Failed to parse color value: {value}");
                    }
                }
            }
            else
            {
                Console.WriteLine($"Theme file not found: {themePath}");
                SetStatus($"Theme file not found: {themePath}");
            }
        }


        void ResetTheme()
        {
            foreach (DictionaryEntry item in Application.Current.Resources.MergedDictionaries[0])
            {
                if (item.Key is string key && item.Value is SolidColorBrush brush)
                {
                    Application.Current.Resources[key] = brush;
                }
            }
        }

        private void ChkUseCustomTheme_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            var isChecked = (bool)((CheckBox)sender).IsChecked;
            Settings.Default.useCustomTheme = isChecked;
            Settings.Default.Save();

            btnReloadTheme.IsEnabled = isChecked;

            // reset colors now
            if (isChecked == true)
            {
                ApplyTheme(txtCustomThemeFile.Text);
            }
            else
            {
                ResetTheme();
            }
        }

        private void BtnReloadTheme_Click(object sender, RoutedEventArgs e)
        {
            ApplyTheme(txtCustomThemeFile.Text);
        }

        private void TxtCustomThemeFile_LostFocus(object sender, RoutedEventArgs e)
        {
            var s = (TextBox)sender;
            Settings.Default.themeFile = s.Text;
            Settings.Default.Save();
        }

        private void TxtCustomThemeFile_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Return: // pressed enter in theme file text box
                    Properties.Settings.Default.themeFile = txtCustomThemeFile.Text;
                    Properties.Settings.Default.Save();
                    btnReloadTheme.Focus();
                    break;
            }
        }

        private void BtnExploreFolder_Click(object sender, RoutedEventArgs e)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Themes");
            if (Tools.LaunchExplorer(path) == false)
            {
                Tools.LaunchExplorer(AppDomain.CurrentDomain.BaseDirectory);
            }
        }

        private void ChkEnablePlatformSelection_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            var isChecked = (bool)((CheckBox)sender).IsChecked;
            Settings.Default.enablePlatformSelection = isChecked;
            Settings.Default.Save();
            chkEnablePlatformSelection.IsChecked = isChecked;
        }

        private void CmbPlatformSelection_DropDownClosed(object sender, EventArgs e)
        {
            if (sender == null) return;
            try
            {
                // get current platform, set it to selected project data
                var cmb = (ComboBox)sender;
                //Console.WriteLine(cmb.SelectedValue);
                var p = GetSelectedProject();
                if (p != null && p.TargetPlatform != null) p.TargetPlatform = cmb.SelectedValue.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private void ChkRunAutomatically_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            var isChecked = (bool)((CheckBox)sender).IsChecked;
            Settings.Default.runAutomatically = isChecked;
            Settings.Default.Save();
            chkRunAutomatically.IsChecked = isChecked;
            // set or unset registry, NOTE should not do this on debug build.. (otherwise 2 builds try to run?)
            Tools.SetStartupRegistry(isChecked);
        }

        private void ChkUseCustomLastModified_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            var isChecked = (bool)((CheckBox)sender).IsChecked;
            chkUseCustomLastModified.IsChecked = isChecked;
            Properties.Settings.Default.useCustomLastModified = isChecked;
            Properties.Settings.Default.Save();

            if (isChecked)
            {
                ValidateCustomDateFormat(txtCustomDateTimeFormat.Text);
            }
            else
            {
                currentDateFormat = defaultDateFormat;
            }
        }

        private void TxtCustomDateTimeFormat_LostFocus(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init
            TextBox textBox = sender as TextBox;
            ValidateCustomDateFormat(textBox.Text);
        }

        private void TxtCustomDateTimeFormat_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init
            TextBox textBox = sender as TextBox;
            ValidateCustomDateFormat(textBox.Text);
        }

        void ValidateCustomDateFormat(string format)
        {
            if (Tools.ValidateDateFormat(format))
            {
                currentDateFormat = format;
                Properties.Settings.Default.customDateFormat = currentDateFormat;
                Properties.Settings.Default.Save();
                txtCustomDateTimeFormat.BorderBrush = System.Windows.Media.Brushes.Transparent;
            }
            else // invalid format
            {
                //txtCustomDateTimeFormat.Foreground = System.Windows.Media.Brushes.Red;
                txtCustomDateTimeFormat.BorderBrush = System.Windows.Media.Brushes.Red;
                currentDateFormat = defaultDateFormat;
            }
        }

        void ValidateFolderFromTextbox(TextBox textBox)
        {
            //Console.WriteLine(textBox.Text);
            if (Directory.Exists(textBox.Text) == true)
            {
                // NOTE this saves for shortcutbat setting, so cannot be used for another fields
                Properties.Settings.Default.shortcutBatchFileFolder = textBox.Text;
                Properties.Settings.Default.Save();
                textBox.BorderBrush = System.Windows.Media.Brushes.Transparent;
            }
            else // invalid format
            {
                textBox.BorderBrush = System.Windows.Media.Brushes.Red;
            }
        }

        bool ValidateIntRange(TextBox textBox, int min, int max)
        {
            int num = 0;
            if (int.TryParse(textBox.Text, out num))
            {
                if (num >= min && num <= max)
                {
                    textBox.BorderBrush = null;
                    return true;
                }
                else
                {
                    textBox.BorderBrush = System.Windows.Media.Brushes.Red;
                }
            }
            else
            {
                textBox.BorderBrush = System.Windows.Media.Brushes.Red;
            }
            return false;
        }

        private void ChkHumanFriendlyDateTime_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            var isChecked = (bool)((CheckBox)sender).IsChecked;

            // cannot have both date formats
            if (isChecked == true)
            {
                if (chkUseCustomLastModified.IsChecked == true) chkUseCustomLastModified.IsChecked = false;
            }
            else
            {
                currentDateFormat = defaultDateFormat;
            }

            useHumanFriendlyDateFormat = isChecked;

            Properties.Settings.Default.useHumandFriendlyLastModified = isChecked;
            Properties.Settings.Default.Save();
        }

        private void GridRecent_ColumnReordered(object sender, DataGridColumnEventArgs e)
        {
            // if amount has changed, need to reset array
            if (Properties.Settings.Default.recentColumnsOrder.Length != gridRecent.Columns.Count) Properties.Settings.Default.recentColumnsOrder = new Int32[gridRecent.Columns.Count];

            // get new display indexes
            for (int i = 0; i < gridRecent.Columns.Count; i++)
            {
                Properties.Settings.Default.recentColumnsOrder[i] = gridRecent.Columns[i].DisplayIndex;
            }
            Properties.Settings.Default.Save();
        }

        private void MenuItemExploreBuildItem_Click(object sender, RoutedEventArgs e)
        {
            OpenSelectedBuildReportFile();
        }

        private void GridBuildReport_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OpenSelectedBuildReportFile();
        }

        void OpenSelectedBuildReportFile()
        {
            var item = GetSelectedBuildItem();

            if (item != null)
            {
                string filePath = Path.Combine(currentBuildReportProjectPath, item.Path);
                Tools.LaunchExplorerSelectFile(filePath);
            }
        }

        private void ChkRunAutomaticallyMinimized_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            var isChecked = (bool)((CheckBox)sender).IsChecked;

            Settings.Default.runAutomaticallyMinimized = isChecked;
            Settings.Default.Save();
        }

        private void MenuItemEditPackages_Click(object sender, RoutedEventArgs e)
        {
            // TODO read Editor\Data\Resources\PackageManager\Editor\manifest.json
            // TODO read list of buildin packages *or no need, its com.unity.modules.*
            // TODO show list of packages (with buildin packages hidden from the list)
            // TODO user can enable/disable packages
            // TODO save back to the JSON file (NOTE cannot write if not admin! need to run some batch command elevated to overwrite file?)
            // TODO or, allow setting filter for packages (so can have custom "dont want"-packages list, and then remove those automatically! (from generated project, so original manifest can stay, but at which point..)
        }

        private void MenuItemUpdatesReleaseNotes_Click(object sender, RoutedEventArgs e)
        {
            var unity = GetSelectedUpdate();
            Tools.OpenReleaseNotes(unity?.Version);
        }

        private void BtnClearBuildReportSearch_Click(object sender, RoutedEventArgs e)
        {
            txtSearchBoxBuildReport.Text = "";
        }

        private void TxtSearchBoxBuildReport_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Up:
                case Key.Down:
                    Tools.SetFocusToGrid(gridBuildReport);
                    break;
                default:
                    break;
            }
        }

        private void TxtSearchBoxBuildReport_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (gridBuildReport.ItemsSource != null) FilterBuildReport();
        }

        private void TxtLogCatArgs_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init
            Properties.Settings.Default.adbLogCatArgs = txtLogCatArgs.Text;
            Properties.Settings.Default.Save();
            adbLogCatArgs = txtLogCatArgs.Text;
        }

        private void BtnResetLogCatArgs_Click(object sender, RoutedEventArgs e)
        {
            adbLogCatArgs = defaultAdbLogCatArgs;
            Properties.Settings.Default.adbLogCatArgs = defaultAdbLogCatArgs;
            Properties.Settings.Default.Save();
            txtLogCatArgs.Text = defaultAdbLogCatArgs;
        }

        private void BtnResources_Click(object sender, RoutedEventArgs e)
        {
            Tools.OpenURL(resourcesURL);
        }

        public void ProcessExitedCallBack(Project proj)
        {
            //Console.WriteLine("Process Exited: " + proj.Path);
            //var index = projectsSource.IndexOf(proj); // this fails since proj has changed after refresh (timestamp or other data)

            // if currently editing field, cancel it (otherwise crash)
            IEditableCollectionView itemsView = gridRecent.Items;
            if (itemsView.IsAddingNew || itemsView.IsEditingItem)
            {
                gridRecent.CancelEdit(DataGridEditingUnit.Cell);
            }

            // FIXME nobody likes extra loops.. but only # items to find correct project? but still..
            for (int i = 0, len = projectsSource.Count; i < len; i++)
            {
                if (projectsSource[i].Path == proj.Path)
                {
                    var tempProj = projectsSource[i];
                    tempProj.Modified = Tools.GetLastModifiedTime(proj.Path);
                    tempProj.Version = Tools.GetProjectVersion(proj.Path);
                    tempProj.GITBranch = Tools.ReadGitBranchInfo(proj.Path, false);
                    tempProj.TargetPlatform = Tools.GetTargetPlatform(proj.Path);
                    projectsSource[i] = tempProj;
                    gridRecent.Items.Refresh();
                    break;
                }
            }
        }

        private void MenuItemDownloadInBrowser_Click(object sender, RoutedEventArgs e)
        {
            var unity = GetSelectedUpdate();
            Tools.DownloadInBrowser(unity?.Version);
        }

        private void MenuItemDownloadLinuxModule_Click(object sender, RoutedEventArgs e)
        {
            var unity = GetSelectedUnity();
            if (unity == null) return;
            Tools.DownloadAdditionalModules(unity.Path, unity.Version, "Linux-IL2CPP");
            Tools.DownloadAdditionalModules(unity.Path, unity.Version, "Linux-Mono");
        }

        private void RadioProjNameFolder_Checked(object sender, RoutedEventArgs e)
        {
            projectNameSetting = 0; // default, folder
            if (this.IsActive == false) return; // dont run code on window init
            RefreshRecentProjects();
        }

        private void RadioProjNameProductName_Checked(object sender, RoutedEventArgs e)
        {
            projectNameSetting = 1; // player settings, product name
            if (this.IsActive == false) return; // dont run code on window init
            RefreshRecentProjects();
        }

        private void BtnThemeEditor_Click(object sender, RoutedEventArgs e)
        {
            if (themeEditorWindow != null && themeEditorWindow.IsVisible == true)
            {
                themeEditorWindow.Activate();
                return;
            }
            themeEditorWindow = new ThemeEditor();
            themeEditorWindow.Show();
        }

        private void MenuRemoveProject_Click(object sender, RoutedEventArgs e)
        {
            RemoveProjectFromList();
        }

        private void MenuItemDownloadAndroidModule_Click(object sender, RoutedEventArgs e)
        {
            var unity = GetSelectedUnity();
            if (unity == null) return;
            Tools.DownloadAdditionalModules(unity.Path, unity.Version, "Android");
        }

        private void MenuItemDownloadIOSModule_Click(object sender, RoutedEventArgs e)
        {
            var unity = GetSelectedUnity();
            if (unity == null) return;
            Tools.DownloadAdditionalModules(unity.Path, unity.Version, "iOS");
        }

        private void MenuItemDownloadWebGLModule_Click(object sender, RoutedEventArgs e)
        {
            var unity = GetSelectedUnity();
            if (unity == null) return;
            Tools.DownloadAdditionalModules(unity.Path, unity.Version, "WebGL");
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            // need to run here, so that main window gets hidden if start from commandline as new/upgrade project
            Start();
        }

        private void ChkSearchProjectPath_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            var isChecked = (bool)((CheckBox)sender).IsChecked;

            searchProjectPathAlso = isChecked;
            Settings.Default.searchProjectPathAlso = isChecked;
            Settings.Default.Save();
        }

        private void BtnCrashDumps_Click(object sender, RoutedEventArgs e)
        {
            Tools.OpenAppdataSpecialFolder("CrashDumps");
        }

        private void BtnGICache_Click(object sender, RoutedEventArgs e)
        {
            Tools.OpenAppdataSpecialFolder("../LocalLow/Unity/Caches/GiCache");
        }

        private void BtnUnityCache_Click(object sender, RoutedEventArgs e)
        {
            Tools.OpenAppdataSpecialFolder("Unity/cache");
        }

        private void MenuBatchBuildAndroid_Click(object sender, RoutedEventArgs e)
        {
            var proj = GetSelectedProject();
            Tools.BuildProject(proj, Platform.Android);
        }

        private void MenuBatchBuildIOS_Click(object sender, RoutedEventArgs e)
        {
            var proj = GetSelectedProject();
            Tools.BuildProject(proj, Platform.iOS);
        }

        private void MenuBatchBuildCustom_Click(object sender, RoutedEventArgs e)
        {
            var proj = GetSelectedProject();
            Tools.BuildProjectCustom(proj);
        }

        private void ChkCheckPlasticBranch_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            Settings.Default.checkPlasticBranch = (bool)chkCheckPlasticBranch.IsChecked;
            Settings.Default.Save();
            RefreshRecentProjects();
        }

        private void MenuCreateDesktopShortCut_Click(object sender, RoutedEventArgs e)
        {
            var proj = GetSelectedProject();
            var res = Tools.CreateDesktopShortCut(proj, txtShortcutBatchFileFolder.Text);
            if (res == false)
            {
                Console.WriteLine("Failed to create shortcut, maybe batch folder location is invalid..");
                SetStatus("Failed to create shortcut, maybe batch folder location is invalid: " + txtShortcutBatchFileFolder.Text);
            }
        }

        private void TxtShortcutBatchFileFolder_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            var folder = ((TextBox)sender).Text;
            if (Directory.Exists(folder))
            {
                Settings.Default.shortcutBatchFileFolder = folder;
                Settings.Default.Save();
            }
        }

        private void TxtShortcutBatchFileFolder_LostFocus(object sender, RoutedEventArgs e)
        {
            ValidateFolderFromTextbox((TextBox)sender);
        }

        private void BtnBrowseBatchFileFolder_Click(object sender, RoutedEventArgs e)
        {
            // TODO change directory browsing window title (now its Project folder)
            var folder = Tools.BrowseForOutputFolder("Select folder for .bat shortcut files", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appName));
            if (string.IsNullOrEmpty(folder) == false)
            {
                txtShortcutBatchFileFolder.Text = folder;
                Properties.Settings.Default.shortcutBatchFileFolder = folder;
                Properties.Settings.Default.Save();
            }
        }

        private void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.F5: // update build reports
                    e.Handled = true;
                    RefreshBuildReports();
                    break;
                case Key.Return: // open build report
                    e.Handled = true;
                    OpenSelectedBuildReportFile();
                    break;
            }
        }

        private void menuItemCopyPathToClipboard_Click(object sender, RoutedEventArgs e)
        {
            var path = GetSelectedBuildItem().Path;
            if (path != null) path = Path.Combine(currentBuildReportProjectPath, path);
            path = path.Replace('\\', '/');
            Clipboard.SetText(path);
        }

        private void dataGridUpdates_Sorting(object sender, DataGridSortingEventArgs e)
        {
            SortHandlerUpdates(sender, e);
        }

        // TODO combine similar methods
        void SortHandlerUpdates(object sender, DataGridSortingEventArgs e)
        {
            DataGridColumn column = e.Column;

            IComparer comparer = null;

            // prevent the built-in sort from sorting
            e.Handled = true;

            ListSortDirection direction = (column.SortDirection != ListSortDirection.Ascending) ? ListSortDirection.Ascending : ListSortDirection.Descending;

            //set the sort order on the column
            column.SortDirection = direction;

            //use a ListCollectionView to do the sort.
            ListCollectionView lcv = (ListCollectionView)CollectionViewSource.GetDefaultView(dataGridUpdates.ItemsSource);

            comparer = new CustomUpdatesSort(direction, column.Header.ToString());

            //apply the sort
            lcv.CustomSort = comparer;
        }

        public class CustomUpdatesSort : IComparer
        {
            private ListSortDirection direction;
            private string sortBy;

            public CustomUpdatesSort(ListSortDirection direction, string sortBy)
            {
                this.direction = direction;
                this.sortBy = sortBy;
            }

            public int Compare(Object a, Object b)
            {
                switch (sortBy)
                {
                    case "Version":
                        // handle null values
                        if (((UnityVersion)a)?.Version == null && ((UnityVersion)b)?.Version == null) return 0;
                        if (((UnityVersion)a)?.Version == null) return direction == ListSortDirection.Ascending ? -1 : 1;
                        if (((UnityVersion)b)?.Version == null) return direction == ListSortDirection.Ascending ? 1 : -1;
                        return direction == ListSortDirection.Ascending ? Tools.VersionAsLong(((UnityVersion)a).Version).CompareTo(Tools.VersionAsLong(((UnityVersion)b).Version)) : Tools.VersionAsLong(((UnityVersion)b).Version).CompareTo(Tools.VersionAsLong(((UnityVersion)a).Version));
                    case "Released":
                        // handle null values
                        if (((UnityVersion)a)?.ReleaseDate == null && ((UnityVersion)b)?.ReleaseDate == null) return 0;
                        if (((UnityVersion)a)?.ReleaseDate == null) return direction == ListSortDirection.Ascending ? -1 : 1;
                        if (((UnityVersion)b)?.ReleaseDate == null) return direction == ListSortDirection.Ascending ? 1 : -1;
                        return direction == ListSortDirection.Ascending ? ((UnityVersion)a).ReleaseDate.CompareTo(((UnityVersion)b).ReleaseDate) : ((DateTime)((UnityVersion)b).ReleaseDate).CompareTo(((UnityVersion)a).ReleaseDate);
                    default:
                        return 0;
                }
            }
        }

        private void gridRecent_Sorting(object sender, DataGridSortingEventArgs e)
        {
            SortHandlerRecentProjects(sender, e);
        }

        // https://stackoverflow.com/a/2130557/5452781
        void SortHandlerRecentProjects(object sender, DataGridSortingEventArgs e)
        {
            // TESTing fix for null ref in commandline start
            if (gridRecent.ItemsSource == null) return;

            DataGridColumn column = e.Column;

            // save current sort to prefs
            Settings.Default.currentSortColumn = column.Header.ToString();

            IComparer comparer = null;

            // prevent the built-in sort from sorting
            e.Handled = true;

            // load sort dir
            column.SortDirection = Settings.Default.currentSortDirectionAscending ? ListSortDirection.Ascending : ListSortDirection.Descending;

            ListSortDirection direction = (column.SortDirection != ListSortDirection.Ascending) ? ListSortDirection.Ascending : ListSortDirection.Descending;

            // save
            Settings.Default.currentSortDirectionAscending = (direction == ListSortDirection.Ascending);

            //set the sort order on the column
            column.SortDirection = direction;

            //use a ListCollectionView to do the sort.
            ListCollectionView lcv = (ListCollectionView)CollectionViewSource.GetDefaultView(gridRecent.ItemsSource);

            comparer = new CustomProjectSort(direction, column.Header.ToString());

            //apply the sort
            lcv.CustomSort = comparer;

            if (gridRecent.SelectedItem != null)
            {
                // scroll view to selected, after sort
                gridRecent.ScrollIntoView(gridRecent.SelectedItem);
                // needed for keyboard to work in grid
                gridRecent.Focus();
            }
        }

        public class CustomProjectSort : IComparer
        {
            private ListSortDirection direction;
            private string sortBy;

            public CustomProjectSort(ListSortDirection direction, string sortBy)
            {
                this.direction = direction;
                this.sortBy = sortBy;
            }

            // TODO cleanup this
            public int Compare(Object a, Object b)
            {
                switch (sortBy)
                {
                    case "Project":
                        return direction == ListSortDirection.Ascending ? ((Project)a).Title.CompareTo(((Project)b).Title) : ((Project)b).Title.CompareTo(((Project)a).Title);
                    case "Version":
                        // handle null values
                        if (((Project)a).Version == null && ((Project)b).Version == null) return 0;
                        if (((Project)a).Version == null) return direction == ListSortDirection.Ascending ? -1 : 1;
                        if (((Project)b).Version == null) return direction == ListSortDirection.Ascending ? 1 : -1;
                        return direction == ListSortDirection.Ascending ? Tools.VersionAsLong(((Project)a).Version).CompareTo(Tools.VersionAsLong(((Project)b).Version)) : Tools.VersionAsLong(((Project)b).Version).CompareTo(Tools.VersionAsLong(((Project)a).Version));
                    case "Path":
                        return direction == ListSortDirection.Ascending ? ((Project)a).Path.CompareTo(((Project)b).Path) : ((Project)b).Path.CompareTo(((Project)a).Path);
                    case "Modified":
                        // handle null values
                        if (((Project)a).Modified == null && ((Project)b).Modified == null) return 0;
                        if (((Project)a).Modified == null) return direction == ListSortDirection.Ascending ? -1 : 1;
                        if (((Project)b).Modified == null) return direction == ListSortDirection.Ascending ? 1 : -1;
                        return direction == ListSortDirection.Ascending ? ((DateTime)((Project)a).Modified).CompareTo(((Project)b).Modified) : ((DateTime)((Project)b).Modified).CompareTo(((Project)a).Modified);
                    case "Arguments":
                        // handle null values
                        if (((Project)a).Arguments == null && ((Project)b).Arguments == null) return 0;
                        if (((Project)a).Arguments == null) return direction == ListSortDirection.Ascending ? -1 : 1;
                        if (((Project)b).Arguments == null) return direction == ListSortDirection.Ascending ? 1 : -1;
                        return direction == ListSortDirection.Ascending ? ((Project)a).Arguments.CompareTo(((Project)b).Arguments) : ((Project)b).Arguments.CompareTo(((Project)a).Arguments);
                    case "Branch":
                        // handle null values
                        if (((Project)a).GITBranch == null && ((Project)b).GITBranch == null) return 0;
                        if (((Project)a).GITBranch == null) return direction == ListSortDirection.Ascending ? -1 : 1;
                        if (((Project)b).GITBranch == null) return direction == ListSortDirection.Ascending ? 1 : -1;
                        return direction == ListSortDirection.Ascending ? ((Project)a).GITBranch.CompareTo(((Project)b).GITBranch) : ((Project)b).GITBranch.CompareTo(((Project)a).GITBranch);
                    case "Platform":
                        // handle null values
                        if (((Project)a).TargetPlatform == null && ((Project)b).TargetPlatform == null) return 0;
                        if (((Project)a).TargetPlatform == null) return direction == ListSortDirection.Ascending ? -1 : 1;
                        if (((Project)b).TargetPlatform == null) return direction == ListSortDirection.Ascending ? 1 : -1;
                        return direction == ListSortDirection.Ascending ? ((Project)a).TargetPlatform.CompareTo(((Project)b).TargetPlatform) : ((Project)b).TargetPlatform.CompareTo(((Project)a).TargetPlatform);
                    case "SRP":
                        // handle null values
                        if (((Project)a).SRP == null && ((Project)b).SRP == null) return 0;
                        if (((Project)a).SRP == null) return direction == ListSortDirection.Ascending ? -1 : 1;
                        if (((Project)b).SRP == null) return direction == ListSortDirection.Ascending ? 1 : -1;
                        return direction == ListSortDirection.Ascending ? ((Project)a).SRP.CompareTo(((Project)b).SRP) : ((Project)b).SRP.CompareTo(((Project)a).SRP);
                    default:
                        return 0;
                }
            }
        }

        private void btnExploreScriptsFolder_Click(object sender, RoutedEventArgs e)
        {
            if (Tools.LaunchExplorer(Path.GetDirectoryName(initScriptFileFullPath)) == false)
            {
                if (string.IsNullOrEmpty(initScriptFileFullPath) == true)
                {
                    initScriptFileFullPath = Tools.GetSafeFilePath("Scripts", "InitializeProject.cs");
                }

                // if failed, open parent folder (probably path was using URL or no scripts yet)
                var parentPath = Directory.GetParent(Path.GetDirectoryName(initScriptFileFullPath)).FullName;
                if (Tools.LaunchExplorer(parentPath) == false)
                {
                    // if still failed, open exe folder
                    Tools.LaunchExplorer(AppDomain.CurrentDomain.BaseDirectory);
                }
            }
        }

        private void txtCustomInitFileURL_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Return: // pressed enter in theme file text box
                    Settings.Default.customInitFileURL = txtCustomInitFileURL.Text;
                    Settings.Default.Save();
                    break;
            }
        }

        private void txtCustomInitFileURL_LostFocus(object sender, RoutedEventArgs e)
        {
            var s = (TextBox)sender;
            Settings.Default.customInitFileURL = s.Text;
            Settings.Default.Save();
        }

        private void chkUseInitScript_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            var isChecked = (bool)((CheckBox)sender).IsChecked;
            Settings.Default.useInitScript = isChecked;
            Settings.Default.Save();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            gridSettingsBg.Focus();
        }

        public void SetStatus(string msg, MessageType messageType = MessageType.Info)
        {
            //Console.WriteLine(messageType);
            switch (messageType)
            {
                case MessageType.Info:
                    txtStatus.Foreground = (SolidColorBrush)Application.Current.Resources["ThemeStatusText"];
                    break;
                case MessageType.Warning:
                    txtStatus.Foreground = (SolidColorBrush)Application.Current.Resources["ThemeMessageWarning"];
                    break;
                case MessageType.Error:
                    txtStatus.Foreground = (SolidColorBrush)Application.Current.Resources["ThemeMessageError"];
                    break;
            }

            txtStatus.Text = msg;
            txtStatus.ToolTip = msg;
        }

        public void SetBuildStatus(System.Windows.Media.Color color)
        {
            btnBuildStatus.Foreground = new SolidColorBrush(color);
        }

        private void btnPatchHubConfig_Click(object sender, RoutedEventArgs e)
        {
            // read the config file from %APPDATA%
            var configFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UnityHub", "editors.json");

            var result = MessageBox.Show("This will modify current " + configFile + " file. Are you sure you want to continue? (This cannot be undone, we dont know which 'manual:'-value was already set to 'false' (but it shouldnt break anything))", "Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                if (File.Exists(configFile) == true)
                {
                    // read the config file
                    var json = File.ReadAllText(configFile);
                    // replace the manual:true with manual:false using regex
                    json = json.Replace("\"manual\":true", "\"manual\":false");

                    //Console.WriteLine(json);

                    // write the config file
                    File.WriteAllText(configFile, json);
                    SetStatus("editors.json file saved");
                }
                else
                {
                    SetStatus(configFile + " not found");
                }
            }
            else
            {
                SetStatus("Cancelled");
            }
        }

        private void menuInstallLastAPK_Click(object sender, RoutedEventArgs e)
        {
            var proj = GetSelectedProject();

            var yamlFile = Path.Combine(proj.Path, "Library", "PlayerDataCache", "Android", "ScriptsOnlyCache.yaml");
            if (File.Exists(yamlFile) == false)
            {
                SetStatus("No ScriptsOnlyCache.yaml file found");
                return;
            }
            var yaml = File.ReadAllLines(yamlFile);
            // loop rows until "playerPath:"
            string playerPath = null;
            foreach (var row in yaml)
            {
                if (row.IndexOf("playerPath:") > -1)
                {
                    // get the player path
                    playerPath = row.Substring(row.IndexOf(":") + 1).Trim();
                    break;
                }
            }

            if (playerPath == null)
            {
                SetStatus("No playerPath found in ScriptsOnlyCache.yaml");
                return;
            }

            // install the apk using ADB using cmd (-r = replace app)
            var cmd = "cmd.exe";
            var pars = $"/C adb install -r \"{playerPath}\"";

            string packageName = null;

            // get package name from ProjectSettings.asset
            var psPath = Path.Combine(proj.Path, "ProjectSettings", "ProjectSettings.asset");
            if (File.Exists(psPath) == true)
            {
                // read project settings
                var rows = File.ReadAllLines(psPath);

                // search applicationIdentifier, Android:
                for (int i = 0, len = rows.Length; i < len; i++)
                {
                    // skip rows until companyname
                    if (rows[i].Trim().IndexOf("applicationIdentifier:") > -1)
                    {
                        var temp = rows[i + 1].Trim();
                        if (temp.IndexOf("Android:") > -1)
                        {
                            // get package name
                            packageName = temp.Substring(temp.IndexOf(":") + 1).Trim();
                            break;
                        }
                    }
                }
            }

            // launch app
            if (string.IsNullOrEmpty(packageName) == false)
            {
                pars += $" && adb shell monkey -p {packageName} 1";
            }

            try
            {
                //Tools.LaunchExe(cmd, pars);
                var process = Tools.LaunchExe(cmd, pars, captureOutput: true);
                var output = process.StandardOutput.ReadToEnd();
                var errorOutput = process.StandardError.ReadToEnd().Replace("\r", "").Replace("\n", "");

                process.WaitForExit();

                // Console.WriteLine(output);
                if (!string.IsNullOrEmpty(errorOutput))
                {
                    SetStatus("Error installing APK: " + errorOutput);
                }
                else
                {
                    // get apk name from path
                    var apkName = Path.GetFileName(playerPath);
                    if (chkStreamerMode.IsChecked == true) apkName = " (hidden in streamermode)";
                    SetStatus("Installed APK:" + apkName);
                }

            }
            catch (Win32Exception ex)
            {
                // Handle case where 'adb' is not found
                SetStatus($"Error: 'adb' not found. Ensure it's installed and added to PATH. Details: {ex.Message}");
            }
            catch (Exception ex)
            {
                // Handle other unexpected exceptions
                SetStatus($"An unexpected error occurred: {ex.Message}");
            }
        }

        private void txtWebglPort_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isInitializing == true) return;

            var ok = ValidateIntRange((TextBox)sender, 50000, 65534);
            if (ok)
            {
                var num = int.Parse(((TextBox)sender).Text);
                webglPort = num;
                Properties.Settings.Default.webglPort = num;
                Properties.Settings.Default.Save();
                SetStatus("WebGL port set to " + num);
            }
        }

        private void txtWebglPort_LostFocus(object sender, RoutedEventArgs e)
        {
            // TODO duplicate code
            var ok = ValidateIntRange((TextBox)sender, 50000, 65534);
            if (ok)
            {
                var num = int.Parse(((TextBox)sender).Text);
                webglPort = num;
                Properties.Settings.Default.webglPort = num;
                Properties.Settings.Default.Save();
                SetStatus("WebGL port set to " + num);
            }
        }

        private void Button_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // only can do restore here, we dont want accidental maximize (max window not really needed)
            if (this.WindowState == WindowState.Maximized) this.WindowState = WindowState.Normal;
            // NOTE workaround for grid not focused when coming back from minimized window
            Tools.SetFocusToGrid(gridRecent, gridRecent.SelectedIndex);
        }

        private void chkOverride40ProjectCount_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            Properties.Settings.Default.override40ProjectCount = (bool)((CheckBox)sender).IsChecked;
            Properties.Settings.Default.Save();
            if ((bool)chkOverride40ProjectCount.IsChecked)
            {
                maxProjectCount = Properties.Settings.Default.maxProjectCount;
            }
            else
            {
                maxProjectCount = 40;
            }
        }

        private void txtMaxProjectCount_LostFocus(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            var ok = ValidateIntRange((TextBox)sender, 10, 1024);
            maxProjectCount = 40;
            if (ok)
            {
                var num = int.Parse(((TextBox)sender).Text);
                maxProjectCount = num;
                Properties.Settings.Default.maxProjectCount = num;
                Properties.Settings.Default.Save();
                SetStatus("Max project count set to " + num);
            }
        }

        private void txtMaxProjectCount_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            // TODO remove duplicate code
            var ok = ValidateIntRange((TextBox)sender, 10, 1024);
            maxProjectCount = 40;
            if (ok)
            {
                var num = int.Parse(((TextBox)sender).Text);
                maxProjectCount = num;
                Properties.Settings.Default.maxProjectCount = num;
                Properties.Settings.Default.Save();
                SetStatus("Max project count set to " + num);
            }
        }

        private void rdoAll_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init
            FilterUpdates();
        }

        private void menuItemDownloadIL2CPPModule_Click(object sender, RoutedEventArgs e)
        {
            var unity = GetSelectedUnity();
            if (unity == null) return;
            Tools.DownloadAdditionalModules(unity.Path, unity.Version, "Windows-IL2CPP");
        }

        private void menuItemDownloadWinDedicatedServerModule_Click(object sender, RoutedEventArgs e)
        {
            var unity = GetSelectedUnity();
            if (unity == null) return;
            Tools.DownloadAdditionalModules(unity.Path, unity.Version, "Windows-Server");
        }

        private void menuItemDownloadLinuxDedicatedServerModule_Click(object sender, RoutedEventArgs e)
        {
            var unity = GetSelectedUnity();
            if (unity == null) return;
            Tools.DownloadAdditionalModules(unity.Path, unity.Version, "Linux-Server");
        }

        private void menuUninstallEditor_Click(object sender, RoutedEventArgs e)
        {
            var unity = GetSelectedUnity();
            if (unity == null) return;
            Tools.UninstallEditor(unity.Path, unity.Version);

            var currentIndex = dataGridUnitys.SelectedIndex;

            // TODO refresh list after exe's have finished (but for now just remove after hit uninstall, since would need to keep track of uninstall exes)
            unityInstallationsSource.Remove(unity);
            dataGridUnitys.Items.Refresh();
            Tools.SetFocusToGrid(dataGridUnitys);
            dataGridUnitys.SelectedIndex = currentIndex - 1 < 0 ? 0 : currentIndex - 1;
        }

        private void tabControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // if press up or down, while tab control is focused, move focus to grid
            if (e.Key == Key.Up || e.Key == Key.Down)
            {
                if (tabControl.SelectedIndex == 0)
                {
                    Tools.SetFocusToGrid(gridRecent, gridRecent.SelectedIndex);
                }
                else if (tabControl.SelectedIndex == 1)
                {
                    Tools.SetFocusToGrid(dataGridUnitys, dataGridUnitys.SelectedIndex);
                }
                else if (tabControl.SelectedIndex == 2)
                {
                    Tools.SetFocusToGrid(dataGridUpdates, dataGridUpdates.SelectedIndex);
                }
            }
        }

        private void btnFetchLatestInitScript_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(initScriptFileFullPath) == true)
            {
                initScriptFileFullPath = Tools.GetSafeFilePath("Scripts", "InitializeProject.cs");
            }

            Tools.DownloadInitScript(initScriptFileFullPath, txtCustomInitFileURL.Text);
        }

        private void btnHubLogs_Click(object sender, RoutedEventArgs e)
        {
            Tools.OpenAppdataSpecialFolder("../Roaming/UnityHub/logs/");
        }

        private void btnOpenEditorLogsFolder_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                var logfolder = Tools.GetEditorLogsFolder();
                var logFile = Path.Combine(logfolder, "Editor.log");
                if (File.Exists(logFile) == true) Tools.LaunchExe(logFile);
            }
        }

        private void UseAlphaReleaseNotes_Checked(object sender, RoutedEventArgs e)
        {
            var isChecked = (bool)((CheckBox)sender).IsChecked;

            Settings.Default.useAlphaReleaseNotes = isChecked;
            Settings.Default.Save();
        }

        private void ActivateRunningInstance()
        {
            try
            {
                using (var pipeClient = new NamedPipeClientStream(".", launcherPipeName, PipeDirection.Out))
                {
                    pipeClient.Connect(1000); // Wait for 1 second to connect
                    using (var writer = new StreamWriter(pipeClient))
                    {
                        writer.WriteLine("WakeUp");
                        writer.Flush();
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle connection failure (e.g., pipe not available)
                Console.WriteLine("Could not connect to the running instance: " + ex.Message);
            }
        }

        private void StartPipeServer()
        {
            launcherPipeServer = new NamedPipeServerStream(launcherPipeName, PipeDirection.In, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
            launcherPipeServer.BeginWaitForConnection(OnPipeConnection, null);
        }

        private void OnPipeConnection(IAsyncResult result)
        {
            try
            {
                launcherPipeServer.EndWaitForConnection(result);

                // Read the message
                using (var reader = new StreamReader(launcherPipeServer))
                {
                    string message = reader.ReadLine();
                    if (message == "WakeUp")
                    {
                        Dispatcher.Invoke(() =>
                        {
                            // Bring the app to the foreground
                            RestoreFromTray();
                        });
                    }
                    else if (message.StartsWith("OpenProject:"))
                    {
                        string[] sep = new string[] { "<|>" };
                        var projData = message.Split(sep, StringSplitOptions.None);

                        Dispatcher.Invoke(() =>
                        {
                            var proj = new Project();
                            proj.Version = projData[1];
                            proj.Path = projData[2];
                            proj.Arguments = projData[3];
                            Tools.LaunchProject(proj);
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle exceptions
                Console.WriteLine(ex);
            }
            finally
            {
                // Restart pipe server to listen for new messages
                StartPipeServer();
            }
        }

        private void CheckCustomIcon()
        {
            string customIconPath = Path.Combine(Environment.CurrentDirectory, "icon.ico");
            if (File.Exists(customIconPath))
            {
                try
                {
                    // Load the custom icon using System.Drawing.Icon
                    using (var icon = new Icon(customIconPath))
                    {
                        // Convert the icon to a BitmapSource and assign it to the WPF window's Icon property
                        this.Icon = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        // window icon
                        IconImage.Source = this.Icon;
                        // tray icon
                        notifyIcon.Icon = new Icon(customIconPath);
                    }
                }
                catch (Exception ex)
                {
                    SetStatus("Failed to load custom icon: " + ex.Message, MessageType.Warning);
                    Debug.WriteLine($"Failed to load custom icon: {ex.Message}");
                }
            }
            else // no custom icon found
            {
                notifyIcon.Icon = new Icon(Application.GetResourceStream(new Uri("pack://application:,,,/Images/icon.ico")).Stream);
                //Debug.WriteLine("Custom icon not found. Using default.");
            }
        }

        private void chkCheckSRP_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            gridRecent.Columns[7].Visibility = (bool)chkCheckSRP.IsChecked ? Visibility.Visible : Visibility.Collapsed;

            Settings.Default.checkSRP = (bool)chkCheckSRP.IsChecked;
            Settings.Default.Save();
            RefreshRecentProjects();
        }

        private void useUnofficialReleaseList_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            Settings.Default.useUnofficialReleaseList = (bool)useUnofficialReleaseList.IsChecked;
            Settings.Default.Save();
        }

        private async void chkDisableUnityHubLaunch_Checked(object sender, RoutedEventArgs e)
        {
            if (!this.IsActive) return; // Don't run code during window initialization

            //Console.WriteLine((bool)chkDisableUnityHubLaunch.IsChecked);

            if ((bool)chkDisableUnityHubLaunch.IsChecked)
            {
                await CloseHubPipeAsync(); // Ensure old task is closed before starting a new one
                StartHubPipe();
            }
            else
            {
                await CloseHubPipeAsync();
            }

            Settings.Default.disableUnityHubLaunch = (bool)chkDisableUnityHubLaunch.IsChecked;
            Settings.Default.Save();
        }

        private void StartHubPipe()
        {
            if (_hubCancellationTokenSource != null && !_hubCancellationTokenSource.IsCancellationRequested)
            {
                Console.WriteLine("Pipe server already running.");
                return; // Avoid starting multiple instances
            }

            _hubCancellationTokenSource = new CancellationTokenSource();
            Task.Run(() => StartPipeServerAsync("Unity-hubIPCService", OnHubMessageReceived, _hubCancellationTokenSource.Token));
            Console.WriteLine("StartHubPipe");
        }

        private async Task CloseHubPipeAsync()
        {
            if (_hubCancellationTokenSource == null || _hubCancellationTokenSource.IsCancellationRequested)
            {
                Console.WriteLine("Pipe server already stopped.");
                return;
            }

            Console.WriteLine("CloseHubPipe..");

            // Cancel the token to stop the server task
            _hubCancellationTokenSource.Cancel();

            try
            {
                // Allow the server to shut down gracefully
                await Task.Delay(100); // Optional: Give some time for clean-up
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during pipe server shutdown: {ex.Message}");
            }
            finally
            {
                _hubCancellationTokenSource.Dispose();
                _hubCancellationTokenSource = null;
                Console.WriteLine("Pipe server stopped.");
            }
        }

        private void chkRegisterInstallAPKMenu_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            if ((bool)chkRegisterInstallAPKMenu.IsChecked)
            {
                Tools.AddContextMenuRegistryAPKInstall(contextRegRoot);
            }
            else // remove
            {
                Tools.RemoveContextMenuRegistryAPKInstall(contextRegRoot);
            }

            Settings.Default.registerExplorerMenuAPK = (bool)chkRegisterInstallAPKMenu.IsChecked;
            Settings.Default.Save();

        }

        private void btnExcludeFolderForDefender_Click(object sender, RoutedEventArgs e)
        {
            var foldersToExclude = new List<string>();
            foreach (var unity in unityInstallationsSource)
            {
                var unityEditorFolder = Path.GetDirectoryName(unity.Path);
                //Console.WriteLine(unityEditorFolder);
                if (Directory.Exists(unityEditorFolder))
                {
                    foldersToExclude.Add(unityEditorFolder);
                }
            }

            Tools.RunExclusionElevated(foldersToExclude);
        }

        private void menuExcludeFromDefender_Click(object sender, RoutedEventArgs e)
        {
            var proj = GetSelectedProject();
            if (proj == null) return;
            if (Directory.Exists(proj.Path) == false) return;

            var foldersToExclude = new List<string>();
            foldersToExclude.Add(proj.Path);
            var res = Tools.RunExclusionElevated(foldersToExclude, silent: true);
            var tempPath = ((chkStreamerMode.IsChecked == true) ? "***" : proj.Path);
            if (res == false)
            {
                SetStatus("Failed to add exclusion for: " + tempPath);
            }
            else
            {
                SetStatus("Added exclusion for project path: " + tempPath);
            }
        }

        private void btnPurgeMissingFolders_Click(object sender, RoutedEventArgs e)
        {
            var validPaths = new List<string>();
            int removedCount = 0;
            foreach (string path in Settings.Default.projectPaths)
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    validPaths.Add(path);
                }
                else
                {
                    Console.WriteLine("Path not found: " + path);
                    removedCount++;
                }
            }

            // Replace the old collection with the filtered one
            var newCollection = new StringCollection();
            foreach (string path in validPaths)
            {
                newCollection.Add(path);
            }

            Settings.Default.projectPaths = newCollection;
            Settings.Default.Save();

            SetStatus("Purged " + removedCount + " items", MessageType.Info);
        }

        private void chkFetchAdditionalInfo_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            Settings.Default.fetchAdditionalInfo = (bool)chkFetchAdditionalInfo.IsChecked;
            Settings.Default.Save();
        }

        private void gridRecent_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void Image_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var proj = GetSelectedProject();
            if (proj == null) return;
            var thumbnailPath = Path.Combine(proj.Path, "ProjectSettings", "icon.png");
            Tools.LaunchExe(thumbnailPath);
        }

        private void chkFetchOnlineTemplates_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return;

            Settings.Default.fetchOnlineTemplates = (bool)chkFetchOnlineTemplates.IsChecked;
            Settings.Default.Save();
        }

        //private void menuProjectProperties_Click(object sender, RoutedEventArgs e)
        //{
        //    var proj = GetSelectedProject();
        //    if (proj == null) return;
        //    Tools.DisplayProjectProperties(proj, this);
        //}
    } // class
} //namespace

