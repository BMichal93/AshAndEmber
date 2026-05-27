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

namespace ColoursOfCalradia
{
    public class MagicCampaignBehavior : CampaignBehaviorBase
    {
        private bool _selectionDone;
        private static readonly Random _rng = new Random();

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
                            "The gift stirs. Hold Alt, type form keys (WASD), press E to Break, type effect keys, release Alt to cast.",
                            new Color(0.7f, 0.5f, 1.0f)));
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Forms: W=Blast  A=Aura  D=Barrier  S=Burst  |  Effects: W=Damage  A=Push  D=Morale  S=Reverse  |  Alt+B = Grimoire",
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

                            int agingDays = AgingSystem.ComputeBattleAgingCost(casts * 4, false);
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

            // Devour Life: Merciless/Devious mage executioner absorbs life from a kill
            if (detail == KillCharacterAction.KillCharacterActionDetail.Executed && killer != null
                && killer != Hero.MainHero && ColourLordRegistry.IsColourLord(killer)
                && ColourLordRegistry.HasTalent(killer, TalentId.DevourLife))
            {
                try
                {
                    int merciless = killer.GetTraitLevel(DefaultTraits.Mercy);
                    int devious   = killer.GetTraitLevel(DefaultTraits.Honor);
                    if (merciless < 0 || devious < 0)
                    {
                        // Absorb 1 day — rejuvenate by setting birthday forward
                        killer.SetBirthDay(killer.BirthDay + CampaignTime.Days(1));
                    }
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
            dataStore.SyncData("LDM_SelectionDone", ref _selectionDone);
            MageKnowledge.Save(dataStore);      // also saves TalentSystem internally
            ColourLordRegistry.Save(dataStore);
        }
    }
}
