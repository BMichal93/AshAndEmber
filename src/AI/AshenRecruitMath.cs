// =============================================================================
// ASH AND EMBER — AI/AshenRecruitMath.cs
//
// Pure numeric core of recruiting the Ashen dead: prisoners are the price, not
// gold — turning a living captive into one of the cold-fire dead is the whole
// transaction, and gold has nothing to do with it. A prisoner only qualifies
// for a given Ashen rank if their OWN tier meets it (see AshenRecruitCatalog
// for the per-troop tier/count table) — this is what naturally limits the
// higher Ashen ranks to however many worthy captives the player has actually
// taken, rather than an arbitrary cap. No TaleWorlds types — unit-tested by
// PureLogicTests.
// =============================================================================

namespace AshAndEmber
{
    public static class AshenRecruitMath
    {
        public static bool PrisonerQualifies(int prisonerTier, int requiredTier)
            => prisonerTier >= requiredTier;
    }
}
