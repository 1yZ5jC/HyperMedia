using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.Data.Xml.Dom;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Notifications;

namespace HyperMedia
{
    // Live tile (start screen) integration for the "Now Playing" tile:
    // wide + square tiles with album art cached in LocalFolder, plus a
    // play/pause glyph badge. Updated on track change / pause / resume / stop.
    public static class LiveTileService
    {
        private const string CoverFolderName = "LiveTile";
        private const string CoverFileName = "cover.jpg";
        private const string CoverUri = "ms-appdata:///local/LiveTile/cover.jpg";
        private const int PhotoRollCount = 5;
        private const string PhotoIndexKey = "LiveTilePhotoIdx";

        // Photo carousel tile: the five most recently opened photos, rotating on
        // the start screen (wide PeekImageCollection + square PeekImage). Overrides
        // the now-playing tile while photos are being browsed.
        public static async Task UpdatePhotoTileAsync(StorageFile photoFile)
        {
            try
            {
                if (photoFile == null) return;
                var thumb = await photoFile.GetThumbnailAsync(
                    Windows.Storage.FileProperties.ThumbnailMode.PicturesView, 360);
                if (thumb == null) return;

                int idx = 0;
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.ContainsKey(PhotoIndexKey))
                    int.TryParse(settings.Values[PhotoIndexKey] as string, out idx);
                idx = (idx + 1) % PhotoRollCount;
                settings.Values[PhotoIndexKey] = idx.ToString();

                var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                    CoverFolderName, CreationCollisionOption.OpenIfExists);
                string photoName = "photo_" + idx + ".jpg";
                var photoFile2 = await folder.CreateFileAsync(
                    photoName, CreationCollisionOption.ReplaceExisting);
                using (var fs = await photoFile2.OpenAsync(FileAccessMode.ReadWrite))
                using (var outStream = fs.AsStreamForWrite())
                using (var inStream = thumb.AsStreamForRead())
                {
                    inStream.Seek(0, System.IO.SeekOrigin.Begin);
                    await inStream.CopyToAsync(outStream);
                }

                UpdatePhotoCollectionTile(folder);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HyperMedia] LiveTile photo update failed: {0}", ex.Message);
            }
        }

        public static async Task UpdateNowPlayingAsync(
            string title, string artist, string album,
            IRandomAccessStream coverStream, bool isPlaying)
        {
            try
            {
                string coverSrc = null;
                if (coverStream != null)
                {
                    try
                    {
                        coverStream.Seek(0);
                        var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                            CoverFolderName, CreationCollisionOption.OpenIfExists);
                        var file = await folder.CreateFileAsync(
                            CoverFileName, CreationCollisionOption.ReplaceExisting);
                        using (var fs = await file.OpenAsync(FileAccessMode.ReadWrite))
                        using (var outStream = fs.AsStreamForWrite())
                        using (var inStream = coverStream.AsStreamForRead())
                        {
                            inStream.Seek(0, System.IO.SeekOrigin.Begin);
                            await inStream.CopyToAsync(outStream);
                        }
                        coverSrc = CoverUri;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[HyperMedia] LiveTile cover write failed: {0}", ex.Message);
                    }
                }

                UpdateTile(title, artist, album, coverSrc);
                UpdateBadge(isPlaying);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HyperMedia] LiveTile update failed: {0}", ex.Message);
            }
        }

        private static async Task UpdatePhotoCollectionTile(StorageFolder folder)
        {
            // Collect the existing photo_<n>.jpg files (0..4) in fixed order.
            var uris = new System.Collections.Generic.List<string>();
            for (int i = 0; i < PhotoRollCount; i++)
            {
                var probe = await folder.TryGetItemAsync("photo_" + i + ".jpg");
                if (probe != null)
                    uris.Add("ms-appdata:///local/LiveTile/photo_" + i + ".jpg");
            }
            if (uris.Count == 0) return;

            var tileDoc = TileUpdateManager.GetTemplateContent(
                TileTemplateType.TileWide310x150PeekImageCollection05);
            var visual = tileDoc.SelectSingleNode("/tile/visual");
            if (visual == null) return;

            // 8.1 has no plain-image square templates; use the image+text variant.
            var square = TileUpdateManager.GetTemplateContent(
                TileTemplateType.TileSquare150x150PeekImageAndText01);
            var squareBinding = square.SelectSingleNode("/tile/visual/binding");
            if (squareBinding != null)
                visual.AppendChild(tileDoc.ImportNode(squareBinding, true));

            var texts = tileDoc.SelectNodes("/tile/visual/binding[2]/text");
            if (texts.Length >= 1)
            {
                texts[0].InnerText = "HyperMedia";
            }

            var images = tileDoc.GetElementsByTagName("image");
            int set = Math.Min((int)images.Length, uris.Count);
            for (int i = 0; i < set; i++)
            {
                var srcAttr = images[i].Attributes.GetNamedItem("src");
                if (srcAttr != null) srcAttr.NodeValue = uris[i];
            }

            TileUpdateManager.CreateTileUpdaterForApplication().Update(
                new TileNotification(tileDoc));
            Debug.WriteLine("[HyperMedia] LiveTile photo collection: {0} images", set);
        }

        public static void UpdateBadge(bool isPlaying)
        {
            try
            {
                var doc = BadgeUpdateManager.GetTemplateContent(BadgeTemplateType.BadgeGlyph);
                var glyph = doc.SelectSingleNode("/badge/glyph");
                if (glyph != null)
                {
                    var attr = glyph.Attributes.GetNamedItem("value");
                    if (attr != null)
                        attr.NodeValue = isPlaying ? "playing" : "paused";
                }
                BadgeUpdateManager.CreateBadgeUpdaterForApplication().Update(
                    new BadgeNotification(doc));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HyperMedia] LiveTile badge failed: {0}", ex.Message);
            }
        }

        public static void Clear()
        {
            try
            {
                TileUpdateManager.CreateTileUpdaterForApplication().Clear();
                BadgeUpdateManager.CreateBadgeUpdaterForApplication().Clear();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HyperMedia] LiveTile clear failed: {0}", ex.Message);
            }
        }

        private static void UpdateTile(string title, string artist, string album, string coverSrc)
        {
            if (string.IsNullOrEmpty(title)) title = "HyperMedia";
            string subtitle = "";
            if (!string.IsNullOrEmpty(artist)) subtitle = artist;
            if (!string.IsNullOrEmpty(album))
            {
                if (subtitle.Length > 0) subtitle += " · ";
                subtitle += album;
            }

            // 8.1 has no strongly-typed tile content model (Win10 only), so build the
            // combined visual from GetTemplateContent templates via XML DOM.
            // Wide ImageAndText02 shows image + two text lines simultaneously; the
            // square template set has no image+text variant, so use PeekImageAndText04
            // (image flips to text periodically). Text count is checked dynamically
            // because the templates returned by 8.1 may have fewer text nodes
            // than documented.
            var tileDoc = TileUpdateManager.GetTemplateContent(
                TileTemplateType.TileWide310x150ImageAndText02);
            var visual = tileDoc.SelectSingleNode("/tile/visual");
            if (visual == null) return;

            var square = TileUpdateManager.GetTemplateContent(
                TileTemplateType.TileSquare150x150PeekImageAndText04);
            var squareBinding = square.SelectSingleNode("/tile/visual/binding");
            if (squareBinding != null)
                visual.AppendChild(tileDoc.ImportNode(squareBinding, true));

            // Wide binding (first) is expected to have two text fields, square
            // (second) one or two — set whatever the template provides.
            var texts = tileDoc.SelectNodes("/tile/visual/binding[1]/text");
            if (texts.Length >= 1)
            {
                texts[0].InnerText = title;
            }
            if (texts.Length >= 2)
            {
                texts[1].InnerText = subtitle;
            }
            texts = tileDoc.SelectNodes("/tile/visual/binding[2]/text");
            if (texts.Length >= 1)
            {
                texts[0].InnerText = title;
            }
            if (texts.Length >= 2)
            {
                texts[1].InnerText = subtitle;
            }

            var images = tileDoc.GetElementsByTagName("image");
            if (coverSrc != null)
            {
                for (int i = 0; i < (int)images.Length; i++)
                {
                    var srcAttr = images[i].Attributes.GetNamedItem("src");
                    if (srcAttr != null) srcAttr.NodeValue = coverSrc;
                }
            }
            else
            {
                // No cover: drop the image elements, the text-only layout remains.
                for (int i = (int)images.Length - 1; i >= 0; i--)
                {
                    var parent = images[i].ParentNode;
                    if (parent != null) parent.RemoveChild(images[i]);
                }
            }

            TileUpdateManager.CreateTileUpdaterForApplication().Update(
                new TileNotification(tileDoc));
            Debug.WriteLine("[HyperMedia] LiveTile XML: " + tileDoc.GetXml());
        }
    }
}