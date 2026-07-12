// =============================================================================
// ASH AND EMBER — Nature/NatureMath.cs
//
// Pure numeric logic for The Living Ember — no TaleWorlds types, fully testable.
// FOUR elements, each with one attack and one barrier power. The caster DRAWS the
// element they want by tracing a direction while focused (W=Wind, S=Earth,
// A=Water, D=Storm); the land no longer decides which element answers.
//
// Terrain still matters, but for COST rather than choice: each land favours
// certain elements (Wind on mountains/steppes, Earth in forest, Water by water,
// Storm on open plain/desert). Drawing a favoured element spends less of the
// place's living energy; drawing against the land spends far more. See
// LivingEnergyMath for the energy economy.
// =============================================================================

using System;

namespace AshAndEmber
{
    // The four channels of the living world.
    public enum NatureElement
    {
        None  = 0,
        Wind  = 1,   // open high country — the breath between things
        Earth = 2,   // forest — root, branch, and the grip of growing things
        Water = 3,   // river, shore, rain and snow — flowing and patient
        Storm = 4,   // open plain and desert sky — charge and sudden violence
    }

    // Eight powers: an attack and a barrier for each element.
    public enum NaturePower
    {
        None = 0,
        // Wind
        Gale      = 1,   // attack  — 360° knockback + damage
        Windwall  = 2,   // barrier — wall of howling wind that hurls foes back
        // Earth
        Entangle  = 3,   // attack  — roots immobilise + damage
        Thornwall = 4,   // barrier — erupting thorns: root + bleed
        // Water
        Torrent   = 5,   // attack  — forward cone, knockback (breaks formation)
        Mistwall  = 6,   // barrier — churning water curtain: push + slow
        // Storm
        ThunderClap = 7, // attack  — bolt + chain
        Stormwall   = 8, // barrier — crackling lightning field: push + damage
    }

    public static class NatureMath
    {
        // ── Channel / charge ────────────────────────────────────────────────────
        // Standing still while focusing fills a charge over this many seconds…
        public const float ChannelFillSeconds = 6f;
        // …and the resulting charge then lasts this long before it fades.
        public const float ChargeLifeSeconds  = 30f;
        // On the campaign map, standing still this many hours yields a charge.
        public const int   ChargeCampaignHours = 4;

        // ── Armour gate ─────────────────────────────────────────────────────────
        // Total armour weight above this cap blocks channelling and casting.
        public const float ArmourWeightCap = 25f;

        // ── Knockback smoothing ─────────────────────────────────────────────────
        // A push is eased across this many seconds instead of snapping straight to
        // its final spot — long enough to read as a body actually travelling the
        // distance, short enough to still feel instantaneous, not a slow glide.
        public const float KnockbackPushDuration = 0.16f;

        // Fraction of the push distance covered after `elapsedSeconds` — a quick
        // quadratic ease-out (fastest at the moment of impact, settling into the
        // landing) rather than a constant-speed slide.
        public static float KnockbackEase(float elapsedSeconds)
        {
            if (elapsedSeconds <= 0f) return 0f;
            if (elapsedSeconds >= KnockbackPushDuration) return 1f;
            float t = elapsedSeconds / KnockbackPushDuration;
            return 1f - (1f - t) * (1f - t);
        }

        // ── Terrain mapping ─────────────────────────────────────────────────────
        // Bannerlord TerrainType name → the element(s) the land FAVOURS. Matched by
        // .ToString() so enum integer drift does not break the lookup. Iconic
        // terrains favour ONE element; transitional/blended terrains favour TWO;
        // truly unknown ground favours none in particular (all four → neutral cost).
        // Drawing a favoured element here is cheap; drawing an unfavoured one drains
        // the land hard (see LivingEnergyMath.MatchFactor). Names are
        // TaleWorlds.Core.TerrainType values.
        public static NatureElement[] TerrainElements(string terrainTypeName)
        {
            switch (terrainTypeName ?? "")
            {
                // ── Pure: one element the whole battle ──────────────────────────
                case "Forest":                                  return _earth;
                case "Mountain":                                return _wind;
                case "Water": case "River": case "Lake":
                case "NonNavigableRiver":
                case "CoastalSea": case "OpenSea":              return _water;
                case "Desert":                                  return _storm;

                // ── Blended: each charge is randomly one of the two ─────────────
                case "Steppe": case "Dune":     return _windStorm;   // open, wind-swept, storm-prone
                case "Plain":  case "RuralArea": return _earthStorm; // grass under open sky
                case "Snow":   case "Beach":    return _waterWind;   // cold surf and sea-wind
                case "Swamp":  case "Fording":  return _waterEarth;  // mud and root
                case "Canyon": case "Cliff":    return _earthWind;   // stone and the wind through it

                default:                                        return _mixed;  // unknown: any of the four
            }
        }

        private static readonly NatureElement[] _wind  = { NatureElement.Wind  };
        private static readonly NatureElement[] _earth = { NatureElement.Earth };
        private static readonly NatureElement[] _water = { NatureElement.Water };
        private static readonly NatureElement[] _storm = { NatureElement.Storm };
        // Two-element blends — the land offers one or the other, rolled per charge.
        private static readonly NatureElement[] _windStorm  = { NatureElement.Wind,  NatureElement.Storm };
        private static readonly NatureElement[] _earthStorm = { NatureElement.Earth, NatureElement.Storm };
        private static readonly NatureElement[] _waterWind  = { NatureElement.Water, NatureElement.Wind  };
        private static readonly NatureElement[] _waterEarth = { NatureElement.Water, NatureElement.Earth };
        private static readonly NatureElement[] _earthWind  = { NatureElement.Earth, NatureElement.Wind  };
        private static readonly NatureElement[] _mixed =
            { NatureElement.Wind, NatureElement.Earth, NatureElement.Water, NatureElement.Storm };

        // ── Element selection ────────────────────────────────────────────────────
        // The caster DRAWS the element by tracing a direction while focused, rather
        // than taking whatever the ground offers. The land no longer decides which
        // element answers — only how dearly it costs (see LivingEnergyMath).
        //   W (Up)    → Wind     S (Down)  → Earth
        //   A (Left)  → Water    D (Right) → Storm
        public static NatureElement ElementForKey(string dir)
        {
            switch ((dir ?? "").ToUpperInvariant())
            {
                case "W": case "U": case "UP":    return NatureElement.Wind;
                case "S": case "D2": case "DOWN": return NatureElement.Earth;
                case "A": case "L": case "LEFT":  return NatureElement.Water;
                case "D": case "R": case "RIGHT": return NatureElement.Storm;
                default:                          return NatureElement.None;
            }
        }

        // The W/A/S/D key that draws an element — for hints and the journal.
        public static string KeyForElement(NatureElement el)
        {
            switch (el)
            {
                case NatureElement.Wind:  return "W";
                case NatureElement.Earth: return "S";
                case NatureElement.Water: return "A";
                case NatureElement.Storm: return "D";
                default:                  return "?";
            }
        }

        // ── Power lookups ───────────────────────────────────────────────────────
        public static NaturePower AttackPower(NatureElement el)
        {
            switch (el)
            {
                case NatureElement.Wind:  return NaturePower.Gale;
                case NatureElement.Earth: return NaturePower.Entangle;
                case NatureElement.Water: return NaturePower.Torrent;
                case NatureElement.Storm: return NaturePower.ThunderClap;
                default:                  return NaturePower.None;
            }
        }

        public static NaturePower SupportPower(NatureElement el)
        {
            switch (el)
            {
                case NatureElement.Wind:  return NaturePower.Windwall;
                case NatureElement.Earth: return NaturePower.Thornwall;
                case NatureElement.Water: return NaturePower.Mistwall;
                case NatureElement.Storm: return NaturePower.Stormwall;
                default:                  return NaturePower.None;
            }
        }

        // NPC use: pick attack or support at random for an element.
        public static NaturePower RandomPower(NatureElement el, Random rng)
            => rng.Next(2) == 0 ? AttackPower(el) : SupportPower(el);

        public static NatureElement ElementOf(NaturePower power)
        {
            switch (power)
            {
                case NaturePower.Gale:
                case NaturePower.Windwall:    return NatureElement.Wind;
                case NaturePower.Entangle:
                case NaturePower.Thornwall:   return NatureElement.Earth;
                case NaturePower.Torrent:
                case NaturePower.Mistwall:    return NatureElement.Water;
                case NaturePower.ThunderClap:
                case NaturePower.Stormwall:   return NatureElement.Storm;
                default:                      return NatureElement.None;
            }
        }

        public static bool IsAttack(NaturePower power)
            => power == NaturePower.Gale || power == NaturePower.Entangle
            || power == NaturePower.Torrent || power == NaturePower.ThunderClap;

        // ── NPC use chances ─────────────────────────────────────────────────────
        public static double NpcBattleUseChance() => 0.003;
        public static double NpcDailyUseChance()  => 0.015;

        // ── Battle effect constants ─────────────────────────────────────────────
        // Wind · Gale — the pusher: a forward GUST/stream, knockback + slow; hits
        // everything in a broad wedge the caster faces (no longer a 360° ring).
        public const float GaleRadius       = 10f;   // reach of the gust
        public const float GaleConeAngleDeg = 55f;   // full wedge width — a driving stream, not a ring
        public const float GaleDamage       = 30f;
        public const float GaleKnockback    = 4f;
        public const float GaleSlowMult     = 0.80f;
        public const float GaleSlowSec      = 5f;
        // Earth · Entangle — the crusher: a SHORT, almost-melee cone of erupting
        // rock at the caster's feet. It reaches barely past sword's length, but
        // what it catches it hits like a landslide and pins in place — the trade
        // is reach for raw force (a close fan the caster faces, not an AoE ring).
        public const float EntangleRange      = 5f;   // close — barely past melee
        public const float EntangleConeAngleDeg = 78f; // a wide fan at the feet, not a distant ridge
        public const float EntangleRadius   = 6f;      // kept for save/visual compat (ring-scaled effects)
        public const float EntangleDamage   = 85f;     // far heavier than the ranged elements — reach traded for force
        public const float EntangleRootSec  = 4f;
        public const float EntangleStaggerSec = 0.4f;  // brief caster pause

        // Water · Torrent — the breaker: forward cone, solid damage + knockback + slow
        public const float TorrentRange     = 9f;
        public const float TorrentAngleDeg  = 70f;
        public const float TorrentDamage    = 34f;
        public const float TorrentKnockback = 5f;
        public const float TorrentSlowMult  = 0.70f;
        public const float TorrentSlowSec   = 5f;

        // Storm · ThunderClap — bolt + chain
        public const float ThunderRange       = 9f;
        public const float ThunderDamage      = 65f;
        public const float ThunderChainDamage = 32f;
        public const int   ThunderChainCount  = 2;
        public const float ThunderChainRadius = 6f;
        public const float ThunderStunSec     = 1.5f;

        // ── Barrier (shared) ────────────────────────────────────────────────────
        public const float BarrierDuration     = 7f;    // seconds the wall persists
        public const float BarrierForwardDist  = 3.0f;  // metres ahead of the caster
        public const float BarrierNodeSpacing  = 2.0f;  // metres between nodes
        public const float BarrierNodeRadius   = 2.0f;  // engagement radius per node
        public const int   BarrierNodeCount    = 5;     // nodes across the wall (8 m wide)
        public const float BarrierTickInterval = 0.4f;  // visual + repulsion pulse rate
        // A fully-drawn wall thickens into this many rows deep (a filled rectangle);
        // a weak one is a single curtain. The rows sit this far apart.
        public const int   BarrierMaxDepthRows = 3;
        public const float BarrierRowSpacing   = 1.8f;
        // Rather than a small nudge (which a running enemy simply out-paces), the
        // wall shoves any foe back to just beyond the node's edge — a firm barrier
        // they cannot walk through. This is the margin past the radius.
        public const float BarrierBounceMargin = 0.75f;

        // Wind · Windwall — pure repulsion, so the repulsion must be VIOLENT: where
        // every other wall merely stops a man at its edge, the howling wind HURLS
        // him well clear of it. Without this the Windwall was strictly the worst
        // wall — the same firm bounce as Mistwall, minus its slow, bite and quench.
        public const float WindwallHurlMargin = 4.0f;   // metres past the node edge (others: BarrierBounceMargin)

        // Earth · Thornwall — firm bounce + brief root + damage-per-tick
        public const float ThornwallDamage  = 8f;    // per 0.4 s tick ≈ 20 dps
        public const float ThornwallRootSec = 0.6f;

        // Water · Mistwall — firm bounce + slow + a cold bite (no longer harmless)
        public const float MistwallSlowMult = 0.45f;
        public const float MistwallSlowSec  = 1.8f;
        public const float MistwallDamage   = 6f;    // per 0.4 s tick ≈ 15 dps

        // Storm · Stormwall — firm bounce + damage-per-tick
        public const float StormwallDamage = 18f;    // per 0.4 s tick ≈ 45 dps

        // ── Naming ──────────────────────────────────────────────────────────────
        public static string PowerName(NaturePower p)        {
            switch (p)
            {
                case NaturePower.Gale:        return "Gale";
                case NaturePower.Windwall:    return "Windwall";
                case NaturePower.Entangle:    return "Entangle";
                case NaturePower.Thornwall:   return "Thornwall";
                case NaturePower.Torrent:     return "Torrent";
                case NaturePower.Mistwall:    return "Mistwall";
                case NaturePower.ThunderClap: return "Thunderclap";
                case NaturePower.Stormwall:   return "Stormwall";
                default:                      return "None";
            }
        }

        public static string ElementName(NatureElement el)
        {
            switch (el)
            {
                case NatureElement.Wind:  return "Wind";
                case NatureElement.Earth: return "Earth";
                case NatureElement.Water: return "Water";
                case NatureElement.Storm: return "Storm";
                default:                  return "None";
            }
        }

        // Short label for the campaign-map menu entry (one per support power).
        // Short button label — just the name and a few words, so it fits the option
        // row. The full effect and cost live in CampaignPowerHint (the hover tooltip).
        public static string CampaignPowerLabel(NaturePower p)
        {
            switch (p)
            {
                case NaturePower.Windwall:  return "Windward — speed the march";
                case NaturePower.Thornwall: return "Root-Mend — bless a village";
                case NaturePower.Mistwall:  return "Still Waters — sea-carry to a harbour";
                case NaturePower.Stormwall: return "Thunder's Edge — a storm of courage";
                default:                    return "";
            }
        }

        // Full description for the option's hover tooltip.
        public static string CampaignPowerHint(NaturePower p)
        {
            switch (p)
            {
                case NaturePower.Windwall:  return "The wind presses the march forward — advances your column toward its destination. Costs food.";
                case NaturePower.Thornwall: return "The roots go deep and give to the land — the nearest village gains +50 hearth. Costs hero HP.";
                case NaturePower.Mistwall:  return "The sea carries you to any harbour. Requires water terrain; your soldiers arrive cold (-20 morale).";
                case NaturePower.Stormwall: return "The storm does not ask sides — a surge of morale, but your own troops may be struck.";
                default:                    return "";
            }
        }
    }
}
