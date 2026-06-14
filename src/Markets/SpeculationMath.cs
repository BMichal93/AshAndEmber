// =============================================================================
// ASH AND EMBER — Markets/SpeculationMath.cs
// Pure numeric rules for the Goods Exchange speculation game. No TaleWorlds
// types — everything here is covered by tests/PureLogicTests.cs.
//
// The game: the player stakes denars on a commodity position that opens at
// 100%. Each round a hidden roll moves the position; the player may SELL
// (stake × position), HOLD STEADY (small swing), or SPECULATE HARD (big swing,
// extra crash risk). Crash risk grows every round spent in the position —
// early rounds are positive expected value, late rounds are not. A crash
// forfeits the position except for a Trade-skill salvage. Running out of
// rounds forces the sale at 90% of book.
//
// Volatility classes (commodity tiers):
//   0 = staple  (Grain, Wool…)    — small swings, lowest crash base
//   1 = crafted (Wine, Iron…)     — medium swings
//   2 = luxury  (Velvet, Spice…)  — wild swings, highest crash base
//
// Trade skill: +1 round per 75 points (cap 8), up to −8 pp crash chance at
// 300, salvage 1% per 10 points (cap 30%).
// =============================================================================

using System;

namespace AshAndEmber
{
    public static class SpeculationMath
    {
        public const int MultiplierStart = 100;
        public const int MultiplierMin   = 10;
        public const int MultiplierMax   = 300;
        public const int ForcedSalePct   = 90;
        public const int CooldownDays    = 4;

        public static readonly int[] StakeTiers = { 500, 2000, 5000, 10000, 50000 };

        /// Rounds before the brokers force the sale: 4 base, +1 per 75 Trade, cap 8.
        public static int RoundsLimit(int tradeSkill)
            => Math.Min(8, 4 + Math.Max(0, tradeSkill) / 75);

        /// Town-prosperity mood: booming markets lift every roll, depressed ones drag it.
        public static int MoodShift(float prosperity)
            => prosperity >= 5000f ? 5 : prosperity < 2000f ? -5 : 0;

        /// Chance the position collapses on the roll about to happen.
        /// Grows 3 pp per round already survived; speculating hard adds 4 pp;
        /// Trade skill shaves up to 8 pp. Clamped to [1%, 50%].
        public static float CrashChance(int volatility, int completedRounds, int tradeSkill, bool aggressive)
        {
            float baseChance = volatility >= 2 ? 0.05f : volatility == 1 ? 0.04f : 0.03f;
            float c = baseChance
                    + 0.03f * Math.Max(0, completedRounds)
                    + (aggressive ? 0.04f : 0f)
                    - Math.Min(0.08f, Math.Max(0, tradeSkill) / 3750f);
            return c < 0.01f ? 0.01f : (c > 0.50f ? 0.50f : c);
        }

        /// Lower bound of the hidden percentage-point roll for one round.
        public static int DeltaMin(int volatility, bool aggressive, int moodShift)
        {
            int v = volatility >= 2 ? (aggressive ? -28 : -12)
                  : volatility == 1 ? (aggressive ? -20 : -8)
                  :                   (aggressive ? -14 : -6);
            return v + moodShift;
        }

        /// Upper bound of the hidden percentage-point roll for one round.
        public static int DeltaMax(int volatility, bool aggressive, int moodShift)
        {
            int v = volatility >= 2 ? (aggressive ? 62 : 24)
                  : volatility == 1 ? (aggressive ? 46 : 18)
                  :                   (aggressive ? 34 : 14);
            return v + moodShift;
        }

        /// Moves the position by a rolled delta, clamped to [10%, 300%].
        public static int ApplyDelta(int multiplier, int delta)
        {
            int m = multiplier + delta;
            return m < MultiplierMin ? MultiplierMin : (m > MultiplierMax ? MultiplierMax : m);
        }

        /// Fraction of the stake recovered on a crash: 1% per 10 Trade, cap 30%.
        public static int SalvagePercent(int tradeSkill)
            => Math.Min(30, Math.Max(0, tradeSkill) / 10);

        public static int Payout(int stake, int multiplier)
            => stake * multiplier / 100;

        public static int ForcedSalePayout(int stake, int multiplier)
            => Payout(stake, multiplier) * ForcedSalePct / 100;

        public static int CrashSalvage(int stake, int tradeSkill)
            => stake * SalvagePercent(tradeSkill) / 100;

        /// Trade XP for a closed venture: half the profit (cap 1000), or a
        /// 50-XP consolation when the venture broke even or lost coin.
        public static int TradeXp(int stake, int payout)
        {
            int profit = payout - stake;
            return profit <= 0 ? 50 : Math.Min(1000, profit / 2);
        }
    }
}
