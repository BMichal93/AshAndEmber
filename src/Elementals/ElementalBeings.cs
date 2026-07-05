// =============================================================================
// ASH AND EMBER — Elementals/ElementalBeings.cs
//
// Runtime registry of THE KINDLED — every elemental being alive in the current
// mission, whatever spawned it (the Spirit Unbinding's champion, an enemy mage's
// summon, a wild band the player marched on, or a battle's Kindling). One place
// holds them so ONE piece of code drives their look and their weakness:
//
//   • TickAuras(dt)   — re-emits the elemental "coat" at each body so the fire /
//                       water / stone / storm clings to it as it moves.
//   • IncomingElementMultiplier(target, attack) — magical weakness, read by
//                       SpellEffects.DamageAgent before a spell lands.
//   • OnWeaponHit(...) — physical weakness (stone shatters to blunt, blades pass
//                       through flame), applied after a real weapon blow.
//
// All state is mission-scoped and cleared with the rest of the battle state
// (ClearBattleState) — nothing here is serialized, so saves are untouched.
//
// The pure tables (which element unmakes which, how much) live in ElementalMath.
// Spawning lives in ElementalFactory. Wild-band map spawning lives in
// ElementalWildsBehavior.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static class ElementalBeings
    {
        private class Being
        {
            public Agent Agent;
            public ElementalKind Kind;
            public float AuraTimer;
        }

        private static readonly List<Being> _beings = new List<Being>();
        private static readonly Dictionary<Agent, ElementalKind> _kindOf = new Dictionary<Agent, ElementalKind>();

        // Set when the player's mission involves a wild elemental band, so the
        // OnAgentBuild hook knows to remake the enemy bodies into that kind. Null
        // in every ordinary battle. Cleared at mission end.
        public static ElementalKind? PendingBattleKind = null;

        // ── Registry ─────────────────────────────────────────────────────────────
        public static void Register(Agent agent, ElementalKind kind)
        {
            if (agent == null) return;
            if (_kindOf.ContainsKey(agent)) { _kindOf[agent] = kind; return; }
            _kindOf[agent] = kind;
            _beings.Add(new Being { Agent = agent, Kind = kind, AuraTimer = 0f });
        }

        public static bool IsElemental(Agent agent)
            => agent != null && _kindOf.ContainsKey(agent);

        public static bool TryGetKind(Agent agent, out ElementalKind kind)
        {
            if (agent != null && _kindOf.TryGetValue(agent, out kind)) return true;
            kind = ElementalKind.Stone;
            return false;
        }

        public static void ClearBattleState()
        {
            _beings.Clear();
            _kindOf.Clear();
            PendingBattleKind = null;
        }

        // ── Wild-band conversion (called from OnAgentBuild) ──────────────────────
        // Remakes an enemy body into a Kindled of the pending kind: registers it
        // for the aura + weakness, and strips its mount so a being of raw magic
        // never rides a horse. It KEEPS its weapons (a marauder the power has
        // claimed and wreathed) — the pure, weaponless beings come from the
        // factory instead.
        public static void ConvertBattleAgent(Agent agent)
        {
            if (PendingBattleKind == null || agent == null || !agent.IsActive()) return;
            if (agent.IsMount || agent.IsPlayerControlled) return;
            try { if (agent.IsHero) return; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            // Only the side that ISN'T the player's becomes elemental.
            try
            {
                Team pt = Mission.Current?.PlayerTeam;
                if (pt != null && agent.Team != null && agent.Team.IsValid &&
                    (agent.Team == pt || agent.Team.IsPlayerAlly)) return;
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            Register(agent, PendingBattleKind.Value);
            try { agent.HealthLimit = Math.Max(agent.HealthLimit, ElementalMath.Health(PendingBattleKind.Value)); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { agent.Health = agent.HealthLimit; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Aura tick (driven by MagicMissionBehavior.OnMissionTick) ─────────────
        private static float _aggroTimer = 0f;

        public static void TickAuras(float dt)
        {
            if (_beings.Count == 0) return;

            // Every few seconds, re-rouse the enemy-side Kindled so a body whose
            // charge order has lapsed (formation reformed, target lost) does not
            // fall idle. The player's own summoned champion is left to agent AI so
            // this never hijacks the player's battle orders.
            _aggroTimer -= dt;
            bool reAggro = _aggroTimer <= 0f;
            if (reAggro) _aggroTimer = 4f;

            Vec3 eye = default(Vec3); bool haveEye = false;
            try { if (Agent.Main != null && Agent.Main.IsActive()) { eye = Agent.Main.Position; haveEye = true; } } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            for (int i = _beings.Count - 1; i >= 0; i--)
            {
                Being b = _beings[i];
                bool alive = false;
                try { alive = b.Agent != null && b.Agent.IsActive() && b.Agent.Health > 0f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                if (!alive) { if (b.Agent != null) _kindOf.Remove(b.Agent); _beings.RemoveAt(i); continue; }

                // LOD: distant bodies re-coat half as often (still reads as an aura).
                float interval = ElementalMath.AuraIntervalSeconds;
                Vec3 at; try { at = b.Agent.Position; } catch { continue; }
                bool near = true;
                if (haveEye)
                {
                    float dx = at.x - eye.x, dy = at.y - eye.y;
                    near = dx * dx + dy * dy <= ElementalMath.AuraLodMetres * ElementalMath.AuraLodMetres;
                    if (!near) interval *= 2f;
                }

                if (reAggro) ReRouse(b.Agent);

                b.AuraTimer -= dt;
                if (b.AuraTimer > 0f) continue;
                b.AuraTimer = interval;
                EmitAura(b.Agent, b.Kind, at, near);
            }
        }

        private static void ReRouse(Agent agent)
        {
            try
            {
                if (agent.Team == null) return;
                if (Mission.Current != null && agent.Team == Mission.Current.PlayerTeam) return; // never the player's line
                try { agent.SetWatchState(Agent.WatchState.Alarmed); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                Formation form = agent.Formation;
                if (form != null)
                    try { form.SetMovementOrder(MovementOrder.MovementOrderCharge); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void EmitAura(Agent agent, ElementalKind kind, Vec3 at, bool near)
        {
            try
            {
                switch (kind)
                {
                    case ElementalKind.Flame:
                        SpellEffects.SpawnTempFireParticle(at + new Vec3(0f, 0f, 0.9f), 0.5f);
                        break;
                    case ElementalKind.Frost:
                        SpellEffects.SpawnTempSnowParticle(at + new Vec3(0f, 0f, 0.7f), 0.5f);
                        break;
                    case ElementalKind.Tide:
                        SpellEffects.SpawnNatureBurst(at + new Vec3(0f, 0f, 0.6f), NatureElement.Water, 0.5f);
                        break;
                    case ElementalKind.Gale:
                        SpellEffects.SpawnNatureBurst(at + new Vec3(0f, 0f, 0.9f), NatureElement.Storm, 0.5f);
                        break;
                    default: // Stone / Sand
                        SpellEffects.SpawnNatureBurst(at, NatureElement.Earth, 0.5f);
                        break;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            // A body-hugging light only for the bodies close enough to see it —
            // dozens of persistent lights across a field is the kind of cost this
            // mod does not pay.
            if (near)
                try { SpellEffects.SpawnTempLightRgb(at + new Vec3(0f, 0f, 1f), AuraRgb(kind), 4.5f, 0.6f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { SpellEffects.BeginAgentGlow(agent, GlowSchool(kind), 0.7f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static Vec3 AuraRgb(ElementalKind kind)
        {
            switch (kind)
            {
                case ElementalKind.Flame: return new Vec3(1.0f, 0.45f, 0.12f);
                case ElementalKind.Tide:  return new Vec3(0.18f, 0.50f, 1.0f);
                case ElementalKind.Frost: return new Vec3(0.70f, 0.85f, 1.0f);
                case ElementalKind.Gale:  return new Vec3(0.62f, 0.52f, 1.0f);
                case ElementalKind.Sand:  return new Vec3(0.85f, 0.70f, 0.40f);
                default:                  return new Vec3(0.50f, 0.46f, 0.42f); // Stone
            }
        }

        private static ColorSchool GlowSchool(ElementalKind kind)
        {
            switch (kind)
            {
                case ElementalKind.Flame: return ColorSchool.Red;
                case ElementalKind.Tide:  return ColorSchool.Blue;
                case ElementalKind.Frost: return ColorSchool.White;
                case ElementalKind.Gale:  return ColorSchool.Purple;
                default:                  return ColorSchool.Nature; // Stone / Sand
            }
        }

        // ── Magical weakness (read by SpellEffects.DamageAgent) ──────────────────
        public static float IncomingElementMultiplier(Agent target, MagicElement attack)
        {
            if (target == null) return 1f;
            if (!_kindOf.TryGetValue(target, out ElementalKind kind)) return 1f;
            return ElementalMath.ElementDamageMultiplier(kind, attack);
        }

        // ── Physical weakness (called from MagicMissionBehavior.OnAgentHit) ──────
        // OnAgentHit fires AFTER the blow is applied, so we can only correct it
        // afterwards: amplify a weakness with a bonus hit, shrug off a resisted
        // blow by healing the mitigated part back (the Nature-barrier pattern).
        public static void OnWeaponHit(Agent affected, Agent affector, DamageTypes damageType, float inflicted)
        {
            if (affected == null || inflicted <= 0f) return;
            if (!_kindOf.TryGetValue(affected, out ElementalKind kind)) return;

            PhysicalHit hit = MapHit(damageType);
            float mult = ElementalMath.PhysicalDamageMultiplier(kind, hit);
            if (Math.Abs(mult - 1f) < 0.01f) return;

            if (mult > 1f)
            {
                float bonus = inflicted * (mult - 1f);
                try { SpellEffects.DamageAgent(affected, bonus, GlowSchool(kind), affector); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            else
            {
                float healBack = inflicted * (1f - mult);
                try { SpellEffects.HealAgent(affected, healBack); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        private static PhysicalHit MapHit(DamageTypes t)
        {
            switch (t)
            {
                case DamageTypes.Blunt: return PhysicalHit.Blunt;
                case DamageTypes.Pierce: return PhysicalHit.Pierce;
                default: return PhysicalHit.Cut;   // Cut / Invalid
            }
        }
    }
}
