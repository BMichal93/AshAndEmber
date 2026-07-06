// =============================================================================
// ASH AND EMBER — SpellEffects.Combat.cs
// Agent target lookups, damage/heal/kill primitives, cone geometry.
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
        // ── Agent helpers ──────────────────────────────────────────────────────
        private static Agent Player => Agent.Main;

        private static List<Agent> Enemies()
        {
            if (Mission.Current == null || Player == null) return new List<Agent>();
            try
            {
                return Mission.Current.Agents
                    .Where(a => a != Player && !a.IsMount && a.IsActive() &&
                                a.Team != null && a.Team != Player.Team)
                    .ToList();
            }
            catch { return new List<Agent>(); }
        }

        private static List<Agent> Allies()
        {
            if (Mission.Current == null || Player == null) return new List<Agent>();
            try
            {
                return Mission.Current.Agents
                    .Where(a => a != Player && !a.IsMount && a.IsActive() &&
                                a.Team != null && a.Team == Player.Team)
                    .ToList();
            }
            catch { return new List<Agent>(); }
        }

        internal static List<Agent> EnemiesOf(Agent source)
        {
            if (Mission.Current == null || source?.Team == null) return new List<Agent>();
            var result = new List<Agent>();
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (a == source || a.IsMount || !a.IsActive() || a.Team == null) continue;
                    bool isEnemy = false;
                    try { isEnemy = source.Team.IsEnemyOf(a.Team); } catch { continue; }
                    if (isEnemy) result.Add(a);
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            return result;
        }

        internal static List<Agent> AlliesOf(Agent source)
        {
            if (Mission.Current == null || source?.Team == null) return new List<Agent>();
            try
            {
                return Mission.Current.Agents
                    .Where(a => a != source && !a.IsMount && a.IsActive() && a.Team == source.Team)
                    .ToList();
            }
            catch { return new List<Agent>(); }
        }

        // ── Deferred death queue ───────────────────────────────────────────────
        private static readonly List<(Agent Target, Agent Owner)> _pendingDeaths
            = new List<(Agent, Agent)>();

        public static void QueueKill(Agent target, Agent owner = null)
        {
            if (target == null || target.IsHero) return;
            bool usingEquip = false;
            try { usingEquip = target.IsUsingGameObject; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            if (usingEquip) { try { target.Health = 1f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } return; }
            if (target.IsActive() && !_pendingDeaths.Exists(e => e.Target == target))
                _pendingDeaths.Add((target, owner));
        }

        public static void FlushPendingDeaths()
        {
            if (_pendingDeaths.Count == 0) return;
            var mission = Mission.Current;
            if (mission == null || mission.CurrentState != Mission.State.Continuing)
            { _pendingDeaths.Clear(); return; }
            if (Agent.Main == null || !Agent.Main.IsActive())
            { _pendingDeaths.Clear(); return; }
            var snapshot = _pendingDeaths.ToList();
            _pendingDeaths.Clear();
            foreach (var (target, owner) in snapshot)
            {
                if (mission.CurrentState != Mission.State.Continuing) return;
                if (Agent.Main == null || !Agent.Main.IsActive()) return;
                if (target?.IsActive() == true) KillAgent(target, owner);
            }
        }

        public static void ClearPendingDeaths() => _pendingDeaths.Clear();

        public static void KillAgent(Agent target, Agent owner = null)
        {
            if (target == null || !target.IsActive()) return;
            if (target.IsHero)
            { try { target.Health = Math.Max(1f, target.Health - 2f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } return; }
            bool usingEquip = false;
            try { usingEquip = target.IsUsingGameObject; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            if (usingEquip) { try { target.Health = 1f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } return; }
            try
            {
                Blow blow = BuildBlow(target, DamageTypes.Cut, 2000f, owner);
                target.Die(blow, (Agent.KillInfo)0);
                return;
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            if (!target.IsActive()) return;
            try { target.MakeDead(true, ActionIndexCache.Create("act_strike_walk_right_stance"), 0); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        public static void DamageAgent(Agent target, float damage, ColorSchool? school = null, Agent owner = null,
            MagicElement? attackElement = null)
        {
            if (target == null || !target.IsActive()) return;

            // The Kindled: an elemental being drinks its own element and buckles to
            // the one that unmakes it. Only bites when the caller knows which
            // element it threw (fire cone/wall, the nature powers) — a plain hit
            // with no element passes through unchanged.
            if (attackElement.HasValue)
            {
                try { damage *= ElementalBeings.IncomingElementMultiplier(target, attackElement.Value); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }

            // Cinder Shell enchantment: reduce incoming damage
            if (_stoneskinAgents.TryGetValue(target, out var skin) && skin.Remaining > 0f)
            {
                float reduction = Math.Min(0.5f, skin.BonusArmor / 100f);
                damage *= (1f - reduction);
            }

            // Sunder enchantment: increase incoming damage (armour shred, max +50%)
            if (_sunderedAgents.TryGetValue(target, out var sunder) && sunder.Remaining > 0f)
            {
                float amplification = Math.Min(0.50f, sunder.BonusVuln / 100f);
                damage *= (1f + amplification);
            }

            float newHealth = target.Health - damage;
            if (newHealth <= 0f)
            {
                if (!target.IsHero) QueueKill(target, owner);
                else try { target.Health = 1f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            else try { target.Health = newHealth; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // As DamageAgent, but delivers the hurt as a REAL engine blow so the mark
        // visibly reacts — the floating damage number, the flinch, the blood, the
        // pained cry — instead of its health silently ticking down. Use this for
        // player-facing magic strikes (the fire blast) where the caster needs to SEE
        // the hit land. Heroes are still spared the killing blow (clamped to 1 HP),
        // matching DamageAgent. Multipliers (elemental wheel, Cinder Shell, Sunder)
        // are applied here, then baked into the blow with DamageCalculated = true so
        // the engine does not recompute them.
        public static void DamageAgentVisible(Agent target, float damage, Agent owner = null,
            MagicElement? attackElement = null)
        {
            if (target == null || !target.IsActive() || damage <= 0f) return;

            if (attackElement.HasValue)
                try { damage *= ElementalBeings.IncomingElementMultiplier(target, attackElement.Value); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            if (_stoneskinAgents.TryGetValue(target, out var skin) && skin.Remaining > 0f)
                damage *= (1f - Math.Min(0.5f, skin.BonusArmor / 100f));
            if (_sunderedAgents.TryGetValue(target, out var sunder) && sunder.Remaining > 0f)
                damage *= (1f + Math.Min(0.50f, sunder.BonusVuln / 100f));
            if (damage <= 0f) return;

            // Heroes never fall to a spell — cap the blow so at least 1 HP remains.
            if (target.IsHero)
                try { damage = Math.Min(damage, Math.Max(0f, target.Health - 1f)); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            if (damage <= 0f) return;

            try
            {
                Vec3 hitPos = target.Position + new Vec3(0f, 0f, 1.0f);
                Vec3 dir; try { dir = target.Position - (owner?.Position ?? target.Position); dir.z = 0f; if (dir.Length < 0.01f) dir = new Vec3(0f, 1f, 0f); dir.Normalize(); } catch { dir = new Vec3(0f, 1f, 0f); }

                Blow blow = new Blow(owner?.Index ?? -1);
                blow.DamageType       = DamageTypes.Blunt;
                blow.BoneIndex        = 0;
                blow.BaseMagnitude    = damage;
                blow.InflictedDamage  = (int)damage;
                blow.GlobalPosition   = hitPos;
                blow.Direction        = dir;
                blow.SwingDirection   = dir;
                blow.DamageCalculated = true;
                blow.VictimBodyPart   = BoneBodyPartType.Chest;
                blow.StrikeType       = StrikeType.Swing;
                blow.AttackType       = AgentAttackType.Standard;
                blow.WeaponRecord     = new BlowWeaponRecord();

                AttackCollisionData acd = AttackCollisionData.GetAttackCollisionDataForDebugPurpose(
                    false, false, false, true, false, false, false, false, false, false, false, false,
                    CombatCollisionResult.StrikeAgent, -1, (int)StrikeType.Swing, (int)DamageTypes.Blunt,
                    0, BoneBodyPartType.Chest, -1, Agent.UsageDirection.AttackDown, -1,
                    CombatHitResultFlags.NormalHit, 0.5f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
                    new Vec3(0f, 0f, 1f), dir, hitPos, Vec3.Zero, Vec3.Zero, Vec3.Zero, new Vec3(0f, 0f, 1f));

                target.RegisterBlow(blow, in acd);
            }
            catch (System.Exception logEx)
            {
                AshAndEmber.ModLog.Error(logEx);
                // Fallback: if the blow could not be registered, fail safe to the
                // silent health drain so the strike is never simply lost. Multipliers
                // are already baked into `damage`, so pass no element (no re-scaling).
                try { DamageAgent(target, damage, ColorSchool.Red, owner, null); } catch (System.Exception logEx2) { AshAndEmber.ModLog.Error(logEx2); }
            }
        }

        public static void HealAgent(Agent target, float amount)
        {
            if (target == null || !target.IsActive()) return;
            try { target.Health = Math.Min(target.HealthLimit, target.Health + amount); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static Blow BuildBlow(Agent target, DamageTypes type, float magnitude, Agent owner = null)
        {
            Blow blow = new Blow();
            blow.OwnerId          = (owner ?? Agent.Main)?.Index ?? 0;
            blow.DamageType       = type;
            blow.BaseMagnitude    = magnitude;
            blow.InflictedDamage  = (int)magnitude;
            blow.GlobalPosition   = target.Position;
            blow.Direction        = new Vec3(0f, 0f, 1f);
            blow.WeaponRecord     = new BlowWeaponRecord();
            blow.DamageCalculated = true;
            blow.NoIgnore         = true;
            blow.StrikeType       = StrikeType.Invalid;
            blow.VictimBodyPart   = BoneBodyPartType.Chest;
            blow.AttackType       = AgentAttackType.Standard;
            blow.BlowFlag         = BlowFlags.NoSound;
            return blow;
        }

        // ── Cone geometry ─────────────────────────────────────────────────────
        internal static List<Agent> ConeAgents(Vec3 origin, Vec3 fwd, float range, float dot)
        {
            if (Mission.Current == null) return new List<Agent>();
            var result = new List<Agent>();
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount || a == Player) continue;
                    Vec3 to = a.Position - origin;
                    if (to.Length > range) continue;
                    if (Vec3.DotProduct(fwd, to.NormalizedCopy()) < dot) continue;
                    result.Add(a);
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            return result;
        }

        internal static List<Agent> ConeAgentsFrom(Agent source, float range, float dot)
        {
            if (Mission.Current == null || source == null) return new List<Agent>();
            Vec3 fwd = source.LookDirection.NormalizedCopy();
            var result = new List<Agent>();
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount || a == source) continue;
                    if (source.Team != null && a.Team == source.Team) continue;
                    Vec3 to = a.Position - source.Position;
                    if (to.Length > range) continue;
                    if (Vec3.DotProduct(fwd, to.NormalizedCopy()) < dot) continue;
                    result.Add(a);
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            return result;
        }

        internal static int CountEnemiesInCone(Agent source, float range, float dot)
            => ConeAgentsFrom(source, range, dot).Count;

        internal static int CountAlliesInCone(Agent source, float range, float dot)
        {
            if (Mission.Current == null || source == null || source.Team == null) return 0;
            Vec3 fwd = source.LookDirection.NormalizedCopy();
            int count = 0;
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount || a == source || a.Team == null) continue;
                    if (a.Team != source.Team) continue;
                    Vec3 to = a.Position - source.Position;
                    if (to.Length > range) continue;
                    if (Vec3.DotProduct(fwd, to.NormalizedCopy()) < dot) continue;
                    count++;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            return count;
        }

        internal static int CountAlliesInRadius(Agent source, float range)
        {
            if (Mission.Current == null || source == null || source.Team == null) return 0;
            int count = 0;
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount || a == source || a.Team == null) continue;
                    if (a.Team != source.Team) continue;
                    if (a.Position.Distance(source.Position) > range) continue;
                    count++;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            return count;
        }

    }
}
