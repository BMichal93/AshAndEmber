// =============================================================================
// ASH AND EMBER — Startup/AshEmberSplash.cs
// A brief black title card shown over the main menu the first time it appears,
// after the engine's intro videos are skipped (see MainSubModule.SkipIntroVideos).
// It draws "ASH & EMBER" in a muted-gold serif on black for a couple of seconds,
// then removes itself — a quiet, Dark-Souls-flavoured opening.
//
// The visuals live in the module's GUI folder:
//   GUI/Brushes/AshEmber.xml          — the gold title/subtitle text brushes
//   GUI/Prefabs/AshEmberSplash.xml    — the black full-screen layout
// This file only owns the lifecycle: build the layer, hold it, tear it down.
// Everything is guarded so a missing asset or an engine change degrades to a
// no-op (you simply land on the menu) rather than a crash.
// =============================================================================

using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;

namespace AshAndEmber
{
    // The bound data for the splash prefab. Plain strings — no behaviour.
    public class AshEmberSplashVM : ViewModel
    {
        private string _title    = "ASH & EMBER";
        private string _subtitle = "Kindle the Inner Fire";

        [DataSourceProperty]
        public string Title
        {
            get => _title;
            set { if (value != _title) { _title = value; OnPropertyChangedWithValue(value, "Title"); } }
        }

        [DataSourceProperty]
        public string Subtitle
        {
            get => _subtitle;
            set { if (value != _subtitle) { _subtitle = value; OnPropertyChangedWithValue(value, "Subtitle"); } }
        }
    }

    // Drives the one-time title card. Ticked from MainSubModule.OnApplicationTick.
    internal static class AshEmberSplash
    {
        private const float HoldSeconds = 2.6f;

        private static bool  _done;       // shown and torn down — never again this run
        private static bool  _active;     // currently on screen
        private static float _elapsed;

        private static GauntletLayer           _layer;
        private static ScreenBase              _hostScreen;
        private static AshEmberSplashVM        _vm;
        private static GauntletMovieIdentifier _movie;

        public static void Tick(float dt)
        {
            try
            {
                if (_done) return;

                if (!_active)
                {
                    // Show once, the first time the main menu is the active state and
                    // the UI is up. By now the intro videos have been skipped.
                    if (GameStateManager.Current?.ActiveState is InitialState
                        && ScreenManager.TopScreen != null)
                        Show();
                    return;
                }

                _elapsed += dt;
                if (_elapsed >= HoldSeconds) Hide();
            }
            catch
            {
                // Never let the title card wedge the menu — bail out cleanly.
                try { Hide(); } catch { }
            }
        }

        private static void Show()
        {
            _hostScreen = ScreenManager.TopScreen;
            if (_hostScreen == null) return;

            _vm    = new AshEmberSplashVM();
            _layer = new GauntletLayer("AshEmberSplash", 4000, false);
            _movie = _layer.LoadMovie("AshEmberSplash", _vm);
            _hostScreen.AddLayer(_layer);
            _active = true;
        }

        private static void Hide()
        {
            _active = false;
            _done   = true;
            try
            {
                if (_hostScreen != null && _layer != null && _hostScreen.HasLayer(_layer))
                    _hostScreen.RemoveLayer(_layer);
            }
            catch { }
            try { _layer?.ReleaseMovie(_movie); } catch { }
            _layer      = null;
            _hostScreen = null;
            _vm         = null;
        }
    }
}
