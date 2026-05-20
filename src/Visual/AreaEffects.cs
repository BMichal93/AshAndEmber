// =============================================================================
// COLOURS OF CALRADIA — AreaEffects.cs
// Mount & Blade II: Bannerlord Mod  v2.0
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
        }
        private static readonly List<AreaEffect> _areaEffects = new List<AreaEffect>();
        // AgentIndex → (seconds remaining, position frozen at cast time) for Azure Arrest halt
        private static readonly Dictionary<int, (float Remaining, Vec3 FrozenPos)> _haltedAgents
            = new Dictionary<int, (float, Vec3)>();
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

        // Returns true if the given position is inside any active Orange retributive aura node.
        public static bool IsInsideOrangeAura(Vec3 pos)
        {
            foreach (var e in _areaEffects)
                if (e.Id == "create_orange" && e.Position.Distance(pos) <= e.Radius)
                    return true;
            return false;
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

        private static GameEntity SpawnAreaLight(Vec3 position, ColorSchool school, float radius)
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
                light.LightColor    = SchoolToLightColor(school);
                light.ShadowEnabled = false;
                entity.AddLight(light);
                return entity;
            }
            catch { return null; }
        }

        private static Vec3 SchoolToLightColor(ColorSchool school)
        {
            switch (school)
            {
                case ColorSchool.Red:    return new Vec3(1f,    0.15f, 0.05f);
                case ColorSchool.Orange: return new Vec3(1f,    0.50f, 0.05f);
                case ColorSchool.Yellow: return new Vec3(1f,    0.90f, 0.10f);
                case ColorSchool.Green:  return new Vec3(0.15f, 0.80f, 0.15f);
                case ColorSchool.Blue:   return new Vec3(0.10f, 0.35f, 1f);
                case ColorSchool.Purple: return new Vec3(0.60f, 0.10f, 0.90f);
                default:                 return new Vec3(1f,    1f,    1f);
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
                if (e.Id == "create_yellow" || e.Id == "self_yellow")
                {
                    e.DirTimer -= dt;
                    if (e.DirTimer <= 0f)
                    {
                        float angle    = (float)(_rng.NextDouble() * Math.PI * 2);
                        Vec3  newVel   = new Vec3((float)Math.Cos(angle) * 2f, (float)Math.Sin(angle) * 2f, 0f);
                        float newTimer = 3f + (float)_rng.NextDouble() * 4f;
                        if (e.Id == "create_yellow")
                        {
                            // Whole cloud turns together — propagate to all nodes (no alloc: plain for loop)
                            for (int j = 0; j < _areaEffects.Count; j++)
                                if (_areaEffects[j].Id == "create_yellow")
                                {
                                    _areaEffects[j].Velocity = newVel;
                                    _areaEffects[j].DirTimer = newTimer;
                                }
                        }
                        else
                        {
                            e.Velocity = newVel;
                            e.DirTimer = newTimer;
                        }
                    }
                    e.Position += e.Velocity * dt;
                }

                // Only moving Yellow clouds need their light repositioned every frame
                if (e.LightEntity != null && (e.Id == "create_yellow" || e.Id == "self_yellow"))
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
                switch (e.Id)
                {
                    case "create_orange": // Golden Recoil — glow agents inside; retribution handled in OnAgentHit
                    {
                        foreach (Agent a in Mission.Current.Agents
                            .Where(a => a.IsActive() && !a.IsMount &&
                                        a.Position.Distance(e.Position) <= e.Radius).ToList())
                        {
                            try { BeginAgentGlow(a, e.School, 2f); } catch { }
                        }
                        break;
                    }

                    case "create_yellow": // Creeping Dread — damage agents in cloud
                    {
                        int dreadHit = 0;
                        float dreadDmg = 30f * e.Power;
                        foreach (Agent a in Mission.Current.Agents
                            .Where(a => a.IsActive() && !a.IsMount &&
                                        a.Position.Distance(e.Position) <= e.Radius).ToList())
                        {
                            if (ProtectedByMirror(a)) continue;
                            try
                            {
                                if (a.Health <= dreadDmg)
                                {
                                    KillAgent(a);
                                }
                                else
                                {
                                    a.Health -= dreadDmg;
                                    try { a.SetMorale(Math.Max(0f, a.GetMorale() - 10f)); } catch { }
                                }
                                BeginAgentGlow(a, e.School, 1.5f);
                                dreadHit++;
                            }
                            catch { }
                        }
                        if (dreadHit > 0)
                            Msg($"Creeping Dread: {dreadHit} caught in the cloud. (−{dreadDmg:F0} HP)", ColorSchool.Yellow);
                        break;
                    }

                    case "create_green": // Emerald Font — heal all agents in area
                    {
                        float fontHeal = 15f * e.Power;
                        foreach (Agent a in Mission.Current.Agents
                            .Where(a => a.IsActive() && !a.IsMount &&
                                        a.Position.Distance(e.Position) <= e.Radius).ToList())
                        {
                            try
                            {
                                float h = Math.Min(fontHeal, a.HealthLimit - a.Health);
                                if (h > 0f) { a.Health += h; BeginAgentGlow(a, e.School, 1.5f); }
                            }
                            catch { }
                        }
                        break;
                    }

                    case "create_blue": // Sapphire Bastion — push all agents inside the radius every tick
                    {
                        foreach (Agent a in Mission.Current.Agents
                            .Where(a => a.IsActive() && !a.IsMount &&
                                        a.Position.Distance(e.Position) <= e.Radius).ToList())
                        {
                            try
                            {
                                Vec3 dir = (a.Position - e.Position);
                                if (dir.Length < 0.01f) dir = new Vec3(1f, 0f, 0f);
                                else dir = dir.NormalizedCopy();
                                Vec3 dest = e.Position + dir * (e.Radius + 1f);
                                dest.z = a.Position.z;
                                QueueMove(a, dest, 0.3f);
                                BeginAgentGlow(a, e.School, 1.5f);
                            }
                            catch { }
                        }
                        break;
                    }

                    case "self_yellow": // Nausea Bloom — drifting toxic cloud
                    {
                        int bloomHit = 0;
                        float bloomDmg = 22f * e.Power;
                        foreach (Agent a in Mission.Current.Agents
                            .Where(a => a.IsActive() && !a.IsMount && a != Player &&
                                        a.Position.Distance(e.Position) <= e.Radius).ToList())
                        {
                            if (ProtectedByMirror(a)) continue;
                            try
                            {
                                float before = a.Health;
                                DamageAgent(a, bloomDmg);
                                if (a.Health < before || a.Health <= 0f) bloomHit++;
                                BeginAgentGlow(a, e.School, 1.5f);
                            }
                            catch { }
                        }
                        if (bloomHit > 0)
                            Msg($"Nausea Bloom: {bloomHit} caught in the cloud. (−{bloomDmg:F0} HP)", ColorSchool.Yellow);
                        break;
                    }
                }
            }
        }

        public static void ClearAreaEffects()
        {
            foreach (var e in _areaEffects)
                try { e.LightEntity?.Remove(0); } catch { }
            _areaEffects.Clear();
            _haltedAgents.Clear();
        }
    }
}
