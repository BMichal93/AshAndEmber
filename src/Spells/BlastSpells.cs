// =============================================================================
// LIFE & DEATH MAGIC — BlastSpells.cs
// BLAST FORM: forward cone, 2.5m range per U input, ~49° half-angle (dot 0.65).
// Hits enemies when DamageCount > 0, allies when RestoreCount > 0.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static partial class SpellEffects
    {
        // ── Player Blast ───────────────────────────────────────────────────────
        public static void ExecuteBlast(SpellCast cast)
        {
            if (Agent.Main == null) return;
            ExecuteBlastFromAgent(Agent.Main, cast, Agent.Main.Team);
        }

        // ── Shared implementation (player + NPC) ──────────────────────────────
        internal static void ExecuteBlastFromAgent(Agent caster, SpellCast cast, Team casterTeam)
        {
            if (caster == null || !caster.IsActive() || Mission.Current == null) return;

            int blastCnt = cast.BlastCount > 0 ? cast.BlastCount : cast.FormCount;
            float range = Math.Max(4f, blastCnt * 2.5f);
            Vec3  fwd   = caster.LookDirection.NormalizedCopy();
            // Project forward vector to horizontal — vertical pitch would otherwise
            // shrink the apparent cone angle when looking slightly up or down.
            Vec3  fwdH  = new Vec3(fwd.x, fwd.y, 0f);
            if (fwdH.Length > 0.01f) fwdH = fwdH.NormalizedCopy();

            bool wantDmg  = cast.DamageCount  > 0;
            bool wantHeal = cast.RestoreCount > 0;

            var targets = new List<Agent>();
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount || a == caster) continue;
                    bool isEnemy = casterTeam != null && a.Team != null && a.Team != casterTeam;
                    bool isAlly  = casterTeam != null && a.Team != null && a.Team == casterTeam;
                    if (!((wantDmg && isEnemy) || (wantHeal && isAlly))) continue;
                    // Horizontal range check so mounted riders at elevation are not missed.
                    Vec3 toH = new Vec3(a.Position.x - caster.Position.x, a.Position.y - caster.Position.y, 0f);
                    if (toH.Length > range) continue;
                    if (toH.Length < 0.01f) continue;
                    if (Vec3.DotProduct(fwdH, toH.NormalizedCopy()) < 0.65f) continue;
                    targets.Add(a);
                }
            }
            catch { }

            ColorSchool glowColor = cast.VisualColor;
            SpawnConeLights(caster.Position, fwd, glowColor, 3f);
            TryCastSound(caster.Position, glowColor);
            TryCastAnimation(caster);

            if (targets.Count == 0)
            {
                if (caster == Agent.Main)
                    InformationManager.DisplayMessage(new InformationMessage(
                        "Nothing in range.", new Color(0.7f, 0.7f, 0.7f)));
                else
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{caster.Name} — blast, no targets in range.", new Color(0.6f, 0.6f, 0.6f)));
                return;
            }

            int affected = 0;
            foreach (Agent a in targets)
            {
                try
                {
                    ApplyEffectsToAgent(a, cast, caster);
                    SpawnImpactBurst(a.Position, glowColor, 5f);
                    affected++;
                }
                catch { }
            }

            if (caster == Agent.Main)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{cast.FormSummary()} — {cast.EffectSummary()} — {affected} {(affected == 1 ? "target" : "targets")}.",
                    ColorSchoolData.GetMessageColor(glowColor)));
            }
            else
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{caster.Name} — blast strikes {affected} {(affected == 1 ? "target" : "targets")}.",
                    new Color(0.6f, 0.6f, 0.6f)));
            }
        }
    }
}
