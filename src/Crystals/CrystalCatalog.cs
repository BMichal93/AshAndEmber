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
        Sunstone     = 0, // warmth pulse  — heal self + nearby allies
        Embershard   = 1, // shard burst   — AoE fire damage
        Rimeshard    = 2, // frost pulse   — slow nearby enemies
        Veilstone    = 3, // veil grasp    — strikes one random enemy at range
        Stormcrystal = 4, // thunder clap  — AoE damage + morale drain
        Duskstone    = 5, // despair wave  — morale drain + slow
        Thornveil    = 6, // root grasp    — immobilises one random enemy at range + damage
        Aegisstone   = 7, // bulwark pulse — self heal + knocks back nearby enemies
        Willowisp    = 8, // dread whisper — shatters the morale of one random enemy at range
        Bloodstone   = 9, // vampiric burst — AoE damage, heals caster on the blood spilled
        Zephyrglass  = 10,// quickening light — AoE haste for caster and nearby allies
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
                EffectDesc  = "Shard burst: deals 35 fire damage to all enemies within 5 m.",
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
                EffectDesc  = "Veil grasp: reaches out to one random enemy within 12 m — deals 60 HP and slows them by 25 % for 4 s.",
                Lore        = "Its surface is never quite still — something moves inside it at the threshold of sight. "
                            + "It does not release energy outward. It reaches. "
                            + "You will not know which of them it chooses until it has already chosen.",
            },
            new CrystalDef
            {
                Type        = CrystalType.Stormcrystal,
                ItemId      = "aae_stormcrystal",
                TradeGoodId = "iron",
                Name        = "Stormcrystal",
                GlowColor   = ColorSchool.Orange,
                EffectDesc  = "Thunder clap: deals 35 damage and drains 15 morale from all enemies within 4 m.",
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
                EffectDesc  = "Despair wave: drains 25 morale and slows enemies within 5 m by 20 % for 5 seconds.",
                Lore        = "It looks like ash pressed into glass. It grew in the penumbra of underground caverns "
                            + "where sunlight arrives only as rumour. "
                            + "What it releases is not warmth but the memory of its absence: a grey weight that settles on the will.",
            },
            new CrystalDef
            {
                Type        = CrystalType.Thornveil,
                ItemId      = "aae_thornveil",
                TradeGoodId = "vegetables",
                Name        = "Thornveil",
                GlowColor   = ColorSchool.Green,
                EffectDesc  = "Root grasp: reaches out to one random enemy within 10 m — deals 25 HP and roots them in place for 3 seconds.",
                Lore        = "Something grows inside this stone that has never seen the sun — a lattice of green threads, "
                            + "coiled and waiting. Loosed, it remembers being a root, and reaches for the nearest thing "
                            + "still able to run.",
            },
            new CrystalDef
            {
                Type        = CrystalType.Aegisstone,
                ItemId      = "aae_aegisstone",
                TradeGoodId = "wine",
                Name        = "Aegisstone",
                GlowColor   = ColorSchool.White,
                EffectDesc  = "Bulwark pulse: heals you for 20 HP and hurls every enemy within 5 m back from you.",
                Lore        = "Cut from a vein that never fractures no matter how the mountain shifts around it. "
                            + "Its lattice does not want to break, and for a moment it lends that stubbornness to its bearer — "
                            + "warmth returning, and the world briefly pushed to arm's length.",
            },
            new CrystalDef
            {
                Type        = CrystalType.Willowisp,
                ItemId      = "aae_willowisp",
                TradeGoodId = "cheese",
                Name        = "Willowisp",
                GlowColor   = ColorSchool.Nature,
                EffectDesc  = "Dread whisper: reaches into the mind of one random enemy within 12 m and shatters their nerve (-40 morale).",
                Lore        = "A cold, pale light drifts inside the stone, never quite where you last looked. "
                            + "It does not strike the body. It finds the one thought a soldier keeps buried — the fear "
                            + "of the dark path home — and holds it up to the light.",
            },
            new CrystalDef
            {
                Type        = CrystalType.Bloodstone,
                ItemId      = "aae_bloodstone",
                TradeGoodId = "meat",
                Name        = "Bloodstone",
                GlowColor   = ColorSchool.Red,
                EffectDesc  = "Vampiric burst: deals 25 damage to all enemies within 4 m and returns half of it to you as healing.",
                Lore        = "Darker than Embershard, and warm in a way no stone should be. "
                            + "It does not simply burn — it trades. What it takes from the flesh of your enemies, "
                            + "it gives back to yours, and asks no question about the fairness of the exchange.",
            },
            new CrystalDef
            {
                Type        = CrystalType.Zephyrglass,
                ItemId      = "aae_zephyrglass",
                TradeGoodId = "fish",
                Name        = "Zephyrglass",
                GlowColor   = ColorSchool.Yellow,
                EffectDesc  = "Quickening light: you and nearby allies (5 m) move 30 % faster for 6 seconds.",
                Lore        = "The clearest of the lattices, and the lightest. "
                            + "It holds no heat and no cold, only a hurry it cannot explain — a stone in a rush to be "
                            + "somewhere else, and generous enough to take you with it.",
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
