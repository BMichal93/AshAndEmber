// =============================================================================
// ASH AND EMBER — Miracles/MiracleInventory.cs
//
// Grace and Cold counters. Grace and Cold are mutually exclusive: the flame
// will not kindle in a vessel already claimed by the cold, and vice versa.
// Both cap at GraceColdCap. State is serialized by MiracleCampaignBehavior
// via MIRACLE_* save keys; a save without those keys loads as zero.
// =============================================================================

using System;

namespace AshAndEmber
{
    public static class MiracleInventory
    {
        internal static int _grace = 0;
        internal static int _cold  = 0;

        public static int  Grace    => _grace;
        public static int  Cold     => _cold;
        public static bool HasGrace => _grace > 0;
        public static bool HasCold  => _cold > 0;

        // Returns how many points were actually added (0 if blocked or already at cap).
        public static int AddGrace(int amount)
        {
            if (_cold > 0) return 0;
            int add = Math.Min(amount, MiracleMath.GraceColdCap - _grace);
            if (add <= 0) return 0;
            _grace += add;
            return add;
        }

        public static int AddCold(int amount)
        {
            if (_grace > 0) return 0;
            int add = Math.Min(amount, MiracleMath.GraceColdCap - _cold);
            if (add <= 0) return 0;
            _cold += add;
            return add;
        }

        public static bool SpendGrace()
        {
            if (_grace <= 0) return false;
            _grace--;
            return true;
        }

        public static bool SpendCold()
        {
            if (_cold <= 0) return false;
            _cold--;
            return true;
        }

        public static void ResetForNewGame()
        {
            _grace = 0;
            _cold  = 0;
        }
    }
}
