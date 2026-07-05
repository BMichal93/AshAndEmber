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
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                if (ColourLordRegistry.IsColourLord(victim))
                    try { ColourLordRegistry.OnLordDied(victim); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                try { RivalShadowSystem.OnHeroKilled(victim); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { EmberConclaveSystem.OnHeroKilled(victim, killer); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                // Whispers: killing an Ashen lord costs 1 — the cold notices its
                // own dying, but fighting the Ashen is the mod's core loop and
                // must not be the fastest road to corruption.
                if (killer == Hero.MainHero && MageKnowledge.IsMage
                    && ColourLordRegistry.IsAshenLord(victim))
                    try { MageKnowledge.AddWhispers(1); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                if (detail != KillCharacterAction.KillCharacterActionDetail.Executed) return;
                if (killer == null) return;
                bool victimIsLord = false;
                try { victimIsLord = victim.IsLord; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                if (!victimIsLord) return;

                // Blood: executing a captured lord gives back the years the fire has
                // burned, scaled by the victim's standing — clan tier × 25 days (25–150).
                // Guard with a StringId set — HeroKilledEvent can fire twice under certain
                // Bannerlord load/save conditions, causing double rejuvenation.
                if (killer == Hero.MainHero
                    && MageKnowledge.IsMage
                    && MageElementKnowledge.HasBlood
                    && victim.StringId != null
                    && !_executedLordIds.Contains(victim.StringId))
                {
                    _executedLordIds.Add(victim.StringId);
                    int tier = 1;
                    try { tier = Math.Max(1, Math.Min(6, victim.Clan?.Tier ?? 1)); } catch { tier = 1; }
                    try { AgingSystem.RestoreLifeExpectancy(Hero.MainHero, ElementMagicMath.BloodRejuvenationDays(tier)); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
                // Whispers: executing any lord costs 5
                if (killer == Hero.MainHero && MageKnowledge.IsMage)
                    try { MageKnowledge.AddWhispers(5); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                if (killer == Hero.MainHero)
                    try { AshenQuestSystem.OnHeroExecuted(victim); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                if (victim.IsLord)
                    try { EmberConclaveSystem.OnLordExecuted(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                    try { KillCharacterAction.ApplyByMurder(hero, null, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Save / Load ───────────────────────────────────────────────────────
        public override void SyncData(IDataStore dataStore)
        {
            try { dataStore.SyncData("LDM_SelectionDone",    ref _selectionDone); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { dataStore.SyncData("LDM_PrisonerSnapshot", ref _prisonerCountSnapshot); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { dataStore.SyncData("LDM_DayCounter",       ref _dayCounter); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { dataStore.SyncData("LDM_ReapRaidCooldown", ref _reapRaidCooldown); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { dataStore.SyncData("LDM_LordAnnounceCD",   ref _lordAnnounceCountdown); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { dataStore.SyncData("LDM_LordAnnounceDone", ref _lordAnnouncementDone); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { MageKnowledge.Save(dataStore); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { MageElementKnowledge.Save(dataStore); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { RivalShadowSystem.Save(dataStore); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { ColourLordRegistry.Save(dataStore); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { AshenCitySystem.Save(dataStore); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { FireWorshippersSystem.Save(dataStore); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { CampaignMapEvents.Save(dataStore); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { SettlementEncounters.Save(dataStore); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { DragonQuestSystem.Save(dataStore); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { AshenQuestSystem.Save(dataStore); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { BurningLabQuestSystem.Save(dataStore); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { EmberConclaveSystem.Save(dataStore); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { AgingSystem.Save(dataStore); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { TempleCovenant.Save(dataStore); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { AshenRuinSystem.Save(dataStore); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { ApprenticeSystem.Save(dataStore); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            if (!dataStore.IsSaving)
                _pendingAppearanceRefresh = true;
        }
    }
}
