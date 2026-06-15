// =============================================================================
// ASH AND EMBER — SanctuaryCampaignBehavior.Npc.cs
// Daily-tick player-side maintenance for sanctuary state.
//
// NPC miracle use is NOT handled here. NPCs act solely through the new Miracle
// system — MiracleBattleAI (battle) and MiracleCampaignBehavior (campaign map).
// The sanctuary itself is only the player's Grace charging station; this file
// handles the deferred establishment toast and the slow virtue drift that
// repeated devotion brings.
//
// Partial of SanctuaryCampaignBehavior (shared static state lives in SanctuaryCampaignBehavior.cs).
// =============================================================================

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class SanctuaryCampaignBehavior
    {
        private static void OnDailyTick()
        {
            // Loaded saves that predate the sanctuary system (no announce flag yet)
            // get the establishment toast on their first tick once IDs are settled.
            if (_needsAnnouncementAfterSync)
                AnnounceSanctuaries();

            // Trait drift: every 10 sanctuary uses, nudge the player's weakest virtue up.
            if (_sanctuaryUseCount > 0 && _sanctuaryUseCount % TraitDriftThreshold == 0)
            {
                try
                {
                    var h = Hero.MainHero;
                    if (h != null)
                    {
                        int mercy = h.GetTraitLevel(DefaultTraits.Mercy);
                        int honor = h.GetTraitLevel(DefaultTraits.Honor);
                        int gen   = h.GetTraitLevel(DefaultTraits.Generosity);
                        if (mercy <= honor && mercy <= gen && mercy < 2)       h.SetTraitLevel(DefaultTraits.Mercy, mercy + 1);
                        else if (honor <= mercy && honor <= gen && honor < 2)  h.SetTraitLevel(DefaultTraits.Honor, honor + 1);
                        else if (gen < 2)                                       h.SetTraitLevel(DefaultTraits.Generosity, gen + 1);
                        MBInformationManager.AddQuickInformation(new TextObject(
                            "The flame has changed you. A virtue has deepened in you without your noticing."));
                    }
                }
                catch { }
                _sanctuaryUseCount = 0;
            }
        }
    }
}
