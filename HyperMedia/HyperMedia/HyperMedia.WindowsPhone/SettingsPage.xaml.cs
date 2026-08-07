using System;
using System.Diagnostics;
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
        private const string KEY_SLEEP_TIMER = "Settings_SleepTimer";
        private const string KEY_SUBTITLE_OUTLINE = "Settings_SubtitleOutline";
        private const string KEY_LOUDNESS = "Settings_Loudness";
        private const string KEY_EPISODE = "Settings_Episode";
        private const string KEY_INTRO_SKIP = "Settings_IntroSkip";
        private const string KEY_LIGHT_THEME = "Settings_LightTheme";
        private const string KEY_LANGUAGE = "Settings_Language";
        private const string KEY_LYRIC_SOURCE = "Settings_LyricSource";

        public SettingsPage()
        {
            _isLoading = true;
            this.InitializeComponent();
            LoadSettings();
            ApplyCurrentLanguage();
            this.Loaded += (s, e) => ApplyCurrentLanguage();
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

        private void ApplyCurrentLanguage()
        {
            try
            {
                var appText = Application.Current.Resources["AppText"] as AppText;
                if (appText != null)
                    appText.ApplyLanguageTo(this);
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] ApplyCurrentLanguage failed: {0}", ex.Message); }
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

            if (settings.Values.ContainsKey(KEY_SUBTITLE_OUTLINE))
            {
                string outline = settings.Values[KEY_SUBTITLE_OUTLINE] as string;
                if (!string.IsNullOrEmpty(outline))
                    SelectComboBoxItem(SubtitleOutlineCombo, outline);
            }

            if (settings.Values.ContainsKey(KEY_SUBTITLE_MARGIN))
                SubtitleMarginSlider.Value = (int)settings.Values[KEY_SUBTITLE_MARGIN];

            if (settings.Values.ContainsKey(KEY_DEINTERLACE))
            {
                string mode = settings.Values[KEY_DEINTERLACE] as string;
                if (!string.IsNullOrEmpty(mode))
                    SelectComboBoxItem(DeinterlaceCombo, mode);
            }

            if (settings.Values.ContainsKey(KEY_SLEEP_TIMER))
            {
                SleepTimerSlider.Value = (int)settings.Values[KEY_SLEEP_TIMER];
                SleepTimerText.Text = ((int)settings.Values[KEY_SLEEP_TIMER]) + " 分钟";
            }

            if (settings.Values.ContainsKey(KEY_LOUDNESS))
                LoudnessToggle.IsOn = (bool)settings.Values[KEY_LOUDNESS];

            if (settings.Values.ContainsKey(KEY_EPISODE))
                EpisodeToggle.IsOn = (bool)settings.Values[KEY_EPISODE];

            if (settings.Values.ContainsKey(KEY_INTRO_SKIP))
                IntroSkipToggle.IsOn = (bool)settings.Values[KEY_INTRO_SKIP];

            if (settings.Values.ContainsKey(KEY_LIGHT_THEME))
                LightThemeToggle.IsOn = (bool)settings.Values[KEY_LIGHT_THEME];

            if (settings.Values.ContainsKey(KEY_LYRIC_SOURCE))
            {
                string source = settings.Values[KEY_LYRIC_SOURCE] as string;
                if (!string.IsNullOrEmpty(source))
                    SelectComboBoxItem(LyricSourceCombo, source);
            }

            // Restore language selection (without triggering reload loop)
            _isLoading = true;
            if (settings.Values.ContainsKey(KEY_LANGUAGE))
            {
                string lang = settings.Values[KEY_LANGUAGE] as string;
                foreach (var obj in LanguageCombo.Items)
                {
                    var it = obj as ComboBoxItem;
                    if (it != null && it.Tag != null && it.Tag.ToString() == lang)
                    {
                        LanguageCombo.SelectedItem = it;
                        break;
                    }
                }
            }
            _isLoading = false;

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
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
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
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
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
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
        }

        private void SubtitleOutlineCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            try
            {
                if (SubtitleOutlineCombo.SelectedItem == null) return;
                var item = SubtitleOutlineCombo.SelectedItem as ComboBoxItem;
                if (item != null && item.Tag != null)
                    SaveSetting(KEY_SUBTITLE_OUTLINE, item.Tag.ToString());
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] SubtitleOutline failed: {0}", ex.Message); }
        }

        public static int GetSubtitleOutline()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.ContainsKey(KEY_SUBTITLE_OUTLINE))
                {
                    string v = settings.Values[KEY_SUBTITLE_OUTLINE] as string;
                    int r;
                    if (int.TryParse(v, out r)) return r;
                }
            }
            catch { }
            return 0;
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
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
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
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
        }

        private void SleepTimerSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            try
            {
                if (SleepTimerText != null)
                    SleepTimerText.Text = ((int)e.NewValue) + " 分钟";
                SaveSetting(KEY_SLEEP_TIMER, (int)e.NewValue);
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] SleepTimerSlider failed: {0}", ex.Message); }
        }

        public static int GetSleepTimer()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.ContainsKey(KEY_SLEEP_TIMER))
                    return (int)settings.Values[KEY_SLEEP_TIMER];
            }
            catch { }
            return 0;
        }

        private void LoudnessToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            try { SaveSetting(KEY_LOUDNESS, LoudnessToggle.IsOn); }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] LoudnessToggle failed: {0}", ex.Message); }
        }

        public static bool GetLoudnessEnabled()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.ContainsKey(KEY_LOUDNESS))
                    return (bool)settings.Values[KEY_LOUDNESS];
            }
            catch { }
            return false;
        }

        private void EpisodeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            try { SaveSetting(KEY_EPISODE, EpisodeToggle.IsOn); }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] EpisodeToggle failed: {0}", ex.Message); }
        }

        public static bool GetEpisodeAutoPlay()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.ContainsKey(KEY_EPISODE))
                    return (bool)settings.Values[KEY_EPISODE];
            }
            catch { }
            return true;
        }

        private void IntroSkipToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            try { SaveSetting(KEY_INTRO_SKIP, IntroSkipToggle.IsOn); }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] IntroSkipToggle failed: {0}", ex.Message); }
        }

        public static bool GetIntroSkipEnabled()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.ContainsKey(KEY_INTRO_SKIP))
                    return (bool)settings.Values[KEY_INTRO_SKIP];
            }
            catch { }
            return true;
        }

        private void LightThemeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            try
            {
                SaveSetting(KEY_LIGHT_THEME, LightThemeToggle.IsOn);
                App.NotifyLightThemeChanged();
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] LightThemeToggle failed: {0}", ex.Message); }
        }

        public static bool GetLightTheme()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.ContainsKey(KEY_LIGHT_THEME))
                    return (bool)settings.Values[KEY_LIGHT_THEME];
            }
            catch { }
            return false;
        }

        private void LyricSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            try
            {
                if (LyricSourceCombo.SelectedItem == null) return;
                var item = LyricSourceCombo.SelectedItem as ComboBoxItem;
                if (item != null && item.Tag != null)
                    SaveSetting(KEY_LYRIC_SOURCE, item.Tag.ToString());
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] LyricSourceCombo failed: {0}", ex.Message); }
        }

        public static string GetLyricSource()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.ContainsKey(KEY_LYRIC_SOURCE))
                {
                    string v = settings.Values[KEY_LYRIC_SOURCE] as string;
                    if (!string.IsNullOrEmpty(v)) return v;
                }
            }
            catch { }
            return "auto";
        }

        private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            try
            {
                var item = LanguageCombo.SelectedItem as ComboBoxItem;
                if (item == null || item.Tag == null) return;
                string lang = item.Tag.ToString();
                SaveSetting(KEY_LANGUAGE, lang);

                var appText = Application.Current.Resources["AppText"] as AppText;
                if (appText != null)
                {
                    appText.Language = lang;
                    appText.ApplyLanguageTo(this);
                }
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] LanguageCombo failed: {0}", ex.Message); }
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

        private async void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Windows.UI.Popups.MessageDialog(L("ClearHistoryConfirm"), L("ClearHistoryTitle"));
            dialog.Commands.Add(new Windows.UI.Popups.UICommand(L("ClearBtn"), (cmd) => { PlayHistory.ClearAll(); }));
            dialog.Commands.Add(new Windows.UI.Popups.UICommand(L("Cancel")));
            dialog.DefaultCommandIndex = 1;
            dialog.CancelCommandIndex = 1;
            await dialog.ShowAsync();
        }

        private async void ClearResume_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Windows.UI.Popups.MessageDialog(L("ClearResumeConfirm"), L("ClearResumePositions"));
            dialog.Commands.Add(new Windows.UI.Popups.UICommand(L("ClearBtn"), (cmd) =>
            {
                try
                {
                    var settings = ApplicationData.Current.LocalSettings;
                    var keys = new System.Collections.Generic.List<string>();
                    foreach (var key in settings.Values.Keys)
                    {
                        string k = key != null ? key.ToString() : "";
                        if (k.StartsWith("ResumePosition_") || k.StartsWith("ResumePercent_"))
                            keys.Add(k);
                    }
                    foreach (var key in keys)
                        settings.Values.Remove(key);
                }
                catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
            }));
            dialog.Commands.Add(new Windows.UI.Popups.UICommand(L("Cancel")));
            dialog.DefaultCommandIndex = 1;
            dialog.CancelCommandIndex = 1;
            await dialog.ShowAsync();
        }

        public static int GetDefaultVolume()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.ContainsKey(KEY_DEFAULT_VOLUME))
                    return (int)settings.Values[KEY_DEFAULT_VOLUME];
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
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
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
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
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
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
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
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
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
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
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
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
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
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
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
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
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
            return "auto";
        }
    }
}
