using System;

namespace HyperMedia
{
    // Iterative radix-2 FFT with Hann windowing. Pure managed code — Windows 8.1
    // Store apps have no System.Numerics FFT, so we roll our own (512/1024 point
    // at 30 fps is trivial for any desktop CPU).
    internal static class Fft
    {
        // Returns magnitudes[0..n/2-1] of the Hann-windowed real input (n = power of two).
        public static float[] Magnitudes(float[] samples)
        {
            int n = samples.Length;
            var re = new float[n];
            var im = new float[n];

            double hannDiv = Math.Max(1, n - 1);
            for (int i = 0; i < n; i++)
            {
                double w = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / hannDiv);
                re[i] = (float)(samples[i] * w);
            }

            // Bit-reversal permutation
            int j = 0;
            for (int i = 1; i < n; i++)
            {
                int bit = n >> 1;
                while ((j & bit) != 0 && bit > 0)
                {
                    j ^= bit;
                    bit >>= 1;
                }
                j ^= bit;
                if (i < j)
                {
                    float tr = re[i]; re[i] = re[j]; re[j] = tr;
                    float ti = im[i]; im[i] = im[j]; im[j] = ti;
                }
            }

            // In-place butterflies
            for (int len = 2; len <= n; len <<= 1)
            {
                double angle = -2.0 * Math.PI / len;
                double wR = Math.Cos(angle);
                double wI = Math.Sin(angle);
                for (int i = 0; i < n; i += len)
                {
                    double curR = 1.0, curI = 0.0;
                    int half = len >> 1;
                    for (int k = 0; k < half; k++)
                    {
                        int a = i + k;
                        int b = i + k + half;
                        float tR = (float)(re[b] * curR - im[b] * curI);
                        float tI = (float)(re[b] * curI + im[b] * curR);
                        re[b] = re[a] - tR;
                        im[b] = im[a] - tI;
                        re[a] += tR;
                        im[a] += tI;
                        double ncR = curR * wR - curI * wI;
                        curI = curR * wI + curI * wR;
                        curR = ncR;
                    }
                }
            }

            var mag = new float[n / 2];
            for (int i = 0; i < n / 2; i++)
                mag[i] = (float)Math.Sqrt(re[i] * re[i] + im[i] * im[i]);
            return mag;
        }

        // Map FFT magnitudes (Nyquist = maxFreq) into `bandCount` logarithmically
        // spaced bins, each bin taking the max magnitude in its range. Normalized
        // against `norm` (>0) and clamped to 0..1.
        public static void ToBands(float[] mag, float nyquist, int bandCount, float norm, float[] bands)
        {
            float fMin = 20f;
            float fMax = Math.Max(fMin + 1f, nyquist * 0.95f);
            int magCount = mag.Length;
            float binPerHz = (float)magCount / nyquist;

            for (int b = 0; b < bandCount; b++)
            {
                float t = (float)b / Math.Max(1, bandCount - 1);
                float fLow = fMin * (float)Math.Pow(fMax / fMin, t);
                float fHigh = fMin * (float)Math.Pow(fMax / fMin, t + 1.0f / Math.Max(1, bandCount - 1));
                int i0 = Math.Max(1, (int)(fLow * binPerHz));
                int i1 = Math.Min(magCount, (int)(fHigh * binPerHz) + 1);
                float peak = 0f;
                for (int i = i0; i < i1; i++)
                    if (mag[i] > peak) peak = mag[i];
                float v = (norm > 0f) ? peak / norm : 0f;
                if (v > 1f) v = 1f;
                bands[b] = v;
            }
        }
    }
}
