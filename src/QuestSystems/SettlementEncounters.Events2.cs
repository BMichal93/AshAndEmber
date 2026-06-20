// =============================================================================
// ASH AND EMBER — SettlementEncounters.Events2.cs
// After-battle/siege encounters and Ashen village/city events.
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
        // ── AFTER BATTLE (Ashen): Memory Drain ────────────────────────────────
        // Fires when an Ashen player cast 3+ spells in a single battle.
        // Choices: resist (Leadership roll), accept loss, or feed it (gains XP, larger loss).
        private static void EB_AshenMemoryDrain()
        {
            // Pick one of the three humane traits to drain (the ones Ashen players are losing)
            var candidates = new[] { DefaultTraits.Mercy, DefaultTraits.Honor, DefaultTraits.Generosity };
            var drainTrait = candidates[_rng.Next(candidates.Length)];
            string traitName = drainTrait == DefaultTraits.Mercy ? "Mercy"
                             : drainTrait == DefaultTraits.Honor ? "Honor" : "Generosity";

            float leadershipChance = SkillChance(DefaultSkills.Leadership, 0.35f);
            string resistHint = SkillHint(DefaultSkills.Leadership, 0.35f, "Hold the memory through will");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Fading",
                "Afterward, in the quiet, you reach for something and it is not there. A name. A face. Something that was yours. " +
                "The working took more than years this time — it reached further back and took something you did not offer. " +
                "You are aware of the shape of what is missing. You are not certain you know what it was.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Hold on. Reach for it — force it back.", null, true, resistHint),
                    new InquiryElement("b", "Let it go. There was a cost. You paid it.", null, true,
                        $"What is gone is gone. You remain. {traitName} fades."),
                    new InquiryElement("c", "Feed it more. If it wants to take, let it take — and take something in return.", null, true,
                        $"A deeper trade. More is lost. Something is gained."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Leadership, 0.35f))
                            {
                                Msg("You force it. The memory surfaces — incomplete, edges worn, but present. A face. The shape of a place. " +
                                    "Something that was yours is still yours. The effort costs you the rest of the evening. " +
                                    "You are aware the cold is patient.", AshenColor);
                            }
                            else
                            {
                                ShiftTrait(drainTrait, -1);
                                Msg($"You reach for it and find the reaching itself is unfamiliar — the path to it has gone cold. " +
                                    $"You hold the effort until it becomes clear that there is nothing left to hold. {traitName} fades. " +
                                    "The cold does not announce itself. It simply expands.", BadColor);
                            }
                            break;
                        case "b":
                            ShiftTrait(drainTrait, -1);
                            ChangeGold(-300);
                            Msg($"{traitName} fades. Something else goes with it — the thread of a connection you can no longer name. " +
                                "Three hundred coin gone by morning, in a sequence of decisions you do not entirely remember making. " +
                                "You paid what was asked. You are still here. The accounting is unclear.", BadColor);
                            break;
                        case "c":
                            ShiftTrait(drainTrait, -1);
                            ShiftTrait(candidates[_rng.Next(candidates.Length)], -1);
                            try { Hero.MainHero.HeroDeveloper.UnspentFocusPoints += 1; } catch { }
                            ChangeRenown(-10f);
                            Msg("You open the door. Something passes through you in both directions — you feel the loss clearly, two things, maybe more. " +
                                "What returns is not the same shape as what left. It is colder. It is useful. " +
                                "Your men look at you strangely over the fire that evening. You do not ask them why.", AshenColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── AFTER BATTLE: The Debrief [Tactics] ────────────────────────────
        private static void EB8_BattleDebrief()
        {
            float chance = SkillChance(DefaultSkills.Tactics, 0.28f);
            string hint  = SkillHint(DefaultSkills.Tactics, 0.28f, "Identify and correct the misread");
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  The Debrief",
                "Your sergeant is debriefing the battle with your officers and his read of what happened at the centre is subtly but importantly wrong — he believes the enemy centre held because of superior numbers, but you saw something else from your position. If his interpretation enters your officers' working model of how battles develop, it will inform a decision the wrong way at a moment that matters.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Correct the read precisely — explain what actually held the centre.", null, true, hint),
                    new InquiryElement("b", "Ask him what he saw from his position before offering a counter-reading.", null, true,
                        "The combined account may be better than either alone."),
                    new InquiryElement("c", "Let it stand for now — his read is close enough for the lesson they need today.", null, true,
                        "The wrong model travels forward. It may matter."),
                    new InquiryElement("d", "Bring in a second officer whose position gave them a clear view of the centre.", null, true,
                        "A third account may resolve the question."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Tactics, 0.28f))
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                AddMorale(5f);
                                Msg("The centre held because of a deliberate false retreat on their right — it drew your left flank's attention and compressed the centre's pressure by a third. The numbers were coincidence. Your sergeant listens, asks two clarifying questions, and restates the corrected read back to the officers. They leave the debrief with the right lesson.", GoodColor);
                            }
                            else
                                Msg("You offer your counter-read but cannot articulate the mechanism clearly enough — you saw something but translating what you saw into the language of formation tactics is harder than seeing it was. Your sergeant acknowledges your disagreement and hedges his read without fully revising it. The debrief ends with ambiguity rather than a clean correction. Better than the wrong certainty. Not as good as the right certainty.", DimColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            ShiftTrait(DefaultTraits.Honor, 1);
                            AddMorale(4f);
                            Msg("You ask what he saw. His account of the centre from his position is actually accurate from where he stood — he simply could not see the false retreat from the left flank, which was behind his line of sight. When you add your account to his the combined picture is clearer than either alone. He revises his conclusion correctly and credits the two-position reading. The officers leave with a better model and a method for building one.", GoodColor);
                            break;
                        case "c":
                            Msg("You let it stand. His read is close enough that the lesson — hold the centre, watch for flanking pressure — is defensible. The mechanism is wrong. This will matter exactly once, at a moment that will not announce itself in advance. You have chosen convenience and it may cost nothing. You do not know yet.", DimColor);
                            break;
                        case "d":
                            AddMorale(3f);
                            Msg("You bring in the officer who held the right flank and had the clearest view of the centre from the side. His account adds the false retreat that your sergeant missed. With all three readings on the table the correct picture emerges naturally — no one person's reading was wrong, it was incomplete. The debrief ends with a model that was earned rather than imposed. Your men respect the process. So does your sergeant.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── AFTER SIEGE: The Siege Stores [Steward] ────────────────────────
        private static void ES8_SiegeStores()
        {
            float chance = SkillChance(DefaultSkills.Steward, 0.25f);
            string hint  = SkillHint(DefaultSkills.Steward, 0.25f, "Divide the stores correctly and equitably");
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚒  The Division",
                "The keep's stores are more than expected — the previous lord was preparing for a long siege. Your quartermaster can manage the military share straightforwardly, but the civilian question is harder: the city's merchants and the keep's garrison staff both have claims under different precedents, and the amounts are large enough that the wrong division will cause problems before the week is out.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Work through the division properly — apply the correct precedents.", null, true, hint),
                    new InquiryElement("b", "Give your quartermaster full authority — this is his domain.", null, true,
                        "This is his domain. Trust him with it."),
                    new InquiryElement("c", "Take the military share, return the city merchants' claims, and call the garrison staff claims void.", null, true,
                        "Fast and clean for some. Not for all."),
                    new InquiryElement("d", "Hire a city administrator to manage the civilian claims under your oversight.", null, true,
                        "A clean distribution. The city may trust the outcome more."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Steward, 0.25f))
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                ChangeRenown(8f);
                                AddMorale(4f);
                                Msg("The correct precedent is siege capture law modified by the city charter's civilian commerce protections — the merchants' pre-siege inventory claims are valid up to the point of city closure, garrison staff claims are pro-rated by months served, and the military share is calculated after both civilian claims are satisfied. You work through it in two hours. Nobody gets everything. Nobody has a legitimate grievance. Your quartermaster is taking notes.", GoodColor);
                            }
                            else
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                AddMorale(2f);
                                Msg("You apply the precedents as best you can, but the intersection of siege law and city charter has a gap that your reading doesn't resolve cleanly. You make a defensible decision rather than a correct one. The merchants accept it. The garrison staff accept it for now — but they will raise the gap in a formal petition in six months.", DimColor);
                            }
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            AddMorale(5f);
                            Msg("You give him the full authority directly and say so in front of the claimants. He takes it and works through it correctly — he has done this before, or something close enough that the differences don't matter. The distribution takes three hours and satisfies both groups adequately. He reports back to you with the numbers.", GoodColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            ChangeRelWithRandomLord(-5);
                            Msg("Fast, clear, defensible on the merchant side. The garrison staff, who served through a siege under a lord who is now your prisoner, receive nothing for that service. Some of them are veterans. Some of them protected this city from your assault for months. The bitterness is specific and immediate. The merchants are satisfied and will say so. The garrison staff are not and will also say so. The city's opinion of you is divided along exactly those lines.", DimColor);
                            break;
                        case "d":
                            ChangeGold(-300);
                            ChangeRenown(5f);
                            Msg("You hire the city's former exchequer — not the previous lord's man, an independent one who worked for the trade council. He manages the civilian claims under your sign-off, which means the distribution carries official weight but the process has local credibility. It costs three hundred coin and two days. The result is accepted without significant objection by anyone, which in post-siege administration is close to a miracle.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // ── (original event 40 follows below, unchanged) ──────────────────
        // ═══════════════════════════════════════════════════════════════════

        // 40. An Old Enemy
        private static void E_OldEnemy(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  An Old Enemy",
                "A weathered veteran in the city square catches your eye and holds it. He was on the other side of a battle three years ago — you remember his face from across a line of shields. He remembers yours. He raises his cup toward you.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Raise your hand in return. Wars end.", null, true,
                        "The gesture contains the whole of it. Wars end."),
                    new InquiryElement("b", "Walk past as if you have not seen him.", null, true,
                        "He watches you pass. The moment passes — but it may not stay passed."),
                    new InquiryElement("c", "Report his presence to the city guard as a potential threat.", null, true,
                        "He is a veteran with a cup. The city guard will remember this. So will his lord."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            ChangeRelWithRandomLord(5);
                            Msg("You raise your hand. He nods. The gesture contains the whole of it — we both survived, we are both still here, that is something. You do not speak. You do not need to.", GoodColor);
                            _oldEnemyOutcome   = 1;
                            _oldEnemyCountdown = 30 + _rng.Next(30);
                            break;
                        case "b":
                            Msg("He watches you pass. He does not follow. He will be here tomorrow, with his cup, waiting for nothing in particular.", DimColor);
                            _oldEnemyOutcome   = 2;
                            _oldEnemyCountdown = 30 + _rng.Next(30);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            ChangeCrime(5f);
                            Msg("He is taken in for questioning and released inside the hour — there is no cause. He looks at you differently when he comes out. So do you, at yourself.", BadColor);
                            _oldEnemyOutcome   = 3;
                            _oldEnemyCountdown = 7;
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── Deferred: FireOldEnemyConsequence ─────────────────────────────────
        private static void FireOldEnemyConsequence()
        {
            if (MageKnowledge._deferredInquiry != null) { _oldEnemyCountdown = 1; return; }
            int outcome = _oldEnemyOutcome;
            _oldEnemyOutcome = 0;

            switch (outcome)
            {
                case 1: // raised hand
                    MageKnowledge._deferredInquiry = () =>
                    {
                        ChangeRelWithRandomLord(5);
                        Msg("A gift arrives by messenger — modest, deliberate, unsigned except for a crest you recognise. The veteran mentioned your name to someone with the right ears. The gesture says: that moment at the city square was noted, and noted correctly.", GoodColor);
                    };
                    break;

                case 2: // walked past
                    MageKnowledge._deferredInquiry = () =>
                    {
                        MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                            "⚔  The Road Meets the Road",
                            "Your outriders report a figure blocking the narrow pass ahead — alone, unhurried, sword drawn and resting point-down in the road. You recognise him. He is not here by accident. He had weeks to find you, and he did. He does not look angry. He looks like a man who made a decision and followed it to its conclusion.",
                            new List<InquiryElement>
                            {
                                new InquiryElement("a", "Hear him out. He found you — give him that much.", null, true, ""),
                                new InquiryElement("b", "Ride around him. Nothing happened then; nothing happens now.", null, true, ""),
                            },
                            false, 1, 1, "Decide", "",
                            sub =>
                            {
                                switch (sub?[0]?.Identifier as string)
                                {
                                    case "a":
                                        ShiftTrait(DefaultTraits.Honor, 1);
                                        ChangeRelWithRandomLord(8);
                                        Msg("He sheathes the blade without ceremony and tells you what he came to say: you were right not to speak at the city square. He was not ready to hear it. He is now. Whatever he needed to settle with you, this was it. He steps aside. His lord receives your name differently after this.", GoodColor);
                                        break;
                                    case "b":
                                        Msg("You ride around him. He watches you go. He made his choice weeks ago, and you've made yours today. There is no wound here, and no resolution. Both of you carry the weight of that junction farther down your separate roads.", DimColor);
                                        break;
                                }
                            }, null, "", false), false, true);
                    };
                    break;

                case 3: // reported
                    MageKnowledge._deferredInquiry = () =>
                    {
                        ChangeRelWithRandomLord(-10);
                        Msg("Word reaches you through a third party: the lord that veteran serves heard what you did at the city gate. He has not forgotten it, and his dislike of you now has a specific shape. A commander who reported a harmless old man drinking in a square is a man whose judgment cannot be trusted — or trusted too precisely, which is worse.", BadColor);
                    };
                    break;
            }
        }

        // ── Helper: wound the player hero and a few party members ─────────────
        private static void WoundPlayer()
        {
            try { Hero.MainHero.HitPoints = Math.Max(1, Hero.MainHero.MaxHitPoints / 4); } catch { }
            try
            {
                var roster = MobileParty.MainParty?.MemberRoster;
                if (roster == null) return;
                int toWound = 3 + _rng.Next(8), wounded = 0;
                foreach (var e in roster.GetTroopRoster().ToList())
                {
                    if (e.Character.IsHero) continue;
                    int healthy = e.Number - e.WoundedNumber;
                    int w = Math.Min(healthy, toWound - wounded);
                    if (w <= 0) continue;
                    roster.AddToCounts(e.Character, 0, false, w);
                    wounded += w;
                    if (wounded >= toWound) break;
                }
            }
            catch { }
        }

        // ── Helper: wound a number of party troops (simulate Curse hit) ───────
        private static void WoundPartyTroops(int count)
        {
            try
            {
                var roster = MobileParty.MainParty?.MemberRoster;
                if (roster == null) return;
                int wounded = 0;
                foreach (var e in roster.GetTroopRoster().ToList())
                {
                    if (e.Character.IsHero) continue;
                    int healthy = e.Number - e.WoundedNumber;
                    int w = Math.Min(healthy, count - wounded);
                    if (w <= 0) continue;
                    roster.AddToCounts(e.Character, 0, false, w);
                    wounded += w;
                    if (wounded >= count) break;
                }
            }
            catch { }
        }

        // ── Helper: permanently remove N troops from the party ────────────────
        private static void KillPartyTroops(int count)
        {
            try
            {
                var roster = MobileParty.MainParty?.MemberRoster;
                if (roster == null) return;
                int killed = 0;
                foreach (var e in roster.GetTroopRoster().ToList())
                {
                    if (e.Character.IsHero || e.Number <= 0) continue;
                    int take = Math.Min(e.Number, count - killed);
                    if (take <= 0) break;
                    roster.AddToCounts(e.Character, -take);
                    killed += take;
                    if (killed >= count) break;
                }
                if (killed > 0) Msg($"({killed} soldier{(killed == 1 ? "" : "s")} found dead at dawn)", BadColor);
            }
            catch { }
        }

        // ── Helper: become Ashen (full conversion sequence) ───────────────────
        private static void BecomeAshen()
        {
            // Sync MageKnowledge player flags first so grimoire + spell aging work correctly.
            // ColourLordRegistry.SetAshen only updates the NPC-tracking sets; MageKnowledge
            // has its own _isMage / _isAshen flags that drive the player-facing UI and mechanics.
            try { MageKnowledge.SetMage(true); }  catch { }
            try { MageKnowledge.SetAshen(true); } catch { }

            try { ColourLordRegistry.SetAshen(Hero.MainHero, true); } catch { }
            try { AshenCitySystem.ApplyAshenPersonality(Hero.MainHero); } catch { }
            try { ColourLordRegistry.SetMage(Hero.MainHero, true); } catch { }
            try { AshenCitySystem.OnPlayerBecameAshen(); } catch { }
            try { MageKnowledge.ApplyAshenAppearance(Hero.MainHero); } catch { }
            // Queue the frenzy event for the next daily tick if there's someone to lose
            if (HasFamilyOrCompanions())
                _ashenFrenzyCountdown = 1;
        }

        // ════════════════════════════════════════════════════════════════════
        // DARK / ASHEN SETTLEMENT EVENTS
        // ════════════════════════════════════════════════════════════════════

        // ── EV_DarknessSpreads — village enter ────────────────────────────────────
        private static void EV_DarknessSpreads(Settlement s)
        {
            string vName = s.Name?.ToString() ?? "the village";
            float scoutChance = SkillChance(DefaultSkills.Scouting, 0.30f);
            string scoutHint  = SkillHint(DefaultSkills.Scouting, 0.30f, "Read the signs for what they are");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★ Darkness in the Roots",
                $"Your scouts found livestock dead in the fields near {vName} — bloodless, cold, " +
                $"facing the same direction. The villagers won't meet your eyes. " +
                $"Someone lit fires in the northern field after midnight, " +
                $"the wrong colour and shape for hearth or harvest. " +
                $"You cannot prove it, but something Ashen has been here recently.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Burn the village. Cultists hide among the innocent here.", null, true,
                        "Fire answers certainty. What it finds is another matter."),
                    new InquiryElement("b", "Spare them. There is no solid proof.", null, true,
                        "Mercy without proof. The outcome will tell you if you were right."),
                    new InquiryElement("c", $"Question the villagers quietly — read the signs for what they are. ({(int)(scoutChance*100)}% Scouting)", null, true,
                        scoutHint),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            try { s.Village.Hearth = Math.Max(10f, s.Village.Hearth * 0.30f); } catch { }
                            ChangeCrime(50f);
                            try { MageKnowledge.AddWhispers(4); } catch { }
                            _burningVillageSettlementId = s.StringId;
                            _burningVillageCountdown    = 14;
                            if (_rng.NextDouble() < 0.5)
                            {
                                ChangeRelWithOwner(s, -60);
                                Msg($"You gave the order. {vName} burned by morning. " +
                                    $"Whether the cultists were inside, or fled, or were never truly there — " +
                                    $"you will not know. The settlement's lord received word before the ash cooled.", BadColor);
                            }
                            else
                            {
                                Msg($"You gave the order. {vName} burned by morning. " +
                                    $"The owner's people came to assess the damage and said nothing in your presence. " +
                                    "They will have something to say in their own time.", BadColor);
                            }
                            break;
                        case "b":
                            if (_rng.NextDouble() < 0.5)
                            {
                                Msg($"You passed through {vName} and rode on. " +
                                    $"The cold feeling faded by nightfall. Perhaps you misread the signs. " +
                                    $"Perhaps the cultists saw your mercy and scattered.", DimColor);
                            }
                            else
                            {
                                try { CampaignMapEvents.SpawnAshenAmbushNear(s.GetPosition2D, 20, 180f); } catch { }
                                Msg($"You showed mercy and rode on. That evening, {vName} caught fire from three sides at once. " +
                                    $"Ashen Spawn poured from the shadows — the cultists had already called for them. " +
                                    $"The village burned regardless of your choice.", BadColor);
                            }
                            break;
                        case "c":
                            if (SkillRoll(DefaultSkills.Scouting, 0.30f))
                            {
                                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                                    "★  The Hidden Root",
                                    $"Your questioning finds a thread. A tanner at the edge of {vName} — not frightened, not defiant, just quietly wrong. The wrong kind of calm for someone who has seen what he has seen. You put the pieces together: one man, acting alone, laying markings in the fields at the Ashen's instruction. He does not know what they mean. He knows what he was paid and what he was threatened with.",
                                    new List<InquiryElement>
                                    {
                                        new InquiryElement("x1", "Execute him publicly — make an example.", null, true,
                                            "Crime +5. Village preserved. Relation with settlement lord +5."),
                                        new InquiryElement("x2", "Exile him. Tell the village what was found.", null, true,
                                            "He escapes. The Ashen lose this agent here, for now."),
                                        new InquiryElement("x3", "Use him — feed false information through the channel.", null, true,
                                            "Calculating +1. Difficult to sustain, but the intelligence value is real."),
                                    },
                                    false, 1, 1, "Decide", "",
                                    sub =>
                                    {
                                        switch (sub?[0]?.Identifier as string)
                                        {
                                            case "x1":
                                                ChangeCrime(5f);
                                                try { s.Village.Hearth = s.Village.Hearth + 50f; } catch { }
                                                ChangeRelWithOwner(s, 5);
                                                Msg("You hold a brief public reckoning. The tanner does not deny it. The village watches. The elder thanks you. The settlement lord, receiving word of how you handled it, revises his opinion of you upward — you found the problem, judged it, and left the village intact.", GoodColor);
                                                break;
                                            case "x2":
                                                Msg("You escort him to the village boundary and tell him what exile means in your jurisdiction: never return, never make contact, and be grateful the alternative was available. He goes. The Ashen network loses this thread — but threads can be replaced.", DimColor);
                                                break;
                                            case "x3":
                                                ShiftTrait(DefaultTraits.Calculating, 1);
                                                Msg("You explain his situation to him precisely. He understands. Whether he cooperates fully or plays both sides is a question you cannot answer without infrastructure you do not have. What you have is a frightened man with divided loyalties and a specific contact in the Ashen's local network. That is worth something.", AshenColor);
                                                break;
                                        }
                                    }, null, "", false), false, true);
                            }
                            else
                            {
                                ChangeRelWithOwner(s, 3);
                                Msg($"You question carefully and find nothing that closes the question. The signs are real. The villagers are hiding something — but whether from fear or loyalty or simply the instinct to protect their own, you cannot determine. You leave them with a warning and a description of what to watch for. The headman writes it down.", DimColor);
                            }
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── Deferred: FireBurningVillageConsequence — 14 days after burning ──
        private static void FireBurningVillageConsequence()
        {
            if (MageKnowledge._deferredInquiry != null) { _burningVillageCountdown = 1; return; }
            string sId = _burningVillageSettlementId;
            _burningVillageSettlementId = null;

            var s = sId != null ? Settlement.All.FirstOrDefault(se => se.StringId == sId) : null;
            string sName = s?.Name?.ToString() ?? "the village";

            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "★  Ashes and Reckoning",
                    $"The settlement lord finds you — or sends a representative who has been riding hard. {sName} is ash. He wants an accounting: who gave the order, what evidence was found, what law was cited. He is not unreasonable. He is also not going to let this pass without a response.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("a", "Justify it — the signs were clear and the threat was real.", null, true,
                            "Leadership roll to persuade him the action was necessary."),
                        new InquiryElement("b", "Pay blood-money. A village has a price.", null, true,
                            "3,000 gold. The lord accepts the payment. He does not accept you."),
                        new InquiryElement("c", "Refuse to account for yourself.", null, true,
                            "He will remember this exactly as long as he lives."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        switch (chosen?[0]?.Identifier as string)
                        {
                            case "a":
                                if (SkillRoll(DefaultSkills.Leadership, 0.35f))
                                {
                                    ChangeRelWithOwner(s, -15);
                                    Msg("He listens with the focused attention of a man deciding whether your judgment can be trusted. He concludes, eventually, that the action was defensible — not right, necessarily, but defensible given the information available. His opinion of you has not recovered. It has stabilised at a specific level of displeasure that will not worsen unless you give it cause.", DimColor);
                                }
                                else
                                {
                                    ChangeRelWithOwner(s, -30);
                                    ChangeRelWithRandomLord(-5);
                                    Msg($"Your justification does not reach him. The signs you described could have been investigated, contained, addressed without fire. He knows this. He tells you he knows this. His displeasure is now formal and documented. Other lords in the region will hear his account of it.", BadColor);
                                }
                                break;
                            case "b":
                                if (!ChangeGold(-3000))
                                {
                                    ChangeRelWithOwner(s, -25);
                                    Msg("You do not have the coin. The lord receives this with the expression of a man who has learned something useful and unpleasant about who he is dealing with.", BadColor);
                                }
                                else
                                {
                                    ChangeRelWithOwner(s, -20);
                                    Msg("The coin changes hands. The lord accepts it formally and says nothing further. He will spend the money and remember the burning. Both things are true simultaneously.", DimColor);
                                }
                                break;
                            case "c":
                                ChangeRelWithOwner(s, -40);
                                ChangeRelWithRandomLord(-10);
                                ChangeCrime(15f);
                                Msg("He records the refusal. He will tell this story for the rest of his life — not loudly, not repeatedly, but accurately and to the right people at the right moments. You have made an enemy of the kind that is patient enough to wait for the moment when it matters.", BadColor);
                                break;
                        }
                    }, null, "", false), false, true);
            };
        }

        // ── EV_BurningWitch — village enter ───────────────────────────────────
        // A young girl is stripped to a stake. The villagers intend to burn her.
        private static void EV_BurningWitch(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★ The Pyre",
                "In the village square, a young girl is bound to a stake. The crowd " +
                "has built a pyre at her feet. \"A witch,\" they say. \"She brings the grey cold " +
                "into our fields.\" Her eyes are dark and frightened — or very still, which is worse. " +
                "The village elder watches you to see what you do.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Let them burn her. She may truly be a witch.", null, true,
                        "The village has made its decision. You agree with it."),
                    new InquiryElement("b", "Watch. You've heard worse ways to spend an afternoon.", null, true,
                        "You watch. You do nothing. This is a choice too."),
                    new InquiryElement("c", "Stop them. There is no proof, only fear.", null, true,
                        "No proof, only fear. Stopping them may not end without consequence."),
                    new InquiryElement("d", "Ride on. You don't have time for this.", null, true,
                        "The road continues."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            try { MageKnowledge.AddWhispers(2); } catch { }
                            Msg("You say nothing. The fire is lit. She does not scream — or she does, and you have already turned away. " +
                                "The village elder nods at your back as you leave.", DimColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Mercy, -1);
                            try { MageKnowledge.AddWhispers(3); } catch { }
                            Msg("You watch. The crowd watches you watching. " +
                                "Something in you marks this moment and files it under things you have become.", BadColor);
                            _burningWitchOutcome   = 1;
                            _burningWitchCountdown = 20;
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            try { MageKnowledge.RemoveWhispers(3); } catch { }
                            if (_rng.NextDouble() < 0.5)
                            {
                                // She was Ashen — casts Curse before dying
                                int w = 5 + _rng.Next(8);
                                WoundPartyTroops(w);
                                try { AgePlayer(3); } catch { }
                                Msg($"You step forward. The crowd parts. The girl raises her head — " +
                                    $"and her eyes are grey. Not frightened. Cold. She speaks one word " +
                                    $"and your soldiers cry out. {w} of them are clutching wounds " +
                                    $"that were not there a moment ago. She was what they said she was.", BadColor);
                            }
                            else
                            {
                                Msg("You step forward. The crowd parts. The girl raises her head — " +
                                    "her eyes are human and wet and terrified. You cut her free. " +
                                    "The elder says nothing. The villagers say nothing. " +
                                    "You ride out with a girl who was not a witch, and the knowledge " +
                                    "of what would have happened if you had kept riding.", GoodColor);
                                _burningWitchOutcome   = 3;
                                _burningWitchCountdown = 90;
                            }
                            break;
                        case "d":
                            Msg("You ride on. Behind you, the fire takes hold. " +
                                "There was nothing you could have done. You choose not to decide whether that is true.", DimColor);
                            _burningWitchOutcome   = 2;
                            _burningWitchCountdown = 7;
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── Deferred: FireBurningWitchConsequence ─────────────────────────────
        private static void FireBurningWitchConsequence()
        {
            if (MageKnowledge._deferredInquiry != null) { _burningWitchCountdown = 1; return; }
            int outcome = _burningWitchOutcome;
            _burningWitchOutcome = 0;

            switch (outcome)
            {
                case 1: // watched — lord's gossip reaches a lord
                    MageKnowledge._deferredInquiry = () =>
                    {
                        ChangeRelWithRandomLord(-8);
                        Msg("A lord who heard the story from a travelling merchant — who heard it from a drover who was passing through — has a specific opinion of you now. He is not outraged. He simply knows you were there, and watched, and moved on. In his circle, that is the kind of thing that travels. He has mentioned it twice.", BadColor);
                    };
                    break;

                case 2: // rode past — messenger arrives with the ending
                    MageKnowledge._deferredInquiry = () =>
                    {
                        if (_rng.NextDouble() < 0.5)
                        {
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("A messenger finds you — sent by the village elder. The girl escaped. Nobody is clear on how: the rope was cut cleanly, the guards found asleep standing, the gate open. The elder's message says only that she is gone and that no one is looking for her. He seems to want you to know. You are not sure why.", GoodColor);
                        }
                        else
                        {
                            Msg("Word reaches you eventually, the way bad news reaches people who have chosen not to stay for it: the village burned what it intended to burn. The elder sent no messenger. You receive no letter. You simply know, from the particular silence that follows certain kinds of decisions, that you rode past something that ended.", DimColor);
                        }
                    };
                    break;

                case 3: // saved, she was human — she finds you
                    MageKnowledge._deferredInquiry = () =>
                    {
                        MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                            "✦  Three Months Later",
                            "She finds you at a market two settlements from where you last saw her — cleaner, steadier, wearing the particular composure of someone who spent three months deciding what they owe. She says she has learned a trade. She says she has been looking for you. She would like to be useful, if you will let her.",
                            new List<InquiryElement>
                            {
                                new InquiryElement("a", "Take her on. She has earned the asking.", null, true, ""),
                                new InquiryElement("b", "Decline. The road is not a safe place for gratitude.", null, true, ""),
                            },
                            false, 1, 1, "Decide", "",
                            sub =>
                            {
                                switch (sub?[0]?.Identifier as string)
                                {
                                    case "a":
                                        ShiftTrait(DefaultTraits.Mercy, 1);
                                        ChangeRenown(10f);
                                        AddMorale(5f);
                                        Msg("She joins your party with the practical efficiency of someone who has already decided this is where she is going and simply needed your agreement. Your men receive her without ceremony. She earns her place inside a week.", GoodColor);
                                        break;
                                    case "b":
                                        ShiftTrait(DefaultTraits.Honor, 1);
                                        Msg("You tell her the road is what it is and gratitude has a short life in it. She listens without argument. She shakes your hand, as though completing a transaction, and finds another path. You see her once more, years later, across a market square — doing well enough that no explanation is needed.", GoodColor);
                                        break;
                                }
                            }, null, "", false), false, true);
                    };
                    break;
            }
        }

        // ── EC_LocalPriest — town enter ───────────────────────────────────────
        // A city priest asks for funding to establish a sanctuary here.
        private static void EC_LocalPriest(Settlement s)
        {
            string cName  = s.Name?.ToString() ?? "the city";
            bool   exists = SanctuaryCampaignBehavior.HasSanctuary(s);
            if (exists) return; // already has a sanctuary; skip

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★ The Priest at the Gate",
                $"A worn priest intercepts you at the city gate of {cName}. " +
                $"He speaks quickly — he has been turned away by two lords already. " +
                $"He wants to build a sanctuary here: a place where the honourable can seek " +
                $"blessing, healing, and protection against the Ashen. He needs coin. A great deal of it.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Donate 10,000 denars — build it properly.", null, true,
                        "A serious investment. The sanctuary will be built."),
                    new InquiryElement("b", "Give 5,000 denars — half now, half later.", null, true,
                        "Half the sum. Whether it is enough remains to be seen."),
                    new InquiryElement("c", "Spare 500 denars — something is better than nothing.", null, true,
                        "A small sum. The priest will do what he can with it."),
                    new InquiryElement("d", "Turn him away. You have nothing to give.", null, true,
                        "He was turned away by two lords already. A third changes nothing."),
                    new InquiryElement("e", "Have him beaten and driven off.", null, true,
                        "He will not return. Neither will some who heard what you did."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (!ChangeGold(-10000)) break;
                            SanctuaryCampaignBehavior.AddPermanentSanctuary(s.StringId);
                            try { MageKnowledge.RemoveWhispers(5); } catch { }
                            Msg($"You give him the coin without ceremony. He bows once and says nothing further. " +
                                $"Within a week, the sanctuary of {cName} is open. " +
                                $"The flame burns clean inside it.", GoodColor);
                            break;
                        case "b":
                            if (!ChangeGold(-5000)) break;
                            if (_rng.NextDouble() < 0.5)
                            {
                                SanctuaryCampaignBehavior.AddPermanentSanctuary(s.StringId);
                                Msg($"Half the sum was enough to begin. The sanctuary of {cName} opens its doors. " +
                                    $"The priest sent you a short note of thanks that says more than it appears to.", GoodColor);
                            }
                            else
                            {
                                Msg($"The coin was not enough to complete the work. The foundations are laid " +
                                    $"but the doors have not opened. The priest writes that he will find the rest.", DimColor);
                            }
                            break;
                        case "c":
                            if (!ChangeGold(-500)) break;
                            if (_rng.NextDouble() < 0.05)
                            {
                                SanctuaryCampaignBehavior.AddPermanentSanctuary(s.StringId);
                                Msg($"You drop a handful of coin into his hands and ride on. A month later, " +
                                    $"word reaches you: the sanctuary of {cName} is open. " +
                                    $"Your small gift opened a door no one expected.", GoodColor);
                            }
                            else
                            {
                                Msg("You give what you can. He thanks you with a quiet dignity that makes the small sum feel smaller. " +
                                    "It was not enough, but it was something.", DimColor);
                            }
                            break;
                        case "d":
                            Msg("You pass him without stopping. He watches you go, then turns back to the gate to wait for the next lord.", DimColor);
                            break;
                        case "e":
                            ShiftTrait(DefaultTraits.Mercy, -1);
                            try { MageKnowledge.AddWhispers(3); } catch { }
                            Msg("Your soldiers scatter him from the gate. He does not return. " +
                                "The city remembers you were there.", BadColor);
                            _priestBeatCountdown = 14;
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── Deferred: FirePriestBeatConsequence — 14 days after beating the priest ──
        private static void FirePriestBeatConsequence()
        {
            if (MageKnowledge._deferredInquiry != null) { _priestBeatCountdown = 1; return; }
            MageKnowledge._deferredInquiry = () =>
            {
                var templeLords = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && !h.IsPrisoner && h != Hero.MainHero
                             && h.MapFaction?.StringId == "vlandia")
                    .ToList();
                foreach (var lord in templeLords)
                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, -10, false); } catch { }
                if (templeLords.Count > 0)
                    Msg("(All Templar lords: −10 relation)", BadColor);
                Msg("The temple received a formal complaint: a lord drove off a priest at the city gate and had him beaten. The complaint includes the city, the gate, the hour, and three witnesses. The temple does not issue threats. It issues records. Your name is in the record, and the record is in the archive, and everyone who needs to know what is in the archive already knows.", BadColor);
            };
        }

        // ── LV_ColdEmbrace — village leave ────────────────────────────────────
        // Resting in the afternoon, a ring of Ashen Spawn closes around you.
        // They reach out the cold and wait.
        private static void LV_ColdEmbrace(Settlement s)
        {
            float athChance = Math.Min(0.90f, 0.35f + (Hero.MainHero?.GetSkillValue(DefaultSkills.Athletics) ?? 0) * 0.003f);

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★ The Circle Closes",
                "You are resting in the afternoon shade outside the village when they arrive. " +
                "A ring of Ashen Spawn — grey-cloaked, cold-eyed — has closed around you without a sound. " +
                "They do not speak. They extend their hands toward you, and the air drops ten degrees. " +
                "They are offering you something.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Embrace the cold. Accept what they offer.", null, true,
                        "The cold does not wait for second thoughts."),
                    new InquiryElement("b", $"Run. Get out of the ring. (Athletics {(int)(athChance*100)}%)", null, true,
                        "Speed may be enough. It may not."),
                    new InquiryElement("c", "Fight them off. Draw your blade — let steel answer the cold.", null, true,
                        "Eight of them. They move without fear. So do you."),
                    new InquiryElement("d", "Burn them with magic. Age 3 days.", null, true,
                        "Fire scatters cold things. The cost is paid in years."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            BecomeAshen();
                            Msg("You reach back. The cold is not a sensation — it is a state. " +
                                "The grey settles into your eyes before you are aware it has begun. " +
                                "The Ashen Spawn lower their hands. You are one of them now.", BadColor);
                            break;
                        case "b":
                            if (_rng.NextDouble() < athChance)
                                Msg("You break from the ring at a dead run, low and fast. " +
                                    "One of them reaches — you feel the cold graze your shoulder " +
                                    "and then you are through and moving and they do not follow. " +
                                    "You do not stop running until the village is behind you.", DimColor);
                            else
                            {
                                WoundPlayer();
                                Msg("You move — but not fast enough. The cold finds you before you clear the ring. " +
                                    "You come through bleeding and slow, the grey chill deep in your shoulder. " +
                                    "They let you go. You cannot decide if that makes it better or worse.", BadColor);
                            }
                            break;
                        case "c":
                            TriggerEncounterBattle(s, 8, ashen: true);
                            Msg("You draw. They do not flinch — they never do. " +
                                "The ring tightens. Whatever happens next happens in the open, " +
                                "blade against cold, until one side stops moving.", BadColor);
                            break;
                        case "d":
                            AgePlayer(3);
                            try { MageKnowledge.AddWhispers(1); } catch { }
                            Msg("The fire comes from somewhere older than your hands. " +
                                "They scatter before it — cold things do not like what burns. " +
                                "They are gone in seconds. You are three days older. " +
                                "Some costs are paid faster than others.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── LV_ColdDream — village leave ──────────────────────────────────────
        // Sleeping at the village inn, the cold reaches into your dreams.
        private static void LV_ColdDream(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★ Ash in the Dream",
                "You slept at the village inn and woke before dawn, cold and certain. " +
                "The dream was vivid: grey plains stretching without horizon, " +
                "something vast and patient moving at the edge of it. " +
                "It looked at you — not with eyes — and extended an invitation. " +
                "You are awake now. The decision feels more real than waking usually does.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Accept the invitation. Join the cold.", null, true,
                        "Invitations from the cold are rarely what they appear to be."),
                    new InquiryElement("b", "Refuse. It was only a dream.", null, true,
                        "It was only a dream."),
                    new InquiryElement("c", "Reach back — try to learn what it wants.", null, true,
                        "Reaching toward the cold is not without risk. What it wants is not nothing."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            BecomeAshen();
                            Msg("You answer the invitation. By morning you are different in ways you cannot fully describe yet. " +
                                "Your reflection in the horse trough shows grey at the edges of your eyes.", BadColor);
                            break;
                        case "b":
                            try { MageKnowledge.RemoveWhispers(2); } catch { }
                            Msg("You get up, drink cold water, and decide it was only a dream. " +
                                "You are probably right. The world looks normal in daylight. " +
                                "It usually does.", DimColor);
                            break;
                        case "c":
                        {
                            double roll = _rng.NextDouble();
                            if (roll < 0.30)
                            {
                                WoundPlayer();
                                Msg("You reach toward it and it reaches back — harder than you expected. " +
                                    "You come awake on the floor, bleeding from nowhere you can explain. " +
                                    "The dream is gone. The wounds are not.", BadColor);
                            }
                            else if (roll < 0.50)
                            {
                                BecomeAshen();
                                Msg("You reach toward it and it takes you the rest of the way. " +
                                    "You learn what it wants. It wants everything. " +
                                    "By the time you understand that, the grey is already in your eyes.", BadColor);
                            }
                            else
                            {
                                try { Hero.MainHero.HeroDeveloper.UnspentFocusPoints += 1; } catch { }
                                try { MageKnowledge.AddWhispers(5); } catch { }
                                Msg("You reach toward it carefully, like touching something hot from the side. " +
                                    "You pull back before it pulls you in — but you bring something with you: " +
                                    "a clarity, a sense of how things connect. One focus point, " +
                                    "paid for in proximity to something you do not fully understand.", GoodColor);
                            }
                            break;
                        }
                    }
                }, null, "", false), false, true);
        }

        // ── LV_ThreeWitches — village leave ───────────────────────────────────
        // Three figures dance around a fire in the dark at the crossroads.
        private static void LV_ThreeWitches(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★ Three Figures at the Crossroads",
                "Your party crests the hill above the crossroads and stops. " +
                "Below, three women are dancing around a fire that burns colours that do not exist in wood. " +
                "They see you. One raises a hand — an invitation, not a threat. " +
                "The fire is warm from here. Your scouts are very still.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Join them at the fire.", null, true,
                        "The dance is not something you will remember clearly in daylight."),
                    new InquiryElement("b", "Ride past without stopping.", null, true,
                        "The crossroads is quiet behind you."),
                    new InquiryElement("c", "Intervene — scatter the rite.", null, true,
                        "You stop something. They may not go quietly."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            try { AgingSystem.RejuvenateHero(Hero.MainHero, 730); } catch { } // ~2 years
                            ShiftTrait(DefaultTraits.Honor, -2);
                            ShiftTrait(DefaultTraits.Mercy, -2);
                            try { MageKnowledge.AddWhispers(8); } catch { }
                            Msg("You ride down and dismount at the fire. They make space for you without speaking. " +
                                "The dance is not something you will remember clearly in daylight. " +
                                "You feel two years lighter when you leave. You feel heavier in other ways.", DimColor);
                            break;
                        case "b":
                            Msg("You ride past without slowing. One of them watches you go, " +
                                "fire still burning in the hollow of her hand. " +
                                "The crossroads is quiet behind you.", DimColor);
                            break;
                        case "c":
                            try { Hero.MainHero.HeroDeveloper.UnspentFocusPoints += 1; } catch { }
                            try { MageKnowledge.RemoveWhispers(3); } catch { }
                            if (_rng.NextDouble() < 0.5)
                            {
                                AgePlayer(365); // 1 year
                                Msg("You ride in fast and scatter them — they break and vanish into the dark, " +
                                    "one of them turning as she runs. You feel something land on you, " +
                                    "light and cold: a curse, spoken quickly, without ceremony. " +
                                    "You are one year older. You stopped something, though. " +
                                    "You take the focus point and the year and call it even.", DimColor);
                            }
                            else
                            {
                                Msg("You ride in and scatter them. They vanish without a word. " +
                                    "The fire goes out as you cross it. " +
                                    "Something remains where the fire was — a clarity, a residue of interrupted power. " +
                                    "You take what you can carry. One focus point, ungiven but yours now.", GoodColor);
                            }
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── LV_HollowHour — village leave, general ────────────────────────────
        // The darkest part of the night settles over camp — something unnatural
        // rides in the fog. Three soldiers die if the player sleeps through it.
        private static void LV_HollowHour(Settlement s)
        {
            Hero hero = Hero.MainHero;
            bool mage = MageKnowledge.IsMage;

            float leadChance = SkillChance(DefaultSkills.Leadership, 0.35f);
            string leadHint  = SkillHint(DefaultSkills.Leadership, 0.35f, "Rally them — your voice cuts the fog");

            float athChance  = SkillChance(DefaultSkills.Athletics, 0.35f);
            string athHint   = SkillHint(DefaultSkills.Athletics, 0.35f, "Move fast enough to wake them physically");

            bool prayGate = hero != null
                         && hero.GetTraitLevel(DefaultTraits.Honor) >= 1
                         && hero.GetTraitLevel(DefaultTraits.Mercy) >= 1
                         && (MobileParty.MainParty?.Morale ?? 0f) >= 60f;
            string prayHint = prayGate
                ? "Your faith is unbroken and your men's hearts are high. The prayer holds."
                : "Requires Honourable and Merciful traits and party morale of 60 or above.";

            string mageHint = mage
                ? "Push the fire outward — cold auras cannot hold against it. Costs a day."
                : "Requires mage ability.";

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★ The Hollow Hour",
                "You wake in the deep of night without knowing why. The camp is wrong — " +
                "too quiet, the fires too low, the air thick with cold that has no wind behind it. " +
                "A pale fog has rolled in from nowhere: close, slow, and patient. " +
                "Your men sleep on, but their breathing is shallow and ragged. " +
                "One of them whimpers. Another goes still. This is not weather. " +
                "Something is feeding on the dark and your camp is in the middle of it.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", $"Shout your men awake. ({(int)(leadChance * 100)}% Leadership)", null, true,
                        leadHint),
                    new InquiryElement("b", "Go back to sleep. It is only fog.", null, true,
                        "Fog. Just fog. That is what you tell yourself."),
                    new InquiryElement("c", "Dispel the darkness with fire. (Mage — 1 day)", null, mage,
                        mageHint),
                    new InquiryElement("d", $"Move through camp and shake them awake. ({(int)(athChance * 100)}% Athletics)", null, true,
                        athHint),
                    new InquiryElement("e", "Kneel and pray until the darkness passes.", null, prayGate,
                        prayHint),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Leadership, 0.35f))
                            {
                                AddMorale(5f);
                                Msg("Your voice cuts through the fog before you have fully thought about it — " +
                                    "command instinct, louder than the wrongness. Heads come up around the camp, " +
                                    "eyes unfocused, hands reaching for weapons by habit. " +
                                    "The aura breaks against the noise and the motion. " +
                                    "By the time dawn comes your men are quieter than usual but alive and present. " +
                                    "They don't ask what you drove off. They can tell something was there.", GoodColor);
                            }
                            else
                            {
                                KillPartyTroops(3);
                                AddMorale(-10f);
                                Msg("You shout — but the fog swallows your voice like cloth swallows water. " +
                                    "The words don't land. The men who were already deep in it don't surface. " +
                                    "By dawn three of them are cold. " +
                                    "No wounds. No mark. Just gone quiet in the night while you stood there shouting into nothing.", BadColor);
                            }
                            break;
                        case "b":
                            KillPartyTroops(3);
                            AddMorale(-10f);
                            Msg("You pull your blanket up and close your eyes. " +
                                "The nightmares come without warning — not vivid ones, just a pressure, " +
                                "a slow sense of something settling over you, of breath becoming harder to draw. " +
                                "You wake at dawn to find three men who did not. " +
                                "No wounds. No marks. Just cold. " +
                                "The fog is gone. The rest of your men look at you and say nothing.", BadColor);
                            break;
                        case "c":
                            AgePlayer(1);
                            AddMorale(5f);
                            Msg("You push the fire outward — not a spell, exactly, more a refusal: " +
                                "warmth moving against cold in the way warmth does when it remembers what it is. " +
                                "The fog pulls back in sections, like cloth being peeled from something wet. " +
                                "Gone before the sky lightens. Your men sleep through it entirely. " +
                                "They wake rested, which is unusual on a cold march. " +
                                "You are a day older. You count it worth the cost.", FireColor);
                            break;
                        case "d":
                            if (SkillRoll(DefaultSkills.Athletics, 0.35f))
                            {
                                AddMorale(5f);
                                Msg("You move fast — camp to camp, shoulder to shoulder, " +
                                    "hands on men's arms and boots on their bedrolls. " +
                                    "It is ungraceful and it works. The ones you reach in time surface confused but breathing. " +
                                    "The aura needs stillness; you denied it stillness. " +
                                    "By dawn the fog is thin and ordinary and your camp is intact.", GoodColor);
                            }
                            else
                            {
                                KillPartyTroops(3);
                                AddMorale(-10f);
                                Msg("You move — but the fog is thicker than it looked and the camp is larger than your legs. " +
                                    "You reach most of them. Three you don't reach in time. " +
                                    "They were already too deep in it when you got there. " +
                                    "Cold in their blankets, faces slack, gone quietly in the part of the night that forgets them.", BadColor);
                            }
                            break;
                        case "e":
                            Msg("You kneel at the edge of the camp and pray — not quickly, not as habit, " +
                                "but with the full weight of someone who believes the words mean something. " +
                                "The fog thins. Not fast, but steadily, like something deciding not to stay. " +
                                "Your men sleep on, undisturbed. By first light it is gone. " +
                                "Nothing happened. In the hollow hour, that is the whole point.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

    }
}
