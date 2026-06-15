// =============================================================================
// ASH AND EMBER — AshenRuins/AshenRuinCampaignBehavior.cs
// Minimal CampaignBehaviorBase that wires AshenRuinMenus into the session
// lifecycle. State and logic live in AshenRuinSystem / AshenRuinMenus.
// =============================================================================

using TaleWorlds.CampaignSystem;

namespace AshAndEmber
{
    public class AshenRuinCampaignBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            try { AshenRuinMenus.OnSessionLaunched(starter); } catch { }
        }

        public override void SyncData(IDataStore dataStore) { }
    }
}
