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
        private const string KEY_SUBTITLE_SIZE = "Settings_SubtitleSize";
        private const string KEY_SUBTITLE_COLOR = "Settings_SubtitleColor";
        private const string KEY_SUBTITLE_MARGIN = "Settings_SubtitleMargin";
        private const string KEY_DEINTERLACE = "Settings_Deinterlace";

        public SettingsPage()
        {
            _isLoading = true;
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

            if (settings.Values.ContainsKey(KEY_AUTO_HIDE_DELAY))
                AutoHideDelaySlider.Value = (int)settings.Values[KEY_AUTO_HIDE_DELAY];

            if (settings.Values.ContainsKey(KEY_SUBTITLE_SIZE))
            {
                int size = (int)settings.Values[KEY_SUBTITLE_SIZE];
                SelectComboBoxItem(SubtitleSizeCombo, size.ToString());
            }

            if (settings.Values.ContainsKey(KEY_SUBTITLE_COLOR))
            {
                string color = settings.Values[KEY_SUBTITLE_COLOR] as string;
                if (!string.IsNullOrEmpty(color))
                    SelectComboBoxItem(SubtitleColorCombo, color);
            }

            if (settings.Values.ContainsKey(KEY_SUBTITLE_MARGIN))
                SubtitleMarginSlider.Value = (int)settings.Values[KEY_SUBTITLE_MARGIN];

            if (settings.Values.ContainsKey(KEY_DEINTERLACE))
            {
                string mode = settings.Values[KEY_DEINTERLACE] as string;
                if (!string.IsNullOrEmpty(mode))
                    SelectComboBoxItem(DeinterlaceCombo, mode);
            }

            DefaultVolumeSlider.ValueChanged += DefaultVolumeSlider_ValueChanged;
            AutoPlayToggle.Toggled += AutoPlayToggle_Toggled;
            ResumeToggle.Toggled += ResumeToggle_Toggled;
            AutoHideToggle.Toggled += AutoHideToggle_Toggled;
            AutoHideDelaySlider.ValueChanged += AutoHideDelaySlider_ValueChanged;
            SubtitleSizeCombo.SelectionChanged += SubtitleSizeCombo_SelectionChanged;
            SubtitleColorCombo.SelectionChanged += SubtitleColorCombo_SelectionChanged;
            _isLoading = false;
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

        private void AutoHideDelaySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (AutoHideDelayText != null)
                AutoHideDelayText.Text = ((int)e.NewValue) + "s";
            SaveSetting(KEY_AUTO_HIDE_DELAY, (int)e.NewValue);
        }

        private bool _isLoading;

        private void SubtitleSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            try
            {
                if (SubtitleSizeCombo.SelectedItem == null) return;
                var item = SubtitleSizeCombo.SelectedItem as ComboBoxItem;
                if (item != null && item.Tag != null)
                    SaveSetting(KEY_SUBTITLE_SIZE, int.Parse(item.Tag.ToString()));
            }
            catch { }
        }

        private void SubtitleColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            try
            {
                if (SubtitleColorCombo.SelectedItem == null) return;
                var item = SubtitleColorCombo.SelectedItem as ComboBoxItem;
                if (item != null && item.Tag != null)
                    SaveSetting(KEY_SUBTITLE_COLOR, item.Tag.ToString());
            }
            catch { }
        }

        private void SubtitleMarginSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            try
            {
                if (SubtitleMarginText != null)
                    SubtitleMarginText.Text = ((int)e.NewValue) + "px";
                SaveSetting(KEY_SUBTITLE_MARGIN, (int)e.NewValue);
            }
            catch { }
        }

        private void DeinterlaceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            try
            {
                if (DeinterlaceCombo.SelectedItem == null) return;
                var item = DeinterlaceCombo.SelectedItem as ComboBoxItem;
                if (item != null && item.Tag != null)
                    SaveSetting(KEY_DEINTERLACE, item.Tag.ToString());
            }
            catch { }
        }

        private void SelectComboBoxItem(ComboBox combo, string tagValue)
        {
            foreach (var item in combo.Items)
            {
                var cbItem = item as ComboBoxItem;
                if (cbItem != null && cbItem.Tag != null && cbItem.Tag.ToString() == tagValue)
                {
                    combo.SelectedItem = cbItem;
                    break;
                }
            }
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

        public static int GetAutoHideDelay()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.ContainsKey(KEY_AUTO_HIDE_DELAY))
                    return (int)settings.Values[KEY_AUTO_HIDE_DELAY];
            }
            catch { }
            return 3;
        }

        public static int GetSubtitleSize()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.ContainsKey(KEY_SUBTITLE_SIZE))
                    return (int)settings.Values[KEY_SUBTITLE_SIZE];
            }
            catch { }
            return 24;
        }

        public static string GetSubtitleColor()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.ContainsKey(KEY_SUBTITLE_COLOR))
                    return settings.Values[KEY_SUBTITLE_COLOR] as string;
            }
            catch { }
            return "#FFFF69B4";
        }

        public static int GetSubtitleMargin()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.ContainsKey(KEY_SUBTITLE_MARGIN))
                    return (int)settings.Values[KEY_SUBTITLE_MARGIN];
            }
            catch { }
            return 0;
        }

        public static string GetDeinterlaceMode()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.ContainsKey(KEY_DEINTERLACE))
                    return settings.Values[KEY_DEINTERLACE] as string;
            }
            catch { }
            return "auto";
        }
    }
}
