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
            try { SpellEffects.ClearScorch();        } catch { }
            try { SpellEffects.ClearAshmark();       } catch { }
            try { SpellEffects.ClearInnerFireHeat(); } catch { }
            try { SpellEffects.ClearMagicMemory();           } catch { }
            try { SpellEffects.ClearDarkGiftsBattleState();  } catch { }
            try { MagicInputHandler.ResetInputState();        } catch { }
            try { MiracleEffects.ClearBattleState();     } catch { }
            try { MiracleBattleAI.Reset();               } catch { }
            try { MiracleInputHandler.ResetInputState(); } catch { }
            try { NatureEffects.ClearBattleState();      } catch { }
            try { NatureCharge.ClearForMission();        } catch { }
            try { NatureChargeBar.Reset();               } catch { }
            try { NatureSeerAI.ClearCooldowns();         } catch { }
            try { NatureInputHandler.ResetInputState();  } catch { }
            try { ColourLordAI.ClearCooldowns();        } catch { }
            try { ColourLordAI.FlushBattleCasts();      } catch { }
            try { BanditMageAI.OnMissionEnd();           } catch { }
            try { AshenSceneTone.Reset();                } catch { }
            try { BattleWhispers.Reset();                } catch { }
            try { AshenVisuals.Reset();                  } catch { }

            if (game.GameType is Campaign &&
                gameStarterObject is CampaignGameStarter campaignStarter)
            {
                campaignStarter.AddModel(new AshenDiplomacyModel());
                campaignStarter.AddBehavior(new MagicCampaignBehavior());
                campaignStarter.AddBehavior(new SchemeCampaignBehavior());
                campaignStarter.AddBehavior(new SanctuaryCampaignBehavior());
                campaignStarter.AddBehavior(new AshenAltarsCampaignBehavior());
                campaignStarter.AddBehavior(new SeaCampaignBehavior());
                campaignStarter.AddBehavior(new CrystallinesCampaignBehavior());
                campaignStarter.AddBehavior(new ExchangeCampaignBehavior());
                campaignStarter.AddBehavior(new TavernCampaignBehavior());
                campaignStarter.AddBehavior(new AshenRuinCampaignBehavior());
                campaignStarter.AddBehavior(new MiracleCampaignBehavior());
                campaignStarter.AddBehavior(new NatureCampaignBehavior());
                campaignStarter.AddBehavior(new ClanOrdersCampaignBehavior());
                try { AshenDialogue.Register(campaignStarter);    } catch { }
                try { ArenicosDialogue.Register(campaignStarter); } catch { }
                try { SchemeSystem.Initialize();              } catch { }
                try { ExchangeCampaignBehavior.ResetState();  } catch { }
                try { SeaCampaignBehavior.ResetForNewGame();  } catch { }
                try { ClanOrdersCampaignBehavior.ResetForNewGame(); } catch { }
            }
        }

        // Game.Initialize() reloads all game texts from XML, which runs AFTER
        // OnGameStart and would wipe an earlier override. This hook fires after that
        // reload and before the character-creation screen is built, so it is the
        // correct point to rewrite the culture-card text (Vlandia IS The Holy Temple).
        public override void OnGameInitializationFinished(Game game)
        {
            base.OnGameInitializationFinished(game);
            try { AshenCitySystem.ApplyTempleCultureTexts(); } catch { }
        }

        // Re-applies the Templar culture text while still in the menu / intro-video /
        // character-creation flow (before the campaign map exists). Stops once the
        // campaign is actually running so it costs nothing during play.
        private static void EnsureTempleCultureTextPreGame()
        {
            object st = null;
            try { st = GameStateManager.Current?.ActiveState; } catch { }
            bool preGame = Campaign.Current == null
                || st is TaleWorlds.CampaignSystem.CharacterCreationContent.CharacterCreationState
                || st is VideoPlaybackState;
            if (preGame)
            {
                try { AshenCitySystem.ApplyTempleCultureTexts(); } catch { }
                // The character-creation culture cards cache their name when built, so
                // the text override above never reaches them — rename the card directly.
                try { TempleCultureCardFixer.TickTryFix(); } catch { }
            }
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            mission.AddMissionBehavior(new MagicMissionBehavior());
        }

        protected override void OnApplicationTick(float dt)
        {
            // Vlandia IS The Holy Temple. Game.Initialize() reloads every game text
            // (restoring "Vlandian") AFTER our game-start hooks, and the culture card
            // is built a few frames later — after the intro-video state — by
            // LaunchSandboxCharacterCreation. The text override (a first-match replace)
            // is cheap and idempotent, so re-apply it every frame through the
            // pre-campaign flow, and do it BEFORE SkipIntroVideos hands off to the
            // character-creation screen, so the card is guaranteed to read "Templars".
            try { EnsureTempleCultureTextPreGame(); } catch { }

            // Runs before the campaign gate below so it also fires at the main menu and
            // during the new-game flow, where Campaign.Current is still null.
            try { SkipIntroVideos(); } catch { }
            try { AshEmberSplash.Tick(dt); } catch { }
            try { AshEmberLoreIntro.Tick(dt); } catch { }
            try { AshEmberLoadingScreen.Tick(dt); } catch { }

            try
            {
                if (Campaign.Current == null || Mission.Current != null) return;
                try { MagicInputHandler.Tick(inMission: false); } catch { }
                try { MiracleInputHandler.Tick(inMission: false); } catch { }
                try { NatureInputHandler.Tick(inMission: false);  } catch { }
                try { ActiveEffectManager.MapTick(dt); } catch { }

                // Ctrl+Shift+F10 — toggle scheme debug mode
                try
                {
                    if (TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.LeftControl)
                     && TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.LeftShift)
                     && TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.F10))
                    {
                        SchemeSystem.DebugFree = !SchemeSystem.DebugFree;
                        MBInformationManager.AddQuickInformation(new TaleWorlds.Localization.TextObject(
                            SchemeSystem.DebugFree
                                ? "[DEBUG] Schemes: costs disabled, success forced."
                                : "[DEBUG] Schemes: normal mode restored."));
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

                // Ctrl+Shift+F12 — debug grant: 100 fp, all dark gifts, max grace,
                // all nature talents, one of each elixir. Nature must be granted before
                // dark gifts because GrantGift clears grace as a side effect.
                try
                {
                    if (TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.LeftControl)
                     && TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.LeftShift)
                     && TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.F12))
                    {
                        DebugGrantAll();
                    }
                }
                catch { }
            }
            catch { }
        }

        // Skips TaleWorlds' intro cinematics — the logo reels at launch and, more
        // importantly, the campaign intro video that plays before character creation.
        // We simply finish any active VideoPlaybackState the moment it appears, which
        // is exactly what the engine's own "skip" button does, so the flow advances
        // cleanly to the next state. Guarded so a future engine change degrades to a
        // no-op rather than a crash.
        private static void SkipIntroVideos()
        {
            try
            {
                var active = GameStateManager.Current?.ActiveState;
                if (active is VideoPlaybackState video)
                    video.OnVideoFinished();
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

        private static void DebugGrantAll()
        {
            var hero = Hero.MainHero;
            if (hero == null) return;

            // 1. Nature magic — must come before dark gifts because GrantGift clears grace.
            try
            {
                TalentSystem.GrantFree(TalentId.NatureLivingRoot, hero);
                TalentSystem.GrantFree(TalentId.NatureStillDraw,  hero);
                TalentSystem.GrantFree(TalentId.NatureOpenGrip,   hero);
                TalentSystem.GrantFree(TalentId.Wildsworn,        hero);
                TalentSystem.GrantFree(TalentId.NatureDeepEarth,  hero);
                TalentSystem.GrantFree(TalentId.NatureDawnCall,   hero);
            }
            catch { }

            // 2. All Dark Gifts (DarkSpirit stacks to 3, grant it three times).
            try
            {
                foreach (DarkGiftId gift in Enum.GetValues(typeof(DarkGiftId)))
                {
                    if (gift == DarkGiftId.DarkSpirit)
                    {
                        DarkGiftSystem.GrantGift(DarkGiftId.DarkSpirit);
                        DarkGiftSystem.GrantGift(DarkGiftId.DarkSpirit);
                        DarkGiftSystem.GrantGift(DarkGiftId.DarkSpirit);
                    }
                    else
                    {
                        DarkGiftSystem.GrantGift(gift);
                    }
                }
            }
            catch { }

            // 3. Max grace — set directly because dark gifts would otherwise block AddGrace.
            try { MiracleInventory._grace = MiracleMath.GraceColdCap; } catch { }

            // 4. 100 focus points.
            try { hero.HeroDeveloper.UnspentFocusPoints += 100; } catch { }

            MBInformationManager.AddQuickInformation(new TaleWorlds.Localization.TextObject(
                "[DEBUG] Granted: 100 focus points, all Dark Gifts, max Grace, all Nature talents."));
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
            CrystalEffects.MissionTick(dt);
            CrystalBattleAI.MissionTick(dt);
            MiracleInputHandler.Tick(inMission: true);
            NatureInputHandler.Tick(inMission: true, dt);
            NatureChargeBar.Tick(dt);
            MiracleEffects.MissionTick(dt);
            MiracleBattleAI.MissionTick(dt);
            NatureEffects.MissionTick(dt);
            NatureSeerAI.MissionTick(dt);
            NatureCharge.MissionTick(dt);
            ActiveEffectManager.MissionTick(dt);
            ColourLordAI.MissionTick(dt);
            SpellEffects.TickGlows(dt);
            SpellEffects.TickFocusVisuals(dt);
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
            SpellEffects.TickScorch(dt);
            SpellEffects.TickAshmark(dt);
            SpellEffects.TickInnerFireHeat(dt);
            SpellEffects.TickMagicMemory(dt);
            SpellEffects.TickHaltedAgents(dt);
            SpellEffects.TickDarkGifts(dt);
            SpellEffects.FlushPendingDeaths();
            BanditMageAI.MissionTick(dt);
            BattleEvents.MissionTick(dt);
            AshenSceneTone.MissionTick(dt);
            BattleWhispers.MissionTick(dt);
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
            try { SpellEffects.ClearScorch();        } catch { }
            try { SpellEffects.ClearAshmark();       } catch { }
            try { SpellEffects.ClearInnerFireHeat(); } catch { }
            try { SpellEffects.ClearMagicMemory();         } catch { }
            try { SpellEffects.ClearDarkGiftsBattleState(); } catch { }
            try { SpellEffects.ClearFocusVisuals();  } catch { }
            try { SpellEffects.ClearGlows();         } catch { }
            try { SpellEffects.ClearColourCooldown();} catch { }
            try { SpellEffects.ClearMoves();         } catch { }
            try { ColourLordAI.ClearCooldowns();            } catch { }
            try { BanditMageAI.OnMissionEnd();             } catch { }
            try { AgingSystem.ClearKnockdowns();           } catch { }
            try { ActiveEffectManager.ClearMissionEffects(); } catch { }
            try { MagicInputHandler.ResetInputState();       } catch { }
            try { CrystalEffects.ClearBattleState();           } catch { }
            try { CrystalBattleAI.Reset();                    } catch { }
            try { NatureEffects.ClearBattleState();           } catch { }
            try { NatureCharge.ClearForMission();             } catch { }
            try { NatureChargeBar.Reset();                    } catch { }
            try { NatureSeerAI.ClearCooldowns();              } catch { }
            try { NatureInputHandler.ResetInputState();       } catch { }
            try { BattleEvents.OnMissionEnd();               } catch { }
            try { AshenSceneTone.Reset();                    } catch { }
            try { BattleWhispers.Reset();                    } catch { }
        }

        public override void OnAgentBuild(Agent agent, Banner banner)
        {
            // Witchy-ashen look (grey skin, cold-blue eyes, ragged armour
            // elements) for Ashen Spawn units, Ashen kingdom soldiers and
            // Ashen heroes.
            try { AshenVisuals.TryApply(agent); } catch { }
            // All non-hero soldiers fighting for the Ashen (kingdom or Ashen player)
            // are called "Ashen Warrior" in battle — they have abandoned their old names.
            try { AshenVisuals.TryRenameToAshenWarrior(agent); } catch { }
            // Dark Gifts: apply persistent contour to player if gifts are active.
            try { SpellEffects.ApplyDarkGiftAgentBuild(agent); } catch { }
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
            // Dark Gifts: passive on-hit effects for attacker and defender.
            try { SpellEffects.ApplyDarkGiftAttackEffects(affectedAgent, affectorAgent, blow.InflictedDamage, isMeleeHit); } catch { }
            try { SpellEffects.ApplyDarkGiftDefenseEffects(affectedAgent, affectorAgent, blow.InflictedDamage, isMeleeHit); } catch { }
            try { MiracleEffects.OnAgentHit(affectedAgent, affectorAgent, blow.InflictedDamage); } catch { }
            try { CrystalEffects.OnCrystalHit(affectedAgent, affectorAgent, affectorWeapon, blow.InflictedDamage); } catch { }
            // Nature resistance (reserved for future barrier talents): OnAgentHit fires after
            // damage is applied; heal back the mitigated portion against real weapon hits.
            try
            {
                if (affectedAgent != null && affectedAgent.IsActive()
                    && NatureEffects.HasResist(affectedAgent) && blow.InflictedDamage > 0)
                {
                    float kept      = NatureEffects.ApplyResistance(affectedAgent, blow.InflictedDamage);
                    float mitigated = blow.InflictedDamage - kept;
                    if (mitigated > 0f)
                        affectedAgent.Health = Math.Min(affectedAgent.HealthLimit, affectedAgent.Health + mitigated);
                }
            }
            catch { }
        }

        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent,
            AgentState agentState, KillingBlow blow)
        {
            try
            {
                if (affectedAgent == null || affectedAgent.IsMount) return;
                if (agentState != AgentState.Killed) return;
                // Blood Pact gift: heal killer on kill (player and gifted NPC lords)
                try { SpellEffects.ApplyDarkGiftKillEffects(affectedAgent, affectorAgent); } catch { }
                // Ember passive: rejuvenate on kill
                if (affectorAgent == Agent.Main && MageKnowledge.IsMage && TalentSystem.Has(TalentId.Ember))
                    if (_rng.NextDouble() < 0.10)
                        AgingSystem.RejuvenateHero(Hero.MainHero, 1);
            }
            catch { }
        }
    }
}
