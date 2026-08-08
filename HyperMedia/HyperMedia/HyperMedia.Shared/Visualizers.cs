using System;
using Windows.UI;

namespace HyperMedia
{
    public interface IVisualizerRenderer
    {
        string Name { get; }
        void Render(byte[] pixels, int width, int height, SpectrumSample data, float beatPulse, Color theme, double timeMs);
    }

    // Shared pixel helpers (BGRA order for WriteableBitmap).
    internal static class PixelDraw
    {
        public static void Clear(byte[] px, int width, int height, byte r, byte g, byte b)
        {
            int n = width * height * 4;
            for (int i = 0; i < n; i += 4)
            {
                px[i] = b;
                px[i + 1] = g;
                px[i + 2] = r;
                px[i + 3] = 255;
            }
        }

        public static void SetPixel(byte[] px, int width, int height, int x, int y, byte r, byte g, byte b, byte a)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return;
            int i = (y * width + x) * 4;
            if (a >= 250)
            {
                px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = 255;
            }
            else
            {
                // alpha blend over existing pixel
                px[i] = (byte)((b * a + px[i] * (255 - a)) / 255);
                px[i + 1] = (byte)((g * a + px[i + 1] * (255 - a)) / 255);
                px[i + 2] = (byte)((r * a + px[i + 2] * (255 - a)) / 255);
                px[i + 3] = 255;
            }
        }

        public static void FillRect(byte[] px, int width, int height, int x0, int y0, int x1, int y1, byte r, byte g, byte b, byte a)
        {
            if (x0 < 0) x0 = 0;
            if (y0 < 0) y0 = 0;
            if (x1 >= width) x1 = width - 1;
            if (y1 >= height) y1 = height - 1;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    SetPixel(px, width, height, x, y, r, g, b, a);
        }

        public static void FillCircle(byte[] px, int width, int height, int cx, int cy, float radius, byte r, byte g, byte b, byte a)
        {
            int r2 = (int)Math.Ceiling(radius);
            for (int y = cy - r2; y <= cy + r2; y++)
            {
                for (int x = cx - r2; x <= cx + r2; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy <= radius * radius)
                        SetPixel(px, width, height, x, y, r, g, b, a);
                }
            }
        }

        public static void DrawLine(byte[] px, int width, int height, float x0, float y0, float x1, float y1, byte r, byte g, byte b, byte a)
        {
            float dx = x1 - x0, dy = y1 - y0;
            float steps = Math.Max(1f, Math.Max(Math.Abs(dx), Math.Abs(dy)));
            for (int i = 0; i <= steps; i++)
            {
                float t = i / steps;
                SetPixel(px, width, height, (int)(x0 + dx * t), (int)(y0 + dy * t), r, g, b, a);
            }
        }

        public static Color Blend(Color from, Color to, float t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return Color.FromArgb(255,
                (byte)(from.R + (to.R - from.R) * t),
                (byte)(from.G + (to.G - from.G) * t),
                (byte)(from.B + (to.B - from.B) * t));
        }
    }

    // 1. Classic equalizer bars
    internal sealed class BarsRenderer : IVisualizerRenderer
    {
        public string Name { get { return "bars"; } }
        private float[] _peak = new float[SpectrumEngine.BandCount];

        public void Render(byte[] px, int width, int height, SpectrumSample data, float beatPulse, Color theme, double timeMs)
        {
            PixelDraw.Clear(px, width, height, 8, 8, 14);

            int count = SpectrumEngine.BandCount;
            float gap = 3f;
            float barW = (width - gap * (count + 1)) / count;
            int baseY = height - 24;
            float maxH = (height - 48) * 0.88f;

            var topColor = Color.FromArgb(255, 224, 64, 251);      // Zune purple
            var lowColor = PixelDraw.Blend(theme, Color.FromArgb(255, 40, 40, 70), 0.4f);
            var midColor = PixelDraw.Blend(theme, Color.FromArgb(255, 90, 90, 160), 0.25f);

            float pulse = 1f + beatPulse * 0.35f;
            for (int i = 0; i < count; i++)
            {
                float v = data.Bands[i] * pulse;
                if (v > _peak[i]) _peak[i] = Math.Min(v, _peak[i] + 0.045f);
                else _peak[i] = Math.Max(0f, _peak[i] - 0.014f);

                float h = _peak[i] * maxH;
                int x0 = (int)(gap + i * (barW + gap));
                int x1 = (int)(x0 + barW);
                int y0 = (int)(baseY - h);

                Color c = (i < count / 3) ? lowColor : (i < 2 * count / 3) ? midColor : topColor;
                PixelDraw.FillRect(px, width, height, x0, y0, x1, baseY, c.R, c.G, c.B, 230);
                PixelDraw.FillRect(px, width, height, x0, Math.Max(0, y0 - 2), x1, y0, 255, 255, 255, (byte)(60 + 40 * v));
            }
        }
    }

    // 2. Mirror symmetric bars from the centre
    internal sealed class SymmetryRenderer : IVisualizerRenderer
    {
        public string Name { get { return "symmetry"; } }
        private float[] _peak = new float[SpectrumEngine.BandCount];

        public void Render(byte[] px, int width, int height, SpectrumSample data, float beatPulse, Color theme, double timeMs)
        {
            PixelDraw.Clear(px, width, height, 5, 7, 16);

            int cx = width / 2;
            int count = SpectrumEngine.BandCount / 2;
            float gap = 4f;
            float halfSpan = width * 0.42f;
            float barW = (halfSpan - gap * count) / count;
            int baseY = height - 20;
            float maxH = height * 0.62f;
            float pulse = 1f + beatPulse * 0.3f;

            for (int i = 0; i < count; i++)
            {
                int bandIdx = count - 1 - i;
                float v = data.Bands[bandIdx] * pulse;
                if (v > _peak[bandIdx]) _peak[bandIdx] = Math.Min(v, _peak[bandIdx] + 0.045f);
                else _peak[bandIdx] = Math.Max(0f, _peak[bandIdx] - 0.012f);
                float h = _peak[bandIdx] * maxH;

                int x0l = (int)(cx - gap - (i + 1) * (barW + gap));
                int x1l = (int)(cx - gap - i * (barW + gap));
                int x0r = (int)(cx + gap + i * (barW + gap));
                int x1r = (int)(cx + gap + (i + 1) * (barW + gap));

                Color c = PixelDraw.Blend(theme, Color.FromArgb(255, 224, 64, 251), i / (float)Math.Max(1, count - 1));
                int y0 = (int)(baseY - h);
                PixelDraw.FillRect(px, width, height, x0l, y0, x1l, baseY, c.R, c.G, c.B, 230);
                PixelDraw.FillRect(px, width, height, x0r, y0, x1r, baseY, c.R, c.G, c.B, 230);
            }

            // centre glow line pulsing with the beat
            float glow = 0.5f + beatPulse * 0.5f;
            PixelDraw.FillRect(px, width, height, cx - 2, 0, cx + 2, height,
                (byte)(theme.R * glow + 30), (byte)(theme.G * glow + 20), (byte)(theme.B * glow + 80), (byte)(30 + 90 * glow));
        }
    }

    // 3. Ring spectrum
    internal sealed class RingRenderer : IVisualizerRenderer
    {
        public string Name { get { return "ring"; } }
        private float[] _peak = new float[SpectrumEngine.BandCount];

        public void Render(byte[] px, int width, int height, SpectrumSample data, float beatPulse, Color theme, double timeMs)
        {
            PixelDraw.Clear(px, width, height, 6, 6, 10);

            int cx = width / 2, cy = height / 2;
            float baseR = Math.Min(width, height) * 0.22f;
            float pulse = 1f + beatPulse * 0.12f;
            int count = SpectrumEngine.BandCount;

            for (int i = 0; i < count; i++)
            {
                float v = data.Bands[i];
                if (v > _peak[i]) _peak[i] = Math.Min(v, _peak[i] + 0.045f);
                else _peak[i] = Math.Max(0f, _peak[i] - 0.010f);

                float angle = (float)(i * 2.0 * Math.PI / count);
                float len = _peak[i] * Math.Min(width, height) * 0.22f + 6;
                float r0 = baseR * pulse;
                float r1 = r0 + len;

                float ca = (float)Math.Cos(angle), sa = (float)Math.Sin(angle);
                float x0 = cx + ca * r0, y0 = cy + sa * r0;
                float x1 = cx + ca * r1, y1 = cy + sa * r1;

                Color c = PixelDraw.Blend(theme, Color.FromArgb(255, 224, 64, 251), (float)i / count);
                PixelDraw.DrawLine(px, width, height, x0, y0, x1, y1, c.R, c.G, c.B, 230);
            }

            // hub glow
            PixelDraw.FillCircle(px, width, height, cx, cy, baseR * 0.25f * pulse + 2,
                (byte)(theme.R * 0.6 + 40), (byte)(theme.G * 0.6 + 30), (byte)(theme.B * 0.6 + 90), (byte)(60 + 120 * beatPulse));
        }
    }

    // 4. BPM-driven particles
    internal sealed class ParticlesRenderer : IVisualizerRenderer
    {
        public string Name { get { return "particles"; } }
        private const int MaxParticles = 220;
        private readonly float[] _px = new float[MaxParticles];
        private readonly float[] _py = new float[MaxParticles];
        private readonly float[] _pvx = new float[MaxParticles];
        private readonly float[] _pvy = new float[MaxParticles];
        private readonly float[] _plife = new float[MaxParticles];
        private readonly byte[] _pr = new byte[MaxParticles];
        private readonly byte[] _pg = new byte[MaxParticles];
        private readonly byte[] _pb = new byte[MaxParticles];
        private readonly Random _rnd = new Random();
        private int _next;

        public void Render(byte[] px, int width, int height, SpectrumSample data, float beatPulse, Color theme, double timeMs)
        {
            PixelDraw.Clear(px, width, height, 4, 4, 12);

            int cx = width / 2, cy = height / 2;
            float burst = 1f + beatPulse * 1.6f;
            int spawn = (int)(2 + beatPulse * 14);

            for (int i = 0; i < MaxParticles; i++)
            {
                if (_plife[i] <= 0)
                {
                    if (spawn > 0)
                    {
                        spawn--;
                        float angle = (float)(_rnd.NextDouble() * 2 * Math.PI);
                        float speed = (float)(0.6 + _rnd.NextDouble() * 2.4) * burst;
                        _px[i] = cx;
                        _py[i] = cy;
                        _pvx[i] = (float)Math.Cos(angle) * speed;
                        _pvy[i] = (float)Math.Sin(angle) * speed;
                        _plife[i] = (float)(0.6 + _rnd.NextDouble() * 1.6);
                        Color c = PixelDraw.Blend(theme, Color.FromArgb(255, 224, 64, 251), (float)_rnd.NextDouble());
                        _pr[i] = c.R; _pg[i] = c.G; _pb[i] = c.B;
                    }
                    else continue;
                }

                float env = data.Envelope * 1.8f + 0.15f;
                _px[i] += _pvx[i] * env;
                _py[i] += _pvy[i] * env;
                _pvx[i] *= 0.985f;
                _pvy[i] *= 0.985f;
                _plife[i] -= 0.016f;

                float fade = Math.Min(1f, _plife[i]);
                PixelDraw.FillCircle(px, width, height, (int)_px[i], (int)_py[i], 1.6f + data.Envelope * 2.2f,
                    _pr[i], _pg[i], _pb[i], (byte)(180 * fade));
            }

            // core that pulses with the beat
            float coreR = 12f + beatPulse * 46f + data.Envelope * 26f;
            PixelDraw.FillCircle(px, width, height, cx, cy, coreR,
                (byte)(theme.R * 0.8 + 40), (byte)(theme.G * 0.8 + 30), (byte)(theme.B * 0.8 + 90), (byte)(70 + 150 * beatPulse));
        }
    }

    // 5. Neon window: gradient backdrop + mirrored spectrum floor
    internal sealed class NebulaRenderer : IVisualizerRenderer
    {
        public string Name { get { return "nebula"; } }
        private float[] _peak = new float[SpectrumEngine.BandCount];
        private float[] _stars = new float[160];
        private float[] _starY = new float[160];
        private float[] _starP = new float[160];
        private readonly Random _rnd = new Random();

        public void Render(byte[] px, int width, int height, SpectrumSample data, float beatPulse, Color theme, double timeMs)
        {
            // static vertical gradient
            for (int y = 0; y < height; y++)
            {
                float t = (float)y / Math.Max(1, height - 1);
                byte r = (byte)(10 + (theme.R * 0.35f + 20) * t);
                byte g = (byte)(10 + (theme.G * 0.25f + 18) * t);
                byte b = (byte)(24 + (theme.B * 0.55f + 30) * t);
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width + x) * 4;
                    px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = 255;
                }
            }

            // twinkling stars
            int stars = _stars.Length;
            if (_stars[0] == 0) // lazy init once
            {
                for (int i = 0; i < stars; i++)
                {
                    _stars[i] = (float)_rnd.NextDouble();
                    _starY[i] = (float)_rnd.NextDouble() * height * 0.6f;
                    _starP[i] = (float)(_rnd.NextDouble() * 2 * Math.PI);
                }
            }
            for (int i = 0; i < stars; i++)
            {
                float tw = (float)(0.3 + 0.7 * (0.5 + 0.5 * Math.Sin(timeMs / 700.0 + _starP[i])));
                int sx = (int)(_stars[i] * width);
                int sy = (int)_starY[i];
                byte a = (byte)(40 + 150 * tw);
                PixelDraw.SetPixel(px, width, height, sx, sy, 255, 255, 255, a);
            }

            // mirrored spectrum floor
            int count = SpectrumEngine.BandCount;
            float gap = 3f;
            float barW = (width - gap * (count + 1)) / count;
            int baseY = height - 20;
            float maxH = height * 0.34f;
            float pulse = 1f + beatPulse * 0.3f;

            for (int i = 0; i < count; i++)
            {
                float v = data.Bands[i] * pulse;
                if (v > _peak[i]) _peak[i] = Math.Min(v, _peak[i] + 0.045f);
                else _peak[i] = Math.Max(0f, _peak[i] - 0.012f);
                float h = _peak[i] * maxH;

                int x0 = (int)(gap + i * (barW + gap));
                int x1 = (int)(x0 + barW);
                int y0 = (int)(baseY - h);
                int y1 = baseY;
                Color c = PixelDraw.Blend(theme, Color.FromArgb(255, 224, 64, 251), (float)i / count);
                PixelDraw.FillRect(px, width, height, x0, y0, x1, y1, c.R, c.G, c.B, 200);
                // mirror
                int my0 = baseY + (baseY - y0);
                if (my0 < height)
                    PixelDraw.FillRect(px, width, height, x0, y1 + 2, x1, my0, c.R, c.G, c.B, (byte)(70 + 40 * (1 - _peak[i])));
            }

            // horizon glow
            PixelDraw.FillRect(px, width, height, 0, baseY, width, baseY + 2,
                (byte)(theme.R * 0.9 + 30), (byte)(theme.G * 0.9 + 20), (byte)(theme.B * 0.9 + 90), (byte)(80 + 120 * beatPulse));
        }
    }

    // 6. Album-hue breathing orb
    internal sealed class AlbumHueRenderer : IVisualizerRenderer
    {
        public string Name { get { return "albumhue"; } }
        private readonly float[] _peak = new float[16];

        public void Render(byte[] px, int width, int height, SpectrumSample data, float beatPulse, Color theme, double timeMs)
        {
            PixelDraw.Clear(px, width, height, 7, 7, 12);

            int cx = width / 2, cy = height / 2;
            float breath = (float)(1 + 0.10 * Math.Sin(timeMs / 1200.0));
            float orbR = Math.Min(width, height) * 0.30f * breath * (1f + beatPulse * 0.18f);

            // halo
            PixelDraw.FillCircle(px, width, height, cx, cy, orbR * 1.35f,
                (byte)(theme.R * 0.5), (byte)(theme.G * 0.5), (byte)(theme.B * 0.5), (byte)(24 + 40 * beatPulse));
            // orb
            PixelDraw.FillCircle(px, width, height, cx, cy, orbR,
                theme.R, theme.G, theme.B, (byte)(80 + 90 * beatPulse));
            // highlight
            PixelDraw.FillCircle(px, width, height, cx - (int)(orbR * 0.25f), cy - (int)(orbR * 0.3f), orbR * 0.45f,
                255, 255, 255, (byte)(24 + 30 * data.Envelope));

            // slim bottom bars
            int count = 16;
            float gap = 4f;
            float barW = (width - gap * (count + 1)) / count;
            int baseY = height - 26;
            float maxH = height * 0.18f;
            for (int i = 0; i < count; i++)
            {
                float v = data.Bands[(int)(i * SpectrumEngine.BandCount / (float)count)] * (1f + beatPulse * 0.25f);
                if (v > _peak[i]) _peak[i] = Math.Min(v, _peak[i] + 0.045f);
                else _peak[i] = Math.Max(0f, _peak[i] - 0.016f);
                int x0 = (int)(gap + i * (barW + gap));
                int x1 = (int)(x0 + barW);
                int y0 = (int)(baseY - _peak[i] * maxH);
                PixelDraw.FillRect(px, width, height, x0, y0, x1, baseY,
                    (byte)(theme.R * 0.7 + 60), (byte)(theme.G * 0.7 + 40), (byte)(theme.B * 0.7 + 110), 220);
            }
        }
    }

    // 7. Lissajous oscilloscope
    internal sealed class WaveRenderer : IVisualizerRenderer
    {
        public string Name { get { return "wave"; } }

        public void Render(byte[] px, int width, int height, SpectrumSample data, float beatPulse, Color theme, double timeMs)
        {
            PixelDraw.Clear(px, width, height, 2, 2, 6);

            int cx = width / 2, cy = height / 2;
            float amp = Math.Min(width, height) * 0.34f * (0.5f + data.Envelope * 0.5f + beatPulse * 0.2f);
            double t = timeMs / 1000.0;
            float f1 = 1.3f + data.Mid * 2.2f;
            float f2 = 1.1f + data.High * 1.8f;

            int steps = 260;
            Color prev = default(Color);
            int prevX = 0, prevY = 0;
            for (int i = 0; i <= steps; i++)
            {
                double tt = t + i / (double)steps * 2.0 * Math.PI;
                float x = cx + (float)(amp * Math.Sin(f1 * tt));
                float y = cy + (float)(amp * Math.Sin(f2 * tt + 0.7 * Math.Sin(t * 1.4)));
                Color c = PixelDraw.Blend(theme, Color.FromArgb(255, 224, 64, 251), (float)i / steps);
                if (i > 0)
                    PixelDraw.DrawLine(px, width, height, prevX, prevY, x, y, c.R, c.G, c.B, 235);
                prev = c;
                prevX = (int)x;
                prevY = (int)y;
            }

            // faint guide circle
            PixelDraw.FillCircle(px, width, height, cx, cy, amp * 1.02f,
                (byte)(theme.R * 0.4), (byte)(theme.G * 0.4), (byte)(theme.B * 0.4), 16);
        }
    }
}
