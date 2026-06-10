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
//   L = Missile (projectile, 3m range per L, explodes: 1+L m radius)
//   R = Barrier (wall, 1 node per R)
//   D = Burst   (circle on caster, 2.5m radius per D)
//
// EFFECTS (effect buffer, stackable)
//   U/L/R = Damage  — 25 fire dmg each (Red visual), hits enemies
//   D     = Restore — 15 heal per D (White visual), heals allies; Burst also heals caster
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace AshAndEmber
{
    public enum SpellForm { None, Blast, Missile, Barrier, Burst }

    public class SpellCast
    {
        // ── Multi-form counts (set by Parse for player casts) ─────────────────
        public int BlastCount;
        public int MissileCount;
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
                int multi = BlastCount + MissileCount + BarrierCount + BurstCount;
                return multi > 0 ? multi : _formCountOverride;
            }
            set { _formCountOverride = value; }
        }

        public bool IsFumble;

        // ── Lost Form flags (set by SpellBuilder.Parse when the talent is owned) ──
        public bool UsingLostBlast;
        public bool UsingLostMissile;
        public bool UsingLostBarrier;
        public bool UsingLostBurst;

        public int DamageCount;    // U effects — fire damage to enemies
        public int RestoreCount;   // D effects — healing to allies (and caster on Burst)

        public bool HasAnyEffect => DamageCount > 0 || RestoreCount > 0;
        public int  TotalInputs  => FormCount + DamageCount + RestoreCount;

        // Set by NPC execution helpers to override the default school-from-effects logic
        // (e.g. Ashen lords emit cold-blue visuals regardless of damage/restore mix).
        public ColorSchool? OverrideVisualColor = null;

        public ColorSchool VisualColor
        {
            get
            {
                if (OverrideVisualColor.HasValue) return OverrideVisualColor.Value;
                if (DamageCount > 0 && RestoreCount > 0) return ColorSchool.Orange;
                if (DamageCount > 0)  return ColorSchool.Red;
                if (RestoreCount > 0) return ColorSchool.White;
                return ColorSchool.Red;
            }
        }

        public int AgingDays(bool hasBattleMageTalent) =>
            AgingSystem.ComputeBattleAgingCost(TotalInputs, hasBattleMageTalent);

        public string EffectSummary()
        {
            if (IsFumble)       return "Fumble — input error.";
            if (!HasAnyEffect)  return "No effects specified.";

            var parts = new List<string>();
            if (DamageCount  > 0) parts.Add($"{DamageCount * 25} damage");
            if (RestoreCount > 0) parts.Add($"+{RestoreCount * 15} restore");
            return string.Join(", ", parts);
        }

        public string FormSummary()
        {
            int multi = BlastCount + MissileCount + BarrierCount + BurstCount;
            if (multi == 0)
            {
                switch (Form)
                {
                    case SpellForm.Blast:   return $"Blast ({_formCountOverride * 2.5f:F0}m cone)";
                    case SpellForm.Missile:
                        float mR  = Math.Max(8f, _formCountOverride * 3f);
                        float mBR = 1f + _formCountOverride;
                        return $"Missile ({mR:F0}m, {mBR:F0}m blast)";
                    case SpellForm.Barrier: return $"Barrier ({_formCountOverride} node{(_formCountOverride > 1 ? "s" : "")})";
                    case SpellForm.Burst:   return $"Burst ({_formCountOverride * 2.5f:F0}m radius)";
                    default:                return "Unknown form";
                }
            }

            var parts = new List<string>();
            if (BlastCount > 0)    parts.Add($"Blast ({BlastCount * 2.5f:F0}m cone)");
            if (MissileCount > 0)
            {
                float r  = Math.Max(8f, MissileCount * 3f);
                float br = 1f + MissileCount;
                parts.Add($"Missile ({r:F0}m, {br:F0}m blast)");
            }
            if (BarrierCount > 0)  parts.Add($"Barrier ({BarrierCount} node{(BarrierCount > 1 ? "s" : "")})");
            if (BurstCount > 0)    parts.Add($"Burst ({BurstCount * 2.5f:F0}m radius)");
            return string.Join(" + ", parts);
        }
    }

    public static class SpellBuilder
    {
        /// <summary>
        /// Parse the two raw buffers into a SpellCast.
        /// formBuffer  — keys pressed before Break.
        /// effectBuffer — keys pressed after Break.
        /// </summary>
        public static SpellCast Parse(string formBuffer, string effectBuffer)
        {
            var cast = new SpellCast();

            if (string.IsNullOrEmpty(formBuffer))
            {
                cast.IsFumble = true;
                return cast;
            }

            foreach (char c in formBuffer)
            {
                switch (c)
                {
                    case 'U': cast.BlastCount++;   break;
                    case 'L': cast.MissileCount++; break;
                    case 'R': cast.BarrierCount++; break;
                    case 'D': cast.BurstCount++;   break;
                    default:  cast.IsFumble = true; return cast;
                }
            }

            // Set legacy Form field for single-form casts (backward compat)
            int activeForms = (cast.BlastCount > 0 ? 1 : 0) + (cast.MissileCount > 0 ? 1 : 0)
                            + (cast.BarrierCount > 0 ? 1 : 0) + (cast.BurstCount > 0 ? 1 : 0);
            if (activeForms == 1)
            {
                if      (cast.BlastCount   > 0) cast.Form = SpellForm.Blast;
                else if (cast.MissileCount > 0) cast.Form = SpellForm.Missile;
                else if (cast.BarrierCount > 0) cast.Form = SpellForm.Barrier;
                else if (cast.BurstCount   > 0) cast.Form = SpellForm.Burst;
            }

            // Effects: U/L/R = Damage, D = Restore.
            if (!string.IsNullOrEmpty(effectBuffer))
            {
                foreach (char c in effectBuffer)
                {
                    switch (c)
                    {
                        case 'U':
                        case 'L':
                        case 'R': cast.DamageCount++;  break;
                        case 'D': cast.RestoreCount++; break;
                    }
                }
            }

            if (cast.BlastCount   > 0) cast.UsingLostBlast   = TalentSystem.Has(TalentId.LostBlast);
            if (cast.MissileCount > 0) cast.UsingLostMissile = TalentSystem.Has(TalentId.LostMissile);
            if (cast.BarrierCount > 0) cast.UsingLostBarrier = TalentSystem.Has(TalentId.LostBarrier);
            if (cast.BurstCount   > 0) cast.UsingLostBurst   = TalentSystem.Has(TalentId.LostBurst);

            if (MageKnowledge.IsAshen)
                cast.OverrideVisualColor = ColorSchool.Ashen;

            return cast;
        }

        /// <summary>
        /// Execute a parsed SpellCast. Returns false if nothing happened (no aging cost).
        /// </summary>
        public static bool Execute(SpellCast cast, bool inMission)
        {
            if (cast == null || cast.IsFumble) return false;
            if (!cast.HasAnyEffect) return false;

            if (inMission)
            {
                SpellEffects.ResetImmolateKill();
                int multi = cast.BlastCount + cast.MissileCount + cast.BarrierCount + cast.BurstCount;

                if (multi == 0)
                {
                    switch (cast.Form)
                    {
                        case SpellForm.Blast:   SpellEffects.ExecuteBlast(cast);   break;
                        case SpellForm.Missile: SpellEffects.ExecuteMissile(cast); break;
                        case SpellForm.Barrier: return SpellEffects.ExecuteBarrier(cast);
                        case SpellForm.Burst:   SpellEffects.ExecuteBurst(cast);   break;
                        default: return false;
                    }
                    return true;
                }

                bool anyFired     = false;
                bool barrierResult = true;

                if (cast.BlastCount > 0)
                {
                    SpellEffects.ExecuteBlast(cast);
                    anyFired = true;
                }
                if (cast.MissileCount > 0)
                {
                    SpellEffects.ExecuteMissile(cast);
                    anyFired = true;
                }
                if (cast.BarrierCount > 0)
                {
                    barrierResult = SpellEffects.ExecuteBarrier(cast);
                    if (anyFired || cast.BurstCount > 0) barrierResult = true;
                    anyFired = true;
                }
                if (cast.BurstCount > 0)
                {
                    SpellEffects.ExecuteBurst(cast);
                    anyFired = true;
                }

                if (cast.BarrierCount > 0 && cast.BlastCount == 0 && cast.MissileCount == 0 && cast.BurstCount == 0)
                    return barrierResult;

                return anyFired;
            }
            return false;
        }
    }
}
