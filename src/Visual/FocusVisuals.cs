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

        // accumulated: total seconds held — used to grow the focus aura radius over time.
        private static readonly List<(Agent agent, ColorSchool school, float timer, float accumulated)> _focusVisuals
            = new List<(Agent, ColorSchool, float, float)>();

        // ── Public API ─────────────────────────────────────────────────────────

        // Begin a focus visual for this agent. Fires the first pulse immediately
        // (timer = 0) so there is no visible delay on key press.
        public static void BeginFocusVisual(Agent agent, ColorSchool school)
        {
            if (agent == null || !agent.IsActive()) return;
            int idx = _focusVisuals.FindIndex(x => x.agent == agent);
            if (idx >= 0) _focusVisuals.RemoveAt(idx);
            _focusVisuals.Add((agent, school, 0f, 0f));
        }

        // Stop the focus visual and immediately clear the contour glow.
        public static void EndFocusVisual(Agent agent)
        {
            int idx = _focusVisuals.FindIndex(x => x.agent == agent);
            if (idx >= 0) _focusVisuals.RemoveAt(idx);
            if (agent == null) return;
            try { agent.AgentVisuals?.GetEntity()?.SetContourColor(null, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            // Remove any pending glow timer so it cannot restore the colour later.
            int gi = _glowTimers.FindIndex(x => x.agent == agent);
            if (gi >= 0) _glowTimers.RemoveAt(gi);
        }

        // Called every mission frame. Refreshes glow, light, and particles as needed.
        public static void TickFocusVisuals(float dt)
        {
            for (int i = _focusVisuals.Count - 1; i >= 0; i--)
            {
                var (a, school, t, acc) = _focusVisuals[i];
                if (a == null || !a.IsActive() || a.Health <= 0f)
                {
                    _focusVisuals.RemoveAt(i);
                    continue;
                }
                float newT   = t - dt;
                float newAcc = acc + dt;
                if (newT <= 0f)
                {
                    PulseFocusVisual(a, school, newAcc);
                    _focusVisuals[i] = (a, school, FocusVisualInterval, newAcc);
                }
                else
                {
                    _focusVisuals[i] = (a, school, newT, newAcc);
                }
            }
        }

        // One-shot focus aura for NPC casters. NPCs do not "hold" the focus key, so
        // instead of a persistent, key-released entry we fire a single focus pulse
        // (contour glow + warm light + fire particles) the moment they begin a cast.
        // The glow lasts ~FocusGlowDuration, long enough to read as "channelling"
        // through the short NPC wind-up. No teardown needed — the pulse self-expires.
        public static void FlashFocusAura(Agent agent, ColorSchool school)
        {
            if (agent == null || !agent.IsActive() || agent.Health <= 0f) return;
            PulseFocusVisual(agent, school);
        }

        // Clear all focus visuals — called on mission end.
        public static void ClearFocusVisuals()
        {
            foreach (var (a, _, _, _) in _focusVisuals)
            {
                if (a == null) continue;
                try { a.AgentVisuals?.GetEntity()?.SetContourColor(null, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                int gi = _glowTimers.FindIndex(x => x.agent == a);
                if (gi >= 0) _glowTimers.RemoveAt(gi);
            }
            _focusVisuals.Clear();
        }

        // ── Internal ───────────────────────────────────────────────────────────

        // accumulated: seconds the focus has been held — light radius grows 9→15m over 4.5 s.
        private static void PulseFocusVisual(Agent agent, ColorSchool school, float accumulated = 0f)
        {
            Vec3 pos  = agent.Position;
            Vec3 posM = pos + new Vec3(0f, 0f, 0.8f);

            float buildUp   = System.Math.Min(accumulated / 4.5f, 1f); // 0→1 over 4.5 s
            float lightRadius = FocusLightRadius + buildUp * 6f;        // 9→15 m

            BeginAgentGlow(agent, school, FocusGlowDuration);
            SpawnTempLight(posM, school, lightRadius, FocusGlowDuration);

            // Fire schools: add live flame particles at the caster's feet.
            bool isFire = school == ColorSchool.Red
                       || school == ColorSchool.Orange
                       || school == ColorSchool.Purple;
            if (isFire)
                SpawnTempFireParticle(pos, FocusParticleDuration);
        }
    }
}
