// =============================================================================
// COLOURS OF CALRADIA — GlowSystem.cs
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
        // =================================================================
        // VISUAL SYSTEM  — per-school coloured glow
        // =================================================================
        private static readonly List<(Agent agent, float remaining)> _glowTimers
            = new List<(Agent, float)>();

        // Called every mission tick — clears expired timers and removes glows from dead agents.
        public static void TickGlows(float dt)
        {
            for (int i = _glowTimers.Count - 1; i >= 0; i--)
            {
                var a = _glowTimers[i].agent;
                // If the agent died mid-timer, clear immediately so corpses don't stay glowing.
                if (a == null || !a.IsActive() || a.Health <= 0f)
                {
                    if (a != null)
                        try { a.AgentVisuals?.GetEntity()?.SetContourColor(null, false); } catch { }
                    _glowTimers.RemoveAt(i);
                    continue;
                }
                float t = _glowTimers[i].remaining - dt;
                if (t <= 0f)
                {
                    try { a.AgentVisuals?.GetEntity()?.SetContourColor(null, false); } catch { }
                    _glowTimers.RemoveAt(i);
                }
                else
                {
                    _glowTimers[i] = (a, t);
                }
            }
        }

        public static void ClearGlows()
        {
            foreach (var (agent, _) in _glowTimers)
                if (agent != null && agent.IsActive() && agent.Health > 0f)
                    try { agent.AgentVisuals?.GetEntity()?.SetContourColor(null, false); } catch { }
            _glowTimers.Clear();
        }


        public static void BeginAgentGlow(Agent agent, ColorSchool school, float duration)
        {
            if (agent == null || !agent.IsActive()) return;
            try
            {
                agent.AgentVisuals?.GetEntity()
                    ?.SetContourColor(ColorSchoolData.GetGlowColor(school), true);
                int idx = _glowTimers.FindIndex(x => x.agent == agent);
                if (idx >= 0) _glowTimers.RemoveAt(idx);
                _glowTimers.Add((agent, duration));
            }
            catch { }
        }

        public static void BeginAgentGlowWhite(Agent agent, float duration)
        {
            if (agent == null || !agent.IsActive()) return;
            try
            {
                agent.AgentVisuals?.GetEntity()
                    ?.SetContourColor(ColorSchoolData.GetGlowColor(ColorSchool.White), true);
                int idx = _glowTimers.FindIndex(x => x.agent == agent);
                if (idx >= 0) _glowTimers.RemoveAt(idx);
                _glowTimers.Add((agent, duration));
            }
            catch { }
        }

        public static void BeginAgentGlowRaw(Agent agent, uint rawArgb, float duration)
        {
            if (agent == null || !agent.IsActive()) return;
            try
            {
                agent.AgentVisuals?.GetEntity()?.SetContourColor(rawArgb, true);
                int idx = _glowTimers.FindIndex(x => x.agent == agent);
                if (idx >= 0) _glowTimers.RemoveAt(idx);
                _glowTimers.Add((agent, duration));
            }
            catch { }
        }

        // ── Inner Fire Heat (glow intensity — scales with recent aging spend) ─────
        // Each battle spell cast adds heat proportional to its aging cost.
        // The heat decays passively and, above a threshold, radiates a visible
        // ambient light around the caster — the inner fire running closer to the surface.
        private static float _innerFireHeat  = 0f;
        private static float _heatLightTimer = 0f;
        private const  float HeatDecayRate   = 2f;   // per second
        private const  float HeatLightInterval = 0.5f;

        public static void AddCastHeat(float agingDays)
        {
            _innerFireHeat = Math.Min(100f, _innerFireHeat + agingDays * 2f);
        }

        public static void TickInnerFireHeat(float dt)
        {
            if (_innerFireHeat <= 0f) return;
            _innerFireHeat = Math.Max(0f, _innerFireHeat - HeatDecayRate * dt);
            if (_innerFireHeat < 10f || Agent.Main?.IsActive() != true) return;
            _heatLightTimer -= dt;
            if (_heatLightTimer > 0f) return;
            _heatLightTimer = HeatLightInterval;
            float radius = 3f + _innerFireHeat * 0.15f;
            try { SpawnTempLight(Agent.Main.Position + new Vec3(0f, 0f, 1f), ColorSchool.Red, radius, HeatLightInterval + 0.1f); }
            catch { }
        }

        public static void ClearInnerFireHeat()
        {
            _innerFireHeat  = 0f;
            _heatLightTimer = 0f;
        }

        public static void CastGlow(Agent caster, ColorSchool school)
        {
            if (caster == null) return;
            try
            {
                BeginAgentGlow(caster, school, 8.0f);
                SpawnTempLight(caster.Position, school, 18f, 7f);
                TryCastSound(caster.Position, school);
                TryCastAnimation(caster);
            }
            catch { }
        }
    }
}
