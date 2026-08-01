using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Media;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Popups;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;
using libVLCX;

namespace HyperMedia
{
    public sealed partial class MainPage : Page
    {
        private const int AUTO_HIDE_DELAY_MS_DEFAULT = 3000;
        private const int NOTIFY_TIMER_SECONDS = 4;
        private const int LYRIC_TIMER_INTERVAL_MS = 100;
        private const int PREV_TRACK_RESTART_MS = 3000;
        private const int RESUME_MIN_TIME_MS = 5000;
        private const int RESUME_MIN_LENGTH_MS = 10000;
        private const int SNAPSHOT_RETRY_COUNT = 20;
        private const int SNAPSHOT_RETRY_DELAY_MS = 150;
        private const double POSITION_TIMER_INTERVAL_MS = 250;
        private const int SMTC_SYNC_INTERVAL_MS = 1000;
        private const int OVERLAY_HIDE_DELAY_MS = 2000;
        private const int SLIDESHOW_INTERVAL_SECONDS = 3;

        private DispatcherTimer _autoHideTimer;
        private DispatcherTimer _positionTimer;
        private DispatcherTimer _overlayNotifyTimer;
        private DispatcherTimer _sleepTimer;
        private int _sleepRemainingSeconds;
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

        // Playlist
        private List<StorageFile> _playlist = new List<StorageFile>();
        private int _playlistIndex = -1;

        // Repeat: 0=off, 1=all, 2=one
        private int _repeatMode = 0;
        private bool _shuffleOn = false;
        private double _playbackSpeed = 1.0;
        private Random _shuffleRandom = new Random();
        private bool _autoAdvancing = false;

        // A-B repeat
        private double _abPointA = -1;
        private double _abPointB = -1;
        private bool _abActive = false;

        // A/V Sync
        private long _audioDelay = 0;
        private long _subtitleDelay = 0;
        private const long AV_SYNC_STEP = 50;

        // Aspect ratio
        private string[] _aspectRatios = { "", "16:9", "4:3", "1:1", "2.35:1" };
        private int _aspectRatioIndex = 0;
        private string[] _videoScales = { "-fit", "fill", "stretch", "crop", "scaled" };
        private float[] _videoScaleFactors = { 1.0f, -1.0f, 0.0f, 2.0f, 1.5f };
        private int _videoScaleIndex = 0;

        // Video rotation: 0=0°, 1=90°, 2=180°, 3=270°
        private int _videoRotation = 0;
        private static readonly string[] _rotationNames = { "0°", "90°", "180°", "270°" };

        // Crop geometry
        private string _cropGeometry = null;
        private string[] _cropGeometries = { null, "16:9", "4:3", "2.35:1", "16:10" };
        private string[] _cropNames = { "无", "16:9", "4:3", "2.35:1", "16:10" };
        private int _cropIndex = 0;

        // Night mode / loudness
        private bool _nightMode = false;
        private bool _recording = false;
        private string _recordingPath = null;
        private string _recordingFileName = null;

        // Equalizer
        private Equalizer _vlcEqualizer;

        // Video filters
        private float _videoBrightness = 1.0f;
        private float _videoContrast = 1.0f;
        private float _videoHue = 0f;
        private float _videoSaturation = 1.0f;
        private float _videoGamma = 1.0f;

        // Resume position
        private const string KEY_RESUME = "ResumePosition_";
        private const string KEY_LAST_FILE_PATH = "Resume_LastFilePath";
        private const string KEY_LAST_PLAYLIST = "Resume_LastPlaylist";
        private const string KEY_LAST_INDEX = "Resume_LastIndex";

        // Settings navigation flag
        private bool _navigatingToSettings = false;

        // Screenshot
        private string _lastScreenshotPath;
        private string _lastScreenshotFileName;

        // Playlist sidebar
        private bool _playlistSidebarVisible = false;

        // Music mode
        private bool _isMusicMode = false;
        private string _currentMusicFilePath = "";
        private string _currentMusicOriginalDir = "";
        private StorageFile _currentOriginalFile = null;

        // Lyric sync
        private List<LyricLine> _lyricLines = new List<LyricLine>();
        private int _currentLyricIndex = -1;
        private DispatcherTimer _lyricTimer;

        // External subtitle pending
        private string _pendingExternalSubPath;

        private class LyricLine
        {
            public double TimeMs;
            public string Text;
            public Border Container;
            public TextBlock UiElement;
            public TextBlock TimeIndicator;
        }

        public MainPage()
        {
            this.InitializeComponent();

            _autoHideTimer = new DispatcherTimer();
            _autoHideTimer.Interval = TimeSpan.FromSeconds(SettingsPage.GetAutoHideDelay());
            _autoHideTimer.Tick += AutoHideTimer_Tick;

            _positionTimer = new DispatcherTimer();
            _positionTimer.Interval = TimeSpan.FromMilliseconds(POSITION_TIMER_INTERVAL_MS);
            _positionTimer.Tick += PositionTimer_Tick;

            _overlayNotifyTimer = new DispatcherTimer();
            _overlayNotifyTimer.Interval = TimeSpan.FromSeconds(NOTIFY_TIMER_SECONDS);
            _overlayNotifyTimer.Tick += (s, ev) =>
            {
                _overlayNotifyTimer.Stop();
                OverlayNotification.Visibility = Visibility.Collapsed;
            };

            _lyricTimer = new DispatcherTimer();
            _lyricTimer.Interval = TimeSpan.FromMilliseconds(LYRIC_TIMER_INTERVAL_MS);
            _lyricTimer.Tick += LyricTimer_Tick;

            _sleepTimer = new DispatcherTimer();
            _sleepTimer.Interval = TimeSpan.FromSeconds(1);
            _sleepTimer.Tick += SleepTimer_Tick;

            // Apply settings
            VolumeSlider.Value = SettingsPage.GetDefaultVolume();

            InitShareAndTile();
            ApplyPlayerLanguage();

            this.Loaded += (s, e) =>
            {
                InitLibVlc();
                ApplyPlayerLanguage();
            };
            ShowControls();
        }

        private void ApplyPlayerLanguage()
        {
            try
            {
                var appText = Application.Current.Resources["AppText"] as AppText;
                if (appText != null)
                {
                    appText.ApplyLanguageTo(this);
                    appText.LanguageChanged -= AppText_LanguageChanged;
                    appText.LanguageChanged += AppText_LanguageChanged;
                }

                ApplyPlayerTheme(SettingsPage.GetLightTheme());
            }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        private void AppText_LanguageChanged(object sender, EventArgs e)
        {
            try
            {
                ApplyPlayerLanguage();
            }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        private void ApplyPlayerTheme(bool light)
        {
            try
            {
                if (TopBar == null || BottomBar == null) return;

                if (light)
                {
                    var topGrad = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(0, 1)
                    };
                    topGrad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF), Offset = 0 });
                    topGrad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), Offset = 1 });
                    TopBar.Background = topGrad;

                    var botGrad = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(0, 1)
                    };
                    botGrad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), Offset = 0 });
                    botGrad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF), Offset = 1 });
                    BottomBar.Background = botGrad;
                }
                else
                {
                    var topGrad = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(0, 1)
                    };
                    topGrad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0xDD, 0x0A, 0x0A, 0x0F), Offset = 0 });
                    topGrad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0x00, 0x0A, 0x0A, 0x0F), Offset = 1 });
                    TopBar.Background = topGrad;

                    var botGrad = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(0, 1)
                    };
                    botGrad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0x00, 0x0A, 0x0A, 0x0F), Offset = 0 });
                    botGrad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0xEE, 0x0A, 0x0A, 0x0F), Offset = 0.3 });
                    botGrad.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0xFF, 0x0A, 0x0A, 0x0F), Offset = 1 });
                    BottomBar.Background = botGrad;
                }

                ApplyBarTextColor(TopBar, light);
                ApplyBarTextColor(BottomBar, light);
            }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        private void ApplyBarTextColor(DependencyObject root, bool light)
        {
            try
            {
                var fg = new SolidColorBrush(Color.FromArgb(0xE6, (byte)(light ? 0x20 : 0xFF), (byte)(light ? 0x20 : 0xFF), (byte)(light ? 0x28 : 0xFF)));
                var whiteFg = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));

                int count = VisualTreeHelper.GetChildrenCount(root);
                for (int i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(root, i);
                    var tb = child as TextBlock;
                    if (tb != null && !tb.Name.StartsWith("Keep", StringComparison.Ordinal))
                    {
                        // Pink/colored accents keep their color; neutral white-ish text becomes dark
                        if (tb.Foreground is SolidColorBrush)
                        {
                            var brush = tb.Foreground as SolidColorBrush;
                            byte r = brush.Color.R, g = brush.Color.G, b = brush.Color.B;
                            bool isColoredAccent = (r > 0x80) && (g < 0x80) && (b > 0x80); // e.g. pink #E040FB
                            bool isCyanAccent = (g > 0x80) && (r < 0x80) && (b > 0x80);
                            if (!isColoredAccent && !isCyanAccent)
                                tb.Foreground = light ? fg : whiteFg;
                        }
                    }
                    ApplyBarTextColor(child, light);
                }
            }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        #region Share Charm & Live Tile

        private void ShowToast(string title, string message)
        {
            try
            {
                var toastXml = Windows.UI.Notifications.ToastNotificationManager.GetTemplateContent(
                    Windows.UI.Notifications.ToastTemplateType.ToastText02);
                var textNodes = toastXml.GetElementsByTagName("text");
                textNodes[0].InnerText = title;
                textNodes[1].InnerText = message;
                var toast = new Windows.UI.Notifications.ToastNotification(toastXml);
                Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier().Show(toast);
            }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        private void InitShareAndTile()
        {
            try
            {
                var dm = Windows.ApplicationModel.DataTransfer.DataTransferManager.GetForCurrentView();
                dm.DataRequested += DataTransferManager_DataRequested;
            }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        private async void DataTransferManager_DataRequested(Windows.ApplicationModel.DataTransfer.DataTransferManager sender,
            Windows.ApplicationModel.DataTransfer.DataRequestedEventArgs args)
        {
            var deferral = args.Request.GetDeferral();
            try
            {
                var request = args.Request;
                request.Data.Properties.Title = L("ShareTitle");
                if (!string.IsNullOrEmpty(_originalFileName))
                {
                    request.Data.SetText(_originalFileName);
                    request.Data.Properties.Description = L("ShareDesc");
                }
                if (!string.IsNullOrEmpty(_lastScreenshotPath))
                {
                    try
                    {
                        var file = await StorageFile.GetFileFromPathAsync(_lastScreenshotPath);
                        if (file != null)
                            request.Data.SetStorageItems(new System.Collections.Generic.List<StorageFile> { file });
                    }
                    catch (Exception ex) { LogUnhandled(ex); }
                }
            }
            catch (Exception ex) { LogUnhandled(ex); }
            finally
            {
                deferral.Complete();
            }
        }

        private void UpdateLiveTile()
        {
            try
            {
                string title = FileNameText.Text;
                if (string.IsNullOrEmpty(title)) return;
                string subtitle = _isPlaying ? L("Playing") : L("Paused");

                var tileXml = Windows.UI.Notifications.TileUpdateManager.GetTemplateContent(
                    Windows.UI.Notifications.TileTemplateType.TileSquare150x150Text04);
                var squareText = tileXml.GetElementsByTagName("text");
                squareText[0].InnerText = title;
                squareText[1].InnerText = subtitle;

                var wideXml = Windows.UI.Notifications.TileUpdateManager.GetTemplateContent(
                    Windows.UI.Notifications.TileTemplateType.TileWide310x150Text03);
                var wideText = wideXml.GetElementsByTagName("text");
                wideText[0].InnerText = title;
                wideText[1].InnerText = subtitle;

                var tile = new Windows.UI.Notifications.TileNotification(wideXml);
                Windows.UI.Notifications.TileUpdateManager.CreateTileUpdaterForApplication().Update(tile);
            }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        #endregion

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
            catch (Exception ex) { LogUnhandled(ex); }
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
            catch (Exception ex) { LogUnhandled(ex); }

            // Handle file activation (StorageFile passed directly)
            if (e.Parameter is StorageFile)
            {
                var file = e.Parameter as StorageFile;
                _playlist.Clear();
                _playlist.Add(file);
                _playlistIndex = 0;
                OpenFile(file);
                return;
            }

            // Handle protocol activation (string target: URL or local file path)
            string protocolTarget = e.Parameter as string;
            if (!string.IsNullOrEmpty(protocolTarget))
            {
                // Secondary tile: playlist:<name>
                if (protocolTarget.StartsWith("playlist:", StringComparison.OrdinalIgnoreCase))
                {
                    string playlistName = protocolTarget.Substring("playlist:".Length).Trim();
                    var files = PlaylistLibrary.GetPlaylistFiles(playlistName);
                    if (files != null && files.Count > 0)
                    {
                        var loaded = new List<StorageFile>();
                        for (int i = 0; i < files.Count; i++)
                        {
                            try
                            {
                                var f = await StorageFile.GetFileFromPathAsync(files[i]);
                                if (f != null) loaded.Add(f);
                            }
                            catch (Exception ex) { LogUnhandled(ex); }
                        }
                        if (loaded.Count > 0)
                        {
                            _playlist = loaded;
                            _playlistIndex = 0;
                            RestoreStateAfterSettings();
                            OpenFile(_playlist[0]);
                            return;
                        }
                    }
                }
                else if (protocolTarget.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    protocolTarget.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                    protocolTarget.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase) ||
                    protocolTarget.StartsWith("mms://", StringComparison.OrdinalIgnoreCase))
                {
                    _playlist.Clear();
                    _playlistIndex = -1;
                    OpenUrl(protocolTarget);
                    return;
                }
                else
                {
                    try
                    {
                        StorageFile file = await StorageFile.GetFileFromPathAsync(protocolTarget);
                        if (file != null)
                        {
                            _playlist.Clear();
                            _playlist.Add(file);
                            _playlistIndex = 0;
                            OpenFile(file);
                            return;
                        }
                    }
                    catch (Exception ex) { LogUnhandled(ex); }
                }
            }

            // Try FutureAccessList first (in-session navigation)
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
                                catch (Exception ex) { LogUnhandled(ex); }
                            }
                        }
                    }

                    _playlistIndex = 0;
                    RestoreStateAfterSettings();
                    OpenFile(_playlist[0]);
                    return;
                }
                catch (Exception ex) { LogUnhandled(ex); }
            }

            // Fallback: restore from LocalSettings (survives app restart)
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.ContainsKey(KEY_LAST_PLAYLIST))
                {
                    string playlistStr = settings.Values[KEY_LAST_PLAYLIST] as string;
                    int savedIndex = settings.Values.ContainsKey(KEY_LAST_INDEX)
                        ? (int)settings.Values[KEY_LAST_INDEX] : 0;

                    if (!string.IsNullOrEmpty(playlistStr))
                    {
                        string[] paths = playlistStr.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                        _playlist.Clear();
                        foreach (string path in paths)
                        {
                            try
                            {
                                var file = await StorageFile.GetFileFromPathAsync(path);
                                _playlist.Add(file);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine("[HyperMedia] Restore file failed ({0}): {1}", path, ex.Message);
                            }
                        }

                        if (_playlist.Count > 0)
                        {
                            _playlistIndex = savedIndex < _playlist.Count ? savedIndex : 0;
                            Debug.WriteLine("[HyperMedia] Restored from LocalSettings: {0} files, index={1}", _playlist.Count, _playlistIndex);

                            settings.Values.Remove(KEY_LAST_PLAYLIST);
                            settings.Values.Remove(KEY_LAST_INDEX);
                            settings.Values.Remove(KEY_LAST_FILE_PATH);

                            RestoreStateAfterSettings();
                            OpenFile(_playlist[_playlistIndex]);
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HyperMedia] Restore from LocalSettings FAILED: {0}", ex.Message);
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
                _autoHideTimer.Interval = TimeSpan.FromSeconds(SettingsPage.GetAutoHideDelay());
            }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            if (_navigatingToSettings)
            {
                Debug.WriteLine("[HyperMedia] OnNavigatedFrom: navigating to settings, skipping save");
                _navigatingToSettings = false;
                return;
            }

            Debug.WriteLine("[HyperMedia] OnNavigatedFrom: saving resume position");
            SaveResumePosition();
            StopPlayback();

            try
            {
                Application.Current.Suspending -= OnSmtcAppSuspended;
                Application.Current.Resuming -= OnSmtcAppResumed;
            }
            catch (Exception ex) { LogUnhandled(ex); }

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

            _introAutoSkippedThisSession = false;

            StopPlayback();
            _isNetworkStream = false;
            WelcomeScreen.Visibility = Visibility.Collapsed;

            _originalFileName = file.Name;
            _currentMusicFilePath = file.Path;
            _currentMusicOriginalDir = System.IO.Path.GetDirectoryName(file.Path);
            _currentOriginalFile = file;
            PlayHistory.Add(file.Path, file.Name);

            if (IsPhotoFile(file))
            {
                OpenPhoto(file);
                return;
            }

            if (_isPhotoMode)
                ClosePhotoViewer();

            // Episode mode: when opening a single media file, auto-queue siblings in same folder
            if (_playlist.Count <= 1 && SettingsPage.GetEpisodeAutoPlay())
                await TryQueueSameFolder(file);

            ShowOverlay(L("LoadingPrefix") + file.Name + "...");

            try
            {
                StatusText.Text = "正在准备...";
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

        private async System.Threading.Tasks.Task TryQueueSameFolder(StorageFile file)
        {
            try
            {
                var folder = await file.GetParentAsync();
                if (folder == null) return;

                var files = await folder.GetFilesAsync();
                if (files == null || files.Count <= 1) return;

                var siblings = new List<StorageFile>();
                foreach (var f in files)
                {
                    if (f.Path == file.Path) continue;
                    string ext = f.FileType.ToLowerInvariant();
                    if (MUSIC_EXTENSIONS.Contains(ext) || IsVideoExtension(ext))
                        siblings.Add(f);
                }
                if (siblings.Count == 0) return;

                // Only queue when a single file was opened directly (not via playlist navigation)
                if (_playlist.Count > 1) return;

                siblings.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                var newList = new List<StorageFile>();
                bool inserted = false;
                foreach (var s in siblings)
                {
                    if (!inserted && string.Compare(s.Name, file.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        newList.Add(file);
                        inserted = true;
                    }
                    newList.Add(s);
                }
                if (!inserted) newList.Add(file);

                _playlist = newList;
                _playlistIndex = newList.FindIndex(f => f.Path == file.Path);
                if (_playlistIndex < 0) _playlistIndex = 0;
                Debug.WriteLine("[HyperMedia] Episode mode: queued {0} files from same folder", newList.Count);
            }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        private bool IsVideoExtension(string ext)
        {
            switch (ext)
            {
                case ".mp4": case ".avi": case ".mkv": case ".webm": case ".flv":
                case ".mov": case ".wmv": case ".3gp": case ".ts": case ".mpg":
                case ".mpeg": case ".m4v": case ".mka": case ".ogv": case ".vob":
                    return true;
                default:
                    return false;
            }
        }

        private void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;

            PlayHistory.AddUrl(url);

            StopPlayback();
            _isNetworkStream = true;
            WelcomeScreen.Visibility = Visibility.Collapsed;
            ShowOverlay(L("LoadingStream"));

            _originalFileName = url;
            StatusText.Text = L("Connecting");
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
                    StatusText.Text = L("VlcNotInit");
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

                // Apply subtitle style settings
                int subSize = SettingsPage.GetSubtitleSize();
                string subColor = SettingsPage.GetSubtitleColor();
                _vlcMedia.addOption(":freetype-rel-fontsize=" + subSize);
                if (!string.IsNullOrEmpty(subColor))
                {
                    try
                    {
                        int colorInt = int.Parse(subColor.Substring(1), System.Globalization.NumberStyles.HexNumber);
                        _vlcMedia.addOption(":freetype-color=" + colorInt);
                    }
                    catch (Exception ex) { LogUnhandled(ex); }
                }

                int subMargin = SettingsPage.GetSubtitleMargin();
                if (subMargin != 0)
                    _vlcMedia.addOption(":sub-margin-v=" + (100 + subMargin));

                int subOutline = SettingsPage.GetSubtitleOutline();
                if (subOutline > 0)
                {
                    _vlcMedia.addOption(":freetype-outline-thickness=" + subOutline);
                    _vlcMedia.addOption(":freetype-outline-color=0");
                }

                string deinterlace = SettingsPage.GetDeinterlaceMode();
                if (!string.IsNullOrEmpty(deinterlace) && deinterlace != "off")
                    _vlcMedia.addOption(":deinterlace=" + deinterlace);

                // Video rotation (transform filter). VLC transform-type: 0=90°, 1=180°, 2=270°
                if (_videoRotation != 0 && !_isNetworkStream)
                {
                    int transformType = (_videoRotation - 1);
                    _vlcMedia.addOption(":video-filter=transform");
                    _vlcMedia.addOption(":transform-type=" + transformType);
                }

                // Loudness normalization
                if (SettingsPage.GetLoudnessEnabled())
                {
                    _vlcMedia.addOption(":audio-filter=compressor");
                    _vlcMedia.addOption(":compressor-attack=20");
                    _vlcMedia.addOption(":compressor-release=250");
                }

                // Recording (transcode to file)
                if (_recording && !_isNetworkStream && !string.IsNullOrEmpty(_recordingPath))
                {
                    _vlcMedia.addOption(":sout=#transcode{vcodec=mp4v,vb=2500,acodec=mpga,ab=128}:standard{access=file,mux=mp4,dst=\"" + _recordingPath + "\"}");
                    _vlcMedia.addOption(":sout-keep");
                }

                if (!string.IsNullOrEmpty(_pendingExternalSubPath))
                {
                    _vlcMedia.addOption(":sub-file=" + _pendingExternalSubPath);
                    _pendingExternalSubPath = null;
                }

                _vlcPlayer = new MediaPlayer(_vlcInstance);
                _vlcPlayer.setMedia(_vlcMedia);
            }
            catch (Exception ex)
            {
                StatusText.Text = L("ErrorPrefix") + ex.Message;
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
            catch (Exception ex) { LogUnhandled(ex); }

            _vlcPlayer.eventManager().OnPlaying += OnVlcPlaying;
            _vlcPlayer.eventManager().OnPaused += OnVlcPaused;
            _vlcPlayer.eventManager().OnStopped += OnVlcStopped;
            _vlcPlayer.eventManager().OnEndReached += OnVlcEndReached;
            _vlcPlayer.eventManager().OnEncounteredError += OnVlcEncounteredError;
            _vlcPlayer.eventManager().OnLengthChanged += OnVlcLengthChanged;
            _vlcPlayer.eventManager().OnSnapshotTaken += OnSnapshotTaken;

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
                catch (Exception ex) { LogUnhandled(ex); }
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

        private void UrlButton_Click(object sender, RoutedEventArgs e)
        {
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
            title.Text = "打开网址";
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
            textBox.KeyDown += (s, ev) =>
            {
                if (ev.Key == VirtualKey.Enter)
                {
                    string url = textBox.Text.Trim();
                    popup.IsOpen = false;
                    if (!string.IsNullOrEmpty(url))
                        OpenUrl(url);
                }
            };
            panel.Children.Add(textBox);

            var history = PlayHistory.GetUrlHistory();
            if (history.Count > 0)
            {
                var historyTitle = new TextBlock();
                historyTitle.Text = "最近打开";
                historyTitle.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
                historyTitle.FontSize = 11;
                historyTitle.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF));
                historyTitle.Margin = new Thickness(0, 14, 0, 6);
                panel.Children.Add(historyTitle);

                var historyList = new ListBox();
                historyList.MaxHeight = 160;
                historyList.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF));
                historyList.BorderThickness = new Thickness(0);
                historyList.Width = 450;
                try
                {
                    historyList.ItemContainerStyle = Application.Current.Resources["ZuneListBoxItemStyle"] as Style;
                }
                catch (Exception ex) { LogUnhandled(ex); }
                foreach (var url in history)
                {
                    var item = new ListBoxItem();
                    item.Content = url;
                    item.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
                    item.FontSize = 12;
                    item.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
                    item.Padding = new Thickness(8, 6, 8, 6);
                    item.Tapped += (s, ev) =>
                    {
                        textBox.Text = url;
                    };
                    historyList.Items.Add(item);
                }
                panel.Children.Add(historyList);
            }

            var btnPanel = new StackPanel();
            btnPanel.Orientation = Windows.UI.Xaml.Controls.Orientation.Horizontal;
            btnPanel.HorizontalAlignment = HorizontalAlignment.Right;
            btnPanel.Margin = new Thickness(0, 16, 0, 0);

            var cancelBtn = new Button();
            cancelBtn.Content = "取消";
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            cancelBtn.Click += (s, ev) => { popup.IsOpen = false; };
            btnPanel.Children.Add(cancelBtn);

            var playBtn = new Button();
            playBtn.Content = "播放";
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
            popup.Height = 340;

            // Position at center of screen
            var bounds = Window.Current.Bounds;
            popup.HorizontalOffset = (bounds.Width - 500) / 2;
            popup.VerticalOffset = (bounds.Height - 340) / 2;

            popup.IsOpen = true;
            textBox.Focus(FocusState.Programmatic);
        }

        #endregion

        #region Playlist

        private class PlaylistItem
        {
            public int Index { get; set; }
            public string FileName { get; set; }
            public string DurationText { get; set; }
            public bool IsCurrent { get; set; }
        }

        private string _playlistFilter = "";

        private void RefreshPlaylistSidebar()
        {
            var items = new List<PlaylistItem>();
            string filter = (_playlistFilter ?? "").Trim();
            for (int i = 0; i < _playlist.Count; i++)
            {
                if (!string.IsNullOrEmpty(filter) &&
                    _playlist[i].Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                string ext = System.IO.Path.GetExtension(_playlist[i].Name ?? "");
                if (ext.Length > 1)
                    ext = ext.Substring(1).ToUpperInvariant();
                items.Add(new PlaylistItem
                {
                    Index = i + 1,
                    FileName = _playlist[i].Name,
                    DurationText = ext,
                    IsCurrent = (i == _playlistIndex)
                });
            }
            PlaylistListBox.ItemsSource = items;
            PlaylistEmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // Restore selection on the currently playing item
            if (_playlistIndex >= 0 && _playlistIndex < _playlist.Count &&
                (string.IsNullOrEmpty(filter) || _playlist[_playlistIndex].Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                PlaylistListBox.SelectedIndex = items.FindIndex(it => it.Index - 1 == _playlistIndex);
            }
        }

        private void PlaylistSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _playlistFilter = PlaylistSearchBox.Text ?? "";
            RefreshPlaylistSidebar();
        }

        private void PlaylistSearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape)
            {
                PlaylistSearchBox.Text = "";
                _playlistFilter = "";
                RefreshPlaylistSidebar();
                e.Handled = true;
            }
        }

        private void PlaylistToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _playlistSidebarVisible = !_playlistSidebarVisible;
            if (_playlistSidebarVisible)
            {
                RefreshPlaylistSidebar();
                PlaylistSidebar.Visibility = Visibility.Visible;
            }
            else
            {
                PlaylistSidebar.Visibility = Visibility.Collapsed;
            }
        }

        private void PlaylistListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PlaylistListBox.SelectedIndex < 0) return;
            if (PlaylistListBox.SelectedIndex == _playlistIndex) return;
            if (PlaylistListBox.SelectedIndex >= _playlist.Count) return;

            SaveResumePosition();
            _playlistIndex = PlaylistListBox.SelectedIndex;
            OpenFile(_playlist[_playlistIndex]);
        }

        private void PlaylistItemDelete_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            int idx = (int)btn.Tag - 1;
            if (idx < 0 || idx >= _playlist.Count) return;

            bool wasPlaying = (idx == _playlistIndex);
            _playlist.RemoveAt(idx);

            if (wasPlaying)
            {
                if (_playlist.Count == 0)
                {
                    _playlistIndex = -1;
                    StopPlayback();
                    WelcomeScreen.Visibility = Visibility.Visible;
                    FileNameText.Text = "";
                    StatusText.Text = L("PlaylistCleared");
                }
                else
                {
                    if (_playlistIndex >= _playlist.Count)
                        _playlistIndex = 0;
                    OpenFile(_playlist[_playlistIndex]);
                }
            }
            else if (idx < _playlistIndex)
            {
                _playlistIndex--;
            }

            UpdatePlaylistCounter();
            RefreshPlaylistSidebar();
        }

        private void PlaylistItemMoveUp_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            int idx = (int)btn.Tag - 1;
            if (idx <= 0 || idx >= _playlist.Count) return;

            var file = _playlist[idx];
            _playlist.RemoveAt(idx);
            _playlist.Insert(idx - 1, file);

            if (_playlistIndex == idx)
                _playlistIndex--;
            else if (_playlistIndex == idx - 1)
                _playlistIndex++;

            UpdatePlaylistCounter();
            RefreshPlaylistSidebar();
        }

        private void PlaylistItemMoveDown_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            int idx = (int)btn.Tag - 1;
            if (idx < 0 || idx >= _playlist.Count - 1) return;

            var file = _playlist[idx];
            _playlist.RemoveAt(idx);
            _playlist.Insert(idx + 1, file);

            if (_playlistIndex == idx)
                _playlistIndex++;
            else if (_playlistIndex == idx + 1)
                _playlistIndex--;

            UpdatePlaylistCounter();
            RefreshPlaylistSidebar();
        }

        private async void PlaylistClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (_playlist.Count == 0) return;

            var dialog = new MessageDialog(L("ClearPlaylistConfirm"), L("ClearPlaylistTitle"));
            dialog.Commands.Add(new UICommand(L("ClearBtn"), (cmd) => { ClearPlaylistCore(); }));
            dialog.Commands.Add(new UICommand("取消"));
            dialog.DefaultCommandIndex = 1;
            dialog.CancelCommandIndex = 1;
            await dialog.ShowAsync();
        }

        private void ClearPlaylistCore()
        {
            StopPlayback();
            _playlist.Clear();
            _playlistIndex = -1;
            WelcomeScreen.Visibility = Visibility.Visible;
            FileNameText.Text = "";
            PlaylistCounter.Text = "";
            StatusText.Text = L("PlaylistCleared");
            RefreshPlaylistSidebar();
        }

        private void PlaylistSaveLibrary_Click(object sender, RoutedEventArgs e)
        {
            if (_playlist.Count == 0)
            {
                ShowOverlay(L("PlaylistEmptyNoSave"));
                HideOverlayDelayed();
                return;
            }

            var popup = new Popup();
            var border = new Border();
            border.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xF0, 0x0A, 0x0A, 0x0F));
            border.Width = 420;
            border.Padding = new Thickness(24);

            var panel = new StackPanel();

            var title = new TextBlock();
            title.Text = "保存为歌单";
            title.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
            title.FontSize = 14;
            title.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x40, 0xFB));
            title.Margin = new Thickness(0, 0, 0, 12);
            panel.Children.Add(title);

            var info = new TextBlock();
            info.Text = "共 " + _playlist.Count + L("ImportedFilesSuffix");
            info.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
            info.FontSize = 11;
            info.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF));
            info.Margin = new Thickness(0, 0, 0, 8);
            panel.Children.Add(info);

            var textBox = new TextBox();
            textBox.PlaceholderText = "歌单名称（如：周杰伦精选）";
            textBox.Width = 370;
            textBox.Height = 36;
            textBox.FontSize = 14;
            textBox.KeyDown += (s, ev) =>
            {
                if (ev.Key == VirtualKey.Enter)
                {
                    string name = textBox.Text.Trim();
                    popup.IsOpen = false;
                    if (!string.IsNullOrEmpty(name))
                        DoSavePlaylist(name);
                }
            };
            panel.Children.Add(textBox);

            var btnPanel = new StackPanel();
            btnPanel.Orientation = Windows.UI.Xaml.Controls.Orientation.Horizontal;
            btnPanel.HorizontalAlignment = HorizontalAlignment.Right;
            btnPanel.Margin = new Thickness(0, 16, 0, 0);

            var cancelBtn = new Button();
            cancelBtn.Content = "取消";
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            cancelBtn.Click += (s, ev) => { popup.IsOpen = false; };
            btnPanel.Children.Add(cancelBtn);

            var saveBtn = new Button();
            saveBtn.Content = "保存";
            saveBtn.Click += (s, ev) =>
            {
                string name = textBox.Text.Trim();
                popup.IsOpen = false;
                if (!string.IsNullOrEmpty(name))
                    DoSavePlaylist(name);
            };
            btnPanel.Children.Add(saveBtn);

            panel.Children.Add(btnPanel);
            border.Child = panel;

            popup.Child = border;
            popup.Width = 420;
            popup.Height = 200;

            var bounds = Window.Current.Bounds;
            popup.HorizontalOffset = (bounds.Width - 420) / 2;
            popup.VerticalOffset = (bounds.Height - 200) / 2;

            popup.IsOpen = true;
            textBox.Focus(FocusState.Programmatic);
        }

        private void DoSavePlaylist(string name)
        {
            try
            {
                var paths = new List<string>();
                foreach (var f in _playlist)
                    paths.Add(f.Path);

                if (PlaylistLibrary.CreatePlaylist(name, paths))
                {
                    ShowOverlay(L("PlaylistSavedPrefix") + name);
                    HideOverlayDelayed();
                }
                else
                {
                    ShowOverlay(L("PlaylistSaveFailed"));
                    HideOverlayDelayed();
                }
            }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        private async void PlaylistExportM3u_Click(object sender, RoutedEventArgs e)
        {
            if (_playlist.Count == 0) return;

            try
            {
                var picker = new FileSavePicker();
                picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
                picker.FileTypeChoices.Add("M3U 播放列表", new System.Collections.Generic.List<string> { ".m3u" });
                picker.SuggestedFileName = "playlist";

                var file = await picker.PickSaveFileAsync();
                if (file != null)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("#EXTM3U");
                    foreach (var item in _playlist)
                    {
                        sb.AppendLine(item.Path);
                    }
                    await FileIO.WriteTextAsync(file, sb.ToString(), Windows.Storage.Streams.UnicodeEncoding.Utf8);
                    ShowOverlay(L("PlaylistExportedPrefix") + file.Name);
                    HideOverlayDelayed();
                }
            }
            catch (Exception ex)
            {
                ShowOverlay(L("ExportFailedPrefix") + ex.Message);
                HideOverlayDelayed();
            }
        }

        private async void PlaylistImportM3u_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
                picker.FileTypeFilter.Add(".m3u");
                picker.FileTypeFilter.Add(".m3u8");

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    var content = await FileIO.ReadTextAsync(file, Windows.Storage.Streams.UnicodeEncoding.Utf8);
                    var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    int added = 0;

                    foreach (var line in lines)
                    {
                        string trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                            continue;

                        try
                        {
                            StorageFile mediaFile = null;
                            if (trimmed.StartsWith("http://") || trimmed.StartsWith("https://") || trimmed.StartsWith("rtsp://"))
                            {
                                continue;
                            }
                            else
                            {
                                mediaFile = await StorageFile.GetFileFromPathAsync(trimmed);
                            }

                            if (mediaFile != null)
                            {
                                _playlist.Add(mediaFile);
                                added++;
                            }
                        }
                        catch (Exception ex) { LogUnhandled(ex); }
                    }

                    if (added > 0)
                    {
                        UpdatePlaylistCounter();
                        RefreshPlaylistSidebar();
                        ShowOverlay(L("ImportedPrefix") + added + L("ImportedFilesSuffix"));
                        HideOverlayDelayed();

                        if (_playlistIndex < 0)
                        {
                            _playlistIndex = 0;
                            OpenFile(_playlist[0]);
                        }
                    }
                    else
                    {
                        ShowOverlay(L("NoPlayableFiles"));
                        HideOverlayDelayed();
                    }
                }
            }
            catch (Exception ex)
            {
                ShowOverlay(L("ImportFailedPrefix") + ex.Message);
                HideOverlayDelayed();
            }
        }

        private void PlayNext()
        {
            if (_playlist.Count == 0) return;
            if (!SettingsPage.GetAutoPlay() && _repeatMode == 0) return;

            if (_repeatMode == 2)
            {
                if (!_autoAdvancing) SaveResumePosition();
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
                    StatusText.Text = L("PlaybackComplete");
                    return;
                }
            }

            if (!_autoAdvancing) SaveResumePosition();
            _playlistIndex = nextIndex;
            OpenFile(_playlist[_playlistIndex]);
        }

        private void PlayPrev()
        {
            if (_playlist.Count == 0) return;

            if (_vlcPlayer != null)
            {
                long time = _vlcPlayer.time();
                if (time > PREV_TRACK_RESTART_MS)
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

            if (_playlistSidebarVisible)
                RefreshPlaylistSidebar();
        }

        #endregion

        #region Resume Position

        private long _pendingResumePos = 0;

        private void SaveResumePosition()
        {
            if (!SettingsPage.GetResumeEnabled()) return;
            if (_vlcPlayer == null || string.IsNullOrEmpty(_originalFileName)) return;
            try
            {
                long time = _vlcPlayer.time();
                long len = _vlcPlayer.length();
                Debug.WriteLine("[HyperMedia] SaveResumePosition: time={0}ms len={1}ms file={2}", time, len, _originalFileName);
                if (time > RESUME_MIN_TIME_MS && len > RESUME_MIN_LENGTH_MS)
                {
                    var settings = ApplicationData.Current.LocalSettings;
                    settings.Values[KEY_RESUME + _originalFileName] = time;
                    settings.Values["ResumePercent_" + _originalFileName] = Math.Min(99.0, time * 100.0 / len);
                    Debug.WriteLine("[HyperMedia] Resume position SAVED: {0}ms for {1}", time, _originalFileName);
                }
                else
                {
                    Debug.WriteLine("[HyperMedia] Resume position NOT saved (time={0} len={1})", time, len);
                }

                // Persist file paths for restart recovery
                SaveFilePersistence();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HyperMedia] SaveResumePosition FAILED: {0}", ex.Message);
            }
        }

        private void SaveFilePersistence()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (_playlist.Count > 0 && _playlistIndex >= 0 && _playlistIndex < _playlist.Count)
                {
                    // Save current file path
                    settings.Values[KEY_LAST_FILE_PATH] = _playlist[_playlistIndex].Path;
                    settings.Values[KEY_LAST_INDEX] = _playlistIndex;

                    // Save all playlist paths
                    var paths = new System.Collections.Generic.List<string>();
                    for (int i = 0; i < _playlist.Count; i++)
                        paths.Add(_playlist[i].Path);
                    settings.Values[KEY_LAST_PLAYLIST] = string.Join("|", paths);
                    Debug.WriteLine("[HyperMedia] File persistence saved: {0} files, index={1}", paths.Count, _playlistIndex);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HyperMedia] SaveFilePersistence FAILED: {0}", ex.Message);
            }
        }

        private long LoadResumePosition(string fileName)
        {
            if (!SettingsPage.GetResumeEnabled())
            {
                Debug.WriteLine("[HyperMedia] LoadResumePosition: resume disabled in settings");
                return 0;
            }
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                string key = KEY_RESUME + fileName;
                Debug.WriteLine("[HyperMedia] LoadResumePosition: looking for key={0}, exists={1}", key, settings.Values.ContainsKey(key));
                if (settings.Values.ContainsKey(key))
                {
                    long pos = (long)settings.Values[key];
                    Debug.WriteLine("[HyperMedia] Resume position FOUND: {0}ms for {1}", pos, fileName);
                    return pos;
                }
                else
                {
                    Debug.WriteLine("[HyperMedia] Resume position NOT FOUND for {0}", fileName);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HyperMedia] LoadResumePosition FAILED: {0}", ex.Message);
            }
            return 0;
        }

        private void RemoveResumePosition(string fileName)
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                string key = KEY_RESUME + fileName;
                if (settings.Values.ContainsKey(key))
                {
                    settings.Values.Remove(key);
                    settings.Values.Remove("ResumePercent_" + fileName);
                    Debug.WriteLine("[HyperMedia] Resume position REMOVED for {0}", fileName);
                }
            }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        private void ClearResumePosition()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                var keys = new System.Collections.Generic.List<string>();
                foreach (var key in settings.Values.Keys)
                {
                    string k = key != null ? key.ToString() : "";
                    if (k.StartsWith("ResumePosition_") || k.StartsWith("ResumePercent_"))
                        keys.Add(k);
                }
                foreach (var key in keys)
                    settings.Values.Remove(key);
                Debug.WriteLine("[HyperMedia] All resume positions CLEARED ({0} entries)", keys.Count);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HyperMedia] ClearResumePosition FAILED: {0}", ex.Message);
            }
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
                _smtcSyncTimer.Interval = TimeSpan.FromMilliseconds(SMTC_SYNC_INTERVAL_MS);
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
                        StatusText.Text = "就绪";
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
            catch (Exception ex) { LogUnhandled(ex); }
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
            catch (Exception ex) { LogUnhandled(ex); }
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
            catch (Exception ex) { LogUnhandled(ex); }
        }

        private void SmtcSyncTimer_Tick(object sender, object e)
        {
            if (_vlcPlayer == null || _smtc == null) return;
            try
            {
                SyncSmtcState();
            }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        private void OnSmtcAppSuspended(object sender, SuspendingEventArgs e)
        {
            if (_vlcPlayer != null && _isPlaying)
            {
                try
                {
                    _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
                }
                catch (Exception ex) { LogUnhandled(ex); }
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
                catch (Exception ex) { LogUnhandled(ex); }
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

        private const long FRAME_STEP_MS = 40;

        private void NudgeAbPoint(long deltaMs)
        {
            if (!_abActive || _vlcPlayer == null) return;
            double current = _vlcPlayer.time() / 1000.0;

            double distA = Math.Abs(current - _abPointA);
            double distB = Math.Abs(current - _abPointB);
            if (distA <= distB)
            {
                _abPointA = Math.Max(0, _abPointA + deltaMs / 1000.0);
                if (_abPointA > _abPointB) _abPointA = _abPointB;
                ShowOverlay(L("PointAPrefix") + FormatTime(_abPointA) + " (" + (deltaMs > 0 ? "+" : "") + deltaMs + "ms)");
            }
            else
            {
                _abPointB = Math.Max(_abPointA, _abPointB + deltaMs / 1000.0);
                ShowOverlay(L("PointBPrefix") + FormatTime(_abPointB) + " (" + (deltaMs > 0 ? "+" : "") + deltaMs + "ms)");
            }
            HideOverlayDelayed();
        }

        #endregion

        #region Screenshot

        private async void TakeScreenshot()
        {
            if (_vlcPlayer == null)
            {
                Debug.WriteLine("[HyperMedia] Screenshot FAILED: _vlcPlayer is null");
                ShowOverlay(L("ScreenshotNotReady"));
                HideOverlayDelayed();
                return;
            }
            if (_isNetworkStream)
            {
                Debug.WriteLine("[HyperMedia] Screenshot FAILED: network stream");
                ShowOverlay(L("ScreenshotNoNetwork"));
                HideOverlayDelayed();
                return;
            }

            try
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = "Screenshot_" + timestamp + ".jpg";

                // Use TemporaryFolder — VLC native code CAN write here (same as playback temp)
                var tempFolder = ApplicationData.Current.TemporaryFolder;
                string filePath = tempFolder.Path + "\\" + fileName;
                Debug.WriteLine("[HyperMedia] Screenshot target: {0}", filePath);

                // Delete existing file if any (VLC can't overwrite)
                try
                {
                    var existing = await tempFolder.GetFileAsync(fileName);
                    if (existing != null) await existing.DeleteAsync();
                }
                catch (Exception ex) { LogUnhandled(ex); }

                _lastScreenshotPath = filePath;
                _lastScreenshotFileName = fileName;
                ShowOverlay(L("TakingScreenshot"));
                Debug.WriteLine("[HyperMedia] Calling takeSnapshot...");
                _vlcPlayer.takeSnapshot(0, filePath, 0, 0);
                Debug.WriteLine("[HyperMedia] takeSnapshot called, waiting for OnSnapshotTaken callback");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HyperMedia] Screenshot FAILED: {0}", ex);
                ShowOverlay(L("ScreenshotFailedPrefix") + ex.Message);
                HideOverlayDelayed();
            }
        }

        private const int SCREENSHOT_BURST_COUNT = 3;
        private const int SCREENSHOT_BURST_INTERVAL_MS = 400;

        private async void TakeScreenshotBurst()
        {
            if (_vlcPlayer == null || _isNetworkStream)
            {
                ShowOverlay(L("BurstNotAvailable"));
                HideOverlayDelayed();
                return;
            }
            ShowOverlay(L("BurstPrefix") + SCREENSHOT_BURST_COUNT + " " + L("BurstSuffix"));
            for (int i = 0; i < SCREENSHOT_BURST_COUNT; i++)
            {
                TakeScreenshot();
                await Task.Delay(SCREENSHOT_BURST_INTERVAL_MS);
            }
        }

        #endregion

        #region Bookmarks

        private const string KEY_BOOKMARKS = "Bookmarks_";

        private void BookmarkBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowBookmarks();
        }

        private void ToggleBookmark()
        {            if (_vlcPlayer == null || string.IsNullOrEmpty(_originalFileName)) return;
            try
            {
                long time = _vlcPlayer.time();
                var settings = ApplicationData.Current.LocalSettings;
                string key = KEY_BOOKMARKS + _originalFileName;

                var list = new List<long>();
                if (settings.Values.ContainsKey(key))
                {
                    string serialized = settings.Values[key] as string;
                    if (!string.IsNullOrEmpty(serialized))
                    {
                        foreach (var part in serialized.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            long v;
                            if (long.TryParse(part, out v)) list.Add(v);
                        }
                    }
                }

                if (list.Remove(time))
                {
                    ShowOverlay(L("BookmarkRemovedPrefix") + FormatTime(time / 1000.0));
                }
                else
                {
                    list.Add(time);
                    list.Sort();
                    ShowOverlay(L("BookmarkAddedPrefix") + FormatTime(time / 1000.0) + L("BookmarkHintSuffix"));
                }
                settings.Values[key] = string.Join("|", list);
            }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        private void ShowBookmarks()
        {
            var menu = new MenuFlyout();
            menu.Placement = FlyoutPlacementMode.Bottom;

            var header = new MenuFlyoutItem { Text = L("BookmarkTitle") };
            header.IsEnabled = false;
            menu.Items.Add(header);

            try
            {
                if (_vlcPlayer == null || string.IsNullOrEmpty(_originalFileName))
                {
                    menu.Items.Add(new MenuFlyoutItem { Text = L("NoBookmarksShort") });
                    menu.ShowAt(BookmarkBtn);
                    return;
                }

                var settings = ApplicationData.Current.LocalSettings;
                string key = KEY_BOOKMARKS + _originalFileName;
                var list = new List<long>();
                if (settings.Values.ContainsKey(key))
                {
                    string serialized = settings.Values[key] as string;
                    if (!string.IsNullOrEmpty(serialized))
                    {
                        foreach (var part in serialized.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            long v;
                            if (long.TryParse(part, out v)) list.Add(v);
                        }
                    }
                }

                if (list.Count == 0)
                {
                    menu.Items.Add(new MenuFlyoutItem { Text = "无书签 (Ctrl+B 添加)" });
                }
                else
                {
                    foreach (var bm in list)
                    {
                        string timeText = FormatTime(bm / 1000.0);
                        var item = new MenuFlyoutItem { Text = timeText };
                        long jumpTo = bm;
                        item.Tapped += (s, ev) =>
                        {
                            try { _vlcPlayer?.setTime(jumpTo); } catch (Exception ex) { LogUnhandled(ex); }
                        };
                        menu.Items.Add(item);
                    }
                }
            }
            catch (Exception ex) { LogUnhandled(ex); }

            menu.ShowAt(BookmarkBtn);
        }

        #endregion

        #region Music Mode (Album Art + Lyrics)

        private static readonly System.Collections.Generic.HashSet<string> MUSIC_EXTENSIONS =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mp3", ".flac", ".wav", ".aac", ".ogg", ".wma", ".m4a", ".opus", ".ape", ".alac", ".aiff"
            };

        private async void DetectMusicMode()
        {
            try
            {
                string ext = System.IO.Path.GetExtension(_originalFileName ?? "");
                bool isAudio = MUSIC_EXTENSIONS.Contains(ext);
                Debug.WriteLine("[HyperMedia] DetectMusicMode: ext={0}, isAudio={1}", ext, isAudio);

                _isMusicMode = isAudio;
                MusicOverlay.Visibility = isAudio ? Visibility.Visible : Visibility.Collapsed;
                UpdateVideoControlsForMode(isAudio);

                if (isAudio)
                {
                    // Use ORIGINAL file for metadata, lyrics, album art — not the temp copy
                    string artist = _vlcMedia != null ? _vlcMedia.meta(MediaMeta.Artist) ?? "" : "";
                    string nowPlaying = _vlcMedia != null ? _vlcMedia.meta(MediaMeta.NowPlaying) ?? "" : "";
                    string album = _vlcMedia != null ? _vlcMedia.meta(MediaMeta.Album) ?? "" : "";

                    Debug.WriteLine("[HyperMedia] Music metadata: artist={0}, nowPlaying={1}, album={2}", artist, nowPlaying, album);

                    string metaText = "";
                    if (!string.IsNullOrEmpty(artist)) metaText += artist;
                    if (!string.IsNullOrEmpty(album))
                    {
                        if (metaText.Length > 0) metaText += " · ";
                        metaText += album;
                    }
                    AlbumArtMetaText.Text = metaText;

                    await LoadAlbumArtAsync();
                    LoadLyrics();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HyperMedia] DetectMusicMode FAILED: {0}", ex.Message);
            }
        }

        private void UpdateVideoControlsForMode(bool isMusic)
        {
            try
            {
                // Video-only controls hidden in music mode
                Visibility v = isMusic ? Visibility.Collapsed : Visibility.Visible;
                AspectRatioButton.Visibility = v;
                VideoFilterBtn.Visibility = v;
                SnapshotButton.Visibility = v;
                ChapterBtn.Visibility = v;
                AudioDeviceBtn.Visibility = v;
                RotateBtn.Visibility = v;
                CropBtn.Visibility = v;
                RecordBtn.Visibility = v;
            }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        private async System.Threading.Tasks.Task LoadAlbumArtAsync()
        {
            try
            {
                var file = _currentOriginalFile;
                if (file == null) return;

                var thumb = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.MusicView, 640);
                if (thumb != null)
                {
                    var bitmap = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                    await bitmap.SetSourceAsync(thumb);
                    AlbumArtImage.Source = bitmap;
                    AlbumArtPlaceholder.Visibility = Visibility.Collapsed;
                    Debug.WriteLine("[HyperMedia] Album art loaded from thumbnail");
                }
                else
                {
                    Debug.WriteLine("[HyperMedia] No album art thumbnail available");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HyperMedia] LoadAlbumArtAsync FAILED: {0}", ex.Message);
            }
        }

        private async void LoadLyrics()
        {
            try
            {
                if (_currentOriginalFile == null) return;

                string baseName = System.IO.Path.GetFileNameWithoutExtension(_currentOriginalFile.Name);

                // 1. Try embedded lyrics from audio container (m4a, mp4, etc.)
                string embedded = await ExtractEmbeddedLyrics();
                if (!string.IsNullOrEmpty(embedded))
                {
                    Debug.WriteLine("[HyperMedia] Embedded lyrics extracted ({0} chars)", embedded.Length);
                    DisplayLyrics(embedded);
                    return;
                }

                // 2. Try external lyrics files next to the ORIGINAL file
                string dir = System.IO.Path.GetDirectoryName(_currentOriginalFile.Path);
                if (string.IsNullOrEmpty(dir)) return;

                string[] lyricsExtensions = { ".lrc", ".txt", ".srt" };
                foreach (string ext in lyricsExtensions)
                {
                    string lyricsPath = System.IO.Path.Combine(dir, baseName + ext);
                    Debug.WriteLine("[HyperMedia] Looking for lyrics: {0}", lyricsPath);
                    try
                    {
                        var lyricsFile = await StorageFile.GetFileFromPathAsync(lyricsPath);
                        if (lyricsFile != null)
                        {
                            string content = await Windows.Storage.FileIO.ReadTextAsync(lyricsFile);
                            if (!string.IsNullOrEmpty(content))
                            {
                                Debug.WriteLine("[HyperMedia] Lyrics loaded from {0} ({1} chars)", lyricsPath, content.Length);
                                DisplayLyrics(content);
                                return;
                            }
                        }
                    }
                    catch (Exception ex) { LogUnhandled(ex); }
                }

                Debug.WriteLine("[HyperMedia] No lyrics file found for {0} in {1}", baseName, dir);
                ShowNoLyrics();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HyperMedia] LoadLyrics FAILED: {0}", ex.Message);
                ShowNoLyrics();
            }
        }

        private void DisplayLyrics(string rawText)
        {
            _lyricLines.Clear();
            LyricsLines.Children.Clear();
            _currentLyricIndex = -1;

            var parsed = ParseLrc(rawText);
            if (parsed.Count == 0)
            {
                var tb = new TextBlock
                {
                    Text = rawText,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 15,
                    Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 4)
                };
                LyricsLines.Children.Add(tb);
                return;
            }

            foreach (var line in parsed)
            {
                var tb = new TextBlock
                {
                    Text = line.Text,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 15,
                    Foreground = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var accentBar = new Border
                {
                    Width = 3,
                    Height = 24,
                    Background = new SolidColorBrush(Color.FromArgb(0x00, 0xE0, 0x40, 0xFB)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0)
                };

                var timeTb = new TextBlock
                {
                    Text = FormatTime(line.TimeMs / 1000.0),
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0),
                    MinWidth = 40
                };

                var row = new StackPanel
                {
                    Orientation = Windows.UI.Xaml.Controls.Orientation.Horizontal,
                    Margin = new Thickness(0, 5, 0, 5)
                };
                row.Children.Add(accentBar);
                row.Children.Add(timeTb);
                row.Children.Add(tb);

                var container = new Border
                {
                    Child = row,
                    Padding = new Thickness(8, 2, 8, 2),
                    CornerRadius = new Windows.UI.Xaml.CornerRadius(0),
                    Background = new SolidColorBrush(Color.FromArgb(0x00, 0x1A, 0x1A, 0x2E))
                };

                LyricsLines.Children.Add(container);

                var lyricLine = new LyricLine
                {
                    TimeMs = line.TimeMs,
                    Text = line.Text,
                    Container = container,
                    UiElement = tb,
                    TimeIndicator = timeTb
                };
                _lyricLines.Add(lyricLine);
            }

            Debug.WriteLine("[HyperMedia] Lyrics parsed: {0} lines with timestamps", _lyricLines.Count);
            if (_lyricLines.Count > 0 && _isPlaying)
                _lyricTimer.Start();
        }

        private void ShowNoLyrics()
        {
            _lyricLines.Clear();
            LyricsLines.Children.Clear();
            _currentLyricIndex = -1;
            var tb = new TextBlock
            {
                Text = "暂无歌词",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 15,
                Foreground = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
                Margin = new Thickness(0, 4, 0, 4)
            };
            LyricsLines.Children.Add(tb);
        }

        private List<LyricLine> ParseLrc(string text)
        {
            var result = new List<LyricLine>();

            // Collect all [timestamp] pairs with their following text
            int pos = 0;
            while (pos < text.Length)
            {
                int openBracket = text.IndexOf('[', pos);
                if (openBracket < 0) break;

                int closeBracket = text.IndexOf(']', openBracket + 1);
                if (closeBracket < 0) break;

                string segment = text.Substring(openBracket + 1, closeBracket - openBracket - 1);
                double ms = ParseLrcTimestamp(segment);

                // Find text until next timestamp or end
                int textStart = closeBracket + 1;
                int textEnd = text.Length;
                int nextOpen = text.IndexOf('[', textStart);
                if (nextOpen >= 0) textEnd = nextOpen;

                string lineText = text.Substring(textStart, textEnd - textStart).Trim();
                // Strip trailing " / " separator
                if (lineText.EndsWith("/")) lineText = lineText.Substring(0, lineText.Length - 1).Trim();
                if (lineText.EndsWith("\\")) lineText = lineText.Substring(0, lineText.Length - 1).Trim();

                if (ms >= 0 && !string.IsNullOrEmpty(lineText))
                {
                    result.Add(new LyricLine { TimeMs = ms, Text = lineText });
                }

                pos = closeBracket + 1;
            }

            result.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
            return result;
        }

        private double ParseLrcTimestamp(string s)
        {
            // Format: MM:SS.xx or MM:SS.xxx or MM:SS
            var parts = s.Split(':');
            if (parts.Length != 2) return -1;

            int min;
            if (!int.TryParse(parts[0], out min)) return -1;

            double sec;
            if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out sec))
                return -1;

            return min * 60000 + sec * 1000;
        }

        private void LyricTimer_Tick(object sender, object e)
        {
            if (_vlcPlayer == null || _lyricLines.Count == 0) return;

            double posMs = 0;
            try { posMs = _vlcPlayer.time(); }
            catch { return; }

            int idx = -1;
            for (int i = _lyricLines.Count - 1; i >= 0; i--)
            {
                if (posMs >= _lyricLines[i].TimeMs - 100)
                {
                    idx = i;
                    break;
                }
            }

            if (idx == _currentLyricIndex) return;
            _currentLyricIndex = idx;

            for (int i = 0; i < _lyricLines.Count; i++)
            {
                var line = _lyricLines[i];
                if (i == idx)
                {
                    line.UiElement.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
                    line.UiElement.FontSize = 17;
                    line.UiElement.FontWeight = Windows.UI.Text.FontWeights.SemiBold;
                    line.TimeIndicator.Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xE0, 0x40, 0xFB));
                    line.TimeIndicator.FontWeight = Windows.UI.Text.FontWeights.SemiBold;
                    // Pink accent bar
                    var accent = (line.Container.Child as StackPanel)?.Children[0] as Border;
                    if (accent != null)
                        accent.Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xE0, 0x40, 0xFB));
                    line.Container.Background = new SolidColorBrush(Color.FromArgb(0x20, 0xE0, 0x40, 0xFB));
                }
                else
                {
                    line.UiElement.Foreground = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF));
                    line.UiElement.FontSize = 15;
                    line.UiElement.FontWeight = Windows.UI.Text.FontWeights.Normal;
                    line.TimeIndicator.Foreground = new SolidColorBrush(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF));
                    line.TimeIndicator.FontWeight = Windows.UI.Text.FontWeights.Normal;
                    var accent = (line.Container.Child as StackPanel)?.Children[0] as Border;
                    if (accent != null)
                        accent.Background = new SolidColorBrush(Color.FromArgb(0x00, 0xE0, 0x40, 0xFB));
                    line.Container.Background = new SolidColorBrush(Color.FromArgb(0x00, 0x1A, 0x1A, 0x2E));
                }
            }

            if (idx >= 0 && idx < _lyricLines.Count)
            {
                try
                {
                    var el = _lyricLines[idx].Container as FrameworkElement;
                    if (el != null)
                    {
                        var transform = el.TransformToVisual(LyricsScrollViewer);
                        var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                        LyricsScrollViewer.ChangeView(null, LyricsScrollViewer.VerticalOffset + point.Y - 80, null);
                    }
                }
                catch (Exception ex) { LogUnhandled(ex); }
            }
        }

        private async System.Threading.Tasks.Task<string> ExtractEmbeddedLyrics()
        {
            try
            {
                var file = _currentOriginalFile;
                if (file == null) return null;

                var fileBytes = await ReadFileBytesAsync(file);
                if (fileBytes == null || fileBytes.Length < 8) return null;

                return ParseMp4Lyrics(fileBytes);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HyperMedia] ExtractEmbeddedLyrics FAILED: {0}", ex.Message);
                return null;
            }
        }

        private async System.Threading.Tasks.Task<byte[]> ReadFileBytesAsync(StorageFile file)
        {
            try
            {
                using (var stream = await file.OpenReadAsync())
                using (var netStream = stream.AsStreamForRead())
                {
                    long fileSize = (long)stream.Size;
                    long firstChunk = Math.Min(fileSize, 4 * 1024 * 1024);
                    long lastChunk = Math.Min(fileSize, 8 * 1024 * 1024);
                    if (firstChunk + lastChunk > fileSize)
                        lastChunk = fileSize - firstChunk;

                    var bytes = new byte[firstChunk + lastChunk];

                    int totalRead = 0;
                    while (totalRead < firstChunk)
                    {
                        int read = netStream.Read(bytes, totalRead, (int)(firstChunk - totalRead));
                        if (read == 0) break;
                        totalRead += read;
                    }

                    if (lastChunk > 0)
                    {
                        netStream.Seek(fileSize - lastChunk, SeekOrigin.Begin);
                        while (totalRead < bytes.Length)
                        {
                            int read = netStream.Read(bytes, totalRead, bytes.Length - totalRead);
                            if (read == 0) break;
                            totalRead += read;
                        }
                    }

                    Debug.WriteLine("[HyperMedia] ReadFileBytesAsync: read {0} bytes from {1} byte file", totalRead, fileSize);
                    return bytes;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HyperMedia] ReadFileBytesAsync FAILED: {0}", ex.Message);
                return null;
            }
        }

        private string ParseMp4Lyrics(byte[] data)
        {
            try
            {
                int pos = 0;
                Debug.WriteLine("[HyperMedia] MP4 parse: searching for moov→udta→meta→ilst in {0} bytes", data.Length);
                string result = FindAtomRecursive(data, ref pos, data.Length, new[] { "moov", "udta", "meta", "ilst" }, 0);
                if (string.IsNullOrEmpty(result))
                    Debug.WriteLine("[HyperMedia] MP4 parse: no lyrics found in ilst");
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HyperMedia] ParseMp4Lyrics FAILED: {0}", ex.Message);
                return null;
            }
        }

        private string FindAtomRecursive(byte[] data, ref int pos, int end, string[] path, int depth)
        {
            if (depth >= path.Length) return null;
            string targetType = path[depth];

            while (pos + 8 <= end)
            {
                int atomSize = ReadInt32BigEndian(data, pos);
                if (atomSize == 1 && pos + 16 <= end)
                    atomSize = (int)ReadInt64BigEndian(data, pos + 8);
                if (atomSize < 8 || pos + atomSize > end)
                    break;

                byte[] typeBytes = new byte[] { data[pos + 4], data[pos + 5], data[pos + 6], data[pos + 7] };
                string atomType = DecodeAtomType(typeBytes);
                int contentStart = pos + 8;
                int contentEnd = pos + atomSize;

                if (depth == 0)
                    Debug.WriteLine("[HyperMedia] Top-level atom: {0} at {1}, size={2}", atomType, pos, atomSize);

                if (atomType == targetType)
                {
                    if (depth == path.Length - 1)
                    {
                        Debug.WriteLine("[HyperMedia] Found {0} at {1}, size={2}", atomType, pos, atomSize);
                        string result = SearchLyricsInIlst(data, contentStart, contentEnd);
                        if (!string.IsNullOrEmpty(result)) return result;
                    }
                    else
                    {
                        Debug.WriteLine("[HyperMedia] Found {0} at {1}, size={2}, recursing...", atomType, pos, atomSize);
                        int childPos = contentStart;
                        // 'meta' atom has 4-byte version/flags header before children
                        if (atomType == "meta")
                            childPos += 4;
                        string result = FindAtomRecursive(data, ref childPos, contentEnd, path, depth + 1);
                        if (!string.IsNullOrEmpty(result)) return result;
                    }
                }

                pos += atomSize;
            }
            return null;
        }

        private string SearchLyricsInIlst(byte[] data, int start, int end)
        {
            Debug.WriteLine("[HyperMedia] ilst contents: scanning {0} to {1}", start, end);
            int pos = start;
            while (pos + 8 <= end)
            {
                int atomSize = ReadInt32BigEndian(data, pos);
                if (atomSize == 1 && pos + 16 <= end)
                    atomSize = (int)ReadInt64BigEndian(data, pos + 8);
                if (atomSize < 8 || pos + atomSize > end)
                {
                    Debug.WriteLine("[HyperMedia] ilst: bad atom at {0}, size={1}", pos, atomSize);
                    break;
                }

                string typeName = string.Format("{0}{1}{2}{3}",
                    (char)data[pos + 4], (char)data[pos + 5], (char)data[pos + 6], (char)data[pos + 7]);
                Debug.WriteLine("[HyperMedia] ilst item: '{0}' at {1}, size={2}", typeName, pos, atomSize);

                if (data[pos + 4] == 0xA9 && data[pos + 5] == 0x6C && data[pos + 6] == 0x79 && data[pos + 7] == 0x72)
                {
                    Debug.WriteLine("[HyperMedia] >>> Found ©lyr atom!");
                    string result = ExtractDataAtomText(data, pos + 8, pos + atomSize);
                    if (!string.IsNullOrEmpty(result)) return result;
                }

                if (data[pos + 4] == 0x6C && data[pos + 5] == 0x79 && data[pos + 6] == 0x72 && data[pos + 7] == 0x63)
                {
                    Debug.WriteLine("[HyperMedia] >>> Found lyrc atom!");
                    string result = ExtractDataAtomText(data, pos + 8, pos + atomSize);
                    if (!string.IsNullOrEmpty(result)) return result;
                }

                pos += atomSize;
            }
            return null;
        }

        private string ExtractDataAtomText(byte[] data, int contentStart, int atomEnd)
        {
            // Content of ©lyr is typically a 'data' sub-atom:
            // [4B size]['data'][4B type_flag][4B locale][text...]
            int pos = contentStart;
            while (pos + 8 <= atomEnd)
            {
                int subSize = ReadInt32BigEndian(data, pos);
                if (subSize == 1 && pos + 16 <= atomEnd)
                    subSize = (int)ReadInt64BigEndian(data, pos + 8);
                if (subSize < 8 || pos + subSize > atomEnd)
                    break;

                // Check for 'data' sub-atom
                if (data[pos + 4] == 0x64 && data[pos + 5] == 0x61 && data[pos + 6] == 0x74 && data[pos + 7] == 0x61)
                {
                    int payloadStart = pos + 8;
                    if (payloadStart + 8 > atomEnd) break;

                    int typeFlag = ReadInt32BigEndian(data, payloadStart);
                    Debug.WriteLine("[HyperMedia] data atom: type_flag={0}", typeFlag);

                    // type_flag 1 = UTF-8 text, 0 = binary
                    int textStart = payloadStart + 8; // skip type_flag(4) + locale(4)
                    if (textStart >= pos + subSize) break;
                    int textLen = pos + subSize - textStart;
                    if (textLen > 0)
                    {
                        string text = Encoding.UTF8.GetString(data, textStart, textLen).TrimEnd('\0');
                        if (!string.IsNullOrEmpty(text))
                        {
                            Debug.WriteLine("[HyperMedia] Extracted lyrics: {0} chars", text.Length);
                            return text;
                        }
                    }
                }

                pos += subSize;
            }
            return null;
        }

        private string DecodeAtomType(byte[] b)
        {
            // Decode 4 bytes as raw ASCII (handles bytes > 0x7F correctly, unlike UTF-8)
            char[] chars = new char[4];
            for (int i = 0; i < 4; i++)
                chars[i] = (char)b[i];
            return new string(chars);
        }

        private static long ReadInt64BigEndian(byte[] data, int offset)
        {
            if (offset + 8 > data.Length) return 0;
            return ((long)data[offset] << 56) | ((long)data[offset + 1] << 48) |
                   ((long)data[offset + 2] << 40) | ((long)data[offset + 3] << 32) |
                   ((long)data[offset + 4] << 24) | ((long)data[offset + 5] << 16) |
                   ((long)data[offset + 6] << 8) | data[offset + 7];
        }

        private static int ReadInt32BigEndian(byte[] data, int offset)
        {
            if (offset + 4 > data.Length) return 0;
            return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
        }

        #endregion

        #region Overlay

        private async void HideOverlayDelayed()
        {
            await Task.Delay(OVERLAY_HIDE_DELAY_MS);
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
                    RestartSleepTimer();
                    ApplyVolumeFadeIn();

                    if (_vlcMedia != null)
                    {
                        string title = _vlcMedia.meta(MediaMeta.Title);
                        bool titleIsTemp = !string.IsNullOrEmpty(title) &&
                            title.IndexOf("hypermedia_temp", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (!string.IsNullOrEmpty(title) && !titleIsTemp)
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
                            else if (!string.IsNullOrEmpty(_originalFileName))
                            {
                                FileNameText.Text = _originalFileName;
                            }
                        }
                    }

                    DetectMusicMode();

                    if (!_isNetworkStream && !string.IsNullOrEmpty(_originalFileName))
                        PlayHistory.IncrementPlayCount(_originalFileName);

                    if (_lyricLines.Count > 0 && !_lyricTimer.IsEnabled)
                        _lyricTimer.Start();

                    UpdateLiveTile();

                    UpdateRatingBtnIcon();

                    // Re-apply runtime-only settings after media reload
                    try
                    {
                        if (!string.IsNullOrEmpty(_cropGeometry) && _vlcPlayer != null)
                            _vlcPlayer.setCropGeometry(_cropGeometry);
                        if (_nightMode && _vlcPlayer != null)
                        {
                            _vlcPlayer.setAdjustFloat(0, 0.72f);
                            _vlcPlayer.setAdjustFloat(1, 0.88f);
                            _vlcPlayer.setAdjustFloat(2, -10f);
                            _vlcPlayer.setAdjustFloat(3, 0.9f);
                            _vlcPlayer.setAdjustFloat(4, 1.08f);
                        }
                    }
                    catch (Exception ex) { LogUnhandled(ex); }

                    // Resume from saved position
                    if (_originalFileName != null && !_isNetworkStream)
                    {
                        long resumePos = LoadResumePosition(_originalFileName);
                        if (resumePos > 0)
                        {
                            _pendingResumePos = resumePos;
                            Debug.WriteLine("[HyperMedia] Attempting resume seek to {0}ms", resumePos);
                            bool seekOk = false;
                            try { _vlcPlayer.setTime(resumePos); seekOk = true; }
                            catch (Exception ex) { Debug.WriteLine("[HyperMedia] setTime FAILED: {0}", ex.Message); }
                            if (seekOk)
                            {
                                RemoveResumePosition(_originalFileName);
                                ShowOverlay(L("ResumeRestoredPrefix") + FormatTime(resumePos / 1000.0));
                                HideOverlayDelayed();
                            }
                        }
                        else
                        {
                            ApplyIntroSkipIfNeeded();
                        }
                    }
                }
                catch (Exception ex) { LogUnhandled(ex); }
            });
        }

        private void OnVlcPaused()
        {
            _isPlaying = false;
            BeginInvokeOnUI(() => _lyricTimer.Stop());
        }

        private void OnVlcStopped()
        {
            _isPlaying = false;
            BeginInvokeOnUI(() =>
            {
                _lyricTimer.Stop();
                _positionTimer.Stop();
            });
        }

        private void OnVlcEndReached()
        {
            _isPlaying = false;
            BeginInvokeOnUI(() =>
            {
                _lyricTimer.Stop();
                _positionTimer.Stop();
                UpdatePlayPauseIcon(false);

                // Played to completion — clear resume marker so HomePage shows no 续播 badge
                if (!_isNetworkStream && !string.IsNullOrEmpty(_originalFileName))
                    RemoveResumePosition(_originalFileName);

                if (_repeatMode == 2 || _playlist.Count > 1)
                {
                    _autoAdvancing = true;
                    PlayNext();
                    _autoAdvancing = false;
                }
                else
                {
                    StatusText.Text = L("PlaybackComplete");
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
                StatusText.Text = L("PlaybackError");
                ShowControls();
                ShowToast(L("PlaybackError"), L("PlaybackErrorDetail"));
            });
        }

        private async void OnSnapshotTaken(string filename)
        {
            Debug.WriteLine("[HyperMedia] OnSnapshotTaken callback: filename={0}", filename);

            // Wait for file flush to disk — takeSnapshot is async, file may not be flushed yet
            bool fileFound = false;
            for (int i = 0; i < SNAPSHOT_RETRY_COUNT; i++)
            {
                await Task.Delay(SNAPSHOT_RETRY_DELAY_MS);
                try
                {
                    if (!string.IsNullOrEmpty(_lastScreenshotPath))
                    {
                        var test = await StorageFile.GetFileFromPathAsync(_lastScreenshotPath);
                        if (test != null)
                        {
                            var props = await test.GetBasicPropertiesAsync();
                            if (props.Size > 0)
                            {
                                Debug.WriteLine("[HyperMedia] Screenshot file found after {0} retries, size={1}", i + 1, props.Size);
                                fileFound = true;
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex) { LogUnhandled(ex); }
            }

            string path = _lastScreenshotPath;
            string savedPath = path;

            // Copy to Pictures library
            if (fileFound && !string.IsNullOrEmpty(path))
            {
                try
                {
                    var tempFile = await StorageFile.GetFileFromPathAsync(path);
                    var picturesFolder = KnownFolders.PicturesLibrary;
                    var savedFile = await tempFile.CopyAsync(picturesFolder, _lastScreenshotFileName, NameCollisionOption.ReplaceExisting);
                    savedPath = savedFile.Path;
                    Debug.WriteLine("[HyperMedia] Screenshot copied to Pictures: {0}", savedPath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[HyperMedia] Copy to Pictures failed: {0}, using temp path", ex.Message);
                    savedPath = path;
                }
            }

            string finalPath = savedPath;
            BeginInvokeOnUI(() =>
            {
                Debug.WriteLine("[HyperMedia] Checking screenshot file: {0}, found={1}", finalPath, fileFound);

                if (fileFound && !string.IsNullOrEmpty(finalPath))
                {
                    Debug.WriteLine("[HyperMedia] Screenshot file confirmed: {0}", finalPath);
                    OverlayText.Text = "截图已保存: " + System.IO.Path.GetFileName(finalPath);
                    OverlayOpenBtn.Visibility = Visibility.Visible;
                    OverlayOpenBtn.Tag = finalPath;
                    OverlayNotification.Visibility = Visibility.Visible;
                    _overlayNotifyTimer.Stop();
                    _overlayNotifyTimer.Start();
                }
                else
                {
                    Debug.WriteLine("[HyperMedia] Screenshot file NOT found at: {0}", finalPath);
                    OverlayText.Text = "截图失败：文件未创建 (path=" + (finalPath ?? "null") + ")";
                    OverlayNotification.Visibility = Visibility.Visible;
                    _overlayNotifyTimer.Stop();
                    _overlayNotifyTimer.Start();
                }
            });
        }

        private void OnVlcLengthChanged(long length)
        {
            if (length > 0)
            {
                _duration = length / 1000.0;
                Debug.WriteLine("[HyperMedia] OnVlcLengthChanged: {0}ms ({1}s)", length, _duration);
                BeginInvokeOnUI(() =>
                {
                    DurationText.Text = FormatTime(_duration);
                    PositionSlider.Maximum = _duration;

                    if (_pendingResumePos > 0 && _vlcPlayer != null)
                    {
                        Debug.WriteLine("[HyperMedia] Retrying resume seek to {0}ms (on length changed)", _pendingResumePos);
                        try
                        {
                            _vlcPlayer.setTime(_pendingResumePos);
                            RemoveResumePosition(_originalFileName);
                            ShowOverlay(L("ResumeRestoredPrefix") + FormatTime(_pendingResumePos / 1000.0));
                            HideOverlayDelayed();
                            _pendingResumePos = 0;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("[HyperMedia] Retry setTime FAILED: {0}", ex.Message);
                        }
                    }
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

        private const int VOLUME_FADE_STEPS = 10;
        private const int VOLUME_FADE_INTERVAL_MS = 40;

        private DispatcherTimer _volumeFadeTimer;
        private int _volumeFadeCurrent;

        private void ApplyVolumeFadeIn()
        {
            try
            {
                if (_vlcPlayer == null) return;
                int target = (int)VolumeSlider.Value;
                if (target <= 0) return;

                if (_volumeFadeTimer == null)
                {
                    _volumeFadeTimer = new DispatcherTimer();
                    _volumeFadeTimer.Interval = TimeSpan.FromMilliseconds(VOLUME_FADE_INTERVAL_MS);
                    _volumeFadeTimer.Tick += (s, ev) =>
                    {
                        try
                        {
                            _volumeFadeCurrent += target / VOLUME_FADE_STEPS;
                            if (_volumeFadeCurrent >= target)
                            {
                                _volumeFadeCurrent = target;
                                _volumeFadeTimer.Stop();
                            }
                            if (_vlcPlayer != null)
                                _vlcPlayer.setVolume(_volumeFadeCurrent);
                        }
                        catch (Exception ex) { LogUnhandled(ex); }
                    };
                }
                _volumeFadeTimer.Stop();
                _volumeFadeCurrent = 0;
                _volumeFadeTimer.Start();
            }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        private void RestartSleepTimer()
        {
            int minutes = SettingsPage.GetSleepTimer();
            if (minutes > 0)
            {
                _sleepRemainingSeconds = minutes * 60;
                _sleepTimer.Start();
            }
            else
            {
                _sleepTimer.Stop();
            }
        }

        private void SleepTimer_Tick(object sender, object e)
        {
            _sleepRemainingSeconds--;
            if (_sleepRemainingSeconds <= 0)
            {
                _sleepTimer.Stop();
                SaveResumePosition();
                StopPlayback();
                WelcomeScreen.Visibility = Visibility.Visible;
                FileNameText.Text = "";
                ShowOverlay(L("SleepTimerStopped"));
                HideOverlayDelayed();
                ShowToast(L("SleepTimer"), L("PlaybackStoppedToast"));
            }
        }

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
            StatusText.Text = "就绪";
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
            _lyricTimer.Stop();
            _isPlaying = false;
            _isSeeking = false;
            _pendingResumePos = 0;

            StopSmtcSync();

            if (_vlcPlayer != null)
            {
                _vlcPlayer.stop();
                _vlcPlayer = null;
            }
            _vlcMedia = null;

            if (_tempFile != null)
            {
                try { var _ = _tempFile.DeleteAsync().AsTask(); }
                catch (Exception ex) { LogUnhandled(ex); }
                _tempFile = null;
            }

            PositionSlider.Value = 0;
            CurrentTimeText.Text = "00:00";
            DurationText.Text = "00:00";
            UpdatePlayPauseIcon(false);
            HideOverlay();
            MusicOverlay.Visibility = Visibility.Collapsed;
            _isMusicMode = false;
            _currentMusicFilePath = "";
            _currentMusicOriginalDir = "";
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

            // Auto-skip intro learning: manual forward seek into 25s-5min zone = "intro ends here"
            if (!_isNetworkStream && !string.IsNullOrEmpty(_originalFileName) && _vlcPlayer != null && !_isSeeking)
            {
                TryLearnIntroSkip(seconds);
            }
        }

        private const string KEY_SKIP_INTRO = "SkipIntro_";
        private const int INTRO_MIN_MS = 25000;
        private const int INTRO_MAX_MS = 300000;
        private bool _introAutoSkippedThisSession = false;

        private void TryLearnIntroSkip(double seconds)
        {
            try
            {
                if (seconds < INTRO_MIN_MS / 1000.0 || seconds > INTRO_MAX_MS / 1000.0)
                {
                    if (seconds < 5 && _vlcPlayer != null)
                    {
                        // User went back to start — clear learned intro
                        var settings = ApplicationData.Current.LocalSettings;
                        settings.Values.Remove(KEY_SKIP_INTRO + _originalFileName);
                        Debug.WriteLine("[HyperMedia] Intro skip cleared (user seeked to start)");
                    }
                    return;
                }
                var settings2 = ApplicationData.Current.LocalSettings;
                settings2.Values[KEY_SKIP_INTRO + _originalFileName] = (long)(seconds * 1000);
                Debug.WriteLine("[HyperMedia] Intro skip learned: {0}s for {1}", (int)seconds, _originalFileName);
            }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        private void ApplyIntroSkipIfNeeded()
        {
            if (_isNetworkStream || string.IsNullOrEmpty(_originalFileName)) return;
            if (_introAutoSkippedThisSession) return;
            if (!SettingsPage.GetIntroSkipEnabled()) return;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                string key = KEY_SKIP_INTRO + _originalFileName;
                if (settings.Values.ContainsKey(key))
                {
                    long skipTo = (long)settings.Values[key];
                    if (skipTo > INTRO_MIN_MS && skipTo < INTRO_MAX_MS && _vlcPlayer != null)
                    {
                        _vlcPlayer.setTime(skipTo);
                        _introAutoSkippedThisSession = true;
                        ShowOverlay(L("IntroSkippedPrefix") + FormatTime(skipTo / 1000.0));
                        HideOverlayDelayed();
                        Debug.WriteLine("[HyperMedia] Auto-skipped intro to {0}ms for {1}", skipTo, _originalFileName);
                    }
                }
            }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            int vol = (int)e.NewValue;
            if (_volumeFadeTimer != null)
                _volumeFadeTimer.Stop();
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
                VolumeIcon.Text = "\uD83D\uDD07";
            else if (vol < 66)
                VolumeIcon.Text = "\uD83D\uDD09";
            else
                VolumeIcon.Text = "\uD83D\uDD0A";
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
                catch (Exception ex) { LogUnhandled(ex); }
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

        private void UpdateShuffleIcon()
        {
            if (_shuffleOn)
                ShuffleIcon.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x40, 0xFB));
            else
                ShuffleIcon.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
        }

        private void ShuffleButton_Click(object sender, RoutedEventArgs e)
        {
            _shuffleOn = !_shuffleOn;
            UpdateShuffleIcon();

            if (_shuffleOn)
            {
                if (_playlist.Count > 1)
                    ShufflePlaylistFromCurrent();
                UpdatePlaylistCounter();
            }
        }

        #endregion

        #region Media Info

        private void MediaInfoButton_Click(object sender, RoutedEventArgs e)
        {
            ShowMediaInfo();
        }

        private async void ShowMediaInfo()
        {
            if (_vlcMedia == null && _playlist.Count == 0) return;

            MediaInfoFileName.Text = _originalFileName ?? (_playlist.Count > 0 && _playlistIndex >= 0
                ? _playlist[_playlistIndex].Name : "");

            MediaInfoVideoTrack.Text = "";
            MediaInfoAudioTrack.Text = "";
            MediaInfoSubCount.Text = "";
            MediaInfoMetaTitle.Text = "";
            MediaInfoMetaArtist.Text = "";
            MediaInfoMetaAlbum.Text = "";
            MediaInfoMetaDate.Text = "";
            MediaInfoFileSize.Text = "";
            MediaInfoDuration.Text = "";

            try
            {
                if (_vlcMedia != null)
                {
                    string title = _vlcMedia.meta(MediaMeta.Title);
                    string artist = _vlcMedia.meta(MediaMeta.Artist);
                    string album = _vlcMedia.meta(MediaMeta.Album);
                    string date = _vlcMedia.meta(MediaMeta.Date);
                    string nowPlaying = _vlcMedia.meta(MediaMeta.NowPlaying);

                    if (!string.IsNullOrEmpty(title) && title != "hypermedia_temp")
                        MediaInfoMetaTitle.Text = L("TitleLabel") + title;
                    if (!string.IsNullOrEmpty(artist) && artist != "hypermedia_temp")
                        MediaInfoMetaArtist.Text = L("ArtistLabel") + artist;
                    if (!string.IsNullOrEmpty(album) && album != "hypermedia_temp")
                        MediaInfoMetaAlbum.Text = L("AlbumLabel") + album;
                    if (!string.IsNullOrEmpty(date) && date != "hypermedia_temp")
                        MediaInfoMetaDate.Text = L("DateLabel") + date;
                    if (!string.IsNullOrEmpty(nowPlaying) && nowPlaying != "hypermedia_temp")
                    {
                        if (string.IsNullOrEmpty(MediaInfoMetaTitle.Text))
                            MediaInfoMetaTitle.Text = L("TitleLabel") + nowPlaying;
                    }
                }
            }
            catch (Exception ex) { LogUnhandled(ex); }

            try
            {
                if (_vlcPlayer != null)
                {
                    int videoCount = _vlcPlayer.videoTrackCount();
                    if (videoCount > 0)
                    {
                        var videoDescs = _vlcPlayer.videoTrackDescription();
                        if (videoDescs != null)
                        {
                            foreach (var v in videoDescs)
                                MediaInfoVideoTrack.Text += (MediaInfoVideoTrack.Text.Length > 0 ? ", " : L("VideoLabel")) + (v.name() ?? "Track " + v.id());
                        }
                    }
                    else
                    {
                        MediaInfoVideoTrack.Text = L("VideoNone");
                    }

                    int audioCount = _vlcPlayer.audioTrackCount();
                    if (audioCount > 0)
                    {
                        var audioDescs = _vlcPlayer.audioTrackDescription();
                        if (audioDescs != null)
                        {
                            foreach (var a in audioDescs)
                                MediaInfoAudioTrack.Text += (MediaInfoAudioTrack.Text.Length > 0 ? ", " : L("AudioLabel")) + (a.name() ?? "Track " + a.id());
                        }
                    }

                    int spuCount = _vlcPlayer.spuCount();
                    if (spuCount > 0)
                        MediaInfoSubCount.Text = L("SubtitleTrackLabel") + spuCount;
                }
            }
            catch (Exception ex) { LogUnhandled(ex); }

            if (_duration > 0)
            {
                TimeSpan ts = TimeSpan.FromSeconds(_duration);
                MediaInfoDuration.Text = string.Format(L("DurationLabel") + string.Format("{0:D2}:{1:D2}:{2:D2}", ts.Hours, ts.Minutes, ts.Seconds));
            }

            try
            {
                if (_playlist.Count > 0 && _playlistIndex >= 0 && _playlistIndex < _playlist.Count)
                {
                    var file = _playlist[_playlistIndex];
                    var props = await file.GetBasicPropertiesAsync();
                    ulong bytes = props.Size;
                    if (bytes > 1024 * 1024 * 1024)
                        MediaInfoFileSize.Text = string.Format(L("FileSizeGb"), bytes / (1024.0 * 1024 * 1024));
                    else if (bytes > 1024 * 1024)
                        MediaInfoFileSize.Text = string.Format(L("FileSizeMb"), bytes / (1024.0 * 1024));
                    else
                        MediaInfoFileSize.Text = string.Format(L("FileSizeKb"), bytes / 1024.0);
                }
            }
            catch (Exception ex) { LogUnhandled(ex); }

            MediaInfoOverlay.Visibility = Visibility.Visible;
        }

        private void MediaInfoOverlay_Close(object sender, RoutedEventArgs e)
        {
            MediaInfoOverlay.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region Photo Viewer

        private bool _isPhotoMode;
        private bool _isSlideshow;
        private DispatcherTimer _slideshowTimer;
        private double _photoZoom = 1.0;
        private double _photoRotation = 0;

        private static readonly HashSet<string> PHOTO_EXTENSIONS = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".webp"
        };

        private bool IsPhotoFile(StorageFile file)
        {
            if (file == null) return false;
            string ext = System.IO.Path.GetExtension(file.Name);
            return PHOTO_EXTENSIONS.Contains(ext);
        }

        private async void OpenPhoto(StorageFile file)
        {
            try
            {
                _isPhotoMode = true;
                WelcomeScreen.Visibility = Visibility.Collapsed;
                VlcVideoPanel.Visibility = Visibility.Collapsed;
                TopBar.Visibility = Visibility.Collapsed;
                BottomBar.Visibility = Visibility.Collapsed;
                _autoHideTimer.Stop();
                PlaylistSidebar.Visibility = Visibility.Collapsed;
                PhotoViewerOverlay.Visibility = Visibility.Visible;

                var stream = await file.OpenReadAsync();
                var bitmap = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                await bitmap.SetSourceAsync(stream);
                PhotoImage.Source = bitmap;

                FileNameText.Text = file.Name;
                PhotoFileName.Text = file.Name;
                UpdatePhotoCounter();

                _photoZoom = 1.0;
                _photoRotation = 0;
                PhotoTransform.ScaleX = 1;
                PhotoTransform.ScaleY = 1;
                PhotoTransform.Rotation = 0;
                PhotoZoomText.Text = "100%";
            }
            catch (Exception ex)
            {
                ShowOverlay(L("OpenImageFailedPrefix") + ex.Message);
                HideOverlayDelayed();
                ClosePhotoViewer();
            }
        }

        private void ClosePhotoViewer()
        {
            _isPhotoMode = false;
            StopSlideshow();
            PhotoViewerOverlay.Visibility = Visibility.Collapsed;
            VlcVideoPanel.Visibility = Visibility.Visible;
            PhotoImage.Source = null;
        }

        private void PhotoCloseBtn_Click(object sender, RoutedEventArgs e)
        {
            ClosePhotoViewer();
            WelcomeScreen.Visibility = Visibility.Visible;
            FileNameText.Text = "";
            StatusText.Text = "";
        }

        private void UpdatePhotoCounter()
        {
            var photoItems = _playlist.Where(f => IsPhotoFile(f)).ToList();
            if (photoItems.Count > 1 && _playlistIndex >= 0 && _playlistIndex < _playlist.Count)
            {
                int photoIdx = photoItems.IndexOf(_playlist[_playlistIndex]) + 1;
                PhotoCounter.Text = string.Format("{0} / {1}", photoIdx, photoItems.Count);
            }
            else
            {
                PhotoCounter.Text = "";
            }
        }

        private void PhotoZoomInBtn_Click(object sender, RoutedEventArgs e)
        {
            _photoZoom = Math.Min(_photoZoom * 1.25, 5.0);
            PhotoTransform.ScaleX = _photoZoom;
            PhotoTransform.ScaleY = _photoZoom;
            PhotoZoomText.Text = ((int)(_photoZoom * 100)) + "%";
        }

        private void PhotoZoomOutBtn_Click(object sender, RoutedEventArgs e)
        {
            _photoZoom = Math.Max(_photoZoom / 1.25, 0.1);
            PhotoTransform.ScaleX = _photoZoom;
            PhotoTransform.ScaleY = _photoZoom;
            PhotoZoomText.Text = ((int)(_photoZoom * 100)) + "%";
        }

        private void PhotoZoomResetBtn_Click(object sender, RoutedEventArgs e)
        {
            _photoZoom = 1.0;
            PhotoTransform.ScaleX = 1;
            PhotoTransform.ScaleY = 1;
            PhotoZoomText.Text = "100%";
        }

        private void PhotoRotateBtn_Click(object sender, RoutedEventArgs e)
        {
            _photoRotation = (_photoRotation + 90) % 360;
            PhotoTransform.Rotation = _photoRotation;
        }

        private void PhotoSlideshowBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isSlideshow)
                StopSlideshow();
            else
                StartSlideshow();
        }

        private void StartSlideshow()
        {
            var photoItems = _playlist.Where(f => IsPhotoFile(f)).ToList();
            if (photoItems.Count <= 1) return;

            _isSlideshow = true;
            PhotoSlideshowIcon.Text = "\u23F8";
            PhotoSlideshowIcon.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x40, 0xFB));

            if (_slideshowTimer == null)
            {
                _slideshowTimer = new DispatcherTimer();
                _slideshowTimer.Interval = TimeSpan.FromSeconds(SLIDESHOW_INTERVAL_SECONDS);
                _slideshowTimer.Tick += SlideshowTimer_Tick;
            }
            _slideshowTimer.Start();
        }

        private void StopSlideshow()
        {
            _isSlideshow = false;
            PhotoSlideshowIcon.Text = "\u25B6";
            PhotoSlideshowIcon.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
            if (_slideshowTimer != null)
                _slideshowTimer.Stop();
        }

        private void SlideshowTimer_Tick(object sender, object e)
        {
            GoToNextPhoto();
        }

        private void GoToNextPhoto()
        {
            var photoItems = _playlist.Where(f => IsPhotoFile(f)).ToList();
            if (photoItems.Count == 0) return;

            int currentPhotoIdx = photoItems.IndexOf(_playlist[_playlistIndex]);
            int nextIdx = (currentPhotoIdx + 1) % photoItems.Count;
            int playlistIdx = _playlist.IndexOf(photoItems[nextIdx]);

            _playlistIndex = playlistIdx;
            OpenPhoto(_playlist[_playlistIndex]);
        }

        private void GoToPrevPhoto()
        {
            var photoItems = _playlist.Where(f => IsPhotoFile(f)).ToList();
            if (photoItems.Count == 0) return;

            int currentPhotoIdx = photoItems.IndexOf(_playlist[_playlistIndex]);
            int prevIdx = (currentPhotoIdx - 1 + photoItems.Count) % photoItems.Count;
            int playlistIdx = _playlist.IndexOf(photoItems[prevIdx]);

            _playlistIndex = playlistIdx;
            OpenPhoto(_playlist[_playlistIndex]);
        }

        private void PhotoPrevBtn_Click(object sender, RoutedEventArgs e)
        {
            StopSlideshow();
            GoToPrevPhoto();
        }

        private void PhotoNextBtn_Click(object sender, RoutedEventArgs e)
        {
            StopSlideshow();
            GoToNextPhoto();
        }

        private void PhotoScrollViewer_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (_photoZoom > 1.0)
            {
                _photoZoom = 1.0;
                PhotoTransform.ScaleX = 1;
                PhotoTransform.ScaleY = 1;
            }
            else
            {
                _photoZoom = 2.0;
                PhotoTransform.ScaleX = 2;
                PhotoTransform.ScaleY = 2;
            }
            PhotoZoomText.Text = ((int)(_photoZoom * 100)) + "%";
        }

        #endregion

        #region Subtitle / Audio Track / Snapshot

        private int _currentSpu = -1;

        private void SubtitleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_vlcPlayer == null) return;

            try
            {
                var menu = new MenuFlyout();
                menu.Placement = FlyoutPlacementMode.Bottom;

                // Disable subtitles
                var disableItem = new MenuFlyoutItem();
                disableItem.Text = L("DisableSubtitles");
                disableItem.Tapped += (s, ev) =>
                {
                    try
                    {
                        _vlcPlayer.setSpu(-1);
                        _currentSpu = -1;
                        ShowOverlay(L("SubtitlesOff"));
                        HideOverlayDelayed();
                    }
                    catch (Exception ex) { LogUnhandled(ex); }
                };
                menu.Items.Add(disableItem);

                // Enumerate embedded subtitle tracks
                try
                {
                    int spuCount = _vlcPlayer.spuCount();
                    if (spuCount > 0)
                    {
                        var descriptions = _vlcPlayer.spuDescription();
                        if (descriptions != null)
                        {
                            foreach (var desc in descriptions)
                            {
                                var trackItem = new MenuFlyoutItem();
                                int tid = desc.id();
                                string trackName = desc.name() ?? (L("SubtitleTrack") + tid);
                                trackItem.Text = trackName;
                                trackItem.Tapped += (s, ev) =>
                                {
                                    try
                                    {
                                        _vlcPlayer.setSpu(tid);
                                        _currentSpu = tid;
                                        ShowOverlay(L("SwitchedPrefix") + trackName);
                                        HideOverlayDelayed();
                                    }
                                    catch (Exception ex) { LogUnhandled(ex); }
                                };
                                menu.Items.Add(trackItem);
                            }
                        }
                    }
                }
                catch (Exception ex) { LogUnhandled(ex); }

                // Load external subtitle
                var loadExternal = new MenuFlyoutItem();
                loadExternal.Text = L("LoadExternalSubtitle");
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

                            _pendingExternalSubPath = tempSub.Path;

                            // Stop current playback, re-open with subtitle
                            string currentPath = _playlist[_playlistIndex].Path;
                            StopPlayback();
                            OpenFile(_playlist[_playlistIndex]);

                            ShowOverlay(L("SubtitleLoadedPrefix") + file.Name);
                            HideOverlayDelayed();
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowOverlay(L("SubtitleErrorPrefix") + ex.Message);
                        HideOverlayDelayed();
                    }
                };
                menu.Items.Add(loadExternal);

                menu.ShowAt(SubtitleButton);
            }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        private void AudioTrackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_vlcPlayer == null) return;

            try
            {
                var menu = new MenuFlyout();
                menu.Placement = FlyoutPlacementMode.Bottom;

                // Enumerate audio tracks
                try
                {
                    int trackCount = _vlcPlayer.audioTrackCount();
                    if (trackCount > 0)
                    {
                        var descriptions = _vlcPlayer.audioTrackDescription();
                        if (descriptions != null)
                        {
                            foreach (var desc in descriptions)
                            {
                                var trackItem = new MenuFlyoutItem();
                                int tid = desc.id();
                                string trackName = desc.name() ?? (L("AudioTrackItem") + tid);
                                trackItem.Text = trackName;
                                trackItem.Tapped += (s, ev) =>
                                {
                                    try
                                    {
                                        _vlcPlayer.setAudioTrack(tid);
                                        ShowOverlay(L("SwitchedPrefix") + trackName);
                                        HideOverlayDelayed();
                                    }
                                    catch (Exception ex) { LogUnhandled(ex); }
                                };
                                menu.Items.Add(trackItem);
                            }
                        }
                    }
                }
                catch (Exception ex) { LogUnhandled(ex); }

                if (menu.Items.Count == 0)
                {
                    var noTrack = new MenuFlyoutItem();
                    noTrack.Text = L("NoAudioTracks");
                    noTrack.IsEnabled = false;
                    menu.Items.Add(noTrack);
                }

                menu.ShowAt(AudioTrackButton);
            }
            catch (Exception ex) { LogUnhandled(ex); }
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
            catch (Exception ex) { LogUnhandled(ex); }
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
            bool ctrl = (Window.Current.CoreWindow.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0;
            bool shift = (Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0;
            bool alt = (Window.Current.CoreWindow.GetKeyState(VirtualKey.Menu) & CoreVirtualKeyStates.Down) != 0;

            if (_isPhotoMode)
            {
                switch (e.Key)
                {
                    case VirtualKey.Left:
                        GoToPrevPhoto();
                        break;
                    case VirtualKey.Right:
                        GoToNextPhoto();
                        break;
                    case VirtualKey.Escape:
                        ClosePhotoViewer();
                        WelcomeScreen.Visibility = Visibility.Visible;
                        FileNameText.Text = "";
                        break;
                    default:
                        handled = false;
                        break;
                }
                e.Handled = handled;
                return;
            }

            switch (e.Key)
            {
                case VirtualKey.Space:
                    TogglePlayPause();
                    break;
                case VirtualKey.Left:
                    if (ctrl && alt) { NudgeAbPoint(-FRAME_STEP_MS); }
                    else if (ctrl && shift) { AdjustVideoContrast(-0.05f); }
                    else if (ctrl) { _audioDelay -= AV_SYNC_STEP; ApplyAudioDelay(); ShowOverlay(L("AudioDelayPrefix") + _audioDelay + "ms"); HideOverlayDelayed(); }
                    else SeekRelative(-10);
                    break;
                case VirtualKey.Right:
                    if (ctrl && alt) { NudgeAbPoint(FRAME_STEP_MS); }
                    else if (ctrl && shift) { AdjustVideoContrast(0.05f); }
                    else if (ctrl) { _audioDelay += AV_SYNC_STEP; ApplyAudioDelay(); ShowOverlay(L("AudioDelayPrefix") + _audioDelay + "ms"); HideOverlayDelayed(); }
                    else SeekRelative(10);
                    break;
                case VirtualKey.Up:
                    if (ctrl && shift) { AdjustVideoBrightness(0.05f); }
                    else if (ctrl) { _subtitleDelay += AV_SYNC_STEP; ApplySubtitleDelay(); ShowOverlay(L("SubtitleDelayPrefix") + _subtitleDelay + "ms"); HideOverlayDelayed(); }
                    else AdjustVolume(5);
                    break;
                case VirtualKey.Down:
                    if (ctrl && shift) { AdjustVideoBrightness(-0.05f); }
                    else if (ctrl) { _subtitleDelay -= AV_SYNC_STEP; ApplySubtitleDelay(); ShowOverlay(L("SubtitleDelayPrefix") + _subtitleDelay + "ms"); HideOverlayDelayed(); }
                    else AdjustVolume(-5);
                    break;
                case VirtualKey.F:
                    ToggleFullscreen();
                    break;
                case VirtualKey.Escape:
                    if (_isFullscreen) ToggleFullscreen();
                    else if (EqualizerOverlay.Visibility == Visibility.Visible) EqualizerOverlay.Visibility = Visibility.Collapsed;
                    else if (VideoFilterOverlay.Visibility == Visibility.Visible) VideoFilterOverlay.Visibility = Visibility.Collapsed;
                    else if (StatsOverlay.Visibility == Visibility.Visible) StatsOverlay.Visibility = Visibility.Collapsed;
                    else if (ShortcutsOverlay.Visibility == Visibility.Visible) ShortcutsOverlay.Visibility = Visibility.Collapsed;
                    else if (MediaInfoOverlay.Visibility == Visibility.Visible) MediaInfoOverlay.Visibility = Visibility.Collapsed;
                    else handled = false;
                    break;
                case VirtualKey.B:
                    if (ctrl && shift) ShowBookmarks();
                    else if (ctrl) ToggleBookmark();
                    else ToggleAbRepeat();
                    break;
                case VirtualKey.L:
                    PlaylistToggleButton_Click(null, null);
                    break;
                case VirtualKey.M:
                    if (!_isMusicMode) ToggleMiniPlayer();
                    break;
                case VirtualKey.S:
                    if (_isMusicMode) { handled = false; break; }
                    if (ctrl) TakeScreenshotBurst();
                    else TakeScreenshot();
                    break;
                case (VirtualKey)190: // Period
                    if (!_isMusicMode) StepForward();
                    break;
                case (VirtualKey)188: // Comma
                    if (!_isMusicMode) StepBackward();
                    break;
                case (VirtualKey)191: // Question/Slash
                    ShortcutsOverlay.Visibility = ShortcutsOverlay.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                    break;
                case VirtualKey.N:
                    if (ctrl) PlayNext();
                    else handled = false;
                    break;
                case VirtualKey.P:
                    if (ctrl) PlayPrev();
                    else handled = false;
                    break;
                case VirtualKey.O:
                    if (ctrl) OpenFileFromPicker();
                    else handled = false;
                    break;
                case VirtualKey.U:
                    if (ctrl) ShowUrlInputOverlay();
                    else handled = false;
                    break;
                case VirtualKey.E:
                    if (ctrl) OpenFolderBtn_Click(null, null);
                    else handled = false;
                    break;
                case VirtualKey.I:
                    if (ctrl) StatsBtn_Click(null, null);
                    else handled = false;
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

        private bool _isSwiping;
        private int _gestureMode = 0; // 0=none, 1=brightness(left), 2=volume(right)
        private double _gestureAccum = 0;

        private const double GESTURE_STEP = 10;

        private void VideoArea_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
        {
            _isSwiping = false;
            _gestureMode = 0;
            _gestureAccum = 0;
        }

        private void VideoArea_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            double deltaX = e.Cumulative.Translation.X;
            double deltaY = e.Cumulative.Translation.Y;

            // Determine gesture mode on first significant movement
            if (_gestureMode == 0 && (Math.Abs(deltaX) > 20 || Math.Abs(deltaY) > 20))
            {
                if (Math.Abs(deltaY) > Math.Abs(deltaX))
                {
                    // Vertical gesture: brightness (left half) / volume (right half)
                    var point = e.Position;
                    double width = VlcVideoPanel.ActualWidth > 0 ? VlcVideoPanel.ActualWidth : 800;
                    _gestureMode = (point.X < width / 2) ? 1 : 2;
                }
                else
                {
                    _gestureMode = -1; // horizontal seek
                    _isSwiping = true;
                    SwipeIndicator.Visibility = Visibility.Visible;
                }
            }

            if (_gestureMode == 1)
            {
                _gestureAccum += e.Delta.Translation.Y;
                while (_gestureAccum > GESTURE_STEP)
                {
                    _gestureAccum -= GESTURE_STEP;
                    AdjustGestureBrightness(-0.03f);
                }
                while (_gestureAccum < -GESTURE_STEP)
                {
                    _gestureAccum += GESTURE_STEP;
                    AdjustGestureBrightness(0.03f);
                }
                e.Handled = true;
            }
            else if (_gestureMode == 2)
            {
                _gestureAccum += e.Delta.Translation.Y;
                while (_gestureAccum > GESTURE_STEP)
                {
                    _gestureAccum -= GESTURE_STEP;
                    AdjustVolume(-3);
                }
                while (_gestureAccum < -GESTURE_STEP)
                {
                    _gestureAccum += GESTURE_STEP;
                    AdjustVolume(3);
                }
                e.Handled = true;
            }
        }

        private void AdjustGestureBrightness(float delta)
        {
            if (_vlcPlayer == null) return;
            try
            {
                _videoBrightness = Math.Max(0.3f, Math.Min(1.7f, _videoBrightness + delta));
                _vlcPlayer.setAdjustFloat(0, _videoBrightness);
            }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        private void AdjustVideoBrightness(float delta)
        {
            AdjustGestureBrightness(delta);
            if (_vlcPlayer != null)
                ShowOverlay(L("BrightnessPrefix") + ((int)(_videoBrightness * 100)) + "%");
            HideOverlayDelayed();
        }

        private void AdjustVideoContrast(float delta)
        {
            if (_vlcPlayer == null) return;
            try
            {
                _videoContrast = Math.Max(0.3f, Math.Min(1.7f, _videoContrast + delta));
                _vlcPlayer.setAdjustFloat(1, _videoContrast);
                ShowOverlay(L("ContrastPrefix") + ((int)(_videoContrast * 100)) + "%");
                HideOverlayDelayed();
            }
            catch (Exception ex) { LogUnhandled(ex); }
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
            _gestureMode = 0;
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

        private void Page_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (_isPhotoMode) return;
            var point = e.GetCurrentPoint(null);
            int delta = point.Properties.MouseWheelDelta;
            if (delta > 0) AdjustVolume(5);
            else if (delta < 0) AdjustVolume(-5);
        }

        #endregion

        #region Auto-Hide Controls

        private const double CONTROL_BAR_OPACITY_HIDDEN = 0.0;
        private const double CONTROL_BAR_OPACITY_VISIBLE = 1.0;
        private const double CONTROL_BAR_FADE_MS = 200;

        private bool _controlsHidden = false;

        private void ShowControls()
        {
            if (_isPhotoMode) return;
            TopBar.Visibility = Visibility.Visible;
            BottomBar.Visibility = Visibility.Visible;
            _controlsHidden = false;
            FadeControlBars(true);
            ResetAutoHide();
        }

        private void HideControls()
        {
            if (!_isPlaying && _duration <= 0) return;
            if (_isSeeking) return;

            _controlsHidden = true;
            FadeControlBars(false);
        }

        private void FadeControlBars(bool show)
        {
            try
            {
                double to = show ? CONTROL_BAR_OPACITY_VISIBLE : CONTROL_BAR_OPACITY_HIDDEN;
                double from = TopBar.Opacity;

                var storyboard = new Storyboard();
                var anim = new DoubleAnimation
                {
                    From = from,
                    To = to,
                    Duration = TimeSpan.FromMilliseconds(CONTROL_BAR_FADE_MS),
                    EnableDependentAnimation = true
                };
                Storyboard.SetTarget(anim, TopBar);
                Storyboard.SetTargetProperty(anim, "Opacity");
                storyboard.Children.Add(anim);

                var anim2 = new DoubleAnimation
                {
                    From = from,
                    To = to,
                    Duration = TimeSpan.FromMilliseconds(CONTROL_BAR_FADE_MS),
                    EnableDependentAnimation = true
                };
                Storyboard.SetTarget(anim2, BottomBar);
                Storyboard.SetTargetProperty(anim2, "Opacity");
                storyboard.Children.Add(anim2);

                if (!show)
                {
                    storyboard.Completed += (s, ev) =>
                    {
                        if (_controlsHidden)
                        {
                            TopBar.Visibility = Visibility.Collapsed;
                            BottomBar.Visibility = Visibility.Collapsed;
                        }
                    };
                }

                storyboard.Begin();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HyperMedia] FadeControlBars FAILED: {0}", ex.Message);
                TopBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                BottomBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            }
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
            catch (Exception ex) { LogUnhandled(ex); }
        }

        #endregion

        #region Overlay

        private void ShowOverlay(string text)
        {
            OverlayText.Text = text;
            OverlayOpenBtn.Visibility = Visibility.Collapsed;
            OverlayNotification.Visibility = Visibility.Visible;
            _overlayNotifyTimer.Stop();
            _overlayNotifyTimer.Start();
        }

        private void ShowScreenshotOverlay(string text, string filePath)
        {
            OverlayText.Text = text;
            OverlayOpenBtn.Visibility = Visibility.Visible;
            OverlayOpenBtn.Tag = filePath;
            OverlayNotification.Visibility = Visibility.Visible;
            _overlayNotifyTimer.Stop();
            _overlayNotifyTimer.Start();
        }

        private void HideOverlay()
        {
            OverlayNotification.Visibility = Visibility.Collapsed;
            _overlayNotifyTimer.Stop();
        }

        private void OverlayNotification_Close(object sender, RoutedEventArgs e)
        {
            HideOverlay();
        }

        private async void OverlayOpen_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null || btn.Tag == null) return;

            string path = btn.Tag as string;
            if (string.IsNullOrEmpty(path)) return;

            Debug.WriteLine("[HyperMedia] OverlayOpen_Click: path={0}", path);

            try
            {
                StorageFile file = null;
                try
                {
                    file = await StorageFile.GetFileFromPathAsync(path);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[HyperMedia] OverlayOpen: GetFileFromPath failed: {0}", ex.Message);
                    return;
                }

                if (file != null)
                {
                    await Launcher.LaunchFileAsync(file);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HyperMedia] OverlayOpen FAILED: {0}", ex.Message);
            }
        }

        #endregion

        #region A/V Sync

        private void AudioSyncBtn_Click(object sender, RoutedEventArgs e)
        {
            var menu = new MenuFlyout();
            menu.Placement = FlyoutPlacementMode.Bottom;

            var audioPlus = new MenuFlyoutItem { Text = "音频延迟 +" + AV_SYNC_STEP + "ms" };
            audioPlus.Tapped += (s, ev) => { _audioDelay += AV_SYNC_STEP; ApplyAudioDelay(); ShowOverlay(L("AudioDelayPrefix") + _audioDelay + "ms"); HideOverlayDelayed(); };
            menu.Items.Add(audioPlus);

            var audioMinus = new MenuFlyoutItem { Text = "音频延迟 -" + AV_SYNC_STEP + "ms" };
            audioMinus.Tapped += (s, ev) => { _audioDelay -= AV_SYNC_STEP; ApplyAudioDelay(); ShowOverlay(L("AudioDelayPrefix") + _audioDelay + "ms"); HideOverlayDelayed(); };
            menu.Items.Add(audioMinus);

            var audioReset = new MenuFlyoutItem { Text = "重置音频延迟" };
            audioReset.Tapped += (s, ev) => { _audioDelay = 0; ApplyAudioDelay(); ShowOverlay(L("AudioDelayReset")); HideOverlayDelayed(); };
            menu.Items.Add(audioReset);

            var subPlus = new MenuFlyoutItem { Text = "字幕延迟 +" + AV_SYNC_STEP + "ms" };
            subPlus.Tapped += (s, ev) => { _subtitleDelay += AV_SYNC_STEP; ApplySubtitleDelay(); ShowOverlay(L("SubtitleDelayPrefix") + _subtitleDelay + "ms"); HideOverlayDelayed(); };
            menu.Items.Add(subPlus);

            var subMinus = new MenuFlyoutItem { Text = "字幕延迟 -" + AV_SYNC_STEP + "ms" };
            subMinus.Tapped += (s, ev) => { _subtitleDelay -= AV_SYNC_STEP; ApplySubtitleDelay(); ShowOverlay(L("SubtitleDelayPrefix") + _subtitleDelay + "ms"); HideOverlayDelayed(); };
            menu.Items.Add(subMinus);

            var subReset = new MenuFlyoutItem { Text = "重置字幕延迟" };
            subReset.Tapped += (s, ev) => { _subtitleDelay = 0; ApplySubtitleDelay(); ShowOverlay(L("SubtitleDelayReset")); HideOverlayDelayed(); };
            menu.Items.Add(subReset);

            menu.ShowAt(AudioSyncBtn);
        }

        private void ApplyAudioDelay()
        {
            try { _vlcPlayer?.setAudioDelay(_audioDelay); } catch (Exception ex) { LogUnhandled(ex); }
        }

        private void ApplySubtitleDelay()
        {
            try { _vlcPlayer?.setSpuDelay(_subtitleDelay); } catch (Exception ex) { LogUnhandled(ex); }
        }

        #endregion

        #region Audio Device Selection

        private void AudioDeviceBtn_Click(object sender, RoutedEventArgs e)
        {
            var menu = new MenuFlyout();
            menu.Placement = FlyoutPlacementMode.Bottom;

            var header = new MenuFlyoutItem { Text = "音频输出设备" };
            header.IsEnabled = false;
            menu.Items.Add(header);

            try
            {
                if (_vlcPlayer == null)
                {
                    menu.Items.Add(new MenuFlyoutItem { Text = "播放器未初始化" });
                    menu.ShowAt(AudioDeviceBtn);
                    return;
                }

                var descs = _vlcPlayer.outputDeviceEnum();
                if (descs == null || descs.Count == 0)
                {
                    menu.Items.Add(new MenuFlyoutItem { Text = "无可用设备" });
                }
                else
                {
                    foreach (var d in descs)
                    {
                        if (d == null) continue;
                        string devId = null;
                        string devName = null;
                        try { devId = d.device(); } catch (Exception ex) { LogUnhandled(ex); }
                        try { devName = d.description(); } catch (Exception ex) { LogUnhandled(ex); }
                        if (string.IsNullOrEmpty(devId) || string.IsNullOrEmpty(devName))
                            continue;

                        string capturedId = devId;
                        var item = new MenuFlyoutItem { Text = devName };
                        item.Tapped += (s, ev) =>
                        {
                            try { _vlcPlayer?.outputDeviceSet(capturedId); } catch (Exception ex) { LogUnhandled(ex); }
                            ShowOverlay(L("AudioDevicePrefix") + devName);
                            HideOverlayDelayed();
                        };
                        menu.Items.Add(item);
                    }
                    if (menu.Items.Count == 1)
                        menu.Items.Add(new MenuFlyoutItem { Text = "无可用设备" });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HyperMedia] AudioDeviceBtn_Click FAILED: {0}", ex.Message);
                menu.Items.Add(new MenuFlyoutItem { Text = "设备列表获取失败" });
            }

            menu.ShowAt(AudioDeviceBtn);
        }

        #endregion

        #region Aspect Ratio & Video Scale

        private void AspectRatioButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new MenuFlyout();
            menu.Placement = FlyoutPlacementMode.Bottom;

            var ratioMenu = new MenuFlyoutItem { Text = "画面比例" };
            ratioMenu.IsEnabled = false;
            menu.Items.Add(ratioMenu);

            string[] ratioNames = { "默认", "16:9", "4:3", "1:1", "2.35:1" };
            for (int i = 0; i < _aspectRatios.Length; i++)
            {
                int idx = i;
                var item = new MenuFlyoutItem { Text = ratioNames[i] };
                if (idx == _aspectRatioIndex) item.Text = "✓ " + item.Text;
                item.Tapped += (s, ev) =>
                {
                    _aspectRatioIndex = idx;
                    try { _vlcPlayer?.setAspectRatio(_aspectRatios[idx]); } catch (Exception ex) { LogUnhandled(ex); }
                    ShowOverlay(L("AspectRatioPrefix") + ratioNames[idx]);
                    HideOverlayDelayed();
                };
                menu.Items.Add(item);
            }

            var scaleMenu = new MenuFlyoutItem { Text = "缩放模式" };
            scaleMenu.IsEnabled = false;
            menu.Items.Add(scaleMenu);

            string[] scaleNames = { "适应", "填充", "拉伸", "裁剪", "缩放" };
            for (int i = 0; i < _videoScales.Length; i++)
            {
                int idx = i;
                var item = new MenuFlyoutItem { Text = scaleNames[i] };
                if (idx == _videoScaleIndex) item.Text = "✓ " + item.Text;
                item.Tapped += (s, ev) =>
                {
                    _videoScaleIndex = idx;
                    try { _vlcPlayer?.setScale(_videoScaleFactors[idx]); } catch (Exception ex) { LogUnhandled(ex); }
                    ShowOverlay(L("ScalePrefix") + scaleNames[idx]);
                    HideOverlayDelayed();
                };
                menu.Items.Add(item);
            }

            menu.ShowAt(AspectRatioButton);
        }

        #endregion

        #region Video Rotation

        private void RotateBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isNetworkStream)
            {
                ShowOverlay(L("NetworkNoRotate"));
                HideOverlayDelayed();
                return;
            }

            _videoRotation = (_videoRotation + 1) % 4;
            ShowOverlay(L("RotationPrefix") + _rotationNames[_videoRotation]);
            HideOverlayDelayed();

            if (_videoRotation == 0)
            {
                // Reopen without transform filter
                ReloadCurrentMedia();
            }
            else
            {
                ReloadCurrentMedia();
            }
        }

        private void ReloadCurrentMedia()
        {
            if (_vlcPlayer == null || _playlist.Count == 0 || _playlistIndex < 0) return;

            // Preserve position via resume mechanism
            SaveResumePosition();
            _isPlaying = false;

            try
            {
                _vlcPlayer.stop();
            }
            catch (Exception ex) { LogUnhandled(ex); }

            OpenFile(_playlist[_playlistIndex]);
        }

        #endregion

        #region Crop

        private void CropBtn_Click(object sender, RoutedEventArgs e)
        {
            var menu = new MenuFlyout();
            menu.Placement = FlyoutPlacementMode.Bottom;

            var header = new MenuFlyoutItem { Text = "画面裁剪" };
            header.IsEnabled = false;
            menu.Items.Add(header);

            for (int i = 0; i < _cropGeometries.Length; i++)
            {
                int idx = i;
                var item = new MenuFlyoutItem { Text = _cropNames[i] };
                if (idx == _cropIndex) item.Text = "✓ " + item.Text;
                item.Tapped += (s, ev) =>
                {
                    _cropIndex = idx;
                    _cropGeometry = _cropGeometries[idx];
                    try
                    {
                        if (_vlcPlayer != null)
                            _vlcPlayer.setCropGeometry(_cropGeometry);
                    }
                    catch (Exception ex) { LogUnhandled(ex); }
                    ShowOverlay(L("CropPrefix") + _cropNames[idx]);
                    HideOverlayDelayed();
                };
                menu.Items.Add(item);
            }

            menu.ShowAt(CropBtn);
        }

        #endregion

        #region Night Mode

        private void NightModeBtn_Click(object sender, RoutedEventArgs e)
        {
            _nightMode = !_nightMode;
            NightModeBtn.Opacity = _nightMode ? 1.0 : 0.6;
            ApplyNightMode();
        }

        private void ApplyNightMode()
        {
            try
            {
                if (_vlcPlayer == null) return;
                if (_nightMode)
                {
                    // Warm low-brightness preset
                    _vlcPlayer.setAdjustFloat(0, 0.72f);   // brightness
                    _vlcPlayer.setAdjustFloat(1, 0.88f);   // contrast
                    _vlcPlayer.setAdjustFloat(2, -10f);    // hue (warm)
                    _vlcPlayer.setAdjustFloat(3, 0.9f);    // saturation
                    _vlcPlayer.setAdjustFloat(4, 1.08f);   // gamma
                    ShowOverlay(L("NightModeOn"));
                }
                else
                {
                    _vlcPlayer.setAdjustFloat(0, _videoBrightness);
                    _vlcPlayer.setAdjustFloat(1, _videoContrast);
                    _vlcPlayer.setAdjustFloat(2, _videoHue);
                    _vlcPlayer.setAdjustFloat(3, _videoSaturation);
                    _vlcPlayer.setAdjustFloat(4, _videoGamma);
                    ShowOverlay(L("NightModeOff"));
                }
                HideOverlayDelayed();
            }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        #endregion

        #region Recording

        private void RecordBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isNetworkStream)
            {
                ShowOverlay(L("NetworkNoRecord"));
                HideOverlayDelayed();
                return;
            }
            if (_vlcPlayer == null || _playlist.Count == 0 || _playlistIndex < 0)
            {
                ShowOverlay(L("NoMedia"));
                HideOverlayDelayed();
                return;
            }

            if (!_recording)
            {
                StartRecording();
            }
            else
            {
                StopRecording();
            }
        }

        private void StartRecording()
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _recordingFileName = "Recording_" + timestamp + ".mp4";
                var tempFolder = ApplicationData.Current.TemporaryFolder;
                _recordingPath = tempFolder.Path + "\\" + _recordingFileName;
                _recording = true;

                var icon = RecordBtn.Content as TextBlock;
                if (icon != null)
                {
                    icon.Text = "\u23F9";
                    icon.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x44, 0x44));
                }

                ShowOverlay(L("RecordingStarted") + " -> " + _recordingFileName);
                HideOverlayDelayed();
                ReloadCurrentMedia();
            }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        private async void StopRecording()
        {
            try
            {
                _recording = false;
                string savedPath = _recordingPath;
                string savedName = _recordingFileName;

                var icon = RecordBtn.Content as TextBlock;
                if (icon != null)
                {
                    icon.Text = "\u23FA";
                    icon.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
                }

                // Reload media without sout to resume normal playback
                ReloadCurrentMedia();

                // Wait for file to flush, then copy to Videos library
                await Task.Delay(2000);
                try
                {
                    var tempFile = await StorageFile.GetFileFromPathAsync(savedPath);
                    var videosFolder = KnownFolders.VideosLibrary;
                    var dest = await tempFile.CopyAsync(videosFolder, savedName, NameCollisionOption.ReplaceExisting);
                    ShowOverlay(L("RecordingSaved") + ": " + savedName);
                    OverlayOpenBtn.Visibility = Visibility.Visible;
                    OverlayOpenBtn.Tag = dest.Path;
                    OverlayNotification.Visibility = Visibility.Visible;
                    _overlayNotifyTimer.Stop();
                    _overlayNotifyTimer.Start();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[HyperMedia] Recording save failed: {0}", ex.Message);
                    ShowOverlay(L("RecordingFailed") + ": " + ex.Message);
                    HideOverlayDelayed();
                }

                _recordingPath = null;
                _recordingFileName = null;
            }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        #endregion

        #region Rating & Stats

        private void RatingBtn_Click(object sender, RoutedEventArgs e)
        {
            var menu = new MenuFlyout();
            menu.Placement = FlyoutPlacementMode.Bottom;

            var header = new MenuFlyoutItem { Text = "媒体评分" };
            header.IsEnabled = false;
            menu.Items.Add(header);

            string[] stars = { "☆☆☆☆☆", "★☆☆☆☆", "★★☆☆☆", "★★★☆☆", "★★★★☆", "★★★★★" };
            int current = 0;
            if (!string.IsNullOrEmpty(_originalFileName))
                current = PlayHistory.GetRating(_originalFileName);

            for (int i = 0; i <= 5; i++)
            {
                int rating = i;
                var item = new MenuFlyoutItem { Text = stars[i] };
                if (rating == current) item.Text = "✓ " + item.Text;
                item.Tapped += (s, ev) =>
                {
                    if (string.IsNullOrEmpty(_originalFileName)) return;
                    PlayHistory.SetRating(_originalFileName, rating);
                    UpdateRatingBtnIcon();
                    ShowOverlay(rating > 0 ? L("RatingPrefix") + stars[rating] : L("RatingCleared"));
                    HideOverlayDelayed();
                };
                menu.Items.Add(item);
            }

            // Show stats
            if (!string.IsNullOrEmpty(_originalFileName))
            {
                int playCount = PlayHistory.GetPlayCount(_originalFileName);
                var statsItem = new MenuFlyoutItem { Text = "播放次数: " + playCount };
                statsItem.IsEnabled = false;
                menu.Items.Add(statsItem);
            }

            menu.ShowAt(RatingBtn);
        }

        private void UpdateRatingBtnIcon()
        {
            try
            {
                var icon = RatingBtn.Content as TextBlock;
                if (icon == null) return;
                int rating = !string.IsNullOrEmpty(_originalFileName) ? PlayHistory.GetRating(_originalFileName) : 0;
                icon.Text = rating > 0 ? "\u2605" : "\u2606";
                icon.Foreground = rating > 0
                    ? new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x40, 0xFB))
                    : new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
            }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        #endregion

        #region Mini Player

        private bool _miniPlayerMode = false;

        private void ToggleMiniPlayer()
        {
            _miniPlayerMode = !_miniPlayerMode;
            if (_miniPlayerMode)
            {
                MiniPlayerTitle.Text = _originalFileName ?? "";
                UpdateMiniPlayerIcon();
                MiniPlayerOverlay.Visibility = Visibility.Visible;
                HideControls();
                _autoHideTimer.Stop();
            }
            else
            {
                MiniPlayerOverlay.Visibility = Visibility.Collapsed;
                ShowControls();
            }
        }

        private void UpdateMiniPlayerIcon()
        {
            var icon = MiniPlayPauseIcon;
            if (icon == null) return;
            icon.Text = _isPlaying ? "\u2016" : "\u25B6";
        }

        private void MiniPlayPauseBtn_Click(object sender, RoutedEventArgs e)
        {
            TogglePlayPause();
            UpdateMiniPlayerIcon();
        }

        private void MiniPrevBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_playlist.Count > 0) PlayPrev();
        }

        private void MiniNextBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_playlist.Count > 0) PlayNext();
        }

        private void MiniExitBtn_Click(object sender, RoutedEventArgs e)
        {
            SaveResumePosition();
            StopPlayback();
            MiniPlayerOverlay.Visibility = Visibility.Collapsed;
            _miniPlayerMode = false;
            WelcomeScreen.Visibility = Visibility.Visible;
            FileNameText.Text = "";
            ShowControls();
        }

        #endregion

        #region Frame Stepping

        private void StepForward()
        {
            if (_vlcPlayer == null || !_isPlaying) return;
            try { _vlcPlayer.nextFrame(); } catch (Exception ex) { LogUnhandled(ex); }
        }

        private void StepBackward()
        {
            if (_vlcPlayer == null) return;
            try
            {
                long time = _vlcPlayer.time();
                double frameMs = 1000.0 / 30.0;
                long newTime = Math.Max(0, time - (long)frameMs);
                _vlcPlayer.setTime(newTime);
            }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        #endregion

        #region Equalizer

        private void EqualizerBtn_Click(object sender, RoutedEventArgs e)
        {
            if (EqualizerOverlay.Visibility == Visibility.Visible)
            {
                EqualizerOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            try
            {
                if (_vlcEqualizer == null)
                    _vlcEqualizer = new Equalizer();

                EqPresetCombo.Items.Clear();
                int presetCount = (int)Equalizer.presetCount();
                for (int i = 0; i < presetCount; i++)
                {
                    var item = new ComboBoxItem { Content = Equalizer.presetName((uint)i), Tag = i };
                    EqPresetCombo.Items.Add(item);
                }

                var customItem = new ComboBoxItem { Content = "自定义", Tag = -1 };
                EqPresetCombo.Items.Add(customItem);

                // Restore custom preset if saved
                var saved = LoadCustomEqPreset();
                if (saved != null)
                {
                    for (int i = 0; i < saved.Count && i < (int)Equalizer.bandCount(); i++)
                    {
                        try { _vlcEqualizer.setAmp(saved[i], (uint)i); } catch (Exception ex) { LogUnhandled(ex); }
                    }
                    EqPresetCombo.SelectedItem = customItem;
                }

                EqBandsPanel.Children.Clear();
                int bandCount = (int)Equalizer.bandCount();
                for (int i = 0; i < bandCount; i++)
                {
                    string bandName = (Equalizer.bandFrequency((uint)i) + " Hz").ToString();
                    var slider = new Slider
                    {
                        Minimum = -20,
                        Maximum = 20,
                        Value = _vlcEqualizer.amp((uint)i),
                        StepFrequency = 0.5,
                        Width = 280,
                        Tag = i,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    slider.ValueChanged += EqBand_ValueChanged;

                    var header = new TextBlock
                    {
                        Text = bandName,
                        Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                        FontSize = 11,
                        Margin = new Thickness(0, 4, 0, 2)
                    };

                    EqBandsPanel.Children.Add(header);
                    EqBandsPanel.Children.Add(slider);
                }

                EqualizerOverlay.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                ShowOverlay(L("EqualizerErrorPrefix") + ex.Message);
                HideOverlayDelayed();
            }
        }

        private void EqBand_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            var slider = sender as Slider;
            if (slider == null || slider.Tag == null) return;
            int band = (int)slider.Tag;
            try { _vlcEqualizer?.setAmp((float)e.NewValue, (uint)band); } catch (Exception ex) { LogUnhandled(ex); }
        }

        private void EqPresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EqPresetCombo.SelectedItem == null) return;
            var item = EqPresetCombo.SelectedItem as ComboBoxItem;
            if (item == null) return;
            int presetIndex = (int)item.Tag;
            try
            {
                if (presetIndex >= 0)
                {
                    _vlcEqualizer = new Equalizer((uint)presetIndex);
                    _vlcPlayer?.setEqualizer(_vlcEqualizer);
                }
                else
                {
                    _vlcEqualizer = new Equalizer();
                    var saved = LoadCustomEqPreset();
                    if (saved != null)
                    {
                        for (int i = 0; i < saved.Count && i < (int)Equalizer.bandCount(); i++)
                            _vlcEqualizer.setAmp(saved[i], (uint)i);
                    }
                    _vlcPlayer?.setEqualizer(_vlcEqualizer);
                }

                for (int i = 0; i < EqBandsPanel.Children.Count; i++)
                {
                    var slider = EqBandsPanel.Children[i] as Slider;
                    if (slider != null && slider.Tag != null)
                    {
                        int band = (int)slider.Tag;
                        slider.Value = _vlcEqualizer.amp((uint)band);
                    }
                }
            }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        private const string KEY_EQ_CUSTOM = "EqCustomPreset";

        private List<float> LoadCustomEqPreset()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (!settings.Values.ContainsKey(KEY_EQ_CUSTOM)) return null;
                string serialized = settings.Values[KEY_EQ_CUSTOM] as string;
                if (string.IsNullOrEmpty(serialized)) return null;
                var list = new List<float>();
                foreach (var part in serialized.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    float v;
                    if (float.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v))
                        list.Add(v);
                }
                return list;
            }
            catch (Exception ex) { LogUnhandled(ex); }
            return null;
        }

        private void EqSaveCustom_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_vlcEqualizer == null) return;
                var settings = ApplicationData.Current.LocalSettings;
                var list = new List<string>();
                for (int i = 0; i < (int)Equalizer.bandCount(); i++)
                {
                    list.Add(_vlcEqualizer.amp((uint)i).ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                settings.Values[KEY_EQ_CUSTOM] = string.Join("|", list);

                if (EqPresetCombo != null)
                {
                    bool hasCustom = false;
                    foreach (var obj in EqPresetCombo.Items)
                    {
                        var it = obj as ComboBoxItem;
                        if (it != null && it.Tag is int && (int)it.Tag == -1) { hasCustom = true; break; }
                    }
                    if (!hasCustom)
                        EqPresetCombo.Items.Add(new ComboBoxItem { Content = "自定义", Tag = -1 });
                }

                ShowOverlay(L("EqCustomSaved"));
                HideOverlayDelayed();
            }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        private void EqualizerOverlay_Close(object sender, RoutedEventArgs e)
        {
            EqualizerOverlay.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region Video Filters

        private bool _filterInitializing = false;

        private void VideoFilterBtn_Click(object sender, RoutedEventArgs e)
        {
            if (VideoFilterOverlay.Visibility == Visibility.Visible)
            {
                VideoFilterOverlay.Visibility = Visibility.Collapsed;
                return;
            }
            _filterInitializing = true;
            BrightnessSlider.Value = _videoBrightness;
            ContrastSlider.Value = _videoContrast;
            HueSlider.Value = _videoHue;
            SaturationSlider.Value = _videoSaturation;
            GammaSlider.Value = _videoGamma;
            _filterInitializing = false;
            VideoFilterOverlay.Visibility = Visibility.Visible;
        }

        private void BrightnessSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_filterInitializing) return;
            _videoBrightness = (float)e.NewValue;
            try { if (_vlcPlayer != null) _vlcPlayer.setAdjustFloat(0, _videoBrightness); }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        private void ContrastSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_filterInitializing) return;
            _videoContrast = (float)e.NewValue;
            try { if (_vlcPlayer != null) _vlcPlayer.setAdjustFloat(1, _videoContrast); }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        private void HueSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_filterInitializing) return;
            _videoHue = (float)e.NewValue;
            try { if (_vlcPlayer != null) _vlcPlayer.setAdjustFloat(2, _videoHue); }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        private void SaturationSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_filterInitializing) return;
            _videoSaturation = (float)e.NewValue;
            try { if (_vlcPlayer != null) _vlcPlayer.setAdjustFloat(3, _videoSaturation); }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        private void GammaSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_filterInitializing) return;
            _videoGamma = (float)e.NewValue;
            try { if (_vlcPlayer != null) _vlcPlayer.setAdjustFloat(4, _videoGamma); }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        private void FilterResetBtn_Click(object sender, RoutedEventArgs e)
        {
            _videoBrightness = 1.0f;
            _videoContrast = 1.0f;
            _videoHue = 0f;
            _videoSaturation = 1.0f;
            _videoGamma = 1.0f;
            try
            {
                if (_vlcPlayer != null)
                {
                    _vlcPlayer.setAdjustFloat(0, _videoBrightness);
                    _vlcPlayer.setAdjustFloat(1, _videoContrast);
                    _vlcPlayer.setAdjustFloat(2, _videoHue);
                    _vlcPlayer.setAdjustFloat(3, _videoSaturation);
                    _vlcPlayer.setAdjustFloat(4, _videoGamma);
                }
            }
            catch (Exception ex) { LogUnhandled(ex); }

            _filterInitializing = true;
            BrightnessSlider.Value = _videoBrightness;
            ContrastSlider.Value = _videoContrast;
            HueSlider.Value = _videoHue;
            SaturationSlider.Value = _videoSaturation;
            GammaSlider.Value = _videoGamma;
            _filterInitializing = false;
            ShowOverlay(L("FilterReset"));
            HideOverlayDelayed();
        }

        private void VideoFilterOverlay_Close(object sender, RoutedEventArgs e)
        {
            VideoFilterOverlay.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region Chapter Navigation

        private void ChapterBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_vlcPlayer == null) return;

            var menu = new MenuFlyout();
            menu.Placement = FlyoutPlacementMode.Bottom;

            try
            {
                int chapterCount = _vlcPlayer.chapterCount();
                if (chapterCount > 0)
                {
                    for (int i = 0; i < chapterCount; i++)
                    {
                        int idx = i;
                        var descs = _vlcPlayer.chapterDescription(0);
                        string name = "章节 " + (i + 1);
                        if (descs != null && idx < descs.Count)
                            name = descs[idx].name() ?? name;

                        var item = new MenuFlyoutItem { Text = name };
                        item.Tapped += (s, ev) =>
                        {
                            try { _vlcPlayer.setChapter(idx); } catch (Exception ex) { LogUnhandled(ex); }
                            ShowOverlay(L("JumpPrefix") + name);
                            HideOverlayDelayed();
                        };
                        menu.Items.Add(item);
                    }
                }
                else
                {
                    menu.Items.Add(new MenuFlyoutItem { Text = "无章节信息" });
                }
            }
            catch
            {
                menu.Items.Add(new MenuFlyoutItem { Text = "无章节信息" });
            }

            menu.ShowAt(ChapterBtn);
        }

        #endregion

        #region Stats OSD

        private bool _statsVisible = false;

        private void StatsBtn_Click(object sender, RoutedEventArgs e)
        {
            _statsVisible = !_statsVisible;
            StatsOverlay.Visibility = _statsVisible ? Visibility.Visible : Visibility.Collapsed;
            if (_statsVisible)
                UpdateStats();
        }

        private void UpdateStats()
        {
            if (!_statsVisible || _vlcPlayer == null) return;
            try
            {
                long time = _vlcPlayer.time();
                long len = _vlcPlayer.length();
                if (_isMusicMode)
                {
                    StatsText.Text = string.Format(
                        "时间: {0} / {1}\n音量: {2}%\n速度: {3}x\n音频延迟: {4}ms",
                        FormatTime(time / 1000.0),
                        FormatTime(len / 1000.0),
                        (int)VolumeSlider.Value,
                        _playbackSpeed,
                        _audioDelay);
                }
                else
                {
                    StatsText.Text = string.Format(
                        "时间: {0} / {1}\n音量: {2}%\n速度: {3}x\n音频延迟: {4}ms\n字幕延迟: {5}ms\n章节: {6}/{7}",
                        FormatTime(time / 1000.0),
                        FormatTime(len / 1000.0),
                        (int)VolumeSlider.Value,
                        _playbackSpeed,
                        _audioDelay,
                        _subtitleDelay,
                        _vlcPlayer.chapter() + 1,
                        _vlcPlayer.chapterCount());
                }
            }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        #endregion

        #region Keyboard Shortcuts Help

        private void ShortcutsOverlay_Close(object sender, RoutedEventArgs e)
        {
            ShortcutsOverlay.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region Open Containing Folder

        private async void OpenFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_playlist.Count == 0 || _playlistIndex < 0 || _playlistIndex >= _playlist.Count) return;
            try
            {
                var file = _playlist[_playlistIndex];
                var folder = await file.GetParentAsync();
                if (folder != null)
                    await Launcher.LaunchUriAsync(new Uri(folder.Path));
            }
            catch (Exception ex) { LogUnhandled(ex); }
        }

        #endregion

        #region Utility

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

        private static void LogUnhandled(Exception ex)
        {
            try { Debug.WriteLine("[HyperMedia] Caught: {0}", ex != null ? ex.Message : "null"); }
            catch { }
        }

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
