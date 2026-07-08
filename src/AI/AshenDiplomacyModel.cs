// =============================================================================
// AshenDiplomacyModel.cs
// Subclasses DefaultDiplomacyModel with two purposes:
//
//  • IsAtConstantWar — marks every Ashen-vs-faction pair as a constant war so
//    the AI never proposes peace with the Ashen and does not count that war
//    against a kingdom's overcommitment limit. Also covers Duneborn-vs-everyone
//    while a CONTROLLED Great Other lives (GreatOtherParty.IsControlledAndAlive)
//    — the Great Awakening's "will not stop until The Great Other dies."
//
//  • GetScoreOfDeclaringPeace — returns -10000 for any Ashen-involved pair, or
//    any Duneborn pair while that same condition holds. For all other pairs,
//    blocks peace for the first MinWarDays of a war so kingdoms cannot
//    immediately end a war they just declared.
// =============================================================================

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;

namespace AshAndEmber
{
    internal sealed class AshenDiplomacyModel : DefaultDiplomacyModel
    {
        private const float MinWarDays = 60f; // ~2 in-game months before peace is possible

        private static bool IsAshenFaction(IFaction f) => AshenCitySystem.IsAshenFaction(f);

        private static bool IsAshenVsOther(IFaction f1, IFaction f2)
        {
            if (f1 == null || f2 == null || f1 == f2) return false;
            return IsAshenFaction(f1) ^ IsAshenFaction(f2);
        }

        private static bool IsArenicosPostMerger(IFaction f)
        {
            if (f == null) return false;
            return BurningLabQuestSystem.AshenMergedWithArenicos
                && BurningLabQuestSystem.ArenicosEmpireId != null
                && (f as Kingdom)?.StringId == BurningLabQuestSystem.ArenicosEmpireId;
        }

        // The Great Awakening, ending A: Duneborn "will not stop until The Great
        // Other dies" — active only while that summoned, CONTROLLED Great Other
        // is still alive (GreatOtherParty.IsControlledAndAlive). The block lifts
        // itself the moment it falls, with no extra bookkeeping needed here.
        private static bool IsDunebornPermanentWar(IFaction f1, IFaction f2)
        {
            if (f1 == null || f2 == null || f1 == f2) return false;
            if (!GreatOtherParty.IsControlledAndAlive()) return false;
            bool f1IsDuneborn = (f1 as Kingdom)?.StringId == GreatAwakeningCampaignBehavior.DunebornKingdomId;
            bool f2IsDuneborn = (f2 as Kingdom)?.StringId == GreatAwakeningCampaignBehavior.DunebornKingdomId;
            return f1IsDuneborn ^ f2IsDuneborn;
        }

        // Marks Ashen-vs-faction wars as constant so the engine excludes them from
        // overcommitment checks and never generates peace proposals for them.
        // Also locks all wars involving Arenicos's empire after the Ashen merger.
        public override bool IsAtConstantWar(IFaction faction1, IFaction faction2)
        {
            if (IsAshenVsOther(faction1, faction2)) return true;
            if (IsArenicosPostMerger(faction1) || IsArenicosPostMerger(faction2)) return true;
            if (IsDunebornPermanentWar(faction1, faction2)) return true;
            return base.IsAtConstantWar(faction1, faction2);
        }

        public override float GetScoreOfDeclaringPeace(IFaction factionDeclaresPeace, IFaction factionDeclaredPeace)
        {
            if (IsAshenFaction(factionDeclaresPeace) || IsAshenFaction(factionDeclaredPeace))
                return -10000f;
            if (IsArenicosPostMerger(factionDeclaresPeace) || IsArenicosPostMerger(factionDeclaredPeace))
                return -10000f;
            if (IsDunebornPermanentWar(factionDeclaresPeace, factionDeclaredPeace))
                return -10000f;

            // Prevent any kingdom from ending a war that started less than MinWarDays ago.
            try
            {
                var stance = factionDeclaresPeace.GetStanceWith(factionDeclaredPeace);
                if (stance != null && stance.IsAtWar && stance.WarStartDate.ElapsedDaysUntilNow < MinWarDays)
                    return -5000f;
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            return base.GetScoreOfDeclaringPeace(factionDeclaresPeace, factionDeclaredPeace);
        }
    }
}
