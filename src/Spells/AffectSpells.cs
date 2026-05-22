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
        // Curse a random enemy village: reduce hearth by 10%.
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
            village.Village.Hearth = Math.Max(10f, before * 0.9f);
            Msg($"Pillager's Brand — the red reaches {village.Name}. Hearth falls from {before:F0} to {village.Village.Hearth:F0}.", ColorSchool.Red);
        }

        // ── Orange — Rallying Call ────────────────────────────────────────
        // Raise party morale by 3 per cast. Reliable, repeatable.
        private static void SpellAffectOrange()
        {
            var party = MobileParty.MainParty;
            if (party == null) return;

            try { party.RecentEventsMorale += 2f; } catch { return; }
            Msg("Rallying Call — your soldiers find new resolve. Morale +2.", ColorSchool.Orange);
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
        // 50% chance to heal one random wounded soldier.
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
            if (_rng.Next(2) == 0)
            {
                Msg($"Mending Touch — the green reaches {element.Character.Name} but cannot fully close the wound.", ColorSchool.Green);
                return;
            }
            try { party.MemberRoster.AddToCounts(element.Character, 0, false, -1); } catch { return; }
            Msg($"Mending Touch — one {element.Character.Name} is mended.", ColorSchool.Green);
        }

        // ── Blue — Philosopher's Stone ────────────────────────────────────
        // Generate gold per cast (scaled by Blue spell power); caster becomes 1 day younger (min age 22).
        private static void SpellAffectBlue()
        {
            if (Hero.MainHero == null) return;

            float power = SpellPower(ColorSchool.Blue);
            int gold = (int)(50f * power);
            try { Hero.MainHero.ChangeHeroGold(gold); } catch { }

            if (Hero.MainHero.Age > 22f)
            {
                Hero.MainHero.SetBirthDay(Hero.MainHero.BirthDay + CampaignTime.Days(1));
                Msg($"Philosopher's Stone — gold flows: +{gold}. Time ebbs. | Age: {(int)Hero.MainHero.Age}", ColorSchool.Blue);
            }
            else
            {
                Msg($"Philosopher's Stone — gold flows: +{gold}. Already at the minimum age.", ColorSchool.Blue);
            }
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
        // Prefers kingdom enemies over bandits/looters when the player is in a kingdom.
        private static void SpellInvokeRed()
        {
            if (Hero.MainHero == null || MobileParty.MainParty == null) return;

            IFaction playerFaction = Hero.MainHero.MapFaction;
            Vec2 playerPos = MobileParty.MainParty.GetPosition2D;

            MobileParty target = null;
            float minDist = float.MaxValue;

            if (Hero.MainHero.Clan?.Kingdom != null)
            {
                foreach (MobileParty p in MobileParty.All)
                {
                    if (p == MobileParty.MainParty || !p.IsActive) continue;
                    if (p.MapFaction == null || p.MapFaction == playerFaction) continue;
                    if (playerFaction != null && !playerFaction.IsAtWarWith(p.MapFaction)) continue;
                    if (!(p.MapFaction is Kingdom)) continue;
                    float d = (p.GetPosition2D - playerPos).Length;
                    if (d < minDist) { minDist = d; target = p; }
                }
            }

            if (target == null)
            {
                foreach (MobileParty p in MobileParty.All)
                {
                    if (p == MobileParty.MainParty || !p.IsActive) continue;
                    if (p.MapFaction == null || p.MapFaction == playerFaction) continue;
                    if (playerFaction != null && !playerFaction.IsAtWarWith(p.MapFaction)) continue;
                    float d = (p.GetPosition2D - playerPos).Length;
                    if (d < minDist) { minDist = d; target = p; }
                }
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
        // Prefers kingdom enemies over bandits/looters when the player is in a kingdom.
        private static void SpellInvokeYellow()
        {
            if (Hero.MainHero == null || MobileParty.MainParty == null) return;

            IFaction playerFaction = Hero.MainHero.MapFaction;
            Vec2 playerPos = MobileParty.MainParty.GetPosition2D;

            MobileParty target = null;
            float minDist = float.MaxValue;

            if (Hero.MainHero.Clan?.Kingdom != null)
            {
                foreach (MobileParty p in MobileParty.All)
                {
                    if (p == MobileParty.MainParty || !p.IsActive) continue;
                    if (p.MapFaction == null || p.MapFaction == playerFaction) continue;
                    if (playerFaction != null && !playerFaction.IsAtWarWith(p.MapFaction)) continue;
                    if (!(p.MapFaction is Kingdom)) continue;
                    float d = (p.GetPosition2D - playerPos).Length;
                    if (d < minDist) { minDist = d; target = p; }
                }
            }

            if (target == null)
            {
                foreach (MobileParty p in MobileParty.All)
                {
                    if (p == MobileParty.MainParty || !p.IsActive) continue;
                    if (p.MapFaction == null || p.MapFaction == playerFaction) continue;
                    if (playerFaction != null && !playerFaction.IsAtWarWith(p.MapFaction)) continue;
                    float d = (p.GetPosition2D - playerPos).Length;
                    if (d < minDist) { minDist = d; target = p; }
                }
            }

            if (target == null) { Msg("Creeping Fear — no enemy party at war found.", ColorSchool.Yellow); return; }

            try { target.RecentEventsMorale -= 2f; } catch { return; }
            Msg($"Creeping Fear — {target.Name} loses 2 morale ({minDist:F1} km).", ColorSchool.Yellow);
        }

        // ── Green — Green's Bounty ────────────────────────────────────────
        // 80% grain, 10% sheep, 10% cow.
        private static void SpellInvokeGreen()
        {
            var party = MobileParty.MainParty;
            if (party == null) return;

            int roll = _rng.Next(10);
            string itemId, itemName;
            if      (roll < 8) { itemId = "grain"; itemName = "1 unit of grain"; }
            else if (roll < 9) { itemId = "sheep"; itemName = "a sheep"; }
            else               { itemId = "cow";   itemName = "a cow"; }

            try
            {
                ItemObject item = Game.Current.ObjectManager.GetObject<ItemObject>(itemId);
                if (item == null)
                {
                    item = Game.Current.ObjectManager.GetObject<ItemObject>("grain");
                    if (item == null) { Msg("Green's Bounty — the green stirs, but nothing takes form.", ColorSchool.Green); return; }
                    itemName = "1 unit of grain";
                }
                party.ItemRoster.AddToCounts(new EquipmentElement(item), 1);
            }
            catch { Msg("Green's Bounty — the green stirs, but nothing takes form.", ColorSchool.Green); return; }
            Msg($"Green's Bounty — {itemName} ripens at your touch.", ColorSchool.Green);
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

        // =================================================================
        // COMMUNE SPELLS (UR prefix) — campaign map only, ambient effects
        // =================================================================

        // ── Red — Crimson Tithe ───────────────────────────────────────────
        // Sacrifice a soldier for skill XP. Party morale −1.
        private static void SpellCommuneRed()
        {
            var party = MobileParty.MainParty;
            if (party == null || Hero.MainHero == null) return;

            var troops = party.MemberRoster.GetTroopRoster()
                .Where(e => !e.Character.IsHero && e.Number > 0).ToList();
            if (troops.Count == 0) { Msg("Crimson Tithe — no soldiers to sacrifice.", ColorSchool.Red); return; }

            var element = troops[_rng.Next(troops.Count)];
            try { party.MemberRoster.AddToCounts(element.Character, -1); } catch { return; }
            try { party.RecentEventsMorale -= 1f; } catch { }

            var skills = new[]
            {
                DefaultSkills.OneHanded, DefaultSkills.TwoHanded, DefaultSkills.Polearm,
                DefaultSkills.Bow, DefaultSkills.Crossbow, DefaultSkills.Throwing,
                DefaultSkills.Riding, DefaultSkills.Athletics, DefaultSkills.Tactics, DefaultSkills.Leadership
            };
            SkillObject chosenSkill = skills[_rng.Next(skills.Length)];
            float power = SpellPower(ColorSchool.Red);
            int xp = (int)(200f * power);
            try { Hero.MainHero.HeroDeveloper.AddSkillXp(chosenSkill, xp); } catch { }
            Msg($"Crimson Tithe — a {element.Character.Name} is spent. {chosenSkill.Name} +{xp} XP. Morale −1.", ColorSchool.Red);
        }

        // ── Orange — Good Word ────────────────────────────────────────────
        // Improve relations with a random lord or notable by +1.
        private static void SpellCommuneOrange()
        {
            if (Hero.MainHero == null) return;

            var candidates = new List<Hero>();
            candidates.AddRange(Hero.AllAliveHeroes
                .Where(h => h.IsLord && h != Hero.MainHero && h.IsAlive && h.Clan != null));
            candidates.AddRange(Hero.AllAliveHeroes
                .Where(h => h.IsNotable && h != Hero.MainHero && h.IsAlive));

            if (candidates.Count == 0) { Msg("Good Word — no one to speak well of you.", ColorSchool.Orange); return; }

            Hero target = candidates[_rng.Next(candidates.Count)];
            try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, target, 1, false); } catch { return; }
            Msg($"Good Word — your warmth reaches {target.Name}. Relations +1.", ColorSchool.Orange);
        }

        // ── Yellow — Sow Doubt ────────────────────────────────────────────
        // Enemy settlement loyalty −10.
        private static void SpellCommuneYellow()
        {
            if (Hero.MainHero == null) return;

            IFaction playerFaction = Hero.MainHero.MapFaction;
            var candidates = Settlement.All
                .Where(s => s.IsTown && s.Town != null
                         && s.MapFaction != null && s.MapFaction != playerFaction)
                .ToList();

            if (candidates.Count == 0) { Msg("Sow Doubt — no enemy towns to unsettle.", ColorSchool.Yellow); return; }

            Settlement target = candidates[_rng.Next(candidates.Count)];
            float before = target.Town.Loyalty;
            try { target.Town.Loyalty = Math.Max(0f, before - 10f); } catch { return; }
            Msg($"Sow Doubt — unease spreads through {target.Name}. Loyalty falls from {before:F0} to {target.Town.Loyalty:F0}.", ColorSchool.Yellow);
        }

        // ── Green — Verdant Bond ──────────────────────────────────────────
        // Friendly village hearth +20.
        private static void SpellCommuneGreen()
        {
            if (Hero.MainHero == null) return;

            IFaction playerFaction = Hero.MainHero.MapFaction;
            var candidates = Settlement.All
                .Where(s => s.IsVillage && s.Village != null
                         && (s.MapFaction == playerFaction || s.OwnerClan == Hero.MainHero.Clan))
                .ToList();

            if (candidates.Count == 0) { Msg("Verdant Bond — no friendly villages to bless.", ColorSchool.Green); return; }

            Settlement target = candidates[_rng.Next(candidates.Count)];
            float before = target.Village.Hearth;
            target.Village.Hearth += 20f;
            Msg($"Verdant Bond — the green breathes into {target.Name}. Hearth {before:F0} → {target.Village.Hearth:F0}.", ColorSchool.Green);
        }

        // ── Blue — Arcane Sight ───────────────────────────────────────────
        // List the 10 nearest colour lords and their distances.
        private static void SpellCommuneBlue()
        {
            if (Hero.MainHero == null || MobileParty.MainParty == null) return;

            Vec2 playerPos = MobileParty.MainParty.GetPosition2D;

            var colourLords = ColourLordRegistry.GetAllColourLords()
                .Where(h => h != Hero.MainHero && h.IsAlive && h.PartyBelongedTo != null)
                .Select(h => new
                {
                    Hero   = h,
                    Dist   = (h.PartyBelongedTo.GetPosition2D - playerPos).Length,
                    Colors = ColourLordRegistry.GetColors(h)
                })
                .OrderBy(x => x.Dist)
                .Take(10)
                .ToList();

            if (colourLords.Count == 0) { Msg("Arcane Sight — no colour lords detected.", ColorSchool.Blue); return; }

            Msg("Arcane Sight — the Scholar's eye opens:", ColorSchool.Blue);
            foreach (var entry in colourLords)
            {
                string colours = string.Join(", ", entry.Colors.Select(c => ColorSchoolData.Info[c].Name));
                Msg($"  {entry.Hero.Name} [{colours}] — {entry.Dist:F1} km", ColorSchool.Blue);
            }
        }

        // ── Purple — Grey Curse ───────────────────────────────────────────
        // A random enemy lord ages 3 days; their clan loses 2 renown.
        private static void SpellCommunePurple()
        {
            if (Hero.MainHero == null) return;

            IFaction playerFaction = Hero.MainHero.MapFaction;
            var enemies = Hero.AllAliveHeroes
                .Where(h => h.IsLord && h.IsAlive && h.Clan != null
                         && h.MapFaction != null && h.MapFaction != playerFaction)
                .ToList();
            if (enemies.Count == 0) { Msg("Grey Curse — no enemy lords to curse.", ColorSchool.Purple); return; }

            Hero target = enemies[_rng.Next(enemies.Count)];
            try { target.SetBirthDay(target.BirthDay - CampaignTime.Days(3)); } catch { }
            try { target.Clan.AddRenown(-2f); } catch { return; }
            Msg($"Grey Curse — {target.Name} ages three days. Clan renown dims. | {target.Name} age: {(int)target.Age}", ColorSchool.Purple);
        }

    }
}
