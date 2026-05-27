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

namespace ColoursOfCalradia
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
            public GameEntity LightEntity; // coloured point light marking the effect area
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
                try { e.LightEntity?.Remove(0); } catch { }
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
                float lightRadius = Math.Min(radius, 8f);
                var light = Light.CreatePointLight(lightRadius);
                light.Radius        = lightRadius;
                light.Intensity     = 3000f;
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
            SpawnTempLight(origin, school, 5f, duration);
            int   count = aoeRadius > 10f ? 6 : 5;
            float ringR = Math.Min(aoeRadius * 0.75f, 8f);
            for (int i = 0; i < count; i++)
            {
                double angle = Math.PI * 2.0 / count * i;
                Vec3 pos = origin + new Vec3((float)Math.Cos(angle) * ringR, (float)Math.Sin(angle) * ringR, 0f);
                SpawnTempLight(pos, school, 5f, duration);
            }
        }

        // Lights a cone shape with 7 temp lights.
        // Matches blast-spell cone geometry: 9m range, ≈±37° half-angle (dot 0.80).
        internal static void SpawnConeLights(Vec3 origin, Vec3 fwd, ColorSchool school, float duration)
        {
            Vec3 right = new Vec3(-fwd.y, fwd.x, 0f);
            right = right.Length < 0.01f ? new Vec3(1f, 0f, 0f) : right.NormalizedCopy();
            Vec3[] pts = {
                origin,                                      // caster origin
                origin + fwd * 2.5f,                         // near centre
                origin + fwd * 4.5f - right * 2.5f,          // mid left
                origin + fwd * 4.5f + right * 2.5f,          // mid right
                origin + fwd * 7.5f,                         // far centre
                origin + fwd * 7.5f - right * 5f,            // far left edge
                origin + fwd * 7.5f + right * 5f,            // far right edge
            };
            foreach (Vec3 pos in pts)
                SpawnTempLight(pos, school, 4f, duration);
            // Fire particle at the cast origin
            if (school != ColorSchool.Blight)
                SpawnTempFireParticle(origin, duration * 2f);
        }

        // Three-light burst at an impact point — centre flash plus two random scatter offsets.
        // Also spawns a brief fire particle if school is warm (non-blight).
        internal static void SpawnImpactBurst(Vec3 origin, ColorSchool school, float duration)
        {
            SpawnTempLight(origin, school, 4f, duration);
            for (int i = 0; i < 2; i++)
            {
                float angle = (float)(_rng.NextDouble() * Math.PI * 2);
                float dist  = 0.8f + (float)_rng.NextDouble() * 1.5f;
                Vec3  off   = new Vec3((float)Math.Cos(angle) * dist, (float)Math.Sin(angle) * dist, 0f);
                SpawnTempLight(origin + off, school, 3f, duration * 0.6f);
            }
            if (school != ColorSchool.Blight)
                SpawnTempFireParticle(origin, duration * 1.5f);
        }

        // ── Fire particle effects ──────────────────────────────────────────────
        // Particle names are wrapped in try/catch — silently skipped if the asset
        // does not exist in the running version of the game.
        private static readonly string[] _fireParticleNames =
        {
            "psys_campfire",
            "psys_game_fire_torch_small",
            "psys_env_fire_medium_01",
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

        // Tries each candidate particle name in order; stops at the first that works.
        internal static void SpawnTempFireParticle(Vec3 position, float duration)
        {
            foreach (string name in _fireParticleNames)
            {
                GameEntity entity = SpawnParticleEntity(position, name);
                if (entity == null) continue;
                _areaEffects.Add(new AreaEffect
                {
                    Id          = "temp_particle",
                    Position    = position,
                    School      = ColorSchool.Red,
                    TickInterval = duration,
                    TickTimer   = duration,
                    Remaining   = duration,
                    LightEntity = entity,
                });
                return;
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
                case ColorSchool.Blight: return new Vec3(0.15f, 0.25f, 0.80f); // cold ash-blue
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
                }
                } catch { } // guard: Mission.Agents modified during switch case
            }
        }

        public static void ClearAreaEffects()
        {
            foreach (var e in _areaEffects)
                try { e.LightEntity?.Remove(0); } catch { }
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
        }
    }
}
