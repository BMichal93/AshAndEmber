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

namespace AshAndEmber
{
    public static class SettlementEncounters
    {
        // ── Tuning ────────────────────────────────────────────────────────────
        public const float EncounterChance       = 0.20f;  // per settlement transition (~1 per 25 days)
        public const int   MinDaysBetween        = 5;      // shared cooldown between any encounter
        public const float BattleEncounterChance = 0.30f;  // per field battle
        public const float SiegeEncounterChance  = 0.45f;  // per siege
        public const float RaidEncounterChance   = 0.50f;  // per raid

        // ── State ─────────────────────────────────────────────────────────────
        private static int    _cooldown              = 0;
        private static string _lastSettlementId      = null; // null = not in settlement
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
            store.SyncData("SE_Cooldown",        ref _cooldown);
            store.SyncData("SE_LastSettlement",  ref _lastSettlementId);
        }

        /// Called from MagicCampaignBehavior.OnDailyTick.
        public static void DailyTick()
        {
            if (_cooldown > 0) { _cooldown--; return; }
            try
            {
                Hero player = Hero.MainHero;
                if (player == null) return;

                string currentId = player.CurrentSettlement?.StringId;
                bool wasIn  = _lastSettlementId != null;
                bool isIn   = currentId != null;

                if (isIn && !wasIn)
                {
                    // Just entered a settlement
                    var s = player.CurrentSettlement;
                    _lastSettlementId = currentId;
                    if (_rng.NextDouble() < EncounterChance)
                        TryFireEnter(s);
                }
                else if (!isIn && wasIn)
                {
                    // Just left — find settlement by saved id
                    string leftId = _lastSettlementId;
                    _lastSettlementId = null;
                    var s = Settlement.Find(leftId);
                    if (s != null && _rng.NextDouble() < EncounterChance)
                        TryFireLeave(s);
                }
                else if (isIn && wasIn && currentId != _lastSettlementId)
                {
                    // Moved directly between settlements
                    var s = player.CurrentSettlement;
                    _lastSettlementId = currentId;
                    if (_rng.NextDouble() < EncounterChance)
                        TryFireEnter(s);
                }
                else
                {
                    _lastSettlementId = currentId;
                }
            }
            catch { }
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
                if (mage)
                {
                    pool.Add(E_OldFlameSeer);
                    pool.Add(E_HealersTrade);
                    pool.Add(E_FireAndStraw);
                    pool.Add(E_ShrineGoesOut);
                    pool.Add(E_WarmthMerchant);
                    pool.Add(EV4_GiftedChild);
                    pool.Add(EV5_FrozenFord);
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
                if (mage)
                {
                    pool.Add(E_CuriousScholar);
                    pool.Add(E_AnotherFire);
                    pool.Add(E_AshTouchedMarket);
                    pool.Add(EC2_StreetPreacher);
                    pool.Add(EC3_SickNoble);
                    pool.Add(EC4_FakeMage);
                    pool.Add(EC5_PhysiciansEye);
                    if (ren >= 1000f) pool.Add(E_CrowdWantsSign);
                }
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
                if (mage)
                {
                    pool.Add(E_MothersPlea);
                    pool.Add(E_WidowsPyre);
                    pool.Add(E_SignalFire);
                    pool.Add(E_EldersSending);
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
                if (mage)
                {
                    pool.Add(E_VeteransQuestion);
                    pool.Add(E_TheCondemned);
                    pool.Add(LC2_WrongnessInAir);
                    if (ren >= 500f) pool.Add(E_PetitionersGate);
                }
                if (ashen) pool.Add(LC4_RecognizedByAshen);
            }

            Fire(pool, s);
        }

        private static void Fire(List<Action<Settlement>> pool, Settlement s)
        {
            if (pool.Count == 0) return;
            if (MageKnowledge._deferredInquiry != null) return; // don't overwrite a pending inquiry
            _cooldown = MinDaysBetween;
            Action<Settlement> chosen = pool[_rng.Next(pool.Count)];
            MageKnowledge._deferredInquiry = () => { try { chosen(s); } catch { } };
        }

        private static void FireBattle(List<Action> pool)
        {
            if (pool.Count == 0) return;
            if (MageKnowledge._deferredInquiry != null) return;
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
                if (mage) pool.Add(ES_OldScorchmarks);
                if (mage) pool.Add(ES4_AshenCrystal);
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
            }
            catch { }
        }

        private static void ChangeGold(int amount)
        {
            try { Hero.MainHero?.ChangeHeroGold(amount); } catch { }
        }

        private static void ChangeRenown(float amount)
        {
            try
            {
                if (Hero.MainHero?.Clan != null)
                    Hero.MainHero.Clan.Renown = Math.Max(0f, Hero.MainHero.Clan.Renown + amount);
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
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, owner, delta, false);
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
                Msg($"{lord.Name} hears of it.", DimColor);
            }
            catch { }
        }

        private static string GoldStr(int amount)
            => amount >= 0 ? $"+{amount} gold" : $"{amount} gold";

        // ═════════════════════════════════════════════════════════════════════
        // LEAVE VILLAGE — MAGE
        // ═════════════════════════════════════════════════════════════════════

        // 1. A Mother's Plea
        private static void E_MothersPlea(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "A Mother's Plea",
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
                "The Widow's Pyre",
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
                "Signal Fire",
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
                "The Elder's Sending",
                "The village elder — older than anyone else here, hands like bark — stops you at the gate. She places both palms on your horse's neck and mutters something. Then she looks up. \"The fire knows its own,\" she says. \"Ride safely.\"",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Accept her blessing with grace.", null, true,
                        "Morale boost. Honor +1."),
                    new InquiryElement("b", "Ask what she means — how does she know?", null, true,
                        "Gain flavor insight. Nothing mechanical."),
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
                "Beggar at the Crossroads",
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
                "The Lame Horse",
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
                "The Coin Game",
                "A village child runs after your horse, shouting that you dropped a coin. You didn't. The child holds up a bent copper piece with an expression of perfect innocence.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Go along with it. Give them the coin.", null, true,
                        "Lose 100 gold. Gain Merciful."),
                    new InquiryElement("b", "Tell them quietly that you know the game, and move on.", null, true,
                        "Nothing happens."),
                    new InquiryElement("c", "Call their parents about it.", null, true,
                        "Small scene. Nothing mechanical."),
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
                            Msg("A mother appears from nowhere, takes the child by the ear, and disappears again. The copper coin remains in the road.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 8. Torches at Dusk
        private static void E_TorchesAtDusk(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "Torches at Dusk",
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
                "The Eager Recruit",
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
                "The Festival Farewell",
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
                "The Old Flame-Seer",
                "An old man sits outside the inn, eyes clouded white. He does not look at you. He faces toward you. \"I can smell the fire from here,\" he says. \"Not the campfire kind. The old kind.\" He taps the bench beside him.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Sit with him. You have questions too.", null, true,
                        "Costs 1 day. Renown +10 as the village sees you honour him."),
                    new InquiryElement("b", "Ask what he sees in you.", null, true,
                        "Flavor insight only. Nothing mechanical."),
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
                "The Healer's Trade",
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
                "Fire and Straw",
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
                "The Shrine Goes Out",
                "The village's roadside shrine — an iron bowl on a post, supposed to burn day and night — has gone cold. The village elder sees this as an ill omen. Three people have already gathered around it, uncertain. They see you arrive.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Relight it. You can do this without effort.", null, true,
                        "Costs 1 day. Renown +5. Gain Merciful."),
                    new InquiryElement("b", "Tell them the omen means nothing and suggest a flint and tinder.", null, true,
                        "Nothing mechanical."),
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
                "The Warmth Merchant",
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
                "A Family's Quarrel",
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
                "The Harvest Festival",
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
                "Ashen Aftermath",
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
                "The Warning",
                "An old woman stops you as you ride in. She tells you the north road past the village has bandits on it — saw them herself this morning, eight or nine, camped in the tree-line. She is not asking anything of you. She is just telling you.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Thank her and leave a coin for her trouble.", null, true,
                        "Lose 100 gold. Gain Merciful."),
                    new InquiryElement("b", "Ask for more details — position, numbers, armed?", null, true,
                        "Gain tactical flavor message."),
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
                "The Spilled Cart",
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
                "The Veteran's Question",
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
                "The Condemned",
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
                "Petitioners' Gate",
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
                "The Lightened Purse",
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
                "The Displaced Noble",
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
                "The Bard's Request",
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
                "A Detained Soldier",
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
                "The Guild's Offer",
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
                "The Ashen Informant",
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
                "An Insult at the Gate",
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
                "The Curious Scholar",
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
                "Another Fire",
                "In the market crowd, for a moment, you feel it — the particular warmth that has nothing to do with weather. Someone here carries the gift, or something close to it. The feeling passes before you can locate the source.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Go back and search the market.", null, true,
                        "50/50: find them and gain good favor, or find nothing."),
                    new InquiryElement("b", "Let it pass. The fire finds its own.", null, true,
                        "Nothing happens."),
                    new InquiryElement("c", "Ask your men if anyone saw anything unusual.", null, true,
                        "Flavor message only."),
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
                            Msg("\"Nothing unusual,\" your sergeant says. \"Unless you count the man selling three different kinds of prayer-charm from one table.\" That is not it.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // 33. The Ash-Touched Market
        private static void E_AshTouchedMarket(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "The Ash-Touched Market",
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
                "Grey Eyes",
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
                "The Fellow Cold",
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
                "The Crowd Wants a Sign",
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
                "A Soldier Dying",
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
                "The Child's Bead",
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
                "The Trade Council",
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
                "The Dying Man",
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
                "Found Among the Dead",
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
                "The Picked-Over Dead",
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
                "One Man Remaining",
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
                "The Field Priest",
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
                "A Strange Heat",
                "Among the enemy dead, a satchel. You smell it from three feet away — old paper, scorched at the edges, and something underneath. Warmth. Not fire. The kind of warmth that knows things. A mage kept notes.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Spend the night reading it properly.", null, true,
                        "Costs 1 day. Renown +10."),
                    new InquiryElement("b", "Take what seems useful and keep moving.", null, true,
                        "Flavor message only."),
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
                "The Herald's Visit",
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
                "The Officer's Bargain",
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
                "The Camp at Dusk",
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
                "The Fallen Lord's Household",
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
                "The Question of the Gate Guard",
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
                "The Surrounding Villages",
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
                "The Treasury",
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
                "Old Marks",
                "In the lowest level of the keep, your torch finds scorched walls that predate this battle by years. The pattern is not the random damage of fire — it is deliberate, and it is old. Someone with the gift worked here, before you, and the working was not small.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Study them carefully. These are a record.", null, true,
                        "Costs 1 day. Gain Ashen intel."),
                    new InquiryElement("b", "Note the pattern and move on.", null, true,
                        "Flavor message only."),
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
                "The Shrine Keeper",
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
                "The First Night",
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
                "The Man Who Stayed",
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
                "The Child on the Road",
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
                "A Question of Shares",
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
                "The Woman in the Doorway",
                "In the last house at the edge of the village, an old woman stands in the doorway as you ride past. She says one word. You catch it — it's not a language you know. But the intonation is clear. It is not a blessing.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Ask what she said.", null, true,
                        "Flavor only."),
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
                "Still Burning",
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
                "A Veteran Asks",
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
                "The Private Dinner",
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
                "The Man Against the Fire",
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
                "The Quarantine Gates",
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
                "The Challenge",
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
                "Marks on the Wall",
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
                "The Small Thief",
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
                "An Accusation",
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
                "The Letter",
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
                "A Parting Gift",
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
                "Something Smothered",
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
                "The Teaching Monk",
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
                "The Dry Well",
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
                "The Wise Woman",
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
                "Your Name",
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
                "The Dog",
                "A farm dog has been barking at you since your party entered the village. Just at you. Not at your horse, not at your men. At you specifically, with the rigid-legged certainty of an animal that knows something is wrong.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Try to approach and calm it.", null, true,
                        "It keeps barking. Flavor only."),
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
                "The Road Companions",
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
                "Your Horse, Pulling Up",
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
                "The Hidden Note",
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
                "The Knight Without a Lord",
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
                "The Wedding",
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
                "The Collector",
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
                "Two Sons",
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
                "The Familiar Face",
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
                "The Inn Fire",
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
                "The Toll",
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
                "The Question",
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
                "The Wealthy Sick",
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
                "Broken Colors",
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
                "A Warning",
                "Half a mile from the city, a stranger passes your column going the other direction and presses a folded note into your hand without slowing. You open it. A name — one of your own men — and four words in a careful hand: 'reporting your movements east.'",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Investigate immediately and quietly.", null, true,
                        "50/50: confirmed and discharged, or clean and you owe an apology."),
                    new InquiryElement("b", "Watch him without acting. Let him reveal himself.", null, true,
                        "Nothing mechanical — the watching begins."),
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
                "The Mercenary Captain",
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
                "The Banner Still Standing",
                "Everyone else on this part of the field has fled or fallen. One man remains: the enemy standard bearer, the banner still upright, looking at you. He is not going to lower it. He is not going to run. He is going to stand there until something changes.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Honour it. He can keep the banner and go home.", null, true,
                        "Gain Honor and Merciful. Renown +10."),
                    new InquiryElement("b", "Demand he surrender the standard.", null, true,
                        "Standard captured. Nothing else."),
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
                "What the Wagons Carried",
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
                "What He Did",
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
                "What the Fire Shows",
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
                "The Healers",
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
                "The Informant",
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
                "The Negotiation",
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
                "What They Left",
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
                "The Empty Houses",
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
                "The Trial",
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
                "What She Sees",
                "A girl of perhaps six stops playing and stares at you. Not at your horse, not at your armor — at you. She reaches toward something she cannot name, cannot see, but clearly senses. Her mother pulls her back. The girl's eyes do not leave yours.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Crouch down and say a quiet word to her.", null, true,
                        "Gain Merciful. The fire recognises her. She will remember this."),
                    new InquiryElement("b", "Meet the mother's eyes and give a small nod.", null, true,
                        "The mother will watch for it now. She may not know what she's watching for."),
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
                "The Road South",
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
                "The Storyteller",
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
                "The Last Request",
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
                "The Escaped Man",
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
                "The Deserter",
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
                "The Block",
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
                "The Impostor",
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
                "Your Name on a Wall",
                "Near the market gate, a notice has been posted with a description of a 'fire-cursed lord causing disruption across the eastern roads' and a bounty attached. The description is vague but unmistakably you. The city guard has been walking past it all morning without looking twice.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Tear it down quietly and ride on.", null, true,
                        "Nothing. Done."),
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
                "The Running Girl",
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
                "The Old Debt",
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
                "The Watching Figure",
                "Leaving the city, you become aware — not by sight but by the particular absence of warmth — of someone in a building's shadow cataloguing your party. Grey cloak, pale still face, the patient posture of something that is not in a hurry because it has learned not to be. An Ashen agent is noting your movements.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Confront them directly.", null, true,
                        "They disappear before you reach them. But they know you saw."),
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
                "Not Enough",
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
                "A Name Worth Something",
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
                "Two Miles",
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
                "The Water",
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
                "The Kept One",
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
                "Under the Floor",
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
                "The Wolves",
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
                "The Ford",
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
                "The Fever",
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
                "The Wrong Song",
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
                "The Likeness",
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
                "The Physician's Question",
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
                "An Old Face",
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
                            ChangeGold(-400);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            MobileParty.MainParty.RecentEventsMorale += 3f;
                            Msg("You press the coin on him first, then name the offer. He straightens up slightly — not all the way, but enough. He falls in at the column's rear. Your veterans give him space without being instructed to. He knows what that means.", GoodColor);
                            break;
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
                "The Horse That Stayed",
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
                "Left Behind",
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
                "The Marked Child",
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
        // ── (original event 40 follows below, unchanged) ──────────────────
        // ═══════════════════════════════════════════════════════════════════

        // 40. An Old Enemy
        private static void E_OldEnemy(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "An Old Enemy",
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
