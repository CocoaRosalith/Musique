using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Musique.Playback {
    public sealed class SmtcSource : IDisposable {
        private readonly CancellationTokenSource _cts = new();
        private readonly Thread _thread;
        private readonly object _writeLock = new();

        private Process? _process;
        private volatile NowPlaying? _current;
        private volatile Tuple<string, byte[]>? _cover;
        private long _receivedTicks;

        public string? Error { get; private set; }
        public bool IsRunning { get; private set; }

        public SmtcSource() {
            _thread = new Thread(Supervise) { IsBackground = true, Name = "Musique SMTC" };
            _thread.Start();
        }

        public NowPlaying? Current {
            get {
                var current = _current;
                if (current is null || !current.IsPlaying) return current;
                var elapsed = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - Interlocked.Read(ref _receivedTicks));
                var position = current.Position + elapsed;
                if (current.Duration > TimeSpan.Zero && position > current.Duration) position = current.Duration;
                return current with { Position = position };
            }
        }

        public (string Key, byte[] Bytes)? Cover => _cover is { } cover ? (cover.Item1, cover.Item2) : null;

        private static string HelperPath {
            get {
                string assets = Musique.Instance.PluginAssetsPath.TrimEnd('\\', '/');
                string pluginFolder = Path.GetDirectoryName(assets) ?? assets;
                return Path.Combine(pluginFolder, "smtc", "SmtcHelper.dll");
            }
        }

        private static string DotnetPath {
            get {
                string runtime = RuntimeEnvironment.GetRuntimeDirectory();
                string root = Path.GetFullPath(Path.Combine(runtime, "..", "..", ".."));
                string dotnet = Path.Combine(root, "dotnet.exe");
                return File.Exists(dotnet) ? dotnet : "dotnet";
            }
        }

        private void Supervise() {
            int failures = 0;
            while (!_cts.IsCancellationRequested) {
                try {
                    RunHelper();
                    failures = 0;
                } catch (Exception ex) {
                    Error = $"Windows media helper failed: {ex.Message}";
                    failures++;
                }
                _current = null;
                IsRunning = false;
                if (_cts.IsCancellationRequested) break;
                try {
                    Task.Delay(Math.Min(30000, 2000 * (failures + 1)), _cts.Token).Wait();
                } catch {
                    break;
                }
            }
        }

        private void RunHelper() {
            string helper = HelperPath;
            if (!File.Exists(helper)) throw new FileNotFoundException($"missing {helper}");
            var info = new ProcessStartInfo(DotnetPath) {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            info.ArgumentList.Add(helper);
            info.Environment["DOTNET_ROOT"] = Path.GetDirectoryName(DotnetPath) ?? "";

            using var process = Process.Start(info) ?? throw new InvalidOperationException("couldn't start the helper");
            lock (_writeLock) _process = process;
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            using var registration = _cts.Token.Register(() => {
                try { process.Kill(true); } catch { }
            });

            string? line;
            while ((line = process.StandardOutput.ReadLine()) is not null) {
                try {
                    Handle(line);
                } catch (Exception ex) {
                    Error = $"Bad message from the Windows media helper: {ex.Message}";
                }
            }
            lock (_writeLock) _process = null;
            if (!_cts.IsCancellationRequested && Error is null) Error = "Windows media helper stopped, restarting...";
        }

        private void Handle(string line) {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            switch (root.GetProperty("t").GetString()) {
                case "ready":
                    IsRunning = true;
                    Error = null;
                    break;
                case "none":
                    _current = null;
                    _cover = null;
                    break;
                case "error":
                    Error = root.GetProperty("msg").GetString();
                    break;
                case "cover":
                    string key = root.GetProperty("key").GetString() ?? "";
                    var data = root.GetProperty("data");
                    _cover = data.ValueKind == JsonValueKind.String ? Tuple.Create($"smtc:{key}", Convert.FromBase64String(data.GetString()!)) : null;
                    break;
                case "state":
                    IsRunning = true;
                    Error = null;
                    string app = root.GetProperty("app").GetString() ?? "";
                    string title = root.GetProperty("title").GetString() ?? "";
                    _current = new NowPlaying(
                        title.Length > 0 ? title : "Unknown title",
                        root.GetProperty("artist").GetString() ?? "",
                        FriendlyAppName(app),
                        TimeSpan.FromMilliseconds(root.GetProperty("position").GetInt64()),
                        TimeSpan.FromMilliseconds(root.GetProperty("duration").GetInt64()),
                        root.GetProperty("playing").GetBoolean(),
                        root.GetProperty("previous").GetBoolean(),
                        root.GetProperty("next").GetBoolean(),
                        root.GetProperty("seek").GetBoolean(),
                        $"smtc:{root.GetProperty("key").GetString()}");
                    Interlocked.Exchange(ref _receivedTicks, DateTime.UtcNow.Ticks);
                    break;
            }
        }

        public static string FriendlyAppName(string appId) {
            string id = appId.ToLowerInvariant();
            if (id.Contains("youtube-music") || id.Contains("youtubemusic")) return "YouTube Music";
            if (id.Contains("spotify")) return "Spotify";
            if (id.Contains("opera")) return "Opera";
            if (id.Contains("chrome")) return "Chrome";
            if (id.Contains("msedge") || id.Contains("microsoftedge")) return "Edge";
            if (id.Contains("firefox")) return "Firefox";
            if (id.Contains("deezer")) return "Deezer";
            if (id.Contains("vlc")) return "VLC";
            if (id.Contains("zunemusic") || id.Contains("media player")) return "Media Player";
            string name = appId.Split('!')[0];
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            int dot = name.LastIndexOf('.');
            return dot >= 0 && dot < name.Length - 1 ? name[(dot + 1)..] : name;
        }

        private void Command(string command) {
            lock (_writeLock) {
                try {
                    _process?.StandardInput.WriteLine(command);
                    _process?.StandardInput.Flush();
                } catch {
                }
            }
        }

        public void PlayPause() => Command("toggle");
        public void Next() => Command("next");
        public void Previous() => Command("previous");
        public void Seek(TimeSpan position) => Command($"seek {(long)position.TotalMilliseconds}");

        public void Dispose() {
            lock (_writeLock) {
                try { _process?.StandardInput.Close(); } catch { }
            }
            _cts.Cancel();
            _thread.Join(1500);
        }
    }
}
