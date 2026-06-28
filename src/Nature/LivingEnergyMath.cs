// =============================================================================
// ASH AND EMBER — Nature/LivingEnergyMath.cs
//
// Pure numeric logic for the LIVING ENERGY of a place — no TaleWorlds types,
// fully testable. Every battlefield and every stretch of the campaign map holds a
// finite reserve of living warmth, set by how much grows there (more plant life →
// more energy). Both Inner Fire and the Living Ember pull from it:
//
//   • Each draw of nature magic, or each cast of fire, spends some of it.
//   • Drawing an element the land FAVOURS spends little; drawing against the land
//     (water in the desert, say) spends far more.
//   • As it thins the caster is warned at the half, the quarter, and the dregs.
//   • Once it runs dry, every further use bleeds the hearth of nearby villages and
//     risks the working SOURING — turning back on the caster who forced it.
//   • Left alone, the land slowly heals.
//
// This holds only the arithmetic; LivingEnergy.cs holds the per-area reserves and
// applies the consequences in the world.
// =============================================================================

using System;

namespace AshAndEmber
{
    // Which warning a draw newly crossed, if any. Ordered by severity.
    public enum EnergyOmen { None = 0, Half = 1, Quarter = 2, Empty = 3 }

    public static class LivingEnergyMath
    {
        // ── Capacity ──────────────────────────────────────────────────────────
        // Maximum living energy a place holds, by terrain — its standing crop of
        // life. Forest teems; desert holds almost nothing. Names are
        // TaleWorlds.Core.TerrainType values (matched by .ToString()).
        public static float AreaCapacity(string terrainTypeName)
        {
            switch (terrainTypeName ?? "")
            {
                case "Forest":                                  return 120f;
                case "Swamp": case "Fording":                   return 100f;
                case "Plain": case "RuralArea":                 return 80f;
                case "Water": case "River": case "Lake":
                case "NonNavigableRiver":
                case "CoastalSea": case "OpenSea":              return 60f;
                case "Steppe": case "Dune":                     return 55f;
                case "Snow": case "Beach":                      return 50f;
                case "Canyon": case "Cliff":                    return 45f;
                case "Mountain":                                return 35f;
                case "Desert":                                  return 15f;
                default:                                        return 60f;
            }
        }

        // ── Match factor ──────────────────────────────────────────────────────
        // How hard a draw bites the land, by how well the chosen element suits it.
        public const float MatchedFactor    = 0.5f;   // a favoured element — gentle
        public const float NeutralFactor    = 1.0f;   // indifferent ground, or fire
        public const float MismatchedFactor = 2.0f;   // forcing the wrong element

        // Fire (NatureElement.None) is element-blind → always neutral.
        public static float MatchFactor(NatureElement el, string terrainTypeName)
        {
            if (el == NatureElement.None) return NeutralFactor;
            NatureElement[] favoured = NatureMath.TerrainElements(terrainTypeName);
            if (favoured == null || favoured.Length >= 4) return NeutralFactor; // unfamiliar ground
            for (int i = 0; i < favoured.Length; i++)
                if (favoured[i] == el) return MatchedFactor;
            return MismatchedFactor;
        }

        // ── Drain ─────────────────────────────────────────────────────────────
        // A single nature draw (gathering one charge) at neutral cost.
        public const float NatureDrawBase  = 8f;
        // Fire spends per stroke (forms + effects); a 4-stroke working ≈ 12.
        public const float FireDrawPerInput = 3f;

        // Energy a nature draw of `el` removes from a place of this terrain.
        public static float NatureDrain(NatureElement el, string terrainTypeName)
            => NatureDrawBase * MatchFactor(el, terrainTypeName);

        // Energy a fire cast of `totalInputs` strokes removes (element-blind).
        public static float FireDrain(int totalInputs, string terrainTypeName)
        {
            int n = totalInputs < 1 ? 1 : totalInputs;
            return n * FireDrawPerInput * MatchFactor(NatureElement.None, terrainTypeName);
        }

        // ── Thresholds ────────────────────────────────────────────────────────
        public const float HalfFraction    = 0.50f;
        public const float QuarterFraction = 0.25f;
        public const float EmptyFraction   = 0.0f;

        // The severity level of a given fraction of capacity (0 = healthy).
        public static EnergyOmen LevelOf(float fraction)
        {
            if (fraction <= EmptyFraction)   return EnergyOmen.Empty;
            if (fraction <= QuarterFraction) return EnergyOmen.Quarter;
            if (fraction <= HalfFraction)    return EnergyOmen.Half;
            return EnergyOmen.None;
        }

        // The omen to ANNOUNCE when energy moves from `beforeFraction` to
        // `afterFraction`, given the deepest level already announced for this place.
        // Returns None unless the draw crossed into a new, deeper warning band.
        public static EnergyOmen OmenCrossed(float beforeFraction, float afterFraction, EnergyOmen alreadyAnnounced)
        {
            EnergyOmen now = LevelOf(afterFraction);
            if (now > alreadyAnnounced && afterFraction < beforeFraction)
                return now;
            return EnergyOmen.None;
        }

        // Lore line for a freshly crossed omen.
        public static string OmenText(EnergyOmen omen)
        {
            switch (omen)
            {
                case EnergyOmen.Half:
                    return "The living warmth of this place is thinning. The green here has gone quiet, and watchful.";
                case EnergyOmen.Quarter:
                    return "The land here is nearly spent — what grows has turned grey and brittle, and the air tastes of ash.";
                case EnergyOmen.Empty:
                    return "The living world here is exhausted. Draw further and you take from things that have nothing left to give.";
                default:
                    return "";
            }
        }

        // ── Past empty: the land bites back ───────────────────────────────────
        // Once a place is drained dry, each further use bleeds a nearby village's
        // hearth and may SOUR — recoiling on the one who forced it.
        public const float HearthToll      = 12f;   // hearth lost per over-draw
        public const float SourChance      = 0.35f; // chance a forced working sours
        public const float SourSelfDamage  = 22f;   // HP recoil when it sours (battle)

        // Regrowth: a place recovers this fraction of its capacity each day it is
        // left in peace (a drained forest mends in roughly two weeks).
        public const float DailyRegenFraction = 0.06f;

        public static float DailyRegen(float capacity)
            => Math.Max(1f, capacity * DailyRegenFraction);

        // ── Rare weeds (the Green Draught) ────────────────────────────────────
        // While the smoke is in you, each draw has this chance to cost the land
        // nothing at all — the living world gives freely to one who is part of it.
        public const float WeedFreeDrawChance = 0.30f;

        // Reserves are floored here so a single hard-pressed place cannot run an
        // unbounded debt that would take a season to heal.
        public static float MinReserve(float capacity) => -capacity;

        public static float Fraction(float energy, float capacity)
            => capacity <= 0f ? 0f : energy / capacity;
    }
}
