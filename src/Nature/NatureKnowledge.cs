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
        // Day-count (CampaignTime.ToDays) until which the rare-weed communion holds.
        private static double _weedBlessUntilDays = -1.0;
        private static bool _hermitBattania  = false;   // Living Root talent unlocked
        private static bool _hermitStrugia   = false;   // Still Draw talent unlocked
        private static bool _hermitKhuzait   = false;   // Open Grip talent unlocked
        private static bool _hermitRetreat   = false;   // Deep Earth talent unlocked
        private static bool _hermitFringe    = false;   // Dawn Call talent unlocked

        public static bool IsAttuned           => _isAttuned;
        public static bool FoundBattaniaHermit => _hermitBattania;
        public static bool FoundStrugiaHermit  => _hermitStrugia;
        public static bool FoundKhuzaitHermit  => _hermitKhuzait;
        public static bool FoundRetreatHermit  => _hermitRetreat;
        public static bool FoundFringeHermit   => _hermitFringe;

        public static void SetAttuned(bool value) { _isAttuned = value; }

        // ── Rare-weed communion (the Green Draught) ───────────────────────────
        // While active, the player's nature draws have a chance to cost the land
        // nothing (LivingEnergyMath.WeedFreeDrawChance). Bought at a tavern.
        public static bool WeedBlessingActive
        {
            get { try { return CampaignTime.Now.ToDays < _weedBlessUntilDays; } catch { return false; } }
        }

        public static void GrantWeedBlessing(double hours)
        {
            try { _weedBlessUntilDays = CampaignTime.Now.ToDays + hours / 24.0; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Called when a hermit teaches the player a nature talent.
        public static void RecordHermitFound(NatureHermitId id)
        {
            switch (id)
            {
                case NatureHermitId.Battania: _hermitBattania = true; break;
                case NatureHermitId.Strugia:  _hermitStrugia  = true; break;
                case NatureHermitId.Khuzait:  _hermitKhuzait  = true; break;
                case NatureHermitId.Retreat:  _hermitRetreat  = true; break;
                case NatureHermitId.Fringe:   _hermitFringe   = true; break;
            }
        }

        public static bool HermitFound(NatureHermitId id)
        {
            switch (id)
            {
                case NatureHermitId.Battania: return _hermitBattania;
                case NatureHermitId.Strugia:  return _hermitStrugia;
                case NatureHermitId.Khuzait:  return _hermitKhuzait;
                case NatureHermitId.Retreat:  return _hermitRetreat;
                case NatureHermitId.Fringe:   return _hermitFringe;
                default:                      return false;
            }
        }

        public static void ResetForNewGame()
        {
            _isAttuned      = false;
            _weedBlessUntilDays = -1.0;
            _hermitBattania = false;
            _hermitStrugia  = false;
            _hermitKhuzait  = false;
            _hermitRetreat  = false;
            _hermitFringe   = false;
        }

        // ── Save / Load ────────────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            bool attuned = _isAttuned;
            bool hb      = _hermitBattania;
            bool hs      = _hermitStrugia;
            bool hk      = _hermitKhuzait;
            bool hr      = _hermitRetreat;
            bool hf      = _hermitFringe;
            double weed  = _weedBlessUntilDays;
            store.SyncData("NATURE_WeedBlessUntil",  ref weed);
            _weedBlessUntilDays = weed;
            store.SyncData("NATURE_Attuned",        ref attuned);
            store.SyncData("NATURE_HermitBattania", ref hb);
            store.SyncData("NATURE_HermitStrugia",  ref hs);
            store.SyncData("NATURE_HermitKhuzait",  ref hk);
            store.SyncData("NATURE_HermitRetreat",  ref hr);
            store.SyncData("NATURE_HermitFringe",   ref hf);
            _isAttuned      = attuned;
            _hermitBattania = hb;
            _hermitStrugia  = hs;
            _hermitKhuzait  = hk;
            _hermitRetreat  = hr;
            _hermitFringe   = hf;
        }
    }

    public enum NatureHermitId { Battania, Strugia, Khuzait, Retreat, Fringe }
}
