// =============================================================================
// ASH AND EMBER — GreatAwakeningCampaignBehavior.NpcContribution.cs
//
// Duneborn lords occasionally hand their own prisoners over to the altar — a
// background trickle (GreatAwakeningMath.NpcWeeklyContributionChance), never
// the primary driver of the ten thousand. Mirrors DarkGiftSystem.SeedNpcGifts's
// probability-gated pattern, but as a recurring weekly roll rather than a
// one-time seed.
// =============================================================================

using System.Linq;
using TaleWorlds.CampaignSystem;

namespace AshAndEmber
{
    public partial class GreatAwakeningCampaignBehavior
    {
        private void NpcContributionWeeklyTick()
        {
            if (_phase != PhaseActive) return;
            if (!AltarIsDunebornOwned()) return;

            Kingdom duneborn = DunebornKingdom();
            if (duneborn == null) return;

            int total = 0;
            try
            {
                foreach (Hero lord in duneborn.Heroes.Where(h =>
                             h != null && h.IsLord && h.IsAlive && !h.IsChild && h != Hero.MainHero).ToList())
                {
                    var party = lord.PartyBelongedTo;
                    var roster = party?.PrisonRoster;
                    if (roster == null) continue;
                    int held = roster.GetTroopRoster().Where(e => !e.Character.IsHero).Sum(e => e.Number);
                    if (held <= 0) continue;
                    if (_rng.NextDouble() >= GreatAwakeningMath.NpcWeeklyContributionChance) continue;

                    int give = GreatAwakeningMath.NpcContributionAmount(_rng, held);
                    if (give <= 0) continue;

                    int left = give;
                    foreach (var entry in roster.GetTroopRoster().Where(e => !e.Character.IsHero).ToList())
                    {
                        if (left <= 0) break;
                        int take = System.Math.Min(entry.Number, left);
                        roster.AddToCounts(entry.Character, -take);
                        left -= take;
                    }
                    total += give - left;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            if (total > 0)
            {
                _prisonersSacrificed += total;
                try { GreatAwakeningQuestLog.UpdateProgress(_prisonersSacrificed, GreatAwakeningMath.PrisonerTarget); }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }
    }
}
