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
        ColdTithe       = 49, // Rite — prisoner sacrifice heals caster
        DreadTide       = 50, // Rite — Dark Tide wounds twice as many soldiers
        ColdCovenant    = 51, // Rite — Altar accumulates 1 Whisper instead of 3
        // ── Sanctuary Rites ───────────────────────────────────────────────────────
        KeepingFlame    = 52, // Rite — prayer heals 10% wounded troops
        UnbrokenWard    = 53, // Rite — Warding Seal lasts 21 days
        EmberCovenant   = 54, // Rite — prayer costs 8 HP; Grace grants +5 morale/day
        // ── Alchemy Rites ─────────────────────────────────────────────────────────
        SteadierHand    = 55, // Rite — +15% brew chance; no misleading reads
        DeeperSatchel   = 56, // Rite — +2 satchel capacity; 20% cheap brew
        VolatileHarvest = 57, // Rite — 40% chance to salvage tainted vials
    }

    public enum TalentCategory { Passive, Enchantment, Spell, Info, LostForm, Rite }

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
            // ── Altar Rites ──────────────────────────────────────────────────────
            new TalentDef
            {
                Id = TalentId.ColdTithe, Category = TalentCategory.Rite, Name = "Cold Tithe",
                Lore = "The cold takes, but it is not without memory. A life given freely — not stolen, not borrowed — leaves a trace of warmth in the giving. The stone returns what it can.",
                MechanicDesc = "Rite. When a prisoner is offered at the Altar, the sacrifice also heals you for 5 HP. The cold repays a small debt."
            },
            new TalentDef
            {
                Id = TalentId.DreadTide, Category = TalentCategory.Rite, Name = "Dread Tide",
                Lore = "What the altar calls does not stay near the stone. The grey hunger travels, and it does not stop when it has taken enough. It stops when it has taken what it came for.",
                MechanicDesc = "Rite. Invoke the Dark Tide wounds twice as many soldiers in nearby enemy forces."
            },
            new TalentDef
            {
                Id = TalentId.ColdCovenant, Category = TalentCategory.Rite, Name = "Cold Covenant",
                Lore = "The stone does not need your suffering — it needs your understanding. An offering given in knowing is worth more than one given in desperation. The cold is patient, and it teaches patience.",
                MechanicDesc = "Rite. The Altar accumulates only 1 Whisper per rite instead of 3. The cold leaves fewer marks on your standing."
            },
            // ── Sanctuary Rites ──────────────────────────────────────────────────
            new TalentDef
            {
                Id = TalentId.KeepingFlame, Category = TalentCategory.Rite, Name = "The Keeping Flame",
                Lore = "The fire does not distinguish between the vessel that tends it and the vessels that warm beside it. To pray is to open a channel; what pours through does not always stop at the one who opened it.",
                MechanicDesc = "Rite. Each prayer at the Sanctuary also heals 10% of wounded troops in your party. The warmth spreads."
            },
            new TalentDef
            {
                Id = TalentId.UnbrokenWard, Category = TalentCategory.Rite, Name = "Unbroken Ward",
                Lore = "A seal drawn in haste does not hold as long as one drawn with knowledge. The priest teaches you the fuller form. The grey things will not find the edges of it as quickly.",
                MechanicDesc = "Rite. The Warding Seal lasts 21 days instead of 14."
            },
            new TalentDef
            {
                Id = TalentId.EmberCovenant, Category = TalentCategory.Rite, Name = "Ember Covenant",
                Lore = "The flame asks less of those who carry it carefully. Devotion is its own fuel — the rite does not need to burn as deep to find the same warmth.",
                MechanicDesc = "Rite. Prayer at the Sanctuary costs 8 HP instead of 12. While you carry any Grace, your party gains +5 morale each day from the warmth you hold."
            },
            // ── Alchemy Rites ─────────────────────────────────────────────────────
            new TalentDef
            {
                Id = TalentId.SteadierHand, Category = TalentCategory.Rite, Name = "The Steadier Hand",
                Lore = "Most who spoil a brew do so in the final measure — the pour, the seal, the moment between intent and completion. You have learned to finish cleanly.",
                MechanicDesc = "Rite. Brewing success chance increases by 15%. Misleading read results are replaced with Unknown at worst — the hand that seals it may doubt, but it will not lie."
            },
            new TalentDef
            {
                Id = TalentId.DeeperSatchel, Category = TalentCategory.Rite, Name = "The Deeper Satchel",
                Lore = "The satchel was always larger than it looked. A practised hand understands the arrangement — what nests against what, which vials share heat and which do not. More can be carried than the count suggests.",
                MechanicDesc = "Rite. Satchel capacity increases by 2. Each brew has a 20% chance to cost only 100 denars as the ingredients combine without waste."
            },
            new TalentDef
            {
                Id = TalentId.VolatileHarvest, Category = TalentCategory.Rite, Name = "Volatile Harvest",
                Lore = "A ruined brew is not always a waste. The instincts of a careful hand can find what is still good inside what went wrong — the part that set true before the rest turned. It takes nerve to drink something that smells like failure.",
                MechanicDesc = "Rite. When a tainted vial would backfire: 40% chance to salvage it and yield the elixir's clean effect instead."
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
