// =============================================================================
// ASH AND EMBER — QuestSystems/ArenicosDialogue.cs
// Replaces all vanilla lord dialogue for Emperor Arenicos with emperor-specific
// lines. True and false emperor flavours are handled separately so the player
// receives different dialogue without knowing which spirit they face.
// =============================================================================

using TaleWorlds.CampaignSystem;

namespace AshAndEmber
{
    internal static class ArenicosDialogue
    {
        internal static void Register(CampaignGameStarter starter)
        {
            const int P = 210; // above AshenDialogue (200) and vanilla (100)

            // ── True Emperor — opening ────────────────────────────────────────
            try { starter.AddDialogLine("ar_true_start",
                "start", "ar_reply",
                "You stand before Arencios — first emperor of a united Calradia. " +
                "Whatever age has passed between that throne and this one, I remember all of it. " +
                "Say what you have come to say.",
                IsTrueEmperor, null, P); } catch { }

            try { starter.AddDialogLine("ar_true_pretalk",
                "lord_pretalk", "ar_reply",
                "The Empire does not wait long. Neither do I. What is it?",
                IsTrueEmperor, null, P); } catch { }

            // ── False Emperor — opening ───────────────────────────────────────
            try { starter.AddDialogLine("ar_false_start",
                "start", "ar_reply",
                "You come before the Emperor. The warmth in you is noticed. " +
                "Everything warm is noticed now. Say what you came to say.",
                IsFalseEmperor, null, P); } catch { }

            try { starter.AddDialogLine("ar_false_pretalk",
                "lord_pretalk", "ar_reply",
                "Speak. Warmth that lingers too long makes me... uncomfortable.",
                IsFalseEmperor, null, P); } catch { }

            // ── Shared player responses ───────────────────────────────────────
            try { starter.AddPlayerLine("ar_reply_serve",
                "ar_reply", "close_window",
                "Long live the Emperor.",
                null, null, P); } catch { }
            try { starter.AddPlayerLine("ar_reply_seen",
                "ar_reply", "close_window",
                "I came to see for myself. I have seen.",
                null, null, P); } catch { }
            try { starter.AddPlayerLine("ar_reply_leave",
                "ar_reply", "close_window",
                "I will not keep you from your work.",
                null, null, P); } catch { }

            // ── Barter / negotiation ──────────────────────────────────────────
            try { starter.AddDialogLine("ar_true_barter",
                "lord_barter_question", "close_window",
                "Emperors do not negotiate. They set terms. " +
                "Come back when you have something worth setting terms about.",
                IsTrueEmperor, null, P); } catch { }

            try { starter.AddDialogLine("ar_false_barter",
                "lord_barter_question", "close_window",
                "Trade requires equal footing. There is no equal footing here. " +
                "There is only the Emperor, and everything else.",
                IsFalseEmperor, null, P); } catch { }

            // ── Defeat / surrender ────────────────────────────────────────────
            try { starter.AddDialogLine("ar_true_defeat_1",
                "defeated_lord_start_1", "close_window",
                "Strike me down if it pleases you. I have died before. It did not take.",
                IsTrueEmperor, null, P); } catch { }

            try { starter.AddDialogLine("ar_true_defeat_2",
                "defeated_lord_start_2", "close_window",
                "You have won a battle. The war for this land began before your grandfather was born.",
                IsTrueEmperor, null, P); } catch { }

            try { starter.AddDialogLine("ar_false_defeat_1",
                "defeated_lord_start_1", "close_window",
                "This vessel will mend. I will not feel what you have done to it. I never do.",
                IsFalseEmperor, null, P); } catch { }

            try { starter.AddDialogLine("ar_false_defeat_2",
                "defeated_lord_start_2", "close_window",
                "You have struck something old and cold. You will understand what that means later.",
                IsFalseEmperor, null, P); } catch { }

            // ── Special request ───────────────────────────────────────────────
            try { starter.AddDialogLine("ar_true_special",
                "lord_special_request", "close_window",
                "You ask favours of emperors. That takes a particular kind of courage, or ignorance. " +
                "Come back when the Empire is whole.",
                IsTrueEmperor, null, P); } catch { }

            try { starter.AddDialogLine("ar_false_special",
                "lord_special_request", "close_window",
                "A request. How mortal of you.",
                IsFalseEmperor, null, P); } catch { }

            // ── Prisoner ──────────────────────────────────────────────────────
            try { starter.AddDialogLine("ar_true_prisoner",
                "prisoner_chat", "close_window",
                "I have waited in darker places than this. The Empire waited for me before. It will wait again.",
                IsTrueEmperor, null, P); } catch { }

            try { starter.AddDialogLine("ar_false_prisoner",
                "prisoner_chat", "close_window",
                "You have caged a body. The thing that wears it thanks you for the silence.",
                IsFalseEmperor, null, P); } catch { }
        }

        private static bool IsTrueEmperor()
        {
            try
            {
                var h = Hero.OneToOneConversationHero;
                return h != null && BurningLabQuestSystem.IsArenicosHero(h) && BurningLabQuestSystem.ArenicosIsTrue;
            }
            catch { return false; }
        }

        private static bool IsFalseEmperor()
        {
            try
            {
                var h = Hero.OneToOneConversationHero;
                return h != null && BurningLabQuestSystem.IsArenicosHero(h) && !BurningLabQuestSystem.ArenicosIsTrue;
            }
            catch { return false; }
        }
    }
}
