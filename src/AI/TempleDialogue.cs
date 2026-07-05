// =============================================================================
// ASH AND EMBER — AI/TempleDialogue.cs
// Replaces vanilla lord dialogue for all Templar lords (The Holy Temple /
// vlandia kingdom) with religiously grounded lines befitting warrior-monks
// of the Order. Mirrors the structure of AshenDialogue.cs.
//
// Each lord speaks from a pool of lines selected deterministically by their
// StringId hash. Multiple variants are registered; conditions evaluate at
// conversation time so the same lord always says the same line.
// =============================================================================

using System;
using TaleWorlds.CampaignSystem;

namespace AshAndEmber
{
    internal static class TempleDialogue
    {
        internal static void Register(CampaignGameStarter starter)
        {
            const int P = 190; // below ArenicosDialogue (210) and AshenDialogue (200), above vanilla (100)

            RegisterPool(starter, "tpl_start",    "start",                "tpl_reply",     _openings,  P);
            RegisterPool(starter, "tpl_pretalk",  "lord_pretalk",         "tpl_reply",     _pretalks,  P);
            RegisterPool(starter, "tpl_barter",   "lord_barter_question", "close_window",  _barters,   P);
            RegisterPool(starter, "tpl_defeat1",  "defeated_lord_start_1","close_window",  _defeats1,  P);
            RegisterPool(starter, "tpl_defeat2",  "defeated_lord_start_2","close_window",  _defeats2,  P);
            RegisterPool(starter, "tpl_special",  "lord_special_request", "close_window",  _specials,  P);
            RegisterPool(starter, "tpl_prisoner", "prisoner_chat",        "close_window",  _prisoners, P);

            try { starter.AddPlayerLine("tpl_reply_matter", "tpl_reply", "lord_pretalk", "I have a matter for the Order.",      null, null, P); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { starter.AddPlayerLine("tpl_reply_light",  "tpl_reply", "lord_pretalk", "The Light keep you.",                 null, null, P); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { starter.AddPlayerLine("tpl_reply_leave",  "tpl_reply", "close_window", "I will not keep you from the vigil.", null, null, P); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                        () => IsTempleVariant(variant, pool.Length),
                        null,
                        priority);
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        private static bool IsTempleVariant(int variant, int poolSize)
        {
            try
            {
                var h = Hero.OneToOneConversationHero;
                if (h == null || ColourLordRegistry.IsAshenLord(h)) return false;
                if (!h.IsLord) return false;   // lord dialogue only — never notables or wanderers
                if (h.MapFaction?.StringId != "vlandia") return false;
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
            "The Order does not stand on ceremony. Say what you have come to say.",
            "Speak plainly. The vigil does not pause for titles.",
            "You stand before a servant of the Light. Make your words count.",
            "Your fire reaches me before your name does. What is it you want?",
            "The march will not wait. Neither do I. Speak.",
            "You have been watched. The Order remembers every face that comes twice. What brings you here?",
        };

        private static readonly string[] _pretalks =
        {
            "You have my attention. Briefly.",
            "The Order's business is not finished. What is it you require?",
            "I remember your fire from before. Is it still clean?",
            "Say it quickly. I have a column to rejoin before nightfall.",
            "You return. The Order has noted each visit. Speak.",
        };

        private static readonly string[] _barters =
        {
            "The Order does not barter. It pledges, and it keeps what it pledges. Come to me with a vow, not a price.",
            "Coin is for merchants. The Light trades in something heavier. I cannot help you this way.",
            "What you are offering is not what the Order needs. Return when you have something worth a vow.",
        };

        private static readonly string[] _defeats1 =
        {
            "Strike me if you intend to. The Light does not abandon those who fall in its service.",
            "You have bested me. The Order will hear of it — they keep an honest record of such things.",
            "A clean fight. Whatever comes next, know that I hold no quarrel with you for this.",
        };

        private static readonly string[] _defeats2 =
        {
            "The vigil does not end with a single defeat. The Order continues whether I stand or not.",
            "Well struck. The Order teaches us to acknowledge that honestly. I acknowledge it.",
            "The fire in you is not the cold. That matters more than the outcome of any single field.",
        };

        private static readonly string[] _specials =
        {
            "Bring what you carry to the High Altar. Some things require proper witness.",
            "The Order has already considered this. We are patient. Come back when the time is right.",
            "What you are asking is known to us. Whether it can be given is not mine to say alone.",
        };

        private static readonly string[] _prisoners =
        {
            "Captivity is a kind of vigil. I have kept harder ones.",
            "The Light has not abandoned me yet. I will wait.",
            "I have been in smaller rooms, held by worse keepers. The Order will find me when it is time.",
        };
    }
}
