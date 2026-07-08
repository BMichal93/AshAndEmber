// =============================================================================
// ASH AND EMBER — GreatAwakeningCampaignBehavior.Dialogue.cs
//
// Regardless of path — Duneborn or opposed — the quest cannot move past
// "Discovered" until the player asks the Duneborn kingdom's leader about the
// Great Awakening in person. Hangs off "hero_main_options", the same hub the
// Soldier and Clan Orders systems use.
// =============================================================================

using TaleWorlds.CampaignSystem;

namespace AshAndEmber
{
    public partial class GreatAwakeningCampaignBehavior
    {
        private const int DialoguePriority = 150;

        private static void RegisterDialogue(CampaignGameStarter starter)
        {
            try
            {
                starter.AddPlayerLine(
                    "grawk_ask_open", "hero_main_options", "grawk_reveal",
                    "I have heard whispers of... the Great Awakening. Tell me what you know.",
                    CondCanAsk, null, DialoguePriority);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try
            {
                starter.AddDialogLine(
                    "grawk_reveal_npc", "grawk_reveal", "grawk_reveal_player",
                    "You already know, then, or near enough. We have reached beyond the Sands and touched " +
                    "something that answered. It hungers, and we have promised it a sacrifice large enough to " +
                    "sate it — every prisoner our knives can spare, poured out at the Dark Altar until the " +
                    "count is paid in full. What it wants in return is simple: to stand on Calradian soil, and " +
                    "to rule it. We mean to give it that chance.",
                    CondCanAsk, null, DialoguePriority);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try
            {
                starter.AddPlayerLine(
                    "grawk_reveal_accept", "grawk_reveal_player", "close_window",
                    "Then it has already begun.",
                    null, OnAskConsequence, DialoguePriority);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static bool CondCanAsk()
        {
            try
            {
                if (_phase != PhaseDiscovered) return false;
                Hero h = Hero.OneToOneConversationHero;
                if (h == null) return false;
                return h == DunebornLeader();
            }
            catch { return false; }
        }

        private static void OnAskConsequence()
        {
            try
            {
                _phase = PhaseActive;
                GreatAwakeningQuestLog.Start();
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
