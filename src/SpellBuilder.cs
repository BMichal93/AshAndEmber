// =============================================================================
// LIFE & DEATH MAGIC — SpellBuilder.cs
// Parses the two-part input buffer (forms + effects) into a SpellCast and
// dispatches it to the appropriate SpellEffects execution method.
//
// BUFFER STRUCTURE
//   _formBuffer  — all direction keys pressed before Break
//   _effectBuffer — all direction keys pressed after Break
//
// FORMS (consistent form buffer → one of Blast/Aura/Barrier/Burst; mixed → fumble)
//   U = Blast   (cone, 2m per U)
//   L = Aura    (expanding cloud, +1 node per L)
//   R = Barrier (wall, 1 node per R)
//   D = Burst   (circle on caster, 2m radius per D)
//
// EFFECTS (effect buffer, stackable)
//   U = Vitality  — 5 dmg per U (Red visual)
//   L = Force     — 2m push per L (Blue visual)
//   R = Will-Break — -3 morale per R (Yellow visual)
//   D = Reverse   — flips all effects (White/pastel visual)
// =============================================================================

using System;
using System.Linq;

namespace ColoursOfCalradia
{
    public enum SpellForm { None, Blast, Aura, Barrier, Burst }

    public class SpellCast
    {
        public SpellForm Form;
        public int       FormCount;
        public bool      IsFumble;

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
            if (IsFumble)  return "Fumble — mixed form inputs.";
            if (!HasAnyEffect) return "No effects specified.";

            var parts = new System.Collections.Generic.List<string>();
            if (DamageCount > 0)
                parts.Add(Reversed
                    ? $"+{DamageCount * 5} healing (Reversed Vitality)"
                    : $"{DamageCount * 5} damage (Vitality)");
            if (PushCount > 0)
                parts.Add(Reversed
                    ? $"{PushCount * 2}m pull (Reversed Force)"
                    : $"{PushCount * 2}m push (Force)");
            if (MoraleCount > 0)
                parts.Add(Reversed
                    ? $"+{MoraleCount * 3} morale bonus (Reversed Will-Break)"
                    : $"-{MoraleCount * 3} morale (Will-Break)");
            return string.Join(", ", parts);
        }

        public string FormSummary()
        {
            switch (Form)
            {
                case SpellForm.Blast:   return $"Blast ({FormCount * 2}m range cone, 37°)";
                case SpellForm.Aura:    return $"Aura ({FormCount} nodes, radius {FormCount * 2}m)";
                case SpellForm.Barrier: return $"Barrier ({FormCount} node{(FormCount > 1 ? "s" : "")})";
                case SpellForm.Burst:   return $"Burst ({FormCount * 2}m radius)";
                default:                return "Unknown form";
            }
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

            // ── Parse form ────────────────────────────────────────────────────
            if (string.IsNullOrEmpty(formBuffer))
            {
                cast.IsFumble = true;
                return cast;
            }

            char firstForm = formBuffer[0];
            bool formMixed = formBuffer.Any(c => c != firstForm);

            if (formMixed)
            {
                cast.IsFumble = true;
                return cast;
            }

            cast.FormCount = formBuffer.Length;
            switch (firstForm)
            {
                case 'U': cast.Form = SpellForm.Blast;   break;
                case 'L': cast.Form = SpellForm.Aura;    break;
                case 'R': cast.Form = SpellForm.Barrier; break;
                case 'D': cast.Form = SpellForm.Burst;   break;
                default:  cast.IsFumble = true; return cast;
            }

            // ── Parse effects ─────────────────────────────────────────────────
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
        /// Execute a parsed SpellCast. Returns false if nothing happened.
        /// </summary>
        public static bool Execute(SpellCast cast, bool inMission)
        {
            if (cast == null || cast.IsFumble) return false;
            if (!cast.HasAnyEffect) return false;

            if (inMission)
            {
                switch (cast.Form)
                {
                    case SpellForm.Blast:   SpellEffects.ExecuteBlast(cast);   break;
                    case SpellForm.Aura:    SpellEffects.ExecuteAura(cast);    break;
                    case SpellForm.Barrier: SpellEffects.ExecuteBarrier(cast); break;
                    case SpellForm.Burst:   SpellEffects.ExecuteBurst(cast);   break;
                    default: return false;
                }
                return true;
            }
            // Campaign map spells are handled by TalentSystem, not SpellBuilder
            return false;
        }
    }
}
