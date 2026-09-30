using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Musique.Lyrics {
    public sealed partial class GeniusClient {
        private const string ContainerMarker = "data-lyrics-container=\"true\"";
        private const string ExcludeMarker = "data-exclude-from-selection=\"true\"";

        [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
        private static partial Regex LineBreak();

        [GeneratedRegex(@"<[^>]+>")]
        private static partial Regex Tag();

        private readonly HttpClient _http;

        public GeniusClient() {
            _http = new HttpClient(new SocketsHttpHandler {
                AutomaticDecompression = DecompressionMethods.All,
            }) {
                Timeout = TimeSpan.FromSeconds(12),
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
            _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        }

        public async Task<LyricsResult?> FindAsync(IReadOnlyList<TrackQuery> queries, CancellationToken token) {
            foreach (var query in queries) {
                string? url = await SearchAsync(query, token);
                if (url is null) continue;
                string html = await _http.GetStringAsync(url, token);
                var lines = Extract(html);
                if (lines.Count > 0) return new LyricsResult(lines, false, "Genius");
            }
            return null;
        }

        private async Task<string?> SearchAsync(TrackQuery query, CancellationToken token) {
            string q = query.Artist.Length > 0 ? $"{query.Artist} {query.Title}" : query.Title;
            using var response = await _http.GetAsync($"https://genius.com/api/search/song?per_page=10&q={Uri.EscapeDataString(q)}", token);
            if (!response.IsSuccessStatusCode) return null;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            if (!json.RootElement.TryGetProperty("response", out var root) || !root.TryGetProperty("sections", out var sections)) return null;

            string? best = null;
            float bestScore = 0.55f;
            foreach (var section in sections.EnumerateArray()) {
                if (!section.TryGetProperty("hits", out var hits)) continue;
                foreach (var hit in hits.EnumerateArray()) {
                    if (!hit.TryGetProperty("result", out var result)) continue;
                    string title = Text(result, "title");
                    string artist = result.TryGetProperty("primary_artist", out var primary) ? Text(primary, "name") : "";
                    string names = Text(result, "artist_names");
                    string url = Text(result, "url");
                    if (url.Length == 0 || Text(result, "lyrics_state") is "unreleased") continue;
                    float score = MathF.Max(LyricsText.Score(query with { Duration = TimeSpan.Zero }, artist, title, TimeSpan.Zero),
                        LyricsText.Score(query with { Duration = TimeSpan.Zero }, names, title, TimeSpan.Zero));
                    if (title.Contains("Translation", StringComparison.OrdinalIgnoreCase) || artist.StartsWith("Genius", StringComparison.OrdinalIgnoreCase)) score -= 0.3f;
                    if (score > bestScore) {
                        bestScore = score;
                        best = url;
                    }
                }
            }
            return best;
        }

        private static List<LyricLine> Extract(string html) {
            var builder = new StringBuilder();
            int at = 0;
            while ((at = html.IndexOf(ContainerMarker, at, StringComparison.Ordinal)) >= 0) {
                int open = html.IndexOf('>', at);
                if (open < 0) break;
                int end = FindClosingDiv(html, open + 1);
                if (end < 0) break;
                if (builder.Length > 0) builder.Append("<br/>");
                builder.Append(RemoveExcluded(html[(open + 1)..end]));
                at = end;
            }
            if (builder.Length == 0) return [];
            string text = LineBreak().Replace(builder.ToString(), "\n");
            text = WebUtility.HtmlDecode(Tag().Replace(text, ""));
            var lines = LyricsText.ParsePlain(text);
            if (lines.Count > 0 && lines[0].Text.StartsWith('[') && lines[0].Text.EndsWith(']') && lines[0].Text.Contains('"')) lines.RemoveAt(0);
            while (lines.Count > 0 && lines[0].Text.Length == 0) lines.RemoveAt(0);
            return lines;
        }

        private static string RemoveExcluded(string html) {
            int at;
            while ((at = html.IndexOf(ExcludeMarker, StringComparison.Ordinal)) >= 0) {
                int start = html.LastIndexOf("<div", at, StringComparison.OrdinalIgnoreCase);
                int open = html.IndexOf('>', at);
                if (start < 0 || open < 0) break;
                int end = FindClosingDiv(html, open + 1);
                if (end < 0) break;
                html = html[..start] + html[Math.Min(html.Length, end + 6)..];
            }
            return html;
        }

        private static int FindClosingDiv(string html, int from) {
            int depth = 1, at = from;
            while (at < html.Length) {
                int next = html.IndexOf('<', at);
                if (next < 0) return -1;
                if (string.CompareOrdinal(html, next, "</div", 0, 5) == 0) {
                    if (--depth == 0) return next;
                } else if (string.CompareOrdinal(html, next, "<div", 0, 4) == 0) {
                    depth++;
                }
                at = next + 1;
            }
            return -1;
        }

        private static string Text(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    }
}
