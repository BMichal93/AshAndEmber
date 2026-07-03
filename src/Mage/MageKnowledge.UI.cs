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
using TaleWorlds.CampaignSystem.Settlements;
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
            // The player wears the cold look only while genuinely Ashen. A mortal
            // Northerner (Sturgian origin who opted out of the Ashen) must never be
            // given the grey skin and cold-blue eyes — every become-Ashen path sets
            // IsAshen before calling here, so this guard only blocks erroneous calls.
            if (hero == Hero.MainHero && !_isAshen) return;
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
                "  Hold Left Alt → stand still and DRAW → Attack looses a cone,\n" +
                "  Block raises a wall → release Alt to stop.\n" +
                "  The longer you hold, the harder it strikes (full at ~7 s);\n" +
                "  hold too long (~15 s) and the charge disperses. Keep a hand free.\n" +
                "  Fire is loaded by default. Tap W / A / S / D to load a learned\n" +
                "  element — W Wind · S Earth · A Water · D Spirit.\n" +
                "  (Full gestures, miracles and crystals: your Codex of Hand and Voice.)\n\n" +
                "── THE FIVE ELEMENTS  (Attack = cone · Block = wall) ─\n" +
                "  Fire    — a cone of fire            / a wall of fire\n" +
                "  Wind    — a blast that hurls & slows / a wall that turns arrows\n" +
                "  Earth   — a burst of rooting stone   / a stone wall\n" +
                "  Water   — a slowing wave             / a barrier of mist\n" +
                "  Spirit  — fear men & horses, a stray / a wall that heartens\n" +
                "            order into their ranks       and mends your own\n" +
                "  A longer draw makes every element strike harder — and a DEEP\n" +
                "  draw of Fire sets its marks ALIGHT: they keep burning (up to\n" +
                "  ~12/s for 5 s at full charge; a snap flick ignites nothing).\n" +
                "  The Ashen cold clings on as deep frost — the same toll.\n\n" +
                "── WALLS WARD  (while they stand) ───────────────────\n" +
                "  Fire   — devours any gale that crosses it; horses shy from flame.\n" +
                "  Wind   — turns arrows and bolts aside, scatters flung stone.\n" +
                "  Earth  — stops arrows dead and breaks the water's wave.\n" +
                "  Water  — quenches fire to steam and drinks the wind's force.\n" +
                "  The elements are impartial: a wall wards against ALL shot and\n" +
                "  magic that crosses it, your own included. Only the Spirit's\n" +
                "  dread passes every wall — as do despair (Duskstone) and Grace,\n" +
                "  for no earthly wall stops the unseen. Other crystals are\n" +
                "  shard-force: wind and stone bar their reach.\n\n" +
                "── THE ELEMENTS REACT ───────────────────────────────\n" +
                "  Fire on water   — boils to a hanging cloud of STEAM; burning\n" +
                "                    ground and burning men are DOUSED by a wave\n" +
                "                    or standing mist (a torrent can breach a\n" +
                "                    fire wall).\n" +
                "  Water on stone  — the broken wave churns the ground to MUD:\n" +
                "                    a bogging patch that slows all who cross it,\n" +
                "                    charging cavalry worst of all.\n" +
                "  Fire on timber  — siege engines and castle GATES burn: a cone\n" +
                "                    scorches them, a standing fire gnaws at them.\n" +
                "                    (The Ashen cold splits the frozen grain.)\n" +
                "  Horses and flame — no horse will hold a burning line.\n\n" +
                "── BATTLE COST  (life expectancy — flat per cast) ────\n" +
                "  Attack = 3 days of life   Wall = 4 days.\n" +
                "  The draw buys power, never a cheaper cast — the toll is flat.\n" +
                "  You are not aged here and now; you simply die sooner (watch\n" +
                "  the death age in the Ledger above). Mage lords pay the same.\n" +
                "  The Nature discipline halves the toll; Steel lets you cast\n" +
                "  with a weapon drawn and bears the armour weight.\n" +
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
                "  Open this book any time: Left Alt + X  (LB + RB on a controller).\n\n" +
                "── DARK ALTARS ──────────────────────────────────────────\n" +
                "  Dark Altars stand in Sanala, Askar, Iyakis, Hubyar,\n" +
                "  and three random Ashen cities.\n" +
                "  The Merciless and Devious may purchase permanent Dark Gifts\n" +
                "  by sacrificing prisoners and captured lords.\n" +
                "  Dark Gifts are always active while you remain cruel or cunning.\n" +
                "  They block Grace (Sanctuary) and Nature (Living Ember) magic.\n" +
                "  Visit a Dark Altar at any time to renounce a gift.\n" +
                DarkGiftSystem.BuildGiftSummary() +
                BuildMurmursSection() +
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
                    () => { _deferredInquiry = MagicLearning.ShowCodex; }
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
                    () => { _deferredInquiry = MagicLearning.ShowCodex; }
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
                + "── CRYSTALS ─────────────────────────────────────────\n"
                + "  Equip a crystal in a weapon slot (Sunstone, Embershard, Rimeshard,\n"
                + "  Veilstone, Stormcrystal, Duskstone). Strike with it between 06:00\n"
                + "  and 20:00 to charge for 2 s then unleash the effect. 10 % burndown\n"
                + "  chance per use. Chambers in Revyl, Varcheg, Dunglanys, Car Banseth,\n"
                + "  and Saneopa.\n\n"
                + "── THE LIVING EMBER — the land's own fire ───────────\n"
                + "  (For those attuned to the living world, not the inner fire.)\n"
                + "  Gather a charge     Hold Left Ctrl + stand still   (hold R3, stand still)\n"
                + "  Cast attack         Hold Ctrl + Attack             (Right Trigger)\n"
                + "  Cast support        Hold Ctrl + Block              (Left Trigger)\n"
                + "  Both hands must be empty. Armour weight must not exceed 25.\n"
                + "  The element is the land's: Wind, Earth, Water, or Storm.\n\n"
                + "The grimoire holds the deeper craft of the Fire. "
                + "The rest, you will learn by surviving.";

            InformationManager.ShowInquiry(new InquiryData(
                "The Disciplines of Hand and Voice",
                body, true, false, "I will remember.", "", null, null), true, true);
        }

        // A short pointer shown at campaign start instead of the full codex above —
        // the gestures now live permanently in the journal ("Notes for the
        // Adventurer"), so a brief nudge there is less intrusive than the manual.
        public static void ShowControlsPointer()
        {
            InformationManager.ShowInquiry(new InquiryData(
                "The Disciplines of Hand and Voice",
                "Every gesture you will need — spells, miracles, crystals and the living ember — "
                + "is recorded in your journal under \"Notes for the Adventurer.\"\n\n"
                + "Open your journal whenever the craft slips your mind.",
                true, false, "I will remember.", "", null, null), true, true);
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

            // The unified magic: Fire is innate; every other element the player has
            // learned grants its own campaign-map working. Each is cast through the
            // memory-rite (ElementSpellMinigame), exactly as the old fire spells were.
            var known = MageElementKnowledge.KnownElements().ToList();
            if (known.Count == 0)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "No workings known.",
                    Color.FromUint(0xFFAAAAAA)));
                return;
            }

            var elements = known.Select(el => new InquiryElement(
                (int)el,
                ElementMapSpells.Name(el),
                null, true,
                ElementMapSpells.Lore(el)
            )).ToList();

            // Only promise Resonance to those who actually carry it (Sorcerer).
            bool hasResonance = TalentSystem.Has(TalentId.Sorcerer);
            string resonanceNote = hasResonance
                ? (TalentSystem.DailyCastCount == 0
                    ? " Resonance spares your first working today."
                    : " Resonance may spare you once in four.")
                : "";
            string castDesc = _isAshen
                ? "Choose a working. Costs criminal rating instead of years." +
                  (TalentSystem.DailyCastCount > 0 ? " After your first working today, further casts risk possession." : "")
                : TalentSystem.DailyCastCount == 0
                    ? "Choose a working. Each costs 1 day." + resonanceNote
                    : $"Choose a working. Working #{TalentSystem.DailyCastCount + 1} today — costs {TalentSystem.GetDailyCastCost()} days." + resonanceNote;

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
                        var el = (MagicElement)(int)chosen[0].Identifier;
                        _deferredInquiry = () => ElementSpellMinigame.Begin(el);
                    }
                },
                null, "", false
            ), false, true);
        }

        // ── Rite talent menu (shown by Altar / Sanctuary / Crystalline Chamber) ──

        public static void ShowRiteTalentMenu(string systemName, IEnumerable<TalentId> talentIds)
        {
            var ids = new HashSet<TalentId>(talentIds);
            var elements = new List<InquiryElement>();

            foreach (var d in TalentSystem.All)
            {
                if (!ids.Contains(d.Id)) continue;
                bool   owned      = TalentSystem.Has(d.Id);
                bool   selectable = !owned;
                int    talentCost = d.FocusCost > 0 ? d.FocusCost : TalentSystem.PurchaseCost();
                string check = owned ? "✓ " : "   ";
                string label = $"{check}◈  {d.Name}";
                string costHint = $"Cost: {talentCost} focus point{(talentCost != 1 ? "s" : "")}";
                string hint  = $"【 {d.Name} 】  rite\n\n" +
                               $"{d.MechanicDesc}\n\n" +
                               $"{d.Lore}\n\n" +
                               (owned ? "— Already known —" : costHint);
                elements.Add(new InquiryElement((int)d.Id, label, null, selectable, hint));
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                $"Rites — {systemName}",
                "Rites transform your mastery of this discipline. The focus cost is shown beside each.",
                elements, true, 0, 1,
                "Learn", "Close",
                chosen =>
                {
                    if (chosen?.Count > 0)
                    {
                        int id = (int)chosen[0].Identifier;
                        _deferredInquiry = () => TalentSystem.TryPurchase((TalentId)id, Hero.MainHero);
                    }
                },
                null, "", false
            ), false, true);
        }

        // ── Whisper murmurs ───────────────────────────────────────────────────
        // At Tier 3 (75+ whispers) the cold shares intelligence the player
        // couldn't otherwise know: Ashen footholds, failing kingdoms, lingering
        // darkness. Each fragment is derived from live world state, not scripted.

        private static string BuildMurmursSection()
        {
            if (WhisperTier < 3) return "";
            try
            {
                if (Campaign.Current == null) return "";
                var lines = new List<string>();

                try
                {
                    int ashenTowns = Settlement.All
                        .Count(s => s.IsTown && s.MapFaction?.StringId == "ashen_kingdom");
                    if (ashenTowns > 0)
                    {
                        var ashenKingdom = Kingdom.All
                            .FirstOrDefault(k => k.StringId == "ashen_kingdom" && !k.IsEliminated);
                        string anchor = "";
                        if (ashenKingdom != null)
                        {
                            var heaviest = ashenKingdom.Settlements
                                .Where(s => s.IsTown && s.Town != null)
                                .OrderByDescending(s => s.Town.Prosperity)
                                .FirstOrDefault();
                            if (heaviest != null) anchor = $" Their weight is greatest near {heaviest.Name}.";
                        }
                        lines.Add($"  The grey hold {ashenTowns} settlement{(ashenTowns != 1 ? "s" : "")}.{anchor}");
                    }
                }
                catch { }

                try
                {
                    var thin = Kingdom.All
                        .Where(k => !k.IsEliminated && k.StringId != "ashen_kingdom")
                        .OrderBy(k => k.Settlements.Count(s => s.IsTown))
                        .FirstOrDefault();
                    if (thin != null)
                    {
                        int n = thin.Settlements.Count(s => s.IsTown);
                        if (n <= 3)
                            lines.Add($"  {thin.Name} grows thin — {n} town{(n != 1 ? "s" : "")} left. Their hall burns fewer torches each season.");
                    }
                }
                catch { }

                try
                {
                    if (CampaignMapEvents.IsLongNight())
                        lines.Add("  The darkness has not lifted. What the Night called has not all gone back.");
                }
                catch { }

                if (lines.Count == 0) return "";
                return "\n── MURMURS  (the cold speaks plainly now) ──────────\n" +
                       string.Join("\n", lines) + "\n";
            }
            catch { return ""; }
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
            store.SyncData("LDM_KnownMage",      ref _isKnownMage);
            store.SyncData("LDM_DreamCD",        ref _dreamCooldown);
            store.SyncData("LDM_DreamIdx",       ref _dreamLastIdx);
            TalentSystem.Save(store);

            _giftedChildIds.Clear();
            if (giftedList != null)
                foreach (var id in giftedList) _giftedChildIds.Add(id);
        }
    }
}
