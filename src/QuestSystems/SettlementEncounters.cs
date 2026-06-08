// =============================================================================
// ASH AND EMBER — SettlementEncounters.cs
// Random personal encounters triggered when the player enters or leaves a
// settlement. The system tracks Hero.MainHero.CurrentSettlement on the daily
// tick to detect transitions, then fires one encounter from an appropriate
// pool (gated by mage status, Ashen status, and renown).
//
// ┌─────────────────────────────┬───────────────────────┬──────────────────┐
// │ Event                       │ Trigger               │ Gate             │
// ├─────────────────────────────┼───────────────────────┼──────────────────┤
// │ A Mother's Plea             │ Leave village         │ Mage             │
// │ The Widow's Pyre            │ Leave village         │ Mage             │
// │ Signal Fire                 │ Leave village         │ Mage             │
// │ The Elder's Sending         │ Leave village         │ Mage             │
// │ Beggar at the Crossroads    │ Leave village         │ General          │
// │ The Lame Horse              │ Leave village         │ General          │
// │ The Coin Game               │ Leave village         │ General          │
// │ Torches at Dusk             │ Leave village         │ General          │
// │ The Eager Recruit           │ Leave village         │ General          │
// │ The Festival Farewell       │ Leave village         │ General          │
// │ The Hollow Hour             │ Leave village         │ General          │
// │ The Old Flame-Seer          │ Enter village         │ Mage             │
// │ The Healer's Trade          │ Enter village         │ Mage             │
// │ Fire and Straw              │ Enter village         │ Mage             │
// │ The Shrine Goes Out         │ Enter village         │ Mage             │
// │ The Warmth Merchant         │ Enter village         │ Mage             │
// │ A Family's Quarrel          │ Enter village         │ General          │
// │ The Harvest Festival        │ Enter village         │ General          │
// │ Ashen Aftermath             │ Enter village         │ General          │
// │ The Warning                 │ Enter village         │ General          │
// │ The Spilled Cart            │ Enter village         │ General          │
// │ The Veteran's Question      │ Leave city/castle     │ Mage             │
// │ The Condemned               │ Leave city/castle     │ Mage             │
// │ Petitioners' Gate           │ Leave city/castle     │ Mage, Renown≥500 │
// │ The Displaced Noble         │ Leave city/castle     │ General          │
// │ The Bard's Request          │ Leave city/castle     │ General, Ren≥300 │
// │ A Detained Soldier          │ Leave city/castle     │ General          │
// │ The Guild's Offer           │ Leave city/castle     │ General, Ren≥500 │
// │ The Ashen Informant         │ Leave city/castle     │ General          │
// │ An Insult at the Gate       │ Leave city/castle     │ General          │
// │ The Curious Scholar         │ Enter city/castle     │ Mage             │
// │ Another Fire                │ Enter city/castle     │ Mage             │
// │ The Ash-Touched Market      │ Enter city/castle     │ Mage             │
// │ Grey Eyes                   │ Enter city/castle     │ Ashen            │
// │ The Fellow Cold             │ Enter city/castle     │ Ashen            │
// │ The Crowd Wants a Sign      │ Enter city/castle     │ Mage, Renown≥1000│
// │ A Soldier Dying             │ Enter city/castle     │ General          │
// │ The Child's Bead            │ Enter city/castle     │ General          │
// │ The Trade Council           │ Enter city/castle     │ General, Ren≥700 │
// │ An Old Enemy                │ Enter city/castle     │ General          │
// │ The Ember-Tithe             │ Enter village/city    │ Mage             │
// │ What the Keep Concealed     │ Siege (won)           │ Mage             │
// │ The Alchemist's Promise     │ Enter city/castle     │ General          │
// │ The Ember Shard             │ Enter village/city    │ General, no trinket│
// │ The Blind Eye               │ Enter village/city    │ General, no trinket│
// │ The Pale Compass            │ Enter village/city    │ General, no trinket│
// │ The Broken Seal             │ Enter city/castle     │ General            │
// └─────────────────────────────┴───────────────────────┴──────────────────┘
//
// Wiring (CampaignBehavior.cs):
//   OnDailyTick  → SettlementEncounters.DailyTick()
//   SyncData     → SettlementEncounters.Save(store)
//   OnNewGameCreated → SettlementEncounters.ResetForNewGame()
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
    public static class SettlementEncounters
    {
        // ── Tuning ────────────────────────────────────────────────────────────
        public const float EncounterChance       = 0.10f;  // per settlement transition (was 0.35)
        public const int   MinDaysBetween        = 7;      // shared cooldown between any encounter (was 3, then 6)
        public const float BattleEncounterChance = 0.14f;  // per field battle (was 0.35)
        public const float SiegeEncounterChance  = 0.22f;  // per siege (was 0.50)
        public const float RaidEncounterChance   = 0.22f;  // per raid (was 0.55)

        // ── State ─────────────────────────────────────────────────────────────
        private static int    _cooldown              = 0;
        private static string _lastSettlementId      = null; // last settlement entered (for leave detection)
        private static bool   _lastBattleWon         = false;
        private static bool   _lastBattleAsAttacker  = false;
        private static int    _childEventCooldown    = 0;   // long cooldown so child event fires rarely
        private static int    _bloodTitheCountdown   = 0;   // days until deferred blood-tithe consequence
        private static int    _babyEventCountdown    = 0;   // days until deferred illegitimate-child event
        private static int    _pregnancyCountdown    = 0;   // days until female-player pregnancy triggers
        private static int    _familyFeverCooldown   = 0;   // long cooldown for the family-plague event
        private static int    _ashenFrenzyCountdown  = 0;   // fires the day after player becomes Ashen
        private static int    _hedgeWitchCooldown    = 0;   // cooldown for the hedge-witch event
        private static int    _hedgeWitchCurse       = 0;   // days until witch-bargain sickness fires
        private static int    _ancientBookFound      = 0;   // 1 after the grimoire event fires (one-time)
        private static int    _trinketCountdown      = 0;   // days until next trinket-dream stage fires
        private static int    _trinketPhase          = 0;   // 0=inactive, 1=first dream, 2=recurring dream
        private static int    _trinketVariant        = 0;   // 1=ember shard, 2=blind eye, 3=pale compass
        private static int    _brokenSealCountdown  = 0;   // days until kingdom-plot consequence fires
        private static int    _brokenSealPlotType   = 0;   // 0=inactive, 1=war, 2=annexation, 3=sabotage
        private static bool   _brokenSealExtraWar   = false; // B also declares war when scouting-pass/charm-fail
        private static string _brokenSealKingdomAId = null; // aggressor kingdom StringId
        private static string _brokenSealKingdomBId = null; // target kingdom StringId
        private static int    _cinderVigilCooldown  = 0;   // cooldown between Cinder Vigil purge encounters
        private static int    _hopeMageConsequenceCountdown = 0;   // days until the youth's castle rebellion stirs
        private static string _hopeMageSettlementId = null;        // settlement where the youth was encountered
        private static bool   _memoryHungerConsumed  = false;      // player chose to "give in" to memory hunger
        private static int    _memoryHungerCountdown = 0;          // days until dissolution fires
        private static int    _poorKnightCooldown     = 0;         // long cooldown so knight event fires rarely
        private static int    _vengefulKnightCountdown = 0;        // days until the mocked knight strikes back
        private static bool   _lastBattleHadAshenEnemy = false;   // true when enemy side had Ashen parties (not persisted)
        private static int    _ashenMachineryCooldown  = 0;       // days between crystal-machine finds
        private static int    _ashenMachineryCountdown = 0;       // days until black-market weapon fires (option D)
        private static string _ashenMachineryKingdomId = null;    // kingdom targeted by deferred option D
        private static readonly Random _rng          = new Random();

        // ── Colours ───────────────────────────────────────────────────────────
        private static readonly Color FireColor  = new Color(0.90f, 0.60f, 0.20f);
        private static readonly Color GoodColor  = new Color(0.55f, 0.80f, 0.45f);
        private static readonly Color GoldColor  = new Color(0.90f, 0.78f, 0.25f);
        private static readonly Color DimColor   = new Color(0.65f, 0.60f, 0.52f);
        private static readonly Color DarkColor  = new Color(0.45f, 0.40f, 0.60f);
        private static readonly Color BadColor   = new Color(0.75f, 0.35f, 0.28f);
        private static readonly Color AshenColor = new Color(0.30f, 0.35f, 0.70f);

        // ── Public API ────────────────────────────────────────────────────────
        public static void ResetForNewGame()
        {
            _cooldown              = 0;
            _lastSettlementId      = null;
            _childEventCooldown    = 0;
            _bloodTitheCountdown   = 0;
            _babyEventCountdown    = 0;
            _pregnancyCountdown    = 0;
            _familyFeverCooldown   = 0;
            _ashenFrenzyCountdown  = 0;
            _hedgeWitchCooldown    = 0;
            _hedgeWitchCurse       = 0;
            _ancientBookFound      = 0;
            _trinketCountdown      = 0;
            _trinketPhase          = 0;
            _trinketVariant        = 0;
            _brokenSealCountdown   = 0;
            _brokenSealPlotType    = 0;
            _brokenSealExtraWar    = false;
            _brokenSealKingdomAId  = null;
            _brokenSealKingdomBId  = null;
            _cinderVigilCooldown          = 0;
            _hopeMageConsequenceCountdown = 0;
            _hopeMageSettlementId         = null;
            _memoryHungerConsumed         = false;
            _memoryHungerCountdown        = 0;
            _poorKnightCooldown           = 0;
            _vengefulKnightCountdown      = 0;
            _ashenMachineryCooldown       = 0;
            _ashenMachineryCountdown      = 0;
            _ashenMachineryKingdomId      = null;
        }

        public static void Save(IDataStore store)
        {
            store.SyncData("SE_Cooldown",           ref _cooldown);
            store.SyncData("SE_ChildEventCooldown", ref _childEventCooldown);
            store.SyncData("SE_BloodTithe",         ref _bloodTitheCountdown);
            store.SyncData("SE_BabyEvent",          ref _babyEventCountdown);
            store.SyncData("SE_Pregnancy",          ref _pregnancyCountdown);
            store.SyncData("SE_FamilyFever",        ref _familyFeverCooldown);
            store.SyncData("SE_AshenFrenzy",        ref _ashenFrenzyCountdown);
            store.SyncData("SE_HedgeWitchCD",       ref _hedgeWitchCooldown);
            store.SyncData("SE_HedgeWitchCurse",    ref _hedgeWitchCurse);
            store.SyncData("SE_AncientBook",        ref _ancientBookFound);
            store.SyncData("SE_TrinketCountdown",   ref _trinketCountdown);
            store.SyncData("SE_TrinketPhase",       ref _trinketPhase);
            store.SyncData("SE_TrinketVariant",     ref _trinketVariant);
            store.SyncData("SE_BrokenSealCD",       ref _brokenSealCountdown);
            store.SyncData("SE_BrokenSealPlot",     ref _brokenSealPlotType);
            store.SyncData("SE_BrokenSealExtraWar", ref _brokenSealExtraWar);
            store.SyncData("SE_BrokenSealKingA",    ref _brokenSealKingdomAId);
            store.SyncData("SE_BrokenSealKingB",    ref _brokenSealKingdomBId);
            store.SyncData("SE_CinderVigilCD",      ref _cinderVigilCooldown);
            store.SyncData("SE_HopeMageCD",         ref _hopeMageConsequenceCountdown);
            store.SyncData("SE_HopeMageSettlement", ref _hopeMageSettlementId);
            store.SyncData("SE_MemHungerConsumed",  ref _memoryHungerConsumed);
            store.SyncData("SE_MemHungerCD",        ref _memoryHungerCountdown);
            store.SyncData("SE_PoorKnightCD",       ref _poorKnightCooldown);
            store.SyncData("SE_VengefulKnightCD",   ref _vengefulKnightCountdown);
            store.SyncData("SE_AshenMachineCD",     ref _ashenMachineryCooldown);
            store.SyncData("SE_AshenMachineTimer",  ref _ashenMachineryCountdown);
            store.SyncData("SE_AshenMachineKing",   ref _ashenMachineryKingdomId);
        }

        /// Called from CampaignEvents.SettlementEntered — fires immediately when the
        /// player's party steps into any settlement.
        public static void OnPartyEnteredSettlement(MobileParty party, Settlement settlement)
        {
            if (party != MobileParty.MainParty || settlement == null) return;
            try
            {
                _lastSettlementId = settlement.StringId;
                if (_cooldown > 0) return;
                if (_rng.NextDouble() < EncounterChance)
                    TryFireEnter(settlement);
            }
            catch { }
        }

        /// Called from CampaignEvents.OnSettlementLeftEvent — fires immediately when the
        /// player's party leaves any settlement.
        public static void OnPartyLeftSettlement(MobileParty party, Settlement settlement)
        {
            if (party != MobileParty.MainParty || settlement == null) return;
            try
            {
                _lastSettlementId = null;
                if (_cooldown > 0) return;
                if (_rng.NextDouble() < EncounterChance)
                    TryFireLeave(settlement);
            }
            catch { }
        }

        /// Called from MagicCampaignBehavior.OnDailyTick — decrements cooldowns and fires deferred events.
        public static void DailyTick()
        {
            if (_cooldown > 0) _cooldown--;
            if (_childEventCooldown > 0) _childEventCooldown--;
            if (_familyFeverCooldown > 0) _familyFeverCooldown--;
            if (_hedgeWitchCooldown > 0) _hedgeWitchCooldown--;
            if (_cinderVigilCooldown > 0) _cinderVigilCooldown--;

            if (_hedgeWitchCurse > 0)
            {
                _hedgeWitchCurse--;
                if (_hedgeWitchCurse == 0)
                    FireHedgeWitchCurse();
            }

            if (_ashenFrenzyCountdown > 0)
            {
                _ashenFrenzyCountdown--;
                if (_ashenFrenzyCountdown == 0)
                    FireAshenFrenzy();
            }

            if (_bloodTitheCountdown > 0)
            {
                _bloodTitheCountdown--;
                if (_bloodTitheCountdown == 0)
                    FireBloodTitheConsequence();
            }

            if (_babyEventCountdown > 0)
            {
                _babyEventCountdown--;
                if (_babyEventCountdown == 0)
                    FireBabyConsequence();
            }

            if (_pregnancyCountdown > 0)
            {
                _pregnancyCountdown--;
                if (_pregnancyCountdown == 0)
                    FirePregnancyConsequence();
            }

            if (_trinketCountdown > 0)
            {
                _trinketCountdown--;
                if (_trinketCountdown == 0)
                    FireTrinketStage();
            }

            if (_brokenSealCountdown > 0)
            {
                _brokenSealCountdown--;
                if (_brokenSealCountdown == 0)
                    FireBrokenSealConsequence();
            }

            if (_hopeMageConsequenceCountdown > 0)
            {
                _hopeMageConsequenceCountdown--;
                if (_hopeMageConsequenceCountdown == 0)
                    FireHopeMageConsequence();
            }

            if (_memoryHungerConsumed && _memoryHungerCountdown > 0)
            {
                _memoryHungerCountdown--;
                if (_memoryHungerCountdown == 0)
                    FireMemoryHungerDissolution();
            }

            if (_poorKnightCooldown > 0) _poorKnightCooldown--;

            if (_vengefulKnightCountdown > 0)
            {
                _vengefulKnightCountdown--;
                if (_vengefulKnightCountdown == 0)
                    FireVengefulKnightConsequence();
            }

            if (_ashenMachineryCooldown > 0) _ashenMachineryCooldown--;

            if (_ashenMachineryCountdown > 0)
            {
                _ashenMachineryCountdown--;
                if (_ashenMachineryCountdown == 0)
                    FireAshenMachineryConsequence();
            }
        }

        /// Called from MagicCampaignBehavior.OnMapEventEnded.
        public static void OnMapEventEnded(MapEvent mapEvent)
        {
            if (_cooldown > 0 || mapEvent == null) return;
            try
            {
                bool playerAttacker = mapEvent.AttackerSide?.Parties
                    .Any(p => p.Party == PartyBase.MainParty) == true;
                bool playerDefender = mapEvent.DefenderSide?.Parties
                    .Any(p => p.Party == PartyBase.MainParty) == true;
                if (!playerAttacker && !playerDefender) return;

                _lastBattleWon = (playerAttacker && mapEvent.WinningSide == BattleSideEnum.Attacker)
                              || (playerDefender && mapEvent.WinningSide == BattleSideEnum.Defender);
                _lastBattleAsAttacker = playerAttacker;

                try
                {
                    MapEventSide enemySide = playerAttacker ? mapEvent.DefenderSide : mapEvent.AttackerSide;
                    _lastBattleHadAshenEnemy = enemySide?.Parties?.Any(p => {
                        string fId = p.Party?.MapFaction?.StringId ?? "";
                        return fId == "ashen_kingdom" ||
                               (p.Party?.LeaderHero != null && ColourLordRegistry.IsAshenLord(p.Party.LeaderHero));
                    }) == true;
                }
                catch { _lastBattleHadAshenEnemy = false; }

                switch (mapEvent.EventType)
                {
                    case MapEvent.BattleTypes.FieldBattle:
                        if (_rng.NextDouble() < BattleEncounterChance) TryFireBattle();
                        break;
                    case MapEvent.BattleTypes.Siege:
                        if (_rng.NextDouble() < SiegeEncounterChance)  TryFireSiege();
                        break;
                    case MapEvent.BattleTypes.Raid:
                        if (_rng.NextDouble() < RaidEncounterChance)   TryFireRaid();
                        break;
                }
            }
            catch { }
        }

        // ── Dispatch ──────────────────────────────────────────────────────────
        private static void TryFireEnter(Settlement s)
        {
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
                pool.Add(EV_DarknessSpreads);
                pool.Add(EV_BurningWitch);
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
                    if (!ashen) pool.Add(EV7_SelfTaughtMage);
                    pool.Add(EV8_TheLie);
                    if (!ashen && ren >= 600f) pool.Add(EV7_OldMastersStudent);
                    if (!ashen) pool.Add(E_EmberTithe);
                    if (_childEventCooldown == 0 && HasEligibleChild()) pool.Add(E_DarkeningInheritance);
                }
                if (ashen) pool.Add(EV2_DogWontStop);
                if (ashen) pool.Add(EV_MemoryHunger);
                if (_cinderEligible) pool.Add(EC_CinderVigil);
            }
            if (town)
            {
                pool.Add(E_OldEnemy);
                if (_familyFeverCooldown == 0 && HasSpouseAndChild()) pool.Add(E_TheWasting);
                if (_hedgeWitchCooldown == 0 && HasHedgeWitchCondition()) pool.Add(E_NightVisitor);
                if (ren >= 250f) pool.Add(EC8_MerchantLedger);
                if (ren >= 300f) pool.Add(EC8_ReluctantOfficial);
                if (!ashen) pool.Add(EC_LocalPriest);
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
                    if (!ashen) pool.Add(E_EmberTithe);
                    if (_childEventCooldown == 0 && HasEligibleChild()) pool.Add(E_DarkeningInheritance);
                }
                pool.Add(EC9_AshenElixir);
                if (ashen) pool.Add(EV_MemoryHunger);
                if (_cinderEligible) pool.Add(EC_CinderVigil);
                // Poor knight wants to prove himself in tournament (or just encountered by chance)
                if (!ashen && _poorKnightCooldown == 0) pool.Add(EC_PoorKnight);
                // Tavern harassment — clan tier < 5, not Ashen
                if (!ashen && clanTier < 5) pool.Add(EC_TavernHarassment);
            }

            Fire(pool, s);
        }

        private static void TryFireLeave(Settlement s)
        {
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
            }
            if (town)
            {
                if (!ashen && ren >= 300f) pool.Add(E_BardsRequest);
                if (mage)
                {
                    pool.Add(LC_BloodCollector);
                }
                if (ashen) pool.Add(LC4_RecognizedByAshen);
                // Encounter: Hope — young mage afraid of Ashen; clan tier ≥ 2, non-Ashen mage
                int clanTier = Hero.MainHero?.Clan?.Tier ?? 0;
                if (mage && !ashen && clanTier >= 2) pool.Add(LC_YoungMageHope);
                pool.Add(EL_InsultAtGate);
            }

            Fire(pool, s);
        }

        private static void Fire(List<Action<Settlement>> pool, Settlement s)
        {
            if (pool.Count == 0) return;
            _cooldown = MinDaysBetween;
            Action<Settlement> chosen = pool[_rng.Next(pool.Count)];
            MageKnowledge._deferredInquiry = () => { try { chosen(s); } catch { } };
        }

        private static void FireBattle(List<Action> pool)
        {
            if (pool.Count == 0) return;
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

            pool.Add(EB8_FieldTriage);

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
            bool mage = MageKnowledge.IsMage;
            var pool  = new List<Action>();

            pool.Add(ER3_CellarSurvivors);

            FireBattle(pool);
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static void Msg(string text, Color c)
        {
            try { MBInformationManager.AddQuickInformation(new TextObject(text)); }
            catch { try { InformationManager.DisplayMessage(new InformationMessage(text, c)); } catch { } }
        }

        private static void AddMorale(float delta)
        {
            try { if (MobileParty.MainParty != null) MobileParty.MainParty.RecentEventsMorale += delta; } catch { }
        }

        private static void AgePlayer(int days)
        {
            try { AgingSystem.AgeHero(Hero.MainHero, days); } catch { }
        }

        private static void ShiftTrait(TraitObject trait, int delta)
        {
            try
            {
                Hero h = Hero.MainHero;
                if (h == null) return;
                int v = h.GetTraitLevel(trait);
                h.SetTraitLevel(trait, Math.Min(2, Math.Max(-2, v + delta)));
                string sign = delta >= 0 ? "+" : "";
                Msg($"({trait.Name} {sign}{delta})", delta >= 0 ? GoodColor : DimColor);
            }
            catch { }
        }

        private static bool ChangeGold(int amount)
        {
            if (amount < 0 && (Hero.MainHero?.Gold ?? 0) < -amount)
            {
                Msg($"Not enough gold. (Need {-amount}, have {Hero.MainHero?.Gold ?? 0})", BadColor);
                return false;
            }
            try { Hero.MainHero?.ChangeHeroGold(amount); } catch { }
            return true;
        }

        private static void ChangeRenown(float amount)
        {
            try
            {
                if (Hero.MainHero?.Clan != null)
                {
                    Hero.MainHero.Clan.Renown = Math.Max(0f, Hero.MainHero.Clan.Renown + amount);
                    string sign = amount >= 0 ? "+" : "";
                    Msg($"({sign}{amount:F0} renown)", GoodColor);
                }
            }
            catch { }
        }

        private static void ChangeCrime(float amount)
        {
            try
            {
                var kingdom = Hero.MainHero?.MapFaction as Kingdom;
                if (kingdom != null)
                    ChangeCrimeRatingAction.Apply(kingdom, amount, true);
            }
            catch { }
        }

        private static void ChangeRelWithOwner(Settlement s, int delta)
        {
            try
            {
                Hero owner = s.OwnerClan?.Leader;
                if (owner != null && owner != Hero.MainHero)
                {
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, owner, delta, false);
                    string sign = delta >= 0 ? "+" : "";
                    Msg($"(Relation with {owner.Name}: {sign}{delta})", delta >= 0 ? GoodColor : BadColor);
                }
            }
            catch { }
        }

        private static void ChangeRelWithRandomLord(int delta)
        {
            try
            {
                var lords = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && h != Hero.MainHero && !h.IsPrisoner)
                    .ToList();
                if (lords.Count == 0) return;
                var lord = lords[_rng.Next(lords.Count)];
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, delta, false);
                string sign = delta >= 0 ? "+" : "";
                Msg($"(Relation with {lord.Name}: {sign}{delta})", delta >= 0 ? GoodColor : BadColor);
            }
            catch { }
        }

        private static string GoldStr(int amount)
            => amount >= 0 ? $"+{amount} gold" : $"{amount} gold";

        // ── Skill-check helpers ───────────────────────────────────────────────
        // Soft roll: base success chance + (skillLevel × perPoint), capped.
        // baseChance 0.25 + perPoint 0.003 means:
        //   skill  50 → 40 %    skill 100 → 55 %
        //   skill 150 → 70 %    skill 200 → 85 %   skill 250+ → 90 % cap
        private static int GetSkill(SkillObject skill)
        {
            try { return Hero.MainHero?.GetSkillValue(skill) ?? 0; }
            catch { return 0; }
        }

        private static float SkillChance(SkillObject skill, float baseChance,
                                          float perPoint = 0.003f, float cap = 0.90f)
        {
            int level = GetSkill(skill);
            return Math.Min(cap, baseChance + level * perPoint);
        }

        private static bool SkillRoll(SkillObject skill, float baseChance,
                                       float perPoint = 0.003f, float cap = 0.90f)
            => _rng.NextDouble() < SkillChance(skill, baseChance, perPoint, cap);

        // Returns a one-word likelihood label used in hint text.
        private static string OddsLabel(float chance)
        {
            if (chance >= 0.85f) return "very likely";
            if (chance >= 0.68f) return "likely";
            if (chance >= 0.48f) return "even odds";
            if (chance >= 0.32f) return "unlikely";
            return "a long shot";
        }

        // Builds a hint string for a skill-check choice, shown as a tooltip.
        // e.g. "[Charm 84] Persuasion attempt — likely (59%)."
        private static string SkillHint(SkillObject skill, float baseChance, string outcomeLabel)
        {
            int level  = GetSkill(skill);
            float pct  = SkillChance(skill, baseChance) * 100f;
            return $"[{skill.Name} {level}] {outcomeLabel} — {OddsLabel(SkillChance(skill, baseChance))} ({(int)pct}%).";
        }

        // ═════════════════════════════════════════════════════════════════════
        // LEAVE VILLAGE — MAGE
        // ═════════════════════════════════════════════════════════════════════

        // 1. A Mother's Plea
        private static void E_MothersPlea(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  A Mother's Plea",
                "A woman in rough-spun wool steps into your path as you ride out. She carries a small child — you can see at a glance it is burning with fever. She has heard what you carry inside you. She weeps and offers nothing but her prayers.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Extend the inner fire to the child.", null, true,
                        "The fire can be given. It is not without cost."),
                    new InquiryElement("b", "Refuse. The road pulls at you.", null, true,
                        "The road continues."),
                    new InquiryElement("c", "Press coins into her hands — see a healer.", null, true,
                        "Coin may do what fire cannot. Something shifts."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgePlayer(1);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You press your palm to the child's brow. The fever breaks. The mother cannot speak for weeping.", GoodColor);
                            break;
                        case "b":
                            Msg("You cannot be the answer to every prayer on every road. You ride on.", DimColor);
                            break;
                        case "c":
                            ChangeGold(-200);
                            ShiftTrait(DefaultTraits.Generosity, 1);
                            Msg("The coins may save the child if a healer is near. You do not look back.", GoldColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═════════════════════════════════════════════════════════════════════
        // ENTER VILLAGE — MAGE
        // ═════════════════════════════════════════════════════════════════════

        // 11. The Old Flame-Seer
        private static void E_OldFlameSeer(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Old Flame-Seer",
                "An old man sits outside the inn, eyes clouded white. He does not look at you. He faces toward you. \"I can smell the fire from here,\" he says. \"Not the campfire kind. The old kind.\" He taps the bench beside him.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Sit with him. You have questions too.", null, true,
                        "A day at the edge of something old. What you leave with may surprise you."),
                    new InquiryElement("b", "Ask what he sees in you.", null, true,
                        "He sees something. So might you."),
                    new InquiryElement("c", "Keep walking. Old men with milky eyes say many things.", null, true,
                        "The road continues."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgePlayer(1);
                            if (TalentSystem.PurchasedCount < 6)
                            {
                                var available = TalentSystem.All
                                    .Where(t => t.Id != TalentId.Gift && !TalentSystem.Has(t.Id))
                                    .ToList();
                                if (available.Count > 0)
                                {
                                    var pick = available[_rng.Next(available.Count)];
                                    TalentSystem.GrantFree(pick.Id, Hero.MainHero);
                                    Msg($"You sit with him for an hour. He speaks around the edges of things you were already reaching toward. By the time the village lanterns are lit, something has opened in you — the shape of {pick.Name}, given without ceremony.", FireColor);
                                }
                                else
                                {
                                    Msg("You sit with him for an hour. He tells you what he knows. It is less than you hoped and more than you can use right now.", FireColor);
                                }
                            }
                            else
                            {
                                ChangeRenown(8f);
                                Msg("You sit with him for an hour. He tells you things about fire that you already knew but had not named — the difference is smaller than it used to be. He senses it. Before you leave he says: 'You have gone further than I can follow.' He does not mean it as a compliment.", FireColor);
                            }
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("\"A fire that eats its own wood,\" he says. \"Burning slow. Burning long.\" He does not explain further.", FireColor);
                            break;
                        case "c":
                            Msg("You walk past. He keeps facing the direction you were standing.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═════════════════════════════════════════════════════════════════════
        // LEAVE CITY/CASTLE — GENERAL
        // ═════════════════════════════════════════════════════════════════════

        // 26. The Bard's Request
        private static void E_BardsRequest(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Bard's Request",
                "A young bard with ink on his collar catches you at the city gate. He wants to write a song about you. He has heard enough already — the fire, the battles, the years. He only needs you to confirm the shape of it.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Tell it honestly. He can do what he likes with the truth.", null, true,
                        "Songs travel. Your name with them."),
                    new InquiryElement("b", "Decline modestly. Your story is not finished yet.", null, true,
                        "Restraint has its own reputation."),
                    new InquiryElement("c", "Embellish freely. Songs should be worth singing.", null, true,
                        "A better story than the truth. The song will travel farther."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeRenown(15f);
                            Msg("You give him an hour of honest account. He stops you twice to make notes. The song he writes turns out better than the truth deserves.", GoodColor);
                            break;
                        case "b":
                            ChangeRenown(5f);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("\"Not yet,\" you tell him. He seems to find this more interesting than a full account. He writes it down anyway.", DimColor);
                            break;
                        case "c":
                            ChangeRenown(20f);
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("You give him a story worth a song — most of it true, the rest better than true. He looks satisfied. So does the version of yourself you described.", GoldColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═════════════════════════════════════════════════════════════════════
        // ENTER CITY/CASTLE — MAGE
        // ═════════════════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 63–67 — ENTER CITY (new)
        // ═══════════════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 68–72 — LEAVE CITY (new)
        // ═══════════════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 73–77 — ENTER VILLAGE (new)
        // ═══════════════════════════════════════════════════════════════════

        // 77. The Dog (Ashen-gated)
        private static void EV2_DogWontStop(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✿  The Dog",
                "A farm dog has been barking at you since your party entered the village. Just at you. Not at your horse, not at your men. At you specifically, with the rigid-legged certainty of an animal that knows something is wrong.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Try to approach and calm it.", null, true,
                        "You approach. It does not stop, but you tried."),
                    new InquiryElement("b", "Ignore it.", null, true,
                        "The barking follows you to the village edge."),
                    new InquiryElement("c", "Offer it meat from your pack.", null, true,
                        "Meat silences it. Uncertainty does not."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You approach slowly. It backs up, still barking, stays beyond your reach. It is not afraid — it simply does not want to be near what you have become. Animals remember things that people learn to ignore.", AshenColor);
                            break;
                        case "b":
                            Msg("The barking follows you to the other end of the village and stops exactly when you leave. The villagers pretend not to notice. They noticed.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You toss the meat gently. It catches it. Stops barking. Then sits and watches you for the rest of your time in the village, eating with its eyes. That is not entirely better.", AshenColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 87–89 — ENTER CITY (new batch)
        // ═══════════════════════════════════════════════════════════════════

        // 103. What She Sees (mage-gated)
        private static void EV4_GiftedChild(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  What She Sees",
                "A girl of perhaps six stops playing and stares at you. Not at your horse, not at your armor — at you. She reaches toward something she cannot name, cannot see, but clearly senses. Her mother pulls her back. The girl's eyes do not leave yours.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Crouch down and say a quiet word to her.", null, true,
                        "Something passes between you. She will remember."),
                    new InquiryElement("b", "Meet the mother's eyes and give a small nod.", null, true,
                        "The mother sees you see it. She will watch more carefully."),
                    new InquiryElement("c", "Ride on. This is not yours to name.", null, true,
                        "You ride on. The fire turns back, briefly."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You crouch down. She does not flinch. You say nothing useful — there are no words yet that would help her — but you let a thread of warmth pass between you, very small, so she knows what it is she is feeling. She smiles. The mother stands frozen. You ride on.", FireColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("The mother sees you see it. You nod once. She does not know what the nod means, but she will think about it later, and later still, and eventually she will start watching her daughter's hands near candles.", FireColor);
                            break;
                        case "c":
                            Msg("You ride past. Behind you, the girl is still facing the direction you were. The fire in you turns back once, briefly, the way it does when it recognises its own.", FireColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 109–111 — ENTER CITY (fourth batch)
        // ═══════════════════════════════════════════════════════════════════

        // 114. The Watching Figure (Ashen-gated)
        private static void LC4_RecognizedByAshen(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Watching Figure",
                "Leaving the city, you become aware — not by sight but by the particular absence of warmth — of someone in a building's shadow cataloguing your party. Grey cloak, pale still face, the patient posture of something that is not in a hurry because it has learned not to be. An Ashen agent is noting your movements.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Confront them directly.", null, true,
                        "They will be gone before you reach them. They know you saw."),
                    new InquiryElement("b", "Pretend not to notice and ride on.", null, true,
                        "They will follow for a time. What they report is out of your hands."),
                    new InquiryElement("c", "Have a message passed: you are not their enemy.", null, true,
                        "You reach out. Whether anything comes back is uncertain."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You turn your horse and ride toward the shadow. They are gone before you reach the doorway — not fled, simply gone, the way the Ashen go when they choose not to be found. The shadow is cold in a way that has nothing to do with the hour. They know you saw them. That may be enough.", AshenColor);
                            break;
                        case "b":
                            Msg("You ride on. They follow at a distance you would not see if you didn't know what to look for. After a mile they stop. Their report will note your route, your party's strength, and that you did not react. The last detail is the one that will be read most carefully.", AshenColor);
                            break;
                        case "c":
                            if (_rng.Next(2) == 0)
                            {
                                ChangeRelWithRandomLord(5);
                                Msg("A rider carries the message back into the city. Nothing happens for an hour. Then, near the road's first bend, something small and cold is placed in your saddlebag. A piece of grey cloth. An acknowledgement. The cold's own currency.", AshenColor);
                            }
                            else
                                Msg("The message goes in. Nothing comes back. Either it was not received, or received and set aside, or received and filed under 'noted.' The Ashen are not hurried correspondents.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // NEW ENCOUNTERS — ENTER CITY (general)
        // ═══════════════════════════════════════════════════════════════════

        // EC_PoorKnight — Young knight in worn gear wants to prove himself [Leadership]
        private static void EC_PoorKnight(Settlement s)
        {
            _poorKnightCooldown = 60; // long cooldown so this doesn't repeat constantly
            bool hasTournament = false;
            try { hasTournament = s.Town?.HasTournament ?? false; } catch { }
            string context = hasTournament
                ? "The tournament yard is buzzing with preparations when you spot him"
                : "You notice him near the training ground";

            float leadershipChance = SkillChance(DefaultSkills.Leadership, 0.38f);
            string coachHint = SkillHint(DefaultSkills.Leadership, 0.38f, "Steady him — teach him what he is missing");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  The Knight Without Fortune",
                $"{context}: a young man in dented, mismatched armour polishing a blade that has seen better decades. " +
                "The other knights are laughing at him from across the yard. He does not look at them. " +
                "He looks at the gate and polishes the blade anyway. There is something in his eyes that has nothing to do with the armour.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Ride past. It is not your concern.", null, true,
                        "The yard and its laughter fall behind you."),
                    new InquiryElement("b", "Outfit him — armour, horse, the works.", null, true,
                        $"−2000 coin. +1 Generous. {coachHint}"),
                    new InquiryElement("c", "Join the laughter.", null, true,
                        "He hears you. Everyone hears you."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            MageKnowledge._deferredInquiry = () =>
                                Msg("Word drifts back to you two days later: he entered, lost in the second round, and rode out without speaking to anyone. Nobody noticed.", DimColor);
                            break;
                        case "b":
                            if (!ChangeGold(-2000)) { Msg("You do not have enough coin.", BadColor); _poorKnightCooldown = 0; break; }
                            ShiftTrait(DefaultTraits.Generosity, 1);
                            if (SkillRoll(DefaultSkills.Leadership, 0.38f))
                            {
                                // B: he wins
                                var cataphract = MBObjectManager.Instance.GetObject<CharacterObject>("imperial_elite_cataphract")
                                             ?? MBObjectManager.Instance.GetObject<CharacterObject>("empire_cataphract");
                                if (cataphract != null)
                                    try { MobileParty.MainParty.MemberRoster.AddToCounts(cataphract, 1); } catch { }
                                ChangeRenown(10f);
                                Msg("Against every expectation in that yard, he wins. Not through luck — through something prepared and stubborn. " +
                                    "He comes to you afterward, the new armour bloodied and proving its worth, and asks to ride with you. " +
                                    "You have gained a seasoned Imperial Cataphract — and a knight who will remember exactly who gave him the chance.", GoodColor);
                            }
                            else
                            {
                                // C: he dies happy
                                AddMorale(-4f);
                                Msg("He rides in well-equipped and hopeful and is unhorsed in the third bout by a man twice his experience. " +
                                    "The fall breaks something that should not be broken. He is carried off the field. " +
                                    "They say he was smiling when they reached him. He had the armour. He had the horse. He had his chance. " +
                                    "He went in having been taken seriously by someone.", DimColor);
                            }
                            break;
                        case "c":
                            // D: he swears revenge
                            ShiftTrait(DefaultTraits.Honor, -1);
                            ShiftTrait(DefaultTraits.Mercy, -1);
                            _vengefulKnightCountdown = 7 + _rng.Next(5);
                            Msg("He looks up when he hears you. He finds your face. His jaw tightens and he goes back to polishing. " +
                                "He enters the tournament angry, and the anger carries him further than expected — three rounds, maybe four. " +
                                "Then it runs out. He loses. Before he rides out he looks back at you and says one sentence: " +
                                "\"I will remember who laughed.\"", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // EC_TavernHarassment — Noble groping a serving girl; clan tier < 5 [Athletics on option 3]
        private static void EC_TavernHarassment(Settlement s)
        {
            float athleticsChance = SkillChance(DefaultSkills.Athletics, 0.40f);
            string fightHint = SkillHint(DefaultSkills.Athletics, 0.40f, "Back him down before it becomes a brawl");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Tavern",
                "The common room is loud enough. Not loud enough to cover it. A minor lord with two men-at-arms at his back has his hand on the arm of a serving girl who is clearly not interested. " +
                "The tavernkeeper starts toward them — the lord's man steps in front and tells him to sit. The tavernkeeper sits. " +
                "Everyone in the room pretends not to notice.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Drink your beer. It is not your business.", null, true,
                        "It is not your business. You are very certain of this."),
                    new InquiryElement("b", "Laugh with the noble — make a friend.", null, true,
                        "You know how this goes. So does he."),
                    new InquiryElement("c", "Tell him to get off her.", null, true, fightHint),
                    new InquiryElement("d", "Send for the city guard.", null, true,
                        "The guard will come. Whether that helps anyone is a different question."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            // A: -1 honour, -1 mercy
                            ShiftTrait(DefaultTraits.Honor, -1);
                            ShiftTrait(DefaultTraits.Mercy, -1);
                            Msg("You drink your beer. Across the room the lord takes the girl behind the counter. " +
                                "Later, when he comes back out, he and his men are laughing about something. " +
                                "She is crying in the scullery. You paid for the beer and left. " +
                                "Nobody in the room looked at anyone else on the way out.", BadColor);
                            break;
                        case "b":
                            // B: -1 mercy, +5 rel with city lord
                            ShiftTrait(DefaultTraits.Mercy, -1);
                            ChangeRelWithOwner(s, 5);
                            Msg("You make a joke. He laughs. His men laugh. He sends you a jug of the good wine from behind the bar — " +
                                "the tavernkeeper's own, you notice — and raises it across the room. " +
                                "You have made a friend. You are both exactly the kind of people this room deserved tonight.", BadColor);
                            break;
                        case "c":
                            // C success: +1 honour +10 renown -10 rel. D fail: +1 honour -20 rel +20 crime
                            ShiftTrait(DefaultTraits.Honor, 1);
                            if (SkillRoll(DefaultSkills.Athletics, 0.40f))
                            {
                                ChangeRenown(10f);
                                ChangeRelWithOwner(s, -10);
                                Msg("You stand up. The room goes quiet with the specific quality of rooms that have been waiting for someone to stand up. " +
                                    "The lord reads your face and reads the room and lets go. He calls you a few things on his way out that confirm your assessment of him. " +
                                    "The girl is gone by the time the door closes behind him. The tavernkeeper sets the good wine in front of you without being asked. " +
                                    "The room breathes again.", GoodColor);
                            }
                            else
                            {
                                ChangeRelWithOwner(s, -20);
                                var kingdom = s.MapFaction as TaleWorlds.CampaignSystem.Kingdom;
                                if (kingdom != null) try { ChangeCrimeRatingAction.Apply(kingdom, 20f, false); } catch { }
                                Msg("You stand up. So do his men. What follows is not the clean confrontation you intended — " +
                                    "it is a tavern brawl with a lord's hired muscle, and you come out of it reported to the guard for disturbing the peace. " +
                                    "On the positive side: the girl got out during the noise. " +
                                    "The lord filed a formal complaint. Your crime rating in this kingdom just went up by twenty.", BadColor);
                            }
                            break;
                        case "d":
                            // E: guards side with noble, crime +10, rel -5
                            ChangeRelWithOwner(s, -5);
                            {
                                var kingdom = s.MapFaction as TaleWorlds.CampaignSystem.Kingdom;
                                if (kingdom != null) try { ChangeCrimeRatingAction.Apply(kingdom, 10f, false); } catch { }
                            }
                            Msg("The guard arrives before long. They apologise to the lord for the interruption. " +
                                "They ask you to come with them for questioning about what you saw and why you sent for them. " +
                                "The lord's men are still in the tavern. The girl is still in the tavern. " +
                                "You can only imagine what happens next. " +
                                "Your crime rating in this kingdom went up by ten.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── Deferred: FireVengefulKnightConsequence — 7–11 days after EC_PoorKnight option C ──
        private static void FireVengefulKnightConsequence()
        {
            if (MageKnowledge._deferredInquiry != null) { _vengefulKnightCountdown = 1; return; }
            ChangeRelWithRandomLord(-8);
            ChangeRenown(-5f);
            MageKnowledge._deferredInquiry = () =>
                Msg("Word finds you on the road: the knight you laughed at in the tournament has been telling anyone who will listen. " +
                    "Not about the loss — about who laughed, and why, and what that means about you. " +
                    "A lord who heard him agrees. Your reputation has taken a small but specific hit.", BadColor);
        }

        // ═══════════════════════════════════════════════════════════════════
        // NEW ENCOUNTERS — LEAVE CITY (mage, non-Ashen, clan tier ≥ 2)
        // ═══════════════════════════════════════════════════════════════════

        // LC_YoungMageHope — A young mage afraid of the Ashen asks about hope [Leadership]
        private static void LC_YoungMageHope(Settlement s)
        {
            float leadershipChance = SkillChance(DefaultSkills.Leadership, 0.35f);
            string hint = SkillHint(DefaultSkills.Leadership, 0.35f, "Steady them — give them something to hold");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  There Is Always Hope",
                "A young man waits at the city gate with the stiff posture of someone who practiced what they would say and forgot it anyway. " +
                "He can feel the fire in you from here. His own gift is new — two years, maybe three. " +
                "He has heard what the Ashen do to people like him. He wants to know if it has to end that way.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "\"There is always hope. If you know what you are, you can choose what you become.\"", null, true, hint),
                    new InquiryElement("b", "Ignore him. You have somewhere to be.", null, true,
                        "He watches you ride out. The gate closes."),
                    new InquiryElement("c", "\"Run. Leave this city and don't come back.\"", null, true, hint),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Leadership, 0.35f))
                            {
                                ShiftTrait(DefaultTraits.Honor, 1);
                                ChangeRenown(5f);
                                Msg("He listens to you — not to the words, but to the way you say them. Something settles in him, then hardens. " +
                                    "A week later, word reaches you from the north: a young mage was seen leading a small band of volunteers against an Ashen raiding column. " +
                                    "They held the village road. Against expectation, they held it. " +
                                    "His name is already travelling faster than he is. He inspired them not by being powerful — by being certain.", GoodColor);
                                // Deferred consequence: castle town stirs — rebellion chance in ~14 days
                                _hopeMageConsequenceCountdown = 14;
                                _hopeMageSettlementId = s.StringId;
                            }
                            else
                            {
                                Msg("You give him the words but not the weight — the right speech without the certainty behind it. " +
                                    "He nods and thanks you and you can see he is no more certain than before. " +
                                    "He will make a choice based on fear, not on what you said. You do not know what that choice will be.", DimColor);
                                // Youth defects to Ashen — no mechanical consequence, narrative only
                                Msg("Three days later, the city guard reports a young man was seen leaving north " +
                                    "toward the grey hills. He took nothing but his coat.", BadColor);
                            }
                            break;
                        case "b":
                            Msg("He watches you ride out. His question travels with you farther than it should. " +
                                "You had an answer. You chose not to spend it.", DimColor);
                            break;
                        case "c":
                            if (SkillRoll(DefaultSkills.Leadership, 0.35f))
                            {
                                ShiftTrait(DefaultTraits.Honor, 1);
                                ChangeRenown(5f);
                                Msg("He takes the warning seriously — the directness of it lands where the gentler version wouldn't. " +
                                    "He is gone from the city by nightfall. You will not hear from him again, " +
                                    "but something in how he moved when you spoke told you he understood exactly what 'run' meant.", GoodColor);
                                // No city rebellion — he simply escaped; that is the whole of this outcome
                            }
                            else
                            {
                                Msg("He hears the urgency but not the reason. He runs — but without direction, without a plan, " +
                                    "toward the grey hills rather than away from them. " +
                                    "Your warning sent him exactly where you were warning him away from.", BadColor);
                                Msg("A week later, the Ashen gain a recruit.", BadColor);
                                // No city rebellion — he went to the wrong side
                            }
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // NEW ENCOUNTERS — AFTER BATTLE (clan tier ≥ 3, lost battle)
        // ═══════════════════════════════════════════════════════════════════

        // EB_HeroInspired — Fleeing refugees after a lost battle; hard choices
        private static void EB_HeroInspired()
        {
            float ridingChance  = SkillChance(DefaultSkills.Riding, 0.38f);
            float combatChance  = SkillChance(DefaultSkills.OneHanded, 0.35f);
            string ridingHint   = SkillHint(DefaultSkills.Riding, 0.38f, "Get the children out before the pursuit catches you");
            string combatHint   = SkillHint(DefaultSkills.OneHanded, 0.35f, "Hold them long enough for the others to clear the road");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  What the Road Holds",
                "Retreating, you overtake a knot of villagers fleeing the same way — two dozen people with what they could carry. " +
                "Children. A cart with one wheel about to fail. Soldiers from the other side are on the road behind you and closing. " +
                "These people will not outrun them.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Use them to slow the pursuit — they are not your concern.", null, true,
                        "The pursuit slows. You clear the road. The villagers do not."),
                    new InquiryElement("b", "Get the children on horseback and ride hard.", null, true, ridingHint),
                    new InquiryElement("c", "Turn and hold the pursuit long enough for them to clear the road.", null, true, combatHint),
                    new InquiryElement("d", "Ride on. You cannot fight a retreat and a rescue.", null, true,
                        "You ride on. The sound behind you is what it is."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            ShiftTrait(DefaultTraits.Mercy, -1);
                            Msg("The villagers scatter into the fields as the soldiers push through. " +
                                "Not all of them make it to the treeline. Your retreat clears. " +
                                "Your men do not look at each other.", BadColor);
                            break;
                        case "b":
                            if (SkillRoll(DefaultSkills.Riding, 0.38f))
                            {
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                Msg("You load what you can onto your horses and ride hard. The pursuit reaches the abandoned cart and slows. " +
                                    "Enough distance opens. You clear the road with seven children who will grow up knowing exactly what happened at that junction.", GoodColor);
                            }
                            else
                            {
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                WoundMainHero(2);
                                Msg("You get the children mounted but the pursuit is faster than the road allows. " +
                                    "You take two wounds getting the last group clear — an arrow in the shoulder, then a blade across your arm at the horse's flank. " +
                                    "You clear the road. Some of the children arrive before you do, and have to wait.", GoodColor);
                            }
                            break;
                        case "c":
                            if (SkillRoll(DefaultSkills.OneHanded, 0.35f))
                            {
                                ShiftTrait(DefaultTraits.Honor, 1);
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                ChangeRenown(15f);
                                Msg("You turn and hold the road alone long enough for the villagers to reach the treeline. " +
                                    "The pursuit pulls back when they realise what they are dealing with. " +
                                    "The story of the lord who turned on a routed road travels faster than you do.", GoodColor);
                            }
                            else
                            {
                                // Permadeath risk — player is wounded seriously
                                ShiftTrait(DefaultTraits.Honor, 1);
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                ChangeRenown(100f);
                                WoundMainHero(5);
                                Msg("You hold them. You hold them longer than anyone watching expected. " +
                                    "The villagers clear the road. You do not clear the road afterward with the same ease. " +
                                    "The story of what you did at that junction will outlast the battle that preceded it.", GoodColor);
                                // Critical wound — check if fatal
                                if (Hero.MainHero != null && Hero.MainHero.IsWounded)
                                    MBInformationManager.AddQuickInformation(new TextObject(
                                        "You are badly wounded. The surgeons are doing what they can."));
                            }
                            break;
                        case "d":
                            Msg("You ride on. The sound behind you is what it is. Your men ride with you " +
                                "and do not say anything, which is its own kind of verdict.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        private static void WoundMainHero(int count)
        {
            try
            {
                var roster = MobileParty.MainParty?.MemberRoster;
                if (roster == null || Hero.MainHero == null) return;
                // Wound the hero directly by marking them wounded (simulated as party morale loss + hero wound)
                Hero.MainHero.HitPoints = Math.Max(1, Hero.MainHero.HitPoints - count * 10);
            }
            catch { }
        }

        // ═══════════════════════════════════════════════════════════════════
        // NEW ENCOUNTERS — ENTER VILLAGE/TOWN (Ashen only, random)
        // ═══════════════════════════════════════════════════════════════════

        // EV_MemoryHunger — Ashen only; fading memories; potentially fatal choice
        private static void EV_MemoryHunger(Settlement s)
        {
            float leadershipChance = SkillChance(DefaultSkills.Leadership, 0.30f);
            string hint = SkillHint(DefaultSkills.Leadership, 0.30f, "Resist the hunger — hold what remains");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Memory-Hunger",
                "It arrives without announcement: a sudden and absolute certainty that something you once knew — " +
                "the texture of a particular moment, a conversation, a face you loved — is no longer retrievable. " +
                "The cold in you is not mourning it. It is hungry. " +
                "It wants to finish what it started.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Try to remember — force the memory back from the edge.", null, true, hint),
                    new InquiryElement("b", "Give in — let it take what it wants.", null, true,
                        "What you give it, it will use. You will not be the same after. " +
                        "You may not survive the exchange at all."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Leadership, 0.30f))
                            {
                                Msg("You hold it. Not the memory — the memory is gone and does not return — but yourself. " +
                                    "The hunger reaches a limit and stops. You are in a village street " +
                                    "and you are still you, with one fewer thing that was yours. " +
                                    "The cold continues at the same pace. It is patient.", AshenColor);
                                ShiftTrait(DefaultTraits.Mercy, -1);
                            }
                            else
                            {
                                Msg("You reach for it and find the reaching itself has been consumed. " +
                                    "When you surface you are standing in the street and you do not know how long you have been standing. " +
                                    "Your men are watching you from a careful distance. " +
                                    "In the hours you cannot account for, money changed hands — debts settled with people you cannot now name, " +
                                    "coin pressed into fists for reasons that made complete sense to whoever was doing it. " +
                                    "Something has passed through you and the thing that came out the other side is quieter than what went in.", BadColor);
                                ShiftTrait(DefaultTraits.Mercy, -1);
                                ShiftTrait(DefaultTraits.Generosity, -1);
                                ChangeGold(-500);
                            }
                            break;
                        case "b":
                            // Give in: big XP gain but 7-day death timer
                            try { Hero.MainHero.HeroDeveloper.UnspentFocusPoints += 2; } catch { }
                            ShiftTrait(DefaultTraits.Mercy, -2);
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("You open the door. Something floods through you — cold, complete, and enormously purposeful. " +
                                "You understand things about the fire that you did not understand before. " +
                                "You cannot remember what you gave it. " +
                                "Your men back away from you without being told to.", AshenColor);
                            MBInformationManager.AddQuickInformation(new TextObject(
                                "The cold is consuming you. Something fundamental is leaving. You have perhaps seven days."));
                            // Schedule a deferred "dissolution" event — player receives warning; after 7 days, game over
                            _memoryHungerConsumed     = true;
                            _memoryHungerCountdown    = 7;
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 118–119 — AFTER SIEGE (fourth batch)
        // ═══════════════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════════════
        // EVENT 120 — AFTER RAID (fourth batch)
        // ═══════════════════════════════════════════════════════════════════

        // 120. Under the Floor
        private static void ER3_CellarSurvivors()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "☠  Under the Floor",
                "As your party prepares to leave, one of your men kicks through a root cellar door in a burned cottage. A family is in the dark below — grandparents, two young children — who have been in there since the raid began. They emerge slowly, covered in dust, squinting at the light. They do not speak. They are waiting to see what you do next.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Ensure they have food, water, and coin before you leave.", null, true,
                        "You leave something behind. The weight shifts, slightly."),
                    new InquiryElement("b", "Point them toward the nearest intact village and ride on.", null, true,
                        "A direction. Practical."),
                    new InquiryElement("c", "Say nothing and leave.", null, true,
                        "You leave. They watch you go."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(-300);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You have food and water left at the cellar entrance, coin pressed into the grandfather's hands. He holds it and looks at you. You rode in this morning and took what was here. You rode out this afternoon and left what was needed.", DimColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You point east and tell them the village name. The grandfather nods. The children stare at you the whole time you are giving the directions, not blinking, and you find you cannot meet their eyes while you speak.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("Your party moves out. The four of them stand in the light of the burned cottage and watch you go. The youngest child does not know to be afraid yet. She is still at the age where she assumes the adults around her know what is happening.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 129. Left Behind (mage, after siege)
        private static void ES4_AshenCrystal()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  Left Behind",
                "In a room off the keep's great hall, placed on a shelf between two books as if it belonged there: a small object of grey stone that is cold in a way that has nothing to do with temperature. The Ashen put this here before the siege began — possibly years before. It is a marker. It means: we were here. We will return for it.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Destroy it completely.", null, true,
                        "An hour's careful work. The cost is in your hands and your years. The thing is gone."),
                    new InquiryElement("b", "Keep it. Knowing it exists is an advantage.", null, true,
                        "You carry a gap in their network. They will eventually notice the silence."),
                    new InquiryElement("c", "Leave it in place — let them think nothing was found.", null, true,
                        "They will find the marker undisturbed. The deception is yours to manage."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgePlayer(1);
                            Msg("You work at it for an hour. The fire does not like what it touches — there is a resistance that is not physical, the cold pushing back against the warmth. Eventually it yields. The stone cracks and the cold in it is gone. The room is just a room. The cost is in your hands and in your years.", FireColor);
                            break;
                        case "b":
                            Msg("You wrap it in cloth and keep it separate from everything else. It will be cold to the touch for as long as you carry it. The Ashen use these to locate each other across distances. You now own a gap in their network. How long before the gap is noticed is a question without an answer yet.", AshenColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You leave it exactly where it is, touching nothing. When the Ashen return — and they will return — they will find the keep changed but the marker undisturbed. They will conclude their absence was unnoticed. You will know they concluded that. That is a small and specific advantage.", DarkColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // GATE-AMBUSH & MAGE-DUEL EVENTS
        // ═══════════════════════════════════════════════════════════════════

        // ── Gate-ambush helper ─────────────────────────────────────────────
        private static readonly Color WarnColor = new Color(0.80f, 0.30f, 0.15f);

        private static void SpawnAshenAtGate(Settlement s, int troops, float minStrength)
        {
            try { CampaignMapEvents.SpawnAshenAmbushNear(s.GetPosition2D, troops, minStrength); }
            catch { }
        }

        // Spawns a hostile party and immediately starts a field battle mission.
        // Uses PlayerEncounter.Start/SetupFields/StartBattle for a direct transition;
        // if that API path fails the party is still placed adjacent to the player
        // so Bannerlord's own encounter detection fires on the next campaign tick.
        private static void TriggerEncounterBattle(Settlement s, int troops)
        {
            try
            {
                var main = MobileParty.MainParty;
                if (main == null) return;

                var enemy = CampaignMapEvents.SpawnCombatPartyAt(s.GetPosition2D, troops);
                if (enemy == null) return;

                // Close enough for immediate encounter detection.
                try { enemy.Position2D = main.Position2D + new Vec2(0.05f, 0f); } catch { }

                // Attempt a direct transition to the battle mission.
                // PlayerEncounter.StartBattle() is the correct call on Bannerlord 1.x;
                // wrapped in try/catch so the proximity fallback above handles any
                // version where the method signature differs.
                try
                {
                    if (PlayerEncounter.Current == null)
                        PlayerEncounter.Start();
                    PlayerEncounter.Current.SetupFields(main, enemy);
                    PlayerEncounter.StartBattle();
                }
                catch { }
            }
            catch { }
        }

        // ── LEAVE CITY/CASTLE: An Insult at the Gate ─────────────────────────
        // A drunk lord's retainer blocks your path. The "fight" option calls
        // TriggerEncounterBattle which spawns the enemy and starts the real battle.
        private static void EL_InsultAtGate(Settlement s)
        {
            string charmHint = SkillHint(DefaultSkills.Charm, 0.35f, "Silence him without a blade");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  An Insult at the Gate",
                "A lord's retainer — drunk, loud, and clearly off a leash — blocks your path at the city gate. He makes a comment about your banner. Then about your horse. Then about you. He has four men behind him who look just sober enough to hold weapons. The gate guards are watching from thirty paces and doing nothing.",
                new List<InquiryElement>
                {
                    new InquiryElement("walk",  "Walk past. He is not worth the blood.", null, true,
                        "He'll say something worse as you pass. You will not turn around."),
                    new InquiryElement("words", "Answer him. One sentence, precisely chosen.", null, true, charmHint),
                    new InquiryElement("fight", "Draw. He asked for this with every word.", null, true,
                        "He and his men will find out what that costs. Right here, right now."),
                    new InquiryElement("coin",  "Pay the gate guards to remove him.", null, true,
                        "Thirty coin. Fast. Quiet. Done."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "walk":
                            Msg("He shouts something at your back. You do not turn around. You can hear him crowing to his men as you ride. It means nothing. You remember it anyway.", DimColor);
                            break;
                        case "words":
                            if (SkillRoll(DefaultSkills.Charm, 0.35f))
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                ChangeRenown(5f);
                                Msg("You stop. You say one thing — something he cannot answer without making himself smaller in front of his own men. The silence that follows is very loud. He lets you pass.", GoodColor);
                            }
                            else
                            {
                                ShiftTrait(DefaultTraits.Honor, -1);
                                Msg("You try. He interrupts. His men laugh. You ride out having said something that didn't land — and he knows it.", BadColor);
                            }
                            break;
                        case "fight":
                            TriggerEncounterBattle(s, 5);
                            Msg("You draw. He does too, half a second slower, and his men scramble behind him. Whatever happens next happens in the open, in daylight, with witnesses.", BadColor);
                            break;
                        case "coin":
                            ChangeGold(-30);
                            ChangeRelWithOwner(s, 2);
                            Msg("Thirty coin to the senior guard. He walks over, says three words. The retainer moves off — not happy, but moving. You ride out.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── ENTER VILLAGE: The Self-Taught Mage (mage-gated) ───────────────
        private static void EV7_SelfTaughtMage(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Self-Taught",
                "A mage at the village inn — self-trained, clearly capable, and entirely certain that the gift he found alone is the real version and what you carry is a lesser, inherited thing. He says this to your face without hostility, the way people state facts. He has been working with fire for eight years. He is wrong about the comparison. He is not wrong about his eight years.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Show him what the fire actually is — no argument, just the thing itself.", null, true,
                        "A day spent showing him the real thing. He cannot unknow it."),
                    new InquiryElement("b", "Challenge him formally — demonstrate the difference by working the same problem.", null, true,
                        "Working the same problem reveals something. His gift is real, if different."),
                    new InquiryElement("c", "Let him demonstrate first. Eight years of self-teaching is worth hearing.", null, true,
                        "Eight years of self-teaching is worth hearing. He may surprise you."),
                    new InquiryElement("d", "Agree with him — the gift finds what it finds. He's not wrong about that.", null, true,
                        "Agreement disarms the whole conversation. You leave him with something."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgePlayer(1);
                            Msg("You set something on the table between you — not a trick, not a demonstration, just the fire being what it is without the performance layer. He watches it for a long time. His own fire responds to it in a way that surprises him. He doesn't revise his opinion out loud. He revises it where opinions actually live. You ride out with a day of your life spent on a genuine exchange.", FireColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You give him a problem: heat a specific point without warming what surrounds it. He solves it differently than you would — more slowly, with more control over the margins. You solve it faster and with less precision. You both sit with this for a moment. He is not lesser. He is different. The difference matters in specific contexts. Neither of you had clearly understood that before.", FireColor);
                            break;
                        case "c":
                            AddMorale(3f);
                            Msg("He shows you a technique for sustaining a working with a fraction of the attention cost — he found it by accident in the third year and refined it over the following five. You have never seen it. It works. It solves a problem you didn't know you had. He looks at your face when you recognise its value and his certainty quietly reorganises itself around this new data point.", FireColor);
                            break;
                        case "d":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You agree with him — the fire is the fire, wherever it lands. He expected a contest. Your agreement disarms the whole conversation. He sits with it for a moment and then buys you a drink, which is the self-taught mage's version of a concession. You ride out with nothing changed and one person in the world who will speak well of you, specifically, for the rest of his life.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── ENTER VILLAGE: The Old Master's Student (mage, renown ≥ 600) ───
        private static void EV7_OldMastersStudent(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Old Master's Student",
                "A young woman — perhaps twenty — has been waiting at the village for three days, asking every traveler if they match her description. She studied under a mage you have heard of: dead now, one of the old ones who knew more than they ever wrote down. She has a question only someone at your level can answer, and she has been carrying it for two years.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Sit with her. Give her the honest answer.", null, true,
                        "A day spent on a question she has carried two years. The knowledge travels forward."),
                    new InquiryElement("b", "Answer but challenge her framing — the question has a better version.", null, true,
                        "The question has a better version. So does the exchange."),
                    new InquiryElement("c", "Test her first — see what she actually carries before deciding what she can receive.", null, true,
                        "A test reveals what she carries. What you find may surprise you."),
                    new InquiryElement("d", "Tell her she needs to find the answer herself — it won't mean anything received.", null, true,
                        "She will find the answer herself. She has reason to be frustrated."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgePlayer(1);
                            Msg("The question was: can the fire grieve. You answer it honestly, which costs more than you expected — the honest answer requires showing her the moment yours did. She takes it in quietly. She thanks you simply. She will teach what you gave her. It will carry your name, eventually, without anyone meaning it to.", FireColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You tell her the question she asked is a narrower version of a better question. She sits with this and produces the better question in about four minutes, which tells you everything about the quality of her training. The better question neither of you can answer fully. You spend an hour working at its edges. The master taught her well. What she does next with it will be hers.", FireColor);
                            break;
                        case "c":
                            AgePlayer(1);
                            Msg("You give her a working to complete — not a display, a test of understanding. She completes it three-quarters correctly and then does something you didn't expect: corrects herself mid-working without stopping, which is harder than getting it right the first time and significantly rarer. Her gift is real and her control is better than yours was at her age. The answer you give her afterward is the best version you have. She is ready for it.", FireColor);
                            break;
                        case "d":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You tell her to find it herself. She is frustrated. She asks why. You tell her: because the answer you find is yours; the answer I give you is mine, and mine won't fit where yours needs to go. She does not thank you. She will find the answer.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // SKILL-CHECK EVENTS — SOFT ROLL (probability scales with skill level)
        // ═══════════════════════════════════════════════════════════════════

        // ── ENTER VILLAGE: The Cold Trail [Scouting] ───────────────────────
        private static void EV8_ColdTrail(Settlement s)
        {
            float chance = SkillChance(DefaultSkills.Scouting, 0.25f);
            string hint  = SkillHint(DefaultSkills.Scouting, 0.25f, "Read the signs accurately");
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Cold Trail",
                "Something passed through this village recently — the signs are subtle and scattered: ash on a doorstep where no fire was lit, a handprint on a well-cover in grey dust, a dog that stopped barking three nights ago and has not started again. The villagers have not connected these things. You have.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Read the full trail — age, numbers, direction, intent.", null, true, hint),
                    new InquiryElement("b", "Warn the village and ask them to watch for more signs.", null, true,
                        "They are alerted. They won't know what to look for."),
                    new InquiryElement("c", "Mark the location and send the coordinates to the nearest garrison.", null, true,
                        "Correct process. Slow response."),
                    new InquiryElement("d", "Note it and keep moving — you have seen this pattern before.", null, true,
                        "The pattern continues."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Scouting, 0.25f))
                            {
                                ChangeRenown(8f);
                                Msg("Three individuals, two nights ago, moving northwest — they stopped at specific houses in a specific order, which means they had a list. They were not foraging or scouting randomly. They were checking names. Whether the names are observation targets or something worse is a question the pattern cannot answer, but the direction and the timing and the specificity all point somewhere that needs to be known. You know it now.", AshenColor);
                            }
                            else
                                Msg("You read what you can. The signs are two nights old — possibly three — and the movement pattern is unclear: you can see that something passed through but the direction fractures into two possibilities and the number could be two or five. What you know is incomplete. You can report what you have, which is better than nothing, which is what the village has.", DimColor);
                            break;
                        case "b":
                            ChangeRelWithOwner(s, 5);
                            Msg("You tell the headman what you saw without explaining what it means. He is the kind of man who will watch for more signs but will not know how to read them. He posts someone near the well at night. It is the wrong choice but it is a choice, and it is his, and he is doing it. You ride on with the mild dissatisfaction of having delegated a task to someone without the tools for it.", DimColor);
                            break;
                        case "c":
                            ChangeRelWithRandomLord(3);
                            Msg("The garrison receives the coordinates and notes them in the patrol log. Whether a patrol reaches the area before the trail is days colder is a scheduling question. You have created a record that exists. That is something.", DimColor);
                            break;
                        case "d":
                            Msg("You note it and ride. The pattern is familiar: preliminary observation, cataloguing. Not action yet. The gap between catalogue and action is the part nobody ever knows in advance. You know the pattern. You did not tell anyone else. That is a small and specific weight.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── ENTER VILLAGE [mage]: The Lie [Roguery] ────────────────────────
        private static void EV8_TheLie(Settlement s)
        {
            float chance = SkillChance(DefaultSkills.Roguery, 0.25f);
            string hint  = SkillHint(DefaultSkills.Roguery, 0.25f, "Read the deception precisely");
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Lie",
                "An old man at the inn table tells you there have been no grey-cloaked visitors in a week. He says this with full eye contact and complete stillness and the specific absence of the small corrections honest people make when they're trying to be accurate. He is lying. Whatever he saw, he was told to say he hadn't. The fire in you feels the cold in the room that isn't the weather.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Read the lie precisely — what is he hiding and who asked him to.", null, true, hint),
                    new InquiryElement("b", "Tell him you know he's lying and give him a chance to correct it.", null, true,
                        "He may talk. He may not. He will know you can read him."),
                    new InquiryElement("c", "Accept the lie and ask everyone else in the inn separately.", null, true,
                        "Someone else may not have been instructed."),
                    new InquiryElement("d", "Leave him to his loyalty. People protect what they have to protect.", null, true,
                        "Loyalty is its own kind of answer."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Roguery, 0.25f))
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                Msg("His hands moved once — toward his left pocket when you said 'visitors', then caught themselves. He was given something to keep and told to say nothing. You do not confront him. You wait until he uses the privy and check the pocket: a folded note with an Ashen symbol and a date three days from now. He was given a message to hold, not just a cover story. The date is a meeting.", AshenColor);
                            }
                            else
                                Msg("You read the performance but not the content behind it — you can see the lie clearly but not what it contains. He was told something and told to deny it. What specifically, you cannot extract from his manner alone. You know the shape of the secret without its substance. That is useful, imprecisely.", DimColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You tell him plainly that you know. He goes very still, then says: \"They told me my family would be checked on.\" He says nothing more. He does not know what they look like without their cloaks, where they came from, or when they are coming back. He knows they were here, they had a specific interest in the mill road north, and they scared him well enough to hold for three days. You have the road. That is something.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            if (_rng.Next(2) == 0)
                                Msg("The innkeeper's wife was not in the room when he was instructed. She tells you freely — three cloaks, two nights ago, asked about the mill road and a specific building at the north end. She describes the building. You know the building. It is a granary that doubles as a way-station on a particular trade route. The pieces connect.", AshenColor);
                            else
                                Msg("Everyone in the room received the same instruction or the same fear. You get polite misdirection from four people in four different registers. They are all protecting the same thing from different angles. The shape of the protection tells you it was recent and specific. You cannot get past the shape.", DimColor);
                            break;
                        case "d":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You let him keep it. He is protecting something with the only tools available to him. You ride on. He watches you leave.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── ENTER CITY: The Reluctant Official [Charm] ─────────────────────
        private static void EC8_ReluctantOfficial(Settlement s)
        {
            float chance = SkillChance(DefaultSkills.Charm, 0.25f);
            string hint  = SkillHint(DefaultSkills.Charm, 0.25f, "Persuade him to speak");
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚜  The Reluctant Official",
                "A city records clerk has information you need — movement orders for a specific gate, filed three weeks ago. He is technically required to provide access to lords on request. He is also clearly frightened of whoever filed those orders, and is deploying bureaucratic friction with the practiced ease of a man who has done it for years. He is not going to say no. He is going to take a very long time to say yes.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Ease his fear rather than press his duty.", null, true, hint),
                    new InquiryElement("b", "Invoke your authority formally — demand the record as a matter of right.", null, true,
                        "You get the record. He files a note. Someone will read it."),
                    new InquiryElement("c", "Offer him something in return — not a bribe, a guarantee.", null, true,
                        "A guarantee costs you nothing but your word. That may be enough."),
                    new InquiryElement("d", "Leave it. Frightened clerks produce bad records under pressure anyway.", null, true,
                        "The information stays filed."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Charm, 0.25f))
                            {
                                ChangeRenown(5f);
                                Msg("You spend ten minutes doing something that is not persuasion exactly — you make him understand that you already know what frightened him and that your interest is not in making his position worse. He relaxes by degrees. By the end he pulls the record himself and explains what it means without being asked. He does this because someone treating him as a person rather than an obstacle is rare enough to be worth responding to honestly.", GoodColor);
                            }
                            else
                                Msg("You try the right approach but the fear in him is too deep and too recent to ease in a single conversation. He appreciates the tone and gives you the record's existence but not its content — technically compliance. He cannot go further than this and he knows it. You have the reference number. That opens other doors.", DimColor);
                            break;
                        case "b":
                            ChangeRelWithOwner(s, -3);
                            ChangeRenown(3f);
                            Msg("You invoke the right and he produces the record — correctly, promptly, with the precise reluctance of a man who knows how to comply without cooperating. The record is there. What it contains is useful. He files a note about the interaction before you are out of the building. Someone will read that note. You have what you came for and a flag on your name in this city's records.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You tell him that if producing this record causes him any administrative difficulty, you will personally document that the request came under lord's authority and that he complied correctly. He looks at you for a long moment, understanding that this is the one thing he actually needed and nobody has ever thought to offer it. He pulls the record and gives it to you with his name on it. Brave, given what he was afraid of.", GoodColor);
                            break;
                        case "d":
                            Msg("You leave him to his friction and his fear. The record stays filed. Whatever it contained will surface another way, eventually, which is a comfort and not a solution.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── ENTER CITY: The Merchant's Ledger [Steward] ────────────────────
        private static void EC8_MerchantLedger(Settlement s)
        {
            float chance = SkillChance(DefaultSkills.Steward, 0.25f);
            string hint  = SkillHint(DefaultSkills.Steward, 0.25f, "Identify the specific irregularity");
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚖  The Numbers",
                "A merchant at the city guild hall is arguing with an auditor about a discrepancy in his import ledger. He says it's a clerical error. The auditor says it's systematic. They both appeal to you — you are a lord, apparently this grants you opinions about accounting. The ledger is on the table. One of them is right.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Read the ledger and find the specific problem.", null, true, hint),
                    new InquiryElement("b", "Side with the auditor — systematic discrepancies are not clerical errors.", null, true,
                        "The auditor is grateful. The truth of the ledger is still open."),
                    new InquiryElement("c", "Side with the merchant — a single anomaly could be clerical.", null, true,
                        "He may owe you something. He may be guilty of something."),
                    new InquiryElement("d", "Decline to arbitrate — this is not your ledger or your fight.", null, true,
                        "They continue without you."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Steward, 0.25f))
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                ChangeRenown(5f);
                                Msg("The irregularity is on page seven: an import weight listed in two different units across two entries — a conversion that only works if the second shipment was a third smaller than recorded. Someone is skimming the margin between what arrives and what is declared, using the unit difference as cover. It has been done the same way four times. The merchant goes pale. The auditor takes notes. You hand the ledger back and leave both of them to what follows.", GoodColor);
                            }
                            else
                                Msg("You work through it. The numbers are internally consistent in most places, which is actually the harder pattern to read — someone who knows accounting well enough to hide something in the averaging rather than a single line. You identify three possible locations for the irregularity but cannot determine which is the source. You tell them both this. The auditor takes it as confirmation. The merchant takes it as uncertainty. Both of them are right.", DimColor);
                            break;
                        case "b":
                            ChangeRelWithOwner(s, -3);
                            ChangeRelWithRandomLord(5);
                            Msg("You support the auditor. The merchant guild will remember this. The auditor thanks you and continues the review. The truth of the ledger remains open, but the process continues without obstruction.", DimColor);
                            break;
                        case "c":
                            ChangeRelWithOwner(s, 5);
                            Msg("You support the merchant. He thanks you warmly. Whether he is guilty is a question you have declined to answer. The auditor closes his notes without a word and leaves. The ledger goes unresolved.", DimColor);
                            break;
                        case "d":
                            Msg("You decline. They argue for another forty minutes and reach no conclusion. The merchant eventually agrees to a third-party review that will take six weeks. The auditor leaves unsatisfied. The ledger goes back into the hall. The truth is still in it.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── ENTER CITY: The Shadow [Scouting] ──────────────────────────────
        private static void EC8_Followed(Settlement s)
        {
            float chance = SkillChance(DefaultSkills.Scouting, 0.28f);
            string hint  = SkillHint(DefaultSkills.Scouting, 0.28f, "Confirm and identify them");
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Shadow",
                "You are being followed. Not by amateurs — whoever this is matches your pace correctly, uses the crowd well, and has been doing it since the eastern gate. You noticed because you were looking. Most people would not have noticed. The question is what to do with the knowledge before they realise you have it.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Confirm identity before acting — read who they are without alerting them.", null, true, hint),
                    new InquiryElement("b", "Reverse the tail — follow your follower.", null, true,
                        "You may get ahead of them, or they may catch your move."),
                    new InquiryElement("c", "Lead them somewhere useful and confront them on your terms.", null, true,
                        "You pick the ground. The confrontation is controlled."),
                    new InquiryElement("d", "Change your route and lose them — you have no time for this today.", null, true,
                        "They lose you. You learn nothing about who sent them."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Scouting, 0.28f))
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                ChangeRenown(5f);
                                Msg("You use a shop window and a narrow passage to get a clear look without stopping. City watch — not uniformed, working plainclothes. This is a sanctioned surveillance, not a freelance tail. Someone in city administration has an official interest in your movements. That is a different kind of problem than an Ashen watcher. You continue your route as if unaware and note everything they observe.", DimColor);
                            }
                            else
                                Msg("You try to get a look but they are better than you expected and shift position exactly when you commit to the read. You confirm they are professional and confirm they are still there. That is all you get. You cannot tell employer, motive, or number — there may be more than one. You continue with incomplete information, which is the standard condition.", DimColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            if (_rng.Next(2) == 0)
                                Msg("You take a corner fast and double back through the market, reading the crowd backward. You find the position they occupied two minutes ago and the position they are moving to. From behind, you follow them to a building two streets from the lord's hall — private, no guild mark, curtains drawn at midday. Someone is using that building for something that isn't commerce. You have an address.", DimColor);
                            else
                                Msg("They catch your move — probably they were watching for it. By the time you complete the reverse they are gone, and there is a different presence behind you: not following, just present. You have announced that you know. Whatever they were doing becomes more careful. The tail continues with better tradecraft.", BadColor);
                            break;
                        case "c":
                            ChangeRenown(5f);
                            Msg("You lead them to a courtyard with one entrance and position your party at it before stopping. When they arrive they find you waiting. The professional response is to acknowledge it: one of them steps forward and explains they are city watch, tracking the movements of 'persons of interest' per the lord's standing order. You are apparently a person of interest. They file their report. You have their faces, their methods, and the confirmation that the order comes from the lord directly.", GoodColor);
                            break;
                        case "d":
                            Msg("Three corners and a market crossing and you are clean. They are good but you know this city's geometry better from yesterday's approach. You complete your business without a tail. They know you noticed. You are ahead by one day.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── LEAVE VILLAGE: The Poisoned Well [Medicine] ────────────────────
        private static void LV8_PoisonedWell(Settlement s)
        {
            float chance = SkillChance(DefaultSkills.Medicine, 0.25f);
            string hint  = SkillHint(DefaultSkills.Medicine, 0.25f, "Diagnose and treat correctly");
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✚  The Well",
                "As you prepare to leave, three children are brought to the inn in quick succession — all from the same family, all with the same symptoms: pale, cramping, confused. The parents are terrified and the herb-woman is overwhelmed. All three drank from the eastern well this morning. The well is still being used. The village does not yet understand what is happening.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Diagnose and treat — identify the cause and act on it.", null, true, hint),
                    new InquiryElement("b", "Seal the well immediately and send for the nearest physician.", null, true,
                        "The well is sealed. Help is coming. The children wait."),
                    new InquiryElement("c", "Leave medicine from your supplies and your best guess at the cause.", null, true,
                        "Your guess may be right. The herb-woman will work from what you leave."),
                    new InquiryElement("d", "Delay your departure until you know they are stable.", null, true,
                        "They will be stable or they will not. You stay either way."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Medicine, 0.25f))
                            {
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                ChangeRenown(8f);
                                Msg("Mineral — cold ash contamination from something upstream, not poison in the active sense but wrong chemistry in quantity. You know the treatment: controlled fluid replacement, specific herbs, no dairy, rest. You walk through the well and seal it correctly, identifying the exact upstream source from the residue pattern. By evening the children are worse before they are better. By morning two of them are asking for food. The third takes another day. All three live. You ride out with one day less and one thing done correctly.", GoodColor);
                            }
                            else
                            {
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                Msg("You identify it as contamination and seal the well, which is correct and saves further damage. The specific cause and treatment, you're less certain about — your best guidance helps somewhat and the herb-woman corrects the gaps with her own knowledge. It becomes a collaboration and it is not elegant but the children survive. Two of them recover fully. One will have a difficult month ahead. All three live, which was the real question.", DimColor);
                            }
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You seal the well yourself and dispatch a rider. The physician arrives two days later. By then one of the children has stabilised on the herb-woman's efforts and two are worse. The physician works through the night. All three survive but the delay cost something. The well was sealed in time to save the rest of the village. The river question was contained. This was the right choice given what you knew. It was not the best possible outcome.", DimColor);
                            break;
                        case "c":
                            ChangeGold(-200);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You leave what you have and your best reading of the symptoms. The herb-woman takes your supplies and your guess with the concentrated focus of someone filtering useful signal from educated approximation. She is better with the approximate information than with nothing. Two children stabilise by evening. The third is harder. She sits with the third child through the night. In the morning you will not know how it ended. You hope your guess was close enough.", DimColor);
                            break;
                        case "d":
                            AddMorale(-3f);
                            Msg("You delay. Your men understand this with the quiet acceptance of soldiers who have ridden for lords who would not have stopped. By dusk two of the children are clearly improving and one is not. You stay through the night. At dawn, the third child's fever breaks. You ride out three hours late and your men ride without complaint for twelve miles before anyone says anything. Then someone near the back laughs at something and the column relaxes.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── LEAVE VILLAGE: The Setup [Tactics] ─────────────────────────────
        private static void LV8_BattleSetup(Settlement s)
        {
            float chance = SkillChance(DefaultSkills.Tactics, 0.28f);
            string hint  = SkillHint(DefaultSkills.Tactics, 0.28f, "Read the setup before it springs");
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  The Setup",
                "A rider catches you at the edge of the village with urgent news: a lord two valleys over needs your help — ambushed, pinned, requesting your column immediately. The message is correctly sealed and the rider is convincing. Something in the framing is wrong — the route he names, the timing, the too-specific detail about troop count. You have seen setups. This has the shape of a setup.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Read the tactical picture — confirm this is a trap and identify its structure.", null, true, hint),
                    new InquiryElement("b", "Send a fast scout ahead before committing the column.", null, true,
                        "A scout confirms the truth of it, whatever it is."),
                    new InquiryElement("c", "Respond as if it's real but at half-speed with flankers out.", null, true,
                        "Caution covers either outcome."),
                    new InquiryElement("d", "Dismiss the rider and send word to the lord mentioned to verify.", null, true,
                        "The truth takes time. Your decision may cost something depending on which truth it is."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Tactics, 0.28f))
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                ChangeRenown(8f);
                                Msg("The troop count detail is too specific — no messenger in a real ambush counts enemy troops that precisely, they estimate. The route named adds ninety minutes for no terrain reason. The seal is correct but the phrasing of the request contains an error that someone who had actually served with that lord would not have made. This is a kill box. You turn your column, take the southern road, and send a rider to the actual lord, who reports he is in his hall and has not been ambushed. Somebody wanted your column on a specific road at a specific time. They are waiting.", GoodColor);
                            }
                            else
                                Msg("Something is wrong but you cannot isolate it cleanly enough to be certain. The seal is right. The rider is convincing. The route bothers you for a reason you cannot fully articulate. You decide to proceed cautiously rather than dismiss it, which is neither fully correct nor fully wrong. You learn the truth two miles down the road, with flankers out, which was the right preparation.", DimColor);
                            break;
                        case "b":
                            Msg("Your scout rides ahead and returns in forty minutes: empty road for two miles, then sign of a prepared position — cut branches covering a ditch, horses tied off the road. It is a trap and they are already committed to it. You have thirty minutes before they adjust. Your column has the information and the initiative.", GoodColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You advance with flankers extended and pace halved. If it is real, your caution costs thirty minutes. If it is not, your flankers reach the ambush position first. They do. Three men in the ditch, caught too early. They surrender before the column arrives. The plan required your column moving at normal speed.", GoodColor);
                            break;
                        case "d":
                            if (_rng.Next(2) == 0)
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                Msg("The verification rider returns: the lord has not sent any message and is not in distress. He is now, however, aware that someone is impersonating his seal and setting traps under his name. He thanks you with the warmth of someone who just avoided an incident they didn't know was coming. You avoided the trap. He deals with the seal forgery. Both of you gained something.", GoodColor);
                            }
                            else
                            {
                                ShiftTrait(DefaultTraits.Honor, -1);
                                Msg("The lord's reply: he was ambushed, he did send the message, the rider is real, and your delay cost him two men who could have been extracted by a column that arrived on time. He is alive. His gratitude is not the warm kind. You chose caution when speed was required. The information that led to that choice was correct in shape but wrong in content.", BadColor);
                            }
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── LEAVE CITY: The Gate Standoff [Charm] ──────────────────────────
        private static void LC8_GateStandoff(Settlement s)
        {
            float chance = SkillChance(DefaultSkills.Charm, 0.25f);
            string hint  = SkillHint(DefaultSkills.Charm, 0.25f, "Talk him down without violence");
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚜  The Standoff",
                "A man is holding a merchant at knifepoint at the city gate. Not a robbery: his daughter was taken by the merchant in lieu of a debt, the law permits it, and he has apparently run out of other options. The gate guard is twenty feet away deciding whether to intervene. The merchant is frightened. The father is not — he is decided. Neither of them is going to improve the situation alone.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Talk the father down without threats.", null, true, hint),
                    new InquiryElement("b", "Buy the daughter's debt yourself — end the material cause.", null, true,
                        "You end the material cause. The law is satisfied."),
                    new InquiryElement("c", "Invoke your authority — demand both parties stand down immediately.", null, true,
                        "A lord's direct order resolves the standoff. Not necessarily the problem."),
                    new InquiryElement("d", "Tell the merchant, publicly, that his behaviour is noted and documented.", null, true,
                        "The merchant's position becomes uncomfortable. The father has a witness now."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Charm, 0.25f))
                            {
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                ChangeRenown(5f);
                                Msg("You speak to him — not about the knife, about the daughter. You ask her name. He tells you. You ask what he was going to do after. He does not have an answer, which is the opening. A man who has run out of options will take a new one if it is real. You tell him you will hear his case formally, today, and that the merchant will be required to present the debt documentation. The knife goes down. The guard, watching, does not intervene. The merchant understands something about the day has changed.", GoodColor);
                            }
                            else
                            {
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                Msg("You speak to him and he listens but the words that would reach him are words you do not quite find. He is beyond the range of reassurance — he needs a specific thing, not comfort. He puts the knife down eventually but not because of what you said. He puts it down because the guard is closer now and he has calculated correctly that this ends badly for him regardless. He goes with the guard. The daughter's situation is unchanged.", DimColor);
                            }
                            break;
                        case "b":
                            ChangeGold(-400);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You step between them and pay the debt to the merchant directly, in coin, in front of the gate guard. The material cause disappears. The father watches the coin change hands with the expression of a man watching the thing he was willing to die for become solvable. He releases the knife. He does not thank you — the words he had ready were not for this outcome. The daughter is released that afternoon.", GoodColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You order both parties to stand down with the full weight of your rank. The father complies because a lord's direct order is not the same as a gate guard's. He is taken by the guard and held. The standoff is over. The daughter's debt remains in force. The merchant walks away quickly. The father's compliance was real and cost him everything he had left. You invoke authority correctly and it resolves the wrong problem.", DimColor);
                            break;
                        case "d":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You name the merchant's action and the debt mechanism loudly enough for the twenty people at the gate to hear every detail. The merchant's face changes. The gate guard, who has a family of his own, starts paying closer attention. The father watches the audience form and understands that he now has witnesses and a lord on record. He lowers the knife himself. The merchant leaves quickly. The father's case is not resolved, but it has a shape and a record now that it did not have two minutes ago.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── LEAVE CITY: The Forgery at the Gate [Roguery] ──────────────────
        private static void LC8_ForgeryAtGate(Settlement s)
        {
            float chance = SkillChance(DefaultSkills.Roguery, 0.28f);
            string hint  = SkillHint(DefaultSkills.Roguery, 0.28f, "Identify the forgery and who wrote it");
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Forgery",
                "The gate guard stops a merchant wagon and shows you a travel permit he is suspicious about — it's for three wagons and the seal looks right but he cannot explain why it bothers him. He is asking you because you are a lord and lords apparently know about seals. The merchant is watching this from twenty feet away with the composed expression of someone who knows exactly what is happening.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Read the forgery — identify what's wrong and who produced it.", null, true, hint),
                    new InquiryElement("b", "Back the guard's instinct — detain the merchant pending verification.", null, true,
                        "Backing the guard's instinct. Verification will tell the rest."),
                    new InquiryElement("c", "Wave it through — the seal looks right and you have somewhere to be.", null, true,
                        "The seal looks right. You have somewhere to be."),
                    new InquiryElement("d", "Ask the merchant directly where the permit was issued.", null, true,
                        "His answer will tell you whether he knows what he is carrying."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Roguery, 0.28f))
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                ChangeRelWithOwner(s, 5);
                                ChangeRenown(5f);
                                Msg("The ink on the seal impression is the wrong shade for the permit office's wax — they changed their wax supplier eight months ago and the forger used old stock. The letter spacing on the issuing authority's title is also wrong: a period after 'Lord' that the real office stopped using two years back. This document was made by someone with access to old examples but not current practice. You show the guard both markers. The merchant's composure breaks precisely at the second marker, which is where he knew the risk was. He is detained.", GoodColor);
                            }
                            else
                                Msg("Something is wrong but you cannot isolate it to a specific marker. The seal is good enough to pass most inspections and your reading of the text formatting finds one possible issue that could also be a variant on legitimate practice. You tell the guard it may be forged but that you cannot confirm it. He detains the merchant for verification based on your maybe. The merchant is furious. Whether the document was forged will be determined by the permit office in three days.", DimColor);
                            break;
                        case "b":
                            ChangeRelWithOwner(s, 5);
                            if (_rng.Next(2) == 0)
                                Msg("The guard detains the merchant. The permit office's response comes in two days: forged. The merchant had contraband in the second wagon. The guard's instinct was correct. Your backing gave him the authority to act on it. He will remember that a lord trusted his read.", GoodColor);
                            else
                                Msg("The permit office's response comes in two days: legitimate. The merchant had a real permit and a delayed shipment and is furious about the detention at a level that may become a formal complaint. The guard's instinct was wrong. Your backing made it stick. He is embarrassed. You have a complaint pending. The seal was genuine.", BadColor);
                            break;
                        case "c":
                            Msg("The permit was forged. The wagon contained grey-dyed cloth that matches Ashen courier colours exactly — not contraband in any legal sense, but material with a specific use. It cleared your gate. The merchant was gone before the guard's follow-up instinct completed. You made a small decision at a gate and someone's supply run went through. Whether that matters depends on what the cloth is for.", BadColor);
                            break;
                        case "d":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("He names the issuing office correctly but the date is yesterday, which is too fast for the permit office's actual processing time — they take three days minimum. He knows the date is wrong, which means he knows what he is carrying and where it came from. He does not flinch when you point this out. He is a professional. You have him detained not for the document but for his knowledge of the document's real origin. That is a better charge.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── AFTER BATTLE (Ashen): Memory Drain ────────────────────────────────
        // Fires when an Ashen player cast 3+ spells in a single battle.
        // Choices: resist (Leadership roll), accept loss, or feed it (gains XP, larger loss).
        private static void EB_AshenMemoryDrain()
        {
            // Pick one of the three humane traits to drain (the ones Ashen players are losing)
            var candidates = new[] { DefaultTraits.Mercy, DefaultTraits.Honor, DefaultTraits.Generosity };
            var drainTrait = candidates[_rng.Next(candidates.Length)];
            string traitName = drainTrait == DefaultTraits.Mercy ? "Mercy"
                             : drainTrait == DefaultTraits.Honor ? "Honor" : "Generosity";

            float leadershipChance = SkillChance(DefaultSkills.Leadership, 0.35f);
            string resistHint = SkillHint(DefaultSkills.Leadership, 0.35f, "Hold the memory through will");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Fading",
                "Afterward, in the quiet, you reach for something and it is not there. A name. A face. Something that was yours. " +
                "The working took more than years this time — it reached further back and took something you did not offer. " +
                "You are aware of the shape of what is missing. You are not certain you know what it was.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Hold on. Reach for it — force it back.", null, true, resistHint),
                    new InquiryElement("b", "Let it go. There was a cost. You paid it.", null, true,
                        $"What is gone is gone. You remain. {traitName} fades."),
                    new InquiryElement("c", "Feed it more. If it wants to take, let it take — and take something in return.", null, true,
                        $"A deeper trade. More is lost. Something is gained."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Leadership, 0.35f))
                            {
                                Msg("You force it. The memory surfaces — incomplete, edges worn, but present. A face. The shape of a place. " +
                                    "Something that was yours is still yours. The effort costs you the rest of the evening. " +
                                    "You are aware the cold is patient.", AshenColor);
                            }
                            else
                            {
                                ShiftTrait(drainTrait, -1);
                                Msg($"You reach for it and find the reaching itself is unfamiliar — the path to it has gone cold. " +
                                    $"You hold the effort until it becomes clear that there is nothing left to hold. {traitName} fades. " +
                                    "The cold does not announce itself. It simply expands.", BadColor);
                            }
                            break;
                        case "b":
                            ShiftTrait(drainTrait, -1);
                            ChangeGold(-300);
                            Msg($"{traitName} fades. Something else goes with it — the thread of a connection you can no longer name. " +
                                "Three hundred coin gone by morning, in a sequence of decisions you do not entirely remember making. " +
                                "You paid what was asked. You are still here. The accounting is unclear.", BadColor);
                            break;
                        case "c":
                            ShiftTrait(drainTrait, -1);
                            ShiftTrait(candidates[_rng.Next(candidates.Length)], -1);
                            try { Hero.MainHero.HeroDeveloper.UnspentFocusPoints += 1; } catch { }
                            ChangeRenown(-10f);
                            Msg("You open the door. Something passes through you in both directions — you feel the loss clearly, two things, maybe more. " +
                                "What returns is not the same shape as what left. It is colder. It is useful. " +
                                "Your men look at you strangely over the fire that evening. You do not ask them why.", AshenColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── AFTER BATTLE: Field Triage [Medicine] ──────────────────────────
        private static void EB8_FieldTriage()
        {
            float chance = SkillChance(DefaultSkills.Medicine, 0.25f);
            string hint  = SkillHint(DefaultSkills.Medicine, 0.25f, "Apply your own knowledge alongside the surgeon");
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✚  The Surgeon's Question",
                "Your surgeon has done what he can with what he has. He comes to you with a specific problem: two men with abdominal wounds, one set of gut-surgery supplies, and a clinical decision he says is above his certainty. He is asking you — not because he thinks you're a surgeon, but because he has seen enough of you to know whether you are the kind of person who has relevant information and the honesty to say when you don't.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Work alongside him — apply whatever you know.", null, true, hint),
                    new InquiryElement("b", "Give him the decision completely — this is his skill, not yours.", null, true,
                        "This is his skill. Your men will see you trust him completely."),
                    new InquiryElement("c", "Ask him to explain the clinical picture fully before you say anything.", null, true,
                        "Your question may reshape his thinking. He may reach a better answer."),
                    new InquiryElement("d", "Spend gold on an urgent courier for a specialist while the surgeon holds the situation.", null, true,
                        "The specialist is coming. The surgeon holds the situation until then."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Medicine, 0.25f))
                            {
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                AddMorale(6f);
                                Msg("You have seen enough battlefield surgery to recognise what he is weighing — the wound sites are different in a way that changes the priority. You tell him what you see. He checks it against his own read and adjusts. Both men receive treatment in the right order. Both survive. Your surgeon looks at you differently afterward — not with deference, just professional respect.", GoodColor);
                            }
                            else
                            {
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                AddMorale(2f);
                                Msg("You share what you know. Some of it is useful and he incorporates it. Some of it is below his knowledge level and he sets it aside without comment. The combined knowledge is better than his alone. One man survives who might not have. The other was beyond the combined knowledge of both of you. Your surgeon closes that file with the quiet efficiency of someone who has written those reports before and will write them again.", DimColor);
                            }
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            AddMorale(4f);
                            Msg("You give him the decision entirely and tell him so directly. He makes it cleanly, without the hesitation of someone who is second-guessing a superior's preferences. One man survives. The other does not — this was the likely outcome either way, and the surgeon's choice was correct given what was knowable. Your men watch you trust your own people completely. That travels through a column faster than orders.", GoodColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            AddMorale(4f);
                            Msg("You ask him to describe the clinical picture fully before you say anything. In describing it, he hears something he had not heard while thinking it. He stops and redirects. Both men receive treatment. Both survive. He thanks you for the question.", GoodColor);
                            break;
                        case "d":
                            ChangeGold(-500);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            AddMorale(3f);
                            Msg("The courier rides hard. The specialist arrives six hours later with better supplies and a different technique. He saves one of the two men with confidence and works on the second for three hours before confirming what your surgeon already knew. One man lives who would not have. The 500 coin bought a life and a specific hour's professional certainty. Your surgeon watches the specialist work with the full attention of a man taking notes.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── AFTER BATTLE: The Debrief [Tactics] ────────────────────────────
        private static void EB8_BattleDebrief()
        {
            float chance = SkillChance(DefaultSkills.Tactics, 0.28f);
            string hint  = SkillHint(DefaultSkills.Tactics, 0.28f, "Identify and correct the misread");
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  The Debrief",
                "Your sergeant is debriefing the battle with your officers and his read of what happened at the centre is subtly but importantly wrong — he believes the enemy centre held because of superior numbers, but you saw something else from your position. If his interpretation enters your officers' working model of how battles develop, it will inform a decision the wrong way at a moment that matters.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Correct the read precisely — explain what actually held the centre.", null, true, hint),
                    new InquiryElement("b", "Ask him what he saw from his position before offering a counter-reading.", null, true,
                        "The combined account may be better than either alone."),
                    new InquiryElement("c", "Let it stand for now — his read is close enough for the lesson they need today.", null, true,
                        "The wrong model travels forward. It may matter."),
                    new InquiryElement("d", "Bring in a second officer whose position gave them a clear view of the centre.", null, true,
                        "A third account may resolve the question."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Tactics, 0.28f))
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                AddMorale(5f);
                                Msg("The centre held because of a deliberate false retreat on their right — it drew your left flank's attention and compressed the centre's pressure by a third. The numbers were coincidence. Your sergeant listens, asks two clarifying questions, and restates the corrected read back to the officers. They leave the debrief with the right lesson.", GoodColor);
                            }
                            else
                                Msg("You offer your counter-read but cannot articulate the mechanism clearly enough — you saw something but translating what you saw into the language of formation tactics is harder than seeing it was. Your sergeant acknowledges your disagreement and hedges his read without fully revising it. The debrief ends with ambiguity rather than a clean correction. Better than the wrong certainty. Not as good as the right certainty.", DimColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            AddMorale(4f);
                            Msg("You ask what he saw. His account of the centre from his position is actually accurate from where he stood — he simply could not see the false retreat from the left flank, which was behind his line of sight. When you add your account to his the combined picture is clearer than either alone. He revises his conclusion correctly and credits the two-position reading. The officers leave with a better model and a method for building one.", GoodColor);
                            break;
                        case "c":
                            Msg("You let it stand. His read is close enough that the lesson — hold the centre, watch for flanking pressure — is defensible. The mechanism is wrong. This will matter exactly once, at a moment that will not announce itself in advance. You have chosen convenience and it may cost nothing. You do not know yet.", DimColor);
                            break;
                        case "d":
                            AddMorale(3f);
                            Msg("You bring in the officer who held the right flank and had the clearest view of the centre from the side. His account adds the false retreat that your sergeant missed. With all three readings on the table the correct picture emerges naturally — no one person's reading was wrong, it was incomplete. The debrief ends with a model that was earned rather than imposed. Your men respect the process. So does your sergeant.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── AFTER SIEGE: The Siege Stores [Steward] ────────────────────────
        private static void ES8_SiegeStores()
        {
            float chance = SkillChance(DefaultSkills.Steward, 0.25f);
            string hint  = SkillHint(DefaultSkills.Steward, 0.25f, "Divide the stores correctly and equitably");
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚒  The Division",
                "The keep's stores are more than expected — the previous lord was preparing for a long siege. Your quartermaster can manage the military share straightforwardly, but the civilian question is harder: the city's merchants and the keep's garrison staff both have claims under different precedents, and the amounts are large enough that the wrong division will cause problems before the week is out.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Work through the division properly — apply the correct precedents.", null, true, hint),
                    new InquiryElement("b", "Give your quartermaster full authority — this is his domain.", null, true,
                        "This is his domain. Trust him with it."),
                    new InquiryElement("c", "Take the military share, return the city merchants' claims, and call the garrison staff claims void.", null, true,
                        "Fast and clean for some. Not for all."),
                    new InquiryElement("d", "Hire a city administrator to manage the civilian claims under your oversight.", null, true,
                        "A clean distribution. The city may trust the outcome more."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Steward, 0.25f))
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                ChangeRenown(8f);
                                AddMorale(4f);
                                Msg("The correct precedent is siege capture law modified by the city charter's civilian commerce protections — the merchants' pre-siege inventory claims are valid up to the point of city closure, garrison staff claims are pro-rated by months served, and the military share is calculated after both civilian claims are satisfied. You work through it in two hours. Nobody gets everything. Nobody has a legitimate grievance. Your quartermaster is taking notes.", GoodColor);
                            }
                            else
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                AddMorale(2f);
                                Msg("You apply the precedents as best you can, but the intersection of siege law and city charter has a gap that your reading doesn't resolve cleanly. You make a defensible decision rather than a correct one. The merchants accept it. The garrison staff accept it for now — but they will raise the gap in a formal petition in six months.", DimColor);
                            }
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            AddMorale(5f);
                            Msg("You give him the full authority directly and say so in front of the claimants. He takes it and works through it correctly — he has done this before, or something close enough that the differences don't matter. The distribution takes three hours and satisfies both groups adequately. He reports back to you with the numbers.", GoodColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            ChangeRelWithRandomLord(-5);
                            Msg("Fast, clear, defensible on the merchant side. The garrison staff, who served through a siege under a lord who is now your prisoner, receive nothing for that service. Some of them are veterans. Some of them protected this city from your assault for months. The bitterness is specific and immediate. The merchants are satisfied and will say so. The garrison staff are not and will also say so. The city's opinion of you is divided along exactly those lines.", DimColor);
                            break;
                        case "d":
                            ChangeGold(-300);
                            ChangeRenown(5f);
                            Msg("You hire the city's former exchequer — not the previous lord's man, an independent one who worked for the trade council. He manages the civilian claims under your sign-off, which means the distribution carries official weight but the process has local credibility. It costs three hundred coin and two days. The result is accepted without significant objection by anyone, which in post-siege administration is close to a miracle.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // ── (original event 40 follows below, unchanged) ──────────────────
        // ═══════════════════════════════════════════════════════════════════

        // 40. An Old Enemy
        private static void E_OldEnemy(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  An Old Enemy",
                "A weathered veteran in the city square catches your eye and holds it. He was on the other side of a battle three years ago — you remember his face from across a line of shields. He remembers yours. He raises his cup toward you.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Raise your hand in return. Wars end.", null, true,
                        "The gesture contains the whole of it. Wars end."),
                    new InquiryElement("b", "Walk past as if you have not seen him.", null, true,
                        "He watches you pass. The moment passes."),
                    new InquiryElement("c", "Report his presence to the city guard as a potential threat.", null, true,
                        "He is a veteran with a cup. The city guard will remember this."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ChangeRelWithRandomLord(5);
                            Msg("You raise your hand. He nods. The gesture contains the whole of it — we both survived, we are both still here, that is something. You do not speak. You do not need to.", GoodColor);
                            break;
                        case "b":
                            Msg("He watches you pass. He does not follow. He will be here tomorrow, with his cup, waiting for nothing in particular.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            ChangeCrime(5f);
                            Msg("He is taken in for questioning and released inside the hour — there is no cause. He looks at you differently when he comes out. So do you, at yourself.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── Helper: wound the player hero and a few party members ─────────────
        private static void WoundPlayer()
        {
            try { Hero.MainHero.HitPoints = Math.Max(1, Hero.MainHero.MaxHitPoints / 4); } catch { }
            try
            {
                var roster = MobileParty.MainParty?.MemberRoster;
                if (roster == null) return;
                int toWound = 3 + _rng.Next(8), wounded = 0;
                foreach (var e in roster.GetTroopRoster().ToList())
                {
                    if (e.Character.IsHero) continue;
                    int healthy = e.Number - e.WoundedNumber;
                    int w = Math.Min(healthy, toWound - wounded);
                    if (w <= 0) continue;
                    roster.AddToCounts(e.Character, 0, false, w);
                    wounded += w;
                    if (wounded >= toWound) break;
                }
            }
            catch { }
        }

        // ── Helper: wound a number of party troops (simulate Curse hit) ───────
        private static void WoundPartyTroops(int count)
        {
            try
            {
                var roster = MobileParty.MainParty?.MemberRoster;
                if (roster == null) return;
                int wounded = 0;
                foreach (var e in roster.GetTroopRoster().ToList())
                {
                    if (e.Character.IsHero) continue;
                    int healthy = e.Number - e.WoundedNumber;
                    int w = Math.Min(healthy, count - wounded);
                    if (w <= 0) continue;
                    roster.AddToCounts(e.Character, 0, false, w);
                    wounded += w;
                    if (wounded >= count) break;
                }
            }
            catch { }
        }

        // ── Helper: permanently remove N troops from the party ────────────────
        private static void KillPartyTroops(int count)
        {
            try
            {
                var roster = MobileParty.MainParty?.MemberRoster;
                if (roster == null) return;
                int killed = 0;
                foreach (var e in roster.GetTroopRoster().ToList())
                {
                    if (e.Character.IsHero || e.Number <= 0) continue;
                    int take = Math.Min(e.Number, count - killed);
                    if (take <= 0) break;
                    roster.AddToCounts(e.Character, -take);
                    killed += take;
                    if (killed >= count) break;
                }
                if (killed > 0) Msg($"({killed} soldier{(killed == 1 ? "" : "s")} found dead at dawn)", BadColor);
            }
            catch { }
        }

        // ── Helper: become Ashen (full conversion sequence) ───────────────────
        private static void BecomeAshen()
        {
            // Sync MageKnowledge player flags first so grimoire + spell aging work correctly.
            // ColourLordRegistry.SetAshen only updates the NPC-tracking sets; MageKnowledge
            // has its own _isMage / _isAshen flags that drive the player-facing UI and mechanics.
            try { MageKnowledge.SetMage(true); }  catch { }
            try { MageKnowledge.SetAshen(true); } catch { }

            try { ColourLordRegistry.SetAshen(Hero.MainHero, true); } catch { }
            try { AshenCitySystem.ApplyAshenPersonality(Hero.MainHero); } catch { }
            try { ColourLordRegistry.SetMage(Hero.MainHero, true); } catch { }
            try { AshenCitySystem.OnPlayerBecameAshen(); } catch { }
            try { MageKnowledge.ApplyAshenAppearance(Hero.MainHero); } catch { }
            // Queue the frenzy event for the next daily tick if there's someone to lose
            if (HasFamilyOrCompanions())
                _ashenFrenzyCountdown = 1;
        }

        // ════════════════════════════════════════════════════════════════════
        // DARK / ASHEN SETTLEMENT EVENTS
        // ════════════════════════════════════════════════════════════════════

        // ── EV_DarknessSpreads — village enter ────────────────────────────────
        // Your scouts report cold blue flames in the fields at night and livestock
        // found bloodless at dawn. Ashen cultists may be hiding in the village.
        private static void EV_DarknessSpreads(Settlement s)
        {
            string vName = s.Name?.ToString() ?? "the village";
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★ Darkness in the Roots",
                $"Your scouts found livestock dead in the fields near {vName} — bloodless, cold, " +
                $"facing the same direction. The villagers won't meet your eyes. " +
                $"Someone lit fires in the northern field after midnight, " +
                $"the wrong colour and shape for hearth or harvest. " +
                $"You cannot prove it, but something Ashen has been here recently.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Burn the village. Cultists hide among the innocent here.", null, true,
                        "Fire answers certainty. What it finds is another matter."),
                    new InquiryElement("b", "Spare them. There is no solid proof.", null, true,
                        "Mercy without proof. The outcome will tell you if you were right."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            // Simulate a village raid
                            try { s.Village.Hearth = Math.Max(10f, s.Village.Hearth * 0.30f); } catch { }
                            ChangeCrime(50f);
                            if (_rng.NextDouble() < 0.5)
                            {
                                ChangeRelWithOwner(s, -60);
                                Msg($"You gave the order. {vName} burned by morning. " +
                                    $"Whether the cultists were inside, or fled, or were never truly there — " +
                                    $"you will not know. The settlement's lord received word before the ash cooled.", BadColor);
                            }
                            else
                            {
                                Msg($"You gave the order. {vName} burned by morning. " +
                                    $"The owner's people came to assess the damage and said nothing in your presence.", BadColor);
                            }
                            break;
                        case "b":
                            if (_rng.NextDouble() < 0.5)
                            {
                                Msg($"You passed through {vName} and rode on. " +
                                    $"The cold feeling faded by nightfall. Perhaps you misread the signs. " +
                                    $"Perhaps the cultists saw your mercy and scattered.", DimColor);
                            }
                            else
                            {
                                // Spawn 200 Ashen near the village
                                try { CampaignMapEvents.SpawnAshenAmbushNear(s.GetPosition2D, 20, 180f); } catch { }
                                Msg($"You showed mercy and rode on. That evening, {vName} caught fire from three sides at once. " +
                                    $"Ashen Spawn poured from the shadows — the cultists had already called for them. " +
                                    $"The village burned regardless of your choice.", BadColor);
                            }
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── EV_BurningWitch — village enter ───────────────────────────────────
        // A young girl is stripped to a stake. The villagers intend to burn her.
        private static void EV_BurningWitch(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★ The Pyre",
                "In the village square, a young girl is bound to a stake. The crowd " +
                "has built a pyre at her feet. \"A witch,\" they say. \"She brings the grey cold " +
                "into our fields.\" Her eyes are dark and frightened — or very still, which is worse. " +
                "The village elder watches you to see what you do.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Let them burn her. She may truly be a witch.", null, true,
                        "The village has made its decision. You agree with it."),
                    new InquiryElement("b", "Watch. You've heard worse ways to spend an afternoon.", null, true,
                        "You watch. You do nothing. This is a choice too."),
                    new InquiryElement("c", "Stop them. There is no proof, only fear.", null, true,
                        "No proof, only fear. Stopping them may not end without consequence."),
                    new InquiryElement("d", "Ride on. You don't have time for this.", null, true,
                        "The road continues."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You say nothing. The fire is lit. She does not scream — or she does, and you have already turned away. " +
                                "The village elder nods at your back as you leave.", DimColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Mercy, -1);
                            Msg("You watch. The crowd watches you watching. " +
                                "Something in you marks this moment and files it under things you have become.", BadColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            if (_rng.NextDouble() < 0.5)
                            {
                                // She was Ashen — casts Curse before dying
                                int w = 5 + _rng.Next(8);
                                WoundPartyTroops(w);
                                try { AgePlayer(3); } catch { }
                                Msg($"You step forward. The crowd parts. The girl raises her head — " +
                                    $"and her eyes are grey. Not frightened. Cold. She speaks one word " +
                                    $"and your soldiers cry out. {w} of them are clutching wounds " +
                                    $"that were not there a moment ago. She was what they said she was.", BadColor);
                            }
                            else
                            {
                                Msg("You step forward. The crowd parts. The girl raises her head — " +
                                    "her eyes are human and wet and terrified. You cut her free. " +
                                    "The elder says nothing. The villagers say nothing. " +
                                    "You ride out with a girl who was not a witch, and the knowledge " +
                                    "of what would have happened if you had kept riding.", GoodColor);
                            }
                            break;
                        case "d":
                            Msg("You ride on. Behind you, the fire takes hold. " +
                                "There was nothing you could have done. You choose not to decide whether that is true.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── EC_LocalPriest — town enter ───────────────────────────────────────
        // A city priest asks for funding to establish a sanctuary here.
        private static void EC_LocalPriest(Settlement s)
        {
            string cName  = s.Name?.ToString() ?? "the city";
            bool   exists = SanctuaryCampaignBehavior.HasSanctuary(s);
            if (exists) return; // already has a sanctuary; skip

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★ The Priest at the Gate",
                $"A worn priest intercepts you at the city gate of {cName}. " +
                $"He speaks quickly — he has been turned away by two lords already. " +
                $"He wants to build a sanctuary here: a place where the honourable can seek " +
                $"blessing, healing, and protection against the Ashen. He needs coin. A great deal of it.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Donate 10,000 denars — build it properly.", null, true,
                        "A serious investment. The sanctuary will be built."),
                    new InquiryElement("b", "Give 5,000 denars — half now, half later.", null, true,
                        "Half the sum. Whether it is enough remains to be seen."),
                    new InquiryElement("c", "Spare 500 denars — something is better than nothing.", null, true,
                        "A small sum. The priest will do what he can with it."),
                    new InquiryElement("d", "Turn him away. You have nothing to give.", null, true,
                        "He was turned away by two lords already. A third changes nothing."),
                    new InquiryElement("e", "Have him beaten and driven off.", null, true,
                        "He will not return. Neither will some who heard what you did."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (!ChangeGold(-10000)) break;
                            SanctuaryCampaignBehavior.AddPermanentSanctuary(s.StringId);
                            Msg($"You give him the coin without ceremony. He bows once and says nothing further. " +
                                $"Within a week, the sanctuary of {cName} is open. " +
                                $"The flame burns clean inside it.", GoodColor);
                            break;
                        case "b":
                            if (!ChangeGold(-5000)) break;
                            if (_rng.NextDouble() < 0.5)
                            {
                                SanctuaryCampaignBehavior.AddPermanentSanctuary(s.StringId);
                                Msg($"Half the sum was enough to begin. The sanctuary of {cName} opens its doors. " +
                                    $"The priest sent you a short note of thanks that says more than it appears to.", GoodColor);
                            }
                            else
                            {
                                Msg($"The coin was not enough to complete the work. The foundations are laid " +
                                    $"but the doors have not opened. The priest writes that he will find the rest.", DimColor);
                            }
                            break;
                        case "c":
                            if (!ChangeGold(-500)) break;
                            if (_rng.NextDouble() < 0.05)
                            {
                                SanctuaryCampaignBehavior.AddPermanentSanctuary(s.StringId);
                                Msg($"You drop a handful of coin into his hands and ride on. A month later, " +
                                    $"word reaches you: the sanctuary of {cName} is open. " +
                                    $"Your small gift opened a door no one expected.", GoodColor);
                            }
                            else
                            {
                                Msg("You give what you can. He thanks you with a quiet dignity that makes the small sum feel smaller. " +
                                    "It was not enough, but it was something.", DimColor);
                            }
                            break;
                        case "d":
                            Msg("You pass him without stopping. He watches you go, then turns back to the gate to wait for the next lord.", DimColor);
                            break;
                        case "e":
                            ShiftTrait(DefaultTraits.Mercy, -1);
                            Msg("Your soldiers scatter him from the gate. He does not return. " +
                                "The city remembers you were there.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── LV_ColdEmbrace — village leave ────────────────────────────────────
        // Resting in the afternoon, a ring of Ashen Spawn closes around you.
        // They reach out the cold and wait.
        private static void LV_ColdEmbrace(Settlement s)
        {
            int   oneH    = Hero.MainHero?.GetSkillValue(DefaultSkills.OneHanded) ?? 0;
            int   twoH    = Hero.MainHero?.GetSkillValue(DefaultSkills.TwoHanded) ?? 0;
            int   best    = Math.Max(oneH, twoH);
            float athChance  = Math.Min(0.90f, 0.35f + (Hero.MainHero?.GetSkillValue(DefaultSkills.Athletics) ?? 0) * 0.003f);
            float combChance = Math.Min(0.90f, 0.35f + best * 0.003f);

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★ The Circle Closes",
                "You are resting in the afternoon shade outside the village when they arrive. " +
                "A ring of Ashen Spawn — grey-cloaked, cold-eyed — has closed around you without a sound. " +
                "They do not speak. They extend their hands toward you, and the air drops ten degrees. " +
                "They are offering you something.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Embrace the cold. Accept what they offer.", null, true,
                        "The cold does not wait for second thoughts."),
                    new InquiryElement("b", $"Run. Get out of the ring. (Athletics {(int)(athChance*100)}%)", null, true,
                        $"Speed may be enough. It may not."),
                    new InquiryElement("c", $"Fight them off. ({(int)(combChance*100)}% with your best blade skill)", null, true,
                        $"Steel still cuts. The odds are what they are."),
                    new InquiryElement("d", "Burn them with magic. Age 3 days.", null, true,
                        "Fire scatters cold things. The cost is paid in years."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            BecomeAshen();
                            Msg("You reach back. The cold is not a sensation — it is a state. " +
                                "The grey settles into your eyes before you are aware it has begun. " +
                                "The Ashen Spawn lower their hands. You are one of them now.", BadColor);
                            break;
                        case "b":
                            if (_rng.NextDouble() < athChance)
                                Msg("You break from the ring at a dead run, low and fast. " +
                                    "One of them reaches — you feel the cold graze your shoulder " +
                                    "and then you are through and moving and they do not follow. " +
                                    "You do not stop running until the village is behind you.", DimColor);
                            else
                            {
                                WoundPlayer();
                                Msg("You move — but not fast enough. The cold finds you before you clear the ring. " +
                                    "You come through bleeding and slow, the grey chill deep in your shoulder. " +
                                    "They let you go. You cannot decide if that makes it better or worse.", BadColor);
                            }
                            break;
                        case "c":
                            if (_rng.NextDouble() < combChance)
                                Msg("You draw and move. They are not afraid of blades — " +
                                    "but blades still cut. You take two down before the others scatter. " +
                                    "Not elegantly, but you come out the other side standing.", GoodColor);
                            else
                            {
                                WoundPlayer();
                                Msg("You were outnumbered, and they moved without fear. " +
                                    "You take wounds before you manage to break the ring. " +
                                    "They let you go when you clear them. You are not sure why.", BadColor);
                            }
                            break;
                        case "d":
                            AgePlayer(3);
                            Msg("The fire comes from somewhere older than your hands. " +
                                "They scatter before it — cold things do not like what burns. " +
                                "They are gone in seconds. You are three days older. " +
                                "Some costs are paid faster than others.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── LV_ColdDream — village leave ──────────────────────────────────────
        // Sleeping at the village inn, the cold reaches into your dreams.
        private static void LV_ColdDream(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★ Ash in the Dream",
                "You slept at the village inn and woke before dawn, cold and certain. " +
                "The dream was vivid: grey plains stretching without horizon, " +
                "something vast and patient moving at the edge of it. " +
                "It looked at you — not with eyes — and extended an invitation. " +
                "You are awake now. The decision feels more real than waking usually does.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Accept the invitation. Join the cold.", null, true,
                        "Invitations from the cold are rarely what they appear to be."),
                    new InquiryElement("b", "Refuse. It was only a dream.", null, true,
                        "It was only a dream."),
                    new InquiryElement("c", "Reach back — try to learn what it wants.", null, true,
                        "Reaching toward the cold is not without risk. What it wants is not nothing."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            BecomeAshen();
                            Msg("You answer the invitation. By morning you are different in ways you cannot fully describe yet. " +
                                "Your reflection in the horse trough shows grey at the edges of your eyes.", BadColor);
                            break;
                        case "b":
                            Msg("You get up, drink cold water, and decide it was only a dream. " +
                                "You are probably right. The world looks normal in daylight. " +
                                "It usually does.", DimColor);
                            break;
                        case "c":
                        {
                            double roll = _rng.NextDouble();
                            if (roll < 0.30)
                            {
                                WoundPlayer();
                                Msg("You reach toward it and it reaches back — harder than you expected. " +
                                    "You come awake on the floor, bleeding from nowhere you can explain. " +
                                    "The dream is gone. The wounds are not.", BadColor);
                            }
                            else if (roll < 0.50)
                            {
                                BecomeAshen();
                                Msg("You reach toward it and it takes you the rest of the way. " +
                                    "You learn what it wants. It wants everything. " +
                                    "By the time you understand that, the grey is already in your eyes.", BadColor);
                            }
                            else
                            {
                                try { Hero.MainHero.HeroDeveloper.UnspentFocusPoints += 1; } catch { }
                                Msg("You reach toward it carefully, like touching something hot from the side. " +
                                    "You pull back before it pulls you in — but you bring something with you: " +
                                    "a clarity, a sense of how things connect. One focus point, " +
                                    "paid for in proximity to something you do not fully understand.", GoodColor);
                            }
                            break;
                        }
                    }
                }, null, "", false), false, true);
        }

        // ── LV_ThreeWitches — village leave ───────────────────────────────────
        // Three figures dance around a fire in the dark at the crossroads.
        private static void LV_ThreeWitches(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★ Three Figures at the Crossroads",
                "Your party crests the hill above the crossroads and stops. " +
                "Below, three women are dancing around a fire that burns colours that do not exist in wood. " +
                "They see you. One raises a hand — an invitation, not a threat. " +
                "The fire is warm from here. Your scouts are very still.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Join them at the fire.", null, true,
                        "The dance is not something you will remember clearly in daylight."),
                    new InquiryElement("b", "Ride past without stopping.", null, true,
                        "The crossroads is quiet behind you."),
                    new InquiryElement("c", "Intervene — scatter the rite.", null, true,
                        "You stop something. They may not go quietly."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            try { AgingSystem.RejuvenateHero(Hero.MainHero, 730); } catch { } // ~2 years
                            ShiftTrait(DefaultTraits.Honor, -2);
                            ShiftTrait(DefaultTraits.Mercy, -2);
                            Msg("You ride down and dismount at the fire. They make space for you without speaking. " +
                                "The dance is not something you will remember clearly in daylight. " +
                                "You feel two years lighter when you leave. You feel heavier in other ways.", DimColor);
                            break;
                        case "b":
                            Msg("You ride past without slowing. One of them watches you go, " +
                                "fire still burning in the hollow of her hand. " +
                                "The crossroads is quiet behind you.", DimColor);
                            break;
                        case "c":
                            try { Hero.MainHero.HeroDeveloper.UnspentFocusPoints += 1; } catch { }
                            if (_rng.NextDouble() < 0.5)
                            {
                                AgePlayer(365); // 1 year
                                Msg("You ride in fast and scatter them — they break and vanish into the dark, " +
                                    "one of them turning as she runs. You feel something land on you, " +
                                    "light and cold: a curse, spoken quickly, without ceremony. " +
                                    "You are one year older. You stopped something, though. " +
                                    "You take the focus point and the year and call it even.", DimColor);
                            }
                            else
                            {
                                Msg("You ride in and scatter them. They vanish without a word. " +
                                    "The fire goes out as you cross it. " +
                                    "Something remains where the fire was — a clarity, a residue of interrupted power. " +
                                    "You take what you can carry. One focus point, ungiven but yours now.", GoodColor);
                            }
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── LV_HollowHour — village leave, general ────────────────────────────
        // The darkest part of the night settles over camp — something unnatural
        // rides in the fog. Three soldiers die if the player sleeps through it.
        private static void LV_HollowHour(Settlement s)
        {
            Hero hero = Hero.MainHero;
            bool mage = MageKnowledge.IsMage;

            float leadChance = SkillChance(DefaultSkills.Leadership, 0.35f);
            string leadHint  = SkillHint(DefaultSkills.Leadership, 0.35f, "Rally them — your voice cuts the fog");

            float athChance  = SkillChance(DefaultSkills.Athletics, 0.35f);
            string athHint   = SkillHint(DefaultSkills.Athletics, 0.35f, "Move fast enough to wake them physically");

            bool prayGate = hero != null
                         && hero.GetTraitLevel(DefaultTraits.Honor) >= 1
                         && hero.GetTraitLevel(DefaultTraits.Mercy) >= 1
                         && (MobileParty.MainParty?.Morale ?? 0f) >= 60f;
            string prayHint = prayGate
                ? "Your faith is unbroken and your men's hearts are high. The prayer holds."
                : "Requires Honourable and Merciful traits and party morale of 60 or above.";

            string mageHint = mage
                ? "Push the fire outward — cold auras cannot hold against it. Costs a day."
                : "Requires mage ability.";

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★ The Hollow Hour",
                "You wake in the deep of night without knowing why. The camp is wrong — " +
                "too quiet, the fires too low, the air thick with cold that has no wind behind it. " +
                "A pale fog has rolled in from nowhere: close, slow, and patient. " +
                "Your men sleep on, but their breathing is shallow and ragged. " +
                "One of them whimpers. Another goes still. This is not weather. " +
                "Something is feeding on the dark and your camp is in the middle of it.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", $"Shout your men awake. ({(int)(leadChance * 100)}% Leadership)", null, true,
                        leadHint),
                    new InquiryElement("b", "Go back to sleep. It is only fog.", null, true,
                        "Fog. Just fog. That is what you tell yourself."),
                    new InquiryElement("c", "Dispel the darkness with fire. (Mage — 1 day)", null, mage,
                        mageHint),
                    new InquiryElement("d", $"Move through camp and shake them awake. ({(int)(athChance * 100)}% Athletics)", null, true,
                        athHint),
                    new InquiryElement("e", "Kneel and pray until the darkness passes.", null, prayGate,
                        prayHint),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Leadership, 0.35f))
                            {
                                AddMorale(5f);
                                Msg("Your voice cuts through the fog before you have fully thought about it — " +
                                    "command instinct, louder than the wrongness. Heads come up around the camp, " +
                                    "eyes unfocused, hands reaching for weapons by habit. " +
                                    "The aura breaks against the noise and the motion. " +
                                    "By the time dawn comes your men are quieter than usual but alive and present. " +
                                    "They don't ask what you drove off. They can tell something was there.", GoodColor);
                            }
                            else
                            {
                                KillPartyTroops(3);
                                AddMorale(-10f);
                                Msg("You shout — but the fog swallows your voice like cloth swallows water. " +
                                    "The words don't land. The men who were already deep in it don't surface. " +
                                    "By dawn three of them are cold. " +
                                    "No wounds. No mark. Just gone quiet in the night while you stood there shouting into nothing.", BadColor);
                            }
                            break;
                        case "b":
                            KillPartyTroops(3);
                            AddMorale(-10f);
                            Msg("You pull your blanket up and close your eyes. " +
                                "The nightmares come without warning — not vivid ones, just a pressure, " +
                                "a slow sense of something settling over you, of breath becoming harder to draw. " +
                                "You wake at dawn to find three men who did not. " +
                                "No wounds. No marks. Just cold. " +
                                "The fog is gone. The rest of your men look at you and say nothing.", BadColor);
                            break;
                        case "c":
                            AgePlayer(1);
                            AddMorale(5f);
                            Msg("You push the fire outward — not a spell, exactly, more a refusal: " +
                                "warmth moving against cold in the way warmth does when it remembers what it is. " +
                                "The fog pulls back in sections, like cloth being peeled from something wet. " +
                                "Gone before the sky lightens. Your men sleep through it entirely. " +
                                "They wake rested, which is unusual on a cold march. " +
                                "You are a day older. You count it worth the cost.", FireColor);
                            break;
                        case "d":
                            if (SkillRoll(DefaultSkills.Athletics, 0.35f))
                            {
                                AddMorale(5f);
                                Msg("You move fast — camp to camp, shoulder to shoulder, " +
                                    "hands on men's arms and boots on their bedrolls. " +
                                    "It is ungraceful and it works. The ones you reach in time surface confused but breathing. " +
                                    "The aura needs stillness; you denied it stillness. " +
                                    "By dawn the fog is thin and ordinary and your camp is intact.", GoodColor);
                            }
                            else
                            {
                                KillPartyTroops(3);
                                AddMorale(-10f);
                                Msg("You move — but the fog is thicker than it looked and the camp is larger than your legs. " +
                                    "You reach most of them. Three you don't reach in time. " +
                                    "They were already too deep in it when you got there. " +
                                    "Cold in their blankets, faces slack, gone quietly in the part of the night that forgets them.", BadColor);
                            }
                            break;
                        case "e":
                            Msg("You kneel at the edge of the camp and pray — not quickly, not as habit, " +
                                "but with the full weight of someone who believes the words mean something. " +
                                "The fog thins. Not fast, but steadily, like something deciding not to stay. " +
                                "Your men sleep on, undisturbed. By first light it is gone. " +
                                "Nothing happened. In the hollow hour, that is the whole point.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── E_EmberTithe — enter village / enter city (mage) ──────────────────
        // An old man asks to bask in your fire, offering knowledge in return.
        private static void E_EmberTithe(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Ember-Tithe",
                "An old man separates from the edge of the road as your party slows — bent, weathered, dressed in something between a travelling coat and a burial shroud. He does not beg. He simply asks. He says he has spent forty years gathering knowledge of fire, of the kind that does not burn wood, and that he has perhaps a season left to him. He asks only to feel the warmth of what you carry, just once, before he faces the cold. In return, he offers what he knows.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Let him stand in it. You can spare the warmth.", null, true,
                        "Warmth given freely returns something. The cost is in days."),
                    new InquiryElement("b", "Refuse. You owe him nothing.", null, true,
                        "He folds his coat and walks away."),
                    new InquiryElement("c", "Seize him. What he knows can be taken without the ceremony.", null, true,
                        "What he knows may be taken. What he kept may not be."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            try { Hero.MainHero.HeroDeveloper.UnspentFocusPoints += 1; } catch { }
                            AgePlayer(30);
                            Msg("He stands in it for a long moment, eyes closed. Then he begins to speak — not quickly, not with ceremony, but as a man unburdening something he has carried for decades. He talks for two hours. When he leaves, you sit with the shape of what he gave you for a long time. One focus point, paid in warmth and thirty days. You call it even.", FireColor);
                            break;
                        case "b":
                            Msg("He nods once when you refuse, as though he expected it. He folds his coat around himself and walks away from the road — not toward any village you can see, just away from the direction you are heading. You do not see where he goes.", DimColor);
                            break;
                        case "c":
                            ChangeRenown(10f);
                            ChangeGold(500);
                            if (_rng.NextDouble() < 0.5)
                            {
                                WoundPlayer();
                                Msg("You take him before he can speak. He does not struggle. He is still through all of it, and when you have what you need he says, clearly, one word — a name, not his, something older. The wound opens in you an hour later, from nowhere, deep and clean as a blade. He kept something back. He kept the part that costs.", BadColor);
                            }
                            else
                            {
                                Msg("You take what he knows, quickly and without ceremony. He gives it without resistance — more than he might have given freely. He is gone before the last of your men pass. What you learned is real. What you paid for it has not arrived yet.", DimColor);
                            }
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── ES7_FallenLaboratory — siege attacker won, mage ───────────────────
        // A sealed chamber reveals a heretical mage's laboratory after capture.
        private static void ES7_FallenLaboratory()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  What the Keep Concealed",
                "Your sergeant reports a sealed chamber discovered behind the keep's lower kitchens — bricked up rather than locked, intended to disappear. Inside: organised notes, apparatus you recognise as capable of real work, and ingredients that would be illegal in three of the five regions you have ridden through. This belonged to the previous lord's personal scholar. The notes are meticulous. The work described in them is heretical by any measure. It is also genuinely interesting.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Study the notes. Knowledge does not become false for being forbidden.", null, true,
                        "Forbidden knowledge is still knowledge. What it costs is another matter."),
                    new InquiryElement("b", "Leave it. It was sealed for reasons you have no need to unpack.", null, true,
                        "It was sealed for reasons. The chamber stays as it is."),
                    new InquiryElement("c", "Burn it. Some work should not outlive the people who made it.", null, true,
                        "Some work should not outlive the people who made it."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            if (_rng.NextDouble() < 0.20)
                            {
                                try { Hero.MainHero.HeroDeveloper.UnspentFocusPoints += 1; } catch { }
                                Msg("You read through most of the night. The scholar was working on the relationship between fire-carrying and certain physical states — not how to create it, but how to deepen it once present. The path they mapped is not one you would have found alone. By morning you understand something you did not understand before. One focus point, bought in hours and the specific unease of learning from someone you cannot question.", FireColor);
                            }
                            else
                            {
                                Msg("You read through most of the night. The scholar was careful, methodical, and working on something genuinely dangerous — not in the explosive sense but in the kind that changes what a person believes is possible. You come away unsettled. Not from the content but from how clearly it was reasoned. The work yields nothing tonight. It may yield something later.", DimColor);
                            }
                            break;
                        case "b":
                            Msg("You have the chamber re-sealed. Whatever was worked here will stay here, in the dark, until the keep is occupied by someone else who finds it and makes the same decision. You have enough decisions already.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You carry the first armful yourself. The notes burn with a faint smell that is not entirely paper, and the apparatus blackens in ways that suggest it already absorbed something it should not have. The chamber is ash by midday. You don't know what was lost. You know what wasn't found.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── EC9_AshenElixir — enter city, general ─────────────────────────────
        // A street alchemist offers a secret of power — for a price.
        private static void EC9_AshenElixir(Settlement s)
        {
            float charmChance = SkillChance(DefaultSkills.Charm, 0.30f);
            string charmHint  = SkillHint(DefaultSkills.Charm, 0.30f, "Intimidate him into speaking");

            void ShowElixirChoice()
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "⚗  The Vial",
                    "He produces it from inside his coat: a small sealed vial, dark and faintly luminescent, the liquid inside not quite settling the way liquid should. He describes the contents — ash-blood drawn from a living Ashen donor, three additional reagents he declines to name, prepared over a fortnight at specific temperatures. He says it will unlock something in whoever drinks it. He says the process is irreversible. He says this as though it is a recommendation.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("a", "Drink it.", null, true,
                            "He says it unlocks something. He says the process is irreversible."),
                        new InquiryElement("b", "Refuse. You've heard enough.", null, true,
                            "The road continues."),
                        new InquiryElement("c", "Report him to the city watch.", null, true,
                            "The watch will be interested. So will others."),
                        new InquiryElement("d", "Beat him and recover your money.", null, true,
                            "Your coin comes back. Something else may leave with it."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen2 =>
                    {
                        switch (chosen2?[0]?.Identifier as string)
                        {
                            case "a":
                            {
                                double roll = _rng.NextDouble();
                                if (roll < 0.50)
                                {
                                    try { Hero.MainHero.HeroDeveloper.UnspentAttributePoints += 2; } catch { }
                                    Msg("The liquid is cold going down and then not cold at all. Your vision whites out briefly. When it returns you are sitting on the cobblestones and your hands are shaking — not from weakness, from something running faster than usual under the surface. You have two attribute points you did not have an hour ago. The alchemist has already left. So has any record of this.", GoodColor);
                                }
                                else if (roll < 0.75)
                                {
                                    BecomeAshen();
                                    Msg("The liquid is cold going down and then colder still. The ash-blood recognises something in you it was looking for. Your reflection in the window across the street is already different — grey at the margins, the eyes beginning to change. The alchemist watches with professional satisfaction and then quietly disappears. You have become something you cannot unbecome.", BadColor);
                                }
                                else
                                {
                                    Msg("The liquid moves wrong in you from the moment it clears your throat. You have time to understand what is happening before you lose the ability to act on it. Your men find you on the street three minutes later, unmoving.", BadColor);
                                    try { KillCharacterAction.ApplyByMurder(Hero.MainHero, null, false); } catch { }
                                }
                                break;
                            }
                            case "b":
                                Msg("You set the vial on his table and leave. He calls after you — something about wasted potential, the usual — but does not follow. The street swallows his voice quickly.", DimColor);
                                break;
                            case "c":
                                ChangeRenown(5f);
                                Msg("You find the nearest city watch officer and describe what you witnessed. The watch takes it seriously — ash-blood preparation is illegal in this city, as in most. They collect him within the hour. His apparatus is confiscated. The vial goes with it. You receive a formal acknowledgement from the watch captain. It will be recorded under your name.", GoodColor);
                                break;
                            case "d":
                                ShiftTrait(DefaultTraits.Mercy, -1);
                                ChangeGold(1000);
                                Msg("You make your position clear without extended negotiation. He returns the coins without being asked twice. He also offers two of the unnamed reagents as an apparent apology, which you did not request and are not certain you want. You leave him on the floor of his shop, breathing, with a considerably reduced opinion of street salesmanship.", DimColor);
                                break;
                        }
                    }, null, "", false), false, true);
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚗  The Alchemist's Promise",
                "A man separates from the crowd near the market gate — not blocking you, just placing himself where you will notice him. He is dressed expensively for someone selling things from a bag. He uses the word 'secret' twice in his opening sentence, which is a technique. He says he has something that will change what you are capable of. He says he can prove it. He says it will cost you one thousand gold to hear the proof.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Refuse. Every man with a secret wants money first.", null, true,
                        "Nothing happens."),
                    new InquiryElement("b", "Agree. Pay the thousand gold and hear him out.", null, true,
                        "Lose 1000 gold. He shows you what he has."),
                    new InquiryElement($"c", $"Threaten him into talking. ({(int)(charmChance * 100)}% Charm)", null, true,
                        charmHint),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            Msg("You ride on. He watches you go with the expression of a man recalculating his approach for the next mark. He will try someone else. That is not your problem.", DimColor);
                            break;
                        case "b":
                            if (!ChangeGold(-1000)) return;
                            ShowElixirChoice();
                            break;
                        case "c":
                            if (SkillRoll(DefaultSkills.Charm, 0.30f))
                            {
                                Msg("You do not raise your voice. You do not need to. He reads the situation correctly and decides that a free demonstration is preferable to the alternative. He is correct.", GoodColor);
                                ShowElixirChoice();
                            }
                            else
                            {
                                ShiftTrait(DefaultTraits.Honor, -1);
                                ChangeCrime(10f);
                                Msg("You push harder than the situation called for. He does not give you what you want — instead he shouts for the watch. You disengage before it escalates further, but not before witnesses have a good look at your face. The city watch receives a complaint. Your criminal rating rises. The alchemist keeps his secret.", BadColor);
                            }
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── Child eligibility check ────────────────────────────────────────────
        private static Hero GetEligibleChild()
        {
            try
            {
                return Hero.AllAliveHeroes.FirstOrDefault(h =>
                    h.IsAlive && !h.IsDisabled && !h.IsPrisoner &&
                    (h.Father == Hero.MainHero || h.Mother == Hero.MainHero) &&
                    h.Age >= 14f && h.Age <= 22f);
            }
            catch { return null; }
        }

        private static bool HasEligibleChild() => GetEligibleChild() != null;

        // ── E_DarkeningInheritance — enter village/city, mage, rare ───────────
        // Something wrong is stirring in a child who is coming of age.
        private static void E_DarkeningInheritance(Settlement s)
        {
            Hero child = GetEligibleChild();
            if (child == null) return;
            _childEventCooldown = 300;

            string childName = child.Name?.ToString() ?? "your child";

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Darkening Inheritance",
                $"You have been watching {childName} for some time now — the way light behaves wrong around them in certain rooms, the cold that has no source, the dreams they won't describe. You have seen this before. Not in yourself, but in others. Something is waking in them, and it is not the fire you carry. It is the other thing. The cold thing. The thing that undoes people from the inside.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", $"Ignore it. {childName} is your child and you love them.", null, true,
                        "Perhaps you are wrong. Perhaps it will pass. Perhaps it is simply waiting."),
                    new InquiryElement("b", $"Isolate {childName} and watch closely.", null, true,
                        $"They will know what you are doing. The cold may slow. It may not stop."),
                    new InquiryElement("c", $"End it. Kill {childName} before they become something they cannot come back from.", null, true,
                        $"You do it yourself. You do not ask anyone else to carry it."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (_rng.NextDouble() < 0.70)
                            {
                                Msg($"You watch {childName} and see nothing further — at least nothing you cannot explain away. Perhaps you were wrong. Perhaps the cold chose someone else. Perhaps it is simply waiting. You do not sleep well, but you wake with them still there, and still yours.", DimColor);
                            }
                            else
                            {
                                try
                                {
                                    ColourLordRegistry.SetAshen(child, true);
                                    ColourLordRegistry.SetMage(child, true);
                                    try { AshenCitySystem.ApplyAshenPersonality(child); } catch { }
                                    try { MageKnowledge.ApplyAshenAppearance(child); } catch { }
                                    // Try to move child to a random Ashen clan
                                    var ashenClans = Clan.All
                                        .Where(c => c != Clan.PlayerClan && c.IsEliminated == false &&
                                               c.Heroes.Any(h => ColourLordRegistry.IsAshenLord(h)))
                                        .ToList();
                                    if (ashenClans.Count > 0)
                                    {
                                        var targetClan = ashenClans[_rng.Next(ashenClans.Count)];
                                        try { child.Clan = targetClan; } catch { }
                                    }
                                }
                                catch { }
                                Msg($"You did nothing. You told yourself it would pass. By the time you admit it is not passing, {childName} is already different — the grey in their eyes unmistakable, the cold they carry no longer concealable. They leave before you make a decision. You hear their name spoken by people you would rather not share a name with.", BadColor);
                            }
                            break;
                        case "b":
                            try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, child, -50, false); } catch { }
                            ShiftTrait(DefaultTraits.Mercy, -1);
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            if (_rng.NextDouble() < 0.30)
                            {
                                try { KillCharacterAction.ApplyByMurder(child, null, false); } catch { }
                                Msg($"You watch, and what you watch for happens. The infection runs faster in isolation — the cold needs no kindness to spread, and it does not need the door to be open. {childName} does not survive it. You kept them from becoming what they were becoming. You are not sure what the distinction is worth.", BadColor);
                            }
                            else
                            {
                                Msg($"You isolate {childName} and you watch. They know what you are doing. They may not survive knowing it, but they survive. The cold slows. Whether it stops is a question that will not answer itself quickly. {childName} looks at you differently now. So do you.", DimColor);
                            }
                            break;
                        case "c":
                            try
                            {
                                Hero.MainHero.SetTraitLevel(DefaultTraits.Mercy, -2);
                                Hero.MainHero.SetTraitLevel(DefaultTraits.Calculating, 2);
                                Hero.MainHero.HeroDeveloper.UnspentFocusPoints += 1;
                                KillCharacterAction.ApplyByMurder(child, Hero.MainHero, false);
                            }
                            catch { }
                            Msg($"You do it yourself. You do not ask anyone else to carry it. The cold that was building in {childName} releases at the end — you feel it disperse, formless, looking for somewhere else to go. It does not find you. You stand with what you did and you do not look away from it. One focus point, paid in full.", DarkColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── LC_BloodCollector — leave city, mage ──────────────────────────────
        // A strange man offers gold for a single drop of the player's fire-blood.
        private static void LC_BloodCollector(Settlement s)
        {
            float charmChance = SkillChance(DefaultSkills.Charm, 0.30f);
            string charmHint  = SkillHint(DefaultSkills.Charm, 0.30f, "Read him and press him");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚗  The Collector",
                "A man intercepts you near the gate with the specific body language of someone who has been waiting. He introduces himself as an alchemist and researcher. He says he is studying the properties of blood in people who carry unusual gifts — your kind, specifically. He wants one drop. He offers a thousand gold for the inconvenience. He says the research is academic. He says the results are for publication. He says a lot of things.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Refuse. You don't trust him.", null, true,
                        "You refuse. He accepts this with practiced ease."),
                    new InquiryElement($"b", $"Refuse — but question him first. ({(int)(charmChance * 100)}% Charm)", null, true,
                        charmHint),
                    new InquiryElement("c", "Accept. One drop costs you nothing.", null, true,
                        "One drop. He pays. You ride on a little less certain than before."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You refuse without elaboration. He accepts the refusal with the practiced ease of someone who receives many refusals. He does not follow. He does not look surprised. You put two streets between you and him before you stop thinking about the specific way he stood while he was listening.", DimColor);
                            break;
                        case "b":
                            if (SkillRoll(DefaultSkills.Charm, 0.30f))
                            {
                                ChangeRenown(10f);
                                Msg("You hold the conversation open a little longer than he planned for. He has a way of redirecting questions that is too practiced — not a scholar's deflection, something more deliberate. You push on the specific wording of 'research' until it shifts. By the time he realises you have found the seam in his story, you have enough to take to the city's intelligence contact. You do not see him again after that. You gain the credit without the credit specifying exactly what it was for.", GoodColor);
                            }
                            else
                            {
                                Msg("You push on his answers and find nothing that clearly breaks. Either he is what he says or he is very good at being what he says. He thanks you for your time and leaves first. You are left with the doubt, which is its own answer but not one you can act on.", DimColor);
                            }
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Calculating, -1);
                            ChangeGold(1000);
                            _bloodTitheCountdown = 3;
                            Msg("One drop. He collects it with practiced care into a sealed vial and pays you without haggling. He thanks you, names no institution, and leaves the way he came. You ride on a thousand gold heavier and slightly less certain than you were an hour ago.", GoldColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── Deferred: FireBloodTitheConsequence — 3 days after LC_BloodCollector C ──
        private static void FireBloodTitheConsequence()
        {
            if (MageKnowledge._deferredInquiry != null) { _bloodTitheCountdown = 1; return; }
            if (_rng.NextDouble() < 0.5)
            {
                MageKnowledge._deferredInquiry = () =>
                {
                    WoundPlayer();
                    AgePlayer(365);
                    MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                        "★  Spectral Pain",
                        "Three days since the alchemist took a drop of your blood. It arrives in the night — not a wound but something working through the fire you carry, using the drop as a thread to reach back through. Your body registers it as pain before your mind can name it. You come awake on the floor of whatever inn you are in, the fire inside doing something involuntary, and the sensation is exactly what it feels like when something reaches into the core of you and pulls.",
                        new List<InquiryElement>
                        {
                            new InquiryElement("ok", "Endure it.", null, true,
                                "Whatever he did with the drop, he did it with intent."),
                        },
                        false, 1, 1, "Endure", "",
                        _ => Msg("Whatever he did with the drop, he did it with intent. You carry the wound and the year and the knowledge that the blood meant something to someone who knew what to do with it.", BadColor),
                        null, "", false), false, true);
                };
            }
            else
            {
                MageKnowledge._deferredInquiry = () =>
                    Msg("Three days since the alchemist took a drop of your blood. Whatever he intended, it has not arrived — or has not arrived yet, or was not intended for you. The fire you carry feels the same. You note it and ride on.", DimColor);
            }
        }

        // ── EC_TavernStranger — enter city, general ───────────────────────────
        // An attractive stranger at the city tavern opens a conversation.
        private static void EC_TavernStranger(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "♦  An Evening in the City",
                "The tavern is busy enough that you have a corner to yourself, or nearly. A person across the room has been watching you since you sat down — attractive, unhurried, clearly aware that you have noticed them noticing you. They cross the room at the pace of someone who is confident in the outcome. They lean against the table and say something that could mean several things, depending on which answer you give.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Tell them to go away.", null, true,
                        "A clear answer. They read it correctly."),
                    new InquiryElement("b", "Flirt back.", null, true,
                        "See where it goes."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You give them the short version — not hostile, just clear. They read it correctly and withdraw with the grace of someone who does this regularly and does not take it personally. You finish your drink in peace.", DimColor);
                            break;
                        case "b":
                        {
                            ShiftTrait(DefaultTraits.Calculating, -1);
                            bool isMale = !(Hero.MainHero?.IsFemale ?? false);
                            int outcome = _rng.Next(4) + 1; // 4 outcomes for both genders

                            switch (outcome)
                            {
                                case 1:
                                    // Spend the night — charm roll for morale (all genders, no extra consequence)
                                    if (SkillRoll(DefaultSkills.Charm, 0.45f))
                                    {
                                        AddMorale(10f);
                                        Msg("The evening goes well by any measure. The conversation outlasts the candles. The morning is ordinary and the better for it. Your mood is noticeably improved when you ride out.", GoodColor);
                                    }
                                    else
                                        Msg("The evening is pleasant without being remarkable. Something in the timing was slightly off. You part amicably. Your men notice you are in an average mood, which is better than most mornings.", DimColor);
                                    break;

                                case 2:
                                    // Robbed in the night
                                    ChangeGold(-100);
                                    Msg("You wake in the early morning to the sound of the door closing carefully. Your purse is 100 coins lighter. They were good at it — no mess, no drama, just a professional taking advantage of a distraction. You dress without hurrying and decide not to mention it.", BadColor);
                                    break;

                                case 3:
                                    // Attempted murder in your sleep — athletics roll
                                    if (SkillRoll(DefaultSkills.Athletics, 0.40f))
                                    {
                                        ChangeRenown(5f);
                                        Msg("Something wakes you — a shift in weight, a sound wrong by half a second. You are moving before you are fully awake, which is the only reason you are still alive. The knife misses. What follows is brief and decisive. Word of the incident finds its way to certain circles before you have left the city.", GoodColor);
                                    }
                                    else
                                    {
                                        WoundPlayer();
                                        Msg("You wake to pain, already bleeding. The knife did its work before your body registered what was happening. You live — they misjudged the angle — but it costs you. You are treated, bandaged, and ride out wounded, with a considerable revision of your judgment of people.", BadColor);
                                    }
                                    break;

                                case 4:
                                    // Male: spend the night + baby in a year. Female: spend the night + pregnant in 30 days.
                                    if (SkillRoll(DefaultSkills.Charm, 0.45f))
                                    {
                                        AddMorale(10f);
                                        Msg("The evening is genuinely good — the kind you do not expect in a city tavern and will not easily explain to anyone who asks. The morning is easy. You ride out in a better mood than you have been in for some time. You do not think much about it. Not yet.", GoodColor);
                                    }
                                    else
                                        Msg("The evening is warm enough. Not remarkable, but real. You part before dawn. It is the kind of night that does not leave a scar. You think about it briefly on the road and then stop thinking about it.", DimColor);
                                    if (isMale)
                                        _babyEventCountdown = 365;
                                    else
                                        _pregnancyCountdown = 30;
                                    break;
                            }
                            break;
                        }
                    }
                }, null, "", false), false, true);
        }

        // ── Deferred: FireBabyConsequence — 365 days after EC_TavernStranger outcome 4 ──
        private static void FireBabyConsequence()
        {
            if (MageKnowledge._deferredInquiry != null) { _babyEventCountdown = 1; return; }

            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "♦  Word from the Road",
                    "A letter finds you through three different couriers, each one more indirect than the last. The handwriting is careful but unpracticed. A woman you spent a night with nearly a year ago has had your child. She is not demanding anything specific. She is informing you of the situation in precise terms and waiting to see what you do with the information.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("a", "Ignore it. You have no obligations here.", null, true,
                            "You set the letter aside without reading the second half."),
                        new InquiryElement("b", "Send money. That is all you can offer from here.", null, true,
                            "Coin through couriers. It is the form of adequate you can manage from here."),
                        new InquiryElement("c", "Acknowledge the child. Bring them into your household.", null, true,
                            "You acknowledge the child. What follows is yours to carry."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        switch (chosen?[0]?.Identifier as string)
                        {
                            case "a":
                                ShiftTrait(DefaultTraits.Mercy, -1);
                                Msg("You set the letter aside without reading the second half. Whatever she decides to do with that is hers. You ride on without writing back. The courier system that found you will not find you again on this — you made sure of it.", DimColor);
                                break;
                            case "b":
                            {
                                int gold = Hero.MainHero?.Gold ?? 0;
                                int amount = Math.Max(500, (int)(gold * 0.05f));
                                ChangeGold(-amount);
                                Msg($"You send {amount} gold through the same chain of couriers in reverse. The letter you include is short. You do not know if it is adequate. You suspect it is not, but it is the form of adequate that you can manage from this distance.", GoldColor);
                                break;
                            }
                            case "c":
                                TryAdoptIllicitChild();
                                break;
                        }
                    }, null, "", false), false, true);
            };
        }

        // ── Helper: adopt illicit child into player clan ───────────────────────
        private static void TryAdoptIllicitChild()
        {
            try
            {
                // Find a non-hero CharacterObject from the player's culture for the template
                CharacterObject template = CharacterObject.All
                    .FirstOrDefault(c => c != null && !c.IsHero
                                     && c.Culture == Hero.MainHero.Culture);
                if (template == null)
                    template = CharacterObject.All.FirstOrDefault(c => c != null && !c.IsHero);

                Settlement birthPlace = Hero.MainHero.HomeSettlement
                    ?? Settlement.All.FirstOrDefault(se => se != null && se.IsTown);

                if (template != null && birthPlace != null)
                {
                    Hero child = HeroCreator.CreateChild(template, birthPlace, Clan.PlayerClan, 1);
                    if (child != null)
                    {
                        try { child.Father = Hero.MainHero; } catch { }
                        PenaliseSpouseForAdoption();
                        Msg($"{child.Name} arrives in your household — quiet, small, and entirely unaware of the circumstances. They are yours now, in whatever sense you decide that means.", GoodColor);
                        return;
                    }
                }
                // Fallback if hero creation fails
                PenaliseSpouseForAdoption();
                Msg("You arrange for the child to be brought to your household through a chain of trusted intermediaries. There is no formal record, but it is done. They are yours.", GoodColor);
            }
            catch
            {
                PenaliseSpouseForAdoption();
                Msg("You arrange for the child to be brought into your household. They arrive. They are yours.", GoodColor);
            }
        }

        // ── EB_TrinketEmberShard — enter settlement, general, no active trinket ──
        // A fragment of amber found on a corpse. Warm to the touch through a gauntlet.
        private static void EB_TrinketEmberShard(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◈  The Ember Shard",
                "Among the enemy dead, you find him — the one who had no reason to carry what he carried. Inside his breastplate, tucked against the lining: a fragment of amber the size of a thumb. Something is suspended inside it, too small to name. What you notice first is not the shape but the warmth. From inside sealed armour, on a dead man, through your gauntlet: warmth that has no right to be there.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Take it.", null, true, ""),
                    new InquiryElement("b", "Leave it.", null, true, ""),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            _trinketVariant   = 1;
                            _trinketPhase     = 1;
                            _trinketCountdown = 3;
                            Msg("You close your gauntlet over it. The warmth doesn't fade when you ride on.", DarkColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You step back and keep walking. For a few strides the warmth seems to follow you. Then it does not.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── EB_TrinketBlindEye — enter settlement, general, no active trinket ────
        // A small iron medallion with an eye etched on both sides. One closed, one open.
        private static void EB_TrinketBlindEye(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◈  The Blind Eye",
                "A small iron medallion, caught in the buckle of a dead man's belt. Black with age. On one side: an eye, etched with a precision that belongs to a different tradition than anything else this man was carrying. The eye is closed. You turn it over. On the reverse, the same eye. Open.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Take it.", null, true, ""),
                    new InquiryElement("b", "Leave it.", null, true, ""),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            _trinketVariant   = 2;
                            _trinketPhase     = 1;
                            _trinketCountdown = 3;
                            Msg("You turn it over twice more. Closed. Open. You put it in your coat and ride on.", DarkColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You set it back against the buckle. You don't look back. The open side faces the sky.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── EB_TrinketPaleCompass — enter settlement, general, no active trinket ─
        // A carved bone disc that settles on a fixed bearing regardless of how it is held.
        private static void EB_TrinketPaleCompass(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◈  The Pale Compass",
                "Inside the lining of a dead man's coat, stitched there with deliberate care: a disc of carved bone, the size of a coin. The face is engraved with radial lines like a compass rose with no directions marked. You set it on your palm to examine it. It rotates. Slowly, precisely, it settles on a bearing. You turn your hand. It corrects. There is no magnet. There is no mechanism you can find.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Take it.", null, true, ""),
                    new InquiryElement("b", "Leave it.", null, true, ""),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            _trinketVariant   = 3;
                            _trinketPhase     = 1;
                            _trinketCountdown = 3;
                            Msg("The bone is warm when you close your fingers over it. The bearing holds.", DarkColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You tuck it back into the lining as you found it. The disc keeps its bearing all the way back to where the coat lies flat.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── Deferred: FireTrinketStage — dispatches first dream or recurring dream ──
        private static void FireTrinketStage()
        {
            if (MageKnowledge._deferredInquiry != null) { _trinketCountdown = 1; return; }
            if (_trinketPhase == 1)
                MageKnowledge._deferredInquiry = FireTrinketFirstDream;
            else if (_trinketPhase == 2)
                MageKnowledge._deferredInquiry = FireTrinketRecurringDream;
        }

        // ── Deferred: FireTrinketFirstDream — 3 days after picking up a trinket ────
        private static void FireTrinketFirstDream()
        {
            string title, desc, choiceA, choiceB, choiceC;
            switch (_trinketVariant)
            {
                case 2:
                    title   = "◈  The Eye Opens";
                    desc    = "Both sides of the medallion have the same eye in the dream. Both are open. There is nothing behind them — not darkness exactly, but the specific quality of attention that has no object. It is watching you the way a locked door watches a room. You wake with the clear sense that something in your belongings is oriented toward you.";
                    choiceA = new[]{ "Hold its gaze.", "Look back at it.", "Don't look away." }[_rng.Next(3)];
                    choiceB = new[]{ "Keep still. Let it watch.", "Don't acknowledge it.", "Hold still. Don't engage." }[_rng.Next(3)];
                    choiceC = new[]{ "Leave it at dawn.", "Throw it out at first light.", "Get rid of it in the morning." }[_rng.Next(3)];
                    break;
                case 3:
                    title   = "◈  A Direction";
                    desc    = "You are in a dark place and the compass is in your hand. Every direction looks identical except one. The disc has settled on a bearing and what is in that direction is not light exactly, but what light might look like if it could form intentions. The pull is as large as you can imagine and no larger. You wake with your hand closed around nothing and the bearing still vivid in your mind.";
                    choiceA = new[]{ "Follow the bearing.", "Step toward it.", "Go in that direction." }[_rng.Next(3)];
                    choiceB = new[]{ "Stay where you are.", "Don't follow. Hold your position.", "Hold still. Don't engage." }[_rng.Next(3)];
                    choiceC = new[]{ "Lose it in the morning.", "Leave it on the road at dawn.", "Get rid of it in the morning." }[_rng.Next(3)];
                    break;
                default:
                    title   = "◈  Something in the Amber";
                    desc    = "You dream of flame suspended in resin — perfectly still, not consuming, not going out. In the dream, you reach toward it. It turns toward you. The warmth in that direction is very old. You wake with your hand extended and the smell of resin in your nose.";
                    choiceA = new[]{ "Reach toward it.", "Open your hand toward it.", "Let it come closer." }[_rng.Next(3)];
                    choiceB = new[]{ "Hold still. Don't engage.", "Don't move. Wait it out.", "Keep your hand away." }[_rng.Next(3)];
                    choiceC = new[]{ "Get rid of it in the morning.", "Throw it at first light.", "Be done with it at dawn." }[_rng.Next(3)];
                    break;
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                title, desc,
                new List<InquiryElement>
                {
                    new InquiryElement("a", choiceA, null, true, ""),
                    new InquiryElement("b", choiceB, null, true, ""),
                    new InquiryElement("c", choiceC, null, true, ""),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Calculating, -1);
                            try { Hero.MainHero.HeroDeveloper.UnspentFocusPoints += 1; } catch { }
                            ChangeRenown(10f);
                            _trinketPhase     = 2;
                            _trinketCountdown = 7;
                            Msg("You reach. Something in the warmth extends toward you in return. You feel the contact as a jolt through the hand and through whatever the fire inside you is. When you wake, you are shaking, and there is one more thing you know how to do. You're not sure where the knowledge came from.", FireColor);
                            break;
                        case "b":
                            _trinketPhase     = 2;
                            _trinketCountdown = 7;
                            Msg("You hold still in the dream until it passes. You wake ordinary and cold. The object is where you left it.", DimColor);
                            break;
                        case "c":
                            _trinketPhase   = 0;
                            _trinketVariant = 0;
                            Msg("You throw it in the morning, as far and as deliberately as you can. You don't look for where it lands. You ride out lighter.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── Deferred: FireTrinketRecurringDream — every 7 days while trinket is held ─
        private static void FireTrinketRecurringDream()
        {
            string title, desc, throwLabel, useLabel, throwMsg, successMsg, ageMsg, deathMsg;
            switch (_trinketVariant)
            {
                case 2: // Blind Eye
                    title = new[]{ "◈  The Eye Again", "◈  Still Watching", "◈  What the Eye Learned" }[_rng.Next(3)];
                    desc = new[]{
                        "The dream returns. The iron eye is waiting. It is more open than before, if that has a meaning. The attention behind it has been watching you for seven days straight and has learned something from the observation. You have options now that you did not have at the start.",
                        "The dream comes again. The eye in the medallion is as you remember it, but the quality of its attention has changed — less like a gaze, more like recognition. Seven days of being watched and it has arrived at a conclusion. It is ready to show you what that conclusion is. Or you can stop now.",
                        "You find yourself in the dream with the medallion in your palm. The eye on both sides is open, as it always is, but now it is watching something specific: you. Not your location, not your face — the thing under those. Seven days of study and it has learned something you did not mean to teach."
                    }[_rng.Next(3)];
                    throwLabel = new[]{ "Throw it away. Enough.", "Cover it and leave it behind.", "Stop. Walk away from the eye." }[_rng.Next(3)];
                    useLabel   = new[]{ "Use it.", "Meet its gaze.", "Look back into it." }[_rng.Next(3)];
                    throwMsg   = new[]{
                        "You find a river and throw it as far as the current will take it. You don't watch where it goes. You ride on without looking back. The dreams stop.",
                        "You bury it at a crossroads in the dark, deep enough that frost won't shift it. The sense of being observed lifts with each shovelful of earth. You ride out. The feeling is gone by noon. The dreams stop.",
                        "You wrap it in cloth and leave it on the threshold of a temple — whatever god watches this place can have the watching. You don't go back. The feeling of attention fades slowly, then all at once. You had not realized how constant it was until it wasn't."
                    }[_rng.Next(3)];
                    successMsg = new[]{
                        "You meet the eye and don't look away. The attention intensifies until it is the only thing in the dream. Then it gives you something — a current of influence, recognition from quarters you did not cultivate, a weight of gold arriving by paths you didn't arrange. The eye closes. You wake with the feeling that you paid something you haven't noticed missing yet.",
                        "The eye narrows — focused attention becoming focused gift. Something passes through the iris like light through a crack and lands in you. You wake to find three men who owed you favors have paid without being asked. Coin in your purse that wasn't there. The medallion is warm in your pocket. You turn it over. Both eyes are still open. Both sides are still watching.",
                        "You hold the gaze without flinching for what feels like the whole of the night. At the end of it the attention does not leave, but its character changes — from scrutiny to endorsement. You wake to influence flowing from quarters that had no reason to give it, gold by paths you didn't open, and a steadiness in the camp's regard that has no obvious cause. The eye has decided something in your favour."
                    }[_rng.Next(3)];
                    ageMsg = new[]{
                        "The eye opens all the way. The attention that has been watching you arrives all at once. The years go first — fifty of them, drawn out through you in a single second. You do not have time to regret the decision. You wake old in the way that is permanent, and the medallion in your pocket is cold iron now, nothing more.",
                        "The gaze becomes total. You feel it pass through you like a census — cataloguing what is there and marking what is owed. The debt is paid in years. Fifty of them leave in a breath. You wake with hands that are slower and a face you do not immediately recognize. The medallion in your pocket has both eyes closed now. You turn it over. Both sides. Closed."
                    }[_rng.Next(2)];
                    deathMsg = new[]{
                        "The eye opens fully and you see what is behind it. You were not supposed to survive this. The attention was always this size. You had simply not understood how small you were standing in front of it.",
                        "The attention turns absolute. You understand, at the moment it becomes too late, that you were never looking at the eye — the eye was looking at whatever is behind you, and what is behind you has no interest in leaving you intact."
                    }[_rng.Next(2)];
                    break;
                case 3: // Pale Compass
                    title = new[]{ "◈  The Bearing Again", "◈  Still Pointing", "◈  The Same Direction" }[_rng.Next(3)];
                    desc = new[]{
                        "The dream returns. The compass is in your hand and the bearing is the same bearing it always is. What is in that direction has not moved. It has only become clearer that it is aware of you now — aware that you found it, aware that you have been carrying it. Seven days. You have options.",
                        "The dream comes again and the compass comes with it, warm and precise in your palm. The bearing has not changed by a degree. But the quality of what it points toward has changed — it is attentive now in a way it was not at the start. Seven days of carrying the compass and what is at the end of the bearing has had time to notice. It is ready.",
                        "You find yourself in the dream with the compass settled in your hand, its rose fixed on the same direction it has held for seven days. What is at the end of that bearing has been patient — patient the way something is patient when it has no reason to hurry. The question now is whether you follow."
                    }[_rng.Next(3)];
                    throwLabel = new[]{ "Throw it away. Enough.", "Bury it. Stop following.", "Set it down and walk away." }[_rng.Next(3)];
                    useLabel   = new[]{ "Use it.", "Follow the bearing.", "Go where it points." }[_rng.Next(3)];
                    throwMsg   = new[]{
                        "You find a river and throw it as far as the current will take it. You don't watch where it goes. You ride on without looking back. The dreams stop.",
                        "You drop it into a gorge on the high road — watch it turn in the air until you can't see it anymore. The bearing it held was pointing somewhere behind you by then. You ride the other direction. The dreams stop.",
                        "You set it on a stone in an empty field and walk away without picking it up. The rose was still pointing its usual bearing when you last looked. You keep walking. The dreams stop."
                    }[_rng.Next(3)];
                    successMsg = new[]{
                        "You follow the bearing. In the dream it takes you somewhere real enough that you remember it on waking — a room, a face, an exchange that settled three separate debts in your favour. The influence flows in from directions you didn't solicit. Gold arrives by paths you didn't arrange. The compass lies still in your pocket, facing its usual direction.",
                        "The bearing leads you somewhere in the dream and you arrive. There is a transaction waiting — impersonal, precise, already arranged on your behalf by something with long reach. Three favors consolidated. Gold shifted into your name. Influence moving like water finding its level. You wake with the compass still pointing. It has not yet decided it is done with you.",
                        "You follow the rose to its conclusion and find what the compass has been promising: not a place, but an arrangement. A favourable redistribution of exactly the resources that matter. You wake with gold that wasn't there and goodwill you didn't earn by visible means, and the compass in your pocket pointing its usual bearing, patient and unremarkable."
                    }[_rng.Next(3)];
                    ageMsg = new[]{
                        "You reach the end of the bearing. What is there takes what it is owed. Fifty years, precise to the day. You feel each one of them leave. You wake in an older body, the compass still in your hand, the rose still pointing nowhere you can follow now. It is, in every sense, spent.",
                        "The bearing terminates. You arrive. What meets you there is not hostile — it is merely exact. An accounting was kept and now it is settled. Fifty years, measured and drawn. You wake with grey in your hair and a weight in your joints that will not leave. The compass in your hand points at nothing now. The rose has gone still."
                    }[_rng.Next(2)];
                    deathMsg = new[]{
                        "You arrive at the end of the bearing. What is there is not what you imagined. The compass was not guiding you. It was leading you.",
                        "The bearing ends. What is at the end of it is not geography. The compass was not measuring direction — it was measuring distance to something that does not announce itself. You arrive. You understand, in a final instant of clarity, what you have been carrying toward."
                    }[_rng.Next(2)];
                    break;
                default: // Ember Shard
                    title = new[]{ "◈  The Amber Again", "◈  The Flame Returns", "◈  Something Waiting" }[_rng.Next(3)];
                    desc = new[]{
                        "The dream returns. The flame in the resin is brighter than before. It knows you now — or has learned to expect you. The warmth extends outward with more precision. You could let it in further. You could put the shard somewhere it would never be found. You have been carrying it for seven days and counting.",
                        "You dream again. The amber is unchanged — still, translucent, the suspended thing inside it unmoved. But the warmth now has a direction. It is no longer radiating outward. It is orienting toward you specifically. Seven days of carrying it and it has learned your particular temperature. You have a choice to make.",
                        "The shard is in your hand in the dream. The flame inside it has grown — not larger, but denser. More certain. It presses against the amber walls from inside as if the resin is a consideration, not a boundary. You have been carrying it for seven days. It has been carrying the same question the whole time."
                    }[_rng.Next(3)];
                    throwLabel = new[]{ "Throw it away. Enough.", "Put it somewhere it won't be found.", "Be done with it. Drop it in the river." }[_rng.Next(3)];
                    useLabel   = new[]{ "Use it.", "Let it in.", "Open your hand again." }[_rng.Next(3)];
                    throwMsg   = new[]{
                        "You find a river and throw it as far as the current will take it. You don't watch where it goes. You ride on without looking back. The dreams stop.",
                        "You leave it in a ditch at the roadside, pushed into the mud far enough that no casual eye will find it. The warmth that followed you lingers for half a day. Then it is gone. The dreams stop.",
                        "You drop it into a well at dusk — listen for the sound of it striking water below. There is no sound. You ride on. The warmth you carried for seven days is simply gone, and you notice its absence the way you notice a tooth when it stops hurting."
                    }[_rng.Next(3)];
                    successMsg = new[]{
                        "You open your hand in the dream and let the warmth come the rest of the way in. It passes through you like a tide: slow, total, indifferent to your comfort. When you wake, your purse is somehow heavier, three lords who have never spoken well of you have revised their estimate, and the fire you carry burns with a steadier quality. You don't know how to explain any of it. You don't try.",
                        "The shard pulses once, twice — a heartbeat that isn't yours. Something passes between you in the dream, a transaction with no words. You wake with ash on your fingers and a stranger's debt settled in your name. Gold finds you before noon. Morale in the camp runs higher than the weather warrants. The shard in your pocket is warm but ordinary-looking. Nobody else feels it.",
                        "You hold nothing back in the dream and the warmth holds nothing back in return. It is like standing too close to a forge — your skin does not burn but you understand what burning is. When you wake, three favors have been called in overnight by no one you instructed. Coin arrives. The camp's temper steadies. The shard sits quiet in your coat, waiting."
                    }[_rng.Next(3)];
                    ageMsg = new[]{
                        "The warmth comes all the way in. You let it. For a moment you think it is good. Then the years begin to run. Fifty of them. Not taken — spent, which is different. You wake gasping, and the face looking back at you from still water is the face of someone who came to this late and paid for the privilege. The shard is cold. It won't warm again.",
                        "The warmth comes all the way in. The amber cracks in the dream — hairline fractures that spread until the whole piece is a web of them. You understand what is being traded before it is finished. Fifty years, drawn precisely through you like thread through a needle. You wake with grey where there wasn't grey and a weight in your joints that will not leave. The shard in your pocket is just amber now."
                    }[_rng.Next(2)];
                    deathMsg = new[]{
                        "The warmth comes all the way in and keeps coming. You understand, in the last moment, that it was never warmth — it was appetite. The fire inside you feeds it until there is nothing left to feed with. You do not wake.",
                        "You let it in completely and it does not stop. The warmth becomes heat becomes the specific temperature of something feeding. The last clear thought you have is that you were the fuel the whole time. You do not wake."
                    }[_rng.Next(2)];
                    break;
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                title, desc,
                new List<InquiryElement>
                {
                    new InquiryElement("a", throwLabel, null, true, ""),
                    new InquiryElement("b", useLabel,   null, true, ""),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            _trinketPhase   = 0;
                            _trinketVariant = 0;
                            Msg(throwMsg, DimColor);
                            break;
                        case "b":
                        {
                            double roll = _rng.NextDouble();
                            if (roll < 0.70)
                            {
                                // Success — significant gains, chain continues
                                _trinketCountdown = 7;
                                try { if (Hero.MainHero?.Clan != null) Hero.MainHero.Clan.Influence += 60f; } catch { }
                                ChangeRenown(50f);
                                ChangeGold(2000);
                                AddMorale(15f);
                                Msg(successMsg, GoodColor);
                            }
                            else if (roll < 0.90)
                            {
                                // Age 50 years — ends chain
                                _trinketPhase   = 0;
                                _trinketVariant = 0;
                                AgePlayer(50 * 365);
                                Msg(ageMsg, BadColor);
                            }
                            else
                            {
                                // Instant death — ends chain
                                _trinketPhase   = 0;
                                _trinketVariant = 0;
                                Msg(deathMsg, BadColor);
                                try { KillCharacterAction.ApplyByMurder(Hero.MainHero, null, false); } catch { }
                            }
                            break;
                        }
                    }
                }, null, "", false), false, true);
        }

        // ── Deferred: FirePregnancyConsequence — 30 days after female-player outcome 1 ──
        private static void FirePregnancyConsequence()
        {
            if (MageKnowledge._deferredInquiry != null) { _pregnancyCountdown = 1; return; }
            MageKnowledge._deferredInquiry = () =>
            {
                try { MakePregnantAction.Apply(Hero.MainHero); } catch { }
                Hero husband = Hero.MainHero?.Spouse;
                if (husband != null && husband.IsAlive)
                {
                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, husband, -50, false); } catch { }
                    Msg($"You are with child — and {husband.Name} knows it is not his. ({husband.Name} −50 relation)", BadColor);
                }
                else
                    Msg("You are with child. The road ahead looks different than it did a month ago.", DimColor);
            };
        }

        private static void PenaliseSpouseForAdoption()
        {
            try
            {
                Hero spouse = Hero.MainHero.Spouse;
                if (spouse == null || !spouse.IsAlive) return;
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, spouse, -20, false);
                Msg($"({spouse.Name} −20 relation)", BadColor);
            }
            catch { }
        }

        // ── Condition helpers ──────────────────────────────────────────────────
        private static bool HasSpouseAndChild()
        {
            try
            {
                if (Hero.MainHero?.Spouse == null || !Hero.MainHero.Spouse.IsAlive) return false;
                return Hero.AllAliveHeroes.Any(h =>
                    h.IsAlive && !h.IsDisabled &&
                    (h.Father == Hero.MainHero || h.Mother == Hero.MainHero));
            }
            catch { return false; }
        }

        // ── Helper: remove troops from garrison then party ─────────────────────
        private static void SacrificeTroops(Settlement s, int count)
        {
            int remaining = count;
            try
            {
                var garrison = s?.Town?.GarrisonParty?.MemberRoster;
                if (garrison != null)
                {
                    foreach (var e in garrison.GetTroopRoster().ToList())
                    {
                        if (e.Character.IsHero) continue;
                        int take = Math.Min(e.Number, remaining);
                        if (take <= 0) continue;
                        garrison.AddToCounts(e.Character, -take);
                        remaining -= take;
                        if (remaining <= 0) break;
                    }
                }
            }
            catch { }
            if (remaining > 0)
            {
                try
                {
                    var roster = MobileParty.MainParty?.MemberRoster;
                    if (roster != null)
                    {
                        foreach (var e in roster.GetTroopRoster().ToList())
                        {
                            if (e.Character.IsHero) continue;
                            int take = Math.Min(e.Number, remaining);
                            if (take <= 0) continue;
                            roster.AddToCounts(e.Character, -take);
                            remaining -= take;
                            if (remaining <= 0) break;
                        }
                    }
                }
                catch { }
            }
            int sacrificed = count - remaining;
            if (sacrificed > 0) Msg($"({sacrificed} troops consumed by the ritual)", BadColor);
        }

        // ── E_TheWasting — enter village/city, requires spouse + living child ──
        // A strange wasting sickness takes hold of the player's family.
        private static void E_TheWasting(Settlement s)
        {
            Hero spouse = Hero.MainHero?.Spouse;
            Hero child  = Hero.AllAliveHeroes.FirstOrDefault(h =>
                h.IsAlive && !h.IsDisabled &&
                (h.Father == Hero.MainHero || h.Mother == Hero.MainHero));

            if (spouse == null || child == null) return;
            _familyFeverCooldown = 600;

            bool mage          = MageKnowledge.IsMage;
            bool hasDarkTalent = mage && (TalentSystem.Has(TalentId.Ember)
                                       || TalentSystem.Has(TalentId.Reap));

            string spouseName = spouse.Name?.ToString() ?? "your spouse";
            string childName  = child.Name?.ToString()  ?? "your child";

            string cHint = mage
                ? $"The fire through both of them at once. Not a thing meant to be done like this."
                : "Requires mage ability.";
            string dHint = hasDarkTalent
                ? $"The ritual requires something living. It requires a lot of it."
                : "Requires Ember or Reap talent.";

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Wasting",
                $"A rider catches you on the road with bad news: a strange wasting sickness has taken hold of both {spouseName} and {childName} at the same time. The healers have done what they can. They can sustain one of them — not both. By the time you arrive, the choice is already framed in the doorway. There is not much time.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", $"Save {spouseName}.", null, true,
                        $"You put everything into one of them. That is the shape of this decision."),
                    new InquiryElement("b", $"Save {childName}.", null, true,
                        $"You put everything into one of them. That is the shape of this decision."),
                    new InquiryElement("c", "Channel the fire through both of them.", null, mage, cHint),
                    new InquiryElement("d", "Perform a dark ritual to sustain them.", null, hasDarkTalent, dHint),
                    new InquiryElement("e", "Make a pact with the cold. Pay whatever it asks.", null, true,
                        $"The cold accepts immediately. They survive. What you become is the cost."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            try { KillCharacterAction.ApplyByMurder(child, null, false); } catch { }
                            try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, spouse, -20, false); } catch { }
                            Msg($"You put everything into {spouseName}. The fever breaks. {childName} does not wake. {spouseName} knows what you chose, and what it cost, and does not yet know what to do with either of those things.", BadColor);
                            break;
                        case "b":
                            try { KillCharacterAction.ApplyByMurder(spouse, null, false); } catch { }
                            Msg($"You put everything into {childName}. The fever breaks. {spouseName} does not wake. {childName} will be older before they understand what happened in that room. You will have to decide what to tell them.", BadColor);
                            break;
                        case "c":
                            AgePlayer(3650); // 10 years
                            try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, spouse, 10, false); } catch { }
                            try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, child,  10, false); } catch { }
                            Msg($"You put the fire through both of them at once — not a thing that is meant to be done like this, not a thing you will be able to explain. It costs ten years. They both wake. {spouseName} holds your face when you come back to yourself and does not ask what you gave. {childName} is already asking for food.", FireColor);
                            break;
                        case "d":
                            try
                            {
                                Hero.MainHero.SetTraitLevel(DefaultTraits.Mercy, -2);
                                ShiftTrait(DefaultTraits.Honor, -1);
                                SacrificeTroops(s, 100);
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, spouse, -20, false);
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, child,  -20, false);
                            }
                            catch { }
                            Msg($"The ritual requires something living and it requires a lot of it. One hundred of your soldiers do not wake up. You do not watch. {spouseName} and {childName} survive, fever-broken, and when they look at you afterward there is something in it that was not there before. You do not explain what you did. They do not ask. Both of you prefer it this way.", DarkColor);
                            break;
                        case "e":
                            BecomeAshen();
                            try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, spouse, -50, false); } catch { }
                            try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, child,  -50, false); } catch { }
                            Msg($"The cold accepts the offer immediately, as though it had been waiting. {spouseName} and {childName} wake — both of them, at the same moment, as if pulled back by the same thread. They look at you and something in both of their faces shifts before they can hide it. You are different now. The grey is already in your eyes. They are alive. They know what it cost.", AshenColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── HasFamilyOrCompanions ──────────────────────────────────────────────
        private static bool HasFamilyOrCompanions()
        {
            try
            {
                if (Hero.MainHero?.Spouse?.IsAlive == true) return true;
                if (Hero.AllAliveHeroes.Any(h =>
                        h.IsAlive && !h.IsDisabled && h.Age < 18 &&
                        (h.Father == Hero.MainHero || h.Mother == Hero.MainHero)))
                    return true;
                return Hero.AllAliveHeroes.Any(h =>
                    h.IsAlive && !h.IsDisabled && !h.IsPrisoner &&
                    h != Hero.MainHero &&
                    h.PartyBelongedTo == MobileParty.MainParty);
            }
            catch { return false; }
        }

        // ── ApplyAshenFrenzyDamage — shared kill logic for B / A-fail ─────────
        private static void ApplyAshenFrenzyDamage()
        {
            bool anyKilled = false;

            Hero spouse = Hero.MainHero?.Spouse;
            if (spouse != null && spouse.IsAlive)
            {
                try { KillCharacterAction.ApplyByMurder(spouse, Hero.MainHero, false); } catch { }
                Msg($"({spouse.Name} killed)", BadColor);
                anyKilled = true;
            }

            var children = Hero.AllAliveHeroes
                .Where(h => h.IsAlive && !h.IsDisabled && h.Age < 18 &&
                            (h.Father == Hero.MainHero || h.Mother == Hero.MainHero))
                .ToList();
            foreach (var ch in children)
            {
                try { KillCharacterAction.ApplyByMurder(ch, Hero.MainHero, false); } catch { }
                Msg($"({ch.Name} killed)", BadColor);
                anyKilled = true;
            }

            if (!anyKilled)
            {
                var companions = Hero.AllAliveHeroes
                    .Where(h => h.IsAlive && !h.IsDisabled && !h.IsPrisoner &&
                                h != Hero.MainHero &&
                                h.PartyBelongedTo == MobileParty.MainParty)
                    .ToList();
                if (companions.Count > 0)
                {
                    var victim = companions[_rng.Next(companions.Count)];
                    try { KillCharacterAction.ApplyByMurder(victim, Hero.MainHero, false); } catch { }
                    Msg($"({victim.Name} killed)", BadColor);
                }
            }
        }

        // ── FireAshenFrenzy — deferred, fires the day after BecomeAshen ───────
        private static void FireAshenFrenzy()
        {
            if (MageKnowledge._deferredInquiry != null) { _ashenFrenzyCountdown = 1; return; }

            float leadChance = SkillChance(DefaultSkills.Leadership, 0.35f);
            string leadHint  = SkillHint(DefaultSkills.Leadership, 0.35f, "Force of will — hold back the hunger");

            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "★  The First Hunger",
                    "You wake before dawn and cannot say what woke you. The grey light from the window is wrong. The air is wrong. There is a sound beneath the silence — not a sound, a pressure — and the fire you used to carry has changed into something that does not distinguish between wood and flesh, between warmth given and warmth taken. It wants. It does not care what you want. The faces of the people closest to you move through your mind the way flame moves through dry straw: not as memory but as inventory.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("a", $"Fight it. ({(int)(leadChance * 100)}% Leadership)", null, true,
                            leadHint),
                        new InquiryElement("b", "Let it take you. You are what you are now.", null, true,
                            "You stop resisting. You will not remember all of what follows."),
                        new InquiryElement("c", "Kill yourself before it uses you.", null, true,
                            "The last decision that is entirely yours."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        switch (chosen?[0]?.Identifier as string)
                        {
                            case "a":
                                if (SkillRoll(DefaultSkills.Leadership, 0.35f))
                                {
                                    Msg("You find something to hold onto — a name, a specific memory, the weight of a specific obligation — and you press it between yourself and the thing that is pulling. It is like holding a door shut with your hands. The pull does not stop. But it does not get through. Not this time. You are still here. You are still choosing. That will have to be enough.", FireColor);
                                }
                                else
                                {
                                    Msg("You try. The thing that is using your hands does not try. It simply moves. You come back to yourself afterward, in a room that is wrong, with the knowledge of what your hands did arriving a second after the sight of it. You failed to hold it. This is what failure costs.", BadColor);
                                    ApplyAshenFrenzyDamage();
                                }
                                break;
                            case "b":
                                Msg("You stop resisting. What happens next comes in flashes: a face you love gone pale, a sound you will not repeat to yourself, the cold smell of the thing you have become doing what it does when nothing holds it back. You surface sometime later. The room is different than when you left it. So are you.", BadColor);
                                ApplyAshenFrenzyDamage();
                                break;
                            case "c":
                                Msg("You make the decision clearly, with both hands, before the hunger can use them for anything else. It is the last decision that is entirely yours. Nobody else dies.", DimColor);
                                try { KillCharacterAction.ApplyByMurder(Hero.MainHero, null, false); } catch { }
                                break;
                        }
                    }, null, "", false), false, true);
            };
        }

        // ── ES_AncientGrimoire — siege won, mage, one-time ────────────────────
        // A strange book found in the conquered keep's sealed archive.
        private static void ES_AncientGrimoire()
        {
            _ancientBookFound = 1;

            void ShowRitePrompt()
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "★  What the Book Describes",
                    "The rituals are specific and detailed — not the vague symbolism of cult texts but something written by someone who had done them and was writing down what worked. The central one describes a working to rekindle a depleted fire-gift at the cost of the lives surrounding it. Not metaphorically. The warmth of the living, taken in bulk, pressed back into a fire-carrier who has burned too low. The author's notes in the margin suggest it was tested. They suggest it worked.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("a", "Discard it. You have read enough.", null, true,
                            "You set it down. Someone else will find it eventually."),
                        new InquiryElement("b", "Perform the rite.", null, true,
                            "The author tested this. They wrote that it worked."),
                        new InquiryElement("c", "Report it to the nearest temple. This should not exist.", null, true,
                            "The temple will know what to do with it. They have handled this before."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen2 =>
                    {
                        switch (chosen2?[0]?.Identifier as string)
                        {
                            case "a":
                                Msg("You set the book face-down on the table and leave it there. Someone will find it when they clear the keep. That is their problem now. You are not certain you made the right decision, but you made a decision, and that is enough for tonight.", DimColor);
                                break;
                            case "b":
                                try
                                {
                                    Hero.MainHero.SetTraitLevel(DefaultTraits.Honor, -2);
                                    Hero.MainHero.SetTraitLevel(DefaultTraits.Mercy, -2);
                                }
                                catch { }
                                // Grant Reap if not owned, else attribute point
                                if (!TalentSystem.Has(TalentId.Reap))
                                    TalentSystem.GrantFree(TalentId.Reap, Hero.MainHero);
                                else
                                    try { Hero.MainHero.HeroDeveloper.UnspentAttributePoints += 1; } catch { }
                                KillHalfParty();
                                Msg("The working is exactly what the book said it was — which is to say it is the worst thing you have done. Your soldiers fall between one breath and the next, not in pain, just gone. The fire in you surges in a way that makes the preceding days feel like ash. You are standing in a room full of people who trusted you, and half of them are not standing anymore. The book's author was correct. It works.", BadColor);
                                break;
                            case "c":
                                if (!ChangeGold(-500)) return;
                                ChangeRenown(10f);
                                ShiftTrait(DefaultTraits.Honor, 1);
                                Msg("You have the book wrapped and sealed for transport. The temple receives it with the grim recognition of people who have handled this category of thing before. The courier confirms delivery. You receive formal acknowledgement and a note of thanks that does not begin to cover what you have handed them. The renown is a side-effect — what you actually did was make sure no one else reads that margin note and decides to test the method.", GoodColor);
                                break;
                        }
                    }, null, "", false), false, true);
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Sealed Archive",
                "Your men found it behind a false wall in the keep's lower study — a sealed room, clearly personal, clearly not meant to be entered by whoever came next. Inside: a single book, handwritten, with a lock that took three of your people an hour to open. The title page has no author and no date. The first ten pages are in a cipher. The next two hundred are not.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Burn it. Some things are better unread.", null, true,
                        "Some things are better unread."),
                    new InquiryElement("b", "Read it.", null, true,
                        "See what it contains."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You burn it in the keep's hearth without reading beyond the first page. The fire takes it quickly — more quickly than paper should. Whatever was in the cipher, it goes with the rest. The room feels different when you leave it. Not better. Just different.", GoodColor);
                            break;
                        case "b":
                            ShowRitePrompt();
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── Helper: kill 50% of non-hero party troops ──────────────────────────
        private static void KillHalfParty()
        {
            try
            {
                var roster = MobileParty.MainParty?.MemberRoster;
                if (roster == null) return;
                int total = roster.GetTroopRoster()
                    .Where(e => !e.Character.IsHero && e.Number > 0)
                    .Sum(e => e.Number);
                int toKill = total / 2;
                if (toKill <= 0) return;
                int killed = 0;
                foreach (var e in roster.GetTroopRoster().ToList())
                {
                    if (e.Character.IsHero || e.Number <= 0) continue;
                    int take = Math.Min(e.Number, toKill - killed);
                    if (take <= 0) break;
                    roster.AddToCounts(e.Character, -take);
                    killed += take;
                    if (killed >= toKill) break;
                }
                if (killed > 0) Msg($"({killed} troops consumed by the rite)", BadColor);
            }
            catch { }
        }

        // ── HasHedgeWitchCondition ─────────────────────────────────────────────
        private static bool HasHedgeWitchCondition()
        {
            try
            {
                Hero h = Hero.MainHero;
                Hero spouse = h?.Spouse;
                if (spouse == null || !spouse.IsAlive) return false;
                if (h.Age < 40f || spouse.Age < 40f) return false;
                if (Hero.AllAliveHeroes.Any(c =>
                        c.IsAlive && !c.IsDisabled &&
                        (c.Father == h || c.Mother == h))) return false;
                if (spouse.GetTraitLevel(DefaultTraits.Honor) >= 1) return false;
                if (spouse.GetTraitLevel(DefaultTraits.Mercy)  >= 1) return false;
                return true;
            }
            catch { return false; }
        }

        // ── E_NightVisitor — enter village/city, conditional ──────────────────
        // A servant reports a strange figure visiting the player's spouse at night.
        private static void E_NightVisitor(Settlement s)
        {
            Hero spouse = Hero.MainHero?.Spouse;
            if (spouse == null) return;
            _hedgeWitchCooldown = 300;

            string spouseName = spouse.Name?.ToString() ?? "your spouse";
            float scoutChance = SkillChance(DefaultSkills.Scouting, 0.35f);
            string scoutHint  = SkillHint(DefaultSkills.Scouting, 0.35f, "Follow the figure without being seen");

            void ShowRevelation()
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "★  What Your Spouse Has Done",
                    $"The figure is a hedge witch — old, deliberate, carrying things in a bag that clink wrong. This is not her first visit. {spouseName} receives her without surprise: they have spoken before, many times, in the hours before dawn when the house is asleep. You piece it together quickly. The herbs, the cost, the quiet desperation of someone who has watched time run and decided to reach for something the healers will not offer. She has been trying to give you children before the years close that door entirely.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("a", "Say nothing. It is kind of them.", null, true,
                            "Something may come of it. Something else may come of it too."),
                        new InquiryElement("b", "Hang the witch.", null, true,
                            "You are not a patient person."),
                        new InquiryElement("c", $"Kill them both — the witch and {spouseName} — in fury.", null, true,
                            "The fury decides for you. Both of them."),
                        new InquiryElement("d", "Tell the witch to go and never come back.", null, true,
                            "You end it quietly. No blood, no answers."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen2 =>
                    {
                        switch (chosen2?[0]?.Identifier as string)
                        {
                            case "a":
                                ShiftTrait(DefaultTraits.Honor, -1);
                                // 20% fertility boost: attempt pregnancy for the appropriate hero
                                if (_rng.NextDouble() < 0.20)
                                {
                                    Hero target = (Hero.MainHero?.IsFemale == true)
                                        ? Hero.MainHero : spouse;
                                    try { MakePregnantAction.Apply(target); } catch { }
                                }
                                _hedgeWitchCurse = 7;
                                Msg($"You say nothing and leave the way you came. {spouseName} never knows you were there. The witch departs before dawn. You carry the knowledge of it without speaking it. Whatever was agreed in that room begins to work. Seven days later, so does everything else.", DimColor);
                                break;
                            case "b":
                                ShiftTrait(DefaultTraits.Calculating, -1);
                                Msg("You have the witch taken before she leaves the grounds. She does not argue. She asks only that you know what you are stopping. You have her hanged before noon. {spouseName} does not speak for three days. Neither do you.", BadColor);
                                break;
                            case "c":
                                ShiftTrait(DefaultTraits.Calculating, -1);
                                try { Hero.MainHero.SetTraitLevel(DefaultTraits.Mercy, -2); } catch { }
                                try { KillCharacterAction.ApplyByMurder(spouse, Hero.MainHero, false); } catch { }
                                Msg($"The fury comes before the thought. The witch is first — she had time to understand what was happening. {spouseName} had less. You surface an hour later in a room that cannot be unchanged. What was done out of love and desperation is done. So is {spouseName}.", BadColor);
                                break;
                            case "d":
                                Msg("You step into the room before {spouseName} can speak. You tell the witch to go — calmly, with enough in your voice that she understands this is the last visit. She leaves. {spouseName} watches you with something that is not quite relief and not quite anger. You do not discuss it. The door stays between you for a long time.", DimColor);
                                break;
                        }
                    }, null, "", false), false, true);
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "♦  The Visitor at Night",
                $"One of your servants finds you before you have finished your first cup of the morning. They are careful with their words — a strange figure, they say, has been seen entering the house in the hours before dawn. Not a burglar: too deliberate, too familiar with the layout, too expected by whoever let them in. They look at you and wait to see what you want to do with that.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Leave it be.", null, true,
                        "Whatever is happening in your house continues without you."),
                    new InquiryElement($"b", $"Investigate. ({(int)(scoutChance * 100)}% Scouting)", null, true,
                        scoutHint),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            Msg("You tell the servant you heard them and will handle it. You do not handle it. Whatever is happening in your house at odd hours continues to happen without your interference. You are not certain if that is restraint or avoidance.", DimColor);
                            break;
                        case "b":
                            if (SkillRoll(DefaultSkills.Scouting, 0.35f))
                                ShowRevelation();
                            else
                                Msg("You watch the house for three mornings without finding anything out of the ordinary. Whatever the servant saw, the timing was either coincidence or whoever it was has learned to move more carefully. The question stays open.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── EC_BrokenSeal — enter city/castle, general ───────────────────────────
        // Player stumbles on evidence of a secret inter-kingdom plot.
        // Four discovery variants; three plot types; deferred 3-day consequence.
        private static void EC_BrokenSeal(Settlement s)
        {
            var kingdoms = Kingdom.All
                .Where(k => !k.IsEliminated && k.Leader != null && k.Leader.IsAlive
                         && (Hero.MainHero?.MapFaction == null || k != Hero.MainHero.MapFaction)
                         && k.StringId != "ashen_kingdom")
                .ToList();
            if (kingdoms.Count < 2) return;

            int idxA = _rng.Next(kingdoms.Count);
            int idxB;
            do { idxB = _rng.Next(kingdoms.Count); } while (idxB == idxA);
            Kingdom kA = kingdoms[idxA];
            Kingdom kB = kingdoms[idxB];

            int plotType = _rng.Next(3) + 1;
            string plotDesc = plotType switch
            {
                1 => $"The document is an order of march. {kA.Name} is prepared to declare war on {kB.Name} — messengers ride in three days.",
                2 => $"The documents describe a quiet land-grab: a manufactured claim, bribed garrison captains, timed troop movements. {kA.Name} intends to take one of {kB.Name}'s castles without a formal declaration.",
                _ => $"The instructions are for saboteurs already inside {kB.Name}'s borders — agents moving toward a specific city, with orders to leave it in disorder while keeping {kA.Name}'s hands clean."
            };

            int variant = _rng.Next(4);
            string discovery = variant switch
            {
                0 => $"Your outriders found a body half off the road a mile back — a courier in {kA.Name}'s colours, throat cut, stripped of valuables but not of the letters inside his coat. One of your men broke the seal before thinking better of it and handed the pages over.",
                1 => $"Two men in the back corner of the tavern were speaking in the careful lowered voices of people who believe they cannot be heard. They were wrong. Between their cups you caught the shape of it — a plan, a target, a name: {kB.Name}.",
                2 => $"A soldier in {kA.Name}'s colours, three cups past sober and pleased with himself, drifted to your table and began talking about things soldiers are not supposed to discuss in public places. His sergeant will be furious tomorrow. You, however, are not.",
                _ => $"The serving girl slid a folded note under your cup as she cleared it and leaned close enough to name a price. She had lifted it from a {kA.Name} messenger an hour ago — their seal intact, {kB.Name}'s name on the outside, and something inside that she had already read and could not unread."
            };

            _brokenSealKingdomAId = kA.StringId;
            _brokenSealKingdomBId = kB.StringId;
            _brokenSealPlotType   = plotType;
            _brokenSealExtraWar   = false;

            float scoutChance = SkillChance(DefaultSkills.Scouting, 0.30f);
            string scoutHint  = SkillHint(DefaultSkills.Scouting, 0.30f, $"Reach {kB.Name}'s court in time");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚜  The Broken Seal",
                $"{discovery}\n\n{plotDesc} You now know something you were not meant to know.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Ignore it. Not your concern.", null, true,
                        "Three days and whatever comes of it comes without your involvement."),
                    new InquiryElement("b", $"Ride hard and warn {kB.Name}. ({(int)(scoutChance * 100)}% Scouting, then Charm)", null, true,
                        scoutHint),
                    new InquiryElement("c", $"Get word to {kA.Name} — their secret is worth something to them.", null, true,
                        "They pay for your silence. The plot proceeds. +1,500 gold, +10 with their leader."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            _brokenSealCountdown = 3;
                            Msg($"You fold the letter along its old creases and move on. Whatever {kA.Name} is planning, it will reach {kB.Name} without your help or your interference.", DimColor);
                            break;

                        case "b":
                            if (SkillRoll(DefaultSkills.Scouting, 0.30f))
                            {
                                if (SkillRoll(DefaultSkills.Charm, 0.30f))
                                {
                                    // Both pass: plot foiled, +10 relation with Kingdom B, Kingdom A declares war
                                    _brokenSealPlotType  = 0;
                                    _brokenSealCountdown = 0;
                                    Hero leaderB = kB.Leader;
                                    if (leaderB != null && leaderB != Hero.MainHero)
                                    {
                                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, leaderB, 10, false);
                                        Msg($"(Relation with {leaderB.Name}: +10)", GoodColor);
                                    }
                                    if (!kA.IsAtWarWith(kB))
                                        try { DeclareWarAction.ApplyByDefault(kA, kB); } catch { }
                                    Msg($"They believe you. {kB.Name}'s council moves before you have finished explaining — emergency session, counter-orders written, messengers dispatched. The plot is dead. {kA.Name}, knowing the game is up, drops all pretence of patience and reaches for the only option left.", GoodColor);
                                }
                                else
                                {
                                    // Scouting pass, Charm fail: +5 with leader B, consequence + extra war
                                    _brokenSealCountdown = 3;
                                    _brokenSealExtraWar  = true;
                                    Hero leaderB = kB.Leader;
                                    if (leaderB != null && leaderB != Hero.MainHero)
                                    {
                                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, leaderB, 5, false);
                                        Msg($"(Relation with {leaderB.Name}: +5)", GoodColor);
                                    }
                                    Msg($"You arrived in time, showed what you had, argued clearly — and were thanked, politely, and not believed. {kB.Name}'s court categorises people who arrive with dramatic letters as a known type of problem. The warning was noted. It was not acted upon. {kA.Name}'s plan will proceed.", DimColor);
                                }
                            }
                            else
                            {
                                // Scouting fail: too late, same as Ignore
                                _brokenSealCountdown = 3;
                                Msg($"You rode hard and arrived at the wrong gate, the wrong hour, the wrong official. {kB.Name}'s court was unreachable in any useful time. By the time your letter reaches the right desk, three days will have passed. Same result. Different road.", DimColor);
                            }
                            break;

                        case "c":
                        {
                            // Sell the information back to Kingdom A — plot proceeds, player profits
                            _brokenSealCountdown = 3;
                            ChangeGold(1500);
                            Msg("(+1,500 gold)", GoldColor);
                            Hero leaderA = kA.Leader;
                            if (leaderA != null && leaderA != Hero.MainHero)
                            {
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, leaderA, 10, false);
                                Msg($"(Relation with {leaderA.Name}: +10)", GoodColor);
                            }
                            Msg($"You find a way to reach {kA.Name}'s people — not with the letter, but with the fact of it: someone nearly broke the seal and you are not that someone. They understand the value of that. The coin arrives quickly and without ceremony. The plan proceeds. {kB.Name} will have no warning.", GoldColor);
                            break;
                        }
                    }
                }, null, "", false), false, true);
        }

        // ── Deferred: FireBrokenSealConsequence — 3 days after EC_BrokenSeal A/B-fail/C ──
        private static void FireBrokenSealConsequence()
        {
            if (MageKnowledge._deferredInquiry != null) { _brokenSealCountdown = 1; return; }

            var kA        = Kingdom.All.FirstOrDefault(k => k.StringId == _brokenSealKingdomAId);
            var kB        = Kingdom.All.FirstOrDefault(k => k.StringId == _brokenSealKingdomBId);
            int plotType  = _brokenSealPlotType;
            bool extraWar = _brokenSealExtraWar;

            _brokenSealPlotType   = 0;
            _brokenSealExtraWar   = false;
            _brokenSealKingdomAId = null;
            _brokenSealKingdomBId = null;

            if (kA == null || kB == null || kA.IsEliminated || kB.IsEliminated) return;

            void MaybeExtraWar()
            {
                if (extraWar && !kB.IsAtWarWith(kA))
                    try { DeclareWarAction.ApplyByDefault(kB, kA); } catch { }
            }

            switch (plotType)
            {
                case 1: // War
                {
                    if (!kA.IsAtWarWith(kB))
                        try { DeclareWarAction.ApplyByDefault(kA, kB); } catch { }
                    MaybeExtraWar();
                    MageKnowledge._deferredInquiry = () =>
                        Msg($"Three days since the letter. {kA.Name} has declared war on {kB.Name}. You had the order in your hands.", BadColor);
                    break;
                }
                case 2: // Annexation — 50/50
                {
                    bool annexed = false;
                    if (_rng.NextDouble() < 0.5)
                    {
                        var castles = Settlement.All
                            .Where(x => x.IsCastle && x.OwnerClan?.Kingdom == kB
                                     && x.OwnerClan != null && !x.OwnerClan.IsEliminated)
                            .ToList();
                        var lordsA = Hero.AllAliveHeroes
                            .Where(h => h.IsLord && h.IsAlive && !h.IsPrisoner
                                     && h.MapFaction == kA && h != Hero.MainHero)
                            .ToList();
                        if (castles.Count > 0 && lordsA.Count > 0)
                        {
                            var castle   = castles[_rng.Next(castles.Count)];
                            var newOwner = lordsA[_rng.Next(lordsA.Count)];
                            string cName = castle.Name.ToString();
                            try { ChangeOwnerOfSettlementAction.ApplyByDefault(newOwner, castle); } catch { }
                            MaybeExtraWar();
                            annexed = true;
                            MageKnowledge._deferredInquiry = () =>
                                Msg($"{cName} now flies {kA.Name}'s banner. The transfer was quick, quiet, and complete. {kB.Name} is still working out how it happened.", BadColor);
                        }
                    }
                    if (!annexed)
                    {
                        MaybeExtraWar();
                        MageKnowledge._deferredInquiry = () =>
                            Msg($"The annexation fell through — wrong timing, or a piece that moved before the rest were ready. {kB.Name} holds what it had. This time.", DimColor);
                    }
                    break;
                }
                case 3: // Sabotage — 50/50
                {
                    bool sabotaged = false;
                    if (_rng.NextDouble() < 0.5)
                    {
                        var towns = Settlement.All
                            .Where(x => x.IsTown && x.Town != null && x.OwnerClan?.Kingdom == kB)
                            .ToList();
                        if (towns.Count > 0)
                        {
                            var target = towns[_rng.Next(towns.Count)];
                            string tName = target.Name.ToString();
                            try
                            {
                                target.Town.Prosperity = Math.Max(10f, target.Town.Prosperity - 300f);
                                target.Town.Security   = Math.Max(0f,  target.Town.Security   - 30f);
                            } catch { }
                            MaybeExtraWar();
                            sabotaged = true;
                            MageKnowledge._deferredInquiry = () =>
                                Msg($"{tName} is in disorder — fires, missing officials, spoiled grain stores. Nobody can name the cause clearly, which was the point. {kA.Name}'s agents have already left.", BadColor);
                        }
                    }
                    if (!sabotaged)
                    {
                        MaybeExtraWar();
                        MageKnowledge._deferredInquiry = () =>
                            Msg($"The saboteurs were caught or turned back. {kB.Name}'s city stands unmarked. Whatever {kA.Name} sent into it, it did not land.", DimColor);
                    }
                    break;
                }
            }
        }

        // ── Deferred: FireHopeMageConsequence — 14 days after LC_YoungMageHope ──────
        private static void FireHopeMageConsequence()
        {
            if (MageKnowledge._deferredInquiry != null) { _hopeMageConsequenceCountdown = 1; return; }
            var settlement = _hopeMageSettlementId != null
                ? Settlement.All.FirstOrDefault(s => s.StringId == _hopeMageSettlementId)
                : null;
            _hopeMageSettlementId = null;
            string sName = settlement?.Name?.ToString() ?? "that city";

            if (settlement?.Town != null)
            {
                // Drop loyalty to near-zero so Bannerlord's native rebellion system fires within 1-2 days.
                try { settlement.Town.Loyalty  = 5f; } catch { }
                try { settlement.Town.Security = 0f; } catch { }
            }

            MageKnowledge._deferredInquiry = () =>
                Msg($"The walls of {sName} are lit by torches held by the city's own people tonight. " +
                    "The young mage who marched north came back with a name and a story, and the story found every ear that was ready for it. " +
                    $"Loyalty in {sName} has collapsed. The city is on the verge of tearing itself loose from its lord.", AshenColor);
        }

        // ── Deferred: FireMemoryHungerDissolution — 7 days after EV_MemoryHunger choice B ──
        private static void FireMemoryHungerDissolution()
        {
            if (MageKnowledge._deferredInquiry != null) { _memoryHungerCountdown = 1; return; }
            _memoryHungerConsumed  = false;
            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "◆  The End of the Hunger",
                    "You wake before dawn and there is nothing left. Not the fire. Not the cold. Not the name you called yourself before either of them arrived. " +
                    "What is looking at the ceiling of your tent through your eyes is not you. " +
                    "It is not certain what it is. " +
                    "It is aware that this is the end of the person who made the choice that brought it here.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("a", "I am still here.", null, true,
                            "You are not. But the thing that says so is convincing."),
                    },
                    false, 1, 1, "", "",
                    _ =>
                    {
                        try
                        {
                            if (Hero.MainHero != null && Hero.MainHero.IsAlive)
                                KillCharacterAction.ApplyByOldAge(Hero.MainHero, true);
                        }
                        catch { }
                    }, null, "", false), false, true);
            };
        }

        // ── Deferred: FireHedgeWitchCurse — 7 days after E_NightVisitor choice A ──
        private static void FireHedgeWitchCurse()
        {
            if (MageKnowledge._deferredInquiry != null) { _hedgeWitchCurse = 1; return; }
            WoundPlayer();
            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "♦  The Price of the Bargain",
                    "Seven days after the witch's visit. It begins in the night — not a wound, not a fever in the ordinary sense. Something the witch's working cost that was not disclosed in the agreement, or was disclosed in terms that were easy to misread at the time. You come awake cold, unable to stand, your body doing things that the healers will describe later as 'an acute episode' in the careful way healers describe things they do not understand. It takes a week to pass. Some of it does not pass.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("ok", "Endure it.", null, true, "The price was not explained in advance."),
                    },
                    false, 1, 1, "Endure", "",
                    _ => Msg("You survive it. The healers say you will recover. They mean most of it. Whatever the witch's working extracted as its price, it took it without asking and gave back something approximate.", BadColor),
                    null, "", false), false, true);
            };
        }

        // Grants a random spell or enchantment talent if the player has fewer than 5 of them.
        private static void GrantMagicalTalent()
        {
            int magCount = TalentSystem.AllPurchased
                .Count(id => { var d = TalentSystem.All.FirstOrDefault(x => x.Id == id);
                               return d != null && (d.IsSpell || d.IsEnchantment); });
            if (magCount >= 5) return;
            var available = TalentSystem.All
                .Where(t => (t.IsSpell || t.IsEnchantment) && !TalentSystem.Has(t.Id))
                .ToList();
            if (available.Count == 0) return;
            TalentSystem.GrantFree(available[_rng.Next(available.Count)].Id, Hero.MainHero);
        }

        // ── EC_CinderVigil — enter village or town, Battania/Strugia/Aserai/Khuzait territory ──
        // Temple soldiers conducting a purge on local practitioners (druids, alchemists, shamans).
        // Not before day 50. 150-day cooldown between recurrences.
        private static void EC_CinderVigil(Settlement s)
        {
            if (_cinderVigilCooldown > 0) return;

            string cult    = s.Culture?.StringId ?? "";
            bool isNorth   = cult == "battania" || cult == "sturgia";
            bool isAserai  = cult == "aserai";
            bool isKhuzait = cult == "khuzait";
            if (!isNorth && !isAserai && !isKhuzait) return;

            _cinderVigilCooldown = 150;

            string[] relevantIds = isNorth   ? new[] { "battania", "sturgia" }
                                 : isAserai  ? new[] { "aserai" }
                                 : new[] { "khuzait" };

            string target = isNorth ? "druids" : isAserai ? "alchemists" : "shamans";

            string description = isNorth
                ? "You come across a column of Temple soldiers at a crossroads between burned longhouses. They have cornered a group of druids — seven or eight, most elderly, some with root-stained hands that mark them as practitioners. The officer is reading from a warrant. His men have already drawn weapons. This is organized. Whatever this is, they were sent to do it."
                : isAserai
                ? "In the afternoon heat of the market quarter, Temple soldiers have cordoned off a workshop. The scholars inside — three alchemists, a cartographer, an old physician — sit with their hands visible, watching their equipment being smashed methodically. A soldier calls names from a list. Across the street, the market crowd watches in the silence of people who have learned not to ask questions."
                : "On the edge of the steppe settlement, Temple soldiers have separated a shaman from the rest of the camp and surrounded her on open ground. The camp watches from a distance, held back by a line of spears. She is old. She is not fighting. The soldiers are waiting for something — a word, perhaps, or a reason to act. They will make one if none arrives.";

            var factionLords = Hero.AllAliveHeroes
                .Where(h => h.IsLord && h.IsAlive && !h.IsPrisoner && h != Hero.MainHero
                         && relevantIds.Contains(h.MapFaction?.StringId))
                .ToList();

            var templeLords = Hero.AllAliveHeroes
                .Where(h => h.IsLord && h.IsAlive && !h.IsPrisoner && h != Hero.MainHero
                         && h.MapFaction?.StringId == "the_temple")
                .ToList();

            string leadHint   = SkillHint(DefaultSkills.Leadership, 0.30f, "Convince the commander");
            var bestCombat    = new[] { DefaultSkills.OneHanded, DefaultSkills.TwoHanded, DefaultSkills.Polearm }
                                    .OrderByDescending(sk => GetSkill(sk)).First();
            string combatHint = SkillHint(bestCombat, 0.30f, "Drive them off");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Cinder Vigil",
                description,
                new List<InquiryElement>
                {
                    new InquiryElement("a",
                        $"Ride past. The {target} will deal with their own fate.",
                        null, true,
                        $"Stay out of it. (Mercy -1)"),
                    new InquiryElement("b",
                        $"Speak to the soldiers. Argue for the {target}.",
                        null, true,
                        $"Reason with the commander. Gain Mercy, Calculating. {leadHint}"),
                    new InquiryElement("c",
                        $"Draw your weapon and drive the soldiers off.",
                        null, true,
                        $"Force the issue. Gain Calculating -1. -20 with Temple lords. -5 with 3 Empire lords. {combatHint}"),
                    new InquiryElement("d",
                        $"Offer your assistance to the soldiers. The {target} may be Ashen servants.",
                        null, true,
                        "Fall in beside them. Gain Calculating -1. -10 with a local lord. +10 with Temple lords. Gain 1000 gold."),
                    new InquiryElement("e",
                        $"Join the killing. The {target} mean nothing to you.",
                        null, true,
                        $"Cruelty is its own reward. Gain Mercy -1. -20 with a local lord. +2 with a Temple lord. Gain Reap if you don't have it."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                        {
                            ShiftTrait(DefaultTraits.Mercy, -1);
                            Msg($"You ride past. The sounds follow you down the road a distance. Not far enough.", DimColor);
                            break;
                        }
                        case "b":
                        {
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            if (SkillRoll(DefaultSkills.Leadership, 0.30f))
                            {
                                Msg($"The officer listens. Eventually, he listens. Whatever you said held weight. The {target} are released with a warning that will expire in a week. You take that as the best available outcome.", GoodColor);
                                if (factionLords.Count > 0)
                                {
                                    var lord = factionLords[_rng.Next(factionLords.Count)];
                                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, 10, false);
                                    Msg($"(Relation with {lord.Name}: +10)", GoodColor);
                                }
                                GrantMagicalTalent();
                            }
                            else
                            {
                                Msg($"You argue. The commander listens with the patience of a man who has heard this before and decided it doesn't apply. The {target} are taken anyway. You were not enough.", DimColor);
                            }
                            break;
                        }
                        case "c":
                        {
                            ShiftTrait(DefaultTraits.Calculating, -1);
                            foreach (var lord in templeLords)
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, -20, false);
                            if (templeLords.Count > 0)
                                Msg("(Relation with all Temple lords: -20)", BadColor);
                            var empireLords = Hero.AllAliveHeroes
                                .Where(h => h.IsLord && h.IsAlive && !h.IsPrisoner && h != Hero.MainHero
                                         && (h.MapFaction?.StringId == "empire"   || h.MapFaction?.StringId == "empire_w"
                                          || h.MapFaction?.StringId == "empire_s" || h.MapFaction?.StringId == "empire_n"))
                                .OrderBy(_ => _rng.Next()).Take(3).ToList();
                            foreach (var lord in empireLords)
                            {
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, -5, false);
                                Msg($"(Relation with {lord.Name}: -5)", BadColor);
                            }
                            if (SkillRoll(bestCombat, 0.30f))
                            {
                                Msg($"The line breaks. The soldiers pull back in the organized retreat of men who know when a fight has changed. The {target} scatter before anyone reorganizes. You have made enemies today, but the {target} are alive.", GoodColor);
                                if (factionLords.Count > 0)
                                {
                                    var lord = factionLords[_rng.Next(factionLords.Count)];
                                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, 10, false);
                                    Msg($"(Relation with {lord.Name}: +10)", GoodColor);
                                }
                                GrantMagicalTalent();
                            }
                            else
                            {
                                Msg($"You charge. It is not enough. The soldiers hold and the {target} are cut down while you are still fighting through. You survive the engagement. They did not.", BadColor);
                            }
                            break;
                        }
                        case "d":
                        {
                            ShiftTrait(DefaultTraits.Calculating, -1);
                            if (factionLords.Count > 0)
                            {
                                var lord = factionLords[_rng.Next(factionLords.Count)];
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, -10, false);
                                Msg($"(Relation with {lord.Name}: -10)", BadColor);
                            }
                            foreach (var lord in templeLords)
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, 10, false);
                            if (templeLords.Count > 0)
                                Msg("(Relation with Temple lords: +10)", GoodColor);
                            ChangeGold(1000);
                            Msg($"You fall in beside the soldiers. The work is brief. The commander thanks you with the formal distance of a man who does not need your name. He leaves a purse. The {target} are gone. The road is clear.", DimColor);
                            break;
                        }
                        case "e":
                        {
                            ShiftTrait(DefaultTraits.Mercy, -1);
                            if (factionLords.Count > 0)
                            {
                                var lord = factionLords[_rng.Next(factionLords.Count)];
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, -20, false);
                                Msg($"(Relation with {lord.Name}: -20)", BadColor);
                            }
                            if (templeLords.Count > 0)
                            {
                                var lord = templeLords[_rng.Next(templeLords.Count)];
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, 2, false);
                                Msg($"(Relation with {lord.Name}: +2)", GoodColor);
                            }
                            if (!TalentSystem.Has(TalentId.Reap))
                                TalentSystem.GrantFree(TalentId.Reap, Hero.MainHero);
                            Msg($"You join them. There is a specific kind of ease that comes with it — no decision to make, no weight to carry afterward. When it is over, you feel the familiar warmth. Something given back. The soldiers give you a wide berth on the road home.", BadColor);
                            break;
                        }
                    }
                }, null, "", false), false, true);
        }

        // ── AFTER BATTLE: The Cold Machine [Ashen enemy defeated, mage player] ────────────────
        private static void EB_AshenMachinery()
        {
            _ashenMachineryCooldown = 90;
            bool mage = MageKnowledge.IsMage;

            string intro = mage
                ? "Among the fallen Ashen your men have found something they brought to you because they did not know what else to do with it. " +
                  "It is a framework of iron and dark glass packed with crystals arranged in a pattern that makes your teeth ache to look at. " +
                  "The mages were moving toward it when your soldiers cut them down. You recognise what it is — not the design, but the purpose. " +
                  "A weapon shaped by someone who understood cold fire from the outside in. It did not fire. Yet."
                : "Among the fallen Ashen your men have found something and brought it to you because they did not know what else to do with it. " +
                  "A framework of iron and dark glass packed with crystals, arranged in no pattern you recognise. " +
                  "The mages were moving toward it when your soldiers cut them down. " +
                  "You do not know what it does. You know that the Ashen were willing to die for it.";

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚗  The Cold Machine",
                intro,
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Destroy it. Some things should not leave this field.", null, true,
                        "Nothing happens. It is gone."),
                    new InquiryElement("b", "Break it apart. The crystals and glass will sell.", null, true,
                        "+5000 coin. No further consequence."),
                    new InquiryElement("c", "Point it at an enemy. Choose a target.", null, true,
                        "A city burns. You age three years. 50% chance of war with that faction."),
                    new InquiryElement("d", "Sell it to the black market — intact, no questions asked.", null, true,
                        "+3000 coin now. A city burns in fourteen days. You will not be named."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            Msg("Your soldiers break it with hammers and scatter the pieces across the field. " +
                                "The crystals go dark one by one as they leave the frame, each one releasing a brief cold pulse you feel in your back teeth. " +
                                "Then nothing. The field smells like iron and old snow. " +
                                "You leave it where it lies.", DimColor);
                            break;
                        case "b":
                            ChangeGold(5000);
                            Msg("The crystals come free cleanly. The glasswork is unusual — someone in the cities will pay for it without asking why. " +
                                "By the time the baggage train reaches the next waypoint the pieces are already wrapped and labelled by your quartermaster. " +
                                "Five thousand coin by nightfall. The machine is gone.", GoldColor);
                            break;
                        case "c":
                            ShowKingdomSelectorForMachinery();
                            break;
                        case "d":
                            _ashenMachineryCountdown = 14;
                            ChangeGold(3000);
                            Msg("A contact is found through the usual chain of intermediaries. " +
                                "Three thousand coin changes hands before you have fully explained what you are selling. " +
                                "They do not ask questions. Neither do you. The machine leaves on a cart you do not follow. " +
                                "Somewhere, in fourteen days, it will be pointed at something. You will not be named.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        private static void ShowKingdomSelectorForMachinery()
        {
            var playerFaction = Hero.MainHero?.MapFaction;
            var validKingdoms = Kingdom.All
                .Where(k => !k.IsEliminated
                         && k.StringId != "ashen_kingdom"
                         && (IFaction)k != playerFaction)
                .ToList();

            if (validKingdoms.Count == 0)
            {
                Msg("There are no suitable targets remaining. You dismantle the machine.", DimColor);
                return;
            }

            var elements = validKingdoms
                .Select(k => new InquiryElement(k.StringId, k.Name?.ToString() ?? k.StringId, null, true,
                    "A random city of this faction will be struck by the machine."))
                .ToList();

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  Choose Your Target",
                "The machine is cold and ready. Point it. When you activate it you will age three years — " +
                "the cost of channelling this much cold fire at once. Choose carefully.",
                elements,
                false, 1, 1, "Release it", "Pull back",
                selected =>
                {
                    if (selected == null || selected.Count == 0) return;
                    string kId = selected[0].Identifier as string;
                    var targetK = Kingdom.All.FirstOrDefault(k => k.StringId == kId);
                    if (targetK == null) return;

                    AgingSystem.AgeHero(Hero.MainHero, 3 * 365);

                    string cityName = DevastateCityInKingdom(targetK);

                    bool declaredWar = false;
                    if (_rng.NextDouble() < 0.5)
                    {
                        var playerKingdom = Hero.MainHero?.Clan?.Kingdom;
                        if (playerKingdom != null && !targetK.IsAtWarWith(playerKingdom))
                        {
                            try { DeclareWarAction.ApplyByDefault(targetK, playerKingdom); } catch { }
                            declaredWar = true;
                        }
                    }

                    string cityLine = string.IsNullOrEmpty(cityName)
                        ? "A city in their territory"
                        : cityName;

                    if (declaredWar)
                        Msg($"The crystals release all at once. You feel three years leave you in a single breath — not pain, just subtraction. " +
                            $"{cityLine} takes the discharge. Prosperity, food, order — all of it collapses. " +
                            $"When the word reaches their council they know it was not natural. " +
                            $"Someone has already given them your name.", BadColor);
                    else
                        Msg($"The crystals release all at once. You feel three years leave you in a single breath — not pain, just subtraction. " +
                            $"{cityLine} takes the discharge. Prosperity, food, order — all of it collapses. " +
                            $"The evidence trails away into rumour. No one is certain who held the machine. " +
                            $"Not yet.", AshenColor);
                },
                null, "", false), false, true);
        }

        private static string DevastateCityInKingdom(Kingdom kingdom)
        {
            var towns = Settlement.All
                .Where(s => s.IsTown && s.Town != null && s.OwnerClan?.Kingdom == kingdom)
                .ToList();
            if (towns.Count == 0) return null;

            var target = towns[_rng.Next(towns.Count)];
            try { target.Town.Prosperity = 0f; } catch { }
            try { target.Town.Security   = 0f; } catch { }
            try { target.Town.FoodStocks = 0f; } catch { }
            try
            {
                // Clear garrison roster
                if (target.Town.GarrisonParty?.MemberRoster != null)
                {
                    foreach (var elem in target.Town.GarrisonParty.MemberRoster.GetTroopRoster().ToList())
                        try { target.Town.GarrisonParty.MemberRoster.AddToCounts(elem.Character, -elem.Number); } catch { }
                }
            }
            catch { }

            return target.Name?.ToString();
        }

        // ── Deferred: FireAshenMachineryConsequence — 14 days after option D ──
        private static void FireAshenMachineryConsequence()
        {
            if (MageKnowledge._deferredInquiry != null) { _ashenMachineryCountdown = 1; return; }

            var validKingdoms = Kingdom.All
                .Where(k => !k.IsEliminated && k.StringId != "ashen_kingdom"
                         && (IFaction)k != Hero.MainHero?.MapFaction)
                .ToList();

            if (validKingdoms.Count == 0) return;

            var targetK = validKingdoms[_rng.Next(validKingdoms.Count)];
            string cityName = DevastateCityInKingdom(targetK);

            string cityLine = string.IsNullOrEmpty(cityName)
                ? $"A city in {targetK.Name}'s territory"
                : $"{cityName}, in {targetK.Name}'s territory";

            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "⚗  The Machine Fires",
                    $"Fourteen days ago you sold something to someone who did not ask questions. " +
                    $"Today, {cityLine} has been struck by something the survivors cannot describe — only that it came from nowhere and took everything with it. " +
                    $"Prosperity gone. Food gone. The garrison walks out empty-handed. " +
                    $"No one is pointing at you. The chain of hands is long. " +
                    $"You know what you sold.",
                    new List<InquiryElement> { new InquiryElement("ok", "You know.", null, true, "") },
                    false, 1, 1, "Close", "",
                    _ => { }, null, "", false), false, true);
            };
        }
    }
}
