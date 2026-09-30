using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Musique.Lyrics {
    public sealed record LyricLine(TimeSpan Time, string Text);

    public sealed record LyricsResult(IReadOnlyList<LyricLine> Lines, bool Synced, string Source) {
        public int IndexAt(TimeSpan position) {
            if (!Synced || Lines.Count == 0) return -1;
            int low = 0, high = Lines.Count - 1, found = -1;
            while (low <= high) {
                int mid = (low + high) / 2;
                if (Lines[mid].Time <= position) {
                    found = mid;
                    low = mid + 1;
                } else {
                    high = mid - 1;
                }
            }
            return found;
        }
    }

    public enum LyricsStatus { Idle, Loading, Found, NotFound }

    public sealed record LyricsState(string Key, LyricsStatus Status, LyricsResult? Result, string? Message) {
        public static readonly LyricsState Idle = new("", LyricsStatus.Idle, null, null);
    }

    public sealed record TrackQuery(string Artist, string Title, TimeSpan Duration);

    public static partial class LyricsText {
        [GeneratedRegex(@"\[(\d+):(\d+(?:[.:]\d+)?)\]")]
        private static partial Regex TimeTag();

        [GeneratedRegex(@"[\(\[]([^\)\]]*)[\)\]]")]
        private static partial Regex Brackets();

        [GeneratedRegex(@"\s+(feat\.?|ft\.?|featuring)\s+.*$", RegexOptions.IgnoreCase)]
        private static partial Regex Featuring();

        [GeneratedRegex(@"\b(official|video|audio|lyrics?|visuali[sz]er|clip|hd|hq|4k|mv|m/v|explicit|remaster(ed)?|music|color coded|topic|feat\.?|ft\.?|prod\.?)\b", RegexOptions.IgnoreCase)]
        private static partial Regex NoiseWords();

        [GeneratedRegex(@"\b(version|edit|mix|mono|stereo|live|radio|acoustic|instrumental|demo|single)\b", RegexOptions.IgnoreCase)]
        private static partial Regex VersionWords();

        [GeneratedRegex(@"\s*(-\s*Topic|VEVO|Official)\s*$", RegexOptions.IgnoreCase)]
        private static partial Regex ChannelSuffix();

        [GeneratedRegex(@"\s{2,}")]
        private static partial Regex Spaces();

        public static List<LyricLine> ParseLrc(string lrc) {
            var lines = new List<LyricLine>();
            foreach (string raw in lrc.Replace("\r", "").Split('\n')) {
                var matches = TimeTag().Matches(raw);
                if (matches.Count == 0) continue;
                string text = TimeTag().Replace(raw, "").Trim();
                foreach (Match match in matches) {
                    int minutes = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    double seconds = double.Parse(match.Groups[2].Value.Replace(':', '.'), CultureInfo.InvariantCulture);
                    lines.Add(new LyricLine(TimeSpan.FromSeconds(minutes * 60 + seconds), text));
                }
            }
            lines.Sort((a, b) => a.Time.CompareTo(b.Time));
            return lines;
        }

        public static List<LyricLine> ParsePlain(string text) {
            var lines = new List<LyricLine>();
            foreach (string raw in text.Replace("\r", "").Split('\n')) {
                string line = raw.Trim();
                if (line.StartsWith("******* This Lyrics is NOT for Commercial use", StringComparison.OrdinalIgnoreCase)) break;
                if (line.Length == 0 && (lines.Count == 0 || lines[^1].Text.Length == 0)) continue;
                lines.Add(new LyricLine(TimeSpan.Zero, line));
            }
            while (lines.Count > 0 && lines[^1].Text.Length == 0) lines.RemoveAt(lines.Count - 1);
            return lines;
        }

        public static string CleanTitle(string title) {
            string cleaned = Brackets().Replace(title, match => NoiseWords().IsMatch(match.Groups[1].Value) ? "" : match.Value);
            cleaned = Featuring().Replace(cleaned, "");
            cleaned = cleaned.Replace("\"", "").Replace("“", "").Replace("”", "");
            return Spaces().Replace(cleaned, " ").Trim(' ', '-', '|');
        }

        public static string CleanArtist(string artist) {
            string cleaned = ChannelSuffix().Replace(artist, "");
            foreach (string separator in new[] { ", ", " & ", " x ", " X ", " feat. ", " ft. ", " feat ", "; " }) {
                int at = cleaned.IndexOf(separator, StringComparison.Ordinal);
                if (at > 0) cleaned = cleaned[..at];
            }
            return cleaned.Trim();
        }

        public static List<TrackQuery> Candidates(string artist, string title, TimeSpan duration) {
            var list = new List<TrackQuery>();
            string cleanTitle = CleanTitle(title);
            string cleanArtist = CleanArtist(artist);
            TrackQuery? split = null;
            int dash = cleanTitle.IndexOf(" - ", StringComparison.Ordinal);
            if (dash < 0) dash = cleanTitle.IndexOf(" – ", StringComparison.Ordinal);
            if (dash > 0) {
                string left = cleanTitle[..dash].Trim();
                string right = CleanTitle(cleanTitle[(dash + 3)..]);
                if (right.Length == 0 || NoiseWords().IsMatch(right) || VersionWords().IsMatch(right)) cleanTitle = left;
                else if (left.Length > 0) split = cleanArtist.Length > 0 && Similarity(right, cleanArtist) > 0.5f
                    ? new TrackQuery(CleanArtist(right), left, duration)
                    : new TrackQuery(CleanArtist(left), right, duration);
            }
            if (split is not null) list.Add(split);
            if (cleanTitle.Length > 0) list.Add(new TrackQuery(cleanArtist, cleanTitle, duration));
            return list.DistinctBy(q => Normalize(q.Artist) + "|" + Normalize(q.Title)).ToList();
        }

        private static bool Contains(string text, string part) => Normalize(text).Contains(Normalize(part));

        public static string Normalize(string text) {
            string decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            bool space = false;
            foreach (char c in decomposed) {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(c)) {
                    builder.Append(c);
                    space = false;
                } else if (!space && builder.Length > 0) {
                    builder.Append(' ');
                    space = true;
                }
            }
            return builder.ToString().Trim();
        }

        public static float Similarity(string a, string b) {
            var left = Normalize(a).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var right = Normalize(b).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            if (left.Count == 0 || right.Count == 0) return 0f;
            int shared = left.Count(right.Contains);
            return 2f * shared / (left.Count + right.Count);
        }

        public static float Score(TrackQuery query, string artist, string title, TimeSpan duration) {
            float titleScore = Similarity(query.Title, CleanTitle(title));
            float artistScore = query.Artist.Length == 0 ? 0.5f : MathF.Max(Similarity(query.Artist, artist), Contains(artist, query.Artist) ? 1f : 0f);
            float score = titleScore * 0.6f + artistScore * 0.4f;
            if (query.Duration > TimeSpan.Zero && duration > TimeSpan.Zero) {
                double gap = Math.Abs((query.Duration - duration).TotalSeconds);
                score += gap < 3 ? 0.15f : gap < 10 ? 0.05f : gap > 30 ? -0.2f : 0f;
            }
            return score;
        }
    }
}
