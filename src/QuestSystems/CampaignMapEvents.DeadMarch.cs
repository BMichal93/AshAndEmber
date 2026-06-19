// =============================================================================
// ASH AND EMBER — CampaignMapEvents.DeadMarch.cs
// The Dead March and the Undying Host.
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
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static partial class CampaignMapEvents
    {
        // ── Event: The Dead March ─────────────────────────────────────────────
        // On campaign day 50 the Ashen perform a cold necromantic rite — the
        // fallen of old campaigns answer. Every Ashen garrison and lord party
        // is reinforced with 40–80 troops spread across tiers 2, 3, and 4
        // (~⅓ each) from that settlement or lord's culture. After the first
        // fire the event recurs roughly every 110 days (chance-based, minimum
        // 95-day gap between fires).
        //
        // Troop tier: walks the culture's elite upgrade chain and returns the
        // first troop that reaches tier 4, or the highest available if the chain
        // is shorter.
        //
        // Safety constraints:
        //   • Wrapped in try/catch per garrison and per party — one failure does
        //     not abort the rest of the reinforcement pass.
        //   • First fire is forced (no chance roll) but still claims the weekly
        //     slot so it cannot stack with another event in the same tick.
        private static void TryFireDeadMarch()
        {
            int day = (int)ElapsedCampaignDays();

            if (!_deadMarchFirstFired)
            {
                if (day < DeadMarchFirstDay) return;
                // First fire is forced — no chance roll, but claim the slot below.
            }
            else
            {
                if (day - _deadMarchLastFiredDay < DeadMarchRecurrenceGap) return;
                if (_rng.NextDouble() >= ChanceDeadMarch) return;
            }

            if (!TryClaimWeeklySlot()) return;

            try
            {
                bool isFirstFire       = !_deadMarchFirstFired;
                _deadMarchFirstFired   = true;
                _deadMarchLastFiredDay = day;

                int garrisonsBoosted = 0;
                int armiesBoosted    = 0;

                // Reinforce every Ashen garrison
                foreach (var s in Settlement.All)
                {
                    try
                    {
                        if (s.MapFaction?.StringId != AshenKingdomId) continue;
                        var garrison = s.Town?.GarrisonParty;
                        if (garrison?.MemberRoster == null) continue;

                        int count = DeadMarchMinTroops + _rng.Next(DeadMarchMaxTroops - DeadMarchMinTroops + 1);
                        AddMixedTierTroops(garrison.MemberRoster, s.Culture, count);
                        garrisonsBoosted++;
                    }
                    catch { }
                }

                // Reinforce every Ashen lord's mobile party
                foreach (var party in MobileParty.All.ToList())
                {
                    try
                    {
                        if (party.MapFaction?.StringId != AshenKingdomId) continue;
                        if (party.IsGarrison) continue;
                        if (party.LeaderHero == null) continue;

                        var culture = party.LeaderHero.Culture ?? party.MapFaction?.Culture;
                        int count = DeadMarchMinTroops + _rng.Next(DeadMarchMaxTroops - DeadMarchMinTroops + 1);
                        AddMixedTierTroops(party.MemberRoster, culture, count);
                        armiesBoosted++;
                    }
                    catch { }
                }

                string flavour = isFirstFire
                    ? "On the fiftieth day the ash stirs in the mountain passes. Shapes walk that do not breathe. " +
                      "The Ashen do not mourn their fallen — they call them back. " +
                      "The dead answer because they were never truly released."
                    : "A grey wind descends from the north carrying no warmth and no sound. " +
                      "The Ashen count their fallen, and find them present. " +
                      "The dead march because the cold permits nothing else.";

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"The Dead March — {flavour} " +
                    $"[{garrisonsBoosted} garrison{(garrisonsBoosted != 1 ? "s" : "")} and " +
                    $"{armiesBoosted} arm{(armiesBoosted != 1 ? "ies" : "y")} reinforced with risen dead.]"));
            }
            catch { }
        }

        // ── Event: The Undying Host ───────────────────────────────────────────
        // Once per campaign, the Ashen forge a conquest army of UndyingHostTroopCount
        // elite troops. The strongest active Ashen lord is chosen as its vanguard;
        // their party roster is packed with high-tier troops and their clan receives
        // crushing influence so army cohesion is never a limiting factor. A massive
        // morale surge keeps the oversized party from bleeding troops to desertion.
        // The Ashen kingdom is guaranteed to be at war when the host marches.
        //
        // Probability curve:
        //   Day < 80   : impossible
        //   Day 80–200 : linear ramp 0 → ChanceUndyingHostBase (4%)
        //   Day 200–400: flat ChanceUndyingHostBase (expected fire ~day 375)
        //   Day 400+   : ChanceUndyingHostLatent (60%) — fires within 1–2 weeks
        //
        // Safety constraints:
        //   • Fires at most once per campaign (_undyingHostFired, saved/loaded).
        //   • Sanctuary protection shows a warning message and aborts.
        //   • All lord / roster / influence mutations individually try/caught.
        //   • Falls back to a flavour-only announcement if no suitable lord is found.
        private static void TryFireTheUndyingHost()
        {
            if (_undyingHostFired) return;

            double days = ElapsedCampaignDays();
            if (days < UndyingHostEarliestDay) return;

            float chance;
            if (days >= UndyingHostNearCertainDay)
                chance = ChanceUndyingHostLatent;
            else if (days >= UndyingHostRampEndDay)
                chance = ChanceUndyingHostBase;
            else
                chance = ChanceUndyingHostBase * (float)((days - UndyingHostEarliestDay) / (UndyingHostRampEndDay - UndyingHostEarliestDay));

            if (_rng.NextDouble() >= chance) return;
            if (!TryClaimWeeklySlot()) return;

            if (_protectedDaysRemaining > 0)
            {
                MBInformationManager.AddQuickInformation(new TextObject(
                    "The Undying Host — Something vast stirs in the mountain passes. " +
                    "The sanctuary's ward blazes white and holds. Tonight the wall stands. " +
                    "But the cold does not tire. It will wait."));
                return;
            }

            _undyingHostFired = true;

            try
            {
                var ashenKingdom = Kingdom.All.FirstOrDefault(k =>
                    k.StringId == AshenKingdomId && !k.IsEliminated);

                // ── Choose the vanguard lord ──────────────────────────────────
                // Prefer the highest-tier active Ashen lord with their own mobile party.
                var vanguard = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && !h.IsChild && !h.IsPrisoner && !h.IsDisabled
                             && ColourLordRegistry.IsAshenLord(h)
                             && h.PartyBelongedTo != null
                             && h.PartyBelongedTo.IsActive
                             && !h.PartyBelongedTo.IsGarrison)
                    .OrderByDescending(h => h.Clan?.Tier ?? 0)
                    .ThenByDescending(h => h.Clan?.Renown ?? 0f)
                    .FirstOrDefault();

                string vanguardName = "an unnamed warlord of ash";
                bool troopsAdded = false;

                if (vanguard != null)
                {
                    vanguardName = vanguard.Name?.ToString() ?? vanguardName;
                    var party = vanguard.PartyBelongedTo;

                    // ── Pack the party roster with UndyingHostTroopCount elite troops ──
                    try
                    {
                        var culture = vanguard.Culture ?? vanguard.Clan?.Culture;
                        // Prefer tier 5; fall back to tier 4 if the culture's chain is shorter.
                        var troop = GetTroopAtTier(culture, 5) ?? GetTroopAtTier(culture, 4);
                        if (troop == null)
                        {
                            troop = MBObjectManager.Instance.GetObject<CharacterObject>("sea_raider")
                                 ?? MBObjectManager.Instance.GetObject<CharacterObject>("mountain_bandit");
                        }
                        if (troop != null)
                        {
                            party.MemberRoster.AddToCounts(troop, UndyingHostTroopCount);
                            troopsAdded = true;
                        }
                    }
                    catch { }

                    // ── Morale surge — oversized parties bleed morale; drown it out ──
                    try { party.RecentEventsMorale += 100f; } catch { }
                }

                // ── Flood the ruling clan with influence ──────────────────────
                // This prevents cohesion from ever becoming a bottleneck if the
                // engine decides to form an army around the vanguard.
                try
                {
                    if (ashenKingdom?.RulingClan != null)
                        ashenKingdom.RulingClan.Influence =
                            Math.Max(ashenKingdom.RulingClan.Influence, UndyingHostInfluenceGrant);
                }
                catch { }

                // Also flood every other Ashen lord clan with generous influence so
                // they can sustain secondary armies and reinforce the vanguard freely.
                try
                {
                    foreach (var hero in Hero.AllAliveHeroes)
                    {
                        if (!hero.IsLord || !ColourLordRegistry.IsAshenLord(hero)) continue;
                        if (hero.Clan == null || hero.Clan == ashenKingdom?.RulingClan) continue;
                        try { hero.Clan.Influence = Math.Max(hero.Clan.Influence, 2000f); } catch { }
                    }
                }
                catch { }

                // ── Guarantee the Ashen are at war ────────────────────────────
                // If the Ashen somehow have no active wars, declare on the strongest
                // non-player, non-Ashen kingdom so the host immediately has a march target.
                try
                {
                    if (ashenKingdom != null)
                    {
                        bool atWar = Kingdom.All.Any(k =>
                            !k.IsEliminated && k != ashenKingdom && ashenKingdom.IsAtWarWith(k));

                        if (!atWar)
                        {
                            var target = Kingdom.All
                                .Where(k => !k.IsEliminated
                                         && k.StringId != AshenKingdomId
                                         && !ashenKingdom.IsAtWarWith(k)
                                         && k != Hero.MainHero?.Clan?.Kingdom)
                                .OrderByDescending(k => k.CurrentTotalStrength)
                                .FirstOrDefault();
                            if (target != null)
                                DeclareWarAction.ApplyByDefault(ashenKingdom, target);
                        }
                    }
                }
                catch { }

                // ── Morale surge across all Ashen lord parties ────────────────
                try
                {
                    foreach (var party in MobileParty.All)
                    {
                        if (!party.IsActive) continue;
                        var leader = party.LeaderHero;
                        if (leader != null && ColourLordRegistry.IsAshenLord(leader))
                            try { party.RecentEventsMorale += 60f; } catch { }
                    }
                }
                catch { }

                string troopStr = troopsAdded
                    ? $"{UndyingHostTroopCount:N0} dead-eyed soldiers, each worth ten living men, "
                    : "a host of shapes too numerous to count, ";

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"The Undying Host — The cold has been patient. No more. " +
                    $"From the grey passes and the burned valleys they came: {troopStr}" +
                    $"silent as falling ash, marching without order yet without break. " +
                    $"At their head: {vanguardName}. " +
                    $"No levy will hold them. No castle wall was built high enough. " +
                    $"The Undying Host has begun its march to conquest."));
            }
            catch { }
        }

        // Distributes totalCount troops across tiers 2, 3, and 4 (~⅓ each),
        // so Dead March reinforcements feel like a returning horde rather than
        // an elite vanguard — a mix of conscripts, veterans, and champions.
        private static void AddMixedTierTroops(TroopRoster roster, CultureObject culture, int totalCount)
        {
            int perTier   = totalCount / 3;
            int remainder = totalCount - perTier * 2; // remainder goes to the highest tier

            var tier2 = GetTroopAtTier(culture, 2);
            var tier3 = GetTroopAtTier(culture, 3);
            var tier4 = GetTroopAtTier(culture, 4);

            try { if (tier2 != null) roster.AddToCounts(tier2, perTier);   } catch { }
            try { if (tier3 != null) roster.AddToCounts(tier3, perTier);   } catch { }
            try { if (tier4 != null) roster.AddToCounts(tier4, remainder); } catch { }
        }

        // Walks a culture's elite troop upgrade chain and returns the first troop
        // that reaches targetTier. Falls back to the highest reachable troop if the
        // chain is shorter than targetTier.
        private static CharacterObject GetTroopAtTier(CultureObject culture, int targetTier)
        {
            if (culture == null) return null;
            try
            {
                var troop = culture.EliteBasicTroop ?? culture.BasicTroop;
                if (troop == null) return null;
                for (int i = 0; i < 10; i++)
                {
                    if (troop.Tier >= targetTier) return troop;
                    if (troop.UpgradeTargets == null || troop.UpgradeTargets.Length == 0) break;
                    troop = troop.UpgradeTargets[0];
                }
                return troop;
            }
            catch { return null; }
        }

        // Spawns a looter party of `troopCount` near `anchorPos` using the
        // same hideout-safe pattern as SpawnAshenSpawnParty.
        private static MobileParty SpawnLooterParty(Vec2 anchorPos, int troopCount)
        {
            try
            {
                Clan banditClan = Clan.BanditFactions.FirstOrDefault(c => c != null && !c.IsEliminated);
                if (banditClan == null) return null;

                var pt = banditClan.DefaultPartyTemplate;
                if (pt == null) return null;

                Hideout hideout = null;
                try
                {
                    Settlement hs = banditClan.Settlements.FirstOrDefault(s => s?.Hideout != null);
                    if (hs == null)
                        hs = Settlement.All
                            .Where(s => s?.Hideout != null)
                            .OrderBy(s => (s.GetPosition2D.x - anchorPos.x) * (s.GetPosition2D.x - anchorPos.x)
                                        + (s.GetPosition2D.y - anchorPos.y) * (s.GetPosition2D.y - anchorPos.y))
                            .FirstOrDefault();
                    if (hs == null) hs = Settlement.All.FirstOrDefault(s => s?.Hideout != null);
                    hideout = hs?.Hideout;
                }
                catch { }
                if (hideout == null) return null;

                const float scatter = 5f;
                Vec2 sp = anchorPos + new Vec2(
                    (float)(_rng.NextDouble() - 0.5) * scatter * 2f,
                    (float)(_rng.NextDouble() - 0.5) * scatter * 2f);
                var cv = new CampaignVec2(sp, true);

                string pid = "peasant_unrest_" + _rng.Next(999999).ToString("D6");
                MobileParty party = BanditPartyComponent.CreateBanditParty(pid, banditClan, hideout, false, pt, cv);
                if (party == null) return null;

                CharacterObject troop =
                    MBObjectManager.Instance.GetObject<CharacterObject>("looter")
                 ?? MBObjectManager.Instance.GetObject<CharacterObject>("mountain_bandit");
                if (troop == null) return null;

                party.MemberRoster.AddToCounts(troop, troopCount);
                return party;
            }
            catch { return null; }
        }

        // Returns true when a settlement is a safe candidate for The Temple's founding city.
        private static bool IsValidTempleCity(Settlement s)
        {
            if (s.OwnerClan == null || s.OwnerClan.IsEliminated) return false;
            if (s.OwnerClan.Leader == null || !s.OwnerClan.Leader.IsAlive) return false;
            if (s.OwnerClan.Leader.IsChild)   return false;
            if (s.OwnerClan.Leader == Hero.MainHero) return false;   // player's clan must opt in, not be forced out
            if (s.OwnerClan.Kingdom == null)  return false;
            if (s.OwnerClan.Kingdom.StringId == AshenKingdomId) return false;
            if (s.OwnerClan == s.OwnerClan.Kingdom.RulingClan) return false;  // don't behead the source faction
            if (s.IsUnderSiege) return false;
            if (s.OwnerClan.Heroes.Count(h => h.IsAlive && !h.IsChild) < 2) return false;
            return true;
        }

    }
}
