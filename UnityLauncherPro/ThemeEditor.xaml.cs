using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace UnityLauncherPro
{
    public partial class ThemeEditor : Window
    {
        // TODO take from mainwindow?
        Dictionary<string, SolidColorBrush> origResourceColors = new Dictionary<string, SolidColorBrush>();

        public ThemeEditor()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // get original colors
            foreach (DictionaryEntry item in Application.Current.Resources.MergedDictionaries[0])
            {
                // take original colors, so can reset them
                origResourceColors[item.Key.ToString()] = (SolidColorBrush)item.Value;
                var col = (SolidColorBrush)item.Value;
                //Console.WriteLine(item.Key.ToString() + "=" + col);

                //var col = new BrushConverter().ConvertFrom(row[1].Trim());
                // apply color
                //Application.Current.Resources[row[0]] = (SolidColorBrush)col;
            }

            // display current theme keys and values
            gridThemeColors.ItemsSource = origResourceColors;

        }

        private void GridThemeColors_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (gridThemeColors.SelectedItem == null) return;
            //Console.WriteLine(gridThemeColors.SelectedItem);
            var k = gridThemeColors.SelectedItem as KeyValuePair<string, SolidColorBrush>?;
            var selectedKey = k.Value.Key;
            Console.WriteLine("Selected: " +selectedKey + "=" + origResourceColors[selectedKey].ToString());
        }
    }
}
