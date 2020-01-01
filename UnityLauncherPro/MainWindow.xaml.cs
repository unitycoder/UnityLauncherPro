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

        Project[] projectsSource;
        Updates[] updatesSource;
        UnityInstallation[] unityInstallationsSource;
        public static Dictionary<string, string> unityInstalledVersions = new Dictionary<string, string>();
        const string contextRegRoot = "Software\\Classes\\Directory\\Background\\shell";
        public static readonly string launcherArgumentsFile = "LauncherArguments.txt";
        string _filterString = null;
        const string githubURL = "https://github.com/unitycoder/UnityLauncherPro";

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

            // updates grid
            dataGridUpdates.Items.Clear();

            // build notifyicon (using windows.forms)
            notifyIcon = new System.Windows.Forms.NotifyIcon();
            notifyIcon.Icon = new Icon(Application.GetResourceStream(new Uri("pack://application:,,,/Images/icon.ico")).Stream);
            notifyIcon.MouseClick += new System.Windows.Forms.MouseEventHandler(NotifyIcon_MouseClick);
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
                    if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                    {
                        Tools.DisplayUpgradeDialog(proj, null);
                    }
                    else
                    {
                        // try launching it
                        Tools.LaunchProject(proj);
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
            // set first row selected
            if (gridRecent.Items.Count > 0)
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
            unityInstallationsSource = GetUnityInstallations.Scan();
            dataGridUnitys.ItemsSource = unityInstallationsSource;

            // make dictionary of installed unitys, to search faster
            unityInstalledVersions.Clear();
            for (int i = 0, len = unityInstallationsSource.Length; i < len; i++)
            {
                var version = unityInstallationsSource[i].Version;
                if (unityInstalledVersions.ContainsKey(version) == false)
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

        void SetFocusToGrid(DataGrid targetGrid)
        {
            //e.Handled = true; // if enabled, we enter to first row initially
            if (targetGrid.Items.Count < 1) return;
            targetGrid.Focus();
            targetGrid.SelectedIndex = 0;
            DataGridRow row = (DataGridRow)targetGrid.ItemContainerGenerator.ContainerFromIndex(0);
            row.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }

        async void CallGetUnityUpdates()
        {
            dataGridUpdates.ItemsSource = null;
            var task = GetUnityUpdates.Scan();
            var items = await task;
            if (items == null) return;
            updatesSource = GetUnityUpdates.Parse(items);
            if (updatesSource == null) return;
            dataGridUpdates.ItemsSource = updatesSource;
        }


        //
        //
        // EVENTS
        //
        //

        //private void OnSearchPreviewKeyDown(object sender, KeyEventArgs e)
        //{
        //    switch (e.Key)
        //    {
        //        case Key.Escape:
        //            if (((TextBox)sender).Text == "")
        //            {
        //                Console.WriteLine(1);
        //                Keyboard.Focus(gridRecent);
        //                e.Handled = true;
        //            }
        //            ((TextBox)sender).Text = "";
        //            break;
        //        default:
        //            break;
        //    }
        //}

        void NotifyIcon_MouseClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            notifyIcon.Visible = false;
        }

        // hide/show notifyicon based on window state
        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                this.ShowInTaskbar = false;
                notifyIcon.BalloonTipTitle = "Minimize Sucessful";
                notifyIcon.BalloonTipText = "Minimized the app ";
                notifyIcon.ShowBalloonTip(400);
                notifyIcon.Visible = true;
            }
            else if (this.WindowState == WindowState.Normal)
            {
                notifyIcon.Visible = false;
                this.ShowInTaskbar = true;
            }
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
            // https://stackoverflow.com/a/50261723/5452781
            // Create a "Save As" dialog for selecting a directory (HACK)
            var dialog = new Microsoft.Win32.SaveFileDialog();
            dialog.InitialDirectory = "c:"; // Use current value for initial dir
            dialog.Title = "Select Project Folder to Add it Into Projects List";
            dialog.Filter = "Project Folder|*.Folder"; // Prevents displaying files
            dialog.FileName = "Project"; // Filename will then be "select.this.directory"
            if (dialog.ShowDialog() == true)
            {
                string path = dialog.FileName;
                // Remove fake filename from resulting path
                path = path.Replace("\\Project.Folder", "");
                path = path.Replace("Project.Folder", "");
                // If user has changed the filename, create the new directory
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                // Our final value is in path
                Console.WriteLine(path);
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
                        case Key.F5: // refresh projects
                            projectsSource = GetProjects.Scan(getGitBranch: (bool)chkShowGitBranchColumn.IsChecked, getArguments: (bool)chkShowLauncherArgumentsColumn.IsChecked, showMissingFolders: (bool)chkShowMissingFolderProjects.IsChecked);
                            gridRecent.ItemsSource = projectsSource;
                            break;
                        case Key.Escape: // clear project search
                            if (txtSearchBox.Text == "")
                            {
                                SetFocusToGrid(gridRecent);
                            }
                            txtSearchBox.Text = "";
                            break;
                        case Key.F2: // edit arguments
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
                if (updatesSource == null)
                {
                    var task = GetUnityUpdates.Scan();
                    var items = await task;
                    // TODO handle errors?
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
            Tools.LaunchProject(GetSelectedProject());
        }

        private void BtnExplore_Click(object sender, RoutedEventArgs e)
        {
            var proj = GetSelectedProject();
            Tools.ExploreProjectFolder(proj);
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
            projectsSource = GetProjects.Scan(getGitBranch: (bool)chkShowGitBranchColumn.IsChecked, getArguments: (bool)chkShowLauncherArgumentsColumn.IsChecked, showMissingFolders: (bool)chkShowMissingFolderProjects.IsChecked);
            gridRecent.ItemsSource = projectsSource;
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
            SetFocusToGrid(gridRecent);
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

        // if press up/down in search box, move to first item in results
        private void TxtSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Return: // open selected project
                    var proj = GetSelectedProject();
                    if (proj != null) Tools.LaunchProject(proj);
                    break;
                case Key.Tab:
                case Key.Up:
                case Key.Down:
                    SetFocusToGrid(gridRecent);
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
                    SetFocusToGrid(dataGridUnitys);
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

        // need to manually move into next/prev rows? https://stackoverflow.com/a/11652175/5452781
        private void GridRecent_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Tab:
                    if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                    {
                        e.Handled = true;
                    }
                    break;
                case Key.Return:
                    // cancel if editing cell
                    IEditableCollectionView itemsView = gridRecent.Items;
                    if (itemsView.IsAddingNew || itemsView.IsEditingItem) return;
                    e.Handled = true;
                    var proj = GetSelectedProject();
                    Tools.LaunchProject(proj);
                    break;
                default:
                    break;
            }
        }

        private void DataGridUnitys_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                e.Handled = true;
                // TODO launchunity
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
                    SetFocusToGrid(dataGridUpdates);
                    break;
                default:
                    break;
            }
        }

        private void BtnOpenEditorLogsFolder_Click(object sender, RoutedEventArgs e)
        {
            var logfolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Unity", "Editor");
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
                Console.WriteLine("Failed getting Unity Installer URL for " + unity.Version);
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

        private void GridRecent_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            // get current row data
            var proj = GetSelectedProject();

            // check that folder exists
            string path = proj.Path;
            if (string.IsNullOrEmpty(path)) return;

            // get current arguments, after editing
            TextBox t = e.EditingElement as TextBox;
            string arguments = t.Text.ToString();

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
                sw.WriteLine(arguments);
                sw.Close();
            }
            catch (Exception ex)
            {
                //SetStatus("File error: " + exception.Message);
                Console.WriteLine(ex);
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
            Tools.LaunchProject(GetSelectedProject());
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
    } // class
} //namespace
