// =============================================================================
// ASH AND EMBER — Nature/NatureSeerRegistry.cs
// Tracks which NPC lords and companions are attuned to The Living Ember.
//
// Seeding targets:
//   Battanian lords  ~20%
//   Strugian lords   ~15%
//   Khuzait lords    ~10%
//   All others       ~3%  (wandering seers, rare finds)
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace AshAndEmber
{
    public static class NatureSeerRegistry
    {
        private static readonly HashSet<string> _seerIds       = new HashSet<string>();
        private static readonly HashSet<string> _companionIds  = new HashSet<string>();
        private static bool _seeded = false;
        private static readonly Random _rng = new Random();

        private const float BattaniaFraction = 0.20f;
        private const float SturgiaFraction  = 0.15f;
        private const float KhuzaitFraction  = 0.10f;
        private const float OtherFraction    = 0.03f;

        // ── Public API ────────────────────────────────────────────────────────
        public static bool IsNatureSeer(Hero hero) =>
            hero != null && _seerIds.Contains(hero.StringId);

        public static bool IsCompanionSeer(Hero hero) =>
            hero != null && _companionIds.Contains(hero.StringId);

        public static void RegisterCompanionSeer(Hero hero)
        {
            if (hero == null) return;
            _seerIds.Add(hero.StringId);
            _companionIds.Add(hero.StringId);
        }

        public static void OnLordDied(Hero hero)
        {
            if (hero == null) return;
            _seerIds.Remove(hero.StringId);
            _companionIds.Remove(hero.StringId);
        }

        public static void ResetForNewGame()
        {
            _seerIds.Clear();
            _companionIds.Clear();
            _seeded = false;
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

                foreach (Hero h in lords)
                {
                    try
                    {
                        float fraction = GetFraction(h);
                        if ((float)_rng.NextDouble() < fraction)
                            _seerIds.Add(h.StringId);
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static float GetFraction(Hero h)
        {
            try
            {
                string culture = h.Culture?.StringId ?? "";
                if (culture == "battania") return BattaniaFraction;
                if (culture == "sturgia")  return SturgiaFraction;
                if (culture == "khuzait")  return KhuzaitFraction;
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            return OtherFraction;
        }

        // Weekly: keep Battanian/Strugian populations roughly stable.
        public static void CheckPopulationBounds()
        {
            try
            {
                var battanian = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && h != Hero.MainHero
                             && (h.Culture?.StringId ?? "") == "battania")
                    .ToList();
                int bTarget = Math.Max(1, (int)(battanian.Count * BattaniaFraction));
                int bCurrent = battanian.Count(h => _seerIds.Contains(h.StringId));
                if (bCurrent < bTarget)
                {
                    var candidates = battanian.Where(h => !_seerIds.Contains(h.StringId)).ToList();
                    Shuffle(candidates);
                    for (int i = 0; i < (bTarget - bCurrent) && i < candidates.Count; i++)
                        _seerIds.Add(candidates[i].StringId);
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Save / Load ───────────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            var ids      = _seerIds.ToList();
            var compIds  = _companionIds.ToList();
            bool seeded  = _seeded;
            store.SyncData("NATURE_SeerIds",     ref ids);
            store.SyncData("NATURE_CompIds",     ref compIds);
            store.SyncData("NATURE_SeerSeeded",  ref seeded);
            _seerIds.Clear();
            if (ids != null) foreach (string id in ids) _seerIds.Add(id);
            _companionIds.Clear();
            if (compIds != null) foreach (string id in compIds) _companionIds.Add(id);
            _seeded = seeded;
        }

        private static void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                T tmp = list[i]; list[i] = list[j]; list[j] = tmp;
            }
        }
    }
}
