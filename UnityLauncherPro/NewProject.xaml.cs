using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace UnityLauncherPro
{
    public partial class NewProject : Window
    {
        public static string newProjectName = null;
        public static string newVersion = null;
        public static string newName = null;
        public static string templateZipPath = null;

        public NewProject(string unityVersion, string suggestedName, string targetFolder)
        {
            InitializeComponent();

            // get version
            newVersion = unityVersion;
            newName = suggestedName;

            txtNewProjectName.Text = newName;
            lblNewProjectFolder.Content = targetFolder;

            // fill available versions, TODO could show which modules are installed
            gridAvailableVersions.ItemsSource = MainWindow.unityInstalledVersions;

            var item = Tools.GetEntry(MainWindow.unityInstalledVersions, unityVersion);
            int index = gridAvailableVersions.Items.IndexOf(item);
            if (index > -1)
            {
                gridAvailableVersions.SelectedIndex = index;
                gridAvailableVersions.ScrollIntoView(item);
            }

            // scan available templates, TODO should cache this at least per session?
            cmbNewProjectTemplate.ItemsSource = Tools.ScanTemplates(item.Value);

            // select projectname text so can overwrite if needed
            txtNewProjectName.Focus();
            txtNewProjectName.SelectAll();
            newProjectName = txtNewProjectName.Text;
        }

        private void BtnCreateNewProject_Click(object sender, RoutedEventArgs e)
        {
            templateZipPath = ((KeyValuePair<string, string>)cmbNewProjectTemplate.SelectedValue).Value;
            UpdateSelectedVersion();
            DialogResult = true;
        }

        private void BtnCancelNewProject_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }


        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter: // enter accept
                    UpdateSelectedVersion();
                    DialogResult = true;
                    e.Handled = true;
                    break;
                case Key.Escape: // esc cancel
                    DialogResult = false;
                    e.Handled = true;
                    break;
                default:
                    break;
            }
        }

        void UpdateSelectedVersion()
        {
            var k = gridAvailableVersions.SelectedItem as KeyValuePair<string, string>?;
            if (k != null && k.Value.Key != newVersion)
            {
                newVersion = k.Value.Key;
            }
        }

        private void TxtNewProjectName_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            newProjectName = txtNewProjectName.Text;
        }

        private void TxtNewProjectName_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.PageUp:
                case Key.PageDown:
                case Key.Up:
                case Key.Down:
                    Tools.SetFocusToGrid(gridAvailableVersions);
                    break;
                default:
                    break;
            }
        }

        void GenerateNewName()
        {
            var newProj = Tools.GetSuggestedProjectName(newVersion, lblNewProjectFolder.Content.ToString());
            txtNewProjectName.Text = newProj;
        }

        private void GridAvailableVersions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (gridAvailableVersions.SelectedItem == null) return;
            // new row selected, generate new project name for this version
            var k = gridAvailableVersions.SelectedItem as KeyValuePair<string, string>?;
            newVersion = k.Value.Key;
            GenerateNewName();
        }

        private void GridAvailableVersions_Loaded(object sender, RoutedEventArgs e)
        {
            // set initial default row color
            DataGridRow row = (DataGridRow)gridAvailableVersions.ItemContainerGenerator.ContainerFromIndex(gridAvailableVersions.SelectedIndex);
            //row.Background = Brushes.Green;
            row.Foreground = Brushes.White;
            row.FontWeight = FontWeights.Bold;
        }
    }
}
