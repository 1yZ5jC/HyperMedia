using System;
using System.Collections.Generic;
using System.Diagnostics;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Media;
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
        private const string KEY_LYRIC_OFFSET = "Settings_LyricOffset";
        private const string KEY_AUTO_SYNC = "Settings_LyricAutoSync";

        public SettingsPage()
        {
            _isLoading = true;
            this.InitializeComponent();
            LoadSettings();
            ApplyCurrentLanguage();
            ApplyEngineLimits();
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

            SelectComboBoxItem(EngineCombo,
                PlaybackEngineSettings.Current == PlaybackEngine.Vlc ? "vlc" : "media");

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

            // Accent color
            _isLoadingAccent = true;
            FollowSystemAccentToggle.IsOn = AccentHelper.GetFollowSystem();
            InitAccentSwatches();
            var cur = AccentHelper.CurrentAccent;
            AccentHexInput.Text = string.Format("#{0:X2}{1:X2}{2:X2}", cur.R, cur.G, cur.B);
            AccentPreview.Background = AccentHelper.AccentBrush;
            _isLoadingAccent = false;

            // Lyric offset
            if (settings.Values.ContainsKey(KEY_LYRIC_OFFSET))
                LyricOffsetSlider.Value = (double)settings.Values[KEY_LYRIC_OFFSET];
            if (settings.Values.ContainsKey(KEY_AUTO_SYNC))
                AutoSyncToggle.IsOn = (bool)settings.Values[KEY_AUTO_SYNC];

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
            LyricOffsetSlider.ValueChanged += LyricOffsetSlider_ValueChanged;
            AutoSyncToggle.Toggled += AutoSyncToggle_Toggled;
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
            try { SaveSetting(KEY_LIGHT_THEME, LightThemeToggle.IsOn); }
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

        private void EngineCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            try
            {
                var item = EngineCombo.SelectedItem as ComboBoxItem;
                if (item == null || item.Tag == null) return;
                PlaybackEngineSettings.Current = item.Tag.ToString() == "vlc"
                    ? PlaybackEngine.Vlc : PlaybackEngine.MediaElement;
                ApplyEngineLimits();
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] EngineCombo failed: {0}", ex.Message); }
        }

        // Settings only libVLC can honor are greyed out in MediaElement mode.
        private void ApplyEngineLimits()
        {
            try
            {
                bool vlc = PlaybackEngineSettings.Current == PlaybackEngine.Vlc;
                SetRowEnabled(ResumeRow, vlc);
                SetRowEnabled(SubtitleSizeRow, vlc);
                SetRowEnabled(SubtitleColorRow, vlc);
                SetRowEnabled(SubtitleOutlineRow, vlc);
                SetRowEnabled(SubtitleMarginRow, vlc);
                SetRowEnabled(DeinterlaceRow, vlc);
                SetRowEnabled(LoudnessRow, vlc);
                SetRowEnabled(IntroSkipRow, vlc);
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] ApplyEngineLimits failed: {0}", ex.Message); }
        }

        private static void SetRowEnabled(UIElement row, bool enabled)
        {
            if (row == null) return;
            row.Opacity = enabled ? 1.0 : 0.45;
            SetControlsEnabled(row, enabled);
        }

        private static void SetControlsEnabled(UIElement el, bool enabled)
        {
            var c = el as Control;
            if (c != null) c.IsEnabled = enabled;
            var p = el as Panel;
            if (p == null) return;
            foreach (var child in p.Children)
                SetControlsEnabled(child, enabled);
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

        /// Raised when hosted in a Popup overlay (no Frame to GoBack to).
        public event EventHandler CloseRequested;

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (CloseRequested == null && Frame != null && Frame.CanGoBack)
                Frame.GoBack();
            else if (CloseRequested != null)
                CloseRequested(this, EventArgs.Empty);
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

        public static double GetLyricOffset()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.ContainsKey(KEY_LYRIC_OFFSET))
                    return (double)settings.Values[KEY_LYRIC_OFFSET];
            }
            catch { }
            return 0;
        }

        public static bool GetAutoSync()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.ContainsKey(KEY_AUTO_SYNC))
                    return (bool)settings.Values[KEY_AUTO_SYNC];
            }
            catch { }
            return false;
        }

        // --- Accent color ---
        private bool _isLoadingAccent;
        private static readonly Color[] AccentPalette = new Color[]
        {
            Color.FromArgb(255, 224, 64, 251),
            Color.FromArgb(255, 0, 120, 215),
            Color.FromArgb(255, 0, 153, 188),
            Color.FromArgb(255, 16, 137, 62),
            Color.FromArgb(255, 218, 59, 1),
            Color.FromArgb(255, 191, 0, 119),
            Color.FromArgb(255, 0, 99, 177),
            Color.FromArgb(255, 107, 105, 214),
            Color.FromArgb(255, 0, 188, 212),
            Color.FromArgb(255, 76, 175, 80),
            Color.FromArgb(255, 255, 152, 0),
            Color.FromArgb(255, 244, 67, 54),
        };

        private void InitAccentSwatches()
        {
            if (AccentSwatches == null) return;
            AccentSwatches.Children.Clear();
            var current = AccentHelper.CurrentAccent;
            for (int i = 0; i < AccentPalette.Length; i++)
            {
                var c = AccentPalette[i];
                var swatch = new Border
                {
                    Width = 32, Height = 32,
                    Margin = new Thickness(0, 0, 6, 6),
                    Background = new SolidColorBrush(c),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    Tag = c
                };
                if (c.R == current.R && c.G == current.G && c.B == current.B)
                {
                    swatch.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 255, 255, 255));
                    swatch.BorderThickness = new Thickness(2);
                }
                swatch.Tapped += AccentSwatch_Tapped;
                AccentSwatches.Children.Add(swatch);
            }
        }

        private void AccentSwatch_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (_isLoadingAccent) return;
            var border = sender as Border;
            if (border == null || border.Tag == null) return;
            var color = (Color)border.Tag;
            _isLoadingAccent = true;
            AccentHelper.SetAccent(color);
            AccentHelper.Load();
            AccentHexInput.Text = string.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
            AccentPreview.Background = AccentHelper.AccentBrush;
            InitAccentSwatches();
            _isLoadingAccent = false;
            ApplyAccentToMainPage();
        }

        private void AccentHexInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoadingAccent) return;
            string hex = AccentHexInput.Text.TrimStart('#');
            if (hex.Length != 6) return;
            byte r, g, b;
            if (!byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out r) ||
                !byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out g) ||
                !byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out b))
                return;
            var color = Color.FromArgb(255, r, g, b);
            _isLoadingAccent = true;
            AccentHelper.SetAccent(color);
            AccentHelper.Load();
            AccentPreview.Background = AccentHelper.AccentBrush;
            InitAccentSwatches();
            _isLoadingAccent = false;
            ApplyAccentToMainPage();
        }

        private void FollowSystemAccent_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            AccentHelper.SetFollowSystem(FollowSystemAccentToggle.IsOn);
            AccentHelper.Load();
            _isLoadingAccent = true;
            var cur = AccentHelper.CurrentAccent;
            AccentHexInput.Text = string.Format("#{0:X2}{1:X2}{2:X2}", cur.R, cur.G, cur.B);
            AccentPreview.Background = AccentHelper.AccentBrush;
            InitAccentSwatches();
            _isLoadingAccent = false;
            ApplyAccentToMainPage();
        }

        private void ApplyAccentToMainPage()
        {
            try
            {
                var frame = Window.Current.Content as Frame;
                var mainPage = frame?.Content as MainPage;
                if (mainPage != null)
                {
                    mainPage.ApplyAccentColor();
                    mainPage.ForceAccentRefresh();
                }
            }
            catch { }
        }

        // --- Lyric offset ---
        private void LyricOffsetSlider_ValueChanged(object sender, Windows.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_isLoading) return;
            SaveSetting(KEY_LYRIC_OFFSET, LyricOffsetSlider.Value);
            if (LyricOffsetText != null)
                LyricOffsetText.Text = ((int)LyricOffsetSlider.Value) + "ms";
        }

        private void AutoSyncToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            SaveSetting(KEY_AUTO_SYNC, AutoSyncToggle.IsOn);
        }
    }
}
