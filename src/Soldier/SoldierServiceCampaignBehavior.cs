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

        // Runtime-only: the service deal was sealed from inside the map meeting with
        // the commander. We must NOT change the player's faction or touch armies
        // while that encounter is live (it corrupts the encounter into a hostile
        // Attack/Surrender resolution and can crash). So the deal only flags the
        // meeting to dissolve, and the first clean map tick (no encounter, no battle)
        // finalises it: mercenary contract + folding into the host. See OnTick.
        private static bool _finalizePending = false;

        // Runtime-only: we raised a host for the commander ourselves (he led none
        // when the player took service), so it is ours to dissolve when the service
        // ends. A commander's own gathered army is never touched.
        private static bool _armyIsOurs = false;

        // Runtime-only: raising a host for this commander threw once, so stop trying
        // and just ride escort instead (avoids a per-tick crash loop).
        private static bool _armyCreateFailed = false;

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
            _finalizePending = false;
            _armyIsOurs = false;
            _armyCreateFailed = false;
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
        // Runs as a conversation consequence, so we are still INSIDE the map meeting
        // with the lord. Changing the player's faction or touching armies here
        // corrupts that encounter (the engine then offers a hostile Attack/Surrender
        // resolution, and can crash). So we only record the deal and ask the meeting
        // to dissolve cleanly; FinalizeJoin (next clean tick) does the real work.
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
                _armyIsOurs = false;
                _armyCreateFailed = false;

                // Dissolve the meeting when the conversation closes instead of
                // presenting its encounter menu (Attack/Leave/Surrender).
                try { if (PlayerEncounter.Current != null) PlayerEncounter.LeaveEncounter = true; }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                _finalizePending = true;

                try
                {
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"You take {lord.Name}'s coin. Your party marches under the banner of {kingdom.Name} for {termDays} days."));
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Finish the join once we are safely back on the map (no live encounter, no
        // battle): become a mercenary of the realm, then fold into the host.
        private static void FinalizeJoin()
        {
            try
            {
                var lord = ServingLord();
                if (lord?.Clan?.Kingdom == null || lord.PartyBelongedTo == null)
                {
                    // Commander vanished between sealing and finalising — abort cleanly.
                    EndServiceCommon();
                    return;
                }
                var kingdom = lord.Clan.Kingdom;
                var clan    = Hero.MainHero?.Clan;
                if (clan == null) { EndServiceCommon(); return; }

                // (4) Treated as a mercenary of the realm, exactly like a contract.
                if (clan.Kingdom == null)
                {
                    try
                    {
                        int award = clan.MercenaryAwardMultiplier > 0 ? clan.MercenaryAwardMultiplier : 1;
                        int rem   = Math.Max(1, (int)Math.Ceiling(_endDays - CampaignTime.Now.ToDays));
                        ChangeKingdomAction.ApplyByJoinFactionAsMercenary(
                            clan, kingdom, CampaignTime.DaysFromNow(rem), award, false);
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }

                // (3) Fall in and march in the commander's host.
                ReassertArmy(lord);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Pick a valid settlement for a raised host to nominally target. Every step
        // is null-guarded; returns null only if nothing usable exists (in which case
        // we do not raise a host at all).
        private static Settlement PickArmyObjective(Hero lord, MobileParty lordParty, Kingdom kingdom)
        {
            try
            {
                var s = lordParty?.TargetSettlement;
                if (s != null) return s;

                s = lordParty?.ShortTermTargetSettlement;
                if (s != null) return s;

                s = lordParty?.CurrentSettlement;
                if (s != null) return s;

                s = lord?.HomeSettlement;
                if (s != null) return s;

                s = lordParty?.HomeSettlement;
                if (s != null) return s;

                s = kingdom?.FactionMidSettlement;
                if (s != null) return s;

                if (kingdom?.Fiefs != null)
                {
                    foreach (var town in kingdom.Fiefs)
                    {
                        var ts = town?.Settlement;
                        if (ts != null) return ts;
                    }
                }

                if (kingdom?.Settlements != null)
                {
                    foreach (var set in kingdom.Settlements)
                        if (set != null) return set;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            return null;
        }

        // ── Army membership ───────────────────────────────────────────────────
        // Fold the player's party into the commander's host so they truly march
        // with the company: moved by the army leader, and pulled into his battles
        // on his side (earning renown/XP/loot) — exactly like any lord who answers
        // a gathering-army call. If the commander leads no host of his own, we raise
        // a small one for him (ours to dissolve when the service ends); if that ever
        // fails we fall back to riding escort so the player at least travels with him.
        // Idempotent and self-healing: safe to call every clean map tick.
        //
        // Never call this while a PlayerEncounter is live — attaching a party to an
        // army mid-encounter corrupts the encounter.
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
                // the same side.) A hand-built army MUST be given a valid objective
                // (AiBehaviorObject) before anything reads it, or the army overlay UI
                // NREs describing it — so if we can't find a settlement to target, we
                // don't fabricate an army at all and just ride escort instead.
                if (army == null && !_armyCreateFailed)
                {
                    var kingdom = lordParty.ActualClan?.Kingdom ?? lord.Clan?.Kingdom;
                    var objective = PickArmyObjective(lord, lordParty, kingdom);
                    if (kingdom != null && !kingdom.IsEliminated && objective != null)
                    {
                        _internalArmyChange = true;
                        try
                        {
                            var raised = new Army(kingdom, lordParty, Army.ArmyTypes.Patrolling);
                            if (raised != null)
                            {
                                // Give it a target so the AI and the overlay UI have
                                // something non-null to work with.
                                try { raised.AiBehaviorObject = objective; }
                                catch (System.Exception ex) { AshAndEmber.ModLog.Error(ex); }
                            }
                        }
                        catch (System.Exception ex) { _armyCreateFailed = true; AshAndEmber.ModLog.Error(ex); }
                        finally { _internalArmyChange = false; }

                        army = lordParty.Army;
                        if (army != null)
                        {
                            _armyIsOurs = true;
                            // Belt-and-braces: never leave the objective null.
                            try { if (army.AiBehaviorObject == null) army.AiBehaviorObject = objective; }
                            catch (System.Exception ex) { AshAndEmber.ModLog.Error(ex); }
                        }
                    }
                }

                if (army != null)
                {
                    if (main.Army != army)
                    {
                        _internalArmyChange = true;
                        try
                        {
                            // Register membership FIRST, then attach. AddPartyToMerged-
                            // Parties sets AttachedTo (firing vanilla's statistics hook)
                            // before it would set Army, and that hook dereferences
                            // MainParty.Army — so for the *player's own* party it NREs
                            // unless MainParty.Army is already set. Setting it up front
                            // (as the join flow expects) avoids the crash; the attach
                            // then also hands map control to the army via OnJoinArmy.
                            main.Army = army;
                            army.AddPartyToMergedParties(main);
                        }
                        catch (System.Exception ex) { AshAndEmber.ModLog.Error(ex); }
                        finally { _internalArmyChange = false; }
                    }
                    return;
                }

                // No host to join (couldn't raise one) — ride escort so we still
                // travel together; the tick will keep trying to form a host.
                try { main.SetMoveEscortParty(lordParty, MobileParty.NavigationType.Default, false); }
                catch (System.Exception ex) { AshAndEmber.ModLog.Error(ex); }
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
            _finalizePending = false;
            _armyIsOurs = false;
            _armyCreateFailed = false;
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

                // Stay in the column and keep any host we raised from dispersing —
                // but only once the join has finalised and no encounter is live.
                if (!_finalizePending && !_leavePromptOpen && !IsEncounterLive())
                {
                    ReassertArmy(lord);
                    SustainArmy(lordParty);
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // True if the player is inside a map encounter or battle right now. We must
        // never change faction or touch armies while this holds.
        private static bool IsEncounterLive()
        {
            try
            {
                if (PlayerEncounter.Current != null) return true;
                if (MapEvent.PlayerMapEvent != null) return true;
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            return false;
        }

        // Fast tick: finalise the join once the meeting has closed, then keep the
        // player folded into the commander's host (movement, settlement stops, and
        // battle-joining are all the army's job once the player is a member).
        private void OnTick(float dt)
        {
            try
            {
                if (!InService) return;

                // The deal was sealed inside the map meeting with the commander; we
                // deliberately did NOT change faction or touch armies there. Wait for
                // the meeting to fully close (the conversation has ended by the time
                // this tick fires, and we asked the encounter to dissolve), then do
                // the real join on solid ground — no Attack/Surrender menu, no crash.
                if (_finalizePending)
                {
                    if (IsEncounterLive())
                    {
                        // Meeting still resolving — keep asking it to dissolve, wait.
                        try { PlayerEncounter.LeaveEncounter = true; }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        return;
                    }
                    _finalizePending = false;
                    FinalizeJoin();
                    return;
                }

                if (_leavePromptOpen) return;

                // Never interfere mid-encounter / mid-battle — the engine drives
                // things then, and joining/leaving a host must not race it.
                if (IsEncounterLive()) return;

                var main = MobileParty.MainParty;
                var lord = ServingLord();
                var lordParty = lord?.PartyBelongedTo;
                if (main == null || lordParty == null) return;

                // Already marching in the commander's host — nothing to do.
                if (main.Army != null && main.Army == lordParty.Army) return;

                // Otherwise fold back in (host dispersed under us, or the commander
                // gathered/joined a new one, or we're riding escort as a fallback).
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
