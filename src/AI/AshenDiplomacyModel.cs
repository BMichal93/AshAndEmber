// =============================================================================
// AshenDiplomacyModel.cs
// Subclasses DefaultDiplomacyModel with two purposes:
//
//  • IsAtConstantWar — marks every Ashen-vs-faction pair as a constant war so
//    the AI never proposes peace with the Ashen and does not count that war
//    against a kingdom's overcommitment limit.
//
//  • GetScoreOfDeclaringPeace — returns -10000 for any Ashen-involved pair.
//    For all other pairs, blocks peace for the first MinWarDays of a war so
//    kingdoms cannot immediately end a war they just declared.
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

        // Marks Ashen-vs-faction wars as constant so the engine excludes them from
        // overcommitment checks and never generates peace proposals for them.
        public override bool IsAtConstantWar(IFaction faction1, IFaction faction2)
        {
            if (IsAshenVsOther(faction1, faction2)) return true;
            return base.IsAtConstantWar(faction1, faction2);
        }

        public override float GetScoreOfDeclaringPeace(IFaction factionDeclaresPeace, IFaction factionDeclaredPeace)
        {
            if (IsAshenFaction(factionDeclaresPeace) || IsAshenFaction(factionDeclaredPeace))
                return -10000f;

            // Prevent any kingdom from ending a war that started less than MinWarDays ago.
            try
            {
                var stance = factionDeclaresPeace.GetStanceWith(factionDeclaredPeace);
                if (stance != null && stance.IsAtWar && stance.WarStartDate.ElapsedDaysUntilNow < MinWarDays)
                    return -5000f;
            }
            catch { }

            return base.GetScoreOfDeclaringPeace(factionDeclaresPeace, factionDeclaredPeace);
        }
    }
}
