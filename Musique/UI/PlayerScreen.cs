using System.Diagnostics;
using Musique.Lyrics;
using Musique.Playback;
using OnixRuntime.Api;
using OnixRuntime.Api.Inputs;
using OnixRuntime.Api.Maths;
using OnixRuntime.Api.OnixClient;
using OnixRuntime.Api.Rendering;

namespace Musique.UI {
    public sealed class PlayerScreen : OnixClientScreen {
        private const float TitleHeight = 18f;
        private const float GripSize = 12f;
        private const float LyricsScale = 0.85f;
        private const float LyricsGap = 5f;
        private const float LyricsMinWidth = 600f;
        private static readonly Vec2 MinWindowSize = new(360f, 200f);
        private static readonly Vec2 DefaultWindowSize = new(520f, 260f);
        private static readonly Vec2 LyricsWindowSize = new(780f, 300f);

        private readonly MusicController _controller;
        private readonly CoverArt _cover;
        private readonly Visualizer _visualizer = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly List<(Rect Area, Action OnClick)> _buttons = [];
        private readonly List<(float Top, float Height, string Text)> _layout = [];

        private float _lastTime;
        private Rect _window;
        private Rect _titleBar;
        private Rect _grip;
        private Rect _close;
        private Rect _lyricsToggle;
        private bool _dragging;
        private bool _resizing;
        private Vec2 _dragOffset;
        private Vec2 _resizeStart;
        private Vec2 _resizeStartSize;

        private LyricsResult? _layoutResult;
        private float _layoutWidth;
        private float _layoutHeight;
        private float _scrollTarget;
        private float _scroll;
        private float _manualUntil;

        public PlayerScreen(MusicController controller, CoverArt cover) : base("musique_player", true, true) {
            _controller = controller;
            _cover = cover;
        }

        public override bool ShouldPreventInputs() => true;

        public override void OnCloseFinished() {
            if (_dragging || _resizing) SaveWindow();
            _dragging = false;
            _resizing = false;
            base.OnCloseFinished();
        }

        public override void OnRender(RendererCommon2D gfx) {
            float time = (float)_clock.Elapsed.TotalSeconds;
            float delta = Math.Clamp(time - _lastTime, 0f, 0.1f);
            _lastTime = time;

            int scroll = ScrollCount;
            if (scroll != 0) HandleAllInputs();

            _controller.Update(Musique.Config.BeatAnimation);
            _cover.Update(_controller.Cover, delta);
            _visualizer.Update(_controller, delta);
            var now = _controller.Current;

            _buttons.Clear();
            var mouse = Onix.Gui.MousePosition;
            var screen = RenderArea;
            UpdateWindow(screen, mouse);

            gfx.FillRectangle(screen, new ColorF(0f, 0f, 0f, 0.3f));
            gfx.FillRoundedRectangle(_window.Expand(3f), new ColorF(0f, 0f, 0f, 0.35f), 6f);
            gfx.FillRectangle(_window, Style.Background);
            var content = new Rect(_window.X, _window.Y + TitleHeight, _window.Z, _window.W);
            var accent = _cover.Accent;
            for (int i = 0; i < 10; i++) {
                float bandHeight = content.Height / 10f;
                var band = new Rect(content.X, content.Y + i * bandHeight, content.Z, content.Y + (i + 1) * bandHeight);
                gfx.FillRectangle(band, ColorF.Opacity(accent, 0.16f * (1f - i / 10f)));
            }
            RenderSpectrum(gfx, content);

            bool lyrics = Musique.Config.ShowLyrics && content.Width >= LyricsMinWidth;
            var player = lyrics ? new Rect(content.X, content.Y, content.X + content.Width * 0.58f, content.W) : content;
            if (now is null) RenderIdle(gfx, player);
            else RenderNowPlaying(gfx, player, now, mouse);
            if (lyrics) {
                var panel = new Rect(player.Z, content.Y + 14f, content.Z - 14f, content.W - 14f);
                RenderLyrics(gfx, panel, now, mouse, time, delta, scroll);
            }

            RenderTitleBar(gfx, mouse);
            RenderGrip(gfx, mouse);
            gfx.DrawRectangle(_window, ColorF.Opacity(ColorF.White, 0.12f), 1f);
        }

        private void RenderSpectrum(RendererCommon2D gfx, Rect content) {
            const int count = 56;
            const float gap = 2f;
            float width = (content.Width - gap * (count + 1)) / count;
            if (width <= 0.5f) return;
            float maxHeight = content.Height * 0.32f;
            var vis = _visualizer;
            for (int i = 0; i < count; i++) {
                float u = (float)i / (count - 1);
                float level = vis.Band(u);
                float wave = vis.WaveAmount * (0.5f + 0.5f * MathF.Sin(u * 9f + vis.Time * 2.2f)) * (0.6f + 0.4f * MathF.Sin(u * 3.3f - vis.Time * 1.3f));
                float height = 2f + wave * 9f + level * maxHeight;
                var bar = Rect.FromSize(content.X + gap + i * (width + gap), content.W - height, width, height);
                var color = ColorF.Lerp(_cover.Accent, _cover.Accent2, u);
                gfx.FillRoundedRectangle(bar, ColorF.Opacity(color, 0.14f + level * 0.3f), MathF.Min(width / 2f, 3f));
            }
        }

        private void RenderIdle(RendererCommon2D gfx, Rect content) {
            float y = content.CenterY - 30f;
            Style.Icon(gfx, Rect.FromCenter(new Vec2(content.CenterX, y), new Vec2(28f, 28f)), Style.Icons.Note, Style.Muted);
            gfx.RenderTextCentered(new Vec2(content.CenterX, y + 28f), Style.Text, "Nothing is playing", 1.2f);
            gfx.RenderTextCentered(new Vec2(content.CenterX, y + 46f), Style.Muted,
                Style.Fit(gfx, "Play music in any app (Spotify, YouTube Music, your browser...) and it shows up here.", content.Width - 30f, 0.75f), 0.75f);
            if (_controller.Error is { } error)
                gfx.RenderTextCentered(new Vec2(content.CenterX, y + 64f), Style.Red, Style.Fit(gfx, error, content.Width - 30f, 0.7f), 0.7f);
        }

        private void RenderNowPlaying(RendererCommon2D gfx, Rect content, NowPlaying now, Vec2 mouse) {
            const float padding = 18f;
            float beat = _visualizer.Beat;
            float coverSize = MathF.Min(content.Height - padding * 2f, content.Width * 0.42f);
            var coverRect = Rect.FromSize(content.X + padding, content.CenterY - coverSize / 2f, coverSize, coverSize);

            for (int i = 4; i >= 0; i--) {
                float grow = 4f + i * 4f + beat * 10f;
                gfx.FillRoundedRectangle(coverRect.Expand(grow), ColorF.Opacity(_cover.Accent, (0.05f + beat * 0.05f) * (1f - i / 5f)), 12f + grow);
            }
            if (_cover.TryGetTextures(gfx, out _, out var square)) {
                gfx.RenderTexture(coverRect, square, 1f);
            } else {
                gfx.FillRoundedRectangle(coverRect, Style.Surface, 10f);
                Style.Icon(gfx, Rect.FromCenter(coverRect.Center, new Vec2(coverSize * 0.35f, coverSize * 0.35f)), Style.Icons.Note, Style.Muted);
            }

            float x = coverRect.Z + padding + 4f;
            float width = content.Z - padding - x;
            float y = coverRect.Y + 4f;

            Style.Icon(gfx, Rect.FromSize(x, y, 10f, 10f), Style.Icons.Windows, Style.Muted);
            gfx.RenderText(new Vec2(x + 14f, y + 5f), Style.Muted, Style.Fit(gfx, $"Playing in {now.AppName}", width - 14f, 0.7f), TextAlignment.Left, TextAlignment.Center, 0.7f);
            y += 16f;

            string title = gfx.WrapText(now.Title, width, 1.5f, 2);
            gfx.RenderText(new Vec2(x, y), Style.Text, title, 1.5f);
            y += gfx.MeasureText(title, 1.5f).Y + 4f;
            gfx.RenderText(new Vec2(x, y), Style.Muted, Style.Fit(gfx, now.Artist, width, 0.95f), 0.95f);

            float controlsY = coverRect.W - 16f;
            float barY = controlsY - 34f;
            var bar = Rect.FromSize(x, barY, width, 4f);
            var hit = bar.Expand(0f, 6f);
            bool hover = now.CanSeek && now.Duration > TimeSpan.Zero && hit.Contains(mouse);
            gfx.FillRoundedRectangle(bar, Style.Faint, 2f);
            if (now.Duration > TimeSpan.Zero) {
                float fraction = (float)Math.Clamp(now.Position.TotalSeconds / now.Duration.TotalSeconds, 0, 1);
                gfx.FillRoundedRectangle(Rect.FromSize(bar.X, bar.Y, bar.Width * fraction, bar.Height), _cover.Accent, 2f);
                if (hover) gfx.FillCircle(new Vec2(bar.X + bar.Width * fraction, bar.CenterY), Style.Text, 5f);
                if (now.CanSeek) {
                    var duration = now.Duration;
                    _buttons.Add((hit, () => {
                        float at = Math.Clamp((Onix.Gui.MousePosition.X - bar.X) / bar.Width, 0f, 1f);
                        _controller.Seek(TimeSpan.FromSeconds(duration.TotalSeconds * at));
                    }));
                }
            }
            gfx.RenderText(new Vec2(bar.X, bar.W + 9f), Style.Muted, Style.Time(now.Position), TextAlignment.Left, TextAlignment.Center, 0.7f);
            gfx.RenderText(new Vec2(bar.Z, bar.W + 9f), Style.Muted, now.Duration > TimeSpan.Zero ? Style.Time(now.Duration) : "--:--", TextAlignment.Right, TextAlignment.Center, 0.7f);

            float cx = x + width / 2f;
            var playPause = Rect.FromCenter(new Vec2(cx, controlsY), new Vec2(30f, 30f));
            var previous = Rect.FromCenter(new Vec2(cx - 42f, controlsY), new Vec2(18f, 18f));
            var next = Rect.FromCenter(new Vec2(cx + 42f, controlsY), new Vec2(18f, 18f));
            IconButton(gfx, previous, Style.Icons.Previous, mouse, _controller.Previous, now.CanPrevious);
            gfx.FillCircle(playPause.Center, playPause.Contains(mouse) ? ColorF.White : Style.Text, 17f);
            Style.Icon(gfx, playPause.Shrink(8f), now.IsPlaying ? Style.Icons.Pause : Style.Icons.Play, Style.Background);
            _buttons.Add((playPause, _controller.PlayPause));
            IconButton(gfx, next, Style.Icons.Next, mouse, _controller.Next, now.CanNext);
        }

        private void RenderLyrics(RendererCommon2D gfx, Rect panel, NowPlaying? now, Vec2 mouse, float time, float delta, int scroll) {
            gfx.FillRoundedRectangle(panel, new ColorF(0f, 0f, 0f, 0.28f), 8f);
            var inner = panel.Shrink(12f);
            var state = _controller.Lyrics.State;
            gfx.RenderText(new Vec2(inner.X, inner.Y + 4f), Style.Muted, "LYRICS", TextAlignment.Left, TextAlignment.Center, 0.6f);
            RenderSourcePicker(gfx, new Vec2(inner.Z, inner.Y + 4f), state, mouse);
            var body = new Rect(inner.X, inner.Y + 16f, inner.Z, inner.W);

            if (now is null) {
                RenderLyricsMessage(gfx, body, "No lyrics found");
                return;
            }
            switch (state.Status) {
                case LyricsStatus.Loading:
                    string dots = new('.', 1 + (int)(time * 3f) % 3);
                    RenderLyricsMessage(gfx, body, $"Looking for lyrics{dots}");
                    return;
                case LyricsStatus.Found when state.Result is { } result:
                    RenderLyricLines(gfx, body, result, now, mouse, time, delta, scroll);
                    return;
                default:
                    RenderLyricsMessage(gfx, body, "No lyrics found");
                    return;
            }
        }

        private void RenderSourcePicker(RendererCommon2D gfx, Vec2 right, LyricsState state, Vec2 mouse) {
            var config = Musique.Config;
            string active = state.Result?.Source ?? (config.PreferGenius ? "Genius" : "Musixmatch");
            float x = right.X;
            foreach (string source in new[] { "Genius", "Musixmatch" }) {
                bool selected = source == active;
                string label = selected && state.Result is { Synced: true } ? $"{source} - synced" : source;
                float width = gfx.MeasureText(label, 0.6f).X + 10f;
                var chip = Rect.FromSize(x - width, right.Y - 6f, width, 12f);
                bool hover = chip.Contains(mouse);
                var fill = selected ? ColorF.Opacity(_cover.Accent, hover ? 0.55f : 0.4f) : hover ? Style.Faint : ColorF.Opacity(ColorF.White, 0.04f);
                gfx.FillRoundedRectangle(chip, fill, 6f);
                gfx.RenderTextCentered(chip.Center, selected ? Style.Text : Style.Muted, label, 0.6f);
                bool genius = source == "Genius";
                _buttons.Add((chip, () => {
                    config.PreferGenius = genius;
                    if (genius) config.GeniusLyrics = true;
                    else config.MusixmatchLyrics = true;
                }));
                x -= width + 4f;
            }
        }

        private static void RenderLyricsMessage(RendererCommon2D gfx, Rect body, string message) {
            string wrapped = gfx.WrapText(message, body.Width - 10f, 0.8f, 3);
            gfx.RenderTextCentered(new Vec2(body.CenterX, body.CenterY - 8f), Style.Muted, wrapped, 0.8f);
        }

        private void RenderLyricLines(RendererCommon2D gfx, Rect body, LyricsResult result, NowPlaying now, Vec2 mouse, float time, float delta, int scroll) {
            if (!ReferenceEquals(result, _layoutResult) || MathF.Abs(_layoutWidth - body.Width) > 0.5f) {
                bool fresh = !ReferenceEquals(result, _layoutResult);
                BuildLayout(gfx, result, body.Width);
                if (fresh) {
                    _scroll = 0f;
                    _scrollTarget = 0f;
                    _manualUntil = 0f;
                }
            }

            float maxScroll = MathF.Max(0f, _layoutHeight - body.Height);
            if (scroll != 0 && body.Contains(mouse)) {
                _scrollTarget = Math.Clamp(_scrollTarget + scroll * 28f, 0f, maxScroll);
                _manualUntil = time + 4f;
            }

            int current = result.IndexAt(now.Position + TimeSpan.FromMilliseconds(250));
            if (result.Synced && time >= _manualUntil) {
                float focus = current >= 0 ? _layout[current].Top + _layout[current].Height / 2f : 0f;
                _scrollTarget = Math.Clamp(focus - body.Height * 0.4f, 0f, maxScroll);
            }
            _scroll += (_scrollTarget - _scroll) * Math.Clamp(delta * 9f, 0f, 1f);

            const float fade = 22f;
            for (int i = 0; i < _layout.Count; i++) {
                var (top, height, text) = _layout[i];
                float y = body.Y + top - _scroll;
                if (y < body.Y - 0.5f || y + height > body.W + 0.5f) continue;
                if (text.Length == 0) continue;
                float edge = MathF.Min(y - body.Y, body.W - (y + height));
                float alpha = Math.Clamp(edge / fade + 0.25f, 0.25f, 1f);
                ColorF color;
                if (!result.Synced) {
                    color = ColorF.Opacity(Style.Text, 0.9f * alpha);
                } else if (i == current) {
                    color = ColorF.Opacity(ColorF.Lerp(Style.Text, _cover.Accent, 0.2f), alpha);
                    gfx.FillRoundedRectangle(Rect.FromSize(body.X - 7f, y + 1f, 2.5f, height - 2f), ColorF.Opacity(_cover.Accent, alpha), 1.25f);
                } else {
                    color = ColorF.Opacity(Style.Muted, (i < current ? 0.55f : 0.85f) * alpha);
                }

                var area = Rect.FromSize(body.X, y, body.Width, height);
                if (result.Synced && now.CanSeek) {
                    if (area.Contains(mouse) && i != current) gfx.FillRoundedRectangle(area.Expand(3f, 1f), Style.Faint, 4f);
                    var at = result.Lines[i].Time;
                    _buttons.Add((area, () => {
                        _manualUntil = 0f;
                        _controller.Seek(at);
                    }));
                }
                gfx.RenderText(new Vec2(body.X, y), color, text, LyricsScale);
            }

            if (maxScroll > 1f) {
                float trackHeight = body.Height;
                float thumb = MathF.Max(16f, trackHeight * body.Height / _layoutHeight);
                float thumbY = body.Y + (trackHeight - thumb) * (_scroll / maxScroll);
                gfx.FillRoundedRectangle(Rect.FromSize(body.Z + 5f, thumbY, 2f, thumb), ColorF.Opacity(ColorF.White, 0.18f), 1f);
            }
        }

        private void BuildLayout(RendererCommon2D gfx, LyricsResult result, float width) {
            _layout.Clear();
            _layoutResult = result;
            _layoutWidth = width;
            float top = 0f;
            float lineHeight = gfx.MeasureText("Ag", LyricsScale).Y;
            foreach (var line in result.Lines) {
                if (line.Text.Length == 0) {
                    string blank = result.Synced ? "..." : "";
                    float gapHeight = result.Synced ? lineHeight : lineHeight * 0.5f;
                    _layout.Add((top, gapHeight, blank));
                    top += gapHeight + LyricsGap;
                    continue;
                }
                string wrapped = gfx.WrapText(line.Text, width, LyricsScale, 4);
                float height = MathF.Max(lineHeight, gfx.MeasureText(wrapped, LyricsScale).Y);
                _layout.Add((top, height, wrapped));
                top += height + LyricsGap;
            }
            _layoutHeight = MathF.Max(0f, top - LyricsGap);
        }

        private void IconButton(RendererCommon2D gfx, Rect area, string icon, Vec2 mouse, Action onClick, bool enabled) {
            if (enabled && area.Contains(mouse)) gfx.FillCircle(area.Center, Style.Faint, area.Width * 0.8f);
            Style.Icon(gfx, area, icon, enabled ? Style.Text : ColorF.Opacity(Style.Text, 0.3f));
            if (enabled) _buttons.Add((area, onClick));
        }

        private void UpdateWindow(Rect screen, Vec2 mouse) {
            var config = Musique.Config;
            var maxSize = new Vec2(screen.Width, screen.Height);
            var fallback = config.ShowLyrics ? LyricsWindowSize : DefaultWindowSize;
            var size = config.WindowSize.X > 0f && config.WindowSize.Y > 0f ? config.WindowSize : fallback;
            var position = config.WindowPosition.X >= 0f && config.WindowPosition.Y >= 0f
                ? config.WindowPosition
                : new Vec2(screen.CenterX - size.X / 2f, screen.CenterY - size.Y / 2f);

            if (_resizing) size = _resizeStartSize + (mouse - _resizeStart);
            if (_dragging) position = mouse - _dragOffset;

            size = new Vec2(
                Math.Clamp(size.X, MathF.Min(MinWindowSize.X, maxSize.X), maxSize.X),
                Math.Clamp(size.Y, MathF.Min(MinWindowSize.Y, maxSize.Y), maxSize.Y));
            position = new Vec2(
                Math.Clamp(position.X, screen.X, screen.Z - size.X),
                Math.Clamp(position.Y, screen.Y, screen.W - size.Y));

            _window = Rect.FromSize(position, size);
            _titleBar = Rect.FromSize(_window.X, _window.Y, _window.Width, TitleHeight);
            _close = Rect.FromSize(_titleBar.Z - TitleHeight - 2f, _titleBar.Y, TitleHeight + 2f, TitleHeight);
            _lyricsToggle = Rect.FromSize(_close.X - 50f, _titleBar.Y + 2f, 46f, TitleHeight - 4f);
            _grip = Rect.FromSize(_window.Z - GripSize, _window.W - GripSize, GripSize, GripSize);
        }

        private void ToggleLyrics() {
            var config = Musique.Config;
            config.ShowLyrics = !config.ShowLyrics;
            if (config.ShowLyrics && _window.Width < LyricsMinWidth + 2f) {
                var screen = RenderArea;
                float width = MathF.Min(LyricsWindowSize.X, screen.Width);
                float height = MathF.Max(_window.Height, MathF.Min(LyricsWindowSize.Y, screen.Height));
                float x = Math.Clamp(_window.CenterX - width / 2f, screen.X, MathF.Max(screen.X, screen.Z - width));
                config.WindowSize = new Vec2(width, height);
                config.WindowPosition = new Vec2(x, _window.Y);
            }
        }

        private void SaveWindow() {
            var config = Musique.Config;
            config.WindowPosition = new Vec2(_window.X, _window.Y);
            config.WindowSize = new Vec2(_window.Width, _window.Height);
        }

        private void RenderTitleBar(RendererCommon2D gfx, Vec2 mouse) {
            gfx.FillRectangle(_titleBar, new ColorF(0.04f, 0.035f, 0.06f, 1f));
            Style.Icon(gfx, Rect.FromSize(_titleBar.X + 6f, _titleBar.Y + 3f, 12f, 12f), Style.Icons.Logo);
            gfx.RenderText(new Vec2(_titleBar.X + 22f, _titleBar.CenterY), Style.Muted, "Musique", TextAlignment.Left, TextAlignment.Center, 0.7f);

            bool on = Musique.Config.ShowLyrics;
            bool hover = _lyricsToggle.Contains(mouse);
            var fill = on ? ColorF.Opacity(_cover.Accent, hover ? 0.55f : 0.4f) : hover ? Style.Faint : ColorF.Opacity(ColorF.White, 0.04f);
            gfx.FillRoundedRectangle(_lyricsToggle, fill, 4f);
            gfx.RenderTextCentered(_lyricsToggle.Center, on ? Style.Text : Style.Muted, "Lyrics", 0.65f);

            if (_close.Contains(mouse)) gfx.FillRectangle(_close, new ColorF(0.9f, 0.15f, 0.2f, 1f));
            Style.Icon(gfx, _close.Shrink(5f), Style.Icons.Close, Style.Text);
            if (_dragging) gfx.FillRectangle(_titleBar, Style.Faint);
        }

        private void RenderGrip(RendererCommon2D gfx, Vec2 mouse) {
            var color = ColorF.Opacity(ColorF.White, _resizing || _grip.Contains(mouse) ? 0.7f : 0.3f);
            for (int row = 0; row < 3; row++) {
                for (int column = 0; column <= row; column++) {
                    float x = _grip.Z - 3f - column * 3f;
                    float y = _grip.W - 3f - (row - column) * 3f;
                    gfx.FillRectangle(Rect.FromSize(x - 0.75f, y - 0.75f, 1.5f, 1.5f), color);
                }
            }
        }

        public override bool OnInput(InputKey key, bool isDown) {
            var mouse = Onix.Gui.MousePosition;
            if (!isDown) {
                if (key.IsMouse && key.ClickInput == InputKey.ClickType.Left && (_dragging || _resizing)) {
                    _dragging = false;
                    _resizing = false;
                    SaveWindow();
                    return true;
                }
                return false;
            }

            if (key.IsMouse) {
                if (key.ClickInput != InputKey.ClickType.Left) return key.ClickInput != InputKey.ClickType.Scroll;
                if (_close.Contains(mouse)) {
                    CloseScreen();
                    return true;
                }
                if (_lyricsToggle.Contains(mouse)) {
                    ToggleLyrics();
                    return true;
                }
                if (_grip.Contains(mouse)) {
                    _resizing = true;
                    _resizeStart = mouse;
                    _resizeStartSize = new Vec2(_window.Width, _window.Height);
                    return true;
                }
                if (_titleBar.Contains(mouse)) {
                    _dragging = true;
                    _dragOffset = mouse - new Vec2(_window.X, _window.Y);
                    return true;
                }
                foreach (var (area, onClick) in _buttons) {
                    if (!area.Contains(mouse)) continue;
                    onClick();
                    return true;
                }
                return true;
            }

            var config = Musique.Config;
            if (key == config.OpenPlayerKey) {
                CloseScreen();
                return true;
            }
            if (key == config.PlayPauseKey || key.Value == InputKey.Type.Space) {
                _controller.PlayPause();
                return true;
            }
            if (key == config.NextKey) {
                _controller.Next();
                return true;
            }
            if (key == config.PreviousKey) {
                _controller.Previous();
                return true;
            }
            return false;
        }
    }
}
