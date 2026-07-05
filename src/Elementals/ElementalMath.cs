// =============================================================================
// ASH AND EMBER — Elementals/ElementalMath.cs
//
// Pure numeric core of THE KINDLED — the elemental beings born where raw magic
// pools too thick and wakes into a body of fire, water, stone or storm. No
// TaleWorlds types (fully covered by PureLogicTests). Runtime behaviour —
// spawning, the following aura, the on-hit weakness — lives in
// ElementalBeings.cs / ElementalFactory.cs.
//
// THE WEAKNESS WHEEL (what the old myths knew):
//   Fire is drowned by Water · Water is drunk by Earth · Earth is worn by Wind ·
//   Wind is burned by Fire. Ice is a special case — it only ever fears Fire.
// A being SHRUGS OFF its own element (it is made of the thing) and BUCKLES to
// the element that unmakes it. Weapons matter too: stone and ice SHATTER under
// blunt force but turn a blade; flame and tide let steel pass half-harmless
// through them and must be unmade by magic instead.
//
// The ElementalKind enum itself lives in ElementUltimateMath.cs (shared with the
// Spirit Unbinding's terrain champion, which is just a Kindled the land sends).
// =============================================================================

using System;

namespace AshAndEmber
{
    // How a weapon bit — mapped at runtime from TaleWorlds' DamageTypes so this
    // stays pure. (Invalid/unknown weapon hits are treated as Cut.)
    public enum PhysicalHit { Cut = 0, Pierce = 1, Blunt = 2 }

    public static class ElementalMath
    {
        // ── Identity ─────────────────────────────────────────────────────────────
        // Which of the five castable elements a being is MADE of. Frost and Sand
        // are cousins of Water and Earth for the wheel below.
        public static MagicElement ElementOf(ElementalKind kind)
        {
            switch (kind)
            {
                case ElementalKind.Flame: return MagicElement.Fire;
                case ElementalKind.Tide:  return MagicElement.Water;
                case ElementalKind.Frost: return MagicElement.Water;
                case ElementalKind.Gale:  return MagicElement.Wind;
                case ElementalKind.Sand:  return MagicElement.Earth;
                default:                  return MagicElement.Earth;   // Stone
            }
        }

        // ── Magical weakness ─────────────────────────────────────────────────────
        // Multiplier on incoming MAGIC damage of `attack` element against a being
        // of `kind`. >1 = it buckles, <1 = it shrugs the blow off.
        public const float ResistOwnElement = 0.35f;   // made of the thing
        public const float WeakToCounter    = 1.90f;   // the element that unmakes it
        public const float FrostMeltsToFire = 2.20f;   // ice fears fire most of all

        public static float ElementDamageMultiplier(ElementalKind kind, MagicElement attack)
        {
            // Ice is the special case — melts to fire, otherwise water-tough.
            if (kind == ElementalKind.Frost)
            {
                if (attack == MagicElement.Fire)  return FrostMeltsToFire;
                if (attack == MagicElement.Water) return ResistOwnElement;
                return 1f;
            }

            MagicElement made = ElementOf(kind);
            if (attack == made)              return ResistOwnElement;
            if (attack == CounterOf(made))   return WeakToCounter;
            return 1f;
        }

        // The element that unmakes `made` on the wheel.
        public static MagicElement CounterOf(MagicElement made)
        {
            switch (made)
            {
                case MagicElement.Fire:  return MagicElement.Water; // drowned
                case MagicElement.Water: return MagicElement.Earth; // drunk up
                case MagicElement.Earth: return MagicElement.Wind;  // worn away
                case MagicElement.Wind:  return MagicElement.Fire;  // burned off
                default:                 return MagicElement.Fire;  // Spirit → n/a
            }
        }

        // ── Physical weakness ────────────────────────────────────────────────────
        // Multiplier on incoming WEAPON damage. Stone/ice shatter under blunt and
        // turn blades; flame/tide/storm let steel pass mostly through and must be
        // unmade by magic. >1 amplifies (applied as a bonus hit); <1 is shrugged
        // off (healed back — the same pattern the Nature-resist barrier uses).
        public static float PhysicalDamageMultiplier(ElementalKind kind, PhysicalHit hit)
        {
            switch (kind)
            {
                case ElementalKind.Stone:
                case ElementalKind.Sand:
                    return hit == PhysicalHit.Blunt ? 1.70f
                         : hit == PhysicalHit.Cut   ? 0.50f
                         :                             0.60f;   // Pierce
                case ElementalKind.Frost:
                    return hit == PhysicalHit.Blunt ? 1.70f
                         : hit == PhysicalHit.Cut   ? 0.80f
                         :                             0.90f;
                case ElementalKind.Flame:
                    // Steel passes half-harmless through living flame.
                    return hit == PhysicalHit.Blunt ? 0.70f : 0.55f;
                case ElementalKind.Tide:
                    // Blades split water and close again.
                    return hit == PhysicalHit.Blunt ? 0.90f : 0.60f;
                case ElementalKind.Gale:
                    // Nothing to grip — a body of moving air.
                    return 0.65f;
                default:
                    return 1f;
            }
        }

        // ── Bodily toughness ─────────────────────────────────────────────────────
        // Worth several men — but the airy ones are frailer than the earthen.
        public static float Health(ElementalKind kind)
        {
            switch (kind)
            {
                case ElementalKind.Stone: return 440f;
                case ElementalKind.Sand:  return 340f;
                case ElementalKind.Frost: return 360f;
                case ElementalKind.Flame: return 320f;
                case ElementalKind.Tide:  return 350f;
                case ElementalKind.Gale:  return 270f;
                default:                  return 350f;
            }
        }

        // ── The following aura ───────────────────────────────────────────────────
        // A short-lived particle is re-emitted at the being's feet this often so
        // the elemental "coat" clings to it as it moves. Cheap enough to run on
        // many bodies at once; the runtime LODs distant ones on top of this.
        public const float AuraIntervalSeconds = 0.35f;
        public const float AuraLodMetres       = 40f;   // beyond this, emit half as often

        // ── The Kindled's attack (it fights with its element, not a weapon) ──────
        // On this cooldown, a Kindled looses a small cone of its OWN element at a
        // foe within reach and roughly ahead. Weaker than a mage's drawn cast (it
        // is instinct, not a working) but relentless — a whole band is a storm of it.
        public const float AttackCooldownSeconds = 4.5f;
        public const float AttackCooldownJitter  = 2.0f;   // ± spread so a band does not volley in lockstep
        public const float AttackRangeMetres     = 11f;    // must have a foe this close to bother loosing
        public const float AttackPower           = 0.55f;  // small: just above an instant flick

        // ── Wild spawns (roaming the remote wilds) ──────────────────────────────
        // What the land breeds where magic pools. Keyed off a lower-cased biome /
        // terrain hint (Bannerlord's face-terrain names, or a settlement culture).
        // Pure — the behaviour passes the string in.
        public static ElementalKind WildKindForBiome(string biomeLower)
        {
            string s = biomeLower ?? string.Empty;
            if (s.Contains("snow") || s.Contains("ice") || s.Contains("tundra"))          return ElementalKind.Frost;
            if (s.Contains("desert") || s.Contains("dune") || s.Contains("sand"))         return ElementalKind.Sand;
            if (s.Contains("forest") || s.Contains("swamp") || s.Contains("water") ||
                s.Contains("lake") || s.Contains("river"))                                return ElementalKind.Tide;
            if (s.Contains("steppe") || s.Contains("plain") || s.Contains("field"))       return ElementalKind.Gale;
            if (s.Contains("mountain") || s.Contains("rock") || s.Contains("canyon"))     return ElementalKind.Stone;
            return ElementalKind.Stone;   // the world's default answer
        }

        // Roaming-party sizing. A band must be big enough to persist and roam
        // (the engine culls near-empty parties and they never travel far enough
        // to be found), so it carries lesser thralls alongside the true Kindled.
        public const int   WildPartyMinBodies = 14;
        public const int   WildPartyMaxBodies = 22;
        // …but only this many of a band's bodies wake into full elementals in a
        // fight — the rest march as ordinary thralls. Keeps the battle dangerous
        // without fielding twenty 400-HP beings at once.
        public const int   MaxConvertedPerBattle = 8;
        // Per daily roll: how likely raw magic wakes a new band somewhere.
        public const float WildDailySpawnChance = 0.10f;
        // Never let the world drown in them.
        public const int   WildMaxLivingBands   = 8;

        // ── Battle reinforcement (The Kindling) ─────────────────────────────────
        public const float KindlingBattleChance = 0.03f;   // per battle, like other events
        public const int   KindlingBodies        = 3;
    }
}
