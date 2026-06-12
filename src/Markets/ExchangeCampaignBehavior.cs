// =============================================================================
// ASH AND EMBER — Markets/ExchangeCampaignBehavior.cs
// The Goods Exchange: a push-your-luck commodity speculation window in towns.
// Deliberately mundane — no magic, no schemes. Coin in, coin out.
//
// UI flow (game menu approach, matching Scheme/Sanctuary pattern):
//   Town menu → "Visit the goods exchange"
//     → "ldm_exchange_menu" game menu (one position per volatility class)
//       → MultiSelectionInquiry for the stake (500 / 2000 / 5000)
//         → ShowInquiry confirmation
//           → CommitVenture() → round loop: SELL / HOLD STEADY / SPECULATE HARD
//
// The stake is paid at commit. The round loop runs in inquiries that do not
// survive a save/load, so the open venture is persisted; on session launch an
// interrupted venture is force-liquidated at 90% of book ("your broker closed
// the position") — strictly worse than selling, so reloading is never a win.
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

        // ── Open venture (persisted) ──────────────────────────────────────────
        private static int    _ventureActive;          // 0/1 — int for SyncData
        private static int    _ventureStake;
        private static int    _ventureMultiplier;
        private static int    _ventureRoundsLeft;
        private static int    _ventureRoundsLimit;
        private static int    _ventureMood;
        private static int    _ventureVolatility;
        private static string _ventureTownId    = "";
        private static string _ventureCommodity = "";

        // Today's boards — one concrete good per volatility class, rolled on entry.
        private static readonly string[] _offer = { "Grain", "Wine", "Velvet" };

        private static readonly string[] StapleGoods  = { "Grain", "Wool", "Clay", "Hides", "Fish", "Hardwood" };
        private static readonly string[] CraftedGoods = { "Wine", "Beer", "Oil", "Iron", "Leather", "Pottery", "Linen" };
        private static readonly string[] LuxuryGoods  = { "Velvet", "Spice", "Jewelry", "Furs", "Warhorses" };
        private static readonly string[] ClassNames   = { "staple goods", "crafted goods", "luxury goods" };
        private static readonly string[] ClassOptionIds = { "staple", "crafted", "luxury" };

        // Flavour lines shown each round — pure colour, no mechanical meaning.
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
                store.SyncData("LDX_Stake",       ref _ventureStake);
                store.SyncData("LDX_Mult",        ref _ventureMultiplier);
                store.SyncData("LDX_RoundsLeft",  ref _ventureRoundsLeft);
                store.SyncData("LDX_RoundsLimit", ref _ventureRoundsLimit);
                store.SyncData("LDX_Mood",        ref _ventureMood);
                store.SyncData("LDX_Vol",         ref _ventureVolatility);
                store.SyncData("LDX_TownId",      ref _ventureTownId);
                store.SyncData("LDX_Commodity",   ref _ventureCommodity);
            }
            catch { }
        }

        /// Clears all static state. Called from MainSubModule.OnGameStart so a
        /// save loaded without restarting the process never inherits stale state.
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
        }

        // ── Session launched ──────────────────────────────────────────────────
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            RegisterExchangeMenus(starter);
            ResolveInterruptedVenture();
        }

        // A venture left open across a save/load is force-liquidated at 90% of
        // book — the stake was already paid, so the coin must come back.
        private static void ResolveInterruptedVenture()
        {
            try
            {
                if (_ventureActive == 0) return;
                int payout    = SpeculationMath.ForcedSalePayout(_ventureStake, _ventureMultiplier);
                string name   = string.IsNullOrEmpty(_ventureCommodity) ? "goods" : _ventureCommodity;
                GiveGold(payout);
                AddTradeXp(SpeculationMath.TradeXp(_ventureStake, payout));
                ClearVenture();
                MageKnowledge._deferredInquiry = () =>
                {
                    try
                    {
                        MBInformationManager.AddQuickInformation(new TextObject(
                            $"While you were away, your broker closed the {name} position at 90% of book — {payout} denars returned."));
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
                                "The factors' hall hums with rumour and chalk dust. Boards list what's moving on the roads."
                                + $"\nYour purse: {gold}g  |  Market mood: {MoodLabel(CurrentMood())}"
                                + $"  |  Trade {trade}: {rounds} rounds, crash salvage {salv}%");
                        }
                        catch { }
                    });
            }
            catch { }

            // ── One position per volatility class ─────────────────────────────
            // Option IDs use letters only (no digits) — digits in IDs fail in this BL version.
            try
            {
                for (int v = 0; v < 3; v++)
                {
                    int vol = v;
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
                                        $"Take a position in {_offer[vol]}  —  {ClassNames[vol]}"
                                        + (canAfford ? "" : "  [Insufficient funds]"));
                                    try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                                    args.IsEnabled = canAfford;

                                    int mood = CurrentMood();
                                    int sMin = SpeculationMath.DeltaMin(vol, false, mood);
                                    int sMax = SpeculationMath.DeltaMax(vol, false, mood);
                                    int hMin = SpeculationMath.DeltaMin(vol, true,  mood);
                                    int hMax = SpeculationMath.DeltaMax(vol, true,  mood);
                                    int crashPct = (int)(SpeculationMath.CrashChance(vol, 0, TradeSkill(), false) * 100f);
                                    try { args.Tooltip = new TextObject(
                                        $"Steady round: {Pct(sMin)} to {Pct(sMax)}. Hard round: {Pct(hMin)} to {Pct(hMax)}. "
                                        + $"Crash risk starts near {crashPct}% and climbs every round you stay in."
                                        + (canAfford ? "" : $"\nYou need at least {minStake}g to open a position.")); } catch { }
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
                            ? $"Open the {_offer[vol]} position with a {tier}-denar stake. Sell at any round for stake × position."
                            : $"You carry {gold}g — not enough for this stake.");
                }).ToList();

                MBInformationManager.ShowMultiSelectionInquiry(
                    new MultiSelectionInquiryData(
                        $"The Exchange — {_offer[vol]}",
                        $"How much coin goes on the {_offer[vol]} board?",
                        elements, true, 1, 1,
                        "Select", "Back",
                        chosen =>
                        {
                            try
                            {
                                if (chosen == null || chosen.Count == 0) return;
                                if (!(chosen[0].Identifier is int stake)) return;
                                ShowVentureConfirmation(vol, stake);
                            }
                            catch { }
                        }, null),
                    true);
            }
            catch { }
        }

        // ── Confirmation ──────────────────────────────────────────────────────
        private static void ShowVentureConfirmation(int vol, int stake)
        {
            try
            {
                int trade  = TradeSkill();
                int rounds = SpeculationMath.RoundsLimit(trade);
                int salv   = SpeculationMath.SalvagePercent(trade);
                int mood   = CurrentMood();
                int sMin = SpeculationMath.DeltaMin(vol, false, mood), sMax = SpeculationMath.DeltaMax(vol, false, mood);
                int hMin = SpeculationMath.DeltaMin(vol, true,  mood), hMax = SpeculationMath.DeltaMax(vol, true,  mood);
                int steadyCrash = (int)(SpeculationMath.CrashChance(vol, 0, trade, false) * 100f);
                int hardCrash   = (int)(SpeculationMath.CrashChance(vol, 0, trade, true)  * 100f);

                string body =
                      $"Commodity: {_offer[vol]}  ({ClassNames[vol]})\n"
                    + $"Stake: {stake}g  |  Market mood: {MoodLabel(mood)}\n"
                    + $"Position opens at 100%. Sell at any round for stake × position.\n"
                    + $"Rounds available (Trade {trade}): {rounds} — run out and the brokers force the sale at 90% of book.\n\n"
                    + $"Each round (exact move hidden — revealed only after you commit):\n"
                    + $"  · HOLD STEADY      {Pct(sMin)} to {Pct(sMax)}  —  crash risk from {steadyCrash}%\n"
                    + $"  · SPECULATE HARD   {Pct(hMin)} to {Pct(hMax)}  —  crash risk from {hardCrash}%\n"
                    + $"  · SELL             take the coin and walk\n\n"
                    + $"Crash risk climbs every round you stay in. A crash forfeits the position — "
                    + $"salvage {salv}% of stake (Trade).\n\n"
                    + $"Profitable ventures pay Trade experience.";

                InformationManager.ShowInquiry(
                    new InquiryData($"The Exchange — {_offer[vol]}", body, true, true,
                        "Buy In", "Walk Away",
                        () => CommitVenture(vol, stake), null),
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

                // Stamp the cooldown NOW so a save-reload mid-venture cannot
                // re-enter the exchange for a fresh roll.
                try { _townCooldowns[_ventureTownId] = SpeculationMath.CooldownDays; } catch { }

                try { GameMenu.SwitchToMenu("town"); } catch { }

                // Defer so the menu transition completes before the first round opens.
                MageKnowledge._deferredInquiry = () => { try { ShowRound(); } catch { } };
            }
            catch { }
        }

        // ── Round loop ────────────────────────────────────────────────────────
        private static void ShowRound()
        {
            if (_ventureActive == 0) return;

            int trade     = TradeSkill();
            int completed = _ventureRoundsLimit - _ventureRoundsLeft;
            int value     = SpeculationMath.Payout(_ventureStake, _ventureMultiplier);
            int sMin = SpeculationMath.DeltaMin(_ventureVolatility, false, _ventureMood);
            int sMax = SpeculationMath.DeltaMax(_ventureVolatility, false, _ventureMood);
            int hMin = SpeculationMath.DeltaMin(_ventureVolatility, true,  _ventureMood);
            int hMax = SpeculationMath.DeltaMax(_ventureVolatility, true,  _ventureMood);
            int steadyCrash = (int)(SpeculationMath.CrashChance(_ventureVolatility, completed, trade, false) * 100f);
            int hardCrash   = (int)(SpeculationMath.CrashChance(_ventureVolatility, completed, trade, true)  * 100f);
            bool finalRound = _ventureRoundsLeft == 1;

            string murmur = Murmurs[_rng.Next(Murmurs.Length)];
            string finalWarning = finalRound
                ? "\n\n⚠  FINAL ROUND — after this, the brokers force the sale at 90% of book."
                : "";

            string body =
                  $"{_ventureCommodity} position  —  stake {_ventureStake}g"
                + $"\nPosition: {_ventureMultiplier}%  ({value}g if sold now)  |  Round {completed + 1}/{_ventureRoundsLimit}"
                + $"\nMood: {MoodLabel(_ventureMood)}  |  Crash salvage: {SpeculationMath.SalvagePercent(trade)}% of stake"
                + $"\n\nFrom the floor:\n\"{murmur}\""
                + finalWarning;

            var options = new List<InquiryElement>
            {
                new InquiryElement("sell",
                    $"SELL — Take {value}g now",
                    null, true,
                    $"Close the position at {_ventureMultiplier}%. "
                    + (value >= _ventureStake
                        ? $"Profit: {value - _ventureStake}g."
                        : $"Loss: {_ventureStake - value}g.")),

                new InquiryElement("steady",
                    $"HOLD STEADY — {Pct(sMin)} to {Pct(sMax)}  [crash {steadyCrash}%]",
                    null, true,
                    "Let the position ride on the ordinary flow of trade. "
                    + "Small swing either way — the exact move is hidden until you commit."),

                new InquiryElement("hard",
                    $"SPECULATE HARD — {Pct(hMin)} to {Pct(hMax)}  [crash {hardCrash}%]",
                    null, true,
                    "Lean on the position: buy rumours, bid up scarcity. "
                    + "Big swing either way, and the extra noise raises the crash risk."),
            };

            try
            {
                MBInformationManager.ShowMultiSelectionInquiry(
                    new MultiSelectionInquiryData(
                        $"The Exchange — {_ventureCommodity}",
                        body,
                        options, false, 1, 1,
                        "Confirm", "Sell Out",
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

            bool aggressive = choiceId == "hard";
            int  trade      = TradeSkill();
            int  completed  = _ventureRoundsLimit - _ventureRoundsLeft;

            if (_rng.NextDouble() < SpeculationMath.CrashChance(_ventureVolatility, completed, trade, aggressive))
            {
                CrashVenture();
                return;
            }

            int min   = SpeculationMath.DeltaMin(_ventureVolatility, aggressive, _ventureMood);
            int max   = SpeculationMath.DeltaMax(_ventureVolatility, aggressive, _ventureMood);
            int delta = min + _rng.Next(max - min + 1);
            _ventureMultiplier = SpeculationMath.ApplyDelta(_ventureMultiplier, delta);

            try { MBInformationManager.AddQuickInformation(new TextObject(
                $"The market moved — position {Pct(delta)}, now {_ventureMultiplier}%.")); } catch { }

            _ventureRoundsLeft--;
            if (_ventureRoundsLeft <= 0) { SellVenture(forced: true); return; }
            ShowRound();
        }

        private static void SellVenture(bool forced)
        {
            try
            {
                int payout = forced
                    ? SpeculationMath.ForcedSalePayout(_ventureStake, _ventureMultiplier)
                    : SpeculationMath.Payout(_ventureStake, _ventureMultiplier);
                int profit = payout - _ventureStake;
                string name = _ventureCommodity;

                GiveGold(payout);
                AddTradeXp(SpeculationMath.TradeXp(_ventureStake, payout));
                ClearVenture();

                string profitLine = profit >= 0 ? $"profit {profit}g" : $"loss {-profit}g";
                MBInformationManager.AddQuickInformation(new TextObject(forced
                    ? $"The brokers called time on {name} — forced sale at 90% of book: {payout} denars ({profitLine})."
                    : $"Sold the {name} position — {payout} denars ({profitLine})."));
            }
            catch { }
        }

        private static void CrashVenture()
        {
            try
            {
                int salvage = SpeculationMath.CrashSalvage(_ventureStake, TradeSkill());
                string name = _ventureCommodity;

                GiveGold(salvage);
                AddTradeXp(50);
                ClearVenture();

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"CRASH — the bottom fell out of {name}. Your broker cannot find a buyer at any price. "
                    + (salvage > 0 ? $"Salvage: {salvage} denars." : "Nothing is recovered.")));
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
