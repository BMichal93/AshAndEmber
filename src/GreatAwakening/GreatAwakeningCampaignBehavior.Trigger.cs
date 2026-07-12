// =============================================================================
// ASH AND EMBER — GreatAwakeningCampaignBehavior.Trigger.cs
// The day-50+ discovery roll. Fires once, ever; after that the leader's
// dialogue line (see .Dialogue.cs) carries the story forward.
// =============================================================================

using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class GreatAwakeningCampaignBehavior
    {
        private void TriggerWeeklyTick()
        {
            if (_phase != PhaseIdle) return;
            // Days elapsed since the campaign started — NOT CampaignTime.Now.ToDays,
            // which is absolute calendar days (~tens of thousands) and would pass
            // the start-day gate immediately on a new campaign.
            int day = (int)CampaignMapEvents.ElapsedCampaignDays();
            if (day < GreatAwakeningMath.TriggerStartDay) return;
            if (_rng.NextDouble() >= GreatAwakeningMath.TriggerChance(day)) return;

            _phase = PhaseDiscovered;
            try
            {
                MBInformationManager.AddQuickInformation(new TextObject(
                    "Dark forces gather in the deep desert. Word reaches you that Duneborn has found " +
                    "something ancient down in the Sands — and means to bring it in."));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
