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

        string _filterString = null;

        public MainWindow()
        {
            InitializeComponent();
            //this.DataContext = this;
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
            var tempRootFolders = new string[] { "D:/Program Files/" };
            unityInstallationsSource = GetUnityInstallations.Scan(tempRootFolders);
            dataGridUnitys.Items.Clear();
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


            //dataGrid.Items.Add(GetProjects.Scan());
            projectsSource = GetProjects.Scan();
            gridRecent.Items.Clear();
            gridRecent.ItemsSource = projectsSource;

            // updates grid
            dataGridUpdates.Items.Clear();
            //dataGridUpdates.ItemsSource = updatesSource;

            // build notifyicon (using windows.forms)
            notifyIcon = new System.Windows.Forms.NotifyIcon();
            notifyIcon.Icon = new Icon(System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Images/icon.ico")).Stream);
            notifyIcon.MouseClick += new System.Windows.Forms.MouseEventHandler(NotifyIcon_MouseClick);


            //gridRecent.CurrentCell = gridRecent.sele;
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
            //FilterProjects(textbox.Text);
        }

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

        void FilterRecentProjects()
        {
            // https://www.wpftutorial.net/DataViews.html
            _filterString = txtSearchBox.Text;
            ICollectionView collection = CollectionViewSource.GetDefaultView(projectsSource);
            collection.Filter = ProjectFilter;
        }

        private bool ProjectFilter(object item)
        {
            Project proj = item as Project;
            return (proj.Title.IndexOf(_filterString, 0, StringComparison.CurrentCultureIgnoreCase) != -1);
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
            notifyIcon.Visible = true;
            this.Hide();
        }

        private void TestButton(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Click!");
        }


        private async void OnGetUnityUpdatesClick(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            button.IsEnabled = false;

            var task = GetUnityUpdates.Scan();
            var items = await task;
            // TODO handle errors?
            if (items == null) return;
            updatesSource = GetUnityUpdates.Parse(items);
            if (updatesSource == null) return;
            dataGridUpdates.ItemsSource = updatesSource;

            button.IsEnabled = true;
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            // TODO if editing cells, dont focus on search
            //if (gridRecent.IsCurrentCellInEditMode == true) return;
            switch (e.Key)
            {
                case Key.Escape: // clear project search
                    if (tabControl.SelectedIndex == 0 && txtSearchBox.Text != "")
                    {
                        txtSearchBox.Text = "";
                    }
                    // clear updates search
                    else if (tabControl.SelectedIndex == 2 && txtSearchBoxUpdates.Text != "")
                    {
                        txtSearchBoxUpdates.Text = "";
                    }
                    break;
                case Key.Up:
                    break;
                case Key.Down:
                    break;
                default: // any key

                    // activate searchbar if not active and we are in tab#1
                    if (tabControl.SelectedIndex == 0 && txtSearchBox.IsFocused == false)
                    {
                        // dont write tab key on search field
                        if (e.Key == Key.Tab) break;

                        txtSearchBox.Focus();
                        //txtSearchBox.Text += e.Key;
                        txtSearchBox.Select(txtSearchBox.Text.Length, 0);
                    }
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

        void LoadSettings()
        {
            // form size
            this.Width = Properties.Settings.Default.windowWidth;
            this.Height = Properties.Settings.Default.windowHeight;

            /*
            // update settings window
            chkMinimizeToTaskbar.Checked = Properties.Settings.Default.minimizeToTaskbar;
            chkQuitAfterCommandline.Checked = Properties.Settings.Default.closeAfterExplorer;
            ChkQuitAfterOpen.Checked = Properties.Settings.Default.closeAfterProject;
            chkShowLauncherArgumentsColumn.Checked = Properties.Settings.Default.showArgumentsColumn;
            chkShowGitBranchColumn.Checked = Properties.Settings.Default.showGitBranchColumn;
            chkDarkSkin.Checked = Properties.Settings.Default.useDarkSkin;

            // update optional grid columns, hidden or visible
            gridRecent.Columns["_launchArguments"].Visible = chkShowLauncherArgumentsColumn.Checked;
            gridRecent.Columns["_gitBranch"].Visible = chkShowGitBranchColumn.Checked;

            // update installations folder listbox
            lstRootFolders.Items.Clear();
            lstRootFolders.Items.AddRange(Properties.Settings.Default.rootFolders.Cast<string>().ToArray());
            // update packages folder listbox
            lstPackageFolders.Items.AddRange(Properties.Settings.Default.packageFolders.Cast<string>().ToArray());

            // restore datagrid column widths
            int[] gridColumnWidths = Properties.Settings.Default.gridColumnWidths;
            if (gridColumnWidths != null)
            {
                for (int i = 0; i < gridColumnWidths.Length; ++i)
                {
                    gridRecent.Columns[i].Width = gridColumnWidths[i];
                }
            }
            */


        } // LoadSettings()

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            SaveSettingsOnExit();
        }

        private void SaveSettingsOnExit()
        {
            /*
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
                    gridWidths[i] = gridRecent.Columns[i].Width;
                }
                else
                {
                    gridWidths.Add(gridRecent.Columns[i].Width);
                }
            }
            Properties.Settings.Default.gridColumnWidths = gridWidths.ToArray();
            Properties.Settings.Default.Save();
            */
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
            LaunchProject(GetSelectedProject());
        }

        void LaunchProject(Project proj)
        {
            // validate
            if (proj == null) return;
            if (Directory.Exists(proj.Path) == false) return;

            Console.WriteLine("launching " + proj.Title);

            // there is no assets path, probably we want to create new project then
            var assetsFolder = Path.Combine(proj.Path, "Assets");
            if (Directory.Exists(assetsFolder) == false)
            {
                // TODO could ask if want to create project..?
                Directory.CreateDirectory(assetsFolder);
            }

            /*
            // TODO when opening project, check for crashed backup scene first
            if (openProject == true)
            {
                var cancelLaunch = CheckCrashBackupScene(projectPath);
                if (cancelLaunch == true)
                {
                    return;
                }
            }*/


            // we dont have this version installed (or no version info available)
            var unityExePath = GetUnityExePath(proj.Version);
            if (unityExePath == null)
            {
                Console.WriteLine("Missing unity version " + proj.Version);
                // SetStatus("Missing Unity version: " + version);
                // TODO
                //if (openProject == true) DisplayUpgradeDialog(version, projectPath);
                return;
            }

            /*
            if (openProject == true)
            {
                SetStatus("Launching project in Unity " + version);
            }
            else
            {
                SetStatus("Launching Unity " + version);
            }*/


            try
            {
                Process myProcess = new Process();
                var cmd = "\"" + unityExePath + "\"";
                myProcess.StartInfo.FileName = cmd;

                //if (openProject == true)
                {
                    var pars = " -projectPath " + "\"" + proj.Path + "\"";

                    // TODO check for custom launch parameters and append them
                    //string customArguments = GetSelectedRowData("_launchArguments");
                    //if (string.IsNullOrEmpty(customArguments) == false)
                    //{
                    //    pars += " " + customArguments;
                    //}

                    myProcess.StartInfo.Arguments = pars;// TODO args + commandLineArguments;
                }
                myProcess.Start();

                /*
                if (Properties.Settings.Default.closeAfterProject)
                {
                    Environment.Exit(0);
                }*/
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }


        string GetUnityExePath(string version)
        {
            return unityInstalledVersions.ContainsKey(version) ? unityInstalledVersions[version] : null;
        }


        Project GetSelectedProject()
        {
            return (Project)gridRecent.SelectedItem;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // override Enter for datagrid
            if (e.Key == Key.Return && e.KeyboardDevice.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                LaunchProject(GetSelectedProject());
                return;
            }

            base.OnKeyDown(e);
        }

        private void BtnExplore_Click(object sender, RoutedEventArgs e)
        {
            var proj = GetSelectedProject();
            Tools.ExploreProjectFolder(proj);
        }

        // copy selected row unity version to clipboard
        private void MenuItemCopyVersion_Click(object sender, RoutedEventArgs e)
        {
            var proj = GetSelectedProject();
            Clipboard.SetText(proj?.Version);
        }

        private void BtnRefreshProjectList_Click(object sender, RoutedEventArgs e)
        {
            projectsSource = GetProjects.Scan();
            gridRecent.ItemsSource = projectsSource;
        }

        // run unity only
        private void BtnLaunchUnity_Click(object sender, RoutedEventArgs e)
        {
            var proj = GetSelectedProject();
            var unitypath = GetUnityExePath(proj?.Version);
            Tools.LaunchExe(unitypath);
        }

        private void BtnUpgradeProject_Click(object sender, RoutedEventArgs e)
        {
            var proj = GetSelectedProject();
            if (proj == null) return;

            DisplayUpgradeDialog(proj);
        }

        void DisplayUpgradeDialog(Project proj)
        {
            UpgradeWindow modalWindow = new UpgradeWindow(proj.Version, proj.Path, proj.Arguments);
            modalWindow.Owner = this;
            modalWindow.ShowDialog();
            var results = modalWindow.DialogResult.HasValue && modalWindow.DialogResult.Value;

            if (results == true)
            {
                var upgradeToVersion = UpgradeWindow.upgradeVersion;
                if (string.IsNullOrEmpty(upgradeToVersion)) return;

                // get selected version to upgrade for
                Console.WriteLine("Upgrade to " + upgradeToVersion);

                // inject new version for this item
                proj.Version = upgradeToVersion;
                LaunchProject(proj);
            }
            else
            {
                Console.WriteLine("results = " + results);
            }
        }

        // need to manually move into next/prev rows? https://stackoverflow.com/a/11652175/5452781
        private void GridRecent_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            //Tools.HandleDataGridScrollKeys(sender, e);
        }

        private void GridRecent_Loaded(object sender, RoutedEventArgs e)
        {
            gridRecent.Focus();
            gridRecent.SelectedIndex = 0;
            // properly set focus to row
            DataGridRow row = (DataGridRow)gridRecent.ItemContainerGenerator.ContainerFromIndex(0);
            row.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }
    } // class
} //namespace



