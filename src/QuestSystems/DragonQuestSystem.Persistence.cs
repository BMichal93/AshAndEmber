// =============================================================================
// ASH AND EMBER — DragonQuestSystem.Persistence.cs
// Save/load.
// Partial of DragonQuestSystem (shared state lives in DragonQuestSystem.cs).
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
    public static partial class DragonQuestSystem
    {
        // ── Save / Load ───────────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            store.SyncData("LDM_DragonPhase",    ref _phase);
            store.SyncData("LDM_DragonGoal1",    ref _goal1Done);
            store.SyncData("LDM_DragonGoal2",    ref _goal2Done);
            store.SyncData("LDM_DragonGoal3",    ref _goal3Done);
            store.SyncData("LDM_DragonGoal4",    ref _goal4Done);
            store.SyncData("LDM_WorldRekindled", ref _worldRekindled);
            store.SyncData("LDM_EndingPhase",    ref _endingPhase);
        }

        private static void EnsureQuestLog()
        {
            _questLog = new DragonQuestLog();
            _questLog.StartQuest();
            _questLog.LogStarted();
            if (_goal1Done) _questLog.LogGoal1();
            if (_goal2Done) _questLog.LogGoal2();
            if (_goal3Done) _questLog.LogGoal3();
            if (_goal4Done) _questLog.LogGoal4();
            if (_goal1Done && _goal2Done && _goal3Done && _goal4Done) _questLog.LogAllDone();
            _questLog.UpdateProgress(Hero.MainHero?.Clan?.Tier ?? 0, _goal2Done, Hero.MainHero?.Level ?? 0, AshenRuinSystem.ClearedCount);
        }

        public static void ResetForNewGame()
        {
            _phase          = PhaseIdle;
            _goal1Done      = false;
            _goal2Done      = false;
            _goal3Done      = false;
            _goal4Done      = false;
            _worldRekindled = false;
            _endingPhase    = 0;
            _questLog       = null;
        }

        public static void OnGameStart()
        {
            // If a rekindled save is loaded, ensure world events stay off
            // (checked via WorldRekindled property in CampaignMapEvents)
        }
    }
}
