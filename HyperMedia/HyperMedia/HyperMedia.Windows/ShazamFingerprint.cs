using System;
using System.Collections.Generic;

namespace HyperMedia
{
    /// <summary>
    /// C# port of the Shazam signature algorithm (from shazamio).
    /// Produces a Shazam-compatible audio signature from 16 kHz mono PCM.
    /// </summary>
    public static class ShazamFingerprint
    {
        private const int SampleRate = 16000;
        private const int RingBufferSize = 2048;
        private const int FftOutputsSize = 256;
        private const double MaxTimeSeconds = 3.1;
        private const int MaxPeaks = 255;
        private const double MinPeakMagnitude = 1.0 / 64.0;

        private class FrequencyPeak
        {
            public int FftPassNumber;
            public int PeakMagnitude;
            public int CorrectedPeakFrequencyBin;
        }

        private class Signature
        {
            public int NumberSamples = 0;
            public Dictionary<int, List<FrequencyPeak>> Bands = new Dictionary<int, List<FrequencyPeak>>();
        }

        /// <summary>
        /// Generate a Shazam signature data-URI from 16 kHz mono PCM samples.
        /// Returns null if there is not enough audio to build a signature.
        /// </summary>
        public static string GenerateSignatureUri(short[] samples)
        {
            System.Diagnostics.Debug.WriteLine("[HyperMedia] Fingerprint: input {0} samples ({1:F1}s @16k)",
                samples != null ? samples.Length : 0, samples != null ? samples.Length / 16000.0 : 0);
            if (samples == null || samples.Length < 16000 * 2)
            {
                System.Diagnostics.Debug.WriteLine("[HyperMedia] Fingerprint: too few samples, aborting");
                return null;
            }

            var sig = BuildSignature(samples);
            if (sig == null)
            {
                System.Diagnostics.Debug.WriteLine("[HyperMedia] Fingerprint: no peaks detected");
                return null;
            }

            int totalPeaks = 0;
            var bandInfo = new System.Text.StringBuilder();
            var sortedBands = new List<int>(sig.Bands.Keys);
            sortedBands.Sort();
            foreach (int b in sortedBands)
            {
                totalPeaks += sig.Bands[b].Count;
                bandInfo.Append("band" + b + ":" + sig.Bands[b].Count + " ");
            }
            System.Diagnostics.Debug.WriteLine("[HyperMedia] Fingerprint: peaks={0} [{1}]", totalPeaks, bandInfo.ToString().Trim());

            string uri = "data:audio/vnd.shazam.sig;base64," + Convert.ToBase64String(EncodeToBinary(sig));
            System.Diagnostics.Debug.WriteLine("[HyperMedia] Fingerprint: signature URI {0} chars", uri.Length);
            return uri;
        }

        // --- Core processing (ported from algorithm.py) ---

        private static double[] HannWindow;

        static ShazamFingerprint()
        {
            // Hann window: np.hanning(2050)[1:-1]
            HannWindow = new double[RingBufferSize];
            for (int i = 0; i < RingBufferSize; i++)
                HannWindow[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * (i + 1) / (2050 - 1));
        }

        private struct Complex
        {
            public double Re;
            public double Im;
            public Complex(double re, double im) { Re = re; Im = im; }
        }

        // Iterative radix-2 FFT (in-place, length 2048)
        private static void Fft(Complex[] data)
        {
            int n = data.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    var t = data[i]; data[i] = data[j]; data[j] = t;
                }
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                double angle = -2 * Math.PI / len;
                var wlen = new Complex(Math.Cos(angle), Math.Sin(angle));
                for (int i = 0; i < n; i += len)
                {
                    var w = new Complex(1, 0);
                    for (int j = 0; j < len / 2; j++)
                    {
                        var u = data[i + j];
                        var v = new Complex(data[i + j + len / 2].Re * w.Re - data[i + j + len / 2].Im * w.Im,
                                            data[i + j + len / 2].Re * w.Im + data[i + j + len / 2].Im * w.Re);
                        data[i + j] = new Complex(u.Re + v.Re, u.Im + v.Im);
                        data[i + j + len / 2] = new Complex(u.Re - v.Re, u.Im - v.Im);
                        w = new Complex(w.Re * wlen.Re - w.Im * wlen.Im, w.Re * wlen.Im + w.Im * wlen.Re);
                    }
                }
            }
        }

        private static double[] FftMagnitude(short[] ringWindow)
        {
            // rfft of 2048 real samples -> 1025 bins
            var buf = new Complex[RingBufferSize];
            for (int i = 0; i < RingBufferSize; i++)
                buf[i] = new Complex(ringWindow[i] * HannWindow[i], 0);

            Fft(buf);

            var mag = new double[1025];
            for (int i = 0; i < 1025; i++)
            {
                double re = buf[i].Re, im = buf[i].Im;
                double m = (re * re + im * im) / (1 << 17);
                if (m < 1e-10) m = 1e-10;
                mag[i] = m;
            }
            return mag;
        }

        private static Signature BuildSignature(short[] samples)
        {
            var sig = new Signature();

            var ringSamples = new int[RingBufferSize];          // ring buffer of int samples
            int ringPos = 0;
            var fftOutputs = new double[FftOutputsSize][];      // ring of magnitude arrays (zeros initially, like Python's [0]*1025)
            int fftPos = 0;
            int fftNumWritten = 0;
            var spreadFft = new double[FftOutputsSize][];
            for (int i = 0; i < FftOutputsSize; i++)
            {
                fftOutputs[i] = new double[1025];
                spreadFft[i] = new double[1025];
            }
            int spreadPos = 0;
            int spreadNumWritten = 0;

            int processed = 0;

            while (processed < samples.Length && (sig.NumberSamples / (double)SampleRate < MaxTimeSeconds ||
                   CountPeaks(sig) < MaxPeaks))
            {
                int chunk = Math.Min(128, samples.Length - processed);
                // Copy chunk into ring buffer
                for (int i = 0; i < chunk; i++)
                {
                    ringSamples[ringPos] = samples[processed + i];
                    ringPos = (ringPos + 1) % RingBufferSize;
                }
                processed += chunk;
                sig.NumberSamples += chunk;

                // Rebuild window: ring[ringPos..] + ring[..ringPos]
                var window = new short[RingBufferSize];
                for (int i = 0; i < RingBufferSize; i++)
                    window[i] = (short)ringSamples[(ringPos + i) % RingBufferSize];

                var fft = FftMagnitude(window);
                fftOutputs[fftPos] = fft;
                fftPos = (fftPos + 1) % FftOutputsSize;
                fftNumWritten++;

                // Peak spreading + recognition (ported)
                DoPeakSpreading(ref spreadFft, ref spreadPos, ref spreadNumWritten, fft);
                if (spreadNumWritten >= 46)
                    DoPeakRecognition(sig, fftOutputs, fftPos, spreadFft, spreadPos, spreadNumWritten);
            }

            if (CountPeaks(sig) == 0) return null;
            return sig;
        }

        private static int CountPeaks(Signature sig)
        {
            int count = 0;
            foreach (var kv in sig.Bands) count += kv.Value.Count;
            return count;
        }

        private static void DoPeakSpreading(ref double[][] spreadFft, ref int spreadPos, ref int spreadNumWritten, double[] originLastFft)
        {
            int n = originLastFft.Length;

            // np.tile(origin,3).reshape(3,-1) with rows rolled by 0,-1,-2:
            // row0[i]=origin[i], row1[i]=origin[i+1] (wrap), row2[i]=origin[i+2] (wrap)
            // max over rows, keep [:n-3], then append origin[n-3:]
            var originNp = new double[n];
            for (int i = 0; i < n - 3; i++)
            {
                double v0 = originLastFft[i];
                double v1 = originLastFft[i + 1];
                double v2 = originLastFft[i + 2];
                originNp[i] = Math.Max(v0, Math.Max(v1, v2));
            }
            originNp[n - 3] = originLastFft[n - 3];
            originNp[n - 2] = originLastFft[n - 2];
            originNp[n - 1] = originLastFft[n - 1];

            int i1 = (spreadPos + FftOutputsSize - 1) % FftOutputsSize;
            int i2 = (spreadPos + FftOutputsSize - 3) % FftOutputsSize;
            int i3 = (spreadPos + FftOutputsSize - 6) % FftOutputsSize;

            if (spreadFft[i1] == null) spreadFft[i1] = new double[n];
            if (spreadFft[i2] == null) spreadFft[i2] = new double[n];
            if (spreadFft[i3] == null) spreadFft[i3] = new double[n];

            // cumulative max: row1 = max(originNp, s1); row2 = max(row1, s2); row3 = max(row2, s3)
            var a1 = new double[n];
            var a2 = new double[n];
            var a3 = new double[n];
            for (int i = 0; i < n; i++)
            {
                a1[i] = Math.Max(originNp[i], spreadFft[i1][i]);
                a2[i] = Math.Max(a1[i], spreadFft[i2][i]);
                a3[i] = Math.Max(a2[i], spreadFft[i3][i]);
            }

            spreadFft[i1] = a1;
            spreadFft[i2] = a2;
            spreadFft[i3] = a3;
            spreadFft[spreadPos] = originNp;
            spreadPos = (spreadPos + 1) % FftOutputsSize;
            spreadNumWritten++;
        }

        private static void DoPeakRecognition(Signature sig, double[][] fftOutputs, int fftPos,
            double[][] spreadFft, int spreadPos, int spreadNumWritten)
        {
            double[] fftMinus46 = fftOutputs[(fftPos + FftOutputsSize - 46) % FftOutputsSize];
            double[] fftMinus49 = spreadFft[(spreadPos + FftOutputsSize - 49) % FftOutputsSize];

            for (int bin = 10; bin < 1015; bin++)
            {
                double peakMag = fftMinus46[bin];
                if (!(peakMag >= MinPeakMagnitude && peakMag >= fftMinus49[bin - 1]))
                    continue;

                double maxNeighbor = 0;
                // [-10,-7,-4,-1, -3, 1, 2,5,8]  (range(-10,-3,3)=[-10,-7,-4]; then -3; then 1; then range(2,9,3)=[2,5,8])
                int[] offsets1 = { -10, -7, -4, -3, 1, 2, 5, 8 };
                foreach (int off in offsets1)
                {
                    double v = fftMinus49[bin + off];
                    if (v > maxNeighbor) maxNeighbor = v;
                }

                if (!(peakMag > maxNeighbor)) continue;

                double maxOther = maxNeighbor;
                // [-53,-45, 165..198 step7, 214..247 step7]
                int[] offsets2 = { -53, -45 };
                foreach (int off in offsets2)
                {
                    double v = spreadFft[(spreadPos + off + FftOutputsSize) % FftOutputsSize][bin - 1];
                    if (v > maxOther) maxOther = v;
                }
                for (int off = 165; off < 201; off += 7)
                {
                    double v = spreadFft[(spreadPos + off + FftOutputsSize) % FftOutputsSize][bin - 1];
                    if (v > maxOther) maxOther = v;
                }
                for (int off = 214; off < 250; off += 7)
                {
                    double v = spreadFft[(spreadPos + off + FftOutputsSize) % FftOutputsSize][bin - 1];
                    if (v > maxOther) maxOther = v;
                }

                if (!(peakMag > maxOther)) continue;

                int fftNumber = spreadNumWritten - 46;

                double peakMagnitude = Math.Log(Math.Max(MinPeakMagnitude, peakMag)) * 1477.3 + 6144;
                double magBefore = Math.Log(Math.Max(MinPeakMagnitude, fftMinus46[bin - 1])) * 1477.3 + 6144;
                double magAfter = Math.Log(Math.Max(MinPeakMagnitude, fftMinus46[bin + 1])) * 1477.3 + 6144;

                double variation1 = peakMagnitude * 2 - magBefore - magAfter;
                if (variation1 <= 0) continue;
                double variation2 = (magAfter - magBefore) * 32 / variation1;

                double correctedBin = bin * 64 + variation2;
                double freqHz = correctedBin * (SampleRate / 2.0 / 1024.0 / 64.0);

                int band;
                // Replicates shazamio exactly: the last branch (3500-5500 Hz) is
                // unreachable in the original (5500 < hz <= 5500), so band 3 is never used.
                if (freqHz > 250 && freqHz < 520) band = 0;
                else if (freqHz > 520 && freqHz < 1450) band = 1;
                else if (freqHz > 1450 && freqHz < 3500) band = 2;
                else if (freqHz > 5500 && freqHz <= 5500) band = 3;
                else continue;

                if (!sig.Bands.ContainsKey(band)) sig.Bands[band] = new List<FrequencyPeak>();
                sig.Bands[band].Add(new FrequencyPeak
                {
                    FftPassNumber = fftNumber,
                    PeakMagnitude = (int)peakMagnitude,
                    CorrectedPeakFrequencyBin = (int)correctedBin
                });
            }
        }

        // --- Binary encoding (ported from signature.py) ---

        private static byte[] EncodeToBinary(Signature sig)
        {
            var contents = new System.IO.MemoryStream();
            var sortedBands = new List<int>(sig.Bands.Keys);
            sortedBands.Sort();

            foreach (int band in sortedBands)
            {
                var peaksBuf = new System.IO.MemoryStream();
                int fftPassNumber = 0;
                var peaks = sig.Bands[band];
                peaks.Sort((a, b) => a.FftPassNumber.CompareTo(b.FftPassNumber));

                foreach (var p in peaks)
                {
                    if (p.FftPassNumber - fftPassNumber >= 255)
                    {
                        peaksBuf.WriteByte(0xFF);
                        WriteUInt32LE(peaksBuf, (uint)p.FftPassNumber);
                        fftPassNumber = p.FftPassNumber;
                    }
                    peaksBuf.WriteByte((byte)(p.FftPassNumber - fftPassNumber));
                    WriteUInt16LE(peaksBuf, (ushort)p.PeakMagnitude);
                    WriteUInt16LE(peaksBuf, (ushort)p.CorrectedPeakFrequencyBin);
                    fftPassNumber = p.FftPassNumber;
                }

                var peakBytes = peaksBuf.ToArray();
                WriteUInt32LE(contents, (uint)(0x60030040 + band));
                WriteUInt32LE(contents, (uint)peakBytes.Length);
                contents.Write(peakBytes, 0, peakBytes.Length);
                // 4-byte alignment padding (Python: -len % 4 is always non-negative)
                int padding = (4 - peakBytes.Length % 4) % 4;
                for (int i = 0; i < padding; i++) contents.WriteByte(0);
            }

            var contentsBytes = contents.ToArray();
            uint sizeMinusHeader = (uint)contentsBytes.Length + 8;

            var buf = new System.IO.MemoryStream();
            // Header (48 bytes) - placeholder, crc patched below
            WriteUInt32LE(buf, 0xCAFE2580);
            WriteUInt32LE(buf, 0); // crc32
            WriteUInt32LE(buf, sizeMinusHeader);
            WriteUInt32LE(buf, 0x94119C00);
            for (int i = 0; i < 3; i++) WriteUInt32LE(buf, 0); // void1
            WriteUInt32LE(buf, 3u << 27); // shifted sample rate id (16000)
            for (int i = 0; i < 2; i++) WriteUInt32LE(buf, 0); // void2
            WriteUInt32LE(buf, (uint)(sig.NumberSamples + SampleRate * 0.24)); // number_samples + sr*0.24
            WriteUInt32LE(buf, (15u << 19) + 0x40000); // fixed value

            WriteUInt32LE(buf, 0x40000000);
            WriteUInt32LE(buf, sizeMinusHeader);
            buf.Write(contentsBytes, 0, contentsBytes.Length);

            var all = buf.ToArray();
            // crc32 over bytes from index 8
            uint crc = Crc32(all, 8, all.Length - 8);
            all[4] = (byte)(crc & 0xFF);
            all[5] = (byte)((crc >> 8) & 0xFF);
            all[6] = (byte)((crc >> 16) & 0xFF);
            all[7] = (byte)((crc >> 24) & 0xFF);
            return all;
        }

        private static void WriteUInt16LE(System.IO.Stream s, ushort v)
        {
            s.WriteByte((byte)(v & 0xFF));
            s.WriteByte((byte)((v >> 8) & 0xFF));
        }

        private static void WriteUInt32LE(System.IO.Stream s, uint v)
        {
            s.WriteByte((byte)(v & 0xFF));
            s.WriteByte((byte)((v >> 8) & 0xFF));
            s.WriteByte((byte)((v >> 16) & 0xFF));
            s.WriteByte((byte)((v >> 24) & 0xFF));
        }

        // Standard IEEE CRC-32
        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }

        private static uint Crc32(byte[] data, int offset, int length)
        {
            uint crc = 0xFFFFFFFF;
            for (int i = offset; i < offset + length; i++)
                crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFF;
        }
    }
}
