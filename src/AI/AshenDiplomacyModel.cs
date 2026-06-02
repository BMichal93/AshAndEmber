// =============================================================================
// AshenDiplomacyModel.cs
// Subclasses DefaultDiplomacyModel to make Bannerlord's diplomacy AI behave
// naturally despite the permanent Ashen war.
//
//  • IsAtConstantWar — marks Ashen-vs-faction wars as permanent so the AI
//    does not count them against a kingdom's overcommitment limit.  Factions
//    still fight the Ashen; they just don't treat that war as a reason to
//    avoid starting new conflicts with each other.
//
//  • GetScoreOfDeclaringWar — nudges inter-faction war desire up so kingdoms
//    are happy to start wars against each other.
//
//  • GetScoreOfDeclaringPeace — trims inter-faction peace desire so wars last
//    long enough to matter.
// =============================================================================

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    internal sealed class AshenDiplomacyModel : DefaultDiplomacyModel
    {
        private const string AshenKingdomId = "ashen_kingdom";

        // True only when one faction is the Ashen kingdom and the other is a
        // genuinely different, non-null faction — never matches self-checks or
        // clan-vs-own-kingdom calls, which avoids triggering ExpelConstantWarClans.
        private static bool IsAshenVsOther(IFaction f1, IFaction f2)
        {
            if (f1 == null || f2 == null || f1 == f2) return false;
            return f1.StringId == AshenKingdomId ^ f2.StringId == AshenKingdomId;
        }

        private static bool InvolvesAshen(IFaction f1, IFaction f2)
            => f1?.StringId == AshenKingdomId || f2?.StringId == AshenKingdomId;

        // The engine queries this before counting a war toward overcommitment and
        // before generating peace proposals. Returning true excludes the Ashen war
        // from both, making it invisible to normal diplomacy AI.
        // Guarded to only match genuine Ashen-vs-other-faction pairs.
        public override bool IsAtConstantWar(IFaction faction1, IFaction faction2)
        {
            if (IsAshenVsOther(faction1, faction2)) return true;
            return base.IsAtConstantWar(faction1, faction2);
        }

        // Boost inter-faction war desire so kingdoms start conflicts readily.
        public override float GetScoreOfDeclaringWar(IFaction factionDeclaresWar, IFaction factionDeclaredWar,
            Clan evaluatingClan, out TextObject reason, bool includeReason)
        {
            float score = base.GetScoreOfDeclaringWar(
                factionDeclaresWar, factionDeclaredWar, evaluatingClan, out reason, includeReason);
            if (InvolvesAshen(factionDeclaresWar, factionDeclaredWar)) return score;
            return score + 30f;
        }

        // Trim peace desire for inter-faction wars so they end through attrition.
        // Floored at half the base score so a faction that is truly desperate can
        // still eventually seek peace rather than fighting to the last man.
        public override float GetScoreOfDeclaringPeace(IFaction factionDeclaresPeace, IFaction factionDeclaredPeace)
        {
            float score = base.GetScoreOfDeclaringPeace(factionDeclaresPeace, factionDeclaredPeace);
            if (InvolvesAshen(factionDeclaresPeace, factionDeclaredPeace)) return score;
            float adjusted = score - 20f;
            return adjusted < score * 0.5f ? score * 0.5f : adjusted;
        }
    }
}
