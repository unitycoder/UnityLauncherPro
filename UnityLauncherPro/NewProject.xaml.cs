using System.Windows;
using System.Windows.Input;

namespace UnityLauncherPro
{
    public partial class NewProject : Window
    {
        public static string newProjectName = null;

        public NewProject(string unityVersion, string suggestedName)
        {
            InitializeComponent();

            // get version
            txtNewProjectVersion.Text = unityVersion;
            txtNewProjectName.Text = suggestedName;

            // select projectname text so can overwrite if needed
            txtNewProjectName.Focus();
            txtNewProjectName.SelectAll();
            newProjectName = txtNewProjectName.Text;
        }

        private void BtnCreateNewProject_Click(object sender, RoutedEventArgs e)
        {
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
                // TODO allow typing anywhere

                case Key.Enter: // enter accept
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

        private void TxtNewProjectName_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            newProjectName = txtNewProjectName.Text;
        }
    }
}
