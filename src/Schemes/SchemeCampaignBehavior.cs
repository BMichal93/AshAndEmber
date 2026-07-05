// =============================================================================
// ASH AND EMBER — Schemes/SchemeCampaignBehavior.cs
// Campaign behaviour: tavern-keeper dialogue hook, scheme game menu,
// NPC AI daily tick, pending-queue tick, and save/load.
//
// UI flow (game menu approach, matching Sanctuary pattern):
//   Town menu → "Discuss some shadier business..."
//     → "ldm_scheme_menu" game menu (one option per scheme type)
//       → MultiSelectionInquiry for target (lord or settlement)
//         → ShowInquiry confirmation
//           → QueueScheme()
//
// Tavernkeeper dialogue ("I have some shadier business...") defers a
// SwitchToMenu("ldm_scheme_menu") so it opens on the next clean tick.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class SchemeCampaignBehavior : CampaignBehaviorBase
    {
        private static SchemeDefinition _selectedDef;
        private static Kingdom          _selectedKingdom;
        private static readonly Random  _rng = new Random();

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore store)
        {
            try { SchemeSystem.Save(store); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

    }
}
