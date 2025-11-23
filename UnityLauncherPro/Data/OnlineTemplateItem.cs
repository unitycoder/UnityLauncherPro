using System.ComponentModel;

namespace UnityLauncherPro.Data
{
    public class OnlineTemplateItem : INotifyPropertyChanged
    {
        private bool _isDownloaded;

        public string Name { get; set; }
        public string Description { get; set; }
        public string RenderPipeline { get; set; }
        public string Type { get; set; } // Core, Learning, Sample, 
        public string PreviewImageURL { get; set; }
        public string TarBallURL { get; set; }

        public bool IsDownloaded
        {
            get { return _isDownloaded; }
            set
            {
                if (_isDownloaded != value)
                {
                    _isDownloaded = value;
                    OnPropertyChanged(nameof(IsDownloaded));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}