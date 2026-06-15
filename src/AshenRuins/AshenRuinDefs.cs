// =============================================================================
// ASH AND EMBER — AshenRuins/AshenRuinDefs.cs
// Compile-time definitions for all 28 Ashen Ruins:
// locations (matched by village name), challenge sequences, and rewards.
// =============================================================================

namespace AshAndEmber
{
    // ── Challenge types ───────────────────────────────────────────────────────
    public enum ChallengeType
    {
        // Standard — can retreat
        CollapsingChamber,  // roll vs party size (solo: auto-fail → pay aging or retreat)
        SpectralGuardian,   // roll vs aging days spent
        VoidWhisper,        // choice: surge (+10 whisper) or resist (roll → aging)
        RiddleGate,         // 3-clue puzzle; 2/3 correct or pay 2 aging to auto-pass
        AshenSentinel,      // Ashen passes free; light mage rolls vs Ashen spell count
        SerpentNest,        // troops or aging — solo forces aging ×1.5
        PoisonedAir,        // roll vs inner fire proficiency
        CursedRelics,       // pick 1 of 3; 1 real, 2 cursed

        // Mandatory — no retreat, cost always paid
        BloodLock,          // 3 aging days, entry toll
        VoidMaw,            // 6 aging days, no retreat
        SoulHarvest,        // 15 aging days
        AshenFlame,         // 10 troops + 6 aging (solo: aging ×1.5 only)
        AncientTrap,        // 60% pass; fail = 5 aging, no option
        SealedMemory,       // always grants vision, always +8 whisper

        // Special — unique mechanics
        MirrorGate,         // rolls against player's own spell cast count
        TemporalCrack,      // random outcome table
        SleepingGiant,      // stealth vs party size (solo = auto-pass)
        NecromanticWard,    // friendly fire; solo = +6 whisper instead
        DragonEgg,          // lore trigger; taking it marks player for Ashen attention
        VisionChamber,      // always passable, narrative only
    }

    // ── Reward types ──────────────────────────────────────────────────────────
    public enum RewardType
    {
        GrimoireFragment,   // grants a random un-owned Lost Form (or 2 focus pts if all owned)
        AgingReclaim,       // AgingSystem.RejuvenateHero(hero, Days)
        WhisperPurge,       // MageKnowledge.RemoveWhispers(Points)
        WhisperBrand,       // MageKnowledge.AddWhispers(Points)
        ReagentCache,       // ReagentSystem.Add(ReagentType, Qty)
        FocusPoints,        // HeroDeveloper.UnspentFocusPoints += Points
        RenownBurst,        // Clan.Renown += Points
        LoreVision,         // deferred inquiry with lore text
        DragonArtifact,     // The Eye of Aenos — sets AshenRuinSystem.EyeFound
        AshenCrownFragment, // collect 3 → bonus 3 focus pts
        VoidCrystal,        // player chooses: 5000 gold or 20 days reclaimed
        AncientGrimoire,    // grants ALL remaining un-owned Lost Forms
    }

    public class RuinChallenge
    {
        public ChallengeType Type;
        // optional extra data
        public int          IntParam;    // aging cost override, troop cost, etc.
        public string       StrParam;    // flavor text variant key
    }

    public class RuinReward
    {
        public RewardType Type;
        public int        Points;        // aging days / whispers / renown / fp
        public string     ReagentType;   // for ReagentCache
        public int        ReagentQty;
        public string     LoreText;      // for LoreVision
    }

    // ── Danger tier ───────────────────────────────────────────────────────────
    public enum RuinTier { Easy = 1, Standard = 2, Brutal = 3, Legendary = 4 }

    public class RuinDef
    {
        public string         VillageName;    // matched at session launch
        public string         RuinName;       // displayed to player
        public string         EntryLore;      // shown before entering
        public RuinTier       Tier;
        public RuinChallenge[] Challenges;
        public RuinReward     MainReward;     // full-clear reward
        public RuinReward     PartialReward;  // reward after ≥1 room, then retreat
    }

    public static class AshenRuinDefs
    {
        private static RuinChallenge Ch(ChallengeType t, int p = 0) =>
            new RuinChallenge { Type = t, IntParam = p };

        private static RuinReward Rew(RewardType t, int pts = 0, string rt = null, int rq = 0, string lore = null) =>
            new RuinReward { Type = t, Points = pts, ReagentType = rt, ReagentQty = rq, LoreText = lore };

        public static readonly RuinDef[] All = new[]
        {
            // ── Tier 1 — Two rooms, survivable ────────────────────────────────

            new RuinDef
            {
                VillageName = "Husn Fulq",
                RuinName  = "The Pale Chapel",
                EntryLore = "A church built inside a ruin, or a ruin built inside a church — you cannot tell which came first. The mortar smells of ash.",
                Tier      = RuinTier.Easy,
                Challenges = new[] { Ch(ChallengeType.VisionChamber), Ch(ChallengeType.AncientTrap) },
                MainReward    = Rew(RewardType.FocusPoints, 1),
                PartialReward = Rew(RewardType.RenownBurst, 20),
            },
            new RuinDef
            {
                VillageName = "Askar",
                RuinName  = "The Ash Garden",
                EntryLore = "A terraced garden, long dead. The soil is grey and fine as flour. Something tended this place until very recently.",
                Tier      = RuinTier.Easy,
                Challenges = new[] { Ch(ChallengeType.BloodLock), Ch(ChallengeType.VisionChamber) },
                MainReward    = Rew(RewardType.ReagentCache, 0, ReagentSystem.FrozenAmber, 1),
                PartialReward = Rew(RewardType.RenownBurst, 10),
            },
            new RuinDef
            {
                VillageName = "Vatcheva",
                RuinName  = "The Drowned Altar",
                EntryLore = "A low cave, ankle-deep in dark water. The altar is submerged. You must kneel to reach it — or choose not to.",
                Tier      = RuinTier.Easy,
                Challenges = new[] { Ch(ChallengeType.VoidWhisper), Ch(ChallengeType.VisionChamber) },
                MainReward    = Rew(RewardType.WhisperPurge, 15),
                PartialReward = Rew(RewardType.RenownBurst, 15),
            },
            new RuinDef
            {
                VillageName = "Amikle",
                RuinName  = "The Fractured Beacon",
                EntryLore = "A lighthouse with no sea in sight. The lens at the top has shattered into a hundred small mirrors, each one reflecting a different light.",
                Tier      = RuinTier.Easy,
                Challenges = new[] { Ch(ChallengeType.CollapsingChamber), Ch(ChallengeType.VisionChamber) },
                MainReward    = Rew(RewardType.AgingReclaim, 5),
                PartialReward = Rew(RewardType.RenownBurst, 20),
            },
            new RuinDef
            {
                VillageName = "Ocs Hall",
                RuinName  = "The Glass Scriptorium",
                EntryLore = "Walls of fused sand — someone fired this building from within. The books inside are legible through the glass, but cannot be touched.",
                Tier      = RuinTier.Easy,
                Challenges = new[] { Ch(ChallengeType.RiddleGate), Ch(ChallengeType.VisionChamber) },
                MainReward    = Rew(RewardType.FocusPoints, 1),
                PartialReward = Rew(RewardType.RenownBurst, 25),
            },

            // ── Tier 2 — Three rooms, standard ────────────────────────────────

            new RuinDef
            {
                VillageName = "Dravend",
                RuinName  = "The Sunken Scriptorium",
                EntryLore = "Below the root-cellar of an abandoned hall, stairs lead down into a chamber that should not exist. The air is older here.",
                Tier      = RuinTier.Standard,
                Challenges = new[] { Ch(ChallengeType.AncientTrap), Ch(ChallengeType.RiddleGate), Ch(ChallengeType.VisionChamber) },
                MainReward    = Rew(RewardType.GrimoireFragment),
                PartialReward = Rew(RewardType.FocusPoints, 1),
            },
            new RuinDef
            {
                VillageName = "Sahra",
                RuinName  = "The Ember Vault",
                EntryLore = "The sand here is darkened in a perfect circle, thirty feet wide. Below the circle, hollow.",
                Tier      = RuinTier.Standard,
                Challenges = new[] { Ch(ChallengeType.SerpentNest), Ch(ChallengeType.AncientTrap), Ch(ChallengeType.AshenSentinel) },
                MainReward    = Rew(RewardType.ReagentCache, 0, ReagentSystem.BrimstoneAsh, 3),
                PartialReward = Rew(RewardType.ReagentCache, 0, ReagentSystem.BrimstoneAsh, 1),
            },
            new RuinDef
            {
                VillageName = "Epis",
                RuinName  = "The Shattered Throne",
                EntryLore = "A subterranean throne room. The throne has been struck by something that came from inside the stone, not from any door.",
                Tier      = RuinTier.Standard,
                Challenges = new[] { Ch(ChallengeType.CollapsingChamber), Ch(ChallengeType.RiddleGate), Ch(ChallengeType.SpectralGuardian) },
                MainReward    = Rew(RewardType.FocusPoints, 2),
                PartialReward = Rew(RewardType.FocusPoints, 1),
            },
            new RuinDef
            {
                VillageName = "Bastan",
                RuinName  = "The Wound in the Earth",
                EntryLore = "A vertical shaft in a hillside, six feet wide. The walls are smooth and warm to the touch. No one dug this.",
                Tier      = RuinTier.Standard,
                Challenges = new[] { Ch(ChallengeType.AncientTrap), Ch(ChallengeType.CollapsingChamber), Ch(ChallengeType.BloodLock) },
                MainReward    = Rew(RewardType.GrimoireFragment),
                PartialReward = Rew(RewardType.AgingReclaim, 3),
            },
            new RuinDef
            {
                VillageName = "Shariz",
                RuinName  = "The Obsidian Lectern",
                EntryLore = "A single piece of black stone, shaped for a book, with no building around it — just desert and a smell like a spent match.",
                Tier      = RuinTier.Standard,
                Challenges = new[] { Ch(ChallengeType.RiddleGate), Ch(ChallengeType.VoidWhisper), Ch(ChallengeType.BloodLock) },
                MainReward    = Rew(RewardType.GrimoireFragment),
                PartialReward = Rew(RewardType.FocusPoints, 1),
            },
            new RuinDef
            {
                VillageName = "Nemos",
                RuinName  = "The Veiled Sanctum",
                EntryLore = "A cave behind a waterfall that should not exist this far from any river. Inside, the cave narrows to a room carved with intent.",
                Tier      = RuinTier.Standard,
                Challenges = new[] { Ch(ChallengeType.BloodLock), Ch(ChallengeType.AshenSentinel), Ch(ChallengeType.SpectralGuardian) },
                MainReward    = Rew(RewardType.ReagentCache, 0, ReagentSystem.FrozenAmber, 2),
                PartialReward = Rew(RewardType.ReagentCache, 0, ReagentSystem.FrozenAmber, 1),
            },
            new RuinDef
            {
                VillageName = "Zupan",
                RuinName  = "The Crimson Repository",
                EntryLore = "Every surface is stained red — not rust, not paint. The shelves still hold jars. None of them are sealed.",
                Tier      = RuinTier.Standard,
                Challenges = new[] { Ch(ChallengeType.BloodLock), Ch(ChallengeType.RiddleGate), Ch(ChallengeType.SpectralGuardian) },
                MainReward    = Rew(RewardType.GrimoireFragment),
                PartialReward = Rew(RewardType.FocusPoints, 1),
            },
            new RuinDef
            {
                VillageName = "Armun",
                RuinName  = "The Serpent Gallery",
                EntryLore = "A tidal cave accessible only at low tide. The walls are carved with serpents. They are not decorative.",
                Tier      = RuinTier.Standard,
                Challenges = new[] { Ch(ChallengeType.SerpentNest), Ch(ChallengeType.AncientTrap), Ch(ChallengeType.VisionChamber) },
                MainReward    = Rew(RewardType.ReagentCache, 0, ReagentSystem.SeaSerpentScale, 2),
                PartialReward = Rew(RewardType.ReagentCache, 0, ReagentSystem.SeaSerpentScale, 1),
            },
            new RuinDef
            {
                VillageName = "Gruntio",
                RuinName  = "The Blind Oracle's Rest",
                EntryLore = "A farmhouse built directly over a much older structure. The farmer left recently — the hearth is still warm.",
                Tier      = RuinTier.Standard,
                Challenges = new[] { Ch(ChallengeType.VoidWhisper), Ch(ChallengeType.RiddleGate), Ch(ChallengeType.VisionChamber) },
                MainReward    = Rew(RewardType.LoreVision, 0, null, 0, "The oracle left a single phrase carved into the floor: 'The fire does not choose who carries it. It simply burns.' Below that, in a different hand: 'It lies.' You are unsettled for days afterward."),
                PartialReward = Rew(RewardType.WhisperPurge, 10),
            },
            new RuinDef
            {
                VillageName = "Boivin",
                RuinName  = "The Cursed Reliquary",
                EntryLore = "A chapel that was locked from the inside, from the inside. You are not sure how that is possible. Everything smells faintly of copper.",
                Tier      = RuinTier.Standard,
                Challenges = new[] { Ch(ChallengeType.CursedRelics), Ch(ChallengeType.SpectralGuardian), Ch(ChallengeType.AncientTrap) },
                MainReward    = Rew(RewardType.WhisperBrand, 25),
                PartialReward = Rew(RewardType.FocusPoints, 1),
            },

            // ── Tier 3 — Three/four rooms, brutal ─────────────────────────────

            new RuinDef
            {
                VillageName = "Tamnuh",
                RuinName  = "The Ashen Crypt",
                EntryLore = "The crypt is unmarked. No names, no dates. Only handprints on the walls in ash — dozens of them — all facing inward.",
                Tier      = RuinTier.Brutal,
                Challenges = new[] { Ch(ChallengeType.AshenSentinel), Ch(ChallengeType.SoulHarvest), Ch(ChallengeType.BloodLock) },
                MainReward    = Rew(RewardType.AgingReclaim, 15),
                PartialReward = Rew(RewardType.AgingReclaim, 5),
            },
            new RuinDef
            {
                VillageName = "Alebat",
                RuinName  = "The Frozen Reliquary",
                EntryLore = "A cave in the northern ice. The cold here is different — purposeful. Everything inside is perfectly preserved.",
                Tier      = RuinTier.Brutal,
                Challenges = new[] { Ch(ChallengeType.CollapsingChamber), Ch(ChallengeType.SpectralGuardian), Ch(ChallengeType.SleepingGiant) },
                MainReward    = Rew(RewardType.AgingReclaim, 10),
                PartialReward = Rew(RewardType.RenownBurst, 60),
            },
            new RuinDef
            {
                VillageName = "Syratos",
                RuinName  = "The Bone Labyrinth",
                EntryLore = "A labyrinth of mortared bone. The bones are not scattered — they are load-bearing. Someone built with them.",
                Tier      = RuinTier.Brutal,
                Challenges = new[] { Ch(ChallengeType.AncientTrap), Ch(ChallengeType.AncientTrap), Ch(ChallengeType.SerpentNest), Ch(ChallengeType.RiddleGate) },
                MainReward    = Rew(RewardType.RenownBurst, 200),
                PartialReward = Rew(RewardType.RenownBurst, 60),
            },
            new RuinDef
            {
                VillageName = "Khulbuk",
                RuinName  = "The Hungering Pit",
                EntryLore = "A pit ten feet wide and fifty feet deep. At the bottom, a sealed door. The pit does not look dug. It looks opened.",
                Tier      = RuinTier.Brutal,
                Challenges = new[] { Ch(ChallengeType.VoidWhisper), Ch(ChallengeType.SealedMemory), Ch(ChallengeType.VisionChamber) },
                MainReward    = Rew(RewardType.LoreVision, 0, null, 0,
                    "The vision is not one of fire. It is one of cold — a calm, grey light spreading from horizon to horizon while the world holds very still. You wake with the sense that someone just looked at you through a keyhole. And then the sensation of warmth that follows, as if the fire inside you pressed back."),
                PartialReward = Rew(RewardType.WhisperBrand, 8),
            },
            new RuinDef
            {
                VillageName = "Stinkor",
                RuinName  = "The Temporal Cistern",
                EntryLore = "A cistern that is never the same depth twice. You suspect time behaves oddly here. The locals agree and avoid it.",
                Tier      = RuinTier.Brutal,
                Challenges = new[] { Ch(ChallengeType.TemporalCrack), Ch(ChallengeType.BloodLock), Ch(ChallengeType.VisionChamber) },
                MainReward    = Rew(RewardType.AgingReclaim, 8),  // resolved dynamically
                PartialReward = Rew(RewardType.FocusPoints, 1),
            },
            new RuinDef
            {
                VillageName = "Pen Cannoc",
                RuinName  = "The Necromancer's Throne",
                EntryLore = "The throne room of someone who collected things. Specifically: things that used to be alive. Very specifically: things that still want to be.",
                Tier      = RuinTier.Brutal,
                Challenges = new[] { Ch(ChallengeType.NecromanticWard), Ch(ChallengeType.SpectralGuardian), Ch(ChallengeType.MirrorGate) },
                MainReward    = Rew(RewardType.FocusPoints, 3),
                PartialReward = Rew(RewardType.FocusPoints, 1),
            },
            new RuinDef
            {
                VillageName = "Jalmarys",
                RuinName  = "The Sunless Archive",
                EntryLore = "The archive was built deep enough that no daylight has ever touched the manuscripts. It smells of ink, candle wax, and something sweeter beneath both.",
                Tier      = RuinTier.Brutal,
                Challenges = new[] { Ch(ChallengeType.RiddleGate), Ch(ChallengeType.BloodLock), Ch(ChallengeType.SpectralGuardian), Ch(ChallengeType.VisionChamber) },
                MainReward    = Rew(RewardType.GrimoireFragment),
                PartialReward = Rew(RewardType.FocusPoints, 1),
            },
            new RuinDef
            {
                VillageName = "Baltib",
                RuinName  = "The Carrion Halls",
                EntryLore = "A feasting hall where something ate. The bones at the long table are arranged with a kind of terrible care — each one exactly where a plate should be.",
                Tier      = RuinTier.Brutal,
                Challenges = new[] { Ch(ChallengeType.AshenSentinel), Ch(ChallengeType.AshenFlame), Ch(ChallengeType.SerpentNest) },
                MainReward    = Rew(RewardType.AshenCrownFragment),
                PartialReward = Rew(RewardType.WhisperBrand, 10),
            },
            new RuinDef
            {
                VillageName = "Pen Dolen",
                RuinName  = "The Ashen Cathedral",
                EntryLore = "Not a cathedral of a god you recognise. The iconography is wrong — fire depicted as the thing that is feared, not worshipped.",
                Tier      = RuinTier.Brutal,
                Challenges = new[] { Ch(ChallengeType.AshenSentinel), Ch(ChallengeType.SoulHarvest), Ch(ChallengeType.DragonEgg) },
                MainReward    = Rew(RewardType.AshenCrownFragment),
                PartialReward = Rew(RewardType.WhisperBrand, 15),
            },

            // ── Tier 4 — Legendary, no mercy ──────────────────────────────────

            new RuinDef
            {
                VillageName = "Myzea",
                RuinName  = "The Dragon's Tomb",
                EntryLore = "A cliff-face with a door that has no hinges and no handle. It opens anyway when you press your fire against it. The warmth that answers is not yours.",
                Tier      = RuinTier.Legendary,
                Challenges = new[]
                {
                    Ch(ChallengeType.AshenFlame),
                    Ch(ChallengeType.MirrorGate),
                    Ch(ChallengeType.VoidMaw),
                    Ch(ChallengeType.SealedMemory),
                },
                MainReward    = Rew(RewardType.DragonArtifact),
                PartialReward = Rew(RewardType.AgingReclaim, 8),
            },
            new RuinDef
            {
                VillageName = "Tilimsal",
                RuinName  = "The Heart of the Void",
                EntryLore = "The local name for this place translates as 'where breathing stops.' Livestock will not graze within three miles of it.",
                Tier      = RuinTier.Legendary,
                Challenges = new[]
                {
                    Ch(ChallengeType.SoulHarvest),
                    Ch(ChallengeType.VoidMaw),
                    Ch(ChallengeType.TemporalCrack),
                    Ch(ChallengeType.SealedMemory),
                },
                MainReward    = Rew(RewardType.AncientGrimoire),
                PartialReward = Rew(RewardType.FocusPoints, 2),
            },
            new RuinDef
            {
                VillageName = "Ronneld",
                RuinName  = "The Binding Dark",
                EntryLore = "A building that has no interior angles. The rooms curve. The floors tilt inward. You feel your inner fire lean away from the walls as you enter.",
                Tier      = RuinTier.Legendary,
                Challenges = new[]
                {
                    Ch(ChallengeType.SoulHarvest),
                    Ch(ChallengeType.NecromanticWard),
                    Ch(ChallengeType.MirrorGate),
                    Ch(ChallengeType.VoidMaw),
                },
                MainReward    = Rew(RewardType.AncientGrimoire),
                PartialReward = Rew(RewardType.AgingReclaim, 6),
            },
            new RuinDef
            {
                VillageName = "Dzerenava",
                RuinName  = "The Sunken Reliquary",
                EntryLore = "At low tide a staircase is exposed, leading down into the sea. At the bottom, the water does not enter. You are not sure why.",
                Tier      = RuinTier.Legendary,
                Challenges = new[]
                {
                    Ch(ChallengeType.SerpentNest),
                    Ch(ChallengeType.SleepingGiant),
                    Ch(ChallengeType.CursedRelics),
                    Ch(ChallengeType.BloodLock),
                },
                MainReward    = Rew(RewardType.AshenCrownFragment),
                PartialReward = Rew(RewardType.ReagentCache, 0, ReagentSystem.SeaSerpentScale, 1),
            },
        };
    }
}
