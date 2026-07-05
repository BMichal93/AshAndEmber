// =============================================================================
// ASH AND EMBER — ClanOrders/ClanOrdersMath.cs
// Pure, TaleWorlds-free math for clan-order loyalty. Kept pure so it can be
// covered by PureLogicTests (see behaviour.md — no engine types in this path).
// =============================================================================

using System;

namespace AshAndEmber
{
    public static class ClanOrdersMath
    {
        // Per-day chance (0–100) that an ordered clan party abandons its orders and
        // returns to its own counsel. A commander with no Leadership can barely hold
        // their captains to a distant task; strong Leadership removes the risk
        // entirely. Rolled once per day per active order.
        public static int DailyAbandonChance(int leadership)
        {
            if (leadership < 0) leadership = 0;
            int chance = 10 - leadership / 30;
            return chance < 0 ? 0 : chance;
        }
    }
}
