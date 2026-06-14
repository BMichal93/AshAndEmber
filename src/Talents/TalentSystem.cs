// =============================================================================
// LIFE & DEATH MAGIC — TalentSystem.cs
// Talent definitions, purchase logic, lore text, and save/load.
// 22 talents: 8 passive, 8 enchantment (4 damage / 4 restore), 6 campaign spell.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public enum TalentId
    {
        Gift        = 0,   // Passive — starting talent
        // 1 reserved (Subjugate — moved to Ashen Altar rites)
        Rejuvenate  = 2,   // REMOVED — kept for save compatibility (healing merged into Kindle)
        PlantGrowth = 3,   // REMOVED — kept for save compatibility
        BreakWills  = 4,   // Spell
        Inspire     = 5,   // Spell
        Plague      = 6,   // Spell
        Clairvoyance= 7,   // Spell
        Extinguish  = 8,   // Spell
        DevourLife  = 9,   // REMOVED — kept for save compatibility (merged into Reap)
        BattleMage  = 10,  // Passive
        Sorcerer    = 11,  // Passive
        Camaraderie = 12,  // Passive
        Reap        = 13,  // Passive
        Ember       = 14,  // Passive
        // ── Damage enchantments ───────────────────────────────────────────────
        Scatter     = 15,  // Enchantment — Damage: push back + slow (absorbed Char)
        Smoulder    = 16,  // Enchantment — Damage: morale drain + bewilder (absorbed Bewilder)
        Bewilder    = 17,  // REMOVED — kept for save compatibility (merged into Smoulder)
        Waver       = 21,  // REMOVED — kept for save compatibility
        // ── Restore enchantments ─────────────────────────────────────────────
        Ashveil     = 18,  // Enchantment — Restore: magic immunity
        CinderShell = 19,  // Enchantment — Restore: armour boost + overheal shield (absorbed Overflow)
        Hearthlight = 20,  // Enchantment — Restore: morale boost
        Rouse       = 22,  // REMOVED — kept for save compatibility
        // ── Damage enchantments (continued) ──────────────────────────────────
        Sunder      = 23,  // Enchantment — Damage: armor shred + attack reduction (absorbed Sear)
        Consume     = 24,  // REMOVED — kept for save compatibility
        Char        = 25,  // REMOVED — kept for save compatibility (merged into Scatter)
        // ── Restore enchantments (continued) ─────────────────────────────────
        Overflow    = 26,  // REMOVED — kept for save compatibility (merged into Cinder Shell)
        Renewal     = 27,  // REMOVED — kept for save compatibility
        Reflect     = 28,  // Enchantment — Restore: melee damage reflection
        // ── Passives ──────────────────────────────────────────────────────────
        Flashfire   = 29,  // Passive — 10% chance to echo a battle spell
        VeteranAsh  = 30,  // REMOVED — kept for save compatibility (merged into Tempered)
        // ── Campaign spells ────────────────────────────────────────────────────
        Ashfall     = 31,  // REMOVED — kept for save compatibility
        Fade        = 32,  // Spell — conceal party from enemy scouts
        AshenGift   = 33,  // Info — status card shown when player is Ashen (not purchasable)
        Immolate    = 34,  // Enchantment — Damage: guaranteed kill at 3+ inputs
        ArmedCasting = 35, // Passive — cast without sheathing weapons
        Ashstorm     = 36, // Spell  — bombard a nearby enemy settlement
        ToxicFog     = 41, // Spell  — one-time powder: chokes nearby settlements and armies
        // ── Lost Forms ────────────────────────────────────────────────────────
        LostBlast   = 37, // Lost Form — widens blast cone (~49° → ~60°)
        LostMissile = 38, // Lost Form — twin bolts at 60% power each
        LostBarrier = 39, // Lost Form — barrier expires after 60 seconds
        LostBurst   = 40, // Lost Form — asymmetric burst (full front, 40% rear)
    }

    public enum TalentCategory { Passive, Enchantment, Spell, Info, LostForm }

    public class TalentDef
    {
        public TalentId      Id;
        public string        Name;
        public bool          IsSpell;        // true = campaign map spell
        public bool          IsEnchantment;  // true = battle enchantment
        public bool          IsInfo;         // true = display-only, not purchasable
        public bool          IsConsumable;   // true = found in world, not purchasable with focus points
        public TalentCategory Category;
        public string        Lore;
        public string        MechanicDesc;
        public int           FocusCost;   // 0 = use standard cost curve; >0 = fixed cost
    }

    public static partial class TalentSystem
    {
        private static readonly Random _rng = new Random();

        public static readonly IReadOnlyList<TalentDef> All = new List<TalentDef>
        {
            // ── Passive ──────────────────────────────────────────────────────
            new TalentDef
            {
                Id = TalentId.Gift, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.Passive, Name = "Gift",
                Lore = "The fire ran in your blood before you understood what fire was. Not warmth — something older. The kind that burns without consuming, and holds the world together at its edges.",
                MechanicDesc = "You carry the inner fire. In battle: form keys, Break, effect keys. W = Sear (burn), A = Force (push), D = Shred (armour) — all deal 25 damage. S = Restore (allies)."
            },
            new TalentDef
            {
                Id = TalentId.BattleMage, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.Passive, Name = "Tempered",
                Lore = "The forge teaches patience. A slow hand draws more from less; a careful reach into the fire takes without burning.",
                MechanicDesc = "Passive. Battle casts cost 25% fewer days (minimum 1 — never free). Beyond age 40, each year further reduces cast cost by 0.5%, up to 30% total."
            },
            new TalentDef
            {
                Id = TalentId.Sorcerer, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.Passive, Name = "Resonance",
                Lore = "Some days the fire gives back what it takes. You cannot predict it — only listen for it.",
                MechanicDesc = "Passive. Your first campaign map cast each day costs no days. Subsequent casts have a 25% chance to be free."
            },
            new TalentDef
            {
                Id = TalentId.Camaraderie, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.Passive, Name = "Kinship",
                Lore = "Those who carry the fire recognise each other from across a room. There is something almost like trust in that. Almost.",
                MechanicDesc = "Passive. +10 relations with mage lords, floor of 0. In battle alongside allied mage lords: −10% battle spell aging cost per allied mage (max −50%)."
            },
            new TalentDef
            {
                Id = TalentId.Reap, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.Passive, Name = "Reap",
                Lore = "Every life spent in your shadow leaves something behind — a warmth, a residue, the last gasp of a flame that burned for your purpose. You have learned to hold a vessel for it.",
                MechanicDesc = "Passive. Raiding a village restores 5 days of youth (7-day cooldown). Each prisoner discarded has a 5% chance to restore 1 day. Executing a captured lord restores 20 days of youth plus 10 per tier of their clan (max 80). Learning this marks you."
            },
            new TalentDef
            {
                Id = TalentId.Ember, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.Passive, Name = "Ember",
                Lore = "In the moment of killing, when fire passes from one vessel to another, some scatters. Sometimes a spark finds you. You have learned, not to seek it, but to cup your hands.",
                MechanicDesc = "Passive. Each kill on the battlefield has a 10% chance to restore 1 day of youth."
            },
            new TalentDef
            {
                Id = TalentId.Flashfire, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.Passive, Name = "Flashfire",
                Lore = "Sometimes the fire does not wait to be asked twice. It finds the shape again on its own — the same working, the same reach, the same burn. You do not question it. You simply let it.",
                MechanicDesc = "Passive. Each battle spell has a 10% chance to echo — firing again instantly at no aging cost."
            },
            new TalentDef
            {
                Id = TalentId.ArmedCasting, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.Passive, Name = "Warcast",
                Lore = "Most who carry the fire release it through open hands — shape first, reach second. You discovered, not by learning but by surviving, that the flame does not ask what you are holding. Only whether you are willing.",
                MechanicDesc = "Passive. You may cast battle spells without sheathing your weapons. The fire flows through you, not only from you."
            },
            // ── Enchantments (Damage) ─────────────────────────────────────────
            new TalentDef
            {
                Id = TalentId.Scatter, IsSpell = false, IsEnchantment = true,
                Category = TalentCategory.Enchantment, Name = "Scatter",
                Lore = "The fire does not merely burn — it expels. What it touches, it unmakes and flings aside. You have learned to aim that expulsion.",
                MechanicDesc = "Enchantment. Amplifies Force (A) inputs: enemies are blasted backward 5m per Force input and their limbs seared, reducing movement speed by 25% per input (max 75%) for 4s + 1.5s per input. Without this talent, Force gives a weak 1.5m push."
            },
            new TalentDef
            {
                Id = TalentId.Smoulder, IsSpell = false, IsEnchantment = true,
                Category = TalentCategory.Enchantment, Name = "Smoulder",
                Lore = "The fire knows what frightens. It does not need to kill a man to defeat him — only to let him feel how little warmth he carries. The courage drains out with the heat.",
                MechanicDesc = "Enchantment. Any Damage input scorches enemy morale (−15 per input) and bewilders non-hero enemies with a random effect — instant rout, force charge, dismount, or morale fractured to 25%. Fear of fire does not care what shape the fire takes."
            },
            new TalentDef
            {
                Id = TalentId.Sunder, IsSpell = false, IsEnchantment = true,
                Category = TalentCategory.Enchantment, Name = "Sunder",
                Lore = "Fire does not merely wound the surface — it reaches inward, finding the joins and seams of what they wear and what they carry. What holds together begins to separate. Not quickly. But enough.",
                MechanicDesc = "Enchantment. Amplifies Shred (D) inputs: vulnerability to incoming damage = 10% per Shred input (max 50% at 5 inputs); attack power reduction = 10% per input (max 50%); duration = 8s + 1.5s per input. Without this talent, Shred gives a weak 4%-per-input vulnerability."
            },
            new TalentDef
            {
                Id = TalentId.Immolate, IsSpell = false, IsEnchantment = true,
                Category = TalentCategory.Enchantment, Name = "Immolate",
                Lore = "Three times the fire has been called. Twice it asked. The third time, it takes. Not the wound — the whole. The body, the heat that kept it standing. The fire does not return what it has already claimed.",
                MechanicDesc = "Enchantment. Amplifies Sear (W) inputs: additional burn damage scales with inputs. At 1 Sear: 33% chance to kill. At 2 Sear: 50% chance to kill. At 3+ Sear: guaranteed kills (Sear inputs / 3). Without this talent, Sear gives a weak 5-per-input burn."
            },
            // ── Enchantments (Restore) ────────────────────────────────────────
            new TalentDef
            {
                Id = TalentId.Ashveil, IsSpell = false, IsEnchantment = true,
                Category = TalentCategory.Enchantment, Name = "Ashveil",
                Lore = "Ash does not burn twice. Coat something in it, and the fire cannot find purchase. For a few seconds, what you kindle becomes untouchable.",
                MechanicDesc = "Enchantment. Restore grants allies brief magic immunity. Duration = 2s per Restore input, max 10s."
            },
            new TalentDef
            {
                Id = TalentId.CinderShell, IsSpell = false, IsEnchantment = true,
                Category = TalentCategory.Enchantment, Name = "Cinder Shell",
                Lore = "Fire hardens what it doesn't consume. The skin does not become stone — it becomes something older. Whatever falls on them will not find the same flesh.",
                MechanicDesc = "Enchantment. Restore hardens allies, reducing incoming damage. Protection = 6% per Restore input (max 30% at 5 inputs). Duration = 4s + 1s per Restore input. When an ally is above 90% health, excess fire adds a damage shield of 10 HP per Restore input for 5s."
            },
            new TalentDef
            {
                Id = TalentId.Hearthlight, IsSpell = false, IsEnchantment = true,
                Category = TalentCategory.Enchantment, Name = "Hearthlight",
                Lore = "The fire in them has not gone out — it has only dimmed. You reach in and remind it what it is for. They remember, for a moment, that the fire is their friend.",
                MechanicDesc = "Enchantment. Restore lifts allied morale. Morale boost = 10 per Restore input. Without this talent, Restore gives a weak +4-per-input lift."
            },
            new TalentDef
            {
                Id = TalentId.Reflect, IsSpell = false, IsEnchantment = true,
                Category = TalentCategory.Enchantment, Name = "Reflect",
                Lore = "The fire you give is not passive. It waits in the body like an ember under ash, and when something cold strikes — it answers.",
                MechanicDesc = "Enchantment. Restore wraps allies in a retaliating flame. Melee hits against them reflect 5% of damage per Restore input back at the attacker, max 25%. Duration scales with diminishing returns: 7s at 1 input, ~10s at 3, ~16s at 10."
            },
            // ── Campaign map spells ──────────────────────────────────────────
            new TalentDef
            {
                Id = TalentId.BreakWills, IsSpell = true, IsEnchantment = false,
                Category = TalentCategory.Spell, Name = "Unsettle",
                Lore = "You let them feel how thin their fire is. Most men have never faced that knowledge directly. Courage is easier when you cannot see the dark.",
                MechanicDesc = "The nearest enemy party within 75m loses 40 morale. Costs 1 day."
            },
            new TalentDef
            {
                Id = TalentId.Inspire, IsSpell = true, IsEnchantment = false,
                Category = TalentCategory.Spell, Name = "Kindle",
                Lore = "You let them feel it briefly — the warmth that says the world cares whether they live. It may be a lie. The fire does not ask.",
                MechanicDesc = "Your party gains 40 morale. Up to 8 wounded soldiers of each troop type recover. Costs 1 day."
            },
            new TalentDef
            {
                Id = TalentId.Plague, IsSpell = true, IsEnchantment = false,
                Category = TalentCategory.Spell, Name = "Wither",
                Lore = "Fire leaves places slowly, or quickly, depending on who tends it. You remove the tender.",
                MechanicDesc = "The nearest enemy village loses a fifth of its hearth. Costs 1 day."
            },
            new TalentDef
            {
                Id = TalentId.Clairvoyance, IsSpell = true, IsEnchantment = false,
                Category = TalentCategory.Spell, Name = "Clairvoyance",
                Lore = "The lines of fire connect every living thing to every other. You read them the way a navigator reads stars — imperfectly, but well enough.",
                MechanicDesc = "Gain 25 influence. Without a kingdom, the insight becomes gold instead. Costs 1 day."
            },
            new TalentDef
            {
                Id = TalentId.Extinguish, IsSpell = true, IsEnchantment = false,
                Category = TalentCategory.Spell, Name = "Extinguish",
                Lore = "You reach into the fire burning in an enemy and close your hand. Not slowly — like snuffing a candle. The body does not understand at first. Then it does.",
                MechanicDesc = "5–12 soldiers in the nearest enemy party within 60m are wounded or killed, and their courage breaks. −30 morale. Costs 1 day."
            },
            // ── Campaign spells (continued) ────────────────────────────────────
            new TalentDef
            {
                Id = TalentId.Fade, IsSpell = true, IsEnchantment = false,
                Category = TalentCategory.Spell, Name = "Fade",
                Lore = "You draw your fire inward — not out, not away, but down into the marrow, down past what can be seen or felt. For a time you are still there. You simply stop being visible to those looking for you.",
                MechanicDesc = "Your party is concealed from enemy scouts for 1 day. Enemy parties will not pursue you. Costs 1 day."
            },
            new TalentDef
            {
                Id = TalentId.Ashstorm, IsSpell = true, IsEnchantment = false,
                Category = TalentCategory.Spell, Name = "Ashstorm",
                Lore = "The fire knows no walls. Stone does not argue with it. You raise your hands toward a distant tower and the flame answers — not as warmth, but as judgment.",
                MechanicDesc = "A storm of fire falls on the nearest enemy town or castle within 50 map units. 10–30 garrison soldiers are killed, food stores are burnt, security drops, and prosperity is scorched. Costs 1 day (standard map spell cost)."
            },
            new TalentDef
            {
                Id = TalentId.ToxicFog, IsSpell = true, IsEnchantment = false, IsConsumable = true,
                Category = TalentCategory.Spell, Name = "Toxic Fog",
                Lore = "A clay vessel stoppered with black wax. The powder inside smells of rot and old smoke. \"Burn it in still air,\" the maker said, \"then walk away.\" He was not wrong about that part.",
                MechanicDesc = "One use only — the vessel is spent on casting. A choking yellow-green cloud rolls across ALL nearby settlements and armies regardless of faction: militia are killed outright, soldiers choke and fall. Even odds the wind turns on your own men too. Every lord whose holdings the fog touches will know.",
                FocusCost = 0
            },
            // ── Ashen status (info-only, not purchasable) ─────────────────────
            new TalentDef
            {
                Id = TalentId.AshenGift, IsSpell = false, IsEnchantment = false, IsInfo = true,
                Category = TalentCategory.Info, Name = "The Cold Within",
                Lore = "The fire is gone. What remains is older, colder, and far more patient. It is not warmth you carry now — it is the memory of warmth and the hollow that followed.",
                MechanicDesc = "You are Ashen. You do not age. Each casting costs criminal rating instead of years. After your first working each day, each further cast risks the cold stirring against you — a possession that may claim your life."
            },
            // ── Lost Forms ─────────────────────────────────────────────────────
            new TalentDef
            {
                Id = TalentId.LostBlast, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.LostForm, FocusCost = 2, Name = "Widened Blast",
                Lore = "The fire does not ask how wide your arms can reach. It asks how wide your will can hold. You found a slightly different angle of release — not taught, not passed down, only survived. The cone opens. More earth scorched, fewer who dodge the edges.",
                MechanicDesc = "Lost Form. Blast cone widens from ~49° to ~60°. More enemies caught at the edge; the forward reach is unchanged."
            },
            new TalentDef
            {
                Id = TalentId.LostMissile, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.LostForm, FocusCost = 2, Name = "Twin Bolt",
                Lore = "The first time you split the bolt it was an accident — it came apart in your hands before release, two pieces each carrying their own heat. The second time was deliberate. Neither bolt is as strong as one whole. But one bolt can miss.",
                MechanicDesc = "Lost Form. Missile fires two bolts side by side. Each bolt carries 60% of the original damage and heal power."
            },
            new TalentDef
            {
                Id = TalentId.LostBarrier, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.LostForm, FocusCost = 2, Name = "Fading Ward",
                Lore = "The old barrier stood until you let it go. This one does not ask to be released — it knows when it has done its work. Sixty seconds, then the fire returns to you. Less permanent, but you carry it lighter.",
                MechanicDesc = "Lost Form. Barrier nodes expire after 60 seconds instead of persisting indefinitely. The fire returns on its own."
            },
            new TalentDef
            {
                Id = TalentId.LostBurst, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.LostForm, FocusCost = 2, Name = "Directed Burst",
                Lore = "You have always stood at the center. The fire went out evenly, touching everything the same. But an even field is not always what the moment needs. Lean into the front; let the rear feel only the echo. Not a perfect circle — a pointed wave.",
                MechanicDesc = "Lost Form. Burst is asymmetric. The forward hemisphere receives full power; the rear hemisphere receives 40%. Useful when your allies stand behind you."
            },
        };

    }


    internal static class MobilePartyExtensions
    {
        public static void Let(this MobileParty p, Action<MobileParty> action)
        {
            if (p != null) action(p);
        }
    }
}
