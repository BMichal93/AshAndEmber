// =============================================================================
// LIFE & DEATH MAGIC — MagicInputHandler.cs
// Two-phase input: form keys before Break, effect keys after Break.
//
// KEYS (while holding Left Alt / LB):
//   W = U (form: Blast   / effect: Damage)
//   A = L (form: Missile / effect: Damage)
//   D = R (form: Barrier / effect: Damage)
//   S = D (form: Burst   / effect: Restore)
//   X = Break (keyboard, while form buffer has input)
//   L3 click = Break (gamepad)
//   X = Spellbook (keyboard, while form buffer is empty)
//   LB + RB = Spellbook (gamepad)
//
// Release Alt/LB → SpellBuilder.Parse + SpellBuilder.Execute
// =============================================================================

using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static class MagicInputHandler
    {
        private static string _formBuffer   = "";
        private static string _effectBuffer = "";
        private static bool   _inEffectPhase  = false;
        private static bool   _wasFocusing    = false;
        private static string _lastDisplayed  = "";
        private const  int    MaxLen          = 10;

        private static bool _prevLUp, _prevLDown, _prevLLeft, _prevLRight;
        private static bool _prevBreakPad;

        public static bool InputSuppressed { get; private set; }

        public static void Tick(bool inMission)
        {
            ColourKnowledge.FlushDeferredInquiry();
            if (!MageKnowledge.IsMage) { InputSuppressed = false; return; }

            bool focusingPad = Input.IsKeyDown(InputKey.ControllerLBumper);
            bool focusingKb  = Input.IsKeyDown(InputKey.LeftAlt) && !focusingPad;
            bool focusing    = focusingKb || focusingPad;

            InputSuppressed = focusing;

            if (focusing)
            {
                if (!_wasFocusing && inMission)
                    try { if (Agent.Main != null) SpellEffects.BeginCastLoop(Agent.Main); } catch { }

                _wasFocusing = true;

                if (!inMission && Campaign.Current != null)
                    Campaign.Current.TimeControlMode = CampaignTimeControlMode.UnstoppablePlay;

                // Spellbook: LB + RB (gamepad)
                if (focusingPad && Input.IsKeyPressed(InputKey.ControllerRBumper))
                {
                    ColourKnowledge.ShowGrimoire(inMission, true);
                    return;
                }

                // Spellbook: Alt + X (keyboard, only when no form has been started)
                if (focusingKb && Input.IsKeyPressed(InputKey.X)
                    && _formBuffer.Length == 0 && !_inEffectPhase)
                {
                    ColourKnowledge.ShowGrimoire(inMission, false);
                    return;
                }

                if (focusingKb)
                {
                    if (!_inEffectPhase)
                    {
                        // X acts as Break once any form key has been pressed
                        if (Input.IsKeyPressed(InputKey.X))
                        {
                            if (_formBuffer.Length > 0) _inEffectPhase = true;
                        }
                        else
                        {
                            if (Input.IsKeyPressed(InputKey.W)) AppendForm("U");
                            else if (Input.IsKeyPressed(InputKey.A)) AppendForm("L");
                            else if (Input.IsKeyPressed(InputKey.D)) AppendForm("R");
                            else if (Input.IsKeyPressed(InputKey.S)) AppendForm("D");
                        }
                    }
                    else
                    {
                        if      (Input.IsKeyPressed(InputKey.W)) AppendEffect("U");
                        else if (Input.IsKeyPressed(InputKey.A)) AppendEffect("L");
                        else if (Input.IsKeyPressed(InputKey.D)) AppendEffect("R");
                        else if (Input.IsKeyPressed(InputKey.S)) AppendEffect("D");
                    }
                }

                if (focusingPad)
                {
                    bool lUp    = Input.IsKeyDown(InputKey.ControllerLStickUp);
                    bool lDown  = Input.IsKeyDown(InputKey.ControllerLStickDown);
                    bool lLeft  = Input.IsKeyDown(InputKey.ControllerLStickLeft);
                    bool lRight = Input.IsKeyDown(InputKey.ControllerLStickRight);
                    bool l3     = Input.IsKeyDown(InputKey.ControllerLThumb);

                    // L3 = Break
                    if (!_inEffectPhase && l3 && !_prevBreakPad && _formBuffer.Length > 0)
                        _inEffectPhase = true;

                    if (!_inEffectPhase)
                    {
                        if (lUp    && !_prevLUp)   AppendForm("U");
                        if (lDown  && !_prevLDown)  AppendForm("D");
                        if (lLeft  && !_prevLLeft)  AppendForm("L");
                        if (lRight && !_prevLRight) AppendForm("R");
                    }
                    else
                    {
                        if (lUp    && !_prevLUp)   AppendEffect("U");
                        if (lDown  && !_prevLDown)  AppendEffect("D");
                        if (lLeft  && !_prevLLeft)  AppendEffect("L");
                        if (lRight && !_prevLRight) AppendEffect("R");
                    }

                    _prevLUp = lUp; _prevLDown = lDown; _prevLLeft = lLeft; _prevLRight = lRight;
                    _prevBreakPad = l3;
                }

                // Buffer display
                string display = _inEffectPhase
                    ? $"{_formBuffer} ▷ {_effectBuffer}"
                    : _formBuffer;
                if (display.Length > 0 && display != _lastDisplayed)
                {
                    _lastDisplayed = display;
                    InformationManager.DisplayMessage(new InformationMessage(
                        "[ " + display + " ]", new Color(0.7f, 0.7f, 0.7f)));
                }
            }
            else if (_wasFocusing)
            {
                _wasFocusing = false;
                _prevLUp = _prevLDown = _prevLLeft = _prevLRight = false;
                _prevBreakPad = false;
                _lastDisplayed = "";

                if (!inMission && Campaign.Current != null)
                    Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;

                // Stop the looping animation before casting — TryCastAnimation takes over from here
                if (inMission)
                    try { SpellEffects.EndCastLoop(Agent.Main); } catch { }

                TryCast(inMission);

                _formBuffer    = "";
                _effectBuffer  = "";
                _inEffectPhase = false;
            }
        }

        public static void ResetInputState()
        {
            _formBuffer    = "";
            _effectBuffer  = "";
            _inEffectPhase = false;
            _wasFocusing   = false;
            _lastDisplayed = "";
            _prevLUp = _prevLDown = _prevLLeft = _prevLRight = false;
            _prevBreakPad  = false;
            InputSuppressed = false;
        }

        private static void AppendForm(string dir)
        {
            if (_formBuffer.Length < MaxLen) _formBuffer += dir;
        }

        private static void AppendEffect(string dir)
        {
            if (_effectBuffer.Length < MaxLen) _effectBuffer += dir;
        }

        private static void TryCast(bool inMission)
        {
            if (_formBuffer.Length == 0) return;

            // Debug: Alt + UUDDLLRRULDR → unlock all talents
            if (!_inEffectPhase && _formBuffer == "UUDDLLRRULDR")
            {
                TalentSystem.UnlockAll();
                return;
            }

            if (!_inEffectPhase)
            {
                Fizzle("No Break — focus dropped.");
                return;
            }

            if (_effectBuffer.Length == 0)
            {
                Fizzle("Nothing shaped after the Break.");
                return;
            }

            if (!inMission)
            {
                // Campaign map — show talent menu instead of battle spells
                ColourKnowledge.ShowCampaignCastMenu();
                return;
            }

            try
            {
                if (Hero.MainHero != null && Hero.MainHero.IsPrisoner)
                {
                    Fizzle("You are bound. The fire cannot kindle.");
                    return;
                }
            }
            catch { }

            if (IsInTournament())
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "Sorcery in the tournament — you are disqualified!",
                    Color.FromUint(0xFFFF4444)));
                SpellEffects.QueueKill(Agent.Main);
                return;
            }

            SpellCast cast = SpellBuilder.Parse(_formBuffer, _effectBuffer);

            if (cast.IsFumble)
            {
                Fizzle("Fumble — the form slipped.");
                return;
            }

            if (!cast.HasAnyEffect)
            {
                Fizzle("Nothing resolved.");
                return;
            }

            bool hasBattleMage = TalentSystem.Has(TalentId.BattleMage);
            bool success = SpellBuilder.Execute(cast, inMission);

            if (success)
            {
                try { if (Agent.Main != null) SpellEffects.RecordMagicCast(Agent.Main.Position); } catch { }
                int agingDays = cast.AgingDays(hasBattleMage);
                if (agingDays > 0)
                {
                    if (MageKnowledge.IsAshen)
                        ApplyBlightCastCost(agingDays);
                    else
                        AgingSystem.AgeHero(Hero.MainHero, agingDays);
                }
                // Flashfire: 10% chance to echo the spell — no additional aging cost.
                if (inMission)
                    try { SpellEffects.TryFlashfire(cast); } catch { }
            }
        }

        private static bool IsInTournament()
        {
            if (Mission.Current == null) return false;
            foreach (MissionBehavior b in Mission.Current.MissionBehaviors)
                if (b.GetType().Name.Contains("Tournament")) return true;
            return false;
        }

        private static void ApplyBlightCastCost(int days)
        {
            try
            {
                if (Hero.MainHero?.MapFaction is TaleWorlds.CampaignSystem.Kingdom k)
                {
                    TaleWorlds.CampaignSystem.Actions.ChangeCrimeRatingAction.Apply(k, days * 5f, false);
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"The ash spreads. Eyes turn toward you. (+{days * 5} criminal rating)",
                        new Color(0.3f, 0.35f, 0.7f)));
                }
                else
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The cold fire stirs. The ash spreads.",
                        new Color(0.3f, 0.35f, 0.7f)));
                }
            }
            catch { }
        }

        private static void Fizzle(string msg) =>
            InformationManager.DisplayMessage(new InformationMessage(
                msg, Color.FromUint(0xFF997755)));
    }
}
