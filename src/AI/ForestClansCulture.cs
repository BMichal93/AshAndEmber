// =============================================================================
// ASH AND EMBER — AI/ForestClansCulture.cs
// The Forest Clans (formerly Battania) culture's mechanics for the player.
//
//   Kinship of Root and Stone — sacred-site Kindled bindings cost 15% less and
//                               succeed 10% more often for the clan-born.
//   The Wilds Remember        — wild-band Kindled strike a Forest Clans hand
//                               for half the usual damage.
//   Debt of the Deep Wood     — every bound Kindled costs 5 gold a day.
//
// The upkeep drain is applied via DailyTick(), called from
// CampaignBehavior.Ticks alongside TempleCulture/TribalCulture. The binding
// discount is read by SacredSitesCampaignBehavior; the damage halving is read
// by ElementalBeings.TryLooseElement.
// =============================================================================

using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.ObjectSystem;

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

        // ── The Wilds Remember (wild-band Kindled damage vs. the player) ───────
        private const float WildKindledDamageMult = 0.5f;

        public static float WildKindledDamageMultiplier()
            => IsPlayerForestClan ? WildKindledDamageMult : 1f;

        // ── Debt of the Deep Wood (daily upkeep for bound Kindled) ─────────────
        private const int KindledUpkeepPerDay = 5;

        public static void DailyTick()
        {
            if (!IsPlayerForestClan) return;
            try
            {
                var roster = MobileParty.MainParty?.MemberRoster;
                if (roster == null) return;
                var kindledIds = new System.Collections.Generic.HashSet<string>(
                    SacredSiteCatalog.All.Select(def => def.TroopId), StringComparer.OrdinalIgnoreCase);
                int bound = roster.GetTroopRoster()
                    .Where(e => e.Character != null && kindledIds.Contains(e.Character.StringId))
                    .Sum(e => e.Number);
                if (bound <= 0) return;
                // Kindred Ease (Sacred Site talent) halves the daily toll.
                int upkeep = (int)Math.Round(bound * KindledUpkeepPerDay * SacredSiteTalents.UpkeepMult,
                    MidpointRounding.AwayFromZero);
                try { Hero.MainHero?.ChangeHeroGold(-upkeep); }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
