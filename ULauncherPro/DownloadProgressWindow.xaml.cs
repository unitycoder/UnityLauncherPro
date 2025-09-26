using System;
using System.Windows;
using System.Windows.Input;

namespace UnityLauncherPro
{
    public partial class DownloadProgressWindow
    {
        private readonly Action _cancelAction;
        private readonly string _subjectName;
        private static MainWindow MainWindow => Tools.mainWindow;

        public DownloadProgressWindow(string subjectName, Action cancelAction = null)
        {
            InitializeComponent();
            _subjectName = subjectName;
            Title = subjectName;
            _cancelAction = cancelAction;
            Topmost = true;
            Owner = MainWindow;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            MainWindow.IsEnabled = false;
        }

        public void UpdateProgress(DownloadProgress downloadProgress)
        {
            Title = $"Downloading {_subjectName} ({downloadProgress.TotalRead / 1024d / 1024d:F1} MB / {downloadProgress.TotalBytes / 1024d / 1024d:F1} MB)";
            var progress = downloadProgress.TotalBytes == 0 ? 0 : downloadProgress.TotalRead * 100d / downloadProgress.TotalBytes;
            ProgressBar.Value = progress;
            ProgressText.Text = $"{progress / 100:P1}";
        }

        private void CancelDownloadClick(object sender, RoutedEventArgs e)
        {
            CancelDownload();
        }

        private void CancelDownload()
        {
            _cancelAction?.Invoke();
        }
        
        private void Window_PreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            var window = (Window)sender;
            window.Topmost = true;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _cancelAction?.Invoke();
            MainWindow.IsEnabled = true;
        }
    }
}