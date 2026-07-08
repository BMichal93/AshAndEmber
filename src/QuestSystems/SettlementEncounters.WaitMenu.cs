// =============================================================================
// ASH AND EMBER — SettlementEncounters.WaitMenu.cs
// A general-purpose blocking wait, mirroring SeaCampaignBehavior's voyage menu
// and TavernCampaignBehavior's inn-stay menu, for the rare settlement encounter
// choice that means the player actually stands still — as opposed to the
// far more common "you move on, word catches up with you later" choices,
// which resolve through the ordinary DailyTick countdown fields instead.
//
// NOTE: unlike Sea/Tavern's wait menus (always entered by clicking an option
// inside an already-open menu, so GameMenu.SwitchToMenu has somewhere to
// return from), StartWait() is called from an ambient inquiry callback with
// no menu currently open — the settlement encounter has already fired via
// OnPartyLeftSettlement by that point. It uses GameMenu.ActivateGameMenu
// instead of SwitchToMenu for that reason. This is the first place in the mod
// that opens a game menu from map/ambient code rather than from inside
// another menu, and the exact signature could not be verified against the
// live TaleWorlds DLLs in the environment this was written in — build and
// test in-game before trusting it.
//
// Partial of SettlementEncounters (shared state lives in SettlementEncounters.cs).
// =============================================================================

using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public static partial class SettlementEncounters
    {
        // Not persisted — same convention as Sea's voyage state and Tavern's inn-stay
        // state: a reload mid-wait simply loses the pending callback, matching how
        // a mid-crossing voyage reload already refunds rather than resumes.
        private static float  _seWaitHoursTotal;
        private static float  _seWaitHoursElapsed;
        private static bool   _seWaitDone;
        private static string _seWaitLine;
        private static Action _seWaitOnComplete;

        /// Blocks the player at the current settlement for the given number of days,
        /// then invokes onComplete once the wait finishes. line is the ongoing
        /// description shown while the wait menu is open.
        private static void StartWait(int days, string line, Action onComplete)
        {
            _seWaitHoursTotal   = Math.Max(1f, days * 24f);
            _seWaitHoursElapsed = 0f;
            _seWaitDone         = false;
            _seWaitLine         = line;
            _seWaitOnComplete   = onComplete;
            try { GameMenu.ActivateGameMenu("se_wait_menu"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        public static void RegisterWaitMenu(CampaignGameStarter starter)
        {
            try
            {
                starter.AddWaitGameMenu("se_wait_menu", "{SE_WAIT_TEXT}",
                    new OnInitDelegate(WaitOnInit),
                    new OnConditionDelegate(WaitOnCondition),
                    new OnConsequenceDelegate(WaitOnConsequence),
                    new OnTickDelegate(WaitOnTick),
                    GameMenu.MenuAndOptionType.WaitMenuShowOnlyProgressOption,
                    GameMenu.MenuOverlayType.None, 0f, GameMenu.MenuFlags.None, null);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void WaitOnInit(MenuCallbackArgs args)
        {
            try
            {
                UpdateWaitText();
                args.MenuContext.GameMenu.StartWait();
                args.MenuContext.GameMenu.SetTargetedWaitingTimeAndInitialProgress(
                    Math.Max(1f, _seWaitHoursTotal), 0f);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static bool WaitOnCondition(MenuCallbackArgs args) => true;

        private static void WaitOnConsequence(MenuCallbackArgs args)
        {
            try
            {
                if (_seWaitDone) return;
                float remaining = _seWaitHoursTotal - _seWaitHoursElapsed;
                if (remaining <= 0.01f) { FinishWait(); return; }
                args.MenuContext.GameMenu.StartWait();
                args.MenuContext.GameMenu.SetTargetedWaitingTimeAndInitialProgress(
                    Math.Max(1f, remaining), 0f);
            }
            catch { try { FinishWait(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } }
        }

        private static void WaitOnTick(MenuCallbackArgs args, CampaignTime dt)
        {
            try
            {
                if (_seWaitDone) return;
                _seWaitHoursElapsed += (float)dt.ToHours;
                UpdateWaitText();
                try
                {
                    args.MenuContext.GameMenu.SetProgressOfWaitingInMenu(
                        Math.Min(1f, _seWaitHoursElapsed / Math.Max(1f, _seWaitHoursTotal)));
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                if (_seWaitHoursElapsed >= _seWaitHoursTotal)
                    FinishWait();
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void UpdateWaitText()
        {
            try
            {
                int left = Math.Max(0, (int)Math.Ceiling((_seWaitHoursTotal - _seWaitHoursElapsed) / 24f));
                string place = Settlement.CurrentSettlement?.Name?.ToString() ?? "the settlement";
                string remain = left <= 1
                    ? $"The night is not over yet at {place}."
                    : $"About {left} day(s) remain at {place}.";
                MBTextManager.SetTextVariable("SE_WAIT_TEXT", $"{_seWaitLine}\n\n{remain}");
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void FinishWait()
        {
            if (_seWaitDone) return;
            _seWaitDone = true;
            Action callback = _seWaitOnComplete;
            _seWaitOnComplete = null;
            try { callback?.Invoke(); }
            finally { try { GameMenu.ExitToLast(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } }
        }
    }
}
