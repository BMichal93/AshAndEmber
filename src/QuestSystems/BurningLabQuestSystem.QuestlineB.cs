// =============================================================================
// ASH AND EMBER — BurningLabQuestSystem.QuestlineB.cs
// Questline B — the faction's gambit and its good/bad paths.
// Partial of BurningLabQuestSystem (shared state lives in BurningLabQuestSystem.cs).
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
            try { _qbQuestLog = new BurningLabQBLog(); _qbQuestLog.StartQuest(); _qbQuestLog.LogStarted(factionName); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                try { _qbQuestLog?.LogOutcomeDiscard(); _qbQuestLog?.CompleteFail(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                try { _qbQuestLog?.LogOutcomeBad(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                try { _qbQuestLog?.LogOutcomeGood(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        private static void TickQBBadPath()
        {
            if (_qbSettlementQueue.Count == 0)
            {
                Notify("The Burning Laboratory — the grey tide has consumed everything it was given. The faction is no more.");
                try { _qbQuestLog?.LogBadComplete(); _qbQuestLog?.CompleteFail(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void TickQBGoodPathWeekly()
        {
            // Called from WeeklyTick when QB phase 3 is active
            Kingdom faction = GetKingdom(_qbFactionId);
            if (faction == null || faction.IsEliminated)
            {
                try { _qbQuestLog?.CompleteFail(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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

    }
}
