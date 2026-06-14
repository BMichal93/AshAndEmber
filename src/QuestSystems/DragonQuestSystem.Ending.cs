// =============================================================================
// ASH AND EMBER — DragonQuestSystem.Ending.cs
// Final dialog before player death and grimoire summary.
// Partial of DragonQuestSystem (shared state lives in DragonQuestSystem.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public static partial class DragonQuestSystem
    {
        // ── Final dialog before player death ─────────────────────────────────
        private static void ShowEndingDialog()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "The Rekindling",

                    "The fire goes out of you all at once.\n\n" +
                    "Not violently. Not like dying in battle. " +
                    "More like a candle that has burned everything it was given.\n\n" +
                    "You feel it pass through you and out — and as it goes, " +
                    "you are briefly aware of every other flame in the world. " +
                    "Every mage whose gift shudders and releases in the same moment. " +
                    "The Ashen. The mage lords. The wanderers and hedge-witches and old teachers. " +
                    "All of them, at once.\n\n" +
                    "It is warmer than you expected.\n\n" +
                    "The darkness breaks. Somewhere the Ashen crumble. " +
                    "Somewhere a city sees dawn without cold fire in the rafters. " +
                    "Somewhere a child is born who will never know the grey march.\n\n" +
                    "They will call it something else. They will never know your name.\n\n" +
                    "The morning comes anyway.\n\n" +
                    "Then it is dark.\n\nThen nothing.",

                    true, false,
                    "It is done.",
                    "",
                    () =>
                    {
                        try { KillCharacterAction.ApplyByMurder(Hero.MainHero, null, true); }
                        catch { }
                    },
                    () => { }
                ), true, true);
            }
            catch { }
        }

        // ── Grimoire summary (called by MageKnowledge) ────────────────────────
        public static string GetGrimoireSummary()
        {
            if (_phase == PhaseIdle || _phase == PhaseEventReady)
                return "";

            if (_phase == PhaseFailed)
                return "\nQuest Failed: The Last Flight of the Dragons.\n" +
                       "The chance is gone. The world turns as it will.\n";

            if (_phase == PhaseRekindled || _worldRekindled)
                return "\nThe world is rekindled. The fire is spent.\n";

            string g1 = _goal1Done ? "✓" : "○";
            string g2 = _goal2Done ? "✓" : "○";
            string g3 = _goal3Done ? "✓" : "○";

            string status = _phase == PhaseAllDone
                ? "\n  [All conditions met — awaiting your decision.]\n"
                : "";

            return $"\nQuest: The Last Flight of the Dragons\n" +
                   $"  {g1}  Establish dominion       (Clan Tier {TargetClanTier})\n" +
                   $"  {g2}  Enter the cold heart     (capture Tyal)\n" +
                   $"  {g3}  Gain the power           (Level {TargetHeroLevel})\n" +
                   status;
        }

    }
}
