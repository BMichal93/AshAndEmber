// =============================================================================
// ASH AND EMBER — Markets/ExchangeCampaignBehavior.cs
// The Goods Exchange: a push-your-luck commodity speculation window in towns.
// Deliberately mundane — no magic, no schemes. Coin in, coin out.
//
// UI flow:
//   Town menu → "Visit the goods exchange"
//     → "ldm_exchange_menu" game menu (one board per volatility class)
//       → MultiSelectionInquiry for stake (500 / 2000 / 5000)  [Buy In / Back]
//           → Round loop popup (repeating)
//               · Round 1 body: brief rules reminder (no prior history yet)
//               · Round 2+ body: history of every completed round
//               · Options: SELL / HOLD STEADY / SPECULATE HARD
//               · Cancel ("Close position"): same as SELL
//
// The confirmation screen that existed before the round loop has been removed —
// the first-round rules summary replaces it without adding an extra screen.
//
// The stake is paid at commit. Interrupted ventures (save/load mid-position)
// are force-liquidated at 90% of book on next session launch.
// All numeric rules live in SpeculationMath (pure, covered by PureLogicTests).
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
    public class ExchangeCampaignBehavior : CampaignBehaviorBase
    {
        private static readonly Random _rng = new Random();

        // ── Per-town cooldown after a venture ends (days) ─────────────────────
        private static readonly Dictionary<string, int> _townCooldowns = new Dictionary<string, int>();

        // ── Open venture (persisted across save/load) ─────────────────────────
        private static int    _ventureActive;
        private static int    _ventureStake;
        private static int    _ventureMultiplier;
        private static int    _ventureRoundsLeft;
        private static int    _ventureRoundsLimit;
        private static int    _ventureMood;
        private static int    _ventureVolatility;
        private static string _ventureTownId    = "";
        private static string _ventureCommodity = "";

        // In-memory round history — not persisted (interrupted ventures are
        // auto-liquidated, so history never needs to survive a reload).
        private static readonly List<string> _roundHistory = new List<string>();

        // Today's boards — one concrete good per volatility class, rolled on entry.
        private static readonly string[] _offer = { "Grain", "Wine", "Velvet" };

        // Corrected to actual Bannerlord trade goods.
        private static readonly string[] StapleGoods  = { "Grain", "Butter", "Cheese", "Fish", "Salt", "Clay" };
        private static readonly string[] CraftedGoods = { "Beer", "Wine", "Oil", "Tools", "Leather", "Pottery", "Linen" };
        private static readonly string[] LuxuryGoods  = { "Velvet", "Jewelry", "Fur", "Silver", "Date Fruit" };

        private static readonly string[] ClassNames      = { "staple goods", "crafted goods", "luxury goods" };
        private static readonly string[] ClassOptionIds  = { "staple", "crafted", "luxury" };
        private static readonly string[] ClassRiskDescs  =
        {
            "safe — modest swings, low crash risk",
            "moderate — follows supply routes and war",
            "volatile — wide swings, high risk and reward",
        };

        // Market murmurs — flavour only, no mechanical meaning. Shown once per round.
        private static readonly string[] Murmurs =
        {
            "A caravan from the east arrived early — its master is selling in a hurry.",
            "Dock hands whisper of a convoy lost to raiders on the coast road.",
            "The harvest tallies are in, and the criers can't agree on what they say.",
            "A guild buyer is quietly cornering stock at the edge of town.",
            "War talk in the keep — the quartermasters are counting wagons.",
            "A warehouse fire two towns over has the factors jittery.",
            "Pilgrims crowd the square; everything sells dearer this week.",
            "A rival exchange posted prices nobody here believes.",
            "The toll on the north road doubled overnight.",
            "An old factor shakes his head: 'I've seen this before.'",
            "Two merchants argued over a load of goods until one of them left. Nobody is sure who won.",
            "A lord's steward has been buying quietly all morning. He won't say for whom.",
            "The river crossing is flooded — the northern shipment is two weeks late.",
            "Smoke on the horizon. The factors won't say which road.",
            "A ship put in last night with half its cargo missing. The captain is not answering questions.",
            "The miller's guild is meeting in a back room. That usually means prices go up.",
            "Three caravans passed through in the last hour. Something is moving fast.",
            "A courier from the capital came through with sealed letters. Prices twitched immediately.",
            "The garrison doubled the road levy this morning. The traders are furious.",
            "Someone bought every last sack of the stuff an hour ago. Now everyone wants it.",
            "The factors are meeting in the back — the chalk tallies don't match the ledgers.",
            "A veteran merchant taps the board twice with one finger. Old sign. It means wait.",
            "The eastern road is clear, but nobody is using it. That's its own kind of warning.",
            "Rumour has a new mine opening three kingdoms east. Or it closed. Nobody is certain.",
            "The day's first sale went badly. The floor is still deciding what that means.",
        };

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore store)
        {
            try
            {
                var cdKeys = _townCooldowns.Keys.ToList();
                var cdVals = _townCooldowns.Values.ToList();
                store.SyncData("LDX_CdKeys", ref cdKeys);
                store.SyncData("LDX_CdVals", ref cdVals);
                if (cdKeys != null && cdVals != null && cdKeys.Count == cdVals.Count)
                {
                    _townCooldowns.Clear();
                    for (int i = 0; i < cdKeys.Count; i++)
                        _townCooldowns[cdKeys[i]] = cdVals[i];
                }

                store.SyncData("LDX_Active",      ref _ventureActive);
                store.SyncData("LDX_Stake",        ref _ventureStake);
                store.SyncData("LDX_Mult",         ref _ventureMultiplier);
                store.SyncData("LDX_RoundsLeft",   ref _ventureRoundsLeft);
                store.SyncData("LDX_RoundsLimit",  ref _ventureRoundsLimit);
                store.SyncData("LDX_Mood",         ref _ventureMood);
                store.SyncData("LDX_Vol",          ref _ventureVolatility);
                store.SyncData("LDX_TownId",       ref _ventureTownId);
                store.SyncData("LDX_Commodity",    ref _ventureCommodity);
            }
            catch { }
        }

        internal static void ResetState()
        {
            _townCooldowns.Clear();
            ClearVenture();
        }

        private static void ClearVenture()
        {
            _ventureActive      = 0;
            _ventureStake       = 0;
            _ventureMultiplier  = 0;
            _ventureRoundsLeft  = 0;
            _ventureRoundsLimit = 0;
            _ventureMood        = 0;
            _ventureVolatility  = 0;
            _ventureTownId      = "";
            _ventureCommodity   = "";
            _roundHistory.Clear();
        }

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
                MageKnowledge._deferredInquiry = () =>
                {
                    try
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"While you were away your broker closed the {name} position at 90% of book — {payout} denars returned.",
                            new Color(0.65f, 0.60f, 0.40f)));
                    }
                    catch { }
                };
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

        // ── Helpers ───────────────────────────────────────────────────────────
        private static int TradeSkill()
        {
            try { return Hero.MainHero?.GetSkillValue(DefaultSkills.Trade) ?? 0; }
            catch { return 0; }
        }

        private static int CurrentMood()
        {
            try { return SpeculationMath.MoodShift(Settlement.CurrentSettlement?.Town?.Prosperity ?? 3000f); }
            catch { return 0; }
        }

        private static string MoodLabel(int mood)
            => mood > 0 ? "Booming" : mood < 0 ? "Depressed" : "Steady";

        private static string Pct(int v) => v >= 0 ? $"+{v}%" : $"{v}%";

        private static void GiveGold(int amount)
        {
            if (amount <= 0) return;
            try { if (Hero.MainHero != null) Hero.MainHero.Gold += amount; } catch { }
        }

        private static void AddTradeXp(int xp)
        {
            if (xp <= 0) return;
            try { Hero.MainHero?.HeroDeveloper?.AddSkillXp(DefaultSkills.Trade, xp); } catch { }
        }
    }
}
