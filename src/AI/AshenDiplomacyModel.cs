// =============================================================================
// AshenDiplomacyModel.cs
// Subclasses DefaultDiplomacyModel with one purpose only: making the Ashen war
// permanent. All non-Ashen diplomacy is left entirely to the base game.
//
//  • IsAtConstantWar — marks every Ashen-vs-faction pair as a constant war so
//    the AI never proposes peace with the Ashen and does not count that war
//    against a kingdom's overcommitment limit.
//
//  • GetScoreOfDeclaringPeace — returns -10000 for any Ashen-involved pair so
//    the AI treats peace with the Ashen as impossible. All other pairs fall
//    through to the unmodified base score.
// =============================================================================

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;

namespace AshAndEmber
{
    internal sealed class AshenDiplomacyModel : DefaultDiplomacyModel
    {
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

        // Only intercept Ashen peace proposals — everything else uses base game logic.
        public override float GetScoreOfDeclaringPeace(IFaction factionDeclaresPeace, IFaction factionDeclaredPeace)
        {
            if (IsAshenFaction(factionDeclaresPeace) || IsAshenFaction(factionDeclaredPeace))
                return -10000f;
            return base.GetScoreOfDeclaringPeace(factionDeclaresPeace, factionDeclaredPeace);
        }
    }
}
