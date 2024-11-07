using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace UnityLauncherPro.Converters
{
    [ValueConversion(typeof(DateTime), typeof(string))]
    public class ReleaseDateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || !(value is DateTime date))
            {
                return DependencyProperty.UnsetValue;
            }

            // Use a default date format if currentDateFormat is null or empty
            string dateStrTrimmed = MainWindow.currentDateFormat ?? "MM/dd/yyyy";

            // If the format includes time, use only the date portion
            if (dateStrTrimmed.Contains(" "))
            {
                dateStrTrimmed = dateStrTrimmed.Split(' ')[0];
            }

            // Return a human-friendly format if enabled; otherwise, format based on dateStrTrimmed
            return MainWindow.useHumanFriendlyDateFormat
                ? Tools.GetElapsedTime(date)
                : date.ToString(dateStrTrimmed, culture);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
            {
                return DependencyProperty.UnsetValue;
            }

            // Attempt to parse back to DateTime using the specified format
            if (DateTime.TryParseExact((string)value, MainWindow.currentDateFormat ?? "MM/dd/yyyy", culture, DateTimeStyles.None, out DateTime parsedDate))
            {
                return parsedDate;
            }

            return DependencyProperty.UnsetValue;
        }
    }
}
