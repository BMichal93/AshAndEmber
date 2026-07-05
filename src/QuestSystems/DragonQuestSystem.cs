// =============================================================================
// ASH AND EMBER — DragonQuestSystem.cs
// The Sundered Crown — campaign quest for non-Ashen players.
//
// Trigger  : Player defeats their first Ashen lord in battle (leading the
//            winning side). A presence stirs — fragments of Aelisar Veth,
//            the First Emperor, who shattered his soul into the Ashen cycle.
//            The player is prompted once, then once more. Two refusals close
//            the quest permanently.
//
// Sequence
//   1. First contact — urgency felt after the 1st Ashen lord kill
//      (accept or refuse; refused once = re-prompted on 2nd kill; refused twice = closed)
//   2. Visions — haunt the player as more Ashen lords are killed; grow clearer
//      and more vocal with each kill (up to 7)
//   3. Three parallel objectives:
//      · Kill 7 Ashen lords (player leads winning party)
//      · Clear 3 predestined ruin sites (The Sunken Scriptorium, The Shattered
//        Throne, The Dragon's Tomb)
//      · Capture Tyal — the Heart of Winter
//   4. Final choice (all objectives met):
//      · Banish Aelisar — campaign continues unchanged
//      · Become the Vessel — player gains fire magic; Aelisar's covenant bars aging
//      · The Last Binding — spend everything to break the Ashen cycle (player dies)
//
// Save keys  (prefix LDQ2_ — distinct from the legacy v1 system's LDQ_ keys)
//   LDQ2_Phase, LDQ2_LordsSlain, LDQ2_VisionPhase, LDQ2_ContactDay,
//   LDQ2_EndingPhase, LDQ2_ColdTarget, LDQ2_ProxCooldown,
//   LDQ2_HeartCaptured, LDQ2_WorldBound,
//   LDQ2_EverAshen (settlement tracking for cold quest path)
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
        private const int PhaseIdle              = 0;  // not triggered
        private const int PhaseFirstContact      = 1;  // 1st lord killed; first prompt pending
        private const int PhaseFirstRefused      = 2;  // refused once; re-prompt on 2nd lord kill
        private const int PhasePermanentlyClosed = 3;  // refused twice — quest never starts
        private const int PhaseActive            = 4;  // quest active, objectives in progress
        private const int PhaseAllDone           = 5;  // all objectives met; final choice pending
        private const int PhaseEndedBanish       = 6;  // player banished Aelisar; campaign normal
        private const int PhaseEndedMerge        = 7;  // player became the Vessel
        private const int PhaseEndedSacrifice    = 8;  // Last Binding fired; player dies, Ashen break
        private const int PhaseColdActive        = 9;  // player turned Ashen; cold conquest begins
        private const int PhaseColdDone          = 10; // cold conquest complete

        // ── Tuning ────────────────────────────────────────────────────────────
        public const int TargetLordsSlain = 7;
        private const string AshenKingdomId = "ashen_kingdom";

        // The three predestined ruins the player must clear
        internal static readonly string[] DestinedRuinVillages =
            { "Dravend", "Epis", "Myzea" };
        internal static readonly string[] DestinedRuinNames =
            { "The Sunken Scriptorium", "The Shattered Throne", "The Dragon's Tomb" };

        // ── State ─────────────────────────────────────────────────────────────
        private static int  _phase            = PhaseIdle;
        private static int  _lordsSlain       = 0;
        private static int  _visionPhase      = 0;  // next vision index to queue (0-6)
        private static int  _contactDay       = -1;
        private static bool _heartCaptured    = false; // Tyal taken by player clan
        private static int  _endingPhase      = 0;  // Sacrifice ending sequence step
        private static bool _worldBound       = false; // true only for Sacrifice ending
        private static int  _coldTownTarget   = 0;

        private static int  _proximityCheckCooldown = 0;

        internal static DragonQuestLog      _questLog     = null;
        internal static EternalColdQuestLog _coldQuestLog = null;

        private static readonly HashSet<string> _everAshenSettlements = new HashSet<string>();
        private static readonly Random          _rng                  = new Random();

        // ── Public accessors ──────────────────────────────────────────────────
        public static bool IsActive         => _phase == PhaseActive || _phase == PhaseAllDone;
        public static bool IsDone           => _phase >= PhaseEndedBanish;
        public static bool WorldRekindled   => _worldBound;
        public static bool IsEmperorMerged  => _phase == PhaseEndedMerge;
        public static int  LordsSlain       => _lordsSlain;

        // Count of the 3 predestined ruins that have been cleared this campaign
        public static int DestinedRuinsCleared =>
            DestinedRuinVillages.Count(v => AshenRuinSystem.IsCleared(v));

        private static int Today()
        {
            try { return (int)CampaignTime.Now.ToDays; } catch { return 0; }
        }

        // ── Called from CampaignBehavior.OnMapEventEnded ──────────────────────
        public static void OnMapEventEnded(MapEvent mapEvent)
        {
            if (mapEvent == null) return;
            if (MageKnowledge.IsAshen) return;

            bool idle         = _phase == PhaseIdle;
            bool firstRefused = _phase == PhaseFirstRefused;
            bool active       = _phase == PhaseActive || _phase == PhaseAllDone;

            if (!idle && !firstRefused && !active) return;
            if (active && _lordsSlain >= TargetLordsSlain) return;

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

                int ashenKilled = 0;
                foreach (var meparty in enemySide.Parties.ToList())
                {
                    Hero leader = meparty?.Party?.LeaderHero;
                    if (leader == null || leader.IsAlive) continue;
                    if (ColourLordRegistry.IsAshenLord(leader))
                        ashenKilled++;
                }
                if (ashenKilled <= 0) return;

                if (idle)
                {
                    // First Ashen lord — the presence stirs
                    _phase      = PhaseFirstContact;
                    _contactDay = Today();
                    _lordsSlain = Math.Min(ashenKilled, 1);
                    if (MageKnowledge._deferredInquiry == null)
                        MageKnowledge._deferredInquiry = ShowFirstContact;
                    return;
                }

                if (firstRefused)
                {
                    // Refused once; the second kill re-prompts with more urgency
                    _lordsSlain = Math.Max(_lordsSlain, Math.Min(_lordsSlain + ashenKilled, 2));
                    if (MageKnowledge._deferredInquiry == null)
                        MageKnowledge._deferredInquiry = ShowSecondContact;
                    return;
                }

                // Active: tally kills toward the 7-lord objective
                for (int i = 0; i < ashenKilled && _lordsSlain < TargetLordsSlain; i++)
                {
                    _lordsSlain++;
                    try { _questLog?.LogLordSlain(_lordsSlain); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"A shard of Aelisar returns to you. [{_lordsSlain}/{TargetLordsSlain} lords]",
                        new Color(0.75f, 0.50f, 0.30f)));
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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

            if (MageKnowledge.IsAshen) { TurnToCold(); return; }

            // Re-queue first contact prompt if the popup slot freed
            if (_phase == PhaseFirstContact && MageKnowledge._deferredInquiry == null)
            {
                MageKnowledge._deferredInquiry = ShowFirstContact;
                return;
            }

            if (_phase != PhaseActive && _phase != PhaseAllDone) return;

            if (_questLog == null) try { EnsureQuestLog(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Track Heart of Winter capture
            try { CheckHeartCapture(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Proximity message
            try { CheckProximityToAshenLord(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Update journal objectives
            try { _questLog?.UpdateProgress(_lordsSlain, DestinedRuinsCleared, _heartCaptured); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Transition to AllDone when every objective is met
            if (_phase == PhaseActive
                && _lordsSlain          >= TargetLordsSlain
                && DestinedRuinsCleared >= 3
                && _heartCaptured)
            {
                _phase = PhaseAllDone;
                try { _questLog?.LogAllDone(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                InformationManager.DisplayMessage(new InformationMessage(
                    "The Sundered Crown — all conditions are met. Aelisar speaks clearly now.",
                    new Color(0.80f, 0.55f, 0.25f)));
            }

            // Queue next vision (one per lord kill, triggered in DailyTick so they
            // don't conflict with the map-event popup slot)
            if (_visionPhase < _lordsSlain && MageKnowledge._deferredInquiry == null)
            {
                int idx = _visionPhase;
                _visionPhase++;
                MageKnowledge._deferredInquiry = () => ShowVision(idx);
                return;
            }

            // Final prompt once all done and all visions shown
            if (_phase == PhaseAllDone
                && _visionPhase >= Math.Min(_lordsSlain, 6)
                && MageKnowledge._deferredInquiry == null)
            {
                MageKnowledge._deferredInquiry = ShowFinalPrompt;
            }
        }

        // ── Heart of Winter capture detection ─────────────────────────────────
        private static void CheckHeartCapture()
        {
            if (_heartCaptured) return;
            var playerClan = Hero.MainHero?.Clan;
            if (playerClan == null) return;

            var tyal = Settlement.All.FirstOrDefault(s =>
                s.IsTown &&
                (s.Name?.ToString().IndexOf("Tyal", StringComparison.OrdinalIgnoreCase) >= 0
                 || s.Name?.ToString().IndexOf("Heart of Winter", StringComparison.OrdinalIgnoreCase) >= 0));
            if (tyal == null || tyal.OwnerClan != playerClan) return;

            _heartCaptured = true;
            try { _questLog?.LogHeartCaptured(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            InformationManager.DisplayMessage(new InformationMessage(
                "The Heart of Winter is yours. Aelisar's voice sharpens — something here remembers him.",
                new Color(0.70f, 0.60f, 0.80f)));
        }

        // ── Proximity message ─────────────────────────────────────────────────
        private static void CheckProximityToAshenLord()
        {
            if (_proximityCheckCooldown > 0) { _proximityCheckCooldown--; return; }
            _proximityCheckCooldown = 3 + _rng.Next(4);

            Vec2 pos;
            try { pos = MobileParty.MainParty.GetPosition2D; } catch { return; }

            foreach (Hero h in Hero.AllAliveHeroes.ToList())
            {
                if (!ColourLordRegistry.IsAshenLord(h)) continue;
                var party = h.PartyBelongedTo;
                if (party == null) continue;
                Vec2 lPos;
                try { lPos = party.GetPosition2D; } catch { continue; }
                if ((lPos - pos).LengthSquared < 900f)
                {
                    string[] msgs =
                    {
                        "Something in you stirs — a shard is close. Aelisar grows urgent.",
                        "The presence sharpens without warning. One of them is near.",
                        "A cold pull, sudden and specific. A fragment, within reach.",
                        "You feel the shape of him — incomplete, close. One of the grey carries what you need.",
                    };
                    InformationManager.DisplayMessage(new InformationMessage(
                        msgs[_rng.Next(msgs.Length)],
                        new Color(0.75f, 0.45f, 0.25f)));
                    break;
                }
            }
        }

        // ── Cold conversion (player turned Ashen) ─────────────────────────────
        private static void TurnToCold()
        {
            if (_phase == PhaseColdActive) { TickColdQuest(); return; }
            if (_phase == PhaseColdDone)   return;

            bool inProgress = _phase == PhaseFirstContact
                           || _phase == PhaseFirstRefused
                           || _phase == PhaseActive
                           || _phase == PhaseAllDone;
            if (!inProgress) return;

            try { _questLog?.LogColdConversion(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            _questLog = null;

            _phase = PhaseColdActive;
            try { StartColdQuest(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { TickColdQuest(); }  catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
            if (_coldQuestLog == null) try { EnsureColdQuestLog(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            int owned = CountTowns(out int total);
            if (_coldTownTarget <= 0) _coldTownTarget = total;

            try { _coldQuestLog?.UpdateProgress(owned, _coldTownTarget); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            if (total > 0 && owned >= total)
            {
                try { _coldQuestLog?.LogComplete(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                _phase = PhaseColdDone;
                InformationManager.DisplayMessage(new InformationMessage(
                    "Calradia is yours. The cold inherits everything. There is nothing left to warm.",
                    new Color(0.40f, 0.55f, 0.80f)));
            }
        }

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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            return owned;
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
    }


    // ── Quest journal: The Sundered Crown ────────────────────────────────────
    public sealed class DragonQuestLog : QuestBase
    {
        public DragonQuestLog()
            : base("ldm_sundered_crown", Hero.MainHero, CampaignTime.Never, 0) { }

        public override TextObject Title => new TextObject("The Sundered Crown");
        public override bool IsRemainingTimeHidden => true;

        protected override void InitializeQuestOnGameLoad()
        {
            DragonQuestSystem._questLog = this;
        }

        protected override void RegisterEvents() { }
        protected override void SetDialogs() { }

        private JournalLog _objLords;
        private JournalLog _objRuin1;
        private JournalLog _objRuin2;
        private JournalLog _objRuin3;
        private JournalLog _objHeart;

        internal void LogStarted()
        {
            AddLog(new TextObject(
                "A presence touched you on the battlefield — ancient and urgent. " +
                "It is Aelisar Veth, the First Emperor, who shattered his soul into the Ashen cycle as a lock. " +
                "Seven of his lords carry his fragments. Three ruins hold his pact. " +
                "The Heart of Winter holds his purpose. Gather all of it."));
            EnsureObjectives();
        }

        private void EnsureObjectives()
        {
            if (_objLords != null && _objRuin1 != null && _objRuin2 != null
                && _objRuin3 != null && _objHeart != null) return;

            if (JournalEntries != null && JournalEntries.Count >= 6)
            {
                if (_objLords == null) _objLords = JournalEntries[1];
                if (_objRuin1 == null) _objRuin1 = JournalEntries[2];
                if (_objRuin2 == null) _objRuin2 = JournalEntries[3];
                if (_objRuin3 == null) _objRuin3 = JournalEntries[4];
                if (_objHeart == null) _objHeart = JournalEntries[5];
                return;
            }

            _objLords = AddDiscreteLog(
                new TextObject("Silence seven Ashen lords in battle — each one releases a shard of Aelisar."),
                new TextObject("Ashen Lords Silenced"), 0, DragonQuestSystem.TargetLordsSlain, null, false);
            _objRuin1 = AddDiscreteLog(
                new TextObject("Clear the Sunken Scriptorium (Dravend) — where the first covenant was written."),
                new TextObject("Sunken Scriptorium"), 0, 1, null, false);
            _objRuin2 = AddDiscreteLog(
                new TextObject("Clear the Shattered Throne (Epis) — where Aelisar's power was centred."),
                new TextObject("Shattered Throne"), 0, 1, null, false);
            _objRuin3 = AddDiscreteLog(
                new TextObject("Clear the Dragon's Tomb (Myzea) — where the original binding was made."),
                new TextObject("Dragon's Tomb"), 0, 1, null, false);
            _objHeart = AddDiscreteLog(
                new TextObject("Capture Tyal — the Heart of Winter — for your clan."),
                new TextObject("Heart of Winter"), 0, 1, null, false);
        }

        internal void UpdateProgress(int lords, int ruinsCleared, bool heartCaptured)
        {
            EnsureObjectives();
            try { _objLords?.UpdateCurrentProgress(Math.Min(lords, DragonQuestSystem.TargetLordsSlain)); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            bool[] cleared =
            {
                AshenRuinSystem.IsCleared(DragonQuestSystem.DestinedRuinVillages[0]),
                AshenRuinSystem.IsCleared(DragonQuestSystem.DestinedRuinVillages[1]),
                AshenRuinSystem.IsCleared(DragonQuestSystem.DestinedRuinVillages[2]),
            };
            try { _objRuin1?.UpdateCurrentProgress(cleared[0] ? 1 : 0); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { _objRuin2?.UpdateCurrentProgress(cleared[1] ? 1 : 0); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { _objRuin3?.UpdateCurrentProgress(cleared[2] ? 1 : 0); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { _objHeart?.UpdateCurrentProgress(heartCaptured ? 1 : 0); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        internal void LogLordSlain(int count) =>
            AddLog(new TextObject(
                $"An Ashen lord silenced — a shard of Aelisar returns. [{count}/{DragonQuestSystem.TargetLordsSlain}]"));

        internal void LogHeartCaptured() =>
            AddLog(new TextObject(
                "The Heart of Winter is taken. Aelisar's presence intensifies."));

        internal void LogAllDone() =>
            AddLog(new TextObject(
                "All of it gathered. Aelisar speaks clearly now — and he has a question for you."));

        internal void LogComplete(string ending) =>
            AddLog(new TextObject(ending));

        internal void LogColdConversion()
        {
            AddLog(new TextObject(
                "Your fire has gone out. The presence of Aelisar Veth — " +
                "who sacrificed himself to hold the cycle — recedes. " +
                "You are now the thing he spent himself to stop. The quest is closed."));
            CompleteQuestWithFail();
        }
    }


    // ── Quest journal: Bring the Eternal Cold ─────────────────────────────────
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
            try { _objConquer?.UpdateCurrentProgress(Math.Min(owned, Math.Max(target, 1))); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
