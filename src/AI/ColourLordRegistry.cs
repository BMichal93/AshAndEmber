// =============================================================================
// LIFE & DEATH MAGIC — AI/ColourLordRegistry.cs
// Tracks which NPC lords carry the gift (isMage).
// Population target: ~20% of all lords. Weekly regulator keeps it stable.
// NPC campaign map spells use TalentSystem.ExecuteNpcMapSpell.
// Mage lords age when they cast and die at age 100.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace AshAndEmber
{
    public static class ColourLordRegistry
    {
        private static readonly HashSet<string> _mageIds  = new HashSet<string>();
        private static readonly HashSet<string> _ashenIds  = new HashSet<string>();
        // Subset of spell talents each mage lord knows (1-2 random)
        private static readonly Dictionary<string, List<int>> _lordTalents
            = new Dictionary<string, List<int>>();
        private static bool _seeded = false;
        private static readonly Random _rng = new Random();
        private const float TargetFraction = 0.20f;
        private const float LowerBound     = 0.10f;
        private const float UpperBound     = 0.30f;
        // Map-cast cooldown per lord: heroId → days remaining
        private static readonly Dictionary<string, int> _campaignCooldowns
            = new Dictionary<string, int>();

        private static readonly TalentId[] SpellTalents =
        {
            TalentId.Subjugate, TalentId.Rejuvenate, TalentId.PlantGrowth,
            TalentId.BreakWills, TalentId.Inspire, TalentId.Plague,
            TalentId.Clairvoyance, TalentId.Curse,
        };

        // ── Public API ────────────────────────────────────────────────────────
        public static bool IsColourLord(Hero hero) =>
            hero != null && _mageIds.Contains(hero.StringId);

        public static bool IsAshenLord(Hero hero) =>
            hero != null && _ashenIds.Contains(hero.StringId);

        public static void SetMage(Hero hero, bool value)
        {
            if (hero == null) return;
            if (value)
            {
                _mageIds.Add(hero.StringId);
                if (!_lordTalents.ContainsKey(hero.StringId))
                    AssignRandomTalents(hero.StringId);
            }
            else
            {
                _mageIds.Remove(hero.StringId);
                _lordTalents.Remove(hero.StringId);
            }
        }

        public static void SetAshen(Hero hero, bool value)
        {
            if (hero == null) return;
            if (value)
            {
                _ashenIds.Add(hero.StringId);
                MageKnowledge.ApplyAshenAppearance(hero);
                // Ashen lords are kicked from their kingdom
                try
                {
                    if (hero.Clan?.Kingdom != null)
                        TaleWorlds.CampaignSystem.Actions.ChangeKingdomAction.ApplyByLeaveKingdom(hero.Clan, false);
                }
                catch { }
                // Cold fire hungers for harm — ensure Blight lords have offensive workings
                if (!_lordTalents.TryGetValue(hero.StringId, out var current))
                {
                    current = new List<int>();
                    _lordTalents[hero.StringId] = current;
                }
                foreach (TalentId t in new[] { TalentId.Curse, TalentId.BreakWills, TalentId.Plague })
                {
                    if (!current.Contains((int)t))
                        current.Add((int)t);
                }
            }
            else
            {
                _ashenIds.Remove(hero.StringId);
            }
        }

        public static bool HasTalent(Hero hero, TalentId id) =>
            hero != null &&
            _lordTalents.TryGetValue(hero.StringId, out var list) &&
            list.Contains((int)id);

        public static void ResetForNewGame()
        {
            _seeded = false;
            _mageIds.Clear();
            _ashenIds.Clear();
            _lordTalents.Clear();
            _campaignCooldowns.Clear();
        }

        // ── Seeding ───────────────────────────────────────────────────────────
        public static void SeedInitialLords()
        {
            if (_seeded) return;
            _seeded = true;
            try
            {
                var lords = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h != Hero.MainHero && h.IsAlive)
                    .ToList();
                int target = Math.Max(1, (int)(lords.Count * TargetFraction));
                Shuffle(lords);
                for (int i = 0; i < Math.Min(target, lords.Count); i++)
                {
                    _mageIds.Add(lords[i].StringId);
                    AssignRandomTalents(lords[i].StringId);
                }
                // seeding is silent — no announcement
            }
            catch { }
        }

        private static void AssignRandomTalents(string heroId)
        {
            var pool = SpellTalents.ToList();
            Shuffle(pool);
            int count = 1 + _rng.Next(2); // 1 or 2 talents
            var assigned = pool.Take(count).Select(t => (int)t).ToList();

            // 20% chance of DevourLife
            if (_rng.Next(100) < 20) assigned.Add((int)TalentId.DevourLife);

            _lordTalents[heroId] = assigned;
        }

        // ── Population regulator ──────────────────────────────────────────────
        public static void CheckPopulationBounds()
        {
            try
            {
                var allLords = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h != Hero.MainHero && h.IsAlive)
                    .ToList();
                if (allLords.Count == 0) return;
                int mageLords = allLords.Count(h => _mageIds.Contains(h.StringId));
                float pct = mageLords / (float)allLords.Count;

                if (pct < LowerBound)
                {
                    int needed = (int)(allLords.Count * TargetFraction) - mageLords;
                    var candidates = allLords.Where(h => !_mageIds.Contains(h.StringId)).ToList();
                    Shuffle(candidates);
                    int added = 0;
                    for (int i = 0; i < needed && i < candidates.Count; i++)
                    {
                        _mageIds.Add(candidates[i].StringId);
                        AssignRandomTalents(candidates[i].StringId);
                        added++;
                    }
                    // population adjustment is silent
                }
                else if (pct > UpperBound)
                {
                    int excess = mageLords - (int)(allLords.Count * TargetFraction);
                    var mages = allLords.Where(h => _mageIds.Contains(h.StringId)).ToList();
                    Shuffle(mages);
                    for (int i = 0; i < excess && i < mages.Count; i++)
                    {
                        _mageIds.Remove(mages[i].StringId);
                        _lordTalents.Remove(mages[i].StringId);
                    }
                }
            }
            catch { }
        }

        // Kill all mage lords aged 100+ (called from weekly tick)
        public static void CheckAgeLimit()
        {
            try
            {
                var toKill = Hero.AllAliveHeroes
                    .Where(h => h != Hero.MainHero && h.IsAlive && _mageIds.Contains(h.StringId)
                             && !_ashenIds.Contains(h.StringId)
                             && h.Age >= 100f)
                    .ToList();
                foreach (Hero h in toKill)
                {
                    try
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"{h.Name} — a century spent. The current takes what remains.",
                            new Color(0.5f, 0.3f, 0.7f)));
                        KillCharacterAction.ApplyByOldAge(h, true);
                    }
                    catch { }
                }
            }
            catch { }
        }

        // ── Campaign map casting ───────────────────────────────────────────────
        public static void DailyMapCast()
        {
            try
            {
                foreach (string id in _mageIds.ToList())
                {
                    Hero hero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == id);
                    if (hero == null || !hero.IsAlive || hero.IsPrisoner) continue;

                    if (_campaignCooldowns.TryGetValue(id, out int cd) && cd > 0)
                    { _campaignCooldowns[id] = cd - 1; continue; }

                    bool isBlight = IsAshenLord(hero);

                    // Blight lords cast hungrily — cold fire demands expression and costs them nothing
                    // Normal lords slow down as age accumulates
                    int castChance = isBlight ? 35
                                   : hero.Age < 50f ? 8
                                   : hero.Age < 70f ? 4
                                   : 2;
                    if (_rng.Next(100) >= castChance) continue;

                    if (!_lordTalents.TryGetValue(id, out var talents) || talents.Count == 0) continue;
                    // Pick a spell talent (not passive)
                    var spellTalents = talents.Where(t => t >= 1 && t <= 8).ToList();
                    if (spellTalents.Count == 0) continue;

                    TalentId chosen = (TalentId)spellTalents[_rng.Next(spellTalents.Count)];
                    try
                    {
                        TalentSystem.ExecuteNpcMapSpell(hero, chosen);
                        // Blight lords recover quickly; normal lords need several days
                        _campaignCooldowns[id] = isBlight ? 1 + _rng.Next(3) : 5 + _rng.Next(5);
                        if (hero.Clan != null) hero.Clan.Renown += 3f;
                    }
                    catch { }
                }
            }
            catch { }
        }

        // ── Death ─────────────────────────────────────────────────────────────
        public static void OnLordDied(Hero hero)
        {
            if (hero == null) return;
            _mageIds.Remove(hero.StringId);
            _ashenIds.Remove(hero.StringId);
            _lordTalents.Remove(hero.StringId);
            _campaignCooldowns.Remove(hero.StringId);
        }

        // ── Legacy no-op stubs (called by old code paths that are being phased out) ─
        public static void FlushAnnouncements() { }
        public static void FlushDeferredPrismInquiry() { }
        public static void FlushDeferredKills() { }
        public static void CheckRespawnTimers() { }

        // ── Save / Load ───────────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            var ids      = _mageIds.ToList();
            var ashenIds = _ashenIds.ToList();
            bool seeded  = _seeded;
            var cdKeys = _campaignCooldowns.Keys.ToList();
            var cdVals = _campaignCooldowns.Values.ToList();

            // Flatten lord talents: parallel lists of heroId, count, flat ints
            var talentIds   = _lordTalents.Keys.ToList();
            var talentCnts  = _lordTalents.Values.Select(v => v.Count).ToList();
            var talentFlat  = _lordTalents.Values.SelectMany(v => v).ToList();

            store.SyncData("LDM_MageIds",     ref ids);
            store.SyncData("LDM_AshenIds",    ref ashenIds);
            store.SyncData("LDM_MageSeeded",  ref seeded);
            store.SyncData("LDM_CdKeys",      ref cdKeys);
            store.SyncData("LDM_CdVals",      ref cdVals);
            store.SyncData("LDM_TalentIds",   ref talentIds);
            store.SyncData("LDM_TalentCnts",  ref talentCnts);
            store.SyncData("LDM_TalentFlat",  ref talentFlat);

            _mageIds.Clear();
            if (ids != null) foreach (var id in ids) _mageIds.Add(id);
            _ashenIds.Clear();
            if (ashenIds != null) foreach (var id in ashenIds) _ashenIds.Add(id);
            _seeded = seeded;

            _campaignCooldowns.Clear();
            if (cdKeys != null && cdVals != null)
                for (int i = 0; i < Math.Min(cdKeys.Count, cdVals.Count); i++)
                    _campaignCooldowns[cdKeys[i]] = cdVals[i];

            _lordTalents.Clear();
            if (talentIds != null && talentCnts != null && talentFlat != null)
            {
                int si = 0;
                for (int i = 0; i < talentIds.Count; i++)
                {
                    int cnt = i < talentCnts.Count ? talentCnts[i] : 0;
                    var list = new List<int>();
                    for (int j = 0; j < cnt && si < talentFlat.Count; j++, si++)
                        list.Add(talentFlat[si]);
                    if (list.Count > 0)
                        _lordTalents[talentIds[i]] = list;
                }
            }
        }

        private static void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                var tmp = list[i]; list[i] = list[j]; list[j] = tmp;
            }
        }
    }
}
