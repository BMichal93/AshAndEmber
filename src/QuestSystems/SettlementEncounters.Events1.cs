// =============================================================================
// ASH AND EMBER — SettlementEncounters.Events1.cs
// Gate/village/city skill-check encounters and their consequences.
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
        // ── Deferred: FireVengefulKnightConsequence — 7–11 days after EC_PoorKnight option C ──
        private static void FireVengefulKnightConsequence()
        {
            if (MageKnowledge._deferredInquiry != null) { _vengefulKnightCountdown = 1; return; }
            ChangeRelWithRandomLord(-8);
            ChangeRenown(-5f);
            MageKnowledge._deferredInquiry = () =>
                Msg("Word finds you on the road: the knight you laughed at in the tournament has been telling anyone who will listen. " +
                    "Not about the loss — about who laughed, and why, and what that means about you. " +
                    "A lord who heard him agrees. Your reputation has taken a small but specific hit.", BadColor);
        }

        // ═══════════════════════════════════════════════════════════════════
        // NEW ENCOUNTERS — LEAVE CITY (mage, non-Ashen, clan tier ≥ 2)
        // ═══════════════════════════════════════════════════════════════════

        // LC_YoungMageHope — A young mage afraid of the Ashen asks about hope [Leadership]
        private static void LC_YoungMageHope(Settlement s)
        {
            float leadershipChance = SkillChance(DefaultSkills.Leadership, 0.35f);
            string hint = SkillHint(DefaultSkills.Leadership, 0.35f, "Steady them — give them something to hold");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  There Is Always Hope",
                "A young man waits at the city gate with the stiff posture of someone who practiced what they would say and forgot it anyway. " +
                "He can feel the fire in you from here. His own gift is new — two years, maybe three. " +
                "He has heard what the Ashen do to people like him. He wants to know if it has to end that way.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "\"There is always hope. If you know what you are, you can choose what you become.\"", null, true, hint),
                    new InquiryElement("b", "Ignore him. You have somewhere to be.", null, true,
                        "He watches you ride out. The gate closes."),
                    new InquiryElement("c", "\"Run. Leave this city and don't come back.\"", null, true, hint),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Leadership, 0.35f))
                            {
                                ShiftTrait(DefaultTraits.Honor, 1);
                                ChangeRenown(5f);
                                Msg("He listens to you — not to the words, but to the way you say them. Something settles in him, then hardens. " +
                                    "A week later, word reaches you from the north: a young mage was seen leading a small band of volunteers against an Ashen raiding column. " +
                                    "They held the village road. Against expectation, they held it. " +
                                    "His name is already travelling faster than he is. He inspired them not by being powerful — by being certain.", GoodColor);
                                // Deferred consequence: castle town stirs — rebellion chance in ~14 days
                                _hopeMageConsequenceCountdown = 14;
                                _hopeMageSettlementId = s.StringId;
                            }
                            else
                            {
                                Msg("You give him the words but not the weight — the right speech without the certainty behind it. " +
                                    "He nods and thanks you and you can see he is no more certain than before. " +
                                    "He will make a choice based on fear, not on what you said. You do not know what that choice will be.", DimColor);
                                // Youth defects to Ashen — no mechanical consequence, narrative only
                                Msg("Three days later, the city guard reports a young man was seen leaving north " +
                                    "toward the grey hills. He took nothing but his coat.", BadColor);
                            }
                            break;
                        case "b":
                            Msg("He watches you ride out. His question travels with you farther than it should. " +
                                "You had an answer. You chose not to spend it.", DimColor);
                            break;
                        case "c":
                            if (SkillRoll(DefaultSkills.Leadership, 0.35f))
                            {
                                ShiftTrait(DefaultTraits.Honor, 1);
                                ChangeRenown(5f);
                                Msg("He takes the warning seriously — the directness of it lands where the gentler version wouldn't. " +
                                    "He is gone from the city by nightfall. You will not hear from him again, " +
                                    "but something in how he moved when you spoke told you he understood exactly what 'run' meant.", GoodColor);
                                // No city rebellion — he simply escaped; that is the whole of this outcome
                            }
                            else
                            {
                                Msg("He hears the urgency but not the reason. He runs — but without direction, without a plan, " +
                                    "toward the grey hills rather than away from them. " +
                                    "Your warning sent him exactly where you were warning him away from.", BadColor);
                                Msg("A week later, the Ashen gain a recruit.", BadColor);
                                // No city rebellion — he went to the wrong side
                            }
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // NEW ENCOUNTERS — AFTER BATTLE (clan tier ≥ 3, lost battle)
        // ═══════════════════════════════════════════════════════════════════

        // EB_HeroInspired — Fleeing refugees after a lost battle; hard choices
        private static void EB_HeroInspired()
        {
            float ridingChance  = SkillChance(DefaultSkills.Riding, 0.38f);
            float combatChance  = SkillChance(DefaultSkills.OneHanded, 0.35f);
            string ridingHint   = SkillHint(DefaultSkills.Riding, 0.38f, "Get the children out before the pursuit catches you");
            string combatHint   = SkillHint(DefaultSkills.OneHanded, 0.35f, "Hold them long enough for the others to clear the road");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  What the Road Holds",
                "Retreating, you overtake a knot of villagers fleeing the same way — two dozen people with what they could carry. " +
                "Children. A cart with one wheel about to fail. Soldiers from the other side are on the road behind you and closing. " +
                "These people will not outrun them.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Use them to slow the pursuit — they are not your concern.", null, true,
                        "The pursuit slows. You clear the road. The villagers do not."),
                    new InquiryElement("b", "Get the children on horseback and ride hard.", null, true, ridingHint),
                    new InquiryElement("c", "Turn and hold the pursuit long enough for them to clear the road.", null, true, combatHint),
                    new InquiryElement("d", "Ride on. You cannot fight a retreat and a rescue.", null, true,
                        "You ride on. The sound behind you is what it is."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            ShiftTrait(DefaultTraits.Mercy, -1);
                            Msg("The villagers scatter into the fields as the soldiers push through. " +
                                "Not all of them make it to the treeline. Your retreat clears. " +
                                "Your men do not look at each other.", BadColor);
                            break;
                        case "b":
                            if (SkillRoll(DefaultSkills.Riding, 0.38f))
                            {
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                Msg("You load what you can onto your horses and ride hard. The pursuit reaches the abandoned cart and slows. " +
                                    "Enough distance opens. You clear the road with seven children who will grow up knowing exactly what happened at that junction.", GoodColor);
                            }
                            else
                            {
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                WoundMainHero(2);
                                Msg("You get the children mounted but the pursuit is faster than the road allows. " +
                                    "You take two wounds getting the last group clear — an arrow in the shoulder, then a blade across your arm at the horse's flank. " +
                                    "You clear the road. Some of the children arrive before you do, and have to wait.", GoodColor);
                            }
                            break;
                        case "c":
                            if (SkillRoll(DefaultSkills.OneHanded, 0.35f))
                            {
                                ShiftTrait(DefaultTraits.Honor, 1);
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                ChangeRenown(15f);
                                Msg("You turn and hold the road alone long enough for the villagers to reach the treeline. " +
                                    "The pursuit pulls back when they realise what they are dealing with. " +
                                    "The story of the lord who turned on a routed road travels faster than you do.", GoodColor);
                            }
                            else
                            {
                                // Permadeath risk — player is wounded seriously
                                ShiftTrait(DefaultTraits.Honor, 1);
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                ChangeRenown(100f);
                                WoundMainHero(5);
                                Msg("You hold them. You hold them longer than anyone watching expected. " +
                                    "The villagers clear the road. You do not clear the road afterward with the same ease. " +
                                    "The story of what you did at that junction will outlast the battle that preceded it.", GoodColor);
                                // Critical wound — check if fatal
                                if (Hero.MainHero != null && Hero.MainHero.IsWounded)
                                    MBInformationManager.AddQuickInformation(new TextObject(
                                        "You are badly wounded. The surgeons are doing what they can."));
                            }
                            break;
                        case "d":
                            Msg("You ride on. The sound behind you is what it is. Your men ride with you " +
                                "and do not say anything, which is its own kind of verdict.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        private static void WoundMainHero(int count)
        {
            try
            {
                var roster = MobileParty.MainParty?.MemberRoster;
                if (roster == null || Hero.MainHero == null) return;
                // Wound the hero directly by marking them wounded (simulated as party morale loss + hero wound)
                Hero.MainHero.HitPoints = Math.Max(1, Hero.MainHero.HitPoints - count * 10);
            }
            catch { }
        }

        // ═══════════════════════════════════════════════════════════════════
        // NEW ENCOUNTERS — ENTER VILLAGE/TOWN (Ashen only, random)
        // ═══════════════════════════════════════════════════════════════════

        // EV_MemoryHunger — Ashen only; fading memories; potentially fatal choice
        private static void EV_MemoryHunger(Settlement s)
        {
            float leadershipChance = SkillChance(DefaultSkills.Leadership, 0.30f);
            string hint = SkillHint(DefaultSkills.Leadership, 0.30f, "Resist the hunger — hold what remains");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Memory-Hunger",
                "It arrives without announcement: a sudden and absolute certainty that something you once knew — " +
                "the texture of a particular moment, a conversation, a face you loved — is no longer retrievable. " +
                "The cold in you is not mourning it. It is hungry. " +
                "It wants to finish what it started.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Try to remember — force the memory back from the edge.", null, true, hint),
                    new InquiryElement("b", "Give in — let it take what it wants.", null, true,
                        "What you give it, it will use. You will not be the same after. " +
                        "You may not survive the exchange at all."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Leadership, 0.30f))
                            {
                                Msg("You hold it. Not the memory — the memory is gone and does not return — but yourself. " +
                                    "The hunger reaches a limit and stops. You are in a village street " +
                                    "and you are still you, with one fewer thing that was yours. " +
                                    "The cold continues at the same pace. It is patient.", AshenColor);
                                ShiftTrait(DefaultTraits.Mercy, -1);
                            }
                            else
                            {
                                Msg("You reach for it and find the reaching itself has been consumed. " +
                                    "When you surface you are standing in the street and you do not know how long you have been standing. " +
                                    "Your men are watching you from a careful distance. " +
                                    "In the hours you cannot account for, money changed hands — debts settled with people you cannot now name, " +
                                    "coin pressed into fists for reasons that made complete sense to whoever was doing it. " +
                                    "Something has passed through you and the thing that came out the other side is quieter than what went in.", BadColor);
                                ShiftTrait(DefaultTraits.Mercy, -1);
                                ShiftTrait(DefaultTraits.Generosity, -1);
                                ChangeGold(-500);
                            }
                            break;
                        case "b":
                            // Give in: big XP gain but 7-day death timer
                            try { Hero.MainHero.HeroDeveloper.UnspentFocusPoints += 2; } catch { }
                            ShiftTrait(DefaultTraits.Mercy, -2);
                            ShiftTrait(DefaultTraits.Honor, -1);
                            Msg("You open the door. Something floods through you — cold, complete, and enormously purposeful. " +
                                "You understand things about the fire that you did not understand before. " +
                                "You cannot remember what you gave it. " +
                                "Your men back away from you without being told to.", AshenColor);
                            MBInformationManager.AddQuickInformation(new TextObject(
                                "The cold is consuming you. Something fundamental is leaving. You have perhaps seven days."));
                            // Schedule a deferred "dissolution" event — player receives warning; after 7 days, game over
                            _memoryHungerConsumed     = true;
                            _memoryHungerCountdown    = 7;
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // EVENTS 118–119 — AFTER SIEGE (fourth batch)
        // ═══════════════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════════════
        // EVENT 120 — AFTER RAID (fourth batch)
        // ═══════════════════════════════════════════════════════════════════

        // 120. Under the Floor - removed


        // 129. Left Behind (mage, after siege)
        private static void ES4_AshenCrystal()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  Left Behind",
                "In a room off the keep's great hall, placed on a shelf between two books as if it belonged there: a small object of grey stone that is cold in a way that has nothing to do with temperature. The Ashen put this here before the siege began — possibly years before. It is a marker. It means: we were here. We will return for it.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Destroy it completely.", null, true,
                        "An hour's careful work. The cost is in your hands and your years. The thing is gone."),
                    new InquiryElement("b", "Keep it. Knowing it exists is an advantage.", null, true,
                        "You carry a gap in their network. They will eventually notice the silence."),
                    new InquiryElement("c", "Leave it in place — let them think nothing was found.", null, true,
                        "They will find the marker undisturbed. The deception is yours to manage."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgePlayer(1);
                            Msg("You work at it for an hour. The fire does not like what it touches — there is a resistance that is not physical, the cold pushing back against the warmth. Eventually it yields. The stone cracks and the cold in it is gone. The room is just a room. The cost is in your hands and in your years.", FireColor);
                            break;
                        case "b":
                            Msg("You wrap it in cloth and keep it separate from everything else. It will be cold to the touch for as long as you carry it. The Ashen use these to locate each other across distances. You now own a gap in their network. How long before the gap is noticed is a question without an answer yet.", AshenColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You leave it exactly where it is, touching nothing. When the Ashen return — and they will return — they will find the keep changed but the marker undisturbed. They will conclude their absence was unnoticed. You will know they concluded that. That is a small and specific advantage.", DarkColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // GATE-AMBUSH & MAGE-DUEL EVENTS
        // ═══════════════════════════════════════════════════════════════════

        // ── Gate-ambush helper ─────────────────────────────────────────────
        private static readonly Color WarnColor = new Color(0.80f, 0.30f, 0.15f);

        private static void SpawnAshenAtGate(Settlement s, int troops, float minStrength)
        {
            try { CampaignMapEvents.SpawnAshenAmbushNear(s.GetPosition2D, troops, minStrength); }
            catch { }
        }

        // Spawns a hostile party and immediately starts a field battle mission.
        // Uses PlayerEncounter.Start/SetupFields/StartBattle for a direct transition;
        // if that API path fails the party is still placed adjacent to the player
        // so Bannerlord's own encounter detection fires on the next campaign tick.
        private static void TriggerEncounterBattle(Settlement s, int troops, bool ashen = false)
        {
            try
            {
                var main = MobileParty.MainParty;
                if (main == null) return;

                var enemy = CampaignMapEvents.SpawnCombatPartyAt(s.GetPosition2D, troops, ashen);
                if (enemy == null) return;

            }
            catch { }
        }

        // ── LEAVE CITY/CASTLE: An Insult at the Gate ─────────────────────────
        // A drunk lord's retainer blocks your path. The "fight" option calls
        // TriggerEncounterBattle which spawns the enemy and starts the real battle.
        private static void EL_InsultAtGate(Settlement s)
        {
            string charmHint = SkillHint(DefaultSkills.Charm, 0.35f, "Silence him without a blade");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  An Insult at the Gate",
                "A lord's retainer — drunk, loud, and clearly off a leash — blocks your path at the city gate. He makes a comment about your banner. Then about your horse. Then about you. He has four men behind him who look just sober enough to hold weapons. The gate guards are watching from thirty paces and doing nothing.",
                new List<InquiryElement>
                {
                    new InquiryElement("walk",  "Walk past. He is not worth the blood.", null, true,
                        "He'll say something worse as you pass. You will not turn around."),
                    new InquiryElement("words", "Answer him. One sentence, precisely chosen.", null, true, charmHint),
                    new InquiryElement("fight", "Draw. He asked for this with every word.", null, true,
                        "He and his men will find out what that costs. Right here, right now."),
                    new InquiryElement("coin",  "Pay the gate guards to remove him.", null, true,
                        "Thirty coin. Fast. Quiet. Done."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "walk":
                            Msg("He shouts something at your back. You do not turn around. You can hear him crowing to his men as you ride. It means nothing. You remember it anyway.", DimColor);
                            break;
                        case "words":
                            if (SkillRoll(DefaultSkills.Charm, 0.35f))
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                ChangeRenown(5f);
                                Msg("You stop. You say one thing — something he cannot answer without making himself smaller in front of his own men. The silence that follows is very loud. He lets you pass.", GoodColor);
                            }
                            else
                            {
                                ShiftTrait(DefaultTraits.Honor, -1);
                                Msg("You try. He interrupts. His men laugh. You ride out having said something that didn't land — and he knows it.", BadColor);
                            }
                            break;
                        case "fight":
                            TriggerEncounterBattle(s, 5);
                            Msg("You draw. He does too, half a second slower, and his men scramble behind him. Whatever happens next happens in the open, in daylight, with witnesses.", BadColor);
                            break;
                        case "coin":
                            ChangeGold(-30);
                            ChangeRelWithOwner(s, 2);
                            Msg("Thirty coin to the senior guard. He walks over, says three words. The retainer moves off — not happy, but moving. You ride out.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── ENTER VILLAGE: The Self-Taught Mage (mage-gated) ───────────────
        private static void EV7_SelfTaughtMage(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Self-Taught",
                "A mage at the village inn — self-trained, clearly capable, and entirely certain that the gift he found alone is the real version and what you carry is a lesser, inherited thing. He says this to your face without hostility, the way people state facts. He has been working with fire for eight years. He is wrong about the comparison. He is not wrong about his eight years.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Show him what the fire actually is — no argument, just the thing itself.", null, true,
                        "A day spent showing him the real thing. He cannot unknow it."),
                    new InquiryElement("b", "Challenge him formally — demonstrate the difference by working the same problem.", null, true,
                        "Working the same problem reveals something. His gift is real, if different."),
                    new InquiryElement("c", "Let him demonstrate first. Eight years of self-teaching is worth hearing.", null, true,
                        "Eight years of self-teaching is worth hearing. He may surprise you."),
                    new InquiryElement("d", "Agree with him — the gift finds what it finds. He's not wrong about that.", null, true,
                        "Agreement disarms the whole conversation. You leave him with something."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgePlayer(1);
                            Msg("You set something on the table between you — not a trick, not a demonstration, just the fire being what it is without the performance layer. He watches it for a long time. His own fire responds to it in a way that surprises him. He doesn't revise his opinion out loud. He revises it where opinions actually live. You ride out with a day of your life spent on a genuine exchange.", FireColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You give him a problem: heat a specific point without warming what surrounds it. He solves it differently than you would — more slowly, with more control over the margins. You solve it faster and with less precision. You both sit with this for a moment. He is not lesser. He is different. The difference matters in specific contexts. Neither of you had clearly understood that before.", FireColor);
                            break;
                        case "c":
                            AddMorale(3f);
                            Msg("He shows you a technique for sustaining a working with a fraction of the attention cost — he found it by accident in the third year and refined it over the following five. You have never seen it. It works. It solves a problem you didn't know you had. He looks at your face when you recognise its value and his certainty quietly reorganises itself around this new data point.", FireColor);
                            break;
                        case "d":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You agree with him — the fire is the fire, wherever it lands. He expected a contest. Your agreement disarms the whole conversation. He sits with it for a moment and then buys you a drink, which is the self-taught mage's version of a concession. You ride out with nothing changed and one person in the world who will speak well of you, specifically, for the rest of his life.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── ENTER VILLAGE: The Old Master's Student (mage, renown ≥ 600) ───
        private static void EV7_OldMastersStudent(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Old Master's Student",
                "A young woman — perhaps twenty — has been waiting at the village for three days, asking every traveler if they match her description. She studied under a mage you have heard of: dead now, one of the old ones who knew more than they ever wrote down. She has a question only someone at your level can answer, and she has been carrying it for two years.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Sit with her. Give her the honest answer.", null, true,
                        "A day spent on a question she has carried two years. The knowledge travels forward."),
                    new InquiryElement("b", "Answer but challenge her framing — the question has a better version.", null, true,
                        "The question has a better version. So does the exchange."),
                    new InquiryElement("c", "Test her first — see what she actually carries before deciding what she can receive.", null, true,
                        "A test reveals what she carries. What you find may surprise you."),
                    new InquiryElement("d", "Tell her she needs to find the answer herself — it won't mean anything received.", null, true,
                        "She will find the answer herself. She has reason to be frustrated."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            AgePlayer(1);
                            Msg("The question was: can the fire grieve. You answer it honestly, which costs more than you expected — the honest answer requires showing her the moment yours did. She takes it in quietly. She thanks you simply. She will teach what you gave her. It will carry your name, eventually, without anyone meaning it to.", FireColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You tell her the question she asked is a narrower version of a better question. She sits with this and produces the better question in about four minutes, which tells you everything about the quality of her training. The better question neither of you can answer fully. You spend an hour working at its edges. The master taught her well. What she does next with it will be hers.", FireColor);
                            break;
                        case "c":
                            AgePlayer(1);
                            Msg("You give her a working to complete — not a display, a test of understanding. She completes it three-quarters correctly and then does something you didn't expect: corrects herself mid-working without stopping, which is harder than getting it right the first time and significantly rarer. Her gift is real and her control is better than yours was at her age. The answer you give her afterward is the best version you have. She is ready for it.", FireColor);
                            break;
                        case "d":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You tell her to find it herself. She is frustrated. She asks why. You tell her: because the answer you find is yours; the answer I give you is mine, and mine won't fit where yours needs to go. She does not thank you. She will find the answer.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ═══════════════════════════════════════════════════════════════════
        // SKILL-CHECK EVENTS — SOFT ROLL (probability scales with skill level)
        // ═══════════════════════════════════════════════════════════════════

        // ── ENTER VILLAGE: The Cold Trail [Scouting] ───────────────────────
        private static void EV8_ColdTrail(Settlement s)
        {
            float chance = SkillChance(DefaultSkills.Scouting, 0.25f);
            string hint  = SkillHint(DefaultSkills.Scouting, 0.25f, "Read the signs accurately");
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Cold Trail",
                "Something passed through this village recently — the signs are subtle and scattered: ash on a doorstep where no fire was lit, a handprint on a well-cover in grey dust, a dog that stopped barking three nights ago and has not started again. The villagers have not connected these things. You have.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Read the full trail — age, numbers, direction, intent.", null, true, hint),
                    new InquiryElement("b", "Warn the village and ask them to watch for more signs.", null, true,
                        "They are alerted. They won't know what to look for."),
                    new InquiryElement("c", "Mark the location and send the coordinates to the nearest garrison.", null, true,
                        "Correct process. Slow response."),
                    new InquiryElement("d", "Note it and keep moving — you have seen this pattern before.", null, true,
                        "The pattern continues."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Scouting, 0.25f))
                            {
                                ChangeRenown(8f);
                                Msg("Three individuals, two nights ago, moving northwest — they stopped at specific houses in a specific order, which means they had a list. They were not foraging or scouting randomly. They were checking names. Whether the names are observation targets or something worse is a question the pattern cannot answer, but the direction and the timing and the specificity all point somewhere that needs to be known. You know it now.", AshenColor);
                            }
                            else
                                Msg("You read what you can. The signs are two nights old — possibly three — and the movement pattern is unclear: you can see that something passed through but the direction fractures into two possibilities and the number could be two or five. What you know is incomplete. You can report what you have, which is better than nothing, which is what the village has.", DimColor);
                            break;
                        case "b":
                            ChangeRelWithOwner(s, 5);
                            Msg("You tell the headman what you saw without explaining what it means. He is the kind of man who will watch for more signs but will not know how to read them. He posts someone near the well at night. It is the wrong choice but it is a choice, and it is his, and he is doing it. You ride on with the mild dissatisfaction of having delegated a task to someone without the tools for it.", DimColor);
                            break;
                        case "c":
                            ChangeRelWithRandomLord(3);
                            Msg("The garrison receives the coordinates and notes them in the patrol log. Whether a patrol reaches the area before the trail is days colder is a scheduling question. You have created a record that exists. That is something.", DimColor);
                            break;
                        case "d":
                            Msg("You note it and ride. The pattern is familiar: preliminary observation, cataloguing. Not action yet. The gap between catalogue and action is the part nobody ever knows in advance. You know the pattern. You did not tell anyone else. That is a small and specific weight.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── ENTER VILLAGE [mage]: The Lie [Roguery] ────────────────────────
        private static void EV8_TheLie(Settlement s)
        {
            float chance = SkillChance(DefaultSkills.Roguery, 0.25f);
            string hint  = SkillHint(DefaultSkills.Roguery, 0.25f, "Read the deception precisely");
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Lie",
                "An old man at the inn table tells you there have been no grey-cloaked visitors in a week. He says this with full eye contact and complete stillness and the specific absence of the small corrections honest people make when they're trying to be accurate. He is lying. Whatever he saw, he was told to say he hadn't. The fire in you feels the cold in the room that isn't the weather.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Read the lie precisely — what is he hiding and who asked him to.", null, true, hint),
                    new InquiryElement("b", "Tell him you know he's lying and give him a chance to correct it.", null, true,
                        "He may talk. He may not. He will know you can read him."),
                    new InquiryElement("c", "Accept the lie and ask everyone else in the inn separately.", null, true,
                        "Someone else may not have been instructed."),
                    new InquiryElement("d", "Leave him to his loyalty. People protect what they have to protect.", null, true,
                        "Loyalty is its own kind of answer."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Roguery, 0.25f))
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                Msg("His hands moved once — toward his left pocket when you said 'visitors', then caught themselves. He was given something to keep and told to say nothing. You do not confront him. You wait until he uses the privy and check the pocket: a folded note with an Ashen symbol and a date three days from now. He was given a message to hold, not just a cover story. The date is a meeting.", AshenColor);
                            }
                            else
                                Msg("You read the performance but not the content behind it — you can see the lie clearly but not what it contains. He was told something and told to deny it. What specifically, you cannot extract from his manner alone. You know the shape of the secret without its substance. That is useful, imprecisely.", DimColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You tell him plainly that you know. He goes very still, then says: \"They told me my family would be checked on.\" He says nothing more. He does not know what they look like without their cloaks, where they came from, or when they are coming back. He knows they were here, they had a specific interest in the mill road north, and they scared him well enough to hold for three days. You have the road. That is something.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            if (_rng.Next(2) == 0)
                                Msg("The innkeeper's wife was not in the room when he was instructed. She tells you freely — three cloaks, two nights ago, asked about the mill road and a specific building at the north end. She describes the building. You know the building. It is a granary that doubles as a way-station on a particular trade route. The pieces connect.", AshenColor);
                            else
                                Msg("Everyone in the room received the same instruction or the same fear. You get polite misdirection from four people in four different registers. They are all protecting the same thing from different angles. The shape of the protection tells you it was recent and specific. You cannot get past the shape.", DimColor);
                            break;
                        case "d":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You let him keep it. He is protecting something with the only tools available to him. You ride on. He watches you leave.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── ENTER CITY: The Reluctant Official [Charm] ─────────────────────
        private static void EC8_ReluctantOfficial(Settlement s)
        {
            float chance = SkillChance(DefaultSkills.Charm, 0.25f);
            string hint  = SkillHint(DefaultSkills.Charm, 0.25f, "Persuade him to speak");
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚜  The Reluctant Official",
                "A city records clerk has information you need — movement orders for a specific gate, filed three weeks ago. He is technically required to provide access to lords on request. He is also clearly frightened of whoever filed those orders, and is deploying bureaucratic friction with the practiced ease of a man who has done it for years. He is not going to say no. He is going to take a very long time to say yes.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Ease his fear rather than press his duty.", null, true, hint),
                    new InquiryElement("b", "Invoke your authority formally — demand the record as a matter of right.", null, true,
                        "You get the record. He files a note. Someone will read it."),
                    new InquiryElement("c", "Offer him something in return — not a bribe, a guarantee.", null, true,
                        "A guarantee costs you nothing but your word. That may be enough."),
                    new InquiryElement("d", "Leave it. Frightened clerks produce bad records under pressure anyway.", null, true,
                        "The information stays filed."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Charm, 0.25f))
                            {
                                ChangeRenown(5f);
                                Msg("You spend ten minutes doing something that is not persuasion exactly — you make him understand that you already know what frightened him and that your interest is not in making his position worse. He relaxes by degrees. By the end he pulls the record himself and explains what it means without being asked. He does this because someone treating him as a person rather than an obstacle is rare enough to be worth responding to honestly.", GoodColor);
                            }
                            else
                                Msg("You try the right approach but the fear in him is too deep and too recent to ease in a single conversation. He appreciates the tone and gives you the record's existence but not its content — technically compliance. He cannot go further than this and he knows it. You have the reference number. That opens other doors.", DimColor);
                            break;
                        case "b":
                            ChangeRelWithOwner(s, -3);
                            ChangeRenown(3f);
                            Msg("You invoke the right and he produces the record — correctly, promptly, with the precise reluctance of a man who knows how to comply without cooperating. The record is there. What it contains is useful. He files a note about the interaction before you are out of the building. Someone will read that note. You have what you came for and a flag on your name in this city's records.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You tell him that if producing this record causes him any administrative difficulty, you will personally document that the request came under lord's authority and that he complied correctly. He looks at you for a long moment, understanding that this is the one thing he actually needed and nobody has ever thought to offer it. He pulls the record and gives it to you with his name on it. Brave, given what he was afraid of.", GoodColor);
                            break;
                        case "d":
                            Msg("You leave him to his friction and his fear. The record stays filed. Whatever it contained will surface another way, eventually, which is a comfort and not a solution.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── ENTER CITY: The Merchant's Ledger [Steward] ────────────────────
        private static void EC8_MerchantLedger(Settlement s)
        {
            float chance = SkillChance(DefaultSkills.Steward, 0.25f);
            string hint  = SkillHint(DefaultSkills.Steward, 0.25f, "Identify the specific irregularity");
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚖  The Numbers",
                "A merchant at the city guild hall is arguing with an auditor about a discrepancy in his import ledger. He says it's a clerical error. The auditor says it's systematic. They both appeal to you — you are a lord, apparently this grants you opinions about accounting. The ledger is on the table. One of them is right.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Read the ledger and find the specific problem.", null, true, hint),
                    new InquiryElement("b", "Side with the auditor — systematic discrepancies are not clerical errors.", null, true,
                        "The auditor is grateful. The truth of the ledger is still open."),
                    new InquiryElement("c", "Side with the merchant — a single anomaly could be clerical.", null, true,
                        "He may owe you something. He may be guilty of something."),
                    new InquiryElement("d", "Decline to arbitrate — this is not your ledger or your fight.", null, true,
                        "They continue without you."),
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
                                ChangeRenown(5f);
                                Msg("The irregularity is on page seven: an import weight listed in two different units across two entries — a conversion that only works if the second shipment was a third smaller than recorded. Someone is skimming the margin between what arrives and what is declared, using the unit difference as cover. It has been done the same way four times. The merchant goes pale. The auditor takes notes. You hand the ledger back and leave both of them to what follows.", GoodColor);
                            }
                            else
                                Msg("You work through it. The numbers are internally consistent in most places, which is actually the harder pattern to read — someone who knows accounting well enough to hide something in the averaging rather than a single line. You identify three possible locations for the irregularity but cannot determine which is the source. You tell them both this. The auditor takes it as confirmation. The merchant takes it as uncertainty. Both of them are right.", DimColor);
                            break;
                        case "b":
                            ChangeRelWithOwner(s, -3);
                            ChangeRelWithRandomLord(5);
                            Msg("You support the auditor. The merchant guild will remember this. The auditor thanks you and continues the review. The truth of the ledger remains open, but the process continues without obstruction.", DimColor);
                            break;
                        case "c":
                            ChangeRelWithOwner(s, 5);
                            Msg("You support the merchant. He thanks you warmly. Whether he is guilty is a question you have declined to answer. The auditor closes his notes without a word and leaves. The ledger goes unresolved.", DimColor);
                            break;
                        case "d":
                            Msg("You decline. They argue for another forty minutes and reach no conclusion. The merchant eventually agrees to a third-party review that will take six weeks. The auditor leaves unsatisfied. The ledger goes back into the hall. The truth is still in it.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── ENTER CITY: The Shadow [Scouting] ──────────────────────────────
        private static void EC8_Followed(Settlement s)
        {
            float chance = SkillChance(DefaultSkills.Scouting, 0.28f);
            string hint  = SkillHint(DefaultSkills.Scouting, 0.28f, "Confirm and identify them");
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Shadow",
                "You are being followed. Not by amateurs — whoever this is matches your pace correctly, uses the crowd well, and has been doing it since the eastern gate. You noticed because you were looking. Most people would not have noticed. The question is what to do with the knowledge before they realise you have it.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Confirm identity before acting — read who they are without alerting them.", null, true, hint),
                    new InquiryElement("b", "Reverse the tail — follow your follower.", null, true,
                        "You may get ahead of them, or they may catch your move."),
                    new InquiryElement("c", "Lead them somewhere useful and confront them on your terms.", null, true,
                        "You pick the ground. The confrontation is controlled."),
                    new InquiryElement("d", "Change your route and lose them — you have no time for this today.", null, true,
                        "They lose you. You learn nothing about who sent them."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Scouting, 0.28f))
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                ChangeRenown(5f);
                                Msg("You use a shop window and a narrow passage to get a clear look without stopping. City watch — not uniformed, working plainclothes. This is a sanctioned surveillance, not a freelance tail. Someone in city administration has an official interest in your movements. That is a different kind of problem than an Ashen watcher. You continue your route as if unaware and note everything they observe.", DimColor);
                            }
                            else
                                Msg("You try to get a look but they are better than you expected and shift position exactly when you commit to the read. You confirm they are professional and confirm they are still there. That is all you get. You cannot tell employer, motive, or number — there may be more than one. You continue with incomplete information, which is the standard condition.", DimColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            if (_rng.Next(2) == 0)
                                Msg("You take a corner fast and double back through the market, reading the crowd backward. You find the position they occupied two minutes ago and the position they are moving to. From behind, you follow them to a building two streets from the lord's hall — private, no guild mark, curtains drawn at midday. Someone is using that building for something that isn't commerce. You have an address.", DimColor);
                            else
                                Msg("They catch your move — probably they were watching for it. By the time you complete the reverse they are gone, and there is a different presence behind you: not following, just present. You have announced that you know. Whatever they were doing becomes more careful. The tail continues with better tradecraft.", BadColor);
                            break;
                        case "c":
                            ChangeRenown(5f);
                            Msg("You lead them to a courtyard with one entrance and position your party at it before stopping. When they arrive they find you waiting. The professional response is to acknowledge it: one of them steps forward and explains they are city watch, tracking the movements of 'persons of interest' per the lord's standing order. You are apparently a person of interest. They file their report. You have their faces, their methods, and the confirmation that the order comes from the lord directly.", GoodColor);
                            break;
                        case "d":
                            Msg("Three corners and a market crossing and you are clean. They are good but you know this city's geometry better from yesterday's approach. You complete your business without a tail. They know you noticed. You are ahead by one day.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── LEAVE VILLAGE: The Poisoned Well [Medicine] ────────────────────
        private static void LV8_PoisonedWell(Settlement s)
        {
            float chance = SkillChance(DefaultSkills.Medicine, 0.25f);
            string hint  = SkillHint(DefaultSkills.Medicine, 0.25f, "Diagnose and treat correctly");
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✚  The Well",
                "As you prepare to leave, three children are brought to the inn in quick succession — all from the same family, all with the same symptoms: pale, cramping, confused. The parents are terrified and the herb-woman is overwhelmed. All three drank from the eastern well this morning. The well is still being used. The village does not yet understand what is happening.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Diagnose and treat — identify the cause and act on it.", null, true, hint),
                    new InquiryElement("b", "Seal the well immediately and send for the nearest physician.", null, true,
                        "The well is sealed. Help is coming. The children wait."),
                    new InquiryElement("c", "Leave medicine from your supplies and your best guess at the cause.", null, true,
                        "Your guess may be right. The herb-woman will work from what you leave."),
                    new InquiryElement("d", "Delay your departure until you know they are stable.", null, true,
                        "They will be stable or they will not. You stay either way."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Medicine, 0.25f))
                            {
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                ChangeRenown(8f);
                                Msg("Mineral — cold ash contamination from something upstream, not poison in the active sense but wrong chemistry in quantity. You know the treatment: controlled fluid replacement, specific herbs, no dairy, rest. You walk through the well and seal it correctly, identifying the exact upstream source from the residue pattern. By evening the children are worse before they are better. By morning two of them are asking for food. The third takes another day. All three live. You ride out with one day less and one thing done correctly.", GoodColor);
                            }
                            else
                            {
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                Msg("You identify it as contamination and seal the well, which is correct and saves further damage. The specific cause and treatment, you're less certain about — your best guidance helps somewhat and the herb-woman corrects the gaps with her own knowledge. It becomes a collaboration and it is not elegant but the children survive. Two of them recover fully. One will have a difficult month ahead. All three live, which was the real question.", DimColor);
                            }
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You seal the well yourself and dispatch a rider. The physician arrives two days later. By then one of the children has stabilised on the herb-woman's efforts and two are worse. The physician works through the night. All three survive but the delay cost something. The well was sealed in time to save the rest of the village. The river question was contained. This was the right choice given what you knew. It was not the best possible outcome.", DimColor);
                            break;
                        case "c":
                            ChangeGold(-200);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You leave what you have and your best reading of the symptoms. The herb-woman takes your supplies and your guess with the concentrated focus of someone filtering useful signal from educated approximation. She is better with the approximate information than with nothing. Two children stabilise by evening. The third is harder. She sits with the third child through the night. In the morning you will not know how it ended. You hope your guess was close enough.", DimColor);
                            break;
                        case "d":
                            AddMorale(-3f);
                            Msg("You delay. Your men understand this with the quiet acceptance of soldiers who have ridden for lords who would not have stopped. By dusk two of the children are clearly improving and one is not. You stay through the night. At dawn, the third child's fever breaks. You ride out three hours late and your men ride without complaint for twelve miles before anyone says anything. Then someone near the back laughs at something and the column relaxes.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── LEAVE VILLAGE: The Setup [Tactics] ─────────────────────────────
        private static void LV8_BattleSetup(Settlement s)
        {
            float chance = SkillChance(DefaultSkills.Tactics, 0.28f);
            string hint  = SkillHint(DefaultSkills.Tactics, 0.28f, "Read the setup before it springs");
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚔  The Setup",
                "A rider catches you at the edge of the village with urgent news: a lord two valleys over needs your help — ambushed, pinned, requesting your column immediately. The message is correctly sealed and the rider is convincing. Something in the framing is wrong — the route he names, the timing, the too-specific detail about troop count. You have seen setups. This has the shape of a setup.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Read the tactical picture — confirm this is a trap and identify its structure.", null, true, hint),
                    new InquiryElement("b", "Send a fast scout ahead before committing the column.", null, true,
                        "A scout confirms the truth of it, whatever it is."),
                    new InquiryElement("c", "Respond as if it's real but at half-speed with flankers out.", null, true,
                        "Caution covers either outcome."),
                    new InquiryElement("d", "Dismiss the rider and send word to the lord mentioned to verify.", null, true,
                        "The truth takes time. Your decision may cost something depending on which truth it is."),
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
                                ChangeRenown(8f);
                                Msg("The troop count detail is too specific — no messenger in a real ambush counts enemy troops that precisely, they estimate. The route named adds ninety minutes for no terrain reason. The seal is correct but the phrasing of the request contains an error that someone who had actually served with that lord would not have made. This is a kill box. You turn your column, take the southern road, and send a rider to the actual lord, who reports he is in his hall and has not been ambushed. Somebody wanted your column on a specific road at a specific time. They are waiting.", GoodColor);
                            }
                            else
                                Msg("Something is wrong but you cannot isolate it cleanly enough to be certain. The seal is right. The rider is convincing. The route bothers you for a reason you cannot fully articulate. You decide to proceed cautiously rather than dismiss it, which is neither fully correct nor fully wrong. You learn the truth two miles down the road, with flankers out, which was the right preparation.", DimColor);
                            break;
                        case "b":
                            Msg("Your scout rides ahead and returns in forty minutes: empty road for two miles, then sign of a prepared position — cut branches covering a ditch, horses tied off the road. It is a trap and they are already committed to it. You have thirty minutes before they adjust. Your column has the information and the initiative.", GoodColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You advance with flankers extended and pace halved. If it is real, your caution costs thirty minutes. If it is not, your flankers reach the ambush position first. They do. Three men in the ditch, caught too early. They surrender before the column arrives. The plan required your column moving at normal speed.", GoodColor);
                            break;
                        case "d":
                            if (_rng.Next(2) == 0)
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                Msg("The verification rider returns: the lord has not sent any message and is not in distress. He is now, however, aware that someone is impersonating his seal and setting traps under his name. He thanks you with the warmth of someone who just avoided an incident they didn't know was coming. You avoided the trap. He deals with the seal forgery. Both of you gained something.", GoodColor);
                            }
                            else
                            {
                                ShiftTrait(DefaultTraits.Honor, -1);
                                Msg("The lord's reply: he was ambushed, he did send the message, the rider is real, and your delay cost him two men who could have been extracted by a column that arrived on time. He is alive. His gratitude is not the warm kind. You chose caution when speed was required. The information that led to that choice was correct in shape but wrong in content.", BadColor);
                            }
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── LEAVE CITY: The Gate Standoff [Charm] ──────────────────────────
        private static void LC8_GateStandoff(Settlement s)
        {
            float chance = SkillChance(DefaultSkills.Charm, 0.25f);
            string hint  = SkillHint(DefaultSkills.Charm, 0.25f, "Talk him down without violence");
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚜  The Standoff",
                "A man is holding a merchant at knifepoint at the city gate. Not a robbery: his daughter was taken by the merchant in lieu of a debt, the law permits it, and he has apparently run out of other options. The gate guard is twenty feet away deciding whether to intervene. The merchant is frightened. The father is not — he is decided. Neither of them is going to improve the situation alone.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Talk the father down without threats.", null, true, hint),
                    new InquiryElement("b", "Buy the daughter's debt yourself — end the material cause.", null, true,
                        "You end the material cause. The law is satisfied."),
                    new InquiryElement("c", "Invoke your authority — demand both parties stand down immediately.", null, true,
                        "A lord's direct order resolves the standoff. Not necessarily the problem."),
                    new InquiryElement("d", "Tell the merchant, publicly, that his behaviour is noted and documented.", null, true,
                        "The merchant's position becomes uncomfortable. The father has a witness now."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Charm, 0.25f))
                            {
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                ChangeRenown(5f);
                                Msg("You speak to him — not about the knife, about the daughter. You ask her name. He tells you. You ask what he was going to do after. He does not have an answer, which is the opening. A man who has run out of options will take a new one if it is real. You tell him you will hear his case formally, today, and that the merchant will be required to present the debt documentation. The knife goes down. The guard, watching, does not intervene. The merchant understands something about the day has changed.", GoodColor);
                            }
                            else
                            {
                                ShiftTrait(DefaultTraits.Mercy, 1);
                                Msg("You speak to him and he listens but the words that would reach him are words you do not quite find. He is beyond the range of reassurance — he needs a specific thing, not comfort. He puts the knife down eventually but not because of what you said. He puts it down because the guard is closer now and he has calculated correctly that this ends badly for him regardless. He goes with the guard. The daughter's situation is unchanged.", DimColor);
                            }
                            break;
                        case "b":
                            ChangeGold(-400);
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            Msg("You step between them and pay the debt to the merchant directly, in coin, in front of the gate guard. The material cause disappears. The father watches the coin change hands with the expression of a man watching the thing he was willing to die for become solvable. He releases the knife. He does not thank you — the words he had ready were not for this outcome. The daughter is released that afternoon.", GoodColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You order both parties to stand down with the full weight of your rank. The father complies because a lord's direct order is not the same as a gate guard's. He is taken by the guard and held. The standoff is over. The daughter's debt remains in force. The merchant walks away quickly. The father's compliance was real and cost him everything he had left. You invoke authority correctly and it resolves the wrong problem.", DimColor);
                            break;
                        case "d":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You name the merchant's action and the debt mechanism loudly enough for the twenty people at the gate to hear every detail. The merchant's face changes. The gate guard, who has a family of his own, starts paying closer attention. The father watches the audience form and understands that he now has witnesses and a lord on record. He lowers the knife himself. The merchant leaves quickly. The father's case is not resolved, but it has a shape and a record now that it did not have two minutes ago.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── LEAVE CITY: The Forgery at the Gate [Roguery] ──────────────────
        private static void LC8_ForgeryAtGate(Settlement s)
        {
            float chance = SkillChance(DefaultSkills.Roguery, 0.28f);
            string hint  = SkillHint(DefaultSkills.Roguery, 0.28f, "Identify the forgery and who wrote it");
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  The Forgery",
                "The gate guard stops a merchant wagon and shows you a travel permit he is suspicious about — it's for three wagons and the seal looks right but he cannot explain why it bothers him. He is asking you because you are a lord and lords apparently know about seals. The merchant is watching this from twenty feet away with the composed expression of someone who knows exactly what is happening.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Read the forgery — identify what's wrong and who produced it.", null, true, hint),
                    new InquiryElement("b", "Back the guard's instinct — detain the merchant pending verification.", null, true,
                        "Backing the guard's instinct. Verification will tell the rest."),
                    new InquiryElement("c", "Wave it through — the seal looks right and you have somewhere to be.", null, true,
                        "The seal looks right. You have somewhere to be."),
                    new InquiryElement("d", "Ask the merchant directly where the permit was issued.", null, true,
                        "His answer will tell you whether he knows what he is carrying."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (SkillRoll(DefaultSkills.Roguery, 0.28f))
                            {
                                ShiftTrait(DefaultTraits.Calculating, 1);
                                ChangeRelWithOwner(s, 5);
                                ChangeRenown(5f);
                                Msg("The ink on the seal impression is the wrong shade for the permit office's wax — they changed their wax supplier eight months ago and the forger used old stock. The letter spacing on the issuing authority's title is also wrong: a period after 'Lord' that the real office stopped using two years back. This document was made by someone with access to old examples but not current practice. You show the guard both markers. The merchant's composure breaks precisely at the second marker, which is where he knew the risk was. He is detained.", GoodColor);
                            }
                            else
                                Msg("Something is wrong but you cannot isolate it to a specific marker. The seal is good enough to pass most inspections and your reading of the text formatting finds one possible issue that could also be a variant on legitimate practice. You tell the guard it may be forged but that you cannot confirm it. He detains the merchant for verification based on your maybe. The merchant is furious. Whether the document was forged will be determined by the permit office in three days.", DimColor);
                            break;
                        case "b":
                            ChangeRelWithOwner(s, 5);
                            if (_rng.Next(2) == 0)
                                Msg("The guard detains the merchant. The permit office's response comes in two days: forged. The merchant had contraband in the second wagon. The guard's instinct was correct. Your backing gave him the authority to act on it. He will remember that a lord trusted his read.", GoodColor);
                            else
                                Msg("The permit office's response comes in two days: legitimate. The merchant had a real permit and a delayed shipment and is furious about the detention at a level that may become a formal complaint. The guard's instinct was wrong. Your backing made it stick. He is embarrassed. You have a complaint pending. The seal was genuine.", BadColor);
                            break;
                        case "c":
                            Msg("The permit was forged. The wagon contained grey-dyed cloth that matches Ashen courier colours exactly — not contraband in any legal sense, but material with a specific use. It cleared your gate. The merchant was gone before the guard's follow-up instinct completed. You made a small decision at a gate and someone's supply run went through. Whether that matters depends on what the cloth is for.", BadColor);
                            break;
                        case "d":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("He names the issuing office correctly but the date is yesterday, which is too fast for the permit office's actual processing time — they take three days minimum. He knows the date is wrong, which means he knows what he is carrying and where it came from. He does not flinch when you point this out. He is a professional. You have him detained not for the document but for his knowledge of the document's real origin. That is a better charge.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

    }
}
