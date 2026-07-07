// =============================================================================
// ASH AND EMBER — AI/AshenRecruitCampaignBehavior.Menus.cs
//
// Game menu implementation for recruiting the Ashen dead.
//
// Menu tree:
//   "town" → "Muster the Ashen Dead"
//     → "ashen_recruit_main"  (description + which rank to bind)
//       → "ashen_recruit_bind_<0..4>"  (per-troop option — checks prisoners)
//       → "ashen_recruit_leave"
// =============================================================================

using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class AshenRecruitCampaignBehavior
    {
        private static void RegisterAshenRecruitMenus(CampaignGameStarter starter)
        {
            RegisterTownEntry(starter);
            RegisterMainMenu(starter);
            RegisterBindOptions(starter);
        }

        // ── Town entry ─────────────────────────────────────────────────────────
        private static void RegisterTownEntry(CampaignGameStarter starter)
        {
            try
            {
                starter.AddGameMenuOption("town", "ashen_recruit_enter", "{ASHEN_RECRUIT_ENTER_TEXT}",
                    args =>
                    {
                        try
                        {
                            if (!HasAshenRecruiter(Settlement.CurrentSettlement)) return false;
                            MBTextManager.SetTextVariable("ASHEN_RECRUIT_ENTER_TEXT", "Muster the Ashen Dead");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                            args.IsEnabled = true;
                            return true;
                        }
                        catch { return false; }
                    },
                    args => { try { GameMenu.SwitchToMenu("ashen_recruit_main"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } },
                    false, -1, false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Main menu ──────────────────────────────────────────────────────────
        private static void RegisterMainMenu(CampaignGameStarter starter)
        {
            try
            {
                starter.AddGameMenu("ashen_recruit_main", "{ASHEN_RECRUIT_MAIN_TEXT}", args =>
                {
                    try
                    {
                        MBTextManager.SetTextVariable("ASHEN_RECRUIT_MAIN_TEXT",
                            "The muster yard holds its ranks in silence — no drill, no idle talk, no need for either. "
                          + "They do not ask for coin. They ask for a body still warm enough to remember what it was. "
                          + "The higher the rank you would raise, the more that captive must already have been worth "
                          + "something before you brought them here.");
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                });
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try
            {
                starter.AddGameMenuOption("ashen_recruit_main", "ashen_recruit_leave", "Leave the muster yard",
                    args => { try { args.optionLeaveType = GameMenuOption.LeaveType.Leave; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } return true; },
                    args => { try { GameMenu.SwitchToMenu("town"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } },
                    true, -1, false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Per-rank binding options ───────────────────────────────────────────
        private static void RegisterBindOptions(CampaignGameStarter starter)
        {
            foreach (var def in AshenRecruitCatalog.All)
            {
                var captured = def;
                string optId  = $"ashen_recruit_bind_{captured.Rank}";
                string textId = $"ASHEN_RECRUIT_BIND_{captured.Rank}";

                try
                {
                    starter.AddGameMenuOption("ashen_recruit_main", optId, $"{{{textId}}}",
                        args =>
                        {
                            try
                            {
                                bool canBind = HasQualifyingPrisoners(captured, out string missing);
                                string note = canBind
                                    ? $"  [{captured.PrisonerCost} prisoner(s), tier {captured.RequiredPrisonerTier}+]"
                                    : $"  [{missing ?? "missing requirements"}]";
                                MBTextManager.SetTextVariable(textId, $"Raise an {captured.Name}{note}");
                                args.IsEnabled = canBind;
                                try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                            }
                            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                            return true;
                        },
                        args => { try { DoBindAshenTroop(captured); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } });
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        // ── Binding action ─────────────────────────────────────────────────────
        private static void DoBindAshenTroop(AshenRecruitDef def)
        {
            if (!HasQualifyingPrisoners(def, out string missing))
            {
                ShowDialog("The Muster Yard Turns You Away",
                    $"You are missing what the raising asks of you: {missing}",
                    () => { try { GameMenu.SwitchToMenu("ashen_recruit_main"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } });
                return;
            }

            ConsumeQualifyingPrisoners(def);
            GrantAshenTroop(def);

            ShowDialog($"{def.Name} raised",
                $"The prisoners are led away and do not come back the same. When the yard is quiet again, "
              + $"an {def.Name} stands where a living captive stood, and waits for its next order.",
                () => { try { GameMenu.SwitchToMenu("ashen_recruit_main"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } });
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        private static void ShowDialog(string title, string body, Action onClose)
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    title, body, true, false, "So be it.", "",
                    onClose, null));
            }
            catch
            {
                string brief = body.Length > 100 ? body.Substring(0, 100) + "…" : body;
                MBInformationManager.AddQuickInformation(new TextObject(brief));
                try { onClose?.Invoke(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }
    }
}
