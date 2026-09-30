using NAudio.Wave;

namespace Musique.Playback {
    public sealed class AudioAnalyzer {
        public const int BandCount = 64;
        private const int FftSize = 4096;
        private const int HopSize = 1024;
        private const float MinFrequency = 35f;
        private const float MaxFrequency = 16000f;
        private const float DynamicRange = 50f;

        private readonly float[] _ring = new float[FftSize];
        private readonly float[] _window = new float[FftSize];
        private readonly float[] _re = new float[FftSize];
        private readonly float[] _im = new float[FftSize];
        private readonly float[] _magnitude = new float[FftSize / 2];
        private readonly float[] _bandLow = new float[BandCount];
        private readonly float[] _bandHigh = new float[BandCount];
        private readonly float[] _bandTilt = new float[BandCount];
        private readonly float[] _previous = new float[BandCount];
        private readonly float _lowPass;
        private readonly float _windowGain;

        private int _write;
        private int _sinceHop;
        private float _filtered;
        private double _bassEnergy;
        private float _bassAverage = 0.02f;
        private float _peakDb = -30f;
        private float _fluxAverage = 0.02f;

        public AudioAnalyzer(int sampleRate) {
            _lowPass = 1f - MathF.Exp(-2f * MathF.PI * 150f / sampleRate);
            float sum = 0;
            for (int i = 0; i < FftSize; i++) {
                _window[i] = 0.5f - 0.5f * MathF.Cos(MathF.Tau * i / (FftSize - 1));
                sum += _window[i];
            }
            _windowGain = 2f / sum;

            float binHz = (float)sampleRate / FftSize;
            float top = MathF.Min(MaxFrequency, sampleRate * 0.45f);
            for (int b = 0; b < BandCount; b++) {
                float low = MinFrequency * MathF.Pow(top / MinFrequency, (float)b / BandCount);
                float high = MinFrequency * MathF.Pow(top / MinFrequency, (float)(b + 1) / BandCount);
                _bandLow[b] = low / binHz;
                _bandHigh[b] = high / binHz;
                float center = MathF.Sqrt(low * high);
                _bandTilt[b] = 3f * MathF.Log2(center / 1000f);
            }
        }

        public void Push(float sample, Action<float, float[]> onFrame) {
            _ring[_write] = sample;
            _write = (_write + 1) % FftSize;
            _filtered += _lowPass * (sample - _filtered);
            _bassEnergy += _filtered * _filtered;
            if (++_sinceHop < HopSize) return;
            _sinceHop = 0;
            var bands = new float[BandCount];
            float level = Analyze(bands);
            onFrame(level, bands);
        }

        private float Analyze(float[] bands) {
            double full = 0;
            for (int i = 0; i < FftSize; i++) {
                float x = _ring[(_write + i) % FftSize];
                if (i >= FftSize - HopSize) full += x * x;
                _re[i] = x * _window[i];
                _im[i] = 0f;
            }
            Fft(_re, _im);
            for (int k = 0; k < _magnitude.Length; k++)
                _magnitude[k] = MathF.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]) * _windowGain;

            float framePeak = -120f;
            var db = new float[BandCount];
            for (int b = 0; b < BandCount; b++) {
                float low = _bandLow[b], high = _bandHigh[b];
                float amplitude;
                if (high - low < 1.5f) {
                    amplitude = Sample((low + high) * 0.5f);
                } else {
                    int start = (int)MathF.Floor(low), end = Math.Min(_magnitude.Length - 1, (int)MathF.Ceiling(high));
                    double power = 0;
                    for (int k = start; k <= end; k++) power += _magnitude[k] * _magnitude[k];
                    amplitude = (float)Math.Sqrt(power / (end - start + 1)) * 1.6f;
                }
                db[b] = 20f * MathF.Log10(amplitude + 1e-9f) + _bandTilt[b];
                framePeak = MathF.Max(framePeak, db[b]);
            }

            _peakDb = framePeak > _peakDb ? _peakDb + (framePeak - _peakDb) * 0.5f : _peakDb - 0.06f;
            _peakDb = MathF.Max(_peakDb, -42f);
            float floor = _peakDb - DynamicRange;

            float flux = 0, sum = 0;
            for (int b = 0; b < BandCount; b++) {
                float value = Math.Clamp((db[b] - floor) / DynamicRange, 0f, 1f);
                value = MathF.Pow(value, 1.7f);
                bands[b] = value;
                flux += MathF.Max(0f, value - _previous[b]);
                _previous[b] = value;
                sum += value;
            }
            flux /= BandCount;
            _fluxAverage = _fluxAverage * 0.95f + flux * 0.05f;
            float onset = Math.Clamp((flux / (_fluxAverage + 0.002f) - 1f) * 0.5f, 0f, 1f);

            float bassRms = (float)Math.Sqrt(_bassEnergy / HopSize);
            _bassEnergy = 0;
            _bassAverage = _bassAverage * 0.96f + bassRms * 0.04f;
            float beat = Math.Clamp((bassRms / (_bassAverage + 0.0005f) - 1f) * 1.1f, 0f, 1f);
            float loudness = Math.Clamp((float)Math.Sqrt(full / HopSize) * 3.5f, 0f, 1f);
            float spread = sum / BandCount;
            return Math.Clamp(loudness * 0.2f + spread * 0.25f + beat * 0.55f + onset * 0.45f, 0f, 1f);
        }

        private float Sample(float bin) {
            int i = Math.Clamp((int)bin, 0, _magnitude.Length - 2);
            float t = Math.Clamp(bin - i, 0f, 1f);
            return _magnitude[i] + (_magnitude[i + 1] - _magnitude[i]) * t;
        }

        private static void Fft(float[] re, float[] im) {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++) {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }
            for (int length = 2; length <= n; length <<= 1) {
                double angle = -2 * Math.PI / length;
                float wr = (float)Math.Cos(angle), wi = (float)Math.Sin(angle);
                int half = length >> 1;
                for (int start = 0; start < n; start += length) {
                    float cr = 1f, ci = 0f;
                    for (int k = 0; k < half; k++) {
                        int a = start + k, b = a + half;
                        float tr = re[b] * cr - im[b] * ci;
                        float ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr;
                        im[b] = im[a] - ti;
                        re[a] += tr;
                        im[a] += ti;
                        float next = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = next;
                    }
                }
            }
        }
    }

    public sealed class LoopbackAudio : IDisposable {
        private static readonly float[] Silence = new float[AudioAnalyzer.BandCount];

        private WasapiLoopbackCapture? _capture;
        private AudioAnalyzer? _analyzer;
        private volatile float _level;
        private volatile float[] _bands = Silence;
        private long _lastUpdateTicks;

        public bool IsRunning => _capture is not null;

        private bool Stale => (DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastUpdateTicks)) / (double)TimeSpan.TicksPerSecond > 0.4;

        public float Level => Stale ? 0f : _level;

        public float[] Bands => Stale ? Silence : _bands;

        public void Start() {
            if (_capture is not null) return;
            try {
                var capture = new WasapiLoopbackCapture();
                var format = capture.WaveFormat;
                _analyzer = new AudioAnalyzer(format.SampleRate);
                capture.DataAvailable += (_, e) => OnData(e.Buffer, e.BytesRecorded, format);
                capture.RecordingStopped += (_, _) => {
                    _level = 0f;
                    _bands = Silence;
                };
                capture.StartRecording();
                _capture = capture;
            } catch (Exception ex) {
                Console.WriteLine($"Musique: audio capture unavailable: {ex.Message}");
                _capture = null;
            }
        }

        private void OnData(byte[] buffer, int count, WaveFormat format) {
            var analyzer = _analyzer;
            if (analyzer is null) return;
            try {
                int channels = format.Channels;
                bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat
                    || (format.Encoding == WaveFormatEncoding.Extensible && format.BitsPerSample == 32);
                int bytesPerSample = format.BitsPerSample / 8;
                int frameBytes = bytesPerSample * channels;
                for (int offset = 0; offset + frameBytes <= count; offset += frameBytes) {
                    float mono = 0;
                    for (int c = 0; c < channels; c++) {
                        int at = offset + c * bytesPerSample;
                        mono += isFloat && bytesPerSample == 4
                            ? BitConverter.ToSingle(buffer, at)
                            : bytesPerSample == 2 ? BitConverter.ToInt16(buffer, at) / 32768f : 0f;
                    }
                    analyzer.Push(mono / channels, Publish);
                }
            } catch {
            }
        }

        private void Publish(float level, float[] bands) {
            _level = level;
            _bands = bands;
            Interlocked.Exchange(ref _lastUpdateTicks, DateTime.UtcNow.Ticks);
        }

        public void Stop() {
            var capture = _capture;
            _capture = null;
            _level = 0f;
            _bands = Silence;
            if (capture is null) return;
            try {
                capture.StopRecording();
                capture.Dispose();
            } catch {
            }
        }

        public void Dispose() => Stop();
    }
}
