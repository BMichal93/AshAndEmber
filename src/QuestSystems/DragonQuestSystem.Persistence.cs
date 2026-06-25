// =============================================================================
// ASH AND EMBER — DragonQuestSystem.Persistence.cs
// Save / load.
// Partial of DragonQuestSystem (shared state lives in DragonQuestSystem.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
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
            store.SyncData("LDQ_Phase",          ref _phase);
            store.SyncData("LDQ_LordsSlain",     ref _lordsSlain);
            store.SyncData("LDQ_MageLordsSlain", ref _mageLordsSlain);
            store.SyncData("LDQ_CitiesTaken",    ref _citiesTaken);
            store.SyncData("LDQ_StoryPhase",     ref _storyPhase);
            store.SyncData("LDQ_MageStoryPhase", ref _mageStoryPhase);
            store.SyncData("LDQ_LetterPhase",    ref _letterPhase);
            store.SyncData("LDQ_EndingPhase", ref _endingPhase);
            store.SyncData("LDQ_ContactDay",  ref _contactDay);
            store.SyncData("LDQ_ColdTarget",  ref _coldTownTarget);

            int worldBoundInt = _worldBound ? 1 : 0;
            store.SyncData("LDQ_WorldBound",  ref worldBoundInt);
            _worldBound = worldBoundInt != 0;

            // Settlement tracking — persist so captures survive reload
            var everList     = _everAshenSettlements.ToList();
            var capturedList = _capturedAshenCities.ToList();
            store.SyncData("LDQ_EverAshen",    ref everList);
            store.SyncData("LDQ_CapturedAshen", ref capturedList);
            if (everList     != null) { _everAshenSettlements.Clear();  foreach (var s in everList)     _everAshenSettlements.Add(s);  }
            if (capturedList != null) { _capturedAshenCities.Clear();   foreach (var s in capturedList) _capturedAshenCities.Add(s);   }
        }

        private static void EnsureQuestLog()
        {
            _questLog = new DragonQuestLog();
            _questLog.StartQuest();
            _questLog.LogStarted();
            _questLog.UpdateProgress(_lordsSlain, _mageLordsSlain, _citiesTaken, AshenRuinSystem.ClearedCount);
        }

        private static void EnsureColdQuestLog()
        {
            _coldQuestLog = new EternalColdQuestLog();
            _coldQuestLog.StartQuest();
            _coldQuestLog.LogStarted(_coldTownTarget);
        }

        public static void ResetForNewGame()
        {
            _phase           = PhaseIdle;
            _lordsSlain      = 0;
            _mageLordsSlain  = 0;
            _citiesTaken     = 0;
            _storyPhase      = 0;
            _mageStoryPhase  = 0;
            _letterPhase     = 0;
            _endingPhase     = 0;
            _worldBound      = false;
            _contactDay      = -1;
            _coldTownTarget  = 0;
            _questLog     = null;
            _coldQuestLog = null;
            _everAshenSettlements.Clear();
            _capturedAshenCities.Clear();
        }

        public static void OnGameStart()
        {
            // If a world-bound save is loaded, downstream systems check WorldRekindled
            // on their own daily ticks — nothing to initialise here.
        }
    }
}
