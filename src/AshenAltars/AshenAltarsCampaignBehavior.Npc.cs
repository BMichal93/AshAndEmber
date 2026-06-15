// =============================================================================
// ASH AND EMBER — AshenAltarsCampaignBehavior.Npc.cs
// Daily-tick player-side maintenance for altar state.
//
// NPC miracle use is NOT handled here. NPCs act solely through the new Miracle
// system — MiracleBattleAI (battle) and MiracleCampaignBehavior (campaign map).
// The altar itself is only the player's Cold charging station; this file handles
// the slow virtue corrosion that repeated rites bring.
//
// Partial of AshenAltarsCampaignBehavior (shared static state lives in AshenAltarsCampaignBehavior.cs).
// =============================================================================

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class AshenAltarsCampaignBehavior
    {
        private static void OnDailyTick()
        {
            // Trait drift: every 10 altar uses, nudge the player's strongest virtue down.
            if (_altarUseCount > 0 && _altarUseCount % TraitDriftThreshold == 0)
            {
                try
                {
                    var h = Hero.MainHero;
                    if (h != null)
                    {
                        int mercy = h.GetTraitLevel(DefaultTraits.Mercy);
                        int honor = h.GetTraitLevel(DefaultTraits.Honor);
                        int gen   = h.GetTraitLevel(DefaultTraits.Generosity);
                        if (mercy >= honor && mercy >= gen && mercy > -2)      h.SetTraitLevel(DefaultTraits.Mercy, mercy - 1);
                        else if (honor >= mercy && honor >= gen && honor > -2) h.SetTraitLevel(DefaultTraits.Honor, honor - 1);
                        else if (gen > -2)                                      h.SetTraitLevel(DefaultTraits.Generosity, gen - 1);
                        MBInformationManager.AddQuickInformation(new TextObject(
                            "The cold has changed you. Something that was soft in you has hardened."));
                    }
                }
                catch { }
                _altarUseCount = 0;
            }
        }
    }
}
