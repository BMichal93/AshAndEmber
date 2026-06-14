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
    public static partial class DragonQuestSystem
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
                // MapEventEnded fires for EVERY battle in the world, so first confirm the
                // player's own party actually fought here. Without this guard a "!playerAttacker"
                // defender-win on any off-screen battle (e.g. an Ashen lord losing a raid the
                // player never joined) would count as a player victory and trigger the quest.
                bool playerAttacker = mapEvent.AttackerSide?.Parties
                    .Any(p => p.Party == PartyBase.MainParty) == true;
                bool playerDefender = mapEvent.DefenderSide?.Parties
                    .Any(p => p.Party == PartyBase.MainParty) == true;
                if (!playerAttacker && !playerDefender) return;

                // Only fires if player won
                bool playerWon = (playerAttacker && mapEvent.WinningSide == BattleSideEnum.Attacker)
                              || (playerDefender && mapEvent.WinningSide == BattleSideEnum.Defender);
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

            // Withering ending already resolved the world via cold fire — rekindling is no longer possible.
            if (BurningLabQuestSystem.WitheringFired) return;

            // Dragon Quest is only for non-Ashen players.
            if (MageKnowledge.IsAshen) return;

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
                try { _questLog?.UpdateProgress(Hero.MainHero?.Clan?.Tier ?? 0, _goal2Done, Hero.MainHero?.Level ?? 0); } catch { }
                if (_goal1Done && _goal2Done && _goal3Done && _phase == PhaseActive)
                {
                    _phase = PhaseAllDone;
                    try { _questLog?.LogAllDone(); } catch { }

                    if (BurningLabQuestSystem.FalseEmperorIsAlive)
                    {
                        // All conditions met, but a false emperor walks — rekindling is blocked.
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The Last Flight of the Dragons — all conditions are met. " +
                            "But something wears the world's face. " +
                            "The rekindling cannot be attempted while the false emperor lives.",
                            new Color(0.75f, 0.55f, 0.3f)));
                    }
                    else if (MageKnowledge._deferredInquiry == null)
                    {
                        MageKnowledge._deferredInquiry = ShowFinalPrompt;
                    }
                }
            }
            else if (_phase == PhaseAllDone && MageKnowledge._deferredInquiry == null)
            {
                // Re-show final prompt if somehow missed — still blocked while false emperor lives.
                if (!BurningLabQuestSystem.FalseEmperorIsAlive)
                    MageKnowledge._deferredInquiry = ShowFinalPrompt;
            }
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
        protected override void SetDialogs() { }

        // Tracked Journal objectives. The JournalLog objects themselves are persisted
        // by QuestBase, but these field handles are not — UpdateProgress/EnsureObjectives
        // re-link them from JournalEntries after a save/load.
        private JournalLog _objClan;
        private JournalLog _objTyal;
        private JournalLog _objLevel;

        internal void LogStarted()
        {
            AddLog(new TextObject(
                "The old mage's last words: gain a grasp on the world, enter the cold heart, gain the power — then rekindle everything. Three conditions, and a sacrifice at the end."));
            EnsureObjectives();
        }

        // Creates the three tracked Journal goals, or recovers the references after a
        // load. They are added in a fixed order right after the intro log, so on a
        // reloaded quest they sit at JournalEntries[1..3].
        private void EnsureObjectives()
        {
            if (_objClan != null && _objTyal != null && _objLevel != null) return;
            if (JournalEntries != null && JournalEntries.Count >= 4)
            {
                _objClan  = JournalEntries[1];
                _objTyal  = JournalEntries[2];
                _objLevel = JournalEntries[3];
                return;
            }
            _objClan = AddDiscreteLog(
                new TextObject("Establish your dominion — grow your clan into a power the world must answer to."),
                new TextObject("Clan Tier"), 0, DragonQuestSystem.TargetClanTier, null, false);
            _objTyal = AddDiscreteLog(
                new TextObject("Enter the cold heart — take the Ashen stronghold of Tyal for your clan."),
                new TextObject("Capture Tyal"), 0, 1, null, false);
            _objLevel = AddDiscreteLog(
                new TextObject("Gain the power — temper your inner fire through trial until it burns at full height."),
                new TextObject("Hero Level"), 0, DragonQuestSystem.TargetHeroLevel, null, false);
        }

        // Refreshes the tracked objective bars from live campaign state. Safe to call
        // daily and after load — EnsureObjectives self-heals the references first.
        internal void UpdateProgress(int clanTier, bool tyalTaken, int heroLevel)
        {
            EnsureObjectives();
            try { _objClan?.UpdateCurrentProgress(Math.Min(clanTier, DragonQuestSystem.TargetClanTier)); } catch { }
            try { _objTyal?.UpdateCurrentProgress(tyalTaken ? 1 : 0); } catch { }
            try { _objLevel?.UpdateCurrentProgress(Math.Min(heroLevel, DragonQuestSystem.TargetHeroLevel)); } catch { }
        }

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
