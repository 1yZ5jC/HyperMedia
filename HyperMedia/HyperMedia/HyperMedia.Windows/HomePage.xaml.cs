using System;
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

        public HomePage()
        {
            this.InitializeComponent();
            Window.Current.CoreWindow.PointerEntered += CoreWindow_PointerEntered;
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

        #region Navigation

        private void NavVideoBtn_Click(object sender, RoutedEventArgs e)
        {
            OpenFilesWithFilter(VIDEO_FILTER, PickerLocationId.VideosLibrary);
        }

        private void NavMusicBtn_Click(object sender, RoutedEventArgs e)
        {
            OpenFilesWithFilter(MUSIC_FILTER, PickerLocationId.MusicLibrary);
        }

        private void NavPhotosBtn_Click(object sender, RoutedEventArgs e)
        {
            OpenFilesWithFilter(PHOTO_FILTER, PickerLocationId.PicturesLibrary);
        }

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

        private void Card_PointerEntered(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var fe = sender as Windows.UI.Xaml.FrameworkElement;
            if (fe != null) fe.Opacity = 0.85;
        }

        private void Card_PointerExited(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var fe = sender as Windows.UI.Xaml.FrameworkElement;
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

            StorageFile file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                StorageApplicationPermissions.FutureAccessList.AddOrReplace("PlaybackFile", file);
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

            StorageFile file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                StorageApplicationPermissions.FutureAccessList.AddOrReplace("PlaybackFile", file);
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
