// =============================================================================
// ASH AND EMBER — Soldier/SoldierServiceCampaignBehavior.cs
// "Take the Lord's Coin" — hire your own party out to a warring commander as a
// common soldier. Unlike a mercenary contract (clan tier 1+), a sworn sword can
// take service from clan level 0.
//
// While in service the player marches *in the commander's host* — a real member
// of his army, moved by the army leader and pulled into his battles on his side,
// earning renown/XP/loot like any lord who answered a gathering call. If the
// commander leads no host of his own, one is raised under his banner for the term
// (and dissolved when it ends). The player is treated as a mercenary of his realm
// and is paid every week (party upkeep + a small bounty). Abandoning the host (the
// vanilla "Abandon Army" button) before the agreed term is desertion (crime +
// relation loss), and the player is warned first. Serving the full term pays a
// parting bonus and releases you cleanly, like a mercenary contract, with no
// lasting allegiance.
//
// Dialogue lives in the .Dialogue.cs partial; the tick/army/pay logic is here.
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

        // Runtime-only: a service deal was just sealed inside the map encounter with
        // the commander. The next map tick steps off that encounter so the player
        // can be folded into the host (see OnTick) instead of being dropped onto the
        // encounter's Attack/Retreat menu.
        private static bool _finishEncounterPending = false;

        // Runtime-only: we raised a host for the commander ourselves (he led none
        // when the player took service), so it is ours to dissolve when the service
        // ends. A commander's own gathered army is never touched.
        private static bool _armyIsOurs = false;

        // Runtime-only re-entrancy guard: set while WE add/remove the player's party
        // to/from a host, so our own OnPartyLeftArmy handler doesn't mistake an
        // internal detach for the player abandoning the column.
        private static bool _internalArmyChange = false;

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
            CampaignEvents.OnPartyLeftArmyEvent.AddNonSerializedListener(this, OnPartyLeftArmy);
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
            _finishEncounterPending = false;
            _armyIsOurs = false;
            _internalArmyChange = false;
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

                // (3) Fall in and march *in the commander's host*. The deal is
                // sealed from inside the map encounter with the lord; the next map
                // tick steps off that encounter (see OnTick) and only then folds the
                // player's party into the army — so the player isn't dropped onto the
                // encounter's Attack/Retreat menu, and joining an army mid-encounter
                // is avoided.
                _finishEncounterPending = true;

                try
                {
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"You take {lord.Name}'s coin. Your party marches under the banner of {kingdom.Name} for {termDays} days."));
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Army membership ───────────────────────────────────────────────────
        // Fold the player's party into the commander's host so they truly march
        // with the company: moved by the army leader, and pulled into his battles
        // on his side (earning renown/XP/loot) — exactly like any lord who answers
        // a gathering-army call. If the commander leads no host of his own, we raise
        // a small one for him (ours to dissolve when the service ends). Idempotent
        // and self-healing: safe to call every tick.
        private static void ReassertArmy(Hero lord)
        {
            try
            {
                var lordParty = lord?.PartyBelongedTo;
                var main = MobileParty.MainParty;
                if (lordParty == null || main == null || main == lordParty) return;

                var army = lordParty.Army;

                // The commander is soldiering alone — raise a host under his banner
                // so the player can serve in it. (Attaching to a party outside an
                // army does not fight as one; only army members auto-join battles on
                // the same side.)
                if (army == null)
                {
                    var kingdom = lordParty.ActualClan?.Kingdom ?? lord.Clan?.Kingdom;
                    if (kingdom == null || kingdom.IsEliminated) return;

                    _internalArmyChange = true;
                    try
                    {
                        var raised = new Army(kingdom, lordParty, Army.ArmyTypes.Patrolling);
                        // The ctor normally binds the leader; assign defensively in
                        // case a game version leaves it unset, so we don't loop.
                        if (lordParty.Army == null) lordParty.Army = raised;
                    }
                    finally { _internalArmyChange = false; }

                    army = lordParty.Army;
                    if (army == null) return; // construction failed — bail, try again next tick
                    _armyIsOurs = true;
                }

                if (main.Army != army)
                {
                    _internalArmyChange = true;
                    try { army.AddPartyToMergedParties(main); }
                    finally { _internalArmyChange = false; }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Keep the host we raised from dispersing under the player (a lone-commander
        // patrolling army bleeds cohesion). A commander's own gathered army is left
        // to the campaign AI. Called on the slow tick.
        private static void SustainArmy(MobileParty lordParty)
        {
            try
            {
                if (!_armyIsOurs || lordParty == null) return;
                var army = lordParty.Army;
                if (army == null || army.LeaderParty != lordParty) return;
                if (army.Cohesion < 90f) army.Cohesion = 100f;
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Pull the player's party out of the host and, if the host was one we raised
        // for the service, dissolve it. Guarded so the resulting OnPartyLeftArmy does
        // not read as the player abandoning the column.
        private static void DetachFromArmy()
        {
            try
            {
                var main = MobileParty.MainParty;
                if (main == null) { _armyIsOurs = false; return; }

                var army = main.Army;
                _internalArmyChange = true;
                try
                {
                    if (army != null) main.Army = null;
                    if (_armyIsOurs && army != null)
                    {
                        try { DisbandArmyAction.ApplyByObjectiveFinished(army); }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    }
                }
                finally { _internalArmyChange = false; }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            _armyIsOurs = false;
        }

        // ── Release paths ─────────────────────────────────────────────────────
        // Leave the host, leave the realm's service, and take back command of the
        // party, like ending a mercenary contract.
        private static void EndServiceCommon()
        {
            // Step out of the column first (and dissolve any host we raised) so the
            // party is free before it leaves the realm.
            DetachFromArmy();

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
            _finishEncounterPending = false;
            _armyIsOurs = false;
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
            // A raised host is serialised by the engine, but our "this host is ours"
            // flag is runtime-only; re-fold the player in after a load so the march
            // resumes even if the flag was lost.
            try
            {
                var lord = ServingLord();
                if (InService && lord?.PartyBelongedTo != null) ReassertArmy(lord);
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
        // player marching in his host, and honour the term the moment it expires.
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

                // Stay in the column and keep any host we raised from dispersing.
                if (!_finishEncounterPending && !_leavePromptOpen)
                {
                    ReassertArmy(lord);
                    SustainArmy(lordParty);
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Fast tick: fold the player into the commander's host and keep them there.
        // Movement, settlement stops, and battle-joining are all the army's job once
        // the player is a member — so this tick only has to seal the join encounter
        // and self-heal the membership if the engine ever drops it.
        private void OnTick(float dt)
        {
            try
            {
                if (!InService) return;

                // A service deal was just sealed from inside the map encounter with
                // the commander. Step off that encounter now (the conversation has
                // closed by the time this tick fires), then fold the player's party
                // into the host — instead of leaving the player on the encounter's
                // Attack/Retreat menu.
                if (_finishEncounterPending)
                {
                    _finishEncounterPending = false;
                    try
                    {
                        if (PlayerEncounter.Current != null) PlayerEncounter.Finish(true);
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    ReassertArmy(ServingLord());
                    return;
                }

                if (_leavePromptOpen) return;

                // Never interfere mid-encounter / mid-battle — the engine drives
                // things then, and joining/leaving a host must not race it.
                if (PlayerEncounter.Current != null) return;
                if (MapEvent.PlayerMapEvent != null) return;

                var main = MobileParty.MainParty;
                var lord = ServingLord();
                var lordParty = lord?.PartyBelongedTo;
                if (main == null || lordParty == null) return;

                // Already marching in the commander's host — nothing to do.
                if (main.Army != null && main.Army == lordParty.Army) return;

                // Otherwise fold back in (first join after the encounter, the host
                // dispersed under us, or the commander gathered/joined a new one).
                ReassertArmy(lord);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // The player pulled their party out of a host (the vanilla "Abandon Army"
        // button). If it was the commander's living host they chose to leave — which,
        // before the term is up, is desertion. A host merely dispersing under them
        // (leader gone / not enough parties) is not the player's doing, so the tick
        // self-heal folds them into the commander's next host instead.
        private void OnPartyLeftArmy(MobileParty party, Army army)
        {
            try
            {
                if (_internalArmyChange || !InService) return;
                if (party == null || party != MobileParty.MainParty) return;

                var lord = ServingLord();
                var lordParty = lord?.PartyBelongedTo;
                if (lord == null || !lord.IsAlive || lord.IsPrisoner || lordParty == null) return;

                // Host being torn down rather than left — let the self-heal re-form up.
                bool hostStillAlive = false;
                try { hostStillAlive = army != null && army.LeaderParty != null && army.Kingdom != null; }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                if (!hostStillAlive) return;

                // Term already served the moment they stepped out — release cleanly.
                if (CampaignTime.Now.ToDays >= _endDays) { HonourableRelease(true); return; }

                // Otherwise, warn: leaving now breaks the oath.
                PromptLeave(lord);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // (6) Ask before letting the player break the oath. Serving the full term is
        // released elsewhere (the hourly tick / a served-term discharge), so leaving
        // here is always desertion — warn plainly.
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
                        // Stay: fall back into the column.
                        _leavePromptOpen = false;
                        try { ReassertArmy(ServingLord()); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
