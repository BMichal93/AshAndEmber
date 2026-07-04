// =============================================================================
// ASH AND EMBER — DragonQuestSystem.Persistence.cs
// Save / load for The Sundered Crown quest.
// Partial of DragonQuestSystem (shared state lives in DragonQuestSystem.cs).
//
// Version history:
//   1 — original Temple / ember-collection system (legacy, no longer playable)
//   2 — The Sundered Crown (Aelisar Veth / Emperor soul quest)
// v2 uses the "LDQ2_" key prefix; a save from the legacy v1 system simply
// lacks these keys, so loading it leaves the quest at PhaseIdle — old saves
// are never corrupted by the changed state machine.
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
        public static void Save(IDataStore store)
        {
            // Keys use the "2_" prefix so they are distinct from the legacy v1 system.
            // Old saves simply lack these keys; SyncData leaves the ref variables at their
            // initialised-to-zero values, which resolves to PhaseIdle — safe default.
            store.SyncData("LDQ2_Phase",         ref _phase);
            store.SyncData("LDQ2_LordsSlain",    ref _lordsSlain);
            store.SyncData("LDQ2_VisionPhase",   ref _visionPhase);
            store.SyncData("LDQ2_ContactDay",    ref _contactDay);
            store.SyncData("LDQ2_EndingPhase",   ref _endingPhase);
            store.SyncData("LDQ2_ColdTarget",    ref _coldTownTarget);
            store.SyncData("LDQ2_ProxCooldown",  ref _proximityCheckCooldown);

            int heartInt = _heartCaptured ? 1 : 0;
            store.SyncData("LDQ2_HeartCaptured", ref heartInt);
            _heartCaptured = heartInt != 0;

            int worldBoundInt = _worldBound ? 1 : 0;
            store.SyncData("LDQ2_WorldBound", ref worldBoundInt);
            _worldBound = worldBoundInt != 0;

            // Settlement history for the cold-conquest path
            var everList = _everAshenSettlements.ToList();
            store.SyncData("LDQ2_EverAshen", ref everList);
            if (everList != null)
            {
                _everAshenSettlements.Clear();
                foreach (var s in everList) _everAshenSettlements.Add(s);
            }
        }

        private static void EnsureQuestLog()
        {
            _questLog = new DragonQuestLog();
            _questLog.StartQuest();
            _questLog.LogStarted();
            _questLog.UpdateProgress(_lordsSlain, DestinedRuinsCleared, _heartCaptured);
        }

        private static void EnsureColdQuestLog()
        {
            _coldQuestLog = new EternalColdQuestLog();
            _coldQuestLog.StartQuest();
            _coldQuestLog.LogStarted(_coldTownTarget);
        }

        public static void ResetForNewGame()
        {
            _phase                  = PhaseIdle;
            _lordsSlain             = 0;
            _visionPhase            = 0;
            _contactDay             = -1;
            _heartCaptured          = false;
            _endingPhase            = 0;
            _worldBound             = false;
            _coldTownTarget         = 0;
            _proximityCheckCooldown = 0;
            _questLog               = null;
            _coldQuestLog           = null;
            _everAshenSettlements.Clear();
        }
    }
}
