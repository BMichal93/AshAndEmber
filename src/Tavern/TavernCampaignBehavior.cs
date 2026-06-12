// =============================================================================
// ASH AND EMBER — Tavern/TavernCampaignBehavior.cs
//
// "Drinking with Locals" — a push-your-luck tavern game.
//
// UI flow:
//   Tavernkeeper dialogue → "ldm_tavern_menu" (order screen)
//     → choose drink tier (20 / 50 / 100 gold per round)
//       → Athletics test + outcome → "ldm_tavern_result" (what happened)
//         → "Another round" (back to order) or "Call it a night" (exit)
//         → [if passed out] → "ldm_tavern_sober_up" wait menu (4-8 hours)
//
// State is entirely transient — no save/load needed.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public class TavernCampaignBehavior : CampaignBehaviorBase
    {
        // ── Transient session state ───────────────────────────────────────────
        private static int    _roundsDrunk       = 0;
        private static int    _totalSpent        = 0;
        private static int    _lastDrinkCost     = 50; // remembered between rounds
        private static float  _soberHoursTotal   = 0f;
        private static float  _soberHoursElapsed = 0f;
        private static bool   _soberDone         = false;

        private static readonly Random _rng = new Random();

        private static readonly Color GoodColor = new Color(0.56f, 0.93f, 0.56f);
        private static readonly Color BadColor  = new Color(0.93f, 0.40f, 0.40f);
        private static readonly Color DimColor  = new Color(0.78f, 0.78f, 0.78f);

        // ── CampaignBehaviorBase ──────────────────────────────────────────────
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        public override void SyncData(IDataStore store) { }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            ResetSessionState();
            try { RegisterDialogue(starter);  } catch { }
            try { RegisterMenus(starter);     } catch { }
        }

        private static void ResetSessionState()
        {
            _roundsDrunk       = 0;
            _totalSpent        = 0;
            _soberHoursTotal   = 0f;
            _soberHoursElapsed = 0f;
            _soberDone         = false;
        }

        // ── Tavernkeeper dialogue ─────────────────────────────────────────────
        private static void RegisterDialogue(CampaignGameStarter starter)
        {
            const int P = 94;
            try
            {
                starter.AddPlayerLine(
                    "ldm_tavern_open", "tavernkeeper_talk", "ldm_tavern_response",
                    "I'll buy a round and see who's about.",
                    CondTavernAvailable, null, P);
            }
            catch { }
            try
            {
                starter.AddDialogLine(
                    "ldm_tavern_npc", "ldm_tavern_response", "close_window",
                    "Coin on the bar and a seat by the fire. You know how this ends.",
                    null, OpenTavernMenuDeferred, P);
            }
            catch { }
        }

        private static bool CondTavernAvailable()
        {
            try
            {
                var npc = CharacterObject.OneToOneConversationCharacter;
                if (npc?.Occupation != Occupation.Tavernkeeper) return false;
                if (Hero.MainHero?.CurrentSettlement?.IsTown != true) return false;
                return true;
            }
            catch { return false; }
        }

        private static void OpenTavernMenuDeferred()
        {
            try
            {
                MageKnowledge._deferredInquiry = () =>
                {
                    ResetSessionState();
                    try { GameMenu.SwitchToMenu("ldm_tavern_menu"); } catch { }
                };
            }
            catch { }
        }

        // ── Game menus ────────────────────────────────────────────────────────
        private static void RegisterMenus(CampaignGameStarter starter)
        {
            RegisterOrderMenu(starter);
            RegisterResultMenu(starter);
            RegisterSoberUpMenu(starter);
        }

        // ── Order screen ──────────────────────────────────────────────────────
        private static void RegisterOrderMenu(CampaignGameStarter starter)
        {
            try
            {
                starter.AddGameMenu("ldm_tavern_menu", "{TAVERN_MENU_HEADER}", args =>
                {
                    try
                    {
                        string state = _roundsDrunk == 0
                            ? "The common room breathes smoke and noise. A fire holds the cold at bay."
                            : $"The room has taken on a pleasant warmth. Round {_roundsDrunk}. Tab: {_totalSpent} denars.";
                        MBTextManager.SetTextVariable("TAVERN_MENU_HEADER", state);
                    }
                    catch { }
                });
            }
            catch { }

            // Cheap swill
            try
            {
                starter.AddGameMenuOption("ldm_tavern_menu", "ldm_tavern_cheap",
                    "{TAVERN_CHEAP_TEXT}",
                    args =>
                    {
                        try
                        {
                            bool canAfford = (Hero.MainHero?.Gold ?? 0) >= 20;
                            if (!canAfford) args.IsEnabled = false;
                            MBTextManager.SetTextVariable("TAVERN_CHEAP_TEXT",
                                canAfford ? "Order the cheap swill (20 denars)" : "Order the cheap swill (20 denars)  [not enough coin]");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => { try { DrinkRound(20, 1); } catch { } },
                    false, -1, false);
            }
            catch { }

            // Decent ale
            try
            {
                starter.AddGameMenuOption("ldm_tavern_menu", "ldm_tavern_decent",
                    "{TAVERN_DECENT_TEXT}",
                    args =>
                    {
                        try
                        {
                            bool canAfford = (Hero.MainHero?.Gold ?? 0) >= 50;
                            if (!canAfford) args.IsEnabled = false;
                            MBTextManager.SetTextVariable("TAVERN_DECENT_TEXT",
                                canAfford ? "Order a decent ale (50 denars)" : "Order a decent ale (50 denars)  [not enough coin]");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => { try { DrinkRound(50, 2); } catch { } },
                    false, -1, false);
            }
            catch { }

            // Fine wine
            try
            {
                starter.AddGameMenuOption("ldm_tavern_menu", "ldm_tavern_fine",
                    "{TAVERN_FINE_TEXT}",
                    args =>
                    {
                        try
                        {
                            bool canAfford = (Hero.MainHero?.Gold ?? 0) >= 100;
                            if (!canAfford) args.IsEnabled = false;
                            MBTextManager.SetTextVariable("TAVERN_FINE_TEXT",
                                canAfford ? "Order the finest wine in the house (100 denars)" : "Order the finest wine in the house (100 denars)  [not enough coin]");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => { try { DrinkRound(100, 3); } catch { } },
                    false, -1, false);
            }
            catch { }

            // Leave
            try
            {
                starter.AddGameMenuOption("ldm_tavern_menu", "ldm_tavern_leave",
                    "Call it a night.",
                    args =>
                    {
                        try { args.optionLeaveType = GameMenuOption.LeaveType.Leave; } catch { }
                        return true;
                    },
                    args => { try { GameMenu.ExitToLast(); } catch { } },
                    true, -1, false);
            }
            catch { }
        }

        // ── Result screen ─────────────────────────────────────────────────────
        private static void RegisterResultMenu(CampaignGameStarter starter)
        {
            try
            {
                starter.AddGameMenu("ldm_tavern_result", "{TAVERN_RESULT_TEXT}", args =>
                {
                    // text is set before switching to this menu
                });
            }
            catch { }

            // Another round
            try
            {
                starter.AddGameMenuOption("ldm_tavern_result", "ldm_tavern_another",
                    "{TAVERN_ANOTHER_TEXT}",
                    args =>
                    {
                        try
                        {
                            MBTextManager.SetTextVariable("TAVERN_ANOTHER_TEXT",
                                $"Push your luck. Order another ({_lastDrinkCost} denars).");
                            bool canAfford = (Hero.MainHero?.Gold ?? 0) >= _lastDrinkCost;
                            if (!canAfford) args.IsEnabled = false;
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => { try { GameMenu.SwitchToMenu("ldm_tavern_menu"); } catch { } },
                    false, -1, false);
            }
            catch { }

            // Leave from result screen
            try
            {
                starter.AddGameMenuOption("ldm_tavern_result", "ldm_tavern_result_leave",
                    "That's enough for one night.",
                    args =>
                    {
                        try { args.optionLeaveType = GameMenuOption.LeaveType.Leave; } catch { }
                        return true;
                    },
                    args => { try { GameMenu.ExitToLast(); } catch { } },
                    true, -1, false);
            }
            catch { }
        }

        // ── Sober-up wait menu ────────────────────────────────────────────────
        private static void RegisterSoberUpMenu(CampaignGameStarter starter)
        {
            try
            {
                starter.AddWaitGameMenu("ldm_tavern_sober_up", "{TAVERN_SOBER_TEXT}",
                    new OnInitDelegate(SoberOnInit),
                    new OnConditionDelegate(SoberOnCondition),
                    new OnConsequenceDelegate(SoberOnConsequence),
                    new OnTickDelegate(SoberOnTick),
                    GameMenu.MenuAndOptionType.WaitMenuShowOnlyProgressOption,
                    GameMenu.MenuOverlayType.None, 0f, GameMenu.MenuFlags.None, null);
            }
            catch { }
        }

        private static void SoberOnInit(MenuCallbackArgs args)
        {
            try
            {
                UpdateSoberText();
                args.MenuContext.GameMenu.StartWait();
                args.MenuContext.GameMenu.SetTargetedWaitingTimeAndInitialProgress(
                    Math.Max(1f, _soberHoursTotal), 0f);
            }
            catch { }
        }

        private static bool SoberOnCondition(MenuCallbackArgs args) => true;

        private static void SoberOnConsequence(MenuCallbackArgs args)
        {
            try
            {
                if (!_soberDone)
                {
                    float remaining = _soberHoursTotal - _soberHoursElapsed;
                    if (remaining <= 0.01f) { WakeUp(); return; }
                    args.MenuContext.GameMenu.StartWait();
                    args.MenuContext.GameMenu.SetTargetedWaitingTimeAndInitialProgress(
                        Math.Max(1f, remaining), 0f);
                }
            }
            catch { try { WakeUp(); } catch { } }
        }

        private static void SoberOnTick(MenuCallbackArgs args, CampaignTime dt)
        {
            try
            {
                if (_soberDone) return;
                _soberHoursElapsed += (float)dt.ToHours;

                UpdateSoberText();
                try
                {
                    args.MenuContext.GameMenu.SetProgressOfWaitingInMenu(
                        Math.Min(1f, _soberHoursElapsed / Math.Max(1f, _soberHoursTotal)));
                }
                catch { }

                if (_soberHoursElapsed >= _soberHoursTotal)
                    WakeUp();
            }
            catch { }
        }

        private static void UpdateSoberText()
        {
            try
            {
                int left = Math.Max(0, (int)(_soberHoursTotal - _soberHoursElapsed));
                MBTextManager.SetTextVariable("TAVERN_SOBER_TEXT",
                    $"The floor of the common room. Straw, boots, and the smell of spilled ale. " +
                    $"You are not dying, but it feels that way. About {left} hour(s) before you can stand without the room spinning.");
            }
            catch { }
        }

        private static void WakeUp()
        {
            if (_soberDone) return;
            _soberDone = true;
            try
            {
                // Small chance of being robbed
                int gold = Hero.MainHero?.Gold ?? 0;
                if (gold > 0 && _rng.NextDouble() < 0.25)
                {
                    int stolen = Math.Min(gold, 20 + _rng.Next(81));
                    try { Hero.MainHero?.ChangeHeroGold(-stolen); } catch { }
                    Msg($"You wake with a splitting head and a lighter purse. Someone helped themselves to {stolen} denars while you slept.", BadColor);
                }
                else
                {
                    Msg("You wake stiff and dry-mouthed, but intact. The common room is already filling for the morning meal.", DimColor);
                }
                AddMorale(-3f);
                ResetSessionState();
                try { GameMenu.ExitToLast(); } catch { }
            }
            catch { }
        }

        // =====================================================================
        // DRINK ROUND LOGIC
        // =====================================================================
        private static void DrinkRound(int cost, int tier)
        {
            if ((Hero.MainHero?.Gold ?? 0) < cost)
            {
                Msg($"You count your coin. Not enough for another round. ({cost} needed)", BadColor);
                return;
            }

            try { Hero.MainHero?.ChangeHeroGold(-cost); } catch { }
            _roundsDrunk++;
            _totalSpent += cost;
            _lastDrinkCost = cost;

            // Athletics check — harder with each round consumed
            int athletics = 0;
            try { athletics = Hero.MainHero?.GetSkillValue(DefaultSkills.Athletics) ?? 0; } catch { }

            float drunkPenalty = (_roundsDrunk - 1) * 0.09f;
            float tierBonus    = (tier - 1) * 0.10f;
            float chance       = Math.Min(0.92f, Math.Max(0.05f, 0.38f + athletics * 0.002f + tierBonus - drunkPenalty));

            bool controlled = _rng.NextDouble() < chance;

            // After 5+ rounds a failed check means passing out
            bool passedOut = !controlled && _roundsDrunk >= 5;

            // Very unlucky even on round 3+
            if (!controlled && _roundsDrunk >= 3 && _rng.NextDouble() < 0.35)
                passedOut = true;

            if (passedOut)
            {
                BeginPassOut();
                return;
            }

            // Roll the outcome
            string resultText;
            if (controlled)
                resultText = RollGoodOutcome(tier);
            else
                resultText = RollBadOutcome(tier);

            try
            {
                MBTextManager.SetTextVariable("TAVERN_RESULT_TEXT",
                    $"Round {_roundsDrunk}. {resultText}");
                GameMenu.SwitchToMenu("ldm_tavern_result");
            }
            catch { }
        }

        private static void BeginPassOut()
        {
            try
            {
                // Give athletics XP for making it this far
                try { Hero.MainHero?.HeroDeveloper?.AddSkillXp(DefaultSkills.Athletics, 80f); } catch { }

                _soberHoursTotal   = 4f + _rng.Next(5); // 4-8 hours
                _soberHoursElapsed = 0f;
                _soberDone         = false;
                AddMorale(-2f);
                GameMenu.SwitchToMenu("ldm_tavern_sober_up");
            }
            catch { }
        }

        // ── Good outcomes ─────────────────────────────────────────────────────
        private static string RollGoodOutcome(int tier)
        {
            // Higher tier shifts weight toward better outcomes
            int roll = _rng.Next(100) + (tier - 1) * 15;

            if (roll < 30)
                return OutcomeNothing();
            if (roll < 48)
                return OutcomeRelationGain();
            if (roll < 62)
                return OutcomeRecruit();
            if (roll < 74)
                return OutcomeGossip();
            if (roll < 84)
                return OutcomeMerchantTip(tier);
            if (roll < 93)
                return OutcomeLordEncounterGood();
            return OutcomeFoundPurse();
        }

        // ── Bad outcomes ──────────────────────────────────────────────────────
        private static string RollBadOutcome(int tier)
        {
            // Higher tier slightly reduces severity (finer establishments, politer patrons)
            int roll = _rng.Next(100) - (tier - 1) * 8;

            if (roll < 22)
                return OutcomeOffend();
            if (roll < 42)
                return OutcomeBrawl();
            if (roll < 56)
                return OutcomeLordEncounterBad();
            if (roll < 68)
                return OutcomePuking();
            if (roll < 82)
                return OutcomeJail();
            return OutcomeOffend(); // fallback
        }

        // ── Outcome implementations ───────────────────────────────────────────

        private static string OutcomeNothing()
        {
            string[] lines =
            {
                "You drink. The fire pops. Someone laughs at the far end of the room. Nothing worth remembering happens — and that, tonight, is enough.",
                "The ale is honest work. You nurse it quietly and watch the room. Nobody notices you. Nobody needs to.",
                "A warm evening. You listen to a bard butcher an old song and decide the world has worse problems.",
                "The cup empties. You feel it. The night asks nothing of you.",
            };
            return lines[_rng.Next(lines.Length)];
        }

        private static string OutcomeRelationGain()
        {
            try
            {
                // Try to find a notable in the current settlement
                var settlement = Settlement.CurrentSettlement;
                var notables = settlement?.Notables?.Where(h => h != null && h.IsAlive && h != Hero.MainHero).ToList();
                if (notables != null && notables.Count > 0)
                {
                    var notable = notables[_rng.Next(notables.Count)];
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, notable, 1, false);
                    return $"You end up sharing a table with {notable.Name}. The conversation is easier than expected — " +
                           $"common ground found in the bottom of a cup. (Relation with {notable.Name}: +1)";
                }

                // Fallback: random lord
                ChangeRelWithRandomHero(1);
                return "By the second cup someone has decided you are fine company. A bond formed in firelight — thin, but real.";
            }
            catch
            {
                return "By the second cup someone has decided you are fine company.";
            }
        }

        private static string OutcomeRecruit()
        {
            try
            {
                int tier = 1 + _rng.Next(3); // 1-3
                CharacterObject troop = GetRecruitOfTier(tier);
                if (troop != null)
                {
                    try { MobileParty.MainParty.MemberRoster.AddToCounts(troop, 1); } catch { }
                    return $"A {troop.Name} slumped in the corner straightens when your coin hits the table. " +
                           $"\"You hiring?\" You are. They follow you out. (+1 {troop.Name})";
                }
                // Fallback if no troop found
                try { Hero.MainHero?.ChangeHeroGold(30); } catch { }
                return "A soldier in the corner offers steady eyes and a steady hand in exchange for a place at your fire. " +
                       "They accept the coin instead when the roster is full. (+30 denars)";
            }
            catch
            {
                return "A down-on-their-luck soldier shares your table. They have nowhere better to be.";
            }
        }

        private static string OutcomeGossip()
        {
            string[] gossips =
            {
                "A man leans close and whispers: a lord two towns over has been moving coin in the dead of night. " +
                "Whether it is taxes, bribes, or something stranger, he cannot say.",
                "An old woman with sharp eyes tells you about a patrol that did not come back last month. " +
                "The garrison is pretending it did. She finds that suspicious. So do you.",
                "A merchant in his cups confides that one of the local trade guilds has been buying grain at twice the market price. " +
                "Someone is expecting a siege — or creating one.",
                "Two soldiers argue quietly about orders they were not supposed to hear. You catch enough to know something is moving that should not be.",
                "A courier fresh off the road talks too freely. A lord nearby is changing sides — or thinking about it.",
            };

            try
            {
                if (Hero.MainHero?.Clan != null)
                    Hero.MainHero.Clan.Influence += 5f;
            }
            catch { }

            return gossips[_rng.Next(gossips.Length)] + " (+5 influence)";
        }

        private static string OutcomeMerchantTip(int tier)
        {
            int gold = tier == 1 ? 30 + _rng.Next(21)
                     : tier == 2 ? 60 + _rng.Next(41)
                                 : 120 + _rng.Next(81);
            try { Hero.MainHero?.ChangeHeroGold(gold); } catch { }
            return $"A merchant is grateful for your company — or perhaps just drunk enough to be generous. " +
                   $"He presses a purse into your hand before his friends drag him off. (+{gold} denars)";
        }

        private static string OutcomeLordEncounterGood()
        {
            try
            {
                var lord = GetLordInSettlement();
                if (lord != null)
                {
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, 2, false);
                    return $"{lord.Name} enters the common room, sees you at the bar, and sits. " +
                           $"Two people without their retinues, sharing something honest. " +
                           $"You part better than you arrived. (Relation with {lord.Name}: +2)";
                }
            }
            catch { }
            return "A lord's steward buys you a cup — which means the lord noticed you, and found it worth noting. The gesture costs nothing and means something.";
        }

        private static string OutcomeFoundPurse()
        {
            int gold = 15 + _rng.Next(36);
            try { Hero.MainHero?.ChangeHeroGold(gold); } catch { }
            return $"You find a fat little purse wedged between the bench and the wall. " +
                   $"You wait. Nobody comes back for it. You reason that whoever lost it has had worse nights than you. (+{gold} denars)";
        }

        // ── Bad outcome implementations ───────────────────────────────────────

        private static string OutcomeOffend()
        {
            try
            {
                var notables = Settlement.CurrentSettlement?.Notables?.Where(h => h != null && h.IsAlive && h != Hero.MainHero).ToList();
                if (notables != null && notables.Count > 0)
                {
                    var notable = notables[_rng.Next(notables.Count)];
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, notable, -1, false);
                    return $"You say something that lands poorly. {notable.Name} doesn't raise their voice — " +
                           $"just looks at you the way people look at things they have decided to remember. " +
                           $"(Relation with {notable.Name}: -1)";
                }
            }
            catch { }
            ChangeRelWithRandomHero(-1);
            return "You say the wrong thing to the wrong person. They leave without a word. " +
                   "You suspect this will be remembered. (Relation: -1)";
        }

        private static string OutcomeBrawl()
        {
            try
            {
                // Deal minor damage to player
                var agent = Hero.MainHero;
                if (agent != null)
                {
                    // Add crime and a morale hit — actual HP can't easily be modified out of mission
                    AddMorale(-4f);
                    ChangeCrime(5f);
                }
            }
            catch { }
            string[] lines =
            {
                "A man takes exception to your elbow. Then your face. You give back what you can. " +
                "You leave with a split lip and a fine for disturbing the peace. (-4 morale, +5 crime)",
                "Three words and someone's stool becomes a weapon. You handle yourself, mostly. " +
                "The watch arrives and everyone pays for the trouble. (-4 morale, +5 crime)",
                "The brawl is short and final — over a comment nobody fully remembers making. " +
                "The tavernkeeper's face says this is a regular occurrence. (-4 morale, +5 crime)",
            };
            return lines[_rng.Next(lines.Length)];
        }

        private static string OutcomeLordEncounterBad()
        {
            try
            {
                var lord = GetLordInSettlement();
                if (lord == null)
                {
                    // Pick any lord
                    lord = Hero.AllAliveHeroes
                        .Where(h => h.IsLord && h.IsAlive && h != Hero.MainHero)
                        .OrderBy(_ => _rng.Next())
                        .FirstOrDefault();
                }
                if (lord != null)
                {
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, -2, false);
                    return $"{lord.Name} enters the common room at exactly the wrong moment — " +
                           $"when you are at your least dignified. They say nothing. They don't have to. " +
                           $"(Relation with {lord.Name}: -2)";
                }
            }
            catch { }
            return "Someone important witnesses something they should not have. The look they give you will not be forgotten.";
        }

        private static string OutcomePuking()
        {
            AddMorale(-5f);
            string[] lines =
            {
                "The cup wins. You spend an unpleasant quarter-hour in the alley and return to the table with significantly less dignity. (-5 morale)",
                "Your body registers a formal objection to the evening's proceedings. The alley wall bears the evidence. (-5 morale)",
                "The drinks disagree with you in a loud and public way. The tavernkeeper points to the door. The alley, at least, is private. (-5 morale)",
            };
            return lines[_rng.Next(lines.Length)];
        }

        private static string OutcomeJail()
        {
            try
            {
                ChangeCrime(15f);
                // Use a brief sober-up to simulate time in a cell
                _soberHoursTotal   = 8f + _rng.Next(5); // 8-12 hours
                _soberHoursElapsed = 0f;
                _soberDone         = false;

                // Switch to the sober-up menu after the result is shown briefly
                // We defer this via the deferred inquiry so the result menu can appear first
                MageKnowledge._deferredInquiry = () =>
                {
                    try
                    {
                        MBTextManager.SetTextVariable("TAVERN_SOBER_TEXT",
                            "A cell in the garrison tower. Straw, iron, and regret. The watch picked you up sometime after the third hour. " +
                            "They will let you out when they feel like it.");
                        GameMenu.SwitchToMenu("ldm_tavern_sober_up");
                    }
                    catch { }
                };
            }
            catch { }
            return "The watch arrives. You are not in a position to argue. " +
                   "The cell is cold and the night is long. (+15 crime — you will be held until morning)";
        }

        // =====================================================================
        // HELPERS
        // =====================================================================

        private static CharacterObject GetRecruitOfTier(int targetTier)
        {
            try
            {
                var culture = Settlement.CurrentSettlement?.Culture
                           ?? Hero.MainHero?.Culture;
                CharacterObject troop = culture?.BasicTroop;
                if (troop == null) return null;

                // Walk the upgrade tree up to the target tier
                for (int i = 0; i < targetTier - 1 && troop != null; i++)
                {
                    if (troop.UpgradeTargets != null && troop.UpgradeTargets.Length > 0)
                        troop = troop.UpgradeTargets[0];
                    else
                        break;
                }
                return troop;
            }
            catch { return null; }
        }

        private static Hero GetLordInSettlement()
        {
            try
            {
                var s = Settlement.CurrentSettlement;
                if (s == null) return null;
                return Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && h != Hero.MainHero
                             && !h.IsPrisoner && h.CurrentSettlement == s)
                    .OrderBy(_ => _rng.Next())
                    .FirstOrDefault();
            }
            catch { return null; }
        }

        private static void ChangeRelWithRandomHero(int delta)
        {
            try
            {
                var heroes = Hero.AllAliveHeroes
                    .Where(h => h.IsAlive && h != Hero.MainHero && !h.IsPrisoner && h.IsLord)
                    .ToList();
                if (heroes.Count == 0) return;
                var target = heroes[_rng.Next(heroes.Count)];
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, target, delta, false);
                string sign = delta >= 0 ? "+" : "";
                Msg($"(Relation with {target.Name}: {sign}{delta})", delta >= 0 ? GoodColor : BadColor);
            }
            catch { }
        }

        private static void ChangeCrime(float amount)
        {
            try
            {
                var kingdom = Hero.MainHero?.MapFaction as Kingdom;
                if (kingdom != null)
                    ChangeCrimeRatingAction.Apply(kingdom, amount, true);
            }
            catch { }
        }

        private static void AddMorale(float delta)
        {
            try { if (MobileParty.MainParty != null) MobileParty.MainParty.RecentEventsMorale += delta; } catch { }
        }

        private static void Msg(string text, Color c)
        {
            try { MBInformationManager.AddQuickInformation(new TextObject(text)); }
            catch { try { InformationManager.DisplayMessage(new InformationMessage(text, c)); } catch { } }
        }
    }
}
