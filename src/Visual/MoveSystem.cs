// =============================================================================
// COLOURS OF CALRADIA — MoveSystem.cs
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
        // ── Smooth movement system ────────────────────────────────────────────
        // Moves agents gradually toward a target over a set duration (lerp).
        // Used by push/pull spells for fluid visual movement.
        private struct PendingMove
        {
            public Agent Agent; public Vec3 Start; public Vec3 Target;
            public float Duration; public float Elapsed;
        }
        private static readonly List<PendingMove> _pendingMoves = new List<PendingMove>();

        public static void QueueMove(Agent a, Vec3 target, float duration)
        {
            if (a == null) return;
            target = ValidatePushTarget(a.Position, target);
            for (int i = _pendingMoves.Count - 1; i >= 0; i--)
                if (_pendingMoves[i].Agent == a) _pendingMoves.RemoveAt(i);
            _pendingMoves.Add(new PendingMove { Agent = a, Start = a.Position, Target = target, Duration = duration, Elapsed = 0f });
        }

        // Walk from the raw push target back toward the agent's current position in 1 m steps
        // until we find a point with valid terrain height.  Guards against off-map pushes.
        private static Vec3 ValidatePushTarget(Vec3 from, Vec3 to)
        {
            try
            {
                var scene = Mission.Current?.Scene;
                if (scene == null) return to;

                Vec3 dir = from - to;
                float len = dir.Length;
                if (len < 0.01f) return to;
                dir = dir.NormalizedCopy();

                for (float d = 0f; d <= len + 0.1f; d += 1f)
                {
                    Vec3 c = to + dir * d;
                    try
                    {
                        float h = scene.GetGroundHeightAtPosition(c);
                        if (h > -500f && h < 9000f && !float.IsNaN(h) && !float.IsInfinity(h))
                            return c;
                    }
                    catch { return c; } // API unavailable — assume position is valid
                }
                return from; // no valid point found — cancel push
            }
            catch { return to; }
        }

        public static void TickMoves(float dt)
        {
            for (int i = _pendingMoves.Count - 1; i >= 0; i--)
            {
                var m = _pendingMoves[i];
                if (m.Agent == null || !m.Agent.IsActive()) { _pendingMoves.RemoveAt(i); continue; }
                bool mounted = false;
                try { mounted = m.Agent.MountAgent != null; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                if (mounted) continue; // pause until dismount propagates, then push
                try { if (m.Agent.IsUsingGameObject) { _pendingMoves.RemoveAt(i); continue; } } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                float elapsed = m.Elapsed + dt;
                float t = Math.Min(elapsed / m.Duration, 1f);
                float smooth = t * t * (3f - 2f * t); // smoothstep
                Vec3 pos = m.Start + (m.Target - m.Start) * smooth;
                pos.z = m.Agent.Position.z;
                try { m.Agent.TeleportToPosition(pos); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                if (elapsed >= m.Duration) _pendingMoves.RemoveAt(i);
                else _pendingMoves[i] = new PendingMove { Agent = m.Agent, Start = m.Start, Target = m.Target, Duration = m.Duration, Elapsed = elapsed };
            }
        }

        public static void ClearMoves()
        {
            _pendingMoves.Clear();
        }
    }
}
