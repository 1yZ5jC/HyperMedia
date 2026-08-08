using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
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
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;

namespace HyperMedia
{
    public sealed partial class HomePage : Page
    {
        private const string VIDEO_FILTER =
            ".mp4,.avi,.mkv,.webm,.flv,.mov,.wmv,.3gp,.ts,.mka,.mpg,.mpeg,.vob,.ogv,.rm,.rmvb,.divx,.asf,.m4v";
        private const string MUSIC_FILTER =
            ".mp3,.flac,.wav,.aac,.ogg,.wma,.m4a,.opus,.ape,.alac,.aiff";
        private const string PHOTO_FILTER =
            ".jpg,.jpeg,.png,.bmp,.gif,.tiff,.tif,.webp";

        private const string KEY_TILE_VIDEOS = "TileView_Videos";
        private const string KEY_TILE_MUSIC = "TileView_Music";
        private const string KEY_TILE_PHOTOS = "TileView_Photos";

        private bool _videosTileView;
        private bool _musicTileView;
        private bool _photosTileView;

        private const string ICON_LIST = "\u2630";
        private const string ICON_TILE = "\u25A6";

        public HomePage()
        {
            this.InitializeComponent();
            Window.Current.CoreWindow.PointerEntered += CoreWindow_PointerEntered;
            // Live light-theme: unsubscribe before subscribing (page is rebuilt on
            // every return navigation, so this keeps handlers from accumulating).
            App.LightThemeChanged -= Home_LightThemeChanged;
            App.LightThemeChanged += Home_LightThemeChanged;
            this.Loaded += (s, e) =>
            {
                ApplyTheme();
                ApplyHomeLanguage();
            };
        }

        private void Home_LightThemeChanged(object sender, EventArgs e)
        {
            try { ApplyTheme(); }
            catch { }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            CleanupLegacyResumeMarkers();

            var settings = ApplicationData.Current.LocalSettings;
            if (settings.Values.ContainsKey(KEY_TILE_VIDEOS))
                _videosTileView = (bool)settings.Values[KEY_TILE_VIDEOS];
            if (settings.Values.ContainsKey(KEY_TILE_MUSIC))
                _musicTileView = (bool)settings.Values[KEY_TILE_MUSIC];
            if (settings.Values.ContainsKey(KEY_TILE_PHOTOS))
                _photosTileView = (bool)settings.Values[KEY_TILE_PHOTOS];

            ApplyToggleState();
            LoadRecentItems();
            ApplyTheme();
            ApplyHomeLanguage();
            SubscribeLanguage();
        }

        private void SubscribeLanguage()
        {
            try
            {
                var appText = Application.Current.Resources["AppText"] as AppText;
                if (appText != null)
                {
                    appText.LanguageChanged -= AppText_LanguageChanged;
                    appText.LanguageChanged += AppText_LanguageChanged;
                }
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] SubscribeLanguage failed: {0}", ex.Message); }
        }

        private void AppText_LanguageChanged(object sender, EventArgs e)
        {
            try
            {
                ApplyTheme();
                ApplyHomeLanguage();
                ApplyToggleState();
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] AppText_LanguageChanged failed: {0}", ex.Message); }
        }

        private void ApplyHomeLanguage()
        {
            try
            {
                var appText = Application.Current.Resources["AppText"] as AppText;
                if (appText != null)
                    appText.ApplyLanguageTo(this);
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] ApplyHomeLanguage failed: {0}", ex.Message); }
        }

        private void ApplyTheme()
        {
            try
            {
                bool light = SettingsPage.GetLightTheme();
                var bgBrush = new Windows.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(0xFF, 0xEE, 0xEE, 0xF5));
                var darkBgBrush = new Windows.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(0xFF, 0x0A, 0x0A, 0x0F));
                var fgBrush = new Windows.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(0xFF, 0x20, 0x20, 0x28));
                var fgSoft = new Windows.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(0xAA, 0x20, 0x20, 0x28));
                var whiteBrush = new Windows.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));

                if (HomeRootGrid != null)
                    HomeRootGrid.Background = light ? bgBrush : darkBgBrush;
                if (HomeBottomBar != null)
                    HomeBottomBar.Background = light ? bgBrush : darkBgBrush;
                if (HomeBrandText != null)
                    HomeBrandText.Foreground = light ? fgSoft : whiteBrush;
                if (HomeBottomBrand != null)
                    HomeBottomBrand.Foreground = light ? fgSoft : whiteBrush;
                if (HomeTitle1 != null)
                    HomeTitle1.Foreground = light ? fgBrush : whiteBrush;
                if (HomeTitle2 != null)
                    HomeTitle2.Foreground = light ? new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x40, 0xFB)) : new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x40, 0xFB));
                if (HomeDesc != null)
                    HomeDesc.Foreground = light ? fgSoft : whiteBrush;
                if (VideosTitle != null)
                    VideosTitle.Foreground = light ? fgBrush : whiteBrush;
                if (MusicTitle != null)
                    MusicTitle.Foreground = light ? fgBrush : whiteBrush;
                if (PhotosTitle != null)
                    PhotosTitle.Foreground = light ? fgBrush : whiteBrush;

                ApplyPageTextColor(this, light);
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] ApplyTheme failed: {0}", ex.Message); }
        }

        private void ApplyPageTextColor(DependencyObject root, bool light)
        {
            try
            {
                var darkFg = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xE6, 0x20, 0x20, 0x28));
                var whiteFg = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));

                int count = VisualTreeHelper.GetChildrenCount(root);
                for (int i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(root, i);
                    var tb = child as TextBlock;
                    if (tb != null && !tb.Name.StartsWith("Keep", StringComparison.Ordinal))
                    {
                        if (tb.Foreground is Windows.UI.Xaml.Media.SolidColorBrush)
                        {
                            var brush = tb.Foreground as Windows.UI.Xaml.Media.SolidColorBrush;
                            byte r = brush.Color.R, g = brush.Color.G, b = brush.Color.B;
                            bool isColoredAccent = (r > 0x80) && (g < 0x80) && (b > 0x80); // pink/purple
                            bool isCyanAccent = (g > 0x80) && (r < 0x80) && (b > 0x80);
                            bool isGreenAccent = (g > 0x80) && (r < 0x80) && (b < 0x80);
                            if (!isColoredAccent && !isCyanAccent && !isGreenAccent)
                                tb.Foreground = light ? darkFg : whiteFg;
                        }
                    }

                    // In light mode, flip white-ish translucent card backgrounds to dark for contrast
                    if (light)
                    {
                        var bd = child as Border;
                        if (bd != null && bd.Background is Windows.UI.Xaml.Media.SolidColorBrush)
                        {
                            var bbr = bd.Background as Windows.UI.Xaml.Media.SolidColorBrush;
                            if (bbr.Color.A < 0xFF && bbr.Color.R > 0xE0 && bbr.Color.G > 0xE0 && bbr.Color.B > 0xE0)
                                bd.Background = new Windows.UI.Xaml.Media.SolidColorBrush(
                                    Windows.UI.Color.FromArgb(bbr.Color.A, 0x00, 0x00, 0x00));
                        }
                    }
                    ApplyPageTextColor(child, light);
                }
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] ApplyPageTextColor failed: {0}", ex.Message); }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            Window.Current.CoreWindow.PointerEntered -= CoreWindow_PointerEntered;
        }

        private string _activeSearchQuery;

        private void ApplySearchFilter(string query)
        {
            _activeSearchQuery = query;
            LoadRecentForCategoryFiltered("Videos", RecentVideosList, RecentVideosTiles);
            LoadRecentForCategoryFiltered("Music", RecentMusicList, RecentMusicTiles);
            LoadRecentForCategoryFiltered("Photos", RecentPhotosList, RecentPhotosTiles);
            StatusText.Text = "搜索: \"" + query + "\"  — 点按任意项目播放 (Esc 清除)";
        }

        private void LoadRecentForCategoryFiltered(string category, ItemsControl listControl, ItemsControl tileControl)
        {
            var tuples = PlayHistory.GetRecent(category);
            var items = new ObservableCollection<RecentItem>();
            foreach (var t in tuples)
            {
                if (!string.IsNullOrEmpty(_activeSearchQuery) &&
                    t.Item2.IndexOf(_activeSearchQuery, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var item = new RecentItem { FilePath = t.Item1, FileName = t.Item2, Category = category };
                item.ResumeText = PlayHistory.GetResumeText(t.Item2);
                item.ResumePercent = PlayHistory.GetResumePercent(t.Item2);
                item.LoadThumbnail();
                int rating = PlayHistory.GetRating(t.Item2);
                if (rating > 0)
                    item.RatingText = new string('\u2605', rating);
                int playCount = PlayHistory.GetPlayCount(t.Item2);
                if (playCount > 1)
                    item.PlayCountText = L("PlayCountLabel") + playCount + L("PlayedTimesSuffix");
                items.Add(item);
            }
            var source = items.Count > 0 ? items : null;
            listControl.ItemsSource = source;
            tileControl.ItemsSource = source;
        }

        private void ClearSearchFilter()
        {
            _activeSearchQuery = null;
            LoadRecentItems();
            StatusText.Text = L("TaglineSupport");
        }

        private void CoreWindow_PointerEntered(CoreWindow sender, PointerEventArgs args)
        {
            Focus(FocusState.Programmatic);
        }

        #region Panorama Snap & Wheel

        private double SnapPitch
        {
            get
            {
                double w = PanoramaScroll?.ActualWidth ?? 0;
                return w > 0 ? w + 24 : 0;
            }
        }

        private bool _snapPending = false;

        // Semantic zoom: shrinking below this factor opens the overview; the
        // overview returns to details once zoomed back to near 1.0.
        private const double ZOOM_IN_OVERVIEW_THRESHOLD = 0.85;
        private const double ZOOM_OUT_DETAILS_THRESHOLD = 0.95;

        private void PanoramaScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            // Shrinking the panorama below threshold opens the overview (semantic zoom out)
            if (!_overviewVisible && PanoramaScroll.ZoomFactor < ZOOM_IN_OVERVIEW_THRESHOLD)
            {
                ShowOverview();
                return;
            }

            if (e.IsIntermediate)
            {
                _snapPending = true;
                return;
            }

            // Only snap sections while fully zoomed in
            if (_snapPending && PanoramaScroll.ZoomFactor >= 0.999)
            {
                _snapPending = false;
                SnapToNearest();
            }
            else
            {
                _snapPending = false;
            }
        }

        private void OverviewScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (e.IsIntermediate) return;

            // Zooming the overview back out to ~1.0 returns to the panorama (semantic zoom in)
            if (_overviewVisible && OverviewScroll.ZoomFactor >= ZOOM_OUT_DETAILS_THRESHOLD)
                HideOverview();
        }

        private void SnapToNearest()
        {
            if (PanoramaScroll == null) return;
            double current = PanoramaScroll.HorizontalOffset;
            double pitch = SnapPitch;
            if (pitch <= 0) return;
            double best = 0;
            double bestDist = double.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                double off = pitch * i;
                double dist = Math.Abs(current - off);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = off;
                }
            }
            if (bestDist > 1)
                PanoramaScroll.ChangeView(best, null, null, true);
        }

        private void ScrollToSection(int idx)
        {
            if (PanoramaScroll == null) return;
            FrameworkElement target = null;
            if (idx == 1) target = VideosSection;
            else if (idx == 2) target = MusicSection;
            else if (idx == 3) target = PhotosSection;
            if (target == null) return;

            var transform = target.TransformToVisual(PanoramaScroll);
            if (transform == null) return;
            var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            double targetX = Math.Max(0, point.X + PanoramaScroll.HorizontalOffset);
            targetX = Math.Min(targetX, PanoramaScroll.ScrollableWidth);
            PanoramaScroll.ChangeView(targetX, null, null, true);
        }

        private void PanoramaScroll_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            // Ctrl+wheel = zoom (semantic zoom); let the ScrollViewer handle it
            bool ctrl = (Window.Current.CoreWindow.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0;
            if (ctrl)
            {
                e.Handled = false;
                return;
            }

            int delta = e.GetCurrentPoint(PanoramaScroll).Properties.MouseWheelDelta;
            double newOffset = PanoramaScroll.HorizontalOffset + (delta > 0 ? -180 : 180);
            newOffset = Math.Max(0, Math.Min(newOffset, PanoramaScroll.ScrollableWidth));
            PanoramaScroll.ChangeView(newOffset, null, null, true);
            e.Handled = true;
        }

        #endregion

        #region Recent Items

        private const string KEY_RESUME_CLEANUP_DONE = "ResumeCleanupV1Done";

        private void CleanupLegacyResumeMarkers()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.ContainsKey(KEY_RESUME_CLEANUP_DONE))
                    return;

                // One-time migration: previous versions never cleared resume markers on completion,
                // so every saved position is stale. Clear them all once.
                var keys = new List<string>();
                foreach (var key in settings.Values.Keys)
                {
                    string k = key != null ? key.ToString() : "";
                    if (k.StartsWith("ResumePosition_") || k.StartsWith("ResumePercent_"))
                        keys.Add(k);
                }
                foreach (var key in keys)
                    settings.Values.Remove(key);

                settings.Values[KEY_RESUME_CLEANUP_DONE] = true;
                Debug.WriteLine("[HyperMedia] One-time resume marker cleanup: removed {0} entries", keys.Count);
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] CleanupLegacyResumeMarkers failed: {0}", ex.Message); }
        }

        private void LoadRecentItems()
        {
            LoadRecentForCategory("Videos", RecentVideosList, RecentVideosTiles);
            LoadRecentForCategory("Music", RecentMusicList, RecentMusicTiles);
            LoadRecentForCategory("Photos", RecentPhotosList, RecentPhotosTiles);
        }

        private void LoadRecentForCategory(string category, ItemsControl listControl, ItemsControl tileControl)
        {
            var tuples = PlayHistory.GetRecent(category);
            var items = new ObservableCollection<RecentItem>();
            foreach (var t in tuples)
            {
                var item = new RecentItem { FilePath = t.Item1, FileName = t.Item2, Category = category };
                item.ResumeText = PlayHistory.GetResumeText(t.Item2);
                item.ResumePercent = PlayHistory.GetResumePercent(t.Item2);
                item.LoadThumbnail();
                int rating = PlayHistory.GetRating(t.Item2);
                if (rating > 0)
                    item.RatingText = new string('\u2605', rating);
                int playCount = PlayHistory.GetPlayCount(t.Item2);
                if (playCount > 1)
                    item.PlayCountText = L("PlayCountLabel") + playCount + L("PlayedTimesSuffix");
                items.Add(item);
            }
            var source = items.Count > 0 ? items : null;
            listControl.ItemsSource = source;
            tileControl.ItemsSource = source;
        }

        private void ApplyToggleState()
        {
            VideosToggleIcon.Text = _videosTileView ? ICON_TILE : ICON_LIST;
            RecentVideosList.Visibility = _videosTileView ? Visibility.Collapsed : Visibility.Visible;
            RecentVideosTiles.Visibility = _videosTileView ? Visibility.Visible : Visibility.Collapsed;

            MusicToggleIcon.Text = _musicTileView ? ICON_TILE : ICON_LIST;
            RecentMusicList.Visibility = _musicTileView ? Visibility.Collapsed : Visibility.Visible;
            RecentMusicTiles.Visibility = _musicTileView ? Visibility.Visible : Visibility.Collapsed;

            PhotosToggleIcon.Text = _photosTileView ? ICON_TILE : ICON_LIST;
            RecentPhotosList.Visibility = _photosTileView ? Visibility.Collapsed : Visibility.Visible;
            RecentPhotosTiles.Visibility = _photosTileView ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SaveToggle(string key, bool value)
        {
            ApplicationData.Current.LocalSettings.Values[key] = value;
        }

        private void VideosToggle_Click(object sender, RoutedEventArgs e)
        {
            _videosTileView = !_videosTileView;
            SaveToggle(KEY_TILE_VIDEOS, _videosTileView);
            ApplyToggleState();
        }

        private void MusicToggle_Click(object sender, RoutedEventArgs e)
        {
            _musicTileView = !_musicTileView;
            SaveToggle(KEY_TILE_MUSIC, _musicTileView);
            ApplyToggleState();
        }

        private void PhotosToggle_Click(object sender, RoutedEventArgs e)
        {
            _photosTileView = !_photosTileView;
            SaveToggle(KEY_TILE_PHOTOS, _photosTileView);
            ApplyToggleState();
        }

        private async void RecentItem_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var border = sender as Border;
            if (border == null) return;

            string filePath = border.Tag as string;
            if (string.IsNullOrEmpty(filePath)) return;

            try
            {
                var file = await StorageFile.GetFileFromPathAsync(filePath);
                if (file != null)
                {
                    StorageApplicationPermissions.FutureAccessList.AddOrReplace("PlaybackFile", file);
                    Frame.Navigate(typeof(MainPage));
                }
            }
            catch
            {
                // File may have been moved or deleted - clear invalid history entry
                try
                {
                    string category = PlayHistory.GetCategory(filePath);
                    if (category != null)
                    {
                        var settings = ApplicationData.Current.LocalSettings;
                        string key = "RecentPlay_" + category;
                        if (settings.Values.ContainsKey(key))
                        {
                            string serialized = settings.Values[key] as string;
                            if (!string.IsNullOrEmpty(serialized))
                            {
                                var list = new System.Collections.Generic.List<string>(
                                    serialized.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
                                list.RemoveAll(x => x.StartsWith(filePath + "::", StringComparison.OrdinalIgnoreCase));
                                settings.Values[key] = string.Join("|", list);
                                LoadRecentItems();
                            }
                        }
                    }
                }
                catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
            }
        }

        private async void RecentItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var border = sender as Border;
            if (border == null) return;
            string filePath = border.Tag as string;
            if (string.IsNullOrEmpty(filePath)) return;

            try
            {
                var menu = new Windows.UI.Popups.PopupMenu();
                menu.Commands.Add(new Windows.UI.Popups.UICommand(L("DeleteFromHistory"), (cmd) =>
                {
                    try
                    {
                        string category = PlayHistory.GetCategory(filePath);
                        if (category != null)
                        {
                            var settings = ApplicationData.Current.LocalSettings;
                            string key = "RecentPlay_" + category;
                            if (settings.Values.ContainsKey(key))
                            {
                                string serialized = settings.Values[key] as string;
                                if (!string.IsNullOrEmpty(serialized))
                                {
                                    var list = new List<string>(
                                        serialized.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
                                    list.RemoveAll(x => x.StartsWith(filePath + "::", StringComparison.OrdinalIgnoreCase));
                                    settings.Values[key] = string.Join("|", list);
                                    LoadRecentItems();
                                }
                            }
                        }
                    }
                    catch (Exception ex) { Debug.WriteLine("[HyperMedia] History delete failed: {0}", ex.Message); }
                }));
                await menu.ShowForSelectionAsync(
                    new Rect(e.GetPosition(null), new Size(1, 1)),
                    Windows.UI.Popups.Placement.Above);
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Context menu failed: {0}", ex.Message); }
        }

        #endregion

        #region Navigation

        private void VideoTile_Tapped(object sender, TappedRoutedEventArgs e)
        {
            OpenFilesWithFilter(VIDEO_FILTER, PickerLocationId.VideosLibrary);
        }

        private void MusicTile_Tapped(object sender, TappedRoutedEventArgs e)
        {
            OpenFilesWithFilter(MUSIC_FILTER, PickerLocationId.MusicLibrary);
        }

        private void PhotosTile_Tapped(object sender, TappedRoutedEventArgs e)
        {
            OpenFilesWithFilter(PHOTO_FILTER, PickerLocationId.PicturesLibrary);
        }

        private void RecentTile_Tapped(object sender, TappedRoutedEventArgs e)
        {
            OpenButton_Click(null, null);
        }

        private void Card_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            var fe = sender as FrameworkElement;
            if (fe != null) fe.Opacity = 0.85;
        }

        private void Card_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            var fe = sender as FrameworkElement;
            if (fe != null) fe.Opacity = 1.0;
        }

        #endregion

        #region Clear History

        private async void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Windows.UI.Popups.MessageDialog(L("ClearHistoryConfirm"), L("ClearHistoryTitle"));
            dialog.Commands.Add(new Windows.UI.Popups.UICommand("清除", (cmd) =>
            {
                PlayHistory.ClearAll();
                LoadRecentItems();
            }));
            dialog.Commands.Add(new Windows.UI.Popups.UICommand(L("Cancel")));
            dialog.DefaultCommandIndex = 1;
            dialog.CancelCommandIndex = 1;
            await dialog.ShowAsync();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(SettingsPage));
        }

        #endregion

        #region Media Library


        private void LibraryButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(MediaLibraryPage));
        }

        #endregion

        #region Open URL

        private void OpenUrlButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(OpenUrlPage));
        }


        #endregion

        #region File Open

private bool _pickerOpen;

        private void OpenFilesWithFilter(string filterExtensions, PickerLocationId location)
        {
            // Win8/8.1 bug: re-showing a picker right after the user cancels/closes
            // one can crash. Guard against double-invoke and defer the next launch
            // so two picker sessions never overlap (same fix as the desktop build).
            if (_pickerOpen) return;
            _pickerOpen = true;
            try
            {
                var picker = new FileOpenPicker();
                picker.SuggestedStartLocation = location;
                foreach (var ext in filterExtensions.Split(','))
                    picker.FileTypeFilter.Add(ext.Trim());

                // WP 8.1 has no synchronous picker API (throws 0x80070032);
                // result arrives async through App.OnActivated.
                App.PendingFileOpen = (files) =>
                {
                    _pickerOpen = false;
                    if (files != null && files.Count > 0)
                    {
                        StorageApplicationPermissions.FutureAccessList.AddOrReplace("PlaybackFile", files[0]);
                        if (files.Count > 1)
                        {
                            var extras = new List<string>();
                            for (int i = 1; i < files.Count; i++)
                                extras.Add(files[i].Path);
                            ApplicationData.Current.LocalSettings.Values["PlaylistExtras"] = string.Join("|", extras);
                        }
                        Frame.Navigate(typeof(MainPage));
                    }
                };
                picker.ContinuationData["picker"] = "media";
                picker.PickMultipleFilesAndContinue();
            }
            catch
            {
                _pickerOpen = false;
                App.PendingFileOpen = null;
            }
        }

        private void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            if (_pickerOpen) return;
            _pickerOpen = true;
            try
            {
                var picker = new FileOpenPicker();
                picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
                string[] extensions = {
                    ".mp4", ".avi", ".mkv", ".webm", ".flv", ".mov", ".wmv",
                    ".mp3", ".flac", ".wav", ".aac", ".ogg", ".wma", ".m4a",
                    ".3gp", ".ts", ".mka", ".opus",
                    ".jpg", ".jpeg", ".png", ".bmp", ".gif"
                };
                foreach (var ext in extensions)
                    picker.FileTypeFilter.Add(ext);

                // WP 8.1: no synchronous picker API — continuation via App.OnActivated.
                App.PendingFileOpen = (files) =>
                {
                    _pickerOpen = false;
                    if (files != null && files.Count > 0)
                    {
                        StorageApplicationPermissions.FutureAccessList.AddOrReplace("PlaybackFile", files[0]);
                        if (files.Count > 1)
                        {
                            var extras = new List<string>();
                            for (int i = 1; i < files.Count; i++)
                                extras.Add(files[i].Path);
                            ApplicationData.Current.LocalSettings.Values["PlaylistExtras"] = string.Join("|", extras);
                        }
                        Frame.Navigate(typeof(MainPage));
                    }
                };
                picker.ContinuationData["picker"] = "all";
                picker.PickMultipleFilesAndContinue();
            }
            catch
            {
                _pickerOpen = false;
                App.PendingFileOpen = null;
            }
        }

        #endregion

        #region Keyboard

        private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            bool ctrl = (Window.Current.CoreWindow.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0;
            if (ctrl && e.Key == VirtualKey.O)
            {
                OpenButton_Click(null, null);
                e.Handled = true;
                return;
            }
            if (ctrl && e.Key == VirtualKey.U)
            {
                OpenUrlButton_Click(null, null);
                e.Handled = true;
                return;
            }
            if (ctrl && e.Key == VirtualKey.M)
            {
                ToggleOverview();
                e.Handled = true;
                return;
            }

            if (_overviewVisible && e.Key == VirtualKey.Escape)
            {
                ToggleOverview();
                e.Handled = true;
                return;
            }

            if (e.Key == VirtualKey.Left || e.Key == VirtualKey.Right)
            {
                double step = PanoramaScroll.ActualWidth + 24;
                double newOffset = PanoramaScroll.HorizontalOffset + (e.Key == VirtualKey.Right ? step : -step);
                newOffset = Math.Max(0, Math.Min(newOffset, PanoramaScroll.ScrollableWidth));
                PanoramaScroll.ChangeView(newOffset, null, null, true);
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Escape && _activeSearchQuery != null)
            {
                ClearSearchFilter();
                e.Handled = true;
            }
        }

        #endregion

        #region Playlists

        private void PlaylistsButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(PlaylistsPage));
        }

        #endregion

        private void ShowOverlay(string text)
        {
            StatusText.Text = text;
        }

        private string L(string key)
        {
            try
            {
                var appText = Application.Current.Resources["AppText"] as AppText;
                if (appText != null) return appText.T(key);
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] L failed: {0}", ex.Message); }
            return key;
        }

        #region Semantic Zoom Overview

        private class OverviewItem
        {
            public string Glyph { get; set; }
            public string Title { get; set; }
            public string Subtitle { get; set; }
            public Windows.UI.Xaml.Media.SolidColorBrush TileBrush { get; set; }
            public string Action { get; set; }
        }

        private bool _overviewVisible = false;

        private void OverviewButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleOverview();
        }

        private void ToggleOverview()
        {
            if (_overviewVisible) HideOverview();
            else ShowOverview();
        }

        private void ShowOverview()
        {
            if (_overviewVisible) return;
            _overviewVisible = true;

            BuildOverviewItems();

            // Start the overview from the zoom level that opened it, so there is
            // room to zoom back out (to 1.0) and return to the details view.
            double startZoom = Math.Max(0.5, Math.Min(PanoramaScroll.ZoomFactor, 0.85));
            PanoramaScroll.ChangeView(null, null, 1.0f, true);
            OverviewScroll.ChangeView(null, null, (float)startZoom, true);

            OverviewScroll.Opacity = 0;
            OverviewScroll.Visibility = Visibility.Visible;
            PanoramaScroll.Visibility = Visibility.Collapsed;
            if (OverviewBtn != null) OverviewBtn.Label = L("Back");

            FadeIn(OverviewScroll);
        }

        private void HideOverview()
        {
            if (!_overviewVisible) return;
            _overviewVisible = false;

            FadeOut(OverviewScroll, () =>
            {
                PanoramaScroll.Visibility = Visibility.Visible;
                PanoramaScroll.ChangeView(null, null, 1.0f, true);
                OverviewScroll.ChangeView(null, null, 1.0f, true);
                OverviewScroll.Visibility = Visibility.Collapsed;
                if (OverviewBtn != null) OverviewBtn.Label = L("Overview");
            });
        }

        private void FadeIn(Windows.UI.Xaml.UIElement target)
        {
            var anim = new Windows.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Windows.UI.Xaml.Duration(TimeSpan.FromMilliseconds(200))
            };
            Storyboard.SetTarget(anim, target);
            Storyboard.SetTargetProperty(anim, "Opacity");
            var sb = new Storyboard();
            sb.Children.Add(anim);
            sb.Begin();
        }

        private void FadeOut(Windows.UI.Xaml.UIElement target, Action onComplete)
        {
            var anim = new Windows.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = new Windows.UI.Xaml.Duration(TimeSpan.FromMilliseconds(200))
            };
            Storyboard.SetTarget(anim, target);
            Storyboard.SetTargetProperty(anim, "Opacity");
            var sb = new Storyboard();
            sb.Children.Add(anim);
            sb.Completed += (s, e) => onComplete();
            sb.Begin();
        }

        private void BuildOverviewItems()
        {
            var items = new System.Collections.ObjectModel.ObservableCollection<OverviewItem>();

            int vids = PlayHistory.GetRecent("Videos").Count;
            int music = PlayHistory.GetRecent("Music").Count;
            int photos = PlayHistory.GetRecent("Photos").Count;
            int libs = PlaylistLibrary.GetPlaylistNames().Count;
            bool en = (Application.Current.Resources["AppText"] as AppText)?.IsEnglish ?? false;

            items.Add(new OverviewItem
            {
                Glyph = "\uD83C\uDFAC",
                Title = L("Videos"),
                Subtitle = vids > 0 ? L("RecentPlayed") + " " + vids + (en ? " items" : " 项") : L("OpenFile"),
                TileBrush = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0xE0, 0x40, 0xFB)),
                Action = "category:1"
            });
            items.Add(new OverviewItem
            {
                Glyph = "\uD83C\uDFB5",
                Title = L("Music"),
                Subtitle = music > 0 ? L("RecentPlayed") + " " + music + (en ? " items" : " 项") : L("OpenFile"),
                TileBrush = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0x00, 0xBC, 0xD4)),
                Action = "category:2"
            });
            items.Add(new OverviewItem
            {
                Glyph = "\uD83D\uDCF7",
                Title = L("Photos"),
                Subtitle = photos > 0 ? L("RecentPlayed") + " " + photos + (en ? " items" : " 项") : L("OpenFile"),
                TileBrush = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0x76, 0xFF, 0x00)),
                Action = "category:3"
            });

            var names = PlaylistLibrary.GetPlaylistNames();
            if (names.Count == 0)
            {
                items.Add(new OverviewItem
                {
                    Glyph = "\uD83C\uDFB6",
                    Title = L("Playlists"),
                    Subtitle = L("NoPlaylists"),
                    TileBrush = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0x88, 0x88, 0xFF)),
                    Action = "playlists"
                });
            }
            else
            {
                foreach (var name in names)
                {
                    int cnt = PlaylistLibrary.GetPlaylistFiles(name).Count;
                    items.Add(new OverviewItem
                    {
                        Glyph = "\uD83C\uDFB6",
                        Title = name.Length > 8 ? name.Substring(0, 8) : name,
                        Subtitle = cnt + (en ? " tracks · tap to play" : " 首 · 点击播放"),
                        TileBrush = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0x88, 0x88, 0xFF)),
                        Action = "playlist:" + name
                    });
                }
            }

            items.Add(new OverviewItem
            {
                Glyph = "\uD83D\uDCC1",
                Title = L("Library"),
                Subtitle = libs > 0 ? L("BrowseFolder") : L("AddFolder"),
                TileBrush = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0x4A, 0x4A, 0x5A)),
                Action = "library"
            });

            OverviewView.ItemsSource = items;
        }

        private void OverviewView_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as OverviewItem;
            if (item == null) return;
            HandleOverviewAction(item.Action);
        }

        private async void HandleOverviewAction(string action)
        {
            if (string.IsNullOrEmpty(action)) return;

            if (action.StartsWith("category:"))
            {
                int idx;
                if (int.TryParse(action.Substring("category:".Length), out idx))
                {
                    ToggleOverview();
                    ScrollToSection(idx);
                }
                return;
            }

            if (action == "playlists")
            {
                ToggleOverview();
                PlaylistsButton_Click(null, null);
                return;
            }

            if (action == "library")
            {
                ToggleOverview();
                LibraryButton_Click(null, null);
                return;
            }

            if (action.StartsWith("playlist:"))
            {
                string name = action.Substring("playlist:".Length);
                ToggleOverview();
                await PlayPlaylistCore(name);
                return;
            }
        }

        private async System.Threading.Tasks.Task PlayPlaylistCore(string name)
        {
            var files = PlaylistLibrary.GetPlaylistFiles(name);
            if (files == null || files.Count == 0)
            {
                ShowOverlay(L("PlaylistEmpty"));
                return;
            }
            try
            {
                StorageFile first = null;
                var extras = new List<string>();
                for (int i = 0; i < files.Count; i++)
                {
                    try
                    {
                        var f = await StorageFile.GetFileFromPathAsync(files[i]);
                        if (i == 0) first = f;
                        else extras.Add(files[i]);
                    }
                    catch (Exception ex) { Debug.WriteLine("[HyperMedia] Playlist file missing: {0}: {1}", files[i], ex.Message); }
                }
                if (first == null)
                {
                    ShowOverlay(L("PlaylistUnavailable"));
                    return;
                }
                StorageApplicationPermissions.FutureAccessList.AddOrReplace("PlaybackFile", first);
                if (extras.Count > 0)
                    ApplicationData.Current.LocalSettings.Values["PlaylistExtras"] = string.Join("|", extras);
                Frame.Navigate(typeof(MainPage));
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] PlayPlaylist failed: {0}", ex.Message); }
        }

        #endregion

        #region Drag & Drop

        private void Page_DragOver(object sender, DragEventArgs e)
        {
            DropOverlay.Visibility = Visibility.Visible;
        }

        private async void Page_Drop(object sender, DragEventArgs e)
        {
            DropOverlay.Visibility = Visibility.Collapsed;

            try
            {
                var view = e.Data.GetView();
                if (view.Contains(StandardDataFormats.StorageItems))
                {
                    var items = await view.GetStorageItemsAsync();
                    if (items.Count > 0)
                    {
                        StorageApplicationPermissions.FutureAccessList.AddOrReplace("PlaybackFile", items[0] as StorageFile);
                        if (items.Count > 1)
                        {
                            var extras = new List<string>();
                            for (int i = 1; i < items.Count; i++)
                            {
                                var f = items[i] as StorageFile;
                                if (f != null) extras.Add(f.Path);
                            }
                            if (extras.Count > 0)
                                ApplicationData.Current.LocalSettings.Values["PlaylistExtras"] = string.Join("|", extras);
                        }
                        Frame.Navigate(typeof(MainPage));
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
        }

        #endregion
    }
}
