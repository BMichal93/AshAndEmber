// =============================================================================
// ASH AND EMBER — Startup/AshEmberLoadingScreen.cs
// Shows the ASH & EMBER black title card OVER the engine's loading screen for the
// duration of any load (save load, boot, entering/leaving battle). It reuses the
// same black-on-gold prefab as the opening splash, so no new art is needed.
//
// IMPORTANT: this is an additive overlay laid on TOP of the loading window — it does
// NOT replace the engine's loading prefab. (A GUI/Prefabs/LoadingWindow.xml override
// previously did that and wedged the boot, because the engine rendered a static card
// it could not dismiss. Never reintroduce that file.)
//
// Safety: the overlay is never shown over the main menu (InitialState), and a hard
// time cap force-hides it if a loading signal ever sticks, so it can NEVER block the
// game. If anything fails it degrades to a no-op and the vanilla screen shows through.
// Driven from MainSubModule.OnApplicationTick.
// =============================================================================

using System;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;

namespace AshAndEmber
{
    internal static class AshEmberLoadingScreen
    {
        // Hard safety cap. If a loading signal sticks on, force-hide after this many
        // seconds and stay down until it genuinely clears — so the card can never
        // wedge the game. Real loads finish well under this.
        private const float MaxHoldSeconds = 40f;

        private static bool  _active;
        private static float _shownElapsed;
        private static bool  _suppressUntilClear;

        private static GauntletLayer           _layer;
        private static ScreenBase              _hostScreen;
        private static AshEmberSplashVM        _vm;
        private static GauntletMovieIdentifier _movie;

        public static void Tick(float dt)
        {
            try
            {
                bool loading = IsLoading();

                if (!loading)
                {
                    _suppressUntilClear = false;   // load cleared — re-arm
                    if (_active) Hide();
                    return;
                }

                if (_suppressUntilClear) return;   // force-hidden this load; stay down

                if (!_active) { Show(); _shownElapsed = 0f; return; }

                _shownElapsed += dt;
                if (_shownElapsed >= MaxHoldSeconds)
                {
                    Hide();
                    _suppressUntilClear = true;
                }
            }
            catch
            {
                try { Hide(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        // True while the game is loading and it is safe to cover the screen. The
        // engine's own loading-window flag covers map<->battle transitions and save
        // loads; the state-name check additionally catches the boot GameLoadingState.
        // The main menu (InitialState) is explicitly excluded so the card can never
        // sit on top of the menu and block it.
        private static bool IsLoading()
        {
            try
            {
                var state = GameStateManager.Current?.ActiveState;
                if (state is InitialState) return false;

                try { if (LoadingWindow.IsLoadingWindowActive) return true; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                if (state != null &&
                    state.GetType().Name.IndexOf("Loading", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            return false;
        }

        private static void Show()
        {
            _hostScreen = ScreenManager.TopScreen;
            if (_hostScreen == null) return;

            _vm    = new AshEmberSplashVM { Subtitle = "Kindle the Inner Fire" };
            _layer = new GauntletLayer("AshEmberLoading", 6000, false);  // above the loading window
            _movie = _layer.LoadMovie("AshEmberSplash", _vm);
            _hostScreen.AddLayer(_layer);
            _active = true;
        }

        private static void Hide()
        {
            _active       = false;
            _shownElapsed = 0f;
            if (_layer != null)
            {
                // Remove from the screen we added to, and from the current top screen
                // in case the stack changed mid-load — whichever still holds it.
                try { if (_hostScreen != null && _hostScreen.HasLayer(_layer)) _hostScreen.RemoveLayer(_layer); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try
                {
                    var top = ScreenManager.TopScreen;
                    if (top != null && top != _hostScreen && top.HasLayer(_layer)) top.RemoveLayer(_layer);
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            try { _layer?.ReleaseMovie(_movie); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            _layer      = null;
            _hostScreen = null;
            _vm         = null;
        }
    }
}
