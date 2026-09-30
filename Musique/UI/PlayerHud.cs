using Musique.Lyrics;
using Musique.Playback;
using OnixRuntime.Api;
using OnixRuntime.Api.Maths;
using OnixRuntime.Api.OnixClient;
using OnixRuntime.Api.Rendering;

namespace Musique.UI {
    public sealed class PlayerHud(MusicController controller, CoverArt cover) {
        public static readonly Vec2 BaseSize = new(170f, 176f);
        private const float LyricsHeight = 16f;

        private const int RingDots = 96;

        public static void Restore(OnixModuleVisual module) {
            var config = Musique.Config;
            module.Size = SizeFor(config);
            if (config.HudPosition.X >= 0f && config.HudPosition.Y >= 0f) module.Position = config.HudPosition;
        }

        private static void SyncLayout(OnixModuleVisual module) {
            var config = Musique.Config;
            var size = SizeFor(config);
            if (MathF.Abs(module.Size.X - size.X) > 0.01f || MathF.Abs(module.Size.Y - size.Y) > 0.01f) module.Size = size;
            var position = module.Position;
            var saved = config.HudPosition;
            if (MathF.Abs(position.X - saved.X) > 0.5f || MathF.Abs(position.Y - saved.Y) > 0.5f) config.HudPosition = position;
        }

        private static Vec2 SizeFor(MusiqueConfig config) =>
            new Vec2(BaseSize.X, BaseSize.Y + (config.HudLyrics ? LyricsHeight : 0f)) * Math.Clamp(config.HudScale, 0.5f, 3f);

        private readonly Visualizer _visualizer = new();

        public void Render(OnixModuleVisual module, RendererCommon2D gfx, float delta) {
            var config = Musique.Config;
            SyncLayout(module);
            gfx.SetDefaultState(false);
            controller.Update(config.BeatAnimation);
            cover.Update(controller.Cover, delta);

            var now = controller.Current;
            if (now is null && config.HideWhenIdle && Onix.Gui.MouseGrabbed) return;

            _visualizer.Update(controller, delta);
            float beat = _visualizer.Beat;

            var area = module.Area;
            float s = area.Width / 170f;
            bool dark = config.DarkHud;
            var card = dark ? new ColorF(0.07f, 0.07f, 0.1f, 0.86f) : new ColorF(0.97f, 0.99f, 1f, 0.9f);
            var text = dark ? Style.Text : new ColorF(0.2f, 0.22f, 0.25f, 1f);
            var muted = dark ? Style.Muted : new ColorF(0.55f, 0.58f, 0.62f, 1f);
            var track = dark ? new ColorF(1f, 1f, 1f, 0.15f) : new ColorF(0.86f, 0.88f, 0.9f, 1f);
            gfx.FillRoundedRectangle(area, card, 12f * s);

            var center = new Vec2(area.CenterX, area.Y + 30f * s + area.Width * 0.24f);
            float radius = area.Width * 0.24f;
            var accent = cover.Accent;
            var accent2 = cover.Accent2;

            for (int i = 5; i >= 0; i--) {
                float glow = radius + (5f + i * 3.2f + beat * 7f) * s;
                gfx.FillCircle(center, ColorF.Opacity(accent, (0.035f + beat * 0.03f) * (1f - i / 6f)), glow);
            }

            DrawRing(gfx, center, radius + 3f * s, s, accent, accent2);

            bool hasCover = cover.TryGetTextures(gfx, out var circle, out _);
            var inner = Rect.FromCenter(center, new Vec2(radius * 2f, radius * 2f));
            if (now is not null && hasCover) {
                gfx.RenderTexture(inner, circle, 1f);
            } else {
                gfx.FillCircle(center, dark ? new ColorF(0.12f, 0.12f, 0.16f, 1f) : new ColorF(0.93f, 0.96f, 0.99f, 1f), radius);
                Style.Icon(gfx, Rect.FromCenter(center, new Vec2(radius * 0.8f, radius * 0.8f)), Style.Icons.Note, ColorF.Opacity(accent, 0.8f));
            }

            if (now is { IsPlaying: false }) {
                gfx.FillCircle(center, new ColorF(0f, 0f, 0f, 0.3f), radius);
                Style.Icon(gfx, Rect.FromCenter(center, new Vec2(radius * 0.7f, radius * 0.7f)), Style.Icons.Play, 0.9f);
            }

            float y = center.Y + radius + 26f * s;
            float textScale = 0.95f * s;
            float maxWidth = area.Width - 20f * s;
            if (now is null) {
                gfx.RenderTextCentered(new Vec2(area.CenterX, y), muted, "Nothing playing", textScale);
                if (controller.Error is { } error)
                    gfx.RenderTextCentered(new Vec2(area.CenterX, y + 26f * s), new ColorF(0.9f, 0.3f, 0.3f, 1f), Style.Fit(gfx, error, maxWidth, 0.6f * s), 0.6f * s);
            } else {
                string artist = now.Artist.Length > 0 ? now.Artist : now.AppName;
                string separator = " - ";
                float artistWidth = gfx.MeasureText(artist + separator, textScale).X;
                string title = Style.Fit(gfx, now.Title, maxWidth - Math.Min(artistWidth, maxWidth * 0.5f), textScale);
                if (artistWidth > maxWidth * 0.5f) {
                    artist = Style.Fit(gfx, artist, maxWidth * 0.5f - gfx.MeasureText(separator, textScale).X, textScale);
                    artistWidth = gfx.MeasureText(artist + separator, textScale).X;
                }
                float total = artistWidth + gfx.MeasureText(title, textScale).X;
                float x = area.CenterX - total / 2f;
                gfx.RenderText(new Vec2(x, y), text, artist + separator, TextAlignment.Left, TextAlignment.Center, textScale);
                gfx.RenderText(new Vec2(x + artistWidth, y), ColorF.Opacity(text, 0.8f), title, TextAlignment.Left, TextAlignment.Center, textScale);
            }

            y += 13f * s;
            var bar = Rect.FromSize(area.X + 14f * s, y, area.Width - 28f * s, 3f * s);
            gfx.FillRoundedRectangle(bar, track, 1.5f * s);
            if (now is not null && now.Duration > TimeSpan.Zero) {
                float fraction = (float)Math.Clamp(now.Position.TotalSeconds / now.Duration.TotalSeconds, 0, 1);
                if (fraction > 0) {
                    var fill = Rect.FromSize(bar.X, bar.Y, bar.Width * fraction, bar.Height);
                    gfx.FillRoundedRectangle(fill, accent2, 1.5f * s);
                    gfx.FillRoundedRectangle(Rect.FromSize(fill.X + fill.Width * 0.5f, fill.Y, fill.Width * 0.5f, fill.Height), ColorF.Opacity(accent, 0.8f), 1.5f * s);
                }
            }

            y += 12f * s;
            float small = 0.7f * s;
            if (now is not null) {
                gfx.RenderText(new Vec2(bar.X, y), muted, Style.Time(now.Position), TextAlignment.Left, TextAlignment.Center, small);
                gfx.RenderText(new Vec2(bar.Z, y), muted, now.Duration > TimeSpan.Zero ? Style.Time(now.Duration) : "--:--", TextAlignment.Right, TextAlignment.Center, small);
            }
            var iconRect = Rect.FromCenter(new Vec2(area.CenterX, y), new Vec2(10f * s, 10f * s));
            Style.Icon(gfx, iconRect, Style.Icons.Windows, ColorF.Opacity(muted, 0.9f));

            if (config.HudLyrics) RenderLyricLine(gfx, now, new Vec2(area.CenterX, y + 16f * s), maxWidth, s, text, muted);
        }

        private void RenderLyricLine(RendererCommon2D gfx, NowPlaying? now, Vec2 at, float maxWidth, float s, ColorF text, ColorF muted) {
            float scale = 0.75f * s;
            if (now is null) return;
            var state = controller.Lyrics.State;
            if (state.Status == LyricsStatus.Loading) {
                gfx.RenderTextCentered(at, ColorF.Opacity(muted, 0.8f), "Looking for lyrics...", scale);
                return;
            }
            if (state.Status != LyricsStatus.Found || state.Result is null) {
                gfx.RenderTextCentered(at, ColorF.Opacity(muted, 0.8f), "No lyrics found", scale);
                return;
            }
            if (state.Result is not { Synced: true } result) return;
            int index = result.IndexAt(now.Position + TimeSpan.FromMilliseconds(250));
            if (index < 0) return;
            string line = result.Lines[index].Text;
            if (line.Length == 0) line = "...";
            gfx.RenderTextCentered(at, text, Style.Fit(gfx, line, maxWidth, scale), scale);
        }

        private void DrawRing(RendererCommon2D gfx, Vec2 center, float radius, float s, ColorF from, ColorF to) {
            var vis = _visualizer;
            float amplitude = (1.3f + vis.Beat * 3.5f + vis.Energy * 2f) * s * vis.WaveAmount;
            float dot = 2.2f * s;
            float barLength = 11f * s;
            for (int k = 0; k < RingDots; k++) {
                float u = (float)k / RingDots;
                float angle = -MathF.PI / 2f + u * MathF.Tau;
                float mirror = u < 0.5f ? u * 2f : (1f - u) * 2f;
                float level = vis.Band(mirror);
                var direction = new Vec2(MathF.Cos(angle), MathF.Sin(angle));
                float r = radius + vis.Wave(angle, 0f) * amplitude;
                var color = ColorF.Lerp(from, to, mirror);
                gfx.FillCircle(center + direction * r, ColorF.Opacity(color, 0.85f), dot);
                if (level < 0.03f) continue;
                float length = level * barLength;
                const int steps = 3;
                for (int j = 1; j <= steps; j++) {
                    float f = (float)j / steps;
                    gfx.FillCircle(center + direction * (r + length * f), ColorF.Opacity(color, 0.35f + 0.55f * f * MathF.Min(1f, level * 1.5f)), dot * (0.7f + 0.3f * f));
                }
            }
        }
    }
}
