using System;
using System.Text;
using System.Threading.Tasks;
using HyperMedia.MediaCore;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;

namespace HyperMedia
{
    // Software-decode the first frame of a video file with libVLC and cache it as a
    // JPEG. Used when Windows cannot produce a system thumbnail (e.g. HEVC on 8.1).
    public static class VideoThumbnailService
    {
        private const int TargetWidth = 160;

        public static async Task<BitmapImage> TryGetThumbnailAsync(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath)) return null;

                string key = HashPath(filePath);
                var cacheFolder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                    "ThumbCache", CreationCollisionOption.OpenIfExists);

                var cached = await cacheFolder.TryGetItemAsync(key + ".jpg") as StorageFile;
                if (cached != null)
                {
                    var cachedStream = await cached.OpenAsync(FileAccessMode.Read);
                    var cachedImage = new BitmapImage();
                    await cachedImage.SetSourceAsync(cachedStream);
                    return cachedImage;
                }

                int width = 0;
                int height = 0;
                byte[] pixels = null;

                await Task.Run(() =>
                {
                    try
                    {
                        var decoder = new LibVlcDecoder();
                        try
                        {
                            if (!decoder.OpenFile(filePath)) return;
                            if (!decoder.HasVideo) return;

                            decoder.SeekTo(2.0);
                            DecodedVideoFrame frame = null;
                            for (int i = 0; i < 12 && frame == null; i++)
                                frame = decoder.ReadNextVideoFrame();
                            if (frame == null || frame.Width <= 0 || frame.Height <= 0 || frame.Data.Length == 0)
                                return;

                            width = frame.Width;
                            height = frame.Height;
                            pixels = new byte[frame.Data.Length];
                            Array.Copy(frame.Data, pixels, pixels.Length);
                        }
                        finally
                        {
                            decoder.Close();
                        }
                    }
                    catch { }
                });

                if (pixels == null || width <= 0 || height <= 0) return null;

                int thumbWidth = TargetWidth;
                int thumbHeight = Math.Max(1, (int)((long)height * TargetWidth / width));
                byte[] small = ScaleBgra(pixels, width, height, thumbWidth, thumbHeight);

                var thumbFile = await cacheFolder.CreateFileAsync(key + ".jpg", CreationCollisionOption.ReplaceExisting);
                using (var stream = await thumbFile.OpenAsync(FileAccessMode.ReadWrite))
                {
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
                    encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                        (uint)thumbWidth, (uint)thumbHeight, 96, 96, small);
                    await encoder.FlushAsync();
                }

                var resultStream = await thumbFile.OpenAsync(FileAccessMode.Read);
                var image = new BitmapImage();
                await image.SetSourceAsync(resultStream);
                return image;
            }
            catch
            {
                return null;
            }
        }

        private static string HashPath(string path)
        {
            var hasher = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha1);
            var buffer = CryptographicBuffer.ConvertStringToBinary(path.ToLowerInvariant(), BinaryStringEncoding.Utf8);
            var hash = hasher.HashData(buffer);
            var bytes = new byte[hash.Length];
            DataReader.FromBuffer(hash).ReadBytes(bytes);
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static byte[] ScaleBgra(byte[] src, int srcW, int srcH, int dstW, int dstH)
        {
            var dst = new byte[dstW * dstH * 4];
            for (int y = 0; y < dstH; y++)
            {
                int sy = (y * srcH) / dstH;
                if (sy >= srcH) sy = srcH - 1;
                for (int x = 0; x < dstW; x++)
                {
                    int sx = (x * srcW) / dstW;
                    if (sx >= srcW) sx = srcW - 1;
                    int si = (sy * srcW + sx) * 4;
                    int di = (y * dstW + x) * 4;
                    dst[di] = src[si];
                    dst[di + 1] = src[si + 1];
                    dst[di + 2] = src[si + 2];
                    dst[di + 3] = 255;
                }
            }
            return dst;
        }
    }
}