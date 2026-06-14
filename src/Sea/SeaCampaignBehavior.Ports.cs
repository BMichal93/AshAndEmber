// =============================================================================
// ASH AND EMBER — Sea/SeaCampaignBehavior.Ports.cs
// Port resolution and party-strength/casualty readings for SeaMath.
// Partial of SeaCampaignBehavior (shared static state lives in SeaCampaignBehavior.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class SeaCampaignBehavior
    {
        // ── Port resolution ────────────────────────────────────────────────────
        private static void ResolvePorts()
        {
            _ports.Clear();
            if (Campaign.Current == null) return;

            // Primary: the curated harbour list above. This is intentionally a
            // designed set (not every literal coastal town), so it takes precedence.
            foreach (var s in Settlement.All)
            {
                if (s == null || !s.IsTown) continue;
                string name = null;
                try { name = s.Name?.ToString(); } catch { }
                if (string.IsNullOrEmpty(name)) continue;
                if (PortTownNames.Any(p => string.Equals(p, name.Trim(), StringComparison.OrdinalIgnoreCase)))
                    _ports.Add(s);
            }
            if (_ports.Count >= 2) return;

            // Fallback: if too few names resolve (localized client, renamed towns),
            // fall back to the game's own coastal flag so a network still forms.
            _ports.Clear();
            foreach (var s in Settlement.All)
            {
                if (s == null || !s.IsTown) continue;
                try { if (s.HasPort) _ports.Add(s); } catch { }
            }
        }

        private static bool IsPort(Settlement s) => s != null && _ports.Contains(s);

        // A port whose holding faction is the Ashen — the cold coast, dreaded by sailors.
        private static bool IsAshenPort(Settlement s)
        {
            try { return s != null && AshenCitySystem.IsAshenFaction(s.MapFaction); }
            catch { return false; }
        }

        private static float PortDistance(Settlement a, Settlement b)
        {
            try { return (a.GetPosition2D - b.GetPosition2D).Length; }
            catch { return 0f; }
        }

        // ── Party readings for SeaMath ─────────────────────────────────────────
        private static float FleetStrengthOf(MobileParty party, bool searTheTide)
        {
            int troops = 0; float tierSum = 0f;
            try
            {
                foreach (var e in party.MemberRoster.GetTroopRoster())
                {
                    if (e.Character == null) continue;
                    int healthy = e.Number - e.WoundedNumber;
                    if (healthy <= 0) continue;
                    troops  += healthy;
                    tierSum += healthy * e.Character.Tier;
                }
            }
            catch { }
            float avgTier = troops > 0 ? tierSum / troops : 0f;
            int tactics = 0;
            try { tactics = party.LeaderHero?.GetSkillValue(DefaultSkills.Tactics) ?? 0; } catch { }
            return SeaMath.FleetStrength(troops, avgTier, tactics, searTheTide);
        }

        // Strikes a fraction of the party's healthy regulars: ~60% wounded,
        // the rest lost to the water. Returns the number of men affected.
        private static int ApplySeaCasualties(MobileParty party, float fraction)
        {
            int affected = 0;
            try
            {
                var roster = party.MemberRoster;
                int totalHealthy = 0;
                foreach (var e in roster.GetTroopRoster().ToList())
                {
                    if (e.Character == null || e.Character.IsHero) continue;
                    totalHealthy += Math.Max(0, e.Number - e.WoundedNumber);
                }
                if (totalHealthy <= 0) return 0;

                int toHit   = Math.Max(1, (int)(totalHealthy * fraction));
                int toKill  = toHit * 2 / 5;
                int toWound = toHit - toKill;

                foreach (var e in roster.GetTroopRoster().ToList())
                {
                    if (toWound <= 0 && toKill <= 0) break;
                    if (e.Character == null || e.Character.IsHero) continue;
                    int healthy = e.Number - e.WoundedNumber;
                    if (healthy <= 0) continue;

                    int w = Math.Min(healthy, toWound);
                    if (w > 0)
                    {
                        try { roster.AddToCounts(e.Character, 0, false, w); toWound -= w; affected += w; healthy -= w; } catch { }
                    }
                    int k = Math.Min(healthy, toKill);
                    if (k > 0)
                    {
                        try { roster.AddToCounts(e.Character, -k); toKill -= k; affected += k; } catch { }
                    }
                }
            }
            catch { }
            return affected;
        }
    }
}
