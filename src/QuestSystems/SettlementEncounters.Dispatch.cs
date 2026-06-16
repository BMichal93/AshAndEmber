// =============================================================================
// ASH AND EMBER — SettlementEncounters.Dispatch.cs
// Event dispatch — selecting and firing encounters on enter/leave/battle.
// Partial of SettlementEncounters (shared state lives in SettlementEncounters.cs).
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
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static partial class SettlementEncounters
    {
        // ── Dispatch ──────────────────────────────────────────────────────────
        private static void TryFireEnter(Settlement s)
        {
            CheckAgingAmbient(s);
            bool mage   = MageKnowledge.IsMage;
            bool ashen  = MageKnowledge.IsAshen;
            float ren   = Hero.MainHero?.Clan?.Renown ?? 0f;
            int clanTier = Hero.MainHero?.Clan?.Tier ?? 0;
            bool village = s.IsVillage;
            bool town    = s.IsTown || s.IsCastle;

            var pool = new List<Action<Settlement>>();

            float _days = (float)CampaignTime.Now.ToDays;
            string _cult = s.Culture?.StringId ?? "";
            bool _cinderEligible = _cinderVigilCooldown == 0 && _days >= 50f
                && (_cult == "battania" || _cult == "sturgia" || _cult == "aserai" || _cult == "khuzait");

            if (village)
            {
                if (_familyFeverCooldown == 0 && HasSpouseAndChild()) pool.Add(E_TheWasting);
                if (_hedgeWitchCooldown == 0 && HasHedgeWitchCondition()) pool.Add(E_NightVisitor);
                pool.Add(EV8_ColdTrail);
                if (_burningVillageCountdown == 0) pool.Add(EV_DarknessSpreads);
                if (_burningWitchCountdown == 0)   pool.Add(EV_BurningWitch);
                if (_trinketPhase == 0)
                {
                    pool.Add(EB_TrinketEmberShard);
                    pool.Add(EB_TrinketBlindEye);
                    pool.Add(EB_TrinketPaleCompass);
                }
                if (mage)
                {
                    pool.Add(E_OldFlameSeer);
                    if (!ashen) pool.Add(EV4_GiftedChild);
                    if (!ashen && _selfTaughtMageCountdown == 0) pool.Add(EV7_SelfTaughtMage);
                    pool.Add(EV8_TheLie);
                    if (!ashen && ren >= 600f && _oldMastersStudentCountdown == 0) pool.Add(EV7_OldMastersStudent);
                    if (!ashen && _emberTitheRefusedCountdown == 0) pool.Add(E_EmberTithe);
                    if (_childEventCooldown == 0 && HasEligibleChild()) pool.Add(E_DarkeningInheritance);
                }
                if (ashen) pool.Add(EV2_DogWontStop);
                if (ashen) pool.Add(EV_MemoryHunger);
                if (_cinderEligible) pool.Add(EC_CinderVigil);
                // Child Who Does Not Sleep — one global arc; phase 0=initial, 10=echo
                if (_ashChildPhase == 0 || _ashChildPhase == 10) pool.Add(EV_ChildWhoDoesNotSleep);
                // Ashes in the Bread — general, 45-day cooldown, no deferred pending
                if (_ashBreadCooldown == 0 && _ashBreadCountdown == 0) pool.Add(EV_AshesInTheBread);
            }
            if (town)
            {
                if (_oldEnemyCountdown == 0) pool.Add(E_OldEnemy);
                if (_familyFeverCooldown == 0 && HasSpouseAndChild()) pool.Add(E_TheWasting);
                if (_hedgeWitchCooldown == 0 && HasHedgeWitchCondition()) pool.Add(E_NightVisitor);
                if (ren >= 250f && _merchantLedgerCountdown == 0) pool.Add(EC8_MerchantLedger);
                if (ren >= 300f) pool.Add(EC8_ReluctantOfficial);
                if (!ashen && _priestBeatCountdown == 0) pool.Add(EC_LocalPriest);
                if (_babyEventCountdown == 0 && _pregnancyCountdown == 0 && _tavernRobberyCountdown == 0)
                    pool.Add(EC_TavernStranger);
                if (_brokenSealCountdown == 0 && _brokenSealPlotType == 0) pool.Add(EC_BrokenSeal);
                if (_trinketPhase == 0)
                {
                    pool.Add(EB_TrinketEmberShard);
                    pool.Add(EB_TrinketBlindEye);
                    pool.Add(EB_TrinketPaleCompass);
                }
                if (mage)
                {
                    if (!ashen && _emberTitheRefusedCountdown == 0) pool.Add(E_EmberTithe);
                    if (_childEventCooldown == 0 && HasEligibleChild()) pool.Add(E_DarkeningInheritance);
                }
                pool.Add(EC9_AshenElixir);
                if (ashen) pool.Add(EV_MemoryHunger);
                if (_cinderEligible) pool.Add(EC_CinderVigil);
                // Poor knight wants to prove himself in tournament (or just encountered by chance)
                if (!ashen && _poorKnightCooldown == 0) pool.Add(EC_PoorKnight);
                // Tavern harassment — clan tier < 5, not Ashen
                if (!ashen && clanTier < 5) pool.Add(EC_TavernHarassment);
                // One-time Aserai alchemist with a dangerous idea
                if (_weaponInventorFound == 0 && _cult == "aserai") pool.Add(E_WeaponInventor);
                // Cartographer of Silences — mage or Ashen, city enter, 3-phase, 60-day cooldown
                if ((mage || ashen) && _cartographerCooldown == 0 && _cartographerPhase < 3)
                    pool.Add(EV_CartographerOfSilences);
            }

            Fire(pool, s);
        }

        private static void TryFireLeave(Settlement s)
        {
            CheckAgingAmbient(s);
            bool mage   = MageKnowledge.IsMage;
            bool ashen  = MageKnowledge.IsAshen;
            float ren   = Hero.MainHero?.Clan?.Renown ?? 0f;
            bool village = s.IsVillage;
            bool town    = s.IsTown || s.IsCastle;

            var pool = new List<Action<Settlement>>();

            if (village)
            {
                pool.Add(LV8_PoisonedWell);
                pool.Add(LV_ColdEmbrace);
                pool.Add(LV_ColdDream);
                pool.Add(LV_ThreeWitches);
                pool.Add(LV_HollowHour);
                if (mage && !ashen)
                {
                    pool.Add(E_MothersPlea);
                }
                // Fever Road — general, recurring hazard; open window OR fresh start
                if (_feverRoadCooldown == 0 && (!_feverRoadActive || _feverRoadTriggerCount < 3))
                    pool.Add(LV_FeverRoad);
            }
            if (town)
            {
                if (!ashen && ren >= 300f) pool.Add(E_BardsRequest);
                if (mage && _bloodTitheCountdown == 0 && _bloodTitheRevealCountdown == 0)
                {
                    pool.Add(LC_BloodCollector);
                }
                if (ashen) pool.Add(LC4_RecognizedByAshen);
                // Encounter: Hope — young mage afraid of Ashen; clan tier ≥ 2, non-Ashen mage
                int clanTier = Hero.MainHero?.Clan?.Tier ?? 0;
                if (mage && !ashen && clanTier >= 2) pool.Add(LC_YoungMageHope);
                pool.Add(EL_InsultAtGate);
                // Vow Undischarged — general, city leave, clan tier ≥ 2, 90-day cooldown
                if (_vowCooldown == 0 && _vowPhase == 0 && clanTier >= 2)
                    pool.Add(EL_VowUndischarged);
            }

            Fire(pool, s);
        }

        private static void Fire(List<Action<Settlement>> pool, Settlement s)
        {
            if (pool.Count == 0) return;
            if (MageKnowledge._deferredInquiry != null) return; // don't clobber a queued quest popup
            _cooldown = MinDaysBetween;

            var filtered = pool.Where(a => !_recentEncounters.Contains(a.Method.Name)).ToList();
            if (filtered.Count == 0) filtered = pool;

            Action<Settlement> chosen = filtered[_rng.Next(filtered.Count)];
            _recentEncounters.Add(chosen.Method.Name);
            if (_recentEncounters.Count > RecentEncounterMemory)
                _recentEncounters.RemoveAt(0);

            ShowEncounterHint(s);
            MageKnowledge._deferredInquiry = () => { try { chosen(s); } catch { } };
        }

        private static readonly string[] _enterVillageHints = {
            "Something stirs as you pass through the village.",
            "A hush settles over the road — the fire feels it before you do.",
            "The air changes as you enter the village.",
            "A dog stops barking when you pass. A child stops playing.",
        };
        private static readonly string[] _enterTownHints = {
            "Something draws the eye as you pass through the gates.",
            "The crowd shifts before you reach the square.",
            "You are not the only one watching today.",
            "The city has a different feel at this hour.",
        };
        private static readonly string[] _enterCastleHints = {
            "The keep's shadow falls differently today.",
            "Eyes find you before you reach the courtyard.",
            "Something waits behind the walls.",
            "The garrison watches the road more carefully than usual.",
        };
        private static void ShowEncounterHint(Settlement s)
        {
            try
            {
                string[] pool = s.IsVillage
                    ? _enterVillageHints
                    : (s.IsTown ? _enterTownHints : _enterCastleHints);
                MBInformationManager.AddQuickInformation(new TextObject(pool[_rng.Next(pool.Length)]));
            }
            catch { }
        }

        private static void FireBattle(List<Action> pool)
        {
            if (pool.Count == 0) return;
            if (MageKnowledge._deferredInquiry != null) return; // don't clobber a queued quest popup
            _cooldown = MinDaysBetween;
            Action chosen = pool[_rng.Next(pool.Count)];
            MageKnowledge._deferredInquiry = () => { try { chosen(); } catch { } };
        }

        private static void TryFireBattle()
        {
            bool mage  = MageKnowledge.IsMage;
            bool ashen = MageKnowledge.IsAshen;
            float ren  = Hero.MainHero?.Clan?.Renown ?? 0f;
            int   ashenCasts = MagicInputHandler.AshenBattleCastCount;
            int   clanTier   = Hero.MainHero?.Clan?.Tier ?? 0;
            var pool  = new List<Action>();

            if (ashen && ashenCasts >= 3)
            {
                // More casts = higher weight: 1 copy at 3 casts, up to 3 copies at 5+
                int weight = Math.Min(ashenCasts - 2, 3);
                for (int i = 0; i < weight; i++)
                    pool.Add(EB_AshenMemoryDrain);
            }

            // Hero-inspired: after a LOST battle, clan tier ≥ 3 — fleeing refugees
            if (!_lastBattleWon && clanTier >= 3)
                pool.Add(EB_HeroInspired);

            // Crystal machine: mage, won, enemy side had Ashen, no recent find and no deferred D pending
            if (mage && _lastBattleWon && _lastBattleHadAshenEnemy
                && _ashenMachineryCooldown == 0 && _ashenMachineryCountdown == 0)
                pool.Add(EB_AshenMachinery);

            FireBattle(pool);
        }

        private static void TryFireSiege()
        {
            bool mage = MageKnowledge.IsMage;
            bool attWon = _lastBattleAsAttacker && _lastBattleWon;

            // Priority: Burning Laboratory quest fires if attacker won and probability passes.
            // This gates on campaign day 80–300+ and fires at most once per campaign.
            if (attWon && BurningLabQuestSystem.RollLabDiscovery())
            {
                _cooldown = MinDaysBetween;
                MageKnowledge._deferredInquiry = BurningLabQuestSystem.ShowInitialDiscovery;
                return;
            }

            var pool = new List<Action>();

            if (attWon)
            {
                pool.Add(ES8_SiegeStores);
                if (mage) pool.Add(ES4_AshenCrystal);
                if (mage) pool.Add(ES7_FallenLaboratory);
                if (mage && _ancientBookFound == 0) pool.Add(ES_AncientGrimoire);
            }
            else
            {
                // Defender won or draw — still interesting
            }

            FireBattle(pool);
        }

        private static void TryFireRaid()
        {
            var pool = new List<Action>();
            // (no raid encounters currently active)
            FireBattle(pool);
        }

    }
}
