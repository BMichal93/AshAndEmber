// =============================================================================
// ASH AND EMBER — Magic/ElementUltimateMath.cs
//
// Pure numeric core of THE UNBINDING (the Ashen call it the UNMAKING) — each
// element's once-per-battle ultimate working. No TaleWorlds types — fully
// testable (PureLogicTests). Runtime behaviour lives in ElementUltimates.cs.
//
// The ultimate is released with the CHORD: while focusing, press Attack and
// Block TOGETHER (a single press is buffered for ChordWindowSeconds so the
// second button of a chord can land before the single-form cast commits).
// It only answers a FULL draw (ElementMagicMath.FullChargeSeconds) — the
// element must be drawn to its fullest before it can be unbound.
//
// The five Unbindings (living face / Ashen face):
//   Fire   — The First Flame Remembered / The Long Winter   — a nova centred
//            on the caster: heavy damage, everything burns, horses bolt.
//   Wind   — On the Wings of the Gale / Carried by the Howl — FLIGHT: the wind
//            bears the caster aloft; ANY hit knocks the wind out and you fall.
//   Earth  — Heart of the Mountain / The Cairn-Shell        — a stone mantle:
//            most damage is shrugged off, but you move slower.
//   Water  — The Weeping Sky / The White Silence            — rain over a wide
//            stretch of field: burns quenched, fire halved, horses mired,
//            bowstrings soaked. There is only ONE sky — a newer working
//            replaces a standing one.
//   Spirit — The Land's Answer / What Sleeps Beneath        — the ground sends
//            a champion: one elemental, shaped by the terrain, fights at the
//            caster's side and then comes apart into the land it rose from.
// =============================================================================

using System;

namespace AshAndEmber
{
    // What the land answers with when Spirit calls (chosen from the scene).
    public enum ElementalKind { Stone = 0, Frost = 1, Sand = 2 }

    public static class ElementUltimateMath
    {
        // ── Input — the chord ────────────────────────────────────────────────────
        // A lone Attack/Block press waits this long for its partner before it
        // commits as a normal cone/wall. Short enough to be imperceptible in
        // combat, long enough for a deliberate two-finger press to register.
        public const float ChordWindowSeconds = 0.25f;

        // ── Cost (days of life expectancy) — FLAT, like every other cast ────────
        // Steep: three walls' worth. The Nature discipline halves it as usual;
        // the Ashen pay it in criminal standing (days × 5, same as other casts).
        public const int UltimateCostDays = 12;

        public static int UltimateAgingDays(bool hasNature)
        {
            int days = UltimateCostDays;
            if (hasNature) days = (int)Math.Round(days * ElementMagicMath.NatureCostMult,
                                                  MidpointRounding.AwayFromZero);
            return Math.Max(ElementMagicMath.MinCastDays, days);
        }

        // The Unbinding only answers a FULL draw — the same threshold that
        // announces "fully charged" (7 s). Released earlier, the element resists.
        public static bool CanUnbind(float drawSeconds)
            => ElementMagicMath.IsFullyCharged(drawSeconds);

        // ── Fire — the nova ──────────────────────────────────────────────────────
        public const float NovaRadius        = 10f;   // metres around the caster
        public const float NovaDamage        = 50f;   // per struck foe (cone caps at 44)
        public const float NovaSiegeDamage   = 200f;  // vs wooden machines/gates in the ring
        public const float NovaRingRadius    = 7f;    // where the burning ring settles
        public const float NovaRingBurnDps   = 10f;   // per-second burn on the ring
        public const float NovaRingBurnSec   = 6f;    // how long the ring smoulders
        public const float NovaHorseBolt     = 3f;    // metres a panicked mount is thrown
        public const float NovaAshenSlowMult = 0.5f;  // the cold's deep-frost slow…
        public const float NovaAshenSlowSec  = 4f;    // …and how long it grips

        // ── Wind — flight ────────────────────────────────────────────────────────
        public const float FlightSeconds        = 12f;  // how long the wind bears you
        public const float FlightSpeed          = 9f;   // metres per second, where you look
        public const float FlightHeight         = 3.5f; // metres above the ground
        public const float FlightLandingSeconds = 2.5f; // the final gentle descent
        // NPC lords cannot PILOT free flight — theirs is a wind-LEAP: a short
        // straight glide out of an encirclement (same magic, fixed direction).
        public const float NpcLeapSeconds = 3f;
        public const float NpcLeapSpeed   = 10f;

        // Height above ground for a flyer with `remaining` seconds left: full
        // height through the flight, easing down to a step-off in the final
        // FlightLandingSeconds so a natural landing deals no falling damage.
        // (Being HIT removes the flyer from the tick entirely — that fall is
        // real, and so is its damage.)
        public static float FlightHeightAt(float remainingSeconds)
        {
            if (remainingSeconds <= 0f) return 0f;
            if (remainingSeconds >= FlightLandingSeconds) return FlightHeight;
            return FlightHeight * (remainingSeconds / FlightLandingSeconds);
        }

        // ── Earth — the stone mantle ─────────────────────────────────────────────
        public const float MantleSeconds         = 25f;
        public const float MantleDamageReduction = 0.75f; // fraction of every blow shrugged off
        public const float MantleSpeedMult       = 0.75f; // made of mountain — a quarter slower

        // The portion of a blow the mantled caster actually keeps.
        public static float MantleKeptDamage(float damage)
            => damage <= 0f ? 0f : damage * (1f - MantleDamageReduction);

        // ── Water — the weeping sky ──────────────────────────────────────────────
        public const float RainRadius        = 35f;   // metres — a wide stretch of field
        public const float RainSeconds       = 90f;
        public const float RainTickSeconds   = 1f;    // how often the zone re-applies itself
        public const float RainFireDamp      = 0.5f;  // fire magic works at half strength inside
        public const float RainMountSlowMult = 0.6f;  // horses mire worst
        public const float RainFootSlowMult  = 0.9f;  // men only slog
        public const float RainArcheryDamp   = 0.4f;  // fraction of ranged damage the wet strings lose
        public const float RainAshenMoraleDrainPerTick = 0.8f; // the White Silence gnaws at foes

        // ── Spirit — the elemental ───────────────────────────────────────────────
        public const float ElementalSeconds = 60f;
        public const float ElementalHealth  = 350f;  // towering — several men's worth
        public const float ElementalSpawnOffset = 2.5f;

        // Which champion the land sends, from what the scene shows. Snow always
        // wins (a snowed-over desert is still winter's ground); then sand by the
        // scene's name; STONE is the default answer everywhere else. Pure —
        // the runtime passes SceneIsSnowy() and the lower-cased scene name in.
        public static ElementalKind ElementalKindForScene(bool snowy, string sceneNameLower)
        {
            if (snowy) return ElementalKind.Frost;
            string s = sceneNameLower ?? string.Empty;
            if (s.Contains("desert") || s.Contains("dune") || s.Contains("sand") || s.Contains("aserai"))
                return ElementalKind.Sand;
            return ElementalKind.Stone;
        }

        public static string ElementalName(ElementalKind kind)
        {
            switch (kind)
            {
                case ElementalKind.Frost: return "Frost-Born";
                case ElementalKind.Sand:  return "Sand-Born";
                default:                  return "Stone-Born";
            }
        }

        // ── NPC use — the Unbinding done TO you ─────────────────────────────────
        // A lord unbinds once per battle, only in battles worth the working, and
        // always behind a LONG telegraphed windup: staggering him during it
        // breaks the working (and still burns his once-per-battle).
        public const float NpcWindupSeconds   = 4.5f;
        public const int   NpcMinCombatants   = 70;   // no village skirmish eats an ultimate
        public const int   NovaCloseEnemies   = 4;    // swarmed → the nova answers
        public const float MantleHpFrac       = 0.45f;// wounded and pressed → stone
        public const float LeapHpFrac         = 0.35f;// desperate → the wind carries him out
        public const int   LeapCloseEnemies   = 3;
        public const int   RainMountedNear    = 4;    // a cavalry wedge → the sky weeps

        // ── Names ────────────────────────────────────────────────────────────────
        public static string UltimateName(MagicElement el, bool ashen)
        {
            if (ashen)
            {
                switch (el)
                {
                    case MagicElement.Fire:   return "The Long Winter";
                    case MagicElement.Wind:   return "Carried by the Howl";
                    case MagicElement.Earth:  return "The Cairn-Shell";
                    case MagicElement.Water:  return "The White Silence";
                    default:                  return "What Sleeps Beneath";
                }
            }
            switch (el)
            {
                case MagicElement.Fire:   return "The First Flame Remembered";
                case MagicElement.Wind:   return "On the Wings of the Gale";
                case MagicElement.Earth:  return "Heart of the Mountain";
                case MagicElement.Water:  return "The Weeping Sky";
                default:                  return "The Land's Answer";
            }
        }
    }
}
