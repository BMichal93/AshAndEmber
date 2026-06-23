// =============================================================================
// ASH AND EMBER — AI/TempleCulture.cs
// The Templar (formerly Vlandian) culture's starting feats — real mechanics.
//
//   ✅ Dawn's Grace      — if Grace is empty at dawn, the Light restores one point.
//   ✅ Oath of the Vigil — a standing party-morale bonus from drilled faith.
//   ⚠️ The Order's Price — dark gifts cost twice as much, and drawing the living
//                          ember takes one second longer (the order shuns both).
//
// The daily effects are applied from MagicCampaignBehavior's daily tick; the cost /
// channel-time penalties are read at the point each is charged. All gated on the
// player having chosen the Templar (vlandia) culture at creation.
// =============================================================================

using TaleWorlds.CampaignSystem;

namespace AshAndEmber
{
    internal static class TempleCulture
    {
        // The player chose the Templar (Vlandian) culture at character creation.
        public static bool IsPlayerTemplar
        {
            get { try { return Hero.MainHero?.Culture?.StringId == "vlandia"; } catch { return false; } }
        }

        // ── The Order's Price (penalties) ──────────────────────────────────────
        private const int   DarkGiftCostMultiplier    = 2;   // dark gifts cost twice as much
        private const float ExtraNatureChannelSeconds = 1f;  // drawing the ember is a beat slower

        public static int DarkGiftCost(int baseCost)
            => IsPlayerTemplar ? baseCost * DarkGiftCostMultiplier : baseCost;

        public static float NatureChannelSeconds(float baseSeconds)
            => IsPlayerTemplar ? baseSeconds + ExtraNatureChannelSeconds : baseSeconds;

        // ── Oath of the Vigil (bonus) ──────────────────────────────────────────
        public const int DailyMoraleBonus = 4;
    }
}
