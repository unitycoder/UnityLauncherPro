using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace UnityLauncherPro
{
    public class ThemeColor : IValueConverter
    {
        public string Key { get; set; }
        public SolidColorBrush Brush { get; set; }


        object IValueConverter.Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (SolidColorBrush)value;
        }

        object IValueConverter.ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
