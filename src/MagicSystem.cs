// =============================================================================
// COLOURS OF CALRADIA — MagicSystem.cs
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
    // =========================================================================
    // 1. MODULE ENTRY POINT
    // =========================================================================
    public class MainSubModule : MBSubModuleBase
    {
        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            if (game.GameType is Campaign &&
                gameStarterObject is CampaignGameStarter campaignStarter)
                campaignStarter.AddBehavior(new MagicCampaignBehavior());
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            mission.AddMissionBehavior(new MagicMissionBehavior());
        }

        protected override void OnApplicationTick(float dt)
        {
            if (Campaign.Current == null || Mission.Current != null) return;
            try { MagicInputHandler.Tick(inMission: false); } catch { }
            ActiveEffectManager.MapTick(dt);
        }
    }

    // =========================================================================
    // 1b. MISSION BEHAVIOR
    // =========================================================================
    public class MagicMissionBehavior : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        private bool _orderHookRegistered = false;
        private static readonly Random _orderRng = new Random();
        private static readonly MovementOrder[] _madnessOrders =
        {
            MovementOrder.MovementOrderStop,
            MovementOrder.MovementOrderCharge,
        };

        public override void OnMissionTick(float dt)
        {
            // All downstream ticks make native calls (TeleportToPosition, SetActionChannel,
            // SetContourColor, GameEntity.SetGlobalFrame, etc.). These crash silently when
            // called on agents/entities whose native backing is being torn down during the
            // transition frames between battle-end and OnEndMission. Skip everything except
            // input during those frames.
            var mission = Mission.Current;
            if (mission == null || mission.CurrentState != Mission.State.Continuing) return;

            TryRegisterOrderHook();
            MagicInputHandler.Tick(inMission: true);
            ActiveEffectManager.MissionTick(dt);
            ColourLordAI.MissionTick(dt);
            ColourUnitRegistry.MissionTick(dt);
            SpellEffects.TickGlows(dt);
            SpellEffects.TickAnimClears(dt);
            SpellEffects.TickMoves(dt);
            SpellEffects.TickAreaEffects(dt);
            SpellEffects.TickHaltedAgents(dt);
            SpellEffects.TickRandomUnitMagic(dt);
            SaturationSystem.TickKnockdown(dt);
            SpellEffects.FlushPendingDeaths();
        }

        private void TryRegisterOrderHook()
        {
            if (_orderHookRegistered) return;
            try
            {
                var ctrl = Mission.Current?.PlayerTeam?.PlayerOrderController;
                if (ctrl == null) return;
                ctrl.OnOrderIssued += OnPlayerOrderIssued;
                _orderHookRegistered = true;
            }
            catch { }
        }

        private void OnPlayerOrderIssued(OrderType orderType,
            MBReadOnlyList<Formation> formations, OrderController orderController, object[] delegateParams)
        {
            int chance = ColourKnowledge.GetMadnessOrderChance();
            if (chance <= 0 || _orderRng.Next(100) >= chance) return;

            bool charge = _orderRng.Next(2) == 0;
            MovementOrder replacement = charge
                ? MovementOrder.MovementOrderCharge
                : MovementOrder.MovementOrderStop;
            string name = charge ? "Charge" : "Halt";

            if (!SpellEffects.IsSiegeActive())
                foreach (Formation f in formations)
                    try { f.SetMovementOrder(replacement); } catch { }

            InformationManager.DisplayMessage(new InformationMessage(
                $"Madness: Your command slips — {name} issued instead.",
                Color.FromUint(0xFFCC44FF)));
        }

        protected override void OnEndMission()
        {
            if (_orderHookRegistered)
            {
                try
                {
                    var ctrl = Mission.Current?.PlayerTeam?.PlayerOrderController;
                    if (ctrl != null) ctrl.OnOrderIssued -= OnPlayerOrderIssued;
                }
                catch { }
                _orderHookRegistered = false;
            }
            // Clear all mission state. The scene is still alive here so GameEntity/agent
            // calls are safe. Without this, stale GameEntity references from area effect
            // lights survive into the next battle where they are freed memory → crash.
            try { SpellEffects.ClearAnimTimers();     } catch { }
            try { SpellEffects.ClearPendingDeaths();  } catch { }
            try { SpellEffects.ClearAreaEffects();    } catch { }
            try { SpellEffects.ClearGlows();          } catch { }
            try { SpellEffects.ClearMoves();          } catch { }
            try { ColourLordAI.ClearCooldowns();      } catch { }
            try { ColourUnitRegistry.OnMissionEnded(); } catch { }
            try { SaturationSystem.ClearKnockdowns(); } catch { }
        }

        public override void OnAgentHit(Agent affectedAgent, Agent affectorAgent,
            in MissionWeapon affectorWeapon, in Blow blow, in AttackCollisionData attackCollisionData)
        {
        }

        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent,
            AgentState agentState, KillingBlow blow)
        {
            ColourUnitRegistry.OnAgentRemoved(affectedAgent);

            // Track blight kills by the player so OnMissionEnded can prompt colour learning
            if (affectorAgent == Agent.Main && affectedAgent != null
                && BlightSystem.IsBlight(affectedAgent))
                BlightSystem.RecordPlayerBlightKill(BlightSystem.GetBlightSchool(affectedAgent));
        }
    }
}
