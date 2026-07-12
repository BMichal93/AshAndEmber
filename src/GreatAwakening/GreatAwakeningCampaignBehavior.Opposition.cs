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
        // One-time flag: the moment the count crosses the threshold, word of the
        // slaughter spreads and the announcement fires once. Persisted in SyncData.
        internal static bool _oppositionRoused = false;

        private void OppositionWeeklyTick()
        {
            if (_phase != PhaseActive) return;
            try { RousedOppositionTick(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

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

        // Past 60% of the count the world starts to move against Duneborn: every
        // kingdom at peace with them (except the player's own, and Duneborn itself)
        // rolls RousedWeeklyWarChance each week to declare war.
        private void RousedOppositionTick()
        {
            if (!GreatAwakeningMath.OppositionRoused(_prisonersSacrificed, GreatAwakeningMath.PrisonerTarget))
                return;

            Kingdom duneborn = DunebornKingdom();
            if (duneborn == null) return;

            if (!_oppositionRoused)
            {
                _oppositionRoused = true;
                try
                {
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "Word of the Dark Altar's count has spread beyond hiding. Thousands of the taken have " +
                        "vanished into Duneborn's south, and the courts of Calradia begin to speak of war."));
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }

            foreach (Kingdom k in Kingdom.All.ToList())
            {
                try
                {
                    if (k == null || k.IsEliminated || k == duneborn) continue;
                    if (k.Leader == Hero.MainHero) continue;              // the player's wars are the player's
                    if (k.IsAtWarWith(duneborn)) continue;
                    if (_rng.NextDouble() >= GreatAwakeningMath.RousedWeeklyWarChance) continue;
                    TaleWorlds.CampaignSystem.Actions.DeclareWarAction.ApplyByDefault(k, duneborn);
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"{k.Name} has declared war on {duneborn.Name} — the Great Awakening will not go unanswered."));
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }
    }
}
