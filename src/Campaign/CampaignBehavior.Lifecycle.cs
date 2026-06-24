// =============================================================================
// ASH AND EMBER — CampaignBehavior.Lifecycle.cs
// Hero killed/created, companion recruitment, and save/load.
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
        // ── Hero killed ───────────────────────────────────────────────────────
        private void OnHeroKilled(Hero victim, Hero killer,
            KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            try
            {
                // Game of Thrones: if this was a faction leader, the kingdom may fracture.
                // Check before any other processing since succession may change Kingdom.Leader.
                try
                {
                    var vClan = victim?.Clan;
                    if (vClan?.Kingdom?.RulingClan == vClan && vClan?.Kingdom?.Leader == victim)
                        CampaignMapEvents.OnFactionLeaderKilled(vClan.Kingdom);
                }
                catch { }

                if (ColourLordRegistry.IsColourLord(victim))
                    try { ColourLordRegistry.OnLordDied(victim); } catch { }

                try { RivalShadowSystem.OnHeroKilled(victim); } catch { }
                try { EmberConclaveSystem.OnHeroKilled(victim, killer); } catch { }

                // Whispers: killing an Ashen lord costs 1 — the cold notices its
                // own dying, but fighting the Ashen is the mod's core loop and
                // must not be the fastest road to corruption.
                if (killer == Hero.MainHero && MageKnowledge.IsMage
                    && ColourLordRegistry.IsAshenLord(victim))
                    try { MageKnowledge.AddWhispers(1); } catch { }

                if (detail != KillCharacterAction.KillCharacterActionDetail.Executed) return;
                if (killer == null) return;
                bool victimIsLord = false;
                try { victimIsLord = victim.IsLord; } catch { }
                if (!victimIsLord) return;

                // Reap: executing a captured lord draws back days of life scaled by
                // the victim's standing — 20 + 10 per clan tier (20–80). A flat 100
                // bought ~20 large battle spells per execution and trivialised the
                // aging economy outright.
                // Guard with a StringId set — HeroKilledEvent can fire twice under certain
                // Bannerlord load/save conditions, causing double rejuvenation.
                if (killer == Hero.MainHero
                    && MageKnowledge.IsMage
                    && TalentSystem.Has(TalentId.Reap)
                    && victim.StringId != null
                    && !_executedLordIds.Contains(victim.StringId))
                {
                    _executedLordIds.Add(victim.StringId);
                    int reapTier = 0;
                    try { reapTier = Math.Max(0, Math.Min(6, victim.Clan?.Tier ?? 0)); } catch { }
                    try { AgingSystem.RejuvenateHero(Hero.MainHero, 20 + 10 * reapTier); } catch { }
                }
                // Whispers: executing any lord costs 5
                if (killer == Hero.MainHero && MageKnowledge.IsMage)
                    try { MageKnowledge.AddWhispers(5); } catch { }
                if (killer == Hero.MainHero)
                    try { AshenQuestSystem.OnHeroExecuted(victim); } catch { }
                if (victim.IsLord)
                    try { EmberConclaveSystem.OnLordExecuted(); } catch { }
            }
            catch { }
        }

        // ── Child inheritance ─────────────────────────────────────────────────
        // 75% if both parents are mages, 50% if one, 10% otherwise.
        // Ashen lords cannot have children — the cold preserves, not creates.
        private void OnHeroCreated(Hero hero, bool bornNaturally)
        {
            if (!bornNaturally) return;
            try
            {
                // Ashen parents cannot produce living children — still the cold.
                bool motherAshen = hero.Mother != null && ColourLordRegistry.IsAshenLord(hero.Mother);
                bool fatherAshen = hero.Father != null && ColourLordRegistry.IsAshenLord(hero.Father);
                if (motherAshen || fatherAshen)
                {
                    try { KillCharacterAction.ApplyByMurder(hero, null, false); } catch { }
                    return;
                }

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
                if (_rng.Next(100) < 20)
                {
                    ColourLordRegistry.RegisterCompanionMage(companion);
                    // Companions with the gift always carry 1–3 enchantments.
                    int enchants = 1 + _rng.Next(3);
                    ColourLordRegistry.AssignCompanionEnchantments(companion, enchants);
                    string[] joinLines =
                    {
                        $"{companion.Name} carries the fire. You felt it before they spoke — the same warmth, the same weight behind the eyes.",
                        $"There is something in {companion.Name} that answers when yours calls. The gift, shaped differently, but the same current.",
                        $"{companion.Name} already knew. They saw it in you first. The fire recognises itself.",
                    };
                    string joinMsg = joinLines[_rng.Next(joinLines.Length)];
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{joinMsg} ({enchants} working{(enchants != 1 ? "s" : "")} shaped in them.)",
                        new Color(0.75f, 0.55f, 0.95f)));
                }
            }
            catch { }
        }

        // ── Save / Load ───────────────────────────────────────────────────────
        public override void SyncData(IDataStore dataStore)
        {
            try { dataStore.SyncData("LDM_SelectionDone",    ref _selectionDone); } catch { }
            try { dataStore.SyncData("LDM_PrisonerSnapshot", ref _prisonerCountSnapshot); } catch { }
            try { dataStore.SyncData("LDM_DayCounter",       ref _dayCounter); } catch { }
            try { dataStore.SyncData("LDM_ReapRaidCooldown", ref _reapRaidCooldown); } catch { }
            try { dataStore.SyncData("LDM_LordAnnounceCD",   ref _lordAnnounceCountdown); } catch { }
            try { dataStore.SyncData("LDM_LordAnnounceDone", ref _lordAnnouncementDone); } catch { }
            // Executed lord IDs — persisted so the double-fire guard survives save/load
            try
            {
                var elList = _executedLordIds.ToList();
                dataStore.SyncData("LDM_ExecutedLordIds", ref elList);
                if (elList != null)
                {
                    _executedLordIds.Clear();
                    foreach (var id in elList) _executedLordIds.Add(id);
                }
            }
            catch { }
            // Ashen prisoner escape tracker (save/load)
            try
            {
                var capKeys = _ashenCaptiveDays.Keys.ToList();
                var capVals = _ashenCaptiveDays.Values.ToList();
                dataStore.SyncData("LDM_AshenCapKeys", ref capKeys);
                dataStore.SyncData("LDM_AshenCapVals", ref capVals);
                if (capKeys != null && capVals != null && capKeys.Count == capVals.Count)
                {
                    _ashenCaptiveDays.Clear();
                    for (int i = 0; i < capKeys.Count; i++)
                        _ashenCaptiveDays[capKeys[i]] = capVals[i];
                }
            }
            catch { }
            try { MageKnowledge.Save(dataStore); } catch { }
            try { RivalShadowSystem.Save(dataStore); } catch { }
            try { ColourLordRegistry.Save(dataStore); } catch { }
            try { AshenCitySystem.Save(dataStore); } catch { }
            try { FireWorshippersSystem.Save(dataStore); } catch { }
            try { CampaignMapEvents.Save(dataStore); } catch { }
            try { SettlementEncounters.Save(dataStore); } catch { }
            try { DragonQuestSystem.Save(dataStore); } catch { }
            try { AshenQuestSystem.Save(dataStore); } catch { }
            try { BurningLabQuestSystem.Save(dataStore); } catch { }
            try { EmberConclaveSystem.Save(dataStore); } catch { }
            try { AgingSystem.Save(dataStore); } catch { }
            try { TempleCovenant.Save(dataStore); } catch { }
            try { AshenRuinSystem.Save(dataStore); } catch { }
            try { ApprenticeSystem.Save(dataStore); } catch { }
            if (!dataStore.IsSaving)
                _pendingAppearanceRefresh = true;
        }
    }
}
