// =============================================================================
// ASH AND EMBER — Schemes/SchemeSystem.cs
// Core scheme logic: definitions, pending-queue, success/failure calculation,
// effect execution, and NPC AI tick.
//
// Design intent:
//   Schemes are a high-risk, high-reward alternative to campaign map spells.
//   Both draw on the same reservoir of player resources (gold, influence) and
//   should feel meaningfully costly. To prevent the world from drowning in
//   events, a player may only have ONE scheme pending at a time, and NPC
//   schemes fire at most once per ~33 days globally.
//
// Execution delay: 1–3 campaign days after queuing.
//
// Success formula (per scheme):
//   chance = baseChance + (skillLevel / 600 × 30%) − (security / 400)
//            − (clanTier × 2.5%)  [lord targets]  or  − (clanTier × 2%)  [settlement]
//   Clamped to [0.05, 0.85].
//   Ashen targets: additional −30% (cold fire does not yield to mortal plots).
//
// Failure outcomes:
//   70% → Agent fled: brief notification, no consequences.
//   30% → Agent caught:
//           • Crime rating +30–60 in target's kingdom.
//           • Relations −60–80 with target / settlement owner.
//           • Assassination / coup caught: 40% chance of war declaration.
//
// NPC AI:
//   3% global chance per day (~1 scheme every 33 days).
//   Per-lord cooldown 20–35 days; NPCs only target enemy factions.
//   NPCs pay the same gold and influence as the player.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    // ─────────────────────────────────────────────────────────────────────────
    // Scheme catalogue
    // ─────────────────────────────────────────────────────────────────────────
    internal enum SchemeType
    {
        Assassinate,        // Kill a lord              — Roguery
        SpreadTerror,       // Drop city security       — Roguery
        PoisonWell,         // Kill militia             — Roguery
        StageCoup,          // Collapse settlement      — Charm
        SpreadRumors,       // Drop loyalty             — Charm
        BurnStorage,        // Destroy food/prosperity  — Roguery
        BribeSoldiers,      // Steal garrison troops    — Charm
        ForgeDocuments,     // Smear a lord             — Charm
        HireAssassin,       // Wound a lord's party     — Roguery
        FalseAccusations,   // Drain clan renown        — Charm
        VipersCounsel,      // Undermine rival clan with king — Charm (same kingdom only)
        ScatterWolves,      // Flood rival kingdom with bandits/deserters — Roguery
    }

    internal sealed class SchemeDefinition
    {
        internal readonly SchemeType   Type;
        internal readonly string       Name;
        internal readonly string       Description;
        internal readonly int          GoldCost;
        internal readonly int          InfluenceCost;
        internal readonly float        BaseSuccess;
        internal readonly SkillObject  Skill;
        internal readonly bool         NeedsLord;
        internal readonly bool         NeedsSettlement;
        internal readonly int          SkillXp;

        internal SchemeDefinition(SchemeType type, string name, string desc,
            int gold, int inf, float baseSuccess, SkillObject skill,
            bool needsLord, bool needsSettlement, int skillXp)
        {
            Type = type; Name = name; Description = desc;
            GoldCost = gold; InfluenceCost = inf; BaseSuccess = baseSuccess;
            Skill = skill; NeedsLord = needsLord; NeedsSettlement = needsSettlement;
            SkillXp = skillXp;
        }
    }

    internal sealed class PendingScheme
    {
        internal string InstigatorId;
        internal SchemeType Type;
        internal string TargetHeroId;
        internal string TargetSettlementId;
        internal int    DaysRemaining;
        internal bool   IsPlayer;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SchemeSystem
    // ─────────────────────────────────────────────────────────────────────────
    internal static partial class SchemeSystem
    {
        private const string AshenKingdomId = "ashen_kingdom";

        // ── Definitions ───────────────────────────────────────────────────────
        // Lazy: DefaultSkills objects are registered by the engine during session
        // launch. A static readonly field initializer runs at type-load time (which
        // can happen earlier — e.g. during SyncData) and would throw a
        // TypeInitializationException, poisoning the type permanently.
        private static SchemeDefinition[] _definitions;
        internal static SchemeDefinition[] Definitions
        {
            get
            {
                if (_definitions != null) return _definitions;
                try { _definitions = BuildDefinitions(); }
                catch { _definitions = new SchemeDefinition[0]; }
                return _definitions;
            }
        }

        private static SchemeDefinition[] BuildDefinitions() => new[]
        {
            // Costs scale with target clan tier: gold linearly (×1.0–3.4),
            // influence exponentially (×1.0–7.5, base 1.4^tier).
            // SkillXp awarded on success.
            //
            // Balance principle: permanent > temporary, more impact > less.
            // Influence hierarchy: assassination (80) > coup (70) > garrison (40)
            //   > soft lord schemes (25–45) > cheap settlement schemes (15–30).
            // StageCoup influence is intentionally below assassination — loyalty
            // and security recover over time; dead lords do not.
            //
            // LORD SCHEMES ─────────────────────────────────────────────────────────
            new SchemeDefinition(SchemeType.Assassinate,
                "Assassinate a Lord",
                "Hire a blade. On success the target dies quietly. On exposure: war may follow. Hard 14-day retry block per target.",
                6000, 80, 0.25f, DefaultSkills.Roguery, needsLord: true, needsSettlement: false, skillXp: 1500),

            new SchemeDefinition(SchemeType.ForgeDocuments,
                "Forge Documents",
                "Fabricated letters damage a lord's reputation with their own faction.",
                2000, 35, 0.40f, DefaultSkills.Charm, needsLord: true, needsSettlement: false, skillXp: 750),

            new SchemeDefinition(SchemeType.FalseAccusations,
                "False Accusations",
                "Slander carefully placed at the right ears. Clan renown is damaged; their standing erodes.",
                1500, 25, 0.45f, DefaultSkills.Charm, needsLord: true, needsSettlement: false, skillXp: 500),

            // SETTLEMENT SCHEMES ───────────────────────────────────────────────────
            new SchemeDefinition(SchemeType.StageCoup,
                "Stage a Coup",
                "Bribe garrison officers. Loyalty collapses — rebellion becomes likely.",
                4500, 70, 0.20f, DefaultSkills.Charm, needsLord: false, needsSettlement: true, skillXp: 1200),

            new SchemeDefinition(SchemeType.PoisonWell,
                "Poison a Well",
                "The garrison sickens. Militia die before anyone connects cause to effect.",
                2200, 40, 0.38f, DefaultSkills.Roguery, needsLord: false, needsSettlement: true, skillXp: 750),

            new SchemeDefinition(SchemeType.BribeSoldiers,
                "Bribe Soldiers",
                "A portion of the garrison deserts. They scatter — no one joins you, they simply leave.",
                2200, 40, 0.38f, DefaultSkills.Charm, needsLord: false, needsSettlement: true, skillXp: 750),

            new SchemeDefinition(SchemeType.BurnStorage,
                "Burn a Storage",
                "Warehouses catch fire. Food is lost, prosperity crumbles.",
                2000, 30, 0.40f, DefaultSkills.Roguery, needsLord: false, needsSettlement: true, skillXp: 500),

            new SchemeDefinition(SchemeType.SpreadTerror,
                "Spread Terror",
                "Random violence shakes the city. Security drops sharply.",
                1500, 25, 0.40f, DefaultSkills.Roguery, needsLord: false, needsSettlement: true, skillXp: 400),

            new SchemeDefinition(SchemeType.SpreadRumors,
                "Spread Rumors",
                "Whisper campaigns corrode trust. Loyalty and prosperity fall.",
                1200, 15, 0.35f, DefaultSkills.Charm, needsLord: false, needsSettlement: true, skillXp: 400),

            // LORD SCHEME (same kingdom only) ──────────────────────────────────────
            new SchemeDefinition(SchemeType.VipersCounsel,
                "Viper's Counsel",
                "Poison the king's ear against a rival clan. Your renown rises; theirs falls. Can only target lords within your own kingdom. On failure, lose standing with both the king and the target.",
                1800, 40, 0.40f, DefaultSkills.Charm, needsLord: true, needsSettlement: false, skillXp: 600),

            // LORD SCHEME (targets enemy kingdom via lord) ─────────────────────────
            new SchemeDefinition(SchemeType.ScatterWolves,
                "Scatter the Wolves",
                "Pay deserters and brigands to flood a rival kingdom's roads. Bandit parties surge across their lands, tying up lords and bleeding resources. Target a lord — their whole kingdom suffers.",
                2500, 35, 0.35f, DefaultSkills.Roguery, needsLord: true, needsSettlement: false, skillXp: 800),
        };

        // ── State ─────────────────────────────────────────────────────────────
        private static readonly List<PendingScheme>          _pending       = new List<PendingScheme>();
        private static readonly Dictionary<string, int>      _npcCooldowns  = new Dictionary<string, int>();

        // Per-target repeat-scheme cooldowns. Key = "{SchemeType}:{targetStringId}".
        // Assassination: 14-day hard block. All others: 7-day 5× cost inflation.
        private static readonly Dictionary<string, int>      _targetCooldowns    = new Dictionary<string, int>();

        // Keys in _targetCooldowns that were set by the player — notified when they expire.
        private static readonly HashSet<string>               _playerCooldownKeys = new HashSet<string>();

        private static readonly Random _rng = new Random();

        // Debug: when true, player schemes cost nothing and always succeed.
        // Toggled via Ctrl+Shift+F10 on the campaign map.
        internal static bool DebugFree = false;

        // ── Retaliation window ────────────────────────────────────────────────
        // When a scheme resolves against the player, open a 1-day window where
        // all player-queued schemes cost 50% less (gold and influence).
        private static int _retaliationDays = 0;
        internal static bool PlayerRetaliationActive => _retaliationDays > 0;

        // ── Global player cooldown ────────────────────────────────────────────
        // Set by the minigame on resolution (3 days) or abort (2 days).
        // Prevents spamming the scheme menu immediately after any operation.
        private static int _playerGlobalCooldown = 0;
        internal static bool PlayerOnGlobalCooldown    => _playerGlobalCooldown > 0;
        internal static int  PlayerGlobalCooldownDays  => _playerGlobalCooldown;

        // ── Unresolved player operation ───────────────────────────────────────
        // Costs are paid at commit, but the minigame runs through deferred
        // inquiries that do not survive a save/load. This record persists the
        // committed operation so a reload mid-operation re-launches the Gambit
        // instead of silently eating the gold and influence.
        private static int    _pendingOpType   = -1; // -1 = none
        private static string _pendingOpHeroId = "";
        private static string _pendingOpSettId = "";

        internal static void SetPendingPlayerOperation(SchemeType type, Hero targetHero, Settlement targetSett)
        {
            _pendingOpType   = (int)type;
            _pendingOpHeroId = targetHero?.StringId ?? "";
            _pendingOpSettId = targetSett?.StringId ?? "";
        }

        internal static void ClearPendingPlayerOperation()
        {
            _pendingOpType   = -1;
            _pendingOpHeroId = "";
            _pendingOpSettId = "";
        }

        /// True when a committed player operation never reached a terminal state
        /// (extract / bust / abort / rounds exhausted) — e.g. a reload mid-Gambit.
        internal static bool TryGetPendingPlayerOperation(out SchemeDefinition def,
            out Hero targetHero, out Settlement targetSett)
        {
            def = null; targetHero = null; targetSett = null;
            if (_pendingOpType < 0) return false;
            def = GetDefinition((SchemeType)_pendingOpType);
            if (def == null) { ClearPendingPlayerOperation(); return false; }
            targetHero = FindHero(_pendingOpHeroId);
            targetSett = FindSettlement(_pendingOpSettId);
            // Target gone (dead hero / removed settlement) — drop the operation.
            if (targetHero == null && targetSett == null)
            { ClearPendingPlayerOperation(); return false; }
            return true;
        }

    }
}
