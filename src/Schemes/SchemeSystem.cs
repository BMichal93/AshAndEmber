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
//            − (clanTier × 4%)  [lord targets]  or  − (clanTier × 2%)  [settlement]
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

        internal SchemeDefinition(SchemeType type, string name, string desc,
            int gold, int inf, float baseSuccess, SkillObject skill,
            bool needsLord, bool needsSettlement)
        {
            Type = type; Name = name; Description = desc;
            GoldCost = gold; InfluenceCost = inf; BaseSuccess = baseSuccess;
            Skill = skill; NeedsLord = needsLord; NeedsSettlement = needsSettlement;
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
        internal static readonly SchemeDefinition[] Definitions =
        {
            new SchemeDefinition(SchemeType.Assassinate,
                "Assassinate a Lord",
                "Hire a blade. On success the target dies quietly. On exposure: war may follow.",
                2000, 30, 0.20f, DefaultSkills.Roguery, needsLord: true,  needsSettlement: false),

            new SchemeDefinition(SchemeType.SpreadTerror,
                "Spread Terror",
                "Random violence shakes the city. Security drops sharply.",
                500, 10, 0.45f, DefaultSkills.Roguery, needsLord: false, needsSettlement: true),

            new SchemeDefinition(SchemeType.PoisonWell,
                "Poison a Well",
                "The garrison sickens. Militia die before anyone connects cause to effect.",
                800, 15, 0.40f, DefaultSkills.Roguery, needsLord: false, needsSettlement: true),

            new SchemeDefinition(SchemeType.StageCoup,
                "Stage a Coup",
                "Bribe garrison officers. Loyalty collapses — rebellion becomes likely.",
                1500, 40, 0.20f, DefaultSkills.Charm,   needsLord: false, needsSettlement: true),

            new SchemeDefinition(SchemeType.SpreadRumors,
                "Spread Rumors",
                "Whisper campaigns corrode trust. Loyalty and prosperity fall.",
                300,  5,  0.55f, DefaultSkills.Charm,   needsLord: false, needsSettlement: true),

            new SchemeDefinition(SchemeType.BurnStorage,
                "Burn a Storage",
                "Warehouses catch fire. Food is lost, prosperity crumbles.",
                600, 10, 0.50f, DefaultSkills.Roguery, needsLord: false, needsSettlement: true),

            new SchemeDefinition(SchemeType.BribeSoldiers,
                "Bribe Soldiers",
                "A portion of the garrison deserts. They scatter — no one joins you, they simply leave.",
                1000, 20, 0.35f, DefaultSkills.Charm,  needsLord: false, needsSettlement: true),

            new SchemeDefinition(SchemeType.ForgeDocuments,
                "Forge Documents",
                "Fabricated letters damage a lord's reputation with their own faction.",
                800, 15, 0.40f, DefaultSkills.Charm,   needsLord: true,  needsSettlement: false),

            new SchemeDefinition(SchemeType.HireAssassin,
                "Hire an Assassin (wound)",
                "The blade finds the lord but doesn't finish. Their party is bloodied and weakened.",
                1200, 20, 0.30f, DefaultSkills.Roguery, needsLord: true,  needsSettlement: false),

            new SchemeDefinition(SchemeType.FalseAccusations,
                "False Accusations",
                "Slander carefully placed at the right ears. Clan renown is damaged; their standing erodes.",
                600, 15, 0.45f, DefaultSkills.Charm,   needsLord: true,  needsSettlement: false),
        };

        // ── State ─────────────────────────────────────────────────────────────
        private static readonly List<PendingScheme>          _pending      = new List<PendingScheme>();
        private static readonly Dictionary<string, int>      _npcCooldowns = new Dictionary<string, int>();
        private static readonly Random _rng = new Random();

        // ── Public API ────────────────────────────────────────────────────────
        internal static void Initialize()
        {
            _pending.Clear();
            _npcCooldowns.Clear();
        }

        /// Returns true if the player already has a scheme pending execution.
        internal static bool PlayerHasPendingScheme()
            => _pending.Any(p => p.IsPlayer);

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

            if (instigator.Gold < def.GoldCost) return false;
            if ((instigator.Clan?.Influence ?? 0) < def.InfluenceCost) return false;

            try { instigator.Gold -= def.GoldCost; } catch { }
            try { if (instigator.Clan != null) instigator.Clan.Influence -= def.InfluenceCost; } catch { }

            _pending.Add(new PendingScheme
            {
                InstigatorId       = instigator.StringId,
                Type               = type,
                TargetHeroId       = targetHero?.StringId       ?? "",
                TargetSettlementId = targetSettlement?.StringId ?? "",
                DaysRemaining      = 1 + _rng.Next(3),
                IsPlayer           = isPlayer
            });
            return true;
        }

        internal static void DailyTick()
        {
            foreach (var key in _npcCooldowns.Keys.ToList())
            {
                _npcCooldowns[key]--;
                if (_npcCooldowns[key] <= 0) _npcCooldowns.Remove(key);
            }

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

        /// 3% daily global chance — ~1 NPC scheme every 33 days.
        internal static void NpcSchemeTick()
        {
            if (_rng.NextDouble() > 0.03) return;
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

                var lord = candidates[_rng.Next(candidates.Count)];

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
                    var enemies = Hero.AllAliveHeroes
                        .Where(t => t.IsLord && t.IsAlive && !t.IsPrisoner && !t.IsChild
                                 && t != Hero.MainHero
                                 && t.Clan != null && t.Clan != lord.Clan
                                 && lord.Clan.Kingdom != null && t.Clan.Kingdom != null
                                 && lord.Clan.Kingdom.IsAtWarWith(t.Clan.Kingdom))
                        .ToList();
                    if (enemies.Count == 0) return;
                    targetHero = enemies[_rng.Next(enemies.Count)];
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

                bool queued = QueueScheme(lord, scheme.Type, targetHero, targetSett, isPlayer: false);
                if (queued)
                    _npcCooldowns[lord.StringId] = 20 + _rng.Next(16);
            }
            catch { }
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

            // Clan-tier penalty
            if (targetHero?.Clan != null)
                try { chance -= targetHero.Clan.Tier * 0.04f; } catch { }
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

            float chance = ComputeSuccessChance(instigator, s.Type, targetHero, targetSett);
            bool  ok     = _rng.NextDouble() < chance;

            if (ok) ApplySuccess(s, instigator, targetHero, targetSett);
            else    ApplyFailure(s, instigator, targetHero, targetSett);
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
                        try { KillCharacterAction.ApplyByMurder(targetHero, null, false); } catch { }
                        Notify(s,
                            $"The arrangement proved final. {tAss} was found this morning — " +
                            $"no wound that tells a clear story, no witnesses. " +
                            $"The sort of death that never quite closes. " +
                            $"{inst} received a small folded cloth by way of confirmation.",
                            col);
                        break;

                    // ── Spread Terror ─────────────────────────────────────────
                    case SchemeType.SpreadTerror:
                        if (targetSett?.Town == null) break;
                        float drop = 25f + _rng.Next(20);
                        try { targetSett.Town.Security = Math.Max(0f, targetSett.Town.Security - drop); } catch { }
                        Notify(s,
                            $"Fires were set in three places on the same night in {targetSett.Name}. " +
                            $"A dockmaster was beaten near the market. The city watch is stretched thin. " +
                            $"Security has dropped and the mood on the streets has curdled.",
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
                            $"The sickness moved through the barracks of {targetSett.Name} quietly. " +
                            $"Bad water, the garrison surgeon said. By the time they suspected otherwise, " +
                            $"{killed} militia had already stopped reporting for duty.",
                            col);
                        break;

                    // ── Stage Coup ────────────────────────────────────────────
                    case SchemeType.StageCoup:
                        if (targetSett?.Town == null) break;
                        try { targetSett.Town.Loyalty  = Math.Max(0f, targetSett.Town.Loyalty  - 40f); } catch { }
                        try { targetSett.Town.Security = Math.Max(0f, targetSett.Town.Security - 35f); } catch { }
                        Notify(s,
                            $"The garrison officers of {targetSett.Name} proved more mercenary than loyal. " +
                            $"They opened the door, pocketed the coin, and said nothing. " +
                            $"Loyalty has collapsed. The city may not hold.",
                            col);
                        break;

                    // ── Spread Rumors ─────────────────────────────────────────
                    case SchemeType.SpreadRumors:
                        if (targetSett?.Town == null) break;
                        try { targetSett.Town.Loyalty    = Math.Max(0f,  targetSett.Town.Loyalty    - 15f); } catch { }
                        try { targetSett.Town.Prosperity = Math.Max(10f, targetSett.Town.Prosperity * 0.92f); } catch { }
                        Notify(s,
                            $"The stories reached market stalls and tavern tables in {targetSett.Name} within two days. " +
                            $"Some weren't even invented — the effective rumor is always half-true. " +
                            $"Trust in the city's rulers is measurably lower.",
                            col);
                        break;

                    // ── Burn Storage ──────────────────────────────────────────
                    case SchemeType.BurnStorage:
                        if (targetSett?.Town == null) break;
                        try { targetSett.Town.FoodStocks  = Math.Max(10f, targetSett.Town.FoodStocks  * 0.50f); } catch { }
                        try { targetSett.Town.Prosperity  = Math.Max(10f, targetSett.Town.Prosperity  * 0.85f); } catch { }
                        Notify(s,
                            $"Three warehouses on the south side of {targetSett.Name} caught in the night. " +
                            $"The fire crews did what they could, but the grain quarter burned long enough to matter. " +
                            $"Half the food stocks are ash.",
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
                            $"A quiet word, a correct number, and a direction to walk. " +
                            $"{deserted} soldiers in {targetSett.Name} took the offer. " +
                            $"They didn't go far, but they left the post.",
                            col);
                        break;

                    // ── Forge Documents ───────────────────────────────────────
                    case SchemeType.ForgeDocuments:
                        if (targetHero == null || !targetHero.IsAlive) break;
                        string tForg = targetHero.Name?.ToString() ?? "the lord";
                        Hero factionLeader = targetHero.Clan?.Kingdom?.Leader;
                        if (factionLeader != null && factionLeader != targetHero)
                            try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(targetHero, factionLeader, -30, false); } catch { }
                        Notify(s,
                            $"Forged letters reached {(factionLeader?.Name?.ToString() ?? "the faction leader")}'s hands " +
                            $"by three different routes. They looked genuine enough. " +
                            $"{tForg}'s standing with their own lord has been shaken.",
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
                            $"The hired blade found {tHire}'s company on the road. " +
                            $"They didn't finish the job — either nerve or odds failed them — " +
                            $"but they bloodied the escort before breaking off. The warband is weakened and shaken.",
                            col);
                        break;

                    // ── False Accusations ─────────────────────────────────────
                    case SchemeType.FalseAccusations:
                        if (targetHero == null || !targetHero.IsAlive || targetHero.Clan == null) break;
                        string tAcc  = targetHero.Name?.ToString() ?? "the lord";
                        string cAcc  = targetHero.Clan.Name?.ToString() ?? "their clan";
                        float renown = 25f + _rng.Next(26); // 25–50
                        try { targetHero.Clan.Renown = Math.Max(0f, targetHero.Clan.Renown - renown); } catch { }
                        // Also damage relations between instigator and target (they'll suspect someone)
                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(instigator, targetHero, -20, false); } catch { }
                        Notify(s,
                            $"Rumor, carefully placed, works like water — it finds the cracks and widens them. " +
                            $"Enough voices repeating the same slander at the right ears. " +
                            $"{cAcc}'s reputation has taken a visible hit. {tAcc} cannot easily deny what they haven't heard yet.",
                            col);
                        break;
                }
            }
            catch { }
        }

        // ── Failure effects ───────────────────────────────────────────────────
        // 70% — agent fled: scheme dissolved, no trace.
        // 30% — agent caught: crime rating, heavy relations penalty, possible war.
        private static void ApplyFailure(PendingScheme s, Hero instigator,
            Hero targetHero, Settlement targetSett)
        {
            bool caught = _rng.NextDouble() < 0.30;

            string inst    = instigator.Name?.ToString() ?? "Someone";
            string tName   = targetHero?.Name?.ToString()
                          ?? targetSett?.Name?.ToString()
                          ?? "the target";
            string tOwner  = (targetSett?.OwnerClan?.Leader ?? targetSett?.MapFaction?.Leader as Hero)
                              ?.Name?.ToString() ?? "";

            if (!caught)
            {
                // Silent failure — agent slipped away, nothing to trace
                if (s.IsPlayer)
                    Notify(s,
                        $"Your agent found no opening. The arrangement against {tName} is dissolved. " +
                        $"The coin is gone, and the task remains undone.",
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
                if (targetKingdom != null && instigator.Clan != null)
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
                        && !instigKingdom.IsAtWarWith(targetKingdom))
                    {
                        try { DeclareWarAction.ApplyByDefault(instigKingdom, targetKingdom); } catch { }
                    }
                }

                // Flavor notification
                string consequence = isWarTrigger
                    ? "The discovery may have consequences beyond broken trust."
                    : "The damage to your standing may be lasting.";
                string ownerLine   = !string.IsNullOrEmpty(tOwner) ? $" {tOwner} has been informed." : "";

                Notify(s,
                    $"Scheme EXPOSED — The agent was taken alive and questioned. " +
                    $"{inst}'s plot against {tName} is known.{ownerLine} " +
                    $"Crime rating rises. Relations have plummeted. {consequence}",
                    new Color(0.80f, 0.20f, 0.18f));
            }
            catch { }
        }

        // ── Helpers ───────────────────────────────────────────────────────────
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

        // Notify: player schemes always shown; NPC schemes shown only if
        // high-profile or directed at the player.
        private static void Notify(PendingScheme s, string text, Color color)
        {
            bool isHighProfile  = s.Type == SchemeType.Assassinate
                               || s.Type == SchemeType.StageCoup
                               || s.Type == SchemeType.PoisonWell;
            bool targetIsPlayer = s.TargetHeroId == Hero.MainHero?.StringId;

            if (s.IsPlayer || isHighProfile || targetIsPlayer)
                MBInformationManager.AddQuickInformation(new TextObject(text));
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
        }
    }
}
