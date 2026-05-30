// =============================================================================
// LIFE & DEATH MAGIC — AI/AshenDialogue.cs
// Replaces all dialogue for Ashen lords and Ashen Spawn with "..." and
// blocks any attempt to bargain, negotiate, or surrender.
// =============================================================================

using TaleWorlds.CampaignSystem;

namespace AshAndEmber
{
    internal static class AshenDialogue
    {
        internal static void Register(CampaignGameStarter starter)
        {
            const int P = 200; // higher than vanilla (100) so our lines fire first

            // ── Opening and any standard lord sub-state ─────────────────────────
            try { starter.AddDialogLine("ashen_start",    "start",        "ashen_done", "...", IsAshenInterlocutor, null, P); } catch { }
            try { starter.AddDialogLine("ashen_pretalk",  "lord_pretalk", "ashen_done", "...", IsAshenInterlocutor, null, P); } catch { }

            // Player's only available response — also "..."
            try { starter.AddPlayerLine("ashen_close",    "ashen_done", "close_window", "...", null, null, P); } catch { }

            // ── Barter / tribute / negotiation ─────────────────────────────────
            try { starter.AddDialogLine("ashen_barter",   "lord_barter_question", "close_window", "...", IsAshenInterlocutor, null, P); } catch { }

            // ── Defeat / surrender offers ───────────────────────────────────────
            try { starter.AddDialogLine("ashen_defeat_1", "defeated_lord_start_1", "close_window", "...", IsAshenInterlocutor, null, P); } catch { }
            try { starter.AddDialogLine("ashen_defeat_2", "defeated_lord_start_2", "close_window", "...", IsAshenInterlocutor, null, P); } catch { }
            try { starter.AddDialogLine("ashen_special",  "lord_special_request",  "close_window", "...", IsAshenInterlocutor, null, P); } catch { }

            // ── Prisoner conversation ───────────────────────────────────────────
            try { starter.AddDialogLine("ashen_prisoner", "prisoner_chat",         "close_window", "...", IsAshenInterlocutor, null, P); } catch { }
        }

        private static bool IsAshenInterlocutor()
        {
            try
            {
                var h = Hero.OneToOneConversationHero;
                if (h == null) return false;
                if (ColourLordRegistry.IsAshenLord(h)) return true;
                if (h.PartyBelongedTo != null && FireWorshippersSystem.IsAshenSpawn(h.PartyBelongedTo))
                    return true;
                return false;
            }
            catch { return false; }
        }
    }
}
