// =============================================================================
// LIFE & DEATH MAGIC — MagicSystem.cs
// Module entry point. Wires up the campaign behaviour and mission behaviour.
// =============================================================================

using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    // =========================================================================
    // MODULE ENTRY POINT
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
    // MISSION BEHAVIOR
    // =========================================================================
    public class MagicMissionBehavior : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;
        private static readonly Random _rng = new Random();

        public override void OnMissionTick(float dt)
        {
            var mission = Mission.Current;
            if (mission == null || mission.CurrentState != Mission.State.Continuing) return;

            MagicInputHandler.Tick(inMission: true);
            ActiveEffectManager.MissionTick(dt);
            ColourLordAI.MissionTick(dt);
            SpellEffects.TickGlows(dt);
            SpellEffects.TickColourCooldown(dt);
            SpellEffects.TickAnimClears(dt);
            SpellEffects.TickMoves(dt);
            SpellEffects.TickAreaEffects(dt);
            SpellEffects.TickWave(dt);
            SpellEffects.TickWard(dt);
            SpellEffects.TickMagicMemory(dt);
            SpellEffects.TickHaltedAgents(dt);
            SpellEffects.FlushPendingDeaths();
            BanditMageAI.MissionTick(dt);
        }

        protected override void OnEndMission()
        {
            try { SpellEffects.ClearAnimTimers();    } catch { }
            try { SpellEffects.ClearPendingDeaths(); } catch { }
            try { SpellEffects.ClearAreaEffects();   } catch { }
            try { SpellEffects.ClearWave();          } catch { }
            try { SpellEffects.ClearWard();          } catch { }
            try { SpellEffects.ClearMagicMemory();   } catch { }
            try { SpellEffects.ClearGlows();         } catch { }
            try { SpellEffects.ClearColourCooldown();} catch { }
            try { SpellEffects.ClearMoves();         } catch { }
            try { ColourLordAI.ClearCooldowns();     } catch { }
            try { BanditMageAI.OnMissionEnd();       } catch { }
            try { AgingSystem.ClearKnockdowns();     } catch { }
        }

        public override void OnAgentHit(Agent affectedAgent, Agent affectorAgent,
            in MissionWeapon affectorWeapon, in Blow blow, in AttackCollisionData attackCollisionData)
        {
        }

        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent,
            AgentState agentState, KillingBlow blow)
        {
            try
            {
                if (affectorAgent != Agent.Main) return;
                if (affectedAgent == null || affectedAgent.IsMount) return;
                if (agentState != AgentState.Killed) return;
                if (!MageKnowledge.IsMage || !TalentSystem.Has(TalentId.Ember)) return;
                if (_rng.NextDouble() < 0.05)
                    AgingSystem.RejuvenateHero(Hero.MainHero, 1);
            }
            catch { }
        }
    }
}
