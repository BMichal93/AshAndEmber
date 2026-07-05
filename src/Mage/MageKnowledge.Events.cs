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
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    // Leave old kingdom and join the Ashen
                    try { AshenCitySystem.OnPlayerBecameAshen(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                    try { TaleWorlds.CampaignSystem.Actions.KillCharacterAction.ApplyByOldAge(Hero.MainHero, true); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                try { TaleWorlds.CampaignSystem.Actions.KillCharacterAction.ApplyByOldAge(Hero.MainHero, true); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                return;
            }
            _possessionStrainDays = 21;
            try { Hero.MainHero.HitPoints = Math.Min(Hero.MainHero.HitPoints, 5); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { MobileParty.MainParty.RecentEventsMorale -= 20f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            InformationManager.DisplayMessage(new InformationMessage(
                failText + " You wake face-down in the ash, body broken, the cold a half-step closer. " +
                "If it turns on you again before your strength returns (21 days), it will not let go.",
                new Color(0.3f, 0.35f, 0.7f)));
        }

        private static void ShowPossessionEvent()
        {
            int lSkill = 0, aSkill = 0;
            try { lSkill = Hero.MainHero?.GetSkillValue(DefaultSkills.Leadership) ?? 0; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { aSkill = Hero.MainHero?.GetSkillValue(DefaultSkills.Athletics) ?? 0; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                        try { TaleWorlds.CampaignSystem.Actions.KillCharacterAction.ApplyByOldAge(Hero.MainHero, true); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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

        // ── Known Mage ────────────────────────────────────────────────────────
        private static void ShowKnownMageEvent()
        {
            InformationManager.ShowInquiry(new InquiryData(
                "A Name That Travels",
                "It reaches you in pieces, later: a soldier who watched too carefully, a merchant who remembered your hands, " +
                "a lord who asked the wrong questions of the wrong people after the battle.\n\n" +
                "They know what you are. Not all of them. Not loudly. But the word has left your control.\n\n" +
                "How lords and strangers treat you will not be the same as before. Some will want what you carry. " +
                "Others will want you gone. A few will be afraid.",
                true, false, "So be it.", null, () => { }, null), true, false);
        }

        // ── Dreams ────────────────────────────────────────────────────────────
        private static readonly string[] _dreamTexts =
        {
            // 1. Hands burning gently
            "Your hands are burning — not painfully, but with the patient insistence of something that has been waiting. " +
            "The fire is not angry. It is looking for a way out that isn't destruction. You watch it move under your skin like a slow tide. " +
            "You wake certain of something you cannot name, and the certainty stays with you through the morning.",

            // 2. Woman across an uncrossable river
            "There is a river you cannot cross. On the far bank a woman stands watching you — not waiting, simply watching, " +
            "with the kind of attention that expects nothing in return. Her eyes are warm in a way the living rarely manage. " +
            "You raise your hand. She does not raise hers. But something passes between you across the water, " +
            "and you wake with the feeling that you have been seen clearly, perhaps for the first time.",

            // 3. Road ending in darkness, older self
            "The road ends in dark you cannot see through. You stop at the edge. " +
            "On the far side of it, your older self stands smiling — not the smile of someone who has won, " +
            "but of someone who arrived somewhere and found it was worth the walk. " +
            "They do not speak. They do not need to. You stand there looking at each other across the dark until you wake.",

            // 4. Teaching a child, child becomes frightened old man
            "You are teaching a child how to make fire from nothing — showing them the gesture, the stillness before the flame. " +
            "They watch with the absolute attention children bring to things that matter. " +
            "When you look up from your hands, the child is old. Frightened, the way old men are frightened — " +
            "not of dying, but of having waited too long. You do not know if they are frightened of you " +
            "or of what they became while you were looking away.",

            // 5. Walking a battlefield, the dead watching
            "The battlefield is quiet in the way only battlefields ever are. You walk it after. " +
            "The dead watch you pass — not accusingly, just watching, the way the dead are said to watch those who can still burn. " +
            "One of them reaches out. You take their hand. The fire passes between you, not much, " +
            "but some — and their expression changes from watching to something close to gratitude. " +
            "You wake with the weight of that hand still in yours.",

            // 6. Frozen lake, wrong name through the ice
            "A frozen lake, perfectly still. Someone is under the ice — you can see their hands pressed flat against it from below, " +
            "their mouth moving. You kneel. The ice is very clear. They are speaking your name — " +
            "but the name they call is not yours. It is not anyone's you know. " +
            "You stay there, kneeling, while they call and call the wrong name, until the cold wakes you.",

            // 7. The cold walking beside you
            "The cold walks beside you on the road, carrying something wrapped in cloth. " +
            "You do not look at it directly. You walk together for a long time without speaking. " +
            "Eventually the cold sets the bundle down beside the road and continues walking without it. " +
            "You stop. You look at what it left. You do not pick it up. " +
            "You don't know, when you wake, whether that was wisdom or cowardice.",

            // 8. Fire dying to a coal
            "A fire dying slowly — not going out, just settling into itself, " +
            "the wood becoming coal becoming something smaller and more essential than fire. " +
            "You watch it for a long time. Near the end, when there is only the one coal left, " +
            "it looks back at you. Not with eyes. With the particular attention of things that have burned " +
            "for a long time and know what it costs.",

            // 9. Old mage across a fire
            "An old mage across a fire from you, somewhere without walls. They look the way " +
            "people look when they have paid a great deal and do not regret it. " +
            "\"The cost was this,\" they say. \"I knew it. I paid anyway.\" " +
            "You ask what the cost was. They look at you with something like patience. " +
            "\"That you had to ask,\" they say. The fire settles between you. You wake not sure which of you they were speaking to.",

            // 10. The fire is very small in your palm
            "The fire is very small in your palm — smaller than you have ever held it, " +
            "small enough to cup your hands around and breathe on. It does not ask for more. " +
            "It simply trusts you to carry it. You walk carefully through the dream, hands cupped, " +
            "protecting something that has never needed protecting before. " +
            "You wake with the feeling that something is very close. Not threatening. Just close.",

            // 11. The cold speaks clearly
            "The cold speaks to you clearly, the way it rarely does — without the usual indirection, " +
            "without the cold distance it usually keeps. \"There is not much left,\" it says. " +
            "\"Use it well.\" You do not know if it means the fire, or the time, or something else entirely. " +
            "It does not sound like a warning. It does not sound like an offer. " +
            "It sounds like both at once, the way the truth sometimes does.",

            // 12. Younger self frightened of what you've become
            "Your younger self stands across from you, frightened — not of you, but of what you show them " +
            "you will become. You understand. You were frightened too, before you understood " +
            "what the fire was asking. You show them, carefully, what the fire does when you trust it: " +
            "a small warmth, nothing dangerous, nothing cold. They watch. " +
            "Their fear doesn't leave all at once, but it changes shape into something more like wonder. " +
            "You wake not afraid.",
        };

        private static void ShowDreamEvent()
        {
            try
            {
                int idx;
                do { idx = _rng.Next(_dreamTexts.Length); } while (idx == _dreamLastIdx && _dreamTexts.Length > 1);
                _dreamLastIdx = idx;

                InformationManager.ShowInquiry(new InquiryData(
                    "A Dream", _dreamTexts[idx],
                    true, false, "I wake.", null, () => { }, null), true, false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                        try { AshenCitySystem.OnPlayerBecameAshen(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
