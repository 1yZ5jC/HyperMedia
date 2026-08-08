using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

namespace HyperMedia
{
    /// <summary>
    /// Playlists page (full page replacement for the old Playlists popup):
    /// smart playlists plus user-defined playlists. Tap to play, long press for menu.
    /// </summary>
    public sealed partial class PlaylistsPage : Page
    {
        public PlaylistsPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            LoadPlaylists();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame != null && Frame.CanGoBack) Frame.GoBack();
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            LoadPlaylists();
        }

        private void SaveCurrentBtn_Click(object sender, RoutedEventArgs e)
        {
            PageHintText.Text = "请在播放页点击\"保存歌单\"来保存当前列表";
        }

        private void LoadPlaylists()
        {
            SmartList.Items.Clear();
            PlaylistList.Items.Clear();

            AddSmartItem("toprated");
            AddSmartItem("mostplayed");
            AddSmartItem("recent");

            var names = PlaylistLibrary.GetPlaylistNames();
            if (names.Count == 0)
            {
                PlaylistList.Items.Add(new ListBoxItem
                {
                    Content = "还没有歌单，在播放页点击\"保存歌单\"即可创建",
                    IsEnabled = false,
                    FontSize = 14,
                    Margin = new Thickness(0, 14, 0, 0),
                });
                return;
            }

            foreach (var name in names)
            {
                var files = PlaylistLibrary.GetPlaylistFiles(name);
                var item = new ListBoxItem();
                item.Padding = new Thickness(10, 10, 10, 10);
                item.Margin = new Thickness(0, 2, 0, 2);
                item.Content = "▶ " + name + "  (" + files.Count + " 首)";
                item.Tapped += async (s, ev) => { await PlayPlaylist(name); };
                item.Holding += async (s, ev) =>
                {
                    ev.Handled = true;
                    await ShowPlaylistMenu(item, name);
                };
                PlaylistList.Items.Add(item);
            }
        }

        private void AddSmartItem(string kind)
        {
            string label = kind == "toprated" ? "最高评分"
                         : kind == "mostplayed" ? "播放最多"
                         : "最近播放";

            var item = new ListBoxItem();
            item.Content = label;
            item.Padding = new Thickness(10, 8, 10, 8);
            item.Tapped += async (s, ev) =>
            {
                var paths = PlaylistLibrary.GetSmartPlaylist(kind);
                if (paths == null || paths.Count == 0)
                {
                    PageHintText.Text = "该智能歌单暂无内容";
                    return;
                }
                await PlayPaths(paths);
            };
            SmartList.Items.Add(item);
        }

        private async Task ShowPlaylistMenu(FrameworkElement anchor, string name)
        {
            var menu = new Windows.UI.Popups.PopupMenu();
            menu.Commands.Add(new Windows.UI.Popups.UICommand("播放", async (cmd) => { await PlayPlaylist(name); }));
            menu.Commands.Add(new Windows.UI.Popups.UICommand("固定到开始屏幕", async (cmd) => { await PinToStart(name); }));
            menu.Commands.Add(new Windows.UI.Popups.UICommand("删除歌单", (cmd) =>
            {
                PlaylistLibrary.DeletePlaylist(name);
                PageHintText.Text = "已删除: " + name;
                LoadPlaylists();
            }));

            try
            {
                var pos = anchor.TransformToVisual(null).TransformPoint(new Point(0, 0));
                var rect = new Rect(pos, new Size(anchor.ActualWidth, anchor.ActualHeight));
                await menu.ShowForSelectionAsync(rect, Windows.UI.Popups.Placement.Above);
            }
            catch (Exception ex) { DebugWrite("Playlist menu failed: {0}", ex.Message); }
        }

        private async Task PinToStart(string name)
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
                    PageHintText.Text = "已固定到开始屏幕: " + name;
            }
            catch (Exception ex) { DebugWrite("Pin tile failed: {0}", ex.Message); }
        }

        private async Task PlayPaths(List<string> paths)
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
                    catch (Exception ex) { DebugWrite("Path missing: {0}: {1}", paths[i], ex.Message); }
                }
                if (first == null)
                {
                    PageHintText.Text = "文件不可用";
                    return;
                }
                StorageApplicationPermissions.FutureAccessList.AddOrReplace("PlaybackFile", first);
                if (extras.Count > 0)
                    ApplicationData.Current.LocalSettings.Values["PlaylistExtras"] = string.Join("|", extras);
                Frame.Navigate(typeof(MainPage));
            }
            catch (Exception ex) { DebugWrite("PlayPaths failed: {0}", ex.Message); }
        }

        private async Task PlayPlaylist(string name)
        {
            var files = PlaylistLibrary.GetPlaylistFiles(name);
            if (files == null || files.Count == 0)
            {
                PageHintText.Text = "歌单为空";
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
                    catch (Exception ex) { DebugWrite("Playlist file missing: {0}: {1}", files[i], ex.Message); }
                }

                if (first == null)
                {
                    PageHintText.Text = "歌单不可用";
                    return;
                }

                StorageApplicationPermissions.FutureAccessList.AddOrReplace("PlaybackFile", first);
                if (extras.Count > 0)
                    ApplicationData.Current.LocalSettings.Values["PlaylistExtras"] = string.Join("|", extras);

                Frame.Navigate(typeof(MainPage));
            }
            catch (Exception ex) { DebugWrite("PlayPlaylist failed: {0}", ex.Message); }
        }

        private static void DebugWrite(string format, params object[] args)
        {
            System.Diagnostics.Debug.WriteLine("[HyperMedia] " + string.Format(format, args));
        }
    }
}