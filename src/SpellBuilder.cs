// =============================================================================
// LIFE & DEATH MAGIC — SpellBuilder.cs
// Parses the two-part input buffer (forms + effects) into a SpellCast and
// dispatches it to the appropriate SpellEffects execution method.
//
// BUFFER STRUCTURE
//   _formBuffer  — all direction keys pressed before Break
//   _effectBuffer — all direction keys pressed after Break
//
// FORMS (form buffer — mix freely, all fire simultaneously)
//   U = Blast   (cone, 2.5m per U)
//   L = Wave    (advancing fire grid, +2m per L)
//   R = Barrier (wall, 1 node per R)
//   D = Burst   (circle on caster, 2.5m radius per D)
//
// EFFECTS (effect buffer, stackable)
//   U = Flame     — 12 dmg per U (Red visual)
//   L = Surge     — 4m push per L (Blue visual)
//   R = Smoulder  — 7 morale per R (Yellow visual)
//   D = Reverse   — flips all effects (White/pastel visual)
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace AshAndEmber
{
    public enum SpellForm { None, Blast, Aura, Barrier, Burst }

    public class SpellCast
    {
        // ── Multi-form counts (set by Parse for player casts) ─────────────────
        public int BlastCount;
        public int WaveCount;
        public int BarrierCount;
        public int BurstCount;

        // ── Legacy single-form fields (used by NPC cast constructors) ────────
        public SpellForm Form;
        private int _formCountOverride;   // non-zero only when set directly by NPC code

        // Total form inputs: multi-form-aware
        public int FormCount
        {
            get
            {
                int multi = BlastCount + WaveCount + BarrierCount + BurstCount;
                return multi > 0 ? multi : _formCountOverride;
            }
            set { _formCountOverride = value; }
        }

        public bool IsFumble;

        public int  DamageCount;   // U effects
        public int  PushCount;     // L effects
        public int  MoraleCount;   // R effects
        public bool Reversed;      // D effect present

        public bool HasAnyEffect => DamageCount > 0 || PushCount > 0 || MoraleCount > 0 || Reversed;
        public int  TotalInputs  => FormCount + DamageCount + PushCount + MoraleCount + (Reversed ? 1 : 0);

        public ColorSchool VisualColor =>
            ColorSchoolData.ComputeEffectColor(DamageCount, PushCount, MoraleCount, Reversed);

        public int AgingDays(bool hasBattleMageTalent) =>
            AgingSystem.ComputeBattleAgingCost(TotalInputs, hasBattleMageTalent);

        public string EffectSummary()
        {
            if (IsFumble)  return "Fumble — input error.";
            if (!HasAnyEffect) return "No effects specified.";

            var parts = new List<string>();
            if (DamageCount > 0)
                parts.Add(Reversed
                    ? $"+{DamageCount * 25} kindled (Reversed Flame)"
                    : $"{DamageCount * 25} flame (Flame)");
            if (PushCount > 0)
                parts.Add(Reversed
                    ? $"{PushCount * 6}m draw (Reversed Surge)"
                    : $"{PushCount * 6}m surge (+{PushCount * 5} kinetic)");
            if (MoraleCount > 0)
                parts.Add(Reversed
                    ? $"+{MoraleCount * 15} kindled morale (Reversed Smoulder)"
                    : $"-{MoraleCount * 15} smoulder (+{MoraleCount * 8} dmg)");
            return string.Join(", ", parts);
        }

        public string FormSummary()
        {
            int multi = BlastCount + WaveCount + BarrierCount + BurstCount;
            if (multi == 0)
            {
                // Legacy single-form path (NPC casts)
                switch (Form)
                {
                    case SpellForm.Blast:   return $"Blast ({_formCountOverride * 2.5f:F0}m range cone, 37°)";
                    case SpellForm.Aura:
                        int wGs = 3 + Math.Max(0, (_formCountOverride - 5) / 5);
                        float wR = Math.Max(3f, _formCountOverride * 2f - 1f);
                        return $"Wave ({wGs}×{wGs} grid, {wR:F0}m)";
                    case SpellForm.Barrier: return $"Barrier ({_formCountOverride} node{(_formCountOverride > 1 ? "s" : "")})";
                    case SpellForm.Burst:   return $"Burst ({_formCountOverride * 2.5f:F0}m radius)";
                    default:                return "Unknown form";
                }
            }

            var parts = new List<string>();
            if (BlastCount > 0)   parts.Add($"Blast ({BlastCount * 2.5f:F0}m cone)");
            if (WaveCount > 0)
            {
                int gs = 3 + Math.Max(0, (WaveCount - 5) / 5);
                float r = Math.Max(3f, WaveCount * 2f - 1f);
                parts.Add($"Wave ({gs}×{gs}, {r:F0}m)");
            }
            if (BarrierCount > 0) parts.Add($"Barrier ({BarrierCount} node{(BarrierCount > 1 ? "s" : "")})");
            if (BurstCount > 0)   parts.Add($"Burst ({BurstCount * 2.5f:F0}m radius)");
            return string.Join(" + ", parts);
        }
    }

    public static class SpellBuilder
    {
        /// <summary>
        /// Parse the two raw buffers into a SpellCast.
        /// formBuffer  — keys pressed before Break.
        /// effectBuffer — keys pressed after Break.
        /// Mixed form types are allowed; all fire simultaneously.
        /// </summary>
        public static SpellCast Parse(string formBuffer, string effectBuffer)
        {
            var cast = new SpellCast();

            if (string.IsNullOrEmpty(formBuffer))
            {
                cast.IsFumble = true;
                return cast;
            }

            // Count each form direction independently — mixed forms all fire
            foreach (char c in formBuffer)
            {
                switch (c)
                {
                    case 'U': cast.BlastCount++;   break;
                    case 'L': cast.WaveCount++;    break;
                    case 'R': cast.BarrierCount++; break;
                    case 'D': cast.BurstCount++;   break;
                    default:  cast.IsFumble = true; return cast;
                }
            }

            // Set legacy Form field for single-form casts (backward compat)
            int activeForms = (cast.BlastCount > 0 ? 1 : 0) + (cast.WaveCount > 0 ? 1 : 0)
                            + (cast.BarrierCount > 0 ? 1 : 0) + (cast.BurstCount > 0 ? 1 : 0);
            if (activeForms == 1)
            {
                if (cast.BlastCount > 0)   cast.Form = SpellForm.Blast;
                else if (cast.WaveCount > 0)    cast.Form = SpellForm.Aura;
                else if (cast.BarrierCount > 0) cast.Form = SpellForm.Barrier;
                else if (cast.BurstCount > 0)   cast.Form = SpellForm.Burst;
            }

            // Parse effects
            if (!string.IsNullOrEmpty(effectBuffer))
            {
                foreach (char c in effectBuffer)
                {
                    switch (c)
                    {
                        case 'U': cast.DamageCount++;  break;
                        case 'L': cast.PushCount++;    break;
                        case 'R': cast.MoraleCount++;  break;
                        case 'D': cast.Reversed = true; break;
                    }
                }
            }

            return cast;
        }

        /// <summary>
        /// Execute a parsed SpellCast. Returns false if nothing happened (no aging cost).
        /// For a barrier-only cast that toggles off, returns false (dispel = no aging).
        /// </summary>
        public static bool Execute(SpellCast cast, bool inMission)
        {
            if (cast == null || cast.IsFumble) return false;
            if (!cast.HasAnyEffect) return false;

            if (inMission)
            {
                int multi = cast.BlastCount + cast.WaveCount + cast.BarrierCount + cast.BurstCount;

                if (multi == 0)
                {
                    // Legacy single-form path (NPC-constructed SpellCasts with Form set)
                    switch (cast.Form)
                    {
                        case SpellForm.Blast:   SpellEffects.ExecuteBlast(cast);   break;
                        case SpellForm.Aura:    SpellEffects.ExecuteWave(cast);    break;
                        case SpellForm.Barrier: return SpellEffects.ExecuteBarrier(cast);
                        case SpellForm.Burst:   SpellEffects.ExecuteBurst(cast);   break;
                        default: return false;
                    }
                    return true;
                }

                // Multi-form: fire all active forms
                bool anyFired = false;
                bool barrierResult = true;

                if (cast.BlastCount > 0)
                {
                    SpellEffects.ExecuteBlast(cast);
                    anyFired = true;
                }
                if (cast.WaveCount > 0)
                {
                    SpellEffects.ExecuteWave(cast);
                    anyFired = true;
                }
                if (cast.BarrierCount > 0)
                {
                    barrierResult = SpellEffects.ExecuteBarrier(cast);
                    // Barrier-only dispel returns false (no aging), but if other forms also fired, charge normally
                    if (anyFired || cast.BurstCount > 0) barrierResult = true;
                    anyFired = true;
                }
                if (cast.BurstCount > 0)
                {
                    SpellEffects.ExecuteBurst(cast);
                    anyFired = true;
                }

                // If barrier was the only form and it dispelled, return false
                if (cast.BarrierCount > 0 && cast.BlastCount == 0 && cast.WaveCount == 0 && cast.BurstCount == 0)
                    return barrierResult;

                return anyFired;
            }
            // Campaign map spells are handled by TalentSystem, not SpellBuilder
            return false;
        }
    }
}
