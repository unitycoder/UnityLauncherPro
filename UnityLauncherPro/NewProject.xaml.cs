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
            if (gridAvailableVersions.ItemsSource == null) gridAvailableVersions.ItemsSource = MainWindow.unityInstalledVersions;

            // we dont have that version installed, TODO show info or pick closest?, for now picks the first item
            if (MainWindow.unityInstalledVersions.ContainsKey(unityVersion) == true)
            {
                // autopick this unity version
                var item = Tools.GetEntry(MainWindow.unityInstalledVersions, unityVersion);
                int index = gridAvailableVersions.Items.IndexOf(item);
                if (index > -1)
                {
                    gridAvailableVersions.SelectedIndex = index;
                    gridAvailableVersions.ScrollIntoView(item);
                }
                UpdateTemplatesDropDown(item.Value);
            }
            else // we dont have requested unity version, get templates for the first item
            {
                var path = MainWindow.unityInstallationsSource[0].Path;
                gridAvailableVersions.SelectedIndex = 0;
                gridAvailableVersions.ScrollIntoView(gridAvailableVersions.Items[0]);
                UpdateTemplatesDropDown(path);
            }

            // select projectname text so can overwrite if needed
            txtNewProjectName.Focus();
            txtNewProjectName.SelectAll();
            newProjectName = txtNewProjectName.Text;
        }

        void UpdateTemplatesDropDown(string unityPath)
        {
            // scan available templates, TODO could cache this at least per session?
            cmbNewProjectTemplate.ItemsSource = Tools.ScanTemplates(unityPath);
            cmbNewProjectTemplate.SelectedIndex = 0;
            lblTemplateTitleAndCount.Content = "Templates: (" + (cmbNewProjectTemplate.Items.Count - 1) + ")";
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
                case Key.Tab:
                    // manually tab into next component (automatic tabstops not really working here)
                    TraversalRequest tRequest = new TraversalRequest(FocusNavigationDirection.Next);
                    UIElement keyboardFocus = Keyboard.FocusedElement as UIElement;
                    if (keyboardFocus != null)
                    {
                        keyboardFocus.MoveFocus(tRequest);
                    }
                    break;
                case Key.Oem5:  // select next template §-key
                    cmbNewProjectTemplate.SelectedIndex = ++cmbNewProjectTemplate.SelectedIndex % cmbNewProjectTemplate.Items.Count;
                    e.Handled = true; // override writing to textbox
                    break;
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

        // FIXME this gets called when list is updated?
        private void GridAvailableVersions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (gridAvailableVersions.SelectedItem == null) return;
            // new row selected, generate new project name for this version
            var k = gridAvailableVersions.SelectedItem as KeyValuePair<string, string>?;
            newVersion = k.Value.Key;
            GenerateNewName();

            // update templates list for selected unity version
            UpdateTemplatesDropDown(k.Value.Value);
        }

        private void GridAvailableVersions_Loaded(object sender, RoutedEventArgs e)
        {
            // set initial default row color
            DataGridRow row = (DataGridRow)gridAvailableVersions.ItemContainerGenerator.ContainerFromIndex(gridAvailableVersions.SelectedIndex);
            // if now unitys available
            if (row == null) return;
            //row.Background = Brushes.Green;
            row.Foreground = Brushes.White;
            row.FontWeight = FontWeights.Bold;
        }
    }
}
