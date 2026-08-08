using System;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

namespace HyperMedia
{
    /// <summary>
    /// Open network media page (full page replacement for the old URL popup):
    /// enter a URL to stream, with a quick-pick history list.
    /// </summary>
    public sealed partial class OpenUrlPage : Page
    {
        public OpenUrlPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            UrlHistoryList.Items.Clear();

            var history = PlayHistory.GetUrlHistory();
            foreach (var url in history)
            {
                var item = new ListBoxItem();
                item.Content = url;
                item.Padding = new Thickness(10, 8, 10, 8);
                item.Tapped += (s, ev) => { UrlBox.Text = url; };
                UrlHistoryList.Items.Add(item);
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame != null && Frame.CanGoBack) Frame.GoBack();
        }

        private void UrlBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                LaunchUrl(UrlBox.Text.Trim());
                e.Handled = true;
            }
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            LaunchUrl(UrlBox.Text.Trim());
        }

        private void PlayBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Frame != null && Frame.CanGoBack) Frame.GoBack();
        }

        private void LaunchUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            PlayHistory.AddUrl(url);
            Frame.Navigate(typeof(MainPage), url);
        }
    }
}
