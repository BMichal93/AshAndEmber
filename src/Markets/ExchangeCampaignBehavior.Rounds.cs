// =============================================================================
// ASH AND EMBER — ExchangeCampaignBehavior.Rounds.cs
// The round loop, choices, selling, and crashes.
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
        // ── Round loop ────────────────────────────────────────────────────────
        private static void ShowRound()
        {
            if (_ventureActive == 0) return;

            int    trade     = TradeSkill();
            int    completed = _ventureRoundsLimit - _ventureRoundsLeft;
            int    value     = SpeculationMath.Payout(_ventureStake, _ventureMultiplier);
            int    profit    = value - _ventureStake;
            int    sMin      = SpeculationMath.DeltaMin(_ventureVolatility, false, _ventureMood);
            int    sMax      = SpeculationMath.DeltaMax(_ventureVolatility, false, _ventureMood);
            int    hMin      = SpeculationMath.DeltaMin(_ventureVolatility, true,  _ventureMood);
            int    hMax      = SpeculationMath.DeltaMax(_ventureVolatility, true,  _ventureMood);
            int    sCrash    = (int)(SpeculationMath.CrashChance(_ventureVolatility, completed, trade, false) * 100f);
            int    hCrash    = (int)(SpeculationMath.CrashChance(_ventureVolatility, completed, trade, true)  * 100f);
            bool   lastRound = _ventureRoundsLeft == 1;
            string murmur    = Murmurs[_rng.Next(Murmurs.Length)];

            // Middle section: rules hint on round 1, running history on subsequent rounds.
            string midSection;
            if (_roundHistory.Count == 0)
            {
                int salv = SpeculationMath.SalvagePercent(trade);
                midSection =
                      $"Position opens at 100% — sell any time to close at position × stake.\n"
                    + $"A crash wipes the position (Trade salvages {salv}% of stake).\n"
                    + $"Crash risk climbs every round you stay in.\n"
                    + $"Run out of rounds and brokers force a sale at 90% of book.\n";
            }
            else
            {
                midSection = "History:\n  " + string.Join("\n  ", _roundHistory) + "\n";
            }

            string finalWarning = lastRound
                ? "\n⚠  FINAL ROUND — brokers force a sale at 90% of book after this.\n"
                : "";

            string profitLabel = profit >= 0 ? $"+{profit}g" : $"{profit}g";
            string body =
                  $"Stake: {_ventureStake}g  |  Position: {_ventureMultiplier}%  =  {value}g  ({profitLabel} vs stake)"
                + $"  |  Market: {MoodLabel(_ventureMood)}\n\n"
                + midSection
                + $"\n\"{murmur}\"\n"
                + finalWarning;

            string sellLabel = profit >= 0
                ? $"SELL — take {value}g  (profit +{profit}g)"
                : $"SELL — cut losses, take {value}g  (loss {profit}g)";

            var options = new List<InquiryElement>
            {
                new InquiryElement("sell",
                    sellLabel, null, true,
                    $"Close the position now at {_ventureMultiplier}%. "
                    + (profit >= 0 ? $"You made {profit}g." : $"You lost {-profit}g.")),

                new InquiryElement("steady",
                    $"HOLD STEADY  [{Pct(sMin)} to {Pct(sMax)}, crash {sCrash}%]",
                    null, true,
                    $"Let the position ride. Modest swing either way — the exact move is hidden until you commit. "
                    + $"Crash risk this round: {sCrash}%."),

                new InquiryElement("hard",
                    $"SPECULATE HARD  [{Pct(hMin)} to {Pct(hMax)}, crash {hCrash}%]",
                    null, true,
                    $"Push the position aggressively. Wide swing either way — and the extra noise raises the crash risk. "
                    + $"Crash risk this round: {hCrash}%."),
            };

            try
            {
                MBInformationManager.ShowMultiSelectionInquiry(
                    new MultiSelectionInquiryData(
                        $"The Exchange — {_ventureCommodity}  |  Round {completed + 1} of {_ventureRoundsLimit}",
                        body,
                        options, false, 1, 1,
                        "Confirm", "Close position",
                        chosen => { try { ProcessRoundChoice(chosen?[0]?.Identifier as string ?? "sell"); } catch { } },
                        _      => { try { SellVenture(forced: false); } catch { } }),
                    true);
            }
            catch { }
        }

        private static void ProcessRoundChoice(string choiceId)
        {
            if (_ventureActive == 0) return;

            if (choiceId == "sell")
            {
                SellVenture(forced: false);
                return;
            }

            bool   aggressive = choiceId == "hard";
            int    trade      = TradeSkill();
            int    completed  = _ventureRoundsLimit - _ventureRoundsLeft;

            if (_rng.NextDouble() < SpeculationMath.CrashChance(_ventureVolatility, completed, trade, aggressive))
            {
                CrashVenture(completed + 1, aggressive);
                return;
            }

            int min   = SpeculationMath.DeltaMin(_ventureVolatility, aggressive, _ventureMood);
            int max   = SpeculationMath.DeltaMax(_ventureVolatility, aggressive, _ventureMood);
            int delta = min + _rng.Next(max - min + 1);
            _ventureMultiplier = SpeculationMath.ApplyDelta(_ventureMultiplier, delta);

            // Record to history — shown in the next round's body instead of a transient toast.
            string action = aggressive ? "HARD" : "STEADY";
            _roundHistory.Add($"R{completed + 1}  {action}  {Pct(delta)}  →  {_ventureMultiplier}%");

            _ventureRoundsLeft--;
            if (_ventureRoundsLeft <= 0) { SellVenture(forced: true); return; }
            if (!MaybeFireExchangeEvent())
                ShowRound();
        }

        private static void SellVenture(bool forced)
        {
            try
            {
                int    payout = forced
                    ? SpeculationMath.ForcedSalePayout(_ventureStake, _ventureMultiplier)
                    : SpeculationMath.Payout(_ventureStake, _ventureMultiplier);
                int    profit = payout - _ventureStake;
                string name   = _ventureCommodity;
                GiveGold(payout);
                AddTradeXp(SpeculationMath.TradeXp(_ventureStake, payout));
                ClearVenture();

                string line;
                Color  col;
                if (forced)
                {
                    line = $"Brokers closed the {name} position at 90% of book — {payout}g "
                         + (profit >= 0 ? $"(profit +{profit}g)." : $"(loss {profit}g).");
                    col  = new Color(0.65f, 0.60f, 0.40f);
                }
                else if (profit >= 0)
                {
                    line = $"Sold the {name} position — {payout}g  (profit +{profit}g).";
                    col  = new Color(0.45f, 0.75f, 0.45f);
                }
                else
                {
                    line = $"Sold the {name} position — {payout}g  (loss {-profit}g).";
                    col  = new Color(0.75f, 0.55f, 0.35f);
                }
                InformationManager.DisplayMessage(new InformationMessage(line, col));
            }
            catch { }
        }

        private static void CrashVenture(int roundNumber, bool wasAggressive)
        {
            try
            {
                int    salvage = SpeculationMath.CrashSalvage(_ventureStake, TradeSkill());
                string name    = _ventureCommodity;
                string push    = wasAggressive ? "pushing hard" : "holding";
                GiveGold(salvage);
                AddTradeXp(50);
                ClearVenture();

                string line = $"CRASH (R{roundNumber}, {push}) — {name} found no buyers. "
                            + (salvage > 0 ? $"Salvage: {salvage}g." : "Nothing recovered.");
                InformationManager.DisplayMessage(new InformationMessage(line, new Color(0.80f, 0.30f, 0.25f)));
            }
            catch { }
        }
    }
}
