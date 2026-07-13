// =============================================================================
// ASH AND EMBER — GreatAwakening/GreatAwakeningMath.cs
//
// Pure numeric core of THE GREAT AWAKENING — Duneborn's bid to drag something
// from beyond the Sands into Calradia on ten thousand sacrificed lives. No
// TaleWorlds types (fully covered by PureLogicTests). Runtime behaviour lives
// in GreatAwakeningCampaignBehavior.* / GreatOtherParty.cs / GreatOtherBattle.cs.
// =============================================================================

namespace AshAndEmber
{
    public static class GreatAwakeningMath
    {
        // ── Discovery trigger ────────────────────────────────────────────────────
        // No chance at all before day 50. From day 50: 10%, rising by another 10%
        // every full 20 days elapsed since day 50, capped at 100%. Rolled weekly,
        // once ever (the roll stops the moment it succeeds).
        public const int   TriggerStartDay      = 50;
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

        // ── The sacrifice ────────────────────────────────────────────────────────
        // Crazy difficult, not impossible: reachable in roughly 1-2 dedicated
        // in-game years given sustained player sacrifice plus the NPC trickle below.
        public const int PrisonerTarget = 10_000;

        // ── NPC lords feeding the altar ──────────────────────────────────────────
        // A background trickle, not the primary driver — "sometimes," per a
        // weekly roll per Duneborn lord holding prisoners.
        public const float NpcWeeklyContributionChance = 0.15f;
        public const int   NpcContributionMin = 3;
        public const int   NpcContributionMax = 15;

        public static int NpcContributionAmount(System.Random rng, int prisonersHeld)
        {
            if (rng == null || prisonersHeld <= 0) return 0;
            int roll = NpcContributionMin + rng.Next(NpcContributionMax - NpcContributionMin + 1);
            return roll < prisonersHeld ? roll : prisonersHeld;
        }

        // ── The world turns on Duneborn ──────────────────────────────────────────
        // Past 60% of the count, the scale of the slaughter can no longer be hidden.
        // Every other kingdom at peace with Duneborn rolls weekly to declare war —
        // "a little more hostile," not an instant world war.
        public const float OppositionThresholdFraction = 0.60f;
        public const float RousedWeeklyWarChance       = 0.10f;

        public static bool OppositionRoused(int sacrificed, int target)
            => target > 0 && sacrificed * 100L >= (long)target * (long)(OppositionThresholdFraction * 100f + 0.5f);

        // ── The Great Other's party ──────────────────────────────────────────────
        public const int   RevenantCap             = 100;
        public const int   RevenantTopUpIntervalDays = 7;
        public const int   HungerIntervalDays        = 30;

        // 50/50 at the moment the summoning completes, independent of which path
        // the player was on.
        public static bool ResolutionIsControlled(System.Random rng)
            => rng != null && rng.NextDouble() < 0.5;
    }
}
