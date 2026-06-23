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
        // ── New enchantments ───────────────────────────────────────────────
        Scorch      = 42, // Enchantment — Damage: Sear leaves a lingering burn
        ChainIgnite = 43, // Enchantment — Damage: Immolate kill spreads fire to nearby enemies
        Ashmark     = 44, // Enchantment — Damage: Sear brands and locks enemy morale
        AnchorWard  = 45, // Enchantment — Barrier warning zone slows enemies
        // ── New Lost Forms ─────────────────────────────────────────────────
        WardenRing  = 46, // Lost Form — circular barrier around caster
        Dirge       = 47, // Lost Form — burst becomes a ground fire patch
        PaleComet   = 48, // Lost Form — missile pierces enemies, detonates at range end
        // ── Altar Rites ──────────────────────────────────────────────────────────
        ColdTithe       = 49, // Rite — Soul Tithe: tier-based prisoner heal + bonus Cold
        DreadTide       = 50, // Rite — Dread Tide: all three tide effects + 5 HP surcharge
        ColdCovenant    = 51, // Rite — halved cooldowns; 1 Whisper; -5 HP invoke
        // ── Sanctuary Rites ───────────────────────────────────────────────────────
        KeepingFlame    = 52, // Rite — 25% wound heal + 20 morale on prayer; morale floor 30
        UnbrokenWard    = 53, // Rite — 21-day ward; +10 morale/day; -2 aging while active
        EmberCovenant   = 54, // Rite — 8 HP cost; double Grace; daily troop heal; +5 morale
        // ── Alchemy Rites ─────────────────────────────────────────────────────────
        SteadierHand    = 55, // Rite — +15% brew; no mislead; 20% double-brew on success
        DeeperSatchel   = 56, // Rite — +4 capacity; flat 150g; 25% field refill
        VolatileHarvest = 57, // Rite — 40% salvage; volatile burst; 30% harm reduction
        // ── Classes (talent packs — 2 FP each; bundle the older single talents) ──
        DarkMage        = 58, // Class — life-eater path
        Seer            = 59, // Class — foresight path
        BattleSworn     = 60, // Class — war-caster path
        WardKeeper      = 61, // Class — ward/shield path
        Heartfire       = 62, // Class — healer/support path
        Pyrelord        = 63, // Class — raw destruction path
        Ashbinder       = 64, // Class — unmaker (morale/control) path
        // ── Discipline classes (rite packs — 2 FP each; bundle the 9 rites) ──────
        Coldsworn       = 65, // Class — Ashen Altar (Cold) rites
        Gracebound      = 66, // Class — Sanctuary (Grace) rites
        AshenAlchemist  = 67, // Class — Alchemy rites
        // ── Nature — The Living Ember ────────────────────────────────────────
        NatureLivingRoot = 68, // Rite — charge capacity ×2; passive gain doubled
        NatureStillDraw  = 69, // Rite — no HP draw cost while stationary in combat
        NatureOpenGrip   = 70, // Rite — held charges do not expire
        Wildsworn        = 71, // Class — the living-ember path
        NatureDeepEarth  = 72, // Rite — siege/city draw cooldown removed
        NatureDawnCall   = 73, // Rite — passive charge accumulation fires every dawn
    }

    public enum TalentCategory { Passive, Enchantment, Spell, Info, LostForm, Rite, Class }

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
            // ── Classes (fire paths — escalating cost) ────────────────────────
            // Six fire paths define a mage's identity. Path cost escalates: 1 fp
            // for the first path owned, 2 fp for the second, and so on. BattleSworn
            // is kept in ClassMembers below for save compatibility but is no longer
            // purchasable (no TalentDef).
            new TalentDef
            {
                Id = TalentId.DarkMage, Category = TalentCategory.Class, FocusCost = 0,
                Name = "Reaper",
                Lore = "The fire has learned to feed on what dies. Each kill leaves a warmth behind — not theirs, not quite yours, but yours to take if you know how to hold a vessel for it. The raids and the executions are not cruelty. They are fuel.",
                MechanicDesc = "Path (cost scales: 1 fp first, 2 fp second, etc.). The way of the life-eater. Grants four talents: Ember (each battlefield kill carries a 10% chance to restore a day of youth), Reap (raids and executions restore years of your life), Wither (drain the hearth from a nearby enemy village), and Extinguish (wound and break a distant enemy party). Walking this path darkens you."
            },
            new TalentDef
            {
                Id = TalentId.Seer, Category = TalentCategory.Class, FocusCost = 0,
                Name = "Seer",
                Lore = "You read the fire the way a navigator reads stars — imperfectly, but well enough. The lines that bind every living thing are never quite still, and you have learned to look without flinching. What you see, you do not always share.",
                MechanicDesc = "Path (cost scales: 1 fp first, 2 fp second, etc.). The way of foresight. Grants three talents: Tempered (battle casts cost 25% fewer days, deepening with age), Clairvoyance (read the threads — turn insight into influence or gold), and Fade (draw your fire inward — conceal your party from enemy scouts for a day)."
            },
            new TalentDef
            {
                Id = TalentId.WardKeeper, Category = TalentCategory.Class, FocusCost = 0,
                Name = "Warden",
                Lore = "The fire that keeps things out is harder to learn than the fire that burns. You have stopped moving toward what threatens you. You have learned to stand still inside the flame and hold your shape while everything else changes around you.",
                MechanicDesc = "Path (cost scales: 1 fp first, 2 fp second, etc.). The way of the shield. Grants four talents: Ashveil (Restore grants brief magic immunity), Cinder Shell (Restore hardens allies and shields overhealed ones), Reflect (Restore retaliates against melee attackers), and The Warden's Ring (barrier nodes ring the caster instead of forming a wall)."
            },
            new TalentDef
            {
                Id = TalentId.Heartfire, Category = TalentCategory.Class, FocusCost = 0,
                Name = "Heartfire",
                Lore = "The fire that tends the living is rarer than the fire that takes. It does not announce itself with smoke. It simply reminds the dying that warmth was theirs all along. Those who carry fire recognise each other across a room — there is something almost like trust in that.",
                MechanicDesc = "Path (cost scales: 1 fp first, 2 fp second, etc.). The way of warmth. Grants four talents: Hearthlight (Restore lifts allied morale, +10 per input), Kinship (mage lords trust you; their presence in battle cuts your casting cost by 10% each, up to 50%), Kindle (a campaign working that heals wounded soldiers and rallies your party), and Dirge (burst sinks into the ground as a lingering fire patch)."
            },
            new TalentDef
            {
                Id = TalentId.Pyrelord, Category = TalentCategory.Class, FocusCost = 0,
                Name = "Pyrelord",
                Lore = "Fire taken to its furthest expression — where warmth becomes judgment. There is no shaping left at this reach, only the question of what is allowed to remain.",
                MechanicDesc = "Path (cost scales: 1 fp first, 2 fp second, etc.). The way of ruin. Grants four talents: Immolate (Sear kills outright — scales with inputs, guaranteed at 3+, deaths spread fire to nearby enemies), Scatter (Force hurls enemies back and breaks their stride), Sunder (Shred rends armour and saps attack power), and Ashstorm (call a firestorm down on a nearby enemy settlement)."
            },
            new TalentDef
            {
                Id = TalentId.Ashbinder, Category = TalentCategory.Class, FocusCost = 0,
                Name = "Ashbinder",
                Lore = "Fire need not kill to win. It hollows — strips the steel from a man's arm and the courage from his chest until nothing answers the call to fight. The body walks away. Whatever held it together does not.",
                MechanicDesc = "Path (cost scales: 1 fp first, 2 fp second, etc.). The way of the unmaker — fire that breaks resolve rather than bodies. Grants three talents: Smoulder (any Damage input scorches morale and bewilders non-heroes, routing them at random), Unsettle (shatter the morale of a nearby enemy party without touching them), and Resonance (your first campaign working each day costs nothing, with a 25% chance on later ones)."
            },
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
                MechanicDesc = "Enchantment. Any Damage input scorches enemy morale (−15 per input) and bewilders non-hero enemies with a random effect — instant rout, force charge, dismount, or morale fractured to 25%. Sear (W) inputs additionally seal the mark: branded enemies cannot recover morale for 30 seconds."
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
                MechanicDesc = "Enchantment. Amplifies Sear (W) inputs: additional burn damage scales with inputs. At 1 Sear: 33% chance to kill. At 2 Sear: 50% chance to kill. At 3+ Sear: guaranteed kills (Sear inputs / 3). Survivors are left burning (2 damage/s per Sear input, 3 seconds). When a kill lands, fire leaps to all enemies within 3m for 30% of the Sear damage. Without this talent, Sear gives a weak 5-per-input burn."
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
                MechanicDesc = "Enchantment. Restore hardens allies, reducing incoming damage. Protection = 6% per Restore input (max 30% at 5 inputs). Duration = 4s + 1s per Restore input. When an ally is above 90% health, excess fire adds a damage shield of 10 HP per Restore input for 5s. Your barrier nodes also warn — enemies entering their heat zone are slowed by 30% for 4 seconds."
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
                Category = TalentCategory.LostForm, FocusCost = 1, Name = "Widened Blast",
                Lore = "The fire does not ask how wide your arms can reach. It asks how wide your will can hold. You found a slightly different angle of release — not taught, not passed down, only survived. The cone opens. More earth scorched, fewer who dodge the edges.",
                MechanicDesc = "Lost Form. Blast cone widens from ~49° to ~60°. More enemies caught at the edge; the forward reach is unchanged."
            },
            new TalentDef
            {
                Id = TalentId.WardenRing, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.LostForm, FocusCost = 1, Name = "The Warden's Ring",
                Lore = "The old form of the barrier was always a wall — a line you drew between yourself and what was coming. But a wall has two ends. The older working was a ring: fire surrounding, not dividing. The form was nearly lost because the warding requires standing inside the fire.",
                MechanicDesc = "Lost Form. Barrier nodes form a complete ring around the caster instead of a wall in front. Each node is placed 2.5 metres from the caster at equal angles."
            },
            new TalentDef
            {
                Id = TalentId.Dirge, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.LostForm, FocusCost = 1, Name = "Dirge",
                Lore = "Most who carry the fire scatter it outward. The old form collapses it inward — downward, into the earth beneath the feet. It does not explode. It seeps. The ground smokes for a long time after. Anything that walks through it, walks through a working that has not finished yet.",
                MechanicDesc = "Lost Form. Burst drives fire into the ground rather than outward. A smouldering patch lingers for 12 seconds, burning enemies who walk through it."
            },
            new TalentDef
            {
                Id = TalentId.PaleComet, IsSpell = false, IsEnchantment = false,
                Category = TalentCategory.LostForm, FocusCost = 1, Name = "Pale Comet",
                Lore = "A bolt that does not stop at the first thing it finds. The fire passes through — not weakened, only saved for later. It finishes what it started at the far end of its reach. You do not see what it does until it is done.",
                MechanicDesc = "Lost Form. The missile passes through enemies rather than detonating on first contact. Each enemy it crosses is struck by the full cast. The bolt detonates only when its full range is spent."
            },
            // ── Discipline classes (rite packs — learned at their own sites) ──────
            // Each bundles a whole discipline's rites into one 2-point purchase,
            // mirroring the combat classes. Sold at the Sanctuary / Lab,
            // not in the grimoire (Category.Rite is skipped there).
            // Coldsworn and its rites (ColdTithe, DreadTide, ColdCovenant) are
            // retired — Dark Altars now grant permanent Dark Gifts instead.
            // TalentId values are kept for save compatibility.
            new TalentDef
            {
                Id = TalentId.Gracebound, Category = TalentCategory.Rite, FocusCost = 2, Name = "Gracebound",
                Lore = "Devotion worn smooth becomes a channel rather than a struggle. You no longer reach for the warmth — it is already moving through you, into the wounded beside you, into the ward overhead, into the courage of those who march in your shadow.",
                MechanicDesc = "Class (2 focus points). The full discipline of the Sanctuary. Grants The Keeping Flame (each prayer heals a quarter of your wounded and holds a morale floor of 30), Unbroken Ward (a 21-day warding seal that grants daily morale and cheaper battle casts while it holds), and Ember Covenant (cheaper prayer, double Grace, and a quiet daily heal while Grace runs high)."
            },
            new TalentDef
            {
                Id = TalentId.AshenAlchemist, Category = TalentCategory.Rite, FocusCost = 2, Name = "Ashen Alchemist",
                Lore = "The satchel was always larger than it looked, the brew always more willing than it seemed. A practised hand finishes confidently, carries more than it counts, and salvages what a clumsier one would spill into ruin.",
                MechanicDesc = "Class (2 focus points). The full discipline of the Lab. Grants The Steadier Hand (+15% brew success, no misleading reads, a chance of a second vial), The Deeper Satchel (+4 capacity, flat 150-denar brews, a chance the field vial refills), and Volatile Harvest (salvage tainted vials, lash a nearby enemy on the save, and soften backfires)."
            },
            // ── Sanctuary Rites ──────────────────────────────────────────────────
            new TalentDef
            {
                Id = TalentId.KeepingFlame, Category = TalentCategory.Rite, FocusCost = 1, Name = "The Keeping Flame",
                Lore = "To open yourself as a vessel is to open a channel wider than your own body. What the fire pours through you reaches the ones beside you — the wounded, the afraid, the ones whose fire was dimming. You cannot direct it; you can only stay open and trust the warmth to find what needs it most.",
                MechanicDesc = "Rite. Each prayer heals 25% of wounded troops and grants your column +20 morale from shared warmth. Daily, your party's morale cannot fall below 30 — the Keeping Flame holds a floor of courage in the ones who march beside you."
            },
            new TalentDef
            {
                Id = TalentId.UnbrokenWard, Category = TalentCategory.Rite, FocusCost = 1, Name = "Unbroken Ward",
                Lore = "The fuller form of the warding sinks deeper into the earth and the air, leaves less of a seam at the edges. The grey things find the seal and do not try the same approach twice. Meanwhile those who march beneath it feel a warmth they cannot name, and their courage does not drain as fast.",
                MechanicDesc = "Rite. The Warding Seal lasts 21 days instead of 14. While the ward holds: your party gains +10 morale each day, and each battle spell costs 2 fewer aging days (minimum 1). The ward makes the fire cheaper to spend while it burns."
            },
            new TalentDef
            {
                Id = TalentId.EmberCovenant, Category = TalentCategory.Rite, FocusCost = 1, Name = "Ember Covenant",
                Lore = "Devotion is its own fuel — the rite does not need to reach as deep when the channel has been worn smooth by repetition. And what you carry comes back to you in the breathing: the Grace you hold does not sit still, it moves, it circulates, it mends what it finds. Quietly. While you sleep.",
                MechanicDesc = "Rite. Prayer costs 8 HP instead of 12, and yields twice the Grace. While you carry any Grace: +5 morale each day. When Grace exceeds half your cap, the warmth quietly heals one wounded soldier per troop type each dawn."
            },
            // ── Alchemy Rites ─────────────────────────────────────────────────────
            new TalentDef
            {
                Id = TalentId.SteadierHand, Category = TalentCategory.Rite, FocusCost = 1, Name = "The Steadier Hand",
                Lore = "Most who spoil a brew do so in the final measure. You have learned to finish confidently — and the confident finish is its own catalyst. Sometimes the brew that should have been one becomes two, as though the formula agreed it was not done yet.",
                MechanicDesc = "Rite. Brewing success chance increases by 15%. Misleading read results are replaced with Unknown — the hand that seals it may doubt, but it will not lie. On a clean brew, 20% chance the formula yields a second vial at no additional cost."
            },
            new TalentDef
            {
                Id = TalentId.DeeperSatchel, Category = TalentCategory.Rite, FocusCost = 1, Name = "The Deeper Satchel",
                Lore = "The satchel was always larger than it looked. A practised hand arranges rather than counts — the vials nest, the heat distributes, the seams of the old leather know where to give. And sometimes, when the vial rises to the lips, the hand reaches back and finds it is somehow still there.",
                MechanicDesc = "Rite. Satchel capacity increases by 4. Ingredient costs drop to a flat 150 denars per brew. When you drink a clean elixir in the field, 25% chance the satchel refills with one clean vial of the same kind — you reach in and find it was not gone after all."
            },
            new TalentDef
            {
                Id = TalentId.VolatileHarvest, Category = TalentCategory.Rite, FocusCost = 1, Name = "Volatile Harvest",
                Lore = "A ruined brew is not always a waste. The instincts of a careful hand find what set true before the rest turned. It takes nerve to drink something that smells like failure. And when the good part surfaces unexpectedly, the volatile remainder does not vanish quietly — it needs somewhere to go.",
                MechanicDesc = "Rite. When a tainted vial would backfire: 40% chance to salvage it and yield the clean effect. In battle, the volatile remnant lashes the nearest enemy for 25 fire damage on a salvage. When a backfire does land, 30% of the self-wound is returned as a partial heal — not all of the ruin reaches you."
            },
            // ── Nature — The Living Ember ─────────────────────────────────────────
            new TalentDef
            {
                Id = TalentId.NatureLivingRoot, Category = TalentCategory.Rite, FocusCost = 1, Name = "Living Root",
                Lore = "The root-voice speaks twice when you know how to listen. The land does not give more than it is asked for — but a patient hand, reaching twice, finds what a hasty one misses.",
                MechanicDesc = "Rite. You may hold two elemental charges at once instead of one. Passive charge accumulation on the campaign map rolls twice each day."
            },
            new TalentDef
            {
                Id = TalentId.NatureStillDraw, Category = TalentCategory.Rite, FocusCost = 1, Name = "Still Draw",
                Lore = "The river does not charge you for what it offers when you are still. Motion is cost. Stillness is the oldest prayer the land knows.",
                MechanicDesc = "Rite. Drawing elemental charge in combat costs no HP when you are stationary. Move, and the land asks its price."
            },
            new TalentDef
            {
                Id = TalentId.NatureOpenGrip, Category = TalentCategory.Rite, FocusCost = 1, Name = "Open Grip",
                Lore = "The steppe does not take back a gift. What the land gives, it means you to have — for as long as you carry it. Open the hand, not wide, just open, and what the world gives you will stay.",
                MechanicDesc = "Rite. Held elemental charges no longer expire. What you draw in battle stays in your hands until you use it or leave the field."
            },
            new TalentDef
            {
                Id = TalentId.Wildsworn, Category = TalentCategory.Rite, FocusCost = 2, Name = "Wildsworn",
                Lore = "You have listened long enough that the listening has changed you. The root-voice, the river's patience, the open hand of the wind — you carry all three, and the land knows you for what you are.",
                MechanicDesc = "Class (2 focus points). The full discipline of The Living Ember. Grants Living Root (hold two charges), Still Draw (free draw while stationary), and Open Grip (charges never expire). A hermit who taught you one of these will not begrudge you the others."
            },
            new TalentDef
            {
                Id = TalentId.NatureDeepEarth, Category = TalentCategory.Rite, FocusCost = 1, Name = "Deep Earth",
                Lore = "Stone is slow to speak, but it does not stay silent. Those who wait long enough beside an old wall or a mountain face come to hear the root-voice through the rock as clearly as through open soil. The muffling is a failure of patience, not a failure of the land.",
                MechanicDesc = "Rite. Drawing elemental charge inside a siege or walled city no longer triggers the stone-muffling cooldown. The land speaks through stone as freely as through soil."
            },
            new TalentDef
            {
                Id = TalentId.NatureDawnCall, Category = TalentCategory.Rite, FocusCost = 1, Name = "Dawn Call",
                Lore = "The desert is loudest at dawn. Most people miss it. The angle of the light, the shift of the cold, the moment when dark ground releases what it held through the night — if you are open enough when it happens, the living world gives without being asked.",
                MechanicDesc = "Rite. The land offers an elemental charge each dawn without fail. The 1-in-3 chance of passive accumulation on the campaign map becomes a certainty."
            },
        };

        // ── Class membership ───────────────────────────────────────────────────
        // Each class bundles the older single talents it replaces. Owning the class
        // satisfies Has() for every member (see TalentSystem.Player.cs). Only live
        // talents (those with a TalentDef) are bundled — the consolidated-out forms
        // (Scorch, Chain Ignite, Ashmark, Anchor Ward, Twin Bolts, Lost Burst, Lost
        // Barrier) are deliberately left out, not silently revived.
        public static readonly IReadOnlyDictionary<TalentId, TalentId[]> ClassMembers =
            new Dictionary<TalentId, TalentId[]>
            {
                // ── Six fire paths (purchasable, escalating cost) ─────────────────
                [TalentId.DarkMage]       = new[] { TalentId.Ember, TalentId.Reap, TalentId.Plague, TalentId.Extinguish },
                [TalentId.Seer]           = new[] { TalentId.BattleMage, TalentId.Clairvoyance, TalentId.Fade },
                [TalentId.WardKeeper]     = new[] { TalentId.Ashveil, TalentId.CinderShell, TalentId.Reflect, TalentId.WardenRing },
                [TalentId.Heartfire]      = new[] { TalentId.Hearthlight, TalentId.Camaraderie, TalentId.Inspire, TalentId.Dirge },
                [TalentId.Pyrelord]       = new[] { TalentId.Immolate, TalentId.Scatter, TalentId.Sunder, TalentId.Ashstorm },
                [TalentId.Ashbinder]      = new[] { TalentId.Smoulder, TalentId.BreakWills, TalentId.Sorcerer },
                // ── Legacy (no TalentDef — kept for save compatibility only) ──────
                [TalentId.BattleSworn]    = new[] { TalentId.ArmedCasting, TalentId.Flashfire, TalentId.PaleComet, TalentId.LostBlast },
                // ── Discipline classes (purchased at their ritual sites) ───────────
                // Coldsworn retired — Dark Altars now grant Dark Gifts directly.
                // [TalentId.Coldsworn] intentionally absent.
                [TalentId.Gracebound]     = new[] { TalentId.KeepingFlame, TalentId.UnbrokenWard, TalentId.EmberCovenant },
                [TalentId.AshenAlchemist] = new[] { TalentId.SteadierHand, TalentId.DeeperSatchel, TalentId.VolatileHarvest },
                [TalentId.Wildsworn]      = new[] { TalentId.NatureLivingRoot, TalentId.NatureStillDraw, TalentId.NatureOpenGrip },
            };

        private static readonly Dictionary<TalentId, TalentId> _memberToClass = BuildMemberToClass();
        private static Dictionary<TalentId, TalentId> BuildMemberToClass()
        {
            var map = new Dictionary<TalentId, TalentId>();
            foreach (var kv in ClassMembers)
                foreach (var member in kv.Value)
                    map[member] = kv.Key;
            return map;
        }

        /// <summary>True if the id is a class (bundles other talents).</summary>
        public static bool IsClass(TalentId id) => ClassMembers.ContainsKey(id);

        /// <summary>The class that grants this member talent, if any.</summary>
        public static bool TryGetOwningClass(TalentId member, out TalentId owningClass) =>
            _memberToClass.TryGetValue(member, out owningClass);

    }


    internal static class MobilePartyExtensions
    {
        public static void Let(this MobileParty p, Action<MobileParty> action)
        {
            if (p != null) action(p);
        }
    }
}
