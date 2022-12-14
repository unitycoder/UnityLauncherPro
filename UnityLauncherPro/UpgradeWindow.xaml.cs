using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace UnityLauncherPro
{
    /// <summary>
    /// Interaction logic for UpgradeWindow.xaml
    /// </summary>
    public partial class UpgradeWindow : Window
    {
        public static string upgradeVersion = null;

        public UpgradeWindow(string currentVersion, string projectPath, string commandLineArguments = null)
        {
            InitializeComponent();
            txtCurrentVersion.Text = currentVersion;
            gridAvailableVersions.ItemsSource = MainWindow.unityInstalledVersions;
            gridAvailableVersions.SelectedItem = null;

            // we have current version info in project
            if (string.IsNullOrEmpty(currentVersion) == false)
            {
                // enable release and dl buttons
                btnOpenReleasePage.IsEnabled = true;
                btnDownload.IsEnabled = true;

                // if dont have exact version, show red outline
                if (MainWindow.unityInstalledVersions.ContainsKey(currentVersion) == false)
                {
                    txtCurrentVersion.BorderBrush = Brushes.Red;
                    txtCurrentVersion.BorderThickness = new Thickness(1);
                }

                // find nearest version
                string nearestVersion = Tools.FindNearestVersion(currentVersion, MainWindow.unityInstalledVersions.Keys.ToList());
                if (nearestVersion != null)
                {
                    // get correct row for nearest version
                    var obj = Tools.GetEntry(MainWindow.unityInstalledVersions, nearestVersion);
                    int index = gridAvailableVersions.Items.IndexOf(obj);
                    if (index > -1)
                    {
                        gridAvailableVersions.SelectedIndex = index;
                    }
                    else
                    {
                        // just select first item then
                        gridAvailableVersions.SelectedIndex = 0;
                    }
                }
            }
            else // we dont have current version info in project
            {
                btnOpenReleasePage.IsEnabled = false;
                btnDownload.IsEnabled = false;
                currentVersion = "None";

                // if we have preferred version, and current is null
                if (string.IsNullOrEmpty(MainWindow.preferredVersion) == false)
                {
                    // get correct row for preferred version
                    var obj = Tools.GetEntry(MainWindow.unityInstalledVersions, MainWindow.preferredVersion);
                    int index = gridAvailableVersions.Items.IndexOf(obj);
                    if (index > -1)
                    {
                        gridAvailableVersions.SelectedIndex = index;
                    }
                    else
                    {
                        // just select first item then
                        gridAvailableVersions.SelectedIndex = 0;
                    }
                }
                else
                {
                    // just select first item then
                    if (gridAvailableVersions != null && gridAvailableVersions.Items.Count > 0) gridAvailableVersions.SelectedIndex = 0;
                }

            }

            gridAvailableVersions.Focus();
        }


        private void BtnUpgradeProject_Click(object sender, RoutedEventArgs e)
        {
            Upgrade();
        }

        private void BtnCancelUpgrade_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void BtnOpenReleasePage_Click(object sender, RoutedEventArgs e)
        {
            Tools.OpenReleaseNotes(txtCurrentVersion.Text);
        }

        private void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            string url = Tools.GetUnityReleaseURL(txtCurrentVersion.Text);
            if (string.IsNullOrEmpty(url) == false)
            {
                Tools.DownloadInBrowser(url, txtCurrentVersion.Text);
            }
            else
            {
                Console.WriteLine("Failed getting Unity Installer URL for " + txtCurrentVersion.Text);
            }
        }

        private void btnInstall_Click(object sender, RoutedEventArgs e)
        {
            string url = Tools.GetUnityReleaseURL(txtCurrentVersion.Text);
            if (string.IsNullOrEmpty(url) == false)
            {
                Tools.DownloadAndInstall(url, txtCurrentVersion.Text);
            }
            else
            {
                Console.WriteLine("Failed getting Unity Installer URL for " + txtCurrentVersion.Text);
            }
        }

        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // override Enter for datagrid
            if (e.Key == Key.Return && e.KeyboardDevice.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                var k = (gridAvailableVersions.SelectedItem) as KeyValuePair<string, string>?;
                upgradeVersion = k.Value.Key;
                DialogResult = true;
                return;
            }
            else // other keys
            {
                switch (e.Key)
                {
                    case Key.Escape:
                        DialogResult = false;
                        break;
                    default:
                        break;
                }
            }

            base.OnKeyDown(e);
        }

        private void GridAvailableVersions_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            Tools.HandleDataGridScrollKeys(sender, e);
        }

        private void GridAvailableVersions_Loaded(object sender, RoutedEventArgs e)
        {
            Tools.SetFocusToGrid(gridAvailableVersions);

            // bolded for current item
            DataGridRow row = (DataGridRow)((DataGrid)sender).ItemContainerGenerator.ContainerFromIndex(gridAvailableVersions.SelectedIndex);
            if (row == null) return;
            row.Foreground = Brushes.White;
            row.FontWeight = FontWeights.Bold;
        }

        private void GridAvailableVersions_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var src = VisualTreeHelper.GetParent((DependencyObject)e.OriginalSource);
            var srcType = src.GetType();
            if (srcType == typeof(ContentPresenter))
            {
                Upgrade();
            }
        }

        void Upgrade()
        {
            var k = (gridAvailableVersions.SelectedItem) as KeyValuePair<string, string>?;
            upgradeVersion = k.Value.Key;
            DialogResult = true;
        }


    }
}
