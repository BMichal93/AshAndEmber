// =============================================================================
// ASH AND EMBER — AI/ForestClansCulture.cs
// The Forest Clans (formerly Battania) culture's mechanics for the player.
//
//   Kinship of Root and Stone — sacred-site Kindled bindings cost 15% less and
//                               succeed 10% more often for the clan-born. (bonus)
//   The Green Roads           — Forest Clan parties move faster while crossing
//                               forest ground. (bonus)
//   Wild and Few              — untamed forest folk close to the land, slow to the
//                               drilled ways of great armies: their warbands run
//                               leaner and cost more to keep. (penalty)
//
// The binding discount is read by SacredSitesCampaignBehavior; the forest speed
// bonus is applied by ForestClansSpeedModel; the wage/size penalty is applied by
// ForestClansWageModel and ForestClansPartySizeModel (all registered in
// MainSubModule).
// =============================================================================

using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;

namespace AshAndEmber
{
    internal static class ForestClansCulture
    {
        public static bool IsPlayerForestClan
        {
            get { try { return Hero.MainHero?.Culture?.StringId == "battania"; } catch { return false; } }
        }

        // ── Kinship of Root and Stone (sacred-site discount) ───────────────────
        private const float SiteCostMult  = 0.85f;  // 15% cheaper
        private const float SiteOddsBonus = 0.10f;  // +10 percentage points

        public static int SiteCost(int baseCost)
        {
            if (!IsPlayerForestClan || baseCost <= 0) return baseCost;
            int reduced = (int)Math.Round(baseCost * SiteCostMult, MidpointRounding.AwayFromZero);
            return Math.Max(1, reduced);
        }

        public static float SiteOdds(float baseOdds)
            => IsPlayerForestClan ? baseOdds + SiteOddsBonus : baseOdds;

        // ── Tuning (values live here so the culture's numbers stay in one place) ─
        public const float ForestSpeedFactor = 0.20f;   // +20% while in forest (The Green Roads)
        public const float WildWageFactor    = 0.05f;   // +5% troop wages        (Wild and Few)
        public const float WildSizeFactor    = 0.05f;   // −5% party size limit   (Wild and Few)

        // Is this party a Forest Clans party (player clan and Forest Clans culture)?
        // Used by the game models, which cannot read IsPlayerForestClan because they
        // must judge each party, not just the main hero.
        public static bool IsForestClanParty(MobileParty party)
        {
            try { return party?.ActualClan == Clan.PlayerClan && IsPlayerForestClan; }
            catch { return false; }
        }

        // True when the party stands on forest ground, per the map scene terrain.
        public static bool IsInForest(MobileParty party)
        {
            try
            {
                var terrain = Campaign.Current?.MapSceneWrapper?.GetTerrainTypeAtPosition(party.Position);
                return terrain.HasValue && terrain.Value == TerrainType.Forest;
            }
            catch { return false; }
        }
    }
}
