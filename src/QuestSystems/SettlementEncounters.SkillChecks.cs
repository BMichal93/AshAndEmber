// =============================================================================
// ASH AND EMBER — SettlementEncounters.SkillChecks.cs
// Skill-check helpers (chance/hint/roll) shared by all encounters.
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
        // ── Skill-check helpers ───────────────────────────────────────────────
        // Soft roll: base success chance + (skillLevel × perPoint), capped.
        // baseChance 0.25 + perPoint 0.003 means:
        //   skill  50 → 40 %    skill 100 → 55 %
        //   skill 150 → 70 %    skill 200 → 85 %   skill 250+ → 90 % cap
        private static int GetSkill(SkillObject skill)
        {
            try { return Hero.MainHero?.GetSkillValue(skill) ?? 0; }
            catch { return 0; }
        }

        private static float SkillChance(SkillObject skill, float baseChance,
                                          float perPoint = 0.003f, float cap = 0.90f)
        {
            int level = GetSkill(skill);
            return Math.Min(cap, baseChance + level * perPoint);
        }

        private static bool SkillRoll(SkillObject skill, float baseChance,
                                       float perPoint = 0.003f, float cap = 0.90f)
            => _rng.NextDouble() < SkillChance(skill, baseChance, perPoint, cap);

        // Returns a one-word likelihood label used in hint text.
        private static string OddsLabel(float chance)
        {
            if (chance >= 0.85f) return "very likely";
            if (chance >= 0.68f) return "likely";
            if (chance >= 0.48f) return "even odds";
            if (chance >= 0.32f) return "unlikely";
            return "a long shot";
        }

        // Builds a hint string for a skill-check choice, shown as a tooltip.
        // e.g. "[Charm 84] Persuasion attempt — likely (59%)."
        private static string SkillHint(SkillObject skill, float baseChance, string outcomeLabel)
        {
            int level  = GetSkill(skill);
            float pct  = SkillChance(skill, baseChance) * 100f;
            return $"[{skill.Name} {level}] {outcomeLabel} — {OddsLabel(SkillChance(skill, baseChance))} ({(int)pct}%).";
        }

        // ═════════════════════════════════════════════════════════════════════
        // LEAVE VILLAGE — MAGE
        // ═════════════════════════════════════════════════════════════════════

        // 1. A Mother's Plea
        private static void E_MothersPlea(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  A Mother's Plea",
                "A woman in rough-spun wool steps into your path as you ride out. She carries a small child — you can see at a glance it is burning with fever. She has heard what you carry inside you. She weeps and offers nothing but her prayers.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Extend the inner fire to the child.", null, true,
                        "The fire can be given. It is not without cost."),
                    new InquiryElement("b", "Refuse. The road pulls at you.", null, true,
                        "The road continues."),
                    new InquiryElement("c", "Press coins into her hands — see a healer.", null, true,
                        "Coin may do what fire cannot. Something shifts."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgePlayer(1);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You press your palm to the child's brow. The fever breaks. The mother cannot speak for weeping.", GoodColor);
                            _mothersPleaPhase = 1;
                            _mothersPleaCountdown = 7;
                            break;
                        case "b":
                            Msg("You cannot be the answer to every prayer on every road. You ride on.", DimColor);
                            _mothersPleaPhase = 3;
                            _mothersPleaCountdown = 7;
                            break;
                        case "c":
                            ChangeGold(-200);
                            ShiftTrait(DefaultTraits.Generosity, 1);
                            Msg("The coins may save the child if a healer is near. You do not look back.", GoldColor);
                            _mothersPleaPhase = 2;
                            _mothersPleaCountdown = 7;
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═════════════════════════════════════════════════════════════════════
        // ENTER VILLAGE — MAGE
        // ═════════════════════════════════════════════════════════════════════

        // 11. The Old Flame-Seer
        private static void E_OldFlameSeer(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Old Flame-Seer",
                "An old man sits outside the inn, eyes clouded white. He does not look at you. He faces toward you. \"I can smell the fire from here,\" he says. \"Not the campfire kind. The old kind.\" He taps the bench beside him.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Sit with him. You have questions too.", null, true,
                        "A day at the edge of something old. What you leave with may surprise you."),
                    new InquiryElement("b", "Ask what he sees in you.", null, true,
                        "He sees something. So might you."),
                    new InquiryElement("c", "Keep walking. Old men with milky eyes say many things.", null, true,
                        "The road continues."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgePlayer(1);
                            if (TalentSystem.PurchasedCount < 6)
                            {
                                var available = TalentSystem.All
                                    .Where(t => t.Id != TalentId.Gift && !TalentSystem.Has(t.Id))
                                    .ToList();
                                if (available.Count > 0)
                                {
                                    var pick = available[_rng.Next(available.Count)];
                                    TalentSystem.GrantFree(pick.Id, Hero.MainHero);
                                    Msg($"You sit with him for an hour. He speaks around the edges of things you were already reaching toward. By the time the village lanterns are lit, something has opened in you — the shape of {pick.Name}, given without ceremony.", FireColor);
                                }
                                else
                                {
                                    Msg("You sit with him for an hour. He tells you what he knows. It is less than you hoped and more than you can use right now.", FireColor);
                                }
                            }
                            else
                            {
                                ChangeRenown(8f);
                                Msg("You sit with him for an hour. He tells you things about fire that you already knew but had not named — the difference is smaller than it used to be. He senses it. Before you leave he says: 'You have gone further than I can follow.' He does not mean it as a compliment.", FireColor);
                            }
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("\"A fire that eats its own wood,\" he says. \"Burning slow. Burning long.\" He does not explain further.", FireColor);
                            break;
                        case "c":
                            Msg("You walk past. He keeps facing the direction you were standing.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═════════════════════════════════════════════════════════════════════
        // LEAVE CITY/CASTLE — GENERAL
        // ═════════════════════════════════════════════════════════════════════

        // 26. The Bard's Request
        private static void E_BardsRequest(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Bard's Request",
                "A young bard with ink on his collar catches you at the city gate. He wants to write a song about you. He has heard enough already — the fire, the battles, the years. He only needs you to confirm the shape of it.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Tell it honestly. He can do what he likes with the truth.", null, true,
                        "Songs travel. Your name with them."),
                    new InquiryElement("b", "Decline modestly. Your story is not finished yet.", null, true,
                        "Restraint has its own reputation."),
                    new InquiryElement("c", "Embellish freely. Songs should be worth singing.", null, true,
                        "A better story than the truth. The song will travel farther."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ChangeRenown(15f);
                            Msg("You give him an hour of honest account. He stops you twice to make notes. The song he writes turns out better than the truth deserves.", GoodColor);
                            break;
                        case "b":
                            ChangeRenown(5f);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("\"Not yet,\" you tell him. He seems to find this more interesting than a full account. He writes it down anyway.", DimColor);
                            break;
                        case "c":
                            ChangeRenown(20f);
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("You give him a story worth a song — most of it true, the rest better than true. He looks satisfied. So does the version of yourself you described.", GoldColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═════════════════════════════════════════════════════════════════════
        // ENTER CITY/CASTLE — MAGE
        // ═════════════════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 63–67 — ENTER CITY (new)
        // ═══════════════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 68–72 — LEAVE CITY (new)
        // ═══════════════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 73–77 — ENTER VILLAGE (new)
        // ═══════════════════════════════════════════════════════════════════

        // 77. The Dog (Ashen-gated)
        private static void EV2_DogWontStop(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✿  The Dog",
                "A farm dog has been barking at you since your party entered the village. Just at you. Not at your horse, not at your men. At you specifically, with the rigid-legged certainty of an animal that knows something is wrong.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Try to approach and calm it.", null, true,
                        "You approach. It does not stop, but you tried."),
                    new InquiryElement("b", "Ignore it.", null, true,
                        "The barking follows you to the village edge."),
                    new InquiryElement("c", "Offer it meat from your pack.", null, true,
                        "Meat silences it. Uncertainty does not."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You approach slowly. It backs up, still barking, stays beyond your reach. It is not afraid — it simply does not want to be near what you have become. Animals remember things that people learn to ignore.", AshenColor);
                            break;
                        case "b":
                            Msg("The barking follows you to the other end of the village and stops exactly when you leave. The villagers pretend not to notice. They noticed.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You toss the meat gently. It catches it. Stops barking. Then sits and watches you for the rest of your time in the village, eating with its eyes. That is not entirely better.", AshenColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 87–89 — ENTER CITY (new batch)
        // ═══════════════════════════════════════════════════════════════════

        // 103. What She Sees (mage-gated)
        private static void EV4_GiftedChild(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  What She Sees",
                "A girl of perhaps six stops playing and stares at you. Not at your horse, not at your armor — at you. She reaches toward something she cannot name, cannot see, but clearly senses. Her mother pulls her back. The girl's eyes do not leave yours.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Crouch down and say a quiet word to her.", null, true,
                        "Something passes between you. She will remember."),
                    new InquiryElement("b", "Meet the mother's eyes and give a small nod.", null, true,
                        "The mother sees you see it. She will watch more carefully."),
                    new InquiryElement("c", "Ride on. This is not yours to name.", null, true,
                        "You ride on. The fire turns back, briefly."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You crouch down. She does not flinch. You say nothing useful — there are no words yet that would help her — but you let a thread of warmth pass between you, very small, so she knows what it is she is feeling. She smiles. The mother stands frozen. You ride on.", FireColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("The mother sees you see it. You nod once. She does not know what the nod means, but she will think about it later, and later still, and eventually she will start watching her daughter's hands near candles.", FireColor);
                            break;
                        case "c":
                            Msg("You ride past. Behind you, the girl is still facing the direction you were. The fire in you turns back once, briefly, the way it does when it recognises its own.", FireColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 109–111 — ENTER CITY (fourth batch)
        // ═══════════════════════════════════════════════════════════════════

        // 114. The Watching Figure (Ashen-gated)
        private static void LC4_RecognizedByAshen(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Watching Figure",
                "Leaving the city, you become aware — not by sight but by the particular absence of warmth — of someone in a building's shadow cataloguing your party. Grey cloak, pale still face, the patient posture of something that is not in a hurry because it has learned not to be. An Ashen agent is noting your movements.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Confront them directly.", null, true,
                        "They will be gone before you reach them. They know you saw."),
                    new InquiryElement("b", "Pretend not to notice and ride on.", null, true,
                        "They will follow for a time. What they report is out of your hands."),
                    new InquiryElement("c", "Have a message passed: you are not their enemy.", null, true,
                        "You reach out. Whether anything comes back is uncertain."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You turn your horse and ride toward the shadow. They are gone before you reach the doorway — not fled, simply gone, the way the Ashen go when they choose not to be found. The shadow is cold in a way that has nothing to do with the hour. They know you saw them. That may be enough.", AshenColor);
                            break;
                        case "b":
                            Msg("You ride on. They follow at a distance you would not see if you didn't know what to look for. After a mile they stop. Their report will note your route, your party's strength, and that you did not react. The last detail is the one that will be read most carefully.", AshenColor);
                            break;
                        case "c":
                            if (_rng.Next(2) == 0)
                            {
                                ChangeRelWithRandomLord(5);
                                Msg("A rider carries the message back into the city. Nothing happens for an hour. Then, near the road's first bend, something small and cold is placed in your saddlebag. A piece of grey cloth. An acknowledgement. The cold's own currency.", AshenColor);
                            }
                            else
                                Msg("The message goes in. Nothing comes back. Either it was not received, or received and set aside, or received and filed under 'noted.' The Ashen are not hurried correspondents.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // NEW ENCOUNTERS — ENTER CITY (general)
        // ═══════════════════════════════════════════════════════════════════

        // EC_PoorKnight — Young knight in worn gear wants to prove himself [Leadership]
        private static void EC_PoorKnight(Settlement s)
        {
            _poorKnightCooldown = 60; // long cooldown so this doesn't repeat constantly
            bool hasTournament = false;
            try { hasTournament = s.Town?.HasTournament ?? false; } catch { }
            string context = hasTournament
                ? "The tournament yard is buzzing with preparations when you spot him"
                : "You notice him near the training ground";

            float leadershipChance = SkillChance(DefaultSkills.Leadership, 0.38f);
            string coachHint = SkillHint(DefaultSkills.Leadership, 0.38f, "Steady him — teach him what he is missing");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  The Knight Without Fortune",
                $"{context}: a young man in dented, mismatched armour polishing a blade that has seen better decades. " +
                "The other knights are laughing at him from across the yard. He does not look at them. " +
                "He looks at the gate and polishes the blade anyway. There is something in his eyes that has nothing to do with the armour.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Ride past. It is not your concern.", null, true,
                        "The yard and its laughter fall behind you."),
                    new InquiryElement("b", "Outfit him — armour, horse, the works.", null, true,
                        $"−2000 coin. +1 Generous. {coachHint}"),
                    new InquiryElement("c", "Join the laughter.", null, true,
                        "He hears you. Everyone hears you."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            MageKnowledge._deferredInquiry = () =>
                                Msg("Word drifts back to you two days later: he entered, lost in the second round, and rode out without speaking to anyone. Nobody noticed.", DimColor);
                            break;
                        case "b":
                            if (!ChangeGold(-2000)) { Msg("You do not have enough coin.", BadColor); _poorKnightCooldown = 0; break; }
                            ShiftTrait(DefaultTraits.Generosity, 1);
                            if (SkillRoll(DefaultSkills.Leadership, 0.38f))
                            {
                                // B: he wins
                                var cataphract = MBObjectManager.Instance.GetObject<CharacterObject>("imperial_elite_cataphract")
                                             ?? MBObjectManager.Instance.GetObject<CharacterObject>("empire_cataphract");
                                if (cataphract != null)
                                    try { MobileParty.MainParty.MemberRoster.AddToCounts(cataphract, 1); } catch { }
                                ChangeRenown(10f);
                                Msg("Against every expectation in that yard, he wins. Not through luck — through something prepared and stubborn. " +
                                    "He comes to you afterward, the new armour bloodied and proving its worth, and asks to ride with you. " +
                                    "You have gained a seasoned Imperial Cataphract — and a knight who will remember exactly who gave him the chance.", GoodColor);
                            }
                            else
                            {
                                // C: he dies happy
                                AddMorale(-4f);
                                Msg("He rides in well-equipped and hopeful and is unhorsed in the third bout by a man twice his experience. " +
                                    "The fall breaks something that should not be broken. He is carried off the field. " +
                                    "They say he was smiling when they reached him. He had the armour. He had the horse. He had his chance. " +
                                    "He went in having been taken seriously by someone.", DimColor);
                            }
                            break;
                        case "c":
                            // D: he swears revenge
                            ShiftTrait(DefaultTraits.Honor, -1);
                            ShiftTrait(DefaultTraits.Mercy, -1);
                            _vengefulKnightCountdown = 7 + _rng.Next(5);
                            Msg("He looks up when he hears you. He finds your face. His jaw tightens and he goes back to polishing. " +
                                "He enters the tournament angry, and the anger carries him further than expected — three rounds, maybe four. " +
                                "Then it runs out. He loses. Before he rides out he looks back at you and says one sentence: " +
                                "\"I will remember who laughed.\"", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // EC_TavernHarassment — Noble groping a serving girl; clan tier < 5 [Athletics on option 3]
        private static void EC_TavernHarassment(Settlement s)
        {
            float athleticsChance = SkillChance(DefaultSkills.Athletics, 0.40f);
            string fightHint = SkillHint(DefaultSkills.Athletics, 0.40f, "Back him down before it becomes a brawl");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Tavern",
                "The common room is loud enough. Not loud enough to cover it. A minor lord with two men-at-arms at his back has his hand on the arm of a serving girl who is clearly not interested. " +
                "The tavernkeeper starts toward them — the lord's man steps in front and tells him to sit. The tavernkeeper sits. " +
                "Everyone in the room pretends not to notice.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Drink your beer. It is not your business.", null, true,
                        "It is not your business. You are very certain of this."),
                    new InquiryElement("b", "Laugh with the noble — make a friend.", null, true,
                        "You know how this goes. So does he."),
                    new InquiryElement("c", "Tell him to get off her.", null, true, fightHint),
                    new InquiryElement("d", "Send for the city guard.", null, true,
                        "The guard will come. Whether that helps anyone is a different question."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            // A: -1 honour, -1 mercy
                            ShiftTrait(DefaultTraits.Honor, -1);
                            ShiftTrait(DefaultTraits.Mercy, -1);
                            Msg("You drink your beer. Across the room the lord takes the girl behind the counter. " +
                                "Later, when he comes back out, he and his men are laughing about something. " +
                                "She is crying in the scullery. You paid for the beer and left. " +
                                "Nobody in the room looked at anyone else on the way out.", BadColor);
                            break;
                        case "b":
                            // B: -1 mercy, +5 rel with city lord
                            ShiftTrait(DefaultTraits.Mercy, -1);
                            ChangeRelWithOwner(s, 5);
                            Msg("You make a joke. He laughs. His men laugh. He sends you a jug of the good wine from behind the bar — " +
                                "the tavernkeeper's own, you notice — and raises it across the room. " +
                                "You have made a friend. You are both exactly the kind of people this room deserved tonight.", BadColor);
                            break;
                        case "c":
                            // C success: +1 honour +10 renown -10 rel. D fail: +1 honour -20 rel +20 crime
                            ShiftTrait(DefaultTraits.Honor, 1);
                            if (SkillRoll(DefaultSkills.Athletics, 0.40f))
                            {
                                ChangeRenown(10f);
                                ChangeRelWithOwner(s, -10);
                                Msg("You stand up. The room goes quiet with the specific quality of rooms that have been waiting for someone to stand up. " +
                                    "The lord reads your face and reads the room and lets go. He calls you a few things on his way out that confirm your assessment of him. " +
                                    "The girl is gone by the time the door closes behind him. The tavernkeeper sets the good wine in front of you without being asked. " +
                                    "The room breathes again.", GoodColor);
                            }
                            else
                            {
                                ChangeRelWithOwner(s, -20);
                                var kingdom = s.MapFaction as TaleWorlds.CampaignSystem.Kingdom;
                                if (kingdom != null) try { ChangeCrimeRatingAction.Apply(kingdom, 20f, false); } catch { }
                                Msg("You stand up. So do his men. What follows is not the clean confrontation you intended — " +
                                    "it is a tavern brawl with a lord's hired muscle, and you come out of it reported to the guard for disturbing the peace. " +
                                    "On the positive side: the girl got out during the noise. " +
                                    "The lord filed a formal complaint. Your crime rating in this kingdom just went up by twenty.", BadColor);
                            }
                            break;
                        case "d":
                            // E: guards side with noble, crime +10, rel -5
                            ChangeRelWithOwner(s, -5);
                            {
                                var kingdom = s.MapFaction as TaleWorlds.CampaignSystem.Kingdom;
                                if (kingdom != null) try { ChangeCrimeRatingAction.Apply(kingdom, 10f, false); } catch { }
                            }
                            Msg("The guard arrives before long. They apologise to the lord for the interruption. " +
                                "They ask you to come with them for questioning about what you saw and why you sent for them. " +
                                "The lord's men are still in the tavern. The girl is still in the tavern. " +
                                "You can only imagine what happens next. " +
                                "Your crime rating in this kingdom went up by ten.", BadColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

    }
}
