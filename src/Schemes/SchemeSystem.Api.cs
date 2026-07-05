// =============================================================================
// ASH AND EMBER — SchemeSystem.Api.cs
// Public scheme API, queueing, daily/NPC scheme ticks.
// Partial of SchemeSystem (shared state lives in SchemeSystem.cs).
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
    internal static partial class SchemeSystem
    {
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
                try { instigator.Gold -= effectiveGold; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { if (instigator.Clan != null) instigator.Clan.Influence -= effectiveInf; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                        try { NotifyCooldownExpired(key); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                    try { ExecuteScheme(s); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                        try { hintChance = Math.Min(75, 30 + (Hero.MainHero?.GetSkillValue(DefaultSkills.Roguery) ?? 0) / 10); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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

    }
}
