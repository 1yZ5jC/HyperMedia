using System;
using System.Diagnostics;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

namespace HyperMedia
{
    /// <summary>
    /// Compact, charm-panel specific settings layout used inside the Settings Flyout.
    /// Reads/writes the same LocalSettings keys as SettingsPage (full-screen layout).
    /// </summary>
    public sealed partial class CharmSettingsControl : UserControl
    {
        private const string KEY_DEFAULT_VOLUME = "Settings_DefaultVolume";
        private const string KEY_AUTO_PLAY = "Settings_AutoPlay";
        private const string KEY_RESUME = "Settings_Resume";
        private const string KEY_AUTO_HIDE = "Settings_AutoHide";
        private const string KEY_AUTO_HIDE_DELAY = "Settings_AutoHideDelay";
        private const string KEY_SUBTITLE_SIZE = "Settings_SubtitleSize";
        private const string KEY_SUBTITLE_COLOR = "Settings_SubtitleColor";
        private const string KEY_SUBTITLE_MARGIN = "Settings_SubtitleMargin";
        private const string KEY_SUBTITLE_OUTLINE = "Settings_SubtitleOutline";
        private const string KEY_DEINTERLACE = "Settings_Deinterlace";
        private const string KEY_SLEEP_TIMER = "Settings_SleepTimer";
        private const string KEY_LOUDNESS = "Settings_Loudness";
        private const string KEY_EPISODE = "Settings_Episode";
        private const string KEY_INTRO_SKIP = "Settings_IntroSkip";
        private const string KEY_LIGHT_THEME = "Settings_LightTheme";
        private const string KEY_LANGUAGE = "Settings_Language";
        private const string KEY_LYRIC_SOURCE = "Settings_LyricSource";

        private bool _isLoading = true;

        public CharmSettingsControl()
        {
            this.InitializeComponent();
            LoadSettings();
            ApplyCurrentLanguage();
            this.Loaded += (s, e) => ApplyCurrentLanguage();
            UpdatePerfLevelText();
        }

        private void UpdatePerfLevelText()
        {
            try
            {
                if (PerfLevelText != null)
                {
                    string levelName = PerformanceProfile.Level == PerformanceLevel.Low ? L("PerfLow")
                        : PerformanceProfile.Level == PerformanceLevel.Medium ? L("PerfMedium")
                        : L("PerfHigh");
                    PerfLevelText.Text = L("PerfLevel") + ": " + levelName;
                }
                if (HardDecodeText != null)
                {
                    int grade = PerformanceProfile.HardwareDecodeGrade;
                    string h264 = grade >= 1 ? L("HardH264Yes") : L("HardH264No");
                    string h265 = grade >= 3 ? L("HardHevc10")
                        : grade >= 2 ? L("HardHevc8") : L("HardHevcNone");
                    HardDecodeText.Text = L("PerfHardDecode") + ": " + h264 + " / " + h265;
                }
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

        private void ApplyCurrentLanguage()
        {
            try
            {
                var appText = Application.Current.Resources["AppText"] as AppText;
                if (appText != null)
                    appText.ApplyLanguageTo(this);
            }
            catch { }
        }

        private void LoadSettings()
        {
            _isLoading = true;
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
            {
                AutoHideDelaySlider.Value = (int)settings.Values[KEY_AUTO_HIDE_DELAY];
                AutoHideDelayText.Text = ((int)settings.Values[KEY_AUTO_HIDE_DELAY]) + "s";
            }

            if (settings.Values.ContainsKey(KEY_SUBTITLE_SIZE))
                SelectComboBoxItem(SubtitleSizeCombo, ((int)settings.Values[KEY_SUBTITLE_SIZE]).ToString());

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
            {
                SubtitleMarginSlider.Value = (int)settings.Values[KEY_SUBTITLE_MARGIN];
                SubtitleMarginText.Text = ((int)settings.Values[KEY_SUBTITLE_MARGIN]) + "px";
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
        }

        private void SaveSetting(string key, object value)
        {
            try
            {
                ApplicationData.Current.LocalSettings.Values[key] = value;
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Charm Caught: " + ex.Message); }
        }

        private void DefaultVolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            SaveSetting(KEY_DEFAULT_VOLUME, (int)e.NewValue);
        }

        private void AutoPlayToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            SaveSetting(KEY_AUTO_PLAY, AutoPlayToggle.IsOn);
        }

        private void ResumeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            SaveSetting(KEY_RESUME, ResumeToggle.IsOn);
        }

        private void AutoHideToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            SaveSetting(KEY_AUTO_HIDE, AutoHideToggle.IsOn);
        }

        private void AutoHideDelaySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            if (AutoHideDelayText != null)
                AutoHideDelayText.Text = ((int)e.NewValue) + "s";
            SaveSetting(KEY_AUTO_HIDE_DELAY, (int)e.NewValue);
        }

        private void SubtitleSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            var item = SubtitleSizeCombo.SelectedItem as ComboBoxItem;
            if (item != null && item.Tag != null)
                SaveSetting(KEY_SUBTITLE_SIZE, int.Parse(item.Tag.ToString()));
        }

        private void SubtitleColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            var item = SubtitleColorCombo.SelectedItem as ComboBoxItem;
            if (item != null && item.Tag != null)
                SaveSetting(KEY_SUBTITLE_COLOR, item.Tag.ToString());
        }

        private void SubtitleOutlineCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            var item = SubtitleOutlineCombo.SelectedItem as ComboBoxItem;
            if (item != null && item.Tag != null)
                SaveSetting(KEY_SUBTITLE_OUTLINE, item.Tag.ToString());
        }

        private void SubtitleMarginSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            if (SubtitleMarginText != null)
                SubtitleMarginText.Text = ((int)e.NewValue) + "px";
            SaveSetting(KEY_SUBTITLE_MARGIN, (int)e.NewValue);
        }

        private void DeinterlaceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            var item = DeinterlaceCombo.SelectedItem as ComboBoxItem;
            if (item != null && item.Tag != null)
                SaveSetting(KEY_DEINTERLACE, item.Tag.ToString());
        }

        private void SleepTimerSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            if (SleepTimerText != null)
                SleepTimerText.Text = ((int)e.NewValue) + " 分钟";
            SaveSetting(KEY_SLEEP_TIMER, (int)e.NewValue);
        }

        private void LoudnessToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            SaveSetting(KEY_LOUDNESS, LoudnessToggle.IsOn);
        }

        private void EpisodeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            SaveSetting(KEY_EPISODE, EpisodeToggle.IsOn);
        }

        private void IntroSkipToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            SaveSetting(KEY_INTRO_SKIP, IntroSkipToggle.IsOn);
        }

        private void LightThemeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            SaveSetting(KEY_LIGHT_THEME, LightThemeToggle.IsOn);
            App.NotifyLightThemeChanged();
        }

        private void LyricSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            var item = LyricSourceCombo.SelectedItem as ComboBoxItem;
            if (item != null && item.Tag != null)
                SaveSetting(KEY_LYRIC_SOURCE, item.Tag.ToString());
        }

        private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
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
                        if (k.StartsWith("ResumePosition_") || k.StartsWith("ResumePercent_") || k.StartsWith("SkipIntro_"))
                            keys.Add(k);
                    }
                    foreach (var key in keys)
                        settings.Values.Remove(key);
                }
                catch (Exception ex) { Debug.WriteLine("[HyperMedia] Charm Caught: " + ex.Message); }
            }));
            dialog.Commands.Add(new Windows.UI.Popups.UICommand(L("Cancel")));
            dialog.DefaultCommandIndex = 1;
            dialog.CancelCommandIndex = 1;
            await dialog.ShowAsync();
        }
    }
}