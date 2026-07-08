// =============================================================================
// ASH AND EMBER — Soldier/SoldierServiceMath.cs
// Pure numeric logic for the Sworn Sword service (hiring your party out to a
// warring lord as a common soldier). No TaleWorlds types, so it stays unit-
// testable in PureLogicTests (see behaviour.md).
// =============================================================================

using System;

namespace AshAndEmber
{
    public static class SoldierServiceMath
    {
        // Term lengths (in days) a commander will offer a hired sword. A season,
        // a half-year, and a full campaign year on the Calradic calendar.
        public static readonly int[] TermDays = { 21, 42, 84 };

        // When the commander leads no host of his own, we raise a small patrol under
        // his banner so the player marches and fights as a true member. But a party
        // that is in an army can neither raise nor be summoned to the realm's real
        // armies, so we must not pin him there forever: after HostHoldDays of
        // patrolling we dissolve our host and ride escort for HostBreatherDays,
        // giving the campaign AI a clean window to draft him into (or let him raise)
        // a proper war host. If none comes, the patrol re-forms.
        public const double HostHoldDays     = 4.0;
        public const double HostBreatherDays = 3.0;

        // Weekly coin a commander pays a hired soldier: the party's full upkeep
        // (so taking the lord's coin never costs the player wages) plus a small
        // bounty that grows with the player's standing (clan tier).
        public static int WeeklyPay(int partyTotalWage, int clanTier)
        {
            int wage   = Math.Max(0, partyTotalWage);
            int bounty  = 50 + 25 * Math.Max(0, clanTier);
            return wage + bounty;
        }

        // Crime added to the betrayed realm's ledger when a soldier breaks the
        // oath before the agreed term. Heavier the more of the term is left.
        public static int DesertionCrime(double daysRemaining, double totalTermDays)
        {
            double frac = TermFraction(daysRemaining, totalTermDays);
            return (int)Math.Round(10.0 + 20.0 * frac); // 10 (almost done) .. 30 (just joined)
        }

        // Relation lost with the commander on desertion, scaled the same way.
        public static int DesertionRelationLoss(double daysRemaining, double totalTermDays)
        {
            double frac = TermFraction(daysRemaining, totalTermDays);
            return (int)Math.Round(6.0 + 12.0 * frac); // 6 .. 18
        }

        // Parting bonus paid on honourable completion of the full term.
        public static int CompletionBonus(int weeklyPay) => Math.Max(100, weeklyPay);

        // Fraction of the term still unserved, clamped to [0, 1].
        internal static double TermFraction(double daysRemaining, double totalTermDays)
        {
            if (totalTermDays <= 0.0) return 0.0;
            double frac = daysRemaining / totalTermDays;
            if (frac < 0.0) return 0.0;
            if (frac > 1.0) return 1.0;
            return frac;
        }
    }
}
