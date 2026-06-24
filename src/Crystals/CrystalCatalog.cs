// =============================================================================
// ASH AND EMBER — Crystals/CrystalCatalog.cs
//
// Static definitions for the six focused-light crystals. Pure data; no
// TaleWorlds types. The item_id matches the id attribute in items.xml.
// The trade_good_id is the vanilla Bannerlord ItemObject StringId for the
// material that resonates with each crystal's internal lattice.
// =============================================================================

using System.Collections.Generic;
using System.Linq;

namespace AshAndEmber
{
    public enum CrystalType
    {
        Sunstone     = 0, // warmth pulse — heal self + nearby allies
        Embershard   = 1, // shard burst  — AoE fire damage
        Rimeshard    = 2, // frost pulse  — slow nearby enemies
        Veilstone    = 3, // speed surge  — boost nearby allies
        Stormcrystal = 4, // thunder clap — AoE damage + morale drain
        Duskstone    = 5, // despair wave — morale drain + slow
    }

    public struct CrystalDef
    {
        public CrystalType  Type;
        public string       ItemId;       // matches items.xml id attribute
        public string       TradeGoodId;  // secondary material (vanilla item id)
        public string       Name;
        public ColorSchool  GlowColor;
        public string       EffectDesc;
        public string       Lore;
    }

    public static class CrystalCatalog
    {
        private static readonly List<CrystalDef> _defs = new List<CrystalDef>
        {
            new CrystalDef
            {
                Type        = CrystalType.Sunstone,
                ItemId      = "aae_sunstone",
                TradeGoodId = "leather",
                Name        = "Sunstone",
                GlowColor   = ColorSchool.Yellow,
                EffectDesc  = "Releases a warmth pulse: heals you for 30 HP and nearby allies for 15 HP (5 m radius).",
                Lore        = "A pale stone grown in the marrow of the earth, where mineral-rich waters pooled and cooled over centuries. "
                            + "In daylight it remembers every sun it has ever caught. "
                            + "When you break the lattice, it releases all of that warmth at once.",
            },
            new CrystalDef
            {
                Type        = CrystalType.Embershard,
                ItemId      = "aae_embershard",
                TradeGoodId = "spice",
                Name        = "Embershard",
                GlowColor   = ColorSchool.Red,
                EffectDesc  = "Shard burst: deals 30 fire damage to all enemies within 5 m.",
                Lore        = "The lattice grew too tight. The crystal holds more light than its structure can bear, "
                            + "and when the inner fire of its bearer meets that surplus, the whole thing detonates. "
                            + "Brief. Bright. Not particularly concerned with what is nearby.",
            },
            new CrystalDef
            {
                Type        = CrystalType.Rimeshard,
                ItemId      = "aae_rimeshard",
                TradeGoodId = "salt",
                Name        = "Rimeshard",
                GlowColor   = ColorSchool.Blue,
                EffectDesc  = "Frost pulse: slows all enemies within 5 m by 40 % for 5 seconds.",
                Lore        = "This one grew in a cold vein — too deep, too dark for proper sunlight. "
                            + "It learned to hold cold instead. What it releases is not warmth but its absence: "
                            + "a stillness that settles over everything nearby and makes motion feel like argument.",
            },
            new CrystalDef
            {
                Type        = CrystalType.Veilstone,
                ItemId      = "aae_veilstone",
                TradeGoodId = "linen_cloth",
                Name        = "Veilstone",
                GlowColor   = ColorSchool.Purple,
                EffectDesc  = "Veil weave: grants you and nearby allies 15 % speed for 4 seconds (5 m radius).",
                Lore        = "Its surface is never quite still — something moves inside it at the threshold of sight. "
                            + "In the moment of release, those caught in its field move as if the air has stepped aside for them.",
            },
            new CrystalDef
            {
                Type        = CrystalType.Stormcrystal,
                ItemId      = "aae_stormcrystal",
                TradeGoodId = "iron",
                Name        = "Stormcrystal",
                GlowColor   = ColorSchool.Orange,
                EffectDesc  = "Thunder clap: deals 35 damage and drains 20 morale from all enemies within 4 m.",
                Lore        = "Grown around a vein of iron ore, it learned to channel something the iron carried — "
                            + "a charge, a potential, a tension that cannot hold. "
                            + "The sound it makes on release has been described as the sky being knocked off its course.",
            },
            new CrystalDef
            {
                Type        = CrystalType.Duskstone,
                ItemId      = "aae_duskstone",
                TradeGoodId = "grain",
                Name        = "Duskstone",
                GlowColor   = ColorSchool.Ashen,
                EffectDesc  = "Despair wave: drains 25 morale and slows enemies within 6 m by 20 % for 5 seconds.",
                Lore        = "It looks like ash pressed into glass. It grew in the penumbra of underground caverns "
                            + "where sunlight arrives only as rumour. "
                            + "What it releases is not warmth but the memory of its absence: a grey weight that settles on the will.",
            },
        };

        public static IReadOnlyList<CrystalDef> All => _defs;

        public static CrystalDef Get(CrystalType type) => _defs.First(d => d.Type == type);

        public static bool TryGetByItemId(string itemId, out CrystalDef def)
        {
            def = default;
            if (string.IsNullOrEmpty(itemId)) return false;
            foreach (var d in _defs)
                if (d.ItemId == itemId) { def = d; return true; }
            return false;
        }

        public static bool IsCrystalItemId(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return false;
            foreach (var d in _defs)
                if (d.ItemId == itemId) return true;
            return false;
        }

        public static string[] AllItemIds()
        {
            var ids = new string[_defs.Count];
            for (int i = 0; i < _defs.Count; i++) ids[i] = _defs[i].ItemId;
            return ids;
        }
    }
}
