// =============================================================================
// ASH AND EMBER — AI/AshenRecruitCatalog.cs
//
// The five Ashen troops (troops.xml — "Ashen Spawn") an Ashen-owned settlement
// will make for the player, and the prisoner toll each one costs. The
// REQUIRED prisoner tier climbs in lockstep with the Ashen troop's own rank
// (1 → 5) — turning common levies into an Ashen Thrall is easy, but an Ashen
// Revenant demands a captive who was already something before the cold took
// them. This is the whole "cost": no gold changes hands, only lives.
// =============================================================================

using System.Collections.Generic;

namespace AshAndEmber
{
    public sealed class AshenRecruitDef
    {
        public string TroopId;
        public string Name;
        public int    Rank;                 // 1 (Thrall) .. 5 (Revenant)
        public int    RequiredPrisonerTier;  // a qualifying prisoner's own CharacterObject.Tier
        public int    PrisonerCost;          // how many qualifying prisoners it costs
    }

    public static class AshenRecruitCatalog
    {
        public static readonly List<AshenRecruitDef> All = new List<AshenRecruitDef>
        {
            new AshenRecruitDef { TroopId = "ashen_thrall",   Name = "Ashen Thrall",   Rank = 1, RequiredPrisonerTier = 1, PrisonerCost = 3 },
            new AshenRecruitDef { TroopId = "ashen_warrior",  Name = "Ashen Warrior",  Rank = 2, RequiredPrisonerTier = 2, PrisonerCost = 3 },
            new AshenRecruitDef { TroopId = "ashen_invoker",  Name = "Ashen Invoker",  Rank = 3, RequiredPrisonerTier = 3, PrisonerCost = 2 },
            new AshenRecruitDef { TroopId = "ashen_warden",   Name = "Ashen Warden",   Rank = 4, RequiredPrisonerTier = 4, PrisonerCost = 2 },
            new AshenRecruitDef { TroopId = "ashen_revenant", Name = "Ashen Revenant", Rank = 5, RequiredPrisonerTier = 5, PrisonerCost = 1 },
        };
    }
}
