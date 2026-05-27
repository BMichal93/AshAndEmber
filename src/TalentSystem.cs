// =============================================================================
// LIFE & DEATH MAGIC — TalentSystem.cs
// Talent definitions, purchase logic, lore text, and save/load.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ColoursOfCalradia
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
    }

    public class TalentDef
    {
        public TalentId  Id;
        public string    Name;
        public bool      IsSpell;   // false = passive
        public string    Lore;
        public string    MechanicDesc;
    }

    public static class TalentSystem
    {
        private static readonly Random _rng = new Random();

        public static readonly IReadOnlyList<TalentDef> All = new List<TalentDef>
        {
            new TalentDef
            {
                Id = TalentId.Gift, IsSpell = false, Name = "Gift",
                Lore = "You were born with a thread of something older than language running through your blood — a current that hums against your bones when the dying are near. Most never feel it. You do.",
                MechanicDesc = "You carry the gift. In battle, shape the current: form keys, Break, effect keys."
            },
            new TalentDef
            {
                Id = TalentId.Subjugate, IsSpell = true, Name = "Subjugate",
                Lore = "The will bends before the life it most fears losing. You reach into a captive's dread and offer them a worse fate than service — and they choose service.",
                MechanicDesc = "A prisoner yields to your will and joins your ranks. Costs 1 day."
            },
            new TalentDef
            {
                Id = TalentId.Rejuvenate, IsSpell = true, Name = "Rejuvenate",
                Lore = "You take the spare warmth of the living world and press it into a wound, pushing the body toward what it still remembers: wholeness.",
                MechanicDesc = "Several wounded soldiers in your army recover. Costs 1 day."
            },
            new TalentDef
            {
                Id = TalentId.PlantGrowth, IsSpell = true, Name = "Quicken",
                Lore = "The soil is never empty — it is merely waiting. You ask it to stop waiting.",
                MechanicDesc = "20 grain grows where you ask it. Costs 1 day."
            },
            new TalentDef
            {
                Id = TalentId.BreakWills, IsSpell = true, Name = "Unsettle",
                Lore = "Courage is a fragile thing, built on the belief that survival is still possible. You whisper to the part of a man that has always known it is not.",
                MechanicDesc = "The nearest enemy party loses 20 morale. Costs 1 day."
            },
            new TalentDef
            {
                Id = TalentId.Inspire, IsSpell = true, Name = "Kindle",
                Lore = "You let them feel it briefly — the warmth that says the world cares whether they live. It is a lie, but a merciful one, and it holds long enough to matter.",
                MechanicDesc = "Your party gains 20 morale. Costs 1 day."
            },
            new TalentDef
            {
                Id = TalentId.Plague, IsSpell = true, Name = "Wither",
                Lore = "Life is not a gift. It is a density — and you know exactly how to thin it. You breathe something old and patient into the fields of your enemies.",
                MechanicDesc = "An enemy village loses a fifth of its hearth. Costs 1 day."
            },
            new TalentDef
            {
                Id = TalentId.Clairvoyance, IsSpell = true, Name = "Clairvoyance",
                Lore = "You press your awareness outward along the threads of life that connect every breathing thing. What others call intelligence, you simply read from the world itself.",
                MechanicDesc = "Gain 20 influence. Costs 1 day."
            },
            new TalentDef
            {
                Id = TalentId.Curse, IsSpell = true, Name = "Curse",
                Lore = "You mark a thread and pull. The body follows where the life energy leads — and you are leading it toward ruin.",
                MechanicDesc = "2–4 soldiers in the nearest enemy party are wounded or killed. Costs 1 day."
            },
            new TalentDef
            {
                Id = TalentId.DevourLife, IsSpell = false, Name = "Harvest",
                Lore = "Death is not waste — it is density returned to the flow. When you take a life deliberately, with your hands and your intent, something comes back to you.",
                MechanicDesc = "Passive. Executing a captured lord draws back 150 days of youth."
            },
            new TalentDef
            {
                Id = TalentId.BattleMage, IsSpell = false, Name = "Tempered",
                Lore = "You have learned to draw more from less — to find the same depth of working with a lighter gesture, a shorter reach into the current.",
                MechanicDesc = "Passive. The aging threshold rises from 4 inputs to 5."
            },
            new TalentDef
            {
                Id = TalentId.Sorcerer, IsSpell = false, Name = "Resonance",
                Lore = "Sometimes the current gives back what it takes. You have learned to listen for the moment when the flow is generous.",
                MechanicDesc = "Passive. One in four castings costs no days."
            },
            new TalentDef
            {
                Id = TalentId.Camaraderie, IsSpell = false, Name = "Kinship",
                Lore = "You know what it is to carry the weight of the gift alone. So do they. That understanding moves faster than politics.",
                MechanicDesc = "Passive. +10 relations with those who carry the gift. Never falls below −10."
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

        // Cost = number of talents already learned (Gift is free, every subsequent costs 1 point)
        public static int PurchaseCost() => Math.Max(1, _purchased.Count);

        public static bool TryPurchase(TalentId id, Hero hero)
        {
            if (_purchased.Contains(id)) return false;
            if (hero == null) return false;

            int cost = PurchaseCost();

            // Spend Focus points if available, otherwise spend an attribute point
            bool spent = false;
            try
            {
                if (hero.HeroDeveloper.UnspentFocusPoints >= cost)
                {
                    hero.HeroDeveloper.UnspentFocusPoints -= cost;
                    spent = true;
                }
                else
                {
                    // Try to spend an attribute point from any attribute that has > 1
                    var attrs = new[]
                    {
                        TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultCharacterAttributes.Vigor,
                        TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultCharacterAttributes.Control,
                        TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultCharacterAttributes.Endurance,
                        TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultCharacterAttributes.Intelligence,
                        TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultCharacterAttributes.Cunning,
                        TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultCharacterAttributes.Social,
                    };
                    foreach (var attr in attrs)
                    {
                        if (hero.GetAttributeValue(attr) > 1)
                        {
                            hero.HeroDeveloper.AddAttribute(attr, -1, false);
                            spent = true;
                            break;
                        }
                    }
                }
            }
            catch { }

            if (!spent)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "Not enough Focus points or attribute points to learn this talent.",
                    new Color(0.8f, 0.5f, 0.2f)));
                return false;
            }

            _purchased.Add(id);

            // Passive: Camaraderie — instant relation bonus with mage lords
            if (id == TalentId.Camaraderie)
                ApplyCamaraderie(hero);

            var def = GetDef(id);
            InformationManager.DisplayMessage(new InformationMessage(
                $"You have learned {def.Name}. {def.MechanicDesc}",
                new Color(0.7f, 0.9f, 0.7f)));
            return true;
        }

        private static void ApplyCamaraderie(Hero player)
        {
            try
            {
                foreach (Hero h in Hero.AllAliveHeroes.ToList())
                {
                    if (h == player || !ColourLordRegistry.IsColourLord(h)) continue;
                    try
                    {
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(player, h, 10, false);
                    }
                    catch { }
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
                if (rel < -10)
                    CharacterRelationManager.SetHeroRelation(player, mage, -10);
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

            // Sorcerer: 25% chance to skip aging; without Sorcerer always age
            bool skipAging = Has(TalentId.Sorcerer) && _rng.Next(4) == 0;
            if (skipAging)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "The current gives back.",
                    new Color(0.7f, 0.9f, 0.7f)));
            }
            else
            {
                AgingSystem.AgeHero(Hero.MainHero, 1);
            }

            switch (id)
            {
                case TalentId.Subjugate:   CastSubjugate();   break;
                case TalentId.Rejuvenate:  CastRejuvenate();  break;
                case TalentId.PlantGrowth: CastPlantGrowth(); break;
                case TalentId.BreakWills:  CastBreakWills();  break;
                case TalentId.Inspire:     CastInspire();     break;
                case TalentId.Plague:      CastPlague();      break;
                case TalentId.Clairvoyance:CastClairvoyance();break;
                case TalentId.Curse:       CastCurse();       break;
            }
        }

        private static void CastSubjugate()
        {
            try
            {
                var prisoners = MobileParty.MainParty?.PrisonRoster?.GetTroopRoster()
                    .Where(e => !e.Character.IsHero && e.Number > 0)
                    .ToList();
                if (prisoners == null || prisoners.Count == 0)
                { Msg("No prisoners to subjugate."); return; }

                var entry = prisoners[_rng.Next(prisoners.Count)];
                MobileParty.MainParty.PrisonRoster.AddToCounts(entry.Character, -1);
                MobileParty.MainParty.MemberRoster.AddToCounts(entry.Character,  1);
                Msg($"Subjugate — {entry.Character.Name} bends the knee and joins your ranks.");
            }
            catch { Msg("Subjugate — no suitable prisoners found."); }
        }

        private static void CastRejuvenate()
        {
            try
            {
                var roster = MobileParty.MainParty?.MemberRoster;
                if (roster == null) return;
                var wounded = roster.GetTroopRoster()
                    .Where(e => !e.Character.IsHero && e.WoundedNumber > 0)
                    .ToList();
                if (wounded.Count == 0) { Msg("Rejuvenate — no wounded soldiers in your ranks."); return; }

                var entry = wounded[_rng.Next(wounded.Count)];
                int healed = Math.Max(1, Math.Min(entry.WoundedNumber, 3));
                roster.AddToCounts(entry.Character, 0, false, -healed);
                Msg($"Rejuvenate — {healed} {entry.Character.Name} rise from their wounds.");
            }
            catch { }
        }

        private static void CastPlantGrowth()
        {
            try
            {
                MobileParty.MainParty?.ItemRoster?.AddToCounts(
                    TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObject<TaleWorlds.Core.ItemObject>("grain"), 20);
                Msg("Plant Growth — the soil answers. 20 grain added.");
            }
            catch { Msg("Plant Growth — the fields are generous. 20 grain added."); }
        }

        private static void CastBreakWills()
        {
            try
            {
                Vec2 playerPos = MobileParty.MainParty.GetPosition2D;
                var target = MobileParty.All
                    .Where(p => p.IsActive && p.IsEnemy(MobileParty.MainParty) && p.LeaderHero != null
                             && (p.GetPosition2D - playerPos).Length < 60f)
                    .OrderBy(p => (p.GetPosition2D - playerPos).Length)
                    .FirstOrDefault();
                if (target == null) { Msg("Break Wills — no enemy party in range."); return; }
                target.RecentEventsMorale -= 20f;
                Msg($"Break Wills — despair seeps into {target.Name}. -20 morale.");
            }
            catch { }
        }

        private static void CastInspire()
        {
            try
            {
                if (MobileParty.MainParty != null)
                    MobileParty.MainParty.RecentEventsMorale += 20f;
                Msg("Inspire — warmth floods your ranks. +20 morale.");
            }
            catch { }
        }

        private static void CastPlague()
        {
            try
            {
                var enemies = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && h.MapFaction != null
                             && h.MapFaction != Hero.MainHero?.MapFaction)
                    .ToList();
                if (enemies.Count == 0) { Msg("Plague — no enemy villages found."); return; }

                var enemy = enemies[_rng.Next(enemies.Count)];
                var villages = Settlement.All.Where(s => s.IsVillage
                    && s.MapFaction == enemy.MapFaction && s.Village != null).ToList();
                if (villages.Count == 0) { Msg("Plague — no enemy villages found."); return; }

                var village = villages[_rng.Next(villages.Count)];
                float before = village.Village.Hearth;
                village.Village.Hearth = Math.Max(10f, before * 0.80f);
                Msg($"Plague — something old settles over {village.Name}. Hearth reduced by 20%.");
            }
            catch { }
        }

        private static void CastClairvoyance()
        {
            try
            {
                if (Hero.MainHero?.Clan?.Kingdom != null)
                {
                    GainKingdomInfluenceAction.ApplyForDefault(Hero.MainHero, 20);
                    Msg("Clairvoyance — the threads of power revealed. +20 influence.");
                }
                else
                    Msg("Clairvoyance — influence gained (kingdom required for full benefit).");
            }
            catch { Msg("Clairvoyance — insight granted."); }
        }

        private static void CastCurse()
        {
            try
            {
                Vec2 playerPos = MobileParty.MainParty.GetPosition2D;
                var target = MobileParty.All
                    .Where(p => p.IsActive && p.IsEnemy(MobileParty.MainParty)
                             && p.MemberRoster.TotalRegulars > 0
                             && (p.GetPosition2D - playerPos).Length < 60f)
                    .OrderBy(p => (p.GetPosition2D - playerPos).Length)
                    .FirstOrDefault();
                if (target == null) { Msg("Curse — no enemy party in range."); return; }

                int count = 2 + _rng.Next(3); // 2–4
                int actual = 0;
                var troops = target.MemberRoster.GetTroopRoster()
                    .Where(e => !e.Character.IsHero && e.Number > e.WoundedNumber)
                    .ToList();
                for (int i = 0; i < count && troops.Count > 0; i++)
                {
                    int idx = _rng.Next(troops.Count);
                    int wound = _rng.Next(2) == 0 ? 1 : 0; // 0=kill, 1=wound
                    try
                    {
                        target.MemberRoster.AddToCounts(troops[idx].Character, wound == 1 ? 0 : -1, false, wound);
                        actual++;
                    }
                    catch { }
                }
                Msg($"Curse — {actual} soul{(actual != 1 ? "s" : "")} marked in {target.Name}.");
            }
            catch { }
        }

        // ── NPC campaign map spell execution ─────────────────────────────────
        public static void ExecuteNpcMapSpell(Hero caster, TalentId id)
        {
            if (caster == null) return;
            try
            {
                switch (id)
                {
                    case TalentId.BreakWills: NpcBreakWills(caster); break;
                    case TalentId.Inspire:    NpcInspire(caster);    break;
                    case TalentId.Plague:     NpcPlague(caster);     break;
                    case TalentId.Curse:      NpcCurse(caster);      break;
                    case TalentId.Rejuvenate: NpcRejuvenate(caster); break;
                    default: break;
                }
            }
            catch { }
            AgingSystem.AgeHero(caster, 1);
        }

        private static void NpcBreakWills(Hero caster)
        {
            Vec2 pos = caster.PartyBelongedTo?.GetPosition2D ?? Vec2.Zero;
            var target = MobileParty.All
                .Where(p => p.IsActive && p.IsEnemy(caster.PartyBelongedTo)
                         && (p.GetPosition2D - pos).Length < 50f)
                .OrderBy(p => (p.GetPosition2D - pos).Length)
                .FirstOrDefault();
            if (target == null) return;
            target.RecentEventsMorale -= 20f;
        }

        private static void NpcInspire(Hero caster)
        {
            caster.PartyBelongedTo?.Let(p => p.RecentEventsMorale += 20f);
        }

        private static void NpcPlague(Hero caster)
        {
            var villages = Settlement.All
                .Where(s => s.IsVillage && s.Village != null
                         && s.MapFaction != caster.MapFaction)
                .ToList();
            if (villages.Count == 0) return;
            var v = villages[_rng.Next(villages.Count)];
            v.Village.Hearth = Math.Max(10f, v.Village.Hearth * 0.80f);
        }

        private static void NpcCurse(Hero caster)
        {
            Vec2 pos = caster.PartyBelongedTo?.GetPosition2D ?? Vec2.Zero;
            var target = MobileParty.All
                .Where(p => p.IsActive && p.IsEnemy(caster.PartyBelongedTo)
                         && p.MemberRoster.TotalRegulars > 2
                         && (p.GetPosition2D - pos).Length < 60f)
                .OrderBy(p => (p.GetPosition2D - pos).Length)
                .FirstOrDefault();
            if (target == null) return;
            var troops = target.MemberRoster.GetTroopRoster()
                .Where(e => !e.Character.IsHero && e.Number > e.WoundedNumber).ToList();
            if (troops.Count == 0) return;
            try { target.MemberRoster.AddToCounts(troops[_rng.Next(troops.Count)].Character, 0, false, 1); } catch { }
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
        }

        private static void Msg(string text) =>
            InformationManager.DisplayMessage(new InformationMessage(
                text, new Color(0.7f, 0.9f, 0.7f)));

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

    // Extension helper so .Let() works on nullable party references
    internal static class MobilePartyExtensions
    {
        public static void Let(this MobileParty p, Action<MobileParty> action)
        {
            if (p != null) action(p);
        }
    }
}
