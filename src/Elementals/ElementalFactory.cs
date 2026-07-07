// =============================================================================
// ASH AND EMBER — Elementals/ElementalFactory.cs
//
// Builds one Kindled — an elemental being — as a real fighting Agent, and hands
// it to ElementalBeings for its aura and its weakness. ONE builder feeds every
// spawner: the Spirit Unbinding's champion, a battle's Kindling, and (later) any
// mage's summon, so a Kindled looks and fights the same however it was called.
//
// A Kindled carries no steel and wears no armour — it is the bare "elemental_being"
// troop (troops.xml), made several men tough, dropped onto a team, and told to
// CHARGE. It does not rely on a weapon to fight: ElementalBeings looses a small
// cone of its OWN element on a cooldown (the aura + glow sell it between blasts),
// so a being of pooled magic attacks with pooled magic, not a looter's club.
// =============================================================================

using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static class ElementalFactory
    {
        private static readonly Random _rng = new Random();

        // Spawns a Kindled of `kind` on `team` at `pos`, registers it for the
        // aura/weakness, and (if charge) sets it marching. Returns the agent, or
        // null on any failure (never throws — mod-conflict safe).
        public static Agent SpawnElemental(ElementalKind kind, Team team, Vec3 pos, bool charge)
        {
            try
            {
                if (Mission.Current == null || team == null) return null;

                // The bare, weaponless, renamed "Elemental" body (troops.xml). Falls
                // back to a looter only if that troop failed to load, so a Kindled is
                // never left as a nameless crash.
                CharacterObject troop =
                    MBObjectManager.Instance.GetObject<CharacterObject>("elemental_being")
                 ?? MBObjectManager.Instance.GetObject<CharacterObject>("mountain_bandit")
                 ?? MBObjectManager.Instance.GetObject<CharacterObject>("looter")
                 ?? MBObjectManager.Instance.GetObject<CharacterObject>("sea_raider");
                if (troop == null) return null;

                // Snap to the ground surface (a body spawned in the air slides).
                try
                {
                    float gz = pos.z;
                    Mission.Current.Scene.GetHeightAtPoint(pos.AsVec2,
                        BodyFlags.CommonCollisionExcludeFlagsForAgent, ref gz);
                    pos.z = gz;
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                int seed = _rng.Next();
                Equipment equipment = troop.FirstBattleEquipment ?? troop.Equipment;
                BodyProperties body = troop.GetBodyProperties(equipment, seed);

                uint cloth = ClothFor(kind);
                var origin    = new BasicBattleAgentOrigin(troop);
                var agentData = new AgentBuildData(origin)
                    .Team(team)
                    .Controller(AgentControllerType.AI)
                    .Equipment(equipment)
                    .BodyProperties(body)
                    .Age((int)body.Age)
                    .ClothingColor1(cloth)
                    .ClothingColor2(cloth)
                    .InitialPosition(in pos);
                Vec2 dir = Vec2.Forward;
                agentData = agentData.InitialDirection(in dir);

                Agent agent = Mission.Current.SpawnAgent(agentData, false);
                if (agent == null) return null;

                try
                {
                    agent.HealthLimit = ElementalMath.Health(kind);
                    agent.Health      = ElementalMath.Health(kind);
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                ElementalBeings.Register(agent, kind);
                EmitSpawnBurst(kind, pos);
                if (charge) SetAggressive(agent, team);
                return agent;
            }
            catch { return null; }
        }

        // A being of pooled magic hunts — never stands and waits. A lone AI body
        // already seeks the nearest foe (the Spirit champion has always fought
        // this way), so allies just get roused. An ENEMY-side Kindled is also
        // dropped into its side's infantry and told to charge, so it advances
        // with the line — safe there, since it is not the player's to command.
        internal static void SetAggressive(Agent agent, Team team)
        {
            try { agent.SetWatchState(Agent.WatchState.Alarmed); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            bool enemySide = false;
            try { enemySide = Mission.Current.PlayerTeam == null || team != Mission.Current.PlayerTeam; }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            if (!enemySide) return;
            try
            {
                Formation form = team.GetFormation(FormationClass.Infantry);
                if (form != null)
                {
                    try { agent.Formation = form; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    try { form.SetMovementOrder(MovementOrder.MovementOrderCharge); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Per-kind dressing ────────────────────────────────────────────────────
        private static uint ClothFor(ElementalKind kind)
        {
            switch (kind)
            {
                case ElementalKind.Frost: return 0xFFBFD9E8; // pale ice
                case ElementalKind.Sand:  return 0xFFC9A96A; // dune ochre
                case ElementalKind.Flame: return 0xFFE05A18; // ember orange
                case ElementalKind.Tide:  return 0xFF2E6FB0; // deep water blue
                case ElementalKind.Gale:  return 0xFF8B7FD0; // storm violet
                default:                  return 0xFF6E6A64; // old grey stone
            }
        }

        private static void EmitSpawnBurst(ElementalKind kind, Vec3 pos)
        {
            try
            {
                switch (kind)
                {
                    case ElementalKind.Flame:
                        SpellEffects.SpawnTempFireParticle(pos + new Vec3(0f, 0f, 0.6f), 1.4f);
                        break;
                    case ElementalKind.Frost:
                        SpellEffects.SpawnTempSnowParticle(pos + new Vec3(0f, 0f, 0.5f), 1.4f);
                        break;
                    case ElementalKind.Tide:
                        SpellEffects.SpawnNatureBurst(pos + new Vec3(0f, 0f, 0.4f), NatureElement.Water, 1.6f);
                        break;
                    case ElementalKind.Gale:
                        SpellEffects.SpawnNatureBurst(pos + new Vec3(0f, 0f, 0.8f), NatureElement.Storm, 1.6f);
                        break;
                    default: // Stone / Sand
                        SpellEffects.SpawnNatureBurst(pos, NatureElement.Earth, 1.6f);
                        break;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            // A wisp at head height right from the first breath — faceless from the
            // moment it wakes, not just once the following aura kicks in.
            try
            {
                Vec3 head = pos + new Vec3(0f, 0f, ElementalMath.AuraHeadHeightMetres);
                switch (kind)
                {
                    case ElementalKind.Flame:
                        SpellEffects.SpawnTempFireWisp(head, 1.0f);
                        break;
                    case ElementalKind.Frost:
                        SpellEffects.SpawnTempSnowWisp(head, 1.0f);
                        break;
                    case ElementalKind.Tide:
                        SpellEffects.SpawnNatureBurst(head, NatureElement.Water, 1.0f);
                        break;
                    case ElementalKind.Gale:
                        SpellEffects.SpawnNatureBurst(head, NatureElement.Storm, 1.0f);
                        break;
                    default: // Stone / Sand
                        SpellEffects.SpawnNatureBurst(head, NatureElement.Earth, 1.0f);
                        break;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
