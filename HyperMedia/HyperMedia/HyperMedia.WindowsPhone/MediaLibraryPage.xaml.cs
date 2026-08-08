using System;
using System.Collections.Generic;
using System.Diagnostics;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace HyperMedia
{
    /// <summary>
    /// Media-library page (full page replacement for the old Library popup):
    /// pick a folder, list its media files, and show discovered network devices.
    /// </summary>
    public sealed partial class MediaLibraryPage : Page
    {
        private const string KEY_LIBRARY_FOLDER = "LibraryFolderToken";

        private static readonly string[] LibraryExtensions = {
            ".mp4", ".avi", ".mkv", ".webm", ".flv", ".mov", ".wmv", ".3gp", ".ts", ".mpg", ".mpeg", ".m4v",
            ".mp3", ".flac", ".wav", ".aac", ".ogg", ".wma", ".m4a", ".opus",
            ".jpg", ".jpeg", ".png", ".bmp", ".gif"
        };

        private bool _pickerOpen;

        public MediaLibraryPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            LoadLibraryFiles();
            LoadNetworkDevices();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame != null && Frame.CanGoBack) Frame.GoBack();
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            LoadLibraryFiles();
            LoadNetworkDevices();
        }

        private void AddFolderBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_pickerOpen) return;
            _pickerOpen = true;
            try
            {
                var picker = new FolderPicker();
                picker.SuggestedStartLocation = PickerLocationId.VideosLibrary;
                picker.FileTypeFilter.Add("*");

                // WP 8.1: no synchronous folder picker — continuation via App.OnActivated.
                App.PendingFolderOpen = (folder) =>
                {
                    _pickerOpen = false;
                    if (folder != null)
                    {
                        try
                        {
                            string token = StorageApplicationPermissions.FutureAccessList.Add(folder);
                            ApplicationData.Current.LocalSettings.Values[KEY_LIBRARY_FOLDER] = token;
                            PageHintText.Text = "已添加媒体库: " + folder.Name;
                            LoadLibraryFiles();
                        }
                        catch (Exception ex) { Debug.WriteLine("[HyperMedia] Library add failed: {0}", ex.Message); }
                    }
                };
                picker.ContinuationData["picker"] = "folder";
                picker.PickFolderAndContinue();
            }
            catch
            {
                _pickerOpen = false;
                App.PendingFolderOpen = null;
            }
        }

        private async void LoadLibraryFiles()
        {
            FilesList.Items.Clear();
            FilesList.Items.Add(new ListBoxItem { Content = "加载中…", IsEnabled = false, FontSize = 14 });

            var settings = ApplicationData.Current.LocalSettings;
            string token = settings.Values.ContainsKey(KEY_LIBRARY_FOLDER)
                ? settings.Values[KEY_LIBRARY_FOLDER] as string : null;

            if (string.IsNullOrEmpty(token))
            {
                FilesList.Items.Clear();
                FilesList.Items.Add(new ListBoxItem
                {
                    Content = "尚未选择媒体文件夹，点击下方按钮添加",
                    IsEnabled = false,
                    FontSize = 14,
                    Padding = new Thickness(10, 8, 10, 8),
                });
                return;
            }

            try
            {
                var folder = await StorageApplicationPermissions.FutureAccessList.GetFolderAsync(token);
                var files = await folder.GetFilesAsync();
                FilesList.Items.Clear();

                int count = 0;
                foreach (var f in files)
                {
                    string ext = f.FileType.ToLowerInvariant();
                    if (Array.IndexOf(LibraryExtensions, ext) >= 0)
                    {
                        string path = f.Path;
                        var item = new ListBoxItem();
                        item.Content = f.Name;
                        item.Padding = new Thickness(10, 8, 10, 8);
                        item.Margin = new Thickness(0, 2, 0, 2);
                        item.Tapped += async (s, ev) =>
                        {
                            try
                            {
                                var storageFile = await StorageFile.GetFileFromPathAsync(path);
                                StorageApplicationPermissions.FutureAccessList.AddOrReplace("PlaybackFile", storageFile);
                                Frame.Navigate(typeof(MainPage));
                            }
                            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Library open failed: {0}", ex.Message); }
                        };
                        FilesList.Items.Add(item);
                        count++;
                    }
                }

                if (count == 0)
                    FilesList.Items.Add(new ListBoxItem { Content = "此文件夹没有媒体文件", IsEnabled = false, FontSize = 14 });
            }
            catch (Exception ex)
            {
                FilesList.Items.Clear();
                FilesList.Items.Add(new ListBoxItem { Content = "读取媒体库失败: " + ex.Message, IsEnabled = false, FontSize = 14 });
                Debug.WriteLine("[HyperMedia] Library load failed: {0}", ex.Message);
            }
        }

        private async void LoadNetworkDevices()
        {
            NetList.Items.Clear();
            NetList.Items.Add(new ListBoxItem { Content = "扫描中…", IsEnabled = false, FontSize = 14 });

            var devices = await DiscoverNetworkDevices();
            NetList.Items.Clear();

            if (devices.Count == 0)
            {
                NetList.Items.Add(new ListBoxItem { Content = "未发现网络设备", IsEnabled = false, FontSize = 14 });
            }
            else
            {
                foreach (var dev in devices)
                    NetList.Items.Add(new ListBoxItem { Content = dev.Item1, IsEnabled = false, FontSize = 14 });
            }
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
    }
}
