using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Data;

namespace UnityLauncherPro
{
    public class Project : IValueConverter
    {
        public string Title { set; get; }
        public string Version { set; get; }
        public string Path { set; get; }
        public DateTime? Modified { set; get; }
        public string Arguments { set; get; }
        public string GITBranch { set; get; }
        //public string TargetPlatform { set; get; }
        public string TargetPlatform { set; get; }
        public string[] TargetPlatforms { set; get; }
        public bool folderExists { set; get; }

        public override string ToString()
        {
            return $"{Title} {Version} {Path} {Modified} {Arguments} {GITBranch} {TargetPlatform}";
        }

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
    }
}