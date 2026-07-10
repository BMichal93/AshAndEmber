// =============================================================================
// ASH AND EMBER — NorthmenStones/NorthmenStonesMath.cs
//
// Pure numeric core of THE BONEFIRE CIRCLE — the Northmen seers' bid to raise
// standing stones at Varcheg and burn back anything Ashen that tries to cross.
// No TaleWorlds types (fully covered by PureLogicTests). Runtime behaviour
// lives in NorthmenStonesCampaignBehavior.*.
// =============================================================================

namespace AshAndEmber
{
    public static class NorthmenStonesMath
    {
        // ── Discovery trigger ────────────────────────────────────────────────────
        // No chance at all before day 30. From day 30: 10%, rising by another 10%
        // every full 20 days elapsed since day 30, capped at 100%. Rolled weekly,
        // once ever (a Northmen player instead gets the call immediately).
        public const int   TriggerStartDay      = 30;
        public const float TriggerBaseChance    = 0.10f;
        public const float TriggerChancePerStep = 0.10f;
        public const int   TriggerStepDays      = 20;

        public static float TriggerChance(int day)
        {
            if (day < TriggerStartDay) return 0f;
            int steps = (day - TriggerStartDay) / TriggerStepDays;
            float chance = TriggerBaseChance + TriggerChancePerStep * steps;
            return chance > 1f ? 1f : chance;
        }

        // ── Material targets ─────────────────────────────────────────────────────
        // Six independent tracks so no single grind dominates — reachable in
        // roughly 2 dedicated in-game years, same ambition as the Great
        // Awakening's single 10,000-prisoner counter. The bound-Kindled track is
        // the deliberately hard one: each unit must first be fought and bound at
        // a Forest Clans sacred site (SacredSitesCampaignBehavior) before it can
        // be marched to Varcheg and given up.
        public const int IronTarget     = 5_000;
        public const int HardwoodTarget = 5_000;
        public const int ToolsTarget    = 2_500;
        public const int SilverTarget   = 1_200;
        public const int DenarsTarget   = 250_000;

        public const int KindledKindCount     = 6; // Stone, Frost, Sand, Flame, Tide, Gale
        public const int KindledTargetPerKind  = 2;
        public const int KindledTotalTarget    = KindledKindCount * KindledTargetPerKind;

        public static float ClampedRatio(double current, double target)
        {
            if (target <= 0) return 1f;
            float r = (float)(current / target);
            if (r < 0f) r = 0f;
            if (r > 1f) r = 1f;
            return r;
        }

        // Mean of the six clamped ratios — the "how close is the working" gauge
        // used for both the invasion thresholds and the journal progress bar.
        public static float BlendedProgress(
            int iron, int hardwood, int tools, int silver, int denars, int kindledTotal)
        {
            float sum =
                ClampedRatio(iron,     IronTarget) +
                ClampedRatio(hardwood, HardwoodTarget) +
                ClampedRatio(tools,    ToolsTarget) +
                ClampedRatio(silver,   SilverTarget) +
                ClampedRatio(denars,   DenarsTarget) +
                ClampedRatio(kindledTotal, KindledTotalTarget);
            return sum / 6f;
        }

        // Completion requires EVERY track individually full, not just a high
        // blended average — "once all materials are delivered."
        public static bool IsMaterialsComplete(
            int iron, int hardwood, int tools, int silver, int denars, int kindledTotal)
            => iron >= IronTarget && hardwood >= HardwoodTarget && tools >= ToolsTarget
            && silver >= SilverTarget && denars >= DenarsTarget && kindledTotal >= KindledTotalTarget;

        // ── Decay while Varcheg is not in Northmen hands ─────────────────────────
        // "Materials are persistent... unless the city is captured by a different
        // faction, in which case they disappear by 10 percent per week."
        public const float DecayFactorPerWeek = 0.90f;

        public static int ApplyWeeklyDecay(int amount)
        {
            if (amount <= 0) return 0;
            int decayed = (int)(amount * DecayFactorPerWeek);
            return decayed < 0 ? 0 : decayed;
        }

        // ── NPC lords contributing in the background ─────────────────────────────
        // A trickle, not the primary driver — "sometimes," per a weekly roll per
        // Northmen lord holding a fief.
        public const float NpcWeeklyContributionChance = 0.12f;

        public static int NpcContributionAmount(System.Random rng, int min, int max)
        {
            if (rng == null || max <= min) return 0;
            return min + rng.Next(max - min + 1);
        }

        // ── Ashen invasion waves at 50 / 75 / 90% ────────────────────────────────
        public static readonly float[] InvasionThresholds = { 0.50f, 0.75f, 0.90f };

        public static int InvasionBandCount(int tier)
        {
            switch (tier)
            {
                case 0: return 2;
                case 1: return 3;
                default: return 4;
            }
        }

        public static float InvasionMinStrength(int tier)
        {
            switch (tier)
            {
                case 0: return 150f;
                case 1: return 220f;
                default: return 300f;
            }
        }

        // ── The Greater Emberfall (post-ending recurring strike) ─────────────────
        public const int   EmberfallIntervalDays     = 7;
        public const float EmberfallGarrisonKillFrac  = 0.70f;
        public const float EmberfallStatRemainingFrac = 0.30f; // Prosperity/Security/FoodStocks multiplier
    }
}
