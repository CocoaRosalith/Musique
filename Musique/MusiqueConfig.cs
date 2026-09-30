using OnixRuntime.Api.Inputs;
using OnixRuntime.Api.Maths;
using OnixRuntime.Api.OnixClient;

namespace Musique {
    public partial class MusiqueConfig : OnixModuleSettingRedirector {
        [Name("Open Player", "Opens the music player screen.")]
        [Value(InputKey.Type.K)]
        public partial InputKey OpenPlayerKey { get; set; }

        [Name("Play / Pause", "Plays or pauses the current song.")]
        [Value(InputKey.Type.F8)]
        public partial InputKey PlayPauseKey { get; set; }

        [Name("Previous", "Goes back to the previous song.")]
        [Value(InputKey.Type.F7)]
        public partial InputKey PreviousKey { get; set; }

        [Name("Next", "Skips to the next song.")]
        [Value(InputKey.Type.F9)]
        public partial InputKey NextKey { get; set; }

        [Name("Visualizer", "Makes the ring around the cover react to every frequency of the sound, not just the bass.")]
        [Value(true)]
        public partial bool BeatAnimation { get; set; }

        [Name("Musixmatch Lyrics", "Looks for synced lyrics on Musixmatch.")]
        [Value(true)]
        public partial bool MusixmatchLyrics { get; set; }

        [Name("Genius Lyrics", "Looks for lyrics on Genius when Musixmatch has no synced ones.")]
        [Value(true)]
        public partial bool GeniusLyrics { get; set; }

        [Name("HUD Lyrics", "Shows the current lyric line under the HUD player.")]
        [Value(true)]
        public partial bool HudLyrics { get; set; }

        [Name("Dark HUD", "Uses a dark background for the HUD player instead of a light one.")]
        [Value(false)]
        public partial bool DarkHud { get; set; }

        [Name("Hide When Idle", "Hides the HUD player when nothing is playing.")]
        [Value(true)]
        public partial bool HideWhenIdle { get; set; }

        [Name("HUD Size", "Makes the HUD player smaller or bigger.")]
        [Value(1f)]
        [MinMax(0.5f, 3f)]
        public partial float HudScale { get; set; }

        [Hidden]
        [Name("Prefer Genius", "Uses Genius first for lyrics instead of Musixmatch.")]
        [Value(false)]
        public partial bool PreferGenius { get; set; }

        [Hidden]
        [Name("Show Lyrics", "Shows the lyrics panel in the player window.")]
        [Value(true)]
        public partial bool ShowLyrics { get; set; }

        [Hidden]
        [Name("HUD Position", "Where the HUD player is on screen.")]
        [Value(-1f, -1f)]
        [MinMax(-1f, -1f, 10000f, 10000f)]
        public partial Vec2 HudPosition { get; set; }

        [Hidden]
        [Name("Player Window Position", "Where the player window was last placed.")]
        [Value(-1f, -1f)]
        [MinMax(-1f, -1f, 10000f, 10000f)]
        public partial Vec2 WindowPosition { get; set; }

        [Hidden]
        [Name("Player Window Size", "The last size of the player window.")]
        [Value(0f, 0f)]
        [MinMax(0f, 0f, 10000f, 10000f)]
        public partial Vec2 WindowSize { get; set; }
    }
}
