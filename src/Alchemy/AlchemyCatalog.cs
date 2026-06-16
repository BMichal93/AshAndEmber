// =============================================================================
// ASH AND EMBER — Alchemy/AlchemyCatalog.cs
//
// Static, lore-friendly definitions for the elixirs. Pure data (strings
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
                Effect = "Restores a quarter of your lifeblood and cleanses poison or weakness.",
                Flavour = "Red glass, warm to the touch. It tastes of iron and resin — and something older that burns clean.",
                UsableInBattle = true, UsableOnMap = true },

            new ElixirDef {
                Type = ElixirType.EmberBrew,
                Name = "Ember Brew",
                Effect = "A reckless fury — swifter feet, heavier blows, and fire in the blood to start.",
                Flavour = "It burns going down and keeps burning. The world slows; you do not.",
                UsableInBattle = true, UsableOnMap = false },

            new ElixirDef {
                Type = ElixirType.OathWine,
                Name = "Oath-Wine",
                Effect = "Lifts the spirit of your whole column and puts fire back in your own veins.",
                Flavour = "Poured at the old oaths. Men remember why they followed you — and you remember why you led them.",
                UsableInBattle = false, UsableOnMap = true },

            new ElixirDef {
                Type = ElixirType.HearthsmokeCenser,
                Name = "Hearthsmoke Censer",
                Effect = "Burned beside a village, it swells the hearth.",
                Flavour = "A grey-gold smoke that settles over the fields like a blessing. Granaries fill faster than they should.",
                UsableInBattle = false, UsableOnMap = true },

            new ElixirDef {
                Type = ElixirType.CausticVial,
                Name = "Caustic Vial",
                Effect = "Bursts in a searing cloud — wounds all nearby; enemies are left burning.",
                Flavour = "Do not breathe when it shatters. Do not stand near anyone you love.",
                UsableInBattle = true, UsableOnMap = false },

            new ElixirDef {
                Type = ElixirType.StonebloodTonic,
                Name = "Stoneblood Tonic",
                Effect = "Flesh sets like slag-stone — blows glance off, and some of the force returns to the striker.",
                Flavour = "Your skin greys and hardens. The man who hits you will feel it in his wrist. It will pass. He may not.",
                UsableInBattle = true, UsableOnMap = false },

            new ElixirDef {
                Type = ElixirType.FieldSurgeonPhiltre,
                Name = "Field Surgeon's Philtre",
                Effect = "Mends most of the wounded in your column — the dedicated army-medic's draught.",
                Flavour = "A field-tent in a bottle. The bandaged sit up before it cools; the dying sometimes follow.",
                UsableInBattle = false, UsableOnMap = true },

            new ElixirDef {
                Type = ElixirType.VeilOfAsh,
                Name = "Veil of Ash",
                Effect = "A shroud nothing can touch — untouchable in battle, and the rising ash slows those who press in.",
                Flavour = "Grey ash rises around you and will not be parted. Neither will you. The cold it carries is everyone else's problem.",
                UsableInBattle = true, UsableOnMap = false },

            new ElixirDef {
                Type = ElixirType.HoarfrostDraught,
                Name = "Hoarfrost Draught",
                Effect = "A breath of deep-winter cold — nearby foes are struck, then slowed and softened.",
                Flavour = "It tastes of the long night. The air frosts; the cold goes looking for others, and finds them.",
                UsableInBattle = true, UsableOnMap = false },

            new ElixirDef {
                Type = ElixirType.PyrebloodPhiltre,
                Name = "Pyreblood Philtre",
                Effect = "Wounds become fuel — every blow you land returns life to you.",
                Flavour = "The fire goes inward instead of out. It does not heal you. It makes the enemy's blood do that.",
                UsableInBattle = true, UsableOnMap = false },

            new ElixirDef {
                Type = ElixirType.MarrowmendTincture,
                Name = "Marrowmend Tincture",
                Effect = "Deep rest in a bottle — heals you in full and mends a share of your wounded.",
                Flavour = "Sleep distilled. You wake whole. Some of the men who could not walk this morning manage it by evening.",
                UsableInBattle = false, UsableOnMap = true },

            new ElixirDef {
                Type = ElixirType.KindlingCenser,
                Name = "Kindling Censer",
                Effect = "Burned beside a town, it steadies the people — loyalty and order both.",
                Flavour = "A warm resin-smoke drifts through the streets. Quarrels cool. The watch stands a little straighter and asks fewer questions.",
                UsableInBattle = false, UsableOnMap = true },
        };

        public static IReadOnlyList<ElixirDef> All => _defs;

        public static ElixirDef Get(ElixirType type)
            => _defs.First(d => d.Type == type);

        public static string Name(ElixirType type) => Get(type).Name;
    }
}
