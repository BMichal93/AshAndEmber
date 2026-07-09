// =============================================================================
// LIFE & DEATH MAGIC — AI/AshenCitySystem.cs
// Manages the Ashen city clans and their kingdom.
//
// Target settlements: Tyal (Heart of Winter), Sibir, Baltakhand, Amprela (cities)
//                     Urikskala, Kaysar, Dinar, Vladiv, and others (castles)
//                     All renamed to Ashen names on session start (see Renaming.cs)
//
// On initialization:
//   1. Finds each target settlement, looks up its owner clan.
//   2. Creates the "ashen_kingdom" Kingdom if it doesn't exist.
//   3. Marks each hero Ashen, renames them, ejects from current kingdom.
//   4. Adds each clan to the Ashen kingdom.
//   5. Restores settlement ownership (prevents fief-distribution snatch).
//   6. Tops up garrisons with high-tier troops.
//   7. Gives each lord starting gold.
//   8. Declares war on every other kingdom.
//
// Daily maintenance:
//   - Ensures the Ashen kingdom is alive; reactivates if eliminated.
//   - Redeclares war with any kingdom that made peace.
//   - Refills garrisons below the minimum threshold.
//   - Refills hero gold below the minimum threshold.
//   - Settlement recovery: if a settlement is not owned by the expected Ashen
//     clan (initial setup or post-conquest), reclaims it on the next daily tick.
//   - Blocks natural aging of Ashen heroes.
//
// Daily maintenance also includes:
//   - TickAshenClanKingdoms: ejects Ashen clans from foreign kingdoms and
//     re-adds them to the Ashen kingdom. Done here (not in OnClanChangedKingdom)
//     to avoid re-entrancy: calling ApplyByLeaveKingdom inside ClanChangedKingdom
//     fires the same event again and can crash the campaign state.
//
// Event hook (ClanChangedKingdom):
//   - Intentionally empty (no-op). See TickAshenClanKingdoms above.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static partial class AshenCitySystem
    {
        private static readonly HashSet<string>           _ashenClanIds     = new HashSet<string>();
        private static readonly Dictionary<string,string> _settlementClanMap = new Dictionary<string,string>();
        private static readonly Dictionary<string,int>    _conqueredDays    = new Dictionary<string,int>();
        private static Kingdom  _ashenKingdom = null;
        private static bool     _initialized  = false;
        private static int      _appearanceDayCounter = 0;
        private static bool     _declaringWar        = false;
        private static bool     _ownershipInitDone   = false;
        private static bool     _settlementsRenamed  = false;
        private static bool     _kingdomsRenamed     = false;
        private static readonly Random _rng  = new Random();
        private const  int      AppearanceTickInterval = 30;

        // ── Throttle counters (not saved — reset in Save/Reset) ───────────────
        // Each counter decrements daily; the operation fires when it reaches 0
        // and is then reset to its interval. In Save() they are set to their
        // grace values so heavy actions never run on the first ticks after load.
        private static int _warThrottle       = 0;  // DeclareWar   — every 5 days
        private static int _clanThrottle      = 0;  // ClanKingdom  — every 3 days
        private static int _villageThrottle   = 0;  // Villages     — every 7 days
        private static int _recoveryThrottle  = 0;  // Settlement   — every 3 days
        private static int _prisonerThrottle  = 0;  // Prisoners    — every 2 days
        private static int _lordPartyThrottle = 0;  // Lord parties — every 7 days
        private const  int WarInterval        = 5;
        private const  int ClanInterval       = 3;
        private const  int VillageInterval    = 7;
        private const  int RecoveryInterval   = 3;
        private const  int PrisonerInterval   = 2;
        private const  int LordPartyInterval  = 7;

        private const int    MinGarrisonCity   = 750;
        private const int    MinGarrisonCastle = 450;
        private const int    MinLordPartySize  = 120;
        private const int    MinHeroGold       = 150_000;
        private const string AshenKingdomId    = "ashen_kingdom";

        // The Ashen realm is the far north: the fallen Sturgian heartland plus the
        // Khuzait marches the cold reached and the one Vlandian port it took. It
        // deliberately holds NO Northern Empire (EN) settlements — Amprela, Argoron,
        // Epinosa, Lochana, Syratos, Atrion, Mecalovea and Rhesos were once claimed
        // here, but they belong to clan_empire_north_* and must stay with the
        // Northern Empire; the old second-pass force-grab was dragging them into the
        // cold against the first pass's own clan-purity filter.
        private static readonly string[] _targetSettlementNames =
        {
            // Core Ashen cities (fallen Sturgia + the Khuzait marches)
            "Tyal", "Sibir", "Baltakhand",
            // Original castles
            "Urikskala", "Kaysar", "Dinar", "Vladiv",
            // Extended Ashen zone (nearby towns & castles — skipped automatically
            // if their clan also owns settlements outside this list)
            "Varnovapol", "Tepes", "Takor", "Khimli",
            // Ostican (Vlandian) — assigned at game start; daily tick enforces retention
            "Ostican",
            // Additional Ashen city
            "Omor",
            // Castles near Omor
            "Ov Castle", "Mazhadan",
        };

        // The same realm, keyed by the settlement's immutable StringId. Display names
        // are session data the mod itself overwrites (RenameAshenSettlements), so a
        // name lookup silently stops finding "Tyal" the moment it has become "The
        // Heart of Winter" — which is why every identity decision below (is this a
        // target? does this clan hold anything outside the cold? is this clan's seat
        // an Ashen city?) reads the id, never the name.
        //
        // Kept in the same order as the names above: Initialize walks this list, and the
        // first target it claims becomes the Ashen kingdom's seat — so Tyal leads.
        internal static readonly string[] _targetSettlementIds =
        {
            "town_S5",    // Tyal
            "town_S6",    // Sibir
            "town_K1",    // Baltakhand
            "castle_S7",  // Urikskala Castle
            "castle_K9",  // Kaysar Castle
            "castle_K6",  // Dinar Castle
            "castle_S8",  // Vladiv Castle
            "town_S4",    // Varnovapol
            "castle_K4",  // Tepes Castle
            "castle_S6",  // Takor Castle
            "castle_K5",  // Khimli Castle
            "town_V8",    // Ostican
            "town_S3",    // Omor
            "castle_S5",  // Ov Castle
            "castle_S2",  // Mazhadan Castle
        };

        // Membership lookups for the list above.
        private static readonly HashSet<string> _targetIdSet =
            new HashSet<string>(_targetSettlementIds, StringComparer.OrdinalIgnoreCase);

        internal static bool IsTargetSettlementId(string stringId) =>
            stringId != null && _targetIdSet.Contains(stringId);

        // True if a settlement belongs to the Ashen realm — exactly the target set,
        // never any other town. Settlements are RENAMED on session start (Tyal →
        // "The Heart of Winter"), so a pure vanilla-name match wrongly fails for the
        // realm's own renamed cities and they get handed away by the confinement
        // guard. The StringId check below is the authority; the two name matches are
        // kept as a safety net for any settlement the id table does not list.
        internal static bool IsTargetSettlement(Settlement s)
        {
            if (s == null) return false;
            try
            {
                // The realm proper (immutable id — survives any display rename).
                if (IsTargetSettlementId(s.StringId)) return true;

                // Claimed Ashen (keyed by StringId — survives any display rename).
                if (_settlementClanMap.ContainsKey(s.StringId)) return true;

                string name = s.Name?.ToString() ?? "";
                if (_targetSettlementNames.Any(n =>
                        name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0))
                    return true;

                // The Ashen display name it was renamed to (town / castle tables).
                if (_townRenames.Values.Any(v => name.Equals(v, StringComparison.OrdinalIgnoreCase))
                 || _castleRenames.Values.Any(v => name.Equals(v, StringComparison.OrdinalIgnoreCase)))
                    return true;

                return false;
            }
            catch { return false; }
        }

        public static void ResetForNewGame()
        {
            _initialized       = false;
            _ashenKingdom      = null;
            _ownershipInitDone = false;
            _declaringWar      = false;
            _ashenClanIds.Clear();
            _settlementClanMap.Clear();
            _conqueredDays.Clear();
            _appearanceDayCounter = 0;
            _settlementsRenamed   = false;
            _kingdomsRenamed      = false;
            _warThrottle      = 0;
            _clanThrottle     = 0;
            _villageThrottle  = 0;
            _recoveryThrottle = 0;
            _prisonerThrottle = 0;
        }
    }
}
