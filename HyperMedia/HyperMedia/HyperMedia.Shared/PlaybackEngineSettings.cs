using Windows.Storage;

namespace HyperMedia
{
    public enum PlaybackEngine { MediaElement, Vlc }

    public static class PlaybackEngineSettings
    {
        private const string KEY = "PlaybackEngine";

        public static bool IsEngineAvailable(PlaybackEngine engine)
        {
            if (engine == PlaybackEngine.MediaElement) return true;
#if USE_LIBVLC
            return true;
#else
            return false;
#endif
        }

        public static PlaybackEngine Default
        {
            get
            {
#if WINDOWS_PHONE_APP
                return PlaybackEngine.MediaElement;
#else
                return PlaybackEngine.Vlc;
#endif
            }
        }

        public static PlaybackEngine Current
        {
            get
            {
                try
                {
                    var v = ApplicationData.Current.LocalSettings.Values[KEY] as string;
                    PlaybackEngine e;
                    if (v == "vlc") e = PlaybackEngine.Vlc;
                    else if (v == "media") e = PlaybackEngine.MediaElement;
                    else return Default;
                    return IsEngineAvailable(e) ? e : Default;
                }
                catch { return Default; }
            }
            set
            {
                try
                {
                    ApplicationData.Current.LocalSettings.Values[KEY] =
                        value == PlaybackEngine.Vlc ? "vlc" : "media";
                }
                catch { }
            }
        }
    }
}