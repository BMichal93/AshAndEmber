// =============================================================================
// ASH AND EMBER — Sea/SeaCampaignBehavior.NpcLanes.cs
// NPC sea lanes, blockade tracking, and port traffic.
// Partial of SeaCampaignBehavior (shared static state lives in SeaCampaignBehavior.cs).
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
    public partial class SeaCampaignBehavior
    {
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
    }
}
