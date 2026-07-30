using System;
using System.Collections.Generic;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
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

            var settings = ApplicationData.Current.LocalSettings;
            if (settings.Values.ContainsKey(KEY_TILE_VIDEOS))
                _videosTileView = (bool)settings.Values[KEY_TILE_VIDEOS];
            if (settings.Values.ContainsKey(KEY_TILE_MUSIC))
                _musicTileView = (bool)settings.Values[KEY_TILE_MUSIC];
            if (settings.Values.ContainsKey(KEY_TILE_PHOTOS))
                _photosTileView = (bool)settings.Values[KEY_TILE_PHOTOS];

            ApplyToggleState();
            LoadRecentItems();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            Window.Current.CoreWindow.PointerEntered -= CoreWindow_PointerEntered;
        }

        private void CoreWindow_PointerEntered(CoreWindow sender, PointerEventArgs args)
        {
            Focus(FocusState.Programmatic);
        }

        #region Recent Items

        private void LoadRecentItems()
        {
            LoadRecentForCategory("Videos", RecentVideosList, RecentVideosTiles);
            LoadRecentForCategory("Music", RecentMusicList, RecentMusicTiles);
            LoadRecentForCategory("Photos", RecentPhotosList, RecentPhotosTiles);
        }

        private void LoadRecentForCategory(string category, ItemsControl listControl, ItemsControl tileControl)
        {
            var items = PlayHistory.GetRecent(category);
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
                // File may have been moved or deleted - clear invalid history
            }
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
            if (e.Key == VirtualKey.O &&
                (Window.Current.CoreWindow.GetKeyState(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0)
            {
                OpenButton_Click(null, null);
                e.Handled = true;
            }
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
                        var file = items[0] as StorageFile;
                        if (file != null)
                        {
                            StorageApplicationPermissions.FutureAccessList.AddOrReplace("PlaybackFile", file);
                            Frame.Navigate(typeof(MainPage));
                        }
                    }
                }
            }
            catch { }
        }

        #endregion
    }
}
