// =============================================================================
// ASH AND EMBER — NorthmenStonesCampaignBehavior.Trigger.cs
// The day-30+ rumor roll. A Northmen player instead gets summoned outright the
// first week they qualify. Either way this only unlocks the leader's dialogue
// line (see .Dialogue.cs) — it does not start the quest by itself.
// =============================================================================

using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class NorthmenStonesCampaignBehavior
    {
        private void TriggerWeeklyTick()
        {
            if (_phase != PhaseIdle) return;
            // Days elapsed since the campaign started — NOT CampaignTime.Now.ToDays,
            // which is absolute calendar days (~tens of thousands) and would pass
            // the start-day gate immediately on a new campaign.
            int day = (int)CampaignMapEvents.ElapsedCampaignDays();
            if (day < NorthmenStonesMath.TriggerStartDay) return;

            if (IsPlayerNorthmen())
            {
                _phase = PhaseOffered;
                try
                {
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "A rider from the Ruler's hall finds you: the seers wish word with you, and word " +
                        "travels fastest to one of their own."));
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                return;
            }

            if (_rng.NextDouble() >= NorthmenStonesMath.TriggerChance(day)) return;

            _phase = PhaseOffered;
            try
            {
                MBInformationManager.AddQuickInformation(new TextObject(
                    "You have heard a rumor that something interesting is happening in the North. " +
                    "You should investigate."));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
