// =============================================================================
// LIFE & DEATH MAGIC — MagicSystem.cs
// Module entry point. Wires up the campaign behaviour and mission behaviour.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
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
            // Reset all in-mission static state that may be stale from a previous
            // game session running in the same process (save load without restart).
            // ClearAreaEffects / ClearSelfEffects are internally try/catch'd.
            try { SpellEffects.ClearAreaEffects();   } catch { }
            try { SpellEffects.ClearSelfEffects();   } catch { }
            try { SpellEffects.ClearGlows();         } catch { }
            try { SpellEffects.ClearMoves();         } catch { }
            try { SpellEffects.ClearPendingDeaths(); } catch { }
            try { SpellEffects.ClearAnimTimers();    } catch { }
            try { SpellEffects.ClearCastLoops();     } catch { }
            try { SpellEffects.ClearWard();          } catch { }
            try { SpellEffects.ClearStoneskin();     } catch { }
            try { SpellEffects.ClearSunder();        } catch { }
            try { SpellEffects.ClearChar();          } catch { }
            try { SpellEffects.ClearReflect();       } catch { }
            try { SpellEffects.ClearAttackWeaken();  } catch { }
            try { SpellEffects.ClearMagicMemory();   } catch { }
            try { MagicInputHandler.ResetInputState();  } catch { }
            try { ColourLordAI.ClearCooldowns();        } catch { }
            try { ColourLordAI.FlushBattleCasts();      } catch { }
            try { BanditMageAI.OnMissionEnd();           } catch { }
            try { AshenSceneTone.Reset();                } catch { }
            try { AshenVisuals.Reset();                  } catch { }

            if (game.GameType is Campaign &&
                gameStarterObject is CampaignGameStarter campaignStarter)
            {
                campaignStarter.AddModel(new AshenDiplomacyModel());
                campaignStarter.AddBehavior(new MagicCampaignBehavior());
                campaignStarter.AddBehavior(new SchemeCampaignBehavior());
                campaignStarter.AddBehavior(new SanctuaryCampaignBehavior());
                campaignStarter.AddBehavior(new AshenAltarsCampaignBehavior());
                try { AshenDialogue.Register(campaignStarter);    } catch { }
                try { ArenicosDialogue.Register(campaignStarter); } catch { }
                try { SchemeSystem.Initialize(); } catch { }
            }
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            mission.AddMissionBehavior(new MagicMissionBehavior());
        }

        protected override void OnApplicationTick(float dt)
        {
            try
            {
                if (Campaign.Current == null || Mission.Current != null) return;
                try { MagicInputHandler.Tick(inMission: false); } catch { }
                try { ActiveEffectManager.MapTick(dt); } catch { }

                // Ctrl+Shift+F10 — toggle scheme debug mode; also force-fires The Temple event
                try
                {
                    if (TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.LeftControl)
                     && TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.LeftShift)
                     && TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.F10))
                    {
                        SchemeSystem.DebugFree = !SchemeSystem.DebugFree;
                        string schemeMsg = SchemeSystem.DebugFree
                            ? "[DEBUG] Schemes: costs disabled, success forced."
                            : "[DEBUG] Schemes: normal mode restored.";

                        // Also queue The Temple event if it hasn't fired yet
                        string templeMsg = "";
                        try
                        {
                            if (SchemeSystem.DebugFree)
                            {
                                CampaignMapEvents.DebugForceTemple();
                                templeMsg = " Temple event queued for next weekly tick.";
                            }
                        }
                        catch { }

                        MBInformationManager.AddQuickInformation(new TaleWorlds.Localization.TextObject(
                            schemeMsg + templeMsg));
                    }
                }
                catch { }

                // Ctrl+Shift+F11 — debug combat trigger: warp nearest hostile to player (or spawn one)
                try
                {
                    if (TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.LeftControl)
                     && TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.LeftShift)
                     && TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.F11))
                    {
                        DebugTriggerCombat();
                    }
                }
                catch { }
            }
            catch { }
        }

        private static void DebugTriggerCombat()
        {
            try
            {
                var main = MobileParty.MainParty;
                if (main == null) return;

                // Look for the nearest party we're at war with
                MobileParty enemy = null;
                try
                {
                    var mainFaction = main.MapFaction;
                    if (mainFaction != null)
                    {
                        enemy = MobileParty.All
                            .Where(p => p != main && p.IsActive && !p.IsGarrison
                                && p.MapFaction != null
                                && FactionManager.IsAtWarAgainstFaction(p.MapFaction, mainFaction))
                            .OrderBy(p => (p.GetPosition2D - main.GetPosition2D).LengthSquared)
                            .FirstOrDefault();
                    }
                }
                catch { }

                if (enemy != null)
                {
                    MBInformationManager.AddQuickInformation(new TaleWorlds.Localization.TextObject(
                        "[DEBUG] Enemy found nearby — engage to enter battle."));
                }
                else
                {
                    // No existing hostile — spawn a fresh Ashen ambush party
                    try
                    {
                        CampaignMapEvents.SpawnAshenAmbushNear(
                            main.GetPosition2D + new Vec2(0.1f, 0f), 30, 0f);
                    }
                    catch { }
                    MBInformationManager.AddQuickInformation(new TaleWorlds.Localization.TextObject(
                        "[DEBUG] No hostile found — Ashen ambush spawned nearby."));
                }
            }
            catch { }
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
            SpellEffects.TickCastLoops(dt);
            SpellEffects.TickPendingNpcCasts(dt);
            SpellEffects.TickMoves(dt);
            SpellEffects.TickAreaEffects(dt);
            SpellEffects.TickMissile(dt);
            SpellEffects.TickWard(dt);
            SpellEffects.TickStoneskin(dt);
            SpellEffects.TickSunder(dt);
            SpellEffects.TickChar(dt);
            SpellEffects.TickReflect(dt);
            SpellEffects.TickAttackWeaken(dt);
            SpellEffects.TickMagicMemory(dt);
            SpellEffects.TickHaltedAgents(dt);
            SpellEffects.FlushPendingDeaths();
            BanditMageAI.MissionTick(dt);
            BattleEvents.MissionTick(dt);
            AshenSceneTone.MissionTick(dt);
        }

        protected override void OnEndMission()
        {
            try { SpellEffects.ClearAnimTimers();    } catch { }
            try { SpellEffects.ClearCastLoops();     } catch { }
            try { SpellEffects.ClearPendingDeaths(); } catch { }
            try { SpellEffects.ClearAreaEffects();   } catch { }
            try { SpellEffects.ClearMissile();       } catch { }
            try { SpellEffects.ClearWard();          } catch { }
            try { SpellEffects.ClearStoneskin();     } catch { }
            try { SpellEffects.ClearSunder();        } catch { }
            try { SpellEffects.ClearChar();          } catch { }
            try { SpellEffects.ClearReflect();       } catch { }
            try { SpellEffects.ClearAttackWeaken();  } catch { }
            try { SpellEffects.ClearMagicMemory();   } catch { }
            try { SpellEffects.ClearGlows();         } catch { }
            try { SpellEffects.ClearColourCooldown();} catch { }
            try { SpellEffects.ClearMoves();         } catch { }
            try { ColourLordAI.ClearCooldowns();            } catch { }
            try { BanditMageAI.OnMissionEnd();             } catch { }
            try { AgingSystem.ClearKnockdowns();           } catch { }
            try { ActiveEffectManager.ClearMissionEffects(); } catch { }
            try { MagicInputHandler.ResetInputState();       } catch { }
            try { BattleEvents.OnMissionEnd();               } catch { }
            try { AshenSceneTone.Reset();                    } catch { }
        }

        public override void OnAgentBuild(Agent agent, Banner banner)
        {
            // Witchy-ashen look (grey skin, cold-blue eyes, ragged armour
            // elements) for Ashen Spawn units, Ashen kingdom soldiers and
            // Ashen heroes.
            try { AshenVisuals.TryApply(agent); } catch { }
        }

        public override void OnAgentHit(Agent affectedAgent, Agent affectorAgent,
            in MissionWeapon affectorWeapon, in Blow blow, in AttackCollisionData attackCollisionData)
        {
            // Reflect enchantment: melee hits only — ranged weapons are excluded.
            bool isMeleeHit = true;
            try { isMeleeHit = affectorWeapon.IsEmpty || !(affectorWeapon.CurrentUsageItem?.IsRangedWeapon ?? false); } catch { }
            if (isMeleeHit)
                try { SpellEffects.TryApplyReflect(affectedAgent, affectorAgent, blow.InflictedDamage); } catch { }
            // Sunder enchantment: applies to all hits (attacker is globally weakened).
            try { SpellEffects.TryApplyAttackWeakening(affectedAgent, affectorAgent, blow.InflictedDamage); } catch { }
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
                if (_rng.NextDouble() < 0.10)
                    AgingSystem.RejuvenateHero(Hero.MainHero, 1);
            }
            catch { }
        }
    }
}
