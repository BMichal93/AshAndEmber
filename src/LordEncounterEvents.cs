// =============================================================================
// ASH AND EMBER — LordEncounterEvents.cs
// "The Whispered Coin" — a rare encounter that triggers during lord dialogue
// when the player's clan tier is 1–3.  A shadowy contact approaches with a
// contract to kill the lord the player is currently visiting.
//
// Three choices:
//   A) Refuse        — nothing happens; cooldown starts.
//   B) Agree         — fight the lord immediately; on victory receive +50
//                      relations with a random lord in that kingdom + 2 000–4 000 gold.
//   C) Turn them in  — OneHanded/Athletics skill check; on success +10 relations
//                      with the target lord and −10 with a random lord; on failure nothing.
//
// Dialogue hook:   "lord_start" → player option → NPC dismissal → close_window
//                  Consequence defers the inquiry via MageKnowledge._deferredInquiry.
// Probability:     8% per unique lord each time the dialogue is opened (gated
//                  by cooldown between events).
// Variants (3):    Cloaked messenger / young cousin of the lord / former soldier.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public class LordEncounterBehavior : CampaignBehaviorBase
    {
        // ── Tuning ─────────────────────────────────────────────────────────────
        private const float OfferChance   = 0.08f;  // 8% per lord per dialogue session
        private const int   CooldownDays  = 14;     // days between any whispered-coin event
        private const int   RewardGoldMin = 2000;
        private const int   RewardGoldMax = 4000;
        private const int   BattleWindow  = 3;      // days after Agree before reward expires

        // ── State ──────────────────────────────────────────────────────────────
        private static int    _cooldown              = 0;
        private static string _checkedLordId         = null;
        private static bool   _offerPendingForLord   = false;
        private static string _veiledBattleKingdomId = null;
        private static int    _veiledBattleDaysLeft  = 0;

        private static readonly Random _rng = new Random();

        // ── CampaignBehaviorBase ───────────────────────────────────────────────
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
        }

        public override void SyncData(IDataStore store)
        {
            try { store.SyncData("LEV_Cooldown",        ref _cooldown); } catch { }
            try { store.SyncData("LEV_CheckedLordId",   ref _checkedLordId); } catch { }
            try { store.SyncData("LEV_OfferPending",    ref _offerPendingForLord); } catch { }
            try { store.SyncData("LEV_BattleKingdomId", ref _veiledBattleKingdomId); } catch { }
            try { store.SyncData("LEV_BattleDaysLeft",  ref _veiledBattleDaysLeft); } catch { }
        }

        // ── Session launched ───────────────────────────────────────────────────
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            RegisterDialogue(starter);
        }

        // ── Daily tick ─────────────────────────────────────────────────────────
        private static void OnDailyTick()
        {
            if (_cooldown > 0) _cooldown--;
            if (_veiledBattleDaysLeft > 0)
            {
                _veiledBattleDaysLeft--;
                if (_veiledBattleDaysLeft == 0)
                    _veiledBattleKingdomId = null;
            }
        }

        // ── Map event ended (battle result) ───────────────────────────────────
        private static void OnMapEventEnded(MapEvent mapEvent)
        {
            try
            {
                if (mapEvent == null || _veiledBattleKingdomId == null) return;

                bool playerAttacker = mapEvent.AttackerSide?.Parties
                    .Any(p => p.Party == PartyBase.MainParty) == true;
                bool playerDefender = mapEvent.DefenderSide?.Parties
                    .Any(p => p.Party == PartyBase.MainParty) == true;
                if (!playerAttacker && !playerDefender) return;

                bool playerWon = (playerAttacker && mapEvent.WinningSide == BattleSideEnum.Attacker)
                              || (playerDefender && mapEvent.WinningSide == BattleSideEnum.Defender);
                if (!playerWon) return;

                ApplyVictoryReward();
            }
            catch { }
        }

        private static void ApplyVictoryReward()
        {
            string kingdomId = _veiledBattleKingdomId;
            _veiledBattleKingdomId = null;
            _veiledBattleDaysLeft  = 0;
            try
            {
                int gold = RewardGoldMin + _rng.Next(RewardGoldMax - RewardGoldMin + 1);
                Hero.MainHero?.ChangeHeroGold(gold);

                Kingdom kingdom = Kingdom.All.FirstOrDefault(k => k.StringId == kingdomId);
                var candidates  = kingdom == null ? null : Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && !h.IsPrisoner && !h.IsChild
                             && h != Hero.MainHero
                             && h.Clan?.Kingdom == kingdom)
                    .ToList();

                if (candidates != null && candidates.Count > 0)
                {
                    Hero ally = candidates[_rng.Next(candidates.Count)];
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                        Hero.MainHero, ally, 50, false);
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"The coin-carrier's employers are satisfied. Word reaches {ally.Name} of your commitment. " +
                        $"+50 relations with {ally.Name}. +{gold:N0} gold."));
                }
                else
                {
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"The coin-carrier's promised payment arrives as agreed. +{gold:N0} gold."));
                }
            }
            catch { }
        }

        // ── Dialogue registration ──────────────────────────────────────────────
        private static void RegisterDialogue(CampaignGameStarter starter)
        {
            // Priority 90: below Ashen override (200), above vanilla (100).
            // Player picks this line from the lord dialogue option list.
            try
            {
                starter.AddPlayerLine(
                    "ldm_veiled_offer_option",
                    "lord_start",
                    "ldm_veiled_offer_npc",
                    "Something stopped me in the passageway before I could reach you.",
                    CondVeiledOffer,
                    null,
                    90);

                starter.AddDialogLine(
                    "ldm_veiled_offer_npc_line",
                    "ldm_veiled_offer_npc",
                    "close_window",
                    "Then see to it. My hall will be here when you return.",
                    null,
                    ConsequenceVeiledOffer,
                    90);
            }
            catch { }
        }

        // ── Condition ──────────────────────────────────────────────────────────
        private static bool CondVeiledOffer()
        {
            try
            {
                Hero lord = Hero.OneToOneConversationHero;
                if (lord == null || !lord.IsLord || lord.IsChild || lord.IsPrisoner || !lord.IsAlive)
                    return false;
                if (ColourLordRegistry.IsAshenLord(lord)) return false;
                if (lord.Clan?.Kingdom == null) return false;

                int tier = Hero.MainHero?.Clan?.Tier ?? 0;
                if (tier < 1 || tier > 3) return false;

                if (_cooldown > 0) return false;

                // Return cached roll for this lord within the same session
                if (_checkedLordId == lord.StringId)
                    return _offerPendingForLord;

                // New lord — roll once
                _checkedLordId       = lord.StringId;
                _offerPendingForLord = _rng.NextDouble() < OfferChance;
                return _offerPendingForLord;
            }
            catch { return false; }
        }

        // ── Dialogue consequence ───────────────────────────────────────────────
        private static void ConsequenceVeiledOffer()
        {
            try
            {
                Hero lord = Hero.OneToOneConversationHero;
                _offerPendingForLord = false;
                _checkedLordId       = null;
                if (lord == null) return;

                MageKnowledge._deferredInquiry = () =>
                {
                    try { FireWhisperedCoin(lord); } catch { }
                };
            }
            catch { }
        }

        // ── Event inquiry ──────────────────────────────────────────────────────
        private static void FireWhisperedCoin(Hero lord)
        {
            if (lord == null || !lord.IsAlive) return;

            string lordName = lord.Name?.ToString() ?? "the lord";
            string clanName = lord.Clan?.Name?.ToString() ?? "their clan";

            string intro;
            switch (_rng.Next(3))
            {
                case 0:
                    intro =
                        $"A cloaked figure intercepts you in the passageway, their face lost beneath a deep hood. " +
                        $"They press a heavy purse into your palm without ceremony.\n\n" +
                        $"\"You are about to speak with {lordName}. We would prefer you did not come back alone.\" " +
                        $"The voice is low and controlled — someone who has hired swords before. " +
                        $"\"Finish what needs to be finished, and those I represent will see you compensated generously.\"";
                    break;
                case 1:
                    intro =
                        $"A young person steps from a side passage. The jaw, the posture, the set of the eyes — " +
                        $"you recognise the family line before they speak.\n\n" +
                        $"\"You are going to see my cousin.\" Not a question. \"I am asking you to make that visit their last. " +
                        $"{clanName} has bent crooked under {lordName}'s hand, and it will not straighten while they draw breath.\" " +
                        $"They name a sum that would cover three months of campaigning. " +
                        $"\"Do this quietly, and I am in your debt besides.\"";
                    break;
                default:
                    intro =
                        $"A scarred woman blocks the corridor, arms loose at her sides. She carries herself like someone " +
                        $"who expects trouble and has long since made peace with it.\n\n" +
                        $"\"Six years I carried {clanName}'s banner,\" she says, watching the far end of the hall. " +
                        $"\"Long enough to know what {lordName} has been doing to the people behind that banner. " +
                        $"I represent those who want it to stop — today, if possible.\" " +
                        $"She slides a sealed purse across the floor with one boot. \"Gold now. More when it is done. " +
                        $"And the quiet thanks of people whose thanks are worth having.\"";
                    break;
            }

            int combatSkill = Math.Max(
                Hero.MainHero?.GetSkillValue(DefaultSkills.OneHanded) ?? 0,
                Hero.MainHero?.GetSkillValue(DefaultSkills.Athletics) ?? 0);
            float turnInChance = Math.Min(0.85f, 0.30f + combatSkill * 0.004f);
            int   chancePct    = (int)(turnInChance * 100f);
            string oddsWord    = combatSkill >= 150 ? "very likely"
                               : combatSkill >= 100 ? "likely"
                               : combatSkill >= 60  ? "even odds"
                               : "unlikely";

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Whispered Coin",
                intro,
                new List<InquiryElement>
                {
                    new InquiryElement("refuse", "Refuse. This is not your business.", null, true,
                        "You want no part in this arrangement. You let the messenger disappear."),
                    new InquiryElement("agree", "Take the coin. Handle it now.", null, true,
                        $"Start a fight with {lordName} immediately. " +
                        $"Win: +50 relations with a lord in their kingdom, {RewardGoldMin:N0}–{RewardGoldMax:N0} gold."),
                    new InquiryElement("turnin", $"Take them to {lordName}.", null, true,
                        $"[OneHanded/Athletics {combatSkill}] Seize the conspirator and present them — {oddsWord} ({chancePct}%). " +
                        $"Success: +10 relations with {lordName}, −10 with a random lord. Failure: nothing."),
                },
                false, 1, 1,
                "Choose.",
                "",
                chosen =>
                {
                    string id = chosen?.FirstOrDefault()?.Identifier as string ?? "refuse";
                    switch (id)
                    {
                        case "agree":   HandleAgree(lord);                  break;
                        case "turnin":  HandleTurnIn(lord, turnInChance);   break;
                        default:        HandleRefuse();                     break;
                    }
                },
                _ => HandleRefuse(),
                "", false
            ), false, true);
        }

        // ── Choice handlers ────────────────────────────────────────────────────
        private static void HandleRefuse()
        {
            _cooldown = CooldownDays;
        }

        private static void HandleAgree(Hero lord)
        {
            _cooldown = CooldownDays;
            try
            {
                MobileParty lordParty = lord?.PartyBelongedTo;
                if (lordParty == null || lordParty == MobileParty.MainParty)
                {
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"{lord?.Name} cannot be reached right now — the moment has passed."));
                    return;
                }

                _veiledBattleKingdomId = lord.Clan?.Kingdom?.StringId;
                _veiledBattleDaysLeft  = BattleWindow;

                PlayerEncounter.RestartPlayerEncounter(
                    MobileParty.MainParty.Party, lordParty.Party, false);
                GameMenu.SwitchToMenu("encounter");
            }
            catch
            {
                _veiledBattleKingdomId = null;
                _veiledBattleDaysLeft  = 0;
                MBInformationManager.AddQuickInformation(new TextObject(
                    "The moment passes before you can act. The lord is beyond your reach for now."));
            }
        }

        private static void HandleTurnIn(Hero lord, float skillChance)
        {
            _cooldown = CooldownDays;
            try
            {
                if (_rng.NextDouble() < skillChance)
                {
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                        Hero.MainHero, lord, 10, false);

                    var candidates = Hero.AllAliveHeroes
                        .Where(h => h.IsLord && h.IsAlive && !h.IsPrisoner && !h.IsChild
                                 && h != Hero.MainHero && h != lord
                                 && h.Clan?.Kingdom != null)
                        .ToList();

                    if (candidates.Count > 0)
                    {
                        Hero target = candidates[_rng.Next(candidates.Count)];
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                            Hero.MainHero, target, -10, false);
                        MBInformationManager.AddQuickInformation(new TextObject(
                            $"{lord.Name} receives the conspirator with a cold eye. You have earned their trust — " +
                            $"and made an enemy somewhere in the shadows. " +
                            $"+10 relations with {lord.Name}. −10 with {target.Name}."));
                    }
                    else
                    {
                        MBInformationManager.AddQuickInformation(new TextObject(
                            $"{lord.Name} receives the conspirator. Your loyalty is noted. " +
                            $"+10 relations with {lord.Name}."));
                    }
                }
                else
                {
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "The conspirator twists free before you can secure them and disappears into the crowd. " +
                        "You arrive at the hall empty-handed."));
                }
            }
            catch { }
        }
    }
}
