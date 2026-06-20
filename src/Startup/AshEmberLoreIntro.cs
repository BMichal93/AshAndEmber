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
            "Every living thing carries a fire. Not warmth. Not light. " +
            "The older kind — the one that binds soul to flesh, that keeps the living from the cold. " +
            "Mages have learned to reach into it. Some have learned to spend it.",

            "Long ago, in the frozen north, certain lords stared into their own dying flames and refused the dark. " +
            "So they did what no one had dared: they let the fire go out.\n\n" +
            "The cold did not kill them.",

            "They are the Ashen. They do not age. They do not grieve. They do not stop.\n\n" +
            "And they have been waiting a very long time.",

            "Now kingdoms crack. Emperors war over ruins. " +
            "And in the silence between the battles, the cold is already whispering — " +
            "and some mages have begun to whisper back.",

            "Something in you has been listening.\n\n" +
            "The fire knows. So does the cold.\n\n" +
            "Both are waiting to learn which way you will burn.",
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
