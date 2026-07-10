// =============================================================================
// ASH AND EMBER — NorthmenStonesCampaignBehavior.Dialogue.cs
//
// The quest cannot move past "Offered" until the player asks the Northmen
// leader about it in person — a one-time line off "hero_main_options", the
// same hub every other kingdom-leader quest reveal in this mod uses.
// =============================================================================

using TaleWorlds.CampaignSystem;

namespace AshAndEmber
{
    public partial class NorthmenStonesCampaignBehavior
    {
        private const int DialoguePriority = 150;

        private static void RegisterDialogue(CampaignGameStarter starter)
        {
            try
            {
                starter.AddPlayerLine(
                    "nstones_ask_open", "hero_main_options", "nstones_reveal",
                    "I hear the seers have found a way to shut the Ashen out for good. Tell me.",
                    CondCanAsk, null, DialoguePriority);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try
            {
                starter.AddDialogLine(
                    "nstones_reveal_npc", "nstones_reveal", "nstones_reveal_player",
                    "You have heard true, then. The seers say the standing stones can be raised at Varcheg " +
                    "and bound with Fire — any Ashen thing that tries to cross there will burn before it " +
                    "sets foot on living ground. To raise them we need iron, hardwood, tools, silver, coin " +
                    "enough to keep the masons fed for years, and — this is the seers' price, not mine — " +
                    "Kindled bound and given up, one of every kind the Forest Clans' sacred sites can wake. " +
                    "We will need the Forest Clans standing with us in this, truly with us, not merely at " +
                    "peace. And Varcheg must be ours when the working closes, or it closes on nothing.",
                    CondCanAsk, null, DialoguePriority);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try
            {
                starter.AddPlayerLine(
                    "nstones_reveal_accept", "nstones_reveal_player", "close_window",
                    "Then let's begin.",
                    null, OnAskConsequence, DialoguePriority);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static bool CondCanAsk()
        {
            try
            {
                if (_phase != PhaseOffered) return false;
                Hero h = Hero.OneToOneConversationHero;
                if (h == null) return false;
                return h == NorthmenLeader();
            }
            catch { return false; }
        }

        private static void OnAskConsequence()
        {
            try
            {
                _phase = PhaseActive;
                NorthmenStonesQuestLog.Start();
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
