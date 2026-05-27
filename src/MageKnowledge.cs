// =============================================================================
// LIFE & DEATH MAGIC — MageKnowledge.cs
// Tracks whether the player carries the gift, manages the grimoire UI,
// and provides the talent learning menu.
// ColourKnowledge is a legacy alias kept for backward-compatible call sites.
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
        private static bool   _isBlight          = false;
        internal static Action _deferredInquiry  = null;
        private static readonly HashSet<string> _giftedChildIds = new HashSet<string>();

        public static bool IsMage         => _isMage;
        public static bool IsBlight       => _isBlight;
        // Backward-compat shims used by old call sites
        public static bool HasAnySchool   => _isMage;
        public static IEnumerable<ColorSchool> AllSchools => System.Array.Empty<ColorSchool>();
        public static bool HasSchool(ColorSchool s) => false;
        public static int  GetMadnessOrderChance() => 0;
        public static bool ReducePurpleFertility() => false;
        public static float PurpleFertilityLevel   => 1f;

        public static void SetMage(bool value)   { _isMage = value; }
        public static void SetBlight(bool value) { _isBlight = value; }

        public static void ResetForNewGame()
        {
            _isMage          = false;
            _isBlight        = false;
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

        // ── Blight ────────────────────────────────────────────────────────────

        /// <summary>
        /// Called by AgingSystem when the player would die at 100.
        /// Queues the blight-or-death inquiry for the next map-layer flush.
        /// </summary>
        public static void QueueBlightPrompt(Action onResolved)
        {
            _deferredInquiry = () => ShowBlightPrompt(onResolved);
        }

        private static void ShowBlightPrompt(Action onResolved)
        {
            InformationManager.ShowInquiry(new InquiryData(
                "The Last Ember",
                "A century of years. The fire should have consumed you by now — but it has not gone out. Something darker waits at the edge of the ash.\n\n" +
                "You can let go. The fire will burn clean, and it will end.\n\n" +
                "Or you can take the cold that remains. You will not die. But what burns in you afterward will not be warm.",
                true, true,
                "Take the cold", "Let it end",
                () =>
                {
                    onResolved?.Invoke();
                    _isBlight = true;
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The fire dies. Something colder and older takes its place. The world will see it in your eyes.",
                        new Color(0.3f, 0.35f, 0.7f)));
                    // Kicked from kingdom — the cold marks you
                    try
                    {
                        if (Hero.MainHero?.Clan?.Kingdom != null)
                            TaleWorlds.CampaignSystem.Actions.LeaveClanFromKingdomAction.Apply(
                                Hero.MainHero.Clan, false);
                    }
                    catch { }
                    // Criminal rating spike
                    try
                    {
                        if (Hero.MainHero?.MapFaction is TaleWorlds.CampaignSystem.Kingdom k)
                            TaleWorlds.CampaignSystem.Actions.ChangeCrimeRatingAction.Apply(k, 50f, true);
                    }
                    catch { }
                },
                () =>
                {
                    onResolved?.Invoke();
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The fire burns clean at last.",
                        new Color(0.8f, 0.6f, 0.3f)));
                    try { TaleWorlds.CampaignSystem.Actions.KillCharacterAction.ApplyByOldAge(Hero.MainHero, true); } catch { }
                }
            ), true, true);
        }

        // ── Spellbook / Grimoire ──────────────────────────────────────────────

        public static void ShowGrimoire(bool inMission, bool usingController)
        {
            if (!_isMage)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "The fire does not stir in you.",
                    Color.FromUint(0xFFBBAA99)));
                return;
            }

            string inputHint = usingController
                ? "Hold LB + left stick (↑/←/→/↓), press L3 to Break. Release LB to cast. LB+RB to open grimoire."
                : "Hold Left Alt + W/A/D/S, press E to Break, release to cast. Alt+B opens grimoire.";

            string blightNote = _isBlight
                ? "\n[Ash-cold] Each cast adds criminal rating instead of aging.\n"
                : "";

            string desc =
                $"{inputHint}\n\n" +
                "Channelling\n" +
                "  Form keys → Break (E/L3) → effect keys → release focus.\n" +
                "  Mixed form inputs fumble. Effects stack.\n\n" +
                "Forms  (before Break)\n" +
                "  ↑  Blast   — forward cone, 2m per ↑\n" +
                "  ←  Wave    — 3×3 fire grid, +2m per ←, +1 size per 5←\n" +
                "  →  Barrier — wall of nodes, 1 per →\n" +
                "  ↓  Burst   — circle around self, 2m radius per ↓\n\n" +
                "Effects  (after Break)\n" +
                "  ↑  Flame     — 8 damage per ↑\n" +
                "  ←  Surge     — 3m push per ←\n" +
                "  →  Smoulder  — 5 morale lost per →\n" +
                "  ↓  Reverse   — flips all effects\n\n" +
                "Combined fires\n" +
                "  Flame+Smoulder = Scorch  |  Surge+Flame = Cinder  |  Smoulder+Surge = Ember Surge\n\n" +
                "Sigil\n" +
                "  ↑←↓→  Ward — 10 s magic immunity, costs 1 day (no Break needed)\n\n" +
                "Burning cost  (form inputs + effect inputs)\n" +
                "  Below 4 — free  |  4–5 = 1 day  |  6–7 = 2 days  |  8–9 = 3 days\n" +
                (TalentSystem.Has(TalentId.BattleMage) ? "  [Tempered] Threshold raised to 5.\n" : "") +
                blightNote +
                "\nExample\n" +
                "  ↑↑↑  Break  ↑↑↑↑↑  =  Blast (6m), 40 flame, 2 days.";

            string title = _isBlight ? "The Ashen Fire" : "The Inner Fire";

            if (!inMission)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    title,
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
                    title,
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
                    "You are bound. The fire cannot kindle.",
                    Color.FromUint(0xFFBBAA99)));
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
            store.SyncData("LDM_IsBlight",      ref _isBlight);
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
