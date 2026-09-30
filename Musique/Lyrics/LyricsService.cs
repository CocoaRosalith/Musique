using Musique.Playback;

namespace Musique.Lyrics {
    public sealed class LyricsService : IDisposable {
        private readonly MusixmatchClient _musixmatch = new(Path.Combine(Musique.Instance.PluginPersistentDataPath, "musixmatch_token.txt"));
        private readonly GeniusClient _genius = new();
        private readonly Dictionary<string, LyricsResult?> _cache = [];
        private readonly object _lock = new();

        private CancellationTokenSource? _cts;
        private string _key = "";
        private volatile LyricsState _state = LyricsState.Idle;

        public LyricsState State => _state;

        public void Update(NowPlaying? now, bool useMusixmatch, bool useGenius, bool preferGenius) {
            if (now is null) {
                if (_key.Length > 0) Cancel("");
                _state = LyricsState.Idle;
                return;
            }
            string sources = $"{(useMusixmatch ? "m" : "")}{(useGenius ? "g" : "")}";
            string key = $"{sources}{(preferGenius ? ">g" : ">m")}|{now.Artist}|{now.Title}";
            if (key == _key) return;
            Cancel(key);

            if (sources.Length == 0) {
                _state = new LyricsState(key, LyricsStatus.NotFound, null, null);
                return;
            }
            lock (_lock) {
                if (_cache.TryGetValue(key, out var cached)) {
                    _state = cached is null ? new LyricsState(key, LyricsStatus.NotFound, null, null) : new LyricsState(key, LyricsStatus.Found, cached, null);
                    return;
                }
            }

            _state = new LyricsState(key, LyricsStatus.Loading, null, null);
            var cts = _cts = new CancellationTokenSource();
            var queries = LyricsText.Candidates(now.Artist, now.Title, now.Duration);
            var duration = now.Duration;
            _ = Task.Run(async () => {
                await Task.Delay(350, cts.Token);
                var (result, error) = await FetchAsync(queries, useMusixmatch, useGenius, preferGenius, cts.Token);
                if (cts.IsCancellationRequested) return;
                if (result is not null || error is null) {
                    lock (_lock) _cache[key] = result;
                }
                _state = result is not null
                    ? new LyricsState(key, LyricsStatus.Found, result, null)
                    : new LyricsState(key, LyricsStatus.NotFound, null, null);
            }, cts.Token);
        }

        private async Task<(LyricsResult?, string?)> FetchAsync(List<TrackQuery> queries, bool useMusixmatch, bool useGenius, bool preferGenius, CancellationToken token) {
            if (queries.Count == 0) return (null, null);
            string? error = null;
            LyricsResult? plain = null;

            async Task<LyricsResult?> Genius() {
                if (!useGenius) return null;
                try {
                    return await _genius.FindAsync(queries, token);
                } catch (OperationCanceledException) when (token.IsCancellationRequested) {
                    throw;
                } catch (Exception ex) {
                    error ??= $"Genius: {ex.Message}";
                    return null;
                }
            }

            if (preferGenius && await Genius() is { } first) return (first, null);
            if (useMusixmatch) {
                try {
                    var (synced, text) = await _musixmatch.FindAsync(queries, token);
                    if (synced is not null) return (synced, null);
                    plain = text;
                } catch (OperationCanceledException) when (token.IsCancellationRequested) {
                    throw;
                } catch (Exception ex) {
                    error ??= $"Musixmatch: {ex.Message}";
                }
            }
            if (plain is not null) return (plain, null);
            if (!preferGenius && await Genius() is { } fallback) return (fallback, null);
            return (null, error);
        }

        private void Cancel(string key) {
            _key = key;
            var cts = _cts;
            _cts = null;
            try {
                cts?.Cancel();
            } catch {
            }
        }

        public void Dispose() => Cancel("");
    }
}
