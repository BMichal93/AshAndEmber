// =============================================================================
// ASH AND EMBER — Miracles/MiracleMath.cs
//
// Pure numeric logic for the miracle system — no TaleWorlds types, fully
// testable. Sequences and effect magnitudes live here so the input handler and
// effects code never contain raw numbers.
//
// Access is now gated by PERSONALITY TRAIT (handled in MiracleEffects, which can
// read Hero traits), not by the old honor/mercy/generosity virtue gates — those
// are removed. Grace GATHERING (GraceGain) is unchanged.
// =============================================================================

using System;

namespace AshAndEmber
{
    public static class MiracleMath
    {
        // ── Counters ───────────────────────────────────────────────────────────
        public const int GraceColdCap        = 10;
        // Effective Grace ceiling — the base cap plus any Abundant Grace devotion.
        public static int GraceCap() => GraceColdCap + MiracleTalents.GraceCapBonus;
        public const int PrayerCooldownDays  =  3;   // Pray for Grace
        public const int WardingCooldownDays =  7;   // Warding Seal

        // ── Miracle sequences (6 chars, W=U A=L D=R S=D) ──────────────────────
        // Two per personality trait: a battle prayer and a map prayer.
        public const string SeqMercyMend      = "UUDDLR"; // Mercy  — Radiant Mending
        public const string SeqMercyRelief    = "RULRUU"; // Mercy  — The Mending Road
        public const string SeqValorFury      = "UDUDUD"; // Valor  — Light of Valour
        public const string SeqValorMarch     = "ULULUL"; // Valor  — The Long March
        public const string SeqHonorAegis     = "LLUURR"; // Honor  — Aegis of the Oath
        public const string SeqHonorOath      = "LRLRLR"; // Honor  — The Sworn Word
        public const string SeqGraceBlessing  = "LLRRUU"; // Gen.   — Shared Light
        public const string SeqGraceBounty    = "DDUULL"; // Gen.   — The Open Hand
        public const string SeqInsightPyre    = "RRUUDD"; // Calc.  — Pyre of Judgement
        public const string SeqInsightSight   = "RDRDRD"; // Calc.  — Far-Sight

        public const int SequenceLength = 6;

        // Returns true and the matched MiracleType if the 6-char input is known.
        public static bool TryMatchSequence(string input, out MiracleType type)
        {
            type = MiracleType.MercyMend;
            if (string.IsNullOrEmpty(input) || input.Length != SequenceLength) return false;
            switch (input)
            {
                case SeqMercyMend:     type = MiracleType.MercyMend;     return true;
                case SeqMercyRelief:   type = MiracleType.MercyRelief;   return true;
                case SeqValorFury:     type = MiracleType.ValorFury;     return true;
                case SeqValorMarch:    type = MiracleType.ValorMarch;    return true;
                case SeqHonorAegis:    type = MiracleType.HonorAegis;    return true;
                case SeqHonorOath:     type = MiracleType.HonorOath;     return true;
                case SeqGraceBlessing: type = MiracleType.GraceBlessing; return true;
                case SeqGraceBounty:   type = MiracleType.GraceBounty;   return true;
                case SeqInsightPyre:   type = MiracleType.InsightPyre;   return true;
                case SeqInsightSight:  type = MiracleType.InsightSight;  return true;
                default:                                                  return false;
            }
        }

        // ── Trait gate ─────────────────────────────────────────────────────────
        // A miracle answers once its granting personality trait is at +1 or higher.
        public const int TraitGateLevel = 1;
        public static bool MeetsTraitGate(int traitLevel) => traitLevel >= TraitGateLevel;

        // ── Point gain (Sanctuary — unchanged) ─────────────────────────────────
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

        // ── NPC battle miracle selection ──────────────────────────────────────
        // Grace flows from an unlimited wellspring, so a devout NPC never rations
        // it — but he still answers the MOMENT rather than praying at random. In
        // priority: mend himself when hurt, call down judgement on the Ashen, ward
        // and mend a wounded line, raise a shield under heavy pressure, and otherwise
        // rally or bless. `roll` in [0,1) only breaks the two calmest ties.
        public static MiracleType ChooseBattleMiracle(bool selfHurt, int alliesHurtNear,
            bool enemyPressingSelf, bool ashenAdjacent, float roll)
        {
            if (selfHurt)            return MiracleType.MercyMend;      // self-preservation first
            if (ashenAdjacent)       return MiracleType.InsightPyre;    // judgement on the cold
            if (alliesHurtNear >= 2) return MiracleType.GraceBlessing;  // shared light — ward + heal many
            if (alliesHurtNear >= 1) return MiracleType.MercyMend;      // mend the one who bleeds
            if (enemyPressingSelf)   return MiracleType.HonorAegis;     // shield under the press
            // Nothing pressing — steel the line: rally, or bless if the roll favours it.
            return roll < 0.5f ? MiracleType.ValorFury : MiracleType.GraceBlessing;
        }

        // ── Battle effect magnitudes (reused effects) ──────────────────────────
        public const float RadiantMendSelfFrac   = 0.35f;
        public const float RadiantMendAllyFrac   = 0.18f;
        public const float RadiantMendAllyRadius = 8f;

        public const float GuidanceBattleMorale   = 20f;
        public const float GuidanceCampaignMorale = 30f;
        public const float GuidanceSpeedMult      =  1.15f;
        public const float GuidanceSpeedDurSec    = 15f;
        public const float GuidanceSpeedRadius    = 12f;

        public const float AegisResistFrac  = 0.30f;
        public const float AegisDurationSec = 18f;

        public const float PyreJudgementReach  = 8f;   // metres ahead the pillar falls
        public const float PyreJudgementRadius = 6f;   // blast radius at the impact point
        public const float PyreJudgementDamage = 60f;  // HP seared from each enemy struck

        public const float HallowedGroundRadius   = 8f;
        public const float HallowedGroundHealFrac = 0.15f;

        // Retired-effect constants — the RepelAshen / SacredFlame / CleansingRite
        // battle effects are no longer granted by any trait, but their (now
        // never-dispatched) implementations still compile against these.
        public const float RepelAshenRadius       = 10f;
        public const float RepelAshenDamage       = 45f;
        public const float SacredFlameDurationSec = 25f;
        public const float SacredFlameBonusDamage = 10f;
        public const float CleansingRiteRadius    = 8f;
        public const float CleansingRiteDamage    = 30f;

        // ── Map effect magnitudes (new) ────────────────────────────────────────
        public const int   ReliefHealCount     = 6;    // wounded healed by The Mending Road
        public const float OathLoyaltyGain      = 15f;  // loyalty/security restored by The Sworn Word
        public const int   OathRelationGain     = 4;    // relation gained when not in a settlement
        public const int   BountyFood           = 30;   // grain added by The Open Hand
        public const float BountyMorale         = 20f;
        public const float SightMorale          = 10f;  // morale from Far-Sight
        public const float SightRadius          = 30f;  // map units scanned for threats
    }
}
