// =============================================================================
// ASH AND EMBER — Miracles/MiracleInventory.cs
//
// Grace counter. Grace is earned by praying at Sanctuaries and spent on
// miracles (Shift+X / Ctrl+sequence). State is serialized by
// MiracleCampaignBehavior via the MIRACLE_Grace save key.
//
// Cold was removed when Dark Altars switched to permanent Dark Gifts.
// Cold miracle effects are still defined and used by NPCs; the player
// simply has no way to accumulate Cold any more.
// =============================================================================

using System;

namespace AshAndEmber
{
    public static class MiracleInventory
    {
        internal static int _grace = 0;

        public static int  Grace    => _grace;
        public static bool HasGrace => _grace > 0;

        // Returns how many points were actually added (0 if blocked or at cap).
        public static int AddGrace(int amount)
        {
            int add = Math.Min(amount, MiracleMath.GraceCap() - _grace);
            if (add <= 0) return 0;
            _grace += add;
            return add;
        }

        public static bool SpendGrace()
        {
            if (_grace <= 0) return false;
            _grace--;
            return true;
        }

        public static void ResetForNewGame()
        {
            _grace = 0;
        }
    }
}
