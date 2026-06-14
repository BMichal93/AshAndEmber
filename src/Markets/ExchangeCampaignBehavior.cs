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
            "A lord's factor is in the hall today. He hasn't bought anything yet.",
            "Someone just sold a very large position very quietly. The board barely twitched.",
            "The south road has been closed for three days. Nobody will say why.",
            "A guild embargo is rumoured somewhere along the supply chain. Nobody has seen the writ.",
            "The scales in the corner are wrong and everyone on the floor knows it.",
            "Three sellers left the hall together an hour ago. Either coincidence, or a signal.",
            "Word from the north: the garrison doubled its ration order this month.",
            "A factor known for steady hands just went red. That does not happen often.",
            "A courier passed through before dawn. The factors who saw him have been quiet ever since.",
            "Two lords are marching toward each other. The roads between them carry something valuable.",
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
                        catch { }
                    },
                    null, "", false), true);
            }
            catch { }
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
                        catch { }
                    },
                    null, "", false), true);
            }
            catch { }
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
                        catch { }
                    },
                    null, "", false), true);
            }
            catch { }
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
                        catch { }
                    },
                    null, "", false), true);
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
