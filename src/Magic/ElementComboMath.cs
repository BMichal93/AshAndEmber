// =============================================================================
// ASH AND EMBER — Magic/ElementComboMath.cs
//
// Pure numeric core of ELEMENTAL FUSION — two known elements drawn together in
// one chord blend into a sixth working (or, paired with Spirit, call a living
// kinsman to the caster's side). No TaleWorlds types — fully testable
// (PureLogicTests). Runtime behaviour lives in ElementSpellEffects /
// ElementMagicInput / ElementUltimates.
//
// THE CHORD: while focusing, the five element keys (W/S/A/D and Fire's own —
// see ElementMagicInput) are peers. A lone press waits ComboChordWindowSeconds
// for a second, different key to land beside it — mirroring the Attack+Block
// Unbinding chord already in ElementUltimateMath. Two DIFFERENT elements inside
// the window fuse; the window closing on just one resolves as a normal single
// load, exactly as before fusion existed.
//
// Every pair among the five blends — there is no "does not mix" case, so
// TryFuse never actually returns null for two distinct base elements. The
// lookup still returns null defensively (treated as "fall back to a single
// element") in case that ever changes.
//
//   Fire + Wind    → Lightning    Fire + Water  → Fog       Fire + Earth → Magma
//   Wind + Water   → Ice          Wind + Earth  → Sandstorm Earth + Water → Mire
//   Spirit + X     → Summon the living kinsman of X (Flame / Gale / Stone / Tide)
// =============================================================================

using System;

namespace AshAndEmber
{
    public static class ElementComboMath
    {
        // A lone element-key press waits this long for a second, different key
        // before it commits as a normal single load. Short enough to feel like
        // one gesture, long enough for a deliberate two-finger chord to land.
        public const float ComboChordWindowSeconds = 0.3f;

        public static bool IsFusion(MagicElement e)
            => e >= MagicElement.Lightning && e <= MagicElement.Mire;

        public static bool IsSummon(MagicElement e)
            => e >= MagicElement.SummonFlame && e <= MagicElement.SummonTide;

        public static bool IsBase(MagicElement e) => e <= MagicElement.Spirit;

        // The living kinsman a Spirit-fusion calls.
        public static ElementalKind SummonKindOf(MagicElement e)
        {
            switch (e)
            {
                case MagicElement.SummonFlame: return ElementalKind.Flame;
                case MagicElement.SummonGale:  return ElementalKind.Gale;
                case MagicElement.SummonStone: return ElementalKind.Stone;
                default:                       return ElementalKind.Tide;   // SummonTide
            }
        }

        // Order-independent fuse lookup — only ever called with two DISTINCT
        // base elements (the input layer cannot chord a fusion with anything
        // else). Null when the pair does not blend, or when either half is
        // not itself a base element.
        public static MagicElement? TryFuse(MagicElement a, MagicElement b)
        {
            if (a == b || !IsBase(a) || !IsBase(b)) return null;
            if (a > b) { var t = a; a = b; b = t; }   // normalise: a is always the lower value

            switch (a)
            {
                case MagicElement.Fire:
                    switch (b)
                    {
                        case MagicElement.Wind:   return MagicElement.Lightning;
                        case MagicElement.Earth:  return MagicElement.Magma;
                        case MagicElement.Water:  return MagicElement.Fog;
                        case MagicElement.Spirit: return MagicElement.SummonFlame;
                    }
                    break;
                case MagicElement.Wind:
                    switch (b)
                    {
                        case MagicElement.Earth:  return MagicElement.Sandstorm;
                        case MagicElement.Water:  return MagicElement.Ice;
                        case MagicElement.Spirit: return MagicElement.SummonGale;
                    }
                    break;
                case MagicElement.Earth:
                    switch (b)
                    {
                        case MagicElement.Water:  return MagicElement.Mire;
                        case MagicElement.Spirit: return MagicElement.SummonStone;
                    }
                    break;
                case MagicElement.Water:
                    if (b == MagicElement.Spirit) return MagicElement.SummonTide;
                    break;
            }
            return null;
        }

        // Which parent element's WALL a fusion borrows when Block is released.
        // Lightning raises its own (Storm's Stormwall — see ElementSpellEffects),
        // so it is not listed here.
        public static MagicElement WallFallback(MagicElement fusion)
        {
            switch (fusion)
            {
                case MagicElement.Fog:       return MagicElement.Water;   // a standing mist
                case MagicElement.Ice:       return MagicElement.Water;   // the cold mirror of the mist
                case MagicElement.Magma:     return MagicElement.Fire;    // the wall still burns
                case MagicElement.Sandstorm: return MagicElement.Wind;    // a driving wall of grit
                case MagicElement.Mire:      return MagicElement.Earth;   // the roots still hold the line
                default:                     return MagicElement.Fire;
            }
        }

        // ── Names ────────────────────────────────────────────────────────────
        public static string ElementName(MagicElement e)
        {
            switch (e)
            {
                case MagicElement.Lightning: return "Lightning";
                case MagicElement.Fog:       return "Fog";
                case MagicElement.Magma:     return "Magma";
                case MagicElement.Ice:       return "Ice";
                case MagicElement.Sandstorm: return "Sandstorm";
                case MagicElement.Mire:      return "Mire";
                default:                     return ElementUltimateMath.ElementalName(SummonKindOf(e));
            }
        }

        // The Ashen cold wears these fusions under a colder mask too.
        public static string AshenElementName(MagicElement e)
        {
            switch (e)
            {
                case MagicElement.Lightning: return "Deathbolt";
                case MagicElement.Fog:       return "The Shroud";
                case MagicElement.Magma:     return "Ashfall";
                case MagicElement.Ice:       return "Rime";
                case MagicElement.Sandstorm: return "Ashstorm";
                case MagicElement.Mire:      return "The Sinking Ash";
                // The kinsman answers a caller the same, whichever mask calls it.
                default:                     return ElementUltimateMath.ElementalName(SummonKindOf(e));
            }
        }
    }
}
