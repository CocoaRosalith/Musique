using System.Net;
using System.Text.Json;

namespace Musique.Lyrics {
    public sealed class MusixmatchClient {
        private const string Root = "https://apic.musixmatch.com/ws/1.1/";
        private const string AppId = "mac-ios-v2.0";

        private readonly HttpClient _http;
        private readonly SemaphoreSlim _tokenLock = new(1, 1);
        private readonly string? _tokenFile;
        private string? _token;
        private DateTime _retryAfter;
        private TimeSpan _backoff = TimeSpan.FromMinutes(1);

        public MusixmatchClient(string? tokenFile) {
            _tokenFile = tokenFile;
            _http = new HttpClient(new SocketsHttpHandler {
                CookieContainer = new CookieContainer(),
                UseCookies = true,
                AutomaticDecompression = DecompressionMethods.All,
            }) {
                Timeout = TimeSpan.FromSeconds(12),
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Musixmatch/0.19.4 (Macintosh; OS X 10.15)");
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }

        public string? RateLimitMessage => DateTime.UtcNow < _retryAfter
            ? $"Musixmatch is asking for a captcha, retrying in {Math.Max(1, (int)Math.Ceiling((_retryAfter - DateTime.UtcNow).TotalMinutes))} min."
            : null;

        public async Task<(LyricsResult? Synced, LyricsResult? Plain)> FindAsync(IReadOnlyList<TrackQuery> queries, CancellationToken token) {
            foreach (var query in queries) {
                var track = await MatchAsync(query, token);
                if (track is not { } found) continue;

                LyricsResult? synced = null, plain = null;
                if (found.HasSubtitles) {
                    using var subtitle = await GetAsync("track.subtitle.get", [("track_id", found.Id), ("subtitle_format", "lrc")], token);
                    if (Body(subtitle) is { } body && Find(body, "subtitle", "subtitle_body") is { ValueKind: JsonValueKind.String } lrc) {
                        var lines = LyricsText.ParseLrc(lrc.GetString()!);
                        if (lines.Count > 0) synced = new LyricsResult(lines, true, "Musixmatch");
                    }
                }
                if (synced is null && found.HasLyrics) {
                    using var lyrics = await GetAsync("track.lyrics.get", [("track_id", found.Id)], token);
                    if (Body(lyrics) is { } body && Find(body, "lyrics", "lyrics_body") is { ValueKind: JsonValueKind.String } text) {
                        var lines = LyricsText.ParsePlain(text.GetString()!);
                        if (lines.Count > 0) plain = new LyricsResult(lines, false, "Musixmatch");
                    }
                }
                if (synced is not null || plain is not null) return (synced, plain);
            }
            return (null, null);
        }

        private readonly record struct Track(string Id, bool HasSubtitles, bool HasLyrics, float Score);

        private async Task<Track?> MatchAsync(TrackQuery query, CancellationToken token) {
            var parameters = new List<(string, string)> {
                ("q_track", query.Title),
                ("page_size", "8"),
                ("page", "1"),
                ("s_track_rating", "desc"),
            };
            if (query.Artist.Length > 0) parameters.Add(("q_artist", query.Artist));
            using var search = await GetAsync("track.search", parameters, token);
            if (Body(search) is not { } body || Find(body, "track_list") is not { ValueKind: JsonValueKind.Array } list) return null;

            Track? best = null;
            foreach (var item in list.EnumerateArray()) {
                if (!item.TryGetProperty("track", out var track)) continue;
                string name = Text(track, "track_name");
                string artist = Text(track, "artist_name");
                bool hasSubtitles = Number(track, "has_subtitles") == 1;
                bool hasLyrics = Number(track, "has_lyrics") == 1;
                if (!hasSubtitles && !hasLyrics) continue;
                var length = TimeSpan.FromSeconds(Number(track, "track_length"));
                float score = LyricsText.Score(query, artist, name, length) + (hasSubtitles ? 0.05f : 0f);
                if (score < 0.55f) continue;
                if (best is null || score > best.Value.Score)
                    best = new Track(Number(track, "track_id").ToString(), hasSubtitles, hasLyrics, score);
            }
            return best;
        }

        private async Task<string?> TokenAsync(CancellationToken token) {
            if (_token is not null) return _token;
            await _tokenLock.WaitAsync(token);
            try {
                if (_token is not null) return _token;
                if (LoadToken() is { } saved) return _token = saved;
                if (DateTime.UtcNow < _retryAfter) return null;
                using var response = await SendAsync("token.get", [("user_language", "en")], null, token);
                if (response is not null && Body(response) is { } body && Find(body, "user_token") is { ValueKind: JsonValueKind.String } value) {
                    string user = value.GetString()!;
                    if (IsUsable(user)) {
                        _backoff = TimeSpan.FromMinutes(1);
                        SaveToken(user);
                        return _token = user;
                    }
                }
                RateLimited();
                return null;
            } finally {
                _tokenLock.Release();
            }
        }

        private static bool IsUsable(string user) =>
            user.Length > 0 && user.Any(c => c != '0') && !user.StartsWith("UpgradeOnly", StringComparison.Ordinal);

        private void RateLimited() {
            _retryAfter = DateTime.UtcNow + _backoff;
            _backoff = TimeSpan.FromMinutes(Math.Min(30, _backoff.TotalMinutes * 2));
        }

        private string? LoadToken() {
            try {
                if (_tokenFile is null || !File.Exists(_tokenFile)) return null;
                string saved = File.ReadAllText(_tokenFile).Trim();
                return IsUsable(saved) ? saved : null;
            } catch {
                return null;
            }
        }

        private void SaveToken(string? user) {
            try {
                if (_tokenFile is null) return;
                if (user is null) {
                    File.Delete(_tokenFile);
                    return;
                }
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_tokenFile)!);
                File.WriteAllText(_tokenFile, user);
            } catch {
            }
        }

        private async Task<JsonDocument?> GetAsync(string action, List<(string, string)> parameters, CancellationToken token) {
            for (int attempt = 0; attempt < 2; attempt++) {
                string? user = await TokenAsync(token);
                if (user is null) throw new HttpRequestException(RateLimitMessage ?? "Musixmatch didn't give a token, try again later.");
                var document = await SendAsync(action, parameters, user, token);
                if (document is null || Status(document) != 401) return document;
                string hint = Find(document.RootElement, "message", "header", "hint") is { ValueKind: JsonValueKind.String } h ? h.GetString() ?? "" : "";
                document.Dispose();
                if (hint == "captcha") {
                    RateLimited();
                    throw new HttpRequestException(RateLimitMessage);
                }
                _token = null;
                SaveToken(null);
            }
            return null;
        }

        private async Task<JsonDocument?> SendAsync(string action, List<(string, string)> parameters, string? user, CancellationToken token) {
            var query = new List<(string, string)>(parameters) {
                ("app_id", AppId),
                ("format", "json"),
                ("t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()),
            };
            if (user is not null) query.Add(("usertoken", user));
            string url = Root + action + "?" + string.Join("&", query.Select(p => $"{p.Item1}={Uri.EscapeDataString(p.Item2)}"));
            using var response = await _http.GetAsync(url, token);
            if (!response.IsSuccessStatusCode) return null;
            string json = await response.Content.ReadAsStringAsync(token);
            try {
                return JsonDocument.Parse(json);
            } catch (JsonException) {
                return null;
            }
        }

        private static int Status(JsonDocument document) =>
            Find(document.RootElement, "message", "header", "status_code") is { ValueKind: JsonValueKind.Number } code ? code.GetInt32() : 0;

        private static JsonElement? Body(JsonDocument? document) {
            if (document is null || Status(document) != 200) return null;
            return Find(document.RootElement, "message", "body") is { ValueKind: JsonValueKind.Object } body ? body : null;
        }

        private static JsonElement? Find(JsonElement element, params string[] names) {
            foreach (string name in names) {
                if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element)) return null;
            }
            return element;
        }

        private static string Text(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

        private static long Number(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number) ? number : 0;
    }
}
