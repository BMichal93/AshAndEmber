// =============================================================================
// ASH AND EMBER — DragonQuestSystem.Quest.cs
// First contact, second contact, visions (7), final three-way choice.
// Partial of DragonQuestSystem (shared state lives in DragonQuestSystem.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public static partial class DragonQuestSystem
    {
        // ── First contact (after 1st Ashen lord kill) ─────────────────────────
        private static void ShowFirstContact()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "A Presence in the Ash",

                    "The battle is not yet cold.\n\n" +
                    "You are standing among the fallen when something shifts — " +
                    "not the wind, not the smoke, not the sound of the dying. " +
                    "Something underneath all of it. A pull. A sensation like hearing a voice " +
                    "from across a great distance, in a language you have never been taught " +
                    "but feel you should know.\n\n" +
                    "Your fire-sense recoils. If you have no fire — something recoils anyway.\n\n" +
                    "It is not hostile. It is desperate. The way something is desperate when it has " +
                    "been waiting a very long time and cannot be certain it will not lose its chance again.\n\n" +
                    "It does not speak in words yet. Only images: fire spreading across a dark plain. " +
                    "Strange robed figures whose faces are too bright to read. " +
                    "Snow falling in summer. A crown dissolving into embers that drift upward " +
                    "instead of down.\n\n" +
                    "Then silence. And the sense that something is listening for your answer.",

                    true, true,
                    "What are you?",
                    "I want no part of this.",

                    () =>
                    {
                        _phase = PhaseActive;
                        _visionPhase = 0;
                        try { _questLog = new DragonQuestLog(); _questLog.StartQuest(); _questLog.LogStarted(); } catch { }
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Quest added: The Sundered Crown.",
                            new Color(0.80f, 0.55f, 0.25f)));
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"Objectives: silence {TargetLordsSlain} Ashen lords  ·  " +
                            "clear 3 predestined ruins  ·  capture the Heart of Winter (Tyal).",
                            new Color(0.70f, 0.50f, 0.25f)));
                        // Queue the first vision immediately as the opening dream
                        if (MageKnowledge._deferredInquiry == null)
                        {
                            int idx = _visionPhase;
                            _visionPhase++;
                            MageKnowledge._deferredInquiry = () => ShowVision(idx);
                        }
                    },
                    () =>
                    {
                        _phase = PhaseFirstRefused;
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The presence withdraws. But it does not leave.",
                            new Color(0.55f, 0.50f, 0.60f)));
                    }
                ), true, true);
            }
            catch { }
        }

        // ── Second contact (after 2nd Ashen kill, having refused once) ─────────
        private static void ShowSecondContact()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Again",

                    "You have killed another Ashen lord, and the presence returns before the blood dries.\n\n" +
                    "Stronger this time. Less patient. The images are the same — fire, snow, " +
                    "strange faces, a crown coming apart — but there is something underneath them " +
                    "now. Urgency. The particular urgency of something that has been trying to " +
                    "reach you for a very long time and has run out of the luxury of being subtle.\n\n" +
                    "A word reaches you. Just one. Broken, as if it has travelled a great distance " +
                    "through stone and cold:\n\n" +
                    "\"...wait...\"\n\n" +
                    "And then: \"...please...\"\n\n" +
                    "The cold ember in the lord you just killed — the thing that passed to you at " +
                    "the moment of victory — flickers. You did not know something in you could flicker.\n\n" +
                    "The presence is still there. Still listening.",

                    true, true,
                    "All right. Show me.",
                    "No. Whatever you are, I am not yours.",

                    () =>
                    {
                        _phase = PhaseActive;
                        _visionPhase = 0;
                        try { _questLog = new DragonQuestLog(); _questLog.StartQuest(); _questLog.LogStarted(); } catch { }
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Quest added: The Sundered Crown.",
                            new Color(0.80f, 0.55f, 0.25f)));
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"Objectives: silence {TargetLordsSlain} Ashen lords  ·  " +
                            "clear 3 predestined ruins  ·  capture the Heart of Winter (Tyal).",
                            new Color(0.70f, 0.50f, 0.25f)));
                        if (MageKnowledge._deferredInquiry == null)
                        {
                            int idx = _visionPhase;
                            _visionPhase++;
                            MageKnowledge._deferredInquiry = () => ShowVision(idx);
                        }
                    },
                    () =>
                    {
                        _phase = PhasePermanentlyClosed;
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The presence recedes — not in anger, but with the quiet of something " +
                            "that has run out of time. It will not come again.",
                            new Color(0.45f, 0.45f, 0.55f)));
                    }
                ), true, true);
            }
            catch { }
        }

        // ── Visions (0–6, one per Ashen lord killed after accepting) ──────────
        internal static void ShowVision(int idx)
        {
            try
            {
                switch (idx)
                {
                    case 0: ShowVision_TheDreamingFire(); break;
                    case 1: ShowVision_TheShapeOfWhatWas(); break;
                    case 2: ShowVision_VoiceInTheCold(); break;
                    case 3: ShowVision_FigureBeforeTheGrey(); break;
                    case 4: ShowVision_EmperорsFace(); break;
                    case 5: ShowVision_TruthOfTheCovenant(); break;
                    case 6: ShowVision_TheHour(); break;
                    default: break;
                }
            }
            catch { }
        }

        // Vision 0 — The Dreaming Fire (shown right after accepting)
        private static void ShowVision_TheDreamingFire()
        {
            InformationManager.ShowInquiry(new InquiryData(
                "The Dreaming Fire",

                "A fire that does not burn hot.\n\n" +
                "It spreads across a continent you do not recognise — borders drawn in flame, " +
                "held in place by something you cannot name. Figures moving underneath it. " +
                "Not burning. Being guided.\n\n" +
                "Between the flames: snow. Clean and cold. And a figure standing at the place " +
                "where fire and snow meet — robed, crowned, utterly still. " +
                "Watching both with the expression of someone who has held something " +
                "heavy for a very long time and is not yet ready to set it down.\n\n" +
                "Then the image dissolves.\n\n" +
                "You wake to your campfire burning lower than it should. " +
                "You are not afraid. You are not sure you should be.",

                true, false,
                "I remember it.",
                "",
                () => { },
                () => { }
            ), true, true);
        }

        // Vision 1 — The Shape of What Was (after 2nd lord kill)
        private static void ShowVision_TheShapeOfWhatWas()
        {
            InformationManager.ShowInquiry(new InquiryData(
                "The Shape of What Was",

                "Towers of stone. A city of impossible scale. Maps that move like water.\n\n" +
                "And at the centre — a throne. Not empty. The figure sitting on it is turned away. " +
                "You cannot see the face. You see only the posture: the posture of someone " +
                "who has made a decision they cannot unmake, and is living in the space " +
                "that comes after.\n\n" +
                "Fires along the walls. Burning steady, burning deliberate — " +
                "arranged to hold something specific in a specific place.\n\n" +
                "A word reaches you: \"...remember...\"\n\n" +
                "Then the city is gone. You are in your camp. The word stays.",

                true, false,
                "I remember it.",
                "",
                () => { },
                () => { }
            ), true, true);
        }

        // Vision 2 — The Voice in the Cold (after 3rd lord kill)
        private static void ShowVision_VoiceInTheCold()
        {
            InformationManager.ShowInquiry(new InquiryData(
                "The Voice in the Cold",

                "Snow now. Just snow, and a voice.\n\n" +
                "Broken. Scattered. As if it has been waiting for a shape for a very long time " +
                "and keeps losing the one it finds.\n\n" +
                "\"...hold... the cycle... must not... the grey is... I...\"\n\n" +
                "A pause. Then, quieter:\n\n" +
                "\"...still here.\"\n\n" +
                "Nothing else. When you open your eyes, your breath makes fog " +
                "even though it is not cold enough for that.",

                true, false,
                "I remember it.",
                "",
                () =>
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The voice in the ash is growing. Each lord silenced, it finds a clearer shape.",
                        new Color(0.70f, 0.50f, 0.35f)));
                },
                () => { }
            ), true, true);
        }

        // Vision 3 — The Figure Before the Grey (after 4th lord kill)
        private static void ShowVision_FigureBeforeTheGrey()
        {
            InformationManager.ShowInquiry(new InquiryData(
                "The Figure Before the Grey",

                "A burning figure. Not burning in agony — burning the way a torch burns. " +
                "Deliberately. Controlled.\n\n" +
                "Standing at the edge of a grey tide so vast it erases the horizon. " +
                "The tide is not advancing. The burning figure is what is holding it.\n\n" +
                "The voice returns. Clearer now — still broken, still reaching across " +
                "something enormous, but closer:\n\n" +
                "\"You who walk in my fire —\"\n\n" +
                "A pause, like the distance closing.\n\n" +
                "\"I remember you. I have been waiting a very long time to remember you.\"",

                true, true,
                "What are you holding back?",
                "I understand.",

                () =>
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The voice strains across the distance: \"...everything. All of it. Since before your name existed.\"",
                        new Color(0.75f, 0.50f, 0.30f)));
                },
                () => { }
            ), true, true);
        }

        // Vision 4 — The Emperor's Face (after 5th lord kill)
        private static void ShowVision_EmperорsFace()
        {
            InformationManager.ShowInquiry(new InquiryData(
                "The Emperor's Face",

                "You can see the face now.\n\n" +
                "Ancient. Burned not by fire but by time and decision. Eyes that have looked " +
                "at things from a very great height for a very long time. " +
                "A crown — the same one you have seen dissolving in the other visions — " +
                "still whole here. Still meaning something.\n\n" +
                "\"I am Aelisar.\"\n\n" +
                "Clear. The clearest thing you have ever heard, and also the quietest.\n\n" +
                "\"First of the Binders. Builder of the covenant that held. " +
                "I chose to shatter myself into the cycle rather than let the cycle shatter everything else. " +
                "I thought it would hold forever. I was wrong about the 'forever' part.\"\n\n" +
                "A pause. The face does not change.\n\n" +
                "\"You are collecting what remains of me. Each Ashen lord you silence — " +
                "a shard returns. I did not know this was possible. Neither did they.\"",

                true, true,
                "Did they know what they were carrying?",
                "How long have you been waiting?",

                () =>
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "\"No. The cold hides it from them. They are my lock — they do not know they are a lock.\"",
                        new Color(0.75f, 0.50f, 0.30f)));
                },
                () =>
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "\"Long enough that I have lost count of the generations that passed while I searched for you.\"",
                        new Color(0.70f, 0.55f, 0.35f)));
                }
            ), true, true);
        }

        // Vision 5 — The Truth of the Covenant (after 6th lord kill)
        private static void ShowVision_TruthOfTheCovenant()
        {
            InformationManager.ShowInquiry(new InquiryData(
                "The Truth of the Covenant",

                "Aelisar again. More present now. More whole.\n\n" +
                "\"The ruins hold my pact — the places where I made the original bargain, " +
                "where I gave myself to the fire in exchange for the cycle holding. " +
                "Three sites. Old. Scarred. They will remember you when you come.\n\n" +
                "\"The Heart of Winter holds my purpose. The thing I fought hardest to protect — " +
                "what I could not let the cold have. Go to Tyal. " +
                "Stand where I stood. You will understand.\n\n" +
                "\"When you have all of it — come back to me. " +
                "We will have choices to make. Both of us.\"",

                true, true,
                "What will we choose between?",
                "I will find the ruins. I will take Tyal.",

                () =>
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "\"What to do with the remainder of a man who was once an empire. " +
                        "What to do with you. Both questions have the same answer — but you must be the one to name it.\"",
                        new Color(0.75f, 0.50f, 0.30f)));
                },
                () =>
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "\"Good. Do not hurry. Whatever you find there — let it settle before you decide anything.\"",
                        new Color(0.70f, 0.55f, 0.35f)));
                }
            ), true, true);
        }

        // Vision 6 — The Hour (after 7th lord / all objectives met)
        private static void ShowVision_TheHour()
        {
            InformationManager.ShowInquiry(new InquiryData(
                "The Hour",

                "This is not a vision.\n\n" +
                "Aelisar is simply there — a presence in the fire that does not flicker, " +
                "a voice in the ash that does not waver. He has been scattered across seven lives " +
                "and three sacred places and the cold heart of winter, and you have gathered him, " +
                "piece by piece, into whatever you are.\n\n" +
                "He does not speak immediately.\n\n" +
                "When he does:\n\n" +
                "\"Thank you. I mean that. I have not meant anything in a very long time.\n\n" +
                "\"Now — before the choice — you should know what the choices are. " +
                "What they will cost. What they will give. " +
                "No more partial truths. You have earned the whole of it.\n\n" +
                "\"I will tell you everything. Then you will decide. " +
                "And whatever you decide — I will accept.\"",

                true, false,
                "Then tell me.",
                "",
                () =>
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The Sundered Crown — all conditions are met. Aelisar awaits your answer.",
                        new Color(0.90f, 0.65f, 0.20f)));
                },
                () => { }
            ), true, true);
        }

        // ── Final three-way choice ─────────────────────────────────────────────
        private static void ShowFinalPrompt()
        {
            try
            {
                string body =
                    "He tells you everything.\n\n" +
                    "The cycle is not natural. It is a mechanism — built, refined, maintained across " +
                    "centuries by those who understood what the grey tide was. The Ashen do not march " +
                    "because they hunger. They march because something in the world calls them forward, " +
                    "the way water is called downhill.\n\n" +
                    "Aelisar built the call into himself. He became the mechanism. He scattered his soul " +
                    "into the Ashen lords as seeds of what he was, hoping the warmth in each fragment " +
                    "would slow the cold from within. It did not work the way he intended.\n\n" +
                    "\"What it did do,\" he says quietly, \"was leave pieces of me everywhere. " +
                    "Including in you, now.\"\n\n" +
                    "He has three paths. He knows what each costs.\n\n" +
                    "[Cast Him Out] — Aelisar's fragments dissolve. The cycle continues, unchanged. " +
                    "You live your life. The grey comes again, eventually.\n\n" +
                    "[Become the Vessel] — You accept him. His knowledge, his fire, his covenant against aging. " +
                    "\"But I am ancient. What makes you you — the edges of it — would blur. " +
                    "You would still be yourself. But you would also be the thing I was. That does not go away.\"\n\n" +
                    "[The Last Binding] — You spend him. All of him — everything gathered — " +
                    "to shatter the mechanism at its root. The cycle breaks. The grey does not return. " +
                    "\"I am destroyed in the spending. And you give your fire as part of the fuel. " +
                    "Both of us. It is permanent. It is complete. It costs everything.\"\n\n" +
                    "He looks at you with eyes that have seen the grey tide recede and return across more " +
                    "generations than your family's memory extends.\n\n" +
                    "\"Whichever you choose — I am grateful you came. " +
                    "I had forgotten what it felt like to be found.\"";

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "The Sundered Crown",
                    body,
                    new List<InquiryElement>
                    {
                        new InquiryElement("banish",
                            "Cast him out — let the cycle continue without me.",
                            null, true,
                            "Aelisar's fragments dissolve. The campaign continues as normal."),
                        new InquiryElement("vessel",
                            "Become the Vessel — I carry what he was.",
                            null, true,
                            "Gain fire magic and Aelisar's covenant against aging. " +
                            "Your identity blurs with his — permanently."),
                        new InquiryElement("sacrifice",
                            "The Last Binding — we end it.",
                            null, true,
                            "Spend Aelisar and your fire to break the Ashen cycle forever. " +
                            "Your hero dies. The Ashen are broken. This ends your campaign."),
                    },
                    true, 1, 1,
                    "Decide.",
                    "",
                    chosen =>
                    {
                        try { HandleFinalChoice(chosen?[0]?.Identifier as string ?? "banish"); } catch { }
                    },
                    _ => { },
                    "", false
                ), false);
            }
            catch { }
        }

        private static void HandleFinalChoice(string choice)
        {
            switch (choice)
            {
                case "vessel":
                    _phase = PhaseEndedMerge;
                    try { _questLog?.LogComplete("You accepted Aelisar. His fire is yours now — so is the weight of what he was."); } catch { }
                    ShowMergeEnding();
                    break;

                case "sacrifice":
                    _phase       = PhaseEndedSacrifice;
                    _endingPhase = 1;
                    _worldBound  = false; // world effects applied gradually in TickEnding
                    try { _questLog?.LogComplete("The Last Binding fires. The grey retreats. It costs everything."); } catch { }
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The Last Binding begins. There is no taking it back.",
                        new Color(0.90f, 0.65f, 0.20f)));
                    break;

                default: // banish
                    _phase = PhaseEndedBanish;
                    try { _questLog?.LogComplete("You cast him out. The cycle continues, as it always has."); } catch { }
                    ShowBanishEnding();
                    break;
            }
        }
    }
}
