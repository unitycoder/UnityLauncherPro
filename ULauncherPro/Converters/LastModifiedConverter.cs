using System;
using System.Globalization;
using System.Windows.Data;

namespace UnityLauncherPro.Converters
{
    // https://stackoverflow.com/a/14283973/5452781
    [ValueConversion(typeof(DateTime), typeof(String))]
    public class LastModifiedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // TODO: without this, editor mode fails with null references.. but would be nice to get rid of if's..
            if (value == null) return null;

            DateTime date = (DateTime)value;

            return MainWindow.useHumanFriendlyDateFormat ? Tools.GetElapsedTime(date) : date.ToString(MainWindow.currentDateFormat);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return DateTime.ParseExact((string)value, MainWindow.currentDateFormat, culture);
        }

    }    
    
    // just for tooltip
    [ValueConversion(typeof(DateTime), typeof(String))]
    public class LastModifiedConverterTooltip : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return null;
            DateTime date = (DateTime)value;
            return date.ToString(MainWindow.currentDateFormat);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return DateTime.ParseExact((string)value, MainWindow.currentDateFormat, culture);
        }

    }
}
