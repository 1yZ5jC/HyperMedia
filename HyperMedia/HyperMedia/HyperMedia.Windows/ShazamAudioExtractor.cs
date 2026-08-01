using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HyperMedia.MediaCore;

namespace HyperMedia
{
    /// <summary>
    /// Extracts the first few seconds of a media file as 16 kHz mono PCM,
    /// ready for Shazam fingerprinting. Uses the MediaCore decoder so the
    /// active libVLCX playback session is not disturbed.
    /// </summary>
    public static class ShazamAudioExtractor
    {
        /// <summary>
        /// Collect up to the given duration (seconds) of audio from a file.
        /// Returns 16 kHz mono samples, or null on failure.
        /// </summary>
        public static async Task<short[]> Extract16kMonoAsync(string filePath, double seconds = 6.0)
        {
            System.Diagnostics.Debug.WriteLine("[HyperMedia] ExtractAudio: file='{0}' target={1:F0}s", filePath, seconds);
            return await Task.Run<short[]>(() =>
            {
                try
                {
                    var decoder = new LibVlcDecoder();
                    if (!decoder.OpenFile(filePath))
                    {
                        decoder.Close();
                        System.Diagnostics.Debug.WriteLine("[HyperMedia] ExtractAudio: LibVlcDecoder.OpenFile FAILED");
                        return null;
                    }
                    System.Diagnostics.Debug.WriteLine("[HyperMedia] ExtractAudio: decoder opened (rate={0} ch={1})",
                        decoder.AudioSampleRate, decoder.AudioChannels);

                    // CollectAudioPcm returns interleaved S16N (44100 Hz stereo)
                    // Capture rate/channels BEFORE Close() resets them to 0.
                    int rate = decoder.AudioSampleRate;
                    int channels = decoder.AudioChannels;
                    var pcm = decoder.CollectAudioPcm(seconds);
                    decoder.Close();
                    if (pcm == null || pcm.Length == 0)
                    {
                        System.Diagnostics.Debug.WriteLine("[HyperMedia] ExtractAudio: collected no PCM");
                        return null;
                    }
                    System.Diagnostics.Debug.WriteLine("[HyperMedia] ExtractAudio: collected {0} samples ({1:F1}s)",
                        pcm.Length, pcm.Length / (rate * (double)channels));

                    // Downsample to 16k mono
                    var mono = DownsampleTo16kMono(pcm, rate, channels);
                    if (mono == null)
                    {
                        System.Diagnostics.Debug.WriteLine("[HyperMedia] ExtractAudio: downsampling FAILED (rate={0} ch={1})",
                            rate, channels);
                        return null;
                    }
                    System.Diagnostics.Debug.WriteLine("[HyperMedia] ExtractAudio: 16k mono {0} samples ({1:F1}s)",
                        mono.Length, mono.Length / 16000.0);
                    return mono;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("[HyperMedia] ShazamAudioExtractor FAILED: {0}", ex.Message);
                    return null;
                }
            });
        }

        private static short[] DownsampleTo16kMono(short[] interleavedStereo, int sampleRate, int channels)
        {
            if (channels <= 0 || sampleRate <= 0) return null;

            // Mono mix (average channels)
            int frames = interleavedStereo.Length / channels;
            var mono = new short[frames];
            for (int i = 0; i < frames; i++)
            {
                long sum = 0;
                for (int c = 0; c < channels; c++)
                    sum += interleavedStereo[i * channels + c];
                mono[i] = (short)(sum / channels);
            }

            // Linear interpolation downsample to 16 kHz
            int targetRate = 16000;
            int outLen = (int)((long)frames * targetRate / sampleRate);
            var result = new short[outLen];
            for (int i = 0; i < outLen; i++)
            {
                double pos = (double)i * sampleRate / targetRate;
                int i0 = (int)pos;
                int i1 = i0 + 1 < frames ? i0 + 1 : i0;
                double frac = pos - i0;
                result[i] = (short)(mono[i0] * (1 - frac) + mono[i1] * frac);
            }
            return result;
        }
    }
}
