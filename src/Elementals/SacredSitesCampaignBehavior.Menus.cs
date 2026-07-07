// =============================================================================
// ASH AND EMBER — Elementals/SacredSitesCampaignBehavior.Menus.cs
//
// Game menu implementation for the Forest Clans' sacred sites.
//
// Menu tree:
//   "town" → "Visit the Standing Stones"
//     → "sacred_site_main"  (description + which Kindled to bind)
//       → "sacred_site_bind_<0..5>"  (per-kind option — checks cost, rolls)
//       → "sacred_site_leave"
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
    public partial class SacredSitesCampaignBehavior
    {
        private static void RegisterSacredSiteMenus(CampaignGameStarter starter)
        {
            RegisterTownEntry(starter);
            RegisterMainMenu(starter);
            RegisterBindOptions(starter);
            RegisterStudyOption(starter);
        }

        // ── Town entry ─────────────────────────────────────────────────────────
        private static void RegisterTownEntry(CampaignGameStarter starter)
        {
            try
            {
                starter.AddGameMenuOption("town", "sacred_site_enter", "{SACRED_SITE_ENTER_TEXT}",
                    args =>
                    {
                        try
                        {
                            if (!HasSacredSite(Settlement.CurrentSettlement)) return false;
                            string note = HasElementalBond ? "  [the old ways already know you]" : "";
                            MBTextManager.SetTextVariable("SACRED_SITE_ENTER_TEXT", "Visit the Standing Stones" + note);
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                            args.IsEnabled = true;
                            return true;
                        }
                        catch { return false; }
                    },
                    args => { try { GameMenu.SwitchToMenu("sacred_site_main"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } },
                    false, -1, false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Main menu ──────────────────────────────────────────────────────────
        private static void RegisterMainMenu(CampaignGameStarter starter)
        {
            try
            {
                starter.AddGameMenu("sacred_site_main", "{SACRED_SITE_MAIN_TEXT}", args =>
                {
                    try
                    {
                        int smithing = 0;
                        try { smithing = Hero.MainHero?.GetSkillValue(DefaultSkills.Crafting) ?? 0; }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        float odds = CurrentBindingOdds();

                        string blockNote = "";
                        if (BlockedByOtherPath(out string reason))
                            blockNote = $"\n\n[{reason}]";

                        string talentNote = SacredSiteTalents.OwnedCount > 0
                            ? $"\n\n[Old ways learned: {SacredSiteTalents.OwnedCount}/3]"
                            : "";

                        MBTextManager.SetTextVariable("SACRED_SITE_MAIN_TEXT",
                            "The standing stones lean at angles no mason chose. Between them the old grove keeps its own "
                          + "weather — cold where the sun should reach, warm where the frost should bite. Binding a Kindled "
                          + "here costs gold and a smith's steady hand.\n\n"
                          + $"Binding chance: {(int)(odds * 100)} % (Smithing {smithing})."
                          + blockNote + talentNote);
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                });
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try
            {
                starter.AddGameMenuOption("sacred_site_main", "sacred_site_leave", "Leave the Standing Stones",
                    args => { try { args.optionLeaveType = GameMenuOption.LeaveType.Leave; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } return true; },
                    args => { try { GameMenu.SwitchToMenu("town"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } },
                    true, -1, false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Study option (the old ways' learnable talents) ────────────────────
        private static void RegisterStudyOption(CampaignGameStarter starter)
        {
            try
            {
                starter.AddGameMenuOption("sacred_site_main", "sacred_site_study", "{SACRED_SITE_STUDY_TEXT}",
                    args =>
                    {
                        try
                        {
                            int have = 0;
                            try { have = Hero.MainHero?.HeroDeveloper?.UnspentFocusPoints ?? 0; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                            MBTextManager.SetTextVariable("SACRED_SITE_STUDY_TEXT",
                                $"Study the old ways  [Focus: {have}] [{SacredSiteTalents.OwnedCount}/3 known]");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        return true;
                    },
                    args =>
                    {
                        try
                        {
                            if (MageKnowledge._deferredInquiry == null)
                                MageKnowledge._deferredInquiry = SacredSiteTalents.ShowCodex;
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        try { GameMenu.SwitchToMenu("sacred_site_main"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    },
                    false, -1, false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Per-kind binding options ───────────────────────────────────────────
        private static void RegisterBindOptions(CampaignGameStarter starter)
        {
            foreach (var def in SacredSiteCatalog.All)
            {
                var captured = def;
                string optId  = $"sacred_site_bind_{(int)def.Kind}";
                string textId = $"SACRED_SITE_BIND_{(int)def.Kind}";

                try
                {
                    starter.AddGameMenuOption("sacred_site_main", optId, $"{{{textId}}}",
                        args =>
                        {
                            try
                            {
                                bool blocked = BlockedByOtherPath(out string reason);
                                string missing = null;
                                bool canBind = !blocked && HasBindingMaterials(out missing);
                                string note = blocked
                                    ? $"  [{reason}]"
                                    : canBind
                                        ? $"  [{ForestClansCulture.SiteCost(SacredSiteMath.GoldCost)} denars + Iron Ore ×{SacredSiteMath.IronOreCost} + Charcoal ×{SacredSiteMath.CharcoalCost}]"
                                        : $"  [{missing ?? "missing requirements"}]";
                                MBTextManager.SetTextVariable(textId, $"Bind a {captured.Name}{note}");
                                args.IsEnabled = canBind;
                                try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                            }
                            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                            return true;
                        },
                        args => { try { DoBindKindled(captured); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } });
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        // ── Binding action ─────────────────────────────────────────────────────
        private static void DoBindKindled(SacredSiteDef def)
        {
            if (BlockedByOtherPath(out string reason))
            {
                ShowDialog("The Old Ways Refuse You", reason,
                    () => { try { GameMenu.SwitchToMenu("sacred_site_main"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } });
                return;
            }
            if (!HasBindingMaterials(out string missing))
            {
                ShowDialog("The Stones Are Silent",
                    $"You are missing what the working asks of you: {missing}",
                    () => { try { GameMenu.SwitchToMenu("sacred_site_main"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } });
                return;
            }

            ConsumeBindingMaterials();
            bool success = RollBinding();

            if (!success)
            {
                bool refunded = SacredSiteTalents.RefundsOnFailure;
                if (refunded) RefundBindingMaterials();

                string failNote = refunded
                    ? "\n\n[Iron Ore and Charcoal returned — Sparing Rite.]"
                    : "\n\n[All materials consumed.]";
                ShowDialog("The Binding Fails",
                    "The stones stay stones a moment longer. Whatever answers here does not always choose to."
                  + failNote,
                    () => { try { GameMenu.SwitchToMenu("sacred_site_main"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } });
                return;
            }

            GrantKindled(def);
            ShowDialog($"{def.Name} bound",
                $"The old grove answers. Something rises where nothing stood, and it turns, waiting, to march at your word. "
              + $"A {def.Name} joins your company.\n\n"
              + def.Lore,
                () => { try { GameMenu.SwitchToMenu("sacred_site_main"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } });
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
