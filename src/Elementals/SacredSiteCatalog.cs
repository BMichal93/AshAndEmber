// =============================================================================
// ASH AND EMBER — Elementals/SacredSiteCatalog.cs
//
// The six Kindled kinds craftable at a Forest Clans sacred site, and the
// permanent army troop (troops.xml) each one becomes once bound.
// =============================================================================

using System.Collections.Generic;

namespace AshAndEmber
{
    public sealed class SacredSiteDef
    {
        public ElementalKind Kind;
        public string TroopId;
        public string Name;
        public string Lore;
    }

    public static class SacredSiteCatalog
    {
        public static readonly List<SacredSiteDef> All = new List<SacredSiteDef>
        {
            new SacredSiteDef
            {
                Kind = ElementalKind.Stone, TroopId = "sacred_kindled_stone",
                Name = "Kindled of Stone",
                Lore = "The standing stones remember being mountains. Ask them for a little of "
                     + "that memory back, and something rises that has never once needed to breathe.",
            },
            new SacredSiteDef
            {
                Kind = ElementalKind.Frost, TroopId = "sacred_kindled_frost",
                Name = "Kindled of Frost",
                Lore = "Even in high summer the old grove keeps one corner of true winter. "
                     + "What wakes there does not feel the cold, because it is the cold.",
            },
            new SacredSiteDef
            {
                Kind = ElementalKind.Sand, TroopId = "sacred_kindled_sand",
                Name = "Kindled of Sand",
                Lore = "Not every sacred place is kind ground. Some old stones remember a desert "
                     + "the forest grew over long after, and what answers there still carries it.",
            },
            new SacredSiteDef
            {
                Kind = ElementalKind.Flame, TroopId = "sacred_kindled_flame",
                Name = "Kindled of Flame",
                Lore = "Where the lightning has found the same tree three winters running, the wood "
                     + "no longer burns — it simply waits for its next chance.",
            },
            new SacredSiteDef
            {
                Kind = ElementalKind.Tide, TroopId = "sacred_kindled_tide",
                Name = "Kindled of the Tide",
                Lore = "A sacred spring that never freezes and never floods keeps something patient "
                     + "coiled beneath its surface. It rises for a price.",
            },
            new SacredSiteDef
            {
                Kind = ElementalKind.Gale, TroopId = "sacred_kindled_gale",
                Name = "Kindled of the Gale",
                Lore = "On the bald hilltops where no tree has ever taken root, the wind itself is "
                     + "old enough to have opinions. This is what it looks like when it agrees to fight.",
            },
        };
    }
}
