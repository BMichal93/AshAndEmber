// =============================================================================
// ASH AND EMBER — Startup/AshEmberLoreIntro.cs
// The opening lore cinematic. It plays in the slot the engine's intro video used
// to occupy — when character creation first appears for a new game — instead of
// the old "Embers and Ash" pop-up (which has been removed).
//
// The lore is shown one paragraph at a time on black: each paragraph fades in,
// holds, then slowly fades back to black before the next begins. Esc skips it.
// The layer is focusable so the character-creation screen beneath stays inert
// until the cinematic finishes (or is skipped), then it is torn down and the
// player creates their character as normal.
//
// Visuals: GUI/Prefabs/AshEmberLore.xml + the AshEmber.Lore.Body brush.
// Everything is guarded so a missing asset or engine change degrades to a no-op
// (you simply land on character creation) rather than a crash.
// =============================================================================

using TaleWorlds.CampaignSystem.CharacterCreationContent;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace AshAndEmber
{
    public class AshEmberLoreVM : ViewModel
    {
        private string _paragraphText = "";
        private float  _textAlpha;
        private string _skipHint  = "Press Esc to skip";
        private float  _hintAlpha = 0.45f;

        [DataSourceProperty]
        public string ParagraphText
        {
            get => _paragraphText;
            set { if (value != _paragraphText) { _paragraphText = value; OnPropertyChangedWithValue(value, "ParagraphText"); } }
        }

        [DataSourceProperty]
        public float TextAlpha
        {
            get => _textAlpha;
            set { if (value != _textAlpha) { _textAlpha = value; OnPropertyChangedWithValue(value, "TextAlpha"); } }
        }

        [DataSourceProperty]
        public string SkipHint
        {
            get => _skipHint;
            set { if (value != _skipHint) { _skipHint = value; OnPropertyChangedWithValue(value, "SkipHint"); } }
        }

        [DataSourceProperty]
        public float HintAlpha
        {
            get => _hintAlpha;
            set { if (value != _hintAlpha) { _hintAlpha = value; OnPropertyChangedWithValue(value, "HintAlpha"); } }
        }
    }

    internal static class AshEmberLoreIntro
    {
        // The opening lore, split into paragraphs.
        private static readonly string[] Paragraphs =
        {
            "First, there was the fire. The power that breathed life into the world, raised mountains, stilled seas. " +
            "Then came humans. They harnessed the fire and built a great empire upon it. With the fire, humans built great cities, raised armies and brought order to the world.",

            "The order lasted a thousand years under the banner of the Empire and aegis of the Temple. " +
            "There were some who wanted to use the Fire. " +
            "In the East, sorcerer-kings raised an army to ride against the first empire. They brought fire and blood.",

            "Then, the last emperor died, and his council and family reached for his crown. The empire was shattered. " +
            "Now, the fire fades. The ancient forces raise their head in the north.",

            "The Ashen mages who defied death are gathering their strengths, and when the time comes, nothing will stop the grey march from the wastelands. " +
            "In the last days of the Empire, among the ruins, you began your journey. Where it will lead you, O Firelord?",
        };

        // Per-paragraph timing (seconds).
        private const float FadeInDur  = 1.3f;
        private const float HoldDur    = 2.8f;
        private const float FadeOutDur = 1.9f;
        private const float GapDur     = 0.5f;

        private enum Phase { FadeIn, Hold, FadeOut, Gap }

        private static bool   _active;
        private static int    _index;
        private static Phase  _phase;
        private static float  _phaseTime;
        private static bool   _prevEsc;
        private static object _lastTriggerState;

        private static GauntletLayer           _layer;
        private static ScreenBase              _hostScreen;
        private static AshEmberLoreVM          _vm;
        private static GauntletMovieIdentifier _movie;

        public static void Tick(float dt)
        {
            try
            {
                if (_active) { Advance(dt); return; }

                // Trigger once per new-game character-creation state instance.
                var state = GameStateManager.Current?.ActiveState;
                if (state is CharacterCreationState && !ReferenceEquals(state, _lastTriggerState)
                    && ScreenManager.TopScreen != null)
                {
                    _lastTriggerState = state;
                    Show();
                }
            }
            catch
            {
                try { Finish(); } catch { }
            }
        }

        private static void Show()
        {
            _hostScreen = ScreenManager.TopScreen;
            if (_hostScreen == null) return;

            _vm    = new AshEmberLoreVM();
            _layer = new GauntletLayer("AshEmberLore", 5000, false);
            _movie = _layer.LoadMovie("AshEmberLore", _vm);
            _layer.IsFocusLayer = true;               // hold the screen beneath inert
            _hostScreen.AddLayer(_layer);

            _index     = 0;
            _phase     = Phase.FadeIn;
            _phaseTime = 0f;
            _prevEsc   = true;                          // ignore an Esc already held on entry
            _active    = true;

            _vm.ParagraphText = Paragraphs[0];
            _vm.TextAlpha     = 0f;
        }

        private static void Advance(float dt)
        {
            // Esc skips the whole cinematic.
            bool esc = Input.IsKeyDown(InputKey.Escape);
            if (esc && !_prevEsc) { Finish(); return; }
            _prevEsc = esc;

            _phaseTime += dt;
            switch (_phase)
            {
                case Phase.FadeIn:
                    _vm.TextAlpha = Clamp01(_phaseTime / FadeInDur);
                    if (_phaseTime >= FadeInDur) { _vm.TextAlpha = 1f; Go(Phase.Hold); }
                    break;

                case Phase.Hold:
                    if (_phaseTime >= HoldDur) Go(Phase.FadeOut);
                    break;

                case Phase.FadeOut:
                    _vm.TextAlpha = Clamp01(1f - _phaseTime / FadeOutDur);
                    if (_phaseTime >= FadeOutDur) { _vm.TextAlpha = 0f; Go(Phase.Gap); }
                    break;

                case Phase.Gap:
                    if (_phaseTime >= GapDur) Next();
                    break;
            }
        }

        private static void Go(Phase p) { _phase = p; _phaseTime = 0f; }

        private static void Next()
        {
            _index++;
            if (_index >= Paragraphs.Length) { Finish(); return; }
            _vm.ParagraphText = Paragraphs[_index];
            _vm.TextAlpha     = 0f;
            Go(Phase.FadeIn);
        }

        private static void Finish()
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

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
