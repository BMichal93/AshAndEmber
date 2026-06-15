// =============================================================================
// ASH AND EMBER — AshenAltarsCampaignBehavior.Npc.cs
// NPC daily-tick rites and their effect helpers.
// Partial of AshenAltarsCampaignBehavior (shared static state lives in AshenAltarsCampaignBehavior.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class AshenAltarsCampaignBehavior
    {
        // ── NPC daily tick ─────────────────────────────────────────────────────
        private static void OnDailyTick()
        {
            int today = CurrentCampaignDay();

            // Solstice passive benefits
            if (_solsticeUntilDay >= today && MobileParty.MainParty != null)
            {
                if (_solsticeType == "winter") { }
                else if (_solsticeType == "sun")
                {
                    try { MobileParty.MainParty.RecentEventsMorale += 0.5f; } catch { }
                }
            }
            else if (_solsticeUntilDay >= 0 && today > _solsticeUntilDay)
            { _solsticeType = ""; _solsticeUntilDay = -1; }

            // Cold Fire freeze: re-apply each day
            if (!string.IsNullOrEmpty(_frozenPartyId) && _frozenUntilDay >= today)
            {
                var frozen = MobileParty.All.FirstOrDefault(p => p.StringId == _frozenPartyId && p.IsActive);
                if (frozen != null)
                {
                    try { frozen.RecentEventsMorale -= 20f; } catch { }
                    int toWound = 2 + _rng.Next(2), w = 0;
                    foreach (var e in frozen.MemberRoster.GetTroopRoster().ToList())
                    {
                        if (e.Character.IsHero) continue;
                        int healthy = e.Number - e.WoundedNumber;
                        int n = Math.Min(healthy, toWound - w); if (n <= 0) continue;
                        try { frozen.MemberRoster.AddToCounts(e.Character, 0, false, n); w += n; } catch { }
                        if (w >= toWound) break;
                    }
                }
            }
            else if (today > _frozenUntilDay && !string.IsNullOrEmpty(_frozenPartyId))
            { _frozenPartyId = ""; _frozenUntilDay = -1; }

            // Trait drift: every 10 altar uses, nudge highest trait down
            if (_altarUseCount > 0 && _altarUseCount % TraitDriftThreshold == 0)
            {
                try
                {
                    var h = Hero.MainHero;
                    if (h != null)
                    {
                        int mercy = h.GetTraitLevel(DefaultTraits.Mercy);
                        int honor = h.GetTraitLevel(DefaultTraits.Honor);
                        int gen   = h.GetTraitLevel(DefaultTraits.Generosity);
                        if (mercy >= honor && mercy >= gen && mercy > -2)      h.SetTraitLevel(DefaultTraits.Mercy, mercy - 1);
                        else if (honor >= mercy && honor >= gen && honor > -2) h.SetTraitLevel(DefaultTraits.Honor, honor - 1);
                        else if (gen > -2)                                      h.SetTraitLevel(DefaultTraits.Generosity, gen - 1);
                        MBInformationManager.AddQuickInformation(new TextObject(
                            "The cold has changed you. Something that was soft in you has hardened."));
                    }
                }
                catch { }
                _altarUseCount = 0;
            }

            // ── Ashen lords in altar cities: dark rites ──────────────────────
            try
            {
                foreach (var hero in Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && !h.IsPrisoner && !h.IsChild
                             && h != Hero.MainHero && h.CurrentSettlement != null
                             && HasAshenAltar(h.CurrentSettlement) && NpcCanUseAltar(h))
                    .OrderBy(_ => _rng.Next()).Take(6))
                {
                    // Aserai lords are drawn to the altars as readily as the Ashen themselves.
                    if (_rng.NextDouble() > (IsAseraiHero(hero) ? 0.010 : 0.005)) continue;

                    float mult = NpcAltarMult(hero);
                    // Simulate ritual (3 rounds), only apply if success
                    bool success = SimulateNpcRitual(BloodTargetLo, BloodTargetHi, mult, 3);
                    if (!success) continue;

                    string city = hero.CurrentSettlement?.Name?.ToString() ?? "the altar";
                    switch (_rng.Next(5))
                    {
                        case 0:
                            NpcHealPartyPartial(hero.PartyBelongedTo, 0.20f);
                            InformationManager.DisplayMessage(new InformationMessage(
                                $"{hero.Name} — dark rite at the altar in {city}. The injured recovered swiftly. Something was paid for it.",
                                new Color(0.38f, 0.50f, 0.75f)));
                            break;
                        case 1:
                            NpcBoostMorale(hero.PartyBelongedTo, 20f);
                            InformationManager.DisplayMessage(new InformationMessage(
                                $"{hero.Name} — blood rite at the altar in {city}. The survivors march with cold resolve.",
                                new Color(0.38f, 0.50f, 0.75f)));
                            break;
                        case 2:
                            NpcCurseNearbyParty(hero.PartyBelongedTo);
                            InformationManager.DisplayMessage(new InformationMessage(
                                $"{hero.Name} — curse whispered at the altar in {city}. Something reached out and touched an enemy in the dark.",
                                new Color(0.38f, 0.50f, 0.75f)));
                            break;
                        case 3:
                            if (NpcCarrionGiftGarrison(out string plagueTarget))
                                InformationManager.DisplayMessage(new InformationMessage(
                                    $"{hero.Name} — carrion rite at the altar in {city}. A grey sickness reached {plagueTarget}.",
                                    new Color(0.38f, 0.50f, 0.75f)));
                            break;
                        case 4:
                            if (NpcBreakWillsCity(out string despairTarget))
                                InformationManager.DisplayMessage(new InformationMessage(
                                    $"{hero.Name} — despair rite at the altar in {city}. {despairTarget} grows restless and fearful.",
                                    new Color(0.38f, 0.50f, 0.75f)));
                            break;
                    }
                }
            }
            catch { }
        }

        // Simulates rounds of a ritual for an NPC. Returns true if accumulated >= target.
        private static bool SimulateNpcRitual(int targetLo, int targetHi, float mult, int rounds)
        {
            int target = targetLo + _rng.Next(Math.Max(1, targetHi - targetLo + 1));
            int acc = 0;
            for (int i = 0; i < rounds; i++) acc += RollRoundPoints(mult);
            return acc >= target;
        }

        // ── NPC effect helpers ─────────────────────────────────────────────────
        private static void NpcHealPartyPartial(MobileParty party, float fraction)
        {
            if (party?.MemberRoster == null) return;
            foreach (var e in party.MemberRoster.GetTroopRoster().ToList())
            {
                if (e.Character.IsHero || e.WoundedNumber <= 0) continue;
                int heal = Math.Max(1, (int)(e.WoundedNumber * fraction));
                try { party.MemberRoster.AddToCounts(e.Character, 0, false, -heal); } catch { }
            }
        }

        private static void NpcBoostMorale(MobileParty party, float amount)
        {
            if (party == null) return;
            try { party.RecentEventsMorale += amount; } catch { }
        }

        private static bool NpcCarrionGiftGarrison(out string targetName)
        {
            targetName = "";
            try
            {
                var candidates = Settlement.All
                    .Where(s => s.IsTown && s.MapFaction?.StringId != AshenKingdomId
                             && s.Town?.GarrisonParty?.MemberRoster?.TotalManCount > 0).ToList();
                if (candidates.Count == 0) return false;
                var target = candidates[_rng.Next(candidates.Count)];
                targetName = target.Name?.ToString() ?? "a distant garrison";
                foreach (var e in target.Town.GarrisonParty.MemberRoster.GetTroopRoster().ToList())
                {
                    if (e.Character.IsHero) continue;
                    int healthy = e.Number - e.WoundedNumber; if (healthy <= 0) continue;
                    int toWound = Math.Max(1, (int)(healthy * (0.10f + (float)_rng.NextDouble() * 0.10f)));
                    try { target.Town.GarrisonParty.MemberRoster.AddToCounts(e.Character, 0, false, toWound); } catch { }
                }
                return true;
            }
            catch { return false; }
        }

        private static bool NpcBreakWillsCity(out string targetName)
        {
            targetName = "";
            try
            {
                var candidates = Settlement.All
                    .Where(s => s.IsTown && s.MapFaction?.StringId != AshenKingdomId && s.Town != null).ToList();
                if (candidates.Count == 0) return false;
                var target = candidates[_rng.Next(candidates.Count)];
                targetName = target.Name?.ToString() ?? "a distant city";
                float drain = 5f + (float)_rng.NextDouble() * 5f;
                try { target.Town.Loyalty  = Math.Max(0f, target.Town.Loyalty  - drain); } catch { }
                try { target.Town.Security = Math.Max(0f, target.Town.Security - drain); } catch { }
                return true;
            }
            catch { return false; }
        }

        private static void NpcCurseNearbyParty(MobileParty source)
        {
            if (source == null) return;
            float sx = 0f, sy = 0f;
            try { sx = source.GetPosition2D.x; sy = source.GetPosition2D.y; } catch { return; }
            const float rangeSquared = 80f * 80f;
            var target = MobileParty.All
                .Where(p => { if (!p.IsActive || p.MapFaction?.StringId == AshenKingdomId) return false;
                              float dx = p.GetPosition2D.x - sx, dy = p.GetPosition2D.y - sy;
                              return dx * dx + dy * dy < rangeSquared; })
                .OrderBy(p => { float dx = p.GetPosition2D.x - sx, dy = p.GetPosition2D.y - sy; return dx * dx + dy * dy; })
                .FirstOrDefault();
            if (target == null) return;
            int toWound = 3 + _rng.Next(5), w = 0;
            foreach (var e in target.MemberRoster.GetTroopRoster().ToList())
            {
                if (e.Character.IsHero) continue;
                int healthy = e.Number - e.WoundedNumber;
                int n = Math.Min(healthy, toWound - w); if (n <= 0) continue;
                try { target.MemberRoster.AddToCounts(e.Character, 0, false, n); w += n; } catch { }
                if (w >= toWound) break;
            }
            try { target.RecentEventsMorale -= 15f; } catch { }
        }
    }
}
