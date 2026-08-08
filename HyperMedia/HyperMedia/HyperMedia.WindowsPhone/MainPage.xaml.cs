using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.UI;
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
        private static readonly double[] SPEEDS = { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };
        private static readonly string[] AUDIO_EXTS =
            { ".mp3", ".wav", ".flac", ".aac", ".m4a", ".ogg", ".oga", ".wma", ".opus", ".amr" };
        private static readonly Color VIS_THEME =
            Color.FromArgb(255, 224, 64, 251); // Zune purple, matches desktop

        private List<StorageFile> _playlist = new List<StorageFile>();
        private string _networkUrl;
        private int _playlistIndex = -1;
        private bool _isPlaying;
        private bool _mediaEnded;
        private bool _isPhotoMode;
        private bool _seeking;
        private int _repeatMode; // 0 = off, 1 = list, 2 = single
        private IPlayerBackend _player;
        private int _speedIndex = 2;
        private string _originalFileName;
        private string _originalPath;
        private double _pendingResumePos;

        // Photo gesture state (WP 8.1 Image has no built-in pinch/pan).
        private bool _photoGesturing;
        private double _photoStartScaleX, _photoStartScaleY;
        private double _photoStartRotation;

        private readonly DispatcherTimer _positionTimer = new DispatcherTimer
        { Interval = TimeSpan.FromMilliseconds(500) };
        private readonly DispatcherTimer _autoHideTimer = new DispatcherTimer
        { Interval = TimeSpan.FromSeconds(3) };
        private DispatcherTimer _sleepTimer;
        private TimeSpan _sleepRemaining;
        private DispatcherTimer _engineNoticeTimer;

        // Visualizer state (audio only; MediaElement exposes no PCM on WP8.1).
        private readonly DispatcherTimer _visTimer = new DispatcherTimer
        { Interval = TimeSpan.FromMilliseconds(33) };
        private SpectrumEngine _spectrum;
        private IVisualizerRenderer _renderer;
        private WriteableBitmap _visBitmap;
        private byte[] _visPixels;
        private int _visIndex;
        private readonly IVisualizerRenderer[] _visStyles =
        {
            new BarsRenderer(),
            new SymmetryRenderer(),
            new RingRenderer(),
            new ParticlesRenderer(),
            new NebulaRenderer(),
            new AlbumHueRenderer(),
            new WaveRenderer(),
        };

        public MainPage()
        {
            this.InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Required;

            _positionTimer.Tick += PositionTimer_Tick;
            _autoHideTimer.Tick += AutoHideTimer_Tick;
            _visTimer.Tick += VisTimer_Tick;

            PositionSlider.AddHandler(UIElement.PointerPressedEvent,
                new Windows.UI.Xaml.Input.PointerEventHandler(PositionSlider_PointerPressed), true);
            PositionSlider.AddHandler(UIElement.PointerReleasedEvent,
                new Windows.UI.Xaml.Input.PointerEventHandler(PositionSlider_PointerReleased), true);

            #if USE_LIBVLC
            if (PlaybackEngineSettings.Current == PlaybackEngine.Vlc
                && PlaybackEngineSettings.IsEngineAvailable(PlaybackEngine.Vlc))
            {
                var vlc = new VlcBackend(VlcVideoPanel);
                vlc.Init();
                if (vlc.IsReady)
                {
                    _player = vlc;
                    VlcVideoPanel.Visibility = Visibility.Visible;
                    VideoPlayer.Visibility = Visibility.Collapsed;
                }
                else
                {
                    _player = new MediaElementBackend(VideoPlayer);
                    ShowEngineNotice(vlc.InitError);
                }
            }
            else
#endif
            {
                _player = new MediaElementBackend(VideoPlayer);
            }
            Debug.WriteLine("[HyperMedia] PlaybackBackend: " +
                (_player is VlcBackend ? "VlcBackend (libVLCX)" : "MediaElementBackend (system)"));
            VolumeSlider.Value = SettingsPage.GetDefaultVolume();

            _player.MediaOpened += VideoPlayer_MediaOpened;
            _player.MediaEnded += VideoPlayer_MediaEnded;
            _player.PlaybackFailed += VideoPlayer_MediaFailed;
        }

        // Visible fallback notice: prevents silently ignoring VLC init failure.
        private void ShowEngineNotice(string detail)
        {
            try
            {
                if (EngineNoticeText == null) return;
                string msg = L("VlcFallback");
                if (!string.IsNullOrEmpty(detail))
                {
                    Debug.WriteLine("[HyperMedia] VlcBackend init detail: " + detail);
                    if (detail.Length > 220) detail = detail.Substring(0, 220) + "...";
                    msg += "\n" + detail;
                }
                EngineNoticeText.Text = msg;
                EngineNoticeText.Visibility = Visibility.Visible;
                if (_engineNoticeTimer == null)
                {
                    _engineNoticeTimer = new DispatcherTimer
                    { Interval = TimeSpan.FromSeconds(20) };
                    _engineNoticeTimer.Tick += (s, e) =>
                    {
                        _engineNoticeTimer.Stop();
                        if (EngineNoticeText != null)
                            EngineNoticeText.Visibility = Visibility.Collapsed;
                    };
                }
                _engineNoticeTimer.Stop();
                _engineNoticeTimer.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HyperMedia] ShowEngineNotice failed: {0}", ex.Message);
            }
        }

        private static string L(string key)
        {
            try
            {
                var appText = Application.Current.Resources["AppText"] as AppText;
                if (appText != null) return appText.T(key);
            }
            catch { }
            return key;
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
            _repeatMode = 0;
            _speedIndex = 2;
            SpeedBtn.Content = "1.0x";
            PhotoBar.Visibility = Visibility.Collapsed;

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
                StopVisualizer();
                OpenPhoto(file);
                return;
            }

            OpenMedia(file);
        }

        private async void OpenMedia(StorageFile file)
        {
            _isPhotoMode = false;
            _mediaEnded = false;
            _seeking = false;
            HidePhotoBar();
            PhotoImage.Visibility = Visibility.Collapsed;
            PhotoImage.Source = null;
            SetVideoSurfaceVisible(true);

            // Audio files have no picture to show; put the faux visualizer behind
            // the (blank) video surface instead of solid black.
            if (AUDIO_EXTS.Contains(file.FileType.ToLowerInvariant()))
            {
                StartVisualizer();
                SetVideoSurfaceVisible(false);
            }
            else
            {
                StopVisualizer();
            }

            try
            {
                _player.Close();
                await OpenMediaFile(file);
                _player.Rate = SPEEDS[_speedIndex];
                _player.Play();
                _isPlaying = true;
                UpdatePlayPauseIcon();
                PlayHistory.Add(file.Path, file.Name);
                StartPositionTimer();
                ShowControls();
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
            _mediaEnded = false;
            _seeking = false;
            HidePhotoBar();
            StopVisualizer();
            PhotoImage.Visibility = Visibility.Collapsed;
            SetVideoSurfaceVisible(true);
            _originalFileName = _networkUrl;
            _originalPath = _networkUrl;
            FileNameText.Text = _networkUrl;

            try
            {
                _player.OpenUrl(_networkUrl);
                _player.Rate = SPEEDS[_speedIndex];
                _player.Play();
                _isPlaying = true;
                UpdatePlayPauseIcon();
                StartPositionTimer();
                ShowControls();
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
            SetVideoSurfaceVisible(false);
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
                PhotoBar.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                DebugLog("OpenPhoto failed: " + ex.Message);
            }
        }

        private void HidePhotoBar()
        {
            PhotoBar.Visibility = Visibility.Collapsed;
        }

        // Shows/hides whichever video surface is active (MediaElement or, when
        // the VLC engine is selected, the SwapChainPanel that libVLC draws into).
        private void SetVideoSurfaceVisible(bool visible)
        {
            VideoPlayer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
#if USE_LIBVLC
            if (VlcVideoPanel != null)
                VlcVideoPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
#endif
        }

        // libVLCX on WP8.1 cannot consume IRandomAccessStream directly, so local
        // files are staged into the app's TemporaryFolder and opened by path.
        private async Task OpenMediaFile(StorageFile file)
        {
#if USE_LIBVLC
            if (_player is VlcBackend)
            {
                try
                {
                    var temp = ApplicationData.Current.TemporaryFolder;
                    string tmpName = "hm_" + Guid.NewGuid().ToString("N") + file.FileType;
                    var copy = await file.CopyAsync(temp, tmpName, NameCollisionOption.ReplaceExisting);
                    _player.OpenPath(copy.Path);
                    return;
                }
                catch
                {
                    try { _player.OpenPath(file.Path); return; } catch { }
                    throw;
                }
            }
#endif
            var stream = await file.OpenReadAsync();
            _player.OpenStream(stream);
        }

        private void ShowMediaUnsupported()
        {
            _isPlaying = false;
            _positionTimer.Stop();
            UpdatePlayPauseIcon();
            NoSupportText.Visibility = Visibility.Visible;
        }

        private void VideoPlayer_MediaOpened(object sender, EventArgs e)
        {
            try
            {
                double total = _player.DurationSeconds;
                if (total > 0)
                {
                    DurationText.Text = FormatTime(total);
                    PositionSlider.Maximum = Math.Max(1, total);
                    if (PositionProgress != null) PositionProgress.Maximum = PositionSlider.Maximum;
                }

                if (_pendingResumePos > 0)
                {
                    double totalDur = _player.DurationSeconds;
                    if (totalDur <= 0 || _pendingResumePos < totalDur - 5)
                    {
                        try { _player.PositionSeconds = _pendingResumePos; }
                        catch { }
                    }
                    _pendingResumePos = 0;
                }
            }
            catch (Exception ex) { DebugLog("MediaOpened failed: " + ex.Message); }
        }

        private void VideoPlayer_MediaEnded(object sender, EventArgs e)
        {
            _isPlaying = false;
            UpdatePlayPauseIcon();

            if (_repeatMode == 2)
            {
                try { _player.PositionSeconds = 0; } catch { }
                _player.Play();
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
                _mediaEnded = true;
                _positionTimer.Stop();
                CurrentTimeText.Text = DurationText.Text;
                PositionSlider.Value = PositionSlider.Maximum;
                SyncPositionProgress();
            }
        }

        private void VideoPlayer_MediaFailed(object sender, EventArgs e)
        {
            ShowMediaUnsupported();
        }

        private void PositionTimer_Tick(object sender, object e)
        {
            if (_seeking) return;
            try
            {
                double total = _player.DurationSeconds;
                if (total > 0)
                {
                    if (Math.Abs(PositionSlider.Maximum - total) > 0.5)
                        PositionSlider.Maximum = total;
                    double pos = _player.PositionSeconds;
                    PositionSlider.Value = Math.Min(total, Math.Max(0, pos));
                }
                CurrentTimeText.Text = FormatTime(_player.PositionSeconds);
                SyncPositionProgress();
            }
            catch (Exception ex) { DebugLog("PositionTimer failed: " + ex.Message); }
        }

        private void SyncPositionProgress()
        {
            if (PositionProgress == null || PositionSlider == null) return;
            try
            {
                PositionProgress.Maximum = Math.Max(1, PositionSlider.Maximum);
                PositionProgress.Value = Math.Min(PositionSlider.Value, PositionProgress.Maximum);
            }
            catch { }
        }

        private void PositionSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            SyncPositionProgress();
            if (!_seeking) return;
            CurrentTimeText.Text = FormatTime(e.NewValue);
            try { _player.PositionSeconds = e.NewValue; }
            catch (Exception ex) { DebugLog("seek failed: " + ex.Message); }
        }

        private void PositionSlider_PointerPressed(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _seeking = true;
        }

        private void PositionSlider_PointerReleased(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _seeking = false;
            try
            {
                _player.PositionSeconds = PositionSlider.Value;
                CurrentTimeText.Text = FormatTime(PositionSlider.Value);
            }
            catch (Exception ex) { DebugLog("seek failed: " + ex.Message); }
        }

        private void VolumeSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_player == null) return;
            try { _player.Volume = Math.Max(0, Math.Min(1.0, e.NewValue / 100.0)); }
            catch (Exception ex) { DebugLog("volume failed: " + ex.Message); }
            try { if (VolumeProgress != null) VolumeProgress.Value = e.NewValue; }
            catch { }
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
            if (_mediaEnded)
            {
                _mediaEnded = false;
                try { _player.PositionSeconds = 0; } catch { }
                CurrentTimeText.Text = "00:00";
                PositionSlider.Value = 0;
            }
            else
            {
                try
                {
                    double total = _player.DurationSeconds;
                    if (total > 0 && _player.PositionSeconds >= total)
                    {
                        _player.PositionSeconds = 0;
                        CurrentTimeText.Text = "00:00";
                        PositionSlider.Value = 0;
                    }
                }
                catch { }
            }

            _player.Play();
            _isPlaying = true;
            if (_spectrum != null) _spectrum.SetPlaying(true);
            UpdatePlayPauseIcon();
            StartPositionTimer();
            SyncPositionProgress();
            ResetAutoHide();
        }

        private void PausePlayback()
        {
            if (!_isPlaying) return;
            _player.Pause();
            _isPlaying = false;
            if (_spectrum != null) _spectrum.SetPlaying(false);
            UpdatePlayPauseIcon();
            ShowControls();
        }

        private void StopBtn_Click(object sender, RoutedEventArgs e)
        {
            SaveResumePosition();
            _mediaEnded = false;
            _player.Stop();
            _isPlaying = false;
            _positionTimer.Stop();
            StopVisualizer();
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
                    _player.PositionSeconds = 0;
                    _player.Play();
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
                try { _player.PositionSeconds = 0; } catch { }
                _player.Play();
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

        private void SpeedBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isPhotoMode) return;
            _speedIndex = (_speedIndex + 1) % SPEEDS.Length;
            SpeedBtn.Content = SPEEDS[_speedIndex].ToString("0.0#x");
            try { _player.Rate = SPEEDS[_speedIndex]; }
            catch (Exception ex) { DebugLog("set speed failed: " + ex.Message); }
            ResetAutoHide();
        }

        #region Photo gestures / toolbar

        private void PhotoImage_ManipulationStarted(object sender, Windows.UI.Xaml.Input.ManipulationStartedRoutedEventArgs e)
        {
            _photoGesturing = true;
            _photoStartScaleX = PhotoTransform.ScaleX;
            _photoStartScaleY = PhotoTransform.ScaleY;
            _photoStartRotation = PhotoTransform.Rotation;
        }

        private void PhotoImage_ManipulationDelta(object sender, Windows.UI.Xaml.Input.ManipulationDeltaRoutedEventArgs e)
        {
            if (!_photoGesturing) return;
            PhotoTransform.ScaleX = Math.Max(0.4, _photoStartScaleX * e.Cumulative.Scale);
            PhotoTransform.ScaleY = Math.Max(0.4, _photoStartScaleY * e.Cumulative.Scale);
            if (e.Cumulative.Rotation != 0)
                PhotoTransform.Rotation = _photoStartRotation + e.Cumulative.Rotation;
        }

        private void PhotoImage_ManipulationCompleted(object sender, Windows.UI.Xaml.Input.ManipulationCompletedRoutedEventArgs e)
        {
            _photoGesturing = false;
            ResetAutoHide();
        }

        private void PhotoZoomInBtn_Click(object sender, RoutedEventArgs e)
        {
            PhotoTransform.ScaleX = Math.Min(8, PhotoTransform.ScaleX * 1.25);
            PhotoTransform.ScaleY = Math.Min(8, PhotoTransform.ScaleY * 1.25);
            ResetAutoHide();
        }

        private void PhotoZoomOutBtn_Click(object sender, RoutedEventArgs e)
        {
            PhotoTransform.ScaleX = Math.Max(0.4, PhotoTransform.ScaleX / 1.25);
            PhotoTransform.ScaleY = Math.Max(0.4, PhotoTransform.ScaleY / 1.25);
            ResetAutoHide();
        }

        private void PhotoRotateBtn_Click(object sender, RoutedEventArgs e)
        {
            PhotoTransform.Rotation = (PhotoTransform.Rotation + 90) % 360;
            ResetAutoHide();
        }

        private void PhotoResetBtn_Click(object sender, RoutedEventArgs e)
        {
            PhotoTransform.ScaleX = 1;
            PhotoTransform.ScaleY = 1;
            PhotoTransform.Rotation = 0;
            ResetAutoHide();
        }

        #endregion

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            SaveResumePosition();
            _player.Stop();
            if (Frame != null && Frame.CanGoBack)
                Frame.GoBack();
            else
                Frame.Navigate(typeof(HomePage));
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            SaveResumePosition();
            try { _player.Stop(); } catch { }
            _isPlaying = false;
            _positionTimer.Stop();
            _autoHideTimer.Stop();
            _visTimer.Stop();
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
                double total = _player.DurationSeconds;
                if (total <= 5) return;
                double pos = _player.PositionSeconds;

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
            if (IsInBars(e.OriginalSource)) return;
            ToggleControls();
        }

        private bool IsInBars(object source)
        {
            var el = source as FrameworkElement;
            while (el != null && el != this)
            {
                if (el == TopBar || el == BottomBar || el == PhotoBar || el == NoSupportText)
                    return true;
                el = el.Parent as FrameworkElement;
            }
            return false;
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
            SyncPositionProgress();
            if (VolumeProgress != null) VolumeProgress.Value = VolumeSlider.Value;
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
            PlayPauseBtn.Content = _isPlaying ? "❚❚" : "▶";
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

        #region Visualizer

        private void StartVisualizer()
        {
            try
            {
                if (_spectrum == null) _spectrum = new SpectrumEngine();
                if (_renderer == null) _renderer = _visStyles[0];

                VisualizerImage.Visibility = Visibility.Visible;
                VisHintText.Visibility = Visibility.Visible;
                _spectrum.SetPlaying(true);
                _visTimer.Start();
            }
            catch (Exception ex) { DebugLog("StartVisualizer failed: " + ex.Message); }
        }

        private void StopVisualizer()
        {
            _visTimer.Stop();
            VisualizerImage.Visibility = Visibility.Collapsed;
            VisHintText.Visibility = Visibility.Collapsed;
            _visPixels = null;
            _visBitmap = null;
        }

        private void VisTimer_Tick(object sender, object e)
        {
            try
            {
                if (_spectrum == null) return;
                int w = (int)Math.Max(1, Math.Round(Window.Current.Bounds.Width / 2));
                int h = (int)Math.Max(1, Math.Round(Window.Current.Bounds.Height / 2));

                if (_visBitmap == null || _visBitmap.PixelWidth != w || _visBitmap.PixelHeight != h)
                {
                    _visBitmap = new WriteableBitmap(w, h);
                    _visPixels = new byte[w * h * 4];
                    VisualizerImage.Source = _visBitmap;
                }

                var data = _spectrum.Next(0);
                if (_spectrum != null)
                {
                    float drive = _isPlaying
                        ? (float)(0.35 + _player.Volume * 0.65)
                        : 0.08f;
                    _spectrum.SetDrive(drive);
                }
                var px = _visPixels;
                _renderer.Render(px, w, h, data, data.BeatPulse, VIS_THEME, DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond);

                using (var stream = _visBitmap.PixelBuffer.AsStream())
                {
                    stream.Seek(0, System.IO.SeekOrigin.Begin);
                    stream.Write(px, 0, px.Length);
                }
                _visBitmap.Invalidate();
            }
            catch (Exception ex) { DebugLog("VisTimer_Tick failed: " + ex.Message); }
        }

        private void CycleVisualizer()
        {
            _visIndex = (_visIndex + 1) % _visStyles.Length;
            _renderer = _visStyles[_visIndex];
        }

        private void VisualizerImage_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            e.Handled = true;
            ToggleControls();
        }

        private void TapSurface_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (IsInBars(e.OriginalSource)) return;
            e.Handled = true;
            ToggleControls();
        }

        private void TapSurface_DoubleTapped(object sender, Windows.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if (VisualizerImage.Visibility == Visibility.Visible)
            {
                e.Handled = true;
                CycleVisualizer();
                ResetAutoHide();
            }
        }

        #endregion

        private static void DebugLog(string message)
        {
            System.Diagnostics.Debug.WriteLine("[HyperMedia.WP] " + message);
        }
    }
}
