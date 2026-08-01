using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Data.Json;

namespace HyperMedia
{
    /// <summary>
    /// Collects public music metadata (album, genre, date, ISRC, ...) for a
    /// recognized track by querying Netease / QQ public search APIs, and
    /// exposes a simple editor that writes tags back via libVLCX.
    /// </summary>
    public static class MetadataEditor
    {
        public class TrackMeta
        {
            public string Title = "";
            public string Artist = "";
            public string Album = "";
            public string Date = "";     // e.g. "2023"
            public string Genre = "";
            public string Isrc = "";
            public int DurationMs = 0;
            public string CoverUrl = "";
        }

        /// <summary>
        /// Query public metadata for (artist, title). Netease first, QQ as
        /// fallback. Returns null if nothing usable was found.
        /// </summary>
        public static async Task<TrackMeta> CollectAsync(string artist, string title)
        {
            try
            {
                var ne = await QueryNeteaseMeta(artist, title);
                if (ne != null) return ne;

                var qq = await QueryQqMeta(artist, title);
                if (qq != null) return qq;

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[HyperMedia] MetadataEditor.Collect FAILED: {0}", ex.Message);
                return null;
            }
        }

        private static async Task<TrackMeta> QueryNeteaseMeta(string artist, string title)
        {
            try
            {
                string query = string.IsNullOrEmpty(artist) ? title : artist + " " + title;
                string searchUrl = "https://music.163.com/api/search/get/web?s=" +
                    Uri.EscapeDataString(query) + "&type=1&offset=0&limit=10";
                string searchJson = await HttpGetAsync(searchUrl);
                if (string.IsNullOrEmpty(searchJson)) return null;

                var search = JsonObject.Parse(searchJson);
                var result = search.GetNamedObject("result", null);
                var songs = result != null ? result.GetNamedArray("songs", null) : null;
                if (songs == null || songs.Count == 0) return null;

                for (uint i = 0; i < songs.Count; i++)
                {
                    var song = songs.GetObjectAt(i);
                    string name = song.GetNamedString("name", "");
                    if (!TitlesMatch(name, title)) continue;
                    if (!string.IsNullOrEmpty(artist) && !ArtistsMatch(song, artist)) continue;

                    var meta = new TrackMeta { Title = name };
                    var artists = song.GetNamedArray("artists", null);
                    if (artists != null && artists.Count > 0)
                        meta.Artist = artists.GetObjectAt(0).GetNamedString("name", "");
                    var album = song.GetNamedObject("album", null);
                    if (album != null)
                        meta.Album = album.GetNamedString("name", "");
                    if (song.ContainsKey("duration"))
                        meta.DurationMs = (int)song.GetNamedNumber("duration", 0);
                    if (song.ContainsKey("id"))
                        meta.CoverUrl = await FetchNeteaseCoverAsync((long)song.GetNamedNumber("id"));
                    return meta;
                }
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[HyperMedia] MetadataEditor Netease FAILED: {0}", ex.Message);
                return null;
            }
        }

        private static async Task<string> FetchNeteaseCoverAsync(long songId)
        {
            try
            {
                string url = "https://music.163.com/api/song/detail?ids=[" + songId + "]";
                string json = await HttpGetAsync(url);
                if (string.IsNullOrEmpty(json)) return null;

                var root = JsonObject.Parse(json);
                var songs = root.GetNamedArray("songs", null);
                if (songs == null || songs.Count == 0) return null;
                var album = songs.GetObjectAt(0).GetNamedObject("album", null);
                if (album == null) return null;
                string pic = album.GetNamedString("picUrl", "");
                System.Diagnostics.Debug.WriteLine("[HyperMedia] MetadataEditor cover: {0}", pic);
                return string.IsNullOrEmpty(pic) ? null : pic;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[HyperMedia] MetadataEditor cover fetch FAILED: {0}", ex.Message);
                return null;
            }
        }

        private static async Task<TrackMeta> QueryQqMeta(string artist, string title)
        {
            try
            {
                string query = string.IsNullOrEmpty(artist) ? title : artist + " " + title;
                string searchUrl = "https://c.y.qq.com/soso/fcgi-bin/client_search_cp?p=1&n=10&w=" +
                    Uri.EscapeDataString(query) + "&format=json";
                string searchJson = await HttpGetAsync(searchUrl);
                if (string.IsNullOrEmpty(searchJson)) return null;

                var search = JsonObject.Parse(searchJson);
                var data = search.GetNamedObject("data", null);
                var song = data != null ? data.GetNamedObject("song", null) : null;
                var list = song != null ? song.GetNamedArray("list", null) : null;
                if (list == null || list.Count == 0) return null;

                for (uint i = 0; i < list.Count; i++)
                {
                    var item = list.GetObjectAt(i);
                    string name = item.GetNamedString("songname", "");
                    if (!TitlesMatch(name, title)) continue;
                    if (!string.IsNullOrEmpty(artist) && !QqArtistsMatch(item, artist)) continue;

                    var meta = new TrackMeta { Title = name };
                    var singers = item.GetNamedArray("singer", null);
                    if (singers != null && singers.Count > 0)
                        meta.Artist = singers.GetObjectAt(0).GetNamedString("name", "");
                    meta.Album = item.GetNamedString("albumname", "");
                    if (item.ContainsKey("interval"))
                        meta.DurationMs = (int)item.GetNamedNumber("interval", 0) * 1000;
                    return meta;
                }
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[HyperMedia] MetadataEditor QQ FAILED: {0}", ex.Message);
                return null;
            }
        }

        private static async Task<string> HttpGetAsync(string url)
        {
            using (var client = new Windows.Web.Http.HttpClient())
            {
                try
                {
                    var resp = await client.GetAsync(new Uri(url)).AsTask(
                        new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8)).Token);
                    if (resp.StatusCode != Windows.Web.Http.HttpStatusCode.Ok) return null;
                    return await resp.Content.ReadAsStringAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[HyperMedia] MetadataEditor HTTP FAILED: {0}", ex.Message);
                    return null;
                }
            }
        }

        private static string NormalizeTitle(string s)
        {
            var sb = new System.Text.StringBuilder();
            bool inParen = false;
            foreach (char c in s.ToLowerInvariant())
            {
                if (c == '(' || c == '（' || c == '[' || c == '【') { inParen = true; continue; }
                if (c == ')' || c == '）' || c == ']' || c == '】') { inParen = false; continue; }
                if (!inParen && !char.IsWhiteSpace(c)) sb.Append(c);
            }
            string norm = sb.ToString();
            int i = 0;
            while (i < norm.Length && char.IsDigit(norm[i])) i++;
            if (i > 0 && i < norm.Length)
            {
                char sep = norm[i];
                if (sep == '.' || sep == '-' || sep == '_' || sep == '：' || sep == ':') return norm.Substring(i + 1);
            }
            return norm;
        }

        private static bool TitlesMatch(string candidate, string title)
        {
            if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(title)) return false;
            string a = NormalizeTitle(candidate);
            string b = NormalizeTitle(title);
            if (a.Length == 0 || a != b) return false;
            // Reject version-tagged candidates (e.g. "X (instrumental)") when query has no tag
            if (ContainsParen(candidate) && !ContainsParen(title)) return false;
            return true;
        }

        private static bool ContainsParen(string s)
        {
            foreach (char c in s)
                if (c == '(' || c == '（' || c == '[' || c == '【') return true;
            return false;
        }

        private static bool ArtistsMatch(JsonObject song, string artist)
        {
            try
            {
                var arr = song.GetNamedArray("artists", null);
                if (arr == null) return false;
                string n = NormalizeTitle(artist);
                for (uint i = 0; i < arr.Count; i++)
                {
                    string name = NormalizeTitle(arr.GetObjectAt(i).GetNamedString("name", ""));
                    if (name.Length > 0 && name == n) return true;
                }
            }
            catch { }
            return false;
        }

        private static bool QqArtistsMatch(JsonObject item, string artist)
        {
            try
            {
                var arr = item.GetNamedArray("singer", null);
                if (arr == null) return false;
                string n = NormalizeTitle(artist);
                for (uint i = 0; i < arr.Count; i++)
                {
                    string name = NormalizeTitle(arr.GetObjectAt(i).GetNamedString("name", ""));
                    if (name.Length > 0 && name == n) return true;
                }
            }
            catch { }
            return false;
        }
    }
}
