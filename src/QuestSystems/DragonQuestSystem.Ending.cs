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
                    "The Binding",

                    "You do not feel it happen as a single thing.\n\n" +
                    "Eleven others beside you. All willing. The cold outside is already moving — " +
                    "not marching, not advancing. Moving toward something it recognises as inevitable. " +
                    "The Ashen come because something in them has been building toward this " +
                    "since before you were born. They are not being destroyed. " +
                    "They are arriving.\n\n" +
                    "The ritual is older than the Temple, older than the Empire, " +
                    "older than the civilisation that wrote those tablets. " +
                    "A complete thing: warmth and absence, balanced for a moment into something that settles.\n\n" +
                    "You feel the other eleven fires go out around you — not extinguished. Spent. Given. " +
                    "You feel the cold fire come in from the grey outside, " +
                    "drawn to the centre of what you have all begun. " +
                    "Not violent. Purposeful. Even the cold has a direction.\n\n" +
                    "For a moment you are aware of everything: the march stopping, the grey retreating, " +
                    "somewhere the first new fire kindling that will not know what it cost.\n\n" +
                    "The High Templar was right. It is not a solution. It is more time.\n\n" +
                    "You think: that is enough.\n\n" +
                    "Then you do not think anything.",

                    true, false,
                    "It is done.",
                    "",
                    () =>
                    {
                        try { KillCharacterAction.ApplyByMurder(Hero.MainHero, null, true); } catch { }
                    },
                    () => { }
                ), true, true);
            }
            catch { }
        }

        // ── Grimoire summary (called by MageKnowledge.UI) ────────────────────
        public static string GetGrimoireSummary()
        {
            if (_phase == PhaseIdle || _phase == PhaseContacted)
                return "";

            if (_phase == PhaseRefused)
                return "\nQuest Ended: The Silence Between Fires.\n" +
                       "You walked away from the Binding. The world turns as it always has.\n";

            if (_phase == PhaseAccepted || _worldBound)
                return "\nThe Binding has fired. The grey retreats.\n" +
                       "The world has more time. So it has been for every cycle.\n";

            string l  = _lordsSlain     >= TargetLordsSlain    ? "✓" : $"{_lordsSlain}/{TargetLordsSlain}";
            string ml = _mageLordsSlain >= TargetMageLordsSlain ? "✓" : $"{_mageLordsSlain}/{TargetMageLordsSlain}";
            string c  = _citiesTaken    >= TargetCitiesTaken   ? "✓" : $"{_citiesTaken}/{TargetCitiesTaken}";
            string r  = AshenRuinSystem.ClearedCount >= TargetRuinsCleared
                        ? "✓" : $"{AshenRuinSystem.ClearedCount}/{TargetRuinsCleared}";

            string status = _phase == PhaseAllDone
                ? "\n  [All conditions met — the Temple's final letter waits.]\n"
                : "";

            string letters = _letterPhase > 0
                ? $"\n  Temple correspondence received: {_letterPhase}/6\n"
                : "";

            return $"\nQuest: The Silence Between Fires\n" +
                   $"  {l}   Cold embers claimed   (need {TargetLordsSlain})\n" +
                   $"  {ml}  Warm embers claimed   (need {TargetMageLordsSlain})\n" +
                   $"  {c}   Strongholds claimed   (need {TargetCitiesTaken})\n" +
                   $"  {r}   Ashen ruins cleared   (need {TargetRuinsCleared})\n" +
                   letters +
                   status;
        }

    }
}
