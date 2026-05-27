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

namespace ColoursOfCalradia
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
                    ?.SetContourColor(0xFFFFFFFF, true);
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

        public static void CastGlow(Agent caster, ColorSchool school)
        {
            if (caster == null) return;
            try
            {
                BeginAgentGlow(caster, school, 5.0f);
                SpawnTempLight(caster.Position, school, 10f, 4f);
                TryCastSound(caster.Position, school);
                TryCastAnimation(caster);
            }
            catch { }
        }
    }
}
