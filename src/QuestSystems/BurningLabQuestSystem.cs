// =============================================================================
// ASH AND EMBER — BurningLabQuestSystem.cs
// "The Burning Laboratory" — a multi-branch quest seeded after a siege victory.
//
// TRIGGER
//   Player wins a siege as attacker, campaign day ≥ 80.
//   Chance scales from ~2 % per siege at day 80 to ~85 % at day 300+.
//   Fires at most once per campaign (_eventFired flag).
//
// INITIAL CHOICE — 11 options (minus those whose faction/leader is dead)
//   1. Destroy      → gain Honour, quest ends
//   2. Keep         → Questline C
//   3. Sell         → +10 000 gold, lose Honour; 50 % chance book reaches
//                     a random living imperial leader → Questline A
//   4. Give Rhagaea → Questline A (empire_s)
//   5. Give Lucon   → Questline A (empire_n)
//   6. Give Garios  → Questline A (empire_w)
//   7–11. Give non-imperial faction → Questline B
//
// QUESTLINE A — The Resurrection of Arenicos
//   Phase 0: 3-day delay  → "Rituals begin in secret…"
//   Phase 1: 10-day delay → Arenicos possesses a random male lord;
//              secretly rolled: true emperor or false emperor (Ashen spirit)
//   Phase 2: 3-day delay  → each other empire faction may submit (one is
//              guaranteed, the rest 50 %); a submitting empire's clans and
//              fiefs are absorbed into Arenicos's empire
//   Phase 3: 3-day delay  → Arenicos declares war on all non-imperial factions
//   Phase 4: active monitoring:
//              • detect Arenicos hero death → empire split (phase 5)
//              • false emperor: 50-day timer → ally with Ashen (peace maintained daily)
//   Phase 5: empire split — distribute Arenicos empire fiefs to surviving empires
//   Phase 9: complete
//
// QUESTLINE B — The Faction's Gambit
//   Phase 0: 3-day delay  → "The book has reached their scholars…"
//   Phase 1: roll outcome (1/3 each):
//              1 = discard   → narrative end
//              2 = bad       → faction consumed; towns/castles flip to Ashen every 3 days
//              3 = good      → weekly: each army gains 30 tier-4 troops;
//                              20 % per week: triggers bad path instead
//   Phase 9: complete
//
// QUESTLINE C — Personal Rites
//   Active while player holds the book.
//   Weekly prompt (7-day cooldown):
//     A. Discard  → quest ends
//     B. Perform rite → Renown +50, large XP gain, lose Honour,
//                        5 % chance player becomes Ashen
//
// SAVE KEYS  (prefix BLQ_)
//   BLQ_Phase, BLQ_Fired
//   BLQ_QASubPhase, BLQ_QATimer, BLQ_QAEmpireId, BLQ_ArenicosId,
//   BLQ_ArenicosTrue, BLQ_QAFalseAlliance, BLQ_QAFalseTimer
//   BLQ_QBSubPhase, BLQ_QBTimer, BLQ_QBFactionId, BLQ_QBOutcome,
//   BLQ_QBSettlements, BLQ_QBConvTimer, BLQ_QBBoostCD
//   BLQ_QCActive, BLQ_QCTimer
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static partial class BurningLabQuestSystem
    {
        // ── Tuning ─────────────────────────────────────────────────────────────
        public const int  LabEarliestDay      = 80;
        public const int  LabNearCertainDay   = 300;
        public const float ChanceLabEarly     = 0.02f;  // per siege at day 80
        public const float ChanceLabLate      = 0.85f;  // per siege at day 300+

        private const int  QAPhaseRitualDelay  = 3;
        private const int  QAPhaseReviveDelay  = 10;
        private const int  QAPhaseAllyDelay    = 3;
        private const int  QAPhaseWarDelay     = 3;
        private const int  QAFalseAllianceDelay = 50;

        private const int  QBOutcomeDelay      = 3;
        private const int  QBConversionDelay   = 3;  // days between each settlement capture

        private const int  QCWeeklyDelay       = 7;

        private const int  SellImperialChance  = 50;   // % chance sell → QA

        private const int  QBGoodUnitCount     = 30;   // tier-4 units per army per week
        private const float QBBadTriggerChance = 0.20f;// per-week chance good→bad in QuestB

        private const string AshenKingdomId    = "ashen_kingdom";
        private static readonly string[] EmpireIds = { "empire_s", "empire_n", "empire_w", "empire" };

        // ── Main phase ─────────────────────────────────────────────────────────
        private const int PhaseIdle   = 0;
        private const int PhaseQA     = 1;
        private const int PhaseQB     = 2;
        private const int PhaseQC     = 3;
        private const int PhaseEnded  = 4;

        // ── State ──────────────────────────────────────────────────────────────
        private static int  _phase      = PhaseIdle;
        private static bool _eventFired = false;

        // Quest A
        private static int    _qaSubPhase          = 0;
        private static int    _qaTimer             = 0;
        private static string _qaEmpireId          = null;
        private static string _arenicosHeroId      = null;
        private static bool   _arenicosIsTrue       = false;
        private static bool   _qaFalseAllianceActive = false;
        private static int    _qaFalseAllianceTimer = 0;
        private static bool   _qaAshenMerged = false;
        private static bool   _qaWitheringFired = false;
        private static int    _qaAnchorThrottle = 0;    // not saved — resets on load
        private static int    _qaReplenishCooldown = 0; // not saved — resets on load

        // Quest journal logs — restored via InitializeQuestOnGameLoad on each log's class
        internal static BurningLabQALog _qaQuestLog = null;
        internal static BurningLabQBLog _qbQuestLog = null;
        internal static BurningLabQCLog _qcQuestLog = null;

        // Quest B
        private static int    _qbSubPhase          = 0;
        private static int    _qbTimer             = 0;
        private static string _qbFactionId         = null;
        private static int    _qbOutcome           = 0;   // 1=discard, 2=bad, 3=good
        private static readonly List<string> _qbSettlementQueue = new List<string>();
        private static int    _qbConversionTimer   = 0;
        private static int    _qbWeeklyBoostCooldown = 0;

        // Quest C
        private static bool   _qcActive            = false;
        private static int    _qcWeeklyTimer       = 0;
        private static int    _qcWhisperTimer      = 0;
        private static int    _qcWhisperIndex      = 0;

        private static readonly Random _rng = new Random();

        // ── Public API ─────────────────────────────────────────────────────────

        /// Returns true if <paramref name="h"/> is the lord currently possessed by Arenicos.
        public static bool IsArenicosHero(Hero h) =>
            h != null && _arenicosHeroId != null && h.StringId == _arenicosHeroId;

        /// True if the current Arenicos is the genuine emperor spirit; false if it is an Ashen impostor.
        public static bool ArenicosIsTrue => _arenicosIsTrue;

        /// True after the Ashen faction has merged into Arenicos's empire.
        public static bool AshenMergedWithArenicos => _qaAshenMerged;

        /// StringId of the empire kingdom that hosts Arenicos.
        public static string ArenicosEmpireId => _qaEmpireId;

        /// True when a false-emperor Arenicos (Ashen impostor) is currently alive.
        public static bool FalseEmperorIsAlive =>
            !_arenicosIsTrue
            && _arenicosHeroId != null
            && Hero.AllAliveHeroes.Any(h => h.StringId == _arenicosHeroId && h.IsAlive);

        /// True after the Withering ending has fired (90 % town threshold met).
        public static bool WitheringFired => _qaWitheringFired;

        /// Called from SettlementEncounters.TryFireSiege().
        /// Returns true if the lab discovery event should fire this siege.
        /// Handles internal day-gate and probability scaling.
        public static bool RollLabDiscovery()
        {
            if (_eventFired) return false;
            double elapsed = ElapsedCampaignDays();
            if (elapsed < LabEarliestDay) return false;

            float t = (float)Math.Min(1.0, (elapsed - LabEarliestDay) / (LabNearCertainDay - LabEarliestDay));
            float chance = ChanceLabEarly + (ChanceLabLate - ChanceLabEarly) * t;
            return _rng.NextDouble() < chance;
        }

        /// Shows the initial lab discovery dialog (set as MageKnowledge._deferredInquiry).
        public static void ShowInitialDiscovery()
        {
            if (_eventFired) return;
            _eventFired = true;

            try
            {
                // Build option list dynamically based on which faction leaders are alive
                var elements = new List<InquiryElement>();

                // Always available
                elements.Add(new InquiryElement("destroy", "Destroy it. These experiments are a sin against the fire.", null, true,
                    "The scrolls burn. The knowledge dies with them. You feel cleaner for it. (+Honour)"));
                elements.Add(new InquiryElement("keep", "Keep it. There may be use in this.", null, true,
                    "The scrolls are yours. What you do with them remains to be seen."));
                elements.Add(new InquiryElement("sell", "Sell it. Someone will pay for secrets this dark.", null, true,
                    "+10 000 gold. −Honour. The buyer's identity is their own business. (May reach an imperial court.)"));

                // Imperial leaders
                AddImperialOption(elements, "empire_s", "give_s",
                    "Give it to Rhagaea — perhaps she can use it to restore the Empire.",
                    "The Southern Empire receives the scrolls. Rhagaea's scholars will study them.");
                AddImperialOption(elements, "empire_n", "give_n",
                    "Give it to Lucon — the North has the scholars for this.",
                    "The Northern Empire receives the scrolls. Lucon's court will decide their fate.");
                AddImperialOption(elements, "empire_w", "give_w",
                    "Give it to Garios — the West has the ambition for it.",
                    "The Western Empire receives the scrolls. Garios's mages will not sleep for weeks.");

                // Non-imperial factions
                AddFactionOption(elements, "sturgia",  "give_sturgia",  "Give it to the Northmen — iron hands may steady iron knowledge.",
                    "The Northmen receive the scrolls. What they do with fire-made-life is their own affair.");
                AddFactionOption(elements, "khuzait",  "give_khuzait",  "Give it to the Tribes of the East — the God-King keeps his own counsel.",
                    "The Tribes of the East receive the scrolls. The wind carries secrets far.");
                AddFactionOption(elements, "battania", "give_battania", "Give it to the Battanians — they understand old power.",
                    "Battania receives the scrolls. They know things about life and death that predate the Empire.");
                AddFactionOption(elements, "aserai",   "give_aserai",   "Give it to the Duneborn — the desert keeps its own secrets.",
                    "The Duneborn receive the scrolls. Desert silence is good for dangerous research.");
                AddFactionOption(elements, "vlandia",  "give_vlandia",  "Give it to The Holy Temple — the Templars guard old fire.",
                    "The Holy Temple receives the scrolls. They will lock them beneath an altar and pray no one breaks the seal.");

                if (elements.Count < 2)
                {
                    _eventFired = false; // Allow retry if world state is broken
                    return;
                }

                string body =
                    "A hidden door in a captured castle. Behind it: a laboratory. Shelves of scroll-cases, sealed jars, " +
                    "things that do not bear close examination.\n\n" +
                    "Someone was trying to create life from fire — not grow it, not birth it. Create it. " +
                    "The scrolls are legible. Dense. Dangerous. Further along than you expected.\n\n" +
                    "What do you do with what you have found?";

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "The Burning Laboratory",
                    body,
                    elements,
                    false, 1, 1,
                    "Decide",
                    "",
                    chosen =>
                    {
                        try { HandleInitialChoice(chosen?[0]?.Identifier as string ?? "destroy"); }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    },
                    null, "", false
                ), false);
            }
            catch { _eventFired = false; }
        }

        // ── Tick hooks ─────────────────────────────────────────────────────────

        public static void DailyTick()
        {
            try
            {
                switch (_phase)
                {
                    case PhaseQA: TickQA(); break;
                    case PhaseQB: TickQB(); break;
                    case PhaseQC: TickQC(); break;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        public static void WeeklyTick()
        {
            try
            {
                if (_phase == PhaseQB && _qbSubPhase == 3 /* good path */)
                    TickQBGoodPathWeekly();

                if (_phase == PhaseQC)
                    TickQCWeeklyPrompt();
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Save / Load ────────────────────────────────────────────────────────

        public static void Save(IDataStore store)
        {
            int eventFiredInt = _eventFired ? 1 : 0;
            store.SyncData("BLQ_Phase",  ref _phase);
            store.SyncData("BLQ_Fired",  ref eventFiredInt);
            _eventFired = eventFiredInt != 0;

            store.SyncData("BLQ_QASubPhase",      ref _qaSubPhase);
            store.SyncData("BLQ_QATimer",         ref _qaTimer);
            store.SyncData("BLQ_QAEmpireId",      ref _qaEmpireId);
            store.SyncData("BLQ_ArenicosId",      ref _arenicosHeroId);
            int arTrue = _arenicosIsTrue ? 1 : 0;
            store.SyncData("BLQ_ArenicosTrue",    ref arTrue);
            _arenicosIsTrue = arTrue != 0;
            int falseAlliance = _qaFalseAllianceActive ? 1 : 0;
            store.SyncData("BLQ_QAFalseAlliance", ref falseAlliance);
            _qaFalseAllianceActive = falseAlliance != 0;
            store.SyncData("BLQ_QAFalseTimer",    ref _qaFalseAllianceTimer);
            int ashenMerged = _qaAshenMerged ? 1 : 0;
            store.SyncData("BLQ_AshenMerged",     ref ashenMerged);
            _qaAshenMerged = ashenMerged != 0;
            int witheringFired = _qaWitheringFired ? 1 : 0;
            store.SyncData("BLQ_WitheringFired",  ref witheringFired);
            _qaWitheringFired = witheringFired != 0;

            store.SyncData("BLQ_QBSubPhase",      ref _qbSubPhase);
            store.SyncData("BLQ_QBTimer",         ref _qbTimer);
            store.SyncData("BLQ_QBFactionId",     ref _qbFactionId);
            store.SyncData("BLQ_QBOutcome",       ref _qbOutcome);
            var qbList = _qbSettlementQueue.ToList();
            store.SyncData("BLQ_QBSettlements",   ref qbList);
            if (qbList != null) { _qbSettlementQueue.Clear(); _qbSettlementQueue.AddRange(qbList); }
            store.SyncData("BLQ_QBConvTimer",     ref _qbConversionTimer);
            store.SyncData("BLQ_QBBoostCD",       ref _qbWeeklyBoostCooldown);

            int qcActive = _qcActive ? 1 : 0;
            store.SyncData("BLQ_QCActive",        ref qcActive);
            _qcActive = qcActive != 0;
            store.SyncData("BLQ_QCTimer",         ref _qcWeeklyTimer);
            store.SyncData("BLQ_QCWhisperTimer",  ref _qcWhisperTimer);
            store.SyncData("BLQ_QCWhisperIndex",  ref _qcWhisperIndex);
        }

        public static void ResetForNewGame()
        {
            _phase                   = PhaseIdle;
            _eventFired              = false;
            _qaSubPhase              = 0;
            _qaTimer                 = 0;
            _qaEmpireId              = null;
            _arenicosHeroId          = null;
            _arenicosIsTrue          = false;
            _qaFalseAllianceActive   = false;
            _qaFalseAllianceTimer    = 0;
            _qaAshenMerged           = false;
            _qaWitheringFired        = false;
            _qaAnchorThrottle        = 0;
            _qaQuestLog              = null;
            _qbQuestLog              = null;
            _qcQuestLog              = null;
            _qbSubPhase              = 0;
            _qbTimer                 = 0;
            _qbFactionId             = null;
            _qbOutcome               = 0;
            _qbSettlementQueue.Clear();
            _qbConversionTimer       = 0;
            _qbWeeklyBoostCooldown   = 0;
            _qcActive                = false;
            _qcWeeklyTimer           = 0;
            _qcWhisperTimer          = 0;
            _qcWhisperIndex          = 0;
        }

    }


    public sealed class BurningLabQALog : QuestBase
    {
        public BurningLabQALog()
            : base("ldm_burninglabqa_quest", Hero.MainHero, CampaignTime.Never, 0) { }

        public override TextObject Title => new TextObject("The Resurrection of Arenicos");
        public override bool IsRemainingTimeHidden => true;

        protected override void InitializeQuestOnGameLoad()
        {
            BurningLabQuestSystem._qaQuestLog = this;
        }
        protected override void RegisterEvents() { }
        protected override void SetDialogs() { }

        internal void LogStarted(string empName) =>
            AddLog(new TextObject($"The imperial scrolls have been delivered to {empName}. Something ancient is being attempted in secret."));

        internal void LogRevival(string heroName, bool isTrue) =>
            AddLog(new TextObject(isTrue
                ? $"The ritual succeeded. A lord named {heroName} now claims to be Emperor Arenicos — and seems to believe it. His eyes are clear."
                : $"The ritual succeeded. Something old and cold passed through {heroName} and did not leave. He calls himself Arenicos. His eyes are wrong."));

        internal void LogMerger(string arName) =>
            AddLog(new TextObject($"{arName}'s empire has revealed its allegiance. The Ashen march under the imperial banner. The cold and the throne are one."));

        internal void LogWitheringVictory() =>
            AddLog(new TextObject("The empire holds nine in ten cities. The cold has won. The world will not recover from this."));

        internal void LogWitheringDefeat() =>
            AddLog(new TextObject("The empire holds nine in ten cities. The withering is complete. The world the fires built is over."));

        internal void LogFalseEmperorDead() =>
            AddLog(new TextObject("The false emperor is dead. The cold alliance shatters. The Ashen withdraw. What the empire does next is its own affair."));

        internal void LogTrueEmperorDead() =>
            AddLog(new TextObject("The emperor is dead. The empire he briefly held together has fractured. The resurrection failed to hold."));

        internal void CompleteSuccess() { try { CompleteQuestWithSuccess(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } }
        internal void CompleteFail()    { try { CompleteQuestWithFail();    } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } }
    }

    public sealed class BurningLabQBLog : QuestBase
    {
        public BurningLabQBLog()
            : base("ldm_burninglabqb_quest", Hero.MainHero, CampaignTime.Never, 0) { }

        public override TextObject Title => new TextObject("The Faction's Gambit");
        public override bool IsRemainingTimeHidden => true;

        protected override void InitializeQuestOnGameLoad()
        {
            BurningLabQuestSystem._qbQuestLog = this;
        }
        protected override void RegisterEvents() { }
        protected override void SetDialogs() { }

        internal void LogStarted(string factionName) =>
            AddLog(new TextObject($"The scrolls have been handed over to {factionName}. Whatever happens next is their decision — and their consequence."));

        internal void LogOutcomeDiscard() =>
            AddLog(new TextObject("The faction studied the scrolls and burned them. The knowledge dies with their caution."));

        internal void LogOutcomeBad() =>
            AddLog(new TextObject("Something went wrong. The grey spreads from the inside. Their cities are being consumed one by one."));

        internal void LogOutcomeGood() =>
            AddLog(new TextObject("The experiment worked — for now. Their armies ride out heavier and stranger. Temporarily. Everything is temporary."));

        internal void LogBadComplete() =>
            AddLog(new TextObject("The grey tide has consumed everything it was given. The faction is no more."));

        internal void CompleteFail() { try { CompleteQuestWithFail(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } }
    }

    public sealed class BurningLabQCLog : QuestBase
    {
        public BurningLabQCLog()
            : base("ldm_burninglabqc_quest", Hero.MainHero, CampaignTime.Never, 0) { }

        public override TextObject Title => new TextObject("The Personal Rites");
        public override bool IsRemainingTimeHidden => true;

        protected override void InitializeQuestOnGameLoad()
        {
            BurningLabQuestSystem._qcQuestLog = this;
        }
        protected override void RegisterEvents() { }
        protected override void SetDialogs() { }

        internal void LogStarted() =>
            AddLog(new TextObject("The scrolls are with you. You have not read them in full. They have not stopped feeling warm since you picked them up."));

        internal void LogRitePerformed() =>
            AddLog(new TextObject("The rite is completed. The fire inside burned different for two days. It has not fully returned to where it was."));

        internal void LogBecameAshen() =>
            AddLog(new TextObject("The fire in you goes cold. Something else answers instead. The rite has taken everything it was promised."));

        internal void LogGivenAway(string recipient) =>
            AddLog(new TextObject($"The scrolls were handed over to {recipient}. You held them longer than you meant to. Whatever they were promising is someone else's problem now."));

        internal void LogDiscarded() =>
            AddLog(new TextObject("The scrolls go into the fire. Whatever they were offering is gone. The last line was still readable when the ashes cooled."));

        internal void CompleteSuccess() { try { CompleteQuestWithSuccess(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } }
        internal void CompleteFail()    { try { CompleteQuestWithFail();    } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } }
    }
}
