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
        public static string selectedPlatform = null;
        public static string[] platformsForThisUnity = null;

        bool isInitializing = true; // to keep OnChangeEvent from firing too early
        int previousSelectedTemplateIndex = -1;
        int previousSelectedModuleIndex = -1;

        public NewProject(string unityVersion, string suggestedName, string targetFolder)
        {
            isInitializing = true;
            InitializeComponent();

            // get version
            newVersion = unityVersion;
            newName = suggestedName;

            txtNewProjectName.Text = newName;
            lblNewProjectFolder.Content = targetFolder;

            // fill available versions, TODO could show which modules are installed
            if (gridAvailableVersions.ItemsSource == null) gridAvailableVersions.ItemsSource = MainWindow.unityInstallationsSource;

            // we have that version installed
            if (MainWindow.unityInstalledVersions.ContainsKey(unityVersion) == true)
            {
                // find this unity version, TODO theres probably easier way than looping all
                for (int i = 0; i < MainWindow.unityInstallationsSource.Length; i++)
                {
                    if (MainWindow.unityInstallationsSource[i].Version == newVersion)
                    {
                        gridAvailableVersions.SelectedIndex = i;
                        gridAvailableVersions.ScrollIntoView(gridAvailableVersions.SelectedItem);
                        break;
                    }
                }

                UpdateTemplatesDropDown((gridAvailableVersions.SelectedItem as UnityInstallation).Path);
                UpdateModulesDropdown(newVersion);
            }
            else // we dont have requested unity version, select first item then
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

            isInitializing = false;
        }  // NewProject

        void UpdateTemplatesDropDown(string unityPath)
        {
            // scan available templates, TODO could cache this at least per session?
            cmbNewProjectTemplate.ItemsSource = Tools.ScanTemplates(unityPath);
            cmbNewProjectTemplate.SelectedIndex = 0;
            lblTemplateTitleAndCount.Content = "Templates: (" + (cmbNewProjectTemplate.Items.Count - 1) + ")";
        }


        void UpdateModulesDropdown(string version)
        {
            // get modules and stick into combobox, NOTE we already have this info from GetProjects.Scan, so could access it
            platformsForThisUnity = Tools.GetPlatformsForUnityVersion(version);
            cmbNewProjectPlatform.ItemsSource = platformsForThisUnity;
            //System.Console.WriteLine(Tools.GetPlatformsForUnityVersion(version).Length);

            var lastUsedPlatform = Properties.Settings.Default.newProjectPlatform;

            for (int i = 0; i < platformsForThisUnity.Length; i++)
            {
                // set default platform (win64) if never used this setting before
                if ((lastUsedPlatform == null && platformsForThisUnity[i].ToLower() == "win64") || platformsForThisUnity[i] == lastUsedPlatform)
                {
                    cmbNewProjectPlatform.SelectedIndex = i;
                    break;
                }
            }

            //lblTemplateTitleAndCount.Content = "Templates: (" + (cmbNewProjectTemplate.Items.Count - 1) + ")";
        }



        private void BtnCreateNewProject_Click(object sender, RoutedEventArgs e)
        {
            templateZipPath = ((KeyValuePair<string, string>)cmbNewProjectTemplate.SelectedValue).Value;
            selectedPlatform = cmbNewProjectPlatform.SelectedValue.ToString();
            UpdateSelectedVersion();

            // save last used value for platform
            Properties.Settings.Default.newProjectPlatform = cmbNewProjectPlatform.SelectedValue.ToString();
            Properties.Settings.Default.Save();

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
                case Key.F2: // select project name field
                    txtNewProjectName.Focus();
                    txtNewProjectName.SelectAll();
                    break;
                case Key.F3: // next platform
                    cmbNewProjectPlatform.SelectedIndex = ++cmbNewProjectPlatform.SelectedIndex % cmbNewProjectPlatform.Items.Count;
                    break;
                case Key.F4: // next template
                case Key.Oem5:  // select next template §-key
                    cmbNewProjectTemplate.SelectedIndex = ++cmbNewProjectTemplate.SelectedIndex % cmbNewProjectTemplate.Items.Count;
                    e.Handled = true; // override writing to textbox
                    break;
                case Key.Enter: // enter, create proj
                    BtnCreateNewProject_Click(null, null);
                    e.Handled = true;
                    break;
                case Key.Escape: // esc cancel
                                 // if pressed esc while combobox is open, close that one instead of closing window
                    if (cmbNewProjectTemplate.IsDropDownOpen)
                    {
                        cmbNewProjectTemplate.IsDropDownOpen = false;
                        if (previousSelectedTemplateIndex > -1) cmbNewProjectTemplate.SelectedIndex = previousSelectedTemplateIndex;
                        return;
                    }

                    if (cmbNewProjectPlatform.IsDropDownOpen)
                    {
                        cmbNewProjectPlatform.IsDropDownOpen = false;
                        if (previousSelectedModuleIndex > -1) cmbNewProjectPlatform.SelectedIndex = previousSelectedModuleIndex;
                        return;
                    }

                    DialogResult = false;
                    e.Handled = true;
                    break;
                default:
                    break;
            }
        }

        void UpdateSelectedVersion()
        {
            var k = gridAvailableVersions.SelectedItem as UnityInstallation;
            if (k != null && k.Version != newVersion)
            {
                newVersion = k.Version;
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
            if (gridAvailableVersions.SelectedItem == null || isInitializing == true) return;
            // new row selected, generate new project name for this version
            var k = gridAvailableVersions.SelectedItem as UnityInstallation;
            newVersion = k.Version;
            GenerateNewName();

            // update templates list for selected unity version
            UpdateTemplatesDropDown(k.Path);
            UpdateModulesDropdown(k.Version);
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

        private void CmbNewProjectTemplate_DropDownOpened(object sender, System.EventArgs e)
        {
            // on open, take current selection, so can undo later
            previousSelectedTemplateIndex = cmbNewProjectTemplate.SelectedIndex;
        }

        private void CmbNewProjectPlatform_DropDownOpened(object sender, System.EventArgs e)
        {
            previousSelectedModuleIndex = cmbNewProjectPlatform.SelectedIndex;
        }
    }
}
