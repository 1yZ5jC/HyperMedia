using System;
using System.ComponentModel;
using System.Diagnostics;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.UI.Xaml.Media.Imaging;

namespace HyperMedia
{
    public class RecentItem : INotifyPropertyChanged
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public string Category { get; set; }

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

        private string _resumeText;
        public string ResumeText
        {
            get { return _resumeText; }
            set
            {
                if (_resumeText != value)
                {
                    _resumeText = value;
                    OnPropertyChanged("ResumeText");
                }
            }
        }

        private double _resumePercent = -1;
        public double ResumePercent
        {
            get { return _resumePercent; }
            set
            {
                if (_resumePercent != value)
                {
                    _resumePercent = value;
                    OnPropertyChanged("ResumePercent");
                }
            }
        }

        private string _ratingText;
        public string RatingText
        {
            get { return _ratingText; }
            set
            {
                if (_ratingText != value)
                {
                    _ratingText = value;
                    OnPropertyChanged("RatingText");
                }
            }
        }

        private string _playCountText;
        public string PlayCountText
        {
            get { return _playCountText; }
            set
            {
                if (_playCountText != value)
                {
                    _playCountText = value;
                    OnPropertyChanged("PlayCountText");
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

                // Try category-appropriate thumbnail mode first
                ThumbnailMode mode = ThumbnailMode.ListView;
                if (Category == "Videos")
                    mode = ThumbnailMode.VideosView;
                else if (Category == "Music")
                    mode = ThumbnailMode.MusicView;

                try
                {
                    var thumbnail = await file.GetThumbnailAsync(mode, 128);
                    if (thumbnail != null)
                    {
                        var image = new BitmapImage();
                        await image.SetSourceAsync(thumbnail);
                        Thumbnail = image;
                        return;
                    }
                }
                catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }

                // Fallback to SingleItem
                try
                {
                    var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 128);
                    if (thumbnail != null)
                    {
                        var image = new BitmapImage();
                        await image.SetSourceAsync(thumbnail);
                        Thumbnail = image;
                        return;
                    }
                }
                catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }

                // Final fallback: try to get icon
                try
                {
                    var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 80);
                    if (thumbnail != null)
                    {
                        var image = new BitmapImage();
                        await image.SetSourceAsync(thumbnail);
                        Thumbnail = image;
                        return;
                    }
                }
                catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }

                // No system thumbnail available (typical for HEVC on Windows 8.1).
                // Software-decode the first frame with libVLC and cache it.
                if (Category == "Videos")
                {
                    var custom = await VideoThumbnailService.TryGetThumbnailAsync(FilePath);
                    if (custom != null)
                        Thumbnail = custom;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HyperMedia] Thumbnail load failed for {0}: {1}", FileName, ex.Message);
            }
        }
    }
}
