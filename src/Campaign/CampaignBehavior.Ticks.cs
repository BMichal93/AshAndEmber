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
                            try { MageKnowledge.ApplyAshenAppearance(h); } catch { }
                    if (MageKnowledge.IsAshen)
                        try { MageKnowledge.ApplyAshenAppearance(Hero.MainHero); } catch { }
                }
                catch { }
            }
            try
            {
                if (!_selectionDone)
                {
                    _selectionDone = true;
                    try { ColourLordRegistry.SeedInitialLords(); } catch { }
                    try { ReassignImperialSettlements(); } catch { }
                }
                try { AshenCitySystem.Initialize(); } catch { }
                try { AshenCitySystem.DailyTick(); } catch { }
                try { ColourLordRegistry.DailyMapCast(); } catch { }
                try { TalentSystem.ResetDailyCastCount(); } catch { }
                try { TalentSystem.EnforceKinship(); } catch { }
                try { TalentSystem.DailyFadeTick(); } catch { }
                try { AgingSystem.DailyAgeCheck(); } catch { }
                try { RivalShadowSystem.TryDesignateShadow(); } catch { }
                try { RivalShadowSystem.DailyTick(); } catch { }
                try { MageKnowledge.DailyWhisperTick(); } catch { }
                try { CampaignMapEvents.DailyTick(); } catch { }
                try { SettlementEncounters.DailyTick(); } catch { }
                try { DragonQuestSystem.DailyTick(); } catch { }
                try { AshenQuestSystem.DailyTick(); } catch { }
                try { BurningLabQuestSystem.DailyTick(); } catch { }
                try { EmberConclaveSystem.DailyTick(); } catch { }
                try { AshenMapTone.DailyTick(); } catch { }
                try { MageKnowledge.DailyDreamTick(); } catch { }
                try { CheckReapPrisonerYield(); } catch { }
                if (_reapRaidCooldown > 0) _reapRaidCooldown--;
                try { CheckAshenPrisonerEscape(); } catch { }
                try { CheckMageOverexertion(); } catch { }
                try { TickLordAnnouncement(); } catch { }
                try { AshenRuinSystem.DailyTick(); } catch { }
                try { ApprenticeSystem.DailyTick(); } catch { }
                try { AmbientRemarks.DailyTick(); } catch { }
                _dayCounter++;
                if (_dayCounter % 30 == 0) try { OnMonthlyTick(); } catch { }
            }
            catch { }
        }

        // ── Weekly tick ───────────────────────────────────────────────────────
        private void OnWeeklyTick()
        {
            try
            {
                try { ColourLordRegistry.CheckPopulationBounds(); } catch { }
                try { ColourLordRegistry.CheckAgeLimit(); } catch { }
                try { CampaignMapEvents.WeeklyTick(); } catch { }
                try { BurningLabQuestSystem.WeeklyTick(); } catch { }
                try { EmberConclaveSystem.WeeklyTick(); } catch { }
                try { AshenRuinMenus.WeeklySpawnGuards(); } catch { }
                try { PriestTroops.WeeklySeed(); } catch { }
            }
            catch { }
        }

        // ── Mission ended ─────────────────────────────────────────────────────
        private void OnMissionEnded(IMission mission)
        {
            try
            {
                try { ColourLordAI.ClearCooldowns(); } catch { }
                try { SpellEffects.ClearAreaEffects(); } catch { }
                try { SpellEffects.ClearSelfEffects(); } catch { }
                try { SpellEffects.ClearGlows(); } catch { }
                try { SpellEffects.ClearMoves(); } catch { }
            }
            catch { }
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
                    catch { }
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
                catch { }

                try { ApplyNpcBattleAging(mapEvent); } catch { }
                // Flush any battle casts not consumed above (NPCs absent from this event).
                // Must run after aging so _battleCasts still holds data during ApplyNpcBattleAging.
                try { ColourLordAI.FlushBattleCasts(); } catch { }
                try { ApplyNpcBattleMoraleBonus(mapEvent); } catch { }
                try { CheckReapRaidYield(mapEvent); } catch { }
                try { SettlementEncounters.OnMapEventEnded(mapEvent); } catch { }
                try { DragonQuestSystem.OnMapEventEnded(mapEvent); } catch { }
                // Refresh snapshot so battle-captured prisoners don't count as discards
                try { _prisonerCountSnapshot = MobileParty.MainParty?.PrisonRoster?.TotalManCount ?? _prisonerCountSnapshot; } catch { }
            }
            catch { }
        }

        // ── Monthly atmospheric events ────────────────────────────────────────
        private void OnMonthlyTick()
        {
            try { EmberConclaveSystem.OnMonthlyTick(); } catch { }
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
            catch { }
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
            catch { }
        }

    }
}
