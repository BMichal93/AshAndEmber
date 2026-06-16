// =============================================================================
// ASH AND EMBER — EmberConclaveSystem.cs
// "The Ember Conclave" — a secret society of mage lords who believe the Ashen
// can be harnessed and directed as a weapon for human dominion.
//
// Their plan ends in ruin. The cold does not negotiate. It consumes.
//
// PHASES
//   Silent    (0–19 power)   — Conclave forms quietly; 3 mage lords seeded as members
//   Stirring  (20+ power)    — First contact: player chooses ally, enemy, or silence
//   Rising    (40+ power)    — Three missions offered on 21-day cooldown
//   Ascendant (60+ power)    — Puppet candidate chosen; corruption warnings begin
//   Hubris    (80+ power)    — The tragic culmination fires
//   Ended     (final)        — Dormant; journal records the outcome
//
// POWER SOURCES
//   Members present:         +1 per member per month (passive)
//   Player kills member:     -10 (removes from set)
//   Player completes mission:+15
//   Player declines mission: -5
//   Lord executed:           +3
//
// MISSIONS (Rising phase, max 1 active, 21-day cooldown between offers)
//   The First Binding  — Eliminate a named lord within 30 days
//   The Sealed Accord  — Visit a named settlement within 21 days
//   The Kindling Pact  — Keep the puppet candidate alive for 21 days
//
// SAVE KEYS  (prefix EC_)
//   EC_Power, EC_Phase, EC_Members, EC_Puppet, EC_Ally, EC_Enemy, EC_Seeded
//   EC_T1, EC_T2, EC_T3, EC_T4, EC_MissionCD, EC_Mission, EC_MTarget, EC_MDays
//   EC_CorrWarn, EC_CorrIdx
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace AshAndEmber
{
    public static partial class EmberConclaveSystem
    {
        // ── Phases ─────────────────────────────────────────────────────────────
        private const int PhaseSilent    = 0;
        private const int PhaseStirring  = 1;
        private const int PhaseRising    = 2;
        private const int PhaseAscendant = 3;
        private const int PhaseHubris    = 4;
        private const int PhaseEnded     = 5;

        // ── Mission types ──────────────────────────────────────────────────────
        internal const int MissionNone      = 0;
        internal const int MissionEliminate = 1;
        internal const int MissionVisit     = 2;
        internal const int MissionProtect   = 3;
        internal const int MissionRuin      = 4;

        // ── Tuning ─────────────────────────────────────────────────────────────
        private const int MaxMembers              = 5;
        private const int InitialMemberCount      = 3;
        private const int PowerTier1              = 20;
        private const int PowerTier2              = 40;
        private const int PowerTier3              = 60;
        private const int PowerTier4              = 80;
        private const int CollapseThreshold       = 2;
        private const int MissionCooldownDays     = 21;
        private const int MissionEliminateDays    = 30;
        private const int MissionRuinDays         = 28;
        private const int MissionVisitDays        = 21;
        private const int MissionProtectDays      = 21;
        private const int CorruptionWarningInterval = 30;
        private const float RecruitChancePerWeek  = 0.20f;

        // ── Persistent state ───────────────────────────────────────────────────
        private static int  _power             = 0;
        private static int  _phase             = PhaseSilent;
        private static readonly HashSet<string> _memberIds = new HashSet<string>();
        private static string _puppetCandidateId = null;
        private static bool _playerIsAlly      = false;
        private static bool _playerIsEnemy     = false;
        private static bool _seeded            = false;

        private static bool _tier1Fired = false;
        private static bool _tier2Fired = false;
        private static bool _tier3Fired = false;
        private static bool _tier4Fired = false;

        private static int    _missionCooldown      = 0;
        private static int    _activeMission        = MissionNone;
        private static string _missionTargetId      = null;
        private static int    _missionDaysRemaining = 0;
        private static int    _corruptionWarningTimer = 0;
        private static int    _corruptionWarningIndex = 0;

        private static int  _enemyFollowUpTimer  = 0;
        private static bool _counterContactFired = false;

        // ── Runtime (not persisted) ────────────────────────────────────────────
        private static readonly Random _rng = new Random();

        // ── Quest log refs ─────────────────────────────────────────────────────
        internal static EmberConclaveMainLog      _mainLog      = null;
        internal static EmberConclaveEliminateLog _eliminateLog = null;
        internal static EmberConclaveVisitLog     _visitLog     = null;
        internal static EmberConclaveProtectLog   _protectLog   = null;
        internal static EmberConclaveRuinLog      _ruinLog      = null;

        // ── Public API ─────────────────────────────────────────────────────────
        public static bool IsActive  => _phase > PhaseSilent && _phase < PhaseEnded;
        public static bool IsMember(Hero h) => h != null && _memberIds.Contains(h.StringId);

        // ── Event hooks ────────────────────────────────────────────────────────

        public static void OnHeroKilled(Hero victim, Hero killer)
        {
            if (victim == null || _phase == PhaseEnded) return;

            bool wasMember = _memberIds.Remove(victim.StringId);
            if (wasMember)
            {
                AddPower(-10);
                try { _mainLog?.LogMemberLost(victim.Name?.ToString() ?? "an ember"); } catch { }
                CheckDefeat();
            }

            // Eliminate mission: target died
            if (_activeMission == MissionEliminate && victim.StringId == _missionTargetId)
                CompleteMissionSuccess();

            // Protect mission: candidate died — immediate failure
            if (_activeMission == MissionProtect && victim.StringId == _puppetCandidateId)
                FailActiveMission("The candidate fell before the hour was right.");
        }

        public static void OnLordExecuted()
        {
            if (_phase == PhaseSilent || _phase == PhaseEnded) return;
            AddPower(3);
        }

        public static void OnSettlementEntered(Settlement settlement)
        {
            if (_activeMission == MissionVisit && settlement?.StringId == _missionTargetId)
                CompleteMissionSuccess();
        }

        // ── Monthly tick ───────────────────────────────────────────────────────

        public static void OnMonthlyTick()
        {
            if (_phase == PhaseEnded) return;

            if (_memberIds.Count > 0)
                AddPower(_memberIds.Count);

            if (_phase == PhaseAscendant && _puppetCandidateId != null)
            {
                _corruptionWarningTimer--;
                if (_corruptionWarningTimer <= 0)
                {
                    FireCorruptionWarning();
                    _corruptionWarningTimer = CorruptionWarningInterval;
                }
            }

            CheckTierTransitions();
        }

        // ── Weekly tick ────────────────────────────────────────────────────────

        public static void WeeklyTick()
        {
            if (_phase == PhaseEnded) return;

            TryRecruit();

            if (_phase >= PhaseRising && _playerIsAlly && _missionCooldown <= 0 && _activeMission == MissionNone)
                TryOfferMission();
        }

        // ── Daily tick ─────────────────────────────────────────────────────────

        public static void DailyTick()
        {
            if (_phase == PhaseEnded) return;

            if (!_seeded)
            {
                _seeded = true;
                try { TrySeedInitialMembers(); } catch { }
            }

            if (_phase >= PhaseStirring && _mainLog == null)
                try { EnsureMainLog(); } catch { }

            if (_missionCooldown > 0) _missionCooldown--;

            if (_playerIsEnemy && !_counterContactFired && _enemyFollowUpTimer > 0)
            {
                _enemyFollowUpTimer--;
                if (_enemyFollowUpTimer <= 0)
                {
                    try
                    {
                        if (MageKnowledge._deferredInquiry == null)
                        {
                            _counterContactFired = true;
                            MageKnowledge._deferredInquiry = ShowCounterContactInquiry;
                        }
                        else
                        {
                            _enemyFollowUpTimer = 1;
                        }
                    }
                    catch { }
                }
            }

            if (_activeMission != MissionNone)
            {
                _missionDaysRemaining--;

                if (_activeMission == MissionProtect)
                {
                    bool puppetAlive = false;
                    try
                    {
                        puppetAlive = _puppetCandidateId != null &&
                            Hero.AllAliveHeroes.Any(h => h.StringId == _puppetCandidateId && h.IsAlive);
                    }
                    catch { }

                    if (!puppetAlive)
                        FailActiveMission("The candidate fell before the hour was right.");
                    else if (_missionDaysRemaining <= 0)
                        CompleteMissionSuccess();
                }
                else if (_missionDaysRemaining <= 0)
                {
                    FailActiveMission("The hour passed. The Conclave noted the silence.");
                }
            }
        }

        // ── Tier transitions ───────────────────────────────────────────────────

        private static void CheckTierTransitions()
        {
            if (_phase == PhaseEnded) return;

            if (!_tier1Fired && _power >= PowerTier1)
            {
                _tier1Fired = true;
                _phase = PhaseStirring;
                try { EnsureMainLog(); } catch { }
                try { _mainLog?.LogFirstContact(); } catch { }
                try { MageKnowledge._deferredInquiry = ShowFirstContactInquiry; } catch { }
            }

            if (!_tier2Fired && _power >= PowerTier2)
            {
                _tier2Fired = true;
                _phase = PhaseRising;
                if (_playerIsEnemy)
                {
                    try { _mainLog?.LogEnemyRisingWarning(); } catch { }
                    try
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The fires at the Ashen ruins burn differently now. Something is being coaxed, not extinguished.",
                            new Color(0.6f, 0.35f, 0.35f)));
                    }
                    catch { }
                }
                else
                {
                    try { _mainLog?.LogRisingPhase(); } catch { }
                }
            }

            if (!_tier3Fired && _power >= PowerTier3)
            {
                _tier3Fired = true;
                _phase = PhaseAscendant;
                try { ChoosePuppetCandidate(); } catch { }
                _corruptionWarningTimer = CorruptionWarningInterval;
                if (_playerIsEnemy)
                {
                    try { _mainLog?.LogEnemyAscendantWarning(); } catch { }
                    try
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            "A mage lord has been seen at three different Ashen sites in a month. The Conclave has chosen its vessel.",
                            new Color(0.6f, 0.35f, 0.35f)));
                    }
                    catch { }
                }
            }

            if (!_tier4Fired && _power >= PowerTier4)
            {
                _tier4Fired = true;
                _phase = PhaseHubris;
                try { FireCulmination(); } catch { }
            }
        }

        // ── Recruitment ────────────────────────────────────────────────────────

        private static void TrySeedInitialMembers()
        {
            var eligible = Hero.AllAliveHeroes
                .Where(h => h.IsLord && h.IsAlive && h != Hero.MainHero
                         && ColourLordRegistry.IsColourLord(h)
                         && !ColourLordRegistry.IsAshenLord(h))
                .OrderBy(_ => _rng.Next())
                .Take(InitialMemberCount)
                .ToList();
            foreach (var h in eligible)
                _memberIds.Add(h.StringId);
        }

        private static void TryRecruit()
        {
            if (_memberIds.Count >= MaxMembers) return;
            if (_rng.NextDouble() > RecruitChancePerWeek) return;

            try
            {
                var existing = _memberIds;
                var candidate = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && h != Hero.MainHero
                             && ColourLordRegistry.IsColourLord(h)
                             && !ColourLordRegistry.IsAshenLord(h)
                             && !existing.Contains(h.StringId)
                             && h.MapFaction != Hero.MainHero?.MapFaction)
                    .OrderBy(_ => _rng.Next())
                    .FirstOrDefault();
                if (candidate != null)
                    _memberIds.Add(candidate.StringId);
            }
            catch { }
        }

        // ── Puppet candidate ───────────────────────────────────────────────────

        private static void ChoosePuppetCandidate()
        {
            try
            {
                var candidate = _memberIds
                    .Select(id =>
                    {
                        try { return Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == id && h.IsAlive); }
                        catch { return null; }
                    })
                    .Where(h => h != null && !ColourLordRegistry.IsAshenLord(h))
                    .OrderByDescending(h => { try { return h.Clan?.Renown ?? 0f; } catch { return 0f; } })
                    .FirstOrDefault();

                if (candidate == null)
                    candidate = Hero.AllAliveHeroes
                        .Where(h => h.IsLord && h.IsAlive && h != Hero.MainHero
                                 && ColourLordRegistry.IsColourLord(h)
                                 && !ColourLordRegistry.IsAshenLord(h))
                        .OrderByDescending(h => { try { return h.Clan?.Renown ?? 0f; } catch { return 0f; } })
                        .FirstOrDefault();

                if (candidate == null) return;
                _puppetCandidateId = candidate.StringId;
                try { _mainLog?.LogPuppetChosen(candidate.Name?.ToString() ?? "a lord"); } catch { }
            }
            catch { }
        }

        // ── Missions ───────────────────────────────────────────────────────────

        private static void TryOfferMission()
        {
            if (_memberIds.Count == 0) return;

            int missionType = PickNextMissionType();
            if (missionType == MissionNone) return;

            if (missionType == MissionEliminate)
            {
                Hero target = null;
                try
                {
                    target = Hero.AllAliveHeroes
                        .Where(h => h.IsLord && h.IsAlive && h != Hero.MainHero
                                 && !ColourLordRegistry.IsColourLord(h)
                                 && h.MapFaction != Hero.MainHero?.MapFaction)
                        .OrderBy(_ => _rng.Next())
                        .FirstOrDefault();
                }
                catch { }
                if (target == null) return;

                string targetName = target.Name?.ToString() ?? "a lord";
                _activeMission        = MissionEliminate;
                _missionTargetId      = target.StringId;
                _missionDaysRemaining = MissionEliminateDays;

                try
                {
                    _eliminateLog = new EmberConclaveEliminateLog();
                    _eliminateLog.StartQuest();
                    _eliminateLog.LogOpened(targetName, MissionEliminateDays);
                }
                catch { }

                try { MageKnowledge._deferredInquiry = () => ShowMissionOffer(MissionEliminate, targetName); } catch { }
            }
            else if (missionType == MissionVisit)
            {
                Settlement target = null;
                try
                {
                    target = Settlement.All
                        .Where(s => s.IsTown
                                 && s.OwnerClan != null
                                 && s.OwnerClan != Hero.MainHero?.Clan)
                        .OrderBy(_ => _rng.Next())
                        .FirstOrDefault();
                }
                catch { }
                if (target == null) return;

                string targetName = target.Name?.ToString() ?? "a settlement";
                _activeMission        = MissionVisit;
                _missionTargetId      = target.StringId;
                _missionDaysRemaining = MissionVisitDays;

                try
                {
                    _visitLog = new EmberConclaveVisitLog();
                    _visitLog.StartQuest();
                    _visitLog.LogOpened(targetName, MissionVisitDays);
                }
                catch { }

                try { MageKnowledge._deferredInquiry = () => ShowMissionOffer(MissionVisit, targetName); } catch { }
            }
            else if (missionType == MissionProtect && _puppetCandidateId != null)
            {
                Hero puppet = null;
                try { puppet = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == _puppetCandidateId && h.IsAlive); }
                catch { }
                if (puppet == null) return;

                string targetName = puppet.Name?.ToString() ?? "the candidate";
                _activeMission        = MissionProtect;
                _missionTargetId      = _puppetCandidateId;
                _missionDaysRemaining = MissionProtectDays;

                try
                {
                    _protectLog = new EmberConclaveProtectLog();
                    _protectLog.StartQuest();
                    _protectLog.LogOpened(targetName, MissionProtectDays);
                }
                catch { }

                try { MageKnowledge._deferredInquiry = () => ShowMissionOffer(MissionProtect, targetName); } catch { }
            }
            else if (missionType == MissionRuin)
            {
                RuinDef targetRuin = null;
                Settlement targetSettlement = null;
                try
                {
                    var eligible = AshenRuinDefs.All
                        .Where(r => !AshenRuinSystem.IsCleared(r.VillageName)
                                 && !AshenRuinSystem.IsOnCooldown(r.VillageName))
                        .OrderBy(_ => _rng.Next())
                        .ToList();
                    foreach (var ruin in eligible)
                    {
                        var s = Settlement.All.FirstOrDefault(x =>
                            x.IsVillage &&
                            string.Equals(x.Name?.ToString()?.Trim(), ruin.VillageName,
                                          StringComparison.OrdinalIgnoreCase));
                        if (s == null) continue;
                        targetRuin       = ruin;
                        targetSettlement = s;
                        break;
                    }
                }
                catch { }
                if (targetRuin == null || targetSettlement == null) return;

                string ruinName = targetRuin.RuinName ?? targetRuin.VillageName;
                _activeMission        = MissionRuin;
                _missionTargetId      = targetSettlement.StringId;
                _missionDaysRemaining = MissionRuinDays;

                try
                {
                    _ruinLog = new EmberConclaveRuinLog();
                    _ruinLog.StartQuest();
                    _ruinLog.LogOpened(ruinName, MissionRuinDays);
                }
                catch { }

                try { MageKnowledge._deferredInquiry = () => ShowMissionOffer(MissionRuin, ruinName); } catch { }
            }
        }

        private static int PickNextMissionType()
        {
            if (_phase >= PhaseAscendant && _puppetCandidateId != null)
            {
                int[] pool = { MissionEliminate, MissionVisit, MissionProtect, MissionRuin };
                return pool[_rng.Next(pool.Length)];
            }
            int[] risingPool = { MissionEliminate, MissionVisit, MissionRuin };
            return risingPool[_rng.Next(risingPool.Length)];
        }

        private static void CompleteMissionSuccess()
        {
            if (_activeMission == MissionNone) return;
            AddPower(15);
            try { GetActiveMissionLog()?.LogSuccess(); } catch { }
            try { GetActiveMissionLog()?.CompleteSuccess(); } catch { }
            ClearActiveMission();
        }

        private static void FailActiveMission(string reason)
        {
            if (_activeMission == MissionNone) return;
            AddPower(-5);
            try { GetActiveMissionLog()?.LogFailed(reason); } catch { }
            try { GetActiveMissionLog()?.CompleteFail(); } catch { }
            ClearActiveMission();
        }

        private static void ClearActiveMission()
        {
            _activeMission        = MissionNone;
            _missionTargetId      = null;
            _missionDaysRemaining = 0;
            _missionCooldown      = MissionCooldownDays;
        }

        private static EmberConclaveMissionLogBase GetActiveMissionLog()
        {
            return _activeMission switch
            {
                MissionEliminate => (EmberConclaveMissionLogBase)_eliminateLog,
                MissionVisit     => (EmberConclaveMissionLogBase)_visitLog,
                MissionProtect   => (EmberConclaveMissionLogBase)_protectLog,
                MissionRuin      => (EmberConclaveMissionLogBase)_ruinLog,
                _                => null,
            };
        }

        // ── Corruption warnings ────────────────────────────────────────────────

        private static readonly string[] _corruptionWarnings =
        {
            "{0} reports that his fire now burns with a quality he describes as 'directed cold'. He considers it a refinement. The Conclave agrees.",
            "Word arrives: {0} no longer requires sleep. He calls it clarity of purpose. The inner circle is calling it a breakthrough.",
            "{0} writes that the Ashen no longer feel like opposition — more like a current he moves with. The Conclave is certain this is what control feels like.",
            "The candidate sends an enthusiastic report. The rituals are accelerating faster than projected. He is eager to continue. More eager than before.",
            "{0} notes that his fire and the cold now feel like the same thing. He assures them he remains in command. The Conclave takes him at his word.",
        };

        private static void FireCorruptionWarning()
        {
            try
            {
                if (_puppetCandidateId == null) return;
                var puppet = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == _puppetCandidateId && h.IsAlive);
                if (puppet == null) return;

                string name = puppet.Name?.ToString() ?? "The candidate";
                string text = string.Format(
                    _corruptionWarnings[_corruptionWarningIndex % _corruptionWarnings.Length], name);
                _corruptionWarningIndex++;

                try { _mainLog?.LogCorruptionWarning(text); } catch { }
                InformationManager.DisplayMessage(new InformationMessage(text, new Color(0.45f, 0.45f, 0.65f)));
            }
            catch { }
        }

        // ── Culmination ────────────────────────────────────────────────────────

        private static void FireCulmination()
        {
            if (_playerIsAlly)
                MageKnowledge._deferredInquiry = ShowAllyEnding;
            else
                MageKnowledge._deferredInquiry = ShowNeutralEnding;
        }

        private static void ShowAllyEnding()
        {
            string puppetName = ResolvePuppetName();
            string body =
                $"The Conclave summons you to witness the binding.\n\n" +
                $"The hall is cold. The candles do not gutter — they simply die, one by one, as you enter. " +
                $"{puppetName} sits in the high seat. He does not rise. He does not blink.\n\n" +
                $"The Conclave members stand in a ring around him, chanting the words they spent years learning. " +
                $"They believe they are directing the cold. They believe the Ashen kneels to the fire inside a chosen vessel.\n\n" +
                $"They are wrong.\n\n" +
                $"{puppetName} turns to look at you. His eyes have no warmth left in them. They have not had warmth for some time. " +
                $"Something behind them smiles — not with his face, but with the shape of his face.\n\n" +
                $"The Conclave does not see it yet. They are still chanting.\n\n" +
                $"You understand, in this moment, that the binding worked perfectly. " +
                $"Just not the way they intended.";

            InformationManager.ShowInquiry(new InquiryData(
                "The Ember Throne",
                body,
                true, false,
                "Leave the hall.",
                "",
                () => { try { ApplyCulmination(allyPath: true); } catch { } },
                () => { try { ApplyCulmination(allyPath: true); } catch { } }
            ), true, true);
        }

        private static void ShowNeutralEnding()
        {
            string body =
                "Word reaches you of what happened in the Conclave's hall.\n\n" +
                "The candidate was found in his seat three days after the rite — cold to the touch, " +
                "still moving. Still speaking. Those who heard him say the words were not his own.\n\n" +
                "Of the Conclave members who attended the rite, none returned home.\n\n" +
                "The Ember Conclave believed that the fire inside a mage was proof against the cold — " +
                "that Inner Fire could not be hollowed, only directed.\n\n" +
                "They were correct. The cold cannot extinguish the fire.\n\n" +
                "They simply failed to understand that the cold can move into the fire and wear it like a coat.";

            InformationManager.ShowInquiry(new InquiryData(
                "The Shape of Cold",
                body,
                true, false,
                "Remember this.",
                "",
                () => { try { ApplyCulmination(allyPath: false); } catch { } },
                () => { try { ApplyCulmination(allyPath: false); } catch { } }
            ), true, true);
        }

        private static void ApplyCulmination(bool allyPath)
        {
            _phase = PhaseEnded;

            try
            {
                if (_puppetCandidateId != null)
                {
                    var puppet = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == _puppetCandidateId && h.IsAlive);
                    if (puppet != null)
                        ColourLordRegistry.SetAshen(puppet, true);
                }
            }
            catch { }

            foreach (var id in _memberIds.ToList())
            {
                try
                {
                    var h = Hero.AllAliveHeroes.FirstOrDefault(x => x.StringId == id && x.IsAlive);
                    if (h != null) ColourLordRegistry.SetAshen(h, true);
                }
                catch { }
            }

            _memberIds.Clear();

            if (allyPath)
            {
                try { _mainLog?.LogAllyEnding(); } catch { }
                try { _mainLog?.CompleteFail(); } catch { }
            }
            else
            {
                try { _mainLog?.LogNeutralEnding(); } catch { }
                try { _mainLog?.CompleteFail(); } catch { }
            }
        }

        // ── Defeat ─────────────────────────────────────────────────────────────

        private static void CheckDefeat()
        {
            if (_phase == PhaseEnded || _phase == PhaseSilent) return;
            if (_memberIds.Count < CollapseThreshold && _power < PowerTier1)
            {
                _phase = PhaseEnded;
                try { _mainLog?.LogDefeat(); } catch { }
                try { _mainLog?.CompleteSuccess(); } catch { }
                InformationManager.DisplayMessage(new InformationMessage(
                    "The Ember Conclave has collapsed. Their records reveal the full shape of what they intended.",
                    new Color(0.7f, 0.5f, 0.3f)));
            }
        }

        // ── Power helper ───────────────────────────────────────────────────────

        private static void AddPower(int delta)
        {
            _power = Math.Max(0, _power + delta);
        }

        // ── First contact inquiry ──────────────────────────────────────────────

        private static void ShowFirstContactInquiry()
        {
            const string body =
                "A letter arrives with your provisions. No signature. The seal is an ember — " +
                "a coal half-spent, pressed in dark wax.\n\n" +
                "\"You carry the fire. So do we. Most mages see only the enemy in the cold — " +
                "the thing that stills the flame and freezes the world. We have spent years learning to see otherwise.\n\n" +
                "The Ashen are not an enemy. They are a force. Fire is a force. Neither cares about you " +
                "unless you reach into them. We have learned how to reach.\n\n" +
                "The throne of Calradia will belong to whoever learns to stand between human will and Ashen power. " +
                "We believe that is possible. We believe we are close.\n\n" +
                "We have watched your fire for some time. Will you hear more?\"";

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "The Ember Seal",
                body,
                new List<InquiryElement>
                {
                    new InquiryElement("ally", "Tell me more.", null, true,
                        "You will hear the Conclave's full design. They will offer you work. What you do with the knowledge is your own."),
                    new InquiryElement("enemy", "The Ashen cannot be directed. You are fools or worse.", null, true,
                        "You close the letter. These people are dangerous in ways they do not understand."),
                    new InquiryElement("ignore", "This letter never reached me.", null, true,
                        "You burn the letter. The seal turns the same colour as the ashes in your campfire."),
                },
                false, 1, 1,
                "Decide.",
                "",
                chosen =>
                {
                    try { HandleFirstContactChoice(chosen?[0]?.Identifier as string ?? "ignore"); } catch { }
                },
                null, "", false
            ), false);
        }

        private static void HandleFirstContactChoice(string choice)
        {
            switch (choice)
            {
                case "ally":
                    _playerIsAlly = true;
                    try { _mainLog?.LogPlayerAllied(); } catch { }
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The Ember Conclave has taken note of your answer. They will be in touch.",
                        new Color(0.75f, 0.5f, 0.3f)));
                    break;

                case "enemy":
                    _playerIsEnemy = true;
                    _enemyFollowUpTimer = 7;
                    try { _mainLog?.LogPlayerOpposed(); } catch { }
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The Ember Conclave now knows you are watching. Their plans will not stop.",
                        new Color(0.7f, 0.35f, 0.25f)));
                    break;

                default:
                    try { _mainLog?.LogPlayerIgnored(); } catch { }
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The ember seal crumbles to dust. Some things cannot simply be left alone.",
                        new Color(0.5f, 0.5f, 0.5f)));
                    break;
            }
        }

        // ── Counter-contact inquiry ────────────────────────────────────────────

        private static void ShowCounterContactInquiry()
        {
            const string body =
                "You were not the only one who refused them.\n\n" +
                "The messenger who waited while the ember seal cooled — he did not return to the Conclave. " +
                "You are being told this so that you understand: there are others in Calradia who know what " +
                "the Ember Conclave is building, and who believe it will end badly for everyone in the hall.\n\n" +
                "What are they building?\n\n" +
                "A binding. A vessel. They intend to pour fire and cold into the same cup and hold it steady. " +
                "They will find out they are wrong only when the cup breaks — and when it does, it will not " +
                "break quietly.\n\n" +
                "The circle requires its members. Fewer embers make for a colder fire. " +
                "The candidate is not yet chosen — when he is, you will know it, because the Conclave's " +
                "confidence will begin to outpace its judgment.\n\n" +
                "No signature. The fewer names attached to this, the better.";

            InformationManager.ShowInquiry(new InquiryData(
                "The Other Letter",
                body,
                true, false,
                "Burn this one too.",
                "",
                () => { try { _mainLog?.LogCounterContact(); } catch { } },
                () => { try { _mainLog?.LogCounterContact(); } catch { } }
            ), true, true);
        }

        // ── Mission offer inquiry ──────────────────────────────────────────────

        private static void ShowMissionOffer(int missionType, string targetName)
        {
            string title, body, accept, decline;
            switch (missionType)
            {
                case MissionEliminate:
                    title   = "The First Binding";
                    body    = $"A courier brings a short note.\n\n" +
                              $"\"There is a lord who has become an obstacle to our preparations — {targetName}. " +
                              $"He has spoken to too many ears about things he should not have heard. " +
                              $"His silence would be a gift to those who understand what is coming. " +
                              $"You have {MissionEliminateDays} days.\"";
                    accept  = "Consider it done.";
                    decline = "Find someone else.";
                    break;

                case MissionVisit:
                    title   = "The Sealed Accord";
                    body    = $"An unsigned letter asks you to attend a gathering.\n\n" +
                              $"\"There are things you must witness before you can understand the scale of what we are building. " +
                              $"Come to {targetName}. You have {MissionVisitDays} days. " +
                              $"Ask for no one by name when you arrive.\"";
                    accept  = "I will be there.";
                    decline = "I cannot make that journey now.";
                    break;

                case MissionProtect:
                    title   = "The Kindling Pact";
                    body    = $"The note arrives in a sealed case.\n\n" +
                              $"\"Our candidate has been noticed. There are those who would see {targetName} fall " +
                              $"before the rite can be completed. Ensure he is still breathing in {MissionProtectDays} days. " +
                              $"The Conclave does not forget those who protect the flame.\"";
                    accept  = "He will live.";
                    decline = "This is not my fight.";
                    break;

                default: // MissionRuin
                    title   = "The Binding Ground";
                    body    = $"A sealed letter marked with the conclave's cipher.\n\n" +
                              $"\"Every great working requires a place where fire and cold have already met — " +
                              $"a site where the boundary between them has thinned. We have identified such a place: {targetName}.\n\n" +
                              $"Go there. Stand in it. The site will do the rest. " +
                              $"You will know when it is done. You have {MissionRuinDays} days.\"";
                    accept  = "I will go.";
                    decline = "Send someone else.";
                    break;
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                title, body,
                new List<InquiryElement>
                {
                    new InquiryElement("accept", accept, null, true, ""),
                    new InquiryElement("decline", decline, null, true, "The Conclave will note your reluctance."),
                },
                false, 1, 1,
                "Respond.", "",
                chosen =>
                {
                    try
                    {
                        if (chosen?[0]?.Identifier as string == "accept")
                        {
                            try { GetActiveMissionLog()?.LogAccepted(); } catch { }
                        }
                        else
                        {
                            AddPower(-5);
                            try { GetActiveMissionLog()?.LogDeclined(); } catch { }
                            try { GetActiveMissionLog()?.CompleteFail(); } catch { }
                            ClearActiveMission();
                        }
                    }
                    catch { }
                },
                null, "", false
            ), false);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static string ResolvePuppetName()
        {
            try
            {
                if (_puppetCandidateId == null) return "your candidate";
                var h = Hero.AllAliveHeroes.FirstOrDefault(p => p.StringId == _puppetCandidateId)
                     ?? Hero.AllDeadOrDisabledHeroes.FirstOrDefault(p => p.StringId == _puppetCandidateId);
                return h?.Name?.ToString() ?? "your candidate";
            }
            catch { return "your candidate"; }
        }

        private static void EnsureMainLog()
        {
            if (_mainLog != null) return;
            _mainLog = new EmberConclaveMainLog();
            _mainLog.StartQuest();
            _mainLog.LogOpened();
        }

        // ── Save / Load ────────────────────────────────────────────────────────

        public static void Save(IDataStore store)
        {
            store.SyncData("EC_Power",     ref _power);
            store.SyncData("EC_Phase",     ref _phase);
            store.SyncData("EC_Mission",   ref _activeMission);
            store.SyncData("EC_MTarget",   ref _missionTargetId);
            store.SyncData("EC_MDays",     ref _missionDaysRemaining);
            store.SyncData("EC_MissionCD", ref _missionCooldown);
            store.SyncData("EC_Puppet",    ref _puppetCandidateId);
            store.SyncData("EC_CorrWarn",  ref _corruptionWarningTimer);
            store.SyncData("EC_CorrIdx",   ref _corruptionWarningIndex);

            var memberList = _memberIds.ToList();
            store.SyncData("EC_Members", ref memberList);
            if (memberList != null) { _memberIds.Clear(); foreach (var id in memberList) _memberIds.Add(id); }

            int seeded  = _seeded              ? 1 : 0; store.SyncData("EC_Seeded",  ref seeded);  _seeded              = seeded  != 0;
            int ally    = _playerIsAlly        ? 1 : 0; store.SyncData("EC_Ally",    ref ally);    _playerIsAlly        = ally    != 0;
            int enemy   = _playerIsEnemy       ? 1 : 0; store.SyncData("EC_Enemy",   ref enemy);   _playerIsEnemy       = enemy   != 0;
            int t1      = _tier1Fired          ? 1 : 0; store.SyncData("EC_T1",      ref t1);      _tier1Fired          = t1      != 0;
            int t2      = _tier2Fired          ? 1 : 0; store.SyncData("EC_T2",      ref t2);      _tier2Fired          = t2      != 0;
            int t3      = _tier3Fired          ? 1 : 0; store.SyncData("EC_T3",      ref t3);      _tier3Fired          = t3      != 0;
            int t4      = _tier4Fired          ? 1 : 0; store.SyncData("EC_T4",      ref t4);      _tier4Fired          = t4      != 0;
            int ccf     = _counterContactFired ? 1 : 0; store.SyncData("EC_CCFired", ref ccf);     _counterContactFired = ccf     != 0;
            store.SyncData("EC_EnemyTimer", ref _enemyFollowUpTimer);
        }

        public static void ResetForNewGame()
        {
            _power                  = 0;
            _phase                  = PhaseSilent;
            _memberIds.Clear();
            _puppetCandidateId      = null;
            _playerIsAlly           = false;
            _playerIsEnemy          = false;
            _seeded                 = false;
            _tier1Fired             = false;
            _tier2Fired             = false;
            _tier3Fired             = false;
            _tier4Fired             = false;
            _missionCooldown        = 0;
            _activeMission          = MissionNone;
            _missionTargetId        = null;
            _missionDaysRemaining   = 0;
            _corruptionWarningTimer = 0;
            _corruptionWarningIndex = 0;
            _enemyFollowUpTimer     = 0;
            _counterContactFired    = false;
            _mainLog                = null;
            _eliminateLog           = null;
            _visitLog               = null;
            _protectLog             = null;
            _ruinLog                = null;
        }
    }
}
