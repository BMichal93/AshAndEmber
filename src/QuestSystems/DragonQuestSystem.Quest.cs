// =============================================================================
// ASH AND EMBER — DragonQuestSystem.Quest.cs
// Temple contact, lord stories, Temple letters, final prompt, ending sequence.
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
        // ── Temple contact (initial trigger) ──────────────────────────────────
        private static void ShowTempleContact()
        {
            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "The Rider Without Colours",

                    "A grey-robed rider falls into step beside you as you enter the market. " +
                    "No banner. No Temple insignia. Just someone who knows where you are going " +
                    "and matches your pace.\n\n" +
                    "\"I have been asked to find you. Not to recruit you — " +
                    "you are not the kind of fire that answers to a summons. To inform you.\"\n\n" +
                    "She hands you a sealed letter without stopping. Her horse doesn't break stride. " +
                    "By the time you turn to ask, she is already through the gate.\n\n" +
                    "The letter has no header. It reads:\n\n" +
                    "\"There is a plan. You have already started it without knowing it — " +
                    "every Ashen lord you push back, every ruin you walk into, " +
                    "every city you pull out of the grey march. Keep going. We will find you again.\"\n\n" +
                    "No signature.",

                    true, true,
                    "Keep going.",
                    "I want nothing to do with this.",

                    () =>
                    {
                        _phase = PhaseActive;
                        try { _questLog = new DragonQuestLog(); _questLog.StartQuest(); _questLog.LogStarted(); } catch { }
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Quest added: The Silence Between Fires.",
                            new Color(0.75f, 0.55f, 0.3f)));
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"Objectives: silence {TargetLordsSlain} Ashen lords  ·  " +
                            $"claim {TargetCitiesTaken} Ashen strongholds  ·  " +
                            $"clear {TargetRuinsCleared} Ashen ruins.",
                            new Color(0.65f, 0.50f, 0.25f)));
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Check progress in the Grimoire (Alt+X).",
                            new Color(0.60f, 0.50f, 0.25f)));
                    },
                    () =>
                    {
                        _phase = PhaseRefused;
                        InformationManager.DisplayMessage(new InformationMessage(
                            "The letter goes into the fire unread. Whatever they were offering dies with it.",
                            new Color(0.5f, 0.5f, 0.55f)));
                    }
                ), true, true);
            }
            catch { }
        }

        // ── Lord stories (one per kill, in order) ────────────────────────────
        // storyIndex 0-4 maps to the five lord archetypes encountered in sequence.
        private static void ShowLordStory(int storyIndex)
        {
            try
            {
                switch (storyIndex)
                {
                    case 0: ShowLordStory_Warden();  break;
                    case 1: ShowLordStory_Scholar(); break;
                    case 2: ShowLordStory_Father();  break;
                    case 3: ShowLordStory_Tablets(); break;
                    case 4: ShowLordStory_Letter();  break;
                    default: break;
                }
            }
            catch { }
        }

        // Story 1 — The Warden's Post (after 1st kill)
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

        // Story 2 — The Scholar's Notes (after 2nd kill)
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

        // Story 3 — The Father's Trail (after 3rd kill)
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

        // Story 4 — The Founding Tablets (after 4th kill)
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
                        "The Temple has always known what it was building toward. " +
                        "The letters are starting to make sense.",
                        new Color(0.65f, 0.50f, 0.30f)));
                },
                () => { }
            ), true, true);
        }

        // Story 5 — The Last Lord's Letter (after 5th kill)
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
                "before the Temple finishes explaining it to you. " +
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
                        "The Temple's final message is waiting. You already know what it will say.",
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

        // ── Temple letters (six, delivered on progress gates) ─────────────────
        private static void ShowTempleLetterByIndex(int idx)
        {
            try
            {
                string[] titles =
                {
                    "The First Letter",           // 0 — 14 days after contact
                    "The Second Letter",          // 1 — after 1st kill
                    "The Third Letter",           // 2 — after 2nd kill
                    "The Fourth Letter",          // 3 — after 3rd kill
                    "The Fifth Letter",           // 4 — after 4th kill
                    "The Sixth Letter",           // 5 — after 5th kill
                };
                string[] bodies =
                {
                    // Letter 0
                    "\"A fire like yours reaches us even through the noise of the grey march. " +
                    "We have been watching you. The Ashen lose ground where you go — for now, " +
                    "which is longer than anyone else is managing.\n\n" +
                    "Strike well. We have nothing yet to offer you except that when we do, we will. " +
                    "Watch for our riders.\"\n\n" +
                    "— The Order of the Last Flame",

                    // Letter 1
                    "\"The soldier at Aldscroft held the northern pass longer than any of the wars " +
                    "you have fought in lasted. We promised him relief. " +
                    "Our own campaigns kept us east. By the time we turned north, the warmth had run out.\n\n" +
                    "Whatever he became, he became it waiting for us. " +
                    "We are not telling you this to soften what you did. " +
                    "We are telling you so you understand what the march is made of.\"\n\n" +
                    "— The Order",

                    // Letter 2
                    "\"You may have found her notes. A scholar in our archives — " +
                    "we classified her correspondence eight years ago. She was not wrong. " +
                    "We know this now. We burned the notes because we were not ready to know it.\n\n" +
                    "The cold does not take people who have given up. " +
                    "It takes people who have seen clearly and found the warmth insufficient. " +
                    "We cannot end the cycle. We can only change the terms. " +
                    "There is a plan. We will tell you when you have earned it.\"\n\n" +
                    "— The Order",

                    // Letter 3
                    "\"The Temple was not built to defeat the Ashen. It was built to perform one act — " +
                    "once each cycle, against the grey march — that interrupts it without ending it. " +
                    "The founders knew the cycle would return. " +
                    "The plan has always been the Binding.\n\n" +
                    "We will tell you what that means when you have shown us you can receive it. " +
                    "Two more to go. Hold the road.\"\n\n" +
                    "— The Order",

                    // Letter 4
                    "\"The Binding. Twelve fires given at once, channeled through rites older than the Empire. " +
                    "The cold retreats for a generation — two, perhaps. " +
                    "The world builds. The fires accumulate. Someone is afraid again. The cycle turns.\n\n" +
                    "The Temple rebuilt itself to be the twelve, this cycle. " +
                    "We have eleven fires. We have been waiting for the twelfth.\n\n" +
                    "You understand now why we have been watching your fire.\"\n\n" +
                    "— The Order",

                    // Letter 5
                    "\"Come to the High Altar. " +
                    "There are things we have not told you about the Binding — " +
                    "about what the cold contributes, about what the march is actually moving toward. " +
                    "The last lord you killed may have told you some of it. They always do.\n\n" +
                    "Some of what you find at the Altar will be difficult. " +
                    "Come first. Hear it whole. Then decide.\n\n" +
                    "You have earned the truth, not just the version that recruits.\"\n\n" +
                    "— The Order of the Last Flame",
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

                    "The High Templar does not make it simple.\n\n" +
                    "\"The Binding needs twelve mage-fires. That much we told you. " +
                    "It also needs the cold fire the march carries — " +
                    "both sides, balanced. " +
                    "The Ashen move toward the ritual site the way the tide moves toward shore. " +
                    "They come because something in the cold recognises what this is. " +
                    "They are not victims of the Binding. They are participants.\"\n\n" +
                    "She pauses. Eleven mages stand behind her, assembled over a century. " +
                    "All of them have agreed to this.\n\n" +
                    "\"The ritual will kill us. The Ashen who arrive will be consumed by it. " +
                    "What is left will be a world with more time — " +
                    "a generation, perhaps two, before the next cycle begins. " +
                    "The grey retreats. The march stops. Somewhere the first new fire kindles " +
                    "that will not know what it cost.\"\n\n" +
                    "You think of the warden at his post. The scholar's sealed notes. " +
                    "The father circling a son he could not reach. " +
                    "The tablets sealed for the next twelve.\n\n" +
                    "\"There is no version of this that ends the cycle,\" the High Templar says. " +
                    "\"We are not offering you a solution. " +
                    "We are offering you an interruption. " +
                    "If that is not enough, tell me now. I will not argue with you.\"\n\n" +
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
                            "The High Templar accepts it without recrimination. " +
                            "\"The last one said the same. We will find another way, or we will not.\" " +
                            "You are free to go. The cycle continues.",
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
