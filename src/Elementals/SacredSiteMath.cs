// =============================================================================
// ASH AND EMBER — Elementals/SacredSiteMath.cs
//
// Pure numeric core of the Forest Clans' sacred sites: binding a Kindled at a
// standing stone or old grove, gated by Smithing — the same steady hand that
// shapes steel is what steadies a working like this. No TaleWorlds types —
// unit-tested by PureLogicTests.
// =============================================================================

using System;

namespace AshAndEmber
{
    public static class SacredSiteMath
    {
        // ── Cost — a costly working, on purpose ──────────────────────────────
        public const int GoldCost     = 3000;
        public const int IronOreCost  = 3;
        public const int CharcoalCost = 2;

        // ── Success odds ──────────────────────────────────────────────────────
        // Floor 10%, +0.4% per Smithing point, capped at 85% — the craft
        // rewards skill without ever becoming a certainty.
        private const float BaseOdds    = 0.10f;
        private const float PerSkill    = 0.004f;
        private const float OddsCeiling = 0.85f;

        public static float FormationOdds(int smithing)
        {
            float odds = BaseOdds + smithing * PerSkill;
            return Math.Min(OddsCeiling, Math.Max(BaseOdds, odds));
        }
    }
}
