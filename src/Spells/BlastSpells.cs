// =============================================================================
// LIFE & DEATH MAGIC — BlastSpells.cs
// BLAST FORM: forward cone, 2m range per U input, 37° half-angle (dot 0.80).
// Effects are applied to all agents in the cone regardless of team.
// Player: enemies only; NPC version accepts a casterTeam parameter.
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

            float range = Math.Max(4f, cast.FormCount * 2f);
            Vec3  fwd   = caster.LookDirection.NormalizedCopy();
            // Project forward vector to horizontal for cone test — vertical pitch would otherwise
            // shrink the apparent cone angle when looking slightly up or down.
            Vec3  fwdH  = new Vec3(fwd.x, fwd.y, 0f);
            if (fwdH.Length > 0.01f) fwdH = fwdH.NormalizedCopy();

            // Gather targets — enemies of the caster
            var targets = new List<Agent>();
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount || a == caster) continue;
                    if (casterTeam != null && a.Team == casterTeam) continue; // skip allies
                    // Check range to agent mid-body (foot position + 0.8 m) so crouched or
                    // uphill targets aren't missed; cone test uses horizontal angle only.
                    Vec3 toMid = (a.Position + new Vec3(0f, 0f, 0.8f)) - caster.Position;
                    if (toMid.Length > range) continue;
                    Vec3 toH = new Vec3(toMid.x, toMid.y, 0f);
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
                return;
            }

            int affected = 0;
            foreach (Agent a in targets)
            {
                try
                {
                    ApplyEffectsToAgent(a, cast, caster, applyPush: true, applyPull: true);
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
        }
    }
}
