// =============================================================================
// ASH AND EMBER — Alchemy/AlchemyInventory.cs
//
// The player's satchel. Two parallel lists (type + tainted flag) hold every
// vial carried; capacity is the carrier's Intelligence (AlchemyMath.CarryCapacity).
// State is static and serialized by AlchemyCampaignBehavior via SyncData using
// ALCH_* keys (parallel lists — the same backward-compatible pattern as the Sea
// systems). A save made before this system simply has no keys and loads empty.
// =============================================================================

using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.CampaignSystem;

namespace AshAndEmber
{
    public static class AlchemyInventory
    {
        // Parallel lists: _types[i] is an ElixirType (as int); _tainted[i] marks
        // a spoiled brew that will backfire when drunk.
        internal static readonly List<int>  _types   = new List<int>();
        internal static readonly List<bool> _tainted = new List<bool>();

        public static int Count => _types.Count;

        // Capacity from the main hero's Intelligence (min 1). Safe off-campaign.
        public static int Capacity()
        {
            int intel = 1;
            try
            {
                var h = Hero.MainHero;
                if (h != null)
                    intel = h.GetAttributeValue(DefaultCharacterAttributes.Intelligence);
            }
            catch { }
            int bonus = TalentSystem.Has(TalentId.DeeperSatchel) ? 2 : 0;
            return AlchemyMath.CarryCapacity(intel) + bonus;
        }

        public static bool HasSpace() => Count < Capacity();

        // Adds a vial. Returns false if the satchel is full.
        public static bool Add(ElixirType type, bool tainted)
        {
            if (!HasSpace()) return false;
            _types.Add((int)type);
            _tainted.Add(tainted);
            return true;
        }

        // Removes the first vial of the given type. Out-param reports whether it
        // was tainted. Returns false if none of that type are held.
        public static bool Remove(ElixirType type, out bool wasTainted)
        {
            wasTainted = false;
            for (int i = 0; i < _types.Count; i++)
            {
                if (_types[i] != (int)type) continue;
                wasTainted = _tainted[i];
                _types.RemoveAt(i);
                _tainted.RemoveAt(i);
                return true;
            }
            return false;
        }

        public static int CountOf(ElixirType type)
            => _types.Count(t => t == (int)type);

        // Distinct elixir types currently held, in catalog order.
        public static IEnumerable<ElixirType> HeldTypes()
            => AlchemyCatalog.All
                .Select(d => d.Type)
                .Where(t => CountOf(t) > 0);

        public static void ResetForNewGame()
        {
            _types.Clear();
            _tainted.Clear();
        }
    }
}
