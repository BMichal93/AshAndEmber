// =============================================================================
// ASH AND EMBER — DragonQuestSystem.cs
// The Silence Between Fires — main campaign goal for non-Ashen players.
//
// Trigger  : Player's party enters any Temple (Vlandia)-owned settlement,
//            campaign day ≥ 30. No Temple membership required — contact alone
//            is enough.
//
// Sequence
//   1. Temple contact — a grey-robed rider passes a letter; quest begins
//   2. Three military objectives (parallel, any order):
//      · Kill 5 Ashen lords in battle (player leads winning party)
//      · Capture 3 Ashen towns or castles (player's clan)
//      · Clear 4 Ashen ruins (AshenRuinSystem.ClearedCount)
//   3. Lord stories — 5 deferred encounters revealing who the dead lords were
//      before the cold took them; fire one by one after each kill
//   4. Temple letters — 6 dispatches on a progress-gated timer, building the
//      context until the full truth is in the player's hands
//   5. Final choice (no sooner than 180 days after contact, day ≥ 150):
//      · Accept the Binding → ending sequence → player dies, Ashen crumble
//      · Refuse → quest closed; the world continues unchanged
//
// Save keys  (prefix LDQ_)
//   LDQ_Phase, LDQ_LordsSlain, LDQ_CitiesTaken,
//   LDQ_StoryPhase, LDQ_LetterPhase, LDQ_EndingPhase,
//   LDQ_WorldBound, LDQ_ContactDay,
//   LDQ_EverAshen, LDQ_CapturedAshen
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
        private const int PhaseIdle      = 0;  // not triggered yet
        private const int PhaseContacted = 1;  // Temple reached out, intro pending
        private const int PhaseActive    = 2;  // objectives in progress
        private const int PhaseAllDone   = 3;  // all three met; final prompt pending
        private const int PhaseAccepted  = 4;  // Binding triggered, ending in progress
        private const int PhaseRefused   = 5;  // player declined or never engaged

        // ── Tuning ────────────────────────────────────────────────────────────
        public const int TargetLordsSlain   = 5;
        public const int TargetCitiesTaken  = 3;
        public const int TargetRuinsCleared = 4;
        private const int TriggerEarliestDay    = 30;
        private const int FinalChoiceMinDay     = 150;   // absolute day floor
        private const int FinalChoiceMinContact = 180;   // days after first contact
        private const string TempleKingdomId = "vlandia";
        private const string AshenKingdomId  = "ashen_kingdom";

        // ── State ─────────────────────────────────────────────────────────────
        private static int  _phase        = PhaseIdle;
        private static int  _lordsSlain   = 0;
        private static int  _citiesTaken  = 0;
        private static int  _storyPhase   = 0;  // 0-5: how many lord stories shown
        private static int  _letterPhase  = 0;  // 0-6: how many Temple letters sent
        private static int  _endingPhase  = 0;  // 0-4: ending sequence progress
        private static bool _worldBound   = false;
        private static int  _contactDay   = -1;
        internal static DragonQuestLog _questLog = null;

        private static readonly HashSet<string> _everAshenSettlements = new HashSet<string>();
        private static readonly HashSet<string> _capturedAshenCities  = new HashSet<string>();
        private static readonly Random          _rng                  = new Random();

        // ── Public accessors ──────────────────────────────────────────────────
        public static bool IsActive       => _phase == PhaseActive || _phase == PhaseAllDone;
        public static bool IsDone         => _phase == PhaseAccepted || _phase == PhaseRefused;
        public static bool WorldRekindled => _worldBound;  // consumed by CampaignMapEvents, ColourLordRegistry, AshenCitySystem
        public static int  LordsSlain     => _lordsSlain;
        public static int  CitiesTaken    => _citiesTaken;

        private static int Today()
        {
            try { return (int)CampaignTime.Now.ToDays; } catch { return 0; }
        }

        // ── Called from CampaignBehavior.Events.cs / OnSettlementEntered ─────
        public static void OnSettlementEntered(Settlement settlement)
        {
            if (_phase != PhaseIdle) return;
            if (MageKnowledge.IsAshen) return;
            if (!MageKnowledge.IsMage) return;
            if (Today() < TriggerEarliestDay) return;
            if (settlement == null) return;

            try
            {
                if (settlement.MapFaction?.StringId != TempleKingdomId) return;
                if (MageKnowledge._deferredInquiry != null) return;
                _phase = PhaseContacted;
                _contactDay = Today();
                MageKnowledge._deferredInquiry = ShowTempleContact;
            }
            catch { }
        }

        // ── Called from CampaignBehavior.OnMapEventEnded ──────────────────────
        public static void OnMapEventEnded(MapEvent mapEvent)
        {
            if (_phase < PhaseActive) return;
            if (MageKnowledge.IsAshen) return;
            if (_lordsSlain >= TargetLordsSlain) return;
            try
            {
                bool playerAttacker = mapEvent.AttackerSide?.Parties
                    .Any(p => p.Party == PartyBase.MainParty) == true;
                bool playerDefender = mapEvent.DefenderSide?.Parties
                    .Any(p => p.Party == PartyBase.MainParty) == true;
                if (!playerAttacker && !playerDefender) return;

                bool playerWon = (playerAttacker && mapEvent.WinningSide == BattleSideEnum.Attacker)
                              || (playerDefender && mapEvent.WinningSide == BattleSideEnum.Defender);
                if (!playerWon) return;

                var enemySide = playerAttacker ? mapEvent.DefenderSide : mapEvent.AttackerSide;
                if (enemySide == null) return;

                foreach (var meparty in enemySide.Parties.ToList())
                {
                    if (_lordsSlain >= TargetLordsSlain) break;
                    Hero leader = meparty?.Party?.LeaderHero;
                    if (leader == null || leader.IsAlive) continue;
                    if (!ColourLordRegistry.IsAshenLord(leader)) continue;
                    _lordsSlain++;
                    try { _questLog?.LogLordSlain(_lordsSlain); } catch { }
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"An Ashen lord falls. The cold retreats where you go. [{_lordsSlain}/{TargetLordsSlain}]",
                        new Color(0.70f, 0.55f, 0.35f)));
                }
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

            if (_worldBound) return;
            if (BurningLabQuestSystem.WitheringFired) return;
            if (MageKnowledge.IsAshen) return;

            // Re-queue contact popup if somehow lost
            if (_phase == PhaseContacted && MageKnowledge._deferredInquiry == null)
            {
                MageKnowledge._deferredInquiry = ShowTempleContact;
                return;
            }

            if (_phase != PhaseActive && _phase != PhaseAllDone) return;

            if (_questLog == null) try { EnsureQuestLog(); } catch { }

            // Track Ashen settlement history and new player captures
            try { TrackSettlements(); } catch { }

            // Update journal objectives
            try { _questLog?.UpdateProgress(_lordsSlain, _citiesTaken, AshenRuinSystem.ClearedCount); } catch { }

            // Transition to AllDone when objectives complete
            if (_phase == PhaseActive
                && _lordsSlain  >= TargetLordsSlain
                && _citiesTaken >= TargetCitiesTaken
                && AshenRuinSystem.ClearedCount >= TargetRuinsCleared)
            {
                _phase = PhaseAllDone;
                try { _questLog?.LogAllDone(); } catch { }
                InformationManager.DisplayMessage(new InformationMessage(
                    "The Silence Between Fires — all conditions are met. A final letter from the Temple waits.",
                    new Color(0.75f, 0.55f, 0.3f)));
            }

            // Queue pending lord story — one at a time, after each kill
            if (_storyPhase < _lordsSlain && MageKnowledge._deferredInquiry == null)
            {
                int storyIdx = _storyPhase;
                _storyPhase++;
                MageKnowledge._deferredInquiry = () => ShowLordStory(storyIdx);
                return;
            }

            // Queue Temple letters once lord stories for that tier are shown
            try { CheckLetterDelivery(); } catch { }

            // Final prompt: all done, all 6 letters sent, time gates met
            if (_phase == PhaseAllDone
                && _letterPhase >= 6
                && Today() >= FinalChoiceMinDay
                && (_contactDay < 0 || Today() - _contactDay >= FinalChoiceMinContact)
                && MageKnowledge._deferredInquiry == null)
            {
                MageKnowledge._deferredInquiry = ShowFinalPrompt;
            }
        }

        private static void TrackSettlements()
        {
            var playerClan = Hero.MainHero?.Clan;
            if (playerClan == null) return;

            foreach (Settlement s in Settlement.All)
            {
                if (!s.IsTown && !s.IsCastle) continue;

                // Maintain a running set of settlements that have ever been Ashen
                if (s.MapFaction?.StringId == AshenKingdomId)
                    _everAshenSettlements.Add(s.StringId);

                // Detect captures: settlement is now player's but was ever Ashen
                if (_citiesTaken >= TargetCitiesTaken) continue;
                if (s.OwnerClan != playerClan) continue;
                if (!_everAshenSettlements.Contains(s.StringId)) continue;
                if (!_capturedAshenCities.Add(s.StringId)) continue;

                _citiesTaken++;
                try { _questLog?.LogCityTaken(s.Name?.ToString() ?? "settlement", _citiesTaken); } catch { }
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{s.Name} wrested from the grey march. [{_citiesTaken}/{TargetCitiesTaken}]",
                    new Color(0.65f, 0.50f, 0.30f)));
            }
        }

        private static void CheckLetterDelivery()
        {
            if (_letterPhase >= 6) return;
            if (MageKnowledge._deferredInquiry != null) return;
            if (_storyPhase < _lordsSlain) return;  // let the story queue drain first

            bool shouldSend = false;
            switch (_letterPhase)
            {
                case 0: shouldSend = _contactDay > 0 && Today() - _contactDay >= 14; break;
                case 1: shouldSend = _lordsSlain >= 1 && _storyPhase >= 1; break;
                case 2: shouldSend = _lordsSlain >= 2 && _storyPhase >= 2; break;
                case 3: shouldSend = _lordsSlain >= 3 && _storyPhase >= 3; break;
                case 4: shouldSend = _lordsSlain >= 4 && _storyPhase >= 4; break;
                case 5: shouldSend = _lordsSlain >= 5 && _storyPhase >= 5; break;
            }

            if (!shouldSend) return;

            int idx = _letterPhase;
            _letterPhase++;
            MageKnowledge._deferredInquiry = () => ShowTempleLetterByIndex(idx);
        }

    }


    public sealed class DragonQuestLog : QuestBase
    {
        public DragonQuestLog()
            : base("ldm_dragon_quest", Hero.MainHero, CampaignTime.Never, 0) { }

        public override TextObject Title => new TextObject("The Silence Between Fires");
        public override bool IsRemainingTimeHidden => true;

        protected override void InitializeQuestOnGameLoad()
        {
            DragonQuestSystem._questLog = this;
        }

        protected override void RegisterEvents() { }
        protected override void SetDialogs() { }

        private JournalLog _objLords;
        private JournalLog _objCities;
        private JournalLog _objRuins;

        internal void LogStarted()
        {
            AddLog(new TextObject(
                "The Temple has made contact. Their plan requires three things of you: " +
                "silence five Ashen lords in battle, claim three Ashen strongholds for your clan, " +
                "and read the darkness of four Ashen ruin sites. The letters will follow your progress."));
            EnsureObjectives();
        }

        private void EnsureObjectives()
        {
            if (_objLords != null && _objCities != null && _objRuins != null) return;
            if (JournalEntries != null && JournalEntries.Count >= 4)
            {
                if (_objLords  == null) _objLords  = JournalEntries[1];
                if (_objCities == null) _objCities = JournalEntries[2];
                if (_objRuins  == null) _objRuins  = JournalEntries[3];
                return;
            }
            _objLords = AddDiscreteLog(
                new TextObject("Silence five Ashen lords in battle — lead the winning party."),
                new TextObject("Ashen Lords"), 0, DragonQuestSystem.TargetLordsSlain, null, false);
            _objCities = AddDiscreteLog(
                new TextObject("Claim three Ashen cities or castles for your clan."),
                new TextObject("Ashen Strongholds"), 0, DragonQuestSystem.TargetCitiesTaken, null, false);
            _objRuins = AddDiscreteLog(
                new TextObject("Clear four Ashen ruin sites — read what was left there."),
                new TextObject("Ruins Cleared"), 0, DragonQuestSystem.TargetRuinsCleared, null, false);
        }

        internal void UpdateProgress(int lords, int cities, int ruins)
        {
            EnsureObjectives();
            try { _objLords?.UpdateCurrentProgress(Math.Min(lords,  DragonQuestSystem.TargetLordsSlain));   } catch { }
            try { _objCities?.UpdateCurrentProgress(Math.Min(cities, DragonQuestSystem.TargetCitiesTaken)); } catch { }
            try { _objRuins?.UpdateCurrentProgress(Math.Min(ruins,   DragonQuestSystem.TargetRuinsCleared)); } catch { }
        }

        internal void LogLordSlain(int count) =>
            AddLog(new TextObject(
                $"An Ashen lord silenced. [{count}/{DragonQuestSystem.TargetLordsSlain}]"));

        internal void LogCityTaken(string name, int count) =>
            AddLog(new TextObject(
                $"{name} claimed from the grey march. [{count}/{DragonQuestSystem.TargetCitiesTaken}]"));

        internal void LogAllDone() =>
            AddLog(new TextObject(
                "All three conditions are met. A final letter from the Temple waits. " +
                "The choice draws close."));

        internal void LogComplete()
        {
            AddLog(new TextObject("The Binding fires. The grey retreats. The world has more time."));
            CompleteQuestWithSuccess();
        }

        internal void LogRefused()
        {
            AddLog(new TextObject(
                "You walked away from the Binding. The Temple accepted it. " +
                "The world turns as it always has. The cycle continues."));
            CompleteQuestWithFail();
        }
    }
}
