// =============================================================================
// ASH AND EMBER — GreatAwakening/GreatAwakeningCampaignBehavior.cs
//
// THE GREAT AWAKENING — Duneborn found something ancient and dark in the deep
// desert, and mean to drag it into Calradia on ten thousand sacrificed lives.
//
// One global, save-persistent counter drives two mirror-image quests:
//   • Duneborn: feed the Dark Altar until the sacrifice is complete.
//   • Everyone else: destroy the Duneborn kingdom before it is.
// Which quest is "yours" is never stored — it is read live off the player's
// current kingdom every time it matters, so leaving (or joining) Duneborn
// swaps which quest is active without any extra bookkeeping
// (GreatAwakeningCampaignBehavior.IsPlayerOnDunebornPath).
//
// Partials:
//   .Trigger.cs         — the day-50+ discovery roll
//   .Dialogue.cs         — "ask the Duneborn leader about the Great Awakening"
//   .Altar.cs            — the "Prepare for a Great Summoning" settlement menu
//   .NpcContribution.cs  — Duneborn lords occasionally feeding the altar
//   .Opposition.cs       — win condition for every other path
//   .Resolution.cs       — what happens once the sacrifice completes
// =============================================================================

using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace AshAndEmber
{
    public partial class GreatAwakeningCampaignBehavior : CampaignBehaviorBase
    {
        internal const string DunebornKingdomId = "aserai";

        // ── Phase state machine ──────────────────────────────────────────────────
        internal const int PhaseIdle                = 0; // not yet discovered
        internal const int PhaseDiscovered           = 1; // rumor fired, leader will now speak of it
        internal const int PhaseActive                = 2; // player has asked the leader; quest running
        internal const int PhaseResolvedControlled    = 3; // ending A — The Great Other serves Duneborn
        internal const int PhaseResolvedUncontrolled  = 4; // ending B — The Great Other roams free
        internal const int PhaseOppositionWon         = 5; // Duneborn kingdom destroyed before completion

        internal static int _phase = PhaseIdle;

        // Global, save-persistent — never reset once a campaign has begun feeding
        // it, and never rolled back even if the altar city changes hands.
        internal static int _prisonersSacrificed = 0;

        // The Great Summoning's own altar — chosen once, the southernmost of the
        // four fixed Dark Altar cities (AshenAltarsCampaignBehavior.FixedAltarCities).
        internal static string _altarSettlementId = null;

        internal static readonly Random _rng = new Random();

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.MapEventStarted.AddNonSerializedListener(this, OnMapEventStarted);
        }

        public override void SyncData(IDataStore store)
        {
            try { store.SyncData("GRAWK_Phase",       ref _phase); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { store.SyncData("GRAWK_Prisoners",   ref _prisonersSacrificed); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { store.SyncData("GRAWK_AltarId",     ref _altarSettlementId); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            SyncResolutionData(store);
            GreatOtherParty.SyncData(store);
        }

        public static void ResetForNewGame()
        {
            _phase = PhaseIdle;
            _prisonersSacrificed = 0;
            _altarSettlementId = null;
            ResetResolutionState();
            GreatOtherParty.ResetForNewGame();
        }

        private void OnDailyTick()
        {
            try { GreatOtherParty.DailyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private void OnMapEventStarted(MapEvent mapEvent, TaleWorlds.CampaignSystem.Party.PartyBase attackerParty, TaleWorlds.CampaignSystem.Party.PartyBase defenderParty)
        {
            try { GreatOtherParty.OnMapEventStarted(mapEvent); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void OnSessionLaunched(CampaignGameStarter starter)
        {
            EnsureAltarChosen();
            RegisterDialogue(starter);
            RegisterAltarMenus(starter);
        }

        private void OnWeeklyTick()
        {
            try { EnsureAltarChosen(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { TriggerWeeklyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { NpcContributionWeeklyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { OppositionWeeklyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { ResolutionWeeklyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { GreatOtherParty.WeeklyTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Shared queries ───────────────────────────────────────────────────────
        internal static Kingdom DunebornKingdom()
        {
            try { return Kingdom.All.FirstOrDefault(k => k.StringId == DunebornKingdomId && !k.IsEliminated); }
            catch { return null; }
        }

        // Kingdom.Leader can go briefly null around a succession — fall back to
        // the ruling clan's own leader, matching AshenCitySystem.Appearance.cs.
        internal static Hero DunebornLeader()
        {
            try { var k = DunebornKingdom(); return k?.Leader ?? k?.RulingClan?.Leader; }
            catch { return null; }
        }

        internal static bool IsPlayerOnDunebornPath()
        {
            try { return Hero.MainHero?.MapFaction is Kingdom k && k.StringId == DunebornKingdomId; }
            catch { return false; }
        }

        internal static Settlement AltarSettlement()
        {
            if (string.IsNullOrEmpty(_altarSettlementId)) return null;
            try { return Settlement.All.FirstOrDefault(s => s.StringId == _altarSettlementId); }
            catch { return null; }
        }

        internal static bool AltarIsDunebornOwned()
        {
            var s = AltarSettlement();
            try { return s != null && s.OwnerClan?.Kingdom?.StringId == DunebornKingdomId; }
            catch { return false; }
        }

        private static void EnsureAltarChosen()
        {
            if (!string.IsNullOrEmpty(_altarSettlementId)) return;
            try
            {
                // Southernmost (lowest map-Y) of the fixed Dark Altar cities — "far
                // down south," resolved at runtime rather than guessed by name so it
                // stays correct across map variations / mod conflicts.
                Settlement southmost = Settlement.All
                    .Where(s => s.IsTown && AshenAltarsCampaignBehavior.FixedAltarCities
                        .Any(city => (s.Name?.ToString() ?? "").IndexOf(city, StringComparison.OrdinalIgnoreCase) >= 0))
                    .OrderBy(s => s.GetPosition2D.y)
                    .FirstOrDefault();
                if (southmost != null) _altarSettlementId = southmost.StringId;
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static int CurrentCampaignDay()
        {
            try { return (int)CampaignTime.Now.ToDays; } catch { return 0; }
        }
    }
}
