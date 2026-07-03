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

        // Spectral smoke — Spirit's whispered dread made visible; a wraith haze.
        private static readonly string[] _smokeParticleNames =
        {
            "psys_smoke",
            "psys_fire_smoke_env_point",
            "psys_dummy_smoke",
            "psys_burnt_wood_smoke",
        };

        // Driven snow and frost — the Ashen cold, standing in for flame.
        private static readonly string[] _snowParticleNames =
        {
            "psys_env_snow_dust",
            "psys_game_env_dust_snow",
            "psys_snow_dust",
            "psys_game_snow",
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
        internal static void SpawnTempFireParticle(Vec3 position, float duration)
            => SpawnParticleCluster(position, duration, _fireParticleNames);

        // Spectral smoke cluster — Spirit's dread made visible.
        internal static void SpawnTempSmokeParticle(Vec3 position, float duration)
            => SpawnParticleCluster(position, duration, _smokeParticleNames);

        // Driven snow / frost cluster — the Ashen cold in place of flame.
        internal static void SpawnTempSnowParticle(Vec3 position, float duration)
            => SpawnParticleCluster(position, duration, _snowParticleNames);

        // Tries each candidate name; on first success spawns a main plume plus two
        // scattered companions for a fuller, churning effect.
        private static void SpawnParticleCluster(Vec3 position, float duration, string[] names)
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

                // Two scattered companions for a fuller effect
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
                case NatureElement.Earth:   // stone erupts, torn earth and roots follow
                    return new[] { "psys_game_boulder_stone_coll", "psys_game_stone_gravel",
                                   "psys_game_infantry_stone_col", "psys_game_infantry_grass_col",
                                   "psys_dust_env_forest" };
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
                                // Canonical damage path: heroes are floored (never
                                // cloud-killed outright), wards/brands apply.
                                DamageAgent(a, cloudDmg);
                                try { a.SetMorale(Math.Max(0f, a.GetMorale() - 10f)); } catch { }
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
                        // Burning patches are LIVING fire (the Ashen cold never
                        // smoulders): on snow-bound ground the drifts steam and
                        // slump around the flame — fire melts snow, visibly.
                        if (SceneIsSnowy())
                            try { SpawnTempSmokeParticle(e.Position + new Vec3(0f, 0f, 0.4f), 1.4f); } catch { }
                        // Dry grass and brush carry the flame a stride outward.
                        try { TryFireCreep(e); } catch { }
                        // A standing burn gnaws at wooden machines and gates in it.
                        try { DamageBurnableStructures(e.Position, e.Radius + 1.5f, e.Power * 3f, null); } catch { }
                        foreach (Agent a in Mission.Current.Agents.ToList())
                        {
                            if (!a.IsActive() || a.IsMount) continue;
                            // Horses will not face open flame — whoever's banner
                            // their rider carries. The mount shies off the fire.
                            if (a.Position.Distance(e.Position) <= e.Radius + 1.5f)
                                TryScareMountFromFire(a, e.Position);
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

                    case "nature_barrier_wind":
                    case "nature_barrier_earth":
                    case "nature_barrier_water":
                    case "nature_barrier_storm":
                        TickNatureBarrierNode(e);
                        // Standing mist puts out fire beneath it: burning ground is
                        // quenched to steam, and any fire-wall warding there dies.
                        if (e.Id == "nature_barrier_water")
                            QuenchFireAt(e.Position, e.Radius + 0.5f);
                        break;

                    case "spell_mudpatch":
                    {
                        // Churned ground: everything crossing it wades — the mount
                        // too, so cavalry bogs down hardest. Impartial (team null).
                        if (_rng.Next(3) == 0)
                            try { SpawnNatureBurst(e.Position, NatureElement.Earth, 0.5f); } catch { }
                        foreach (Agent a in Mission.Current.Agents.ToList())
                        {
                            if (!a.IsActive() || a.IsMount) continue;
                            if (a.Position.Distance(e.Position) > e.Radius) continue;
                            try
                            {
                                NatureEffects.ApplySpeedToken(a, 0.55f, 0.8f);
                                if (a.MountAgent != null && a.MountAgent.IsActive())
                                    NatureEffects.ApplySpeedToken(a.MountAgent, 0.45f, 0.8f);
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

        // ── Nature barriers ──────────────────────────────────────────────────────
        // Water puts out fire. Burning ground within reach is quenched to steam
        // (patches are expired in place — the sweep disposes them safely) and the
        // fire-wall warding there dies with it, opening a real gap in the wall.
        internal static void QuenchFireAt(Vec3 pos, float radius)
        {
            float r2 = radius * radius;
            bool any = false;
            try
            {
                foreach (var fx in _areaEffects)
                {
                    if (fx.Id != "spell_firepatch" || fx.Remaining <= 0f) continue;
                    float dx = fx.Position.x - pos.x, dy = fx.Position.y - pos.y;
                    if (dx * dx + dy * dy > r2) continue;
                    fx.Remaining = 0f;   // the expiry sweep removes and disposes it
                    any = true;
                    try { SpawnTempSmokeParticle(fx.Position + new Vec3(0f, 0f, 0.5f), 3.5f); } catch { }
                }
            }
            catch { }
            try { if (ElementWallWards.QuenchFireNodesNear(pos, radius) > 0) any = true; } catch { }
            if (any)
                try { SpawnTempSmokeParticle(pos + new Vec3(0f, 0f, 0.6f), 3f); } catch { }
        }

        // ── Scene feel — terrain-aware elemental flavour ─────────────────────────
        // The battle terrain type (verified engine API) tells the elements what
        // ground they work on: snow steams under fire, desert sand rides the wind,
        // dry grass carries a creeping flame.
        internal static bool SceneIsSnowy()
        {
            try
            {
                var m = Mission.Current;
                if (m != null && m.HasValidTerrainType
                    && m.TerrainType == TaleWorlds.Core.TerrainType.Snow) return true;
            }
            catch { }
            // Winter anywhere reads as snow-bound ground (Seasons: 0 Spring … 3 Winter).
            try
            {
                return TaleWorlds.CampaignSystem.Campaign.Current != null
                    && (int)TaleWorlds.CampaignSystem.CampaignTime.Now.GetSeasonOfYear == 3;
            }
            catch { return false; }
        }

        internal static bool SceneIsDesert()
        {
            try
            {
                var m = Mission.Current;
                return m != null && m.HasValidTerrainType
                    && (m.TerrainType == TaleWorlds.Core.TerrainType.Desert
                     || m.TerrainType == TaleWorlds.Core.TerrainType.Dune);
            }
            catch { return false; }
        }

        // ── Fire creep — dry ground carries the flame ────────────────────────────
        // Each burning patch may seed ONE child patch a stride away per tick:
        // eager through grass and brush (plain/steppe/forest), reluctant on sand,
        // snow-bound or sodden ground. Two generations at most, and a hard cap,
        // so a wall of fire smoulders outward without ever consuming the field.
        private const int MaxFirePatches = 24;

        private static float FireCreepChance()
        {
            try
            {
                var m = Mission.Current;
                if (m != null && m.HasValidTerrainType)
                {
                    switch (m.TerrainType)
                    {
                        case TaleWorlds.Core.TerrainType.Forest:
                        case TaleWorlds.Core.TerrainType.Plain:
                        case TaleWorlds.Core.TerrainType.Steppe:
                            return SceneIsSnowy() ? 0.02f : 0.10f;  // grass burns — unless under snow
                        case TaleWorlds.Core.TerrainType.Desert:
                        case TaleWorlds.Core.TerrainType.Dune:
                        case TaleWorlds.Core.TerrainType.Swamp:
                            return 0.02f;                            // sand and sodden ground barely carry it
                    }
                }
            }
            catch { }
            return SceneIsSnowy() ? 0.02f : 0.06f;
        }

        private static void TryFireCreep(AreaEffect e)
        {
            if (e.Generation >= 2) return;
            if (_rng.NextDouble() >= FireCreepChance()) return;
            int patches = 0;
            try { foreach (var fx in _areaEffects) if (fx.Id == "spell_firepatch") patches++; } catch { }
            if (patches >= MaxFirePatches) return;

            double ang = _rng.NextDouble() * Math.PI * 2;
            Vec3 p = e.Position + new Vec3((float)Math.Cos(ang), (float)Math.Sin(ang), 0f)
                                * (2.5f + (float)_rng.NextDouble());
            var child = new AreaEffect
            {
                Id           = "spell_firepatch",
                School       = ColorSchool.Red,
                Position     = p,
                Radius       = 2.2f,
                TickInterval = 1f,
                TickTimer    = 1f,
                Remaining    = 6f,
                Power        = Math.Max(3f, e.Power * 0.7f),   // the creeping flame burns lower
                CasterTeam   = e.CasterTeam,
                Generation   = e.Generation + 1,
            };
            child.LightEntity = SpawnAreaLight(p, ColorSchool.Red, 5f);
            _areaEffects.Add(child);   // appended above the reverse loop's cursor — safe
            try { SpawnTempFireParticle(p, 5f); } catch { }
        }

        // Horses fear open flame: a mounted agent beside a burning patch has its
        // mount shy away — a short bolt off the fire line and a cry of fear.
        // Mirrors the Spirit panic's mount handling; fires at the patch tick rate.
        private static void TryScareMountFromFire(Agent rider, Vec3 firePos)
        {
            try
            {
                if (rider?.MountAgent == null || !rider.MountAgent.IsActive()) return;
                Vec3 away = rider.Position - firePos; away.z = 0f;
                if (away.Length > 0.1f) away.Normalize(); else away = new Vec3(1f, 0f, 0f);
                rider.MountAgent.TeleportToPosition(rider.MountAgent.Position + away * 2.5f);
                try { rider.MountAgent.MakeVoice(SkinVoiceManager.VoiceType.Fear, SkinVoiceManager.CombatVoiceNetworkPredictionType.NoPrediction); } catch { }
            }
            catch { }
        }

        // Spawns a wall of BarrierNodeCount nodes perpendicular to the caster's
        // forward. Each node holds two persistent particle columns (low + high) and a
        // coloured point light for its full duration; repulsion and elemental effects
        // pulse at BarrierTickInterval for as long as the wall stands.
        public static void SpawnNatureBarrier(Vec3 pos, Vec3 lookDir, NatureElement el, Team casterTeam, float castPower = 1f)
        {
            // Flatten to the horizontal plane.
            Vec3 fwd = new Vec3(lookDir.x, lookDir.y, 0f);
            float fLen = (float)Math.Sqrt(fwd.x * fwd.x + fwd.y * fwd.y);
            if (fLen < 0.01f) fwd = new Vec3(1f, 0f, 0f);
            else { fwd.x /= fLen; fwd.y /= fLen; }
            Vec3 right = new Vec3(-fwd.y, fwd.x, 0f);

            string   id      = BarrierNodeId(el);
            Vec3     rgb     = NatureBarrierLightRgb(el);
            string[] pNames  = NatureParticleNames(el);
            float    dur     = NatureMath.BarrierDuration;
            float    spacing = NatureMath.BarrierNodeSpacing;
            float    fwdD    = NatureMath.BarrierForwardDist;
            float    nodeR   = NatureMath.BarrierNodeRadius;
            int      count   = NatureMath.BarrierNodeCount;
            // A fully-drawn wall runs several rows deep — a filled rectangle rather
            // than a single curtain — matching the charged fire wall.
            int      rows    = Math.Min(NatureMath.BarrierMaxDepthRows, ElementMagicMath.WallDepthRows(castPower));

            RemoveAreaEffect(id);
            // The replaced wall's warding falls with it (same replacement scope).
            try { ElementWallWards.ClearBySourceKey(id); } catch { }

            for (int row = 0; row < rows; row++)
            {
                float rowFwd = fwdD + row * NatureMath.BarrierRowSpacing;
                for (int i = 0; i < count; i++)
                {
                    float lateral = (i - (count - 1) * 0.5f) * spacing;
                    Vec3  nodePos = pos + fwd * rowFwd + right * lateral;
                    nodePos.z = pos.z;

                    // Lower particle column (ground level).
                    GameEntity partLow = null;
                    foreach (string name in pNames)
                    {
                        partLow = SpawnParticleEntity(nodePos + new Vec3(0f, 0f, 0.25f), name);
                        if (partLow != null) break;
                    }

                    // Upper particle column for a taller, more imposing wall.
                    GameEntity partHigh = null;
                    foreach (string name in pNames)
                    {
                        partHigh = SpawnParticleEntity(nodePos + new Vec3(0f, 0f, 1.1f), name);
                        if (partHigh != null) break;
                    }

                    var node = new AreaEffect
                    {
                        Id           = id,
                        School       = ColorSchool.Nature,
                        Position     = nodePos,
                        Radius       = nodeR,
                        TickInterval = NatureMath.BarrierTickInterval,
                        TickTimer    = NatureMath.BarrierTickInterval,
                        Remaining    = dur,
                        CasterTeam   = casterTeam,
                        // Middle rows of a deep wall are hidden inside the churn of the
                        // outer rows — their per-tick visuals cost frames and show nothing.
                        InteriorNode = row > 0 && row < rows - 1,
                    };
                    node.LightEntity  = partLow;                                                          // lower particle
                    node.LightEntity2 = partHigh;                                                         // upper particle
                    node.LightEntity3 = SpawnAreaLightRaw(nodePos + new Vec3(0f, 0f, 0.8f), rgb, 8f);    // glow column
                    _areaEffects.Add(node);

                    // The wall WARDS while it stands: wind and stone turn missiles
                    // (and each other's element), water quenches fire and drinks
                    // the gale. Storm is the gale's kin and wards as wind does.
                    try
                    {
                        MagicElement wardEl = el == NatureElement.Earth ? MagicElement.Earth
                                            : el == NatureElement.Water ? MagicElement.Water
                                            :                             MagicElement.Wind;
                        ElementWallWards.RegisterNode(wardEl, nodePos, nodeR, dur, casterTeam, id);
                    }
                    catch { }
                }
            }
        }

        private static void TickNatureBarrierNode(AreaEffect e)
        {
            NatureElement el = BarrierNodeElement(e.Id);

            // Animated pulse so the wall feels alive. Per-node cost matters here — a
            // charged wall runs 15 nodes at 2.5 pulses/s, so this used to be the
            // heaviest per-frame load in the mod (full 3-entity clusters at two
            // heights plus a fresh point light per node per pulse). The persistent
            // glow column (LightEntity3) already lights the wall for its whole
            // duration, and single plumes at two heights read the same as clusters
            // once neighbouring nodes (2 m apart) fill the line. Interior rows are
            // hidden inside the outer churn and skip visuals entirely.
            if (!e.InteriorNode)
            {
                string[] pulseNames = NatureParticleNames(el);
                try { SpawnSingleParticle(e.Position + new Vec3(0f, 0f, 0.3f), 0.5f, pulseNames); } catch { }
                try { SpawnSingleParticle(e.Position + new Vec3(0f, 0f, 1.0f), 0.4f, pulseNames); } catch { }
                // A wall of wind over desert sand stands inside its own dust-devil.
                if (el == NatureElement.Wind && _rng.Next(2) == 0 && SceneIsDesert())
                    try { SpawnSingleParticle(e.Position + new Vec3(0f, 0f, 0.2f), 0.5f, NatureParticleNames(NatureElement.Earth)); } catch { }
                if (el == NatureElement.Storm)
                    try { SpawnTempLightWhite(e.Position + new Vec3(0f, 0f, 1.2f), 4f, 0.25f); } catch { }
            }

            if (Mission.Current == null) return;
            float r2 = e.Radius * e.Radius;
            foreach (Agent a in BarrierAgentSnapshot())
            {
                if (!a.IsActive() || a.IsMount || a.Health <= 0f) continue;
                if (e.CasterTeam != null && a.Team == e.CasterTeam) continue;
                if ((a.Position - e.Position).LengthSquared > r2) continue;

                try
                {
                    Vec3 away = a.Position - e.Position;
                    if (away.Length < 0.01f)
                        away = new Vec3((float)(_rng.NextDouble() - 0.5), (float)(_rng.NextDouble() - 0.5), 0f);
                    away = away.NormalizedCopy();
                    away.z = 0f;

                    // Firm barrier: shove the foe back to just beyond the node's edge
                    // rather than a small nudge a runner would out-pace, so the wall
                    // genuinely cannot be walked through. Each element then layers its
                    // own bite (root/slow/arc) on top.
                    Vec3 bounce = e.Position + away * (e.Radius + NatureMath.BarrierBounceMargin);
                    bounce.z = a.Position.z;
                    // Mount-safe: a mounted rider is bounced by moving his HORSE,
                    // never by teleporting the rider out of the saddle.
                    NatureEffects.KnockbackAgent(a, bounce);

                    switch (el)
                    {
                        case NatureElement.Wind:
                            // Pure repulsion — powerful, instantaneous gust.
                            try { SpawnNatureBurst(a.Position, NatureElement.Wind, 0.4f); } catch { }
                            break;

                        case NatureElement.Earth:
                            // Thrown into thorns + rooted + bled. The bite goes
                            // through DamageAgent so heroes are floored, never
                            // wall-killed outright, and armour brands apply.
                            NatureEffects.ApplySpeedToken(a, 0f, NatureMath.ThornwallRootSec);
                            try { DamageAgent(a, NatureMath.ThornwallDamage); } catch { }
                            try { SpawnNatureBurst(a.Position, NatureElement.Earth, 0.4f); } catch { }
                            break;

                        case NatureElement.Water:
                            // Churning bounce + lingering slow + a cold bite. The
                            // mist also douses a burning man to a puff of steam.
                            try { ElementSpellEffects.QuenchIgnition(a); } catch { }
                            NatureEffects.ApplySpeedToken(a, NatureMath.MistwallSlowMult, NatureMath.MistwallSlowSec);
                            try { DamageAgent(a, NatureMath.MistwallDamage); } catch { }
                            try { SpawnNatureBurst(a.Position, NatureElement.Water, 0.4f); } catch { }
                            break;

                        case NatureElement.Storm:
                            // Lightning discharge — arc damage.
                            try { DamageAgent(a, NatureMath.StormwallDamage); } catch { }
                            try { SpawnTempLightWhite(a.Position + new Vec3(0f, 0f, 0.6f), 4f, 0.2f); } catch { }
                            try { SpawnNatureBurst(a.Position, NatureElement.Storm, 0.4f); } catch { }
                            break;
                    }
                }
                catch { }
            }
        }

        // Shared agent snapshot for the barrier-node pulses. All nodes of a wall
        // pulse on the same tick, so without this every node copied the full
        // Mission agent list for itself (15 copies per pulse on a charged wall).
        // The snapshot is reused within a short window; IsActive()/Health guards
        // in the consumer keep any agent that died inside the window harmless.
        private static List<Agent> _barrierAgentSnapshot;
        private static float _barrierSnapshotTime = -1f;

        private static List<Agent> BarrierAgentSnapshot()
        {
            var mission = Mission.Current;
            if (mission == null) return _barrierAgentSnapshot ?? (_barrierAgentSnapshot = new List<Agent>());
            float now = mission.CurrentTime;
            if (_barrierAgentSnapshot == null || now < _barrierSnapshotTime || now - _barrierSnapshotTime > 0.1f)
            {
                try { _barrierAgentSnapshot = mission.Agents.ToList(); } catch { _barrierAgentSnapshot = new List<Agent>(); }
                _barrierSnapshotTime = now;
            }
            return _barrierAgentSnapshot;
        }

        private static string BarrierNodeId(NatureElement el)
        {
            switch (el)
            {
                case NatureElement.Wind:  return "nature_barrier_wind";
                case NatureElement.Earth: return "nature_barrier_earth";
                case NatureElement.Water: return "nature_barrier_water";
                case NatureElement.Storm: return "nature_barrier_storm";
                default: return "nature_barrier_wind";
            }
        }

        private static NatureElement BarrierNodeElement(string id)
        {
            switch (id)
            {
                case "nature_barrier_wind":  return NatureElement.Wind;
                case "nature_barrier_earth": return NatureElement.Earth;
                case "nature_barrier_water": return NatureElement.Water;
                case "nature_barrier_storm": return NatureElement.Storm;
                default: return NatureElement.Wind;
            }
        }

        // Public accessor so the Nature combat code can light its attack shapes in
        // the matching element colour (the raw debris particles are one-shot and
        // flash too briefly to read as an eruption — bright lights carry the shape).
        internal static Vec3 NatureElementRgb(NatureElement el) => NatureBarrierLightRgb(el);

        private static Vec3 NatureBarrierLightRgb(NatureElement el)
        {
            switch (el)
            {
                case NatureElement.Wind:  return new Vec3(0.88f, 0.92f, 1.00f); // pale white-blue
                case NatureElement.Earth: return new Vec3(0.55f, 0.62f, 0.24f); // mossy earth (stone + root)
                case NatureElement.Water: return new Vec3(0.20f, 0.60f, 1.00f); // deep blue
                case NatureElement.Storm: return new Vec3(1.00f, 1.00f, 1.00f); // stark white
                default: return new Vec3(0.88f, 0.92f, 1.00f);
            }
        }

    }
}
