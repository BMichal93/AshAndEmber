// =============================================================================
// ASH AND EMBER — AreaEffects.Particles.cs
// Fire particle effects.
// Partial of SpellEffects (shared state lives in AreaEffects.cs).
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

        // ── Nature (Living Ember) particle effects ──────────────────────────────
        // Real terrain debris stands in for each element so a cast looks like the
        // land itself answering: torn grass and leaves for Earth (forest), water
        // splashes for Water, blown dust for Wind, and sparks for Storm. All names
        // are verified present in this build
        // (drawn from the engine's collision/footstep/weather particle sets); the
        // first that attaches wins, the rest are fallbacks.
        private static string[] NatureParticleNames(NatureElement el)
        {
            switch (el)
            {
                case NatureElement.Earth:   // forest — torn grass, leaves, roots
                    return new[] { "psys_game_infantry_grass_col", "psys_game_hoof_grass_col",
                                   "psys_game_boulder_grass_coll", "psys_dust_env_forest" };
                case NatureElement.Water:   // splashes
                    return new[] { "psys_game_water_splash_circular", "psys_game_water_splash_1",
                                   "psys_game_water_splash_2", "psys_game_hoof_water_coll" };
                case NatureElement.Wind:    // blown dust
                    return new[] { "psys_dust_env", "psys_game_cam_dust",
                                   "psys_dust_env_2", "psys_game_hoof_dust" };
                case NatureElement.Storm:   // sparks
                    return new[] { "psys_campfire_sparks", "psys_game_stone_dust_a",
                                   "psys_dust_env" };
                default:
                    return new[] { "psys_dust_env" };
            }
        }

        // A cluster: one burst at the point plus a couple of scattered companions.
        internal static void SpawnNatureBurst(Vec3 position, NatureElement el, float duration)
        {
            string[] names = NatureParticleNames(el);
            SpawnSingleParticle(position, duration, names);
            for (int i = 0; i < 2; i++)
            {
                float a = (float)(_rng.NextDouble() * Math.PI * 2);
                float r = 0.3f + (float)_rng.NextDouble() * 0.7f;
                Vec3  off = new Vec3((float)Math.Cos(a) * r, (float)Math.Sin(a) * r, 0f);
                SpawnSingleParticle(position + off, duration * 0.8f, names);
            }
        }

        // A ring of bursts at the given radius — for shockwaves and AoE pulses.
        internal static void SpawnNatureRing(Vec3 origin, NatureElement el, float radius, float duration)
        {
            string[] names = NatureParticleNames(el);
            int count = Math.Max(6, Math.Min(14, (int)(radius * 1.6f)));
            for (int i = 0; i < count; i++)
            {
                double ang = Math.PI * 2.0 / count * i;
                Vec3 p = origin + new Vec3((float)Math.Cos(ang) * radius, (float)Math.Sin(ang) * radius, 0f);
                SpawnSingleParticle(p, duration, names);
            }
        }

        // A line of bursts between two points — for cones, pulls, dashes, beams.
        internal static void SpawnNatureLine(Vec3 from, Vec3 to, NatureElement el, float duration)
        {
            string[] names = NatureParticleNames(el);
            Vec3 delta = to - from;
            int steps = Math.Max(3, Math.Min(10, (int)(delta.Length)));
            for (int i = 0; i <= steps; i++)
            {
                float t = steps == 0 ? 0f : (float)i / steps;
                SpawnSingleParticle(from + delta * t, duration, names);
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

                    case "spell_dirge":
                    {
                        SpawnTempFireParticle(e.Position, 1.5f);
                        SpawnTempLight(e.Position, ColorSchool.Red, Math.Max(5f, e.Radius * 0.6f), 1.5f);
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
                }
                } catch { } // guard: Mission.Agents modified during switch case
            }
        }

    }
}
