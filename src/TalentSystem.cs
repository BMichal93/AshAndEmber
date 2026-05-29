// =============================================================================
// LIFE & DEATH MAGIC — TalentSystem.cs
// Talent definitions, purchase logic, lore text, and save/load.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace AshAndEmber
{
    public enum TalentId
    {
        Gift        = 0,   // Passive — starting talent
        Subjugate   = 1,   // Spell
        Rejuvenate  = 2,   // Spell
        PlantGrowth = 3,   // Spell
        BreakWills  = 4,   // Spell
        Inspire     = 5,   // Spell
        Plague      = 6,   // Spell
        Clairvoyance= 7,   // Spell
        Curse       = 8,   // Spell
        DevourLife  = 9,   // Passive
        BattleMage  = 10,  // Passive
        Sorcerer    = 11,  // Passive
        Camaraderie = 12,  // Passive
        Reap        = 13,  // Passive
        Ember       = 14,  // Passive
        // ── Damage enchantments ───────────────────────────────────────────────
        Scatter     = 15,  // Enchantment — Damage: push back
        Smoulder    = 16,  // Enchantment — Damage: morale penalty
        Bewilder    = 17,  // Enchantment — Damage: random command
        // ── Restore enchantments ─────────────────────────────────────────────
        Ashveil     = 18,  // Enchantment — Restore: magic immunity
        CinderShell = 19,  // Enchantment — Restore: armour boost
        Hearthlight = 20,  // Enchantment — Restore: morale boost
    }

    public enum TalentCategory { Passive, Enchantment, Spell }

    public class TalentDef
    {
        public TalentId      Id;
        public string        Name;
        public bool          IsSpell;        // true = campaign map spell
        public bool          IsEnchantment;  // true = battle enchantment
        public TalentCategory Category;
        public string        Lore;
        public string        MechanicDesc;
    }

    public static class TalentSystem
    {
        private static readonly Random _rng = new Random();

        public static readonly IReadOnlyList<TalentDef> All = new List<TalentDef>
        {
            // ── Passive ──────────────────────────────────────────────────────
            new TalentDef
            {
                Id = TalentId.Gift, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.Passive, Name = "Gift",
                Lore = "The fire ran in your blood before you understood what fire was. Not warmth — something older. The kind that burns without consuming, and holds the world together at its edges.",
                MechanicDesc = "You carry the inner fire. In battle: form keys, Break, effect keys. U = Damage (enemies). D = Restore (allies)."
            },
            new TalentDef
            {
                Id = TalentId.BattleMage, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.Passive, Name = "Tempered",
                Lore = "The forge teaches patience. A slow hand draws more from less; a careful reach into the fire takes without burning.",
                MechanicDesc = "Passive. Each battle cast costs 1 fewer day (minimum 0)."
            },
            new TalentDef
            {
                Id = TalentId.Sorcerer, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.Passive, Name = "Resonance",
                Lore = "Some days the fire gives back what it takes. You cannot predict it — only listen for it.",
                MechanicDesc = "Passive. One in four campaign map castings costs no days."
            },
            new TalentDef
            {
                Id = TalentId.Ember, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.Passive, Name = "Ember",
                Lore = "In the moment of killing, when fire passes from one vessel to another, some scatters. Sometimes a spark finds you. You have learned, not to seek it, but to cup your hands.",
                MechanicDesc = "Passive. Each kill on the battlefield has a 5% chance to restore 1 day of youth."
            },
            new TalentDef
            {
                Id = TalentId.DevourLife, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.Passive, Name = "Harvest",
                Lore = "You take the last warmth from a life you have ended. The fire knows no guilt — it only spreads.",
                MechanicDesc = "Passive. Executing a captured lord draws back 100 days of youth."
            },
            new TalentDef
            {
                Id = TalentId.Reap, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.Passive, Name = "Reap",
                Lore = "Every life spent in your shadow leaves something behind — a warmth, a residue, the last gasp of a flame that burned for your purpose. You have learned to hold a vessel for it.",
                MechanicDesc = "Passive. Raiding a village draws back 5 days of youth (7-day cooldown). Each prisoner discarded has a 5% chance to draw back 1 day. Learning this marks you."
            },
            new TalentDef
            {
                Id = TalentId.Camaraderie, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.Passive, Name = "Kinship",
                Lore = "Those who carry the fire recognise each other from across a room. There is something almost like trust in that. Almost.",
                MechanicDesc = "Passive. +10 relations with those who carry the fire. Never falls below −10."
            },
            // ── Enchantments (Damage) ─────────────────────────────────────────
            new TalentDef
            {
                Id = TalentId.Scatter, IsSpell = false, IsEnchantment = true,
                Category = TalentCategory.Enchantment, Name = "Scatter",
                Lore = "The fire does not merely burn — it expels. What it touches, it unmakes and flings aside. You have learned to aim that expulsion.",
                MechanicDesc = "Enchantment. Damage also blasts enemies backward. Push distance = 4m per Damage input."
            },
            new TalentDef
            {
                Id = TalentId.Smoulder, IsSpell = false, IsEnchantment = true,
                Category = TalentCategory.Enchantment, Name = "Smoulder",
                Lore = "The fire knows what frightens. It does not need to kill a man to defeat him — only to let him feel how little warmth he carries. The courage drains out with the heat.",
                MechanicDesc = "Enchantment. Damage scorches enemy morale. Morale loss = 12 per Damage input."
            },
            new TalentDef
            {
                Id = TalentId.Bewilder, IsSpell = false, IsEnchantment = true,
                Category = TalentCategory.Enchantment, Name = "Bewilder",
                Lore = "The fire is not just heat — it is signal. When you push it through a mind unprepared, the signals cross. Halt, charge, flee, stand. They will not know which they were told.",
                MechanicDesc = "Enchantment. Damage bewilders enemies with a random command — halt, charge, dismount, or go into melee."
            },
            // ── Enchantments (Restore) ────────────────────────────────────────
            new TalentDef
            {
                Id = TalentId.Ashveil, IsSpell = false, IsEnchantment = true,
                Category = TalentCategory.Enchantment, Name = "Ashveil",
                Lore = "Ash does not burn twice. Coat something in it, and the fire cannot find purchase. For a few seconds, what you kindle becomes untouchable.",
                MechanicDesc = "Enchantment. Restore grants allies brief magic immunity. Duration = 2s per Restore input."
            },
            new TalentDef
            {
                Id = TalentId.CinderShell, IsSpell = false, IsEnchantment = true,
                Category = TalentCategory.Enchantment, Name = "Cinder Shell",
                Lore = "Fire hardens what it doesn't consume. The skin does not become stone — it becomes something older. Whatever falls on them will not find the same flesh.",
                MechanicDesc = "Enchantment. Restore hardens allies, reducing incoming damage for 8 seconds. Protection = 5% per Restore input, max 50%."
            },
            new TalentDef
            {
                Id = TalentId.Hearthlight, IsSpell = false, IsEnchantment = true,
                Category = TalentCategory.Enchantment, Name = "Hearthlight",
                Lore = "The fire in them has not gone out — it has only dimmed. You reach in and remind it what it is for. They remember, for a moment, that the fire is their friend.",
                MechanicDesc = "Enchantment. Restore lifts allied morale. Morale boost = 12 per Restore input."
            },
            // ── Campaign map spells ──────────────────────────────────────────
            new TalentDef
            {
                Id = TalentId.Subjugate, IsSpell = true, IsEnchantment = false,
                Category = TalentCategory.Spell, Name = "Subjugate",
                Lore = "The fire bends toward those who fear losing it most. You press yours against a captive's fear, and they choose service over what you are silently offering instead.",
                MechanicDesc = "All prisoners of your largest captive group yield and join your ranks. Costs 1 day."
            },
            new TalentDef
            {
                Id = TalentId.Rejuvenate, IsSpell = true, IsEnchantment = false,
                Category = TalentCategory.Spell, Name = "Rejuvenate",
                Lore = "You press a sliver of your own fire into a wound, just enough to wake theirs. They will not know what was given. You will feel it, briefly.",
                MechanicDesc = "Up to 8 wounded soldiers of each type across all your ranks recover. Costs 1 day."
            },
            new TalentDef
            {
                Id = TalentId.Inspire, IsSpell = true, IsEnchantment = false,
                Category = TalentCategory.Spell, Name = "Kindle",
                Lore = "You let them feel it briefly — the warmth that says the world cares whether they live. It may be a lie. The fire does not ask.",
                MechanicDesc = "Your party gains 40 morale and 5 wounded soldiers recover. Costs 1 day."
            },
            new TalentDef
            {
                Id = TalentId.PlantGrowth, IsSpell = true, IsEnchantment = false,
                Category = TalentCategory.Spell, Name = "Quicken",
                Lore = "Seeds carry fire in them — a very old, very patient kind. You ask that patience to end.",
                MechanicDesc = "Grain grows in proportion to your party's need — one measure per soldier. Costs 1 day."
            },
            new TalentDef
            {
                Id = TalentId.BreakWills, IsSpell = true, IsEnchantment = false,
                Category = TalentCategory.Spell, Name = "Unsettle",
                Lore = "You let them feel how thin their fire is. Most men have never faced that knowledge directly. Courage is easier when you cannot see the dark.",
                MechanicDesc = "The nearest enemy party within 100m loses 35 morale. Costs 1 day."
            },
            new TalentDef
            {
                Id = TalentId.Plague, IsSpell = true, IsEnchantment = false,
                Category = TalentCategory.Spell, Name = "Wither",
                Lore = "Fire leaves places slowly, or quickly, depending on who tends it. You remove the tender.",
                MechanicDesc = "The nearest enemy village loses a fifth of its hearth. Costs 1 day."
            },
            new TalentDef
            {
                Id = TalentId.Clairvoyance, IsSpell = true, IsEnchantment = false,
                Category = TalentCategory.Spell, Name = "Clairvoyance",
                Lore = "The lines of fire connect every living thing to every other. You read them the way a navigator reads stars — imperfectly, but well enough.",
                MechanicDesc = "Gain 40 influence. Without a kingdom, the insight becomes gold instead. Costs 1 day."
            },
            new TalentDef
            {
                Id = TalentId.Curse, IsSpell = true, IsEnchantment = false,
                Category = TalentCategory.Spell, Name = "Curse",
                Lore = "A thread of flame, pulled. The body follows where the fire leads — and you are leading it toward ash.",
                MechanicDesc = "5–12 soldiers in the nearest enemy party are wounded or killed, and their courage breaks. Costs 1 day."
            },
        };

        // ── Player talent tracking ─────────────────────────────────────────────
        private static readonly HashSet<TalentId> _purchased = new HashSet<TalentId>();

        public static bool Has(TalentId id) => _purchased.Contains(id);
        public static IEnumerable<TalentId> AllPurchased => _purchased;
        public static int PurchasedCount => _purchased.Count;

        public static void ResetForNewGame()
        {
            _purchased.Clear();
            _purchased.Add(TalentId.Gift);
        }

        public static void UnlockAll()
        {
            foreach (TalentId id in Enum.GetValues(typeof(TalentId)))
                _purchased.Add(id);
            InformationManager.DisplayMessage(new InformationMessage(
                "All talents unlocked.",
                new Color(1f, 0.8f, 0.2f)));
        }

        // Cost curve: 1 pt for the first 3 talents after Gift, 2 pts after that. Max 2.
        public static int PurchaseCost() => _purchased.Count <= 3 ? 1 : 2;

        public static bool TryPurchase(TalentId id, Hero hero)
        {
            if (_purchased.Contains(id)) return false;
            if (hero == null) return false;

            int cost = PurchaseCost();

            bool spent = false;
            try
            {
                if (hero.HeroDeveloper.UnspentFocusPoints >= cost)
                {
                    hero.HeroDeveloper.UnspentFocusPoints -= cost;
                    spent = true;
                }
            }
            catch { }

            if (!spent)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"Not enough focus points. Cost: {cost} point{(cost != 1 ? "s" : "")}.",
                    new Color(0.8f, 0.5f, 0.2f)));
                return false;
            }

            _purchased.Add(id);

            if (id == TalentId.Camaraderie) ApplyCamaraderie(hero);
            if (id == TalentId.Reap)        ApplyReapTraits(hero);

            var def = GetDef(id);
            InformationManager.DisplayMessage(new InformationMessage(
                $"You have learned {def.Name}. {def.MechanicDesc}",
                new Color(0.7f, 0.9f, 0.7f)));
            return true;
        }

        private static void ApplyReapTraits(Hero hero)
        {
            try
            {
                int mercy = hero.GetTraitLevel(DefaultTraits.Mercy);
                if (mercy > -3) hero.SetTraitLevel(DefaultTraits.Mercy, mercy - 1);
                int honor = hero.GetTraitLevel(DefaultTraits.Honor);
                if (honor > -3) hero.SetTraitLevel(DefaultTraits.Honor, honor - 1);
                if (hero.MapFaction is Kingdom k)
                    try { ChangeCrimeRatingAction.Apply(k, 30f, true); } catch { }
                InformationManager.DisplayMessage(new InformationMessage(
                    "The fire darkens with hunger. Those who witness what you do will remember.",
                    new Color(0.8f, 0.4f, 0.2f)));
            }
            catch { }
        }

        private static void ApplyCamaraderie(Hero player)
        {
            try
            {
                foreach (Hero h in Hero.AllAliveHeroes.ToList())
                {
                    if (h == player || !ColourLordRegistry.IsColourLord(h)) continue;
                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(player, h, 10, false); } catch { }
                }
            }
            catch { }
        }

        public static void EnforceCaramaraderieLimits(Hero player, Hero mage)
        {
            if (!Has(TalentId.Camaraderie)) return;
            try
            {
                int rel = CharacterRelationManager.GetHeroRelation(player, mage);
                if (rel < -10) CharacterRelationManager.SetHeroRelation(player, mage, -10);
            }
            catch { }
        }

        public static TalentDef GetDef(TalentId id) =>
            All.FirstOrDefault(d => d.Id == id) ?? All[0];

        // ── Campaign map spell execution ──────────────────────────────────────
        public static void ExecuteMapSpell(TalentId id)
        {
            if (!Has(id)) return;
            var def = GetDef(id);
            if (!def.IsSpell) return;

            if (MageKnowledge.IsAshen)
            {
                try
                {
                    if (Hero.MainHero?.MapFaction is Kingdom ashenK)
                    {
                        ChangeCrimeRatingAction.Apply(ashenK, GetBlightCrimeCost(id), false);
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The ash spreads.", new Color(0.3f, 0.35f, 0.7f)));
                    }
                }
                catch { }
            }
            else
            {
                bool skipAging = Has(TalentId.Sorcerer) && _rng.Next(4) == 0;
                if (skipAging)
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The fire gives back.", new Color(0.9f, 0.6f, 0.3f)));
                else
                    AgingSystem.AgeHero(Hero.MainHero, 1);
            }

            switch (id)
            {
                case TalentId.Subjugate:    CastSubjugate();    break;
                case TalentId.Rejuvenate:   CastRejuvenate();   break;
                case TalentId.PlantGrowth:  CastPlantGrowth();  break;
                case TalentId.BreakWills:   CastBreakWills();   break;
                case TalentId.Inspire:      CastInspire();      break;
                case TalentId.Plague:       CastPlague();       break;
                case TalentId.Clairvoyance: CastClairvoyance(); break;
                case TalentId.Curse:        CastCurse();        break;
            }
        }

        private static void CastSubjugate()
        {
            try
            {
                var prisoners = MobileParty.MainParty?.PrisonRoster?.GetTroopRoster()
                    .Where(e => !e.Character.IsHero && e.Number > 0).ToList();
                if (prisoners == null || prisoners.Count == 0)
                { Msg("Subjugate — no prisoners to subjugate."); return; }
                var entry = prisoners.OrderByDescending(e => e.Number).First();
                int count = entry.Number;
                MobileParty.MainParty.PrisonRoster.AddToCounts(entry.Character, -count);
                MobileParty.MainParty.MemberRoster.AddToCounts(entry.Character,  count);
                Msg($"Subjugate — {count} {entry.Character.Name}{(count != 1 ? "s" : "")} bend the knee.");
            }
            catch { Msg("Subjugate — no suitable prisoners found."); }
        }

        private static void CastRejuvenate()
        {
            var roster = MobileParty.MainParty?.MemberRoster;
            if (roster == null) { Msg("Rejuvenate — no party found."); return; }
            var wounded = roster.GetTroopRoster()
                .Where(e => !e.Character.IsHero && e.WoundedNumber > 0).ToList();
            if (wounded.Count == 0) { Msg("Rejuvenate — no wounded soldiers."); return; }
            int totalHealed = 0;
            foreach (var entry in wounded)
            {
                try
                {
                    int healed = Math.Min(entry.WoundedNumber, 8);
                    roster.AddToCounts(entry.Character, 0, false, -healed);
                    totalHealed += healed;
                }
                catch { }
            }
            if (totalHealed > 0)
                Msg($"Rejuvenate — {totalHealed} soldier{(totalHealed != 1 ? "s" : "")} rise from their wounds.");
            else
                Msg("Rejuvenate — the wounds hold.");
        }

        private static void CastPlantGrowth()
        {
            try
            {
                int partySize = MobileParty.MainParty?.MemberRoster?.TotalManCount ?? 50;
                int grain = Math.Max(50, Math.Min(partySize, 200));
                MobileParty.MainParty?.ItemRoster?.AddToCounts(
                    TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObject<TaleWorlds.Core.ItemObject>("grain"), grain);
                Msg($"Quicken — the soil answers. {grain} grain added.");
            }
            catch { Msg("Quicken — the fields are generous."); }
        }

        private static void CastBreakWills()
        {
            try
            {
                if (MobileParty.MainParty == null) return;
                Vec2 playerPos = MobileParty.MainParty.GetPosition2D;
                var target = MobileParty.All
                    .Where(p => p.IsActive && FactionManager.IsAtWarAgainstFaction(p.MapFaction, MobileParty.MainParty.MapFaction)
                             && p.LeaderHero != null
                             && (p.GetPosition2D - playerPos).Length < 100f)
                    .OrderBy(p => (p.GetPosition2D - playerPos).Length)
                    .FirstOrDefault();
                if (target == null) { Msg("Unsettle — no enemy party in range."); return; }
                target.RecentEventsMorale -= 35f;
                Msg($"Unsettle — dread settles over {target.Name}. -35 morale.");
            }
            catch { }
        }

        private static void CastInspire()
        {
            try
            {
                if (MobileParty.MainParty == null) return;
                MobileParty.MainParty.RecentEventsMorale += 40f;
                var roster = MobileParty.MainParty.MemberRoster;
                var wounded = roster.GetTroopRoster()
                    .Where(e => !e.Character.IsHero && e.WoundedNumber > 0).ToList();
                int roused = 0;
                foreach (var entry in wounded)
                {
                    if (roused >= 5) break;
                    int heal = Math.Min(entry.WoundedNumber, 5 - roused);
                    roster.AddToCounts(entry.Character, 0, false, -heal);
                    roused += heal;
                }
                string msg = roused > 0
                    ? $"Kindle — warmth floods your ranks. +40 morale, {roused} soldier{(roused != 1 ? "s" : "")} roused."
                    : "Kindle — warmth floods your ranks. +40 morale.";
                Msg(msg);
            }
            catch { }
        }

        private static void CastPlague()
        {
            try
            {
                if (MobileParty.MainParty == null) return;
                Vec2 playerPos = MobileParty.MainParty.GetPosition2D;
                var playerFaction = MobileParty.MainParty.MapFaction;
                var target = Settlement.All
                    .Where(s => s.IsVillage && s.Village != null && s.MapFaction != null
                             && s.MapFaction != playerFaction
                             && FactionManager.IsAtWarAgainstFaction(s.MapFaction, playerFaction))
                    .OrderBy(s => (s.GetPosition2D - playerPos).Length)
                    .FirstOrDefault();
                if (target == null) { Msg("Wither — no enemy villages found."); return; }
                float before = target.Village.Hearth;
                target.Village.Hearth = Math.Max(10f, before * 0.80f);
                Msg($"Wither — something old settles over {target.Name}. Hearth reduced by 20%.");
            }
            catch { }
        }

        private static void CastClairvoyance()
        {
            try
            {
                if (Hero.MainHero?.Clan?.Kingdom != null)
                {
                    Hero.MainHero.Clan.Influence += 40f;
                    Msg("Clairvoyance — the threads of power revealed. +40 influence.");
                }
                else
                {
                    Hero.MainHero.ChangeHeroGold(1000);
                    Msg("Clairvoyance — no throne to bend, but the fire finds other currents. +1000 gold.");
                }
            }
            catch { Msg("Clairvoyance — insight granted."); }
        }

        private static void CastCurse()
        {
            try
            {
                if (MobileParty.MainParty == null) return;
                Vec2 playerPos = MobileParty.MainParty.GetPosition2D;
                var target = MobileParty.All
                    .Where(p => p.IsActive && FactionManager.IsAtWarAgainstFaction(p.MapFaction, MobileParty.MainParty.MapFaction)
                             && p.MemberRoster.TotalRegulars > 0
                             && (p.GetPosition2D - playerPos).Length < 60f)
                    .OrderBy(p => (p.GetPosition2D - playerPos).Length)
                    .FirstOrDefault();
                if (target == null) { Msg("Curse — no enemy party in range."); return; }
                int count = 5 + _rng.Next(8);
                int actual = 0;
                var troops = target.MemberRoster.GetTroopRoster()
                    .Where(e => !e.Character.IsHero && e.Number > e.WoundedNumber).ToList();
                for (int i = 0; i < count && troops.Count > 0; i++)
                {
                    int idx = _rng.Next(troops.Count);
                    int wound = _rng.Next(2) == 0 ? 1 : 0;
                    try { target.MemberRoster.AddToCounts(troops[idx].Character, wound == 1 ? 0 : -1, false, wound); actual++; } catch { }
                }
                target.RecentEventsMorale -= 25f;
                Msg($"Curse — {actual} soul{(actual != 1 ? "s" : "")} marked in {target.Name}. Their courage breaks. -25 morale.");
            }
            catch { }
        }

        private static float GetBlightCrimeCost(TalentId id)
        {
            switch (id)
            {
                case TalentId.Curse:
                case TalentId.Clairvoyance: return 15f;
                case TalentId.BreakWills:
                case TalentId.Plague:       return 10f;
                default:                    return 5f;
            }
        }

        // ── NPC campaign map spell execution ─────────────────────────────────
        public static void ExecuteNpcMapSpell(Hero caster, TalentId id)
        {
            if (caster == null) return;
            string blurb = null;
            try
            {
                switch (id)
                {
                    case TalentId.BreakWills: NpcBreakWills(caster); blurb = "casts Unsettle — dread spreads through an enemy party."; break;
                    case TalentId.Inspire:    NpcInspire(caster);    blurb = "kindles their warband — morale rises."; break;
                    case TalentId.Plague:     NpcPlague(caster);     blurb = "works a Wither — a village's hearth fades."; break;
                    case TalentId.Curse:      NpcCurse(caster);      blurb = "casts Curse — soldiers fall in a distant party."; break;
                    case TalentId.Rejuvenate: NpcRejuvenate(caster); blurb = "draws on Rejuvenate — their wounded recover."; break;
                    default: break;
                }
            }
            catch { }

            if (blurb != null)
            {
                bool isAshen = ColourLordRegistry.IsAshenLord(caster);
                Color c = isAshen ? new Color(0.35f, 0.4f, 0.65f) : new Color(0.65f, 0.45f, 0.8f);
                InformationManager.DisplayMessage(new InformationMessage($"{caster.Name} — {blurb}", c));
            }

            if (ColourLordRegistry.IsAshenLord(caster)) ApplyBlightDrain(caster);
            else AgingSystem.AgeHero(caster, 1);
        }

        private static void ApplyBlightDrain(Hero caster)
        {
            try
            {
                var party = caster.PartyBelongedTo;
                if (_rng.Next(2) == 0 && party != null)
                {
                    var troops = party.MemberRoster.GetTroopRoster()
                        .Where(e => !e.Character.IsHero && e.Number > e.WoundedNumber).ToList();
                    if (troops.Count > 0)
                    {
                        var entry = troops[_rng.Next(troops.Count)];
                        try { party.MemberRoster.AddToCounts(entry.Character, 0, false, 1); } catch { }
                        return;
                    }
                }
                Vec2 pos = party?.GetPosition2D ?? Vec2.Zero;
                var village = Settlement.All
                    .Where(s => s.IsVillage && s.Village != null)
                    .OrderBy(s => (s.GetPosition2D - pos).Length)
                    .FirstOrDefault();
                if (village != null)
                    village.Village.Hearth = Math.Max(10f, village.Village.Hearth * 0.97f);
            }
            catch { }
        }

        private static void NpcBreakWills(Hero caster)
        {
            Vec2 pos = caster.PartyBelongedTo?.GetPosition2D ?? Vec2.Zero;
            var target = MobileParty.All
                .Where(p => p.IsActive && FactionManager.IsAtWarAgainstFaction(p.MapFaction, caster.PartyBelongedTo?.MapFaction)
                         && (p.GetPosition2D - pos).Length < 50f)
                .OrderBy(p => (p.GetPosition2D - pos).Length).FirstOrDefault();
            if (target == null) return;
            target.RecentEventsMorale -= 20f;
            InformationManager.DisplayMessage(new InformationMessage(
                $"{caster.Name} casts Unsettle — dread grips {target.Name}.", new Color(0.5f, 0.3f, 0.7f)));
        }

        private static void NpcInspire(Hero caster)
        {
            var party = caster.PartyBelongedTo;
            if (party == null) return;
            party.RecentEventsMorale += 20f;
            InformationManager.DisplayMessage(new InformationMessage(
                $"{caster.Name} casts Kindle — {party.Name} surges with new purpose.", new Color(0.9f, 0.6f, 0.2f)));
        }

        private static void NpcPlague(Hero caster)
        {
            var villages = Settlement.All
                .Where(s => s.IsVillage && s.Village != null && s.MapFaction != caster.MapFaction).ToList();
            if (villages.Count == 0) return;
            var v = villages[_rng.Next(villages.Count)];
            v.Village.Hearth = Math.Max(10f, v.Village.Hearth * 0.80f);
            InformationManager.DisplayMessage(new InformationMessage(
                $"{caster.Name} casts Wither — blight spreads through {v.Name}.", new Color(0.4f, 0.6f, 0.3f)));
        }

        private static void NpcCurse(Hero caster)
        {
            Vec2 pos = caster.PartyBelongedTo?.GetPosition2D ?? Vec2.Zero;
            var target = MobileParty.All
                .Where(p => p.IsActive && FactionManager.IsAtWarAgainstFaction(p.MapFaction, caster.PartyBelongedTo?.MapFaction)
                         && p.MemberRoster.TotalRegulars > 2
                         && (p.GetPosition2D - pos).Length < 60f)
                .OrderBy(p => (p.GetPosition2D - pos).Length).FirstOrDefault();
            if (target == null) return;
            var troops = target.MemberRoster.GetTroopRoster()
                .Where(e => !e.Character.IsHero && e.Number > e.WoundedNumber).ToList();
            if (troops.Count == 0) return;
            try { target.MemberRoster.AddToCounts(troops[_rng.Next(troops.Count)].Character, 0, false, 1); } catch { }
            InformationManager.DisplayMessage(new InformationMessage(
                $"{caster.Name} casts Curse — a soldier in {target.Name} falls.", new Color(0.7f, 0.2f, 0.2f)));
        }

        private static void NpcRejuvenate(Hero caster)
        {
            var roster = caster.PartyBelongedTo?.MemberRoster;
            if (roster == null) return;
            var wounded = roster.GetTroopRoster()
                .Where(e => !e.Character.IsHero && e.WoundedNumber > 0).ToList();
            if (wounded.Count == 0) return;
            var entry = wounded[_rng.Next(wounded.Count)];
            try { roster.AddToCounts(entry.Character, 0, false, -Math.Min(entry.WoundedNumber, 2)); } catch { }
            InformationManager.DisplayMessage(new InformationMessage(
                $"{caster.Name} casts Rejuvenate — wounded soldiers stir in {caster.PartyBelongedTo?.Name}.",
                new Color(0.3f, 0.7f, 0.5f)));
        }

        private static void Msg(string text) =>
            InformationManager.DisplayMessage(new InformationMessage(text, new Color(0.7f, 0.9f, 0.7f)));

        // ── Save / Load ────────────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            var list = _purchased.Select(t => (int)t).ToList();
            store.SyncData("LDM_Talents", ref list);
            _purchased.Clear();
            if (list != null)
                foreach (int v in list) _purchased.Add((TalentId)v);
        }
    }

    internal static class MobilePartyExtensions
    {
        public static void Let(this MobileParty p, Action<MobileParty> action)
        {
            if (p != null) action(p);
        }
    }
}
