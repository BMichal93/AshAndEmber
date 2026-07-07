// =============================================================================
// ASH AND EMBER — AI/ForestClansPenaltyModels.cs
// Wild and Few — the Forest Clans are untamed forest folk, close to the land and
// slow to the drilled ways of great armies. Their warbands run leaner, and
// keeping a host fed and paid in the deep wood costs more than it would in tamer
// country.
//
//   ForestClansWageModel      — +12% troop wages for the player's Forest Clan parties.
//   ForestClansPartySizeModel — −10% party member size limit for the same.
//
// Both subclass the default game model and touch only the player's Forest Clan
// parties; every other party resolves exactly as vanilla. Registered in
// MainSubModule.OnGameStart alongside AshenDiplomacyModel / ForestClansSpeedModel.
// =============================================================================

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    internal sealed class ForestClansWageModel : DefaultPartyWageModel
    {
        public override ExplainedNumber GetTotalWage(MobileParty mobileParty, TroopRoster troopRoster, bool includeDescriptions = false)
        {
            var result = base.GetTotalWage(mobileParty, troopRoster, includeDescriptions);
            try
            {
                if (ForestClansCulture.IsForestClanParty(mobileParty))
                    result.AddFactor(ForestClansCulture.WildWageFactor,
                        new TextObject("{=ae_wild_and_few}Wild and Few"));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            return result;
        }
    }

    internal sealed class ForestClansPartySizeModel : DefaultPartySizeLimitModel
    {
        public override ExplainedNumber GetPartyMemberSizeLimit(PartyBase party, bool includeDescriptions = false)
        {
            var result = base.GetPartyMemberSizeLimit(party, includeDescriptions);
            try
            {
                if (ForestClansCulture.IsForestClanParty(party?.MobileParty))
                    result.AddFactor(-ForestClansCulture.WildSizeFactor,
                        new TextObject("{=ae_wild_and_few}Wild and Few"));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            return result;
        }
    }
}
