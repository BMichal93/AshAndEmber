// =============================================================================
// ASH AND EMBER — Elementals/SacredSitesCampaignBehavior.cs
//
// Campaign layer for the Forest Clans' sacred sites:
//   • Standing stones / old groves in the Forest Clans' two remaining cities
//     (Dunglanys, Pen Cannoc) — binding a Kindled costs gold, Iron Ore and
//     Charcoal, gated by Smithing skill (CraftingMenu flow, mirrors the
//     Crystalline Chamber's formation menu).
//   • A bound Kindled is a REAL, persistent army troop (sacred_kindled_*,
//     troops.xml) added straight to the player's roster — not a mission-only
//     spawn — and registers for the normal Kindled aura/weakness/self-cast
//     the instant it is fielded (ElementalBeings.RegisterSacredKindled).
//   • Exclusive with Dark Gifts and Grace: the old ways and the darker or
//     holier paths do not share a caster (see HasElementalBond / the block in
//     DarkGiftSystem.TryPurchaseGift/GrantGift and the Sanctuary Grace menu).
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
    public partial class SacredSitesCampaignBehavior : CampaignBehaviorBase
    {
        private static readonly Random _rng = new Random();

        // The Forest Clans' two remaining cities (their other three were
        // reassigned to the Northern Empire at world start — see
        // CampaignBehavior.Events.ReassignImperialSettlements).
        private static readonly string[] SiteTowns = { "Dunglanys", "Pen Cannoc" };

        // Once true, the player has bound at least one Kindled — permanently
        // exclusive with Dark Gifts and Grace from that point on (see the two-way
        // block in DarkGiftSystem and SanctuaryCampaignBehavior.Menus).
        private static bool _hasElementalBond = false;
        public static bool HasElementalBond => _hasElementalBond;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            // Forest Clans lords born/spawned after campaign start roll the same
            // elemental-bond chance the initial cast gets from EstablishForNewCampaign.
            CampaignEvents.HeroCreated.AddNonSerializedListener(this, ElementalLordRegistry.OnHeroCreated);
        }

        public override void SyncData(IDataStore store)
        {
            try { store.SyncData("SACRED_HasElementalBond", ref _hasElementalBond); }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        public static void ResetForNewGame()
        {
            _hasElementalBond = false;
        }

        private static void OnSessionLaunched(CampaignGameStarter starter)
        {
            try { RegisterSacredSiteMenus(starter); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Site gating ────────────────────────────────────────────────────────
        internal static bool HasSacredSite(Settlement s)
        {
            if (s == null || !s.IsTown) return false;
            try
            {
                string name = s.Name?.ToString() ?? "";
                return SiteTowns.Any(t => name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch { return false; }
        }

        // ── Exclusivity with Dark Gifts / Grace ───────────────────────────────
        internal static bool BlockedByOtherPath(out string reason)
        {
            reason = "";
            if (DarkGiftSystem.HasAnyGift) { reason = "The dark gifts in you have already claimed that debt — the old ways will not share you."; return true; }
            if (MiracleInventory.HasGrace) { reason = "Grace already answers when you call — the old ways will not share you."; return true; }
            return false;
        }

        // ── Materials ──────────────────────────────────────────────────────────
        internal static bool HasBindingMaterials(out string missingMsg)
        {
            missingMsg = null;
            try
            {
                var party = MobileParty.MainParty;
                if (party == null) { missingMsg = "No active party."; return false; }
                int goldCost = ForestClansCulture.SiteCost(SacredSiteMath.GoldCost);
                if ((Hero.MainHero?.Gold ?? 0) < goldCost)
                {
                    missingMsg = $"You need {goldCost} denars.";
                    return false;
                }
                var iron = MBObjectManager.Instance?.GetObject<ItemObject>("iron_ore");
                if (iron == null || party.ItemRoster.GetItemNumber(iron) < SacredSiteMath.IronOreCost)
                {
                    missingMsg = $"You need {SacredSiteMath.IronOreCost}× Iron Ore.";
                    return false;
                }
                var charcoal = MBObjectManager.Instance?.GetObject<ItemObject>("charcoal");
                if (charcoal == null || party.ItemRoster.GetItemNumber(charcoal) < SacredSiteMath.CharcoalCost)
                {
                    missingMsg = $"You need {SacredSiteMath.CharcoalCost}× Charcoal.";
                    return false;
                }
            }
            catch { missingMsg = "Could not check materials."; return false; }
            return true;
        }

        internal static void ConsumeBindingMaterials()
        {
            try
            {
                var party = MobileParty.MainParty;
                if (party == null) return;
                Hero.MainHero?.ChangeHeroGold(-ForestClansCulture.SiteCost(SacredSiteMath.GoldCost));
                var iron = MBObjectManager.Instance?.GetObject<ItemObject>("iron_ore");
                if (iron != null) party.ItemRoster.AddToCounts(iron, -SacredSiteMath.IronOreCost);
                var charcoal = MBObjectManager.Instance?.GetObject<ItemObject>("charcoal");
                if (charcoal != null) party.ItemRoster.AddToCounts(charcoal, -SacredSiteMath.CharcoalCost);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        internal static bool RollBinding()
        {
            int smithing = 0;
            try { smithing = Hero.MainHero?.GetSkillValue(DefaultSkills.Smithing) ?? 0; }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            float odds = ForestClansCulture.SiteOdds(SacredSiteMath.FormationOdds(smithing));
            return _rng.NextDouble() < odds;
        }

        internal static void GrantKindled(SacredSiteDef def)
        {
            try
            {
                var party = MobileParty.MainParty;
                var troop = MBObjectManager.Instance?.GetObject<CharacterObject>(def.TroopId);
                if (party?.MemberRoster != null && troop != null)
                {
                    party.MemberRoster.AddToCounts(troop, 1);
                    _hasElementalBond = true;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
