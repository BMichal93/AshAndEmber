// =============================================================================
// ASH AND EMBER — Talents/TalentCostCurve.cs
//
// The shared focus-point cost curve for EVERY learnable talent tree — the mage's
// element Codex, the Grace devotions, the crystal lattice, the fire paths and
// disciplines, and the dark gifts' will-price. Pure; no TaleWorlds types.
//
// Growth is gentle: each cost tier is charged several times before it rises, so
// the sequence of prices as you buy the 1st, 2nd, 3rd… thing in a tree is
//
//     1, 1, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 4, …
//
// (tier N is charged N+1 times). This replaces the old straight 1→2→3→4… ramp,
// which grew too steep too fast.
// =============================================================================

namespace AshAndEmber
{
    public static class TalentCostCurve
    {
        // Focus-point price of the NEXT purchase, given how many you already own
        // in that tree. Cost tier N covers owned counts up to N(N+3)/2 (exclusive):
        //   N=1 → owned 0..1   (2 buys at 1)
        //   N=2 → owned 2..4   (3 buys at 2)
        //   N=3 → owned 5..8   (4 buys at 3) …
        public static int Cost(int owned)
        {
            if (owned < 0) owned = 0;
            int n = 1;
            while (owned >= n * (n + 3) / 2) n++;
            return n;
        }
    }
}
