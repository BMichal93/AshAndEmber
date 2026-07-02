// =============================================================================
// ASH AND EMBER — AshenAltarsCampaignBehavior.Npc.cs
// Daily maintenance for Dark Gift state.
// Partial of AshenAltarsCampaignBehavior.
// =============================================================================

using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class AshenAltarsCampaignBehavior
    {
        private static bool _giftDisabledNotified = false;

        private static void OnDailyTick()
        {
            // The dynamic (Ashen-city) altars cannot be rolled at session launch on a
            // NEW campaign — the Ashen kingdom receives its cities only after character
            // creation, and OnNewGameCreated resets this behaviour besides. Retry here
            // until the world has Ashen towns to pick, then announce the full set once.
            try { EnsureDynamicAltars(); AnnounceAltars(); } catch { }

            if (!DarkGiftSystem.HasAnyGift) { _giftDisabledNotified = false; return; }

            bool active = DarkGiftSystem.GiftsActive;
            if (!active && !_giftDisabledNotified)
            {
                _giftDisabledNotified = true;
                MBInformationManager.AddQuickInformation(new TextObject(
                    "Your dark gifts lie dormant. The stone sees something bright in you. Return to darkness to reawaken them."));
            }
            else if (active && _giftDisabledNotified)
            {
                _giftDisabledNotified = false;
                MBInformationManager.AddQuickInformation(new TextObject(
                    "The cold stirs again. Your dark gifts wake."));
            }
        }
    }
}
