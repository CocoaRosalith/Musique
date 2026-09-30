using OnixRuntime.Api.Maths;
using OnixRuntime.Api.Rendering;

namespace Musique.UI {
    public static class Style {
        public static readonly ColorF Background = new(0.055f, 0.05f, 0.08f, 1f);
        public static readonly ColorF Surface = new(0.14f, 0.13f, 0.18f, 1f);
        public static readonly ColorF Text = new(0.96f, 0.96f, 0.98f, 1f);
        public static readonly ColorF Muted = new(0.64f, 0.64f, 0.7f, 1f);
        public static readonly ColorF Faint = new(1f, 1f, 1f, 0.08f);
        public static readonly ColorF Red = new(1f, 0f, 0.2f, 1f);
        public static readonly ColorF DefaultAccent = new(0.2f, 0.85f, 1f, 1f);
        public static readonly ColorF DefaultAccent2 = new(0.35f, 0.55f, 1f, 1f);

        public static class Icons {
            public const string Play = "icons/play.png";
            public const string Pause = "icons/pause.png";
            public const string Next = "icons/next.png";
            public const string Previous = "icons/previous.png";
            public const string Close = "icons/close.png";
            public const string Note = "icons/note.png";
            public const string Windows = "icons/windows.png";
            public const string Logo = "icons/logo.png";
        }

        private static readonly Dictionary<string, TexturePath> AssetCache = [];

        public static TexturePath Asset(string path) {
            if (!AssetCache.TryGetValue(path, out var texture)) {
                texture = TexturePath.Assets(path);
                AssetCache[path] = texture;
            }
            return texture;
        }

        public static void Icon(RendererCommon2D gfx, Rect area, string icon, float opacity = 1f) =>
            gfx.RenderTexture(area, Asset(icon), opacity);

        public static void Icon(RendererCommon2D gfx, Rect area, string icon, ColorF tint) =>
            gfx.RenderTexture(area, Asset(icon), tint);

        public static string Time(TimeSpan time) {
            if (time < TimeSpan.Zero) time = TimeSpan.Zero;
            return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
        }

        public static string Fit(RendererCommon2D gfx, string text, float maxWidth, float scale) {
            if (maxWidth <= 0) return "";
            if (gfx.MeasureText(text, scale).X <= maxWidth) return text;
            int low = 0, high = text.Length;
            while (low < high) {
                int mid = (low + high + 1) / 2;
                if (gfx.MeasureText(text[..mid].TrimEnd() + "...", scale).X <= maxWidth) low = mid;
                else high = mid - 1;
            }
            return low == 0 ? "" : text[..low].TrimEnd() + "...";
        }

        public static ColorF WithAlpha(ColorF color, float alpha) => ColorF.Opacity(color, alpha);
    }
}
