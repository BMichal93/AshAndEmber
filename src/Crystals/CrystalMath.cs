// =============================================================================
// ASH AND EMBER — Crystals/CrystalMath.cs
//
// Pure numeric logic for the crystal system. No TaleWorlds types — everything
// here is covered by tests/PureLogicTests.cs. All randomness is passed in as
// rolls in [0,1) so every outcome is deterministic and testable.
// =============================================================================

using System;

namespace AshAndEmber
{
    public static class CrystalMath
    {
        // ── Formation odds ────────────────────────────────────────────────────
        // Combined skill = (Medicine + Engineering) / 2.
        // Floor 6 %, ceiling 90 %. Each combined point above 0 adds 0.3 %.
        // At 150 combined: ~6 + 150*0.003 = 51 %. At 280 combined: ~90 % cap.
        public static float FormationOdds(int medicine, int engineering)
        {
            float combined = (medicine + engineering) / 2f;
            return Math.Min(0.90f, Math.Max(0.06f, 0.06f + combined * 0.003f));
        }

        // PatientGrowth rite applies a flat bonus before the ceiling clamp.
        public static float FormationOddsWithPatience(int medicine, int engineering)
            => Math.Min(0.90f, FormationOdds(medicine, engineering) + 0.20f);

        // ── Daylight gate ─────────────────────────────────────────────────────
        // Crystals focus sunlight; they are inert in the dark.
        // Standard window: 06:00–19:59. SolarFlare extends to 04:00–21:59.
        public static bool IsDaylight(float hourOfDay)         => hourOfDay >= 6f  && hourOfDay < 20f;
        public static bool IsDaylightExtended(float hourOfDay) => hourOfDay >= 4f  && hourOfDay < 22f;

        // ── Burndown chance ───────────────────────────────────────────────────
        // Each activation has a 10 % chance to shatter the crystal.
        public const float BurndownChance = 0.10f;

        // PatientGrowth: on a successful formation, 15 % chance to grow two.
        public const float DoubleGrowChance = 0.15f;

        // ── Charge duration ───────────────────────────────────────────────────
        public const float ChargeDurationSec = 2.0f;

        // ── Sunstone — warmth pulse ───────────────────────────────────────────
        public const float SunRadius   = 5f;
        public const float SunSelfHeal = 30f;
        public const float SunAllyHeal = 15f;

        // ── Embershard — shard burst (AoE fire damage) ────────────────────────
        public const float EmberRadius = 5f;
        public const float EmberDamage = 35f;

        // ── Rimeshard — frost pulse (enemy slow) ─────────────────────────────
        public const float RimeRadius      = 5f;
        public const float RimeSlowMult    = 0.70f; // 70 % of normal speed (30 % slow)
        public const float RimeDurationSec = 5f;

        // ── Veilstone — veil grasp (single random enemy in extended range) ──────
        // The veil reaches out and locks onto one random enemy; reliable but
        // unpredictable in who it targets. Range is wider than AoE crystals.
        public const float VeilRange       = 12f;
        public const float VeilDamage      = 60f;
        public const float VeilSlowMult    = 0.75f; // 25 % slow
        public const float VeilDurationSec = 4f;

        // ── Stormcrystal — thunder clap (AoE damage + morale drain) ──────────
        public const float StormRadius      = 4f;
        public const float StormDamage      = 35f;
        public const float StormMoraleDrain = 15f;

        // ── Duskstone — despair wave (morale drain + slow) ────────────────────
        public const float DuskRadius      = 5f;
        public const float DuskMoraleDrain = 18f;
        public const float DuskSlowMult    = 0.80f; // 80 % of normal speed (20 % slow)
        public const float DuskDurationSec = 5f;

        // ── Seeding odds for lords ────────────────────────────────────────────
        public const float LordSeedChance = 0.05f; // 5 % flat

        // ── Formation materials ───────────────────────────────────────────────
        public const int SilverOreCost  = 1;
        public const int TradeGoodCost  = 1;

        // ── Shop stock ────────────────────────────────────────────────────────
        // Each Crystalline Chamber town keeps this many of each crystal type.
        // Restocked weekly; purchased crystals won't reappear until the next tick.
        public const int ShopStockPerType = 1;

        // SolarFlare (+25 % AoE radius multiplier) applies to burst-type crystals.
        public static float SolarFlareRadius(float baseRadius) => baseRadius * 1.25f;
    }
}
