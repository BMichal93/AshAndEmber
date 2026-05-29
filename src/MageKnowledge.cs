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

namespace AshAndEmber
{
    public static class MageKnowledge
    {
        private static bool   _isMage            = false;
        private static bool   _isAshen          = false;
        internal static Action _deferredInquiry  = null;
        private static readonly HashSet<string> _giftedChildIds = new HashSet<string>();

        public static bool IsMage         => _isMage;
        public static bool IsAshen         => _isAshen;
        // Backward-compat shims used by old call sites
        public static bool HasAnySchool   => _isMage;
        public static IEnumerable<ColorSchool> AllSchools => System.Array.Empty<ColorSchool>();
        public static bool HasSchool(ColorSchool s) => false;
        public static int  GetMadnessOrderChance() => 0;
        public static bool ReducePurpleFertility() => false;
        public static float PurpleFertilityLevel   => 1f;

        public static void SetMage(bool value)   { _isMage = value; }
        public static void SetAshen(bool value) { _isAshen = value; }

        public static void ResetForNewGame()
        {
            _isMage          = false;
            _isAshen        = false;
            _deferredInquiry = null;
            _giftedChildIds.Clear();
            TalentSystem.ResetForNewGame();
            ColourLordRegistry.ResetForNewGame();
            AshenCitySystem.ResetForNewGame();
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
        public static void QueueAshenPrompt(Action onResolved)
        {
            _deferredInquiry = () => ShowAshenPrompt(onResolved);
        }

        private static void ShowAshenPrompt(Action onResolved)
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
                    _isAshen = true;
                    ApplyAshenAppearance(Hero.MainHero);
                    try { AshenCitySystem.OnPlayerBecameAshen(); } catch { }
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The fire dies. Something colder and older takes its place. The world will see it in your eyes.",
                        new Color(0.3f, 0.35f, 0.7f)));
                    // Kicked from kingdom — the cold marks you
                    try
                    {
                        if (Hero.MainHero?.Clan?.Kingdom != null)
                            TaleWorlds.CampaignSystem.Actions.ChangeKingdomAction.ApplyByLeaveKingdom(
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

        // ── Ashen appearance ─────────────────────────────────────────────────
        // Called whenever a hero becomes Ashen. Modifies StaticBodyProperties
        // bit fields to approximate ash-white hair. The exact bit layout is
        // Bannerlord-version-dependent so the whole method is wrapped in try/catch.
        internal static void ApplyAshenAppearance(Hero hero)
        {
            if (hero == null) return;
            try
            {
                var bp = hero.BodyProperties;
                var sp = bp.StaticProperties;
                // All colour positions below are empirical; exact bit layout varies by game version.
                // Hair: bits 32-39 ≈ saturation, bits 40-47 ≈ hue in KeyPart4.
                // Clearing saturation and setting near-zero hue gives ash-grey hair.
                ulong k4 = sp.KeyPart4;
                k4 = (k4 & ~0x00FFFF0000000000UL) | 0x0000010000000000UL;
                // Eyes: same colour-encoding byte range in KeyPart5 (empirical).
                // Zeroing colour data pushes iris colour toward grey.
                ulong k5 = sp.KeyPart5;
                k5 = k5 & ~0x00FFFF0000000000UL;
                // Skin: colour bytes in KeyPart7 (empirical).
                // Clearing these bits approximates a pale grey/ashen skin tone.
                ulong k7 = sp.KeyPart7;
                k7 = k7 & ~0x000000FFFFFF0000UL;
                var newStatic = new StaticBodyProperties(
                    sp.KeyPart1, sp.KeyPart2, sp.KeyPart3, k4,
                    k5, sp.KeyPart6, k7, sp.KeyPart8);
                var newBp = new BodyProperties(bp.DynamicProperties, newStatic);
                // Hero.BodyProperties has a non-public setter in most Bannerlord builds
                typeof(Hero).GetProperty("BodyProperties",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                    ?.GetSetMethod(nonPublic: true)
                    ?.Invoke(hero, new object[] { newBp });
            }
            catch { }
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

            string breakKey   = usingController ? "L3" : "X";
            string inputHint = usingController
                ? "Hold LB + left stick (↑/←/→/↓), press L3 to Break. Release LB to cast. LB+RB to open grimoire."
                : "Hold Left Alt + W/A/D/S, press X to Break, release to cast. Alt+X opens grimoire (when no form started).";

            string ashenNote = _isAshen
                ? "\n[Ashen] Each cast adds criminal rating instead of aging.\n"
                : "";

            string desc =
                $"{inputHint}\n\n" +
                "Channelling\n" +
                $"  Form keys → Break ({breakKey}) → effect keys → release focus.\n" +
                "  Mixed forms all fire simultaneously. Effects stack.\n\n" +
                "Forms  (before Break, mix freely)\n" +
                "  ↑  Blast   — forward cone, 2.5m per ↑\n" +
                "  ←  Wave    — 3×3 fire grid, +2m per ←, +1 size per 5←\n" +
                "  →  Barrier — wall of nodes, 1 per →; cast again to release\n" +
                "  ↓  Burst   — circle around self, 2.5m radius per ↓\n\n" +
                "Effects  (after Break)\n" +
                "  ↑  Flame     — 25 damage per ↑\n" +
                "  ←  Surge     — 6m push per ←  (+5 kinetic damage per ←)\n" +
                "  →  Smoulder  — 15 morale lost per →  (+8 side damage per →)\n" +
                "  ↓  Reverse   — flips all effects (heal / pull / morale boost)\n\n" +
                "Combined fires\n" +
                "  Flame+Smoulder = Scorch  |  Surge+Flame = Cinder  |  Smoulder+Surge = Ember Surge\n\n" +
                "Sigil  (no Break needed, repeat to expand)\n" +
                "  ↓↓         Ward — self only,        1 day\n" +
                "  ↓↓↓        Ward — 2m radius,        2 days\n" +
                "  ↓↓↓↓       Ward — 4m radius,        3 days\n\n" +
                "Burning cost  (every input costs 1 day)\n" +
                "  1 input = 1 day  |  3 inputs = 3 days  |  6 inputs = 6 days  |  …\n" +
                (TalentSystem.Has(TalentId.BattleMage) ? "  [Tempered] Cost − 1 day (minimum 0).\n" : "") +
                ashenNote +
                "\nExample\n" +
                "  ↑  X  ↑  =  Blast (2.5m), 12 flame, 2 days  (2 inputs).\n" +
                "  ↑↑↑  X  ↑↑↑  =  Blast (7.5m), 36 flame, 6 days  (6 inputs).\n" +
                "  ↑↑  ↓↓  X  ↑↑  =  Blast (5m) + Burst (5m) + 24 flame, 6 days  (6 inputs).";

            string title = _isAshen ? "The Ashen Fire" : "The Inner Fire";

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
            string costStr = $"{cost} focus point{(cost != 1 ? "s" : "")}";

            var elements = all.Select(d =>
            {
                bool   owned = TalentSystem.Has(d.Id);
                string icon  = d.IsSpell ? "✦" : "◆";
                string tag   = d.IsSpell ? "spell" : "passive";
                string check = owned ? "✓ " : "   ";
                string label = $"{check}{icon}  {d.Name}   [{tag}]";
                string hint  = $"【 {d.Name} 】  {tag}\n\n" +
                               $"{d.MechanicDesc}\n\n" +
                               $"{d.Lore}\n\n" +
                               (owned ? "— Already known —" : $"Cost: {costStr}");
                return new InquiryElement((int)d.Id, label, null, !owned, hint);
            }).ToList();

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "Talents  —  The Inner Fire",
                $"✦ = spell   ◆ = passive   Cost: {costStr} each. Gift is free.",
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
            store.SyncData("LDM_IsAshen",        ref _isAshen);
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
