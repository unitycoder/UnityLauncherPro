using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
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

        string selectedKey = null;

        private void GridThemeColors_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (gridThemeColors.SelectedItem == null) return;
            //Console.WriteLine(gridThemeColors.SelectedItem);
            var k = gridThemeColors.SelectedItem as KeyValuePair<string, SolidColorBrush>?;
            selectedKey = k.Value.Key;
            //Console.WriteLine("Selected: " +selectedKey + "=" + origResourceColors[selectedKey].ToString());

            // show color
            // TODO show current color AND modified color next to each other
            rectSelectedColor.Fill = origResourceColors[selectedKey];

            //txtSelectedColorHex.Text = origResourceColors[selectedKey].ToString();

            sliderRed.Value = origResourceColors[selectedKey].Color.R;
            sliderGreen.Value = origResourceColors[selectedKey].Color.G;
            sliderBlue.Value = origResourceColors[selectedKey].Color.B;

        }

        void UpdateColorPreview()
        {
            var newColor = new Color();
            newColor.A = 255;
            newColor.R = byte.Parse(((int)sliderRed.Value).ToString());
            newColor.G = byte.Parse(((int)sliderGreen.Value).ToString());
            newColor.B = byte.Parse(((int)sliderBlue.Value).ToString());
            var newColorBrush = new SolidColorBrush(newColor);
            rectSelectedColor.Fill = newColorBrush;

            // TODO apply color to datagrid or dictionary
            //if (selectedKey == null) return;
            //origResourceColors[selectedKey] = newColorBrush;
            //gridThemeColors.Items.Refresh();

            // TODO apply color changes to mainwindow
        }

        private void SliderRed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // onchanged is called before other components are ready..wpf :D
            if (txtRed == null) return;
            txtRed.Text = ((int)((Slider)sender).Value).ToString();
            UpdateColorPreview();
        }

        private void SliderGreen_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (txtGreen == null) return;
            txtGreen.Text = ((int)((Slider)sender).Value).ToString();
            UpdateColorPreview();
        }

        private void SliderBlue_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (txtBlue == null) return;
            txtBlue.Text = ((int)((Slider)sender).Value).ToString();
            UpdateColorPreview();
        }

        private void BtnSaveTheme_Click(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("TODO save theme to file..");
        }
    }
}
