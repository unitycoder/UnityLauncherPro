using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace UnityLauncherPro.Converters
{
    public class ThumbnailConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Return UnsetValue if no project is selected
            if (value == null)
            {
                return DependencyProperty.UnsetValue;
            }

            if (value is Project project)
            {
                if (!string.IsNullOrEmpty(project.Path))
                {
                    string thumbnailPath = Path.Combine(project.Path, "ProjectSettings", "icon.png");

                    if (File.Exists(thumbnailPath))
                    {
                        try
                        {
                            // Check if this is for Width/Height parameter
                            if (parameter != null && (parameter.ToString() == "Width" || parameter.ToString() == "Height"))
                            {
                                return 64.0; // Return default dimension
                            }

                            // For Source binding, load the bitmap
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                            bitmap.UriSource = new Uri(thumbnailPath, UriKind.Absolute);
                            bitmap.DecodePixelWidth = 64; // Match your display size
                            bitmap.DecodePixelHeight = 64;

                            bitmap.EndInit();

                            // Freeze for cross-thread access
                            if (bitmap.CanFreeze)
                            {
                                bitmap.Freeze();
                            }

                            return bitmap;
                        }
                        catch
                        {
                            // Ignore and fall back to UnsetValue for Source, or 64.0 for dimensions
                            if (parameter != null && (parameter.ToString() == "Width" || parameter.ToString() == "Height"))
                            {
                                return 1.0;
                            }
                            return DependencyProperty.UnsetValue;
                        }
                    }
                }

                // Project path doesn't exist or no thumbnail found
                if (parameter != null && (parameter.ToString() == "Width" || parameter.ToString() == "Height"))
                {
                    return 1.0; // Return default dimension
                }
                return DependencyProperty.UnsetValue;
            }

            return DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}