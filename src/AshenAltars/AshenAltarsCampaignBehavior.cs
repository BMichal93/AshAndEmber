// =============================================================================
// ASH AND EMBER — AshenAltars/AshenAltarsCampaignBehavior.cs
//
// Dark Altars are sites of blood sacrifice where the willing (and the
// merciless) purchase permanent Dark Gifts. Each gift exacts a geometrically
// growing toll of prisoners and captured lords.
//
// Fixed altar cities: Tyal / Sibir / Baltakhand / Amprela (Ashen lands),
// plus one random Aserai town and two random Empire towns per campaign.
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
        private const string AshenKingdomId  = "ashen_kingdom";
        private const string AseraiKingdomId = "aserai";
        private static readonly HashSet<string> EmpireKingdomIds =
            new HashSet<string> { "empire", "empire_n", "empire_s", "empire_w" };

        private static readonly string[] FixedAltarCities =
            { "Tyal", "Sibir", "Baltakhand", "Amprela" };

        // Dynamic altar settlement StringIds — 1 Aserai + 2 Empire
        private static readonly List<string> _dynamicAltarIds = new List<string>();

        private static bool _altarsAnnounced = false;
        private static readonly Random _rng = new Random();

        // Cross-system: last day any Dark Altar was used (read by SanctuaryCampaignBehavior)
        internal static int _lastAltarUseDay = -999;

        // ── CampaignBehaviorBase ───────────────────────────────────────────────
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore store)
        {
            try { store.SyncData("ALTAR_Announced",   ref _altarsAnnounced); } catch { }
            try { store.SyncData("ALTAR_LastUseDay",  ref _lastAltarUseDay); } catch { }

            // Backward-compat: silently read (and discard) old Cold keys so saves don't error.
            try { int dummy = 0; store.SyncData("ALTAR_UseCount",     ref dummy); } catch { }
            try { int dummy = 0; store.SyncData("ALTAR_LastInvokeDay", ref dummy); } catch { }

            try
            {
                var dynIds = _dynamicAltarIds.ToList();
                store.SyncData("ALTAR_DynamicIds", ref dynIds);
                if (dynIds != null)
                {
                    _dynamicAltarIds.Clear();
                    foreach (var id in dynIds) _dynamicAltarIds.Add(id);
                }
            }
            catch { }

            DarkGiftSystem.SyncData(store);
        }

        private static void OnSessionLaunched(CampaignGameStarter starter)
        {
            EnsureDynamicAltars();
            AnnounceAltars();
            RegisterAltarMenus(starter);
        }

        public static void EstablishForNewCampaign()
        {
            // The altars themselves are set up and announced once by OnSessionLaunched
            // (which fires for both new games and loads). Re-running it here would
            // re-roll the cities and announce a SECOND time, so only the player's gift
            // slate is cleared for a fresh campaign.
            DarkGiftSystem.ResetForNewGame();
        }

        public static void ResetForNewGame()
        {
            _altarsAnnounced = false;
            _lastAltarUseDay = -999;
            _dynamicAltarIds.Clear();
            DarkGiftSystem.ResetForNewGame();
        }

        private static void EnsureDynamicAltars()
        {
            if (_dynamicAltarIds.Count > 0) return;
            try
            {
                // 1 Aserai town
                var aserai = Settlement.All
                    .Where(s => s.IsTown && s.OwnerClan?.Kingdom?.StringId == AseraiKingdomId)
                    .OrderBy(_ => _rng.Next()).FirstOrDefault();
                if (aserai != null) _dynamicAltarIds.Add(aserai.StringId);

                // 2 Empire towns
                var empireTowns = Settlement.All
                    .Where(s => s.IsTown && EmpireKingdomIds.Contains(s.OwnerClan?.Kingdom?.StringId ?? ""))
                    .OrderBy(_ => _rng.Next()).Take(2).ToList();
                foreach (var t in empireTowns) _dynamicAltarIds.Add(t.StringId);
            }
            catch { }
        }

        private static void AnnounceAltars()
        {
            if (_altarsAnnounced) return;
            _altarsAnnounced = true;
            try
            {
                var names = FixedAltarCities.ToList();
                foreach (var id in _dynamicAltarIds)
                {
                    var s = Settlement.All.FirstOrDefault(x => x.StringId == id);
                    if (s != null) names.Add(s.Name?.ToString() ?? id);
                }
                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Dark Altars have been raised in {string.Join(", ", names)}. " +
                    "Only the Merciless and Devious may kneel before them."));
            }
            catch { }
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        private static int CurrentCampaignDay()
        {
            try { return (int)CampaignTime.Now.ToDays; } catch { return 0; }
        }

        internal static bool HasDarkAltar(Settlement s)
        {
            if (s == null || !s.IsTown) return false;
            try
            {
                string name = s.Name?.ToString() ?? "";
                // Fixed Ashen altars + dynamically rolled Aserai/Empire ones
                if (FixedAltarCities.Any(city =>
                        name.IndexOf(city, StringComparison.OrdinalIgnoreCase) >= 0)
                    || AshenQuestSystem.IsWastelandCity(s.StringId)
                    || _dynamicAltarIds.Contains(s.StringId))
                    return true;

                // Every town held by the Tribes of the East has a Dark Altar —
                // the God-King's blood-pacts run through each city he claims.
                return s.OwnerClan?.Kingdom?.StringId == "khuzait";
            }
            catch { return false; }
        }

        // Keep old name for any call sites that still reference HasAshenAltar
        internal static bool HasAshenAltar(Settlement s) => HasDarkAltar(s);
    }
}
