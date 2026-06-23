// =============================================================================
// ASH AND EMBER — Nature/NatureMath.cs
//
// Pure numeric logic for The Living Ember — no TaleWorlds types, fully testable.
// Simplified to FOUR elements, each with one attack and one barrier power, drawn
// from the combat environment:
//   Wind  — mountains, steppes, hills
//   Earth — forests
//   Water — rivers, shores, lakes, rain, snow, wetland
//   Storm — deserts, open plains
//   (mixed / unknown ground gives a random element)
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

        // ── Terrain mapping ─────────────────────────────────────────────────────
        // Bannerlord TerrainType name → element. Matched by .ToString() so enum
        // integer drift does not break the lookup. Unknown / mixed ground returns
        // all four, so the draw is random.
        public static NatureElement[] TerrainElements(string terrainTypeName)
        {
            switch (terrainTypeName ?? "")
            {
                case "Forest":                                  return _earth;
                case "Mountain": case "Hill": case "Hills":
                case "Steppe":                                  return _wind;
                case "Water": case "ShallowRiver": case "River":
                case "Lake":   case "Shore":
                case "Snow":   case "Arctic":
                case "Swamp":  case "Wetland":                  return _water;
                case "Desert": case "Plain":
                case "Meadow": case "Grassland":                return _storm;
                default:                                        return _mixed;  // random
            }
        }

        private static readonly NatureElement[] _wind  = { NatureElement.Wind  };
        private static readonly NatureElement[] _earth = { NatureElement.Earth };
        private static readonly NatureElement[] _water = { NatureElement.Water };
        private static readonly NatureElement[] _storm = { NatureElement.Storm };
        private static readonly NatureElement[] _mixed =
            { NatureElement.Wind, NatureElement.Earth, NatureElement.Water, NatureElement.Storm };

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
        // Wind · Gale — 360° knockback + light damage
        public const float GaleRadius     = 10f;
        public const float GaleDamage     = 22f;
        public const float GaleKnockback  = 4f;
        public const float GaleSlowMult   = 0.80f;
        public const float GaleSlowSec    = 5f;
        // Earth · Entangle — roots immobilise + damage
        public const float EntangleRadius   = 6f;
        public const float EntangleDamage   = 40f;
        public const float EntangleRootSec  = 4f;
        public const float EntangleStaggerSec = 0.4f;  // brief caster pause

        // Water · Torrent — forward cone, damage + knockback (breaks formation)
        public const float TorrentRange     = 9f;
        public const float TorrentAngleDeg  = 70f;
        public const float TorrentDamage    = 30f;
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

        // Wind · Windwall — pure repulsion
        public const float WindwallPush = 3.5f;

        // Earth · Thornwall — push + brief root + damage-per-tick
        public const float ThornwallPush    = 0.8f;
        public const float ThornwallDamage  = 8f;    // per 0.4 s tick ≈ 20 dps
        public const float ThornwallRootSec = 0.6f;

        // Water · Mistwall — push + slow
        public const float MistwallPush     = 2.5f;
        public const float MistwallSlowMult = 0.45f;
        public const float MistwallSlowSec  = 1.8f;

        // Storm · Stormwall — push + damage-per-tick
        public const float StormwallPush   = 2.0f;
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
        public static string CampaignPowerLabel(NaturePower p)
        {
            switch (p)
            {
                case NaturePower.Windwall:  return "Windward — the wind goes ahead, but it takes a price [scouts enemies; costs ~20 food]";
                case NaturePower.Thornwall: return "Root-Mend — the earth feeds and mends, but takes a tithe [provisions + heal; costs hero HP]";
                case NaturePower.Mistwall:  return "Still Waters — the current shows a road, but the march arrives unsettled [teleport; -20 morale]";
                case NaturePower.Stormwall: return "Thunder's Edge — the storm does not ask sides [morale surge; own troops may be struck]";
                default:                    return "";
            }
        }
    }
}
