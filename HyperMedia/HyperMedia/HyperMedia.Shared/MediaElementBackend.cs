using System;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace HyperMedia
{
    public class MediaElementBackend : IPlayerBackend
    {
        private readonly MediaElement _element;

        public MediaElementBackend(MediaElement element)
        {
            _element = element;
            _element.MediaOpened += (s, e) => { var h = MediaOpened; if (h != null) h(this, EventArgs.Empty); };
            _element.MediaEnded += (s, e) => { var h = MediaEnded; if (h != null) h(this, EventArgs.Empty); };
            _element.MediaFailed += (s, e) => { var h = PlaybackFailed; if (h != null) h(this, EventArgs.Empty); };
        }

        public double DurationSeconds
        {
            get
            {
                try
                {
                    return _element.NaturalDuration.HasTimeSpan
                        ? _element.NaturalDuration.TimeSpan.TotalSeconds : -1;
                }
                catch { return -1; }
            }
        }

        public double PositionSeconds
        {
            get { try { return _element.Position.TotalSeconds; } catch { return 0; } }
            set { try { _element.Position = TimeSpan.FromSeconds(Math.Max(0, value)); } catch { } }
        }

        public double Volume
        {
            get { try { return _element.Volume; } catch { return 1; } }
            set { try { _element.Volume = Math.Max(0, Math.Min(1.0, value)); } catch { } }
        }

        public double Rate
        {
            get { try { return _element.PlaybackRate; } catch { return 1; } }
            set { try { _element.PlaybackRate = Math.Max(0.25, Math.Min(4.0, value)); } catch { } }
        }

        public void OpenStream(IRandomAccessStream stream)
        {
            try { _element.Source = null; _element.SetSource(stream, ""); } catch { }
        }

        public void OpenPath(string path)
        {
            try
            {
                var file = StorageFile.GetFileFromPathAsync(path).GetResults();
                if (file == null) return;
                var stream = file.OpenReadAsync().GetResults();
                if (stream != null) OpenStream(stream);
            }
            catch { }
        }

        public void OpenUrl(string url)
        {
            try { _element.Source = new Uri(url); } catch { }
        }

        public void Close()
        {
            try { _element.Source = null; } catch { }
        }

        public void Play() { try { _element.Play(); } catch { } }

        public void Pause() { try { _element.Pause(); } catch { } }

        public void Stop() { try { _element.Stop(); } catch { } }

        public event EventHandler MediaOpened;
        public event EventHandler MediaEnded;
        public event EventHandler PlaybackFailed;
    }
}