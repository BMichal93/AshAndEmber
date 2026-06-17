// =============================================================================
// ASH AND EMBER — TavernCampaignBehavior.Outcomes.cs
// The drinking loop and every good/bad outcome.
// Partial of TavernCampaignBehavior (shared static state lives in TavernCampaignBehavior.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class TavernCampaignBehavior
    {
        // =====================================================================
        // DRINK ROUND LOGIC
        // =====================================================================
        private static void DrinkRound(int cost, int tier)
        {
            _diceMode = false;
            if ((Hero.MainHero?.Gold ?? 0) < cost)
            {
                Msg($"You count your coin. Not enough for another round. ({cost} needed)", BadColor);
                return;
            }

            try { Hero.MainHero?.ChangeHeroGold(-cost); } catch { }
            _roundsDrunk++;
            _totalSpent += cost;
            _lastDrinkCost = cost;

            // Holding your drink is its own slow conditioning — a trickle of
            // Athletics XP per round, a touch more for the stronger stuff.
            try { Hero.MainHero?.HeroDeveloper?.AddSkillXp(DefaultSkills.Athletics, 3f + tier); } catch { }

            // Athletics check — harder with each round consumed
            int athletics = 0;
            try { athletics = Hero.MainHero?.GetSkillValue(DefaultSkills.Athletics) ?? 0; } catch { }

            float drunkPenalty = (_roundsDrunk - 1) * 0.09f;
            float tierBonus    = (tier - 1) * 0.10f;
            float chance       = Math.Min(0.92f, Math.Max(0.05f, 0.38f + athletics * 0.002f + tierBonus - drunkPenalty));

            bool controlled = _rng.NextDouble() < chance;

            // After 5+ rounds a failed check means passing out
            bool passedOut = !controlled && _roundsDrunk >= 5;

            // Very unlucky even on round 3+
            if (!controlled && _roundsDrunk >= 3 && _rng.NextDouble() < 0.35)
                passedOut = true;

            if (passedOut)
            {
                BeginPassOut();
                return;
            }

            // Roll the outcome
            string resultText;
            if (controlled)
                resultText = RollGoodOutcome(tier);
            else
                resultText = RollBadOutcome(tier);

            try
            {
                MBTextManager.SetTextVariable("TAVERN_RESULT_TEXT",
                    $"Round {_roundsDrunk}. {resultText}");
                GameMenu.SwitchToMenu("ldm_tavern_result");
            }
            catch { }
        }

        private static void BeginPassOut()
        {
            try
            {
                // Give athletics XP for making it this far
                try { Hero.MainHero?.HeroDeveloper?.AddSkillXp(DefaultSkills.Athletics, 80f); } catch { }

                _soberHoursTotal   = 4f + _rng.Next(5); // 4-8 hours
                _soberHoursElapsed = 0f;
                _soberDone         = false;
                AddMorale(-2f);
                GameMenu.SwitchToMenu("ldm_tavern_sober_up");
            }
            catch { }
        }

        // ── Good outcomes ─────────────────────────────────────────────────────
        private static string RollGoodOutcome(int tier)
        {
            // Higher tier shifts weight toward better outcomes
            int roll = _rng.Next(100) + (tier - 1) * 15;

            if (roll < 28)
                return OutcomeNothing();
            if (roll < 45)
                return OutcomeRelationGain();
            if (roll < 58)
                return OutcomeRecruit(tier);
            if (roll < 70)
                return OutcomeGossip(tier);
            if (roll < 80)
                return OutcomeMerchantTip(tier);
            if (roll < 89)
                return OutcomeLordEncounterGood(tier);
            if (roll < 97)
                return OutcomeFoundPurse(tier);
            if (roll < 108)
                return OutcomeDiceGame(tier);
            if (roll < 119)
                return OutcomeOldVeteran();
            return OutcomeSecretMap(tier);
        }

        // ── Bad outcomes ──────────────────────────────────────────────────────
        private static string RollBadOutcome(int tier)
        {
            // Higher tier slightly reduces severity (finer establishments, politer patrons)
            int roll = _rng.Next(100) - (tier - 1) * 8;

            if (roll < 22)
                return OutcomeOffend();
            if (roll < 42)
                return OutcomeBrawl();
            if (roll < 56)
                return OutcomeLordEncounterBad();
            if (roll < 68)
                return OutcomePuking();
            if (roll < 82)
                return OutcomeJail();
            if (roll < 91)
                return OutcomeCheated(tier);
            if (roll < 99)
                return OutcomeSpilledSecret();
            return OutcomeOffend();
        }

        // ── Outcome implementations ───────────────────────────────────────────

        private static string OutcomeNothing()
        {
            string[] lines =
            {
                "You drink. The fire pops. Someone laughs at the far end of the room. Nothing worth remembering happens — and that, tonight, is enough.",
                "The ale is honest work. You nurse it quietly and watch the room. Nobody notices you. Nobody needs to.",
                "A warm evening. You listen to a bard butcher an old song and decide the world has worse problems.",
                "The cup empties. You feel it. The night asks nothing of you.",
            };
            return lines[_rng.Next(lines.Length)];
        }

        private static string OutcomeRelationGain()
        {
            try
            {
                // Try to find a notable in the current settlement
                var settlement = Settlement.CurrentSettlement;
                var notables = settlement?.Notables?.Where(h => h != null && h.IsAlive && h != Hero.MainHero).ToList();
                if (notables != null && notables.Count > 0)
                {
                    var notable = notables[_rng.Next(notables.Count)];
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, notable, 1, false);
                    return $"You end up sharing a table with {notable.Name}. The conversation is easier than expected — " +
                           $"common ground found in the bottom of a cup. (Relation with {notable.Name}: +1)";
                }

                // Fallback: random lord
                ChangeRelWithRandomHero(1);
                return "By the second cup someone has decided you are fine company. A bond formed in firelight — thin, but real.";
            }
            catch
            {
                return "By the second cup someone has decided you are fine company.";
            }
        }

        private static string OutcomeRecruit(int drinkTier)
        {
            try
            {
                // Better drink → chance of a higher-tier soldier
                int tier = drinkTier == 1 ? 1 + _rng.Next(2)   // tier 1-2
                         : drinkTier == 2 ? 1 + _rng.Next(3)   // tier 1-3
                                          : 2 + _rng.Next(3);  // tier 2-4
                CharacterObject troop = GetRecruitOfTier(tier);
                if (troop != null)
                {
                    try { MobileParty.MainParty.MemberRoster.AddToCounts(troop, 1); } catch { }
                    return $"A {troop.Name} slumped in the corner straightens when your coin hits the table. " +
                           $"\"You hiring?\" You are. They follow you out. (+1 {troop.Name})";
                }
                // Fallback if no troop found
                try { Hero.MainHero?.ChangeHeroGold(30); } catch { }
                return "A soldier in the corner offers steady eyes and a steady hand in exchange for a place at your fire. " +
                       "They accept the coin instead when the roster is full. (+30 denars)";
            }
            catch
            {
                return "A down-on-their-luck soldier shares your table. They have nowhere better to be.";
            }
        }

        private static string OutcomeGossip(int tier)
        {
            string[] gossips =
            {
                "A man leans close and whispers: a lord two towns over has been moving coin in the dead of night. " +
                "Whether it is taxes, bribes, or something stranger, he cannot say.",
                "An old woman with sharp eyes tells you about a patrol that did not come back last month. " +
                "The garrison is pretending it did. She finds that suspicious. So do you.",
                "A merchant in his cups confides that one of the local trade guilds has been buying grain at twice the market price. " +
                "Someone is expecting a siege — or creating one.",
                "Two soldiers argue quietly about orders they were not supposed to hear. You catch enough to know something is moving that should not be.",
                "A courier fresh off the road talks too freely. A lord nearby is changing sides — or thinking about it.",
            };

            float influence = tier == 1 ? 5f : tier == 2 ? 15f : 35f;
            try
            {
                if (Hero.MainHero?.Clan != null)
                    Hero.MainHero.Clan.Influence += influence;
            }
            catch { }

            return gossips[_rng.Next(gossips.Length)] + $" (+{(int)influence} influence)";
        }

        private static string OutcomeMerchantTip(int tier)
        {
            // Tier 1 (20g): modest windfall; Tier 2 (100g): worth the risk; Tier 3 (500g): potentially profitable
            int gold = tier == 1 ?  50 + _rng.Next(76)    //  50-125g
                     : tier == 2 ? 200 + _rng.Next(201)   // 200-400g
                                 : 600 + _rng.Next(601);  // 600-1200g
            try { Hero.MainHero?.ChangeHeroGold(gold); } catch { }
            return $"A merchant is grateful for your company — or perhaps just drunk enough to be generous. " +
                   $"He presses a purse into your hand before his friends drag him off. (+{gold} denars)";
        }

        private static string OutcomeLordEncounterGood(int tier)
        {
            int bonus = tier == 1 ? 1 : tier == 2 ? 2 : 4;
            try
            {
                var lord = GetLordInSettlement();
                if (lord != null)
                {
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, bonus, false);
                    return $"{lord.Name} enters the common room, sees you at the bar, and sits. " +
                           $"Two people without their retinues, sharing something honest. " +
                           $"You part better than you arrived. (Relation with {lord.Name}: +{bonus})";
                }
            }
            catch { }
            return "A lord's steward buys you a cup — which means the lord noticed you, and found it worth noting. The gesture costs nothing and means something.";
        }

        private static string OutcomeFoundPurse(int tier)
        {
            int gold = tier == 1 ?  20 + _rng.Next(41)   //  20-60g
                     : tier == 2 ?  80 + _rng.Next(121)  //  80-200g
                                 : 250 + _rng.Next(251); // 250-500g
            try { Hero.MainHero?.ChangeHeroGold(gold); } catch { }
            return $"You find a fat little purse wedged between the bench and the wall. " +
                   $"You wait. Nobody comes back for it. You reason that whoever lost it has had worse nights than you. (+{gold} denars)";
        }

        // ── Dedicated dice game (menu-driven, player-chosen bet) ──────────────
        private static void ResolveDiceGame(int bet)
        {
            if ((Hero.MainHero?.Gold ?? 0) < bet)
            {
                Msg($"You count your coin. Not enough to cover that wager. ({bet} denars needed)", BadColor);
                return;
            }

            _diceMode    = true;
            _diceLastBet = bet;

            int playerRoll   = _rng.Next(1, 7) + _rng.Next(1, 7);
            int opponentRoll = _rng.Next(1, 7) + _rng.Next(1, 7);
            bool cheated     = _rng.NextDouble() < 0.08;

            string resultText;

            if (cheated)
            {
                int lost = Math.Min(bet, Hero.MainHero?.Gold ?? 0);
                if (lost > 0) try { Hero.MainHero?.ChangeHeroGold(-lost); } catch { }
                string[] lines =
                {
                    $"You roll well. They roll better. Something about the way the dice land doesn't sit right — but the coin is gone before you can place it. (-{lost} denars)",
                    $"The cup comes up short. You replay the sequence in your mind on the walk back to the bar. Those dice didn't tumble right. (-{lost} denars)",
                    $"A clean game on the surface. You lose steadily. Only afterward does it click — the weight of those dice. (-{lost} denars)",
                };
                resultText = lines[_rng.Next(lines.Length)];
            }
            else if (playerRoll > opponentRoll)
            {
                try { Hero.MainHero?.ChangeHeroGold(bet); } catch { }
                string[] lines =
                {
                    $"You roll {playerRoll}. They roll {opponentRoll}. The cup lifts. The table sighs. You collect. (+{bet} denars)",
                    $"{playerRoll} against {opponentRoll}. A clean win. You drag the coin across the felt without ceremony. (+{bet} denars)",
                    $"Your {playerRoll} against their {opponentRoll}. Nobody argues with the numbers. (+{bet} denars)",
                };
                resultText = lines[_rng.Next(lines.Length)];
            }
            else
            {
                int lost = Math.Min(bet, Hero.MainHero?.Gold ?? 0);
                if (lost > 0) try { Hero.MainHero?.ChangeHeroGold(-lost); } catch { }
                string[] lines =
                {
                    $"You roll {playerRoll}. They roll {opponentRoll}. The felt pulls the coin toward them. You sit with it a moment. (-{lost} denars)",
                    $"{playerRoll} to their {opponentRoll}. The cup doesn't lie. You push the coin forward. (-{lost} denars)",
                    $"Their {opponentRoll}, your {playerRoll}. The hand that sweeps the table isn't yours tonight. (-{lost} denars)",
                };
                resultText = lines[_rng.Next(lines.Length)];
            }

            try
            {
                MBTextManager.SetTextVariable("TAVERN_RESULT_TEXT", resultText);
                GameMenu.SwitchToMenu("ldm_tavern_result");
            }
            catch { }
        }

        // ── Dice game as a drinking outcome (random windfall) ─────────────────
        private static string OutcomeDiceGame(int tier)
        {
            int gold = tier == 1 ?  30 + _rng.Next(51)    //  30-80g
                     : tier == 2 ? 100 + _rng.Next(151)   // 100-250g
                                 : 300 + _rng.Next(401);  // 300-700g
            try { Hero.MainHero?.ChangeHeroGold(gold); } catch { }
            string[] lines =
            {
                $"A circle of dice-rollers scoots over to make room. The bones roll true all night. (+{gold} denars)",
                $"Three clean passes and the table pays. The other players have the grace to look impressed. (+{gold} denars)",
                $"A leather cup slaps the table. Your toss. The cup lifts. The room groans. You collect. (+{gold} denars)",
            };
            return lines[_rng.Next(lines.Length)];
        }

        private static string OutcomeOldVeteran()
        {
            int xp = 60 + _rng.Next(41); // 60-100
            try { Hero.MainHero?.HeroDeveloper?.AddSkillXp(DefaultSkills.Tactics, xp); } catch { }
            string[] lines =
            {
                $"A veteran with a scar across his chin and nothing left to prove talks to you for an hour. " +
                $"He names formations the academy does not teach anymore. You listen carefully. (+{xp} Tactics xp)",
                $"An old soldier holds court at the end of the bar — wars, lords, mistakes made and survived. " +
                $"By closing time you understand something about ground that no book ever put clearly. (+{xp} Tactics xp)",
                $"He served three lords, outlived two of them, and has opinions about all of it. " +
                $"The things he says about positioning will stay with you longer than the drink. (+{xp} Tactics xp)",
            };
            return lines[_rng.Next(lines.Length)];
        }

        private static string OutcomeSecretMap(int tier)
        {
            int gold = tier == 1 ?  50 + _rng.Next(51)    //  50-100g
                     : tier == 2 ? 150 + _rng.Next(201)   // 150-350g
                                 : 400 + _rng.Next(501);  // 400-900g
            try { Hero.MainHero?.ChangeHeroGold(gold); } catch { }
            string[] lines =
            {
                $"A drunk merchant produces a rolled paper and calls it a treasure map. You buy it for almost nothing. " +
                $"In daylight it turns out to be a real merchant route — with margin notes about where to buy low. (+{gold} denars)",
                $"He says he found it in a dead man's boot, then says he won it at cards, then falls asleep. " +
                $"The paper he left behind describes a courier cache three streets away. You find it. (+{gold} denars)",
                $"The merchant is more drunk than map, but the figures scrawled at the margin add up to something real. " +
                $"A waymarker you recognize, a buried cache, and coin enough to make the night worthwhile. (+{gold} denars)",
            };
            return lines[_rng.Next(lines.Length)];
        }

        // ── Bad outcome implementations ───────────────────────────────────────

        private static string OutcomeCheated(int tier)
        {
            int gold = tier == 1 ?  10 + _rng.Next(31)    //  10-40g
                     : tier == 2 ?  30 + _rng.Next(91)    //  30-120g
                                 : 100 + _rng.Next(201);  // 100-300g
            gold = Math.Min(gold, Hero.MainHero?.Gold ?? 0);
            if (gold > 0) try { Hero.MainHero?.ChangeHeroGold(-gold); } catch { }
            AddMorale(-2f);
            string[] lines =
            {
                $"The cards were marked. You only figure it out when the pot is gone and the man who smiled most is leaving. " +
                $"(-{gold} denars, -2 morale)",
                $"A card game, apparently social. You lose steadily enough to suspect skill — until you notice the same crease on every ten. " +
                $"The cheat is three streets away by the time you check. (-{gold} denars, -2 morale)",
                $"The dice felt wrong, the count felt off, and by the time you trust your gut it is too late. " +
                $"Someone at this table has a profession. (-{gold} denars, -2 morale)",
            };
            return lines[_rng.Next(lines.Length)];
        }

        private static string OutcomeSpilledSecret()
        {
            int influence = 8 + _rng.Next(13); // 8-20
            try
            {
                if (Hero.MainHero?.Clan != null && Hero.MainHero.Clan.Influence >= influence)
                    Hero.MainHero.Clan.Influence -= influence;
            }
            catch { }
            string[] lines =
            {
                $"The drink loosens your tongue and you say something about a lord that should have stayed in your head. " +
                $"The room is quieter than you thought. (-{influence} influence)",
                $"You did not mean to share your plans aloud. You are reasonably sure most of them did not understand. " +
                $"Reasonably. (-{influence} influence)",
                $"A word about your clan's next move. A pause. A look. The kind of quiet that follows a mistake. " +
                $"By morning someone will have passed it on. (-{influence} influence)",
            };
            return lines[_rng.Next(lines.Length)];
        }

        private static string OutcomeOffend()
        {
            try
            {
                var notables = Settlement.CurrentSettlement?.Notables?.Where(h => h != null && h.IsAlive && h != Hero.MainHero).ToList();
                if (notables != null && notables.Count > 0)
                {
                    var notable = notables[_rng.Next(notables.Count)];
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, notable, -1, false);
                    return $"You say something that lands poorly. {notable.Name} doesn't raise their voice — " +
                           $"just looks at you the way people look at things they have decided to remember. " +
                           $"(Relation with {notable.Name}: -1)";
                }
            }
            catch { }
            ChangeRelWithRandomHero(-1);
            return "You say the wrong thing to the wrong person. They leave without a word. " +
                   "You suspect this will be remembered. (Relation: -1)";
        }

        private static string OutcomeBrawl()
        {
            try
            {
                // Deal minor damage to player
                var agent = Hero.MainHero;
                if (agent != null)
                {
                    // Add crime and a morale hit — actual HP can't easily be modified out of mission
                    AddMorale(-4f);
                    ChangeCrime(5f);
                }
            }
            catch { }
            string[] lines =
            {
                "A man takes exception to your elbow. Then your face. You give back what you can. " +
                "You leave with a split lip and a fine for disturbing the peace. (-4 morale, +5 crime)",
                "Three words and someone's stool becomes a weapon. You handle yourself, mostly. " +
                "The watch arrives and everyone pays for the trouble. (-4 morale, +5 crime)",
                "The brawl is short and final — over a comment nobody fully remembers making. " +
                "The tavernkeeper's face says this is a regular occurrence. (-4 morale, +5 crime)",
            };
            return lines[_rng.Next(lines.Length)];
        }

        private static string OutcomeLordEncounterBad()
        {
            try
            {
                var lord = GetLordInSettlement();
                if (lord == null)
                {
                    // Pick any lord
                    lord = Hero.AllAliveHeroes
                        .Where(h => h.IsLord && h.IsAlive && h != Hero.MainHero)
                        .OrderBy(_ => _rng.Next())
                        .FirstOrDefault();
                }
                if (lord != null)
                {
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, lord, -2, false);
                    return $"{lord.Name} enters the common room at exactly the wrong moment — " +
                           $"when you are at your least dignified. They say nothing. They don't have to. " +
                           $"(Relation with {lord.Name}: -2)";
                }
            }
            catch { }
            return "Someone important witnesses something they should not have. The look they give you will not be forgotten.";
        }

        private static string OutcomePuking()
        {
            AddMorale(-5f);
            string[] lines =
            {
                "The cup wins. You spend an unpleasant quarter-hour in the alley and return to the table with significantly less dignity. (-5 morale)",
                "Your body registers a formal objection to the evening's proceedings. The alley wall bears the evidence. (-5 morale)",
                "The drinks disagree with you in a loud and public way. The tavernkeeper points to the door. The alley, at least, is private. (-5 morale)",
            };
            return lines[_rng.Next(lines.Length)];
        }

        private static string OutcomeJail()
        {
            try
            {
                ChangeCrime(15f);
                // Use a brief sober-up to simulate time in a cell
                _soberHoursTotal   = 8f + _rng.Next(5); // 8-12 hours
                _soberHoursElapsed = 0f;
                _soberDone         = false;

                // Switch to the sober-up menu after the result is shown briefly
                // We defer this via the deferred inquiry so the result menu can appear first
                MageKnowledge._deferredInquiry = () =>
                {
                    try
                    {
                        MBTextManager.SetTextVariable("TAVERN_SOBER_TEXT",
                            "A cell in the garrison tower. Straw, iron, and regret. The watch picked you up sometime after the third hour. " +
                            "They will let you out when they feel like it.");
                        GameMenu.SwitchToMenu("ldm_tavern_sober_up");
                    }
                    catch { }
                };
            }
            catch { }
            return "The watch arrives. You are not in a position to argue. " +
                   "The cell is cold and the night is long. (+15 crime — you will be held until morning)";
        }
    }
}
