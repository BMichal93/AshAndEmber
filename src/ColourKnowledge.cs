// =============================================================================
// LIFE & DEATH MAGIC — MageKnowledge.cs  (ColourKnowledge.cs)
// Tracks whether the player is a mage, manages the spellbook UI,
// and provides the talent Learning tab.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace ColoursOfCalradia
{
    public static class MageKnowledge
    {
        private static bool   _isMage            = false;
        private static Action _deferredInquiry   = null;
        private static readonly HashSet<string> _giftedChildIds = new HashSet<string>();

        public static bool IsMage         => _isMage;
        // Backward-compat shims used by old call sites
        public static bool HasAnySchool   => _isMage;
        public static IEnumerable<ColorSchool> AllSchools => System.Array.Empty<ColorSchool>();
        public static bool HasSchool(ColorSchool s) => false;
        public static int  GetMadnessOrderChance() => 0;
        public static bool ReducePurpleFertility() => false;
        public static float PurpleFertilityLevel   => 1f;

        public static void SetMage(bool value) { _isMage = value; }

        public static void ResetForNewGame()
        {
            _isMage          = false;
            _deferredInquiry = null;
            _giftedChildIds.Clear();
            TalentSystem.ResetForNewGame();
        }

        public static bool IsChildGifted(string id) => _giftedChildIds.Contains(id);
        public static void AddGiftedChild(string id) => _giftedChildIds.Add(id);

        public static void FlushDeferredInquiry()
        {
            Action pending = _deferredInquiry;
            _deferredInquiry = null;
            pending?.Invoke();
        }

        // Legacy no-op kept for CampaignBehavior references
        public static void AddSchool(ColorSchool s) { }
        public static void ClearAllSchools() { }
        public static void RecordCast(ColorSchool s) { }

        // ── Spellbook / Grimoire ──────────────────────────────────────────────

        public static void ShowGrimoire(bool inMission, bool usingController)
        {
            if (!_isMage)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "The current does not stir.",
                    Color.FromUint(0xFFAAAAAA)));
                return;
            }

            string inputHint = usingController
                ? "Hold LB + left stick (↑/←/→/↓), press L3 to Break. Release LB to cast. LB+RB to open spellbook."
                : "Hold Left Alt + W/A/D/S, press E to Break, release to cast. Alt+B opens spellbook.";

            string desc =
                $"{inputHint}\n\n" +
                "Casting\n" +
                "  Form keys → Break (E/L3) → effect keys → release focus.\n" +
                "  Mixed form inputs fumble. Effects stack.\n\n" +
                "Forms  (before Break)\n" +
                "  ↑  Blast   — forward cone, 2m per ↑\n" +
                "  ←  Aura    — expanding cloud, +1 node per ←\n" +
                "  →  Barrier — wall of nodes, 1 per →\n" +
                "  ↓  Burst   — circle around self, 2m radius per ↓\n\n" +
                "Effects  (after Break)\n" +
                "  ↑  Vitality   — 5 damage per ↑\n" +
                "  ←  Force      — 2m push per ←\n" +
                "  →  Will-Break — 3 morale lost per →\n" +
                "  ↓  Reverse    — flips all effects\n\n" +
                "Combined colours\n" +
                "  Vitality+Will-Break = Orange  |  Force+Vitality = Purple  |  Will-Break+Force = Green\n\n" +
                "Aging cost  (form inputs + effect inputs)\n" +
                "  Below 4 — free  |  4–5 = 1 day  |  6–7 = 2 days  |  8–9 = 3 days\n" +
                (TalentSystem.Has(TalentId.BattleMage) ? "  [Tempered] Threshold raised to 5.\n" : "") +
                "\nExample\n" +
                "  ↑↑↑  Break  ↑↑↑↑↑  =  Blast (6m), 25 damage, 2 days.";

            if (!inMission)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "The Current",
                    desc,
                    true, true,
                    "Cast", "Talents",
                    () => { _deferredInquiry = ShowCampaignCastMenu; },
                    () => { _deferredInquiry = ShowTalentMenu; }
                ), true, true);
            }
            else
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "The Current",
                    desc,
                    true, true,
                    "Close", "Talents",
                    () => { },
                    () => { _deferredInquiry = () => ShowTalentMenu(); }
                ), true, true);
            }
        }

        // ── Campaign cast menu ────────────────────────────────────────────────

        internal static void ShowCampaignCastMenu()
        {
            if (Hero.MainHero?.IsPrisoner == true)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "You are bound. The current cannot reach you.",
                    Color.FromUint(0xFFAAAAAA)));
                return;
            }

            var spells = TalentSystem.All
                .Where(d => d.IsSpell && TalentSystem.Has(d.Id))
                .ToList();

            if (spells.Count == 0)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "No workings known. Learn spell talents first.",
                    Color.FromUint(0xFFAAAAAA)));
                return;
            }

            var elements = spells.Select(d => new InquiryElement(
                (int)d.Id,
                d.Name,
                null, true,
                $"{d.MechanicDesc}\n\n{d.Lore}"
            )).ToList();

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "Cast",
                "Choose a working. Each costs 1 day. Resonance may spare you once in four.",
                elements,
                true, 1, 1,
                "Cast", "Cancel",
                chosen =>
                {
                    if (chosen?.Count > 0)
                    {
                        var id = (TalentId)(int)chosen[0].Identifier;
                        _deferredInquiry = () => TalentSystem.ExecuteMapSpell(id);
                    }
                },
                null, "", false
            ), false, true);
        }

        // ── Talent menu ───────────────────────────────────────────────────────

        public static void ShowTalentMenu()
        {
            var all = TalentSystem.All.ToList();
            int cost = TalentSystem.PurchaseCost();
            string costStr = $"1 focus or attribute point ({cost}pt)";

            var elements = all.Select(d =>
            {
                bool owned = TalentSystem.Has(d.Id);
                string label = (owned ? "✓ " : "") + d.Name + (d.IsSpell ? "  [spell]" : "  [passive]");
                string hint  = $"{d.MechanicDesc}\n\n{d.Lore}\n\n" +
                               (owned ? "Known." : $"Cost: {costStr}");
                return new InquiryElement((int)d.Id, label, null, !owned, hint);
            }).ToList();

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "Talents",
                $"Choose what to learn. Gift is free; each after costs {cost}pt.",
                elements,
                true, 0, 1,
                "Learn", "Close",
                chosen =>
                {
                    if (chosen?.Count > 0)
                    {
                        var id = (TalentId)(int)chosen[0].Identifier;
                        _deferredInquiry = () => TalentSystem.TryPurchase(id, Hero.MainHero);
                    }
                },
                null, "", false
            ), false, true);
        }

        // ── Save / Load ───────────────────────────────────────────────────────

        public static void Save(IDataStore store)
        {
            var giftedList = _giftedChildIds.ToList();
            store.SyncData("LDM_IsMage",        ref _isMage);
            store.SyncData("LDM_GiftedChildren", ref giftedList);
            TalentSystem.Save(store);

            _giftedChildIds.Clear();
            if (giftedList != null)
                foreach (var id in giftedList) _giftedChildIds.Add(id);
        }
    }

    // Legacy alias — keeps old call-sites compiling without breaking changes
    public static class ColourKnowledge
    {
        public static bool HasAnySchool   => MageKnowledge.IsMage;
        public static bool HasSchool(ColorSchool s) => false;
        public static IEnumerable<ColorSchool> AllSchools => System.Array.Empty<ColorSchool>();
        public static int  GetMadnessOrderChance() => 0;
        public static bool ReducePurpleFertility() => false;
        public static float PurpleFertilityLevel   => 1f;
        public static void AddSchool(ColorSchool s) { }
        public static void ClearAllSchools() { }
        public static void RecordCast(ColorSchool s) { }
        public static bool IsChildGifted(string id) => MageKnowledge.IsChildGifted(id);
        public static void AddGiftedChild(string id) => MageKnowledge.AddGiftedChild(id);
        public static void FlushDeferredInquiry()    => MageKnowledge.FlushDeferredInquiry();
        public static void ResetForNewGame()         => MageKnowledge.ResetForNewGame();
        public static void ShowGrimoire(bool inMission, bool usingController)
            => MageKnowledge.ShowGrimoire(inMission, usingController);
        public static void ShowCampaignCastMenu()    => MageKnowledge.ShowCampaignCastMenu();
        public static void Save(IDataStore store)    => MageKnowledge.Save(store);
    }
}
