// =============================================================================
// ASH AND EMBER — AshenQuestSystem.Ending.cs
// Final prompt, ending sequence, final dialog, grimoire summary.
// Partial of AshenQuestSystem (shared state lives in AshenQuestSystem.cs).
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
    public static partial class AshenQuestSystem
    {
        // ── Final prompt ──────────────────────────────────────────────────────
        private static void ShowFinalPrompt()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "The Seven Are Done",

                    "All seven capitals stand consecrated.\n\n" +
                    "The void has what it asked for — " +
                    "the warm world's centres, hollowed, remade, " +
                    "altars rising in the silence where the fires were.\n\n" +
                    "One act remains. " +
                    "The mage-lords still carry the last fires in Calradia — " +
                    "the wandering gifted, the warm-blooded lords who refused the cold. " +
                    "The void will move through you and extinguish them all at once. " +
                    "Not destruction. Completion.\n\n" +
                    "What comes after is permanent. " +
                    "The world will not end. It will simply stop burning.\n\n" +
                    "The void has been patient for a very long time.\n\n" +
                    "It is ready now. So are you.",

                    true, true,
                    "Finish it. Let the cold have everything.",
                    "Not like this. Not yet.",

                    () =>
                    {
                        _phase       = PhaseFrozen;
                        _endingPhase = 1;
                        try { _questLog?.LogComplete(); } catch { }
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The cold moves through you. There is no calling it back.",
                            new Color(0.4f, 0.5f, 0.9f)));
                    },
                    () =>
                    {
                        _phase = PhaseFailed;
                        try { _questLog?.LogFailed(); } catch { }
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The void withdraws. It has outlasted everything before you. " +
                            "It will outlast your hesitation too.",
                            new Color(0.5f, 0.5f, 0.55f)));
                    }
                ), true, true);
            }
            catch { }
        }

        // ── Ending sequence ───────────────────────────────────────────────────
        // Phase 1: Set frozen, kill mage lords (up to 5).
        // Phase 2: Kill remaining mage lords (up to 10).
        // Phase 3: Kill remaining mage lords + mage companions.
        // Phase 4: Final dialog → player dies.
        private static void TickEnding()
        {
            try
            {
                switch (_endingPhase)
                {
                    case 1:
                        _worldFrozen = true;
                        KillMageLords(5);
                        _endingPhase = 2;
                        break;

                    case 2:
                        KillMageLords(10);
                        _endingPhase = 3;
                        break;

                    case 3:
                        KillMageLords(20);
                        KillMageCompanions();
                        _endingPhase = 4;
                        break;

                    case 4:
                        _endingPhase = 5;
                        if (MageKnowledge._deferredInquiry == null)
                            MageKnowledge._deferredInquiry = ShowEndingDialog;
                        break;
                }
            }
            catch { }
        }

        private static void KillMageLords(int cap)
        {
            int killed = 0;
            try
            {
                foreach (Hero h in Hero.AllAliveHeroes.ToList())
                {
                    if (killed >= cap) break;
                    if (!h.IsAlive || h.IsChild || h == Hero.MainHero) continue;
                    if (!ColourLordRegistry.IsColourLord(h)) continue;
                    if (ColourLordRegistry.IsAshenLord(h)) continue;
                    try { KillCharacterAction.ApplyByMurder(h, null, false); killed++; }
                    catch { }
                }
            }
            catch { }
        }

        private static void KillMageCompanions()
        {
            try
            {
                var roster = MobileParty.MainParty?.MemberRoster;
                if (roster == null) return;
                foreach (var entry in roster.GetTroopRoster().ToList())
                {
                    Hero companion = entry.Character?.HeroObject;
                    if (companion == null || companion == Hero.MainHero) continue;
                    if (!ColourLordRegistry.IsColourLord(companion)) continue;
                    if (ColourLordRegistry.IsAshenLord(companion)) continue;
                    try { KillCharacterAction.ApplyByMurder(companion, null, false); } catch { }
                }
            }
            catch { }
        }

        // ── Final dialog ──────────────────────────────────────────────────────
        private static void ShowEndingDialog()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "The Void Complete",

                    "The last fire goes out.\n\n" +
                    "Not violently. Not with struggle. " +
                    "The warmth was always going to end — you simply made it certain.\n\n" +
                    "You feel it move through you, the void that has been waiting since the world was young. " +
                    "It does not take from you. It acknowledges you. " +
                    "Every mage whose fire shudders and releases in the same moment — " +
                    "you are briefly, perfectly aware of all of them. " +
                    "Bright recognitions, then silence.\n\n" +
                    "The world does not end. It settles.\n\n" +
                    "Somewhere a city wakes to grey light and altars where hearths used to be. " +
                    "Somewhere a child is born who will never know the burning wars. " +
                    "Somewhere the void holds everything it was promised — " +
                    "the seven capitals, the empty roads, the permanent grey morning — " +
                    "and the world, still and cold and finished, does not argue.\n\n" +
                    "The grey march has arrived.\n\n" +
                    "You were the last thing it needed.\n\n" +
                    "The void does not need you anymore.\n\n" +
                    "And so it keeps you.\n\nForever.",

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

        // ── Grimoire summary ──────────────────────────────────────────────────
        public static string GetGrimoireSummary()
        {
            switch (_phase)
            {
                case PhaseIdle:
                case PhaseHungerReady:
                case PhaseHunger2Ready:
                    return "";

                case PhaseFailed:
                    return "\nQuest Failed: The Hunger of the Void.\n" +
                           "The void closed. It will not show you this again.\n";

                case PhaseFrozen:
                    return "\nThe void is complete. The world is still.\n";

                case PhasePrereqs:
                {
                    string g1 = _prereqGoal1 ? "✓" : "○";
                    string g2 = _prereqGoal2 ? "✓" : "○";
                    return $"\nQuest: The Hunger of the Void\n" +
                           $"  {g1}  Cold dominion  (Clan Tier {TargetClanTier})\n" +
                           $"  {g2}  Claim the warm heart  (capture Epicrotea)\n" +
                           "  ○  Wasteland Rite  [locked until both conditions met]\n";
                }

                case PhaseWasteland:
                case PhaseAllDone:
                {
                    string done  = _phase == PhaseAllDone ? "\n  [All seven consecrated — awaiting final rite.]\n" : "";
                    string caps  = string.Join(", ", TargetCapitalNames.Select(n =>
                    {
                        bool consecrated = _wastelandCities.Any(id =>
                        {
                            var s = Settlement.All.FirstOrDefault(x => x.StringId == id);
                            return s != null && s.Name.ToString().IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0;
                        });
                        return consecrated ? $"[✓{n}]" : n;
                    }));
                    return $"\nQuest: The Hunger of the Void\n" +
                           $"  Consecrate the seven capitals  [{_capitalsCount}/{RequiredCapitals}]\n" +
                           $"  {caps}\n" +
                           done;
                }

                default:
                    return "";
            }
        }

    }
}
