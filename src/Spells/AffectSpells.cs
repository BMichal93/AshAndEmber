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

        // ── Red — Burning Winds ───────────────────────────────────────────
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
                Msg("Burning Winds — no enemy villages to reach.", ColorSchool.Red);
                return;
            }

            Settlement village = candidates[_rng.Next(candidates.Count)];
            float before = village.Village.Hearth;
            village.Village.Hearth = Math.Max(10f, before * 0.9f);
            Msg($"Burning Winds — the red rides the wind to {village.Name}. Hearth falls from {before:F0} to {village.Village.Hearth:F0}.", ColorSchool.Red);
        }

        // ── Orange — Rallying Call ────────────────────────────────────────
        // Raise party morale by 5 per cast.
        private static void SpellAffectOrange()
        {
            var party = MobileParty.MainParty;
            if (party == null) return;

            try { party.RecentEventsMorale += 5f; } catch { return; }
            Msg("Rallying Call — warmth moves through the ranks. They stand taller. Morale +5.", ColorSchool.Orange);
        }

        // ── Yellow — Chains of Fear ───────────────────────────────────────
        // Conscript a random non-hero prisoner from your prison roster. Morale -1.
        private static void SpellAffectYellow()
        {
            var party = MobileParty.MainParty;
            if (party == null) return;

            var prisoners = party.PrisonRoster.GetTroopRoster()
                .Where(e => !e.Character.IsHero && e.Number > 0).ToList();
            if (prisoners.Count == 0)
            {
                Msg("Chains of Fear — no prisoners to conscript.", ColorSchool.Yellow);
                return;
            }

            var element = prisoners[_rng.Next(prisoners.Count)];
            try
            {
                party.PrisonRoster.AddToCounts(element.Character, -1);
                party.MemberRoster.AddToCounts(element.Character, 1);
                party.RecentEventsMorale -= 1f;
            }
            catch { return; }
            Msg($"Chains of Fear — a {element.Character.Name} is coerced into the ranks. Your soldiers watch in silence. Morale −1.", ColorSchool.Yellow);
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

        // ── Purple — Pale Dirge ───────────────────────────────────────────
        // The nearest enemy party loses 5 soldiers and 20 morale.
        // Cost: −1% fertility + 1 day aging (handled by MagicInputHandler).
        private static void SpellAffectPurple()
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

            if (target == null) { Msg("Pale Dirge — no enemy party at war found.", ColorSchool.Purple); return; }

            const int SoldiersLost = 5;
            const float MoraleLost = 20f;
            int removed = 0;
            var troops = target.MemberRoster.GetTroopRoster()
                .Where(e => !e.Character.IsHero && e.Number > 0).ToList();
            for (int i = 0; i < SoldiersLost && troops.Count > 0; i++)
            {
                var elem = troops[_rng.Next(troops.Count)];
                try { target.MemberRoster.AddToCounts(elem.Character, -1); removed++; } catch { }
                if (elem.Number <= 1) troops.Remove(elem);
            }
            try { target.RecentEventsMorale -= MoraleLost; } catch { }
            Msg($"Pale Dirge — {removed} soldiers fade from {target.Name}. Morale −{MoraleLost:F0}. ({minDist:F1} km)", ColorSchool.Purple);
        }

        // =================================================================
        // INVOKE SPELLS (LU prefix) — campaign map, advanced
        // =================================================================

        // ── Red — Red Lightning ───────────────────────────────────────────
        // Strike 2–4 soldiers in the nearest enemy party at war.
        // Each has a 30% chance to die outright; otherwise wounded.
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

            if (target == null) { Msg("Red Lightning — no enemy party at war found.", ColorSchool.Red); return; }

            var troops = target.MemberRoster.GetTroopRoster()
                .Where(e => !e.Character.IsHero && e.Number > e.WoundedNumber).ToList();
            if (troops.Count == 0) { Msg("Red Lightning — no healthy soldiers to strike.", ColorSchool.Red); return; }

            int affected = 2 + _rng.Next(3); // 2–4
            int killed = 0, wounded = 0;
            for (int i = 0; i < affected; i++)
            {
                if (troops.Count == 0) break;
                var element = troops[_rng.Next(troops.Count)];
                try
                {
                    if (_rng.Next(100) < 30)
                    {
                        target.MemberRoster.AddToCounts(element.Character, -1);
                        killed++;
                    }
                    else
                    {
                        target.MemberRoster.AddToCounts(element.Character, 0, false, 1);
                        wounded++;
                    }
                }
                catch { }
            }

            var parts = new System.Collections.Generic.List<string>();
            if (killed  > 0) parts.Add($"{killed} killed");
            if (wounded > 0) parts.Add($"{wounded} wounded");
            Msg($"Red Lightning — {string.Join(", ", parts)} in {target.Name} ({minDist:F1} km).", ColorSchool.Red);
        }

        // ── Orange — Guidance ────────────────────────────────────────────
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
                ? $"Guidance — {element.Character.Name} gains {xp} experience."
                : $"Guidance — something shifts in {element.Character.Name}.", ColorSchool.Orange);
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

            float moraleLoss = 15f + _rng.Next(16); // 15–30
            try { target.RecentEventsMorale -= moraleLoss; } catch { return; }
            Msg($"Creeping Fear — {target.Name} loses {moraleLoss:F0} morale ({minDist:F1} km).", ColorSchool.Yellow);
        }

        // ── Green — Animal Friendship ─────────────────────────────────────
        // A random living thing finds its way to you: grain(50%), pig(15%), sheep(12%),
        // horse(8%), mule(8%), cow(7%).
        private static void SpellInvokeGreen()
        {
            var party = MobileParty.MainParty;
            if (party == null) return;

            // Weighted item table: [id, display name, cumulative threshold]
            var table = new[] {
                ("grain",       "1 unit of grain",  50),
                ("pig",         "a pig",            65),
                ("sheep",       "a sheep",          77),
                ("sumpter_horse","a sumpter horse", 85),
                ("mule",        "a mule",           93),
                ("cow",         "a cow",            100),
            };

            int roll = _rng.Next(100);
            string itemId = "grain", itemName = "1 unit of grain";
            foreach (var (id, name, threshold) in table)
            {
                if (roll < threshold) { itemId = id; itemName = name; break; }
            }

            try
            {
                ItemObject item = Game.Current.ObjectManager.GetObject<ItemObject>(itemId);
                if (item == null)
                {
                    item = Game.Current.ObjectManager.GetObject<ItemObject>("grain");
                    if (item == null) { Msg("Animal Friendship — the green stirs, but nothing takes form.", ColorSchool.Green); return; }
                    itemName = "1 unit of grain";
                }
                party.ItemRoster.AddToCounts(new EquipmentElement(item), 1);
            }
            catch { Msg("Animal Friendship — the green stirs, but nothing takes form.", ColorSchool.Green); return; }
            Msg($"Animal Friendship — {itemName} finds its way to you.", ColorSchool.Green);
        }

        // ── Blue — Blue Influence ─────────────────────────────────────────
        // Gain 4 influence. Requires kingdom membership.
        private static void SpellInvokeBlue()
        {
            if (Hero.MainHero == null) return;
            if (Hero.MainHero.Clan?.Kingdom == null)
            {
                Msg("Blue Influence — you must belong to a kingdom for influence to mean anything.", ColorSchool.Blue);
                return;
            }

            try { GainKingdomInfluenceAction.ApplyForDefault(Hero.MainHero, 5); } catch { return; }
            Msg("Blue Influence — the Scholar's insight earns 5 influence.", ColorSchool.Blue);
        }

        // ── Purple — Purple Isolation ─────────────────────────────────────
        // A random enemy lord loses 8 renown. Cost: −1% fertility + 1 day aging.
        private static void SpellInvokePurple()
        {
            if (Hero.MainHero == null) return;

            IFaction playerFaction = Hero.MainHero.MapFaction;
            var enemies = Hero.AllAliveHeroes
                .Where(h => h.IsLord && h.IsAlive && h.Clan != null
                         && h.MapFaction != null && h.MapFaction != playerFaction)
                .ToList();
            if (enemies.Count == 0) { Msg("Purple Isolation — no enemy lords found.", ColorSchool.Purple); return; }

            Hero target = enemies[_rng.Next(enemies.Count)];
            try { target.Clan.AddRenown(-8f); } catch { return; }

            Msg($"Purple Isolation — something significant leaves {target.Name}. Clan renown −8.", ColorSchool.Purple);
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
            try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, target, 2, false); } catch { return; }
            Msg($"Good Word — your warmth reaches {target.Name}. Relations +2.", ColorSchool.Orange);
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
        // List the 10 nearest colour lords and their distances in a popup dialog.
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

            if (colourLords.Count == 0)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Arcane Sight",
                    "The Scholar's eye opens across Calradia — and finds no colour lords near you.",
                    true, false, "Close", "", () => { }, () => { }
                ), true, true);
                return;
            }

            var lines = colourLords.Select(e =>
            {
                string colours = string.Join(", ", e.Colors.Select(c => ColorSchoolData.Info[c].Name));
                return $"  {e.Hero.Name}  [{colours}]  —  {e.Dist:F1} km";
            });

            InformationManager.ShowInquiry(new InquiryData(
                "Arcane Sight — Colour Lords Nearby",
                string.Join("\n", lines),
                true, false, "Close", "", () => { }, () => { }
            ), true, true);
        }

        // ── Purple — The Waning ───────────────────────────────────────────
        // A random enemy lord ages 7 days; their clan loses 3 renown.
        private static void SpellCommunePurple()
        {
            if (Hero.MainHero == null) return;

            IFaction playerFaction = Hero.MainHero.MapFaction;
            var enemies = Hero.AllAliveHeroes
                .Where(h => h.IsLord && h.IsAlive && h.Clan != null
                         && h.MapFaction != null && h.MapFaction != playerFaction)
                .ToList();
            if (enemies.Count == 0) { Msg("The Waning — no enemy lords to reach.", ColorSchool.Purple); return; }

            Hero target = enemies[_rng.Next(enemies.Count)];
            try { target.SetBirthDay(target.BirthDay - CampaignTime.Days(7)); } catch { }
            try { target.Clan.AddRenown(-3f); } catch { return; }
            Msg($"The Waning — seven days taken from {target.Name}. Clan renown falls. | {target.Name} age: {(int)target.Age}", ColorSchool.Purple);
        }

    }
}
