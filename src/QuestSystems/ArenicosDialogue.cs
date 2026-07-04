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
                "You stand before Arenicos — emperor of a united Calradia, returned. " +
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
                "Come forward. I have learned to be patient with arrivals. There have been many. Say what you came to say.",
                IsFalseEmperor, null, P); } catch { }

            try { starter.AddDialogLine("ar_false_pretalk",
                "lord_pretalk", "ar_reply",
                "You return. I find I remember your face very clearly. I find I remember most things very clearly, now.",
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
                "Put it away. What I want and what you can offer are rarely the same kind of thing.",
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
                "You are better than the last one who stood where you are standing. I noted the difference. I will remember this.",
                IsFalseEmperor, null, P); } catch { }

            try { starter.AddDialogLine("ar_false_defeat_2",
                "defeated_lord_start_2", "close_window",
                "I have been in this position before. Not recently — but I remember how it resolved. Have patience with the process.",
                IsFalseEmperor, null, P); } catch { }

            // ── Special request ───────────────────────────────────────────────
            try { starter.AddDialogLine("ar_true_special",
                "lord_special_request", "close_window",
                "You ask favours of emperors. That takes a particular kind of courage, or ignorance. " +
                "Come back when the Empire is whole.",
                IsTrueEmperor, null, P); } catch { }

            try { starter.AddDialogLine("ar_false_special",
                "lord_special_request", "close_window",
                "You want something. I have noted it. Whether it can be given is a separate question.",
                IsFalseEmperor, null, P); } catch { }

            // ── Prisoner ──────────────────────────────────────────────────────
            try { starter.AddDialogLine("ar_true_prisoner",
                "prisoner_chat", "close_window",
                "I have waited in darker places than this. The Empire waited for me before. It will wait again.",
                IsTrueEmperor, null, P); } catch { }

            try { starter.AddDialogLine("ar_false_prisoner",
                "prisoner_chat", "close_window",
                "I have been in smaller rooms. I have been in rooms with no door. You learn to wait.",
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
