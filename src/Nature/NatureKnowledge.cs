// =============================================================================
// ASH AND EMBER — Nature/NatureKnowledge.cs
// Tracks player attunement to The Living Ember and hermit discovery flags.
// Exclusive with Miracles (Grace/Cold) — both cannot be active at once.
// =============================================================================

using TaleWorlds.CampaignSystem;

namespace AshAndEmber
{
    public static class NatureKnowledge
    {
        private static bool _isAttuned       = false;
        private static bool _hermitBattania  = false;   // Living Root talent unlocked
        private static bool _hermitStrugia   = false;   // Still Draw talent unlocked
        private static bool _hermitKhuzait   = false;   // Open Grip talent unlocked

        public static bool IsAttuned        => _isAttuned;
        public static bool FoundBattaniaHermit => _hermitBattania;
        public static bool FoundStrugiaHermit  => _hermitStrugia;
        public static bool FoundKhuzaitHermit  => _hermitKhuzait;

        public static void SetAttuned(bool value) { _isAttuned = value; }

        // Called when a hermit teaches the player a nature talent.
        public static void RecordHermitFound(NatureHermitId id)
        {
            switch (id)
            {
                case NatureHermitId.Battania: _hermitBattania = true; break;
                case NatureHermitId.Strugia:  _hermitStrugia  = true; break;
                case NatureHermitId.Khuzait:  _hermitKhuzait  = true; break;
            }
        }

        public static bool HermitFound(NatureHermitId id)
        {
            switch (id)
            {
                case NatureHermitId.Battania: return _hermitBattania;
                case NatureHermitId.Strugia:  return _hermitStrugia;
                case NatureHermitId.Khuzait:  return _hermitKhuzait;
                default:                      return false;
            }
        }

        public static void ResetForNewGame()
        {
            _isAttuned      = false;
            _hermitBattania = false;
            _hermitStrugia  = false;
            _hermitKhuzait  = false;
        }

        // ── Save / Load ────────────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            bool attuned = _isAttuned;
            bool hb      = _hermitBattania;
            bool hs      = _hermitStrugia;
            bool hk      = _hermitKhuzait;
            store.SyncData("NATURE_Attuned",        ref attuned);
            store.SyncData("NATURE_HermitBattania", ref hb);
            store.SyncData("NATURE_HermitStrugia",  ref hs);
            store.SyncData("NATURE_HermitKhuzait",  ref hk);
            _isAttuned      = attuned;
            _hermitBattania = hb;
            _hermitStrugia  = hs;
            _hermitKhuzait  = hk;
        }
    }

    public enum NatureHermitId { Battania, Strugia, Khuzait }
}
