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
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace AshAndEmber
{
    public static class SettlementEncounters
    {
        // ── Tuning ────────────────────────────────────────────────────────────
        public const float EncounterChance = 0.38f;         // per transition
        public const int   MinDaysBetween  = 3;             // cooldown

        // ── State ─────────────────────────────────────────────────────────────
        private static int    _cooldown              = 0;
        private static string _lastSettlementId      = null; // null = not in settlement
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
                if (mage)
                {
                    pool.Add(E_OldFlameSeer);
                    pool.Add(E_HealersTrade);
                    pool.Add(E_FireAndStraw);
                    pool.Add(E_ShrineGoesOut);
                    pool.Add(E_WarmthMerchant);
                }
            }
            if (town)
            {
                pool.Add(E_SoldierDying);
                pool.Add(E_ChildsBead);
                pool.Add(E_OldEnemy);
                if (ren >= 700f) pool.Add(E_TradeCouncil);
                if (mage)
                {
                    pool.Add(E_CuriousScholar);
                    pool.Add(E_AnotherFire);
                    pool.Add(E_AshTouchedMarket);
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
                if (ren >= 300f) pool.Add(E_BardsRequest);
                if (ren >= 500f) pool.Add(E_GuildsOffer);
                if (mage)
                {
                    pool.Add(E_VeteransQuestion);
                    pool.Add(E_TheCondemned);
                    if (ren >= 500f) pool.Add(E_PetitionersGate);
                }
            }

            Fire(pool, s);
        }

        private static void Fire(List<Action<Settlement>> pool, Settlement s)
        {
            if (pool.Count == 0) return;
            _cooldown = MinDaysBetween;
            Action<Settlement> chosen = pool[_rng.Next(pool.Count)];
            // Defer so the inquiry fires on the next tick flush
            MageKnowledge._deferredInquiry = () => { try { chosen(s); } catch { } };
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
