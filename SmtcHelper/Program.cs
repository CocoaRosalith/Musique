using System.Collections.Concurrent;
using System.Text.Json;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true, NewLine = "\n" };
var commands = new BlockingCollection<string>();
var stdin = new Thread(() => {
    try {
        string? line;
        while ((line = Console.In.ReadLine()) is not null) commands.Add(line.Trim());
    } catch {
    }
    commands.CompleteAdding();
}) { IsBackground = true };
stdin.Start();

void Send(object message) {
    try {
        output.WriteLine(JsonSerializer.Serialize(message));
    } catch {
        Environment.Exit(0);
    }
}

static T Wait<T>(IAsyncOperation<T> operation) => operation.AsTask().GetAwaiter().GetResult();

GlobalSystemMediaTransportControlsSessionManager manager;
try {
    manager = Wait(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
} catch (Exception ex) {
    Send(new { t = "error", msg = $"Windows media controls unavailable: {ex.Message}" });
    return 1;
}
Send(new { t = "ready" });

string lastKey = "";
string lastState = "";
long lastSent = 0;
while (!commands.IsCompleted) {
    GlobalSystemMediaTransportControlsSession? session = null;
    try {
        session = manager.GetCurrentSession();
        if (session is null) {
            if (lastState != "none") Send(new { t = "none" });
            lastState = "none";
            lastKey = "";
        } else {
            var properties = Wait(session.TryGetMediaPropertiesAsync());
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            string title = properties?.Title ?? "";
            string artist = properties?.Artist ?? "";
            if (artist.Length == 0) artist = properties?.AlbumArtist ?? "";
            string app = session.SourceAppUserModelId ?? "";
            string key = $"{app}|{artist}|{title}";
            bool playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var duration = timeline.EndTime - timeline.StartTime;
            var position = timeline.Position;
            if (playing && timeline.LastUpdatedTime.Year > 2000) position += DateTimeOffset.Now - timeline.LastUpdatedTime;
            if (duration > TimeSpan.Zero && position > duration) position = duration;

            if (key != lastKey) {
                lastKey = key;
                string? cover = null;
                if (properties?.Thumbnail is { } thumbnail) {
                    try {
                        using var stream = Wait(thumbnail.OpenReadAsync());
                        var bytes = new byte[stream.Size];
                        using var reader = new DataReader(stream);
                        Wait(reader.LoadAsync((uint)stream.Size));
                        reader.ReadBytes(bytes);
                        cover = Convert.ToBase64String(bytes);
                    } catch {
                    }
                }
                Send(new { t = "cover", key, data = cover });
            }

            string state = $"{key}|{playing}|{(long)duration.TotalMilliseconds}|{playback.Controls.IsNextEnabled}|{playback.Controls.IsPreviousEnabled}";
            long now = Environment.TickCount64;
            if (state != lastState || now - lastSent > 1000) {
                Send(new {
                    t = "state",
                    key,
                    app,
                    title,
                    artist,
                    playing,
                    position = (long)position.TotalMilliseconds,
                    duration = (long)Math.Max(0, duration.TotalMilliseconds),
                    next = playback.Controls.IsNextEnabled,
                    previous = playback.Controls.IsPreviousEnabled,
                    seek = playback.Controls.IsPlaybackPositionEnabled,
                });
                lastState = state;
                lastSent = now;
            }
        }
    } catch (Exception ex) {
        Send(new { t = "error", msg = ex.Message });
    }

    if (!commands.TryTake(out var command, 250)) continue;
    try {
        session = manager.GetCurrentSession();
        if (session is null) continue;
        var parts = command.Split(' ', 2);
        switch (parts[0]) {
            case "toggle": Wait(session.TryTogglePlayPauseAsync()); break;
            case "next": Wait(session.TrySkipNextAsync()); break;
            case "previous": Wait(session.TrySkipPreviousAsync()); break;
            case "seek" when parts.Length == 2 && long.TryParse(parts[1], out long ms):
                Wait(session.TryChangePlaybackPositionAsync(TimeSpan.FromMilliseconds(ms).Ticks));
                break;
        }
        lastState = "";
    } catch (Exception ex) {
        Send(new { t = "error", msg = $"Command failed: {ex.Message}" });
    }
}
return 0;
