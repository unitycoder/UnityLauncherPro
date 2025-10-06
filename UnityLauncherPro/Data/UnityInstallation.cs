using System;
using System.ComponentModel;
using System.Windows.Data;

namespace UnityLauncherPro
{
    public class UnityInstallation : IValueConverter, INotifyPropertyChanged
    {
        public string Version { set; get; }
        public long VersionCode { set; get; } // version as number, cached for sorting
        public string Path { set; get; } // exe path
        public DateTime? Installed { set; get; }
        public string PlatformsCombined { set; get; }
        public string[] Platforms { set; get; }
        public int ProjectCount { set; get; }
        public bool IsPreferred { set; get; }
        public string ReleaseType { set; get; } // Alpha, Beta, LTS.. TODO could be enum

        //public string InfoLabel { set; get; } // this is additional info from Releases API (like vulnerabilities..)

        private string _infoLabel;
        public string InfoLabel
        {
            get => _infoLabel;
            set
            {
                if (_infoLabel == value) return;
                _infoLabel = value;
                OnPropertyChanged(nameof(InfoLabel));
            }
        }

        // https://stackoverflow.com/a/5551986/5452781
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            string version = value as string;
            if (string.IsNullOrEmpty(version)) return null;

            bool checkInfoLabel = true;

            if (MainWindow.unityInstalledVersions.ContainsKey(version))
            {
                //Console.WriteLine("checking version: "+version);
                if (checkInfoLabel && string.IsNullOrEmpty(InfoLabel) == false)
                {
                    Console.WriteLine("Contains warning: " + version);
                    return -1; // has warning
                }
                else
                {
                    return 1; // normal
                }
            }
            else
            {
                return 0; // not installed
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        // status label results are ready
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    }
}
