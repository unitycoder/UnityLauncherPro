using System;
using System.Drawing; // for notifyicon
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;

namespace UnityLauncherPro
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        private System.Windows.Forms.NotifyIcon notifyIcon;

        Project[] projectsSource;
        Updates[] updatesSource;
        UnityInstallation[] unityInstallationsSource;

        public MainWindow()
        {
            InitializeComponent();
            Start();
        }

        void Start()
        {
            // make window resizable (this didnt work when used pure xaml to do this)
            WindowChrome Resizable_BorderLess_Chrome = new WindowChrome();
            Resizable_BorderLess_Chrome.GlassFrameThickness = new Thickness(0);
            Resizable_BorderLess_Chrome.CornerRadius = new CornerRadius(0);
            Resizable_BorderLess_Chrome.CaptionHeight = 1.0;
            WindowChrome.SetWindowChrome(this, Resizable_BorderLess_Chrome);


            // get unity installations
            var tempRootFolders = new string[] { "D:/Program Files/" };
            unityInstallationsSource = GetUnityInstallations.Scan(tempRootFolders);
            dataGridUnitys.Items.Clear();
            dataGridUnitys.ItemsSource = unityInstallationsSource;

            //dataGrid.Items.Add(GetProjects.Scan());
            projectsSource = GetProjects.Scan();
            dataGrid.Items.Clear();
            dataGrid.ItemsSource = projectsSource;

            // updates grid
            dataGridUpdates.Items.Clear();
            //dataGridUpdates.ItemsSource = updatesSource;

            // build notifyicon (using windows.forms)
            notifyIcon = new System.Windows.Forms.NotifyIcon();
            notifyIcon.Icon = new Icon(System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Images/icon.ico")).Stream);
            notifyIcon.MouseClick += new System.Windows.Forms.MouseEventHandler(NotifyIcon_MouseClick);

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
            //FilterProjects(textbox.Text);
        }

        private void OnSearchPreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    ((TextBox)sender).Text = "";
                    //FilterProjects(null);
                    break;
                default:
                    break;
            }
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
                if (!System.IO.Directory.Exists(path))
                {
                    System.IO.Directory.CreateDirectory(path);
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
            dataGrid.Focus();
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
                default: // any key
                    // activate searchbar if not active and we are in tab#1
                    if (tabControl.SelectedIndex == 0 && txtSearchBox.IsFocused == false)
                    {
                        // dont write tab key on search field
                        if (e.Key == Key.Tab) break;
                        txtSearchBox.Focus();
                        txtSearchBox.Text += e.Key;
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
    }
}