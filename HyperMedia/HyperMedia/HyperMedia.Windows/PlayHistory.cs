using System;
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
                list.RemoveAll(x => x.Equals(filePath, StringComparison.OrdinalIgnoreCase));

                // Add to front
                list.Insert(0, filePath + "::" + fileName);

                // Trim to max
                while (list.Count > MAX_PER_CATEGORY)
                    list.RemoveAt(list.Count - 1);

                settings.Values[key] = string.Join("|", list);
            }
            catch { }
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
            catch { }
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
            catch { }
        }
    }
}
