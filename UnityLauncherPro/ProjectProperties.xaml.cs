using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace UnityLauncherPro
{
    /// <summary>
    /// Interaction logic for ProjectProperties.xaml
    /// </summary>
    public partial class ProjectProperties : Window
    {
        Project proj;

        public ProjectProperties(Project proj)
        {
            this.proj = proj;
            InitializeComponent();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {

        }

        private void btnCloseProperties_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void txtCustomEnvVariables_PreviewKeyDown(object sender, KeyEventArgs e)
        {

        }

        private void txtCustomEnvVariables_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            // TODO validate
        }

        private void btnApplyProperties_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;

            // TODO save settings to usersettings folder
            Tools.SaveProjectSettings(proj, txtCustomEnvVariables.Text);
        }
    }
}
