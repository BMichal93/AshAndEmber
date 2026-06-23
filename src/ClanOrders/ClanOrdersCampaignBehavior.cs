// =============================================================================
// ASH AND EMBER — ClanOrders/ClanOrdersCampaignBehavior.cs
// Issue commands to your clan parties: ride to a settlement or hunt a lord.
//
// Dialogue hook fires in "lord_pretalk" for any hero who is the leader of a
// non-main clan party. Two orders are available:
//   Travel — party rides to a chosen settlement; clears on arrival.
//   Hunt   — party pursues a named lord; resolves by natural combat (if at war)
//            or by an abstract assassination inquiry when close enough.
// Risky hunt orders require a Leadership roll before the order is accepted.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class ClanOrdersCampaignBehavior : CampaignBehaviorBase
    {
        // ── Persistent state (parallel lists, serialised per save) ────────────
        private static List<string> _orderPartyIds  = new List<string>();
        private static List<string> _orderTypes     = new List<string>(); // "travel" | "hunt"
        private static List<string> _orderTargetIds = new List<string>(); // settlement or hero StringId

        internal static readonly Random _rng = new Random();

        // ── CampaignBehaviorBase ──────────────────────────────────────────────
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
        }

        public override void SyncData(IDataStore store)
        {
            try { store.SyncData("CLANORD_PARTY_IDS",  ref _orderPartyIds);  } catch { }
            try { store.SyncData("CLANORD_TYPES",      ref _orderTypes);     } catch { }
            try { store.SyncData("CLANORD_TARGET_IDS", ref _orderTargetIds); } catch { }
        }

        // ── Order CRUD ────────────────────────────────────────────────────────
        internal static void SetOrder(MobileParty party, string type, string targetId)
        {
            if (party == null) return;
            ClearOrder(party);
            _orderPartyIds.Add(party.StringId);
            _orderTypes.Add(type);
            _orderTargetIds.Add(targetId);
        }

        internal static void ClearOrder(MobileParty party)
        {
            if (party == null) return;
            int idx = _orderPartyIds.IndexOf(party.StringId);
            if (idx < 0) return;
            _orderPartyIds.RemoveAt(idx);
            _orderTypes.RemoveAt(idx);
            _orderTargetIds.RemoveAt(idx);
        }

        internal static bool HasOrder(MobileParty party)
            => party != null && _orderPartyIds.Contains(party.StringId);

        internal static bool TryGetOrder(MobileParty party, out string type, out string targetId)
        {
            type = null; targetId = null;
            if (party == null) return false;
            int idx = _orderPartyIds.IndexOf(party.StringId);
            if (idx < 0) return false;
            type     = _orderTypes[idx];
            targetId = _orderTargetIds[idx];
            return true;
        }

        // ── Leadership test ───────────────────────────────────────────────────
        // Chance (0–95%) that a risky order is accepted. A risky hunt is one where
        // the quarry's party strength exceeds the ordered party's by 50 % or more.
        internal static int LeadershipSuccessChance(Hero player)
        {
            try
            {
                int leadership = player?.GetSkillValue(DefaultSkills.Leadership) ?? 0;
                return Math.Min(95, 40 + leadership / 5);
            }
            catch { return 40; }
        }

        internal static void RemoveOrderAt(int idx)
        {
            try
            {
                _orderPartyIds.RemoveAt(idx);
                _orderTypes.RemoveAt(idx);
                _orderTargetIds.RemoveAt(idx);
            }
            catch { }
        }
    }
}
