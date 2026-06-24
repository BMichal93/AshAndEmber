// =============================================================================
// ASH AND EMBER — AshenCitySystem.Maintenance.cs
// War, garrison, town health, gold, recovery, extinction, crime upkeep.
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
        // ── War maintenance ───────────────────────────────────────────────────
        // Called from the MakePeace campaign event. Resets the war throttle to 0
        // so the next daily tick immediately re-declares war — no DeclareWarAction
        // call here because that crashes during save loading.
        public static void OnPeaceMade(IFaction faction1, IFaction faction2)
        {
            // Re-fetch kingdom reference if null (happens after save/load before daily tick)
            if (_ashenKingdom == null)
                _ashenKingdom = Kingdom.All.FirstOrDefault(k => k.StringId == AshenKingdomId);
            if (!IsAshenFaction(faction1) && !IsAshenFaction(faction2)) return;

            // Refund the influence the non-Ashen side spent — peace with the Ashen
            // can never stick, so the AI should not be permanently drained by it.
            try
            {
                IFaction other = IsAshenFaction(faction1) ? faction2 : faction1;
                var otherKingdom = other as Kingdom;
                var rulingClan = otherKingdom?.RulingClan;
                if (rulingClan != null) rulingClan.Influence += 100f;
            }
            catch { }

            _warThrottle = 0;
        }

        // Re-entrancy guard prevents a rapid peace→war→peace loop: if Bannerlord's
        // diplomacy AI fires MakePeace while we are already inside DeclareWar, we
        // skip the nested call rather than cascading into a CPU spike.
        public static void DeclareWarWithAllKingdoms()
        {
            if (_declaringWar) return;
            if (_ashenKingdom == null)
                _ashenKingdom = Kingdom.All.FirstOrDefault(k => k.StringId == AshenKingdomId);
            if (_ashenKingdom == null) return;
            if (_ashenKingdom.IsEliminated)
                try { _ashenKingdom.ReactivateKingdom(); } catch { }
            _declaringWar = true;
            try
            {
                foreach (Kingdom k in Kingdom.All.ToList())
                {
                    if (k == _ashenKingdom || k.IsEliminated) continue;
                    // While Ashen clans are merged into Arenicos's empire, the two factions
                    // are allied — declaring war on the empire would expel the Ashen clans from it.
                    if (BurningLabQuestSystem.AshenMergedWithArenicos
                        && BurningLabQuestSystem.ArenicosEmpireId != null
                        && k.StringId == BurningLabQuestSystem.ArenicosEmpireId) continue;
                    if (!_ashenKingdom.IsAtWarWith(k))
                        try { DeclareWarAction.ApplyByDefault(_ashenKingdom, k); } catch { }
                }
            }
            catch { }
            finally { _declaringWar = false; }
        }

        // ── Garrison maintenance ──────────────────────────────────────────────
        private static void EnsureGarrison(Settlement settlement)
        {
            if (settlement?.Town == null) return;
            var garrison = settlement.Town.GarrisonParty;
            if (garrison?.Party == null) return;

            int min     = settlement.IsTown ? MinGarrisonCity : MinGarrisonCastle;
            int current = garrison.MemberRoster.TotalManCount;
            if (current >= min) return;

            // Walk the first upgrade path to find the highest-tier troop
            var troop = GetHighestTierTroop(settlement.Culture);
            if (troop == null) return;

            garrison.MemberRoster.AddToCounts(troop, min - current);
        }

        private static CharacterObject GetHighestTierTroop(CultureObject culture)
        {
            // Force Sturgian troops so all Ashen garrisons share the same unit theme as Tyal.
            culture = MBObjectManager.Instance.GetObject<CultureObject>("sturgia") ?? culture;
            if (culture == null) return null;
            try
            {
                var troop = culture.EliteBasicTroop ?? culture.BasicTroop;
                if (troop == null) return null;
                // Walk the first upgrade path to reach max tier
                for (int i = 0; i < 10; i++)
                {
                    if (troop.UpgradeTargets == null || troop.UpgradeTargets.Length == 0) break;
                    troop = troop.UpgradeTargets[0];
                }
                return troop;
            }
            catch { return null; }
        }

        private static void RefillGarrisons()
        {
            foreach (var kvp in _settlementClanMap.ToList())
            {
                try
                {
                    var s = Settlement.All.FirstOrDefault(x => x.StringId == kvp.Key);
                    if (s == null) continue;
                    // Only refill while the settlement is still Ashen-owned
                    if (!_ashenClanIds.Contains(s.OwnerClan?.StringId ?? "")) continue;
                    EnsureGarrison(s);
                }
                catch { }
            }
        }

        // ── Town satisfaction / food maintenance ──────────────────────────────
        // Ashen villages are permanently looted (no food production), so Ashen
        // towns would otherwise starve, lose loyalty, and trigger rebellions.
        // Locks all vital settlement stats for Ashen-owned towns/castles daily.
        // Pure float assignments — no campaign events are fired, crash-safe.
        // Skips any settlement that has already changed hands so the system
        // never touches a conquered city that is pending map-removal.
        private static void MaintainAshenTownHealth()
        {
            foreach (var kvp in _settlementClanMap.ToList())
            {
                try
                {
                    var s = Settlement.All.FirstOrDefault(x => x.StringId == kvp.Key);
                    if (s == null || s.Town == null) continue;
                    if (!s.IsTown && !s.IsCastle) continue;
                    // Guard: only maintain while still Ashen-owned
                    if (!_ashenClanIds.Contains(s.OwnerClan?.StringId ?? "")) continue;

                    Town t = s.Town;

                    // Food — prevent starvation from looted villages
                    try
                    {
                        float cap = t.FoodStocksUpperLimit();
                        if (cap > 0f && t.FoodStocks < cap) t.FoodStocks = cap;
                    }
                    catch { }

                    // Loyalty + security — never rebel
                    try { if (t.Security < 100f) t.Security = 100f; } catch { }
                    try { if (t.Loyalty  < 100f) t.Loyalty  = 100f; } catch { }

                    // Prosperity — ensures full tax, recruitment, and militia growth
                    try { if (t.Prosperity < 5000f) t.Prosperity = 5000f; } catch { }
                }
                catch { }
            }
        }

        // ── Hero gold maintenance ─────────────────────────────────────────────
        private static void RefillHeroGold()
        {
            foreach (string clanId in _ashenClanIds)
            {
                try
                {
                    var clan = Clan.All.FirstOrDefault(c => c.StringId == clanId);
                    if (clan == null) continue;
                    foreach (Hero h in clan.Heroes.Where(h => h.IsAlive).ToList())
                        if (h.Gold < MinHeroGold)
                            try { h.ChangeHeroGold(MinHeroGold - h.Gold); } catch { }
                }
                catch { }
            }
        }

        // ── Kingdom health ────────────────────────────────────────────────────
        private static void EnsureKingdomAlive()
        {
            if (_ashenKingdom == null)
            {
                _ashenKingdom = Kingdom.All.FirstOrDefault(k => k.StringId == AshenKingdomId);
                return;
            }
            if (_ashenKingdom.IsEliminated)
            {
                // While all Ashen clans are merged into Arenicos's empire the kingdom is
                // intentionally empty. Reactivating it would trigger war re-declarations
                // against the empire and cause Bannerlord to expel the Ashen clans from it.
                if (BurningLabQuestSystem.AshenMergedWithArenicos) return;
                try { _ashenKingdom.ReactivateKingdom(); } catch { }
            }
        }

        // ── Settlement ownership recovery ──────────────────────────────────────
        // Runs every RecoveryInterval days. Detects settlements that are no longer
        // owned by an Ashen clan.
        //
        // KEY DESIGN: if another faction legitimately conquered the settlement
        // (not under siege, owned by non-Ashen clan), we RELEASE it permanently —
        // we do NOT reclaim it. The only exception is intra-Ashen redistribution
        // (fief moved to a different Ashen clan), which we silently remap.
        //
        // After processing, CheckAshenExtinction fires to give the Ashen a new
        // foothold if they have been completely dispossessed.
        private static void TickSettlementRecovery()
        {
            foreach (var kvp in _settlementClanMap.ToList())
            {
                try
                {
                    var settlement = Settlement.All.FirstOrDefault(s => s.StringId == kvp.Key);
                    if (settlement == null) { _settlementClanMap.Remove(kvp.Key); continue; }

                    string currentClanId = settlement.OwnerClan?.StringId ?? "";
                    if (currentClanId == kvp.Value) continue; // still correct — no action

                    if (settlement.IsUnderSiege) continue; // battle in progress — wait

                    // Fief redistributed within Ashen kingdom (different Ashen clan) — remap
                    if (_ashenClanIds.Contains(currentClanId))
                    {
                        _settlementClanMap[kvp.Key] = currentClanId;
                        continue;
                    }

                    // Legitimately conquered by an outside faction — release permanently
                    _settlementClanMap.Remove(kvp.Key);
                    try
                    {
                        // Stabilise so the new owner doesn't face an instant rebellion
                        if (settlement.Town != null)
                        {
                            settlement.Town.Loyalty  = 100f;
                            settlement.Town.Security = 100f;
                        }
                        MBInformationManager.AddQuickInformation(new TextObject(
                            $"{settlement.Name} — wrested from the Ashen. The cold retreats there, for now."));
                    }
                    catch { }

                    return; // one release per tick — remainder handled on subsequent days
                }
                catch { }
            }

            CheckAshenExtinction();
        }

        // ── Confinement guard ──────────────────────────────────────────────────
        // The Ashen realm is EXACTLY the renamed target set — never any other town
        // (see _targetSettlementNames). Nothing stops the permanently-warring Ashen
        // from besieging and holding ordinary frontier towns next to their capital
        // (e.g. Ocs Hall, Rovalt, Car Banseth, which sit a short ride from Ostican).
        // This hands any such conquered, non-target settlement back to a defensible
        // kingdom of its own culture so the cold cannot spread beyond its set.
        private static void ReleaseNonTargetSettlements()
        {
            if (_ashenKingdom == null || _ashenKingdom.IsEliminated) return;
            int released = 0;
            try
            {
                foreach (var s in Settlement.All.ToList())
                {
                    if (released >= 2) break;            // at most a couple of handovers per day
                    try
                    {
                        if (!(s.IsTown || s.IsCastle)) continue;           // villages follow their bound town
                        if (s.MapFaction?.StringId != AshenKingdomId) continue;
                        if (IsTargetSettlement(s)) continue;                // legitimately Ashen
                        if (s.IsUnderSiege) continue;                       // wait out an active battle
                        if (s.OwnerClan?.Leader == Hero.MainHero) continue; // never touch the player's fiefs

                        Hero recipient = FindNonAshenLeaderForCulture(s.Culture);
                        if (recipient == null) continue;

                        ChangeOwnerOfSettlementAction.ApplyByDefault(recipient, s);
                        if (s.Town != null) { s.Town.Loyalty = 100f; s.Town.Security = 100f; }
                        MBInformationManager.AddQuickInformation(new TextObject(
                            $"{s.Name} — the cold is driven back. The town returns to its own."));
                        released++;
                    }
                    catch { }
                }
            }
            catch { }
        }

        // A non-Ashen kingdom leader to receive a released town — preferring one of the
        // settlement's own culture so it returns to a faction that can hold it.
        private static Hero FindNonAshenLeaderForCulture(CultureObject culture)
        {
            try
            {
                var k = Kingdom.All.FirstOrDefault(x => !x.IsEliminated
                    && x.StringId != AshenKingdomId && x.Culture == culture && x.Leader != null);
                if (k?.Leader != null) return k.Leader;
                k = Kingdom.All.FirstOrDefault(x => !x.IsEliminated
                    && x.StringId != AshenKingdomId && x.Leader != null);
                return k?.Leader;
            }
            catch { return null; }
        }

        // ── Ashen extinction guard ─────────────────────────────────────────────
        // If the Ashen have been completely dispossessed, assign one random
        // non-player town to them so they always maintain a foothold on the map.
        private static void CheckAshenExtinction()
        {
            if (_settlementClanMap.Count > 0) return;
            if (DragonQuestSystem.WorldRekindled) return;

            // Need at least one living, free Ashen lord to claim the settlement
            Hero ashenLord = null;
            try
            {
                ashenLord = Hero.AllAliveHeroes.FirstOrDefault(h =>
                    h.IsAlive && !h.IsDisabled && !h.IsPrisoner && IsAshenClanMember(h));
            }
            catch { }
            if (ashenLord == null) return;

            // Reclaim only a settlement that BELONGS to the Ashen by design — one of
            // the renamed target cities. The Ashen must never spread to ordinary
            // Empire/Battanian/etc. towns (e.g. Ocs Hall): their realm is exactly the
            // renamed set, no more.
            Settlement target = null;
            try
            {
                var candidates = Settlement.All
                    .Where(s => s.IsTown && !s.IsUnderSiege
                             && s.MapFaction?.StringId != AshenKingdomId
                             && IsTargetSettlement(s)
                             && s.OwnerClan?.Leader != Hero.MainHero
                             && s.MapFaction != Hero.MainHero?.MapFaction)
                    .ToList();
                if (candidates.Count > 0)
                    target = candidates[_rng.Next(candidates.Count)];
            }
            catch { }
            if (target == null) return;
            if (ashenLord.Clan == null) return; // detached hero — cannot anchor a settlement

            try
            {
                ChangeOwnerOfSettlementAction.ApplyByDefault(ashenLord, target);
                try { if (target.Town != null) { target.Town.Loyalty = 100f; target.Town.Security = 100f; } } catch { }
                _settlementClanMap[target.StringId] = ashenLord.Clan.StringId;
                try { EnsureGarrison(target); } catch { }

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"The grey fire resurges — {target.Name} falls to the Ashen without warning. The cold claims new ground."));
            }
            catch { }
        }

        // ── Criminal status ───────────────────────────────────────────────────
        // Non-Ashen players are permanent criminals in Ashen lands; Ashen players
        // are welcomed (crime rating reset to 0).
        private static void MaintainCriminalStatus()
        {
            if (_ashenKingdom == null) return;
            try
            {
                bool playerIsAshen = MageKnowledge.IsAshen;
                float current = _ashenKingdom.MainHeroCrimeRating;
                if (playerIsAshen)
                {
                    if (current > 0f)
                        ChangeCrimeRatingAction.Apply(_ashenKingdom, -current, false);
                }
                else
                {
                    if (current < 100f)
                        ChangeCrimeRatingAction.Apply(_ashenKingdom, 100f - current, false);
                }
            }
            catch { }
        }
    }
}
