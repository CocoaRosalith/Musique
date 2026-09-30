namespace Musique.Playback {
    public sealed record NowPlaying(
        string Title,
        string Artist,
        string AppName,
        TimeSpan Position,
        TimeSpan Duration,
        bool IsPlaying,
        bool CanPrevious,
        bool CanNext,
        bool CanSeek,
        string CoverKey);
}
