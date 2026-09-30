using Musique.Playback;

namespace Musique.UI {
    public sealed class Visualizer {
        private readonly float[] _bands = new float[AudioAnalyzer.BandCount];

        public float Beat { get; private set; }
        public float Energy { get; private set; }
        public float Time { get; private set; }
        public float WaveAmount { get; private set; }

        public void Update(MusicController controller, float delta) {
            var config = Musique.Config;
            bool on = config.BeatAnimation;
            float[]? target = on ? controller.Bands : null;
            float sum = 0;
            for (int b = 0; b < _bands.Length; b++) {
                float t = target is not null && b < target.Length ? target[b] : 0f;
                float v = _bands[b];
                _bands[b] = t > v ? v + (t - v) * Math.Clamp(delta * 28f, 0f, 1f) : MathF.Max(t, v - delta * (0.9f + v * 1.6f));
                sum += _bands[b];
            }
            Energy = sum / _bands.Length;

            float beat = on ? controller.BeatLevel : 0f;
            Beat = beat > Beat ? Beat + (beat - Beat) * Math.Clamp(delta * 30f, 0f, 1f) : MathF.Max(beat, Beat - delta * 2.2f);

            float wave = Math.Clamp(Energy * 5f + Beat * 2f, 0f, 1f);
            WaveAmount = wave > WaveAmount ? WaveAmount + (wave - WaveAmount) * Math.Clamp(delta * 12f, 0f, 1f) : MathF.Max(wave, WaveAmount - delta * 1.5f);
            Time += delta * (0.8f + Energy * 1.6f + Beat * 1.4f) * MathF.Max(WaveAmount, 0.15f);
        }

        public float Band(float u) {
            float at = Math.Clamp(u, 0f, 1f) * (_bands.Length - 1);
            int i = Math.Min((int)at, _bands.Length - 2);
            float t = at - i;
            return _bands[i] + (_bands[i + 1] - _bands[i]) * t;
        }

        public float Wave(float angle, float phase) {
            float t = Time + phase;
            return MathF.Sin(3f * angle + t * 1.1f) + 0.6f * MathF.Sin(5f * angle - t * 1.7f) + 0.35f * MathF.Sin(2f * angle + t * 0.6f);
        }
    }
}
