using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing; // for notifyicon
using System.IO;
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

        Project[] projectsSource;
        Updates[] updatesSource;
        UnityInstallation[] unityInstallationsSource;
        public static Dictionary<string, string> unityInstalledVersions = new Dictionary<string, string>();
        const string contextRegRoot = "Software\\Classes\\Directory\\Background\\shell";
        public static readonly string launcherArgumentsFile = "LauncherArguments.txt";
        string _filterString = null;

        public MainWindow()
        {
            InitializeComponent();
            Start();
        }

        void Start()
        {
            LoadSettings();

            // make window resizable (this didnt work when used pure xaml to do this)
            WindowChrome Resizable_BorderLess_Chrome = new WindowChrome();
            //Resizable_BorderLess_Chrome.GlassFrameThickness = new Thickness(0);
            Resizable_BorderLess_Chrome.CornerRadius = new CornerRadius(0);
            Resizable_BorderLess_Chrome.CaptionHeight = 1.0;
            WindowChrome.SetWindowChrome(this, Resizable_BorderLess_Chrome);

            // get unity installations
            dataGridUnitys.Items.Clear();
            UpdateUnityInstallationsList();

            HandleCommandLineLaunch();

            // update projects list
            projectsSource = GetProjects.Scan(getGitBranch: (bool)chkShowGitBranchColumn.IsChecked, getArguments: (bool)chkShowLauncherArgumentsColumn.IsChecked);
            gridRecent.Items.Clear();
            gridRecent.ItemsSource = projectsSource;

            // updates grid
            dataGridUpdates.Items.Clear();

            // build notifyicon (using windows.forms)
            notifyIcon = new System.Windows.Forms.NotifyIcon();
            notifyIcon.Icon = new Icon(Application.GetResourceStream(new Uri("pack://application:,,,/Images/icon.ico")).Stream);
            notifyIcon.MouseClick += new System.Windows.Forms.MouseEventHandler(NotifyIcon_MouseClick);
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
                        //DisplayUpgradeDialog(version, projectPathArgument, launchProject: true, commandLineArguments: commandLineArguments);
                        //MessageBox.Show("Do you want to Save?", "3", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        //this.ShowActivated = false;
                        //Hide();'
                        //Show();
                        //Height = 10;
                        //Width = 10;
                        //Topmost = true;
                        Tools.DisplayUpgradeDialog(proj, null);
                    }
                    else
                    {
                        //MessageBox.Show("Do you want to Save?", "2", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        // try launching it
                        //LaunchProject(projectPathArgument, version, openProject: true, commandLineArguments: commandLineArguments);
                        Tools.LaunchProject(proj);
                    }

                    // quit after launch if enabled in settings
                    if (Properties.Settings.Default.closeAfterExplorer == true)
                    {
                        //MessageBox.Show("Do you want to Save?", "quit", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        //Environment.Exit(0);
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

            // set first row selected (good, especially if only one results)
            if (gridRecent.Items.Count > 0)
            {
                gridRecent.SelectedIndex = 0;
            }
        }

        private bool ProjectFilter(object item)
        {
            Project proj = item as Project;
            return (proj.Title.IndexOf(_filterString, 0, StringComparison.CurrentCultureIgnoreCase) != -1);
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
            chkShowFullTime.IsChecked = Properties.Settings.Default.showFullModifiedTime;

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
            for (int i = 0; i < unityInstallationsSource.Length; i++)
            {
                var version = unityInstallationsSource[i].Version;
                if (unityInstalledVersions.ContainsKey(version) == false)
                {
                    unityInstalledVersions.Add(version, unityInstallationsSource[i].Path);
                }
            }
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
            // TODO handle errors?
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

        private void OnSearchPreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    ((TextBox)sender).Text = "";
                    break;
                default:
                    break;
            }
        }

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
            TextBox textbox = (TextBox)sender;
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

        private async void OnGetUnityUpdatesClick(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            button.IsEnabled = false;

            CallGetUnityUpdates();

            button.IsEnabled = true;
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            // TODO if editing cells, dont focus on search
            //if (gridRecent.IsCurrentCellInEditMode == true) return;

            switch (tabControl.SelectedIndex)
            {
                case 0: // Projects

                    switch (e.Key)
                    {
                        case Key.F5: // refresh projects
                            projectsSource = GetProjects.Scan(getGitBranch: (bool)chkShowGitBranchColumn.IsChecked, getArguments: (bool)chkShowLauncherArgumentsColumn.IsChecked);
                            gridRecent.ItemsSource = projectsSource;
                            break;
                        case Key.Escape: // clear project search
                            txtSearchBox.Text = "";
                            break;
                        default: // any key
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
                //var update = getselect
            }
            if (copy != null) Clipboard.SetText(copy);
        }

        private void BtnRefreshProjectList_Click(object sender, RoutedEventArgs e)
        {
            projectsSource = GetProjects.Scan(getGitBranch: (bool)chkShowGitBranchColumn.IsChecked, getArguments: (bool)chkShowLauncherArgumentsColumn.IsChecked);
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
            // TODO check for newer available version in Updates tab, select that row and jump to tab
        }

        // if press up/down in search box, move to first item in results
        private void TxtSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
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
                    e.Handled = true;
                    Tools.LaunchProject(GetSelectedProject());
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
                // NOTE windows 10 cmd line supports ansi colors, otherwise remove -v color
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
            // TODO async
            //// check if your device is present
            //adb devices
            //// get device ip address (see inet row)
            //adb shell ip addr show wlan0
            //// enable tcpip and port
            //adb tcpip 5555
            //// connect device (use your device ip address)
            //adb connect IP_HERE:5555
        }

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

        private void ChkShowFullTime_CheckedChanged(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.showFullModifiedTime = (bool)chkShowFullTime.IsChecked;
            Properties.Settings.Default.Save();
        }

    } // class
} //namespace
