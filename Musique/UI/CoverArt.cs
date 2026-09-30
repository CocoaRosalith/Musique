using OnixRuntime.Api.Maths;
using OnixRuntime.Api.Rendering;
using OnixRuntime.Api.Utils;

namespace Musique.UI {
    public sealed class CoverArt {
        private const int TextureSize = 256;

        private sealed record Processed(string Key, int Version, RawImageData Circle, RawImageData Square, ColorF Accent, ColorF Accent2);

        private sealed class RendererSlot {
            public int Shown = -1;
            public int Uploading = -1;
            public int UploadedVersion = -1;
            public int ShownVersion = -1;
            public int PendingFrames;
        }

        private readonly Dictionary<Type, RendererSlot> _slots = [];
        private string? _key;
        private int _version;
        private volatile Processed? _processed;
        private ColorF _accentTarget = Style.DefaultAccent;
        private ColorF _accent2Target = Style.DefaultAccent2;

        public ColorF Accent { get; private set; } = Style.DefaultAccent;
        public ColorF Accent2 { get; private set; } = Style.DefaultAccent2;

        public void Update((string Key, byte[] Bytes)? cover, float delta) {
            string? key = cover?.Key;
            if (key != _key) {
                _key = key;
                int version = ++_version;
                if (cover is { } c) {
                    var bytes = c.Bytes;
                    _ = Task.Run(() => {
                        try {
                            var result = Process(c.Key, version, bytes);
                            if (version == _version) _processed = result;
                        } catch (Exception ex) {
                            Console.WriteLine($"Musique: couldn't decode cover: {ex.Message}");
                        }
                    });
                } else {
                    _processed = null;
                    _accentTarget = Style.DefaultAccent;
                    _accent2Target = Style.DefaultAccent2;
                }
            }

            if (_processed is { } processed && processed.Version == _version) {
                _accentTarget = processed.Accent;
                _accent2Target = processed.Accent2;
            }
            float t = Math.Clamp(delta * 3f, 0f, 1f);
            Accent = ColorF.Lerp(Accent, _accentTarget, t);
            Accent2 = ColorF.Lerp(Accent2, _accent2Target, t);
        }

        public bool TryGetTextures(RendererCommon2D gfx, out TexturePath circle, out TexturePath square) {
            var type = gfx.GetType();
            if (!_slots.TryGetValue(type, out var slot)) {
                slot = new RendererSlot();
                _slots[type] = slot;
            }
            string prefix = $"Musique/{type.Name}";
            var processed = _processed;

            if (processed is not null && processed.Version == _version && slot.UploadedVersion != processed.Version && slot.Uploading < 0) {
                int next = slot.Shown == 0 ? 1 : 0;
                gfx.UploadTexture(TexturePath.Raw($"{prefix}/circle_{next}"), processed.Circle);
                gfx.UploadTexture(TexturePath.Raw($"{prefix}/square_{next}"), processed.Square);
                slot.Uploading = next;
                slot.UploadedVersion = processed.Version;
                slot.PendingFrames = 0;
            }

            if (slot.Uploading >= 0) {
                var pendingCircle = TexturePath.Raw($"{prefix}/circle_{slot.Uploading}");
                var pendingSquare = TexturePath.Raw($"{prefix}/square_{slot.Uploading}");
                var circleStatus = gfx.GetTextureStatus(pendingCircle);
                var squareStatus = gfx.GetTextureStatus(pendingSquare);
                if (circleStatus == RendererTextureStatus.Loaded && squareStatus == RendererTextureStatus.Loaded) {
                    slot.Shown = slot.Uploading;
                    slot.ShownVersion = slot.UploadedVersion;
                    slot.Uploading = -1;
                } else if (circleStatus is RendererTextureStatus.Missing or RendererTextureStatus.Unloaded
                           && squareStatus is RendererTextureStatus.Missing or RendererTextureStatus.Unloaded
                           && ++slot.PendingFrames > 120) {
                    slot.Uploading = -1;
                    slot.UploadedVersion = -1;
                    slot.PendingFrames = 0;
                }
            } else if (slot.Shown >= 0) {
                var shownCircle = TexturePath.Raw($"{prefix}/circle_{slot.Shown}");
                if (gfx.GetTextureStatus(shownCircle) is RendererTextureStatus.Missing or RendererTextureStatus.Unloaded) {
                    slot.Shown = -1;
                    slot.ShownVersion = -1;
                    slot.UploadedVersion = -1;
                }
            }

            circle = TexturePath.Raw($"{prefix}/circle_{Math.Max(0, slot.Shown)}");
            square = TexturePath.Raw($"{prefix}/square_{Math.Max(0, slot.Shown)}");
            return slot.Shown >= 0 && _key is not null && slot.ShownVersion == _version;
        }

        private static Processed Process(string key, int version, byte[] bytes) {
            var image = new RawImageData(bytes);
            if (image.IsEmpty || image.Width < 4 || image.Height < 4) throw new InvalidDataException("empty image");
            byte[] data = image.Data;
            int width = image.Width, height = image.Height;

            var (left, top, right, bottom) = TrimBars(data, width, height);
            int side = Math.Min(right - left, bottom - top);
            int x0 = left + (right - left - side) / 2;
            int y0 = top + (bottom - top - side) / 2;

            var cropped = new byte[side * side * 4];
            for (int y = 0; y < side; y++)
                Buffer.BlockCopy(data, ((y0 + y) * width + x0) * 4, cropped, y * side * 4, side * 4);

            var square = new RawImageData(cropped, side, side).Resized(TextureSize, TextureSize, RawImageData.ResizeFilter.Default);
            var circle = new RawImageData(cropped, side, side).Resized(TextureSize, TextureSize, RawImageData.ResizeFilter.Default);
            square.RoundImageCorners(TextureSize * 0.08f);
            circle.RoundImageCorners(TextureSize / 2f);

            var (accent, accent2) = PickAccents(cropped, side);
            return new Processed(key, version, circle, square, accent, accent2);
        }

        private static float Luminance(byte[] data, int index) =>
            (data[index] * 0.299f + data[index + 1] * 0.587f + data[index + 2] * 0.114f) / 255f;

        private static (int Left, int Top, int Right, int Bottom) TrimBars(byte[] data, int width, int height) {
            bool DarkRow(int y) {
                float sum = 0;
                for (int x = 0; x < width; x += 4) sum += Luminance(data, (y * width + x) * 4);
                return sum / ((width + 3) / 4) < 0.06f;
            }
            bool DarkColumn(int x, int top, int bottom) {
                float sum = 0;
                int count = 0;
                for (int y = top; y < bottom; y += 4, count++) sum += Luminance(data, (y * width + x) * 4);
                return count > 0 && sum / count < 0.06f;
            }

            int t = 0, b = height;
            while (t < height / 4 && DarkRow(t)) t++;
            while (b > height * 3 / 4 && DarkRow(b - 1)) b--;
            int l = 0, r = width;
            while (l < width / 4 && DarkColumn(l, t, b)) l++;
            while (r > width * 3 / 4 && DarkColumn(r - 1, t, b)) r--;
            if (r - l < 8 || b - t < 8) return (0, 0, width, height);
            return (l, t, r, b);
        }

        private static (ColorF, ColorF) PickAccents(byte[] data, int side) {
            double sx = 0, sy = 0, weightSum = 0, satSum = 0;
            int step = Math.Max(1, side / 48);
            int samples = 0;
            for (int y = 0; y < side; y += step) {
                for (int x = 0; x < side; x += step) {
                    int i = (y * side + x) * 4;
                    var (h, s, v) = ToHsv(data[i] / 255f, data[i + 1] / 255f, data[i + 2] / 255f);
                    double weight = s * s * v;
                    sx += Math.Cos(h * Math.Tau) * weight;
                    sy += Math.Sin(h * Math.Tau) * weight;
                    weightSum += weight;
                    satSum += s;
                    samples++;
                }
            }
            if (samples == 0 || weightSum < 0.5 || satSum / samples < 0.12) return (Style.DefaultAccent, Style.DefaultAccent2);
            double hue = Math.Atan2(sy, sx) / Math.Tau;
            if (hue < 0) hue += 1;
            var first = FromHsv((float)hue, 0.75f, 1f);
            var second = FromHsv((float)((hue + 0.08) % 1.0), 0.8f, 0.92f);
            return (first, second);
        }

        private static (float H, float S, float V) ToHsv(float r, float g, float b) {
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            float delta = max - min;
            float h = 0;
            if (delta > 0) {
                if (max == r) h = ((g - b) / delta % 6f + 6f) % 6f;
                else if (max == g) h = (b - r) / delta + 2f;
                else h = (r - g) / delta + 4f;
                h /= 6f;
            }
            return (h, max <= 0 ? 0 : delta / max, max);
        }

        private static ColorF FromHsv(float h, float s, float v) {
            float c = v * s;
            float x = c * (1 - Math.Abs(h * 6f % 2f - 1));
            float m = v - c;
            var (r, g, b) = (int)(h * 6f) switch {
                0 => (c, x, 0f),
                1 => (x, c, 0f),
                2 => (0f, c, x),
                3 => (0f, x, c),
                4 => (x, 0f, c),
                _ => (c, 0f, x),
            };
            return new ColorF(r + m, g + m, b + m, 1f);
        }
    }
}
