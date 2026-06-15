// =============================================================================
// ASH AND EMBER — MageKnowledge.UI.cs
// Ashen appearance, grimoire, campaign-cast and talent menus, save/load.
// Partial of MageKnowledge (shared static state lives in MageKnowledge.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public partial class MageKnowledge
    {
        // ── Ashen appearance ─────────────────────────────────────────────────
        // Called whenever a hero becomes Ashen. Persists the witchy-ashen look
        // (ash-grey hair, cold-blue eyes, grey skin — see AshenVisuals) into
        // the hero's BodyProperties. The bit layout is Bannerlord-version-
        // dependent so the whole method is wrapped in try/catch.
        internal static void ApplyAshenAppearance(Hero hero)
        {
            if (hero == null) return;
            try
            {
                var newBp = AshenVisuals.MakeAshenBodyProperties(hero.BodyProperties);
                AshenVisuals.SetHeroBodyProperties(hero, newBp);
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

            string ashenNote = _isAshen
                ? "\n[Ashen] Each cast raises criminal rating instead of aging you. After your first working each day, further casts risk possession.\n"
                : "";

            // The casting gestures (which keys to hold, break, release) live in the
            // Codex of Hand and Voice. This book keeps the deeper craft — what each
            // mark shapes and what power it carries.
            string desc =
                AgingSystem.BuildLedgerText() +
                "── THE CASTING, IN BRIEF ────────────────────────────\n" +
                "  Hold Left Alt → shape with W A D S → press X to Break →\n" +
                "  give power with W A D S → release Alt to loose it.\n" +
                "  Watch [ U ▷ U ] build your formula; keep your hands free.\n" +
                "  (Full gestures, miracles and satchel: your Codex of Hand and Voice.)\n\n" +
                "── SHAPES  (before Break) ───────────────────────────\n" +
                "  W / ↑  Blast    — cone of fire forward  (+2.5 m per W)\n" +
                "  A / ←  Missile  — fire bolt that explodes  (+3 m range, +1 m blast per A)\n" +
                "  D / →  Barrier  — summoned wall of nodes  (press again to release)\n" +
                "  S / ↓  Burst    — ring around you  (+2.5 m radius per S, also heals you)\n" +
                "  Mix freely — W then S fires a Blast and a Burst at the same time.\n\n" +
                "── POWER  (after Break) ─────────────────────────────\n" +
                "  Every damage key deals 25 fire damage — but each carries a nature:\n" +
                "  W / ↑  Sear   — searing burn  (+5 burn per press; Immolate amplifies)\n" +
                "  A / ←  Force  — concussive push (1.5 m per press; Scatter amplifies)\n" +
                "  D / →  Shred  — armour shred  (+4% damage taken; Sunder amplifies)\n" +
                "  S / ↓  Restore — 15 healing per press + small morale lift to allies\n" +
                "  Mix natures freely: WWA after Break = 75 damage, sear ×2 + force ×1.\n" +
                "  Owning a key's talent replaces its weak innate effect with the full one.\n\n" +
                "── EXAMPLES  (try these first!) ─────────────────────\n" +
                "  W, X, W, release        →  Blast,   25 dmg,   1 day\n" +
                "  A, X, W, release        →  Missile, 25 dmg,   1 day\n" +
                "  S, X, SS, release       →  Burst,   30 heal,  2 days  (heals you too)\n" +
                "  AAA, X, WW, release     →  Long missile, 50 dmg,  4 days\n" +
                "  WW+SS, X, W+S, release  →  Blast+Burst, dmg+heal, 5 days\n\n" +
                "── BATTLE COST  (days of life per cast — geometric) ──\n" +
                "  1–2 inputs = 1 day   3 = 2 days   4 = 3   5 = 4   6 = 5\n" +
                "  7 = 8 days   8 = 11   9 = 15   10 = 21   12 = 41   14 = 80\n" +
                "  Hard cap: 84 days (= 1 year). Mage lords age at the same rate.\n" +
                (TalentSystem.Has(TalentId.BattleMage) ? "  [Tempered] −25% cost (min 1) + up to 30% age reduction.\n" : "") +
                ashenNote +
                "\n── CAMPAIGN SPELLS  (outside a mission → \"Cast\") ────────\n" +
                "  A 3-step ritual description appears. Commit it to memory.\n" +
                "  Each step has many variant phrasings — one is shown each cast.\n" +
                "  You are then asked to pick the correct phrasing from three options.\n\n" +
                "  Score → power multiplier:\n" +
                "    3/3 correct → 1.50×   Resonance — the rite was perfect.\n" +
                "    2/3 correct → 1.20×   Amplified.\n" +
                "    1/3 correct → 0.80×   Flickering.\n" +
                "    0/3 correct → 0.50×   Scattered.\n\n" +
                "  The aging cost is always paid.\n" +
                "  \"Cast without the rite\" skips the game at 1.00×.\n" +
                "\n── LOST FORMS  (Talents → Lost Form, 2 focus pts each) ──────\n" +
                "  ◈ Widened Blast   — blast cone opens from ~49° to ~60°\n" +
                "  ◈ Twin Bolt       — missile fires two bolts at 60% power each\n" +
                "  ◈ Fading Ward     — barrier expires after 60 seconds\n" +
                "  ◈ Directed Burst  — full power forward, 40% in the rear arc\n\n" +
                "  Open this book any time: Left Alt + X  (LB + RB on a controller)." +
                (_isAshen ? AshenQuestSystem.GetGrimoireSummary() : DragonQuestSystem.GetGrimoireSummary());

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

        // ── Controls codex ────────────────────────────────────────────────────
        // The one general manual for every key combo across the mod's three
        // disciplines. Shown once at campaign start; the grimoire points here for
        // the casting gestures, and the Sanctuary / Altar / Lab menus echo the
        // miracle and satchel chords. Climatic, terse, device-agnostic (keyboard
        // primary, the controller chord beside it).
        public static void ShowControlsCodex()
        {
            string body =
                "Three disciplines answer a prepared hand. Learn their gestures here, "
                + "while the candle is steady — the field gives no time to remember.\n\n"
                + "Keys are for keyboard; the (controller) chord stands beside each.\n\n"
                + "── THE INNER FIRE — spells (for the gifted) ─────────\n"
                + "  Open the grimoire    Left Alt + X        (LB + RB)\n"
                + "  Work a spell         Hold Left Alt …      (hold LB)\n"
                + "    • shape it         W  A  D  S           (left stick)\n"
                + "    • Break to power   X                    (L3)\n"
                + "    • give it power    W  A  D  S           (left stick)\n"
                + "    • loose it         release Left Alt     (release LB)\n"
                + "  Sheathe your weapon first — the working needs free hands.\n\n"
                + "── MIRACLES — Grace & Cold ──────────────────────────\n"
                + "  First charge at a Sanctuary (Grace) or Ashen Altar (Cold).\n"
                + "  On the field         Shift + X            (L3 + R3)\n"
                + "  In battle            Hold Left Ctrl …     (hold RB)\n"
                + "    then type the six-mark sequence shown for the miracle, and release.\n\n"
                + "── ALCHEMY — the satchel ────────────────────────────\n"
                + "  Open it anywhere, even mid-battle, to drink an elixir:\n"
                + "  Open the satchel     Left Ctrl + X        (RB + R3)\n\n"
                + "The grimoire holds the deeper craft of the Fire. "
                + "The rest, you will learn by surviving.";

            InformationManager.ShowInquiry(new InquiryData(
                "The Disciplines of Hand and Voice",
                body, true, false, "I will remember.", "", null, null), true, true);
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
                        _deferredInquiry = () => SpellMinigame.Begin(id);
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
                // Consumables are granted through encounters, not purchased here
                if (d.IsConsumable) continue;
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
                        TalentCategory.LostForm    => "─── Lost Form ───",
                        _                          => "───────────",
                    };
                    // Negative identifier marks non-selectable separator rows
                    elements.Add(new InquiryElement(-(int)d.Category - 1, header, null, false, ""));
                }

                bool   owned      = TalentSystem.Has(d.Id);
                bool   selectable = !d.IsInfo && !owned;
                int    talentCost = d.FocusCost > 0 ? d.FocusCost : cost;
                string icon  = d.IsInfo                                   ? "◉"
                             : d.Category == TalentCategory.Spell         ? "✦"
                             : d.Category == TalentCategory.Enchantment   ? "❋"
                             : d.Category == TalentCategory.LostForm      ? "◈"
                             :                                               "◆";
                string tag   = d.IsInfo             ? "status"
                             : d.Category == TalentCategory.LostForm ? "lost form"
                             : d.Category.ToString().ToLowerInvariant();
                string check = owned ? "✓ " : "   ";
                string label = $"{check}{icon}  {d.Name}   [{tag}]";
                string costHint = $"Cost: {talentCost} focus point{(talentCost != 1 ? "s" : "")}";
                string hint  = $"【 {d.Name} 】  {tag}\n\n" +
                               $"{d.MechanicDesc}\n\n" +
                               $"{d.Lore}\n\n" +
                               (d.IsInfo ? "— Status — not a talent to be learned —" : owned ? "— Already known —" : costHint);
                elements.Add(new InquiryElement((int)d.Id, label, null, selectable, hint));
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "Talents  —  The Inner Fire",
                $"✦ = spell   ❋ = enchantment   ◆ = passive   ◈ = lost form   Cost: {costStr} (lost forms: 2 pts).",
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
            store.SyncData("LDM_WhisperCount",   ref _whisperCount);
            store.SyncData("LDM_ColdCallCD",     ref _coldCallCountdown);
            store.SyncData("LDM_WhisperQuiet",   ref _daysSinceWhisperGain);
            store.SyncData("LDM_PossessStrain",  ref _possessionStrainDays);
            TalentSystem.Save(store);

            _giftedChildIds.Clear();
            if (giftedList != null)
                foreach (var id in giftedList) _giftedChildIds.Add(id);
        }
    }
}
