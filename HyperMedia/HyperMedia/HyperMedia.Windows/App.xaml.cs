using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace HyperMedia
{
    public sealed partial class App : Application
    {
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

        public App()
        {
            this.InitializeComponent();
            this.Suspending += this.OnSuspending;
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
#if DEBUG
            if (System.Diagnostics.Debugger.IsAttached)
            {
                this.DebugSettings.EnableFrameRateCounter = true;
            }
#endif

            RegisterSettingsCharm();

            Frame rootFrame = Window.Current.Content as Frame;

            if (rootFrame == null)
            {
                rootFrame = new Frame();
                rootFrame.CacheSize = 1;

                if (e.PreviousExecutionState == ApplicationExecutionState.Terminated)
                {
                }

                Window.Current.Content = rootFrame;
            }

            if (rootFrame.Content == null)
            {
                // Secondary tile activation: playlist:<name> goes straight to playback
                if (e.Arguments != null && e.Arguments.StartsWith("playlist:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!rootFrame.Navigate(typeof(MainPage), e.Arguments))
                        throw new Exception("Failed to create playback page");
                }
                else
                {
                    if (!rootFrame.Navigate(typeof(HomePage), e.Arguments))
                        throw new Exception("Failed to create initial page");
                }
            }
            else if (e.Arguments != null && e.Arguments.StartsWith("playlist:", StringComparison.OrdinalIgnoreCase))
            {
                // App already running — jump to playback
                rootFrame.Navigate(typeof(MainPage), e.Arguments);
            }

            Window.Current.Activate();
        }

        private bool _settingsCharmRegistered = false;

        private void RegisterSettingsCharm()
        {
            try
            {
                if (_settingsCharmRegistered) return;
                _settingsCharmRegistered = true;

                var settingsPane = Windows.UI.ApplicationSettings.SettingsPane.GetForCurrentView();
                settingsPane.CommandsRequested += (s, args) =>
                {
                    try
                    {
                        var about = new Windows.UI.ApplicationSettings.SettingsCommand(
                            "about", L("AboutCharm"),
                            (cmd) => ShowAboutFlyout());
                        args.Request.ApplicationCommands.Add(about);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("[HyperMedia] SettingsCharm failed: {0}", ex.Message);
                    }
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[HyperMedia] RegisterSettingsCharm failed: {0}", ex.Message);
            }
        }

        private void ShowAboutFlyout()
        {
            try
            {
                var flyout = new SettingsFlyout();
                flyout.Title = L("AboutCharm");

                var panel = new StackPanel { Margin = new Windows.UI.Xaml.Thickness(28, 8, 28, 28) };

                var logoRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Windows.UI.Xaml.Thickness(0, 0, 0, 12) };
                var logo = new TextBlock
                {
                    Text = "\u25B6",
                    FontFamily = new Windows.UI.Xaml.Media.FontFamily("Segoe UI Symbol"),
                    FontSize = 18,
                    Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x40, 0xFB)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Windows.UI.Xaml.Thickness(0, 0, 10, 0)
                };
                var name = new TextBlock
                {
                    Text = "HYPERMEDIA",
                    FontSize = 18,
                    CharacterSpacing = 80,
                    Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x33, 0x33, 0x33)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                logoRow.Children.Add(logo);
                logoRow.Children.Add(name);
                panel.Children.Add(logoRow);

                var v = Package.Current.Id.Version;
                panel.Children.Add(MakeAboutLine(L("AboutVersion") + " " + v.Major + "." + v.Minor + "." + v.Build + "." + v.Revision));
                panel.Children.Add(MakeAboutLine(L("PerfLevel") + ": " + PerfLevelName()));
                panel.Children.Add(MakeAboutLine("基于 libVLC 驱动 / Powered by libVLC"));
                panel.Children.Add(MakeAboutLine(L("AboutLicense")));

                flyout.Content = panel;
                flyout.ShowIndependent();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[HyperMedia] ShowAboutFlyout failed: {0}", ex.Message);
            }
        }

        private static TextBlock MakeAboutLine(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 13,
                Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0x33, 0x33, 0x33)),
                Margin = new Windows.UI.Xaml.Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };
        }

        private static string PerfLevelName()
        {
            switch (PerformanceProfile.Level)
            {
                case PerformanceLevel.Low: return L("PerfLow");
                case PerformanceLevel.Medium: return L("PerfMedium");
                default: return L("PerfHigh");
            }
        }

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            deferral.Complete();
        }

        protected override void OnActivated(IActivatedEventArgs args)
        {
            base.OnActivated(args);

            RegisterSettingsCharm();

            if (args.Kind == ActivationKind.File)
            {
                var fileArgs = args as FileActivatedEventArgs;
                if (fileArgs != null && fileArgs.Files.Count > 0)
                {
                    var file = fileArgs.Files[0] as StorageFile;
                    if (file != null)
                    {
                        Frame rootFrame = Window.Current.Content as Frame;
                        if (rootFrame == null)
                        {
                            rootFrame = new Frame();
                            rootFrame.CacheSize = 1;
                            Window.Current.Content = rootFrame;
                        }

                        rootFrame.Navigate(typeof(MainPage), file);
                        Window.Current.Activate();
                    }
                }
            }
            else if (args.Kind == ActivationKind.Protocol)
            {
                var protocolArgs = args as ProtocolActivatedEventArgs;
                if (protocolArgs != null)
                {
                    Frame rootFrame = Window.Current.Content as Frame;
                    if (rootFrame == null)
                    {
                        rootFrame = new Frame();
                        rootFrame.CacheSize = 1;
                        Window.Current.Content = rootFrame;
                    }

                    string target = null;
                    try
                    {
                        var uri = protocolArgs.Uri;
                        string full = uri.ToString();
                        // Strip scheme prefix: hypermedia://
                        int idx = full.IndexOf("://");
                        if (idx >= 0)
                            full = full.Substring(idx + 3);
                        else if (full.StartsWith("hypermedia:", StringComparison.OrdinalIgnoreCase))
                            full = full.Substring("hypermedia:".Length);

                        full = full.TrimStart('/');

                        if (!string.IsNullOrEmpty(full))
                        {
                            if (full.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                full.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                                full.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase) ||
                                full.StartsWith("mms://", StringComparison.OrdinalIgnoreCase))
                            {
                                target = full;
                            }
                            else if (full.StartsWith("play/", StringComparison.OrdinalIgnoreCase))
                            {
                                target = full.Substring(5);
                            }
                            else
                            {
                                // Assume a local file path
                                target = full.Replace('/', '\\');
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("[HyperMedia] Protocol parse failed: {0}", ex.Message);
                    }

                    rootFrame.Navigate(typeof(MainPage), target);
                    Window.Current.Activate();
                }
            }
        }
    }
}
