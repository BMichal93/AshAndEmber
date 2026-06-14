// =============================================================================
// ASH AND EMBER — SanctuaryCampaignBehavior.Npc.cs
// NPC daily-tick rites and their party-effect helpers.
// Partial of SanctuaryCampaignBehavior (shared static state lives in SanctuaryCampaignBehavior.cs).
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
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public partial class SanctuaryCampaignBehavior
    {
        // ── NPC daily tick ─────────────────────────────────────────────────────
        private static void OnDailyTick()
        {
            int today = CurrentCampaignDay();

            // Loaded saves that predate the sanctuary system (no announce flag yet)
            // get the establishment toast on their first tick once IDs are settled.
            if (_needsAnnouncementAfterSync)
                AnnounceSanctuaries();

            // Blessed status: 10% healing per day
            if (_blessedUntilDay >= today && MobileParty.MainParty?.MemberRoster != null)
                foreach (var e in MobileParty.MainParty.MemberRoster.GetTroopRoster().ToList())
                {
                    if (e.Character.IsHero || e.WoundedNumber <= 0) continue;
                    int heal = Math.Max(1, (int)(e.WoundedNumber * 0.10f));
                    try { MobileParty.MainParty.MemberRoster.AddToCounts(e.Character, 0, false, -heal); } catch { }
                }

            // Trait boost expiry
            if (_traitBoostUntilDay >= 0 && today > _traitBoostUntilDay)
            { _traitBoostAmount = 0f; _traitBoostUntilDay = -1; }

            // Trait drift: every 10 sanctuary uses, nudge highest-deficit trait up
            if (_sanctuaryUseCount > 0 && _sanctuaryUseCount % TraitDriftThreshold == 0)
            {
                try
                {
                    var h = Hero.MainHero;
                    if (h != null)
                    {
                        int mercy = h.GetTraitLevel(DefaultTraits.Mercy);
                        int honor = h.GetTraitLevel(DefaultTraits.Honor);
                        int gen   = h.GetTraitLevel(DefaultTraits.Generosity);
                        if (mercy <= honor && mercy <= gen && mercy < 2)       h.SetTraitLevel(DefaultTraits.Mercy, mercy + 1);
                        else if (honor <= mercy && honor <= gen && honor < 2)  h.SetTraitLevel(DefaultTraits.Honor, honor + 1);
                        else if (gen < 2)                                       h.SetTraitLevel(DefaultTraits.Generosity, gen + 1);
                        MBInformationManager.AddQuickInformation(new TextObject(
                            "The flame has changed you. A virtue has deepened in you without your noticing."));
                    }
                }
                catch { }
                _sanctuaryUseCount = 0;
            }

            // Steady the Line: extra healing while active
            if (_steadyLineUntilDay >= today && MobileParty.MainParty?.MemberRoster != null)
            {
                int healed = 0;
                foreach (var e in MobileParty.MainParty.MemberRoster.GetTroopRoster().ToList())
                {
                    if (e.Character.IsHero || e.WoundedNumber <= 0) continue;
                    int heal = Math.Min(e.WoundedNumber, 2);
                    try { MobileParty.MainParty.MemberRoster.AddToCounts(e.Character, 0, false, -heal); healed += heal; } catch { }
                    if (healed >= 3) break;
                }
            }

            // ── Honourable + Merciful lords in sanctuary cities ──────────────
            try
            {
                foreach (var hero in Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && !h.IsPrisoner && !h.IsChild
                             && h != Hero.MainHero && h.CurrentSettlement != null
                             && HasSanctuary(h.CurrentSettlement) && NpcCanUseSanctuary(h))
                    .OrderBy(_ => _rng.Next()).Take(8))
                {
                    if (_rng.NextDouble() > 0.003) continue;

                    float mult = NpcSanctuaryMult(hero);
                    // Simulate ritual: 3 rounds, check if succeeds
                    bool success = SimulateNpcRitual(PrayerTargetLo, PrayerTargetHi, mult, 3);
                    if (!success) continue;

                    string city = hero.CurrentSettlement?.Name?.ToString() ?? "the sanctuary";
                    switch (_rng.Next(3))
                    {
                        case 0:
                            NpcHealPartyFull(hero.PartyBelongedTo);
                            InformationManager.DisplayMessage(new InformationMessage(
                                $"{hero.Name} — miracle at the sanctuary in {city}. The wounded rose before sunrise.",
                                new Color(0.80f, 0.72f, 0.45f)));
                            break;
                        case 1:
                            NpcBoostMorale(hero.PartyBelongedTo);
                            InformationManager.DisplayMessage(new InformationMessage(
                                $"{hero.Name} — miracle at the sanctuary in {city}. A renewal of spirit was reported.",
                                new Color(0.80f, 0.72f, 0.45f)));
                            break;
                        case 2:
                            NpcHealPartyPartial(hero.PartyBelongedTo, 0.20f);
                            NpcHealPartyPartial(hero.PartyBelongedTo, 0.20f);
                            InformationManager.DisplayMessage(new InformationMessage(
                                $"{hero.Name} — ward from the sanctuary in {city}. Their injuries knit faster than they should.",
                                new Color(0.80f, 0.72f, 0.45f)));
                            break;
                    }
                }
            }
            catch { }

            // ── Temple lords: partial healing + Turn ──────────────────────────
            try
            {
                var temple = Kingdom.All.FirstOrDefault(k => k.StringId == TempleKingdomId && !k.IsEliminated);
                if (temple == null) return;
                foreach (var lord in Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && !h.IsPrisoner && !h.IsChild
                             && h != Hero.MainHero && h.Clan?.Kingdom == temple
                             && h.PartyBelongedTo?.IsActive == true))
                {
                    float mult = NpcSanctuaryMult(lord);
                    if (_rng.NextDouble() < 0.03 && SimulateNpcRitual(HealTargetLo, HealTargetHi, mult, 4))
                        try { NpcHealPartyPartial(lord.PartyBelongedTo, 0.30f); } catch { }
                    if (_rng.NextDouble() < 0.02 && SimulateNpcRitual(TurnTargetLo, TurnTargetHi, mult, 4))
                        try { NpcTurnAshenNear(lord.PartyBelongedTo); } catch { }
                }
            }
            catch { }
        }

        // Simulates rounds rounds of a ritual for an NPC. Returns true if accumulated >= target.
        private static bool SimulateNpcRitual(int targetLo, int targetHi, float mult, int rounds)
        {
            int target = targetLo + _rng.Next(Math.Max(1, targetHi - targetLo + 1));
            int acc = 0;
            for (int i = 0; i < rounds; i++) acc += RollRoundPoints(mult);
            return acc >= target;
        }

        // ── NPC effect helpers ─────────────────────────────────────────────────
        private static void NpcHealPartyFull(MobileParty party)
        {
            if (party?.MemberRoster == null) return;
            foreach (var e in party.MemberRoster.GetTroopRoster().ToList())
            {
                if (e.Character.IsHero || e.WoundedNumber <= 0) continue;
                try { party.MemberRoster.AddToCounts(e.Character, 0, false, -e.WoundedNumber); } catch { }
            }
        }

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

        private static void NpcBoostMorale(MobileParty party)
        {
            if (party == null) return;
            try { party.RecentEventsMorale += 20f; } catch { }
        }

        private static void NpcTurnAshenNear(MobileParty source)
        {
            if (source == null) return;
            float sx = 0f, sy = 0f;
            try { sx = source.GetPosition2D.x; sy = source.GetPosition2D.y; } catch { return; }
            const float rangeSquared = 100f * 100f;
            var target = MobileParty.All
                .Where(p => { if (!p.IsActive || p.MapFaction?.StringId != AshenKingdomId) return false;
                              float dx = p.GetPosition2D.x - sx, dy = p.GetPosition2D.y - sy;
                              return dx * dx + dy * dy < rangeSquared; })
                .OrderBy(p => { float dx = p.GetPosition2D.x - sx, dy = p.GetPosition2D.y - sy; return dx * dx + dy * dy; })
                .FirstOrDefault();
            if (target == null) return;
            int toWound = 5 + _rng.Next(6), w = 0;
            foreach (var e in target.MemberRoster.GetTroopRoster().ToList())
            {
                if (e.Character.IsHero) continue;
                int healthy = e.Number - e.WoundedNumber;
                int n = Math.Min(healthy, toWound - w); if (n <= 0) continue;
                try { target.MemberRoster.AddToCounts(e.Character, 0, false, n); w += n; } catch { }
                if (w >= toWound) break;
            }
            try { target.RecentEventsMorale -= 20f; } catch { }
        }
    }
}
