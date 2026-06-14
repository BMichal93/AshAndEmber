// =============================================================================
// ASH AND EMBER — ExchangeCampaignBehavior.Flow.cs
// Session/daily lifecycle, menus, stake selection, and commit.
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
        // ── Session launched ──────────────────────────────────────────────────
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            RegisterExchangeMenus(starter);
            ResolveInterruptedVenture();
        }

        private static void ResolveInterruptedVenture()
        {
            try
            {
                if (_ventureActive == 0) return;
                int    payout = SpeculationMath.ForcedSalePayout(_ventureStake, _ventureMultiplier);
                string name   = string.IsNullOrEmpty(_ventureCommodity) ? "goods" : _ventureCommodity;
                GiveGold(payout);
                AddTradeXp(SpeculationMath.TradeXp(_ventureStake, payout));
                ClearVenture();
                // A log line is safe to post directly at session launch — routing it
                // through _deferredInquiry would needlessly risk clobbering a quest popup
                // queued by another behavior on the same launch.
                InformationManager.DisplayMessage(new InformationMessage(
                    $"While you were away your broker closed the {name} position at 90% of book — {payout} denars returned.",
                    new Color(0.65f, 0.60f, 0.40f)));
            }
            catch { }
        }

        // ── Daily tick ────────────────────────────────────────────────────────
        private static void OnDailyTick()
        {
            try
            {
                foreach (var key in _townCooldowns.Keys.ToList())
                {
                    _townCooldowns[key]--;
                    if (_townCooldowns[key] <= 0) _townCooldowns.Remove(key);
                }
            }
            catch { }
        }

        // ── Menus ─────────────────────────────────────────────────────────────
        private static void RegisterExchangeMenus(CampaignGameStarter starter)
        {
            // ── Entry point in the town menu ──────────────────────────────────
            try
            {
                starter.AddGameMenuOption(
                    "town", "ldm_exchange_city",
                    "Visit the goods exchange",
                    args =>
                    {
                        try
                        {
                            var s = Settlement.CurrentSettlement;
                            if (s == null || !s.IsTown) return false;
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }

                            int cd = 0;
                            try { _townCooldowns.TryGetValue(s.StringId, out cd); } catch { }

                            args.IsEnabled = cd <= 0 && _ventureActive == 0;
                            if (cd > 0)
                                try { args.Tooltip = new TextObject(
                                    $"The factors remember your last venture — {cd} day{(cd != 1 ? "s" : "")} before they deal with you again."); } catch { }
                            else if (_ventureActive != 0)
                                try { args.Tooltip = new TextObject("You already hold an open position."); } catch { }
                            return true;
                        }
                        catch { return false; }
                    },
                    args =>
                    {
                        try { RollOffers(); GameMenu.SwitchToMenu("ldm_exchange_menu"); } catch { }
                    });
            }
            catch { }

            // ── Exchange floor menu ───────────────────────────────────────────
            try
            {
                starter.AddGameMenu(
                    "ldm_exchange_menu",
                    "{LDM_EXCH_HDR}",
                    args =>
                    {
                        try
                        {
                            int gold   = Hero.MainHero?.Gold ?? 0;
                            int trade  = TradeSkill();
                            int rounds = SpeculationMath.RoundsLimit(trade);
                            int salv   = SpeculationMath.SalvagePercent(trade);
                            MBTextManager.SetTextVariable("LDM_EXCH_HDR",
                                "The factors' hall hums with rumour and chalk dust.\n"
                                + $"Your purse: {gold}g  |  Market: {MoodLabel(CurrentMood())}"
                                + $"  |  Trade {trade} — {rounds} rounds per venture, salvage {salv}% on crash\n\n"
                                + "Pick a board to open a position. Each round: SELL to close, HOLD STEADY, or SPECULATE HARD.\n"
                                + "Crash risk grows the longer you stay in. Running out of rounds forces a sale at 90%.");
                        }
                        catch { }
                    });
            }
            catch { }

            // ── One option per volatility class ───────────────────────────────
            try
            {
                for (int v = 0; v < 3; v++)
                {
                    int    vol      = v;
                    string optionId = "ldm_exch_" + ClassOptionIds[vol];
                    string textKey  = "LDM_EXCH_" + ClassOptionIds[vol].ToUpperInvariant();
                    try
                    {
                        starter.AddGameMenuOption(
                            "ldm_exchange_menu",
                            optionId,
                            "{" + textKey + "}",
                            args =>
                            {
                                try
                                {
                                    int  minStake  = SpeculationMath.StakeTiers[0];
                                    bool canAfford = (Hero.MainHero?.Gold ?? 0) >= minStake;
                                    MBTextManager.SetTextVariable(textKey,
                                        $"{_offer[vol]}  —  {ClassRiskDescs[vol]}"
                                        + (canAfford ? "" : "  [not enough coin]"));
                                    try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                                    args.IsEnabled = canAfford;

                                    int mood  = CurrentMood();
                                    int sMin  = SpeculationMath.DeltaMin(vol, false, mood);
                                    int sMax  = SpeculationMath.DeltaMax(vol, false, mood);
                                    int hMin  = SpeculationMath.DeltaMin(vol, true,  mood);
                                    int hMax  = SpeculationMath.DeltaMax(vol, true,  mood);
                                    int cPct  = (int)(SpeculationMath.CrashChance(vol, 0, TradeSkill(), false) * 100f);
                                    try { args.Tooltip = new TextObject(
                                        $"Hold Steady: {Pct(sMin)} to {Pct(sMax)} per round.\n"
                                        + $"Speculate Hard: {Pct(hMin)} to {Pct(hMax)} per round.\n"
                                        + $"Starting crash risk: ~{cPct}% (climbs each round)."
                                        + (canAfford ? "" : $"\nMinimum stake: {minStake}g.")); } catch { }
                                }
                                catch { }
                                return true;
                            },
                            args => OpenStakeUI(vol));
                    }
                    catch { }
                }
            }
            catch { }

            // ── Leave ──────────────────────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption(
                    "ldm_exchange_menu", "ldm_exch_leave",
                    "Step back out into the street.",
                    args =>
                    {
                        try { args.optionLeaveType = GameMenuOption.LeaveType.Leave; } catch { }
                        return true;
                    },
                    args => { try { GameMenu.SwitchToMenu("town"); } catch { } },
                    true);
            }
            catch { }
        }

        private static void RollOffers()
        {
            try
            {
                _offer[0] = StapleGoods[_rng.Next(StapleGoods.Length)];
                _offer[1] = CraftedGoods[_rng.Next(CraftedGoods.Length)];
                _offer[2] = LuxuryGoods[_rng.Next(LuxuryGoods.Length)];
            }
            catch { }
        }

        // ── Stake selection ───────────────────────────────────────────────────
        // Pressing Buy In goes straight to CommitVenture — no confirmation screen.
        private static void OpenStakeUI(int vol)
        {
            try
            {
                int gold = Hero.MainHero?.Gold ?? 0;
                var elements = SpeculationMath.StakeTiers.Select(tier =>
                {
                    bool affordable = gold >= tier;
                    return new InquiryElement((object)tier,
                        $"{tier} denars",
                        null, affordable,
                        affordable
                            ? $"Open the {_offer[vol]} position with a {tier}g stake. Sell at any round for stake × position."
                            : $"You carry {gold}g — not enough for this stake.");
                }).ToList();

                MBInformationManager.ShowMultiSelectionInquiry(
                    new MultiSelectionInquiryData(
                        $"The Exchange — {_offer[vol]}",
                        $"How much coin goes on the {_offer[vol]} board?",
                        elements, true, 1, 1,
                        "Buy In", "Back",
                        chosen =>
                        {
                            try
                            {
                                if (chosen == null || chosen.Count == 0) return;
                                if (!(chosen[0].Identifier is int stake)) return;
                                CommitVenture(vol, stake);
                            }
                            catch { }
                        }, null),
                    true);
            }
            catch { }
        }

        private static void CommitVenture(int vol, int stake)
        {
            try
            {
                var sett = Settlement.CurrentSettlement;
                if (sett == null || Hero.MainHero == null) return;
                if (Hero.MainHero.Gold < stake)
                {
                    MBInformationManager.AddQuickInformation(
                        new TextObject("Your purse came up short — the factors wave you off."));
                    return;
                }
                if (_ventureActive != 0) return;

                try { Hero.MainHero.Gold -= stake; } catch { }

                _ventureActive      = 1;
                _ventureStake       = stake;
                _ventureMultiplier  = SpeculationMath.MultiplierStart;
                _ventureRoundsLimit = SpeculationMath.RoundsLimit(TradeSkill());
                _ventureRoundsLeft  = _ventureRoundsLimit;
                _ventureMood        = SpeculationMath.MoodShift(sett.Town?.Prosperity ?? 3000f);
                _ventureVolatility  = vol;
                _ventureTownId      = sett.StringId ?? "";
                _ventureCommodity   = _offer[vol];
                _roundHistory.Clear();

                try { _townCooldowns[_ventureTownId] = SpeculationMath.CooldownDays; } catch { }

                try { GameMenu.SwitchToMenu("town"); } catch { }
                MageKnowledge._deferredInquiry = () => { try { ShowRound(); } catch { } };
            }
            catch { }
        }
    }
}
