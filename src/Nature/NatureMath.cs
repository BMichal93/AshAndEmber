// =============================================================================
// ASH AND EMBER — Nature/NatureMath.cs
//
// Pure numeric logic for The Living Ember — no TaleWorlds types, fully
// testable. Terrain-to-element mapping, power constants, and draw costs.
// =============================================================================

using System;
using System.Collections.Generic;

namespace AshAndEmber
{
    // Six elemental channels of the living world.
    public enum NatureElement
    {
        None    = 0,
        Verdant = 1,   // Forest — the living web of root and branch
        Stone   = 2,   // Mountain — unyielding earth
        Water   = 3,   // River / Shore — flowing, cold, patient
        Wind    = 4,   // Plain / Sky — the breath between things
        Frost   = 5,   // Tundra / Snow — crystalline stillness
        Storm   = 6,   // Steppe / Desert — charge in open sky
    }

    // The twelve powers: two per element (attack + support).
    public enum NaturePower
    {
        None = 0,
        // Verdant
        Thorngrasp   = 1,   // pull + hold   — attack
        LivingBreath = 2,   // AoE heal       — support
        // Stone
        StoneSurge   = 3,   // blunt + root   — attack
        EarthMantle  = 4,   // resist buff    — support
        // Water
        Undertow     = 5,   // cone knockback — attack
        StillWater   = 6,   // self-heal      — support
        // Wind
        CallingGale  = 7,   // 360° knockback — attack
        FairWind     = 8,   // speed buff     — support
        // Frost
        Hoarfrost    = 9,   // AoE slow       — attack
        GlacialShell = 10,  // ice armour     — support
        // Storm
        WrathOfTheSky = 11, // lightning arc  — attack
        LevinStep    = 12,  // instant dash   — support
    }

    public static class NatureMath
    {
        // ── Armour gate ────────────────────────────────────────────────────────
        // Total armour weight above this cap blocks drawing and casting.
        public const float ArmourWeightCap = 25f;

        // ── Charge ────────────────────────────────────────────────────────────
        // Base expiry for a held charge (seconds in mission, days on campaign).
        public const float ChargeMissionExpirySec  = 90f;
        public const int   ChargeCampaignExpiryDays = 1;

        // HP cost when drawing in combat (except Verdant, which is free).
        public const float DrawHpCostStone   = 12f;
        public const float DrawHpCostWater   = 10f;
        public const float DrawHpCostWind    = 10f;
        public const float DrawHpCostFrost   = 14f;
        public const float DrawHpCostStorm   = 13f;
        public const float DrawHpCostVerdant =  0f; // free — the world gives willingly

        public static float DrawHpCost(NatureElement el)
        {
            switch (el)
            {
                case NatureElement.Stone:   return DrawHpCostStone;
                case NatureElement.Water:   return DrawHpCostWater;
                case NatureElement.Wind:    return DrawHpCostWind;
                case NatureElement.Frost:   return DrawHpCostFrost;
                case NatureElement.Storm:   return DrawHpCostStorm;
                default:                   return DrawHpCostVerdant;
            }
        }

        // ── Terrain mapping ────────────────────────────────────────────────────
        // Bannerlord TerrainType name → NatureElement list (1 = pure, 2 = hybrid).
        // Matched by .ToString() so enum integer drift does not break the lookup.
        public static NatureElement[] TerrainElements(string terrainTypeName)
        {
            switch (terrainTypeName ?? "")
            {
                case "Forest":                          return _verdant;
                case "Mountain":                        return _stone;
                case "Water": case "ShallowRiver":
                case "River": case "Lake":              return _water;
                case "Shore":                           return _waterWind;
                case "Plain":                           return _wind;
                case "Snow": case "Arctic":             return _frost;
                case "Desert":                          return _storm;
                case "Steppe":                          return _stormWind;
                case "Swamp": case "Wetland":           return _stoneWater;
                case "Hill": case "Hills":              return _stoneWind;
                case "Meadow": case "Grassland":        return _verdantWind;
                default:                                return _wind;
            }
        }

        private static readonly NatureElement[] _verdant     = { NatureElement.Verdant };
        private static readonly NatureElement[] _stone       = { NatureElement.Stone };
        private static readonly NatureElement[] _water       = { NatureElement.Water };
        private static readonly NatureElement[] _wind        = { NatureElement.Wind };
        private static readonly NatureElement[] _frost       = { NatureElement.Frost };
        private static readonly NatureElement[] _storm       = { NatureElement.Storm };
        private static readonly NatureElement[] _waterWind   = { NatureElement.Water,   NatureElement.Wind   };
        private static readonly NatureElement[] _stormWind   = { NatureElement.Storm,   NatureElement.Wind   };
        private static readonly NatureElement[] _stoneWater  = { NatureElement.Stone,   NatureElement.Water  };
        private static readonly NatureElement[] _stoneWind   = { NatureElement.Stone,   NatureElement.Wind   };
        private static readonly NatureElement[] _verdantWind = { NatureElement.Verdant, NatureElement.Wind   };

        // Given an element, randomly pick attack or support power.
        public static NaturePower RandomPower(NatureElement el, Random rng)
        {
            bool attack = rng.Next(2) == 0;
            switch (el)
            {
                case NatureElement.Verdant: return attack ? NaturePower.Thorngrasp    : NaturePower.LivingBreath;
                case NatureElement.Stone:   return attack ? NaturePower.StoneSurge    : NaturePower.EarthMantle;
                case NatureElement.Water:   return attack ? NaturePower.Undertow      : NaturePower.StillWater;
                case NatureElement.Wind:    return attack ? NaturePower.CallingGale   : NaturePower.FairWind;
                case NatureElement.Frost:   return attack ? NaturePower.Hoarfrost     : NaturePower.GlacialShell;
                case NatureElement.Storm:   return attack ? NaturePower.WrathOfTheSky : NaturePower.LevinStep;
                default:                    return NaturePower.None;
            }
        }

        public static NatureElement ElementOf(NaturePower power)
        {
            switch (power)
            {
                case NaturePower.Thorngrasp:
                case NaturePower.LivingBreath:  return NatureElement.Verdant;
                case NaturePower.StoneSurge:
                case NaturePower.EarthMantle:   return NatureElement.Stone;
                case NaturePower.Undertow:
                case NaturePower.StillWater:    return NatureElement.Water;
                case NaturePower.CallingGale:
                case NaturePower.FairWind:      return NatureElement.Wind;
                case NaturePower.Hoarfrost:
                case NaturePower.GlacialShell:  return NatureElement.Frost;
                case NaturePower.WrathOfTheSky:
                case NaturePower.LevinStep:     return NatureElement.Storm;
                default:                        return NatureElement.None;
            }
        }

        // ── NPC use chances ────────────────────────────────────────────────────
        public static double NpcBattleUseChance() => 0.003;
        public static double NpcDailyUseChance()  => 0.015;

        // ── Battle effect constants ────────────────────────────────────────────

        // Thorngrasp — pull + hold
        public const float ThorngrassPullDist  = 3f;
        public const float ThorngaspHoldSec    = 2.5f;
        public const float ThorngraspDamage    = 0f;   // no direct damage
        public const float ThorngraspRange     = 7f;

        // Living Breath — AoE heal (Verdant: no HP cost to draw, so this is pure gain)
        public const float LivingBreathSelfHp     = 25f;
        public const float LivingBreathAllyHp     = 18f;
        public const float LivingBreathMorale     = 15f;
        public const float LivingBreathRadius     = 10f;

        // Stone Surge — blunt + root
        public const float StoneSurgeDamage   = 45f;
        public const float StoneSurgeRadius   = 5f;
        public const float StoneSurgeRootSec  = 4f;
        public const float StoneSurgeStaggerSec = 0.5f;  // caster immobile

        // Earth Mantle — resist buff
        public const float EarthMantleResist  = 0.40f;
        public const float EarthMantleSec     = 10f;

        // Undertow — cone knockback
        public const float UndertowDamage     = 30f;
        public const float UndertowKnockback  = 4f;
        public const float UndertowSpeedMult  = 0.75f;
        public const float UndertowSpeedSec   = 5f;
        public const float UndertowRange      = 8f;
        public const float UndertowAngleDeg   = 60f;

        // Still Water — self-heal (free draw in Verdant; costs HP in Water, so moderate)
        public const float StillWaterHeal     = 35f;

        // Calling Gale — 360° knockback
        public const float CallingGaleDamage  = 20f;
        public const float CallingGaleKnockback = 3f;
        public const float CallingGaleSpeedMult = 0.80f;
        public const float CallingGaleSpeedSec  = 6f;
        public const float CallingGaleAllySpeed = 1.15f;
        public const float CallingGaleAllySpeedSec = 8f;
        public const float CallingGaleRadius   = 10f;

        // Fair Wind — speed buff
        public const float FairWindSpeedMult  = 1.35f;
        public const float FairWindSec        = 15f;
        public const float FairWindRadius     = 8f;

        // Hoarfrost — AoE slow
        public const float HoarfrostDamage   = 30f;
        public const float HoarfrostSpeedMult = 0.60f;
        public const float HoarfrostSpeedSec  = 7f;
        public const float HoarfrostRadius   = 8f;
        public const float HoarfrostSelfSlowSec = 2f;
        public const float HoarfrostSelfSlowMult = 0.85f;

        // Glacial Shell — ice armour
        public const float GlacialShellResist   = 0.40f;
        public const float GlacialShellSec      = 10f;
        public const float GlacialShellSpeedMult = 0.80f;

        // Wrath of the Sky — lightning arc + chain
        public const float WrathDamagePrimary   = 70f;
        public const float WrathDamageChain     = 35f;
        public const float WrathRange           = 8f;
        public const int   WrathChainCount      = 2;
        public const float WrathChainRadius     = 6f;

        // Levin Step — instant dash
        public const float LevinStepDist        = 5f;
        public const float LevinStepInvulnerSec = 0.3f;

        // ── Naming ────────────────────────────────────────────────────────────
        public static string PowerName(NaturePower p)
        {
            switch (p)
            {
                case NaturePower.Thorngrasp:    return "Thorngrasp";
                case NaturePower.LivingBreath:  return "Living Breath";
                case NaturePower.StoneSurge:    return "Stone Surge";
                case NaturePower.EarthMantle:   return "Earth Mantle";
                case NaturePower.Undertow:      return "Undertow";
                case NaturePower.StillWater:    return "Still Water";
                case NaturePower.CallingGale:   return "Calling Gale";
                case NaturePower.FairWind:      return "Fair Wind";
                case NaturePower.Hoarfrost:     return "Hoarfrost";
                case NaturePower.GlacialShell:  return "Glacial Shell";
                case NaturePower.WrathOfTheSky: return "Wrath of the Sky";
                case NaturePower.LevinStep:     return "Levin Step";
                default:                        return "None";
            }
        }

        public static string ElementName(NatureElement el)
        {
            switch (el)
            {
                case NatureElement.Verdant: return "Verdant";
                case NatureElement.Stone:   return "Stone";
                case NatureElement.Water:   return "Water";
                case NatureElement.Wind:    return "Wind";
                case NatureElement.Frost:   return "Frost";
                case NatureElement.Storm:   return "Storm";
                default:                   return "None";
            }
        }
    }
}
