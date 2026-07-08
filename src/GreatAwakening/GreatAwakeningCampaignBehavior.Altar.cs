// =============================================================================
// ASH AND EMBER — GreatAwakeningCampaignBehavior.Altar.cs
//
// "Prepare for a Great Summoning" — the town menu option at the one Dark Altar
// city chosen for the Great Awakening (GreatAwakeningCampaignBehavior.cs).
// Contributing pours out the player's ENTIRE current prisoner roster at once
// (no numeric input popup — simpler, and it matches the leader's own words:
// "every prisoner our knives can spare"). Only a Duneborn-aligned player can
// contribute; anyone else may still visit and read the running count.
// =============================================================================

using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class GreatAwakeningCampaignBehavior
    {
        internal static void RegisterAltarMenus(CampaignGameStarter starter)
        {
            try
            {
                starter.AddGameMenuOption("town", "grawk_altar_enter", "{GRAWK_ALTAR_ENTER_TEXT}",
                    args =>
                    {
                        try
                        {
                            if (_phase < PhaseActive) return false;
                            if (Settlement.CurrentSettlement == null
                                || Settlement.CurrentSettlement.StringId != _altarSettlementId) return false;
                            MBTextManager.SetTextVariable("GRAWK_ALTAR_ENTER_TEXT",
                                $"Prepare for a Great Summoning  [{_prisonersSacrificed:N0} / {GreatAwakeningMath.PrisonerTarget:N0}]");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                            args.IsEnabled = true;
                            return true;
                        }
                        catch { return false; }
                    },
                    args => { try { GameMenu.SwitchToMenu("grawk_altar_main"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } },
                    false, -1, false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try
            {
                starter.AddGameMenu("grawk_altar_main", "{GRAWK_ALTAR_HEADER}", args =>
                {
                    try
                    {
                        bool ownedByDuneborn = AltarIsDunebornOwned();
                        string status = ownedByDuneborn
                            ? "The Dark Altar still answers to Duneborn."
                            : "The altar has fallen out of Duneborn's hands. Nothing offered here reaches it now.";
                        MBTextManager.SetTextVariable("GRAWK_ALTAR_HEADER",
                            "Something vast and patient waits on the other side of this stone. It does not need to be fed "
                          + "quickly — only fully.\n\n"
                          + $"Sacrificed toward the Great Summoning: {_prisonersSacrificed:N0} / {GreatAwakeningMath.PrisonerTarget:N0}\n\n"
                          + status);
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                });
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try
            {
                starter.AddGameMenuOption("grawk_altar_main", "grawk_altar_contribute", "{GRAWK_ALTAR_CONTRIBUTE_TEXT}",
                    args =>
                    {
                        try
                        {
                            if (_phase != PhaseActive) return false;
                            int held = PlayerPrisonerCount();
                            bool canGive = AltarIsDunebornOwned() && IsPlayerOnDunebornPath() && held > 0;
                            string note = !AltarIsDunebornOwned() ? "  [the altar is not Duneborn's to use]"
                                        : !IsPlayerOnDunebornPath() ? "  [only Duneborn's own may offer here]"
                                        : held <= 0 ? "  [you hold no prisoners]"
                                        : "";
                            MBTextManager.SetTextVariable("GRAWK_ALTAR_CONTRIBUTE_TEXT",
                                $"Give every prisoner you hold to the summoning ({held}){note}");
                            args.IsEnabled = canGive;
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Continue; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                            return true;
                        }
                        catch { return false; }
                    },
                    args => DoContribute());
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try
            {
                starter.AddGameMenuOption("grawk_altar_main", "grawk_altar_leave", "Step away",
                    args => { try { args.optionLeaveType = GameMenuOption.LeaveType.Leave; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } return true; },
                    args => { try { GameMenu.SwitchToMenu("town"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } },
                    true, -1, false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static int PlayerPrisonerCount()
        {
            try
            {
                var roster = MobileParty.MainParty?.PrisonRoster?.GetTroopRoster();
                return roster?.Where(e => !e.Character.IsHero).Sum(e => e.Number) ?? 0;
            }
            catch { return 0; }
        }

        private static void DoContribute()
        {
            try
            {
                var party = MobileParty.MainParty;
                var roster = party?.PrisonRoster?.GetTroopRoster()?.ToList();
                if (roster == null) return;

                int given = 0;
                foreach (var entry in roster)
                {
                    if (entry.Character.IsHero || entry.Number <= 0) continue;
                    party.PrisonRoster.AddToCounts(entry.Character, -entry.Number);
                    given += entry.Number;
                }
                if (given <= 0) return;

                _prisonersSacrificed += given;
                GreatAwakeningQuestLog.UpdateProgress(_prisonersSacrificed, GreatAwakeningMath.PrisonerTarget);

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"{given} prisoners are led to the Dark Altar and do not come back. " +
                    $"The Great Summoning stands at {_prisonersSacrificed:N0} / {GreatAwakeningMath.PrisonerTarget:N0}."));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
