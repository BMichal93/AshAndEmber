// =============================================================================
// ASH AND EMBER — DragonQuestSystem.cs
// The Last Flight of the Dragons — main campaign goal.
//
// Trigger : Player defeats an Ashen lord's party for the first time.
// Event   : A dying old mage approaches. Lore, then death.
// Quest   : Active if player does not disregard him.
//
// Goals
//   1. Reach Clan Tier 6            (grasp on the world)
//   2. Capture Tyal                 (enter the heart of darkness)
//   3. Reach Hero Level 25          (gain the power)
//
// Completion → final prompt → rekindle the world or refuse.
//
// Ending (Yes)
//   · All Ashen lords, mage lords, and mage companions die.
//   · All Ashen settlements distributed to other kingdoms.
//   · World map events disabled.
//   · Player hero dies — Bannerlord game-over screen.
//
// Ending (No)  → quest fails, game continues normally.
//
// Save keys
//   LDM_DragonPhase     int   0=idle 1=event-ready 2=active 3=all-done 4=rekindled 5=failed
//   LDM_DragonGoal1-3   bool
//   LDM_WorldRekindled  bool
//   LDM_EndingPhase     int
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
    public static class DragonQuestSystem
    {
        // ── Quest phases ──────────────────────────────────────────────────────
        private const int PhaseIdle       = 0;
        private const int PhaseEventReady = 1;  // event pending, fires on next map tick
        private const int PhaseActive     = 2;  // quest running
        private const int PhaseAllDone    = 3;  // all goals met, final prompt pending
        private const int PhaseRekindled  = 4;  // ending triggered
        private const int PhaseFailed     = 5;  // player refused

        private static int  _phase          = PhaseIdle;
        private static bool _goal1Done      = false; // clan tier
        private static bool _goal2Done      = false; // Tyal capture
        private static bool _goal3Done      = false; // hero level
        private static bool _worldRekindled = false;
        private static int  _endingPhase    = 0;     // 0=not started 1-4=in progress
        internal static DragonQuestLog _questLog = null;

        private static readonly Random _rng = new Random();

        // ── Tuning ────────────────────────────────────────────────────────────
        public const int TargetClanTier  = 6;
        public const int TargetHeroLevel = 25;
        public const string TyalMarker   = "Tyal"; // matched via IndexOf

        // ── Public accessors (read by MageKnowledge for grimoire display) ─────
        public static bool IsActive       => _phase == PhaseActive;
        public static bool IsAllDone      => _phase == PhaseAllDone;
        public static bool IsDone         => _phase == PhaseRekindled || _phase == PhaseFailed;
        public static bool WorldRekindled => _worldRekindled;
        public static bool Goal1Done      => _goal1Done;
        public static bool Goal2Done      => _goal2Done;
        public static bool Goal3Done      => _goal3Done;

        // ── Called from CampaignBehavior.OnMapEventEnded ──────────────────────
        public static void OnMapEventEnded(MapEvent mapEvent)
        {
            if (_phase != PhaseIdle) return;
            if (MageKnowledge.IsAshen) return;
            try
            {
                // Only fires if player won
                bool playerAttacker = mapEvent.AttackerSide?.Parties
                    .Any(p => p.Party == PartyBase.MainParty) == true;
                bool playerWon = (playerAttacker && mapEvent.WinningSide == BattleSideEnum.Attacker)
                              || (!playerAttacker && mapEvent.WinningSide == BattleSideEnum.Defender);
                if (!playerWon) return;

                // Check if any enemy hero is an Ashen lord
                var enemySide = playerAttacker ? mapEvent.DefenderSide : mapEvent.AttackerSide;
                if (enemySide == null) return;
                bool ashenLordDefeated = false;
                foreach (var meparty in enemySide.Parties)
                {
                    Hero leader = meparty?.Party?.LeaderHero;
                    if (leader != null && ColourLordRegistry.IsAshenLord(leader))
                    { ashenLordDefeated = true; break; }
                }
                if (!ashenLordDefeated) return;

                _phase = PhaseEventReady;
            }
            catch { }
        }

        // ── Called from CampaignBehavior.OnDailyTick ─────────────────────────
        public static void DailyTick()
        {
            // The ending sequence must complete even after _worldRekindled is set
            // (phase 1 sets the flag; phases 2-4 must still run on subsequent days).
            if (_endingPhase > 0)
            {
                if (_endingPhase < 5) TickEnding();
                return;
            }

            if (_worldRekindled) return;

            // Fire old man event (deferred so the UI is clean)
            if (_phase == PhaseEventReady && MageKnowledge._deferredInquiry == null)
            {
                _phase = PhaseIdle; // prevent re-trigger if player cancels the UI
                MageKnowledge._deferredInquiry = ShowOldManEvent;
                return;
            }

            if (_phase != PhaseActive && _phase != PhaseAllDone) return;

            // Check goals in active quest
            if (_phase == PhaseActive)
            {
                if (_questLog == null) try { EnsureQuestLog(); } catch { }
                CheckGoals();
                if (_goal1Done && _goal2Done && _goal3Done && _phase == PhaseActive)
                {
                    _phase = PhaseAllDone;
                    try { _questLog?.LogAllDone(); } catch { }
                    if (MageKnowledge._deferredInquiry == null)
                        MageKnowledge._deferredInquiry = ShowFinalPrompt;
                }
            }
            else if (_phase == PhaseAllDone && MageKnowledge._deferredInquiry == null)
            {
                // Re-show final prompt if somehow missed
                MageKnowledge._deferredInquiry = ShowFinalPrompt;
            }
        }

        // ── Goal checks ───────────────────────────────────────────────────────
        private static void CheckGoals()
        {
            try
            {
                // Goal 1 — Clan Tier 6
                if (!_goal1Done && (Hero.MainHero?.Clan?.Tier ?? 0) >= TargetClanTier)
                {
                    _goal1Done = true;
                    try { _questLog?.LogGoal1(); } catch { }
                    if (MageKnowledge._deferredInquiry == null)
                        MageKnowledge._deferredInquiry = () => ShowGoalComplete(1);
                }
            }
            catch { }
            try
            {
                // Goal 2 — Capture Tyal
                if (!_goal2Done)
                {
                    var tyal = Settlement.All.FirstOrDefault(s =>
                        s.Name.ToString().IndexOf(TyalMarker, StringComparison.OrdinalIgnoreCase) >= 0
                        && (s.IsTown || s.IsCastle));
                    if (tyal != null && tyal.OwnerClan == Hero.MainHero?.Clan)
                    {
                        _goal2Done = true;
                        try { _questLog?.LogGoal2(); } catch { }
                        if (MageKnowledge._deferredInquiry == null)
                            MageKnowledge._deferredInquiry = () => ShowGoalComplete(2);
                    }
                }
            }
            catch { }
            try
            {
                // Goal 3 — Hero Level 25
                if (!_goal3Done && (Hero.MainHero?.Level ?? 0) >= TargetHeroLevel)
                {
                    _goal3Done = true;
                    try { _questLog?.LogGoal3(); } catch { }
                    if (MageKnowledge._deferredInquiry == null)
                        MageKnowledge._deferredInquiry = () => ShowGoalComplete(3);
                }
            }
            catch { }
        }

        // ── Old man event ─────────────────────────────────────────────────────
        private static void ShowOldManEvent()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "The Last Ember",

                    "A very old man steps out from the shadow of a wall as you pass. He is not threatening, " +
                    "but he is deliberate. His eyes are the eyes of someone who has carried the fire for a very long time " +
                    "and has almost nothing left.\n\n" +
                    "\"You. The one who carries it. I have walked a long road to find you.\"\n\n" +
                    "He does not wait for a response.\n\n" +
                    "\"The world is going cold. The Ashen spread because no one is willing to spend themselves to stop them. " +
                    "I have outlived everyone I knew by forty years — mages who spent themselves on the wrong fires, " +
                    "lords who spent themselves on the wrong wars. None of it was enough.\"\n\n" +
                    "He steadies himself against the wall. His hands are shaking.\n\n" +
                    "\"There is a way to rekindle the world. One great burning — everything, at once. " +
                    "The one who carries it must give everything they have. Not some of it. All. " +
                    "Every mage's fire, every thread of the gift still woven through Calradia. " +
                    "The Ashen will crumble. The darkness will break. But the cost is everything.\"\n\n" +
                    "He looks at you with the patience of a man who has said this many times before and been refused.\n\n" +
                    "\"I have spent forty years looking for someone strong enough and willing enough. " +
                    "You may be neither. But you are the last one I will find.\"\n\n" +
                    "He sits down against the wall. His fire goes out quietly, " +
                    "like a candle that has burned everything it was given. " +
                    "By the time you reach him, he is cold.\n\n" +
                    "The question is whether you heard him.",

                    true, true,
                    "I heard him. Tell me the path.",
                    "I have no time for dying old men.",

                    () =>
                    {
                        // Accept — quest begins
                        _phase = PhaseActive;
                        try { _questLog = new DragonQuestLog(); _questLog.StartQuest(); _questLog.LogStarted(); } catch { }
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Quest added: The Last Flight of the Dragons.",
                            new Color(0.75f, 0.55f, 0.3f)));
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"Goals: Clan Tier {TargetClanTier}  ·  Capture Tyal  ·  Reach Level {TargetHeroLevel}",
                            new Color(0.65f, 0.5f, 0.25f)));
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Check quest progress in the Grimoire (Alt+X).",
                            new Color(0.6f, 0.5f, 0.25f)));
                    },
                    () =>
                    {
                        // Refuse — quest permanently unavailable; lore: "died with him"
                        _phase = PhaseFailed;
                        InformationManager.DisplayMessage(new InformationMessage(
                            "You walked away. Whatever he was offering died with him.",
                            new Color(0.5f, 0.5f, 0.5f)));
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
                        title = "The Forge of Power";
                        body  = "Your clan's name is known across the length of Calradia. " +
                                "Lords who would not have received you now send messengers. " +
                                "The fire you carry has become something the world takes seriously.\n\n" +
                                "The old man's words come back to you: *gain a grasp on the world.*\n\n" +
                                "You understand them now. The first condition is met.";
                        button = "The world is listening.";
                        break;
                    case 2:
                        title = "The Cold Heart";
                        body  = "You stand in the great hall of Tyal. The Ashen who built this place " +
                                "are gone, or cowed, or watching from the shadows.\n\n" +
                                "You feel it — the deep cold underneath everything here. " +
                                "The memory of what was done in this room. " +
                                "The long dark that the Ashen have been carrying west.\n\n" +
                                "You were inside the darkness. Now you know its dimensions.\n\n" +
                                "The second condition is met.";
                        button = "Now I understand the weight of it.";
                        break;
                    default:
                        title = "The Fire at Full Height";
                        body  = "You have been through enough battles, enough decisions, enough years " +
                                "to have changed beyond what you were. " +
                                "The inner fire burns different now — steadier, hotter, more certain of itself.\n\n" +
                                "The old man said *gain the power.* He meant this: " +
                                "the kind of strength that comes from having been tested " +
                                "enough times that you no longer flinch.\n\n" +
                                "The third condition is met.";
                        button = "I am ready.";
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
                    "The Last Light",

                    "It is possible now. All three conditions are met.\n\n" +
                    "To rekindle the world, you would pour everything — not just your own fire, " +
                    "but the fire of every mage still living, every thread of the gift " +
                    "woven through Calradia. They will not survive it. You will not survive it.\n\n" +
                    "But the cold will break.\n\n" +
                    "The Ashen will crumble. The darkness will retreat. " +
                    "And the world will have a morning it would not have had.\n\n" +
                    "The chance exists. It will not exist forever.\n\n" +
                    "This is what he was asking.",

                    true, true,
                    "Do it. Light everything.",
                    "Not like this. Not now.",

                    () =>
                    {
                        // Yes — begin ending sequence
                        _phase       = PhaseRekindled;
                        _endingPhase = 1;
                        try { _questLog?.LogComplete(); } catch { }
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The fire begins to move. There is no taking it back.",
                            new Color(0.9f, 0.65f, 0.15f)));
                    },
                    () =>
                    {
                        // No — quest fails
                        _phase = PhaseFailed;
                        try { _questLog?.LogFailed(); } catch { }
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The chance fleets. Whatever he offered is gone. The world turns as it will.",
                            new Color(0.5f, 0.5f, 0.55f)));
                    }
                ), true, true);
            }
            catch { }
        }

        // ── Ending sequence ───────────────────────────────────────────────────
        // Called from DailyTick when _endingPhase > 0.
        // Phase 1: Set rekindled flag, kill Ashen lords (up to 5).
        // Phase 2: Kill remaining Ashen lords, kill mage lords (up to 5).
        // Phase 3: Kill remaining mage lords + mage companions, redistribute Ashen settlements (up to 3).
        // Phase 4: Finish redistribution, show final dialog → player dies.
        private static void TickEnding()
        {
            try
            {
                switch (_endingPhase)
                {
                    case 1:
                        _worldRekindled = true;
                        KillAshenLords(5);
                        _endingPhase = 2;
                        break;

                    case 2:
                        KillAshenLords(20);   // finish remaining Ashen
                        KillMageLords(5);
                        _endingPhase = 3;
                        break;

                    case 3:
                        KillMageLords(20);    // finish remaining mage lords
                        KillMageCompanions();
                        RedistributeAshenSettlements(3);
                        _endingPhase = 4;
                        break;

                    case 4:
                        RedistributeAshenSettlements(20); // finish remaining
                        _endingPhase = 5;
                        // Queue the final dialog
                        if (MageKnowledge._deferredInquiry == null)
                            MageKnowledge._deferredInquiry = ShowEndingDialog;
                        break;
                }
            }
            catch { }
        }

        private static void KillAshenLords(int cap)
        {
            int killed = 0;
            try
            {
                foreach (Hero h in Hero.AllAliveHeroes.ToList())
                {
                    if (killed >= cap) break;
                    if (!h.IsAlive || h.IsChild || h == Hero.MainHero) continue;
                    if (!ColourLordRegistry.IsAshenLord(h)) continue;
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
                    try { KillCharacterAction.ApplyByMurder(companion, null, false); } catch { }
                }
            }
            catch { }
        }

        private static void RedistributeAshenSettlements(int cap)
        {
            const string AshenId = "ashen_kingdom";
            int moved = 0;
            try
            {
                var kingdoms = Kingdom.All
                    .Where(k => !k.IsEliminated && k.StringId != AshenId)
                    .ToList();
                if (kingdoms.Count == 0) return;

                foreach (Settlement s in Settlement.All.ToList())
                {
                    if (moved >= cap) break;
                    if (s.MapFaction?.StringId != AshenId) continue;
                    if (s.IsUnderSiege) continue;

                    var target = kingdoms[_rng.Next(kingdoms.Count)];
                    Hero lord = target.Leader ?? target.RulingClan?.Leader;
                    if (lord == null) continue;
                    try
                    {
                        ChangeOwnerOfSettlementAction.ApplyByDefault(lord, s);
                        try { if (s.Town != null) { s.Town.Loyalty = 100f; s.Town.Security = 100f; } } catch { }
                        moved++;
                    }
                    catch { }
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
                    "The Rekindling",

                    "The fire goes out of you all at once.\n\n" +
                    "Not violently. Not like dying in battle. " +
                    "More like a candle that has burned everything it was given.\n\n" +
                    "You feel it pass through you and out — and as it goes, " +
                    "you are briefly aware of every other flame in the world. " +
                    "Every mage whose gift shudders and releases in the same moment. " +
                    "The Ashen. The mage lords. The wanderers and hedge-witches and old teachers. " +
                    "All of them, at once.\n\n" +
                    "It is warmer than you expected.\n\n" +
                    "The darkness breaks. Somewhere the Ashen crumble. " +
                    "Somewhere a city sees dawn without cold fire in the rafters. " +
                    "Somewhere a child is born who will never know the grey march.\n\n" +
                    "They will call it something else. They will never know your name.\n\n" +
                    "The morning comes anyway.\n\n" +
                    "Then it is dark.\n\nThen nothing.",

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
                return "\nQuest Failed: The Last Flight of the Dragons.\n" +
                       "The chance is gone. The world turns as it will.\n";

            if (_phase == PhaseRekindled || _worldRekindled)
                return "\nThe world is rekindled. The fire is spent.\n";

            string g1 = _goal1Done ? "✓" : "○";
            string g2 = _goal2Done ? "✓" : "○";
            string g3 = _goal3Done ? "✓" : "○";

            string status = _phase == PhaseAllDone
                ? "\n  [All conditions met — awaiting your decision.]\n"
                : "";

            return $"\nQuest: The Last Flight of the Dragons\n" +
                   $"  {g1}  Establish dominion       (Clan Tier {TargetClanTier})\n" +
                   $"  {g2}  Enter the cold heart     (capture Tyal)\n" +
                   $"  {g3}  Gain the power           (Level {TargetHeroLevel})\n" +
                   status;
        }

        // ── Save / Load ───────────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            store.SyncData("LDM_DragonPhase",    ref _phase);
            store.SyncData("LDM_DragonGoal1",    ref _goal1Done);
            store.SyncData("LDM_DragonGoal2",    ref _goal2Done);
            store.SyncData("LDM_DragonGoal3",    ref _goal3Done);
            store.SyncData("LDM_WorldRekindled", ref _worldRekindled);
            store.SyncData("LDM_EndingPhase",    ref _endingPhase);
        }

        private static void EnsureQuestLog()
        {
            _questLog = new DragonQuestLog();
            _questLog.StartQuest();
            _questLog.LogStarted();
            if (_goal1Done) _questLog.LogGoal1();
            if (_goal2Done) _questLog.LogGoal2();
            if (_goal3Done) _questLog.LogGoal3();
            if (_goal1Done && _goal2Done && _goal3Done) _questLog.LogAllDone();
        }

        public static void ResetForNewGame()
        {
            _phase          = PhaseIdle;
            _goal1Done      = false;
            _goal2Done      = false;
            _goal3Done      = false;
            _worldRekindled = false;
            _endingPhase    = 0;
            _questLog       = null;
        }

        public static void OnGameStart()
        {
            // If a rekindled save is loaded, ensure world events stay off
            // (checked via WorldRekindled property in CampaignMapEvents)
        }
    }

    public sealed class DragonQuestLog : QuestBase
    {
        public DragonQuestLog()
            : base("ldm_dragon_quest", Hero.MainHero, CampaignTime.Never, 0) { }

        public override TextObject Title => new TextObject("The Last Flight of the Dragons");
        public override bool IsRemainingTimeHidden => true;

        protected override void InitializeQuestOnGameLoad()
        {
            DragonQuestSystem._questLog = this;
        }

        protected override void RegisterEvents() { }
        protected override void HoldNotificationOnce() { }
        protected override void HoldAfterTick(float realDt) { }

        internal void LogStarted() =>
            AddLog(new TextObject(
                "The old mage's last words: gain a grasp on the world, enter the cold heart, gain the power — then rekindle everything. Three conditions, and a sacrifice at the end."));

        internal void LogGoal1() =>
            AddLog(new TextObject(
                "Clan Tier 6 reached. Your name is known across Calradia. The first condition is met."));

        internal void LogGoal2() =>
            AddLog(new TextObject(
                "Tyal taken. The cold heart has been entered and understood. The second condition is met."));

        internal void LogGoal3() =>
            AddLog(new TextObject(
                "The inner fire burns at full height. The third condition is met."));

        internal void LogAllDone() =>
            AddLog(new TextObject(
                "All three conditions are met. The rekindling is possible. The choice remains."));

        internal void LogComplete()
        {
            AddLog(new TextObject("The fire is released. The world has its morning. The cost was everything."));
            CompleteQuestWithSuccess();
        }

        internal void LogFailed()
        {
            AddLog(new TextObject("The chance is gone. Whatever the old mage was offering died with your refusal."));
            CompleteQuestWithFail();
        }
    }
}
