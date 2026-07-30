using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private bool _controlsVisible;
        private double _duration;
        private StorageFile _tempFile;

        private Instance _vlcInstance;
        private MediaPlayer _vlcPlayer;
        private Media _vlcMedia;
        private string _vlcInitError;

        // Swipe gesture tracking
        private double _swipeStartX;
        private bool _isSwiping;

        public MainPage()
        {
            this.InitializeComponent();

            _autoHideTimer = new DispatcherTimer();
            _autoHideTimer.Interval = TimeSpan.FromMilliseconds(AUTO_HIDE_DELAY_MS);
            _autoHideTimer.Tick += AutoHideTimer_Tick;

            _positionTimer = new DispatcherTimer();
            _positionTimer.Interval = TimeSpan.FromMilliseconds(250);
            _positionTimer.Tick += PositionTimer_Tick;

            this.Loaded += (s, e) =>
            {
                InitLibVlc();
            };

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
                    OpenFile(file);
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

            FileNameText.Text = tempFile.Name;

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

            _isPlaying = true;
            _positionTimer.Start();
            UpdatePlayPauseIcon(true);
            CenterPlayButton.Visibility = Visibility.Visible;
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

            StorageFile file = await picker.PickSingleFileAsync();
            OpenFile(file);
        }

        private void WelcomeOpenButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileFromPicker();
        }

        private void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileFromPicker();
        }

        #endregion

        #region libVLCX Events

        private void OnVlcPlaying()
        {
            _isPlaying = true;
        }

        private void OnVlcPaused()
        {
            _isPlaying = false;
        }

        private void OnVlcStopped()
        {
            _isPlaying = false;
            BeginInvokeOnUI(() =>
            {
                _positionTimer.Stop();
            });
        }

        private void OnVlcEndReached()
        {
            _isPlaying = false;
            BeginInvokeOnUI(() =>
            {
                _positionTimer.Stop();
                UpdatePlayPauseIcon(false);
                StatusText.Text = "Finished";
                ShowControls();
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
            CenterPlayButton.Visibility = Visibility.Collapsed;
            FileNameText.Text = "";
            StatusText.Text = "Ready";
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.GoBack();
        }

        private void ResumePlayback()
        {
            if (_isPlaying) return;

            _isPlaying = true;
            if (_vlcPlayer == null) { _isPlaying = false; return; }
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

            VideoImage.Visibility = Visibility.Collapsed;
            AudioPlayer.Visibility = Visibility.Collapsed;

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
            string glyph = playing ? "\u23F8" : "\u25B6";
            PlayPauseIcon.Text = glyph;
            CenterPlayIcon.Text = glyph;
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
                {
                    _duration = len / 1000.0;
                }
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
                case VirtualKey.O:
                    if ((Window.Current.CoreWindow.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0)
                        OpenFileFromPicker();
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

        #region Fullscreen

        private void ToggleFullscreen()
        {
            _isFullscreen = !_isFullscreen;
            FullscreenIcon.Text = _isFullscreen ? "\u2716" : "\u26F6";
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
                {
                    // Swipe right - previous (seek back 30s)
                    SeekRelative(-30);
                }
                else if (deltaX < -100)
                {
                    // Swipe left - next (seek forward 30s)
                    SeekRelative(30);
                }
            }

            _isSwiping = false;
        }

        #endregion

        #region Mouse / Touch Interaction

        private void VideoArea_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (!_isPlaying && _duration <= 0) return;
            TogglePlayPause();
            ShowControls();
        }

        private void CenterPlayButton_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (!_isPlaying && _duration <= 0) return;
            TogglePlayPause();
            ShowControls();
            e.Handled = true;
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
            _controlsVisible = true;
            ResetAutoHide();
        }

        private void HideControls()
        {
            if (!_isPlaying && _duration <= 0) return;
            if (_isSeeking) return;

            TopBar.Visibility = Visibility.Collapsed;
            BottomBar.Visibility = Visibility.Collapsed;
            _controlsVisible = false;
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
                        var file = items[0] as StorageFile;
                        if (file != null)
                        {
                            OpenFile(file);
                        }
                    }
                }
            }
            catch { }
        }

        #endregion

        #region MediaElement Events

        private void MediaPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            Debug.WriteLine("[HyperMedia] MediaFailed: {0}", e.ErrorMessage);
            StatusText.Text = "Error: " + e.ErrorMessage;
        }

        private void MediaPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("[HyperMedia] MediaEnded");
            _positionTimer.Stop();
            _isPlaying = false;
            UpdatePlayPauseIcon(false);
            StatusText.Text = "Finished";
            ShowControls();
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
