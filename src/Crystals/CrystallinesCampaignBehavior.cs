// =============================================================================
// ASH AND EMBER — Crystals/CrystallinesCampaignBehavior.cs
//
// Campaign layer for the crystal system:
//   • Crystalline Chambers in 5 towns (2 Sturgian, 2 Battanian, 1 Northern Empire)
//     — rare places where crystals grow in deep mines and mountain passes infused
//     with fire magic. Formation menu (Silver Ore + trade good), open to any visitor.
//   • EstablishForNewCampaign — faction-weighted seed across lords at game start
//     (8% Sturgian, 8% Battanian, 6% Northern Empire, 5% others)
//   • OnHeroCreated — faction-weighted chance for new lords' equipment
//   • Weekly/session shop restock so chamber towns always carry every crystal
//   • SyncData — nothing to persist (crystals are real items in inventories)
//
// Formation menu flow:
//   Town "Visit the Crystalline Chamber"
//     → crystal_main (which crystal to form)
//       → crystal_form_<type> (select, check materials, roll, add item or refund)
//       → crystal_leave
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public partial class CrystallinesCampaignBehavior : CampaignBehaviorBase
    {
        private static readonly Random _rng = new Random();

        // Towns that host a Crystalline Chamber.
        // Five locations in deep mines, mountain passes, and quarries where fire magic
        // runs strong — rare places where crystals naturally grow. Sturgians control two,
        // Battania two, and the Northern Empire one.
        private static readonly string[] ChamberTowns =
        {
            // Sturgian
            "Revyl", "Varcheg",
            // Battania
            "Dunglanys", "Car Banseth",
            // Northern Empire
            "Saneopa",
        };

        // ── CampaignBehaviorBase ─────────────────────────────────────────────

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.HeroCreated.AddNonSerializedListener(this, OnHeroCreated);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
        }

        // Crystals themselves are real items in the world — but the lapidary's
        // learned craft (CrystalTalents) is persisted here.
        public override void SyncData(IDataStore store)
        {
            try { CrystalTalents.Save(store); } catch { }
        }

        // One-time seeding of the lords that already exist at campaign start. The
        // HeroCreated hook only fires for heroes spawned/born AFTER the campaign
        // begins, so without this pass no crystal would be carried by any NPC until
        // a new generation grew up. Uses faction-weighted chances.
        public static void EstablishForNewCampaign()
        {
            try
            {
                var lords = Hero.AllAliveHeroes
                    .Where(h => h != null && h != Hero.MainHero && h.IsAlive
                             && (h.IsLord || h.IsMinorFactionHero))
                    .ToList();
                foreach (var hero in lords)
                {
                    if (_rng.NextDouble() >= GetSeedChanceForHero(hero)) continue;
                    try { SeedCrystalOnHero(hero); } catch { }
                }
            }
            catch { }
        }

        // ── Session start ─────────────────────────────────────────────────────

        private static void OnSessionLaunched(CampaignGameStarter starter)
        {
            try { RegisterCrystalMenus(starter); } catch { }
            try { RestockChamberTownShops(); }     catch { }
        }

        private static void OnWeeklyTick()
        {
            try { RestockChamberTownShops(); } catch { }
        }

        // Ensures each Crystalline Chamber town carries one of every crystal type
        // for sale. Called on load and weekly so stock recovers after purchases.
        internal static void RestockChamberTownShops()
        {
            foreach (var s in Settlement.All)
            {
                if (!HasCrystallineChamber(s)) continue;
                try
                {
                    var roster = s.ItemRoster;
                    if (roster == null) continue;
                    foreach (var def in CrystalCatalog.All)
                    {
                        var item = MBObjectManager.Instance?.GetObject<ItemObject>(def.ItemId);
                        if (item == null) continue;
                        int have = roster.GetItemNumber(item);
                        if (have < CrystalMath.ShopStockPerType)
                            roster.AddToCounts(item, CrystalMath.ShopStockPerType - have);
                    }
                }
                catch { }
            }
        }

        // ── Lord seeding ──────────────────────────────────────────────────────

        private static void OnHeroCreated(Hero hero, bool isBornNaturally)
        {
            if (hero == null || hero == Hero.MainHero) return;
            if (!hero.IsLord && !hero.IsMinorFactionHero) return;
            if (_rng.NextDouble() >= GetSeedChanceForHero(hero)) return;
            try { SeedCrystalOnHero(hero); } catch { }
        }

        // Gives a hero a random crystal in a free battle-equipment weapon slot.
        private static void SeedCrystalOnHero(Hero hero)
        {
            if (hero == null) return;
            var defs = CrystalCatalog.All;
            var def  = defs[_rng.Next(defs.Count)];
            var item = MBObjectManager.Instance?.GetObject<ItemObject>(def.ItemId);
            if (item == null) return;

            for (int i = 0; i < 4; i++)
            {
                if (hero.BattleEquipment[(EquipmentIndex)i].IsEmpty)
                {
                    hero.BattleEquipment[(EquipmentIndex)i] = new EquipmentElement(item);
                    break;
                }
            }
        }

        // ── Chamber check helper ──────────────────────────────────────────────

        internal static bool HasCrystallineChamber(Settlement s)
        {
            if (s == null || !s.IsTown) return false;
            try
            {
                string name = s.Name?.ToString() ?? "";
                return ChamberTowns.Any(c =>
                    name.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch { return false; }
        }

        // ── Faction-based seeding ─────────────────────────────────────────

        private static double GetSeedChanceForHero(Hero hero)
        {
            try
            {
                if (hero?.Clan?.Kingdom == null) return CrystalMath.LordSeedChance;

                string kingdomId = hero.Clan.Kingdom.StringId;
                // Sturgian mountain passes yield crystals.
                if (kingdomId == "sturgia") return 0.08;
                // Battanian forests and ruins hold them.
                if (kingdomId == "battania") return 0.08;
                // Northern Empire cities shelter them.
                if (kingdomId == "empire_n") return 0.06;
            }
            catch { }
            return CrystalMath.LordSeedChance;
        }

        // ── Material helpers ──────────────────────────────────────────────────

        internal static bool HasFormationMaterials(CrystalDef def, out string missingMsg)
        {
            missingMsg = null;
            try
            {
                var party = MobileParty.MainParty;
                if (party == null) { missingMsg = "No active party."; return false; }

                var silver = MBObjectManager.Instance?.GetObject<ItemObject>("silver_ore");
                if (silver == null || party.ItemRoster.GetItemNumber(silver) < CrystalMath.SilverOreCost)
                {
                    missingMsg = $"You need {CrystalMath.SilverOreCost} Silver Ore.";
                    return false;
                }

                var good = MBObjectManager.Instance?.GetObject<ItemObject>(def.TradeGoodId);
                if (good == null || party.ItemRoster.GetItemNumber(good) < CrystalMath.TradeGoodCost)
                {
                    string goodName = good?.Name?.ToString() ?? def.TradeGoodId;
                    missingMsg = $"You need {CrystalMath.TradeGoodCost}× {goodName}.";
                    return false;
                }
            }
            catch { missingMsg = "Could not check materials."; return false; }
            return true;
        }

        internal static void ConsumeFormationMaterials(CrystalDef def)
        {
            try
            {
                var roster = MobileParty.MainParty?.ItemRoster;
                if (roster == null) return;

                var silver = MBObjectManager.Instance?.GetObject<ItemObject>("silver_ore");
                if (silver != null) roster.AddToCounts(silver, -CrystalMath.SilverOreCost);

                var good = MBObjectManager.Instance?.GetObject<ItemObject>(def.TradeGoodId);
                if (good != null) roster.AddToCounts(good, -CrystalMath.TradeGoodCost);
            }
            catch { }
        }

        internal static void RefundSilverOre()
        {
            try
            {
                var roster = MobileParty.MainParty?.ItemRoster;
                var silver = MBObjectManager.Instance?.GetObject<ItemObject>("silver_ore");
                if (roster != null && silver != null)
                    roster.AddToCounts(silver, CrystalMath.SilverOreCost);
            }
            catch { }
        }

        internal static void GrantCrystal(CrystalDef def)
        {
            try
            {
                var roster = MobileParty.MainParty?.ItemRoster;
                var item   = MBObjectManager.Instance?.GetObject<ItemObject>(def.ItemId);
                if (roster != null && item != null)
                    roster.AddToCounts(item, 1);
            }
            catch { }
        }

        // ── Formation skill check ─────────────────────────────────────────────

        internal static bool RollFormation(out bool doubleGrow)
        {
            doubleGrow = false;
            int med = 0, eng = 0;
            try
            {
                if (Hero.MainHero != null)
                {
                    med = Hero.MainHero.GetSkillValue(DefaultSkills.Medicine);
                    eng = Hero.MainHero.GetSkillValue(DefaultSkills.Engineering);
                }
            }
            catch { }

            float odds = TalentSystem.Has(TalentId.PatientGrowth)
                ? CrystalMath.FormationOddsWithPatience(med, eng)
                : CrystalMath.FormationOdds(med, eng);

            bool success = _rng.NextDouble() < odds;
            if (success && TalentSystem.Has(TalentId.PatientGrowth))
                doubleGrow = _rng.NextDouble() < CrystalMath.DoubleGrowChance;

            return success;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        internal static int CurrentCampaignDay()
        {
            try { return (int)CampaignTime.Now.ToDays; } catch { return 0; }
        }
    }
}
