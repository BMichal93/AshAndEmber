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
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static class MageKnowledge
    {
        private static bool   _isMage            = false;
        private static bool   _isAshen          = false;

        // Deferred map-layer dialog queue. Historically a single Action slot:
        // when two systems queued a popup the same day, one was silently lost —
        // including main-quest beats. The property keeps the old field contract
        // (read = peek, assign = enqueue, assign null = pop) so the ~90 existing
        // call sites work unchanged, but unguarded writers now queue behind the
        // pending dialog instead of overwriting it. The flush drains one per tick.
        private static readonly Queue<Action> _inquiryQueue = new Queue<Action>();
        internal static Action _deferredInquiry
        {
            get => _inquiryQueue.Count > 0 ? _inquiryQueue.Peek() : null;
            set
            {
                if (value == null) { if (_inquiryQueue.Count > 0) _inquiryQueue.Dequeue(); }
                else _inquiryQueue.Enqueue(value);
            }
        }
        private static readonly HashSet<string> _giftedChildIds = new HashSet<string>();
        private static readonly Random _rng = new Random();

        // ── Whisper System ────────────────────────────────────────────────────
        // Tracks how deeply the cold has seeped into the player's fire.
        // Incremented by dark acts; decremented slowly by virtuous ones.
        // At 100+ the Cold Calls Your Name.
        private static int _whisperCount        = 0;
        private static int _coldCallCountdown   = 0;  // 0 = not pending
        private static int _daysSinceWhisperGain = 0; // quiet conduct lets the cold lose interest
        private static int _lastAmbientIdx      = -1;

        public static int WhisperCount => _whisperCount;

        // Whisper tier: 0 = quiet, 1 = 25+ (the cold has noticed), 2 = 50+
        // (the cold favours you), 3 = 75+ (the cold is close). Tiers pull both
        // ways: altar rites accelerate, sanctuary rites resist you.
        public static int WhisperTier =>
            _whisperCount >= 75 ? 3 : _whisperCount >= 50 ? 2 : _whisperCount >= 25 ? 1 : 0;

        public static void AddWhispers(int n)
        {
            if (n <= 0 || !_isMage) return;
            int tierBefore = WhisperTier;
            _whisperCount += n;
            _daysSinceWhisperGain = 0;
            if (_whisperCount >= 100 && _coldCallCountdown == 0)
                _coldCallCountdown = 7; // fires in 7 days
            if (WhisperTier > tierBefore)
                try { AnnounceWhisperTier(WhisperTier); } catch { }
        }

        public static void RemoveWhispers(int n)
        {
            _whisperCount = Math.Max(0, _whisperCount - n);
        }

        private static void AnnounceWhisperTier(int tier)
        {
            string msg = tier switch
            {
                1 => "Something at the edge of your fire has begun to listen. (The cold has noticed you.)",
                2 => "The whispers no longer wait for the dark. The grey altars will open faster for you now — and the sanctuary flame leans away. (The cold favours you.)",
                3 => "You catch yourself answering before they speak. The sanctuary flame gutters when you kneel. (The cold is very close.)",
                _ => null,
            };
            if (msg != null)
                InformationManager.DisplayMessage(new InformationMessage(msg, new Color(0.45f, 0.45f, 0.65f)));
        }

        private static readonly string[] _ambientWhispers =
        {
            "A voice in the wind says your name the way an old friend would. There is no one there.",
            "The campfire bends north for a moment. No wind blows.",
            "In the morning frost you find one set of footprints circling your tent. They end mid-stride.",
            "You wake with ash on your fingertips. Your fire burned clean last night.",
            "Someone in the column is humming a tune you have only heard in dreams. When you turn, the humming stops.",
        };

        public static void DailyWhisperTick()
        {
            if (!_isMage) return;

            if (_coldCallCountdown > 0)
            {
                _coldCallCountdown--;
                if (_coldCallCountdown == 0)
                    _deferredInquiry = ShowColdCallsEvent; // queued — never lost to a busy day
            }

            // Ambient flavour: once the cold has noticed (25+), it occasionally
            // speaks — rarely (tier/20 per day), never the same line twice in a
            // row, and one whisper in three carries something true: the bearing
            // of the nearest Ashen warband.
            try
            {
                if (!_isAshen && WhisperTier >= 1 && _rng.Next(20) < WhisperTier)
                {
                    string line = null;
                    if (_rng.Next(3) == 0) line = UsefulWhisper();
                    if (line == null)
                    {
                        int idx;
                        do { idx = _rng.Next(_ambientWhispers.Length); } while (idx == _lastAmbientIdx);
                        _lastAmbientIdx = idx;
                        line = _ambientWhispers[idx];
                    }
                    InformationManager.DisplayMessage(new InformationMessage(
                        line, new Color(0.45f, 0.45f, 0.65f)));
                }
            }
            catch { }

            // Passive decay: honourable, merciful players shed whispers slowly
            try
            {
                if (_whisperCount > 0 && Hero.MainHero != null)
                {
                    int mercy  = Hero.MainHero.GetTraitLevel(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultTraits.Mercy);
                    int honor  = Hero.MainHero.GetTraitLevel(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultTraits.Honor);
                    if (mercy + honor >= 2 && _rng.Next(7) == 0)
                        _whisperCount = Math.Max(0, _whisperCount - 1);
                }
            }
            catch { }

            // Possession strain heals with time.
            if (_possessionStrainDays > 0) _possessionStrainDays--;

            // Quiet-conduct decay: 10 clean days and the cold starts losing
            // interest — roughly 1 whisper every 3 days regardless of traits.
            // Whispers reflect recent conduct, not a permanent stain.
            try
            {
                _daysSinceWhisperGain++;
                if (_whisperCount > 0 && _daysSinceWhisperGain >= 10 && _rng.Next(3) == 0)
                    _whisperCount = Math.Max(0, _whisperCount - 1);
            }
            catch { }
        }

        // A whisper that is also intelligence: the compass bearing of the
        // nearest Ashen lord's warband. Returns null if none is in the field.
        private static string UsefulWhisper()
        {
            try
            {
                if (MobileParty.MainParty == null) return null;
                Vec2 pos = MobileParty.MainParty.GetPosition2D;
                Hero nearest = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && ColourLordRegistry.IsAshenLord(h)
                             && h.PartyBelongedTo != null)
                    .OrderBy(h => (h.PartyBelongedTo.GetPosition2D - pos).Length)
                    .FirstOrDefault();
                if (nearest?.PartyBelongedTo == null) return null;
                Vec2 d = nearest.PartyBelongedTo.GetPosition2D - pos;
                string dir = Math.Abs(d.y) > Math.Abs(d.x)
                    ? (d.y > 0 ? "north" : "south")
                    : (d.x > 0 ? "east" : "west");
                return $"The whisper is almost kind tonight. It says one of the cold ones rides to the {dir} of you. It does not say why it tells you.";
            }
            catch { return null; }
        }

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
            _isMage           = false;
            _isAshen          = false;
            _inquiryQueue.Clear();
            _whisperCount     = 0;
            _coldCallCountdown = 0;
            _daysSinceWhisperGain = 0;
            _lastAmbientIdx   = -1;
            _possessionStrainDays = 0;
            _giftedChildIds.Clear();
            TalentSystem.ResetForNewGame();
            ColourLordRegistry.ResetForNewGame();
            AshenCitySystem.ResetForNewGame();
            AgingSystem.ResetForNewGame();
            TempleCovenant.ResetForNewGame();
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
        // Two-strike rule: the first failed test does not kill — the cold gains
        // ground (wounded, morale loss, 21 days of strain). A second failure
        // while strained is death. One bad roll should hurt, not end a campaign.
        private static int _possessionStrainDays = 0;
        public static bool IsPossessionStrained => _possessionStrainDays > 0;

        public static void QueuePossessionEvent()
        {
            _deferredInquiry = ShowPossessionEvent;
        }

        private static void OnPossessionTestFailed(string failText)
        {
            if (_possessionStrainDays > 0)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    failText + " There was nothing left to hold it back. The cold claims you.",
                    new Color(0.3f, 0.35f, 0.7f)));
                try { TaleWorlds.CampaignSystem.Actions.KillCharacterAction.ApplyByOldAge(Hero.MainHero, true); } catch { }
                return;
            }
            _possessionStrainDays = 21;
            try { Hero.MainHero.HitPoints = Math.Min(Hero.MainHero.HitPoints, 5); } catch { }
            try { MobileParty.MainParty.RecentEventsMorale -= 20f; } catch { }
            InformationManager.DisplayMessage(new InformationMessage(
                failText + " You wake face-down in the ash, body broken, the cold a half-step closer. " +
                "If it turns on you again before your strength returns (21 days), it will not let go.",
                new Color(0.3f, 0.35f, 0.7f)));
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
                        $"Leadership test. Skill: {lSkill}. Success chance: {lPct}%." +
                        (IsPossessionStrained ? " You are still strained — failure now is death." : " Failure leaves you broken and strained, not dead.")),
                    new InquiryElement("athlete", "Drive it back — overwhelm it with your body.", null, true,
                        $"Athletics test. Skill: {aSkill}. Success chance: {aPct}%." +
                        (IsPossessionStrained ? " You are still strained — failure now is death." : " Failure leaves you broken and strained, not dead.")),
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
                            OnPossessionTestFailed("Your will breaks.");
                    }
                    else if (choice == "athlete")
                    {
                        if (_rng.NextDouble() < aChance)
                            InformationManager.DisplayMessage(new InformationMessage(
                                "You push it back. The cold recoils.", new Color(0.7f, 0.7f, 0.9f)));
                        else
                            OnPossessionTestFailed("Your body gives out.");
                    }
                },
                null, "", false
            ), false, true);
        }

        // ── The Cold Calls Your Name ──────────────────────────────────────────
        // Fires when WhisperCount reaches 100. After Resist or Bargain, whispers
        // drop and the event can fire again once they climb back to 100.
        private static void ShowColdCallsEvent()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "The Cold Calls Your Name",
                "Three pale figures stand at the crossroads. They wear no faces you recognise — but they know yours. " +
                "The ash in your blood has been speaking to them for a long time, and tonight they have come to collect.\n\n" +
                "You can feel the fire straining against them. It always has. But it has never strained this hard.",
                new List<InquiryElement>
                {
                    new InquiryElement("resist", "I will not hear it. Not tonight. Not ever.", null, true,
                        "Resist. −10 days. −30 whispers. They will return."),
                    new InquiryElement("bargain", "Hear them out. Give what they ask and walk away.", null, true,
                        "Bargain. −30 days. −60 whispers. They withdraw, satisfied — for now."),
                    new InquiryElement("accept", "The fire in me has always been theirs.", null, true,
                        "Accept the cold. Become Ashen."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    string choice = chosen?[0]?.Identifier as string ?? "resist";
                    if (choice == "resist")
                    {
                        AgingSystem.AgeHero(Hero.MainHero, 10);
                        RemoveWhispers(30);
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The figures recede into the dark. The fire holds — barely. They will return. −10 days, −30 whispers.",
                            new Color(0.7f, 0.6f, 0.8f)));
                    }
                    else if (choice == "bargain")
                    {
                        AgingSystem.AgeHero(Hero.MainHero, 30);
                        RemoveWhispers(60);
                        InformationManager.DisplayMessage(new InformationMessage(
                            "They take what they came for and step aside. The road ahead is clear — for now. −30 days, −60 whispers.",
                            new Color(0.5f, 0.4f, 0.6f)));
                    }
                    else
                    {
                        // Become Ashen
                        _isAshen = true;
                        _whisperCount = 0;
                        _coldCallCountdown = 0;
                        ApplyAshenAppearance(Hero.MainHero);
                        try { AshenCitySystem.OnPlayerBecameAshen(); } catch { }
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The fire goes out. Something older and colder fills the space where it was.",
                            new Color(0.3f, 0.35f, 0.7f)));
                    }
                },
                null, "", false
            ), false, true);
        }

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

            string stepFocus   = usingController ? "Hold LB"              : "Hold Left Alt";
            string stepShape   = usingController ? "Left stick ↑ ← → ↓"  : "W  A  D  S";
            string stepBreak   = usingController ? "Press L3"             : "Press X";
            string stepRelease = usingController ? "Release LB"           : "Release Left Alt";
            string openBook    = usingController ? "LB + RB"              : "Left Alt + X  (before any key is pressed)";

            string ashenNote = _isAshen
                ? "\n[Ashen] Each cast raises criminal rating instead of aging you. After your first working each day, further casts risk possession.\n"
                : "";

            string desc =
                AgingSystem.BuildLedgerText() +
                "── HOW TO CAST ──────────────────────────────────────\n" +
                $"  1. {stepFocus}  → enter Focus\n" +
                $"  2. {stepShape}  → shape the spell  (repeat for more power)\n" +
                $"  3. {stepBreak}  → Break: locks shape, enter power phase\n" +
                "  4. W = Sear / A = Force / D = Shred  (damage)   |   S → Heal\n" +
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
                $"  Open this page: {openBook}" +
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
