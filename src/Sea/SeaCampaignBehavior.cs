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
    public partial class SeaCampaignBehavior : CampaignBehaviorBase
    {
        // ── Harbor towns ───────────────────────────────────────────────────────
        // Curated harbour towns, matched by name at session launch. A name that
        // fails to resolve (renamed by another mod, localized client) just drops
        // that port from the network.
        private static readonly string[] PortTownNames =
        {
            "Revyl", "Varcheg", "Balgard", "Sibir",        // Sturgia — the cold coast
            "Galend", "Pravend", "Jaculan", "Ostican", "Charas", // Vlandia — the western sea
            "Ortysia", "Zeonica",                          // Western Empire
            "Poros", "Vostrum",                            // Southern Empire
            "Sanala", "Razih", "Quyaz",                    // Aserai — the southern waters
            "Argoron", "Jalmarys",                         // outlying
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
        private static float _floatsamAtHour   = -1f;
        private static float _survivorsAtHour  = -1f;
        private static float _serpentAtHour    = -1f;
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
    }
}
