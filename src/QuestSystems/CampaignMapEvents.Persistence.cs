// =============================================================================
// ASH AND EMBER — CampaignMapEvents.Persistence.cs
// Save/load.
// Partial of CampaignMapEvents (shared state lives in CampaignMapEvents.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static partial class CampaignMapEvents
    {
        // ── Save / Load ───────────────────────────────────────────────────────
        // Called from MagicCampaignBehavior.SyncData().
        // SyncData works bidirectionally: saves on game-save, restores on load.
        public static void Save(IDataStore store)
        {
            store.SyncData("LDM_LongNightDays",   ref _longNightDaysRemaining);
            store.SyncData("LDM_BrokenWillFired", ref _brokenWillFired);
            var brokenList = _brokenKingdomIds.ToList();
            store.SyncData("LDM_BrokenKingdoms",  ref brokenList);
            if (brokenList != null)
            {
                _brokenKingdomIds.Clear();
                foreach (var id in brokenList) _brokenKingdomIds.Add(id);
            }

            var gotK = _gotKingdoms.ToList();
            var gotD = _gotDays.ToList();
            store.SyncData("LDM_GoTKingdoms", ref gotK);
            store.SyncData("LDM_GoTDays",     ref gotD);
            if (gotK != null && gotD != null && gotK.Count == gotD.Count)
            {
                _gotKingdoms.Clear(); _gotDays.Clear();
                for (int i = 0; i < gotK.Count; i++) { _gotKingdoms.Add(gotK[i]); _gotDays.Add(gotD[i]); }
            }

            int templeFounded = _templeFounded ? 1 : 0;
            store.SyncData("LDM_TempleFounded",    ref templeFounded);
            store.SyncData("LDM_ProtectedDays",    ref _protectedDaysRemaining);
            _templeFounded = templeFounded != 0;

            int pendingJoin = _pendingTempleJoin ? 1 : 0;
            store.SyncData("LDM_PendingTempleJoin", ref pendingJoin);
            _pendingTempleJoin = pendingJoin != 0;

            int gambitFired = _ashenGambitFired ? 1 : 0;
            store.SyncData("LDM_AshenGambitFired", ref gambitFired);
            _ashenGambitFired = gambitFired != 0;

            int deadMarchFirst = _deadMarchFirstFired ? 1 : 0;
            store.SyncData("LDM_DeadMarchFirst",   ref deadMarchFirst);
            _deadMarchFirstFired = deadMarchFirst != 0;
            store.SyncData("LDM_DeadMarchLastDay", ref _deadMarchLastFiredDay);

            int undyingHostFired = _undyingHostFired ? 1 : 0;
            store.SyncData("LDM_UndyingHostFired", ref undyingHostFired);
            _undyingHostFired = undyingHostFired != 0;

            store.SyncData("LDM_CampaignStartDay",    ref _campaignStartDay);
            store.SyncData("LDM_LastEventDay",       ref _lastEventElapsedDay);
            store.SyncData("LDM_LastConflictSeedDay", ref _lastConflictSeedDay);

            // Battlefield echo
            int echoPending = _battleEchoPending ? 1 : 0;
            store.SyncData("LDM_BattleEchoPending", ref echoPending);
            store.SyncData("LDM_BattleEchoPosX",    ref _battleEchoPosX);
            store.SyncData("LDM_BattleEchoPosY",    ref _battleEchoPosY);
            _battleEchoPending = echoPending != 0;

            // Portents
            SavePortents(store);
        }
    }
}
