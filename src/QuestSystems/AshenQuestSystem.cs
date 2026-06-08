// =============================================================================
// ASH AND EMBER — AshenQuestSystem.cs
// The Final Silence — Ashen player campaign goal.
//
// Trigger : Ashen player defeats a mage lord's (non-Ashen ColourLord) party
//           for the first time.
// Event   : The deep cold speaks — a pressure older than any Ashen lord.
// Quest   : Active if player accepts the calling.
//
// Goals
//   1. Reach Clan Tier 6            (establish cold dominion)
//   2. Capture Pravend              (claim the warm heart)
//   3. Reach Hero Level 25          (master the cold's power)
//
// Completion → final prompt → extinguish the last warmth or refuse.
//
// Ending (Yes)
//   · All non-Ashen mage lords and mage companions die.
//   · The world goes cold and still.
//   · Player hero dies — Bannerlord game-over screen.
//
// Ending (No)  → quest fails, game continues normally.
//
// Save keys
//   LDM_AshenQPhase      int   0=idle 1=event-ready 2=active 3=all-done 4=frozen 5=failed
//   LDM_AshenQGoal1-3    bool
//   LDM_WorldFrozen      bool
//   LDM_AshenQEndPhase   int
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
        private const int PhaseIdle       = 0;
        private const int PhaseEventReady = 1;  // event pending, fires on next map tick
        private const int PhaseActive     = 2;  // quest running
        private const int PhaseAllDone    = 3;  // all goals met, final prompt pending
        private const int PhaseFrozen     = 4;  // ending triggered
        private const int PhaseFailed     = 5;  // player refused

        private static int  _phase       = PhaseIdle;
        private static bool _goal1Done   = false; // clan tier
        private static bool _goal2Done   = false; // Pravend capture
        private static bool _goal3Done   = false; // hero level
        private static bool _worldFrozen = false;
        private static int  _endingPhase = 0;     // 0=not started 1-4=in progress

        private static readonly Random _rng = new Random();

        // ── Tuning ────────────────────────────────────────────────────────────
        public const int    TargetClanTier  = 6;
        public const int    TargetHeroLevel = 25;
        public const string PravendMarker   = "Pravend";

        // ── Public accessors ──────────────────────────────────────────────────
        public static bool IsActive     => _phase == PhaseActive;
        public static bool IsAllDone    => _phase == PhaseAllDone;
        public static bool IsDone       => _phase == PhaseFrozen || _phase == PhaseFailed;
        public static bool WorldFrozen  => _worldFrozen;
        public static bool Goal1Done    => _goal1Done;
        public static bool Goal2Done    => _goal2Done;
        public static bool Goal3Done    => _goal3Done;

        // ── Called from CampaignBehavior.OnMapEventEnded ──────────────────────
        public static void OnMapEventEnded(MapEvent mapEvent)
        {
            if (_phase != PhaseIdle) return;
            if (!MageKnowledge.IsAshen) return;
            try
            {
                bool playerAttacker = mapEvent.AttackerSide?.Parties
                    .Any(p => p.Party == PartyBase.MainParty) == true;
                bool playerWon = (playerAttacker && mapEvent.WinningSide == BattleSideEnum.Attacker)
                              || (!playerAttacker && mapEvent.WinningSide == BattleSideEnum.Defender);
                if (!playerWon) return;

                var enemySide = playerAttacker ? mapEvent.DefenderSide : mapEvent.AttackerSide;
                if (enemySide == null) return;
                bool mageLordDefeated = false;
                foreach (var meparty in enemySide.Parties)
                {
                    Hero leader = meparty?.Party?.LeaderHero;
                    if (leader != null
                        && ColourLordRegistry.IsColourLord(leader)
                        && !ColourLordRegistry.IsAshenLord(leader))
                    { mageLordDefeated = true; break; }
                }
                if (!mageLordDefeated) return;

                _phase = PhaseEventReady;
            }
            catch { }
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

            if (_phase == PhaseEventReady && MageKnowledge._deferredInquiry == null)
            {
                _phase = PhaseIdle;
                MageKnowledge._deferredInquiry = ShowCallingEvent;
                return;
            }

            if (_phase != PhaseActive && _phase != PhaseAllDone) return;

            if (_phase == PhaseActive)
            {
                CheckGoals();
                if (_goal1Done && _goal2Done && _goal3Done && _phase == PhaseActive)
                {
                    _phase = PhaseAllDone;
                    if (MageKnowledge._deferredInquiry == null)
                        MageKnowledge._deferredInquiry = ShowFinalPrompt;
                }
            }
            else if (_phase == PhaseAllDone && MageKnowledge._deferredInquiry == null)
            {
                MageKnowledge._deferredInquiry = ShowFinalPrompt;
            }
        }

        // ── Goal checks ───────────────────────────────────────────────────────
        private static void CheckGoals()
        {
            try
            {
                if (!_goal1Done && (Hero.MainHero?.Clan?.Tier ?? 0) >= TargetClanTier)
                {
                    _goal1Done = true;
                    if (MageKnowledge._deferredInquiry == null)
                        MageKnowledge._deferredInquiry = () => ShowGoalComplete(1);
                }
            }
            catch { }
            try
            {
                if (!_goal2Done)
                {
                    var pravend = Settlement.All.FirstOrDefault(s =>
                        s.Name.ToString().IndexOf(PravendMarker, StringComparison.OrdinalIgnoreCase) >= 0
                        && (s.IsTown || s.IsCastle));
                    if (pravend != null && pravend.OwnerClan == Hero.MainHero?.Clan)
                    {
                        _goal2Done = true;
                        if (MageKnowledge._deferredInquiry == null)
                            MageKnowledge._deferredInquiry = () => ShowGoalComplete(2);
                    }
                }
            }
            catch { }
            try
            {
                if (!_goal3Done && (Hero.MainHero?.Level ?? 0) >= TargetHeroLevel)
                {
                    _goal3Done = true;
                    if (MageKnowledge._deferredInquiry == null)
                        MageKnowledge._deferredInquiry = () => ShowGoalComplete(3);
                }
            }
            catch { }
        }

        // ── The Calling event ─────────────────────────────────────────────────
        private static void ShowCallingEvent()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "The Calling",

                    "Something moves through you after the battle — not feeling, not thought, " +
                    "but a pressure from the cold itself. Older than the Ashen. " +
                    "Older than anyone who chose the cold.\n\n" +
                    "You have felt it before, briefly. Now it is close enough to speak.\n\n" +
                    "Not in words. In the absence of words. In the way a room goes quiet when something certain enters it.\n\n" +
                    "The mage you defeated carried a fire that will not relight. " +
                    "That fire fed a world that has burned too long — too bright, too wasteful, too warm. " +
                    "It never asked whether the warmth was wanted. It simply burned.\n\n" +
                    "The cold does not burn. It waits. It endures. " +
                    "It takes back everything that was taken.\n\n" +
                    "There is a way to finish it. Not a battle — a completion. " +
                    "Every flame in Calradia extinguished at once. " +
                    "The mage-lords who carry the last fire. The warm cities. " +
                    "The wandering gifted who will never know what they almost started.\n\n" +
                    "The cold has been patient for a very long time.\n\n" +
                    "It is asking you to be the last thing it needs.",

                    true, true,
                    "I hear it. Tell me the path.",
                    "The cold will have to wait.",

                    () =>
                    {
                        _phase = PhaseActive;
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Quest added: The Final Silence.",
                            new Color(0.35f, 0.45f, 0.75f)));
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"Goals: Clan Tier {TargetClanTier}  ·  Capture Pravend  ·  Reach Level {TargetHeroLevel}",
                            new Color(0.3f, 0.4f, 0.7f)));
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Check quest progress in the Grimoire (Alt+X).",
                            new Color(0.3f, 0.4f, 0.65f)));
                    },
                    () =>
                    {
                        _phase = PhaseFailed;
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The cold withdraws. It has waited before. It will wait again.",
                            new Color(0.5f, 0.5f, 0.55f)));
                    }
                ), true, true);
            }
            catch { }
        }

        // ── Goal completion pop-ups ───────────────────────────────────────────
        private static void ShowGoalComplete(int goal)
        {
            try
            {
                string title, body, button;
                switch (goal)
                {
                    case 1:
                        title = "Cold Dominion";
                        body  = "Your name reaches every corner of Calradia. Lords who built walls against the cold " +
                                "now negotiate with you instead. They are afraid, which means they are already half-gone.\n\n" +
                                "The cold said: *establish dominion.*\n\n" +
                                "What you have is more than dominion. It is inevitability.\n\n" +
                                "The first condition is met.";
                        button = "Calradia is listening.";
                        break;
                    case 2:
                        title = "The Warm Heart";
                        body  = "You stand in the great hall of Pravend. The fires they kept burning here — " +
                                "the great hearths, the torches, the eternal flames of their ancestors — " +
                                "have all gone out. Not by force. By consequence.\n\n" +
                                "The warmth this city held was deliberate. They chose it. " +
                                "Tended it. Believed it was proof that the cold had limits.\n\n" +
                                "You are the proof that it does not.\n\n" +
                                "The second condition is met.";
                        button = "The heart is cold now.";
                        break;
                    default:
                        title = "The Cold at Full Depth";
                        body  = "You have been through enough cold — enough endurance, enough silence, " +
                                "enough battles in the grey half-light — to have become something the cold " +
                                "trusts completely.\n\n" +
                                "The inner fire burned in you once. " +
                                "What burns now is its opposite: not absence, but presence. " +
                                "The cold is not nothing. It is everything the warmth refused to be.\n\n" +
                                "The third condition is met.";
                        button = "I am what remains.";
                        break;
                }

                InformationManager.ShowInquiry(new InquiryData(
                    title, body, true, false, button, "",
                    () => { }, () => { }
                ), true, true);
            }
            catch { }
        }

        // ── Final prompt ──────────────────────────────────────────────────────
        private static void ShowFinalPrompt()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "The Final Silence",

                    "It is possible now. All three conditions are met.\n\n" +
                    "To finish it, the cold would move through you and out — through every mage-lord " +
                    "still carrying the fire, every gifted wanderer, every warm thing still burning in Calradia. " +
                    "Not destruction. Completion.\n\n" +
                    "The world will not end. It will simply stop burning.\n\n" +
                    "What comes after is not nothing. It is permanence. " +
                    "The cold has always been more patient than the flame — " +
                    "and now it will have the world it was always going to have.\n\n" +
                    "The calling is complete. This is what it was asking.\n\n" +
                    "It will not ask again.",

                    true, true,
                    "Extinguish it all. Let the cold settle.",
                    "Not yet. Not like this.",

                    () =>
                    {
                        _phase       = PhaseFrozen;
                        _endingPhase = 1;
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The cold begins to move. There is no calling it back.",
                            new Color(0.4f, 0.5f, 0.85f)));
                    },
                    () =>
                    {
                        _phase = PhaseFailed;
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The cold withdraws. It will outlast your hesitation. It always does.",
                            new Color(0.5f, 0.5f, 0.55f)));
                    }
                ), true, true);
            }
            catch { }
        }

        // ── Ending sequence ───────────────────────────────────────────────────
        // Called from DailyTick when _endingPhase > 0.
        // Phase 1: Set frozen flag, kill mage lords (up to 5).
        // Phase 2: Kill remaining mage lords (up to 10).
        // Phase 3: Kill remaining mage lords + mage companions.
        // Phase 4: Show final dialog → player dies.
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
                        KillMageLords(20);   // finish remaining mage lords
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
                    if (ColourLordRegistry.IsAshenLord(h)) continue; // Ashen are spared
                    try
                    {
                        KillCharacterAction.ApplyByMurder(h, null, false);
                        killed++;
                    }
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

        // ── Final dialog before player death ─────────────────────────────────
        private static void ShowEndingDialog()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "The Cold Complete",

                    "The last fire goes out.\n\n" +
                    "Not violently. Not with struggle. " +
                    "The warmth was always going to exhaust itself — you simply helped it understand that.\n\n" +
                    "You feel it move through you, the cold that has been waiting since the world was young. " +
                    "It does not take from you. It completes you. " +
                    "Every mage whose fire shudders and releases in the same moment — " +
                    "you are aware of all of them. " +
                    "Brief flares of recognition, then nothing.\n\n" +
                    "The world does not end. It settles.\n\n" +
                    "The battles are done. The warmth is done. " +
                    "The grey march has arrived at the place it was always walking toward.\n\n" +
                    "Somewhere a city wakes to a morning with no fire in the rafters. " +
                    "Somewhere a child is born who will never know the burning wars. " +
                    "Somewhere the cold holds everything it was promised — " +
                    "and the world, still and grey and permanent, does not argue.\n\n" +
                    "You remain. But what you were is complete.\n\n" +
                    "The cold does not need you anymore.\n\nAnd so it keeps you.\n\nForever.",

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

        // ── Grimoire summary (called by MageKnowledge) ────────────────────────
        public static string GetGrimoireSummary()
        {
            if (_phase == PhaseIdle || _phase == PhaseEventReady)
                return "";

            if (_phase == PhaseFailed)
                return "\nQuest Failed: The Final Silence.\n" +
                       "The cold withdraws. It will wait.\n";

            if (_phase == PhaseFrozen || _worldFrozen)
                return "\nThe world is still. The cold is complete.\n";

            string g1 = _goal1Done ? "✓" : "○";
            string g2 = _goal2Done ? "✓" : "○";
            string g3 = _goal3Done ? "✓" : "○";

            string status = _phase == PhaseAllDone
                ? "\n  [All conditions met — awaiting your decision.]\n"
                : "";

            return $"\nQuest: The Final Silence\n" +
                   $"  {g1}  Establish cold dominion  (Clan Tier {TargetClanTier})\n" +
                   $"  {g2}  Claim the warm heart     (capture Pravend)\n" +
                   $"  {g3}  Master the cold's power  (Level {TargetHeroLevel})\n" +
                   status;
        }

        // ── Save / Load ───────────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            store.SyncData("LDM_AshenQPhase",    ref _phase);
            store.SyncData("LDM_AshenQGoal1",    ref _goal1Done);
            store.SyncData("LDM_AshenQGoal2",    ref _goal2Done);
            store.SyncData("LDM_AshenQGoal3",    ref _goal3Done);
            store.SyncData("LDM_WorldFrozen",    ref _worldFrozen);
            store.SyncData("LDM_AshenQEndPhase", ref _endingPhase);
        }

        public static void ResetForNewGame()
        {
            _phase       = PhaseIdle;
            _goal1Done   = false;
            _goal2Done   = false;
            _goal3Done   = false;
            _worldFrozen = false;
            _endingPhase = 0;
        }
    }
}
