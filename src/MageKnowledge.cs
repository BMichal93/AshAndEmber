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
using TaleWorlds.CampaignSystem.CharacterDevelopment;
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
        private static readonly Random _rng = new Random();

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
            if (pending == null) return;
            if (Campaign.Current != null)
                Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
            pending.Invoke();
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
                    // Apply crime rating to old kingdom before leaving it
                    try
                    {
                        if (Hero.MainHero?.Clan?.Kingdom is TaleWorlds.CampaignSystem.Kingdom oldK)
                            TaleWorlds.CampaignSystem.Actions.ChangeCrimeRatingAction.Apply(oldK, 50f, true);
                    }
                    catch { }
                    // Leave old kingdom and join the Ashen
                    try { AshenCitySystem.OnPlayerBecameAshen(); } catch { }
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The fire dies. Something colder and older takes its place. The world will see it in your eyes.",
                        new Color(0.3f, 0.35f, 0.7f)));
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

        // ── Possession event (Ashen 2nd+ cast per day) ───────────────────────

        public static void QueuePossessionEvent()
        {
            _deferredInquiry = ShowPossessionEvent;
        }

        private static void ShowPossessionEvent()
        {
            int lSkill = 0, aSkill = 0;
            try { lSkill = Hero.MainHero?.GetSkillValue(DefaultSkills.Leadership) ?? 0; } catch { }
            try { aSkill = Hero.MainHero?.GetSkillValue(DefaultSkills.Athletics) ?? 0; } catch { }
            int lPct = Math.Min(90, (int)(lSkill * 0.3f));
            int aPct = Math.Min(90, (int)(aSkill * 0.3f));
            float lChance = Math.Min(0.9f, lSkill * 0.003f);
            float aChance = Math.Min(0.9f, aSkill * 0.003f);

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "The Flame Turns",
                "Dark instincts and cold flame flood your body. The ash stirs something ancient — it recognises itself in you, and it is not yet satisfied.\n\nFor a terrible moment you cannot tell whether you are resisting the cold or whether what fights back is still you.",
                new List<InquiryElement>
                {
                    new InquiryElement("surrender", "Surrender to it.", null, true,
                        "Let the cold take what it wants. It will not need much more."),
                    new InquiryElement("leader", "Focus your will — fight it from within.", null, true,
                        $"Leadership test. Skill: {lSkill}. Success chance: {lPct}%."),
                    new InquiryElement("athlete", "Drive it back — overwhelm it with your body.", null, true,
                        $"Athletics test. Skill: {aSkill}. Success chance: {aPct}%."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    string choice = chosen?[0]?.Identifier as string;
                    if (choice == "surrender")
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            "You let go. The cold is grateful.", new Color(0.3f, 0.35f, 0.7f)));
                        try { TaleWorlds.CampaignSystem.Actions.KillCharacterAction.ApplyByOldAge(Hero.MainHero, true); } catch { }
                    }
                    else if (choice == "leader")
                    {
                        if (_rng.NextDouble() < lChance)
                            InformationManager.DisplayMessage(new InformationMessage(
                                "Your will holds. The cold retreats — for now.", new Color(0.7f, 0.7f, 0.9f)));
                        else
                        {
                            InformationManager.DisplayMessage(new InformationMessage(
                                "Your will breaks. The cold claims you.", new Color(0.3f, 0.35f, 0.7f)));
                            try { TaleWorlds.CampaignSystem.Actions.KillCharacterAction.ApplyByOldAge(Hero.MainHero, true); } catch { }
                        }
                    }
                    else if (choice == "athlete")
                    {
                        if (_rng.NextDouble() < aChance)
                            InformationManager.DisplayMessage(new InformationMessage(
                                "You push it back. The cold recoils.", new Color(0.7f, 0.7f, 0.9f)));
                        else
                        {
                            InformationManager.DisplayMessage(new InformationMessage(
                                "Your body gives out. The cold takes you.", new Color(0.3f, 0.35f, 0.7f)));
                            try { TaleWorlds.CampaignSystem.Actions.KillCharacterAction.ApplyByOldAge(Hero.MainHero, true); } catch { }
                        }
                    }
                },
                null, "", false
            ), false, true);
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

            string stepFocus   = usingController ? "Hold LB"              : "Hold Left Alt";
            string stepShape   = usingController ? "Left stick ↑ ← → ↓"  : "W  A  D  S";
            string stepBreak   = usingController ? "Press L3"             : "Press X";
            string stepRelease = usingController ? "Release LB"           : "Release Left Alt";
            string openBook    = usingController ? "LB + RB"              : "Left Alt + X  (before any key is pressed)";

            string ashenNote = _isAshen
                ? "\n[Ashen] Each cast raises criminal rating instead of aging you. After your first working each day, further casts risk possession.\n"
                : "";

            string desc =
                "── HOW TO CAST ──────────────────────────────────────\n" +
                $"  1. {stepFocus}  → enter Focus\n" +
                $"  2. {stepShape}  → shape the spell  (repeat for more power)\n" +
                $"  3. {stepBreak}  → Break: locks shape, enter power phase\n" +
                "  4. W / A / D  → Damage   |   S → Heal\n" +
                $"  5. {stepRelease}  → the spell fires!\n\n" +
                "  Watch the screen: [ U ▷ U ] shows your formula as you build it.\n" +
                "  Your hands must be free — sheathe your weapon first.\n\n" +
                "── SHAPES  (step 2, before Break) ───────────────────\n" +
                "  W / ↑  Blast    — cone of fire forward  (+2.5 m per W)\n" +
                "  A / ←  Missile  — fire bolt that explodes  (+3 m range, +1 m blast per A)\n" +
                "  D / →  Barrier  — summoned wall of nodes  (press again to release)\n" +
                "  S / ↓  Burst    — ring around you  (+2.5 m radius per S, also heals you)\n" +
                "  Mix freely — W then S fires a Blast and a Burst at the same time.\n\n" +
                "── POWER  (step 4, after Break) ─────────────────────\n" +
                "  W / A / D  →  Damage  — 25 fire damage per press, hits enemies\n" +
                "  S          →  Restore — 15 healing per press, reaches nearby allies\n" +
                "  Talents add enchantments automatically — no extra keys needed.\n\n" +
                "── EXAMPLES  (try these first!) ─────────────────────\n" +
                "  W, X, W, release        →  Blast,   25 dmg,   1 day\n" +
                "  A, X, W, release        →  Missile, 25 dmg,   1 day\n" +
                "  S, X, SS, release       →  Burst,   30 heal,  1 day  (heals you too)\n" +
                "  AAA, X, WW, release     →  Long missile, 50 dmg,  2 days\n" +
                "  WW+SS, X, W+S, release  →  Blast+Burst, dmg+heal, 2 days\n\n" +
                "── BATTLE COST  (days of life per cast — geometric) ──\n" +
                "  1–2 inputs = 1 day   3 = 2 days   4 = 3   5 = 4   6 = 5\n" +
                "  7 = 8 days   8 = 11   9 = 15   10 = 21   12 = 41   14 = 80\n" +
                "  Hard cap: 84 days (= 1 year). Mage lords age at the same rate.\n" +
                (TalentSystem.Has(TalentId.BattleMage) ? "  [Tempered] −1 day cost (min 1) + up to 30% age reduction.\n" : "") +
                ashenNote +
                $"\n  Open this page: {openBook}" +
                DragonQuestSystem.GetGrimoireSummary();

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

            string castDesc = _isAshen
                ? "Choose a working. Costs criminal rating instead of years." +
                  (TalentSystem.DailyCastCount > 0 ? " After your first working today, further casts risk possession." : "")
                : TalentSystem.DailyCastCount == 0
                    ? "Choose a working. Each costs 1 day. Resonance may spare you once in four."
                    : $"Choose a working. Working #{TalentSystem.DailyCastCount + 1} today — costs {TalentSystem.GetDailyCastCost()} days. Resonance may spare you.";

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "Cast",
                castDesc,
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

            var elements = new List<InquiryElement>();

            TalentCategory? lastCategory = null;
            foreach (var d in all)
            {
                // Info cards are only shown when the condition is met
                if (d.IsInfo && d.Id == TalentId.AshenGift && !_isAshen) continue;

                // Insert a disabled separator when the category changes
                if (d.Category != lastCategory)
                {
                    lastCategory = d.Category;
                    string header = d.Category switch
                    {
                        TalentCategory.Passive     => "─── Passive ───",
                        TalentCategory.Enchantment => "─── Enchantment ───",
                        TalentCategory.Spell       => "─── Spell ───",
                        TalentCategory.Info        => "─── Ashen Status ───",
                        _                          => "───────────",
                    };
                    // Negative identifier marks non-selectable separator rows
                    elements.Add(new InquiryElement(-(int)d.Category - 1, header, null, false, ""));
                }

                bool   owned     = TalentSystem.Has(d.Id);
                bool   selectable = !d.IsInfo && !owned;
                string icon  = d.IsInfo                              ? "◉"
                             : d.Category == TalentCategory.Spell   ? "✦"
                             : d.Category == TalentCategory.Enchantment ? "❋"
                             :                                            "◆";
                string tag   = d.IsInfo ? "status" : d.Category.ToString().ToLowerInvariant();
                string check = owned ? "✓ " : "   ";
                string label = $"{check}{icon}  {d.Name}   [{tag}]";
                string hint  = $"【 {d.Name} 】  {tag}\n\n" +
                               $"{d.MechanicDesc}\n\n" +
                               $"{d.Lore}\n\n" +
                               (d.IsInfo ? "— Status — not a talent to be learned —" : owned ? "— Already known —" : $"Cost: {costStr}");
                elements.Add(new InquiryElement((int)d.Id, label, null, selectable, hint));
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "Talents  —  The Inner Fire",
                $"✦ = spell   ❋ = enchantment   ◆ = passive   Cost: {costStr} each.",
                elements,
                true, 0, 1,
                "Learn", "Close",
                chosen =>
                {
                    if (chosen?.Count > 0)
                    {
                        int id = (int)chosen[0].Identifier;
                        if (id < 0) return; // separator row — ignore
                        _deferredInquiry = () => TalentSystem.TryPurchase((TalentId)id, Hero.MainHero);
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
