// =============================================================================
// ASH AND EMBER — BattleEvents.Helpers.cs
// Spawn helper, general helpers, visual atmosphere, registration.
// Partial of BattleEvents (shared state lives in BattleEvents.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static partial class BattleEvents
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        // Returns true if the agent belongs to the Ashen side.
        // Uses team membership when _ashenTeam is known; falls back to checking
        // the hero's Ashen status directly for hero agents only.
        private static bool IsAshenAgent(Agent agent)
        {
            if (_ashenTeam != null) return agent.Team == _ashenTeam;
            if (!agent.IsHero) return false;
            var hero = (agent.Character as CharacterObject)?.HeroObject;
            if (hero == null) return false;
            return hero == Hero.MainHero
                ? MageKnowledge.IsAshen
                : ColourLordRegistry.IsAshenLord(hero);
        }

        private static bool Roll(float chance) => _rng.NextDouble() < chance;

        // Snap a scattered point down onto the scene surface. Field-hazard patches
        // (Cinder Rain, Tremor) damage by 3-D distance, so a point left floating a
        // few metres above the ground would sit harmlessly over the fighters' heads.
        private static void SnapToGround(ref Vec3 pos)
        {
            try
            {
                var scene = Mission.Current?.Scene;
                if (scene == null) return;
                float gz = pos.z;
                scene.GetHeightAtPoint(pos.AsVec2, BodyFlags.CommonCollisionExcludeFlagsForAgent, ref gz);
                pos.z = gz;
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Average position of all active non-mount agents across the whole field.
        private static Vec3 GetFieldCentre()
        {
            if (Mission.Current == null) return Vec3.Zero;
            float x = 0f, y = 0f, z = 0f;
            int   n = 0;
            try
            {
                foreach (var a in Mission.Current.Agents)
                {
                    if (!a.IsActive() || a.IsMount) continue;
                    x += a.Position.x; y += a.Position.y; z += a.Position.z;
                    n++;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            return n == 0 ? Vec3.Zero : new Vec3(x / n, y / n, z / n);
        }

        // Average position of all active non-mount agents on a team.
        private static Vec3 GetTeamCentroid(Team team)
        {
            if (team == null || Mission.Current == null) return Vec3.Zero;
            float x = 0f, y = 0f, z = 0f;
            int   n = 0;
            try
            {
                foreach (var a in Mission.Current.Agents)
                {
                    if (!a.IsActive() || a.IsMount || a.Team != team) continue;
                    x += a.Position.x; y += a.Position.y; z += a.Position.z;
                    n++;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            return n == 0 ? Vec3.Zero : new Vec3(x / n, y / n, z / n);
        }

        // ── Visual atmosphere helpers ─────────────────────────────────────────

        // Sets scene time-of-day for a dramatic sky (only fires once per battle
        // so periodic events can't fight over it with one-shot events).
        private static void TintSky(float timeOfDay)
        {
            if (_skySet) return;
            _skySet = true;
            try { Mission.Current?.Scene.TimeOfDay = timeOfDay; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Applies scene fog via reflection (same pattern as AshenSceneTone).
        // rgb = light colour in linear space; falloff = fog density coefficient.
        private static void ApplyFog(Vec3 rgb, float falloff = 0.004f, float density = 1.0f)
        {
            try
            {
                var scene = Mission.Current?.Scene;
                if (scene == null) return;
                if (!_fogResolved)
                {
                    _fogResolved  = true;
                    _setFogMethod = scene.GetType().GetMethod("SetFog",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }
                if (_setFogMethod == null) return;
                if (_setFogMethod.GetParameters().Length == 3)
                    _setFogMethod.Invoke(scene, new object[] { falloff, rgb, density });
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Scatters fire particles and coloured point lights across the field.
        // Safe to call every event tick — particle/light duration < interval prevents stacking.
        private static void SpawnGroundFireField(Vec3 centre, float radius, int count,
            ColorSchool school, float duration)
        {
            for (int i = 0; i < count; i++)
            {
                try
                {
                    double angle = _rng.NextDouble() * Math.PI * 2;
                    float  dist  = (float)(_rng.NextDouble() * radius);
                    Vec3   pos   = centre + new Vec3((float)Math.Cos(angle) * dist,
                                                     (float)Math.Sin(angle) * dist, 0f);
                    if (school != ColorSchool.Ashen)
                        try { SpellEffects.SpawnTempFireParticle(pos, duration); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    SpellEffects.SpawnTempLight(pos, school, 10f, duration * 0.7f);
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        // Places large-radius glow lights high above the field to simulate a
        // burning or darkened sky reflected onto the scene.
        private static void SpawnAerialGlow(Vec3 centre, float spread, float height,
            int count, ColorSchool school, float duration)
        {
            for (int i = 0; i < count; i++)
            {
                try
                {
                    double angle = _rng.NextDouble() * Math.PI * 2;
                    float  dist  = (float)(_rng.NextDouble() * spread);
                    float  h     = height + (float)(_rng.NextDouble() * 10f);
                    Vec3   pos   = new Vec3(centre.x + (float)Math.Cos(angle) * dist,
                                            centre.y + (float)Math.Sin(angle) * dist,
                                            centre.z + h);
                    SpellEffects.SpawnTempLight(pos, school, 28f, duration);
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        // Spawns fire particles at three stacked heights above each column position.
        // The vertical layers (5 m, 12 m, 22 m) read as fire streaks descending from the sky.
        private static void SpawnFireRainLayer(Vec3 centre, float radius, int columns)
        {
            float[] heights = { 5f, 12f, 22f };
            for (int i = 0; i < columns; i++)
            {
                try
                {
                    double angle = _rng.NextDouble() * Math.PI * 2;
                    float  dist  = (float)(_rng.NextDouble() * radius);
                    float  bx    = centre.x + (float)Math.Cos(angle) * dist;
                    float  by    = centre.y + (float)Math.Sin(angle) * dist;
                    foreach (float h in heights)
                    {
                        Vec3 pos = new Vec3(bx, by, centre.z + h);
                        try { SpellEffects.SpawnTempFireParticle(pos, CinderRainInterval * 0.45f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    }
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        // ── Registration helpers ──────────────────────────────────────────────

        private static void Add(string name, float interval, Action onFire)
        {
            _active.Add(new RunningEvent
            {
                Name     = name,
                Interval = interval,
                Timer    = interval,
                OnFire   = onFire,
            });
        }

        private static void AddOneShot(string name, float delay, Action onFire)
        {
            _active.Add(new RunningEvent
            {
                Name     = name,
                Interval = 0f,
                Timer    = delay,
                OnFire   = onFire,
            });
        }
    }
}
