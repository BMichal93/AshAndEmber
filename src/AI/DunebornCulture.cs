// =============================================================================
// ASH AND EMBER — AI/DunebornCulture.cs
// Cultural mechanics for a player of the Duneborn (aserai) culture.
//
// Blood Tithe — the Duneborn's pact with the thing beneath the dunes replaces
// the old merchant ways: instead of the vanilla cheaper-caravans bonus (its
// feat is zeroed in AshenCitySystem.Renaming), every Dark Altar sacrifice
// costs 20% less — prisoners and captive lords alike.
// =============================================================================

using System;
using TaleWorlds.CampaignSystem;

namespace AshAndEmber
{
    internal static class DunebornCulture
    {
        // The player chose the Duneborn (Aserai) culture at character creation.
        public static bool IsPlayerDuneborn
        {
            get { try { return Hero.MainHero?.Culture?.StringId == "aserai"; } catch { return false; } }
        }

        // ── Blood Tithe (altar discount) ───────────────────────────────────────
        private const float AltarCostMult = 0.8f;   // sacrifices 20% cheaper

        // Applied to every Dark Altar sacrifice cost (prisoners and lords).
        // Rounded to nearest; a nonzero base cost never drops below 1.
        public static int AltarCost(int baseCost)
        {
            if (!IsPlayerDuneborn || baseCost <= 0) return baseCost;
            int reduced = (int)Math.Round(baseCost * AltarCostMult, MidpointRounding.AwayFromZero);
            return Math.Max(1, reduced);
        }
    }
}
