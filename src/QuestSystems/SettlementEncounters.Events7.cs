// =============================================================================
// ASH AND EMBER — SettlementEncounters.Events7.cs
// The Merchant of Endings — a mage-gated tavern wager.
// Partial of SettlementEncounters (shared state lives in SettlementEncounters.cs).
//
// ┌────────────────────────────────┬──────────────────────┬──────────────────┐
// │ Event                          │ Trigger              │ Gate             │
// ├────────────────────────────────┼──────────────────────┼──────────────────┤
// │ The Merchant of Endings        │ Enter city           │ Mage             │
// └────────────────────────────────┴──────────────────────┴──────────────────┘
//
// Outcomes:
//   A. Refuse  → he vanishes without a trace.
//   B. Wealth  → 50 % win 20 000 gold / 50 % lose magic.
//   C. Glory   → 50 % win 150 renown + 250 influence / 50 % lose magic.
//   D. Power   → 50 % win up to 4 unknown Codex powers / 50 % lose magic.
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
        // THE MERCHANT OF ENDINGS
        //   City enter — mage-gated — 90-day cooldown.
        //   A stranger in the tavern offers a coin-flip for what you value most.
        //   Win: wealth, glory, or four gifts of the Inner Fire.
        //   Lose: the fire itself is taken.
        // ═══════════════════════════════════════════════════════════════════

        private static void EC_MerchantOfEndings(Settlement s)
        {
            _merchantOfEndingsCooldown = 90;

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "✦  The Merchant of Endings",
                "He is already sitting at your table when you enter the tavern — as though he knew you were " +
                "coming before you did. His cloak is the colour of old ash. His eyes hold the flat patience of " +
                "a man who has made this offer before and is in no hurry about the answer. He sets a single coin " +
                "on the wood between you, face-down, and speaks without introduction: 'One flip. One coin. " +
                "For what you value most. You name your stake. I name the other side of it.'",
                new List<InquiryElement>
                {
                    new InquiryElement("refuse",
                        "Refuse. You have not come this far to wager with strangers.",
                        null, true,
                        "He will not press the matter."),
                    new InquiryElement("wealth",
                        "Agree — play for wealth.",
                        null, true,
                        "Twenty thousand coin, against what you carry inside you."),
                    new InquiryElement("glory",
                        "Agree — play for glory.",
                        null, true,
                        "Renown and standing in the world, against what defines you. (+150 renown, +250 influence)"),
                    new InquiryElement("power",
                        "Agree — play for power.",
                        null, true,
                        "Four gifts of the Inner Fire, against the fire itself."),
                },
                false, 1, 1, "Decide", "",
                chosen =>
                {
                    switch (chosen?[0]?.Identifier as string)
                    {
                        case "refuse":
                            Msg("You tell him no. He picks up the coin without looking at it and stands. " +
                                "By the time you follow the movement of his cloak with your eyes he is no longer " +
                                "in the room. No door opened. No one saw him go.", DimColor);
                            break;

                        case "wealth":
                            FlipForMerchant(
                                winMsg:
                                    "He flips the coin. It rises slowly, turns once, and falls heads. " +
                                    "He pushes a heavy satchel under the table with his foot without standing. " +
                                    "'Twenty thousand,' he says, without warmth or ceremony. 'As agreed.' " +
                                    "He is gone before the coin finishes settling. The satchel is real. The weight is right.",
                                winColor: GoldColor,
                                winAction: () => ChangeGold(20000),
                                lossMsg:
                                    "He flips the coin. It lands tails. " +
                                    "He extends one hand across the table, palm up, and something leaves you " +
                                    "that you feel before you can name it. The fire dims and then it is simply gone — " +
                                    "not extinguished but taken, as cleanly as a debt settled. " +
                                    "You sit in the tavern a long time after he leaves. The room is colder than it was.");
                            break;

                        case "glory":
                            FlipForMerchant(
                                winMsg:
                                    "He flips the coin. It comes up heads. He nods once — not a bow, an acknowledgement " +
                                    "between parties who have concluded their business — and leaves a sealed letter " +
                                    "on the table. Inside: a testimony in a hand you do not recognise, addressed to " +
                                    "no one in particular, describing your name and deeds with an authority that implies " +
                                    "the author was present at each of them. It travels further than you expect.",
                                winColor: GoodColor,
                                winAction: () =>
                                {
                                    try
                                    {
                                        if (Hero.MainHero?.Clan != null)
                                        {
                                            Hero.MainHero.Clan.Renown    += 150f;
                                            Hero.MainHero.Clan.Influence += 250f;
                                            Msg("(+150 renown, +250 influence)", GoodColor);
                                        }
                                    }
                                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                                },
                                lossMsg:
                                    "He flips the coin. It lands tails. " +
                                    "He extends one hand across the table, palm up, and something leaves you " +
                                    "that you feel before you can name it. The fire dims and then it is simply gone — " +
                                    "not extinguished but taken, as cleanly as a debt settled. " +
                                    "You sit in the tavern a long time after he leaves. The other patrons do not meet your eye.");
                            break;

                        case "power":
                            FlipForMerchant(
                                winMsg:
                                    "He flips the coin. It rises higher than it should, hangs for a long moment, " +
                                    "and falls heads. He sets four fingers briefly on the table — a count — then lifts them. " +
                                    "Something arrives in you that was not there before. Four things, settling " +
                                    "into the fire like wood laid on a ready hearth. He says nothing. He leaves. " +
                                    "The fire is louder in you than it was.",
                                winColor: FireColor,
                                winAction: GrantMerchantTalents,
                                lossMsg:
                                    "He flips the coin. It hangs — it actually hangs, suspended, for one long moment — " +
                                    "and then falls tails. He extends one hand across the table, palm up, and everything " +
                                    "that made the fire yours leaves before you can reach for it. " +
                                    "You can feel the shape of the absence. He is gone before the coin stops spinning.");
                            break;
                    }
                }, null, "", false), false, true);
        }

        private static void FlipForMerchant(string winMsg, Color winColor, Action winAction, string lossMsg)
        {
            if (_rng.NextDouble() < 0.5)
            {
                winAction?.Invoke();
                Msg(winMsg, winColor);
            }
            else
            {
                try { MageKnowledge.SetMage(false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                Msg(lossMsg, BadColor);
            }
        }

        // The merchant's winnings are paid in the LIVING craft — up to four Codex
        // powers (elements and disciplines) the player does not yet hold. The old
        // spell/enchantment talents are retired and would pay in ash.
        private static void GrantMerchantTalents()
        {
            var learned = new List<string>();
            for (int i = 0; i < 4; i++)
            {
                if (!MagicLearning.TryGrantRandomUnknown(_rng, out string name)) break;
                learned.Add(name);
            }

            if (learned.Count > 0)
                Msg("The coin falls heads, and the merchant pays in full — you know " +
                    string.Join(", ", learned).ToUpperInvariant() + " now, unearned and unasked.", FireColor);
            else
                Msg("The fire surges inward and finds nothing new to carry — " +
                    "you already hold what was offered.", FireColor);
        }
    }
}
