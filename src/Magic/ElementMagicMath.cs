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
// Casting: hold focus, stand still to DRAW a charge for ~3 s (the nature limit),
// then Attack or Block releases it. The longer you draw (up to 7 s) the LESS the
// working ages you — gently — and the Harmony talent makes that patience pay far
// more. Aging "burns through" exactly like the old fire magic.
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
        // ── Draw / charge ───────────────────────────────────────────────────────
        public const float MinDrawSeconds  = 3f;   // must draw at least this long to release
        public const float FullDrawSeconds = 7f;   // draw benefit (and Harmony) maxes out here

        // ── Aging cost (days) ───────────────────────────────────────────────────
        public const int   AttackBaseDays = 4;     // a released attack ages you this much at the 3 s minimum
        public const int   WallBaseDays   = 6;     // a wall is a bigger working
        public const int   MinCastDays    = 1;     // a cast is never free
        public const float DrawDiscountPerSec       = 0.5f; // days shaved per second drawn past the minimum
        public const float NatureDrawDiscountPerSec = 1.5f; // …with the Nature attunement (the patient draw)

        // Days of aging a cast costs, given the form, how long it was drawn, and
        // whether the caster knows Nature. Drawing longer is cheaper; floored at 1.
        public static int CastAgingDays(CastForm form, float drawSeconds, bool hasNature)
        {
            int baseDays = form == CastForm.Wall ? WallBaseDays : AttackBaseDays;
            float over   = Math.Max(0f, Math.Min(drawSeconds, FullDrawSeconds) - MinDrawSeconds);
            float perSec = hasNature ? NatureDrawDiscountPerSec : DrawDiscountPerSec;
            int cost     = (int)Math.Round(baseDays - over * perSec, MidpointRounding.AwayFromZero);
            return Math.Max(MinCastDays, cost);
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
