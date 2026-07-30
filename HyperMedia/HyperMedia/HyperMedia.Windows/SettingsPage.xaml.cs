using System;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Navigation;

namespace HyperMedia
{
    public sealed partial class SettingsPage : Page
    {
        private const string KEY_DEFAULT_VOLUME = "Settings_DefaultVolume";
        private const string KEY_AUTO_PLAY = "Settings_AutoPlay";
        private const string KEY_RESUME = "Settings_Resume";
        private const string KEY_AUTO_HIDE = "Settings_AutoHide";
        private const string KEY_AUTO_HIDE_DELAY = "Settings_AutoHideDelay";

        public SettingsPage()
        {
            this.InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            var settings = ApplicationData.Current.LocalSettings;

            if (settings.Values.ContainsKey(KEY_DEFAULT_VOLUME))
                DefaultVolumeSlider.Value = (int)settings.Values[KEY_DEFAULT_VOLUME];

            if (settings.Values.ContainsKey(KEY_AUTO_PLAY))
                AutoPlayToggle.IsOn = (bool)settings.Values[KEY_AUTO_PLAY];

            if (settings.Values.ContainsKey(KEY_RESUME))
                ResumeToggle.IsOn = (bool)settings.Values[KEY_RESUME];

            if (settings.Values.ContainsKey(KEY_AUTO_HIDE))
                AutoHideToggle.IsOn = (bool)settings.Values[KEY_AUTO_HIDE];

            DefaultVolumeSlider.ValueChanged += DefaultVolumeSlider_ValueChanged;
            AutoPlayToggle.Toggled += AutoPlayToggle_Toggled;
            ResumeToggle.Toggled += ResumeToggle_Toggled;
            AutoHideToggle.Toggled += AutoHideToggle_Toggled;
        }

        private void SaveSetting(string key, object value)
        {
            try
            {
                ApplicationData.Current.LocalSettings.Values[key] = value;
            }
            catch { }
        }

        private void DefaultVolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            SaveSetting(KEY_DEFAULT_VOLUME, (int)e.NewValue);
        }

        private void AutoPlayToggle_Toggled(object sender, RoutedEventArgs e)
        {
            SaveSetting(KEY_AUTO_PLAY, AutoPlayToggle.IsOn);
        }

        private void ResumeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            SaveSetting(KEY_RESUME, ResumeToggle.IsOn);
        }

        private void AutoHideToggle_Toggled(object sender, RoutedEventArgs e)
        {
            SaveSetting(KEY_AUTO_HIDE, AutoHideToggle.IsOn);
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
                Frame.GoBack();
        }

        private void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            PlayHistory.ClearAll();
        }

        private void ClearResume_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                var keys = new System.Collections.Generic.List<string>();
                foreach (var key in settings.Values.Keys)
                {
                    if (key != null && key.ToString().StartsWith("ResumePosition_"))
                        keys.Add(key.ToString());
                }
                foreach (var key in keys)
                    settings.Values.Remove(key);
            }
            catch { }
        }

        public static int GetDefaultVolume()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.ContainsKey(KEY_DEFAULT_VOLUME))
                    return (int)settings.Values[KEY_DEFAULT_VOLUME];
            }
            catch { }
            return 100;
        }

        public static bool GetAutoPlay()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.ContainsKey(KEY_AUTO_PLAY))
                    return (bool)settings.Values[KEY_AUTO_PLAY];
            }
            catch { }
            return true;
        }

        public static bool GetResumeEnabled()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.ContainsKey(KEY_RESUME))
                    return (bool)settings.Values[KEY_RESUME];
            }
            catch { }
            return true;
        }

        public static bool GetAutoHideEnabled()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.ContainsKey(KEY_AUTO_HIDE))
                    return (bool)settings.Values[KEY_AUTO_HIDE];
            }
            catch { }
            return true;
        }
    }
}
