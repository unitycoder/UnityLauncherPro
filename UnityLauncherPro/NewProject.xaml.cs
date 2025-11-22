using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UnityLauncherPro.Data;
using UnityLauncherPro.Properties;

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
        private CancellationTokenSource _templateLoadCancellation;

        public NewProject(string unityVersion, string suggestedName, string targetFolder, bool nameIsLocked = false)
        {
            isInitializing = true;
            InitializeComponent();

            NewProject.targetFolder = targetFolder;

            LoadSettings();

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

                        string baseVersion = GetBaseVersion(newVersion);
                        _ = LoadOnlineTemplatesAsync(baseVersion);
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

        private void LoadSettings()
        {
            chkForceDX11.IsChecked = Settings.Default.forceDX11;
        }

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
            lblOverride.Visibility = chkForceDX11.Visibility = is6000 ? Visibility.Visible : Visibility.Collapsed;
            //chkForceDX11.IsChecked = chkForceDX11.Visibility == Visibility.Visible ? forceDX11 : false;
            forceDX11 = Settings.Default.forceDX11 && is6000;

            string baseVersion = GetBaseVersion(k.Version);
            // Cancel previous request
            _templateLoadCancellation?.Cancel();
            _templateLoadCancellation = new CancellationTokenSource();
            _ = LoadOnlineTemplatesAsync(baseVersion, _templateLoadCancellation.Token);
        }

        string GetBaseVersion(string version)
        {
            // e.g. 2020.3.15f1 -> 2020.3
            var parts = version.Split('.');
            if (parts.Length >= 2)
            {
                return parts[0] + "." + parts[1];
            }
            return version;
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
            if (isInitializing) return; // Don't save during initialization

            Settings.Default.forceDX11 = forceDX11;
            Settings.Default.Save();
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

        private async Task LoadOnlineTemplatesAsync(string baseVersion, CancellationToken cancellationToken = default)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Accept", "application/json");

                    var graphqlJson = "{\"query\":\"fragment TemplateEntity on Template { __typename name packageName description type buildPlatforms renderPipeline previewImage { url } versions { name tarball { url } } } query HUB__getTemplates($limit: Int! $skip: Int! $orderBy: TemplateOrder! $supportedUnityEditorVersions: [String!]!) { getTemplates(limit: $limit skip: $skip orderBy: $orderBy supportedUnityEditorVersions: $supportedUnityEditorVersions) { edges { node { ...TemplateEntity } } } }\",\"variables\":{\"limit\":40,\"skip\":0,\"orderBy\":\"WEIGHTED_DESC\",\"supportedUnityEditorVersions\":[\"" + baseVersion + "\"]}}";

                    var content = new StringContent(graphqlJson, Encoding.UTF8, "application/json");

                    // Check for cancellation before making request
                    if (cancellationToken.IsCancellationRequested) return;

                    var response = await client.PostAsync("https://live-platform-api.prd.ld.unity3d.com/graphql", content, cancellationToken);

                    // Check for cancellation after request
                    if (cancellationToken.IsCancellationRequested) return;

                    if (response.IsSuccessStatusCode)
                    {
                        var responseString = await response.Content.ReadAsStringAsync();

                        // Check for cancellation before parsing
                        if (cancellationToken.IsCancellationRequested) return;

                        var templates = ParseTemplatesFromJson(responseString);

                        // Update UI on dispatcher thread only if not cancelled
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                // Only set ItemsSource, don't touch Items
                                listOnlineTemplates.ItemsSource = templates;
                            });
                        }
                    }
                    else
                    {
                        Console.WriteLine($"GraphQL request failed: {response.StatusCode}");
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            LoadFallbackTemplates();
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Request was cancelled, this is expected
                Console.WriteLine("Template loading cancelled");
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine($"Error loading online templates: {ex.Message}");
                    LoadFallbackTemplates();
                }
            }
        }

        private void LoadFallbackTemplates()
        {
            var templates = new List<OnlineTemplateItem>
    {
        new OnlineTemplateItem
        {
            Name = "3D Template",
            Description = "A great starting point for 3D projects using the Universal Render Pipeline (URP).",
            PreviewImageURL = "pack://application:,,,/Images/icon.png",
            Type = "CORE",
            RenderPipeline = "URP"
        }
    };

            Dispatcher.Invoke(() =>
            {
                // Only set ItemsSource, don't use Items.Clear()
                listOnlineTemplates.ItemsSource = templates;
            });
        }

        private List<OnlineTemplateItem> ParseTemplatesFromJson(string json)
        {
            var templates = new List<OnlineTemplateItem>();

            try
            {
                // Find the edges array
                int edgesStart = json.IndexOf("\"edges\":");
                if (edgesStart == -1) return templates;

                // Find all node objects
                int currentPos = edgesStart;
                while (true)
                {
                    int nodeStart = json.IndexOf("{\"__typename\":\"Template\"", currentPos);
                    if (nodeStart == -1) break;

                    // Find the end of this node object (simplified - find matching brace)
                    int nodeEnd = FindMatchingBrace(json, nodeStart);
                    if (nodeEnd == -1) break;

                    string nodeJson = json.Substring(nodeStart, nodeEnd - nodeStart + 1);

                    // Parse individual fields
                    var template = new OnlineTemplateItem
                    {
                        Name = ExtractJsonString(nodeJson, "\"name\""),
                        Description = ExtractJsonString(nodeJson, "\"description\""),
                        Type = ExtractJsonString(nodeJson, "\"type\""),
                        RenderPipeline = ExtractJsonString(nodeJson, "\"renderPipeline\""),
                        PreviewImageURL = ExtractNestedJsonString(nodeJson, "\"previewImage\"", "\"url\"") ?? "pack://application:,,,/Images/icon.png",
                        TarBallURL = ExtractNestedJsonString(nodeJson, "\"tarball\"", "\"url\"")
                    };

                    templates.Add(template);
                    currentPos = nodeEnd + 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing templates: {ex.Message}");
            }

            return templates;
        }

        private string ExtractJsonString(string json, string key)
        {
            int keyIndex = json.IndexOf(key + ":");
            if (keyIndex == -1) return null;

            int valueStart = json.IndexOf("\"", keyIndex + key.Length + 1);
            if (valueStart == -1) return null;

            int valueEnd = json.IndexOf("\"", valueStart + 1);
            if (valueEnd == -1) return null;

            return json.Substring(valueStart + 1, valueEnd - valueStart - 1);
        }

        private string ExtractNestedJsonString(string json, string parentKey, string childKey)
        {
            int parentIndex = json.IndexOf(parentKey + ":");
            if (parentIndex == -1) return null;

            // Find the object after parentKey
            int objectStart = json.IndexOf("{", parentIndex);
            if (objectStart == -1) return null;

            int objectEnd = FindMatchingBrace(json, objectStart);
            if (objectEnd == -1) return null;

            string nestedJson = json.Substring(objectStart, objectEnd - objectStart + 1);
            return ExtractJsonString(nestedJson, childKey);
        }

        private int FindMatchingBrace(string json, int openBraceIndex)
        {
            int braceCount = 0;
            bool inString = false;
            bool escapeNext = false;

            for (int i = openBraceIndex; i < json.Length; i++)
            {
                char c = json[i];

                if (escapeNext)
                {
                    escapeNext = false;
                    continue;
                }

                if (c == '\\')
                {
                    escapeNext = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (!inString)
                {
                    if (c == '{') braceCount++;
                    else if (c == '}')
                    {
                        braceCount--;
                        if (braceCount == 0) return i;
                    }
                }
            }

            return -1;
        }
    } // class NewProject
} // namespace UnityLauncherPro
