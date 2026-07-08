// =============================================================================
// ASH AND EMBER — GreatAwakeningCampaignBehavior.Opposition.cs
// Win condition for the non-Duneborn path: Duneborn's kingdom destroyed
// (by anyone, not only the player) before the sacrifice completes. The
// counter is frozen, never rolled back — it simply can never move again.
// =============================================================================

using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class GreatAwakeningCampaignBehavior
    {
        private void OppositionWeeklyTick()
        {
            if (_phase != PhaseActive) return;

            bool dunebornDestroyed = false;
            try
            {
                dunebornDestroyed = !Kingdom.All.Any(k => k.StringId == DunebornKingdomId && !k.IsEliminated);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            if (!dunebornDestroyed) return;

            _phase = PhaseOppositionWon;
            try
            {
                MBInformationManager.AddQuickInformation(new TextObject(
                    "Duneborn's kingdom is destroyed. Whatever waited beyond the Sands waits still — the Great " +
                    "Awakening has failed. The Dark Altar stands cold and unfinished."));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { GreatAwakeningQuestLog.CompleteOppositionWon(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
