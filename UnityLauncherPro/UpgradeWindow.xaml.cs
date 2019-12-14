using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

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

            //lstUnityVersionsForUpgrade.Items.Clear();
            //lstUnityVersionsForUpgrade.Items.Add(MainWindow.unityInstalledVersions.ToArray());

            //lstUnityVersionsForUpgrade.ItemsSource = MainWindow.unityInstalledVersions;
            //lstUnityVersionsForUpgrade.DataValueField = "Key";
            //lstUnityVersionsForUpgrade.DataTextField = "Value";
            //lstUnityVersionsForUpgrade.DataBind();

        }



        private void BtnUpgradeProject_Click(object sender, RoutedEventArgs e)
        {
            var k = (gridAvailableVersions.SelectedItem) as KeyValuePair<string, string>?;
            //Console.WriteLine(k.Value.Key);
            upgradeVersion = k.Value.Key;
            DialogResult = true;
        }

        private void BtnCancelUpgrade_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
