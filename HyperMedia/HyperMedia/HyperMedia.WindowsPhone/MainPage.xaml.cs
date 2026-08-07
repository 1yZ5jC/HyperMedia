using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace HyperMedia
{
    /// <summary>
    /// Playback page for Windows Phone: MediaElement-based player (libVLC has
    /// no WP8.1 binaries). Consumes what HomePage hands over via
    /// FutureAccessList["PlaybackFile"] + LocalSettings["PlaylistExtras"],
    /// or a direct navigation parameter (StorageFile / playlist:name / URL).
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private static readonly string[] PHOTO_EXTS =
            { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };

        private List<StorageFile> _playlist = new List<StorageFile>();
        private string _networkUrl;
        private int _playlistIndex = -1;
        private bool _isPlaying;
        private bool _isPhotoMode;
        private bool _seeking;
        private int _repeatMode; // 0 = off, 1 = list, 2 = single
        private string _originalFileName;
        private string _originalPath;
        private double _pendingResumePos;

        private readonly DispatcherTimer _positionTimer = new DispatcherTimer
        { Interval = TimeSpan.FromMilliseconds(500) };
        private readonly DispatcherTimer _autoHideTimer = new DispatcherTimer
        { Interval = TimeSpan.FromSeconds(3) };
        private DispatcherTimer _sleepTimer;
        private TimeSpan _sleepRemaining;

        public MainPage()
        {
            this.InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Required;

            _positionTimer.Tick += PositionTimer_Tick;
            _autoHideTimer.Tick += AutoHideTimer_Tick;

            VolumeSlider.Value = SettingsPage.GetDefaultVolume();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // Fresh open always replaces the previous playlist.
            _playlist.Clear();
            _networkUrl = null;
            _playlistIndex = -1;
            _isPhotoMode = false;
            _pendingResumePos = 0;
            PhotoImage.Source = null;
            NoSupportText.Visibility = Visibility.Collapsed;

            if (e.Parameter is StorageFile)
            {
                _playlist.Add(e.Parameter as StorageFile);
            }
            else if (e.Parameter is string)
            {
                string arg = e.Parameter as string;
                if (arg.StartsWith("playlist:", StringComparison.OrdinalIgnoreCase))
                {
                    string name = arg.Substring("playlist:".Length).Trim();
                    var files = PlaylistLibrary.GetPlaylistFiles(name);
                    if (files != null)
                    {
                        for (int i = 0; i < files.Count; i++)
                        {
                            try
                            {
                                var f = await StorageFile.GetFileFromPathAsync(files[i]);
                                if (f != null) _playlist.Add(f);
                            }
                            catch (Exception ex) { DebugLog("playlist item failed: " + ex.Message); }
                        }
                    }
                }
                else if (arg.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         arg.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                         arg.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase) ||
                         arg.StartsWith("mms://", StringComparison.OrdinalIgnoreCase))
                {
                    _networkUrl = arg;
                }
                else
                {
                    try
                    {
                        var f = await StorageFile.GetFileFromPathAsync(arg);
                        if (f != null) _playlist.Add(f);
                    }
                    catch (Exception ex) { DebugLog("path item failed: " + ex.Message); }
                }
            }

            if (_playlist.Count == 0 && _networkUrl == null)
                await LoadFromLocalStorage();

            _playlistIndex = 0;

            if (_networkUrl != null)
            {
                OpenNetworkStream();
            }
            else if (_playlist.Count > 0)
            {
                OpenCurrent();
            }
            else
            {
                FileNameText.Text = "没有可播放的文件";
                ShowControls();
            }
        }

        private async Task LoadFromLocalStorage()
        {
            try
            {
                if (StorageApplicationPermissions.FutureAccessList.ContainsItem("PlaybackFile"))
                {
                    var file = await StorageApplicationPermissions.FutureAccessList.GetFileAsync("PlaybackFile");
                    if (file != null) _playlist.Add(file);
                }

                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.ContainsKey("PlaylistExtras"))
                {
                    string joined = settings.Values["PlaylistExtras"] as string;
                    if (!string.IsNullOrEmpty(joined))
                    {
                        foreach (var p in joined.Split('|'))
                        {
                            if (string.IsNullOrEmpty(p)) continue;
                            try
                            {
                                var f = await StorageFile.GetFileFromPathAsync(p);
                                if (f != null) _playlist.Add(f);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex) { DebugLog("LoadFromLocalStorage failed: " + ex.Message); }
        }

        private void OpenCurrent()
        {
            if (_playlistIndex < 0 || _playlistIndex >= _playlist.Count) return;
            var file = _playlist[_playlistIndex];
            _originalFileName = file.Name;
            _originalPath = file.Path;

            PlayHistory.Add(file.Path, file.Name);
            FileNameText.Text = file.Name;
            NoSupportText.Visibility = Visibility.Collapsed;

            string ext = file.FileType.ToLowerInvariant();
            if (PHOTO_EXTS.Contains(ext))
            {
                OpenPhoto(file);
                return;
            }

            OpenMedia(file);
        }

        private async void OpenMedia(StorageFile file)
        {
            _isPhotoMode = false;
            PhotoImage.Visibility = Visibility.Collapsed;
            PhotoImage.Source = null;
            VideoPlayer.Visibility = Visibility.Visible;

            try
            {
                VideoPlayer.Source = null;
                var stream = await file.OpenReadAsync();
                VideoPlayer.SetSource(stream, "");
                VideoPlayer.Play();
                _isPlaying = true;
                UpdatePlayPauseIcon();
                PlayHistory.Add(file.Path, file.Name);
                StartPositionTimer();
                ResetAutoHide();
            }
            catch (Exception ex)
            {
                DebugLog("OpenMedia failed: " + ex.Message);
                ShowMediaUnsupported();
            }
        }

        private void OpenNetworkStream()
        {
            _isPhotoMode = false;
            PhotoImage.Visibility = Visibility.Collapsed;
            VideoPlayer.Visibility = Visibility.Visible;
            _originalFileName = _networkUrl;
            _originalPath = _networkUrl;
            FileNameText.Text = _networkUrl;

            try
            {
                VideoPlayer.Source = new Uri(_networkUrl);
                VideoPlayer.Play();
                _isPlaying = true;
                UpdatePlayPauseIcon();
                StartPositionTimer();
                ResetAutoHide();
            }
            catch (Exception ex)
            {
                DebugLog("OpenNetworkStream failed: " + ex.Message);
                ShowMediaUnsupported();
            }
        }

        private async void OpenPhoto(StorageFile file)
        {
            _isPhotoMode = true;
            VideoPlayer.Visibility = Visibility.Collapsed;
            PhotoImage.Visibility = Visibility.Visible;
            _positionTimer.Stop();
            UpdatePlayPauseIcon();
            ResetAutoHide();

            try
            {
                var stream = await file.OpenReadAsync();
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(stream);
                PhotoImage.Source = bitmap;
                PhotoTransform.ScaleX = 1;
                PhotoTransform.ScaleY = 1;
                PhotoTransform.Rotation = 0;
            }
            catch (Exception ex)
            {
                DebugLog("OpenPhoto failed: " + ex.Message);
            }
        }

        private void ShowMediaUnsupported()
        {
            _isPlaying = false;
            _positionTimer.Stop();
            UpdatePlayPauseIcon();
            NoSupportText.Visibility = Visibility.Visible;
        }

        private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            try
            {
                if (VideoPlayer.NaturalDuration.HasTimeSpan)
                {
                    double total = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                    DurationText.Text = FormatTime(total);
                    PositionSlider.Maximum = Math.Max(1, total);
                }

                if (_pendingResumePos > 0)
                {
                    double total = VideoPlayer.NaturalDuration.HasTimeSpan
                        ? VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds : 0;
                    if (total <= 0 || _pendingResumePos < total - 5)
                    {
                        try { VideoPlayer.Position = TimeSpan.FromSeconds(_pendingResumePos); }
                        catch { }
                    }
                    _pendingResumePos = 0;
                }
            }
            catch (Exception ex) { DebugLog("MediaOpened failed: " + ex.Message); }
        }

        private void VideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            _isPlaying = false;
            UpdatePlayPauseIcon();

            if (_repeatMode == 2)
            {
                try { VideoPlayer.Position = TimeSpan.Zero; } catch { }
                VideoPlayer.Play();
                _isPlaying = true;
                UpdatePlayPauseIcon();
                return;
            }

            if (_repeatMode == 1 || _playlist.Count > 1)
            {
                PlayNext();
            }
            else
            {
                _positionTimer.Stop();
                CurrentTimeText.Text = DurationText.Text;
                PositionSlider.Value = PositionSlider.Maximum;
            }
        }

        private void VideoPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            ShowMediaUnsupported();
        }

        private void PositionTimer_Tick(object sender, object e)
        {
            if (_seeking) return;
            try
            {
                if (VideoPlayer.NaturalDuration.HasTimeSpan && VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds > 0)
                {
                    double total = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                    double pos = VideoPlayer.Position.TotalSeconds;
                    PositionSlider.Value = Math.Min(total, Math.Max(0, pos));
                }
                CurrentTimeText.Text = FormatTime(VideoPlayer.Position.TotalSeconds);
            }
            catch (Exception ex) { DebugLog("PositionTimer failed: " + ex.Message); }
        }

        private void PositionSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (!_seeking) return;
            CurrentTimeText.Text = FormatTime(e.NewValue);
            try { VideoPlayer.Position = TimeSpan.FromSeconds(e.NewValue); }
            catch (Exception ex) { DebugLog("seek failed: " + ex.Message); }
        }

        private void VolumeSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            try { VideoPlayer.Volume = Math.Max(0, Math.Min(1.0, e.NewValue / 100.0)); }
            catch (Exception ex) { DebugLog("volume failed: " + ex.Message); }
        }

        private void PlayPauseBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isPhotoMode)
            {
                GoToPhoto(1);
                return;
            }

            if (_isPlaying)
                PausePlayback();
            else
                ResumePlayback();
        }

        private void ResumePlayback()
        {
            if (_isPlaying || _networkUrl == null && _playlist.Count == 0) return;

            // Played-to-completion state: restart from the beginning.
            try
            {
                if (VideoPlayer.NaturalDuration.HasTimeSpan &&
                    VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds > 0 &&
                    VideoPlayer.Position >= VideoPlayer.NaturalDuration.TimeSpan)
                {
                    VideoPlayer.Position = TimeSpan.Zero;
                    CurrentTimeText.Text = "00:00";
                    PositionSlider.Value = 0;
                }
            }
            catch { }

            VideoPlayer.Play();
            _isPlaying = true;
            UpdatePlayPauseIcon();
            StartPositionTimer();
            ResetAutoHide();
        }

        private void PausePlayback()
        {
            if (!_isPlaying) return;
            VideoPlayer.Pause();
            _isPlaying = false;
            UpdatePlayPauseIcon();
            ShowControls();
        }

        private void StopBtn_Click(object sender, RoutedEventArgs e)
        {
            SaveResumePosition();
            VideoPlayer.Stop();
            _isPlaying = false;
            _positionTimer.Stop();
            CurrentTimeText.Text = "00:00";
            PositionSlider.Value = 0;
            UpdatePlayPauseIcon();
            ShowControls();
        }

        private void PrevBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isPhotoMode) { GoToPhoto(-1); return; }
            if (_playlist.Count == 0 && _networkUrl == null) return;
            PlayPrev();
        }

        private void NextBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isPhotoMode) { GoToPhoto(1); return; }
            if (_playlist.Count == 0 && _networkUrl == null) return;
            PlayNext();
        }

        private void PlayPrev()
        {
            SaveResumePosition();
            if (_playlist.Count > 1)
            {
                _playlistIndex = (_playlistIndex - 1 + _playlist.Count) % _playlist.Count;
                OpenCurrent();
            }
            else
            {
                try
                {
                    VideoPlayer.Position = TimeSpan.Zero;
                    VideoPlayer.Play();
                    _isPlaying = true;
                    UpdatePlayPauseIcon();
                }
                catch { }
            }
        }

        private void PlayNext()
        {
            SaveResumePosition();
            if (_playlist.Count > 1)
            {
                if (_repeatMode == 0 && _playlistIndex >= _playlist.Count - 1)
                {
                    _isPlaying = false;
                    _positionTimer.Stop();
                    UpdatePlayPauseIcon();
                    return;
                }
                _playlistIndex = (_playlistIndex + 1) % _playlist.Count;
                OpenCurrent();
            }
            else
            {
                try { VideoPlayer.Position = TimeSpan.Zero; } catch { }
                VideoPlayer.Play();
                _isPlaying = true;
                UpdatePlayPauseIcon();
            }
        }

        private void GoToPhoto(int delta)
        {
            if (_playlist.Count <= 1) return;
            _playlistIndex = (_playlistIndex + delta + _playlist.Count) % _playlist.Count;
            OpenCurrent();
        }

        private void RepeatBtn_Click(object sender, RoutedEventArgs e)
        {
            _repeatMode = (_repeatMode + 1) % 3;
            switch (_repeatMode)
            {
                case 0: RepeatBtn.Content = "⇄"; break;
                case 1: RepeatBtn.Content = "循环列表"; break;
                case 2: RepeatBtn.Content = "单曲循环"; break;
            }
            ResetAutoHide();
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            SaveResumePosition();
            VideoPlayer.Stop();
            if (Frame != null && Frame.CanGoBack)
                Frame.GoBack();
            else
                Frame.Navigate(typeof(HomePage));
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            SaveResumePosition();
            try { VideoPlayer.Stop(); } catch { }
            _isPlaying = false;
            _positionTimer.Stop();
            _autoHideTimer.Stop();
            if (_sleepTimer != null) _sleepTimer.Stop();
        }

        #region Resume position

        private void SaveResumePosition()
        {
            if (!SettingsPage.GetResumeEnabled()) return;
            if (_networkUrl != null || string.IsNullOrEmpty(_originalFileName)) return;
            if (_isPhotoMode) return;

            try
            {
                if (!VideoPlayer.NaturalDuration.HasTimeSpan) return;
                double total = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                if (total <= 5) return;
                double pos = VideoPlayer.Position.TotalSeconds;

                // Near the end counts as finished.
                if (pos >= total - 5)
                {
                    RemoveResumePosition();
                    return;
                }

                var settings = ApplicationData.Current.LocalSettings;
                settings.Values["ResumePosition_" + _originalFileName] = pos;
                settings.Values["ResumePercent_" + _originalFileName] = total > 0 ? pos / total : 0;
            }
            catch (Exception ex) { DebugLog("SaveResumePosition failed: " + ex.Message); }
        }

        private void LoadResumePosition()
        {
            if (!SettingsPage.GetResumeEnabled()) return;
            if (_networkUrl != null || string.IsNullOrEmpty(_originalFileName)) return;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.ContainsKey("ResumePosition_" + _originalFileName))
                {
                    _pendingResumePos = Convert.ToDouble(settings.Values["ResumePosition_" + _originalFileName]);
                }
            }
            catch { }
        }

        private void RemoveResumePosition()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values.Remove("ResumePosition_" + _originalFileName);
                settings.Values.Remove("ResumePercent_" + _originalFileName);
            }
            catch (Exception ex) { DebugLog("RemoveResumePosition failed: " + ex.Message); }
        }

        #endregion

        #region Sleep timer

        private void StartSleepTimer()
        {
            int minutes = SettingsPage.GetSleepTimer();
            if (minutes <= 0) return;

            if (_sleepTimer == null)
            {
                _sleepTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _sleepTimer.Tick += SleepTimer_Tick;
            }
            _sleepRemaining = TimeSpan.FromMinutes(minutes);
            _sleepTimer.Start();
        }

        private void SleepTimer_Tick(object sender, object e)
        {
            _sleepRemaining = _sleepRemaining - TimeSpan.FromSeconds(1);
            if (_sleepRemaining <= TimeSpan.Zero)
            {
                _sleepTimer.Stop();
                if (_isPlaying) PausePlayback();
                NoSupportText.Text = "睡眠定时器: 播放已停止";
                NoSupportText.Visibility = Visibility.Visible;
            }
        }

        #endregion

        #region Controls / auto-hide

        private void Root_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            ToggleControls();
        }

        private void ToggleControls()
        {
            if (BottomBar.Visibility == Visibility.Visible)
                HideControls();
            else
                ShowControls();
        }

        private void ShowControls()
        {
            BottomBar.Visibility = Visibility.Visible;
            TopBar.Visibility = Visibility.Visible;
            ResetAutoHide();
        }

        private void HideControls()
        {
            BottomBar.Visibility = Visibility.Collapsed;
            TopBar.Visibility = Visibility.Collapsed;
        }

        private void ResetAutoHide()
        {
            if (!SettingsPage.GetAutoHideEnabled() || _isPhotoMode) return;
            _autoHideTimer.Stop();
            _autoHideTimer.Interval = TimeSpan.FromSeconds(SettingsPage.GetAutoHideDelay());
            _autoHideTimer.Start();
        }

        private void AutoHideTimer_Tick(object sender, object e)
        {
            _autoHideTimer.Stop();
            HideControls();
        }

        private void StartPositionTimer()
        {
            _positionTimer.Start();
            LoadResumePosition();
            StartSleepTimer();
        }

        private void UpdatePlayPauseIcon()
        {
            if (_isPhotoMode)
            {
                PlayPauseBtn.Content = "▶";
                return;
            }
            PlayPauseBtn.Content = _isPlaying ? "⏸" : "▶";
        }

        #endregion

        private static string FormatTime(double seconds)
        {
            if (seconds < 0) seconds = 0;
            var t = TimeSpan.FromSeconds(seconds);
            return t.Hours > 0
                ? string.Format("{0}:{1:D2}:{2:D2}", t.Hours, t.Minutes, t.Seconds)
                : string.Format("{0}:{1:D2}", t.Minutes, t.Seconds);
        }

        private static void DebugLog(string message)
        {
            System.Diagnostics.Debug.WriteLine("[HyperMedia.WP] " + message);
        }
    }
}
