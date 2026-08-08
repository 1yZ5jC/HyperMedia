using System;

namespace HyperMedia
{
    public interface IPlayerBackend
    {
        double DurationSeconds { get; }
        double PositionSeconds { get; set; }
        double Volume { get; set; }
        double Rate { get; set; }
        void OpenStream(Windows.Storage.Streams.IRandomAccessStream stream);
        void OpenPath(string path);
        void OpenUrl(string url);
        void Close();
        void Play();
        void Pause();
        void Stop();
        event EventHandler MediaOpened;
        event EventHandler MediaEnded;
        event EventHandler PlaybackFailed;
    }
}