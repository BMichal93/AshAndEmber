// =============================================================================
// ASH AND EMBER — Magic/ElementComboMath.cs
//
// Pure numeric core of ELEMENTAL FUSION — two known elements drawn together in
// one chord blend into a sixth working (or, paired with Spirit, a BATTLE COMMAND
// the caster wills into their own nearby troops). No TaleWorlds types — fully
// testable (PureLogicTests). Runtime behaviour lives in ElementSpellEffects /
// ElementMagicInput.
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
//
// SPIRIT + X → a command, not a working. Spirit is the mind: paired with another
// element it does not throw anything at the foe, it steadies and drives the
// caster's OWN warriors within CommandRadius for CommandDurationSec, each pairing
// with a single, distinct battlefield identity:
//
//   Spirit + Fire  → Onslaught     — offense:    charge, morale surged (fearless)
//   Spirit + Wind  → Quicken       — mobility:   +speed and an ordered advance
//   Spirit + Earth → Steadfast     — resilience: morale locked, they will not rout
//   Spirit + Water → Hold the Line — positioning: halt and lock the line in place
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

        // ── Battle-command tuning (Spirit pairs) ────────────────────────────────
        // A command reaches every friendly agent within this radius of the caster
        // at the instant of the cast (like Spirit's own nova and Zephyrglass'
        // haste), then rides its own timer regardless of where they move after —
        // so a mage rallies the ranks around them, not a distant army.
        public const float CommandRadius      = 18f;   // metres
        public const float CommandDurationSec = 12f;   // how long the command holds
        public const float QuickenSpeedMult   = 1.30f; // Quicken: +30 % move speed
        public const float OnslaughtMorale    = 90f;   // fearless fury
        public const float SteadfastMorale    = 80f;   // will not break
        public const float HoldMorale         = 55f;   // merely steady (identity is the halt, not the nerve)

        public static bool IsFusion(MagicElement e)
            => e >= MagicElement.Lightning && e <= MagicElement.Mire;

        // A Spirit pairing — a command to the caster's own troops, not a working
        // thrown at the foe.
        public static bool IsCommand(MagicElement e)
            => e >= MagicElement.CommandCharge && e <= MagicElement.CommandHold;

        public static bool IsBase(MagicElement e) => e <= MagicElement.Spirit;

        // The base element a command borrows its colour/flavour from (the non-
        // Spirit half of the pair). Drives the cast light and load visual.
        public static MagicElement CommandBaseElement(MagicElement e)
        {
            switch (e)
            {
                case MagicElement.CommandCharge:    return MagicElement.Fire;
                case MagicElement.CommandQuicken:   return MagicElement.Wind;
                case MagicElement.CommandSteadfast: return MagicElement.Earth;
                default:                            return MagicElement.Water;  // CommandHold
            }
        }

        // The morale floor a command holds its warriors at for its duration
        // (re-asserted each tick). 0 = the command does not touch morale.
        public static float CommandMoraleFloor(MagicElement e)
        {
            switch (e)
            {
                case MagicElement.CommandCharge:    return OnslaughtMorale;
                case MagicElement.CommandSteadfast: return SteadfastMorale;
                case MagicElement.CommandHold:      return HoldMorale;
                default:                            return 0f;                  // CommandQuicken — pure mobility
            }
        }

        // The movement-speed multiplier a command grants (1 = unchanged).
        public static float CommandSpeedMult(MagicElement e)
            => e == MagicElement.CommandQuicken ? QuickenSpeedMult : 1f;

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
                        case MagicElement.Spirit: return MagicElement.CommandCharge;
                    }
                    break;
                case MagicElement.Wind:
                    switch (b)
                    {
                        case MagicElement.Earth:  return MagicElement.Sandstorm;
                        case MagicElement.Water:  return MagicElement.Ice;
                        case MagicElement.Spirit: return MagicElement.CommandQuicken;
                    }
                    break;
                case MagicElement.Earth:
                    switch (b)
                    {
                        case MagicElement.Water:  return MagicElement.Mire;
                        case MagicElement.Spirit: return MagicElement.CommandSteadfast;
                    }
                    break;
                case MagicElement.Water:
                    if (b == MagicElement.Spirit) return MagicElement.CommandHold;
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
                case MagicElement.Lightning:        return "Lightning";
                case MagicElement.Fog:              return "Fog";
                case MagicElement.Magma:            return "Magma";
                case MagicElement.Ice:              return "Ice";
                case MagicElement.Sandstorm:        return "Sandstorm";
                case MagicElement.Mire:             return "Mire";
                case MagicElement.CommandCharge:    return "Onslaught";
                case MagicElement.CommandQuicken:   return "Quicken";
                case MagicElement.CommandSteadfast: return "Steadfast";
                case MagicElement.CommandHold:      return "Hold the Line";
                default:                            return "Working";
            }
        }

        // The Ashen cold wears these workings under a colder mask too.
        public static string AshenElementName(MagicElement e)
        {
            switch (e)
            {
                case MagicElement.Lightning:        return "Deathbolt";
                case MagicElement.Fog:              return "The Shroud";
                case MagicElement.Magma:            return "Ashfall";
                case MagicElement.Ice:              return "Rime";
                case MagicElement.Sandstorm:        return "Ashstorm";
                case MagicElement.Mire:             return "The Sinking Ash";
                case MagicElement.CommandCharge:    return "The Cold Advance";
                case MagicElement.CommandQuicken:   return "The Driven Dead";
                case MagicElement.CommandSteadfast: return "The Unyielding";
                case MagicElement.CommandHold:      return "The Frozen Line";
                default:                            return "Working";
            }
        }
    }
}
