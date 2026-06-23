// =============================================================================
// ASH AND EMBER — Miracles/MiracleMath.cs
//
// Pure numeric logic for the miracle system — no TaleWorlds types, fully
// testable. Sequences, trait gates, effect magnitudes all live here so the
// input handler and effects code never contain raw numbers.
//
// Cold miracle constants removed — Dark Altars now grant permanent Dark Gifts
// instead of Cold, and Cold miracles have been retired entirely.
// =============================================================================

using System;

namespace AshAndEmber
{
    public enum MiracleGate { None, OneVirtue, AllVirtues }

    public static class MiracleMath
    {
        // ── Counters ───────────────────────────────────────────────────────────
        public const int GraceColdCap        = 10;
        public const int PrayerCooldownDays  =  3;   // Pray for Grace
        public const int WardingCooldownDays =  7;   // Warding Seal

        // ── Miracle sequences (6 chars, W=U A=L D=R S=D) ──────────────────────
        public const string SeqRepelAshen      = "UUUURR";
        public const string SeqRadiantMending  = "UUDDLR";
        public const string SeqLightOfGuidance = "UDUDUD";
        public const string SeqSacredFlame     = "UURRDL";
        public const string SeqAegisOfFaith    = "LLUURR";
        public const string SeqCleansingRite   = "RULRUU";

        public const int SequenceLength = 6;

        // Returns true and the matched MiracleType if the 6-char input is known.
        public static bool TryMatchSequence(string input, out MiracleType type)
        {
            type = MiracleType.RadiantMending;
            if (string.IsNullOrEmpty(input) || input.Length != SequenceLength) return false;
            switch (input)
            {
                case SeqRepelAshen:      type = MiracleType.RepelAshen;      return true;
                case SeqRadiantMending:  type = MiracleType.RadiantMending;  return true;
                case SeqLightOfGuidance: type = MiracleType.LightOfGuidance; return true;
                case SeqSacredFlame:     type = MiracleType.SacredFlame;     return true;
                case SeqAegisOfFaith:    type = MiracleType.AegisOfFaith;    return true;
                case SeqCleansingRite:   type = MiracleType.CleansingRite;   return true;
                default:                                                      return false;
            }
        }

        // ── Trait gate ─────────────────────────────────────────────────────────
        public static bool MeetsGraceGate(MiracleGate gate, int honor, int mercy, int generosity)
        {
            switch (gate)
            {
                case MiracleGate.None:       return true;
                case MiracleGate.OneVirtue:  return honor >= 1 || mercy >= 1 || generosity >= 1;
                case MiracleGate.AllVirtues: return honor >= 1 && mercy >= 1 && generosity >= 1;
                default:                     return false;
            }
        }

        // ── Point gain ─────────────────────────────────────────────────────────
        // Grace scales with virtue; a hero with no virtue gains nothing.
        public static int GraceGain(int honor, int mercy, int generosity)
        {
            float avg = (honor + mercy + generosity) / 3f;
            int gain  = (int)Math.Round(Math.Max(0f, avg) * 4f);
            return Math.Min(4, gain);
        }

        // ── NPC use chances ────────────────────────────────────────────────────
        public static double NpcBattleUseChance(bool isPriest) => isPriest ? 0.004 : 0.001;
        public static double NpcDailyUseChance(bool isPriest)  => isPriest ? 0.05  : 0.01;

        // ── Battle effect magnitudes ───────────────────────────────────────────
        public const float RepelAshenRadius     = 10f;
        public const float RepelAshenDamage     = 45f;

        public const float RadiantMendSelfFrac   = 0.35f;
        public const float RadiantMendAllyFrac   = 0.18f;
        public const float RadiantMendAllyRadius = 8f;

        public const float GuidanceBattleMorale   = 20f;
        public const float GuidanceCampaignMorale = 30f;
        public const float GuidanceSpeedMult      =  1.15f;
        public const float GuidanceSpeedDurSec    = 15f;
        public const float GuidanceSpeedRadius    = 12f;

        public const float SacredFlameDurationSec = 25f;
        public const float SacredFlameBonusDamage = 10f;

        public const float AegisResistFrac  = 0.30f;
        public const float AegisDurationSec = 18f;

        public const float CleansingRiteRadius = 8f;
        public const float CleansingRiteDamage = 30f;
    }
}
