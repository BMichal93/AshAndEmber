// =============================================================================
// ASH AND EMBER — AshenCitySystem.Appearance.cs
// Settlement appearance, clan-kingdom enforcement, ownership, villages.
// Partial of AshenCitySystem (shared static state lives in AshenCitySystem.cs).
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
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public partial class AshenCitySystem
    {
        // Called from CampaignBehavior when the player enters a settlement —
        // applies Ashen appearance to any qualifying hero currently present there
        // so portraits look correct before the player opens a conversation.
        public static void ApplyAshenAppearanceToSettlement(Settlement settlement)
        {
            if (settlement == null) return;
            try
            {
                bool isAshenSettlement = _settlementClanMap.ContainsKey(settlement.StringId);
                foreach (Hero h in Hero.AllAliveHeroes.ToList())
                {
                    if (h == Hero.MainHero) continue;
                    if (h.CurrentSettlement != settlement) continue;
                    bool qualifies =
                        ColourLordRegistry.IsAshenLord(h) ||
                        isAshenSettlement ||
                        h.MapFaction?.StringId == AshenKingdomId;
                    if (!qualifies) continue;
                    try { MageKnowledge.ApplyAshenAppearance(h); } catch { }
                }
            }
            catch { }
        }

        // Apply Ashen appearance (grey skin, hair, eyes) to any hero (lord/wanderer/notable)
        // that meets at least one of the following conditions:
        //   1. Is registered as an Ashen lord (ColourLordRegistry.IsAshenLord)
        //   2. Currently resides in an Ashen settlement
        //   3. Belongs to the Ashen faction/kingdom
        //   4. Belongs to an Ashen Spawn party
        private static void ApplyAshenLookToSettlementHeroes()
        {
            try
            {
                foreach (Hero h in Hero.AllAliveHeroes.ToList())
                {
                    if (h == Hero.MainHero) continue;
                    if (!h.IsLord && !h.IsWanderer && !h.IsNotable) continue;

                    bool qualifies =
                        ColourLordRegistry.IsAshenLord(h) ||
                        (h.CurrentSettlement != null && _settlementClanMap.ContainsKey(h.CurrentSettlement.StringId)) ||
                        h.MapFaction?.StringId == AshenKingdomId ||
                        (h.PartyBelongedTo != null && FireWorshippersSystem.IsAshenSpawn(h.PartyBelongedTo));

                    if (!qualifies) continue;
                    try { MageKnowledge.ApplyAshenAppearance(h); } catch { }
                }
            }
            catch { }
        }

        // ── Ashen clan kingdom enforcement ────────────────────────────────────
        // Called from DailyTick. If any Ashen clan has drifted into a foreign
        // kingdom (through AI diplomacy or Bannerlord's own assignment logic),
        // eject and re-add them here rather than inside event callbacks to avoid
        // ClanChangedKingdom re-entrancy crashes.
        private static void TickAshenClanKingdoms()
        {
            if (_ashenKingdom == null || _ashenKingdom.IsEliminated) return;
            int adjustments = 0;
            foreach (string clanId in _ashenClanIds.ToList())
            {
                if (adjustments >= 2) break; // at most 2 kingdom actions per tick
                try
                {
                    var clan = Clan.All.FirstOrDefault(c => c.StringId == clanId);
                    if (clan == null || clan.IsEliminated) continue;
                    if (clan.Kingdom?.StringId == AshenKingdomId) continue; // already home

                    // During the Arenicos merger these city clans are intentionally
                    // inside the Arenicos empire — MaintainFalseEmperorAlliance owns them.
                    if (BurningLabQuestSystem.AshenMergedWithArenicos
                        && BurningLabQuestSystem.ArenicosEmpireId != null
                        && clan.Kingdom?.StringId == BurningLabQuestSystem.ArenicosEmpireId) continue;

                    // Eject from whatever kingdom grabbed them
                    if (clan.Kingdom != null)
                        try { ChangeKingdomAction.ApplyByLeaveKingdom(clan, false); } catch { }

                    // Re-add to the Ashen kingdom
                    if (_ashenKingdom != null && !_ashenKingdom.IsEliminated)
                    {
                        bool needsRuler = _ashenKingdom.RulingClan == null;
                        if (needsRuler)
                            try { ChangeKingdomAction.ApplyByCreateKingdom(clan, _ashenKingdom, false); } catch { }
                        else
                            try { ChangeKingdomAction.ApplyByJoinToKingdom(
                                    clan, _ashenKingdom,
                                    CampaignTime.Now + CampaignTime.Years(1000),
                                    false); }
                            catch { }
                    }
                    adjustments++;
                }
                catch { }
            }
        }

        // ── One-time settlement re-ownership ─────────────────────────────────
        // Called once from Initialize(). Transfers specific contested cities to
        // their intended Empire factions so the political map starts correctly.
        private static void InitialiseSettlementOwnership()
        {
            var assignments = new[]
            {
                ("Husn Fulq", "empire_s"),
                ("Seonon",    "empire_n"),
            };
            foreach (var (name, kingdomId) in assignments)
            {
                try
                {
                    var settlement = Settlement.All.FirstOrDefault(s =>
                        s.Name.ToString().IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (settlement == null || settlement.IsUnderSiege) continue;

                    var kingdom = Kingdom.All.FirstOrDefault(k => k.StringId == kingdomId);
                    if (kingdom == null || kingdom.IsEliminated) continue;

                    Hero leader = kingdom.Leader ?? kingdom.RulingClan?.Leader;
                    if (leader == null) continue;
                    if (settlement.OwnerClan == leader.Clan) continue;

                    try { ChangeOwnerOfSettlementAction.ApplyByDefault(leader, settlement); } catch { }
                    try { if (settlement.Town != null) { settlement.Town.Loyalty = 100f; settlement.Town.Security = 100f; } } catch { }
                }
                catch { }
            }
        }

        // ── Permanent village raid state ──────────────────────────────────────
        // Iterates only village settlements; exits early on non-villages so
        // performance cost is negligible (runs once per in-game day).
        // VillageStates.Looted = burned/destroyed state with no interactions.
        private static void TickAshenVillages()
        {
            try
            {
                foreach (Settlement s in Settlement.All)
                {
                    try
                    {
                        if (!s.IsVillage || s.Village == null) continue;
                        var bound = s.Village.Bound;
                        if (bound == null) continue;
                        if (!_settlementClanMap.TryGetValue(bound.StringId, out string clanId)) continue;
                        if (bound.OwnerClan?.StringId != clanId) continue;
                        // Leave alone while actively being raided
                        if (s.Village.VillageState == Village.VillageStates.BeingRaided) continue;
                        if (s.Village.VillageState != Village.VillageStates.Looted)
                            try { s.Village.VillageState = Village.VillageStates.Looted; } catch { }
                        if (s.Village.Hearth > 10f)
                            try { s.Village.Hearth = 10f; } catch { }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
