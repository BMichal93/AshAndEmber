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
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static class SettlementEncounters
    {
        // ── Tuning ────────────────────────────────────────────────────────────
        public const float EncounterChance       = 0.35f;  // per settlement transition
        public const int   MinDaysBetween        = 3;      // shared cooldown between any encounter
        public const float BattleEncounterChance = 0.35f;  // per field battle
        public const float SiegeEncounterChance  = 0.50f;  // per siege
        public const float RaidEncounterChance   = 0.55f;  // per raid

        // ── State ─────────────────────────────────────────────────────────────
        private static int    _cooldown              = 0;
        private static string _lastSettlementId      = null; // last settlement entered (for leave detection)
        private static bool   _lastBattleWon         = false;
        private static bool   _lastBattleAsAttacker  = false;
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
            _cooldown         = 0;
            _lastSettlementId = null;
        }

        public static void Save(IDataStore store)
        {
            store.SyncData("SE_Cooldown", ref _cooldown);
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

        /// Called from MagicCampaignBehavior.OnDailyTick — only decrements cooldown.
        public static void DailyTick()
        {
            if (_cooldown > 0) _cooldown--;
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
                pool.Add(E_FamilyQuarrel);
                pool.Add(E_HarvestFestival);
                pool.Add(E_AshenAftermath);
                pool.Add(E_BanditWarning);
                pool.Add(E_SpilledCart);
                pool.Add(EV2_TravelingMonk);
                pool.Add(EV2_DriedWell);
                pool.Add(EV2_WiseWomanWarning);
                if (ren >= 600f) pool.Add(EV2_ChildNamedAfterYou);
                pool.Add(EV3_OldKnight);
                pool.Add(EV3_WeddingNews);
                pool.Add(EV3_VillageCoercion);
                pool.Add(EV4_EmptyVillage);
                pool.Add(EV4_VillageTrial);
                pool.Add(EV4_FleeingFamily);
                pool.Add(EV5_WolvesCircling);
                pool.Add(EV6_DebtCollector);
                pool.Add(EV6_StrangersHorse);
                pool.Add(EV6_SickHealer);
                pool.Add(EV6_MillDispute);
                pool.Add(EV7_WatchedVillage);
                pool.Add(EV8_WrongWound);
                pool.Add(EV8_ColdTrail);
                if (mage)
                {
                    pool.Add(E_OldFlameSeer);
                    pool.Add(E_HealersTrade);
                    pool.Add(E_FireAndStraw);
                    pool.Add(E_ShrineGoesOut);
                    pool.Add(E_WarmthMerchant);
                    pool.Add(EV4_GiftedChild);
                    pool.Add(EV5_FrozenFord);
                    pool.Add(EV6_BurnedShrine);
                    pool.Add(EV6_ChildsMap);
                    pool.Add(EV7_SelfTaughtMage);
                    pool.Add(EV8_TheLie);
                    if (ren >= 600f) pool.Add(EV7_OldMastersStudent);
                }
                if (ashen) pool.Add(EV2_DogWontStop);
            }
            if (town)
            {
                pool.Add(E_SoldierDying);
                pool.Add(E_ChildsBead);
                pool.Add(E_OldEnemy);
                pool.Add(EC2_CityQuarantine);
                pool.Add(EC2_AshenGraffiti);
                if (ren >= 300f) pool.Add(EC2_SellswordChallenge);
                if (ren >= 400f) pool.Add(EC2_NoblewomansInvitation);
                if (ren >= 400f) pool.Add(EC5_PortraitPainter);
                if (ren >= 700f) pool.Add(E_TradeCouncil);
                pool.Add(EC3_GuardsExtorting);
                pool.Add(EC3_Philosopher);
                pool.Add(EC4_PrisonerBlock);
                pool.Add(EC4_WantedPoster);
                pool.Add(EC6_Tribunal);
                pool.Add(EC6_Petition);
                pool.Add(EC6_Gladiator);
                pool.Add(EC6_SmuggledLetters);
                pool.Add(EC7_GreyCloaks);
                pool.Add(EC8_ReluctantOfficial);
                pool.Add(EC8_MerchantLedger);
                pool.Add(EC8_Followed);
                if (mage)
                {
                    pool.Add(E_CuriousScholar);
                    pool.Add(E_AnotherFire);
                    pool.Add(E_AshTouchedMarket);
                    pool.Add(EC2_StreetPreacher);
                    pool.Add(EC3_SickNoble);
                    pool.Add(EC4_FakeMage);
                    pool.Add(EC5_PhysiciansEye);
                    pool.Add(EC6_AlchemistFire);
                    if (ren >= 400f) pool.Add(EC7_TheDuelist);
                    if (ren >= 1000f) pool.Add(E_CrowdWantsSign);
                }
                if (ashen) pool.Add(EC7_AshenSurveillance);
                if (ashen)
                {
                    pool.Add(E_GreyEyes);
                    pool.Add(E_FellowCold);
                }
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
                pool.Add(E_BeggarCrossroads);
                pool.Add(E_LameHorse);
                pool.Add(E_CoinGame);
                pool.Add(E_TorchesAtDusk);
                pool.Add(E_EagerRecruit);
                pool.Add(E_FestivalFarewell);
                pool.Add(LV2_PilgrimsRequest);
                pool.Add(LV2_LameHorseYours);
                pool.Add(LV2_VillageGirlNote);
                pool.Add(LV3_TwoSons);
                pool.Add(LV3_HiddenCriminal);
                pool.Add(LV3_InnFire);
                pool.Add(LV4_StoryTeller);
                pool.Add(LV4_DyingTraveler);
                pool.Add(LV4_EscapedPrisoner);
                pool.Add(LV4_DeserterSoldier);
                pool.Add(LV5_TroopFever);
                pool.Add(LV5_WrongSong);
                pool.Add(LV6_OathOnRoad);
                pool.Add(LV6_HiddenGrave);
                pool.Add(LV6_BlindSoldier);
                pool.Add(LV7_RoadWatchesBack);
                pool.Add(LV8_PoisonedWell);
                pool.Add(LV8_BattleSetup);
                if (mage)
                {
                    pool.Add(E_MothersPlea);
                    pool.Add(E_WidowsPyre);
                    pool.Add(E_SignalFire);
                    pool.Add(E_EldersSending);
                    pool.Add(LV6_FireTender);
                }
            }
            if (town)
            {
                pool.Add(E_LightenedPurse);
                pool.Add(E_DisplacedNoble);
                pool.Add(E_DetainedSoldier);
                pool.Add(E_AshenInformant);
                pool.Add(E_InsultAtGate);
                pool.Add(LC2_ChildPickpocket);
                pool.Add(LC2_MerchantAccusation);
                pool.Add(LC2_SealedLetter);
                if (ren >= 300f) pool.Add(E_BardsRequest);
                if (ren >= 400f) pool.Add(LC2_PartingGift);
                if (ren >= 500f) pool.Add(E_GuildsOffer);
                pool.Add(LC3_DishonoredSoldier);
                pool.Add(LC3_SpyWarning);
                pool.Add(LC3_MercenaryOffer);
                pool.Add(LC4_RunawayServant);
                pool.Add(LC4_OldDebt);
                pool.Add(LC5_OldAlly);
                pool.Add(LC6_EmptyWagon);
                pool.Add(LC6_Confiscation);
                pool.Add(LC6_TheKneel);
                if (ren >= 400f) pool.Add(LC6_NobleHostage);
                if (ren >= 300f) pool.Add(LC6_InformantsNote);
                pool.Add(LC7_DeadGuard);
                pool.Add(LC8_GateStandoff);
                pool.Add(LC8_ForgeryAtGate);
                if (mage)
                {
                    pool.Add(E_VeteransQuestion);
                    pool.Add(E_TheCondemned);
                    pool.Add(LC2_WrongnessInAir);
                    if (ren >= 500f) pool.Add(E_PetitionersGate);
                }
                if (ashen) pool.Add(LC4_RecognizedByAshen);
                if (ashen && mage) pool.Add(LC7_AshenChallenge);
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

            pool.Add(EB_DyingEnemy);
            pool.Add(EB_ChildAmongDead);
            pool.Add(EB_LootedSoldier);
            pool.Add(EB_LoneSurrender);
            pool.Add(EB_BattlefieldPriest);
            if (_lastBattleWon) pool.Add(EB_CampAtDusk);
            if (_lastBattleAsAttacker) pool.Add(EB_OfficerDeal);
            if (ren >= 400f && _lastBattleWon) pool.Add(EB_HeraldAfterVictory);
            pool.Add(EB2_StandardBearer);
            pool.Add(EB2_EnemySupplies);
            pool.Add(EB2_HeroInParty);
            pool.Add(EB3_TriageDecision);
            pool.Add(EB3_PrisonerNobleClaim);
            pool.Add(EB3_TheyCarriedHim);
            pool.Add(EB4_WarHorse);
            pool.Add(EB5_MercenaryTerms);
            pool.Add(EB5_EnemySurgeon);
            pool.Add(EB5_YoungOfficer);
            if (_lastBattleWon) pool.Add(EB5_OwnStandard);
            if (mage && _lastBattleWon) pool.Add(EB6_SurvivorMageDuel);
            pool.Add(EB8_FieldTriage);
            pool.Add(EB8_BattleDebrief);
            if (mage) pool.Add(EB_EnemyMageJournal);
            if (mage) pool.Add(EB2_FireReveals);

            FireBattle(pool);
        }

        private static void TryFireSiege()
        {
            bool mage = MageKnowledge.IsMage;
            bool attWon = _lastBattleAsAttacker && _lastBattleWon;
            var pool = new List<Action>();

            if (attWon)
            {
                pool.Add(ES_FallenLordsFamily);
                pool.Add(ES_MakeExample);
                pool.Add(ES_SurroundingVillages);
                pool.Add(ES_TreasuryFound);
                pool.Add(ES_ShrineKeeper);
                pool.Add(ES_FirstNight);
                pool.Add(ES2_HospitalWard);
                pool.Add(ES2_SpyInCamp);
                pool.Add(ES3_PoisonedWell);
                pool.Add(ES3_LongPrisoner);
                pool.Add(ES5_Archives);
                pool.Add(ES5_Collaborator);
                pool.Add(ES5_HiddenGold);
                pool.Add(ES5_Torturer);
                pool.Add(ES8_SiegeStores);
                if (mage) pool.Add(ES_OldScorchmarks);
                if (mage) pool.Add(ES4_AshenCrystal);
                if (mage) pool.Add(ES6_KeepMage);
            }
            else
            {
                // Defender won or draw — still interesting
                pool.Add(EB_CampAtDusk);
                pool.Add(EB_BattlefieldPriest);
            }

            FireBattle(pool);
        }

        private static void TryFireRaid()
        {
            bool mage = MageKnowledge.IsMage;
            var pool  = new List<Action>();

            pool.Add(ER_HeadmanConfronts);
            pool.Add(ER_ChildFollows);
            pool.Add(ER_SpoilsDivision);
            pool.Add(ER_WomanInDoorway);
            pool.Add(ER_VeteranQuestions);
            pool.Add(ER2_ElderNegotiates);
            pool.Add(ER2_AshenEvidence);
            pool.Add(ER3_CellarSurvivors);
            pool.Add(ER4_AshenChild);
            pool.Add(ER5_Apiary);
            pool.Add(ER5_NameList);
            if (mage) pool.Add(ER_ShrineBurning);

            FireBattle(pool);
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static void Msg(string text, Color c)
            => InformationManager.DisplayMessage(new InformationMessage(text, c));

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
                        "Costs 1 day of life. Gain Merciful."),
                    new InquiryElement("b", "Refuse. The road pulls at you.", null, true,
                        "Nothing happens."),
                    new InquiryElement("c", "Press coins into her hands — see a healer.", null, true,
                        "Lose 200 gold. Gain Generous."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
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
                        "Costs 1 day. Gain Merciful. Renown +5."),
                    new InquiryElement("b", "Decline gently. This is not what the fire is for.", null, true,
                        "Nothing happens."),
                    new InquiryElement("c", "Agree — but name a price.", null, true,
                        "Gain 200 gold. Lose Honor."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
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

        // 3. Signal Fire
        private static void E_SignalFire(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  Signal Fire",
                "On the hill above the road, a fire burns where no fire should be — wrong colour, wrong rhythm. It could be a signal. It could be an Ashen working. It could be nothing. But you feel it before you see it, which means something.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Ride toward it. Whatever it is, you should know.", null, true,
                        "Possible Ashen intel or delay. 50/50."),
                    new InquiryElement("b", "Note the position and send word to the nearest lord.", null, true,
                        "Relation +5 with a lord."),
                    new InquiryElement("c", "Ignore it. The fire speaks to you, but not always for a reason.", null, true,
                        "Nothing happens."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (_rng.Next(2) == 0)
                            {
                                Msg("The fire was a crude beacon — old Ashen sigil scorched into the earth. Someone lit it recently. The Ashen are closer than the lords believe.", FireColor);
                                ChangeRenown(5f);
                            }
                            else
                            {
                                Msg("Shepherd children, burning rubbish. They scatter when they see you coming. You ride back having learned nothing useful.", DimColor);
                            }
                            break;
                        case "b":
                            ChangeRelWithRandomLord(5);
                            Msg("A rider carries the report. Whether anyone acts on it is another matter.", DimColor);
                            break;
                        case "c":
                            Msg("The fire gutters as you pass below it. Whatever it was, it burns itself out.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 4. The Elder's Sending
        private static void E_EldersSending(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚜  The Elder's Sending",
                "The village elder — older than anyone else here, hands like bark — stops you at the gate. She places both palms on your horse's neck and mutters something. Then she looks up. \"The fire knows its own,\" she says. \"Ride safely.\"",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Accept her blessing with grace.", null, true,
                        "Morale boost. Honor +1."),
                    new InquiryElement("b", "Ask what she means — how does she know?", null, true,
                        "Gain Calculating. Old memory of what your kind once was."),
                    new InquiryElement("c", "Nod and ride. Old women and old words.", null, true,
                        "Nothing happens."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            MobileParty.MainParty.RecentEventsMorale += 5f;
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("Your party rides out with a lightness that has no single cause. The elder watches from the gate until the road bends.", GoodColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("\"My grandmother's grandmother remembered a man with the same hands as yours,\" she says. \"Warm in winter. He was not cruel.\" She says nothing more.", FireColor);
                            break;
                        case "c":
                            Msg("You ride on. Behind you, she watches the road long after you have gone.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═════════════════════════════════════════════════════════════════════
        // LEAVE VILLAGE — GENERAL
        // ═════════════════════════════════════════════════════════════════════

        // 5. Beggar at the Crossroads
        private static void E_BeggarCrossroads(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  Beggar at the Crossroads",
                "An old man sits at the junction where the roads fork, wrapped in a blanket despite the season. His bowl is empty. He does not beg loudly — he just holds the bowl out, watching you pass.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Drop a coin.", null, true,
                        "Lose 100 gold. Gain Merciful."),
                    new InquiryElement("b", "Give enough to last him a week.", null, true,
                        "Lose 500 gold. Gain Merciful and Generous."),
                    new InquiryElement("c", "Ride past.", null, true,
                        "Nothing happens."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(-100);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("A coin drops into the bowl. He nods once without raising his eyes.", GoodColor);
                            break;
                        case "b":
                            ChangeGold(-500);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ShiftTrait(DefaultTraits.Generosity, 1);
                            Msg("Enough silver to keep him fed for days. He stares at it for a long moment, then at you. \"God keep you,\" he says.", GoodColor);
                            break;
                        case "c":
                            Msg("You ride past. He lowers the bowl.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 6. The Lame Horse
        private static void E_LameHorse(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Lame Horse",
                "A cart horse has collapsed in the middle of the road, blocking the way out of the village. The farmer is red-faced, shouting at the animal, and getting nowhere. A queue of carts is forming behind him.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Dismount and help lever it back to its feet.", null, true,
                        "Gain Merciful. Small goodwill."),
                    new InquiryElement("b", "Buy the horse from him to spare him the loss.", null, true,
                        "Lose 300 gold. Gain Merciful."),
                    new InquiryElement("c", "Order your party to clear the road and ride around.", null, true,
                        "Nothing happens."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("With several hands the horse finds its feet again. The farmer is wordlessly grateful. The queue disperses.", GoodColor);
                            break;
                        case "b":
                            ChangeGold(-300);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("The farmer cannot believe his luck. The old horse is led to the side of the road. He will eat tonight.", GoldColor);
                            break;
                        case "c":
                            Msg("Your party pushes through. The farmer glares. Nobody says anything.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 7. The Coin Game
        private static void E_CoinGame(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚖  The Coin Game",
                "A village child runs after your horse, shouting that you dropped a coin. You didn't. The child holds up a bent copper piece with an expression of perfect innocence.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Go along with it. Give them the coin.", null, true,
                        "Lose 100 gold. Gain Merciful."),
                    new InquiryElement("b", "Tell them quietly that you know the game, and move on.", null, true,
                        "Nothing happens."),
                    new InquiryElement("c", "Call their parents about it.", null, true,
                        "Gain Honor. Small civic scene."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(-100);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You take the bent copper and hand back a real coin. The child runs off before you can change your mind.", GoodColor);
                            break;
                        case "b":
                            Msg("\"That is not mine,\" you say, \"and you know it.\" The child considers this, then vanishes into a doorway.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("A mother appears from nowhere, takes the child by the ear, and disappears again. The copper coin remains in the road.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 8. Torches at Dusk
        private static void E_TorchesAtDusk(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  Torches at Dusk",
                "A group of men carrying torches and farming tools is moving toward a family's home at the edge of the village. The mood is ugly. You don't know the cause, but you know how this ends.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Ride in front of them. Your authority ends this now.", null, true,
                        "Gain Merciful. Renown +5. Possible brief confrontation."),
                    new InquiryElement("b", "Report the situation to the headman before riding on.", null, true,
                        "Gain Merciful. Nothing immediate."),
                    new InquiryElement("c", "This village is not your concern. Ride on.", null, true,
                        "Lose Honor."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ChangeRenown(5f);
                            Msg("Your party fills the road. The men stop. Nobody in a mob wants to be the first to challenge a lord. They disperse slowly, torches still lit, going nowhere.", GoodColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("The headman runs. Whether he reaches them in time is not your problem to witness.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("Behind you, the torches keep moving. You do not look back.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 9. The Eager Recruit
        private static void E_EagerRecruit(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  The Eager Recruit",
                "A young man — seventeen, perhaps eighteen — is trotting alongside your horse with a cloth bundle on his back. He says he is strong, quick, that he can ride, that he has no family to miss him. His boots are falling apart.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Take him on. The party could use the hands.", null, true,
                        "Gain Merciful. Morale +3."),
                    new InquiryElement("b", "Decline politely. The road is not what he imagines.", null, true,
                        "Nothing happens."),
                    new InquiryElement("c", "Tell him to go home and grow up.", null, true,
                        "Lose Honor."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            MobileParty.MainParty.RecentEventsMorale += 3f;
                            Msg("He falls in at the back of the column, trying to look like he has done this before. He has not. Your veterans watch him with something between amusement and memory.", GoodColor);
                            break;
                        case "b":
                            Msg("\"The roads kill boys like you,\" you tell him honestly. He stops running after you, but he does not go back into the village either.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("He stops. He does not argue. That is worse, somehow.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 10. The Festival Farewell
        private static void E_FestivalFarewell(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✿  The Festival Farewell",
                "The village has been celebrating a saint's day. As you ride out, a group of villagers press food and a small clay jug of cider on your party — festival excess, freely given. The headman raises his cup at you from a doorway.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Accept with thanks and share it among the party.", null, true,
                        "Morale +8. Small renown."),
                    new InquiryElement("b", "Accept but donate a gift back in kind.", null, true,
                        "Lose 200 gold. Morale +8. Renown +5."),
                    new InquiryElement("c", "Decline. You prefer to travel light.", null, true,
                        "Nothing happens."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            MobileParty.MainParty.RecentEventsMorale += 8f;
                            ChangeRenown(3f);
                            Msg("The party eats well for the first hour on the road. Songs get sung. The cider is better than expected.", GoodColor);
                            break;
                        case "b":
                            ChangeGold(-200);
                            MobileParty.MainParty.RecentEventsMorale += 8f;
                            ChangeRenown(5f);
                            Msg("You leave a purse with the headman for the festival fund. Word of it spreads the way good news travels — slowly, but it travels.", GoldColor);
                            break;
                        case "c":
                            Msg("The villagers pull back their gifts politely. The headman lowers his cup.", DimColor);
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
                        "Costs 1 day. Renown +10 as the village sees you honour him."),
                    new InquiryElement("b", "Ask what he sees in you.", null, true,
                        "Gain Calculating. Lore insight from an old seer."),
                    new InquiryElement("c", "Keep walking. Old men with milky eyes say many things.", null, true,
                        "Nothing happens."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
                            ChangeRenown(10f);
                            Msg("You sit with him for an hour. He tells you things about fire that you already knew but could not have named. The village watches from doorways. By evening they speak of you differently.", FireColor);
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

        // 12. The Healer's Trade
        private static void E_HealersTrade(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✚  The Healer's Trade",
                "The village healer — a woman in her forties with ink-stained fingers — corners you near the well. She has been watching you since you rode in. \"You carry warmth that moves,\" she says quietly. \"I have been trying to understand that for thirty years.\"",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Share what you know about the fire's nature.", null, true,
                        "Costs 2 days. Gain Honor and Merciful."),
                    new InquiryElement("b", "Let her demonstrate her herb-work while you watch.", null, true,
                        "Lose 300 gold. Gain flavor knowledge."),
                    new InquiryElement("c", "Smile and say you know nothing of what she means.", null, true,
                        "Nothing happens."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgingSystem.AgeHero(Hero.MainHero, 2);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You spend the afternoon explaining what you have learned. She fills three pages of notes and asks questions you cannot answer. Something about that is satisfying.", FireColor);
                            break;
                        case "b":
                            ChangeGold(-300);
                            Msg("She teaches you how she coaxes heat from poultices and why fever-breaks work. There is a different kind of fire in what she does. You leave thinking about the difference.", DimColor);
                            break;
                        case "c":
                            Msg("She watches you walk away. She knows you lied. You can tell by the way she doesn't follow.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 13. Fire and Straw
        private static void E_FireAndStraw(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  Fire and Straw",
                "Two children are crouched behind the grain barn, feeding sparks from a stolen tinderbox into a pile of loose straw. The wind is wrong. The barn is dry.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Snuff the fire with a controlled working before it catches.", null, true,
                        "Costs 1 day. Gain Merciful. Renown +5."),
                    new InquiryElement("b", "Shout a warning and run toward them.", null, true,
                        "Gain Merciful. Nothing else."),
                    new InquiryElement("c", "Walk past. Not your barn.", null, true,
                        "Lose Honor."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ChangeRenown(5f);
                            Msg("The straw dies with a soft sound, smoke curling upward. The children stare at your hands. You put a finger to your lips. They run.", FireColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("The children scatter. The straw scatters with them. The barn survives.", GoodColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("You hear the shout from behind you. You do not turn around.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 14. The Shrine Goes Out
        private static void E_ShrineGoesOut(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Shrine Goes Out",
                "The village's roadside shrine — an iron bowl on a post, supposed to burn day and night — has gone cold. The village elder sees this as an ill omen. Three people have already gathered around it, uncertain. They see you arrive.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Relight it. You can do this without effort.", null, true,
                        "Costs 1 day. Renown +5. Gain Merciful."),
                    new InquiryElement("b", "Tell them the omen means nothing and suggest a flint and tinder.", null, true,
                        "Gain Calculating. The elder looks unconvinced."),
                    new InquiryElement("c", "Keep walking. Shrines are not your business.", null, true,
                        "Nothing happens."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
                            ChangeRenown(5f);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("The bowl catches on your breath. The flame is gold, not orange. The elder makes a sound you have not heard before. The villagers will talk about this for years.", FireColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("The elder does not look comforted by logic. But one of the young men goes looking for a flint.", DimColor);
                            break;
                        case "c":
                            Msg("You ride in past the cold shrine. Nobody stops you.", DimColor);
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
                        "Renown +5. Gain Honor."),
                    new InquiryElement("b", "Make him an offer: you will make a real one for him to sell, for a cut.", null, true,
                        "Gain 300 gold. Costs 1 day."),
                    new InquiryElement("c", "Buy one as a joke. You can afford the amusement.", null, true,
                        "Lose 50 gold."),
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
                            AgingSystem.AgeHero(Hero.MainHero, 1);
                            Msg("You spend a quiet hour doing something small and strange to a dozen clay pieces. They will hold warmth longer than they should. The merchant looks at them wide-eyed. The arrangement is profitable.", GoldColor);
                            break;
                        case "c":
                            ChangeGold(-50);
                            Msg("You turn it over in your palm. Ordinary clay. You pocket it anyway — it will make a fine illustration someday.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═════════════════════════════════════════════════════════════════════
        // ENTER VILLAGE — GENERAL
        // ═════════════════════════════════════════════════════════════════════

        // 16. A Family's Quarrel
        private static void E_FamilyQuarrel(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  A Family's Quarrel",
                "Two families are shouting at each other in the village square over a boundary stone that has apparently moved. Both claim the other moved it. The headman is not available. They see your party and go quiet, looking at you.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Rule on it with authority.", null, true,
                        "Renown +5. One side pleased, one side resentful."),
                    new InquiryElement("b", "Suggest they take it to the headman when he returns.", null, true,
                        "Nothing happens."),
                    new InquiryElement("c", "Take a coin from the richer family to rule in their favor.", null, true,
                        "Gain 300 gold. Lose Honor."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeRenown(5f);
                            Msg("You look at the field lines, the stone, the growth patterns on both sides, and make a decision. One family grumbles. The other thanks you loudly. Either way the shouting stops.", GoodColor);
                            break;
                        case "b":
                            Msg("They stare at you as if you have failed them. You have not. They will shout again tomorrow.", DimColor);
                            break;
                        case "c":
                            ChangeGold(300);
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("You pocket the coins and announce your judgement. The poorer family leaves in silence. The coin feels ordinary in your hand.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 17. The Harvest Festival
        private static void E_HarvestFestival(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✿  The Harvest Festival",
                "The village is in the middle of a harvest feast. Tables are set in the square, children are underfoot, someone is playing a three-string instrument badly. The headman sees you ride in and waves you toward a seat.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Join them. You eat and let the men drink to your name.", null, true,
                        "Morale +8. Renown +5."),
                    new InquiryElement("b", "Donate to the feast and ride on — you cannot stay.", null, true,
                        "Lose 300 gold. Renown +10. Gain Generous."),
                    new InquiryElement("c", "Pass through respectfully.", null, true,
                        "Nothing happens."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            MobileParty.MainParty.RecentEventsMorale += 8f;
                            ChangeRenown(5f);
                            Msg("The party eats. Your men loosen up in a way that only happens when they feel safe. The music improves by the second cup.", GoodColor);
                            break;
                        case "b":
                            ChangeGold(-300);
                            ChangeRenown(10f);
                            ShiftTrait(DefaultTraits.Generosity, 1);
                            Msg("You press a purse on the headman and keep riding. The feast will get better for it. You hear the cheer from the road.", GoldColor);
                            break;
                        case "c":
                            Msg("You thread through the tables carefully. They make room without complaint.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 18. Ashen Aftermath
        private static void E_AshenAftermath(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  Ashen Aftermath",
                "The village has been raided within the last day — not by bandits. The ash-grey marks on charred wood, the particular way the animals have been left, the silence: these are Ashen Spawn signs. Some people are wounded. The headman is counting the dead.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Help with the wounded and leave supplies.", null, true,
                        "Lose 300 gold. Gain Merciful. Renown +5."),
                    new InquiryElement("b", "Send a rider to report to the nearest lord.", null, true,
                        "Relation +5 with nearest lord."),
                    new InquiryElement("c", "Look for anything salvageable in the confusion.", null, true,
                        "Gain 200 gold. Lose Honor. Crime +5."),
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
                            Msg("Your men set bones and distribute grain from the wagons. The headman grips your arm without speaking. The village will survive.", GoodColor);
                            break;
                        case "b":
                            ChangeRelWithRandomLord(5);
                            Msg("A rider leaves at speed. Whether the lord sends troops before the Spawn return is uncertain.", DimColor);
                            break;
                        case "c":
                            ChangeGold(200);
                            ShiftTrait(DefaultTraits.Honor, -1);
                            ChangeCrime(5f);
                            Msg("You pick through what the Spawn left behind. The headman watches you from across the square and says nothing. You do not meet his eyes.", BadColor);
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
                        "Lose 100 gold. Gain Merciful."),
                    new InquiryElement("b", "Ask for more details — position, numbers, armed?", null, true,
                        "Gain Calculating. Useful tactical detail gathered."),
                    new InquiryElement("c", "Nod and ride past.", null, true,
                        "Nothing happens."),
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

        // 20. The Spilled Cart
        private static void E_SpilledCart(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✿  The Spilled Cart",
                "A merchant's cart has gone over on a muddy rut outside the village gate, scattering grain sacks across the road. The merchant is arguing with his driver. Neither of them is picking anything up.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Set your men to help reload it.", null, true,
                        "Gain Merciful. Morale +3."),
                    new InquiryElement("b", "Buy some of the scattered grain at a fair price.", null, true,
                        "Lose 100 gold. Gain useful flavor."),
                    new InquiryElement("c", "Ride around them.", null, true,
                        "Nothing happens."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            MobileParty.MainParty.RecentEventsMorale += 3f;
                            Msg("Your men stop arguing about the mud and start working. The merchant shuts up and helps. By the time the cart is righted, the argument is forgotten.", GoodColor);
                            break;
                        case "b":
                            ChangeGold(-100);
                            Msg("The merchant is relieved to sell anything he doesn't have to reload. The grain is good quality. Both of you leave satisfied.", GoldColor);
                            break;
                        case "c":
                            Msg("You find a way through. The argument continues behind you.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═════════════════════════════════════════════════════════════════════
        // LEAVE CITY/CASTLE — MAGE
        // ═════════════════════════════════════════════════════════════════════

        // 21. The Veteran's Question
        private static void E_VeteransQuestion(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  The Veteran's Question",
                "A scarred veteran — missing two fingers, grey at the temples — falls in beside your horse at the city gate. He has been watching you for three days in the tavern. \"You don't age like other lords,\" he says. \"My commander wants to know how.\"",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Deflect. Every man has his own roads.", null, true,
                        "Nothing mechanical. Good flavor."),
                    new InquiryElement("b", "Speak plainly: there is a gift, and it has a price.", null, true,
                        "Gain Honor. Relation +10 with the lord he serves."),
                    new InquiryElement("c", "Offer to speak to the commander directly — for a fee.", null, true,
                        "Gain 300 gold. Lose Honor."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            Msg("\"Every man finds his own roads,\" you say. He nods as if this is an answer. It is not. But he doesn't press.", DimColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ChangeRelWithRandomLord(10);
                            Msg("You tell him what it costs. He is quiet for a long moment. \"My commander should know that,\" he says. \"He has been asking the wrong question.\"", FireColor);
                            break;
                        case "c":
                            ChangeGold(300);
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("The coin changes hands and a meeting is arranged. The commander listens, pale-faced, then excuses himself. You leave knowing you have sold something intangible.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 22. The Condemned
        private static void E_TheCondemned(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚖  The Condemned",
                "A group of prisoners is being marched to the city square for public execution. Among them, one face turns toward you. You recognize the marks — the faint smell of old smoke, the way the eyes track fire. A Fire Worshipper. They hold your gaze for a moment before looking away.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Speak to the guards in their defense.", null, true,
                        "Gain Honor. Relation -10 with city lord. Crime +5."),
                    new InquiryElement("b", "Look away and ride on.", null, true,
                        "Nothing happens."),
                    new InquiryElement("c", "Slip them a tool they might use.", null, true,
                        "Gain Honor. Crime +20."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ChangeRelWithOwner(s, -10);
                            ChangeCrime(5f);
                            Msg("The guards stop. You argue the case. The lord's man listens with the patient look of someone who has already decided. The execution is delayed, not stopped. But delayed is something.", GoodColor);
                            break;
                        case "b":
                            Msg("You ride past. Their eyes follow you. You do not look back. This is the choice that asks nothing of you and costs the most.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ChangeCrime(20f);
                            Msg("A small knife pressed palm-to-palm in a crowd. Whether they get free or not, they have a chance they did not have this morning.", DarkColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 23. Petitioners' Gate
        private static void E_PetitionersGate(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚜  Petitioners' Gate",
                "Your reputation precedes you. A queue of people — farmers, merchants, a woman with a written complaint, a man with a battered ledger — waits at the city gate, hoping to speak to you before you ride out.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Hear them all out. Every voice.", null, true,
                        "Costs 1 day. Renown +15. Gain Merciful."),
                    new InquiryElement("b", "Pick one worthy case and give it your attention.", null, true,
                        "Renown +7. Relation +10 with one lord."),
                    new InquiryElement("c", "Wave them away. You have roads to ride.", null, true,
                        "Renown -5."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
                            ChangeRenown(15f);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("The sun moves while you listen. Not all of it is solvable. Some of it is. You leave knowing more about what is wrong in this city than the lord who governs it.", GoodColor);
                            break;
                        case "b":
                            ChangeRenown(7f);
                            ChangeRelWithRandomLord(10);
                            Msg("You pick the case that smells of injustice rather than inconvenience. The ruling takes twenty minutes. The queue disperses, some disappointed, one person not.", GoodColor);
                            break;
                        case "c":
                            ChangeRenown(-5f);
                            Msg("You ride through the queue. They part. Some of them have been waiting since dawn.", BadColor);
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
                        "Recover 200 gold. Takes effort."),
                    new InquiryElement("b", "Accept the loss. Cities are cities.", null, true,
                        "Lose 200 gold."),
                    new InquiryElement("c", "Have your guards make an example of likely suspects.", null, true,
                        "50% chance recover gold. Lose Honor. Crime +10."),
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

        // 25. The Displaced Noble
        private static void E_DisplacedNoble(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚜  The Displaced Noble",
                "A woman in tattered clothing that was once expensive waits near the city gate. She says she is a noblewoman from a clan displaced by the Ashen advance. Her name is one you have not heard. She asks for nothing directly — only looks at you.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Give her enough to find her feet.", null, true,
                        "Lose 500 gold. Gain Merciful. She may or may not be real."),
                    new InquiryElement("b", "Offer her work in your party's household.", null, true,
                        "Gain Merciful. Potential useful contact."),
                    new InquiryElement("c", "Ride past.", null, true,
                        "Nothing happens."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(-500);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            if (_rng.Next(2) == 0)
                                Msg("She receives the coins with practiced dignity. Real or not, there is something in her bearing that makes you think the story was true.", GoodColor);
                            else
                                Msg("She takes the coins and is gone before you have ridden half a street. You cannot know what she really was. You find you don't mind either way.", DimColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("She accepts without hesitation. Whether her claim is true or not, she is useful and grateful — both worth more than a name.", GoodColor);
                            break;
                        case "c":
                            Msg("She watches you pass. Her face does not change.", DimColor);
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
                        "Renown +15."),
                    new InquiryElement("b", "Decline modestly. Your story is not finished yet.", null, true,
                        "Renown +5. Gain Honor."),
                    new InquiryElement("c", "Embellish freely. Songs should be worth singing.", null, true,
                        "Renown +20. Lose Honor."),
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

        // 27. A Detained Soldier
        private static void E_DetainedSoldier(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  A Detained Soldier",
                "One of your men has been stopped at the gate by a city guard claiming an outstanding debt — a tavern bill from three years ago with a number that has somehow grown to 400 gold. Your man insists it was settled. The guard insists otherwise.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Pay the claimed amount and move on.", null, true,
                        "Lose 400 gold."),
                    new InquiryElement("b", "Argue it. Your man is not a liar.", null, true,
                        "50/50: free, or spend a day and pay half."),
                    new InquiryElement("c", "Bribe the guard to forget it.", null, true,
                        "Lose 200 gold. Crime +5."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(-400);
                            Msg("You pay. It is extortion and you both know it. Your man apologises on the road, which is unnecessary but appreciated.", DimColor);
                            break;
                        case "b":
                            if (_rng.Next(2) == 0)
                            {
                                Msg("The guard folds under examination. There is no record. The debt evaporates. Your man walks free and the guard doesn't make eye contact.", GoodColor);
                            }
                            else
                            {
                                ChangeGold(-200);
                                Msg("The ledger produced is dubious but official-looking. You pay half to end the argument. Your man swears he will never drink in this city again.", DimColor);
                            }
                            break;
                        case "c":
                            ChangeGold(-200);
                            ChangeCrime(5f);
                            Msg("The bribe changes hands. The guard waves your man through without looking at either of you. The city's corruption runs in the same directions everywhere.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 28. The Guild's Offer
        private static void E_GuildsOffer(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚖  The Guild's Offer",
                "A well-dressed guild representative has been waiting at the city gate since early morning. He represents a consortium of merchants who have been watching your campaigns. They will back you — significantly — in exchange for trade route protection through your territories.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Accept the arrangement.", null, true,
                        "Gain 1000 gold. Minor Honor cost — you owe them something."),
                    new InquiryElement("b", "Decline. You don't want to owe merchants.", null, true,
                        "Nothing happens."),
                    new InquiryElement("c", "Counter-demand — the terms are not good enough.", null, true,
                        "50/50: Gain 1500 gold, or the deal falls through."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(1000);
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("Coin and obligation exchange hands. The guild gets a letter of protection. You get a campaign fund. Neither side is fully satisfied, which usually means a fair deal.", GoldColor);
                            break;
                        case "b":
                            Msg("\"The offer stands,\" he says, folding the contract away. He says it as though he expected nothing else from someone in your position.", DimColor);
                            break;
                        case "c":
                            if (_rng.Next(2) == 0)
                            {
                                ChangeGold(1500);
                                Msg("He pauses, consults a second sheet, and doubles the figure. Apparently you were worth more to them than the first offer implied.", GoldColor);
                            }
                            else
                            {
                                Msg("He folds the contract and wishes you a pleasant road. The guild will find someone else.", DimColor);
                            }
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 29. The Ashen Informant
        private static void E_AshenInformant(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Ashen Informant",
                "A beggar at the city gate catches your stirrup and speaks quietly. He claims to know where the Ashen Spawn were three days ago — specific roads, specific numbers. Either he saw it or he heard it. He wants 300 gold to keep talking.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Pay. Information has a price.", null, true,
                        "Lose 300 gold. Gain Ashen intel message."),
                    new InquiryElement("b", "Offer food instead of coin.", null, true,
                        "Lose 50 gold. Gain Merciful. Get partial information."),
                    new InquiryElement("c", "Dismiss him.", null, true,
                        "Nothing happens."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(-300);
                            string[] intelMessages = {
                                "The Ashen Spawn were moving east three days ago — a column of thirty or more, avoiding roads. They weren't raiding. They were positioning.",
                                "A Ashen lord was seen near the eastern passes without a military escort. Something quiet is being arranged.",
                                "The Spawn burned a grain depot north of here — not to eat, not to loot. Just to burn. The ash goes somewhere.",
                            };
                            Msg(intelMessages[_rng.Next(intelMessages.Length)], AshenColor);
                            break;
                        case "b":
                            ChangeGold(-50);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("He eats first, then speaks: \"They were near the river two days ago. That is all I know for certain.\" It is something.", DimColor);
                            break;
                        case "c":
                            Msg("He releases your stirrup and sits back. He may have been real. You will not know.", DimColor);
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
                        "Win: Renown +20, his clan -10 relation. Lose: Renown -10."),
                    new InquiryElement("b", "Ignore it with visible dignity.", null, true,
                        "Honor +1. Small renown gain for restraint."),
                    new InquiryElement("c", "Report the insult to the city lord.", null, true,
                        "Relation +5 with city lord."),
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
                        "Costs 2 days. Renown +20."),
                    new InquiryElement("b", "Decline. The gift is not a subject for study.", null, true,
                        "Nothing happens."),
                    new InquiryElement("c", "Give him false information and collect his fee.", null, true,
                        "Gain 200 gold. Lose Honor."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgingSystem.AgeHero(Hero.MainHero, 2);
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

        // 32. Another Fire
        private static void E_AnotherFire(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  Another Fire",
                "In the market crowd, for a moment, you feel it — the particular warmth that has nothing to do with weather. Someone here carries the gift, or something close to it. The feeling passes before you can locate the source.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Go back and search the market.", null, true,
                        "50/50: find them and gain good favor, or find nothing."),
                    new InquiryElement("b", "Let it pass. The fire finds its own.", null, true,
                        "Nothing happens."),
                    new InquiryElement("c", "Ask your men if anyone saw anything unusual.", null, true,
                        "Gain Calculating. Your men saw nothing useful, but you were looking."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (_rng.Next(2) == 0)
                            {
                                ChangeRelWithRandomLord(15);
                                Msg("You find them — a young woman with a merchant's colors and careful eyes. She knows what you are before you speak. The conversation is the kind you cannot have with anyone else. She is not a mage yet. She will be.", FireColor);
                            }
                            else
                                Msg("The feeling is gone. The market is ordinary. Whoever it was, they knew how to go quiet. You remember what that felt like, once.", DimColor);
                            break;
                        case "b":
                            Msg("The fire does not give you everything you reach for. You have learned to accept this.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("\"Nothing unusual,\" your sergeant says. \"Unless you count the man selling three different kinds of prayer-charm from one table.\" That is not it.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 33. The Ash-Touched Market
        private static void E_AshTouchedMarket(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Ash-Touched Market",
                "A woman in the market is selling goods she calls \"ash-touched\" — blessed by the Ashen, supposed to ward off the Spawn. She has a small crowd around her. The goods are ordinary cloth. You know the Ashen bless nothing.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Challenge her claim publicly.", null, true,
                        "Renown +5. Honor +1."),
                    new InquiryElement("b", "Report her to the city guard.", null, true,
                        "Relation +5 with city lord."),
                    new InquiryElement("c", "Buy something. Let people have their comfort.", null, true,
                        "Lose 200 gold."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeRenown(5f);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("\"The Ashen bless nothing,\" you say to the crowd. \"They consume. If these cloths were touched by them, you would not want to wear them.\" The crowd thins. The woman packs her table.", FireColor);
                            break;
                        case "b":
                            ChangeRelWithOwner(s, 5);
                            Msg("The guard takes your report without surprise. She is apparently known. This will be her second offence.", DimColor);
                            break;
                        case "c":
                            ChangeGold(-200);
                            Msg("You buy a length of cloth you will never use. People need to believe something wards off the dark. You cannot take that from them without giving something else.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═════════════════════════════════════════════════════════════════════
        // ENTER CITY/CASTLE — ASHEN ONLY
        // ═════════════════════════════════════════════════════════════════════

        // 34. Grey Eyes
        private static void E_GreyEyes(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  Grey Eyes",
                "A child at the gate stares at your face with the unself-conscious directness of the very young. \"Your eyes are the wrong colour,\" she says. \"And your hair. Are you dead?\" Her mother is mortified.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Kneel down and tell her you are just very, very old.", null, true,
                        "Gain Merciful. Morale +5."),
                    new InquiryElement("b", "Smile and ride past.", null, true,
                        "Nothing happens."),
                    new InquiryElement("c", "Say yes. And that you have come for children who misbehave.", null, true,
                        "Lose Honor. City relation -5."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            MobileParty.MainParty.RecentEventsMorale += 5f;
                            Msg("You kneel. \"I have been alive since before your grandmother's grandmother,\" you tell her. \"The colour goes after a while.\" She considers this with great seriousness. Her mother pulls her away, apologising. Your men are smiling.", GoodColor);
                            break;
                        case "b":
                            Msg("You ride past. She keeps staring at the space where you were.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            ChangeRelWithOwner(s, -5);
                            Msg("She bursts into tears. Her mother shouts something at your back. Your men are quiet for an uncomfortable stretch of road.", BadColor);
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
                        "Relation +10 with that lord."),
                    new InquiryElement("b", "Step aside and let them pass without contact.", null, true,
                        "Nothing happens."),
                    new InquiryElement("c", "Cross the crowd and speak to them directly.", null, true,
                        "50/50: Relation +20 — or they are hostile, Crime +10."),
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
                        "Costs 2 days. Renown +30. Morale +10."),
                    new InquiryElement("b", "Decline quietly and ride in.", null, true,
                        "Renown -5. Honor +1 for the refusal."),
                    new InquiryElement("c", "Do something small and charge for the sight.", null, true,
                        "Gain 300 gold. Lose Honor."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgingSystem.AgeHero(Hero.MainHero, 2);
                            ChangeRenown(30f);
                            MobileParty.MainParty.RecentEventsMorale += 10f;
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

        // ═════════════════════════════════════════════════════════════════════
        // ENTER CITY/CASTLE — GENERAL
        // ═════════════════════════════════════════════════════════════════════

        // 37. A Soldier Dying
        private static void E_SoldierDying(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "☠  A Soldier Dying",
                "A man in a city guard's colours is dragging himself toward the healers' quarter, one hand pressed to a wound in his side. He fell in the night watch, he says between his teeth. He is going in the wrong direction.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Help carry him to the healers.", null, true,
                        "Gain Merciful. Renown +5."),
                    new InquiryElement("b", "Point the right way and call for help.", null, true,
                        "Gain Merciful."),
                    new InquiryElement("c", "Keep riding.", null, true,
                        "Lose Honor."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ChangeRenown(5f);
                            Msg("Your men carry him between them. He loses consciousness before you reach the door. The healer says he will live. Your sergeant looks pleased with himself in a way that does not require comment.", GoodColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You shout for a runner and point the way. Two city folk respond without being asked. He will probably make it.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("He watches you ride past. He does not ask again.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 38. The Child's Bead
        private static void E_ChildsBead(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Child's Bead",
                "A small child stands at the city gate with a fistful of clay beads on hemp thread, selling them for a coin each. The beads are rough-made — probably the child's own work. They look up at you with absolute confidence.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Buy one for a coin.", null, true,
                        "Lose 50 gold. Gain Merciful."),
                    new InquiryElement("b", "Buy one and give triple the asking price.", null, true,
                        "Lose 150 gold. Gain Merciful. Morale +3."),
                    new InquiryElement("c", "Ride past.", null, true,
                        "Nothing happens."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(-50);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You buy one and pocket it. The child adds your coin to a small pile without breaking their sales face.", GoodColor);
                            break;
                        case "b":
                            ChangeGold(-150);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            MobileParty.MainParty.RecentEventsMorale += 3f;
                            Msg("You drop three coins instead of one. The child's expression cracks into a grin before they can control it. Your men notice. It is a good way to enter a city.", GoodColor);
                            break;
                        case "c":
                            Msg("The child turns to the next rider before you have passed.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 39. The Trade Council
        private static void E_TradeCouncil(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚖  The Trade Council",
                "The city's merchant council sends a runner as you enter. They meet weekly to discuss trade and security, and your arrival — with your reputation — means they would like a word with you at the table. It is an invitation, not a summons.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Attend and speak frankly on what you have seen.", null, true,
                        "Renown +10. Relation +5 with city faction."),
                    new InquiryElement("b", "Attend and say little — listen instead.", null, true,
                        "Relation +5 with city faction. Gain useful flavor."),
                    new InquiryElement("c", "Send your apologies and settle in at the inn.", null, true,
                        "Nothing happens."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeRenown(10f);
                            ChangeRelWithOwner(s, 5);
                            Msg("You tell them what the roads are like, what you have seen of the Ashen movements, what the villages are saying. The room is quiet in a listening way. You leave understanding the city better than before.", GoodColor);
                            break;
                        case "b":
                            ChangeRelWithOwner(s, 5);
                            Msg("You let them talk. Merchants talk. Behind the figures and complaints is a map of the city's fears — which roads, which clans, which names come up repeatedly. You file all of it.", DimColor);
                            break;
                        case "c":
                            Msg("The runner returns with your apologies. The council continues without you. You sleep well.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 41–49 — AFTER FIELD BATTLE
        // ═══════════════════════════════════════════════════════════════════

        // 41. The Dying Man
        private static void EB_DyingEnemy()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "☠  The Dying Man",
                "He is enemy colours, but the fight is over. He is propped against a wheel, holding his side, watching you walk past. He does not ask. He just watches.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Sit with him a moment. He does not need to die alone.", null, true,
                        "Gain Merciful. Morale +3."),
                    new InquiryElement("b", "Give him water and move on.", null, true,
                        "Gain Merciful."),
                    new InquiryElement("c", "Keep walking.", null, true,
                        "Lose Honor."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            MobileParty.MainParty.RecentEventsMorale += 3f;
                            Msg("You sit until the breathing stops. He never says anything. Neither do you. Your men, who are watching from the treeline, say nothing about it afterward.", GoodColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("He takes the water. He nods. You keep moving.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("You keep walking. Your shadow crosses him and keeps going.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 42. Found Among the Dead
        private static void EB_ChildAmongDead()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "☠  Found Among the Dead",
                "One of your men calls you over. Behind a farmstead caught in the crossfire, a child — eight, perhaps nine — is sitting very still among the dead. She is not wounded. She has been here since before the battle started.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Take her to the nearest village yourself.", null, true,
                        "Gain Merciful. Morale -3 from delay, but worth it."),
                    new InquiryElement("b", "Leave coin and point the direction. The party cannot stop.", null, true,
                        "Lose 200 gold. Gain Merciful."),
                    new InquiryElement("c", "Leave her. She can find her own way.", null, true,
                        "Lose Honor."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            MobileParty.MainParty.RecentEventsMorale -= 3f;
                            Msg("She does not speak on the road. She does not cry either. You leave her with a headman's wife who takes one look and asks no questions. Your men are quiet when you ride back.", GoodColor);
                            break;
                        case "b":
                            ChangeGold(-200);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You press enough coin into her hand to matter and show her north. She goes. You watch until she is out of sight.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("You ride away. Your sergeant watches the field behind you for a long time after the party moves on.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 43. The Picked-Over Dead
        private static void EB_LootedSoldier()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "☠  The Picked-Over Dead",
                "One of your men is caught with a silver ring that came from a dead enemy's finger. He does not try to hide it when you notice. He shrugs. \"He's not using it.\"",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Confiscate it and leave it with the dead man's effects.", null, true,
                        "Gain Honor."),
                    new InquiryElement("b", "Let it pass. That is what soldiers do.", null, true,
                        "Nothing happens."),
                    new InquiryElement("c", "Take your commander's cut first.", null, true,
                        "Gain 50 gold. Lose Honor."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You take the ring. He does not argue. The ring goes with the body. Your man holds his opinion behind his eyes.", GoodColor);
                            break;
                        case "b":
                            Msg("You walk on. The ring stays where it is. You don't look for more of this.", DimColor);
                            break;
                        case "c":
                            ChangeGold(50);
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("He hands half over with a knowing look. The two of you understand each other in a way you do not enjoy.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 44. One Man Remaining
        private static void EB_LoneSurrender()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  One Man Remaining",
                "All his companions are dead or fled. He is the last of them, and he has driven his sword into the ground and dropped to one knee. He is looking at you.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "He fought well. Release him and let him go home.", null, true,
                        "Gain Honor and Merciful."),
                    new InquiryElement("b", "Take him as a prisoner.", null, true,
                        "Standard outcome."),
                    new InquiryElement("c", "Offer him a place in your party — the kind of man who stays fighting is worth having.", null, true,
                        "Gain Honor. Morale +5."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("\"Go home,\" you tell him. He rises slowly, retrieves his sword, and walks east without turning back. Your men watch him go with expressions you cannot quite name.", GoodColor);
                            break;
                        case "b":
                            Msg("He stands without argument. He is a prisoner who will not cause trouble — you can see it in the way he holds his hands.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            MobileParty.MainParty.RecentEventsMorale += 5f;
                            Msg("He considers the offer for a moment, then stands and pulls the sword back out of the ground. \"All right,\" he says. Your sergeant gives him a look. It is a good look.", GoodColor);
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
                        "Lose 100 gold. Gain Merciful. Morale +5."),
                    new InquiryElement("b", "Allow it.", null, true,
                        "Morale +3."),
                    new InquiryElement("c", "Our dead only.", null, true,
                        "Nothing mechanical."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(-100);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            MobileParty.MainParty.RecentEventsMorale += 5f;
                            Msg("He moves through the field until dark. Your men camp at a distance and watch the lantern. Nobody speaks much.", GoodColor);
                            break;
                        case "b":
                            MobileParty.MainParty.RecentEventsMorale += 3f;
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
                        "Costs 1 day. Renown +10."),
                    new InquiryElement("b", "Take what seems useful and keep moving.", null, true,
                        "Gain Calculating. Some pages kept for later."),
                    new InquiryElement("c", "Leave it. Other fires are other fires.", null, true,
                        "Nothing happens."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
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
                        "Renown +10. Relation +5 with a lord."),
                    new InquiryElement("b", "A brief word and let him go.", null, true,
                        "Relation +5 with a lord."),
                    new InquiryElement("c", "Send him away. You have a battlefield to tend.", null, true,
                        "Nothing happens."),
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

        // 48. The Officer's Bargain
        private static void EB_OfficerDeal()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  The Officer's Bargain",
                "A captured enemy officer has been separated from the others. He leans in close and speaks quietly: he will tell you everything he knows about his lord's plans in exchange for release. He is calm. He has clearly been thinking about this since he surrendered.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Accept the bargain. Take the intelligence and release him.", null, true,
                        "Gain Honor. Relation -5 with his faction. Get intel."),
                    new InquiryElement("b", "Take the intelligence and keep him anyway.", null, true,
                        "Lose Honor. Get intel."),
                    new InquiryElement("c", "Refuse. He deals with his lord, not with you.", null, true,
                        "Gain Honor. He remains a prisoner."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ChangeRelWithRandomLord(-5);
                            string[] intel1 = {
                                "He confirms a supply route you suspected but couldn't verify. The lord is stretched thinner than he looks.",
                                "He names three lords who are only nominally loyal to the campaign. The alliance is more fragile than its banners suggest.",
                                "Their next planned strike is three days west. That gives you time.",
                            };
                            Msg(intel1[_rng.Next(intel1.Length)], DimColor);
                            Msg("He walks east without looking back. A man who kept his word.", GoodColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            string[] intel2 = {
                                "He tells you everything, understanding what the lie of your promise means. He says it without expression.",
                                "He speaks quickly, without eye contact. He knows. You both know.",
                            };
                            Msg(intel2[_rng.Next(intel2.Length)], BadColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("\"You deal with your lord,\" you say. He nods as if he expected exactly that. He is led away with the others.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 49. The Camp at Dusk (player won)
        private static void EB_CampAtDusk()
        {
            bool mage = MageKnowledge.IsMage;
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Camp at Dusk",
                "The battle is done. The camp is quiet in the way camps go quiet when men have spent themselves entirely — not resting, not sleeping, just stopped. They are tending wounds and staring at fires.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", mage ? "Go among them. Share what warmth you can." : "Go among them. Let them see you.", null, true,
                        mage ? "Costs 1 day. Renown +5. Morale +8." : "Renown +5. Morale +8."),
                    new InquiryElement("b", "Stay in your tent. They need space more than ceremony.", null, true,
                        "Nothing happens."),
                    new InquiryElement("c", "Oversee the surgeons. Make sure the work is done.", null, true,
                        "Gain Merciful. Morale +5."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (mage)
                            {
                                AgingSystem.AgeHero(Hero.MainHero, 1);
                                Msg("You move through the camp without speaking much. Where you pause by a fire, the warmth increases slightly. Nobody names it. They feel it anyway. By morning the camp has something it didn't have before.", FireColor);
                            }
                            else
                                Msg("You walk among them, stopping here and there. You know their names. That matters more than you might think. The camp settles.", GoodColor);
                            ChangeRenown(5f);
                            MobileParty.MainParty.RecentEventsMorale += 8f;
                            break;
                        case "b":
                            Msg("You give them the evening. They use it. By morning they have pieced themselves back together in the way soldiers do — quietly and thoroughly.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            MobileParty.MainParty.RecentEventsMorale += 5f;
                            Msg("You stand over the surgeons until the last wound is dressed. Nobody dies tonight who didn't have to. The men notice this without acknowledging it.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 50–56 — AFTER SIEGE (attacker won)
        // ═══════════════════════════════════════════════════════════════════

        // 50. The Fallen Lord's Household
        private static void ES_FallenLordsFamily()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚜  The Fallen Lord's Household",
                "The keep's inner gate opens, and a woman walks out with two children close behind her. She stands in the dust of the courtyard and looks at you. She does not plead. She is past pleading. She is waiting to know what you are.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Guarantee their safety. They are not the battle.", null, true,
                        "Gain Honor and Merciful. Renown +10."),
                    new InquiryElement("b", "Accept ransom. Let them buy their freedom.", null, true,
                        "Gain 800 gold."),
                    new InquiryElement("c", "Leave it to your captains. You have a keep to secure.", null, true,
                        "Lose Honor and Mercy. Crime +15."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ChangeRenown(10f);
                            Msg("\"You are under my protection,\" you say. She closes her eyes for one breath, then opens them. The children do not know what that means yet. They will.", GoodColor);
                            break;
                        case "b":
                            ChangeGold(800);
                            Msg("Ransom is negotiated and paid. They leave with what they came with. The arrangement is clean and everyone understands its nature.", GoldColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            ShiftTrait(DefaultTraits.Mercy, -1);
                            ChangeCrime(15f);
                            Msg("Your captains make their decisions. You hear what happens afterward from a man who does not look you in the eye when he reports it.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 51. The Question of the Gate Guard
        private static void ES_MakeExample()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚒  The Question of the Gate Guard",
                "Your senior commander comes to you with a suggestion. The captain of the gate — the man who held the door longest, who cost you the most time and men — is kneeling in the yard. The suggestion is that an example would be heard in the next city before you arrive.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Forbid it. He did his duty. That is not a crime.", null, true,
                        "Gain Honor and Merciful. Morale -5 — some men wanted this."),
                    new InquiryElement("b", "Leave it to the commander's discretion.", null, true,
                        "Nothing. You will not know the outcome."),
                    new InquiryElement("c", "Allow it. The lesson should reach the next gate first.", null, true,
                        "Crime +20. Morale +5. Lose Honor."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            MobileParty.MainParty.RecentEventsMorale -= 5f;
                            Msg("\"Release him.\" The commander presses his lips together but obeys. The gate captain looks at you for a long moment, then walks away. He will carry this for the rest of his life. So will you.", GoodColor);
                            break;
                        case "b":
                            Msg("You give no instruction. The commander interprets silence as permission. You do not ask what happened in the yard afterward.", DimColor);
                            break;
                        case "c":
                            ChangeCrime(20f);
                            ShiftTrait(DefaultTraits.Honor, -1);
                            MobileParty.MainParty.RecentEventsMorale += 5f;
                            Msg("The next city opens its gates before your siege engines are assembled. The lesson was heard exactly as intended. You do not let yourself think about what that means about who you are.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 52. The Surrounding Villages
        private static void ES_SurroundingVillages()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚒  The Surrounding Villages",
                "Three village elders have walked to the gate before the dust has settled. They are not there to celebrate or mourn. They want to know if the harvest will be left to them. They stand very still while they ask.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Give them your word. Their fields will not be touched.", null, true,
                        "Gain Honor and Merciful. Renown +5."),
                    new InquiryElement("b", "Make no promises, but wish them well.", null, true,
                        "Nothing happens."),
                    new InquiryElement("c", "Requisition their grain stores for the campaign.", null, true,
                        "Gain 500 gold. Lose Honor. Crime +10."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ChangeRenown(5f);
                            Msg("They hear it, look at each other, and bow. By evening the surrounding villages have sent food to your camp — small amounts, freely given. Your men understand the difference.", GoodColor);
                            break;
                        case "b":
                            Msg("They leave without expression. They have heard men make no promises before. They know what that usually means.", DimColor);
                            break;
                        case "c":
                            ChangeGold(500);
                            ShiftTrait(DefaultTraits.Honor, -1);
                            ChangeCrime(10f);
                            Msg("The grain wagons are taken. The elders watch from the road and say nothing. Next season the fields around this city will be underplanted. You will not be here to see it.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 53. The Treasury
        private static void ES_TreasuryFound()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚒  The Treasury",
                "The keep's treasury was sealed before the fighting started and remained untouched in the chaos. Your men have found it. It is substantial. How it is handled will be remembered.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Declare it yours and your party's — the coin of conquest.", null, true,
                        "Gain 1200 gold."),
                    new InquiryElement("b", "Distribute it among the men who fought.", null, true,
                        "Gain 400 gold (your share). Morale +15. Renown +10."),
                    new InquiryElement("c", "Seal it and send word to your liege — this belongs to the realm.", null, true,
                        "Gain Honor. Relation +10 with your liege."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(1200);
                            Msg("The coin is counted and stored. Your men watch the wagons load. They will remember this when the next city needs to be taken.", GoldColor);
                            break;
                        case "b":
                            ChangeGold(400);
                            MobileParty.MainParty.RecentEventsMorale += 15f;
                            ChangeRenown(10f);
                            Msg("The distribution takes an hour. By the time it is done, your men are laughing. You have not paid them to take this city — you have paid them to follow you to the next one.", GoodColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ChangeRelWithRandomLord(10);
                            Msg("The seal is replaced and a rider sent. Your liege will hear of this. So will everyone else. Some of your men are disappointed. All of them respect it.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 54. Old Marks (mage-gated)
        private static void ES_OldScorchmarks()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  Old Marks",
                "In the lowest level of the keep, your torch finds scorched walls that predate this battle by years. The pattern is not the random damage of fire — it is deliberate, and it is old. Someone with the gift worked here, before you, and the working was not small.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Study them carefully. These are a record.", null, true,
                        "Costs 1 day. Gain Ashen intel."),
                    new InquiryElement("b", "Note the pattern and move on.", null, true,
                        "Gain Calculating. Pattern noted, filed away."),
                    new InquiryElement("c", "Have them scrubbed out. Your men don't need to ask questions.", null, true,
                        "Nothing happens."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
                            string[] siegeIntel = {
                                "The marks are an Ashen working from at least twenty years ago. Someone in this lord's line made a bargain, or a mistake. Either way, the cold has been in this keep longer than anyone living here knew.",
                                "The pattern is a ward — badly made, partially collapsed. Someone was trying to keep the Ashen out. They failed, and then whoever made the ward left, or was removed.",
                                "The scorching forms words in a dialect you only partially read. The last phrase translates roughly as: 'it was already here when we arrived.'"
                            };
                            Msg(siegeIntel[_rng.Next(siegeIntel.Length)], AshenColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("The marks are Ashen-adjacent — old, deliberate, something happened here. You cannot tell what without more time. You file it away.", FireColor);
                            break;
                        case "c":
                            Msg("Masons are set to work. By morning the walls are blank. Whatever they recorded, it is gone.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 55. The Shrine Keeper
        private static void ES_ShrineKeeper()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚒  The Shrine Keeper",
                "An old priest stands in front of the city's main shrine with his arms out. He is not armed. He is not going to move. He is simply standing there, making it structurally inconvenient to do anything without going through him first.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Honor the shrine and the man standing in front of it.", null, true,
                        "Gain Honor and Merciful. Renown +5."),
                    new InquiryElement("b", "Take the valuables but leave the priest unharmed.", null, true,
                        "Gain 400 gold. Lose Honor."),
                    new InquiryElement("c", "Have him removed. Gently, but removed.", null, true,
                        "Gain 500 gold. Lose Honor. Crime +10."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ChangeRenown(5f);
                            Msg("You wave your men back and give the shrine a wide berth. The old priest lowers his arms. He does not thank you. He simply returns to tending the lamps as if you were never here. The city sees all of this.", GoodColor);
                            break;
                        case "b":
                            ChangeGold(400);
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("The valuables are collected. The priest watches without speaking. You leave him standing in front of an emptied shrine, which is its own kind of statement.", BadColor);
                            break;
                        case "c":
                            ChangeGold(500);
                            ShiftTrait(DefaultTraits.Honor, -1);
                            ChangeCrime(10f);
                            Msg("Two men take him by the arms and carry him, still arms-out, to the street. The city watches from windows. The city remembers.", BadColor);
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
                        "Gain Honor. Morale -8. Renown +15 from city folk."),
                    new InquiryElement("b", "One hour. Then order restored.", null, true,
                        "Crime +15. Morale +10. Lose Honor."),
                    new InquiryElement("c", "Declare it a clean capture. The work is done — and you mean it.", null, true,
                        "Gain Honor. Morale +5. No crime."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            MobileParty.MainParty.RecentEventsMorale -= 8f;
                            ChangeRenown(15f);
                            Msg("The order goes out. Some of your men are angry. The city folk open their shutters by midnight. By morning there are women putting bread on windowsills for your sentries. That is a different kind of victory.", GoodColor);
                            break;
                        case "b":
                            ChangeCrime(15f);
                            ShiftTrait(DefaultTraits.Honor, -1);
                            MobileParty.MainParty.RecentEventsMorale += 10f;
                            Msg("One hour. The streets are loud and then quiet. Restoring order takes two hours, not one. You do not count what was taken. Some cities remember this for a generation.", BadColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            MobileParty.MainParty.RecentEventsMorale += 5f;
                            Msg("\"The work is done.\" The captains pass it along. The men take the words at face value — they are tired, and a clean victory is easier to carry home than the other kind.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 57–62 — AFTER RAID
        // ═══════════════════════════════════════════════════════════════════

        // 57. The Man Who Stayed
        private static void ER_HeadmanConfronts()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  The Man Who Stayed",
                "He did not run. Most of them did. But the headman stood at the edge of the village and watched the whole thing, and now he is standing in the road as your party forms up to leave. He is not armed. He just looks at you.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Stop and hear what he has to say.", null, true,
                        "Gain Merciful. Morale will carry the weight."),
                    new InquiryElement("b", "Leave him gold and ride past.", null, true,
                        "Lose 400 gold. Gain Merciful."),
                    new InquiryElement("c", "Ride around him.", null, true,
                        "Nothing happens."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("He says the village will not survive another season of this. He says it without accusation, as a fact. You have no answer that helps. You ride on carrying what he said. That is what he wanted.", DimColor);
                            break;
                        case "b":
                            ChangeGold(-400);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("He does not take the coin at first. Then he does — not for himself, he makes that clear. He steps aside. He does not watch you leave.", GoldColor);
                            break;
                        case "c":
                            Msg("You go around him. He does not move from the road for a long time.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 58. The Child on the Road
        private static void ER_ChildFollows()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "☠  The Child on the Road",
                "Half a mile out, your rearguard reports a child following the column. She has been keeping pace since the village. She is not asking for anything — she is just following.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Stop and take her back yourself.", null, true,
                        "Gain Merciful. Morale -3."),
                    new InquiryElement("b", "Send a man back with coin for her care.", null, true,
                        "Lose 200 gold. Gain Merciful."),
                    new InquiryElement("c", "She will turn back on her own.", null, true,
                        "Lose Honor."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            MobileParty.MainParty.RecentEventsMorale -= 3f;
                            Msg("You ride back alone. She watches you come without expression. You take her back to the village and leave her with an old woman who opens the door without surprise. You do not speak on the ride back.", GoodColor);
                            break;
                        case "b":
                            ChangeGold(-200);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("Your man catches up with her, presses the coin into her hand, points back toward the village. She stops following. You watch from the column.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("She follows for another mile, then sits down at the roadside. Your rearguard does not report her again.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 59. A Question of Shares
        private static void ER_SpoilsDivision()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  A Question of Shares",
                "Your men are arguing about the split of what was taken. It has gone from muttering to raised voices. Two groups have formed. Your sergeants are watching you.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Rule on it: more to those who fought harder, and say why.", null, true,
                        "Morale +10. Gain Honor."),
                    new InquiryElement("b", "Equal shares — you will not have this in your camp.", null, true,
                        "Morale +5."),
                    new InquiryElement("c", "Leave it to the sergeants. You trust them.", null, true,
                        "50/50: Morale +5 or Morale -5."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            MobileParty.MainParty.RecentEventsMorale += 10f;
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You make the call clearly and explain the reasoning. The arguing stops. The men respect the decision more than they would have respected equal shares — because they know you watched.", GoodColor);
                            break;
                        case "b":
                            MobileParty.MainParty.RecentEventsMorale += 5f;
                            Msg("Equal shares, final answer. Some men are happy. Some are not. All of them stop arguing, which is the point.", DimColor);
                            break;
                        case "c":
                            if (_rng.Next(2) == 0)
                            {
                                MobileParty.MainParty.RecentEventsMorale += 5f;
                                Msg("The sergeants sort it cleanly. You hear the voices settle. Sometimes trust is the right call.", DimColor);
                            }
                            else
                            {
                                MobileParty.MainParty.RecentEventsMorale -= 5f;
                                Msg("The sergeants cannot agree with each other. By the time it resolves, everyone has less than they started with and nobody is sure why.", BadColor);
                            }
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 60. The Woman in the Doorway
        private static void ER_WomanInDoorway()
        {
            bool mage = MageKnowledge.IsMage;
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "☠  The Woman in the Doorway",
                "In the last house at the edge of the village, an old woman stands in the doorway as you ride past. She says one word. You catch it — it's not a language you know. But the intonation is clear. It is not a blessing.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Ask what she said.", null, true,
                        "Gain Calculating. What the word means stays with you."),
                    new InquiryElement("b", "Ride on.", null, true,
                        "Nothing happens."),
                    new InquiryElement("c", mage ? "Feel the word land. Your fire flinches." : "Give her a coin and ride on.", null, true,
                        mage ? "Costs 1 day. Ominous flavor." : "Lose 100 gold. Gain Merciful."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("She says it again when asked, and then a third time slowly. Your interpreter shakes his head. \"Old northern dialect,\" he says. \"Means something like... 'come back as ash.'\"", DarkColor);
                            break;
                        case "b":
                            Msg("You ride past. Behind you, she is still saying it. You can hear it past the first bend in the road.", DimColor);
                            break;
                        case "c":
                            if (mage)
                            {
                                AgingSystem.AgeHero(Hero.MainHero, 1);
                                Msg("The word is old. Old enough that your fire knows it before your mind does. There is a weight in it that travels with you for the rest of the day, like smoke that followed a wind home.", AshenColor);
                            }
                            else
                            {
                                ChangeGold(-100);
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                Msg("You drop a coin in the road in front of her doorway. She does not acknowledge it. She keeps saying the word.", DimColor);
                            }
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 61. Still Burning (mage-gated)
        private static void ER_ShrineBurning()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  Still Burning",
                "As your party forms up to leave, you notice the village shrine is lit. You didn't touch it. You are certain of that. But the flame is gold and still, not orange and moving — the way a fire looks when it has been touched by something other than wood and air.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Stop and examine what lit it.", null, true,
                        "Costs 1 day. Unsettling flavor."),
                    new InquiryElement("b", "Ride on. You didn't do that.", null, true,
                        "Nothing. The question stays."),
                    new InquiryElement("c", "Snuff it clean. It should not be there.", null, true,
                        "Costs 1 day. Gain Merciful."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
                            Msg("You press your hand close. The flame does not react the way fire should. It knows you. That is the only word for it. It knows you, and it has been here longer than this village. You ride away with the feeling of something watching your back.", FireColor);
                            break;
                        case "b":
                            Msg("You ride out. You look back at the crossroads. The shrine is still lit. Whatever it is, it stays behind.", DimColor);
                            break;
                        case "c":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You breathe it out. The flame obeys, goes dark. The bowl is just an iron bowl again. The village does not see this, but it will feel the difference when it wakes.", FireColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 62. A Veteran Asks
        private static void ER_VeteranQuestions()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  A Veteran Asks",
                "An older man in your party — years of service, no complaints, someone you trust — rides up alongside you on the road out. He doesn't look at you. He says, quietly: \"I've been thinking about what we're doing.\"",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "\"Say it plainly. I won't punish honest questions.\"", null, true,
                        "Gain Honor. He respects you more."),
                    new InquiryElement("b", "Deflect. The road is the road.", null, true,
                        "Nothing happens."),
                    new InquiryElement("c", "\"If you have doubts, the party is better without them.\"", null, true,
                        "Morale -8. Lose Honor."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("He talks for a while. It is not what you expected — he is not questioning you, he is questioning the shape of the campaign. His conclusion is that you are better than this, which is its own kind of pressure.", GoodColor);
                            break;
                        case "b":
                            Msg("He rides at your pace for another minute, then drops back. He will raise it again when the weight of it gets heavier.", DimColor);
                            break;
                        case "c":
                            MobileParty.MainParty.RecentEventsMorale -= 8f;
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("He drops back without argument. The men who heard it are quieter for the rest of the day. The question does not go away — you just made it invisible.", BadColor);
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
                        "Renown +5. Relation +10 with city faction."),
                    new InquiryElement("b", "Attend and speak plainly.", null, true,
                        "Gain Honor. Relation +10 with city faction."),
                    new InquiryElement("c", "Decline gracefully.", null, true,
                        "Nothing happens."),
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
                        "Renown +5. Gain Honor. Small crime +5 for disturbing order."),
                    new InquiryElement("b", "Walk past. You've heard it before and worse.", null, true,
                        "Nothing happens."),
                    new InquiryElement("c", "Press a coin into his collection box as you pass.", null, true,
                        "Gain Merciful. Small amusing scene."),
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
                        "Lose 300 gold. Gain Merciful. Normal entry."),
                    new InquiryElement("b", "Enter by right. You have business here.", null, true,
                        "Entry granted. Morale -3 — your men are uncomfortable."),
                    new InquiryElement("c", "Turn back and camp outside until it clears.", null, true,
                        "Nothing. You avoid the city for now."),
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
                            MobileParty.MainParty.RecentEventsMorale -= 3f;
                            Msg("She waves you through without protest. Your men pass the sick quarter without looking at it directly. Everyone is thinking the same thing and nobody says it.", DimColor);
                            break;
                        case "c":
                            Msg("You set up camp outside the walls. The city can wait. Your men are not displeased by this decision.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 66. The Challenge (renown≥300)
        private static void EC2_SellswordChallenge(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  The Challenge",
                "A famous sellsword captain — you know the name, most people in this part of Calradia do — is at the city inn and has sent a message to your party before you've finished stabling the horses. He wants a bout in the training yard. No weapons, no grudges, just to know.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Accept. You'd like to know too.", null, true,
                        "Win: Renown +20, Morale +8. Lose: Renown -5, Morale -3."),
                    new InquiryElement("b", "Decline. You have a city to see.", null, true,
                        "Nothing happens."),
                    new InquiryElement("c", "Accept but make it a training bout, not a spectacle.", null, true,
                        "Morale +8. No renown swing — just a good morning."),
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
                                MobileParty.MainParty.RecentEventsMorale += 8f;
                                Msg("He is better than most. You are better than him. The yard fills up by the second pass and is not quiet again until it is over. He shakes your hand without saying anything. That is respect.", GoodColor);
                            }
                            else
                            {
                                ChangeRenown(-5f);
                                MobileParty.MainParty.RecentEventsMorale -= 3f;
                                Msg("He is fast and he knows it. The bout ends in his favour in a way that fills the yard with an uncomfortable quiet. You accept it cleanly, which helps. It does not help enough.", BadColor);
                            }
                            break;
                        case "b":
                            Msg("He takes the refusal in good humour. \"Another time,\" he says, and means it.", DimColor);
                            break;
                        case "c":
                            MobileParty.MainParty.RecentEventsMorale += 8f;
                            Msg("No audience, no stakes. Just two people testing the limits of what they know. By the end you have both learned something. Your men eat breakfast in a good mood.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 67. Marks on the Wall
        private static void EC2_AshenGraffiti(Settlement s)
        {
            bool mage = MageKnowledge.IsMage;
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  Marks on the Wall",
                "Ashen sigils have appeared overnight on a stretch of the city wall near the market gate. Not painted — scorched, from inside the stone. The city guard is looking at them with the expression of men who would like to pretend this is not what it is.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Report it to the city lord with urgency.", null, true,
                        "Relation +5 with city lord. Renown +5."),
                    new InquiryElement("b", mage ? "Read them. You recognise the script." : "Study them carefully.", null, true,
                        mage ? "Flavor intel message." : "Flavor only."),
                    new InquiryElement("c", "Start scrubbing one out. Somebody has to.", null, true,
                        "Gain Merciful. Morale +3 — your men appreciate it."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeRelWithOwner(s, 5);
                            ChangeRenown(5f);
                            Msg("The lord goes pale. He sends men to the wall immediately. He also sends a man to your inn, later, with wine and a quietly desperate look. He wants you to stay longer.", DimColor);
                            break;
                        case "b":
                            if (mage)
                                Msg("The sigils are old Ashen marking-script — a claim, not a warning. This city has been chosen for something. The Ashen do not put their name on a thing until they are confident they will have it.", AshenColor);
                            else
                                Msg("The marks are deliberate and recent. Whatever language they belong to, the intent behind them is not ambiguous. Someone wanted to be seen doing this.", DarkColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            MobileParty.MainParty.RecentEventsMorale += 3f;
                            Msg("It takes an hour and three men. By the time you are done, the wall is clean and a small crowd has formed to watch. Nobody cheers. They do look steadier.", GoodColor);
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
                        "Gain Merciful. Lose 150 gold."),
                    new InquiryElement("b", "Take it back and release them without a word.", null, true,
                        "Nothing happens."),
                    new InquiryElement("c", "Hand them to the city watch.", null, true,
                        "Lose Honor. Small crime +5."),
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

        // 69. An Accusation
        private static void LC2_MerchantAccusation(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚖  An Accusation",
                "A merchant is blocking the gate exit, waving a ledger and claiming one of your men broke three jars of oil in his shop and paid for none of it. Your man says he was never in that shop. One of them is lying.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Investigate it properly.", null, true,
                        "50/50: either vindicated or you pay 300 gold."),
                    new InquiryElement("b", "Pay the claim and move on.", null, true,
                        "Lose 300 gold."),
                    new InquiryElement("c", "Back your man and call the accusation a lie.", null, true,
                        "Gain Honor. 30% chance Crime +10 if you were wrong."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (_rng.Next(2) == 0)
                            {
                                Msg("A witness confirms your man's account. The merchant closes his ledger without making eye contact. Your man leaves with his reputation intact.", GoodColor);
                            }
                            else
                            {
                                ChangeGold(-300);
                                Msg("A second witness places your man in the shop. He goes white. You pay the merchant and deal with your man separately. Both of them are now the problem.", BadColor);
                            }
                            break;
                        case "b":
                            ChangeGold(-300);
                            Msg("You pay without comment. The merchant looks slightly embarrassed, which is either guilt or satisfaction — you can't tell which.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            if (_rng.NextDouble() < 0.30)
                            {
                                ChangeCrime(10f);
                                Msg("You call it false and leave. A city guard runs after you with a formal complaint. Your man was there after all. The ride out is quiet.", BadColor);
                            }
                            else
                                Msg("You call it false and leave. Nobody follows. Your man thanks you with a look rather than words.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 70. The Letter
        private static void LC2_SealedLetter(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Letter",
                "A captain of the city guard catches your stirrup at the gate with a sealed letter and a straightforward request: carry it to a lord in the next city. He can't trust regular riders with it. He is trusting you because of who you are.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Agree to carry it.", null, true,
                        "Relation +5 with guard captain's lord."),
                    new InquiryElement("b", "Decline. You carry enough obligations.", null, true,
                        "Nothing happens."),
                    new InquiryElement("c", "Accept and read it on the road.", null, true,
                        "Lose Honor. Get intel flavor."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeRelWithRandomLord(5);
                            Msg("You take the letter. It is lighter than it looks. The captain's relief is heavier.", DimColor);
                            break;
                        case "b":
                            Msg("He accepts the refusal without argument. He turns to look for another rider, with the expression of a man who is running out of people he trusts.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            string[] letterContent = {
                                "The letter details a clan dispute that has not yet gone public. Three names, one allegation, and a request for discretion. You know now what the city guard knew. It sits uncomfortably.",
                                "The letter is a warning about Ashen activity near the eastern roads. Specific, current, and clearly something the guard wanted kept from official channels.",
                            };
                            Msg(letterContent[_rng.Next(letterContent.Length)], DarkColor);
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
                        "Relation +10 with that lord."),
                    new InquiryElement("b", "Return it with respect — you prefer not to carry obligations.", null, true,
                        "Gain Honor. Relation +5."),
                    new InquiryElement("c", "Accept it and send nothing back.", null, true,
                        "Relation +5."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeRelWithOwner(s, 10);
                            Msg("The servant carries your thanks back. The lord will hear it before evening. This is how things are maintained at this level — small, consistent acknowledgements.", DimColor);
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
                        "Costs 1 day. Uncover Ashen influence. Renown +5."),
                    new InquiryElement("b", "Trust your fire and ride. You know what this means.", null, true,
                        "Flavor message. Nothing mechanical."),
                    new InquiryElement("c", "Report what you felt to the city lord.", null, true,
                        "Relation +5. They will not understand — but they will remember."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
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
                        "Lose 300 gold. Gain Merciful. Renown +5."),
                    new InquiryElement("b", "Watch for a moment and leave a small coin.", null, true,
                        "Lose 50 gold. Gain Merciful."),
                    new InquiryElement("c", "Offer him a place with your party — traveling with a lord is safer.", null, true,
                        "Gain Merciful. He may prove useful."),
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

        // 74. The Dry Well
        private static void EV2_DriedWell(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✿  The Dry Well",
                "The village well has given out. A group of men is standing around the dry shaft with the particular stillness of people trying to understand a problem they cannot solve with what they have. Children are being sent to a stream a mile away.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Fund a new well. Leave enough for the work to be done.", null, true,
                        "Lose 400 gold. Gain Generous and Merciful. Renown +10."),
                    new InquiryElement("b", "Have your men help dig what they can this afternoon.", null, true,
                        "Gain Merciful. Morale -3 — it's hot work."),
                    new InquiryElement("c", "Note it. Ride on.", null, true,
                        "Nothing happens."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(-400);
                            ShiftTrait(DefaultTraits.Generosity, 1);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ChangeRenown(10f);
                            Msg("You leave the coin with the headman and the name of a reliable well-digger from the nearest town. You will not see the finished well. You also will not have to.", GoodColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            MobileParty.MainParty.RecentEventsMorale -= 3f;
                            Msg("Your men dig for three hours. They hit moisture at twenty feet. Not a full well, but enough for now. They come out muddy and satisfied in a way that has nothing to do with strategy.", GoodColor);
                            break;
                        case "c":
                            Msg("You note it. You ride on. You think about it for the next mile before moving on to other things.", DimColor);
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
                        "Gain Merciful. Possible tactical advantage."),
                    new InquiryElement("b", "Pay for more detail.", null, true,
                        "Lose 200 gold. Specific intel message."),
                    new InquiryElement("c", "Thank her politely and file it under general caution.", null, true,
                        "Nothing happens."),
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
                        mage ? "Costs 1 day. Gain Merciful. Renown +5." : "Gain Merciful. Renown +5."),
                    new InquiryElement("b", "Acknowledge it warmly and leave a gift.", null, true,
                        "Lose 300 gold. Gain Merciful."),
                    new InquiryElement("c", "Ask them gently to choose another name — it is a burden.", null, true,
                        "Gain Honor. Small kind scene."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (mage) AgingSystem.AgeHero(Hero.MainHero, 1);
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
                        "Gain Merciful. It keeps barking, but you tried."),
                    new InquiryElement("b", "Ignore it.", null, true,
                        "Nothing happens. It doesn't stop."),
                    new InquiryElement("c", "Offer it meat from your pack.", null, true,
                        "Gain Merciful. It stops. Uncertainly."),
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
        // EVENTS 78–80 — LEAVE VILLAGE (new)
        // ═══════════════════════════════════════════════════════════════════

        // 78. The Road Companions
        private static void LV2_PilgrimsRequest(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Road Companions",
                "A group of eight pilgrims — mixed ages, walking — asks to travel with your column to the next town. The roads are dangerous and they know it. They are not asking for soldiers; they are asking to walk near soldiers.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Accept. Nobody suffers for your protection.", null, true,
                        "Gain Honor and Merciful. Morale -3 — slower pace."),
                    new InquiryElement("b", "Decline regretfully. You move faster than they can.", null, true,
                        "Nothing happens."),
                    new InquiryElement("c", "Accept and name a fee.", null, true,
                        "Gain 200 gold. Lose Honor."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            MobileParty.MainParty.RecentEventsMorale -= 3f;
                            Msg("They walk at the rear of your column and do not cause trouble. By evening one of your younger soldiers is carrying an old man's pack. Nobody ordered it.", GoodColor);
                            break;
                        case "b":
                            Msg("They accept it. They will wait for the next party. They have done this before.", DimColor);
                            break;
                        case "c":
                            ChangeGold(200);
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("They pay without argument — they were expecting to pay something. That is what makes it worse.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 79. Your Horse, Pulling Up
        private static void LV2_LameHorseYours(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✿  Your Horse, Pulling Up",
                "Half a mile from the village your horse begins favoring its left foreleg. Your groom examines it and shakes his head — stone bruise, probably from yesterday's road. Not serious, but not ignorable.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Go back and have it properly treated by the village stable.", null, true,
                        "Lose 150 gold. The horse is well."),
                    new InquiryElement("b", "Push on at a slower pace and deal with it at the next town.", null, true,
                        "Morale -3 — your men see you push a lame horse."),
                    new InquiryElement("c", "Ask a villager to sell you their horse and leave this one in their care.", null, true,
                        "Lose 300 gold."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(-150);
                            Msg("The village stable does adequate work. An hour and a half lost. The horse leaves sound. Your groom approves with the minimal expression of a man paid to disapprove of most things.", DimColor);
                            break;
                        case "b":
                            MobileParty.MainParty.RecentEventsMorale -= 3f;
                            Msg("You push on slowly. Your groom says nothing. Your men keep glancing back at the leg. Some things are noticed without being said.", BadColor);
                            break;
                        case "c":
                            ChangeGold(-300);
                            Msg("The farmer is pleased with the exchange. The new horse is unremarkable and sound. Your horse will be well-kept — the farmer already likes it more than he liked you.", GoldColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 80. The Hidden Note
        private static void LV2_VillageGirlNote(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Hidden Note",
                "As your party leaves, a girl — perhaps sixteen — slips a folded piece of cloth into your saddlebag when she hands your horse back its feed bucket. She does not look at you when she does it. When you open it, it is a careful description of a lord's behavior toward this village that ends with a name and a plea.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Look into it when you reach the next city.", null, true,
                        "Gain Merciful. Renown +5. Relation -10 with that lord."),
                    new InquiryElement("b", "Send the note to a higher authority through proper channels.", null, true,
                        "Relation +5 with a senior lord. Nothing immediate."),
                    new InquiryElement("c", "Pocket it and ride. You cannot carry every village's problem.", null, true,
                        "Lose Honor."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ChangeRenown(5f);
                            ChangeRelWithRandomLord(-10);
                            Msg("You investigate. The story holds. The lord in question receives a visit from your party that is brief and unmistakably pointed. He complains about it later to people who know better than to act surprised.", GoodColor);
                            break;
                        case "b":
                            ChangeRelWithRandomLord(5);
                            Msg("The note goes up the chain. Whether anything comes of it depends on who intercepts it. You've done what you can without taking it personally, which means you may have done nothing.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("You pocket it and ride. On the road out you pass the village boundary stone and notice someone has scratched something into it recently. You don't stop to read it.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 81–83 — ENTER VILLAGE (new batch)
        // ═══════════════════════════════════════════════════════════════════

        // 81. The Knight Without a Lord
        private static void EV3_OldKnight(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  The Knight Without a Lord",
                "A man in worn but impeccably maintained armor is splitting wood outside the inn — methodical, precise, the kind of labor that comes from training rather than habit. His sword hangs on a post nearby. He is not a farmhand.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Offer him a place — a knight without a lord is a waste.", null, true,
                        "Morale +5. Gain Honor."),
                    new InquiryElement("b", "Give him coin and leave him his choice.", null, true,
                        "Lose 300 gold. Gain Merciful."),
                    new InquiryElement("c", "Let him be. He chose this road for a reason.", null, true,
                        "Nothing happens."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            MobileParty.MainParty.RecentEventsMorale += 5f;
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("He sets the axe down, takes his sword from the post, and follows you without a word of ceremony. Your veterans make room without being asked.", GoodColor);
                            break;
                        case "b":
                            ChangeGold(-300);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("He takes the coin without embarrassment. \"I've earned enough of those,\" he says. You believe him. He goes back to the wood.", DimColor);
                            break;
                        case "c":
                            Msg("He splits another log without looking up. Whatever brought him here, it is his.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 82. The Wedding
        private static void EV3_WeddingNews(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✿  The Wedding",
                "The village is mid-celebration — music, tables in the square, flower garlands. As you ride in, a rider from the east arrives ahead of you and whispers something to the headman. The headman looks at the bride. The music does not stop, but the bride's expression changes. You know the look. Bad news from the direction of the Ashen.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Stay and celebrate with them. Let the road wait one hour.", null, true,
                        "Morale +8. Gain Merciful."),
                    new InquiryElement("b", "Leave a gift and ride on — they don't need witnesses to their grief.", null, true,
                        "Lose 300 gold. Gain Merciful."),
                    new InquiryElement("c", "Offer the groom a place in your column — if he wants to reach his family.", null, true,
                        "Gain Honor. Morale +5."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            MobileParty.MainParty.RecentEventsMorale += 8f;
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You stay for the dancing. The music recovers before the faces do. By the time your party leaves, the celebration has decided to be real rather than pretend. That is what people do. That is what they have to do.", GoodColor);
                            break;
                        case "b":
                            ChangeGold(-300);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("The gift goes to the headman quietly. Nobody stops celebrating, and nobody believes the celebration anymore either. Sometimes that is all that can be offered.", DimColor);
                            break;
                        case "c":
                            MobileParty.MainParty.RecentEventsMorale += 5f;
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("The groom looks at the bride. She nods once. He picks up his jacket from the chair and follows you without going back for anything else. Your men do not make jokes about it.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 83. The Collector
        private static void EV3_VillageCoercion(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚖  The Collector",
                "A city tax collector stands in the village square with a ledger showing numbers that cannot be legal — three times the standard levy, with a line for a \"processing fee\" that does not exist in any law you know. The headman is signing it because he does not see another option. The collector hasn't noticed you arrive.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Intervene — those figures are illegal and you will say so.", null, true,
                        "Gain Honor. Renown +5. Relation -10 with his lord."),
                    new InquiryElement("b", "Pay the excess yourself and send the headman on his way.", null, true,
                        "Lose 400 gold. Gain Merciful and Honor."),
                    new InquiryElement("c", "It is not your district.", null, true,
                        "Lose Honor."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ChangeRenown(5f);
                            ChangeRelWithOwner(s, -10);
                            Msg("You name the statute. The collector's face runs through several expressions before landing on careful deference. He adjusts the figures. He will report this to his lord. So will the headman, in a different tone.", GoodColor);
                            break;
                        case "b":
                            ChangeGold(-400);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You pay the difference quietly, so the headman's humiliation is at least private. He looks at you for a moment like he is calculating a debt he will never be able to repay.", GoodColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("You ride past. The headman signs. The collector notes the figures without looking up.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 84–86 — LEAVE VILLAGE (new batch)
        // ═══════════════════════════════════════════════════════════════════

        // 84. Two Sons
        private static void LV3_TwoSons(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  Two Sons",
                "A farmer stands at the road's edge with two young men behind him. One wants to go with your party; the other says the village needs him for the harvest. Both are right. The farmer's hands are shaking. He has asked you to decide, because he cannot.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "The one who wants to go should go — that matters.", null, true,
                        "Gain Honor. Morale +3."),
                    new InquiryElement("b", "Neither. This village needs both of them.", null, true,
                        "Gain Honor and Merciful."),
                    new InquiryElement("c", "Tell the farmer: this is his decision, not yours.", null, true,
                        "Nothing mechanical. You hand it back."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            MobileParty.MainParty.RecentEventsMorale += 3f;
                            Msg("The eager son takes two steps forward before the farmer can react. He joins the column with the grin of someone who spent the last year waiting to do exactly this.", GoodColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("Both sons stay. The father's hands stop shaking. You ride on. This may be the most useful thing you do today.", GoodColor);
                            break;
                        case "c":
                            Msg("The farmer looks at you for a long moment. Then he looks at his sons. Then he takes a breath and makes the decision he was always going to have to make.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 85. The Familiar Face
        private static void LV3_HiddenCriminal(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Familiar Face",
                "You recognise him from a different angle three months ago — same scar above the left eye, same way of standing. He led the bandit group that hit your supply column. Killed one of your men. He is sitting behind a cobbler's bench, working quietly, and he has not recognised you yet.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Have him taken. He owes your party a death.", null, true,
                        "Gain Honor. Renown +5."),
                    new InquiryElement("b", "Let him be. He found his way out of that life.", null, true,
                        "Gain Merciful. Honor cost for what he took."),
                    new InquiryElement("c", "Make him understand that silence has a price.", null, true,
                        "Gain 400 gold. Lose Honor."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ChangeRenown(5f);
                            Msg("He recognises you a second before your men reach him. He doesn't run. Whatever he thought this moment would look like, he has been waiting for it. Your soldier's name is spoken in camp that evening.", GoodColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You ride on. He looks up as your party passes, and then he sees you, and then he goes very still over his work. You keep going. Whatever he does with the rest of his life, you are not in it.", DimColor);
                            break;
                        case "c":
                            ChangeGold(400);
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("He understands immediately. The coin is produced from a box under the bench. He does not make eye contact when he hands it over. Neither do you.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 86. The Inn Fire
        private static void LV3_InnFire(Settlement s)
        {
            bool mage = MageKnowledge.IsMage;
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Inn Fire",
                "As your party forms up to leave, a shout goes up from the inn — grease fire in the kitchen, and it is catching fast. The innkeeper is shouting. People are running. The thatch is dry.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", mage ? "Extinguish it before it spreads." : "Organize your men into a bucket line.", null, true,
                        mage ? "Costs 1 day. Gain Merciful. Renown +5." : "Gain Merciful. Morale +5."),
                    new InquiryElement("b", "Get people out of the building first.", null, true,
                        "Gain Merciful. Morale -3 — hard work."),
                    new InquiryElement("c", "There are people enough here to handle it.", null, true,
                        "Lose Honor."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            if (mage)
                            {
                                AgingSystem.AgeHero(Hero.MainHero, 1);
                                ChangeRenown(5f);
                                Msg("You breathe the fire out of itself in one movement. It dies so completely that the smoke stops mid-column. The village stares. You tell them to check the kitchen floor for embers and ride on.", FireColor);
                            }
                            else
                            {
                                MobileParty.MainParty.RecentEventsMorale += 5f;
                                Msg("Your men form a line in under a minute. The innkeeper finds the well. The fire loses before it can find the thatch. Your men are wet and satisfied and slightly competitive about who threw the most water.", GoodColor);
                            }
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            MobileParty.MainParty.RecentEventsMorale -= 3f;
                            Msg("You clear the building first. The fire takes the kitchen and part of a storage room before it is stopped. Everyone who was inside is outside. The innkeeper will rebuild. Your men smell of smoke for two days.", GoodColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("You ride. Behind you the shouting continues, then eventually stops. You don't look back to determine which kind of stop it was.", BadColor);
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
                        "Gain Honor. Renown +5. Relation -5 with city faction."),
                    new InquiryElement("b", "Pay what they're demanding on her behalf.", null, true,
                        "Lose 200 gold. Gain Merciful."),
                    new InquiryElement("c", "Every city has its corner tolls. Keep moving.", null, true,
                        "Lose Honor."),
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
                        "Renown +5. Rich flavor exchange."),
                    new InquiryElement("b", "A brief answer and keep moving.", null, true,
                        "Flavor only."),
                    new InquiryElement("c", mage ? "Show him, briefly, what the fire sees when it looks at the ash." : "Tell him what you have seen with your own eyes.", null, true,
                        mage ? "Costs 1 day. Profound flavor." : "Honor +1. He will write this down."),
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
                                AgingSystem.AgeHero(Hero.MainHero, 1);
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

        // 89. The Wealthy Sick (mage-gated)
        private static void EC3_SickNoble(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✚  The Wealthy Sick",
                "A merchant family — three generations of money, none of it useful right now — approaches before you have put your horse in the stall. Their patriarch is dying. They have heard what you can do. They are not begging; they are negotiating. The sum they name is significant. They are very scared.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Agree to try — the fire can sometimes hold death back.", null, true,
                        "Costs 2 days. Gain 1000 gold. 50/50 outcome."),
                    new InquiryElement("b", "Refuse. Dying is not always something that should be stopped.", null, true,
                        "Gain Honor. Nothing else."),
                    new InquiryElement("c", "Take the payment and attempt nothing.", null, true,
                        "Gain 1000 gold. Lose Honor — badly."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgingSystem.AgeHero(Hero.MainHero, 2);
                            ChangeGold(1000);
                            if (_rng.Next(2) == 0)
                            {
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                Msg("The fire finds something to work with. His breathing steadies. You cannot say how long — days, weeks — but he is not dying this morning. The family's relief is very loud and very private. You leave feeling older than the arithmetic of it.", FireColor);
                            }
                            else
                            {
                                Msg("The fire reaches and finds nothing to hold. Whatever process is ending in him, it is further along than it looks. You return the coin. They try not to accept it back. You insist. You ride out lighter than you arrived.", DimColor);
                            }
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("\"There are things the fire should not be used to purchase,\" you tell them. They do not understand. You do not explain further. You take your horse and go.", GoodColor);
                            break;
                        case "c":
                            ChangeGold(1000);
                            ShiftTrait(DefaultTraits.Honor, -2);
                            Msg("You take the payment. You sit with the old man for an hour and do nothing. He dies the next morning. The family sends a message after. You do not open it.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 90–92 — LEAVE CITY (new batch)
        // ═══════════════════════════════════════════════════════════════════

        // 90. Broken Colors
        private static void LC3_DishonoredSoldier(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  Broken Colors",
                "Against the city wall, a man who was clearly a soldier — posture, hands, the particular stillness of someone trained to wait — is sleeping rough. The chevrons have been pulled from his jacket recently. He isn't asking for anything. He is simply there.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Give him coin and ask if he wants work.", null, true,
                        "Lose 200 gold. Gain Merciful. Morale +3."),
                    new InquiryElement("b", "Leave coin in the cup without stopping.", null, true,
                        "Lose 100 gold. Gain Merciful."),
                    new InquiryElement("c", "Walk past.", null, true,
                        "Nothing happens."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(-200);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            MobileParty.MainParty.RecentEventsMorale += 3f;
                            Msg("He straightens up slowly and looks at you. \"What happened?\" you ask. \"Wrong captain at the wrong time,\" he says. He falls in at the rear of the column. Your sergeant gives him a look. It is a respectful one.", GoodColor);
                            break;
                        case "b":
                            ChangeGold(-100);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("The coin lands in the cup. He opens one eye, looks at it, closes it again. He doesn't thank you. You don't need him to.", DimColor);
                            break;
                        case "c":
                            Msg("You walk past. He doesn't move.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 91. A Warning
        private static void LC3_SpyWarning(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  A Warning",
                "Half a mile from the city, a stranger passes your column going the other direction and presses a folded note into your hand without slowing. You open it. A name — one of your own men — and four words in a careful hand: 'reporting your movements east.'",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Investigate immediately and quietly.", null, true,
                        "50/50: confirmed and discharged, or clean and you owe an apology."),
                    new InquiryElement("b", "Watch him without acting. Let him reveal himself.", null, true,
                        "Gain Calculating. The watching begins."),
                    new InquiryElement("c", "Anonymous notes are a weapon too. Discard it.", null, true,
                        "Nothing happens."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (_rng.Next(2) == 0)
                            {
                                MobileParty.MainParty.RecentEventsMorale -= 5f;
                                Msg("The evidence is there once you look for it. Inconsistencies in his timing, gaps in his account of certain evenings. He doesn't deny it when confronted. He is discharged. The column is quieter after.", BadColor);
                            }
                            else
                            {
                                MobileParty.MainParty.RecentEventsMorale += 3f;
                                Msg("Nothing holds under examination. The man is clean. You tell him what was said and why you looked. He takes it with more grace than you deserve. Your men appreciate you telling him openly.", GoodColor);
                            }
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You fold the note and pocket it. You begin to watch in the way you have learned to watch — without looking like watching. The road ahead is a different shape now.", DimColor);
                            break;
                        case "c":
                            Msg("You tear the note and let the pieces go. It might be true, it might not be. Suspicion without evidence is its own kind of poison.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 92. The Mercenary Captain
        private static void LC3_MercenaryOffer(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  The Mercenary Captain",
                "A mercenary captain is at the city gate as you leave with her company of thirty behind her — experienced, well-equipped, moving in the same direction you are. She offers to ride with you at half her normal rate. She gives no explanation for the discount.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Accept the offered rate.", null, true,
                        "Lose 500 gold. Morale +8. Unknown quantity."),
                    new InquiryElement("b", "Accept at full rate — a clean arrangement.", null, true,
                        "Lose 900 gold. Morale +10. Reliable arrangement."),
                    new InquiryElement("c", "Decline. Half-rate mercenaries have half-reasons.", null, true,
                        "Nothing happens."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(-500);
                            MobileParty.MainParty.RecentEventsMorale += 8f;
                            Msg("They fall in. Competent, quiet, and visibly curious about what you are doing and why. The reason for the discount never becomes clear. That stays with you.", DimColor);
                            break;
                        case "b":
                            ChangeGold(-900);
                            MobileParty.MainParty.RecentEventsMorale += 10f;
                            Msg("Full rate, clean contract, no ambiguity. She looks slightly relieved when you name it. \"Good,\" she says. \"I prefer that.\" So do you.", GoodColor);
                            break;
                        case "c":
                            Msg("She nods as if she expected that answer and doesn't look offended. She turns back to her company and says something. They begin moving west instead.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 93–96 — AFTER FIELD BATTLE (new batch)
        // ═══════════════════════════════════════════════════════════════════

        // 93. The Banner Still Standing
        private static void EB2_StandardBearer()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  The Banner Still Standing",
                "Everyone else on this part of the field has fled or fallen. One man remains: the enemy standard bearer, the banner still upright, looking at you. He is not going to lower it. He is not going to run. He is going to stand there until something changes.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Honour it. He can keep the banner and go home.", null, true,
                        "Gain Honor and Merciful. Renown +10."),
                    new InquiryElement("b", "Demand he surrender the standard.", null, true,
                        "Standard captured. Gain Calculating."),
                    new InquiryElement("c", "Ride past. Let your men decide.", null, true,
                        "Nothing — but your men will decide."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ChangeRenown(10f);
                            Msg("\"Keep it,\" you say. \"Go home.\" He looks at you for a long moment. Then he lowers the banner — not in surrender, just to carry it easier — and walks east. Your men watch him go in silence.", GoodColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("He gives it up. He makes the decision cleanly, without a last stand. He may be smarter than you gave him credit for.", DimColor);
                            break;
                        case "c":
                            Msg("You ride past. Behind you, you hear the exchange your men have with him. He is not badly treated. The banner does not survive the conversation.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 94. What the Wagons Carried
        private static void EB2_EnemySupplies()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  What the Wagons Carried",
                "The captured supply wagons contain things that don't belong to a military campaign: children's shoes, household tools, grain sacks stamped with village headmen's seals. Someone stripped villages to feed this army. The soldiers who drove these wagons are among your prisoners.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Return what can be returned. Send word to the villages.", null, true,
                        "Gain Merciful. Renown +5. Morale +5."),
                    new InquiryElement("b", "Divide it among your men — spoils are spoils.", null, true,
                        "Morale +10. Nothing moral."),
                    new InquiryElement("c", "Burn it. None of this should have been taken.", null, true,
                        "Gain Honor. Morale -5 — your men are hungry."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ChangeRenown(5f);
                            MobileParty.MainParty.RecentEventsMorale += 5f;
                            Msg("The wagons are catalogued. Riders are sent with what can be identified. What cannot be traced is distributed to the nearest villages. Your men do this work without complaint, which surprises you until you realise it doesn't.", GoodColor);
                            break;
                        case "b":
                            MobileParty.MainParty.RecentEventsMorale += 10f;
                            Msg("The grain is divided, the tools shared out, the shoes kept by whoever fits them best. Your men eat well tonight. Somewhere, families do not.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            MobileParty.MainParty.RecentEventsMorale -= 5f;
                            Msg("You put the torch to it yourself. Your men watch without speaking. They are hungry and they know why this is happening and they don't complain about it. That says something. You don't know exactly what.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 95. What He Did
        private static void EB2_HeroInParty()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  What He Did",
                "A soldier comes to you privately after the battle — not a troublemaker, someone you know and trust. He reports that during the fighting he killed a man who had already surrendered. He is not minimising it. He came forward himself. He is waiting for your judgment.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Punish him formally.", null, true,
                        "Gain Honor. Morale -5 — but the party respects the consistency."),
                    new InquiryElement("b", "Acknowledge it and give him extra duty — one error does not end a career.", null, true,
                        "Nothing mechanical. A private resolution."),
                    new InquiryElement("c", "Discharge him.", null, true,
                        "Gain Honor. Morale -3."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            MobileParty.MainParty.RecentEventsMorale -= 5f;
                            Msg("The punishment is announced. Some of your men are uncomfortable — he is popular. All of them understand why it is happening. Nobody argues with it. That is the best outcome you could have hoped for.", GoodColor);
                            break;
                        case "b":
                            Msg("You tell him what you think of what he did and what his extra duty will be. He does not argue. He does not thank you either — this is not the kind of mercy that needs thanking. He goes back to his post.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            MobileParty.MainParty.RecentEventsMorale -= 3f;
                            Msg("He is discharged. He goes quietly, which is the only thing left to his credit. Your men are subdued. They understand the rule now in a way that abstracts do not produce.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 96. What the Fire Shows (mage-gated)
        private static void EB2_FireReveals()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  What the Fire Shows",
                "Standing on the battlefield after dark, your fire does something it rarely does. Not a vision — a sensation: layered echoes of the last moments of the men who died here, pressed against your awareness like heat through a wall. It did not ask permission.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Stay with it. Witness fully.", null, true,
                        "Costs 1 day. Gain Merciful. Profound flavor."),
                    new InquiryElement("b", "Push it back. You cannot carry this weight tonight.", null, true,
                        "Flavor only. The echoes recede."),
                    new InquiryElement("c", "Let it run through and out — transform it into something.", null, true,
                        "Costs 1 day. Renown +5. You light a proper pyre."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You let it happen. Dozens of final seconds, layered, none of them clean. Enemy and ally blurred together in the last moment into something undifferentiated. You stand there until it passes. The fire is quieter after. So are you.", FireColor);
                            break;
                        case "b":
                            Msg("You close it down. The echoes compress and go dark. The battlefield is just a field with bodies on it again, which is what it was before. That is not nothing. Sometimes that is exactly right.", DimColor);
                            break;
                        case "c":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
                            ChangeRenown(5f);
                            Msg("You take what the fire is giving you and let it out through your hands into the wood you pile in the dark. The pyre lights gold. Your men come out of their tents without being called. Nobody speaks. Everyone stays until it burns down.", FireColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 97–98 — AFTER SIEGE (new batch)
        // ═══════════════════════════════════════════════════════════════════

        // 97. The Healers
        private static void ES2_HospitalWard()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✚  The Healers",
                "In the lowest level of the keep, behind a door your men nearly missed, a hospital ward — families of garrison soldiers, a few merchants, an old woman who simply never left. Two exhausted physicians are still working, and they do not stop when your men enter.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Post guards and ensure the healers can work undisturbed.", null, true,
                        "Gain Merciful. Morale -5 — those men could be looting."),
                    new InquiryElement("b", "Send your own surgeons to relieve them.", null, true,
                        "Gain Merciful. Morale +5 — soldiers respect good healers."),
                    new InquiryElement("c", "The ward is needed for your wounded — have them cleared out.", null, true,
                        "Lose Honor and Mercy."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            MobileParty.MainParty.RecentEventsMorale -= 5f;
                            Msg("Guards posted, orders given. The physicians keep working without acknowledging the change. The old woman in the corner opens her eyes, looks at the guards, and closes them again with the expression of someone who has updated their estimate of you.", GoodColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            MobileParty.MainParty.RecentEventsMorale += 5f;
                            Msg("Your surgeons go in. The two exhausted physicians step back and watch for exactly long enough to confirm your men know what they're doing. Then they sit down, back against the wall, and sleep where they are sitting.", GoodColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            ShiftTrait(DefaultTraits.Mercy, -1);
                            Msg("The ward is cleared. The physicians go last, still carrying what they can carry. One of them looks at you as she passes. That look will remain accessible to you at inconvenient moments for some time.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 98. The Informant
        private static void ES2_SpyInCamp()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Informant",
                "After the siege, your intelligence man presents you with a name and evidence: someone inside your camp was passing information to the defenders throughout. He has been with you for four months. The evidence is solid. He is standing outside your tent right now, not knowing why he was summoned.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Public punishment. The party needs to see this handled.", null, true,
                        "Gain Honor. Morale mixed — respect and unease in equal measure."),
                    new InquiryElement("b", "Quiet discharge. You believe the evidence; you don't need the spectacle.", null, true,
                        "Gain Honor. Practical."),
                    new InquiryElement("c", "Use him. Feed false information through him to the next target.", null, true,
                        "Gain Calculating. Moral complexity. He may not cooperate."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            MobileParty.MainParty.RecentEventsMorale += 2f;
                            Msg("The announcement is made. The punishment follows. Your men are quiet in the specific way of people who needed to see this happen and are not entirely comfortable that they did.", DimColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You tell him what you know and why he is leaving. He does not confess, does not deny, does not plead. He goes. Three of your men notice the absence and ask no questions. That is its own kind of answer.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            if (_rng.Next(2) == 0)
                                Msg("He cooperates — whether from calculation or fear, you cannot tell. The false information passes east. What it sets in motion will not be visible for weeks.", DarkColor);
                            else
                                Msg("He refuses, quietly and completely. \"Do what you need to,\" he says. \"But not that.\" You discharge him. You respect the refusal more than you expected to.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 99–100 — AFTER RAID (new batch)
        // ═══════════════════════════════════════════════════════════════════

        // 99. The Negotiation
        private static void ER2_ElderNegotiates()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  The Negotiation",
                "As your men form up at the raid's conclusion, the village elder appears from a doorway with a locked box. He names a sum — everything the village has saved — and asks simply if it is enough. He is not afraid. He is experienced. He has done this before.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Accept less than he offered and stand down.", null, true,
                        "Gain Honor and Merciful. Less gold than offered."),
                    new InquiryElement("b", "Accept his full offer.", null, true,
                        "Gain gold. Honor cost for taking a village's savings."),
                    new InquiryElement("c", "Decline and spare the village anyway.", null, true,
                        "Gain Honor and Merciful. Morale -5 — your men were ready."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(300);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You take a third of what he offered and tell him to keep the rest. He closes the box. He looks at you with a careful, practiced expression that might be gratitude or might be the face a man makes when he decides to trust something just enough.", GoodColor);
                            break;
                        case "b":
                            ChangeGold(700);
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("You take the box. He steps back. He was prepared for this. That is the worst part of it — that he was prepared.", BadColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            MobileParty.MainParty.RecentEventsMorale -= 5f;
                            Msg("\"Put it away,\" you say. You wave your men back. He closes the box slowly, not quite believing it. Your men are frustrated. You accept that. Some costs are worth having.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 100. What They Left
        private static void ER2_AshenEvidence()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  What They Left",
                "In the village, your men find it: grain stored in marked sacks with Ashen sigils, a hidden correspondence in a dialect that is not quite any language, a room that has been cold for the wrong reasons. Whether this village was collaborating willingly or supplying the Ashen under compulsion, you cannot tell from what you have found.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Burn the evidence and spare the village further consequence.", null, true,
                        "Gain Merciful. Honor +1 — compulsion is not collaboration."),
                    new InquiryElement("b", "Report everything to the nearest lord and let it be investigated.", null, true,
                        "Renown +5. Relation +5. The village's fate is out of your hands."),
                    new InquiryElement("c", "Keep the information. A village with Ashen ties is a useful monitoring point.", null, true,
                        "Gain Calculating. A morally complicated asset."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("The sacks burn. The correspondence burns. The cold room stays cold but its contents are gone. You ride with the knowledge that whatever was happening here will now have to begin again somewhere else, and that is worth something.", GoodColor);
                            break;
                        case "b":
                            ChangeRenown(5f);
                            ChangeRelWithRandomLord(5);
                            Msg("The report is thorough. You include everything you found and nothing you speculated. What happens to the village after the investigation is beyond your sight. That is what jurisdiction means.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You leave no sign that you found it. A village that feeds information to the Ashen can be encouraged to feed different information. Whether that is cleverness or cruelty depends on how it is used, and that has not been determined yet.", DarkColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 101–104 — ENTER VILLAGE (fourth batch)
        // ═══════════════════════════════════════════════════════════════════

        // 101. The Empty Houses
        private static void EV4_EmptyVillage(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Empty Houses",
                "A third of the village is dark. Not abandoned by choice — hearth-fires still warm, meals half-eaten, tools left where they fell. Whatever made people leave, they left fast and they left last night. The remaining villagers are watching you from behind shutters.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Find out what happened before you ride on.", null, true,
                        "Gain Merciful. Uncover the cause — Ashen or something else."),
                    new InquiryElement("b", "Speak to the headman directly.", null, true,
                        "Relation +5 with settlement owner. Flavor message."),
                    new InquiryElement("c", "Ride through and say nothing.", null, true,
                        "Nothing happens."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            string[] causes = {
                                "A grey-cloaked party came through at dusk. Nobody could say how many. They counted the houses and left without taking anything. That was worse than if they had taken something.",
                                "Three families saw something at the eastern tree line two nights ago. They described it as fog that didn't move with the wind. The Ashen have been this far south before, but not quietly.",
                                "The miller's well started running cold and wrong-smelling four days ago. Families with children left first. There is no disease yet. That word 'yet' is doing considerable work in the village's thinking.",
                            };
                            Msg(causes[_rng.Next(causes.Length)], AshenColor);
                            break;
                        case "b":
                            ChangeRelWithOwner(s, 5);
                            Msg("The headman tells you in three sentences what took the families away. His face is the face of a man who has been deciding all morning whether to tell someone official. You are the first official thing to ride through.", DimColor);
                            break;
                        case "c":
                            Msg("You ride through. The shutters do not open.", DimColor);
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
                        "Gain Merciful. Renown +5."),
                    new InquiryElement("b", "Pay the value of the grain yourself and end it.", null, true,
                        "Lose 200 gold. Gain Merciful and Honor."),
                    new InquiryElement("c", "Let it proceed. Village justice is village justice.", null, true,
                        "Nothing happens."),
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
                        "Gain Merciful. The fire recognises her. She will remember this."),
                    new InquiryElement("b", "Meet the mother's eyes and give a small nod.", null, true,
                        "Gain Calculating. The mother will watch for it now."),
                    new InquiryElement("c", "Ride on. This is not yours to name.", null, true,
                        "Nothing happens. But the fire knows."),
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

        // 104. The Road South
        private static void EV4_FleeingFamily(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Road South",
                "A family with everything they own on one cart is moving south. Fast, for a loaded cart. Their village is three days north. They will not say what they saw. They don't need to — the direction alone carries the answer, and the children are not asking where they are going.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Give them coin and tell them what roads south are safe.", null, true,
                        "Lose 300 gold. Gain Merciful. Learn something about what's north."),
                    new InquiryElement("b", "Send a rider north to confirm what they fled.", null, true,
                        "Relation +5 with nearby lord. Ashen intel."),
                    new InquiryElement("c", "Let them go. They are heading away from the problem.", null, true,
                        "Nothing happens."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(-300);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("They take the coin. The father speaks then — quickly, as if he has been saving this for someone who might use it: a patrol of grey-cloaked figures, no fire in their camp, moving south along the old eastern road. Three days ago. Moving the same direction the family is moving.", AshenColor);
                            break;
                        case "b":
                            ChangeRelWithRandomLord(5);
                            Msg("Your rider returns by evening: signs of Ashen activity at the village. Nothing burning. The kind of visit that is more unsettling than fire — a taking of stock, a counting of what is there. The Ashen are noting things.", AshenColor);
                            break;
                        case "c":
                            Msg("The cart passes. The smallest child stares at you over the tailboard until the road bends.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 105–108 — LEAVE VILLAGE (fourth batch)
        // ═══════════════════════════════════════════════════════════════════

        // 105. The Storyteller
        private static void LV4_StoryTeller(Settlement s)
        {
            bool mage = MageKnowledge.IsMage;
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Storyteller",
                "An old woman has been at the inn for three days, trading stories for meals and a corner to sleep in. The innkeeper says she knows things about the first Ashen wars that aren't in any written record — she heard them from someone who heard them from someone who was there. She sees you saddling your horse and raises an eyebrow.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", mage ? "Stay the evening. The fire wants to hear this." : "Stay the evening. Pay for her meal and yours.", null, true,
                        mage ? "Costs 1 day. Deep lore. Renown +5." : "Lose 200 gold. Renown +5. Rich lore flavor."),
                    new InquiryElement("b", "Buy her the meal but ride — you remember what you hear.", null, true,
                        "Lose 100 gold. Short flavor."),
                    new InquiryElement("c", "No time for old stories.", null, true,
                        "Nothing happens."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (mage) AgingSystem.AgeHero(Hero.MainHero, 1);
                            else ChangeGold(-200);
                            ChangeRenown(5f);
                            string[] stories = {
                                "The first Ashen were not conquered. They chose the cold, and they chose it knowingly, because the alternative was watching everything they loved die around them while they did not. She says this without judgment. She says it like weather.",
                                "The fire-lords of the old age did not age slowly — they aged in bursts, after great workings, and then were still for years. What ended them was not age. It was the moment they stopped being afraid of it.",
                                "There was a name for what you carry, in the old language. It translates badly. The closest is 'the fire that knows it is fire.' Most people never have that. It is the difference between a torch and a hearth.",
                            };
                            Msg(stories[_rng.Next(stories.Length)], FireColor);
                            break;
                        case "b":
                            ChangeGold(-100);
                            Msg("She tells you one thing quickly, between her first cup and her second: \"The Ashen do not want your land. They want the warmth in you. Everything else is means.\" She does not explain further.", AshenColor);
                            break;
                        case "c":
                            Msg("She watches you ride out. She will still be here tomorrow for someone else.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 106. The Last Request
        private static void LV4_DyingTraveler(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "☠  The Last Request",
                "A man by the road has been robbed and wounded — not by battle. He is not going to reach the next village. He presses something into your hand — a sealed letter, a ring, a name — and asks one thing: make sure it reaches them. He is not panicking. He is very focused.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Accept the task.", null, true,
                        "Gain Honor and Merciful. The obligation is real."),
                    new InquiryElement("b", "Sit with him, but explain you cannot be his messenger.", null, true,
                        "Gain Merciful. He accepts it."),
                    new InquiryElement("c", "Take what he gives and keep it.", null, true,
                        "Gain 200 gold (if it's worth anything). Lose Honor."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You take it. He exhales. He explains who and where — a city two days east, a name, a street. He dies before evening. The thing he gave you weighs nothing and costs nothing to carry. Getting it there is a different matter.", GoodColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You stay until the breathing slows. He spends the time making the same calculation he made when he first saw you and arriving at the same answer. You sit with him. That is not nothing.", GoodColor);
                            break;
                        case "c":
                            ChangeGold(200);
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("It is worth something. The ring is silver. He watches you pocket it with the expression of a man who has just revised his estimate of the world significantly downward.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 107. The Escaped Man
        private static void LV4_EscapedPrisoner(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Escaped Man",
                "A man with raw wrists where manacles have recently been removed crouches in the shadow of your horse and asks very quietly that you not acknowledge him to the guard that just passed. He says he was held for a debt, not a crime. He might be telling the truth.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Help him disappear — coin and a direction.", null, true,
                        "Lose 200 gold. Gain Merciful."),
                    new InquiryElement("b", "Walk away without acknowledging him — he takes his chances.", null, true,
                        "Nothing. He is on his own."),
                    new InquiryElement("c", "Ask the guard what he was held for before deciding.", null, true,
                        "50/50: debt or crime. You decide accordingly."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(-200);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("Coin and north. He goes without looking back. Whether his story was true or not, he is gone now and the guard has lost the trail. You ride on carrying the ambiguity.", GoodColor);
                            break;
                        case "b":
                            Msg("You keep walking. He stays very still. The guard passes without finding him. What happens after you leave is not your knowledge.", DimColor);
                            break;
                        case "c":
                            if (_rng.Next(2) == 0)
                            {
                                // Debt — let him go
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                Msg("Debt, the guard confirms — unpaid, not disputed. You step between them and tell the guard the debt will be settled by the end of the week. It is a lie you say with the confidence required to make it believable. The man is gone before the guard finishes blinking.", GoodColor);
                            }
                            else
                            {
                                // Crime — hand him back
                                ShiftTrait(DefaultTraits.Honor, 1);
                                Msg("Assault on a merchant's guard, the guard says. Three witnesses. You step aside. He runs and is caught. He looks at you as they retake him. You look back. This is what the answer to that question costs.", DimColor);
                            }
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 108. The Deserter
        private static void LV4_DeserterSoldier(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  The Deserter",
                "You recognise the posture before you recognise the face — a former enemy soldier, living as a village craftsman. He was at the battle of the eastern crossing; you remember his unit's colors. He has seen you see him. He has gone very still over his work, and he is waiting.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Let him be. The war passed him and he chose a different road.", null, true,
                        "Gain Honor and Merciful."),
                    new InquiryElement("b", "Report his whereabouts to the lord he deserted from.", null, true,
                        "Relation +5. Nothing moral."),
                    new InquiryElement("c", "Stop and ask him why he stopped fighting.", null, true,
                        "Flavor exchange. Nothing mechanical."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You ride past without slowing. He does not move until your party has fully cleared the village. Then he goes back to work. You will not know what he makes of this.", GoodColor);
                            break;
                        case "b":
                            ChangeRelWithRandomLord(5);
                            Msg("The report is sent. A patrol will come. Whether the man stays to meet it or reads the wind and leaves is not something you will see.", DimColor);
                            break;
                        case "c":
                            Msg("He looks at you for a long moment. Then: \"I had a daughter born the week I left. I had never seen her.\" He goes back to his work. You ride on with nothing to say to that and nothing to do with it.", DimColor);
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
                "A group of prisoners is being auctioned in the city square into indentured service — several years' labour for debts that may or may not be documented. Most of them are wrong for criminals: clothing, bearing. One of them has fire-worker's calluses and is tracking everything with the particular attention of someone who knows what is happening to them.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Buy their freedom. All of them.", null, true,
                        "Lose 800 gold. Gain Merciful and Honor."),
                    new InquiryElement("b", "Report the auction's legality to the city lord.", null, true,
                        "Relation +5. The process begins. It will take time."),
                    new InquiryElement("c", "Ride past.", null, true,
                        "Lose Honor."),
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
                        "Renown +5. Gain Honor. His income ends today."),
                    new InquiryElement("b", "Let him work. People want to believe in fire — even the wrong kind.", null, true,
                        "Gain Merciful. Nothing mechanical."),
                    new InquiryElement("c", "Approach him privately. Offer him a real lesson instead.", null, true,
                        "Costs 1 day. The tricks stop. He learns something true."),
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
                            AgingSystem.AgeHero(Hero.MainHero, 1);
                            Msg("He sees your eyes and knows immediately. You find a quiet street. You show him one real thing — very small, not dangerous, but unmistakably true. He stares at his hands for a long time afterward. When he looks up his expression has changed entirely. He will not perform that act again. You don't know what he will do instead.", FireColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 111. Your Name on a Wall
        private static void EC4_WantedPoster(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  Your Name on a Wall",
                "Near the market gate, a notice has been posted with a description of a 'fire-cursed lord causing disruption across the eastern roads' and a bounty attached. The description is vague but unmistakably you. The city guard has been walking past it all morning without looking twice.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Tear it down quietly and ride on.", null, true,
                        "Gain Calculating. Done quietly, evidence kept."),
                    new InquiryElement("b", "Report it to the city lord — someone posted this.", null, true,
                        "Renown +5. Relation +5. The lord is alarmed on your behalf."),
                    new InquiryElement("c", "Leave it. A bounty posted by men who can't catch you.", null, true,
                        "Nothing. Someone will note you left it up."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You take it down cleanly. Nobody sees. You fold it and keep it — if the author is identified later, this is evidence. The wall is blank. The guard walks past.", DimColor);
                            break;
                        case "b":
                            ChangeRenown(5f);
                            ChangeRelWithOwner(s, 5);
                            Msg("The lord reads it and goes slightly pale. He apologises for the insult to your standing while being visibly more interested in who posted it than in your feelings about it. An investigation begins. You leave with a more thorough understanding of who does not wish you well in this city.", DimColor);
                            break;
                        case "c":
                            Msg("You leave it. A passing merchant reads it, looks at you, and keeps walking. The description is bad enough that most people can't confirm it. But some can.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 112–114 — LEAVE CITY (fourth batch)
        // ═══════════════════════════════════════════════════════════════════

        // 112. The Running Girl
        private static void LC4_RunawayServant(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Running Girl",
                "A young woman falls in beside your horse as you leave, keeping your party between herself and the gate. She is walking at exactly the pace required to not seem to be running. She says she is not a runaway. The bruising on her wrists suggests someone else has been making that determination for her.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Let her walk with your party to the next town.", null, true,
                        "Gain Merciful and Honor. Morale +3."),
                    new InquiryElement("b", "Give her coin and point south — your column draws eyes.", null, true,
                        "Lose 200 gold. Gain Merciful."),
                    new InquiryElement("c", "Stop and ask what is actually happening.", null, true,
                        "50/50: the full story, which changes what you decide."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            MobileParty.MainParty.RecentEventsMorale += 3f;
                            Msg("She walks with the party and says very little. One of your soldiers' wives has the look of someone who understands precisely what has happened and deals with it practically and without ceremony. By the next town, the girl has a different name and a different story. Your party moves on.", GoodColor);
                            break;
                        case "b":
                            ChangeGold(-200);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("She takes the coin and south with equal readiness, suggesting she had already planned this part. She disappears into a side street before your party has cleared the gate. Whatever she's running toward, she had it figured out.", GoodColor);
                            break;
                        case "c":
                            if (_rng.Next(2) == 0)
                            {
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                ShiftTrait(DefaultTraits.Honor, 1);
                                Msg("She tells you the truth in two minutes. Her employer's son. A debt her family doesn't know about. A locked room. You ride back to the gate with her and have a brief conversation with the city lord's secretary that leaves no room for interpretation. She is free by the time you leave for the second time.", GoodColor);
                            }
                            else
                            {
                                Msg("She tells you a story that has too many details and not enough coherence. She may be lying or she may be in genuine shock. You cannot tell which. You give her the coin anyway and let her decide the rest.", DimColor);
                            }
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 113. The Old Debt
        private static void LC4_OldDebt(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚖  The Old Debt",
                "A man catches your horse at the gate and names a sum and a situation from three years ago — a deal gone sideways, a loan with no written record, your name attached. The sum is not ruinous. His expression is careful in the way of someone who has rehearsed this.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Pay it. Three years is long enough to lose track.", null, true,
                        "Lose 400 gold. Gain Honor — you may have actually owed it."),
                    new InquiryElement("b", "Deny it and ride on. If it were real, he'd have come sooner.", null, true,
                        "Nothing. 40% chance it was real and you'll never know."),
                    new InquiryElement("c", "Ask for documentation.", null, true,
                        "50/50: he has something, or he folds."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(-400);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You pay it without argument. He takes the coin with the expression of someone who was not entirely certain this would work. Whether the debt was real or invented, you leave with a clear conscience, which is worth something.", GoldColor);
                            break;
                        case "b":
                            Msg("You deny it and ride. He does not follow. This could mean he was lying. It could mean he expected this and is filing it away. The road is long enough that the uncertainty eventually becomes background noise.", DimColor);
                            break;
                        case "c":
                            if (_rng.Next(2) == 0)
                            {
                                ChangeGold(-400);
                                ShiftTrait(DefaultTraits.Honor, 1);
                                Msg("He produces a letter in a handwriting you recognise. The amount is there. You pay it. The man leaves with the slightly surprised air of someone whose gamble worked completely.", GoldColor);
                            }
                            else
                            {
                                Msg("He hesitates. Then apologises. Then walks away quickly. You watch him go and decide not to pursue the question of whether he had the wrong person or simply no documentation.", DimColor);
                            }
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
                        "Gain Honor. They disappear before you reach them. But they know you saw."),
                    new InquiryElement("b", "Pretend not to notice and ride on.", null, true,
                        "They follow for a mile, then stop. The documentation continues elsewhere."),
                    new InquiryElement("c", "Have a message passed: you are not their enemy.", null, true,
                        "50/50: quiet acknowledgement or no response. The gesture is made."),
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
        // EVENTS 115–117 — AFTER FIELD BATTLE (fourth batch)
        // ═══════════════════════════════════════════════════════════════════

        // 115. Not Enough
        private static void EB3_TriageDecision()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✚  Not Enough",
                "Your surgeon comes to you with the numbers: the serious wounded outnumber the supplies available for serious care by more than the margin can absorb. By the time more supplies arrive, some of these men will not benefit from them. He is asking for guidance on how to allocate what is here.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Those most likely to recover come first.", null, true,
                        "Gain Merciful. Morale -3 — those passed over notice."),
                    new InquiryElement("b", "Those who have served longest come first.", null, true,
                        "Gain Honor. Morale +5 — veterans are steadied."),
                    new InquiryElement("c", "Give the surgeon the authority. This decision is his to make.", null, true,
                        "Gain Honor. Morale +3. The decision is correct and costs you nothing but the distance of it."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            MobileParty.MainParty.RecentEventsMorale -= 3f;
                            Msg("The surgeon works by likelihood. Some men watch others receive care that does not come to them. They understand the logic. Understanding the logic does not make it easier to lie on a stretcher in the dark and understand the logic.", DimColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            MobileParty.MainParty.RecentEventsMorale += 5f;
                            Msg("The veterans are treated first. The younger men see it and accept it — some with relief, some with the look of people calculating how long they need to survive in order to earn that precedence themselves.", GoodColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            MobileParty.MainParty.RecentEventsMorale += 3f;
                            Msg("The surgeon nods once — he already knew the right answer, he just needed someone with authority to say he could use it. He works through the night. In the morning he reports the number who survived. He does not list the names of those who did not.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 116. A Name Worth Something
        private static void EB3_PrisonerNobleClaim()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚜  A Name Worth Something",
                "Listing the prisoners with your sergeant, a man near the end of the line gives his name quietly. It is a minor noble family — not great, but real. He is watching your face to see if you place it. He has clearly done this before and is evaluating whether you are the sort of person who recognises names.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Ransom him through proper channels.", null, true,
                        "Gain Honor. Ransom payment arrives eventually."),
                    new InquiryElement("b", "Keep him as leverage — a name is more useful unspent.", null, true,
                        "Gain Calculating. Honor cost."),
                    new InquiryElement("c", "Release him. You don't deal in names.", null, true,
                        "Gain Honor. He is surprised. That surprise is its own reward."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ChangeGold(600);
                            Msg("The ransom is negotiated and received in the standard way. He leaves having been treated exactly as well as the arrangement required. He will speak well of you, within the limits of what speaking well of a captor permits.", GoldColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("He is kept separately and treated correctly. He knows exactly what is happening and accepts it with the patience of someone who has had time to think about this possibility. The name remains unspent. That is a resource with a shelf life.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("\"Go,\" you say. He blinks. He was prepared for negotiation, for leverage, for the long road of ransoming. He was not prepared for this. He goes before you can change your mind. He will tell the story of this for years and never be entirely sure he has it right.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 117. Two Miles
        private static void EB3_TheyCarriedHim()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  Two Miles",
                "Your sergeant reports something nobody mentioned during the battle: two of your soldiers carried a third — gut-wound, unable to walk — for two miles during a contested retreat, taking turns, under fire. Nobody ordered it. The man lived. The two of them are at camp eating as if nothing happened.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Commend them publicly — this is what your party is.", null, true,
                        "Morale +10. Renown +5. Gain Honor."),
                    new InquiryElement("b", "Tell them personally and quietly — not everything needs ceremony.", null, true,
                        "Morale +6. Gain Honor. They appreciate the privacy."),
                    new InquiryElement("c", "Say nothing. They don't need you to name it.", null, true,
                        "Morale +3. Honor +1. They already know what they did."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            MobileParty.MainParty.RecentEventsMorale += 10f;
                            ChangeRenown(5f);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You call it out in front of everyone. The two men go red and eat their dinner faster. The rest of the camp is louder than it was. The man they carried looks at the fire for a long time without speaking.", GoodColor);
                            break;
                        case "b":
                            MobileParty.MainParty.RecentEventsMorale += 6f;
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You find them separately, tell them what you saw and what you think of it, and leave them to their meal. One of them nods. The other says \"he would have done it for us.\" That is the entire conversation.", GoodColor);
                            break;
                        case "c":
                            MobileParty.MainParty.RecentEventsMorale += 3f;
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("Nothing is said. The camp feels the weight of what happened without it needing to be named. The two men eat. The man they carried passes them his bread ration. No explanation is given or requested.", GoodColor);
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
                        "Lose 500 gold. Gain Merciful. Morale -5 — your men give up their own supplies."),
                    new InquiryElement("b", "Seal the well and send for specialist help from outside the city.", null, true,
                        "Morale neutral. Some civilians will suffer before help arrives."),
                    new InquiryElement("c", mage ? "There may be a way to use the fire to purify the water." : "Requisition all available stocks from the city's merchants.", null, true,
                        mage ? "Costs 2 days. Gain Merciful. The fire can sometimes clean." : "Lose 400 gold. Partial solution."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(-500);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            MobileParty.MainParty.RecentEventsMorale -= 5f;
                            Msg("Everything is committed. Your men give up their water rations without being asked — not all of them, but enough that the ones who don't quietly match the ones who do by the second hour. The city knows what your party did here. It will know for a long time.", GoodColor);
                            break;
                        case "b":
                            Msg("The well is sealed and sealed clearly. The message goes out for help. It will take two days to arrive. In those two days, people who drank this morning will feel it. Some of them will be children. You make the practical decision and carry the practical decision.", DimColor);
                            break;
                        case "c":
                            if (mage)
                            {
                                AgingSystem.AgeHero(Hero.MainHero, 2);
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

        // 119. The Kept One
        private static void ES3_LongPrisoner()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚒  The Kept One",
                "In the deepest part of the dungeon, behind a door sealed separately from the others, a man who was there before you besieged the place. Two years, he says, when he can speak. He knows why he was kept rather than killed — he knows something the previous lord did not want spoken. He is offering it to you.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Hear everything he has to say.", null, true,
                        "Renown +5. Political and Ashen intel."),
                    new InquiryElement("b", "Release him, give him coin, and ask nothing.", null, true,
                        "Gain Honor and Merciful."),
                    new InquiryElement("c", "Ask why he was kept before deciding anything.", null, true,
                        "Flavor — then choose between the first two."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeRenown(5f);
                            string[] prisonerKnowledge = {
                                "The previous lord was in correspondence with the Ashen. Not the Ashen kingdom — individual Ashen lords. The correspondence covered trade in something the lord called 'cold-kept goods.' The prisoner was the courier who understood what he was carrying.",
                                "The lord's treasurer was skimming from the crown levy — had been for six years. The amounts are in a ledger the prisoner memorised, because memorising it was the only insurance against being killed for knowing it.",
                                "Three neighbouring lords have a private agreement that was not disclosed to the king. The prisoner was the scrivener who wrote the original document and was kept to prevent him from taking it elsewhere.",
                            };
                            Msg(prisonerKnowledge[_rng.Next(prisonerKnowledge.Length)], DimColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You have him fed, give him the coin, and open the outer gate. He stands in the light for a long moment, blinking. He does not look back at the keep. He walks north at the pace of someone who has thought very carefully about where he is going.", GoodColor);
                            break;
                        case "c":
                            Msg("He tells you in two sentences. The reason is political, old, and specific. You think for a moment. Then you make one of the first two choices — and it is a different choice than it would have been before he spoke.", DimColor);
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
                        "Lose 300 gold. Gain Merciful. The weight shifts, slightly."),
                    new InquiryElement("b", "Point them toward the nearest intact village and ride on.", null, true,
                        "Gain Merciful. Practical."),
                    new InquiryElement("c", "Say nothing and leave.", null, true,
                        "Lose Honor."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(-300);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You have food and water left at the cellar entrance, coin pressed into the grandfather's hands. He holds it and looks at you. You rode in this morning and took what was here. You rode out this afternoon and left what was needed. Both of those things are true at once. He knows it. So do you.", DimColor);
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

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 121–130 — FIFTH BATCH
        // ═══════════════════════════════════════════════════════════════════

        // 121. The Wolves
        private static void EV5_WolvesCircling(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✿  The Wolves",
                "A pack has been circling the village since last night — driven south by the cold or by something further north that displaced them. One child went to the stream at dawn and hasn't returned. The men have formed a search party, but they're going into the treeline against something faster than they are.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Lead the search yourself.", null, true,
                        "Gain Merciful. Renown +5. 50/50 outcome."),
                    new InquiryElement("b", "Send your men with your party's weapons and expertise.", null, true,
                        "Gain Merciful. Your men handle it."),
                    new InquiryElement("c", "Ride on — villages have dealt with wolves since before there were villages.", null, true,
                        "Lose Honor."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ChangeRenown(5f);
                            if (_rng.Next(2) == 0)
                                Msg("The child is found in a hollow oak, frightened but whole. The wolves retreated before the party reached them — something about your column's size and noise was enough. The village will tell this story for years. You will be taller in each telling.", GoodColor);
                            else
                                Msg("The child is found, but not unharmed. The wolves had time. Your party drives them north and the child lives. What that means for the family is longer than this road. You ride on carrying the specific weight of almost-enough.", DimColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("Your men go in efficiently. The wolves break off when they meet something that knows how to move in a treeline. The child is found cold but breathing. Your men come back tasting the particular satisfaction of a thing done for no reward but the doing.", GoodColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("You ride past the treeline. Behind you the search party disappears into the trees. You don't hear how it ends.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 122. The Ford (mage-gated)
        private static void EV5_FrozenFord(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✿  The Ford",
                "The river ford near the village has frozen solid. The wrong season for it, the wrong temperature by ten degrees. The villagers are staring at it with an expression that sits between grateful and afraid. You know the exact moment it froze: last night, when you were cold and tired and thinking about the road ahead.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Check the ice and tell them it's safe to cross.", null, true,
                        "Gain Merciful. You made it; you take responsibility for it."),
                    new InquiryElement("b", "Say nothing. A frozen ford is a frozen ford.", null, true,
                        "Nothing. The fire doesn't apologise."),
                    new InquiryElement("c", "Melt it again quietly before anyone asks questions.", null, true,
                        "Costs 1 day. Back to normal. The village will wonder for years."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You test the ice thoroughly and tell them it will hold a cart. The headman squints at you. He is doing arithmetic in his head about the season and the temperature and your arrival. He does not say what he concludes. He thanks you and sends the first cart across.", DimColor);
                            break;
                        case "b":
                            Msg("You ride past the staring villagers and across the ford yourself, first, without comment. Your horse's hooves ring on the ice. The village watches. The fire doesn't explain itself and neither do you.", FireColor);
                            break;
                        case "c":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
                            Msg("Before the village wakes fully, you unseal it. The ice cracks and thins and is gone by the time the first villager reaches the bank. They find running water and muddy tracks leading away. You are already on the road. They will talk about it as a dream.", FireColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 123. The Fever
        private static void LV5_TroopFever(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✚  The Fever",
                "The column is ready to move when your sergeant pulls you aside: one of your men collapsed in the inn stable this morning. Not wounded. Fever. He is not contagious — the surgeon thinks — but he cannot sit a horse, and the way he looks at the ceiling suggests he is not going to be able to for several days.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Leave him with coin to recover and rejoin you later.", null, true,
                        "Lose 200 gold. Gain Merciful. He will catch up."),
                    new InquiryElement("b", "Delay until he can ride.", null, true,
                        "Morale +3. Your men note the decision."),
                    new InquiryElement("c", "Have him secured to his horse and push on.", null, true,
                        "Morale -5. He will be worse when you arrive. Some men won't forget."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(-200);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("Coin left with the innkeeper, instructions given. He watches you ride out from the stable door with the expression of a man memorising the moment he was not left. He will catch up in a week. Your men file past him without comment, which is its own form of respect.", GoodColor);
                            break;
                        case "b":
                            MobileParty.MainParty.RecentEventsMorale += 3f;
                            Msg("You wait two days. The men use the time without complaint. On the third day he walks out under his own power, embarrassed and grateful in equal measure. The delay costs something. The decision costs nothing.", GoodColor);
                            break;
                        case "c":
                            MobileParty.MainParty.RecentEventsMorale -= 5f;
                            Msg("He is secured and he rides. He says nothing and neither does anyone else. Three men check on him every hour and file their reports to you without eye contact. He makes it to the next town. He is worse. That information travels through your camp faster than orders.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 124. The Wrong Song
        private static void LV5_WrongSong(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Wrong Song",
                "The village inn has a bard performing a ballad about you. It has your name, your approximate description, and three specific incidents that are entirely wrong — in one you slew a dragon, in another you appeared at a battle you were not at, and in the third you apparently said something wise that you have no memory of saying. He is mid-verse when he sees you standing in the doorway.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Let him finish. The audience is enjoying it.", null, true,
                        "Morale +5. Renown +3. The story is already out there."),
                    new InquiryElement("b", "Correct the record afterward — buy him a drink and give him the truth.", null, true,
                        "Lose 100 gold. Renown +5. The song improves."),
                    new InquiryElement("c", "Ask him where he heard it.", null, true,
                        "Flavor only. The story is three tellings removed and has developed opinions of its own."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            MobileParty.MainParty.RecentEventsMorale += 5f;
                            ChangeRenown(3f);
                            Msg("He finishes. The crowd applauds. He catches your eye across the room and his expression moves through three stages: recognition, panic, and then — when you don't stop him — relief. He takes his coin. The song will continue to be wrong in all the same ways. That is how legend works.", DimColor);
                            break;
                        case "b":
                            ChangeGold(-100);
                            ChangeRenown(5f);
                            Msg("You sit with him afterward and give him the actual account of each incident. He listens, makes notes, and looks genuinely delighted at how much better the truth is than the invention. The revised version will travel faster and be wrong in new ways within a month. But for now it's accurate.", GoodColor);
                            break;
                        case "c":
                            Msg("He traced it through four sources before finding a merchant who heard it from a soldier who claimed to have been there. The soldier was not at any of those events. He embellished freely. You are now the protagonist of a story about yourself that you never participated in. This is apparently normal.", DimColor);
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
                        "Renown +10. The authorised portrait becomes the standard."),
                    new InquiryElement("b", "Approve his enterprise and wish him well.", null, true,
                        "Renown +5. Gain Honor — grace costs nothing."),
                    new InquiryElement("c", "Buy all the existing copies.", null, true,
                        "Lose 300 gold. He will make more. You know this."),
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
                        "Costs 1 day. Renown +5. She understands something true."),
                    new InquiryElement("b", "Deflect kindly. The fire is not a medical subject.", null, true,
                        "Nothing. She writes it in her notes anyway."),
                    new InquiryElement("c", "Tell her she is mistaken.", null, true,
                        "Lose Honor. She does not believe you and notes that too."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
                            ChangeRenown(5f);
                            Msg("You tell her what it costs and what it gives. She listens with the complete attention of someone updating a life's framework in real time. She asks three questions that are so specific you have to think before answering. When you leave, she is already writing. Something in you is satisfied by being understood precisely.", FireColor);
                            break;
                        case "b":
                            Msg("\"It is an unusual condition,\" you say. She nods with the expression of someone who has been told a polite version of something real and is already translating it. She will write something true about you from the deflection alone. That is the hazard of doctors.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("She looks at you for a moment. Then she looks at her notes. \"Mistaken,\" she repeats, the way people repeat a word they are storing. She thanks you for your time and goes back inside. You have given her more data than an honest answer would have.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 127. An Old Face
        private static void LC5_OldAlly(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  An Old Face",
                "Someone calls your name from near the gate. It takes a moment — you knew him as a capable captain, sharp, well-regarded in his clan. He is less than that now. The uniform is gone, the bearing mostly. He is not quite begging. He is finding reasons to stand near you.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Give him money and offer him work if he wants it.", null, true,
                        "Lose 400 gold. Gain Merciful. Morale +3 if he joins."),
                    new InquiryElement("b", "Give him coin and part ways.", null, true,
                        "Lose 200 gold. Gain Merciful."),
                    new InquiryElement("c", "Walk past. You don't know what you would even say.", null, true,
                        "Lose Honor. The distance is easier than the conversation."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                        {
                            ChangeGold(-400);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            MobileParty.MainParty.RecentEventsMorale += 3f;
                            var t_lc5 = MBObjectManager.Instance.GetObject<CharacterObject>("watchman")
                                     ?? MBObjectManager.Instance.GetObject<CharacterObject>("sea_raider")
                                     ?? MBObjectManager.Instance.GetObject<CharacterObject>("looter");
                            if (t_lc5 != null) try { MobileParty.MainParty.MemberRoster.AddToCounts(t_lc5, 1); } catch { }
                            Msg("You press the coin on him first, then name the offer. He straightens up slightly — not all the way, but enough. He falls in at the column's rear. Your veterans give him space without being instructed to. He knows what that means.", GoodColor);
                            break;
                        }
                        case "b":
                            ChangeGold(-200);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You give him what you have in your belt. He takes it without the elaborate gratitude of someone who has been doing this for a while, which suggests he hasn't been doing this for a while. The money surprises him. You ride on before the surprise turns into conversation.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("You keep walking. He watches you go with the expression of a man who has had this conversation before and knows exactly how it ends. You will think about his face at odd moments for several weeks.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 128. The Horse That Stayed (after battle)
        private static void EB4_WarHorse()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  The Horse That Stayed",
                "One horse on the field is not yours. All the others have bolted or been taken. This one is standing beside its dead rider, still saddled, and will not move for anyone in your party — not from fear, not from stubbornness. It simply has not been given permission to go.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Approach it yourself and take its reins.", null, true,
                        "It comes with you. A good horse. Morale +3."),
                    new InquiryElement("b", "Send your most gentle rider.", null, true,
                        "50/50: it accepts or it goes its own way."),
                    new InquiryElement("c", "Leave it. Some loyalties should finish on their own terms.", null, true,
                        "Gain Honor. It stands there until nightfall."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            MobileParty.MainParty.RecentEventsMorale += 3f;
                            Msg("It watches you come. When you take the reins it drops its head once — not in defeat, in acknowledgement. Your men give it a name before you have cleared the field. They will argue about the name for three days.", GoodColor);
                            break;
                        case "b":
                            if (_rng.Next(2) == 0)
                            {
                                MobileParty.MainParty.RecentEventsMorale += 3f;
                                Msg("It accepts the gentle approach and the gentle hands. It comes. It is a very good horse. Your rider looks as if he has been given something he did not expect to deserve.", GoodColor);
                            }
                            else
                                Msg("It allows the approach, considers the hand, and then simply walks north at an angle that suggests a destination. Your rider watches it go. You all do.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You leave it. At nightfall, when you look back, it is still there — a dark shape against the field. By morning it is gone. Where is not your knowledge, and probably not your business.", GoodColor);
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
                        "Costs 1 day. The working is precise and costs accordingly. The marker is gone."),
                    new InquiryElement("b", "Keep it. Knowing it exists is an advantage.", null, true,
                        "Cold intel artifact. The Ashen will eventually notice its silence."),
                    new InquiryElement("c", "Leave it in place — let them think nothing was found.", null, true,
                        "Gain Calculating. The deception is yours to manage."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
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

        // 130. The Marked Child (after raid)
        private static void ER4_AshenChild()
        {
            bool mage = MageKnowledge.IsMage;
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Marked Child",
                "In the aftermath, one of your men finds a hidden cache in a burned cottage — cold tools, a correspondence in the Ashen dialect, and what looks like the beginning of marks on a child's clothes left behind. The family says their son has not been seen for two days. The marks are not birthmarks. They are made.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Tell the family what the marks mean and what to watch for.", null, true,
                        "Gain Merciful. They will be devastated. They need to know."),
                    new InquiryElement("b", mage ? "Read the fire-echo of what was done here." : "Search the area before the trail goes cold.", null, true,
                        mage ? "Costs 1 day. Ashen intel about who made the marks." : "Renown +5. Uncover something useful."),
                    new InquiryElement("c", "Report it to the nearest lord — this is beyond what you can address alone.", null, true,
                        "Relation +5. Renown +5. The machinery of authority begins."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You tell them as directly as you can what the marks mean and who makes them and what they want. The father sits down in the mud. The mother asks one question: can he come back from it? You tell them the truth, which is: it depends on how long and how willing. They have hope and they have terror. Both are more useful than ignorance.", AshenColor);
                            break;
                        case "b":
                            if (mage)
                            {
                                AgingSystem.AgeHero(Hero.MainHero, 1);
                                Msg("You put your hand on the stone floor of the cottage and let the fire look back in time, briefly, the way it can when something significant happened in a space. An Ashen lord — one you know by reputation, one who operates quietly — was here three weeks ago. The child was brought to them, not taken. That changes the shape of this considerably.", AshenColor);
                            }
                            else
                            {
                                ChangeRenown(5f);
                                Msg("Your men search the surrounding area. A trail leads north for two miles before losing itself in rocky ground. Evidence of a camp. Cold ash that was cold before it was ash — the Ashen kind. Whoever took the child has a three-day lead and knows how to move without being followed.", DimColor);
                            }
                            break;
                        case "c":
                            ChangeRelWithRandomLord(5);
                            ChangeRenown(5f);
                            Msg("The report goes out with a rider. What the lord does with it — whether they treat it as urgent or as administration — is not in your hands. You have given it to a system. Systems move slowly. The child has a two-day head start northward.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 131–160 — SIXTH BATCH
        // ═══════════════════════════════════════════════════════════════════

        // 131. The Mill Dispute
        private static void EV6_MillDispute(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚖  The Mill Dispute",
                "Two families are on the verge of violence at the village mill. The issue is water rights: the upstream family diverted the millstream last autumn and the downstream family's mill has been running dry since. Both families have children watching from a distance. The headman has been trying to mediate for three months and has nothing left. He looks at you with the expression of a man who has run out of tools.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Rule on it as a lord — split the water rights formally by season.", null, true,
                        "Gain Honor. Relation +5 with settlement owner. Both families are partially satisfied."),
                    new InquiryElement("b", "Hear both sides in full before deciding.", null, true,
                        "Gain Calculating. Honor +1. Your ruling will be informed and fair."),
                    new InquiryElement("c", "Pay to have a second channel dug — remove the scarcity entirely.", null, true,
                        "Lose 500 gold. Gain Merciful. The dispute ends permanently. Both families remember."),
                    new InquiryElement("d", "Tell the headman to hold them apart until a proper magistrate arrives.", null, true,
                        "Nothing immediate. The delay may outlast the headman's ability to keep peace."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ChangeRelWithOwner(s, 5);
                            Msg("You give a ruling: upstream family diverts only during the dry months, downstream family has priority when the stream is low. It is not what either wanted. It is workable. Both families stand with the specific expression of people who have received the most justice the situation allowed rather than the most justice they hoped for. The headman thanks you with genuine relief.", GoodColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You spend an hour with each family separately. The upstream family's diversion was originally permitted by the previous headman in exchange for a loan that was repaid. The downstream family has documentation of the original water rights. The ruling you give addresses both and cites both. It will hold because it is demonstrably correct. The families accept it. The headman copies it out and pins it to the mill post.", GoodColor);
                            break;
                        case "c":
                            ChangeGold(-500);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You commission the work on the spot and leave coin with the headman sufficient to see it done. Both families watch this with the particular attention of people who have been fighting over a scarcity and are now watching the scarcity disappear. The upstream father and the downstream father look at each other for the first time without the argument between them. It is a beginning.", GoodColor);
                            break;
                        case "d":
                            Msg("The headman receives this with the expression of a man who has been given a task that will end him before the magistrate arrives. He thanks you anyway. He will do what he can. The families separate for the evening. Whether they stay separated through the week is a question the headman will be answering alone.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 132. The Debt Collector
        private static void EV6_DebtCollector(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚖  The Debt Collector",
                "A city official with two armed escorts is working through the village, seizing goods against unpaid grain levies. The amounts seem correct on paper. The method is not — he has taken a widow's seed stock, which means she has nothing to plant in spring. She is standing in her doorway watching her future be loaded onto a cart.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Intervene. Seed stock is exempt from levy seizure by custom.", null, true,
                        "Gain Honor. Relation -5 with city lord. She keeps her seeds."),
                    new InquiryElement("b", "Pay the widow's debt yourself and say nothing to the official.", null, true,
                        "Lose 300 gold. Gain Merciful. No confrontation."),
                    new InquiryElement("c", "Ask to review his ledger and document the irregularities.", null, true,
                        "Gain Calculating. Relation +5 with a lord who dislikes this official."),
                    new InquiryElement("d", "Ride past. The law is the law and so is his authority.", null, true,
                        "Lose Honor. The widow watches you go."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ChangeRelWithOwner(s, -5);
                            Msg("You cite the custom directly. The official's escorts stiffen but don't move — you are a lord and the law is genuinely ambiguous on this. He orders the seeds returned with the expression of a man keeping a list. The widow says nothing. She doesn't need to.", GoodColor);
                            break;
                        case "b":
                            ChangeGold(-300);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You pay the debt in coin, quietly, to the official's clerk while he is occupied with the next house. The widow's cart is released. She will spend the rest of the day trying to understand why. The answer, if she finds it, will be correct.", GoodColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            ChangeRelWithRandomLord(5);
                            Msg("You spend twenty minutes with the ledger and your own quill. The irregularities are real and specific and will travel well as written complaints. You say nothing to the official. His enemies in the city administration will hear about this before he does.", DimColor);
                            break;
                        case "d":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("You ride past. The cart rolls. The widow's face does not change because it already knows the shape of this. Your men file past without meeting her eyes, which is its own answer.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 132. The Stranger's Horse
        private static void EV6_StrangersHorse(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✿  The Stranger's Horse",
                "An expensive horse — a lord's horse, by its tack — is tied outside an abandoned house on the edge of the village. It has been there since yesterday morning. Nobody claims to know whose it is. The house has been empty for two years. Nobody is willing to approach it. The horse watches everything with a steadiness that has nothing to do with waiting.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Enter the house and see what's inside.", null, true,
                        "50/50: valuable intelligence or a trap."),
                    new InquiryElement("b", "Have someone watch the horse and wait.", null, true,
                        "Gain Calculating. The owner returns. You have seen his face."),
                    new InquiryElement("c", "Ask the headman plainly — someone knows something.", null, true,
                        "Flavor. The headman knows something small."),
                    new InquiryElement("d", "Leave it alone and tell the headman to report anything to the nearest garrison.", null, true,
                        "Relation +3 with local garrison commander."),
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
                                Msg("Inside: a satchel, maps of the eastern road with patrol schedules marked, and a letter in cipher. Whoever left this expected to return for it and did not. The horse is calm because it has been here before. You take the satchel. The information inside is valuable and someone is looking for it.", GoodColor);
                            }
                            else
                            {
                                MobileParty.MainParty.RecentEventsMorale -= 3f;
                                Msg("The door opens on a man seated at the table, very still, with a crossbow resting on his knee and aimed at exactly where you walked in. He is a hired courier who was told to wait. He was told it by someone you would rather not have your location. He lets you leave. He stays.", BadColor);
                            }
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("Three hours later: a rider in plain clothing, moving quickly, stops at the horse and discovers the watcher. He makes a decision in less than a second and rides north at a pace that contains the answer to every question. The horse goes with him. You have seen his face.", DimColor);
                            break;
                        case "c":
                            Msg("The headman says it arrived with a man who paid for stabling and a meal, went to the house, and did not come back out. He did not go in to check. He explains this as if it is obvious. Perhaps it is.", DimColor);
                            break;
                        case "d":
                            ChangeRelWithRandomLord(3);
                            Msg("The garrison commander receives the report and sends two men. They find the horse, the empty house, and nothing else. They write it down and file it. It may matter later. You have ensured it exists as a record. This is most of what administration is.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 133. The Sick Healer
        private static void EV6_SickHealer(Settlement s)
        {
            bool mage = MageKnowledge.IsMage;
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✚  The Sick Healer",
                "The village healer — the person this village relies on for fever, birth, broken bones, and every other thing that can go wrong with a body — is sick. Not gravely, but genuinely incapacitated. The village is managing, but managing is not the same as fine. Two families have members who need real attention. The healer apologises for the inconvenience with the specific exhaustion of someone who has never been allowed to be sick before.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", mage ? "Use the fire to speed the healer's recovery." : "Leave your party's surgeon with the village for two days.", null, true,
                        mage ? "Costs 1 day. Gain Merciful. Healer recovers immediately." : "Gain Merciful. Your surgeon handles it. You wait."),
                    new InquiryElement("b", "Leave coin and medicine from your own supplies.", null, true,
                        "Lose 200 gold. Gain Merciful. The village manages better."),
                    new InquiryElement("c", "Sit with the healer and document what they know before riding on.", null, true,
                        "Gain Calculating. You leave with knowledge. The healer is moved by this."),
                    new InquiryElement("d", "Ride on. Healers get sick; villages endure.", null, true,
                        "Nothing."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (mage)
                            {
                                AgingSystem.AgeHero(Hero.MainHero, 1);
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                Msg("You place your hands on theirs and let the fire work at something more subtle than heat — the particular warmth that allows a body to correct itself. By evening they are standing. They look at their own hands with the expression of someone who has been given something they cannot keep but will remember.", FireColor);
                            }
                            else
                            {
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                MobileParty.MainParty.RecentEventsMorale -= 2f;
                                Msg("Your surgeon stays. He grumbles about it with the specific grumbling of a man who is entirely willing. You ride ahead and meet him two days later on the road. He reports both families stable and the healer standing. He is in a good mood. Some work agrees with a person.", GoodColor);
                            }
                            break;
                        case "b":
                            ChangeGold(-200);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You leave willow bark, clean bandaging, and enough coin for a week's provisions. The healer counts it with the careful attention of someone who has never received this without conditions. There are no conditions. They check twice to confirm.", GoodColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You spend two hours at their bedside with a writing board. They dictate remedies, dosages, and the specific knowledge of two dozen years of treating this particular valley's ailments. By the end they are flushed and talking faster. Being taken seriously appears to be medicinal. You leave with more than you arrived with.", DimColor);
                            break;
                        case "d":
                            Msg("You ride on. The village manages, as villages do, in the particular way that means some things get slightly worse before anyone admits they should not have waited.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 134. The Child's Map (mage-gated)
        private static void EV6_ChildsMap(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Child's Map",
                "A boy of perhaps ten runs alongside your horse and holds up a piece of bark with markings scratched into it. He says: \"I drew where the cold men camp. Nobody will look at it.\" The markings are crude but specific — a stream bend, a hill shape, distances approximated by how long he walked. He went there alone to draw it. He is proud of himself in the way of someone who has been ignored.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Study the map carefully and ask him questions.", null, true,
                        "Ashen camp intel. Renown +5. He will remember being listened to for the rest of his life."),
                    new InquiryElement("b", "Take the map and give him coin — this is real intelligence work.", null, true,
                        "Lose 100 gold. Renown +5. Ashen camp location."),
                    new InquiryElement("c", "Tell him to show the village headman and explain what he saw.", null, true,
                        "Nothing mechanical. The headman is unlikely to listen. The boy will learn something about authority."),
                    new InquiryElement("d", "Tell him gently to stay away from wherever the cold men are.", null, true,
                        "Gain Merciful. No intel. He is disappointed but alive."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeRenown(5f);
                            Msg("You halt and study the bark properly. You ask him about the cold — was it air cold or ground cold — and he knows the difference without being told that there is one. The camp is real, recent, and closer than anyone official believes. He watches you copy the markings into your own journal with an expression he will not have words for until he is much older.", FireColor);
                            break;
                        case "b":
                            ChangeGold(-100);
                            ChangeRenown(5f);
                            Msg("You pay him in silver and tell him specifically that this is what good scout work is worth. His face does three things at once. You ride with his map and the knowledge that somewhere behind you a boy is deciding what he wants to be.", GoodColor);
                            break;
                        case "c":
                            Msg("You send him to the headman. The headman will pat him on the head and tell him not to wander. The boy will understand something about the world from this, and the lesson will not be a gentle one. The camp remains where it is.", DimColor);
                            break;
                        case "d":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You tell him the cold men are dangerous and that his map was brave but that brave is not always safe. He is disappointed in the specific way of a child who hoped to matter. He will think about this conversation for a long time. So will you.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 135. The Burned Shrine (mage-gated)
        private static void EV6_BurnedShrine(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Burned Shrine",
                "A roadside shrine at the village edge was burned recently — the ash is still warm. It was a fire-shrine, the old kind, the kind that predates the current priesthood by several hundred years. The villagers say it burned itself in the night. You know what burned it: something cold passing close, extinguishing by proximity rather than intent. The Ashen do not always mean to kill fire. Sometimes they simply cannot help it.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Relight the shrine. What was here should still be here.", null, true,
                        "Costs 1 day. Gain Merciful. The fire remembers the shape of the place."),
                    new InquiryElement("b", "Read what remains — the Ashen passing left traces.", null, true,
                        "Ashen movement intel. Costs 1 day."),
                    new InquiryElement("c", "Tell the headman what you suspect and which direction they came from.", null, true,
                        "Relation +5 with settlement owner. The village prepares."),
                    new InquiryElement("d", "Say nothing. They will rebuild it themselves when the fear fades.", null, true,
                        "Nothing."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You rebuild the shrine's form from the ash and put the fire back into it — not decoratively, but properly, the way the old shrines held warmth: a working, a lasting, something that will not go out easily. The villagers gather without being summoned. A child touches the warm stone and pulls their hand back and then reaches again.", FireColor);
                            break;
                        case "b":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
                            Msg("You press your palm to the ash and let the fire look backward at what extinguished it. A pale shape, moving fast, three of them, northwest to southeast — they were not scouting. They were running from something. That changes the intelligence considerably. Something in the north is making the Ashen move south in haste.", AshenColor);
                            break;
                        case "c":
                            ChangeRelWithOwner(s, 5);
                            Msg("You tell the headman what a cold-passing leaves behind and which direction they came from. He listens with the attention of someone who has been waiting for official confirmation of what he already suspected. He thanks you formally, then immediately sends the oldest children to relatives further south.", DimColor);
                            break;
                        case "d":
                            Msg("You ride past. The ash is still warm. By next week someone will have swept it clean and put up a cross of straw instead, which is what happens when a tradition outlasts its understanding.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 136. The Oath on the Road
        private static void LV6_OathOnRoad(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚜  The Oath on the Road",
                "A man in his thirties steps onto the road in front of your column and kneels. He says he has been waiting three days. He offers you his sword — an ordinary one, well-kept — and his oath of service. He has a reason, which he will tell you if you ask: a lord in the east took his family's land and he has no legal recourse and no army. He is not desperate. He is decided.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Accept the oath and take him on.", null, true,
                        "Morale +3. He joins your party. A solid man."),
                    new InquiryElement("b", "Accept the oath but ask about the land dispute first.", null, true,
                        "He joins. You learn something potentially actionable about the eastern lord."),
                    new InquiryElement("c", "Decline with respect — you cannot solve his problem by accepting his service.", null, true,
                        "Gain Honor. He understands. He will find another way."),
                    new InquiryElement("d", "Tell him to petition the lord's court formally before taking this step.", null, true,
                        "Flavor. He has already tried. He tells you this without bitterness."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                        {
                            MobileParty.MainParty.RecentEventsMorale += 3f;
                            var t_lv6a = MBObjectManager.Instance.GetObject<CharacterObject>("watchman")
                                      ?? MBObjectManager.Instance.GetObject<CharacterObject>("sea_raider")
                                      ?? MBObjectManager.Instance.GetObject<CharacterObject>("looter");
                            if (t_lv6a != null) try { MobileParty.MainParty.MemberRoster.AddToCounts(t_lv6a, 1); } catch { }
                            Msg("You accept. He stands and sheathes his sword with the particular economy of a man who has been practising this moment in his head. Your veterans make room for him without being asked. He will be useful because he has already decided to be.", GoodColor);
                            break;
                        }
                        case "b":
                        {
                            MobileParty.MainParty.RecentEventsMorale += 3f;
                            ChangeRelWithRandomLord(-3);
                            var t_lv6b = MBObjectManager.Instance.GetObject<CharacterObject>("watchman")
                                      ?? MBObjectManager.Instance.GetObject<CharacterObject>("sea_raider")
                                      ?? MBObjectManager.Instance.GetObject<CharacterObject>("looter");
                            if (t_lv6b != null) try { MobileParty.MainParty.MemberRoster.AddToCounts(t_lv6b, 1); } catch { }
                            Msg("He tells you about the eastern lord's land seizure — specific, documented in his head if not on paper, the kind of grievance that tends to be accurate because it has been rehearsed for a long time. You accept his oath. The information travels with him.", DimColor);
                            break;
                        }
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You explain it plainly: his service would be real but his problem would remain. He considers this for a moment, then nods. He picks up his sword and walks back off the road. He is not crushed. He is recalculating. Some men need only to be taken seriously to find their own way.", GoodColor);
                            break;
                        case "d":
                            Msg("\"I have petitioned twice,\" he says, without heat. \"The second time they returned my letter unopened.\" He holds the information out to you as evidence, not complaint. He waits. You have told him to try something he has already tried. He is deciding what this means about you.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 137. The Fire Tender (mage-gated)
        private static void LV6_FireTender(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Fire Tender",
                "An old woman at the village's edge has kept a small fire burning continuously for thirty-one years. She says her grandmother told her someone would come who would know what it was for. She says this plainly, without drama, the way people say things they have said to themselves so many times the words have worn smooth. She looks at you and whatever she sees satisfies something.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Sit with her and learn what the fire is.", null, true,
                        "Costs 1 day. Rare lore. The fire tells you something it has been keeping."),
                    new InquiryElement("b", "Accept her recognition and give her something in return.", null, true,
                        "Lose 200 gold. Gain Honor. Morale +5. She has been waiting a long time."),
                    new InquiryElement("c", "Ask who taught her grandmother and why.", null, true,
                        "Gain Calculating. Rare lineage lore."),
                    new InquiryElement("d", "Tell her kindly that she has the wrong person.", null, true,
                        "She shakes her head once. She does not believe you. You almost believe her."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
                            Msg("You sit beside the fire for a full day. What it has been holding is not words — it is a shape, a memory of a working done here three generations ago by someone trying to leave a message for the next person who could receive it. You receive it. The working is a warning about the northern pass. It was placed here because the person who placed it did not expect to survive to deliver it in person. They were correct.", FireColor);
                            break;
                        case "b":
                            ChangeGold(-200);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            MobileParty.MainParty.RecentEventsMorale += 5f;
                            Msg("You give her enough coin to outlast the fire and tell her she did not wait for nothing. She receives this with the stillness of someone who has been very still for thirty-one years and is now very quietly not still. Your men, watching from the road, do not speak until you are well past the village.", GoodColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("Her grandmother was taught by a woman who passed through in a winter so cold the wells froze solid, which was wrong for the season. The woman had warm hands and left quickly. She gave three instructions: tend the fire, tell no one official, wait. The waiting was the hardest part. It was also, apparently, the point.", FireColor);
                            break;
                        case "d":
                            Msg("She looks at your hands while you speak. She says: \"I know what warm hands look like in winter.\" She does not argue. She does not need to. You ride away with the specific feeling of having made an error you cannot prove was an error.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 138. The Hidden Grave
        private static void LV6_HiddenGrave(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "☠  The Hidden Grave",
                "A fresh grave just outside the village boundary — no marker, turned earth still dark. Someone was buried in the last two days and buried quietly, outside the common ground, which means either shame or secrecy. The village knows it is there. Nobody mentions it. Two people looked away as you passed the spot.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Ask the headman directly who is buried there.", null, true,
                        "Gain Calculating. He tells you what he is allowed to tell you."),
                    new InquiryElement("b", "Investigate quietly yourself.", null, true,
                        "50/50: confirms a crime or reveals a tragedy."),
                    new InquiryElement("c", "Leave a marker — whoever it is deserves that much.", null, true,
                        "Gain Merciful. Honor +1. The village watches. Some will be grateful."),
                    new InquiryElement("d", "Note it and ride on — unmarked graves are not always your jurisdiction.", null, true,
                        "Nothing. The unease travels with you."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("The headman says it was a traveler who died of cold. He says this to your left shoulder rather than your face, and he says 'traveler' the way people say a word they've agreed on. The traveler had no name he knew. The burial was done quickly for health reasons. Every sentence is technically true. None of them are entirely honest.", DimColor);
                            break;
                        case "b":
                            if (_rng.Next(2) == 0)
                            {
                                ChangeCrime(3f);
                                Msg("The grave is shallow. The man inside has a lord's seal on a chain around his neck and bruising inconsistent with a fall. Someone in this village killed a messenger and buried the evidence six feet from the road. The message he was carrying is gone. The seal identifies which lord sent him.", BadColor);
                            }
                            else
                            {
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                Msg("A young woman, buried with her child beside her. Both recent. No violence. The headman finds you at the grave and says quietly that her husband rode south three months ago and did not come back, and she had no one. He buried her outside the common ground because there are people here who would have objected to her being inside it. He looks like he has been ashamed about that for two days.", DimColor);
                            }
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You dismount and set a stone. A simple one, upright. It is not much but it is something to mark the place against. Three village women watch from a distance and none of them explain why they are there. When you ride past them they step aside with something that is not quite gratitude but is adjacent to it.", GoodColor);
                            break;
                        case "d":
                            Msg("You ride on. The unease does not. Some things ask questions at you from behind and the only answer is speed, which is not an answer.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 139. The Blind Soldier
        private static void LV6_BlindSoldier(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  The Blind Soldier",
                "A man working a cobbler's stall near the road is blind — cloth bound over both eyes, the permanent kind. He turns toward your horse's sound with a specificity that means he has been expecting you, or someone like you. He asks, very carefully, if you are the lord who ordered the charge at the river crossing four years ago. He was a soldier there. He lost his eyes in the river. He says he is not asking for anything. He says this in a way that contains several possible meanings.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Tell him the truth about the order.", null, true,
                        "Gain Honor. He receives the truth. What he does with it is his."),
                    new InquiryElement("b", "Sit down and hear his account of the crossing first.", null, true,
                        "Gain Calculating. You learn something about how that battle was understood by the men in it."),
                    new InquiryElement("c", "Give him money and keep walking.", null, true,
                        "Lose 300 gold. Gain Merciful. He takes the coin. He notes that you didn't answer."),
                    new InquiryElement("d", "Tell him you were not there.", null, true,
                        "Lose Honor. He says: 'I know what you sound like.' He lets you go anyway."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You tell him the order, the reason, and the cost as you knew it at the time. He listens without moving. When you finish he is quiet for a long time. Then he says: 'That's what I thought it would be.' Not forgiveness. Not accusation. Recognition. He picks up his work and resumes it. You ride away with the specific weight of having been accurate.", GoodColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("He tells you what the crossing looked like from the water. He uses the word 'us' for your men and the word 'them' without anger, just geography. He describes the moment the order reached the front of the column with the precision of a man who has reconstructed it ten thousand times in the dark. You learn things about that battle that your officers' reports did not include.", DimColor);
                            break;
                        case "c":
                            ChangeGold(-300);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("He takes the coin without counting it. He says: \"That's not nothing.\" He means the money. He also means that you stopped. He also means that you didn't answer. He is very precise with language for a cobbler.", DimColor);
                            break;
                        case "d":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("He says: \"I know what you sound like. I've known for four years.\" He does not raise his voice. He does not pursue. He lets you walk past with his answer, which is worse than anything he could have said.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 140. The Tribunal
        private static void EC6_Tribunal(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚖  The Tribunal",
                "A public sentencing is underway in the square: a woman accused of theft — three bolts of cloth. The evidence is thin. The sentence proposed is the removal of a hand. The merchant pressing charges has a lord's cousin on his ledger as a debtor, which may explain why the presiding magistrate is not looking at the crowd. The woman has three children standing at the square's edge watching.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Intervene as a lord — demand the evidence be properly examined.", null, true,
                        "Gain Honor. Relation -5 with magistrate's lord. She may go free."),
                    new InquiryElement("b", "Pay the merchant's claimed loss and end the proceeding.", null, true,
                        "Lose 400 gold. Gain Merciful. Clean resolution. The merchant is furious."),
                    new InquiryElement("c", "Question the merchant publicly about his debt to the lord's cousin.", null, true,
                        "Gain Calculating. Honor +1. The proceeding collapses. He will not forget this."),
                    new InquiryElement("d", "Watch. Intervening in a city's legal process has costs you cannot always afford.", null, true,
                        "Lose Honor. You will think about her hands at odd moments for weeks."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ChangeRelWithOwner(s, -5);
                            Msg("You identify yourself and demand a proper accounting of the evidence. The magistrate recesses. Twice. The merchant's original complaint cannot survive scrutiny and the magistrate knows it. The charge is reduced to a fine that the woman cannot pay, which means she walks away with her hand and a debt she will carry for years. Better than the alternative. Not good.", GoodColor);
                            break;
                        case "b":
                            ChangeGold(-400);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You pay the claimed amount plus costs to the merchant directly, in front of the magistrate, and declare the debt settled. The merchant objects on principle. The magistrate thanks you for resolving the matter and closes the proceeding. The woman collects her children without looking at you. She is not going to look at you. She is going home.", GoodColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You ask the merchant, in the court, with witnesses, about his outstanding debt to the lord's cousin and whether the lord's cousin is aware the debt is being called in indirectly via this proceeding. The square goes quiet. The merchant goes pale. The magistrate adjourns. The woman is not released today but she will be. The merchant has a different kind of problem now.", DimColor);
                            break;
                        case "d":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("You watch. The sentence is confirmed. The woman does not cry, which is the worst part — she has already understood that crying will not help. Her children watch from the edge of the square. You ride on with the specific heaviness of a thing you chose not to do.", BadColor);
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
                        "Costs 1 day. Gain Merciful. Renown +10. You save him."),
                    new InquiryElement("b", "Call to him and guide him out with your voice.", null, true,
                        "50/50: he makes it or the smoke takes him before you reach him."),
                    new InquiryElement("c", "Contain the fire from outside — stop it spreading while the watch enters.", null, true,
                        "Costs 1 day. The building is lost. He survives. Renown +5."),
                    new InquiryElement("d", "Ask the watch what he has in there before committing to anything.", null, true,
                        "Gain Calculating. The answer changes the calculus considerably."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ChangeRenown(10f);
                            Msg("You walk through the burning door. The fire parts around you with the specific recognition of something meeting itself. The alchemist is on his knees trying to seal a set of ceramic jars. You pick him up under one arm, take the jars under the other on instinct, and walk back out. The watch stares. The building comes down twenty seconds later. He is alive. The jars are intact. He says they contain something he has been twenty years developing and says nothing else for a long time.", FireColor);
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
                            AgingSystem.AgeHero(Hero.MainHero, 1);
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

        // 142. The Petition
        private static void EC6_Petition(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚜  The Petition",
                "A queue of citizens outside the lord's hall, waiting to file grievances. Most of them are here with complaints that the hall will classify as administrative and return unread. One of them is not: a farmer third from the back with a folded document who keeps looking toward the gate as if expecting someone to stop him from filing it. He has found something. What he has found is real enough that someone would prefer he not file it.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Approach him and ask about his petition.", null, true,
                        "He tells you. The information is significant. Someone is watching this queue."),
                    new InquiryElement("b", "Have one of your people stand with him in the queue so he reaches the front.", null, true,
                        "Gain Calculating. His petition is filed. Someone notices."),
                    new InquiryElement("c", "Offer to take his petition directly to the lord yourself.", null, true,
                        "Renown +5. Relation +5 with lord. The petition is heard. Gain Honor."),
                    new InquiryElement("d", "Ride on. You do not have time for every queue in every city.", null, true,
                        "Nothing. Whatever he found stays buried."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("He shows you the document. It is a record of grain shipments that went to the wrong destination by the same accounting error for six consecutive seasons. The error is the same each time, in the same handwriting, in the same direction. That is not an error. Someone in the city administration has been diverting grain for six years. He found it by accident. He is very scared and very certain.", DimColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("Your man stands behind him and the queue moves. He reaches the clerk. The petition is stamped and entered. Whether it reaches the lord is a different question, but it is now a record in the system. The man with the folded document leaves faster than he arrived. Two men who were standing at the gate's edge are no longer there.", DimColor);
                            break;
                        case "c":
                            ChangeRenown(5f);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ChangeRelWithOwner(s, 5);
                            Msg("You take the document and deliver it to the lord's chamberlain personally. The lord receives it the same afternoon. Whatever it contains causes an internal inquiry that you will hear about three weeks later as a rumor and then six weeks later as fact. The farmer will never know you were involved. That is fine. That was the point.", GoodColor);
                            break;
                        case "d":
                            Msg("You ride on. The queue shuffles. Whoever was watching the queue from the far side of the square continues watching. The farmer with the document reaches the front by evening and is told to come back tomorrow. The document does not reach the lord. Whatever it contained stays buried.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 143. The Gladiator
        private static void EC6_Gladiator(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  The Gladiator",
                "A pit fighter in the city's arena district recognises you — not from your reputation, from your face. He was a man-at-arms in your third campaign, left after a wound that ended his military usefulness, and found his way here. He is not bitter about it. He looks like someone who has organised a life around what remained after the thing he was good at was taken from him. He asks if you have a moment.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Give him a moment. Hear what he has to say.", null, true,
                        "He has information about the arena's owner, who has connections you'll want to know about."),
                    new InquiryElement("b", "Give him coin and an offer to return to service.", null, true,
                        "Lose 200 gold. Morale +3. He joins. Your veterans recognise him."),
                    new InquiryElement("c", "Sit with him and buy him a meal — the man earned it.", null, true,
                        "Lose 100 gold. Gain Honor. Morale +5. Your men see this."),
                    new InquiryElement("d", "Acknowledge him and keep moving — you have appointments.", null, true,
                        "Nothing mechanical. He nods. He did not expect much more."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("The arena's owner is a merchant who fronts legitimate trade but uses the pit's income to finance something he does not name to anyone. Your former man-at-arms has heard conversations through thin walls that he has been waiting to give to someone with rank. You are the first officer he has seen in three years. He gives them to you. They are useful and specific.", DimColor);
                            break;
                        case "b":
                        {
                            ChangeGold(-200);
                            MobileParty.MainParty.RecentEventsMorale += 3f;
                            var t_ec6 = MBObjectManager.Instance.GetObject<CharacterObject>("watchman")
                                     ?? MBObjectManager.Instance.GetObject<CharacterObject>("sea_raider")
                                     ?? MBObjectManager.Instance.GetObject<CharacterObject>("looter");
                            if (t_ec6 != null) try { MobileParty.MainParty.MemberRoster.AddToCounts(t_ec6, 1); } catch { }
                            Msg("He accepts the offer without performing surprise — he was hoping for it. He falls back in at the column with a familiarity that belongs to someone returning to something rather than entering it. Your veterans who remember him confirm his placement without ceremony. He is good at this. He was always good at this.", GoodColor);
                            break;
                        }
                        case "c":
                            ChangeGold(-100);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            MobileParty.MainParty.RecentEventsMorale += 5f;
                            Msg("You sit with him for an hour in a tavern that smells like sawdust and old fights. He tells you about the last three years in a way that is not a complaint. Your men learn about this afterward and do not say anything about it to you, which is how your men express approval. It was an hour well spent and everyone present seems to know it.", GoodColor);
                            break;
                        case "d":
                            Msg("He nods when you acknowledge him. He expected approximately this. He goes back to his preparations with the particular stillness of a man who has been not-expected-much-of for long enough that he has built a life that does not require it.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 144. The Smuggled Letters
        private static void EC6_SmuggledLetters(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Smuggled Letters",
                "A courier is arrested at the city gate directly in front of your party — city guard, efficient, clearly expected. The courier has a satchel that the guard is not yet examining. Before he is taken he meets your eyes and his gaze goes to his horse's saddlebag, very briefly, very specifically. Then he looks away. The guard has not noticed. Whatever is in that saddlebag was intended for someone. It may now be intended for you.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Take the saddlebag before the guard reaches the horse.", null, true,
                        "Gain Calculating. Crime +3. You have the contents. So does someone who sent them."),
                    new InquiryElement("b", "Tell the guard about the saddlebag.", null, true,
                        "Gain Honor. Relation +5 with city lord. The courier gives you a look you will remember."),
                    new InquiryElement("c", "Follow at distance and see where the guard takes him.", null, true,
                        "Costs half a day. You learn which faction made the arrest. Useful context."),
                    new InquiryElement("d", "Ride past. This is not your arrest and not your courier.", null, true,
                        "Nothing. The saddlebag goes with the horse. Its contents surface elsewhere."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            ChangeCrime(3f);
                            Msg("You take the saddlebag with the casual authority of a lord who does this all the time, which is enough. Inside: three sealed letters in a cipher you partially know, a list of names, and a map of the northern road with stops marked. Whoever sent this knows it was collected. Whether they know by whom is a question that will answer itself in time.", DarkColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ChangeRelWithOwner(s, 5);
                            Msg("You tell the guard sergeant. He collects the saddlebag with the efficiency of someone who already knew about it and was waiting for an excuse to seize it legally. The courier looks at you once. It is not the look of a man betrayed — it is the look of a man taking note of something for later. You are now in someone's ledger under a specific heading.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("The courier is taken to a house two streets from the lord's hall, not to the garrison. Private arrest, private interests. The faction that made this arrest is not the city watch acting in the lord's name — it is someone operating parallel to that authority. The saddlebag's contents are now in that faction's hands. This is useful knowledge about the shape of power in this city.", DimColor);
                            break;
                        case "d":
                            Msg("You ride past. The guard takes the horse and the saddlebag and the courier. Whatever the letters contained surfaces three weeks later as a rumor about someone in this city doing something they should not have been doing. You have no context for the rumor. You might have.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 145. The Informant's Note
        private static void LC6_InformantsNote(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Informant's Note",
                "As you pass through the gate a folded note is pressed into your hand by someone who does not stop walking. It reads: 'I know who burned the eastern way-station and why. Second bridge at dusk. Come alone or don't come.' The way-station fire killed three of your men six months ago and was recorded as an accident. It was not an accident. You suspected this. Now someone else knows that you know.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Go to the second bridge at dusk — alone, as instructed.", null, true,
                        "50/50: real intelligence or a trap set by whoever burned the station."),
                    new InquiryElement("b", "Go to the bridge but station men within reach.", null, true,
                        "Gain Calculating. Slightly lower chance of betrayal. The informant will notice."),
                    new InquiryElement("c", "Try to identify who passed the note before deciding.", null, true,
                        "Costs half a day. 50/50: you identify them or they are already gone."),
                    new InquiryElement("d", "Ignore it. Notes from strangers at gates are how ambushes begin.", null, true,
                        "Nothing. Safe. The information does not reach you. Whoever burned the station remains unnamed."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (_rng.Next(2) == 0)
                            {
                                ChangeRenown(10f);
                                Msg("A woman in grey, a former clerk to the eastern garrison. She names the man who ordered the fire, the faction that paid for it, and the reason — your supply route was inconvenient to someone's trade arrangement. She hands you three documents before disappearing. The information is specific, actionable, and dangerous to have. You ride back with it.", GoodColor);
                            }
                            else
                            {
                                MobileParty.MainParty.RecentEventsMorale -= 5f;
                                Msg("Four men. The note was sent by whoever burned the station, to confirm you hadn't stopped looking. You survive the bridge. Two of your party do not. The men who ambushed you were professionals and the information that the station fire was not an accident dies with them, because they didn't know it either — they were just hired to remove you.", BadColor);
                            }
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            if (_rng.Next(3) != 0)
                            {
                                ChangeRenown(8f);
                                Msg("The informant arrives, sees your men positioned, and pauses. Then continues. She names her source as a point of trust — if she was going to betray you, she would not have come. She gives you what she has. It is real and she knows it is dangerous for her to have given it. Your men's presence means she gets less than she planned to say. She says enough.", GoodColor);
                            }
                            else
                            {
                                Msg("Nobody comes. The bridge is empty at dusk and at full dark. Whoever wrote the note saw your men before you arrived and decided the risk was wrong. The information remains with them. The option to find it again may not reappear.", DimColor);
                            }
                            break;
                        case "c":
                            if (_rng.Next(2) == 0)
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                Msg("Your people work back through the gate crowd and find the passer: a young man who was paid a coin to deliver it and remembers the woman who gave it to him well enough to describe her. Former garrison, probably. The description is enough to find her if you want to. The choice is still yours.", DimColor);
                            }
                            else
                                Msg("The crowd has moved on and whoever delivered the note is not findable in it. You have a note and a bridge and a dusk. The decision remains what it was.", DimColor);
                            break;
                        case "d":
                            Msg("You pocket the note and ride on. Whoever burned the station remains unknown. The note is still in your pocket three days later, unread a second time, and you are not sure what that means about you.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 146. The Noble Hostage
        private static void LC6_NobleHostage(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚜  The Noble Hostage",
                "A noble family is being escorted through the gate by eight armed men — not a guard of honour, an escort. The family's bearing says they know the difference. The youngest daughter, perhaps fourteen, makes direct eye contact with you as the group passes. Her gaze goes from your face to the armed men and back, very specifically, in the way of someone who has been waiting for someone with rank to walk past.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Stop the escort and ask the family if they are travelling willingly.", null, true,
                        "Gain Honor. Relation -10 with the escort's patron. Possible hostage crisis."),
                    new InquiryElement("b", "Intercept the girl — ask her directly while the men are occupied.", null, true,
                        "Gain Calculating. Higher risk. You learn what is actually happening."),
                    new InquiryElement("c", "Note the livery of the escort and have a rider report to the appropriate lord.", null, true,
                        "Relation +5 with the family's clan. No immediate conflict."),
                    new InquiryElement("d", "Ride on. Noble families travel with escorts; this may be entirely normal.", null, true,
                        "Nothing. It may have been normal. It may not have been."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ChangeRelWithRandomLord(-10);
                            Msg("You address the family directly. The father answers before the daughter can — that they are travelling as arranged with Lord Whoever, completely voluntarily. His jaw is tight. The mother's hands are folded too carefully. The daughter says nothing and stares at the middle distance. The escort's captain looks at you with the expression of a man making a note. The family continues. You have made someone's day harder. Possibly their life.", DimColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You create a momentary press of bodies at a narrow point in the gate and get close enough to the girl for thirty seconds. She speaks very quickly and very quietly. The family is being moved as security against her uncle's compliance in a land dispute. It is legal, in the technical sense that all hostage arrangements in noble circles are legal. She has a name for you — the lord arranging this — and a request: tell someone who will write it down. You have heard it. You have written it.", DimColor);
                            break;
                        case "c":
                            ChangeRelWithRandomLord(5);
                            Msg("Your rider carries the livery description to the family's clan seat. Whether they act on it depends on what leverage they have, which you don't know. What you have done is ensure the family's location is known to someone who cares about them. That is not nothing. It may be everything, depending on what was happening.", GoodColor);
                            break;
                        case "d":
                            Msg("You ride on. The escort continues through the gate. The girl's eyes follow your column until the buildings close. Whether it was normal or not is a question that will not answer itself for you.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 147. The Empty Wagon
        private static void LC6_EmptyWagon(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Empty Wagon",
                "A merchant's wagon is stopped at the side of the road outside the gate. Fully loaded, canvas intact, horse still harnessed and calm. The driver is dead at the reins — recently, within the hour. No mark of violence visible at distance. No one else is near it. The wagon has a trade guild seal. Whatever killed the driver was fast and quiet and apparently uninterested in the cargo.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Search the driver and wagon properly.", null, true,
                        "Gain Calculating. Crime +2 if the guild disputes jurisdiction. You learn what happened."),
                    new InquiryElement("b", "Report it to the gate guard and stay until they arrive.", null, true,
                        "Relation +3 with city lord. Costs half a day. Clean resolution."),
                    new InquiryElement("c", "Check the driver for signs of Ashen involvement.", null, true,
                        "Mage instinct: the cold sometimes leaves a mark. Free action."),
                    new InquiryElement("d", "Send one of your men to the guild hall with the description.", null, true,
                        "Gain Honor. No delay for you. The guild handles it."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            ChangeCrime(2f);
                            Msg("The driver has a small wound below the ear, barely visible — a bolt or a needle, fired close. The cargo is untouched but there is a false panel in the wagon's floor. Under it: a sealed crate with a wax mark you don't immediately recognise. Someone was not killing a merchant. They were killing a courier who looked like a merchant. The crate's contents, and whoever they were intended for, are now your problem to decide about.", DimColor);
                            break;
                        case "b":
                            ChangeRelWithOwner(s, 3);
                            Msg("The gate guard records your report and dispatches two men. You wait. They arrive, assess, send for the guild representative. The process takes most of an afternoon. What it reveals is their problem now, not yours. You are clean and a half-day behind where you intended to be.", DimColor);
                            break;
                        case "c":
                            Msg("You place your hand near the driver without touching him. The fire reads the immediate past the way it sometimes can when death was recent. Cold proximity — not weather cold, the other kind. Something Ashen passed this wagon within the last two hours. The driver's death and the Ashen passing may be unrelated. The timing makes that unlikely.", AshenColor);
                            break;
                        case "d":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("Your man rides to the guild hall and delivers the description. The guild sends a factor within the hour. You are already on the road. Whatever the wagon contained and whatever the driver was doing is now in guild hands, which is probably where it belongs. You have your name associated with the report. That will be useful or irrelevant depending on what turns up.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 148. The Confiscation
        private static void LC6_Confiscation(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚖  The Confiscation",
                "City guards are stripping a foreign merchant's cart at the gate — spices, tools, quality cloth. The merchant is arguing in accented but correct legal language: his papers are in order, he has paid the city's gate toll, and the specific goods being confiscated are not on the restricted list. He is right. The guards know he is right. They are continuing anyway, which means they have orders from someone with enough rank that being right doesn't help.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Back the merchant's legal argument as a witnessing lord.", null, true,
                        "Gain Honor. Relation -5 with whoever issued the orders. His goods are returned."),
                    new InquiryElement("b", "Ask the guard sergeant whose orders these are.", null, true,
                        "Gain Calculating. You learn who is running this. Useful intelligence."),
                    new InquiryElement("c", "Compensate the merchant for what's taken and advise him on appeal.", null, true,
                        "Lose 300 gold. Gain Merciful. He can continue his journey."),
                    new InquiryElement("d", "Keep riding. Foreign merchants in local politics is a deep well.", null, true,
                        "Nothing. His goods are taken. His appeal goes nowhere."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ChangeRelWithRandomLord(-5);
                            Msg("You cite the trade code specifically and correctly. The sergeant looks at you, looks at his men, and makes a decision about which authority is more immediately present. The goods are returned. The merchant thanks you formally. The sergeant files a report that will reach whoever issued the orders. They will know a lord intervened. You have made an administrative enemy who does not yet have a face for you.", GoodColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("The sergeant does not want to answer this question in public. That alone is the answer. He names a city official — mid-level, connected to two of the merchant guilds that compete with the foreigner's trade route. This is a commercial suppression dressed as law enforcement. You have the name. The sergeant knows you have it. Everyone continues.", DimColor);
                            break;
                        case "c":
                            ChangeGold(-300);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You pay the value of what's taken and give him the name of the appeal clerk who handles trade disputes with actual authority. He writes both down in a small leather-covered book. He thanks you with the specific efficiency of a merchant: thoroughly, briefly, and while already calculating his next move. He has done this before. He will recover.", GoodColor);
                            break;
                        case "d":
                            Msg("You ride past. The guards continue. The merchant's arguments become less legal and more human as you go. By the time you are out of sight they are finished. His cart is lighter. He will spend three months in appeal and receive a partial settlement. This is the city working as designed.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 149. The Kneel
        private static void LC6_TheKneel(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚜  The Kneel",
                "A man prostrates himself in the street as your party passes — full prostration, forehead to the cobbles, arms forward. This is not feudal formality. This is something older and more specific. Two people nearby step back. The man is shaking, slightly, with an emotion that is not fear. A woman beside him — his wife, probably — is watching you with the expression of someone who has been trying to talk him out of this for some time.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Dismount and raise him up personally.", null, true,
                        "Gain Honor. Morale +5. This will be told and retold."),
                    new InquiryElement("b", "Acknowledge him formally and keep moving.", null, true,
                        "Morale +3. Renown +3. The appropriate response. He is satisfied."),
                    new InquiryElement("c", "Stop and ask him why.", null, true,
                        "The answer changes what you think about your own reputation. Gain Calculating."),
                    new InquiryElement("d", "Ride past without acknowledging it.", null, true,
                        "Lose Honor. Morale -3. Your men see what you did."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            MobileParty.MainParty.RecentEventsMorale += 5f;
                            Msg("You dismount and reach down and bring him to his feet with both hands. He cannot speak. His wife covers her mouth. Your men, who have been watching, are very quiet in the way of soldiers who have decided something about who they serve. You remount and ride on. By nightfall, three versions of this moment are circulating through the column. All of them are true.", GoodColor);
                            break;
                        case "b":
                            MobileParty.MainParty.RecentEventsMorale += 3f;
                            ChangeRenown(3f);
                            Msg("You raise your hand toward him in formal acknowledgement and say his gesture is received. He rises. He is satisfied. The wife relaxes. Your column passes. The man watches until you are out of sight. He went home and told the story correctly, which is the best thing a witness can do.", GoodColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("He says you saved his daughter's village from a raid six months ago and rode on without waiting to be thanked. He heard about it from the headman. He has been in this city three weeks on trade and has been looking for you each day. He wanted you to know someone remembered. You did not know this about yourself — that this is what your reputation contains. You do now.", FireColor);
                            break;
                        case "d":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            MobileParty.MainParty.RecentEventsMorale -= 3f;
                            Msg("You ride past. He rises slowly without help, watching your column go. Your men have been riding for two minutes before the silence in them develops a specific quality. Nobody says anything. Nobody needs to.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 150. The Mercenary Terms
        private static void EB5_MercenaryTerms()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  The Mercenary Terms",
                "A surviving mercenary captain — his company is broken, he has perhaps thirty men left standing — approaches your lines under a white cloth. He offers surrender and immediate service. He says this without hesitation or performance: his contract ended when the army he was fighting for broke, and he is a professional. He wants to know your terms before his men decide for themselves. He has thirty minutes before they stop listening to him.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Accept him and his men — survivors who fight through a defeat are useful.", null, true,
                        "Morale +5. Gain thirty veteran soldiers. Honor +1."),
                    new InquiryElement("b", "Accept, but only after he tells you what his contract was and who held it.", null, true,
                        "Gain Calculating. Morale +3. You take the men and the intelligence."),
                    new InquiryElement("c", "Offer them safe passage and pay — no service required.", null, true,
                        "Lose 500 gold. Gain Honor. They leave. Some will find their way back to you later."),
                    new InquiryElement("d", "Decline and let them go. You do not take defeated mercenaries as a practice.", null, true,
                        "Honor +1. They disperse. No ongoing complication."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                        {
                            ShiftTrait(DefaultTraits.Honor, 1);
                            MobileParty.MainParty.RecentEventsMorale += 5f;
                            var t_eb5 = MBObjectManager.Instance.GetObject<CharacterObject>("imperial_infantry")
                                     ?? MBObjectManager.Instance.GetObject<CharacterObject>("watchman")
                                     ?? MBObjectManager.Instance.GetObject<CharacterObject>("sea_raider")
                                     ?? MBObjectManager.Instance.GetObject<CharacterObject>("looter");
                            if (t_eb5 != null) try { MobileParty.MainParty.MemberRoster.AddToCounts(t_eb5, 30); } catch { }
                            Msg("He gives the terms to his men. Three walk away. The rest accept. They fold into your column with the quiet efficiency of people who know how to be useful in an army. Your veterans test them in the first hour — small things, competence checks — and file their reports informally. The verdict is: solid. The captain takes his place in the order of march without being told where it is. He already knows.", GoodColor);
                            break;
                        }
                        case "b":
                        {
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            MobileParty.MainParty.RecentEventsMorale += 3f;
                            var t_eb5b = MBObjectManager.Instance.GetObject<CharacterObject>("imperial_infantry")
                                      ?? MBObjectManager.Instance.GetObject<CharacterObject>("watchman")
                                      ?? MBObjectManager.Instance.GetObject<CharacterObject>("sea_raider")
                                      ?? MBObjectManager.Instance.GetObject<CharacterObject>("looter");
                            if (t_eb5b != null) try { MobileParty.MainParty.MemberRoster.AddToCounts(t_eb5b, 30); } catch { }
                            Msg("He answers without hesitation: contracted to a lord two kingdoms over, passed through three intermediaries, the final payment never arrived. He tells you the lord's name and the reason the contract was placed. The information explains something about a troop movement you heard about last month. You take his men. The intelligence is worth as much as they are.", DimColor);
                            break;
                        }
                        case "c":
                            ChangeGold(-500);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You pay them a week's rate each and grant passage. The captain looks at the coin and then at you with the expression of a man recalibrating. He thanks you without performance. They disperse south. Two of them will come back to your banner within the year — not because of the money but because of what the money meant about who gave it.", GoodColor);
                            break;
                        case "d":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You decline. He nods once — he expected this to be a possibility and prepared for it. He goes back to his men and gives them the news. They break into groups and head in three directions. They are professionals and they know where the next contract is. You watch them go with the mild regret of a decision that was correct and still cost something.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 151. Your Standard in Enemy Hands
        private static void EB5_OwnStandard()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  Your Standard in Enemy Hands",
                "Among the captured enemy effects: your own banner, folded in a wax-sealed case. Not a copy. The actual standard, with the specific repairs from two campaigns ago. Someone in your party gave it to the enemy, or sold it, or it was taken in circumstances nobody reported. Your sergeant is standing very still when he shows it to you. He has already started a list.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Let your sergeant run the inquiry — he will be thorough and correct.", null, true,
                        "Gain Calculating. Morale -3 during investigation. The traitor is found."),
                    new InquiryElement("b", "Run the inquiry yourself, personally.", null, true,
                        "Gain Calculating. Honor +1. Morale -5. You find the answer. It is not what you expected."),
                    new InquiryElement("c", "Burn the standard and say nothing — you do not want to know.", null, true,
                        "Nothing mechanical. Your sergeant's face when you say this will remain with you."),
                    new InquiryElement("d", "Ask the captured enemy officers directly how they got it.", null, true,
                        "50/50: they know and tell you, or they don't know and you learn something worse."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            MobileParty.MainParty.RecentEventsMorale -= 3f;
                            Msg("Your sergeant works through the list in two days. It was a sutler — not a soldier, a camp follower, someone no one looked at. He had taken the standard from a storage chest during the confusion of the last march and sold it to an intermediary. He is gone; he ran when the inquiry started. Your sergeant gives you the name. The list of what he had access to is longer than the standard.", DimColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            MobileParty.MainParty.RecentEventsMorale -= 5f;
                            Msg("You conduct the interviews yourself. You find the answer on the second day: a veteran of eleven years, decorated twice, who sold it for a debt he could not pay by any other means. He does not deny it. He does not make excuses. He tells you the exact amount and the exact day. You have known this man for three campaigns. The inquiry ends with that conversation and does not get easier after it.", BadColor);
                            break;
                        case "c":
                            Msg("You tell your sergeant to burn it. He holds the standard for a moment and then does as you say, without expression. He files no report. He says nothing. For three weeks after this, he is slightly more formal with you than before, and you understand exactly why, and there is nothing you can do about it.", DimColor);
                            break;
                        case "d":
                            if (_rng.Next(2) == 0)
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                Msg("The senior prisoner knows: it was given to their commander by a rider with no livery, three weeks before the battle, with a note attached that described your march route. Someone with access to your plans gave the standard as proof of relationship. The enemy commander is dead. The rider's description is of nobody you can immediately place. The route information was accurate.", DimColor);
                            }
                            else
                            {
                                Msg("They don't know. Their quartermaster received it in a crate of captured effects from a different engagement entirely — it has been in their supply train for six months and nobody knew whose it was. It was taken in a minor skirmish that killed seven of your rear guard. An engagement nobody reported to you as involving your standard. Someone failed to report it. That is a different problem.", DimColor);
                            }
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 152. The Enemy Surgeon
        private static void EB5_EnemySurgeon()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✚  The Enemy Surgeon",
                "Your surgeon comes to you with a specific request: the enemy field surgeon, taken prisoner, has been working on your wounded alongside him for the last three hours and is better than competent — he has saved two men your own surgeon was not certain about. He wants to keep him working. He is technically a prisoner. His continued presence requires your explicit permission and carries legal complications if this goes poorly.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Grant it. A surgeon working is a surgeon not rotting in a prison camp.", null, true,
                        "Gain Merciful. Honor +1. Morale +5. Some men owe him their lives."),
                    new InquiryElement("b", "Grant it on the condition he is released when the wounded are stable.", null, true,
                        "Gain Honor. Morale +3. He accepts. He works. He leaves with his parole intact."),
                    new InquiryElement("c", "Decline. He is a prisoner and the arrangement could be misused.", null, true,
                        "Nothing. Your surgeon accepts the decision. He will save fewer men today."),
                    new InquiryElement("d", "Ask the surgeon what he wants in exchange for his cooperation.", null, true,
                        "Gain Calculating. His terms are specific and reasonable. Agreement has consequences."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            MobileParty.MainParty.RecentEventsMorale += 5f;
                            Msg("He works through the night. In the morning three men who were dying are stable. Your surgeon reports this to you with the specificity of a man who has been counting. The enemy surgeon asks to speak to you before leaving — he wants to say that in twenty years he has not worked alongside another surgeon who made his decisions the way your man does. That is the only thing he says. He is released at noon.", GoodColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            MobileParty.MainParty.RecentEventsMorale += 3f;
                            Msg("He accepts the terms and returns to work immediately. When the last serious case is stable he comes to you directly, states that his obligation is fulfilled, and thanks you for the terms. He means it. He walks out of your camp with his equipment and his parole and the particular dignity of a man whose professional self has been treated as real. Your surgeon watches him go and says nothing, which is how your surgeon expresses approval.", GoodColor);
                            break;
                        case "c":
                            Msg("Your surgeon receives the decision and returns to work. He will do what he can. Two men he was uncertain about do not make it through the night. Whether the enemy surgeon would have changed that outcome is not a question with a clean answer, but it is a question that will be in your surgeon's face for the next few days.", DimColor);
                            break;
                        case "d":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("He wants a letter of passage signed by a lord stating he was taken, treated fairly, and released on parole — documentation that protects him if he is accused of collaboration by his own side. The request is entirely reasonable. You write the letter. He works. Three men live. The letter costs you nothing but the writing and may cost him nothing or everything depending on how his own army treats its returning surgeons.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 153. The Young Officer
        private static void EB5_YoungOfficer()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  The Young Officer",
                "An enemy officer — seventeen, perhaps — is sitting at the field's edge with a broken arm set crudely and a look on his face that is not quite shock and not quite composure. He has a lieutenant's mark. He was in his first command. He is deciding whether to ask something or say nothing, and he has been deciding this since your patrol found him. He has no weapon.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "See that his arm is properly set and release him.", null, true,
                        "Gain Merciful. Honor +1. Morale +3. He will remember who set his arm."),
                    new InquiryElement("b", "Speak to him — he saw the battle from the enemy's command position.", null, true,
                        "Gain Calculating. Intelligence value. He is shaken enough to answer honestly."),
                    new InquiryElement("c", "Process him as a prisoner of rank — ransom is the protocol.", null, true,
                        "Gain gold via ransom eventually. Standard outcome."),
                    new InquiryElement("d", "Ask him why he was fighting against you specifically.", null, true,
                        "Flavor. His answer is more complicated than the battle was."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            MobileParty.MainParty.RecentEventsMorale += 3f;
                            Msg("Your surgeon resets the arm properly. You give him water and point him north. He asks why. You tell him because the war ends eventually and people remember how they were treated when it was over. He thinks about this for a moment with the particular seriousness of someone very young encountering an idea that will reorganise several years of thought. He thanks you and walks north, holding his arm.", GoodColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("He answers your questions with the specific honesty of someone in mild shock. He tells you about his commander's planned second position, the reserve force's size and location, and the reason the centre held longer than it should have. He does not realise he is giving you operational intelligence — he thinks he is explaining the battle. Both things are true. You release him afterward with his arm set. He was never going to withhold anything.", DimColor);
                            break;
                        case "c":
                            ChangeGold(200);
                            Msg("He is catalogued and held. His family pays within the month — a minor noble house, stretched, but capable. He is released in good condition. His family receives him. His first letter home, which you will not read, includes something about the circumstances of his capture that is not inaccurate. The ransom was fair. The process was correct. He will remember both.", DimColor);
                            break;
                        case "d":
                            Msg("His family lost land in the previous generation to someone who serves your faction. He was recruited from a position of obligation, not choice, and rose to lieutenant because he was competent and because his commander needed officers who would not ask questions. He is not ideological. He is employed. He tells you this without self-pity. It is a more common story than the ballads suggest.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 154. The Archives
        private static void ES5_Archives()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚒  The Archives",
                "In the keep's lower level: a room of records, ledgers, correspondence — years of it. Someone is already burning them. Not a soldier, a clerk: methodical, moving through the shelves in order, using a small lamp. He has been at this since before your men entered the building. He looks up when you enter and does not run. He is doing his job.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Stop him — everything in this room is now evidence.", null, true,
                        "Gain Calculating. Crime -5 with previous lord's faction. You save most of it."),
                    new InquiryElement("b", "Ask him what he has already burned before stopping him.", null, true,
                        "Gain Calculating. You learn what is gone. Save what remains."),
                    new InquiryElement("c", "Let him finish, but take one ledger at random first.", null, true,
                        "50/50: the ledger is significant or mundane. He finishes. You have one thing."),
                    new InquiryElement("d", "Let him finish. You did not take this keep to read correspondence.", null, true,
                        "Nothing. Whatever the records contained is ash."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            ChangeRelWithRandomLord(-5);
                            Msg("You stop him and have him sit against the wall while your people secure the room. He cooperates without protest. He is a professional and the room has changed hands — he understands this. What survives is three years of the previous lord's accounts, correspondence with two lords you are interested in, and a ledger of payments that includes at least four names you would not have expected to find there.", GoodColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("He tells you, precisely, in order: the personal correspondence goes first, then the financial records marked private, then the garrison orders. He is already past the first two categories. You stop him on the garrison orders. What is ash contained the previous lord's private communications and his private accounts. What you have is operational. It is enough to understand what was happening here, if not why.", DimColor);
                            break;
                        case "c":
                            if (_rng.Next(2) == 0)
                            {
                                ChangeRenown(8f);
                                Msg("The ledger you take at random is the private accounts — the one the clerk was burning toward specifically. What it contains explains several things about this lord's relationships with his neighbours that will be useful for a long time. The clerk watches you leave with it and says nothing. He knows which ledger you took.", GoodColor);
                            }
                            else
                            {
                                Msg("The ledger is grain inventory from six seasons ago. Accurate, mundane, useless. The clerk finishes his work. The room is ash. Whatever the records contained is gone. The grain inventory will tell you nothing except that this lord counted his harvest carefully.", DimColor);
                            }
                            break;
                        case "d":
                            Msg("He finishes. The room is ash. He gathers his lamp and leaves without being asked. He is already employed by whoever holds this keep next — that is what clerks are, and it is useful to know that.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 155. The Collaborator
        private static void ES5_Collaborator()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Collaborator",
                "A city administrator — tax assessor, title of record — who served the previous lord is present in the hall when you enter the keep. He has already prepared a summary of accounts and is offering his services. Three village headmen are also present, having come in with your soldiers. One of them recognises the administrator and says, in a flat voice, his name, and what he did: he organised the levy suppression that starved four villages two winters ago. The administrator does not deny it. He says he had orders.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Hold him for formal inquiry — what he did requires a judge, not a battlefield decision.", null, true,
                        "Gain Honor. Gain Calculating. The headmen are satisfied with process. He is not released."),
                    new InquiryElement("b", "Dismiss him from service and ban him from the city — exile, not execution.", null, true,
                        "Gain Merciful. Honor +1. The headmen accept this, mostly."),
                    new InquiryElement("c", "Keep him on. He knows the accounts and you need the administration to function.", null, true,
                        "Gain Calculating. Lose Honor. The headmen leave the hall without speaking."),
                    new InquiryElement("d", "Give the headmen the decision — this is their city now, not his.", null, true,
                        "Gain Honor. The headmen confer for ten minutes. Their decision is clear and consistent."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You name a date and a judge and have him held. The headmen's faces change — not to happiness but to something more durable than happiness. They have been given the form of justice rather than its result, which is how justice usually works and is usually better than the alternative. The administrator sits in his cell with the accounts he prepared, which will be used against him. This is appropriate.", GoodColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You revoke his title and his city access in the same breath and have him out of the gate within the hour. The headmen watch him go. Two of them nod. One doesn't — he thinks it is not enough. He is probably correct. The man is alive and somewhere else, which is more than the villages he suppressed can say about the two years he organised against them.", GoodColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("You tell him his services are retained. The headmen leave the hall without looking at you. The administrator begins briefing you on the accounts with the specific efficiency of a man who has survived transitions before and knows the value of making himself immediately useful. He is, in fact, very useful. That is the entire problem.", BadColor);
                            break;
                        case "d":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("The headmen confer quietly in the corner for ten minutes. Then the eldest comes to you and says: dismissed, banned, compensated — meaning the villages receive a portion of his accumulated salary as partial reparation. They have already calculated the amount. You approve it. The administrator is walked out. The headmen return to their villages. The accounts are in a chest waiting for whoever you appoint next.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 156. The Hidden Gold
        private static void ES5_HiddenGold()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚒  The Hidden Gold",
                "Your men found a secondary cache beneath the keep's stables — significantly larger than the treasury your quartermaster recorded. A floor panel, recently replaced. The gold is real and unregistered. Your sergeant reports it to you privately, which means he has not reported it generally, which means he is waiting to understand what you intend before deciding what he knows. Six of your veterans know it exists. None of them have said anything yet.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Declare the full amount and add it to the official inventory.", null, true,
                        "Gain Honor. Morale +3 — your men expected this. Nothing irregular."),
                    new InquiryElement("b", "Declare most of it and give your sergeant's men a finder's share.", null, true,
                        "Lose Honor slightly. Morale +8. Loyalty from those six specifically."),
                    new InquiryElement("c", "Declare none of it — this cache predates any legal ownership.", null, true,
                        "Gain significant gold. Honor -2. Your sergeant files what he is told to file."),
                    new InquiryElement("d", "Ask your sergeant what he thinks should happen with it.", null, true,
                        "Gain Calculating. His answer tells you something about what your men think you are."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            MobileParty.MainParty.RecentEventsMorale += 3f;
                            Msg("You declare the full amount. Your sergeant writes it down with the specific expression of a man whose guess about you has been confirmed. The six veterans hear about it by evening. None of them comment on it directly. The morale effect of a correct expectation is different from a surprise and more lasting. They knew you would do this. They are glad they were right.", GoodColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            MobileParty.MainParty.RecentEventsMorale += 8f;
                            Msg("You declare most of it, give the remainder to the six as a finder's share — significant, enough to matter — and watch what happens to their faces. The irregularity is small enough that your quartermaster will not press it. The loyalty from those six is specific and durable. Your sergeant thanks you without performing gratitude. He means it in the particular way of someone who asked for less than this and received more.", GoodColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, -2);
                            ChangeGold(1200);
                            Msg("You declare nothing. Your sergeant files the treasury as found and the cache as not found. The six men receive their standard shares. They know. They will not say anything. But they know, and the knowledge changes something small and permanent in how they think about this campaign and this command. The gold is useful. The cost is harder to count.", BadColor);
                            break;
                        case "d":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("Your sergeant thinks for a moment, then says: declare it and give the men their finder's cut. He says this as a suggestion, not a demand, which means it is actually a test. He wants to know which lord you are. You have the information now. What you do with it is the answer.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 157. The Torturer
        private static void ES5_Torturer()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚒  The Torturer",
                "In the garrison, waiting with the other soldiers to be processed: a man your sergeant identifies quietly by reputation, not by rank. The previous lord's dedicated interrogator. Not a soldier — a specialist, titled, salaried. There are people in the city who remember his face and his work. He is standing in line with everyone else, waiting. He has not tried to flee or to hide, which is either professional confidence or the specific resignation of a man who has been expecting this day.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Separate him from the others and hold him for a formal accounting.", null, true,
                        "Gain Honor. The city will hear about this. Relation +5 with the population."),
                    new InquiryElement("b", "Dismiss him from service and expel him from the city immediately.", null, true,
                        "Gain Honor. Clean break. He leaves. What he does next is his."),
                    new InquiryElement("c", "Interrogate him yourself — he knows things about the previous lord's operations.", null, true,
                        "Gain Calculating. You learn significant intelligence. The irony is not lost on either of you."),
                    new InquiryElement("d", "Process him the same as the other soldiers — rank and service, not specialisation.", null, true,
                        "Lose Honor. The city learns you did this. They do not forget."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ChangeRelWithRandomLord(5);
                            Msg("You pull him out of line personally and have him held separately. The other soldiers watch this with the specific attention of people determining what kind of administration is beginning. By evening, three city residents have come to the gate to ask about the process. They have names of people who were taken to the lower rooms. You have started something that will require a judge and several months. You have started it correctly.", GoodColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You revoke his commission, confiscate his title, and have him out of the gate before noon. He goes without protest. He has prepared for this. His preparation included identifying where he will go next, which is not your jurisdiction. The city knows you expelled him. They are satisfied with the expulsion in the way people are satisfied by a thing that was too late but was still done.", GoodColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("He answers your questions with professional directness. He tells you about the previous lord's network, the payments, the correspondence, and three names who were not imprisoned but should have been. He does not attempt to bargain. He gives you everything you ask for with the efficiency of a man who has decided that cooperation is the correct strategic response to his situation. You use the information. The irony of the method sits between you and neither of you names it.", DimColor);
                            break;
                        case "d":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            ChangeRelWithRandomLord(-5);
                            Msg("He is processed as a soldier. He is released with the others at standard terms. Three city residents come to your gate the next morning, separately, to ask about his status. When they are told he was released they leave without speaking. Whatever they say afterward, and they say something, travels faster through a city than any official announcement.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 158. The Apiary
        private static void ER5_Apiary()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✿  The Apiary",
                "At the village's edge: a beekeeper, perhaps sixty, standing in the ash of what were twelve hives. He is counting what is left — one hive, cracked but intact, the bees confused but alive. He does not look up when you approach. He is doing arithmetic about years: how long it took to build, how many years he has left, whether the numbers allow what the numbers would need to allow. You can see the calculation in his face.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Give him enough gold to rebuild in full.", null, true,
                        "Lose 600 gold. Gain Merciful. The arithmetic changes. He looks at you differently."),
                    new InquiryElement("b", "Sit with him for a moment without trying to fix it.", null, true,
                        "Gain Honor. Morale +3. Your men see this. No material change."),
                    new InquiryElement("c", "Give him what you have on you and ride on.", null, true,
                        "Lose 200 gold. Gain Merciful. Not enough. But something."),
                    new InquiryElement("d", "Tell him you will send compensation through your quartermaster.", null, true,
                        "Lose 400 gold. Gain Honor. He will receive it. He does not know if he believes you."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeGold(-600);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You give him the full amount, in coin, with no conditions. He counts it — not because he doubts you but because counting is how he processes things, it is a habit of precision that runs through his whole life. The arithmetic changes. He looks up at you for the first time. He says: five years, if the remaining hive holds and the season is reasonable. That is a different sentence than the one he was calculating before.", GoodColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            MobileParty.MainParty.RecentEventsMorale += 3f;
                            Msg("You dismount and stand beside him in the ash for a few minutes without speaking. Your men watch from the road and do not hurry you. He does not thank you — there is nothing to thank. But he stops counting for a moment, which may be what sitting with someone in a loss actually accomplishes. You remount and rejoin your column. Something about the way your men fall back into march is different.", GoodColor);
                            break;
                        case "c":
                            ChangeGold(-200);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You empty your belt purse into his hands. It is not enough for twelve hives, not close. He knows this and you know this and neither of you says it. He nods once. You ride on. Something is better than the alternative, and the alternative was nothing.", DimColor);
                            break;
                        case "d":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ChangeGold(-400);
                            Msg("You give your quartermaster the instruction before you leave. The money arrives in three days with your seal on the order. The beekeeper receives it, holds it, and reads the seal twice. He puts it somewhere careful. He begins clearing the ash on the fourth day. He does not know, yet, whether to believe that this is how it works. He will find out.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 159. The Name List
        private static void ER5_NameList()
        {
            bool mage = MageKnowledge.IsMage;
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Name List",
                "Among the burned papers, your sergeant finds a fragment that survived: a list of names in the Ashen script, partially legible, with a second column of annotations. Three of the names can be matched to villagers here — alive, present, currently watching your men clear the site. The annotations suggest observation records, not recruitment. These people were being watched. They do not know this. The question is whether to tell them, and whether that changes their safety or reduces it.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Tell them what was found and what it means.", null, true,
                        "Gain Merciful. They can protect themselves. They will also be afraid."),
                    new InquiryElement("b", mage ? "Read the fire-echo of who compiled this list." : "Keep the list and have the village watched for Ashen contact.", null, true,
                        mage ? "Costs 1 day. You identify the Ashen agent who wrote it." : "Gain Calculating. Ashen surveillance countered with your own."),
                    new InquiryElement("c", "Report it to the nearest lord with jurisdiction and let the garrison handle it.", null, true,
                        "Gain Honor. Relation +5. Correct process. Slow process."),
                    new InquiryElement("d", "Burn what remains. Knowing they were watched and knowing why are different things.", null, true,
                        "Nothing. The surveillance stops here. The reason for it does not."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You tell the three villagers separately, privately, what the annotations mean. Each of them receives it differently: one nods immediately as if confirming something they suspected, one asks what they did to be watched, one cannot speak. You tell them what the Ashen observation record suggests — proximity to trade routes, nothing personal. It is probably the truth. Probably is the best you can offer. They have it now. Whether it keeps them safer depends on what they do with it.", AshenColor);
                            break;
                        case "b":
                            if (mage)
                            {
                                AgingSystem.AgeHero(Hero.MainHero, 1);
                                Msg("You put your hand on the fragment and let the fire look back along the cold trace of the writing. The agent who compiled this list was in this village eight days ago, cataloguing. The face you receive is specific: a woman, unremarkable, who trades in cloth and moves between villages on a regular circuit. You have her route and her face. She does not know you have either.", AshenColor);
                            }
                            else
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                Msg("You leave two men with instructions: watch the three named villagers for external contact, note anyone who asks about the raid's aftermath specifically. No intervention until you know more. What the list describes is Ashen preparation, not action. Counter-surveillance buys time and information. Your men know how to do this without being noticed. Probably.", DimColor);
                            }
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ChangeRelWithRandomLord(5);
                            Msg("The report goes out with a full transcription of the fragment. The garrison responds in a week — they are aware of this Ashen observation pattern in the region and have two other fragments from other raids. Your contribution completes a picture they were missing. The three villagers are contacted by the garrison and warned, formally, six weeks after you leave. The process was correct. The timeline was not ideal.", DimColor);
                            break;
                        case "d":
                            Msg("You burn what remains. The three villagers continue their lives watched by something that no longer has a record here but still has an interest. Whether the interest expires with the observation or persists without the record is a question that will answer itself in its own time and probably not yours.", DimColor);
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
                        "Ashen party spawns at gate. You choose the ground."),
                    new InquiryElement("b", "Warn the village headman and set a watch — catch them if they move.", null, true,
                        "Ashen party spawns at gate, weakened — they lost the advantage. Relation +5."),
                    new InquiryElement("c", "Slip into the village without alerting them. Let them think you didn't notice.", null, true,
                        "Gain Calculating. They remain. You know they're there. They don't know you know."),
                    new InquiryElement("d", "Ride in openly and loudly. Let them see your column's full size.", null, true,
                        "50/50: they retreat or decide size doesn't matter to them."),
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
                                Msg("The column's size reads correctly. The scouts withdraw north in the specific unhurried way of a thing that was not afraid — it simply calculated and left. They will report your position and your number. That information is now travelling.", DimColor);
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
                        "Costs 1 day. He understands something he cannot un-understand. Deep lore exchange."),
                    new InquiryElement("b", "Challenge him formally — demonstrate the difference by working the same problem.", null, true,
                        "Gain Calculating. You both learn something. His gift is real and different."),
                    new InquiryElement("c", "Let him demonstrate first. Eight years of self-teaching is worth hearing.", null, true,
                        "Flavor. He shows you something you genuinely didn't know. Morale +3."),
                    new InquiryElement("d", "Agree with him — the gift finds what it finds. He's not wrong about that.", null, true,
                        "Gain Honor. He is surprised. You leave him with something to reconsider."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
                            Msg("You set something on the table between you — not a trick, not a demonstration, just the fire being what it is without the performance layer. He watches it for a long time. His own fire responds to it in a way that surprises him. He doesn't revise his opinion out loud. He revises it where opinions actually live. You ride out with a day of your life spent on a genuine exchange.", FireColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You give him a problem: heat a specific point without warming what surrounds it. He solves it differently than you would — more slowly, with more control over the margins. You solve it faster and with less precision. You both sit with this for a moment. He is not lesser. He is different. The difference matters in specific contexts. Neither of you had clearly understood that before.", FireColor);
                            break;
                        case "c":
                            MobileParty.MainParty.RecentEventsMorale += 3f;
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
                        "Costs 1 day. She receives it. The knowledge travels forward."),
                    new InquiryElement("b", "Answer but challenge her framing — the question has a better version.", null, true,
                        "Gain Calculating. She returns with a better question. Rare lore exchange."),
                    new InquiryElement("c", "Test her first — see what she actually carries before deciding what she can receive.", null, true,
                        "Costs 1 day. Her gift is real and specific. Your test reveals something unexpected."),
                    new InquiryElement("d", "Tell her she needs to find the answer herself — it won't mean anything received.", null, true,
                        "Gain Honor. She is frustrated. She is also right to be frustrated. Both things are true."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
                            Msg("The question was: can the fire grieve. You answer it honestly, which costs more than you expected, because the honest answer requires showing her the specific moment yours did. She receives it with the attention of someone who has been carrying a wrong assumption for years and can now correct it. She thanks you simply. She will teach what you gave her. It will carry your name, eventually, in the way that knowledge carries names: silently and without permission.", FireColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You tell her the question she asked is a narrower version of a better question. She sits with this and produces the better question in about four minutes, which tells you everything about the quality of her training. The better question neither of you can answer fully. You spend an hour working at its edges. The master taught her well. What she does next with it will be hers.", FireColor);
                            break;
                        case "c":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
                            Msg("You give her a working to complete — not a display, a test of understanding. She completes it three-quarters correctly and then does something you didn't expect: corrects herself mid-working without stopping, which is harder than getting it right the first time and significantly rarer. Her gift is real and her control is better than yours was at her age. The answer you give her afterward is the best version you have. She is ready for it.", FireColor);
                            break;
                        case "d":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You tell her to find it herself. She is frustrated in the specific way of someone who has been told a truth they did not want. She asks why. You tell her: because the answer you find is yours; the answer I give you is mine, and mine won't fit where yours needs to go. She sits with this and does not thank you. She will find the answer. When she does, it will be shaped correctly. You will not be there to see it.", GoodColor);
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
                        "Ashen party spawns at gate. You're ready. They're not."),
                    new InquiryElement("b", "Alert the city guard — if they're asking about a lord, the lord's guard should know.", null, true,
                        "Relation +5. They scatter. You lose the confrontation but gain the city's cooperation."),
                    new InquiryElement("c", "Have your people track them while you conduct your business normally.", null, true,
                        "Gain Calculating. Your people follow them to a location. You learn something about their network."),
                    new InquiryElement("d", "Do your business and leave faster than expected.", null, true,
                        "They adjust. Ashen party spawns at gate — smaller, less prepared, but still there."),
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

        // ── ENTER CITY: The Duelist (mage, renown ≥ 400) ──────────────────
        private static void EC7_TheDuelist(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  The Challenge",
                "A mage in the city square calls your name loudly enough for the crowd to hear. He is theatrical about it — cloak, posture, timing. He challenges you to demonstrate your gift against his, publicly, in the market. He has an audience that is now watching you for your reaction. He may be good. He may be performing. He is definitely committed.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Accept. Meet him in the square on your own terms.", null, true,
                        "Costs 1 day. Renown +15. The crowd witnesses something real."),
                    new InquiryElement("b", "Accept but set the terms yourself — something that tests control, not spectacle.", null, true,
                        "Costs 1 day. Renown +10. Honor +1. He performs less well in control tests."),
                    new InquiryElement("c", "Decline formally and publicly — not every challenge deserves the ground it's offered on.", null, true,
                        "Renown +5. Honor +1. The crowd reads this correctly. He does not."),
                    new InquiryElement("d", "Ask him, in front of the crowd, what he actually wants.", null, true,
                        "Gain Calculating. His answer is more honest than the challenge was. Something changes."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
                            ChangeRenown(15f);
                            Msg("He is good — better than the theatrics suggested. The crowd watches the square generate more heat than it has in years. He ends the contest first, which is his version of honour: yield before it becomes defeat. The crowd has seen something. They will describe it inaccurately for years. You will be larger in each telling.", FireColor);
                            break;
                        case "b":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
                            ChangeRenown(10f);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You name the test: sustain a working at precise temperature for sixty counted seconds, nothing added, nothing removed. He agrees. He lasts forty. You last seventy-two. Control is not what he trained for — he trained for display. The crowd is less entertained and more impressed. He takes the result with better grace than you expected.", GoodColor);
                            break;
                        case "c":
                            ChangeRenown(5f);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You tell him and the crowd that the square is not the right ground for what he's describing, and that a challenge offered for an audience's benefit rather than an honest purpose is something different than a duel. The crowd receives this. He argues with it briefly, then quiets. He wanted your attention. He has it, briefly, and it is not what he planned.", GoodColor);
                            break;
                        case "d":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("The crowd goes quiet because the question was not what anyone expected. He hesitates, then answers honestly: he has a technique he cannot finish alone and needs someone at your level to complete it with him. The challenge was the only approach he thought you would respond to. He is not wrong about that. You spend an afternoon on the technique instead. The crowd disperses disappointed. The working is completed.", FireColor);
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
                        "They approach. A cold exchange follows. Intel or threat — you'll find out."),
                    new InquiryElement("b", "Move through the gate without acknowledgement — you have seen each other, nothing more.", null, true,
                        "Gain Calculating. Mutual awareness, no confrontation. They track your movements."),
                    new InquiryElement("c", "Signal hostility. You do not want them tracking your column.", null, true,
                        "Ashen party spawns at gate. You chose the fight. So did they."),
                    new InquiryElement("d", "Send someone to them with a single question: why are you here?", null, true,
                        "50/50: they answer, or your messenger returns alone and the gate suddenly has fewer grey cloaks."),
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
                            Msg("You walk past. They watch you go with the patience of something that has time. Your column tracks their position as it passes. They stay visible long enough to confirm they are not hiding — they want you to know they are watching. You do your business in the city with the specific focus of a person who is also being filed away in someone else's record.", AshenColor);
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

        // ── LEAVE VILLAGE: The Road Watches Back ───────────────────────────
        private static void LV7_RoadWatchesBack(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Road Watches Back",
                "Your outriders report movement in the treeline — three positions, coordinated, moving with your column's speed without getting closer. Not wildlife. Not bandits: bandits would have committed or retreated by now. They are pacing you, which means they are waiting for something. The gate is twenty yards behind you. The treeline continues for two miles ahead.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Turn and engage on your own terms while you still have the settlement behind you.", null, true,
                        "Ashen party spawns at gate. You chose the ground. Better than theirs."),
                    new InquiryElement("b", "Accelerate through the ambush corridor — make their timing wrong.", null, true,
                        "50/50: you clear the corridor or they commit early and intercept."),
                    new InquiryElement("c", "Send flankers into the treeline — remove the threat before it matures.", null, true,
                        "Smaller Ashen spawn. Your flankers found some of them first."),
                    new InquiryElement("d", "Return to the settlement and leave by a different route at a different hour.", null, true,
                        "Safe. Costs half a day. The watchers are gone when you leave at dusk."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            SpawnAshenAtGate(s, 12, 55f);
                            Msg("You halt the column and wheel back toward the settlement, forcing the watchers to commit before they are ready. They come out of the trees with the unhurried aggression of something that adjusted its plan in half a second. They are at the gate with full numbers and less positioning than they wanted. You chose better ground. You will need it.", WarnColor);
                            break;
                        case "b":
                            if (_rng.Next(2) == 0)
                                Msg("The acceleration disrupts their timing. They commit early, from two positions instead of three, and your column's speed means the intercept is partial. You clear the corridor with two of your people wounded and one of theirs left behind. The one they left behind is dead. The other two positions dissolved north. You are through.", GoodColor);
                            else
                            {
                                SpawnAshenAtGate(s, 10, 50f);
                                Msg("They adjusted faster than expected. The third position moved to close the exit ahead of you. You are back at the gate, formation intact, on worse terms than when you started. They are patient. They have been patient before.", WarnColor);
                            }
                            break;
                        case "c":
                            SpawnAshenAtGate(s, 6, 30f);
                            Msg("Your flankers reach the treeline and find two positions. The third was further back than expected and sees the flankers moving. The two found positions break toward the road — toward you, not away. Your flankers engage their rear. The survivors reach the gate. Smaller than the original group. Still requiring an answer.", WarnColor);
                            break;
                        case "d":
                            Msg("You return to the village and explain the delay to the headman without detail. You leave by the mill road at dusk in a column of three, the rest following at intervals. The treeline is empty when you check it from the road. The watchers had a specific window and you did not fill it. They will report your route change. You are ahead of the report by half a day.", GoodColor);
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
                        "Ashen party spawns at outer gate. You catch them before they disperse."),
                    new InquiryElement("b", "Raise the alarm first, then pursue — the city guard needs to know a gate guard is dead.", null, true,
                        "Relation +5 with city lord. Ashen party spawns at gate, weaker — guard joins the fight."),
                    new InquiryElement("c", "Follow one of them without engaging — find out where they go.", null, true,
                        "Gain Calculating. You tail the slowest one. The location they reach is more informative than the fight."),
                    new InquiryElement("d", "Seal the gate and hold position — let them come to you on your ground.", null, true,
                        "Ashen party spawns at gate. You hold the chokepoint. The numbers favor you here."),
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

        // ── LEAVE CITY: The Ashen Challenge (ashen + mage gated) ───────────
        private static void LC7_AshenChallenge(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Cold Gauntlet",
                "At the outer gate, an Ashen mage intercepts you alone — no escort, no weapons drawn. They state what they want clearly: a cold duel. Not a fight. A test of endurance: your warmth against their cold, in contact, until one yields. They say this the way one professional addresses another. They have done this before. They are not asking to harm you. They are asking to measure you.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Accept. You want to know what they are as much as they want to know what you are.", null, true,
                        "Costs 1 day. Renown +20. Ashen intel. Honor +1. The measurement goes both ways."),
                    new InquiryElement("b", "Accept but set conditions — a formal witness, a time limit, both sides bound by the result.", null, true,
                        "Costs 1 day. Renown +15. Honor +1. The Ashen mage respects the terms."),
                    new InquiryElement("c", "Counter-challenge: your test, not theirs. Demonstrate warmth, not endurance.", null, true,
                        "Costs 1 day. Morale +5. Renown +10. They accept the counter. The results are unexpected."),
                    new InquiryElement("d", "Decline. You do not owe the Ashen a measurement.", null, true,
                        "Lose Honor. They accept the refusal without argument. They already have data from the refusal."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
                            ChangeRenown(20f);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You make contact. The cold is a presence — not temperature, something older, the specific absence that the Ashen carry where warmth should be. You hold against it. They hold against yours. For ninety seconds neither of you moves. Then they release, slowly, and step back. They say one thing: 'Longer than the last one.' They leave south. You have a day less in your life and a measure of what you are against, which is worth it.", AshenColor);
                            break;
                        case "b":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
                            ChangeRenown(15f);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("They accept your conditions without hesitation — they were expecting conditions and prepared for them. You call a witness from your party. The time limit is sixty seconds. You both know going in that sixty seconds against this kind of cold is not easy. You hold for fifty-eight. They hold for all sixty on their side. The result is a draw by your terms. By what you both learned, it is something else. They leave. Your witness says nothing for an hour.", AshenColor);
                            break;
                        case "c":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
                            MobileParty.MainParty.RecentEventsMorale += 5f;
                            ChangeRenown(10f);
                            Msg("You counter with warmth at contact — not aggression, just the fire being itself, sustained and present. They accept and make contact. The cold and the warm meet in whatever space exists between two carriers. What happens is not what either of you planned: they hold longer than expected, and so do you, and at the end there is something in their expression that is not the Ashen absence. They leave without speaking. Your column watched the whole thing and will not forget it.", FireColor);
                            break;
                        case "d":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("You decline. They receive the refusal with the patience of something that has been refused before. They step aside. As you ride past they say, very quietly, the duration they estimated from observation alone. They are three seconds off the real number. They leave. They had what they came for before you opened your mouth, and you gave them the rest when you closed it.", BadColor);
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
                        "Costs 1 day. Gain Honor. Renown +10. The exchange before the end carries lore."),
                    new InquiryElement("b", "Accept, but offer him a choice at the end — yield and receive a clean parole.", null, true,
                        "Costs 1 day. Gain Merciful. Honor +1. He takes the parole. Something changes in him."),
                    new InquiryElement("c", "Decline the duel but treat his wounds and release him.", null, true,
                        "Gain Merciful. Honor +1. He receives this with more complexity than you expected."),
                    new InquiryElement("d", "Ask what he wants to say before the duel — he invoked the right but he has something to tell you.", null, true,
                        "Gain Calculating. Lore exchange. He gives you something and then accepts whatever comes."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
                            ChangeRenown(10f);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("The duel is formal and brief. He can barely stand but his working is precise — the body failing, the gift still itself. You end it cleanly. Before it ends he tells you the name of his teacher. You know the name. The lineage it implies is older than you expected him to carry. You will think about that name for a long time. Your men watched without speaking, which is how soldiers watch something that is larger than the battle it followed.", FireColor);
                            break;
                        case "b":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
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

        // ── POST-SIEGE: The Keep's Mage (mage-gated) ───────────────────────
        private static void ES6_KeepMage()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  What the Keep Powered",
                "In the lowest room of the conquered keep, chained to a working frame: a mage. Alive, clearly, and aware. They have been here long enough that the chains are worn where they've tested them. The previous lord used their gift to power something in the keep's walls — the cold storage that kept the garrison fed through three winters, apparently. They look at you and wait to find out which kind of lord you are.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Release them unconditionally. What was done here is done — they owe nothing forward.", null, true,
                        "Gain Merciful. Honor +1. They leave. What they do after is theirs."),
                    new InquiryElement("b", "Release them and offer a fair exchange — work freely chosen, compensation real.", null, true,
                        "Gain Calculating. They consider it. 50/50: they accept or they need to leave first."),
                    new InquiryElement("c", "Challenge them to prove themselves worth trusting before release.", null, true,
                        "Costs 1 day. Deep lore exchange. Their gift is unusual. You both learn something."),
                    new InquiryElement("d", "Ask what the keep actually ran on their gift before releasing them.", null, true,
                        "Gain Calculating. The answer is more complex than cold storage. Release follows the answer."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You remove the chains personally. They stand carefully, testing their own weight. They say thank you with the specific brevity of someone who has been saving the word for when it was actually true. They take nothing from the room. They leave through the east door. You see them once more, three days later, on the road to a city you were not heading toward. They are moving faster than they should be able to. Something resolved in them.", GoodColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            if (_rng.Next(2) == 0)
                            {
                                ChangeRenown(8f);
                                Msg("You lay out the terms: voluntary, compensated, ending with a clear date. They listen with the full attention of someone evaluating whether this is real. They conclude it is and accept. They work for you for six weeks before the agreed date and leave with what they earned and the specific look of someone who has revised their expectations of lords upward by one significant data point.", GoodColor);
                            }
                            else
                                Msg("They receive the offer and ask for one thing: a week alone to decide. You grant it. At the end of the week they are gone. They left a note that says the offer was fair and they needed something else first. You will hear their name again in about two years, in a context that will make the note make sense.", DimColor);
                            break;
                        case "c":
                            AgingSystem.AgeHero(Hero.MainHero, 1);
                            Msg("You give them a working to attempt, chained as they are. They complete it. Then they give you a counter-test, unprompted, that you cannot complete with the same elegance. The gift they carry is cold-adjacent without being Ashen — something older, something that was apparently possible before the split between fire and cold produced what it produced. You release them having learned the shape of a third thing. They leave knowing you recognised it.", FireColor);
                            break;
                        case "d":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("Cold storage was the cover. What the keep actually ran was a working that kept the previous lord's health stable — a slow continuous gift that maintained his body at some equilibrium while he aged slower than he should have. Three years they powered this. The lord is dead now and they are standing here and neither of them got what they should have from the arrangement. You release them with that knowledge. They walk out without looking back.", AshenColor);
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
                        "Gain Honor. She may or may not be able to correct it."),
                    new InquiryElement("c", "Give the family coin to fetch a real surgeon from the next town.", null, true,
                        "Lose 300 gold. Gain Merciful. He waits. That costs time he may not have."),
                    new InquiryElement("d", "Say nothing. You could be wrong.", null, true,
                        "Nothing. You might be wrong. You are not wrong."),
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
                                Msg("You reopen the wound correctly, clean it properly, and pack it with what you have. The herb-woman watches with the particular attention of someone updating a technique in real time. He has a week of fever ahead of him and then a slow recovery. He was going to have a much shorter future. Your hands knew what they were doing.", GoodColor);
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
                            Msg("You say nothing and ride on. In the morning you know, with the specific certainty of something you could have changed, that you were right. You carry this for the rest of the day. You do not know what he carries.", BadColor);
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
                        "Relation +5. They are alerted. They won't know what to look for."),
                    new InquiryElement("c", "Mark the location and send the coordinates to the nearest garrison.", null, true,
                        "Relation +3. Correct process. Slow response time."),
                    new InquiryElement("d", "Note it and keep moving — you have seen this pattern before.", null, true,
                        "Nothing. The pattern continues without a report."),
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
                        "Gain Calculating. He may talk. He may not. He will definitely know you can read him."),
                    new InquiryElement("c", "Accept the lie and ask everyone else in the inn separately.", null, true,
                        "Gain Calculating. Someone else was not instructed. 50/50."),
                    new InquiryElement("d", "Leave him to his loyalty. People protect what they have to protect.", null, true,
                        "Gain Honor. The information stays with him. You respect the choice."),
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
                            Msg("You let him keep it. He is protecting something with the only tools available to him. You ride on with the specific quiet of a choice that cost you information and cost him nothing, which is not the usual direction for that trade. He watches you leave with an expression he will carry for longer than you carry this road.", GoodColor);
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
                "A city records clerk has information you need — movement orders for a specific gate, filed three weeks ago. He is technically required to provide access to lords on request. He is also clearly frightened of whoever filed those orders, and is performing bureaucratic friction with the specific expertise of a man who has been doing it for years. He is not going to say no. He is going to take a very long time to say yes.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Ease his fear rather than press his duty.", null, true, hint),
                    new InquiryElement("b", "Invoke your authority formally — demand the record as a matter of right.", null, true,
                        "Relation -3 with city lord. You get the record. He files a complaint."),
                    new InquiryElement("c", "Offer him something in return — not a bribe, a guarantee.", null, true,
                        "Gain Calculating. A specific guarantee costs you nothing but your word. He takes it."),
                    new InquiryElement("d", "Leave it. Frightened clerks produce bad records under pressure anyway.", null, true,
                        "Nothing. The information stays filed."),
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
                                Msg("You try the right approach but the fear in him is too deep and too recent to ease in a single conversation. He appreciates the tone and gives you the record's existence but not its content, which is technically compliance. He is sorry about this in the specific way of someone who would like to do more but cannot yet. You have the reference number. That opens other doors.", DimColor);
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
                        "Relation -5 with merchant guild. The auditor is grateful. The truth is still unknown."),
                    new InquiryElement("c", "Side with the merchant — a single anomaly could be clerical.", null, true,
                        "Relation +5 with merchant guild. He may owe you. He may be guilty."),
                    new InquiryElement("d", "Decline to arbitrate — this is not your ledger or your fight.", null, true,
                        "Nothing. They continue without you."),
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
                            Msg("You support the auditor. The merchant guild will remember this in the specific way that guilds remember things: collectively, without urgency, and at a moment of their choosing. The auditor thanks you and continues the review. The truth of the ledger remains open, but the process continues now without obstruction. That is what auditors are for.", DimColor);
                            break;
                        case "c":
                            ChangeRelWithOwner(s, 5);
                            Msg("You support the merchant. He thanks you with the warmth of someone to whom this favour will translate into cooperation later. Whether he is guilty is a question you have declined to answer. The auditor closes his notes with the particular expression of a man who has been overruled before and knows exactly what it means for his profession. The ledger goes unresolved.", DimColor);
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
                        "Gain Calculating. 50/50: you get ahead of them or they catch your move."),
                    new InquiryElement("c", "Lead them somewhere useful and confront them on your terms.", null, true,
                        "Renown +5. You pick the ground. The confrontation is controlled."),
                    new InquiryElement("d", "Change your route and lose them — you have no time for this today.", null, true,
                        "Safe. They lose you. You learn nothing about who sent them."),
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
                            Msg("Three corners and a market crossing and you are clean. They are good but you know this city's geometry better from yesterday's approach. You complete your business without a tail. They know you noticed. They file that you were 'surveillance-aware', which is its own kind of flag, but one without immediate consequences. You are ahead by one day.", DimColor);
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
                        "Gain Merciful. The well is sealed. The children wait for help that is two days away."),
                    new InquiryElement("c", "Leave medicine from your supplies and your best guess at the cause.", null, true,
                        "Lose 200 gold. Gain Merciful. Your guess may be right. The herb-woman will work from it."),
                    new InquiryElement("d", "Delay your departure until you know they are stable.", null, true,
                        "Morale -3. Costs half a day. They will be stable or they will not — you stay either way."),
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
                            MobileParty.MainParty.RecentEventsMorale -= 3f;
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
                        "Costs half a day. The scout confirms the truth of it, whatever it is."),
                    new InquiryElement("c", "Respond as if it's real but at half-speed with flankers out.", null, true,
                        "If trap: you spring it on your terms. If real: you arrive cautiously. No bad outcome."),
                    new InquiryElement("d", "Dismiss the rider and send word to the lord mentioned to verify.", null, true,
                        "Lose Honor if it's real. Gain Calculating if it's not. The truth takes half a day."),
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
                            Msg("You advance with flankers extended and pace halved. If it is real, your caution costs thirty minutes. If it is not, your flankers reach the ambush position first. They do. Three men in the ditch with the particular expression of ambushers who have been found before they were ready. They surrender before the column arrives. The plan required your column moving at normal speed. You denied them that.", GoodColor);
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
                        "Lose 400 gold. Gain Merciful. The debt is ended. The law is satisfied. He releases the knife."),
                    new InquiryElement("c", "Invoke your authority — demand both parties stand down immediately.", null, true,
                        "Gain Honor. The father complies. He goes to jail anyway. The daughter's situation is unchanged."),
                    new InquiryElement("d", "Tell the merchant, publicly, that his behaviour is noted and documented.", null, true,
                        "Gain Calculating. The merchant's position becomes uncomfortable. The father has a witness now."),
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
                        "Relation +5. Correct process. 50/50 if the document was actually real."),
                    new InquiryElement("c", "Wave it through — the seal looks right and you have somewhere to be.", null, true,
                        "Nothing. It was forged. The merchant clears the gate with contraband."),
                    new InquiryElement("d", "Ask the merchant directly where the permit was issued.", null, true,
                        "Gain Calculating. His answer will tell you whether he knows it's forged or is carrying it in good faith."),
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
                                Msg("The guard detains the merchant. The permit office's response comes in two days: forged. The merchant had contraband in the second wagon. The guard's instinct was correct. Your backing gave him the authority to act on it. He will remember that a lord trusted his read. That is the kind of thing that makes gate guards better at their jobs.", GoodColor);
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
                        "Gain Honor. He makes the best call he can. Your men see you trust him completely."),
                    new InquiryElement("c", "Ask him to explain the clinical picture fully before you say anything.", null, true,
                        "Gain Calculating. Your question reshapes his thinking. He reaches a better decision."),
                    new InquiryElement("d", "Spend gold on an urgent courier for a specialist while the surgeon holds the situation.", null, true,
                        "Lose 500 gold. Gain Merciful. The specialist arrives in time for one of the two."),
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
                                MobileParty.MainParty.RecentEventsMorale += 6f;
                                Msg("You have seen enough battlefield surgery to know the specific indicator he is weighing — the depth and quality of the wound site is different between the two in a way that changes the priority. You tell him what you see. He checks it against his own read and adjusts. Both men receive treatment in the right order with the right technique. Both survive. Your surgeon looks at you differently afterward — not with deference, with the specific respect of a professional whose assessment was confirmed.", GoodColor);
                            }
                            else
                            {
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                MobileParty.MainParty.RecentEventsMorale += 2f;
                                Msg("You share what you know. Some of it is useful and he incorporates it. Some of it is below his knowledge level and he sets it aside without comment. The combined knowledge is better than his alone. One man survives who might not have. The other was beyond the combined knowledge of both of you. Your surgeon closes that file with the quiet efficiency of someone who has written those reports before and will write them again.", DimColor);
                            }
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            MobileParty.MainParty.RecentEventsMorale += 4f;
                            Msg("You give him the decision entirely and tell him so directly. He makes it cleanly, without the hesitation of someone who is second-guessing a superior's preferences. One man survives. The other does not — this was the likely outcome either way, and the surgeon's choice was correct given what was knowable. Your men watch you trust your own people completely. That travels through a column faster than orders.", GoodColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            MobileParty.MainParty.RecentEventsMorale += 4f;
                            Msg("You ask him to describe the clinical picture fully before you say anything. In describing it, he hears something in the structure of his own account that he had not heard while thinking it. He stops and redirects. The question you asked changed nothing about the facts and everything about the framing. Both men receive treatment. Both survive. He thanks you for the question specifically, which is how surgeons acknowledge a useful non-intervention.", GoodColor);
                            break;
                        case "d":
                            ChangeGold(-500);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            MobileParty.MainParty.RecentEventsMorale += 3f;
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
                        "Gain Calculating. Honor +1. The combined account is better than either alone."),
                    new InquiryElement("c", "Let it stand for now — his read is close enough for the lesson they need today.", null, true,
                        "Nothing. The wrong model travels forward. It may matter."),
                    new InquiryElement("d", "Bring in a second officer whose position gave them a clear view of the centre.", null, true,
                        "Morale +3. The third account resolves the question. Both readings are partially correct."),
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
                                MobileParty.MainParty.RecentEventsMorale += 5f;
                                Msg("The centre held because of a deliberate false retreat on their right — it drew your left flank's attention and compressed the centre's pressure by a third. The numbers were coincidence. Your sergeant listens with the specific attention of someone updating a model. He asks two clarifying questions and then restates the corrected read back to the officers. They leave the debrief with the right lesson. This is what debriefs are for and it requires someone who was watching the whole field. You were.", GoodColor);
                            }
                            else
                                Msg("You offer your counter-read but cannot articulate the mechanism clearly enough — you saw something but translating what you saw into the language of formation tactics is harder than seeing it was. Your sergeant acknowledges your disagreement and hedges his read without fully revising it. The debrief ends with ambiguity rather than a clean correction. Better than the wrong certainty. Not as good as the right certainty.", DimColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            MobileParty.MainParty.RecentEventsMorale += 4f;
                            Msg("You ask what he saw. His account of the centre from his position is actually accurate from where he stood — he simply could not see the false retreat from the left flank, which was behind his line of sight. When you add your account to his the combined picture is clearer than either alone. He revises his conclusion correctly and credits the two-position reading. The officers leave with a better model and a method for building one.", GoodColor);
                            break;
                        case "c":
                            Msg("You let it stand. His read is close enough that the lesson — hold the centre, watch for flanking pressure — is defensible. The mechanism is wrong. This will matter exactly once, at a moment that will not announce itself in advance. You have chosen convenience and it may cost nothing. You do not know yet.", DimColor);
                            break;
                        case "d":
                            MobileParty.MainParty.RecentEventsMorale += 3f;
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
                        "Gain Honor. He handles it correctly and resents no implication he couldn't."),
                    new InquiryElement("c", "Take the military share, return the city merchants' claims, and call the garrison staff claims void.", null, true,
                        "Gain Calculating. Fast. The garrison staff will be bitter. The merchants will be satisfied."),
                    new InquiryElement("d", "Hire a city administrator to manage the civilian claims under your oversight.", null, true,
                        "Lose 300 gold. The distribution is clean. The city trusts the outcome more."),
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
                                MobileParty.MainParty.RecentEventsMorale += 4f;
                                Msg("The correct precedent is siege capture law modified by the city charter's civilian commerce protections — the merchants' pre-siege inventory claims are valid up to the point of city closure, garrison staff claims are pro-rated by months served, and the military share is calculated after both civilian claims are satisfied. You work through it in two hours. Nobody gets everything. Nobody has a legitimate grievance. Your quartermaster is taking notes. This is what governing is.", GoodColor);
                            }
                            else
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                MobileParty.MainParty.RecentEventsMorale += 2f;
                                Msg("You apply the precedents as best you can, but the intersection of siege law and city charter has a gap that your reading doesn't resolve cleanly. You make a defensible decision rather than a correct one. The merchants accept it. The garrison staff accept it with the specific acceptance of people who will raise the gap in a formal petition in six months. You have managed the problem rather than solved it. For now, that is enough.", DimColor);
                            }
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            MobileParty.MainParty.RecentEventsMorale += 5f;
                            Msg("You give him the full authority directly and say so in front of the claimants. He takes it and works through it correctly — he has done this before, or something close enough that the differences don't matter. The distribution takes three hours and satisfies both groups adequately. He reports back to you with the numbers and a note that says he would have arrived at the same conclusions with or without the authority. That is the point.", GoodColor);
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
                        "Honor +1. Relation +5 with him."),
                    new InquiryElement("b", "Walk past as if you have not seen him.", null, true,
                        "Nothing happens."),
                    new InquiryElement("c", "Report his presence to the city guard as a potential threat.", null, true,
                        "Honor -1. Crime +5."),
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
    }
}
