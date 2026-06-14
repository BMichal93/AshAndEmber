// =============================================================================
// ASH AND EMBER — Sanctuary/SanctuaryCampaignBehavior.cs
//
// Ritual-based prayer system. When a player selects a prayer/spell, a hidden
// target number is rolled. Each round of Meditation inflicts a cost on the
// party (troops wounded or days aging). A hidden number of points — scaled
// by alignment traits — is added to the player's accumulated pool. The player
// chooses to stop or continue each round. If they stop once accumulated points
// meet or exceed the target, the prayer fires. Stopping short wastes the cost.
//
// Sanctuaries: Temple-owned towns + 4 random Empire towns.
// Any hero may approach. Alignment (Mercy+Honor+Generosity)/6 determines yield per
// round and effect strength. Zero alignment gives 1 pt/round — success is possible
// but requires many painful rounds for little reward.
// Temple members reduce all rite cooldowns by 40%.
//
// NPC effects (daily tick):
//   Honourable + Merciful lords in a sanctuary city: 0.3% chance/day.
//   Temple faction lords: 3% chance/day to heal; 2% to Turn nearby Ashen.
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
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public partial class SanctuaryCampaignBehavior : CampaignBehaviorBase
    {
        // ── Tuning ─────────────────────────────────────────────────────────────
        private const int MoralePrayerBoost    = 40;
        private const int ProtectiveDays       = 14;
        private const int BlessingRejuvDays    = 365;
        private const int BlessingMinAge       = 20;
        private const int BlessedDays          = 3;
        private const int SteadyLineDays       = 5;
        private const int TraitBoostDays       = 60;
        private const int TraitDriftThreshold  = 10;
        private const int CrossInterferenceDays= 30;
        private const int PermanentSanctuaryCount = 4;

        // Cooldowns (base days; Temple reduces by 40%)
        private const int PrayerCooldownBase    =  7;
        private const int ProtectiveCooldownBase= 10;
        private const int TurnAshenCooldownBase = 10;
        private const int HealingCooldownBase   =  7;
        private const int BlessingCooldownBase  = 30;

        // Location depletion: after DepletionThreshold ritual starts the flame rests
        private const int DepletionThreshold    =  5;
        private const int DepletionCooldown     = 30;

        // Ritual target ranges (hidden from player — lo inclusive, hi inclusive)
        private const int PrayerTargetLo   = 10; private const int PrayerTargetHi   = 18;
        private const int HealTargetLo     = 18; private const int HealTargetHi     = 30;
        private const int ProtectTargetLo  = 22; private const int ProtectTargetHi  = 35;
        private const int TurnTargetLo     = 26; private const int TurnTargetHi     = 40;
        private const int BlessTargetLo    = 35; private const int BlessTargetHi    = 55;

        private const string TempleKingdomId = "the_temple";
        private const string AshenKingdomId  = "ashen_kingdom";

        private static readonly List<string> _permanentSanctuaryIds = new List<string>();
        private static bool _sanctuariesAnnounced       = false;
        private static bool _needsAnnouncementAfterSync = false;

        // Cross-system state (read by AshenAltarsCampaignBehavior)
        internal static int _lastSanctuaryUseDay = -999;
        private static int  _sanctuaryUseCount   = 0;

        private static int  _blessedUntilDay    = -1;
        private static int  _steadyLineUntilDay = -1;
        private static int  _traitBoostUntilDay = -1;
        private static float _traitBoostAmount  = 0f;

        // Per-rite cooldown tracking
        private static int  _lastPrayerDay     = -999;
        private static int  _lastProtectiveDay = -999;
        private static int  _lastTurnAshenDay  = -999;
        private static int  _lastHealingDay    = -999;
        private static int  _lastBlessingDay   = -999;

        private static readonly Dictionary<string, int> _locationUses          = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _locationDepletedUntil = new Dictionary<string, int>();

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
            catch { }
            try { store.SyncData("SANCT_Announced", ref _sanctuariesAnnounced); } catch { }
            try { store.SyncData("SANCT_LastUseDay", ref _lastSanctuaryUseDay); } catch { }
            try { store.SyncData("SANCT_UseCount", ref _sanctuaryUseCount); } catch { }
            try { store.SyncData("SANCT_BlessedUntilDay", ref _blessedUntilDay); } catch { }
            try { store.SyncData("SANCT_SteadyLineUntilDay", ref _steadyLineUntilDay); } catch { }
            try { store.SyncData("SANCT_TraitBoostUntilDay", ref _traitBoostUntilDay); } catch { }
            try { store.SyncData("SANCT_TraitBoostAmount", ref _traitBoostAmount); } catch { }
            try { store.SyncData("SANCT_LastPrayerDay", ref _lastPrayerDay); } catch { }
            try { store.SyncData("SANCT_LastProtectiveDay", ref _lastProtectiveDay); } catch { }
            try { store.SyncData("SANCT_LastTurnAshenDay", ref _lastTurnAshenDay); } catch { }
            try { store.SyncData("SANCT_LastHealingDay", ref _lastHealingDay); } catch { }
            try { store.SyncData("SANCT_LastBlessingDay", ref _lastBlessingDay); } catch { }
            try
            {
                var luKeys = _locationUses.Keys.ToList();
                var luVals = _locationUses.Values.ToList();
                store.SyncData("SANCT_LocUseKeys", ref luKeys);
                store.SyncData("SANCT_LocUseVals", ref luVals);
                if (luKeys != null && luVals != null)
                { _locationUses.Clear(); for (int i = 0; i < Math.Min(luKeys.Count, luVals.Count); i++) _locationUses[luKeys[i]] = luVals[i]; }
            } catch { }
            try
            {
                var ldKeys = _locationDepletedUntil.Keys.ToList();
                var ldVals = _locationDepletedUntil.Values.ToList();
                store.SyncData("SANCT_LocDepKeys", ref ldKeys);
                store.SyncData("SANCT_LocDepVals", ref ldVals);
                if (ldKeys != null && ldVals != null)
                { _locationDepletedUntil.Clear(); for (int i = 0; i < Math.Min(ldKeys.Count, ldVals.Count); i++) _locationDepletedUntil[ldKeys[i]] = ldVals[i]; }
            } catch { }
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
            _blessedUntilDay            = -1;
            _steadyLineUntilDay         = -1;
            _traitBoostUntilDay         = -1;
            _traitBoostAmount           = 0f;
            _lastPrayerDay              = -999;
            _lastProtectiveDay          = -999;
            _lastTurnAshenDay           = -999;
            _lastHealingDay             = -999;
            _lastBlessingDay            = -999;
            _locationUses.Clear();
            _locationDepletedUntil.Clear();
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
                    catch { }
                }
            }
            catch { }
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
                var picks = towns
                    .Where(s => s.OwnerClan?.Kingdom != null
                             && empireIds.Contains(s.OwnerClan.Kingdom.StringId))
                    .OrderBy(_ => _rng.Next()).Take(PermanentSanctuaryCount).ToList();

                // Fallback: if the empire filter comes up short (e.g. ownership not
                // yet settled, or a heavily-modded map), top up with any towns so the
                // sanctuary network is never empty.
                if (picks.Count < PermanentSanctuaryCount)
                    picks.AddRange(towns.Where(s => !picks.Contains(s))
                                        .OrderBy(_ => _rng.Next())
                                        .Take(PermanentSanctuaryCount - picks.Count));

                _permanentSanctuaryIds.Clear();
                foreach (var s in picks) _permanentSanctuaryIds.Add(s.StringId);
                if (picks.Count > 0 && !_sanctuariesAnnounced)
                    _needsAnnouncementAfterSync = true;
            }
            catch { }
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
    }
}
