// =============================================================================
// ASH AND EMBER — CampaignMapEvents.ApiSeasonal.cs
// Sanctuary/protective public API and Ashen-altar seasonal events.
// Partial of CampaignMapEvents (shared state lives in CampaignMapEvents.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static partial class CampaignMapEvents
    {
        // ── Sanctuary / protective rites public API ───────────────────────────
        internal static int  ProtectedDaysRemaining => _protectedDaysRemaining;
        internal static bool IsProtectedFromAshen   => _protectedDaysRemaining > 0;
        internal static void StartProtection(int days)
            => _protectedDaysRemaining = Math.Max(_protectedDaysRemaining, days);
        internal static void DebugForceTemple()
        {
            if (!_templeFounded) _debugForceNextTemple = true;
        }

        // ── Ashen Altar forced seasonal events ───────────────────────────────
        // Called by AshenAltarsCampaignBehavior when a player performs the
        // Ashen Solstice rite. The season-check guard is intentionally omitted —
        // the sacrifice is what makes it possible regardless of the calendar.
        public static void ForceIronWinter()
        {
            try
            {
                var northKingdoms = Kingdom.All
                    .Where(k => !k.IsEliminated
                             && System.Array.IndexOf(NorthernKingdoms, k.StringId) >= 0)
                    .ToList();
                if (northKingdoms.Count == 0) return;
                var kingdom = northKingdoms[_rng.Next(northKingdoms.Count)];

                int villages = 0, towns = 0;
                foreach (var s in Settlement.All)
                {
                    if (s == null || s.MapFaction != kingdom) continue;
                    if (s.IsVillage && s.Village != null)
                        try { s.Village.Hearth = Math.Max(10f, s.Village.Hearth * 0.5f); villages++; } catch { }
                    else if (s.IsTown && s.Town != null)
                        try
                        {
                            s.Town.Prosperity = Math.Max(10f, s.Town.Prosperity * 0.5f);
                            s.Town.FoodStocks = Math.Max(10f, s.Town.FoodStocks * 0.5f);
                            towns++;
                        }
                        catch { }
                }

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Iron Winter (Ashen Altar) — the cold called by the altar has descended on {kingdom.Name}. " +
                    $"{villages} village{(villages != 1 ? "s" : "")} cannot keep their fires lit. " +
                    $"{towns} cit{(towns != 1 ? "ies" : "y")} ha{(towns != 1 ? "ve" : "s")} halved their stores."));
            }
            catch { }
        }

        public static void ForceScorchingSun()
        {
            try
            {
                var desertKingdoms = Kingdom.All
                    .Where(k => !k.IsEliminated
                             && System.Array.IndexOf(DesertKingdoms, k.StringId) >= 0)
                    .ToList();
                if (desertKingdoms.Count == 0) return;
                var kingdom = desertKingdoms[_rng.Next(desertKingdoms.Count)];

                int villages = 0, towns = 0;
                foreach (var s in Settlement.All)
                {
                    if (s == null || s.MapFaction != kingdom) continue;
                    if (s.IsVillage && s.Village != null)
                        try { s.Village.Hearth = Math.Max(10f, s.Village.Hearth * 0.5f); villages++; } catch { }
                    else if (s.IsTown && s.Town != null)
                        try
                        {
                            s.Town.Prosperity = Math.Max(10f, s.Town.Prosperity * 0.5f);
                            s.Town.FoodStocks = Math.Max(10f, s.Town.FoodStocks * 0.5f);
                            towns++;
                        }
                        catch { }
                }

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Scorching Sun (Ashen Altar) — the heat called by the altar burns {kingdom.Name}. " +
                    $"The wells in {villages} village{(villages != 1 ? "s" : "")} are low or dry. " +
                    $"{towns} cit{(towns != 1 ? "ies" : "y")} ha{(towns != 1 ? "ve" : "s")} rationed their stores."));
            }
            catch { }
        }

        private static void TryFireBrokenWill()
        {
            if (_brokenWillFired >= BrokenWillMaxFires) return;
            if (ElapsedCampaignDays() < BrokenWillEarliestDay) return;
            if (_rng.NextDouble() >= ChanceBrokenWill) return;
            if (!TryClaimWeeklySlot()) return;
            if (_declaringBrokenWill) return;
            if (_protectedDaysRemaining > 0)
            {
                MBInformationManager.AddQuickInformation(new TextObject(
                    "Broken Will — the protective rites hold. The cold fire finds no crack in the ward to slip through."));
                return;
            }

            try
            {
                var candidates = Kingdom.All
                    .Where(k => !k.IsEliminated
                             && k.StringId != AshenKingdomId
                             && !_brokenKingdomIds.Contains(k.StringId)
                             && k.Leader != null
                             && k.Clans.Count(c => c != null && !c.IsEliminated) >= 2)
                    .ToList();
                if (candidates.Count == 0) return;

                var broken = candidates[_rng.Next(candidates.Count)];
                _brokenKingdomIds.Add(broken.StringId);
                _brokenWillFired++;

                string brokenName = broken.Name?.ToString() ?? "a kingdom";
                string leaderName = broken.Leader?.Name?.ToString() ?? "its lord";

                _declaringBrokenWill = true;
                try
                {
                    foreach (var other in Kingdom.All.ToList())
                    {
                        if (other == broken || other.IsEliminated) continue;
                        if (!broken.IsAtWarWith(other))
                            try { DeclareWarAction.ApplyByDefault(broken, other); } catch { }
                    }
                }
                finally { _declaringBrokenWill = false; }

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Broken Will — {leaderName} of {brokenName} has stared into the cold fire " +
                    $"long enough that it began to stare back. " +
                    $"Their banners are raised against every throne in Calradia. " +
                    $"The cold does not negotiate. It does not offer terms. " +
                    $"It only waits. " +
                    $"[{brokenName} declared war on all kingdoms.]"));
            }
            catch { _declaringBrokenWill = false; }
        }

    }
}
