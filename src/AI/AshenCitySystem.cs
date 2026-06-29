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

        private const int    MinGarrisonCity   = 500;
        private const int    MinGarrisonCastle = 300;
        private const int    MinHeroGold       = 150_000;
        private const string AshenKingdomId    = "ashen_kingdom";

        private static readonly string[] _targetSettlementNames =
        {
            // Core Ashen cities
            "Tyal", "Sibir", "Baltakhand", "Amprela",
            // Original castles
            "Urikskala", "Kaysar", "Dinar", "Vladiv",
            // Extended Ashen zone (nearby towns & castles — skipped automatically
            // if their clan also owns settlements outside this list)
            "Varnovapol", "Tepes", "Epinosa", "Takor", "Khimli",
            // Castles near Amprela (Lochana ~27, Syratos ~44; Epinosa already above)
            "Lochana", "Syratos",
            // Ostican (Vlandian) — assigned at game start; daily tick enforces retention
            "Ostican",
            // Additional Ashen cities
            "Argoron", "Omor",
            // Castles near Argoron
            "Atrion",
            // Castles near Omor
            "Ov Castle", "Mazhadan", "Mecalovea", "Rhesos",
        };

        // True if a settlement is one of the renamed Ashen cities/castles (matched by
        // vanilla name). The Ashen realm is exactly this set — never any other town.
        internal static bool IsTargetSettlement(Settlement s)
        {
            if (s == null) return false;
            try
            {
                string name = s.Name?.ToString() ?? "";
                return _targetSettlementNames.Any(n =>
                    name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
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
            _warThrottle      = 0;
            _clanThrottle     = 0;
            _villageThrottle  = 0;
            _recoveryThrottle = 0;
            _prisonerThrottle = 0;
        }
    }
}
