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

        // Supplies the Abundant Grace cap bonus. Defaults to a pure 0 so the Grace
        // bank can be unit-tested without loading TaleWorlds; production wires this to
        // MiracleTalents.GraceCapBonus in MainSubModule.OnGameStart (see behaviour.md).
        public static Func<int> TalentCapBonusProvider = () => 0;

        public static int  Grace    => _grace;
        public static bool HasGrace => _grace > 0;

        // Returns how many points were actually added (0 if blocked or at cap).
        public static int AddGrace(int amount)
        {
            int cap = MiracleMath.GraceCap(TalentCapBonusProvider());
            int add = Math.Min(amount, cap - _grace);
            if (add <= 0) return 0;
            _grace += add;
            return add;
        }

        public static bool SpendGrace() => SpendGrace(1);

        // The Undivided Flame / The Reckoning ask more than the usual toll. Spends
        // nothing and fails if the bank can't cover the full amount.
        public static bool SpendGrace(int amount)
        {
            if (amount <= 0 || _grace < amount) return false;
            _grace -= amount;
            return true;
        }

        public static void ResetForNewGame()
        {
            _grace = 0;
        }
    }
}
