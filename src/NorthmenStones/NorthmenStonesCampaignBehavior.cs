// =============================================================================
// ASH AND EMBER — NorthmenStones/NorthmenStonesCampaignBehavior.cs
//
// THE BONEFIRE CIRCLE — the Northmen seers mean to raise standing stones at
// Varcheg and bind them with Fire, so nothing Ashen can cross there living.
// Building them takes a long grind of donated materials (some of them bound
// Kindled elementals, fought and bound at a Forest Clans sacred site first),
// an actual alliance between the Northmen and the Forest Clans, and Varcheg
// held by the Northmen at the moment the ritual closes — all while the Ashen
// throw themselves at the town harder as the working nears completion.
//
// Partials:
//   .Trigger.cs          — the day-30+ rumor roll / direct Northmen summons
//   .Dialogue.cs         — "ask the Northmen leader about the standing stones"
//   .Menu.cs             — the Varcheg "Donate materials" submenu + the silent
//                          standing-stone flavor entry once it's raised
//   .NpcContribution.cs  — Northmen lords occasionally donating materials
//   .Invasion.cs         — the 50/75/90% Ashen invasion waves on Varcheg
//   .Ending.cs           — the final choice and its consequences, plus the
//                          recurring Greater Emberfall once the stones stand
// =============================================================================

using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace AshAndEmber
{
    public partial class NorthmenStonesCampaignBehavior : CampaignBehaviorBase
    {
        internal const string NorthmenKingdomId    = "sturgia";
        internal const string ForestClansKingdomId = "battania";
        internal const string VarchegTownName       = "Varcheg";

        // ── Phase state machine ──────────────────────────────────────────────────
        internal const int PhaseIdle    = 0; // not yet triggered
        internal const int PhaseOffered = 1; // rumor/summons fired, leader will now speak of it
        internal const int PhaseActive  = 2; // player has accepted; the grind is on
        internal const int PhaseEnded   = 3; // any ending has resolved

        internal static int _phase = PhaseIdle;

        // 0 = none yet, 1 = Disagree, 2 = Agree (self), 3 = Sacrifice the child
        internal const int EndingNone     = 0;
        internal const int EndingDisagree = 1;
        internal const int EndingSelf     = 2;
        internal const int EndingChild    = 3;
        internal static int _endingKind = EndingNone;

        // ── Material counters — persistent, decay only while Varcheg isn't ours ──
        internal static int _iron;
        internal static int _hardwood;
        internal static int _tools;
        internal static int _silver;
        internal static int _denars;

        internal static int _kindledStone;
        internal static int _kindledFrost;
        internal static int _kindledSand;
        internal static int _kindledFlame;
        internal static int _kindledTide;
        internal static int _kindledGale;

        internal static int KindledTotal()
            => _kindledStone + _kindledFrost + _kindledSand + _kindledFlame + _kindledTide + _kindledGale;

        // ── One-shot flags ────────────────────────────────────────────────────────
        internal static bool _invasion50Fired;
        internal static bool _invasion75Fired;
        internal static bool _invasion90Fired;
        internal static bool _materialsCompleteNotified;
        internal static bool _resolutionHandled;
        internal static bool _stoneBuilt;
        internal static int  _lastEmberfallDay = -1;

        internal static readonly Random _rng = new Random();

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore store)
        {
            try { store.SyncData("NSTONES_Phase",      ref _phase); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { store.SyncData("NSTONES_Ending",     ref _endingKind); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try { store.SyncData("NSTONES_Iron",       ref _iron); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { store.SyncData("NSTONES_Hardwood",   ref _hardwood); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { store.SyncData("NSTONES_Tools",      ref _tools); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { store.SyncData("NSTONES_Silver",     ref _silver); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { store.SyncData("NSTONES_Denars",     ref _denars); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try { store.SyncData("NSTONES_KStone",     ref _kindledStone); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { store.SyncData("NSTONES_KFrost",     ref _kindledFrost); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { store.SyncData("NSTONES_KSand",      ref _kindledSand); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { store.SyncData("NSTONES_KFlame",     ref _kindledFlame); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { store.SyncData("NSTONES_KTide",      ref _kindledTide); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { store.SyncData("NSTONES_KGale",      ref _kindledGale); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            int inv50 = _invasion50Fired ? 1 : 0;
            store.SyncData("NSTONES_Inv50", ref inv50);
            _invasion50Fired = inv50 != 0;

            int inv75 = _invasion75Fired ? 1 : 0;
            store.SyncData("NSTONES_Inv75", ref inv75);
            _invasion75Fired = inv75 != 0;

            int inv90 = _invasion90Fired ? 1 : 0;
            store.SyncData("NSTONES_Inv90", ref inv90);
            _invasion90Fired = inv90 != 0;

            int notified = _materialsCompleteNotified ? 1 : 0;
            store.SyncData("NSTONES_MatNotify", ref notified);
            _materialsCompleteNotified = notified != 0;

            int resolved = _resolutionHandled ? 1 : 0;
            store.SyncData("NSTONES_Resolved", ref resolved);
            _resolutionHandled = resolved != 0;

            int built = _stoneBuilt ? 1 : 0;
            store.SyncData("NSTONES_StoneBuilt", ref built);
            _stoneBuilt = built != 0;

            try { store.SyncData("NSTONES_LastEmberfallDay", ref _lastEmberfallDay); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        public static void ResetForNewGame()
        {
            _phase = PhaseIdle;
            _endingKind = EndingNone;
            _iron = _hardwood = _tools = _silver = _denars = 0;
            _kindledStone = _kindledFrost = _kindledSand = _kindledFlame = _kindledTide = _kindledGale = 0;
            _invasion50Fired = _invasion75Fired = _invasion90Fired = false;
            _materialsCompleteNotified = false;
            _resolutionHandled = false;
            _stoneBuilt = false;
            _lastEmberfallDay = -1;
        }

        private static void OnSessionLaunched(CampaignGameStarter starter)
        {
            RegisterDialogue(starter);
            RegisterMenus(starter);
        }

        private void OnWeeklyTick()
        {
            try { TriggerWeeklyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { NpcContributionWeeklyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { ApplyDecayWeeklyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { InvasionWeeklyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private void OnDailyTick()
        {
            try { EndingDailyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { EmberfallDailyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static int CurrentCampaignDay()
        {
            try { return (int)CampaignTime.Now.ToDays; } catch { return 0; }
        }

        // ── Shared queries ───────────────────────────────────────────────────────
        internal static Kingdom NorthmenKingdom()
        {
            try { return Kingdom.All.FirstOrDefault(k => k.StringId == NorthmenKingdomId && !k.IsEliminated); }
            catch { return null; }
        }

        internal static Kingdom ForestClansKingdom()
        {
            try { return Kingdom.All.FirstOrDefault(k => k.StringId == ForestClansKingdomId && !k.IsEliminated); }
            catch { return null; }
        }

        // Kingdom.Leader can go briefly null around a succession — fall back to
        // the ruling clan's own leader, matching GreatAwakeningCampaignBehavior.
        internal static Hero NorthmenLeader()
        {
            try { var k = NorthmenKingdom(); return k?.Leader ?? k?.RulingClan?.Leader; }
            catch { return null; }
        }

        internal static bool IsPlayerNorthmen()
        {
            try { return Hero.MainHero?.MapFaction is Kingdom k && k.StringId == NorthmenKingdomId; }
            catch { return false; }
        }

        // Varcheg is a fixed vanilla town — resolved by name each time it's
        // queried (mirrors SeaCampaignBehavior's port matching) rather than
        // cached to a persisted id, since there is nothing dynamic to remember.
        internal static Settlement VarchegSettlement()
        {
            try
            {
                return Settlement.All.FirstOrDefault(s => s != null && s.IsTown
                    && string.Equals(s.Name?.ToString()?.Trim(), VarchegTownName, StringComparison.OrdinalIgnoreCase));
            }
            catch { return null; }
        }

        internal static bool IsVarchegNorthmenOwned()
        {
            try { return VarchegSettlement()?.MapFaction?.StringId == NorthmenKingdomId; }
            catch { return false; }
        }

        // Vanilla kingdom alliances (Kingdom.IsAllyWith) — distinct from simply
        // not being at war. Both kingdoms must still exist.
        internal static bool IsNorthmenForestClansAllied()
        {
            try
            {
                var northmen = NorthmenKingdom();
                var forest = ForestClansKingdom();
                return northmen != null && forest != null && northmen.IsAllyWith(forest);
            }
            catch { return false; }
        }
    }
}
