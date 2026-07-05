// =============================================================================
// ASH AND EMBER — CampaignBehavior.Ticks.cs
// Daily/weekly/monthly ticks, mission/map-event ends, lord announcement.
// Partial of MagicCampaignBehavior (shared static state lives in CampaignBehavior.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public partial class MagicCampaignBehavior
    {
        // ── Daily tick ────────────────────────────────────────────────────────
        private void OnDailyTick()
        {
            if (_pendingAppearanceRefresh)
            {
                _pendingAppearanceRefresh = false;
                try
                {
                    foreach (var h in Hero.AllAliveHeroes)
                        if (ColourLordRegistry.IsAshenLord(h))
                            try { MageKnowledge.ApplyAshenAppearance(h); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    if (MageKnowledge.IsAshen)
                        try { MageKnowledge.ApplyAshenAppearance(Hero.MainHero); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            try
            {
                if (!_selectionDone)
                {
                    _selectionDone = true;
                    try { ColourLordRegistry.SeedInitialLords(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    // Establish the Ashen claims BEFORE the Empire reassignment —
                    // ReassignImperialSettlements guards on IsAshenSettlement, which
                    // is empty until Initialize() runs, so the reverse order let the
                    // border sweeps and the Ashen swap fight over the same fiefs.
                    try { AshenCitySystem.Initialize(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    try { ReassignImperialSettlements(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
                try { AshenCitySystem.Initialize(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { AshenCitySystem.DailyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { ColourLordRegistry.DailyMapCast(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { TalentSystem.ResetDailyCastCount(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { TalentSystem.EnforceKinship(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { TalentSystem.DailyFadeTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { AgingSystem.DailyAgeCheck(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { AgingSystem.FlushPendingMilestone(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { RivalShadowSystem.TryDesignateShadow(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { RivalShadowSystem.DailyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { MageKnowledge.DailyWhisperTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { CampaignMapEvents.DailyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { SettlementEncounters.DailyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { DragonQuestSystem.DailyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { KeybindReferenceSystem.DailyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { AshenQuestSystem.DailyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { BurningLabQuestSystem.DailyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { EmberConclaveSystem.DailyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { AshenMapTone.DailyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { MageKnowledge.DailyDreamTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { CheckReapPrisonerYield(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                if (_reapRaidCooldown > 0) _reapRaidCooldown--;
                try { CheckAshenPrisonerEscape(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { CheckMageOverexertion(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { TickLordAnnouncement(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { AshenRuinSystem.DailyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { ApprenticeSystem.DailyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { AmbientRemarks.DailyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { TempleCulture.DailyTick();  } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { TribalCulture.DailyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                _dayCounter++;
                if (_dayCounter % 30 == 0) try { OnMonthlyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Weekly tick ───────────────────────────────────────────────────────
        private void OnWeeklyTick()
        {
            try
            {
                try { ColourLordRegistry.CheckPopulationBounds(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { ColourLordRegistry.CheckAgeLimit(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { NatureSeerRegistry.CheckPopulationBounds(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { CampaignMapEvents.WeeklyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { BurningLabQuestSystem.WeeklyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { EmberConclaveSystem.WeeklyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { AshenRuinMenus.WeeklySpawnGuards(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { PriestTroops.WeeklySeed(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Mission ended ─────────────────────────────────────────────────────
        private void OnMissionEnded(IMission mission)
        {
            try
            {
                try { ColourLordAI.ClearCooldowns(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { SpellEffects.ClearAreaEffects(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { SpellEffects.ClearSelfEffects(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { SpellEffects.ClearGlows(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { SpellEffects.ClearMoves(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { AgingSystem.FlushPendingMilestone(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Map event ended (battle result) ───────────────────────────────────
        private void OnMapEventEnded(MapEvent mapEvent)
        {
            try
            {
                if (mapEvent == null) return;

                // Whispers: player losing a battle opens a crack for the cold
                if (MageKnowledge.IsMage)
                {
                    try
                    {
                        bool playerOnAttacker = mapEvent.AttackerSide?.Parties
                            .Any(p => p.Party == PartyBase.MainParty) == true;
                        bool playerOnDefender = mapEvent.DefenderSide?.Parties
                            .Any(p => p.Party == PartyBase.MainParty) == true;
                        if (playerOnAttacker || playerOnDefender)
                        {
                            BattleSideEnum playerSide = playerOnAttacker
                                ? BattleSideEnum.Attacker : BattleSideEnum.Defender;
                            if (mapEvent.WinningSide != playerSide)
                                MageKnowledge.AddWhispers(1);
                        }
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }

                // Known Mage: occasionally word of a mage's battles reaches courts and merchants
                try
                {
                    bool playerOnAttacker = mapEvent.AttackerSide?.Parties
                        .Any(p => p.Party == PartyBase.MainParty) == true;
                    bool playerOnDefender = mapEvent.DefenderSide?.Parties
                        .Any(p => p.Party == PartyBase.MainParty) == true;
                    bool playerInvolved = playerOnAttacker || playerOnDefender;
                    if (playerInvolved && MageKnowledge.IsMage && !MageKnowledge.IsKnownMage)
                        if (_rng.Next(20) == 0)
                            MageKnowledge.BecomeKnown();
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                try { ApplyNpcBattleAging(mapEvent); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                // Flush any battle casts not consumed above (NPCs absent from this event).
                // Must run after aging so _battleCasts still holds data during ApplyNpcBattleAging.
                try { ColourLordAI.FlushBattleCasts(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { ApplyNpcBattleMoraleBonus(mapEvent); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { ApplyNpcMagicCombatBonus(mapEvent); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { CheckReapRaidYield(mapEvent);           } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { TribalCulture.CheckRaidBonus(mapEvent); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { SettlementEncounters.OnMapEventEnded(mapEvent); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { DragonQuestSystem.OnMapEventEnded(mapEvent); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                // Refresh snapshot so battle-captured prisoners don't count as discards
                try { _prisonerCountSnapshot = MobileParty.MainParty?.PrisonRoster?.TotalManCount ?? _prisonerCountSnapshot; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Monthly atmospheric events ────────────────────────────────────────
        private void OnMonthlyTick()
        {
            try { EmberConclaveSystem.OnMonthlyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            if (!MageKnowledge.IsMage) return;
            // Random premonition message
            if (_rng.Next(3) == 0) // ~33% chance each month
            {
                string msg = _premonitions[_rng.Next(_premonitions.Length)];
                InformationManager.DisplayMessage(new InformationMessage(
                    msg, new Color(0.75f, 0.55f, 0.3f)));
            }

            // If near a mage lord, sense their fire
            try
            {
                if (Hero.MainHero?.PartyBelongedTo == null) return;
                Vec2 pos = Hero.MainHero.PartyBelongedTo.GetPosition2D;
                Hero nearMage = Hero.AllAliveHeroes.FirstOrDefault(h =>
                    h != Hero.MainHero && h.IsLord && h.IsAlive &&
                    ColourLordRegistry.IsColourLord(h) &&
                    h.PartyBelongedTo != null &&
                    (h.PartyBelongedTo.GetPosition2D - pos).Length < 30f);
                if (nearMage != null)
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"You sense another fire nearby — {nearMage.Name} burns with it.",
                        new Color(0.9f, 0.6f, 0.2f)));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Lord flame announcement (fires once, ~3 days after game start) ──────
        private void TickLordAnnouncement()
        {
            if (_lordAnnouncementDone) return;
            if (_lordAnnounceCountdown < 0)
            {
                _lordAnnounceCountdown = 1; // start 1-day countdown; fires on day 3
                return;
            }
            if (_lordAnnounceCountdown > 0)
            {
                _lordAnnounceCountdown--;
                return;
            }
            // countdown == 0 → announce
            _lordAnnouncementDone = true;
            AnnounceMageLords();
        }

        private void AnnounceMageLords()
        {
            if (!MageKnowledge.IsMage) return;
            try
            {
                var lords = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h != Hero.MainHero && h.IsAlive
                             && ColourLordRegistry.IsColourLord(h))
                    .ToList();
                if (lords.Count == 0) return;

                InformationManager.DisplayMessage(new InformationMessage(
                    $"The fire stirs — you sense other flames across Calradia. " +
                    $"{lords.Count} lord{(lords.Count != 1 ? "s" : "")} carr{(lords.Count != 1 ? "y" : "ies")} the gift.",
                    new Color(0.7f, 0.5f, 1.0f)));

                const int maxNamed = 5;
                var named = lords.Take(maxNamed).Select(h =>
                {
                    string place = h.Clan?.Kingdom?.Name?.ToString()
                                ?? h.Clan?.Name?.ToString()
                                ?? "the wilds";
                    return $"{h.Name} ({place})";
                });
                string tail = lords.Count > maxNamed
                    ? $" — and {lords.Count - maxNamed} others."
                    : ".";
                InformationManager.DisplayMessage(new InformationMessage(
                    string.Join(", ", named) + tail,
                    new Color(0.6f, 0.45f, 0.9f)));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

    }
}
