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
    public partial class ExchangeCampaignBehavior : CampaignBehaviorBase
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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
    }
}
