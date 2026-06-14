// =============================================================================
// ASH AND EMBER — MageKnowledge.Events.cs
// Ashen prompt, possession event, and 'The Cold Calls Your Name'.
// Partial of MageKnowledge (shared static state lives in MageKnowledge.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public partial class MageKnowledge
    {
        // ── Blight ────────────────────────────────────────────────────────────

        /// <summary>
        /// Called by AgingSystem when the player would die at 100.
        /// Queues the blight-or-death inquiry for the next map-layer flush.
        /// </summary>
        public static void QueueAshenPrompt(Action onResolved)
        {
            _deferredInquiry = () => ShowAshenPrompt(onResolved);
        }

        private static void ShowAshenPrompt(Action onResolved)
        {
            InformationManager.ShowInquiry(new InquiryData(
                "The Last Ember",
                "A century of years. The fire should have consumed you by now — but it has not gone out. Something darker waits at the edge of the ash.\n\n" +
                "You can let go. The fire will burn clean, and it will end.\n\n" +
                "Or you can take the cold that remains. You will not die. But what burns in you afterward will not be warm.",
                true, true,
                "Take the cold", "Let it end",
                () =>
                {
                    onResolved?.Invoke();
                    _isAshen = true;
                    ApplyAshenAppearance(Hero.MainHero);
                    // Apply crime rating to old kingdom before leaving it
                    try
                    {
                        if (Hero.MainHero?.Clan?.Kingdom is TaleWorlds.CampaignSystem.Kingdom oldK)
                            TaleWorlds.CampaignSystem.Actions.ChangeCrimeRatingAction.Apply(oldK, 50f, true);
                    }
                    catch { }
                    // Leave old kingdom and join the Ashen
                    try { AshenCitySystem.OnPlayerBecameAshen(); } catch { }
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The fire dies. Something colder and older takes its place. The world will see it in your eyes.",
                        new Color(0.3f, 0.35f, 0.7f)));
                },
                () =>
                {
                    onResolved?.Invoke();
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The fire burns clean at last.",
                        new Color(0.8f, 0.6f, 0.3f)));
                    try { TaleWorlds.CampaignSystem.Actions.KillCharacterAction.ApplyByOldAge(Hero.MainHero, true); } catch { }
                }
            ), true, true);
        }

        // ── Possession event (Ashen 2nd+ cast per day) ───────────────────────
        // Two-strike rule: the first failed test does not kill — the cold gains
        // ground (wounded, morale loss, 21 days of strain). A second failure
        // while strained is death. One bad roll should hurt, not end a campaign.
        private static int _possessionStrainDays = 0;
        public static bool IsPossessionStrained => _possessionStrainDays > 0;

        public static void QueuePossessionEvent()
        {
            _deferredInquiry = ShowPossessionEvent;
        }

        private static void OnPossessionTestFailed(string failText)
        {
            if (_possessionStrainDays > 0)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    failText + " There was nothing left to hold it back. The cold claims you.",
                    new Color(0.3f, 0.35f, 0.7f)));
                try { TaleWorlds.CampaignSystem.Actions.KillCharacterAction.ApplyByOldAge(Hero.MainHero, true); } catch { }
                return;
            }
            _possessionStrainDays = 21;
            try { Hero.MainHero.HitPoints = Math.Min(Hero.MainHero.HitPoints, 5); } catch { }
            try { MobileParty.MainParty.RecentEventsMorale -= 20f; } catch { }
            InformationManager.DisplayMessage(new InformationMessage(
                failText + " You wake face-down in the ash, body broken, the cold a half-step closer. " +
                "If it turns on you again before your strength returns (21 days), it will not let go.",
                new Color(0.3f, 0.35f, 0.7f)));
        }

        private static void ShowPossessionEvent()
        {
            int lSkill = 0, aSkill = 0;
            try { lSkill = Hero.MainHero?.GetSkillValue(DefaultSkills.Leadership) ?? 0; } catch { }
            try { aSkill = Hero.MainHero?.GetSkillValue(DefaultSkills.Athletics) ?? 0; } catch { }
            int lPct = Math.Min(90, (int)(lSkill * 0.3f));
            int aPct = Math.Min(90, (int)(aSkill * 0.3f));
            float lChance = Math.Min(0.9f, lSkill * 0.003f);
            float aChance = Math.Min(0.9f, aSkill * 0.003f);

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "The Flame Turns",
                "Dark instincts and cold flame flood your body. The ash stirs something ancient — it recognises itself in you, and it is not yet satisfied.\n\nFor a terrible moment you cannot tell whether you are resisting the cold or whether what fights back is still you.",
                new List<InquiryElement>
                {
                    new InquiryElement("surrender", "Surrender to it.", null, true,
                        "Let the cold take what it wants. It will not need much more."),
                    new InquiryElement("leader", "Focus your will — fight it from within.", null, true,
                        $"Leadership test. Skill: {lSkill}. Success chance: {lPct}%." +
                        (IsPossessionStrained ? " You are still strained — failure now is death." : " Failure leaves you broken and strained, not dead.")),
                    new InquiryElement("athlete", "Drive it back — overwhelm it with your body.", null, true,
                        $"Athletics test. Skill: {aSkill}. Success chance: {aPct}%." +
                        (IsPossessionStrained ? " You are still strained — failure now is death." : " Failure leaves you broken and strained, not dead.")),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    string choice = chosen?[0]?.Identifier as string;
                    if (choice == "surrender")
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            "You let go. The cold is grateful.", new Color(0.3f, 0.35f, 0.7f)));
                        try { TaleWorlds.CampaignSystem.Actions.KillCharacterAction.ApplyByOldAge(Hero.MainHero, true); } catch { }
                    }
                    else if (choice == "leader")
                    {
                        if (_rng.NextDouble() < lChance)
                            InformationManager.DisplayMessage(new InformationMessage(
                                "Your will holds. The cold retreats — for now.", new Color(0.7f, 0.7f, 0.9f)));
                        else
                            OnPossessionTestFailed("Your will breaks.");
                    }
                    else if (choice == "athlete")
                    {
                        if (_rng.NextDouble() < aChance)
                            InformationManager.DisplayMessage(new InformationMessage(
                                "You push it back. The cold recoils.", new Color(0.7f, 0.7f, 0.9f)));
                        else
                            OnPossessionTestFailed("Your body gives out.");
                    }
                },
                null, "", false
            ), false, true);
        }

        // ── The Cold Calls Your Name ──────────────────────────────────────────
        // Fires when WhisperCount reaches 100. After Resist or Bargain, whispers
        // drop and the event can fire again once they climb back to 100.
        private static void ShowColdCallsEvent()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "The Cold Calls Your Name",
                "Three pale figures stand at the crossroads. They wear no faces you recognise — but they know yours. " +
                "The ash in your blood has been speaking to them for a long time, and tonight they have come to collect.\n\n" +
                "You can feel the fire straining against them. It always has. But it has never strained this hard.",
                new List<InquiryElement>
                {
                    new InquiryElement("resist", "I will not hear it. Not tonight. Not ever.", null, true,
                        "Resist. −10 days. −30 whispers. They will return."),
                    new InquiryElement("bargain", "Hear them out. Give what they ask and walk away.", null, true,
                        "Bargain. −30 days. −60 whispers. They withdraw, satisfied — for now."),
                    new InquiryElement("accept", "The fire in me has always been theirs.", null, true,
                        "Accept the cold. Become Ashen."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    string choice = chosen?[0]?.Identifier as string ?? "resist";
                    if (choice == "resist")
                    {
                        AgingSystem.AgeHero(Hero.MainHero, 10);
                        RemoveWhispers(30);
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The figures recede into the dark. The fire holds — barely. They will return. −10 days, −30 whispers.",
                            new Color(0.7f, 0.6f, 0.8f)));
                    }
                    else if (choice == "bargain")
                    {
                        AgingSystem.AgeHero(Hero.MainHero, 30);
                        RemoveWhispers(60);
                        InformationManager.DisplayMessage(new InformationMessage(
                            "They take what they came for and step aside. The road ahead is clear — for now. −30 days, −60 whispers.",
                            new Color(0.5f, 0.4f, 0.6f)));
                    }
                    else
                    {
                        // Become Ashen
                        _isAshen = true;
                        _whisperCount = 0;
                        _coldCallCountdown = 0;
                        ApplyAshenAppearance(Hero.MainHero);
                        try { AshenCitySystem.OnPlayerBecameAshen(); } catch { }
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The fire goes out. Something older and colder fills the space where it was.",
                            new Color(0.3f, 0.35f, 0.7f)));
                    }
                },
                null, "", false
            ), false, true);
        }

    }
}
