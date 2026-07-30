using System;
using System.ComponentModel;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.UI.Xaml.Media.Imaging;

namespace HyperMedia
{
    public class RecentItem : INotifyPropertyChanged
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }

        private BitmapImage _thumbnail;
        public BitmapImage Thumbnail
        {
            get { return _thumbnail; }
            set
            {
                if (_thumbnail != value)
                {
                    _thumbnail = value;
                    OnPropertyChanged("Thumbnail");
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(name));
        }

        public async void LoadThumbnail()
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(FilePath);
                if (file == null) return;

                var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 80);
                if (thumbnail != null)
                {
                    var image = new BitmapImage();
                    await image.SetSourceAsync(thumbnail);
                    Thumbnail = image;
                }
            }
            catch
            {
                // File may not exist or thumbnail not available
            }
        }
    }
}
