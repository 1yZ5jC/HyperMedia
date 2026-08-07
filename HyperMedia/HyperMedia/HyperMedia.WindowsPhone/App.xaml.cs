using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Phone.UI.Input;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace HyperMedia
{
    public sealed partial class App : Application
    {
        /// <summary>
        /// Raised when the light-theme setting changes (e.g. from the Settings
        /// page) so live pages can re-theme immediately. Mirrors the desktop
        /// build's App.LightThemeChanged.
        /// </summary>
        public static event EventHandler LightThemeChanged;

        public static void NotifyLightThemeChanged()
        {
            try
            {
                var h = LightThemeChanged;
                if (h != null) h(null, EventArgs.Empty);
            }
            catch { }
        }

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

            HardwareButtons.BackPressed += OnBackPressed;

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

        private void OnBackPressed(object sender, BackPressedEventArgs e)
        {
            Frame rootFrame = Window.Current.Content as Frame;
            if (rootFrame != null && rootFrame.CanGoBack)
            {
                e.Handled = true;
                rootFrame.GoBack();
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
