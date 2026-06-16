// =============================================================================
// ASH AND EMBER — CampaignMapEvents.Portents.cs
// Atmospheric portent messages that fire once as day-thresholds approach for
// once-per-campaign events. They telegraph dread before the hammer falls —
// giving the player a 7–14 day window of unease before the big event fires.
// Partial of CampaignMapEvents (shared state lives in CampaignMapEvents.cs).
// =============================================================================

using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace AshAndEmber
{
    public static partial class CampaignMapEvents
    {
        // ── Portent flags ─────────────────────────────────────────────────────
        // Each fires exactly once per campaign. Persisted so reloading doesn't
        // re-show a portent the player already received.
        private static bool _ashenGambitPortentShown = false;
        private static bool _undyingHostPortentShown = false;
        private static bool _brokenWillPortentShown  = false;

        // ── Portent logic ─────────────────────────────────────────────────────
        // Called from WeeklyTick() before the main event slot is consumed.
        // Portents are ambient message-log lines (not popups) and do not claim
        // the weekly event slot.
        internal static void TryFirePortents()
        {
            int day = (int)ElapsedCampaignDays();

            // Ashen Gambit: fires 14 days before the earliest possible trigger
            if (!_ashenGambitFired && !_ashenGambitPortentShown
                && day >= AshenGambitEarliestDay - 14)
            {
                _ashenGambitPortentShown = true;
                InformationManager.DisplayMessage(new InformationMessage(
                    "Travelers from the east bring dark news — cities found silent, not sieged. " +
                    "Fires burning inside walls with no smoke outside. The roads are very quiet.",
                    new Color(0.38f, 0.50f, 0.75f)));
            }

            // Undying Host: fires once the day threshold is first crossed
            if (!_undyingHostFired && !_undyingHostPortentShown
                && day >= UndyingHostEarliestDay)
            {
                _undyingHostPortentShown = true;
                InformationManager.DisplayMessage(new InformationMessage(
                    "A herald found cold on the northern road — no wounds, no horse, no name. " +
                    "The Ashen have stopped raiding the villages. " +
                    "When your enemies go quiet, they are not retreating.",
                    new Color(0.38f, 0.50f, 0.75f)));
            }

            // Broken Will: fires once the gating day is reached (kingdom goes mad)
            if (_brokenWillFired < BrokenWillMaxFires && !_brokenWillPortentShown
                && day >= BrokenWillEarliestDay)
            {
                _brokenWillPortentShown = true;
                InformationManager.DisplayMessage(new InformationMessage(
                    "Court rumours of a kingdom losing its compass — lords who looked into the grey too long. " +
                    "Kingdoms have broken faith with themselves before. The pattern has a name now.",
                    new Color(0.38f, 0.50f, 0.75f)));
            }
        }

        // ── Persistence helpers (called from CampaignMapEvents.Persistence.cs) ─
        internal static void SavePortents(IDataStore store)
        {
            int gPortent = _ashenGambitPortentShown ? 1 : 0;
            int uPortent = _undyingHostPortentShown ? 1 : 0;
            int bPortent = _brokenWillPortentShown  ? 1 : 0;
            store.SyncData("LDM_PortentGambit",  ref gPortent);
            store.SyncData("LDM_PortentUndying", ref uPortent);
            store.SyncData("LDM_PortentBroken",  ref bPortent);
            _ashenGambitPortentShown = gPortent != 0;
            _undyingHostPortentShown = uPortent != 0;
            _brokenWillPortentShown  = bPortent != 0;
        }

        internal static void ResetPortents()
        {
            _ashenGambitPortentShown = false;
            _undyingHostPortentShown = false;
            _brokenWillPortentShown  = false;
        }
    }
}
