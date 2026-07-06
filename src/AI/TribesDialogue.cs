// =============================================================================
// ASH AND EMBER — AI/TribesDialogue.cs
// Replaces vanilla lord dialogue for all Tribal lords (Tribes of the East /
// khuzait kingdom) with fanatical, blood-hungry lines befitting riders sworn
// to the God-King's fire. Mirrors the structure of TempleDialogue.cs.
//
// Each lord speaks from a pool of lines selected deterministically by their
// StringId hash. Multiple variants are registered; conditions evaluate at
// conversation time so the same lord always says the same line.
// =============================================================================

using System;
using TaleWorlds.CampaignSystem;

namespace AshAndEmber
{
    internal static class TribesDialogue
    {
        internal static void Register(CampaignGameStarter starter)
        {
            const int P = 190; // below ArenicosDialogue (210) and AshenDialogue (200), above vanilla (100)

            // Flavour greeting only — route straight into the normal vanilla lord
            // options hub (hero_main_options) so Tribes lords keep every standard
            // interaction (tasks, barter, war/peace, recruitment). Gated on HasMet
            // so first-meeting introductions still run through vanilla untouched.
            RegisterPool(starter, "trb_start",    "start",                "hero_main_options", _openings, P, requireMet: true);

            // Text-only flavour on flows vanilla already ends by closing the window
            // (prisoner chat) or on legacy/unreachable tokens. These never divert
            // the normal conversation hub.
            RegisterPool(starter, "trb_barter",   "lord_barter_question", "close_window",  _barters,   P);
            RegisterPool(starter, "trb_defeat1",  "defeated_lord_start_1","close_window",  _defeats1,  P);
            RegisterPool(starter, "trb_defeat2",  "defeated_lord_start_2","close_window",  _defeats2,  P);
            RegisterPool(starter, "trb_prisoner", "prisoner_chat",        "close_window",  _prisoners, P);
        }

        // Registers one line per pool entry. Conditions pick the right variant
        // at conversation time based on the interlocutor's StringId hash.
        private static void RegisterPool(CampaignGameStarter starter,
            string idPrefix, string inputToken, string outputToken,
            string[] pool, int priority, bool requireMet = false)
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
                        () => IsTribesVariant(variant, pool.Length, requireMet),
                        null,
                        priority);
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        private static bool IsTribesVariant(int variant, int poolSize, bool requireMet)
        {
            try
            {
                var h = Hero.OneToOneConversationHero;
                if (h == null || ColourLordRegistry.IsAshenLord(h)) return false;
                if (!h.IsLord) return false;   // lord dialogue only — never notables or wanderers
                if (requireMet && !h.HasMet) return false; // let vanilla handle the first-meeting introduction
                if (h.MapFaction?.StringId != "khuzait") return false;
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
            "Your fire is a candle next to the God-King's sun. Speak before I lose patience with it.",
            "The steppe remembers every enemy who begged. Do not beg. Speak.",
            "You still draw breath. That is either luck or the God-King's amusement — say your piece before it runs out.",
            "Blood or words, stranger — the Tribes have use for both. Choose quickly.",
            "I have killed men for wasting less of my time than this. Speak, or don't.",
            "You stand before a warrior of the God-King's own blood-oath. There is no peace here — only how you spend your breath.",
        };

        private static readonly string[] _barters =
        {
            "Coin? The Tribes take what they want — we do not buy it back.",
            "The God-King's word is the only currency that matters on this steppe, and he has not spoken your name.",
            "Trade is for men who fear the blade. Give me a reason to respect you instead.",
        };

        private static readonly string[] _defeats1 =
        {
            "You have blooded me. The steppe teaches that pain sharpens — it does not break. Finish it, or don't. I care little which.",
            "A good cut. The God-King will hear I fell to a worthy hand, not a soft one.",
            "Struck down, not humbled. The Tribes do not know that word.",
        };

        private static readonly string[] _defeats2 =
        {
            "One fall does not end a war the God-King already won in his heart.",
            "Kill me or don't. Either way, the steppe outlives the both of us.",
            "You wear my blood on your blade now. Few earn that much — carry it proudly.",
        };

        private static readonly string[] _prisoners =
        {
            "Chains are a small thing. The steppe has held me in worse cages than this.",
            "I have bled for the God-King before. I will bleed again, here or free.",
            "Keep me if you dare. The Tribes do not forget where their own are kept.",
        };
    }
}
