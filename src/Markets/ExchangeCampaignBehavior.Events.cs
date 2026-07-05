// =============================================================================
// ASH AND EMBER — ExchangeCampaignBehavior.Events.cs
// The four between-round exchange events.
// Partial of ExchangeCampaignBehavior (shared static state lives in ExchangeCampaignBehavior.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class ExchangeCampaignBehavior
    {
        // ── Exchange events (fire between rounds 2..N-1, ~30% chance) ─────────
        private static bool MaybeFireExchangeEvent()
        {
            // Skip the very first completed round (history is empty) and the last.
            if (_ventureRoundsLeft <= 1) return false;
            int completed = _ventureRoundsLimit - _ventureRoundsLeft;
            if (completed < 1) return false;
            if (_rng.NextDouble() >= 0.30) return false;

            double r = _rng.NextDouble();
            if (r < 0.25)
                FireInsiderTipEvent();
            else if (r < 0.50)
                FireMarketPanicEvent();
            else if (r < 0.75)
                FireEmbargoScareEvent();
            else
                FireKingsFactorEvent();
            return true;
        }

        private static void FireInsiderTipEvent()
        {
            try
            {
                string name   = _ventureCommodity;
                int    bonusN = 15;
                int    bonusM = 25;

                var options = new List<InquiryElement>
                {
                    new InquiryElement("take",
                        $"Act on the tip — position +{bonusN}%", null, true,
                        "Buy into the move. If the man is right, you gain. He gets a cut, whatever happens."),
                    new InquiryElement("ignore",
                        "Ignore it. Blind luck is cleaner than bought information.", null, true,
                        "Continue the normal round. No gain, no taint."),
                };
                if (MageKnowledge.IsMage)
                    options.Add(new InquiryElement("read",
                        $"Read the speaker's intent ({bonusM}% if sincere, nothing if lying)", null, true,
                        "Let the Inner Fire taste his intentions. A sincere tip pays well; a false one costs you nothing."));

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "The Exchange — Insider Tip",
                    $"A hooded figure slides alongside your position at the {name} board. He speaks quickly, quietly: " +
                    $"\"Load heavy. The western convoy is two days out and they are not carrying {name} — they are carrying debt. " +
                    "Prices move when word reaches the floor.\" He does not wait for thanks.",
                    options, false, 1, 1, "Decide", "Decide",
                    chosen =>
                    {
                        try
                        {
                            string pick = chosen?[0]?.Identifier as string ?? "ignore";
                            switch (pick)
                            {
                                case "take":
                                    _ventureMultiplier = SpeculationMath.ApplyDelta(_ventureMultiplier, bonusN);
                                    _roundHistory.Add($"Tip  +{bonusN}%  →  {_ventureMultiplier}%");
                                    MBInformationManager.AddQuickInformation(new TextObject(
                                        $"The {name} board twitches upward. Your position gains {bonusN}%."));
                                    break;
                                case "read":
                                    if (_rng.NextDouble() < 0.65)
                                    {
                                        _ventureMultiplier = SpeculationMath.ApplyDelta(_ventureMultiplier, bonusM);
                                        _roundHistory.Add($"Tip (verified)  +{bonusM}%  →  {_ventureMultiplier}%");
                                        MBInformationManager.AddQuickInformation(new TextObject(
                                            $"The Inner Fire reads no deceit in him. The {name} position gains {bonusM}%."));
                                    }
                                    else
                                    {
                                        _roundHistory.Add("Tip (false) — nothing gained.");
                                        MBInformationManager.AddQuickInformation(new TextObject(
                                            "The Inner Fire finds cold calculation behind the offer — a misdirection. You wave him off."));
                                    }
                                    break;
                                default: // ignore
                                    _roundHistory.Add("Tip — ignored.");
                                    MBInformationManager.AddQuickInformation(new TextObject(
                                        "You let him go. The board moves without you."));
                                    break;
                            }
                            ShowRound();
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    },
                    null, "", false), true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void FireMarketPanicEvent()
        {
            try
            {
                string name       = _ventureCommodity;
                int    value      = SpeculationMath.Payout(_ventureStake, _ventureMultiplier);
                int    dumpPayout = value * 70 / 100;
                int    dumpProfit = dumpPayout - _ventureStake;
                int    completed  = _ventureRoundsLimit - _ventureRoundsLeft;
                int    trade      = TradeSkill();

                var options = new List<InquiryElement>
                {
                    new InquiryElement("dump",
                        $"Dump it — sell at 70% of book ({dumpPayout}g)", null, true,
                        "Take the hit now. Better than nothing if the crash is real."),
                    new InquiryElement("hold",
                        "Hold your nerve — the panic is the opportunity", null, true,
                        "Stay in. If you are right, the position recovers with a bonus. If wrong, the crash takes everything."),
                    new InquiryElement("normal",
                        "This is noise — take the normal round", null, true,
                        "Ignore the floor and play the standard round. Crash risk is unaffected."),
                };

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    $"☌  Market Panic — {name}",
                    $"A shout from the far end of the hall: a major buyer has cancelled. The {name} board erupts — " +
                    "chalk flying, men shouting, half the floor running for the door. " +
                    "Your broker grabs your arm: \"Tell me now, or I decide for you.\"",
                    options, false, 1, 1, "Decide", "Decide",
                    chosen =>
                    {
                        try
                        {
                            string pick = chosen?[0]?.Identifier as string ?? "normal";
                            switch (pick)
                            {
                                case "dump":
                                {
                                    GiveGold(dumpPayout);
                                    AddTradeXp(SpeculationMath.TradeXp(_ventureStake, dumpPayout));
                                    string dLine = dumpProfit >= 0
                                        ? $"Dumped the {name} position for {dumpPayout}g  (profit +{dumpProfit}g)."
                                        : $"Dumped the {name} position for {dumpPayout}g  (loss {-dumpProfit}g).";
                                    ClearVenture();
                                    InformationManager.DisplayMessage(new InformationMessage(dLine,
                                        dumpProfit >= 0 ? new Color(0.45f, 0.75f, 0.45f) : new Color(0.75f, 0.55f, 0.35f)));
                                    break;
                                }
                                case "hold":
                                {
                                    double crashThresh = Math.Min(0.85,
                                        SpeculationMath.CrashChance(_ventureVolatility, completed, trade, false) * 2.0);
                                    if (_rng.NextDouble() < crashThresh)
                                    {
                                        int salvage = SpeculationMath.CrashSalvage(_ventureStake, trade);
                                        GiveGold(salvage);
                                        AddTradeXp(50);
                                        string cLine = $"CRASH (Panic) — {name} went to nothing. "
                                            + (salvage > 0 ? $"Salvage: {salvage}g." : "Nothing recovered.");
                                        ClearVenture();
                                        InformationManager.DisplayMessage(new InformationMessage(cLine, new Color(0.80f, 0.30f, 0.25f)));
                                    }
                                    else
                                    {
                                        int rally = 20;
                                        _ventureMultiplier = SpeculationMath.ApplyDelta(_ventureMultiplier, rally);
                                        _roundHistory.Add($"Panic (held)  +{rally}%  →  {_ventureMultiplier}%");
                                        MBInformationManager.AddQuickInformation(new TextObject(
                                            $"The panic burns itself out. Your {name} position rallies {rally}% — the weak hands sold into your arms."));
                                        ShowRound();
                                    }
                                    break;
                                }
                                default: // normal
                                    _roundHistory.Add("Panic — held nerve.");
                                    MBInformationManager.AddQuickInformation(new TextObject(
                                        "You watch the floor scramble and stay put. The chalk settles. The round plays normally."));
                                    ShowRound();
                                    break;
                            }
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    },
                    null, "", false), true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void FireEmbargoScareEvent()
        {
            try
            {
                string name       = _ventureCommodity;
                int    value      = SpeculationMath.Payout(_ventureStake, _ventureMultiplier);
                int    dumpPayout = value * 85 / 100;
                int    dumpProfit = dumpPayout - _ventureStake;

                var options = new List<InquiryElement>
                {
                    new InquiryElement("dump",
                        $"Liquidate quietly at 85% of book ({dumpPayout}g)", null, true,
                        "Take what the board will give before the word spreads further."),
                    new InquiryElement("ride",
                        "Ride it out — rumours are cheap", null, true,
                        "Embargoes seldom stick. Uncertainty shaves a few points, but the round plays on."),
                };
                if (MageKnowledge.IsMage)
                    options.Add(new InquiryElement("read",
                        "Read the factor spreading the news", null, true,
                        "Let the Inner Fire taste his intent. Manufactured fear means the board may recover; a real embargo means cut and run."));

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    $"⚠  Embargo Scare — {name}",
                    $"A guild crier nails a writ to the exchange door. It speaks of an embargo on {name} — " +
                    "insufficient quality, undeclared origin, or a lord's grudge dressed as regulation. " +
                    "The hall erupts in low voices. Your broker catches your eye across the floor and lifts his hands.",
                    options, false, 1, 1, "Decide", "Decide",
                    chosen =>
                    {
                        try
                        {
                            string pick = chosen?[0]?.Identifier as string ?? "ride";
                            switch (pick)
                            {
                                case "dump":
                                {
                                    GiveGold(dumpPayout);
                                    AddTradeXp(SpeculationMath.TradeXp(_ventureStake, dumpPayout));
                                    string dLine = dumpProfit >= 0
                                        ? $"Slipped the {name} position out at 85% of book — {dumpPayout}g  (profit +{dumpProfit}g)."
                                        : $"Slipped the {name} position out at 85% of book — {dumpPayout}g  (loss {-dumpProfit}g).";
                                    ClearVenture();
                                    InformationManager.DisplayMessage(new InformationMessage(dLine,
                                        dumpProfit >= 0 ? new Color(0.45f, 0.75f, 0.45f) : new Color(0.75f, 0.55f, 0.35f)));
                                    break;
                                }
                                case "read":
                                {
                                    if (_rng.NextDouble() < 0.55)
                                    {
                                        int bonus = 8;
                                        _ventureMultiplier = SpeculationMath.ApplyDelta(_ventureMultiplier, bonus);
                                        _roundHistory.Add($"Embargo (false)  +{bonus}%  →  {_ventureMultiplier}%");
                                        MBInformationManager.AddQuickInformation(new TextObject(
                                            $"The Inner Fire finds theatre in him, not conviction. The embargo writ is leverage, not law. " +
                                            $"The {name} board ticks upward as the short sellers close. +{bonus}%"));
                                        ShowRound();
                                    }
                                    else
                                    {
                                        int exitPayout = value * 90 / 100;
                                        int exitProfit = exitPayout - _ventureStake;
                                        GiveGold(exitPayout);
                                        AddTradeXp(SpeculationMath.TradeXp(_ventureStake, exitPayout));
                                        string eLine = exitProfit >= 0
                                            ? $"The embargo is real. Cut the {name} position at 90% — {exitPayout}g  (+{exitProfit}g)."
                                            : $"The embargo is real. Cut the {name} position at 90% — {exitPayout}g  ({exitProfit}g).";
                                        ClearVenture();
                                        InformationManager.DisplayMessage(new InformationMessage(eLine,
                                            exitProfit >= 0 ? new Color(0.45f, 0.75f, 0.45f) : new Color(0.75f, 0.55f, 0.35f)));
                                    }
                                    break;
                                }
                                default: // ride
                                {
                                    int discount = 5;
                                    _ventureMultiplier = SpeculationMath.ApplyDelta(_ventureMultiplier, -discount);
                                    _roundHistory.Add($"Embargo scare  -{discount}%  →  {_ventureMultiplier}%");
                                    MBInformationManager.AddQuickInformation(new TextObject(
                                        $"The scare shaves {discount}% off the position — uncertainty has a price. " +
                                        "The writ disappears by midday. The round plays on."));
                                    ShowRound();
                                    break;
                                }
                            }
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    },
                    null, "", false), true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void FireKingsFactorEvent()
        {
            try
            {
                string name  = _ventureCommodity;
                int    value = SpeculationMath.Payout(_ventureStake, _ventureMultiplier);
                int    bonus = 8 + _rng.Next(8); // +8 to +15%

                var options = new List<InquiryElement>
                {
                    new InquiryElement("sell",
                        $"Sell directly to the royal factor at full book ({value}g)", null, true,
                        "The crown pays full price and asks nothing. A clean exit with no crash risk."),
                    new InquiryElement("hold",
                        $"Hold and ride the royal lift  (+{bonus}% to position before the next round)", null, true,
                        $"The factor is buying steadily, propping the floor. Your position gains {bonus}% before normal price action resumes."),
                };
                if (MageKnowledge.IsMage)
                    options.Add(new InquiryElement("read",
                        "Read the factor's royal commission", null, true,
                        "Taste the scope of the brief. A broad commission means the crown buys all day — a narrow one means they stop at noon."));

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    $"⚜  The King's Factor — {name}",
                    $"A man in a sober coat and a royal seal at his belt takes up position at the {name} board. " +
                    "He begins buying, steadily and without drama, and the chalk moves with him. " +
                    "The floor watches him the way the floor watches weather.",
                    options, false, 1, 1, "Decide", "Decide",
                    chosen =>
                    {
                        try
                        {
                            string pick = chosen?[0]?.Identifier as string ?? "hold";
                            switch (pick)
                            {
                                case "sell":
                                {
                                    GiveGold(value);
                                    AddTradeXp(SpeculationMath.TradeXp(_ventureStake, value));
                                    int profit = value - _ventureStake;
                                    string sLine = profit >= 0
                                        ? $"Sold the {name} position to the royal factor at full book — {value}g  (+{profit}g)."
                                        : $"Sold the {name} position to the royal factor at full book — {value}g  ({profit}g).";
                                    ClearVenture();
                                    InformationManager.DisplayMessage(new InformationMessage(sLine,
                                        profit >= 0 ? new Color(0.45f, 0.75f, 0.45f) : new Color(0.75f, 0.55f, 0.35f)));
                                    break;
                                }
                                case "read":
                                {
                                    if (_rng.NextDouble() < 0.60)
                                    {
                                        int broadBonus = 18;
                                        _ventureMultiplier = SpeculationMath.ApplyDelta(_ventureMultiplier, broadBonus);
                                        _roundHistory.Add($"Royal factor (broad brief)  +{broadBonus}%  →  {_ventureMultiplier}%");
                                        MBInformationManager.AddQuickInformation(new TextObject(
                                            $"The commission has no ceiling — the crown is filling a war chest and {name} is on the list. " +
                                            $"The board climbs steadily. +{broadBonus}%"));
                                        ShowRound();
                                    }
                                    else
                                    {
                                        int premiumPayout = value * 105 / 100;
                                        int premiumProfit = premiumPayout - _ventureStake;
                                        GiveGold(premiumPayout);
                                        AddTradeXp(SpeculationMath.TradeXp(_ventureStake, premiumPayout));
                                        string pLine = premiumProfit >= 0
                                            ? $"Sold into the narrow royal window at a small premium — {premiumPayout}g  (+{premiumProfit}g)."
                                            : $"Sold into the narrow royal window at a small premium — {premiumPayout}g  ({premiumProfit}g).";
                                        ClearVenture();
                                        InformationManager.DisplayMessage(new InformationMessage(pLine,
                                            premiumProfit >= 0 ? new Color(0.45f, 0.75f, 0.45f) : new Color(0.75f, 0.55f, 0.35f)));
                                    }
                                    break;
                                }
                                default: // hold
                                {
                                    _ventureMultiplier = SpeculationMath.ApplyDelta(_ventureMultiplier, bonus);
                                    _roundHistory.Add($"Royal factor  +{bonus}%  →  {_ventureMultiplier}%");
                                    MBInformationManager.AddQuickInformation(new TextObject(
                                        $"The royal factor buys steadily and the board rises with him. Position climbs {bonus}% before the next round."));
                                    ShowRound();
                                    break;
                                }
                            }
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    },
                    null, "", false), true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
