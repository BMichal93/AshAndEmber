// =============================================================================
// ASH AND EMBER — Miracles/MiracleMath.cs
//
// Pure numeric logic for the miracle system — no TaleWorlds types, fully
// testable. Sequences, trait gates, effect magnitudes all live here so the
// input handler and effects code never contain raw numbers.
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
        public const int AltarCooldownDays   =  3;   // Embrace the Cold
        public const int InvokeCooldownDays  = 14;   // Invoke the Dark Tide

        // ── Miracle sequences (6 chars, W=U A=L D=R S=D) ──────────────────────
        public const string SeqRepelAshen      = "UUUURR";
        public const string SeqRadiantMending  = "UUDDLR";
        public const string SeqLightOfGuidance = "UDUDUD";
        public const string SeqSacredFlame     = "UURRDL";
        public const string SeqAegisOfFaith    = "LLUURR";

        public const string SeqAshenCurse      = "DDDDLL";
        public const string SeqDreadmending    = "DDUURR";
        public const string SeqDreadPresence   = "DLDLDR";
        public const string SeqFrostBrand      = "LLDRUU";
        public const string SeqShadowShroud    = "RRDDLL";

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
                case SeqAshenCurse:      type = MiracleType.AshenCurse;      return true;
                case SeqDreadmending:    type = MiracleType.Dreadmending;    return true;
                case SeqDreadPresence:   type = MiracleType.DreadPresence;   return true;
                case SeqFrostBrand:      type = MiracleType.FrostBrand;      return true;
                case SeqShadowShroud:    type = MiracleType.ShadowShroud;    return true;
                default:                                                      return false;
            }
        }

        // ── Trait gates ────────────────────────────────────────────────────────
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

        // Cold gates are the mirror image: negative traits are required.
        public static bool MeetsColdGate(MiracleGate gate, int honor, int mercy, int generosity)
        {
            switch (gate)
            {
                case MiracleGate.None:       return true;
                case MiracleGate.OneVirtue:  return honor <= -1 || mercy <= -1 || generosity <= -1;
                case MiracleGate.AllVirtues: return honor <= -1 && mercy <= -1 && generosity <= -1;
                default:                     return false;
            }
        }

        // ── Point gains ────────────────────────────────────────────────────────
        // Grace scales positively with virtue. Baseline 1 ensures any hero can pray.
        public static int GraceGain(int honor, int mercy, int generosity)
        {
            float avg = (honor + mercy + generosity) / 3f;
            int gain  = (int)Math.Round(1f + Math.Max(0f, avg) * 3f);
            return Math.Max(1, Math.Min(4, gain));
        }

        // Cold scales inversely — dishonour and cruelty feed the stone.
        public static int ColdGain(int honor, int mercy, int generosity)
        {
            float avg = (honor + mercy + generosity) / 3f;
            int gain  = (int)Math.Round(1f + Math.Max(0f, -avg) * 3f);
            return Math.Max(1, Math.Min(4, gain));
        }

        // ── NPC use chances ────────────────────────────────────────────────────
        public static double NpcBattleUseChance(bool isPriest) => isPriest ? 0.004 : 0.001;
        public static double NpcDailyUseChance(bool isPriest)  => isPriest ? 0.05  : 0.01;

        // ── Battle effect magnitudes ───────────────────────────────────────────
        public const float RepelAshenRadius     = 10f;
        public const float RepelAshenDamage     = 45f;

        public const float RadiantMendSelfFrac  = 0.35f;
        public const float RadiantMendAllyFrac  = 0.18f;
        public const float RadiantMendAllyRadius= 8f;

        public const float GuidanceBattleMorale  = 20f;
        public const float GuidanceCampaignMorale= 30f;

        public const float SacredFlameDurationSec  = 25f;
        public const float SacredFlameBonusDamage  = 10f;

        public const float AegisBonusHP = 40f;

        public const float CurseRadius   = 9f;
        public const float CurseDamage   = 40f;

        public const float DreadmendRadius= 6f;
        public const float DreadmendFrac  = 0.25f;

        public const float DreadPresenceRadius     = 10f;
        public const float DreadPresenceMorale     = -35f;
        public const float DreadPresenceDurationSec= 18f;
        public const float DreadPresenceSpeedMult  = 0.75f;

        public const float FrostBrandDurationSec = 20f;
        public const float FrostBrandChillSec    =  8f;
        public const float FrostBrandSpeedMult   = 0.70f;

        public const float ShadowShroudDurationSec = 20f;
        public const float ShadowShroudResistFrac  = 0.40f;
    }
}
