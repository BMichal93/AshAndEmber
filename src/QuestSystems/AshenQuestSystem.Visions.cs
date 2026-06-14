// =============================================================================
// ASH AND EMBER — AshenQuestSystem.Visions.cs
// Prereq goal checks, the two visions, pop-ups, and the wasteland rite.
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
        // ── Prereq goal checks ────────────────────────────────────────────────
        private static void CheckPrereqGoals()
        {
            try
            {
                if (!_prereqGoal1 && (Hero.MainHero?.Clan?.Tier ?? 0) >= TargetClanTier)
                {
                    _prereqGoal1 = true;
                    try { _questLog?.LogPrereq1(); } catch { }
                    if (MageKnowledge._deferredInquiry == null)
                        MageKnowledge._deferredInquiry = ShowPrereqGoalComplete1;
                }
            }
            catch { }
            try
            {
                if (!_prereqGoal2)
                {
                    var epicrotea = Settlement.All.FirstOrDefault(s =>
                        s.Name.ToString().IndexOf(EpicroteaMarker, StringComparison.OrdinalIgnoreCase) >= 0
                        && (s.IsTown || s.IsCastle));
                    if (epicrotea != null && epicrotea.OwnerClan == Hero.MainHero?.Clan)
                    {
                        _prereqGoal2 = true;
                        try { _questLog?.LogPrereq2(); } catch { }
                        if (MageKnowledge._deferredInquiry == null)
                            MageKnowledge._deferredInquiry = ShowPrereqGoalComplete2;
                    }
                }
            }
            catch { }
            try
            {
                if (_prereqGoal1 && _prereqGoal2)
                    UnlockWastelandRite();
            }
            catch { }
        }

        private static void UnlockWastelandRite()
        {
            _phase = PhaseWasteland;
            try { _questLog?.LogWastelandUnlocked(); } catch { }
            InformationManager.DisplayMessage(new InformationMessage(
                "The Wasteland Rite is revealed. Visit an Ashen-owned city to consecrate it.",
                new Color(0.4f, 0.5f, 0.85f)));
            InformationManager.DisplayMessage(new InformationMessage(
                $"Consecrate {RequiredCapitals} capitals. Epicrotea awaits the rite first.",
                new Color(0.35f, 0.45f, 0.80f)));
        }

        // ── The Silence After (vision 1) ──────────────────────────────────────
        private static void ShowHungerVision1()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "The Silence After",

                    "The moment the lord's fire goes out, you feel something you have not felt before.\n\n" +
                    "Not satisfaction. Not power. Something older.\n\n" +
                    "The space where their flame was — it is not empty. " +
                    "Something is there that was there before the flame, waiting. " +
                    "It was always there, behind every fire that was ever extinguished. " +
                    "You could feel it as cold. But it is not cold. " +
                    "Cold is just what it feels like from the outside.\n\n" +
                    "From the inside, it is absence made complete. " +
                    "A void that has been growing for ages and has finally found something to look through.\n\n" +
                    "It is looking through you now.\n\n" +
                    "And it is showing you something.",

                    true, true,
                    "Look into it.",
                    "Shut it out.",

                    () =>
                    {
                        _phase = PhaseHunger2Ready;
                    },
                    () =>
                    {
                        _phase = PhaseFailed;
                        InformationManager.DisplayMessage(new InformationMessage(
                            "You closed the door on it. It will not knock again.",
                            new Color(0.5f, 0.5f, 0.55f)));
                    }
                ), true, true);
            }
            catch { }
        }

        // ── The Hunger Speaks (vision 2) ──────────────────────────────────────
        private static void ShowHungerVision2()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "The Hunger",

                    "It shows you a city.\n\n" +
                    "Not burning — the opposite. Every fire in it extinguished at once, " +
                    "as if the city itself exhaled and did not breathe in again. " +
                    "The streets are still. The people are still. " +
                    "Not dead — emptied. Preserved in the grey permanence the cold has always offered.\n\n" +
                    "This is the Wasteland Rite. " +
                    "Not a spell, not a ritual — a consecration. " +
                    "When the cold has been given enough, a city can be remade in its image. " +
                    "The warmth does not survive it. The altars rise on their own.\n\n" +
                    "The void shows you which cities must answer.\n\n" +
                    "Seven of them. The beating hearts of the warm world — " +
                    "Pravend, Epicrotea, Pen Cannoc, Husn Fulq, Quyaz, Sargot, Marunath. " +
                    "Capitals. Centres. The places the warm world has built its faith around.\n\n" +
                    "To reach the Wasteland Rite, you must first establish your dominion — " +
                    "Clan Tier 6 — and claim Epicrotea. Stand in its great hall. " +
                    "Let the cold settle there. The rite will come to you.\n\n" +
                    "Then: city by city, until all seven stand emptied.\n\n" +
                    "The void has been waiting a very long time for someone who could carry this.\n\n" +
                    "It believes, with something that is not hope but functions like it, that you can.",

                    true, true,
                    "I will carry it.",
                    "This is not what I chose.",

                    () =>
                    {
                        _phase = PhasePrereqs;
                        try { _questLog = new AshenQuestLog(); _questLog.StartQuest(); _questLog.LogStarted(); } catch { }
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Quest added: The Hunger of the Void.",
                            new Color(0.38f, 0.50f, 0.85f)));
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"Goals: Clan Tier {TargetClanTier}  ·  Capture Epicrotea  ·  Then: consecrate 7 capitals.",
                            new Color(0.35f, 0.45f, 0.80f)));
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Check quest progress in the Grimoire (Alt+X).",
                            new Color(0.35f, 0.45f, 0.75f)));
                    },
                    () =>
                    {
                        _phase = PhaseFailed;
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The void withdraws. Something that was waiting closes. " +
                            "It will not show you this again.",
                            new Color(0.5f, 0.5f, 0.55f)));
                    }
                ), true, true);
            }
            catch { }
        }

        // ── Prereq goal pop-ups ───────────────────────────────────────────────
        private static void ShowPrereqGoalComplete1()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Cold Dominion",

                    "Your name reaches every corner of Calradia now. " +
                    "Lords who built walls against the cold send emissaries instead. " +
                    "They are afraid — which means they are already part of the way there.\n\n" +
                    "The first condition is met. Epicrotea remains.",

                    true, false, "Calradia is listening.", "",
                    () => { }, () => { }
                ), true, true);
            }
            catch { }
        }

        private static void ShowPrereqGoalComplete2()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "The Warm Heart",

                    "You stand in Epicrotea. The fires they kept burning here — " +
                    "the great torches, the hearths of the ancient court — " +
                    "flicker as you walk its halls.\n\n" +
                    "The void stirs. You can feel what Epicrotea is to the warm world: " +
                    "a centre, a symbol, a place that has believed in itself for a very long time.\n\n" +
                    "The Wasteland Rite is here now. You can feel it waiting, " +
                    "the way you felt the cold waiting behind every flame you have ever snuffed.\n\n" +
                    "The second condition is met. " +
                    "Visit any Ashen-owned city and look for the Wasteland Rite.",

                    true, false, "Begin the consecrations.", "",
                    () => { }, () => { }
                ), true, true);
            }
            catch { }
        }

        // ── Wasteland Rite (called from AshenAltarsCampaignBehavior) ──────────
        public static void ShowWastelandRiteDialog(Settlement settlement)
        {
            if (settlement == null) return;
            try
            {
                string cityName   = settlement.Name?.ToString() ?? "this city";
                bool   isCapital  = IsTargetCapital(cityName);
                string capitalNote = isCapital
                    ? $"\n\nThis is one of the seven. The void hungers for it. [{_capitalsCount}/{RequiredCapitals} claimed]"
                    : $"\n\nThis is not one of the seven capitals, but the cold will take anything offered. [{_capitalsCount}/{RequiredCapitals} capitals]";

                InformationManager.ShowInquiry(new InquiryData(
                    "The Wasteland Rite",

                    $"You stand at the centre of {cityName}. " +
                    "The cold within you reaches out — not to warm, but to void.\n\n" +
                    "The Wasteland Rite does not destroy. It hollows. " +
                    "Every fire in this city goes out at once. The villages fall silent. " +
                    "The stone remembers nothing but cold.\n\n" +
                    "What remains will serve the grey march permanently. " +
                    "An altar will rise here, as it does in all true Ashen cities. " +
                    "The people who remain will serve, or they will not remain." +
                    capitalNote,

                    true, true,
                    "Consecrate it. Let the cold have it.",
                    "Not yet.",

                    () => { OnWastelandRiteConfirmed(settlement); },
                    () => { }
                ), true, true);
            }
            catch { }
        }

        private static void OnWastelandRiteConfirmed(Settlement settlement)
        {
            if (settlement == null) return;
            try
            {
                if (!_wastelandCities.Add(settlement.StringId)) return;

                // Permanently loot all bound villages
                try
                {
                    foreach (Settlement v in Settlement.All)
                    {
                        if (!v.IsVillage || v.Village?.Bound != settlement) continue;
                        try { v.Village.VillageState = Village.VillageStates.Looted; } catch { }
                        try { v.Village.Hearth = 1f; } catch { }
                    }
                }
                catch { }

                // Lock town stats
                try
                {
                    if (settlement.Town != null)
                    {
                        settlement.Town.Loyalty  = 100f;
                        settlement.Town.Security = 100f;
                    }
                }
                catch { }

                bool isCapital = IsTargetCapital(settlement.Name?.ToString() ?? "");
                if (isCapital)
                {
                    _capitalsCount++;
                    try { _questLog?.LogCapital(settlement.Name?.ToString() ?? "", _capitalsCount); } catch { }
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{settlement.Name} — consecrated to the void. [{_capitalsCount}/{RequiredCapitals} capitals]",
                        new Color(0.4f, 0.5f, 0.9f)));

                    if (_capitalsCount >= RequiredCapitals && _phase == PhaseWasteland)
                    {
                        _phase = PhaseAllDone;
                        try { _questLog?.LogAllDone(); } catch { }
                        if (!BurningLabQuestSystem.WitheringFired && MageKnowledge._deferredInquiry == null)
                            MageKnowledge._deferredInquiry = ShowFinalPrompt;
                    }
                }
                else
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{settlement.Name} — consecrated. The void grows.",
                        new Color(0.38f, 0.50f, 0.80f)));
                }
            }
            catch { }
        }

    }
}
