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
    // v0.37 — FUSIONS: two known elements drawn together in one chord (see
    // ElementComboMath) blend into a sixth working, or (with Spirit) call a
    // living kinsman to the caster's side. APPENDED, never renumbered — nothing
    // here is serialized, but keep the numbering stable for sanity's sake.
    public enum MagicElement
    {
        Fire = 0, Wind = 1, Earth = 2, Water = 3, Spirit = 4,
        // ── Fusions (attack-only workings) ──────────────────────────────────
        Lightning = 5,   // Fire + Wind
        Fog       = 6,   // Fire + Water
        Magma     = 7,   // Fire + Earth
        Ice       = 8,   // Wind + Water
        Sandstorm = 9,   // Wind + Earth
        Mire      = 10,  // Earth + Water
        // ── Fusions with Spirit — summon a living kinsman of the paired element ──
        SummonFlame = 11,  // Fire  + Spirit
        SummonGale  = 12,  // Wind  + Spirit
        SummonStone = 13,  // Earth + Spirit
        SummonTide  = 14,  // Water + Spirit
    }

    // Which half of an element's expression is being cast.
    public enum CastForm { Attack, Wall }

    public static class ElementMagicMath
    {
        // ── Draw / charge → POWER ────────────────────────────────────────────────
        // No minimum: you may release instantly. The draw sets power, from a weak
        // instant cast up to FULL strength at FullChargeSeconds — then it holds at
        // full through a grace window, and a caster who keeps pouring reaches the
        // OVERCHANNEL at OverchannelSeconds: the working strikes twice as hard.
        // Held past MaxDrawSeconds, the charge finally DISPERSES and is lost.
        public const float FullChargeSeconds  = 5f;    // power reaches its peak here
        public const float OverchannelSeconds = 10f;   // keep drawing and the working overchannels
        public const float MaxDrawSeconds     = 15f;   // held this long, the charge disperses
        public const float MinPower           = 0.5f;  // strength of an instant (0 s) cast
        public const float MaxPower           = 1.0f;  // strength once fully charged
        public const float OverchannelMult    = 2.0f;  // an overchannelled working strikes this much harder

        // Power multiplier for a cast released after `drawSeconds` of drawing.
        // Linear from MinPower at 0 s up to MaxPower at FullChargeSeconds; holds at
        // MaxPower through the grace window; then jumps to the overchannel (MaxPower
        // × OverchannelMult) once OverchannelSeconds is reached.
        public static float PowerMult(float drawSeconds)
        {
            if (drawSeconds >= OverchannelSeconds) return MaxPower * OverchannelMult;
            float t = drawSeconds <= 0f ? 0f
                    : drawSeconds >= FullChargeSeconds ? 1f
                    : drawSeconds / FullChargeSeconds;
            return MinPower + (MaxPower - MinPower) * t;
        }

        // True once the draw has reached full strength (used to announce "fully
        // charged" and to unlock the charged cone-range / rectangular-wall shapes).
        public static bool IsFullyCharged(float drawSeconds) => drawSeconds >= FullChargeSeconds;

        // True once the draw has been held into the overchannel — the doubled working.
        public static bool IsOverchannelled(float drawSeconds) => drawSeconds >= OverchannelSeconds;

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

        // ── Ignition — a deep draw sets its marks ALIGHT ─────────────────────────
        // Fire is the bruiser, and the full draw must cross the kill threshold: a
        // fully-charged cone leaves every struck foe BURNING (the Ashen cold clings
        // as deep frost — same toll, colder face). The burn scales with the charge,
        // so a snap flick ignites nothing and spam gains nothing: at full draw the
        // cone's 44 plus the burn's 18/s × 5 s (= 90) finishes an unarmoured man
        // over the seconds that follow, and leaves an armoured one crippled. The
        // burn is element-typed (see ElementSpellEffects.Tick), so it BITES the
        // fire-weak Kindled — a Frost-Born keeps taking ×2.2 fire as it melts.
        public const float IgniteMaxDps  = 18f;
        public const float IgniteSeconds = 5f;
        public static float IgniteDps(float power) => IgniteMaxDps * ChargeFraction(power);

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
