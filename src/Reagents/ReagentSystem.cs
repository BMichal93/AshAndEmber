// =============================================================================
// ASH AND EMBER — Reagents/ReagentSystem.cs
// Magical components acquired via sea ventures and ruin exploration.
// Used to reduce Sanctuary / Altar cooldowns and unlock ritual variants.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace AshAndEmber
{
    public static class ReagentSystem
    {
        // ── Reagent type constants ────────────────────────────────────────────
        public const string BrimstoneAsh    = "BrimstoneAsh";
        public const string FrozenAmber     = "FrozenAmber";
        public const string SeaSerpentScale = "SeaSerpentScale";
        public const string VoidResin       = "VoidResin";

        public static readonly string[] AllTypes =
            { BrimstoneAsh, FrozenAmber, SeaSerpentScale, VoidResin };

        private static readonly Dictionary<string, int> _stash = new Dictionary<string, int>();
        private static readonly Random _rng = new Random();

        // ── Public stash API ──────────────────────────────────────────────────
        public static int Count(string type)
        {
            _stash.TryGetValue(type, out int n);
            return n;
        }

        public static bool HasAny(string type) => Count(type) > 0;

        public static void Add(string type, int qty)
        {
            if (qty <= 0) return;
            if (!_stash.ContainsKey(type)) _stash[type] = 0;
            _stash[type] += qty;
            InformationManager.DisplayMessage(new InformationMessage(
                $"Reagent acquired: {FriendlyName(type)} ×{qty}. You carry {_stash[type]}.",
                new Color(0.70f, 0.55f, 0.85f)));
        }

        public static bool Consume(string type, int qty = 1)
        {
            if (Count(type) < qty) return false;
            _stash[type] -= qty;
            return true;
        }

        // ── Display helpers ───────────────────────────────────────────────────
        public static string FriendlyName(string type) => type switch
        {
            BrimstoneAsh    => "Brimstone Ash",
            FrozenAmber     => "Frozen Amber",
            SeaSerpentScale => "Sea Serpent Scale",
            VoidResin       => "Void Resin",
            _               => type,
        };

        public static string StashSummary()
        {
            var parts = AllTypes.Where(t => Count(t) > 0)
                                .Select(t => $"{FriendlyName(t)}: {Count(t)}");
            return parts.Any() ? string.Join(", ", parts) : "none";
        }

        // ── Cooldown reduction by context ─────────────────────────────────────
        // Days shaved off Sanctuary cooldowns when the best available reagent is offered.
        public static int SanctuaryCooldownReduction() =>
            BestForContext(isSanctuary: true) is string t ? ContextReduction(t, isSanctuary: true) : 0;

        // Days shaved off Altar cooldowns.
        public static int AltarCooldownReduction() =>
            BestForContext(isSanctuary: false) is string t ? ContextReduction(t, isSanctuary: false) : 0;

        // Consumes the best reagent for the given context. Returns true if one was used.
        public static bool ConsumeForSanctuary()
        {
            var t = BestForContext(isSanctuary: true);
            return t != null && Consume(t);
        }

        public static bool ConsumeForAltar()
        {
            var t = BestForContext(isSanctuary: false);
            return t != null && Consume(t);
        }

        public static string BestForContext(bool isSanctuary)
            => AllTypes
                .Where(t => Count(t) > 0 && ContextReduction(t, isSanctuary) > 0)
                .OrderByDescending(t => ContextReduction(t, isSanctuary))
                .FirstOrDefault();

        private static int ContextReduction(string type, bool isSanctuary) => isSanctuary
            ? type switch
            {
                FrozenAmber  => 3,
                VoidResin    => 2,
                _            => 0,
            }
            : type switch
            {
                BrimstoneAsh => 3,
                VoidResin    => 3,
                _            => 0,
            };

        // ── Port-flavoured random selection ───────────────────────────────────
        public static string RandomReagentForPort(Settlement port)
        {
            string culture = null;
            try { culture = port?.Culture?.StringId; } catch { }
            return culture switch
            {
                "aserai"   => _rng.Next(2) == 0 ? BrimstoneAsh : VoidResin,
                "khuzait"  => _rng.Next(2) == 0 ? BrimstoneAsh : VoidResin,
                "sturgia"  => _rng.Next(2) == 0 ? FrozenAmber  : SeaSerpentScale,
                "battania" => _rng.Next(2) == 0 ? FrozenAmber  : SeaSerpentScale,
                _          => AllTypes[_rng.Next(AllTypes.Length)],
            };
        }

        // ── Persistence ───────────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            try
            {
                var keys = _stash.Keys.ToList();
                var vals = keys.Select(k => _stash[k]).ToList();
                store.SyncData("REA_Keys", ref keys);
                store.SyncData("REA_Vals", ref vals);
                if (keys != null && vals != null && keys.Count == vals.Count)
                {
                    _stash.Clear();
                    for (int i = 0; i < keys.Count; i++)
                        _stash[keys[i]] = vals[i];
                }
            }
            catch { }
        }

        public static void ResetForNewGame() => _stash.Clear();
    }
}
