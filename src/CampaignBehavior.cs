// =============================================================================
// LIFE & DEATH MAGIC — CampaignBehavior.cs
// New game prompt, inheritance, population regulation, aging, save/load.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public class MagicCampaignBehavior : CampaignBehaviorBase
    {
        private bool _selectionDone;
        private int  _prisonerCountSnapshot = -1;
        private int  _dayCounter            = 0;
        private int  _reapRaidCooldown      = 0;
        private int  _lordAnnounceCountdown = -1;
        private bool _lordAnnouncementDone  = false;
        private static readonly Random _rng = new Random();

        private static readonly string[] _premonitions =
        {
            "The fire in you whispers tonight — something distant is ending.",
            "On the road, you pass the ruins of a great pyre. The air still carries old smoke. Something in you recognises it.",
            "You wake with the taste of ash on your tongue. The inner fire is restless.",
            "Your shadow moves a half-step behind you. The fire inside is watching something you cannot see.",
            "You watch a forge-fire die to coals. For a moment, you understand exactly what you are.",
            "A child in the village stares at your hands as you pass. She sees something there that you do not show others.",
            "The fire does not sleep when you do. You feel it turning in its sleep, searching.",
            "You smell smoke where there is none. An old instinct — the fire recognising itself in the distance.",
        };

        public override void RegisterEvents()
        {
            CampaignEvents.OnCharacterCreationIsOverEvent.AddNonSerializedListener(this, OnNewGameCreated);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
            CampaignEvents.OnMissionEndedEvent.AddNonSerializedListener(this, OnMissionEnded);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.HeroCreated.AddNonSerializedListener(this, OnHeroCreated);
            CampaignEvents.NewCompanionAdded.AddNonSerializedListener(this, OnCompanionAdded);
        }

        // ── New game prompt ───────────────────────────────────────────────────
        private void OnNewGameCreated()
        {
            MageKnowledge.ResetForNewGame();

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "The Gift",
                "As a child, you sometimes sensed things others could not — warmth ebbing from the wounded, the weight behind dying eyes. Do you feel it still?",
                new List<InquiryElement>
                {
                    new InquiryElement("yes", "I feel it still.", null, true,
                        "The current stirs in you. Press Alt+B to open your grimoire."),
                    new InquiryElement("no", "I feel nothing.", null, true,
                        "The current passes you by."),
                },
                false, 1, 1,
                "Choose.",
                "",
                chosen =>
                {
                    bool isMage = chosen?.Any(e => e.Identifier is string s && s == "yes") == true;
                    MageKnowledge.SetMage(isMage);
                    if (isMage)
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The gift stirs. Hold Alt, type form keys (WASD), press X to Break, type effect keys, release Alt to cast.",
                            new Color(0.7f, 0.5f, 1.0f)));
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Forms: W=Blast  A=Wave  D=Barrier  S=Burst  |  Effects: W=Damage  A=Push  D=Morale  S=Reverse  |  Alt+X = Grimoire",
                            new Color(0.6f, 0.6f, 0.8f)));
                    }
                    else
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The current passes you by.",
                            new Color(0.6f, 0.6f, 0.6f)));
                    }
                    _selectionDone = true;
                    try { ColourLordRegistry.SeedInitialLords(); } catch { }
                },
                _ =>
                {
                    MageKnowledge.SetMage(false);
                    _selectionDone = true;
                    try { ColourLordRegistry.SeedInitialLords(); } catch { }
                },
                "", false
            ), false, true);
        }

        // ── Daily tick ────────────────────────────────────────────────────────
        private void OnDailyTick()
        {
            if (!_selectionDone)
            {
                _selectionDone = true;
                try { ColourLordRegistry.SeedInitialLords(); } catch { }
            }
            try { ColourLordRegistry.SeedInitialLords(); } catch { }
            try { ColourLordRegistry.DailyMapCast(); } catch { }
            try { AgingSystem.DailyAgeCheck(); } catch { }
            try { CheckReapPrisonerYield(); } catch { }
            if (_reapRaidCooldown > 0) _reapRaidCooldown--;
            try { TickLordAnnouncement(); } catch { }
            _dayCounter++;
            if (_dayCounter % 30 == 0) try { OnMonthlyTick(); } catch { }
        }

        // ── Weekly tick ───────────────────────────────────────────────────────
        private void OnWeeklyTick()
        {
            try { ColourLordRegistry.CheckPopulationBounds(); } catch { }
            try { ColourLordRegistry.CheckAgeLimit(); } catch { }
        }

        // ── Mission ended ─────────────────────────────────────────────────────
        private void OnMissionEnded(IMission mission)
        {
            try { ColourLordAI.ClearCooldowns(); } catch { }
            try { SpellEffects.ClearAreaEffects(); } catch { }
            try { SpellEffects.ClearSelfEffects(); } catch { }
            try { SpellEffects.ClearGlows(); } catch { }
            try { SpellEffects.ClearMoves(); } catch { }
        }

        // ── Map event ended (battle result) ───────────────────────────────────
        private void OnMapEventEnded(MapEvent mapEvent)
        {
            if (mapEvent == null) return;
            try { ApplyNpcBattleAging(mapEvent); } catch { }
            try { ApplyNpcBattleMoraleBonus(mapEvent); } catch { }
            try { CheckReapRaidYield(mapEvent); } catch { }
            // Refresh snapshot so battle-captured prisoners don't count as discards
            try { _prisonerCountSnapshot = MobileParty.MainParty?.PrisonRoster?.TotalManCount ?? _prisonerCountSnapshot; } catch { }
        }

        // ── Monthly atmospheric events ────────────────────────────────────────
        private void OnMonthlyTick()
        {
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

        // ── Reap: raid yield (7-day cooldown) ────────────────────────────────
        private void CheckReapRaidYield(MapEvent mapEvent)
        {
            if (!MageKnowledge.IsMage || !TalentSystem.Has(TalentId.Reap)) return;
            if (mapEvent.EventType != MapEvent.BattleTypes.Raid) return;
            if (_reapRaidCooldown > 0) return;

            bool playerAttacker = mapEvent.AttackerSide?.Parties
                .Any(p => p.Party == PartyBase.MainParty) == true;
            if (!playerAttacker) return;
            if (mapEvent.WinningSide != BattleSideEnum.Attacker) return;

            AgingSystem.RejuvenateHero(Hero.MainHero, 5);
            _reapRaidCooldown = 7;
        }

        // ── Reap: prisoner discard yield ──────────────────────────────────────
        private void CheckReapPrisonerYield()
        {
            if (!MageKnowledge.IsMage || !TalentSystem.Has(TalentId.Reap)) return;

            int current = MobileParty.MainParty?.PrisonRoster?.TotalManCount ?? 0;

            if (_prisonerCountSnapshot >= 0 && current < _prisonerCountSnapshot)
            {
                int discarded = _prisonerCountSnapshot - current;
                int daysGained = 0;
                for (int i = 0; i < discarded; i++)
                {
                    if (_rng.NextDouble() < 0.05)
                        daysGained++;
                }
                if (daysGained > 0)
                    AgingSystem.RejuvenateHero(Hero.MainHero, daysGained);
            }

            _prisonerCountSnapshot = current;
        }

        private void ApplyNpcBattleAging(MapEvent mapEvent)
        {
            bool playerInvolved = false;
            try
            {
                playerInvolved =
                    mapEvent.AttackerSide.Parties.Any(p => p.Party == PartyBase.MainParty) ||
                    mapEvent.DefenderSide.Parties.Any(p => p.Party == PartyBase.MainParty);
            }
            catch { }

            foreach (MapEventSide side in new[] { mapEvent.AttackerSide, mapEvent.DefenderSide })
            {
                if (side == null) continue;
                try
                {
                    foreach (var meparty in side.Parties)
                    {
                        try
                        {
                            Hero leader = meparty?.Party?.LeaderHero;
                            if (leader == null || leader == Hero.MainHero
                                || !ColourLordRegistry.IsColourLord(leader)) continue;

                            int casts = ColourLordAI.ConsumeBattleCasts(leader);
                            if (casts <= 0) continue;

                            // 1 aging day per spell cast — formula-independent, keeps NPC
                            // mage lord lifespan stable regardless of player formula tuning.
                            int agingDays = casts;
                            if (agingDays <= 0) continue;

                            AgingSystem.AgeHero(leader, agingDays);
                            if (playerInvolved)
                                InformationManager.DisplayMessage(new InformationMessage(
                                    $"{leader.Name} is spent by the working — {agingDays} day{(agingDays > 1 ? "s" : "")} older.",
                                    new Color(0.5f, 0.4f, 0.7f)));
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        private void ApplyNpcBattleMoraleBonus(MapEvent mapEvent)
        {
            bool playerInvolved = false;
            try
            {
                playerInvolved =
                    mapEvent.AttackerSide.Parties.Any(p => p.Party == PartyBase.MainParty) ||
                    mapEvent.DefenderSide.Parties.Any(p => p.Party == PartyBase.MainParty);
            }
            catch { }
            if (playerInvolved) return;

            foreach (MapEventSide side in new[] { mapEvent.AttackerSide, mapEvent.DefenderSide })
            {
                if (side == null) continue;
                try
                {
                    bool hasMage = side.Parties.Any(p =>
                    {
                        Hero leader = p?.Party?.LeaderHero;
                        return leader != null && ColourLordRegistry.IsColourLord(leader);
                    });
                    if (!hasMage) continue;

                    foreach (var meparty in side.Parties)
                        try
                        {
                            if (meparty?.Party?.MobileParty != null)
                                meparty.Party.MobileParty.RecentEventsMorale += 10f;
                        }
                        catch { }
                }
                catch { }
            }
        }

        // ── Hero killed ───────────────────────────────────────────────────────
        private void OnHeroKilled(Hero victim, Hero killer,
            KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            if (ColourLordRegistry.IsColourLord(victim))
                try { ColourLordRegistry.OnLordDied(victim); } catch { }

            if (detail != KillCharacterAction.KillCharacterActionDetail.Executed) return;
            if (killer == null || !victim.IsLord) return;

            // Player DevourLife: executing a captured lord draws back 100 days
            if (killer == Hero.MainHero
                && MageKnowledge.IsMage
                && TalentSystem.Has(TalentId.DevourLife))
            {
                try { AgingSystem.RejuvenateHero(Hero.MainHero, 100); } catch { }
            }

            // NPC DevourLife: merciless/devious mage lord executioner absorbs 1 day
            if (killer != Hero.MainHero
                && ColourLordRegistry.IsColourLord(killer)
                && ColourLordRegistry.HasTalent(killer, TalentId.DevourLife))
            {
                try
                {
                    int merciless = killer.GetTraitLevel(DefaultTraits.Mercy);
                    int devious   = killer.GetTraitLevel(DefaultTraits.Honor);
                    if (merciless < 0 || devious < 0)
                        killer.SetBirthDay(killer.BirthDay + CampaignTime.Days(1));
                }
                catch { }
            }
        }

        // ── Child inheritance ─────────────────────────────────────────────────
        // 75% if both parents are mages, 50% if one, 10% otherwise
        private void OnHeroCreated(Hero hero, bool bornNaturally)
        {
            if (!bornNaturally) return;
            try
            {
                bool motherMage = hero.Mother != null && (
                    hero.Mother == Hero.MainHero ? MageKnowledge.IsMage
                                                 : ColourLordRegistry.IsColourLord(hero.Mother));
                bool fatherMage = hero.Father != null && (
                    hero.Father == Hero.MainHero ? MageKnowledge.IsMage
                                                 : ColourLordRegistry.IsColourLord(hero.Father));

                float chance;
                if (motherMage && fatherMage)       chance = 0.75f;
                else if (motherMage || fatherMage)   chance = 0.50f;
                else                                 chance = 0.10f;

                if ((float)_rng.NextDouble() < chance)
                {
                    bool isPlayerChild = hero.Mother == Hero.MainHero || hero.Father == Hero.MainHero;
                    if (isPlayerChild)
                        MageKnowledge.AddGiftedChild(hero.StringId);
                    else
                        ColourLordRegistry.SetMage(hero, true);
                }
            }
            catch { }
        }

        // ── Companion recruitment ─────────────────────────────────────────────
        private void OnCompanionAdded(Hero companion)
        {
            try
            {
                if (_rng.Next(100) < 10)
                    ColourLordRegistry.SetMage(companion, true);
            }
            catch { }
        }

        // ── Save / Load ───────────────────────────────────────────────────────
        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("LDM_SelectionDone",        ref _selectionDone);
            dataStore.SyncData("LDM_PrisonerSnapshot",     ref _prisonerCountSnapshot);
            dataStore.SyncData("LDM_DayCounter",           ref _dayCounter);
            dataStore.SyncData("LDM_ReapRaidCooldown",     ref _reapRaidCooldown);
            dataStore.SyncData("LDM_LordAnnounceCD",       ref _lordAnnounceCountdown);
            dataStore.SyncData("LDM_LordAnnounceDone",     ref _lordAnnouncementDone);
            MageKnowledge.Save(dataStore);      // also saves TalentSystem internally
            ColourLordRegistry.Save(dataStore);
        }
    }
}
