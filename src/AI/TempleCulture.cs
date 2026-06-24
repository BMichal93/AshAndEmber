// =============================================================================
// ASH AND EMBER — AI/TempleCulture.cs
// The Templar (formerly Vlandian) culture's starting feats — real mechanics.
//
//   Dawn's Grace      — if Grace is empty at dawn, the Light restores one point.
//   Oath of the Vigil — a standing party-morale bonus from drilled faith (+4/day).
//   The Order's Price — dark gifts cost twice as much; drawing the living
//                       ember takes one second longer (the order shuns both).
//
// The daily effects are applied via DailyTick(), called from MagicCampaignBehavior.
// The cost and channel-time penalties are read at the point each is charged.
// All effects are gated on the player having chosen the Templar (vlandia) culture.
// =============================================================================

using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace AshAndEmber
{
    internal static class TempleCulture
    {
        // The player chose the Templar (Vlandian) culture at character creation.
        public static bool IsPlayerTemplar
        {
            get { try { return Hero.MainHero?.Culture?.StringId == "vlandia"; } catch { return false; } }
        }

        // ── The Order's Price (penalties) ──────────────────────────────────────
        private const int   DarkGiftCostMultiplier    = 2;
        private const float ExtraNatureChannelSeconds = 1f;

        public static int DarkGiftCost(int baseCost)
            => IsPlayerTemplar ? baseCost * DarkGiftCostMultiplier : baseCost;

        public static float NatureChannelSeconds(float baseSeconds)
            => IsPlayerTemplar ? baseSeconds + ExtraNatureChannelSeconds : baseSeconds;

        // ── Oath of the Vigil (daily morale floor) ───────────────────────────
        // Keeps RecentEventsMorale at or above this soft floor for all clan parties.
        // Only fills when below the threshold — decays naturally above it, so the
        // net effect is a standing ~+10 on recent-events morale, consistent in scale
        // with what other in-game events (paid wages, food variety) contribute.
        public const float VigilMoraleFloor = 10f;

        // ── Templar Kingdom Setup — called once per session ───────────────────
        // A holy order is small and elite. Trims Vlandia to TempleMaxClans so
        // it fields fewer clans than any other faction. Also ensures every Temple
        // lord carries at least one Grace (Honor ≥ 1 and Mercy ≥ 1) so they can
        // invoke miracles in battle.
        private const int TempleMaxClans = 4;

        public static void SetupTempleKingdom()
        {
            TrimTempleClans();
            EnsureTemplarGrace();
        }

        private static void TrimTempleClans()
        {
            try
            {
                var vlandia = Kingdom.All.FirstOrDefault(k =>
                    k.StringId == "vlandia" && !k.IsEliminated);
                if (vlandia == null) return;

                var ordered = vlandia.Clans
                    .Where(c => !c.IsEliminated && c != Clan.PlayerClan)
                    .OrderByDescending(c => c.Renown)
                    .ToList();

                if (ordered.Count <= TempleMaxClans) return;

                // Ruling clan is always kept; fill remaining slots from the top.
                var toKeep = new List<Clan>();
                var ruling = vlandia.RulingClan;
                if (ruling != null && !ruling.IsEliminated) toKeep.Add(ruling);

                foreach (var c in ordered)
                {
                    if (toKeep.Count >= TempleMaxClans) break;
                    if (!toKeep.Contains(c)) toKeep.Add(c);
                }

                foreach (var c in ordered.Where(c => !toKeep.Contains(c)))
                    try { ChangeKingdomAction.ApplyByLeaveKingdom(c, false); } catch { }
            }
            catch { }
        }

        private static void EnsureTemplarGrace()
        {
            try
            {
                var vlandia = Kingdom.All.FirstOrDefault(k =>
                    k.StringId == "vlandia" && !k.IsEliminated);
                if (vlandia == null) return;

                foreach (var clan in vlandia.Clans.ToList())
                {
                    foreach (var hero in clan.Heroes.ToList())
                    {
                        if (hero == null || !hero.IsAlive || hero.IsDisabled || hero == Hero.MainHero) continue;
                        try
                        {
                            if (hero.GetTraitLevel(DefaultTraits.Honor) < 1)
                                hero.SetTraitLevel(DefaultTraits.Honor, 1);
                            if (hero.GetTraitLevel(DefaultTraits.Mercy) < 1)
                                hero.SetTraitLevel(DefaultTraits.Mercy, 1);
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        // ── Daily tick — called from MagicCampaignBehavior.OnDailyTick ────────
        public static void DailyTick()
        {
            if (!IsPlayerTemplar) return;

            // Dawn's Grace: if Grace is empty at dawn, the Light restores one point.
            if (MiracleInventory.Grace == 0)
            {
                MiracleInventory.AddGrace(1);
                InformationManager.DisplayMessage(new InformationMessage(
                    "Dawn's Grace — the Light finds you in want and restores a measure of it. (+1 Grace)",
                    new Color(0.90f, 0.82f, 0.42f)));
            }

            // Oath of the Vigil: drilled faith keeps the column steady.
            // Acts as a soft floor — only fills when recent-events morale has fallen
            // below VigilMoraleFloor, rather than stacking on top each day.
            try
            {
                var playerClan = Clan.PlayerClan;
                if (playerClan != null)
                {
                    foreach (var party in MobileParty.All.ToList())
                    {
                        if (party == null || !party.IsActive) continue;
                        if (party.LeaderHero?.Clan != playerClan) continue;
                        try
                        {
                            if (party.RecentEventsMorale < VigilMoraleFloor)
                                party.RecentEventsMorale = VigilMoraleFloor;
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }
    }
}
