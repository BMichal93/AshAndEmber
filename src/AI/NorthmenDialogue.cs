// =============================================================================
// ASH AND EMBER — AI/NorthmenDialogue.cs
// Replaces vanilla lord dialogue for all Northmen lords (Northmen / sturgia
// kingdom) with blunt, hardy lines befitting the folk who hold the cold line
// against the Ashen. Mirrors the structure of TempleDialogue.cs.
//
// Each lord speaks from a pool of lines selected deterministically by their
// StringId hash. Multiple variants are registered; conditions evaluate at
// conversation time so the same lord always says the same line.
// =============================================================================

using System;
using TaleWorlds.CampaignSystem;

namespace AshAndEmber
{
    internal static class NorthmenDialogue
    {
        internal static void Register(CampaignGameStarter starter)
        {
            const int P = 190; // below ArenicosDialogue (210) and AshenDialogue (200), above vanilla (100)

            RegisterPool(starter, "nor_start",    "start",                "nor_reply",     _openings,  P);
            RegisterPool(starter, "nor_pretalk",  "lord_pretalk",         "nor_reply",     _pretalks,  P);
            RegisterPool(starter, "nor_barter",   "lord_barter_question", "close_window",  _barters,   P);
            RegisterPool(starter, "nor_defeat1",  "defeated_lord_start_1","close_window",  _defeats1,  P);
            RegisterPool(starter, "nor_defeat2",  "defeated_lord_start_2","close_window",  _defeats2,  P);
            RegisterPool(starter, "nor_special",  "lord_special_request", "close_window",  _specials,  P);
            RegisterPool(starter, "nor_prisoner", "prisoner_chat",        "close_window",  _prisoners, P);

            try { starter.AddPlayerLine("nor_reply_business", "nor_reply", "lord_pretalk", "I've business worth your time.",       null, null, P); } catch { }
            try { starter.AddPlayerLine("nor_reply_respect",  "nor_reply", "lord_pretalk", "You've my respect, plain as that.",    null, null, P); } catch { }
            try { starter.AddPlayerLine("nor_reply_leave",    "nor_reply", "close_window", "I'll leave you to the watch.",         null, null, P); } catch { }
        }

        // Registers one line per pool entry. Conditions pick the right variant
        // at conversation time based on the interlocutor's StringId hash.
        private static void RegisterPool(CampaignGameStarter starter,
            string idPrefix, string inputToken, string outputToken,
            string[] pool, int priority)
        {
            for (int i = 0; i < pool.Length; i++)
            {
                int variant = i; // capture for closure
                string text = pool[i];
                try
                {
                    starter.AddDialogLine(
                        $"{idPrefix}_{variant}",
                        inputToken, outputToken,
                        text,
                        () => IsNorthmenVariant(variant, pool.Length),
                        null,
                        priority);
                }
                catch { }
            }
        }

        private static bool IsNorthmenVariant(int variant, int poolSize)
        {
            try
            {
                var h = Hero.OneToOneConversationHero;
                if (h == null || ColourLordRegistry.IsAshenLord(h)) return false;
                if (!h.IsLord) return false;   // lord dialogue only — never notables or wanderers
                if (h.MapFaction?.StringId != "sturgia") return false;
                int idx = Math.Abs(DeterministicHash(h.StringId ?? h.Name?.ToString() ?? "")) % poolSize;
                return idx == variant;
            }
            catch { return false; }
        }

        // String.GetHashCode() is randomized per-process in .NET 4.7.2+.
        // This deterministic version ensures the same lord always says the same line.
        private static int DeterministicHash(string s)
        {
            int h = 0;
            foreach (char c in s) h = h * 31 + c;
            return h;
        }

        // ── Line pools ─────────────────────────────────────────────────────────

        private static readonly string[] _openings =
        {
            "Say your business plainly. The cold has no patience for riddles, and neither do I.",
            "You've caught me between watches. Speak your mind — there's little time to spare.",
            "I don't waste words on courtesies. What do you want?",
            "Stand and speak plainly. A Northman judges a man by his aim, not his manners.",
            "The watch-fires need tending before dusk. Say what you came to say.",
            "You've crossed half the north to find me. It had better be worth the walk.",
        };

        private static readonly string[] _pretalks =
        {
            "Back again. Good — I like a man who finishes what he starts. What is it?",
            "Speak plainly, same as before. I've no more patience for riddles now than then.",
            "The line holds another day. What do you need of me?",
            "You're still standing. That says something about you. Go on.",
            "Say it straight. I've a watch to stand before dark.",
        };

        private static readonly string[] _barters =
        {
            "I don't haggle. Name your need straight, or don't name it at all.",
            "Coin doesn't hold the line against the Ashen. Bring me something that does.",
            "A Northman's word is worth more than his purse. I'll not trade in the other direction.",
        };

        private static readonly string[] _defeats1 =
        {
            "Well fought. I'd shake your hand if my arm still answered me.",
            "You've beaten me fair. I'll not pretend otherwise — that's not our way.",
            "Strike true or let me stand. Either way, I'll not beg for it.",
        };

        private static readonly string[] _defeats2 =
        {
            "The line doesn't fall because one man does. Others will hold it after me.",
            "You've earned this. I'll say it plainly, and mean it.",
            "I've faced worse than you and colder things than death. Do what you came to do.",
        };

        private static readonly string[] _specials =
        {
            "Bring it to the hall. Matters like that aren't settled on open ground.",
            "That's a heavier ask than it sounds. Give me time to weigh it honestly.",
            "I'll not promise what I can't deliver. Let me think on it plainly.",
        };

        private static readonly string[] _prisoners =
        {
            "A cage is just another kind of winter. I've outlasted worse.",
            "I'll hold here as long as I must. The north taught me that much.",
            "Chain me if it eases you. I've stood longer watches than this.",
        };
    }
}
