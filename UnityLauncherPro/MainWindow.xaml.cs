using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace UnityLauncherPro
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Start();
        }

        void Start()
        {
            // test data
            dataGrid.Items.Add(new TestData { Project = "asdf", Version = "5000", Path = "A:/", Modified = DateTime.Now, Arguments ="", GITBranch = "-" });
            dataGrid.Items.Add(new TestData { Project = "asdf asd", Version = "2", Path = "C:/", Modified = DateTime.Now, Arguments = "", GITBranch = "-" });
            dataGrid.Items.Add(new TestData { Project = "kuykkyu", Version = "23.23.23", Path = "8,1", Modified = DateTime.Now, Arguments = "", GITBranch = "-" });
            dataGrid.Items.Add(new TestData { Project = "RT435y", Version = "3333", Path = "X:/", Modified = DateTime.Now, Arguments = "", GITBranch = "-" });
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
            dialog.InitialDirectory = "c:";//textbox.Text; // Use current value for initial dir
            dialog.Title = "Select a Directory"; // instead of default "Save As"
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
                //textbox.Text = path;
                Console.WriteLine(path);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }

    public struct TestData
    {
        public string Project { set; get; }
        public string Version { set; get; }
        public string Path { set; get; }
        public DateTime Modified { set; get; }
        public string Arguments { set; get; }
        public string GITBranch { set; get; }
    }



}