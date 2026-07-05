// =============================================================================
// ASH AND EMBER — Elementals/ElementalDialogue.cs
//
// The Kindled do not parley. A band of elemental beings is not a warband of men
// with a captain to bargain with — it is walking fire and stone. So, exactly as
// the Ashen were silenced (AshenDialogue), every conversation state a wild
// elemental band could open is answered with "..." and closed: no barter, no
// tribute, no surrender, no talk. You meet them with magic or you leave.
//
// Ashen lords are silenced by their HERO; a wild band has no hero leader, so the
// gate reads the conversation PARTY instead (ElementalWildsBehavior.IsWildBand).
// =============================================================================

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace AshAndEmber
{
    internal static class ElementalDialogue
    {
        internal static void Register(CampaignGameStarter starter)
        {
            const int P = 200; // higher than vanilla (100) so our lines fire first

            try { starter.AddDialogLine("elem_start",    "start",                  "elem_done", "...", IsElementalBand, null, P); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { starter.AddDialogLine("elem_pretalk",  "lord_pretalk",           "elem_done", "...", IsElementalBand, null, P); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { starter.AddPlayerLine("elem_close",    "elem_done",              "close_window", "...", null, null, P); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Barter / tribute / surrender / prisoner states — all closed silently.
            try { starter.AddDialogLine("elem_barter",   "lord_barter_question",   "close_window", "...", IsElementalBand, null, P); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { starter.AddDialogLine("elem_defeat_1", "defeated_lord_start_1",  "close_window", "...", IsElementalBand, null, P); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { starter.AddDialogLine("elem_defeat_2", "defeated_lord_start_2",  "close_window", "...", IsElementalBand, null, P); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { starter.AddDialogLine("elem_special",  "lord_special_request",   "close_window", "...", IsElementalBand, null, P); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { starter.AddDialogLine("elem_prisoner", "prisoner_chat",          "close_window", "...", IsElementalBand, null, P); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // True while the player is in conversation with a wild elemental band —
        // whether the game handed us the band's party directly or a troop from it.
        private static bool IsElementalBand()
        {
            try
            {
                MobileParty cp = MobileParty.ConversationParty;
                if (cp != null && ElementalWildsBehavior.IsWildBand(cp)) return true;

                var h = Hero.OneToOneConversationHero;
                if (h?.PartyBelongedTo != null && ElementalWildsBehavior.IsWildBand(h.PartyBelongedTo)) return true;
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            return false;
        }
    }
}
