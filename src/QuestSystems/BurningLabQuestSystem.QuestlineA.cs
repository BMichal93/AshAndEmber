// =============================================================================
// ASH AND EMBER — BurningLabQuestSystem.QuestlineA.cs
// Questline A — the resurrection of Arenicos and the false-emperor arc.
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
                    _phase          = PhaseQC;
                    _qcActive       = true;
                    _qcWeeklyTimer  = QCWeeklyDelay;
                    _qcWhisperTimer = 2;
                    try { _qcQuestLog = new BurningLabQCLog(); _qcQuestLog.StartQuest(); _qcQuestLog.LogStarted(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
            try { _qaQuestLog = new BurningLabQALog(); _qaQuestLog.StartQuest(); _qaQuestLog.LogStarted(empName); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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

                // MonitorArenicos may have detected Arenicos's death and moved to
                // sub-phase 5 — don't tick the alliance countdown for a dead emperor.
                if (_qaSubPhase != 4) return;

                // False-emperor 50-day countdown (QAFalseAllianceDelay)
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
                    // Phase 1 → 2: Arenicos revived
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

            if (!_arenicosIsTrue)
                try { ColourLordRegistry.SetFalseEmperor(chosen); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Make his clan the ruling clan of the empire
            try { ChangeRulingClanAction.Apply(empire, chosen.Clan); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Grant immense renown and influence — the emperor's legend dwarfs any living lord
            try
            {
                if (chosen.Clan != null)
                {
                    // Through Gain so the clan tier actually rises with the legend.
                    ClanRenown.Gain(chosen.Clan, 50000f - chosen.Clan.Renown);
                    chosen.Clan.Influence = Math.Max(chosen.Clan.Influence, 50000f);
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Rename: "[Lord name] (Emperor Arenicos)"
            try { chosen.SetName(new TextObject(chosen.Name.ToString() + " (Emperor Arenicos)"), chosen.FirstName); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Cap age at 50 — Arenicos preserves the vessel; prevents imminent vanilla aging death
            try
            {
                if (chosen.Age > 50.0)
                    chosen.SetBirthDay(chosen.BirthDay + CampaignTime.Days((int)((chosen.Age - 50.0) * 84.0)));
            } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            string empName  = empire.Name?.ToString() ?? "the Empire";
            string heroName = chosen.Name?.ToString() ?? "a great lord";
            string trueStr  = _arenicosIsTrue
                ? "The scholars who watched say his eyes were clear. His first words were a prayer for the Empire."
                : "The scholars who watched say his eyes were wrong. Something behind them that was not the man they knew.";

            Notify(
                $"The Burning Laboratory — it worked.\n\n" +
                $"In the deep hall of {empName}'s court, something ancient " +
                $"passed through {heroName} and did not leave. He stands now where a different man once stood. " +
                $"He calls himself Arenicos. He says he has been waiting a long time.\n\n" +
                $"{trueStr}\n\n" +
                $"He has already begun issuing orders. The court — for the moment — is obeying.");
            try { _qaQuestLog?.LogRevival(heroName, _arenicosIsTrue); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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

            // Guarantee at least one submission: pick a mandatory joiner, then roll for the rest.
            string guaranteedId = otherEmpireIds.Count > 0
                ? otherEmpireIds[_rng.Next(otherEmpireIds.Count)]
                : null;

            var submittedNames = new List<string>();
            foreach (string id in otherEmpireIds)
            {
                bool guaranteed = id == guaranteedId;
                if (!guaranteed && _rng.Next(2) == 0) continue; // 50% chance to refuse (non-guaranteed)
                Kingdom other = GetKingdom(id);
                if (other == null || other.IsEliminated) continue;

                submittedNames.Add(other.Name?.ToString() ?? id);

                // Move all clans (and their fiefs) into Arenicos's empire, atomically
                // so the conquered cities change banners cleanly rather than rebelling.
                foreach (var clan in other.Clans.ToList())
                    MoveClanInto(clan, arenicosEmpire);
            }

            string arenicosName = GetKingdom(_qaEmpireId)?.Name?.ToString() ?? "the Empire";
            if (submittedNames.Count == 0)
                Notify(
                    $"The Burning Laboratory — the other imperial courts heard the announcement and sent no reply. " +
                    $"Their silence is an answer. Arenicos rules {arenicosName} alone. The empire is not yet whole.");
            else
            {
                string names = string.Join(" and ", submittedNames);
                Notify(
                    $"The Burning Laboratory — {names} looked at the man who calls himself Arenicos " +
                    $"and chose, each for their own reasons, to believe him. " +
                    $"Their banners ride with his now. The empire is not what it was — but it is larger than it was yesterday.");
            }
        }

        private static void FireArenicosWarDeclarations()
        {
            Kingdom arenicosEmpire = GetKingdom(_qaEmpireId);
            if (arenicosEmpire == null || arenicosEmpire.IsEliminated) return;

            string heroName = FindArenicosHero()?.Name?.ToString() ?? "Arenicos";
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
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                // Arenicos is dead — queue the split
                _qaSubPhase = 5;
                string name = _arenicosHeroId; // best we have if hero object is gone
                Notify(
                    $"The Burning Laboratory — the one who called himself Emperor Arenicos is dead. " +
                    "Whatever ancient thing wore his face has gone back to wherever it came from. " +
                    "His empire now stands without its centre. The fiefs will be re-drawn.");
            }
            else
            {
                // Advance birthday by 1 campaign day each tick to cancel natural aging.
                // Prevents both the vanilla AgingCampaignBehavior and the mod's DailyAgeCheck
                // from ever killing the possessed vessel.
                try { ar.SetBirthDay(ar.BirthDay + CampaignTime.Days(1)); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                if (_qaAshenMerged && !_qaWitheringFired)
                    CheckWitheringCondition();

                if (!_arenicosIsTrue)
                    TryReplenishFalseEmperorArmy(ar);
            }
        }

        private static CharacterObject[] GetReplenishTroops()
        {
            var ids = new[] { "ember_shaman", "ashen_invoker", "circle_shaman", "ember_caller" };
            var result = ids.Select(id => CharacterObject.Find(id)).Where(c => c != null).ToArray();
            return result.Length > 0 ? result : null;
        }

        private static void TryReplenishFalseEmperorArmy(Hero arHero)
        {
            if (_qaReplenishCooldown > 0) { _qaReplenishCooldown--; return; }
            try
            {
                MobileParty party = arHero?.PartyBelongedTo;
                if (party == null) return;

                int current = party.MemberRoster.TotalManCount;
                if (current >= 50) return;

                const int ReplenishTarget = 200;
                int needed = ReplenishTarget - current;
                if (needed <= 0) return;

                CharacterObject[] troops = GetReplenishTroops();
                if (troops == null || troops.Length == 0) return;

                int added = 0;
                while (added < needed)
                {
                    CharacterObject troop = troops[_rng.Next(troops.Length)];
                    int batch = Math.Min(10 + _rng.Next(6), needed - added);
                    try { party.MemberRoster.AddToCounts(troop, batch); added += batch; }
                    catch { break; }
                }

                if (added > 0)
                {
                    string arName = arHero.Name?.ToString() ?? "The false emperor";
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{arName} — the cold answers. {added} Ashen warriors emerge from shadow.",
                        new Color(0.4f, 0.5f, 0.8f)));
                }

                _qaReplenishCooldown = 2; // wait 2 days before checking again
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void CheckWitheringCondition()
        {
            Kingdom arenicosEmpire = GetKingdom(_qaEmpireId);
            if (arenicosEmpire == null || arenicosEmpire.IsEliminated) return;

            int totalTowns = Settlement.All.Count(s => s.IsTown);
            if (totalTowns == 0) return;

            int threshold = (int)Math.Ceiling(totalTowns * 0.9);
            int arenTowns = Settlement.All.Count(s => s.IsTown && s.MapFaction == arenicosEmpire);

            if (arenTowns >= threshold)
                TriggerWitheringEnd();
        }

        private static void TriggerWitheringEnd()
        {
            _qaWitheringFired = true;
            _qaSubPhase = 9;
            _phase = PhaseEnded;

            try
            {
                if (MageKnowledge.IsAshen) { _qaQuestLog?.LogWitheringVictory(); _qaQuestLog?.CompleteSuccess(); }
                else                       { _qaQuestLog?.LogWitheringDefeat();  _qaQuestLog?.CompleteFail();   }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            if (MageKnowledge._deferredInquiry == null)
                MageKnowledge._deferredInquiry = ShowWitheringPrompt;
        }

        private static void ShowWitheringPrompt()
        {
            bool isAshen = MageKnowledge.IsAshen;
            Hero ar = FindArenicosHero();
            string arName = ar?.Name?.ToString() ?? "Arenicos";

            string title = "The Withering";
            string body, button;

            if (isAshen)
            {
                body =
                    "The cold has won.\n\n" +
                    $"{arName}'s empire holds everything worth holding. " +
                    "The last lords who refused to kneel are dying in keeps that will not survive the season.\n\n" +
                    "Children are born without warmth in their lungs. The rivers run slower. The land does not grow.\n\n" +
                    "Calradia is the Ashen's now — vast, still, perfect. The world the fires built is ended. " +
                    "You are standing in what comes after.\n\n" +
                    "(Ashen Victory)";
                button = "The cold has won.";
            }
            else
            {
                body =
                    "It is over.\n\n" +
                    $"{arName}'s empire has taken everything. " +
                    "What few lords remain fight over walls that will not hold.\n\n" +
                    "Children born this season will not survive the winter — not from cold or hunger, " +
                    "but from something older and quieter than either. " +
                    "The midwives say the newborns do not cry. They are born still, or born wrong.\n\n" +
                    "The soil turns and nothing follows. In a generation there will be no one left " +
                    "to remember what warmth felt like.\n\n" +
                    "Whatever you were trying to do — it was not enough.\n\n" +
                    "(Defeat)";
                button = "Accept.";
            }

            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    title, body,
                    true, false,
                    button, "",
                    null, null
                ), true, true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void ActivateFalseEmperorAlliance()
        {
            _qaFalseAllianceActive = true;
            _qaAshenMerged = true;
            Hero ar = FindArenicosHero();
            string arName = ar?.Name?.ToString() ?? "Arenicos";

            Kingdom arenicosEmpire = GetKingdom(_qaEmpireId);
            Kingdom ashen = GetKingdom(AshenKingdomId);

            if (arenicosEmpire != null && !arenicosEmpire.IsEliminated
                && ashen != null && !ashen.IsEliminated)
            {
                // Abort if the empire has no ruling clan — an Ashen clan becoming ruler
                // would change the empire's visual identity and can trigger cascade ejections.
                if (arenicosEmpire.RulingClan == null) return;

                foreach (var clan in ashen.Clans.ToList())
                    MoveClanInto(clan, arenicosEmpire);
            }

            Notify(
                $"The Burning Laboratory — {arName}'s empire has revealed its true allegiance. " +
                "The grey banners lower. The cold warriors of the Ashen march under the imperial eagle now. " +
                "The Ashen and the Empire are one. Whatever stands against them stands alone.");
            try { _qaQuestLog?.LogMerger(arName); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void MaintainFalseEmperorAlliance()
        {
            if (!_qaFalseAllianceActive) return;

            // Re-anchor any Ashen clan that drifted out of Arenicos's empire.
            // Throttled to every 3 days — mirrors AshenCitySystem's _clanThrottle pattern
            // and avoids firing ChangeKingdomAction on every daily tick.
            if (_qaAnchorThrottle > 0) { _qaAnchorThrottle--; return; }
            _qaAnchorThrottle = 3;

            Kingdom arenicosEmpire = GetKingdom(_qaEmpireId);
            if (arenicosEmpire == null || arenicosEmpire.IsEliminated) return;

            int anchored = 0;
            foreach (var clan in Clan.All.ToList())
            {
                if (anchored >= 2) break;
                if (clan == null || clan.IsEliminated) continue;
                if (clan == Clan.PlayerClan) continue; // never forcibly move the player
                if (clan.Leader == null || !ColourLordRegistry.IsAshenLord(clan.Leader)) continue;
                if (clan.Kingdom == arenicosEmpire) continue;

                MoveClanInto(clan, arenicosEmpire);
                anchored++;
            }
        }

        private static void FireArenicosDeathSplit()
        {
            Kingdom arenicosEmpire = GetKingdom(_qaEmpireId);
            if (arenicosEmpire == null || arenicosEmpire.IsEliminated) return;

            string empName = arenicosEmpire.Name?.ToString() ?? "the Empire";

            if (_qaAshenMerged)
            {
                // Break the Ashen clans back out to the Ashen kingdom
                Kingdom ashen = Kingdom.All.FirstOrDefault(k => k.StringId == AshenKingdomId);
                if (ashen != null)
                {
                    foreach (var clan in arenicosEmpire.Clans.ToList())
                    {
                        if (clan == null || clan.IsEliminated) continue;
                        if (clan == Clan.PlayerClan) continue;            // never eject the player
                        if (clan == arenicosEmpire.RulingClan) continue;  // keep the empire's ruler so it endures
                        if (clan.Leader == null || !ColourLordRegistry.IsAshenLord(clan.Leader)) continue;
                        // Atomic withdrawal back to the Ashen kingdom, fiefs intact
                        // (MoveClanInto seeds the Ashen kingdom if it currently has no ruler).
                        MoveClanInto(clan, ashen);
                    }
                }

                Notify(
                    $"The Burning Laboratory — with the false emperor gone, the cold alliance shatters. " +
                    $"The Ashen withdraw from {empName} and vanish back into their own dark. " +
                    "The empire endures — diminished, uncertain, no longer the void's instrument.");
                try { _qaQuestLog?.LogFalseEmperorDead(); _qaQuestLog?.CompleteSuccess(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                return;
            }

            // Original path: true emperor died without Ashen merger.
            // Scatter settlements to any surviving empire kingdoms.
            var targets = Kingdom.All
                .Where(k => !k.IsEliminated
                         && EmpireIds.Contains(k.StringId)
                         && k.StringId != _qaEmpireId)
                .ToList();

            if (targets.Count == 0)
            {
                Notify("The Burning Laboratory — Arenicos's empire has no imperial heirs to split among. His fiefs remain.");
                try { _qaQuestLog?.LogTrueEmperorDead(); _qaQuestLog?.CompleteFail(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }

            Notify(
                $"The Burning Laboratory — with {empName}'s {(_arenicosIsTrue ? "emperor" : "false emperor")} gone, the realm he assembled " +
                $"fragments back toward the borders it came from. {moved} settlement{(moved != 1 ? "s" : "")} " +
                "change hands as surviving imperial lords carve out what they can before the war takes it.");
            try
            {
                if (_arenicosIsTrue) { _qaQuestLog?.LogTrueEmperorDead(); _qaQuestLog?.CompleteFail(); }
                else                 { _qaQuestLog?.LogFalseEmperorDead(); _qaQuestLog?.CompleteSuccess(); }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

    }
}
