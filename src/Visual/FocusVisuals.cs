// =============================================================================
// ASH AND EMBER — Visual/FocusVisuals.cs
// Looping visual aura shown while the player holds the spell/miracle focus key.
// Fires immediately, refreshes every FocusVisualInterval seconds, and is
// cleared the moment the key is released.
//
// Fire schools   → red/orange contour glow + fire particles + warm light.
// Ashen school   → cold-blue contour glow + blue light (no fire particles).
// Grace miracles → amber-gold contour glow + golden light.
// Partial of SpellEffects (shared state lives in AreaEffects.cs / GlowSystem.cs).
// =============================================================================

using System.Collections.Generic;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static partial class SpellEffects
    {
        // Glow duration is slightly longer than the interval so there is no flicker
        // between refresh pulses.
        private const float FocusVisualInterval    = 1.5f;
        private const float FocusGlowDuration      = FocusVisualInterval + 0.4f;
        private const float FocusParticleDuration  = FocusVisualInterval - 0.1f;
        private const float FocusLightRadius       = 9f;

        private static readonly List<(Agent agent, ColorSchool school, float timer)> _focusVisuals
            = new List<(Agent, ColorSchool, float)>();

        // ── Public API ─────────────────────────────────────────────────────────

        // Begin a focus visual for this agent. Fires the first pulse immediately
        // (timer = 0) so there is no visible delay on key press.
        public static void BeginFocusVisual(Agent agent, ColorSchool school)
        {
            if (agent == null || !agent.IsActive()) return;
            int idx = _focusVisuals.FindIndex(x => x.agent == agent);
            if (idx >= 0) _focusVisuals.RemoveAt(idx);
            _focusVisuals.Add((agent, school, 0f));
        }

        // Stop the focus visual and immediately clear the contour glow.
        public static void EndFocusVisual(Agent agent)
        {
            int idx = _focusVisuals.FindIndex(x => x.agent == agent);
            if (idx >= 0) _focusVisuals.RemoveAt(idx);
            if (agent == null) return;
            try { agent.AgentVisuals?.GetEntity()?.SetContourColor(null, false); } catch { }
            // Remove any pending glow timer so it cannot restore the colour later.
            int gi = _glowTimers.FindIndex(x => x.agent == agent);
            if (gi >= 0) _glowTimers.RemoveAt(gi);
        }

        // Called every mission frame. Refreshes glow, light, and particles as needed.
        public static void TickFocusVisuals(float dt)
        {
            for (int i = _focusVisuals.Count - 1; i >= 0; i--)
            {
                var (a, school, t) = _focusVisuals[i];
                if (a == null || !a.IsActive() || a.Health <= 0f)
                {
                    _focusVisuals.RemoveAt(i);
                    continue;
                }
                float newT = t - dt;
                if (newT <= 0f)
                {
                    PulseFocusVisual(a, school);
                    _focusVisuals[i] = (a, school, FocusVisualInterval);
                }
                else
                {
                    _focusVisuals[i] = (a, school, newT);
                }
            }
        }

        // Clear all focus visuals — called on mission end.
        public static void ClearFocusVisuals()
        {
            foreach (var (a, _, _) in _focusVisuals)
            {
                if (a == null) continue;
                try { a.AgentVisuals?.GetEntity()?.SetContourColor(null, false); } catch { }
                int gi = _glowTimers.FindIndex(x => x.agent == a);
                if (gi >= 0) _glowTimers.RemoveAt(gi);
            }
            _focusVisuals.Clear();
        }

        // ── Internal ───────────────────────────────────────────────────────────

        private static void PulseFocusVisual(Agent agent, ColorSchool school)
        {
            Vec3 pos  = agent.Position;
            Vec3 posM = pos + new Vec3(0f, 0f, 0.8f); // mid-body height for light

            BeginAgentGlow(agent, school, FocusGlowDuration);
            SpawnTempLight(posM, school, FocusLightRadius, FocusGlowDuration);

            // Fire schools: add live flame particles at the caster's feet.
            bool isFire = school == ColorSchool.Red
                       || school == ColorSchool.Orange
                       || school == ColorSchool.Purple;
            if (isFire)
                SpawnTempFireParticle(pos, FocusParticleDuration);
        }
    }
}
