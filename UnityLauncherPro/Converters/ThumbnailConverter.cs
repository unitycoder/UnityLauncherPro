using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace UnityLauncherPro.Converters
{
    public class ThumbnailConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Project project)
            {
                if (!string.IsNullOrEmpty(project.Path))
                {
                    string thumbnailPath = Path.Combine(project.Path, "ProjectSettings", "icon.png");

                    if (File.Exists(thumbnailPath))
                    {
                        try
                        {
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
                            // Ignore and fall back to null
                        }
                    }
                }
                return null;
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}