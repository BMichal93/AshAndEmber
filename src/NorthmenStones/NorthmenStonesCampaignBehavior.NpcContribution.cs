// =============================================================================
// ASH AND EMBER — NorthmenStonesCampaignBehavior.NpcContribution.cs
//
// Northmen lords occasionally hand over materials of their own — a background
// trickle, never the primary driver. They never donate bound Kindled; that
// stays a deliberately hard, player-only track.
// =============================================================================

using System.Linq;
using TaleWorlds.CampaignSystem;

namespace AshAndEmber
{
    public partial class NorthmenStonesCampaignBehavior
    {
        private void NpcContributionWeeklyTick()
        {
            if (_phase != PhaseActive) return;
            if (!IsVarchegNorthmenOwned()) return;

            Kingdom northmen = NorthmenKingdom();
            if (northmen == null) return;

            try
            {
                foreach (Hero lord in northmen.Heroes.Where(h =>
                             h != null && h.IsLord && h.IsAlive && !h.IsChild && h != Hero.MainHero
                             && h.Clan != null && h.Clan.Settlements != null
                             && h.Clan.Settlements.Any(s => s.IsTown || s.IsCastle)).ToList())
                {
                    if (_rng.NextDouble() >= NorthmenStonesMath.NpcWeeklyContributionChance) continue;

                    switch (_rng.Next(5))
                    {
                        case 0: _iron     += NorthmenStonesMath.NpcContributionAmount(_rng, 20, 80);   break;
                        case 1: _hardwood += NorthmenStonesMath.NpcContributionAmount(_rng, 20, 80);   break;
                        case 2: _tools    += NorthmenStonesMath.NpcContributionAmount(_rng, 10, 40);   break;
                        case 3: _silver   += NorthmenStonesMath.NpcContributionAmount(_rng, 5, 20);    break;
                        default: _denars  += NorthmenStonesMath.NpcContributionAmount(_rng, 500, 3000); break;
                    }
                }

                int pct = (int)(NorthmenStonesMath.BlendedProgress(
                    _iron, _hardwood, _tools, _silver, _denars, KindledTotal()) * 100f);
                NorthmenStonesQuestLog.UpdateProgress(pct);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // "Materials are persistent... unless the city is captured by a
        // different faction, in which case they disappear by 10 percent per
        // week." Applies every week the town isn't Northmen's, resumes normal
        // accumulation the moment it's retaken.
        private void ApplyDecayWeeklyTick()
        {
            if (_phase != PhaseActive) return;
            if (IsVarchegNorthmenOwned()) return;

            try
            {
                _iron     = NorthmenStonesMath.ApplyWeeklyDecay(_iron);
                _hardwood = NorthmenStonesMath.ApplyWeeklyDecay(_hardwood);
                _tools    = NorthmenStonesMath.ApplyWeeklyDecay(_tools);
                _silver   = NorthmenStonesMath.ApplyWeeklyDecay(_silver);
                _denars   = NorthmenStonesMath.ApplyWeeklyDecay(_denars);
                _kindledStone = NorthmenStonesMath.ApplyWeeklyDecay(_kindledStone);
                _kindledFrost = NorthmenStonesMath.ApplyWeeklyDecay(_kindledFrost);
                _kindledSand  = NorthmenStonesMath.ApplyWeeklyDecay(_kindledSand);
                _kindledFlame = NorthmenStonesMath.ApplyWeeklyDecay(_kindledFlame);
                _kindledTide  = NorthmenStonesMath.ApplyWeeklyDecay(_kindledTide);
                _kindledGale  = NorthmenStonesMath.ApplyWeeklyDecay(_kindledGale);

                int pct = (int)(NorthmenStonesMath.BlendedProgress(
                    _iron, _hardwood, _tools, _silver, _denars, KindledTotal()) * 100f);
                NorthmenStonesQuestLog.UpdateProgress(pct);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
