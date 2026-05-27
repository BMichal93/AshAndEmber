// =============================================================================
// LIFE & DEATH MAGIC — MagicInputHandler.cs
// Two-phase input: form keys before Break, effect keys after Break.
//
// KEYS (while holding Left Alt / LB):
//   W = U (form: Blast / effect: Damage)
//   A = L (form: Aura  / effect: Push)
//   D = R (form: Barrier / effect: Morale)
//   S = D (form: Burst / effect: Reverse)
//   E = Break (keyboard)  — switches from form phase to effect phase
//   L3 click = Break (gamepad)
//   B = Spellbook (keyboard, while Alt held)
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

namespace ColoursOfCalradia
{
    public static class MagicInputHandler
    {
        private static string _formBuffer   = "";
        private static string _effectBuffer = "";
        private static bool   _inEffectPhase  = false;
        private static bool   _wasFocusing    = false;
        private static string _lastDisplayed  = "";
        private const  int    MaxLen          = 12; // raised to allow up to 3×ULDR ward repetitions

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
                _wasFocusing = true;

                if (!inMission && Campaign.Current != null)
                    Campaign.Current.TimeControlMode = CampaignTimeControlMode.UnstoppablePlay;

                // Spellbook: LB + RB (gamepad)
                if (focusingPad && Input.IsKeyPressed(InputKey.ControllerRBumper))
                {
                    ColourKnowledge.ShowGrimoire(inMission, true);
                    return;
                }

                // Spellbook: Alt + B (keyboard)
                if (focusingKb && Input.IsKeyPressed(InputKey.B))
                {
                    ColourKnowledge.ShowGrimoire(inMission, false);
                    return;
                }

                if (focusingKb)
                {
                    if (!_inEffectPhase)
                    {
                        if (Input.IsKeyPressed(InputKey.E))
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
                        if (Input.IsKeyPressed(InputKey.W)) AppendEffect("U");
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

                TryCast(inMission);

                _formBuffer    = "";
                _effectBuffer  = "";
                _inEffectPhase = false;
            }
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

            // Sigil: DD×N → Ward (no Break required, must not have entered effect phase)
            //   DD   = self only,  0m radius;  cost 1 day
            //   DDD  = 2m radius;  cost 2 days
            //   DDDD = 4m radius;  cost 3 days  (dCount-1 days, radius = (dCount-1)×2m)
            int dCount = _formBuffer.Length;
            if (!_inEffectPhase && dCount >= 2 && IsAllD(_formBuffer))
            {
                if (!inMission) { Fizzle("The ward only holds in battle."); return; }
                try { if (Hero.MainHero?.IsPrisoner == true) { Fizzle("You are bound. The fire cannot kindle."); return; } } catch { }
                SpellEffects.ExecuteWard(dCount);
                int cost = dCount - 1; // DD=1day, DDD=2days, DDDD=3days
                if (cost > 0)
                {
                    if (MageKnowledge.IsBlight) ApplyBlightCastCost(cost);
                    else AgingSystem.AgeHero(Hero.MainHero, cost);
                }
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
                    if (MageKnowledge.IsBlight)
                        ApplyBlightCastCost(agingDays);
                    else
                        AgingSystem.AgeHero(Hero.MainHero, agingDays);
                }
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

        private static bool IsAllD(string buf)
        {
            for (int i = 0; i < buf.Length; i++)
                if (buf[i] != 'D') return false;
            return true;
        }

        private static void Fizzle(string msg) =>
            InformationManager.DisplayMessage(new InformationMessage(
                msg, Color.FromUint(0xFF997755)));
    }
}
