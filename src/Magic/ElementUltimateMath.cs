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
//   Earth  — The Mountain's Wrath / The Barrow Wakes        — the Sundering:
//            the ground erupts around the caster, hurling foes back and
//            leaving churned rubble that bogs the field.
//   Water  — The Weeping Sky / The White Silence            — rain over a wide
//            stretch of field: burns quenched, fire halved, horses mired,
//            bowstrings soaked. There is only ONE sky — a newer working
//            replaces a standing one.
//   Spirit — The Bent Knee / The Hollow Oath                — a random
//            will nearby is seized and turns to fight at the caster's side;
//            when the short-lived borrowing runs out it lets go and the foe
//            staggers back to their own line, dazed (the Ashen working
//            leaves a parting frost-bite besides).
// =============================================================================

using System;

namespace AshAndEmber
{
    // What the land answers with when Spirit calls (chosen from the scene), and
    // — from v0.36 — what raw magic condenses into where it pools too thick.
    // Stone/Frost/Sand are the terrain champions the Spirit Unbinding still sends;
    // Flame/Tide/Gale are the "pure" beings born of concentrated magic (the
    // Kindled), roaming the wilds and summoned against you. APPENDED, never
    // renumbered — the enum is not serialized, but old summons pass ints around.
    public enum ElementalKind { Stone = 0, Frost = 1, Sand = 2, Flame = 3, Tide = 4, Gale = 5 }

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
        // Spirit's Unbinding does not spend itself — it turns a living enemy to
        // your side for a short while. That borrowed will still costs more life
        // than a one-moment nova or gale.
        public const int SpiritUltimateCostDays = 22;

        public static int UltimateAgingDays(bool hasNature)
            => UltimateAgingDays(hasNature, MagicElement.Fire);

        public static int UltimateAgingDays(bool hasNature, MagicElement el)
        {
            int days = el == MagicElement.Spirit ? SpiritUltimateCostDays : UltimateCostDays;
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

        // ── Earth — the Sundering (a radial earthquake) ──────────────────────────
        // The ground heaves outward from the caster: heavy damage, every foe thrown
        // off his feet, and a ring of churned rubble left to bog the field.
        public const float QuakeRadius        = 11f;   // metres of heaving ground
        public const float QuakeDamage        = 45f;   // per struck foe (cone caps at 44)
        public const float QuakeKnockback     = 6f;    // metres the ground throws a foe
        public const float QuakeSlowMult      = 0.5f;  // staggered footing…
        public const float QuakeSlowSec       = 4f;    // …while they find their feet
        public const int   QuakeRubblePatches = 5;     // lingering bog-rings left behind
        public const float QuakeRubbleRing    = 6f;    // radius the rubble ring settles at
        public const float QuakeSiegeDamage   = 200f;  // shaken wooden machines/gates, ×power

        // ── Water — the weeping sky ──────────────────────────────────────────────
        public const float RainRadius        = 35f;   // metres — a wide stretch of field
        public const float RainSeconds       = 90f;
        public const float RainTickSeconds   = 1f;    // how often the zone re-applies itself
        public const float RainFireDamp      = 0.5f;  // fire magic works at half strength inside
        public const float RainMountSlowMult = 0.6f;  // horses mire worst
        public const float RainFootSlowMult  = 0.9f;  // men only slog
        public const float RainArcheryDamp   = 0.4f;  // fraction of ranged damage the wet strings lose
        public const float RainAshenMoraleDrainPerTick = 0.8f; // the White Silence gnaws at foes

        // ── Spirit — the seized will ─────────────────────────────────────────────
        // Random target (not the strongest nearby), and a short hold — a fair
        // trade for never whiffing on "no target in range."
        public const float ThrallSeconds          = 30f;   // how long the borrowed will lasts
        public const float ThrallRangeMetres       = 16f;   // reach of the seizing — generous, it's an ultimate
        public const float ThrallDazedSpeedMult    = 0.4f;  // stagger on release…
        public const float ThrallDazedSeconds      = 3f;    // …while the borrowed will lets go
        public const float ThrallAshenPartingDamage = 18f;  // the Ashen working's parting frost-bite

        // ── Spirit fusion — the summoned kinsman ─────────────────────────────────
        // A lesser, repeatable cousin of the old champion-summon Unbinding: a
        // Spirit + X fusion (see ElementComboMath) still calls a Kindled to the
        // caster's side, gated to one living kinsman per summoner at a time.
        public const float ElementalSeconds     = 60f;  // how long the summoned kinsman lasts
        public const float ElementalSpawnOffset = 2.5f; // metres in front of the caster it appears

        // Which shape the land answers Spirit's OLDER working with (still used by
        // the Kindling battle event and wild elemental bands — the Unbinding itself
        // no longer summons). Snow always wins (a snowed-over desert is still
        // winter's ground); then sand by the scene's name; STONE is the default
        // answer everywhere else. Pure — the runtime passes SceneIsSnowy() and the
        // lower-cased scene name in.
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
                case ElementalKind.Flame: return "the Kindled";
                case ElementalKind.Tide:  return "the Risen Tide";
                case ElementalKind.Gale:  return "the Gathered Storm";
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
        public const float QuakeHpFrac        = 0.45f;// wounded and pressed → heave them back
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
                    case MagicElement.Earth:  return "The Barrow Wakes";
                    case MagicElement.Water:  return "The White Silence";
                    default:                  return "The Hollow Oath";
                }
            }
            switch (el)
            {
                case MagicElement.Fire:   return "The First Flame Remembered";
                case MagicElement.Wind:   return "On the Wings of the Gale";
                case MagicElement.Earth:  return "The Mountain's Wrath";
                case MagicElement.Water:  return "The Weeping Sky";
                default:                  return "The Bent Knee";
            }
        }
    }
}
