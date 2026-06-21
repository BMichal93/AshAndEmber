// =============================================================================
// ASH AND EMBER — Startup/AshEmberLoadingScreen.cs
// Shows the ASH & EMBER black title card OVER the engine's loading screen for the
// duration of any load (save load, entering/leaving battle, etc.). It reuses the
// same black-on-gold prefab as the opening splash, so no new art is needed.
//
// This is purely additive: the layer is laid on TOP of the normal loading window.
// If anything fails (no top screen, missing prefab, engine change) it degrades to
// a no-op and the vanilla loading screen simply shows through — it never breaks a
// load. Driven from MainSubModule.OnApplicationTick.
// =============================================================================

using System;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace AshAndEmber
{
    internal static class AshEmberLoadingScreen
    {
        private static bool _active;

        private static GauntletLayer           _layer;
        private static ScreenBase              _hostScreen;
        private static AshEmberSplashVM        _vm;
        private static GauntletMovieIdentifier _movie;

        public static void Tick(float dt)
        {
            try
            {
                bool loading = IsLoading();
                if (loading && !_active)      Show();
                else if (!loading && _active) Hide();
            }
            catch
            {
                try { Hide(); } catch { }
            }
        }

        // True while a loading game-state is active. Matched by type-name so it
        // covers GameLoadingState and any other *Loading* state without needing a
        // hard reference to each one.
        private static bool IsLoading()
        {
            try
            {
                var state = GameStateManager.Current?.ActiveState;
                if (state == null) return false;
                return state.GetType().Name.IndexOf("Loading", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private static void Show()
        {
            _hostScreen = ScreenManager.TopScreen;
            if (_hostScreen == null) return;

            // Reuse the splash prefab; a blank subtitle keeps it quiet during loads.
            _vm    = new AshEmberSplashVM { Subtitle = "" };
            _layer = new GauntletLayer("AshEmberLoading", 6000, false);  // above the loading window
            _movie = _layer.LoadMovie("AshEmberSplash", _vm);
            _hostScreen.AddLayer(_layer);
            _active = true;
        }

        private static void Hide()
        {
            _active = false;
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
