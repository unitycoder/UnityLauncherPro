// Unity Project Launcher by https://unitycoder.com
// https://github.com/unitycoder/UnityLauncherPro

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing; // for notifyicon
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using UnityLauncherPro.Helpers;

namespace UnityLauncherPro
{
    public partial class MainWindow : Window
    {
        public const string appName = "UnityLauncherPro";
        public static string currentDateFormat = null;
        public static bool useHumanFriendlyDateFormat = false;
        public static bool searchProjectPathAlso = false;
        public static List<Project> projectsSource;
        public static UnityInstallation[] unityInstallationsSource;
        public static ObservableDictionary<string, string> unityInstalledVersions = new ObservableDictionary<string, string>(); // versionID and installation folder
        public static readonly string launcherArgumentsFile = "LauncherArguments.txt";
        public static readonly string projectNameFile = "ProjectName.txt";
        public static string preferredVersion = "none";
        public static int projectNameSetting = 0; // 0 = folder or ProjectName.txt if exists, 1=ProductName

        const string contextRegRoot = "Software\\Classes\\Directory\\Background\\shell";
        const string githubURL = "https://github.com/unitycoder/UnityLauncherPro";
        const string resourcesURL = "https://github.com/unitycoder/UnityResources";
        const string defaultAdbLogCatArgs = "-s Unity ActivityManager PackageManager dalvikvm DEBUG -v color";
        System.Windows.Forms.NotifyIcon notifyIcon;

        Updates[] updatesSource;

        string _filterString = null;
        int lastSelectedProjectIndex = 0;
        Mutex myMutex;
        ThemeEditor themeEditorWindow;

        string defaultDateFormat = "dd/MM/yyyy HH:mm:ss";
        string adbLogCatArgs = defaultAdbLogCatArgs;

        Dictionary<string, SolidColorBrush> origResourceColors = new Dictionary<string, SolidColorBrush>();

        string currentBuildReportProjectPath = null;
        //List<List<string>> buildReports = new List<List<string>>();
        List<BuildReport> buildReports = new List<BuildReport>(); // multiple reports, each contains their own stats and items
        int currentBuildReport = 0;

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

            // need to load here to get correct window size early
            LoadSettings();
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
                bool aIsNewInstance = false;
                myMutex = new Mutex(true, appName, out aIsNewInstance);
                if (!aIsNewInstance)
                {
                    // NOTE doesnt work if its minized to tray
                    ActivateOtherWindow();
                    App.Current.Shutdown();
                }
            }

            // update projects list
            projectsSource = GetProjects.Scan(getGitBranch: (bool)chkShowGitBranchColumn.IsChecked, getPlasticBranch: (bool)chkCheckPlasticBranch.IsChecked, getArguments: (bool)chkShowLauncherArgumentsColumn.IsChecked, showMissingFolders: (bool)chkShowMissingFolderProjects.IsChecked, showTargetPlatform: (bool)chkShowPlatform.IsChecked);
            gridRecent.Items.Clear();
            gridRecent.ItemsSource = projectsSource;

            // clear updates grid
            dataGridUpdates.Items.Clear();

            // clear buildreport grid
            gridBuildReport.Items.Clear();

            // build notifyicon (using windows.forms)
            notifyIcon = new System.Windows.Forms.NotifyIcon();
            notifyIcon.Icon = new Icon(Application.GetResourceStream(new Uri("pack://application:,,,/Images/icon.ico")).Stream);
            notifyIcon.MouseClick += new System.Windows.Forms.MouseEventHandler(NotifyIcon_MouseClick);

            // get original colors
            foreach (DictionaryEntry item in Application.Current.Resources.MergedDictionaries[0])
            {
                origResourceColors[item.Key.ToString()] = (SolidColorBrush)item.Value;
            }

            ApplyTheme(txtCustomThemeFile.Text);

            // for autostart with minimized
            if (Properties.Settings.Default.runAutomatically == true && Properties.Settings.Default.runAutomaticallyMinimized == true)
            {
                // if application got started by the system, then hide, otherwise dont hide (user started it)
                if (Directory.GetCurrentDirectory().ToLower() == @"c:\windows\system32")
                {
                    notifyIcon.Visible = true;
                    this.Hide();
                }

            }

            // TEST
            //themeEditorWindow = new ThemeEditor();
            //themeEditorWindow.Show();

            isInitializing = false;
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
                if (commandLineArgs == "-projectPath")
                {
                    Console.WriteLine("Launching from commandline ...");

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
                        if (Directory.Exists(Path.Combine(proj.Path, "Assets")) == true)
                        {
                            Tools.DisplayUpgradeDialog(proj, null);
                        }
                        else // no assets folder here, then its new project
                        {
                            //Tools.DisplayUpgradeDialog(proj, null);
                            CreateNewEmptyProject(proj.Path);
                        }
                    }
                    else
                    {
                        // try launching it
                        var proc = Tools.LaunchProject(proj);
                        //proj.Process = proc;
                        //ProcessHandler.Add(proj, proc);
                    }

                    // quit after launch if enabled in settings
                    if (Properties.Settings.Default.closeAfterExplorer == true)
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
        }

        // main search
        void FilterRecentProjects()
        {
            // https://www.wpftutorial.net/DataViews.html
            _filterString = txtSearchBox.Text;
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
            _filterString = txtSearchBoxUpdates.Text;
            ICollectionView collection = CollectionViewSource.GetDefaultView(dataGridUpdates.ItemsSource);
            collection.Filter = UpdatesFilter;
            if (dataGridUpdates.Items.Count > 0)
            {
                dataGridUpdates.SelectedIndex = 0;
            }
        }

        void FilterUnitys()
        {
            _filterString = txtSearchBoxUnity.Text;
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
            return (proj.Title.IndexOf(_filterString, 0, StringComparison.CurrentCultureIgnoreCase) != -1) || (searchProjectPathAlso && (proj.Path.IndexOf(_filterString, 0, StringComparison.CurrentCultureIgnoreCase) != -1));
        }

        private bool UpdatesFilter(object item)
        {
            Updates unity = item as Updates;
            return (unity.Version.IndexOf(_filterString, 0, StringComparison.CurrentCultureIgnoreCase) != -1);
        }

        private bool UnitysFilter(object item)
        {
            UnityInstallation unity = item as UnityInstallation;
            return (unity.Version.IndexOf(_filterString, 0, StringComparison.CurrentCultureIgnoreCase) != -1);
        }

        private bool BuildReportFilter(object item)
        {
            BuildReportItem reportItem = item as BuildReportItem;
            return (reportItem.Path.IndexOf(_filterString, 0, StringComparison.CurrentCultureIgnoreCase) != -1);
        }

        void LoadSettings()
        {
            // form size
            this.Width = Properties.Settings.Default.windowWidth;
            this.Height = Properties.Settings.Default.windowHeight;

            chkMinimizeToTaskbar.IsChecked = Properties.Settings.Default.minimizeToTaskbar;
            chkRegisterExplorerMenu.IsChecked = Properties.Settings.Default.registerExplorerMenu;

            // update settings window
            chkQuitAfterCommandline.IsChecked = Properties.Settings.Default.closeAfterExplorer;
            chkQuitAfterOpen.IsChecked = Properties.Settings.Default.closeAfterProject;
            chkShowLauncherArgumentsColumn.IsChecked = Properties.Settings.Default.showArgumentsColumn;
            chkShowGitBranchColumn.IsChecked = Properties.Settings.Default.showGitBranchColumn;
            chkShowMissingFolderProjects.IsChecked = Properties.Settings.Default.showProjectsMissingFolder;
            chkAllowSingleInstanceOnly.IsChecked = Properties.Settings.Default.AllowSingleInstanceOnly;
            chkAskNameForQuickProject.IsChecked = Properties.Settings.Default.askNameForQuickProject;
            chkEnableProjectRename.IsChecked = Properties.Settings.Default.enableProjectRename;
            chkStreamerMode.IsChecked = Properties.Settings.Default.streamerMode;
            chkShowPlatform.IsChecked = Properties.Settings.Default.showTargetPlatform;
            chkUseCustomTheme.IsChecked = Properties.Settings.Default.useCustomTheme;
            txtRootFolderForNewProjects.Text = Properties.Settings.Default.newProjectsRoot;
            txtWebglRelativePath.Text = Properties.Settings.Default.webglBuildPath;
            txtCustomThemeFile.Text = Properties.Settings.Default.themeFile;

            chkEnablePlatformSelection.IsChecked = Properties.Settings.Default.enablePlatformSelection;
            chkRunAutomatically.IsChecked = Properties.Settings.Default.runAutomatically;
            chkRunAutomaticallyMinimized.IsChecked = Properties.Settings.Default.runAutomaticallyMinimized;

            // update optional grid columns, hidden or visible
            gridRecent.Columns[4].Visibility = (bool)chkShowLauncherArgumentsColumn.IsChecked ? Visibility.Visible : Visibility.Collapsed;
            gridRecent.Columns[5].Visibility = (bool)chkShowGitBranchColumn.IsChecked ? Visibility.Visible : Visibility.Collapsed;
            gridRecent.Columns[6].Visibility = (bool)chkShowPlatform.IsChecked ? Visibility.Visible : Visibility.Collapsed;

            // update installations folder listbox
            lstRootFolders.Items.Clear();
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
            preferredVersion = Properties.Settings.Default.preferredVersion;

            // get last modified date format
            chkUseCustomLastModified.IsChecked = Properties.Settings.Default.useCustomLastModified;
            txtCustomDateTimeFormat.Text = Properties.Settings.Default.customDateFormat;

            if (Properties.Settings.Default.useCustomLastModified)
            {
                currentDateFormat = Properties.Settings.Default.customDateFormat;
            }
            else // use default
            {
                currentDateFormat = defaultDateFormat;
            }

            chkHumanFriendlyDateTime.IsChecked = Properties.Settings.Default.useHumandFriendlyLastModified;
            // if both enabled, then disable custom
            if (chkHumanFriendlyDateTime.IsChecked == true && chkUseCustomLastModified.IsChecked == true)
            {
                chkUseCustomLastModified.IsChecked = false;
            }

            useHumanFriendlyDateFormat = Properties.Settings.Default.useHumandFriendlyLastModified;
            searchProjectPathAlso = Properties.Settings.Default.searchProjectPathAlso;
            chkSearchProjectPath.IsChecked = searchProjectPathAlso;

            // recent grid column display index order
            var order = Properties.Settings.Default.recentColumnsOrder;

            // if we dont have any values, get & set them now
            // also, if user has disabled optional columns, saved order must be reset to default
            if (order == null || gridRecent.Columns.Count != Properties.Settings.Default.recentColumnsOrder.Length)
            {
                Properties.Settings.Default.recentColumnsOrder = new Int32[gridRecent.Columns.Count];
                for (int i = 0; i < gridRecent.Columns.Count; i++)
                {
                    Properties.Settings.Default.recentColumnsOrder[i] = gridRecent.Columns[i].DisplayIndex;
                }
                Properties.Settings.Default.Save();
            }
            else // load existing order
            {
                for (int i = 0; i < gridRecent.Columns.Count; i++)
                {
                    if (Properties.Settings.Default.recentColumnsOrder[i] > -1)
                    {
                        gridRecent.Columns[i].DisplayIndex = Properties.Settings.Default.recentColumnsOrder[i];
                    }
                }
            }

            adbLogCatArgs = Properties.Settings.Default.adbLogCatArgs;
            txtLogCatArgs.Text = adbLogCatArgs;

            projectNameSetting = Properties.Settings.Default.projectName;
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
                Properties.Settings.Default.shortcutBatchFileFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appName);
                txtShortcutBatchFileFolder.Text = Properties.Settings.Default.shortcutBatchFileFolder;
            }

            chkUseInitScript.IsChecked = Properties.Settings.Default.useInitScript;
            txtCustomInitFile.Text = Properties.Settings.Default.customInitFile;

        } // LoadSettings()


        private void SaveSettingsOnExit()
        {
            // save recent project column widths
            List<int> gridWidths;

            // if we dont have any settings yet
            if (Properties.Settings.Default.gridColumnWidths != null)
            {
                gridWidths = new List<int>(Properties.Settings.Default.gridColumnWidths);
            }
            else
            {
                gridWidths = new List<int>();
            }

            // get data grid view widths
            var column = gridRecent.Columns[0];
            for (int i = 0; i < gridRecent.Columns.Count; ++i)
            {
                if (Properties.Settings.Default.gridColumnWidths != null && Properties.Settings.Default.gridColumnWidths.Length > i)
                {
                    gridWidths[i] = (int)gridRecent.Columns[i].Width.Value;
                }
                else
                {
                    gridWidths.Add((int)gridRecent.Columns[i].Width.Value);
                }
            }
            Properties.Settings.Default.gridColumnWidths = gridWidths.ToArray();
            Properties.Settings.Default.Save();


            // save buildrepot column widths
            gridWidths.Clear();

            // if we dont have any settings yet
            if (Properties.Settings.Default.gridColumnWidthsBuildReport != null)
            {
                gridWidths = new List<int>(Properties.Settings.Default.gridColumnWidthsBuildReport);
            }
            else
            {
                gridWidths = new List<int>();
            }

            // get data grid view widths
            column = gridBuildReport.Columns[0];
            for (int i = 0; i < gridBuildReport.Columns.Count; ++i)
            {
                if (Properties.Settings.Default.gridColumnWidthsBuildReport != null && Properties.Settings.Default.gridColumnWidthsBuildReport.Length > i)
                {
                    gridWidths[i] = (int)gridBuildReport.Columns[i].Width.Value;
                }
                else
                {
                    gridWidths.Add((int)gridBuildReport.Columns[i].Width.Value);
                }
            }
            Properties.Settings.Default.gridColumnWidthsBuildReport = gridWidths.ToArray();

            Properties.Settings.Default.projectName = projectNameSetting;

            Properties.Settings.Default.Save();
        }

        void UpdateUnityInstallationsList()
        {
            // reset preferred string, if user changed it
            //preferredVersion = "none";

            unityInstallationsSource = GetUnityInstallations.Scan();
            dataGridUnitys.ItemsSource = unityInstallationsSource;

            // also make dictionary of installed unitys, to search faster
            unityInstalledVersions.Clear();

            for (int i = 0, len = unityInstallationsSource.Length; i < len; i++)
            {
                var version = unityInstallationsSource[i].Version;
                // NOTE cannot have same version id in 2 places with this
                if (string.IsNullOrEmpty(version) == false && unityInstalledVersions.ContainsKey(version) == false)
                {
                    unityInstalledVersions.Add(version, unityInstallationsSource[i].Path);
                }
            }

            lblFoundXInstallations.Content = "Found " + unityInstallationsSource.Length + " installations";
        }

        Project GetSelectedProject()
        {
            return (Project)gridRecent.SelectedItem;
        }

        int GetSelectedProjectIndex()
        {
            return gridRecent.SelectedIndex;
        }

        UnityInstallation GetSelectedUnity()
        {
            return (UnityInstallation)dataGridUnitys.SelectedItem;
        }

        Updates GetSelectedUpdate()
        {
            return (Updates)dataGridUpdates.SelectedItem;
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
            if (String.IsNullOrWhiteSpace(newRoot) == false && Directory.Exists(newRoot) == true)
            {
                Properties.Settings.Default.rootFolders.Add(newRoot);
                lstRootFolders.Items.Refresh();
                Properties.Settings.Default.Save();
                UpdateUnityInstallationsList();
            }
        }

        // waits for unity update results and assigns to datagrid
        async Task CallGetUnityUpdates()
        {
            dataGridUpdates.ItemsSource = null;
            var task = GetUnityUpdates.Scan();
            var items = await task;
            //Console.WriteLine("CallGetUnityUpdates=" + items == null);
            if (items == null) return;
            updatesSource = GetUnityUpdates.Parse(items);
            if (updatesSource == null) return;
            dataGridUpdates.ItemsSource = updatesSource;
        }

        async void GoLookForUpdatesForThisUnity()
        {
            // call for updates list fetch
            await CallGetUnityUpdates();

            var unity = GetSelectedUnity();
            if (unity == null) return;


            // NOTE if updates are not loaded, should wait for that
            if (dataGridUpdates.ItemsSource != null)
            {
                tabControl.SelectedIndex = 2;

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
            projectsSource = GetProjects.Scan(getGitBranch: (bool)chkShowGitBranchColumn.IsChecked, getPlasticBranch: (bool)chkCheckPlasticBranch.IsChecked, getArguments: (bool)chkShowLauncherArgumentsColumn.IsChecked, showMissingFolders: (bool)chkShowMissingFolderProjects.IsChecked, showTargetPlatform: (bool)chkShowPlatform.IsChecked);
            gridRecent.ItemsSource = projectsSource;
            // focus back
            Tools.SetFocusToGrid(gridRecent, lastSelectedProjectIndex);
        }

        //
        //
        // EVENTS
        //
        //

        // maximize window
        void NotifyIcon_MouseClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            notifyIcon.Visible = false;
            // NOTE workaround for grid not focused when coming back from minimized window
            Tools.SetFocusToGrid(gridRecent, GetSelectedProjectIndex());
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
                AddNewProjectToList(proj);
                // clear search, so can see added project
                txtSearchBox.Text = "";
            }
        }

        Project GetNewProjectData(string folder)
        {
            var p = new Project();
            p.Path = folder;
            p.Title = Path.GetFileName(folder);
            p.Version = Tools.GetProjectVersion(folder);
            p.Arguments = Tools.ReadCustomProjectData(folder, MainWindow.launcherArgumentsFile);
            if ((bool)chkShowPlatform.IsChecked == true) p.TargetPlatform = Tools.GetTargetPlatform(folder);
            if ((bool)chkShowGitBranchColumn.IsChecked == true) p.GITBranch = Tools.ReadGitBranchInfo(folder);
            return p;
        }

        void AddNewProjectToList(Project proj)
        {
            projectsSource.Insert(0, proj);
            gridRecent.Items.Refresh();
            Tools.SetFocusToGrid(gridRecent); // force focus
            gridRecent.SelectedIndex = 0;
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
                        default: // any key
                            // cancel if editing cell
                            IEditableCollectionView itemsView = gridRecent.Items;
                            if (itemsView.IsAddingNew || itemsView.IsEditingItem) return;

                            // skip these keys
                            if (Keyboard.Modifiers == ModifierKeys.Alt) return;

                            // activate searchbar if not active and we are in tab#1
                            if (txtSearchBox.IsFocused == false)
                            {
                                txtSearchBox.Focus();
                                txtSearchBox.Select(txtSearchBox.Text.Length, 0);
                            }
                            break;
                    }

                    break;
                case 1: // Unitys

                    switch (e.Key)
                    {
                        case Key.F5: // refresh unitys
                            UpdateUnityInstallationsList();
                            break;
                        case Key.Escape: // clear project search
                            txtSearchBoxUnity.Text = "";
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

        private async void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // if going into updates tab, fetch list (first time only)
            if (((TabControl)sender).SelectedIndex == (int)Tabs.Updates)
            {
                // if we dont have previous results yet, TODO scan again if previous was 24hrs ago
                if (updatesSource == null)
                {
                    var task = GetUnityUpdates.Scan();
                    var items = await task;
                    if (task.IsCompleted == false || task.IsFaulted == true) return;
                    if (items == null) return;
                    updatesSource = GetUnityUpdates.Parse(items);
                    if (updatesSource == null) return;
                    dataGridUpdates.ItemsSource = updatesSource;
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
        }

        private void CloseThemeEditor()
        {
            if (themeEditorWindow != null) themeEditorWindow.Close();
        }


        // save window size after resize
        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var win = (Window)sender;
            Properties.Settings.Default.windowWidth = (int)win.Width;
            Properties.Settings.Default.windowHeight = (int)win.Height;
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
            Tools.ExploreFolder(proj.Path);
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
        private void MenuItemCopyUpdateDownloadURL_Click(object sender, RoutedEventArgs e)
        {
            string copy = null;
            var unity = GetSelectedUpdate();
            copy = unity?.Version; //https://unity3d.com/get-unity/download?thank-you=update&download_nid=65083&os=Win
            string exeURL = Tools.ParseDownloadURLFromWebpage(copy);
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

            Tools.DisplayUpgradeDialog(proj, this);
        }

        private void GridRecent_Loaded(object sender, RoutedEventArgs e)
        {
            //Console.WriteLine("GridRecent_Loaded");
            Tools.SetFocusToGrid(gridRecent);
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

        private void BtnDonwloadInBrowser_Click(object sender, RoutedEventArgs e)
        {
            var unity = GetSelectedUpdate();
            string url = Tools.GetUnityReleaseURL(unity?.Version);
            if (string.IsNullOrEmpty(url) == false)
            {
                Tools.DownloadInBrowser(url, unity.Version);
            }
            else
            {
                Console.WriteLine("Failed getting Unity Installer URL for " + unity?.Version);
                SetStatus("Failed getting Unity Installer URL for " + unity?.Version);
            }
        }

        private void BtnOpenWebsite_Click(object sender, RoutedEventArgs e)
        {
            var unity = GetSelectedUpdate();
            Tools.OpenReleaseNotes(unity?.Version);
        }

        private void ChkMinimizeToTaskbar_CheckedChanged(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.minimizeToTaskbar = (bool)chkMinimizeToTaskbar.IsChecked;
            Properties.Settings.Default.Save();
        }

        private void ChkRegisterExplorerMenu_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if ((bool)chkRegisterExplorerMenu.IsChecked)
            {
                Tools.AddContextMenuRegistry(contextRegRoot);
            }
            else // remove
            {
                Tools.RemoveContextMenuRegistry(contextRegRoot);
            }

            Properties.Settings.Default.registerExplorerMenu = (bool)chkRegisterExplorerMenu.IsChecked;
            Properties.Settings.Default.Save();
        }

        private void ChkShowLauncherArgumentsColumn_CheckedChanged(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.showArgumentsColumn = (bool)chkShowLauncherArgumentsColumn.IsChecked;
            Properties.Settings.Default.Save();
            gridRecent.Columns[4].Visibility = (bool)chkShowLauncherArgumentsColumn.IsChecked ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ChkShowGitBranchColumn_CheckedChanged(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.showGitBranchColumn = (bool)chkShowGitBranchColumn.IsChecked;
            Properties.Settings.Default.Save();
            gridRecent.Columns[5].Visibility = (bool)chkShowGitBranchColumn.IsChecked ? Visibility.Visible : Visibility.Collapsed;
        }


        private void ChkQuitAfterOpen_CheckedChanged(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.closeAfterProject = (bool)chkQuitAfterOpen.IsChecked;
            Properties.Settings.Default.Save();
        }

        private void ChkQuitAfterCommandline_CheckedChanged(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.closeAfterExplorer = (bool)chkQuitAfterCommandline.IsChecked;
            Properties.Settings.Default.Save();
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
            Properties.Settings.Default.showProjectsMissingFolder = (bool)chkShowMissingFolderProjects.IsChecked;
            Properties.Settings.Default.Save();
        }

        private void ChkAllowSingleInstanceOnly_CheckedChanged(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.AllowSingleInstanceOnly = (bool)chkAllowSingleInstanceOnly.IsChecked;
            Properties.Settings.Default.Save();
        }

        private void BtnAssetPackages_Click(object sender, RoutedEventArgs e)
        {
            Tools.OpenAppdataSpecialFolder("../Roaming/Unity/Asset Store-5.x");
        }

        // sets selected unity version as preferred main unity version (to be preselected in case of unknown version projects, when creating new empty project, etc)
        private void MenuItemSetPreferredUnityVersion_Click(object sender, RoutedEventArgs e)
        {
            var ver = GetSelectedUnity().Version;
            Properties.Settings.Default.preferredVersion = ver;
            Properties.Settings.Default.Save();

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
            Properties.Settings.Default.newProjectsRoot = txtRootFolderForNewProjects.Text;
            Properties.Settings.Default.Save();
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
                    return;
                }
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
                    Console.WriteLine("Missing selected unity version");
                    SetStatus("Missing selected unity version (its null)");
                    return;
                }

                var suggestedName = targetFolder != null ? Path.GetFileName(targetFolder) : Tools.GetSuggestedProjectName(newVersion, rootFolder);
                NewProject modalWindow = new NewProject(newVersion, suggestedName, rootFolder, targetFolder != null);
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
                    Console.WriteLine("Create project " + NewProject.newVersion + " : " + rootFolder);
                    if (string.IsNullOrEmpty(rootFolder)) return;

                    var p = Tools.FastCreateProject(NewProject.newVersion, rootFolder, NewProject.newProjectName, NewProject.templateZipPath, NewProject.platformsForThisUnity, NewProject.selectedPlatform, (bool)chkUseInitScript.IsChecked, txtCustomInitFile.Text);

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
                // TODO custom init file here also
                var p = Tools.FastCreateProject(newVersion, rootFolder);
                if (p != null) AddNewProjectToList(p);
            }

        }

        private void ChkAskNameForQuickProject_Checked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.askNameForQuickProject = (bool)chkAskNameForQuickProject.IsChecked;
            Properties.Settings.Default.Save();
        }

        bool isInitializing = true; // used to avoid doing things while still starting up
        private void ChkStreamerMode_Checked(object sender, RoutedEventArgs e)
        {
            // TODO could add "(streamer mode)" text in statusbar?

            var isChecked = (bool)((CheckBox)sender).IsChecked;

            Properties.Settings.Default.streamerMode = isChecked;
            Properties.Settings.Default.Save();

            // Create cellstyle and assign if streamermode is enabled
            Style cellStyle = new Style(typeof(DataGridCell));
            cellStyle.Setters.Add(new Setter(FontSizeProperty, 1.0));
            txtColumnTitle.CellStyle = isChecked ? cellStyle : null;
            txtColumnName.CellStyle = isChecked ? cellStyle : null;

            Style txtBoxStyle = new Style(typeof(TextBox));
            txtBoxStyle.Setters.Add(new Setter(FontSizeProperty, 1.0));
            txtShortcutBatchFileFolder.Style = isChecked ? txtBoxStyle : null;
            txtRootFolderForNewProjects.Style = isChecked ? txtBoxStyle : null;

            // need to reload list if user changed setting
            if (isInitializing == false)
            {
                RefreshRecentProjects();
            }
        }

        private void ChkShowPlatform_Checked(object sender, RoutedEventArgs e)
        {
            var isChecked = (bool)((CheckBox)sender).IsChecked;

            Properties.Settings.Default.showTargetPlatform = isChecked;
            Properties.Settings.Default.Save();

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

        bool isDirtyCell = false;
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
            Properties.Settings.Default.enableProjectRename = (bool)chkEnableProjectRename.IsChecked;
            Properties.Settings.Default.Save();
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
                using (FileStream fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    using (StreamReader sr = new StreamReader(fs))
                    {
                        bool collectRows = false; // actual log rows
                        bool collectStats = false; // category stat rows
                        bool collectedBuildTime = false;

                        bool gotProjectPath = false;

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
                            if (line == "-projectPath") gotProjectPath = true;


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

                                var ms = line.Substring(line.IndexOf("(") + 1, line.IndexOf(")") - line.IndexOf("(") - 1).Trim().Replace(" ms", "");
                                singleReport.ElapsedTimeMS = long.Parse(ms);
                                collectedBuildTime = true;

                                // get streamingassets folder size and add to report, NOTE need to recalculate sizes then?
                                long streamingAssetFolderSize = Tools.GetFolderSizeInBytes(Path.Combine(currentBuildReportProjectPath, "Assets", "StreamingAssets"));
                                singleReport.Stats.Insert(singleReport.Stats.Count - 1, new BuildReportItem() { Category = "StreamingAssets", Size = Tools.GetBytesReadable(streamingAssetFolderSize) });

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
                                var line2 = line.Trim();
                                // get 2xspace after category name
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
                        }
                    }
                }
            }
            catch (Exception)
            {
                gridBuildReport.ItemsSource = null;
                gridBuildReport.Items.Clear();

                gridBuildReportData.ItemsSource = null;
                gridBuildReportData.Items.Clear();

                txtBuildTime.Text = "";

                Console.WriteLine("Failed to open editor log or other error in parsing: " + logFile);
                SetStatus("Failed to open editor log or other error in parsing: " + logFile);
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
            Properties.Settings.Default.webglBuildPath = txtWebglRelativePath.Text;
            Properties.Settings.Default.Save();
        }

        private void MenuItemBrowsePersistentDataPath_Click(object sender, RoutedEventArgs e)
        {
            var proj = GetSelectedProject();
            var projPath = proj?.Path.Replace('/', '\\');
            if (string.IsNullOrEmpty(projPath) == true) return;

            var psPath = Path.Combine(projPath, "ProjectSettings", "ProjectSettings.asset");
            if (File.Exists(psPath) == false) return;
            // read project settings
            var rows = File.ReadAllLines(psPath);

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

        void ApplyTheme(string themeFile)
        {
            if (chkUseCustomTheme.IsChecked == false) return;

            //Console.WriteLine("Load theme: " + themefile);

            themeFile = "Themes/" + themeFile;

            if (File.Exists(themeFile) == true)
            {
                var colors = File.ReadAllLines(themeFile);

                // parse lines
                for (int i = 0, length = colors.Length; i < length; i++)
                {
                    // skip comments
                    if (colors[i].IndexOf('#') == 0) continue;
                    // split row (name and color)
                    var row = colors[i].Split('=');
                    // skip bad rows
                    if (row.Length != 2) continue;

                    // parse color
                    try
                    {
                        //Console.WriteLine(row[0] +"="+ row[1].Trim());
                        var col = new BrushConverter().ConvertFrom(row[1].Trim());
                        // apply color
                        Application.Current.Resources[row[0]] = (SolidColorBrush)col;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        SetStatus("Failed to parse color value: " + row[1]);
                    }

                }
            }
            else
            {
                Console.WriteLine("Theme file not found: " + themeFile);
                SetStatus("Theme file not found: " + themeFile);
            }
        }

        void ResetTheme()
        {
            foreach (KeyValuePair<string, SolidColorBrush> item in origResourceColors)
            {
                Application.Current.Resources[item.Key] = item.Value;
            }
        }

        private void ChkUseCustomTheme_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            var isChecked = (bool)((CheckBox)sender).IsChecked;
            Properties.Settings.Default.useCustomTheme = isChecked;
            Properties.Settings.Default.Save();

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
            Properties.Settings.Default.themeFile = s.Text;
            Properties.Settings.Default.Save();
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
            Tools.LaunchExplorer(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Themes"));
        }

        private void ChkEnablePlatformSelection_Checked(object sender, RoutedEventArgs e)
        {
            var isChecked = (bool)((CheckBox)sender).IsChecked;
            Properties.Settings.Default.enablePlatformSelection = isChecked;
            Properties.Settings.Default.Save();
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
                if (p != null) p.TargetPlatform = cmb.SelectedValue.ToString();
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
            Properties.Settings.Default.runAutomatically = isChecked;
            Properties.Settings.Default.Save();
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
                Properties.Settings.Default.shortcutBatchFileFolder = textBox.Text;
                Properties.Settings.Default.Save();
                textBox.BorderBrush = System.Windows.Media.Brushes.Transparent;
            }
            else // invalid format
            {
                textBox.BorderBrush = System.Windows.Media.Brushes.Red;
                //Properties.Settings.Default.shortcutBatchFileFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appName);
                //Properties.Settings.Default.Save();
            }
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

            Properties.Settings.Default.runAutomaticallyMinimized = isChecked;
            Properties.Settings.Default.Save();
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

            // FIXME nobody likes extra loops.. but only 40 items to find correct project? but still..
            for (int i = 0, len = projectsSource.Count; i < len; i++)
            {
                if (projectsSource[i].Path == proj.Path)
                {
                    var tempProj = projectsSource[i];
                    tempProj.Modified = Tools.GetLastModifiedTime(proj.Path);
                    tempProj.Version = Tools.GetProjectVersion(proj.Path);
                    tempProj.GITBranch = Tools.ReadGitBranchInfo(proj.Path);
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
            string exeURL = Tools.ParseDownloadURLFromWebpage(unity?.Version);
            Tools.DownloadInBrowser(exeURL, unity?.Version);
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
            var proj = GetSelectedProject();
            if (proj == null) return;
            if (GetProjects.RemoveRecentProject(proj.Path))
            {
                RefreshRecentProjects();
            }
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
            Properties.Settings.Default.searchProjectPathAlso = isChecked;
            Properties.Settings.Default.Save();
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

        private void ChkCheckPlasticBranch_Checked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.checkPlasticBranch = (bool)chkCheckPlasticBranch.IsChecked;
            Properties.Settings.Default.Save();
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
            var folder = ((TextBox)sender).Text;
            if (Directory.Exists(folder))
            {
                Properties.Settings.Default.shortcutBatchFileFolder = folder;
                Properties.Settings.Default.Save();
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

        private void gridRecent_Sorting(object sender, DataGridSortingEventArgs e)
        {
            SortHandler(sender, e);
        }

        // https://stackoverflow.com/a/2130557/5452781
        void SortHandler(object sender, DataGridSortingEventArgs e)
        {
            DataGridColumn column = e.Column;

            //Console.WriteLine("Sorted by " + column.Header);

            IComparer comparer = null;

            // prevent the built-in sort from sorting
            e.Handled = true;

            ListSortDirection direction = (column.SortDirection != ListSortDirection.Ascending) ? ListSortDirection.Ascending : ListSortDirection.Descending;

            //set the sort order on the column
            column.SortDirection = direction;

            //use a ListCollectionView to do the sort.
            ListCollectionView lcv = (ListCollectionView)CollectionViewSource.GetDefaultView(gridRecent.ItemsSource);

            comparer = new CustomProjectSort(direction, column.Header.ToString());

            //apply the sort
            lcv.CustomSort = comparer;
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
                        return direction == ListSortDirection.Ascending ? Tools.VersionAsInt(((Project)a).Version).CompareTo(Tools.VersionAsInt(((Project)b).Version)) : Tools.VersionAsInt(((Project)b).Version).CompareTo(Tools.VersionAsInt(((Project)a).Version));
                    case "Path":
                        return direction == ListSortDirection.Ascending ? ((Project)a).Path.CompareTo(((Project)b).Path) : ((Project)b).Path.CompareTo(((Project)a).Path);
                    case "Modified":
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
                    default:
                        return 0;
                }
            }
        }

        private void btnExploreScriptsFolder_Click(object sender, RoutedEventArgs e)
        {
            // TODO later this script should be inside some unity project, for easier updating..
            Tools.LaunchExplorer(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts"));
        }

        private void txtCustomInitFile_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Return: // pressed enter in theme file text box
                    Properties.Settings.Default.customInitFile = txtCustomInitFile.Text;
                    Properties.Settings.Default.Save();
                    break;
            }
        }

        private void txtCustomInitFile_LostFocus(object sender, RoutedEventArgs e)
        {
            var s = (TextBox)sender;
            Properties.Settings.Default.customInitFile = s.Text;
            Properties.Settings.Default.Save();
        }

        private void chkUseInitScript_Checked(object sender, RoutedEventArgs e)
        {
            if (this.IsActive == false) return; // dont run code on window init

            var isChecked = (bool)((CheckBox)sender).IsChecked;
            Properties.Settings.Default.useInitScript = isChecked;
            Properties.Settings.Default.Save();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            gridSettingsBg.Focus();
        }

        void SetStatus(string msg)
        {
            txtStatus.Text = msg;
        }

        private void btnPatchHubConfig_Click(object sender, RoutedEventArgs e)
        {
            // read the config file from %APPDATA%
            var configFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UnityHub", "editors.json");

            var result = MessageBox.Show("This will modify current " + configFile + " file. Are you sure you want to continue? (This cannot be undone, we dont know which 'manual:'-value was already set to 'false' (but it shouldnt break anything)", "Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                if (File.Exists(configFile) == true)
                {
                    // read the config file
                    var json = File.ReadAllText(configFile);
                    // replace the manual:true with manual:false using regex
                    json = json.Replace("\"manual\":true", "\"manual\":false");

                    Console.WriteLine(json);

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
            var cmd = "cmd.exe";// /C adb install -r \"{playerPath}\"";
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

            if (string.IsNullOrEmpty(packageName) == false)
            {
                pars += $" && adb shell monkey -p {packageName} 1";
            }

            // TODO start cmd minimized
            Tools.LaunchExe(cmd, pars);
            // get apk name from path
            var apkName = Path.GetFileName(playerPath);
            if (chkStreamerMode.IsChecked == true) apkName = " (hidden in streamermode)";
            SetStatus("Installed APK:" + apkName);
        }

        //private void BtnBrowseTemplateUnityPackagesFolder_Click(object sender, RoutedEventArgs e)
        //{
        //    var folder = Tools.BrowseForOutputFolder("Select unitypackage Templates folder");
        //    if (string.IsNullOrEmpty(folder) == false)
        //    {
        //        txtTemplatePackagesFolder.Text = folder;
        //        Properties.Settings.Default.templatePackagesFolder = folder;
        //        Properties.Settings.Default.Save();
        //    }
        //}

        //private void TxtTemplatePackagesFolder_TextChanged(object sender, TextChangedEventArgs e)
        //{
        //    Properties.Settings.Default.templatePackagesFolder = txtTemplatePackagesFolder.Text;
        //    Properties.Settings.Default.Save();
        //}

    } // class
} //namespace

