// =============================================================================
// ASH AND EMBER — SpellEffects.Battlefield.cs
// Dismount, magic-event memory, AOE scatter, and battle commands.
// Partial of SpellEffects (shared state lives in SpellEffects.cs).
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
        // ── Dismount helper ────────────────────────────────────────────────────
        public static void ForceDismount(Agent a, Agent owner = null)
        {
            Agent mount = null;
            try { mount = a.MountAgent; } catch { }
            if (mount == null || !mount.IsActive()) return;
            try
            {
                Blow b = BuildBlow(mount, DamageTypes.Blunt, mount.HealthLimit + 1f, owner);
                mount.Die(b, (Agent.KillInfo)0);
            }
            catch { }
        }

        // ── Magic-event memory ─────────────────────────────────────────────────
        private static readonly List<(Vec3 Pos, float Left)> _recentMagicEvents
            = new List<(Vec3, float)>();
        private const float MagicMemoryDuration = 8f;

        public static void RecordMagicCast(Vec3 position)
            => _recentMagicEvents.Add((position, MagicMemoryDuration));

        public static bool HasRecentMagicNearby(Vec3 position, float radius)
        {
            foreach (var e in _recentMagicEvents)
                if (e.Pos.Distance(position) <= radius) return true;
            return false;
        }

        public static void TickMagicMemory(float dt)
        {
            for (int i = _recentMagicEvents.Count - 1; i >= 0; i--)
            {
                float left = _recentMagicEvents[i].Left - dt;
                if (left <= 0f) _recentMagicEvents.RemoveAt(i);
                else _recentMagicEvents[i] = (_recentMagicEvents[i].Pos, left);
            }
        }

        public static void ClearMagicMemory() => _recentMagicEvents.Clear();

        // ── AOE scatter ───────────────────────────────────────────────────────
        // Nudges nearby non-hero enemies outward from an AOE impact point so
        // surviving units visibly flee the blast zone.  Heroes hold their ground.
        // scatterRadius extends beyond hitRadius so units just outside the hit
        // zone react too (fear response from witnessing the spell).
        internal static void ScatterEnemies(Vec3 center, float hitRadius, Team casterTeam)
        {
            if (Mission.Current == null) return;
            float scatterRadius = hitRadius + 3f;
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount || a.IsHero) continue;
                    if (casterTeam != null && a.Team == casterTeam) continue;
                    float dx = a.Position.x - center.x;
                    float dy = a.Position.y - center.y;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (dist > scatterRadius) continue;
                    // Outward direction with ±~20° random lateral variation
                    float len = dist > 0.01f ? dist : 1f;
                    Vec3 outDir = new Vec3(dx / len, dy / len, 0f);
                    float angle = ((float)_rng.NextDouble() - 0.5f) * 0.7f;
                    float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
                    Vec3 scatterDir = new Vec3(
                        outDir.x * cos - outDir.y * sin,
                        outDir.x * sin + outDir.y * cos, 0f);
                    float scatterDist = 2.5f + (float)_rng.NextDouble() * 2.5f;
                    Vec3 target = new Vec3(
                        a.Position.x + scatterDir.x * scatterDist,
                        a.Position.y + scatterDir.y * scatterDist,
                        a.Position.z);
                    try { QueueMove(a, target, 0.5f); } catch { }
                }
            }
            catch { }
        }

        // ── Battle command ─────────────────────────────────────────────────────
        public enum BattleCommandKind { Halt, Enrage, Dismount, StopArrows }

        public static void IssueBattleCommand(Agent source, BattleCommandKind kind,
            string successText, ColorSchool school)
        {
            if (source == null || Mission.Current == null || Mission.Current.Scene == null) return;
            var formations = new HashSet<Formation>();
            var scene = Mission.Current.Scene;
            foreach (Agent a in EnemiesOf(source).ToList())
            {
                if (a.Formation == null) continue;
                if (a.Position.Distance(source.Position) > 500f) continue;
                bool visible = true;
                try { visible = scene.CheckPointCanSeePoint(source.Position, a.Position, 500f); } catch { }
                if (!visible) continue;
                formations.Add(a.Formation);
                BeginAgentGlow(a, school, 1.5f);
            }
            if (formations.Count == 0) return;
            foreach (Formation f in formations)
            {
                try
                {
                    switch (kind)
                    {
                        case BattleCommandKind.Halt:
                            foreach (Agent fa in Mission.Current.Agents.Where(a => a.IsActive() && a.Formation == f).ToList())
                                try { fa.SetMorale(0f); } catch { }
                            break;
                        case BattleCommandKind.Enrage:
                            foreach (Agent fa in Mission.Current.Agents.Where(a => a.IsActive() && a.Formation == f).ToList())
                                try { fa.SetMorale(100f); } catch { }
                            if (!IsSiegeActive()) try { f.SetMovementOrder(MovementOrder.MovementOrderCharge); } catch { }
                            break;
                    }
                }
                catch { }
            }
        }

    }
}
