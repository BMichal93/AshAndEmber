// =============================================================================
// THE WITHERING ART — MagicSystem.cs
// Mount & Blade II: Bannerlord Mod
// Target: .NET Framework 4.7.2
//
// COMPILATION NOTE:
//   Set BANNERLORD_PATH env var to your game folder before building, e.g.
//   BANNERLORD_PATH = C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord
//
// TODO MARKERS:
//   Several effects are marked TODO where the native API requires deeper research
//   (e.g. true invisibility, missile slowdown, gate destruction).
//   Those spells still apply their age cost and display flavour text — they are
//   placeholders, not crashes.
// !! TODO Refactor spells to different classes; remove unused parts of code !!
// !! TODO: Using magic during tournament should disqualify the caster immediately
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
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using TaleWorlds.CampaignSystem.MapEvents;

namespace TheWitheringArt
{
    // =========================================================================
    // 1. MODULE ENTRY POINT
    // =========================================================================
    public class MainSubModule : MBSubModuleBase
    {
        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            if (game.GameType is Campaign &&
                gameStarterObject is CampaignGameStarter campaignStarter)
                campaignStarter.AddBehavior(new MagicCampaignBehavior());
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            mission.AddMissionBehavior(new MagicMissionBehavior());
        }

        // Fires every frame on the Campaign Map (no active Mission)
        protected override void OnApplicationTick(float dt)
        {
            if (Campaign.Current == null || Mission.Current != null) return;
            MagicInputHandler.Tick(inMission: false);
            ActiveEffectManager.MapTick(dt);
            SpellEffects.TickAuraOfHate();
        }
    }

    // =========================================================================
    // 1b. MISSION BEHAVIOR  —  per-frame tick inside battles/towns/sieges
    // =========================================================================
    public class MagicMissionBehavior : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType =>
            MissionBehaviorType.Other;

        private bool _mageFightChecked  = false;
        private bool _alone10Checked    = false;

        public override void OnMissionTick(float dt)
        {
            MagicInputHandler.Tick(inMission: true);
            ActiveEffectManager.MissionTick(dt);
            MageLordAI.MissionTick(dt);
            MageUnitManager.MissionTick(dt);
            SpellEffects.TickSuppress(dt);
            SpellEffects.TickGlows(dt);

            if (Agent.Main != null && Agent.Main.IsActive() &&
                Hero.MainHero != null && Hero.MainHero.Age > 120f)
            {
                Agent.Main.Health = 0f;
                InformationManager.DisplayMessage(new InformationMessage(
                    "Your body cannot carry these years any further. The withering is complete.",
                    Color.FromUint(0xFFCC0000)));
            }

            if (!SpellKnowledge.HasGift || Mission.Current == null) return;

            // Detect mage fight (once per mission)
            if (!_mageFightChecked)
            {
                try
                {
                    bool hasMageEnemy = Mission.Current.Agents.Any(a =>
                        a.IsActive() && !a.IsMount &&
                        a.Team != null && Agent.Main != null &&
                        a.Team != Agent.Main.Team &&
                        (MageLordRegistry.IsMageLord((a.Character as CharacterObject)?.HeroObject) ||
                         (a.Character?.StringId?.StartsWith("twa_mage_") ?? false)));
                    if (hasMageEnemy)
                    {
                        _mageFightChecked = true;
                        SpellKnowledge.TriggerFoughtMage();
                    }
                }
                catch { _mageFightChecked = true; }
            }

            // Detect alone vs 10+ (once per mission, check early ticks)
            if (!_alone10Checked)
            {
                try
                {
                    if (Agent.Main != null && Mission.Current.Agents.Count > 2)
                    {
                        _alone10Checked = true;
                        bool hasAlly = Mission.Current.Agents.Any(a =>
                            a != Agent.Main && !a.IsMount && a.IsActive() &&
                            a.Team == Agent.Main.Team);
                        int enemyCount = Mission.Current.Agents.Count(a =>
                            !a.IsMount && a.IsActive() &&
                            Agent.Main != null && a.Team != Agent.Main.Team);
                        if (!hasAlly && enemyCount >= 10)
                            SpellKnowledge.TriggerFoughtAlone10();
                    }
                }
                catch { _alone10Checked = true; }
            }
        }
    }

    // =========================================================================
    // 2. SPELL DATABASE  —  single source of truth for all 28 spells
    // =========================================================================
    public enum SpellContext   { Mission, Map, Both }
    // Combat = red offensive magic, Healing = green support/healing, Support = blue control/manipulation
    public enum SpellGlowColor { Combat, Healing, Support }
    public enum LearnHow       { Starting, Companion, Event, Travel, MageLord, Condition, Personality }

    public class SpellEntry
    {
        public string        Name;
        public string        Combo;
        public int           DayCost;
        public string        BookTag;
        public SpellContext  Context;
        public SpellGlowColor GlowColor;
        public LearnHow      LearnHow    = LearnHow.MageLord;
        public string        LearnHint   = "Kill or befriend a Mage Lord";
        public string        Flavour     = "";
        public string        LordFaction = ""; // "" = any lord; "battania","aserai" etc = faction-specific
        public int           ReqIntelligence;
        public int           ReqSocial;
        public int           ReqCunning;
    }

    public static class SpellDatabase
    {
        public static readonly IReadOnlyList<SpellEntry> All = new List<SpellEntry>
        {
            // ── STARTING ─────────────────────────────────────────────────
            new SpellEntry { Name="Memory",       Combo="(book)",  DayCost=0,  BookTag="MEMORY",
                Context=SpellContext.Both,    GlowColor=SpellGlowColor.Support,
                LearnHow=LearnHow.Starting, LordFaction="",
                LearnHint="Starting spell",
                Flavour="The formulas do not live in your mind. They live in your blood. This is simply the act of listening." },

            // ── ATTRIBUTE ────────────────────────────────────────────────
            new SpellEntry { Name="Vortex",       Combo="LRL",     DayCost=8,  BookTag="VORTEX",
                Context=SpellContext.Mission, GlowColor=SpellGlowColor.Combat,
                LearnHow=LearnHow.Personality, LordFaction="",
                LearnHint="Become Munificent",
                Flavour="The same force, turned inward. You become the heaviest thing on the field." },

            new SpellEntry { Name="Detonate",     Combo="UURR",    DayCost=50, BookTag="BLAST",
                Context=SpellContext.Mission, GlowColor=SpellGlowColor.Combat,
                LearnHow=LearnHow.Travel, LordFaction="sturgia",
                LearnHint="Visit the Sturgian settlement while friendly",
                Flavour="A needle of condensed nothing. Whatever it strikes, it unmakes — briefly, but completely." },

            new SpellEntry { Name="Mark",         Combo="ULR",     DayCost=8,  BookTag="MARK",
                Context=SpellContext.Mission, GlowColor=SpellGlowColor.Support,
                LearnHow=LearnHow.Companion, LordFaction="", ReqCunning=5,
                LearnHint="Requires 5 Cunning",
                Flavour="You leave part of yourself here. It waits. It is patient in a way you are not." },

            // ── EVENT ────────────────────────────────────────────────────
            new SpellEntry { Name="Suppress",     Combo="RRLU",    DayCost=20, BookTag="SUPPRESS",
                Context=SpellContext.Mission, GlowColor=SpellGlowColor.Support,
                LearnHow=LearnHow.Event, LordFaction="",
                LearnHint="Fight a Mage Lord or Mage Unit in battle",
                Flavour="The art requires a channel. For sixty seconds, every channel on this field is closed." },

            new SpellEntry { Name="Halt",         Combo="URUL",    DayCost=30, BookTag="HALT",
                Context=SpellContext.Mission, GlowColor=SpellGlowColor.Support,
                LearnHow=LearnHow.Travel, LordFaction="vlandia",
                LearnHint="Visit the Vlandian settlement while friendly",
                Flavour="The enemy line remembers what it was doing and decides to stop." },

            new SpellEntry { Name="Shroud",       Combo="LLUR",    DayCost=60, BookTag="SHROUDING",
                Context=SpellContext.Map, GlowColor=SpellGlowColor.Support,
                LearnHow=LearnHow.Personality, LordFaction="",
                LearnHint="Become Deceitful",
                Flavour="You step sideways out of the world and leave the battle behind. No one notices until you are already gone." },

            new SpellEntry { Name="Bane",         Combo="LULR",    DayCost=25, BookTag="BANE",
                Context=SpellContext.Map, GlowColor=SpellGlowColor.Support,
                LearnHow=LearnHow.Travel, LordFaction="aserai",
                LearnHint="Visit the Aserai city while allied",
                Flavour="A shadow settles over the roads and camps around you. Enemy scouts forget their purpose." },

            new SpellEntry { Name="Enrage",      Combo="URUR",    DayCost=20, BookTag="ENRAGE",
                Context=SpellContext.Mission, GlowColor=SpellGlowColor.Combat,
                LearnHow=LearnHow.Travel, LordFaction="sturgia",
                LearnHint="Visit the Sturgian settlement while friendly",
                Flavour="The order reaches the enemy before their caution does. They surge forward in a murderous rush." },

            new SpellEntry { Name="Dismount",    Combo="RRUUL",   DayCost=25, BookTag="DISMOUNT",
                Context=SpellContext.Mission, GlowColor=SpellGlowColor.Support,
                LearnHow=LearnHow.Travel, LordFaction="vlandia",
                LearnHint="Visit the Vlandian settlement while friendly",
                Flavour="Horses are told to be elsewhere. Riders comply the hard way." },

            new SpellEntry { Name="Repel",        Combo="LUURL",   DayCost=40, BookTag="REPEL",
                Context=SpellContext.Mission, GlowColor=SpellGlowColor.Combat,
                LearnHow=LearnHow.Personality, LordFaction="",
                LearnHint="Become Very Cautious",
                Flavour="A pulse, repeated. The first time is a warning. The rest are a statement." },

            new SpellEntry { Name="Stop Arrows",  Combo="LURLUR",  DayCost=45, BookTag="STOP_ARROWS",
                Context=SpellContext.Mission, GlowColor=SpellGlowColor.Support,
                LearnHow=LearnHow.Travel, LordFaction="vlandia",
                LearnHint="Visit the Vlandian settlement while friendly",
                Flavour="The bowstrings slacken. The enemy remembers steel exists." },

            new SpellEntry { Name="Scatter",      Combo="RULR",    DayCost=25, BookTag="SCATTER",
                Context=SpellContext.Mission, GlowColor=SpellGlowColor.Combat,
                LearnHow=LearnHow.Event, LordFaction="",
                LearnHint="Win a battle while outnumbered 3 to 1",
                Flavour="Formation is belief. The Gift ends the belief." },

            new SpellEntry { Name="Dark Bargain", Combo="RRRLLLU", DayCost=0,  BookTag="DARK_BARGAIN",
                Context=SpellContext.Map, GlowColor=SpellGlowColor.Combat,
                LearnHow=LearnHow.Event, LordFaction="",
                LearnHint="Execute at least one lord",
                Flavour="Someone else's years, given in the only way that cannot be refused." },

            // ── TRAVEL (friendly/allied faction required) ─────────────────
            // Battania — nature, life, air
            new SpellEntry { Name="Rejuvenate",   Combo="ULUL",    DayCost=10, BookTag="REJUVENATE",
                Context=SpellContext.Map, GlowColor=SpellGlowColor.Healing,
                LearnHow=LearnHow.Travel, LordFaction="battania",
                LearnHint="Visit the Battanian village while friendly",
                Flavour="The earth does not mourn what it yields. Only the one who asked pays the toll." },

            new SpellEntry { Name="Restore",      Combo="UULL",    DayCost=25, BookTag="RESTORE",
                Context=SpellContext.Map, GlowColor=SpellGlowColor.Healing,
                LearnHow=LearnHow.Personality, LordFaction="",
                LearnHint="Become Munificent",
                Flavour="You cannot give life. But you can redistribute it — from your years to their wounds." },

            new SpellEntry { Name="Featherfall",  Combo="LUUL",    DayCost=10, BookTag="FEATHERFALL",
                Context=SpellContext.Mission, GlowColor=SpellGlowColor.Support,
                LearnHow=LearnHow.Travel, LordFaction="battania",
                LearnHint="Visit the Battanian village while friendly",
                Flavour="The void does not catch you. It simply slows the agreement between you and the ground." },

            new SpellEntry { Name="Inspire",      Combo="RULRU",   DayCost=20, BookTag="INSPIRE",
                Context=SpellContext.Map, GlowColor=SpellGlowColor.Healing,
                LearnHow=LearnHow.Personality, LordFaction="",
                LearnHint="Become Fearless",
                Flavour="Not courage. Certainty. They feel — briefly — that you cannot lose." },

            new SpellEntry { Name="Mending",      Combo="UULUR",   DayCost=30, BookTag="MENDING",
                Context=SpellContext.Mission, GlowColor=SpellGlowColor.Healing,
                LearnHow=LearnHow.Travel, LordFaction="battania",
                LearnHint="Visit the Battanian village while friendly",
                Flavour="The body knows how to be whole. The Gift tells it to hurry." },

            // Aserai — manipulation, dark arts
            new SpellEntry { Name="Charm",        Combo="RLLR",    DayCost=25, BookTag="CHARM",
                Context=SpellContext.Map, GlowColor=SpellGlowColor.Support,
                LearnHow=LearnHow.Travel, LordFaction="aserai",
                LearnHint="Visit the Aserai village while friendly",
                Flavour="You are not lying. You are simply allowing them to believe what is convenient." },

            new SpellEntry { Name="Sinister Will", Combo="RRULR",  DayCost=25, BookTag="SINISTER_WILL",
                Context=SpellContext.Map, GlowColor=SpellGlowColor.Combat,
                LearnHow=LearnHow.Personality, LordFaction="",
                LearnHint="Become Sadistic",
                Flavour="The land does not resist you. It simply forgets how to produce." },

            new SpellEntry { Name="Severe Life",  Combo="UURRLL",  DayCost=45, BookTag="SEVERE_LIFE",
                Context=SpellContext.Mission, GlowColor=SpellGlowColor.Combat,
                LearnHow=LearnHow.Travel, LordFaction="aserai",
                LearnHint="Visit the Aserai city while allied",
                Flavour="Somewhere on the field, someone stops. You did not choose who. The void chose." },

            // Sturgia — brute force
            new SpellEntry { Name="Clairvoyance", Combo="LLRRU",   DayCost=10, BookTag="CLAIRVOYANCE",
                Context=SpellContext.Map, GlowColor=SpellGlowColor.Support,
                LearnHow=LearnHow.Travel, LordFaction="empire",
                LearnHint="Visit the Empire settlement while friendly",
                Flavour="You are everywhere. Briefly. The cost of being everywhere is that you are briefly nowhere." },

            new SpellEntry { Name="Blast",        Combo="RUURL",   DayCost=20, BookTag="HURT",
                Context=SpellContext.Mission, GlowColor=SpellGlowColor.Combat,
                LearnHow=LearnHow.Companion, LordFaction="", ReqIntelligence=4,
                LearnHint="Recruit a magical companion",
                Flavour="The void does not throw. It simply removes the distance between your will and their ruin." },

            // Vlandia — order, positioning
            new SpellEntry { Name="Relocate",     Combo="LLRR",    DayCost=10, BookTag="RELOCATE",
                Context=SpellContext.Mission, GlowColor=SpellGlowColor.Support,
                LearnHow=LearnHow.Travel, LordFaction="vlandia",
                LearnHint="Visit the Vlandian settlement while friendly",
                Flavour="You are here. Then you are somewhere else. The distance between was never real." },

            new SpellEntry { Name="Pacify",       Combo="LLURL",   DayCost=40, BookTag="PACIFY",
                Context=SpellContext.Mission, GlowColor=SpellGlowColor.Support,
                LearnHow=LearnHow.Personality, LordFaction="",
                LearnHint="Become Compassionate",
                Flavour="The killing impulse, suspended. They will remember how to hate you. Later." },

            // Khuzait — absorbed from the land, not taught
            new SpellEntry { Name="Weightless",   Combo="LLUU",    DayCost=30, BookTag="WEIGHTLESS",
                Context=SpellContext.Mission, GlowColor=SpellGlowColor.Combat,
                LearnHow=LearnHow.Travel, LordFaction="",
                LearnHint="Visit the Khuzait settlement while friendly",
                Flavour="Weight is a contract. You void it for everyone nearby, including your allies. Choose carefully." },

            // ── MAGE LORD (faction-specific) ──────────────────────────────
            // Aserai lords
            new SpellEntry { Name="Devour",       Combo="UULR",    DayCost=0,  BookTag="DEVOUR",
                Context=SpellContext.Mission, GlowColor=SpellGlowColor.Combat,
                LearnHow=LearnHow.MageLord, LordFaction="aserai",
                LearnHint="Kill or befriend an Aserai Mage Lord",
                Flavour="They followed you. That was always going to mean something." },

            new SpellEntry { Name="Confuse",      Combo="LRULR",   DayCost=30, BookTag="CONFUSE",
                Context=SpellContext.Mission, GlowColor=SpellGlowColor.Support,
                LearnHow=LearnHow.Personality, LordFaction="",
                LearnHint="Become Cerebral",
                Flavour="A word in the blood, not the ear. The enemy follows an order they did not choose." },

            // Vlandia lords
            new SpellEntry { Name="Accelerate",   Combo="RRLL",    DayCost=15, BookTag="ACCELERATE",
                Context=SpellContext.Mission, GlowColor=SpellGlowColor.Support,
                LearnHow=LearnHow.Companion, LordFaction="", ReqCunning=4,
                LearnHint="Recruit a magical companion",
                Flavour="The body was always capable of this. The mind simply did not believe it yet." },

            // Empire lords

            new SpellEntry { Name="Calling",      Combo="UULRLU",  DayCost=90, BookTag="CALLING",
                Context=SpellContext.Map, GlowColor=SpellGlowColor.Support,
                LearnHow=LearnHow.MageLord, LordFaction="empire",
                LearnHint="Kill or befriend an Empire Mage Lord",
                Flavour="The call carries further than a voice. Those who hear it do not know why they march." },

            new SpellEntry { Name="Aura of Hate", Combo="RLLUR",   DayCost=45, BookTag="AURA_OF_HATE",
                Context=SpellContext.Map, GlowColor=SpellGlowColor.Combat,
                LearnHow=LearnHow.Condition, LordFaction="",
                LearnHint="Raze at least 5 villages",
                Flavour="They see you coming. They see what you intend. Their legs simply will not carry them to the fight." },

            new SpellEntry { Name="Hollow Name",  Combo="RUUR",    DayCost=30, BookTag="HOLLOW_NAME",
                Context=SpellContext.Map, GlowColor=SpellGlowColor.Combat,
                LearnHow=LearnHow.Event, LordFaction="",
                LearnHint="Reach age 70 through spell use",
                Flavour="You are less. But so are they. The void does not play favourites — it only takes." },

            new SpellEntry { Name="Break Spirits", Combo="LRUR",   DayCost=40, BookTag="BREAK_SPIRITS",
                Context=SpellContext.Mission, GlowColor=SpellGlowColor.Support,
                LearnHow=LearnHow.Event, LordFaction="",
                LearnHint="Win a battle while your warband is on the verge of breaking",
                Flavour="The last thread of courage snaps. Men remember fear faster than they remember orders." },

            new SpellEntry { Name="Long Road",    Combo="LRLU",    DayCost=25, BookTag="LONG_ROAD",
                Context=SpellContext.Map, GlowColor=SpellGlowColor.Support,
                LearnHow=LearnHow.MageLord, LordFaction="vlandia",
                LearnHint="Kill or befriend a Vlandian Mage Lord",
                Flavour="Distance is negotiable. You simply have to be willing to pay the toll." },

            new SpellEntry { Name="Unname",       Combo="LRRUL",   DayCost=35, BookTag="UNNAME",
                Context=SpellContext.Mission, GlowColor=SpellGlowColor.Support,
                LearnHow=LearnHow.MageLord, LordFaction="",
                LearnHint="Kill or befriend any Mage Lord",
                Flavour="A lord's power is the belief of those who follow them. For ten seconds, they forget to believe." },

            new SpellEntry { Name="Crush",        Combo="UUURRRL", DayCost=65, BookTag="CRUSH",
                Context=SpellContext.Mission, GlowColor=SpellGlowColor.Combat,
                LearnHow=LearnHow.MageLord, LordFaction="",
                LearnHint="Kill or befriend any Mage Lord",
                Flavour="More. The force is proportional to your will. So is what is left of them." },
        };
        public static SpellEntry Find(string combo) =>
            All.FirstOrDefault(s => s.Combo == combo);
    }

    // =========================================================================
    // 3. SPELL KNOWLEDGE  —  tracks the Gift flag, book notifications, and
    //    whether the Hollow Covenant (prisoner ritual) has been learned.
    //    Books do NOT gate casting. They only inform the player of combos.
    //    Any spell can be cast by anyone with the Gift, at any time.
    //    The ritual IS gated — it requires carrying and reading the scroll.
    // =========================================================================
    public static class SpellKnowledge
    {
        private static bool _hasGift;
        private static bool _ritualKnown;
        private static int  _scrollDaysCarried;
        private static bool _firstSoldierSacrifice = true;

        // Specific seeded settlement names for travel-gated spells
        // Assigned randomly at game start, saved to disk
        public static string SiteBattania  { get; private set; } = "";
        public static string SiteSturgia   { get; private set; } = "";
        public static string SiteVlandia   { get; private set; } = "";
        public static string SiteKhuzait   { get; private set; } = "";
        public static string SiteAserai    { get; private set; } = "";
        public static string SiteAseraiCity{ get; private set; } = "";
        public static string SiteEmpire    { get; private set; } = "";

        private static readonly Random _siteRng = new Random();

        public static void SeedLocations()
        {
            if (!string.IsNullOrEmpty(SiteBattania)) return;
            try
            {
                SiteBattania   = PickSettlement("battania", false);
                SiteSturgia    = PickSettlement("sturgia",  false);
                SiteVlandia    = PickSettlement("vlandia",  false);
                SiteKhuzait    = PickSettlement("khuzait",  false);
                SiteAserai     = PickSettlement("aserai",   false);
                SiteAseraiCity = PickSettlement("aserai",   true);
                SiteEmpire     = PickSettlement("empire",   false);

                InformationManager.DisplayMessage(new InformationMessage(
                    "The Gift stirs with a sense of distant places. Some knowledge is not found — it is travelled to.",
                    new Color(0.5f, 0.2f, 0.7f)));
            }
            catch { }
        }

        private static string PickSettlement(string culture, bool townOnly)
        {
            var candidates = Settlement.All
                .Where(s => s.Culture?.StringId == culture &&
                            (townOnly ? s.IsTown : !s.IsTown))
                .ToList();
            if (candidates.Count == 0)
            {
                // Fallback: any settlement of that culture
                candidates = Settlement.All
                    .Where(s => s.Culture?.StringId == culture)
                    .ToList();
            }
            if (candidates.Count == 0) return "";
            return candidates[_siteRng.Next(candidates.Count)].Name?.ToString() ?? "";
        }

        // Learning event flags
        public static bool HasFoughtMage      { get; private set; }
        public static bool HasDefeatedKhuzait { get; private set; }
        public static bool HasFledBattle      { get; private set; }
        public static bool HasWonSoloBattle   { get; private set; }
        public static bool HasExecutedLord    { get; private set; }
        public static bool HasRazedAtLeast5Villages { get; private set; }
        private static int _razedVillagesCount;

        public static void TriggerFoughtMage()      { if (!HasFoughtMage)      { HasFoughtMage      = true; CheckEventSpells(); } }
        public static void TriggerDefeatedKhuzait() { if (!HasDefeatedKhuzait) { HasDefeatedKhuzait = true; CheckEventSpells(); } }
        public static void TriggerFledBattle()      { if (!HasFledBattle)      { HasFledBattle      = true; CheckEventSpells(); } }
        public static void TriggerWonSoloBattle()   { if (!HasWonSoloBattle)   { HasWonSoloBattle   = true; CheckEventSpells(); } }
        public static void TriggerExecutedLord()    { if (!HasExecutedLord)    { HasExecutedLord    = true; CheckEventSpells(); } }
        public static void TriggerRazedVillage()
        {
            _razedVillagesCount++;
            if (!HasRazedAtLeast5Villages && _razedVillagesCount >= 5)
            {
                HasRazedAtLeast5Villages = true;
                CheckConditionSpells();
            }
        }
        public static bool HasVisitedBattania { get; private set; }
        public static bool HasVisitedAserai   { get; private set; }

        public static bool HasVisitedSturgia  { get; private set; }
        public static bool HasVisitedVlandia  { get; private set; }
        public static bool HasUsedPush7InBattle { get; private set; }
        public static bool HasFoughtAlone10   { get; private set; }

        public static void TriggerPush7InBattle()    { if (!HasUsedPush7InBattle) { HasUsedPush7InBattle = true; CheckEventSpells(); } }
        public static void TriggerVisitedSturgia()   { if (!HasVisitedSturgia)  { HasVisitedSturgia  = true; CheckEventSpells(); CheckTravelSpells(); } }
        public static void TriggerVisitedVlandia()   { if (!HasVisitedVlandia)  { HasVisitedVlandia  = true; CheckTravelSpells(); } }
        public static bool HasVisitedKhuzait  { get; private set; }
        public static bool HasVisitedEmpire   { get; private set; }
        public static void TriggerVisitedKhuzait()   { if (!HasVisitedKhuzait)  { HasVisitedKhuzait  = true; CheckTravelSpells(); } }
        public static void TriggerVisitedEmpire()    { if (!HasVisitedEmpire)   { HasVisitedEmpire   = true; CheckTravelSpells(); } }
        public static bool HasReachedAge50     { get; private set; }
        public static bool HasReachedAge70     { get; private set; }
        public static bool HasWonOutnumbered3to1 { get; private set; }
        public static bool HasWonWhileMoraleBroken { get; private set; }

        public static void TriggerReachedAge50()
        {
            if (!HasReachedAge50 && Hero.MainHero?.Age >= 50f)
            { HasReachedAge50 = true; CheckEventSpells(); }
        }

        public static void TriggerReachedAge70()
        {
            if (!HasReachedAge70 && Hero.MainHero?.Age >= 70f)
            { HasReachedAge70 = true; CheckEventSpells(); }
        }

        public static void TriggerOutnumbered3to1()
        {
            if (!HasWonOutnumbered3to1)
            {
                HasWonOutnumbered3to1 = true;
                CheckEventSpells();
            }
        }
        public static void TriggerWonWhileMoraleBroken()
        {
            if (!HasWonWhileMoraleBroken)
            {
                HasWonWhileMoraleBroken = true;
                CheckEventSpells();
            }
        }
        public static void TriggerFoughtAlone10()    { if (!HasFoughtAlone10)   { HasFoughtAlone10   = true; CheckEventSpells(); } }
        public static void TriggerVisitedBattania() { if (!HasVisitedBattania) { HasVisitedBattania = true; CheckTravelSpells(); } }
        public static void TriggerVisitedAserai()     { if (!HasVisitedAserai)     { HasVisitedAserai     = true; CheckTravelSpells(); } }
        public static bool HasVisitedAseraiCity { get; private set; }
        public static void TriggerVisitedAseraiCity() { if (!HasVisitedAseraiCity) { HasVisitedAseraiCity = true; CheckTravelSpells(); } }

        private static readonly HashSet<string> _notifiedTags   = new HashSet<string>();
        private static readonly HashSet<string> _bookReminderTags = new HashSet<string>();
        private static readonly HashSet<string> _taughtByLords  = new HashSet<string>();
        private static readonly HashSet<string> _intellectLearnedTags = new HashSet<string>();
        private static readonly HashSet<string> _giftedChildIds = new HashSet<string>();
        private static int _grimoirePage = 0;
        private const  int GrimoirePageSize = 4;

        public static bool IsChildGifted(string heroId) => _giftedChildIds.Contains(heroId);
        public static void AddGiftedChild(string heroId) => _giftedChildIds.Add(heroId);

        public static bool HasGift               => _hasGift;
        public static bool IsRitualKnown         => _ritualKnown;
        public static bool IsFirstSoldierSacrifice => _firstSoldierSacrifice;

        public static void GrantGift()                        => _hasGift = true;
        public static void MarkBookKnown(string bookTag)      => _notifiedTags.Add(bookTag);
        public static bool HasLearnedFromLord(string lordId)  => _taughtByLords.Contains(lordId);
        public static void MarkLearnedFromLord(string lordId) => _taughtByLords.Add(lordId);
        public static bool IsKnown(string bookTag)            => _notifiedTags.Contains(bookTag);
        public static void MarkFirstSoldierSacrifice()        => _firstSoldierSacrifice = false;

        // Grant all Starting spells immediately on gift
        public static void GrantStartingSpells()
        {
            foreach (SpellEntry s in SpellDatabase.All.Where(s => s.LearnHow == LearnHow.Starting))
                _notifiedTags.Add(s.BookTag);
        }

        public static void GrantAwakeningBonusSpell()
        {
            var candidates = SpellDatabase.All
                .Where(s => s.LearnHow != LearnHow.Starting && !IsKnown(s.BookTag))
                .ToList();
            if (candidates.Count == 0) return;

            SpellEntry learned = candidates[MBRandom.RandomInt(candidates.Count)];
            RevealSpell(learned, "The Gift opens violently. One formula comes with it, unearned and undeniable.");
        }

        // Learn random spells from raw Intelligence, and reserve companion and
        // personality spells for their dedicated unlock paths.
        public static void CheckIntellectSpells()
        {
            if (!_hasGift || Hero.MainHero == null) return;
            Hero h = Hero.MainHero;
            int targetCount = h.GetAttributeValue(DefaultCharacterAttributes.Intelligence) / 2;

            while (_intellectLearnedTags.Count < targetCount)
            {
                var candidates = SpellDatabase.All
                    .Where(s => !IsKnown(s.BookTag) &&
                                s.LearnHow != LearnHow.Starting &&
                                s.LearnHow != LearnHow.Companion &&
                                s.LearnHow != LearnHow.Personality)
                    .ToList();
                if (candidates.Count == 0) return;

                SpellEntry learned = candidates[MBRandom.RandomInt(candidates.Count)];
                _intellectLearnedTags.Add(learned.BookTag);
                RevealSpell(learned, "Your intellect sharpens â€” a new formula surfaces.");
            }

            return;
            /*

            foreach (SpellEntry s in SpellDatabase.All.Where(s => s.LearnHow == LearnHow.Companion))
            {
                if (_notifiedTags.Contains(s.BookTag)) continue;
                try
                {
                    int intel  = h.GetAttributeValue(DefaultCharacterAttributes.Intelligence);
                    int social = h.GetAttributeValue(DefaultCharacterAttributes.Social);
                    int cunn   = h.GetAttributeValue(DefaultCharacterAttributes.Cunning);

                    if (intel  < s.ReqIntelligence) continue;
                    if (social < s.ReqSocial)       continue;
                    if (cunn   < s.ReqCunning)       continue;

                    RevealSpell(s, "Your knowledge deepens — a new formula surfaces.");
                }
                catch { }
            }
            */
        }

        public static void CheckPersonalitySpells()
        {
            if (!_hasGift || Hero.MainHero == null) return;

            Hero h = Hero.MainHero;
            foreach (SpellEntry s in SpellDatabase.All.Where(s => s.LearnHow == LearnHow.Personality))
            {
                if (_notifiedTags.Contains(s.BookTag)) continue;

                bool conditionMet = false;
                string traitLabel = "";

                try
                {
                    if (s.BookTag == "SHROUDING")
                    {
                        conditionMet = h.GetTraitLevel(DefaultTraits.Honor) <= -2;
                        traitLabel = "Deceitful";
                    }
                    else if (s.BookTag == "PACIFY")
                    {
                        conditionMet = h.GetTraitLevel(DefaultTraits.Mercy) >= 2;
                        traitLabel = "Compassionate";
                    }
                    else if (s.BookTag == "SINISTER_WILL")
                    {
                        conditionMet = h.GetTraitLevel(DefaultTraits.Mercy) <= -2;
                        traitLabel = "Sadistic";
                    }
                    else if (s.BookTag == "REPEL")
                    {
                        conditionMet = h.GetTraitLevel(DefaultTraits.Valor) <= -2;
                        traitLabel = "Very Cautious";
                    }
                    else if (s.BookTag == "INSPIRE")
                    {
                        conditionMet = h.GetTraitLevel(DefaultTraits.Valor) >= 2;
                        traitLabel = "Fearless";
                    }
                    else if (s.BookTag == "CONFUSE")
                    {
                        conditionMet = h.GetTraitLevel(DefaultTraits.Calculating) >= 2;
                        traitLabel = "Cerebral";
                    }
                    else if (s.BookTag == "RESTORE")
                    {
                        conditionMet = h.GetTraitLevel(DefaultTraits.Generosity) >= 2;
                        traitLabel = "Munificent";
                    }
                    else if (s.BookTag == "VORTEX")
                    {
                        conditionMet = h.GetTraitLevel(DefaultTraits.Generosity) >= 2;
                        traitLabel = "Munificent";
                    }
                }
                catch { }

                if (conditionMet)
                    RevealSpell(s, $"Your {traitLabel.ToLowerInvariant()} nature unlocks a new formula.");
            }
        }

        public static void CheckTravelSpells()
        {
            if (!_hasGift) return;
            foreach (SpellEntry s in SpellDatabase.All.Where(s => s.LearnHow == LearnHow.Travel))
            {
                if (_notifiedTags.Contains(s.BookTag)) continue;

                bool conditionMet;
                string siteName = "";
                if      (s.BookTag == "REJUVENATE")    { conditionMet = HasVisitedBattania;   siteName = SiteBattania; }
                else if (s.BookTag == "FEATHERFALL")  { conditionMet = HasVisitedBattania;   siteName = SiteBattania; }
                else if (s.BookTag == "MENDING")       { conditionMet = HasVisitedBattania;   siteName = SiteBattania; }
                else if (s.BookTag == "CHARM")         { conditionMet = HasVisitedAserai;     siteName = SiteAserai; }
                else if (s.BookTag == "SEVERE_LIFE")   { conditionMet = HasVisitedAseraiCity; siteName = SiteAseraiCity; }
                else if (s.BookTag == "BANE")          { conditionMet = HasVisitedAseraiCity; siteName = SiteAseraiCity; }
                else if (s.BookTag == "BLAST")         { conditionMet = HasVisitedSturgia;    siteName = SiteSturgia; }
                else if (s.BookTag == "CLAIRVOYANCE")  { conditionMet = HasVisitedEmpire;     siteName = SiteEmpire; }
                else if (s.BookTag == "RELOCATE")      { conditionMet = HasVisitedVlandia;    siteName = SiteVlandia; }
                else if (s.BookTag == "HALT")          { conditionMet = HasVisitedVlandia;    siteName = SiteVlandia; }
                else if (s.BookTag == "ENRAGE")        { conditionMet = HasVisitedSturgia;    siteName = SiteSturgia; }
                else if (s.BookTag == "DISMOUNT")      { conditionMet = HasVisitedVlandia;    siteName = SiteVlandia; }
                else if (s.BookTag == "STOP_ARROWS")   { conditionMet = HasVisitedVlandia;    siteName = SiteVlandia; }
                else if (s.BookTag == "WEIGHTLESS")    { conditionMet = HasVisitedKhuzait;    siteName = SiteKhuzait; }
                else                                   { conditionMet = false; }

                if (conditionMet)
                {
                    string ctx = string.IsNullOrEmpty(siteName)
                        ? "Something in this land stirs the Gift."
                        : $"Something in {siteName} stirs the Gift.";
                    RevealSpell(s, ctx);
                }
            }
        }

        // Check event-gated spells
        public static void CheckEventSpells()
        {
            if (!_hasGift) return;
            foreach (SpellEntry s in SpellDatabase.All.Where(s => s.LearnHow == LearnHow.Event))
            {
                if (_notifiedTags.Contains(s.BookTag)) continue;

                bool conditionMet;
                if      (s.BookTag == "SUPPRESS")     conditionMet = HasFoughtMage;
                else if (s.BookTag == "SCATTER")       conditionMet = HasWonOutnumbered3to1;
                else if (s.BookTag == "DARK_BARGAIN")  conditionMet = HasExecutedLord;
                else if (s.BookTag == "HOLLOW_NAME")   conditionMet = HasReachedAge70;
                else if (s.BookTag == "BREAK_SPIRITS") conditionMet = HasWonWhileMoraleBroken;
                else                                   conditionMet = false;

                if (conditionMet)
                    RevealSpell(s, "An experience unlocks something you didn't know you knew.");
            }
        }

        public static void CheckConditionSpells()
        {
            if (!_hasGift) return;
            foreach (SpellEntry s in SpellDatabase.All.Where(s => s.LearnHow == LearnHow.Condition))
            {
                if (_notifiedTags.Contains(s.BookTag)) continue;

                bool conditionMet;
                if      (s.BookTag == "AURA_OF_HATE") conditionMet = HasRazedAtLeast5Villages;
                else                                  conditionMet = false;

                if (conditionMet)
                    RevealSpell(s, "Your conquests leave a pattern behind.");
            }
        }

        private static bool TryGetTravelSpellSite(SpellEntry s, out string siteName)
        {
            siteName = "";
            if (s == null || s.LearnHow != LearnHow.Travel) return false;

            if      (s.BookTag == "REJUVENATE")    siteName = SiteBattania;
            else if (s.BookTag == "FEATHERFALL")   siteName = SiteBattania;
            else if (s.BookTag == "MENDING")       siteName = SiteBattania;
            else if (s.BookTag == "CHARM")         siteName = SiteAserai;
            else if (s.BookTag == "SEVERE_LIFE")   siteName = SiteAseraiCity;
            else if (s.BookTag == "BANE")          siteName = SiteAseraiCity;
            else if (s.BookTag == "BLAST")         siteName = SiteSturgia;
            else if (s.BookTag == "CLAIRVOYANCE")  siteName = SiteEmpire;
            else if (s.BookTag == "RELOCATE")      siteName = SiteVlandia;
            else if (s.BookTag == "HALT")          siteName = SiteVlandia;
            else if (s.BookTag == "ENRAGE")        siteName = SiteSturgia;
            else if (s.BookTag == "DISMOUNT")      siteName = SiteVlandia;
            else if (s.BookTag == "STOP_ARROWS")   siteName = SiteVlandia;
            else if (s.BookTag == "WEIGHTLESS")    siteName = SiteKhuzait;

            return !string.IsNullOrEmpty(siteName);
        }

        public static void TryLearnRandomUnknownSpell(string contextLine, string flavourPrefix = null)
        {
            if (!_hasGift) return;

            var candidates = SpellDatabase.All
                .Where(s => !IsKnown(s.BookTag) &&
                            s.LearnHow != LearnHow.Starting &&
                            s.LearnHow != LearnHow.Personality)
                .ToList();

            if (candidates.Count == 0) return;

            SpellEntry learned = candidates[MBRandom.RandomInt(candidates.Count)];
            string ctx = string.IsNullOrEmpty(contextLine)
                ? "You find an old book among the spoils."
                : contextLine;
            if (!string.IsNullOrEmpty(flavourPrefix) && !string.IsNullOrEmpty(learned.Flavour))
                ctx = flavourPrefix + ctx;
            RevealSpell(learned, ctx);
        }

        public static bool TryLearnTravelSpellFromSiteName(string settlementName, string contextLine, int chancePercent = 50)
        {
            if (!_hasGift || string.IsNullOrEmpty(settlementName)) return false;

            foreach (SpellEntry s in SpellDatabase.All.Where(s => s.LearnHow == LearnHow.Travel))
            {
                if (IsKnown(s.BookTag)) continue;
                string siteName;
                if (!TryGetTravelSpellSite(s, out siteName)) continue;
                if (string.IsNullOrEmpty(siteName)) continue;
                if (!settlementName.Contains(siteName)) continue;
                if (MBRandom.RandomInt(100) >= chancePercent) continue;

                RevealSpell(s, contextLine);
                return true;
            }

            return false;
        }

        public static void RevealSpell(SpellEntry s, string contextLine)
        {
            _notifiedTags.Add(s.BookTag);
            string cost = s.DayCost == 0 ? "no cost" : s.DayCost >= 252 ? $"{s.DayCost/252}y" : $"{s.DayCost}d";
            InformationManager.DisplayMessage(new InformationMessage(
                contextLine, new Color(0.6f, 0.2f, 0.8f)));
            if (!string.IsNullOrEmpty(s.Flavour))
                InformationManager.DisplayMessage(new InformationMessage(
                    $"\"{s.Flavour}\"", new Color(0.55f, 0.3f, 0.75f)));
            InformationManager.DisplayMessage(new InformationMessage(
                $"You learn {s.Name}. Combo: {s.Combo} — {cost}. (S / L3 opens grimoire)",
                new Color(0.8f, 0.4f, 1f)));
        }

        public static void LearnFromCompanion(string companionName)
        {
            var candidates = SpellDatabase.All
                .Where(s => !IsKnown(s.BookTag) && s.LearnHow == LearnHow.Companion)
                .ToList();

            if (candidates.Count == 0)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{companionName} carries the Gift, but you already know the companion-taught formulas.",
                    new Color(0.5f, 0.3f, 0.7f)));
                return;
            }

            SpellEntry learned = candidates[MBRandom.RandomInt(candidates.Count)];
            RevealSpell(learned, $"{companionName} teaches you something only they know.");
        }

        public static void ShowGrimoire()
        {
            var known = SpellDatabase.All
                .Where(s => _notifiedTags.Contains(s.BookTag))
                .OrderBy(s => s.DayCost)
                .ToList();

            if (known.Count == 0)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "The gift is there — the words are not yet.",
                    new Color(0.5f, 0.1f, 0.6f)));
                return;
            }

            int totalPages = (known.Count + GrimoirePageSize - 1) / GrimoirePageSize;
            _grimoirePage  = _grimoirePage % totalPages;

            var pageSpells = known.Skip(_grimoirePage * GrimoirePageSize).Take(GrimoirePageSize).ToList();

            InformationManager.DisplayMessage(new InformationMessage(
                $"══ Grimoire — Page {_grimoirePage + 1}/{totalPages} ({known.Count}/{SpellDatabase.All.Count} known) ══",
                new Color(0.8f, 0.3f, 1f)));

            foreach (SpellEntry s in pageSpells)
            {
                string ctx  = s.Context == SpellContext.Map ? "Map" :
                              s.Context == SpellContext.Both ? "Any" : "Battle";
                string cost = s.DayCost == 0   ? "free" :
                              s.DayCost >= 252 ? $"{s.DayCost/252}y" : $"{s.DayCost}d";
                InformationManager.DisplayMessage(new InformationMessage(
                    $"  {s.Name} [{s.Combo}] {cost} {ctx}",
                    new Color(0.7f, 0.5f, 0.9f)));
            }

            bool isLastPage = _grimoirePage == totalPages - 1;
            if (isLastPage)
            {
            var unknown = SpellDatabase.All
                .Where(s => !_notifiedTags.Contains(s.BookTag))
                .ToList();
            if (unknown.Count > 0)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"  — {unknown.Count} unknown —",
                    new Color(0.4f, 0.4f, 0.4f)));

                foreach (SpellEntry s in unknown.Take(4))
                {
                    string hint = s.LearnHint;
                    if (s.LearnHow == LearnHow.Travel)
                    {
                        if (s.LordFaction == "battania" && !string.IsNullOrEmpty(SiteBattania))
                            hint = $"Visit {SiteBattania} (Battania) — friendly relations required";
                        else if ((s.BookTag == "CHARM" || s.BookTag == "SINISTER_WILL") && !string.IsNullOrEmpty(SiteAserai))
                            hint = $"Visit {SiteAserai} (Aserai) — friendly relations required";
                        else if (s.LordFaction == "aserai" && !string.IsNullOrEmpty(SiteAserai))
                            hint = $"Visit {SiteAserai} (Aserai) — friendly relations required";
                        else if (s.BookTag == "SEVERE_LIFE" && !string.IsNullOrEmpty(SiteAseraiCity))
                            hint = $"Visit {SiteAseraiCity} (Aserai city) — alliance required";
                        else if (s.BookTag == "BANE" && !string.IsNullOrEmpty(SiteAseraiCity))
                            hint = $"Visit {SiteAseraiCity} (Aserai city) — alliance required";
                        else if (s.BookTag == "ENRAGE" && !string.IsNullOrEmpty(SiteSturgia))
                            hint = $"Visit {SiteSturgia} (Sturgia) — friendly relations required";
                        else if (s.LordFaction == "sturgia" && !string.IsNullOrEmpty(SiteSturgia))
                            hint = $"Visit {SiteSturgia} (Sturgia) — friendly relations required";
                        else if (s.LordFaction == "vlandia" && !string.IsNullOrEmpty(SiteVlandia))
                            hint = $"Visit {SiteVlandia} (Vlandia) — friendly relations required";
                        else if (string.IsNullOrEmpty(s.LordFaction) && !string.IsNullOrEmpty(SiteKhuzait))
                            hint = $"Visit {SiteKhuzait} (Khuzait) — friendly relations required";
                    }
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"  ? {hint}",
                        new Color(0.35f, 0.35f, 0.35f)));
                }
                if (unknown.Count > 4)
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"  ... and {unknown.Count - 4} more",
                        new Color(0.3f, 0.3f, 0.3f)));
            }
            } // end isLastPage

            if (totalPages > 1)
                InformationManager.DisplayMessage(new InformationMessage(
                    $"  [ Page {_grimoirePage + 1}/{totalPages} — press S / L3 again for next ]",
                    new Color(0.5f, 0.3f, 0.7f)));

            _grimoirePage = (_grimoirePage + 1) % totalPages;
        }

        /// <summary>
        /// Called on daily tick: shows a one-time reminder for each spell book
        /// found in inventory. Books do not unlock spells; criteria do.
        /// </summary>
        public static void ScanInventoryForBooks(Hero hero)
        {
            if (hero?.PartyBelongedTo?.ItemRoster == null) return;

            foreach (SpellEntry spell in SpellDatabase.All)
            {
                if (_notifiedTags.Contains(spell.BookTag)) continue;
                if (_bookReminderTags.Contains(spell.BookTag)) continue;

                foreach (ItemRosterElement element in hero.PartyBelongedTo.ItemRoster)
                {
                    ItemObject item = element.EquipmentElement.Item;
                    if (item == null) continue;

                    // Match by StringId convention (twa_book_<tag_lower>)
                    string itemId = item.StringId ?? string.Empty;
                    if (!itemId.Contains(spell.BookTag.ToLower())) continue;

                    _bookReminderTags.Add(spell.BookTag);
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"You study the scroll. {spell.Name} — combo: {spell.Combo}.",
                        new Color(0.7f, 0.2f, 1f)));
                    break;
                }
            }
        }

        /// <summary>
        /// Called on daily tick: checks if the player carries the Hollow Covenant.
        /// The ritual is not unlocked immediately — the scroll must be carried for
        /// 3 in-game days. Each day a whisper surfaces. On day 3, it opens.
        /// The counter is cumulative and persists if the scroll is dropped and
        /// re-acquired, so the player cannot be stuck.
        /// </summary>
        public static void ScanForRitualScroll(Hero hero)
        {
            if (_ritualKnown) return;
            if (hero?.PartyBelongedTo?.ItemRoster == null) return;

            ItemObject scroll =
                MBObjectManager.Instance.GetObject<ItemObject>("twa_scroll_ritual");
            if (scroll == null) return;

            // Only count days it is actually carried (match by StringId)
            bool hasScroll = hero.PartyBelongedTo.ItemRoster
                .Any(e => e.EquipmentElement.Item?.StringId == "twa_scroll_ritual");
            if (!hasScroll) return;

            _scrollDaysCarried++;

            switch (_scrollDaysCarried)
            {
                case 1:
                    InformationManager.DisplayMessage(new InformationMessage(
                        "Something in your pack feels warmer than it should.",
                        new Color(0.45f, 0.05f, 0.5f)));
                    break;

                case 2:
                    InformationManager.DisplayMessage(new InformationMessage(
                        "You dream of strangers' faces. They age. You watch.",
                        new Color(0.45f, 0.05f, 0.5f)));
                    break;

                case 3:
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The parchment has not moved. Your hand keeps finding it.",
                        new Color(0.45f, 0.05f, 0.5f)));

                    // The knowledge takes root on the third day
                    _ritualKnown = true;
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The words settle. You understand now. " +
                        "The years in your captives — they were always yours to take. " +
                        "So were the years in those who follow you willingly.",
                        Color.FromUint(0xFFCC0000)));
                    break;
            }
        }

        public static void Save(IDataStore store)
        {
            var tagList    = _notifiedTags.ToList();
            var bookReminderList = _bookReminderTags.ToList();
            var taughtList = _taughtByLords.ToList();
            var intellectList = _intellectLearnedTags.ToList();
            bool hfm = HasFoughtMage;      bool hdk = HasDefeatedKhuzait;
            bool hfb = HasFledBattle;      bool hwsb = HasWonSoloBattle;
            bool hel = HasExecutedLord;    bool hvb = HasVisitedBattania;
            bool hva = HasVisitedAserai;   bool hvs = HasVisitedSturgia;
            bool hvac = HasVisitedAseraiCity;
            bool hvv = HasVisitedVlandia;  bool hvk = HasVisitedKhuzait;
            bool hp7 = HasUsedPush7InBattle; bool hve = HasVisitedEmpire;
            bool hfa = HasFoughtAlone10;  bool hr50 = HasReachedAge50;
            bool hr70 = HasReachedAge70;
            bool ho3 = HasWonOutnumbered3to1;
            bool hbm = HasWonWhileMoraleBroken;

            store.SyncData("TWA_HasGift",               ref _hasGift);
            store.SyncData("TWA_RitualKnown",           ref _ritualKnown);
            store.SyncData("TWA_ScrollDaysCarried",     ref _scrollDaysCarried);
            store.SyncData("TWA_FirstSoldierSacrifice", ref _firstSoldierSacrifice);
            store.SyncData("TWA_NotifiedTags",    ref tagList);
            store.SyncData("TWA_BookReminderTags", ref bookReminderList);
            store.SyncData("TWA_TaughtByLords",   ref taughtList);
            store.SyncData("TWA_IntellectLearnedTags", ref intellectList);

            // Seeded travel locations
            string sb = SiteBattania; string ss = SiteSturgia; string sv = SiteVlandia;
            string sk = SiteKhuzait;  string sa = SiteAserai;  string sac = SiteAseraiCity;
            string se = SiteEmpire;
            store.SyncData("TWA_SiteBattania",   ref sb);
            store.SyncData("TWA_SiteSturgia",    ref ss);
            store.SyncData("TWA_SiteVlandia",    ref sv);
            store.SyncData("TWA_SiteKhuzait",    ref sk);
            store.SyncData("TWA_SiteAserai",     ref sa);
            store.SyncData("TWA_SiteAseraiCity", ref sac);
            store.SyncData("TWA_SiteEmpire",     ref se);
            if (!string.IsNullOrEmpty(sb))  SiteBattania   = sb;
            if (!string.IsNullOrEmpty(ss))  SiteSturgia    = ss;
            if (!string.IsNullOrEmpty(sv))  SiteVlandia    = sv;
            if (!string.IsNullOrEmpty(sk))  SiteKhuzait    = sk;
            if (!string.IsNullOrEmpty(sa))  SiteAserai     = sa;
            if (!string.IsNullOrEmpty(sac)) SiteAseraiCity = sac;
            if (!string.IsNullOrEmpty(se))  SiteEmpire     = se;
            store.SyncData("TWA_HasFoughtMage",         ref hfm);
            store.SyncData("TWA_HasDefeatedKhuzait",    ref hdk);
            store.SyncData("TWA_HasFledBattle",         ref hfb);
            store.SyncData("TWA_HasWonSoloBattle",      ref hwsb);
            store.SyncData("TWA_HasExecutedLord",       ref hel);
            store.SyncData("TWA_HasVisitedBattania",    ref hvb);
            store.SyncData("TWA_HasVisitedAserai",      ref hva);
            store.SyncData("TWA_HasVisitedAseraiCity",  ref hvac);
            store.SyncData("TWA_HasVisitedSturgia",     ref hvs);
            store.SyncData("TWA_HasVisitedVlandia",     ref hvv);
            store.SyncData("TWA_HasUsedPush7",          ref hp7);
            store.SyncData("TWA_HasFoughtAlone10",      ref hfa);
            store.SyncData("TWA_HasReachedAge50",       ref hr50);
            store.SyncData("TWA_HasReachedAge70",       ref hr70);
            store.SyncData("TWA_HasWonOutnumbered",     ref ho3);
            store.SyncData("TWA_HasWonWhileMoraleBroken", ref hbm);
            store.SyncData("TWA_RazedVillagesCount",    ref _razedVillagesCount);
            store.SyncData("TWA_HasVisitedKhuzait",     ref hvk);
            store.SyncData("TWA_HasVisitedEmpire",      ref hve);

            if (hfm)  HasFoughtMage         = true;
            if (hdk)  HasDefeatedKhuzait    = true;
            if (hfb)  HasFledBattle         = true;
            if (hwsb) HasWonSoloBattle      = true;
            if (hel)  HasExecutedLord       = true;
            if (hvb)  HasVisitedBattania    = true;
            if (hva)  HasVisitedAserai      = true;
            if (hvac) HasVisitedAseraiCity  = true;
            if (hvs)  HasVisitedSturgia     = true;
            if (hvv)  HasVisitedVlandia     = true;
            if (hvk)  HasVisitedKhuzait     = true;
            if (hve)  HasVisitedEmpire      = true;
            if (hp7)  HasUsedPush7InBattle  = true;
            if (hfa)  HasFoughtAlone10      = true;
            if (hr50) HasReachedAge50       = true;
            if (hr70) HasReachedAge70       = true;
            if (ho3)  HasWonOutnumbered3to1 = true;
            if (hbm)  HasWonWhileMoraleBroken = true;
            HasRazedAtLeast5Villages = _razedVillagesCount >= 5;

            _notifiedTags.Clear();
            if (tagList    != null) foreach (var t in tagList)    _notifiedTags.Add(t);
            _bookReminderTags.Clear();
            if (bookReminderList != null) foreach (var t in bookReminderList) _bookReminderTags.Add(t);
            _taughtByLords.Clear();
            if (taughtList != null) foreach (var t in taughtList) _taughtByLords.Add(t);
            _intellectLearnedTags.Clear();
            if (intellectList != null) foreach (var t in intellectList) _intellectLearnedTags.Add(t);

            var giftedList = _giftedChildIds.ToList();
            store.SyncData("TWA_GiftedChildIds", ref giftedList);
            _giftedChildIds.Clear();
            if (giftedList != null) foreach (var id in giftedList) _giftedChildIds.Add(id);
        }
    }

    // =========================================================================
    // 4. RITUAL SETTINGS  —  player-controlled sacrifice rate
    //    Toggled via combo RLRL (not a spell — no age cost, no spell entry).
    //    Cycles: Off → 1 per hour → 5 per hour → All available → Off
    //    Applies to both prisoner sacrifice and Dark Tithe equally.
    // =========================================================================
    public enum SacrificeMode { Off, One, Five, All }

    public static class RitualSettings
    {
        private static SacrificeMode _mode = SacrificeMode.One;

        public static SacrificeMode Mode => _mode;

        // How many to consume in a single hourly tick
        public static int CountForTick(int available)
        {
            switch (_mode)
            {
                case SacrificeMode.Off:  return 0;
                case SacrificeMode.One:  return Math.Min(1, available);
                case SacrificeMode.Five: return Math.Min(5, available);
                case SacrificeMode.All:  return available;
                default:                 return 0;
            }
        }

        public static void Cycle()
        {
            switch (_mode)
            {
                case SacrificeMode.Off:  _mode = SacrificeMode.One;  break;
                case SacrificeMode.One:  _mode = SacrificeMode.Five; break;
                case SacrificeMode.Five: _mode = SacrificeMode.All;  break;
                default:                 _mode = SacrificeMode.Off;  break;
            }

            string label;
            switch (_mode)
            {
                case SacrificeMode.Off:  label = "Off — the ritual is suspended.";      break;
                case SacrificeMode.One:  label = "1 per hour — measured, deliberate.";  break;
                case SacrificeMode.Five: label = "5 per hour — you are hungry for it."; break;
                case SacrificeMode.All:  label = "All available — everything, at once."; break;
                default:                 label = "Unknown.";                             break;
            }

            InformationManager.DisplayMessage(new InformationMessage(
                $"Sacrifice rate: {label}",
                Color.FromUint(0xFFCC4444)));
        }

        public static void Save(IDataStore store)
        {
            int m = (int)_mode;
            store.SyncData("TWA_SacrificeMode", ref m);
            _mode = (SacrificeMode)m;
        }
    }
    // =========================================================================
    // 5. ACTIVE EFFECT MANAGER  —  timed buffs & debuffs
    // =========================================================================
    public class ActiveEffect
    {
        public string   Name;
        public float    Duration;
        public float    Elapsed;
        public bool     IsMissionEffect; // true = ticks during Mission, false = Campaign Map
        public Action<float> OnTick;     // called every frame, receives dt
        public Action   OnExpire;

        public bool IsExpired => Elapsed >= Duration;
    }

    public static class ActiveEffectManager
    {
        private static readonly List<ActiveEffect> _effects = new List<ActiveEffect>();

        private const int MaxEffects = 10;

        public static void Add(ActiveEffect e)
        {
            if (_effects.Count >= MaxEffects) return; // hard safety cap
            _effects.Add(e);
        }

        public static bool Has(string name) =>
            _effects.Any(e => e.Name == name);

        public static void MissionTick(float dt) => Tick(dt, true);
        public static void MapTick(float dt)     => Tick(dt, false);

        private static void Tick(float dt, bool inMission)
        {
            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                var e = _effects[i];
                if (e.IsMissionEffect != inMission) continue;

                e.Elapsed += dt;
                try { e.OnTick?.Invoke(dt); }
                catch { /* don't let a bad effect tick crash the game */ }

                if (!e.IsExpired) continue;

                try { e.OnExpire?.Invoke(); }
                catch { }

                _effects.RemoveAt(i);
                if (e.OnExpire == null)
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{e.Name} fades.", new Color(0.5f, 0.5f, 0.5f)));
            }
        }

        public static void ClearMissionEffects() =>
            _effects.RemoveAll(e => e.IsMissionEffect);
    }

    // =========================================================================
    // 6. CAMPAIGN BEHAVIOR  —  save/load, hourly ritual, daily book scan,
    //                           "Born with the Gift" first-run check
    // =========================================================================
    public class MagicCampaignBehavior : CampaignBehaviorBase
    {
        private bool _giftCheckDone;

        public override void RegisterEvents()
        {
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.OnMissionEndedEvent.AddNonSerializedListener(this, OnMissionEnded);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.BeforeHeroKilledEvent.AddNonSerializedListener(this, OnBeforeHeroKilled);
            CampaignEvents.OnCharacterCreationIsOverEvent.AddNonSerializedListener(this, OnNewGameCreated);
            CampaignEvents.PlayerStartTalkFromMenu.AddNonSerializedListener(this, OnPlayerTalkToHero);
            CampaignEvents.HeroCreated.AddNonSerializedListener(this, OnHeroCreated);
            CampaignEvents.NewCompanionAdded.AddNonSerializedListener(this, OnCompanionAdded);
            CampaignEvents.OnTroopRecruitedEvent.AddNonSerializedListener(this, OnTroopRecruited);
            CampaignEvents.OnHideoutBattleCompletedEvent.AddNonSerializedListener(this, OnHideoutBattleCompleted);
            CampaignEvents.VillageStateChanged.AddNonSerializedListener(this, OnVillageStateChanged);
        }

        // ── Daily: scan inventory for new spell books ─────────────────────
        private void OnDailyTick()
        {
            if (!_giftCheckDone)
            {
                _giftCheckDone = true;
                CheckGiftMark();
            }

            if (_pendingStartingBookDays > 0)
            {
                _pendingStartingBookDays--;
                if (GiveStartingBook())
                    _pendingStartingBookDays = 0;
            }

            // Check intelligence and personality spells every day
            SpellKnowledge.CheckIntellectSpells();
            SpellKnowledge.CheckPersonalitySpells();
            SpellKnowledge.CheckConditionSpells();

            // Age milestone check
            SpellKnowledge.TriggerReachedAge50();
            SpellKnowledge.TriggerReachedAge70();

            // Village visit detection — check current settlement via hero's party
            // Also check the static Settlement.CurrentSettlement which is set during
            // settlement menus where Hero.MainHero.CurrentSettlement can be null.
            if (SpellKnowledge.HasGift)
            {
                try
                {
                    Settlement current = Hero.MainHero?.CurrentSettlement
                                      ?? Settlement.CurrentSettlement;
                    if (current != null)
                    {
                        string name = current.Name?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(name))
                        {
                            // Friendly = same faction OR not at war
                            bool isFriendly = false;
                            try
                            {
                                IFaction playerFaction  = Hero.MainHero?.MapFaction;
                                IFaction settleFaction  = current.MapFaction;
                                if (playerFaction == settleFaction)
                                    isFriendly = true;
                                else if (playerFaction is Kingdom pk && settleFaction is Kingdom sk)
                                    isFriendly = !pk.IsAtWarWith(sk);
                                else
                                    isFriendly = true; // fallback: allow if check fails
                            }
                            catch { isFriendly = true; }

                            if (isFriendly)
                            {
                                // Use Contains() rather than == to tolerate any formatting
                                // differences between how the name was seeded and how it
                                // appears on Hero.CurrentSettlement.Name.ToString().
                                if (!string.IsNullOrEmpty(SpellKnowledge.SiteBattania)   && name.Contains(SpellKnowledge.SiteBattania))   SpellKnowledge.TriggerVisitedBattania();
                                if (!string.IsNullOrEmpty(SpellKnowledge.SiteAserai)     && name.Contains(SpellKnowledge.SiteAserai))     SpellKnowledge.TriggerVisitedAserai();
                                if (!string.IsNullOrEmpty(SpellKnowledge.SiteAseraiCity) && name.Contains(SpellKnowledge.SiteAseraiCity)) SpellKnowledge.TriggerVisitedAseraiCity();
                                if (!string.IsNullOrEmpty(SpellKnowledge.SiteSturgia)    && name.Contains(SpellKnowledge.SiteSturgia))    SpellKnowledge.TriggerVisitedSturgia();
                                if (!string.IsNullOrEmpty(SpellKnowledge.SiteVlandia)    && name.Contains(SpellKnowledge.SiteVlandia))    SpellKnowledge.TriggerVisitedVlandia();
                                if (!string.IsNullOrEmpty(SpellKnowledge.SiteEmpire)     && name.Contains(SpellKnowledge.SiteEmpire))     SpellKnowledge.TriggerVisitedEmpire();
                                if (!string.IsNullOrEmpty(SpellKnowledge.SiteKhuzait)    && name.Contains(SpellKnowledge.SiteKhuzait))    SpellKnowledge.TriggerVisitedKhuzait();
                            }
                        }
                    }
                }
                catch { }
            }

            MageLordRegistry.SeedInitialMageLords();
            MageLordRegistry.DailyAgeDrift();
            MageLordRegistry.DailyMapCast();
            MageLordRegistry.MaintainMageArmies();
            SpellEffects.TickLongRoad();

            // Seed locations if not yet done (e.g. save loaded before this feature)
            SpellKnowledge.SeedLocations();

            if (SpellKnowledge.HasGift)
            {
                SpellKnowledge.ScanInventoryForBooks(Hero.MainHero);
                SpellKnowledge.ScanForRitualScroll(Hero.MainHero);
            }
        }

        // ── Seed the Hollow Covenant into one random town at game start ───
        // Called from OnNewGameCreated only. The scroll may also appear later
        // as a rare drop when a Mage Lord dies (see MageLordRegistry).
        private void SeedRitualScrollAtGameStart()
        {
            ItemObject scroll =
                MBObjectManager.Instance.GetObject<ItemObject>("twa_scroll_ritual");
            if (scroll == null) return;

            var towns = Settlement.All.Where(s => s.IsTown).ToList();
            if (towns.Count == 0) return;

            // Pick one town at random — the scroll exists once in the world
            Settlement chosen = towns[MBRandom.RandomInt(towns.Count)];
            chosen.ItemRoster.AddToCounts(scroll, 1);
        }

        // ── New game: present the Gift as a revelation, not a choice ─────
        private void OnNewGameCreated()
        {
            // Seed the Hollow Covenant into one random town at campaign start
            SeedRitualScrollAtGameStart();

            // Assign specific settlements for travel-gated spells
            SpellKnowledge.SeedLocations();

            InformationManager.ShowInquiry(new InquiryData(
                titleText: "The Mark of the Gift",
                text:      "Since before you could name it, something has moved beneath your skin. " +
                           "A pull toward the world's hidden seams. A certainty, quiet as bone, " +
                           "that the fabric between what is and what could be is thinner than other people believe.\n\n" +
                           "Your body has always known the cost. One of your physical attributes — " +
                           "Vigor, Control, or Endurance — grew up lesser than it should have, worn thinner " +
                           "by what you carry. You were never told. But you felt it.\n\n" +
                           "Will you claim what you were born with?",
                isAffirmativeOptionShown: true,
                isNegativeOptionShown:    true,
                affirmativeText: "I feel it. I always have.",
                negativeText:    "Whatever stirs in me — I will keep it buried.",
                affirmativeAction: OnGiftAccepted,
                negativeAction:    null,
                soundEventPath:    "event:/ui/panels/settlement_village_enter"
            ));
        }

        private int _pendingStartingBookDays = 0; // counts down from 3, 0 = not pending

        private void OnGiftAccepted()
        {
            SpellKnowledge.GrantGift();
            SpellKnowledge.GrantStartingSpells();
            SpellKnowledge.GrantAwakeningBonusSpell();
            // Immediately reveal any intellect or personality spells the player already qualifies for
            SpellKnowledge.CheckIntellectSpells();
            SpellKnowledge.CheckPersonalitySpells();
            ApplyBirthPenalty();
            _giftCheckDone = true;

            InformationManager.DisplayMessage(new InformationMessage(
                "It was always there. It answers now.",
                new Color(0.7f, 0.2f, 1f)));

            if (!GiveStartingBook())
                _pendingStartingBookDays = 1;
        }

        private void CheckGiftMark()
        {
            // Legacy fallback: detect the twa_gift_mark item for saves that
            // pre-date the character-creation inquiry, or for console-granted gifts.
            if (_giftCheckDone) return;

            var markItem = MBObjectManager.Instance.GetObject<ItemObject>("twa_gift_mark");
            if (markItem == null) return;

            if (MobileParty.MainParty?.ItemRoster?.FindIndexOfItem(markItem) >= 0)
            {
                SpellKnowledge.GrantGift();
                SpellKnowledge.GrantStartingSpells();
                SpellKnowledge.GrantAwakeningBonusSpell();
                SpellKnowledge.CheckIntellectSpells();
                SpellKnowledge.CheckPersonalitySpells();
                ApplyBirthPenalty();
            }
        }

        private void ApplyBirthPenalty()
        {
            // Note: SetAttributeValue does not exist in this version of the API.
            // The penalty is represented narratively — the attribute description
            // tells the player which attribute is affected by the Gift.
            string[] names = { "Vigor", "Control", "Endurance" };
            int pick = MBRandom.RandomInt(3);
            InformationManager.DisplayMessage(new InformationMessage(
                $"{names[pick]} has always been lesser — the mark your gift left on your body.",
                Color.FromUint(0xFFCC4444)));
        }

        private bool GiveStartingBook()
        {
            if (Hero.MainHero == null) return false;

            // Show all currently known spells (Starting + any Attribute spells already qualified)
            var knownNow = SpellDatabase.All
                .Where(s => SpellKnowledge.IsKnown(s.BookTag))
                .OrderBy(s => s.DayCost)
                .ToList();

            InformationManager.DisplayMessage(new InformationMessage(
                "The Gift answers. Formulas surface — some you have always carried, some earned by your mind.",
                new Color(0.7f, 0.55f, 0.9f)));

            InformationManager.DisplayMessage(new InformationMessage(
                "\"The formulas do not live in your mind. They live in your blood. This is simply the act of listening.\"",
                new Color(0.55f, 0.3f, 0.75f)));

            foreach (SpellEntry s in knownNow)
            {
                string cost = s.DayCost == 0 ? "free" : $"{s.DayCost}d";
                string ctx  = s.Context == SpellContext.Map ? "Map" :
                              s.Context == SpellContext.Both ? "Any" : "Battle";
                InformationManager.DisplayMessage(new InformationMessage(
                    $"  {s.Name} [{s.Combo}] — {cost} — {ctx}",
                    new Color(0.75f, 0.35f, 1f)));
            }

            InformationManager.DisplayMessage(new InformationMessage(
                "Hold Left Alt, type the combo with WASD, release to cast. UDL opens your grimoire.",
                new Color(0.6f, 0.4f, 0.8f)));

            return true;
        }

        // ── Hourly: Tyrant Ritual + Dark Tithe + Mage Lord respawn timers ──
        private void OnHourlyTick()
        {
            MageLordRegistry.CheckRespawnTimers();

            Hero player = Hero.MainHero;
            if (player == null || !SpellKnowledge.HasGift || !SpellKnowledge.IsRitualKnown) return;
            if (RitualSettings.Mode == SacrificeMode.Off) return;

            MobileParty party = MobileParty.MainParty;
            if (party == null || !party.ComputeIsWaiting()) return;
            if (player.Age <= 20f) return;

            if (party.PrisonRoster.TotalManCount > 0)
            {
                // ── Prisoner ritual ───────────────────────────────────────────
                int count = RitualSettings.CountForTick(party.PrisonRoster.TotalManCount);
                for (int i = 0; i < count; i++)
                {
                    if (party.PrisonRoster.TotalManCount == 0) break;

                    // Remove first available prisoner
                    var prisoner = party.PrisonRoster.GetTroopRoster()
                        .Where(e => e.Character != null && e.Number > 0)
                        .FirstOrDefault();
                    if (prisoner.Character == null) break;
                    party.PrisonRoster.RemoveTroop(prisoner.Character, 1);

                    player.SetBirthDay(player.BirthDay + CampaignTime.Years(30f / 252f));
                    if (player.Age < 20f)
                        player.SetBirthDay(CampaignTime.Now - CampaignTime.Years(20));

                    // Honor/morale penalty — TraitLevelingHelper.OnPointsGained
                    // does not exist in this API version; morale penalty applied only
                    party.RecentEventsMorale -= 5f;
                }

                string plural = count == 1 ? "soul" : "souls";
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{count} {plural} consumed. Your youth returns. | Age: {(int)player.Age}",
                    Color.FromUint(0xFFCC0000)));
            }
            else
            {
                // ── Dark Tithe — soldiers when no prisoners remain ────────────
                OnHourlyTickDarkTithe(player, party);
            }
        }

        // ── Hourly: Dark Tithe — soldiers when no prisoners remain ────────
        // Requires: ritual known, waiting, age > 20, party ≥ 6 troops.
        // Wounded troops are taken first, then smallest healthy group.
        private void OnHourlyTickDarkTithe(Hero player, MobileParty party)
        {
            var roster = party.MemberRoster;

            if (roster.TotalManCount < 6) return;

            int count = RitualSettings.CountForTick(roster.TotalManCount - 5);
            if (count <= 0) return;

            int taken = 0;
            for (int i = 0; i < count; i++)
            {
                if (roster.TotalManCount <= 5) break;

                var wounded = roster.GetTroopRoster()
                    .Where(e => e.WoundedNumber > 0)
                    .OrderByDescending(e => e.WoundedNumber)
                    .FirstOrDefault();

                var sacrifice = wounded.Character != null
                    ? wounded
                    : roster.GetTroopRoster()
                        .Where(e => e.Number > 0)
                        .OrderBy(e => e.Number)
                        .FirstOrDefault();

                if (sacrifice.Character == null) break;

                roster.RemoveTroop(sacrifice.Character, 1);
                player.SetBirthDay(player.BirthDay + CampaignTime.Years(60f / 252f));
                if (player.Age < 20f)
                    player.SetBirthDay(CampaignTime.Now - CampaignTime.Years(20));

                // TraitLevelingHelper.OnPointsGained unavailable in this API version
                party.RecentEventsMorale -= 20f;
                taken++;
            }

            if (taken == 0) return;

            if (SpellKnowledge.IsFirstSoldierSacrifice)
            {
                SpellKnowledge.MarkFirstSoldierSacrifice();
                InformationManager.DisplayMessage(new InformationMessage(
                    $"You have taken from those who chose to follow you. " +
                    $"The years flow in. Your soldiers feel something change in the air. " +
                    $"They do not know what. | Age: {(int)player.Age}",
                    Color.FromUint(0xFF990000)));
            }
            else
            {
                string plural = taken == 1 ? "tithe" : "tithes";
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{taken} {plural} paid. Your warband grows quieter each time. | Age: {(int)player.Age}",
                    Color.FromUint(0xFF990000)));
            }
        }

        // ── When a Mage Lord dies in any context ──────────────────────────
        private void OnHeroKilled(Hero victim, Hero killer,
                                  KillCharacterAction.KillCharacterActionDetail detail,
                                  bool showNotification)
        {
            if (!MageLordRegistry.IsMageLord(victim)) return;

            bool killedFightingPlayer = false;
            try
            {
                var ev = MapEvent.PlayerMapEvent;
                if (ev != null && detail == KillCharacterAction.KillCharacterActionDetail.DiedInBattle)
                {
                    BattleSideEnum playerSide = ev.PlayerSide;
                    PartyBase victimParty = victim.PartyBelongedTo?.Party;
                    killedFightingPlayer = victimParty != null && ev.InvolvedParties.Any(p =>
                        p.Side != playerSide && p == victimParty);
                }
            }
            catch { }

            MageLordRegistry.OnMageLordDied(victim, killedFightingPlayer);
        }

        // ── Clean up mission effects and AI cooldowns when a battle ends ──
        private void OnMissionEnded(IMission mission)
        {
            ActiveEffectManager.ClearMissionEffects();
            MageLordAI.ClearCooldowns();
            MageUnitManager.ClearTimers();
            SpellEffects.ResetPushCounter();
            SpellEffects.ClearMark();

            if (!SpellKnowledge.HasGift) return;

            try
            {
                MapEvent ev = MapEvent.PlayerMapEvent;
                if (ev == null) return;

                BattleState state   = ev.BattleState;
                BattleSideEnum side = ev.PlayerSide;
                bool playerWon = (side == BattleSideEnum.Attacker &&
                                  state == BattleState.AttackerVictory) ||
                                 (side == BattleSideEnum.Defender &&
                                  state == BattleState.DefenderVictory);
                // Fled = player didn't win and battle ended (DefeatedSide = player, or not a win)
                bool playerFled = !playerWon && ev.DefeatedSide == side;

                if (playerFled)
                    SpellKnowledge.TriggerFledBattle();

                if (!playerWon) return;

                // Detect Khuzait opponent
                bool foughtKhuzait = false;
                try
                {
                    foughtKhuzait = ev.InvolvedParties.Any(p =>
                        p.Side != side &&
                        (p.Culture?.StringId == "khuzait" ||
                         p.MobileParty?.MapFaction?.Name?.ToString().ToLower().Contains("khuzait") == true));
                }
                catch { }
                if (foughtKhuzait) SpellKnowledge.TriggerDefeatedKhuzait();

                // Detect solo battle (player side: no hero companions, only troops)
                try
                {
                    bool noCompanions = !ev.InvolvedParties
                        .Where(p => p.Side == side)
                        .SelectMany(p => p.MemberRoster.GetTroopRoster())
                        .Any(e => e.Character?.IsHero == true &&
                                  e.Character != Hero.MainHero?.CharacterObject);
                    if (noCompanions)
                        SpellKnowledge.TriggerWonSoloBattle();
                }
                catch { }

                // Detect outnumbered 3:1
                try
                {
                    int playerStrength = ev.InvolvedParties
                        .Where(p => p.Side == side)
                        .Sum(p => p.MemberRoster.TotalManCount);
                    int enemyStrength = ev.InvolvedParties
                        .Where(p => p.Side != side)
                        .Sum(p => p.MemberRoster.TotalManCount);
                    if (playerStrength > 0 && enemyStrength >= playerStrength * 3)
                        SpellKnowledge.TriggerOutnumbered3to1();
                }
                catch { }

                try
                {
                    float morale = MobileParty.MainParty?.RecentEventsMorale ?? 100f;
                    if (morale <= 25f)
                        SpellKnowledge.TriggerWonWhileMoraleBroken();
                }
                catch { }
            }
            catch { }
        }

        private void OnBeforeHeroKilled(Hero victim, Hero killer,
            KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            if (!SpellKnowledge.HasGift) return;
            try
            {
                if (detail == KillCharacterAction.KillCharacterActionDetail.Executed
                    && victim != null && victim.IsLord)
                    SpellKnowledge.TriggerExecutedLord();
            }
            catch { }
        }

        private void OnPlayerTalkToHero(Hero hero)
        {
            if (!SpellKnowledge.HasGift) return;
            if (hero == null || !MageLordRegistry.IsMageLord(hero)) return;
            if (hero.MapFaction != Hero.MainHero?.MapFaction) return;
            if (SpellKnowledge.HasLearnedFromLord(hero.StringId)) return;

            // Check if this lord has faction-specific spells to teach
            string lordFaction = (hero.MapFaction as Kingdom)?.StringId?.ToLower() ?? "";
            var teachable = SpellDatabase.All
                .Where(s => !SpellKnowledge.IsKnown(s.BookTag) &&
                            s.LearnHow == LearnHow.MageLord &&
                            (s.LordFaction == "" || s.LordFaction == lordFaction ||
                             (s.BookTag == "AURA_OF_HATE" && lordFaction == "khuzait")))
                .ToList();

            if (teachable.Count == 0) return;

            InformationManager.ShowInquiry(new InquiryData(
                titleText: "A Kindred Mark",
                text: $"{hero.Name} pauses mid-sentence. Their eyes settle on you differently — " +
                      $"not as a lord regards a companion, but as one marked regards another.\n\n" +
                      $"\"You carry it too,\" they say quietly. " +
                      $"\"I wondered. There is something I know that you do not. " +
                      $"I can show you, if you want it. The years it costs are yours to pay — " +
                      $"I only open the door.\"",
                isAffirmativeOptionShown: true,
                isNegativeOptionShown:    true,
                affirmativeText: "Show me.",
                negativeText:    "Another time.",
                affirmativeAction: () =>
                {
                    SpellKnowledge.MarkLearnedFromLord(hero.StringId);
                    MageLordRegistry.RevealRandomUnknownSpell(
                        hero.Name.ToString(), lordFaction);
                },
                negativeAction: null,
                soundEventPath: ""
            ));
        }

        private void OnHeroCreated(Hero hero, bool bornNaturally)
        {
            if (!bornNaturally || !SpellKnowledge.HasGift) return;
            try
            {
                bool parentIsPlayer = hero.Mother == Hero.MainHero || hero.Father == Hero.MainHero;
                if (!parentIsPlayer) return;
                if (MBRandom.RandomInt(100) < 30)
                {
                    SpellKnowledge.AddGiftedChild(hero.StringId);
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{hero.Name} was born carrying a faint echo of the Gift.",
                        new Color(0.6f, 0.2f, 0.8f)));
                }
            }
            catch { }
        }

        private void OnCompanionAdded(Hero companion)
        {
            if (!SpellKnowledge.HasGift || companion == null) return;
            try
            {
                if (MBRandom.RandomInt(100) < 10)
                    MageLordRegistry.TryGrantCompanionMagic(companion);
            }
            catch { }
        }

        private void OnTroopRecruited(Hero recruiter, Settlement settlement, Hero volunteer,
                                      CharacterObject troop, int count)
        {
            if (!SpellKnowledge.HasGift) return;
            if (recruiter != Hero.MainHero) return;
            if (troop == null || troop.Tier > 1) return;
            if (MobileParty.MainParty == null) return;
            try
            {
                var mageChar = MBObjectManager.Instance.GetObject<CharacterObject>("twa_mage_initiate");
                if (mageChar == null) return;
                int transformed = 0;
                for (int i = 0; i < count; i++)
                    if (MBRandom.RandomInt(100) < 2) transformed++;
                if (transformed <= 0) return;
                MobileParty.MainParty.MemberRoster.AddToCounts(troop, -transformed);
                MobileParty.MainParty.MemberRoster.AddToCounts(mageChar, transformed);
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{transformed} {(transformed == 1 ? "recruit manifests" : "recruits manifest")} an echo of the Gift.",
                    new Color(0.7f, 0.2f, 1f)));
            }
            catch { }
        }

        private void OnHideoutBattleCompleted(BattleSideEnum winnerSide, HideoutEventComponent hideoutEventComponent)
        {
            if (!SpellKnowledge.HasGift) return;
            if (winnerSide != BattleSideEnum.Attacker) return;
            if (hideoutEventComponent == null) return;
            try
            {
                if (MBRandom.RandomInt(100) < 10)
                {
                    SpellKnowledge.TryLearnRandomUnknownSpell(
                        "You sift through the bandit camp's loot and find an old spellbook.");
                }
            }
            catch { }
        }

        private void OnVillageStateChanged(Village village, Village.VillageStates oldState,
                                           Village.VillageStates newState, MobileParty raidingParty)
        {
            if (!SpellKnowledge.HasGift || village == null) return;
            if (newState != Village.VillageStates.Looted) return;
            if (raidingParty == null || raidingParty != MobileParty.MainParty) return;
            try
            {
                SpellKnowledge.TriggerRazedVillage();
                string settlementName = village.Bound?.Name?.ToString() ?? "";
                if (string.IsNullOrEmpty(settlementName)) return;
                SpellKnowledge.TryLearnTravelSpellFromSiteName(
                    settlementName,
                    $"A prisoner whispers of hidden books as {settlementName} burns.");
            }
            catch { }
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("TWA_GiftCheckDone",       ref _giftCheckDone);
            dataStore.SyncData("TWA_PendingStartingBook",  ref _pendingStartingBookDays);
            SpellKnowledge.Save(dataStore);
            RitualSettings.Save(dataStore);
            MageLordRegistry.Save(dataStore);
        }
    }

    // =========================================================================
    // 7. INPUT HANDLER  —  reads the directional buffer and fires spells
    // =========================================================================
    public static class MagicInputHandler
    {
        private static string _buffer     = "";
        private static bool   _wasFocusing = false;
        private static string _lastDisplayedBuffer = "";
        private const  int    MaxLen       = 10;

        // True while the magic focus key is held.
        // NOTE: this flag cannot intercept Bannerlord's own input pipeline — the game
        // still receives every button press regardless of this value.  On keyboard,
        // Left Alt rarely conflicts with game bindings so the experience is clean.
        // On controller, L3 + face buttons may trigger game actions alongside spells;
        // full input isolation would require a Harmony patch.
        public static bool InputSuppressed { get; private set; }

        public static void Tick(bool inMission)
        {
            if (!SpellKnowledge.HasGift) { InputSuppressed = false; return; }

            // Focus key: Left Alt (KB) or Left Trigger / LT (Gamepad)
            bool focusing = Input.IsKeyDown(InputKey.LeftAlt)
                        || Input.IsKeyDown(InputKey.ControllerLTrigger);

            InputSuppressed = focusing;

            if (focusing)
            {
                _wasFocusing = true;

                // Jeśli jesteśmy na mapie, odblokuj upływ czasu (Prędkość x1)
                if (!inMission && Campaign.Current != null)
                {
                    // Używamy bezpośredniego przypisania, aby uniknąć błędu CS1061
                    // Używamy UnstoppablePlay, aby uniknąć błędu CS0117
                    Campaign.Current.TimeControlMode = CampaignTimeControlMode.UnstoppablePlay;
                }

                // Klawiatura — W/A/D dla kombo, Spacja dla księgi
                if      (Input.IsKeyPressed(InputKey.W)) Append("U");
                else if (Input.IsKeyPressed(InputKey.A)) Append("L");
                else if (Input.IsKeyPressed(InputKey.D)) Append("R");
                else if (Input.IsKeyPressed(InputKey.S)) { SpellKnowledge.ShowGrimoire(); } // Zmienione z S na Space

                // Pad — Przyciski geometryczne dla kombo, RB (ControllerRUpper) dla księgi
                else if (Input.IsKeyPressed(InputKey.ControllerRUp))    Append("U");
                else if (Input.IsKeyPressed(InputKey.ControllerRLeft))  Append("L");
                else if (Input.IsKeyPressed(InputKey.ControllerRDown))  Append("R");
                else if (Input.IsKeyPressed(InputKey.ControllerLThumb)) { SpellKnowledge.ShowGrimoire(); } // Zmienione z LThumb na RB

                if (_buffer.Length > 0 && _buffer != _lastDisplayedBuffer)
                {
                    _lastDisplayedBuffer = _buffer;
                    InformationManager.DisplayMessage(new InformationMessage(
                        "[ " + _buffer + " ]", new Color(0.7f, 0.2f, 1f)));
                }
            }
            else if (_wasFocusing)
            {
                _wasFocusing = false;

                // Opcjonalnie: Zatrzymaj czas po puszczeniu skupienia na mapie
                if (!inMission && Campaign.Current != null)
                {
                    Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
                }

                _lastDisplayedBuffer = "";

                if (_buffer.Length >= 3)
                    TryCast(_buffer, inMission);
                else if (_buffer.Length > 0)
                    Fizzle("Chant too short.");

                _buffer = "";
            }
        }

        private static void Append(string dir)
        {
            if (_buffer.Length < MaxLen) _buffer += dir;
        }

        private static void TryCast(string combo, bool inMission)
        {
            // ── Meta-combos — no age cost ────────────────────────────────────
            if (combo == "RLRL")
            {
                if (!SpellKnowledge.IsRitualKnown)
                    Fizzle("You do not yet understand the ritual.");
                else
                    RitualSettings.Cycle();
                return;
            }

            // Check suppress
            if (SpellEffects.MagicSuppressedSeconds > 0f)
            {
                Fizzle("The void is suppressed. You cannot cast.");
                return;
            }

            SpellEntry spell = SpellDatabase.Find(combo);

            if (spell == null) { Fizzle("You failed to channel the Gift."); return; }

            if (!SpellKnowledge.IsKnown(spell.BookTag))
            {
                Fizzle($"Unknown. Hint: {spell.LearnHint}");
                return;
            }

            // Context check
            bool needsMission = spell.Context == SpellContext.Mission;
            bool needsMap     = spell.Context == SpellContext.Map;
            // SpellContext.Both works everywhere

            if (needsMission && !inMission)
            { Fizzle($"{spell.Name} can only be cast in the field."); return; }

            if (needsMap && inMission)
            { Fizzle($"{spell.Name} can only be cast on the campaign map."); return; }

            if (inMission && Agent.Main?.MountAgent != null)
            {
                bool allowsMountedCast =
                    spell.BookTag == "HALT" ||
                    spell.BookTag == "ENRAGE" ||
                    spell.BookTag == "DISMOUNT" ||
                    spell.BookTag == "STOP_ARROWS";

                if (!allowsMountedCast)
                {
                    Fizzle("Dismount to cast.");
                    return;
                }
            }

            // Age cost: spec-days / 252 = campaign years
            // 252 = 3 Bannerlord years — keeps costs felt but not punishing on frequent casts
            Hero.MainHero.SetBirthDay(
                Hero.MainHero.BirthDay - CampaignTime.Years((float)spell.DayCost / 252f));

            int years = spell.DayCost / 365;
            string cost = spell.DayCost >= 365
                ? $"{years} {(years == 1 ? "year" : "years")}"
                : $"{spell.DayCost} {(spell.DayCost == 1 ? "day" : "days")}";

            string batteryNote = MageUnitManager.BatteryMultiplier > 1f
                ? $" [Battery ×{MageUnitManager.BatteryMultiplier:F1}]" : "";

            InformationManager.DisplayMessage(new InformationMessage(
                $"You unleash {spell.Name}. Life withers... (-{cost}) | Age: {(int)Hero.MainHero.Age}{batteryNote}",
                Color.FromUint(0xFFFFAA00)));

            bool successfulCast = SpellEffects.Execute(combo);

            // Visual — coloured point light glow at caster
            if (inMission && Agent.Main != null)
                SpellEffects.CastGlow(Agent.Main, SpellEffects.ResolveGlowColor(spell));

            if (successfulCast && inMission && SpellEffects.IsTournamentMission())
            {
                SpellEffects.DisqualifyTournamentCaster(Agent.Main, Hero.MainHero, spell.Name);
                return;
            }
        }

        private static void Fizzle(string msg) =>
            InformationManager.DisplayMessage(new InformationMessage(
                msg, Color.FromUint(0xFF884444)));
    }

    // =========================================================================
    // 8. SPELL EFFECTS  —  the actual gameplay consequences
    //
    //    LEGEND:
    //      ✓  Fully implemented using verified native API
    //      ~  Best-effort: works but may not perfectly match spec
    //      !  TODO: requires deeper native API research; applies cost + message only
    // =========================================================================
    public static class SpellEffects
    {
        private static readonly Random _rng = new Random();

        // Suppress timer
        public static float MagicSuppressedSeconds { get; private set; } = 0f;

        // Push counter — Repel unlocks after 7 Pushes in one combat
        private static int _pushCastCount = 0;
        private static bool _lastCastFizzled = false;
        public static void ResetPushCounter() => _pushCastCount = 0;
        public static void ClearMark()        => _markAnchor = null;

        public static void TickSuppress(float dt)
        {
            if (MagicSuppressedSeconds > 0f)
                MagicSuppressedSeconds = Math.Max(0f, MagicSuppressedSeconds - dt);
        }

        public static bool Execute(string combo)
        {
            _lastCastFizzled = false;
            switch (combo)
            {
                // STARTING
                case "LLRR":    Relocate();     break;
                case "UULL":    Restore();      break;
                // ATTRIBUTE
                case "LRL":     Vortex();       break;
                case "UURR":    Detonate();     break;
                case "UULUR":   Mending();      break;
                case "LRULR":   Confuse();      break;
                // EVENT
                case "RRLU":    Suppress();     break;
                case "LLUR":    ShroudEscape(); break;
                case "LULR":    Bane();         break;
                case "RULRU":   Inspire();      break;
                case "RRRLLLU": DarkBargain();  break;
                case "ULUL":    Rejuvenate();   break;
                case "RLLR":    Charm();        break;
                // TRAVEL
                case "RRULR":   SinisterWill(); break;
                // MAGE LORD
                case "RUURL":   Blast();        break;
                case "LUURL":   Repel();        break;
                case "LLURL":   Pacify();       break;
                case "RRLL":    Accelerate();   break;
                case "UURRLL":  SevereLife();   break;
                case "LLRRU":   Clairvoyance(); break;
                case "UUURRRL": Crush();        break;
                case "RULR":    Scatter();      break;
                case "UULR":    Devour();       break;
                case "ULR":     Mark();         break;
                case "LRUR":    BreakSpirits(); break;
                case "LRRUL":   Unname();       break;
                case "RUUR":    HollowName();   break;
                case "LRLU":    LongRoad();     break;
                case "LUUL":    Featherfall();  break;
                case "LLUU":    Weightless();   break;
                case "UULRLU":  Calling();      break;
                case "RLLUR":   AuraOfHate();   break;
                case "URUL":    Halt();        break;
                case "URUR":    Enrage();      break;
                case "RRUUL":   Dismount();    break;
                case "LURLUR":  StopArrows();  break;
            }

            return !_lastCastFizzled;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static void Fizzle(string msg) =>
            InformationManager.DisplayMessage(new InformationMessage(
                msg, Color.FromUint(0xFF884444)));

        private static Agent Player => Agent.Main;

        private static IEnumerable<Agent> Enemies()
        {
            if (Mission.Current == null || Player == null) yield break;
            foreach (Agent a in Mission.Current.Agents)
                if (a != Player && !a.IsMount && a.IsActive() &&
                    a.Team != null && a.Team != Player.Team)
                    yield return a;
        }

        public static void KillAgent(Agent target)
        {
            if (target == null || !target.IsActive()) return;

            // Primary: try Die() — proper engine kill
            try
            {
                Blow blow = BuildBlow(target, DamageTypes.Cut, 2000f);
                target.Die(blow, (Agent.KillInfo)0);
                return;
            }
            catch { }

            // Fallback: MakeDead — forces visual death state
            try
            {
                target.MakeDead(true,
                    ActionIndexCache.Create("act_strike_walk_right_stance"), 0);
                return;
            }
            catch { }

            // Last resort: permanent void-stun
            // Agent is frozen in place and cannot act — effectively removed from combat
            VoidStun(target);
        }

        // Permanently freezes an agent using the same mechanism as Frost Shackle.
        // Confirmed working — SetActionChannel is used by Frost Shackle successfully.
        private static void VoidStun(Agent target)
        {
            if (target == null) return;
            try
            {
                ActionIndexCache freeze = ActionIndexCache.Create("act_stand_1");
                int targetIdx = target.Index;

                // Freeze immediately
                target.SetActionChannel(0, freeze, true);

                // Disable AI movement — these are confirmed available
                try { target.DisableScriptedMovement(); }      catch { }
                try { target.DisableScriptedCombatMovement(); } catch { }

                // Keep frozen every tick for the entire battle
                ActiveEffectManager.Add(new ActiveEffect
                {
                    Name            = $"_vstun_{targetIdx}",
                    Duration        = 999f,
                    IsMissionEffect = true,
                    OnTick = _ =>
                    {
                        if (Mission.Current == null) return;
                        Agent t = Mission.Current.Agents
                            .FirstOrDefault(a => a.Index == targetIdx);
                        if (t != null && t.IsActive())
                        {
                            t.SetActionChannel(0, freeze, true);
                            try { t.DisableScriptedMovement(); }      catch { }
                            try { t.DisableScriptedCombatMovement(); } catch { }
                        }
                    }
                });
            }
            catch { }
        }

        private static Blow BuildBlow(Agent target, DamageTypes type, float magnitude)
        {
            Blow blow = new Blow();
            blow.OwnerId        = Agent.Main?.Index ?? 0;
            blow.DamageType     = type;
            blow.BaseMagnitude  = magnitude;
            blow.InflictedDamage = (int)magnitude;
            blow.GlobalPosition = target.Position;
            blow.Direction      = new Vec3(0f, 0f, 1f);
            blow.WeaponRecord   = new BlowWeaponRecord();
            blow.DamageCalculated = true;
            blow.NoIgnore       = true;
            return blow;
        }


        // =================================================================
        // SPELL IMPLEMENTATIONS — new design based on confirmed working APIs
        // Fall damage (TeleportToPosition upward) replaces direct damage.
        // =================================================================

        // ── STARTING ─────────────────────────────────────────────────────

        private static void Memory()
        {
            // Grimoire is handled as meta-combo before Execute() is called.
            // If Execute reaches here it means something went wrong; show anyway.
            SpellKnowledge.ShowGrimoire();
        }

        private static void Detonate()
        {
            if (Player == null || Mission.Current == null) return;

            var targets = Mission.Current.Agents
                .Where(a => a != null && a.IsActive() && a != Player &&
                            a.Position.Distance(Player.Position) <= 10f)
                .ToList();

            if (targets.Count == 0)
            {
                Fizzle("No units are close enough.");
                return;
            }

            int killed = 0;
            foreach (Agent target in targets)
            {
                try
                {
                    BeginAgentGlow(target, SpellGlowColor.Combat);
                    KillAgent(target);
                    killed++;
                }
                catch { }
            }

            InformationManager.DisplayMessage(new InformationMessage(
                $"Detonate tears through {killed} {(killed == 1 ? "unit" : "units")} within 5 metres.",
                new Color(0.9f, 0.3f, 0.3f)));
        }

        private static void Relocate()
        {
            if (Player == null) return;
            Vec3 dest = Player.Position + Player.LookDirection.NormalizedCopy() * 20f;
            dest.z = Player.Position.z;
            Player.TeleportToPosition(dest);
            InformationManager.DisplayMessage(new InformationMessage(
                "You vanish and reappear 20 metres forward.",
                new Color(0.5f, 0.5f, 0.9f)));
        }

        private static void Restore()
        {
            if (MobileParty.MainParty == null) return;
            var roster = MobileParty.MainParty.MemberRoster;
            int healed = 0;
            foreach (var element in roster.GetTroopRoster().ToList())
            {
                if (element.WoundedNumber <= 0) continue;
                int h = Math.Min(element.WoundedNumber, 5);
                roster.AddToCounts(element.Character, 0, false, -h);
                healed += h;
            }
            InformationManager.DisplayMessage(new InformationMessage(
                healed > 0 ? $"Life flows through your warband. {healed} wounds close."
                           : "Your party is already whole.",
                new Color(0.6f, 1f, 0.6f)));
        }

        // ── ATTRIBUTE ────────────────────────────────────────────────────

        private static void Push()
        {
            // Repel enemies in forward cone 10m — TeleportToPosition away, then trip
            if (Player == null) return;

            // Track for Repel unlock (7 uses in one combat)
            _pushCastCount++;
            if (_pushCastCount >= 7)
                SpellKnowledge.TriggerPush7InBattle();

            Vec3 fwd = Player.LookDirection.NormalizedCopy();
            ActionIndexCache trip = ActionIndexCache.Create("act_struck_from_back_medium_left_staff");
            int pushed = 0;
            foreach (Agent a in Enemies().ToList())
            {
                Vec3 toAgent = (a.Position - Player.Position);
                float dist = toAgent.Length;
                if (dist > 20f) continue;
                // Check forward cone (~120 degrees)
                float dot = Vec3.DotProduct(fwd, toAgent.NormalizedCopy());
                if (dot < 0.5f) continue; // outside cone
                Vec3 pushDest = a.Position + fwd * 20f;
                pushDest.z = a.Position.z;
                try
                {
                    a.TeleportToPosition(pushDest);
                    if (trip.Index >= 0) a.SetActionChannel(0, trip, false);
                    pushed++;
                    BeginAgentGlow(a, SpellGlowColor.Combat);
                }
                catch { }
            }
            InformationManager.DisplayMessage(new InformationMessage(
                pushed > 0 ? $"{pushed} {(pushed==1?"enemy":"enemies")} thrown back."
                           : "No enemies in forward cone.",
                new Color(0.9f, 0.6f, 0.2f)));
        }

        private static void Vortex()
    {
        // Range extended to 20m. Pull distance remains 6m, but logic is added to prevent overshooting.
        if (Player == null) return;
        
        // Using a more standard knockdown action to ensure 'tripping' occurs reliably
        ActionIndexCache trip = ActionIndexCache.Create("act_fall_back_on_ground");
        int pulled = 0;

        foreach (Agent a in Enemies().ToList())
        {
            float dist = a.Position.Distance(Player.Position);
            
            // Stage 1: Check extended 20m range
            if (dist > 20f || dist < 0.5f) continue;

            // Stage 2: Calculate pull vector
            Vec3 dir = (Player.Position - a.Position).NormalizedCopy();
            
            // Ensure we don't teleport the enemy behind the player if they are closer than 6m
            float pullAmount = Math.Min(dist - 0.5f, 6f);
            Vec3 dest = a.Position + dir * pullAmount;
            dest.z = a.Position.z;

            try
            {
                a.TeleportToPosition(dest);

                // Stage 3: Force the trip
                if (trip.Index >= 0)
                {
                    // Channel 0 is the primary movement/action channel
                    // Using 'true' for the third parameter can help force the override
                    a.SetActionChannel(0, trip, true, (ulong)0);
                }
                pulled++;
                BeginAgentGlow(a, SpellGlowColor.Combat);
            }
            catch { }
        }

    InformationManager.DisplayMessage(new InformationMessage(
        pulled > 0 ? $"{pulled} {(pulled == 1 ? "enemy" : "enemies")} caught in the vortex."
                   : "No enemies within 20 m.",
        new Color(0.7f, 0.2f, 0.9f)));
}

        private static void Mending()
        {
            if (Player == null) return;
            Player.Health = Player.HealthLimit;
            if (Player.MountAgent != null)
                Player.MountAgent.Health = Player.MountAgent.HealthLimit;
            InformationManager.DisplayMessage(new InformationMessage(
                "Life surges back. You and your mount are whole.",
                new Color(0.4f, 1f, 0.4f)));
        }

        private static void Confuse()
        {
            // Briefly freeze all non-hero enemy troops in forward 10m cone
            if (Player == null) return;
            Vec3 fwd = Player.LookDirection.NormalizedCopy();
            int frozen = 0;
            ActionIndexCache freeze = ActionIndexCache.Create("act_stand_1");
            foreach (Agent a in Enemies().Where(a => !a.IsHero).ToList())
            {
                Vec3 toAgent = (a.Position - Player.Position);
                if (toAgent.Length > 10f) continue;
                float dot = Vec3.DotProduct(fwd, toAgent.NormalizedCopy());
                if (dot < 0.3f) continue;
                ActiveEffectManager.Add(new ActiveEffect
                {
                    Name            = $"_confuse_{a.Index}",
                    Duration        = 5f,
                    IsMissionEffect = true,
                    OnTick = _ =>
                    {
                        if (Mission.Current == null) return;
                        Agent t = Mission.Current.Agents.FirstOrDefault(x => x.Index == a.Index);
                        if (t != null && t.IsActive()) t.SetActionChannel(0, freeze, true);
                    }
                });
                frozen++;
                BeginAgentGlow(a, SpellGlowColor.Support);
            }
            InformationManager.DisplayMessage(new InformationMessage(
                frozen > 0 ? $"{frozen} {(frozen==1?"enemy":"enemies")} stand confused for 5 seconds."
                           : "No troops in cone.",
                new Color(0.6f, 0.8f, 0.3f)));
        }

        // ── EVENT ────────────────────────────────────────────────────────

        private static void Suppress()
        {
            if (ActiveEffectManager.Has("Suppress")) { Fizzle("Already suppressed."); return; }
            MagicSuppressedSeconds = 60f;
            InformationManager.DisplayMessage(new InformationMessage(
                "The void closes. No one can cast for one minute.",
                new Color(0.5f, 0.5f, 0.8f)));
        }

        public enum BattleCommandKind
        {
            Halt,
            Enrage,
            Dismount,
            StopArrows
        }

        public static void IssueBattleCommand(Agent source, BattleCommandKind kind, string successText)
        {
            if (source == null || Mission.Current == null || Mission.Current.Scene == null)
            {
                Fizzle("No battle is active.");
                return;
            }

            var formations = new HashSet<Formation>();
            var scene = Mission.Current.Scene;

            foreach (Agent a in Enemies().ToList())
            {
                if (a.Formation == null) continue;
                if (a.Position.Distance(source.Position) > 500f) continue;

                bool visible = false;
                try
                {
                    visible = scene.CheckPointCanSeePoint(source.Position, a.Position, 500f);
                }
                catch { }

                if (!visible) continue;
                formations.Add(a.Formation);
                BeginAgentGlow(a, SpellGlowColor.Support);
            }

            if (formations.Count == 0)
            {
                Fizzle("No visible enemy formations within 500m.");
                return;
            }

            int affected = 0;
            foreach (Formation formation in formations)
            {
                try
                {
                    switch (kind)
                    {
                        case BattleCommandKind.Halt:
                            formation.SetMovementOrder(MovementOrder.MovementOrderStop);
                            affected++;
                            break;
                        case BattleCommandKind.Enrage:
                            formation.SetMovementOrder(MovementOrder.MovementOrderCharge);
                            affected++;
                            break;
                        case BattleCommandKind.Dismount:
                            if (formation.HasAnyMountedUnit)
                            {
                                formation.SetRidingOrder(RidingOrder.RidingOrderDismount);
                                affected++;
                            }
                            break;
                        case BattleCommandKind.StopArrows:
                            if (formation.GetCountOfUnitsBelongingToLogicalClass(TaleWorlds.Core.FormationClass.Ranged) > 0 ||
                                formation.GetCountOfUnitsBelongingToLogicalClass(TaleWorlds.Core.FormationClass.HorseArcher) > 0)
                            {
                                formation.SetFiringOrder(FiringOrder.FiringOrderHoldYourFire);
                                affected++;
                            }
                            break;
                    }
                }
                catch { }
            }

            InformationManager.DisplayMessage(new InformationMessage(
                affected > 0 ? string.Format(successText, affected, affected == 1 ? "" : "s")
                             : "No matching enemy formations were close enough.",
                new Color(0.7f, 0.5f, 0.9f)));
        }

        private static void Halt()     => IssueBattleCommand(Player, BattleCommandKind.Halt,      "{0} enemy formation{1} ordered to halt.");
        private static void Enrage()   => IssueBattleCommand(Player, BattleCommandKind.Enrage,    "{0} enemy formation{1} driven into a charge.");
        private static void Dismount() => IssueBattleCommand(Player, BattleCommandKind.Dismount,  "{0} enemy formation{1} forced to dismount.");
        private static void StopArrows()=> IssueBattleCommand(Player, BattleCommandKind.StopArrows,"{0} enemy formation{1} told to stop shooting.");

        private static void Bane()
        {
            if (MobileParty.MainParty == null) return;
            // Apply confusion to nearby enemy parties — they lose sight of your intent
            int affected = 0;
            try
            {
                foreach (MobileParty party in MobileParty.All.ToList())
                {
                    if (party == MobileParty.MainParty) continue;
                    if (party.MapFaction == Hero.MainHero?.MapFaction) continue;
                    if (!party.IsActive) continue;
                    party.RecentEventsMorale -= 10f;
                    affected++;
                    if (affected >= 5) break; // limit to nearby parties
                }
            }
            catch { }
            InformationManager.DisplayMessage(new InformationMessage(
                "A bane settles over your party. Enemy scouts lose your trail for a few hours.",
                new Color(0.4f, 0.4f, 0.7f)));
        }

        private static void ShroudEscape()
        {
            if (TaleWorlds.CampaignSystem.Encounters.PlayerEncounter.Battle == null)
            {
                Fizzle("Shroud can only be used while an encounter is active.");
                return;
            }

            try
            {
                TaleWorlds.CampaignSystem.Encounters.PlayerEncounter.LeaveBattle();
            }
            catch
            {
                Fizzle("The shroud fails to take hold.");
                return;
            }

            InformationManager.DisplayMessage(new InformationMessage(
                "The battle falls away. You escape without losing a soldier.",
                new Color(0.4f, 0.4f, 0.7f)));
        }

        private static void Inspire()
        {
            if (MobileParty.MainParty == null) return;
            MobileParty.MainParty.RecentEventsMorale += 20f;
            InformationManager.DisplayMessage(new InformationMessage(
                "Your will reaches into your soldiers. Morale rises.",
                new Color(0.8f, 0.8f, 0.3f)));
        }

        private static void DarkBargain()
        {
            Hero player = Hero.MainHero;
            if (player == null) return;

            bool usedPrisoner = false;
            bool usedSoldier  = false;
            string sacrificeName = "an unknown soul";

            // Try prisoner first
            if (MobileParty.MainParty?.PrisonRoster?.TotalManCount > 0)
            {
                var prisoner = MobileParty.MainParty.PrisonRoster.GetTroopRoster()
                    .Where(e => e.Character != null && e.Number > 0)
                    .FirstOrDefault();
                if (prisoner.Character != null)
                {
                    sacrificeName = prisoner.Character.Name?.ToString() ?? "a prisoner";
                    MobileParty.MainParty.PrisonRoster.RemoveTroop(prisoner.Character, 1);
                    usedPrisoner = true;
                }
            }

            // Fall back to weakest non-hero soldier
            if (!usedPrisoner && MobileParty.MainParty?.MemberRoster != null)
            {
                var soldier = MobileParty.MainParty.MemberRoster.GetTroopRoster()
                    .Where(e => e.Character != null && !e.Character.IsHero && e.Number > 0)
                    .OrderBy(e => e.Character.Level)
                    .FirstOrDefault();
                if (soldier.Character != null)
                {
                    sacrificeName = soldier.Character.Name?.ToString() ?? "a soldier";
                    MobileParty.MainParty.MemberRoster.RemoveTroop(soldier.Character, 1);
                    usedSoldier = true;
                }
            }

            if (!usedPrisoner && !usedSoldier)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "The bargain requires a life. You have no prisoners and no soldiers to offer.",
                    Color.FromUint(0xFF884444)));
                return;
            }

            // Restore 30 spec-days of youth
            player.SetBirthDay(player.BirthDay + CampaignTime.Years(30f / 252f));
            if (player.Age < 20f)
                player.SetBirthDay(CampaignTime.Now - CampaignTime.Years(20f));

            // Morale penalty — worse if you consumed your own
            float moralePenalty = usedSoldier ? -20f : -10f;
            MobileParty.MainParty.RecentEventsMorale += moralePenalty;
            try { player.Clan?.AddRenown(-5f); } catch { }

            string source = usedSoldier
                ? $"one of your own — {sacrificeName}. The warband knows what happened."
                : $"a prisoner — {sacrificeName}. They had no say.";

            InformationManager.DisplayMessage(new InformationMessage(
                $"The bargain is paid. You spent {source} | Age: {(int)player.Age}",
                Color.FromUint(0xFF990000)));
        }

        private static void Rejuvenate()
        {
            ItemObject grain = MBObjectManager.Instance.GetObject<ItemObject>("grain");
            if (grain == null) return;
            MobileParty.MainParty.ItemRoster.AddToCounts(grain, 10);
            InformationManager.DisplayMessage(new InformationMessage(
                "Ten measures of grain appear in your stores.",
                new Color(0.7f, 0.9f, 0.4f)));
        }

        private static void Charm()
        {
            Hero player = Hero.MainHero;
            
            if (player?.Clan == null) 
            {
                return;
            }

            player.Clan.AddRenown(5f);

            ChangeClanInfluenceAction.Apply(player.Clan, 5f);

            InformationManager.DisplayMessage(new InformationMessage(
                "Your presence is felt. +5 Renown & +5 Influence",
                Color.FromUint(0xFFE6CC4D) // Use a Hex-based Gold color for better visibility
            ));
        }

        // ── MAGE LORD ────────────────────────────────────────────────────

        private static void Blast()
        {
            // Deal direct damage in forward 10m cone — enough to kill a looter
            if (Player == null) return;
            Vec3 fwd = Player.LookDirection.NormalizedCopy();
            ActionIndexCache trip = ActionIndexCache.Create("act_struck_from_back_medium_left_staff");
            int pushed = 0;
            int hurt = 0;
            _pushCastCount++;
            if (_pushCastCount >= 7)
                SpellKnowledge.TriggerPush7InBattle();
            foreach (Agent a in Enemies().ToList())
            {
                Vec3 toAgent = (a.Position - Player.Position);
                if (toAgent.Length > 20f) continue;
                float dot = Vec3.DotProduct(fwd, toAgent.NormalizedCopy());
                if (toAgent.Length <= 20f && dot >= 0.5f)
                {
                    Vec3 pushDest = a.Position + fwd * 20f;
                    pushDest.z = a.Position.z;
                    try
                    {
                        a.TeleportToPosition(pushDest);
                        if (trip.Index >= 0) a.SetActionChannel(0, trip, false);
                        pushed++;
                        BeginAgentGlow(a, SpellGlowColor.Combat);
                    }
                    catch { }
                }
                if (toAgent.Length > 10f || dot < 0.3f) continue;
                a.Health = Math.Max(0f, a.Health - 100f);
                if (a.Health <= 0f) KillAgent(a);
                hurt++;
            }
            InformationManager.DisplayMessage(new InformationMessage(
                (pushed > 0 || hurt > 0)
                    ? $"{pushed} {(pushed == 1 ? "enemy" : "enemies")} thrown back; " +
                      $"{hurt} {(hurt == 1 ? "enemy" : "enemies")} struck."
                    : "No enemies in forward cone.",
                new Color(0.9f, 0.4f, 0.1f)));
        }

        private static void Repel()
        {
            if (ActiveEffectManager.Has("Repel")) { Fizzle("Repel is already active."); return; }

            // Auto-blast in 4m radius every 2 seconds for 60 seconds
            float elapsed = 0f;
            ActiveEffectManager.Add(new ActiveEffect
            {
                Name            = "Repel",
                Duration        = 60f,
                IsMissionEffect = true,
                OnTick = dt =>
                {
                    elapsed += dt;
                    if (elapsed < 2f) return;
                    elapsed = 0f;
                    if (Player == null || Mission.Current == null) return;
                    foreach (Agent a in Enemies()
                        .Where(a => a.Position.Distance(Player.Position) <= 4f).ToList())
                    {
                        Vec3 dir = (a.Position - Player.Position).NormalizedCopy();
                        Vec3 dest = a.Position + dir * 4f;
                        dest.z = a.Position.z;
                        try { a.TeleportToPosition(dest); BeginAgentGlow(a, SpellGlowColor.Support);} catch { }
                    }
                }
            });
            InformationManager.DisplayMessage(new InformationMessage(
                "A repelling field surrounds you for 60 seconds.",
                new Color(0.7f, 0.4f, 0.9f)));
        }

        private static void Pacify()
        {
            // Enemies in forward 10m cone freeze for 5 seconds
            if (Player == null) return;
            Vec3 fwd = Player.LookDirection.NormalizedCopy();
            ActionIndexCache freeze = ActionIndexCache.Create("act_stand_1");
            int pacified = 0;
            foreach (Agent a in Enemies().ToList())
            {
                Vec3 toAgent = (a.Position - Player.Position);
                if (toAgent.Length > 10f) continue;
                float dot = Vec3.DotProduct(fwd, toAgent.NormalizedCopy());
                if (dot < 0.3f) continue;
                int idx = a.Index;
                ActiveEffectManager.Add(new ActiveEffect
                {
                    Name            = $"_pacify_{idx}",
                    Duration        = 5f,
                    IsMissionEffect = true,
                    OnTick = _ =>
                    {
                        Agent t = Mission.Current?.Agents.FirstOrDefault(x => x.Index == idx);
                        if (t != null && t.IsActive()) t.SetActionChannel(0, freeze, true);
                    }
                });
                pacified++;
                BeginAgentGlow(a, SpellGlowColor.Support);
            }
            InformationManager.DisplayMessage(new InformationMessage(
                pacified > 0 ? $"{pacified} {(pacified==1?"enemy":"enemies")} stand motionless."
                             : "No enemies in forward cone.",
                new Color(0.5f, 0.8f, 0.8f)));
        }

        private static void Accelerate()
        {
            if (Player == null) return;
            if (ActiveEffectManager.Has("_accel")) { Fizzle("Already accelerated."); return; }

            // SetMaximumSpeedLimit only CAPS speed; it cannot raise it above the agent's
            // natural base.  The actual speed multiplier lives in AgentDrivenProperties.
            // AgentDrivenProperties may be a struct, so we copy-modify-assign each tick.
            const float SpeedMult = 3f;

            ActiveEffectManager.Add(new ActiveEffect
            {
                Name            = "_accel",
                Duration        = 300f,
                IsMissionEffect = true,
                OnTick = _ =>
                {
                    if (Player == null || !Player.IsActive()) return;
                    try { Player.AgentDrivenProperties.MaxSpeedMultiplier = SpeedMult; } catch { }
                },
                OnExpire = () =>
                {
                    try { Player?.AgentDrivenProperties?.MaxSpeedMultiplier = 1f; } catch { }
                }
            });
            InformationManager.DisplayMessage(new InformationMessage(
                "You move at the speed of a horse for 5 minutes.",
                new Color(0.6f, 0.9f, 0.6f)));
        }

        private static void SevereLife()
        {
            // Instantly kill a random non-hero enemy anywhere on the field
            if (Player == null) return;
            var targets = Enemies().Where(a => !a.IsHero).ToList();
            if (targets.Count == 0) { Fizzle("No enemy targets."); return; }
            Agent target = targets[_rng.Next(targets.Count)];
            KillAgent(target);
            BeginAgentGlow(target, SpellGlowColor.Combat);
            InformationManager.DisplayMessage(new InformationMessage(
                $"Somewhere on the field, {target.Name} simply stops.",
                Color.FromUint(0xFFCC0000)));
        }

        private static void Clairvoyance()
        {
            // List enemy heroes/lords visible on the world map
            var lords = Hero.AllAliveHeroes
                .Where(h => h.IsLord && h.MapFaction != Hero.MainHero?.MapFaction
                            && h.IsAlive && h.PartyBelongedTo != null)
                .Take(8)
                .ToList();

            InformationManager.DisplayMessage(new InformationMessage(
                "Your sight extends across the land:", new Color(0.6f, 0.8f, 1f)));
            if (lords.Count == 0)
                InformationManager.DisplayMessage(new InformationMessage(
                    "  No enemy parties visible.", new Color(0.5f, 0.5f, 0.7f)));
            else
                foreach (var h in lords)
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"  {h.Name} [{h.MapFaction?.Name}] — {h.PartyBelongedTo?.MemberRoster?.TotalManCount ?? 0} troops",
                        new Color(0.5f, 0.7f, 1f)));
        }

        private static void Crush()
        {
            // Deal direct damage in forward 10m cone — twice Hurt's damage (200f) — stagger survivors
            if (Player == null) return;
            Vec3 fwd = Player.LookDirection.NormalizedCopy();
            ActionIndexCache stagger = ActionIndexCache.Create("act_struck_from_back_medium_left_staff");
            int crushed = 0;
            int killed = 0;
            foreach (Agent a in Enemies().ToList())
            {
                Vec3 toAgent = (a.Position - Player.Position);
                if (toAgent.Length > 14f) continue;
                float dot = Vec3.DotProduct(fwd, toAgent.NormalizedCopy());
                if (dot < 0.2f) continue;
                a.Health = Math.Max(0f, a.Health - 120f);
                if (a.Health <= 0f) { KillAgent(a); killed++; }
                else if (stagger.Index >= 0) { try { a.SetActionChannel(0, stagger, false); } catch { } }
                crushed++;
                BeginAgentGlow(a, SpellGlowColor.Combat);
            }
            InformationManager.DisplayMessage(new InformationMessage(
                crushed > 0 ? $"{crushed} {(crushed==1?"enemy":"enemies")} crushed; {killed} killed outright."
                            : "No enemies in crushing range.",
                new Color(1f, 0.3f, 0.1f)));
        }

        private static void Scatter()
        {
            if (Player == null) return;
            var targets = Enemies()
                .Where(a => a.Position.Distance(Player.Position) <= 15f)
                .ToList();
            if (targets.Count == 0) { Fizzle("No enemies within range."); return; }

            foreach (Agent a in targets)
            {
                float angle = (float)(_rng.NextDouble() * Math.PI * 2);
                float dist  = 8f + (float)(_rng.NextDouble() * 12f);
                Vec3 dest   = Player.Position + new Vec3(
                    (float)Math.Cos(angle) * dist,
                    (float)Math.Sin(angle) * dist, 0f);
                try { a.TeleportToPosition(dest); BeginAgentGlow(a, SpellGlowColor.Support);} catch { }
            }
            InformationManager.DisplayMessage(new InformationMessage(
                $"{targets.Count} {(targets.Count == 1 ? "enemy" : "enemies")} scattered.",
                new Color(0.8f, 0.4f, 0.1f)));
        }

        private static void BreakSpirits()
        {
            if (Agent.Main == null || Mission.Current == null) return;


            var targets = Mission.Current.Agents
                .Where(a => a.IsEnemyOf(Agent.Main) && a.Position.Distance(Agent.Main.Position) <= 14f)
                .ToList();

            if (targets.Count == 0) return;

            int shaken = 0;
            foreach (Agent a in targets)
            {
                if (a.IsActive())
                {

                    a.SetMorale(0f);
                    a.Retreat(true);
                    a.InvalidateTargetAgent();

                    shaken++;
                    BeginAgentGlow(a, SpellGlowColor.Support);
                }
            }

            // 5. Visual/Audio feedback
            InformationManager.DisplayMessage(new InformationMessage(
                $"{shaken} enemies are overcome with terror and flee!", 
                new Color(0.3f, 0.6f, 1f)));
        }


        private static void Devour()
        {
            if (Player == null || MobileParty.MainParty == null) return;
            var roster = MobileParty.MainParty.MemberRoster;
            var wounded = roster.GetTroopRoster()
                .Where(e => e.Character != null && e.Character.HeroObject == null && e.WoundedNumber > 0)
                .OrderBy(e => e.Character.Level)
                .FirstOrDefault();
            if (wounded.Character == null) { Fizzle("No wounded soldiers to devour."); return; }

            roster.RemoveTroop(wounded.Character, 1);
            float gain = Math.Min(25f, Player.HealthLimit - Player.Health);
            Player.Health = Math.Min(Player.Health + 25f, Player.HealthLimit);

            InformationManager.DisplayMessage(new InformationMessage(
                $"You consume {wounded.Character.Name}. +{gain:F0} HP. They did not scream.",
                Color.FromUint(0xFF880000)));
        }

        private static void Featherfall()
        {
            if (Player == null) return;
            if (ActiveEffectManager.Has("Featherfall")) 
            { 
                Fizzle("Featherfall already active."); 
                return; 
            }

            // Zapamiętujemy startową wysokość
            float lastZ = Player.Position.z;

            ActiveEffectManager.Add(new ActiveEffect
            {
                Name = "Featherfall",
                Duration = 30f,
                IsMissionEffect = true,
                OnTick = dt =>
                {
                    if (Player == null || !Player.IsActive()) return;

                    float currentZ = Player.Position.z;
                    float dropped = lastZ - currentZ;

                    if (dropped > 0.1f)
                    {
                        Vec3 slowedPos = Player.Position;
                        slowedPos.z = lastZ - 0.1f;

                        try 
                        { 
                            Player.TeleportToPosition(slowedPos);
                        } 
                        catch { }
                        
                        lastZ = slowedPos.z;
                    }
                    else
                    {
                        lastZ = currentZ;
                    }
                }
            });

            InformationManager.DisplayMessage(new InformationMessage(
                "Gravity loosens its claim on you for 30 seconds.",
                new Color(0.6f, 0.8f, 1f)));
        }

       private static void Weightless()
        {
            if (Player == null || Mission.Current == null) return;

            var targets = Mission.Current.Agents
                .Where(a => a != null && a != Player && a.IsActive() && !a.IsMount &&
                            a.Position.Distance(Player.Position) <= 10f)
                .ToList();

            if (targets.Count == 0) { Fizzle("No agents within range."); return; }

            foreach (Agent a in targets)
            {
                try 
                { 
                    Vec3 newPos = a.Position;
                    newPos.z += 1.5f; 
                    a.TeleportToPosition(newPos);

                    a.SetActionChannel(0, ActionIndexCache.Create("act_strike_fall_back"), true);
                    BeginAgentGlow(a, SpellGlowColor.Combat);

                } 
                catch { }
            }

            int enemies = targets.Count(a => a.Team != Player.Team);
            int allies  = targets.Count(a => a.Team == Player.Team);

            InformationManager.DisplayMessage(new InformationMessage(
                $"Gravity fails. {enemies} enemies and {allies} allies are cast upward.",
                new Color(0.7f, 0.5f, 1f)));
        }

        // Mark anchor — stores the player's position for recall
        private static Vec3? _markAnchor = null;

        private static void RemovedSpell_0()
        {
            if (Player == null) return;
            var targets = Enemies()
                .Where(a => a.Position.Distance(Player.Position) <= 12f)
                .ToList();
            if (targets.Count == 0) { Fizzle("No enemies within range."); return; }

            foreach (Agent a in targets)
            {
                // Lift enemies 4m up — they take fall damage on landing
                Vec3 lifted = a.Position;
                lifted.z += 4f;
                try { a.TeleportToPosition(lifted); } catch { }
            }

            InformationManager.DisplayMessage(new InformationMessage(
                $"{targets.Count} {(targets.Count == 1 ? "enemy" : "enemies")} lifted and dropped.",
                new Color(0.5f, 0.3f, 0.8f)));
        }

        private static void Mark()
        {
            if (Player == null) return;

            if (_markAnchor == null)
            {
                // First cast — set anchor
                _markAnchor = Player.Position;
                InformationManager.DisplayMessage(new InformationMessage(
                    "Position marked. Cast Mark again to return here.",
                    new Color(0.4f, 0.7f, 1f)));
            }
            else
            {
                // Second cast — teleport back
                Vec3 dest = _markAnchor.Value;
                _markAnchor = null;
                Player.TeleportToPosition(dest);
                InformationManager.DisplayMessage(new InformationMessage(
                    "You return to where you left yourself.",
                    new Color(0.4f, 0.7f, 1f)));
            }
        }

        private static void Unname()
        {
            if (Player == null || Mission.Current == null) return;

            // Find nearest enemy hero
            Agent lordAgent = Mission.Current.Agents
                .Where(a => a.IsActive() && a.IsHero && !a.IsMount &&
                            a.Team != Player.Team &&
                            a.Position.Distance(Player.Position) <= 25f)
                .OrderBy(a => a.Position.Distance(Player.Position))
                .FirstOrDefault();

            if (lordAgent == null) { Fizzle("No enemy lord within 25m."); return; }

            // Freeze all non-hero agents near the lord for 10 seconds
            ActionIndexCache freeze = ActionIndexCache.Create("act_stand_1");
            int frozen = 0;
            foreach (Agent a in Mission.Current.Agents
                .Where(a => !a.IsHero && a.IsActive() && !a.IsMount &&
                            a.Team == lordAgent.Team &&
                            a.Position.Distance(lordAgent.Position) <= 12f)
                .ToList())
            {
                int idx = a.Index;
                ActiveEffectManager.Add(new ActiveEffect
                {
                    Name            = $"_unname_{idx}",
                    Duration        = 10f,
                    IsMissionEffect = true,
                    OnTick = _ =>
                    {
                        Agent t = Mission.Current?.Agents.FirstOrDefault(x => x.Index == idx);
                        if (t != null && t.IsActive()) t.SetActionChannel(0, freeze, true);
                    }
                });
                frozen++;
                BeginAgentGlow(a, SpellGlowColor.Support);
            }

            InformationManager.DisplayMessage(new InformationMessage(
                $"{lordAgent.Name}'s soldiers forget themselves. {frozen} troops idle for 10 seconds.",
                new Color(0.6f, 0.4f, 0.9f)));
        }

        private static void HollowName()
        {
            // Find a nearby enemy lord and drain their clan's renown
            Hero target = Hero.AllAliveHeroes
                .Where(h => h.IsLord && h.MapFaction != Hero.MainHero?.MapFaction
                            && h.Clan != null && h.IsAlive)
                .OrderBy(h => MBRandom.RandomInt(100)) // random pick, not nearest
                .FirstOrDefault();

            if (target == null)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "There is no one worth hollowing right now.",
                    Color.FromUint(0xFF884444)));
                return;
            }

            try { Hero.MainHero?.Clan?.AddRenown(-5f); }  catch { }
            try { target.Clan?.AddRenown(-25f); }         catch { }

            InformationManager.DisplayMessage(new InformationMessage(
                $"You pour part of yourself into nothing. {target.Name}'s name grows quieter in the world. " +
                $"(-5 your renown, -25 theirs)",
                new Color(0.6f, 0.2f, 0.7f)));
        }

        private static int _longRoadDaysRemaining = 0;
        public static int LongRoadDaysRemaining => _longRoadDaysRemaining;
        public static void SetLongRoadDays(int days) => _longRoadDaysRemaining = days;

        private static void LongRoad()
        {
            _longRoadDaysRemaining = 10;
            // Morale boost — Bannerlord's march speed scales with morale
            if (MobileParty.MainParty != null)
                MobileParty.MainParty.RecentEventsMorale += 30f;

            InformationManager.DisplayMessage(new InformationMessage(
                "The road opens before your party. +30 morale. Your warband moves with unusual purpose for 10 days.",
                new Color(0.5f, 0.8f, 0.5f)));
        }

        // Called from OnDailyTick to tick down Long Road
        public static void TickLongRoad()
        {
            if (_longRoadDaysRemaining <= 0) return;
            _longRoadDaysRemaining--;
            if (MobileParty.MainParty != null)
                MobileParty.MainParty.RecentEventsMorale += 5f; // daily top-up
            if (_longRoadDaysRemaining == 0)
                InformationManager.DisplayMessage(new InformationMessage(
                    "The road's favour fades. Your pace returns to normal.",
                    new Color(0.4f, 0.6f, 0.4f)));
        }

        // ── New map spells ───────────────────────────────────────────────────

        private static void SinisterWill()
        {
            if (MobileParty.MainParty == null) return;
            CampaignVec2 playerPos = MobileParty.MainParty.Position;
            Settlement target = Settlement.All
                .Where(s => s.IsVillage)
                .OrderBy(s => {
                    try { Vec2 sp = s.GetPosition2D; float dx = sp.x - playerPos.X, dy = sp.y - playerPos.Y; return dx*dx + dy*dy; }
                    catch { return float.MaxValue; }
                })
                .FirstOrDefault();
            if (target == null) { Fizzle("No village is within reach."); return; }
            try { if (target.Village != null) target.Village.Hearth = Math.Max(10f, target.Village.Hearth * 0.5f); }
            catch { }
            InformationManager.DisplayMessage(new InformationMessage(
                $"{target.Name} withers. Its hearths grow cold and its fields grow thin.",
                new Color(0.5f, 0.1f, 0.3f)));
        }

        private static void Calling()
        {
            if (MobileParty.MainParty == null) return;
            CharacterObject recruit =
                MBObjectManager.Instance.GetObject<CharacterObject>("imperial_recruit")
             ?? MBObjectManager.Instance.GetObject<CharacterObject>("imperial_levy_infantryman")
             ?? MBObjectManager.Instance.GetObject<CharacterObject>("imperial_levy");
            if (recruit == null)
            {
                // Runtime fallback: find any tier-1 empire troop
                foreach (CharacterObject c in CharacterObject.All)
                {
                    if (!c.IsHero && c.Tier == 1 && c.Culture?.StringId?.Contains("empire") == true)
                    { recruit = c; break; }
                }
            }
            if (recruit == null) { Fizzle("The call finds no ears."); return; }
            int count = 20 + _rng.Next(41); // 20-60
            try { MobileParty.MainParty.MemberRoster.AddToCounts(recruit, count); }
            catch { Fizzle("The call was heard but no one came."); return; }
            InformationManager.DisplayMessage(new InformationMessage(
                $"{count} Imperial soldiers answer the call and fall in behind you.",
                new Color(0.8f, 0.6f, 0.2f)));
        }

        // ── Aura of Hate state ────────────────────────────────────────────────
        // Drains village militia to zero — no soldiers means no resistance.
        // Militia regenerates naturally via the game's daily tick, so no restore needed.
        private static bool _auraOfHateActive = false;
        private static CampaignTime _auraOfHateExpiry;

        public static bool IsAuraOfHateActive =>
            _auraOfHateActive && Campaign.Current != null && CampaignTime.Now < _auraOfHateExpiry;

        // Called from OnApplicationTick every frame — cheap when inactive.
        public static void TickAuraOfHate()
        {
            if (!_auraOfHateActive || Campaign.Current == null) return;
            if (CampaignTime.Now < _auraOfHateExpiry) return;
            _auraOfHateActive = false;
            InformationManager.DisplayMessage(new InformationMessage(
                "The aura of hate fades. Courage returns to the land.",
                new Color(0.5f, 0.2f, 0.3f)));
        }

        // set_Militia exists in the binary but is not public — reach it via reflection.
        private static MethodInfo _setMilitiaSetter;
        private static bool _setMilitiaResolved;

        internal static bool TrySetMilitia(Village v, float value)
        {
            if (!_setMilitiaResolved)
            {
                _setMilitiaResolved = true;
                PropertyInfo prop = typeof(Village).GetProperty("Militia",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                _setMilitiaSetter = prop?.GetSetMethod(nonPublic: true);
            }
            if (_setMilitiaSetter == null) return false;
            try { _setMilitiaSetter.Invoke(v, new object[] { value }); return true; }
            catch { return false; }
        }

        private static void AuraOfHate()
        {
            if (Hero.MainHero == null) return;
            if (IsAuraOfHateActive) { Fizzle("Fear already grips these lands."); return; }

            int affected = 0;
            foreach (Settlement s in Settlement.All)
            {
                if (s.Village == null) continue;
                if (TrySetMilitia(s.Village, 0f)) affected++;
            }

            _auraOfHateActive = true;
            _auraOfHateExpiry = CampaignTime.Now + CampaignTime.Hours(3);
            string reach = affected > 0
                ? $"Your hatred radiates outward. {affected} villages have been stripped of their defenders — none will resist for three hours."
                : "Your hatred radiates outward. For three hours, no villager dares raise a hand against you.";
            InformationManager.DisplayMessage(new InformationMessage(reach, new Color(0.7f, 0.1f, 0.2f)));
        }

        // ── Visual feedback system ───────────────────────────────────────────
        // Layers (all wrapped in try/catch so nothing here can crash the game):
        //   1. Cast animation  — school-specific, with confirmed-working fallbacks
        //   2. Particle burst  — vanilla particle systems at caster position
        //   3. Screen bloom    — fading flash via reflection (player casts only)
        //   4. Agent flinch    — enemies + allies react differently per school
        //   5. Positional sound
        // ────────────────────────────────────────────────────────────────────
        // ── Agent contour glow ───────────────────────────────────────────────
        private static readonly List<(Agent agent, float remaining)> _glowTimers
            = new List<(Agent, float)>();

        public static void TickGlows(float dt)
        {
            for (int i = _glowTimers.Count - 1; i >= 0; i--)
            {
                float t = _glowTimers[i].remaining - dt;
                if (t <= 0f)
                {
                    try { _glowTimers[i].agent?.AgentVisuals?.GetEntity()
                              ?.SetContourColor(null, false); } catch { }
                    _glowTimers.RemoveAt(i);
                }
                else _glowTimers[i] = (_glowTimers[i].agent, t);
            }
        }

        public static void BeginAgentGlow(Agent agent, SpellGlowColor color)
        {
            try
            {
                uint col = color == SpellGlowColor.Combat  ? 0xFFFF4400u
                         : color == SpellGlowColor.Healing ? 0xFFFFD700u
                         :                                   0xFF4A83FFu;
                agent.AgentVisuals?.GetEntity()?.SetContourColor(col, true);
                _glowTimers.Add((agent, 3f));
            }
            catch { }
        }

        public static void CastGlow(Agent caster, SpellGlowColor glowColor)
        {
            if (caster == null) return;
            try
            {
                BeginAgentGlow(caster, glowColor);
                PlayCastAnimation(caster, glowColor);
                TrySpawnCastParticle(caster.Position, glowColor);
                FlinchAgentsNear(caster, glowColor);
                TryCastSound(caster.Position, glowColor);
            }
            catch { }
        }

        public static SpellGlowColor ResolveGlowColor(SpellEntry spell)
        {
            if (spell == null) return SpellGlowColor.Combat;

            switch (spell.BookTag)
            {
                case "MEMORY":
                case "MARK":
                case "REJUVENATE":
                case "RESTORE":
                case "FEATHERFALL":
                case "INSPIRE":
                case "MENDING":
                case "ACCELERATE":
                case "LONG_ROAD":
                case "CLAIRVOYANCE":
                case "RELOCATE":
                    return SpellGlowColor.Healing;

                case "CHARM":
                case "BANE":
                case "SUPPRESS":
                case "HALT":
                case "SHROUDING":
                case "SHROUD":
                case "DISMOUNT":
                case "STOP_ARROWS":
                case "PACIFY":
                case "CONFUSE":
                case "UNNAME":
                case "CALLING":
                case "AURA_OF_HATE":
                case "BREAK_SPIRITS":
                case "REPEL":
                case "ENRAGE":
                case "WEIGHTLESS":
                case "HOLLOW_NAME":
                case "SINISTER_WILL":
                    return SpellGlowColor.Support;

                default:
                    return SpellGlowColor.Combat;
            }
        }

        public static bool IsTournamentMission()
        {
            try
            {
                if (Mission.Current == null) return false;

                var modeProp = Mission.Current.GetType().GetProperty("Mode");
                object mode = modeProp?.GetValue(Mission.Current);
                string modeText = mode?.ToString() ?? "";
                return modeText.IndexOf("tournament", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { }

            return false;
        }

        public static void DisqualifyTournamentCaster(Agent agent, Hero hero, string spellName)
        {
            try
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{hero?.Name?.ToString() ?? "A contestant"} is disqualified for casting {spellName} in the tournament.",
                    new Color(0.95f, 0.15f, 0.15f)));
            }
            catch { }

            try
            {
                if (agent != null && agent.IsActive())
                    agent.Health = 0f;
            }
            catch { }
        }

        // School-appropriate animations with a confirmed-working fallback at the end.
        private static void PlayCastAnimation(Agent caster, SpellGlowColor glowColor)
        {
            string[] candidates;
            switch (glowColor)
            {
                case SpellGlowColor.Combat:
                    candidates = new[]
                    {
                        "act_yield_hard",
                        "act_pickup_boulder_begin",
                        "act_struck_from_back_medium_left_staff"  // confirmed
                    };
                    break;
                case SpellGlowColor.Healing:
                    candidates = new[]
                    {
                        "act_pickup_boulder_end",
                        "act_struck_from_front_light"             // confirmed
                    };
                    break;
                default: // Support/control
                    candidates = new[]
                    {
                        "act_thrust_staff_wielder",
                        "act_struck_from_front_light"             // confirmed
                    };
                    break;
            }

            foreach (string name in candidates)
            {
                try
                {
                    ActionIndexCache cache = ActionIndexCache.Create(name);
                    if (cache.Index < 0) continue;
                    caster.SetActionChannel(0, cache, false);
                    return;
                }
                catch { }
            }
        }

        // Spawn a vanilla particle burst at the cast origin.
        // Uses reflection so the code compiles even if the engine API changes.
        private static void TrySpawnCastParticle(Vec3 position, SpellGlowColor color)
        {
            try
            {
                var scene = Mission.Current?.Scene;
                if (scene == null) return;

                // Pick a vanilla particle that fits the spell school
                string particleName = color == SpellGlowColor.Combat
                    ? "psys_game_blood_burst_a"
                    : color == SpellGlowColor.Healing
                        ? "psys_game_dust_fall"
                        : "psys_game_throw_stone";

                // Resolve ParticleSystemManager at runtime — avoids a hard engine API dependency
                Type psmType = Type.GetType(
                    "TaleWorlds.Engine.ParticleSystemManager, TaleWorlds.Engine");
                if (psmType == null) return;

                MethodInfo getId = psmType.GetMethod("GetRuntimeIdByName",
                    BindingFlags.Public | BindingFlags.Static);
                if (getId == null) return;

                object idObj = getId.Invoke(null, new object[] { particleName });
                if (idObj == null || (int)idObj < 0) return;

                MethodInfo burst = scene.GetType().GetMethod("CreateBurstParticle",
                    BindingFlags.Public | BindingFlags.Instance);
                if (burst == null) return;

                burst.Invoke(scene, new object[] { idObj, new MatrixFrame(Mat3.Identity, position) });
            }
            catch { }
        }

        // Bloom flash that fades over 0.4 s.  Uses reflection so we handle both
        // SetBloomStrength(float) and SetBloom(bool) variants of the engine API.
        private static int _activeFlashCount = 0;

        private static void TryBeginScreenFlash()
        {
            if (Mission.Current == null) return;
            int flashId = ++_activeFlashCount;
            float elapsed = 0f;
            const float duration = 0.4f;

            ActiveEffectManager.Add(new ActiveEffect
            {
                Name            = $"_sflash_{flashId}",
                Duration        = duration,
                IsMissionEffect = true,
                OnTick = dt =>
                {
                    elapsed += dt;
                    float t = Math.Max(0f, 1f - elapsed / duration);
                    TrySetBloomStrength(t * 2.5f);
                },
                OnExpire = () =>
                {
                    _activeFlashCount = Math.Max(0, _activeFlashCount - 1);
                    if (_activeFlashCount == 0)
                        TrySetBloomStrength(0f);
                }
            });
        }

        private static void TrySetBloomStrength(float strength)
        {
            try
            {
                var scene = Mission.Current?.Scene;
                if (scene == null) return;
                Type t = scene.GetType();

                // Prefer the float overload (SetBloomStrength confirmed working per code comment)
                MethodInfo m = t.GetMethod("SetBloomStrength",
                    BindingFlags.Public | BindingFlags.Instance,
                    null, new[] { typeof(float) }, null);
                if (m != null) { m.Invoke(scene, new object[] { strength }); return; }

                // Fall back to the bool toggle version
                m = t.GetMethod("SetBloom",
                    BindingFlags.Public | BindingFlags.Instance,
                    null, new[] { typeof(bool) }, null);
                if (m != null) m.Invoke(scene, new object[] { strength > 0.05f });
            }
            catch { }
        }

        // Flinch nearby agents — enemies react to combat/support; allies react to healing.
        private static void FlinchAgentsNear(Agent caster, SpellGlowColor glowColor)
        {
            if (Mission.Current == null) return;

            // Combat: enemies flinch hard in a wide radius
            // Healing: allies pulse with a lighter animation in medium radius
            // Support: all agents flinch lightly in a small radius
            float radius;
            bool hitEnemies, hitAllies;
            string enemyAnim, allyAnim;

            switch (glowColor)
            {
                case SpellGlowColor.Combat:
                    radius     = 10f;
                    hitEnemies = true;
                    hitAllies  = false;
                    enemyAnim  = "act_struck_from_back_medium_left_staff";
                    allyAnim   = "act_struck_from_front_light";
                    break;
                case SpellGlowColor.Healing:
                    radius     = 8f;
                    hitEnemies = false;
                    hitAllies  = true;
                    enemyAnim  = "act_struck_from_front_light";
                    allyAnim   = "act_pickup_boulder_end";
                    break;
                default: // Support
                    radius     = 6f;
                    hitEnemies = true;
                    hitAllies  = true;
                    enemyAnim  = "act_struck_from_front_light";
                    allyAnim   = "act_struck_from_front_light";
                    break;
            }

            ActionIndexCache enemyCache = ActionIndexCache.Create(enemyAnim);
            ActionIndexCache allyCache  = ActionIndexCache.Create(allyAnim);

            foreach (Agent agent in Mission.Current.Agents.ToList())
            {
                if (!agent.IsActive() || agent.IsMount || agent == caster) continue;
                if (agent.Position.Distance(caster.Position) > radius) continue;

                bool isEnemy = caster.Team != null && agent.Team != caster.Team;
                bool isAlly  = caster.Team != null && agent.Team == caster.Team;

                try
                {
                    if (isEnemy && hitEnemies && enemyCache.Index >= 0)
                        agent.SetActionChannel(0, enemyCache, false);
                    else if (isAlly && hitAllies && allyCache.Index >= 0)
                        agent.SetActionChannel(0, allyCache, false);
                }
                catch { }
            }
        }

        // Cache of the SoundEvent type resolved once at runtime.
        private static Type _soundEventType;
        private static MethodInfo _soundGetId;

        private static bool TryResolveSoundEvent()
        {
            if (_soundGetId != null) return true;
            try
            {
                foreach (System.Reflection.Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    foreach (string candidate in new[]
                        { "TaleWorlds.MountAndBlade.SoundEvent",
                          "TaleWorlds.Engine.SoundEvent" })
                    {
                        Type t = asm.GetType(candidate);
                        if (t == null) continue;
                        MethodInfo m = t.GetMethod("GetEventIdFromString",
                            BindingFlags.Public | BindingFlags.Static);
                        if (m == null) continue;
                        _soundEventType = t;
                        _soundGetId     = m;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        // Positional sound at the cast origin.  Tries school-appropriate events first.
        private static void TryCastSound(Vec3 position, SpellGlowColor color)
        {
            if (Mission.Current == null) return;
            if (!TryResolveSoundEvent()) return;

            string[] candidates = color == SpellGlowColor.Combat
                ? new[]
                {
                    "event:/mission/ambient/detail/wind_hit",
                    "event:/mission/ambient/detail/wind_medium",
                    "event:/ui/panels/open"
                }
                : color == SpellGlowColor.Healing
                ? new[]
                {
                    "event:/ui/notifications/quest_update",
                    "event:/ui/panels/open"
                }
                : new[]
                {
                    "event:/ui/notifications/quest_update",
                    "event:/ui/panels/open"
                };

            foreach (string path in candidates)
            {
                try
                {
                    object idObj = _soundGetId.Invoke(null, new object[] { path });
                    if (idObj == null) continue;
                    int soundId = (int)idObj;
                    if (soundId < 0) continue;
                    Mission.Current.MakeSound(soundId, position, false, false, -1, -1);
                    return;
                }
                catch { }
            }
        }

        private static void FlashAgentsNear(Vec3 position, float radius)
        {
            if (Mission.Current == null) return;
            ActionIndexCache flinch = ActionIndexCache.Create("act_struck_from_front_light");
            foreach (Agent agent in Mission.Current.Agents.ToList())
            {
                if (!agent.IsActive() || agent.IsMount) continue;
                if (agent.Position.Distance(position) > radius) continue;
                try { agent.SetActionChannel(0, flinch, false); }
                catch { }
            }
        }
    }

    // =========================================================================
    // 9. MAGE LORD REGISTRY
    //    Tracks which heroes across the world hold the Gift.
    //    Enforces 1–2 Mage Lords per faction, handles death and respawn.
    // =========================================================================
    public static class MageLordRegistry
    {
        private const int MaxPerFaction = 2;

        private static readonly HashSet<string>         _mageLordIds  = new HashSet<string>();
        private static readonly Dictionary<string, int> _respawnHours = new Dictionary<string, int>();
        private static bool   _seeded;
        private static readonly Random _rng = new Random();

        // Campaign map casting — lords cast roughly once per week
        private static readonly Dictionary<string, int> _campaignCooldownDays = new Dictionary<string, int>();

        public static bool IsMageLord(Hero hero) =>
            hero != null && _mageLordIds.Contains(hero.StringId);

        public static void ApplyMageLordAgeCost(Hero lord, int dayCost)
        {
            if (lord == null || dayCost <= 0) return;

            float multiplier = 1.4f;
            try
            {
                int valor = lord.GetTraitLevel(DefaultTraits.Valor);
                if (valor >= 2) multiplier = 1.9f;
                else if (valor >= 1) multiplier = 1.6f;
                else if (valor <= -2) multiplier = 1.15f;
                else if (valor <= -1) multiplier = 1.25f;
            }
            catch { }

            lord.SetBirthDay(lord.BirthDay - CampaignTime.Years((float)dayCost * multiplier / 252f));
        }

        public static void DailyAgeDrift()
        {
            foreach (string id in _mageLordIds.ToList())
            {
                Hero lord = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == id);
                if (lord == null || !lord.IsAlive) continue;

                float dailyDayCost = 2f;
                try
                {
                    int valor = lord.GetTraitLevel(DefaultTraits.Valor);
                    if (valor >= 2) dailyDayCost = 3.5f;
                    else if (valor >= 1) dailyDayCost = 3.0f;
                    else if (valor <= -2) dailyDayCost = 1.5f;
                    else if (valor <= -1) dailyDayCost = 1.75f;
                }
                catch { }

                lord.SetBirthDay(lord.BirthDay - CampaignTime.Years(dailyDayCost / 252f));
            }
        }

        // Called from OnDailyTick — gives each living Mage Lord a chance to cast a map spell
        public static string PerformAseraiDarkBargain(Hero lord)
        {
            if (lord == null) return null;

            if (lord.Age < 40f)
            {
                var rival = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.MapFaction != lord.MapFaction && h.IsAlive)
                    .OrderBy(h => _rng.Next())
                    .FirstOrDefault();
                if (rival == null) return null;

                rival.SetBirthDay(rival.BirthDay - CampaignTime.Years(10f / 252f));
                return $"{rival.Name} feels years settle onto their shoulders uninvited.";
            }

            MobileParty party = lord.PartyBelongedTo;
            if (party == null) return null;

            int taken = 0;

            while (party.PrisonRoster.TotalManCount > 0 && lord.Age > 20f)
            {
                var prisoner = party.PrisonRoster.GetTroopRoster()
                    .Where(e => e.Character != null && e.Number > 0)
                    .FirstOrDefault();
                if (prisoner.Character == null) break;

                party.PrisonRoster.RemoveTroop(prisoner.Character, 1);
                lord.SetBirthDay(lord.BirthDay + CampaignTime.Years(30f / 252f));
                if (lord.Age < 20f)
                    lord.SetBirthDay(CampaignTime.Now - CampaignTime.Years(20));
                party.RecentEventsMorale -= 5f;
                taken++;
            }

            while (party.MemberRoster.TotalManCount > 5 && lord.Age > 20f)
            {
                var wounded = party.MemberRoster.GetTroopRoster()
                    .Where(e => e.WoundedNumber > 0)
                    .OrderByDescending(e => e.WoundedNumber)
                    .FirstOrDefault();

                var sacrifice = wounded.Character != null
                    ? wounded
                    : party.MemberRoster.GetTroopRoster()
                        .Where(e => e.Number > 0)
                        .OrderBy(e => e.Number)
                        .FirstOrDefault();

                if (sacrifice.Character == null) break;

                party.MemberRoster.RemoveTroop(sacrifice.Character, 1);
                lord.SetBirthDay(lord.BirthDay + CampaignTime.Years(60f / 252f));
                if (lord.Age < 20f)
                    lord.SetBirthDay(CampaignTime.Now - CampaignTime.Years(20));
                party.RecentEventsMorale -= 20f;
                taken++;
            }

            if (taken == 0) return null;

            return $"{lord.Name} pays in lives and drags their age down to {(int)lord.Age}.";
        }

        public static void DailyMapCast()
        {
            foreach (string id in _mageLordIds.ToList())
            {
                Hero hero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == id);
                if (hero == null || hero.MapFaction == null) continue;

                // Tick cooldown
                if (_campaignCooldownDays.TryGetValue(id, out int cd) && cd > 0)
                { _campaignCooldownDays[id] = cd - 1; continue; }

                // 20% chance per day when cooldown is 0 (averages ~5 days between casts)
                if (_rng.Next(100) >= 20) continue;

                _campaignCooldownDays[id] = 6 + _rng.Next(4); // 6-9 day cooldown after cast

                CastMapSpell(hero);
            }
        }

        private class MapSpellEntry
        {
            public string SpellName;
            public int    DayCost;
            public Func<string> Action; // returns effect message, null = cancel
        }

        private static void CastMapSpell(Hero lord)
        {
            string faction = (lord.MapFaction as Kingdom)?.StringId?.ToLower() ?? "";
            var pool = BuildMapSpellPool(lord, faction);
            if (pool.Count == 0) return;

            MapSpellEntry chosen = pool[_rng.Next(pool.Count)];
            string msg = null;
            try { msg = chosen.Action(); } catch { }
            if (msg == null) return;

            ApplyMageLordAgeCost(lord, chosen.DayCost);

            InformationManager.DisplayMessage(new InformationMessage(
                $"✦ {lord.Name} channels {chosen.SpellName}. {msg} ✦",
                new Color(0.6f, 0.1f, 0.7f)));
        }

        private static List<MapSpellEntry> BuildMapSpellPool(Hero lord, string faction)
        {
            var pool = new List<MapSpellEntry>();

            switch (faction)
            {
                case "battania":
                    // Inspire — morale boost own party
                    if (lord.PartyBelongedTo != null)
                        pool.Add(new MapSpellEntry { SpellName="Inspire", DayCost=20, Action=() =>
                        {
                            lord.PartyBelongedTo.RecentEventsMorale += 15f;
                            return $"{lord.Name}'s warband surges with renewed purpose.";
                        }});
                    // Mending — heal wounded in own party
                    if (lord.PartyBelongedTo != null)
                        pool.Add(new MapSpellEntry { SpellName="Mending", DayCost=15, Action=() =>
                        {
                            int healed = 0;
                            foreach (var e in lord.PartyBelongedTo.MemberRoster.GetTroopRoster().ToList())
                            {
                                int h = Math.Min(e.WoundedNumber, 3);
                                if (h > 0)
                                {
                                    lord.PartyBelongedTo.MemberRoster.AddToCounts(e.Character, 0, false, -h);
                                    healed += h;
                                }
                            }
                            return healed > 0
                                ? $"{lord.Name} tends the wounds. {healed} soldiers recover."
                                : null;
                        }});
                    // Rejuvenate — add food
                    pool.Add(new MapSpellEntry { SpellName="Rejuvenate", DayCost=10, Action=() =>
                    {
                        ItemObject grain = MBObjectManager.Instance.GetObject<ItemObject>("grain");
                        if (grain == null || lord.PartyBelongedTo == null) return null;
                        lord.PartyBelongedTo.ItemRoster.AddToCounts(grain, 5);
                        return $"Five measures of grain appear in {lord.Name}'s stores.";
                    }});
                    break;

                case "aserai":
                    // Charm — raise the lord's renown
                    pool.Add(new MapSpellEntry { SpellName="Charm", DayCost=12, Action=() =>
                    {
                        try 
                        { 
                            lord.Clan?.AddRenown(5f); 
                            ChangeClanInfluenceAction.Apply(lord.Clan, 5f);
                            return $"{lord.Name}'s charm spreads through the land. Their reputation grows.";
                        } 
                        catch 
                        { 
                            return $"{lord.Name}'s charm flows, but has little effect.";
                        }
                    }});
                    // Sinister Will � drain the nearest enemy village of hearth
                    if (lord.PartyBelongedTo != null)
                        pool.Add(new MapSpellEntry { SpellName="Sinister Will", DayCost=25, Action=() =>
                        {
                            CampaignVec2 lordPos = lord.PartyBelongedTo.Position;
                            Settlement nearest = Settlement.All
                                .Where(s => s.IsVillage && s.MapFaction != lord.MapFaction)
                                .OrderBy(s => {
                                    try { Vec2 sp = s.GetPosition2D; float dx = sp.x - lordPos.X, dy = sp.y - lordPos.Y; return dx*dx + dy*dy; }
                                    catch { return float.MaxValue; }
                                })
                                .FirstOrDefault();
                            if (nearest == null) return null;
                            try { if (nearest.Village != null) nearest.Village.Hearth = Math.Max(10f, nearest.Village.Hearth * 0.5f); }
                            catch { }
                            return $"{nearest.Name} withers under {lord.Name}'s will.";
                        }});
                    // Aura of Hate � drain militia from a random enemy village
                    pool.Add(new MapSpellEntry { SpellName="Aura of Hate", DayCost=45, Action=() =>
                    {
                        Settlement target = Settlement.All
                            .Where(s => s.IsVillage && s.MapFaction != lord.MapFaction && s.Village != null && s.Village.Militia > 0f)
                            .OrderBy(s => _rng.Next()).FirstOrDefault();
                        if (target == null) return null;
                        if (!SpellEffects.TrySetMilitia(target.Village, 0f)) return null;
                        return $"Fear spreads from {lord.Name}. The defenders of {target.Name} disperse.";
                    }});
                    // Hollow Name � drain an enemy clan
                    pool.Add(new MapSpellEntry { SpellName="Hollow Name", DayCost=20, Action=() =>
                    {
                        var target = Hero.AllAliveHeroes
                            .Where(h => h.IsLord && h.MapFaction != lord.MapFaction
                                        && h.Clan != null && h.IsAlive)
                            .OrderBy(h => _rng.Next()).FirstOrDefault();
                        if (target == null) return null;
                        try { lord.Clan?.AddRenown(-3f); } catch { }
                        try { target.Clan?.AddRenown(-10f); } catch { }
                        return $"{target.Name}'s name grows quieter in the world.";
                    }});
                    // Severe Life � sometimes age a random enemy lord
                    if (_rng.Next(100) < 25)
                        pool.Add(new MapSpellEntry { SpellName="Severe Life", DayCost=45, Action=() =>
                        {
                            var target = Hero.AllAliveHeroes
                                .Where(h => h.IsLord && h.MapFaction != lord.MapFaction && h.IsAlive)
                                .OrderBy(h => _rng.Next()).FirstOrDefault();
                            if (target == null) return null;
                            target.SetBirthDay(target.BirthDay - CampaignTime.Years(10f / 252f));
                            return $"{target.Name} reels as life is torn loose.";
                        }});
                    // Dark Bargain � when older Aserai lords grow desperate, they spend lives to reclaim years
                    if (_rng.Next(100) < 8)
                        pool.Add(new MapSpellEntry { SpellName="Dark Bargain", DayCost=0, Action=() =>
                            MageLordRegistry.PerformAseraiDarkBargain(lord) });
                    if (pool.Count == 0) pool.Add(new MapSpellEntry { SpellName="Charm", DayCost=12, Action=() =>
                    {
                        lord.Gold += 1000;
                        return $"{lord.Name}'s dealings grow unusually profitable.";
                    }});
                    break;

                case "sturgia":
                    // Suppress — drain a random enemy's morale
                    pool.Add(new MapSpellEntry { SpellName="Suppress", DayCost=20, Action=() =>
                    {
                        var enemy = Hero.AllAliveHeroes
                            .Where(h => h.IsLord && h.MapFaction != lord.MapFaction
                                        && h.PartyBelongedTo != null && h.IsAlive)
                            .OrderBy(h => _rng.Next()).FirstOrDefault();
                        if (enemy == null) return null;
                        enemy.PartyBelongedTo.RecentEventsMorale -= 12f;
                        return $"Soldiers near {enemy.Name} feel something press down on them.";
                    }});                    
                    break;
                case "empire":
                    // Clairvoyance — flavour, no mechanical effect
                    pool.Add(new MapSpellEntry { SpellName="Clairvoyance", DayCost=3, Action=() =>
                        {
                            ChangeClanInfluenceAction.Apply(lord.Clan, 5f);
                            return $"{lord.Name} watches the roads. Nothing moves unseen.";
                        }
                    });
                    // Calling — summon Imperial recruits to own party
                    if (lord.PartyBelongedTo != null && _rng.Next(100) < 35)
                        pool.Add(new MapSpellEntry { SpellName="Calling", DayCost=90, Action=() =>
                        {
                            CharacterObject recruit =
                                MBObjectManager.Instance.GetObject<CharacterObject>("imperial_recruit")
                             ?? MBObjectManager.Instance.GetObject<CharacterObject>("imperial_levy_infantryman");
                            if (recruit == null) return null;
                            int count = 8 + _rng.Next(13); // 8-20 for lords; powerful, but no longer cheap
                            try { lord.PartyBelongedTo.MemberRoster.AddToCounts(recruit, count); }
                            catch { return null; }
                            return $"{count} soldiers answer {lord.Name}'s call and march to join them.";
                        }});
                    // Hollow Name — the Empire strips renown through law and whisper
                    pool.Add(new MapSpellEntry { SpellName="Hollow Name", DayCost=30, Action=() =>
                    {
                        var target = Hero.AllAliveHeroes
                            .Where(h => h.IsLord && h.MapFaction != lord.MapFaction
                                        && h.Clan != null && h.IsAlive)
                            .OrderBy(h => _rng.Next()).FirstOrDefault();
                        if (target == null) return null;
                        try { target.Clan?.AddRenown(-10f); } catch { }
                        return $"{target.Name}'s name loses weight in the great houses.";
                    }});
                    break;

                case "khuzait":
                    // Aura of Hate — drain militia from a random enemy village
                    pool.Add(new MapSpellEntry { SpellName="Aura of Hate", DayCost=45, Action=() =>
                    {
                        Settlement target = Settlement.All
                            .Where(s => s.IsVillage && s.MapFaction != lord.MapFaction && s.Village != null && s.Village.Militia > 0f)
                            .OrderBy(s => _rng.Next()).FirstOrDefault();
                        if (target == null) return null;
                        if (!SpellEffects.TrySetMilitia(target.Village, 0f)) return null;
                        return $"Fear rides ahead of {lord.Name}. The people of {target.Name} abandon their posts.";
                    }});
                    // Hollow Name — Khuzait riders spread word of weakness
                    pool.Add(new MapSpellEntry { SpellName="Hollow Name", DayCost=20, Action=() =>
                    {
                        var target = Hero.AllAliveHeroes
                            .Where(h => h.IsLord && h.MapFaction != lord.MapFaction
                                        && h.Clan != null && h.IsAlive)
                            .OrderBy(h => _rng.Next()).FirstOrDefault();
                        if (target == null) return null;
                        try { target.Clan?.AddRenown(-8f); } catch { }
                        return $"Rumour travels the steppe. {target.Name}'s name carries less weight tonight.";
                    }});
                    break;

                case "vlandia":
                    // Long Road — morale/speed boost own party
                    if (lord.PartyBelongedTo != null)
                        pool.Add(new MapSpellEntry { SpellName="Long Road", DayCost=25, Action=() =>
                        {
                            lord.PartyBelongedTo.RecentEventsMorale += 12f;
                            return $"{lord.Name}'s column moves with uncommon purpose.";
                        }});
                    // Inspire — secondary morale boost
                    if (lord.PartyBelongedTo != null)
                        pool.Add(new MapSpellEntry { SpellName="Inspire", DayCost=20, Action=() =>
                        {
                            lord.PartyBelongedTo.RecentEventsMorale += 10f;
                            return $"Discipline holds in {lord.Name}'s ranks.";
                        }});
                    break;
            }

            return pool;
        }

        // ── Called once per save, on the first daily tick ─────────────────
        public static void SeedInitialMageLords()
        {
            if (_seeded) return;
            _seeded = true;

            foreach (Kingdom kingdom in Campaign.Current.Kingdoms)
                SeedFaction(kingdom);
        }

        private static void SeedFaction(IFaction faction)
        {
            int existing = CountActiveMageLordsIn(faction);
            if (existing >= MaxPerFaction) return;

            var candidates = Hero.AllAliveHeroes
                .Where(h => h.MapFaction == faction && h.IsLord && !IsMageLord(h))
                .ToList();

            int toAdd = MaxPerFaction - existing;
            for (int i = 0; i < toAdd && candidates.Count > 0; i++)
            {
                int  idx    = _rng.Next(candidates.Count);
                Hero chosen = candidates[idx];
                candidates.RemoveAt(idx);

                _mageLordIds.Add(chosen.StringId);
                InformationManager.DisplayMessage(new InformationMessage(
                    $"The Gift stirs in {chosen.Name} of {faction.Name}.",
                    new Color(0.7f, 0.2f, 1f)));
            }
        }

        private static int CountActiveMageLordsIn(IFaction faction) =>
            Hero.AllAliveHeroes.Count(h => h.MapFaction == faction && IsMageLord(h));

        // ── Called by OnHeroKilled when a Mage Lord dies ──────────────────
        public static void OnMageLordDied(Hero hero, bool killedFightingPlayer = false)
        {
            _mageLordIds.Remove(hero.StringId);

            string factionId = (hero.MapFaction as Kingdom)?.StringId;
            if (factionId == null) return;

            // Schedule respawn
            _respawnHours[factionId] = 168;

            InformationManager.DisplayMessage(new InformationMessage(
                $"The Gift of {hero.Name} is extinguished. It will seek a new vessel in one week.",
                Color.FromUint(0xFFCC4444)));

            // Player learns a faction-specific spell only if the lord died fighting their party
            if (SpellKnowledge.HasGift && killedFightingPlayer)
            {
                string lordFactionId = (hero.MapFaction as Kingdom)?.StringId?.ToLower() ?? "";
                RevealRandomUnknownSpell(hero.Name.ToString(), lordFactionId);
            }

            // 40% chance: ritual scroll surfaces in a town
            if (_rng.Next(100) < 40)
                TryDropRitualScroll();
        }

        public static void RevealRandomUnknownSpell(string lordName, string lordFaction = "")
        {
            // Prefer spells matching this lord's faction.
            // Special case: Aura of Hate can also be learned from Khuzait lords.
            var unknown = SpellDatabase.All
                .Where(s => !SpellKnowledge.IsKnown(s.BookTag) &&
                            s.LearnHow == LearnHow.MageLord &&
                            (s.LordFaction == "" || s.LordFaction == lordFaction ||
                             (s.BookTag == "AURA_OF_HATE" && lordFaction == "khuzait")))
                .ToList();

            // Fallback: only generic (faction="") spells — no cross-faction teaching
            if (unknown.Count == 0)
                unknown = SpellDatabase.All
                    .Where(s => !SpellKnowledge.IsKnown(s.BookTag) &&
                                s.LordFaction == "")
                    .ToList();

            if (unknown.Count == 0)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"As {lordName} falls, their knowledge washes over you — but you already know everything they knew.",
                    new Color(0.5f, 0.3f, 0.7f)));
                return;
            }

            SpellEntry learned = unknown[_rng.Next(unknown.Count)];
            SpellKnowledge.RevealSpell(learned,
                $"As {lordName} falls, their knowledge tears free — you catch it.");
        }

        private static void TryDropRitualScroll()
        {
            ItemObject scroll =
                MBObjectManager.Instance.GetObject<ItemObject>("twa_scroll_ritual");
            if (scroll == null) return;

            // Only drop if the scroll isn't already present anywhere
            // (prevents the world from filling up with copies over a long campaign)
            var towns = Settlement.All.Where(s => s.IsTown).ToList();
            bool alreadyExists = towns.Any(t => t.ItemRoster.FindIndexOfItem(scroll) >= 0);
            if (alreadyExists) return;

            if (towns.Count == 0) return;
            Settlement target = towns[_rng.Next(towns.Count)];
            target.ItemRoster.AddToCounts(scroll, 1);

            InformationManager.DisplayMessage(new InformationMessage(
                "Somewhere, something the dead Mage carried finds its way into the world.",
                new Color(0.45f, 0.05f, 0.5f)));
        }

        // ── Called on every hourly tick ───────────────────────────────────
        public static void CheckRespawnTimers()
        {
            foreach (string factionId in _respawnHours.Keys.ToList())
            {
                _respawnHours[factionId]--;
                if (_respawnHours[factionId] > 0) continue;

                _respawnHours.Remove(factionId);

                // Find the kingdom and seed a new Mage Lord in it
                Kingdom kingdom = Campaign.Current.Kingdoms
                    .FirstOrDefault(k => k.StringId == factionId);
                if (kingdom != null)
                    SeedFaction(kingdom);
            }
        }

        // ── Ensure every mage lord's party has ~5% mage units ────────────
        public static void MaintainMageArmies()
        {
            var mageChar = MBObjectManager.Instance.GetObject<CharacterObject>("twa_mage_channeler");
            if (mageChar == null) return;

            foreach (string id in _mageLordIds.ToList())
            {
                try
                {
                    Hero lord = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == id);
                    if (lord?.PartyBelongedTo == null) continue;

                    MobileParty party = lord.PartyBelongedTo;
                    int total = party.MemberRoster.TotalManCount;
                    if (total < 10) continue; // don't pad tiny parties

                    int currentMages = party.MemberRoster.GetTroopRoster()
                        .Where(e => e.Character?.StringId?.StartsWith("twa_mage_") ?? false)
                        .Sum(e => e.Number);
                    int target = Math.Max(1, (int)(total * 0.05f));

                    if (currentMages < target)
                        party.MemberRoster.AddToCounts(mageChar, target - currentMages);
                }
                catch { }
            }
        }

        public static void TryGrantCompanionMagic(Hero companion)
        {
            if (companion == null || _mageLordIds.Contains(companion.StringId)) return;
            _mageLordIds.Add(companion.StringId);
            InformationManager.DisplayMessage(new InformationMessage(
                $"{companion.Name} carries a faint echo of the Gift. They will fight as one who knows it.",
                new Color(0.7f, 0.2f, 1f)));

            SpellKnowledge.LearnFromCompanion(companion.Name.ToString());
        }

        // ── Save / Load ───────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            var idList  = _mageLordIds.ToList();
            var rKeys   = _respawnHours.Keys.ToList();
            var rVals   = _respawnHours.Values.ToList();
            var ccKeys  = _campaignCooldownDays.Keys.ToList();
            var ccVals  = _campaignCooldownDays.Values.ToList();
            bool seeded = _seeded;
            int  lrDays = SpellEffects.LongRoadDaysRemaining;

            store.SyncData("TWA_MageLordIds",       ref idList);
            store.SyncData("TWA_RespawnKeys",       ref rKeys);
            store.SyncData("TWA_RespawnVals",       ref rVals);
            store.SyncData("TWA_MageLordSeeded",    ref seeded);
            store.SyncData("TWA_CampaignCDKeys",    ref ccKeys);
            store.SyncData("TWA_CampaignCDVals",    ref ccVals);
            store.SyncData("TWA_LongRoadDays",      ref lrDays);

            _seeded = seeded;
            SpellEffects.SetLongRoadDays(lrDays);

            _mageLordIds.Clear();
            if (idList != null) foreach (var id in idList) _mageLordIds.Add(id);

            _respawnHours.Clear();
            if (rKeys != null && rVals != null)
                for (int i = 0; i < Math.Min(rKeys.Count, rVals.Count); i++)
                    _respawnHours[rKeys[i]] = rVals[i];

            _campaignCooldownDays.Clear();
            if (ccKeys != null && ccVals != null)
                for (int i = 0; i < Math.Min(ccKeys.Count, ccVals.Count); i++)
                    _campaignCooldownDays[ccKeys[i]] = ccVals[i];
        }
    }

    // =========================================================================
    // 10. MAGE LORD AI
    //    Runs every mission frame. Finds hero agents tagged as Mage Lords and
    //    makes them cast spells based on a priority system.
    //
    //    Priority order:
    //      1. Mending       — HP < 30%
    //      2. Repel         — 3+ enemies within 8 m
    //      3. Iron Shout    — 2+ enemies in forward 12 m cone
    // =========================================================================
    public static class MageLordAI
    {
        // Seconds between consecutive casts per Mage Lord
        private const float CastInterval = 10f;

        private static readonly Random _rng = new Random();
        private static readonly Dictionary<string, float> _cooldowns =
            new Dictionary<string, float>();

        private static float _tickAccumulator = 0f;
        private const  float TickInterval     = 0.5f; // only process every 0.5s

        public static void MissionTick(float dt)
        {
            if (Mission.Current == null) return;

            _tickAccumulator += dt;
            if (_tickAccumulator < TickInterval) return;
            _tickAccumulator = 0f;

            // Tick down all cooldowns
            foreach (string key in _cooldowns.Keys.ToList())
            {
                _cooldowns[key] -= TickInterval;
                if (_cooldowns[key] <= 0f) _cooldowns.Remove(key);
            }

            foreach (Agent agent in Mission.Current.Agents.ToList())
            {
                if (!agent.IsActive() || agent.IsMount) continue;
                if (agent == Agent.Main) continue;
                if (!agent.IsHero) continue; // Mage Lords are always hero agents

                Hero hero = (agent.Character as CharacterObject)?.HeroObject;
                if (hero == null || !MageLordRegistry.IsMageLord(hero)) continue;

                if (_cooldowns.ContainsKey(hero.StringId)) continue;

                if (agent.MountAgent != null)
                {
                    try { agent.SetRidingOrder(RidingOrder.RidingOrderDismount.OrderEnum); }
                    catch { }
                    continue;
                }

                DecideAndCast(agent, hero);
            }
        }

        private static void DecideAndCast(Agent agent, Hero hero)
        {
            float hpPct = agent.Health / Math.Max(agent.HealthLimit, 1f);

            // Priority 1 — Heal self when HP falls below 30%
            if (hpPct < 0.3f)
            {
                TriggerCast(agent, hero, "Mending", 30, () => AIMending(agent));
                return;
            }

            if (MBRandom.RandomInt(100) < 5 && TryCastRandomSpell(agent, hero))
                return;

            // Priority 2 — Repel when 3+ enemies are swarming within melee range
            int closeEnemies = Mission.Current.Agents
                .Count(a => a != agent && a.IsActive() && !a.IsMount &&
                            a.Team != agent.Team &&
                            a.Position.Distance(agent.Position) < 8f);

            if (closeEnemies >= 3)
            {
                TriggerCast(agent, hero, "Repel", 40, () => AIRepel(agent));
                return;
            }

            // Priority 3 — Hurt when 2+ enemies are directly ahead
            int coneEnemies = Mission.Current.Agents
                .Count(a => a != agent && a.IsActive() && !a.IsMount &&
                            a.Team != agent.Team &&
                            a.Position.Distance(agent.Position) < 12f &&
                            Vec3.DotProduct(
                                agent.LookDirection.NormalizedCopy(),
                                (a.Position - agent.Position).NormalizedCopy()) > 0.5f);

            if (coneEnemies >= 2)
            {
                TriggerCast(agent, hero, "Blast", 20, () => AIBlast(agent));
                return;
            }

            // Priority 4 — Confuse when any non-hero enemies are nearby
            bool hasNearbyEnemy = Mission.Current.Agents
                .Any(a => a != agent && a.IsActive() && !a.IsMount && !a.IsHero &&
                          a.Team != agent.Team &&
                          a.Position.Distance(agent.Position) < 10f);

            if (hasNearbyEnemy)
            {
                TriggerCast(agent, hero, "Confuse", 30, () => AIConfuse(agent));
                return;
            }

            if (TryCastCommandSpell(agent, hero))
                return;
        }

        private static bool TryCastRandomSpell(Agent agent, Hero hero)
        {
            var options = new List<Action>();

            int closeEnemies = Mission.Current.Agents
                .Count(a => a != agent && a.IsActive() && !a.IsMount &&
                            a.Team != agent.Team &&
                            a.Position.Distance(agent.Position) < 8f);
            if (closeEnemies >= 3)
                options.Add(() => TriggerCast(agent, hero, "Repel", 40, () => AIRepel(agent)));

            int coneEnemies = Mission.Current.Agents
                .Count(a => a != agent && a.IsActive() && !a.IsMount &&
                            a.Team != agent.Team &&
                            a.Position.Distance(agent.Position) < 12f &&
                            Vec3.DotProduct(
                                agent.LookDirection.NormalizedCopy(),
                                (a.Position - agent.Position).NormalizedCopy()) > 0.5f);
            if (coneEnemies >= 2)
                options.Add(() => TriggerCast(agent, hero, "Blast", 20, () => AIBlast(agent)));

            bool hasNearbyEnemy = Mission.Current.Agents
                .Any(a => a != agent && a.IsActive() && !a.IsMount && !a.IsHero &&
                          a.Team != agent.Team &&
                          a.Position.Distance(agent.Position) < 10f);
            if (hasNearbyEnemy)
                options.Add(() => TriggerCast(agent, hero, "Confuse", 30, () => AIConfuse(agent)));

            if ((hero.MapFaction as Kingdom)?.StringId?.ToLower() == "aserai")
                options.Add(() => TriggerCast(agent, hero, "Severe Life", 45, () => AISevereLife(agent)));

            if (TryHasVisibleEnemyFormations(agent, out bool hasMounted, out bool hasRanged))
            {
                options.Add(() => TriggerCast(agent, hero, "Halt", 30,
                    () => SpellEffects.IssueBattleCommand(agent, SpellEffects.BattleCommandKind.Halt,
                        "{0} enemy formation{1} ordered to halt.")));
                options.Add(() => TriggerCast(agent, hero, "Enrage", 35,
                    () => SpellEffects.IssueBattleCommand(agent, SpellEffects.BattleCommandKind.Enrage,
                        "{0} enemy formation{1} driven into a charge.")));
                if (hasMounted)
                    options.Add(() => TriggerCast(agent, hero, "Dismount", 30,
                        () => SpellEffects.IssueBattleCommand(agent, SpellEffects.BattleCommandKind.Dismount,
                            "{0} enemy formation{1} forced to dismount.")));
                if (hasRanged)
                    options.Add(() => TriggerCast(agent, hero, "Stop Arrows", 45,
                        () => SpellEffects.IssueBattleCommand(agent, SpellEffects.BattleCommandKind.StopArrows,
                            "{0} enemy formation{1} told to stop shooting.")));
            }

            if (options.Count == 0) return false;
            options[MBRandom.RandomInt(options.Count)].Invoke();
            return true;
        }

        private static bool TryCastCommandSpell(Agent agent, Hero hero)
        {
            if (!TryHasVisibleEnemyFormations(agent, out bool hasMounted, out bool hasRanged))
                return false;

            var options = new List<Action>
            {
                () => TriggerCast(agent, hero, "Halt", 30,
                    () => SpellEffects.IssueBattleCommand(agent, SpellEffects.BattleCommandKind.Halt,
                        "{0} enemy formation{1} ordered to halt.")),
                () => TriggerCast(agent, hero, "Enrage", 35,
                    () => SpellEffects.IssueBattleCommand(agent, SpellEffects.BattleCommandKind.Enrage,
                        "{0} enemy formation{1} driven into a charge."))
            };

            if (hasMounted)
                options.Add(() => TriggerCast(agent, hero, "Dismount", 30,
                    () => SpellEffects.IssueBattleCommand(agent, SpellEffects.BattleCommandKind.Dismount,
                        "{0} enemy formation{1} forced to dismount.")));

            if (hasRanged)
                options.Add(() => TriggerCast(agent, hero, "Stop Arrows", 45,
                    () => SpellEffects.IssueBattleCommand(agent, SpellEffects.BattleCommandKind.StopArrows,
                        "{0} enemy formation{1} told to stop shooting.")));

            options[MBRandom.RandomInt(options.Count)].Invoke();
            return true;
        }

        private static bool TryHasVisibleEnemyFormations(Agent agent, out bool hasMounted, out bool hasRanged)
        {
            hasMounted = false;
            hasRanged = false;
            if (Mission.Current == null || Mission.Current.Scene == null) return false;

            var scene = Mission.Current.Scene;
            var formations = new HashSet<Formation>();

            foreach (Agent enemy in Mission.Current.Agents.ToList())
            {
                if (enemy == agent || enemy.IsMount || !enemy.IsActive()) continue;
                if (enemy.Team == agent.Team || enemy.Formation == null) continue;
                if (enemy.Position.Distance(agent.Position) > 500f) continue;

                bool visible = false;
                try { visible = scene.CheckPointCanSeePoint(agent.Position, enemy.Position, 500f); }
                catch { }
                if (!visible) continue;

                formations.Add(enemy.Formation);
            }

            foreach (Formation formation in formations)
            {
                try
                {
                    if (formation.HasAnyMountedUnit)
                        hasMounted = true;
                    if (formation.GetCountOfUnitsBelongingToLogicalClass(TaleWorlds.Core.FormationClass.Ranged) > 0 ||
                        formation.GetCountOfUnitsBelongingToLogicalClass(TaleWorlds.Core.FormationClass.HorseArcher) > 0)
                        hasRanged = true;
                }
                catch { }
            }

            return formations.Count > 0;
        }

        private static void TriggerCast(Agent agent, Hero hero,
                                        string spellName, int dayCost, Action effect)
        {
            MageLordRegistry.ApplyMageLordAgeCost(hero, dayCost);
            _cooldowns[hero.StringId] = CastInterval;

            // Distinctive cast message — visible across the battlefield
            InformationManager.DisplayMessage(new InformationMessage(
                $"✦ {hero.Name} channels {spellName} ✦",
                new Color(0.8f, 0.1f, 0.9f)));

            // Glow on the caster agent
            SpellEntry entry = SpellDatabase.All.FirstOrDefault(s => s.Name == spellName);
            SpellEffects.CastGlow(agent,
                SpellEffects.ResolveGlowColor(entry));

            try { effect(); }
            catch { }

            if (SpellEffects.IsTournamentMission())
                SpellEffects.DisqualifyTournamentCaster(agent, hero, spellName);
        }

        // ── AI-centric spell implementations ──────────────────────────────
        // These mirror the player spells but use `agent` as the caster origin
        // rather than Agent.Main.

        private static void AIMending(Agent caster)
        {
            caster.Health = Math.Min(caster.Health + 20f, caster.HealthLimit);
        }

        private static void AIRepel(Agent caster)
        {
            if (Mission.Current == null) return;
            Vec3 origin = caster.Position;

            foreach (Agent enemy in Mission.Current.Agents.ToList())
            {
                if (enemy == caster || enemy.IsMount || !enemy.IsActive()) continue;
                if (enemy.Team == caster.Team) continue;

                float dist = enemy.Position.Distance(origin);
                if (dist > 8f || dist < 0.1f) continue;

                Vec3 dir = (enemy.Position - origin).NormalizedCopy();
                enemy.TeleportToPosition(enemy.Position + dir * 3f);
            }
        }

        private static void AIBlast(Agent caster)
        {
            if (Mission.Current == null) return;
            Vec3 forward = caster.LookDirection.NormalizedCopy();
            Vec3 origin  = caster.Position;
            ActionIndexCache trip = ActionIndexCache.Create("act_struck_from_back_medium_left_staff");

            foreach (Agent enemy in Mission.Current.Agents.ToList())
            {
                if (enemy == caster || enemy.IsMount || !enemy.IsActive()) continue;
                if (enemy.Team == caster.Team) continue;

                Vec3 toEnemy = enemy.Position - origin;
                float dot = Vec3.DotProduct(forward, toEnemy.NormalizedCopy());

                if (toEnemy.Length <= 20f && dot >= 0.5f)
                {
                    Vec3 pushDest = enemy.Position + forward * 20f;
                    pushDest.z = enemy.Position.z;
                    try
                    {
                        enemy.TeleportToPosition(pushDest);
                        if (trip.Index >= 0) enemy.SetActionChannel(0, trip, false);
                    }
                    catch { }
                }

                if (toEnemy.Length > 10f || dot < 0.3f) continue;

                enemy.Health = Math.Max(0f, enemy.Health - 100f);
                if (enemy.Health <= 0f) SpellEffects.KillAgent(enemy);
            }
        }

        private static void AIConfuse(Agent caster)
        {
            if (Mission.Current == null) return;
            Vec3 forward = caster.LookDirection.NormalizedCopy();
            ActionIndexCache freeze = ActionIndexCache.Create("act_stand_1");
            foreach (Agent enemy in Mission.Current.Agents.ToList())
            {
                if (enemy == caster || enemy.IsMount || !enemy.IsActive()) continue;
                if (enemy.Team == caster.Team || enemy.IsHero) continue;
                Vec3 toEnemy = enemy.Position - caster.Position;
                if (toEnemy.Length > 10f) continue;
                if (Vec3.DotProduct(forward, toEnemy.NormalizedCopy()) < 0.3f) continue;
                int idx = enemy.Index;
                ActiveEffectManager.Add(new ActiveEffect
                {
                    Name            = $"_ai_confuse_{idx}",
                    Duration        = 5f,
                    IsMissionEffect = true,
                    OnTick = _ =>
                    {
                        Agent t = Mission.Current?.Agents.FirstOrDefault(x => x.Index == idx);
                        if (t != null && t.IsActive()) t.SetActionChannel(0, freeze, true);
                    }
                });
            }
        }

        private static void AISevereLife(Agent caster)
        {
            if (Mission.Current == null) return;
            var targets = Mission.Current.Agents
                .Where(a => a != caster && a.IsActive() && !a.IsMount &&
                            a.Team != caster.Team && !a.IsHero)
                .ToList();
            if (targets.Count == 0) return;

            Agent target = targets[_rng.Next(targets.Count)];
            SpellEffects.KillAgent(target);
        }

        public static void ClearCooldowns() => _cooldowns.Clear();
    }

    // =========================================================================
    // 11. MAGE UNIT MANAGER
    //     Handles the two Mage Unit mechanics from the spec:
    //
    //     BURN-OUT  — each time a Mage Unit casts (every ~15 s), roll 1–100.
    //                 On a result ≤ 5 the unit explodes (KillAgent) and deals
    //                 30 blunt splash damage to nearby enemies as a parting gift.
    //
    //     BATTERY   — while any allied Mage Unit is within 10 m of the player,
    //                 BatteryMultiplier = 1.5 (×50% spell damage). Recalculated
    //                 every frame so it drops the instant the unit moves away or
    //                 is killed.
    //
    //     Mage Units are identified by their CharacterObject StringId prefix
    //     "twa_mage_" (defined in troops.xml).
    // =========================================================================
    public static class MageUnitManager
    {
        private const float CastInterval  = 15f;   // seconds between casts
        private const float BatteryRange  = 10f;   // metres for battery effect
        private const float BatteryBonus  = 1.5f;  // damage multiplier when active
        private const int   BurnoutChance = 5;     // percent per cast

        // Per-agent cast cooldowns (agent Index → remaining seconds)
        private static readonly Dictionary<int, float> _castTimers =
            new Dictionary<int, float>();

        private static readonly Random _rng = new Random();
        private static float _tickAccumulator = 0f;
        private const  float TickInterval     = 0.5f;

        public static float BatteryMultiplier { get; private set; } = 1f;

        public static void MissionTick(float dt)
        {
            if (Mission.Current == null || Agent.Main == null)
            {
                BatteryMultiplier = 1f;
                return;
            }

            _tickAccumulator += dt;
            bool fullTick = _tickAccumulator >= TickInterval;
            if (fullTick) _tickAccumulator = 0f;

            bool batteryActive = false;
            Vec3 playerPos     = Agent.Main.Position;

            foreach (Agent agent in Mission.Current.Agents.ToList())
            {
                if (!agent.IsActive() || agent.IsMount) continue;
                if (!IsMageUnit(agent)) continue;

                // Battery check every frame (lightweight distance check only)
                if (agent.Team == Agent.Main.Team &&
                    agent.Position.Distance(playerPos) <= BatteryRange)
                    batteryActive = true;

                // Casting logic only on full tick interval
                if (!fullTick) continue;
                if (agent.Team != Agent.Main.Team) continue;

                if (!_castTimers.ContainsKey(agent.Index))
                    _castTimers[agent.Index] = CastInterval;

                _castTimers[agent.Index] -= TickInterval;
                if (_castTimers[agent.Index] > 0f) continue;

                _castTimers[agent.Index] = CastInterval;
                TriggerMageUnitCast(agent);
            }

            BatteryMultiplier = batteryActive ? BatteryBonus : 1f;
        }

        // ── Determine whether an agent is a Mage Unit by troop id prefix ──
        private static bool IsMageUnit(Agent agent)
        {
            string id = agent.Character?.StringId ?? "";
            return id.StartsWith("twa_mage_");
        }

        // ── A Mage Unit fires a small AOE bolt, then rolls for burn-out ───
        private static void TriggerMageUnitCast(Agent caster)
        {
            if (Mission.Current == null) return;

            Agent target = Mission.Current.Agents
                .Where(a => a != caster && a.IsActive() && !a.IsMount &&
                            a.Team != caster.Team &&
                            a.Position.Distance(caster.Position) <= 20f)
                .OrderBy(a => a.Position.Distance(caster.Position))
                .FirstOrDefault();

            if (target != null)
            {
                // Distinctive cast message
                InformationManager.DisplayMessage(new InformationMessage(
                    $"✦ A Void Channeler strikes {target.Name} ✦",
                    new Color(0.6f, 0.1f, 0.8f)));

                SpellEffects.CastGlow(caster, SpellGlowColor.Combat);
                SpellEffects.KillAgent(target);
            }

            // Burn-out roll — 5% chance to explode
            if (_rng.Next(100) < BurnoutChance)
                Burnout(caster);
        }

        // ── The unit explodes: killed instantly, 30 blunt AOE to nearby enemies
        private static void Burnout(Agent caster)
        {
            InformationManager.DisplayMessage(new InformationMessage(
                $"A Void Channeler burns out! The overload tears through nearby enemies.",
                new Color(1f, 0.4f, 0.1f)));

            if (Mission.Current != null)
            {
                Vec3 origin = caster.Position;
                foreach (Agent enemy in Mission.Current.Agents.ToList())
                {
                    if (enemy == caster || enemy.IsMount || !enemy.IsActive()) continue;
                    if (enemy.Team == caster.Team) continue;
                    if (enemy.Position.Distance(origin) > 6f) continue;

                    enemy.Health = Math.Max(0f, enemy.Health - 30f);
                }
            }

            // Kill the channeler after the splash
            _castTimers.Remove(caster.Index);
            SpellEffects.KillAgent(caster);
        }

        public static void ClearTimers() => _castTimers.Clear();
    }
}








