// =============================================================================
// LIFE & DEATH MAGIC — SelfSpells.cs
// MISSILE FORM (L keys): a fast projectile that travels forward and explodes.
//   range          = max(8, missileCount × 3) metres  — travel distance
//   explosion radius = 1 + missileCount metres         — blast on impact/end
//   Speed 28 m/s; trail every 0.05 s; hit-detect radius 1.5 m.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static partial class SpellEffects
    {
        // ── Missile state ─────────────────────────────────────────────────────
        private class MissileState
        {
            public Vec3        Position;
            public Vec3        Forward;
            public float       TravelLeft;
            public float       ExplosionRadius;
            public SpellCast   Cast;
            public Team        CasterTeam;
            public GameEntity  Light;
            public GameEntity  Light2;  // fireball cluster — upper glow for sphere appearance
            public float       TrailTimer = 0f;
            // Pale Comet: tracks agents already struck so each is only hit once per flight.
            public HashSet<Agent> PiercedAgents = null;
            public const float Speed         = 28f;
            public const float TrailInterval = 0.035f;  // was 0.05 — denser fire trail
            public const float DetectRadius  = 1.5f;
        }

        private static MissileState _missile  = null;
        private static MissileState _missile2 = null;  // Twin Bolt second projectile

        private static SpellCast ScaleMissileCast(SpellCast original, float factor)
        {
            var sc = new SpellCast();
            sc.MissileCount         = original.MissileCount;
            sc.Form                 = original.Form;
            sc.DamageCount          = original.DamageCount  > 0 ? Math.Max(1, (int)(original.DamageCount  * factor)) : 0;
            sc.RestoreCount         = original.RestoreCount > 0 ? Math.Max(1, (int)(original.RestoreCount * factor)) : 0;
            // Preserve the per-key damage natures so split-cast effects survive the scale.
            sc.SearCount            = original.SearCount    > 0 ? Math.Max(1, (int)(original.SearCount    * factor)) : 0;
            sc.ForceCount           = original.ForceCount   > 0 ? Math.Max(1, (int)(original.ForceCount   * factor)) : 0;
            sc.ShredCount           = original.ShredCount   > 0 ? Math.Max(1, (int)(original.ShredCount   * factor)) : 0;
            sc.UsingLostMissile     = true;
            sc.OverrideVisualColor  = original.OverrideVisualColor;
            return sc;
        }

        // ── Execute ───────────────────────────────────────────────────────────
        public static void ExecuteMissile(SpellCast cast)
        {
            Agent caster = Agent.Main;
            if (caster == null || !caster.IsActive()) return;

            if (_missile != null || _missile2 != null)
            {
                ClearMissile();
                InformationManager.DisplayMessage(new InformationMessage(
                    "Missile dispersed.", new Color(0.7f, 0.7f, 0.7f)));
                return;
            }

            int   missileCnt   = cast.MissileCount > 0 ? cast.MissileCount : cast.FormCount;
            float range        = Math.Max(8f, missileCnt * 3f);
            float explRadius   = 1f + missileCnt;

            Vec3 fwd      = caster.LookDirection.NormalizedCopy();
            Vec3 startPos = caster.Position + fwd * 1.5f + new Vec3(0f, 0f, 1.2f);

            ColorSchool col = cast.VisualColor;

            if (cast.UsingLostMissile)
            {
                SpellCast cast1 = ScaleMissileCast(cast, 0.60f);
                SpellCast cast2 = ScaleMissileCast(cast, 0.60f);
                Vec3 right = Vec3.CrossProduct(fwd, new Vec3(0f, 0f, 1f)).NormalizedCopy() * 0.35f;
                _missile = new MissileState
                {
                    Position = startPos - right, Forward = fwd,
                    TravelLeft = range, ExplosionRadius = explRadius,
                    Cast = cast1, CasterTeam = caster.Team,
                };
                _missile2 = new MissileState
                {
                    Position = startPos + right, Forward = fwd,
                    TravelLeft = range, ExplosionRadius = explRadius,
                    Cast = cast2, CasterTeam = caster.Team,
                };
                Vec3 rgb = SchoolToLightColor(col);
                _missile.Light   = SpawnAreaLight(_missile.Position,  col, 5f);
                _missile.Light2  = SpawnAreaLightRaw(_missile.Position  + new Vec3(0f, 0f, 0.35f), rgb, 3f);
                _missile2.Light  = SpawnAreaLight(_missile2.Position, col, 5f);
                _missile2.Light2 = SpawnAreaLightRaw(_missile2.Position + new Vec3(0f, 0f, 0.35f), rgb, 3f);
                if (col != ColorSchool.Ashen)
                {
                    SpawnBigFireParticle(startPos - right, 0.5f);
                    SpawnBigFireParticle(startPos + right, 0.5f);
                }
                InformationManager.DisplayMessage(new InformationMessage(
                    $"Twin Bolt ({range:F0}m, {explRadius:F0}m blast) ×2 — {cast1.EffectSummary()} each.",
                    ColorSchoolData.GetMessageColor(col)));
            }
            else
            {
                _missile = new MissileState
                {
                    Position = startPos, Forward = fwd,
                    TravelLeft = range, ExplosionRadius = explRadius,
                    Cast = cast, CasterTeam = caster.Team,
                };
                if (cast.UsingPaleComet)
                    _missile.PiercedAgents = new HashSet<Agent>();
                Vec3 rgb = SchoolToLightColor(col);
                _missile.Light  = SpawnAreaLight(startPos, col, 6f);
                _missile.Light2 = SpawnAreaLightRaw(startPos + new Vec3(0f, 0f, 0.4f), rgb, 3.5f);
                if (col != ColorSchool.Ashen) SpawnBigFireParticle(startPos, 0.6f);
                string formLabel = cast.UsingPaleComet ? "Pale Comet" : "Missile";
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{formLabel} ({range:F0}m, {explRadius:F0}m blast) — {cast.EffectSummary()}.",
                    ColorSchoolData.GetMessageColor(col)));
            }

            TryCastSound(caster.Position, col);
            TryCastAnimation(caster);
            BeginAgentGlow(caster, col, 1f);
        }

        // ── Tick (called from MagicSystem every mission frame) ─────────────────
        public static void TickMissile(float dt)
        {
            if (Mission.Current == null) return;
            TickMissileState(ref _missile, dt, false);
            TickMissileState(ref _missile2, dt, true);
        }

        private static void TickMissileState(ref MissileState m, float dt, bool isTwin)
        {
            if (m == null) return;

            float moved    = MissileState.Speed * dt;
            m.Position    += m.Forward * moved;
            m.TravelLeft  -= moved;

            if (m.Light != null)
            {
                try
                {
                    var lf = new MatrixFrame(Mat3.Identity, m.Position);
                    m.Light.SetGlobalFrame(in lf, true);
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            if (m.Light2 != null)
            {
                try
                {
                    var lf2 = new MatrixFrame(Mat3.Identity, m.Position + new Vec3(0f, 0f, 0.4f));
                    m.Light2.SetGlobalFrame(in lf2, true);
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }

            m.TrailTimer -= dt;
            if (m.TrailTimer <= 0f)
            {
                m.TrailTimer = MissileState.TrailInterval;
                SpawnTempLight(m.Position, m.Cast.VisualColor, 4f, 0.5f);
                if (m.Cast.VisualColor != ColorSchool.Ashen)
                    SpawnTrailParticle(m.Position, 0.35f);
            }

            bool wantDmg  = m.Cast.DamageCount  > 0;
            bool wantHeal = m.Cast.RestoreCount > 0;
            Vec3 mpos = m.Position;

            // Pale Comet: pass through enemies, applying effects on first contact per agent.
            if (m.PiercedAgents != null)
            {
                try
                {
                    foreach (Agent a in Mission.Current.Agents)
                    {
                        if (!a.IsActive() || a.IsMount || a == Agent.Main) continue;
                        if (m.PiercedAgents.Contains(a)) continue;
                        bool isEnemy = m.CasterTeam != null && a.Team != null && a.Team != m.CasterTeam;
                        bool isAlly  = m.CasterTeam != null && a.Team != null && a.Team == m.CasterTeam;
                        if (!((wantDmg && isEnemy) || (wantHeal && isAlly))) continue;
                        float dx = a.Position.x - mpos.x;
                        float dy = a.Position.y - mpos.y;
                        if (dx * dx + dy * dy > MissileState.DetectRadius * MissileState.DetectRadius) continue;
                        m.PiercedAgents.Add(a);
                        if (!IsWarded(a))
                        {
                            try
                            {
                                ApplyEffectsToAgent(a, m.Cast, Agent.Main);
                                SpawnImpactBurst(a.Position, m.Cast.VisualColor, 3f);
                            }
                            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        }
                    }
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                if (m.TravelLeft <= 0f)
                    ExplodeMissileState(ref m, m.Position, isTwin);
            }
            else
            {
                try
                {
                    foreach (Agent a in Mission.Current.Agents)
                    {
                        if (!a.IsActive() || a.IsMount || a == Agent.Main) continue;
                        bool isEnemy = m.CasterTeam != null && a.Team != null && a.Team != m.CasterTeam;
                        bool isAlly  = m.CasterTeam != null && a.Team != null && a.Team == m.CasterTeam;
                        if (!((wantDmg && isEnemy) || (wantHeal && isAlly))) continue;
                        float dx = a.Position.x - mpos.x;
                        float dy = a.Position.y - mpos.y;
                        if (dx * dx + dy * dy > MissileState.DetectRadius * MissileState.DetectRadius) continue;
                        ExplodeMissileState(ref m, mpos, isTwin);
                        return;
                    }
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                if (m.TravelLeft <= 0f)
                    ExplodeMissileState(ref m, m.Position, isTwin);
            }
        }

        // ── Explosion ─────────────────────────────────────────────────────────
        private static void ExplodeMissileState(ref MissileState slot, Vec3 pos, bool silent)
        {
            if (slot == null) return;
            MissileState m = slot;
            try { m.Light?.Remove(0);  } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { m.Light2?.Remove(0); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            slot = null;

            if (Mission.Current == null) return;

            float       radius = m.ExplosionRadius;
            ColorSchool col    = m.Cast.VisualColor;

            SpawnExplosionEffect(pos, col, radius, 5f);
            TryCastSound(pos, col);
            if (!silent) RecordMagicCast(pos);

            int affected  = 0;
            int alliesHit = 0;
            bool wantDmg  = m.Cast.DamageCount  > 0;
            bool wantHeal = m.Cast.RestoreCount > 0;
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount) continue;
                    float dist = new Vec3(a.Position.x - pos.x,
                                         a.Position.y - pos.y, 0f).Length;
                    if (dist > radius) continue;
                    bool isEnemy = m.CasterTeam != null && a.Team != null && a.Team != m.CasterTeam;
                    bool isAlly  = m.CasterTeam != null && a.Team != null && a.Team == m.CasterTeam;
                    if (!(wantDmg || (wantHeal && isAlly))) continue;
                    if (IsWarded(a)) continue;
                    try
                    {
                        if (wantDmg && isAlly) alliesHit++;
                        ApplyEffectsToAgent(a, m.Cast, Agent.Main);
                        SpawnImpactBurst(a.Position, col, 4f);
                        affected++;
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            if (wantDmg) ScatterEnemies(pos, radius, m.CasterTeam);

            // Fire patch persists at explosion point for 8 seconds.
            if (wantDmg) SpawnFirePatch(pos, m.Cast.DamageCount, m.CasterTeam);

            if (!silent)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"Missile detonates — {m.Cast.EffectSummary()} — {affected} {(affected == 1 ? "target" : "targets")}.",
                    ColorSchoolData.GetMessageColor(col)));
                if (alliesHit > 0)
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"Friendly fire — {alliesHit} {(alliesHit == 1 ? "ally" : "allies")} hit!",
                        new Color(1f, 0.35f, 0.1f)));
            }
        }

        public static void ClearMissile()
        {
            if (_missile != null)
            {
                try { _missile.Light?.Remove(0);  } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { _missile.Light2?.Remove(0); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                _missile = null;
            }
            if (_missile2 != null)
            {
                try { _missile2.Light?.Remove(0);  } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { _missile2.Light2?.Remove(0); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                _missile2 = null;
            }
        }

        // ── Legacy stub ───────────────────────────────────────────────────────
        public static void ExecuteAura(SpellCast cast) => ExecuteMissile(cast);
        internal static void TickAuraNode(AreaEffect e) { }

        // ── Ward state ────────────────────────────────────────────────────────
        // Keyed by Agent reference, not index — avoids inheriting protection when
        // an agent dies and a newly spawned agent reuses the same index slot.
        private static readonly Dictionary<Agent, float> _wardedAgents = new Dictionary<Agent, float>();

        public static bool IsWarded(Agent a)
        {
            if (a == null) return false;
            return _wardedAgents.TryGetValue(a, out float t) && t > 0f;
        }

        // Player sigil DD×N — wards caster + all allies within (N-1)×2 m for 10 s
        public static void ExecuteWard(int dCount)
        {
            Agent caster = Agent.Main;
            if (caster == null || !caster.IsActive()) return;

            float radius = (dCount - 1) * 2f;
            _wardedAgents[caster] = 10f;
            BeginAgentGlow(caster, ColorSchool.White, 10f);
            SpawnCircleLights(caster.Position, ColorSchool.White, Math.Max(2f, radius), 3f);
            TryCastSound(caster.Position, ColorSchool.White);
            TryCastAnimation(caster);

            int count = 1;
            if (radius > 0f && Mission.Current != null)
            {
                try
                {
                    foreach (Agent ally in Mission.Current.Agents.ToList())
                    {
                        if (ally == caster || !ally.IsActive() || ally.IsMount) continue;
                        if (caster.Team != null && ally.Team != caster.Team) continue;
                        if (ally.Position.Distance(caster.Position) > radius) continue;
                        _wardedAgents[ally] = 10f;
                        BeginAgentGlow(ally, ColorSchool.White, 10f);
                        count++;
                    }
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }

            string msg = count > 1
                ? $"Ward ({radius:F0}m) — {count} protected for 10 seconds."
                : "Ward — magic cannot touch you for 10 seconds.";
            InformationManager.DisplayMessage(new InformationMessage(
                msg, ColorSchoolData.GetMessageColor(ColorSchool.White)));
        }

        // NPC ward — wards caster and optionally nearby allies within allyRadius
        public static void ExecuteWardFromAgent(Agent caster, float allyRadius = 0f)
        {
            if (caster == null || !caster.IsActive()) return;
            _wardedAgents[caster] = 10f;
            BeginAgentGlow(caster, ColorSchool.White, 10f);
            TryCastSound(caster.Position, ColorSchool.White);
            TryCastAnimation(caster);

            if (allyRadius > 0f && Mission.Current != null)
            {
                try
                {
                    foreach (Agent ally in Mission.Current.Agents.ToList())
                    {
                        if (ally == caster || !ally.IsActive() || ally.IsMount) continue;
                        if (ally.Team != caster.Team) continue;
                        if (ally.Position.Distance(caster.Position) > allyRadius) continue;
                        _wardedAgents[ally] = 10f;
                        BeginAgentGlow(ally, ColorSchool.White, 10f);
                    }
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        public static void TickWard(float dt)
        {
            var keys = _wardedAgents.Keys.ToList();
            foreach (Agent a in keys)
            {
                float t = _wardedAgents[a] - dt;
                if (t <= 0f) _wardedAgents.Remove(a);
                else _wardedAgents[a] = t;
            }
        }

        public static void ClearWard()
        {
            _wardedAgents.Clear();
        }
    }
}
