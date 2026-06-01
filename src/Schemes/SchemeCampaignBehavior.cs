// =============================================================================
// ASH AND EMBER — Schemes/SchemeCampaignBehavior.cs
// Campaign behaviour: tavern-keeper dialogue hook, player UI flow,
// NPC AI daily tick, pending-queue tick, and save/load.
//
// Dialogue flow:
//   tavernkeeper_talk
//     → player: "I have some shadier business to arrange."
//       → NPC: "Coin spent here buys silence. Name what you need."
//         → close_window + consequence = OpenSchemeSelectionUI()
//
// UI flow (MultiSelectionInquiry chain):
//   1. Choose scheme type
//   2. Choose target (lord list or settlement list, depending on scheme)
//   3. Confirm (shows success %, cost, delay)
//   → QueueScheme()
//
// The MultiSelectionInquiry is wrapped entirely in try/catch so that if the
// API has changed in a given Bannerlord version, nothing crashes — the player
// simply won't see the UI for that version.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public class SchemeCampaignBehavior : CampaignBehaviorBase
    {
        // ── State held during the multi-step inquiry chain ────────────────────
        private static SchemeDefinition _selectedDef;

        // ── CampaignBehaviorBase ──────────────────────────────────────────────
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore store)
        {
            try { SchemeSystem.Save(store); } catch { }
        }

        // ── Session launched: register dialogue + city-menu option ───────────────
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            RegisterDialogue(starter);
            RegisterCityMenuOption(starter);
        }

        private static void RegisterCityMenuOption(CampaignGameStarter starter)
        {
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
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                            // Guard SchemeSystem access separately — a static-init failure there
                            // must not prevent the option from appearing.
                            bool pending = false;
                            try { pending = SchemeSystem.PlayerHasPendingScheme(); } catch { }
                            args.IsEnabled = !pending;
                            if (pending)
                                try { args.Tooltip = new TextObject("A scheme is already in motion."); } catch { }
                            return true;
                        }
                        catch { return false; }
                    },
                    args =>
                    {
                        try { GameMenu.ExitToLast(); } catch { }
                        MageKnowledge._deferredInquiry = OpenSchemeSelectionUI;
                    });
            }
            catch { }
        }

        private static void RegisterDialogue(CampaignGameStarter starter)
        {
            const int P = 95; // below Ashen-dialogue priority (200) but above vanilla (100)

            // Player line shown during tavern-keeper conversation
            try
            {
                starter.AddPlayerLine(
                    "ldm_scheme_open",
                    "tavernkeeper_talk",
                    "ldm_scheme_response",
                    "I have some shadier business that needs arranging.",
                    CondSchemeAvailable,
                    null,
                    P);
            }
            catch { }

            try
            {
                starter.AddDialogLine(
                    "ldm_scheme_npc",
                    "ldm_scheme_response",
                    "close_window",
                    "Coin spent here buys silence. Name what you need done.",
                    null,
                    OpenSchemeSelectionUI,
                    P);
            }
            catch { }
        }

        private static bool CondSchemeAvailable()
        {
            try
            {
                // Tavernkeepers are generic NPCs, not Hero objects — use CharacterObject directly.
                var npc = CharacterObject.OneToOneConversationCharacter;
                if (npc?.Occupation != Occupation.Tavernkeeper) return false;
                if (Hero.MainHero?.CurrentSettlement?.IsTown != true) return false;
                // Must afford at least the cheapest scheme (Spread Rumors: 300g)
                if (Hero.MainHero.Gold < 300) return false;
                return true;
            }
            catch { return false; }
        }

        // ── Daily tick ────────────────────────────────────────────────────────
        private static void OnDailyTick()
        {
            try { SchemeSystem.DailyTick();     } catch { }
            try { SchemeSystem.NpcSchemeTick(); } catch { }
        }

        // ── UI: step 1 — choose scheme ────────────────────────────────────────
        internal static void OpenSchemeSelectionUI()
        {
            try
            {
                // One pending scheme at a time — prevents spamming and keeps balance
                // with campaign map spells (both draw on the same finite resources).
                if (SchemeSystem.PlayerHasPendingScheme())
                {
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "A scheme is already in motion. Wait for it to resolve before arranging another."));
                    return;
                }

                var elements = new List<InquiryElement>();
                int affordableCount = 0;
                int playerInfluence = (int)(Hero.MainHero.Clan?.Influence ?? 0);
                foreach (var def in SchemeSystem.Definitions)
                {
                    float chance   = SchemeSystem.ComputeSuccessChance(Hero.MainHero, def.Type, null, null);
                    int   baseCost = SchemeSystem.ComputeGoldCost(def, null, null, ignoreCooldown: true);
                    bool  hardBlock = SchemeSystem.IsHardBlocked(def.Type, null, null);
                    string cooldownNote = hardBlock ? "  [BLOCKED — retry cooldown active]"
                                       : "";
                    bool   isAss   = def.Type == SchemeType.Assassinate;
                    string traits  = isAss ? "Honor −1 (Dishonorable), Calculating −1 (Devious), Mercy −1 (Merciless) — on commit"
                                           : "Honor −1 (Dishonorable), Calculating −1 (Devious) — on commit";
                    string hint = $"{def.Description}\n" +
                                  $"Base cost: from {baseCost}g (scales with target tier; 5× if repeated within 7 days){cooldownNote}\n" +
                                  $"Influence: {def.InfluenceCost}  |  " +
                                  $"Est. success (no target): {(int)(chance * 100)}%  |  Delay: 1-3 days\n" +
                                  $"Personality cost: {traits}\n" +
                                  $"Failure: 70% silent / 30% exposed — crime rating, relations hit, possible war.";
                    bool canAfford = !hardBlock
                                  && Hero.MainHero.Gold >= baseCost
                                  && playerInfluence >= def.InfluenceCost;
                    if (canAfford) affordableCount++;
                    string label = def.Name + (canAfford ? "" : hardBlock ? " [blocked]" : " (can't afford)");
                    elements.Add(new InquiryElement(def.Type, label, null, canAfford, hint));
                }

                string desc = affordableCount > 0
                    ? "Choose what needs to be arranged:"
                    : $"You need influence to arrange schemes (you have {playerInfluence}). " +
                      $"Gain influence through battles and quests. Browse available options below.";

                // minSelectableOptionCount=0 so the list always opens even when everything is unaffordable.
                MBInformationManager.ShowMultiSelectionInquiry(
                    new MultiSelectionInquiryData(
                        "Schemes",
                        desc,
                        elements,
                        true, 0, 1,
                        "Select", "Cancel",
                        OnSchemeTypeChosen, null),
                    true);
            }
            catch { }
        }

        private static void OnSchemeTypeChosen(List<InquiryElement> selected)
        {
            try
            {
                if (selected == null || selected.Count == 0) return;
                var type = (SchemeType)selected[0].Identifier;
                _selectedDef = SchemeSystem.GetDefinition(type);
                if (_selectedDef == null) return;

                if (_selectedDef.NeedsLord)   OpenLordTargetUI();
                else                           OpenSettlementTargetUI();
            }
            catch { }
        }

        // ── UI: step 2a — choose lord target ─────────────────────────────────
        private static void OpenLordTargetUI()
        {
            try
            {
                var lords = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && !h.IsPrisoner && !h.IsChild
                             && h != Hero.MainHero)
                    .OrderBy(h => h.Clan?.Kingdom?.Name?.ToString() ?? "")
                    .ThenBy(h => h.Name?.ToString() ?? "")
                    .Take(60) // cap list size for UI performance
                    .ToList();

                if (lords.Count == 0)
                {
                    MBInformationManager.AddQuickInformation(new TextObject("No valid lord targets found."));
                    return;
                }

                var elements = lords.Select(h =>
                {
                    float ch   = SchemeSystem.ComputeSuccessChance(Hero.MainHero, _selectedDef.Type, h, null);
                    int   cost = SchemeSystem.ComputeGoldCost(_selectedDef, h, null);
                    bool  cd   = SchemeSystem.IsOnCooldown(_selectedDef.Type, h, null);
                    bool  blk  = SchemeSystem.IsHardBlocked(_selectedDef.Type, h, null);
                    string label = $"{h.Name}  [{h.Clan?.Name} / {h.Clan?.Kingdom?.Name?.ToString() ?? "landless"}]" +
                                   (blk ? "  [BLOCKED]" : "");
                    string hint  = $"Success: {(int)(ch * 100)}%  |  Cost: {cost}g  |  Tier: {h.Clan?.Tier ?? 0}" +
                                   (cd ? "  [5× repeat penalty]" : "");
                    return new InquiryElement(h.StringId, label, null, !blk, hint);
                }).ToList();

                MBInformationManager.ShowMultiSelectionInquiry(
                    new MultiSelectionInquiryData(
                        $"Target — {_selectedDef.Name}",
                        "Select the lord to target:",
                        elements,
                        true, 1, 1,
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

        // ── UI: step 2b — choose settlement target ────────────────────────────
        private static void OpenSettlementTargetUI()
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
                    float ch   = SchemeSystem.ComputeSuccessChance(Hero.MainHero, _selectedDef.Type, null, s);
                    int   cost = SchemeSystem.ComputeGoldCost(_selectedDef, null, s);
                    bool  cd   = SchemeSystem.IsOnCooldown(_selectedDef.Type, null, s);
                    string label = $"{s.Name}  [{s.OwnerClan?.Name?.ToString() ?? "?"} / {s.OwnerClan?.Kingdom?.Name?.ToString() ?? "?"}]  " +
                                   $"Security: {(int)(s.Town?.Security ?? 0)}";
                    string hint  = $"Success: {(int)(ch * 100)}%  |  Cost: {cost}g" + (cd ? "  [5× repeat penalty]" : "");
                    return new InquiryElement(s.StringId, label, null, true, hint);
                }).ToList();

                MBInformationManager.ShowMultiSelectionInquiry(
                    new MultiSelectionInquiryData(
                        $"Target — {_selectedDef.Name}",
                        "Select the settlement to target:",
                        elements,
                        true, 1, 1,
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

        // ── UI: step 3 — confirmation ─────────────────────────────────────────
        private static void ShowConfirmation(Hero targetHero, Settlement targetSett)
        {
            try
            {
                if (_selectedDef == null) return;

                float chance    = SchemeSystem.ComputeSuccessChance(
                    Hero.MainHero, _selectedDef.Type, targetHero, targetSett);
                int   goldCost  = SchemeSystem.ComputeGoldCost(_selectedDef, targetHero, targetSett);
                bool  onCooldown = SchemeSystem.IsOnCooldown(_selectedDef.Type, targetHero, targetSett);
                string tName    = targetHero?.Name?.ToString() ?? targetSett?.Name?.ToString() ?? "target";
                string cooldownNote = onCooldown ? "\n[!] Repeat-use penalty active — cost is 5× base." : "";
                bool   isAssassinate = _selectedDef.Type == SchemeType.Assassinate;
                string traitNote = isAssassinate
                    ? "\nPersonality: Honor −1 (Dishonorable)  +  Calculating −1 (Devious)  +  Mercy −1 (Merciless) — paid on commit."
                    : "\nPersonality: Honor −1 (Dishonorable)  +  Calculating −1 (Devious) — paid on commit.";
                string body   = $"Scheme: {_selectedDef.Name}\n" +
                                $"Target: {tName}\n" +
                                $"Cost: {goldCost} gold  +  {_selectedDef.InfluenceCost} influence{cooldownNote}\n" +
                                $"Success chance: {(int)(chance * 100)}%\n" +
                                $"Execution delay: 1-3 days\n" +
                                traitNote + "\n\n" +
                                $"On failure (30% chance of exposure): crime rating, relations, possible war.";

                InformationManager.ShowInquiry(
                    new InquiryData(
                        "Confirm Scheme",
                        body,
                        true, true,
                        "Commit", "Cancel",
                        () => CommitScheme(targetHero, targetSett),
                        null),
                    true);
            }
            catch { }
        }

        private static void CommitScheme(Hero targetHero, Settlement targetSett)
        {
            try
            {
                if (_selectedDef == null) return;

                bool ok = SchemeSystem.QueueScheme(
                    Hero.MainHero, _selectedDef.Type, targetHero, targetSett, isPlayer: true);

                if (ok)
                {
                    // Personality cost: scheming is inherently dishonorable and devious
                    try { ShiftPlayerTrait(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultTraits.Honor, -1); } catch { }
                    try { ShiftPlayerTrait(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultTraits.Calculating, -1); } catch { }
                    // Assassination additionally marks the player as merciless
                    if (_selectedDef.Type == SchemeType.Assassinate)
                        try { ShiftPlayerTrait(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultTraits.Mercy, -1); } catch { }

                    MBInformationManager.AddQuickInformation(
                        new TextObject("Scheme arranged. Results in 1-3 days."));
                }
                else
                {
                    MBInformationManager.AddQuickInformation(
                        new TextObject("You cannot arrange this scheme right now — blocked, in flight, or insufficient funds."));
                }

                _selectedDef = null;
            }
            catch { }
        }

        // Shifts the player's personality trait by delta, clamped to [-2, 2].
        private static void ShiftPlayerTrait(
            TaleWorlds.CampaignSystem.CharacterDevelopment.TraitObject trait, int delta)
        {
            var hero = Hero.MainHero;
            if (hero == null) return;
            int current = hero.GetTraitLevel(trait);
            hero.SetTraitLevel(trait, Math.Min(2, Math.Max(-2, current + delta)));
        }
    }
}
