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
            public int    Generation;  // fire creep: 0 = the cast patch; children inherit +1
            public bool   InteriorNode; // barrier: inner-row node hidden inside the wall — skips per-tick visuals
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
                try { _areaEffects[idx].LightEntity?.Remove(0); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { _areaEffects[idx].LightEntity2?.Remove(0); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { _areaEffects[idx].LightEntity3?.Remove(0); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                try { e.LightEntity?.Remove(0); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { e.LightEntity2?.Remove(0); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { e.LightEntity3?.Remove(0); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            _areaEffects.RemoveAll(e => e.Id == id);
        }

        public static bool HasAreaEffect(string id) => _areaEffects.Any(e => e.Id == id);

        // Spawns a point light using a raw RGB color — for nature elements where each element
        // has its own color that doesn't map to an existing ColorSchool.
        public static void SpawnTempLightRgb(Vec3 position, Vec3 rgb, float radius, float duration)
        {
            var node = new AreaEffect
            {
                Id = "temp_light", School = ColorSchool.Nature,
                Position = position, Radius = radius,
                TickInterval = duration, TickTimer = duration,
                Remaining = duration
            };
            node.LightEntity = SpawnAreaLightRaw(position, rgb, radius);
            _areaEffects.Add(node);
        }

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

    }
}
