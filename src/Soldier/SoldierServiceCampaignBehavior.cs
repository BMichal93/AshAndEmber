// =============================================================================
// ASH AND EMBER — Soldier/SoldierServiceCampaignBehavior.cs
// "Take the Lord's Coin" — hire your own party out to a warring commander as a
// common soldier. Unlike a mercenary contract (clan tier 1+), a sworn sword can
// take service from clan level 0.
//
// While in service the player's party escorts the commander and fights his
// battles, is treated as a mercenary of his realm, and is paid every week
// (party upkeep + a small bounty). Riding off on your own — clicking anywhere on
// the map — ends the contract; doing so before the agreed term is desertion
// (crime + relation loss), and the player is warned first. Serving the full term
// pays a parting bonus and releases you cleanly, like a mercenary contract, with
// no lasting allegiance.
//
// Dialogue lives in the .Dialogue.cs partial; the tick/escort/pay logic is here.
// =============================================================================

using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class SoldierServiceCampaignBehavior : CampaignBehaviorBase
    {
        // ── Persistent state (serialised per save) ────────────────────────────
        // Empty commander id == not in service.
        private static string _lordId    = "";
        private static string _kingdomId = "";
        private static double _endDays   = 0.0;   // CampaignTime.ToDays at which the term ends
        private static double _termDays  = 0.0;   // full agreed term length (for penalty scaling)

        // Runtime-only guard so the desertion prompt is raised once, not per tick.
        private static bool _leavePromptOpen = false;

        private const string AshenKingdomId = "ashen_kingdom";

        internal static bool InService => !string.IsNullOrEmpty(_lordId);

        // ── CampaignBehaviorBase ──────────────────────────────────────────────
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
        }

        public override void SyncData(IDataStore store)
        {
            try { store.SyncData("SOLDIER_LORD_ID",   ref _lordId);    } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { store.SyncData("SOLDIER_KINGDOM_ID", ref _kingdomId); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { store.SyncData("SOLDIER_END_DAYS",   ref _endDays);   } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { store.SyncData("SOLDIER_TERM_DAYS",  ref _termDays);  } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Clears stale service state when a new campaign starts in the same
        // process (StringIds collide across campaigns). On loading a save,
        // SyncData repopulates immediately afterward.
        internal static void ResetForNewGame()
        {
            _lordId = ""; _kingdomId = ""; _endDays = 0.0; _termDays = 0.0;
            _leavePromptOpen = false;
        }

        // ── Lookups ───────────────────────────────────────────────────────────
        private static Hero ServingLord()
        {
            if (string.IsNullOrEmpty(_lordId)) return null;
            try { return Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == _lordId); }
            catch { return null; }
        }

        internal static double DaysRemaining()
        {
            try { return _endDays - CampaignTime.Now.ToDays; }
            catch { return 0.0; }
        }

        // ── Eligibility ───────────────────────────────────────────────────────
        // True if the player may offer to take service from the conversation lord.
        internal static bool CanOfferService(Hero lord)
        {
            try
            {
                if (InService) return false;
                if (lord == null || !lord.IsLord || !lord.IsAlive || lord == Hero.MainHero) return false;
                if (lord.IsPrisoner) return false;

                var main = Hero.MainHero;
                if (main?.Clan == null) return false;
                // Not while already sworn to a realm (vassal or mercenary elsewhere).
                if (main.Clan.Kingdom != null) return false;

                // The commander must lead a party we can actually ride with.
                var lordParty = lord.PartyBelongedTo;
                if (lordParty == null || lordParty == MobileParty.MainParty) return false;

                var kingdom = lord.Clan?.Kingdom;
                if (kingdom == null || kingdom.IsEliminated) return false;

                // (11) Never the Ashen.
                if (IsAshenFaction(kingdom) || ColourLordRegistry.IsAshenLord(lord)) return false;

                // (8) The realm only takes on soldiers while at war with a real
                // rival (not merely the Ashen).
                if (!IsAtRealWar(kingdom)) return false;

                // (10) Not a realm at war with the player's own faction.
                var playerFaction = main.MapFaction;
                if (playerFaction != null && kingdom.IsAtWarWith(playerFaction)) return false;

                return true;
            }
            catch { return false; }
        }

        // Kingdom is at war with a living, non-Ashen kingdom.
        private static bool IsAtRealWar(Kingdom kingdom)
        {
            try
            {
                foreach (var other in Kingdom.All)
                {
                    if (other == kingdom || other.IsEliminated) continue;
                    if (IsAshenFaction(other)) continue;
                    if (kingdom.IsAtWarWith(other)) return true;
                }
                return false;
            }
            catch { return false; }
        }

        private static bool IsAshenFaction(IFaction f)
        {
            try { return AshenCitySystem.IsAshenFaction(f) || f?.StringId == AshenKingdomId; }
            catch { return false; }
        }

        // ── Joining ───────────────────────────────────────────────────────────
        internal static void JoinService(Hero lord, int termDays)
        {
            try
            {
                if (lord?.Clan?.Kingdom == null || lord.PartyBelongedTo == null) return;
                var kingdom = lord.Clan.Kingdom;
                var clan    = Hero.MainHero?.Clan;
                if (clan == null) return;

                _lordId    = lord.StringId;
                _kingdomId = kingdom.StringId;
                _termDays  = termDays;
                _endDays   = CampaignTime.Now.ToDays + termDays;

                // (4) Treated as a mercenary of the realm, exactly like a contract.
                try
                {
                    int award = clan.MercenaryAwardMultiplier > 0 ? clan.MercenaryAwardMultiplier : 1;
                    ChangeKingdomAction.ApplyByJoinFactionAsMercenary(
                        clan, kingdom, CampaignTime.DaysFromNow(termDays), award, false);
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                // (3) Fall in and ride with the commander.
                ReassertEscort(lord);

                try
                {
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"You take {lord.Name}'s coin. Your party marches under the banner of {kingdom.Name} for {termDays} days."));
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Escort maintenance ────────────────────────────────────────────────
        private static void ReassertEscort(Hero lord)
        {
            try
            {
                var lordParty = lord?.PartyBelongedTo;
                var main = MobileParty.MainParty;
                if (lordParty == null || main == null) return;
                main.SetMoveEscortParty(lordParty, MobileParty.NavigationType.Default, false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Release paths ─────────────────────────────────────────────────────
        // Drop the escort and leave the realm's service, like ending a contract.
        private static void EndServiceCommon()
        {
            try
            {
                var clan = Hero.MainHero?.Clan;
                if (clan != null && clan.Kingdom != null && clan.Kingdom.StringId == _kingdomId)
                    ChangeKingdomAction.ApplyByLeaveKingdomAsMercenary(clan, false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try { MobileParty.MainParty?.SetMoveModeHold(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            _lordId = ""; _kingdomId = ""; _endDays = 0.0; _termDays = 0.0;
            _leavePromptOpen = false;
        }

        // (7) Full term served: parting bonus + clean release, no ill will.
        internal static void HonourableRelease(bool termCompleted)
        {
            var lord = ServingLord();
            try
            {
                if (termCompleted && lord != null)
                {
                    int pay   = SoldierServiceMath.WeeklyPay(MobileParty.MainParty?.TotalWage ?? 0, ClanTier());
                    int bonus = SoldierServiceMath.CompletionBonus(pay);
                    PayPlayer(lord, bonus);
                    try { ChangeRelationAction.ApplyPlayerRelation(lord, 3, false, true); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    try
                    {
                        MBInformationManager.AddQuickInformation(new TextObject(
                            $"Your term with {lord.Name} is served. You are released with {bonus}{{GOLD_ICON}} and their thanks."));
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
                else
                {
                    try
                    {
                        MBInformationManager.AddQuickInformation(new TextObject(
                            "Your service ends. Your party is your own again."));
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            EndServiceCommon();
        }

        // (6) Break the oath early: crime + relation loss, branded a deserter.
        internal static void Desert()
        {
            var lord = ServingLord();
            var kingdom = Kingdom.All?.FirstOrDefault(k => k.StringId == _kingdomId);
            try
            {
                double rem = DaysRemaining();
                int crime = SoldierServiceMath.DesertionCrime(rem, _termDays);
                int rel   = SoldierServiceMath.DesertionRelationLoss(rem, _termDays);

                if (kingdom != null)
                    try { ChangeCrimeRatingAction.Apply(kingdom, crime, true); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                if (lord != null)
                    try { ChangeRelationAction.ApplyPlayerRelation(lord, -rel, true, true); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                try
                {
                    string realm = kingdom?.Name?.ToString() ?? "the realm";
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"You desert {(lord != null ? lord.Name.ToString() : "your commander")}. {realm} brands you an oathbreaker."));
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            EndServiceCommon();
        }

        private static int ClanTier()
        {
            try { return Hero.MainHero?.Clan?.Tier ?? 0; } catch { return 0; }
        }

        private static void PayPlayer(Hero lord, int amount)
        {
            if (amount <= 0) return;
            try
            {
                // The commander pays from his own purse when he can; otherwise the
                // realm's coffers (its ruler) cover the wage.
                Hero payer = lord;
                if (payer == null || payer.Gold < amount)
                {
                    var kingdom = Kingdom.All?.FirstOrDefault(k => k.StringId == _kingdomId);
                    var ruler = kingdom?.Leader;
                    if (ruler != null && ruler.Gold >= amount) payer = ruler;
                }
                if (payer != null)
                    GiveGoldAction.ApplyBetweenCharacters(payer, Hero.MainHero, amount, true);
                else
                    // Nobody solvent to pay — the realm still owes it; grant it so the
                    // player is never shorted their agreed wage.
                    GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, amount, true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Event handlers ────────────────────────────────────────────────────
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            RegisterDialogue(starter);
            // Escort state is runtime-only; re-issue it after a load.
            try
            {
                var lord = ServingLord();
                if (InService && lord?.PartyBelongedTo != null) ReassertEscort(lord);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            try
            {
                if (InService && victim != null && victim.StringId == _lordId)
                {
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "Your commander is dead. Your oath dies with them."));
                    EndServiceCommon();
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private void OnWeeklyTick()
        {
            try
            {
                if (!InService) return;
                var lord = ServingLord();
                if (lord?.PartyBelongedTo == null) return; // hourly tick handles broken service
                int pay = SoldierServiceMath.WeeklyPay(MobileParty.MainParty?.TotalWage ?? 0, ClanTier());
                PayPlayer(lord, pay);
                MBInformationManager.AddQuickInformation(new TextObject(
                    $"{lord.Name} pays your week's soldiering: {pay}{{GOLD_ICON}}."));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Slow tick: validate the commander is still someone we can serve, keep the
        // player waiting inside a settlement the commander has entered, and honour
        // the term the moment it expires.
        private void OnHourlyTick()
        {
            try
            {
                if (!InService) return;

                var lord = ServingLord();
                var lordParty = lord?.PartyBelongedTo;
                if (lord == null || !lord.IsAlive || lord.IsPrisoner || lordParty == null)
                {
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "Your commander can no longer be followed. Your service ends."));
                    EndServiceCommon();
                    return;
                }

                // (7) Term served — release cleanly with the parting bonus.
                if (CampaignTime.Now.ToDays >= _endDays)
                {
                    HonourableRelease(true);
                    return;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Fast tick: keep formation with the commander and watch for the player
        // striking out on their own (a map click), which ends the service.
        private void OnTick(float dt)
        {
            try
            {
                if (!InService || _leavePromptOpen) return;

                var main = MobileParty.MainParty;
                var lord = ServingLord();
                var lordParty = lord?.PartyBelongedTo;
                if (main == null || lordParty == null) return;

                // Never interfere mid-encounter / mid-battle — the engine drives
                // movement then, and it must not read as the player leaving.
                if (PlayerEncounter.Current != null) return;
                if (MapEvent.PlayerMapEvent != null) return;

                // The player sitting in a settlement (waiting) is fine.
                if (main.CurrentSettlement != null)
                {
                    // …unless the commander has ridden back out to campaign, in which
                    // case fall back in behind him.
                    if (lordParty.CurrentSettlement == null)
                        ReassertEscort(lord);
                    return;
                }

                var behavior = main.DefaultBehavior;

                // The commander has holed up in a town/castle: follow him in and wait.
                if (lordParty.CurrentSettlement != null)
                {
                    bool followingIn = behavior == AiBehavior.GoToSettlement
                                       && main.TargetSettlement == lordParty.CurrentSettlement;
                    if (followingIn) return;
                    if (behavior == AiBehavior.Hold || behavior == AiBehavior.None || behavior == AiBehavior.EscortParty)
                    {
                        try { main.SetMoveGoToSettlement(lordParty.CurrentSettlement, MobileParty.NavigationType.Default, false); }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        return;
                    }
                    // Player steered elsewhere — treat as leaving.
                    PromptLeave(lord);
                    return;
                }

                // Commander is on the open map: we should be escorting him.
                if (behavior == AiBehavior.EscortParty) return;
                if (behavior == AiBehavior.Hold || behavior == AiBehavior.None)
                {
                    // Engine idled us (e.g. just after a battle) — re-form up.
                    ReassertEscort(lord);
                    return;
                }

                // (5) Any other behaviour means the player issued a manual order.
                PromptLeave(lord);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // (6) Ask before letting a click break the oath. Serving the term is never
        // reached here (the hourly tick releases at expiry), so leaving now is
        // always desertion — warn plainly.
        private static void PromptLeave(Hero lord)
        {
            if (_leavePromptOpen) return;
            _leavePromptOpen = true;
            try
            {
                double rem = DaysRemaining();
                int crime = SoldierServiceMath.DesertionCrime(rem, _termDays);
                int rel   = SoldierServiceMath.DesertionRelationLoss(rem, _termDays);
                string lordName = lord?.Name?.ToString() ?? "your commander";

                InformationManager.ShowInquiry(new InquiryData(
                    "Break Your Oath?",
                    $"You still owe {lordName} {Math.Max(0, (int)Math.Ceiling(rem))} days of service. "
                        + $"Ride off now and you desert: your crimes against {lordName}'s realm rise by {crime}, "
                        + $"and {lordName} will think the worse of you ({rel} relation). Abandon the banner?",
                    true, true, "Desert", "Return to the column",
                    () =>
                    {
                        _leavePromptOpen = false;
                        Desert();
                    },
                    () =>
                    {
                        // Stay: fall back into formation and cancel the stray order.
                        _leavePromptOpen = false;
                        try { ReassertEscort(ServingLord()); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    }),
                    true);
            }
            catch (System.Exception logEx)
            {
                _leavePromptOpen = false;
                AshAndEmber.ModLog.Error(logEx);
            }
        }
    }
}
