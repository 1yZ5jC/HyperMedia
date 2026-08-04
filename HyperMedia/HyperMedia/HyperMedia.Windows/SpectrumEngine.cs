using System;
using System.Threading;
using System.Threading.Tasks;
using HyperMedia.MediaCore;
using Windows.Storage;

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

    internal interface ISpectrumSource
    {
        string Name { get; }
        bool Available { get; }
        void Start();
        void Stop();
        // Called on the engine loop thread; returns null to keep the previous frame.
        SpectrumSample Poll(double trackTimeSec, bool playing);
        void NotifySeek(double seconds);
    }

    // ---- Source A: live PCM via a silent second libVLC instance ----
    internal sealed class RealtimeSpectrum : ISpectrumSource
    {
        public const int FftSize = 1024;

        private readonly string _path;
        private LibVlcDecoder _decoder;
        private long _samplesDecoded;
        private float[] _normSmooth = new float[SpectrumEngine.BandCount];
        private bool _openFailed;
        private float _curveMax;
        private bool _decWasPlaying;

        public RealtimeSpectrum(string path) { _path = path; }
        public string Name { get { return "realtime"; } }
        public bool Available { get { return _decoder != null && !_openFailed; } }

        public void Start()
        {
            Task.Run(() =>
            {
                try
                {
                    var dec = new LibVlcDecoder();
                    if (!dec.OpenFile(_path) || !dec.HasAudio)
                    {
                        dec.Close();
                        _openFailed = true;
                        return;
                    }
                    _decoder = dec;
                    _samplesDecoded = 0;
                    SpectrumEngine.DebugLog("[HyperMedia] RealtimeSpectrum ready");
                }
                catch { _openFailed = true; }
            });
        }

        public void Stop()
        {
            var dec = _decoder;
            _decoder = null;
            if (dec != null)
            {
                try { dec.Close(); } catch { }
            }
        }

        public SpectrumSample Poll(double trackTimeSec, bool playing)
        {
            var dec = _decoder;
            if (dec == null || _openFailed) return null;

            // Track the main player's play state in the analyser so the two never
            // drift apart: pause the silent decoder on pause, re-align on resume.
            if (playing != _decWasPlaying)
            {
                try
                {
                    if (playing)
                    {
                        dec.SeekTo(trackTimeSec);
                        dec.SetPlayPause(true);
                        _samplesDecoded = (long)(trackTimeSec * 44100);
                    }
                    else
                    {
                        dec.SetPlayPause(false);
                    }
                    _decWasPlaying = playing;
                }
                catch { }
            }
            if (!playing) return null;

            try
            {
                DecodedAudioFrame frame = null;
                for (int i = 0; i < 3; i++)
                {
                    frame = dec.ReadNextAudioFrame();
                    if (frame != null) break;
                }
                if (frame == null) return null;

                _samplesDecoded += frame.SampleCount;
                double selfTime = (double)_samplesDecoded / frame.SampleRate;

                // Drift correction against the main player
                if (Math.Abs(selfTime - trackTimeSec) > 0.8 && trackTimeSec > 0)
                {
                    try
                    {
                        dec.SeekTo(trackTimeSec);
                        _samplesDecoded = (long)(trackTimeSec * frame.SampleRate);
                    }
                    catch { }
                }

                int channels = frame.Channels;
                int sampleCount = frame.SampleCount;
                int monoCount = (channels > 0) ? sampleCount : 0;
                if (monoCount < FftSize) return null;

                var window = new float[FftSize];
                byte[] data = frame.Data;
                long acc = 0;
                for (int i = 0; i < FftSize; i++)
                {
                    // interleaved S16N -> mixed mono (average channels)
                    acc = 0;
                    int baseIdx = i * channels * 2;
                    for (int c = 0; c < channels; c++)
                    {
                        int idx = baseIdx + c * 2;
                        if (idx + 1 < data.Length)
                            acc += (short)(data[idx] | (data[idx + 1] << 8));
                    }
                    window[i] = (float)(acc / (double)Math.Max(1, channels) / 32768.0);
                }

                float[] mag = Fft.Magnitudes(window);
                float nyquist = frame.SampleRate / 2f;

                var sample = new SpectrumSample();
                float peakNorm = 1f;
                Fft.ToBands(mag, nyquist, SpectrumEngine.BandCount, peakNorm, sample.Bands);

                // Self-scaling normalisation: track the running frame MAX instead of a
                // fixed norm or a straight-line tilt, so the true spectral curve shape
                // is shown while loudness levels itself.
                int nb = sample.Bands.Length;
                float frameMax = 0f;
                for (int i = 0; i < nb; i++)
                    if (sample.Bands[i] > frameMax) frameMax = sample.Bands[i];
                if (frameMax > _curveMax) _curveMax = frameMax;
                else _curveMax *= 0.9995f;
                float denom = Math.Max(0.02f, _curveMax);
                for (int i = 0; i < nb; i++)
                    sample.Bands[i] = Math.Min(1f, sample.Bands[i] / denom);

                // Log-ish smoothing on top of the FFT bands
                float rms = 0f;
                for (int i = 0; i < sample.Bands.Length; i++)
                {
                    float v = sample.Bands[i];
                    sample.Bands[i] = _normSmooth[i] = (_normSmooth[i] * 0.35f + v * 0.65f);
                    rms += v * v;
                }
                rms = (float)Math.Sqrt(rms / sample.Bands.Length);

                float low = 0f, mid = 0f, high = 0f;
                int n = sample.Bands.Length;
                for (int i = 0; i < n; i++)
                {
                    if (i < n / 3) low = Math.Max(low, sample.Bands[i]);
                    else if (i < 2 * n / 3) mid = Math.Max(mid, sample.Bands[i]);
                    else high = Math.Max(high, sample.Bands[i]);
                }
                sample.Low = low;
                sample.Mid = mid;
                sample.High = high;
                sample.Envelope = Math.Min(1f, rms * 2.2f);
                return sample;
            }
            catch
            {
                return null;
            }
        }

        public void NotifySeek(double seconds)
        {
            var dec = _decoder;
            if (dec == null) return;
            try
            {
                dec.SeekTo(seconds);
                _samplesDecoded = (long)(seconds * 44100);
            }
            catch { }
        }
    }

    // ---- Source B: pre-scanned envelope (fast offline pass, cached) ----
    internal sealed class WaveformSpectrum : ISpectrumSource
    {
        private readonly string _path;
        private double _durationSec;
        private float[] _wave;          // 4 floats per 50ms frame: low, mid, high, env
        private float[] _smooth = new float[SpectrumEngine.BandCount];
        private double _bpm = 96.0;
        private double _beatPhase;
        private float _lastEnv;
        private float _envAvg;
        private float _curveMax;
        private bool _scanning;

        public WaveformSpectrum(string path, double durationSec)
        {
            _path = path;
            _durationSec = durationSec;
        }

        public string Name { get { return "waveform"; } }
        public bool Available { get { return _wave != null; } }

        public void Start()
        {
            _scanning = true;
            Task.Run(async () =>
            {
                try
                {
                    _wave = await WaveformCache.LoadAsync(_path);
                    if (_wave == null)
                    {
                        var scan = await Task.Run(() =>
                        {
                            try
                            {
                                var dec = new LibVlcDecoder();
                                try
                                {
                                    if (!dec.OpenFile(_path) || !dec.HasAudio) return null;
                                    return dec.ScanWaveform();
                                }
                                finally { dec.Close(); }
                            }
                            catch { return null; }
                        });
                        if (scan == null || scan.Length < 8)
                        {
                            _wave = null;
                            return;
                        }
                        _wave = new float[scan.Length];
                        Array.Copy(scan, _wave, scan.Length);
                        await WaveformCache.SaveAsync(_path, _wave);
                    }
                    EstimateBpm();
                    SpectrumEngine.DebugLog("[HyperMedia] WaveformSpectrum ready, {0} frames, bpm={1:F0}",
                        (_wave.Length / 4).ToString(), _bpm);
                }
                catch { _wave = null; }
                finally { _scanning = false; }
            });
        }

        public void Stop() { }

        public void SetDuration(double seconds) { _durationSec = seconds; }

        public void NotifySeek(double seconds) { }

        private void EstimateBpm()
        {
            if (_wave == null || _wave.Length < 4 * 20) return;
            int frames = _wave.Length / 4;
            var env = new float[frames];
            for (int i = 0; i < frames; i++) env[i] = _wave[i * 4 + 3];

            int minLag = 12;   // 0.6 s -> 100 bpm
            int maxLag = 30;   // 1.5 s -> 40 bpm
            double bestScore = -1;
            int bestLag = 18;
            for (int lag = minLag; lag <= maxLag; lag++)
            {
                double score = 0;
                int pairs = frames - lag;
                if (pairs <= 0) break;
                for (int i = 0; i < pairs; i++)
                    score += (env[i] - 0.5) * (env[i + lag] - 0.5);
                score /= pairs;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestLag = lag;
                }
            }
            if (bestScore > 0.002)
                _bpm = 60.0 / (bestLag / 20.0);
        }

        public SpectrumSample Poll(double trackTimeSec, bool playing)
        {
            if (_wave == null) return null;
            int frames = _wave.Length / 4;
            if (frames == 0) return null;

            double t = Math.Max(0, Math.Min(_durationSec, trackTimeSec));
            double pos = t * 20.0;
            int i0 = (int)Math.Floor(pos);
            int i1 = i0 + 1;
            double frac = pos - i0;
            if (i0 < 0) { i0 = 0; i1 = 1; frac = 0; }
            if (i0 >= frames - 1) { i0 = frames - 1; i1 = frames - 1; frac = 0; }
            if (i1 >= frames) i1 = frames - 1;

            int a = i0 * 4, b = i1 * 4;
            float low = Lerp(_wave[a], _wave[b], (float)frac);
            float mid = Lerp(_wave[a + 1], _wave[b + 1], (float)frac);
            float high = Lerp(_wave[a + 2], _wave[b + 2], (float)frac);
            float env = Lerp(_wave[a + 3], _wave[b + 3], (float)frac);

            var sample = new SpectrumSample();
            int n = SpectrumEngine.BandCount;
            for (int i = 0; i < n; i++)
            {
                float t2 = (float)i / Math.Max(1, n - 1);
                // blend of the three bands shaped across the bar spectrum; frame-relative
                // normalisation below keeps the real curve shape at any loudness.
                float shape = low * (1 - t2) + mid * 0.6f + high * t2;
                float v = shape;
                if (v > _curveMax) _curveMax = v;
                else _curveMax *= 0.9995f;
                if (_curveMax > 0.02f) v = Math.Min(1f, v / _curveMax);
                if (v > 1f) v = 1f;
                sample.Bands[i] = _smooth[i] = (_smooth[i] * 0.3f + v * 0.7f);
            }
            sample.Low = Math.Min(1f, low * 0.6f);
            sample.Mid = Math.Min(1f, mid * 0.5f);
            sample.High = Math.Min(1f, high * 0.55f);
            sample.Envelope = Math.Min(1f, env * 1.4f);

            // beat from envelope rise
            _envAvg = _envAvg * 0.90f + env * 0.10f;
            if (env > _envAvg * 1.35f && env > 0.25f && _lastEnv <= env)
                sample.BeatPulse = 1f;
            _lastEnv = env;

            // steady beat from BPM (fallback for percussion-free sections)
            if (_bpm > 0)
            {
                double period = 60.0 / _bpm;
                double phase = t % period / period;
                sample.BeatPulse = Math.Max(sample.BeatPulse,
                    phase < 0.06 ? (float)(1 - phase / 0.06) : 0f);
            }
            return sample;
        }

        private static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
    }

    // ---- Source C: fake spectrum, last-resort fallback ----
    internal sealed class FauxSpectrum : ISpectrumSource
    {
        private readonly Random _rnd = new Random();
        private float[] _smooth = new float[SpectrumEngine.BandCount];
        private double _time;

        public string Name { get { return "faux"; } }
        public bool Available { get { return true; } }
        public void Start() { }
        public void Stop() { }
        public void NotifySeek(double seconds) { }

        public SpectrumSample Poll(double trackTimeSec, bool playing)
        {
            _time += 1.0 / 30.0;
            var sample = new SpectrumSample();
            int n = SpectrumEngine.BandCount;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Math.Max(1, n - 1);
                double wobble = Math.Sin(_time * (1.2 + t * 3.1)) * 0.4;
                float v = (float)((0.35 + 0.55 * Math.Abs(Math.Sin(_time * 0.8 + t * 5.0))) * 0.6
                    + wobble * 0.5 + _rnd.NextDouble() * 0.2);
                v = Math.Min(1f, Math.Max(0.04f, v));
                sample.Bands[i] = _smooth[i] = (_smooth[i] * 0.7f + v * 0.3f);
            }
            sample.Low = sample.Bands[5];
            sample.Mid = sample.Bands[n / 2];
            sample.High = sample.Bands[n - 4];
            sample.Envelope = 0.5f + 0.5f * (float)Math.Abs(Math.Sin(_time * 1.1));
            sample.BeatPulse = (Math.Sin(_time * 2.2) > 0.9) ? 1f : 0f;
            return sample;
        }
    }

    // ---- Engine: owns sources, runs the analysis loop, exposes the snapshot ----
    public sealed class SpectrumEngine : IDisposable
    {
        internal static void DebugLog(string format, params object[] args)
        {
            System.Diagnostics.Debug.WriteLine(format, args);
        }

        public const int BandCount = 48;

        private readonly object _sync = new object();
        private ISpectrumSource _active;
        private RealtimeSpectrum _realtime;
        private WaveformSpectrum _waveform;
        private FauxSpectrum _faux = new FauxSpectrum();
        private CancellationTokenSource _cts;
        private Task _loopTask;
        private SpectrumSample _current = new SpectrumSample();
        private float _beatPulse;
        private double _trackTime;
        private bool _playing;
        private readonly float[] _smoothOut = new float[BandCount];
        private float _smoothEnv, _smoothLow, _smoothMid, _smoothHigh;

        public SpectrumSample Current { get { lock (_sync) { return _current; } } }
        public float BeatPulse { get { lock (_sync) { return _beatPulse; } } }
        public bool IsPlaying { get { lock (_sync) { return _playing; } } }
        public string ActiveSourceName
        {
            get { lock (_sync) { return _active != null ? _active.Name : "none"; } }
        }

        // local media file (temp copy) + duration; null path -> faux only
        public void BeginTrack(string path, double durationSec)
        {
            EndTrack();

            lock (_sync)
            {
                _trackTime = 0;
                _playing = true;
                if (!string.IsNullOrEmpty(path))
                {
                    _realtime = new RealtimeSpectrum(path);
                    _waveform = new WaveformSpectrum(path, durationSec);
                }
                _active = _faux;
                _cts = new CancellationTokenSource();
            }

            if (_realtime != null)
            {
                _realtime.Start();
                _waveform.Start();
            }
            _faux.Start();
            _loopTask = LoopAsync(_cts.Token);
        }

        public void EndTrack()
        {
            lock (_sync)
            {
                if (_cts != null) { _cts.Cancel(); _cts = null; }
                _playing = false;
            }
            try { _loopTask?.Wait(500); } catch { }
            _loopTask = null;

            lock (_sync)
            {
                if (_realtime != null) { _realtime.Stop(); _realtime = null; }
                _waveform = null;
                _active = _faux;
            }
        }

        public void SetPlaying(bool playing)
        {
            lock (_sync) { _playing = playing; }
        }

        public void NotifySeek(double seconds)
        {
            lock (_sync) { _trackTime = seconds; }
            var rt = _realtime;
            if (rt != null) rt.NotifySeek(seconds);
        }

        public void NotifyPosition(double seconds)
        {
            lock (_sync) { _trackTime = seconds; }
        }

        public void NotifyDuration(double seconds)
        {
            lock (_sync)
            {
                if (_waveform != null) _waveform.SetDuration(seconds);
            }
        }

        public void Dispose() { EndTrack(); }

        private async Task LoopAsync(CancellationToken token)
        {
            var stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();
            var frame = new SpectrumSample();

            while (!token.IsCancellationRequested)
            {
                double trackTime;
                bool playing;
                ISpectrumSource active;
                lock (_sync)
                {
                    trackTime = _trackTime;
                    playing = _playing;
                    active = _active;
                }

                // Prefer realtime once ready; fall back to waveform when the scan
                // finishes; faux is the evergreen last resort.
                if (_realtime != null && _realtime.Available && !ReferenceEquals(active, _realtime))
                {
                    lock (_sync) { _active = _realtime; active = _realtime; }
                    DebugLog("[HyperMedia] Spectrum source -> realtime");
                }
                else if (active == _faux && _waveform != null && _waveform.Available)
                {
                    lock (_sync) { _active = _waveform; active = _waveform; }
                    DebugLog("[HyperMedia] Spectrum source -> waveform");
                }

                SpectrumSample sample = null;
                if (active != null)
                    sample = active.Poll(trackTime, playing);

                lock (_sync)
                {
                    if (sample != null)
                    {
                        frame = sample;
                        if (frame.BeatPulse > _beatPulse) _beatPulse = 1f;
                        else _beatPulse = Math.Max(0f, _beatPulse - 0.05f);
                        if (!playing)
                        {
                            // Paused: freeze the frame. Only the beat pulse is killed so
                            // the bars don't throb/jitter while holding still.
                            frame.BeatPulse = 0f;
                        }
                        else
                        {
                            // Micro-jitter filter: changes smaller than the deadband are
                            // discarded (frozen), larger ones are chased at 60%.
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
                    }
                    else if (_beatPulse > 0)
                    {
                        _beatPulse = Math.Max(0f, _beatPulse - 0.02f);
                    }
                }

                int sleep = 33 - (int)stopwatch.ElapsedMilliseconds % 33;
                if (sleep > 4) await Task.Delay(sleep);
                else await Task.Delay(4);
            }
        }

        private static void FilterDeadband(ref float smooth, ref float value, float dead)
        {
            float d = value - smooth;
            if (Math.Abs(d) < dead) value = smooth;
            else { smooth += d * 0.6f; value = smooth; }
        }
    }

    internal static class WaveformCache
    {
        // Layout: int32 frameCount, then frameCount * 4 float32 (low, mid, high, env)
        public static async Task<float[]> LoadAsync(string path)
        {
            try
            {
                string key = HashPath(path);
                var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                    "WaveCache", CreationCollisionOption.OpenIfExists);
                var file = await folder.TryGetItemAsync(key + ".vfs") as StorageFile;
                if (file == null) return null;

                var bytes = await FileIO.ReadBufferAsync(file);
                if (bytes.Length < 8) return null;
                var reader = Windows.Storage.Streams.DataReader.FromBuffer(bytes);
                int count = reader.ReadInt32();
                if (count <= 0 || count > 2 * 1024 * 1024) return null;
                var result = new float[count];
                for (int i = 0; i < count; i++)
                {
                    float v = reader.ReadSingle();
                    if (float.IsNaN(v) || float.IsInfinity(v)) v = 0f;
                    result[i] = v;
                }
                return result;
            }
            catch { return null; }
        }

        public static async Task SaveAsync(string path, float[] wave)
        {
            try
            {
                string key = HashPath(path);
                var folder = await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                    "WaveCache", CreationCollisionOption.OpenIfExists);
                var file = await folder.CreateFileAsync(key + ".vfs", CreationCollisionOption.ReplaceExisting);

                var writer = new Windows.Storage.Streams.DataWriter();
                writer.WriteInt32(wave.Length);
                foreach (float v in wave) writer.WriteSingle(v);
                await FileIO.WriteBufferAsync(file, writer.DetachBuffer());
            }
            catch { }
        }

        private static string HashPath(string path)
        {
            var hasher = Windows.Security.Cryptography.Core.HashAlgorithmProvider.OpenAlgorithm(
                Windows.Security.Cryptography.Core.HashAlgorithmNames.Sha1);
            var buffer = Windows.Security.Cryptography.CryptographicBuffer.ConvertStringToBinary(
                path.ToLowerInvariant(), Windows.Security.Cryptography.BinaryStringEncoding.Utf8);
            var hash = hasher.HashData(buffer);
            var bytes = new byte[hash.Length];
            Windows.Storage.Streams.DataReader.FromBuffer(hash).ReadBytes(bytes);
            var sb = new System.Text.StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
