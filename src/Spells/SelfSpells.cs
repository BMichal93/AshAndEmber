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
            public float       TrailTimer = 0f;
            public const float Speed         = 28f;
            public const float TrailInterval = 0.05f;
            public const float DetectRadius  = 1.5f;
        }

        private static MissileState _missile = null;

        // ── Execute ───────────────────────────────────────────────────────────
        public static void ExecuteMissile(SpellCast cast)
        {
            Agent caster = Agent.Main;
            if (caster == null || !caster.IsActive()) return;

            if (_missile != null)
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

            _missile = new MissileState
            {
                Position        = startPos,
                Forward         = fwd,
                TravelLeft      = range,
                ExplosionRadius = explRadius,
                Cast            = cast,
                CasterTeam      = caster.Team,
            };

            ColorSchool col = cast.VisualColor;
            _missile.Light = SpawnAreaLight(startPos, col, 5f);
            TryCastSound(caster.Position, col);
            TryCastAnimation(caster);
            BeginAgentGlow(caster, col, 1f);

            InformationManager.DisplayMessage(new InformationMessage(
                $"Missile ({range:F0}m, {explRadius:F0}m blast) — {cast.EffectSummary()}.",
                ColorSchoolData.GetMessageColor(col)));
        }

        // ── Tick (called from MagicSystem every mission frame) ─────────────────
        public static void TickMissile(float dt)
        {
            if (_missile == null || Mission.Current == null) return;

            float moved        = MissileState.Speed * dt;
            _missile.Position  += _missile.Forward * moved;
            _missile.TravelLeft -= moved;

            // Move head light with missile
            if (_missile.Light != null)
            {
                try
                {
                    var lf = new MatrixFrame(Mat3.Identity, _missile.Position);
                    _missile.Light.SetGlobalFrame(in lf, true);
                }
                catch { }
            }

            // Particle trail — short-lived lights left behind every TrailInterval
            _missile.TrailTimer -= dt;
            if (_missile.TrailTimer <= 0f)
            {
                _missile.TrailTimer = MissileState.TrailInterval;
                SpawnTempLight(_missile.Position, _missile.Cast.VisualColor, 3f, 0.4f);
                if (_missile.Cast.VisualColor != ColorSchool.Ashen)
                    SpawnTempFireParticle(_missile.Position, 0.3f);
            }

            // Hit-detect: explode on close contact with a valid target.
            // Direct iteration (no .ToList()) avoids per-frame heap allocation.
            bool wantDmg  = _missile.Cast.DamageCount  > 0;
            bool wantHeal = _missile.Cast.RestoreCount > 0;
            Vec3 mpos = _missile.Position;
            try
            {
                foreach (Agent a in Mission.Current.Agents)
                {
                    if (!a.IsActive() || a.IsMount) continue;
                    bool isEnemy = _missile.CasterTeam != null && a.Team != _missile.CasterTeam;
                    bool isAlly  = _missile.CasterTeam != null && a.Team == _missile.CasterTeam;
                    if (!(wantDmg || (wantHeal && isAlly))) continue;
                    float dx = a.Position.x - mpos.x;
                    float dy = a.Position.y - mpos.y;
                    if (dx * dx + dy * dy > MissileState.DetectRadius * MissileState.DetectRadius) continue;
                    ExplodeMissile(mpos);
                    return;
                }
            }
            catch { }

            if (_missile.TravelLeft <= 0f)
                ExplodeMissile(_missile.Position);
        }

        // ── Explosion ─────────────────────────────────────────────────────────
        private static void ExplodeMissile(Vec3 pos)
        {
            if (_missile == null) return;
            MissileState m = _missile;
            ClearMissile();

            if (Mission.Current == null) return;

            float       radius = m.ExplosionRadius;
            ColorSchool col    = m.Cast.VisualColor;

            SpawnCircleLights(pos, col, radius, 5f);
            SpawnImpactBurst(pos, col, 8f);
            TryCastSound(pos, col);
            RecordMagicCast(pos);

            int affected = 0;
            int alliesHit = 0;
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount) continue;
                    float dist = new Vec3(a.Position.x - pos.x,
                                         a.Position.y - pos.y, 0f).Length;
                    if (dist > radius) continue;
                    bool isEnemy = m.CasterTeam != null && a.Team != m.CasterTeam;
                    bool isAlly  = m.CasterTeam != null && a.Team == m.CasterTeam;
                    bool wantDmg  = m.Cast.DamageCount  > 0;
                    bool wantHeal = m.Cast.RestoreCount > 0;
                    if (!(wantDmg || (wantHeal && isAlly))) continue;
                    if (IsWarded(a)) continue;
                    try
                    {
                        if (wantDmg && isAlly) alliesHit++;
                        ApplyEffectsToAgent(a, m.Cast, Agent.Main);
                        SpawnImpactBurst(a.Position, col, 4f);
                        affected++;
                    }
                    catch { }
                }
            }
            catch { }

            // Scatter surviving enemies from the explosion point.
            if (m.Cast.DamageCount > 0) ScatterEnemies(pos, radius, m.CasterTeam);

            InformationManager.DisplayMessage(new InformationMessage(
                $"Missile detonates — {m.Cast.EffectSummary()} — {affected} {(affected == 1 ? "target" : "targets")}.",
                ColorSchoolData.GetMessageColor(col)));
            if (alliesHit > 0)
                InformationManager.DisplayMessage(new InformationMessage(
                    $"Friendly fire — {alliesHit} {(alliesHit == 1 ? "ally" : "allies")} hit!",
                    new Color(1f, 0.35f, 0.1f)));
        }

        public static void ClearMissile()
        {
            if (_missile == null) return;
            try { _missile.Light?.Remove(0); } catch { }
            _missile = null;
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
                catch { }
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
                catch { }
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
