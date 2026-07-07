// =============================================================================
// ASH AND EMBER — Magic/ElementResonanceMath.cs
//
// Pure numeric core of the RESONANCE DRAW — the campaign-map minigame that
// replaced the old memory-rite for the unified element workings (fire's own
// rite is now this too; only Miracles kept the word-recall prayer).
//
// The charge sweeps a track (0 → 1 → 0 …, a triangle wave) at an element-tuned
// speed. The caster looses it with a keypress; how close the sweep sits to the
// element's own "true" point when released sets the power tier. Fast/volatile
// elements (Fire, Spirit) sweep quickly with a narrow perfect band — a real bid
// for 1.50× that punishes hesitation. Slow/patient elements (Earth, Water)
// sweep gently with a wide, forgiving band. No TaleWorlds types — fully
// testable (PureLogicTests).
// =============================================================================

using System;

namespace AshAndEmber
{
    public static class ElementResonanceMath
    {
        // Full sweep cycles per second (0 → 1 → 0 is one cycle).
        public static float SweepSpeed(MagicElement el)
        {
            switch (el)
            {
                case MagicElement.Spirit: return 0.68f;
                case MagicElement.Fire:   return 0.58f;
                case MagicElement.Wind:   return 0.50f;
                case MagicElement.Water:  return 0.38f;
                case MagicElement.Earth:  return 0.28f;
                default:                  return 0.5f;
            }
        }

        // Where along the track (0..1) the element's true point sits.
        public static float ZoneCenter(MagicElement el)
        {
            switch (el)
            {
                case MagicElement.Fire:   return 0.90f; // released at the fire's height
                case MagicElement.Spirit: return 0.95f; // the nova forgives least of all
                case MagicElement.Wind:   return 0.50f; // the crest of the gust
                case MagicElement.Water:  return 0.45f; // the calm under the tide
                case MagicElement.Earth:  return 0.50f; // once the roots have settled
                default:                  return 0.5f;
            }
        }

        // Half-width (0..1 fraction of the track) of the PERFECT band — 1.50×.
        public static float PerfectHalfWidth(MagicElement el)
        {
            switch (el)
            {
                case MagicElement.Earth:  return 0.16f;
                case MagicElement.Water:  return 0.13f;
                case MagicElement.Wind:   return 0.10f;
                case MagicElement.Fire:   return 0.07f;
                case MagicElement.Spirit: return 0.05f;
                default:                  return 0.10f;
            }
        }

        // The GOOD band — 1.20× — is wider than the perfect band by this factor.
        public static float GoodHalfWidth(MagicElement el) => PerfectHalfWidth(el) * 2.4f;

        // Beyond the good band but still inside this reach, the draw is merely
        // uneven — 0.80× — rather than a total miss.
        public static float WideHalfWidth(MagicElement el) => PerfectHalfWidth(el) * 4.2f;

        // Triangle wave: 0 → 1 → 0 as phase runs 0 → 1 → 2 → … (any phase, any sign).
        public static float TrianglePosition01(float phase)
        {
            float t = phase % 1f;
            if (t < 0f) t += 1f;
            return t <= 0.5f ? t * 2f : 2f - t * 2f;
        }

        // The power tier for releasing the charge while the sweep sits at
        // sweepPos01 (0..1). Mirrors the four tiers of the old memory-rite so the
        // downstream economy (TalentSystem cost, ElementMapSpells payouts) is
        // unchanged — only how the tier is earned changed.
        public static float ResolveMult(MagicElement el, float sweepPos01)
        {
            float dist = Math.Abs(sweepPos01 - ZoneCenter(el));
            if (dist <= PerfectHalfWidth(el)) return 1.50f;
            if (dist <= GoodHalfWidth(el))    return 1.20f;
            if (dist <= WideHalfWidth(el))    return 0.80f;
            return 0.50f;
        }
    }
}
