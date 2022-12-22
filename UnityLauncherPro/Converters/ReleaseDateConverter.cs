using System;
using System.Globalization;
using System.Windows.Data;

namespace UnityLauncherPro.Converters
{
    [ValueConversion(typeof(DateTime), typeof(String))]
    public class ReleaseDateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return null;
            DateTime date = (DateTime)value;

            // get first part of string until space character (updates only contain mm/dd/yyyy)
            string dateStrTrimmed = MainWindow.currentDateFormat;
            if (dateStrTrimmed.IndexOf(' ') > -1) dateStrTrimmed = dateStrTrimmed.Split(' ')[0];

            return MainWindow.useHumanFriendlyDateFormat ? Tools.GetElapsedTime(date) : date.ToString(dateStrTrimmed);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // not used ?
            return DateTime.ParseExact((string)value, MainWindow.currentDateFormat, culture);
        }

    }
}
