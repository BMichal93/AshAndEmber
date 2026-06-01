// =============================================================================
// ASH AND EMBER — Schemes/SchemeCampaignBehavior.cs
// Campaign behaviour: tavern-keeper dialogue hook, scheme game menu,
// NPC AI daily tick, pending-queue tick, and save/load.
//
// UI flow (game menu approach, matching Sanctuary pattern):
//   Town menu → "Discuss some shadier business..."
//     → "ldm_scheme_menu" game menu (one option per scheme type)
//       → MultiSelectionInquiry for target (lord or settlement)
//         → ShowInquiry confirmation
//           → QueueScheme()
//
// Tavernkeeper dialogue ("I have some shadier business...") defers a
// SwitchToMenu("ldm_scheme_menu") so it opens on the next clean tick.
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
    public class SchemeCampaignBehavior : CampaignBehaviorBase
    {
        private static SchemeDefinition _selectedDef;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore store)
        {
            try { SchemeSystem.Save(store); } catch { }
        }

        // ── Session launched ──────────────────────────────────────────────────
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            RegisterDialogue(starter);
            RegisterSchemeMenus(starter);
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
                            bool pending = false;
                            try { pending = SchemeSystem.PlayerHasPendingScheme(); } catch { }
                            args.IsEnabled = !pending;
                            if (pending)
                                try { args.Tooltip = new TextObject("A scheme is already in motion."); } catch { }
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
                                _selectedDef = captured;
                                if (captured.NeedsLord) OpenLordTargetUI();
                                else                    OpenSettlementTargetUI();
                            });
                    }
                    catch { }
                }
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

        // ── Daily tick ────────────────────────────────────────────────────────
        private static void OnDailyTick()
        {
            try { SchemeSystem.DailyTick();     } catch { }
            try { SchemeSystem.NpcSchemeTick(); } catch { }
        }

        // ── Target selection: lords ───────────────────────────────────────────
        internal static void OpenLordTargetUI()
        {
            try
            {
                var lords = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && !h.IsPrisoner && !h.IsChild
                             && h != Hero.MainHero)
                    .OrderBy(h => h.Clan?.Kingdom?.Name?.ToString() ?? "")
                    .ThenBy(h => h.Name?.ToString() ?? "")
                    .Take(60)
                    .ToList();

                if (lords.Count == 0)
                {
                    MBInformationManager.AddQuickInformation(new TextObject("No valid lord targets found."));
                    return;
                }

                var elements = lords.Select(h =>
                {
                    float ch      = SchemeSystem.ComputeSuccessChance(Hero.MainHero, _selectedDef.Type, h, null);
                    int   cost    = SchemeSystem.ComputeGoldCost(_selectedDef, h, null);
                    int   infCost = SchemeSystem.ComputeInfluenceCost(_selectedDef, h, null);
                    bool  blk     = SchemeSystem.IsHardBlocked(_selectedDef.Type, h, null);
                    bool  cd      = SchemeSystem.IsOnCooldown(_selectedDef.Type, h, null);
                    string label = $"{h.Name}  [{h.Clan?.Name} / {h.Clan?.Kingdom?.Name?.ToString() ?? "landless"}]"
                                 + (blk ? "  [BLOCKED]" : "");
                    string hint  = $"Success: {(int)(ch * 100)}%  |  Cost: {cost}g / {infCost} inf  |  Tier: {h.Clan?.Tier ?? 0}"
                                 + (cd ? "  [5× repeat penalty]" : "");
                    return new InquiryElement(h.StringId, label, null, !blk, hint);
                }).ToList();

                MBInformationManager.ShowMultiSelectionInquiry(
                    new MultiSelectionInquiryData(
                        $"Target — {_selectedDef.Name}",
                        "Select the lord to target:",
                        elements, true, 1, 1,
                        "Confirm", "Back",
                        OnLordTargetChosen, null),
                    true);
            }
            catch { }
        }

        private static void OnLordTargetChosen(List<InquiryElement> selected)
        {
            try
            {
                if (selected == null || selected.Count == 0) return;
                string heroId = selected[0].Identifier as string;
                Hero target = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == heroId);
                if (target == null) return;
                ShowConfirmation(target, null);
            }
            catch { }
        }

        // ── Target selection: settlements ─────────────────────────────────────
        internal static void OpenSettlementTargetUI()
        {
            try
            {
                var settlements = Settlement.All
                    .Where(s => s.IsTown || s.IsCastle)
                    .OrderBy(s => s.OwnerClan?.Kingdom?.Name?.ToString() ?? "")
                    .ThenBy(s => s.Name?.ToString() ?? "")
                    .Take(60)
                    .ToList();

                if (settlements.Count == 0)
                {
                    MBInformationManager.AddQuickInformation(new TextObject("No valid settlement targets found."));
                    return;
                }

                var elements = settlements.Select(s =>
                {
                    float ch      = SchemeSystem.ComputeSuccessChance(Hero.MainHero, _selectedDef.Type, null, s);
                    int   cost    = SchemeSystem.ComputeGoldCost(_selectedDef, null, s);
                    int   infCost = SchemeSystem.ComputeInfluenceCost(_selectedDef, null, s);
                    bool  cd      = SchemeSystem.IsOnCooldown(_selectedDef.Type, null, s);
                    string label = $"{s.Name}  [{s.OwnerClan?.Name?.ToString() ?? "?"} / {s.OwnerClan?.Kingdom?.Name?.ToString() ?? "?"}]  "
                                 + $"Security: {(int)(s.Town?.Security ?? 0)}";
                    string hint  = $"Success: {(int)(ch * 100)}%  |  Cost: {cost}g / {infCost} inf"
                                 + (cd ? "  [5× repeat penalty]" : "");
                    return new InquiryElement(s.StringId, label, null, true, hint);
                }).ToList();

                MBInformationManager.ShowMultiSelectionInquiry(
                    new MultiSelectionInquiryData(
                        $"Target — {_selectedDef.Name}",
                        "Select the settlement to target:",
                        elements, true, 1, 1,
                        "Confirm", "Back",
                        OnSettlementTargetChosen, null),
                    true);
            }
            catch { }
        }

        private static void OnSettlementTargetChosen(List<InquiryElement> selected)
        {
            try
            {
                if (selected == null || selected.Count == 0) return;
                string settId = selected[0].Identifier as string;
                Settlement target = Settlement.All.FirstOrDefault(s => s.StringId == settId);
                if (target == null) return;
                ShowConfirmation(null, target);
            }
            catch { }
        }

        // ── Confirmation ──────────────────────────────────────────────────────
        private static void ShowConfirmation(Hero targetHero, Settlement targetSett)
        {
            try
            {
                if (_selectedDef == null) return;

                float chance     = SchemeSystem.ComputeSuccessChance(Hero.MainHero, _selectedDef.Type, targetHero, targetSett);
                int   goldCost   = SchemeSystem.ComputeGoldCost(_selectedDef, targetHero, targetSett);
                int   infCost    = SchemeSystem.ComputeInfluenceCost(_selectedDef, targetHero, targetSett);
                bool  onCooldown = SchemeSystem.IsOnCooldown(_selectedDef.Type, targetHero, targetSett);
                string tName     = targetHero?.Name?.ToString() ?? targetSett?.Name?.ToString() ?? "target";
                string cdNote    = onCooldown ? "\n[!] Repeat-use penalty — cost is 5× base." : "";
                bool   isAss     = _selectedDef.Type == SchemeType.Assassinate;
                string traitNote = isAss
                    ? "\nPersonality: Honor −1  Calculating −1  Mercy −1  — on commit"
                    : "\nPersonality: Honor −1  Calculating −1  — on commit";
                string body = $"Scheme: {_selectedDef.Name}\n"
                            + $"Target: {tName}\n"
                            + $"Cost: {goldCost}g  +  {infCost} influence{cdNote}\n"
                            + $"Success: {(int)(chance * 100)}%  |  Delay: 1–3 days\n"
                            + traitNote + "\n\n"
                            + "On failure (30% exposed): crime rating, relations hit, possible war.";

                InformationManager.ShowInquiry(
                    new InquiryData("Confirm Scheme", body, true, true, "Commit", "Cancel",
                        () => CommitScheme(targetHero, targetSett), null),
                    true);
            }
            catch { }
        }

        private static void CommitScheme(Hero targetHero, Settlement targetSett)
        {
            try
            {
                if (_selectedDef == null) return;

                bool ok = SchemeSystem.QueueScheme(Hero.MainHero, _selectedDef.Type, targetHero, targetSett, isPlayer: true);

                if (ok)
                {
                    try { ShiftPlayerTrait(DefaultTraits.Honor,      -1); } catch { }
                    try { ShiftPlayerTrait(DefaultTraits.Calculating, -1); } catch { }
                    if (_selectedDef.Type == SchemeType.Assassinate)
                        try { ShiftPlayerTrait(DefaultTraits.Mercy, -1); } catch { }

                    MBInformationManager.AddQuickInformation(new TextObject("Scheme arranged. Results in 1–3 days."));
                }
                else
                {
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "You cannot arrange this scheme right now — blocked, in flight, or insufficient funds."));
                }

                _selectedDef = null;
                try { GameMenu.SwitchToMenu("town"); } catch { }
            }
            catch { }
        }

        private static void ShiftPlayerTrait(TraitObject trait, int delta)
        {
            var hero = Hero.MainHero;
            if (hero == null) return;
            int cur = hero.GetTraitLevel(trait);
            hero.SetTraitLevel(trait, Math.Min(2, Math.Max(-2, cur + delta)));
        }
    }
}
