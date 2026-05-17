// =============================================================================
// COLOURS OF CALRADIA — MagicInputHandler.cs
// Mount & Blade II: Bannerlord Mod  v2.0
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Engine;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using TaleWorlds.CampaignSystem.MapEvents;

namespace ColoursOfCalradia
{
    // =========================================================================
    // 7. INPUT HANDLER  — 4-key combo system (focus = Left Alt / LT)
    // =========================================================================
    public static class MagicInputHandler
    {
        private static string _buffer              = "";
        private static bool   _wasFocusing         = false;
        private static string _lastDisplayedBuffer = "";
        private const  int    MaxLen               = 10;

        // Previous-frame state for R-stick directions (IsKeyPressed unreliable for analog axes)
        private static bool _prevRUp, _prevRDown, _prevRLeft, _prevRRight;

        public static bool InputSuppressed { get; private set; }

        public static void Tick(bool inMission)
        {
            if (!ColourKnowledge.HasAnySchool) { InputSuppressed = false; return; }

            // ControllerLTrigger takes priority — Bannerlord maps L-trigger to LeftAlt at the
            // OS level, so both can be true simultaneously when using a controller. Checking
            // the controller key first and excluding it from the keyboard path prevents face
            // buttons (Y→W, X→A, A→S) from bleeding into the spell buffer.
            bool focusingPad = Input.IsKeyDown(InputKey.ControllerLTrigger);
            bool focusingKb  = Input.IsKeyDown(InputKey.LeftAlt) && !focusingPad;
            bool focusing    = focusingKb || focusingPad;

            InputSuppressed = focusing;

            if (focusing)
            {
                _wasFocusing = true;

                if (!inMission && Campaign.Current != null)
                    Campaign.Current.TimeControlMode = CampaignTimeControlMode.UnstoppablePlay;

                // Keyboard path: only when Left Alt is the focus key, so controller face-button
                // virtual keys (Y→W, X→A, A→S) don't bleed into the spell buffer.
                if (focusingKb)
                {
                    if      (Input.IsKeyPressed(InputKey.W)) Append("U");
                    else if (Input.IsKeyPressed(InputKey.A)) Append("L");
                    else if (Input.IsKeyPressed(InputKey.D)) Append("R");
                    else if (Input.IsKeyPressed(InputKey.S))
                    {
                        if (_buffer.Length == 0) ColourKnowledge.ShowGrimoire();
                        else Append("D");
                    }
                }

                // Gamepad: R-stick directions via manual edge detection (IsKeyDown + prev-state)
                // Only runs when L-trigger is the focus key.
                if (focusingPad)
                {
                    bool rUp    = Input.IsKeyDown(InputKey.ControllerRStickUp);
                    bool rDown  = Input.IsKeyDown(InputKey.ControllerRStickDown);
                    bool rLeft  = Input.IsKeyDown(InputKey.ControllerRStickLeft);
                    bool rRight = Input.IsKeyDown(InputKey.ControllerRStickRight);

                    if (rUp    && !_prevRUp)   Append("U");
                    if (rDown  && !_prevRDown) { if (_buffer.Length == 0) ColourKnowledge.ShowGrimoire(); else Append("D"); }
                    if (rLeft  && !_prevRLeft) Append("L");
                    if (rRight && !_prevRRight) Append("R");

                    _prevRUp = rUp; _prevRDown = rDown; _prevRLeft = rLeft; _prevRRight = rRight;

                    if (Input.IsKeyPressed(InputKey.ControllerLThumb)) ColourKnowledge.ShowGrimoire();
                }

                if (_buffer.Length > 0 && _buffer != _lastDisplayedBuffer)
                {
                    _lastDisplayedBuffer = _buffer;
                    InformationManager.DisplayMessage(new InformationMessage(
                        "[ " + _buffer + " ]", new Color(0.7f, 0.7f, 0.7f)));
                }
            }
            else if (_wasFocusing)
            {
                _wasFocusing = false;
                _prevRUp = _prevRDown = _prevRLeft = _prevRRight = false;

                if (!inMission && Campaign.Current != null)
                    Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;

                _lastDisplayedBuffer = "";

                if (_buffer.Length >= 4)
                    TryCast(_buffer, inMission);
                else if (_buffer.Length > 0)
                    Fizzle("Incantation too short — colour magic requires four keys.");

                _buffer = "";
            }
        }

        private static void Append(string dir)
        {
            if (_buffer.Length < MaxLen) _buffer += dir;
        }

        private static void TryCast(string combo, bool inMission)
        {
            SpellEntry spell = SpellDatabase.Find(combo);
            if (spell == null) { Fizzle("The colours do not answer."); return; }

            // School access check
            if (!ColourKnowledge.HasSchool(spell.School))
            {
                Fizzle($"You have not chosen the {ColorSchoolData.Info[spell.School].Name} school.");
                return;
            }

            // Context check
            if (spell.Context == SpellContext.Mission && !inMission)
            { Fizzle($"{spell.Name} can only be cast in battle."); return; }
            if (spell.Context == SpellContext.Map && inMission)
            { Fizzle($"{spell.Name} can only be cast on the campaign map."); return; }

            // ── Pre-cast limitation checks ────────────────────────────────────

            // Global: magic requires light — the mage acts as a prism.
            // School affinity (season/city) can upgrade Dark → Dim for specific schools.
            var lightLevel = SpellEffects.GetEffectiveLightLevel(spell.School);
            if (lightLevel == SpellEffects.LightLevel.Dark)
            {
                Fizzle("The colours require light. Magic cannot be woven in deep night or dark places.");
                return;
            }
            if (lightLevel == SpellEffects.LightLevel.Dim && SpellEffects.RollDimFizzle())
            {
                Fizzle("The fading light weakens the weave. The spell unravels before taking shape.");
                return;
            }


            // Green — Pacifist: no weapon in hand
            if (spell.School == ColorSchool.Green && inMission && Agent.Main != null)
            {
                try
                {
                    var wielded = Agent.Main.WieldedWeapon;
                    bool hasWeapon = !wielded.IsEmpty &&
                        wielded.CurrentUsageItem?.WeaponClass != WeaponClass.Boulder &&
                        wielded.CurrentUsageItem?.IsShield != true;
                    if (hasWeapon)
                    {
                        Fizzle("Pacifist: Sheathe your weapon before casting Green magic.");
                        return;
                    }
                }
                catch { }
            }

            // ── Tournament guard ──────────────────────────────────────────────
            if (inMission && IsInTournament())
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "Sorcery in the tournament — you are disqualified!",
                    Color.FromUint(0xFFFF4444)));
                try { SpellEffects.KillAgent(Agent.Main); } catch { }
                return;
            }

            // ── Cast ─────────────────────────────────────────────────────────
            InformationManager.DisplayMessage(new InformationMessage(
                $"[{ColorSchoolData.Info[spell.School].Name} — {spell.Name}]  {spell.Flavour}",
                ColorSchoolData.GetMessageColor(spell.School)));

            bool success = SpellEffects.Execute(combo);

            // Visual: caster glow + charge animation + sound
            if (inMission && Agent.Main != null)
                SpellEffects.CastGlow(Agent.Main, spell.School);

            if (!success) return;

            // ── Post-cast limitation side effects ─────────────────────────────

            // Red A1 — Furious: issue Charge to own formations
            if (spell.School == ColorSchool.Red && inMission && Agent.Main != null)
                SpellEffects.IssueChargeToOwnFormations(Agent.Main);

            // Red A2 — Blood Price: small self-damage
            if (spell.School == ColorSchool.Red && inMission && Agent.Main != null)
            {
                try { Agent.Main.Health = Math.Max(1f, Agent.Main.Health - 8f); }
                catch { }
            }

            // Yellow — Suspicious: party morale loss + criminal rating increase
            if (spell.School == ColorSchool.Yellow)
            {
                try { if (MobileParty.MainParty != null) MobileParty.MainParty.RecentEventsMorale -= 8f; }
                catch { }
                // Increase criminal rating in the kingdom where the spell is used
                try
                {
                    Kingdom crimKingdom = null;
                    if (inMission && Mission.Current != null)
                    {
                        foreach (Agent a in Mission.Current.Agents)
                        {
                            if (a.Character is CharacterObject co && co.IsHero && co.HeroObject != null
                                && a.Team != Mission.Current.PlayerTeam)
                            {
                                crimKingdom = co.HeroObject.Clan?.Kingdom;
                                if (crimKingdom != null) break;
                            }
                        }
                    }
                    crimKingdom = crimKingdom ?? Hero.MainHero?.Clan?.Kingdom;
                    if (crimKingdom != null)
                        ChangeCrimeRatingAction.Apply(crimKingdom, 3f, true);
                }
                catch { }
            }


            // Orange — Generous Hunger: stagger the caster in random directions
            if (spell.School == ColorSchool.Orange && inMission)
                SpellEffects.TriggerConfusion();

            // Blue — Scholar's Weight: equipment grows heavier each cast, limiting speed
            if (spell.School == ColorSchool.Blue && inMission)
            {
                SpellEffects.ApplyBlueWeight();
                // Grounded: 50 % chance to be thrown when casting from horseback
                try
                {
                    if (Agent.Main?.MountAgent != null && new Random().Next(2) == 0)
                    {
                        Agent.Main.Formation?.SetRidingOrder(RidingOrder.RidingOrderDismount);
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Grounded: The Scholar's Weight unsettles your mount — you are thrown from the saddle.",
                            ColorSchoolData.GetMessageColor(ColorSchool.Blue)));
                    }
                }
                catch { }
            }

            // Purple — Waning Cost: ages the caster ~2 days; also quietly reduces fertility
            if (spell.School == ColorSchool.Purple)
            {
                try
                {
                    Hero.MainHero?.SetBirthDay(Hero.MainHero.BirthDay - CampaignTime.Years(1f / 365f));
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"Waning Cost: The grey takes its years. | Age: {(int)(Hero.MainHero?.Age ?? 0)}",
                        ColorSchoolData.GetMessageColor(ColorSchool.Purple)));
                }
                catch { }
                if (ColourKnowledge.ReducePurpleFertility())
                {
                    int pct = (int)(ColourKnowledge.PurpleFertilityLevel * 100f);
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"The Slow Unravelling: Something within grows quieter. Fertility: {pct}%",
                        ColorSchoolData.GetMessageColor(ColorSchool.Purple)));
                }
            }

            // Personality drift
            ColourKnowledge.RecordCast(spell.School);
        }

        private static bool IsInTournament()
        {
            if (Mission.Current == null) return false;
            foreach (MissionBehavior b in Mission.Current.MissionBehaviors)
                if (b.GetType().Name.Contains("Tournament")) return true;
            return false;
        }

        private static void Fizzle(string msg) =>
            InformationManager.DisplayMessage(new InformationMessage(
                msg, Color.FromUint(0xFF996644)));
    }
}
