// =============================================================================
// ASH AND EMBER — AI/LordDialogueGuard.cs
// Shared guard for the faction flavour dialogues (Ashen, Temple, Tribes,
// Northmen, Duneborn). Vanilla's defeated-lord conversation (take prisoner /
// let go) and the prisoner-chat flow both begin at the "start" token; our
// flavour greetings register there at priority 190–200 and would otherwise
// outprioritize them — leaving the player with no capture option and letting
// beaten lords walk free. Any flavour line on "start" must stand down when
// the conversation belongs to a battle encounter or a captive.
// =============================================================================

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;

namespace AshAndEmber
{
    internal static class LordDialogueGuard
    {
        // True when vanilla must own the conversation: the interlocutor is a
        // prisoner, or the player encounter carries a battle (a defeated-lord
        // capture/release conversation, mid- or post-battle talk). Fail-safe:
        // on any error, yield to vanilla rather than risk blocking capture.
        internal static bool MustYieldToVanilla()
        {
            try
            {
                var h = Hero.OneToOneConversationHero;
                if (h != null && h.IsPrisoner) return true;

                if (PlayerEncounter.Current != null)
                {
                    if (PlayerEncounter.Battle != null || PlayerEncounter.EncounteredBattle != null)
                        return true;

                    var st = PlayerEncounter.Current.EncounterState;
                    if (st == PlayerEncounterState.PrepareResults ||
                        st == PlayerEncounterState.ApplyResults ||
                        st == PlayerEncounterState.PlayerVictory ||
                        st == PlayerEncounterState.PlayerTotalDefeat ||
                        st == PlayerEncounterState.CaptureHeroes ||
                        st == PlayerEncounterState.FreeHeroes ||
                        st == PlayerEncounterState.LootParty ||
                        st == PlayerEncounterState.LootInventory)
                        return true;
                }

                return false;
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); return true; }
        }
    }
}
