// =============================================================================
// ASH AND EMBER — AI/DunebornDialogue.cs
// Replaces vanilla lord dialogue for all Duneborn lords (Duneborn / aserai
// kingdom) with distant, calculating lines befitting the desert's scholars
// and quiet debt-collectors. Mirrors the structure of TempleDialogue.cs.
//
// Each lord speaks from a pool of lines selected deterministically by their
// StringId hash. Multiple variants are registered; conditions evaluate at
// conversation time so the same lord always says the same line.
// =============================================================================

using System;
using TaleWorlds.CampaignSystem;

namespace AshAndEmber
{
    internal static class DunebornDialogue
    {
        internal static void Register(CampaignGameStarter starter)
        {
            const int P = 190; // below ArenicosDialogue (210) and AshenDialogue (200), above vanilla (100)

            RegisterPool(starter, "dun_start",    "start",                "dun_reply",     _openings,  P);
            RegisterPool(starter, "dun_pretalk",  "lord_pretalk",         "dun_reply",     _pretalks,  P);
            RegisterPool(starter, "dun_barter",   "lord_barter_question", "close_window",  _barters,   P);
            RegisterPool(starter, "dun_defeat1",  "defeated_lord_start_1","close_window",  _defeats1,  P);
            RegisterPool(starter, "dun_defeat2",  "defeated_lord_start_2","close_window",  _defeats2,  P);
            RegisterPool(starter, "dun_special",  "lord_special_request", "close_window",  _specials,  P);
            RegisterPool(starter, "dun_prisoner", "prisoner_chat",        "close_window",  _prisoners, P);

            try { starter.AddPlayerLine("dun_reply_matter", "dun_reply", "lord_pretalk", "I have a matter that may interest you.", null, null, P); } catch { }
            try { starter.AddPlayerLine("dun_reply_wary",   "dun_reply", "lord_pretalk", "I won't pretend to trust you.",          null, null, P); } catch { }
            try { starter.AddPlayerLine("dun_reply_leave",  "dun_reply", "close_window", "I'll leave you to your calculations.",   null, null, P); } catch { }
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
                        () => IsDunebornVariant(variant, pool.Length),
                        null,
                        priority);
                }
                catch { }
            }
        }

        private static bool IsDunebornVariant(int variant, int poolSize)
        {
            try
            {
                var h = Hero.OneToOneConversationHero;
                if (h == null || ColourLordRegistry.IsAshenLord(h)) return false;
                if (h.MapFaction?.StringId != "aserai") return false;
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
            "You have crossed a great deal of sand to stand here. I wonder if you understand what that costs you yet.",
            "Speak. I have learned to weigh a man's worth before his second sentence.",
            "The desert teaches patience, stranger — I have more of it than you have time. Say what you came for.",
            "Every visitor carries a price they haven't noticed yet. Let us discover yours. Speak.",
            "You stand closer to me than most men are permitted. Choose your words as if that mattered — because it does.",
            "I have already decided three things about you. Say something to change my mind, or don't bother.",
        };

        private static readonly string[] _pretalks =
        {
            "You return. I confess I did not expect that — or perhaps I did. Speak.",
            "The sand remembers footsteps longer than most men remember debts. What do you want this time?",
            "I have thought of you since we last spoke. Not fondly. Not unfondly. Go on.",
            "Say it plainly, if such a thing is in your nature. I will hear the rest regardless.",
            "You are still useful to me. That is the only reason this conversation continues.",
        };

        private static readonly string[] _barters =
        {
            "Coin is a crude language. I prefer debts — they last longer, and I collect them at my leisure.",
            "What you offer is not what you think it is. I already knew its price before you named it.",
            "I do not trade. I invest. Bring me something worth the patience it will cost me to wait.",
        };

        private static readonly string[] _defeats1 =
        {
            "Interesting. I had not accounted for this outcome — I will, next time. Do what you intend.",
            "You have won something today, though I doubt you know what it is yet.",
            "A miscalculation on my part. I do not make the same one twice.",
        };

        private static readonly string[] _defeats2 =
        {
            "Defeat is only ever temporary, for men who plan as I do. Enjoy this while it lasts.",
            "You have my measure now — a dangerous thing to hold. Be careful what you do with it.",
            "I concede the field, not the account. We are far from settled, you and I.",
        };

        private static readonly string[] _specials =
        {
            "That request has weight to it. I will need to consider what it truly costs you before I answer.",
            "Bring that to me somewhere the sand cannot listen. Some bargains are not made in daylight.",
            "I know already what you are asking. Whether I will admit it is a different matter.",
        };

        private static readonly string[] _prisoners =
        {
            "A cage changes nothing I have already set in motion. You will find that out eventually.",
            "I have waited longer than this in worse places, for better reasons. I can wait again.",
            "Keep me, if it comforts you. I have never needed to be free to get what I want.",
        };
    }
}
