// =============================================================================
// ASH AND EMBER — SettlementEncounters.Events5.cs
// Cinder Vigil, the Cold Machine, and their deferred consequences.
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
        // ── EC_CinderVigil — enter village or town, Battania/Strugia/Aserai/Khuzait territory ──
        // Temple soldiers conducting a purge on local practitioners (druids, alchemists, shamans).
        // Not before day 50. 150-day cooldown between recurrences.
        private static void EC_CinderVigil(Settlement s)
        {
            if (_cinderVigilCooldown > 0) return;

            string cult    = s.Culture?.StringId ?? "";
            bool isNorth   = cult == "battania" || cult == "sturgia";
            bool isAserai  = cult == "aserai";
            bool isKhuzait = cult == "khuzait";
            if (!isNorth && !isAserai && !isKhuzait) return;

            _cinderVigilCooldown = 150;

            string[] relevantIds = isNorth   ? new[] { "battania", "sturgia" }
                                 : isAserai  ? new[] { "aserai" }
                                 : new[] { "khuzait" };

            string target = isNorth ? "druids" : isAserai ? "alchemists" : "shamans";

            string description = isNorth
                ? "You come across a column of Temple soldiers at a crossroads between burned longhouses. They have cornered a group of druids — seven or eight, most elderly, some with root-stained hands that mark them as practitioners. The officer is reading from a warrant. His men have already drawn weapons. This is organized. Whatever this is, they were sent to do it."
                : isAserai
                ? "In the afternoon heat of the market quarter, Temple soldiers have cordoned off a workshop. The scholars inside — three alchemists, a cartographer, an old physician — sit with their hands visible, watching their equipment being smashed methodically. A soldier calls names from a list. Across the street, the market crowd watches in the silence of people who have learned not to ask questions."
                : "On the edge of the steppe settlement, Temple soldiers have separated a shaman from the rest of the camp and surrounded her on open ground. The camp watches from a distance, held back by a line of spears. She is old. She is not fighting. The soldiers are waiting for something — a word, perhaps, or a reason to act. They will make one if none arrives.";

            var factionLords = Hero.AllAliveHeroes
                .Where(h => h.IsLord && h.IsAlive && !h.IsPrisoner && h != Hero.MainHero
                         && relevantIds.Contains(h.MapFaction?.StringId))
                .ToList();

            var templeLords = Hero.AllAliveHeroes
                .Where(h => h.IsLord && h.IsAlive && !h.IsPrisoner && h != Hero.MainHero
                         && h.MapFaction?.StringId == "vlandia")
                .ToList();

            string leadHint   = SkillHint(DefaultSkills.Leadership, 0.30f, "Convince the commander");
            var bestCombat    = new[] { DefaultSkills.OneHanded, DefaultSkills.TwoHanded, DefaultSkills.Polearm }
                                    .OrderByDescending(sk => GetSkill(sk)).First();
            string combatHint = SkillHint(bestCombat, 0.30f, "Drive them off");

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "★  The Cinder Vigil",
                description,
                new List<InquiryElement>
                {
                    new InquiryElement("a",
                        $"Ride past. The {target} will deal with their own fate.",
                        null, true,
                        $"Stay out of it. (Mercy -1)"),
                    new InquiryElement("b",
                        $"Speak to the soldiers. Argue for the {target}.",
                        null, true,
                        $"Reason with the commander. Gain Mercy, Calculating. {leadHint}"),
                    new InquiryElement("c",
                        $"Draw your weapon and drive the soldiers off.",
                        null, true,
                        $"Force the issue. Gain Calculating -1. -20 with Temple lords. -5 with 3 Empire lords. {combatHint}"),
                    new InquiryElement("d",
                        $"Offer your assistance to the soldiers. The {target} may be Ashen servants.",
                        null, true,
                        "Fall in beside them. Gain Calculating -1. -10 with a local lord. +10 with Temple lords. Gain 1000 gold."),
                    new InquiryElement("e",
                        $"Join the killing. The {target} mean nothing to you.",
                        null, true,
                        $"Cruelty is its own reward. Gain Mercy -1. -20 with a local lord. +2 with a Temple lord. Learn the BLOOD discipline if you don't know it."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                        {
                            ShiftTrait(DefaultTraits.Mercy, -1);
                            Msg($"You ride past. The sounds follow you down the road a distance. Not far enough.", DimColor);
                            break;
                        }
                        case "b":
                        {
                            ShiftTrait(DefaultTraits.Mercy, 1);
                            ShiftTrait(DefaultTraits.Calculating, 1);
                            if (SkillRoll(DefaultSkills.Leadership, 0.30f))
                            {
                                Msg($"The officer listens. Eventually, he listens. Whatever you said held weight. The {target} are released with a warning that will expire in a week. You take that as the best available outcome.", GoodColor);
                                if (factionLords.Count > 0)
                                {
                                    var lord = factionLords[_rng.Next(factionLords.Count)];
                                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, 10, false);
                                    Msg($"(Relation with {lord.Name}: +10)", GoodColor);
                                }
                                GrantMagicalTalent();
                            }
                            else
                            {
                                Msg($"You argue. The commander listens with the patience of a man who has heard this before and decided it doesn't apply. The {target} are taken anyway. You were not enough.", DimColor);
                            }
                            break;
                        }
                        case "c":
                        {
                            ShiftTrait(DefaultTraits.Calculating, -1);
                            foreach (var lord in templeLords)
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, -20, false);
                            if (templeLords.Count > 0)
                                Msg("(Relation with all Temple lords: -20)", BadColor);
                            var empireLords = Hero.AllAliveHeroes
                                .Where(h => h.IsLord && h.IsAlive && !h.IsPrisoner && h != Hero.MainHero
                                         && (h.MapFaction?.StringId == "empire"   || h.MapFaction?.StringId == "empire_w"
                                          || h.MapFaction?.StringId == "empire_s" || h.MapFaction?.StringId == "empire_n"))
                                .OrderBy(_ => _rng.Next()).Take(3).ToList();
                            foreach (var lord in empireLords)
                            {
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, -5, false);
                                Msg($"(Relation with {lord.Name}: -5)", BadColor);
                            }
                            if (SkillRoll(bestCombat, 0.30f))
                            {
                                Msg($"The line breaks. The soldiers pull back in the organized retreat of men who know when a fight has changed. The {target} scatter before anyone reorganizes. You have made enemies today, but the {target} are alive.", GoodColor);
                                if (factionLords.Count > 0)
                                {
                                    var lord = factionLords[_rng.Next(factionLords.Count)];
                                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, 10, false);
                                    Msg($"(Relation with {lord.Name}: +10)", GoodColor);
                                }
                                GrantMagicalTalent();
                            }
                            else
                            {
                                Msg($"You charge. It is not enough. The soldiers hold and the {target} are cut down while you are still fighting through. You survive the engagement. They did not.", BadColor);
                            }
                            break;
                        }
                        case "d":
                        {
                            ShiftTrait(DefaultTraits.Calculating, -1);
                            if (factionLords.Count > 0)
                            {
                                var lord = factionLords[_rng.Next(factionLords.Count)];
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, -10, false);
                                Msg($"(Relation with {lord.Name}: -10)", BadColor);
                            }
                            foreach (var lord in templeLords)
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, 10, false);
                            if (templeLords.Count > 0)
                                Msg("(Relation with Temple lords: +10)", GoodColor);
                            ChangeGold(1000);
                            Msg($"You fall in beside the soldiers. The work is brief. The commander thanks you with the formal distance of a man who does not need your name. He leaves a purse. The {target} are gone. The road is clear.", DimColor);
                            break;
                        }
                        case "e":
                        {
                            ShiftTrait(DefaultTraits.Mercy, -1);
                            if (factionLords.Count > 0)
                            {
                                var lord = factionLords[_rng.Next(factionLords.Count)];
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, -20, false);
                                Msg($"(Relation with {lord.Name}: -20)", BadColor);
                            }
                            if (templeLords.Count > 0)
                            {
                                var lord = templeLords[_rng.Next(templeLords.Count)];
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, 2, false);
                                Msg($"(Relation with {lord.Name}: +2)", GoodColor);
                            }
                            // The taking teaches its own art — the BLOOD discipline
                            // (Reap's heir in the merged magic).
                            if (!MageElementKnowledge.HasBlood)
                            {
                                MageElementKnowledge.LearnBlood();
                                Msg("Something in the taking stays with you. You know the BLOOD discipline now — a lord's death by your hand gives back the years the fire has burned.", BadColor);
                            }
                            Msg($"You join them. There is a specific kind of ease that comes with it — no decision to make, no weight to carry afterward. When it is over, you feel the familiar warmth. Something given back. The soldiers give you a wide berth on the road home.", BadColor);
                            break;
                        }
                    }
                }, null, "", false), false, true);
        }

        // ── AFTER BATTLE: The Cold Machine [Ashen enemy defeated, mage player] ────────────────
        private static void EB_AshenMachinery()
        {
            _ashenMachineryCooldown = 90;
            bool mage = MageKnowledge.IsMage;

            string intro = mage
                ? "Among the fallen Ashen your men have found something they brought to you because they did not know what else to do with it. " +
                  "It is a framework of iron and dark glass packed with crystals arranged in a pattern that makes your teeth ache to look at. " +
                  "The mages were moving toward it when your soldiers cut them down. You recognise what it is — not the design, but the purpose. " +
                  "A weapon shaped by someone who understood cold fire from the outside in. It did not fire. Yet."
                : "Among the fallen Ashen your men have found something and brought it to you because they did not know what else to do with it. " +
                  "A framework of iron and dark glass packed with crystals, arranged in no pattern you recognise. " +
                  "The mages were moving toward it when your soldiers cut them down. " +
                  "You do not know what it does. You know that the Ashen were willing to die for it.";

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "⚗  The Cold Machine",
                intro,
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Destroy it. Some things should not leave this field.", null, true,
                        "Nothing happens. It is gone."),
                    new InquiryElement("b", "Break it apart. The crystals and glass will sell.", null, true,
                        "+5000 coin. No further consequence."),
                    new InquiryElement("c", "Point it at an enemy. Choose a target.", null, true,
                        "A city burns. You age three years. 50% chance of war with that faction."),
                    new InquiryElement("d", "Sell it to the black market — intact, no questions asked.", null, true,
                        "+3000 coin now. A city burns in fourteen days. You will not be named."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            Msg("Your soldiers break it with hammers and scatter the pieces across the field. " +
                                "The crystals go dark one by one as they leave the frame, each one releasing a brief cold pulse you feel in your back teeth. " +
                                "Then nothing. The field smells like iron and old snow. " +
                                "You leave it where it lies.", DimColor);
                            break;
                        case "b":
                            ChangeGold(5000);
                            Msg("The crystals come free cleanly. The glasswork is unusual — someone in the cities will pay for it without asking why. " +
                                "By the time the baggage train reaches the next waypoint the pieces are already wrapped and labelled by your quartermaster. " +
                                "Five thousand coin by nightfall. The machine is gone.", GoldColor);
                            break;
                        case "c":
                            MageKnowledge._deferredInquiry = ShowKingdomSelectorForMachinery;
                            break;
                        case "d":
                            _ashenMachineryCountdown = 14;
                            ChangeGold(3000);
                            Msg("A contact is found through the usual chain of intermediaries. " +
                                "Three thousand coin changes hands before you have fully explained what you are selling. " +
                                "They do not ask questions. Neither do you. The machine leaves on a cart you do not follow. " +
                                "Somewhere, in fourteen days, it will be pointed at something. You will not be named.", DimColor);
                            break;
                    }
                }, null, "", false), false, true);
        }

        private static void ShowKingdomSelectorForMachinery()
        {
            var playerFaction = Hero.MainHero?.MapFaction;
            var validKingdoms = Kingdom.All
                .Where(k => !k.IsEliminated
                         && k.StringId != "ashen_kingdom"
                         && (IFaction)k != playerFaction)
                .ToList();

            if (validKingdoms.Count == 0)
            {
                Msg("There are no suitable targets remaining. You dismantle the machine.", DimColor);
                return;
            }

            var elements = validKingdoms
                .Select(k => new InquiryElement(k.StringId, k.Name?.ToString() ?? k.StringId, null, true,
                    "A random city of this faction will be struck by the machine."))
                .ToList();

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "◆  Choose Your Target",
                "The machine is cold and ready. Point it. When you activate it you will age three years — " +
                "the cost of channelling this much cold fire at once. Choose carefully.",
                elements,
                false, 1, 1, "Release it", "Pull back",
                selected =>
                {
                    if (selected == null || selected.Count == 0) return;
                    string kId = selected[0].Identifier as string;
                    var targetK = Kingdom.All.FirstOrDefault(k => k.StringId == kId);
                    if (targetK == null) return;

                    AgingSystem.AgeHero(Hero.MainHero, 3 * 365);

                    string cityName = DevastateCityInKingdom(targetK);

                    bool declaredWar = false;
                    if (_rng.NextDouble() < 0.5)
                    {
                        var playerKingdom = Hero.MainHero?.Clan?.Kingdom;
                        if (playerKingdom != null && !targetK.IsAtWarWith(playerKingdom))
                        {
                            try { DeclareWarAction.ApplyByDefault(targetK, playerKingdom); } catch { }
                            declaredWar = true;
                        }
                    }

                    string cityLine = string.IsNullOrEmpty(cityName)
                        ? "A city in their territory"
                        : cityName;

                    if (declaredWar)
                        Msg($"The crystals release all at once. You feel three years leave you in a single breath — not pain, just subtraction. " +
                            $"{cityLine} takes the discharge. Prosperity, food, order — all of it collapses. " +
                            $"When the word reaches their council they know it was not natural. " +
                            $"Someone has already given them your name.", BadColor);
                    else
                        Msg($"The crystals release all at once. You feel three years leave you in a single breath — not pain, just subtraction. " +
                            $"{cityLine} takes the discharge. Prosperity, food, order — all of it collapses. " +
                            $"The evidence trails away into rumour. No one is certain who held the machine. " +
                            $"Not yet.", AshenColor);
                },
                null, "", false), false, true);
        }

        private static string DevastateCityInKingdom(Kingdom kingdom)
        {
            var towns = Settlement.All
                .Where(s => s.IsTown && s.Town != null && s.OwnerClan?.Kingdom == kingdom)
                .ToList();
            if (towns.Count == 0) return null;

            var target = towns[_rng.Next(towns.Count)];
            try { target.Town.Prosperity = 0f; } catch { }
            try { target.Town.Security   = 0f; } catch { }
            try { target.Town.FoodStocks = 0f; } catch { }
            try
            {
                // Clear garrison roster
                if (target.Town.GarrisonParty?.MemberRoster != null)
                {
                    foreach (var elem in target.Town.GarrisonParty.MemberRoster.GetTroopRoster().ToList())
                        try { target.Town.GarrisonParty.MemberRoster.AddToCounts(elem.Character, -elem.Number); } catch { }
                }
            }
            catch { }

            return target.Name?.ToString();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // E_WeaponInventor — town / Aserai / one-time
        // An alchemist in the bazaar solicits funding for a devastating weapon.
        // ═══════════════════════════════════════════════════════════════════════

        private static void E_WeaponInventor(Settlement s)
        {
            _weaponInventorFound = 1;
            string sName = s.Name?.ToString() ?? "the city";

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "☁  The Alchemist's Promise",
                "In the shadow of a market stall, a gaunt man with yellowed fingers and soot-streaked robes steps into your path. He speaks quickly, eyes bright with that particular fever of a man too close to his own ideas.\n\n" +
                "A formula, he says — ground bone, black sand, refined sulphur. One breath of it and armies collapse. Cities empty. The world has never seen its equal. He needs only coin to gather the materials. A dozen failed attempts, he admits freely. This time he is certain.",
                new List<InquiryElement>
                {
                    new InquiryElement("a", "Fund his work fully.  (10,000 gold)",
                        null, (Hero.MainHero?.Gold ?? 0) >= 10000,
                        "Generous. With proper resources, there is an 80% chance this produces something real."),
                    new InquiryElement("b", "Offer him what you can spare.  (5,000 gold)",
                        null, (Hero.MainHero?.Gold ?? 0) >= 5000,
                        "A reasonable investment. Even odds — 50% chance of a working formula."),
                    new InquiryElement("c", "Press a few coins into his hand.  (2,000 gold)",
                        null, (Hero.MainHero?.Gold ?? 0) >= 2000,
                        "Meagre. Desperation does not make up the difference. One chance in five."),
                    new InquiryElement("d", "Walk away. His problems are not yours.",
                        null, true,
                        "[Calculating] Leave him to it. The watch may find him first — or his powder may find the city."),
                    new InquiryElement("e", "Point him out to the city watch.",
                        null, true,
                        "[Calculating] A reward is likely. Though if he has already started mixing, the outcome may not wait for the authorities."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "a":
                            if (!ChangeGold(-10000)) return;
                            if (_rng.NextDouble() < 0.80) InventorResult1(); else InventorResult2();
                            break;
                        case "b":
                            if (!ChangeGold(-5000)) return;
                            if (_rng.NextDouble() < 0.50) InventorResult1(); else InventorResult2();
                            break;
                        case "c":
                            if (!ChangeGold(-2000)) return;
                            if (_rng.NextDouble() < 0.20) InventorResult1(); else InventorResult2();
                            break;
                        case "d":
                            if (_rng.NextDouble() < 0.50) InventorResult2(); else InventorResult3(s, sName);
                            break;
                        case "e":
                            if (_rng.NextDouble() < 0.33) InventorResult3(s, sName); else InventorResult4();
                            break;
                    }
                },
                null, "", false), false, true);
        }

        private static void InventorResult1()
        {
            // The formula works — but it is not a thing to carry. Its worth is in
            // the wanting: a buyer with no scruples pays a fortune for the vessel
            // and the recipe both, and the alchemist is never seen again.
            ChangeGold(18000);
            Msg("He returns three days later clutching a clay vessel sealed with black wax. His hands are steady; his voice is not. " +
                "\"It works,\" he whispers. \"In still air. I dare not carry it further.\" " +
                "You do not carry it either. Word travels fast; within the week a buyer with no name and no scruples takes the vessel and the recipe both, " +
                "and leaves behind a strongbox heavier than any weapon. You do not ask where the others who funded him went — or where he goes now.",
                FireColor);
        }

        private static void InventorResult2()
        {
            Msg("He does not return. A week passes, then a fortnight. You make enquiries — no one has seen him since the second day. " +
                "Perhaps he spent the coin on something else. Perhaps he simply failed again, and had the sense to disappear before you found out.",
                DimColor);
        }

        private static void InventorResult3(Settlement s, string sName)
        {
            try
            {
                if (s.IsTown && s.Town != null)
                {
                    try { s.Militia = Math.Max(0f, s.Militia * 0.10f); } catch { }

                    var garrison = s.Town?.GarrisonParty?.MemberRoster;
                    if (garrison != null)
                    {
                        foreach (var e in garrison.GetTroopRoster()
                            .Where(e => !e.Character.IsHero).ToList())
                        {
                            int healthy = e.Number - e.WoundedNumber;
                            int toWound = Math.Min(healthy,
                                (int)(healthy * (0.50f + (float)_rng.NextDouble() * 0.30f)));
                            if (toWound > 0)
                                try { garrison.AddToCounts(e.Character, 0, false, toWound); } catch { }
                        }
                    }

                    try { s.Town.Prosperity = Math.Max(100f, s.Town.Prosperity - 300f); } catch { }
                    try { s.Town.Security   = Math.Max(0f,   s.Town.Security   -  30f); } catch { }
                }
            }
            catch { }

            Msg($"Something goes wrong before anyone can stop it. A thick yellow-green reek rolls out of a district near the market, " +
                $"driving animals mad before it reaches the men. By the time anyone understands what has happened, that quarter of {sName} is empty of the living. " +
                "They say the alchemist was the first to breathe it. He left no note.",
                BadColor);
        }

        private static void InventorResult4()
        {
            ChangeGold(1000);
            Msg("You point him out to a passing watchman without breaking your stride. You are half a district away when the shouting begins. " +
                "Three days later a city official finds you at your lodgings — a sealed pouch, no explanation, no name on the note. " +
                "The alchemist is not spoken of again.",
                GoldColor);
        }

        // ── Deferred: FireAshenMachineryConsequence — 14 days after option D ──
        private static void FireAshenMachineryConsequence()
        {
            if (MageKnowledge._deferredInquiry != null) { _ashenMachineryCountdown = 1; return; }

            var validKingdoms = Kingdom.All
                .Where(k => !k.IsEliminated && k.StringId != "ashen_kingdom"
                         && (IFaction)k != Hero.MainHero?.MapFaction)
                .ToList();

            if (validKingdoms.Count == 0) return;

            var targetK = validKingdoms[_rng.Next(validKingdoms.Count)];
            string cityName = DevastateCityInKingdom(targetK);

            string cityLine = string.IsNullOrEmpty(cityName)
                ? $"A city in {targetK.Name}'s territory"
                : $"{cityName}, in {targetK.Name}'s territory";

            MageKnowledge._deferredInquiry = () =>
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "⚗  The Machine Fires",
                    $"Fourteen days ago you sold something to someone who did not ask questions. " +
                    $"Today, {cityLine} has been struck by something the survivors cannot describe — only that it came from nowhere and took everything with it. " +
                    $"Prosperity gone. Food gone. The garrison walks out empty-handed. " +
                    $"No one is pointing at you. The chain of hands is long. " +
                    $"You know what you sold.",
                    new List<InquiryElement> { new InquiryElement("ok", "You know.", null, true, "") },
                    false, 1, 1, "Close", "",
                    _ => { }, null, "", false), false, true);
            };
        }
    }
}
