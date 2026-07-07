// =============================================================================
// ASH AND EMBER — AshenRuins/AshenRuinMath.cs
// Pure numeric logic for the newer Ashen Ruins challenge rooms. No TaleWorlds
// types — covered directly by PureLogicTests. Older rooms in AshenRuinSystem.cs
// still roll inline; new formulas land here per the project's *Math.cs convention.
// =============================================================================

using System;

namespace AshAndEmber
{
    public enum ShiftingHallOutcome { RenownGain, Desertion, WhisperGain }

    public static class AshenRuinMath
    {
        // ── Ember Wraith — fame draws the wraith's eye ─────────────────────────
        public static int EmberWraithPassChance(float clanRenown)
        {
            int chance = 75 - (int)(clanRenown / 40f);
            return Math.Max(20, Math.Min(75, chance));
        }

        // ── Wardstone Gate — a straight Roguery check ──────────────────────────
        public static int WardstoneGatePassChance(int roguerySkill)
        {
            int chance = 25 + roguerySkill / 3;
            return Math.Max(25, Math.Min(90, chance));
        }

        // ── Hollow Choir — the cold recognises its own ─────────────────────────
        public static int HollowChoirPassChance(int whisperTier)
        {
            if (whisperTier >= 2) return 100;
            int chance = 45 + whisperTier * 20;
            return Math.Max(45, Math.Min(100, chance));
        }

        // ── Weight of Ash — a mandatory toll that scales with the ruin's tier ──
        public static int WeightOfAshCost(RuinTier tier) => tier switch
        {
            RuinTier.Easy     => 2,
            RuinTier.Standard => 4,
            RuinTier.Brutal   => 7,
            _                 => 10,
        };

        // ── Triune Reckoning — the fallback roll for casters on no clear path ──
        public static int TriuneReckoningFallbackPassChance(int purchasedTalents)
        {
            int chance = 40 + purchasedTalents * 3;
            return Math.Max(30, Math.Min(80, chance));
        }

        // ── Shifting Hall — a three-way outcome table ───────────────────────────
        public static ShiftingHallOutcome ResolveShiftingHall(int roll100)
        {
            if (roll100 < 35) return ShiftingHallOutcome.RenownGain;
            if (roll100 < 70) return ShiftingHallOutcome.Desertion;
            return ShiftingHallOutcome.WhisperGain;
        }

        public static int ShiftingHallDesertionLoss(int healthyTroops) =>
            Math.Max(4, healthyTroops / 7);
    }
}
