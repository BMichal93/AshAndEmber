// =============================================================================
// ASH AND EMBER — DragonQuestSystem.Quest.cs
// Temple contact, lord stories, mage lord stories, Temple letters, final prompt.
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
        // ── First contact (initial trigger) ───────────────────────────────────
        private static void ShowTempleContact()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "The Figure on the Field",

                    "The battle is not yet cold. You are walking among the dead when you notice " +
                    "someone who does not belong there — sitting upright, back against a fallen horse, " +
                    "watching the last of the smoke.\n\n" +
                    "Not wounded. Not Ashen either — there is no grey about them. " +
                    "Just spent, the way a fire looks in its last minutes before the light gives out. " +
                    "Old burn scars on their hands, healed long ago into white ridges.\n\n" +
                    "They do not move until you are close.\n\n" +
                    "\"I was wondering when you would come through,\" they say. Not quite to you. To the moment. " +
                    "\"You killed one of the march's lords. A cold ember is already in you — " +
                    "you probably cannot feel it yet. Most do not.\"\n\n" +
                    "They look at their hands for a moment, then back at the field.\n\n" +
                    "\"There is a Binding that needs doing. You have already begun collecting what it requires. " +
                    "Cold embers from the march's lords. Warm ones from mages who kept their fire to the end. " +
                    "You will need six cold, five warm — and three of their strongholds taken, " +
                    "four of their ruin sites read and understood.\n\n" +
                    "I will find you again when the count changes. I always do.\"\n\n" +
                    "They stand. Very still when they do it, the way someone moves when they have " +
                    "long since forgotten how to hurry.\n\n" +
                    "They walk. Not toward anything visible. " +
                    "By the time you think to call after them, they are gone.\n\n" +
                    "You did not get their name. They did not offer it.",

                    true, true,
                    "I will keep going.",
                    "I want nothing to do with this.",

                    () =>
                    {
                        _phase = PhaseActive;
                        try { _questLog = new DragonQuestLog(); _questLog.StartQuest(); _questLog.LogStarted(); } catch { }
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Quest added: The Silence Between Fires.",
                            new Color(0.75f, 0.55f, 0.3f)));
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"Objectives: claim {TargetLordsSlain} cold embers (Ashen lords)  ·  " +
                            $"{TargetMageLordsSlain} warm embers (mage lords)  ·  " +
                            $"{TargetCitiesTaken} Ashen strongholds  ·  " +
                            $"{TargetRuinsCleared} Ashen ruins.",
                            new Color(0.65f, 0.50f, 0.25f)));
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Check progress in the Grimoire (Alt+X).",
                            new Color(0.60f, 0.50f, 0.25f)));
                    },
                    () =>
                    {
                        _phase = PhaseRefused;
                        InformationManager.DisplayMessage(new InformationMessage(
                            "You walk away. Whatever they were waiting for, it will have to wait longer.",
                            new Color(0.5f, 0.5f, 0.55f)));
                    }
                ), true, true);
            }
            catch { }
        }

        // ── Succession contact (fires when the main hero changes mid-quest) ──
        private static void ShowSuccessionContact()
        {
            try
            {
                // Recreate the quest log under the new hero before showing the popup,
                // so the journal reflects the new bearer from this moment forward.
                try
                {
                    _questLog = new DragonQuestLog();
                    _questLog.StartQuest();
                    _questLog.LogStarted();
                    _questLog.UpdateProgress(_lordsSlain, _mageLordsSlain, _citiesTaken, AshenRuinSystem.ClearedCount);
                }
                catch { }

                string ordinal = GetOrdinal(_generation);
                bool firstHandoff = _generation == 2;

                string title = firstHandoff ? "The Burden Passes" : "The Burden Passes Again";

                string body = firstHandoff
                    ? "A figure at your campfire. You do not hear them arrive.\n\n" +
                      "The same burn-scarred hands. The same stillness — the kind that comes from " +
                      "having nothing left to be restless about.\n\n" +
                      "\"Second bearer,\" they say. \"I counted the transition.\"\n\n" +
                      "They do not explain how they found you. They do not seem to think it requires explanation.\n\n" +
                      "\"Embers do not recognise mortality as a reason to move. " +
                      "Everything your predecessor gathered — the cold, the warm, all of it — " +
                      "has been yours since the moment their fire went out. " +
                      "You have been carrying it without feeling the weight.\n\n" +
                      "The count stands. The altar is still there. " +
                      "It has waited longer than your bloodline has existed.\n\n" +
                      "I will find you as I found them. What they started, you carry forward.\"\n\n" +
                      "They stand and walk into the dark without looking back.\n\n" +
                      "You did not ask their name this time either."
                    : $"A figure at your fire again. You are starting to recognise the stillness before you see the face.\n\n" +
                      $"\"The {ordinal} bearer,\" they say. Nothing else for a moment.\n\n" +
                      "The brevity is its own message. They have done this enough times " +
                      "that ceremony has worn away and only fact remains.\n\n" +
                      "\"The count stands. The embers are yours. The altar waits.\n\n" +
                      "The cycle does not care whose hands hold what it needs — only that those hands hold it. " +
                      "You are those hands now.\"\n\n" +
                      "They are gone before the fire shifts.";

                InformationManager.ShowInquiry(new InquiryData(
                    title,
                    body,
                    true, false,
                    "I carry it.",
                    "",
                    () =>
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            firstHandoff
                                ? "The burden passes. The embers do not mourn."
                                : $"The {ordinal} bearer takes up the count. The altar is still waiting.",
                            new Color(0.70f, 0.55f, 0.35f)));
                    },
                    () => { }
                ), true, true);
            }
            catch { }
        }

        // ── Ashen lord stories (one per kill, in order) ───────────────────────
        private static void ShowLordStory(int storyIndex)
        {
            try
            {
                switch (storyIndex)
                {
                    case 0: ShowLordStory_Warden();    break;
                    case 1: ShowLordStory_Scholar();   break;
                    case 2: ShowLordStory_Father();    break;
                    case 3: ShowLordStory_Tablets();   break;
                    case 4: ShowLordStory_Candidate(); break;
                    case 5: ShowLordStory_Letter();    break;
                    default: break;
                }
            }
            catch { }
        }

        // Story 1 — The Warden's Post (after 1st Ashen kill)
        private static void ShowLordStory_Warden()
        {
            InformationManager.ShowInquiry(new InquiryData(
                "What the Border Left Behind",

                "Camp records, found in the ruins behind the battle site. " +
                "Thirty-one years of ledger entries from the northern pass — " +
                "supply counts, patrol tallies, request dispatches.\n\n" +
                "Relief armies are marked as \"en route\" in year three. Year five. Year nine. " +
                "Each one diverted. Each one explained in a margin note by a different hand: " +
                "*campaign requirements*, *strategic necessity*, *the pass can hold*.\n\n" +
                "The last entry in the warden's own writing is dated two years before the Empire collapsed:\n\n" +
                "\"If no one is coming, then no one is coming. The pass is cold tonight. " +
                "There is something in the cold that is more patient than I am. " +
                "I think I am going to let it in.\"\n\n" +
                "Then nothing. Then a different hand — neater, colder — " +
                "continuing the supply records without interruption. For three more years.",

                true, true,
                "I want to know who he was.",
                "The dead don't need me to witness them.",

                () =>
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "He held the northern pass for thirty-one years. He is not in the Temple's records of the fallen — " +
                        "only in the supply ledgers, as a recurring cost the campaign could not justify resupplying.",
                        new Color(0.70f, 0.55f, 0.35f)));
                },
                () => { }
            ), true, true);
        }

        // Story 2 — The Scholar's Notes (after 2nd Ashen kill)
        private static void ShowLordStory_Scholar()
        {
            InformationManager.ShowInquiry(new InquiryData(
                "The Notes They Didn't Burn",

                "A sealed dispatch case, found beneath the battle site. " +
                "Inside: correspondence from a Temple scholar to the High Templar, " +
                "dated eight years ago.\n\n" +
                "She had been studying conversion records — " +
                "the accounts of those who had become Ashen — " +
                "looking for a pattern of weakness, of failure, of fear.\n\n" +
                "She found something else.\n\n" +
                "\"Every record I have examined shows the same moment,\" she writes. " +
                "\"Not despair. Not surrender. A kind of clarity. " +
                "They chose the cold with their eyes open, at the moment they understood something " +
                "the warmth could not answer for them. " +
                "The question we should be asking is not what broke them. " +
                "It is what they saw.\"\n\n" +
                "The last letter in the case has no salutation and no closing. " +
                "Only: *what the fire cannot promise, the cold delivers.*\n\n" +
                "Stamped on the outside in red: FILE AND SEAL.\n\n" +
                "Her name appears on no current Temple roster.",

                true, true,
                "Keep it. Someone should know this.",
                "Burn it. Whatever she found, it's still the cold.",

                () =>
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The correspondence goes into your kit. You are not sure what to do with it. " +
                        "That feels appropriate.",
                        new Color(0.65f, 0.55f, 0.40f)));
                },
                () =>
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The pages burn well. Whatever she knew dies with them, again.",
                        new Color(0.50f, 0.50f, 0.55f)));
                }
            ), true, true);
        }

        // Story 3 — The Father's Trail (after 3rd Ashen kill)
        private static void ShowLordStory_Father()
        {
            InformationManager.ShowInquiry(new InquiryData(
                "Two Sets of Tracks",

                "In the ruins near the battle site: a child's camp.\n\n" +
                "Small things. A carved horse, worn smooth. Blankets, maintained. " +
                "A journal written by the lord you just killed — in his warmth, " +
                "in a hand that grew steadily colder across its pages.\n\n" +
                "The early entries follow his son, who walked north twelve years ago. " +
                "Fourteen at the time. The entries are not angry. They are precise — " +
                "distances covered, bearings taken, what the boy ate and when.\n\n" +
                "\"He is cold now. I have watched him for three years. " +
                "He does not know I am here. He does not know anything the way he used to know things. " +
                "I cannot reach him. I cannot leave him. " +
                "The cold that took him has not taken me because I am not willing and I am not afraid. " +
                "I am only here.\"\n\n" +
                "The last entry, written in the neat cold hand:\n\n" +
                "\"I am still here.\"",

                true, false,
                "Leave the camp as it is.",
                "",
                () => { },
                () => { }
            ), true, true);
        }

        // Story 4 — The Founding Tablets (after 4th Ashen kill)
        private static void ShowLordStory_Tablets()
        {
            InformationManager.ShowInquiry(new InquiryData(
                "Before Your Clan Had a Name",

                "Beneath the ruins, in a sealed lower chamber older than the structure above it: " +
                "stone tablets.\n\n" +
                "Pre-Empire. Pre-calendar. Written in an old form but legible. " +
                "They describe a ritual — twelve mages who gave their fire at once to stop a cold march.\n\n" +
                "The tablets are not celebratory. They are clinical. " +
                "Each of the twelve is named. " +
                "What happened to them is described in a single sentence per person, " +
                "all of them ending the same way.\n\n" +
                "What happened to the march is described in more detail — " +
                "settlement by settlement, the grey retreating, the cold losing its hold, " +
                "the fires returning where there had been none.\n\n" +
                "The final tablet is shorter than the others. It reads:\n\n" +
                "\"The fires will accumulate. The cold will find them. " +
                "Someone will be afraid again. The march will begin again. " +
                "We have sealed this record so that when it does, " +
                "the next twelve will know they are not the first, and will not be the last, " +
                "and did not fail by being insufficient — only by being mortal.\"\n\n" +
                "The wax of the seal is the same colour as the Temple's current crest.",

                true, false,
                "I have read this.",
                "",
                () =>
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "Someone has always known what this was building toward. " +
                        "The marks they leave are starting to make sense.",
                        new Color(0.65f, 0.50f, 0.30f)));
                },
                () => { }
            ), true, true);
        }

        // Story 5 — The Temple Candidate (after 5th Ashen kill)
        private static void ShowLordStory_Candidate()
        {
            InformationManager.ShowInquiry(new InquiryData(
                "The Seat That Went to Someone Else",

                "Among the effects of the Ashen lord you just killed: Temple documents. Old ones.\n\n" +
                "A candidate record — her name, her assessed fire-strength, the date she was offered " +
                "a place in the Binding. And her refusal. Dated. Signed.\n\n" +
                "A second invitation. Also refused.\n\n" +
                "A third invitation, prepared but never sent. " +
                "Below it, a conversion record. The dates match the gap between the second refusal " +
                "and when the third invitation was drafted.\n\n" +
                "The figure you have seen before is already at the field's edge when you look up. " +
                "They look at the documents for a long time and do not take them from you.\n\n" +
                "\"She was supposed to be one of the eleven,\" they say finally. " +
                "\"Her seat went to someone else. The eleven were never told where it came from.\"\n\n" +
                "The cold ember you carry from this battle is different from the others. " +
                "It has the faint warmth of something that was almost something else.",

                true, true,
                "What happened to the seat?",
                "It doesn't change what she became.",

                () =>
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The rider is quiet for a moment. \"Someone filled it. They do not know " +
                        "the seat had a name on it before theirs.\"",
                        new Color(0.65f, 0.50f, 0.35f)));
                },
                () =>
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The rider nods slowly. \"No. It doesn't.\"",
                        new Color(0.55f, 0.50f, 0.55f)));
                }
            ), true, true);
        }

        // Story 6 — The Last Lord's Letter (after 6th and final Ashen kill)
        private static void ShowLordStory_Letter()
        {
            InformationManager.ShowInquiry(new InquiryData(
                "What the Last One Carried",

                "The Ashen lord you just killed was carrying a sealed letter.\n\n" +
                "The address reads: *to whoever kills me last.*\n\n" +
                "\"By the time you find this, you have already been recruited. " +
                "They have already told you about the Binding — the twelve fires, the one great ritual, " +
                "the cycle interrupted.\n\n" +
                "They have not told you everything.\n\n" +
                "The Binding needs mage-fire. Twelve flames given at once. That much is true. " +
                "But it also needs what the march carries — the cold fire that the Ashen have been accumulating. " +
                "Both sides. Balanced. The way the first tablets describe it.\n\n" +
                "The march does not happen by accident. " +
                "The cold moves toward the ritual site the way the tide moves toward the shore — " +
                "drawn to something it recognises. " +
                "We are not trying to destroy. We are trying to arrive.\n\n" +
                "I am telling you this because you deserve to know what you are part of " +
                "before whoever guides you finishes explaining it. " +
                "The last Binding used twelve mages and twelve of us. " +
                "The twelve mages knew. The twelve of us did not.\n\n" +
                "Now you do. Whether you proceed is your affair.\n\n" +
                "The cycle continues regardless.\"",

                true, true,
                "This changes what I thought I was doing.",
                "It doesn't matter. The Binding still stops the march.",

                () =>
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "You sit with the letter for a long time. " +
                        "The final summons is waiting. You already know what it will say.",
                        new Color(0.65f, 0.55f, 0.40f)));
                },
                () =>
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The march stops. That is the thing it stops. " +
                        "Whatever it costs, that is what it costs.",
                        new Color(0.70f, 0.55f, 0.35f)));
                }
            ), true, true);
        }

        // ── Mage lord stories (first 3 kills, in order) ───────────────────────
        private static void ShowMageLordStory(int storyIndex)
        {
            try
            {
                switch (storyIndex)
                {
                    case 0: ShowMageLordStory_Rider();        break;
                    case 1: ShowMageLordStory_Cartographer(); break;
                    case 2: ShowMageLordStory_Letter();       break;
                    default: break;
                }
            }
            catch { }
        }

        // Mage story 1 — The Rider Explains (after 1st mage lord kill)
        private static void ShowMageLordStory_Rider()
        {
            InformationManager.ShowInquiry(new InquiryData(
                "What You Have Been Carrying",

                "The battle is not yet cold when you notice the figure standing at the field edge. " +
                "The same stillness. The same burn-scarred hands.\n\n" +
                "They do not ask about the fight. They ask you to hold out your hand.\n\n" +
                "You feel something settle against your palm — invisible, faintly warm, " +
                "the specific warmth of something that recently burned a great deal.\n\n" +
                "\"A warm ember. You have been carrying cold ones for months and may not have noticed. " +
                "They announce themselves differently — grey weight, a heaviness that is not quite physical. " +
                "This one is warm. Brighter. You will feel the difference.\"\n\n" +
                "They produce a small iron vessel and hold it open. " +
                "Inside, a faint grey glow — cold embers, they explain, from the Ashen lords " +
                "you have already killed. The vessel registers them.\n\n" +
                "\"The Binding needs both in balance. You are doing what was needed " +
                "without knowing it. Keep going.\"\n\n" +
                "They walk away before you can ask who they are.",

                true, true,
                "What happens to the embers at the Altar?",
                "I understand.",

                () =>
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "\"They are given,\" she says without turning back. " +
                        "\"That is all anyone has ever been able to say about it precisely.\"",
                        new Color(0.75f, 0.65f, 0.40f)));
                },
                () => { }
            ), true, true);
        }

        // Mage story 2 — The Cartographer's Map (after 2nd mage lord kill)
        private static void ShowMageLordStory_Cartographer()
        {
            InformationManager.ShowInquiry(new InquiryData(
                "The Map He Made",

                "Found among the mage lord's effects: a map. Not of roads or territories.\n\n" +
                "Two kinds of marks, in grey and gold. Grey: Ashen lords, with dates beside each name. " +
                "Every date you recognise. Every battle you led.\n\n" +
                "Gold: mage lords. Some crossed out. Some crossed out in the last week. " +
                "The marks converge — every line, grey and gold alike, " +
                "drawing toward a single point on the map's edge. " +
                "The High Altar site.\n\n" +
                "The cartographer knew. He mapped the ember-flow the way you might map a river — " +
                "following what it was drawn toward, tracing the current back to its source.\n\n" +
                "Your name is not on the map. At the convergence point, where all the lines meet, " +
                "there is only a small mark in a third colour the map's legend does not explain.\n\n" +
                "The colour is the same as the Temple's seal.",

                true, false,
                "I have read this.",
                "",
                () =>
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The map goes into your kit. You are somewhere on it, even if you cannot see where.",
                        new Color(0.65f, 0.55f, 0.40f)));
                },
                () => { }
            ), true, true);
        }

        // Mage story 3 — The Letter He Left (after 3rd mage lord kill)
        private static void ShowMageLordStory_Letter()
        {
            InformationManager.ShowInquiry(new InquiryData(
                "The Letter He Left",

                "The mage lord you killed was carrying a sealed letter.\n\n" +
                "Addressed: *to whoever is collecting.*\n\n" +
                "\"I have been watching the tally. I know what the Temple is building, " +
                "and I have known for some years that I would be part of it " +
                "whether I intended to be or not.\n\n" +
                "I could not give what I carry willingly — not the way the Temple's eleven gave theirs, " +
                "with ceremony and understanding. That kind of surrender requires a patience I never had. " +
                "But I can give it to someone who earned it by fire. " +
                "That is a different thing. It burns cleaner.\n\n" +
                "If you are reading this, you have taken what I carried. " +
                "The warm ember does not grieve. It only burns.\n\n" +
                "Use it well. The cold has been patient for a very long time. " +
                "It is almost time for something to be patient back.\"\n\n" +
                "No signature. On the back, in a different hand and fresh ink:\n\n" +
                "*He has always been on the list. He knew.*\n" +
                "— The Order.",

                true, true,
                "He chose this.",
                "He had no choice. None of us do.",

                () =>
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The letter goes into your kit alongside the cartographer's map. " +
                        "You are building something. Someone else drew the plans long ago.",
                        new Color(0.70f, 0.60f, 0.40f)));
                },
                () =>
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "You fold the letter and keep it anyway. Chosen or not, it matters that someone " +
                        "understood what they were part of.",
                        new Color(0.60f, 0.55f, 0.50f)));
                }
            ), true, true);
        }

        // ── The Vigil's words (six, delivered on progress gates) ─────────────
        // Presented as words found burned at camp, scratched into stone at ruin sites,
        // or spoken by a figure glimpsed briefly at dusk — never formal letters.
        private static void ShowTempleLetterByIndex(int idx)
        {
            try
            {
                string[] titles =
                {
                    "Words at the Campfire",      // 0 — 14 days after contact
                    "Left at the Ruins",           // 1 — after 1st Ashen kill
                    "Scratched Into the Stone",    // 2 — after 2nd Ashen kill
                    "At the Field Edge",           // 3 — after 1st mage lord kill
                    "A Figure at Dusk",            // 4 — after 4th Ashen kill
                    "Come to the Altar",           // 5 — all objectives met (PhaseAllDone)
                };
                string[] bodies =
                {
                    // 0 — ember lore, burned into the wood of the campfire surround
                    "You find words burned into the wood of your fire surround. " +
                    "Not carved — burned, letter by letter, as if by a careful fingertip.\n\n" +
                    "\"Every mage of true power carries an ember at their core. " +
                    "Most never know. Most die without learning what it becomes.\n\n" +
                    "An ember is not a metaphor. It is a dense compression of fire-essence — " +
                    "years of working and casting and carrying the gift, crystallised into something " +
                    "that does not disperse when the body it lived in stops burning. " +
                    "It moves. It goes to whoever was burning brightest nearby.\n\n" +
                    "You have been collecting them without knowing it. Cold ones from the Ashen march. " +
                    "The Binding requires both — cold and warm, in balance. " +
                    "There are old tablets that describe the ratio. I have them memorised.\n\n" +
                    "You are already a collector. You were, from the moment you led that first battle.\"",

                    // 1 — the warden's story, left at the site of the battle
                    "Words scratched into a stone near the site of the battle. " +
                    "Small, precise, unhurried — as if whoever left them had all the time in the world.\n\n" +
                    "\"The lord you killed at the northern pass held that ground " +
                    "longer than most of the wars you have fought in lasted. " +
                    "He waited for relief. It kept being redirected. By the time anyone turned north, " +
                    "the warmth had run out.\n\n" +
                    "What you took from him was a cold ember — thirty-one years of accumulated chill, " +
                    "compressed into something that barely weighs anything and will not warm under flame.\n\n" +
                    "I am not telling you this to soften what you did. " +
                    "I am telling you so you understand what the march is made of. " +
                    "And what the Binding will need to hold against it.\"",

                    // 2 — the scholar, cold embers burn clean in the Binding
                    "Words burned into the lintel of a ruin doorway, at eye level, " +
                    "as if left for someone who knew to look.\n\n" +
                    "\"You may have found her notes. " +
                    "A scholar who spent years studying those who had turned — " +
                    "looking for weakness, for failure. She found something else.\n\n" +
                    "The cold does not take people who have given up. " +
                    "It takes people who have seen clearly and found the warmth insufficient. " +
                    "What it leaves behind burns cold and precise and completely without regret.\n\n" +
                    "Those embers burn cleaner in the Binding than the warm ones do. " +
                    "I have seen it. The ritual requires both — not as a cost. As a component. " +
                    "The cycle is not broken by warmth alone.\"",

                    // 3 — first warm ember; The Vigil speaks briefly in person at the field edge
                    "A figure at the field edge after the battle. You recognise the stillness " +
                    "before you recognise the face.\n\n" +
                    "They do not approach. They speak across the distance, and the words carry.\n\n" +
                    "\"The warm ember you took from that mage lord is different from what I expected. " +
                    "I have seen warm embers taken by force before — they burn unevenly, " +
                    "resist the vessel. Yours does not.\n\n" +
                    "I do not know what that means yet. I have been trying to work it out " +
                    "since before you arrived.\n\n" +
                    "Keep gathering. I will have an answer before the altar is ready.\"\n\n" +
                    "By the time you cross the field toward them, they are already gone.",

                    // 4 — the twelve, the specific warmth required; The Vigil at dusk
                    "A figure at the edge of your camp at dusk — only there long enough " +
                    "to say what they came to say.\n\n" +
                    "\"There is enough cold fire. There has been for some time — " +
                    "the march has been generous in that regard.\n\n" +
                    "What the Binding has always needed that past cycles could not provide: " +
                    "a warm fire that has been tested. That has walked through the cold and returned. " +
                    "That has gathered both kinds of ember and is still burning.\n\n" +
                    "The ritual needs twelve. Eleven have been assembled over many years — " +
                    "people who kept their fire in protected places, never crossed what you have crossed. " +
                    "They are real fires. But they have not been where you have been.\n\n" +
                    "I have been looking for the twelfth for a very long time.\"\n\n" +
                    "They walk into the dusk. You do not follow.",

                    // 5 — altar summons; words found carved into the earth near camp, unmistakable
                    "Words carved into the earth beside where you slept — " +
                    "too precise to be accidental, too deliberate to be anything other than a summons.\n\n" +
                    "\"Come to the High Altar.\n\n" +
                    "You will not need to bring anything. The Binding will find what you carry — " +
                    "the cold embers and the warm, everything accumulated since the first battle. " +
                    "It is already part of you. It has been since the beginning.\n\n" +
                    "There are things I have not told you. About what happens at the altar. " +
                    "About what the cold brings when it arrives at the site. " +
                    "About what it costs the Ashen, and why they keep coming anyway.\n\n" +
                    "The last lord you killed knew. They always know, by the end. " +
                    "Whatever they told you — hear me whole before you decide.\n\n" +
                    "You have earned the truth, not just the version that recruits.\"",
                };

                if (idx < 0 || idx >= titles.Length) return;

                InformationManager.ShowInquiry(new InquiryData(
                    titles[idx],
                    bodies[idx],
                    true, false,
                    "I have read this.",
                    "",
                    () => { },
                    () => { }
                ), true, true);
            }
            catch { }
        }

        // ── Final prompt ──────────────────────────────────────────────────────
        private static void ShowFinalPrompt()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "The High Altar",

                    "They are already at the altar when you arrive. The figure you have been following " +
                    "across all of this — sitting on the altar steps, the same stillness, " +
                    "the same burn-scarred hands resting on their knees.\n\n" +
                    "Eleven others stand nearby. Not soldiers. Mages, by the look of them — " +
                    "old fires, assembled over years. They do not speak.\n\n" +
                    "\"The Binding needs twelve mage-fires,\" the figure says. Not to the eleven. To you. " +
                    "\"You already know that part. What I have not told you: " +
                    "it also needs the cold fire the march carries. Both sides, balanced. " +
                    "The embers you have gathered are already part of the ritual — " +
                    "they have been since the first battle. The Ashen move toward this site " +
                    "the way the tide moves toward shore. " +
                    "They come because something in the cold recognises what this is. " +
                    "They are not victims. They are participants. The cold fire answering the warm.\"\n\n" +
                    "They stand. The same slow, deliberate movement.\n\n" +
                    "\"The ritual will kill us. The Ashen who arrive will be consumed by it. " +
                    "Every ember — cold and warm — will be spent in the moment. " +
                    "What remains is a world with more time. " +
                    "A generation, perhaps two, before the next cycle begins. " +
                    "The grey retreats. The march stops. " +
                    "Somewhere the first new fire kindles that will not know what it cost.\"\n\n" +
                    "You think of the warden at his post. The scholar's sealed notes. " +
                    "The father circling a son he could not reach. " +
                    "The cartographer's map with its unlabelled centre. " +
                    "The tablets sealed for the next twelve.\n\n" +
                    "\"I have done this before,\" they say quietly. " +
                    "\"The last time, the twelfth did not come. The ritual failed. " +
                    "The cycle continued. I have been here since.\"\n\n" +
                    "They look at you with eyes the colour of old ash.\n\n" +
                    "\"There is no version of this that ends the cycle. " +
                    "We are not offering a solution. We are offering an interruption. " +
                    "If that is not enough — say so now. I will not argue with you.\"\n\n" +
                    "(Choosing to proceed ENDS YOUR CAMPAIGN — your hero dies as part of the Binding. " +
                    "Refusing closes the quest forever; the campaign continues.)",

                    true, true,
                    "Begin it. Give the world more time.",
                    "I will not be fuel for something that cannot end.",

                    () =>
                    {
                        _phase       = PhaseAccepted;
                        _endingPhase = 1;
                        try { _questLog?.LogComplete(); } catch { }
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The Binding begins. There is no taking it back.",
                            new Color(0.90f, 0.65f, 0.20f)));
                    },
                    () =>
                    {
                        _phase = PhaseRefused;
                        try { _questLog?.LogRefused(); } catch { }
                        InformationManager.DisplayMessage(new InformationMessage(
                            "They accept it without recrimination. " +
                            "\"The last twelfth said the same. The cycle continued. It always does.\" " +
                            "You are free to go.",
                            new Color(0.55f, 0.55f, 0.60f)));
                    }
                ), true, true);
            }
            catch { }
        }

        // ── Ending sequence ───────────────────────────────────────────────────
        // Phase 1: set worldBound, kill Ashen lords (up to 5)
        // Phase 2: finish Ashen lords (up to 20), kill mage lords (up to 5)
        // Phase 3: finish mage lords (up to 20), kill mage companions, redistribute (up to 3)
        // Phase 4: finish redistribution, queue final dialog
        private static void TickEnding()
        {
            try
            {
                switch (_endingPhase)
                {
                    case 1:
                        _worldBound = true;
                        KillAshenLords(5);
                        _endingPhase = 2;
                        break;

                    case 2:
                        KillAshenLords(20);
                        KillMageLords(5);
                        _endingPhase = 3;
                        break;

                    case 3:
                        KillMageLords(20);
                        KillMageCompanions();
                        RedistributeAshenSettlements(3);
                        _endingPhase = 4;
                        break;

                    case 4:
                        RedistributeAshenSettlements(20);
                        _endingPhase = 5;
                        if (MageKnowledge._deferredInquiry == null)
                            MageKnowledge._deferredInquiry = ShowEndingDialog;
                        break;
                }
            }
            catch { }
        }

        private static void KillAshenLords(int cap)
        {
            int killed = 0;
            try
            {
                foreach (Hero h in Hero.AllAliveHeroes.ToList())
                {
                    if (killed >= cap) break;
                    if (!h.IsAlive || h.IsChild || h == Hero.MainHero) continue;
                    if (!ColourLordRegistry.IsAshenLord(h)) continue;
                    try { KillCharacterAction.ApplyByMurder(h, null, false); killed++; } catch { }
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
                    try { KillCharacterAction.ApplyByMurder(h, null, false); killed++; } catch { }
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
                    try { KillCharacterAction.ApplyByMurder(companion, null, false); } catch { }
                }
            }
            catch { }
        }

        private static void RedistributeAshenSettlements(int cap)
        {
            int moved = 0;
            try
            {
                var kingdoms = Kingdom.All
                    .Where(k => !k.IsEliminated && k.StringId != AshenKingdomId)
                    .ToList();
                if (kingdoms.Count == 0) return;

                foreach (Settlement s in Settlement.All.ToList())
                {
                    if (moved >= cap) break;
                    if (s.MapFaction?.StringId != AshenKingdomId) continue;
                    if (s.IsUnderSiege) continue;

                    var target = kingdoms[_rng.Next(kingdoms.Count)];
                    Hero lord = target.Leader ?? target.RulingClan?.Leader;
                    if (lord == null) continue;
                    try
                    {
                        ChangeOwnerOfSettlementAction.ApplyByDefault(lord, s);
                        try { if (s.Town != null) { s.Town.Loyalty = 100f; s.Town.Security = 100f; } } catch { }
                        moved++;
                    }
                    catch { }
                }
            }
            catch { }
        }

    }
}
