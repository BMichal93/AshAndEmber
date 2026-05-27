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
                    "You feel nothing. The current does not stir for you.",
                    Color.FromUint(0xFFAAAAAA)));
                return;
            }

            string inputHint = usingController
                ? "Hold LB + left stick (↑/←/→/↓), press L3 to Break. Release LB to cast. LB+RB to open spellbook."
                : "Hold Left Alt + W/A/D/S, press E to Break, release to cast. Alt+B opens spellbook.";

            string desc =
                "[LIFE & DEATH MAGIC]\n" +
                $"{inputHint}\n\n" +
                "CASTING\n" +
                "  Input form keys → press Break (E/L3) → input effect keys → release focus key.\n" +
                "  Mixed form inputs = fumble. Effects stack freely.\n\n" +
                "FORMS (before Break)\n" +
                "  ↑  Blast  — forward cone, 2m per ↑\n" +
                "  ←  Aura   — expanding cloud, +1 node per ←\n" +
                "  →  Barrier — wall nodes, 1 node per →\n" +
                "  ↓  Burst  — circle around self, 2m radius per ↓\n\n" +
                "EFFECTS (after Break)\n" +
                "  ↑  Vitality  — 5 damage per ↑ (Red)\n" +
                "  ←  Force     — 2m pushback per ← (Blue)\n" +
                "  →  Will-Break — -3 morale per → (Yellow)\n" +
                "  ↓  Reverse   — flip all effects (heal/pull/boost morale)\n\n" +
                "COMBINED COLOURS\n" +
                "  Vitality+Will-Break = Orange  |  Force+Vitality = Purple  |  Will-Break+Force = Green\n" +
                "  Reversed effects appear in lighter shades.\n\n" +
                "AGING COST  (total inputs = form + effect)\n" +
                "  <4 inputs — free  |  4–5 = 1 day  |  6–7 = 2 days  |  8–9 = 3 days\n" +
                (TalentSystem.Has(TalentId.BattleMage) ? "  [Battle Mage] Cost lowered: threshold 5 instead of 4.\n" : "") +
                "\nEXAMPLE\n" +
                "  ↑↑↑  Break  ↑↑↑↑↑  =  Blast (6m cone), 25 damage, ages 2 days.";

            if (!inMission)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Life & Death Magic",
                    desc,
                    true, true,
                    "Cast a Spell", "Talents",
                    () => { _deferredInquiry = ShowCampaignCastMenu; },
                    () => { _deferredInquiry = ShowTalentMenu; }
                ), true, true);
            }
            else
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Life & Death Magic",
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
                    "You are a captive. Life energy cannot flow in chains.",
                    Color.FromUint(0xFFAAAAAA)));
                return;
            }

            var spells = TalentSystem.All
                .Where(d => d.IsSpell && TalentSystem.Has(d.Id))
                .ToList();

            if (spells.Count == 0)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "No campaign spells available. Learn Spell talents first.",
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
                "Cast a Life & Death Spell",
                "Choose a spell to cast. Each Spell talent ages you by 1 day (Sorcerer: 25% chance to avoid).",
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
            string costStr = $"1 Focus point or attribute point (currently: {cost}pt per talent)";

            var elements = all.Select(d =>
            {
                bool owned = TalentSystem.Has(d.Id);
                string label = (owned ? "✓ " : "") + d.Name + (d.IsSpell ? " [Spell]" : " [Passive]");
                string hint  = $"{d.MechanicDesc}\n\n{d.Lore}\n\n" +
                               (owned ? "Already learned." : $"Cost: {costStr}");
                return new InquiryElement((int)d.Id, label, null, !owned, hint);
            }).ToList();

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "Life & Death Talents",
                $"Select a talent to learn. Cost: {costStr}.\n" +
                "Talents can be learned in any order. 'Gift' is free and already learned.",
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
