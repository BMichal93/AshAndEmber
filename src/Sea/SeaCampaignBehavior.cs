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
            "Sargot", "Ostican",                           // Vlandia — the western sea
            "Ortysia",                                     // Western Empire
            "Epicrotea", "Saneopa", "Myzea",               // Northern Empire — the Perassic
            "Quyaz", "Iyakis",                             // Aserai — the southern waters
        };

        private static readonly List<Settlement> _ports = new List<Settlement>();
        private static readonly Random _rng = new Random();

        // ── Voyage state (transient — a reload mid-voyage refunds the fare) ───
        private static Settlement _voyageOrigin;
        private static Settlement _voyageDest;
        private static float _voyageHoursTotal;
        private static float _voyageHoursElapsed;
        private static float _pirateAtHour   = -1f;   // -1 → no corsairs this crossing
        private static float _stormAtHour   = -1f;
        private static float _fogAtHour     = -1f;
        private static float _floatsamAtHour = -1f;
        private static bool  _voyageEmberwind;
        private static bool  _voyageDone;
        private static int   _fareEscrow;           // persisted; refunded if a reload strands the voyage

        // Emberwind bought in the harbor, consumed by the next voyage this session.
        private static bool _emberwindCalled;

        // ── Blockade state (in-memory, recomputed on daily tick) ──────────────
        // Maps port StringId → (blockading faction, combined fleet strength).
        // A port is blockaded by whoever has the strongest lord party within
        // SeaMath.BlockadeReachUnits. Only hostile-to-crosser blockades matter.
        private struct BlockadeEntry { public IFaction Faction; public float Strength; }
        private static readonly Dictionary<string, BlockadeEntry> _blockades
            = new Dictionary<string, BlockadeEntry>();

        // Blockade encounter state for the current player voyage (transient).
        private static float    _blockadeAtHour   = -1f;
        private static IFaction _blockadeFaction  = null;
        private static float    _blockadeStrength = 0f;

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

        // NPC sea-leg cooldowns by party id — in-memory only; a reload simply
        // lets everyone sail again, which costs nothing.
        private static readonly Dictionary<string, int> _npcSailCooldown = new Dictionary<string, int>();

        // ── CampaignBehaviorBase ───────────────────────────────────────────────
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.OnSettlementLeftEvent.AddNonSerializedListener(this, OnSettlementLeft);
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

        // Clears per-campaign static state so a new game started in the same
        // Bannerlord session does not inherit the previous game's trade ventures,
        // escrowed fare, Emberwind charge, or NPC sail cooldowns. (On a new game the
        // SyncData rebuild below is fed by these same statics, so without an explicit
        // reset the previous campaign's ventures would carry straight over.)
        public static void ResetForNewGame()
        {
            _ventures.Clear();
            _npcSailCooldown.Clear();
            _blockades.Clear();
            _fareEscrow      = 0;
            _emberwindCalled = false;
            ResetVoyageState();
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
            try { TickNpcSea(); } catch { }
            try { TickBlockades(); } catch { }
        }

        private static void TickNpcSea()
        {
            // Sea-leg cooldowns tick down.
            foreach (var key in _npcSailCooldown.Keys.ToList())
            {
                if (--_npcSailCooldown[key] <= 0)
                    _npcSailCooldown.Remove(key);
            }

            // Harbor towns skim a living off the traffic.
            foreach (var p in _ports)
            {
                try { if (p?.Town != null) p.Town.Prosperity += SeaMath.PortProsperityPerDay; } catch { }
            }
        }

        // ── Blockade tracking ─────────────────────────────────────────────────
        // Rebuilds which ports are contested each day. A port is blockaded by
        // the faction with the strongest lord party within BlockadeReachUnits.
        private static void TickBlockades()
        {
            _blockades.Clear();
            if (Campaign.Current == null || _ports.Count == 0) return;
            foreach (var port in _ports)
            {
                if (port == null) continue;
                IFaction best = null; float bestStr = 0f;
                try
                {
                    foreach (var party in MobileParty.All)
                    {
                        try
                        {
                            if (party == null || party.IsMainParty || !party.IsLordParty
                                || party.MapFaction == null || party.LeaderHero == null) continue;
                            float d = (party.GetPosition2D - port.GetPosition2D).Length;
                            if (d > SeaMath.BlockadeReachUnits) continue;
                            float s = FleetStrengthOf(party, false);
                            if (s > bestStr) { bestStr = s; best = party.MapFaction; }
                        }
                        catch { }
                    }
                }
                catch { }
                if (best != null)
                {
                    string key = port.StringId ?? port.Name?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(key))
                        _blockades[key] = new BlockadeEntry { Faction = best, Strength = bestStr };
                }
            }
        }

        // Returns the blockade entry for a port if it is blockaded by a faction
        // hostile to the given crosser faction, otherwise returns null.
        private static BlockadeEntry? HostileBlockade(Settlement port, IFaction crosser)
        {
            if (port == null || crosser == null) return null;
            string key = port.StringId ?? port.Name?.ToString() ?? "";
            if (!string.IsNullOrEmpty(key) && _blockades.TryGetValue(key, out var b)
                && b.Faction != null && b.Faction.IsAtWarWith(crosser))
                return b;
            return null;
        }

        // ── NPC sea lanes ──────────────────────────────────────────────────────
        // Lords and caravans leaving a harbor town may take ship: the party is
        // moved to the destination port after weathering the same corsair odds
        // the player faces (resolved silently against their roster).
        private void OnSettlementLeft(MobileParty party, Settlement settlement)
        {
            try
            {
                if (party == null || settlement == null || party.IsMainParty) return;
                bool caravan = party.IsCaravan;
                bool lord    = !caravan && party.IsLordParty && party.LeaderHero != null;
                if (!caravan && !lord) return;
                if (_ports.Count < 2 || !IsPort(settlement)) return;
                if (!party.IsActive || party.MapEvent != null || party.Army != null) return;

                string id = party.StringId ?? "";
                if (_npcSailCooldown.ContainsKey(id)) return;

                Settlement dest = null; float crossing = 0f;
                if (lord)
                {
                    // Primary path: lord's AI already wants a settlement reachable from another port.
                    Settlement target = null;
                    try { target = party.TargetSettlement; } catch { }
                    if (target != null && target != settlement)
                    {
                        var portNear = PortNear(target, exclude: settlement);
                        if (portNear != null)
                        {
                            float d = PortDistance(settlement, portNear);
                            if (SeaMath.NpcCrossingViable(d, caravan: false)
                                && _rng.NextDouble() < SeaMath.NpcLordSailChance)
                            { dest = portNear; crossing = d; }
                        }
                    }

                    // Invasion path: lord at war may strike directly at an enemy coastal port.
                    if (dest == null && party.MapFaction != null)
                    {
                        var enemyPorts = _ports
                            .Where(p => p != settlement && p.MapFaction != null
                                        && party.MapFaction.IsAtWarWith(p.MapFaction))
                            .ToList();
                        if (enemyPorts.Count > 0)
                        {
                            var ep = enemyPorts[_rng.Next(enemyPorts.Count)];
                            float d = PortDistance(settlement, ep);
                            if (SeaMath.NpcCrossingViable(d, caravan: false)
                                && _rng.NextDouble() < SeaMath.NpcInvasionSailChance)
                            { dest = ep; crossing = d; }
                        }
                    }

                    if (dest == null) return;
                }
                else
                {
                    if (_rng.NextDouble() >= SeaMath.NpcCaravanSailChance) return;
                    var legs = _ports
                        .Where(p => p != settlement)
                        .Select(p => new { Port = p, Dist = PortDistance(settlement, p) })
                        .Where(t => SeaMath.NpcCrossingViable(t.Dist, caravan: true))
                        .ToList();
                    if (legs.Count == 0) return;
                    var pick = legs[_rng.Next(legs.Count)];
                    dest = pick.Port; crossing = pick.Dist;
                }

                _npcSailCooldown[id] = SeaMath.NpcSailCooldownDays;

                // Blockade interception: if the destination is held by a hostile
                // faction, there is a chance the crossing is turned back or bloodied.
                if (party.MapFaction != null)
                {
                    var blk = HostileBlockade(dest, party.MapFaction);
                    if (blk.HasValue)
                    {
                        float crosserStr = FleetStrengthOf(party, false);
                        if (_rng.NextDouble() < SeaMath.BlockadeInterceptChance(blk.Value.Strength, crosserStr))
                        {
                            var fight = SeaMath.ResolveSeaBattle(crosserStr, blk.Value.Strength, _rng.NextDouble());
                            ApplySeaCasualties(party, fight.CasualtyFraction);
                            if (!fight.Victory)
                            {
                                // Turned back — abort this sea leg entirely.
                                return;
                            }
                        }
                    }
                }

                // The same sea, the same corsairs — resolved off-screen.
                if (_rng.NextDouble() < SeaMath.PirateChance(crossing))
                {
                    float str = FleetStrengthOf(party, false);
                    var fight = SeaMath.ResolveSeaBattle(str,
                        SeaMath.CorsairStrength(Math.Max(1f, str), crossing, _rng.NextDouble()),
                        _rng.NextDouble());
                    ApplySeaCasualties(party, fight.CasualtyFraction);
                }

                try { party.Position = dest.GatePosition; } catch { return; }
                try { party.SetMoveGoToSettlement(dest, MobileParty.NavigationType.Default, false); } catch { }

                // Word travels when it's your kingdom's banner or your own coin.
                try
                {
                    if (lord && party.MapFaction != null && Hero.MainHero != null
                        && party.MapFaction == Hero.MainHero.MapFaction)
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"{party.LeaderHero.Name} takes ship from {settlement.Name} and lands at {dest.Name}.",
                            new Color(0.65f, 0.75f, 0.9f)));
                    else if (caravan && party.Owner == Hero.MainHero)
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"Your caravan takes the sea route from {settlement.Name} to {dest.Name}.",
                            new Color(0.65f, 0.75f, 0.9f)));
                }
                catch { }
            }
            catch { }
        }

        // Nearest port to a settlement, if any lies within reach of it.
        private static Settlement PortNear(Settlement target, Settlement exclude)
        {
            Settlement best = null; float bd = float.MaxValue;
            foreach (var p in _ports)
            {
                if (p == exclude) continue;
                float d = PortDistance(p, target);
                if (d < bd) { bd = d; best = p; }
            }
            return bd <= SeaMath.NpcPortReachUnits ? best : null;
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

        // A port whose holding faction is the Ashen — the cold coast, dreaded by sailors.
        private static bool IsAshenPort(Settlement s)
        {
            try { return s != null && AshenCitySystem.IsAshenFaction(s.MapFaction); }
            catch { return false; }
        }

        private static float PortDistance(Settlement a, Settlement b)
        {
            try { return (a.GetPosition2D - b.GetPosition2D).Length; }
            catch { return 0f; }
        }

        // ── Party readings for SeaMath ─────────────────────────────────────────
        private static float FleetStrengthOf(MobileParty party, bool searTheTide)
        {
            int troops = 0; float tierSum = 0f;
            try
            {
                foreach (var e in party.MemberRoster.GetTroopRoster())
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
            try { tactics = party.LeaderHero?.GetSkillValue(DefaultSkills.Tactics) ?? 0; } catch { }
            return SeaMath.FleetStrength(troops, avgTier, tactics, searTheTide);
        }

        // Strikes a fraction of the party's healthy regulars: ~60% wounded,
        // the rest lost to the water. Returns the number of men affected.
        private static int ApplySeaCasualties(MobileParty party, float fraction)
        {
            int affected = 0;
            try
            {
                var roster = party.MemberRoster;
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

            // ── Raid the coast ──────────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("sea_harbor", "sea_raid", "{SEA_RAID_TEXT}",
                    args =>
                    {
                        try
                        {
                            if (Hero.MainHero?.MapFaction == null) return false;
                            bool anyEnemyPort = false;
                            var here = Settlement.CurrentSettlement;
                            foreach (var p in _ports)
                            {
                                if (p == here || p.MapFaction == null) continue;
                                if (Hero.MainHero.MapFaction.IsAtWarWith(p.MapFaction))
                                { anyEnemyPort = true; break; }
                            }
                            if (!anyEnemyPort) return false;
                            MBTextManager.SetTextVariable("SEA_RAID_TEXT", "Raid an enemy coast");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                            return true;
                        }
                        catch { return false; }
                    },
                    args => ShowRaidDestinationPicker());
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
                    GameMenu.MenuOverlayType.None, 0f, GameMenu.MenuFlags.None, null);
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
                        int risk  = (int)(SeaMath.AshenAdjusted(SeaMath.PirateChance(dist), IsAshenPort(p)) * 100f);
                        hover = $"Fare {fare} denars. About {hours} hours at sea. Corsair risk roughly {risk}%."
                              + (IsAshenPort(p) ? " The grey waters off this cold coast take far more ships than they give back." : "");
                    }
                    string faction = "";
                    try { faction = p.MapFaction?.Name?.ToString() ?? ""; } catch { }
                    string ashenMark = IsAshenPort(p) ? "  ❄" : "";
                    options.Add(new InquiryElement(p, $"{p.Name} ({faction}){ashenMark}", null, true, hover));
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

        private static void ShowRaidDestinationPicker()
        {
            try
            {
                var here = Settlement.CurrentSettlement;
                if (!IsPort(here) || Hero.MainHero?.MapFaction == null) return;

                var options = new List<InquiryElement>();
                foreach (var p in _ports)
                {
                    if (p == here || p.MapFaction == null) continue;
                    if (!Hero.MainHero.MapFaction.IsAtWarWith(p.MapFaction)) continue;
                    float dist  = PortDistance(here, p);
                    int   fare  = SeaMath.Fare(dist, PartySize());
                    int   hours = (int)SeaMath.TravelHours(dist, _emberwindCalled);
                    int   risk  = (int)(SeaMath.PirateChance(dist) * 100f);
                    bool  blkd  = HostileBlockade(p, Hero.MainHero.MapFaction).HasValue;
                    string blkNote = blkd ? "  [BLOCKADED — expect a fight at the harbor mouth]" : "";
                    string hover = $"Fare {fare} denars. About {hours} hours. Corsair risk {risk}%.{blkNote} " +
                                   "You will land at the gate — take what you can hold.";
                    options.Add(new InquiryElement(p,
                        $"{p.Name} ({p.MapFaction?.Name}){(blkd ? "  ⚓" : "")}",
                        null, true, hover));
                }
                if (options.Count == 0) return;

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "Raid an Enemy Coast",
                    "Choose your target. You will land at their harbor gate — from there it is steel and nerve. " +
                    "Blockaded ports will contest your approach.",
                    options, true, 1, 1, "Sail for it", "Never mind",
                    chosen =>
                    {
                        var dest = chosen?[0]?.Identifier as Settlement;
                        if (dest != null) StartVoyage(dest);
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
                // random point in the middle stretch of the voyage. Crossings bound
                // for an Ashen-held port run a far greater risk of every hazard.
                bool ashenDest = IsAshenPort(dest);
                _pirateAtHour = _rng.NextDouble() < SeaMath.AshenAdjusted(SeaMath.PirateChance(dist), ashenDest)
                    ? _voyageHoursTotal * (0.25f + 0.5f * (float)_rng.NextDouble()) : -1f;
                _stormAtHour = !_voyageEmberwind && _rng.NextDouble() < SeaMath.AshenAdjusted(SeaMath.StormChancePerVoyage, ashenDest)
                    ? _voyageHoursTotal * (0.25f + 0.5f * (float)_rng.NextDouble()) : -1f;

                // Fog settles in the early-to-middle stretch; Emberwind burns it clear.
                _fogAtHour = !_voyageEmberwind && _rng.NextDouble() < SeaMath.AshenAdjusted(SeaMath.FogChancePerVoyage, ashenDest)
                    ? _voyageHoursTotal * (0.15f + 0.35f * (float)_rng.NextDouble()) : -1f;

                // A wrecked vessel drifts into view in the middle of the crossing.
                _floatsamAtHour = _rng.NextDouble() < SeaMath.AshenAdjusted(SeaMath.FloatsamChancePerVoyage, ashenDest)
                    ? _voyageHoursTotal * (0.30f + 0.40f * (float)_rng.NextDouble()) : -1f;

                // Check for a blockade at the destination. The encounter fires
                // near the end of the crossing — the party is committed by then.
                _blockadeAtHour = -1f; _blockadeFaction = null; _blockadeStrength = 0f;
                try
                {
                    if (Hero.MainHero?.MapFaction != null)
                    {
                        var blk = HostileBlockade(dest, Hero.MainHero.MapFaction);
                        if (blk.HasValue)
                        {
                            _blockadeAtHour   = _voyageHoursTotal * 0.80f;
                            _blockadeFaction  = blk.Value.Faction;
                            _blockadeStrength = blk.Value.Strength;
                        }
                    }
                }
                catch { }

                GameMenu.SwitchToMenu("sea_voyage");
            }
            catch { }
        }

        private static void ResetVoyageState()
        {
            _voyageOrigin       = null;
            _voyageDest         = null;
            _voyageHoursTotal   = 0f;
            _voyageHoursElapsed = 0f;
            _pirateAtHour       = -1f;
            _stormAtHour        = -1f;
            _fogAtHour          = -1f;
            _floatsamAtHour     = -1f;
            _blockadeAtHour     = -1f;
            _blockadeFaction    = null;
            _blockadeStrength   = 0f;
            _voyageEmberwind    = false;
            _voyageDone         = false;
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
                if (_fogAtHour >= 0f && _voyageHoursElapsed >= _fogAtHour)
                {
                    _fogAtHour = -1f;
                    FireFog();
                }
                if (_floatsamAtHour >= 0f && _voyageHoursElapsed >= _floatsamAtHour)
                {
                    _floatsamAtHour = -1f;
                    FireFlotsam();
                }
                if (_pirateAtHour >= 0f && _voyageHoursElapsed >= _pirateAtHour)
                {
                    _pirateAtHour = -1f;
                    FireCorsairs();
                }
                if (_blockadeAtHour >= 0f && _voyageHoursElapsed >= _blockadeAtHour)
                {
                    _blockadeAtHour = -1f;
                    FireBlockade();
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
                    try { main.Position = dest.GatePosition; } catch { }
                    try { main.SetMoveGoToSettlement(dest, MobileParty.NavigationType.Default, false); } catch { }

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
                int hurt = ApplySeaCasualties(MobileParty.MainParty, 0.05f);

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

        // ── Sea Fog ────────────────────────────────────────────────────────────
        private static void FireFog()
        {
            try
            {
                int extra = 3 + _rng.Next(3); // 3–5 hours lost if slowing down or unlucky push

                var options = new List<InquiryElement>
                {
                    new InquiryElement("slow", "Heave to and sound the lead", null, true,
                        $"Take it careful. The coast finds you eventually — adds {extra} hours."),
                };
                if (MageKnowledge.IsMage)
                    options.Add(new InquiryElement("burn",
                        $"Burn it away ({SeaMath.FogBurnAgingDays} days aging)", null, true,
                        "Push a thread of the Inner Fire through the air. The fog boils off clean — no delay, no danger."));
                options.Add(new InquiryElement("push",
                    "Push through — the captain swears he knows these waters", null, true,
                    "Even odds. Either you thread the channel cleanly, or something hard finds the hull."));

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "🌫  Sea Fog",
                    "The fog comes down like a curtain — twenty feet of visibility, no horizon, no stars. " +
                    "The helmsman is steering by feel and prayer.",
                    options, false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        string pick = chosen?[0]?.Identifier as string ?? "slow";
                        switch (pick)
                        {
                            case "burn":
                                try { AgingSystem.AgeHero(Hero.MainHero, SeaMath.FogBurnAgingDays); } catch { }
                                MBInformationManager.AddQuickInformation(new TextObject(
                                    "A breath of the Inner Fire and the fog tears apart like cloth. " +
                                    "The crew stares. The crossing continues."));
                                break;
                            case "push":
                                if (_rng.NextDouble() < 0.5)
                                {
                                    MBInformationManager.AddQuickInformation(new TextObject(
                                        "The captain threads it. The fog lifts after a tense hour and open water spreads ahead."));
                                }
                                else
                                {
                                    int hurt = ApplySeaCasualties(MobileParty.MainParty, 0.04f);
                                    _voyageHoursTotal += extra;
                                    InformationManager.ShowInquiry(new InquiryData(
                                        "🌫  Sea Fog — Hard Landing",
                                        "Something solid materializes out of the grey — a reef, or the shoulder of a headland. " +
                                        "The hull scrapes and holds, but " +
                                        (hurt > 0 ? $"{hurt} men are thrown about and hurt" : "the crew is badly shaken") +
                                        $". It takes {extra} hours to find open water again.",
                                        true, false, "Limp on.", "", null, null), true);
                                }
                                break;
                            default: // slow
                                _voyageHoursTotal += extra;
                                MBInformationManager.AddQuickInformation(new TextObject(
                                    $"The captain shortens sail and takes it slow. The fog burns off eventually — {extra} hours behind schedule."));
                                break;
                        }
                    },
                    null, "", false), true, true);
            }
            catch { }
        }

        // ── Flotsam ────────────────────────────────────────────────────────────
        private static void FireFlotsam()
        {
            try
            {
                var options = new List<InquiryElement>
                {
                    new InquiryElement("salvage",
                        "Heave to and put men on the wreck", null, true,
                        "Board the hulk and strip what the sea left behind. Adds 2 hours."),
                    new InquiryElement("pass",
                        "Leave it. Dead ships keep their own time.", null, true,
                        "Sail past and stay on schedule."),
                };
                if (MageKnowledge.IsMage)
                    options.Add(new InquiryElement("sense",
                        $"Read the wreck ({SeaMath.SenseWreckAgingDays} days aging)", null, true,
                        "Let the Inner Fire taste the hull — feel where coin and cargo lay heaviest. Finds more than blind hands would."));

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "⚓  Flotsam",
                    "A dark shape rolls in the swell ahead — a trading cog, or what's left of one. No sail, no crew visible. " +
                    "The flag she flew has been torn away. She could be days dead, or hours.",
                    options, false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        string pick = chosen?[0]?.Identifier as string ?? "pass";
                        switch (pick)
                        {
                            case "salvage":
                            {
                                _voyageHoursTotal += 2f;
                                int gold = SeaMath.FloatsamGold(_rng.NextDouble());
                                if (gold > 0)
                                    try { GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, gold, true); } catch { }
                                InformationManager.ShowInquiry(new InquiryData(
                                    "⚓  Flotsam — Salvaged",
                                    "Your men go over the side with ropes. The hold has been ransacked — corsairs, most likely — " +
                                    $"but the bilges still yielded {gold} denars of overlooked coin and goods. Two hours behind schedule.",
                                    true, false, "Back on course.", "", null, null), true);
                                break;
                            }
                            case "sense":
                            {
                                try { AgingSystem.AgeHero(Hero.MainHero, SeaMath.SenseWreckAgingDays); } catch { }
                                _voyageHoursTotal += 1f;
                                int gold = SeaMath.FloatsamGold(_rng.NextDouble()) * 2;
                                if (gold > 0)
                                    try { GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, gold, true); } catch { }
                                InformationManager.ShowInquiry(new InquiryData(
                                    "⚓  Flotsam — Sensed",
                                    "The Inner Fire finds the warm spots — where hands last gripped, where coin lay heaviest. " +
                                    $"Your men follow the warmth and pull {gold} denars from the wreck before it rolls and sinks.",
                                    true, false, "Back on course.", "", null, null), true);
                                break;
                            }
                            default: // pass
                                MBInformationManager.AddQuickInformation(new TextObject(
                                    "You sail past. The wreck slowly turns in the current, keeping its secrets."));
                                break;
                        }
                    },
                    null, "", false), true, true);
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
                float playerStr  = FleetStrengthOf(MobileParty.MainParty, searTheTide: false);
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
                                ResolveBoardingFight(FleetStrengthOf(MobileParty.MainParty, searTheTide: true), corsairStr);
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
                int hurt = ApplySeaCasualties(MobileParty.MainParty, outcome.CasualtyFraction);

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

        // ── Blockade encounter ─────────────────────────────────────────────────
        private static void FireBlockade()
        {
            try
            {
                float playerStr = FleetStrengthOf(MobileParty.MainParty, searTheTide: false);
                float blkStr    = _blockadeStrength;
                string fName    = _blockadeFaction?.Name?.ToString() ?? "hostile ships";

                string odds = blkStr > playerStr * 1.1f ? "Their fleet outguns yours."
                            : blkStr < playerStr * 0.8f ? "Your fleet should carry it."
                            : "It will be a bloody approach.";

                var options = new List<InquiryElement>
                {
                    new InquiryElement("fight", "Force the harbor — break through the line", null, true,
                        $"An assault on the blockade fleet. {odds}"),
                };
                if (MageKnowledge.IsMage)
                    options.Add(new InquiryElement("sear",
                        $"Sear the Tide ({SeaMath.SearTheTideAgingDays} days aging)", null, true,
                        "Open the Inner Fire over the blockade line. Burning rigging, broken formation, and much better odds."));
                options.Add(new InquiryElement("turn", "Turn back — the harbor is denied today", null, true,
                    "Abort the crossing and return to your port of origin. Your fare will be refunded."));

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "⚓  Blockade",
                    $"War galleys flying {fName} colors hold the harbor mouth. They have formed a line " +
                    "and they are not moving. You are still a good hour out, but there is no other way in.",
                    options, false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        string pick = chosen?[0]?.Identifier as string ?? "fight";
                        float effectiveStr = playerStr;
                        if (pick == "sear")
                        {
                            try { AgingSystem.AgeHero(Hero.MainHero, SeaMath.SearTheTideAgingDays); } catch { }
                            effectiveStr = FleetStrengthOf(MobileParty.MainParty, searTheTide: true);
                        }
                        if (pick == "turn")
                            TurnBackFromBlockade();
                        else
                            ResolveBlockadeBattle(effectiveStr, blkStr);
                    },
                    null, "", false), true, true);
            }
            catch { }
        }

        private static void ResolveBlockadeBattle(float playerStr, float blkStr)
        {
            try
            {
                var outcome = SeaMath.ResolveSeaBattle(playerStr, blkStr, _rng.NextDouble());
                int hurt = ApplySeaCasualties(MobileParty.MainParty, outcome.CasualtyFraction);

                if (outcome.Victory)
                {
                    if (outcome.LootGold > 0)
                        try { GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, outcome.LootGold, true); } catch { }
                    try { GainRenownAction.Apply(Hero.MainHero, 5f); } catch { }
                    InformationManager.ShowInquiry(new InquiryData(
                        "⚓  Blockade Broken",
                        "You force the line at bloody cost. Burning hulks drift aside and the harbor mouth opens." +
                        (hurt > 0 ? $" {hurt} of your soldiers paid for the approach." : ""),
                        true, false, "Press on.", "", null, null), true);
                    // Voyage continues — VoyageOnTick will call Arrive() normally.
                }
                else
                {
                    InformationManager.ShowInquiry(new InquiryData(
                        "⚓  Blockade — Repulsed",
                        "The line holds. Your ships are beaten back with heavy loss." +
                        (hurt > 0 ? $" {hurt} soldiers are gone." : "") +
                        " You limp back to your port of origin.",
                        true, false, "Withdraw.", "", () => TurnBackFromBlockade(), null), true);
                }
            }
            catch { }
        }

        private static void TurnBackFromBlockade()
        {
            try
            {
                if (_fareEscrow > 0)
                {
                    GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, _fareEscrow, true);
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The harbormaster refunds your fare — the harbor was denied.",
                        new Color(0.65f, 0.75f, 0.9f)));
                    _fareEscrow = 0;
                }
                var origin = _voyageOrigin;
                ResetVoyageState();
                if (origin != null)
                {
                    var main = MobileParty.MainParty;
                    try { main.Position = origin.GatePosition; } catch { }
                    try { main.SetMoveGoToSettlement(origin, MobileParty.NavigationType.Default, false); } catch { }
                }
                try { GameMenu.SwitchToMenu("town"); } catch { }
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
