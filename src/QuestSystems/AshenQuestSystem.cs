// =============================================================================
// ASH AND EMBER — AshenQuestSystem.cs
// The Hunger of the Void — Ashen player campaign goal.
//
// Trigger : Ashen player executes a prisoner lord for the first time.
//
// Sequence
//   1. "The Silence After"   — two-part Hunger vision (accept or refuse).
//   2. Prereq goals          — Clan Tier 6 + capture Epicrotea.
//   3. Wasteland Rite        — unlocked after prereqs; cast via town menu.
//   4. Seven capitals        — consecrate all seven with the Wasteland Rite.
//   5. Finale                — mage lords die; the world goes dark.
//                              Player hero dies (game-over screen).
//
// Seven target capitals
//   Pravend, Epicrotea, Pen Cannoc, Husn Fulq, Quyaz, Sargot, Marunath
//   (Epicrotea is also the prereq conquest target — capture it, then consecrate it.)
//
// Wasteland Rite effects
//   · The settlement is marked as a Wasteland (tracked in _wastelandCities).
//   · HasAshenAltar() returns true for Wasteland cities — altar rites become
//     available there automatically.
//   · All bound villages are permanently looted.
//   · If the settlement is one of the seven capitals, _capitalsCount increments.
//
// Save keys
//   LDM_VoidPhase        int
//   LDM_VoidPreG1        bool   (Tier 6)
//   LDM_VoidPreG2        bool   (Epicrotea)
//   LDM_VoidCapCount     int    (capitals wastelanified)
//   LDM_VoidFrozen       bool
//   LDM_VoidEndPhase     int
//   LDM_VoidWastelandIds List<string>
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public static class AshenQuestSystem
    {
        // ── Quest phases ──────────────────────────────────────────────────────
        private const int PhaseIdle         = 0;
        private const int PhaseHungerReady  = 1;  // execution happened, vision 1 pending
        private const int PhaseHunger2Ready = 2;  // vision 1 accepted, vision 2 pending
        private const int PhasePrereqs      = 3;  // working toward tier 6 + Epicrotea
        private const int PhaseWasteland    = 4;  // rite unlocked, 7 capitals needed
        private const int PhaseAllDone      = 5;  // 7 capitals done, finale pending
        private const int PhaseFrozen       = 6;  // ending triggered
        private const int PhaseFailed       = 7;

        private static int  _phase          = PhaseIdle;
        private static bool _prereqGoal1    = false; // Clan Tier 6
        private static bool _prereqGoal2    = false; // Epicrotea captured
        private static int  _capitalsCount  = 0;
        private static bool _worldFrozen    = false;
        private static int  _endingPhase    = 0;
        internal static AshenQuestLog _questLog = null;

        private static readonly HashSet<string> _wastelandCities = new HashSet<string>();
        private static readonly Random _rng = new Random();

        // ── Tuning ────────────────────────────────────────────────────────────
        public const int    TargetClanTier    = 6;
        public const int    RequiredCapitals  = 7;
        public const string EpicroteaMarker   = "Epicrotea";

        public static readonly string[] TargetCapitalNames =
        {
            "Pravend",    // Vlandia
            "Epicrotea",  // Northern Empire — also the prereq capture target
            "Pen Cannoc", // Battania
            "Husn Fulq",  // Southern Empire
            "Quyaz",      // Aserai / Khuzait
            "Sargot",     // Western Empire
            "Marunath",   // Battanian / Northern Empire (reassigned in mod)
        };

        // ── Public accessors ──────────────────────────────────────────────────
        public static bool IsActive           => _phase >= PhasePrereqs && _phase < PhaseFrozen;
        public static bool IsDone             => _phase == PhaseFrozen || _phase == PhaseFailed;
        public static bool WorldFrozen        => _worldFrozen;
        public static bool IsWastelandUnlocked => _phase == PhaseWasteland
                                               || _phase == PhaseAllDone
                                               || _phase == PhaseFrozen;
        public static int  CapitalsCount      => _capitalsCount;

        public static bool IsWastelandCity(string settlementId)
            => !string.IsNullOrEmpty(settlementId) && _wastelandCities.Contains(settlementId);

        public static bool IsTargetCapital(string cityName)
        {
            if (string.IsNullOrEmpty(cityName)) return false;
            return TargetCapitalNames.Any(n =>
                cityName.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // ── Called from CampaignBehavior.OnHeroKilled ─────────────────────────
        public static void OnHeroExecuted(Hero victim)
        {
            if (_phase != PhaseIdle) return;
            if (!MageKnowledge.IsAshen) return;
            if (victim == null || !victim.IsLord || victim.IsChild) return;
            _phase = PhaseHungerReady;
            if (MageKnowledge._deferredInquiry == null)
                MageKnowledge._deferredInquiry = ShowHungerVision1;
        }

        // ── Called from CampaignBehavior.OnDailyTick ─────────────────────────
        public static void DailyTick()
        {
            if (_endingPhase > 0)
            {
                if (_endingPhase < 5) TickEnding();
                return;
            }

            if (_worldFrozen) return;
            if (!MageKnowledge.IsAshen) return;

            // Flush pending hunger visions
            if (_phase == PhaseHunger2Ready && MageKnowledge._deferredInquiry == null)
            {
                MageKnowledge._deferredInquiry = ShowHungerVision2;
                return;
            }

            if (_phase != PhasePrereqs && _phase != PhaseAllDone) return;

            if (_phase == PhasePrereqs)
            {
                if (_questLog == null) try { EnsureQuestLog(); } catch { }
                CheckPrereqGoals();
            }
            else if (_phase == PhaseAllDone && MageKnowledge._deferredInquiry == null)
            {
                MageKnowledge._deferredInquiry = ShowFinalPrompt;
            }
        }

        // ── Prereq goal checks ────────────────────────────────────────────────
        private static void CheckPrereqGoals()
        {
            try
            {
                if (!_prereqGoal1 && (Hero.MainHero?.Clan?.Tier ?? 0) >= TargetClanTier)
                {
                    _prereqGoal1 = true;
                    try { _questLog?.LogPrereq1(); } catch { }
                    if (MageKnowledge._deferredInquiry == null)
                        MageKnowledge._deferredInquiry = ShowPrereqGoalComplete1;
                }
            }
            catch { }
            try
            {
                if (!_prereqGoal2)
                {
                    var epicrotea = Settlement.All.FirstOrDefault(s =>
                        s.Name.ToString().IndexOf(EpicroteaMarker, StringComparison.OrdinalIgnoreCase) >= 0
                        && (s.IsTown || s.IsCastle));
                    if (epicrotea != null && epicrotea.OwnerClan == Hero.MainHero?.Clan)
                    {
                        _prereqGoal2 = true;
                        try { _questLog?.LogPrereq2(); } catch { }
                        if (MageKnowledge._deferredInquiry == null)
                            MageKnowledge._deferredInquiry = ShowPrereqGoalComplete2;
                    }
                }
            }
            catch { }
            try
            {
                if (_prereqGoal1 && _prereqGoal2)
                    UnlockWastelandRite();
            }
            catch { }
        }

        private static void UnlockWastelandRite()
        {
            _phase = PhaseWasteland;
            try { _questLog?.LogWastelandUnlocked(); } catch { }
            InformationManager.DisplayMessage(new InformationMessage(
                "The Wasteland Rite is revealed. Visit an Ashen-owned city to consecrate it.",
                new Color(0.4f, 0.5f, 0.85f)));
            InformationManager.DisplayMessage(new InformationMessage(
                $"Consecrate {RequiredCapitals} capitals. Epicrotea awaits the rite first.",
                new Color(0.35f, 0.45f, 0.80f)));
        }

        // ── The Silence After (vision 1) ──────────────────────────────────────
        private static void ShowHungerVision1()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "The Silence After",

                    "The moment the lord's fire goes out, you feel something you have not felt before.\n\n" +
                    "Not satisfaction. Not power. Something older.\n\n" +
                    "The space where their flame was — it is not empty. " +
                    "Something is there that was there before the flame, waiting. " +
                    "It was always there, behind every fire that was ever extinguished. " +
                    "You could feel it as cold. But it is not cold. " +
                    "Cold is just what it feels like from the outside.\n\n" +
                    "From the inside, it is absence made complete. " +
                    "A void that has been growing for ages and has finally found something to look through.\n\n" +
                    "It is looking through you now.\n\n" +
                    "And it is showing you something.",

                    true, true,
                    "Look into it.",
                    "Shut it out.",

                    () =>
                    {
                        _phase = PhaseHunger2Ready;
                    },
                    () =>
                    {
                        _phase = PhaseFailed;
                        InformationManager.DisplayMessage(new InformationMessage(
                            "You closed the door on it. It will not knock again.",
                            new Color(0.5f, 0.5f, 0.55f)));
                    }
                ), true, true);
            }
            catch { }
        }

        // ── The Hunger Speaks (vision 2) ──────────────────────────────────────
        private static void ShowHungerVision2()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "The Hunger",

                    "It shows you a city.\n\n" +
                    "Not burning — the opposite. Every fire in it extinguished at once, " +
                    "as if the city itself exhaled and did not breathe in again. " +
                    "The streets are still. The people are still. " +
                    "Not dead — emptied. Preserved in the grey permanence the cold has always offered.\n\n" +
                    "This is the Wasteland Rite. " +
                    "Not a spell, not a ritual — a consecration. " +
                    "When the cold has been given enough, a city can be remade in its image. " +
                    "The warmth does not survive it. The altars rise on their own.\n\n" +
                    "The void shows you which cities must answer.\n\n" +
                    "Seven of them. The beating hearts of the warm world — " +
                    "Pravend, Epicrotea, Pen Cannoc, Husn Fulq, Quyaz, Sargot, Marunath. " +
                    "Capitals. Centres. The places the warm world has built its faith around.\n\n" +
                    "To reach the Wasteland Rite, you must first establish your dominion — " +
                    "Clan Tier 6 — and claim Epicrotea. Stand in its great hall. " +
                    "Let the cold settle there. The rite will come to you.\n\n" +
                    "Then: city by city, until all seven stand emptied.\n\n" +
                    "The void has been waiting a very long time for someone who could carry this.\n\n" +
                    "It believes, with something that is not hope but functions like it, that you can.",

                    true, true,
                    "I will carry it.",
                    "This is not what I chose.",

                    () =>
                    {
                        _phase = PhasePrereqs;
                        try { _questLog = new AshenQuestLog(); _questLog.StartQuest(); _questLog.LogStarted(); } catch { }
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Quest added: The Hunger of the Void.",
                            new Color(0.38f, 0.50f, 0.85f)));
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"Goals: Clan Tier {TargetClanTier}  ·  Capture Epicrotea  ·  Then: consecrate 7 capitals.",
                            new Color(0.35f, 0.45f, 0.80f)));
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Check quest progress in the Grimoire (Alt+X).",
                            new Color(0.35f, 0.45f, 0.75f)));
                    },
                    () =>
                    {
                        _phase = PhaseFailed;
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The void withdraws. Something that was waiting closes. " +
                            "It will not show you this again.",
                            new Color(0.5f, 0.5f, 0.55f)));
                    }
                ), true, true);
            }
            catch { }
        }

        // ── Prereq goal pop-ups ───────────────────────────────────────────────
        private static void ShowPrereqGoalComplete1()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Cold Dominion",

                    "Your name reaches every corner of Calradia now. " +
                    "Lords who built walls against the cold send emissaries instead. " +
                    "They are afraid — which means they are already part of the way there.\n\n" +
                    "The first condition is met. Epicrotea remains.",

                    true, false, "Calradia is listening.", "",
                    () => { }, () => { }
                ), true, true);
            }
            catch { }
        }

        private static void ShowPrereqGoalComplete2()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "The Warm Heart",

                    "You stand in Epicrotea. The fires they kept burning here — " +
                    "the great torches, the hearths of the ancient court — " +
                    "flicker as you walk its halls.\n\n" +
                    "The void stirs. You can feel what Epicrotea is to the warm world: " +
                    "a centre, a symbol, a place that has believed in itself for a very long time.\n\n" +
                    "The Wasteland Rite is here now. You can feel it waiting, " +
                    "the way you felt the cold waiting behind every flame you have ever snuffed.\n\n" +
                    "The second condition is met. " +
                    "Visit any Ashen-owned city and look for the Wasteland Rite.",

                    true, false, "Begin the consecrations.", "",
                    () => { }, () => { }
                ), true, true);
            }
            catch { }
        }

        // ── Wasteland Rite (called from AshenAltarsCampaignBehavior) ──────────
        public static void ShowWastelandRiteDialog(Settlement settlement)
        {
            if (settlement == null) return;
            try
            {
                string cityName   = settlement.Name?.ToString() ?? "this city";
                bool   isCapital  = IsTargetCapital(cityName);
                string capitalNote = isCapital
                    ? $"\n\nThis is one of the seven. The void hungers for it. [{_capitalsCount}/{RequiredCapitals} claimed]"
                    : $"\n\nThis is not one of the seven capitals, but the cold will take anything offered. [{_capitalsCount}/{RequiredCapitals} capitals]";

                InformationManager.ShowInquiry(new InquiryData(
                    "The Wasteland Rite",

                    $"You stand at the centre of {cityName}. " +
                    "The cold within you reaches out — not to warm, but to void.\n\n" +
                    "The Wasteland Rite does not destroy. It hollows. " +
                    "Every fire in this city goes out at once. The villages fall silent. " +
                    "The stone remembers nothing but cold.\n\n" +
                    "What remains will serve the grey march permanently. " +
                    "An altar will rise here, as it does in all true Ashen cities. " +
                    "The people who remain will serve, or they will not remain." +
                    capitalNote,

                    true, true,
                    "Consecrate it. Let the cold have it.",
                    "Not yet.",

                    () => { OnWastelandRiteConfirmed(settlement); },
                    () => { }
                ), true, true);
            }
            catch { }
        }

        private static void OnWastelandRiteConfirmed(Settlement settlement)
        {
            if (settlement == null) return;
            try
            {
                _wastelandCities.Add(settlement.StringId);

                // Permanently loot all bound villages
                try
                {
                    foreach (Settlement v in Settlement.All)
                    {
                        if (!v.IsVillage || v.Village?.Bound != settlement) continue;
                        try { v.Village.VillageState = Village.VillageStates.Looted; } catch { }
                        try { v.Village.Hearth = 1f; } catch { }
                    }
                }
                catch { }

                // Lock town stats
                try
                {
                    if (settlement.Town != null)
                    {
                        settlement.Town.Loyalty  = 100f;
                        settlement.Town.Security = 100f;
                    }
                }
                catch { }

                bool isCapital = IsTargetCapital(settlement.Name?.ToString() ?? "");
                if (isCapital)
                {
                    _capitalsCount++;
                    try { _questLog?.LogCapital(settlement.Name?.ToString() ?? "", _capitalsCount); } catch { }
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{settlement.Name} — consecrated to the void. [{_capitalsCount}/{RequiredCapitals} capitals]",
                        new Color(0.4f, 0.5f, 0.9f)));

                    if (_capitalsCount >= RequiredCapitals && _phase == PhaseWasteland)
                    {
                        _phase = PhaseAllDone;
                        try { _questLog?.LogAllDone(); } catch { }
                        if (MageKnowledge._deferredInquiry == null)
                            MageKnowledge._deferredInquiry = ShowFinalPrompt;
                    }
                }
                else
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{settlement.Name} — consecrated. The void grows.",
                        new Color(0.38f, 0.50f, 0.80f)));
                }
            }
            catch { }
        }

        // ── Final prompt ──────────────────────────────────────────────────────
        private static void ShowFinalPrompt()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "The Seven Are Done",

                    "All seven capitals stand consecrated.\n\n" +
                    "The void has what it asked for — " +
                    "the warm world's centres, hollowed, remade, " +
                    "altars rising in the silence where the fires were.\n\n" +
                    "One act remains. " +
                    "The mage-lords still carry the last fires in Calradia — " +
                    "the wandering gifted, the warm-blooded lords who refused the cold. " +
                    "The void will move through you and extinguish them all at once. " +
                    "Not destruction. Completion.\n\n" +
                    "What comes after is permanent. " +
                    "The world will not end. It will simply stop burning.\n\n" +
                    "The void has been patient for a very long time.\n\n" +
                    "It is ready now. So are you.",

                    true, true,
                    "Finish it. Let the cold have everything.",
                    "Not like this. Not yet.",

                    () =>
                    {
                        _phase       = PhaseFrozen;
                        _endingPhase = 1;
                        try { _questLog?.LogComplete(); } catch { }
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The cold moves through you. There is no calling it back.",
                            new Color(0.4f, 0.5f, 0.9f)));
                    },
                    () =>
                    {
                        _phase = PhaseFailed;
                        try { _questLog?.LogFailed(); } catch { }
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The void withdraws. It has outlasted everything before you. " +
                            "It will outlast your hesitation too.",
                            new Color(0.5f, 0.5f, 0.55f)));
                    }
                ), true, true);
            }
            catch { }
        }

        // ── Ending sequence ───────────────────────────────────────────────────
        // Phase 1: Set frozen, kill mage lords (up to 5).
        // Phase 2: Kill remaining mage lords (up to 10).
        // Phase 3: Kill remaining mage lords + mage companions.
        // Phase 4: Final dialog → player dies.
        private static void TickEnding()
        {
            try
            {
                switch (_endingPhase)
                {
                    case 1:
                        _worldFrozen = true;
                        KillMageLords(5);
                        _endingPhase = 2;
                        break;

                    case 2:
                        KillMageLords(10);
                        _endingPhase = 3;
                        break;

                    case 3:
                        KillMageLords(20);
                        KillMageCompanions();
                        _endingPhase = 4;
                        break;

                    case 4:
                        _endingPhase = 5;
                        if (MageKnowledge._deferredInquiry == null)
                            MageKnowledge._deferredInquiry = ShowEndingDialog;
                        break;
                }
            }
            catch { }
        }

        private static void KillMageLords(int cap)
        {
            int killed = 0;
            try
            {
                foreach (Hero h in Hero.AllAliveHeroes.ToList())
                {
                    if (killed >= cap) break;
                    if (!h.IsAlive || h.IsChild || h == Hero.MainHero) continue;
                    if (!ColourLordRegistry.IsColourLord(h)) continue;
                    if (ColourLordRegistry.IsAshenLord(h)) continue;
                    try { KillCharacterAction.ApplyByMurder(h, null, false); killed++; }
                    catch { }
                }
            }
            catch { }
        }

        private static void KillMageCompanions()
        {
            try
            {
                var roster = MobileParty.MainParty?.MemberRoster;
                if (roster == null) return;
                foreach (var entry in roster.GetTroopRoster().ToList())
                {
                    Hero companion = entry.Character?.HeroObject;
                    if (companion == null || companion == Hero.MainHero) continue;
                    if (!ColourLordRegistry.IsColourLord(companion)) continue;
                    if (ColourLordRegistry.IsAshenLord(companion)) continue;
                    try { KillCharacterAction.ApplyByMurder(companion, null, false); } catch { }
                }
            }
            catch { }
        }

        // ── Final dialog ──────────────────────────────────────────────────────
        private static void ShowEndingDialog()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "The Void Complete",

                    "The last fire goes out.\n\n" +
                    "Not violently. Not with struggle. " +
                    "The warmth was always going to end — you simply made it certain.\n\n" +
                    "You feel it move through you, the void that has been waiting since the world was young. " +
                    "It does not take from you. It acknowledges you. " +
                    "Every mage whose fire shudders and releases in the same moment — " +
                    "you are briefly, perfectly aware of all of them. " +
                    "Bright recognitions, then silence.\n\n" +
                    "The world does not end. It settles.\n\n" +
                    "Somewhere a city wakes to grey light and altars where hearths used to be. " +
                    "Somewhere a child is born who will never know the burning wars. " +
                    "Somewhere the void holds everything it was promised — " +
                    "the seven capitals, the empty roads, the permanent grey morning — " +
                    "and the world, still and cold and finished, does not argue.\n\n" +
                    "The grey march has arrived.\n\n" +
                    "You were the last thing it needed.\n\n" +
                    "The void does not need you anymore.\n\n" +
                    "And so it keeps you.\n\nForever.",

                    true, false,
                    "It is done.",
                    "",
                    () =>
                    {
                        try { KillCharacterAction.ApplyByMurder(Hero.MainHero, null, true); }
                        catch { }
                    },
                    () => { }
                ), true, true);
            }
            catch { }
        }

        // ── Grimoire summary ──────────────────────────────────────────────────
        public static string GetGrimoireSummary()
        {
            switch (_phase)
            {
                case PhaseIdle:
                case PhaseHungerReady:
                case PhaseHunger2Ready:
                    return "";

                case PhaseFailed:
                    return "\nQuest Failed: The Hunger of the Void.\n" +
                           "The void closed. It will not show you this again.\n";

                case PhaseFrozen:
                    return "\nThe void is complete. The world is still.\n";

                case PhasePrereqs:
                {
                    string g1 = _prereqGoal1 ? "✓" : "○";
                    string g2 = _prereqGoal2 ? "✓" : "○";
                    return $"\nQuest: The Hunger of the Void\n" +
                           $"  {g1}  Cold dominion  (Clan Tier {TargetClanTier})\n" +
                           $"  {g2}  Claim the warm heart  (capture Epicrotea)\n" +
                           "  ○  Wasteland Rite  [locked until both conditions met]\n";
                }

                case PhaseWasteland:
                case PhaseAllDone:
                {
                    string done  = _phase == PhaseAllDone ? "\n  [All seven consecrated — awaiting final rite.]\n" : "";
                    string caps  = string.Join(", ", TargetCapitalNames.Select(n =>
                    {
                        bool consecrated = _wastelandCities.Any(id =>
                        {
                            var s = Settlement.All.FirstOrDefault(x => x.StringId == id);
                            return s != null && s.Name.ToString().IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0;
                        });
                        return consecrated ? $"[✓{n}]" : n;
                    }));
                    return $"\nQuest: The Hunger of the Void\n" +
                           $"  Consecrate the seven capitals  [{_capitalsCount}/{RequiredCapitals}]\n" +
                           $"  {caps}\n" +
                           done;
                }

                default:
                    return "";
            }
        }

        // ── Save / Load ───────────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            store.SyncData("LDM_VoidPhase",    ref _phase);
            store.SyncData("LDM_VoidPreG1",    ref _prereqGoal1);
            store.SyncData("LDM_VoidPreG2",    ref _prereqGoal2);
            store.SyncData("LDM_VoidCapCount", ref _capitalsCount);
            store.SyncData("LDM_VoidFrozen",   ref _worldFrozen);
            store.SyncData("LDM_VoidEndPhase", ref _endingPhase);

            var wList = _wastelandCities.ToList();
            store.SyncData("LDM_VoidWastelandIds", ref wList);
            _wastelandCities.Clear();
            if (wList != null) foreach (var id in wList) _wastelandCities.Add(id);
        }

        private static void EnsureQuestLog()
        {
            _questLog = new AshenQuestLog();
            _questLog.StartQuest();
            _questLog.LogCatchUp(_prereqGoal1, _prereqGoal2, _phase >= PhaseWasteland, _capitalsCount, _phase == PhaseAllDone);
        }

        public static void ResetForNewGame()
        {
            _phase         = PhaseIdle;
            _prereqGoal1   = false;
            _prereqGoal2   = false;
            _capitalsCount = 0;
            _worldFrozen   = false;
            _endingPhase   = 0;
            _questLog      = null;
            _wastelandCities.Clear();
        }
    }

    public sealed class AshenQuestLog : QuestBase
    {
        public AshenQuestLog()
            : base("ldm_ashen_quest", Hero.MainHero, CampaignTime.Never, 0) { }

        public override TextObject Title => new TextObject("The Hunger of the Void");
        public override bool IsRemainingTimeHidden => true;

        protected override void InitializeQuestOnGameLoad()
        {
            AshenQuestSystem._questLog = this;
        }

        protected override void RegisterEvents() { }
        protected override void SetDialogs() { }

        internal void LogStarted() =>
            AddLog(new TextObject(
                "The void has shown you its design — seven capitals consecrated with the Wasteland Rite. " +
                "First: establish dominion (Clan Tier 6) and claim Epicrotea."));

        internal void LogPrereq1() =>
            AddLog(new TextObject(
                "Clan Tier 6 reached. Cold dominion established. The first condition is met."));

        internal void LogPrereq2() =>
            AddLog(new TextObject(
                "Epicrotea taken. The warm heart has been entered. The second condition is met."));

        internal void LogWastelandUnlocked() =>
            AddLog(new TextObject(
                "The Wasteland Rite is revealed. Visit any Ashen-controlled city to consecrate it. " +
                $"Seven capitals must answer: {string.Join(", ", AshenQuestSystem.TargetCapitalNames)}."));

        internal void LogCapital(string name, int count) =>
            AddLog(new TextObject(
                $"{name} consecrated to the void. [{count}/{AshenQuestSystem.RequiredCapitals}] capitals done."));

        internal void LogAllDone() =>
            AddLog(new TextObject(
                "All seven capitals stand consecrated. The void is ready. The final rite awaits."));

        internal void LogComplete()
        {
            AddLog(new TextObject("The last fire goes out. The world settles into grey permanence."));
            CompleteQuestWithSuccess();
        }

        internal void LogFailed()
        {
            AddLog(new TextObject(
                "The void withdraws. It has outlasted everything before you. It will outlast your hesitation too."));
            CompleteQuestWithFail();
        }

        internal void LogCatchUp(bool pre1, bool pre2, bool wasteland, int capCount, bool allDone)
        {
            AddLog(new TextObject("The Hunger of the Void — quest resumed from save."));
            if (pre1) AddLog(new TextObject("Clan Tier 6 reached. Cold dominion established."));
            if (pre2) AddLog(new TextObject("Epicrotea taken. The Wasteland Rite revealed."));
            if (wasteland && capCount > 0)
                AddLog(new TextObject($"{capCount} of {AshenQuestSystem.RequiredCapitals} capitals consecrated."));
            if (allDone) AddLog(new TextObject("All seven capitals are done. The final rite awaits."));
        }
    }
}
