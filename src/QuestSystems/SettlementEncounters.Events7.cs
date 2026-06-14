// =============================================================================
// ASH AND EMBER — SettlementEncounters.Events7.cs
// Five multi-phase questline encounters (Batch 2).
//   ES_Ardrath         — Siege (won), clan tier ≥ 2, one-time
//   E_Cartographer     — Enter village, day 50+, 80-day cooldown
//   E_WidowCommission  — Enter castle, clan tier ≥ 1, renown ≥ 300, day 60+
//   E_GodThatDidntBurn — Enter village, non-Ashen, day 70+, 90-day cooldown
//   E_HeirApparent     — Leave city/castle, clan tier ≥ 4, day 120+, 120-day cooldown
// Partial of SettlementEncounters (shared state lives in SettlementEncounters.cs).
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
        // THE CHILD IN THE KEEP (Ardrath) — Siege won, clan tier ≥ 2, one-time
        //
        // Phase 0: idle — fires as siege consequence
        // Phase 1 (7d):  the letter's contents become clear; player decides
        // Phase 2 (14d): the named lord receives word / responds
        // Phase 3 (21d): political resolution or sale consequence
        // ═══════════════════════════════════════════════════════════════════

        private static void ES_Ardrath()
        {
            _ardrathFound = 1;

            var recipient = Hero.AllAliveHeroes
                .Where(h => h.IsLord && h.IsAlive && h != Hero.MainHero && !h.IsPrisoner)
                .OrderBy(_ => _rng.Next()).FirstOrDefault();
            _ardrathLordId = recipient?.StringId;

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Child in the Keep",
                "Your soldiers find a child in a sealed tower room — seven years old, maybe eight. " +
                "She is not afraid. She holds a sealed letter in both hands with the gravity of someone who was told its importance before she was told why. " +
                "The seal is a lord's seal, intact. The name written on the front is not the lord who held this keep. " +
                "She says she was told to give it only to the right person when the right person arrived.",
                new List<InquiryElement>
                {
                    new InquiryElement("open",    "Break the seal. Read it.", null, true,
                        "You have taken a keep today. What is one more sealed thing."),
                    new InquiryElement("deliver", "Take the letter unread. Deliver it as addressed.", null, true,
                        "You will be the right person. You will not need to know what it says."),
                    new InquiryElement("give",    "Give the child to a safe household. Leave the letter with her.", null, true,
                        "Whatever the letter was meant to do, let it do it without you."),
                    new InquiryElement("sell",    "Keep both. Find out who will pay for an unread lord's correspondence.", null, true,
                        "Information is currency. Sealed information is worth more."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "open":
                            Msg("The letter names a conspiracy involving three lords and a merchant guild. " +
                                "The dead lord apparently changed his mind at the last moment and wrote it all down for someone he trusted. " +
                                "He trusted a man who is still alive and who will want to know that you have read this.", DimColor);
                            _ardrathPhase = 1;
                            _ardrathCountdown = 7;
                            break;

                        case "deliver":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You seal the letter again with wax from your own kit and dispatch a rider to the named recipient. " +
                                "The child is placed with a merchant family in the town below. " +
                                "A week passes before you understand what the letter must have said.", DimColor);
                            _ardrathPhase = 1;
                            _ardrathCountdown = 7;
                            break;

                        case "give":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ChangeRenown(15f);
                            Msg("The child and the letter leave together in a wagon headed south. " +
                                "Your soldiers ask you later what was in it. You tell them you chose not to know. " +
                                "They believe you. You do not entirely believe yourself.", DimColor);
                            // Quest ends quietly — no further phases
                            break;

                        case "sell":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            _ardrathLordSold = true;
                            Msg("You send word to three different parties simultaneously. The one who answers fastest will buy the letter unopened. " +
                                "Two days later, someone you have never met delivers a sizable sum and a note that says only: 'Burned.'", DimColor);
                            _ardrathPhase = 1;
                            _ardrathCountdown = 7;
                            break;
                    }
                }
            ), true);
        }

        private static void FireArdrathPhase1()
        {
            if (MageKnowledge._deferredInquiry != null) { _ardrathCountdown = 2; return; }

            if (_ardrathLordSold)
            {
                // Sold path: the buyer was the wrong party; consequences arrive
                MageKnowledge._deferredInquiry = () =>
                {
                    MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                        "✦  The Letter's Echo",
                        "A messenger arrives at your camp. The letter you sold arrived in the wrong hands — " +
                        "the buyer was a front for one of the conspirators named in it. " +
                        "The note they send back reads: 'We know your name now. We consider this balance, not threat. " +
                        "Do not look for the child.' " +
                        "The child was not harmed. She simply cannot be found.",
                        new List<InquiryElement>
                        {
                            new InquiryElement("accept", "Let it rest. You were paid.", null, true,
                                "Some transactions have a second ledger that only opens later."),
                            new InquiryElement("pursue", "Find out who the buyer really was.", null, true,
                                "This will cost you something before it gives you anything."),
                        },
                        false, 1, 1, "Decide", "",
                        chosen2 =>
                        {
                            if ((chosen2?[0]?.Identifier as string) == "pursue")
                            {
                                ShiftTrait(DefaultTraits.Valor, 1);
                                ChangeGold(-800);
                                Msg("You spend a week and a moderate sum confirming what you half-knew. " +
                                    "The buyer is connected to the Ember Tithe network — or what is left of it. " +
                                    "The child is with them, unharmed, apparently willing. " +
                                    "You are now a person they have noticed.", DimColor);
                                if (_titheCultAgent)
                                    Msg("(Your existing contact within the Collector's network hears that your name has surfaced. They send a warning.)", AshenColor);
                                _ardrathPhase = 3;
                                _ardrathCountdown = 21;
                            }
                            else
                            {
                                ChangeRenown(-20f);
                                Msg("You let it rest. The feeling of having participated in something you do not fully understand settles into the back of your accounts.", DimColor);
                                _ardrathPhase = 0;
                            }
                        }
                    ), true);
                };
                return;
            }

            // Opened or delivered path
            var lord = _ardrathLordId != null ? Hero.FindFirst(h => h.StringId == _ardrathLordId) : null;
            string lordName = lord?.Name?.ToString() ?? "the named lord";

            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Letter's Reach",
                    $"You now understand the shape of what the dead lord wrote. " +
                    $"Three lords agreed to cede taxing rights on a river crossing to a merchant coalition in exchange for financing a private war — one they planned to blame on a fourth party. " +
                    $"The letter was addressed to {lordName}. It reads as a confession and a warning both. " +
                    $"You are holding the kind of knowledge that changes relationships permanently.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("warn",     $"Send word to {lordName}. Let him know what you hold.", null, true,
                            "He will owe you something he can never fully repay."),
                        new InquiryElement("leverage", "Hold it. See who approaches you first.", null, true,
                            "Knowledge does not expire. Patience does."),
                        new InquiryElement("burn",     "Destroy it. This is not your war.", null, true,
                            "The dead lord may have intended this outcome too."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen2 =>
                    {
                        switch (chosen2?[0]?.Identifier as string)
                        {
                            case "warn":
                                if (lord != null)
                                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, 12, false);
                                Msg($"A rider reaches {lordName} before the conspirators know you exist. " +
                                    $"His response arrives three days later: measured, careful, the tone of someone who has just recalculated several years of planning.", DimColor);
                                _ardrathPhase = 2;
                                _ardrathCountdown = 14;
                                break;

                            case "leverage":
                                Msg("You wait. Within ten days you receive two separate, oblique inquiries from parties who heard something moved at that keep. " +
                                    "Neither one knows you know. You know more than both of them.", DimColor);
                                _ardrathPhase = 2;
                                _ardrathCountdown = 14;
                                break;

                            case "burn":
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                ChangeRenown(10f);
                                Msg("You burn the letter. The child, wherever she is, carries nothing dangerous now. " +
                                    "The conspiracy may run its course. You have declined to be the instrument of its unravelling or its acceleration.", DimColor);
                                _ardrathPhase = 0;
                                break;
                        }
                    }
                ), true);
            };
        }

        private static void FireArdrathPhase2()
        {
            if (MageKnowledge._deferredInquiry != null) { _ardrathCountdown = 2; return; }

            var lord = _ardrathLordId != null ? Hero.FindFirst(h => h.StringId == _ardrathLordId) : null;
            string lordName = lord?.Name?.ToString() ?? "your contact";

            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Named Lord Moves",
                    $"{lordName} has acted on what you shared. Two of the three conspirators have quietly withdrawn from the merchant coalition. " +
                    $"The third — the one who planned the blamed war — is now moving soldiers. " +
                    $"A message from {lordName} asks if you are prepared to be named openly as the source of the intelligence. " +
                    $"Openly means credit. It also means the third conspirator knows your name.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("name",    "Allow it. Let your name be used.", null, true,
                            "You chose this. Finish it."),
                        new InquiryElement("refuse",  "Refuse. You were never in this room.", null, true,
                            "This is still an option. For now."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen3 =>
                    {
                        if ((chosen3?[0]?.Identifier as string) == "name")
                        {
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ChangeRenown(40f);
                            if (lord != null)
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, 8, false);
                            ChangeRelWithRandomLord(-10);
                            Msg($"Your name enters the record. {lordName} moves swiftly after that. " +
                                $"The third conspirator dissolves the soldier movement within a week and retires to his estates. " +
                                $"You have made one firm ally and one careful enemy. Both will remember.", DimColor);
                        }
                        else
                        {
                            ChangeRenown(15f);
                            Msg($"{lordName} acts without naming you. The conspiracy fragments without a public narrative. " +
                                $"You receive a sealed token from {lordName} — it is a calling card that will open one door, once, when you present it.", DimColor);
                        }
                        _ardrathPhase = 0;
                    }
                ), true);
            };
        }

        private static void FireArdrathPhase3()
        {
            if (MageKnowledge._deferredInquiry != null) { _ardrathCountdown = 2; return; }

            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Network Notices",
                    "A rider bearing no insignia brings you a folded note. Inside is a list of your last four camps, " +
                    "correct to within a day. Below the list, in a different hand: " +
                    "'We hold no grievance. We prefer it remain that way. " +
                    "The child is being educated. She will be useful to many people in time. " +
                    "We thought you would want to know she is well.'",
                    new List<InquiryElement>
                    {
                        new InquiryElement("reply",  "Send a reply. One sentence. Make it count.", null, true,
                            "They will read it exactly as carefully as you wrote it."),
                        new InquiryElement("ignore", "Burn the note. Acknowledge nothing.", null, true,
                            "Silence is also a language they speak."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen4 =>
                    {
                        if ((chosen4?[0]?.Identifier as string) == "reply")
                        {
                            ShiftTrait(DefaultTraits.Cunning, 1);
                            Msg("You write: 'I have no objection to her education.' You send it. " +
                                "Three days later a rider brings a small wooden box. Inside is the child's name, written in her own hand. " +
                                "She has been taught to write already. You keep the note.", DimColor);
                        }
                        else
                        {
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ChangeRenown(10f);
                            Msg("You burn the note and say nothing. " +
                                "Whatever is being built around that child, you have declined to be the cornerstone. " +
                                "That may be the only thing in this whole affair that was entirely your choice.", DimColor);
                        }
                        _ardrathPhase = 0;
                    }
                ), true);
            };
        }

        // ═══════════════════════════════════════════════════════════════════
        // THE CARTOGRAPHER — Enter village, day 50+, 80-day cooldown
        //
        // Phase 0: idle
        // Phase 1 (7d):  player has carried the map; its implications settle
        // Phase 2 (14d): a scholar arrives asking about the cartographer
        // Phase 3 (21d): the map's meaning resolves
        // ═══════════════════════════════════════════════════════════════════

        private static void E_Cartographer(Settlement s)
        {
            _cartCooldown = 80;

            string settlementName = s?.Name?.ToString() ?? "the village";

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Cartographer",
                $"An old man is drawing in the dirt outside the mill at {settlementName}. " +
                $"He uses a stick with the unhurried precision of someone who has done this for a long time. " +
                $"You lean in and see roads. Some of them are roads you know. Some of them are roads you have not seen yet. " +
                $"One of them, if you read the scale correctly, leads through a mountain pass that does not exist.",
                new List<InquiryElement>
                {
                    new InquiryElement("speak",  "Speak to him.", null, true,
                        "He will answer. He seems to have been expecting a question."),
                    new InquiryElement("map",    "Ask to see his papers — the real map, not the dirt.", null, true,
                        "If he has been drawing from something, you want to see what."),
                    new InquiryElement("watch",  "Say nothing. Watch him finish.", null, true,
                        "Sometimes the answer is in how a thing ends, not in asking."),
                    new InquiryElement("ignore", "Walk past. You have business here.", null, true,
                        "The roads in the dirt will wash away in the next rain."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "speak":
                            Msg("He says he maps roads before they are built, passes before they are found, " +
                                "and settlements before they are named. He has been doing this since before the war. " +
                                "He says he does not decide which roads come true. That part, he says, depends on who is walking.", DimColor);
                            _cartPhase = 1;
                            _cartCountdown = 7;
                            _cartMapGranted = true;
                            break;

                        case "map":
                            var papers = true;
                            Msg("He has a roll of vellum under his coat. He gives it to you without negotiation. " +
                                "The map is impossibly detailed — settlements named, roads marked, a scale in the margin that places it forty years ahead. " +
                                "Your clan's seat is on the map. The name above it is blank. The symbol beneath it means 'burned.'", DimColor);
                            _cartPhase = 1;
                            _cartCountdown = 7;
                            _cartMapGranted = true;
                            break;

                        case "watch":
                            Msg("He finishes the road. He smooths it over. He begins again somewhere else. " +
                                "When he looks up, something in his expression suggests the watching was the right answer. " +
                                "He reaches into his coat and gives you something without explanation.", DimColor);
                            _cartPhase = 1;
                            _cartCountdown = 7;
                            _cartMapGranted = true;
                            break;

                        case "ignore":
                            Msg("You walk past. Two days later, a trader mentions that an old cartographer died peacefully in his sleep at the village mill. " +
                                "He apparently had no possessions except a roll of vellum which no one could find.", DimColor);
                            _cartCooldown = 180;
                            break;
                    }
                }
            ), true);

            _cartMapSettlementId = s?.StringId;
        }

        private static void FireCartographerPhase1()
        {
            if (MageKnowledge._deferredInquiry != null) { _cartCountdown = 2; return; }

            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Blank Name",
                    "You have had a week to study the map. The cartography is accurate where you can check it — " +
                    "down to which bridges have been replaced since the last war and which mills are on the wrong bank. " +
                    "The scale is consistent. The date implied in the margin is forty-three years from now. " +
                    "\n\nYour clan's seat is marked. The name above the symbol is blank. " +
                    "The symbol means the same thing in every cartographic tradition you can name.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("keep",    "Keep it. You will need to know this.", null, true,
                            "The map becomes something you consult more often than you intend to."),
                        new InquiryElement("two",     "Copy it before anything else. Keep both versions.", null, true,
                            "You are already thinking about who might want the original."),
                        new InquiryElement("burn",    "Burn it. Some futures should not be read.", null, true,
                            "The cartographer did not tell you whether this was a prediction or a warning."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen2 =>
                    {
                        switch (chosen2?[0]?.Identifier as string)
                        {
                            case "keep":
                                _cartMapKept = true;
                                Msg("You roll the map and keep it with your campaign documents. " +
                                    "You find yourself noting which roads in the present already match the ones marked in the future. " +
                                    "Most of them do.", DimColor);
                                _cartPhase = 2;
                                _cartCountdown = 14;
                                break;

                            case "two":
                                _cartMapKept = true;
                                _cartMapTwoVersions = true;
                                ShiftTrait(DefaultTraits.Cunning, 1);
                                Msg("You make a careful copy on ordinary parchment, then keep both. " +
                                    "The original has something in the paper itself — it does not smudge, even in rain. " +
                                    "You notice this on the third day and choose not to examine it further.", DimColor);
                                _cartPhase = 2;
                                _cartCountdown = 14;
                                break;

                            case "burn":
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                Msg("You burn the map in your camp fire. " +
                                    "It takes longer than paper should. When it is gone, you feel something that is not quite relief " +
                                    "and not quite loss, and you understand that the cartographer probably knew you would make this choice.", DimColor);
                                _cartPhase = 0;
                                _cartMapGranted = false;
                                break;
                        }
                    }
                ), true);
            };
        }

        private static void FireCartographerPhase2()
        {
            if (MageKnowledge._deferredInquiry != null) { _cartCountdown = 2; return; }

            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Scholar's Visit",
                    "A scholar of middle age arrives at your camp under a flag of academic neutrality — " +
                    "the kind of flag that is technically respected but practically inconvenient. " +
                    "She says she studies cartographic prophecy and she has been following the old man's trail for three years. " +
                    "She believes he distributed several maps before he died. " +
                    "She wants to know if you received one.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("share",   "Tell her what you have. Show her the map.", null, _cartMapKept,
                            "She will study it for hours without speaking."),
                        new InquiryElement("copy",    "Give her the copy, keep the original.", null, _cartMapTwoVersions,
                            "She will know immediately that this is not the original. She will accept it anyway."),
                        new InquiryElement("deny",    "Tell her you found nothing at that village.", null, true,
                            "She will not believe you entirely. She is too careful a scholar for that."),
                        new InquiryElement("question","Ask what the maps mean before you decide anything.", null, true,
                            "She knows more than she has said. She was waiting for this question."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen3 =>
                    {
                        switch (chosen3?[0]?.Identifier as string)
                        {
                            case "share":
                                ChangeRenown(20f);
                                Msg("She studies the map for three hours without speaking. " +
                                    "When she looks up she says: 'The blank names are not predictions. They are absences. " +
                                    "Something will make them blank. The cartographer drew what would be, not what must be.' " +
                                    "She returns the map and does not ask to copy it.", DimColor);
                                _cartPhase = 3;
                                _cartCountdown = 21;
                                break;

                            case "copy":
                                ShiftTrait(DefaultTraits.Cunning, 1);
                                Msg("She takes the copy and examines it for ten minutes. " +
                                    "'This is not the original,' she says. 'The original has something in the paper.' " +
                                    "She does not press the point. She thanks you and leaves with the copy. " +
                                    "You still have the map.", DimColor);
                                _cartPhase = 3;
                                _cartCountdown = 21;
                                break;

                            case "deny":
                                Msg("She accepts the denial graciously, which suggests she expected it. " +
                                    "Before she leaves she says: 'If you find yourself on a road the map shows as blank, " +
                                    "do not walk to its end.' You remember this later.", DimColor);
                                _cartPhase = 3;
                                _cartCountdown = 21;
                                break;

                            case "question":
                                Msg("She says the maps are drawn from a current that runs beneath the world — " +
                                    "not the future exactly, but the shape of accumulated choices settling. " +
                                    "The blank names are places the fire has already touched in the version she can see. " +
                                    "She says your clan seat being blank is not inevitable. It is simply the most likely path from here.", DimColor);
                                ShiftTrait(DefaultTraits.Cunning, 1);
                                _cartPhase = 3;
                                _cartCountdown = 21;
                                break;
                        }
                    }
                ), true);
            };
        }

        private static void FireCartographerPhase3()
        {
            if (MageKnowledge._deferredInquiry != null) { _cartCountdown = 2; return; }

            MageKnowledge._deferredInquiry = () =>
            {
                string msg = _cartMapKept
                    ? "You look at the map again, three weeks after the scholar. " +
                      "You have added small marks of your own — decisions made, roads taken. " +
                      "The blank above your clan's seat still reads as absence. " +
                      "You fold the map and keep it because you have decided that carrying knowledge of what might happen " +
                      "is not the same as accepting it. The cartographer did not tell you whose road this was."
                    : "The scholar's words have settled into something that feels less like fear and more like attention. " +
                      "You find yourself noticing which roads you take without needing to. Which detours you choose. " +
                      "The map is gone, but you carry the shape of it — and the shape is not fixed.";

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Shape of What Might Be",
                    msg,
                    new List<InquiryElement>
                    {
                        new InquiryElement("forward", "You are not walking to a blank name. Forward.", null, true,
                            "The next road is yours to mark."),
                    },
                    false, 1, 1, "Continue", "",
                    _ =>
                    {
                        ShiftTrait(DefaultTraits.Valor, 1);
                        ChangeRenown(25f);
                        Msg("Whatever the cartographer drew, you have not yet walked to its end. That, at least, is yours.", GoodColor);
                        _cartPhase = 0;
                    }
                ), true);
            };
        }

        // ═══════════════════════════════════════════════════════════════════
        // THE WIDOW'S COMMISSION — Enter castle, clan tier ≥ 1, renown ≥ 300, day 60+
        //
        // Phase 0: idle
        // Phase 1 (7d):  documentation arrives; player decides what to do with it
        // Phase 2 (21d): the exposed lord responds; confrontation or flight
        // Phase 3 (14d): political settlement
        // ═══════════════════════════════════════════════════════════════════

        private static void E_WidowCommission(Settlement s)
        {
            _widowCooldown = 90;

            var exposedLord = Hero.AllAliveHeroes
                .Where(h => h.IsLord && h.IsAlive && h != Hero.MainHero && !h.IsPrisoner)
                .OrderBy(_ => _rng.Next()).FirstOrDefault();
            _widowLordId = exposedLord?.StringId;

            string lordName = exposedLord?.Name?.ToString() ?? "a merchant lord";
            string castleName = s?.Name?.ToString() ?? "the castle";

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Widow's Commission",
                $"A woman in deep mourning receives you in the hall at {castleName}. " +
                $"Her husband died three months ago — an illness that came quickly, without precedent. " +
                $"She has spent those three months, she says, going through his papers. " +
                $"She found things that were not his. Records in another hand. Lists. " +
                $"She believes {lordName} sold census records of known mage-touched individuals to a collector she cannot name. " +
                $"She wants you specifically because you have the reach to do something with it.",
                new List<InquiryElement>
                {
                    new InquiryElement("accept", "Take the commission. You will look into it.", null, true,
                        "She gives you a sealed case of papers without ceremony."),
                    new InquiryElement("verify", "Ask to see the papers first. You do not take commissions on faith.", null, true,
                        "She anticipated this. She has a summary prepared."),
                    new InquiryElement("warn",   $"Tell her you will first warn {lordName} that she has found this.", null, true,
                        "This is not loyalty to the lord. It is an opening bid."),
                    new InquiryElement("refuse", "Decline. This is politics dressed as justice.", null, true,
                        "She watches you leave with the patience of someone who has other options."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "accept":
                            Msg("She gives you the case. It is heavy and well-organized, the work of someone who has been doing nothing else for ninety days. " +
                                "You will read it on the road.", DimColor);
                            _widowPhase = 1;
                            _widowCountdown = 7;
                            _widowBuyerKnown = false;
                            break;

                        case "verify":
                            Msg("The summary is five pages in a precise hand. The records include names, locations, dates, " +
                                "and a recurring payment notation that correlates with a specific guild account. " +
                                "The guilt, if it is what it looks like, is thorough.", DimColor);
                            _widowPhase = 1;
                            _widowCountdown = 7;
                            _widowBuyerKnown = true;
                            break;

                        case "warn":
                            ShiftTrait(DefaultTraits.Cunning, 1);
                            Msg($"You send a careful message to {lordName}: 'A widow has papers. I have been invited to examine them. " +
                                $"I thought you should know.' You will see what he does with that information before you decide anything else.", DimColor);
                            _widowLordHostile = false;
                            _widowThirdPartyFlag = true;
                            _widowPhase = 1;
                            _widowCountdown = 7;
                            break;

                        case "refuse":
                            ChangeRenown(-10f);
                            Msg("You leave. Three days later a different lord takes the commission. You begin to understand why she said 'you specifically.'", DimColor);
                            break;
                    }
                }
            ), true);
        }

        private static void FireWidowPhase1()
        {
            if (MageKnowledge._deferredInquiry != null) { _widowCountdown = 2; return; }

            var lord = _widowLordId != null ? Hero.FindFirst(h => h.StringId == _widowLordId) : null;
            string lordName = lord?.Name?.ToString() ?? "the merchant lord";

            MageKnowledge._deferredInquiry = () =>
            {
                string context = _widowThirdPartyFlag
                    ? $"{lordName} has responded to your warning. He sent a very large sum and a message that says he wishes to discuss 'the matter of the widow's misunderstanding.' He has not denied anything. He has offered you a price before you have made any demand."
                    : $"You have had a week with the documentation. It is genuine — the paper, the ink, the accounting method all check out. The records sold included names that you recognise. Some of them are now dead. One of them is a person you know.";

                bool titheLinkActive = _titheCultAgent;

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Documentation",
                    context + (titheLinkActive
                        ? "\n\nYour contact within the Tithe Collector's network confirms, unprompted, that these census records were the source material for a purge list three seasons ago."
                        : ""),
                    new List<InquiryElement>
                    {
                        new InquiryElement("expose",  $"Bring it to the relevant lords. Expose {lordName} publicly.", null, true,
                            "This will cost him everything. It will cost you the people who preferred him comfortable."),
                        new InquiryElement("extort",  $"Approach {lordName} directly. Name your price.", null, true,
                            "He will pay. The question is what paying does to him."),
                        new InquiryElement("give",    "Give everything to the widow. Let her decide what to do with it.", null, true,
                            "She will do something with it. She has had ninety days to plan."),
                        new InquiryElement("archive", "File it somewhere safe. Hold it for a moment that needs it.", null, true,
                            "Information held is information available. For now."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen2 =>
                    {
                        switch (chosen2?[0]?.Identifier as string)
                        {
                            case "expose":
                                ShiftTrait(DefaultTraits.Honor, 1);
                                ChangeRenown(30f);
                                if (lord != null)
                                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, -20, false);
                                _widowLordHostile = true;
                                Msg($"The documentation moves through the right channels. {lordName}'s financial relationships begin to unravel within a fortnight. " +
                                    $"He knows your name in this. He will not forget it.", DimColor);
                                _widowPhase = 2;
                                _widowCountdown = 21;
                                break;

                            case "extort":
                                ShiftTrait(DefaultTraits.Honor, -1);
                                ShiftTrait(DefaultTraits.Cunning, 1);
                                ChangeGold(3000);
                                _widowLordHostile = false;
                                Msg($"{lordName} pays without negotiation, which tells you the documentation is worse than you thought. " +
                                    $"He pays in coin and in a quiet promise of one favour, which he specifies must not touch his primary holdings. " +
                                    $"The widow receives nothing. You are aware of this.", DimColor);
                                _widowPhase = 2;
                                _widowCountdown = 21;
                                break;

                            case "give":
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                ChangeRenown(20f);
                                Msg("You deliver everything to the widow. She thanks you with the controlled precision of someone who has been waiting for this moment for three months. " +
                                    "What she does with it will take longer than you will be in a position to observe directly.", DimColor);
                                _widowPhase = 2;
                                _widowCountdown = 21;
                                break;

                            case "archive":
                                ShiftTrait(DefaultTraits.Cunning, 1);
                                Msg("You file it. The documentation is secure. You have declined to act on it for now, " +
                                    "which means you have also declined to give the widow what she commissioned.", DimColor);
                                _widowPhase = 2;
                                _widowCountdown = 21;
                                break;
                        }
                    }
                ), true);
            };
        }

        private static void FireWidowPhase2()
        {
            if (MageKnowledge._deferredInquiry != null) { _widowCountdown = 2; return; }

            var lord = _widowLordId != null ? Hero.FindFirst(h => h.StringId == _widowLordId) : null;
            string lordName = lord?.Name?.ToString() ?? "the lord";

            MageKnowledge._deferredInquiry = () =>
            {
                string situation = _widowLordHostile
                    ? $"{lordName} has retained legal counsel and challenged the documentation's provenance in a formal hearing. He names you as the source of a forgery campaign. This is false. It is also believed by three lords who preferred his old arrangements."
                    : $"{lordName} has quietly divested from the guild accounts named in the papers. He has also, less quietly, begun making charitable gestures toward families of the dead. You are watching a man do his penance in public while keeping what matters private.";

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Lord's Response",
                    situation,
                    new List<InquiryElement>
                    {
                        new InquiryElement("counter", "Counter it directly. Produce additional witnesses.", null, _widowLordHostile,
                            "This will take resources and leave a permanent mark on the record."),
                        new InquiryElement("pressure", $"Apply pressure where {lordName} is exposed. Force a settlement.", null, true,
                            "He knows where he is exposed. So do you."),
                        new InquiryElement("let",      "Let it settle on its own. You have done what you agreed to do.", null, true,
                            "Sometimes the shape of an outcome is enough, even without a verdict."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen3 =>
                    {
                        switch (chosen3?[0]?.Identifier as string)
                        {
                            case "counter":
                                ChangeGold(-1500);
                                ChangeRenown(35f);
                                ShiftTrait(DefaultTraits.Honor, 1);
                                if (lord != null)
                                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, -15, false);
                                Msg($"The hearing is long and expensive. {lordName} loses the challenge formally. " +
                                    $"He retires from two of his primary offices. The widow sends you a note that says only: 'Thank you.'", DimColor);
                                break;

                            case "pressure":
                                ShiftTrait(DefaultTraits.Cunning, 1);
                                ChangeRenown(20f);
                                ChangeGold(2000);
                                Msg($"{lordName} settles before the hearing concludes. " +
                                    $"The terms are private. You come away with something tangible and the widow comes away with an apology she did not ask for.", DimColor);
                                break;

                            case "let":
                                ChangeRenown(10f);
                                Msg("The matter resolves itself over months, the way these things do — into a permanent awkwardness that everyone navigates around. " +
                                    "The widow does not write to you again. You take this to mean she expected this ending and accepted it.", DimColor);
                                break;
                        }
                        _widowPhase = 0;
                    }
                ), true);
            };
        }

        // ═══════════════════════════════════════════════════════════════════
        // THE GOD THAT DIDN'T BURN — Enter village, non-Ashen, day 70+, 90-day cooldown
        //
        // Phase 0: idle
        // Phase 1 (14d): player investigates the named lord
        // Phase 2 (14d): the named date approaches; player positions themselves
        // Phase 3 (7d):  resolution — outcome of the intervention
        // ═══════════════════════════════════════════════════════════════════

        private static void E_GodThatDidntBurn(Settlement s)
        {
            _godCooldown = 90;

            var targetLord = Hero.AllAliveHeroes
                .Where(h => h.IsLord && h.IsAlive && h != Hero.MainHero && !h.IsPrisoner)
                .OrderBy(_ => _rng.Next()).FirstOrDefault();
            _godLordId = targetLord?.StringId;

            string lordName = targetLord?.Name?.ToString() ?? "a lord";

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The God That Didn't Burn",
                "The shrine at the edge of the village is not on fire, exactly. The thatch roof is burning. The stone idol inside is not. " +
                "The villagers have formed a circle at a respectful distance and are watching. " +
                "As you pass it, the idol speaks — not in any voice that travels through air, but in the way that certain knowledge arrives " +
                "without a pathway you can name. It gives you three things: a lord's name, a date fourteen days hence, and a road.",
                new List<InquiryElement>
                {
                    new InquiryElement("listen",  "Listen. Let the idol finish.", null, true,
                        "The message is for someone who will do something with it. That may be you."),
                    new InquiryElement("question","Ask what is supposed to happen on that road, on that date.", null, true,
                        "The idol does not elaborate. It has already said the important part."),
                    new InquiryElement("record",  "Commit it to memory, exactly. Nothing more.", null, true,
                        "You have the information. What you do with it is still undecided."),
                    new InquiryElement("walk",    "Walk away. You did not agree to receive messages.", null, true,
                        "The message arrives anyway. It simply arrives without your consent."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    string resp = chosen?[0]?.Identifier as string;
                    switch (resp)
                    {
                        case "listen":
                        case "record":
                            Msg($"The information is clear and specific: {lordName} will travel the old river road in fourteen days. " +
                                $"Someone intends to ensure he does not reach the end of it. " +
                                $"The idol has given you a warning intended for someone. You cannot tell if that someone is you or {lordName}.", DimColor);
                            break;

                        case "question":
                            Msg($"The shrine gives no more. The thatch finishes burning. The idol is untouched, " +
                                $"which seems to answer your question about what didn't burn and why.", DimColor);
                            break;

                        case "walk":
                            Msg($"You walk away. The three things stay with you regardless: {lordName}, a date, a road. " +
                                $"The idol has said everything it intends to say.", DimColor);
                            break;
                    }
                    _godPhase = 1;
                    _godCountdown = 14;
                    _godListAcquired = (resp == "listen" || resp == "question" || resp == "record");
                }
            ), true);
        }

        private static void FireGodPhase1()
        {
            if (MageKnowledge._deferredInquiry != null) { _godCountdown = 2; return; }

            var lord = _godLordId != null ? Hero.FindFirst(h => h.StringId == _godLordId) : null;
            string lordName = lord?.Name?.ToString() ?? "the lord";

            MageKnowledge._deferredInquiry = () =>
            {
                bool titheLinkActive = _titheCultAgent;

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Named Road",
                    $"You have spent two weeks learning what you can about {lordName} and the road the idol named. " +
                    $"{lordName} has made enemies in the conventional ways: taxation disputes, a contested inheritance, " +
                    $"a border skirmish that was never formally settled. Any of them could have hired what the idol described." +
                    (titheLinkActive ? $"\n\nYour contact in the Tithe network mentions that {lordName} recently refused a request from a Collector — without specifying what was asked. That refusal may be relevant." : ""),
                    new List<InquiryElement>
                    {
                        new InquiryElement("warn",    $"Go to {lordName}. Tell him what you know.", null, true,
                            "He will want to know who told you and why you are telling him."),
                        new InquiryElement("intercept","Position yourself on that road before the date. Intervene if needed.", null, true,
                            "You will be there when it happens, whatever it is."),
                        new InquiryElement("expose",  "Find out who ordered it. That is the more useful information.", null, true,
                            "You can warn the lord after you know who to name."),
                        new InquiryElement("nothing", "Do nothing. This is not your road to walk.", null, true,
                            "The idol spoke. It did not commission you."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen2 =>
                    {
                        switch (chosen2?[0]?.Identifier as string)
                        {
                            case "warn":
                                if (lord != null)
                                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, 8, false);
                                _godContactMade = true;
                                Msg($"{lordName} receives you carefully. He does not ask how you know what you know, " +
                                    $"which suggests he already suspects someone. He changes his route. He sends you a rider to say so.", DimColor);
                                _godPhase = 2;
                                _godCountdown = 14;
                                break;

                            case "intercept":
                                _godContactMade = false;
                                Msg($"You are on the old river road three days before the named date. " +
                                    $"You wait. This is a different kind of patience than the campaign map asks for.", DimColor);
                                _godPhase = 2;
                                _godCountdown = 14;
                                break;

                            case "expose":
                                ShiftTrait(DefaultTraits.Cunning, 1);
                                _godContactMade = false;
                                _godListAcquired = true;
                                Msg($"You spend two weeks tracing the connections. The trail ends at a merchant coalition " +
                                    $"with outstanding grievances against {lordName}'s land decisions. " +
                                    $"You now have a name. You also have a date and a road and fourteen days left.", DimColor);
                                _godPhase = 2;
                                _godCountdown = 14;
                                break;

                            case "nothing":
                                Msg($"You do nothing. You mark the date in your ledger and continue your campaign. " +
                                    $"What happens on that road will happen without you.", DimColor);
                                _godPhase = 2;
                                _godCountdown = 14;
                                break;
                        }
                    }
                ), true);
            };
        }

        private static void FireGodPhase2()
        {
            if (MageKnowledge._deferredInquiry != null) { _godCountdown = 2; return; }

            var lord = _godLordId != null ? Hero.FindFirst(h => h.StringId == _godLordId) : null;
            string lordName = lord?.Name?.ToString() ?? "the lord";

            MageKnowledge._deferredInquiry = () =>
            {
                string situation = _godContactMade
                    ? $"{lordName} changed his route. The date passed. A trader on the old road reports finding signs of a prepared ambush — caltrops, a concealed approach — abandoned."
                    : $"The date has arrived. You are positioned as you planned. " +
                      $"{lordName} is on the road. Two riders with covered weapons emerge from the treeline ahead of his escort.";

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Date on the Road",
                    situation,
                    new List<InquiryElement>
                    {
                        new InquiryElement("intervene", "Ride forward. Intervene.", null, !_godContactMade,
                            "What happens next depends on how many there are and how fast you move."),
                        new InquiryElement("watch",     "Watch first. You need to see who is behind this.", null, !_godContactMade,
                            "This is the more dangerous patience."),
                        new InquiryElement("reflect",   "The idol was right. Consider what that means.", null, _godContactMade,
                            "A god that didn't burn gave you accurate intelligence. That has implications."),
                        new InquiryElement("name",      "You have the name of who ordered this. Use it now.", null, _godListAcquired,
                            "Bring the name to the right authority before the trail goes cold."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen3 =>
                    {
                        switch (chosen3?[0]?.Identifier as string)
                        {
                            case "intervene":
                                _godLordSaved = true;
                                ShiftTrait(DefaultTraits.Valor, 1);
                                ChangeRenown(25f);
                                if (lord != null)
                                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, 15, false);
                                Msg($"You ride forward. The riders scatter when they see you coming with purpose. " +
                                    $"{lordName}'s escort secures the road. He will spend the rest of the day asking who told you.", DimColor);
                                _godPhase = 3;
                                _godCountdown = 7;
                                break;

                            case "watch":
                                ShiftTrait(DefaultTraits.Cunning, 1);
                                _godListAcquired = true;
                                Msg($"You watch long enough to see the riders' approach and identify their livery. " +
                                    $"Then you ride forward — late enough that {lordName} is shaken but not harmed. " +
                                    $"You have a face and a faction. You also have a lord who nearly died and will want to know why.", DimColor);
                                _godLordSaved = true;
                                if (lord != null)
                                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, 10, false);
                                _godPhase = 3;
                                _godCountdown = 7;
                                break;

                            case "reflect":
                                ShiftTrait(DefaultTraits.Cunning, 1);
                                Msg("The idol gave you true intelligence. It burned the thatch and left the stone and spoke once, correctly. " +
                                    "You add this to your understanding of what the world contains. " +
                                    "The file does not close; it simply opens into a larger question.", DimColor);
                                _godPhase = 3;
                                _godCountdown = 7;
                                break;

                            case "name":
                                ChangeRenown(30f);
                                ChangeRelWithRandomLord(-12);
                                ShiftTrait(DefaultTraits.Honor, 1);
                                Msg("The name of the coalition behind the plot reaches the right authorities before the date. " +
                                    "The ambush is abandoned before it begins — word travels faster than swords when it has to. " +
                                    "You have made enemies who know they miscalculated.", DimColor);
                                _godLordSaved = true;
                                _godPhase = 3;
                                _godCountdown = 7;
                                break;
                        }
                    }
                ), true);
            };
        }

        private static void FireGodPhase3()
        {
            if (MageKnowledge._deferredInquiry != null) { _godCountdown = 2; return; }

            var lord = _godLordId != null ? Hero.FindFirst(h => h.StringId == _godLordId) : null;
            string lordName = lord?.Name?.ToString() ?? "the lord";

            MageKnowledge._deferredInquiry = () =>
            {
                string resolution = _godLordSaved
                    ? $"{lordName} is alive and has sent you a formal acknowledgment — the careful, restrained kind that lords send when they owe a debt they cannot quantify. He does not ask how you knew. He has probably worked out that he does not want to know."
                    : "The lord lives or dies as the road decided without you. The idol has gone cold. The village rebuilt its shrine roof. The stone is still unmarked by fire.";

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  What the Idol Knew",
                    resolution + "\n\nYou have been thinking about the shrine. A thing that does not burn when everything around it burns. " +
                    "It spoke once and was accurate. These facts do not require a theology but they suggest one.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("close", "Let it rest. You know what you need to know.", null, true,
                            "The question of what it was does not need an answer today."),
                    },
                    false, 1, 1, "Continue", "",
                    _ =>
                    {
                        if (_godLordSaved)
                        {
                            ChangeRenown(20f);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                        }
                        Msg("The god that didn't burn has not spoken again. You are watching, without quite deciding that you are watching, for the next shrine.", GoodColor);
                        _godPhase = 0;
                    }
                ), true);
            };
        }

        // ═══════════════════════════════════════════════════════════════════
        // THE HEIR APPARENT — Leave city/castle, clan tier ≥ 4, day 120+, 120-day cooldown
        //
        // Phase 0: idle
        // Phase 1 (14d): claim surfaces; player decides how to handle the officer
        // Phase 2 (21d): spymaster confrontation
        // Phase 3 (14d): crown military pressure arrives
        // Phase 4 (21d): political resolution
        // Phase 5 (14d): historical record / legacy
        // ═══════════════════════════════════════════════════════════════════

        private static void E_HeirApparent(Settlement s)
        {
            _heirCooldown = 120;

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Heir Apparent",
                "An officer of middle rank approaches you as your party prepares to leave. " +
                "He is not from your household. He shows you a document — one page, three seals, a lineage table. " +
                "He says he has spent four years verifying it and he is certain. " +
                "He says your bloodline carries a legitimate claim to the throne of the kingdom you currently serve or operate within. " +
                "He says he is telling you privately, first, because he believes that is what the document deserves.",
                new List<InquiryElement>
                {
                    new InquiryElement("examine", "Take the document. Examine it yourself.", null, true,
                        "He gives it to you without hesitation. He has copies."),
                    new InquiryElement("silence", "Tell him to say nothing. Take the document. Burn it.", null, true,
                        "He has copies. He will tell you that he has copies."),
                    new InquiryElement("ask",     "Ask him what he wants from this.", null, true,
                        "He says he wants nothing. He says he simply thought you should know. You do not entirely believe him."),
                    new InquiryElement("dismiss", "Send him away. You have no interest in claims.", null, true,
                        "He goes. The document does not. He has already filed it somewhere."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "examine":
                            Msg("The document is genuine by every test you can apply on the road. " +
                                "Three seals from institutions you recognise, a lineage that checks against public records, " +
                                "a gap in the succession that everyone chose to paper over forty years ago. " +
                                "It is not a forgery. That is the problem.", DimColor);
                            _heirOfficerAllied = true;
                            _heirDocumentHeld = true;
                            _heirPhase = 1;
                            _heirCountdown = 14;
                            break;

                        case "silence":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("You take it. He tells you he has three copies placed with three different institutions under seal. " +
                                "'I am not threatening you,' he says. 'I am telling you that this cannot be undone by burning one page.' " +
                                "He leaves. The claim is now yours whether you hold it or not.", DimColor);
                            _heirOfficerAllied = false;
                            _heirSuppressed = true;
                            _heirDocumentHeld = true;
                            _heirPhase = 1;
                            _heirCountdown = 14;
                            break;

                        case "ask":
                            Msg("He says he spent four years on this because he believed the current succession was built on concealment. " +
                                "He is a historian by training. He says knowing what is true matters more than what is convenient. " +
                                "He is, you think, probably sincere. That makes this more complicated, not less.", DimColor);
                            _heirOfficerAllied = true;
                            _heirHistorianSafe = true;
                            _heirPhase = 1;
                            _heirCountdown = 14;
                            break;

                        case "dismiss":
                            Msg("He goes. The claim has been filed with the historian's guild, an archive, and a religious institution. " +
                                "You learn this within the week because someone else already knows and sends you a message asking what you intend to do about it.", DimColor);
                            _heirOfficerAllied = false;
                            _heirPhase = 1;
                            _heirCountdown = 14;
                            break;
                    }
                }
            ), true);
        }

        private static void FireHeirPhase1()
        {
            if (MageKnowledge._deferredInquiry != null) { _heirCountdown = 2; return; }

            MageKnowledge._deferredInquiry = () =>
            {
                string situation = _heirSuppressed
                    ? "The claim has surfaced publicly despite your attempt to contain it. The officer's copies reached three separate institutions before you could act. You are now associated with a succession claim you tried to bury, which reads, to outside parties, as either guilt or cowardice."
                    : "The claim is known within political circles. You have not announced it. You do not need to — the document's existence has done that for you. Two factions have already approached you with congratulations that were actually opening positions in a negotiation you did not start.";

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Claim Surfaces",
                    situation,
                    new List<InquiryElement>
                    {
                        new InquiryElement("pursue",   "Pursue the claim. Formally and publicly.", null, true,
                            "You will need allies, time, and the willingness to become a different kind of person."),
                        new InquiryElement("hold",     "Hold it. Use it as leverage without pressing it.", null, true,
                            "A claim held is a card not yet played. The value depends on the game."),
                        new InquiryElement("denounce", "Denounce it publicly. State you want no claim.", null, true,
                            "This will cost you the people who already attached themselves to it."),
                        new InquiryElement("protect",  _heirHistorianSafe ? "Protect the historian. He is the one at risk." : "Find the officer. Ensure his safety.", null, true,
                            "Whatever the claim means, the man who found it is now a target."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen2 =>
                    {
                        switch (chosen2?[0]?.Identifier as string)
                        {
                            case "pursue":
                                ShiftTrait(DefaultTraits.Valor, 1);
                                ChangeRenown(50f);
                                Msg("You make it known that the document exists and that you intend to pursue it through proper channels. " +
                                    "Within a week you have three supporters, four opponents, and one spymaster who arrives uninvited.", DimColor);
                                _heirPhase = 2;
                                _heirCountdown = 21;
                                break;

                            case "hold":
                                ShiftTrait(DefaultTraits.Cunning, 1);
                                Msg("You hold it. The political weight of a held claim is different from a pressed one — " +
                                    "it creates uncertainty, which is its own kind of pressure. " +
                                    "The kingdom's spymaster will notice within the month.", DimColor);
                                _heirPhase = 2;
                                _heirCountdown = 21;
                                break;

                            case "denounce":
                                ShiftTrait(DefaultTraits.Honor, 1);
                                ChangeRenown(20f);
                                ChangeRenown(-15f);
                                Msg("Your public statement is well-received by some and treated as a tactical retreat by others. " +
                                    "The claim does not disappear — it is now part of the record even with your renunciation attached. " +
                                    "The spymaster arrives three weeks later anyway.", DimColor);
                                _heirSuppressed = true;
                                _heirPhase = 2;
                                _heirCountdown = 21;
                                break;

                            case "protect":
                                if (_heirOfficerAllied) _heirHistorianSafe = true;
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                ChangeGold(-500);
                                Msg("You ensure the officer — the historian — has a guard and a route out if needed. " +
                                    "He is grateful in the way that people are grateful when they understand they were about to become a loose end. " +
                                    "He tells you he has one more document that he has not yet shown anyone.", DimColor);
                                _heirPhase = 2;
                                _heirCountdown = 21;
                                break;
                        }
                    }
                ), true);
            };
        }

        private static void FireHeirPhase2()
        {
            if (MageKnowledge._deferredInquiry != null) { _heirCountdown = 2; return; }

            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Spymaster's Visit",
                    "He arrives with four escorts and no appointment. He is polite, which is the spymaster's version of a drawn blade. " +
                    "He says the crown is aware of the document. He says the crown has a position on succession claims that arise from genealogical research, " +
                    "and that position has been consistent for forty years. He says he is here to understand your intentions before the crown is required to act on its position. " +
                    "He says this pleasantly, which makes it worse.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("stand",    "Stand firm. The claim is legitimate. You will not be intimidated.", null, true,
                            "He notes this and thanks you for your clarity."),
                        new InquiryElement("negotiate","Negotiate. You want something from this, not the throne.", null, true,
                            "He is authorised to negotiate. He came prepared."),
                        new InquiryElement("concede",  "Concede the claim formally in his presence.", null, true,
                            "He will document it. You will receive something in return for making his visit easy."),
                        new InquiryElement("expose",   "Tell him you know how the gap in the succession was created. All forty years of it.", null, _heirHistorianSafe,
                            "The historian gave you the second document. This is what it says."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen3 =>
                    {
                        switch (chosen3?[0]?.Identifier as string)
                        {
                            case "stand":
                                ShiftTrait(DefaultTraits.Valor, 1);
                                Msg("He leaves. Politely. The crown's position will arrive in a different form.", DimColor);
                                _heirPhase = 3;
                                _heirCountdown = 14;
                                break;

                            case "negotiate":
                                ShiftTrait(DefaultTraits.Cunning, 1);
                                _heirConcession = true;
                                ChangeRenown(30f);
                                ChangeGold(5000);
                                Msg("You negotiate for two hours. You do not get the throne. " +
                                    "You get the acknowledgment that the claim exists and three material concessions that you specify. " +
                                    "He is satisfied. You have traded a claim for leverage, which was probably the smarter choice.", DimColor);
                                _heirPhase = 3;
                                _heirCountdown = 14;
                                break;

                            case "concede":
                                ShiftTrait(DefaultTraits.Honor, 1);
                                ChangeRenown(40f);
                                ChangeGold(8000);
                                Msg("He documents the concession and the crown provides the agreed consideration: " +
                                    "land, a formal acknowledgment of your clan's service, and a title that means something on paper. " +
                                    "The claim is closed. The record of its existence is not.", DimColor);
                                _heirSuppressed = true;
                                _heirPhase = 4;
                                _heirCountdown = 21;
                                break;

                            case "expose":
                                ShiftTrait(DefaultTraits.Cunning, 2);
                                ChangeRenown(60f);
                                Msg("His expression does not change, which is a kind of expression. " +
                                    "You tell him what the second document says about how the succession gap was created forty years ago. " +
                                    "He takes a long moment. Then he says: 'This conversation will continue at a different level.' " +
                                    "He leaves without the answer he came for. You have escalated.", DimColor);
                                _heirPhase = 3;
                                _heirCountdown = 14;
                                break;
                        }
                    }
                ), true);
            };
        }

        private static void FireHeirPhase3()
        {
            if (MageKnowledge._deferredInquiry != null) { _heirCountdown = 2; return; }

            bool conceded = _heirSuppressed;

            MageKnowledge._deferredInquiry = () =>
            {
                string situation = conceded
                    ? "The formal concession has been processed and the crown's consideration delivered. The document is sealed in an archive with your renunciation attached. It will be there for anyone who looks in fifty years."
                    : "A military escort — not an army, not quite — has been positioned near your holdings. Not threatening. Observing. The crown has decided to make its position legible through logistics rather than words.";

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Crown's Position",
                    situation,
                    new List<InquiryElement>
                    {
                        new InquiryElement("match",   "Match it. Position your own forces. Signal that you understand the language.", null, !conceded,
                            "This is how these things are conducted when words have reached their limit."),
                        new InquiryElement("forward", "Move forward politically. The military posture is theatre.", null, !conceded,
                            "You have been in enough campaigns to know the difference between a threat and a signal."),
                        new InquiryElement("accept",  "Accept the situation. You have what you negotiated for.", null, conceded,
                            "The record stands. The claim is on file. That is enough for now."),
                        new InquiryElement("ignore",  "Continue your normal operations. Answer nothing.", null, true,
                            "Silence, in this context, is also a position."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen4 =>
                    {
                        switch (chosen4?[0]?.Identifier as string)
                        {
                            case "match":
                                ShiftTrait(DefaultTraits.Valor, 1);
                                AddMorale(10f);
                                Msg("The mutual positioning continues for three weeks and then resolves when both sides realise they have communicated everything they intended to. " +
                                    "No blow is struck. A new equilibrium is established.", DimColor);
                                _heirPhase = 4;
                                _heirCountdown = 21;
                                break;

                            case "forward":
                                ShiftTrait(DefaultTraits.Cunning, 1);
                                ChangeRenown(25f);
                                Msg("You move politically while the crown postures militarily. By the time they realise you were using the window, you have three new relationships they did not expect.", DimColor);
                                _heirPhase = 4;
                                _heirCountdown = 21;
                                break;

                            case "accept":
                                ChangeRenown(20f);
                                Msg("You have what you negotiated for. The rest is noise. You return to your primary work.", DimColor);
                                _heirPhase = 5;
                                _heirCountdown = 14;
                                break;

                            case "ignore":
                                Msg("You continue your operations without acknowledging the escort. After ten days they are reassigned. " +
                                    "The matter is tabled but not closed.", DimColor);
                                _heirPhase = 4;
                                _heirCountdown = 21;
                                break;
                        }
                    }
                ), true);
            };
        }

        private static void FireHeirPhase4()
        {
            if (MageKnowledge._deferredInquiry != null) { _heirCountdown = 2; return; }

            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Political Settlement",
                    "The claim has reached a kind of political equilibrium — not resolved, not active, but present in the record. " +
                    "Three lords have privately indicated they would support a formal press of the claim if you chose to make one. " +
                    "Two have privately indicated they would oppose it. " +
                    "The crown has not acted beyond what it has already done. " +
                    "The historian, if he is still protected, is working on a third document. " +
                    "\n\nYou are not the same kind of lord you were before the officer stopped you at the gate.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("press",   "Press the claim formally. This is the moment.", null, true,
                            "Everything that follows will be the consequence of this decision."),
                        new InquiryElement("hold",    "Hold it. Let it sit in the record. Build power first.", null, true,
                            "A claim not yet pressed is still a claim."),
                        new InquiryElement("release", "Release it permanently. You have decided this is not your path.", null, true,
                            "You can choose who you are in this. That is a kind of power too."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen5 =>
                    {
                        switch (chosen5?[0]?.Identifier as string)
                        {
                            case "press":
                                ShiftTrait(DefaultTraits.Valor, 2);
                                ChangeRenown(80f);
                                ChangeRelWithRandomLord(-20);
                                Msg("The formal press of the claim begins. This is a long road with an uncertain end. " +
                                    "What it means for your clan, your allies, and your enemies is something that will unfold over years. " +
                                    "The historian records the date. He says it is the right one.", DimColor);
                                break;

                            case "hold":
                                ShiftTrait(DefaultTraits.Cunning, 2);
                                ChangeRenown(35f);
                                Msg("The claim sits in the archive. The three supporting lords know you are aware of their position. " +
                                    "The crown knows you have not moved yet. Everyone is watching a card that has not been played. " +
                                    "That, for now, is exactly where you want it.", DimColor);
                                break;

                            case "release":
                                ShiftTrait(DefaultTraits.Honor, 2);
                                ChangeRenown(50f);
                                Msg("You release the claim with a public statement that is precise and unambiguous. " +
                                    "The three supporting lords are surprised. The crown is relieved. The historian records that you had a choice and made it. " +
                                    "You go back to the campaign. The kind of lord you are is now in the record too.", GoodColor);
                                break;
                        }
                        _heirPhase = 5;
                        _heirCountdown = 14;
                    }
                ), true);
            };
        }

        private static void FireHeirPhase5()
        {
            if (MageKnowledge._deferredInquiry != null) { _heirCountdown = 2; return; }

            MageKnowledge._deferredInquiry = () =>
            {
                string legacy = _heirSuppressed
                    ? "The historian's final document is a short monograph on how succession gaps are created and maintained. It names no names. It draws no conclusions. It is exactly as dangerous as its readers need it to be."
                    : "The historian sends you a copy of his final document. It is a history of the gap in the succession, factual and careful. Your name appears in it, correctly. What you did with the claim is recorded. That will not change.";

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Record",
                    legacy + "\n\nThe officer who came to you at the gate is now working at an archive in a quiet city. He sent a note: 'I thought you should know.'",
                    new List<InquiryElement>
                    {
                        new InquiryElement("close", "Close this chapter. The record stands.", null, true,
                            "Whatever the historian writes, you are still the one who decided."),
                    },
                    false, 1, 1, "Continue", "",
                    _ =>
                    {
                        if (_heirHistorianSafe)
                        {
                            ChangeRenown(20f);
                            Msg("The historian is safe. The document exists. The choice you made is in the record. That is the only kind of permanence this world offers.", GoodColor);
                        }
                        else
                        {
                            Msg("The record is what it is. You made choices. They are written down now. This is the kind of history that doesn't need you to agree with it.", DimColor);
                        }
                        _heirPhase = 0;
                    }
                ), true);
            };
        }
    }
}
