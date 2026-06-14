// =============================================================================
// ASH AND EMBER — SettlementEncounters.Events6.cs
// Five multi-phase questline encounters.
//   E_AshenPilgrim    — Leave city/castle, mage, day 60+, 90-day cooldown
//   E_BurnedArchive   — Enter city, day 40+, 60-day cooldown
//   E_QuietVillage    — Enter village, mage, day 80+, 120-day cooldown
//   E_GallowsMage     — Enter city/castle, day 30+, 100-day cooldown
//   EC_TitheCollector — Enter city, day 50+, 90-day cooldown
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
        // THE ASHEN PILGRIM — Leave city/castle, mage, day 60+
        // Phase 0: idle
        // Phase 1: 14-day passage reveal → on fire: set phase 2 (30d return)
        // Phase 2: 30-day return → player offered grimoire or letter → phase 3 (14d letter)
        // Phase 3: 14-day letter resolution
        // Phase 10: 21-day debt collection (from mage-deal path)
        // ═══════════════════════════════════════════════════════════════════

        private static void E_AshenPilgrim(Settlement s)
        {
            bool mage = MageKnowledge.IsMage;
            _pilgrimCooldown = 90;

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Ashen Pilgrim",
                "A man blocks the road at the city gate. His linen wrappings are scorched in patterns that nearly form words. " +
                "He knows your name without being told, and speaks with the unhurried certainty of someone who has already settled the question of whether this meeting was necessary. " +
                "He says he has walked from where the fire ended. He says there is one road still open to him and he needs your seal to take it.",
                new List<InquiryElement>
                {
                    new InquiryElement("give",   "Give him passage. Let him take his road.", null, true,
                        "He accepts without ceremony. Something passes between you that isn't gratitude."),
                    new InquiryElement("ask",    "Ask where he walked from.", null, true,
                        "He will name places. You will recognise them later."),
                    new InquiryElement("quiet",  "Draw him aside. Speak quietly.", null, mage,
                        "He drops the pilgrim entirely when you step away from the guards. He knows you can hear what he carries."),
                    new InquiryElement("refuse", "Refuse. Something is wrong here.", null, true,
                        "He nods. He seems to have expected this answer too."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "give":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("He accepts without ceremony — no bow, no excessive thanks — and walks through the gate with the deliberate economy of someone who has been conserving energy for a long time. " +
                                "You watch him until the crowd takes him. A week later you begin to notice things.", DimColor);
                            _pilgrimPhase = 1;
                            _pilgrimCountdown = 14;
                            break;

                        case "ask":
                            Msg("He names a village. A lord. A date. All three with the precision of someone who memorised them to tell you specifically. " +
                                "You give him passage because the names are real and the dates are wrong in the right direction — all of them before, none of them after. " +
                                "He walks through the gate. You will check the names against what you know.", DimColor);
                            _pilgrimPhase = 1;
                            _pilgrimCountdown = 14;
                            break;

                        case "quiet":
                        {
                            Msg("He drops the act entirely when you step out of earshot. He is an extinguished mage — the fire in him is gone, deliberately spent. " +
                                "He wants to reach a specific settlement before the end of the month. He offers one thing in exchange for your seal and your silence.", DimColor);

                            var lord = Hero.AllAliveHeroes
                                .Where(h => h.IsLord && h.IsAlive && h != Hero.MainHero && !h.IsPrisoner)
                                .OrderBy(_ => _rng.Next()).FirstOrDefault();

                            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                                "✦  The Pilgrim's Offer",
                                "\"I know which of your rivals is carrying a Void-touched relic. I watched them receive it. " +
                                "I can name the lord, the object, and the road it came in on. " +
                                "You give me the seal and say nothing. We part without owing each other anything else.\"",
                                new List<InquiryElement>
                                {
                                    new InquiryElement("deal",   "\"Tell me. You'll have your passage.\"", null, true,
                                        "He names the lord. You will carry this knowledge forward."),
                                    new InquiryElement("relic",  "\"I want the relic, not the name.\"", null, true,
                                        "He doesn't have it. You've misjudged the exchange."),
                                    new InquiryElement("nodeal", "\"No deals. Move on.\"", null, true,
                                        "He leaves without argument. The offer closes."),
                                },
                                false, 1, 1, "Decide", "",
                                inner =>
                                {
                                    switch (inner?[0]?.Identifier as string)
                                    {
                                        case "deal":
                                            if (lord != null)
                                            {
                                                _pilgrimLordId = lord.StringId;
                                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, -15, false);
                                                Msg($"He names {lord.Name}. He describes the object with enough specificity that you believe him. " +
                                                    "You give him the seal. He is gone before the hour is out. " +
                                                    $"{lord.Name} will know someone is watching. They always know.", AshenColor);
                                                Msg($"(Relation with {lord.Name}: -15 — they sense scrutiny.)", BadColor);
                                            }
                                            else
                                                Msg("He names a lord. You give him the seal. He is gone.", DimColor);
                                            _pilgrimPhase = 10;
                                            _pilgrimCountdown = 21;
                                            break;

                                        case "relic":
                                            ShiftTrait(DefaultTraits.Calculating, -1);
                                            Msg("He laughs — a short, dry sound. \"I don't have it. I watched it change hands from a distance and survived the experience. " +
                                                "That's all I have.\" He walks past you without the seal and does not look back.", BadColor);
                                            break;

                                        case "nodeal":
                                            ShiftTrait(DefaultTraits.Honor, 1);
                                            Msg("He nods and goes. No argument, no visible disappointment. He had another road. You will not know where it led.", GoodColor);
                                            break;
                                    }
                                }, null, "", false), false, true);
                        }
                        break;

                        case "refuse":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("He nods once, as if this answer also tells him something useful, and walks back into the city. " +
                                "You ride out. For three miles you are aware that you made a decision based on instinct alone and that instinct is not always wrong.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── Deferred: Phase 1 (14d) — revelation fires ─────────────────────
        private static void FirePilgrimPhase1()
        {
            if (MageKnowledge._deferredInquiry != null) { _pilgrimCountdown = 1; return; }
            ChangeRenown(10f);
            MageKnowledge._deferredInquiry = () =>
            {
                Msg("A rider from the south brings a report that has been following you for several days. " +
                    "The village the pilgrim named is gone — not destroyed, not evacuated, simply absent, as though it had been a drawing someone erased. " +
                    "The lord he named died the morning after your meeting. No fire was reported. No illness. " +
                    "The date he gave for the burning of a place you've heard of matches the date in the garrison record exactly. " +
                    "He knew these things before they were finished happening. " +
                    "You gave him passage.", AshenColor);
                _pilgrimPhase = 2;
                _pilgrimCountdown = 30;
            };
        }

        // ── Deferred: Phase 2 (30d) — the return ───────────────────────────
        private static void FirePilgrimPhase2()
        {
            if (MageKnowledge._deferredInquiry != null) { _pilgrimCountdown = 1; return; }
            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Pilgrim Returns",
                    "He finds you in a city market, no longer wearing the burned linen. He wears clan colours now — someone else's clan. " +
                    "He bows, which is a different register entirely from how he spoke at the gate. " +
                    "\"You didn't ask what I was carrying,\" he says. \"Most do. The fire remembers that.\" " +
                    "He offers a choice and makes clear it is a genuine one.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("fragment", "Accept the grimoire fragment.", null, true,
                            "He hands it over without ceremony. The knowledge is old."),
                        new InquiryElement("letter",   "Carry a letter to a named lord — no questions asked.", null, true,
                            "A sealed letter. A name. A settlement. He does not explain what it contains."),
                        new InquiryElement("nothing",  "Ask for nothing. The passage was enough.", null, true,
                            "He seems genuinely uncertain how to answer this."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        switch (chosen?[0]?.Identifier as string)
                        {
                            case "fragment":
                                try { Hero.MainHero?.HeroDeveloper.UnspentFocusPoints += 2; } catch { }
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                Msg("He hands you a rolled skin covered in notation you half-recognise from three different traditions. " +
                                    "Two weeks with it will open something you've been working at for longer. " +
                                    "He says only: \"The old one who wrote that died before he finished it. You'll have to work out the last section yourself.\" " +
                                    "Then he's gone into the crowd.", FireColor);
                                Msg("(+2 focus points — the pilgrim's debt, in kind.)", GoodColor);
                                _pilgrimPhase = 0;
                                break;

                            case "letter":
                                _pilgrimLetterOpened = false;
                                _pilgrimPhase = 3;
                                _pilgrimCountdown = 14;
                                Msg("He gives you a sealed letter — the seal is a mark you don't recognise, which is itself information — and the name of a lord and the settlement where they are most likely to be found. " +
                                    "\"Fourteen days,\" he says. \"After that it doesn't matter.\"", DimColor);
                                break;

                            case "nothing":
                                ShiftTrait(DefaultTraits.Honor, 1);
                                ChangeRenown(15f);
                                Msg("He is quiet for a long moment — genuinely uncertain, which you would not have expected from the man at the gate. " +
                                    "\"Then you have it,\" he says eventually, and bows again, and goes. " +
                                    "Word of what you did at that gate has apparently traveled somewhere useful. " +
                                    "You become aware of this in the following weeks through the way certain people look at you.", GoodColor);
                                _pilgrimPhase = 0;
                                break;
                        }
                    }, null, "", false), false, true);
            };
        }

        // ── Deferred: Phase 3 (14d) — letter resolution ────────────────────
        private static void FirePilgrimPhase3()
        {
            if (MageKnowledge._deferredInquiry != null) { _pilgrimCountdown = 1; return; }

            var lord = !string.IsNullOrEmpty(_pilgrimLordId)
                ? Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == _pilgrimLordId)
                : Hero.AllAliveHeroes.Where(h => h.IsLord && h.IsAlive && h != Hero.MainHero && !h.IsPrisoner)
                    .OrderBy(_ => _rng.Next()).FirstOrDefault();

            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Sealed Letter",
                    "You have carried it for two weeks and the seal has not broken on its own, which you half-expected. " +
                    "The lord is in the next settlement over. You have the letter, the time, and the choice of whether to arrive with it intact.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("deliver", "Deliver it sealed.", null, true,
                            "You kept your word. What they do with it is not your question."),
                        new InquiryElement("open",    "Open it first.", null, true,
                            "The seal is made to be opened by the right hands. You are perhaps the wrong hands."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        switch (chosen?[0]?.Identifier as string)
                        {
                            case "deliver":
                                ShiftTrait(DefaultTraits.Honor, 1);
                                if (lord != null)
                                {
                                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, 15, false);
                                    ChangeGold(2000);
                                    Msg($"{lord.Name} reads it, is quiet for a long time, and then pays you without discussing the amount. " +
                                        "You leave with two thousand coin, a lord's curiosity about you, and no idea what was in the letter. " +
                                        "The pilgrim knew you would not open it. This was part of the arrangement.", GoodColor);
                                    Msg($"(Relation with {lord.Name}: +15)", GoodColor);
                                }
                                else
                                {
                                    ChangeGold(1500);
                                    Msg("The letter reaches its recipient. Two thousand coin arrive in your camp three days later with no note. " +
                                        "The pilgrim kept his side of this exactly.", GoodColor);
                                }
                                break;

                            case "open":
                                ShiftTrait(DefaultTraits.Calculating, -1);
                                ChangeGold(500);
                                Msg("The letter is blank. A perfect sheet of unmarked vellum under a seal that held for two weeks. " +
                                    "You deliver it anyway, for lack of a better option. " +
                                    "The lord reads the blank page, sets it down, and gives you five hundred coin without meeting your eyes. " +
                                    "Whatever the letter was, you were the letter. The pilgrim knew you would look.", DimColor);
                                if (lord != null)
                                {
                                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, -5, false);
                                    Msg($"(Relation with {lord.Name}: -5)", BadColor);
                                }
                                break;
                        }
                        _pilgrimPhase = 0;
                        _pilgrimLetterOpened = false;
                        _pilgrimLordId = null;
                    }, null, "", false), false, true);
            };
        }

        // ── Deferred: Phase 10 (21d) — debt collection ─────────────────────
        private static void FirePilgrimDebt()
        {
            if (MageKnowledge._deferredInquiry != null) { _pilgrimCountdown = 1; return; }

            var namedLord = !string.IsNullOrEmpty(_pilgrimLordId)
                ? Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == _pilgrimLordId)
                : null;

            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Agent in the Dark",
                    "A man finds your camp at night — not armed, not threatening, simply present in a way that makes clear he could have arrived at any hour he chose. " +
                    "He speaks quietly. \"You were told something. We would like to know who told you.\"",
                    new List<InquiryElement>
                    {
                        new InquiryElement("away",  "\"Send them away. What I know is mine.\"", null, true,
                            "They will note your answer and withdraw. The question will not go away."),
                        new InquiryElement("name",  "\"A pilgrim at the gate. I don't know his name.\"", null, true,
                            "You give them what they asked for. They will act on it."),
                        new InquiryElement("false", "Feed them false information.", null, true,
                            SkillHint(DefaultSkills.Roguery, 0.35f, "The lie needs to be specific enough to be believed.")),
                    },
                    false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        switch (chosen?[0]?.Identifier as string)
                        {
                            case "away":
                                ChangeCrime(8f);
                                if (namedLord != null)
                                {
                                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, namedLord, -20, false);
                                    Msg($"They withdraw without argument. {namedLord.Name} will hear that you refused, " +
                                        "which is its own kind of answer. The question remains open.", BadColor);
                                    Msg($"(Relation with {namedLord.Name}: -20. Crime rating: +8.)", BadColor);
                                }
                                else
                                {
                                    Msg("They withdraw without argument. The question remains open.", BadColor);
                                    Msg("(Crime rating: +8.)", BadColor);
                                }
                                break;

                            case "name":
                                if (namedLord != null)
                                {
                                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, namedLord, 10, false);
                                    Msg($"They thank you with the compressed efficiency of people who already half-knew the answer. " +
                                        $"{namedLord.Name} will find you more reliable after this. " +
                                        "The pilgrim made a calculation about your reliability and he was wrong. " +
                                        "You do not know what happens to him.", DimColor);
                                    Msg($"(Relation with {namedLord.Name}: +10.)", GoodColor);
                                }
                                else
                                    Msg("You name the pilgrim. They note it and go.", DimColor);
                                break;

                            case "false":
                                if (SkillRoll(DefaultSkills.Roguery, 0.35f))
                                {
                                    if (namedLord != null)
                                    {
                                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, namedLord, 5, false);
                                        Msg($"You give them a name — someone specific enough to be believable, connected enough to be plausible, gone enough to be unchecked. " +
                                            $"They write it down. {namedLord.Name} will hear you cooperated. " +
                                            "The pilgrim's real road remains unchallenged.", GoodColor);
                                        Msg($"(Relation with {namedLord.Name}: +5.)", GoodColor);
                                    }
                                    else
                                        Msg("The lie lands. They note it and withdraw.", GoodColor);
                                }
                                else
                                {
                                    ChangeCrime(15f);
                                    if (namedLord != null)
                                    {
                                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, namedLord, -25, false);
                                        Msg("They catch the specific detail that doesn't hold — you made it too clean, no loose ends where loose ends would naturally exist. " +
                                            $"They leave without comment. {namedLord.Name} will know you tried to deceive them. " +
                                            "Crime rating increases; your name is now recorded differently.", BadColor);
                                        Msg($"(Relation with {namedLord.Name}: -25. Crime rating: +15.)", BadColor);
                                    }
                                    else
                                    {
                                        Msg("The lie doesn't hold. They leave. Crime rating increases.", BadColor);
                                        Msg("(Crime rating: +15.)", BadColor);
                                    }
                                }
                                break;
                        }
                        _pilgrimPhase = 0;
                        _pilgrimLordId = null;
                    }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // THE BURNED ARCHIVE — Enter city, any, day 40+
        // Phase 0: idle
        // Phase 1: 7-day map (from 1A bucket line)
        // Phase 2: 2-day coverup visit (from 1B investigation success)
        // Phase 3: 21-day author meeting (from 1C study or Phase 1 secret)
        // ═══════════════════════════════════════════════════════════════════

        private static void E_BurnedArchive(Settlement s)
        {
            _archiveCooldown = 60;

            string charmHint = SkillHint(DefaultSkills.Roguery, 0.30f, "Find what the fire was meant to hide");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Burned Archive",
                "Smoke from the east quarter. Locals form a chain passing salvaged books hand to hand, but the line is already losing to the heat. " +
                "A town official catches your sleeve: \"Someone set this deliberately. We found an oil barrel inside, locked from within.\"",
                new List<InquiryElement>
                {
                    new InquiryElement("line",   "Help with the bucket line.", null, true,
                        "You save what can be saved. Something in the ash may be worth finding."),
                    new InquiryElement("invest", "Find who did this.", null, true, charmHint),
                    new InquiryElement("tome",   "Salvage one tome before it is lost.", null, MageKnowledge.IsMage,
                        "Something kept it intact through the heat. That is not nothing."),
                    new InquiryElement("ride",   "Ride on.", null, true,
                        "The fire is not yours."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "line":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ChangeRelWithOwner(s, 12);
                            ChangeRenown(10f);
                            Msg("The fire is contained — partially. In the ash you find a charred binding, title gone, one page surviving: " +
                                "a map drawn in a hand that was precise enough to be professional. Not of any road you know. " +
                                "You pocket it and mention it to no one.", GoodColor);
                            _archivePhase = 1;
                            _archiveCountdown = 7;
                            break;

                        case "invest":
                            if (SkillRoll(DefaultSkills.Roguery, 0.30f))
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                var arsonLord = Hero.AllAliveHeroes
                                    .Where(h => h.IsLord && h.IsAlive && h != Hero.MainHero && !h.IsPrisoner)
                                    .OrderBy(_ => _rng.Next()).FirstOrDefault();
                                if (arsonLord != null) _archiveLordId = arsonLord.StringId;
                                Msg("A child near the well saw a lord's livery on the man who entered the archive at midnight. " +
                                    "The child is specific about the colours. You now hold leverage over someone who believed this would go unwitnessed.", DimColor);
                                _archivePhase = 2;
                                _archiveCountdown = 2;
                            }
                            else
                            {
                                ChangeRenown(5f);
                                Msg("The official thanks you for the effort. The investigation finds nothing a court would accept. " +
                                    "Whoever burned the archive knew the width of the window they had.", DimColor);
                            }
                            break;

                        case "tome":
                            AgePlayer(3);
                            Msg("You pull one book from the outer edge of the fire. It's warm in a way that isn't heat — the cover shouldn't have held. " +
                                "Three nights with it gives you half of what it contains: partial spell notation in a script from three different traditions, layered. " +
                                "The other half requires more time and more focus than three nights allow.", FireColor);
                            try { Hero.MainHero?.HeroDeveloper.UnspentFocusPoints += 1; } catch { }
                            Msg("(+1 focus point — partial study. More is possible.)", GoodColor);
                            _archivePhase = 3;
                            _archiveCountdown = 21;
                            break;

                        case "ride":
                            ShiftTrait(DefaultTraits.Mercy, -1);
                            Msg("The fire is not yours. You ride out. The smoke is visible from the road for two hours.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── Deferred: Phase 1 (7d) — the map leads somewhere ──────────────
        private static void FireArchivePhase1()
        {
            if (MageKnowledge._deferredInquiry != null) { _archiveCountdown = 1; return; }
            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Hidden Archive",
                    "The map leads to a ruin half a day's ride from the city — a specific building, a specific room. Inside: a second archive, entirely intact. " +
                    "Sealed, dry, untouched by any fire. Whoever burned the public one was ensuring only this copy remained. " +
                    "The collection is substantial. Someone has been maintaining it.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("report", "Report it to the town lord.", null, true,
                            "The right thing. The information becomes theirs to manage."),
                        new InquiryElement("secret", "Keep it secret. Select volumes only — sell them quietly.", null, true,
                            "The value is in the selectivity. The rest stays hidden."),
                        new InquiryElement("burn",   "Burn this one too. Whoever maintained it had a reason — remove the reason.", null, true,
                            "Whatever this was meant to protect or enable ends here."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        switch (chosen?[0]?.Identifier as string)
                        {
                            case "report":
                                ShiftTrait(DefaultTraits.Honor, 1);
                                ChangeRenown(15f);
                                var archiveSett = Settlement.All.FirstOrDefault(x => x.IsTown);
                                if (archiveSett != null) ChangeRelWithOwner(archiveSett, 20);
                                Msg("The town lord receives the location with the controlled expression of someone managing their response to unexpected news. " +
                                    "His scholars arrive within two days. Whatever was in that collection will inform someone else's decisions now.", GoodColor);
                                _archivePhase = 0;
                                break;

                            case "secret":
                                ShiftTrait(DefaultTraits.Honor, -1);
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                ChangeGold(1500);
                                Msg("Twelve volumes, selected for specificity and rarity, quietly moved and quietly sold to three different buyers in three different cities over the following month. " +
                                    "The archive continues to sit where it is. The town lord does not know it exists.", DimColor);
                                _archivePhase = 3;
                                _archiveCountdown = 21;
                                break;

                            case "burn":
                                ShiftTrait(DefaultTraits.Honor, 1);
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                AgePlayer(2);
                                Msg("An hour to be certain, then another hour because certain wasn't certain enough. " +
                                    "The building comes down. The collection is ash. Whatever it was meant to serve no longer has that resource. " +
                                    "You do not know what that costs, in aggregate, across whatever was using it.", DimColor);
                                _archivePhase = 0;
                                break;
                        }
                    }, null, "", false), false, true);
            };
        }

        // ── Deferred: Phase 2 (2d) — the steward visits ────────────────────
        private static void FireArchivePhase2()
        {
            if (MageKnowledge._deferredInquiry != null) { _archiveCountdown = 1; return; }
            var arsonLord = !string.IsNullOrEmpty(_archiveLordId)
                ? Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == _archiveLordId)
                : Hero.AllAliveHeroes.Where(h => h.IsLord && h.IsAlive && h != Hero.MainHero && !h.IsPrisoner)
                    .OrderBy(_ => _rng.Next()).FirstOrDefault();

            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Steward's Visit",
                    "A steward arrives at your camp — not the lord, the steward, which is already a statement about how seriously this is being managed. " +
                    "He is very polite. \"Whatever the child told you, my lord had nothing to do with any fire. " +
                    "We would like to ensure that story stays localised.\" He has not named a price, which means you name it.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("gold",    "\"Name your price.\" Take the gold.", null, true,
                            "Clean, quiet, profitable. The child's account is buried."),
                        new InquiryElement("lord",    "\"Take this to the town lord.\"", null, true,
                            "You convert leverage into reputation. The arsonist lord loses standing."),
                        new InquiryElement("favour",  "\"I want a favour, not gold.\"", null, true,
                            SkillHint(DefaultSkills.Leadership, 0.40f, "The lord owes you something unnamed — if he accepts the framing.")),
                    },
                    false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        switch (chosen?[0]?.Identifier as string)
                        {
                            case "gold":
                                ShiftTrait(DefaultTraits.Honor, -1);
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                ChangeGold(3000);
                                Msg("Three thousand coin by morning. The steward leaves with your agreement to say nothing. " +
                                    "The child's account is localised. You are considerably richer and one compromise further along.", DimColor);
                                break;

                            case "lord":
                                ShiftTrait(DefaultTraits.Honor, 1);
                                ChangeRenown(15f);
                                if (arsonLord != null)
                                {
                                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, arsonLord, -30, false);
                                    ChangeCrime(5f);
                                    Msg($"The town lord receives the account in full, with the child's description attached. " +
                                        $"{arsonLord.Name} is summoned to explain themselves. The political fallout is unresolved but the shape of it is yours. " +
                                        "Their territory becomes unstable for the following season.", GoodColor);
                                    Msg($"(Relation with {arsonLord.Name}: -30. Crime in their territory: +5.)", BadColor);
                                }
                                else
                                    Msg("The town lord receives the account. The political consequences follow.", GoodColor);
                                break;

                            case "favour":
                                if (SkillRoll(DefaultSkills.Leadership, 0.40f))
                                {
                                    ShiftTrait(DefaultTraits.Calculating, 1);
                                    Msg("The steward sits with this for a moment, then says he will relay the framing to his lord. " +
                                        "A day later, a sealed letter arrives: one favour, unnamed, to be called when you require it. " +
                                        "A lord's personal debt. These are worth more than gold in specific circumstances.", GoodColor);
                                    if (arsonLord != null)
                                    {
                                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, arsonLord, 5, false);
                                        Msg($"(Relation with {arsonLord.Name}: +5 — they respect the framing.)", GoodColor);
                                    }
                                }
                                else
                                {
                                    ChangeCrime(10f);
                                    if (arsonLord != null)
                                    {
                                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, arsonLord, -20, false);
                                        Msg($"The steward takes it as a threat rather than an offer. {arsonLord.Name} receives the framing differently. " +
                                            "Crime rating increases; you now have an enemy with a reason.", BadColor);
                                        Msg($"(Relation with {arsonLord.Name}: -20. Crime: +10.)", BadColor);
                                    }
                                }
                                break;
                        }
                        _archivePhase = 0;
                        _archiveLordId = null;
                    }, null, "", false), false, true);
            };
        }

        // ── Deferred: Phase 3 (21d) — the author ───────────────────────────
        private static void FireArchivePhase3()
        {
            if (MageKnowledge._deferredInquiry != null) { _archiveCountdown = 1; return; }
            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Author",
                    "Someone finds you. They are calm in a way that is not composure — it is the calm of someone who has already resolved the question of what happens next. " +
                    "\"I wrote that notation,\" they say. \"I burned the other copies myself. You were not supposed to find this.\" " +
                    "They do not threaten. They explain: the diagram is incomplete on purpose. The missing piece would make it catastrophically powerful. " +
                    "They hid the halves separately. You have one. They have the other.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("buy",     "\"Give me the other half.\" Negotiate a price.", null, true,
                            "Gold and time, in exchange for a complete diagram."),
                        new InquiryElement("mutual",  "\"Destroy your half. I'll destroy mine.\"", null, true,
                            "A mutual disarmament. The diagram ceases to exist."),
                        new InquiryElement("keep",    "\"Keep your half. I'm keeping mine.\"", null, true,
                            "A standoff. Neither half is useful alone. For now."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        switch (chosen?[0]?.Identifier as string)
                        {
                            case "buy":
                                if (ChangeGold(-2000))
                                {
                                    AgePlayer(14);
                                    try { Hero.MainHero?.HeroDeveloper.UnspentFocusPoints += 3; } catch { }
                                    Msg("Two thousand coin and two weeks integrating halves that were designed not to be integrated. " +
                                        "The author watches you work and corrects two fundamental misreadings without comment. " +
                                        "The complete diagram opens something that would have taken years to reach otherwise.", FireColor);
                                    Msg("(+3 focus points — complete notation. Cost: 2000 gold, 14 days.)", GoodColor);
                                }
                                break;

                            case "mutual":
                                ShiftTrait(DefaultTraits.Honor, 1);
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                Msg("You destroy your half in front of them. They destroy theirs in front of you. " +
                                    "It takes about four minutes and the author is visibly relieved in a way that tells you exactly how seriously they took the risk. " +
                                    "The diagram is gone. Neither of you speaks after.", GoodColor);
                                break;

                            case "keep":
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                Msg("A standoff. They leave without threatening because threatening would be less effective than what they're already doing, " +
                                    "which is knowing that you have an incomplete thing and knowing that they know where you are. " +
                                    "The tension between the two halves is now a permanent feature of your situation.", DimColor);
                                break;
                        }
                        _archivePhase = 0;
                        _archiveLordId = null;
                    }, null, "", false), false, true);
            };
        }

        // ═══════════════════════════════════════════════════════════════════
        // THE QUIET VILLAGE — Enter village, mage, day 80+
        // Phase 0: idle
        // Phase 1: anchor destroyed (7d consequence)
        // Phase 2: figurine carried (7d first dream)
        // Phase 3: scholar lead active (14d to find the carver)
        // Phase 4: figurine escalating dream repeat (14d)
        // ═══════════════════════════════════════════════════════════════════

        private static void E_QuietVillage(Settlement s)
        {
            bool mage  = MageKnowledge.IsMage;
            bool ashen = MageKnowledge.IsAshen;
            _quietVCooldown = 120;

            string spellHint = SkillHint(DefaultSkills.Leadership, 0.35f, "Hold the anchor's pull without letting it take you");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Quiet Village",
                "The village is wrong before you are through the gate. Fires burning in hearths, bread rising in a window, a child's toy on the road — " +
                "and not one person moving. Thirty souls asleep in the middle of the day. No marks, no fever, no sound except the fire crackling without anyone to tend it. " +
                "Your horse refuses to go further without urging.",
                new List<InquiryElement>
                {
                    new InquiryElement("wake",   "Wake one of them.", null, true,
                        "They will open their eyes. What they say next is not a greeting."),
                    new InquiryElement("search", "Search the village for a cause.", null, true,
                        "Something made this happen. The sign is here if you can read it."),
                    new InquiryElement("burn",   "Burn the village.", null, ashen,
                        "The sleeping things are already gone. The village is a vessel."),
                    new InquiryElement("leave",  "Leave immediately.", null, true,
                        "You have seen enough of things like this to know when not to look closer."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "wake":
                        {
                            Msg("You wake one — a woman, middle-aged. She opens her eyes and says a name you don't recognise, then collapses back into sleep as though the effort of surfacing was the entirety of what she had left.", DimColor);
                            if (SkillRoll(DefaultSkills.Leadership, 0.35f))
                            {
                                Msg("The pattern becomes legible: mass trance anchored to a focal object. The pull is on the fire in you — not strongly, but it's there. " +
                                    "The object is in the elder's house. You can feel it from the doorway.", FireColor);
                                FireQuietVillageAnchorChoice(s);
                            }
                            else
                            {
                                AgePlayer(1);
                                Msg("You sit with it for most of a day and cannot isolate the cause cleanly enough to act. " +
                                    "The pull is there — you can feel it — but identifying the source requires more stillness than you can manage on a road." +
                                    "By evening you have a direction: the elder's house. The object is inside.", DimColor);
                                FireQuietVillageAnchorChoice(s);
                            }
                        }
                        break;

                        case "search":
                        {
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("At the well: a stone carved with a spiral — fresh cuts, days old at most. Underneath the spiral, scratched smaller: a name. " +
                                "You know that name. It belongs to a minor lord two settlements over. " +
                                "You have a lead without waking anyone.", DimColor);
                            _quietVPhase = 3;
                            _quietVCountdown = 14;
                        }
                        break;

                        case "burn":
                        {
                            ShiftTrait(DefaultTraits.Mercy, -2);
                            ShiftTrait(DefaultTraits.Honor, -1);
                            ChangeRelWithOwner(s, -50);
                            ChangeCrime(20f);
                            Msg("The fire spreads faster than it should. The sleeping don't wake. They don't burn the way living things burn — " +
                                "they diminish, which is a different thing, and quieter. " +
                                "Your men watch from the road without speaking.", BadColor);
                            if (ashen && !MageKnowledge.IsAshen)
                                Msg("An Ashen elder finds your camp three days later. \"That was wasteful,\" they say. \"Sleeping things are not dead things.\" " +
                                    "They offer nothing except the observation and leave before you can reply.", AshenColor);
                            _quietVPhase = 0;
                        }
                        break;

                        case "leave":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You have seen enough of things like this to know when not to look closer. " +
                                "You ride out. You remember the toy in the road for longer than you expected.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        private static void FireQuietVillageAnchorChoice(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Anchor",
                "In the elder's house, on the mantle between two ordinary objects: a carved figurine, warm to the touch in a way that has nothing to do with the hearth. " +
                "Touching it, you feel the pull toward sleep — not strongly, but it is absolutely present and absolutely intentional. " +
                "Thirty people are asleep because of this.",
                new List<InquiryElement>
                {
                    new InquiryElement("destroy", "Destroy it.", null, true,
                        "An hour's careful work. The village wakes confused. The object is gone."),
                    new InquiryElement("take",    "Take it. Knowing it exists is an advantage.", null, true,
                        "You carry something you don't fully understand. The village stays asleep."),
                    new InquiryElement("leave_it","Leave it in place.", null, true,
                        "The village stays asleep. You do not remove the cause."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "destroy":
                            AgePlayer(1);
                            ChangeRenown(20f);
                            ChangeRelWithOwner(s, 15);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("The fire does not like what it touches. There is resistance that isn't physical. Eventually it yields — the stone cracks and the pull vanishes. " +
                                "Across the village, thirty people wake at once, confused and without memory of the past several hours. " +
                                "Within an hour they are asking what happened to the bread.", GoodColor);
                            _quietVPhase = 1;
                            _quietVCountdown = 7;
                            break;

                        case "take":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You wrap it and carry it separate from everything else. The village remains asleep. " +
                                "You ride out with a gap in someone's network and thirty people who will not remember what happened today. " +
                                "The object is warm in your saddlebag.", AshenColor);
                            _quietVFigurine = true;
                            _quietVPhase = 2;
                            _quietVCountdown = 7;
                            break;

                        case "leave_it":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You leave the figurine exactly where it is. The village stays asleep. Whatever made this happen continues to have what it needs. " +
                                "You ride out with the knowledge of the object's location and no claim on what it does.", DimColor);
                            _quietVPhase = 0;
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── Deferred: Phase 1 (7d) — anchor destroyed, village sends word ──
        private static void FireQuietVillagePhase1()
        {
            if (MageKnowledge._deferredInquiry != null) { _quietVCountdown = 1; return; }
            ChangeGold(800);
            MageKnowledge._deferredInquiry = () =>
                Msg("A rider from the village finds you. One of the thirty, while sleeping, experienced something that left a specific spatial impression — " +
                    "a direction, a depth, a landmark. When they woke and described it, the village elder recognised the location: " +
                    "a sealed cache in the foundations of an outbuilding, untouched for two generations. Eight hundred coin in old minting. " +
                    "They sent you half, which is more than protocol required.", GoodColor);
            _quietVPhase = 0;
        }

        // ── Deferred: Phase 2 (7d) — figurine dream ───────────────────────
        private static void FireQuietVillagePhase2()
        {
            if (MageKnowledge._deferredInquiry != null) { _quietVCountdown = 1; return; }
            _quietVFigKeep++;

            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Dream Returns",
                    "You are sleeping longer than you should — an hour past dawn, then two. The figurine is warm in whatever pocket or bag it occupies. " +
                    (_quietVFigKeep >= 2
                        ? "This is the third time. The dream is more specific now: the village, all thirty awake, looking at you. The eldest says: \"You kept it warm. Something is coming to collect it.\""
                        : "In the dream you are in the village. All thirty are awake and looking at you. No one speaks."),
                    new List<InquiryElement>
                    {
                        new InquiryElement("destroy", "Destroy the figurine. End this.", null, true,
                            "It will take something from you to unmake it. Less than keeping it."),
                        new InquiryElement("give",    "Give it to someone else — transfer the weight.", null, true,
                            "The object finds a new carrier. The cost transfers with it."),
                        new InquiryElement("keep",    "Keep it. You are not done with it yet.", null, true,
                            _quietVFigKeep >= 2 ? "The dream will escalate." : "The dream will return in two weeks."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        switch (chosen?[0]?.Identifier as string)
                        {
                            case "destroy":
                                AgePlayer(3);
                                Msg("You spend most of a night on it. The resistance is stronger than the first time — it has been carried too long and is used to the warmth. " +
                                    "It yields eventually. The dreams stop. You sleep normally the following morning and notice the absence of the extra hour.", GoodColor);
                                _quietVFigurine = false;
                                _quietVFigKeep = 0;
                                _quietVPhase = 0;
                                break;

                            case "give":
                                ChangeGold(2000);
                                Msg("A scholar in the next city pays two thousand coin for it without asking where it came from, which is its own kind of answer about what he suspects it is. " +
                                    "He looks slightly unwell the following morning when you pass his window. " +
                                    "The dreams stop. The weight is no longer yours.", DimColor);
                                _quietVFigurine = false;
                                _quietVFigKeep = 0;
                                _quietVPhase = 0;
                                break;

                            case "keep":
                                AgePlayer(7);
                                Msg("The extra hours of sleep cost you a week's worth of mornings, cumulatively. " +
                                    "The figurine is still warm. The dream will return.", BadColor);
                                _quietVPhase = 4;
                                _quietVCountdown = 14;
                                break;
                        }
                    }, null, "", false), false, true);
            };
        }

        // ── Deferred: Phase 3 (14d) — find the carver ─────────────────────
        private static void FireQuietVillagePhase3()
        {
            if (MageKnowledge._deferredInquiry != null) { _quietVCountdown = 1; return; }
            var carverLord = Hero.AllAliveHeroes
                .Where(h => h.IsLord && h.IsAlive && h != Hero.MainHero && !h.IsPrisoner)
                .OrderBy(_ => _rng.Next()).FirstOrDefault();

            MageKnowledge._deferredInquiry = () =>
            {
                string carverName = carverLord?.Name?.ToString() ?? "the lord";
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Carver",
                    $"You find {carverName}. They admit it immediately — no shame, no fear, the way people state facts they believe are defensible. " +
                    "The village refused a tithe. A traveling scholar sold them the method. They say it wears off on its own in a month. " +
                    "Thirty people are sleeping in a village and this lord considers that a proportionate response to an unpaid tithe.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("undo",   $"\"Undo it now.\"", null, true,
                            SkillHint(DefaultSkills.Leadership, 0.35f, "Make them reverse it today, in front of a rider you send immediately.")),
                        new InquiryElement("liege",  "\"I'll tell your liege.\"", null, true,
                            "Political weight. Slow but durable."),
                        new InquiryElement("scholar","\"What did the scholar look like?\"", null, true,
                            "The method exists. The scholar is still moving. You want the source."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        switch (chosen?[0]?.Identifier as string)
                        {
                            case "undo":
                                if (SkillRoll(DefaultSkills.Leadership, 0.35f))
                                {
                                    if (carverLord != null)
                                    {
                                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, carverLord, -10, false);
                                        Msg($"{carverName} sends a rider immediately — not because they want to, but because you've made the cost of refusal legible. " +
                                            "The village wakes within a day. The villagers will not know who made them sleep, which is perhaps the lord's only remaining advantage.", GoodColor);
                                        Msg($"(Relation with {carverName}: -10 — humiliation remembered.)", BadColor);
                                    }
                                    ChangeRelWithOwner(Settlement.All.FirstOrDefault(x => x.IsVillage) ?? Settlement.All.First(), 20);
                                    ChangeRenown(15f);
                                }
                                else
                                {
                                    Msg($"{carverName} listens and declines. Politely, firmly, with the confidence of someone who has calculated the exact degree to which your intervention is useful to them. " +
                                        "The village wears off on its own in a month, as they said. You leave with nothing except the specific shape of their contempt.", BadColor);
                                }
                                break;

                            case "liege":
                                ShiftTrait(DefaultTraits.Honor, 1);
                                ChangeRenown(15f);
                                if (carverLord != null)
                                {
                                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, carverLord, -25, false);
                                    Msg($"The liege receives the account with the careful attention of someone who needed a specific justification to act on a problem they already had. " +
                                        $"{carverName} will be summoned. What happens to them takes months and may not be visible to you. " +
                                        "The village wakes on its own. The record of what happened to it now exists.", GoodColor);
                                    Msg($"(Relation with {carverName}: -25.)", BadColor);
                                }
                                break;

                            case "scholar":
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                Msg($"{carverName} gives you a description without hesitation — they want something in return and information is the easiest currency. " +
                                    "The scholar is moving east, working market towns, selling minor workings at the edge of legality. " +
                                    "The anchor technique is one of several. You have a profile and a direction.", AshenColor);
                                break;
                        }
                        _quietVPhase = 0;
                    }, null, "", false), false, true);
            };
        }

        // ── Phase 4: repeated dream (escalates from Phase 2 keep) ──────────
        private static void FireQuietVillagePhase4()
        {
            _quietVPhase = 2;
            _quietVCountdown = 0;
            FireQuietVillagePhase2();
        }

        // ═══════════════════════════════════════════════════════════════════
        // THE GALLOWS MAGE — Enter city/castle, any, day 30+
        // Phase 0: idle
        // Phase 1: child thread (14d)
        // Phase 2: freed mage (3d)
        // Phase 3: cell investigation result (7d)
        // ═══════════════════════════════════════════════════════════════════

        private static void E_GallowsMage(Settlement s)
        {
            _gallowsCooldown = 100;
            string leadHint = SkillHint(DefaultSkills.Leadership, 0.30f, "Demand a trial — invoke your standing as a lord");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Gallows Mage",
                "He has been there three days. The locals say so matter-of-factly, the way people speak about weather. " +
                "The rope is real. The neck is not broken. The body breathes — shallowly, but it breathes. " +
                "Anti-magic sigils are carved into the wood of the gibbet, not into him. The lord's captain watches from the wall.",
                new List<InquiryElement>
                {
                    new InquiryElement("ask",  "Ask the lord what this man did.", null, true,
                        "The lord will tell you. The account will be truthful and insufficient."),
                    new InquiryElement("cut",  "Cut him down.", null, true,
                        "The captain intervenes immediately. There is a price on interference."),
                    new InquiryElement("speak","Speak to the man on the gibbet.", null, MageKnowledge.IsMage,
                        "He opens his eyes when you approach. He knew you were coming."),
                    new InquiryElement("past", "Walk past.", null, true,
                        "This is not your concern. You will remember that you said so."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "ask":
                        {
                            Msg("The lord is forthright: the man was caught working rituals on livestock, then on a child who survived but speaks differently now. " +
                                "The sigils are keeping him alive deliberately — a warning made visible. The lord is not proud of it. He is also not stopping it.", DimColor);
                            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                                "✦  After the Account",
                                "The lord has given you the truth. The child is in this settlement, with her family, changed. The man is on the gibbet.",
                                new List<InquiryElement>
                                {
                                    new InquiryElement("child",  "\"The child — is she being helped?\"", null, true,
                                        "You ask about the person the lord is not asking about."),
                                    new InquiryElement("down",   "\"This is excessive. Cut him down.\"", null, true,
                                        "You override the lord's judgement. They will remember."),
                                    new InquiryElement("stands", "\"Your sentence stands.\"", null, true,
                                        "You agree. He will eventually die there."),
                                },
                                false, 1, 1, "Decide", "",
                                inner =>
                                {
                                    switch (inner?[0]?.Identifier as string)
                                    {
                                        case "child":
                                            ShiftTrait(DefaultTraits.Mercy, 1);
                                            Msg("The lord pauses. \"Not adequately,\" he says, which is more honest than you expected. " +
                                                "You arrange a rider to a physician you trust, at your expense, with a description of the symptoms. " +
                                                "The child's situation becomes your concern. The man on the gibbet remains the lord's.", GoodColor);
                                            _gallowsPhase = 1;
                                            _gallowsCountdown = 14;
                                            break;
                                        case "down":
                                            ChangeRelWithOwner(s, -15);
                                            Msg("The lord's expression closes. You have used your standing to override their judgement in public, in their settlement. " +
                                                "They comply because they have to. They do not forget.", BadColor);
                                            _gallowsPhase = 2;
                                            _gallowsCountdown = 3;
                                            break;
                                        case "stands":
                                            ShiftTrait(DefaultTraits.Calculating, 1);
                                            ChangeRelWithOwner(s, 10);
                                            Msg("You agree. The lord is quietly relieved — not pleased, relieved. He will die there within the week. " +
                                                "The sentence was disproportionate. You agreed with it anyway. That is a small and specific weight.", DimColor);
                                            break;
                                    }
                                }, null, "", false), false, true);
                        }
                        break;

                        case "cut":
                        {
                            Msg("The captain moves immediately — not with swords, with words. " +
                                "There is a price on interference with the lord's justice: five hundred coin or a public trial.", DimColor);
                            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                                "✦  The Captain's Terms",
                                "\"Five hundred coin, or you invoke your right to a public trial. Those are the terms.\" " +
                                "The captain is not bluffing. The man on the gibbet is watching.",
                                new List<InquiryElement>
                                {
                                    new InquiryElement("pay",   "Pay five hundred coin.", null, (Hero.MainHero?.Gold ?? 0) >= 500,
                                        "Fast. Clean. The man is freed."),
                                    new InquiryElement("trial", "Demand a public trial.", null, true, leadHint),
                                    new InquiryElement("force", "Force it. Draw.", null, true,
                                        "Your men are here. The captain's are also here. This becomes a different kind of event."),
                                },
                                false, 1, 1, "Decide", "",
                                inner =>
                                {
                                    switch (inner?[0]?.Identifier as string)
                                    {
                                        case "pay":
                                            ChangeGold(-500);
                                            ChangeRelWithOwner(s, -10);
                                            Msg("The captain takes the coin with the efficiency of someone who has done this before. The man is cut down inside the hour. " +
                                                "He is in worse shape than he looked from the road.", DimColor);
                                            _gallowsPhase = 2;
                                            _gallowsCountdown = 3;
                                            break;
                                        case "trial":
                                            if (SkillRoll(DefaultSkills.Leadership, 0.30f))
                                            {
                                                ChangeRenown(15f);
                                                Msg("The captain accepts the invocation. A trial is assembled — not immediately, but within the day. " +
                                                    "You are unexpectedly called to speak. You are the most notable person present.", GoodColor);
                                                _gallowsPhase = 2;
                                                _gallowsCountdown = 3;
                                            }
                                            else
                                            {
                                                ChangeCrime(10f);
                                                ChangeRelWithOwner(s, -20);
                                                Msg("The captain doesn't accept the invocation — your standing here is insufficient or your manner wrong. " +
                                                    "He calls the gate guards. You leave without the man and with a record of the attempt.", BadColor);
                                            }
                                            break;
                                        case "force":
                                            ChangeCrime(15f);
                                            ChangeRelWithOwner(s, -30);
                                            Msg("Your men and the captain's men look at each other with the professional assessment of people calculating odds. " +
                                                "The captain stands down — the odds do not favour him. The man is cut down. " +
                                                "You are not welcome in this settlement. That is now a formal thing.", BadColor);
                                            _gallowsPhase = 2;
                                            _gallowsCountdown = 3;
                                            break;
                                    }
                                }, null, "", false), false, true);
                        }
                        break;

                        case "speak":
                        {
                            Msg("He opens his eyes when you are close enough. He speaks at the threshold of hearing so the guards don't catch it. " +
                                "\"They can't kill me while those marks stand. They won't take them down because they're afraid of what I'll do. " +
                                "We've been at this impasse for three days. I find it almost restful.\"", AshenColor);
                            string rogueHint = SkillHint(DefaultSkills.Roguery, 0.35f, "Remove one sigil without the guards noticing");
                            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                                "✦  The Bargain",
                                "He asks you to remove one sigil — just one — so he can recover enough strength to break the rope himself. " +
                                "He offers information in exchange. He says he knows about a specific Ashen cell operating in this region.",
                                new List<InquiryElement>
                                {
                                    new InquiryElement("sigil", "Remove the sigil. Covertly.", null, true, rogueHint),
                                    new InquiryElement("info",  "\"Tell me about the cell first.\"", null, true,
                                        "He gives you half. Then you decide."),
                                    new InquiryElement("walk",  "\"I won't help you.\"", null, true,
                                        "He watches you go without expression. He has time."),
                                },
                                false, 1, 1, "Decide", "",
                                inner =>
                                {
                                    switch (inner?[0]?.Identifier as string)
                                    {
                                        case "sigil":
                                            if (SkillRoll(DefaultSkills.Roguery, 0.35f))
                                            {
                                                Msg("You remove the sigil during a moment when the captain's attention is elsewhere. " +
                                                    "By nightfall the rope is empty. The guards find nothing but frayed hemp and an intact gibbet. " +
                                                    "He is gone before they finish the report.", GoodColor);
                                                _gallowsPhase = 2;
                                                _gallowsCountdown = 3;
                                            }
                                            else
                                            {
                                                ChangeCrime(10f);
                                                Msg("A guard catches the motion — not the sigil, but your hand near the wood, which is enough. " +
                                                    "You are escorted to the gate and told to continue your journey. " +
                                                    "The man on the gibbet watches you leave. His expression does not change.", BadColor);
                                            }
                                            break;
                                        case "info":
                                            ShiftTrait(DefaultTraits.Calculating, 1);
                                            Msg("He names a settlement and a method — the cell uses a specific merchant front, operates in three-person units, " +
                                                "does not have contact with the larger network more than once per season. " +
                                                "It's half of what he knows. He waits.", AshenColor);
                                            var intelSett = Settlement.All
                                                .Where(x => x.IsTown && x != s).OrderBy(_ => _rng.Next()).FirstOrDefault();
                                            if (intelSett != null) _gallowsIntelId = intelSett.StringId;
                                            _gallowsPhase = 3;
                                            _gallowsCountdown = 7;
                                            break;
                                        case "walk":
                                            ShiftTrait(DefaultTraits.Calculating, 1);
                                            Msg("You walk away. He watches you go without changing expression. " +
                                                "You have seen people in worse situations. This does not make it easier to have walked away from this one.", DimColor);
                                            break;
                                    }
                                }, null, "", false), false, true);
                        }
                        break;

                        case "past":
                            ShiftTrait(DefaultTraits.Mercy, -1);
                            Msg("You walk past. He breathes. The rope holds. The captain watches from the wall. " +
                                "You complete your business in the settlement and leave, and you are aware that you looked at him twice more than you intended to.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── Deferred: Phase 1 (14d) — the child ────────────────────────────
        private static void FireGallowsPhase1()
        {
            if (MageKnowledge._deferredInquiry != null) { _gallowsCountdown = 1; return; }
            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Changed Child",
                    "The physician you sent replies — and then the family sends their own rider. " +
                    "The girl speaks in fragments now: disconnected words, occasionally in languages she does not know, correct in syntax for those languages. " +
                    "The physician has no diagnosis. The family is asking whether you can come.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("help",    "Go to her. Try to help.", null, MageKnowledge.IsMage,
                            SkillHint(DefaultSkills.Medicine, 0.50f, "The working on her is old and specific — you may be able to find its edges.")),
                        new InquiryElement("arrange", "Arrange for her to be taken to someone more capable.", null, true,
                            "Eight hundred coin. She travels. Someone with deeper knowledge receives her."),
                        new InquiryElement("reply",   "Send a reply — your best assessment of what was done and how it might be addressed.", null, true,
                            "Words and a direction. The physician works from what you give them."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        switch (chosen?[0]?.Identifier as string)
                        {
                            case "help":
                                AgePlayer(7);
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                if (SkillRoll(DefaultSkills.Medicine, 0.50f))
                                {
                                    ChangeRenown(25f);
                                    Msg("A week of careful work. The working on her is old enough that its edges have become part of her pattern — you can't remove it, " +
                                        "but you can blunt the intrusions. The fragments become less frequent. She begins to distinguish her voice from the others. " +
                                        "She is not cured. She is no longer worsening.", GoodColor);
                                }
                                else
                                {
                                    ShiftTrait(DefaultTraits.Mercy, 1);
                                    Msg("You find the edges of what was done and cannot move past them. Whatever was put into her is too deeply settled to lift. " +
                                        "She is no worse when you leave than when you arrived, which is a kind of result. " +
                                        "You tried. The family knows you tried. That is not nothing.", DimColor);
                                }
                                break;
                            case "arrange":
                                if (ChangeGold(-800))
                                {
                                    ShiftTrait(DefaultTraits.Generosity, 1);
                                    Msg("The physician confirms the arrangement. She travels within the week to someone who has dealt with deep workings before. " +
                                        "A month later a brief note arrives: she is stable. Not recovered — stable. The fragments continue but she has learned to navigate them.", GoodColor);
                                }
                                break;
                            case "reply":
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                Msg("You write what you know: the mechanism, the likely source, the probable intent. The physician works from your assessment. " +
                                    "They send back a month later: the assessment was correct, the treatment is ongoing, the prognosis is uncertain but not dire. " +
                                    "Words at the right time can be medicine.", DimColor);
                                break;
                        }
                        _gallowsPhase = 0;
                    }, null, "", false), false, true);
            };
        }

        // ── Deferred: Phase 2 (3d) — the freed mage finds you ─────────────
        private static void FireGallowsPhase2()
        {
            if (MageKnowledge._deferredInquiry != null) { _gallowsCountdown = 1; return; }
            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Freed Mage",
                    "He finds your camp three days later. He looks worse than he did on the gibbet — alive, but depleted in a way that doesn't come from the rope. " +
                    "He tells you what he did: the rituals, the livestock, the child. Without justification. Without asking for forgiveness. " +
                    "\"I am telling you this because you freed me,\" he says, \"and I don't deal in comfortable lies. What do you want?\"",
                    new List<InquiryElement>
                    {
                        new InquiryElement("child",  "\"Help me understand what you did to the child.\"", null, true,
                            "He will explain. The explanation will be complete."),
                        new InquiryElement("leave",  "\"Leave this region and don't come back.\"", null, true,
                            "He will go. He seems to find the terms reasonable."),
                        new InquiryElement("work",   "\"Work with me.\"", null, (Hero.MainHero?.GetTraitLevel(DefaultTraits.Calculating) ?? 0) >= 0,
                            "A capable mage who operates outside conventional constraints. Useful in specific ways."),
                        new InquiryElement("judge",  "\"What you did was unforgivable.\"", null, true,
                            "He will agree. He will leave. That will be the entirety of it."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        switch (chosen?[0]?.Identifier as string)
                        {
                            case "child":
                                AgePlayer(3);
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                Msg("He explains the working precisely — the mechanism, the entry point, what was changed and what it would take to reverse it. " +
                                    "It is a complete technical account delivered without affect, which is more disturbing than if he had been upset about it. " +
                                    "You leave with information that will change how you approach the child's situation.", FireColor);
                                _gallowsPhase = 1;
                                _gallowsCountdown = 14;
                                break;
                            case "leave":
                                ShiftTrait(DefaultTraits.Honor, 1);
                                ChangeRenown(10f);
                                Msg("He nods. He asks if there are specific regions you'd prefer he avoid. You name two. " +
                                    "He accepts both without argument and is gone before morning. " +
                                    "You will not see him again, which is a result.", GoodColor);
                                _gallowsPhase = 0;
                                break;
                            case "work":
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                _gallowsAllied = true;
                                Msg("He considers the offer with the careful attention of someone who has been in situations where work was the only available option. " +
                                    "He agrees, with conditions: he does not repeat what he did. You agree, with conditions: neither do you discuss it. " +
                                    "You ride out together in the morning. Your men keep their distance from him.", DimColor);
                                _gallowsPhase = 0;
                                break;
                            case "judge":
                                Msg("He nods once — a full, unhesitating nod, the kind that means I know — and leaves. " +
                                    "No argument, no attempt at mitigation. He agrees with your assessment and goes. " +
                                    "You watch him until the road takes him.", DimColor);
                                _gallowsPhase = 0;
                                break;
                        }
                    }, null, "", false), false, true);
            };
        }

        // ── Deferred: Phase 3 (7d) — cell investigation result ─────────────
        private static void FireGallowsPhase3()
        {
            if (MageKnowledge._deferredInquiry != null) { _gallowsCountdown = 1; return; }
            var cellSett = !string.IsNullOrEmpty(_gallowsIntelId)
                ? Settlement.Find(_gallowsIntelId)
                : Settlement.All.Where(x => x.IsTown).OrderBy(_ => _rng.Next()).FirstOrDefault();
            string cellName = cellSett?.Name?.ToString() ?? "a nearby settlement";

            MageKnowledge._deferredInquiry = () =>
            {
                string rogueHint = SkillHint(DefaultSkills.Roguery, 0.30f, "Find them before they find out you're looking");
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Cell",
                    $"The mage's intelligence leads to {cellName}. A merchant front, three-person unit, seasonal contact schedule. " +
                    "You have enough to act on if you move before the season turns.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("find",    "Investigate — find them before they find out.", null, true, rogueHint),
                        new InquiryElement("report",  "Report the intelligence to the local lord.", null, true,
                            "The lord acts on it. You receive credit for the information."),
                        new InquiryElement("discard", "Discard the intelligence. You've done enough here.", null, true,
                            "The cell continues. Your involvement ends."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        switch (chosen?[0]?.Identifier as string)
                        {
                            case "find":
                                if (SkillRoll(DefaultSkills.Roguery, 0.30f))
                                {
                                    ChangeRenown(20f);
                                    ChangeGold(1000);
                                    Msg("Three people, as described. More frightened than dangerous, which is often how these things are. " +
                                        "You give them two options: leave the region with nothing, or be handed to the local lord with everything. " +
                                        "They take the road. The cell is gone. The mage's intelligence was accurate.", GoodColor);
                                    if (cellSett != null) ChangeRelWithOwner(cellSett, 15);
                                }
                                else
                                {
                                    Msg("They find out someone is asking questions. By the time you locate the merchant front it is empty — closed with the specific thoroughness of people who practise leaving. " +
                                        "The cell is gone. You didn't catch them but you ended this operation. That is a partial success.", DimColor);
                                }
                                break;
                            case "report":
                                if (cellSett != null) ChangeRelWithOwner(cellSett, 20);
                                ChangeRenown(15f);
                                ChangeCrime(-10f);
                                Msg("The lord receives the account with the focused attention of someone being handed a problem they can actually solve. " +
                                    "The cell is rolled up within a week. Your name is associated with the intelligence that made it possible.", GoodColor);
                                break;
                            case "discard":
                                Msg("You have done enough here. The cell continues, somewhere, doing what cells do. " +
                                    "You have the information and chose not to use it, which is its own kind of answer about what you consider your concern.", DimColor);
                                break;
                        }
                        _gallowsPhase = 0;
                        _gallowsIntelId = null;
                    }, null, "", false), false, true);
            };
        }

        // ═══════════════════════════════════════════════════════════════════
        // THE EMBER TITHE COLLECTOR — Enter city, any, day 50+
        // Phase 0: idle
        // Phase 1: 7-day marked dream (from paying the tithe)
        // Phase 2: 7-day network investigation (from lord inquiry)
        // Phase 3: 30-day buyer letter (from any investigation completing)
        // ═══════════════════════════════════════════════════════════════════

        private static void EC_TitheCollector(Settlement s)
        {
            _titheCultCooldown = 90;
            string mageHint = SkillHint(DefaultSkills.Roguery, 0.35f, "Read what the ritual is actually doing");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Ember Tithe",
                "At the city gate, a tax collector stops travelers one by one. He presses a small ash-marked coin against each forehead, murmurs something, then waves them through. " +
                "Most people look annoyed but compliant. One man who refused is sitting in the mud beside the road with three guards standing over him. " +
                "The collector looks at you with particular interest.",
                new List<InquiryElement>
                {
                    new InquiryElement("pay",    "Submit to the toll. Pass through.", null, true,
                        "The coin presses cold against your forehead. Something passes."),
                    new InquiryElement("refuse", "Refuse and push past.", null, true,
                        SkillHint(DefaultSkills.Athletics, 0.40f, "Through the gate before they organise a response.")),
                    new InquiryElement("lord",   "Ask the city lord what this tithe is.", null, true,
                        "The lord will not know. That is itself the answer."),
                    new InquiryElement("watch",  "Watch the ritual carefully before deciding.", null, MageKnowledge.IsMage,
                        mageHint),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "pay":
                            ChangeGold(-50);
                            Msg("The coin is cold in a way that has nothing to do with temperature. The murmur is quick and specific. " +
                                "You are through the gate and the moment is behind you before you can examine it. " +
                                "That night you sleep an hour longer than usual.", DimColor);
                            _titheCultPhase = 1;
                            _titheCultCountdown = 7;
                            break;

                        case "refuse":
                            if (SkillRoll(DefaultSkills.Athletics, 0.40f))
                            {
                                ChangeCrime(5f);
                                Msg("You are through before the guards finish the motion of reaching for you. The collector marks something in his ledger. " +
                                    "You are in the city. Your name is in his record with a different notation than everyone else.", DimColor);
                            }
                            else
                            {
                                ChangeGold(-50);
                                Msg("Two guards and a narrow gate make the physics unfavourable. You pay the coin after all, which is worse than paying it willingly — " +
                                    "you demonstrated the refusal and then complied anyway. The collector notes this also.", BadColor);
                                _titheCultPhase = 1;
                                _titheCultCountdown = 7;
                            }
                            break;

                        case "lord":
                        {
                            var cityLord = s.OwnerClan?.Leader;
                            Msg("The lord receives your question with the expression of someone hearing about a thing that is happening in their own settlement for the first time. " +
                                "He did not issue this order. He did not authorise any tithe. The collector has been at the gate for a week with documentation that passed three clerks.", DimColor);
                            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                                "✦  The Lord's Response",
                                "The lord is embarrassed and quietly furious. The collector is still at the gate. The documentation exists. Someone created it.",
                                new List<InquiryElement>
                                {
                                    new InquiryElement("help",   "\"Help me find who he reports to.\"", null, true,
                                        "A joint investigation. The source is somewhere in the city."),
                                    new InquiryElement("arrest", "\"Arrest him now.\"", null, true,
                                        "The collector is taken. The operation moves. It does not stop."),
                                    new InquiryElement("yours",  "\"This is your problem.\"", null, true,
                                        "The lord handles it. You ride on."),
                                },
                                false, 1, 1, "Decide", "",
                                inner =>
                                {
                                    switch (inner?[0]?.Identifier as string)
                                    {
                                        case "help":
                                            _titheCultPhase = 2;
                                            _titheCultCountdown = 7;
                                            Msg("You and the lord's investigator begin working backward from the documentation. " +
                                                "The trail is clean but not invisible. You'll have something within the week.", DimColor);
                                            break;
                                        case "arrest":
                                            ChangeRelWithOwner(s, 15);
                                            ChangeRenown(10f);
                                            Msg("The collector is detained. The lord is grateful in a visible way that is useful. " +
                                                "The operation simply relocates — you will see this pattern again, in a different city, within the season.", GoodColor);
                                            _titheCultPhase = 3;
                                            _titheCultCountdown = 30;
                                            break;
                                        case "yours":
                                            ShiftTrait(DefaultTraits.Calculating, 1);
                                            Msg("The lord handles it. The collector is gone by morning — whether detained or warned off or simply reassigned is unclear. " +
                                                "The documentation is confiscated. The problem continues without this specific expression of it.", DimColor);
                                            break;
                                    }
                                }, null, "", false), false, true);
                        }
                        break;

                        case "watch":
                        {
                            Msg("What he murmurs is not a blessing. It is a read — name, lineage, magical sensitivity, all of it extracted through the coin contact and encoded in the mark. " +
                                "The mark fades from skin within hours. The record does not fade. This is a census of mage bloodlines.", AshenColor);
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                                "✦  What You Saw",
                                "You know what this is. You know the collector knows what he is doing. He is watching you watch him.",
                                new List<InquiryElement>
                                {
                                    new InquiryElement("confront", "Confront him quietly — away from the guards.", null, true,
                                        "He was expecting someone to notice eventually."),
                                    new InquiryElement("coins",    "Destroy the coins.", null, true,
                                        "A working, quickly. His batch is ruined. He panics."),
                                    new InquiryElement("silent",   "Say nothing. Leave and consider what you saw.", null, true,
                                        "He continues. You have knowledge he doesn't know you have."),
                                },
                                false, 1, 1, "Decide", "",
                                inner =>
                                {
                                    switch (inner?[0]?.Identifier as string)
                                    {
                                        case "confront":
                                            Msg("He doesn't panic. He was expecting this. He says: his employer pays well, the work is just census-taking from a certain angle. " +
                                                "He offers a commission — mages recording mages, the most reliable method.", DimColor);
                                            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                                                "✦  The Commission",
                                                "He is offering you an ongoing arrangement. He has a name for his employer. You can extract it or accept the commission or refuse both.",
                                                new List<InquiryElement>
                                                {
                                                    new InquiryElement("who",    "\"Who is your employer?\"", null, true,
                                                        SkillHint(DefaultSkills.Charm, 0.35f, "He will tell you if the framing is right.")),
                                                    new InquiryElement("accept", "Accept the commission.", null, true,
                                                        "Gold and a name on their list. On their side of the list."),
                                                    new InquiryElement("refuse2","Refuse and warn him off.", null, true,
                                                        "He leaves. The operation moves."),
                                                },
                                                false, 1, 1, "Decide", "",
                                                deep =>
                                                {
                                                    switch (deep?[0]?.Identifier as string)
                                                    {
                                                        case "who":
                                                            if (SkillRoll(DefaultSkills.Charm, 0.35f))
                                                            {
                                                                Msg("He names an organisation with no public presence and a long operating history. " +
                                                                    "Not Ashen — older, quieter, cataloguing for reasons that predate the Ashen by generations. " +
                                                                    "He says the name once. He leaves before you can follow up.", AshenColor);
                                                                _titheCultPhase = 3;
                                                                _titheCultCountdown = 30;
                                                            }
                                                            else
                                                            {
                                                                Msg("He declines to name them and offers only the commission again. " +
                                                                    "The framing wasn't right for extraction. He knows what he's protecting.", DimColor);
                                                            }
                                                            break;
                                                        case "accept":
                                                            ShiftTrait(DefaultTraits.Honor, -1);
                                                            ChangeGold(1500);
                                                            _titheCultAgent = true;
                                                            _titheCultPhase = 3;
                                                            _titheCultCountdown = 30;
                                                            Msg("Fifteen hundred coin paid immediately, as a gesture of good faith from both directions. " +
                                                                "You are now an asset. They will know what you know. " +
                                                                "The collector marks you in his ledger differently from everyone else.", DimColor);
                                                            break;
                                                        case "refuse2":
                                                            Msg("He accepts the refusal with the practiced ease of someone who has been refused before. " +
                                                                "He is gone within the hour. The gate is clear. The operation moves to the next city.", DimColor);
                                                            _titheCultPhase = 3;
                                                            _titheCultCountdown = 30;
                                                            break;
                                                    }
                                                }, null, "", false), false, true);
                                            break;

                                        case "coins":
                                            AgePlayer(1);
                                            ChangeRenown(15f);
                                            ShiftTrait(DefaultTraits.Mercy, 1);
                                            ChangeCrime(5f);
                                            Msg("The batch shatters. He goes pale — not from the loss of the coins but from what their shattering implies about you. " +
                                                "He packs up his table and is gone from the gate inside four minutes. " +
                                                "The census record for this city is destroyed. The one for every other city is not.", GoodColor);
                                            _titheCultPhase = 3;
                                            _titheCultCountdown = 30;
                                            break;

                                        case "silent":
                                            ShiftTrait(DefaultTraits.Calculating, 1);
                                            Msg("You walk through the gate and say nothing. He watches you pass with the attention of someone who is now uncertain whether you saw nothing or everything. " +
                                                "The uncertainty is the advantage. You know what he is doing. He doesn't know what you'll do with that.", DimColor);
                                            _titheCultPhase = 3;
                                            _titheCultCountdown = 30;
                                            break;
                                    }
                                }, null, "", false), false, true);
                        }
                        break;
                    }
                }, null, "", false), false, true);
        }

        // ── Deferred: Phase 1 (7d) — marked dream ─────────────────────────
        private static void FireTitheCultPhase1()
        {
            if (MageKnowledge._deferredInquiry != null) { _titheCultCountdown = 1; return; }
            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Dream",
                    "You remember a dream with architectural specificity: a building you have never entered, a room you have never stood in, a set of stairs going down. " +
                    "The dream recurs every night this week. You are sleeping longer than you should, waking with the building clearly in mind. " +
                    "The coin at the gate was a few days ago.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("find",  "Find the building.", null, true,
                            "The dream is a real location. That means it was put there."),
                        new InquiryElement("resist","Resist the pull.", null, true,
                            SkillHint(DefaultSkills.Leadership, 0.50f, "Override what the coin started.")),
                    },
                    false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        switch (chosen?[0]?.Identifier as string)
                        {
                            case "find":
                                Msg("The building is real. A basement below a grain merchant — the stairs match exactly. " +
                                    "Inside: a ledger. Names, lineages, magical sensitivity ratings, organised by settlement and date. " +
                                    "The tithe has been running for years, moving quietly through every major city on a rotating schedule. " +
                                    "You are in it. Third page. Your name is correct and your rating is underestimated.", AshenColor);
                                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                                    "✦  The Ledger",
                                    "You are holding a census of every mage bloodline in three kingdoms. Someone has been maintaining this for a long time.",
                                    new List<InquiryElement>
                                    {
                                        new InquiryElement("report", "Take it to the local lord.", null, true,
                                            "You convert discovery into reputation. The ledger becomes someone else's problem."),
                                        new InquiryElement("keep",   "Keep it. This is leverage.", null, true,
                                            "The ledger has political value. You determine how it moves."),
                                        new InquiryElement("burn",   "Burn the ledger.", null, true,
                                            "The list ceases to exist for everyone. Including you."),
                                    },
                                    false, 1, 1, "Decide", "",
                                    ledger =>
                                    {
                                        switch (ledger?[0]?.Identifier as string)
                                        {
                                            case "report":
                                                ChangeRenown(30f);
                                                var nearSett = Settlement.All.Where(x => x.IsTown).OrderBy(_ => _rng.Next()).FirstOrDefault();
                                                if (nearSett != null) ChangeRelWithOwner(nearSett, 25);
                                                Msg("The lord receives it with the controlled composure of someone being handed something that changes the shape of several conversations they are currently having. " +
                                                    "Your name is attached to the discovery. The ledger becomes an instrument of their investigation.", GoodColor);
                                                break;
                                            case "keep":
                                                ShiftTrait(DefaultTraits.Honor, -1);
                                                ShiftTrait(DefaultTraits.Calculating, 1);
                                                ChangeGold(3000);
                                                Msg("Three thousand coin, from three different buyers who each believe they purchased something exclusive. " +
                                                    "The original remains with you. The copies have entered circulation. " +
                                                    "The organisation that made the ledger will eventually notice it has moved.", DimColor);
                                                break;
                                            case "burn":
                                                ShiftTrait(DefaultTraits.Honor, 1);
                                                AgePlayer(1);
                                                Msg("You burn it in the basement where you found it, using enough heat that nothing is recoverable. " +
                                                    "Your own entry burns with everything else. The list ceases to exist. " +
                                                    "Whoever maintained it will build a new one. But not this one.", GoodColor);
                                                break;
                                        }
                                        _titheCultPhase = 3;
                                        _titheCultCountdown = 30;
                                    }, null, "", false), false, true);
                                break;

                            case "resist":
                                if (SkillRoll(DefaultSkills.Leadership, 0.50f))
                                {
                                    Msg("You suppress the pull through an act of deliberate attention — spending three evenings doing nothing else. " +
                                        "The dreams stop. The building exists somewhere and you do not know where. " +
                                        "The coin's work was interrupted, not completed. Your entry in the ledger is probably flagged as anomalous.", GoodColor);
                                }
                                else
                                {
                                    AgePlayer(7);
                                    Msg("The pull wins the week. By the end of it you know the building in more detail than you wanted to. " +
                                        "You are now compelled toward a specific grain merchant in a city two days' ride away. " +
                                        "You either go or spend another week in the same fight.", BadColor);
                                    _titheCultPhase = 1;
                                    _titheCultCountdown = 7;
                                    return;
                                }
                                _titheCultPhase = 0;
                                break;
                        }
                    }, null, "", false), false, true);
            };
        }

        // ── Deferred: Phase 2 (7d) — network investigation result ──────────
        private static void FireTitheCultPhase2()
        {
            if (MageKnowledge._deferredInquiry != null) { _titheCultCountdown = 1; return; }
            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Network",
                    "The documentation traces to a merchant consortium operating across three kingdoms. Not Ashen — something older and quieter. " +
                    "They are cataloguing mage bloodlines for an unnamed buyer. The consortium has been operating in some form for at least forty years. " +
                    "You have the consortium's structure but not the buyer.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("buyer",   "Find the buyer.", null, true,
                            "A deeper investigation. Time and days. The answer is at the end of it."),
                        new InquiryElement("sell",    "Sell what you know to an interested lord.", null, true,
                            "Someone else deals with the consortium. You take the coin."),
                        new InquiryElement("infiltrate","Infiltrate the consortium.", null, true,
                            SkillHint(DefaultSkills.Roguery, 0.25f, "Hard. If it works, you become part of the information flow.")),
                    },
                    false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        switch (chosen?[0]?.Identifier as string)
                        {
                            case "buyer":
                                AgePlayer(14);
                                _titheCultPhase = 3;
                                _titheCultCountdown = 30;
                                Msg("Two weeks of careful following — the consortium's contact schedule, their relay points, the specific mechanism by which information moves from collector to buyer. " +
                                    "You are close enough to see the shape of it. The buyer reaches out before you reach them.", DimColor);
                                break;
                            case "sell":
                                ShiftTrait(DefaultTraits.Honor, -1);
                                ChangeGold(2000);
                                Msg("A lord with a specific interest in the catalogue's existence pays two thousand coin for the consortium's structure. " +
                                    "They will act on it — slowly, through official channels, in a way that will take a season to complete. " +
                                    "You are considerably richer and entirely uninvolved in what follows.", DimColor);
                                _titheCultPhase = 0;
                                break;
                            case "infiltrate":
                                if (SkillRoll(DefaultSkills.Roguery, 0.25f))
                                {
                                    _titheCultAgent = true;
                                    _titheCultPhase = 3;
                                    _titheCultCountdown = 30;
                                    Msg("You construct an approach that looks, from the inside, like exactly what they expect to see. " +
                                        "They accept the approach. You are inside the information flow. " +
                                        "The buyer will make contact when the next collection round completes.", GoodColor);
                                }
                                else
                                {
                                    ChangeCrime(15f);
                                    ChangeRelWithOwner(Settlement.All.Where(x => x.IsTown).OrderBy(_ => _rng.Next()).FirstOrDefault() ?? Settlement.All.First(), -20);
                                    Msg("They catch the approach — specifically the detail you improvised in the third exchange, which didn't match what they expected to hear. " +
                                        "They don't confront you directly. Your name is in the ledger now but with a different marker than everyone else's. " +
                                        "Crime rating increases. Your covert access to this city is compromised.", BadColor);
                                    _titheCultPhase = 0;
                                }
                                break;
                        }
                    }, null, "", false), false, true);
            };
        }

        // ── Deferred: Phase 3 (30d) — the buyer makes contact ──────────────
        private static void FireTitheCultPhase3()
        {
            if (MageKnowledge._deferredInquiry != null) { _titheCultCountdown = 1; return; }
            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "✦  The Letter",
                    "A letter. No seal. One line: " +
                    (_titheCultAgent
                        ? "\"You've been useful. We would like to discuss a longer arrangement. Come to the marked settlement.\""
                        : "\"You've been looking for us. We've been watching you longer. Come to the marked settlement if you want to understand why the list matters.\""),
                    new List<InquiryElement>
                    {
                        new InquiryElement("go",    "Go.", null, true,
                            "A meeting with people who have been operating undetected for forty years. You go to them on their terms."),
                        new InquiryElement("trap",  "Set a trap.", null, true,
                            SkillHint(DefaultSkills.Roguery, 0.30f, "They are professionals. The trap needs to be better than their caution.")),
                        new InquiryElement("ignore","Ignore the letter.", null, true,
                            "They may write again. The offer may not stay open."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        switch (chosen?[0]?.Identifier as string)
                        {
                            case "go":
                                Msg("They are not villainous by their own accounting. They believe a Resurgence is coming — not Ashen, something older and less organised — " +
                                    "and they want to know which bloodlines will be affected and which will be useful. " +
                                    "The census is preparation, not predation. " +
                                    "They offer you a standing arrangement: access to their information in exchange for access to yours.", AshenColor);
                                ChangeRenown(20f);
                                if (!_titheCultAgent) ChangeGold(1000);
                                Msg(_titheCultAgent
                                    ? "(The arrangement formalises. You are now a named resource in a network that predates your grandfather.)"
                                    : "(+1000 gold — a good-faith payment. The arrangement is yours to continue or refuse.)", GoodColor);
                                _titheCultPhase = 0;
                                break;

                            case "trap":
                                if (SkillRoll(DefaultSkills.Roguery, 0.30f))
                                {
                                    ChangeRenown(20f);
                                    ChangeGold(1000);
                                    Msg("You prepare the meeting location and position people before they arrive. " +
                                        "A mid-rank operative. Professional, unhappy about the situation, and willing to exchange information for a clean exit. " +
                                        "You learn enough to understand the consortium's purpose without becoming part of it.", GoodColor);
                                }
                                else
                                {
                                    Msg("They don't come. The location is empty. Somewhere in the approach they identified the preparation. " +
                                        "A second letter arrives the following day: \"The offer is withdrawn. We will note that you tried.\" " +
                                        "The consortium is aware of you. You are aware of them. Neither of you has a clean next move.", BadColor);
                                }
                                _titheCultPhase = 0;
                                _titheCultAgent = false;
                                break;

                            case "ignore":
                                Msg("You do not go. The letter burns well. " +
                                    "A second letter arrives in forty days — shorter, one sentence: \"The offer expires with the next collector's route.\" " +
                                    "You have one more decision.", DimColor);
                                _titheCultPhase = 3;
                                _titheCultCountdown = 40;
                                break;
                        }
                    }, null, "", false), false, true);
            };
        }
    }
}
