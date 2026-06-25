// =============================================================================
// ASH AND EMBER — DragonQuestSystem.cs
// The Silence Between Fires — main campaign goal for non-Ashen players.
//
// Trigger  : Player defeats their first Ashen lord in battle (leading the
//            winning side). That kill draws the Temple's eye and counts as the
//            first of the five. No Temple membership required — contact alone
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
        private const int PhaseColdActive = 6; // player turned Ashen; Temple path failed, cold conquest begins
        private const int PhaseColdDone   = 7; // cold conquest complete — everything is conquered

        // ── Tuning ────────────────────────────────────────────────────────────
        public const int TargetLordsSlain      = 6;  // Ashen lords (cold embers)
        public const int TargetMageLordsSlain  = 5;  // non-Ashen mage lords (warm embers)
        public const int TargetCitiesTaken     = 3;
        public const int TargetRuinsCleared    = 4;
        private const int FinalChoiceMinDay     = 300;   // absolute day floor
        private const int FinalChoiceMinContact = 730;   // days after first contact (two years)
        private const string AshenKingdomId  = "ashen_kingdom";

        // ── State ─────────────────────────────────────────────────────────────
        private static int  _phase        = PhaseIdle;
        private static int  _lordsSlain      = 0;  // Ashen lords killed
        private static int  _mageLordsSlain  = 0;  // non-Ashen mage lords killed
        private static int  _citiesTaken     = 0;
        private static int  _storyPhase      = 0;  // 0-6: Ashen lord stories shown
        private static int  _mageStoryPhase  = 0;  // 0-3: mage lord stories shown (first 3 kills)
        private static int  _letterPhase  = 0;  // 0-6: how many Temple letters sent
        private static int  _endingPhase  = 0;  // 0-4: ending sequence progress
        private static bool _worldBound            = false;
        private static int  _contactDay            = -1;
        private static int  _generation            = 1;    // how many main heroes have carried this
        private static bool _pendingSuccessionPopup = false;
        private static int  _coldTownTarget        = 0;

        // Not saved — initialised at session start to detect mid-session succession.
        private static string _lastMainHeroId = null;
        internal static DragonQuestLog      _questLog     = null;
        internal static EternalColdQuestLog _coldQuestLog = null;

        private static readonly HashSet<string> _everAshenSettlements = new HashSet<string>();
        private static readonly HashSet<string> _capturedAshenCities  = new HashSet<string>();
        private static readonly Random          _rng                  = new Random();

        // ── Public accessors ──────────────────────────────────────────────────
        public static bool IsActive        => _phase == PhaseActive || _phase == PhaseAllDone;
        public static bool IsDone          => _phase == PhaseAccepted || _phase == PhaseRefused;
        public static bool WorldRekindled  => _worldBound;  // consumed by CampaignMapEvents, ColourLordRegistry, AshenCitySystem
        public static int  LordsSlain      => _lordsSlain;
        public static int  MageLordsSlain  => _mageLordsSlain;
        public static int  CitiesTaken     => _citiesTaken;

        private static int Today()
        {
            try { return (int)CampaignTime.Now.ToDays; } catch { return 0; }
        }

        // ── Called from CampaignBehavior.OnMapEventEnded ──────────────────────
        // Two roles, by phase:
        //   · Idle   — the player's first Ashen-lord kill triggers Temple contact
        //              and counts as the first of the five.
        //   · Active — each Ashen-lord kill counts toward the objective.
        public static void OnMapEventEnded(MapEvent mapEvent)
        {
            if (mapEvent == null) return;
            if (MageKnowledge.IsAshen) return;

            bool idle   = _phase == PhaseIdle;
            bool active = _phase == PhaseActive || _phase == PhaseAllDone;
            if (!idle && !active) return;                       // Contacted/finished: nothing to do
            bool ashenDone = active && _lordsSlain >= TargetLordsSlain;
            bool mageDone  = active && _mageLordsSlain >= TargetMageLordsSlain;
            if (ashenDone && mageDone) return;

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

                int ashenKilled    = 0;
                int mageLordKilled = 0;
                foreach (var meparty in enemySide.Parties.ToList())
                {
                    Hero leader = meparty?.Party?.LeaderHero;
                    if (leader == null || leader.IsAlive) continue;
                    if (ColourLordRegistry.IsAshenLord(leader))
                        ashenKilled++;
                    else if (ColourLordRegistry.IsColourLord(leader))
                        mageLordKilled++;
                }
                if (ashenKilled <= 0 && mageLordKilled <= 0) return;

                if (idle && ashenKilled > 0)
                {
                    // First Ashen lord defeated — the Temple takes notice. Open to
                    // any non-Ashen player, mage or not (the Ashen gate is above).
                    _phase      = PhaseContacted;
                    _contactDay = Today();
                    _lordsSlain = Math.Min(ashenKilled, TargetLordsSlain);  // the kill that drew their eye counts
                    if (MageKnowledge._deferredInquiry == null)
                        MageKnowledge._deferredInquiry = ShowTempleContact;
                    // else DailyTick re-queues the contact popup when the slot frees
                    return;
                }

                // Active / AllDone: tally Ashen kills toward the cold-ember objective.
                for (int i = 0; i < ashenKilled && _lordsSlain < TargetLordsSlain; i++)
                {
                    _lordsSlain++;
                    try { _questLog?.LogLordSlain(_lordsSlain); } catch { }
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"An Ashen lord falls. Their cold ember passes to you. [{_lordsSlain}/{TargetLordsSlain}]",
                        new Color(0.70f, 0.55f, 0.35f)));
                }

                // Tally non-Ashen mage lord kills toward the warm-ember objective.
                for (int i = 0; i < mageLordKilled && _mageLordsSlain < TargetMageLordsSlain; i++)
                {
                    _mageLordsSlain++;
                    try { _questLog?.LogMageLordSlain(_mageLordsSlain); } catch { }
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"A mage lord's fire is spent. Their warm ember is yours. [{_mageLordsSlain}/{TargetMageLordsSlain}]",
                        new Color(0.80f, 0.70f, 0.40f)));
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

            // Succession check — must run before any popup logic.
            try { CheckSuccession(); } catch { }

            // Queue succession popup before anything else if it's pending.
            if (_pendingSuccessionPopup && MageKnowledge._deferredInquiry == null)
            {
                _pendingSuccessionPopup = false;
                MageKnowledge._deferredInquiry = ShowSuccessionContact;
                return;
            }

            // The cold has claimed the player. The Temple's path dies; a colder
            // ambition takes its place.
            if (MageKnowledge.IsAshen) { TurnToCold(); return; }

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
            try { _questLog?.UpdateProgress(_lordsSlain, _mageLordsSlain, _citiesTaken, AshenRuinSystem.ClearedCount); } catch { }

            // Transition to AllDone when all objectives complete
            if (_phase == PhaseActive
                && _lordsSlain      >= TargetLordsSlain
                && _mageLordsSlain  >= TargetMageLordsSlain
                && _citiesTaken     >= TargetCitiesTaken
                && AshenRuinSystem.ClearedCount >= TargetRuinsCleared)
            {
                _phase = PhaseAllDone;
                try { _questLog?.LogAllDone(); } catch { }
                InformationManager.DisplayMessage(new InformationMessage(
                    "The Silence Between Fires — all conditions are met. A final letter from the Temple waits.",
                    new Color(0.75f, 0.55f, 0.3f)));
            }

            // Queue pending Ashen lord story — one at a time, after each kill
            if (_storyPhase < _lordsSlain && MageKnowledge._deferredInquiry == null)
            {
                int storyIdx = _storyPhase;
                _storyPhase++;
                MageKnowledge._deferredInquiry = () => ShowLordStory(storyIdx);
                return;
            }

            // Queue pending mage lord stories — first 3 kills each get a vignette
            int mageStoriesExpected = Math.Min(_mageLordsSlain, 3);
            if (_mageStoryPhase < mageStoriesExpected && MageKnowledge._deferredInquiry == null)
            {
                int storyIdx = _mageStoryPhase;
                _mageStoryPhase++;
                MageKnowledge._deferredInquiry = () => ShowMageLordStory(storyIdx);
                return;
            }

            // Queue Temple letters once all pending stories have been shown
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

        // ── Succession detection ──────────────────────────────────────────────
        // Called every daily tick. Detects when Hero.MainHero has changed
        // (old age or battle death) and flags a pending succession popup if the
        // quest is active. Progress (embers, kills, phase) persists unchanged —
        // the fire inherits through the bloodline.
        private static void CheckSuccession()
        {
            string currentId = Hero.MainHero?.StringId;
            if (currentId == null) return;

            if (_lastMainHeroId == null)
            {
                _lastMainHeroId = currentId;
                return;
            }

            if (currentId == _lastMainHeroId) return;

            // Main hero changed — succession happened.
            _lastMainHeroId = currentId;

            bool questCarried = _phase == PhaseContacted
                             || _phase == PhaseActive
                             || _phase == PhaseAllDone;
            if (!questCarried) return;

            _generation++;

            // Orphan the old quest log (its QuestGiver hero is now dead).
            // The engine will clean it up; a new one is created after the popup.
            _questLog = null;

            _pendingSuccessionPopup = true;
        }

        internal static string GetOrdinal(int n)
        {
            if (n == 1) return "first";
            if (n == 2) return "second";
            if (n == 3) return "third";
            if (n == 4) return "fourth";
            if (n == 5) return "fifth";
            return $"{n}th";
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
            if (_storyPhase < _lordsSlain) return;  // let Ashen story queue drain first
            if (_mageStoryPhase < Math.Min(_mageLordsSlain, 3)) return;  // let mage story queue drain

            bool shouldSend = false;
            switch (_letterPhase)
            {
                case 0: shouldSend = _contactDay > 0 && Today() - _contactDay >= 14; break;
                case 1: shouldSend = _lordsSlain >= 1 && _storyPhase >= 1; break;
                case 2: shouldSend = _lordsSlain >= 2 && _storyPhase >= 2; break;
                // Letter 3 arrives after the first warm ember — gates on first mage lord kill + story shown.
                case 3: shouldSend = _mageLordsSlain >= 1 && _mageStoryPhase >= 1; break;
                case 4: shouldSend = _lordsSlain >= 4 && _storyPhase >= 4; break;
                // Letter 5 is the altar summons — only fires when all objectives are complete.
                case 5: shouldSend = _phase == PhaseAllDone; break;
            }

            if (!shouldSend) return;

            int idx = _letterPhase;
            _letterPhase++;
            MageKnowledge._deferredInquiry = () => ShowTempleLetterByIndex(idx);
        }

        // ── Cold conversion (player turned Ashen) ─────────────────────────────
        // Fails an in-progress Temple quest and replaces it with the cold conquest.
        // Idle / already-ended states are left untouched: a never-started quest has
        // nothing to fail, and the Ashen Hunger questline covers players who were
        // Ashen from the outset.
        private static void TurnToCold()
        {
            if (_phase == PhaseColdActive) { TickColdQuest(); return; }
            if (_phase == PhaseColdDone)   return;

            bool inProgress = _phase == PhaseContacted
                           || _phase == PhaseActive
                           || _phase == PhaseAllDone;
            if (!inProgress) return;

            // The Temple's path dies the moment the player's fire goes out.
            try { _questLog?.LogColdConversion(); } catch { }
            _questLog = null;

            _phase = PhaseColdActive;
            try { StartColdQuest(); } catch { }
            try { TickColdQuest();  } catch { }
        }

        private static void StartColdQuest()
        {
            _coldTownTarget = CountTowns(out _);
            _coldQuestLog = new EternalColdQuestLog();
            _coldQuestLog.StartQuest();
            _coldQuestLog.LogStarted(_coldTownTarget);
            InformationManager.DisplayMessage(new InformationMessage(
                "Quest added: Bring the Eternal Cold.",
                new Color(0.40f, 0.55f, 0.80f)));
        }

        private static void TickColdQuest()
        {
            if (_coldQuestLog == null) { try { EnsureColdQuestLog(); } catch { } }

            int owned = CountTowns(out int total);
            if (_coldTownTarget <= 0) _coldTownTarget = total;

            try { _coldQuestLog?.UpdateProgress(owned, _coldTownTarget); } catch { }

            if (total > 0 && owned >= total)
            {
                try { _coldQuestLog?.LogComplete(); } catch { }
                _phase = PhaseColdDone;
                InformationManager.DisplayMessage(new InformationMessage(
                    "Calradia is yours. The cold inherits everything. There is nothing left to warm.",
                    new Color(0.40f, 0.55f, 0.80f)));
            }
        }

        // Counts towns owned by the player's faction; outputs the total town count.
        private static int CountTowns(out int total)
        {
            total = 0;
            int owned = 0;
            try
            {
                var playerFaction = Hero.MainHero?.MapFaction;
                foreach (Settlement s in Settlement.All)
                {
                    if (!s.IsTown) continue;
                    total++;
                    if (playerFaction != null && s.MapFaction == playerFaction) owned++;
                }
            }
            catch { }
            return owned;
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
        private JournalLog _objMageLords;
        private JournalLog _objCities;
        private JournalLog _objRuins;

        internal void LogStarted()
        {
            AddLog(new TextObject(
                "The Temple has made contact. Their plan requires four things of you: " +
                "claim six cold embers from Ashen lords, claim five warm embers from mage lords in battle, " +
                "take three Ashen strongholds for your clan, " +
                "and read the darkness of four Ashen ruin sites. The letters will follow your progress."));
            EnsureObjectives();
        }

        private void EnsureObjectives()
        {
            if (_objLords != null && _objMageLords != null && _objCities != null && _objRuins != null) return;
            if (JournalEntries != null && JournalEntries.Count >= 5)
            {
                if (_objLords     == null) _objLords     = JournalEntries[1];
                if (_objMageLords == null) _objMageLords = JournalEntries[2];
                if (_objCities    == null) _objCities    = JournalEntries[3];
                if (_objRuins     == null) _objRuins     = JournalEntries[4];
                return;
            }
            _objLords = AddDiscreteLog(
                new TextObject("Silence six Ashen lords in battle — claim their cold embers."),
                new TextObject("Cold Embers"), 0, DragonQuestSystem.TargetLordsSlain, null, false);
            _objMageLords = AddDiscreteLog(
                new TextObject("Silence five mage lords in battle — claim their warm embers."),
                new TextObject("Warm Embers"), 0, DragonQuestSystem.TargetMageLordsSlain, null, false);
            _objCities = AddDiscreteLog(
                new TextObject("Claim three Ashen cities or castles for your clan."),
                new TextObject("Ashen Strongholds"), 0, DragonQuestSystem.TargetCitiesTaken, null, false);
            _objRuins = AddDiscreteLog(
                new TextObject("Clear four Ashen ruin sites — read what was left there."),
                new TextObject("Ruins Cleared"), 0, DragonQuestSystem.TargetRuinsCleared, null, false);
        }

        internal void UpdateProgress(int lords, int mageLords, int cities, int ruins)
        {
            EnsureObjectives();
            try { _objLords?.UpdateCurrentProgress(Math.Min(lords,     DragonQuestSystem.TargetLordsSlain));     } catch { }
            try { _objMageLords?.UpdateCurrentProgress(Math.Min(mageLords, DragonQuestSystem.TargetMageLordsSlain)); } catch { }
            try { _objCities?.UpdateCurrentProgress(Math.Min(cities,   DragonQuestSystem.TargetCitiesTaken));   } catch { }
            try { _objRuins?.UpdateCurrentProgress(Math.Min(ruins,     DragonQuestSystem.TargetRuinsCleared));  } catch { }
        }

        internal void LogLordSlain(int count) =>
            AddLog(new TextObject(
                $"An Ashen lord silenced — cold ember claimed. [{count}/{DragonQuestSystem.TargetLordsSlain}]"));

        internal void LogMageLordSlain(int count) =>
            AddLog(new TextObject(
                $"A mage lord's fire spent — warm ember claimed. [{count}/{DragonQuestSystem.TargetMageLordsSlain}]"));

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

        internal void LogHandoff(int generation)
        {
            AddLog(new TextObject(
                $"The fire passes. This was the {DragonQuestSystem.GetOrdinal(generation - 1)} " +
                "bearer of the burden. The embers do not mourn."));
        }

        internal void LogColdConversion()
        {
            AddLog(new TextObject(
                "Your fire has gone out. The Temple's riders will not come again — " +
                "you are no longer the kind of flame their plan was written for. " +
                "Whatever they were building, you are now the thing it was meant to stop."));
            CompleteQuestWithFail();
        }
    }


    public sealed class EternalColdQuestLog : QuestBase
    {
        public EternalColdQuestLog()
            : base("ldq_eternal_cold", Hero.MainHero, CampaignTime.Never, 0) { }

        public override TextObject Title => new TextObject("Bring the Eternal Cold");
        public override bool IsRemainingTimeHidden => true;

        protected override void InitializeQuestOnGameLoad()
        {
            DragonQuestSystem._coldQuestLog = this;
        }

        protected override void RegisterEvents() { }
        protected override void SetDialogs() { }

        private JournalLog _objConquer;

        internal void LogStarted(int townTarget)
        {
            AddLog(new TextObject(
                "The warmth is gone, and with it every smaller want. " +
                "What is left of you reaches for the only thing the cold has ever wanted: " +
                "all of it. Every hearth put out. Every banner grey. " +
                "Conquer everything — let the eternal cold inherit the world."));
            EnsureObjective(townTarget);
        }

        private void EnsureObjective(int townTarget)
        {
            if (_objConquer != null) return;
            if (JournalEntries != null && JournalEntries.Count >= 2)
            {
                _objConquer = JournalEntries[1];
                return;
            }
            if (townTarget < 1) townTarget = 1;
            _objConquer = AddDiscreteLog(
                new TextObject("Conquer every town in Calradia."),
                new TextObject("Towns Held"), 0, townTarget, null, false);
        }

        internal void UpdateProgress(int owned, int target)
        {
            EnsureObjective(target);
            try { _objConquer?.UpdateCurrentProgress(Math.Min(owned, Math.Max(target, 1))); } catch { }
        }

        internal void LogComplete()
        {
            AddLog(new TextObject(
                "The last warm hearth is yours. Calradia is one unbroken silence now. " +
                "The cold has everything it ever asked for, and asks for nothing more."));
            CompleteQuestWithSuccess();
        }
    }
}
