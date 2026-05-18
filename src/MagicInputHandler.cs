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
        private static readonly Random _rng = new Random();

        // Previous-frame state for L-stick directions (IsKeyPressed unreliable for analog axes)
        private static bool _prevLUp, _prevLDown, _prevLLeft, _prevLRight;

        // Form prefixes that can be released early to trigger a random known spell of that form
        private static readonly HashSet<string> _formPrefixes =
            new HashSet<string> { "UU", "RL", "LR", "UL", "LU" };

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
                        if (_buffer.Length == 0) ColourKnowledge.ShowGrimoire(inMission, false);
                        else Append("D");
                    }
                }

                // Gamepad: L-stick directions via manual edge detection (IsKeyDown + prev-state)
                // Only runs when L-trigger is the focus key.
                if (focusingPad)
                {
                    bool lUp    = Input.IsKeyDown(InputKey.ControllerLStickUp);
                    bool lDown  = Input.IsKeyDown(InputKey.ControllerLStickDown);
                    bool lLeft  = Input.IsKeyDown(InputKey.ControllerLStickLeft);
                    bool lRight = Input.IsKeyDown(InputKey.ControllerLStickRight);

                    if (lUp    && !_prevLUp)   Append("U");
                    if (lDown  && !_prevLDown) { if (_buffer.Length == 0) ColourKnowledge.ShowGrimoire(inMission, true); else Append("D"); }
                    if (lLeft  && !_prevLLeft) Append("L");
                    if (lRight && !_prevLRight) Append("R");

                    _prevLUp = lUp; _prevLDown = lDown; _prevLLeft = lLeft; _prevLRight = lRight;

                    if (Input.IsKeyPressed(InputKey.ControllerLThumb)) ColourKnowledge.ShowGrimoire(inMission, true);
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
                _prevLUp = _prevLDown = _prevLLeft = _prevLRight = false;

                if (!inMission && Campaign.Current != null)
                    Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;

                _lastDisplayedBuffer = "";

                if (_buffer.Length >= 4)
                    TryCast(_buffer, inMission);
                else if (_buffer.Length == 2 && _formPrefixes.Contains(_buffer))
                    TryRandomFormCast(_buffer, inMission);
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


            // Green — Pacifist: no weapon in hand; Horse-Shy: no casting from horseback
            if (spell.School == ColorSchool.Green && inMission && Agent.Main != null)
            {
                try
                {
                    if (Agent.Main.MountAgent != null)
                    {
                        Fizzle("Horse-Shy: Green magic will not flow through a mount's distress — dismount first.");
                        return;
                    }
                }
                catch { }
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

            // ── Madness redirect ─────────────────────────────────────────────
            // Chance to misfire as a random different-colour spell of the same form.
            // Uses the same threshold as battle-order scrambling.
            int madnessChance = ColourKnowledge.GetMadnessOrderChance();
            if (madnessChance > 0 && _rng.Next(100) < madnessChance)
            {
                string prefix = combo.Substring(0, 2);
                var alts = SpellDatabase.All
                    .Where(s => s.Combo.StartsWith(prefix)
                             && s.School != spell.School
                             && ColourKnowledge.HasSchool(s.School)
                             && s.Context == spell.Context)
                    .ToList();
                if (alts.Count > 0)
                {
                    SpellEntry redirect = alts[_rng.Next(alts.Count)];
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"Madness twists the weave — {spell.Name} unravels into {redirect.Name}!",
                        Color.FromUint(0xFFAA44FF)));
                    combo = redirect.Combo;
                    spell = redirect;
                }
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


            // Orange — Generous Flood: stagger + 33% horseback throw + ammo drain
            if (spell.School == ColorSchool.Orange && inMission)
            {
                SpellEffects.TriggerConfusion();
                try
                {
                    if (Agent.Main?.MountAgent != null && _rng.Next(3) == 0)
                    {
                        Agent.Main.Formation?.SetRidingOrder(RidingOrder.RidingOrderDismount);
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Generous Flood: The warmth startles your mount — you are thrown from the saddle.",
                            ColorSchoolData.GetMessageColor(ColorSchool.Orange)));
                    }
                }
                catch { }
                for (var ammoSlot = EquipmentIndex.Weapon0; ammoSlot <= EquipmentIndex.ExtraWeaponSlot; ammoSlot++)
                {
                    try
                    {
                        MissionWeapon w = Agent.Main.Equipment[ammoSlot];
                        if (w.IsEmpty || w.Amount <= 0) continue;
                        Agent.Main.SetWeaponAmountInSlot(ammoSlot, (short)Math.Max(0, w.Amount - 2), false);
                    }
                    catch { }
                }
            }

            // Blue — Scholar's Weight: battle only; Timeless Toll aging applies everywhere
            if (spell.School == ColorSchool.Blue && inMission)
                SpellEffects.ApplyBlueWeight();

            if (spell.School == ColorSchool.Blue)
            {
                try
                {
                    if (Hero.MainHero != null)
                    {
                        Hero.MainHero.SetBirthDay(Hero.MainHero.BirthDay - CampaignTime.Days(2));
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"Timeless Toll: The Scholar's pursuit costs years. | Age: {(int)Hero.MainHero.Age}",
                            ColorSchoolData.GetMessageColor(ColorSchool.Blue)));
                    }
                }
                catch { }
            }

            // Purple — Hollow Standing: each cast costs renown and influence
            if (spell.School == ColorSchool.Purple)
            {
                try
                {
                    Hero.MainHero?.Clan?.AddRenown(-5f);
                    InformationManager.DisplayMessage(new InformationMessage(
                        "Hollow Standing: The grey bleeds your presence. Renown −5.",
                        ColorSchoolData.GetMessageColor(ColorSchool.Purple)));
                }
                catch { }
                try
                {
                    if (Hero.MainHero?.Clan?.Kingdom != null)
                    {
                        GainKingdomInfluenceAction.ApplyForDefault(Hero.MainHero, -2f);
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Hollow Standing: Influence −2.",
                            ColorSchoolData.GetMessageColor(ColorSchool.Purple)));
                    }
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

        // Picks a random known spell whose combo starts with `prefix` and matches context,
        // then casts it via TryCast (which applies all normal validation + madness checks).
        private static void TryRandomFormCast(string prefix, bool inMission)
        {
            var context = inMission ? SpellContext.Mission : SpellContext.Map;
            var candidates = SpellDatabase.All
                .Where(s => s.Combo.StartsWith(prefix)
                         && ColourKnowledge.HasSchool(s.School)
                         && s.Context == context)
                .ToList();

            if (candidates.Count == 0)
            {
                Fizzle($"No known {FormName(prefix)} spells for this context.");
                return;
            }

            SpellEntry chosen = candidates[_rng.Next(candidates.Count)];
            InformationManager.DisplayMessage(new InformationMessage(
                $"[ {prefix} → {chosen.Combo} ]",
                new Color(0.7f, 0.7f, 0.7f)));
            TryCast(chosen.Combo, inMission);
        }

        private static string FormName(string prefix)
        {
            switch (prefix)
            {
                case "UU": return "Blast";
                case "RL": return "Self";
                case "LR": return "Create";
                case "UL": return "Affect";
                case "LU": return "Invoke";
                default:   return prefix;
            }
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
