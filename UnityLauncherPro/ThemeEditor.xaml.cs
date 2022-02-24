using Microsoft.Win32;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace UnityLauncherPro
{
    public partial class ThemeEditor : Window
    {
        static ObservableCollection<ThemeColor> themeColors = new ObservableCollection<ThemeColor>();
        static ObservableCollection<ThemeColor> themeColorsOrig = new ObservableCollection<ThemeColor>();

        string previousSaveFileName = null;

        // hack for adjusting slider, without triggering onchange..
        bool forceValue = false;

        // for single undo
        Slider previousSlider;
        int previousValue = -1;

        public ThemeEditor()
        {
            InitializeComponent();
        }

        void UpdateColorPreview()
        {
            var newColor = new Color();
            newColor.R = (byte)sliderRed.Value;
            newColor.G = (byte)sliderGreen.Value;
            newColor.B = (byte)sliderBlue.Value;
            newColor.A = (byte)sliderAlpha.Value;
            var newColorBrush = new SolidColorBrush(newColor);
            rectSelectedColor.Fill = newColorBrush;

            // set new color into our collection values
            themeColors[themeColors.IndexOf((ThemeColor)gridThemeColors.SelectedItem)].Brush = newColorBrush;

            // NOTE slow but works..
            gridThemeColors.Items.Refresh();

            // apply color changes to mainwindow
            var item = gridThemeColors.SelectedItem as ThemeColor;
            Application.Current.Resources[item.Key] = newColorBrush;
            forceValue = false;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            themeColors.Clear();
            themeColorsOrig.Clear();

            // get original colors to collection
            foreach (DictionaryEntry item in Application.Current.Resources.MergedDictionaries[0])
            {
                // take currently used colors
                var currentColor = (SolidColorBrush)Application.Current.Resources[item.Key];

                var themeColorPair = new ThemeColor();
                themeColorPair.Key = item.Key.ToString();
                themeColorPair.Brush = currentColor;
                themeColors.Add(themeColorPair);

                // take backup copy
                var themeColorPair2 = new ThemeColor();
                themeColorPair2.Key = item.Key.ToString();
                themeColorPair2.Brush = currentColor;
                themeColorsOrig.Add(themeColorPair2);
            }
            // display current theme keys and values
            gridThemeColors.ItemsSource = themeColors;

            // sort by key sa default
            gridThemeColors.Items.SortDescriptions.Add(new SortDescription("Key", ListSortDirection.Ascending));

            gridThemeColors.SelectedIndex = 0;
        }

        private void GridThemeColors_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (gridThemeColors.SelectedIndex == -1) return;

            var item = gridThemeColors.SelectedItem as ThemeColor;
            if (item == null) return;

            // update preview box
            rectSelectedColor.Fill = item.Brush;

            // update RGBA sliders
            forceValue = true;
            sliderRed.Value = item.Brush.Color.R;
            forceValue = true;
            sliderGreen.Value = item.Brush.Color.G;
            forceValue = true;
            sliderBlue.Value = item.Brush.Color.B;
            forceValue = true;
            sliderAlpha.Value = item.Brush.Color.A;
            forceValue = false;
        }

        private void BtnSaveTheme_Click(object sender, RoutedEventArgs e)
        {
            var themeFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Themes");

            if (Directory.Exists(themeFolder) == false) Directory.CreateDirectory(themeFolder);

            SaveFileDialog saveFileDialog = new SaveFileDialog();
            if (string.IsNullOrEmpty(previousSaveFileName))
            {
                saveFileDialog.FileName = "custom";
            }
            else
            {
                saveFileDialog.FileName = previousSaveFileName;
            }
            saveFileDialog.DefaultExt = ".ini";
            saveFileDialog.Filter = "Theme files (.ini)|*.ini";
            saveFileDialog.InitialDirectory = themeFolder;
            saveFileDialog.RestoreDirectory = true;

            if (saveFileDialog.ShowDialog() == true)
            {
                List<string> iniRows = new List<string>();
                iniRows.Add("# Created with UnityLauncherPro built-in theme editor " + DateTime.Now.ToString("dd/MM/YYYY"));
                for (int i = 0; i < themeColors.Count; i++)
                {
                    iniRows.Add(themeColors[i].Key + "=" + themeColors[i].Brush.ToString());
                }

                var themePath = saveFileDialog.FileName;
                previousSaveFileName = Path.GetFileNameWithoutExtension(themePath);
                File.WriteAllLines(themePath, iniRows);
                Console.WriteLine("Saved theme: " + themePath);
                // TODO close theme editor window?
            }
        }

        private void BtnResetTheme_Click(object sender, RoutedEventArgs e)
        {
            for (int i = 0; i < themeColorsOrig.Count; i++)
            {
                // reset collection colors
                themeColors[i].Brush = themeColorsOrig[i].Brush;

                // reset application colors
                Application.Current.Resources[themeColors[i].Key] = themeColorsOrig[i].Brush;
            }

            // reset current color
            if (gridThemeColors.SelectedItem != null)
            {
                var item = gridThemeColors.SelectedItem as ThemeColor;
                forceValue = true;
                sliderRed.Value = item.Brush.Color.R;
                forceValue = true;
                sliderGreen.Value = item.Brush.Color.G;
                forceValue = true;
                sliderBlue.Value = item.Brush.Color.B;
                forceValue = true;
                sliderAlpha.Value = item.Brush.Color.A;
                forceValue = false;
            }

            UpdateColorPreview();

            gridThemeColors.Items.Refresh();
        }

        private void SliderRed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (forceValue == true) return;
            if (txtRed == null) return; // onchanged is called before other components are ready..thanks wpf :D
            UpdateColorPreview();
        }

        private void SliderGreen_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (forceValue == true) return;
            if (txtGreen == null) return;
            UpdateColorPreview();
        }

        private void SliderBlue_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (forceValue == true) return;
            if (txtBlue == null) return;
            UpdateColorPreview();
        }

        public void Executed_Undo(object sender, ExecutedRoutedEventArgs e)
        {
            // restore previous color
            forceValue = true;
            previousSlider.Value = previousValue;
            forceValue = false;
            UpdateColorPreview();
        }

        public void CanExecute_Undo(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = previousValue > -1;
        }

        // TODO could add paste HTML code from clipboard
        //public void Executed_Paste(object sender, ExecutedRoutedEventArgs e)
        //{
        //    //OnPasteImageFromClipboard();
        //}

        //public void CanExecute_Paste(object sender, CanExecuteRoutedEventArgs e)
        //{
        //    e.CanExecute = true;
        //}

        private void SliderAlpha_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (forceValue == true) return;
            if (txtAlpha == null) return;
            UpdateColorPreview();
        }

        public void Executed_Save(object sender, ExecutedRoutedEventArgs e)
        {
            BtnSaveTheme_Click(null, null);
        }

        public void CanExecute_Save(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }

        private void SliderRed_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            SetUndoValues(sender, txtRed);
        }

        private void SliderGreen_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            SetUndoValues(sender, txtGreen);
        }

        private void SliderBlue_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            SetUndoValues(sender, txtBlue);
        }

        private void SliderAlpha_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            SetUndoValues(sender, txtAlpha);
        }

        void SetUndoValues(Object sender, TextBox textBox)
        {
            previousSlider = (Slider)sender;
            previousValue = (int)previousSlider.Value;
        }
    } // class
} // namespace