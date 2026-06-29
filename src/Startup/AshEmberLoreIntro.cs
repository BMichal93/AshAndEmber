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
        // The opening lore, split into paragraphs. Moved here from the old
        // ShowLoreIntro pop-up; the wording is unchanged.
        private static readonly string[] Paragraphs =
        {
            "All living things carry an Inner Fire. It is not warmth or light — " +
            "it is the force that binds soul to body, that separates the living from the cold. " +
            "Those who learn to feel it are called mages. Those who learn to spend it are called dangerous.",

            "The Temple of Ember has governed the use of fire for three hundred years. " +
            "Its scholars catalogued the schools of magic, trained the first court mages, " +
            "and wrote the laws that still govern who may cast and what may be worked. " +
            "Most kingdoms respect those laws. Most.",

            "The Tribes of the East do not. Beyond the mountain passes the empire never fully held, " +
            "they have practiced a rougher, older kind of fire — passed down through rites " +
            "rather than texts, and answerable to no Temple canon. " +
            "They are not unified, and they are not going away.",

            "The empire itself is fracturing. Three claimants hold three portions of it. " +
            "Its roads are still the best in the world. Its armies are fighting each other.",

            "Into this, the Ashen have begun to move. They are lords — or were, long ago — " +
            "who extinguished their own Inner Fire rather than let it burn out with age. " +
            "They do not die. They do not tire. " +
            "For centuries they kept to the northern wastes. That appears to be changing.",

            "You are a mage. The rest of your story is your own to make.",
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
