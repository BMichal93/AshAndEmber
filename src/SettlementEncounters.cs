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
// │ The Lightened Purse         │ Leave city/castle     │ General          │
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
            bool village = s.IsVillage;
            bool town    = s.IsTown || s.IsCastle;

            var pool = new List<Action<Settlement>>();

            if (village)
            {
                pool.Add(E_BanditWarning);
                if (_familyFeverCooldown == 0 && HasSpouseAndChild()) pool.Add(E_TheWasting);
                if (_hedgeWitchCooldown == 0 && HasHedgeWitchCondition()) pool.Add(E_NightVisitor);
                pool.Add(EV2_TravelingMonk);
                pool.Add(EV2_WiseWomanWarning);
                if (ren >= 600f) pool.Add(EV2_ChildNamedAfterYou);
                if (ren >= 200f) pool.Add(EV4_VillageTrial);
                pool.Add(EV7_WatchedVillage);
                pool.Add(EV8_WrongWound);
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
                    pool.Add(E_WarmthMerchant);
                    pool.Add(EV4_GiftedChild);
                    pool.Add(EV7_SelfTaughtMage);
                    pool.Add(EV8_TheLie);
                    if (ren >= 600f) pool.Add(EV7_OldMastersStudent);
                    pool.Add(E_EmberTithe);
                    if (_childEventCooldown == 0 && HasEligibleChild()) pool.Add(E_DarkeningInheritance);
                }
                if (ashen) pool.Add(EV2_DogWontStop);
            }
            if (town)
            {
                pool.Add(E_OldEnemy);
                pool.Add(EC2_CityQuarantine);
                if (_familyFeverCooldown == 0 && HasSpouseAndChild()) pool.Add(E_TheWasting);
                if (_hedgeWitchCooldown == 0 && HasHedgeWitchCondition()) pool.Add(E_NightVisitor);
                if (ren >= 150f) pool.Add(EC3_Philosopher);
                if (ren >= 250f) pool.Add(EC8_MerchantLedger);
                if (ren >= 300f) pool.Add(EC8_ReluctantOfficial);
                if (ren >= 400f) pool.Add(EC2_NoblewomansInvitation);
                if (ren >= 400f) pool.Add(EC5_PortraitPainter);
                pool.Add(EC3_GuardsExtorting);
                pool.Add(EC4_PrisonerBlock);
                pool.Add(EC7_GreyCloaks);
                pool.Add(EC8_Followed);
                pool.Add(EC_LocalPriest);
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
                    pool.Add(E_CuriousScholar);
                    pool.Add(EC2_StreetPreacher);
                    pool.Add(EC4_FakeMage);
                    pool.Add(EC5_PhysiciansEye);
                    pool.Add(EC6_AlchemistFire);
                    if (ren >= 1000f) pool.Add(E_CrowdWantsSign);
                    pool.Add(E_EmberTithe);
                    if (_childEventCooldown == 0 && HasEligibleChild()) pool.Add(E_DarkeningInheritance);
                }
                pool.Add(EC9_AshenElixir);
                if (ashen) pool.Add(EC7_AshenSurveillance);
                if (ashen) pool.Add(E_FellowCold);
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
                pool.Add(LV8_BattleSetup);
                pool.Add(LV_ColdEmbrace);
                pool.Add(LV_ColdDream);
                pool.Add(LV_ThreeWitches);
                pool.Add(LV_HollowHour);
                if (mage)
                {
                    pool.Add(E_MothersPlea);
                    pool.Add(E_WidowsPyre);
                }
            }
            if (town)
            {
                pool.Add(E_LightenedPurse);
                pool.Add(E_InsultAtGate);
                pool.Add(LC2_ChildPickpocket);
                if (ren >= 300f) pool.Add(E_BardsRequest);
                if (ren >= 400f) pool.Add(LC2_PartingGift);
                pool.Add(LC7_DeadGuard);
                pool.Add(LC8_GateStandoff);
                pool.Add(LC8_ForgeryAtGate);
                if (mage)
                {
                    pool.Add(LC2_WrongnessInAir);
                    pool.Add(LC_BloodCollector);
                }
                if (ashen) pool.Add(LC4_RecognizedByAshen);
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
            bool mage = MageKnowledge.IsMage;
            float ren = Hero.MainHero?.Clan?.Renown ?? 0f;
            var pool  = new List<Action>();

            pool.Add(EB_BattlefieldPriest);
            if (ren >= 400f && _lastBattleWon) pool.Add(EB_HeraldAfterVictory);
            if (mage && _lastBattleWon) pool.Add(EB6_SurvivorMageDuel);
            pool.Add(EB8_FieldTriage);
            pool.Add(EB8_BattleDebrief);
            if (mage) pool.Add(EB_EnemyMageJournal);

            FireBattle(pool);
        }

        private static void TryFireSiege()
        {
            bool mage = MageKnowledge.IsMage;
            bool attWon = _lastBattleAsAttacker && _lastBattleWon;
            var pool = new List<Action>();

            if (attWon)
            {
                pool.Add(ES_FirstNight);
                pool.Add(ES3_PoisonedWell);
                pool.Add(ES8_SiegeStores);
                if (mage) pool.Add(ES4_AshenCrystal);
                if (mage) pool.Add(ES7_FallenLaboratory);
                if (mage && _ancientBookFound == 0) pool.Add(ES_AncientGrimoire);
            }
            else
            {
                // Defender won or draw — still interesting
                pool.Add(EB_BattlefieldPriest);
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
                "A woman in rough-spun wool steps into your path as you ride out. She carries a small child — by a single look you can see it is burning with fever. She has heard what you carry inside you. She cries and offers nothing but her prayers.",
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

        // 2. The Widow's Pyre
        private static void E_WidowsPyre(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Widow's Pyre",
                "A grey-haired woman waits at the village edge beside a wrapped body on a bier. Her husband died this morning. The village priest is three days' ride away. She has heard that your fire is not like other fire — that it burns clean and true — and asks you to send him on.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Grant it. Light his pyre with the inner fire.", null, true,
                        "The fire knows how to honour the dead. Others may hear of it."),
                    new InquiryElement("b", "Decline gently. This is not what the fire is for.", null, true,
                        "The road continues."),
                    new InquiryElement("c", "Agree — but name a price.", null, true,
                        "She will pay. You will carry the weight of it."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgePlayer(1);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ChangeRenown(5f);
                            Msg("The pyre catches with a single breath. It burns gold and white, not orange. The woman watches until there is only ash. She does not weep — she looks satisfied.", FireColor);
                            break;
                        case "b":
                            Msg("You tell her the fire is not a priest's tool. She nods slowly, as if she expected nothing else from the world.", DimColor);
                            break;
                        case "c":
                            ChangeGold(200);
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("She pays without hesitation. The pyre burns. You feel the weight of the coin heavier than it should be.", BadColor);
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

        // 15. The Warmth Merchant
        private static void E_WarmthMerchant(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚖  The Warmth Merchant",
                "A nervous merchant is selling small clay pendants, claiming they are \"fire-touched — blessed by a real mage, keeps fever away, keeps the cold off.\" The pendants are ordinary clay. He has sold six already.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Expose him. You know exactly what these are.", null, true,
                        "The truth has its uses. So does being seen to carry it."),
                    new InquiryElement("b", "Make him an offer: you will make a real one for him to sell, for a cut.", null, true,
                        "There is profit here, if you want it."),
                    new InquiryElement("c", "Buy one as a joke. You can afford the amusement.", null, true,
                        "An amusement. Not your finest hour."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeRenown(5f);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You hold up one of his pendants and let a small warmth into it — then let it go cold. \"That is what his charms feel like,\" you tell the buyers. The merchant folds his stall quickly.", FireColor);
                            break;
                        case "b":
                            ChangeGold(300);
                            AgePlayer(1);
                            Msg("You spend a quiet hour doing something small and strange to a dozen clay pieces. They will hold warmth longer than they should. The merchant looks at them wide-eyed. The arrangement is profitable.", GoldColor);
                            break;
                        case "c":
                            ChangeGold(-50);
                            Msg("You turn it over in your palm. Ordinary clay. You pocket it anyway — it will make a fine illustration someday.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 19. The Warning
        private static void E_BanditWarning(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  The Warning",
                "An old woman stops you as you ride in. She tells you the north road past the village has bandits on it — saw them herself this morning, eight or nine, camped in the tree-line. She is not asking anything of you. She is just telling you.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Thank her and leave a coin for her trouble.", null, true,
                        "She risked nothing. You might spend something."),
                    new InquiryElement("b", "Ask for more details — position, numbers, armed?", null, true,
                        "There may be more to learn, if you ask."),
                    new InquiryElement("c", "Nod and ride past.", null, true,
                        "The road continues."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(-100);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("She pockets the coin without looking at it. \"The Ashen have made everyone dangerous,\" she says. You believe her.", GoodColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("\"Eight, maybe ten. Short bows. A wagon they haven't moved in two days.\" She has been watching longer than this morning.", DimColor);
                            break;
                        case "c":
                            Msg("You file the warning away. Bandits on the north road. Noted.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═════════════════════════════════════════════════════════════════════
        // LEAVE CITY/CASTLE — GENERAL
        // ═════════════════════════════════════════════════════════════════════

        // 24. The Lightened Purse
        private static void E_LightenedPurse(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Lightened Purse",
                "A day after leaving the city, your treasurer informs you that a purse is lighter than it should be. A pickpocket — and a skilled one — worked the crowd near the gate.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Send men back to find the thief.", null, true,
                        "Your men are capable. Results are not certain."),
                    new InquiryElement("b", "Accept the loss. Cities are cities.", null, true,
                        "An expensive lesson."),
                    new InquiryElement("c", "Have your guards make an example of likely suspects.", null, true,
                        "Rough justice. The outcome is uncertain, and the method is not clean."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(200);
                            Msg("Your men find the thief in an alley. The purse is returned. The thief is released with a bruise and a warning.", GoldColor);
                            break;
                        case "b":
                            ChangeGold(-200);
                            Msg("Two hundred gold is the price of learning not to trust city crowds. Expensive lesson.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            ChangeCrime(10f);
                            if (_rng.Next(2) == 0)
                            {
                                ChangeGold(200);
                                Msg("The right man is found — or at least a man with the coins. The method is ugly but the result is satisfying in a way that costs something.", BadColor);
                            }
                            else
                            {
                                Msg("The wrong man is roughed up. The real thief is long gone. The city guard notes what happened.", BadColor);
                            }
                            break;
                    }
                }, null, "", false), false, true);
        }

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

        // 30. An Insult at the Gate
        private static void E_InsultAtGate(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚜  An Insult at the Gate",
                "A minor lord — drunk, red-faced, standing with two companions who are pretending not to be embarrassed — makes a loud remark about your clan's origins in front of a small crowd. It is specific enough to be intentional.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Challenge him on the spot.", null, true,
                        "A public test. The outcome is not guaranteed."),
                    new InquiryElement("b", "Ignore it with visible dignity.", null, true,
                        "Dignity costs nothing. People will notice."),
                    new InquiryElement("c", "Report the insult to the city lord.", null, true,
                        "There are quieter ways to settle things."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (_rng.Next(3) != 0)
                            {
                                ChangeRenown(20f);
                                ChangeRelWithOwner(s, -10);
                                Msg("The duel is brief. He is not entirely incompetent — just drunk. You end it cleanly and sheathe your blade without a word. The crowd remembers.", GoodColor);
                            }
                            else
                            {
                                ChangeRenown(-10f);
                                Msg("He is not as drunk as he looked. You take a cut that you did not expect. You ride out saying nothing but the silence is harder to maintain.", BadColor);
                            }
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ChangeRenown(5f);
                            Msg("You look at him for a moment — nothing threatening, nothing yielding — then ride on. His companions look away first. That is sufficient.", GoodColor);
                            break;
                        case "c":
                            ChangeRelWithOwner(s, 5);
                            Msg("The city lord's man takes your account with barely concealed irritation at the minor lord's behaviour. Something will be said. Privately.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═════════════════════════════════════════════════════════════════════
        // ENTER CITY/CASTLE — MAGE
        // ═════════════════════════════════════════════════════════════════════

        // 31. The Curious Scholar
        private static void E_CuriousScholar(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Curious Scholar",
                "A university scholar — young, coat covered in chalk marks — has been watching the city gate for you specifically. He has a theory about the inner fire and wants to test it. He has three pages of notes already. He looks like he has not slept.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Give him a demonstration and answer his questions.", null, true,
                        "Two days of questions. What you show him will travel."),
                    new InquiryElement("b", "Decline. The gift is not a subject for study.", null, true,
                        "The road continues."),
                    new InquiryElement("c", "Give him false information and collect his fee.", null, true,
                        "He will believe what you tell him. That costs something."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgePlayer(2);
                            ChangeRenown(20f);
                            Msg("You spend the afternoon showing him things that take a cost to show. He fills both sides of every page he has. \"The fire is not magic,\" he says finally. \"It is something older than magic.\" You think he might be right.", FireColor);
                            break;
                        case "b":
                            Msg("He takes his notes and his theory back to his room. He will find someone else eventually. Or he will figure it out himself.", DimColor);
                            break;
                        case "c":
                            ChangeGold(200);
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("You tell him a plausible story with enough detail to feel real. He pays you and begins writing immediately. The theory he builds will be wrong in interesting ways.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 35. The Fellow Cold
        private static void E_FellowCold(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Fellow Cold",
                "Moving through the city crowd, you see the grey hair and pale eyes of an Ashen lord you recognize — not well, but enough. They see you. Neither of you moves. The crowd parts around both of you.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Acknowledge them — a nod, no more.", null, true,
                        "A nod means something, between such as you."),
                    new InquiryElement("b", "Step aside and let them pass without contact.", null, true,
                        "The moment passes."),
                    new InquiryElement("c", "Cross the crowd and speak to them directly.", null, true,
                        "You may find common ground — or something less comfortable."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeRelWithRandomLord(10);
                            Msg("The nod is returned. Nothing is said. Among the cold, silence is a kind of conversation.", AshenColor);
                            break;
                        case "b":
                            Msg("You step aside. They pass. The cold in the air does not come from the weather.", DimColor);
                            break;
                        case "c":
                            if (_rng.Next(2) == 0)
                            {
                                ChangeRelWithRandomLord(20);
                                Msg("They stop. You speak briefly — nothing that could be reported, everything that matters. You part without explanation. The crowd moved around you as if you were two stones in a river.", AshenColor);
                            }
                            else
                            {
                                ChangeCrime(10f);
                                Msg("Their eyes go hard before you reach them. Whatever they expected, you are not it. You withdraw before the situation becomes public.", BadColor);
                            }
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 36. The Crowd Wants a Sign
        private static void E_CrowdWantsSign(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Crowd Wants a Sign",
                "Word spreads faster than you do. A crowd has formed at the city gate — not hostile, not petitioning. Watching. Someone shouts that you should show them the fire. Others take it up. Your reputation has preceded you to a degree that is either flattering or dangerous.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Give them something worth seeing.", null, true,
                        "The fire given freely has weight. Your men will feel it."),
                    new InquiryElement("b", "Decline quietly and ride in.", null, true,
                        "Some will respect the refusal. Others will not."),
                    new InquiryElement("c", "Do something small and charge for the sight.", null, true,
                        "A small thing for coin. Not your finest hour."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgePlayer(2);
                            ChangeRenown(30f);
                            AddMorale(10f);
                            Msg("You give them fire — not a trick, not a performance, but the real thing. The crowd goes quiet in the way people go quiet when they understand they are seeing something that will not come again. Your men ride in proud.", FireColor);
                            break;
                        case "b":
                            ChangeRenown(-5f);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("\"Not today,\" you say, and ride through the crowd. They part. The refusal is clean. Some people will respect it. Others will say you are afraid.", DimColor);
                            break;
                        case "c":
                            ChangeGold(300);
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("A small working, a passed hat. The crowd is satisfied with less than they thought they wanted. You are satisfied with the coin and less sure about everything else.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 45. The Field Priest
        private static void EB_BattlefieldPriest()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Field Priest",
                "A wandering priest — no faction's colors, just a grey robe and a lantern — has appeared at the edge of the battlefield and is asking permission to walk among the dead and speak words over both sides.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Allow it. Give him a coin for the work.", null, true,
                        "A coin from your own purse. Your men will notice."),
                    new InquiryElement("b", "Allow it.", null, true,
                        "Let him do his work. Something lifts."),
                    new InquiryElement("c", "Our dead only.", null, true,
                        "Your dead, properly attended."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(-100);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            AddMorale(5f);
                            Msg("He moves through the field until dark. Your men camp at a distance and watch the lantern. Nobody speaks much.", GoodColor);
                            break;
                        case "b":
                            AddMorale(3f);
                            Msg("He goes among them without hurry. The battlefield goes quiet in a different way while he works.", DimColor);
                            break;
                        case "c":
                            Msg("He nods and confines himself to your side of the field. He does not look troubled by the limit. He has been at this a long time.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 46. A Strange Heat (mage-gated)
        private static void EB_EnemyMageJournal()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  A Strange Heat",
                "Among the enemy dead, a satchel. You smell it from three feet away — old paper, scorched at the edges, and something underneath. Warmth. Not fire. The kind of warmth that knows things. A mage kept notes.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Spend the night reading it properly.", null, true,
                        "A day in their margins. What they knew was not nothing."),
                    new InquiryElement("b", "Take what seems useful and keep moving.", null, true,
                        "A few pages. Understanding them is another matter."),
                    new InquiryElement("c", "Leave it. Other fires are other fires.", null, true,
                        "The road continues."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgePlayer(1);
                            ChangeRenown(10f);
                            Msg("They were further along one path than you expected. Their fire ran hot and fast and it killed them for it. The notes stop mid-sentence. You read the last entry three times.", FireColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("Some formulations you recognise, some you don't. You fold the pages carefully. Understanding them will take longer than tonight.", FireColor);
                            break;
                        case "c":
                            Msg("You step over the satchel and keep moving. Whatever they knew, they carried it with them.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 47. The Herald's Visit (renown≥400, player won)
        private static void EB_HeraldAfterVictory()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚜  The Herald's Visit",
                "A herald in the colours of a neighbouring lord has ridden to the field while the smoke still rises. He is here to witness for his master. He bows carefully and says that your victory has been noted.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Receive him properly. Let him carry a full account.", null, true,
                        "An honest account travels far. Someone powerful will hear it."),
                    new InquiryElement("b", "A brief word and let him go.", null, true,
                        "Brief but sufficient. His master will note the courtesy."),
                    new InquiryElement("c", "Send him away. You have a battlefield to tend.", null, true,
                        "He will say you were occupied. That is true."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeRenown(10f);
                            ChangeRelWithRandomLord(5);
                            Msg("You give him twenty minutes and the full truth of what happened. He writes it in his ledger. His master will read that you did not exaggerate and did not diminish. That has a value.", GoodColor);
                            break;
                        case "b":
                            ChangeRelWithRandomLord(5);
                            Msg("A brief exchange. He has what he came for. He rides back before the ground cools.", DimColor);
                            break;
                        case "c":
                            Msg("He retreats gracefully. He will report that you were occupied. That is true.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 56. The First Night
        private static void ES_FirstNight()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚒  The First Night",
                "Your men are in the streets of the fallen city. The day is over. The question of what the evening holds is still open. Your captains are looking at you — not asking directly, but looking.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Strict order. No looting, no violence. This city is yours now, not a prize.", null, true,
                        "Your men will be unhappy. The city will remember."),
                    new InquiryElement("b", "One hour. Then order restored.", null, true,
                        "One hour is a long time. Some cities remember for a generation."),
                    new InquiryElement("c", "Declare it a clean capture. The work is done — and you mean it.", null, true,
                        "Clean is easier to carry home."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            AddMorale(-8f);
                            ChangeRenown(15f);
                            Msg("The order goes out. Some of your men are angry. The city folk open their shutters by midnight. By morning there are women putting bread on windowsills for your sentries. That is a different kind of victory.", GoodColor);
                            break;
                        case "b":
                            ChangeCrime(15f);
                            ShiftTrait(DefaultTraits.Honor, -1);
                            AddMorale(10f);
                            Msg("One hour. The streets are loud and then quiet. Restoring order takes two hours, not one. You do not count what was taken. Some cities remember this for a generation.", BadColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            AddMorale(5f);
                            Msg("\"The work is done.\" The captains pass it along. The men take the words at face value — they are tired, and a clean victory is easier to carry home than the other kind.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 63–67 — ENTER CITY (new)
        // ═══════════════════════════════════════════════════════════════════

        // 63. The Private Dinner (renown≥400)
        private static void EC2_NoblewomansInvitation(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚜  The Private Dinner",
                "Before you have stabled your horse, an invitation arrives from a city noblewoman whose name appears in conversations without being attached to any specific action. She wants to dine privately. The formality of the invitation suggests this is not entirely social.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Attend and play it carefully.", null, true,
                        "Careful diplomacy has its rewards."),
                    new InquiryElement("b", "Attend and speak plainly.", null, true,
                        "Honesty may surprise her. The evening could go differently than planned."),
                    new InquiryElement("c", "Decline gracefully.", null, true,
                        "She notes the refusal. The road continues."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeRenown(5f);
                            ChangeRelWithOwner(s, 10);
                            Msg("The dinner is excellent and the conversation is careful. You each take what you came for and leave the rest unsaid. This is called diplomacy.", DimColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ChangeRelWithOwner(s, 10);
                            Msg("She seems surprised to find honesty where she expected maneuvering. The conversation changes register halfway through the meal. You leave understanding each other, which was not what either of you planned.", GoodColor);
                            break;
                        case "c":
                            Msg("She will note the decline. She will also note it was declined gracefully. Neither helps nor hurts, but it is logged.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 64. The Man Against the Fire (mage-gated)
        private static void EC2_StreetPreacher(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Man Against the Fire",
                "A street preacher has gathered a small crowd near the main gate. He is describing what mages do to good people in specific and lurid terms. Several of his claims are not entirely wrong. He has not seen you yet.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Confront him publicly — let the crowd hear both sides.", null, true,
                        "The crowd will see the gap between story and fact. Not without cost."),
                    new InquiryElement("b", "Walk past. You've heard it before and worse.", null, true,
                        "Some fires cannot be starved."),
                    new InquiryElement("c", "Press a coin into his collection box as you pass.", null, true,
                        "An unexpected gesture. The crowd may find it funnier than expected."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeRenown(5f);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ChangeCrime(5f);
                            Msg("He recognises what you are when you stop. The crowd shifts, aware suddenly that this is a different kind of lecture. You speak for three minutes. You don't argue his claims — you simply stand there carrying the fire and let the crowd see the gap between the story and the fact.", FireColor);
                            break;
                        case "b":
                            Msg("He is still talking as you ride past. He will be talking when you leave. Some fires cannot be starved by ignoring them.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You drop a coin into his tin as you pass. He stops mid-sentence and stares at it, then at you, then at the coin again. The crowd finds this funnier than they expected.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 65. The Quarantine Gates
        private static void EC2_CityQuarantine(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✚  The Quarantine Gates",
                "The city gates are barred except for one lane, and a city physician is turning people back. There is a fever moving through the eastern quarter. Entry is permitted for known lords, but the physician looks at your party with obvious calculations happening behind her eyes.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Offer your party's medical supplies to the sick quarter.", null, true,
                        "A gift of your own supplies. The physician's expression may change."),
                    new InquiryElement("b", "Enter by right. You have business here.", null, true,
                        "Your right of entry is not contested. Your men will notice the sick quarter."),
                    new InquiryElement("c", "Turn back and camp outside until it clears.", null, true,
                        "The city can wait."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(-300);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("The physician's expression changes. She waves your party through and immediately turns to unpack what you left. Your men enter a quieter city than usual.", GoodColor);
                            break;
                        case "b":
                            AddMorale(-3f);
                            Msg("She waves you through without protest. Your men pass the sick quarter without looking at it directly. Everyone is thinking the same thing and nobody says it.", DimColor);
                            break;
                        case "c":
                            Msg("You set up camp outside the walls. The city can wait. Your men are not displeased by this decision.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 68–72 — LEAVE CITY (new)
        // ═══════════════════════════════════════════════════════════════════

        // 68. The Small Thief
        private static void LC2_ChildPickpocket(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Small Thief",
                "Your sergeant has caught a child — twelve at most, feet bare, ribs visible through a thin shirt — with a hand in your saddlebag. He is holding the child by the collar and looking at you for instruction.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Let them keep what they took and add a coin.", null, true,
                        "Something for the road ahead of them, at a cost to you."),
                    new InquiryElement("b", "Take it back and release them without a word.", null, true,
                        "The moment passes."),
                    new InquiryElement("c", "Hand them to the city watch.", null, true,
                        "The law has teeth. So does using them on children."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ChangeGold(-150);
                            Msg("\"Keep it,\" you say, and press a second coin on top. The child bolts before you can say anything else. Your sergeant watches them go with an expression that is not entirely professional.", GoodColor);
                            break;
                        case "b":
                            Msg("You hold out your hand. The child gives back what was taken and is released. They walk away at normal speed for exactly ten steps, then run.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            ChangeCrime(5f);
                            Msg("The watch takes them. Your sergeant says nothing. Three of your men find reasons to be looking elsewhere.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 71. A Parting Gift (renown≥400)
        private static void LC2_PartingGift(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  A Parting Gift",
                "As you reach the city gate, a servant catches up with your party carrying a wrapped gift from a lord you dined with. The gift is appropriate in value — not a bribe, not an insult. A gesture.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Accept graciously and send word of thanks.", null, true,
                        "A gesture returned. The relationship continues."),
                    new InquiryElement("b", "Return it with respect — you prefer not to carry obligations.", null, true,
                        "You prefer not to carry obligations. He will understand."),
                    new InquiryElement("c", "Accept it and send nothing back.", null, true,
                        "The gift accepted. The road continues."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeRelWithOwner(s, 10);
                            Msg("The servant carries your thanks back. The lord will hear it before evening.", DimColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ChangeRelWithOwner(s, 5);
                            Msg("The servant carries the gift back with your compliments. The lord will understand the message: you appreciate the gesture and don't require the currency.", GoodColor);
                            break;
                        case "c":
                            ChangeRelWithOwner(s, 5);
                            Msg("You accept and ride on. The gift is practical. The relationship continues.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 72. Something Smothered (mage-gated)
        private static void LC2_WrongnessInAir(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  Something Smothered",
                "You feel it as your party reaches the gate — a wrongness, like a fire that has been deliberately put out in a small and specific place. Not a natural cold. Someone has been working at suppressing something inside this city, and the absence of it is loud to your fire.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Go back and find the source.", null, true,
                        "A day spent finding what the fire already told you. It may be worse than you expected."),
                    new InquiryElement("b", "Trust your fire and ride. You know what this means.", null, true,
                        "The knowledge travels with you."),
                    new InquiryElement("c", "Report what you felt to the city lord.", null, true,
                        "The city lord will not understand what you describe. They will remember that you said it."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgePlayer(1);
                            ChangeRenown(5f);
                            Msg("You find it in the merchant quarter: a room in a trading house whose walls have been subtly worked. Not by fire — by its absence. Someone has been conditioning this room for Ashen use. The merchant who rents it hasn't been seen in three days.", AshenColor);
                            break;
                        case "b":
                            Msg("The Ashen have been in this city longer than anyone knows. That is what the absence means. You ride with that knowledge and decide what to do with it.", DimColor);
                            break;
                        case "c":
                            ChangeRelWithOwner(s, 5);
                            Msg("The lord listens carefully and writes down what you describe. He does not know what it means. He is frightened by the fact that you do.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 73–77 — ENTER VILLAGE (new)
        // ═══════════════════════════════════════════════════════════════════

        // 73. The Teaching Monk
        private static void EV2_TravelingMonk(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Teaching Monk",
                "An itinerant monk has set up a makeshift school at the inn — six or seven children sitting on the floor, trying to read from a single copied page. He looks underfed and completely undeterred.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Donate to his work generously.", null, true,
                        "A gift large enough to matter. Word travels."),
                    new InquiryElement("b", "Watch for a moment and leave a small coin.", null, true,
                        "A small gesture. He will not stop for it."),
                    new InquiryElement("c", "Offer him a place with your party — traveling with a lord is safer.", null, true,
                        "The offer is honest. His answer may surprise you."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(-300);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ChangeRenown(5f);
                            Msg("He receives the donation as if receiving a problem he will need to solve — where to get more pages, how to keep the children's interest through winter. You leave him making lists.", GoodColor);
                            break;
                        case "b":
                            ChangeGold(-50);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("He pockets the coin without breaking his lesson. One of the children is watching you instead of the page.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("He thinks about it for a moment, then shakes his head. \"These children will need someone here in the spring,\" he says. \"But thank you.\" You leave respecting the answer.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 75. The Wise Woman
        private static void EV2_WiseWomanWarning(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Wise Woman",
                "An old woman the villagers step around carefully asks to speak with you. She has been watching the road from her window for three days, she says. She knows something about what is ahead — not from rumour. From other means.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Hear her out and adjust your route accordingly.", null, true,
                        "She may know something worth heeding."),
                    new InquiryElement("b", "Pay for more detail.", null, true,
                        "More detail costs more. What she knows is specific."),
                    new InquiryElement("c", "Thank her politely and file it under general caution.", null, true,
                        "The road continues."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("She tells you the north fork smells wrong — ash-cold, she says. You have no reason to trust this and three good reasons not to. You take the south fork anyway. Nothing happens. That is the best possible outcome.", DimColor);
                            break;
                        case "b":
                            ChangeGold(-200);
                            string[] wiseWoman = {
                                "\"Three days ago a cold party moved east. Grey cloaks. No fire in camp. Eight of them, maybe ten. They were not hungry — that is the worst kind.\"",
                                "\"The stream north of here has ash in it. Not wood ash. Bone ash. Something was burned there not long ago.\"",
                                "\"A lord came through six days back with too many soldiers for a peace-time escort and too few for a campaign. He was not going anywhere he wanted to be seen going.\"",
                            };
                            Msg(wiseWoman[_rng.Next(wiseWoman.Length)], DimColor);
                            break;
                        case "c":
                            Msg("You thank her and ride on. She does not argue. She has given her warning. What you do with it is not her problem.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 76. Your Name (renown≥600)
        private static void EV2_ChildNamedAfterYou(Settlement s)
        {
            bool mage = MageKnowledge.IsMage;
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  Your Name",
                "The headman intercepts you at the village entrance with the expression of a man trying to decide if this is an honor or an imposition. A child born three weeks ago has been named after you. He wanted you to know.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Ask to see the child and give a proper blessing.", null, true,
                        mage ? "A proper blessing costs something. The mother will remember." : "A proper moment. The village will remember."),
                    new InquiryElement("b", "Acknowledge it warmly and leave a gift.", null, true,
                        "A gift that matters to a family of this size."),
                    new InquiryElement("c", "Ask them gently to choose another name — it is a burden.", null, true,
                        "You spare the child the weight of a name like yours. The headman may be secretly relieved."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (mage) AgePlayer(1);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ChangeRenown(5f);
                            Msg("The child is brought out, asleep, unbothered by the weight of the name they were given. You say the words. The mother looks at you with an expression you will remember for a long time.", GoodColor);
                            break;
                        case "b":
                            ChangeGold(-300);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You leave a gift large enough to matter to a family of this size. The headman thanks you. The child sleeps on, indifferent.", GoldColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("The headman blinks. You explain — it is a heavy thing to carry, a name like yours; the child might prefer the freedom of their own history. The headman nods slowly. You think he was secretly hoping you would say exactly that.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

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

        // 87. The Toll
        private static void EC3_GuardsExtorting(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚖  The Toll",
                "Two city guards are blocking a young merchant's cart and demanding an \"inspection fee\" that appears nowhere in any tariff you know. She is paying because she doesn't see another way through. The guards haven't noticed your party yet.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Step in. Those fees are invented.", null, true,
                        "The guards recalculate their position. Not without cost to your standing with the city."),
                    new InquiryElement("b", "Pay what they're demanding on her behalf.", null, true,
                        "A gift from your purse. The merchant's expression will be complicated."),
                    new InquiryElement("c", "Every city has its corner tolls. Keep moving.", null, true,
                        "Every city has its corner tolls. Not every toll needs your eyes."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ChangeRenown(5f);
                            ChangeRelWithOwner(s, -5);
                            Msg("You name the tariff law. The guards recalculate their understanding of the situation very quickly. The merchant goes through. She looks at you once — not gratitude, exactly. Recognition.", GoodColor);
                            break;
                        case "b":
                            ChangeGold(-200);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You pay it yourself. The guards are briefly confused. The merchant gives you a look that is more complicated than a thank-you and moves on.", GoldColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("You keep moving. The fee gets paid. The cart goes through. The guards find someone else before the hour is out.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 88. The Question
        private static void EC3_Philosopher(Settlement s)
        {
            bool mage = MageKnowledge.IsMage;
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Question",
                "A man with ink-stained fingers and a year's worth of notes under his arm has been waiting specifically for you. He has one question about the Ashen — not academic. He has been thinking about this for years, and he is clearly afraid the answer will confirm what he suspects.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Give him the time the question deserves.", null, true,
                        "A question worth an hour of your time."),
                    new InquiryElement("b", "A brief answer and keep moving.", null, true,
                        "A brief answer for a long question."),
                    new InquiryElement("c", mage ? "Show him, briefly, what the fire sees when it looks at the ash." : "Tell him what you have seen with your own eyes.", null, true,
                        mage ? "What you show him he cannot un-see." : "He will write this down."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeRenown(5f);
                            Msg("You sit with him for an hour. His question — once you get past the framing — is whether the Ashen chose the cold or the cold chose them. You tell him honestly: you don't know, and that is the part that keeps you up.", DimColor);
                            break;
                        case "b":
                            Msg("You give him the short version. He writes it down in the margin of everything else he brought. The short version is what he will quote. You hope it holds up.", DimColor);
                            break;
                        case "c":
                            if (mage)
                            {
                                AgePlayer(1);
                                Msg("You let a thread of fire move toward the part of yourself that remembers the Ashen you have known. He gasps. Not from the heat — from understanding something he has only described in words until now. He closes his notes. He will not write this part down.", FireColor);
                            }
                            else
                            {
                                ShiftTrait(DefaultTraits.Honor, 1);
                                Msg("You tell him what you saw — battle, aftermath, what the Ashen do to the things they touch. He writes faster than you speak. He thanks you with the look of a man who just confirmed the worst possible answer and is somehow relieved to know.", DimColor);
                            }
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 102. The Trial
        private static void EV4_VillageTrial(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚖  The Trial",
                "A ring of villagers, a kneeling man, and a headman with a written list of grievances. Petty theft — three sacks of grain, taken in winter when he had none. The sentence being settled on is exile. His wife and two children are standing at the edge of the ring.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Intervene. Exile for winter theft is too much.", null, true,
                        "A word of authority, well placed."),
                    new InquiryElement("b", "Pay the value of the grain yourself and end it.", null, true,
                        "The debt settled. Something releases."),
                    new InquiryElement("c", "Let it proceed. Village justice is village justice.", null, true,
                        "Village justice proceeds."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ChangeRenown(5f);
                            Msg("You speak briefly and with authority. The headman listens — you are a lord, and lords outrank winter sentiment. The man is fined instead of exiled. His wife does not look at you. She looks at her children.", GoodColor);
                            break;
                        case "b":
                            ChangeGold(-200);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You pay the value without ceremony and tell the headman the debt is settled. The man kneeling in the mud stares at the ground for a long moment before standing. The ring disperses.", GoodColor);
                            break;
                        case "c":
                            Msg("You watch from the road. The sentence is finalized. The man stands up and begins walking without collecting anything from his house. His family follows.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

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

        // 109. The Block
        private static void EC4_PrisonerBlock(Settlement s)
        {
            bool mage = MageKnowledge.IsMage;
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚖  The Block",
                "A group of prisoners is being auctioned in the city square into indentured service — several years' labour for debts that may or may not be documented. Most of them are wrong for criminals: clothing, bearing. One of them has fire-worker's calluses and is watching everything carefully — someone who knows exactly what is happening to them.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Buy their freedom. All of them.", null, true,
                        "All of them, at significant cost. Your coin is as valid as anyone's."),
                    new InquiryElement("b", "Report the auction's legality to the city lord.", null, true,
                        "The law will begin its work. Slowly."),
                    new InquiryElement("c", "Ride past.", null, true,
                        "The auction continues. You were there."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(-800);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            if (mage)
                                Msg("You pay for all of them. As they scatter, the fire-worker pauses beside your horse. They know what you are. They say nothing. They nod once. Then they go. The fire in you turns to watch them leave.", FireColor);
                            else
                                Msg("You pay for all of them. The auctioneer adjusts his ledger without argument — your coin is as valid as anyone's. They go in six directions before the crowd has dispersed.", GoodColor);
                            break;
                        case "b":
                            ChangeRelWithOwner(s, 5);
                            Msg("The lord's officer takes your complaint with the expression of a man who already knew and was hoping nobody official would notice. An investigation is opened. It will conclude in weeks, by which time the auction will have concluded also.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("You ride past. The auctioneer continues. The fire-worker's eyes track you until the crowd closes.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 110. The Impostor (mage-gated)
        private static void EC4_FakeMage(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Impostor",
                "In the market, a man in conspicuously dramatic clothing is performing 'fire blessings' for coin — tricks with hidden flint and powder, practiced patter, a crowd that wants to believe. He is good at his pitch. He has not seen you arrive. Your fire identifies what his is immediately: nothing.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Expose him. Let the crowd hear the difference.", null, true,
                        "The crowd hears the difference. His pitch ends here."),
                    new InquiryElement("b", "Let him work. People want to believe in fire — even the wrong kind.", null, true,
                        "People want to believe in fire. Even the wrong kind."),
                    new InquiryElement("c", "Approach him privately. Offer him a real lesson instead.", null, true,
                        "A day and a private lesson. What he does after is his choice."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeRenown(5f);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You walk through the crowd and hold your hand next to his torch. The difference is immediate and visible to everyone present. The crowd goes quiet in a way that is not comfortable for the performer. He packs his tin box and does not look at you while he does it.", FireColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You watch him work for a minute. He is genuinely talented at the craft of performance. The crowd gets something from it. You ride on, carrying the question of whether what they get is worth what they pay.", DimColor);
                            break;
                        case "c":
                            AgePlayer(1);
                            Msg("He sees your eyes and knows immediately. You find a quiet street. You show him one real thing — very small, not dangerous, but unmistakably true. He stares at his hands for a long time afterward. When he looks up his expression has changed entirely. He will not perform that act again. You don't know what he will do instead.", FireColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

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
        // EVENTS 118–119 — AFTER SIEGE (fourth batch)
        // ═══════════════════════════════════════════════════════════════════

        // 118. The Water
        private static void ES3_PoisonedWell()
        {
            bool mage = MageKnowledge.IsMage;
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "☠  The Water",
                "Your surgeon reports it an hour after the city falls: the main well was poisoned by the defenders before they surrendered. Something grey and chemical, not lethal in small amounts, but the families who drew water this morning are already showing symptoms. Your purification supplies will not cover the scale of it.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Commit everything available to treating the sick and sealing the well.", null, true,
                        "Everything committed. Your men may give up their own supplies."),
                    new InquiryElement("b", "Seal the well and send for specialist help from outside the city.", null, true,
                        "Help will come. The gap before it arrives is the cost."),
                    new InquiryElement("c", mage ? "There may be a way to use the fire to purify the water." : "Requisition all available stocks from the city's merchants.", null, true,
                        mage ? "The fire can sometimes clean. It costs accordingly." : "Partial coverage. Some suffering will not be prevented."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(-500);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            AddMorale(-5f);
                            Msg("Everything is committed. Your men give up their water rations without being asked — not all of them, but enough that the ones who don't quietly match the ones who do by the second hour. The city knows what your party did here. It will know for a long time.", GoodColor);
                            break;
                        case "b":
                            Msg("The well is sealed and sealed clearly. The message goes out for help. It will take two days to arrive. In those two days, people who drank this morning will feel it. Some of them will be children. You make the practical decision and carry the practical decision.", DimColor);
                            break;
                        case "c":
                            if (mage)
                            {
                                AgePlayer(2);
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                Msg("You spend two days doing something very precise with the fire — not burning, but the opposite of burning, a working so controlled it is almost silence. By the end the water runs clear and the fire has aged you accordingly. The city's physician tests it three times before trusting it. Your men don't ask how you did it.", FireColor);
                            }
                            else
                            {
                                ChangeGold(-400);
                                Msg("The merchants surrender their stocks under sufferance. It covers half the need. The other half waits for the supply wagons. It is a partial solution, which is what it is.", DimColor);
                            }
                            break;
                    }
                }, null, "", false), false, true);
        }

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

        // 125. The Likeness (renown≥400)
        private static void EC5_PortraitPainter(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Likeness",
                "A painter in the city market is selling small portrait copies of you — worked from a verbal description, apparently, but unnervingly accurate in the eyes and the set of the jaw. He has sold six. He bows elaborately when you appear, which draws a crowd.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Pose for a corrected version — if it exists, let it be right.", null, true,
                        "Your face, correctly rendered. The copies will follow."),
                    new InquiryElement("b", "Approve his enterprise and wish him well.", null, true,
                        "Grace costs nothing. He takes the endorsement and runs with it."),
                    new InquiryElement("c", "Buy all the existing copies.", null, true,
                        "You buy the copies. He will make more. You know this."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeRenown(10f);
                            Msg("You give him an hour. He makes adjustments with the focused urgency of a man who has been handed a professional second chance. The corrected version is better than the original and has your face exactly. He sells twelve before you leave the city.", GoodColor);
                            break;
                        case "b":
                            ChangeRenown(5f);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You tell him it is good work and ride on. He calls after you that he will include this endorsement in his pitch. The portrait trade continues. You have been told your likeness is now selling well in three other cities, which is either a compliment or a warning.", DimColor);
                            break;
                        case "c":
                            ChangeGold(-300);
                            Msg("You buy the six. He takes the coin and reaches under his stall for the stock he keeps for exactly this eventuality. There are four more. You buy those too. He smiles the smile of a man doing arithmetic. You ride on. He begins a new batch before the hour is out.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 126. The Physician's Question (mage-gated)
        private static void EC5_PhysiciansEye(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✚  The Physician's Question",
                "A city physician stops you at the gate — not for medical reasons, she says, but because she has been studying aging for twenty years and your face has done something irreparable to her professional certainty. She says it carefully: \"You look forty and you look eighty. At the same time. I have been trying to understand that since you entered the gate.\"",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Give her an honest explanation.", null, true,
                        "A day spent in honest account. What she understands may travel."),
                    new InquiryElement("b", "Deflect kindly. The fire is not a medical subject.", null, true,
                        "The polite version tells her something too."),
                    new InquiryElement("c", "Tell her she is mistaken.", null, true,
                        "She does not believe you. She will note that too."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgePlayer(1);
                            ChangeRenown(5f);
                            Msg("You tell her what it costs and what it gives. She listens with the complete attention of someone updating a life's framework in real time. She asks three questions that are so specific you have to think before answering. When you leave, she is already writing. Something in you is satisfied by being understood precisely.", FireColor);
                            break;
                        case "b":
                            Msg("\"It is an unusual condition,\" you say. She nods. She is already translating the polite version into whatever is actually true. She will write something accurate about you from the deflection alone.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("She looks at you for a moment. Then she looks at her notes. \"Mistaken,\" she repeats, the way people repeat a word they are storing. She thanks you for your time and goes back inside. You have given her more data than an honest answer would have.", BadColor);
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

        // 141. The Alchemist's Fire (mage-gated)
        private static void EC6_AlchemistFire(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Alchemist's Fire",
                "A shop two streets from the gate is burning. The city watch is present but keeping their distance — the smoke is wrong colours, which means the contents are volatile and the watch knows it. The owner is inside. He is alive: you can hear him, and so can the fire you carry, which is currently extremely interested in what he has in that building. He is not trying to escape. He is trying to save something.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Go in yourself — the fire won't harm you the same way.", null, true,
                        "The fire does not burn you the same way. He is in there."),
                    new InquiryElement("b", "Call to him and guide him out with your voice.", null, true,
                        "He may follow your voice out. The smoke is patient."),
                    new InquiryElement("c", "Contain the fire from outside — stop it spreading while the watch enters.", null, true,
                        "The building may be the cost of keeping the fire contained. He survives."),
                    new InquiryElement("d", "Ask the watch what he has in there before committing to anything.", null, true,
                        "What he has in there changes the calculation considerably."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgePlayer(1);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ChangeRenown(10f);
                            Msg("You walk through the burning door. The fire gives way around you. The alchemist is on his knees trying to seal a set of ceramic jars. You pick him up under one arm, take the jars under the other, and walk back out. The watch stares. The building comes down twenty seconds later. He is alive. The jars are intact. He says they contain something he has been twenty years developing and says nothing else for a long time.", FireColor);
                            break;
                        case "b":
                            if (_rng.Next(2) == 0)
                            {
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                ChangeRenown(5f);
                                Msg("Your voice reaches him and he follows it — the fire-carrier's voice has a quality in burning buildings that is difficult to explain and very easy to follow. He comes out coughing, clutching two ceramic jars, covered in soot. He saved what he needed. You saved him. He will want to discuss this at length when he can breathe.", GoodColor);
                            }
                            else
                            {
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                Msg("You call to him and he calls back and then the roof shifts and the calling stops. He is pulled out by the watch three minutes later, alive but badly burned, one of the jars still in his hand. He survives. It will be a long recovery. The jar is unbroken, which he appears to consider a reasonable exchange.", BadColor);
                            }
                            break;
                        case "c":
                            AgePlayer(1);
                            ChangeRenown(5f);
                            Msg("You keep the fire from the neighboring buildings with a working that costs a day's worth of years, and the watch gets him out. He is conscious and yelling about the jars. The building is ash. Half his work is ash. He is alive and furious, which is a better outcome than the alternative.", GoodColor);
                            break;
                        case "d":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("The watch sergeant says: suspended compounds, some experimental, at least two of which will explode if the temperature drops suddenly, which is what happens if you try to drown the fire with water. The alchemist comes out on his own, eventually, through the back, with the jars. He had a route the whole time. He just didn't want to leave without the work. Nobody asked him if he had a route.", DimColor);
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

        // ── ENTER VILLAGE: The Watched Village ─────────────────────────────
        private static void EV7_WatchedVillage(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Watched Village",
                "You feel it before you see it: three sets of eyes from the treeline that are too still to be villagers. Ashen scouts, posted here — which means someone sent them, and someone knows you were coming. The village is unaware. The scouts have not moved yet.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Ride straight at them. End this before it becomes something.", null, true,
                        "You choose the ground. They will be there."),
                    new InquiryElement("b", "Warn the village headman and set a watch — catch them if they move.", null, true,
                        "The village is alerted. The scouts lose the advantage."),
                    new InquiryElement("c", "Slip into the village without alerting them. Let them think you didn't notice.", null, true,
                        "A different kind of safety. They watch. So do you."),
                    new InquiryElement("d", "Ride in openly and loudly. Let them see your column's full size.", null, true,
                        "Size may be enough. It may not."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            SpawnAshenAtGate(s, 10, 50f);
                            Msg("You wheel your horse toward the treeline and your column follows. They don't run — Ashen scouts rarely do. They step out of the trees with the patience of something that has been cold long enough to stop hurrying. They are waiting at the gate.", WarnColor);
                            break;
                        case "b":
                            ChangeRelWithOwner(s, 5);
                            SpawnAshenAtGate(s, 6, 25f);
                            Msg("The headman alerts his people quietly. The scouts shift when they realise they have been seen. They lose the tree cover and commit early. Smaller than you expected — the advantage of surprise was most of their plan. They are at the gate with less than they intended to bring.", WarnColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You enter normally, eyes forward, giving them nothing. They watch. You watch them watching. The village receives you without knowing what is in the treeline. You ride out later knowing exactly where you are not safe — which is a different kind of safety.", DimColor);
                            break;
                        case "d":
                            if (_rng.Next(2) == 0)
                                Msg("The column's size reads correctly. The scouts withdraw north without hurrying — not afraid, just done. They will report your position and your number.", DimColor);
                            else
                            {
                                SpawnAshenAtGate(s, 10, 50f);
                                Msg("They don't retreat. Size, apparently, was not the relevant variable. They emerge from the trees and move toward the gate with the patience of something that has decided. They are there when you leave.", WarnColor);
                            }
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

        // ── ENTER CITY: The Grey Cloaks ────────────────────────────────────
        private static void EC7_GreyCloaks(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Grey Cloaks",
                "City watch reports that grey-cloaked figures have been moving through the market since this morning, asking specifically about you — your column's size, your route north, who rides at your left. Not merchants. Not scouts in any natural sense. They move like people who have been patient for a long time and are now less patient.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Find them now and end this before it ends somewhere of their choosing.", null, true,
                        "You choose the confrontation. They are not prepared for it."),
                    new InquiryElement("b", "Alert the city guard — if they're asking about a lord, the lord's guard should know.", null, true,
                        "The city is alerted. The grey cloaks will scatter — but not be caught."),
                    new InquiryElement("c", "Have your people track them while you conduct your business normally.", null, true,
                        "Your people tail them somewhere specific. What you learn may be worth more than the fight."),
                    new InquiryElement("d", "Do your business and leave faster than expected.", null, true,
                        "They anticipated the early departure. Not perfectly."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            SpawnAshenAtGate(s, 10, 55f);
                            Msg("You locate them in the market district — five, moving with the collective focus of a coordinated group — and your column changes direction toward them visibly. They see. They assess. They move toward the gate rather than away from it, because Ashen agents do not break toward safety on instinct; they break toward their objective. They will be at the gate.", WarnColor);
                            break;
                        case "b":
                            ChangeRelWithOwner(s, 5);
                            Msg("The watch captain receives your report and sends eight men. The grey cloaks dissolve into the market crowd with the practiced invisibility of people who have done this before. They are gone before the watch reaches the market. The watch finds nothing. The city is notified. The agents are elsewhere. The information about your route is already with whoever sent them.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("Your people tail them to a warehouse in the tanner's district that is nominally owned by a cloth merchant. The name on the lease is a front. The merchant's name connects to a trading house with Ashen-adjacent clients in three other cities. Your people pull back before they are spotted. You have an address, a name, and a network structure. The grey cloaks leave the city before evening without incident.", AshenColor);
                            break;
                        case "d":
                            SpawnAshenAtGate(s, 7, 30f);
                            Msg("You finish your business in half the time and move for the gate. They anticipated the early departure — not perfectly, but enough. Fewer than their original plan. Less coordinated. Still there.", WarnColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── ENTER CITY: Ashen Surveillance (ashen-gated) ───────────────────
        private static void EC7_AshenSurveillance(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  Recognised",
                "A figure in grey pauses at the sight of you entering the gate — not fear, recognition. Ashen, clearly. And they know what you are. They look at you the way Ashen look at something that is not quite what they expected to find in a human body. They do not move. They are deciding whether you are a complication or an opportunity.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Meet their eyes and hold them until they make a choice.", null, true,
                        "They approach. What follows is a cold exchange. You will find out what it is."),
                    new InquiryElement("b", "Move through the gate without acknowledgement — you have seen each other, nothing more.", null, true,
                        "You pass. They watch. Mutual awareness without confrontation — for now."),
                    new InquiryElement("c", "Signal hostility. You do not want them tracking your column.", null, true,
                        "You signal clearly. So do they. There will be more of them than you expected."),
                    new InquiryElement("d", "Send someone to them with a single question: why are you here?", null, true,
                        "You ask directly. They may answer, or the question may end the conversation entirely."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (_rng.Next(2) == 0)
                            {
                                ChangeRenown(5f);
                                Msg("They approach. They tell you — briefly, in the flat factual tone of an Ashen report — that there is a cold-marker in this city that was not placed by their side, and they want to know if you placed it. You didn't. They believe you. They tell you what it means if a third party is placing Ashen markers in human settlements. You ride away with that information.", AshenColor);
                            }
                            else
                            {
                                SpawnAshenAtGate(s, 8, 40f);
                                Msg("They hold your eyes and reach a conclusion. They turn and walk toward the gate. Not away — toward. They are going to be there when you leave, having assessed that a confrontation outside is preferable to whatever you might arrange inside. They will be at the gate.", WarnColor);
                            }
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You walk past. They watch you go without moving. Your column tracks their position as it passes. They stay visible long enough to make sure you noticed — they want you to know they are watching. You do your business in the city aware that you are being catalogued.", AshenColor);
                            break;
                        case "c":
                            SpawnAshenAtGate(s, 12, 60f);
                            Msg("They read your signal without confusion and begin moving with the unhurried efficiency of something that was prepared for this contingency. They have reinforcements — they were not alone in the city. More grey shapes detach from doorways and walls. They will be at the gate with numbers. You have chosen the ground, which was the right move. The numbers are less comfortable.", WarnColor);
                            break;
                        case "d":
                            if (_rng.Next(2) == 0)
                                Msg("They answer through your messenger: they are cataloguing the fire-carrier's movements as standard Ashen intelligence. They mean this as a neutral statement of fact. It is, in a way, reassuring — you are important enough to catalogue and not important enough to act on. For now. The messenger returns shaken by the quality of the stillness in them.", AshenColor);
                            else
                            {
                                Msg("Your messenger returns alone. The grey figure is gone. Three positions that appeared to be occupied are now empty. Whatever they were doing in this city, your inquiry ended it. They are not at the gate when you leave. They are somewhere else. This is not a comfort.", DimColor);
                            }
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── LEAVE CITY: The Dead Guard ─────────────────────────────────────
        private static void LC7_DeadGuard(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "☠  The Dead Guard",
                "The guard at the inner gate is dead. He has been propped up to look like he is standing at his post — someone needed this gate unwatched for a specific window of time. The window is now. Three grey-cloaked figures are moving through the gate passage with purposeful efficiency. They see you. You see them. There are thirty yards between you and the outcome is not yet fixed.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Pursue immediately — they used this gate for a reason and you need to know what.", null, true,
                        "Immediate pursuit. You catch them before they split."),
                    new InquiryElement("b", "Raise the alarm first, then pursue — the city guard needs to know a gate guard is dead.", null, true,
                        "The city guard needs to know. The fight will be smaller for it."),
                    new InquiryElement("c", "Follow one of them without engaging — find out where they go.", null, true,
                        "Following is quieter than fighting. What you find may be more useful."),
                    new InquiryElement("d", "Seal the gate and hold position — let them come to you on your ground.", null, true,
                        "You hold the chokepoint. The numbers may favour you."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            SpawnAshenAtGate(s, 10, 50f);
                            Msg("You pursue immediately through the passage and out the outer gate. They have split — two are running north, one has stopped and turned. The one who turned is the answer to why they chose this gate specifically. They were ready for pursuit. The two running are the distraction. They are at the outer gate.", WarnColor);
                            break;
                        case "b":
                            ChangeRelWithOwner(s, 5);
                            SpawnAshenAtGate(s, 7, 30f);
                            Msg("You pull the gate bell before you move. The guard response takes ninety seconds — long enough for two of them to clear the outer gate. The third waited too long, or was assigned to wait. The city guard arrives and takes the perimeter. You have three against one, with the city guard's weight behind you. The gate fight is short. The other two are already in the city by a different route.", WarnColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You follow the rearmost one through the outer gate at distance. They move east, then north, then into the tannery district and through a back door of a warehouse that should be empty at this hour. It is not empty. You note the address, the approach route, and the specific knock pattern. You do not enter. You ride away with more than a fight would have given you.", AshenColor);
                            break;
                        case "d":
                            SpawnAshenAtGate(s, 10, 50f);
                            Msg("You wedge the inner gate open and take position in the passage. The chokepoint is yours — nothing comes through without coming through you. Two of them decide not to. The third does, because the third is covering the other two's exit. The fight is in a narrow space with stone walls and you chose it. Your party handles it as it should be handled.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── POST-BATTLE: The Survivor Mage Duel (mage, battle won) ─────────
        private static void EB6_SurvivorMageDuel()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Survivor's Right",
                "A mage on the losing side — badly wounded, one arm unusable — calls to you across the field before your men can reach him. He invokes a formal right from the old codes: a duel between fire-carriers for the right to a clean ending. Not a fight for his life — he knows that argument is over. A duel for the manner of it. He is asking you to take it seriously.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Accept the formal duel. The codes that govern fire-carriers still mean something.", null, true,
                        "The codes still mean something. The exchange before the end carries weight."),
                    new InquiryElement("b", "Accept, but offer him a choice at the end — yield and receive a clean parole.", null, true,
                        "An offer at the end of a formal exchange. Whether he takes it is his choice."),
                    new InquiryElement("c", "Decline the duel but treat his wounds and release him.", null, true,
                        "You decline the form. You treat the wound. He will receive this in his own way."),
                    new InquiryElement("d", "Ask what he wants to say before the duel — he invoked the right but he has something to tell you.", null, true,
                        "He invoked the right but he has something to say. You will want to hear it."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgePlayer(1);
                            ChangeRenown(10f);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("The duel is formal and brief. He can barely stand but his working is precise — the body failing, the gift still itself. You end it cleanly. Before it ends he tells you the name of his teacher. You know the name. The lineage it implies is older than you expected him to carry. Your men watched in silence.", FireColor);
                            break;
                        case "b":
                            AgePlayer(1);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("At the end of the formal exchange — him down to one working, you still upright — you offer the parole. He considers it for ten seconds. He takes it. He sets down what he was holding and looks at his hands and then at you, and something in the Ashen training he carried releases. He will recover. Where he goes after this is his choice, which is different from what he expected to have today.", GoodColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You decline the duel and call your surgeon. He does not speak while being treated. When the wounds are dressed and he is given water he says: he invoked the right because he thought you would refuse, and refusal would have told him something. Acceptance would have told him something else. Treatment without the duel told him a third thing he was not expecting. He does not say what the third thing is. He is released at dusk.", GoodColor);
                            break;
                        case "d":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("He has been watching the Ashen's movement pattern for six months and disagrees with it. He is a mage on the wrong side of something that started as politics and metastasized. He gives you the pattern — in detail, specifically — and then accepts the duel. He fights correctly. The exchange is brief and formal and at the end of it you are carrying intelligence about Ashen strategy that he chose to give rather than let go with him. This was the whole point.", AshenColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // SKILL-CHECK EVENTS — SOFT ROLL (probability scales with skill level)
        // ═══════════════════════════════════════════════════════════════════

        // ── ENTER VILLAGE: The Wrong Wound [Medicine] ───────────────────────
        private static void EV8_WrongWound(Settlement s)
        {
            float chance = SkillChance(DefaultSkills.Medicine, 0.25f);
            string hint  = SkillHint(DefaultSkills.Medicine, 0.25f, "Diagnose correctly");
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✚  The Wrong Wound",
                "A farmer is being treated at the inn — the village herb-woman has cleaned and bound a wound in his side. She is confident. She is wrong: the binding has sealed in something, and the smell is the smell of a wound going the wrong way. He is not complaining yet. He will be, badly, by morning.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Intervene — reopen and treat it properly.", null, true, hint),
                    new InquiryElement("b", "Tell the herb-woman what you see and let her decide.", null, true,
                        "Her territory. She will make of it what she can."),
                    new InquiryElement("c", "Give the family coin to fetch a real surgeon from the next town.", null, true,
                        "A surgeon is coming. He may not have the time for that."),
                    new InquiryElement("d", "Say nothing. You could be wrong.", null, true,
                        "You might be wrong."),
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
                                ChangeRenown(5f);
                                Msg("You reopen the wound correctly, clean it properly, and pack it with what you have. The herb-woman watches closely. He has a week of fever ahead of him and then a slow recovery. He was going to have a much shorter future.", GoodColor);
                            }
                            else
                            {
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                Msg("You intervene with confidence and correct intention but the wound is deeper than it looked and your treatment, while better than what was there, is not quite right either. He is no worse than he would have been by morning. He is better than he would have been by the following morning. The herb-woman completes the work after you leave. Between the two of you, he lives.", DimColor);
                            }
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You tell her what you see. She listens with visible difficulty — this is her territory and her diagnosis. She looks again at the wound for a long moment, then starts unwrapping. She does not thank you. She does not need to. The farmer is better for the conversation. So is she, eventually.", GoodColor);
                            break;
                        case "c":
                            ChangeGold(-300);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("A rider goes for the surgeon. He arrives by the following morning. The wound has started going wrong by then but not irreversibly. The surgeon works for two hours and the farmer survives. He will carry a scar and a story about the lord who paid without being asked. The surgeon will carry the memory of almost-too-late.", DimColor);
                            break;
                        case "d":
                            Msg("You say nothing and ride on. In the morning you know you were right. You ride on.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

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
            try { ColourLordRegistry.SetAshen(Hero.MainHero, true); } catch { }
            try { AshenCitySystem.ApplyAshenPersonality(Hero.MainHero); } catch { }
            try { ColourLordRegistry.SetMage(Hero.MainHero, true); } catch { }
            try { AshenCitySystem.OnHeroSetAshen(Hero.MainHero); } catch { }
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
            string title, desc;
            switch (_trinketVariant)
            {
                case 2:
                    title = "◈  The Eye Opens";
                    desc  = "Both sides of the medallion have the same eye in the dream. Both are open. There is nothing behind them — not darkness exactly, but the specific quality of attention that has no object. It is watching you the way a locked door watches a room. You wake with the clear sense that something in your belongings is oriented toward you.";
                    break;
                case 3:
                    title = "◈  A Direction";
                    desc  = "You are in a dark place and the compass is in your hand. Every direction looks identical except one. The disc has settled on a bearing and what is in that direction is not light exactly, but what light might look like if it could form intentions. The pull is as large as you can imagine and no larger. You wake with your hand closed around nothing and the bearing still vivid in your mind.";
                    break;
                default:
                    title = "◈  Something in the Amber";
                    desc  = "You dream of flame suspended in resin — perfectly still, not consuming, not going out. In the dream, you reach toward it. It turns toward you. The warmth in that direction is very old. You wake with your hand extended and the smell of resin in your nose.";
                    break;
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                title, desc,
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Reach toward it.", null, true, ""),
                    new InquiryElement("b", "Hold still. Don't engage.", null, true, ""),
                    new InquiryElement("c", "Get rid of it in the morning.", null, true, ""),
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
            string title, desc, successMsg, ageMsg, deathMsg;
            switch (_trinketVariant)
            {
                case 2:
                    title      = "◈  The Eye Again";
                    desc       = "The dream returns. The iron eye is waiting. It is more open than before, if that has a meaning. The attention behind it has been watching you for seven days straight and has learned something from the observation. You have options now that you did not have at the start.";
                    successMsg = "You meet the eye and don't look away. The attention intensifies until it is the only thing in the dream. Then it gives you something — a current of influence, recognition from quarters you did not cultivate, a weight of gold arriving by paths you didn't arrange. The eye closes. You wake with the feeling that you paid something you haven't noticed missing yet.";
                    ageMsg     = "The eye opens all the way. The attention that has been watching you arrives all at once. The years go first — fifty of them, drawn out through you in a single second. You do not have time to regret the decision. You wake old in the way that is permanent, and the medallion in your pocket is cold iron now, nothing more.";
                    deathMsg   = "The eye opens fully and you see what is behind it. You were not supposed to survive this. The attention was always this size. You had simply not understood how small you were standing in front of it.";
                    break;
                case 3:
                    title      = "◈  The Bearing Again";
                    desc       = "The dream returns. The compass is in your hand and the bearing is the same bearing it always is. What is in that direction has not moved. It has only become clearer that it is aware of you now — aware that you found it, aware that you have been carrying it. Seven days. You have options.";
                    successMsg = "You follow the bearing. In the dream it takes you somewhere real enough that you remember it on waking — a room, a face, an exchange that settled three separate debts in your favour. The influence flows in from directions you didn't solicit. Gold arrives by paths you didn't arrange. The compass lies still in your pocket, facing its usual direction.";
                    ageMsg     = "You reach the end of the bearing. What is there takes what it is owed. Fifty years, precise to the day. You feel each one of them leave. You wake in an older body, the compass still in your hand, the rose still pointing nowhere you can follow now. It is, in every sense, spent.";
                    deathMsg   = "You arrive at the end of the bearing. What is there is not what you imagined. The compass was not guiding you. It was leading you.";
                    break;
                default:
                    title      = "◈  The Amber Again";
                    desc       = "The dream returns. The flame in the resin is brighter than before. It knows you now — or has learned to expect you. The warmth extends outward with more precision. You could let it in further. You could put the shard somewhere it would never be found. You have been carrying it for seven days and counting.";
                    successMsg = "You open your hand in the dream and let the warmth come the rest of the way in. It passes through you like a tide: slow, total, indifferent to your comfort. When you wake, your purse is somehow heavier, three lords who have never spoken well of you have revised their estimate, and the fire you carry burns with a steadier quality. You don't know how to explain any of it. You don't try.";
                    ageMsg     = "The warmth comes all the way in. You let it. For a moment you think it is good. Then the years begin to run. Fifty of them. Not taken — spent, which is different. You wake gasping, and the face looking back at you from still water is the face of someone who came to this late and paid for the privilege. The shard is cold. It won't warm again.";
                    deathMsg   = "The warmth comes all the way in and keeps coming. You understand, in the last moment, that it was never warmth — it was appetite. The fire inside you feeds it until there is nothing left to feed with. You do not wake.";
                    break;
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                title, desc,
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Throw it away. Enough.", null, true, ""),
                    new InquiryElement("b", "Use it.", null, true, ""),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            _trinketPhase   = 0;
                            _trinketVariant = 0;
                            Msg("You find a river and throw it as far as the current will take it. You don't watch where it goes. You ride on without looking back. The dreams stop.", DimColor);
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
                                       || TalentSystem.Has(TalentId.Reap)
                                       || TalentSystem.Has(TalentId.DevourLife));

            string spouseName = spouse.Name?.ToString() ?? "your spouse";
            string childName  = child.Name?.ToString()  ?? "your child";

            string cHint = mage
                ? $"The fire through both of them at once. Not a thing meant to be done like this."
                : "Requires mage ability.";
            string dHint = hasDarkTalent
                ? $"The ritual requires something living. It requires a lot of it."
                : "Requires Ember, Reap, or Devour Life talent.";

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
                                // Grant Reap if not owned, then Harvest (DevourLife), else attribute point
                                if (!TalentSystem.Has(TalentId.Reap))
                                    TalentSystem.GrantFree(TalentId.Reap, Hero.MainHero);
                                else if (!TalentSystem.Has(TalentId.DevourLife))
                                    TalentSystem.GrantFree(TalentId.DevourLife, Hero.MainHero);
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
                    new InquiryElement("c", $"Send quiet word to one of {kB.Name}'s lords as you leave.", null, true,
                        "Not a full warning — enough to unsettle one man, unlikely to stop what is already in motion."),
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
                            _brokenSealCountdown = 3;
                            var lordsB = Hero.AllAliveHeroes
                                .Where(h => h.IsLord && h.IsAlive && !h.IsPrisoner
                                         && h != Hero.MainHero && h.MapFaction == kB)
                                .ToList();
                            if (lordsB.Count > 0)
                            {
                                Hero lord = lordsB[_rng.Next(lordsB.Count)];
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, 2, false);
                                Msg($"(Relation with {lord.Name}: +2)", GoodColor);
                            }
                            Msg($"You find one of {kB.Name}'s lords before you ride out and tell them enough to make them uneasy — not everything, not the letter, but enough. Whether they act on it is their business. It probably will not be enough to stop what is already in motion.", DimColor);
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
    }
}
