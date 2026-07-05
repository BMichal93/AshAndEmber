// =============================================================================
// ASH AND EMBER — BurningLabQuestSystem.QuestlineC.cs
// Questline C — the personal rites and weekly prompts.
// Partial of BurningLabQuestSystem (shared state lives in BurningLabQuestSystem.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static partial class BurningLabQuestSystem
    {
        // ── Questline C ────────────────────────────────────────────────────────

        private static void TickQC()
        {
            if (!_qcActive) return;
            if (_qcWeeklyTimer > 0)
                _qcWeeklyTimer--;
            if (_qcWhisperTimer > 0)
                _qcWhisperTimer--;
            else
            {
                FireQCWhisper();
                _qcWhisperTimer = 2;
            }
        }

        private static void TickQCWeeklyPrompt()
        {
            if (!_qcActive) return;
            if (_qcWeeklyTimer > 0) return;

            // Only show if no deferred inquiry is already pending
            if (MageKnowledge._deferredInquiry != null) return;

            MageKnowledge._deferredInquiry = ShowQCWeeklyPrompt;
        }

        private static readonly string[] _qcWhispers =
        {
            "You found yourself reading again last night. You do not remember picking up the scrolls.",
            "The fire in your hands looked different this morning. Cooler. You told yourself it was the cold.",
            "You dreamed of the scholar who wrote this. He was standing in a city that no longer exists. He did not look up.",
            "There is a passage near the end you cannot read twice in succession. The words shift between readings.",
            "Your servants have started giving you more space. You have not asked them to.",
            "The fire answered slower this morning. Like something else was listening first.",
            "You found a margin note in a hand that is not the scholar's. It says: do not finish this.",
            "You woke with the scrolls in your hands. You do not remember taking them from the saddlebag.",
            "The words in the last chapter are in a language you do not know. You understood them anyway.",
            "Someone in camp asked if you were well. You said yes. You are less certain than you were.",
        };

        private static void FireQCWhisper()
        {
            string msg = _qcWhispers[_qcWhisperIndex % _qcWhispers.Length];
            _qcWhisperIndex++;
            Notify("The Burning Laboratory — " + msg);
        }

        private static void ShowQCWeeklyPrompt()
        {
            if (!_qcActive) return;
            try
            {
                var elements = new List<InquiryElement>();

                elements.Add(new InquiryElement("perform", "Perform the rite.", null, true,
                    "Long, strange, exhausting. Something about the fire inside. The price is ambiguous in all the ways that matter."));

                AddImperialOption(elements, "empire_s", "give_s",
                    "Pass them to Rhagaea's scholars.",
                    "The Southern Empire receives the scrolls.");
                AddImperialOption(elements, "empire_n", "give_n",
                    "Pass them to Lucon's court.",
                    "The Northern Empire receives the scrolls.");
                AddImperialOption(elements, "empire_w", "give_w",
                    "Pass them to Garios's mages.",
                    "The Western Empire receives the scrolls.");

                AddFactionOption(elements, "sturgia",  "give_sturgia",
                    "Pass them to the Northmen.",
                    "The Northmen receive the scrolls.");
                AddFactionOption(elements, "khuzait",  "give_khuzait",
                    "Pass them to the Tribes of the East.",
                    "The Tribes of the East receive the scrolls.");
                AddFactionOption(elements, "battania", "give_battania",
                    "Pass them to the Battanians.",
                    "Battania receives the scrolls.");
                AddFactionOption(elements, "aserai",   "give_aserai",
                    "Pass them to the Duneborn.",
                    "The Duneborn receive the scrolls.");
                AddFactionOption(elements, "vlandia",  "give_vlandia",
                    "Pass them to The Holy Temple.",
                    "The Templars receive the scrolls.");

                elements.Add(new InquiryElement("sell", "Sell them. You have carried them long enough to know their value.", null, true,
                    "+10 000 gold. −Honour. The buyer's identity is their own business."));

                elements.Add(new InquiryElement("discard", "Set them aside. Into the fire.", null, true,
                    "The scrolls burn. The knowledge dies. The warmth stops."));

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "The Burning Laboratory — The Scrolls",

                    "The scrolls are still there. You have read further than you intended. " +
                    "There is a rite described — long, strange, exhausting. The text says it does something to the practitioner. " +
                    "Something about the fire inside, burning hotter and stranger. " +
                    "The price, described in the dry language of an old scholar, is ambiguous in all the ways that matter.\n\n" +
                    "The last paragraph is marked. Someone read this before you.",

                    elements,
                    false, 1, 1,
                    "Decide",
                    "",
                    chosen =>
                    {
                        try { HandleQCWeeklyChoice(chosen?[0]?.Identifier as string ?? "discard"); }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    },
                    null, "", false
                ), false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void HandleQCWeeklyChoice(string id)
        {
            switch (id)
            {
                case "perform":
                    try { PerformQCRite(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    break;

                case "sell":
                {
                    _qcActive = false;
                    try { _qcQuestLog?.LogGivenAway("a merchant"); _qcQuestLog?.CompleteFail(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    GainGold(10000);
                    ShiftHonour(-1);
                    bool soldToImperial = _rng.Next(100) < SellImperialChance;
                    string receivingEmpireId = soldToImperial ? PickLivingImperialEmpireId() : null;
                    if (receivingEmpireId != null)
                    {
                        Kingdom emp = Kingdom.All.FirstOrDefault(k => k.StringId == receivingEmpireId && !k.IsEliminated);
                        string empName = emp?.Name?.ToString() ?? "an imperial court";
                        Notify(
                            "The Burning Laboratory — the scrolls change hands in a tavern you will not visit again. " +
                            $"Three days later you hear the name: {empName}. Their scholars are already at work.");
                        StartQuestlineA(receivingEmpireId);
                    }
                    else
                    {
                        Notify(
                            "The Burning Laboratory — the buyer did not give a name. The scrolls left in a locked chest " +
                            "and you watched them go and felt something you are not certain how to name.");
                        _phase = PhaseEnded;
                    }
                    break;
                }

                case "give_s":        HandleQCGive("empire_s",  true);  break;
                case "give_n":        HandleQCGive("empire_n",  true);  break;
                case "give_w":        HandleQCGive("empire_w",  true);  break;
                case "give_sturgia":  HandleQCGive("sturgia",   false); break;
                case "give_khuzait":  HandleQCGive("khuzait",   false); break;
                case "give_battania": HandleQCGive("battania",  false); break;
                case "give_aserai":   HandleQCGive("aserai",    false); break;
                case "give_vlandia":  HandleQCGive("vlandia",   false); break;

                default: // "discard"
                    _qcActive = false;
                    _phase    = PhaseEnded;
                    Notify(
                        "The Burning Laboratory — the scrolls go into the fire. " +
                        "You watch them burn. It takes longer than it should. " +
                        "The last line is still readable when the ashes cool. " +
                        "You do not write it down.");
                    try { _qcQuestLog?.LogDiscarded(); _qcQuestLog?.CompleteFail(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    break;
            }
        }

        private static void HandleQCGive(string factionId, bool isImperial)
        {
            Kingdom faction = Kingdom.All.FirstOrDefault(k => k.StringId == factionId && !k.IsEliminated);
            string factionName = faction?.Name?.ToString() ?? "the recipient";
            _qcActive = false;
            Notify(
                $"The Burning Laboratory — the scrolls are handed over to {factionName}. " +
                "You held them longer than you meant to. You are not certain what you expected to feel.");
            try { _qcQuestLog?.LogGivenAway(factionName); _qcQuestLog?.CompleteFail(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            if (isImperial)
                StartQuestlineA(factionId);
            else
                StartQuestlineB(factionId);
        }

        private static void PerformQCRite()
        {
            Hero h = Hero.MainHero;
            if (h == null) return;

            // Renown
            if (h.Clan != null)
                h.Clan.Renown = Math.Max(0f, h.Clan.Renown + 50f);

            // Large XP grant across fitting skills
            try { h.AddSkillXp(DefaultSkills.Athletics,   3000f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { h.AddSkillXp(DefaultSkills.Medicine,    3000f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { h.AddSkillXp(DefaultSkills.Roguery,     3000f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { h.AddSkillXp(DefaultSkills.Leadership,  3000f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { h.AddSkillXp(DefaultSkills.Charm,       2000f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Lose honour
            ShiftHonour(-1);

            // 5 % chance: become Ashen
            bool turnedAshen = _rng.NextDouble() < 0.05;
            if (turnedAshen && !MageKnowledge.IsAshen)
            {
                try { MageKnowledge.SetMage(true); }    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { MageKnowledge.SetAshen(true); }   catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { MageKnowledge.ApplyAshenAppearance(h); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { AshenCitySystem.OnPlayerBecameAshen(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                InformationManager.DisplayMessage(new InformationMessage(
                    "The Burning Laboratory — the fire in you goes cold. Something else answers instead.",
                    new Color(0.3f, 0.35f, 0.7f)));
                try { _qcQuestLog?.LogBecameAshen(); _qcQuestLog?.CompleteSuccess(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                _qcActive = false;
                _phase    = PhaseEnded;
            }
            else
            {
                Notify(
                    "The Burning Laboratory — the rite is completed. " +
                    "You are not certain what you expected. What you received was stranger and quieter. " +
                    "The fire inside burned different for two days — hotter and without direction. " +
                    "It has settled back now. But not entirely to where it was.");
                try { _qcQuestLog?.LogRitePerformed(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                _qcWeeklyTimer = QCWeeklyDelay; // Schedule next prompt
            }
        }

    }
}
