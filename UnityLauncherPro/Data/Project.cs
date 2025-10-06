using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;

namespace UnityLauncherPro
{
    public class Project : IValueConverter, INotifyPropertyChanged
    {
        public string Title { set; get; }
        public string Version { set; get; }
        public string Path { set; get; }
        public DateTime? Modified { set; get; }
        public string Arguments { set; get; }
        public string GITBranch { set; get; } // TODO rename to Branch
        //public string TargetPlatform { set; get; }
        public string TargetPlatform { set; get; } // TODO rename to Platform
        public string[] TargetPlatforms { set; get; }
        public bool folderExists { set; get; }
        public string SRP { set; get; } // Scriptable Render Pipeline, TODO add version info?

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

        // WPF keeps calling this method from AppendFormatHelper, GetNameCore..? not sure if need to return something else or default would be faster?
        public override string ToString()
        {
            return Path;
        }

        // for debugging
        //public override string ToString()
        //{
        //    return $"{Title} {Version} {Path} {Modified} {Arguments} {GITBranch} {TargetPlatform}";
        //}

        // change datagrid colors based on value using converter https://stackoverflow.com/a/5551986/5452781
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = (bool)value;
            return b;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}