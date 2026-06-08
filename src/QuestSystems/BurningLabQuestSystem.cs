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
//   4. Give Rhagea  → Questline A (empire_s)
//   5. Give Lucorn  → Questline A (empire_n)
//   6. Give Gairos  → Questline A (empire_w)
//   7–11. Give non-imperial faction → Questline B
//
// QUESTLINE A — The Resurrection of Arencios
//   Phase 0: 3-day delay  → "Rituals begin in secret…"
//   Phase 1: 10-day delay → Arencios possesses a random male lord;
//              secretly rolled: true emperor or false emperor (Ashen spirit)
//   Phase 2: 3-day delay  → other two empire factions: 50 % chance to ally
//              (peace + shared wars; no clan movement to keep kingdoms alive)
//   Phase 3: 3-day delay  → Arencios declares war on all non-imperial factions
//   Phase 4: active monitoring:
//              • detect Arencios hero death → empire split (phase 5)
//              • false emperor: 30-day timer → ally with Ashen (peace maintained daily)
//   Phase 5: empire split — distribute Arencios empire fiefs to surviving empires
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
    public static class BurningLabQuestSystem
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
        private const int  QAFalseAllianceDelay = 30;

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

        private static readonly Random _rng = new Random();

        // ── Public API ─────────────────────────────────────────────────────────

        /// Returns true if <paramref name="h"/> is the lord currently possessed by Arenicos.
        public static bool IsArenicosHero(Hero h) =>
            h != null && _arenicosHeroId != null && h.StringId == _arenicosHeroId;

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
                    "Give it to Rhagea — perhaps she can use it to restore the Empire.",
                    "The Southern Empire receives the scrolls. Rhagea's scholars will study them.");
                AddImperialOption(elements, "empire_n", "give_n",
                    "Give it to Lucorn — the North has the scholars for this.",
                    "The Northern Empire receives the scrolls. Lucorn's court will decide their fate.");
                AddImperialOption(elements, "empire_w", "give_w",
                    "Give it to Gairos — the West has the ambition for it.",
                    "The Western Empire receives the scrolls. Gairos's mages will not sleep for weeks.");

                // Non-imperial factions
                AddFactionOption(elements, "sturgia",  "give_sturgia",  "Give it to the Sturgians — iron hands may steady iron knowledge.",
                    "Sturgia receives the scrolls. What they do with fire-made-life is their own affair.");
                AddFactionOption(elements, "khuzait",  "give_khuzait",  "Give it to the Khuzaites — the steppe lords keep their own counsel.",
                    "The Khuzait Khanate receives the scrolls. The wind carries secrets far.");
                AddFactionOption(elements, "battania", "give_battania", "Give it to the Battanians — they understand old power.",
                    "Battania receives the scrolls. They know things about life and death that predate the Empire.");
                AddFactionOption(elements, "aserai",   "give_aserai",   "Give it to the Aserai — scholars of the deep south.",
                    "The Aserai receive the scrolls. Desert silence is good for dangerous research.");
                AddFactionOption(elements, "vlandia",  "give_vlandia",  "Give it to the Vlandians — wealth buys discretion.",
                    "Vlandia receives the scrolls. Their coin and their ambition will put these to use.");

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
                        catch { }
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
            catch { }
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
            catch { }
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
            _qbSubPhase              = 0;
            _qbTimer                 = 0;
            _qbFactionId             = null;
            _qbOutcome               = 0;
            _qbSettlementQueue.Clear();
            _qbConversionTimer       = 0;
            _qbWeeklyBoostCooldown   = 0;
            _qcActive                = false;
            _qcWeeklyTimer           = 0;
        }

        // ── Initial choice handler ─────────────────────────────────────────────

        private static void HandleInitialChoice(string id)
        {
            switch (id)
            {
                case "destroy":
                    ShiftHonour(+1);
                    Notify(
                        "The Burning Laboratory — you fed the scrolls to the nearest torch one by one. " +
                        "It took longer than you expected; the parchment resisted, as if it remembered what was written on it. " +
                        "The last one burned green. The laboratory is ash and silence now. " +
                        "Some things should not be known. You made certain they would not be.");
                    _phase = PhaseEnded;
                    break;

                case "keep":
                    Notify(
                        "The Burning Laboratory — the scrolls are yours now. You have not read them in full. " +
                        "You are not certain you are ready to. But they are in your saddlebag, " +
                        "wrapped in oilcloth, and they have not stopped feeling warm since you picked them up.");
                    _phase    = PhaseQC;
                    _qcActive = true;
                    _qcWeeklyTimer = QCWeeklyDelay;
                    break;

                case "sell":
                    GainGold(10000);
                    ShiftHonour(-1);
                    // 50 % chance the buyer is connected to an imperial court
                    bool soldToImperial = _rng.Next(100) < SellImperialChance;
                    string receivingEmpireId = soldToImperial ? PickLivingImperialEmpireId() : null;
                    if (receivingEmpireId != null)
                    {
                        Kingdom emp = Kingdom.All.FirstOrDefault(k => k.StringId == receivingEmpireId && !k.IsEliminated);
                        string empName = emp?.Name?.ToString() ?? "an imperial court";
                        Notify(
                            "The Burning Laboratory — the merchant paid without negotiating. That alone should have told you. " +
                            $"Word reaches you three days later: the scrolls were delivered to {empName}. " +
                            "Their scholars are already at work.");
                        StartQuestlineA(receivingEmpireId);
                    }
                    else
                    {
                        Notify(
                            "The Burning Laboratory — the merchant paid without negotiating. The scrolls left in a locked chest " +
                            "on the back of a horse you never saw again. " +
                            "You do not know where they went. You have been telling yourself that is better.");
                        _phase = PhaseEnded;
                    }
                    break;

                case "give_s": StartQuestlineA("empire_s"); break;
                case "give_n": StartQuestlineA("empire_n"); break;
                case "give_w": StartQuestlineA("empire_w"); break;

                case "give_sturgia":  StartQuestlineB("sturgia");  break;
                case "give_khuzait":  StartQuestlineB("khuzait");  break;
                case "give_battania": StartQuestlineB("battania"); break;
                case "give_aserai":   StartQuestlineB("aserai");   break;
                case "give_vlandia":  StartQuestlineB("vlandia");  break;

                default:
                    ShiftHonour(+1);
                    _phase = PhaseEnded;
                    break;
            }
        }

        // ── Questline A ────────────────────────────────────────────────────────

        private static void StartQuestlineA(string empireId)
        {
            _phase        = PhaseQA;
            _qaEmpireId   = empireId;
            _qaSubPhase   = 0;
            _qaTimer      = QAPhaseRitualDelay;

            Kingdom emp = Kingdom.All.FirstOrDefault(k => k.StringId == empireId && !k.IsEliminated);
            string empName = emp?.Name?.ToString() ?? "the Empire";
            Notify(
                $"The Burning Laboratory — the scrolls have been delivered to {empName}. " +
                "They are not the kind of people who read slowly.");
        }

        private static void TickQA()
        {
            // Maintain false-emperor Ashen alliance (keep peace with Ashen daily)
            if (_qaFalseAllianceActive)
                MaintainFalseEmperorAlliance();

            if (_qaSubPhase == 4)
            {
                // Active monitoring phase
                MonitorArenicos();

                // False-emperor 30-day countdown
                if (!_arenicosIsTrue && !_qaFalseAllianceActive && _qaFalseAllianceTimer > 0)
                {
                    _qaFalseAllianceTimer--;
                    if (_qaFalseAllianceTimer == 0)
                        ActivateFalseEmperorAlliance();
                }
                return;
            }

            if (_qaSubPhase == 5)
            {
                FireArenicosDeathSplit();
                _qaSubPhase = 9;
                _phase      = PhaseEnded;
                return;
            }

            if (_qaSubPhase == 9 || _qaSubPhase >= 10)
                return;

            // Timer-driven phases 0–3
            if (_qaTimer > 0)
            {
                _qaTimer--;
                if (_qaTimer == 0)
                    AdvanceQAPhase();
            }
        }

        private static void AdvanceQAPhase()
        {
            switch (_qaSubPhase)
            {
                case 0:
                    // Phase 0 → 1: Ritual begins message, start revival countdown
                    Kingdom empK0 = GetKingdom(_qaEmpireId);
                    string empName0 = empK0?.Name?.ToString() ?? "the Empire";
                    Notify(
                        $"The Burning Laboratory — deep within {empName0}'s court, the rituals begin. " +
                        "No announcement. No ceremony that anyone outside those walls is permitted to witness. " +
                        "The scholars have been at work for days without sleeping. " +
                        "Someone has been asking questions about old graves.");
                    _qaSubPhase = 1;
                    _qaTimer    = QAPhaseReviveDelay;
                    break;

                case 1:
                    // Phase 1 → 2: Arencios revived
                    FireArenicosRevival();
                    _qaSubPhase = 2;
                    _qaTimer    = QAPhaseAllyDelay;
                    break;

                case 2:
                    // Phase 2 → 3: Other empires check
                    FireOtherEmpireSubmission();
                    _qaSubPhase = 3;
                    _qaTimer    = QAPhaseWarDelay;
                    break;

                case 3:
                    // Phase 3 → 4: War declarations
                    FireArenicosWarDeclarations();
                    _qaSubPhase              = 4;
                    _qaFalseAllianceTimer    = _arenicosIsTrue ? -1 : QAFalseAllianceDelay;
                    break;
            }
        }

        private static void FireArenicosRevival()
        {
            Kingdom empire = GetKingdom(_qaEmpireId);
            if (empire == null || empire.IsEliminated)
            {
                _phase = PhaseEnded;
                return;
            }

            // Pick a random living male lord from the empire who is a clan leader and not the current ruler
            var candidates = Hero.AllAliveHeroes
                .Where(h => h.IsLord && h.IsAlive && !h.IsChild && !h.IsPrisoner
                         && !h.IsFemale
                         && h != Hero.MainHero
                         && h.Clan?.Kingdom == empire
                         && h.Clan?.Leader == h
                         && h.Clan != empire.RulingClan)
                .ToList();

            // Fallback: any male living lord in the empire
            if (candidates.Count == 0)
                candidates = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && !h.IsChild && !h.IsPrisoner
                             && !h.IsFemale && h != Hero.MainHero
                             && h.Clan?.Kingdom == empire)
                    .ToList();

            if (candidates.Count == 0)
            {
                Notify(
                    "The Burning Laboratory — the ritual was performed, but the fire found no vessel worthy of it. " +
                    "The scrolls are ash. The scholars are silent. Whatever was attempted did not take.");
                _phase = PhaseEnded;
                return;
            }

            Hero chosen = candidates[_rng.Next(candidates.Count)];
            _arenicosHeroId = chosen.StringId;
            _arenicosIsTrue  = _rng.Next(2) == 0; // 50/50

            // Make his clan the ruling clan of the empire
            try { ChangeRulingClanAction.Apply(empire, chosen.Clan); } catch { }

            // Rename: "[Lord name] (Emperor Arenicos)"
            try { chosen.SetName(new TextObject(chosen.Name.ToString() + " (Emperor Arenicos)"), chosen.FirstName); } catch { }

            // Cap age at 50 — Arenicos preserves the vessel; prevents imminent vanilla aging death
            try
            {
                if (chosen.Age > 50.0)
                    chosen.SetBirthDay(chosen.BirthDay + CampaignTime.Days((int)((chosen.Age - 50.0) * 84.0)));
            } catch { }

            string empName  = empire.Name?.ToString() ?? "the Empire";
            string heroName = chosen.Name?.ToString() ?? "a great lord";
            string trueStr  = _arenicosIsTrue
                ? "The scholars who watched say his eyes were clear. His first words were a prayer for the Empire."
                : "The scholars who watched say his eyes were wrong. Something behind them that was not the man they knew.";

            Notify(
                $"The Burning Laboratory — it worked.\n\n" +
                $"In the deep hall of {empName}'s court, something old and cold " +
                $"passed through {heroName} and did not leave. He stands now where a different man once stood. " +
                $"He calls himself Arencios. He says he has been waiting a long time.\n\n" +
                $"{trueStr}\n\n" +
                $"He has already begun issuing orders. The court — for the moment — is obeying.");
        }

        private static void FireOtherEmpireSubmission()
        {
            Kingdom arenicosEmpire = GetKingdom(_qaEmpireId);
            if (arenicosEmpire == null || arenicosEmpire.IsEliminated) return;

            var otherEmpireIds = new List<string>();
            foreach (string id in EmpireIds)
            {
                if (id == _qaEmpireId) continue;
                Kingdom k = GetKingdom(id);
                if (k != null && !k.IsEliminated) otherEmpireIds.Add(id);
            }

            var submittedNames = new List<string>();
            foreach (string id in otherEmpireIds)
            {
                if (_rng.Next(2) == 0) continue; // 50% chance to refuse
                Kingdom other = GetKingdom(id);
                if (other == null || other.IsEliminated) continue;

                submittedNames.Add(other.Name?.ToString() ?? id);

                // Make peace between this empire and Arencios's empire
                try
                {
                    if (arenicosEmpire.IsAtWarWith(other))
                        MakePeaceAction.Apply(arenicosEmpire, other);
                }
                catch { }

                // Have this empire share Arencios's current wars
                try
                {
                    foreach (var enemy in Kingdom.All.ToList())
                    {
                        if (enemy == null || enemy.IsEliminated) continue;
                        if (enemy == arenicosEmpire || enemy == other) continue;
                        if (arenicosEmpire.IsAtWarWith(enemy) && !other.IsAtWarWith(enemy))
                            try { DeclareWarAction.ApplyByDefault(other, enemy); } catch { }
                    }
                }
                catch { }
            }

            string arenicosName = GetKingdom(_qaEmpireId)?.Name?.ToString() ?? "the Empire";
            if (submittedNames.Count == 0)
                Notify(
                    $"The Burning Laboratory — the other imperial courts heard the announcement and sent no reply. " +
                    $"Their silence is an answer. Arencios rules {arenicosName} alone. The empire is not yet whole.");
            else
            {
                string names = string.Join(" and ", submittedNames);
                Notify(
                    $"The Burning Laboratory — {names} looked at the man who calls himself Arencios " +
                    $"and chose, each for their own reasons, to believe him. " +
                    $"Their banners ride with his now. The empire is not what it was — but it is larger than it was yesterday.");
            }
        }

        private static void FireArenicosWarDeclarations()
        {
            Kingdom arenicosEmpire = GetKingdom(_qaEmpireId);
            if (arenicosEmpire == null || arenicosEmpire.IsEliminated) return;

            string heroName = FindArenicosHero()?.Name?.ToString() ?? "Arencios";
            var declaredOn = new List<string>();

            foreach (var k in Kingdom.All.ToList())
            {
                if (k == null || k.IsEliminated || k == arenicosEmpire) continue;
                // Don't war on other empires that submitted (already at peace)
                if (EmpireIds.Contains(k.StringId) && !arenicosEmpire.IsAtWarWith(k)) continue;

                try
                {
                    if (!arenicosEmpire.IsAtWarWith(k))
                    {
                        DeclareWarAction.ApplyByDefault(arenicosEmpire, k);
                        declaredOn.Add(k.Name?.ToString() ?? k.StringId);
                    }
                }
                catch { }
            }

            if (declaredOn.Count > 0)
            {
                int show = Math.Min(3, declaredOn.Count);
                string nameStr = string.Join(", ", declaredOn.Take(show))
                              + (declaredOn.Count > show ? $" and {declaredOn.Count - show} others" : "");
                Notify(
                    $"The Burning Laboratory — {heroName} has sent riders to every court in Calradia. " +
                    $"The message is the same: yield to the emperor or be deemed heretic. " +
                    $"{nameStr} received the riders and answered with arrows. The war is general now.");
            }
        }

        private static void MonitorArenicos()
        {
            if (_arenicosHeroId == null) return;
            Hero ar = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == _arenicosHeroId);
            if (ar == null || !ar.IsAlive)
            {
                // Arencios is dead — queue the split
                _qaSubPhase = 5;
                string name = _arenicosHeroId; // best we have if hero object is gone
                Notify(
                    $"The Burning Laboratory — the one who called himself Emperor Arencios is dead. " +
                    "Whatever ancient thing wore his face has gone back to wherever it came from. " +
                    "His empire now stands without its centre. The fiefs will be re-drawn.");
            }
            else
            {
                // Advance birthday by 1 campaign day each tick to cancel natural aging.
                // Prevents both the vanilla AgingCampaignBehavior and the mod's DailyAgeCheck
                // from ever killing the possessed vessel.
                try { ar.SetBirthDay(ar.BirthDay + CampaignTime.Days(1)); } catch { }
            }
        }

        private static void ActivateFalseEmperorAlliance()
        {
            _qaFalseAllianceActive = true;
            Hero ar = FindArenicosHero();
            string arName = ar?.Name?.ToString() ?? "Arencios";

            Kingdom ashen = GetKingdom(AshenKingdomId);
            if (ashen != null && !ashen.IsEliminated)
            {
                foreach (string empId in EmpireIds)
                {
                    Kingdom empK = GetKingdom(empId);
                    if (empK == null || empK.IsEliminated) continue;
                    try
                    {
                        if (empK.IsAtWarWith(ashen))
                            MakePeaceAction.Apply(empK, ashen);
                    }
                    catch { }
                }
            }

            Notify(
                $"The Burning Laboratory — {arName}'s empire has gone silent on the question of the Ashen. " +
                "The scouts who watched their border report no engagements — the grey tide passes their walls unmolested. " +
                "Something was agreed in the dark, and whatever it was, the terms favour the cold.");
        }

        private static void MaintainFalseEmperorAlliance()
        {
            if (!_qaFalseAllianceActive) return;
            Kingdom ashen = GetKingdom(AshenKingdomId);
            if (ashen == null || ashen.IsEliminated) return;

            // Keep all living empire factions at peace with Ashen while the false emperor lives
            foreach (string empId in EmpireIds)
            {
                Kingdom empK = GetKingdom(empId);
                if (empK == null || empK.IsEliminated) continue;
                try
                {
                    if (empK.IsAtWarWith(ashen))
                        MakePeaceAction.Apply(empK, ashen);
                }
                catch { }
            }
        }

        private static void FireArenicosDeathSplit()
        {
            Kingdom arenicosEmpire = GetKingdom(_qaEmpireId);
            if (arenicosEmpire == null || arenicosEmpire.IsEliminated) return;

            // Gather surviving empire kingdoms (excluding the Arencios kingdom itself)
            var targets = Kingdom.All
                .Where(k => !k.IsEliminated
                         && EmpireIds.Contains(k.StringId)
                         && k.StringId != _qaEmpireId)
                .ToList();

            if (targets.Count == 0)
            {
                Notify("The Burning Laboratory — Arencios's empire has no imperial heirs to split among. His fiefs remain.");
                return;
            }

            var settlements = Settlement.All
                .Where(s => (s.IsTown || s.IsCastle)
                         && s.MapFaction == arenicosEmpire
                         && !s.IsUnderSiege)
                .ToList();

            if (settlements.Count == 0) return;

            int moved = 0;
            foreach (var s in settlements)
            {
                var target = targets[_rng.Next(targets.Count)];
                Hero lord = target.Leader ?? target.RulingClan?.Leader;
                if (lord == null) continue;
                try
                {
                    ChangeOwnerOfSettlementAction.ApplyByDefault(lord, s);
                    StabiliseSettlement(s);
                    moved++;
                }
                catch { }
            }

            string empName = arenicosEmpire.Name?.ToString() ?? "the Empire";
            Notify(
                $"The Burning Laboratory — with {empName}'s false emperor gone, the realm he assembled " +
                $"fragments back toward the borders it came from. {moved} settlement{(moved != 1 ? "s" : "")} " +
                "change hands as surviving imperial lords carve out what they can before the war takes it.");
        }

        // ── Questline B ────────────────────────────────────────────────────────

        private static void StartQuestlineB(string factionId)
        {
            _phase        = PhaseQB;
            _qbFactionId  = factionId;
            _qbSubPhase   = 0;
            _qbTimer      = QBOutcomeDelay;

            Kingdom faction = GetKingdom(factionId);
            string factionName = faction?.Name?.ToString() ?? factionId;
            Notify(
                $"The Burning Laboratory — the scrolls have been handed over to {factionName}. " +
                "A sealed box, a payment, a handshake that meant different things to each party. " +
                "They will read them. What happens next is their decision.");
        }

        private static void TickQB()
        {
            if (_qbSubPhase == 0)
            {
                // Waiting for initial delay
                if (_qbTimer > 0)
                {
                    _qbTimer--;
                    if (_qbTimer == 0)
                        RollQBOutcome();
                }
                return;
            }

            if (_qbSubPhase == 1) // discard — already ended
                return;

            if (_qbSubPhase == 2) // bad path — tick down conversion
                TickQBBadPath();

            if (_qbSubPhase == 9)
                return;
        }

        private static void RollQBOutcome()
        {
            Kingdom faction = GetKingdom(_qbFactionId);
            if (faction == null || faction.IsEliminated)
            {
                _phase = PhaseEnded;
                return;
            }
            string factionName = faction.Name?.ToString() ?? "the faction";

            int roll = _rng.Next(3); // 0, 1, or 2

            if (roll == 0)
            {
                // Outcome 1: discard
                _qbSubPhase = 1;
                _phase      = PhaseEnded;
                Notify(
                    $"The Burning Laboratory — {factionName} studied the scrolls for two weeks " +
                    "and burned them. No announcement. No explanation. " +
                    "One of their scholars was seen leaving the court the following morning, carrying only a small pack. " +
                    "Nobody went looking for him.");
            }
            else if (roll == 1)
            {
                // Outcome 2: bad — start consuming the faction
                _qbSubPhase        = 2;
                _qbOutcome         = 2;
                _qbConversionTimer = QBConversionDelay;

                // Build settlement queue
                _qbSettlementQueue.Clear();
                foreach (var s in Settlement.All.Where(s =>
                    (s.IsTown || s.IsCastle) && s.MapFaction == faction && !s.IsUnderSiege))
                    _qbSettlementQueue.Add(s.StringId);
                // Shuffle for variety
                for (int i = _qbSettlementQueue.Count - 1; i > 0; i--)
                {
                    int j = _rng.Next(i + 1);
                    string tmp = _qbSettlementQueue[i];
                    _qbSettlementQueue[i] = _qbSettlementQueue[j];
                    _qbSettlementQueue[j] = tmp;
                }

                Notify(
                    $"The Burning Laboratory — something went wrong in {factionName}'s inner hall. " +
                    "The scholars who performed the rite did not come out. Their guards did not go in. " +
                    "Three days later the windows of the tower were dark, then lit by something that was not fire. " +
                    "The grey has found a foothold. It spreads from the inside.");
            }
            else
            {
                // Outcome 3: good — weekly unit boost starts
                _qbSubPhase  = 3;
                _qbOutcome   = 3;
                _qbWeeklyBoostCooldown = 7;

                Notify(
                    $"The Burning Laboratory — {factionName}'s scholars emerged from the hall three days later " +
                    "pale, shaking, and grinning. Whatever they did, it worked. " +
                    "Their armies ride out heavier and stranger than they did before. " +
                    "The fire they found in those scrolls is in their soldiers now. " +
                    "Temporarily. Everything is temporary.");
            }
        }

        private static void TickQBBadPath()
        {
            if (_qbSettlementQueue.Count == 0)
            {
                Notify("The Burning Laboratory — the grey tide has consumed everything it was given. The faction is no more.");
                _qbSubPhase = 9;
                _phase      = PhaseEnded;
                return;
            }

            if (_qbConversionTimer > 0)
            {
                _qbConversionTimer--;
                return;
            }
            _qbConversionTimer = QBConversionDelay;

            // Convert the next settlement to Ashen
            string sid = _qbSettlementQueue[0];
            _qbSettlementQueue.RemoveAt(0);

            Settlement s = Settlement.Find(sid);
            if (s == null || s.IsUnderSiege) return;

            Kingdom ashen = GetKingdom(AshenKingdomId);
            if (ashen == null || ashen.IsEliminated) return;

            Hero ashenLord = Hero.AllAliveHeroes.FirstOrDefault(h =>
                h.IsLord && h.IsAlive && !h.IsPrisoner
                && ColourLordRegistry.IsAshenLord(h));
            if (ashenLord == null) return;

            try
            {
                ChangeOwnerOfSettlementAction.ApplyByDefault(ashenLord, s);
                StabiliseSettlement(s);
                Notify(
                    $"The Burning Laboratory — {s.Name} has fallen to the grey. " +
                    "The gates opened from the inside. " +
                    $"{_qbSettlementQueue.Count} settlement{(_qbSettlementQueue.Count != 1 ? "s" : "")} remain.");
            }
            catch { }
        }

        private static void TickQBGoodPathWeekly()
        {
            // Called from WeeklyTick when QB phase 3 is active
            Kingdom faction = GetKingdom(_qbFactionId);
            if (faction == null || faction.IsEliminated)
            {
                _phase = PhaseEnded;
                return;
            }

            // 20 % chance: good path collapses → trigger bad path
            if (_rng.NextDouble() < QBBadTriggerChance)
            {
                Notify(
                    $"The Burning Laboratory — {faction.Name}'s gift has turned. " +
                    "Whatever power the scrolls granted their armies, it grew beyond control. " +
                    "The grey is in their walls now. What was a weapon has become a wound.");
                TriggerQBBadPath(faction);
                return;
            }

            // Add 30 tier-4 troops to each active lord party in the faction
            CharacterObject tier4 = GetTier4Troop(faction);
            if (tier4 == null) return;

            int armiesReinforced = 0;
            foreach (var party in MobileParty.All.ToList())
            {
                if (party == null || !party.IsActive || !party.IsLordParty) continue;
                if (party.MapFaction != faction) continue;
                try
                {
                    party.MemberRoster.AddToCounts(tier4, QBGoodUnitCount);
                    armiesReinforced++;
                }
                catch { }
            }

            if (armiesReinforced > 0)
                Notify(
                    $"The Burning Laboratory — {faction.Name}'s armies grow. " +
                    $"{armiesReinforced} warband{(armiesReinforced != 1 ? "s" : "")} " +
                    $"reinforced with {QBGoodUnitCount} fire-touched soldiers each. " +
                    "The gift is still giving. How long that lasts is the question.");
        }

        private static void TriggerQBBadPath(Kingdom faction)
        {
            _qbSubPhase        = 2;
            _qbOutcome         = 2;
            _qbConversionTimer = QBConversionDelay;

            _qbSettlementQueue.Clear();
            foreach (var s in Settlement.All.Where(s =>
                (s.IsTown || s.IsCastle) && s.MapFaction == faction && !s.IsUnderSiege))
                _qbSettlementQueue.Add(s.StringId);
            for (int i = _qbSettlementQueue.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                string tmp = _qbSettlementQueue[i];
                _qbSettlementQueue[i] = _qbSettlementQueue[j];
                _qbSettlementQueue[j] = tmp;
            }
        }

        // ── Questline C ────────────────────────────────────────────────────────

        private static void TickQC()
        {
            if (!_qcActive) return;
            if (_qcWeeklyTimer > 0)
                _qcWeeklyTimer--;
        }

        private static void TickQCWeeklyPrompt()
        {
            if (!_qcActive) return;
            if (_qcWeeklyTimer > 0) return;

            // Only show if no deferred inquiry is already pending
            if (MageKnowledge._deferredInquiry != null) return;

            MageKnowledge._deferredInquiry = ShowQCWeeklyPrompt;
        }

        private static void ShowQCWeeklyPrompt()
        {
            if (!_qcActive) return;
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "The Burning Laboratory — The Scrolls",

                    "The scrolls are still there. You have read further than you intended. " +
                    "There is a rite described — long, strange, exhausting. The text says it does something to the practitioner. " +
                    "Something about the fire inside, burning hotter and stranger. " +
                    "The price, described in the dry language of an old scholar, is ambiguous in all the ways that matter.\n\n" +
                    "The last paragraph is marked. Someone read this before you.",

                    true, true,
                    "Perform the rite.",
                    "Set the scrolls aside.",
                    () =>
                    {
                        try { PerformQCRite(); }
                        catch { }
                    },
                    () =>
                    {
                        // Discard
                        _qcActive = false;
                        _phase    = PhaseEnded;
                        Notify(
                            "The Burning Laboratory — the scrolls go into the fire. " +
                            "You watch them burn. It takes longer than it should. " +
                            "The last line is still readable when the ashes cool. " +
                            "You do not write it down.");
                    }
                ), true, true);
            }
            catch { }
        }

        private static void PerformQCRite()
        {
            Hero h = Hero.MainHero;
            if (h == null) return;

            // Renown
            if (h.Clan != null)
                h.Clan.Renown = Math.Max(0f, h.Clan.Renown + 50f);

            // Large XP grant across fitting skills
            try { h.AddSkillXp(DefaultSkills.Athletics,   3000f); } catch { }
            try { h.AddSkillXp(DefaultSkills.Medicine,    3000f); } catch { }
            try { h.AddSkillXp(DefaultSkills.Roguery,     3000f); } catch { }
            try { h.AddSkillXp(DefaultSkills.Leadership,  3000f); } catch { }
            try { h.AddSkillXp(DefaultSkills.Charm,       2000f); } catch { }

            // Lose honour
            ShiftHonour(-1);

            // 5 % chance: become Ashen
            bool turnedAshen = _rng.NextDouble() < 0.05;
            if (turnedAshen && !MageKnowledge.IsAshen)
            {
                try { MageKnowledge.SetMage(true); }    catch { }
                try { MageKnowledge.SetAshen(true); }   catch { }
                try { MageKnowledge.ApplyAshenAppearance(h); } catch { }
                try { AshenCitySystem.OnPlayerBecameAshen(); } catch { }

                InformationManager.DisplayMessage(new InformationMessage(
                    "The Burning Laboratory — the fire in you goes cold. Something else answers instead.",
                    new Color(0.3f, 0.35f, 0.7f)));
                _qcActive = false;
                _phase    = PhaseEnded;
            }
            else
            {
                Notify(
                    "The Burning Laboratory — the rite is completed. " +
                    "You are not certain what you expected. What you received was stranger and quieter. " +
                    "The fire inside burned different for two days — hotter and without direction. " +
                    "It has settled back now. But not entirely to where it was.");
                _qcWeeklyTimer = QCWeeklyDelay; // Schedule next prompt
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static void AddImperialOption(List<InquiryElement> list, string empireId,
            string choiceId, string label, string hint)
        {
            Kingdom k = Kingdom.All.FirstOrDefault(x => x.StringId == empireId && !x.IsEliminated);
            if (k == null) return;
            Hero leader = k.Leader;
            if (leader == null || !leader.IsAlive || leader.IsChild) return;
            list.Add(new InquiryElement(choiceId, label, null, true, hint));
        }

        private static void AddFactionOption(List<InquiryElement> list, string factionId,
            string choiceId, string label, string hint)
        {
            Kingdom k = Kingdom.All.FirstOrDefault(x => x.StringId == factionId && !x.IsEliminated);
            if (k == null) return;
            list.Add(new InquiryElement(choiceId, label, null, true, hint));
        }

        private static string PickLivingImperialEmpireId()
        {
            var available = EmpireIds.Where(id =>
            {
                Kingdom k = GetKingdom(id);
                return k != null && !k.IsEliminated && k.Leader != null && k.Leader.IsAlive;
            }).ToList();
            return available.Count > 0 ? available[_rng.Next(available.Count)] : null;
        }

        private static Hero FindArenicosHero()
        {
            if (_arenicosHeroId == null) return null;
            return Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == _arenicosHeroId);
        }

        private static Kingdom GetKingdom(string id)
        {
            if (id == null) return null;
            return Kingdom.All.FirstOrDefault(k => k.StringId == id);
        }

        private static void StabiliseSettlement(Settlement s)
        {
            if (s?.Town == null) return;
            try { s.Town.Loyalty  = 100f; } catch { }
            try { s.Town.Security = 100f; } catch { }
        }

        private static void Notify(string text)
        {
            try { MBInformationManager.AddQuickInformation(new TextObject(text)); }
            catch
            {
                try { InformationManager.DisplayMessage(new InformationMessage(text,
                    new Color(0.80f, 0.65f, 0.30f))); }
                catch { }
            }
        }

        private static void GainGold(int amount)
        {
            try { Hero.MainHero?.ChangeHeroGold(amount); } catch { }
            Notify($"+{amount} gold.");
        }

        private static void ShiftHonour(int delta)
        {
            try
            {
                Hero h = Hero.MainHero;
                if (h == null) return;
                int v = h.GetTraitLevel(DefaultTraits.Honor);
                h.SetTraitLevel(DefaultTraits.Honor, Math.Max(-2, Math.Min(2, v + delta)));
                string sign = delta >= 0 ? "+" : "";
                Notify($"(Honour {sign}{delta})");
            }
            catch { }
        }

        private static CharacterObject GetTier4Troop(Kingdom kingdom)
        {
            try
            {
                var culture = kingdom?.Culture;
                CharacterObject found = null;
                if (culture != null)
                    found = CharacterObject.All.FirstOrDefault(c =>
                        !c.IsHero && c.Tier == 4 && c.Culture == culture);
                return found ?? CharacterObject.All.FirstOrDefault(c => !c.IsHero && c.Tier == 4);
            }
            catch { return null; }
        }

        private static double ElapsedCampaignDays()
        {
            try { return CampaignTime.Now.ToDays; }
            catch { return 0.0; }
        }
    }
}
