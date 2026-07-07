// =============================================================================
// ASH AND EMBER — Elementals/ElementalBeings.cs
//
// Runtime registry of THE KINDLED — every elemental being alive in the current
// mission, whatever spawned it (the Spirit Unbinding's champion, an enemy mage's
// summon, a wild band the player marched on, or a battle's Kindling). One place
// holds them so ONE piece of code drives their look and their weakness:
//
//   • TickAuras(dt)   — binds each body's continuous element to its skeleton the
//                       first tick it is seen alive (via ElementalVisuals), then
//                       keeps it roused, looses its element on a cooldown, and
//                       tears the shroud down as it falls. The element itself is
//                       carried by the engine, NOT re-stamped here every tick.
//   • IncomingElementMultiplier(target, attack) — magical weakness, read by
//                       SpellEffects.DamageAgent before a spell lands.
//   • OnWeaponHit(...) — physical weakness (stone shatters to blunt, blades pass
//                       through flame), applied after a real weapon blow.
//
// The LOOK — the bone-bound particle systems, the follower light and the contour
// — lives in ElementalVisuals; this file only decides WHEN to raise and drop it.
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
            public bool Shrouded;       // whether its continuous element has been bound to the skeleton yet
            public float AttackTimer;   // seconds until this Kindled next looses its element
        }

        private static readonly Random _rng = new Random();

        private static readonly List<Being> _beings = new List<Being>();
        private static readonly Dictionary<Agent, ElementalKind> _kindOf = new Dictionary<Agent, ElementalKind>();

        // Set when the player's mission involves a wild elemental band, so the
        // OnAgentBuild hook knows to remake the enemy bodies into that kind. Null
        // in every ordinary battle. Cleared at mission end.
        public static ElementalKind? PendingBattleKind = null;
        // How many enemy bodies have already woken this battle — capped so a big
        // band fields a dangerous knot of Kindled, not a whole elemental army.
        private static int _convertedThisBattle = 0;

        // ── Registry ─────────────────────────────────────────────────────────────
        public static void Register(Agent agent, ElementalKind kind)
        {
            if (agent == null) return;
            if (_kindOf.ContainsKey(agent)) { _kindOf[agent] = kind; return; }
            _kindOf[agent] = kind;
            _beings.Add(new Being
            {
                Agent = agent, Kind = kind,
                // Stagger the first blast so a freshly-woken band does not volley as one.
                AttackTimer = (float)(_rng.NextDouble() * ElementalMath.AttackCooldownSeconds),
            });
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
            try { ElementalVisuals.ClearAll(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            _beings.Clear();
            _kindOf.Clear();
            PendingBattleKind = null;
            _convertedThisBattle = 0;
        }

        // ── Sacred-Kindled registration (called from OnAgentBuild) ───────────────
        // Troop ids for the six sacred-site-crafted elemental variants
        // (troops.xml), each a permanent, persistent army troop rather than a
        // mission-only spawn. Since a roster entry carries no per-unit metadata,
        // the KIND must be read off the troop id itself.
        private static readonly Dictionary<string, ElementalKind> _sacredKindledIds =
            new Dictionary<string, ElementalKind>(StringComparer.OrdinalIgnoreCase)
        {
            { "sacred_kindled_stone", ElementalKind.Stone },
            { "sacred_kindled_frost", ElementalKind.Frost },
            { "sacred_kindled_sand",  ElementalKind.Sand  },
            { "sacred_kindled_flame", ElementalKind.Flame },
            { "sacred_kindled_tide",  ElementalKind.Tide  },
            { "sacred_kindled_gale",  ElementalKind.Gale  },
        };

        // A sacred-crafted troop fields under its own army's normal orders — it
        // does not need SetAggressive's charge/formation override, only the
        // aura/weakness/self-cast registration every other Kindled gets.
        public static void RegisterSacredKindled(Agent agent)
        {
            try
            {
                if (agent == null || agent.IsMount) return;
                string id = agent.Character?.StringId;
                if (id == null) return;
                if (_sacredKindledIds.TryGetValue(id, out ElementalKind kind))
                    Register(agent, kind);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
            if (_convertedThisBattle >= ElementalMath.MaxConvertedPerBattle) return;
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
            _convertedThisBattle++;
            Register(agent, PendingBattleKind.Value);
            try { ElementalFactory.SetAggressive(agent, agent.Team); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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

            for (int i = _beings.Count - 1; i >= 0; i--)
            {
                Being b = _beings[i];
                bool alive = false;
                try { alive = b.Agent != null && b.Agent.IsActive() && b.Agent.Health > 0f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                if (!alive)
                {
                    if (b.Agent != null)
                    {
                        _kindOf.Remove(b.Agent);
                        try { ElementalVisuals.Detach(b.Agent); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    }
                    _beings.RemoveAt(i);
                    continue;
                }

                // The being's element is BUILT ONCE — real particle systems bound to
                // its skeleton the first tick we see it alive (visuals guaranteed
                // ready by now), after which the engine carries them for free. All
                // that remains each tick is dragging its one light to the body.
                if (!b.Shrouded)
                {
                    try { ElementalVisuals.Attach(b.Agent, b.Kind); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    b.Shrouded = ElementalVisuals.IsShrouded(b.Agent);
                }
                else
                {
                    try { ElementalVisuals.Follow(b.Agent); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }

                if (reAggro) ReRouse(b.Agent);

                // The Kindled fights with its element: on a cooldown, loose a small
                // cone of its own kind at a foe within reach. This is how a
                // weaponless being of raw magic actually kills — no club, no stones.
                b.AttackTimer -= dt;
                if (b.AttackTimer <= 0f)
                {
                    b.AttackTimer = ElementalMath.AttackCooldownSeconds
                                  + (float)((_rng.NextDouble() - 0.5) * 2.0 * ElementalMath.AttackCooldownJitter);
                    TryLooseElement(b.Agent, b.Kind);
                }
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

        // Loose a small cone of the being's own element, but only when a living
        // enemy stands within reach — no point spraying fire at empty ground. The
        // cone flies in the body's facing direction; a charging Kindled faces its
        // prey, so it lands. Reuses the exact player cast path (weakness wheel,
        // walls, wards) at a low, instinctive power.
        private static void TryLooseElement(Agent agent, ElementalKind kind)
        {
            try
            {
                if (agent == null || !agent.IsActive() || agent.Team == null) return;
                if (Mission.Current == null) return;
                if (Mission.Current.CurrentState != Mission.State.Continuing) return;

                Vec3 pos = agent.Position;
                float r2 = ElementalMath.AttackRangeMetres * ElementalMath.AttackRangeMetres;
                Agent nearest = null; float bestD2 = r2;
                foreach (Agent a in Mission.Current.Agents)
                {
                    if (a == null || !a.IsActive() || a.IsMount || a.Team == null) continue;
                    if (!agent.Team.IsEnemyOf(a.Team)) continue;
                    float dx = a.Position.x - pos.x, dy = a.Position.y - pos.y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 <= bestD2) { bestD2 = d2; nearest = a; }
                }
                if (nearest == null) return;   // no foe within reach — don't loose into empty ground

                // Every damage element now strikes FORWARD (Fire a bursting bolt, Water
                // a wave, Wind a gust, Earth a line of roots), so only loose it when the
                // foe is actually ahead (a charging Kindled usually is) — otherwise the
                // working sails past behind it. Only Spirit is all-around, and the
                // Kindled do not wield it.
                MagicElement el = ElementalMath.ElementOf(kind);
                if (el != MagicElement.Spirit)
                {
                    Vec3 fwd = agent.LookDirection; fwd.z = 0f;
                    Vec3 to  = nearest.Position - pos; to.z = 0f;
                    float fl = fwd.Length, tl = to.Length;
                    if (fl > 0.01f && tl > 0.01f &&
                        Vec3.DotProduct(fwd * (1f / fl), to * (1f / tl)) < 0.2f)
                        return;   // foe is not ahead — hold the working this beat
                }

                float power = ElementalMath.AttackPower;
                ElementSpellEffects.CastAttack(el, agent, power);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // The cheap, single-particle wisp for a given kind at a given point — used
        // by the one-shot spawn burst in ElementalFactory (a body's continuous
        // element is now bound to its skeleton by ElementalVisuals, not stamped
        // here every tick).
        internal static void EmitKindWisp(ElementalKind kind, Vec3 pos, float duration)
        {
            switch (kind)
            {
                case ElementalKind.Flame:
                    SpellEffects.SpawnTempFireWisp(pos, duration);
                    break;
                case ElementalKind.Frost:
                    SpellEffects.SpawnTempSnowWisp(pos, duration);
                    break;
                case ElementalKind.Tide:
                    SpellEffects.SpawnNatureBurst(pos, NatureElement.Water, duration);
                    break;
                case ElementalKind.Gale:
                    SpellEffects.SpawnNatureBurst(pos, NatureElement.Storm, duration);
                    break;
                default: // Stone / Sand
                    SpellEffects.SpawnNatureBurst(pos, NatureElement.Earth, duration);
                    break;
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
