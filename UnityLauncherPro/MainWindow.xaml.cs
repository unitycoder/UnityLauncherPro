using System;
using System.Drawing; // for notifyicon
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

            // add test data
            dataGrid.Items.Clear();
            /*
            for (int i = 0; i < 6; i++)
            {
                dataGrid.Items.Add(new Project { Title = "asdf" + i, Version = "5000", Path = "A:/", Modified = DateTime.Now, Arguments = "", GITBranch = "-" });
                dataGrid.Items.Add(new Project { Title = "asdf asd", Version = "2", Path = "C:/", Modified = DateTime.Now, Arguments = "", GITBranch = "-" });
                dataGrid.Items.Add(new Project { Title = "kuykkyu" + i * 2, Version = "23.23.23", Path = "8,1", Modified = DateTime.Now, Arguments = "", GITBranch = "-" });
                dataGrid.Items.Add(new Project { Title = "RT435y", Version = "3333", Path = "X:/", Modified = DateTime.Now, Arguments = "", GITBranch = "-" });
                dataGrid.Items.Add(new Project { Title = "asdf", Version = "5000", Path = "A:/", Modified = DateTime.Now, Arguments = "", GITBranch = "-" });
                dataGrid.Items.Add(new Project { Title = "asdf asd" + i * 3, Version = "2", Path = "C:/", Modified = DateTime.Now, Arguments = "", GITBranch = "-" });
                dataGrid.Items.Add(new Project { Title = "kuykkyu", Version = "23.23.23", Path = "8,1", Modified = DateTime.Now, Arguments = "", GITBranch = "-" });
            }*/

            //dataGrid.Items.Add(GetProjects.Scan());
            projectsSource = GetProjects.Scan();
            dataGrid.ItemsSource = projectsSource;


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
    }
}