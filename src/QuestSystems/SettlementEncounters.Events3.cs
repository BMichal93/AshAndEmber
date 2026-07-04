// =============================================================================
// ASH AND EMBER — SettlementEncounters.Events3.cs
// Ember tithe, elixirs, inheritance, blood collector, trinkets, tavern.
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
        // ── E_EmberTithe — enter village / enter city (mage) ──────────────────
        // An old man asks to bask in your fire, offering knowledge in return.
        private static void E_EmberTithe(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Ember-Tithe",
                "An old man separates from the edge of the road as your party slows — bent, weathered, dressed in something between a travelling coat and a burial shroud. He does not beg. He simply asks. He says he has spent forty years gathering knowledge of fire, of the kind that does not burn wood, and that he has perhaps a season left to him. He asks only to feel the warmth of what you carry, just once, before he faces the cold. In return, he offers what he knows.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Let him stand in it. You can spare the warmth.", null, true,
                        "Warmth given freely returns something. The cost is in days."),
                    new InquiryElement("b", "Refuse. You owe him nothing.", null, true,
                        "He folds his coat and walks away."),
                    new InquiryElement("c", "Seize him. What he knows can be taken without the ceremony.", null, true,
                        "What he knows may be taken. What he kept may not be."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            try { Hero.MainHero.HeroDeveloper.UnspentFocusPoints += 1; } catch { }
                            AgePlayer(7);
                            Msg("He stands in it for a long moment, eyes closed. Then he begins to speak — not quickly, not with ceremony, but as a man unburdening something he has carried for decades. He talks for two hours. When he leaves, you sit with the shape of what he gave you for a long time. One focus point, paid in warmth and seven days. You call it even.", FireColor);
                            break;
                        case "b":
                            Msg("He nods once when you refuse, as though he expected it. He folds his coat around himself and walks away from the road — not toward any village you can see, just away from the direction you are heading. You do not see where he goes.", DimColor);
                            _emberTitheRefusedCountdown = 14;
                            break;
                        case "c":
                            ChangeGold(500);
                            if (_rng.NextDouble() < 0.5)
                            {
                                WoundPlayer();
                                Msg("You take him before he can speak. He does not struggle. He is still through all of it, and when you have what you need he says, clearly, one word — a name, not his, something older. The wound opens in you an hour later, from nowhere, deep and clean as a blade. He kept something back. He kept the part that costs.", BadColor);
                            }
                            else
                            {
                                Msg("You take what he knows, quickly and without ceremony. He gives it without resistance — more than he might have given freely. He is gone before the last of your men pass. What you learned is real. What you paid for it has not arrived yet.", DimColor);
                            }
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── Deferred: FireEmberTitheRefusedConsequence — 14 days after refusing the old man ──
        private static void FireEmberTitheRefusedConsequence()
        {
            if (MageKnowledge._deferredInquiry != null) { _emberTitheRefusedCountdown = 1; return; }
            MageKnowledge._deferredInquiry = () =>
            {
                if (_rng.NextDouble() < 0.50)
                {
                    try { Hero.MainHero.HeroDeveloper.UnspentFocusPoints += 1; } catch { }
                    AgePlayer(14);
                    Msg("A soldier in your company found a body on the road outside the settlement — an old man, coat pulled around him, dead of cold or age or some combination of the two. His notes were scattered in the wind. The soldier recovered a single page before they blew, and brought it because he did not know what else to do with it. You read it in ten minutes. It took the old man forty years to write. The page teaches you something. One focus point, purchased at a price that was not yours to pay.", FireColor);
                }
                else
                {
                    Msg("Word comes back from the village: a body was found on the road, old man, no identifying marks. His notes were scattered before anyone thought to collect them. Whatever he carried for forty years is gone with him. You refused him and he died on the road and the knowledge died with him. That is the complete record of this.", DimColor);
                }
            };
        }

        // ── ES7_FallenLaboratory — siege attacker won, mage ───────────────────
        // A sealed chamber reveals a heretical mage's laboratory after capture.
        private static void ES7_FallenLaboratory()
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  What the Keep Concealed",
                "Your sergeant reports a sealed chamber discovered behind the keep's lower kitchens — bricked up rather than locked, intended to disappear. Inside: organised notes, apparatus you recognise as capable of real work, and ingredients that would be illegal in three of the five regions you have ridden through. This belonged to the previous lord's personal scholar. The notes are meticulous. The work described in them is heretical by any measure. It is also genuinely interesting.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Study the notes. Knowledge does not become false for being forbidden.", null, true,
                        "Forbidden knowledge is still knowledge. What it costs is another matter."),
                    new InquiryElement("b", "Leave it. It was sealed for reasons you have no need to unpack.", null, true,
                        "It was sealed for reasons. The chamber stays as it is."),
                    new InquiryElement("c", "Burn it. Some work should not outlive the people who made it.", null, true,
                        "Some work should not outlive the people who made it."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Honor, -1);
                            if (_rng.NextDouble() < 0.20)
                            {
                                try { Hero.MainHero.HeroDeveloper.UnspentFocusPoints += 1; } catch { }
                                Msg("You read through most of the night. The scholar was working on the relationship between fire-carrying and certain physical states — not how to create it, but how to deepen it once present. The path they mapped is not one you would have found alone. By morning you understand something you did not understand before. One focus point, bought in hours and the specific unease of learning from someone you cannot question.", FireColor);
                            }
                            else
                            {
                                Msg("You read through most of the night. The scholar was careful, methodical, and working on something genuinely dangerous — not in the explosive sense but in the kind that changes what a person believes is possible. You come away unsettled. Not from the content but from how clearly it was reasoned. The work yields nothing tonight. It may yield something later.", DimColor);
                            }
                            break;
                        case "b":
                            Msg("You have the chamber re-sealed. Whatever was worked here will stay here, in the dark, until the keep is occupied by someone else who finds it and makes the same decision. You have enough decisions already.", DimColor);
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Honor, 1);
                            Msg("You carry the first armful yourself. The notes burn with a faint smell that is not entirely paper, and the apparatus blackens in ways that suggest it already absorbed something it should not have. The chamber is ash by midday. You don't know what was lost. You know what wasn't found.", GoodColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── EC9_AshenElixir — enter city, general ─────────────────────────────
        // A street alchemist offers a secret of power — for a price.
        private static void EC9_AshenElixir(Settlement s)
        {
            float charmChance = SkillChance(DefaultSkills.Charm, 0.30f);
            string charmHint  = SkillHint(DefaultSkills.Charm, 0.30f, "Intimidate him into speaking");

            void ShowElixirChoice(bool paid)
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "⚗  The Vial",
                    "He produces it from inside his coat: a small sealed vial, dark and faintly luminescent, the liquid inside not quite settling the way liquid should. He describes the contents — ash-blood drawn from a living Ashen donor, three additional reagents he declines to name, prepared over a fortnight at specific temperatures. He says it will unlock something in whoever drinks it. He says the process is irreversible. He says this as though it is a recommendation.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("a", "Drink it.", null, true,
                            "He says it unlocks something. He says the process is irreversible."),
                        new InquiryElement("b", "Refuse. You've heard enough.", null, true,
                            "The road continues."),
                        new InquiryElement("c", "Report him to the city watch.", null, true,
                            "The watch will be interested. So will others."),
                        new InquiryElement("d", paid ? "Beat him and recover your money." : "Beat him and take his purse.", null, true,
                            paid ? "Your coin comes back. Something else may leave with it."
                                 : "His coin for your trouble. Something else may leave with it."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen2 =>
                    {
                        switch (chosen2?[0]?.Identifier as string)
                        {
                            case "a":
                            {
                                double roll = _rng.NextDouble();
                                if (roll < 0.50)
                                {
                                    try { Hero.MainHero.HeroDeveloper.UnspentAttributePoints += 2; } catch { }
                                    Msg("The liquid is cold going down and then not cold at all. Your vision whites out briefly. When it returns you are sitting on the cobblestones and your hands are shaking — not from weakness, from something running faster than usual under the surface. You have two attribute points you did not have an hour ago. The alchemist has already left. So has any record of this.", GoodColor);
                                }
                                else if (roll < 0.75)
                                {
                                    BecomeAshen();
                                    Msg("The liquid is cold going down and then colder still. The ash-blood recognises something in you it was looking for. Your reflection in the window across the street is already different — grey at the margins, the eyes beginning to change. The alchemist watches with professional satisfaction and then quietly disappears. You have become something you cannot unbecome.", BadColor);
                                }
                                else
                                {
                                    Msg("The liquid moves wrong in you from the moment it clears your throat. You have time to understand what is happening before you lose the ability to act on it. Your men find you on the street three minutes later, unmoving.", BadColor);
                                    try { KillCharacterAction.ApplyByMurder(Hero.MainHero, null, false); } catch { }
                                }
                                break;
                            }
                            case "b":
                                Msg("You set the vial on his table and leave. He calls after you — something about wasted potential, the usual — but does not follow. The street swallows his voice quickly.", DimColor);
                                break;
                            case "c":
                                ChangeRenown(5f);
                                Msg("You find the nearest city watch officer and describe what you witnessed. The watch takes it seriously — ash-blood preparation is illegal in this city, as in most. They collect him within the hour. His apparatus is confiscated. The vial goes with it. You receive a formal acknowledgement from the watch captain. It will be recorded under your name.", GoodColor);
                                break;
                            case "d":
                                ShiftTrait(DefaultTraits.Mercy, -1);
                                ChangeGold(paid ? 1000 : 200);
                                Msg(paid
                                    ? "You make your position clear without extended negotiation. He returns the coins without being asked twice. He also offers two of the unnamed reagents as an apparent apology, which you did not request and are not certain you want. You leave him on the floor of his shop, breathing, with a considerably reduced opinion of street salesmanship."
                                    : "You make your position clear without extended negotiation. He empties his purse without being asked twice. He also offers two of the unnamed reagents as an apparent apology, which you did not request and are not certain you want. You leave him on the floor of his shop, breathing, with a considerably reduced opinion of street salesmanship.", DimColor);
                                break;
                        }
                    }, null, "", false), false, true);
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚗  The Alchemist's Promise",
                "A man separates from the crowd near the market gate — not blocking you, just placing himself where you will notice him. He is dressed expensively for someone selling things from a bag. He uses the word 'secret' twice in his opening sentence, which is a technique. He says he has something that will change what you are capable of. He says he can prove it. He says it will cost you one thousand gold to hear the proof.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Refuse. Every man with a secret wants money first.", null, true,
                        "Nothing happens."),
                    new InquiryElement("b", "Agree. Pay the thousand gold and hear him out.", null, true,
                        "Lose 1000 gold. He shows you what he has."),
                    new InquiryElement($"c", $"Threaten him into talking. ({(int)(charmChance * 100)}% Charm)", null, true,
                        charmHint),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            Msg("You ride on. He watches you go with the expression of a man recalculating his approach for the next mark. He will try someone else. That is not your problem.", DimColor);
                            break;
                        case "b":
                            if (!ChangeGold(-1000)) return;
                            MageKnowledge._deferredInquiry = () => ShowElixirChoice(true);
                            break;
                        case "c":
                            if (SkillRoll(DefaultSkills.Charm, 0.30f))
                            {
                                Msg("You do not raise your voice. You do not need to. He reads the situation correctly and decides that a free demonstration is preferable to the alternative. He is correct.", GoodColor);
                                MageKnowledge._deferredInquiry = () => ShowElixirChoice(false);
                            }
                            else
                            {
                                ShiftTrait(DefaultTraits.Honor, -1);
                                ChangeCrime(10f);
                                Msg("You push harder than the situation called for. He does not give you what you want — instead he shouts for the watch. You disengage before it escalates further, but not before witnesses have a good look at your face. The city watch receives a complaint. Your criminal rating rises. The alchemist keeps his secret.", BadColor);
                            }
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── Child eligibility check ────────────────────────────────────────────
        private static Hero GetEligibleChild()
        {
            try
            {
                return Hero.AllAliveHeroes.FirstOrDefault(h =>
                    h.IsAlive && !h.IsDisabled && !h.IsPrisoner &&
                    (h.Father == Hero.MainHero || h.Mother == Hero.MainHero) &&
                    h.Age >= 14f && h.Age <= 22f);
            }
            catch { return null; }
        }

        private static bool HasEligibleChild() => GetEligibleChild() != null;

        // ── E_DarkeningInheritance — enter village/city, mage, rare ───────────
        // Something wrong is stirring in a child who is coming of age.
        private static void E_DarkeningInheritance(Settlement s)
        {
            Hero child = GetEligibleChild();
            if (child == null) return;
            _childEventCooldown = 300;

            string childName = child.Name?.ToString() ?? "your child";

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Darkening Inheritance",
                $"You have been watching {childName} for some time now — the way light behaves wrong around them in certain rooms, the cold that has no source, the dreams they won't describe. You have seen this before. Not in yourself, but in others. Something is waking in them, and it is not the fire you carry. It is the other thing. The cold thing. The thing that undoes people from the inside.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", $"Ignore it. {childName} is your child and you love them.", null, true,
                        "Perhaps you are wrong. Perhaps it will pass. Perhaps it is simply waiting."),
                    new InquiryElement("b", $"Isolate {childName} and watch closely.", null, true,
                        $"They will know what you are doing. The cold may slow. It may not stop."),
                    new InquiryElement("c", $"End it. Kill {childName} before they become something they cannot come back from.", null, true,
                        $"You do it yourself. You do not ask anyone else to carry it."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (_rng.NextDouble() < 0.70)
                            {
                                Msg($"You watch {childName} and see nothing further — at least nothing you cannot explain away. Perhaps you were wrong. Perhaps the cold chose someone else. Perhaps it is simply waiting. You do not sleep well, but you wake with them still there, and still yours.", DimColor);
                            }
                            else
                            {
                                try
                                {
                                    ColourLordRegistry.SetAshen(child, true);
                                    ColourLordRegistry.SetMage(child, true);
                                    try { AshenCitySystem.ApplyAshenPersonality(child); } catch { }
                                    try { MageKnowledge.ApplyAshenAppearance(child); } catch { }
                                    // Try to move child to a random Ashen clan
                                    var ashenClans = Clan.All
                                        .Where(c => c != Clan.PlayerClan && c.IsEliminated == false &&
                                               c.Heroes.Any(h => ColourLordRegistry.IsAshenLord(h)))
                                        .ToList();
                                    if (ashenClans.Count > 0)
                                    {
                                        var targetClan = ashenClans[_rng.Next(ashenClans.Count)];
                                        try { child.Clan = targetClan; } catch { }
                                    }
                                }
                                catch { }
                                Msg($"You did nothing. You told yourself it would pass. By the time you admit it is not passing, {childName} is already different — the grey in their eyes unmistakable, the cold they carry no longer concealable. They leave before you make a decision. You hear their name spoken by people you would rather not share a name with.", BadColor);
                            }
                            break;
                        case "b":
                            try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, child, -50, false); } catch { }
                            ShiftTrait(DefaultTraits.Mercy, -1);
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            if (_rng.NextDouble() < 0.30)
                            {
                                try { KillCharacterAction.ApplyByMurder(child, null, false); } catch { }
                                Msg($"You watch, and what you watch for happens. The infection runs faster in isolation — the cold needs no kindness to spread, and it does not need the door to be open. {childName} does not survive it. You kept them from becoming what they were becoming. You are not sure what the distinction is worth.", BadColor);
                            }
                            else
                            {
                                Msg($"You isolate {childName} and you watch. They know what you are doing. They may not survive knowing it, but they survive. The cold slows. Whether it stops is a question that will not answer itself quickly. {childName} looks at you differently now. So do you.", DimColor);
                            }
                            break;
                        case "c":
                            try
                            {
                                Hero.MainHero.SetTraitLevel(DefaultTraits.Mercy, -2);
                                Hero.MainHero.SetTraitLevel(DefaultTraits.Calculating, 2);
                                Hero.MainHero.HeroDeveloper.UnspentFocusPoints += 1;
                                KillCharacterAction.ApplyByMurder(child, Hero.MainHero, false);
                            }
                            catch { }
                            Msg($"You do it yourself. You do not ask anyone else to carry it. The cold that was building in {childName} releases at the end — you feel it disperse, formless, looking for somewhere else to go. It does not find you. You stand with what you did and you do not look away from it. One focus point, paid in full.", DarkColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── LC_BloodCollector — leave city, mage ──────────────────────────────
        // A strange man offers gold for a single drop of the player's fire-blood.
        private static void LC_BloodCollector(Settlement s)
        {
            float charmChance = SkillChance(DefaultSkills.Charm, 0.30f);
            string charmHint  = SkillHint(DefaultSkills.Charm, 0.30f, "Read him and press him");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚗  The Collector",
                "A man intercepts you near the gate with the specific body language of someone who has been waiting. He introduces himself as an alchemist and researcher. He says he is studying the properties of blood in people who carry unusual gifts — your kind, specifically. He wants one drop. He offers a thousand gold for the inconvenience. He says the research is academic. He says the results are for publication. He says a lot of things.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Refuse. You don't trust him.", null, true,
                        "You refuse. He accepts this with practiced ease."),
                    new InquiryElement($"b", $"Refuse — but question him first. ({(int)(charmChance * 100)}% Charm)", null, true,
                        charmHint),
                    new InquiryElement("c", "Accept. One drop costs you nothing.", null, true,
                        "One drop. He pays. You ride on a little less certain than before."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You refuse without elaboration. He accepts the refusal with the practiced ease of someone who receives many refusals. He does not follow. He does not look surprised. You put two streets between you and him before you stop thinking about the specific way he stood while he was listening.", DimColor);
                            break;
                        case "b":
                            if (SkillRoll(DefaultSkills.Charm, 0.30f))
                            {
                                ChangeRenown(10f);
                                Msg("You hold the conversation open a little longer than he planned for. He has a way of redirecting questions that is too practiced — not a scholar's deflection, something more deliberate. You push on the specific wording of 'research' until it shifts. By the time he realises you have found the seam in his story, you have enough to take to the city's intelligence contact. You do not see him again after that. You gain the credit without the credit specifying exactly what it was for.", GoodColor);
                            }
                            else
                            {
                                Msg("You push on his answers and find nothing that clearly breaks. Either he is what he says or he is very good at being what he says. He thanks you for your time and leaves first. You are left with the doubt, which is its own answer but not one you can act on.", DimColor);
                            }
                            break;
                        case "c":
                            ShiftTrait(DefaultTraits.Calculating, -1);
                            ChangeGold(1000);
                            _bloodTitheCountdown = 3;
                            Msg("One drop. He collects it with practiced care into a sealed vial and pays you without haggling. He thanks you, names no institution, and leaves the way he came. You ride on a thousand gold heavier and slightly less certain than you were an hour ago.", GoldColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── Deferred: FireBloodTitheConsequence — 3 days after LC_BloodCollector C ──
        private static void FireBloodTitheConsequence()
        {
            if (MageKnowledge._deferredInquiry != null) { _bloodTitheCountdown = 1; return; }

            double roll = _rng.NextDouble();

            if (roll < 0.35)
            {
                // Tracking both ways — player gets a choice
                MageKnowledge._deferredInquiry = () =>
                {
                    MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                        "★  The Thread Pulls Both Ways",
                        "Three days since the alchemist took a drop of your blood. It arrives as a sensation, not a wound — a pull, specific and directional, as though someone has attached a thread to the fire you carry and is testing whether it holds. He used the blood to create a working that reaches back through the sample. The fire inside you feels it. You can feel where it is coming from.",
                        new List<InquiryElement>
                        {
                            new InquiryElement("a", "Follow the thread. Find out where he is.", null, true,
                                "The direction is clear. The distance is walkable."),
                            new InquiryElement("b", "Sever it. The fire can burn a thread.", null, true,
                                "One day's aging. The connection ends. You learn nothing further."),
                            new InquiryElement("c", "Let it run — see what he does with the connection.", null, true,
                                "He may learn something. So may you."),
                        },
                        false, 1, 1, "Decide", "",
                        chosen =>
                        {
                            switch (chosen?[0]?.Identifier as string)
                            {
                                case "a":
                                    ShiftTrait(DefaultTraits.Calculating, 1);
                                    ChangeRenown(10f);
                                    _bloodTitheRevealCountdown = 3;
                                    Msg("You follow the pull through two streets and a market and find him — a back room above a tannery, equipment he was not carrying when you met. He has been expecting something like you to come. He is not surprised. He has questions and so do you.", AshenColor);
                                    break;
                                case "b":
                                    AgePlayer(1);
                                    Msg("You push the fire through the thread deliberately until it burns. The pull stops. You are one day older. You know someone was reaching through your blood toward you, and that you ended the reach before it became anything more.", DimColor);
                                    break;
                                case "c":
                                    _bloodTitheRevealCountdown = 7;
                                    Msg("You let it run. The connection sits in you like a splinter — present, not painful, oriented. He is doing something with what he can feel through it. Whether that is useful or dangerous is not yet apparent. You note the direction and ride on.", DimColor);
                                    break;
                            }
                        }, null, "", false), false, true);
                };
            }
            else if (roll < 0.70)
            {
                // Spectral agony — wound + age, with at least the dignity of a choice to endure
                MageKnowledge._deferredInquiry = () =>
                {
                    MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                        "★  Spectral Pain",
                        "Three days since the alchemist took a drop of your blood. It arrives in the night — not a wound but something working through the fire you carry, using the drop as a thread to reach back through. Your body registers it as pain before your mind can name it. You come awake on the floor of whatever inn you are in, the fire inside doing something involuntary.",
                        new List<InquiryElement>
                        {
                            new InquiryElement("ok", "Endure it.", null, true,
                                "Whatever he did with the drop, he did it with intent."),
                        },
                        false, 1, 1, "Endure", "",
                        _ =>
                        {
                            WoundPlayer();
                            AgePlayer(365);
                            Msg("Whatever he did with the drop, he did it with intent. You carry the wound and the year and the knowledge that the blood meant something to someone who knew what to do with it.", BadColor);
                        },
                        null, "", false), false, true);
                };
            }
            else
            {
                // Quiet tracker — something now knows where you are
                MageKnowledge._deferredInquiry = () =>
                    Msg("Three days since the alchemist took a drop of your blood. Whatever he intended, it has not arrived in any form you can feel. The fire you carry is unchanged. But you notice, over the following days, that certain things find you more easily than they should — a message through an intermediary you did not know had your route, a face in a market you have seen before in a different city. Something knows your location. It has not yet decided what to do with that information.", DimColor);
            }
        }

        // ── Deferred: FireBloodTitheReveal — follow-up after "follow the thread" or "let it run" ──
        private static void FireBloodTitheReveal()
        {
            if (MageKnowledge._deferredInquiry != null) { _bloodTitheRevealCountdown = 1; return; }
            MageKnowledge._deferredInquiry = () =>
                Msg("The alchemist's location is no longer what it was. The trail ends at the tannery room — cleared, methodically, without haste. He knew you were coming with enough time to move properly. What he left behind is a single sealed vial on the windowsill. Inside: your blood sample, returned whole. There is a note folded under it. It says only: 'Adequate precautions. Come back when you are ready for the conversation.' You are not certain what that means. You are certain he means it.", AshenColor);
        }

        // ── EC_TavernStranger — enter city, general ───────────────────────────
        // An attractive stranger at the city tavern opens a conversation.
        private static void EC_TavernStranger(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "♦  An Evening in the City",
                "The tavern is busy enough that you have a corner to yourself, or nearly. A person across the room has been watching you since you sat down — attractive, unhurried, clearly aware that you have noticed them noticing you. They cross the room at the pace of someone who is confident in the outcome. They lean against the table and say something that could mean several things, depending on which answer you give.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Tell them to go away.", null, true,
                        "A clear answer. They read it correctly."),
                    new InquiryElement("b", "Flirt back.", null, true,
                        "See where it goes."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You give them the short version — not hostile, just clear. They read it correctly and withdraw with the grace of someone who does this regularly and does not take it personally. You finish your drink in peace.", DimColor);
                            break;
                        case "b":
                        {
                            ShiftTrait(DefaultTraits.Calculating, -1);
                            bool isMale = !(Hero.MainHero?.IsFemale ?? false);
                            int outcome = _rng.Next(4) + 1; // 4 outcomes for both genders

                            switch (outcome)
                            {
                                case 1:
                                    // Spend the night — charm roll for morale (all genders, no extra consequence)
                                    if (SkillRoll(DefaultSkills.Charm, 0.45f))
                                    {
                                        AddMorale(10f);
                                        Msg("The evening goes well by any measure. The conversation outlasts the candles. The morning is ordinary and the better for it. Your mood is noticeably improved when you ride out.", GoodColor);
                                    }
                                    else
                                        Msg("The evening is pleasant without being remarkable. Something in the timing was slightly off. You part amicably. Your men notice you are in an average mood, which is better than most mornings.", DimColor);
                                    break;

                                case 2:
                                    ChangeGold(-100);
                                    Msg("You wake in the early morning to the sound of the door closing carefully. Your purse is 100 coins lighter. They were good at it — no mess, no drama, just a professional taking advantage of a distraction.", BadColor);
                                    MageKnowledge._deferredInquiry = () =>
                                    {
                                        float rogChance = SkillChance(DefaultSkills.Roguery, 0.40f);
                                        MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                                            "◆  The Empty Purse",
                                            "They left less than ten minutes ago. The city is large but the night gates are not open yet — whoever they are, they are still inside the walls.",
                                            new List<InquiryElement>
                                            {
                                                new InquiryElement("a", $"Hunt them down before the city wakes. ({(int)(rogChance*100)}% Roguery)", null, true,
                                                    "Recover the coin and learn who sent them."),
                                                new InquiryElement("b", "Let them go. A hundred coin is not worth the morning.", null, true,
                                                    "The coin is gone. So are they."),
                                            },
                                            false, 1, 1, "Decide", "",
                                            sub =>
                                            {
                                                switch (sub?[0]?.Identifier as string)
                                                {
                                                    case "a":
                                                        if (SkillRoll(DefaultSkills.Roguery, 0.40f))
                                                        {
                                                            ChangeGold(100);
                                                            ShiftTrait(DefaultTraits.Calculating, 1);
                                                            _tavernRobberyCountdown = 7;
                                                            Msg("You find them two streets over — not hiding, not running, operating with the confidence of people who believe the window between action and consequence is safe. It is not. You recover the coin and, from the kit they were carrying, a sealed letter with an address in the city. The letter describes you. Someone commissioned this. You have their address and seven days before they notice the commission failed.", AshenColor);
                                                        }
                                                        else
                                                        {
                                                            Msg("You move through the pre-dawn city and arrive at each likely location one step behind them. By the time the morning gates open they are outside the walls. The coin is gone. You know they were professional. You do not know who hired them.", DimColor);
                                                        }
                                                        break;
                                                    case "b":
                                                        Msg("You let them go. A hundred coin and a revision of your judgment of certain kinds of evenings. You dress without hurrying and decide not to mention it.", DimColor);
                                                        break;
                                                }
                                            }, null, "", false), false, true);
                                    };
                                    break;

                                case 3:
                                    // Attempted murder in your sleep — athletics roll
                                    if (SkillRoll(DefaultSkills.Athletics, 0.40f))
                                    {
                                        ChangeRenown(5f);
                                        Msg("Something wakes you — a shift in weight, a sound wrong by half a second. You are moving before you are fully awake, which is the only reason you are still alive. The knife misses. What follows is brief and decisive. Word of the incident finds its way to certain circles before you have left the city.", GoodColor);
                                    }
                                    else
                                    {
                                        WoundPlayer();
                                        Msg("You wake to pain, already bleeding. The knife did its work before your body registered what was happening. You live — they misjudged the angle — but it costs you. You are treated, bandaged, and ride out wounded, with a considerable revision of your judgment of people.", BadColor);
                                    }
                                    break;

                                case 4:
                                    // Male: spend the night + baby in a year. Female: spend the night + pregnant in 30 days.
                                    if (SkillRoll(DefaultSkills.Charm, 0.45f))
                                    {
                                        AddMorale(10f);
                                        Msg("The evening is genuinely good — the kind you do not expect in a city tavern and will not easily explain to anyone who asks. The morning is easy. You ride out in a better mood than you have been in for some time. You do not think much about it. Not yet.", GoodColor);
                                    }
                                    else
                                        Msg("The evening is warm enough. Not remarkable, but real. You part before dawn. It is the kind of night that does not leave a scar. You think about it briefly on the road and then stop thinking about it.", DimColor);
                                    if (isMale)
                                        _babyEventCountdown = 365;
                                    else
                                        _pregnancyCountdown = 30;
                                    break;
                            }
                            break;
                        }
                    }
                }, null, "", false), false, true);
        }

        // ── Deferred: FireTavernRobberyConsequence — 7 days after successful robbery pursuit ──
        private static void FireTavernRobberyConsequence()
        {
            if (MageKnowledge._deferredInquiry != null) { _tavernRobberyCountdown = 1; return; }
            MageKnowledge._deferredInquiry = () =>
            {
                ChangeRelWithRandomLord(-5);
                Msg("Seven days. The address you found led to a counting house — legitimate front, illegitimate side contracts. The handler who commissioned the robbery has noticed the commission failed and the couriers have not returned. He does not know your name. He has narrowed the field, and you are one of the three possibilities. A lord who had business with that counting house receives the concern through proper channels and is now slightly less inclined toward you, for reasons neither of you can state cleanly.", BadColor);
            };
        }

        // ── Deferred: FireBabyConsequence — 365 days after EC_TavernStranger outcome 4 ──
        private static void FireBabyConsequence()
        {
            if (MageKnowledge._deferredInquiry != null) { _babyEventCountdown = 1; return; }

            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "♦  Word from the Road",
                    "A letter finds you through three different couriers, each one more indirect than the last. The handwriting is careful but unpracticed. A woman you spent a night with nearly a year ago has had your child. She is not demanding anything specific. She is informing you of the situation in precise terms and waiting to see what you do with the information.",
                    new List<InquiryElement>
                    {
                        new InquiryElement("a", "Ignore it. You have no obligations here.", null, true,
                            "You set the letter aside without reading the second half."),
                        new InquiryElement("b", "Send money. That is all you can offer from here.", null, true,
                            "Coin through couriers. It is the form of adequate you can manage from here."),
                        new InquiryElement("c", "Acknowledge the child. Bring them into your household.", null, true,
                            "You acknowledge the child. What follows is yours to carry."),
                    },
                    false, 1, 1, "Decide", "",
                    chosen =>
                    {
                        switch (chosen?[0]?.Identifier as string)
                        {
                            case "a":
                                ShiftTrait(DefaultTraits.Mercy, -1);
                                Msg("You set the letter aside without reading the second half. Whatever she decides to do with that is hers. You ride on without writing back. The courier system that found you will not find you again on this — you made sure of it.", DimColor);
                                break;
                            case "b":
                            {
                                int gold = Hero.MainHero?.Gold ?? 0;
                                int amount = Math.Max(500, (int)(gold * 0.05f));
                                ChangeGold(-amount);
                                Msg($"You send {amount} gold through the same chain of couriers in reverse. The letter you include is short. You do not know if it is adequate. You suspect it is not, but it is the form of adequate that you can manage from this distance.", GoldColor);
                                break;
                            }
                            case "c":
                                TryAdoptIllicitChild();
                                break;
                        }
                    }, null, "", false), false, true);
            };
        }

        // ── Helper: adopt illicit child into player clan ───────────────────────
        private static void TryAdoptIllicitChild()
        {
            try
            {
                // Find a non-hero CharacterObject from the player's culture for the template
                CharacterObject template = CharacterObject.All
                    .FirstOrDefault(c => c != null && !c.IsHero
                                     && c.Culture == Hero.MainHero.Culture);
                if (template == null)
                    template = CharacterObject.All.FirstOrDefault(c => c != null && !c.IsHero);

                Settlement birthPlace = Hero.MainHero.HomeSettlement
                    ?? Settlement.All.FirstOrDefault(se => se != null && se.IsTown);

                if (template != null && birthPlace != null)
                {
                    Hero child = HeroCreator.CreateChild(template, birthPlace, Clan.PlayerClan, 1);
                    if (child != null)
                    {
                        try { child.Father = Hero.MainHero; } catch { }
                        PenaliseSpouseForAdoption();
                        Msg($"{child.Name} arrives in your household — quiet, small, and entirely unaware of the circumstances. They are yours now, in whatever sense you decide that means.", GoodColor);
                        return;
                    }
                }
                // Fallback if hero creation fails
                PenaliseSpouseForAdoption();
                Msg("You arrange for the child to be brought to your household through a chain of trusted intermediaries. There is no formal record, but it is done. They are yours.", GoodColor);
            }
            catch
            {
                PenaliseSpouseForAdoption();
                Msg("You arrange for the child to be brought into your household. They arrive. They are yours.", GoodColor);
            }
        }

        // ── EB_TrinketEmberShard — enter settlement, general, no active trinket ──
        // A fragment of amber found on a corpse. Warm to the touch through a gauntlet.
        private static void EB_TrinketEmberShard(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◈  The Ember Shard",
                "Among the enemy dead, you find him — the one who had no reason to carry what he carried. Inside his breastplate, tucked against the lining: a fragment of amber the size of a thumb. Something is suspended inside it, too small to name. What you notice first is not the shape but the warmth. From inside sealed armour, on a dead man, through your gauntlet: warmth that has no right to be there.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Take it.", null, true, ""),
                    new InquiryElement("b", "Leave it.", null, true, ""),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            _trinketVariant   = 1;
                            _trinketPhase     = 1;
                            _trinketCountdown = 3;
                            Msg("You close your gauntlet over it. The warmth doesn't fade when you ride on.", DarkColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You step back and keep walking. For a few strides the warmth seems to follow you. Then it does not.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── EB_TrinketBlindEye — enter settlement, general, no active trinket ────
        // A small iron medallion with an eye etched on both sides. One closed, one open.
        private static void EB_TrinketBlindEye(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◈  The Blind Eye",
                "A small iron medallion, caught in the buckle of a dead man's belt. Black with age. On one side: an eye, etched with a precision that belongs to a different tradition than anything else this man was carrying. The eye is closed. You turn it over. On the reverse, the same eye. Open.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Take it.", null, true, ""),
                    new InquiryElement("b", "Leave it.", null, true, ""),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            _trinketVariant   = 2;
                            _trinketPhase     = 1;
                            _trinketCountdown = 3;
                            Msg("You turn it over twice more. Closed. Open. You put it in your coat and ride on.", DarkColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You set it back against the buckle. You don't look back. The open side faces the sky.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── EB_TrinketPaleCompass — enter settlement, general, no active trinket ─
        // A carved bone disc that settles on a fixed bearing regardless of how it is held.
        private static void EB_TrinketPaleCompass(Settlement s)
        {
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◈  The Pale Compass",
                "Inside the lining of a dead man's coat, stitched there with deliberate care: a disc of carved bone, the size of a coin. The face is engraved with radial lines like a compass rose with no directions marked. You set it on your palm to examine it. It rotates. Slowly, precisely, it settles on a bearing. You turn your hand. It corrects. There is no magnet. There is no mechanism you can find.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Take it.", null, true, ""),
                    new InquiryElement("b", "Leave it.", null, true, ""),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            _trinketVariant   = 3;
                            _trinketPhase     = 1;
                            _trinketCountdown = 3;
                            Msg("The bone is warm when you close your fingers over it. The bearing holds.", DarkColor);
                            break;
                        case "b":
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            Msg("You tuck it back into the lining as you found it. The disc keeps its bearing all the way back to where the coat lies flat.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── Deferred: FireTrinketStage — dispatches first dream or recurring dream ──
        private static void FireTrinketStage()
        {
            if (MageKnowledge._deferredInquiry != null) { _trinketCountdown = 1; return; }
            if (_trinketPhase == 1)
                MageKnowledge._deferredInquiry = FireTrinketFirstDream;
            else if (_trinketPhase == 2)
                MageKnowledge._deferredInquiry = FireTrinketRecurringDream;
        }

        // ── Deferred: FireTrinketFirstDream — 3 days after picking up a trinket ────
        private static void FireTrinketFirstDream()
        {
            string title, desc, choiceA, choiceB, choiceC;
            switch (_trinketVariant)
            {
                case 2:
                    title   = "◈  The Eye Opens";
                    desc    = "Both sides of the medallion have the same eye in the dream. Both are open. There is nothing behind them — not darkness exactly, but the specific quality of attention that has no object. It is watching you the way a locked door watches a room. You wake with the clear sense that something in your belongings is oriented toward you.";
                    choiceA = new[]{ "Hold its gaze.", "Look back at it.", "Don't look away." }[_rng.Next(3)];
                    choiceB = new[]{ "Keep still. Let it watch.", "Don't acknowledge it.", "Hold still. Don't engage." }[_rng.Next(3)];
                    choiceC = new[]{ "Leave it at dawn.", "Throw it out at first light.", "Get rid of it in the morning." }[_rng.Next(3)];
                    break;
                case 3:
                    title   = "◈  A Direction";
                    desc    = "You are in a dark place and the compass is in your hand. Every direction looks identical except one. The disc has settled on a bearing and what is in that direction is not light exactly, but what light might look like if it could form intentions. The pull is as large as you can imagine and no larger. You wake with your hand closed around nothing and the bearing still vivid in your mind.";
                    choiceA = new[]{ "Follow the bearing.", "Step toward it.", "Go in that direction." }[_rng.Next(3)];
                    choiceB = new[]{ "Stay where you are.", "Don't follow. Hold your position.", "Hold still. Don't engage." }[_rng.Next(3)];
                    choiceC = new[]{ "Lose it in the morning.", "Leave it on the road at dawn.", "Get rid of it in the morning." }[_rng.Next(3)];
                    break;
                default:
                    title   = "◈  Something in the Amber";
                    desc    = "You dream of flame suspended in resin — perfectly still, not consuming, not going out. In the dream, you reach toward it. It turns toward you. The warmth in that direction is very old. You wake with your hand extended and the smell of resin in your nose.";
                    choiceA = new[]{ "Reach toward it.", "Open your hand toward it.", "Let it come closer." }[_rng.Next(3)];
                    choiceB = new[]{ "Hold still. Don't engage.", "Don't move. Wait it out.", "Keep your hand away." }[_rng.Next(3)];
                    choiceC = new[]{ "Get rid of it in the morning.", "Throw it at first light.", "Be done with it at dawn." }[_rng.Next(3)];
                    break;
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                title, desc,
                new List<InquiryElement>
                {
                    new InquiryElement("a", choiceA, null, true, ""),
                    new InquiryElement("b", choiceB, null, true, ""),
                    new InquiryElement("c", choiceC, null, true, ""),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            ShiftTrait(DefaultTraits.Calculating, -1);
                            try { Hero.MainHero.HeroDeveloper.UnspentFocusPoints += 1; } catch { }
                            ChangeRenown(10f);
                            _trinketPhase     = 2;
                            _trinketCountdown = 7;
                            Msg("You reach. Something in the warmth extends toward you in return. You feel the contact as a jolt through the hand and through whatever the fire inside you is. When you wake, you are shaking, and there is one more thing you know how to do. You're not sure where the knowledge came from.", FireColor);
                            break;
                        case "b":
                            _trinketPhase     = 2;
                            _trinketCountdown = 7;
                            Msg("You hold still in the dream until it passes. You wake ordinary and cold. The object is where you left it.", DimColor);
                            break;
                        case "c":
                            _trinketPhase   = 0;
                            _trinketVariant = 0;
                            Msg("You throw it in the morning, as far and as deliberately as you can. You don't look for where it lands. You ride out lighter.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        // ── Deferred: FireTrinketRecurringDream — every 7 days while trinket is held ─
        private static void FireTrinketRecurringDream()
        {
            string title, desc, throwLabel, useLabel, throwMsg, successMsg, ageMsg, deathMsg;
            switch (_trinketVariant)
            {
                case 2: // Blind Eye
                    title = new[]{ "◈  The Eye Again", "◈  Still Watching", "◈  What the Eye Learned" }[_rng.Next(3)];
                    desc = new[]{
                        "The dream returns. The iron eye is waiting. It is more open than before, if that has a meaning. The attention behind it has been watching you for seven days straight and has learned something from the observation. You have options now that you did not have at the start.",
                        "The dream comes again. The eye in the medallion is as you remember it, but the quality of its attention has changed — less like a gaze, more like recognition. Seven days of being watched and it has arrived at a conclusion. It is ready to show you what that conclusion is. Or you can stop now.",
                        "You find yourself in the dream with the medallion in your palm. The eye on both sides is open, as it always is, but now it is watching something specific: you. Not your location, not your face — the thing under those. Seven days of study and it has learned something you did not mean to teach."
                    }[_rng.Next(3)];
                    throwLabel = new[]{ "Throw it away. Enough.", "Cover it and leave it behind.", "Stop. Walk away from the eye." }[_rng.Next(3)];
                    useLabel   = new[]{ "Use it.", "Meet its gaze.", "Look back into it." }[_rng.Next(3)];
                    throwMsg   = new[]{
                        "You find a river and throw it as far as the current will take it. You don't watch where it goes. You ride on without looking back. The dreams stop.",
                        "You bury it at a crossroads in the dark, deep enough that frost won't shift it. The sense of being observed lifts with each shovelful of earth. You ride out. The feeling is gone by noon. The dreams stop.",
                        "You wrap it in cloth and leave it on the threshold of a temple — whatever god watches this place can have the watching. You don't go back. The feeling of attention fades slowly, then all at once. You had not realized how constant it was until it wasn't."
                    }[_rng.Next(3)];
                    successMsg = new[]{
                        "You meet the eye and don't look away. The attention intensifies until it is the only thing in the dream. Then it gives you something — a current of influence, recognition from quarters you did not cultivate, a weight of gold arriving by paths you didn't arrange. The eye closes. You wake with the feeling that you paid something you haven't noticed missing yet.",
                        "The eye narrows — focused attention becoming focused gift. Something passes through the iris like light through a crack and lands in you. You wake to find three men who owed you favors have paid without being asked. Coin in your purse that wasn't there. The medallion is warm in your pocket. You turn it over. Both eyes are still open. Both sides are still watching.",
                        "You hold the gaze without flinching for what feels like the whole of the night. At the end of it the attention does not leave, but its character changes — from scrutiny to endorsement. You wake to influence flowing from quarters that had no reason to give it, gold by paths you didn't open, and a steadiness in the camp's regard that has no obvious cause. The eye has decided something in your favour."
                    }[_rng.Next(3)];
                    ageMsg = new[]{
                        "The eye opens all the way. The attention that has been watching you arrives all at once. The years go first — fifty of them, drawn out through you in a single second. You do not have time to regret the decision. You wake old in the way that is permanent, and the medallion in your pocket is cold iron now, nothing more.",
                        "The gaze becomes total. You feel it pass through you like a census — cataloguing what is there and marking what is owed. The debt is paid in years. Fifty of them leave in a breath. You wake with hands that are slower and a face you do not immediately recognize. The medallion in your pocket has both eyes closed now. You turn it over. Both sides. Closed."
                    }[_rng.Next(2)];
                    deathMsg = new[]{
                        "The eye opens fully and you see what is behind it. You were not supposed to survive this. The attention was always this size. You had simply not understood how small you were standing in front of it.",
                        "The attention turns absolute. You understand, at the moment it becomes too late, that you were never looking at the eye — the eye was looking at whatever is behind you, and what is behind you has no interest in leaving you intact."
                    }[_rng.Next(2)];
                    break;
                case 3: // Pale Compass
                    title = new[]{ "◈  The Bearing Again", "◈  Still Pointing", "◈  The Same Direction" }[_rng.Next(3)];
                    desc = new[]{
                        "The dream returns. The compass is in your hand and the bearing is the same bearing it always is. What is in that direction has not moved. It has only become clearer that it is aware of you now — aware that you found it, aware that you have been carrying it. Seven days. You have options.",
                        "The dream comes again and the compass comes with it, warm and precise in your palm. The bearing has not changed by a degree. But the quality of what it points toward has changed — it is attentive now in a way it was not at the start. Seven days of carrying the compass and what is at the end of the bearing has had time to notice. It is ready.",
                        "You find yourself in the dream with the compass settled in your hand, its rose fixed on the same direction it has held for seven days. What is at the end of that bearing has been patient — patient the way something is patient when it has no reason to hurry. The question now is whether you follow."
                    }[_rng.Next(3)];
                    throwLabel = new[]{ "Throw it away. Enough.", "Bury it. Stop following.", "Set it down and walk away." }[_rng.Next(3)];
                    useLabel   = new[]{ "Use it.", "Follow the bearing.", "Go where it points." }[_rng.Next(3)];
                    throwMsg   = new[]{
                        "You find a river and throw it as far as the current will take it. You don't watch where it goes. You ride on without looking back. The dreams stop.",
                        "You drop it into a gorge on the high road — watch it turn in the air until you can't see it anymore. The bearing it held was pointing somewhere behind you by then. You ride the other direction. The dreams stop.",
                        "You set it on a stone in an empty field and walk away without picking it up. The rose was still pointing its usual bearing when you last looked. You keep walking. The dreams stop."
                    }[_rng.Next(3)];
                    successMsg = new[]{
                        "You follow the bearing. In the dream it takes you somewhere real enough that you remember it on waking — a room, a face, an exchange that settled three separate debts in your favour. The influence flows in from directions you didn't solicit. Gold arrives by paths you didn't arrange. The compass lies still in your pocket, facing its usual direction.",
                        "The bearing leads you somewhere in the dream and you arrive. There is a transaction waiting — impersonal, precise, already arranged on your behalf by something with long reach. Three favors consolidated. Gold shifted into your name. Influence moving like water finding its level. You wake with the compass still pointing. It has not yet decided it is done with you.",
                        "You follow the rose to its conclusion and find what the compass has been promising: not a place, but an arrangement. A favourable redistribution of exactly the resources that matter. You wake with gold that wasn't there and goodwill you didn't earn by visible means, and the compass in your pocket pointing its usual bearing, patient and unremarkable."
                    }[_rng.Next(3)];
                    ageMsg = new[]{
                        "You reach the end of the bearing. What is there takes what it is owed. Fifty years, precise to the day. You feel each one of them leave. You wake in an older body, the compass still in your hand, the rose still pointing nowhere you can follow now. It is, in every sense, spent.",
                        "The bearing terminates. You arrive. What meets you there is not hostile — it is merely exact. An accounting was kept and now it is settled. Fifty years, measured and drawn. You wake with grey in your hair and a weight in your joints that will not leave. The compass in your hand points at nothing now. The rose has gone still."
                    }[_rng.Next(2)];
                    deathMsg = new[]{
                        "You arrive at the end of the bearing. What is there is not what you imagined. The compass was not guiding you. It was leading you.",
                        "The bearing ends. What is at the end of it is not geography. The compass was not measuring direction — it was measuring distance to something that does not announce itself. You arrive. You understand, in a final instant of clarity, what you have been carrying toward."
                    }[_rng.Next(2)];
                    break;
                default: // Ember Shard
                    title = new[]{ "◈  The Amber Again", "◈  The Flame Returns", "◈  Something Waiting" }[_rng.Next(3)];
                    desc = new[]{
                        "The dream returns. The flame in the resin is brighter than before. It knows you now — or has learned to expect you. The warmth extends outward with more precision. You could let it in further. You could put the shard somewhere it would never be found. You have been carrying it for seven days and counting.",
                        "You dream again. The amber is unchanged — still, translucent, the suspended thing inside it unmoved. But the warmth now has a direction. It is no longer radiating outward. It is orienting toward you specifically. Seven days of carrying it and it has learned your particular temperature. You have a choice to make.",
                        "The shard is in your hand in the dream. The flame inside it has grown — not larger, but denser. More certain. It presses against the amber walls from inside as if the resin is a consideration, not a boundary. You have been carrying it for seven days. It has been carrying the same question the whole time."
                    }[_rng.Next(3)];
                    throwLabel = new[]{ "Throw it away. Enough.", "Put it somewhere it won't be found.", "Be done with it. Drop it in the river." }[_rng.Next(3)];
                    useLabel   = new[]{ "Use it.", "Let it in.", "Open your hand again." }[_rng.Next(3)];
                    throwMsg   = new[]{
                        "You find a river and throw it as far as the current will take it. You don't watch where it goes. You ride on without looking back. The dreams stop.",
                        "You leave it in a ditch at the roadside, pushed into the mud far enough that no casual eye will find it. The warmth that followed you lingers for half a day. Then it is gone. The dreams stop.",
                        "You drop it into a well at dusk — listen for the sound of it striking water below. There is no sound. You ride on. The warmth you carried for seven days is simply gone, and you notice its absence the way you notice a tooth when it stops hurting."
                    }[_rng.Next(3)];
                    successMsg = new[]{
                        "You open your hand in the dream and let the warmth come the rest of the way in. It passes through you like a tide: slow, total, indifferent to your comfort. When you wake, your purse is somehow heavier, three lords who have never spoken well of you have revised their estimate, and the fire you carry burns with a steadier quality. You don't know how to explain any of it. You don't try.",
                        "The shard pulses once, twice — a heartbeat that isn't yours. Something passes between you in the dream, a transaction with no words. You wake with ash on your fingers and a stranger's debt settled in your name. Gold finds you before noon. Morale in the camp runs higher than the weather warrants. The shard in your pocket is warm but ordinary-looking. Nobody else feels it.",
                        "You hold nothing back in the dream and the warmth holds nothing back in return. It is like standing too close to a forge — your skin does not burn but you understand what burning is. When you wake, three favors have been called in overnight by no one you instructed. Coin arrives. The camp's temper steadies. The shard sits quiet in your coat, waiting."
                    }[_rng.Next(3)];
                    ageMsg = new[]{
                        "The warmth comes all the way in. You let it. For a moment you think it is good. Then the years begin to run. Fifty of them. Not taken — spent, which is different. You wake gasping, and the face looking back at you from still water is the face of someone who came to this late and paid for the privilege. The shard is cold. It won't warm again.",
                        "The warmth comes all the way in. The amber cracks in the dream — hairline fractures that spread until the whole piece is a web of them. You understand what is being traded before it is finished. Fifty years, drawn precisely through you like thread through a needle. You wake with grey where there wasn't grey and a weight in your joints that will not leave. The shard in your pocket is just amber now."
                    }[_rng.Next(2)];
                    deathMsg = new[]{
                        "The warmth comes all the way in and keeps coming. You understand, in the last moment, that it was never warmth — it was appetite. The fire inside you feeds it until there is nothing left to feed with. You do not wake.",
                        "You let it in completely and it does not stop. The warmth becomes heat becomes the specific temperature of something feeding. The last clear thought you have is that you were the fuel the whole time. You do not wake."
                    }[_rng.Next(2)];
                    break;
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                title, desc,
                new List<InquiryElement>
                {
                    new InquiryElement("a", throwLabel, null, true, ""),
                    new InquiryElement("b", useLabel,   null, true, ""),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            _trinketPhase   = 0;
                            _trinketVariant = 0;
                            Msg(throwMsg, DimColor);
                            break;
                        case "b":
                        {
                            double roll = _rng.NextDouble();
                            if (roll < 0.70)
                            {
                                // Success — significant gains, chain continues
                                _trinketCountdown = 7;
                                try { if (Hero.MainHero?.Clan != null) Hero.MainHero.Clan.Influence += 60f; } catch { }
                                ChangeRenown(50f);
                                ChangeGold(2000);
                                AddMorale(15f);
                                Msg(successMsg, GoodColor);
                            }
                            else if (roll < 0.90)
                            {
                                // Age 50 years — ends chain
                                _trinketPhase   = 0;
                                _trinketVariant = 0;
                                AgePlayer(50 * 365);
                                Msg(ageMsg, BadColor);
                            }
                            else
                            {
                                // Instant death — ends chain
                                _trinketPhase   = 0;
                                _trinketVariant = 0;
                                Msg(deathMsg, BadColor);
                                try { KillCharacterAction.ApplyByMurder(Hero.MainHero, null, false); } catch { }
                            }
                            break;
                        }
                    }
                }, null, "", false), false, true);
        }

    }
}
