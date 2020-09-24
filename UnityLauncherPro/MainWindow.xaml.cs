// Unity Project Launcher by https://unitycoder.com
// Sources https://github.com/unitycoder/UnityLauncherPro

using System;
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
using System.Windows.Shell;

namespace UnityLauncherPro
{
    public partial class MainWindow : Window
    {
        private System.Windows.Forms.NotifyIcon notifyIcon;
        const string appName = "UnityLauncherPro";

        [DllImport("user32", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindow(string cls, string win);
        [DllImport("user32")]
        static extern IntPtr SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32")]
        static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32")]
        static extern bool OpenIcon(IntPtr hWnd);

        // datagrid sources
        List<Project> projectsSource;
        Updates[] updatesSource;
        UnityInstallation[] unityInstallationsSource;

        public static Dictionary<string, string> unityInstalledVersions = new Dictionary<string, string>();
        const string contextRegRoot = "Software\\Classes\\Directory\\Background\\shell";
        public static readonly string launcherArgumentsFile = "LauncherArguments.txt";
        string _filterString = null;
        const string githubURL = "https://github.com/unitycoder/UnityLauncherPro";
        int lastSelectedProjectIndex = 0;
        public static string preferredVersion = "none";
        Mutex myMutex;

        public MainWindow()
        {
            InitializeComponent();
            Start();
        }

        void Start()
        {
            LoadSettings();

            // disable accesskeys without alt
            CoreCompatibilityPreferences.IsAltKeyRequiredInAccessKeyDefaultScope = true;

            // make window resizable (this didnt work when used pure xaml to do this)
            WindowChrome Resizable_BorderLess_Chrome = new WindowChrome();
            Resizable_BorderLess_Chrome.CornerRadius = new CornerRadius(0);
            Resizable_BorderLess_Chrome.CaptionHeight = 1.0;
            WindowChrome.SetWindowChrome(this, Resizable_BorderLess_Chrome);

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
                    ActivateOtherWindow();
                    App.Current.Shutdown();
                }
            }

            // update projects list
            projectsSource = GetProjects.Scan(getGitBranch: (bool)chkShowGitBranchColumn.IsChecked, getArguments: (bool)chkShowLauncherArgumentsColumn.IsChecked, showMissingFolders: (bool)chkShowMissingFolderProjects.IsChecked);
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

            isInitializing = false;
        }

        // bring old window to front, but needs matching appname.. https://stackoverflow.com/a/36804161/5452781
        private static void ActivateOtherWindow()
        {
            var other = FindWindow(null, appName);
            if (other != IntPtr.Zero)
            {
                SetForegroundWindow(other);
                if (IsIconic(other))
                    OpenIcon(other);
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

                    // check if force-update button is down
                    // NOTE if keydown, window doesnt become active and focused
                    if (string.IsNullOrEmpty(version) || (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                    {
                        Tools.DisplayUpgradeDialog(proj, null);
                    }
                    else
                    {
                        // try launching it
                        var proc = Tools.LaunchProject(proj);
                        proj.Process = proc;
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

        private bool ProjectFilter(object item)
        {
            Project proj = item as Project;
            return (proj.Title.IndexOf(_filterString, 0, StringComparison.CurrentCultureIgnoreCase) != -1);
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
            txtRootFolderForNewProjects.Text = Properties.Settings.Default.newProjectsRoot;
            chkAskNameForQuickProject.IsChecked = Properties.Settings.Default.askNameForQuickProject;
            chkEnableProjectRename.IsChecked = Properties.Settings.Default.enableProjectRename;
            chkStreamerMode.IsChecked = Properties.Settings.Default.streamerMode;

            // update optional grid columns, hidden or visible
            gridRecent.Columns[4].Visibility = (bool)chkShowLauncherArgumentsColumn.IsChecked ? Visibility.Visible : Visibility.Collapsed;
            gridRecent.Columns[5].Visibility = (bool)chkShowGitBranchColumn.IsChecked ? Visibility.Visible : Visibility.Collapsed;

            // update installations folder listbox
            lstRootFolders.Items.Clear();
            lstRootFolders.ItemsSource = Properties.Settings.Default.rootFolders;

            // restore datagrid column widths
            int[] gridColumnWidths = Properties.Settings.Default.gridColumnWidths;
            if (gridColumnWidths != null)
            {
                for (int i = 0; i < gridColumnWidths.Length; ++i)
                {
                    gridRecent.Columns[i].Width = gridColumnWidths[i];
                }
            }

            // other setting vars
            preferredVersion = Properties.Settings.Default.preferredVersion;
        } // LoadSettings()

        private void SaveSettingsOnExit()
        {
            // save list column widths
            List<int> gridWidths;
            if (Properties.Settings.Default.gridColumnWidths != null)
            {
                gridWidths = new List<int>(Properties.Settings.Default.gridColumnWidths);
            }
            else
            {
                gridWidths = new List<int>();
            }

            // restore data grid view widths
            var colum = gridRecent.Columns[0];
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

        }

        void UpdateUnityInstallationsList()
        {
            // reset preferred string, if user changed it
            //preferredVersion = "none";

            unityInstallationsSource = GetUnityInstallations.Scan();
            dataGridUnitys.ItemsSource = unityInstallationsSource;

            // make dictionary of installed unitys, to search faster
            unityInstalledVersions.Clear();
            for (int i = 0, len = unityInstallationsSource.Length; i < len; i++)
            {
                var version = unityInstallationsSource[i].Version;
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
            Console.WriteLine(items == null);
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
            // NOTE for now, just select the same version.. then user can see what has been released after this
            // NOTE if updates are not loaded, should wait for that
            if (dataGridUpdates.ItemsSource != null)
            {
                tabControl.SelectedIndex = 2;
                // find matching version
                for (int i = 0; i < dataGridUpdates.Items.Count; i++)
                {
                    Updates row = (Updates)dataGridUpdates.Items[i];
                    if (row.Version == unity.Version)
                    {
                        dataGridUpdates.SelectedIndex = i;
                        dataGridUpdates.ScrollIntoView(row);
                        break;
                    }
                }
            }
        }

        void RefreshRecentProjects()
        {
            // clear search
            txtSearchBox.Text = "";
            // take currently selected project row
            lastSelectedProjectIndex = gridRecent.SelectedIndex;
            // rescan recent projects
            projectsSource = GetProjects.Scan(getGitBranch: (bool)chkShowGitBranchColumn.IsChecked, getArguments: (bool)chkShowLauncherArgumentsColumn.IsChecked, showMissingFolders: (bool)chkShowMissingFolderProjects.IsChecked);
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
        }

        private void BtnAddProjectFolder_Click(object sender, RoutedEventArgs e)
        {
            var folder = Tools.BrowseForOutputFolder("Select Project Folder to Add it Into Projects List");
            if (string.IsNullOrEmpty(folder) == false)
            {
                // create new project item
                var p = new Project();
                p.Path = folder;
                p.Title = Path.GetFileName(folder);
                p.Version = Tools.GetProjectVersion(folder);
                p.Arguments = Tools.ReadCustomLaunchArguments(folder, MainWindow.launcherArgumentsFile);
                // add to list
                projectsSource.Insert(0, p);
                gridRecent.Items.Refresh();
                Tools.SetFocusToGrid(gridRecent); // force focus
                gridRecent.SelectedIndex = 0;

            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
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
                            else // we have text in searchbox
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
                        case Key.Left:
                        case Key.Right:
                        case Key.Down:
                            break;
                        case Key.F2: // edit arguments or project name
                            if (chkEnableProjectRename.IsChecked == false) return; //if rename not enabled
                            // if in first cell (or no cell)
                            var cell = gridRecent.CurrentCell;
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
                            UpdateUnityInstallationsList(); break;
                        case Key.Escape: // clear project search
                            txtSearchBoxUnity.Text = "";
                            break;
                        default: // any key
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
                        default: // any key
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
            txtSearchBoxUpdates.Text = "";
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            SaveSettingsOnExit();
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
            var proc = Tools.LaunchProject(proj);
            proj.Process = proc;
            Tools.SetFocusToGrid(gridRecent);
        }

        private void BtnExplore_Click(object sender, RoutedEventArgs e)
        {
            var proj = GetSelectedProject();
            Tools.ExploreProjectFolder(proj);
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
                    proj.Process = proc;
                    break;
                case Key.Tab:
                case Key.Up:
                    Tools.SetFocusToGrid(gridRecent);
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
                    Tools.SetFocusToGrid(gridRecent);
                    //                    }
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
                    proj.Process = proc;
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
                }
            }
        }

        private void BtnOpenPlayerLogs_Click(object sender, RoutedEventArgs e)
        {
            var logfolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "/../LocalLow");
            if (Directory.Exists(logfolder) == true)
            {
                if (Tools.LaunchExplorer(logfolder) == false)
                {
                    Console.WriteLine("Error> Directory not found: " + logfolder);
                }
            }
        }

        private void BtnOpenADBLogCat_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process myProcess = new Process();
                var cmd = "cmd.exe";
                myProcess.StartInfo.FileName = cmd;
                // NOTE windows10 cmd line supports ansi colors, otherwise remove -v color
                var pars = " /c adb logcat -s Unity ActivityManager PackageManager dalvikvm DEBUG -v color";
                myProcess.StartInfo.Arguments = pars;
                myProcess.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
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

        // finished editing project name cell or launcher argument cell
        private void GridRecent_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            // avoid ending event running twice
            if (isDirtyCell == false) return;
            isDirtyCell = false;

            // get selected row data
            var proj = GetSelectedProject();

            // check that folder exists
            string path = proj.Path;
            if (string.IsNullOrEmpty(path)) return;

            // get current arguments, after editing
            TextBox t = e.EditingElement as TextBox;
            string newcellValue = t.Text.ToString();


            // check if we edited project name, or launcher arguments
            if (e.Column.DisplayIndex == 0)
            {
                // restore read only
                e.Column.IsReadOnly = true;

                if (string.IsNullOrEmpty(newcellValue))
                {
                    Console.WriteLine("Project name is null: " + newcellValue);
                    return;
                }

                // cannot allow / or \ or . as last character (otherwise might have issues going parent folder?)
                if (newcellValue.EndsWith("\\") || newcellValue.EndsWith("/") || newcellValue.EndsWith("."))
                {
                    Console.WriteLine("Project name cannot end with / or \\ or . ");
                    return;
                }

                // get new path
                var newPath = Path.Combine(Directory.GetParent(path).ToString(), newcellValue);

                // check if has invalid characters for full path
                if (newPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                {
                    Console.WriteLine("Invalid project path: " + newPath);
                    return;
                }

                // check if same as before (need to replace mismatch slashes)
                if (path.Replace('/', '\\') == newPath.Replace('/', '\\'))
                {
                    Console.WriteLine("Rename cancelled..");
                    return;
                }

                // check if new folder already exists
                if (Directory.Exists(newPath))
                {
                    Console.WriteLine("Directory already exists: " + newPath);
                    return;
                }

                // try rename project folder by moving directory to new name
                Directory.Move(path, newPath);

                // check if move was success
                if (Directory.Exists(newPath))
                {
                    // force ending edit (otherwise only ends on enter or esc)
                    gridRecent.CommitEdit(DataGridEditingUnit.Row, true);

                    // TODO save to registry (otherwise not listed in recent projects, unless opened)
                }
                else
                {
                    Console.WriteLine("Failed to rename directory..");
                }

            }
            else // edit launcher arguments
            {

                string projSettingsFolder = "ProjectSettings";

                // check if projectsettings folder exists, if not then add
                string outputFolder = Path.Combine(path, projSettingsFolder);
                if (Directory.Exists(outputFolder) == false)
                {
                    Directory.CreateDirectory(outputFolder);
                }

                // save arguments to projectsettings folder
                string outputFile = Path.Combine(path, projSettingsFolder, launcherArgumentsFile);

                try
                {
                    StreamWriter sw = new StreamWriter(outputFile);
                    sw.WriteLine(newcellValue);
                    sw.Close();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error saving launcher arguments: " + ex);
                }
            }

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
            // cancel if editing cell, because often try to double click to edit
            if (IsEditingCell(gridRecent)) return;

            // cancel run if double click arguments editable field
            var currentColumnCell = gridRecent.CurrentCell.Column.DisplayIndex;
            if (currentColumnCell == 4) return;

            var proj = GetSelectedProject();
            var proc = Tools.LaunchProject(proj);
            proj.Process = proc;
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
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Unity", "Asset Store-5.x");
            if (Directory.Exists(folder) == false) return;
            if (Tools.LaunchExplorer(folder) == false)
            {
                Console.WriteLine("Cannot open folder.." + folder);
            }
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

        private void Window_Activated(object sender, EventArgs e)
        {
            //Console.WriteLine("gridRecent.IsFocused=" + gridRecent.IsFocused);
            //Console.WriteLine("gridRecent.IsFocused=" + gridRecent.IsKeyboardFocused);
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
            // save to prefs when? onchange
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

        void CreateNewEmptyProject()
        {
            if (chkAskNameForQuickProject.IsChecked == true)
            {
                // ask name
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

                if (string.IsNullOrEmpty(newVersion))
                {
                    Console.WriteLine("Missing selected unity version");
                    return;
                }

                NewProject modalWindow = new NewProject(newVersion, Tools.GetSuggestedProjectName(newVersion, txtRootFolderForNewProjects.Text));
                modalWindow.ShowInTaskbar = this == null;
                modalWindow.WindowStartupLocation = this == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
                modalWindow.Topmost = this == null;
                modalWindow.ShowActivated = true;
                modalWindow.Owner = this;
                modalWindow.ShowDialog();
                var results = modalWindow.DialogResult.HasValue && modalWindow.DialogResult.Value;

                if (results == true)
                {
                    var projectPath = txtRootFolderForNewProjects.Text;
                    Console.WriteLine("create project " + projectPath);
                    if (string.IsNullOrEmpty(projectPath)) return;

                    Tools.FastCreateProject(newVersion, projectPath, NewProject.newProjectName);
                }
                else // false, cancel
                {
                    Console.WriteLine("Cancellled project creation..");
                }

            }
            else // use automatic name
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
                Tools.FastCreateProject(newVersion, txtRootFolderForNewProjects.Text);
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
            var isChecked = (bool)chkStreamerMode.IsChecked;

            Properties.Settings.Default.streamerMode = isChecked;
            Properties.Settings.Default.Save();

            // Create cellstyle and assign if enabled
            Style cellStyle = new Style(typeof(DataGridCell));
            cellStyle.Setters.Add(new Setter(FontSizeProperty, 1.0));
            txtColumnTitle.CellStyle = isChecked ? cellStyle : null;
            txtColumnName.CellStyle = isChecked ? cellStyle : null;

            // need to reload list if user clicked
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
            Console.WriteLine(path);

            if (string.IsNullOrEmpty(path) == false) Clipboard.SetText(path);
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
            var logfolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "Unity", "Editor", "Crashes");
            if (Directory.Exists(logfolder) == true)
            {
                if (Tools.LaunchExplorer(logfolder) == false)
                {
                    Console.WriteLine("Cannot open folder.." + logfolder);
                }
            }
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
            if (proj.Process != null)
            {
                try
                {
                    proj.Process.Kill();
                }
                catch (Exception)
                {
                }
                proj.Process = null;
            }
        }

        private void GridRecent_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (tabControl.SelectedIndex == 0)
            {
                var proj = GetSelectedProject();
                menuItemKillProcess.IsEnabled = proj.Process != null;
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
            // TODO keep previous build report (total size?) so can compare to current one

            var logFile = Path.Combine(Tools.GetEditorLogsFolder(), "Editor.log");
            //Console.WriteLine("read editor log: " + logFile);

            // TODO use streamreader to scan.. some log files are huge

            string[] rows;

            try
            {
                rows = File.ReadAllLines(logFile);
            }
            catch (Exception)
            {
                throw;
            }

            if (rows == null)
            {
                Console.WriteLine("Failed to open editor log: " + logFile);
                return;
            }

            // TODO parse project folder info also, so can browse to selected file

            int startRow = -1;
            int endRow = -1;
            // loop backwards to find latest report
            for (int i = rows.Length - 1; i >= 0; i--)
            {
                // find start of build report
                //if (rows[i].IndexOf("Build Report") == 0) // TODO take overview also
                if (rows[i].IndexOf("Used Assets and files from the Resources folder, sorted by uncompressed size:") == 0)
                {
                    startRow = i + 1;

                    // find end of report
                    for (int k = i; k < rows.Length; k++)
                    {
                        if (rows[k].IndexOf("-------------------------------------------------------------------------------") == 0)
                        {
                            endRow = k - 1;
                            break;
                        }
                    }
                    break;
                }
            }

            if (startRow == -1 || endRow == -1)
            {
                Console.WriteLine("Failed to parse Build Report, start= " + startRow + " end= " + endRow);
                return;
            }

            //Console.WriteLine("buildreport at " + startRow + " - " + endRow);

            var reportSource = new BuildReport[endRow - startRow];

            // get report rows
            int index = 0;
            for (int i = startRow; i < endRow; i++)
            {
                //Console.WriteLine(rows[i]);
                var d = rows[i].Trim();

                // get tab after kb
                var space1 = d.IndexOf('\t');
                // get % between % and path
                var space2 = d.IndexOf('%');

                if (space1 == -1 || space2 == -1)
                {
                    Console.WriteLine("Failed to parse build report row: " + d);
                    continue;
                }

                var r = new BuildReport();
                r.Size = d.Substring(0, space1);
                r.Percentage = d.Substring(space1 + 2, space2 - space1 - 1);
                r.Path = d.Substring(space2 + 2, d.Length - space2 - 2);
                reportSource[index++] = r;
            }
            gridBuildReport.ItemsSource = reportSource;

        }

        private void BtnClearBuildReport_Click(object sender, RoutedEventArgs e)
        {
            gridBuildReport.ItemsSource = null;
        }
    } // class
} //namespace




