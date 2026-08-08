using System;
using System.Diagnostics;
using System.Collections.Generic;
using Windows.Storage;

namespace HyperMedia
{
    public static class PlayHistory
    {
        private const string KEY_PREFIX = "RecentPlay_";
        private const int MAX_PER_CATEGORY = 6;

        private static readonly Dictionary<string, string[]> CategoryExtensions = new Dictionary<string, string[]>
        {
            { "Videos", new[] { ".mp4", ".avi", ".mkv", ".webm", ".flv", ".mov", ".wmv", ".3gp", ".ts", ".mka", ".mpg", ".mpeg", ".m4v" } },
            { "Music", new[] { ".mp3", ".flac", ".wav", ".aac", ".ogg", ".wma", ".m4a", ".opus", ".ape", ".alac" } },
            { "Photos", new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".webp" } }
        };

        public static string GetCategory(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            string ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
            foreach (var cat in CategoryExtensions)
            {
                foreach (var e in cat.Value)
                {
                    if (e == ext) return cat.Key;
                }
            }
            return null;
        }

        public static void Add(string filePath, string fileName)
        {
            try
            {
                string category = GetCategory(fileName);
                if (category == null) return;

                var settings = ApplicationData.Current.LocalSettings;
                string key = KEY_PREFIX + category;

                var list = new List<string>();
                if (settings.Values.ContainsKey(key))
                {
                    string serialized = settings.Values[key] as string;
                    if (!string.IsNullOrEmpty(serialized))
                    {
                        list.AddRange(serialized.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
                    }
                }

                // Remove duplicate if exists
                list.RemoveAll(x => x.StartsWith(filePath + "::", StringComparison.OrdinalIgnoreCase));

                // Add to front
                list.Insert(0, filePath + "::" + fileName);

                // Trim to max
                while (list.Count > MAX_PER_CATEGORY)
                    list.RemoveAt(list.Count - 1);

                settings.Values[key] = string.Join("|", list);
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
        }

        public static List<Tuple<string, string>> GetRecent(string category)
        {
            var result = new List<Tuple<string, string>>();
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                string key = KEY_PREFIX + category;

                if (!settings.Values.ContainsKey(key)) return result;

                string serialized = settings.Values[key] as string;
                if (string.IsNullOrEmpty(serialized)) return result;

                string[] items = serialized.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string item in items)
                {
                    string[] parts = item.Split(new[] { "::" }, 2, StringSplitOptions.None);
                    if (parts.Length == 2)
                    {
                        result.Add(Tuple.Create(parts[0], parts[1]));
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
            return result;
        }

        public static void Clear(string category)
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                string key = KEY_PREFIX + category;
                if (settings.Values.ContainsKey(key))
                    settings.Values.Remove(key);
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
        }

        public static string GetResumeText(string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName)) return null;
                var settings = ApplicationData.Current.LocalSettings;
                string key = "ResumePosition_" + fileName;
                if (settings.Values.ContainsKey(key))
                {
                    long ms = (long)settings.Values[key];
                    if (ms <= 0) return null;
                    var ts = TimeSpan.FromMilliseconds(ms);
                    string text = string.Format("{0:00}:{1:00}", (int)ts.TotalMinutes, ts.Seconds);
                    if (ts.TotalHours >= 1)
                        text = string.Format("{0}:{1:00}:{2:00}", (int)ts.TotalHours, ts.Minutes, ts.Seconds);
                    return "续播 " + text;
                }
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
            return null;
        }

        public static double GetResumePercent(string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName)) return -1;
                var settings = ApplicationData.Current.LocalSettings;
                string key = "ResumePercent_" + fileName;
                if (settings.Values.ContainsKey(key))
                    return (double)settings.Values[key];
            }
            catch { }
            return -1;
        }

        private const string KEY_RATING = "Rating_";
        private const string KEY_PLAYCOUNT = "PlayCount_";

        public static int GetRating(string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName)) return 0;
                var settings = ApplicationData.Current.LocalSettings;
                string key = KEY_RATING + fileName;
                if (settings.Values.ContainsKey(key))
                    return (int)settings.Values[key];
            }
            catch { }
            return 0;
        }

        public static void SetRating(string fileName, int rating)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName)) return;
                var settings = ApplicationData.Current.LocalSettings;
                string key = KEY_RATING + fileName;
                if (rating <= 0)
                    settings.Values.Remove(key);
                else
                    settings.Values[key] = rating;
            }
            catch { }
        }

        public static int GetPlayCount(string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName)) return 0;
                var settings = ApplicationData.Current.LocalSettings;
                string key = KEY_PLAYCOUNT + fileName;
                if (settings.Values.ContainsKey(key))
                    return (int)settings.Values[key];
            }
            catch { }
            return 0;
        }

        public static void IncrementPlayCount(string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName)) return;
                var settings = ApplicationData.Current.LocalSettings;
                string key = KEY_PLAYCOUNT + fileName;
                int count = 1;
                if (settings.Values.ContainsKey(key))
                    count = (int)settings.Values[key] + 1;
                settings.Values[key] = count;
            }
            catch { }
        }

        public static void ClearAll()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                foreach (string category in CategoryExtensions.Keys)
                {
                    string key = KEY_PREFIX + category;
                    if (settings.Values.ContainsKey(key))
                        settings.Values.Remove(key);
                }
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
        }

        private const string KEY_URL_HISTORY = "UrlHistory";
        private const int MAX_URL_HISTORY = 10;

        public static void AddUrl(string url)
        {
            try
            {
                if (string.IsNullOrEmpty(url)) return;
                var settings = ApplicationData.Current.LocalSettings;
                var list = new List<string>();
                if (settings.Values.ContainsKey(KEY_URL_HISTORY))
                {
                    string serialized = settings.Values[KEY_URL_HISTORY] as string;
                    if (!string.IsNullOrEmpty(serialized))
                        list.AddRange(serialized.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
                }
                list.RemoveAll(x => x.Equals(url, StringComparison.OrdinalIgnoreCase));
                list.Insert(0, url);
                while (list.Count > MAX_URL_HISTORY)
                    list.RemoveAt(list.Count - 1);
                settings.Values[KEY_URL_HISTORY] = string.Join("|", list);
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
        }

        public static List<string> GetUrlHistory()
        {
            var result = new List<string>();
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (!settings.Values.ContainsKey(KEY_URL_HISTORY)) return result;
                string serialized = settings.Values[KEY_URL_HISTORY] as string;
                if (string.IsNullOrEmpty(serialized)) return result;
                result.AddRange(serialized.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
            return result;
        }
    }

    public static class PlaylistLibrary
    {
        private const string KEY_LIB = "PlaylistLib_";
        private const string KEY_LIB_NAMES = "PlaylistLib_Names";

        public static List<string> GetPlaylistNames()
        {
            var result = new List<string>();
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (!settings.Values.ContainsKey(KEY_LIB_NAMES)) return result;
                string serialized = settings.Values[KEY_LIB_NAMES] as string;
                if (!string.IsNullOrEmpty(serialized))
                    result.AddRange(serialized.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
            return result;
        }

        public static bool CreatePlaylist(string name, List<string> filePaths)
        {
            try
            {
                if (string.IsNullOrEmpty(name)) return false;
                name = name.Trim();
                if (filePaths == null || filePaths.Count == 0) return false;

                var settings = ApplicationData.Current.LocalSettings;

                var names = GetPlaylistNames();
                if (!names.Contains(name))
                {
                    names.Add(name);
                    settings.Values[KEY_LIB_NAMES] = string.Join("|", names);
                }

                settings.Values[KEY_LIB + name] = string.Join("|", filePaths);
                return true;
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
            return false;
        }

        public static List<string> GetPlaylistFiles(string name)
        {
            var result = new List<string>();
            try
            {
                if (string.IsNullOrEmpty(name)) return result;
                var settings = ApplicationData.Current.LocalSettings;
                string key = KEY_LIB + name;
                if (!settings.Values.ContainsKey(key)) return result;
                string serialized = settings.Values[key] as string;
                if (!string.IsNullOrEmpty(serialized))
                    result.AddRange(serialized.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
            return result;
        }

        public static bool DeletePlaylist(string name)
        {
            try
            {
                if (string.IsNullOrEmpty(name)) return false;
                var settings = ApplicationData.Current.LocalSettings;

                var names = GetPlaylistNames();
                if (names.Remove(name))
                    settings.Values[KEY_LIB_NAMES] = string.Join("|", names);

                string key = KEY_LIB + name;
                if (settings.Values.ContainsKey(key))
                    settings.Values.Remove(key);
                return true;
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
            return false;
        }

        // Smart playlists built from history metadata
        public static List<string> GetSmartPlaylist(string kind)
        {
            var result = new List<string>();
            try
            {
                var candidates = new List<Tuple<string, string, int, int>>(); // path, name, rating, count
                foreach (var cat in new[] { "Videos", "Music" })
                {
                    foreach (var t in PlayHistory.GetRecent(cat))
                    {
                        candidates.Add(Tuple.Create(t.Item1, t.Item2, PlayHistory.GetRating(t.Item2), PlayHistory.GetPlayCount(t.Item2)));
                    }
                }

                if (kind == "toprated")
                {
                    candidates.Sort((a, b) => b.Item3.CompareTo(a.Item3));
                    foreach (var c in candidates)
                        if (c.Item3 >= 4) result.Add(c.Item1);
                }
                else if (kind == "mostplayed")
                {
                    candidates.Sort((a, b) => b.Item4.CompareTo(a.Item4));
                    foreach (var c in candidates)
                        if (c.Item4 >= 3) result.Add(c.Item1);
                }
                else if (kind == "recent")
                {
                    foreach (var c in candidates)
                        result.Add(c.Item1);
                }
            }
            catch (Exception ex) { Debug.WriteLine("[HyperMedia] Caught: " + ex.Message); }
            return result;
        }
    }
}
