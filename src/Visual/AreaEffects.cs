// =============================================================================
// COLOURS OF CALRADIA — AreaEffects.cs
// Mount & Blade II: Bannerlord Mod  v1.2.0.0
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Engine;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using TaleWorlds.CampaignSystem.MapEvents;

namespace AshAndEmber
{
    public static partial class SpellEffects
    {
        // ── Persistent area effects ────────────────────────────────────────────
        // Create spells place lasting effects on the field; each ticks on its own interval.
        internal class AreaEffect
        {
            public string Id;           // Unique ID for toggling (e.g. "create_orange")
            public Vec3   Position;
            public Vec3   Velocity;     // For moving effects (Create Yellow)
            public float  Radius;
            public ColorSchool School;
            public float  TickInterval;
            public float  TickTimer;
            public float  Remaining;    // negative = no expiry (toggle-only)
            public float  DirTimer;     // Create Yellow direction-change timer
            public float  Power = 1f;   // spell-power multiplier captured at cast time
            public GameEntity LightEntity;  // coloured point light marking the effect area
            public GameEntity LightEntity2; // extra persistent column light (1 m above node)
            public GameEntity LightEntity3; // extra persistent column light (2 m above node)
            public Team   CasterTeam;  // null = affect all teams; NPC effects set this to filter to enemies only
        }
        private static readonly List<AreaEffect> _areaEffects = new List<AreaEffect>();
        // AgentIndex → (remaining, frozen position, original agent reference).
        // The Agent reference guards against reinforcements reusing a dead agent's index —
        // without it, a newly spawned agent with the same index would be teleported to the
        // freeze position of the original, which crashes the engine in large battles.
        private static readonly Dictionary<int, (float Remaining, Vec3 FrozenPos, Agent Source)> _haltedAgents
            = new Dictionary<int, (float, Vec3, Agent)>();
        private static float _haltTeleportTimer = 0f;
        private const  float HaltTeleportInterval = 0.25f;
        private static readonly Dictionary<int, Agent> _haltAgentMap  = new Dictionary<int, Agent>();
        private static readonly List<int>              _haltKeySnap   = new List<int>();
        private static readonly List<int>              _expiredHaltKeys = new List<int>();

        // If an effect with this id exists, remove it. Otherwise add newEffect (if not null).
        internal static void ToggleAreaEffect(string id, AreaEffect newEffect)
        {
            int idx = _areaEffects.FindIndex(e => e.Id == id);
            if (idx >= 0)
            {
                try { _areaEffects[idx].LightEntity?.Remove(0); } catch { }
                try { _areaEffects[idx].LightEntity2?.Remove(0); } catch { }
                try { _areaEffects[idx].LightEntity3?.Remove(0); } catch { }
                _areaEffects.RemoveAt(idx);
                return;
            }
            if (newEffect != null)
            {
                newEffect.LightEntity = SpawnAreaLight(newEffect.Position, newEffect.School, newEffect.Radius);
                _areaEffects.Add(newEffect);
            }
        }

        public static void RemoveAreaEffect(string id)
        {
            foreach (var e in _areaEffects.Where(e => e.Id == id).ToList())
            {
                try { e.LightEntity?.Remove(0); } catch { }
                try { e.LightEntity2?.Remove(0); } catch { }
                try { e.LightEntity3?.Remove(0); } catch { }
            }
            _areaEffects.RemoveAll(e => e.Id == id);
        }

        public static bool HasAreaEffect(string id) => _areaEffects.Any(e => e.Id == id);

        // Spawns a coloured point light that expires after `duration` seconds with no gameplay effect.
        internal static void SpawnTempLight(Vec3 position, ColorSchool school, float radius, float duration)
        {
            var node = new AreaEffect
            {
                Id = "temp_light", School = school,
                Position = position, Radius = radius,
                TickInterval = duration, TickTimer = duration,
                Remaining = duration
            };
            node.LightEntity = SpawnAreaLight(node.Position, node.School, node.Radius);
            _areaEffects.Add(node);
        }

        private static GameEntity SpawnAreaLightRaw(Vec3 position, Vec3 rgb, float radius)
        {
            try
            {
                var scene = Mission.Current?.Scene;
                if (scene == null) return null;
                var entity = GameEntity.CreateEmpty(scene, false, false, false);
                var frame  = new MatrixFrame(Mat3.Identity, position + new Vec3(0f, 0f, 0.5f));
                entity.SetGlobalFrame(in frame, true);
                float lightRadius = Math.Min(radius, 20f);
                var light = Light.CreatePointLight(lightRadius);
                light.Radius        = lightRadius;
                light.Intensity     = 25000f;
                light.LightColor    = rgb;
                light.ShadowEnabled = false;
                entity.AddLight(light);
                return entity;
            }
            catch { return null; }
        }

        private static GameEntity SpawnAreaLight(Vec3 position, ColorSchool school, float radius)
            => SpawnAreaLightRaw(position, SchoolToLightColor(school), radius);

        // Lights a circular AoE with a centre node plus an evenly spaced ring.
        // Ring radius is 75% of aoeRadius, capped at 8m so nodes stay within the engine light limit.
        // Larger AoE (>10m) gets 6 ring nodes; smaller gets 5.
        internal static void SpawnCircleLights(Vec3 origin, ColorSchool school, float aoeRadius, float duration)
        {
            SpawnTempLight(origin, school, 10f, duration);
            int   count = aoeRadius > 10f ? 6 : 5;
            float ringR = Math.Min(aoeRadius * 0.75f, 12f);
            for (int i = 0; i < count; i++)
            {
                double angle = Math.PI * 2.0 / count * i;
                Vec3 pos = origin + new Vec3((float)Math.Cos(angle) * ringR, (float)Math.Sin(angle) * ringR, 0f);
                SpawnTempLight(pos, school, 7f, duration);
            }
            if (school != ColorSchool.Ashen)
                SpawnTempFireParticle(origin, duration * 1.5f);
        }

        // Lights a cone shape with 7 temp lights scaled to the actual blast range.
        // range matches the gameplay damage range so visuals never overreach.
        internal static void SpawnConeLights(Vec3 origin, Vec3 fwd, ColorSchool school, float duration, float range = 7.5f)
        {
            Vec3 right = new Vec3(-fwd.y, fwd.x, 0f);
            right = right.Length < 0.01f ? new Vec3(1f, 0f, 0f) : right.NormalizedCopy();
            float s = range / 7.5f; // scale factor so geometry tracks actual range
            Vec3[] pts = {
                origin,                                             // caster origin
                origin + fwd * 2.5f * s,                            // near centre
                origin + fwd * 4.5f * s - right * 2.5f * s,        // mid left
                origin + fwd * 4.5f * s + right * 2.5f * s,        // mid right
                origin + fwd * range,                               // far centre
                origin + fwd * range - right * 5f * s,              // far left edge
                origin + fwd * range + right * 5f * s,              // far right edge
            };
            foreach (Vec3 pos in pts)
                SpawnTempLight(pos, school, 8f, duration);
            // Fire particles spread along the cone up to the actual range
            if (school != ColorSchool.Ashen)
            {
                SpawnTempFireParticle(origin,               duration * 2.5f);
                SpawnTempFireParticle(origin + fwd * range * 0.5f, duration * 2f);
                SpawnTempFireParticle(origin + fwd * range,  duration * 1.5f);
            }
        }

        // Three-light burst at an impact point — centre flash plus two random scatter offsets.
        // Also spawns a brief fire particle if school is warm (non-blight).
        internal static void SpawnImpactBurst(Vec3 origin, ColorSchool school, float duration)
        {
            SpawnTempLight(origin, school, 10f, duration);
            for (int i = 0; i < 3; i++)
            {
                float angle = (float)(_rng.NextDouble() * Math.PI * 2);
                float dist  = 0.8f + (float)_rng.NextDouble() * 2f;
                Vec3  off   = new Vec3((float)Math.Cos(angle) * dist, (float)Math.Sin(angle) * dist, 0f);
                SpawnTempLight(origin + off, school, 6f, duration * 0.8f);
            }
            if (school != ColorSchool.Ashen)
            {
                SpawnTempFireParticle(origin, duration * 2f);
                SpawnTempFireParticle(origin + new Vec3(0.4f, 0.2f, 0f), duration * 1.5f);
            }
        }

        // ── Fire particle effects ──────────────────────────────────────────────
        // Particle names are tried in order; the first that attaches wins. NOTE:
        // AddParticleSystemComponent does NOT fail on an unknown name — it attaches
        // a component that renders nothing — so the first name in each list MUST be
        // one that actually exists, or the effect shows only its light and no flame.
        // Every name below is verified present in this game build's particle data.

        // General ambient fire — for impact scatter, barrier columns, etc.
        private static readonly string[] _fireParticleNames =
        {
            "psys_fire_vertical",
            "psys_campfire",
            "psys_torch_fire",
            "psys_battleground_env_fire",
        };

        // Fireball head / large static fire.
        private static readonly string[] _bigFireParticleNames =
        {
            "psys_battleground_env_fire",
            "psys_fire_vertical",
            "psys_campfire",
            "psys_torch_fire",
        };

        // Explosion / detonation — a burst of flame and sparks.
        private static readonly string[] _explosionParticleNames =
        {
            "psys_campfire_sparks",
            "psys_burning_woods_parts",
            "psys_fire_vertical",
            "psys_campfire",
        };

        // Missile trail — moving fire wake, designed for projectiles.
        private static readonly string[] _trailParticleNames =
        {
            "psys_torch_fire_moving",
            "psys_fire_vertical",
            "psys_campfire",
            "psys_torch_fire",
        };

        private static GameEntity SpawnParticleEntity(Vec3 position, string particleName)
        {
            try
            {
                var scene = Mission.Current?.Scene;
                if (scene == null) return null;
                var entity = GameEntity.CreateEmpty(scene, false, false, false);
                var frame  = new MatrixFrame(Mat3.Identity, position);
                entity.SetGlobalFrame(in frame, true);
                entity.AddParticleSystemComponent(particleName);
                return entity;
            }
            catch { return null; }
        }

        // Spawns a cluster of fire particles at the given position.
        // Tries each candidate name; on first success spawns a main + two offset flames.
        internal static void SpawnTempFireParticle(Vec3 position, float duration)
        {
            foreach (string name in _fireParticleNames)
            {
                GameEntity entity = SpawnParticleEntity(position, name);
                if (entity == null) continue;

                _areaEffects.Add(new AreaEffect
                {
                    Id = "temp_particle", Position = position, School = ColorSchool.Red,
                    TickInterval = duration, TickTimer = duration, Remaining = duration,
                    LightEntity = entity,
                });

                // Two scattered companion flames for a fuller fire effect
                for (int i = 0; i < 2; i++)
                {
                    float a = (float)(_rng.NextDouble() * Math.PI * 2);
                    float r = 0.3f + (float)_rng.NextDouble() * 0.7f;
                    Vec3  off = new Vec3((float)Math.Cos(a) * r, (float)Math.Sin(a) * r, 0f);
                    GameEntity extra = SpawnParticleEntity(position + off, name);
                    if (extra != null)
                        _areaEffects.Add(new AreaEffect
                        {
                            Id = "temp_particle", Position = position + off, School = ColorSchool.Red,
                            TickInterval = duration * 0.75f, TickTimer = duration * 0.75f,
                            Remaining = duration * 0.75f, LightEntity = extra,
                        });
                }
                return;
            }
        }

        // Single large fire particle — catapult fireball first, campfire as last resort.
        internal static void SpawnBigFireParticle(Vec3 position, float duration)
            => SpawnSingleParticle(position, duration, _bigFireParticleNames);

        // Detonation/impact particle — catapult hit or fire-arrow hit first.
        internal static void SpawnExplosionParticle(Vec3 position, float duration)
            => SpawnSingleParticle(position, duration, _explosionParticleNames);

        // Projectile wake particle — catapult trail or fire-arrow trail first.
        internal static void SpawnTrailParticle(Vec3 position, float duration)
            => SpawnSingleParticle(position, duration, _trailParticleNames);

        private static void SpawnSingleParticle(Vec3 position, float duration, string[] names)
        {
            foreach (string name in names)
            {
                GameEntity entity = SpawnParticleEntity(position, name);
                if (entity == null) continue;
                _areaEffects.Add(new AreaEffect
                {
                    Id = "temp_particle", Position = position, School = ColorSchool.Red,
                    TickInterval = duration, TickTimer = duration, Remaining = duration,
                    LightEntity = entity,
                });
                return;
            }
        }

        // Fireball detonation: central explosion column + radial fire-jet ring.
        // Centre uses impact/explosion particles; ring uses ambient fire as scatter.
        internal static void SpawnExplosionEffect(Vec3 pos, ColorSchool school, float radius, float duration)
        {
            bool useFire = school != ColorSchool.Ashen;

            // Central detonation column — explosion particles at three heights
            if (useFire)
            {
                SpawnExplosionParticle(pos,                           duration);
                SpawnExplosionParticle(pos + new Vec3(0f, 0f, 0.6f), duration * 0.75f);
                SpawnExplosionParticle(pos + new Vec3(0f, 0f, 1.2f), duration * 0.5f);
            }

            // Brief blinding flash then sustained glow
            SpawnTempLight(pos, school, Math.Min(radius * 3f,  22f), duration * 0.25f);
            SpawnTempLight(pos, school, Math.Min(radius * 1.5f, 16f), duration);
            SpawnTempLight(pos + new Vec3(0f, 0f, 1f), school, Math.Min(radius * 1.2f, 12f), duration * 0.6f);

            // Radial ring of fire jets
            int count = Math.Max(4, Math.Min(8, (int)(radius * 1.5f)));
            float ringR = radius * 0.7f;
            for (int i = 0; i < count; i++)
            {
                double angle = Math.PI * 2.0 / count * i;
                Vec3 rp = pos + new Vec3((float)Math.Cos(angle) * ringR, (float)Math.Sin(angle) * ringR, 0f);
                if (useFire) SpawnTempFireParticle(rp, duration * 0.5f);
                SpawnTempLight(rp, school, 6f, duration * 0.4f);
            }
        }

        // Burst shockwave explosion: concentric rings of fire erupting from the blast centre.
        // Centre uses explosion particles; rings use ambient fire as scatter.
        internal static void SpawnBurstExplosion(Vec3 origin, ColorSchool school, float aoeRadius, float duration)
        {
            bool useFire = school != ColorSchool.Ashen;

            // Central detonation
            if (useFire)
            {
                SpawnExplosionParticle(origin,                           duration);
                SpawnExplosionParticle(origin + new Vec3(0f, 0f, 0.5f), duration * 0.8f);
            }
            SpawnTempLight(origin, school, Math.Min(aoeRadius * 2.5f, 20f), duration);
            SpawnTempLight(origin + new Vec3(0f, 0f, 1.2f), school, Math.Min(aoeRadius * 1.5f, 14f), duration * 0.5f);

            // Inner ring — most intense
            int innerCount = Math.Max(4, Math.Min(8, (int)(aoeRadius * 1.2f)));
            float innerR = aoeRadius * 0.45f;
            for (int i = 0; i < innerCount; i++)
            {
                double angle = Math.PI * 2.0 / innerCount * i;
                Vec3 p = origin + new Vec3((float)Math.Cos(angle) * innerR, (float)Math.Sin(angle) * innerR, 0f);
                if (useFire) SpawnTempFireParticle(p, duration * 0.7f);
                SpawnTempLight(p, school, 7f, duration * 0.6f);
            }

            // Outer ring — shockwave edge
            int outerCount = Math.Max(5, Math.Min(10, (int)(aoeRadius * 1.6f)));
            float outerR = aoeRadius * 0.85f;
            for (int i = 0; i < outerCount; i++)
            {
                double angle = Math.PI * 2.0 / outerCount * i + Math.PI / outerCount;
                Vec3 p = origin + new Vec3((float)Math.Cos(angle) * outerR, (float)Math.Sin(angle) * outerR, 0f);
                if (useFire) SpawnTempFireParticle(p, duration * 0.5f);
                SpawnTempLight(p, school, 5f, duration * 0.35f);
            }
        }

        internal static void SpawnTempLightWhite(Vec3 position, float radius, float duration)
        {
            var node = new AreaEffect
            {
                Id = "temp_light", School = ColorSchool.Red,
                Position = position, Radius = radius,
                TickInterval = duration, TickTimer = duration,
                Remaining = duration
            };
            node.LightEntity = SpawnAreaLightRaw(position, new Vec3(1f, 1f, 1f), radius);
            _areaEffects.Add(node);
        }

        private static Vec3 SchoolToLightColor(ColorSchool school)
        {
            switch (school)
            {
                case ColorSchool.Red:    return new Vec3(1f,    0.15f, 0.02f); // bright fire-red
                case ColorSchool.Orange: return new Vec3(1f,    0.47f, 0.02f); // deep orange
                case ColorSchool.Yellow: return new Vec3(1f,    0.80f, 0.05f); // amber-gold
                case ColorSchool.Green:  return new Vec3(1f,    0.60f, 0.02f); // warm amber (was cold green)
                case ColorSchool.Blue:   return new Vec3(1f,    0.40f, 0.02f); // hot ember-orange (was cold blue)
                case ColorSchool.Purple: return new Vec3(0.87f, 0.07f, 0.02f); // deep crimson (was purple)
                case ColorSchool.White:  return new Vec3(1f,    0.93f, 0.75f); // pale warm flame
                case ColorSchool.Ashen: return new Vec3(0.28f, 0.32f, 0.42f); // dim ash grey-blue
                default:                 return new Vec3(1f,    0.70f, 0.30f);
            }
        }

        public static void TickAreaEffects(float dt)
        {
            if (Mission.Current == null) return;
            for (int i = _areaEffects.Count - 1; i >= 0; i--)
            {
                var e = _areaEffects[i];
                if (e.Remaining >= 0f)
                {
                    e.Remaining -= dt;
                    if (e.Remaining <= 0f)
                    {
                        try { e.LightEntity?.Remove(0); } catch { }
                        try { e.LightEntity2?.Remove(0); } catch { }
                        try { e.LightEntity3?.Remove(0); } catch { }
                        _areaEffects.RemoveAt(i);
                        continue;
                    }
                }

                // Yellow clouds drift randomly from their spawn position
                if (e.Id == "npc_yellow_cloud")
                {
                    e.DirTimer -= dt;
                    if (e.DirTimer <= 0f)
                    {
                        float angle    = (float)(_rng.NextDouble() * Math.PI * 2);
                        e.Velocity     = new Vec3((float)Math.Cos(angle) * 2f, (float)Math.Sin(angle) * 2f, 0f);
                        e.DirTimer     = 3f + (float)_rng.NextDouble() * 4f;
                    }
                    e.Position += e.Velocity * dt;
                }

                // Moving clouds need their light repositioned every frame
                if (e.LightEntity != null && e.Id == "npc_yellow_cloud")
                {
                    try
                    {
                        var lf = new MatrixFrame(Mat3.Identity, e.Position + new Vec3(0f, 0f, 0.5f));
                        e.LightEntity.SetGlobalFrame(in lf, true);
                    }
                    catch { }
                }

                e.TickTimer -= dt;
                if (e.TickTimer > 0f) continue;
                e.TickTimer = e.TickInterval;

                // Apply the area effect this tick
                try
                {
                switch (e.Id)
                {
                    case "spell_aura":
                        TickAuraNode(e);
                        break;

                    case "spell_barrier":
                        TickBarrierNode(e);
                        break;

                    case "npc_barrier":
                    {
                        foreach (Agent a in Mission.Current.Agents.ToList())
                        {
                            if (!a.IsActive() || a.IsMount || a.MountAgent != null) continue;
                            if (a.Position.Distance(e.Position) > e.Radius) continue;
                            if (e.CasterTeam != null && a.Team == e.CasterTeam) continue;
                            try
                            {
                                Vec3 dir = (a.Position - e.Position);
                                if (dir.Length < 0.01f) dir = new Vec3(1f, 0f, 0f);
                                else dir = dir.NormalizedCopy();
                                Vec3 dest = e.Position + dir * (e.Radius + 2f);
                                dest.z = a.Position.z;
                                a.TeleportToPosition(dest);
                                BeginAgentGlow(a, e.School, 1.5f);
                            }
                            catch { }
                        }
                        break;
                    }

                    case "npc_heal_zone":
                    {
                        float heal = 15f * e.Power;
                        foreach (Agent a in Mission.Current.Agents.ToList())
                        {
                            if (!a.IsActive() || a.IsMount || a.Position.Distance(e.Position) > e.Radius) continue;
                            if (e.CasterTeam != null && a.Team != e.CasterTeam) continue;
                            try
                            {
                                float h = Math.Min(heal, a.HealthLimit - a.Health);
                                if (h > 0f) { a.Health += h; BeginAgentGlow(a, e.School, 1.5f); }
                            }
                            catch { }
                        }
                        break;
                    }

                    case "npc_morale_aura":
                    {
                        foreach (Agent a in Mission.Current.Agents.ToList())
                        {
                            if (!a.IsActive() || a.IsMount || a.Position.Distance(e.Position) > e.Radius) continue;
                            if (e.CasterTeam != null && a.Team == e.CasterTeam) continue;
                            try
                            {
                                a.SetMorale(Math.Max(0f, a.GetMorale() - 5f));
                                BeginAgentGlow(a, e.School, 1.5f);
                            }
                            catch { }
                        }
                        break;
                    }

                    case "npc_yellow_cloud":
                    {
                        float cloudDmg = 38f * e.Power;
                        foreach (Agent a in Mission.Current.Agents
                            .Where(a => a.IsActive() && !a.IsMount &&
                                        a.Position.Distance(e.Position) <= e.Radius).ToList())
                        {
                            if (e.CasterTeam != null && a.Team == e.CasterTeam) continue;
                            try
                            {
                                if (a.Health <= cloudDmg) QueueKill(a);
                                else
                                {
                                    a.Health -= cloudDmg;
                                    try { a.SetMorale(Math.Max(0f, a.GetMorale() - 10f)); } catch { }
                                }
                                BeginAgentGlow(a, e.School, 1.5f);
                            }
                            catch { }
                        }
                        break;
                    }

                    case "spell_firepatch":
                    {
                        SpawnTempFireParticle(e.Position, 1.5f);
                        SpawnTempLight(e.Position, ColorSchool.Red, 5f, 1.5f);
                        foreach (Agent a in Mission.Current.Agents.ToList())
                        {
                            if (!a.IsActive() || a.IsMount) continue;
                            if (e.CasterTeam != null && a.Team == e.CasterTeam) continue;
                            if (a.Position.Distance(e.Position) > e.Radius) continue;
                            if (IsWarded(a)) continue;
                            try
                            {
                                DamageAgent(a, e.Power);
                                BeginAgentGlow(a, ColorSchool.Red, 1.5f);
                            }
                            catch { }
                        }
                        break;
                    }

                    case "spell_holyzone":
                    {
                        SpawnTempLightWhite(e.Position, e.Radius, 1.5f);
                        foreach (Agent a in Mission.Current.Agents.ToList())
                        {
                            if (!a.IsActive() || a.IsMount) continue;
                            if (e.CasterTeam != null && a.Team != e.CasterTeam) continue;
                            if (a.Position.Distance(e.Position) > e.Radius) continue;
                            try
                            {
                                float h = Math.Min(e.Power, a.HealthLimit - a.Health);
                                if (h > 0f) { a.Health += h; BeginAgentGlow(a, ColorSchool.White, 1.5f); }
                            }
                            catch { }
                        }
                        break;
                    }
                }
                } catch { } // guard: Mission.Agents modified during switch case
            }
        }

        // ── Spell aftermath helpers ────────────────────────────────────────────
        // Called from ExplodeMissile (when DamageCount > 0) — a patch of fire lingers
        // at the explosion point, damaging enemies who walk through it.
        internal static void SpawnFirePatch(Vec3 pos, int damageCount, Team casterTeam)
        {
            var node = new AreaEffect
            {
                Id           = "spell_firepatch",
                School       = ColorSchool.Red,
                Position     = pos,
                Radius       = 3f,
                TickInterval = 1f,
                TickTimer    = 1f,
                Remaining    = 8f,
                Power        = damageCount * 8f,
                CasterTeam   = casterTeam,
            };
            node.LightEntity = SpawnAreaLight(pos, ColorSchool.Red, 5f);
            _areaEffects.Add(node);
            SpawnTempFireParticle(pos, 8f);
        }

        // Called from ExecuteBurstFromAgent (when RestoreCount > 0 and player is caster) —
        // a consecrated zone lingers at the burst centre, slowly healing allies.
        internal static void SpawnHolyZone(Vec3 pos, int restoreCount, float radius, Team casterTeam)
        {
            var node = new AreaEffect
            {
                Id           = "spell_holyzone",
                School       = ColorSchool.White,
                Position     = pos,
                Radius       = Math.Max(3f, radius),
                TickInterval = 1f,
                TickTimer    = 1f,
                Remaining    = 5f,
                Power        = restoreCount * 8f,
                CasterTeam   = casterTeam,
            };
            node.LightEntity = SpawnAreaLight(pos, ColorSchool.White, Math.Max(3f, radius));
            _areaEffects.Add(node);
        }

        public static void ClearAreaEffects()
        {
            foreach (var e in _areaEffects)
            {
                try { e.LightEntity?.Remove(0); } catch { }
                try { e.LightEntity2?.Remove(0); } catch { }
                try { e.LightEntity3?.Remove(0); } catch { }
            }
            foreach (var kvp in _haltedAgents)
            {
                try
                {
                    Agent agent = kvp.Value.Source;
                    if (agent?.IsActive() == true && agent.Health > 0f)
                    {
                        bool usingEquip = false;
                        try { usingEquip = agent.IsUsingGameObject; } catch { }
                        if (!usingEquip)
                            agent.SetMaximumSpeedLimit(10f, false);
                    }
                }
                catch { }
            }
            _areaEffects.Clear();
            _haltedAgents.Clear();
            _haltTeleportTimer = 0f;
            ClearMissile();
        }
    }
}
