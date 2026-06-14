// =============================================================================
// ASH AND EMBER — AshenQuestSystem.Persistence.cs
// Save/load.
// Partial of AshenQuestSystem (shared state lives in AshenQuestSystem.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public static partial class AshenQuestSystem
    {
        // ── Save / Load ───────────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            store.SyncData("LDM_VoidPhase",    ref _phase);
            store.SyncData("LDM_VoidPreG1",    ref _prereqGoal1);
            store.SyncData("LDM_VoidPreG2",    ref _prereqGoal2);
            store.SyncData("LDM_VoidCapCount", ref _capitalsCount);
            store.SyncData("LDM_VoidFrozen",   ref _worldFrozen);
            store.SyncData("LDM_VoidEndPhase", ref _endingPhase);

            var wList = _wastelandCities.ToList();
            store.SyncData("LDM_VoidWastelandIds", ref wList);
            _wastelandCities.Clear();
            if (wList != null) foreach (var id in wList) _wastelandCities.Add(id);
        }

        private static void EnsureQuestLog()
        {
            _questLog = new AshenQuestLog();
            _questLog.StartQuest();
            _questLog.LogCatchUp(_prereqGoal1, _prereqGoal2, _phase >= PhaseWasteland, _capitalsCount, _phase == PhaseAllDone);
        }

        public static void ResetForNewGame()
        {
            _phase         = PhaseIdle;
            _prereqGoal1   = false;
            _prereqGoal2   = false;
            _capitalsCount = 0;
            _worldFrozen   = false;
            _endingPhase   = 0;
            _questLog      = null;
            _wastelandCities.Clear();
        }
    }
}
