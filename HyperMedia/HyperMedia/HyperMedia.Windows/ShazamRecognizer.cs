using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Data.Json;

namespace HyperMedia
{
    /// <summary>
    /// Shazam-compatible HTTP recognition client (unofficial endpoint).
    /// Sends a Shazam audio signature and returns the matched track.
    /// </summary>
    public static class ShazamRecognizer
    {
        public class TrackResult
        {
            public string Title;
            public string Subtitle; // artist
            public string Key;      // Shazam track key (for lyric lookups)
        }

        /// <summary>
        /// Recognize a track from 16 kHz mono PCM audio.
        /// Returns null when no match or the endpoint is unreachable.
        /// </summary>
        public static async Task<TrackResult> RecognizeAsync(short[] samples)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[HyperMedia] Recognize: {0} samples ({1:F1}s)",
                    samples != null ? samples.Length : 0, samples != null ? samples.Length / 16000.0 : 0);
                string uri = ShazamFingerprint.GenerateSignatureUri(samples);
                if (uri == null)
                {
                    System.Diagnostics.Debug.WriteLine("[HyperMedia] Recognize: signature generation returned null");
                    return null;
                }

                // Match shazamio's request shape
                var body = new JsonObject();
                body["timezone"] = JsonValue.CreateStringValue("Asia/Shanghai");
                var sig = new JsonObject();
                sig["uri"] = JsonValue.CreateStringValue(uri);
                int sampleMs = (int)(samples.Length / 16.0);
                sig["samplems"] = JsonValue.CreateNumberValue(sampleMs);
                body["signature"] = sig;
                body["timestamp"] = JsonValue.CreateNumberValue(
                    (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds);
                body["context"] = new JsonObject();
                body["geolocation"] = new JsonObject();

                // Build the request URL the same way shazamio does. The two
                // UUIDs must be fresh and distinct every request (a fixed or
                // duplicated UUID makes the endpoint return 404).
                string uuid1 = Guid.NewGuid().ToString().ToUpperInvariant();
                string uuid2 = Guid.NewGuid().ToString().ToUpperInvariant();
                string url = "https://amp.shazam.com/discovery/v5/en-US/US/iphone/-/tag/" +
                    uuid1 + "/" + uuid2 +
                    "?sync=true&webv3=true&sampling=true&connected=&shazamapiversion=v3" +
                    "&sharehub=true&hubv5minorversion=v5.1&hidelb=true&video=v3";
                System.Diagnostics.Debug.WriteLine("[HyperMedia] Recognize: POST {0} (samplems={1})", url, sampleMs);
                string json = await HttpPostJsonAsync(url, body.ToString());
                if (string.IsNullOrEmpty(json))
                {
                    System.Diagnostics.Debug.WriteLine("[HyperMedia] Recognize: HTTP response empty/failed");
                    return null;
                }
                System.Diagnostics.Debug.WriteLine("[HyperMedia] Recognize: response {0} chars, head: {1}", json.Length,
                    json.Length > 150 ? json.Substring(0, 150) : json);

                var root = JsonObject.Parse(json);
                JsonObject track = null;
                var actions = root.GetNamedArray("matches", null);
                System.Diagnostics.Debug.WriteLine("[HyperMedia] Recognize: matches count = {0}",
                    actions != null ? actions.Count : 0);
                if (actions != null && actions.Count > 0)
                {
                    var match = actions.GetObjectAt(0);
                    var trackArr = match.GetNamedArray("tracks", null);
                    if (trackArr != null && trackArr.Count > 0)
                    {
                        track = trackArr.GetObjectAt(0);
                        System.Diagnostics.Debug.WriteLine("[HyperMedia] Recognize: first match tracks = {0}", trackArr.Count);
                    }
                }

                if (track == null)
                {
                    // Older response shape: {"track": {...}}
                    track = root.GetNamedObject("track", null);
                }
                if (track == null)
                {
                    System.Diagnostics.Debug.WriteLine("[HyperMedia] Recognize: no track in response");
                    return null;
                }

                var result = new TrackResult
                {
                    Title = track.GetNamedString("title", ""),
                    Subtitle = track.GetNamedString("subtitle", ""),
                    Key = track.GetNamedString("key", "")
                };
                System.Diagnostics.Debug.WriteLine("[HyperMedia] Recognize: RESULT '{0}' - '{1}' (key={2})",
                    result.Title, result.Subtitle, result.Key);
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[HyperMedia] ShazamRecognizer FAILED: {0}", ex.Message);
                return null;
            }
        }

        private static async Task<string> HttpPostJsonAsync(string url, string bodyJson)
        {
            // Use the WinRT HttpClient: System.Net.Http in Win 8.1 UWP restricts
            // custom headers (User-Agent, X-Shazam-*) on POST, which the Shazam
            // endpoint requires. Windows.Web.Http is the UWP-native stack.
            using (var client = new Windows.Web.Http.HttpClient())
            {
                var headers = client.DefaultRequestHeaders;
                headers.TryAppendWithoutValidation("User-Agent", "Mozilla/5.0 (iPhone; CPU iPhone OS 14_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/14.0 Mobile/15E148 Safari/604.1");
                headers.TryAppendWithoutValidation("X-Shazam-Platform", "IPHONE");
                headers.TryAppendWithoutValidation("X-Shazam-AppVersion", "14.1.0");
                headers.TryAppendWithoutValidation("Accept", "*/*");
                headers.TryAppendWithoutValidation("Accept-Language", "en-US,en;q=0.9");

                var content = new Windows.Web.Http.HttpStringContent(bodyJson,
                    Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json");
                try
                {
                    using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(12)))
                    {
                        var resp = await client.PostAsync(new Uri(url), content).AsTask(cts.Token);
                        System.Diagnostics.Debug.WriteLine("[HyperMedia] Recognize HTTP status: {0} ({1})",
                            (int)resp.StatusCode, resp.StatusCode);
                        if (resp.StatusCode != Windows.Web.Http.HttpStatusCode.Ok) return null;
                        return await resp.Content.ReadAsStringAsync();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[HyperMedia] Recognize HTTP POST FAILED: {0}", ex.Message);
                    return null;
                }
            }
        }
    }
}
