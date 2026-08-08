using System;

namespace HyperMedia
{
    // A single snapshot of analyser output, consumed by the renderers.
    public sealed class SpectrumSample
    {
        public float[] Bands = new float[SpectrumEngine.BandCount];
        public float Low;
        public float Mid;
        public float High;
        public float Envelope;
        public float BeatPulse;
    }

    // WP8.1: MediaElement exposes no raw PCM, so the visualizer drives a
    // self-animating faux source on the UI thread (30 fps timer). This mirrors
    // the desktop FauxSpectrum fallback so the renderers behave identically.
    public sealed class SpectrumEngine
    {
        public const int BandCount = 48;

        private readonly Random _rnd = new Random();
        private float[] _smooth = new float[BandCount];
        private double _time;
        private bool _playing;
        private float _beatPulse;
        private float _drive = 1f;
        private readonly float[] _smoothOut = new float[BandCount];
        private float _smoothEnv, _smoothLow, _smoothMid, _smoothHigh;
        private SpectrumSample _current = new SpectrumSample();

        public void SetPlaying(bool playing) { _playing = playing; }

        public void SetDrive(float amplitude) { _drive = Math.Max(0f, Math.Min(1.5f, amplitude)); }

        public SpectrumSample Next(double timeMs)
        {
            var sample = new SpectrumSample();
            int n = BandCount;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Math.Max(1, n - 1);
                double wobble = Math.Sin(_time * (1.2 + t * 3.1)) * 0.4;
                float v = (float)((0.35 + 0.55 * Math.Abs(Math.Sin(_time * 0.8 + t * 5.0))) * 0.6
                    + wobble * 0.5 + _rnd.NextDouble() * 0.2);
                v = Math.Min(1f, Math.Max(0.04f, v));
                v *= _drive;
                sample.Bands[i] = _smooth[i] = (_smooth[i] * 0.7f + v * 0.3f);
            }
            sample.Low = sample.Bands[5];
            sample.Mid = sample.Bands[n / 2];
            sample.High = sample.Bands[n - 4];
            sample.Envelope = 0.5f + 0.5f * (float)Math.Abs(Math.Sin(_time * 1.1));
            sample.BeatPulse = (_time * 2.2f % (2.0 * Math.PI) > 4.3 && _time * 2.2f % (2.0 * Math.PI) < 4.7) ? 1f : 0f;

            var frame = sample;
            if (frame.BeatPulse > _beatPulse) _beatPulse = 1f;
            else _beatPulse = Math.Max(0f, _beatPulse - 0.05f);
            frame.BeatPulse = _beatPulse;
            if (!_playing)
            {
                frame.BeatPulse = 0f;
            }
            else
            {
                const float dead = 0.012f;
                for (int i = 0; i < frame.Bands.Length; i++)
                {
                    float d = frame.Bands[i] - _smoothOut[i];
                    if (Math.Abs(d) < dead) frame.Bands[i] = _smoothOut[i];
                    else { _smoothOut[i] += d * 0.6f; frame.Bands[i] = _smoothOut[i]; }
                }
                FilterDeadband(ref _smoothEnv, ref frame.Envelope, dead * 2f);
                FilterDeadband(ref _smoothLow, ref frame.Low, dead);
                FilterDeadband(ref _smoothMid, ref frame.Mid, dead);
                FilterDeadband(ref _smoothHigh, ref frame.High, dead);
            }

            _current = frame;
            _time += 1.0 / 30.0;
            return _current;
        }

        private static void FilterDeadband(ref float smooth, ref float value, float dead)
        {
            float d = value - smooth;
            if (Math.Abs(d) < dead) value = smooth;
            else { smooth += d * 0.6f; value = smooth; }
        }
    }
}