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
//   (Pravend and Quyaz are reassigned to Western Empire at game-start)
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
    public static partial class AshenQuestSystem
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
            "Sargot",     // Vlandia
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

            // Withering ending (Arenicos-Ashen territorial victory/defeat) already resolved the world.
            if (BurningLabQuestSystem.WitheringFired) return;

            // Flush pending hunger visions
            if (_phase == PhaseHungerReady && MageKnowledge._deferredInquiry == null)
            {
                MageKnowledge._deferredInquiry = ShowHungerVision1;
                return;
            }
            if (_phase == PhaseHunger2Ready && MageKnowledge._deferredInquiry == null)
            {
                MageKnowledge._deferredInquiry = ShowHungerVision2;
                return;
            }

            if (_phase != PhasePrereqs && _phase != PhaseAllDone) return;

            if (_phase == PhasePrereqs)
            {
                if (_questLog == null) try { EnsureQuestLog(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                CheckPrereqGoals();
            }
            else if (_phase == PhaseAllDone && MageKnowledge._deferredInquiry == null)
            {
                MageKnowledge._deferredInquiry = ShowFinalPrompt;
            }
        }

    }


    public sealed class AshenQuestLog : QuestBase
    {
        public AshenQuestLog()
            : base("ldm_ashen_quest", Hero.MainHero, CampaignTime.Never, 0) { }

        public override TextObject Title => new TextObject("The Hunger of the Void");
        // Exempts the quest from the engine's cancel-on-load sweep (see GreatAwakeningQuestLog).
        public override string SpecialQuestType => "AshAndEmberQuest";
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
