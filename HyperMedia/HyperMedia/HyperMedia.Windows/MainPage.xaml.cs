using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Popups;
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
        private bool _isNetworkStream;

        private Instance _vlcInstance;
        private MediaPlayer _vlcPlayer;
        private Media _vlcMedia;
        private string _vlcInitError;

        // Swipe
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

        // A-B repeat
        private double _abPointA = -1;
        private double _abPointB = -1;
        private bool _abActive = false;

        // Resume position
        private const string KEY_RESUME = "ResumePosition_";

        // Settings navigation flag
        private bool _navigatingToSettings = false;

        public MainPage()
        {
            this.InitializeComponent();

            _autoHideTimer = new DispatcherTimer();
            _autoHideTimer.Interval = TimeSpan.FromMilliseconds(AUTO_HIDE_DELAY_MS);
            _autoHideTimer.Tick += AutoHideTimer_Tick;

            _positionTimer = new DispatcherTimer();
            _positionTimer.Interval = TimeSpan.FromMilliseconds(250);
            _positionTimer.Tick += PositionTimer_Tick;

            // Apply settings
            VolumeSlider.Value = SettingsPage.GetDefaultVolume();

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

            // Register for suspension/resumption for SMTC
            try
            {
                Application.Current.Suspending += OnSmtcAppSuspended;
                Application.Current.Resuming += OnSmtcAppResumed;
            }
            catch { }

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
                    RestoreStateAfterSettings();
                    OpenFile(_playlist[0]);
                }
                catch { }
            }
        }

        private void RestoreStateAfterSettings()
        {
            try
            {
                var restore = ApplicationData.Current.LocalSettings;
                if (restore.Values.ContainsKey("Restore_Volume"))
                {
                    VolumeSlider.Value = (int)restore.Values["Restore_Volume"];
                    restore.Values.Remove("Restore_Volume");
                }
                if (restore.Values.ContainsKey("Restore_Speed"))
                {
                    _playbackSpeed = (double)restore.Values["Restore_Speed"];
                    restore.Values.Remove("Restore_Speed");
                }
                if (restore.Values.ContainsKey("Restore_RepeatMode"))
                {
                    _repeatMode = (int)restore.Values["Restore_RepeatMode"];
                    restore.Values.Remove("Restore_RepeatMode");
                }
                if (restore.Values.ContainsKey("Restore_Shuffle"))
                {
                    _shuffleOn = (bool)restore.Values["Restore_Shuffle"];
                    restore.Values.Remove("Restore_Shuffle");
                }
                UpdateRepeatIcon();
                UpdateShuffleIcon();
            }
            catch { }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            if (_navigatingToSettings)
            {
                _navigatingToSettings = false;
                return;
            }

            SaveResumePosition();
            StopPlayback();

            try
            {
                Application.Current.Suspending -= OnSmtcAppSuspended;
                Application.Current.Resuming -= OnSmtcAppResumed;
            }
            catch { }

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
            _isNetworkStream = false;
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

                OpenWithLibVlc(tempFile.Path, sw);
            }
            catch (Exception ex)
            {
                HideOverlay();
                StatusText.Text = "Error: " + ex.Message;
                Debug.WriteLine("[HyperMedia] ERROR: {0}", ex);
            }
        }

        private void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;

            StopPlayback();
            _isNetworkStream = true;
            WelcomeScreen.Visibility = Visibility.Collapsed;
            ShowOverlay("Loading stream...");

            _originalFileName = url;
            StatusText.Text = "Connecting...";
            var sw = Stopwatch.StartNew();
            OpenWithLibVlc(url, sw);
        }

        private void OpenWithLibVlc(string mediaPath, Stopwatch sw)
        {
            if (_vlcInstance == null)
            {
                if (_vlcInitError != null)
                    StatusText.Text = _vlcInitError;
                else
                    StatusText.Text = "Error: VLC not initialized";
                HideOverlay();
                return;
            }

            FileNameText.Text = _originalFileName ?? System.IO.Path.GetFileName(mediaPath);
            UpdatePlaylistCounter();

            try
            {
                if (_isNetworkStream)
                    _vlcMedia = new Media(_vlcInstance, mediaPath, FromType.FromLocation);
                else
                    _vlcMedia = new Media(_vlcInstance, mediaPath, FromType.FromPath);

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
            StatusText.Text = "";
            HideOverlay();
            ResetAutoHide();

            if (_playbackSpeed != 1.0)
            {
                try { _vlcPlayer.setRate((float)_playbackSpeed); }
                catch { }
            }

            StartSmtcSync();
            SyncSmtcMetadata();

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

        private async void UrlButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new MessageDialog("Enter the URL of a media stream to play.", "Open URL");
            dialog.Commands.Add(new UICommand("Open", null, "open"));
            dialog.Commands.Add(new UICommand("Cancel", null, "cancel"));
            var result = await dialog.ShowAsync();
            // MessageDialog doesn't support text input in Win8.1
            // For URL input, use keyboard shortcut Ctrl+U or the button will open a TextBox overlay
            ShowUrlInputOverlay();
        }

        private void ShowUrlInputOverlay()
        {
            var popup = new Popup();

            var border = new Border();
            border.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xF0, 0x0A, 0x0A, 0x0F));
            border.Width = 500;
            border.Padding = new Thickness(24);

            var panel = new StackPanel();

            var title = new TextBlock();
            title.Text = "OPEN URL";
            title.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
            title.FontSize = 14;
            title.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x40, 0xFB));
            title.Margin = new Thickness(0, 0, 0, 16);
            panel.Children.Add(title);

            var textBox = new TextBox();
            textBox.PlaceholderText = "http://example.com/video.mp4";
            textBox.Width = 450;
            textBox.Height = 36;
            textBox.FontSize = 14;
            panel.Children.Add(textBox);

            var btnPanel = new StackPanel();
            btnPanel.Orientation = Windows.UI.Xaml.Controls.Orientation.Horizontal;
            btnPanel.HorizontalAlignment = HorizontalAlignment.Right;
            btnPanel.Margin = new Thickness(0, 16, 0, 0);

            var cancelBtn = new Button();
            cancelBtn.Content = "Cancel";
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            cancelBtn.Click += (s, ev) => { popup.IsOpen = false; };
            btnPanel.Children.Add(cancelBtn);

            var playBtn = new Button();
            playBtn.Content = "Play";
            playBtn.Click += (s, ev) =>
            {
                string url = textBox.Text.Trim();
                popup.IsOpen = false;
                if (!string.IsNullOrEmpty(url))
                    OpenUrl(url);
            };
            btnPanel.Children.Add(playBtn);

            panel.Children.Add(btnPanel);
            border.Child = panel;

            popup.Child = border;
            popup.Width = 500;
            popup.Height = 160;

            // Position at center of screen
            var bounds = Window.Current.Bounds;
            popup.HorizontalOffset = (bounds.Width - 500) / 2;
            popup.VerticalOffset = (bounds.Height - 160) / 2;

            popup.IsOpen = true;
            textBox.Focus(FocusState.Programmatic);
        }

        #endregion

        #region Playlist

        private void PlayNext()
        {
            if (_playlist.Count == 0) return;
            if (!SettingsPage.GetAutoPlay() && _repeatMode == 0) return;

            if (_repeatMode == 2)
            {
                SaveResumePosition();
                OpenFile(_playlist[_playlistIndex]);
                return;
            }

            int nextIndex = _playlistIndex + 1;

            if (nextIndex >= _playlist.Count)
            {
                if (_repeatMode == 1)
                    nextIndex = 0;
                else
                {
                    StopPlayback();
                    WelcomeScreen.Visibility = Visibility.Visible;
                    FileNameText.Text = "";
                    StatusText.Text = "Finished";
                    return;
                }
            }

            SaveResumePosition();
            _playlistIndex = nextIndex;
            OpenFile(_playlist[_playlistIndex]);
        }

        private void PlayPrev()
        {
            if (_playlist.Count == 0) return;

            if (_vlcPlayer != null)
            {
                long time = _vlcPlayer.time();
                if (time > 3000)
                {
                    SaveResumePosition();
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

            SaveResumePosition();
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

        #region Resume Position

        private void SaveResumePosition()
        {
            if (!SettingsPage.GetResumeEnabled()) return;
            if (_vlcPlayer == null || string.IsNullOrEmpty(_originalFileName)) return;
            try
            {
                long time = _vlcPlayer.time();
                long len = _vlcPlayer.length();
                if (time > 5000 && len > 10000)
                {
                    var settings = ApplicationData.Current.LocalSettings;
                    settings.Values[KEY_RESUME + _originalFileName] = time;
                }
            }
            catch { }
        }

        private long LoadResumePosition(string fileName)
        {
            if (!SettingsPage.GetResumeEnabled()) return 0;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.ContainsKey(KEY_RESUME + fileName))
                {
                    long pos = (long)settings.Values[KEY_RESUME + fileName];
                    settings.Values.Remove(KEY_RESUME + fileName);
                    return pos;
                }
            }
            catch { }
            return 0;
        }

        #endregion

        #region SMTC - System Media Transport Controls

        private SystemMediaTransportControls _smtc;
        private DispatcherTimer _smtcSyncTimer;

        private void InitSmtc()
        {
            try
            {
                _smtc = SystemMediaTransportControls.GetForCurrentView();
                _smtc.IsEnabled = true;
                _smtc.IsPlayEnabled = true;
                _smtc.IsPauseEnabled = true;
                _smtc.IsStopEnabled = true;
                _smtc.IsNextEnabled = _playlist.Count > 1;
                _smtc.IsPreviousEnabled = _playlist.Count > 1;

                _smtc.ButtonPressed += Smtc_ButtonPressed;

                _smtcSyncTimer = new DispatcherTimer();
                _smtcSyncTimer.Interval = TimeSpan.FromMilliseconds(1000);
                _smtcSyncTimer.Tick += SmtcSyncTimer_Tick;

                Debug.WriteLine("[HyperMedia] SMTC initialized");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HyperMedia] SMTC init failed: {0}", ex.Message);
            }
        }

        private async void Smtc_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                switch (args.Button)
                {
                    case SystemMediaTransportControlsButton.Play:
                        ResumePlayback();
                        SyncSmtcState();
                        break;
                    case SystemMediaTransportControlsButton.Pause:
                        PausePlayback();
                        SyncSmtcState();
                        break;
                    case SystemMediaTransportControlsButton.Stop:
                        StopPlayback();
                        WelcomeScreen.Visibility = Visibility.Visible;
                        FileNameText.Text = "";
                        StatusText.Text = "Ready";
                        break;
                    case SystemMediaTransportControlsButton.Next:
                        PlayNext();
                        break;
                    case SystemMediaTransportControlsButton.Previous:
                        PlayPrev();
                        break;
                }
            });
        }

        private void StartSmtcSync()
        {
            try
            {
                if (_smtc == null) InitSmtc();
                if (_smtc == null) return;

                SyncSmtcMetadata();
                _smtcSyncTimer.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HyperMedia] SMTC start failed: {0}", ex.Message);
            }
        }

        private void StopSmtcSync()
        {
            if (_smtcSyncTimer != null)
                _smtcSyncTimer.Stop();
            try
            {
                if (_smtc != null)
                    _smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
            }
            catch { }
        }

        private void SyncSmtcState()
        {
            if (_smtc == null) return;
            try
            {
                _smtc.PlaybackStatus = _isPlaying
                    ? MediaPlaybackStatus.Playing
                    : MediaPlaybackStatus.Paused;
            }
            catch { }
        }

        private void SyncSmtcMetadata()
        {
            if (_smtc == null) return;
            try
            {
                var updater = _smtc.DisplayUpdater;
                updater.Type = MediaPlaybackType.Music;
                updater.MusicProperties.Title = _originalFileName ?? "HyperMedia";
                updater.MusicProperties.Artist = "HyperMedia";
                updater.Update();

                _smtc.PlaybackStatus = _isPlaying
                    ? MediaPlaybackStatus.Playing
                    : MediaPlaybackStatus.Paused;
            }
            catch { }
        }

        private void SmtcSyncTimer_Tick(object sender, object e)
        {
            if (_vlcPlayer == null || _smtc == null) return;
            try
            {
                SyncSmtcState();
            }
            catch { }
        }

        private void OnSmtcAppSuspended(object sender, SuspendingEventArgs e)
        {
            if (_vlcPlayer != null && _isPlaying)
            {
                try
                {
                    _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
                }
                catch { }
            }
        }

        private void OnSmtcAppResumed(object sender, object e)
        {
            if (_smtc != null)
            {
                try
                {
                    SyncSmtcState();
                }
                catch { }
            }
        }

        #endregion

        #region A-B Repeat

        private void ToggleAbRepeat()
        {
            if (_abPointA < 0)
            {
                _abPointA = _vlcPlayer?.time() / 1000.0 ?? 0;
                _abPointB = -1;
                _abActive = false;
                AbRepeatIndicator.Visibility = Visibility.Visible;
                AbRepeatText.Text = "A: " + FormatTime(_abPointA);
            }
            else if (_abPointB < 0)
            {
                _abPointB = _vlcPlayer?.time() / 1000.0 ?? 0;
                if (_abPointB <= _abPointA)
                {
                    ClearAbRepeat();
                    return;
                }
                _abActive = true;
                AbRepeatText.Text = "A-B: " + FormatTime(_abPointA) + " → " + FormatTime(_abPointB);
            }
            else
            {
                ClearAbRepeat();
            }
        }

        private void ClearAbRepeat()
        {
            _abPointA = -1;
            _abPointB = -1;
            _abActive = false;
            AbRepeatIndicator.Visibility = Visibility.Collapsed;
        }

        private void CheckAbRepeat()
        {
            if (!_abActive || _vlcPlayer == null) return;
            double current = _vlcPlayer.time() / 1000.0;
            if (current >= _abPointB)
                _vlcPlayer.setTime((long)(_abPointA * 1000));
        }

        #endregion

        #region Screenshot

        private async void TakeScreenshot()
        {
            if (_vlcPlayer == null || _isNetworkStream) return;

            try
            {
                var folder = KnownFolders.PicturesLibrary;
                var subFolder = await folder.CreateFolderAsync("HyperMedia",
                    CreationCollisionOption.OpenIfExists);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = "Screenshot_" + timestamp + ".png";
                var file = await subFolder.CreateFileAsync(fileName,
                    CreationCollisionOption.GenerateUniqueName);

                ShowOverlay("Screenshot saved: " + file.Name);
                HideOverlayDelayed();
            }
            catch (Exception ex)
            {
                ShowOverlay("Screenshot error: " + ex.Message);
                HideOverlayDelayed();
            }
        }

        private async void HideOverlayDelayed()
        {
            await Task.Delay(2000);
            HideOverlay();
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

                    // Resume from saved position
                    if (_originalFileName != null && !_isNetworkStream)
                    {
                        long resumePos = LoadResumePosition(_originalFileName);
                        if (resumePos > 0)
                        {
                            _vlcPlayer.setTime(resumePos);
                            ShowOverlay("Resumed from " + FormatTime(resumePos / 1000.0));
                            HideOverlayDelayed();
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
                    PlayNext();
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
            SaveResumePosition();
            ClearAbRepeat();
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
            SyncSmtcState();
        }

        private void StopPlayback()
        {
            _autoHideTimer.Stop();
            _positionTimer.Stop();
            _isPlaying = false;
            _isSeeking = false;

            StopSmtcSync();

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

            CheckAbRepeat();
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
                try { _vlcPlayer.setRate((float)_playbackSpeed); }
                catch { }
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

        #region Subtitle / Audio Track / Snapshot

        private bool _subtitlesEnabled = true;

        private async void SubtitleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_vlcPlayer == null || _vlcMedia == null) return;

            try
            {
                var menu = new MenuFlyout();
                menu.Placement = PlacementMode.Bottom;

                var loadExternal = new MenuFlyoutItem();
                loadExternal.Text = "Load External Subtitle...";
                loadExternal.Tapped += async (s, ev) =>
                {
                    try
                    {
                        var picker = new FileOpenPicker();
                        picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
                        picker.FileTypeFilter.Add(".srt");
                        picker.FileTypeFilter.Add(".ass");
                        picker.FileTypeFilter.Add(".ssa");
                        picker.FileTypeFilter.Add(".sub");
                        picker.FileTypeFilter.Add(".vtt");

                        var file = await picker.PickSingleFileAsync();
                        if (file != null)
                        {
                            var tempFolder = ApplicationData.Current.TemporaryFolder;
                            var tempSub = await file.CopyAsync(tempFolder, "subtitle" + file.FileType,
                                NameCollisionOption.ReplaceExisting);

                            _vlcMedia.addOption(":sub-file=" + tempSub.Path);
                            ShowOverlay("Subtitle loaded: " + file.Name);
                            HideOverlayDelayed();
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowOverlay("Subtitle: " + ex.Message);
                        HideOverlayDelayed();
                    }
                };
                menu.Items.Add(loadExternal);

                var toggleSub = new MenuFlyoutItem();
                toggleSub.Text = _subtitlesEnabled ? "Disable Subtitles" : "Enable Subtitles";
                toggleSub.Tapped += (s, ev) =>
                {
                    try
                    {
                        _subtitlesEnabled = !_subtitlesEnabled;
                        if (_subtitlesEnabled)
                            _vlcMedia.addOption(":sub-track=0");
                        else
                            _vlcMedia.addOption(":no-spu");
                        ShowOverlay(_subtitlesEnabled ? "Subtitles ON" : "Subtitles OFF");
                        HideOverlayDelayed();
                    }
                    catch { }
                };
                menu.Items.Add(toggleSub);

                menu.ShowAt(SubtitleButton);
            }
            catch { }
        }

        private async void AudioTrackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_vlcPlayer == null) return;

            try
            {
                var dialog = new MessageDialog(
                    "Audio track selection requires libVLCX audio track API.\n" +
                    "Use VLC's built-in audio track selector if available.",
                    "Audio Track");
                dialog.Commands.Add(new UICommand("OK", null, "ok"));
                await dialog.ShowAsync();
            }
            catch { }
        }

        private void SnapshotButton_Click(object sender, RoutedEventArgs e)
        {
            TakeScreenshot();
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

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SaveStateForRestore();
            _navigatingToSettings = true;
            Frame.Navigate(typeof(SettingsPage));
        }

        private void SaveStateForRestore()
        {
            try
            {
                if (_playlist.Count > 0 && _playlistIndex >= 0 && _playlistIndex < _playlist.Count)
                {
                    var file = _playlist[_playlistIndex];
                    StorageApplicationPermissions.FutureAccessList.AddOrReplace("PlaybackFile", file);

                    if (_playlist.Count > 1)
                    {
                        var extras = new List<string>();
                        for (int i = 0; i < _playlist.Count; i++)
                        {
                            if (i != _playlistIndex)
                                extras.Add(_playlist[i].Path);
                        }
                        ApplicationData.Current.LocalSettings.Values["PlaylistExtras"] = string.Join("|", extras);
                    }

                    var restore = ApplicationData.Current.LocalSettings;
                    restore.Values["Restore_Volume"] = (int)VolumeSlider.Value;
                    restore.Values["Restore_Speed"] = _playbackSpeed;
                    restore.Values["Restore_RepeatMode"] = _repeatMode;
                    restore.Values["Restore_Shuffle"] = _shuffleOn;
                }

                SaveResumePosition();
            }
            catch { }
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
                case VirtualKey.B:
                    ToggleAbRepeat();
                    break;
                case VirtualKey.S:
                    TakeScreenshot();
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
                case VirtualKey.U:
                    if ((Window.Current.CoreWindow.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0)
                        ShowUrlInputOverlay();
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
            if (_isPlaying && SettingsPage.GetAutoHideEnabled())
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
