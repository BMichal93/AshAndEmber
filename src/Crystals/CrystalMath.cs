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
        // The dedicated crowd-slow: it does no damage, so it must excel at its one
        // job — the deepest slow of any crystal (Duskstone's slow is shallower).
        public const float RimeRadius      = 5f;
        public const float RimeSlowMult    = 0.60f; // 60 % of normal speed (40 % slow)
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
        // The dedicated morale-breaker: its slow is deliberately shallower than
        // Rimeshard's; its bite is the heavy morale drain (routs a wavering line).
        public const float DuskRadius      = 5f;
        public const float DuskMoraleDrain = 25f;
        public const float DuskSlowMult    = 0.80f; // 80 % of normal speed (20 % slow)
        public const float DuskDurationSec = 5f;

        // ── Thornveil — root grasp (single random enemy, near-total immobilise) ──
        // The deepest of any crystal's slows — a near-full root — but brief and
        // narrower in reach than Veilstone, and its damage is the lowest of the
        // single-target stones since the control is the point.
        public const float ThornRange           = 10f;
        public const float ThornDamage          = 25f;
        public const float ThornRootMult        = 0.05f; // 5 % of normal speed — a near-total root
        public const float ThornRootDurationSec = 3f;

        // ── Aegisstone — bulwark pulse (self heal + AoE knockback) ────────────
        // The one purely defensive crystal: no damage at all, just breathing room
        // and a modest mend when the bearer is boxed in.
        public const float AegisRadius    = 5f;
        public const float AegisSelfHeal  = 20f;
        public const float AegisKnockback = 4f; // metres — matches Gale's knockback scale

        // ── Willowisp — dread whisper (single random enemy, morale only) ──────
        // No damage and no slow — it spends its whole potency on one enemy's
        // nerve, a single hit far heavier than either AoE morale-breaker.
        public const float WillowRange       = 12f; // matches Veilstone's reach
        public const float WillowMoraleDrain = 40f;

        // ── Bloodstone — vampiric burst (AoE damage, lifesteal to caster) ─────
        public const float BloodRadius        = 4f;
        public const float BloodDamage        = 25f;
        public const float BloodLifestealFrac = 0.5f; // half of total damage dealt returns as healing

        // ── Zephyrglass — quickening light (AoE haste, caster + allies) ───────
        // The only crystal that helps allies move rather than enemies move less;
        // does not scale with Solar Flare (support effects don't, per Sunstone).
        public const float ZephyrRadius      = 5f;
        public const float ZephyrHasteMult   = 1.30f; // 30 % faster
        public const float ZephyrDurationSec = 6f;

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

        // ── NPC situational use ───────────────────────────────────────────────
        // A crystal-bearer breaks a crystal when it actually helps, not on a blind
        // roll: the Sunstone heals, so it wants its bearer hurt; every other crystal
        // is offensive/control, so it wants enemies in reach. `roll` in [0,1) gates
        // the base eagerness so use stays occasional even when the situation fits.
        public static bool IsHealingCrystal(CrystalType type) =>
            type == CrystalType.Sunstone || type == CrystalType.Aegisstone;

        // The reach at which a crystal has worthwhile targets — the reach-out-to-one
        // stones (Veilstone, Willowisp) look further than the short-range AoE stones.
        public static float CrystalUseRange(CrystalType type)
        {
            if (type == CrystalType.Veilstone) return VeilRange;
            if (type == CrystalType.Willowisp)  return WillowRange;
            return 10f;
        }

        public static bool NpcShouldUse(CrystalType type, float hpFrac, int enemiesInRange, float roll)
        {
            if (IsHealingCrystal(type))
                return hpFrac < 0.55f && roll < 0.70f;   // mend when meaningfully hurt
            if (enemiesInRange <= 0) return false;        // never spend an offensive crystal on empty air
            float eagerness = enemiesInRange >= 3 ? 0.70f : 0.45f;
            return roll < eagerness;
        }
    }
}
