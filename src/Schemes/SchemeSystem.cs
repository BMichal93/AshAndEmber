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
    internal static class SchemeSystem
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
                2200, 40, 0.32f, DefaultSkills.Charm, needsLord: false, needsSettlement: true, skillXp: 750),

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

        // ── Public API ────────────────────────────────────────────────────────
        internal static void Initialize()
        {
            _definitions = null;
            _pending.Clear();
            _npcCooldowns.Clear();
            _targetCooldowns.Clear();
            _playerCooldownKeys.Clear();
            _retaliationDays      = 0;
            _playerGlobalCooldown = 0;
            ClearPendingPlayerOperation();
        }

        /// Returns true if the player already has a scheme pending execution.
        internal static bool PlayerHasPendingScheme()
            => _pending.Any(p => p.IsPlayer);

        /// Returns one NPC scheme currently targeting the player (picked at random if multiple).
        internal static bool TryGetSchemeAgainstPlayer(out Hero instigator, out SchemeType type)
        {
            instigator = null; type = SchemeType.Assassinate;
            var against = _pending.Where(p => !p.IsPlayer && p.TargetHeroId == Hero.MainHero?.StringId).ToList();
            if (against.Count == 0) return false;
            var s = against[_rng.Next(against.Count)];
            instigator = FindHero(s.InstigatorId);
            type = s.Type;
            return true;
        }

        /// Cancels all NPC schemes against the player launched by the given instigator.
        internal static void CancelSchemesFromInstigator(Hero instigator)
        {
            _pending.RemoveAll(p => !p.IsPlayer
                && p.TargetHeroId == Hero.MainHero?.StringId
                && p.InstigatorId == instigator?.StringId);
        }

        /// True when the pending scheme is aimed at the player or a player-clan fief.
        private static bool TargetsPlayerInterests(PendingScheme p)
        {
            if (p.TargetHeroId == Hero.MainHero?.StringId) return true;
            if (!string.IsNullOrEmpty(p.TargetSettlementId))
            {
                var s = FindSettlement(p.TargetSettlementId);
                return s?.OwnerClan != null && s.OwnerClan == Hero.MainHero?.Clan;
            }
            return false;
        }

        /// Finds one pending NPC scheme aimed at the player or their fiefs
        /// (picked at random if multiple). Used by the counter-intelligence sweep.
        internal static PendingScheme FindSchemeAgainstPlayerInterests()
        {
            try
            {
                var list = _pending.Where(p => !p.IsPlayer && TargetsPlayerInterests(p)).ToList();
                return list.Count == 0 ? null : list[_rng.Next(list.Count)];
            }
            catch { return null; }
        }

        internal static void RemovePendingScheme(PendingScheme s)
        {
            if (s != null) _pending.Remove(s);
        }

        internal static Hero FindHeroById(string id) => FindHero(id);

        // Cooldown key for a given scheme+target combination.
        private static string CooldownKey(SchemeType type, string targetId)
            => $"{(int)type}:{targetId ?? ""}";

        /// True when assassination is in the hard-block window for this lord.
        internal static bool IsHardBlocked(SchemeType type, Hero targetHero, Settlement targetSett)
        {
            if (type != SchemeType.Assassinate) return false;
            string id  = targetHero?.StringId ?? targetSett?.StringId ?? "";
            string key = CooldownKey(type, id);
            return _targetCooldowns.TryGetValue(key, out int cd) && cd > 0;
        }

        /// Returns true if a repeat-scheme cooldown is active for this target
        /// (non-assassination schemes: cost will be inflated 5×).
        internal static bool IsOnCooldown(SchemeType type, Hero targetHero, Settlement targetSett)
        {
            string id  = targetHero?.StringId ?? targetSett?.StringId ?? "";
            string key = CooldownKey(type, id);
            return _targetCooldowns.TryGetValue(key, out int cd) && cd > 0;
        }

        /// Effective gold cost, accounting for tier scaling and repeat-cooldown inflation.
        internal static int ComputeGoldCost(SchemeDefinition def,
            Hero targetHero, Settlement targetSett, bool ignoreCooldown = false)
        {
            int tier = targetHero?.Clan?.Tier ?? targetSett?.OwnerClan?.Tier ?? 0;
            float tierMult = 1f + tier * 0.40f;                       // 1.0× – 3.4×
            int cost = (int)(def.GoldCost * tierMult);
            if (!ignoreCooldown && IsOnCooldown(def.Type, targetHero, targetSett))
                cost *= 5;                                             // 5× penalty for repeat use
            return cost;
        }

        /// Effective influence cost, scaling exponentially with target clan tier.
        /// 1.0× at tier 0, ~7.5× at tier 6 — affordable for minor clans, punishing for high-tier.
        internal static int ComputeInfluenceCost(SchemeDefinition def,
            Hero targetHero, Settlement targetSett)
        {
            int tier = targetHero?.Clan?.Tier ?? targetSett?.OwnerClan?.Tier ?? 0;
            float tierMult = (float)Math.Pow(1.4, tier); // 1.0, 1.4, 2.0, 2.7, 3.8, 5.4, 7.5
            return (int)(def.InfluenceCost * tierMult);
        }

        /// Queue a scheme. Gold and influence are deducted immediately.
        /// Returns false if the instigator cannot afford it, or if the player
        /// already has a scheme in flight (one-at-a-time limit).
        internal static bool QueueScheme(Hero instigator, SchemeType type,
            Hero targetHero, Settlement targetSettlement, bool isPlayer)
        {
            var def = GetDefinition(type);
            if (def == null) return false;

            // Player is limited to one pending scheme at a time for balance
            if (isPlayer && PlayerHasPendingScheme()) return false;

            // Assassination: hard block while cooldown is active (no retrying same lord)
            if (IsHardBlocked(type, targetHero, targetSettlement)) return false;

            int effectiveGold = ComputeGoldCost(def, targetHero, targetSettlement);
            int effectiveInf  = ComputeInfluenceCost(def, targetHero, targetSettlement);

            // Retaliation window: player schemes cost 50% less for 1 day after an NPC scheme resolves against them
            if (isPlayer && _retaliationDays > 0)
            {
                effectiveGold = effectiveGold / 2;
                effectiveInf  = effectiveInf  / 2;
            }

            if (!isPlayer || !DebugFree)
            {
                if (instigator.Gold < effectiveGold) return false;
                if ((instigator.Clan?.Influence ?? 0) < effectiveInf) return false;
                try { instigator.Gold -= effectiveGold; } catch { }
                try { if (instigator.Clan != null) instigator.Clan.Influence -= effectiveInf; } catch { }
            }

            _pending.Add(new PendingScheme
            {
                InstigatorId       = instigator.StringId,
                Type               = type,
                TargetHeroId       = targetHero?.StringId       ?? "",
                TargetSettlementId = targetSettlement?.StringId ?? "",
                DaysRemaining      = 1 + _rng.Next(3),
                IsPlayer           = isPlayer
            });

            // Set per-target cooldown: 14 days for assassination (hard block), 7 days for others (5× cost)
            string targetId = targetHero?.StringId ?? targetSettlement?.StringId ?? "";
            if (!string.IsNullOrEmpty(targetId))
            {
                string cdKey = CooldownKey(type, targetId);
                _targetCooldowns[cdKey] = type == SchemeType.Assassinate ? 14 : 7;
                if (isPlayer) _playerCooldownKeys.Add(cdKey);
            }

            return true;
        }

        internal static void DailyTick()
        {
            foreach (var key in _npcCooldowns.Keys.ToList())
            {
                _npcCooldowns[key]--;
                if (_npcCooldowns[key] <= 0) _npcCooldowns.Remove(key);
            }

            foreach (var key in _targetCooldowns.Keys.ToList())
            {
                _targetCooldowns[key]--;
                if (_targetCooldowns[key] <= 0)
                {
                    _targetCooldowns.Remove(key);
                    if (_playerCooldownKeys.Remove(key))
                        try { NotifyCooldownExpired(key); } catch { }
                }
            }

            if (_retaliationDays      > 0) _retaliationDays--;
            if (_playerGlobalCooldown > 0) _playerGlobalCooldown--;

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                _pending[i].DaysRemaining--;
                if (_pending[i].DaysRemaining <= 0)
                {
                    var s = _pending[i];
                    _pending.RemoveAt(i);
                    try { ExecuteScheme(s); } catch { }
                }
            }
        }

        /// Each eligible lord independently rolls to attempt a scheme each day.
        /// Villainous or calculating lords scheme readily; honourable lords almost never do
        /// (they rely on Sanctuary instead). The 20–35-day per-lord cooldown caps each lord
        /// at roughly 1–2 schemes per year.
        internal static void NpcSchemeTick()
        {
            if (Campaign.Current == null) return;
            try
            {
                var candidates = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && !h.IsPrisoner && !h.IsChild
                             && h != Hero.MainHero
                             && h.Clan != null
                             && !_npcCooldowns.ContainsKey(h.StringId)
                             && h.Gold >= 300
                             && h.Clan.Kingdom != null)
                    .ToList();
                if (candidates.Count == 0) return;

                // At most one new NPC scheme per day — prevents notification clusters.
                bool schemeLaunchedToday = false;
                foreach (var lord in candidates)
                {
                    if (schemeLaunchedToday) break;

                    bool schemer;
                    try
                    {
                        // Dishonourable, ruthless, or calculating lords are natural schemers.
                        // Honourable + merciful lords prefer the Sanctuary — they scheme only rarely.
                        schemer = lord.GetTraitLevel(DefaultTraits.Honor)      <= 0
                               || lord.GetTraitLevel(DefaultTraits.Mercy)      <= 0
                               || lord.GetTraitLevel(DefaultTraits.Calculating) >= 1;
                    }
                    catch { schemer = true; }

                    double chance = schemer ? 0.03 : 0.005;
                    if (_rng.NextDouble() > chance) continue;
                    try
                    {
                        int countBefore = _pending.Count(p => !p.IsPlayer);
                        TryQueueNpcScheme(lord);
                        if (_pending.Count(p => !p.IsPlayer) > countBefore)
                            schemeLaunchedToday = true;
                    }
                    catch { }
                }

                // If a scheme was just queued against the player or their fiefs, give a
                // vague whisper. Base 30% chance; Roguery sharpens the ear (up to 75%).
                if (schemeLaunchedToday)
                {
                    try
                    {
                        bool targetsPlayer = _pending.Any(p => !p.IsPlayer
                            && p.DaysRemaining >= 1
                            && TargetsPlayerInterests(p));
                        int hintChance = 30;
                        try { hintChance = Math.Min(75, 30 + (Hero.MainHero?.GetSkillValue(DefaultSkills.Roguery) ?? 0) / 10); } catch { }
                        if (targetsPlayer && _rng.Next(100) < hintChance)
                        {
                            string[] whispers =
                            {
                                "Whispers from the court reach you — someone's designs toward you feel hostile.",
                                "A merchant on the road gives you an odd look, then moves on. You notice. You file it away.",
                                "Someone in the tavern stops talking when you enter. The silence has a shape to it.",
                                "A courier avoids your route. Small things accumulate.",
                                "You catch a name spoken low in a crowded square — yours, you think. The speaker is already gone.",
                            };
                            InformationManager.DisplayMessage(new InformationMessage(
                                whispers[_rng.Next(whispers.Length)],
                                new Color(0.55f, 0.45f, 0.6f)));
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void TryQueueNpcScheme(Hero lord)
        {
            // Filter to schemes the lord can afford at base cost (quick pre-filter).
            var affordable = Definitions
                .Where(d => d.GoldCost <= lord.Gold
                         && d.InfluenceCost <= (lord.Clan?.Influence ?? 0))
                .ToList();
            if (affordable.Count == 0) return;

            // Assassination: weight 1 (rare). All others: weight 3.
            var weighted = new List<SchemeDefinition>();
            foreach (var d in affordable)
                weighted.AddRange(Enumerable.Repeat(d,
                    d.Type == SchemeType.Assassinate ? 1 : 3));
            if (weighted.Count == 0) return;

            var scheme = weighted[_rng.Next(weighted.Count)];

            Hero       targetHero = null;
            Settlement targetSett = null;

            if (scheme.NeedsLord)
            {
                List<Hero> lordTargets;
                if (scheme.Type == SchemeType.VipersCounsel)
                {
                    // Same-kingdom court intrigue — target a rival clan within the same kingdom
                    lordTargets = Hero.AllAliveHeroes
                        .Where(t => t.IsLord && t.IsAlive && !t.IsPrisoner && !t.IsChild
                                 && t != lord
                                 && t.Clan != null && t.Clan != lord.Clan
                                 && lord.Clan.Kingdom != null && !lord.Clan.Kingdom.IsEliminated
                                 && t.Clan.Kingdom == lord.Clan.Kingdom)
                        .ToList();
                }
                else
                {
                    // Open to any lord from a different, non-eliminated foreign kingdom —
                    // not just war enemies. Schemes can be peacetime intelligence operations.
                    lordTargets = Hero.AllAliveHeroes
                        .Where(t => t.IsLord && t.IsAlive && !t.IsPrisoner && !t.IsChild
                                 && t.Clan != null && t.Clan != lord.Clan
                                 && lord.Clan.Kingdom != null && !lord.Clan.Kingdom.IsEliminated
                                 && t.Clan.Kingdom != null && !t.Clan.Kingdom.IsEliminated
                                 && t.Clan.Kingdom != lord.Clan.Kingdom)
                        .ToList();
                }
                if (lordTargets.Count == 0) return;

                // Ashen targets are valid but rare — weight pool 85% non-Ashen / 15% Ashen.
                var nonAshenTargets = lordTargets
                    .Where(t => !ColourLordRegistry.IsAshenLord(t)
                             && t.Clan?.Kingdom?.StringId != AshenKingdomId).ToList();
                var ashenTargets = lordTargets
                    .Where(t => ColourLordRegistry.IsAshenLord(t)
                             || t.Clan?.Kingdom?.StringId == AshenKingdomId).ToList();

                List<Hero> targetPool;
                if (nonAshenTargets.Count > 0 && ashenTargets.Count > 0)
                    targetPool = _rng.NextDouble() < 0.85 ? nonAshenTargets : ashenTargets;
                else
                    targetPool = lordTargets;

                targetHero = targetPool[_rng.Next(targetPool.Count)];
            }
            else
            {
                var targets = Settlement.All
                    .Where(s => (s.IsTown || s.IsCastle)
                             && s.OwnerClan != null
                             && s.OwnerClan != lord.Clan
                             && s.OwnerClan.Kingdom != lord.Clan.Kingdom)
                    .ToList();
                if (targets.Count == 0) return;
                targetSett = targets[_rng.Next(targets.Count)];
            }

            // Verify the lord can actually afford the tier-scaled effective cost before
            // committing — the base-cost pre-filter above misses tier multipliers.
            int effectiveGold = ComputeGoldCost(scheme, targetHero, targetSett);
            int effectiveInf  = ComputeInfluenceCost(scheme, targetHero, targetSett);
            if (lord.Gold < effectiveGold || (lord.Clan?.Influence ?? 0) < effectiveInf) return;

            bool queued = QueueScheme(lord, scheme.Type, targetHero, targetSett, isPlayer: false);
            if (queued)
                _npcCooldowns[lord.StringId] = 20 + _rng.Next(16);
        }

        // ── Success chance ────────────────────────────────────────────────────
        internal static float ComputeSuccessChance(Hero instigator, SchemeType type,
            Hero targetHero, Settlement targetSettlement)
        {
            var def = GetDefinition(type);
            if (def == null || instigator == null) return 0f;

            float chance = def.BaseSuccess;

            // Skill bonus: up to +15% at skill 300
            try
            {
                int skill = instigator.GetSkillValue(def.Skill);
                chance += skill / 600f * 0.30f;
            }
            catch { }

            // Security penalty for settlement targets
            if (targetSettlement?.Town != null)
                try { chance -= targetSettlement.Town.Security / 400f; } catch { }

            // Clan-tier penalty (lower than before so high-tier targets are hard but not impossible)
            if (targetHero?.Clan != null)
                try { chance -= targetHero.Clan.Tier * 0.025f; } catch { }
            else if (targetSettlement?.OwnerClan != null)
                try { chance -= targetSettlement.OwnerClan.Tier * 0.02f; } catch { }

            // Ashen targets resist mortal scheming — cold fire does not yield
            bool isAshenTarget = (targetHero != null && ColourLordRegistry.IsAshenLord(targetHero))
                              || targetSettlement?.OwnerClan?.Kingdom?.StringId == AshenKingdomId;
            if (isAshenTarget) chance -= 0.30f;

            return Math.Max(0.05f, Math.Min(0.85f, chance));
        }

        // ── Execution ─────────────────────────────────────────────────────────
        private static void ExecuteScheme(PendingScheme s)
        {
            if (Campaign.Current == null) return;

            Hero       instigator = FindHero(s.InstigatorId);
            Hero       targetHero = string.IsNullOrEmpty(s.TargetHeroId)       ? null : FindHero(s.TargetHeroId);
            Settlement targetSett = string.IsNullOrEmpty(s.TargetSettlementId) ? null : FindSettlement(s.TargetSettlementId);

            if (instigator == null) return;
            if (!string.IsNullOrEmpty(s.TargetHeroId)       && targetHero == null) return;
            if (!string.IsNullOrEmpty(s.TargetSettlementId) && targetSett == null) return;

            float chance = (s.IsPlayer && DebugFree) ? 1f
                         : ComputeSuccessChance(instigator, s.Type, targetHero, targetSett);
            bool  ok     = _rng.NextDouble() < chance;

            if (ok) ApplySuccess(s, instigator, targetHero, targetSett);
            else    ApplyFailure(s, instigator, targetHero, targetSett);

            // If this was an NPC scheme that resolved against the player, open a 1-day retaliation window.
            if (!s.IsPlayer)
            {
                bool hitPlayer = targetHero == Hero.MainHero
                              || (targetSett?.OwnerClan != null && targetSett.OwnerClan == Hero.MainHero?.Clan);
                if (hitPlayer)
                {
                    _retaliationDays = 1;
                    try
                    {
                        MBInformationManager.AddQuickInformation(
                            new TextObject("The fire answers. For the next day, all your schemes cost half their price."));
                    }
                    catch { }
                }
            }
        }

        // ── Success effects ───────────────────────────────────────────────────
        private static void ApplySuccess(PendingScheme s, Hero instigator,
            Hero targetHero, Settlement targetSett)
        {
            string inst = instigator.Name?.ToString() ?? "Someone";
            var    col  = s.IsPlayer ? new Color(0.45f, 0.30f, 0.60f)  // shadowy violet
                                     : new Color(0.65f, 0.25f, 0.25f); // dark red for NPC
            try
            {
                switch (s.Type)
                {
                    // ── Assassinate ───────────────────────────────────────────
                    case SchemeType.Assassinate:
                        if (targetHero == null || !targetHero.IsAlive) break;
                        string tAss = targetHero.Name?.ToString() ?? "the lord";
                        if (targetHero == Hero.MainHero)
                        {
                            // Player cannot be killed by assassination — wounded instead.
                            try { targetHero.MakeWounded(); } catch { }
                            Notify(s,
                                "An assassin found you in the night. The blade missed the mark — you are wounded, not dead. Watch your back.",
                                col);
                        }
                        else
                        {
                            try { KillCharacterAction.ApplyByMurder(targetHero, null, false); } catch { }
                            Notify(s,
                                $"Done. {tAss} was found dead this morning — no witnesses, no clear wound.",
                                col);
                        }
                        break;

                    // ── Spread Terror ─────────────────────────────────────────
                    case SchemeType.SpreadTerror:
                        if (targetSett?.Town == null) break;
                        float drop = 25f + _rng.Next(20);
                        try { targetSett.Town.Security = Math.Max(0f, targetSett.Town.Security - drop); } catch { }
                        Notify(s,
                            $"Violence erupts across {targetSett.Name}. Security falls sharply.",
                            col);
                        break;

                    // ── Poison Well ───────────────────────────────────────────
                    case SchemeType.PoisonWell:
                        if (targetSett?.Town?.GarrisonParty?.MemberRoster == null) break;
                        int toKill = 20 + _rng.Next(41);
                        int killed = 0;
                        try
                        {
                            foreach (var e in targetSett.Town.GarrisonParty.MemberRoster.GetTroopRoster().ToList())
                            {
                                if (e.Character.IsHero) continue;
                                int remove = Math.Min(e.Number - e.WoundedNumber, toKill - killed);
                                if (remove <= 0) continue;
                                targetSett.Town.GarrisonParty.MemberRoster.AddToCounts(e.Character, -remove);
                                killed += remove;
                                if (killed >= toKill) break;
                            }
                        }
                        catch { }
                        Notify(s,
                            $"Sickness swept the barracks of {targetSett.Name}. {killed} militia are dead.",
                            col);
                        break;

                    // ── Stage Coup ────────────────────────────────────────────
                    case SchemeType.StageCoup:
                        if (targetSett?.Town == null) break;
                        try { targetSett.Town.Loyalty  = Math.Max(0f, targetSett.Town.Loyalty  - 40f); } catch { }
                        try { targetSett.Town.Security = Math.Max(0f, targetSett.Town.Security - 35f); } catch { }
                        Notify(s,
                            $"The garrison officers took the coin and stepped aside. Loyalty collapses in {targetSett.Name}.",
                            col);
                        break;

                    // ── Spread Rumors ─────────────────────────────────────────
                    case SchemeType.SpreadRumors:
                        if (targetSett?.Town == null) break;
                        try { targetSett.Town.Loyalty    = Math.Max(0f,  targetSett.Town.Loyalty    - 15f); } catch { }
                        try { targetSett.Town.Prosperity = Math.Max(10f, targetSett.Town.Prosperity * 0.92f); } catch { }
                        Notify(s,
                            $"Whispers have taken hold in {targetSett.Name}. Loyalty and prosperity fall.",
                            col);
                        break;

                    // ── Burn Storage ──────────────────────────────────────────
                    case SchemeType.BurnStorage:
                        if (targetSett?.Town == null) break;
                        try { targetSett.Town.FoodStocks  = Math.Max(10f, targetSett.Town.FoodStocks  * 0.50f); } catch { }
                        try { targetSett.Town.Prosperity  = Math.Max(10f, targetSett.Town.Prosperity  * 0.85f); } catch { }
                        Notify(s,
                            $"Warehouses burned through the night in {targetSett.Name}. Half the food stocks are lost.",
                            col);
                        break;

                    // ── Bribe Soldiers ────────────────────────────────────────
                    case SchemeType.BribeSoldiers:
                        if (targetSett?.Town?.GarrisonParty?.MemberRoster == null) break;
                        int toDesert = 20 + _rng.Next(31);
                        int deserted = 0;
                        try
                        {
                            foreach (var e in targetSett.Town.GarrisonParty.MemberRoster.GetTroopRoster().ToList())
                            {
                                if (e.Character.IsHero) continue;
                                int remove = Math.Min(e.Number - e.WoundedNumber, toDesert - deserted);
                                if (remove <= 0) continue;
                                targetSett.Town.GarrisonParty.MemberRoster.AddToCounts(e.Character, -remove);
                                deserted += remove;
                                if (deserted >= toDesert) break;
                            }
                        }
                        catch { }
                        Notify(s,
                            $"{deserted} soldiers left their posts in {targetSett.Name}. The garrison is weakened.",
                            col);
                        break;

                    // ── Forge Documents ───────────────────────────────────────
                    case SchemeType.ForgeDocuments:
                        if (targetHero == null || !targetHero.IsAlive) break;
                        string tForg = targetHero.Name?.ToString() ?? "the lord";
                        Hero factionLeader = targetHero.Clan?.Kingdom?.Leader;
                        if (factionLeader != null && factionLeader != targetHero && factionLeader.IsAlive)
                            try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(targetHero, factionLeader, -55, false); } catch { }
                        Notify(s,
                            $"Forged letters reached {(factionLeader?.Name?.ToString() ?? "the faction leader")}. {tForg}'s standing with their lord is shaken.",
                            col);
                        break;

                    // ── Hire Assassin ─────────────────────────────────────────
                    case SchemeType.HireAssassin:
                        if (targetHero == null || !targetHero.IsAlive) break;
                        string tHire = targetHero.Name?.ToString() ?? "the lord";
                        try
                        {
                            if (targetHero.PartyBelongedTo?.MemberRoster != null)
                            {
                                foreach (var e in targetHero.PartyBelongedTo.MemberRoster.GetTroopRoster().ToList())
                                {
                                    if (e.Character.IsHero) continue;
                                    int toWound = Math.Max(1, (e.Number - e.WoundedNumber) / 5);
                                    if (toWound <= 0) continue;
                                    try { targetHero.PartyBelongedTo.MemberRoster.AddToCounts(e.Character, 0, false, toWound); } catch { }
                                }
                            }
                        }
                        catch { }
                        Notify(s,
                            $"The blade bloodied {tHire}'s escort and broke off. The warband is wounded and shaken.",
                            col);
                        break;

                    // ── False Accusations ─────────────────────────────────────
                    case SchemeType.FalseAccusations:
                        if (targetHero == null || !targetHero.IsAlive || targetHero.Clan == null) break;
                        string tAcc  = targetHero.Name?.ToString() ?? "the lord";
                        string cAcc  = targetHero.Clan.Name?.ToString() ?? "their clan";
                        // 5% flat renown loss, floor 50 — meaningful at any clan size
                        float renown = Math.Max(50f, targetHero.Clan.Renown * 0.05f);
                        try { targetHero.Clan.Renown = Math.Max(0f, targetHero.Clan.Renown - renown); } catch { }
                        // Also damage relations between instigator and target (they'll suspect someone)
                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, targetHero, -20, false); } catch { }
                        Notify(s,
                            $"Slander reached the right ears. {cAcc}'s renown takes a visible hit.",
                            col);
                        break;

                    // ── Viper's Counsel ───────────────────────────────────────
                    case SchemeType.VipersCounsel:
                        if (targetHero == null || !targetHero.IsAlive || targetHero.Clan == null) break;
                        string tVipr  = targetHero.Name?.ToString() ?? "the lord";
                        string cVipr  = targetHero.Clan.Name?.ToString() ?? "their clan";
                        // Target loses 7% renown (floor 50) — more than FalseAccusations, justified by the king's direct involvement
                        float viprLoss = Math.Max(50f, targetHero.Clan.Renown * 0.07f);
                        try { targetHero.Clan.Renown = Math.Max(0f, targetHero.Clan.Renown - viprLoss); } catch { }
                        // Instigator clan gains renown — the contrast is the point
                        float viprGain = 30f + _rng.Next(21); // 30–50
                        try { if (instigator.Clan != null) instigator.Clan.Renown += viprGain; } catch { }
                        Notify(s,
                            $"The king's ear was turned against {cVipr}. Their renown falls; {inst}'s rises.",
                            col);
                        break;

                    // ── Scatter the Wolves ────────────────────────────────────
                    case SchemeType.ScatterWolves:
                        if (targetHero?.Clan?.Kingdom == null) break;
                        Kingdom scatterKingdom = targetHero.Clan.Kingdom;
                        string scatterKingdomName = scatterKingdom.Name?.ToString() ?? "the kingdom";
                        int partyCount = 5 + _rng.Next(4); // 5–8 parties
                        int scatterSpawned = 0;
                        try { scatterSpawned = SpawnBanditsInKingdom(scatterKingdom, partyCount); } catch { }
                        Notify(s,
                            $"{scatterSpawned} bandit parties now roam {scatterKingdomName}'s roads. Their lords will spend weeks chasing shadows.",
                            col);
                        break;
                }
            }
            catch { }

            // Award skill XP to the instigator on any successful scheme.
            try
            {
                var def = GetDefinition(s.Type);
                if (def?.Skill != null && def.SkillXp > 0)
                    instigator.HeroDeveloper?.AddSkillXp(def.Skill, def.SkillXp);
            }
            catch { }
        }

        // ── Failure effects ───────────────────────────────────────────────────
        // 70% — agent fled: scheme dissolved, no trace.
        // 30% — agent caught: crime rating, heavy relations penalty, possible war.
        private static void ApplyFailure(PendingScheme s, Hero instigator,
            Hero targetHero, Settlement targetSett)
        {
            // VipersCounsel always surfaces on failure — there is no silent slip when
            // the king's court is involved. The target is always told and the king sours
            // on the manipulator regardless of whether an agent was literally "caught".
            if (s.Type == SchemeType.VipersCounsel)
            {
                string tVFail = targetHero?.Name?.ToString() ?? "the lord";
                if (targetHero != null && targetHero.IsAlive && targetHero != instigator)
                {
                    int tDelta = -(50 + _rng.Next(21)); // −50 to −70
                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, targetHero, tDelta, false); } catch { }
                }
                Hero king = instigator.Clan?.Kingdom?.Leader;
                if (king != null && king.IsAlive && king != instigator && king != targetHero)
                {
                    int kDelta = -(30 + _rng.Next(21)); // −30 to −50
                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, king, kDelta, false); } catch { }
                }
                Notify(s,
                    $"EXPOSED — {tVFail} learned of the plot before the king did. Relations with both have suffered.",
                    new Color(0.80f, 0.20f, 0.18f));
                return;
            }

            bool caught = _rng.NextDouble() < 0.30;

            string inst    = instigator.Name?.ToString() ?? "Someone";
            string tName   = targetHero?.Name?.ToString()
                          ?? targetSett?.Name?.ToString()
                          ?? "the target";
            string tOwner  = (targetSett?.OwnerClan?.Leader ?? targetSett?.MapFaction?.Leader as Hero)
                              ?.Name?.ToString() ?? "";

            if (!caught)
            {
                // 30% chance the blade found the target but didn't finish —
                // the party is bloodied and shaken even in failure.
                bool nearMiss = s.Type == SchemeType.Assassinate && _rng.NextDouble() < 0.30;
                if (nearMiss && targetHero != null && targetHero.IsAlive)
                {
                    try
                    {
                        if (targetHero.PartyBelongedTo?.MemberRoster != null)
                        {
                            foreach (var e in targetHero.PartyBelongedTo.MemberRoster.GetTroopRoster().ToList())
                            {
                                if (e.Character.IsHero) continue;
                                int toWound = Math.Max(1, (e.Number - e.WoundedNumber) / 6);
                                if (toWound <= 0) continue;
                                try { targetHero.PartyBelongedTo.MemberRoster.AddToCounts(e.Character, 0, false, toWound); } catch { }
                            }
                        }
                    }
                    catch { }
                    if (s.IsPlayer)
                        Notify(s,
                            $"The blade reached {tName}'s escort but fled before finishing. The lord lives; the coin is spent.",
                            new Color(0.60f, 0.50f, 0.30f));
                    return;
                }

                // Silent failure — agent slipped away, nothing to trace
                if (s.IsPlayer)
                    Notify(s,
                        $"No opening found. The scheme against {tName} is dissolved.",
                        new Color(0.55f, 0.55f, 0.55f));
                return;
            }

            // ── Caught: apply consequences ────────────────────────────────────
            try
            {
                // Crime rating in target's kingdom
                var targetKingdom = targetHero?.Clan?.Kingdom
                                 ?? targetSett?.OwnerClan?.Kingdom
                                 ?? (targetSett?.MapFaction as Kingdom);
                // Only apply crime rating for player schemes — the API affects the player's
                // clan regardless of who instigator is, so calling it for NPC schemes would
                // incorrectly penalise the player for plots they had no part in.
                if (s.IsPlayer && targetKingdom != null && !targetKingdom.IsEliminated)
                {
                    float crimeDelta = 30f + _rng.Next(31); // 30–60
                    try { ChangeCrimeRatingAction.Apply(targetKingdom, crimeDelta, false); } catch { }
                }

                // Relations penalty with target / settlement owner
                Hero penaltyTarget = targetHero
                    ?? targetSett?.OwnerClan?.Leader
                    ?? targetSett?.MapFaction?.Leader as Hero;

                if (penaltyTarget != null && penaltyTarget.IsAlive && penaltyTarget != instigator)
                {
                    int relDelta = -(60 + _rng.Next(21)); // −60 to −80
                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, penaltyTarget, relDelta, false); } catch { }
                }

                // War declaration: assassination or coup caught, 40% chance, different kingdoms
                bool isWarTrigger = s.Type == SchemeType.Assassinate || s.Type == SchemeType.StageCoup;
                if (isWarTrigger && _rng.NextDouble() < 0.40)
                {
                    var instigKingdom = instigator.Clan?.Kingdom;
                    if (instigKingdom != null && targetKingdom != null
                        && instigKingdom != targetKingdom
                        && !instigKingdom.IsEliminated && !targetKingdom.IsEliminated
                        && !instigKingdom.IsAtWarWith(targetKingdom))
                    {
                        try { DeclareWarAction.ApplyByDefault(instigKingdom, targetKingdom); } catch { }
                    }
                }

                // Flavor notification
                string consequence = isWarTrigger
                    ? "War may follow."
                    : s.IsPlayer
                        ? "The damage to your standing is lasting."
                        : $"{inst}'s standing is damaged.";
                string ownerLine   = !string.IsNullOrEmpty(tOwner) ? $" {tOwner} knows." : "";

                Notify(s,
                    $"EXPOSED — {inst}'s plot against {tName} is known.{ownerLine} Crime rating rises; relations plummet. {consequence}",
                    new Color(0.80f, 0.20f, 0.18f));
            }
            catch { }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        // Fires when a player-set per-target cooldown expires.
        // Key format: "{schemeTypeInt}:{targetStringId}"
        private static void NotifyCooldownExpired(string key)
        {
            var parts = key.Split(new[] { ':' }, 2);
            if (parts.Length < 2) return;
            if (!int.TryParse(parts[0], out int typeInt)) return;
            var  type       = (SchemeType)typeInt;
            bool hardBlock  = type == SchemeType.Assassinate;
            var  def        = GetDefinition(type);
            string scheme   = def?.Name ?? "The scheme";

            // Resolve target name
            string targetId = parts[1];
            string target   = "the target";
            try
            {
                Hero h = FindHero(targetId);
                if (h != null) target = h.Name?.ToString() ?? target;
                else
                {
                    Settlement s = FindSettlement(targetId);
                    if (s != null) target = s.Name?.ToString() ?? target;
                }
            }
            catch { }

            string msg = hardBlock
                ? $"Contacts reset — the path to {target} is open again. Assassination may be attempted."
                : $"Network cooled — {scheme} against {target} may be repeated at normal cost.";

            MBInformationManager.AddQuickInformation(new TextObject(msg));
        }

        internal static SchemeDefinition GetDefinition(SchemeType type)
            => Definitions.FirstOrDefault(d => d.Type == type);

        private static Hero FindHero(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            try { return Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == id)
                       ?? Hero.DeadOrDisabledHeroes.FirstOrDefault(h => h.StringId == id); }
            catch { return null; }
        }

        private static Settlement FindSettlement(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            try { return Settlement.All.FirstOrDefault(s => s.StringId == id); }
            catch { return null; }
        }

        // Notify: player schemes → popup notification; NPC schemes → console log.
        // Schemes targeting the player are always shown as popups regardless of instigator.
        private static void Notify(PendingScheme s, string text, Color color)
        {
            bool targetIsPlayer = s.TargetHeroId == Hero.MainHero?.StringId;
            if (s.IsPlayer || targetIsPlayer)
                MBInformationManager.AddQuickInformation(new TextObject(text));
            else
                InformationManager.DisplayMessage(new InformationMessage(text, color));
        }

        // Spawns bandit parties throughout the target kingdom, each tied to the
        // nearest hideout — critical to avoid the null-hideout crash. Mirrors the
        // SpawnLooterParty pattern in CampaignMapEvents.cs exactly.
        // Returns the number of parties actually created.
        internal static int SpawnBanditsInKingdom(Kingdom kingdom, int partyCount)
        {
            if (kingdom == null || kingdom.IsEliminated) return 0;

            Clan banditClan = Clan.BanditFactions.FirstOrDefault(c => c != null && !c.IsEliminated);
            if (banditClan == null) return 0;
            var pt = banditClan.DefaultPartyTemplate;
            if (pt == null) return 0;

            CharacterObject troop =
                MBObjectManager.Instance.GetObject<CharacterObject>("looter")
             ?? MBObjectManager.Instance.GetObject<CharacterObject>("mountain_bandit");
            if (troop == null) return 0;

            // Gather settlement positions in the target kingdom as spawn anchors.
            var anchors = Settlement.All
                .Where(s => (s.IsTown || s.IsCastle) && s.OwnerClan?.Kingdom == kingdom)
                .Select(s => s.GetPosition2D)
                .ToList();
            if (anchors.Count == 0) return 0;

            int spawned = 0;
            for (int i = 0; i < partyCount; i++)
            {
                try
                {
                    Vec2 anchor = anchors[_rng.Next(anchors.Count)];

                    // 3-level hideout fallback — never pass null to CreateBanditParty.
                    Hideout hideout = null;
                    try
                    {
                        Settlement hs = banditClan.Settlements.FirstOrDefault(s => s?.Hideout != null);
                        if (hs == null)
                            hs = Settlement.All
                                .Where(s => s?.Hideout != null)
                                .OrderBy(s => (s.GetPosition2D.x - anchor.x) * (s.GetPosition2D.x - anchor.x)
                                            + (s.GetPosition2D.y - anchor.y) * (s.GetPosition2D.y - anchor.y))
                                .FirstOrDefault();
                        if (hs == null) hs = Settlement.All.FirstOrDefault(s => s?.Hideout != null);
                        hideout = hs?.Hideout;
                    }
                    catch { }
                    if (hideout == null) continue;

                    const float scatter = 5f;
                    Vec2 sp = anchor + new Vec2(
                        (float)(_rng.NextDouble() - 0.5) * scatter * 2f,
                        (float)(_rng.NextDouble() - 0.5) * scatter * 2f);
                    var cv = new CampaignVec2(sp, true);

                    int troops = 20 + _rng.Next(16); // 20–35 per party
                    string pid = "scatter_wolves_" + _rng.Next(999999).ToString("D6");

                    MobileParty party = BanditPartyComponent.CreateBanditParty(pid, banditClan, hideout, false, pt, cv);
                    if (party == null) continue;
                    party.MemberRoster.AddToCounts(troop, troops);
                    spawned++;
                }
                catch { }
            }
            return spawned;
        }

        // ── Minigame API ──────────────────────────────────────────────────────
        // Called by SchemeMinigame after a player operation resolves.

        /// Stamps only the per-target cooldown (no global). Called from CommitScheme
        /// BEFORE the deferred minigame launches so a save-reload between cost deduction
        /// and the first phase cannot bypass the cooldown and let the player retry for free.
        internal static void PreStampTargetCooldown(SchemeType type, Hero targetHero, Settlement targetSett)
        {
            string targetId = targetHero?.StringId ?? targetSett?.StringId ?? "";
            if (string.IsNullOrEmpty(targetId)) return;
            string cdKey = CooldownKey(type, targetId);
            _targetCooldowns[cdKey] = type == SchemeType.Assassinate ? 14 : 7;
            _playerCooldownKeys.Add(cdKey);
        }

        /// Stamps the per-target cooldown and briefly disables the scheme menu.
        /// Called by SchemeMinigame on extract, bust, or abort. Overwrites the pre-stamp
        /// from PreStampTargetCooldown with the outcome-correct value.
        internal static void SetPlayerCooldown(SchemeType type, Hero targetHero,
            Settlement targetSett, int days = -1)
        {
            // Every minigame terminal path (extract, bust, abort, rounds exhausted)
            // ends here — the committed operation is resolved.
            ClearPendingPlayerOperation();

            string targetId = targetHero?.StringId ?? targetSett?.StringId ?? "";
            if (string.IsNullOrEmpty(targetId)) return;
            string cdKey = CooldownKey(type, targetId);
            int cdDays = days >= 0 ? days : (type == SchemeType.Assassinate ? 14 : 7);
            _targetCooldowns[cdKey] = cdDays;
            _playerCooldownKeys.Add(cdKey);
            // Brief global cooldown prevents immediate menu re-entry after resolution.
            // Overwrite directly — the minigame calls this at the end of every path,
            // so the final call should always win (don't clamp to the pre-stamp value).
            _playerGlobalCooldown = days >= 0 ? days : 3;
        }

        /// Applies the minigame outcome for a player operation.
        /// Routes to ApplySuccess, a silent-retreat notification, or ApplyBreakConsequence.
        internal static void ApplyPlayerSchemeOutcome(SchemeType type, Hero instigator,
            Hero targetHero, Settlement targetSett, SchemeOutcome outcome)
        {
            if (instigator == null) return;

            var fake = new PendingScheme
            {
                InstigatorId       = instigator.StringId ?? "",
                Type               = type,
                TargetHeroId       = targetHero?.StringId ?? "",
                TargetSettlementId = targetSett?.StringId  ?? "",
                DaysRemaining      = 0,
                IsPlayer           = true
            };

            switch (outcome)
            {
                case SchemeOutcome.SmallLoss:
                {
                    string tName = targetHero?.Name?.ToString()
                                ?? targetSett?.Name?.ToString() ?? "the target";
                    MBInformationManager.AddQuickInformation(
                        new TextObject($"Your agent withdrew. The operation against {tName} dissolved without trace."));
                    break;
                }
                case SchemeOutcome.Success:
                    try { ApplySuccess(fake, instigator, targetHero, targetSett); } catch { }
                    break;

                case SchemeOutcome.Bust:
                    try { ApplyBreakConsequence(type, instigator, targetHero, targetSett); } catch { }
                    break;
            }

            // Award full skill XP on success; partial (1/3) on bust.
            try
            {
                var def = GetDefinition(type);
                if (def?.Skill != null && def.SkillXp > 0)
                {
                    int xp = outcome == SchemeOutcome.Success ? def.SkillXp
                           : outcome == SchemeOutcome.Bust    ? def.SkillXp / 3
                           : 0;
                    if (xp > 0)
                        instigator?.HeroDeveloper?.AddSkillXp(def.Skill, xp);
                }
            }
            catch { }
        }

        /// Per-scheme bust consequences — called directly from the minigame (bust = always caught).
        internal static void ApplyBreakConsequence(SchemeType type, Hero instigator,
            Hero targetHero, Settlement targetSett)
        {
            if (instigator == null) return;

            switch (type)
            {
                case SchemeType.Assassinate:
                {
                    var targetKingdom = targetHero?.Clan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 80f, false); } catch { }
                    if (targetHero != null && targetHero.IsAlive && targetHero != instigator)
                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, targetHero, -80, false); } catch { }
                    var instigKingdom = instigator.Clan?.Kingdom;
                    if (_rng.NextDouble() < 0.60
                        && instigKingdom != null && targetKingdom != null
                        && instigKingdom != targetKingdom
                        && !instigKingdom.IsEliminated && !targetKingdom.IsEliminated
                        && !instigKingdom.IsAtWarWith(targetKingdom))
                        try { DeclareWarAction.ApplyByDefault(instigKingdom, targetKingdom); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — Your assassin was taken alive. Under interrogation, they spoke your name. War may follow."));
                    break;
                }
                case SchemeType.ForgeDocuments:
                {
                    Hero ownLeader = instigator.Clan?.Kingdom?.Leader;
                    if (ownLeader != null && ownLeader.IsAlive && ownLeader != instigator)
                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, ownLeader, -60, false); } catch { }
                    var targetKingdom = targetHero?.Clan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 40f, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The forgery unraveled and traced back to you. Your own lord is asking questions."));
                    break;
                }
                case SchemeType.FalseAccusations:
                {
                    if (instigator.Clan != null)
                    {
                        float loss = Math.Max(80f, instigator.Clan.Renown * 0.10f);
                        try { instigator.Clan.Renown = Math.Max(0f, instigator.Clan.Renown - loss); } catch { }
                    }
                    if (targetHero != null && targetHero.IsAlive)
                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, targetHero, -60, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The slander was too clumsy. It circled back. Your own reputation is now in question."));
                    break;
                }
                case SchemeType.StageCoup:
                {
                    var targetKingdom = targetSett?.OwnerClan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 50f, false); } catch { }
                    var own = Settlement.All.FirstOrDefault(s =>
                        (s.IsTown || s.IsCastle) && s.OwnerClan == instigator.Clan && s.Town != null);
                    if (own?.Town != null)
                    {
                        try { own.Town.Loyalty  = Math.Max(0f, own.Town.Loyalty  - 25f); } catch { }
                        try { own.Town.Security = Math.Max(0f, own.Town.Security - 20f); } catch { }
                    }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The coup collapsed into riots. Your involvement reached the wrong ears — your own holdings suffer."));
                    break;
                }
                case SchemeType.PoisonWell:
                {
                    var targetKingdom = targetSett?.OwnerClan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 70f, false); } catch { }
                    var own = Settlement.All.FirstOrDefault(s =>
                        s.IsTown && s.OwnerClan == instigator.Clan && s.Town != null);
                    if (own?.Town != null)
                        try { own.Town.FoodStocks = Math.Max(10f, own.Town.FoodStocks * 0.60f); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The agent confused the supply lines. Your own stores were poisoned."));
                    break;
                }
                case SchemeType.BribeSoldiers:
                {
                    var targetKingdom = targetSett?.OwnerClan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 60f, false); } catch { }
                    Hero owner = targetSett?.OwnerClan?.Leader;
                    if (owner != null && owner.IsAlive && owner != instigator)
                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, owner, -70, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The soldiers took your coin and marched straight to their captain."));
                    break;
                }
                case SchemeType.BurnStorage:
                {
                    var targetKingdom = targetSett?.OwnerClan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 60f, false); } catch { }
                    if (targetSett?.Town != null)
                    {
                        try { targetSett.Town.FoodStocks = Math.Max(10f, targetSett.Town.FoodStocks * 0.25f); } catch { }
                        try { targetSett.Town.Prosperity = Math.Max(10f, targetSett.Town.Prosperity * 0.70f); } catch { }
                    }
                    Hero owner = targetSett?.OwnerClan?.Leader;
                    if (owner != null && owner.IsAlive && owner != instigator)
                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, owner, -70, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The fire leaped every wall. The destruction is total — and undeniably yours."));
                    break;
                }
                case SchemeType.SpreadTerror:
                {
                    var targetKingdom = targetSett?.OwnerClan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 70f, false); } catch { }
                    Hero owner = targetSett?.OwnerClan?.Leader;
                    if (owner != null && owner.IsAlive && owner != instigator)
                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, owner, -70, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The violence was too organized. It left a trail straight to your doorstep."));
                    break;
                }
                case SchemeType.SpreadRumors:
                {
                    var own = Settlement.All.FirstOrDefault(s =>
                        s.IsTown && s.OwnerClan == instigator.Clan && s.Town != null);
                    if (own?.Town != null)
                    {
                        try { own.Town.Loyalty    = Math.Max(0f,  own.Town.Loyalty    - 20f); } catch { }
                        try { own.Town.Prosperity = Math.Max(10f, own.Town.Prosperity * 0.92f); } catch { }
                    }
                    var targetKingdom = targetSett?.OwnerClan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 40f, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The rumors warped in transit. Now they're about you. Your own people are whispering."));
                    break;
                }
                case SchemeType.VipersCounsel:
                {
                    Hero king = instigator.Clan?.Kingdom?.Leader;
                    if (king != null && king.IsAlive && king != instigator)
                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, king, -80, false); } catch { }
                    if (targetHero != null && targetHero.IsAlive)
                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, targetHero, -60, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The king saw through the veil and recognized the hand behind it. You are no longer welcome at court."));
                    break;
                }
                case SchemeType.HireAssassin:
                {
                    if (Hero.MainHero?.PartyBelongedTo?.MemberRoster != null)
                    {
                        foreach (var e in Hero.MainHero.PartyBelongedTo.MemberRoster.GetTroopRoster().ToList())
                        {
                            if (e.Character.IsHero) continue;
                            int toWound = Math.Max(1, (e.Number - e.WoundedNumber) / 5);
                            if (toWound <= 0) continue;
                            try { Hero.MainHero.PartyBelongedTo.MemberRoster.AddToCounts(e.Character, 0, false, toWound); } catch { }
                        }
                    }
                    var targetKingdom = targetHero?.Clan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 50f, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The blade turned around. Your own escort was ambushed."));
                    break;
                }
                case SchemeType.ScatterWolves:
                {
                    var ownKingdom = instigator.Clan?.Kingdom;
                    if (ownKingdom != null && !ownKingdom.IsEliminated)
                        try { SpawnBanditsInKingdom(ownKingdom, 3); } catch { }
                    var targetKingdom = targetHero?.Clan?.Kingdom;
                    if (targetKingdom != null && !targetKingdom.IsEliminated)
                        try { ChangeCrimeRatingAction.Apply(targetKingdom, 40f, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "BUST — The bandits took your coin and went wherever they pleased — including your own roads."));
                    break;
                }
            }
        }

        // ── Save / Load ───────────────────────────────────────────────────────
        internal static void Save(IDataStore store)
        {
            var types  = _pending.Select(p => (int)p.Type).ToList();
            var insts  = _pending.Select(p => p.InstigatorId).ToList();
            var tHero  = _pending.Select(p => p.TargetHeroId).ToList();
            var tSett  = _pending.Select(p => p.TargetSettlementId).ToList();
            var days   = _pending.Select(p => p.DaysRemaining).ToList();
            var isPlyr = _pending.Select(p => p.IsPlayer ? 1 : 0).ToList();

            store.SyncData("SCH_Types",   ref types);
            store.SyncData("SCH_Insts",   ref insts);
            store.SyncData("SCH_THero",   ref tHero);
            store.SyncData("SCH_TSett",   ref tSett);
            store.SyncData("SCH_Days",    ref days);
            store.SyncData("SCH_IsPlayer",ref isPlyr);

            var cdKeys = _npcCooldowns.Keys.ToList();
            var cdVals = _npcCooldowns.Values.ToList();
            store.SyncData("SCH_CdKeys",  ref cdKeys);
            store.SyncData("SCH_CdVals",  ref cdVals);

            // Restore from loaded data
            if (types != null)
            {
                _pending.Clear();
                for (int i = 0; i < types.Count; i++)
                    _pending.Add(new PendingScheme
                    {
                        Type               = (SchemeType)(types[i]),
                        InstigatorId       = insts?[i]  ?? "",
                        TargetHeroId       = tHero?[i]  ?? "",
                        TargetSettlementId = tSett?[i]  ?? "",
                        DaysRemaining      = days?[i]   ?? 1,
                        IsPlayer           = (isPlyr?[i] ?? 0) != 0
                    });
            }
            if (cdKeys != null && cdVals != null && cdKeys.Count == cdVals.Count)
            {
                _npcCooldowns.Clear();
                for (int i = 0; i < cdKeys.Count; i++)
                    _npcCooldowns[cdKeys[i]] = cdVals[i];
            }

            var tcdKeys = _targetCooldowns.Keys.ToList();
            var tcdVals = _targetCooldowns.Values.ToList();
            store.SyncData("SCH_TcdKeys", ref tcdKeys);
            store.SyncData("SCH_TcdVals", ref tcdVals);
            if (tcdKeys != null && tcdVals != null && tcdKeys.Count == tcdVals.Count)
            {
                _targetCooldowns.Clear();
                for (int i = 0; i < tcdKeys.Count; i++)
                    _targetCooldowns[tcdKeys[i]] = tcdVals[i];
            }

            var pckList = _playerCooldownKeys.ToList();
            store.SyncData("SCH_PckList", ref pckList);
            if (pckList != null)
            {
                _playerCooldownKeys.Clear();
                foreach (var k in pckList) _playerCooldownKeys.Add(k);
            }

            store.SyncData("SCH_RetDays",    ref _retaliationDays);
            store.SyncData("SCH_GlobCd",     ref _playerGlobalCooldown);

            store.SyncData("SCH_PendOpType", ref _pendingOpType);
            store.SyncData("SCH_PendOpHero", ref _pendingOpHeroId);
            store.SyncData("SCH_PendOpSett", ref _pendingOpSettId);
        }
    }
}
