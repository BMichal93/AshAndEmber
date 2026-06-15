// =============================================================================
// ASH AND EMBER — AshenAltars/AshenAltarsCampaignBehavior.cs
//
// Ashen Altars are the player's Cold charging stations. Each offers two rites:
//   • Embrace the Cold    — gain Cold (scales inversely with Honor/Mercy/Generosity).
//   • Invoke the Dark Tide — unleash Ashen influence on the world around you.
// Cold is spent on miracles (see the Miracle system). NPC miracle use lives
// entirely in MiracleBattleAI / MiracleCampaignBehavior — not here.
//
// Altars: the fixed cities Tyal / Sibir / Baltakhand / Amprela, the wasteland
// cities, and one random Aserai town picked per campaign.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class AshenAltarsCampaignBehavior : CampaignBehaviorBase
    {
        // ── Tuning ─────────────────────────────────────────────────────────────
        private const int TraitDriftThreshold   = 10; // uses between virtue nudges
        private const int CrossInterferenceDays  = 30; // sanctuary use saps Cold yield

        private const string AshenKingdomId  = "ashen_kingdom";
        private const string AseraiKingdomId = "aserai";
        private static readonly string[] AshenAltarCities = { "Tyal", "Sibir", "Baltakhand", "Amprela" };
        private static readonly List<string> _dynamicAltarIds = new List<string>();
        private static readonly Random _rng = new Random();

        private static bool _altarsAnnounced = false;

        // Cross-system state (read by SanctuaryCampaignBehavior)
        internal static int _lastAltarUseDay  = -999;
        private static int  _altarUseCount    = 0;

        // Per-rite cooldown tracking
        internal static int _lastInvokeDay = -999;

        // ── CampaignBehaviorBase ───────────────────────────────────────────────
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore store)
        {
            try { store.SyncData("ALTAR_Announced", ref _altarsAnnounced); } catch { }
            try { store.SyncData("ALTAR_LastUseDay", ref _lastAltarUseDay); } catch { }
            try { store.SyncData("ALTAR_UseCount", ref _altarUseCount); } catch { }
            try { store.SyncData("ALTAR_LastInvokeDay", ref _lastInvokeDay); } catch { }
            try
            {
                var dynIds = _dynamicAltarIds.ToList();
                store.SyncData("ALTAR_DynamicIds", ref dynIds);
                if (dynIds != null) { _dynamicAltarIds.Clear(); foreach (var id in dynIds) _dynamicAltarIds.Add(id); }
            } catch { }
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            EnsureDynamicAltars();
            AnnounceAltars();
            RegisterAltarMenus(starter);
        }

        // Authoritative new-game setup, fired once from OnCharacterCreationIsOver
        // (after the world is built, never on a load). Clears carry-over from a prior
        // game in the same session, then announces the altars fresh.
        public static void EstablishForNewCampaign()
        {
            ResetForNewGame();
            EnsureDynamicAltars();
            AnnounceAltars();
        }

        // Clears per-campaign static state so a new game started in the same
        // Bannerlord session does not inherit the previous game's "announced" flag
        // (which would suppress the altar establishment toast) or stale cooldowns.
        public static void ResetForNewGame()
        {
            _altarsAnnounced  = false;
            _lastAltarUseDay  = -999;
            _altarUseCount    = 0;
            _lastInvokeDay    = -999;
            _dynamicAltarIds.Clear();
        }

        // Selects one random Aserai town as a dynamic altar if not already set.
        // Called on session launch (existing saves) and on new campaign creation.
        private static void EnsureDynamicAltars()
        {
            if (_dynamicAltarIds.Count > 0) return;
            try
            {
                var pick = Settlement.All
                    .Where(s => s.IsTown && s.OwnerClan?.Kingdom?.StringId == AseraiKingdomId)
                    .OrderBy(_ => _rng.Next()).FirstOrDefault();
                if (pick != null) _dynamicAltarIds.Add(pick.StringId);
            }
            catch { }
        }

        private static void AnnounceAltars()
        {
            if (_altarsAnnounced) return;
            _altarsAnnounced = true;
            try
            {
                var names = AshenAltarCities.ToList();
                foreach (var id in _dynamicAltarIds)
                {
                    var s = Settlement.All.FirstOrDefault(x => x.StringId == id);
                    if (s != null) names.Add(s.Name?.ToString() ?? id);
                }
                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Ashen Altars stand in {string.Join(", ", names)}. " +
                    "Only the Merciless and Devious may kneel before them."));
            }
            catch { }
        }

        // ── Shared helpers (previously in the now-removed Rites partial) ────────
        private static int CurrentCampaignDay()
        {
            try { return (int)CampaignTime.Now.ToDays; } catch { return 0; }
        }

        internal static bool HasAshenAltar(Settlement s)
        {
            if (s == null || !s.IsTown) return false;
            try
            {
                string name = s.Name?.ToString() ?? "";
                return AshenAltarCities.Any(city =>
                    name.IndexOf(city, StringComparison.OrdinalIgnoreCase) >= 0)
                    || AshenQuestSystem.IsWastelandCity(s.StringId)
                    || _dynamicAltarIds.Contains(s.StringId);
            }
            catch { return false; }
        }
    }
}
