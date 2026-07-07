// =============================================================================
// ASH AND EMBER — Magic/MageElementKnowledge.cs
//
// Runtime state for the unified elemental magic: which elements and disciplines
// the player has LEARNED, and which element is currently LOADED while focusing.
//
// Fire is innate to every mage (the physical-and-spiritual root); Wind, Earth,
// Water and Spirit are each learned, as are the three disciplines:
//   • Steel  — cast with a weapon in hand, and double the weight you can bear.
//   • Blood  — executing a lord gives back some of the years the fire has burned.
//   • Nature — drawing slowly and in tune with the land (the full ~5s) makes a
//              working cost far less, as the old seers' attunement once did.
//
// These are learned through the talent system (focus points) or from a teacher,
// exactly as before; this class is the authoritative record of what was learned
// and is persisted in the save. The loaded element is session-only.
// =============================================================================

using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace AshAndEmber
{
    public static class MageElementKnowledge
    {
        // Learned elements (Fire is always known and never stored here).
        private static readonly HashSet<MagicElement> _learned = new HashSet<MagicElement>();
        private static bool _steel, _blood, _nature;

        // Session-only: the element loaded while focusing (defaults to Fire each focus).
        private static MagicElement _loaded = MagicElement.Fire;

        // ── Elements ────────────────────────────────────────────────────────────
        public static bool HasElement(MagicElement e) => e == MagicElement.Fire || _learned.Contains(e);

        public static void LearnElement(MagicElement e)
        {
            if (e != MagicElement.Fire) _learned.Add(e);
        }

        // Every element the mage may wield, Fire first.
        public static IEnumerable<MagicElement> KnownElements()
        {
            yield return MagicElement.Fire;
            foreach (MagicElement e in new[] { MagicElement.Wind, MagicElement.Earth, MagicElement.Water, MagicElement.Spirit })
                if (_learned.Contains(e)) yield return e;
        }

        public static int LearnedElementCount => _learned.Count; // excludes innate Fire

        // ── Disciplines ─────────────────────────────────────────────────────────
        public static bool HasSteel  => _steel;
        public static bool HasBlood  => _blood;
        public static bool HasNature => _nature;
        public static void LearnSteel()  => _steel  = true;
        public static void LearnBlood()  => _blood  = true;
        public static void LearnNature() => _nature = true;

        // ── Loaded element (while focusing) ─────────────────────────────────────
        public static MagicElement Loaded => _loaded;

        // Reset to the innate Fire — called when focus begins.
        public static void ResetLoaded() => _loaded = MagicElement.Fire;

        // Display name of the loaded element — the cold Ashen mask if the hero is Ashen.
        public static string LoadedName()
        {
            bool ashen = false;
            try { ashen = MageKnowledge.IsAshen; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            bool combo = ElementComboMath.IsFusion(_loaded) || ElementComboMath.IsCommand(_loaded);
            if (combo) return ashen ? ElementComboMath.AshenElementName(_loaded) : ElementComboMath.ElementName(_loaded);
            return ashen ? ElementMagicMath.AshenElementName(_loaded) : ElementMagicMath.ElementName(_loaded);
        }

        // Load an element if it is known; ignored otherwise (so the input handler can
        // blindly forward a W/S/A/D press without first checking).
        public static bool TryLoad(MagicElement e)
        {
            if (!HasElement(e)) return false;
            _loaded = e;
            return true;
        }

        // Load a FUSION or SUMMON directly. These are never individually "learned"
        // (_learned only ever holds base elements) — a fusion is available the
        // instant both its halves are known, so the input layer checks HasElement
        // on the two BASE parents itself before calling this. No further gate here.
        public static void LoadDirect(MagicElement e) => _loaded = e;

        // ── Lifecycle ───────────────────────────────────────────────────────────
        public static void ResetForNewGame()
        {
            _learned.Clear();
            _steel = _blood = _nature = false;
            _loaded = MagicElement.Fire;
        }

        public static void Save(IDataStore store)
        {
            try
            {
                var learned = _learned.Select(e => (int)e).ToList();
                store.SyncData("MEK_LearnedElements", ref learned);
                store.SyncData("MEK_Steel",  ref _steel);
                store.SyncData("MEK_Blood",  ref _blood);
                store.SyncData("MEK_Nature", ref _nature);
                if (learned != null)
                {
                    _learned.Clear();
                    foreach (int i in learned) _learned.Add((MagicElement)i);
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
