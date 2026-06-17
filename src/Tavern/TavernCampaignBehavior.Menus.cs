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
            RegisterResultMenu(starter);
            RegisterSoberUpMenu(starter);
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
    }
}
