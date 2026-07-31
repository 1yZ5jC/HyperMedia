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
            StatusText.Text = "支持几乎所有媒体格式";
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

        private void PanoramaScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (e.IsIntermediate)
            {
                _snapPending = true;
                return;
            }
            if (_snapPending)
            {
                _snapPending = false;
                SnapToNearest();
            }
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
                menu.Commands.Add(new Windows.UI.Popups.UICommand("从历史记录删除", (cmd) =>
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
            var dialog = new Windows.UI.Popups.MessageDialog("确定要清除所有播放历史吗？此操作不可撤销。", "清除播放历史");
            dialog.Commands.Add(new Windows.UI.Popups.UICommand("清除", (cmd) =>
            {
                PlayHistory.ClearAll();
                LoadRecentItems();
            }));
            dialog.Commands.Add(new Windows.UI.Popups.UICommand("取消"));
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
            popup.Height = 420;

            var border = new Border();
            border.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xF0, 0x0A, 0x0A, 0x0F));
            border.Width = 520;
            border.Padding = new Thickness(24);

            var panel = new StackPanel();

            var title = new TextBlock();
            title.Text = "媒体库";
            title.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
            title.FontSize = 16;
            title.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x40, 0xFB));
            title.Margin = new Thickness(0, 0, 0, 8);
            panel.Children.Add(title);

            var addBtn = new Button();
            addBtn.Content = "+ 添加文件夹";
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
                        empty.Content = "文件夹中没有媒体文件";
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
                empty.Content = "尚未添加文件夹 — 点击上方按钮选择";
                empty.IsEnabled = false;
                empty.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
                empty.FontSize = 13;
                empty.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF));
                empty.Padding = new Thickness(10, 8, 10, 8);
                filesList.Items.Add(empty);
            }

            var closeBtn = new Button();
            closeBtn.Content = "关闭";
            closeBtn.HorizontalAlignment = HorizontalAlignment.Right;
            closeBtn.Margin = new Thickness(0, 12, 0, 0);
            closeBtn.Click += (s, ev) => { popup.IsOpen = false; };
            panel.Children.Add(closeBtn);

            border.Child = panel;
            popup.Child = border;

            var bounds = Window.Current.Bounds;
            popup.HorizontalOffset = (bounds.Width - 520) / 2;
            popup.VerticalOffset = (bounds.Height - 420) / 2;

            popup.IsOpen = true;
        }

        private void ShowOverlay(string text)
        {
            StatusText.Text = text;
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
            title.Text = "打开网络媒体";
            title.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
            title.FontSize = 14;
            title.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x40, 0xFB));
            title.Margin = new Thickness(0, 0, 0, 16);
            panel.Children.Add(title);

            var textBox = new TextBox();
            textBox.PlaceholderText = "http://example.com/video.mp4 或 rtsp://...";
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
            title.Text = "我的歌单";
            title.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
            title.FontSize = 16;
            title.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x40, 0xFB));
            title.Margin = new Thickness(0, 0, 0, 4);
            panel.Children.Add(title);

            var hint = new TextBlock();
            hint.Text = "在播放器的播放列表中点击 💾 即可将当前列表保存为歌单";
            hint.FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI");
            hint.FontSize = 11;
            hint.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
            hint.Margin = new Thickness(0, 0, 0, 12);
            hint.TextWrapping = TextWrapping.Wrap;
            panel.Children.Add(hint);

            var names = PlaylistLibrary.GetPlaylistNames();
            if (names.Count == 0)
            {
                var empty = new TextBlock();
                empty.Text = "暂无歌单 — 在播放器播放列表点 💾 保存";
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
                        menu.Commands.Add(new Windows.UI.Popups.UICommand("播放", async (cmd) => { await PlayPlaylist(popup, name); }));
                        menu.Commands.Add(new Windows.UI.Popups.UICommand("固定到开始屏幕", async (cmd) =>
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
                                    ShowOverlay("已固定到开始屏幕: " + name);
                            }
                            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Pin tile failed: {0}", ex.Message); }
                        }));
                        menu.Commands.Add(new Windows.UI.Popups.UICommand("删除歌单", (cmd) =>
                        {
                            PlaylistLibrary.DeletePlaylist(name);
                            popup.IsOpen = false;
                            ShowOverlay("歌单已删除: " + name);
                        }));
                        await menu.ShowForSelectionAsync(new Rect(ev.GetPosition(null), new Size(1, 1)), Windows.UI.Popups.Placement.Above);
                    };
                    playlistList.Items.Add(item);
                }
            }

            var closeBtn = new Button();
            closeBtn.Content = "关闭";
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

        private async System.Threading.Tasks.Task PlayPlaylist(Popup popup, string name)
        {
            var files = PlaylistLibrary.GetPlaylistFiles(name);
            if (files == null || files.Count == 0)
            {
                ShowOverlay("歌单为空");
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
                    ShowOverlay("歌单文件不可用（可能已被移动）");
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
            _overviewVisible = !_overviewVisible;
            if (_overviewVisible)
            {
                BuildOverviewItems();
                OverviewView.Visibility = Visibility.Visible;
                PanoramaScroll.Visibility = Visibility.Collapsed;
                OverviewBtnText.Text = "返回";
                OverviewBtnGlyph.Text = "\u21A9";
            }
            else
            {
                OverviewView.Visibility = Visibility.Collapsed;
                PanoramaScroll.Visibility = Visibility.Visible;
                OverviewBtnText.Text = "概览";
                OverviewBtnGlyph.Text = "\uD83D\uDDD4";
            }
        }

        private void BuildOverviewItems()
        {
            var items = new System.Collections.ObjectModel.ObservableCollection<OverviewItem>();

            int vids = PlayHistory.GetRecent("Videos").Count;
            int music = PlayHistory.GetRecent("Music").Count;
            int photos = PlayHistory.GetRecent("Photos").Count;
            int libs = PlaylistLibrary.GetPlaylistNames().Count;

            items.Add(new OverviewItem
            {
                Glyph = "\uD83C\uDFAC",
                Title = "视频",
                Subtitle = vids > 0 ? "最近播放 " + vids + " 项" : "打开视频文件",
                TileBrush = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0xE0, 0x40, 0xFB)),
                Action = "category:1"
            });
            items.Add(new OverviewItem
            {
                Glyph = "\uD83C\uDFB5",
                Title = "音乐",
                Subtitle = music > 0 ? "最近播放 " + music + " 项" : "打开音乐文件",
                TileBrush = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0x00, 0xBC, 0xD4)),
                Action = "category:2"
            });
            items.Add(new OverviewItem
            {
                Glyph = "\uD83D\uDCF7",
                Title = "图片",
                Subtitle = photos > 0 ? "最近播放 " + photos + " 项" : "打开图片文件",
                TileBrush = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0x76, 0xFF, 0x00)),
                Action = "category:3"
            });

            var names = PlaylistLibrary.GetPlaylistNames();
            if (names.Count == 0)
            {
                items.Add(new OverviewItem
                {
                    Glyph = "\uD83C\uDFB6",
                    Title = "歌单",
                    Subtitle = "在播放器中将列表保存为歌单",
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
                        Subtitle = cnt + " 首 · 点击播放",
                        TileBrush = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0x88, 0x88, 0xFF)),
                        Action = "playlist:" + name
                    });
                }
            }

            items.Add(new OverviewItem
            {
                Glyph = "\uD83D\uDCC1",
                Title = "媒体库",
                Subtitle = libs > 0 ? "浏览文件夹媒体" : "添加文件夹浏览",
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
                ShowOverlay("歌单为空");
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
                    ShowOverlay("歌单文件不可用（可能已被移动）");
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
