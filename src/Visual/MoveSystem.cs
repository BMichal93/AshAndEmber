// =============================================================================
// COLOURS OF CALRADIA — MoveSystem.cs
// Mount & Blade II: Bannerlord Mod  v1.2.0.0.0
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
            for (int i = _pendingMoves.Count - 1; i >= 0; i--)
                if (_pendingMoves[i].Agent == a) _pendingMoves.RemoveAt(i);
            _pendingMoves.Add(new PendingMove { Agent = a, Start = a.Position, Target = target, Duration = duration, Elapsed = 0f });
        }

        public static void TickMoves(float dt)
        {
            for (int i = _pendingMoves.Count - 1; i >= 0; i--)
            {
                var m = _pendingMoves[i];
                if (m.Agent == null || !m.Agent.IsActive()) { _pendingMoves.RemoveAt(i); continue; }
                float elapsed = m.Elapsed + dt;
                float t = Math.Min(elapsed / m.Duration, 1f);
                float smooth = t * t * (3f - 2f * t); // smoothstep
                Vec3 pos = m.Start + (m.Target - m.Start) * smooth;
                pos.z = m.Agent.Position.z;
                try { m.Agent.TeleportToPosition(pos); } catch { }
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
