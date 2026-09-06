using System;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace HyperMedia
{
    public static class AccentHelper
    {
        public const string KEY_ACCENT_COLOR = "Settings_AccentColor";
        public const string KEY_FOLLOW_SYSTEM = "Settings_FollowSystemAccent";

        private static readonly Color DefaultAccent = Color.FromArgb(255, 224, 64, 251);

        private static Color _currentAccent = DefaultAccent;
        public static Color CurrentAccent
        {
            get { return _currentAccent; }
        }

        public static SolidColorBrush AccentBrush
        {
            get { return new SolidColorBrush(_currentAccent); }
        }

        public static void Load()
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            bool followSystem = false;
            if (localSettings.Values.ContainsKey(KEY_FOLLOW_SYSTEM))
                followSystem = (bool)localSettings.Values[KEY_FOLLOW_SYSTEM];

            if (followSystem)
            {
                _currentAccent = DefaultAccent;
            }
            else if (localSettings.Values.ContainsKey(KEY_ACCENT_COLOR))
            {
                uint packed = (uint)localSettings.Values[KEY_ACCENT_COLOR];
                _currentAccent = Color.FromArgb(
                    (byte)((packed >> 24) & 0xFF),
                    (byte)((packed >> 16) & 0xFF),
                    (byte)((packed >> 8) & 0xFF),
                    (byte)(packed & 0xFF));
            }
            else
            {
                _currentAccent = DefaultAccent;
            }

            UpdateResources();
        }

        private static void UpdateResources()
        {
            try
            {
                var resources = Application.Current.Resources;
                var c = _currentAccent;
                resources["AccentBrush"] = new SolidColorBrush(c);
                resources["AccentDimBrush"] = new SolidColorBrush(Color.FromArgb(0x22, c.R, c.G, c.B));
                resources["AccentSubtleBrush"] = new SolidColorBrush(Color.FromArgb(0x44, c.R, c.G, c.B));
                resources["AccentSemiBrush"] = new SolidColorBrush(Color.FromArgb(0x33, c.R, c.G, c.B));
                resources["AccentHoverBrush"] = new SolidColorBrush(Color.FromArgb(0x15, c.R, c.G, c.B));
                resources["AccentTagBrush"] = new SolidColorBrush(Color.FromArgb(0x20, c.R, c.G, c.B));
                resources["AccentVeryDimBrush"] = new SolidColorBrush(Color.FromArgb(0x18, c.R, c.G, c.B));
            }
            catch { }
        }

        public static void SetAccent(Color color)
        {
            _currentAccent = color;
            uint packed = (uint)((color.A << 24) | (color.R << 16) | (color.G << 8) | color.B);
            ApplicationData.Current.LocalSettings.Values[KEY_ACCENT_COLOR] = packed;
            UpdateResources();
        }

        public static void SetFollowSystem(bool follow)
        {
            ApplicationData.Current.LocalSettings.Values[KEY_FOLLOW_SYSTEM] = follow;
            Load();
        }

        public static bool GetFollowSystem()
        {
            var settings = ApplicationData.Current.LocalSettings;
            if (settings.Values.ContainsKey(KEY_FOLLOW_SYSTEM))
                return (bool)settings.Values[KEY_FOLLOW_SYSTEM];
            return false;
        }

        public static Color GetStoredColor()
        {
            var settings = ApplicationData.Current.LocalSettings;
            if (settings.Values.ContainsKey(KEY_ACCENT_COLOR))
            {
                uint packed = (uint)settings.Values[KEY_ACCENT_COLOR];
                return Color.FromArgb(
                    (byte)((packed >> 24) & 0xFF),
                    (byte)((packed >> 16) & 0xFF),
                    (byte)((packed >> 8) & 0xFF),
                    (byte)(packed & 0xFF));
            }
            return DefaultAccent;
        }

        public static SolidColorBrush BrushWithAlpha(byte alpha)
        {
            return new SolidColorBrush(Color.FromArgb(alpha, _currentAccent.R, _currentAccent.G, _currentAccent.B));
        }

        public static Color ColorWithAlpha(byte alpha)
        {
            return Color.FromArgb(alpha, _currentAccent.R, _currentAccent.G, _currentAccent.B);
        }
    }
}
