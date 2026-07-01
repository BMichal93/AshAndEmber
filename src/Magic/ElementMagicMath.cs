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
        // instant cast up to full strength at the cap; holding to the cap disperses.
        public const float MaxDrawSeconds = 10f;   // power cap AND the disperse threshold
        public const float MinPower       = 0.35f; // strength of an instant (0 s) cast
        public const float MaxPower       = 1.0f;  // strength at a full 10 s draw

        // Power multiplier for a cast released after `drawSeconds` of drawing.
        // Linear from MinPower at 0 s to MaxPower at the cap; clamped past the cap.
        public static float PowerMult(float drawSeconds)
        {
            float t = drawSeconds <= 0f ? 0f
                    : drawSeconds >= MaxDrawSeconds ? 1f
                    : drawSeconds / MaxDrawSeconds;
            return MinPower + (MaxPower - MinPower) * t;
        }

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
