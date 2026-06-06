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
        private static Kingdom          _selectedKingdom;

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

        // ── Faction filter ────────────────────────────────────────────────────
        // Step inserted between scheme selection and target selection.
        // Viper's Counsel skips this step — it always targets the player's own kingdom.
        internal static void OpenFactionFilterUI()
        {
            try
            {
                if (_selectedDef == null) return;

                // Viper's Counsel: always targets own kingdom — skip faction step
                if (_selectedDef.Type == SchemeType.VipersCounsel)
                {
                    _selectedKingdom = null;
                    OpenLordTargetUI();
                    return;
                }

                bool needsLord = _selectedDef.NeedsLord;

                // Collect factions that have at least one valid target for this scheme type
                var factions = Kingdom.All
                    .Where(k => !k.IsEliminated && k.StringId != "ashen_kingdom"
                             && (needsLord
                                 ? k.Heroes.Any(h => h.IsLord && h.IsAlive && !h.IsChild
                                                  && !h.IsPrisoner && h != Hero.MainHero)
                                 : Settlement.All.Any(s => (s.IsTown || s.IsCastle)
                                                        && s.OwnerClan?.Kingdom == k)))
                    .OrderBy(k => k.Name?.ToString() ?? "")
                    .ToList();

                if (factions.Count == 0)
                {
                    MBInformationManager.AddQuickInformation(new TextObject("No valid factions to scheme against."));
                    return;
                }

                var elements = factions.Select(k =>
                {
                    int count = needsLord
                        ? k.Heroes.Count(h => h.IsLord && h.IsAlive && !h.IsChild
                                           && !h.IsPrisoner && h != Hero.MainHero)
                        : Settlement.All.Count(s => (s.IsTown || s.IsCastle) && s.OwnerClan?.Kingdom == k);
                    string hint = needsLord
                        ? $"{count} lord{(count != 1 ? "s" : "")} available"
                        : $"{count} settlement{(count != 1 ? "s" : "")} available";
                    return new InquiryElement(k.StringId, k.Name?.ToString() ?? "?", null, true, hint);
                }).ToList();

                MBInformationManager.ShowMultiSelectionInquiry(
                    new MultiSelectionInquiryData(
                        $"Choose Faction — {_selectedDef.Name}",
                        "Select the faction to scheme against:",
                        elements, true, 1, 1,
                        "Select", "Back",
                        chosen =>
                        {
                            try
                            {
                                if (chosen == null || chosen.Count == 0) return;
                                string kid = chosen[0].Identifier as string;
                                _selectedKingdom = Kingdom.All.FirstOrDefault(k => k.StringId == kid);
                                if (_selectedKingdom == null) return;
                                if (needsLord) OpenLordTargetUI();
                                else           OpenSettlementTargetUI();
                            }
                            catch { }
                        }, null),
                    true);
            }
            catch { }
        }

        // ── Target selection: lords ───────────────────────────────────────────
        internal static void OpenLordTargetUI()
        {
            try
            {
                bool isVipers = _selectedDef.Type == SchemeType.VipersCounsel;
                var playerKingdom = Hero.MainHero?.Clan?.Kingdom;

                if (isVipers && playerKingdom == null)
                {
                    MBInformationManager.AddQuickInformation(
                        new TextObject("You must belong to a kingdom to use Viper's Counsel."));
                    return;
                }

                // Filter to the selected faction (or own kingdom for Viper's Counsel)
                Kingdom factionFilter = isVipers ? playerKingdom : _selectedKingdom;

                var lords = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && !h.IsPrisoner && !h.IsChild
                             && h != Hero.MainHero
                             && (factionFilter == null || h.Clan?.Kingdom == factionFilter))
                    .OrderBy(h => h.Clan?.Kingdom?.Name?.ToString() ?? "")
                    .ThenBy(h => h.Name?.ToString() ?? "")
                    .Take(60)
                    .ToList();

                if (lords.Count == 0)
                {
                    MBInformationManager.AddQuickInformation(new TextObject(isVipers
                        ? "No rival lords found within your kingdom."
                        : $"No valid lord targets found in {_selectedKingdom?.Name?.ToString() ?? "that faction"}."));
                    return;
                }

                var elements = lords.Select(h =>
                {
                    float ch      = SchemeSystem.ComputeSuccessChance(Hero.MainHero, _selectedDef.Type, h, null);
                    int   cost    = SchemeSystem.ComputeGoldCost(_selectedDef, h, null);
                    int   infCost = SchemeSystem.ComputeInfluenceCost(_selectedDef, h, null);
                    bool  blk     = SchemeSystem.IsHardBlocked(_selectedDef.Type, h, null);
                    bool  cd      = SchemeSystem.IsOnCooldown(_selectedDef.Type, h, null);
                    string label = $"{h.Name}  [{h.Clan?.Name}]"
                                 + (blk ? "  [BLOCKED]" : "");
                    string hint  = $"Success: {(int)(ch * 100)}%  |  Cost: {cost}g / {infCost} inf  |  Tier: {h.Clan?.Tier ?? 0}"
                                 + (cd ? "  [5× repeat penalty]" : "");
                    return new InquiryElement(h.StringId, label, null, !blk, hint);
                }).ToList();

                string factionLabel = isVipers
                    ? playerKingdom?.Name?.ToString() ?? "your kingdom"
                    : _selectedKingdom?.Name?.ToString() ?? "selected faction";
                string selectMsg = isVipers
                    ? $"Select a lord from {factionLabel} to undermine in the king's eyes:"
                    : $"Select the lord to target in {factionLabel}:";

                MBInformationManager.ShowMultiSelectionInquiry(
                    new MultiSelectionInquiryData(
                        $"Target — {_selectedDef.Name}  ({factionLabel})",
                        selectMsg,
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
                string factionLabel = _selectedKingdom?.Name?.ToString() ?? "selected faction";

                var settlements = Settlement.All
                    .Where(s => (s.IsTown || s.IsCastle)
                             && (_selectedKingdom == null || s.OwnerClan?.Kingdom == _selectedKingdom))
                    .OrderBy(s => s.Name?.ToString() ?? "")
                    .Take(60)
                    .ToList();

                if (settlements.Count == 0)
                {
                    MBInformationManager.AddQuickInformation(
                        new TextObject($"No valid settlement targets found in {factionLabel}."));
                    return;
                }

                var elements = settlements.Select(s =>
                {
                    float ch      = SchemeSystem.ComputeSuccessChance(Hero.MainHero, _selectedDef.Type, null, s);
                    int   cost    = SchemeSystem.ComputeGoldCost(_selectedDef, null, s);
                    int   infCost = SchemeSystem.ComputeInfluenceCost(_selectedDef, null, s);
                    bool  cd      = SchemeSystem.IsOnCooldown(_selectedDef.Type, null, s);
                    string label = $"{s.Name}  [{s.OwnerClan?.Name?.ToString() ?? "?"}]  "
                                 + $"Security: {(int)(s.Town?.Security ?? 0)}";
                    string hint  = $"Success: {(int)(ch * 100)}%  |  Cost: {cost}g / {infCost} inf"
                                 + (cd ? "  [5× repeat penalty]" : "");
                    return new InquiryElement(s.StringId, label, null, true, hint);
                }).ToList();

                MBInformationManager.ShowMultiSelectionInquiry(
                    new MultiSelectionInquiryData(
                        $"Target — {_selectedDef.Name}  ({factionLabel})",
                        $"Select the settlement to target in {factionLabel}:",
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

                // Minigame pass chance: 30% base + (skill / 600) × 55%, clamped [30%, 85%]
                // (Not the old NPC formula — this is what the skill check rolls against each turn.)
                int   playerSkill = Hero.MainHero?.GetSkillValue(_selectedDef.Skill) ?? 0;
                float chance      = Math.Max(0.30f, Math.Min(0.85f, 0.30f + (playerSkill / 600f) * 0.55f));
                int   goldCost    = SchemeSystem.ComputeGoldCost(_selectedDef, targetHero, targetSett);
                int   infCost    = SchemeSystem.ComputeInfluenceCost(_selectedDef, targetHero, targetSett);
                bool  onCooldown = SchemeSystem.IsOnCooldown(_selectedDef.Type, targetHero, targetSett);
                string tName     = targetHero?.Name?.ToString() ?? targetSett?.Name?.ToString() ?? "target";
                string cdNote    = onCooldown ? "\n[!] Repeat-use penalty — cost is 5× base." : "";
                bool   isAss     = _selectedDef.Type == SchemeType.Assassinate;
                bool   isVipers  = _selectedDef.Type == SchemeType.VipersCounsel;
                string traitNote = isAss
                    ? "\nPersonality: Honor −1  Calculating −1  Mercy −1  — on commit"
                    : "\nPersonality: Honor −1  Calculating −1  — on commit";
                // Minigame config for display
                int roguery       = Hero.MainHero?.GetSkillValue(DefaultSkills.Roguery) ?? 0;
                int charm         = Hero.MainHero?.GetSkillValue(DefaultSkills.Charm)   ?? 0;
                string abilityHints = "";
                if (roguery >= 150) abilityHints += "  · SLIP PAST (Roguery 150+)\n";
                if (roguery >= 300) abilityHints += "  · SCOUT AHEAD (Roguery 300+)\n";
                if (charm   >= 150) abilityHints += "  · SMOOTH IT OVER (Charm 150+)\n";
                if (charm   >= 300) abilityHints += "  · SILVER TONGUE (Charm 300+)\n";
                string abilityBlock = string.IsNullOrEmpty(abilityHints)
                    ? ""
                    : "Your abilities for this operation:\n" + abilityHints;

                string bustNote = isVipers
                    ? "On bust/fail: always exposed — relations hit with target and king, no crime rating."
                    : "On bust (score >21): agent caught — crime rating, relations hit, possible war.";
                string body = $"Scheme: {_selectedDef.Name}\n"
                            + $"Target: {tName}\n"
                            + $"Cost: {goldCost}g  +  {infCost} influence{cdNote}\n\n"
                            + $"This scheme is resolved interactively.\n"
                            + $"Each turn your agent files a field report. You choose how to respond.\n"
                            + $"Skill check pass chance: {(int)(chance * 100)}%  (Roguery {roguery} / Charm {charm})\n\n"
                            + abilityBlock
                            + traitNote + "\n\n"
                            + bustNote;

                InformationManager.ShowInquiry(
                    new InquiryData("Confirm Scheme", body, true, true, "Commit", "Cancel",
                        () => CommitScheme(targetHero, targetSett), null),
                    true);
            }
            catch { }
        }

        // Commits a player scheme:
        //   1. Validates resources (same checks QueueScheme used to do for the player).
        //   2. Deducts gold and influence immediately.
        //   3. Stamps per-target and global cooldowns BEFORE the minigame starts
        //      so a save-and-load mid-operation cannot bypass the cost.
        //   4. Applies trait shifts.
        //   5. Switches away from the game menu.
        //   6. Defers SchemeMinigame.BeginOperation() to the next tick so the
        //      menu is fully closed before the first inquiry opens.
        private static void CommitScheme(Hero targetHero, Settlement targetSett)
        {
            try
            {
                if (_selectedDef == null || Hero.MainHero == null) return;

                // ── Validate and deduct costs ─────────────────────────────────
                int goldCost = SchemeSystem.ComputeGoldCost(_selectedDef, targetHero, targetSett);
                int infCost  = SchemeSystem.ComputeInfluenceCost(_selectedDef, targetHero, targetSett);

                if (!SchemeSystem.DebugFree)
                {
                    if (Hero.MainHero.Gold < goldCost
                        || (Hero.MainHero.Clan?.Influence ?? 0f) < infCost)
                    {
                        MBInformationManager.AddQuickInformation(
                            new TextObject("Insufficient funds — the scheme cannot be arranged."));
                        _selectedDef = null;
                        return;
                    }
                    try { Hero.MainHero.Gold -= goldCost; } catch { }
                    try { if (Hero.MainHero.Clan != null) Hero.MainHero.Clan.Influence -= infCost; } catch { }
                }

                // ── Stamp cooldowns NOW, before the minigame ──────────────────
                // If the player saves mid-minigame and reloads, costs are gone.
                // The cooldowns ensure they cannot immediately retry for free.
                try { SchemeSystem.SetPerTargetCooldown(_selectedDef.Type, targetHero, targetSett); } catch { }
                try { SchemeSystem.SetPlayerCooldown(3); } catch { } // overwritten on real resolution

                // ── Personality shifts ────────────────────────────────────────
                try { ShiftPlayerTrait(DefaultTraits.Honor,      -1); } catch { }
                try { ShiftPlayerTrait(DefaultTraits.Calculating, -1); } catch { }
                if (_selectedDef.Type == SchemeType.Assassinate)
                    try { ShiftPlayerTrait(DefaultTraits.Mercy, -1); } catch { }

                // ── Close the menu, then launch the minigame ──────────────────
                // Capture scheme state before clearing the static fields.
                SchemeDefinition capturedDef  = _selectedDef;
                _selectedDef = null;

                try { GameMenu.SwitchToMenu("town"); } catch { }

                // Defer so the menu transition is fully complete before the
                // first MultiSelectionInquiry opens (matches OpenSchemMenuDeferred pattern).
                MageKnowledge._deferredInquiry = () =>
                {
                    try { SchemeMinigame.BeginOperation(capturedDef, targetHero, targetSett); } catch { }
                };
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
