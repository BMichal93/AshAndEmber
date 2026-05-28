// =============================================================================
// LIFE & DEATH MAGIC — AI/AshenCitySystem.cs
// Manages the Ashen city clans and their kingdom.
//
// Target settlements: Tyal, Sibir, Baltakhand (cities)
//                     Urikskala, Kaysar, Dinar, Vladiv (castles)
//
// On initialization:
//   1. Finds each target settlement, looks up its owner clan.
//   2. Creates the "ashen_kingdom" Kingdom if it doesn't exist.
//   3. Marks each hero Ashen, renames them, ejects from current kingdom.
//   4. Adds each clan to the Ashen kingdom.
//   5. Restores settlement ownership (prevents fief-distribution snatch).
//   6. Tops up garrisons with high-tier troops.
//   7. Gives each lord starting gold.
//   8. Declares war on every other kingdom.
//
// Daily maintenance:
//   - Ensures the Ashen kingdom is alive; reactivates if eliminated.
//   - Redeclares war with any kingdom that made peace.
//   - Refills garrisons below the minimum threshold.
//   - Refills hero gold below the minimum threshold.
//   - 7-day settlement recovery: if a settlement was conquered, restores it
//     after 7 days (fallback — the kingdom's own AI should retake it first).
//   - Blocks natural aging of Ashen heroes.
//
// Event hook (ClanChangedKingdom):
//   - Ejects Ashen clans that try to join a non-Ashen kingdom.
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
    public static class AshenCitySystem
    {
        private static readonly HashSet<string>           _ashenClanIds     = new HashSet<string>();
        private static readonly Dictionary<string,string> _settlementClanMap = new Dictionary<string,string>();
        private static readonly Dictionary<string,int>    _conqueredDays    = new Dictionary<string,int>();
        private static Kingdom  _ashenKingdom = null;
        private static bool     _initialized  = false;

        private const int    MinGarrisonCity   = 500;
        private const int    MinGarrisonCastle = 350;
        private const int    MinHeroGold       = 150_000;
        private const int    RecoveryDays      = 7;
        private const string AshenKingdomId    = "ashen_kingdom";

        private static readonly string[] _targetSettlementNames =
        {
            // Core Ashen cities
            "Tyal", "Sibir", "Baltakhand",
            // Original castles
            "Urikskala", "Kaysar", "Dinar", "Vladiv",
            // Extended Ashen zone (nearby towns & castles — skipped automatically
            // if their clan also owns settlements outside this list)
            "Varnovapol", "Tepes", "Epinosa", "Takor", "Khimli",
        };

        public static void ResetForNewGame()
        {
            _initialized  = false;
            _ashenKingdom = null;
            _ashenClanIds.Clear();
            _settlementClanMap.Clear();
            _conqueredDays.Clear();
        }

        // ── Initialization ────────────────────────────────────────────────────
        public static void Initialize()
        {
            if (_initialized) return;

            bool     foundAny      = false;
            Settlement homeSettlement = null;

            foreach (string name in _targetSettlementNames)
            {
                try
                {
                    var settlement = Settlement.All.FirstOrDefault(s =>
                        s.Name.ToString().IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (settlement == null) continue;

                    Clan clan = settlement.OwnerClan;
                    if (clan == null) continue;

                    // Skip clans that own settlements outside our target list —
                    // moving them to the Ashen kingdom would drag those extra
                    // settlements (Vercheng, Balagad, etc.) in as well.
                    bool hasNonTarget = clan.Settlements.Any(s =>
                        (s.IsTown || s.IsCastle) &&
                        !_targetSettlementNames.Any(n =>
                            s.Name.ToString().IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0));
                    if (hasNonTarget) continue;

                    if (homeSettlement == null) homeSettlement = settlement;

                    // Create the Ashen kingdom on first found settlement
                    if (_ashenKingdom == null)
                        CreateOrFindAshenKingdom(homeSettlement);

                    if (_ashenClanIds.Add(clan.StringId))
                    {
                        _settlementClanMap[settlement.StringId] = clan.StringId;
                        MarkClanAshen(clan, settlement);
                        foundAny = true;
                    }
                }
                catch { }
            }

            if (foundAny)
            {
                try { DeclareWarWithAllKingdoms(); } catch { }
                _initialized = true;
            }
        }

        // ── Kingdom creation ──────────────────────────────────────────────────
        private static void CreateOrFindAshenKingdom(Settlement homeSettlement)
        {
            _ashenKingdom = Kingdom.All.FirstOrDefault(k => k.StringId == AshenKingdomId);
            if (_ashenKingdom != null) return;
            try
            {
                _ashenKingdom = Kingdom.CreateKingdom(AshenKingdomId);
                var culture = homeSettlement.OwnerClan?.Culture
                           ?? MBObjectManager.Instance.GetObject<CultureObject>("sturgia");
                _ashenKingdom.InitializeKingdom(
                    new TextObject("The Ashen"),
                    new TextObject("Ashen"),
                    culture,
                    Banner.CreateRandomBanner(),
                    0xFF1E1E1E,    // ash black
                    0xFF3D1A1A,    // dark ember red
                    homeSettlement,
                    new TextObject("Ancient fire-lords who neither age nor die."),
                    new TextObject("The Ashen"),
                    new TextObject("Ashen Sovereign")
                );
            }
            catch { _ashenKingdom = null; }
        }

        // ── Called by ColourLordRegistry.SetAshen for every hero turned Ashen ──
        // Moves the hero's clan into the Ashen kingdom. Safe to call at any time:
        // if the kingdom isn't ready yet the clan is simply ejected (Initialize
        // will re-join them on its next run).
        public static void OnHeroSetAshen(Hero hero)
        {
            var clan = hero?.Clan;
            if (clan == null || hero == Hero.MainHero) return;
            try
            {
                if (_ashenKingdom == null)
                {
                    // Kingdom not created yet — just eject; daily tick will re-add later
                    if (clan.Kingdom != null)
                        try { ChangeKingdomAction.ApplyByLeaveKingdom(clan, false); } catch { }
                    return;
                }

                if (clan.Kingdom?.StringId == AshenKingdomId) return; // already home

                // Eject from current kingdom first
                if (clan.Kingdom != null)
                    try { ChangeKingdomAction.ApplyByLeaveKingdom(clan, false); } catch { }

                // Use ApplyByCreateKingdom only if the Ashen kingdom has no ruling clan yet;
                // this establishes the first clan as ruler so the kingdom is properly set up.
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
            catch { }
        }

        // ── Clan setup ────────────────────────────────────────────────────────
        // SetAshen → OnHeroSetAshen handles kingdom placement; this method only
        // handles marking/renaming, settlement ownership, gold, and garrison.
        private static void MarkClanAshen(Clan clan, Settlement settlement)
        {
            if (clan == null) return;
            try
            {
                // Mark and rename — SetAshen calls OnHeroSetAshen which joins the Ashen kingdom
                foreach (Hero hero in clan.Heroes.Where(h => h.IsAlive).ToList())
                {
                    try { ColourLordRegistry.SetAshen(hero, true); } catch { }
                    try { RenameAshenHero(hero); } catch { }
                    try { ColourLordRegistry.SetMage(hero, true); } catch { }
                }

                // Re-assert settlement ownership (guards against fief-distribution firing in the gap)
                if (settlement != null)
                {
                    Hero lord = clan.Leader
                             ?? clan.Heroes.FirstOrDefault(h => h.IsAlive && !h.IsDisabled);
                    if (lord != null)
                        try { ChangeOwnerOfSettlementAction.ApplyByDefault(lord, settlement); } catch { }
                }

                // Starting gold
                foreach (Hero hero in clan.Heroes.Where(h => h.IsAlive).ToList())
                    try { if (hero.Gold < MinHeroGold) hero.ChangeHeroGold(MinHeroGold - hero.Gold); } catch { }

                // Garrison boost
                if (settlement != null) try { EnsureGarrison(settlement); } catch { }
            }
            catch { }
        }

        private static void RenameAshenHero(Hero hero)
        {
            if (hero == null) return;
            string title = hero.IsFemale ? "Ashen Lady" : "Ashen Lord";
            try
            {
                hero.SetName(new TextObject(title + " " + hero.Name.ToString()),
                             new TextObject(title));
            }
            catch
            {
                try { hero.SetName(new TextObject(title), new TextObject(title)); } catch { }
            }
        }

        private static void MaxRelationsWithPlayer(Hero hero)
        {
            if (hero == null || Hero.MainHero == null) return;
            try
            {
                int cur = CharacterRelationManager.GetHeroRelation(Hero.MainHero, hero);
                if (cur < 100)
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                        Hero.MainHero, hero, 100 - cur, false);
            }
            catch { }
        }

        // ── War maintenance ───────────────────────────────────────────────────
        private static void DeclareWarWithAllKingdoms()
        {
            if (_ashenKingdom == null) return;
            foreach (Kingdom k in Kingdom.All.ToList())
            {
                if (k == _ashenKingdom || k.IsEliminated) continue;
                if (!_ashenKingdom.IsAtWarWith(k))
                    try { DeclareWarAction.ApplyByDefault(_ashenKingdom, k); } catch { }
            }
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
                    if (s != null) EnsureGarrison(s);
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
                try { _ashenKingdom.ReactivateKingdom(); } catch { }
        }

        // ── 7-day settlement recovery (fallback) ──────────────────────────────
        private static void TickSettlementRecovery()
        {
            foreach (var kvp in _settlementClanMap.ToList())
            {
                try
                {
                    var settlement = Settlement.All.FirstOrDefault(s => s.StringId == kvp.Key);
                    if (settlement == null) continue;

                    if (settlement.OwnerClan?.StringId == kvp.Value)
                    {
                        _conqueredDays.Remove(kvp.Key);
                        continue;
                    }

                    _conqueredDays.TryGetValue(kvp.Key, out int days);
                    days++;

                    if (days >= RecoveryDays)
                    {
                        var clan = Clan.All.FirstOrDefault(c => c.StringId == kvp.Value);
                        if (clan == null || clan.IsEliminated) continue;
                        Hero lord = clan.Leader
                                 ?? clan.Heroes.FirstOrDefault(h => h.IsAlive && !h.IsDisabled);
                        if (lord == null) continue;

                        ChangeOwnerOfSettlementAction.ApplyByDefault(lord, settlement);
                        // Eject from any foreign kingdom the restoration may have triggered
                        if (clan.Kingdom != null && clan.Kingdom.StringId != AshenKingdomId)
                            try { ChangeKingdomAction.ApplyByLeaveKingdom(clan, false); } catch { }
                        // Re-add to Ashen kingdom if lost
                        if (clan.Kingdom?.StringId != AshenKingdomId && _ashenKingdom != null)
                            try { ChangeKingdomAction.ApplyByJoinToKingdom(
                                    clan, _ashenKingdom,
                                    CampaignTime.Now + CampaignTime.Years(1000),
                                    false); }
                            catch { }

                        _conqueredDays.Remove(kvp.Key);
                    }
                    else
                    {
                        _conqueredDays[kvp.Key] = days;
                    }
                }
                catch { }
            }
        }

        // ── Daily tick ────────────────────────────────────────────────────────
        public static void DailyTick()
        {
            if (_ashenClanIds.Count == 0) return;

            EnsureKingdomAlive();
            DeclareWarWithAllKingdoms();
            RefillGarrisons();
            RefillHeroGold();
            TickSettlementRecovery();

            bool playerIsAshen = MageKnowledge.IsAshen;
            try
            {
                foreach (Hero h in Hero.AllAliveHeroes.ToList())
                {
                    if (!IsAshenClanMember(h)) continue;
                    if (!h.IsAlive) continue;
                    // Keep age at 35
                    float targetAge = 35f;
                    float currentAge = h.Age;
                    if (currentAge > targetAge + 0.5f)
                    {
                        float excessDays = (currentAge - targetAge) * 365f;
                        try { h.SetBirthDay(h.BirthDay + CampaignTime.Days(excessDays)); } catch { }
                    }
                    if (playerIsAshen)
                        MaxRelationsWithPlayer(h);
                }
            }
            catch { }
        }

        // ── Kingdom rejoin prevention ─────────────────────────────────────────
        public static void OnClanChangedKingdom(Clan clan, Kingdom oldKingdom, Kingdom newKingdom,
            ChangeKingdomAction.ChangeKingdomActionDetail detail, bool showNotification)
        {
            if (newKingdom == null) return;
            if (clan == null || !_ashenClanIds.Contains(clan.StringId)) return;
            // Joining the Ashen kingdom is fine; joining anything else is not
            if (newKingdom.StringId == AshenKingdomId) return;
            try { ChangeKingdomAction.ApplyByLeaveKingdom(clan, false); } catch { }
        }

        // ── Max relations when player goes Ashen ──────────────────────────────
        public static void OnPlayerBecameAshen()
        {
            if (Hero.MainHero == null) return;
            try
            {
                foreach (Hero h in Hero.AllAliveHeroes.ToList())
                    if (IsAshenClanMember(h))
                        MaxRelationsWithPlayer(h);
            }
            catch { }
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        public static bool IsAshenClanMember(Hero hero)
        {
            if (hero == null || hero.Clan == null) return false;
            return _ashenClanIds.Contains(hero.Clan.StringId);
        }

        // ── Save / Load ───────────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            var ids   = _ashenClanIds.ToList();
            bool init = _initialized;
            store.SyncData("LDM_AshenClanIds",  ref ids);
            store.SyncData("LDM_AshenCityInit", ref init);

            var sKeys = _settlementClanMap.Keys.ToList();
            var sVals = _settlementClanMap.Values.ToList();
            store.SyncData("LDM_AshenSettlementKeys", ref sKeys);
            store.SyncData("LDM_AshenSettlementVals", ref sVals);

            var cKeys = _conqueredDays.Keys.ToList();
            var cVals = _conqueredDays.Values.ToList();
            store.SyncData("LDM_AshenConqueredKeys", ref cKeys);
            store.SyncData("LDM_AshenConqueredVals", ref cVals);

            // Reconstruct in-memory state from the just-synced data
            _ashenClanIds.Clear();
            if (ids != null) foreach (var id in ids) _ashenClanIds.Add(id);
            _initialized = init;

            _settlementClanMap.Clear();
            if (sKeys != null && sVals != null)
                for (int i = 0; i < Math.Min(sKeys.Count, sVals.Count); i++)
                    _settlementClanMap[sKeys[i]] = sVals[i];

            _conqueredDays.Clear();
            if (cKeys != null && cVals != null)
                for (int i = 0; i < Math.Min(cKeys.Count, cVals.Count); i++)
                    _conqueredDays[cKeys[i]] = cVals[i];
        }
    }
}
