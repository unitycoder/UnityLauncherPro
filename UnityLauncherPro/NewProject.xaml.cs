using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UnityLauncherPro.Data;

namespace UnityLauncherPro
{
    public partial class NewProject : Window
    {
        public static string newProjectName = null;
        public static string newVersion = null;
        public static string newName = null;
        public static string templateZipPath = null;
        public static string selectedPlatform = null;
        public static bool forceDX11 = false;
        public static string[] platformsForThisUnity = null;

        bool isInitializing = true; // to keep OnChangeEvent from firing too early
        int previousSelectedTemplateIndex = -1;
        int previousSelectedModuleIndex = -1;

        public static string targetFolder { get; private set; } = null;

        public NewProject(string unityVersion, string suggestedName, string targetFolder, bool nameIsLocked = false)
        {
            isInitializing = true;
            InitializeComponent();

            NewProject.targetFolder = targetFolder;

            // TODO could optionally disable templates in settings
            _ = LoadOnlineTemplatesAsync();

            // get version
            newVersion = unityVersion;
            newName = suggestedName;

            txtNewProjectName.IsEnabled = !nameIsLocked;

            txtNewProjectName.Text = newName;
            txtNewProjectFolder.Text = targetFolder;

            // fill available versions
            if (gridAvailableVersions.ItemsSource == null)
            {
                gridAvailableVersions.ItemsSource = MainWindow.unityInstallationsSource;
            }

            // we have that version installed
            if (MainWindow.unityInstalledVersions.ContainsKey(unityVersion) == true)
            {
                // find this unity version, TODO theres probably easier way than looping all
                for (int i = 0; i < MainWindow.unityInstallationsSource.Count; i++)
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

            var lastUsedPlatform = Properties.Settings.Default.newProjectPlatform;

            for (int i = 0; i < platformsForThisUnity.Length; i++)
            {
                // set default platform (win64) if never used this setting before
                if ((string.IsNullOrEmpty(lastUsedPlatform) && platformsForThisUnity[i].ToLower() == "win64") || platformsForThisUnity[i] == lastUsedPlatform)
                {
                    cmbNewProjectPlatform.SelectedIndex = i;
                    break;
                }
            }

            // if nothing found, use win64
            if (cmbNewProjectPlatform.SelectedIndex == -1)
            {
                //cmbNewProjectPlatform.SelectedIndex = cmbNewProjectPlatform.Items.Count > 1 ? 1 : 0;
                for (int i = 0; i < platformsForThisUnity.Length; i++)
                {
                    if (platformsForThisUnity[i].ToLower() == "win64")
                    {
                        cmbNewProjectPlatform.SelectedIndex = i;
                        break;
                    }
                }

                // if still nothing, use first
                if (cmbNewProjectPlatform.SelectedIndex == -1) cmbNewProjectPlatform.SelectedIndex = 0;
                //lblTemplateTitleAndCount.Content = "Templates: (" + (cmbNewProjectTemplate.Items.Count - 1) + ")";
            }
        }

        private void BtnCreateNewProject_Click(object sender, RoutedEventArgs e)
        {
            // check if projectname already exists (only if should be automatically created name)
            var targetPath = Path.Combine(targetFolder, txtNewProjectName.Text);
            if (txtNewProjectName.IsEnabled == true && Directory.Exists(targetPath) == true)
            {
                Tools.SetStatus("Project already exists: " + txtNewProjectName.Text);
                return;
            }

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

        private void TxtNewProjectName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isInitializing == true) return;

            // warning yellow if contains space at start or end
            if (txtNewProjectName.Text.StartsWith(" ") || txtNewProjectName.Text.EndsWith(" "))
            {
                // NOTE txtbox outline didnt work
                txtNewProjectName.Background = Brushes.Yellow;
                txtNewProjectStatus.Text = "Warning: Project name starts or ends with SPACE character";
                txtNewProjectStatus.Foreground = Brushes.Orange;
            }
            else
            {
                // NOTE this element is not using themes yet, so can set white
                txtNewProjectName.Background = Brushes.White;
                txtNewProjectStatus.Foreground = Brushes.White;
                txtNewProjectStatus.Text = "";
            }

            // validate new projectname that it doesnt exists already
            var targetPath = Path.Combine(targetFolder, txtNewProjectName.Text);
            if (Directory.Exists(targetPath) == true)
            {
                System.Console.WriteLine("Project already exists");
                txtNewProjectName.BorderBrush = Brushes.Red; // not visible if focused
                txtNewProjectName.ToolTip = "Project folder already exists";
                btnCreateNewProject.IsEnabled = false;
            }
            else
            {
                txtNewProjectName.BorderBrush = null;
                btnCreateNewProject.IsEnabled = true;
                txtNewProjectName.ToolTip = "";
            }

            //System.Console.WriteLine("newProjectName: " + txtNewProjectName.Text);

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
            var newProj = Tools.GetSuggestedProjectName(newVersion, txtNewProjectFolder.Text.ToString());
            txtNewProjectName.Text = newProj;
        }

        // FIXME this gets called when list is updated?
        private void GridAvailableVersions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (gridAvailableVersions.SelectedItem == null || isInitializing == true) return;
            // new row selected, generate new project name for this version
            var k = gridAvailableVersions.SelectedItem as UnityInstallation;
            newVersion = k.Version;
            // no new name, if field is locked (because its folder name then)
            if (txtNewProjectName.IsEnabled == true) GenerateNewName();

            // update templates list for selected unity version
            UpdateTemplatesDropDown(k.Path);
            UpdateModulesDropdown(k.Version);

            // hide forceDX11 checkbox if version is below 6000
            bool is6000 = k.Version.Contains("6000");
            chkForceDX11.Visibility = is6000 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void GridAvailableVersions_Loaded(object sender, RoutedEventArgs e)
        {
            // set initial default row color
            DataGridRow row = (DataGridRow)gridAvailableVersions.ItemContainerGenerator.ContainerFromIndex(gridAvailableVersions.SelectedIndex);
            // if no unitys available
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

        private void gridAvailableVersions_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // check that we clicked actually on a row
            var src = VisualTreeHelper.GetParent((DependencyObject)e.OriginalSource);
            var srcType = src.GetType();
            if (srcType == typeof(ContentPresenter))
            {
                BtnCreateNewProject_Click(null, null);
            }
        }

        private void chkForceDX11_Checked(object sender, RoutedEventArgs e)
        {
            forceDX11 = chkForceDX11.IsChecked == true;
        }

        private void btnBrowseForProjectFolder_Click(object sender, RoutedEventArgs e)
        {
            string defaultFolder = null;
            if (txtNewProjectFolder.Text != null)
            {
                if (Directory.Exists(txtNewProjectFolder.Text) == true)
                {
                    defaultFolder = txtNewProjectFolder.Text;
                }
                else
                {
                    // find closest existing parent folder
                    var dir = new DirectoryInfo(txtNewProjectFolder.Text);
                    while (dir.Parent != null)
                    {
                        dir = dir.Parent;
                        if (Directory.Exists(dir.FullName) == true)
                        {
                            defaultFolder = dir.FullName;
                            break;
                        }
                    }
                }
            }

            var folder = Tools.BrowseForOutputFolder("Select New Project folder", defaultFolder);
            if (string.IsNullOrEmpty(folder) == false && Directory.Exists(folder) == true)
            {
                txtNewProjectFolder.Text = folder;
            }

        }

        private void txtNewProjectFolder_TextChanged(object sender, TextChangedEventArgs e)
        {
            // validate that folder exists
            if (Directory.Exists(txtNewProjectFolder.Text) == false)
            {
                txtNewProjectFolder.BorderBrush = Brushes.Red; // not visible if focused
                btnCreateNewProject.IsEnabled = false;
                btnCreateMissingFolder.IsEnabled = true;
            }
            else
            {
                txtNewProjectFolder.BorderBrush = null;
                btnCreateNewProject.IsEnabled = true;
                targetFolder = txtNewProjectFolder.Text;
                btnCreateMissingFolder.IsEnabled = false;
            }
        }

        private void btnCreateMissingFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(txtNewProjectFolder.Text);
                txtNewProjectFolder.BorderBrush = null;
                btnCreateNewProject.IsEnabled = true;
                targetFolder = txtNewProjectFolder.Text;
            }
            catch (Exception ex)
            {
                Tools.SetStatus("Failed to create folder: " + ex.Message);
            }
        }

        private async System.Threading.Tasks.Task LoadOnlineTemplatesAsync()
        {
            // Simulate async loading (replace with actual async HTTP call later)
            await System.Threading.Tasks.Task.Run(() =>
            {
                var templates = new List<OnlineTemplateItem>
        {
            new OnlineTemplateItem
            {
                Name = "3D Template",
                Description = "A great starting point for 3D projects using the Universal Render Pipeline (URP).",
                PreviewImageURL = "https://storage.googleapis.com/live-platform-resources-prd/templates/assets/AR_Mobile_Thumbnail_HUB_464008d11a/AR_Mobile_Thumbnail_HUB_464008d11a.png",
                Type = "CORE",
                RenderPipeline = "URP",
                TarBallURL = "https://download.packages.unity.com/com.unity.template.hdrp-blank/-/com.unity.template.hdrp-blank-17.0.2.tgz"
            },
            new OnlineTemplateItem
            {
                Name = "2D Template",
                Description = "A great starting point for 2D projects using the Built-in Render Pipeline.",
                PreviewImageURL = "https://storage.googleapis.com/live-platform-resources-prd/templates/assets/Platformer_preview_887cd85a63/Platformer_preview_887cd85a63.png",
                Type = "CORE",
                RenderPipeline = "Built-in",
                TarBallURL = "https://download.packages.unity.com/com.unity.template.mr-multiplayer/-/com.unity.template.mr-multiplayer-1.0.3.tgz"
            },
            new OnlineTemplateItem
            {
                Name = "Wubba Template",
                Description = "A great asdfasdf projects using.",
                PreviewImageURL = "https://storage.googleapis.com/live-platform-resources-prd/templates/assets/2_4_1_Overview_627c09d1be/2_4_1_Overview_627c09d1be.png",
                Type = "SAMPLES",
                RenderPipeline = "URP",
                TarBallURL = "https://download.packages.unity.com/com.unity.template.vr/-/com.unity.template.vr-9.2.0.tgz"
            },
            new OnlineTemplateItem
            {
                Name = "ASDF Template",
                Description = "A great asdfasdf projects using.",
                PreviewImageURL = "https://storage.googleapis.com/live-platform-resources-prd/templates/assets/HDRP_c27702ce66/HDRP_c27702ce66.jpg",
                Type = "LEARNING",
                RenderPipeline = "HDRP",
                TarBallURL = "https://download.packages.unity.com/com.unity.template.platformer/-/com.unity.template.platformer-5.0.5.tgz"
            }
        };

                // Update UI on dispatcher thread
                Dispatcher.Invoke(() =>
                {
                    listOnlineTemplates.Items.Clear();
                    listOnlineTemplates.ItemsSource = templates;
                });
            });
        }
    }
}
