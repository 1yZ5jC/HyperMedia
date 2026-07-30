using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using libVLCX;

namespace HyperMedia
{
    public sealed partial class MainPage : Page
    {
        private const int AUTO_HIDE_DELAY_MS = 3000;

        private DispatcherTimer _autoHideTimer;
        private DispatcherTimer _positionTimer;
        private bool _isPlaying;
        private bool _isSeeking;
        private bool _isFullscreen;
        private double _duration;
        private StorageFile _tempFile;
        private string _originalFileName;

        private Instance _vlcInstance;
        private MediaPlayer _vlcPlayer;
        private Media _vlcMedia;
        private string _vlcInitError;

        // Swipe gesture
        private double _swipeStartX;
        private bool _isSwiping;

        // Playlist
        private List<StorageFile> _playlist = new List<StorageFile>();
        private int _playlistIndex = -1;

        // Repeat: 0=off, 1=all, 2=one
        private int _repeatMode = 0;
        private bool _shuffleOn = false;
        private double _playbackSpeed = 1.0;
        private Random _shuffleRandom = new Random();

        public MainPage()
        {
            this.InitializeComponent();

            _autoHideTimer = new DispatcherTimer();
            _autoHideTimer.Interval = TimeSpan.FromMilliseconds(AUTO_HIDE_DELAY_MS);
            _autoHideTimer.Tick += AutoHideTimer_Tick;

            _positionTimer = new DispatcherTimer();
            _positionTimer.Interval = TimeSpan.FromMilliseconds(250);
            _positionTimer.Tick += PositionTimer_Tick;

            this.Loaded += (s, e) => InitLibVlc();
            ShowControls();
        }

        private void InitLibVlc()
        {
            try
            {
                _vlcInstance = new Instance(MakeVlcArgs(), VlcVideoPanel);
                _vlcInstance.setUserAgent("HyperMedia/1.0", "HyperMedia/1.0");
                _vlcInstance.UpdateSize(
                    (float)VlcVideoPanel.ActualWidth * VlcVideoPanel.CompositionScaleX,
                    (float)VlcVideoPanel.ActualHeight * VlcVideoPanel.CompositionScaleY);
                VlcVideoPanel.CompositionScaleChanged += VlcVideoPanel_CompositionScaleChanged;
                _vlcInitError = null;
                Debug.WriteLine("[HyperMedia] libVLCX initialized");
            }
            catch (Exception ex)
            {
                _vlcInitError = "VLC init: " + ex.Message;
                StatusText.Text = _vlcInitError;
                Debug.WriteLine("[HyperMedia] libVLCX init failed: {0}", ex);
            }
        }

        private static List<string> MakeVlcArgs()
        {
            var args = new List<string>
            {
                "-I", "dummy",
                "--no-plugins-cache",
                "--no-osd",
                "--no-stats",
                "--no-loop",
                "--no-video-title-show",
                "--drop-late-frames",
                "--avcodec-hw=any",
                "--aout=winstore",
                "--no-keyboard-events",
                "--no-mouse-events"
            };
            try
            {
                var pkg = Windows.ApplicationModel.Package.Current;
                if (pkg != null)
                {
                    string pluginPath = pkg.InstalledLocation.Path + "\\plugins";
                    args.Add("--plugin-path=" + pluginPath);
                }
            }
            catch { }
            return args;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Window.Current.CoreWindow.PointerEntered += CoreWindow_PointerEntered;

            if (StorageApplicationPermissions.FutureAccessList.ContainsItem("PlaybackFile"))
            {
                try
                {
                    StorageFile file = await StorageApplicationPermissions.FutureAccessList.GetFileAsync("PlaybackFile");
                    StorageApplicationPermissions.FutureAccessList.Remove("PlaybackFile");

                    _playlist.Clear();
                    _playlist.Add(file);

                    var settings = ApplicationData.Current.LocalSettings;
                    if (settings.Values.ContainsKey("PlaylistExtras"))
                    {
                        string extras = settings.Values["PlaylistExtras"] as string;
                        settings.Values.Remove("PlaylistExtras");
                        if (!string.IsNullOrEmpty(extras))
                        {
                            string[] paths = extras.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (string path in paths)
                            {
                                try
                                {
                                    var extraFile = await StorageFile.GetFileFromPathAsync(path);
                                    _playlist.Add(extraFile);
                                }
                                catch { }
                            }
                        }
                    }

                    _playlistIndex = 0;
                    OpenFile(_playlist[0]);
                }
                catch { }
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            StopPlayback();
            Window.Current.CoreWindow.PointerEntered -= CoreWindow_PointerEntered;
        }

        private void CoreWindow_PointerEntered(CoreWindow sender, PointerEventArgs args)
        {
            Focus(FocusState.Programmatic);
            ShowControls();
        }

        #region File Open

        private async void OpenFile(StorageFile file)
        {
            if (file == null) return;

            StopPlayback();
            WelcomeScreen.Visibility = Visibility.Collapsed;
            ShowOverlay("Loading " + file.Name + "...");

            _originalFileName = file.Name;
            PlayHistory.Add(file.Path, file.Name);

            try
            {
                StatusText.Text = "Preparing...";
                var sw = Stopwatch.StartNew();

                var tempFolder = ApplicationData.Current.TemporaryFolder;
                var tempFile = await file.CopyAsync(tempFolder, "hypermedia_temp" + file.FileType,
                    NameCollisionOption.ReplaceExisting);
                _tempFile = tempFile;

                OpenWithLibVlc(tempFile, sw);
            }
            catch (Exception ex)
            {
                HideOverlay();
                StatusText.Text = "Error: " + ex.Message;
                Debug.WriteLine("[HyperMedia] ERROR: {0}", ex);
            }
        }

        private void OpenWithLibVlc(StorageFile tempFile, Stopwatch sw)
        {
            if (_vlcInstance == null)
            {
                if (_vlcInitError != null)
                    StatusText.Text = _vlcInitError;
                else
                    StatusText.Text = "Error: VLC not initialized";
                HideOverlay();
                Debug.WriteLine("[HyperMedia] VLC instance not available");
                return;
            }

            FileNameText.Text = _originalFileName ?? tempFile.Name;
            UpdatePlaylistCounter();

            try
            {
                _vlcMedia = new Media(_vlcInstance, tempFile.Path, FromType.FromPath);
                _vlcMedia.addOption(":avcodec-hw=d3d11va");
                _vlcPlayer = new MediaPlayer(_vlcInstance);
                _vlcPlayer.setMedia(_vlcMedia);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Error: " + ex.Message;
                HideOverlay();
                Debug.WriteLine("[HyperMedia] VLC media/player create failed: {0}", ex.Message);
                return;
            }

            try
            {
                string audioDeviceId = Windows.Media.Devices.MediaDevice.GetDefaultAudioRenderId(
                    Windows.Media.Devices.AudioDeviceRole.Default);
                if (!string.IsNullOrEmpty(audioDeviceId))
                    _vlcPlayer.outputDeviceSet(audioDeviceId);
            }
            catch { }

            _vlcPlayer.eventManager().OnPlaying += OnVlcPlaying;
            _vlcPlayer.eventManager().OnPaused += OnVlcPaused;
            _vlcPlayer.eventManager().OnStopped += OnVlcStopped;
            _vlcPlayer.eventManager().OnEndReached += OnVlcEndReached;
            _vlcPlayer.eventManager().OnEncounteredError += OnVlcEncounteredError;
            _vlcPlayer.eventManager().OnLengthChanged += OnVlcLengthChanged;

            _vlcPlayer.play();

            if (_playbackSpeed != 1.0)
            {
                try
                {
                    _vlcPlayer.setRate((float)_playbackSpeed);
                    Debug.WriteLine("[HyperMedia] Set rate to {0}", _playbackSpeed);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[HyperMedia] setRate failed: {0}", ex.Message);
                }
            }

            _isPlaying = true;
            _positionTimer.Start();
            UpdatePlayPauseIcon(true);
            StatusText.Text = "";
            HideOverlay();
            ResetAutoHide();

            Debug.WriteLine("[HyperMedia] libVLC playback started: {0}ms", sw.ElapsedMilliseconds);
        }

        private async void OpenFileFromPicker()
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
            string[] extensions = {
                ".mp4", ".avi", ".mkv", ".webm", ".flv", ".mov", ".wmv",
                ".mp3", ".flac", ".wav", ".aac", ".ogg", ".wma", ".m4a",
                ".3gp", ".ts", ".mka", ".opus"
            };
            foreach (var ext in extensions)
                picker.FileTypeFilter.Add(ext);

            var files = await picker.PickMultipleFilesAsync();
            if (files != null && files.Count > 0)
            {
                _playlist.Clear();
                foreach (var f in files)
                    _playlist.Add(f);
                _playlistIndex = 0;

                if (_shuffleOn)
                    ShufflePlaylistFromCurrent();

                OpenFile(_playlist[0]);
            }
        }

        private void WelcomeOpenButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileFromPicker();
        }

        #endregion

        #region Playlist

        private void PlayNext()
        {
            if (_playlist.Count == 0) return;

            if (_repeatMode == 2)
            {
                // Repeat one
                OpenFile(_playlist[_playlistIndex]);
                return;
            }

            int nextIndex = _playlistIndex + 1;

            if (nextIndex >= _playlist.Count)
            {
                if (_repeatMode == 1)
                {
                    // Repeat all
                    nextIndex = 0;
                }
                else
                {
                    // No repeat, stop
                    StopPlayback();
                    WelcomeScreen.Visibility = Visibility.Visible;
                    FileNameText.Text = "";
                    StatusText.Text = "Finished";
                    return;
                }
            }

            _playlistIndex = nextIndex;
            OpenFile(_playlist[_playlistIndex]);
        }

        private void PlayPrev()
        {
            if (_playlist.Count == 0) return;

            // If more than 3s into the track, restart it
            if (_vlcPlayer != null)
            {
                long time = _vlcPlayer.time();
                if (time > 3000)
                {
                    OpenFile(_playlist[_playlistIndex]);
                    return;
                }
            }

            int prevIndex = _playlistIndex - 1;
            if (prevIndex < 0)
            {
                if (_repeatMode == 1)
                    prevIndex = _playlist.Count - 1;
                else
                    prevIndex = 0;
            }

            _playlistIndex = prevIndex;
            OpenFile(_playlist[_playlistIndex]);
        }

        private void ShufflePlaylistFromCurrent()
        {
            if (_playlist.Count <= 1) return;
            var current = _playlist[_playlistIndex];
            var rest = _playlist.Where((f, i) => i != _playlistIndex).ToList();
            var shuffled = new List<StorageFile> { current };
            int n = rest.Count;
            while (n > 1)
            {
                int k = _shuffleRandom.Next(n);
                var tmp = rest[k];
                rest[k] = rest[n - 1];
                rest[n - 1] = tmp;
                n--;
            }
            shuffled.AddRange(rest);
            _playlist = shuffled;
            _playlistIndex = 0;
        }

        private void UpdatePlaylistCounter()
        {
            if (_playlist.Count > 1)
                PlaylistCounter.Text = string.Format("({0}/{1})", _playlistIndex + 1, _playlist.Count);
            else
                PlaylistCounter.Text = "";
        }

        #endregion

        #region libVLCX Events

        private void OnVlcPlaying()
        {
            _isPlaying = true;

            BeginInvokeOnUI(() =>
            {
                try
                {
                    if (_vlcMedia != null)
                    {
                        string title = _vlcMedia.meta(MediaMeta.Title);
                        if (!string.IsNullOrEmpty(title))
                        {
                            FileNameText.Text = title;
                        }
                        else
                        {
                            string artist = _vlcMedia.meta(MediaMeta.Artist);
                            string nowPlaying = _vlcMedia.meta(MediaMeta.NowPlaying);
                            if (!string.IsNullOrEmpty(artist) && !string.IsNullOrEmpty(nowPlaying))
                            {
                                FileNameText.Text = artist + " - " + nowPlaying;
                            }
                        }
                    }
                }
                catch { }
            });
        }

        private void OnVlcPaused()
        {
            _isPlaying = false;
        }

        private void OnVlcStopped()
        {
            _isPlaying = false;
            BeginInvokeOnUI(() => _positionTimer.Stop());
        }

        private void OnVlcEndReached()
        {
            _isPlaying = false;
            BeginInvokeOnUI(() =>
            {
                _positionTimer.Stop();
                UpdatePlayPauseIcon(false);

                if (_playlist.Count > 1)
                {
                    PlayNext();
                }
                else
                {
                    StatusText.Text = "Finished";
                    ShowControls();
                }
            });
        }

        private void OnVlcEncounteredError()
        {
            _isPlaying = false;
            BeginInvokeOnUI(() =>
            {
                _positionTimer.Stop();
                StatusText.Text = "Playback error";
                ShowControls();
            });
        }

        private void OnVlcLengthChanged(long length)
        {
            if (length > 0)
            {
                _duration = length / 1000.0;
                BeginInvokeOnUI(() =>
                {
                    DurationText.Text = FormatTime(_duration);
                    PositionSlider.Maximum = _duration;
                });
            }
        }

        private void VlcVideoPanel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_vlcInstance != null)
            {
                var panel = (SwapChainPanel)sender;
                _vlcInstance.UpdateSize(
                    (float)e.NewSize.Width * panel.CompositionScaleX,
                    (float)e.NewSize.Height * panel.CompositionScaleY);
            }
        }

        private void VlcVideoPanel_CompositionScaleChanged(SwapChainPanel sender, object args)
        {
            if (_vlcInstance != null)
            {
                _vlcInstance.UpdateScale(sender.CompositionScaleX, sender.CompositionScaleY);
                _vlcInstance.UpdateSize(
                    (float)sender.ActualWidth * sender.CompositionScaleX,
                    (float)sender.ActualHeight * sender.CompositionScaleY);
            }
        }

        private async void BeginInvokeOnUI(Action action)
        {
            var disp = Dispatcher;
            if (disp != null)
                await disp.RunAsync(CoreDispatcherPriority.Normal, () => action());
        }

        #endregion

        #region Playback Control

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            TogglePlayPause();
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopPlayback();
            WelcomeScreen.Visibility = Visibility.Visible;
            FileNameText.Text = "";
            StatusText.Text = "Ready";
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.GoBack();
        }

        private void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            PlayPrev();
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            PlayNext();
        }

        private void ResumePlayback()
        {
            if (_isPlaying) return;
            if (_vlcPlayer == null) return;

            _isPlaying = true;
            _vlcPlayer.play();
            _positionTimer.Start();
            UpdatePlayPauseIcon(true);
            ResetAutoHide();
        }

        private void PausePlayback()
        {
            if (!_isPlaying) return;

            _isPlaying = false;
            _vlcPlayer?.pause();
            _positionTimer.Stop();
            UpdatePlayPauseIcon(false);
            ShowControls();
        }

        private void TogglePlayPause()
        {
            if (_isPlaying)
                PausePlayback();
            else
                ResumePlayback();
        }

        private void StopPlayback()
        {
            _autoHideTimer.Stop();
            _positionTimer.Stop();
            _isPlaying = false;
            _isSeeking = false;

            if (_vlcPlayer != null)
            {
                _vlcPlayer.stop();
                _vlcPlayer = null;
            }
            _vlcMedia = null;

            if (_tempFile != null)
            {
                try { _tempFile.DeleteAsync().AsTask().Wait(TimeSpan.FromSeconds(3)); }
                catch { }
                _tempFile = null;
            }

            PositionSlider.Value = 0;
            CurrentTimeText.Text = "00:00";
            DurationText.Text = "00:00";
            UpdatePlayPauseIcon(false);
            HideOverlay();
            _duration = 0;
        }

        private void UpdatePlayPauseIcon(bool playing)
        {
            PlayPauseIcon.Text = playing ? "\u23F8" : "\u25B6";
        }

        #endregion

        #region Position Tracking

        private void PositionTimer_Tick(object sender, object e)
        {
            if (_vlcPlayer == null) return;
            long time = _vlcPlayer.time();
            double sec = time / 1000.0;
            if (_duration <= 0)
            {
                long len = _vlcPlayer.length();
                if (len > 0)
                    _duration = len / 1000.0;
            }
            if (sec > 0)
            {
                if (_duration > 0)
                {
                    PositionSlider.Value = sec;
                    CurrentTimeText.Text = FormatTime(sec);
                }
                else
                {
                    CurrentTimeText.Text = FormatTime(sec);
                }
            }
        }

        #endregion

        #region Slider

        private void PositionSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isSeeking) return;
            if (_duration <= 0) return;
            if (Math.Abs(e.NewValue - e.OldValue) < 1.0) return;
            SeekTo(e.NewValue);
        }

        private void PositionSlider_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
        {
            _isSeeking = true;
        }

        private void PositionSlider_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            double seekTo = PositionSlider.Value;
            _isSeeking = false;
            SeekTo(seekTo);
        }

        private void SeekTo(double seconds)
        {
            if (_duration <= 0) return;
            _vlcPlayer?.setTime((long)(seconds * 1000));
            CurrentTimeText.Text = FormatTime(seconds);
        }

        private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            int vol = (int)e.NewValue;
            _vlcPlayer?.setVolume(vol);
            UpdateVolumeIcon(vol);
        }

        private void MuteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_vlcPlayer == null) return;

            if (VolumeSlider.Value > 0)
            {
                VolumeSlider.Tag = VolumeSlider.Value;
                VolumeSlider.Value = 0;
            }
            else
            {
                double prev = 100;
                if (VolumeSlider.Tag is double)
                    prev = (double)VolumeSlider.Tag;
                VolumeSlider.Value = prev;
            }
        }

        private void UpdateVolumeIcon(int vol)
        {
            if (VolumeIcon == null) return;
            if (vol == 0)
                VolumeIcon.Text = "\u2716";
            else if (vol < 33)
                VolumeIcon.Text = "\u1F507";
            else if (vol < 66)
                VolumeIcon.Text = "\u1F509";
            else
                VolumeIcon.Text = "\u1F50A";
        }

        #endregion

        #region Speed

        private void SpeedButton_Click(object sender, RoutedEventArgs e)
        {
            double[] speeds = { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };
            int current = Array.IndexOf(speeds, _playbackSpeed);
            if (current < 0) current = 2;
            int next = (current + 1) % speeds.Length;
            _playbackSpeed = speeds[next];

            if (_vlcPlayer != null)
            {
                try
                {
                    _vlcPlayer.setRate((float)_playbackSpeed);
                    Debug.WriteLine("[HyperMedia] Speed changed to {0}x", _playbackSpeed);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[HyperMedia] setRate failed: {0}", ex.Message);
                }
            }

            SpeedIcon.Text = _playbackSpeed == 1.0 ? "1x" : _playbackSpeed + "x";
        }

        #endregion

        #region Repeat / Shuffle

        private void RepeatButton_Click(object sender, RoutedEventArgs e)
        {
            _repeatMode = (_repeatMode + 1) % 3;
            UpdateRepeatIcon();
        }

        private void UpdateRepeatIcon()
        {
            switch (_repeatMode)
            {
                case 0:
                    RepeatIcon.Text = "\u1F501";
                    RepeatIcon.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(
                        Windows.UI.Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
                    break;
                case 1:
                    RepeatIcon.Text = "\u1F501";
                    RepeatIcon.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(
                        Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x40, 0xFB));
                    break;
                case 2:
                    RepeatIcon.Text = "\u1F502";
                    RepeatIcon.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(
                        Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x40, 0xFB));
                    break;
            }
        }

        private void ShuffleButton_Click(object sender, RoutedEventArgs e)
        {
            _shuffleOn = !_shuffleOn;

            if (_shuffleOn)
            {
                ShuffleIcon.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x40, 0xFB));
                if (_playlist.Count > 1)
                    ShufflePlaylistFromCurrent();
            }
            else
            {
                ShuffleIcon.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
            }
        }

        #endregion

        #region Fullscreen

        private void ToggleFullscreen()
        {
            _isFullscreen = !_isFullscreen;
            FullscreenIcon.Text = _isFullscreen ? "\u2716" : "\u26F6";

            if (_isFullscreen)
            {
                TopBar.Visibility = Visibility.Collapsed;
                BottomBar.Visibility = Visibility.Collapsed;
            }
            else
            {
                ShowControls();
            }
        }

        private void FullscreenButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleFullscreen();
        }

        private void VideoArea_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            ToggleFullscreen();
        }

        #endregion

        #region Keyboard

        private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            bool handled = true;

            switch (e.Key)
            {
                case VirtualKey.Space:
                    TogglePlayPause();
                    break;
                case VirtualKey.Left:
                    SeekRelative(-10);
                    break;
                case VirtualKey.Right:
                    SeekRelative(10);
                    break;
                case VirtualKey.Up:
                    AdjustVolume(5);
                    break;
                case VirtualKey.Down:
                    AdjustVolume(-5);
                    break;
                case VirtualKey.F:
                    ToggleFullscreen();
                    break;
                case VirtualKey.Escape:
                    if (_isFullscreen) ToggleFullscreen();
                    break;
                case VirtualKey.N:
                    if ((Window.Current.CoreWindow.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0)
                        PlayNext();
                    else
                        handled = false;
                    break;
                case VirtualKey.P:
                    if ((Window.Current.CoreWindow.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0)
                        PlayPrev();
                    else
                        handled = false;
                    break;
                case VirtualKey.O:
                    if ((Window.Current.CoreWindow.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0)
                        OpenFileFromPicker();
                    else
                        handled = false;
                    break;
                default:
                    handled = false;
                    break;
            }

            e.Handled = handled;
        }

        private void SeekRelative(double seconds)
        {
            if (_duration <= 0) return;

            double current = _vlcPlayer?.time() / 1000.0 ?? 0;
            double target = current + seconds;
            if (target < 0) target = 0;
            if (target > _duration) target = _duration;

            SeekTo(target);
        }

        private void AdjustVolume(double delta)
        {
            double newVal = VolumeSlider.Value + delta;
            if (newVal < 0) newVal = 0;
            if (newVal > 100) newVal = 100;
            VolumeSlider.Value = newVal;
        }

        #endregion

        #region Swipe Gestures

        private void VideoArea_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
        {
            _swipeStartX = e.Position.X;
            _isSwiping = false;
        }

        private void VideoArea_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            double deltaX = e.Cumulative.Translation.X;
            if (Math.Abs(deltaX) > 50)
            {
                _isSwiping = true;
                SwipeIndicator.Visibility = Visibility.Visible;
            }
        }

        private void VideoArea_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            SwipeIndicator.Visibility = Visibility.Collapsed;

            if (_isSwiping)
            {
                double deltaX = e.Cumulative.Translation.X;
                if (deltaX > 100)
                    SeekRelative(-30);
                else if (deltaX < -100)
                    SeekRelative(30);
            }

            _isSwiping = false;
        }

        #endregion

        #region Mouse / Touch Interaction

        private void VideoArea_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            if (source != null)
            {
                while (source != null)
                {
                    if (source is Button || source is Slider)
                        return;
                    source = VisualTreeHelper.GetParent(source);
                }
            }

            if (!_isPlaying && _duration <= 0) return;
            TogglePlayPause();
            ShowControls();
        }

        private void Page_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            ShowControls();
        }

        #endregion

        #region Auto-Hide Controls

        private void ShowControls()
        {
            TopBar.Visibility = Visibility.Visible;
            BottomBar.Visibility = Visibility.Visible;
            ResetAutoHide();
        }

        private void HideControls()
        {
            if (!_isPlaying && _duration <= 0) return;
            if (_isSeeking) return;

            TopBar.Visibility = Visibility.Collapsed;
            BottomBar.Visibility = Visibility.Collapsed;
        }

        private void ResetAutoHide()
        {
            _autoHideTimer.Stop();
            if (_isPlaying)
                _autoHideTimer.Start();
        }

        private void AutoHideTimer_Tick(object sender, object e)
        {
            _autoHideTimer.Stop();
            if (_isPlaying)
                HideControls();
        }

        #endregion

        #region Drag & Drop

        private void Page_DragOver(object sender, DragEventArgs e)
        {
        }

        private async void Page_Drop(object sender, DragEventArgs e)
        {
            try
            {
                var view = e.Data.GetView();
                if (view.Contains(StandardDataFormats.StorageItems))
                {
                    var items = await view.GetStorageItemsAsync();
                    if (items.Count > 0)
                    {
                        _playlist.Clear();
                        foreach (var item in items)
                        {
                            var file = item as StorageFile;
                            if (file != null)
                                _playlist.Add(file);
                        }
                        _playlistIndex = 0;
                        OpenFile(_playlist[0]);
                    }
                }
            }
            catch { }
        }

        #endregion

        #region Overlay

        private void ShowOverlay(string text)
        {
            OverlayText.Text = text;
            OverlayText.Visibility = Visibility.Visible;
        }

        private void HideOverlay()
        {
            OverlayText.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region Utility

        private string FormatTime(double seconds)
        {
            if (seconds < 0) seconds = 0;
            var ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 1)
                return ts.ToString(@"hh\:mm\:ss");
            return ts.ToString(@"mm\:ss");
        }

        #endregion
    }
}
