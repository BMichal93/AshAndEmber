// =============================================================================
// ASH AND EMBER — AI/NpcCastPlanner.cs
//
// Pure decision math for how an NPC mage lord SPENDS his magic — no TaleWorlds
// types, fully testable (PureLogicTests).
//
// A lord pays LIFE EXPECTANCY to cast (like the player): every working lowers the
// age at which the inner fire finally burns him out. His remaining life is a real,
// finite RESOURCE. A person spends a resource differently depending on how much of
// it is left and on temperament:
//   • plenty of life  → cast big and often
//   • near burnout    → hoard the years: weaker workings, cast rarely, and only
//                        pour out full power when survival is on the line
//   • Impulsive lords spend recklessly regardless; Calculating lords are misers.
//
// The Ashen pay nothing (the cold preserves what remains) and never route through
// this planner — the caller skips it for them.
// =============================================================================

using System;

namespace AshAndEmber
{
    // A lord's temperament, read from DefaultTraits.Calculating at the call site.
    public enum CasterTemper { Calculating, Balanced, Impulsive }

    public static class NpcCastPlanner
    {
        // Years of remaining life at (or beyond) which a lord treats his reserve as
        // full and spends without restraint.
        public const float FullLifeYears = 40f;

        // ── Situational base power (before the life/temper multiplier) ────────────
        // How hard the SITUATION alone calls for power, independent of resources.
        public const float BaseDesperate = 1.00f; // survival — pour it all out
        public const float BaseCluster   = 0.90f; // a fat target worth spending on
        public const float BaseHarass    = 0.60f; // opportunistic pressure, cheap

        // Non-emergency power never drops below the floor a temper is willing to
        // spend down to when his life runs short.
        public static float MinPowerMult(CasterTemper temper)
        {
            switch (temper)
            {
                case CasterTemper.Calculating: return 0.50f; // the miser — weak workings when old
                case CasterTemper.Impulsive:   return 0.90f; // barely restrains himself
                default:                        return 0.65f;
            }
        }

        // When survival is on the line, thrift is forgotten — power is floored high.
        public const float EmergencyFloor = 0.90f;

        // 0 at no life left, 1 once the reserve is "full" (FullLifeYears+).
        public static float LifeFrac(float budgetYears)
        {
            if (budgetYears <= 0f) return 0f;
            float f = budgetYears / FullLifeYears;
            return f > 1f ? 1f : f;
        }

        // The life/temper multiplier on a cast's power: lerps from the temper's
        // MinPowerMult (life spent) up to 1.0 (life plentiful). Emergencies floor it.
        public static float PowerMult(float lifeFrac, CasterTemper temper, bool emergency)
        {
            float min = MinPowerMult(temper);
            float m = min + (1f - min) * Clamp01(lifeFrac);
            if (emergency && m < EmergencyFloor) m = EmergencyFloor;
            return m;
        }

        // Final cast power = situational demand × the life/temper multiplier, with
        // an emergency floor and a sane clamp (>1 lets boss-tier callers overcharge).
        public static float CastPower(float situationBase, float lifeFrac,
                                      CasterTemper temper, bool emergency)
        {
            float p = situationBase * PowerMult(lifeFrac, temper, emergency);
            if (emergency && p < EmergencyFloor) p = EmergencyFloor;
            return p < 0.35f ? 0.35f : p > 1.2f ? 1.2f : p;
        }

        // ── Overchannel (the doubled working) ─────────────────────────────────────
        // A lord may pour everything into one cast for a doubled effect — the same
        // overchannel the player reaches by holding the draw. It costs twice the
        // life, so a lord short on years won't risk it unless survival demands it.
        // Chance rises with recklessness of temper and with desperation.
        public static float OverchannelChance(CasterTemper temper, float lifeFrac, bool emergency)
        {
            if (!emergency && Clamp01(lifeFrac) < 0.35f) return 0f; // too old to gamble the years
            float baseChance;
            switch (temper)
            {
                case CasterTemper.Impulsive:   baseChance = 0.25f; break; // holds little back
                case CasterTemper.Calculating: baseChance = 0.05f; break; // a miser with his fire
                default:                        baseChance = 0.12f; break;
            }
            if (emergency) baseChance += 0.25f;   // desperate lords spend everything
            return baseChance > 0.8f ? 0.8f : baseChance;
        }

        // Roll `roll` (0..1) against the overchannel chance.
        public static bool ShouldOverchannel(CasterTemper temper, float lifeFrac, bool emergency, float roll)
            => roll < OverchannelChance(temper, lifeFrac, emergency);

        // Double an already-decided cast power (bypasses the normal 1.2 clamp — the
        // overchannel is meant to exceed it).
        public static float Overchannelled(float basePower) => basePower * ElementMagicMath.OverchannelMult;

        // How far a temper will stretch his casting cadence when near burnout.
        public static float MaxCooldownStretch(CasterTemper temper)
        {
            switch (temper)
            {
                case CasterTemper.Calculating: return 2.5f; // goes quiet to save his years
                case CasterTemper.Impulsive:   return 1.1f; // hardly slows at all
                default:                        return 1.7f;
            }
        }

        // Cooldown multiplier: 1.0 while life is plentiful, growing toward the
        // temper's MaxCooldownStretch as the reserve empties.
        public static float CooldownMult(float lifeFrac, CasterTemper temper)
        {
            float scarcity   = 1f - Clamp01(lifeFrac); // 0 fresh .. 1 near burnout
            float maxStretch = MaxCooldownStretch(temper);
            return 1f + (maxStretch - 1f) * scarcity;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
