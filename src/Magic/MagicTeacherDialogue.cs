// =============================================================================
// ASH AND EMBER — Magic/MagicTeacherDialogue.cs
//
// The attuned — those the land speaks through (Nature Seers) — each carry ONE
// craft of the inner fire beyond their own. When a mage who already carries the
// fire speaks with one, they may be taught that craft for one focus point less
// than the lonely road would cost (see MagicLearning.NextCost(fromTeacher:true)).
//
// Which craft a given teacher carries is fixed by who they are — a stable hash
// of their identity — so the same lord always teaches the same power.
// =============================================================================

using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    internal static class MagicTeacherDialogue
    {
        private const int P = 150; // above vanilla (100), below the Ashen "..." lines (200)

        internal static void Register(CampaignGameStarter starter)
        {
            // Offer from the lord's main conversation hub.
            try
            {
                starter.AddPlayerLine("ae_teach_ask", "hero_main_options", "ae_teach_reply",
                    "There is a craft in you the others lack. Teach me what you carry.",
                    CanTeach, null, P);

                starter.AddDialogLine("ae_teach_reply", "ae_teach_reply", "ae_teach_choice",
                    "\"{AE_TEACH_INTRO}\"", null, null, P);

                starter.AddPlayerLine("ae_teach_yes", "ae_teach_choice", "ae_teach_done",
                    "Show me. ({AE_TEACH_COST} focus)", null, DoTeach, P);
                starter.AddPlayerLine("ae_teach_no", "ae_teach_choice", "ae_teach_done",
                    "Another time.", null, null, P);

                starter.AddDialogLine("ae_teach_done", "ae_teach_done", "hero_main_options",
                    "\"The fire keeps its own counsel. Come back to it.\"", null, null, P);
            }
            catch { }
        }

        // The conversation partner is an attuned teacher; the player is a mage who
        // carries the fire but not yet this teacher's craft.
        private static bool CanTeach()
        {
            try
            {
                if (!MageKnowledge.IsMage) return false;
                var h = Hero.OneToOneConversationHero;
                if (h == null || h == Hero.MainHero) return false;
                if (ColourLordRegistry.IsAshenLord(h)) return false;   // the Ashen teach nothing
                if (!NatureSeerRegistry.IsNatureSeer(h)) return false;

                MagePower power = PowerOf(h);
                if (MagicLearning.Has(power)) return false;            // nothing left to teach you

                int cost = MagicLearning.NextCost(fromTeacher: true);
                MBTextManager.SetTextVariable("AE_TEACH_INTRO", TeachIntro(power));
                MBTextManager.SetTextVariable("AE_TEACH_COST", cost.ToString());
                return true;
            }
            catch { return false; }
        }

        private static void DoTeach()
        {
            try
            {
                var h = Hero.OneToOneConversationHero;
                if (h == null) return;
                MagePower power = PowerOf(h);
                if (MagicLearning.TryLearn(power, fromTeacher: true, out string msg))
                    InformationManager.DisplayMessage(new InformationMessage(msg, Gold));
                else
                    InformationManager.DisplayMessage(new InformationMessage(msg, Dim));
            }
            catch { }
        }

        // ── Which craft a teacher carries ────────────────────────────────────────
        // A stable hash of the hero's identity picks one of the seven powers, so a
        // given lord always teaches the same thing across saves and sessions.
        public static MagePower PowerOf(Hero hero)
        {
            int n = 7; // MagePower.Wind .. MagePower.Nature
            try
            {
                string id = hero?.StringId ?? "";
                int h = StableHash(id);
                return (MagePower)(((h % n) + n) % n);
            }
            catch { return MagePower.Wind; }
        }

        // FNV-1a — stable across processes (string.GetHashCode is not guaranteed to be).
        private static int StableHash(string s)
        {
            unchecked
            {
                int hash = (int)2166136261;
                foreach (char c in s) { hash ^= c; hash *= 16777619; }
                return hash;
            }
        }

        private static string TeachIntro(MagePower p)
        {
            switch (p)
            {
                case MagePower.Wind:   return "You feel it already — the air leans toward you. I can show you how to make it lean farther.";
                case MagePower.Earth:  return "The stone is patient and so am I. Sit, and I will teach your fire to speak through soil.";
                case MagePower.Water:  return "Water does not fight. It waits, and it wins. There is a working in that, if you would learn it.";
                case MagePower.Spirit: return "Men break before swords ever touch them. I know the words the fire whispers into a frightened mind.";
                case MagePower.Steel:  return "They told you to put down the blade to draw. They were wrong, and lazy. I will teach you to hold both.";
                case MagePower.Blood:  return "When a great one falls by your hand, the years it cost you can be taken back. It is an ugly craft. I carry it anyway.";
                default:               return "You burn through your life because you are in a hurry. Draw slow, with the land, and it costs you almost nothing. Let me show you.";
            }
        }

        private static readonly Color Gold = new Color(0.95f, 0.8f, 0.3f);
        private static readonly Color Dim  = new Color(0.7f, 0.65f, 0.55f);
    }
}
