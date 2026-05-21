// =============================================================================
// COLOURS OF CALRADIA — AffectSpells.cs
// Mount & Blade II: Bannerlord Mod  v1.2.0.0
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
    public static partial class SpellEffects
    {
        // =================================================================
        // AFFECT SPELLS (UL prefix) — campaign map, situational
        // INVOKE SPELLS (LU prefix) — campaign map, advanced
        //
        // Spam limiter: saturation (handled by MagicInputHandler, same as
        // battle spells). No HP, food, or gold costs unless noted.
        // Purple spells apply The Slow Unravelling (+1 day age, −1% fertility).
        // =================================================================

        // ── Red — Pillager's Brand ────────────────────────────────────────
        // Curse a random enemy village: reduce hearth by 20%.
        private static void SpellAffectRed()
        {
            if (Hero.MainHero == null) return;
            IFaction playerFaction = Hero.MainHero.MapFaction;

            var candidates = Settlement.All
                .Where(s => s.IsVillage && s.Village != null
                         && s.MapFaction != null && s.MapFaction != playerFaction
                         && (playerFaction == null || playerFaction.IsAtWarWith(s.MapFaction)))
                .ToList();

            if (candidates.Count == 0)
            {
                Msg("Pillager's Brand — no enemy villages to curse.", ColorSchool.Red);
                return;
            }

            Settlement village = candidates[_rng.Next(candidates.Count)];
            float before = village.Village.Hearth;
            village.Village.Hearth = Math.Max(10f, before * 0.8f);
            Msg($"Pillager's Brand — fire and ruin reach {village.Name}. Hearth falls from {before:F0} to {village.Village.Hearth:F0}.", ColorSchool.Red);
        }

        // ── Orange — Rallying Call ────────────────────────────────────────
        // Raise party morale by 3 per cast. Reliable, repeatable.
        private static void SpellAffectOrange()
        {
            var party = MobileParty.MainParty;
            if (party == null) return;

            try { party.RecentEventsMorale += 3f; } catch { return; }
            Msg("Rallying Call — your soldiers find new resolve. Morale +3.", ColorSchool.Orange);
        }

        // ── Yellow — Press Gang ───────────────────────────────────────────
        // Conscript a random non-hero prisoner from your prison roster.
        private static void SpellAffectYellow()
        {
            var party = MobileParty.MainParty;
            if (party == null) return;

            var prisoners = party.PrisonRoster.GetTroopRoster()
                .Where(e => !e.Character.IsHero && e.Number > 0).ToList();
            if (prisoners.Count == 0)
            {
                Msg("Press Gang — no prisoners to conscript.", ColorSchool.Yellow);
                return;
            }

            var element = prisoners[_rng.Next(prisoners.Count)];
            try
            {
                party.PrisonRoster.AddToCounts(element.Character, -1);
                party.MemberRoster.AddToCounts(element.Character, 1);
                party.RecentEventsMorale -= 2f;
            }
            catch { return; }
            Msg($"Press Gang — a {element.Character.Name} is forced into the ranks. Your soldiers are unsettled. Morale −2.", ColorSchool.Yellow);
        }

        // ── Green — Mending Touch ─────────────────────────────────────────
        // Heal one random wounded soldier to full (1 troop removed from wounded).
        private static void SpellAffectGreen()
        {
            var party = MobileParty.MainParty;
            if (party == null) return;

            var wounded = party.MemberRoster.GetTroopRoster()
                .Where(e => !e.Character.IsHero && e.WoundedNumber > 0).ToList();
            if (wounded.Count == 0)
            {
                Msg("Mending Touch — no wounded soldiers to tend.", ColorSchool.Green);
                return;
            }

            var element = wounded[_rng.Next(wounded.Count)];
            try { party.MemberRoster.AddToCounts(element.Character, 0, false, -1); } catch { return; }
            Msg($"Mending Touch — one {element.Character.Name} is restored to full health.", ColorSchool.Green);
        }

        // ── Blue — Scholar's Blueprint ────────────────────────────────────
        // Requires an active siege. Advances construction of siege engines via
        // field reflection (construction float fields on SiegeEngines / BesiegerCamp).
        private static void SpellAffectBlue()
        {
            if (Hero.MainHero == null || MobileParty.MainParty == null) return;

            Settlement besieged = MobileParty.MainParty.BesiegedSettlement;
            if (besieged == null)
            {
                Msg("Scholar's Blueprint — you must be conducting a siege to accelerate construction.", ColorSchool.Blue);
                return;
            }

            float power = SpellPower(ColorSchool.Blue);
            float bonus = 150f * power;

            bool advanced = false;
            try
            {
                var siege   = besieged.SiegeEvent;
                var camp    = siege?.BesiegerCamp;
                var engines = camp?.SiegeEngines;

                string[] keywords = { "work", "progress", "construct", "stage", "build" };
                const float MaxSane = 50000f;

                foreach (object target in new object[] { engines, camp })
                {
                    if (target == null) continue;
                    foreach (FieldInfo fi in target.GetType().GetFields(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        if (fi.FieldType != typeof(float) && fi.FieldType != typeof(double)) continue;
                        string nm = fi.Name.ToLowerInvariant();
                        bool relevant = false;
                        foreach (string kw in keywords) if (nm.Contains(kw)) { relevant = true; break; }
                        if (!relevant) continue;
                        try
                        {
                            if (fi.FieldType == typeof(float))
                            {
                                float cur = (float)fi.GetValue(target);
                                if (cur < 0f || cur > MaxSane) continue;
                                fi.SetValue(target, cur + bonus);
                            }
                            else
                            {
                                double cur = (double)fi.GetValue(target);
                                if (cur < 0.0 || cur > MaxSane) continue;
                                fi.SetValue(target, cur + bonus);
                            }
                            advanced = true;
                        }
                        catch { }
                    }
                }
            }
            catch { }

            if (!advanced)
            {
                Msg("Scholar's Blueprint — the diagrams find no machines under construction.", ColorSchool.Blue);
                return;
            }

            Msg($"Scholar's Blueprint — construction progress +{(int)bonus}.", ColorSchool.Blue);
        }

        // ── Purple — Grey Veil ────────────────────────────────────────────
        // Scatter nearby enemy parties (15 map-unit radius, 10-unit push).
        // Cost: −1% fertility + 1 day aging.
        private static void SpellAffectPurple()
        {
            if (Hero.MainHero == null || MobileParty.MainParty == null) return;

            Vec2 playerPos = MobileParty.MainParty.GetPosition2D;
            IFaction playerFaction = Hero.MainHero.MapFaction;
            int scattered = 0;
            foreach (MobileParty p in MobileParty.All.ToList())
            {
                if (p == MobileParty.MainParty || !p.IsActive) continue;
                if (p.MapFaction == null) continue;
                if (playerFaction != null && p.MapFaction == playerFaction) continue;
                if (playerFaction != null && !playerFaction.IsAtWarWith(p.MapFaction)) continue;
                if ((p.GetPosition2D - playerPos).Length > 15f) continue;

                Vec2 away = p.GetPosition2D - playerPos;
                if (away.Length < 0.01f) away = new Vec2(1f, 0f); else away = away.Normalized();
                Vec2 dest = p.GetPosition2D + away * 10f;
                try { p.SetMoveGoToPoint(new CampaignVec2(dest, true), MobileParty.NavigationType.Default); scattered++; } catch { }
            }

            string effect = scattered > 0
                ? $"{scattered} nearby {(scattered == 1 ? "enemy loses" : "enemies lose")} your trail."
                : "No enemies close enough to scatter.";
            Msg($"Grey Veil — {effect}", ColorSchool.Purple);
        }

        // =================================================================
        // INVOKE SPELLS (LU prefix) — campaign map, advanced
        // =================================================================

        // ── Red — Withering Strike ────────────────────────────────────────
        // Wound one random non-hero soldier in the nearest enemy party at war.
        private static void SpellInvokeRed()
        {
            if (Hero.MainHero == null || MobileParty.MainParty == null) return;

            IFaction playerFaction = Hero.MainHero.MapFaction;
            Vec2 playerPos = MobileParty.MainParty.GetPosition2D;

            MobileParty target = null;
            float minDist = float.MaxValue;
            foreach (MobileParty p in MobileParty.All)
            {
                if (p == MobileParty.MainParty || !p.IsActive) continue;
                if (p.MapFaction == null || p.MapFaction == playerFaction) continue;
                if (playerFaction != null && !playerFaction.IsAtWarWith(p.MapFaction)) continue;
                float d = (p.GetPosition2D - playerPos).Length;
                if (d < minDist) { minDist = d; target = p; }
            }

            if (target == null) { Msg("Withering Strike — no enemy party at war found.", ColorSchool.Red); return; }

            var troops = target.MemberRoster.GetTroopRoster()
                .Where(e => !e.Character.IsHero && e.Number > e.WoundedNumber).ToList();
            if (troops.Count == 0) { Msg("Withering Strike — no healthy soldiers to wound.", ColorSchool.Red); return; }

            var element = troops[_rng.Next(troops.Count)];
            try { target.MemberRoster.AddToCounts(element.Character, 0, false, 1); } catch { return; }
            Msg($"Withering Strike — one {element.Character.Name} in {target.Name} falls wounded ({minDist:F1} km).", ColorSchool.Red);
        }

        // ── Orange — Inspired Word ────────────────────────────────────────
        // Grant XP to a random soldier in your party. Uses reflection to call
        // TroopRoster.AddXpToTroop — if unavailable, shows a flavour message only.
        private static MethodInfo _addXpToTroopMethod;
        private static bool _addXpResolved;

        private static void SpellInvokeOrange()
        {
            var party = MobileParty.MainParty;
            if (party == null) return;

            var troops = party.MemberRoster.GetTroopRoster()
                .Where(e => !e.Character.IsHero && e.Number > 0).ToList();
            if (troops.Count == 0) { Msg("Inspired Word — no soldiers to inspire.", ColorSchool.Orange); return; }

            float power = SpellPower(ColorSchool.Orange);
            var element = troops[_rng.Next(troops.Count)];
            int xp = (int)(150f * power);

            if (!_addXpResolved)
            {
                _addXpResolved = true;
                _addXpToTroopMethod = typeof(TroopRoster).GetMethod("AddXpToTroop",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }

            bool applied = false;
            if (_addXpToTroopMethod != null)
            {
                try { _addXpToTroopMethod.Invoke(party.MemberRoster, new object[] { xp, element.Character }); applied = true; } catch { }
            }

            Msg(applied
                ? $"Inspired Word — {element.Character.Name} gains {xp} experience."
                : $"Inspired Word — inspiration stirs in {element.Character.Name}.", ColorSchool.Orange);
        }

        // ── Yellow — Creeping Fear ────────────────────────────────────────
        // The nearest enemy party at war loses 3 morale.
        private static void SpellInvokeYellow()
        {
            if (Hero.MainHero == null || MobileParty.MainParty == null) return;

            IFaction playerFaction = Hero.MainHero.MapFaction;
            Vec2 playerPos = MobileParty.MainParty.GetPosition2D;

            MobileParty target = null;
            float minDist = float.MaxValue;
            foreach (MobileParty p in MobileParty.All)
            {
                if (p == MobileParty.MainParty || !p.IsActive) continue;
                if (p.MapFaction == null || p.MapFaction == playerFaction) continue;
                if (playerFaction != null && !playerFaction.IsAtWarWith(p.MapFaction)) continue;
                float d = (p.GetPosition2D - playerPos).Length;
                if (d < minDist) { minDist = d; target = p; }
            }

            if (target == null) { Msg("Creeping Fear — no enemy party at war found.", ColorSchool.Yellow); return; }

            try { target.RecentEventsMorale -= 3f; } catch { return; }
            Msg($"Creeping Fear — {target.Name} loses 3 morale ({minDist:F1} km).", ColorSchool.Yellow);
        }

        // ── Green — Green's Bounty ────────────────────────────────────────
        // Add 1 grain to the party.
        private static void SpellInvokeGreen()
        {
            var party = MobileParty.MainParty;
            if (party == null) return;

            try
            {
                ItemObject grain = Game.Current.ObjectManager.GetObject<ItemObject>("grain");
                if (grain == null) { Msg("Green's Bounty — the green stirs, but nothing takes form.", ColorSchool.Green); return; }
                party.ItemRoster.AddToCounts(new EquipmentElement(grain), 1);
            }
            catch { Msg("Green's Bounty — the green stirs, but nothing takes form.", ColorSchool.Green); return; }
            Msg("Green's Bounty — 1 unit of grain ripens at your touch.", ColorSchool.Green);
        }

        // ── Blue — Scholar's Word ─────────────────────────────────────────
        // Gain 1 influence. Requires kingdom membership.
        private static void SpellInvokeBlue()
        {
            if (Hero.MainHero == null) return;
            if (Hero.MainHero.Clan?.Kingdom == null)
            {
                Msg("Scholar's Word — you must belong to a kingdom for influence to mean anything.", ColorSchool.Blue);
                return;
            }

            try { GainKingdomInfluenceAction.ApplyForDefault(Hero.MainHero, 1); } catch { return; }
            Msg("Scholar's Word — the Scholar's insight earns 1 influence.", ColorSchool.Blue);
        }

        // ── Purple — Wither's Touch ───────────────────────────────────────
        // A random enemy lord loses 2 renown. Cost: −1% fertility + 1 day aging.
        private static void SpellInvokePurple()
        {
            if (Hero.MainHero == null) return;

            IFaction playerFaction = Hero.MainHero.MapFaction;
            var enemies = Hero.AllAliveHeroes
                .Where(h => h.IsLord && h.IsAlive && h.Clan != null
                         && h.MapFaction != null && h.MapFaction != playerFaction)
                .ToList();
            if (enemies.Count == 0) { Msg("Wither's Touch — no enemy lords found.", ColorSchool.Purple); return; }

            Hero target = enemies[_rng.Next(enemies.Count)];
            try { target.Clan.AddRenown(-2f); } catch { return; }

            Msg($"Wither's Touch — {target.Name}'s clan loses 2 renown.", ColorSchool.Purple);
        }

    }
}
