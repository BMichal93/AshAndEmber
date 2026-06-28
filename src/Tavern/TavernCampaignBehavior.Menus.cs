// =============================================================================
// ASH AND EMBER — TavernCampaignBehavior.Menus.cs
// Tavernkeeper dialogue, game menus, and the sober-up wait menu.
// Partial of TavernCampaignBehavior (shared static state lives in TavernCampaignBehavior.cs).
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
    public partial class TavernCampaignBehavior
    {
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
            RegisterTownEntry(starter);
            RegisterOrderMenu(starter);
            RegisterDiceMenus(starter);
            RegisterResultMenu(starter);
            RegisterSoberUpMenu(starter);
            RegisterInnStayMenu(starter);
        }

        // Reliable access from the town menu — the tavernkeeper dialogue line is a
        // bonus path, but this entry guarantees the game is reachable in any town.
        private static void RegisterTownEntry(CampaignGameStarter starter)
        {
            try
            {
                starter.AddGameMenuOption("town", "ldm_tavern_enter",
                    "Drink with the locals at the tavern",
                    args =>
                    {
                        try
                        {
                            if (Settlement.CurrentSettlement?.IsTown != true) return false;
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; } catch { }
                            return true;
                        }
                        catch { return false; }
                    },
                    args => { try { ResetSessionState(); GameMenu.SwitchToMenu("ldm_tavern_menu"); } catch { } },
                    false, -1, false);
            }
            catch { }
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
                            bool canAfford = (Hero.MainHero?.Gold ?? 0) >= 100;
                            if (!canAfford) args.IsEnabled = false;
                            MBTextManager.SetTextVariable("TAVERN_DECENT_TEXT",
                                canAfford ? "Order a decent ale (100 denars)" : "Order a decent ale (100 denars)  [not enough coin]");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => { try { DrinkRound(100, 2); } catch { } },
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
                            bool canAfford = (Hero.MainHero?.Gold ?? 0) >= 500;
                            if (!canAfford) args.IsEnabled = false;
                            MBTextManager.SetTextVariable("TAVERN_FINE_TEXT",
                                canAfford ? "Order the finest wine in the house (500 denars)" : "Order the finest wine in the house (500 denars)  [not enough coin]");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => { try { DrinkRound(500, 3); } catch { } },
                    false, -1, false);
            }
            catch { }

            // Listen for rumours
            try
            {
                starter.AddGameMenuOption("ldm_tavern_menu", "ldm_tavern_rumors",
                    "{TAVERN_RUMORS_TEXT}",
                    args =>
                    {
                        try
                        {
                            bool canAfford = (Hero.MainHero?.Gold ?? 0) >= 30;
                            if (!canAfford) args.IsEnabled = false;
                            MBTextManager.SetTextVariable("TAVERN_RUMORS_TEXT",
                                canAfford
                                    ? "Listen for word of the world (30 denars)"
                                    : "Listen for word of the world (30 denars)  [not enough coin]");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => { try { TryListenForRumors(); } catch { } },
                    false, -1, false);
            }
            catch { }

            // Roll dice
            try
            {
                starter.AddGameMenuOption("ldm_tavern_menu", "ldm_tavern_dice",
                    "Join a dice game",
                    args =>
                    {
                        try { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; } catch { }
                        return true;
                    },
                    args => { try { GameMenu.SwitchToMenu("ldm_dice_menu"); } catch { } },
                    false, -1, false);
            }
            catch { }

            // Spend an evening
            try
            {
                starter.AddGameMenuOption("ldm_tavern_menu", "ldm_tavern_evening",
                    "{TAVERN_EVENING_TEXT}",
                    args =>
                    {
                        try
                        {
                            int eveningCost = TavernCampaignBehavior.EveningCost();
                            bool canAfford = (Hero.MainHero?.Gold ?? 0) >= eveningCost;
                            if (!canAfford) args.IsEnabled = false;
                            MBTextManager.SetTextVariable("TAVERN_EVENING_TEXT",
                                canAfford
                                    ? $"Spend an evening in good company ({eveningCost} denars)"
                                    : $"Spend an evening in good company ({eveningCost} denars)  [not enough coin]");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => { try { SpendEvening(); } catch { } },
                    false, -1, false);
            }
            catch { }

            // Smoke the rare weeds — only the land-attuned have any use for them.
            try
            {
                starter.AddGameMenuOption("ldm_tavern_menu", "ldm_tavern_weeds",
                    "{TAVERN_WEEDS_TEXT}",
                    args =>
                    {
                        try
                        {
                            if (!NatureKnowledge.IsAttuned) return false;
                            int cost = WeedCost();
                            bool canAfford = (Hero.MainHero?.Gold ?? 0) >= cost;
                            if (!canAfford) args.IsEnabled = false;
                            string held = NatureKnowledge.WeedBlessingActive ? "  [communion already upon you]" : "";
                            MBTextManager.SetTextVariable("TAVERN_WEEDS_TEXT",
                                canAfford
                                    ? $"Buy a pouch of the old green and smoke it ({cost} denars){held}"
                                    : $"Buy a pouch of the old green and smoke it ({cost} denars)  [not enough coin]");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { return false; }
                        return true;
                    },
                    args => { try { SmokeNatureWeeds(); } catch { } },
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

            // Another round / Roll again
            try
            {
                starter.AddGameMenuOption("ldm_tavern_result", "ldm_tavern_another",
                    "{TAVERN_ANOTHER_TEXT}",
                    args =>
                    {
                        try
                        {
                            if (_diceMode)
                            {
                                MBTextManager.SetTextVariable("TAVERN_ANOTHER_TEXT", "Roll again.");
                                bool canAfford = (Hero.MainHero?.Gold ?? 0) >= DiceSmallBet();
                                if (!canAfford) args.IsEnabled = false;
                            }
                            else
                            {
                                MBTextManager.SetTextVariable("TAVERN_ANOTHER_TEXT",
                                    $"Push your luck. Order another ({_lastDrinkCost} denars).");
                                bool canAfford = (Hero.MainHero?.Gold ?? 0) >= _lastDrinkCost;
                                if (!canAfford) args.IsEnabled = false;
                            }
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args =>
                    {
                        try
                        {
                            if (_diceMode)
                                GameMenu.SwitchToMenu("ldm_dice_menu");
                            else
                                GameMenu.SwitchToMenu("ldm_tavern_menu");
                        }
                        catch { }
                    },
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
                string text = _weedRest
                    ? "A bench near the fire, or near enough. The green smoke has you. Sounds arrive late and meaning later. " +
                      $"You are not asleep and not awake — somewhere the land keeps. About {left} hour(s) before your limbs are your own again."
                    : "The floor of the common room. Straw, boots, and the smell of spilled ale. " +
                      $"You are not dying, but it feels that way. About {left} hour(s) before you can stand without the room spinning.";
                MBTextManager.SetTextVariable("TAVERN_SOBER_TEXT", text);
            }
            catch { }
        }

        // ── Dice game menus ───────────────────────────────────────────────────
        private static void RegisterDiceMenus(CampaignGameStarter starter)
        {
            // Bet selection screen
            try
            {
                starter.AddGameMenu("ldm_dice_menu", "{DICE_MENU_HEADER}", args =>
                {
                    try
                    {
                        MBTextManager.SetTextVariable("DICE_MENU_HEADER",
                            "A circle of men with a leather cup and a worn board of felt. " +
                            "They look up when you approach. One nudges the cup your way.");
                    }
                    catch { }
                });
            }
            catch { }

            // Small bet — always available
            try
            {
                starter.AddGameMenuOption("ldm_dice_menu", "ldm_dice_small",
                    "{DICE_SMALL_TEXT}",
                    args =>
                    {
                        try
                        {
                            int bet = DiceSmallBet();
                            bool canAfford = (Hero.MainHero?.Gold ?? 0) >= bet;
                            if (!canAfford) args.IsEnabled = false;
                            MBTextManager.SetTextVariable("DICE_SMALL_TEXT",
                                canAfford ? $"Low stakes ({bet} denars)" : $"Low stakes ({bet} denars)  [not enough coin]");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => { try { ResolveDiceGame(DiceSmallBet()); } catch { } },
                    false, -1, false);
            }
            catch { }

            // Medium bet — prosperous towns only
            try
            {
                starter.AddGameMenuOption("ldm_dice_menu", "ldm_dice_medium",
                    "{DICE_MEDIUM_TEXT}",
                    args =>
                    {
                        try
                        {
                            if (DiceMaxBet() < 200) return false;
                            int bet = DiceMediumBet();
                            bool canAfford = (Hero.MainHero?.Gold ?? 0) >= bet;
                            if (!canAfford) args.IsEnabled = false;
                            MBTextManager.SetTextVariable("DICE_MEDIUM_TEXT",
                                canAfford ? $"A real wager ({bet} denars)" : $"A real wager ({bet} denars)  [not enough coin]");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { return false; }
                        return true;
                    },
                    args => { try { ResolveDiceGame(DiceMediumBet()); } catch { } },
                    false, -1, false);
            }
            catch { }

            // Large bet — wealthy towns only
            try
            {
                starter.AddGameMenuOption("ldm_dice_menu", "ldm_dice_large",
                    "{DICE_LARGE_TEXT}",
                    args =>
                    {
                        try
                        {
                            if (DiceMaxBet() < 500) return false;
                            int bet = DiceLargeBet();
                            bool canAfford = (Hero.MainHero?.Gold ?? 0) >= bet;
                            if (!canAfford) args.IsEnabled = false;
                            MBTextManager.SetTextVariable("DICE_LARGE_TEXT",
                                canAfford ? $"Empty the table ({bet} denars)" : $"Empty the table ({bet} denars)  [not enough coin]");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { return false; }
                        return true;
                    },
                    args => { try { ResolveDiceGame(DiceLargeBet()); } catch { } },
                    false, -1, false);
            }
            catch { }

            // Walk away
            try
            {
                starter.AddGameMenuOption("ldm_dice_menu", "ldm_dice_back",
                    "Walk away.",
                    args =>
                    {
                        try { args.optionLeaveType = GameMenuOption.LeaveType.Leave; } catch { }
                        return true;
                    },
                    args => { try { GameMenu.SwitchToMenu("ldm_tavern_menu"); } catch { } },
                    true, -1, false);
            }
            catch { }
        }

        // ── Dice bet helpers ──────────────────────────────────────────────────
        // Max bet scales with prosperity so backwater villages can't host high-stakes games.
        private static int DiceMaxBet()
        {
            float p = Settlement.CurrentSettlement?.Town?.Prosperity ?? 1000f;
            return (int)Math.Max(100, p / 10);
        }

        private static int DiceSmallBet()  => Math.Max(25,  DiceMaxBet() / 4);
        private static int DiceMediumBet() => Math.Max(100, DiceMaxBet() / 2);
        private static int DiceLargeBet()  => DiceMaxBet();

        // ── Inn stay wait menu ────────────────────────────────────────────────
        private static void RegisterInnStayMenu(CampaignGameStarter starter)
        {
            try
            {
                starter.AddWaitGameMenu("ldm_inn_stay_menu", "{INN_STAY_TEXT}",
                    new OnInitDelegate(InnStayOnInit),
                    new OnConditionDelegate(InnStayOnCondition),
                    new OnConsequenceDelegate(InnStayOnConsequence),
                    new OnTickDelegate(InnStayOnTick),
                    GameMenu.MenuAndOptionType.WaitMenuShowOnlyProgressOption,
                    GameMenu.MenuOverlayType.None, 0f, GameMenu.MenuFlags.None, null);
            }
            catch { }
        }

        private static void InnStayOnInit(MenuCallbackArgs args)
        {
            try
            {
                UpdateInnStayText();
                args.MenuContext.GameMenu.StartWait();
                args.MenuContext.GameMenu.SetTargetedWaitingTimeAndInitialProgress(
                    Math.Max(1f, _innStayHoursTotal), 0f);
            }
            catch { }
        }

        private static bool InnStayOnCondition(MenuCallbackArgs args) => true;

        private static void InnStayOnConsequence(MenuCallbackArgs args)
        {
            try
            {
                if (!_innStayDone)
                {
                    float remaining = _innStayHoursTotal - _innStayHoursElapsed;
                    if (remaining <= 0.01f) { FinishInnStay(); return; }
                    args.MenuContext.GameMenu.StartWait();
                    args.MenuContext.GameMenu.SetTargetedWaitingTimeAndInitialProgress(
                        Math.Max(1f, remaining), 0f);
                }
            }
            catch { try { FinishInnStay(); } catch { } }
        }

        private static void InnStayOnTick(MenuCallbackArgs args, CampaignTime dt)
        {
            try
            {
                if (_innStayDone) return;
                _innStayHoursElapsed += (float)dt.ToHours;
                UpdateInnStayText();
                try
                {
                    args.MenuContext.GameMenu.SetProgressOfWaitingInMenu(
                        Math.Min(1f, _innStayHoursElapsed / Math.Max(1f, _innStayHoursTotal)));
                }
                catch { }
                if (_innStayHoursElapsed >= _innStayHoursTotal)
                    FinishInnStay();
            }
            catch { }
        }

        private static void UpdateInnStayText()
        {
            try
            {
                int left = Math.Max(0, (int)(_innStayHoursTotal - _innStayHoursElapsed));
                string inn = Settlement.CurrentSettlement?.Name?.ToString() ?? "the inn";
                MBTextManager.SetTextVariable("INN_STAY_TEXT",
                    $"{_innStayLine1}\n\n{_innStayLine2}\n\nAbout {left} hour(s) of evening remain at {inn}.");
            }
            catch { }
        }

        private static void FinishInnStay()
        {
            if (_innStayDone) return;
            _innStayDone = true;
            try
            {
                AddMorale(5f);
                Msg("The party rests well. A full evening of warmth and company does its work. (+5 morale)", GoodColor);
                try { GameMenu.ExitToLast(); } catch { }
            }
            catch { }
        }

        // ── Sober-up wait menu ────────────────────────────────────────────────
        private static void WakeUp()
        {
            if (_soberDone) return;
            _soberDone = true;

            // The weed drowse wakes differently — no purse-cutting, just the slow,
            // heavy return, with the communion now settled into you.
            if (_weedRest)
            {
                _weedRest = false;
                try
                {
                    Msg("You come back to yourself slow and heavy-limbed, ears ringing with a green quiet. " +
                        "The land hums faintly under everything now — and will, until tomorrow.", DimColor);
                    ResetSessionState();
                    try { GameMenu.SwitchToMenu("ldm_tavern_menu"); } catch { }
                }
                catch { }
                return;
            }

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
    }
}
