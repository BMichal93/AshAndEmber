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

using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    internal sealed class AshenDiplomacyModel : DefaultDiplomacyModel
    {
        // Covers both the Ashen kingdom and any individual Ashen clan temporarily
        // outside it — delegates to AshenCitySystem so the clan list stays in one place.
        private static bool IsAshenFaction(IFaction f) => AshenCitySystem.IsAshenFaction(f);

        private static bool IsAshenVsOther(IFaction f1, IFaction f2)
        {
            if (f1 == null || f2 == null || f1 == f2) return false;
            return IsAshenFaction(f1) ^ IsAshenFaction(f2);
        }

        private static bool InvolvesAshen(IFaction f1, IFaction f2)
            => IsAshenFaction(f1) || IsAshenFaction(f2);

        // The engine queries this before counting a war toward overcommitment and
        // before generating peace proposals. Returning true excludes the Ashen war
        // from both — covers both the Ashen kingdom and individual Ashen clans.
        public override bool IsAtConstantWar(IFaction faction1, IFaction faction2)
        {
            if (IsAshenVsOther(faction1, faction2)) return true;
            return base.IsAtConstantWar(faction1, faction2);
        }

        // Boost inter-faction war desire strongly so kingdoms start conflicts readily.
        // +100 is needed because Bannerlord's base score is typically −50 to −100 for
        // neutral-relation factions; +30 was insufficient to cross the threshold.
        public override float GetScoreOfDeclaringWar(IFaction factionDeclaresWar, IFaction factionDeclaredWar,
            Clan evaluatingClan, out TextObject reason, bool includeReason)
        {
            float score = base.GetScoreOfDeclaringWar(
                factionDeclaresWar, factionDeclaredWar, evaluatingClan, out reason, includeReason);
            if (InvolvesAshen(factionDeclaresWar, factionDeclaredWar)) return score;
            return score + 100f;
        }

        // Peace with the Ashen is impossible — return a deeply negative score so the
        // AI never proposes it; this covers both the kingdom and individual Ashen clans.
        // For inter-faction wars: only reduce peace desire when the faction positively wants
        // peace (score > 0). Leave negative scores alone — a faction already fighting and
        // not wanting peace should not have that score bumped upward to 0.
        public override float GetScoreOfDeclaringPeace(IFaction factionDeclaresPeace, IFaction factionDeclaredPeace)
        {
            float score = base.GetScoreOfDeclaringPeace(factionDeclaresPeace, factionDeclaredPeace);
            if (InvolvesAshen(factionDeclaresPeace, factionDeclaredPeace)) return -10000f;
            if (score <= 0f) return score; // already against peace — don't interfere
            return Math.Max(0f, score - 20f);
        }
    }
}
