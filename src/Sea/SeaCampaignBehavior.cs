// =============================================================================
// ASH AND EMBER — Sea/SeaCampaignBehavior.cs
//
// Sea travel, sea trade, and abstracted sea battles.
//
// Harbors: a curated set of coastal towns gains a "Visit the harbor" entry in
// the town menu. From the harbor the player can:
//   • Charter passage — pay a distance-scaled fare, then wait out the crossing
//     in a wait-menu voyage. Mid-voyage hazards roll once each: a storm (lost
//     hours, wounded men) and corsairs (an abstracted boarding battle resolved
//     by SeaMath, with fight / spell / tribute / flee choices).
//   • Fund a trade venture — invest denars on a route; the cog returns after
//     SeaMath.VentureDays and pays out (or is lost to the sea) on daily tick.
//   • Call the Emberwind (mages) — spend aging days to halve the next crossing
//     and ward it against storms.
//
// All numerical resolution lives in SeaMath.cs (pure, tested). This file only
// gathers inputs from the campaign, applies outcomes, and drives the menus.
// Ports are matched by town name so a wrong entry degrades to "no harbor"
// rather than a crash; localized town names simply disable the system.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Overlay;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public class SeaCampaignBehavior : CampaignBehaviorBase
    {
        // ── Harbor towns ───────────────────────────────────────────────────────
        // Coastal towns of Calradia, matched by name at session launch. A name
        // that fails to resolve (renamed by another mod, localized client) just
        // drops that port from the network.
        private static readonly string[] PortTownNames =
        {
            "Balgard", "Varcheg", "Revyl",                 // Sturgia — the cold coast
            "Sargot", "Ostican", "Pravend",                // Vlandia — the western sea
            "Ortysia", "Zeonica",                          // Western Empire
            "Epicrotea", "Diathma", "Saneopa", "Myzea",    // Northern Empire — the Perassic
            "Vostrum",                                     // Southern Empire
            "Quyaz", "Razih", "Iyakis",                    // Aserai — the southern waters
        };

        private static readonly List<Settlement> _ports = new List<Settlement>();
        private static readonly Random _rng = new Random();

        // ── Voyage state (transient — a reload mid-voyage refunds the fare) ───
        private static Settlement _voyageOrigin;
        private static Settlement _voyageDest;
        private static float _voyageHoursTotal;
        private static float _voyageHoursElapsed;
        private static float _pirateAtHour = -1f;   // -1 → no corsairs this crossing
        private static float _stormAtHour  = -1f;
        private static bool  _voyageEmberwind;
        private static bool  _voyageDone;
        private static int   _fareEscrow;           // persisted; refunded if a reload strands the voyage

        // Emberwind bought in the harbor, consumed by the next voyage this session.
        private static bool _emberwindCalled;

        // ── Trade ventures (persisted) ─────────────────────────────────────────
        private class Venture
        {
            public string DestName;
            public int    DaysLeft;
            public int    Invested;
            public bool   Blessed;
            public float  Distance;
        }
        private static readonly List<Venture> _ventures = new List<Venture>();

        // ── CampaignBehaviorBase ───────────────────────────────────────────────
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore store)
        {
            try { store.SyncData("SEA_FareEscrow", ref _fareEscrow); } catch { }

            try
            {
                var dests    = _ventures.Select(v => v.DestName).ToList();
                var days     = _ventures.Select(v => v.DaysLeft).ToList();
                var invested = _ventures.Select(v => v.Invested).ToList();
                var blessed  = _ventures.Select(v => v.Blessed ? 1 : 0).ToList();
                var dist     = _ventures.Select(v => v.Distance).ToList();
                store.SyncData("SEA_VentureDests",    ref dests);
                store.SyncData("SEA_VentureDays",     ref days);
                store.SyncData("SEA_VentureInvested", ref invested);
                store.SyncData("SEA_VentureBlessed",  ref blessed);
                store.SyncData("SEA_VentureDist",     ref dist);

                if (dests != null && days != null && invested != null && blessed != null && dist != null
                    && dests.Count == days.Count && dests.Count == invested.Count
                    && dests.Count == blessed.Count && dests.Count == dist.Count)
                {
                    _ventures.Clear();
                    for (int i = 0; i < dests.Count; i++)
                        _ventures.Add(new Venture
                        {
                            DestName = dests[i],
                            DaysLeft = days[i],
                            Invested = invested[i],
                            Blessed  = blessed[i] != 0,
                            Distance = dist[i],
                        });
                }
            }
            catch { }
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            try { ResolvePorts(); } catch { }
            try { RegisterHarborMenus(starter); } catch { }

            // Voyage state is not serialized: a save made mid-crossing reloads
            // with the party still docked at the origin. Return the fare.
            try
            {
                ResetVoyageState();
                if (_fareEscrow > 0)
                {
                    GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, _fareEscrow, true);
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"The harbormaster returns your fare of {_fareEscrow} denars — the ship never sailed.",
                        new Color(0.65f, 0.75f, 0.9f)));
                    _fareEscrow = 0;
                }
            }
            catch { }
        }

        private void OnDailyTick()
        {
            try { TickVentures(); } catch { }
        }

        // ── Port resolution ────────────────────────────────────────────────────
        private static void ResolvePorts()
        {
            _ports.Clear();
            if (Campaign.Current == null) return;
            foreach (var s in Settlement.All)
            {
                if (s == null || !s.IsTown) continue;
                string name = null;
                try { name = s.Name?.ToString(); } catch { }
                if (string.IsNullOrEmpty(name)) continue;
                if (PortTownNames.Any(p => string.Equals(p, name.Trim(), StringComparison.OrdinalIgnoreCase)))
                    _ports.Add(s);
            }
        }

        private static bool IsPort(Settlement s) => s != null && _ports.Contains(s);

        private static float PortDistance(Settlement a, Settlement b)
        {
            try { return (a.GetPosition2D - b.GetPosition2D).Length; }
            catch { return 0f; }
        }

        // ── Party readings for SeaMath ─────────────────────────────────────────
        private static float PlayerFleetStrength(bool searTheTide)
        {
            int troops = 0; float tierSum = 0f;
            try
            {
                foreach (var e in MobileParty.MainParty.MemberRoster.GetTroopRoster())
                {
                    if (e.Character == null) continue;
                    int healthy = e.Number - e.WoundedNumber;
                    if (healthy <= 0) continue;
                    troops  += healthy;
                    tierSum += healthy * e.Character.Tier;
                }
            }
            catch { }
            float avgTier = troops > 0 ? tierSum / troops : 0f;
            int tactics = 0;
            try { tactics = Hero.MainHero.GetSkillValue(DefaultSkills.Tactics); } catch { }
            return SeaMath.FleetStrength(troops, avgTier, tactics, searTheTide);
        }

        // Strikes a fraction of the party's healthy regulars: ~60% wounded,
        // the rest lost to the water. Returns the number of men affected.
        private static int ApplySeaCasualties(float fraction)
        {
            int affected = 0;
            try
            {
                var roster = MobileParty.MainParty.MemberRoster;
                int totalHealthy = 0;
                foreach (var e in roster.GetTroopRoster().ToList())
                {
                    if (e.Character == null || e.Character.IsHero) continue;
                    totalHealthy += Math.Max(0, e.Number - e.WoundedNumber);
                }
                if (totalHealthy <= 0) return 0;

                int toHit   = Math.Max(1, (int)(totalHealthy * fraction));
                int toKill  = toHit * 2 / 5;
                int toWound = toHit - toKill;

                foreach (var e in roster.GetTroopRoster().ToList())
                {
                    if (toWound <= 0 && toKill <= 0) break;
                    if (e.Character == null || e.Character.IsHero) continue;
                    int healthy = e.Number - e.WoundedNumber;
                    if (healthy <= 0) continue;

                    int w = Math.Min(healthy, toWound);
                    if (w > 0)
                    {
                        try { roster.AddToCounts(e.Character, 0, false, w); toWound -= w; affected += w; healthy -= w; } catch { }
                    }
                    int k = Math.Min(healthy, toKill);
                    if (k > 0)
                    {
                        try { roster.AddToCounts(e.Character, -k); toKill -= k; affected += k; } catch { }
                    }
                }
            }
            catch { }
            return affected;
        }

        // ── Menu registration ──────────────────────────────────────────────────
        private static void RegisterHarborMenus(CampaignGameStarter starter)
        {
            // Entry in town menu
            try
            {
                starter.AddGameMenuOption("town", "sea_harbor_enter", "{SEA_ENTER_TEXT}",
                    args =>
                    {
                        try
                        {
                            if (_ports.Count < 2 || !IsPort(Settlement.CurrentSettlement)) return false;
                            MBTextManager.SetTextVariable("SEA_ENTER_TEXT", "Visit the harbor");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; } catch { }
                            args.IsEnabled = true;
                            return true;
                        }
                        catch { return false; }
                    },
                    args => { try { GameMenu.SwitchToMenu("sea_harbor"); } catch { } },
                    false, -1, false);
            }
            catch { }

            // Harbor menu header
            try
            {
                starter.AddGameMenu("sea_harbor", "{SEA_HARBOR_HEADER}", args =>
                {
                    try
                    {
                        string ventureNote = "";
                        if (_ventures.Count > 0)
                            ventureNote = "  " + string.Join("  ", _ventures.Select(v =>
                                $"[Venture to {v.DestName}: {v.DaysLeft} day(s) out, {v.Invested} denars{(v.Blessed ? ", blessed" : "")}]"));
                        string windNote = _emberwindCalled ? "  [The Emberwind is called — the next crossing will be swift and stormless]" : "";
                        MBTextManager.SetTextVariable("SEA_HARBOR_HEADER",
                            "The harbor. Gulls argue over fish guts, ropes creak against the tide, " +
                            "and captains weigh your purse from across the quay." + windNote + ventureNote);
                    }
                    catch { }
                });
            }
            catch { }

            // ── Charter passage ─────────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("sea_harbor", "sea_charter", "{SEA_CHARTER_TEXT}",
                    args =>
                    {
                        try
                        {
                            MBTextManager.SetTextVariable("SEA_CHARTER_TEXT",
                                "Charter passage to another port" + (_emberwindCalled ? " (Emberwind called)" : ""));
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => ShowDestinationPicker(forTrade: false));
            }
            catch { }

            // ── Fund a trade venture ────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("sea_harbor", "sea_venture", "{SEA_VENTURE_TEXT}",
                    args =>
                    {
                        try
                        {
                            string full = _ventures.Count >= SeaMath.MaxActiveVentures
                                ? $"  [All your factors are at sea — {_ventures.Count} venture(s) out]" : "";
                            if (full.Length > 0) args.IsEnabled = false;
                            MBTextManager.SetTextVariable("SEA_VENTURE_TEXT", "Fund a trade venture" + full);
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => ShowDestinationPicker(forTrade: true));
            }
            catch { }

            // ── Call the Emberwind (mages) ──────────────────────────────────
            try
            {
                starter.AddGameMenuOption("sea_harbor", "sea_emberwind", "{SEA_WIND_TEXT}",
                    args =>
                    {
                        try
                        {
                            if (!MageKnowledge.IsMage) return false;
                            if (_emberwindCalled) args.IsEnabled = false;
                            MBTextManager.SetTextVariable("SEA_WIND_TEXT",
                                _emberwindCalled
                                    ? "Call the Emberwind  [already called — it waits in the rigging]"
                                    : $"Call the Emberwind ({SeaMath.EmberwindAgingDays} days aging) — halve the next crossing and ward it against storms");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { return false; }
                        return true;
                    },
                    args =>
                    {
                        try
                        {
                            if (_emberwindCalled) return;
                            AgingSystem.AgeHero(Hero.MainHero, SeaMath.EmberwindAgingDays);
                            _emberwindCalled = true;
                            MBInformationManager.AddQuickInformation(new TextObject(
                                "You breathe a thread of the Inner Fire into the sky. The pennants snap taut toward open water."));
                            try { GameMenu.SwitchToMenu("sea_harbor"); } catch { }
                        }
                        catch { }
                    });
            }
            catch { }

            // ── Leave ───────────────────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("sea_harbor", "sea_leave", "Back to the town",
                    args =>
                    {
                        try { args.optionLeaveType = GameMenuOption.LeaveType.Leave; } catch { }
                        return true;
                    },
                    args => { try { GameMenu.SwitchToMenu("town"); } catch { } },
                    true, -1, false);
            }
            catch { }

            // ── Voyage wait menu ────────────────────────────────────────────
            try
            {
                starter.AddWaitGameMenu("sea_voyage", "{SEA_VOYAGE_TEXT}",
                    new OnInitDelegate(VoyageOnInit),
                    new OnConditionDelegate(VoyageOnCondition),
                    new OnConsequenceDelegate(VoyageOnConsequence),
                    new OnTickDelegate(VoyageOnTick),
                    GameMenu.MenuAndOptionType.WaitMenuShowOnlyProgressOption,
                    GameOverlays.MenuOverlayType.None, 0f, GameMenu.MenuFlags.None, null);
            }
            catch { }
        }

        // ── Destination picking (shared by passage and ventures) ──────────────
        private static void ShowDestinationPicker(bool forTrade)
        {
            try
            {
                var here = Settlement.CurrentSettlement;
                if (!IsPort(here)) return;

                var options = new List<InquiryElement>();
                foreach (var p in _ports)
                {
                    if (p == here) continue;
                    float dist = PortDistance(here, p);
                    string hover;
                    if (forTrade)
                    {
                        int days = SeaMath.VentureDays(dist);
                        int risk = (int)(SeaMath.VentureLossChance(dist, false) * 100f);
                        hover = $"A factor sails, trades, and returns in about {days} days. Roughly {risk}% of cargoes on this route never come home.";
                    }
                    else
                    {
                        int fare  = SeaMath.Fare(dist, PartySize());
                        int hours = (int)SeaMath.TravelHours(dist, _emberwindCalled);
                        int risk  = (int)(SeaMath.PirateChance(dist) * 100f);
                        hover = $"Fare {fare} denars. About {hours} hours at sea. Corsair risk roughly {risk}%.";
                    }
                    string faction = "";
                    try { faction = p.MapFaction?.Name?.ToString() ?? ""; } catch { }
                    options.Add(new InquiryElement(p, $"{p.Name} ({faction})", null, true, hover));
                }
                if (options.Count == 0) return;

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    forTrade ? "Fund a Trade Venture" : "Charter Passage",
                    forTrade
                        ? "Choose the route. Your factor buys here, sells there, and sails back with whatever the sea allows."
                        : "Choose your destination. The captain wants the fare up front — drowned men pay nothing.",
                    options, true, 1, 1, "Choose", "Never mind",
                    chosen =>
                    {
                        var dest = chosen?[0]?.Identifier as Settlement;
                        if (dest == null) return;
                        if (forTrade) StartVenture(dest);
                        else StartVoyage(dest);
                    },
                    null, "", false), false, true);
            }
            catch { }
        }

        private static int PartySize()
        {
            try { return MobileParty.MainParty.MemberRoster.TotalManCount; } catch { return 1; }
        }

        // =====================================================================
        // SEA TRAVEL
        // =====================================================================
        private static void StartVoyage(Settlement dest)
        {
            try
            {
                var here = Settlement.CurrentSettlement;
                if (!IsPort(here) || !IsPort(dest) || here == dest) return;

                float dist = PortDistance(here, dest);
                int fare = SeaMath.Fare(dist, PartySize());
                if (Hero.MainHero.Gold < fare)
                {
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"The captain looks at your purse and shakes his head. The fare is {fare} denars."));
                    return;
                }

                GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, fare, true);
                _fareEscrow = fare;

                _voyageOrigin       = here;
                _voyageDest         = dest;
                _voyageEmberwind    = _emberwindCalled;
                _emberwindCalled    = false;
                _voyageHoursTotal   = SeaMath.TravelHours(dist, _voyageEmberwind);
                _voyageHoursElapsed = 0f;
                _voyageDone         = false;

                // Roll the crossing's hazards up front and schedule them at a
                // random point in the middle stretch of the voyage.
                _pirateAtHour = _rng.NextDouble() < SeaMath.PirateChance(dist)
                    ? _voyageHoursTotal * (0.25f + 0.5f * (float)_rng.NextDouble()) : -1f;
                _stormAtHour = !_voyageEmberwind && _rng.NextDouble() < SeaMath.StormChancePerVoyage
                    ? _voyageHoursTotal * (0.25f + 0.5f * (float)_rng.NextDouble()) : -1f;

                GameMenu.SwitchToMenu("sea_voyage");
            }
            catch { }
        }

        private static void ResetVoyageState()
        {
            _voyageOrigin = null;
            _voyageDest = null;
            _voyageHoursTotal = 0f;
            _voyageHoursElapsed = 0f;
            _pirateAtHour = -1f;
            _stormAtHour = -1f;
            _voyageEmberwind = false;
            _voyageDone = false;
        }

        private static void VoyageOnInit(MenuCallbackArgs args)
        {
            try
            {
                UpdateVoyageText();
                args.MenuContext.GameMenu.StartWait();
                args.MenuContext.GameMenu.SetTargetedWaitingTimeAndInitialProgress(
                    Math.Max(1f, _voyageHoursTotal), 0f);
            }
            catch { }
        }

        private static bool VoyageOnCondition(MenuCallbackArgs args) => true;

        private static void VoyageOnConsequence(MenuCallbackArgs args)
        {
            // The engine fires this when the hours targeted at init elapse —
            // but storms and failed escapes lengthen the crossing mid-voyage.
            // If there is water left, re-arm the wait instead of arriving early.
            try
            {
                if (_voyageDone || _voyageDest == null) return;
                float remaining = _voyageHoursTotal - _voyageHoursElapsed;
                if (remaining <= 0.01f) { Arrive(); return; }
                args.MenuContext.GameMenu.StartWait();
                args.MenuContext.GameMenu.SetTargetedWaitingTimeAndInitialProgress(
                    Math.Max(1f, remaining), 0f);
            }
            catch { try { Arrive(); } catch { } }
        }

        private static void VoyageOnTick(MenuCallbackArgs args, CampaignTime dt)
        {
            try
            {
                if (_voyageDone || _voyageDest == null) return;
                _voyageHoursElapsed += (float)dt.ToHours;

                if (_stormAtHour >= 0f && _voyageHoursElapsed >= _stormAtHour)
                {
                    _stormAtHour = -1f;
                    FireStorm();
                }
                if (_pirateAtHour >= 0f && _voyageHoursElapsed >= _pirateAtHour)
                {
                    _pirateAtHour = -1f;
                    FireCorsairs();
                }

                UpdateVoyageText();
                try
                {
                    args.MenuContext.GameMenu.SetProgressOfWaitingInMenu(
                        Math.Min(1f, _voyageHoursElapsed / Math.Max(1f, _voyageHoursTotal)));
                }
                catch { }

                if (_voyageHoursElapsed >= _voyageHoursTotal)
                    Arrive();
            }
            catch { }
        }

        private static void UpdateVoyageText()
        {
            try
            {
                int left = Math.Max(0, (int)(_voyageHoursTotal - _voyageHoursElapsed));
                string wind = _voyageEmberwind ? " The Emberwind hums in the rigging." : "";
                MBTextManager.SetTextVariable("SEA_VOYAGE_TEXT",
                    $"At sea, bound for {_voyageDest?.Name}.{wind} The coast is a smudge behind you. About {left} hour(s) of water remain.");
            }
            catch { }
        }

        private static void Arrive()
        {
            if (_voyageDone) return;
            _voyageDone = true;
            try
            {
                var dest = _voyageDest;
                _fareEscrow = 0;

                // Step off the origin's menus/encounter, drop the party at the
                // destination's gate, and let the engine walk it in normally.
                try
                {
                    if (TaleWorlds.CampaignSystem.Encounters.PlayerEncounter.Current != null)
                        TaleWorlds.CampaignSystem.Encounters.PlayerEncounter.Finish(true);
                }
                catch { }

                if (dest != null)
                {
                    var main = MobileParty.MainParty;
                    Vec2 gate;
                    try { gate = dest.GatePosition; }
                    catch { gate = dest.GetPosition2D; }
                    try { main.Position2D = gate; } catch { }
                    try { main.Ai.SetMoveGoToSettlement(dest); } catch { }

                    InformationManager.DisplayMessage(new InformationMessage(
                        $"The ship noses into {dest.Name}. Land legs come back slowly.",
                        new Color(0.65f, 0.75f, 0.9f)));
                }
                ResetVoyageState();
            }
            catch { }
        }

        // ── Storm ──────────────────────────────────────────────────────────────
        private static void FireStorm()
        {
            try
            {
                float remaining = Math.Max(0f, _voyageHoursTotal - _voyageHoursElapsed);
                int extra = SeaMath.StormExtraHours(remaining, _rng.NextDouble());
                _voyageHoursTotal += extra;
                int hurt = ApplySeaCasualties(0.05f);

                string body =
                    "The sky goes the color of wet slate and the sea stands up. The crew lash down what " +
                    "they can and pray to whatever listens out here. " +
                    $"The storm costs you {extra} hour(s) of hard-won water" +
                    (hurt > 0 ? $" and leaves {hurt} of your soldiers battered below decks." : ".");
                InformationManager.ShowInquiry(new InquiryData(
                    "⛈  Storm", body, true, false, "Ride it out.", "", null, null), true);
            }
            catch { }
        }

        // ── Corsairs ───────────────────────────────────────────────────────────
        private static void FireCorsairs()
        {
            try
            {
                float dist = _voyageOrigin != null && _voyageDest != null
                    ? PortDistance(_voyageOrigin, _voyageDest) : 300f;
                float playerStr  = PlayerFleetStrength(searTheTide: false);
                float corsairStr = SeaMath.CorsairStrength(Math.Max(1f, playerStr), dist, _rng.NextDouble());
                int fare    = SeaMath.Fare(dist, PartySize());
                int tribute = SeaMath.TributeDemand(fare, corsairStr);

                string odds = corsairStr > playerStr * 1.1f ? "They have the numbers."
                            : corsairStr < playerStr * 0.8f ? "They may have picked the wrong hull."
                            : "It could go either way.";

                var options = new List<InquiryElement>
                {
                    new InquiryElement("fight", "Repel boarders — steel on the gunwales", null, true,
                        $"An honest boarding fight. {odds}"),
                };
                if (MageKnowledge.IsMage)
                    options.Add(new InquiryElement("sear", $"Sear the Tide ({SeaMath.SearTheTideAgingDays} days aging)", null, true,
                        "Open the Inner Fire over open water. Burning rigging, screaming corsairs, and much better odds."));
                options.Add(new InquiryElement("tribute", $"Pay tribute ({tribute} denars)", null, Hero.MainHero.Gold >= tribute,
                    Hero.MainHero.Gold >= tribute
                        ? "Coin buys passage. It always has."
                        : "You cannot afford what they're asking."));
                options.Add(new InquiryElement("flee", "Crowd sail and run", null, true,
                    "Half the time the wind loves you. The other half, they board you winded and angry."));

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "☠  Corsairs",
                    "Sails on the horizon — low, fast hulls that don't fly any kingdom's colors. They've seen you, " +
                    "and they're turning. The captain looks at you, because you're the one with soldiers.",
                    options, false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        string pick = chosen?[0]?.Identifier as string ?? "fight";
                        switch (pick)
                        {
                            case "sear":
                                try { AgingSystem.AgeHero(Hero.MainHero, SeaMath.SearTheTideAgingDays); } catch { }
                                ResolveBoardingFight(PlayerFleetStrength(searTheTide: true), corsairStr);
                                break;
                            case "tribute":
                                try { GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, tribute, true); } catch { }
                                MBInformationManager.AddQuickInformation(new TextObject(
                                    $"The corsairs take {tribute} denars and sheer away, already hunting the next sail."));
                                break;
                            case "flee":
                                if (_rng.NextDouble() < SeaMath.FleeEscapeChance)
                                {
                                    _voyageHoursTotal += 4f;
                                    MBInformationManager.AddQuickInformation(new TextObject(
                                        "The wind holds. The low hulls fall away astern — four hours lost beating off course."));
                                }
                                else
                                {
                                    MBInformationManager.AddQuickInformation(new TextObject(
                                        "They cut the angle and close. The fight comes anyway, on their terms."));
                                    ResolveBoardingFight(playerStr * SeaMath.FleeStrengthPenalty, corsairStr);
                                }
                                break;
                            default:
                                ResolveBoardingFight(playerStr, corsairStr);
                                break;
                        }
                    },
                    null, "", false), true, true);
            }
            catch { }
        }

        private static void ResolveBoardingFight(float playerStr, float corsairStr)
        {
            try
            {
                var outcome = SeaMath.ResolveSeaBattle(playerStr, corsairStr, _rng.NextDouble());
                int hurt = ApplySeaCasualties(outcome.CasualtyFraction);

                if (outcome.Victory)
                {
                    if (outcome.LootGold > 0)
                        try { GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, outcome.LootGold, true); } catch { }
                    try { GainRenownAction.Apply(Hero.MainHero, 3f); } catch { }
                    InformationManager.ShowInquiry(new InquiryData(
                        "☠  Corsairs — Repelled",
                        "It is ugly, close work between the rails, and then it is over. The corsairs that can still " +
                        $"swim, swim. You strip {outcome.LootGold} denars from their hulks" +
                        (hurt > 0 ? $", though {hurt} of your soldiers paid for the privilege." : "."),
                        true, false, "Sail on.", "", null, null), true);
                }
                else
                {
                    int stolen = Math.Min(Hero.MainHero.Gold, Math.Max(100, Hero.MainHero.Gold * 15 / 100));
                    if (stolen > 0)
                        try { GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, stolen, true); } catch { }
                    InformationManager.ShowInquiry(new InquiryData(
                        "☠  Corsairs — Overrun",
                        "They take the deck and hold it long enough to take everything else. " +
                        $"{hurt} of your soldiers are cut down or pulled into the water, and {stolen} denars leave " +
                        "with the corsairs. They let the ship limp on — a stripped hull is tomorrow's customer.",
                        true, false, "Endure it.", "", null, null), true);
                }
            }
            catch { }
        }

        // =====================================================================
        // SEA TRADE
        // =====================================================================
        private static void StartVenture(Settlement dest)
        {
            try
            {
                var here = Settlement.CurrentSettlement;
                if (!IsPort(here) || !IsPort(dest) || here == dest) return;
                if (_ventures.Count >= SeaMath.MaxActiveVentures) return;

                float dist = PortDistance(here, dest);
                var options = new List<InquiryElement>();
                foreach (int tier in SeaMath.VentureTiers)
                {
                    bool can = Hero.MainHero.Gold >= tier;
                    var safe = SeaMath.ResolveVenture(tier, dist, false, 1.0, 0.5);
                    options.Add(new InquiryElement(tier, $"{tier} denars", null, can,
                        can ? $"A typical run returns around {safe.Payout} denars in {SeaMath.VentureDays(dist)} days — if the sea allows."
                            : "Your purse cannot cover this stake."));
                }

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    $"Venture to {dest.Name}",
                    "Your factor needs a stake to fill the hold. The bigger the cargo, the bigger the return — and the bigger the prize floating out there with your name on it.",
                    options, true, 1, 1, "Invest", "Never mind",
                    chosen =>
                    {
                        if (!(chosen?[0]?.Identifier is int invested)) return;
                        if (Hero.MainHero.Gold < invested) return;

                        if (MageKnowledge.IsMage)
                        {
                            InformationManager.ShowInquiry(new InquiryData(
                                "Bless the Cargo?",
                                $"For {SeaMath.BlessVentureAgingDays} day of aging you can breathe a ward into the hold — " +
                                "corsairs and weather find the ship half as often, and the goods arrive warm and wanted.",
                                true, true, "Bless it", "Let it sail bare",
                                () => LaunchVenture(dest, dist, invested, blessed: true),
                                () => LaunchVenture(dest, dist, invested, blessed: false)), true);
                        }
                        else
                        {
                            LaunchVenture(dest, dist, invested, blessed: false);
                        }
                    },
                    null, "", false), false, true);
            }
            catch { }
        }

        private static void LaunchVenture(Settlement dest, float dist, int invested, bool blessed)
        {
            try
            {
                if (Hero.MainHero.Gold < invested) return;
                GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, invested, true);
                if (blessed)
                    try { AgingSystem.AgeHero(Hero.MainHero, SeaMath.BlessVentureAgingDays); } catch { }

                _ventures.Add(new Venture
                {
                    DestName = dest.Name?.ToString() ?? "a far port",
                    DaysLeft = SeaMath.VentureDays(dist),
                    Invested = invested,
                    Blessed  = blessed,
                    Distance = dist,
                });
                MBInformationManager.AddQuickInformation(new TextObject(
                    $"The cog warps out on the evening tide, {invested} denars of cargo in her hold" +
                    (blessed ? ", a faint warmth clinging to her timbers." : ".")));
                try { GameMenu.SwitchToMenu("sea_harbor"); } catch { }
            }
            catch { }
        }

        private static void TickVentures()
        {
            if (_ventures.Count == 0) return;
            for (int i = _ventures.Count - 1; i >= 0; i--)
            {
                var v = _ventures[i];
                v.DaysLeft--;
                if (v.DaysLeft > 0) continue;
                _ventures.RemoveAt(i);

                var outcome = SeaMath.ResolveVenture(v.Invested, v.Distance, v.Blessed,
                                                     _rng.NextDouble(), _rng.NextDouble());
                if (outcome.Payout > 0)
                    try { GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, outcome.Payout, true); } catch { }

                if (outcome.Lost)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"Word from the coast: your venture to {v.DestName} is lost — corsairs, or the sea itself. " +
                        $"The salvagers return {outcome.Payout} denars of your {v.Invested}.",
                        new Color(0.85f, 0.45f, 0.35f)));
                }
                else
                {
                    int profit = outcome.Payout - v.Invested;
                    try { Hero.MainHero.AddSkillXp(DefaultSkills.Trade, Math.Max(10, profit / 10)); } catch { }
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"Your factor returns from {v.DestName}: {outcome.Payout} denars back on {v.Invested} invested" +
                        $" ({(profit >= 0 ? "+" : "")}{profit}).",
                        new Color(0.55f, 0.8f, 0.45f)));
                }
            }
        }
    }
}
