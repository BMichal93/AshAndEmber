// =============================================================================
// ASH AND EMBER — AshenCitySystem.Persistence.cs
// Public membership helpers and save/load.
// Partial of AshenCitySystem (shared static state lives in AshenCitySystem.cs).
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
    public partial class AshenCitySystem
    {
        // ── Helpers ───────────────────────────────────────────────────────────
        public static bool IsAshenClanMember(Hero hero)
        {
            if (hero == null || hero.Clan == null) return false;
            return _ashenClanIds.Contains(hero.Clan.StringId);
        }

        // Returns true for both the Ashen kingdom and any individual Ashen clan
        // that is temporarily outside the kingdom — used by AshenDiplomacyModel.
        public static bool IsAshenFaction(IFaction f)
        {
            if (f == null) return false;
            return f.StringId == AshenKingdomId || _ashenClanIds.Contains(f.StringId);
        }

        public static bool IsAshenSettlement(Settlement settlement) =>
            settlement != null && _settlementClanMap.ContainsKey(settlement.StringId);

        // ── Save / Load ───────────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            var ids   = _ashenClanIds.ToList();
            bool init    = _initialized;
            bool ownDone = _ownershipInitDone;
            store.SyncData("LDM_AshenClanIds",      ref ids);
            store.SyncData("LDM_AshenCityInit",     ref init);
            store.SyncData("LDM_OwnershipInitDone", ref ownDone);

            var sKeys = _settlementClanMap.Keys.ToList();
            var sVals = _settlementClanMap.Values.ToList();
            store.SyncData("LDM_AshenSettlementKeys", ref sKeys);
            store.SyncData("LDM_AshenSettlementVals", ref sVals);

            var cKeys = _conqueredDays.Keys.ToList();
            var cVals = _conqueredDays.Values.ToList();
            store.SyncData("LDM_AshenConqueredKeys", ref cKeys);
            store.SyncData("LDM_AshenConqueredVals", ref cVals);

            // Reconstruct in-memory state from the just-synced data
            _ashenClanIds.Clear();
            if (ids != null) foreach (var id in ids) _ashenClanIds.Add(id);
            _initialized        = init;
            _ownershipInitDone  = ownDone;

            _settlementClanMap.Clear();
            if (sKeys != null && sVals != null)
                for (int i = 0; i < Math.Min(sKeys.Count, sVals.Count); i++)
                    _settlementClanMap[sKeys[i]] = sVals[i];

            _conqueredDays.Clear();
            if (cKeys != null && cVals != null)
                for (int i = 0; i < Math.Min(cKeys.Count, cVals.Count); i++)
                    _conqueredDays[cKeys[i]] = cVals[i];

            // Clear Kingdom reference so it is always re-fetched from live
            // Kingdom.All — stale references from a previous session cause a
            // native crash when accessing .IsEliminated on a dead object.
            _ashenKingdom  = null;
            _declaringWar  = false;

            // Set grace periods so heavy campaign actions never fire on the
            // first daily ticks after loading (avoids stacking ChangeOwner /
            // DeclareWar / KillCharacter calls during game initialization).
            _warThrottle      = 2;                    // first DeclareWar: day 2
            _clanThrottle     = ClanInterval     + 1; // first ClanKingdom: day 4
            _villageThrottle  = VillageInterval  + 0; // first Village:    day 7
            _recoveryThrottle = RecoveryInterval + 1; // first Recovery:   day 4
            _prisonerThrottle = PrisonerInterval + 5; // first Prisoners:  day 7
        }
    }
}
