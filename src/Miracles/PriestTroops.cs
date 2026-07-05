// =============================================================================
// ASH AND EMBER — Miracles/PriestTroops.cs
//
// Seeds dedicated miracle-casting priest troops into the world so the Miracle
// battle AI (MiracleBattleAI) has bodies to act through:
//   • "Priest of the Flame" (flame_priest) → Sanctuary towns — Grace miracles.
//   • "Ashen Priest"        (ashen_priest)  → Ashen / Dark Altar towns.
//
// Priests are garrisoned (so they appear when their town is besieged or
// defended) and topped up slowly each week up to a small cap per town. Troop
// definitions live in ModuleData/troops.xml. Pure-ish wiring; all TaleWorlds
// access is null-guarded and wrapped in try/catch for mod-conflict safety.
// =============================================================================

using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace AshAndEmber
{
    public static class PriestTroops
    {
        private const string FlamePriestId = "flame_priest";
        private const string AshenPriestId  = "ashen_priest";
        private const string AshenKingdomId = "ashen_kingdom";

        private const int MaxPerTown   = 4; // garrison cap of priests per town
        private const int AddPerWeek   = 1; // gentle top-up so they don't flood

        // Called from the weekly campaign tick.
        public static void WeeklySeed()
        {
            if (Campaign.Current == null) return;

            CharacterObject flame = Find(FlamePriestId);
            CharacterObject ashen = Find(AshenPriestId);
            if (flame == null && ashen == null) return;

            foreach (var s in Settlement.All)
            {
                if (s == null || !s.IsTown) continue;
                var garrison = s.Town?.GarrisonParty;
                if (garrison?.MemberRoster == null) continue;

                bool isSanctuary = SafeHasSanctuary(s);
                bool isAshen     = SafeHasAltar(s) || s.MapFaction?.StringId == AshenKingdomId;

                if (flame != null && isSanctuary) TopUp(garrison, flame);
                // A town that is both (rare) leans to its faction; Ashen wins.
                if (ashen != null && isAshen)     TopUp(garrison, ashen);
            }
        }

        private static void TopUp(MobileParty garrison, CharacterObject priest)
        {
            try
            {
                int current = garrison.MemberRoster.GetTroopRoster()
                    .Where(e => e.Character == priest).Sum(e => e.Number);
                int room = MaxPerTown - current;
                if (room <= 0) return;
                garrison.MemberRoster.AddToCounts(priest, System.Math.Min(AddPerWeek, room));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static CharacterObject Find(string id)
        {
            try { return CharacterObject.All.FirstOrDefault(c => c.StringId == id); }
            catch { return null; }
        }

        private static bool SafeHasSanctuary(Settlement s)
        {
            try { return SanctuaryCampaignBehavior.HasSanctuary(s); } catch { return false; }
        }

        private static bool SafeHasAltar(Settlement s)
        {
            try { return AshenAltarsCampaignBehavior.HasAshenAltar(s); } catch { return false; }
        }
    }
}
