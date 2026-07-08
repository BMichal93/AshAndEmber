// =============================================================================
// ASH AND EMBER — AI/AshenRecruitCampaignBehavior.cs
//
// Lets the player recruit the Ashen dead directly from any Ashen-owned town
// or castle, instead of only ever meeting them as garrison filler
// (AshenCitySystem.Maintenance) or roaming Spawn bands. The price is
// prisoners, not gold — turning a captive into one of the cold-fire dead IS
// the transaction (see AshenRecruitMath / AshenRecruitCatalog for the
// per-rank prisoner-tier toll). Cruelty this real does not also take your
// coin.
// =============================================================================

using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public partial class AshenRecruitCampaignBehavior : CampaignBehaviorBase
    {
        private const string AshenKingdomId = "ashen_kingdom";

        // Settlement granted Ashen recruiting by The Scholar's Bargain questline,
        // independent of who actually owns it or holds the Ashen kingdom.
        private static string _scholarGrantedSettlementId = null;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        public override void SyncData(IDataStore store)
        {
            store.SyncData("ARC_ScholarGrantedSettlement", ref _scholarGrantedSettlementId);
        }

        private static void OnSessionLaunched(CampaignGameStarter starter)
        {
            try { RegisterAshenRecruitMenus(starter); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Called by ScholarBargainQuestSystem when the scholar's bargain pays off:
        // the named settlement can muster the Ashen dead permanently, whether or
        // not the player (or the settlement) is ever actually Ashen.
        internal static void GrantScholarRecruiting(string settlementId)
        {
            _scholarGrantedSettlementId = settlementId;
        }

        // ── Gating ─────────────────────────────────────────────────────────────
        internal static bool HasAshenRecruiter(Settlement s)
        {
            if (s == null || (!s.IsTown && !s.IsCastle)) return false;
            try { return s.MapFaction?.StringId == AshenKingdomId || s.StringId == _scholarGrantedSettlementId; }
            catch { return false; }
        }

        // True when the muster yard exists here only because of the scholar's
        // bargain, not because this is an actual Ashen holding — used to add the
        // per-unit crime toll the bargain carries that a native Ashen city does not.
        internal static bool IsScholarGrantedOnly(Settlement s)
        {
            if (s == null || s.StringId != _scholarGrantedSettlementId) return false;
            try { return s.MapFaction?.StringId != AshenKingdomId; }
            catch { return true; }
        }

        // ── Prisoners ──────────────────────────────────────────────────────────
        internal static bool HasQualifyingPrisoners(AshenRecruitDef def, out string missingMsg)
        {
            missingMsg = null;
            try
            {
                var party = MobileParty.MainParty;
                if (party?.PrisonRoster == null) { missingMsg = "No prisoners."; return false; }
                int have = party.PrisonRoster.GetTroopRoster()
                    .Where(e => e.Character != null && !e.Character.IsHero
                             && AshenRecruitMath.PrisonerQualifies(e.Character.Tier, def.RequiredPrisonerTier))
                    .Sum(e => e.Number);
                if (have < def.PrisonerCost)
                {
                    missingMsg = $"Requires {def.PrisonerCost} prisoner(s) of tier {def.RequiredPrisonerTier}+ (you hold {have}).";
                    return false;
                }
            }
            catch { missingMsg = "Could not check prisoners."; return false; }
            return true;
        }

        // Spends the cheapest qualifying prisoners first, so a captive who just
        // clears the bar is taken before one who clears it by a wide margin.
        internal static void ConsumeQualifyingPrisoners(AshenRecruitDef def)
        {
            try
            {
                var party = MobileParty.MainParty;
                if (party?.PrisonRoster == null) return;

                var roster = party.PrisonRoster.GetTroopRoster()
                    .Where(e => e.Character != null && !e.Character.IsHero
                             && AshenRecruitMath.PrisonerQualifies(e.Character.Tier, def.RequiredPrisonerTier))
                    .OrderBy(e => e.Character.Tier)
                    .ToList();

                int left = def.PrisonerCost;
                foreach (var entry in roster)
                {
                    if (left <= 0) break;
                    int take = Math.Min(entry.Number, left);
                    party.PrisonRoster.AddToCounts(entry.Character, -take);
                    left -= take;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        internal static void GrantAshenTroop(AshenRecruitDef def)
        {
            try
            {
                var party = MobileParty.MainParty;
                var troop = MBObjectManager.Instance?.GetObject<CharacterObject>(def.TroopId);
                if (party?.MemberRoster != null && troop != null)
                    party.MemberRoster.AddToCounts(troop, 1);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
