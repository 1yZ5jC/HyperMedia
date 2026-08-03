using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        private const string ICON_LIST = "\u25A1";
        private const string ICON_TILE = "\u25A3";

        public HomePage()
        {
            this.InitializeComponent();
            Window.Current.CoreWindow.PointerEntered += CoreWindow_PointerEntered;
            this.Loaded += (s, e) =>
            {
                ApplyTheme();
                ApplyHomeLanguage();
            };
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
            InitSearchPane();
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
            try
            {
                if (_searchPane != null)
                    _searchPane.QuerySubmitted -= SearchPane_QuerySubmitted;
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] SearchPane unsub failed: {0}", ex.Message); }
        }

        private Windows.ApplicationModel.Search.SearchPane _searchPane;

        private void InitSearchPane()
        {
            try
            {
                _searchPane = Windows.ApplicationModel.Search.SearchPane.GetForCurrentView();
                _searchPane.QuerySubmitted += SearchPane_QuerySubmitted;
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] SearchPane init failed: {0}", ex.Message); }
        }

        private void SearchPane_QuerySubmitted(Windows.ApplicationModel.Search.SearchPane sender,
            Windows.ApplicationModel.Search.SearchPaneQuerySubmittedEventArgs args)
        {
            try
            {
                string query = (args.QueryText ?? "").Trim();
                if (string.IsNullOrEmpty(query)) return;
                ApplySearchFilter(query);
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Search failed: {0}", ex.Message); }
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

        private const double SECTION_HERO = 580;
        private const double SECTION_WIDTH = 480;
        private const double SECTION_GUTTER = 40;
        private static readonly double[] SnapOffsets = { 0, SECTION_HERO + SECTION_GUTTER, SECTION_HERO + SECTION_GUTTER * 2 + SECTION_WIDTH, SECTION_HERO + SECTION_GUTTER * 3 + SECTION_WIDTH * 2 };
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
            double best = 0;
            double bestDist = double.MaxValue;
            foreach (double off in SnapOffsets)
            {
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

            // Shift+wheel = horizontal panorama paging; plain wheel = vertical scrolling
            bool shift = (Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0;
            if (shift)
            {
                double newOffset = PanoramaScroll.HorizontalOffset + (delta > 0 ? -180 : 180);
                newOffset = Math.Max(0, Math.Min(newOffset, PanoramaScroll.ScrollableWidth));
                PanoramaScroll.ChangeView(newOffset, null, null, true);
            }
            else
            {
                double newOffsetY = PanoramaScroll.VerticalOffset + (delta > 0 ? -90 : 90);
                newOffsetY = Math.Max(0, Math.Min(newOffsetY, PanoramaScroll.ScrollableHeight));
                PanoramaScroll.ChangeView(null, newOffsetY, null, true);
            }
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

        private const string KEY_LIBRARY_FOLDER = "LibraryFolderToken";
        private const string KEY_LIBRARY_PATH = "LibraryFolderPath";

        private static readonly string[] LibraryExtensions = {
            ".mp4", ".avi", ".mkv", ".webm", ".flv", ".mov", ".wmv", ".3gp", ".ts", ".mpg", ".mpeg", ".m4v",
            ".mp3", ".flac", ".wav", ".aac", ".ogg", ".wma", ".m4a", ".opus",
            ".jpg", ".jpeg", ".png", ".bmp", ".gif"
        };

        private async void LibraryButton_Click(object sender, RoutedEventArgs e)
        {
            var popup = new Popup();
            popup.Width = 520;
            popup.Height = 620;

            var border = new Border();
            border.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xF0, 0x0A, 0x0A, 0x0F));
            border.Width = 520;
            border.Padding = new Thickness(24);

            var panel = new StackPanel();

            var title = new TextBlock();
            title.Text = L("Library");
            title.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
            title.FontSize = 16;
            title.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x40, 0xFB));
            title.Margin = new Thickness(0, 0, 0, 8);
            panel.Children.Add(title);

            var addBtn = new Button();
            addBtn.Content = L("AddFolderBtn");
            addBtn.Margin = new Thickness(0, 4, 0, 12);
            addBtn.Click += async (s, ev) =>
            {
                var picker = new FolderPicker();
                picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
                picker.FileTypeFilter.Add("*");
                var folder = await picker.PickSingleFolderAsync();
                if (folder != null)
                {
                    try
                    {
                        string token = StorageApplicationPermissions.FutureAccessList.Add(folder);
                        ApplicationData.Current.LocalSettings.Values[KEY_LIBRARY_FOLDER] = token;
                        ApplicationData.Current.LocalSettings.Values[KEY_LIBRARY_PATH] = folder.Path;
                        ShowOverlay("已添加媒体库: " + folder.Name);
                    }
                    catch (Exception ex) { Debug.WriteLine("[HyperMedia] Library add failed: {0}", ex.Message); }
                }
            };
            panel.Children.Add(addBtn);

            var filesList = new ListBox();
            filesList.MaxHeight = 300;
            filesList.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
            filesList.BorderThickness = new Thickness(0);
            filesList.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
            filesList.FontSize = 13;
            filesList.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
            try
            {
                filesList.ItemContainerStyle = Application.Current.Resources["ZuneListBoxItemStyle"] as Style;
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] ItemContainerStyle failed: {0}", ex.Message); }
            panel.Children.Add(filesList);

            var settings = ApplicationData.Current.LocalSettings;
            string token2 = settings.Values.ContainsKey(KEY_LIBRARY_FOLDER) ? settings.Values[KEY_LIBRARY_FOLDER] as string : null;
            if (!string.IsNullOrEmpty(token2))
            {
                try
                {
                    var folder = await StorageApplicationPermissions.FutureAccessList.GetFolderAsync(token2);
                    var files = await folder.GetFilesAsync();
                    int count = 0;
                    foreach (var f in files)
                    {
                        string ext = f.FileType.ToLowerInvariant();
                        if (Array.IndexOf(LibraryExtensions, ext) >= 0)
                        {
                            string path = f.Path;
                            var item = new ListBoxItem();
                            item.Content = f.Name;
                            item.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
                            item.FontSize = 13;
                            item.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
                            item.Padding = new Thickness(10, 8, 10, 8);
                            item.Margin = new Thickness(0, 2, 0, 2);
                            item.Tapped += async (s, ev) =>
                            {
                                popup.IsOpen = false;
                                try
                                {
                                    var storageFile = await StorageFile.GetFileFromPathAsync(path);
                                    StorageApplicationPermissions.FutureAccessList.AddOrReplace("PlaybackFile", storageFile);
                                    Frame.Navigate(typeof(MainPage));
                                }
                                catch (Exception ex) { Debug.WriteLine("[HyperMedia] Library open failed: {0}", ex.Message); }
                            };
                            filesList.Items.Add(item);
                            count++;
                        }
                    }
                    if (count == 0)
                    {
                        var empty = new ListBoxItem();
                        empty.Content = L("FolderEmpty");
                        empty.IsEnabled = false;
                        empty.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
                        empty.FontSize = 13;
                        empty.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF));
                        empty.Padding = new Thickness(10, 8, 10, 8);
                        filesList.Items.Add(empty);
                    }
                }
                catch (Exception ex) { Debug.WriteLine("[HyperMedia] Library load failed: {0}", ex.Message); }
            }
            else
            {
                var empty = new ListBoxItem();
                empty.Content = L("NoFolderYet");
                empty.IsEnabled = false;
                empty.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
                empty.FontSize = 13;
                empty.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF));
                empty.Padding = new Thickness(10, 8, 10, 8);
                filesList.Items.Add(empty);
            }

            // Network devices (UPnP/DLNA discovery — Win 8.1 has no content-browse API, only device discovery)
            var netTitle = new TextBlock();
            netTitle.Text = L("NetworkDevices");
            netTitle.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
            netTitle.FontSize = 11;
            netTitle.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF));
            netTitle.Margin = new Thickness(0, 14, 0, 6);
            panel.Children.Add(netTitle);

            var netList = new ListBox();
            netList.MaxHeight = 160;
            netList.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
            netList.BorderThickness = new Thickness(0);
            netList.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
            netList.FontSize = 13;
            netList.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
            try
            {
                netList.ItemContainerStyle = Application.Current.Resources["ZuneListBoxItemStyle"] as Style;
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] ItemContainerStyle failed: {0}", ex.Message); }
            panel.Children.Add(netList);

            var netLoading = new TextBlock();
            netLoading.Text = L("Scanning");
            netLoading.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
            netLoading.FontSize = 12;
            netLoading.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF));
            netLoading.Margin = new Thickness(10, 6, 0, 6);
            netList.Items.Add(new ListBoxItem { Content = netLoading.Text, IsEnabled = false, FontSize = 12 });

            var netDevices = await DiscoverNetworkDevices();
            netList.Items.Clear();
            if (netDevices.Count == 0)
            {
                var empty = new ListBoxItem();
                empty.Content = L("NoDevices");
                empty.IsEnabled = false;
                empty.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
                empty.FontSize = 12;
                empty.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF));
                netList.Items.Add(empty);
            }
            else
            {
                foreach (var dev in netDevices)
                {
                    var item = new ListBoxItem();
                    item.Content = "\uD83D\uDDA5\uFE0F " + dev.Item1;
                    item.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
                    item.FontSize = 12;
                    item.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
                    item.Padding = new Thickness(10, 6, 10, 6);
                    item.IsEnabled = false;
                    netList.Items.Add(item);
                }
            }

            var closeBtn = new Button();
            closeBtn.Content = L("Close");
            closeBtn.HorizontalAlignment = HorizontalAlignment.Right;
            closeBtn.Margin = new Thickness(0, 12, 0, 0);
            closeBtn.Click += (s, ev) => { popup.IsOpen = false; };
            panel.Children.Add(closeBtn);

            border.Child = panel;
            popup.Child = border;

            var bounds = Window.Current.Bounds;
            popup.HorizontalOffset = (bounds.Width - 520) / 2;
            popup.VerticalOffset = (bounds.Height - 620) / 2;

            popup.IsOpen = true;
        }

        private void ShowOverlay(string text)
        {
            StatusText.Text = text;
        }

        private async System.Threading.Tasks.Task<List<Tuple<string, string>>> DiscoverNetworkDevices()
        {
            var result = new List<Tuple<string, string>>();
            try
            {
                var devices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(
                    Windows.Devices.Enumeration.DeviceClass.All);
                foreach (var d in devices)
                {
                    if (d == null) continue;
                    string name = d.Name ?? "";
                    string id = d.Id ?? "";
                    if (string.IsNullOrEmpty(name)) continue;

                    // Skip local hardware (GUID-based ids) and common local interface names
                    if (id.Contains("{")) continue;
                    if (name.IndexOf("Ethernet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Wi-Fi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Bluetooth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("WAN", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Realtek", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Monitor", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                    result.Add(Tuple.Create(name, "网络设备"));
                    if (result.Count >= 30) break;
                }
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] DiscoverNetworkDevices failed: {0}", ex.Message); }
            return result;
        }

        #endregion

        #region Open URL

        private void OpenUrlButton_Click(object sender, RoutedEventArgs e)
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
            title.Text = L("OpenNetworkMedia");
            title.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
            title.FontSize = 14;
            title.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x40, 0xFB));
            title.Margin = new Thickness(0, 0, 0, 16);
            panel.Children.Add(title);

            var textBox = new TextBox();
            textBox.PlaceholderText = L("UrlPlaceholder");
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
                        LaunchUrl(url);
                }
            };
            panel.Children.Add(textBox);

            var history = PlayHistory.GetUrlHistory();
            if (history.Count > 0)
            {
                var historyTitle = new TextBlock();
                historyTitle.Text = L("RecentOpened");
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
                catch (Exception ex) { Debug.WriteLine("[HyperMedia] ItemContainerStyle failed: {0}", ex.Message); }
                foreach (var url in history)
                {
                    var item = new ListBoxItem();
                    item.Content = url;
                    item.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
                    item.FontSize = 12;
                    item.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
                    item.Padding = new Thickness(8, 6, 8, 6);
                    item.Tapped += (s, ev) => { textBox.Text = url; };
                    historyList.Items.Add(item);
                }
                panel.Children.Add(historyList);
            }

            var btnPanel = new StackPanel();
            btnPanel.Orientation = Windows.UI.Xaml.Controls.Orientation.Horizontal;
            btnPanel.HorizontalAlignment = HorizontalAlignment.Right;
            btnPanel.Margin = new Thickness(0, 16, 0, 0);

            var cancelBtn = new Button();
            cancelBtn.Content = L("Cancel");
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            cancelBtn.Click += (s, ev) => { popup.IsOpen = false; };
            btnPanel.Children.Add(cancelBtn);

            var playBtn = new Button();
            playBtn.Content = L("Play");
            playBtn.Click += (s, ev) =>
            {
                string url = textBox.Text.Trim();
                popup.IsOpen = false;
                if (!string.IsNullOrEmpty(url))
                    LaunchUrl(url);
            };
            btnPanel.Children.Add(playBtn);

            panel.Children.Add(btnPanel);
            border.Child = panel;

            popup.Child = border;
            popup.Width = 500;
            popup.Height = 340;

            var bounds = Window.Current.Bounds;
            popup.HorizontalOffset = (bounds.Width - 500) / 2;
            popup.VerticalOffset = (bounds.Height - 340) / 2;

            popup.IsOpen = true;
            textBox.Focus(FocusState.Programmatic);
        }

        private void LaunchUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            Frame.Navigate(typeof(MainPage), url);
        }

        #endregion

        #region File Open

        private async void OpenFilesWithFilter(string filterExtensions, PickerLocationId location)
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = location;
            foreach (var ext in filterExtensions.Split(','))
                picker.FileTypeFilter.Add(ext.Trim());

            var files = await picker.PickMultipleFilesAsync();
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
        }

        private async void OpenButton_Click(object sender, RoutedEventArgs e)
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

            var files = await picker.PickMultipleFilesAsync();
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
        }

        #endregion

        private async void IdentifyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                picker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
                string[] audioExts = { ".mp3", ".flac", ".wav", ".aac", ".ogg", ".wma", ".m4a", ".opus", ".ape", ".alac", ".aiff" };
                foreach (var ext in audioExts)
                    picker.FileTypeFilter.Add(ext);

                var file = await picker.PickSingleFileAsync();
                if (file == null) return;

                string origText = IdentifyBtnText != null ? IdentifyBtnText.Text : "";
                if (IdentifyBtnText != null) IdentifyBtnText.Text = "识别中...";

                var samples = await ShazamAudioExtractor.Extract16kMonoAsync(file.Path, 8.0);
                if (samples == null || samples.Length < 16000 * 2)
                {
                    if (IdentifyBtnText != null) IdentifyBtnText.Text = origText;
                    await new Windows.UI.Popups.MessageDialog(L("IdentifyFailed"), L("IdentifyTitle")).ShowAsync();
                    return;
                }

                var track = await ShazamRecognizer.RecognizeAsync(samples);
                if (IdentifyBtnText != null) IdentifyBtnText.Text = origText;

                ShowIdentifyResult(file, track);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HyperMedia] IdentifyButton_Click FAILED: {0}", ex.Message);
                try { await new Windows.UI.Popups.MessageDialog(L("IdentifyFailed"), L("IdentifyTitle")).ShowAsync(); } catch { }
            }
        }

        private void ShowIdentifyResult(StorageFile file, ShazamRecognizer.TrackResult track)
        {
            var popup = new Popup();
            popup.Width = 520;
            popup.IsLightDismissEnabled = true;

            var content = new StackPanel { MaxWidth = 520 };
            var panel = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x0D, 0x0D, 0x16)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x40, 0xFB)),
                BorderThickness = new Thickness(1, 0, 0, 0),
                Padding = new Thickness(28, 24, 28, 24),
                Child = content
            };

            content.Children.Add(new TextBlock
            {
                Text = L("IdentifyTitle"),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 20,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x40, 0xFB)),
                Margin = new Thickness(0, 0, 0, 16)
            });

            if (track == null || string.IsNullOrEmpty(track.Title))
            {
                content.Children.Add(new TextBlock
                {
                    Text = L("IdentifyNoMatch"),
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                    TextWrapping = TextWrapping.Wrap
                });
            }
            else
            {
                content.Children.Add(new TextBlock
                {
                    Text = track.Title,
                    FontFamily = new FontFamily("Segoe UI Light"),
                    FontSize = 30,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 4)
                });

                content.Children.Add(new TextBlock
                {
                    Text = track.Subtitle ?? "",
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 16,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
                    Margin = new Thickness(0, 0, 0, 20)
                });

                var playBtn = new Button
                {
                    Content = L("IdentifyPlay"),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x40, 0xFB)),
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x0A, 0x0A, 0x0F)),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(20, 10, 20, 10),
                    Margin = new Thickness(0, 8, 0, 0)
                };
                playBtn.Click += (s, ev) =>
                {
                    popup.IsOpen = false;
                    try
                    {
                        StorageApplicationPermissions.FutureAccessList.AddOrReplace("PlaybackFile", file);
                        Frame.Navigate(typeof(MainPage));
                    }
                    catch (Exception ex) { Debug.WriteLine("[HyperMedia] Identify play failed: {0}", ex.Message); }
                };
                content.Children.Add(playBtn);

                var editBtn = new Button
                {
                    Content = L("MetaEdit"),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x22, 0xE0, 0x40, 0xFB)),
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(20, 10, 20, 10),
                    Margin = new Thickness(0, 8, 0, 0)
                };
                editBtn.Click += async (s, ev) =>
                {
                    popup.IsOpen = false;
                    await ShowMetadataEditor(file, track);
                };
                content.Children.Add(editBtn);
            }

            popup.Child = panel;
            popup.HorizontalOffset = 40;
            popup.VerticalOffset = 60;
            popup.IsOpen = true;
        }

        private async System.Threading.Tasks.Task ShowMetadataEditor(StorageFile file, ShazamRecognizer.TrackResult track)
        {
            try
            {
                var editPopup = new Popup();
                editPopup.Width = 560;
                editPopup.IsLightDismissEnabled = true;

                var root = new StackPanel();
                var panel = new Border
                {
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x0D, 0x0D, 0x16)),
                    BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x40, 0xFB)),
                    BorderThickness = new Thickness(1, 0, 0, 0),
                    Padding = new Thickness(28, 24, 28, 24),
                    Child = root
                };

                root.Children.Add(new TextBlock
                {
                    Text = L("MetaEditTitle"),
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 20,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x40, 0xFB)),
                    Margin = new Thickness(0, 0, 0, 8)
                });
                root.Children.Add(new TextBlock
                {
                    Text = file.Name,
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 16)
                });

                // Fields (initially from recognition result)
                var titleBox = new TextBox { Text = track != null ? track.Title : "", Margin = new Thickness(0, 4, 0, 8) };
                var artistBox = new TextBox { Text = track != null ? track.Subtitle : "", Margin = new Thickness(0, 4, 0, 8) };
                var albumBox = new TextBox { Text = "", Margin = new Thickness(0, 4, 0, 8) };
                var dateBox = new TextBox { Text = "", Margin = new Thickness(0, 4, 0, 8) };

                root.Children.Add(MakeField(L("MetaTitle"), titleBox));
                root.Children.Add(MakeField(L("MetaArtist"), artistBox));
                root.Children.Add(MakeField(L("MetaAlbum"), albumBox));
                root.Children.Add(MakeField(L("MetaDate"), dateBox));

                var statusTb = new TextBlock
                {
                    Text = L("MetaCollecting"),
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF)),
                    Margin = new Thickness(0, 4, 0, 8),
                    TextWrapping = TextWrapping.Wrap
                };
                root.Children.Add(statusTb);

                var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
                var saveBtn = new Button
                {
                    Content = L("MetaSave"),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x40, 0xFB)),
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x0A, 0x0A, 0x0F)),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(20, 10, 20, 10),
                    Margin = new Thickness(0, 0, 8, 0)
                };
                var cancelBtn = new Button
                {
                    Content = L("Cancel"),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(16, 10, 16, 10)
                };
                cancelBtn.Click += (s2, ev2) => { editPopup.IsOpen = false; };
                btnRow.Children.Add(saveBtn);
                btnRow.Children.Add(cancelBtn);
                root.Children.Add(btnRow);

                editPopup.Child = panel;
                editPopup.HorizontalOffset = 40;
                editPopup.VerticalOffset = 60;
                editPopup.IsOpen = true;

                // Collect public metadata in the background and fill fields
                string artist = track != null ? track.Subtitle : "";
                string title = track != null ? track.Title : "";
                string collectedCoverUrl = null;
                var collected = await MetadataEditor.CollectAsync(artist, title);
                if (collected != null)
                {
                    if (!string.IsNullOrEmpty(collected.Title)) titleBox.Text = collected.Title;
                    if (!string.IsNullOrEmpty(collected.Artist)) artistBox.Text = collected.Artist;
                    if (!string.IsNullOrEmpty(collected.Album)) albumBox.Text = collected.Album;
                    if (!string.IsNullOrEmpty(collected.Date)) dateBox.Text = collected.Date;
                    collectedCoverUrl = collected.CoverUrl;
                    statusTb.Text = L("MetaCollected") + (collected.DurationMs > 0 ? "  (" + FormatDuration(collected.DurationMs) + ")" : "");
                    if (!string.IsNullOrEmpty(collectedCoverUrl))
                        statusTb.Text += "  (封面已获取)";
                }
                else
                {
                    statusTb.Text = L("MetaCollectFailed");
                }

                saveBtn.Click += async (s3, ev3) =>
                {
                    try
                    {
                        saveBtn.IsEnabled = false;
                        bool ok = await WriteTagsAsync(file, titleBox.Text, artistBox.Text, albumBox.Text, dateBox.Text, collectedCoverUrl);
                        statusTb.Text = ok ? L("MetaSaved") : L("MetaSaveFailed");
                        if (ok)
                        {
                            saveBtn.Content = L("MetaSaved");
                            await System.Threading.Tasks.Task.Delay(600);
                            editPopup.IsOpen = false;
                        }
                        else
                        {
                            saveBtn.IsEnabled = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[HyperMedia] Meta save failed: {0}", ex.Message);
                        statusTb.Text = L("MetaSaveFailed");
                        saveBtn.IsEnabled = true;
                    }
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HyperMedia] ShowMetadataEditor FAILED: {0}", ex.Message);
            }
        }

        private static StackPanel MakeField(string label, TextBox box)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
            sp.Children.Add(new TextBlock
            {
                Text = label,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF))
            });
            sp.Children.Add(box);
            return sp;
        }

        private static string FormatDuration(int ms)
        {
            int totalSec = ms / 1000;
            return (totalSec / 60) + ":" + (totalSec % 60).ToString("00");
        }

        // Write ID3/FLAC tags via libVLCX by opening a fresh Media on the file.
        // Optionally embeds a cover image: download coverUrl to a temp file and
        // point ArtworkURL at it (libVLC stores the picture during saveMeta).
        private static async System.Threading.Tasks.Task<bool> WriteTagsAsync(StorageFile file,
            string title, string artist, string album, string date, string coverUrl)
        {
            string coverPath = null;
            if (!string.IsNullOrEmpty(coverUrl))
            {
                coverPath = await DownloadCoverAsync(coverUrl);
                if (coverPath == null)
                    System.Diagnostics.Debug.WriteLine("[HyperMedia] WriteTags: cover download failed, skipping cover");
            }

            string localCover = coverPath;
            return await System.Threading.Tasks.Task.Run<bool>(() =>
            {
                try
                {
                    var args = new List<string> { "-I", "dummy", "--no-plugins-cache", "--no-osd" };
                    var inst = new libVLCX.Instance(args, null);
                    var media = new libVLCX.Media(inst, file.Path, libVLCX.FromType.FromPath);
                    media.setMeta(libVLCX.MediaMeta.Title, title ?? "");
                    media.setMeta(libVLCX.MediaMeta.Artist, artist ?? "");
                    media.setMeta(libVLCX.MediaMeta.Album, album ?? "");
                    media.setMeta(libVLCX.MediaMeta.Date, date ?? "");
                    if (!string.IsNullOrEmpty(localCover))
                        media.setMeta(libVLCX.MediaMeta.ArtworkURL, localCover);
                    bool ok = media.saveMeta() == 0;
                    if (ok && !string.IsNullOrEmpty(localCover))
                        System.Diagnostics.Debug.WriteLine("[HyperMedia] WriteTags: cover embedded from {0}", localCover);
                    return ok;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[HyperMedia] WriteTagsAsync FAILED: {0}", ex.Message);
                    return false;
                }
            });
        }

        private static async System.Threading.Tasks.Task<string> DownloadCoverAsync(string coverUrl)
        {
            try
            {
                using (var client = new Windows.Web.Http.HttpClient())
                {
                    var resp = await client.GetAsync(new Uri(coverUrl)).AsTask(
                        new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15)).Token);
                    if (resp.StatusCode != Windows.Web.Http.HttpStatusCode.Ok) return null;
                    var buf = await resp.Content.ReadAsBufferAsync();
                    var bytes = new byte[buf.Length];
                    Windows.Storage.Streams.DataReader.FromBuffer(buf).ReadBytes(bytes);
                    if (bytes.Length == 0) return null;

                    string ext = ".jpg";
                    string lower = coverUrl.ToLowerInvariant();
                    if (lower.Contains(".png")) ext = ".png";
                    var tempFile = await ApplicationData.Current.TemporaryFolder.CreateFileAsync(
                        "hypermedia_cover" + ext, Windows.Storage.CreationCollisionOption.ReplaceExisting);
                    await Windows.Storage.FileIO.WriteBytesAsync(tempFile, bytes);
                    System.Diagnostics.Debug.WriteLine("[HyperMedia] DownloadCover: saved {0} ({1} bytes)", tempFile.Path, bytes.Length);
                    return tempFile.Path;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[HyperMedia] DownloadCover FAILED: {0}", ex.Message);
                return null;
            }
        }

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
                ShowUrlInputOverlay();
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
                double newOffset = PanoramaScroll.HorizontalOffset + (e.Key == VirtualKey.Right ? SECTION_HERO : -SECTION_HERO);
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
            var popup = new Popup();
            popup.Width = 560;
            popup.Height = 460;

            var border = new Border();
            border.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xF0, 0x0A, 0x0A, 0x0F));
            border.Width = 560;
            border.Padding = new Thickness(24);

            var panel = new StackPanel();

            var title = new TextBlock();
            title.Text = L("MyPlaylists");
            title.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
            title.FontSize = 16;
            title.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x40, 0xFB));
            title.Margin = new Thickness(0, 0, 0, 4);
            panel.Children.Add(title);

            var hint = new TextBlock();
            hint.Text = L("PlaylistHint");
            hint.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
            hint.FontSize = 11;
            hint.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
            hint.Margin = new Thickness(0, 0, 0, 12);
            hint.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(hint);

            // Smart playlists (auto-generated from history metadata)
            var smartTitle = new TextBlock();
            smartTitle.Text = L("SmartPlaylists");
            smartTitle.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
            smartTitle.FontSize = 11;
            smartTitle.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF));
            smartTitle.Margin = new Thickness(0, 0, 0, 6);
            panel.Children.Add(smartTitle);

            var smartList = new ListBox();
            smartList.MaxHeight = 150;
            smartList.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
            smartList.BorderThickness = new Thickness(0);
            smartList.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
            smartList.FontSize = 13;
            smartList.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
            try
            {
                smartList.ItemContainerStyle = Application.Current.Resources["ZuneListBoxItemStyle"] as Style;
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] ItemContainerStyle failed: {0}", ex.Message); }
            panel.Children.Add(smartList);

            AddSmartItem(smartList, popup, L("TopRated"), "toprated");
            AddSmartItem(smartList, popup, L("MostPlayed"), "mostplayed");
            AddSmartItem(smartList, popup, L("RecentlyPlayedSmart"), "recent");

            var names = PlaylistLibrary.GetPlaylistNames();
            if (names.Count == 0)
            {
                var empty = new TextBlock();
                empty.Text = L("NoPlaylistYet");
                empty.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
                empty.FontSize = 13;
                empty.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
                empty.Margin = new Thickness(0, 20, 0, 0);
                empty.HorizontalAlignment = HorizontalAlignment.Center;
                panel.Children.Add(empty);
            }
            else
            {
                var playlistList = new ListBox();
                playlistList.MaxHeight = 380;
                playlistList.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
                playlistList.BorderThickness = new Thickness(0);
                playlistList.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
                playlistList.FontSize = 14;
                playlistList.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
                try
                {
                    playlistList.ItemContainerStyle = Application.Current.Resources["ZuneListBoxItemStyle"] as Style;
                }
                catch (Exception ex) { Debug.WriteLine("[HyperMedia] ItemContainerStyle failed: {0}", ex.Message); }
                panel.Children.Add(playlistList);

                foreach (var name in names)
                {
                    var files = PlaylistLibrary.GetPlaylistFiles(name);
                    var item = new ListBoxItem();
                    item.Padding = new Thickness(10, 10, 10, 10);
                    item.Margin = new Thickness(0, 2, 0, 2);
                    item.Content = name + "  (" + files.Count + " 首)";
                    item.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
                    item.FontSize = 14;
                    item.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
                    item.Tapped += async (s, ev) =>
                    {
                        await PlayPlaylist(popup, name);
                    };
                    item.RightTapped += async (s, ev) =>
                    {
                        var menu = new Windows.UI.Popups.PopupMenu();
                        menu.Commands.Add(new Windows.UI.Popups.UICommand(L("Play"), async (cmd) => { await PlayPlaylist(popup, name); }));
                        menu.Commands.Add(new Windows.UI.Popups.UICommand(L("PinToStart"), async (cmd) =>
                        {
                            try
                            {
                                var tile = new Windows.UI.StartScreen.SecondaryTile();
                                tile.TileId = "HyperMediaPlaylist_" + name;
                                tile.DisplayName = name;
                                tile.Arguments = "playlist:" + name;
                                tile.VisualElements.Square150x150Logo = new Uri("ms-appx:///Assets/Logo.png");
                                tile.VisualElements.ShowNameOnSquare150x150Logo = true;
                                tile.VisualElements.ForegroundText = Windows.UI.StartScreen.ForegroundText.Light;
                                bool created = await tile.RequestCreateAsync();
                                if (created)
                                    ShowOverlay(L("PinnedToStart") + name);
                            }
                            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Pin tile failed: {0}", ex.Message); }
                        }));
                        menu.Commands.Add(new Windows.UI.Popups.UICommand(L("DeletePlaylist"), (cmd) =>
                        {
                            PlaylistLibrary.DeletePlaylist(name);
                            popup.IsOpen = false;
                            ShowOverlay(L("PlaylistDeleted") + name);
                        }));
                        await menu.ShowForSelectionAsync(new Rect(ev.GetPosition(null), new Size(1, 1)), Windows.UI.Popups.Placement.Above);
                    };
                    playlistList.Items.Add(item);
                }
            }

            var closeBtn = new Button();
            closeBtn.Content = L("Close");
            closeBtn.HorizontalAlignment = HorizontalAlignment.Right;
            closeBtn.Margin = new Thickness(0, 12, 0, 0);
            closeBtn.Click += (s, ev) => { popup.IsOpen = false; };
            panel.Children.Add(closeBtn);

            border.Child = panel;
            popup.Child = border;

            var bounds = Window.Current.Bounds;
            popup.HorizontalOffset = (bounds.Width - 560) / 2;
            popup.VerticalOffset = (bounds.Height - 460) / 2;

            popup.IsOpen = true;
        }

        private void AddSmartItem(ListBox smartList, Popup popup, string label, string kind)
        {
            var item = new ListBoxItem();
            item.Content = label;
            item.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
            item.FontSize = 13;
            item.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
            item.Padding = new Thickness(10, 8, 10, 8);
            item.Tapped += async (s, ev) =>
            {
                var paths = PlaylistLibrary.GetSmartPlaylist(kind);
                if (paths == null || paths.Count == 0)
                {
                    ShowOverlay(L("SmartPlaylistEmpty"));
                    return;
                }
                popup.IsOpen = false;
                await PlayPaths(paths);
            };
            smartList.Items.Add(item);
        }

        private async System.Threading.Tasks.Task PlayPaths(System.Collections.Generic.List<string> paths)
        {
            try
            {
                StorageFile first = null;
                var extras = new List<string>();
                for (int i = 0; i < paths.Count; i++)
                {
                    try
                    {
                        var f = await StorageFile.GetFileFromPathAsync(paths[i]);
                        if (i == 0) first = f;
                        else extras.Add(paths[i]);
                    }
                    catch (Exception ex) { Debug.WriteLine("[HyperMedia] Path missing: {0}: {1}", paths[i], ex.Message); }
                }
                if (first == null)
                {
                    ShowOverlay(L("FileUnavailable"));
                    return;
                }
                StorageApplicationPermissions.FutureAccessList.AddOrReplace("PlaybackFile", first);
                if (extras.Count > 0)
                    ApplicationData.Current.LocalSettings.Values["PlaylistExtras"] = string.Join("|", extras);
                Frame.Navigate(typeof(MainPage));
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] PlayPaths failed: {0}", ex.Message); }
        }

        private async System.Threading.Tasks.Task PlayPlaylist(Popup popup, string name)
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

                popup.IsOpen = false;
                Frame.Navigate(typeof(MainPage));
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] PlayPlaylist failed: {0}", ex.Message); }
        }

        #endregion

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
            OverviewBtnText.Text = L("Back");
            OverviewBtnGlyph.Text = "\u21A9";

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
                OverviewBtnText.Text = L("Overview");
                OverviewBtnGlyph.Text = "\uD83D\uDDD4";
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
                    if (idx < SnapOffsets.Length)
                        PanoramaScroll.ChangeView(SnapOffsets[idx], null, null, true);
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
    }
}
