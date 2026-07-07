// =============================================================================
// ASH AND EMBER — Elementals/ElementalLordRegistry.cs
//
// Forest Clans (Battania) lords keep the same bond with the sacred sites the
// player can now craft at: a chance, seeded once per campaign (and rolled
// again for every lord born after), that a lord's own war-band already
// marches with one or more Kindled bound to its cause.
//
// This seeds real, persistent roster troops (sacred_kindled_*, troops.xml) —
// not a mission-only spawn — so it reuses the exact roster-injection idiom
// used elsewhere in the mod (TavernCampaignBehavior.Outcomes, PriestTroops).
// =============================================================================

using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static class ElementalLordRegistry
    {
        private static readonly Random _rng = new Random();

        // The Forest Clans alone keep this bond — no other culture's lords seed it.
        private const float ForestClansLordChance = 0.18f;
        private const int   MinKindled = 1;
        private const int   MaxKindled = 3;   // inclusive

        // One-time seeding of the lords that already exist at campaign start.
        // OnHeroCreated only fires for heroes born/spawned AFTER the campaign
        // begins, so this pass covers the starting cast the same way
        // CrystallinesCampaignBehavior.EstablishForNewCampaign does.
        public static void EstablishForNewCampaign()
        {
            try
            {
                var lords = Hero.AllAliveHeroes
                    .Where(h => h != null && h != Hero.MainHero && h.IsAlive
                             && (h.IsLord || h.IsMinorFactionHero)
                             && (h.Culture?.StringId ?? "") == "battania")
                    .ToList();
                foreach (var hero in lords)
                {
                    if (_rng.NextDouble() >= ForestClansLordChance) continue;
                    try { SeedElementalsOnHero(hero); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        public static void OnHeroCreated(Hero hero, bool isBornNaturally)
        {
            if (hero == null || hero == Hero.MainHero) return;
            if (!hero.IsLord && !hero.IsMinorFactionHero) return;
            if ((hero.Culture?.StringId ?? "") != "battania") return;
            if (_rng.NextDouble() >= ForestClansLordChance) return;
            try { SeedElementalsOnHero(hero); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void SeedElementalsOnHero(Hero hero)
        {
            var party = hero?.PartyBelongedTo;
            if (party?.MemberRoster == null) return;

            var defs = SacredSiteCatalog.All;
            if (defs.Count == 0) return;
            int count = MinKindled + _rng.Next(MaxKindled - MinKindled + 1);
            for (int i = 0; i < count; i++)
            {
                var def = defs[_rng.Next(defs.Count)];
                var troop = MBObjectManager.Instance?.GetObject<CharacterObject>(def.TroopId);
                if (troop == null) continue;
                try { party.MemberRoster.AddToCounts(troop, 1); }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }
    }
}
