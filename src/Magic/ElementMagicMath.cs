// =============================================================================
// ASH AND EMBER — Magic/ElementMagicMath.cs
//
// Pure numeric core of the unified elemental magic that merges the old fire and
// nature systems. No TaleWorlds types — fully testable (PureLogicTests).
//
// One inner fire, five elements. Fire is the physical-and-spiritual root and is
// always available; Wind / Earth / Water / Spirit are learned. Each element has
// an ATTACK (cone/blast) and a WALL.
//
// Casting: hold focus, stand still, and DRAW a charge. The length of the draw
// sets the working's POWER, not its price — an instant release is weak, and the
// power climbs to full at a 10 s cap (drawing longer gains nothing). Hold a full
// 10 s without releasing and the gathered energy DISPERSES — you must draw again.
// The aging cost is FLAT: the same however long you drew. The Nature discipline
// makes that flat cost cheaper (the patient, land-tuned draw spends fewer years).
// Aging "burns through" exactly like the old fire magic.
// =============================================================================

using System;

namespace AshAndEmber
{
    // The five elements of the unified magic. Fire is the default/free root.
    public enum MagicElement { Fire = 0, Wind = 1, Earth = 2, Water = 3, Spirit = 4 }

    // Which half of an element's expression is being cast.
    public enum CastForm { Attack, Wall }

    public static class ElementMagicMath
    {
        // ── Draw / charge → POWER ────────────────────────────────────────────────
        // No minimum: you may release instantly. The draw sets power, from a weak
        // instant cast up to FULL strength at FullChargeSeconds — then it holds at
        // full through a short grace window before the charge finally DISPERSES at
        // MaxDrawSeconds. That window is when the working is at its most potent.
        public const float FullChargeSeconds = 7f;    // power reaches its peak here
        public const float MaxDrawSeconds     = 15f;   // held this long, the charge disperses
        public const float MinPower           = 0.5f;  // strength of an instant (0 s) cast
        public const float MaxPower           = 1.0f;  // strength once fully charged

        // Power multiplier for a cast released after `drawSeconds` of drawing.
        // Linear from MinPower at 0 s up to MaxPower at FullChargeSeconds, then flat
        // at MaxPower until the charge disperses.
        public static float PowerMult(float drawSeconds)
        {
            float t = drawSeconds <= 0f ? 0f
                    : drawSeconds >= FullChargeSeconds ? 1f
                    : drawSeconds / FullChargeSeconds;
            return MinPower + (MaxPower - MinPower) * t;
        }

        // True once the draw has reached full strength (used to announce "fully
        // charged" and to unlock the charged cone-range / rectangular-wall shapes).
        public static bool IsFullyCharged(float drawSeconds) => drawSeconds >= FullChargeSeconds;

        // 0 at an instant release, 1 at full charge — the normalised charge level,
        // independent of MinPower. Effects use it to grow a cone's reach or turn a
        // wall from a thin line into a filled rectangle as the draw deepens.
        public static float ChargeFraction(float power)
        {
            float f = (power - MinPower) / (MaxPower - MinPower);
            return f < 0f ? 0f : f > 1f ? 1f : f;
        }

        // ── Charged cone reach ───────────────────────────────────────────────────
        // A cone thrown instantly barely leaves the hand; a fully-drawn one lances
        // far further. Reach scales from 1.0× (instant) to ConeRangeChargedMult× (full).
        public const float ConeRangeChargedMult = 1.6f;
        public static float ConeRange(float baseRange, float power)
            => baseRange * (1f + (ConeRangeChargedMult - 1f) * ChargeFraction(power));

        // ── Charged wall depth ───────────────────────────────────────────────────
        // A wall thrown weakly is a single thin curtain; drawn to full it thickens
        // into a filled rectangle this many rows deep.
        public const int WallMaxDepthRows = 4;
        public static int WallDepthRows(float power)
            => 1 + (int)Math.Round((WallMaxDepthRows - 1) * ChargeFraction(power), MidpointRounding.AwayFromZero);

        // ── Aging cost (days) — FLAT, independent of draw time ───────────────────
        public const int   AttackCostDays = 3;     // a released attack ages you this much
        public const int   WallCostDays   = 4;     // a wall is a slightly bigger working
        public const int   MinCastDays    = 1;     // a cast is never free
        public const float NatureCostMult = 0.5f;  // the Nature discipline's flat discount

        // Days of aging a cast costs. The draw length no longer matters — only the
        // form and whether the caster knows Nature. Floored at 1.
        public static int CastAgingDays(CastForm form, bool hasNature)
        {
            int baseDays = form == CastForm.Wall ? WallCostDays : AttackCostDays;
            if (hasNature) baseDays = (int)Math.Round(baseDays * NatureCostMult, MidpointRounding.AwayFromZero);
            return Math.Max(MinCastDays, baseDays);
        }

        // ── Blood ───────────────────────────────────────────────────────────────
        // Executing a lord gives back this many days of youth, scaled by clan tier.
        public const int BloodDaysPerTier = 25;
        public static int BloodRejuvenationDays(int clanTier) => Math.Max(BloodDaysPerTier, clanTier * BloodDaysPerTier);

        // ── Element labels ──────────────────────────────────────────────────────
        public static string ElementName(MagicElement e)
        {
            switch (e)
            {
                case MagicElement.Fire:   return "Fire";
                case MagicElement.Wind:   return "Wind";
                case MagicElement.Earth:  return "Earth";
                case MagicElement.Water:  return "Water";
                default:                  return "Spirit";
            }
        }

        // The Ashen wield the same elements wearing a colder mask.
        public static string AshenElementName(MagicElement e)
        {
            switch (e)
            {
                case MagicElement.Fire:   return "Cold";
                case MagicElement.Wind:   return "Storm";
                case MagicElement.Earth:  return "Ash";
                case MagicElement.Water:  return "Snow";
                default:                  return "Void";
            }
        }
    }
}
