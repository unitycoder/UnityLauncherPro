using System;
using System.Windows.Data;

namespace UnityLauncherPro
{
    public class UnityInstallation : IValueConverter
    {
        public string Version { set; get; }
        public string Path { set; get; }
        public DateTime? Installed { set; get; }
        public string Platforms { set; get; }
        public bool IsPreferred { set; get; }

        // color project unity version cells, depending if have that version installed
        // https://stackoverflow.com/a/5551986/5452781
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            string version = value as string;
            //return MainWindow.unityInstalledVersions.ContainsKey(version) ? Brushes.Yellow : Brushes.Red;
            return MainWindow.unityInstalledVersions.ContainsKey(version);// ? "Red" : "Blue";
            //return Brushes.Yellow;
            // DependencyProperty.UnsetValue
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }

    }
}
