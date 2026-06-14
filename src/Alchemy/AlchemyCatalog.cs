// =============================================================================
// ASH AND EMBER — Alchemy/AlchemyCatalog.cs
//
// Static, lore-friendly definitions for the eight elixirs. Pure data (strings
// and enums only — no TaleWorlds types) so it is safe for the test runner and
// shared by both the menu UI and the effect code. Where an elixir may be drunk
// is captured by the UsableInBattle / UsableOnMap flags; the satchel screen and
// the brewing menu both read those flags rather than hard-coding behaviour.
// =============================================================================

using System.Collections.Generic;
using System.Linq;

namespace AshAndEmber
{
    public struct ElixirDef
    {
        public ElixirType Type;
        public string     Name;        // launcher/menu-visible name
        public string     Effect;      // one-line mechanical summary
        public string     Flavour;     // short climatic blurb
        public bool       UsableInBattle;
        public bool       UsableOnMap;  // campaign map (no mission)

        public string Context =>
            UsableInBattle && UsableOnMap ? "battle or field"
            : UsableInBattle              ? "battle only"
            :                               "field only";
    }

    public static class AlchemyCatalog
    {
        private static readonly List<ElixirDef> _defs = new List<ElixirDef>
        {
            new ElixirDef {
                Type = ElixirType.HealingDraught,
                Name = "Healing Draught",
                Effect = "Restores a quarter of your lifeblood.",
                Flavour = "Red glass, warm to the touch. It tastes of iron and resin.",
                UsableInBattle = true, UsableOnMap = true },

            new ElixirDef {
                Type = ElixirType.EmberBrew,
                Name = "Ember Brew",
                Effect = "A reckless fury — swifter feet, heavier blows for a time.",
                Flavour = "It burns going down and keeps burning. The world slows; you do not.",
                UsableInBattle = true, UsableOnMap = false },

            new ElixirDef {
                Type = ElixirType.OathWine,
                Name = "Oath-Wine",
                Effect = "Lifts the spirit of your whole column.",
                Flavour = "Poured at the old oaths. Men remember why they followed you.",
                UsableInBattle = false, UsableOnMap = true },

            new ElixirDef {
                Type = ElixirType.HearthsmokeCenser,
                Name = "Hearthsmoke Censer",
                Effect = "Burned beside a village, it swells the hearth.",
                Flavour = "A grey-gold smoke that settles over the fields like a blessing.",
                UsableInBattle = false, UsableOnMap = true },

            new ElixirDef {
                Type = ElixirType.CausticVial,
                Name = "Caustic Vial",
                Effect = "Bursts in a searing cloud — wounds all around you.",
                Flavour = "Do not breathe when it shatters. Do not stand near anyone you love.",
                UsableInBattle = true, UsableOnMap = false },

            new ElixirDef {
                Type = ElixirType.StonebloodTonic,
                Name = "Stoneblood Tonic",
                Effect = "Flesh sets like slag-stone — blows glance for a time.",
                Flavour = "Your skin greys and hardens. It will pass. It had better.",
                UsableInBattle = true, UsableOnMap = false },

            new ElixirDef {
                Type = ElixirType.FieldSurgeonPhiltre,
                Name = "Field Surgeon's Philtre",
                Effect = "Mends much of the wounded in your column.",
                Flavour = "A field-tent in a bottle. The bandaged sit up before it cools.",
                UsableInBattle = false, UsableOnMap = true },

            new ElixirDef {
                Type = ElixirType.VeilOfAsh,
                Name = "Veil of Ash",
                Effect = "A shroud nothing can touch — briefly untouchable in battle.",
                Flavour = "Grey ash rises around you and will not be parted. Neither will you.",
                UsableInBattle = true, UsableOnMap = false },
        };

        public static IReadOnlyList<ElixirDef> All => _defs;

        public static ElixirDef Get(ElixirType type)
            => _defs.First(d => d.Type == type);

        public static string Name(ElixirType type) => Get(type).Name;
    }
}
