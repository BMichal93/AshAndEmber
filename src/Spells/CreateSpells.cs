// =============================================================================
// LIFE & DEATH MAGIC — CreateSpells.cs
// BARRIER FORM: wall of nodes in front of caster, 1 node per R input.
// BURST FORM   : instant circle around caster, 2.5m radius per D input.
//   Burst heals the caster when RestoreCount > 0.
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
        private const string BarrierId = "spell_barrier";

        // ── BARRIER ───────────────────────────────────────────────────────────
        public static bool ExecuteBarrier(SpellCast cast)
        {
            Agent caster = Agent.Main;
            if (caster == null || !caster.IsActive()) return false;

            Vec3 fwd   = caster.LookDirection.NormalizedCopy();
            Vec3 right = new Vec3(-fwd.y, fwd.x, 0f).NormalizedCopy();
            int  count = Math.Max(1, cast.BarrierCount > 0 ? cast.BarrierCount : cast.FormCount);

            for (int i = 0; i < count; i++)
            {
                // Spread nodes left to right across the forward direction
                float offset = (i - (count - 1) * 0.5f) * 1.5f; // 1.5m = hit radius → solid wall, no gaps
                Vec3 pos = caster.Position + fwd * 3f + right * offset;
                AddBarrierNode(pos, cast, caster.Team);
            }

            ColorSchool col = cast.VisualColor;
            SpawnConeLights(caster.Position, fwd, col, 3f);
            TryCastSound(caster.Position, col);
            TryCastAnimation(caster);

            InformationManager.DisplayMessage(new InformationMessage(
                $"Barrier — {cast.EffectSummary()}.",
                ColorSchoolData.GetMessageColor(col)));
            return true;
        }

        private static void AddBarrierNode(Vec3 pos, SpellCast cast, Team casterTeam)
        {
            // Encode effects: high bits = damage, low bits = restore
            float token = cast.DamageCount * 1000f + cast.RestoreCount;
            var node = new AreaEffect
            {
                Id           = BarrierId,
                School       = cast.VisualColor,
                Position     = pos,
                Radius       = 1.5f,
                TickInterval = 0.5f,
                TickTimer    = 0.5f,
                Remaining    = cast.UsingLostBarrier ? 60f : 50f,
                Power        = token,
                CasterTeam   = casterTeam
            };
            // Persistent column of three lights — stay lit for the entire barrier lifetime.
            node.LightEntity  = SpawnAreaLight(pos,                          cast.VisualColor, 12f);
            node.LightEntity2 = SpawnAreaLight(pos + new Vec3(0f, 0f, 1f),  cast.VisualColor, 10f);
            node.LightEntity3 = SpawnAreaLight(pos + new Vec3(0f, 0f, 2f),  cast.VisualColor, 10f);
            if (cast.VisualColor != ColorSchool.Ashen)
            {
                SpawnTempFireParticle(pos,                          6f);
                SpawnTempFireParticle(pos + new Vec3(0f, 0f, 1f),  6f);
                SpawnTempFireParticle(pos + new Vec3(0f, 0f, 2f),  6f);
            }
            _areaEffects.Add(node);
        }

        // Called from AreaEffects.cs tick (every 0.5 s per node)
        // Regular barrier expires after 50 s; Lost Barrier after 60 s.
        internal static void TickBarrierNode(AreaEffect e)
        {
            if (Mission.Current == null) return;

            // Visuals: the column lights (LightEntity/2/3) and the initial fire particles are
            // already persistent for the barrier's whole lifetime, so we do NOT re-spawn lights
            // every 0.5 s tick — that flooded the scene with short-lived point lights and was the
            // main performance drain of long / multi-node walls. Fire is refreshed only on a slow
            // ~3 s cadence (DirTimer is otherwise unused by barriers) so the flames keep burning
            // continuously without churning particle entities ten times a second.
            e.DirTimer -= 0.5f;
            if (e.School != ColorSchool.Ashen && e.DirTimer <= 0f)
            {
                e.DirTimer = 3f;
                SpawnTempFireParticle(e.Position,                          3.5f);
                SpawnTempFireParticle(e.Position + new Vec3(0f, 0f, 1f),  3.5f);
                SpawnTempFireParticle(e.Position + new Vec3(0f, 0f, 2f),  3.5f);
            }

            int token   = (int)e.Power;
            int dmg     = token / 1000;
            int restore = token % 1000;

            foreach (Agent a in Mission.Current.Agents.ToList())
            {
                if (!a.IsActive() || a.IsMount) continue;

                // Horizontal distance so mounted riders at elevation are hit correctly
                Vec3 toH = new Vec3(a.Position.x - e.Position.x, a.Position.y - e.Position.y, 0f);
                float dist = toH.Length;

                bool isAlly  = e.CasterTeam != null && a.Team != null && a.Team == e.CasterTeam;
                bool isEnemy = e.CasterTeam != null && a.Team != null && a.Team != e.CasterTeam;

                // Warning zone: push enemies away from the barrier wall.
                // Extended to 5 m beyond radius; heroes get a gentler nudge.
                bool inWarningZone = dist > e.Radius && dist < e.Radius + 5f;
                if (inWarningZone && isEnemy)
                {
                    Vec3 outDir = toH.Length < 0.01f ? new Vec3(1f, 0f, 0f) : toH.NormalizedCopy();
                    bool mounted = false; try { mounted = a.MountAgent != null; } catch { }
                    float pushDist = a.IsHero ? 1.5f : (mounted ? 4f : 2.5f);
                    Vec3 dest = a.Position + outDir * pushDist;
                    dest.z = a.Position.z;
                    try { QueueMove(a, dest, 0.3f); } catch { }
                }

                if (dist > e.Radius) continue;
                if (IsWarded(a)) continue;

                try
                {
                    if (dmg > 0 && isEnemy)
                    {
                        BeginAgentGlowRaw(a, ColorSchoolData.GetGlowColor(e.School), 1.5f);
                        // 2.5f per 0.5s tick = 5 DPS per damage count
                        DamageAgent(a, dmg * 2.5f);
                        SpawnImpactBurst(a.Position, e.School, 3f);
                        // Teleport enemies back so they cannot simply walk through the barrier
                        Vec3 outDir = toH.Length < 0.01f ? new Vec3(1f, 0f, 0f) : toH.NormalizedCopy();
                        Vec3 outPos = e.Position + outDir * (e.Radius + 1.5f);
                        outPos.z = a.Position.z;
                        try { a.TeleportToPosition(outPos); } catch { }
                    }
                    if (dmg > 0 && isAlly)
                    {
                        BeginAgentGlowRaw(a, ColorSchoolData.GetGlowColor(e.School), 1.5f);
                        DamageAgent(a, dmg * 2.5f);
                        SpawnImpactBurst(a.Position, e.School, 3f);
                    }
                    if (restore > 0 && isAlly)
                    {
                        BeginAgentGlowRaw(a, ColorSchoolData.GetReversedGlowColor(e.School), 1.5f);
                        // 2.5f per 0.5s tick = 5 HPS per restore count
                        HealAgent(a, restore * 2.5f);
                        SpawnImpactBurst(a.Position, e.School, 3f);
                    }
                }
                catch { }
            }
        }

        // ── BURST ─────────────────────────────────────────────────────────────
        public static void ExecuteBurst(SpellCast cast)
        {
            Agent caster = Agent.Main;
            if (caster == null) return;
            ExecuteBurstFromAgent(caster, cast, caster.Team);
        }

        internal static void ExecuteBurstFromAgent(Agent caster, SpellCast cast, Team casterTeam)
        {
            if (caster == null || !caster.IsActive() || Mission.Current == null) return;

            int burstCnt = cast.BurstCount > 0 ? cast.BurstCount : cast.FormCount;
            float radius = Math.Max(2f, burstCnt * 2.5f);

            bool wantDmg  = cast.DamageCount  > 0;
            bool wantHeal = cast.RestoreCount > 0;

            // Directed Burst: compute forward vector for hemisphere split.
            Vec3 fwdH = Vec3.Zero;
            if (cast.UsingLostBurst)
            {
                Vec3 fwd = caster.LookDirection.NormalizedCopy();
                fwdH = new Vec3(fwd.x, fwd.y, 0f);
                if (fwdH.Length > 0.01f) fwdH = fwdH.NormalizedCopy();
            }

            var targets = new List<Agent>();
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount || a == caster) continue;
                    bool isEnemy = casterTeam != null && a.Team != null && a.Team != casterTeam;
                    bool isAlly  = casterTeam != null && a.Team != null && a.Team == casterTeam;
                    if (!(wantDmg || (wantHeal && isAlly))) continue;
                    Vec3 toH = new Vec3(a.Position.x - caster.Position.x, a.Position.y - caster.Position.y, 0f);
                    if (toH.Length > radius) continue;
                    targets.Add(a);
                }
            }
            catch { }

            ColorSchool col = cast.VisualColor;
            SpawnBurstExplosion(caster.Position, col, radius, 6f);
            TryCastSound(caster.Position, col);
            TryCastAnimation(caster);

            // Rear-hemisphere scaled cast (40% power) for Directed Burst.
            SpellCast rearCast = null;
            if (cast.UsingLostBurst)
            {
                rearCast = new SpellCast();
                rearCast.BurstCount         = cast.BurstCount;
                rearCast.Form               = cast.Form;
                rearCast.DamageCount        = cast.DamageCount  > 0 ? Math.Max(1, (int)(cast.DamageCount  * 0.4f)) : 0;
                rearCast.RestoreCount       = cast.RestoreCount > 0 ? Math.Max(1, (int)(cast.RestoreCount * 0.4f)) : 0;
                // Preserve the per-key damage natures so split-cast effects survive the scale.
                rearCast.SearCount          = cast.SearCount    > 0 ? Math.Max(1, (int)(cast.SearCount    * 0.4f)) : 0;
                rearCast.ForceCount         = cast.ForceCount   > 0 ? Math.Max(1, (int)(cast.ForceCount   * 0.4f)) : 0;
                rearCast.ShredCount         = cast.ShredCount   > 0 ? Math.Max(1, (int)(cast.ShredCount   * 0.4f)) : 0;
                rearCast.OverrideVisualColor = cast.OverrideVisualColor;
            }

            int affected = 0;
            int alliesHit = 0;
            foreach (Agent a in targets)
            {
                try
                {
                    SpellCast useCast = cast;
                    if (cast.UsingLostBurst && rearCast != null)
                    {
                        Vec3 toH = new Vec3(a.Position.x - caster.Position.x, a.Position.y - caster.Position.y, 0f);
                        if (toH.Length > 0.01f && Vec3.DotProduct(fwdH, toH.NormalizedCopy()) < 0f)
                            useCast = rearCast;
                    }
                    if (useCast.DamageCount > 0 && casterTeam != null && a.Team != null && a.Team == casterTeam)
                        alliesHit++;
                    ApplyEffectsToAgent(a, useCast, caster);
                    SpawnImpactBurst(a.Position, col, 4f);
                    affected++;
                }
                catch { }
            }

            if (wantDmg) ScatterEnemies(caster.Position, radius, casterTeam);

            // Burst also heals the caster when Restore is active
            if (wantHeal && caster.IsActive())
            {
                try
                {
                    HealAgent(caster, cast.RestoreCount * 15f);
                    ApplyRestoreEnchantments(caster, cast, caster);
                    SpawnImpactBurst(caster.Position, col, 4f);
                    affected++;
                }
                catch { }

                // Consecrated zone lingers at the burst point for 5 seconds.
                if (caster == Agent.Main)
                    SpawnHolyZone(caster.Position, cast.RestoreCount, radius, casterTeam);
            }

            if (caster == Agent.Main)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{cast.FormSummary()} — {cast.EffectSummary()} — {affected} {(affected == 1 ? "target" : "targets")}.",
                    ColorSchoolData.GetMessageColor(col)));
                if (alliesHit > 0)
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"Friendly fire — {alliesHit} {(alliesHit == 1 ? "ally" : "allies")} hit!",
                        new Color(1f, 0.35f, 0.1f)));
            }
            else
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{caster.Name} — burst strikes {affected} {(affected == 1 ? "target" : "targets")}.",
                    new Color(0.6f, 0.6f, 0.6f)));
            }
        }
    }
}
