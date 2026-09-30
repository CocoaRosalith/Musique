using Musique.Playback;
using Musique.UI;
using OnixRuntime.Api;
using OnixRuntime.Api.Inputs;
using OnixRuntime.Api.Maths;
using OnixRuntime.Api.OnixClient;
using OnixRuntime.Plugin;

namespace Musique {
    public class Musique : OnixPluginBase {
        public static Musique Instance { get; private set; } = null!;
        public static MusiqueConfig Config { get; private set; } = null!;

        private MusicController? _controller;
        private CoverArt? _cover;
        private PlayerHud? _hud;
        private PlayerScreen? _screen;
        private OnixModuleVisual? _hudModule;

        public Musique(OnixPluginInitInfo initInfo) : base(initInfo) {
            Instance = this;
            base.DisablingShouldUnloadPlugin = false;
        }

        protected override void OnLoaded() {
            Config = new MusiqueConfig(PluginDisplayModule, true);
            _controller = new MusicController();
            _cover = new CoverArt();
            _hud = new PlayerHud(_controller, _cover);
            _screen = new PlayerScreen(_controller, _cover);

            _hudModule = new OnixModuleVisual(
                "Musique Player",
                "Shows the song that's playing, with its cover and progress. Drag it in the HUD editor.",
                new Vec2(10, 60),
                OnixModuleVisual.VisualAnchor.TopLeft,
                PlayerHud.BaseSize,
                "musique_player_hud") {
                Enabled = true,
                UsesLegacyRenderer = false,
            };
            PlayerHud.Restore(_hudModule);
            _hudModule.Render += _hud.Render;
        }

        protected override void OnEnabled() {
            Onix.Events.Input.Input += OnInput;
        }

        protected override void OnDisabled() {
            Onix.Events.Input.Input -= OnInput;
            _screen?.CloseScreen();
        }

        private bool OnInput(InputKey key, bool isDown) {
            if (!isDown || !Onix.Gui.MouseGrabbed || _controller is null) return false;
            if (key == Config.OpenPlayerKey) return _screen?.OpenScreen() ?? false;
            if (key == Config.PlayPauseKey) {
                _controller.PlayPause();
                return true;
            }
            if (key == Config.NextKey) {
                _controller.Next();
                return true;
            }
            if (key == Config.PreviousKey) {
                _controller.Previous();
                return true;
            }
            return false;
        }

        protected override void OnUnloaded() {
            Onix.Events.Input.Input -= OnInput;
            if (_hudModule is not null && _hud is not null) _hudModule.Render -= _hud.Render;
            _screen?.Dispose();
            _controller?.Dispose();
            _screen = null;
            _controller = null;
        }
    }
}
