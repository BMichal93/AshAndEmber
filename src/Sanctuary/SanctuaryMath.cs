// =============================================================================
// ASH AND EMBER — Sanctuary/SanctuaryMath.cs
//
// Pure numeric logic for the Vigil of Virtue (raising a personality trait at
// a Sanctuary). No TaleWorlds types, fully testable.
// =============================================================================

namespace AshAndEmber
{
    public static class SanctuaryMath
    {
        // Bannerlord traits run -2..2; the flame will not carry a hero further.
        public const int TraitCap = 2;

        public const int CommunionCooldownDays = 7;

        public static bool CanRaiseTrait(int currentLevel) => currentLevel < TraitCap;

        // The tribute scales with the level being reached — the flame asks little
        // of a first step toward virtue, and much of the last.
        public static int CommunionGoldCost(int targetLevel) => 2000 * (targetLevel + 3);

        public static int CommunionHpCost(int targetLevel) => 5 * (targetLevel + 3);
    }
}
