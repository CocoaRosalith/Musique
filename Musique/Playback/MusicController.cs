using Musique.Lyrics;

namespace Musique.Playback {
    public sealed class MusicController : IDisposable {
        public SmtcSource Windows { get; } = new();

        public LyricsService Lyrics { get; } = new();

        private readonly LoopbackAudio _loopback = new();

        public NowPlaying? Current => Windows.Current;

        public (string Key, byte[] Bytes)? Cover => Windows.Cover;

        public string? Error => Windows.Error;

        public float BeatLevel => _loopback.Level;

        public float[] Bands => _loopback.Bands;

        public void Update(bool visualizerWanted) {
            if (visualizerWanted && !_loopback.IsRunning) _loopback.Start();
            else if (!visualizerWanted && _loopback.IsRunning) _loopback.Stop();
            var config = Musique.Config;
            Lyrics.Update(Current, config.MusixmatchLyrics, config.GeniusLyrics, config.PreferGenius);
        }

        public void PlayPause() => Windows.PlayPause();
        public void Next() => Windows.Next();
        public void Previous() => Windows.Previous();
        public void Seek(TimeSpan position) => Windows.Seek(position);

        public void Dispose() {
            _loopback.Dispose();
            Lyrics.Dispose();
            Windows.Dispose();
        }
    }
}
