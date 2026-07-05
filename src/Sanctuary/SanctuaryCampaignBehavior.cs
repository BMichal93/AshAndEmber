// =============================================================================
// ASH AND EMBER — Sanctuary/SanctuaryCampaignBehavior.cs
//
// Sanctuaries are the player's Grace charging stations. Each offers two rites:
//   • Pray for Grace      — gain Grace (scales with Honor/Mercy/Generosity).
//   • Take the Warding Seal — ward the world against Ashen events for a time.
// Grace is spent on miracles (see the Miracle system). NPC miracle use lives
// entirely in MiracleBattleAI / MiracleCampaignBehavior — not here.
//
// Sanctuaries: Holy Temple (Vlandia)-owned towns auto-qualify + 5 random Empire
// towns picked once per campaign. Temple members get shorter rite cooldowns.
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
    public partial class SanctuaryCampaignBehavior : CampaignBehaviorBase
    {
        // ── Tuning ─────────────────────────────────────────────────────────────
        private const int TraitDriftThreshold     = 10; // uses between virtue nudges
        private const int CrossInterferenceDays    = 30; // altar use saps Grace yield
        private const int PermanentSanctuaryCount  = 5;
        private const int EmpireSanctuaryCount     = 5;

        private const string TempleKingdomId = "vlandia";

        private static readonly List<string> _permanentSanctuaryIds = new List<string>();
        private static bool _sanctuariesAnnounced       = false;
        private static bool _needsAnnouncementAfterSync = false;

        // Cross-system state (read by AshenAltarsCampaignBehavior)
        internal static int _lastSanctuaryUseDay = -999;
        private static int  _sanctuaryUseCount   = 0;

        // Per-rite cooldown tracking
        private static int  _lastPrayerDay     = -999;
        private static int  _lastProtectiveDay = -999;

        private static readonly Random _rng = new Random();

        // ── CampaignBehaviorBase ───────────────────────────────────────────────
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore store)
        {
            try
            {
                var ids = _permanentSanctuaryIds.ToList();
                store.SyncData("SANCT_PermanentIds", ref ids);
                if (ids != null) { _permanentSanctuaryIds.Clear(); foreach (var id in ids) _permanentSanctuaryIds.Add(id); }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { store.SyncData("SANCT_Announced", ref _sanctuariesAnnounced); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { store.SyncData("SANCT_LastUseDay", ref _lastSanctuaryUseDay); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { store.SyncData("SANCT_UseCount", ref _sanctuaryUseCount); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { store.SyncData("SANCT_LastPrayerDay", ref _lastPrayerDay); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { store.SyncData("SANCT_LastProtectiveDay", ref _lastProtectiveDay); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            EnsurePermanentSanctuaries();
            RegisterSanctuaryMenus(starter);
        }

        // Clears all per-campaign static state. Must be called when a new game is
        // created, otherwise sanctuary picks and the "announced" flag carry over
        // from a previous game played in the same Bannerlord session — which makes
        // EnsurePermanentSanctuaries early-return (list already full) and suppresses
        // the establishment toast on the new campaign.
        public static void ResetForNewGame()
        {
            _permanentSanctuaryIds.Clear();
            _sanctuariesAnnounced       = false;
            _needsAnnouncementAfterSync = false;
            _lastSanctuaryUseDay        = -999;
            _sanctuaryUseCount          = 0;
            _lastPrayerDay              = -999;
            _lastProtectiveDay          = -999;
        }

        // Authoritative new-game setup. Fired once from OnCharacterCreationIsOver —
        // a point that is unambiguously after the world is fully built and never
        // fires on a load — so it sidesteps the fragile ordering between
        // OnNewGameCreated and OnSessionLaunched. Clears any carry-over from a prior
        // game in the same session, picks fresh sanctuaries, and announces them.
        public static void EstablishForNewCampaign()
        {
            ResetForNewGame();
            EnsurePermanentSanctuaries();
            AnnounceSanctuaries();
        }

        // Announces the established sanctuaries once. Safe to call repeatedly — it
        // self-guards on the announced flag and an empty list.
        private static void AnnounceSanctuaries()
        {
            _needsAnnouncementAfterSync = false;
            if (_sanctuariesAnnounced || _permanentSanctuaryIds.Count == 0) return;
            _sanctuariesAnnounced = true;
            try
            {
                var names = _permanentSanctuaryIds
                    .Select(id => Settlement.All.FirstOrDefault(s => s.StringId == id)?.Name?.ToString())
                    .Where(n => !string.IsNullOrEmpty(n)).ToList();
                if (names.Count > 0)
                {
                    string joined = names.Count == 1 ? names[0]
                        : string.Join(", ", names.Take(names.Count - 1)) + ", and " + names.Last();
                    string line = $"Sanctuaries of the Flame have been established in {joined}. "
                                + "Honourable and Merciful travellers may seek their rites there.";
                    MBInformationManager.AddQuickInformation(new TextObject(line));
                    // Also post to the message log so the list survives the new-game
                    // lore popup and stays retrievable in the scrollback.
                    try { InformationManager.DisplayMessage(new InformationMessage(line, new Color(0.95f, 0.75f, 0.35f))); }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Permanent sanctuary selection ──────────────────────────────────────
        private static void EnsurePermanentSanctuaries()
        {
            if (_permanentSanctuaryIds.Count >= PermanentSanctuaryCount) return;
            try
            {
                var empireIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { "empire_w", "empire", "empire_s", "empire_n" };
                var towns = Settlement.All.Where(s => s.IsTown).ToList();
                // All Vlandia (Holy Temple) towns are auto-sanctuaries via HasSanctuary.
                // The permanent list holds Empire towns only.
                var empirePicks = towns
                    .Where(s => s.OwnerClan?.Kingdom != null
                             && empireIds.Contains(s.OwnerClan.Kingdom.StringId))
                    .OrderBy(_ => _rng.Next()).Take(EmpireSanctuaryCount).ToList();

                // Fallback: if the empire filter comes up short top up with any non-Temple towns.
                if (empirePicks.Count < EmpireSanctuaryCount)
                    empirePicks.AddRange(towns
                        .Where(s => !empirePicks.Contains(s)
                                 && s.OwnerClan?.Kingdom?.StringId != TempleKingdomId)
                        .OrderBy(_ => _rng.Next())
                        .Take(EmpireSanctuaryCount - empirePicks.Count));

                _permanentSanctuaryIds.Clear();
                foreach (var s in empirePicks) _permanentSanctuaryIds.Add(s.StringId);

                if (_permanentSanctuaryIds.Count > 0 && !_sanctuariesAnnounced)
                    _needsAnnouncementAfterSync = true;
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        internal static bool AddPermanentSanctuary(string id)
        {
            if (string.IsNullOrEmpty(id) || _permanentSanctuaryIds.Contains(id)) return false;
            _permanentSanctuaryIds.Add(id);
            return true;
        }

        internal static bool HasSanctuary(Settlement s)
        {
            if (s == null || !s.IsTown) return false;
            if (s.OwnerClan?.Kingdom?.StringId == TempleKingdomId) return true;
            return _permanentSanctuaryIds.Contains(s.StringId);
        }

        // ── Shared helpers (previously in the now-removed Rites partial) ────────
        private static int CurrentCampaignDay()
        {
            try { return (int)CampaignTime.Now.ToDays; } catch { return 0; }
        }

        internal static bool IsTempleMember()
            => Hero.MainHero?.Clan?.Kingdom?.StringId == TempleKingdomId;
    }
}
