// =============================================================================
// ASH AND EMBER — SchemeCampaignBehavior.Menus.cs
// Session resume, tavernkeeper dialogue, and scheme menus.
// Partial of SchemeCampaignBehavior (shared state lives in SchemeCampaignBehavior.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class SchemeCampaignBehavior
    {
        // ── Session launched ──────────────────────────────────────────────────
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            RegisterDialogue(starter);
            RegisterSchemeMenus(starter);
            ResumeUnresolvedOperation();
        }

        // A committed operation whose Gambit never resolved (save/load mid-game)
        // is re-launched — the player already paid for it.
        private static void ResumeUnresolvedOperation()
        {
            try
            {
                if (!SchemeSystem.TryGetPendingPlayerOperation(out var def, out Hero hero, out Settlement sett))
                    return;
                MageKnowledge._deferredInquiry = () =>
                {
                    try
                    {
                        MBInformationManager.AddQuickInformation(new TextObject(
                            $"Your operative is still in the field — the {def.Name} operation resumes."));
                        SchemeMinigame.Begin(def, hero, sett);
                    }
                    catch { }
                };
            }
            catch { }
        }

        // ── Tavernkeeper dialogue ─────────────────────────────────────────────
        private static void RegisterDialogue(CampaignGameStarter starter)
        {
            const int P = 95;
            try
            {
                starter.AddPlayerLine(
                    "ldm_scheme_open", "tavernkeeper_talk", "ldm_scheme_response",
                    "I have some shadier business that needs arranging.",
                    CondSchemeAvailable, null, P);
            }
            catch { }
            try
            {
                starter.AddDialogLine(
                    "ldm_scheme_npc", "ldm_scheme_response", "close_window",
                    "Coin spent here buys silence. Find the usual spot in the square.",
                    null, OpenSchemMenuDeferred, P);
            }
            catch { }
        }

        private static bool CondSchemeAvailable()
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

        // Deferred so the dialogue window fully closes before the menu opens.
        private static void OpenSchemMenuDeferred()
        {
            try
            {
                MageKnowledge._deferredInquiry = () =>
                {
                    try { GameMenu.SwitchToMenu("ldm_scheme_menu"); } catch { }
                };
            }
            catch { }
        }

        // ── Scheme game menus ─────────────────────────────────────────────────
        private static void RegisterSchemeMenus(CampaignGameStarter starter)
        {
            // ── Entry point in the town menu ──────────────────────────────────
            try
            {
                starter.AddGameMenuOption(
                    "town", "ldm_scheme_city",
                    "Discuss some shadier business...",
                    args =>
                    {
                        try
                        {
                            var s = Settlement.CurrentSettlement;
                            if (s == null || !s.IsTown) return false;
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; } catch { }

                            // Disable while a queued NPC-era scheme is still in flight
                            bool pending = false;
                            try { pending = SchemeSystem.PlayerHasPendingScheme(); } catch { }

                            // Disable while the post-operation global cooldown is active
                            bool onCooldown = false;
                            int  cdDays     = 0;
                            try { onCooldown = SchemeSystem.PlayerOnGlobalCooldown;
                                  cdDays     = SchemeSystem.PlayerGlobalCooldownDays; } catch { }

                            args.IsEnabled = !pending && !onCooldown;
                            if (pending)
                                try { args.Tooltip = new TextObject("A scheme is already in motion."); } catch { }
                            else if (onCooldown)
                                try { args.Tooltip = new TextObject($"Network cooling down — {cdDays} day{(cdDays != 1 ? "s" : "")} before the next operation."); } catch { }
                            return true;
                        }
                        catch { return false; }
                    },
                    args => { try { GameMenu.SwitchToMenu("ldm_scheme_menu"); } catch { } });
            }
            catch { }

            // ── Scheme selection menu ─────────────────────────────────────────
            try
            {
                starter.AddGameMenu(
                    "ldm_scheme_menu",
                    "{LDM_SCHEME_HDR}",
                    args =>
                    {
                        try
                        {
                            int gold = Hero.MainHero?.Gold ?? 0;
                            int inf  = (int)(Hero.MainHero?.Clan?.Influence ?? 0f);
                            MBTextManager.SetTextVariable("LDM_SCHEME_HDR",
                                $"The tavernkeeper leans forward. Name the work.\nYour resources: {gold}g  |  {inf} influence");
                        }
                        catch { }
                    });
            }
            catch { }

            // ── One option per scheme — option IDs use letters only (no digits) ─
            // Pattern mirrors Sanctuary: always return true (show all), grey when
            // player can't afford the base cost, guard consequence against edge cases.
            // Costs shown here are base (tier-0) minimums; the target picker shows
            // the actual scaled cost per target (1×–3.4× depending on clan tier).
            try
            {
                foreach (var def in SchemeSystem.Definitions)
                {
                    SchemeDefinition captured = def;
                    // Option ID: letters + underscores only — digits in IDs fail in this BL version.
                    string optionId = "ldm_sc_" + captured.Type.ToString().ToLowerInvariant();
                    // Text-variable key: same restriction, all-caps + underscores.
                    string textKey  = "LDM_SC_" + captured.Type.ToString().ToUpperInvariant();
                    try
                    {
                        starter.AddGameMenuOption(
                            "ldm_scheme_menu",
                            optionId,
                            "{" + textKey + "}",
                            args =>
                            {
                                try
                                {
                                    int  playerGold = Hero.MainHero?.Gold ?? 0;
                                    int  playerInf  = (int)(Hero.MainHero?.Clan?.Influence ?? 0f);
                                    bool canAfford  = playerGold >= captured.GoldCost
                                                   && playerInf  >= captured.InfluenceCost;
                                    string label = captured.Name
                                        + $"  —  from {captured.GoldCost}g / from {captured.InfluenceCost} inf"
                                        + (canAfford ? "" : "  [Insufficient funds]");
                                    MBTextManager.SetTextVariable(textKey, label);
                                    try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                                    args.IsEnabled = canAfford;
                                    try { args.Tooltip = new TextObject(captured.Description + "\nFinal cost scales with target clan tier."); } catch { }
                                }
                                catch { }
                                return true;
                            },
                            args =>
                            {
                                if ((Hero.MainHero?.Gold ?? 0) < captured.GoldCost) return;
                                _selectedDef     = captured;
                                _selectedKingdom = null;
                                OpenFactionFilterUI();
                            });
                    }
                    catch { }
                }
            }
            catch { }

            // ── Counter-intelligence sweep ──────────────────────────────────────
            // Active defence against NPC schemes targeting the player or their
            // fiefs. A paid probe: finds nothing if no plot exists; if one does,
            // a Roguery check decides whether it is rooted out and its author named.
            try
            {
                starter.AddGameMenuOption(
                    "ldm_scheme_menu", "ldm_scheme_sweep",
                    "{LDM_SCHEME_SWEEP}",
                    args =>
                    {
                        try
                        {
                            int roguery = Hero.MainHero?.GetSkillValue(DefaultSkills.Roguery) ?? 0;
                            int pct = (int)(SweepSuccessChance(roguery) * 100f);
                            MBTextManager.SetTextVariable("LDM_SCHEME_SWEEP",
                                $"Sweep the city for hostile agents  —  {SweepCostGold}g  [{pct}% Roguery]");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                            args.IsEnabled = (Hero.MainHero?.Gold ?? 0) >= SweepCostGold;
                            try { args.Tooltip = new TextObject(
                                "Pay informants to comb the underworld for plots against you or your fiefs. "
                                + "If a scheme is in motion, a successful sweep cancels it and names its author. "
                                + "If nothing is in motion, the coin buys only rumours."); } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => RunCounterIntelSweep());
            }
            catch { }

            // ── Leave ──────────────────────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption(
                    "ldm_scheme_menu", "ldm_scheme_leave",
                    "Think better of it.",
                    args =>
                    {
                        try { args.optionLeaveType = GameMenuOption.LeaveType.Leave; } catch { }
                        return true;
                    },
                    args => { try { GameMenu.SwitchToMenu("town"); } catch { } },
                    true);
            }
            catch { }
        }

    }
}
