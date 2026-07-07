// =============================================================================
// ASH AND EMBER — AI/ForestClansSpeedModel.cs
// The Green Roads — the Forest Clans (formerly Battania) know the wood's hidden
// paths, and cross forest ground faster than any other host.
//
// Subclasses DefaultPartySpeedCalculatingModel and adds a flat speed factor to
// the player's Forest Clan parties whenever they stand on forest terrain. The
// bonus value lives in ForestClansCulture.ForestSpeedFactor. Registered in
// MainSubModule.OnGameStart alongside AshenDiplomacyModel.
// =============================================================================

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    internal sealed class ForestClansSpeedModel : DefaultPartySpeedCalculatingModel
    {
        public override ExplainedNumber CalculateFinalSpeed(MobileParty mobileParty, ExplainedNumber finalSpeed)
        {
            var result = base.CalculateFinalSpeed(mobileParty, finalSpeed);
            try
            {
                if (ForestClansCulture.IsForestClanParty(mobileParty) &&
                    ForestClansCulture.IsInForest(mobileParty))
                {
                    result.AddFactor(ForestClansCulture.ForestSpeedFactor,
                        new TextObject("{=ae_green_roads}The Green Roads"));
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            return result;
        }
    }
}
