// =============================================================================
// ASH AND EMBER — SettlementEncounters.Events6.cs
// Five new multi-phase, consequence-heavy settlement encounters.
// Partial of SettlementEncounters (shared state lives in SettlementEncounters.cs).
//
// ┌────────────────────────────────┬──────────────────────┬──────────────────┐
// │ Event                          │ Trigger              │ Gate             │
// ├────────────────────────────────┼──────────────────────┼──────────────────┤
// │ The Cartographer of Silences   │ Enter city           │ Mage or Ashen    │
// │ The Child Who Does Not Sleep   │ Enter village        │ General (1 arc)  │
// │ The Vow Undischarged           │ Leave city           │ General, tier ≥2 │
// │ The Fever Road                 │ Leave village        │ General          │
// │ Ashes in the Bread             │ Enter village        │ General          │
// └────────────────────────────────┴──────────────────────┴──────────────────┘
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static partial class SettlementEncounters
    {
        // ═══════════════════════════════════════════════════════════════════
        // 1. THE CARTOGRAPHER OF SILENCES
        //    City enter — mage or Ashen — escalates across three sightings.
        //    Phase 0 → 1 → 2 → 3 (done). 60-day cooldown between sightings.
        //    Phase 3 reached without stopping it: Ashen raid fires 14 days later.
        // ═══════════════════════════════════════════════════════════════════

        private static void EV_CartographerOfSilences(Settlement s)
        {
            _cartographerCooldown = 60;
            switch (_cartographerPhase)
            {
                case 0: EV_Cartographer_Phase1(s); break;
                case 1: EV_Cartographer_Phase2(s); break;
                case 2: EV_Cartographer_Phase3(s); break;
            }
        }

        private static void EV_Cartographer_Phase1(Settlement s)
        {
            bool isAshen = MageKnowledge.IsAshen;
            string scoutHint = SkillHint(DefaultSkills.Scouting, 0.30f, "Read what he is marking before he notices you watching");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Cartographer of Silences",
                "A hooded figure in the market square makes chalk marks on cobblestones at precise intervals — " +
                "thresholds, doorways, the base of a pillar. He is not measuring. The marks are too specific and too " +
                "scattered for measurement. They describe something the square has inside it that only certain eyes can see.",
                new List<InquiryElement>
                {
                    new InquiryElement("watch",    "Watch quietly. Read what he is marking.", null, true, scoutHint),
                    new InquiryElement("confront", "Step toward him. Make yourself known.",   null, true,
                        "He will be gone before you reach him. He will know you saw."),
                    new InquiryElement("report",   "Report him to the city watch.",           null, true,
                        "The watch will take your coin. The cartographer will not be found."),
                    new InquiryElement("ashen",    "Acknowledge him as you pass.",            null, isAshen,
                        "The grey acknowledgement. Cold currency."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "watch":
                            if (SkillRoll(DefaultSkills.Scouting, 0.30f))
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                Msg("You read the pattern without moving toward it. The marks describe resonance points — " +
                                    "places where the cold gathers like water in a low place. He is cataloguing. " +
                                    "He has not finished. He does not know you have the knowledge.", AshenColor);
                            }
                            else
                                Msg("You watch but the angle is wrong — you can see marks exist without reading their pattern. " +
                                    "He adjusts slightly as you linger. What he resumes after you look away is a corrected version.", DimColor);
                            break;
                        case "confront":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You step toward him. He is gone — not hurried, simply gone, the way the Ashen go when they choose " +
                                "not to be found. The marks remain on the cobblestones for three days before rain removes them. " +
                                "You were not fast enough to read all of them. He knows you saw him. That will change what he does next.", DimColor);
                            break;
                        case "report":
                            Msg("The watch takes your description and your coin. The cartographer is reported and not found. " +
                                "Two weeks later a city clerk files a note that the marks were probably a surveyor's. " +
                                "The watch believes this.", DimColor);
                            break;
                        case "ashen":
                            if (isAshen)
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                Msg("You make the grey acknowledgement as you pass. He notices, continues his work, and as you " +
                                    "leave the square something small and cold is placed in your saddlebag. Inside: coordinates " +
                                    "of a cache, and three words in a notation you understand as 'the work continues.'", AshenColor);
                                ChangeGold(400);
                            }
                            break;
                    }
                    _cartographerPhase = 1;
                }, null, "", false), false, true);
        }

        private static void EV_Cartographer_Phase2(Settlement s)
        {
            bool isAshen = MageKnowledge.IsAshen;
            string scoutHint = SkillHint(DefaultSkills.Scouting, 0.25f, "Intercept the apprentice's notes");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Cartographer Returns",
                "The same hooded figure — or one wearing the same cloak with the same patience — has returned to the square " +
                "and resumed. He brought an apprentice this time. The marks cover more of the square than before, and some " +
                "of the older chalk lines have been refreshed. He has been back more than once.",
                new List<InquiryElement>
                {
                    new InquiryElement("watch",  "Watch. Read what they are building.", null, true, scoutHint),
                    new InquiryElement("confront","Intercept them both.", null, true,
                        "They will split. The apprentice is the slower one."),
                    new InquiryElement("follow", "Follow the apprentice when they separate.", null, true,
                        "Wait for them to split, then follow the apprentice."),
                    new InquiryElement("ashen",  "Acknowledge the cartographer.", null, isAshen,
                        "He has more to pass than last time."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "watch":
                            if (SkillRoll(DefaultSkills.Scouting, 0.25f))
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                ChangeRenown(5f);
                                Msg("You watch long enough to see the apprentice make an error and the cartographer correct it " +
                                    "without looking. They are preparing something specific: the north gate, the well, and a " +
                                    "building that backs onto the grain market. Whatever the ritual requires, those three " +
                                    "points are its corners. You carry this.", AshenColor);
                            }
                            else
                                Msg("You watch but the apprentice notices your attention and repositions to block your sightline. " +
                                    "The corrections the cartographer makes are small enough that you cannot parse their meaning " +
                                    "from this angle. You know the work is more complete than before.", DimColor);
                            break;
                        case "confront":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            if (_rng.NextDouble() < 0.5)
                            {
                                ChangeRenown(5f);
                                Msg("You move fast enough to cut off the apprentice before they separate. He drops a satchel and runs. " +
                                    "Inside: partial notes in a cold notation. You have a fragment of the map and the knowledge " +
                                    "that the cartographer will need to begin that section again.", AshenColor);
                            }
                            else
                                Msg("You move and they split immediately — both gone into the crowd with the practiced efficiency of " +
                                    "people who have rehearsed this. The square empties around you. You have the marks, the " +
                                    "pattern, and no one to question about it.", DimColor);
                            break;
                        case "follow":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            if (SkillRoll(DefaultSkills.Scouting, 0.30f))
                            {
                                ChangeRenown(8f);
                                Msg("You follow the apprentice for six blocks. He leads you — unknowingly — to a safehouse in the " +
                                    "tanner's quarter, where he knocks three times and enters. You note the address, the time, " +
                                    "the route. The next Ashen patrol through this city will find that house empty. Someone " +
                                    "who receives your message will make sure of it.", GoodColor);
                            }
                            else
                                Msg("You follow the apprentice and lose him in the market. He moved as though he expected a tail. " +
                                    "You have a general direction — east of the grain market — and nothing more specific.", DimColor);
                            break;
                        case "ashen":
                            if (isAshen)
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                ChangeGold(600);
                                Msg("The cartographer stops his work for a moment as you pass and speaks without turning. " +
                                    "He gives no date, only a place north of here and what you will be expected to bring " +
                                    "when word finally comes. The work in the square is part of a larger preparation " +
                                    "that you are now part of.", AshenColor);
                            }
                            break;
                    }
                    _cartographerPhase = 2;
                }, null, "", false), false, true);
        }

        private static void EV_Cartographer_Phase3(Settlement s)
        {
            bool isAshen = MageKnowledge.IsAshen;
            string scoutHint = SkillHint(DefaultSkills.Scouting, 0.25f, "Decode the completed map before it activates");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Final Cartography",
                "The square has been mapped. You can see it now: a web of cold notation, complete, oriented north. " +
                "The apprentice is gone. The cartographer works alone with the deliberate calm of a man who knows " +
                "the work is almost done. Whatever this was preparation for, the preparation is ending.",
                new List<InquiryElement>
                {
                    new InquiryElement("watch",   "Decode the map — learn what it activates.", null, true, scoutHint),
                    new InquiryElement("destroy", "Burn what he has drawn. End it here.",      null, true,
                        "An hour with fire. The marks are cold but they will yield. The cost is in your years."),
                    new InquiryElement("report",  "Report to a lord you trust. Now, while it can be stopped.", null, true,
                        "If the lord acts fast enough, the ritual can be interrupted."),
                    new InquiryElement("ashen",   "Let it complete. You want to see what it does.", null, isAshen,
                        "You will see exactly what it does. So will a village in your territory."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "watch":
                            if (SkillRoll(DefaultSkills.Scouting, 0.25f))
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                ChangeRenown(12f);
                                ChangeRelWithRandomLord(8);
                                Msg("You read the completed map. It describes an activation sequence running north-to-south, " +
                                    "converging on the city's oldest well. You carry the sequence to a lord whose garrison " +
                                    "reaches the point before the cartographer does. The ritual does not complete. " +
                                    "The city does not know what it was saved from. The cartographer is not found.", FireColor);
                                _cartographerPhase = 3; // clean — no consequence
                            }
                            else
                            {
                                Msg("You study the map but the notation is complete enough to be opaque — you see the structure " +
                                    "without the key to it. It describes a direction and a convergence and nothing more specific. " +
                                    "You carry the image without the meaning. The cartographer finishes and leaves.", DimColor);
                                _cartographerPhase = 3;
                                _cartographerConsequenceCountdown = 14;
                            }
                            break;
                        case "destroy":
                            AgePlayer(2);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You work at the marks with fire for an hour. The cold in them resists — not physically, but " +
                                "the fire finds something to push against. Eventually they yield, one by one. The pattern breaks. " +
                                "The square is just a square. The cartographer, somewhere, will feel the absence of two years' work. " +
                                "The cost is in your hands and in your years.", FireColor);
                            _cartographerPhase = 3; // clean — no consequence
                            break;
                        case "report":
                            ChangeRelWithRandomLord(10);
                            ChangeRenown(8f);
                            Msg("You name what you have seen to a lord whose forces reach the city within a day. They find the " +
                                "square, the marks, and — at the grain market, too late for the cartographer to stop them — an " +
                                "Ashen operative preparing the final stage. The ritual does not complete. Your name is in the " +
                                "garrison commander's report. The lord reads it.", GoodColor);
                            _cartographerPhase = 3; // clean — no consequence
                            break;
                        case "ashen":
                            if (isAshen)
                            {
                                ShiftTrait(DefaultTraits.Mercy, -1);
                                Msg("You stand aside and watch him finish. He rolls the map without looking at you and leaves by " +
                                    "the north gate. Three days from now something will happen in your territory and you will " +
                                    "know exactly what caused it and exactly what you chose.", AshenColor);
                                _cartographerPhase = 3;
                                _cartographerConsequenceCountdown = 3; // faster — you chose it
                            }
                            break;
                        default:
                            _cartographerPhase = 3;
                            _cartographerConsequenceCountdown = 14;
                            break;
                    }
                }, null, "", false), false, true);
        }

        private static void FireCartographerConsequence()
        {
            if (MageKnowledge._deferredInquiry != null) { _cartographerConsequenceCountdown = 1; return; }

            var village = Settlement.All
                .Where(se => se.IsVillage && se.MapFaction == Hero.MainHero?.MapFaction)
                .OrderBy(_ => _rng.Next())
                .FirstOrDefault();
            string vName = village?.Name?.ToString() ?? "a village in your territory";

            try { if (village != null) ChangeRelWithOwner(village, -6); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            MageKnowledge._deferredInquiry = () =>
                Msg($"Word reaches you from {vName}: a grey column swept through two nights ago. They came from the north " +
                    "and went north again. They took nothing visible. Three people are missing and the well runs cold " +
                    "in a way that has nothing to do with the season. Whatever the cartographer was preparing for, " +
                    "it has been used. The map was not decorative.", BadColor);
        }

        // ═══════════════════════════════════════════════════════════════════
        // 2. THE CHILD WHO DOES NOT SLEEP
        //    Village enter — general — one global arc, may echo once.
        //    Phase 0: initial encounter.
        //    Phase 1: taken to camp (30-day countdown → FireAshChildConsequence).
        //    Phase 2: kept long-term (180-day countdown → FireAshChildFinal).
        //    Phase 10: echo — reappears in a different village.
        //    Phase 11: arc complete.
        // ═══════════════════════════════════════════════════════════════════

        private static void EV_ChildWhoDoesNotSleep(Settlement s)
        {
            if (_ashChildPhase == 10)
            {
                EV_ChildEcho(s);
                return;
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Child Who Does Not Sleep",
                "A small girl sits alone in the ash of a burned house, unharmed. Frost-feather patterns spread " +
                "from where she sits into the surrounding soot. No villager will look directly at her. " +
                "No one will claim her. She watches you arrive with the expression of someone who has been waiting " +
                "for a specific person and is now deciding whether you are that person.",
                new List<InquiryElement>
                {
                    new InquiryElement("take",    "Take her. She comes with you.",                   null, true,
                        "She does not resist. She does not thank you. She comes."),
                    new InquiryElement("village", "Leave coin with the village elders. Their problem.", null, true,
                        "The elders take your coin. They will try to manage what they do not understand."),
                    new InquiryElement("priest",  "Send for a priest from the next settlement.",       null, true,
                        "A priest will know what to do with her. Or believe he does."),
                    new InquiryElement("ignore",  "Ride past. You have seen enough strangeness.",      null, true,
                        "You ride past. She watches you go."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "take":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("She stands when you extend your hand. She follows without being led. Your men give her " +
                                "a wide berth on the first day. By the second, they have stopped looking at her at all. " +
                                "She watches the fire in you with the quiet attention of someone studying a language " +
                                "they already half-know.", DimColor);
                            _ashChildPhase    = 1;
                            _ashChildCountdown = 30;
                            break;
                        case "village":
                            if (!ChangeGold(-150)) break;
                            Msg("The elders take the coins. They put her in an empty house and set a boy to watch the door. " +
                                "She sits in the dark and does not look at them. Fourteen days later, word reaches you: " +
                                "the house was locked from the outside. She was gone by morning. The frost-patterns were " +
                                "still on the floor. The boy says he did not hear her leave.", DimColor);
                            _ashChildPhase = 10; // echo: may appear again
                            break;
                        case "priest":
                            Msg("A priest comes from the next village. He spends an hour with her behind a closed door. " +
                                "He looks pale when he comes out. He says he will take her to the nearest convent — that " +
                                "she needs care he cannot provide in the village. He takes her. Three weeks later, word " +
                                "comes: the priest was found on the road, dead of no cause the physician could name. " +
                                "There is no sign of the girl.", BadColor);
                            _ashChildPhase = 10;
                            break;
                        case "ignore":
                            Msg("You ride past. At the village edge you notice something that was not there when you arrived: " +
                                "a frost-feather handprint on the canvas of your tent, pressed from the inside. " +
                                "The tent was latched.", AshenColor);
                            _ashChildPhase = 10;
                            break;
                    }
                }, null, "", false), false, true);
        }

        private static void EV_ChildEcho(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Same Child",
                "She is sitting in the ash of a different village's burned house, a hundred miles from wherever " +
                "you last encountered her. She is the same age. The frost-feather patterns spread from the same " +
                "posture. She looks up at you with the expression of someone who waited and is now ready to ask again.",
                new List<InquiryElement>
                {
                    new InquiryElement("take",  "Take her this time.", null, true,
                        "Whatever she is, she has found you twice. That is information."),
                    new InquiryElement("leave", "Ride on. Whatever this is, you want no part of it.", null, true,
                        "She watches you go. She does not follow. She does not need to."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "take":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("She stands before you finish reaching down. Your men say nothing this time. " +
                                "They have seen enough to know that asking questions about her is not a good use " +
                                "of the limited certainty anyone has left. She settles into the column and watches " +
                                "the fire in you with the same patient attention as before.", DimColor);
                            _ashChildPhase    = 1;
                            _ashChildCountdown = 30;
                            break;
                        case "leave":
                            Msg("You ride on. She is still sitting in the ash when the village drops behind you. " +
                                "You will not see her again. Whatever she was waiting to ask, she asked it twice " +
                                "and is done asking.", DimColor);
                            _ashChildPhase = 11; // arc complete
                            break;
                    }
                }, null, "", false), false, true);
        }

        private static void FireAshChildConsequence()
        {
            if (MageKnowledge._deferredInquiry != null) { _ashChildCountdown = 1; return; }

            if (_ashChildPhase == 1)
                FireAshChildCampPhase();
            else if (_ashChildPhase == 2)
                FireAshChildFinal();
        }

        private static void FireAshChildCampPhase()
        {
            MageKnowledge._deferredInquiry = () =>
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "◆  She Does Not Eat",
                    "Your soldiers wake with nightmares about cold fire — specific, quiet nightmares they do not " +
                    "describe to each other. The girl has been in camp for a month. She does not sleep. She does not " +
                    "eat anything you have seen. She does not speak. She has been watching the fire in you when she " +
                    "thinks you are not watching. Sometimes the frost-patterns spread from where she walks.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("keep", "Keep her. She is not harming anyone you can point to.", null, true,
                            "Whatever she is, she found you. That is not nothing."),
                        new InquiryElement("send", "Send her to a settlement. She cannot stay.",           null, true,
                            "You arrange passage to a town with a proper convent. She goes without protest."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        switch (chosen?[0]?.Identifier as string)
                        {
                            case "keep":
                                if (MageKnowledge.IsMage && !MageKnowledge.IsAshen)
                                {
                                    if (MagicLearning.TryGrantRandomUnknown(_rng, out string childLearned))
                                    {
                                        Msg($"One morning she places her hand on your arm and holds it there for a moment. " +
                                            $"Something passes — not words, not instruction, but the shape of {childLearned}, " +
                                            $"given the way children give things: completely and without understanding the value.", FireColor);
                                    }
                                    else
                                    {
                                        ChangeRenown(10f);
                                        Msg("She places her hand on your arm one morning. Something passes. The fire in you " +
                                            "turns back once and finds something it already knew but had not named.", FireColor);
                                    }
                                }
                                else if (MageKnowledge.IsAshen)
                                {
                                    ShiftTrait(DefaultTraits.Mercy, -1);
                                    Msg("She places her hand on your arm one morning and holds it there. The cold in you " +
                                        "responds to something in her — a resonance, deep and patient. Your men's nightmares " +
                                        "get worse that week. You do not notice your own until someone else describes one " +
                                        "that is also yours.", AshenColor);
                                }
                                else
                                {
                                    AddMorale(3f);
                                    Msg("She places her hand on your arm one morning. Nothing passes — or nothing you can name. " +
                                        "But your men's nightmares stop that same week. She continues to watch the fire in you, " +
                                        "patient and unhurried, as though waiting for something to ripen.", GoodColor);
                                }
                                _ashChildPhase    = 2;
                                _ashChildCountdown = 180;
                                break;

                            case "send":
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                Msg("You arrange passage to a town large enough to have a proper convent. She goes without " +
                                    "protest — she goes with the same unhurried certainty she arrived with, which is its own " +
                                    "kind of answer about whether she needed your permission. Your men's nightmares stop " +
                                    "the night she leaves. Nobody mentions this.", DimColor);
                                _ashChildPhase = 11; // arc done
                                break;
                        }
                    }, null, "", false), false, true);
        }

        private static void FireAshChildFinal()
        {
            _ashChildPhase = 11; // arc complete after this

            MageKnowledge._deferredInquiry = () =>
            {
                bool isMage  = MageKnowledge.IsMage;
                bool isAshen = MageKnowledge.IsAshen;

                string body = isAshen
                    ? "She speaks for the first time after six months — not to you, but facing you. " +
                      "Three words, clear and without inflection, as though reciting something she memorised before you met: " +
                      "'The cold was always here.' She does not say it as an accusation. She says it the way you say a fact " +
                      "that everyone in the room already knows but hasn't named."
                    : "She speaks for the first time after six months, at dawn, while your camp is still quiet. " +
                      "She faces you and says two words: 'You are warm.' Not a question. Not a request. " +
                      "An observation she has been working toward for six months, verified now to her satisfaction.";

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    isAshen ? "◆  The Cold Was Always Here" : "✦  You Are Warm",
                    body,
                    new List<InquiryElement>
                    {
                        new InquiryElement("a", isAshen ? "Ask her what she is." : "Ask her what she wants.", null, true,
                            isAshen ? "She will answer. You may not want the answer."
                                    : "She has wanted something for six months. Now she tells you."),
                        new InquiryElement("b", "Say nothing. Let the moment be what it is.",               null, true,
                            "She has been patient this long. She can be patient a little longer."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        if (isAshen)
                        {
                            // Ashen reveal: she is a cold anchor; retroactive camp suppression explained
                            ShiftTrait(DefaultTraits.Mercy, -1);
                            AddMorale(-6f);
                            Msg("She tells you what she is: a vessel the cold uses to extend itself into warm spaces. " +
                                "Not by intent — by nature. She has been drawing warmth out of your camp, slowly, for six months. " +
                                "She does not apologise. She says she thought you knew. Then she walks north and is gone before " +
                                "dawn. Your men wake that morning looking rested for the first time in months.", BadColor);
                        }
                        else if (isMage)
                        {
                            // Mage: she is fire-touched, can be mentored
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ChangeRenown(12f);
                            Msg("She tells you she wants to learn what the warm thing inside her is. She has known it was there " +
                                "for as long as she can remember and has been looking for someone else who knew, which is why she " +
                                "waited at the burned house, and why she waited at the second one. She found what she was looking for. " +
                                "She is six years old and she already knows exactly what she is. You take her on.", FireColor);
                            try
                            {
                                // Mark the child as a mage — she is already in the party as a follower (narrative)
                                // Represent as a small future-investment talent grant
                                try { Hero.MainHero.HeroDeveloper.UnspentFocusPoints += 1; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                            }
                            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        }
                        else
                        {
                            // General: ambiguous, she leaves, you are changed
                            Msg("You say nothing. She nods once — as though you gave the right answer — and by midday she is gone. " +
                                "No note, no goodbye, no frost-patterns on the canvas. Only the feeling, for weeks afterward, " +
                                "that the camp is slightly warmer than the weather accounts for.", GoodColor);
                        }
                    }, null, "", false), false, true);
            };
        }

        // ═══════════════════════════════════════════════════════════════════
        // 3. THE VOW UNDISCHARGED
        //    City leave — general — clan tier ≥ 2 — 90-day cooldown.
        //    Phase 0: old knight blocks the gate with three offers.
        //    Phase 1: "name" path — FireVowConsequence 14 days later.
        //    Phase 2: blackmail path — FireVowAssassins 30 days later.
        //    Phase 3: arc complete.
        // ═══════════════════════════════════════════════════════════════════

        private static void EL_VowUndischarged(Settlement s)
        {
            _vowCooldown = 90;

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  The Vow Undischarged",
                "An old man in rusted livery intercepts you at the city gate. He is visibly dying — not recently, " +
                "but in the way of men who have been dying for a while and are managing it. He has been waiting here, " +
                "he says, for forty years. He swore an oath to your ancestor and could not find the line after the wars. " +
                "He offers three things: his sword arm, a chest he buried that was entrusted to him, or a name. " +
                "He can deliver only one before he falls. He knows this. You can see he has made his peace with it.",
                new List<InquiryElement>
                {
                    new InquiryElement("arm",   "His sword arm — what remains of it.",                null, true,
                        "He attaches himself to your column. He is old, but whatever he was, he was good."),
                    new InquiryElement("chest", "The buried chest.",                                   null, true,
                        "He gives you coordinates. Something of your ancestor waits in the ground."),
                    new InquiryElement("name",  "The name. The man who betrayed your ancestor's quest.", null, true,
                        "He speaks one name. Forty years he has been holding it. It will cost him to release it."),
                    new InquiryElement("pass",  "Thank him and ride on. The debt was not yours.",       null, true,
                        "You acknowledge him. He has found the line, at least. That is something."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "arm":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ChangeRenown(10f);
                            AddMorale(5f);
                            Msg("He attaches himself to your column without ceremony. He says very little and does not ask " +
                                "questions. Three nights later your men find him in his bedroll at dawn — died in the night, " +
                                "quietly, still in the harness. He had discharged his oath. He appears to have known the timing. " +
                                "Your men carry him to the next settlement and see him buried properly.", DimColor);
                            _vowPhase = 3;
                            break;

                        case "chest":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            int chestGold = 1400 + _rng.Next(800);
                            ChangeGold(chestGold);
                            Msg($"He tells you where. You ride to the location and dig. Inside the chest: {chestGold} coin, " +
                                "tarnished but real, and a letter in your ancestor's hand, sealed, addressed to someone whose " +
                                "name is half worn away by damp. You read it alone. You do not read it aloud. " +
                                "Whatever the letter contains, it is yours now.", GoldColor);
                            _vowPhase = 3;
                            break;

                        case "name":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("He speaks one name — quiet, precise, the way you say a thing you have been holding for forty " +
                                "years. He finishes saying it and seems lighter. He touches his chest once, over the old " +
                                "livery, and looks at you as though confirming you are real. He rides away east. " +
                                "You ride out carrying what he discharged into you.", DimColor);
                            _vowPhase    = 1;
                            _vowCountdown = 14;
                            break;

                        case "pass":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You tell him the debt was his alone to carry and he has found the bloodline he was looking for. " +
                                "That is enough. That his oath was made in good faith to someone who is gone is a debt paid to " +
                                "the departed, not to you. He sits with this for a moment and then nods. He looks relieved. " +
                                "You ride out. He watches you go.", GoodColor);
                            _vowPhase = 3;
                            break;
                    }
                }, null, "", false), false, true);
        }

        private static void FireVowConsequence()
        {
            if (MageKnowledge._deferredInquiry != null) { _vowCountdown = 1; return; }

            string charmHint  = SkillHint(DefaultSkills.Charm, 0.35f, "Make the accusation land in public");
            string combatHint = SkillHint(DefaultSkills.OneHanded, 0.40f, "Win the duel cleanly");

            MageKnowledge._deferredInquiry = () =>
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "★  The Named Man",
                    "You find him through a factor in the city markets: a minor merchant-lord, old now, prosperous, " +
                    "with the particular ease of a man who put something terrible behind him long enough ago that " +
                    "it no longer costs him sleep. He does not know you are looking. He does not know the old knight " +
                    "is dead, or found you, or that the name was passed on. He is eating well and trading in good faith " +
                    "with people who have no reason to doubt him.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("public",    "Confront him publicly in his guild hall.",            null, true, charmHint),
                        new InquiryElement("blackmail", "Confront him privately. Take what silence is worth.",  null, true,
                            "He will pay. Men like this always pay."),
                        new InquiryElement("duel",      "Challenge him to a duel. Let the old form settle it.", null, true, combatHint),
                        new InquiryElement("let",       "Let it go. The old man has been paid. This one is old.", null, true,
                            "He will never know you found him. That is its own kind of verdict."),
                    },
                    false, 1, 1, "Decide", "",
                    sub =>
                    {
                        switch (sub?[0]?.Identifier as string)
                        {
                            case "public":
                                if (SkillRoll(DefaultSkills.Charm, 0.35f))
                                {
                                    ShiftTrait(DefaultTraits.Honor, 1);
                                    ChangeRenown(15f);
                                    ChangeRelWithRandomLord(-12);
                                    Msg("You name the betrayal in front of his guild hall — the event, the year, the line he " +
                                        "crossed. He cannot answer it publicly without admitting something. He does not try. " +
                                        "He leaves the hall with his men and files no complaint. His name now carries a story " +
                                        "it will never fully shed. The guild remembers.", GoodColor);
                                }
                                else
                                {
                                    ShiftTrait(DefaultTraits.Honor, -1);
                                    Msg("You name the betrayal but the hall does not believe you without documentation. " +
                                        "He denies it calmly, names witnesses of his own, and suggests your information " +
                                        "came from a dead man who couldn't be questioned. He is technically correct. " +
                                        "You leave having made a claim that the room cannot verify.", BadColor);
                                }
                                _vowPhase = 3;
                                break;

                            case "blackmail":
                                ShiftTrait(DefaultTraits.Honor, -1);
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                int bribeGold = 1800 + _rng.Next(600);
                                ChangeGold(bribeGold);
                                Msg($"He listens to what you know. He counts what silence is worth in his head — you can watch " +
                                    $"him do it — and arrives at {bribeGold} coin. He pays without argument or warmth. " +
                                    $"He files no complaint. He sends the money in two instalments, without a note. " +
                                    $"He will not forget this. His family is large.", GoldColor);
                                _vowPhase            = 2;
                                _vowAssassinCountdown = 30;
                                break;

                            case "duel":
                                if (SkillRoll(DefaultSkills.OneHanded, 0.40f))
                                {
                                    ShiftTrait(DefaultTraits.Honor, 1);
                                    ChangeRenown(20f);
                                    Msg("He accepts — men of his era were trained for this, and he is old but not a coward. " +
                                        "The duel is short and one-sided. He yields on one knee at dawn with a sword at his " +
                                        "throat and you say the name of your ancestor and let him go. He lives. He will carry " +
                                        "that morning for the rest of whatever time he has left.", GoodColor);
                                }
                                else
                                {
                                    WoundMainHero(3);
                                    ShiftTrait(DefaultTraits.Honor, 1);
                                    Msg("He is better than you expected — or you are worse than you thought. " +
                                        "The duel ends when his blade finds your ribs and your second steps in. " +
                                        "He bows correctly and walks away. You lose the duel and take the wound " +
                                        "and your ancestor's name stays in a man who got away with what he did.", BadColor);
                                }
                                _vowPhase = 3;
                                break;

                            case "let":
                                ShiftTrait(DefaultTraits.Honor, 1);
                                Msg("You watch him for an hour from the square outside his hall. He eats well. He laughs at " +
                                    "something a clerk says. He has no idea you are here. You ride out. The old knight is dead " +
                                    "and the name has been delivered and the man it belongs to will die of old age without " +
                                    "knowing you found him. The vow is discharged. The debt is a different matter.", DimColor);
                                _vowPhase = 3;
                                break;
                        }
                    }, null, "", false), false, true);
        }

        private static void FireVowAssassins()
        {
            if (MageKnowledge._deferredInquiry != null) { _vowAssassinCountdown = 1; return; }
            _vowPhase = 3;

            MageKnowledge._deferredInquiry = () =>
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "★  The Price of Silence",
                    "Two mounted men intercept you at a road exit — not bandits, not soldiers. Well-equipped, " +
                    "quiet, riding horses worth more than most soldiers see in a year. They have been waiting at " +
                    "this junction. They know your route. The merchant-lord's family is large and patient " +
                    "and it has been thirty days since you took his coin.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("fight", "Draw. End it.",       null, true,
                            "Two against you and however many of your escort are near."),
                        new InquiryElement("talk",  "Talk your way out.", null, true,
                            "They were sent to send a message, not necessarily to kill you. Maybe."),
                    },
                    false, 1, 1, "Decide", "",
                    sub =>
                    {
                        switch (sub?[0]?.Identifier as string)
                        {
                            case "fight":
                                if (_rng.NextDouble() < 0.65)
                                {
                                    ChangeRenown(8f);
                                    ShiftTrait(DefaultTraits.Calculating, 1);
                                    Msg("Your escort handles one; you handle the other. It is brief and unpleasant. " +
                                        "On the dead man's body: a letter naming you by full title, which means " +
                                        "the merchant-lord dictated it himself. The letter also names three things " +
                                        "about your route that you told no one, which means someone in your circle " +
                                        "is telling him things. The assassin is not the problem. The source is.", BadColor);
                                }
                                else
                                {
                                    WoundMainHero(3);
                                    AddMorale(-5f);
                                    Msg("They are better than anticipated. One of them reaches you before your escort can close. " +
                                        "You take a wound that will cost you weeks. You win — barely — and the road is clear, " +
                                        "but the merchant-lord now knows his men found you and did not come back. " +
                                        "He will send more careful men next time.", BadColor);
                                }
                                break;

                            case "talk":
                                if (SkillRoll(DefaultSkills.Charm, 0.30f))
                                {
                                    ShiftTrait(DefaultTraits.Calculating, 1);
                                    Msg("You tell them that what you know and what you have shared are different things, " +
                                        "and that the lord's family is better served by your silence than by your death. " +
                                        "You make the math visible. They are soldiers, not strategists; they count on their " +
                                        "fingers and conclude you are right. They ride away. The message goes back: " +
                                        "the problem is managed by other means.", GoodColor);
                                }
                                else
                                {
                                    WoundMainHero(2);
                                    Msg("They are not interested in the math. They were given a task and they attempt it. " +
                                        "Your escort is enough to prevent a killing — barely — but not enough to prevent " +
                                        "a wound and a very clear demonstration that the merchant-lord's patience has limits.", BadColor);
                                }
                                break;
                        }
                    }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // 4. THE FEVER ROAD
        //    Village leave — general — recurring hazard over a 60-day window.
        //    Up to 3 triggers per window. 180-day cooldown between windows.
        //    Stakes escalate with each trigger. Troop exposure → 7-day spread.
        // ═══════════════════════════════════════════════════════════════════

        private static void LV_FeverRoad(Settlement s)
        {
            // Activate or continue the window
            if (!_feverRoadActive)
            {
                _feverRoadActive      = true;
                _feverRoadWindow      = 60;
                _feverRoadTriggerCount = 0;
            }
            _feverRoadTriggerCount++;
            // Close the window after 3 triggers
            if (_feverRoadTriggerCount >= 3)
            {
                _feverRoadActive   = false;
                _feverRoadCooldown = 180;
            }

            int trigger = _feverRoadTriggerCount;

            string title = trigger == 1 ? "★  The Fever Road"
                         : trigger == 2 ? "★  The Fever Road Again"
                                        : "★  The Fever Road, Once More";

            string opening = trigger == 1
                ? "A dying rider slumps from his horse at the road junction ahead of you. He has come from the east " +
                  "and is going nowhere. Before he stops moving he gets out one sentence: plague ahead. Three villages. " +
                  "You can see the smoke from here."
                : trigger == 2
                ? "You have seen this smoke before — the specific grey-brown column of a village in fever-quarantine, " +
                  "burning its bedding. Three columns, different distances. The same fever you left behind last month " +
                  "has found the next road. Your men recognize it before you point."
                : "The smoke is familiar now. Your men have begun marking the sick villages on their own maps. " +
                  "The fever has been following the trade roads for two months. It has found your road again. " +
                  "Three villages. The same columns. Smaller parties than yours have been stopped by it entirely.";

            string medicineHint  = SkillHint(DefaultSkills.Medicine,  0.35f, "Find a path that reduces exposure");
            string leadHint      = SkillHint(DefaultSkills.Leadership, 0.35f, "Send scouts ahead and trust the report");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                title, opening,
                new List<InquiryElement>
                {
                    new InquiryElement("detour",  "Burn the road and detour. Seven days lost.",     null, true,
                        "Safe. Expensive in time. No exposure."),
                    new InquiryElement("careful", "Press through carefully.", null, true, medicineHint),
                    new InquiryElement("fast",    "Press through fast. Outrun the exposure.",        null, true,
                        "Fifty-fifty. Either it works or it doesn't."),
                    new InquiryElement("scouts",  "Send scouts ahead to find the safest path.",      null, true, leadHint),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    int extraStakes = (trigger - 1) * 2; // escalation: more troops at risk each pass

                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "detour":
                            AddMorale(-(3 + extraStakes));
                            Msg($"You detour. Seven days added to the march. {(trigger > 1 ? "Again. " : "")}Your men do not complain, " +
                                "not out loud. They have seen what waits on the other road, but a week is a week.", DimColor);
                            // Safe — no exposure, paid for in time and patience rather than health
                            break;

                        case "careful":
                            if (SkillRoll(DefaultSkills.Medicine, 0.35f))
                            {
                                Msg("You read the villages correctly and route between the worst of them. Your men take " +
                                    $"the route in close order, hands off doorways, water from your own supply. {(trigger > 1 ? "You have learned, at least. " : "")}" +
                                    "Minor morale cost. Nobody develops a fever.", GoodColor);
                                AddMorale(-(2 + extraStakes));
                            }
                            else
                            {
                                AddMorale(-(5 + extraStakes));
                                Msg("You read the villages but your route clips the edge of the worst one. " +
                                    $"Three men are feverish by nightfall.{(trigger > 1 ? " Again." : "")} " +
                                    "The camp is silent in the way camps get when the fever is already among them.", BadColor);
                                _feverRoadSpreadCountdown = 7;
                            }
                            break;

                        case "fast":
                            if (_rng.NextDouble() < 0.50)
                            {
                                Msg($"You ride hard and come out the other side clean.{(trigger > 1 ? " You have done this before." : "")} " +
                                    "Nobody develops a fever. The fever road is behind you.", GoodColor);
                            }
                            else
                            {
                                AddMorale(-(6 + extraStakes));
                                Msg($"Speed is not enough.{(trigger > 1 ? " It was not enough last time either." : "")} " +
                                    $"Four men are sick by the second night. The column smells like it. " +
                                    "You will lose more if you do not act.", BadColor);
                                _feverRoadSpreadCountdown = 7;
                            }
                            break;

                        case "scouts":
                            if (SkillRoll(DefaultSkills.Leadership, 0.35f))
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                Msg("Your scouts find a mill track that bypasses the worst villages entirely. " +
                                    $"Two days added.{(trigger > 1 ? " Better than last time." : "")} No exposure. " +
                                    "Your men follow the track without complaint.", GoodColor);
                                AddMorale(2f);
                            }
                            else
                            {
                                AddMorale(-(3 + extraStakes));
                                Msg("Your scouts lose time and come back with a route that clears two of the three villages " +
                                    "but not the third. You take it anyway — it is better than nothing. " +
                                    $"Two men develop symptoms by nightfall.", DimColor);
                                _feverRoadSpreadCountdown = 7;
                            }
                            break;
                    }
                }, null, "", false), false, true);
        }

        private static void FireFeverRoadSpread()
        {
            if (MageKnowledge._deferredInquiry != null) { _feverRoadSpreadCountdown = 1; return; }

            bool isMage    = MageKnowledge.IsMage;
            bool isAshen   = MageKnowledge.IsAshen;
            // Old attunement or the merged art — any living mage draws the living
            // world now. Not the Ashen: the cold has its own answer below.
            bool isNature  = NatureKnowledge.IsAttuned || (isMage && !isAshen);

            MageKnowledge._deferredInquiry = () =>
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "◆  The Camp Sickness",
                    "The fever that began in the road villages has spread through your camp. " +
                    "More men are sick than the first night suggested. Your surgeon says you have perhaps three days " +
                    "before it becomes a march-stopping problem. He has ideas but none of them are cheap.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("healer",  "Pay for a traveling healer. Stop it properly.",  null, true,
                            "Expensive. Certain. −600 gold."),
                        new InquiryElement("ride",    "Ride through it. Your men are tougher than a fever.", null, true,
                            "Some of them are. The question is which some."),
                        new InquiryElement("fire",    "Use the Inner Fire to purge it.",                  null, isMage && !isAshen,
                            "The fire can burn out fever. The cost is in your years."),
                        new InquiryElement("nature",  "Draw from the living world to clear the fever.",   null, isNature,
                            "The land's warmth knows sickness. You give of yourself — your own blood, not your years."),
                        new InquiryElement("cold",    "Let the cold still the fever.",                    null, isAshen,
                            "Extreme cold kills what heat sustains. You will cleanse them — but the cold asks something back."),
                    },
                    false, 1, 1, "Decide", "",
                    sub =>
                    {
                        switch (sub?[0]?.Identifier as string)
                        {
                            case "healer":
                                if (ChangeGold(-600))
                                {
                                    Msg("The healer arrives before nightfall. He is methodical and humourless and effective. " +
                                        "The fever peaks and breaks over two days. Your column is slowed but not stopped. " +
                                        "You do not lose anyone you can name.", GoodColor);
                                }
                                break;

                            case "ride":
                                AddMorale(-10f);
                                if (_rng.NextDouble() < 0.35)
                                {
                                    ChangeRelWithRandomLord(-5);
                                    Msg("You ride through it. Most of your men are tougher than a fever. " +
                                        "Not all of them. You bury two before the column clears the sick stretch. " +
                                        "The men who survive it are not impressed with the decision.", BadColor);
                                }
                                else
                                    Msg("You ride through it. The fever burns through the column in four days and leaves. " +
                                        "Nobody dies — or nobody you hear about. Your men are diminished and quiet " +
                                        "for a week afterward.", DimColor);
                                break;

                            case "fire":
                                AgePlayer(30);
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                Msg("You work through the column through two nights, burning the fever out of them one by one. " +
                                    "The fire knows the difference between what belongs in a body and what doesn't. " +
                                    "By the second dawn the column is clean. Your men do not understand what you did. " +
                                    "They understand the result. The cost is thirty days of your years, spent without ceremony " +
                                    "in a camp that will never know the exact price.", FireColor);
                                AddMorale(8f);
                                break;

                            case "nature":
                                try { Hero.MainHero.HitPoints = Math.Max(1, Hero.MainHero.HitPoints - 25); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                Msg("You move through the camp at dusk and let the living world work through your hands. " +
                                    "It does not burn — it draws. The fever lifts off your men like morning mist off a river, " +
                                    "and settles somewhere beyond you, dispersed into the land. By dawn the column is clear. " +
                                    "What it cost is in you now — not years, but blood. You will feel it for a day or two.",
                                    new TaleWorlds.Library.Color(0.35f, 0.75f, 0.35f));
                                AddMorale(8f);
                                break;

                            case "cold":
                                AgePlayer(15);
                                ShiftTrait(DefaultTraits.Mercy, -1);
                                Msg("You push the cold through the camp like a winter passing through. " +
                                    "The fever breaks instantly — fever cannot survive this. Your men wake shivering " +
                                    "and hollow-eyed, but standing. The sickness is gone. So is something else: " +
                                    "the camp feels three degrees colder than the night accounts for, " +
                                    "and two of the dogs refuse to come in from the dark.", AshenColor);
                                AddMorale(3f);
                                break;
                        }
                    }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // 5. ASHES IN THE BREAD
        //    Village enter — general — 45-day cooldown.
        //    Moral trap: mob assembled, eastern settlers blamed. Investigate or comply.
        //    Mage layer: the ash is from a cold site; children are changing.
        //    Deferred 14 days: consequences based on choice.
        // ═══════════════════════════════════════════════════════════════════

        private static void EV_AshesInTheBread(Settlement s)
        {
            _ashBreadCooldown = 45;

            bool isMage = MageKnowledge.IsMage;
            string investigateHint = SkillHint(DefaultSkills.Medicine, 0.35f,
                "Identify the actual cause before the mob acts");
            string leadHint = SkillHint(DefaultSkills.Leadership, 0.35f,
                "Disperse the crowd without violence");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  Ashes in the Bread",
                "Children in the village are sick and grey-lipped. A mob has assembled around the house of an " +
                "eastern family who settled here last autumn. The village elder stands at the front of the crowd " +
                "and points. The eastern family — two adults, three children of their own — stand in their doorway " +
                "and say nothing. They know what saying something would cost them. The mob is on the edge " +
                "and the elder is deciding whether to push it.",
                new List<InquiryElement>
                {
                    new InquiryElement("investigate", "Investigate. Find the cause before the mob does something permanent.",
                        null, true, investigateHint),
                    new InquiryElement("mob",         "Support the mob. The elder knows this village.",
                        null, true,
                        "The elder's authority is the village's authority. You reinforce it."),
                    new InquiryElement("defend",      "Defend the family. Put yourself between them and the crowd.",
                        null, true, leadHint),
                    new InquiryElement("pay",         "Pay the elder to stand the mob down. Thirty coin.",
                        null, true,
                        "Thirty coin. The mob disperses. The cause remains."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "investigate":
                            if (SkillRoll(DefaultSkills.Medicine, 0.35f))
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                ShiftTrait(DefaultTraits.Honor, 1);
                                // Mage second layer: the ash is from a cold site
                                if (isMage)
                                {
                                    Msg("You examine the bread in the eastern family's house and the flour in the miller's " +
                                        "store. The ash is in the flour — not in the eastern family's supply. It is the " +
                                        "miller adulterating the grain for margin. But you feel something else: the ash " +
                                        "has cold in it that has nothing to do with hearth-fire. This came from a cold " +
                                        "site. The miller used Ashen residue without knowing what it was. " +
                                        "The children are not simply sick. They are changing.", AshenColor);
                                }
                                else
                                {
                                    Msg("You examine the bread and trace the ash to the miller's store. He has been adulterating " +
                                        "the flour with fire-ash to stretch the weight — buying cheap and selling full. " +
                                        "The eastern family's supply is clean. You bring the miller out in front of the crowd " +
                                        "and show them the two sacks side by side.", GoodColor);
                                }
                                ChangeRelWithOwner(s, 5);
                                _ashBreadOutcome      = 1;
                                _ashBreadSettlementId = s.StringId;
                                _ashBreadCountdown    = 14;
                            }
                            else
                            {
                                Msg("You examine what you can, but the cause is buried deep enough in the supply chain that " +
                                    "you cannot extract it quickly. You tell the mob you have found nothing conclusive. " +
                                    "The elder takes this as a failure of method rather than a verdict. The mob " +
                                    "disperses — for now — but the eastern family spends the night watching their door.", DimColor);
                                // No deferred consequence — inconclusive
                            }
                            break;

                        case "mob":
                            ShiftTrait(DefaultTraits.Mercy, -1);
                            Msg("You stand beside the elder. The mob reads your presence correctly. The eastern family is " +
                                "given until nightfall to take what they can carry. By dark they are on the eastern road " +
                                "with two packs and three children and no destination. The mob disperses. " +
                                "The children in the village are still sick.", BadColor);
                            _ashBreadOutcome      = 2;
                            _ashBreadSettlementId = s.StringId;
                            _ashBreadCountdown    = 14;
                            break;

                        case "defend":
                            if (SkillRoll(DefaultSkills.Leadership, 0.35f))
                            {
                                ShiftTrait(DefaultTraits.Honor, 1);
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                Msg("You put yourself between the family and the crowd and give the elder a choice: " +
                                    "produce evidence or stand down. He has none and he knows it. The crowd reads the " +
                                    "situation correctly — a lord, in armor, on the side of the door — and disperses. " +
                                    "The elder is publicly humiliated. The family stays. The cause is still unknown.", GoodColor);
                                ChangeRelWithOwner(s, -8);
                                _ashBreadOutcome      = 3;
                                _ashBreadSettlementId = s.StringId;
                                _ashBreadCountdown    = 14;
                            }
                            else
                            {
                                ShiftTrait(DefaultTraits.Honor, 1);
                                ChangeRelWithOwner(s, -12);
                                var kingdom = s.MapFaction as Kingdom;
                                if (kingdom != null) try { ChangeCrimeRatingAction.Apply(kingdom, 15f, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                                Msg("The mob does not read your authority the way mobs should. Someone throws something " +
                                    "and then someone else throws something and what follows is a brawl in a village street " +
                                    "with a lord in the middle of it. The family gets out in the confusion. You are left " +
                                    "with a crime citation and an elder who will file a report that puts your name in it.", BadColor);
                            }
                            break;

                        case "pay":
                            if (!ChangeGold(-30)) { _ashBreadCooldown = 0; break; }
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("Thirty coin to the elder. He pockets it without ceremony and tells the crowd he will " +
                                "investigate the matter properly. The crowd disperses. The elder does not investigate. " +
                                "The cause remains in the flour and the children remain grey-lipped and the eastern " +
                                "family remains behind a closed door until you leave.", DimColor);
                            _ashBreadOutcome      = 4;
                            _ashBreadSettlementId = s.StringId;
                            _ashBreadCountdown    = 14;
                            break;
                    }
                }, null, "", false), false, true);
        }

        private static void FireAshBreadConsequence()
        {
            if (MageKnowledge._deferredInquiry != null) { _ashBreadCountdown = 1; return; }
            int    outcome = _ashBreadOutcome;
            string sId     = _ashBreadSettlementId;
            _ashBreadOutcome      = 0;
            _ashBreadSettlementId = null;

            var settl = sId != null
                ? Settlement.All.FirstOrDefault(se => se.StringId == sId)
                : null;

            switch (outcome)
            {
                case 1: // exposed miller
                    MageKnowledge._deferredInquiry = () =>
                    {
                        int reward = 250 + _rng.Next(200);
                        ChangeGold(reward);
                        ShiftTrait(DefaultTraits.Mercy, 1);
                        // The eastern family's eldest joins your party
                        AddMorale(4f);
                        Msg($"Word reaches you from the village: the miller was brought before the lord's court and fined. " +
                            $"The eastern family is still there. Their eldest son — who watched everything from the doorway — " +
                            $"rode out the morning after the verdict and found your column on the road. He asks to ride with you. " +
                            $"He does not explain why. He doesn't need to. The village sends {reward} coin as formal thanks.", GoodColor);
                    };
                    break;

                case 2: // mob supported, family expelled
                    MageKnowledge._deferredInquiry = () =>
                    {
                        if (MageKnowledge.IsAshen)
                        {
                            ShiftTrait(DefaultTraits.Mercy, -1);
                            Msg("Word from the region: the eastern family — expelled, homeless, with nowhere to go — was " +
                                "met on the grey road by figures in grey cloaks who offered them food and shelter and something " +
                                "to do with their anger. The cold has found three new voices. Your name is in one of the " +
                                "reasons they gave for accepting.", AshenColor);
                        }
                        else
                        {
                            AddMorale(-5f);
                            ChangeRelWithOwner(settl, -4);
                            Msg("Word from the region: the village's food output has dropped — the eastern family were its " +
                                "best farmers by a margin nobody counted until they were gone. The children are still sick. " +
                                "The miller's flour is still the same flour. The lord sends a letter asking what exactly " +
                                "you accomplished when you passed through.", BadColor);
                        }
                    };
                    break;

                case 3: // defended the family
                    MageKnowledge._deferredInquiry = () =>
                    {
                        if (_rng.NextDouble() < 0.55)
                        {
                            // Elder's guilt becomes obvious — apology and gold
                            int apologyGold = 180 + _rng.Next(120);
                            ChangeGold(apologyGold);
                            ChangeRelWithOwner(settl, 3);
                            Msg("Word reaches you: the miller's adulteration was discovered by a different traveler two weeks " +
                                "after you left. The elder — who had fined the eastern family ten coin for the disruption — " +
                                "reversed the fine and sent a sealed message to your last known camp. Inside: " +
                                $"{apologyGold} coin and two words in the village's script that the messenger translates as " +
                                "'you were right.' It is a private apology, not a public one. It is something.", GoodColor);
                        }
                        else
                        {
                            ChangeRelWithOwner(settl, -3);
                            Msg("Word reaches you: the children are still sick, the cause still unknown, and the elder " +
                                "has been telling the village that a passing lord interfered with local justice and " +
                                "prevented a proper resolution. He is wrong. He does not know he is wrong. " +
                                "He has been saying it anyway for two weeks.", BadColor);
                        }
                    };
                    break;

                case 4: // paid off, cause unresolved
                    MageKnowledge._deferredInquiry = () =>
                    {
                        ChangeGold(-60); // fine for 'delayed response'
                        AddMorale(-3f);
                        Msg("Word from the region's lord: the village's sickness spread to a second hamlet before it was " +
                            "traced to the miller's adulterated flour. A formal inquiry names everyone who passed through " +
                            "the village during the outbreak. Your name is in the list under 'observed, no action taken.' " +
                            "The lord levies a small fine for the delayed response. You pay it. The children recovered.", BadColor);
                    };
                    break;
            }
        }
    }
}
