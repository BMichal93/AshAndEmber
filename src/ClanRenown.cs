// =============================================================================
// ASH AND EMBER — ClanRenown.cs
// The one correct way to move a clan's renown.
//
// Writing Clan.Renown directly is a trap: the setter is a bare auto-property.
// Gains applied that way never recalculate the clan Tier (only Clan.AddRenown
// does, and only upward), and losses can push renown BELOW the current tier's
// lower limit — the engine never de-tiers, so the clan-screen progress bar then
// shows nonsense and appears frozen until renown climbs all the way back to the
// tier floor. That was the "renown bar shows wrong numbers / stops growing
// after being targeted by a plot" bug.
//
//   • Gain(clan, amount)  → Clan.AddRenown (tier-up + OnClanTierChanged fire)
//   • Lose(clan, amount)  → subtract, floored at the CURRENT tier's renown
//                           threshold (fame erodes; rank, once won, holds)
//   • RepairFloor(clan)   → self-heal for saves already damaged by old direct
//                           writes: lifts renown back up to the tier floor
// =============================================================================

using TaleWorlds.CampaignSystem;

namespace AshAndEmber
{
    internal static class ClanRenown
    {
        internal static void Gain(Clan clan, float amount)
        {
            if (clan == null || amount <= 0f) return;
            try { clan.AddRenown(amount); }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        internal static void Lose(Clan clan, float amount)
        {
            if (clan == null || amount <= 0f) return;
            try
            {
                float floor = TierFloor(clan);
                float target = clan.Renown - amount;
                clan.Renown = target < floor ? floor : target;
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Saves written before this class existed can hold renown below the tier
        // floor. Lifting it back to the floor unsticks the clan-screen bar.
        internal static void RepairFloor(Clan clan)
        {
            if (clan == null) return;
            try
            {
                float floor = TierFloor(clan);
                if (clan.Renown < floor) clan.Renown = floor;
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static float TierFloor(Clan clan)
        {
            try { return Campaign.Current?.Models?.ClanTierModel?.GetRequiredRenownForTier(clan.Tier) ?? 0f; }
            catch { return 0f; }
        }
    }
}
