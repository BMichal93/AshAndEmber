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
//   - Settlement recovery: if a settlement is not owned by the expected Ashen
//     clan (initial setup or post-conquest), reclaims it on the next daily tick.
//   - Blocks natural aging of Ashen heroes.
//
// Daily maintenance also includes:
//   - TickAshenClanKingdoms: ejects Ashen clans from foreign kingdoms and
//     re-adds them to the Ashen kingdom. Done here (not in OnClanChangedKingdom)
//     to avoid re-entrancy: calling ApplyByLeaveKingdom inside ClanChangedKingdom
//     fires the same event again and can crash the campaign state.
//
// Event hook (ClanChangedKingdom):
//   - Intentionally empty (no-op). See TickAshenClanKingdoms above.
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
        private static int      _appearanceDayCounter = 0;
        private static bool     _declaringWar        = false;
        private static bool     _ownershipInitDone   = false;
        private static readonly Random _rng  = new Random();
        private const  int      AppearanceTickInterval = 30;

        // ── Throttle counters (not saved — reset in Save/Reset) ───────────────
        // Each counter decrements daily; the operation fires when it reaches 0
        // and is then reset to its interval. In Save() they are set to their
        // grace values so heavy actions never run on the first ticks after load.
        private static int _warThrottle      = 0;  // DeclareWar  — every 5 days
        private static int _clanThrottle     = 0;  // ClanKingdom — every 3 days
        private static int _villageThrottle  = 0;  // Villages    — every 7 days
        private static int _recoveryThrottle = 0;  // Settlement  — every 3 days
        private static int _prisonerThrottle = 0;  // Prisoners   — every 2 days
        private const  int WarInterval      = 5;
        private const  int ClanInterval     = 3;
        private const  int VillageInterval  = 7;
        private const  int RecoveryInterval = 3;
        private const  int PrisonerInterval = 2;

        private const int    MinGarrisonCity   = 1500; // overwhelming standing army
        private const int    MinGarrisonCastle = 800;  // heavy castle garrison
        private const int    MinHeroGold       = 150_000;
        private const string AshenKingdomId    = "ashen_kingdom";

        private static readonly string[] _targetSettlementNames =
        {
            // Core Ashen cities
            "Tyal", "Sibir", "Baltakhand", "Amprela",
            // Original castles
            "Urikskala", "Kaysar", "Dinar", "Vladiv",
            // Extended Ashen zone (nearby towns & castles — skipped automatically
            // if their clan also owns settlements outside this list)
            "Varnovapol", "Tepes", "Epinosa", "Takor", "Khimli",
            // Castles near Amprela (Lochana ~27, Syratos ~44; Epinosa already above)
            "Lochana", "Syratos",
            // Ostican (Vlandian) — assigned at game start; daily tick enforces retention
            "Ostican",
        };

        public static void ResetForNewGame()
        {
            _initialized       = false;
            _ashenKingdom      = null;
            _ownershipInitDone = false;
            _declaringWar      = false;
            _ashenClanIds.Clear();
            _settlementClanMap.Clear();
            _conqueredDays.Clear();
            _appearanceDayCounter = 0;
            _warThrottle      = 0;
            _clanThrottle     = 0;
            _villageThrottle  = 0;
            _recoveryThrottle = 0;
            _prisonerThrottle = 0;
        }

        // ── Initialization ────────────────────────────────────────────────────
        public static void Initialize()
        {
            if (_initialized) return;

            bool foundAny = false;
            Settlement homeSettlement = null;

            // First pass: process settlements whose clans exclusively own Ashen settlements.
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
                    // those extra settlements (Vercheng, Balagad, etc.) must not be dragged in.
                    bool hasNonTarget = clan.Settlements.Any(s =>
                        (s.IsTown || s.IsCastle) &&
                        !_targetSettlementNames.Any(n =>
                            s.Name.ToString().IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0));
                    if (hasNonTarget) continue;

                    if (homeSettlement == null) homeSettlement = settlement;
                    if (_ashenKingdom == null) CreateOrFindAshenKingdom(homeSettlement);

                    if (_ashenClanIds.Add(clan.StringId))
                    {
                        _settlementClanMap[settlement.StringId] = clan.StringId;
                        MarkClanAshen(clan, settlement);
                        foundAny = true;
                    }
                }
                catch { }
            }

            // Second pass: any target settlement whose clan failed the check above is still
            // claimed — transfer it directly to the first Ashen clan so it doesn't float free
            // and get snatched by another faction.
            if (_ashenClanIds.Count > 0)
            {
                var ashenClan = Clan.All.FirstOrDefault(c => _ashenClanIds.Contains(c.StringId) && !c.IsEliminated);
                if (ashenClan != null)
                {
                    Hero ashenLord = ashenClan.Leader
                                  ?? ashenClan.Heroes.FirstOrDefault(h => h.IsAlive && !h.IsDisabled);

                    foreach (string name in _targetSettlementNames)
                    {
                        try
                        {
                            var settlement = Settlement.All.FirstOrDefault(s =>
                                s.Name.ToString().IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                            if (settlement == null) continue;
                            if (_settlementClanMap.ContainsKey(settlement.StringId)) continue;

                            _settlementClanMap[settlement.StringId] = ashenClan.StringId;
                            if (ashenLord != null)
                            {
                                try { ChangeOwnerOfSettlementAction.ApplyByDefault(ashenLord, settlement); } catch { }
                                try { if (settlement.Town != null) { settlement.Town.Loyalty = 100f; settlement.Town.Security = 100f; } } catch { }
                            }
                            try { EnsureGarrison(settlement); } catch { }
                            foundAny = true;
                        }
                        catch { }
                    }
                }
            }

            if (foundAny || _ashenKingdom != null)
            {
                try { DeclareWarWithAllKingdoms(); } catch { }
                try { ApplyAshenLookToSettlementHeroes(); } catch { }
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

        // Sets the three personality traits that define every Ashen lord:
        // Merciless (Mercy −2), Closefisted (Generosity −2), Deceitful (Honor −2).
        // Called whenever any hero joins the Ashen — including via events.
        public static void ApplyAshenPersonality(Hero hero)
        {
            if (hero == null) return;
            try { hero.SetTraitLevel(DefaultTraits.Mercy,      -2); } catch { }
            try { hero.SetTraitLevel(DefaultTraits.Generosity, -2); } catch { }
            try { hero.SetTraitLevel(DefaultTraits.Honor,      -2); } catch { }
        }

        // ── Called by ColourLordRegistry.SetAshen for every hero turned Ashen ──
        // Moves the hero's clan into the Ashen kingdom. Safe to call at any time:
        // if the kingdom isn't ready yet the clan is simply ejected (Initialize
        // will re-join them on its next run).
        public static void OnHeroSetAshen(Hero hero)
        {
            var clan = hero?.Clan;
            if (clan == null || hero == Hero.MainHero) return;
            try { ApplyAshenPersonality(hero); } catch { }
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
                    {
                        try { ChangeOwnerOfSettlementAction.ApplyByDefault(lord, settlement); } catch { }
                        try { if (settlement.Town != null) { settlement.Town.Loyalty = 100f; settlement.Town.Security = 100f; } } catch { }
                    }
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
                IFaction other = faction1 == _ashenKingdom ? faction2 : faction1;
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
                try { _ashenKingdom.ReactivateKingdom(); } catch { }
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

            // Pick a random town not already controlled by the Ashen or the player
            Settlement target = null;
            try
            {
                var candidates = Settlement.All
                    .Where(s => s.IsTown && !s.IsUnderSiege
                             && s.MapFaction?.StringId != AshenKingdomId
                             && s.OwnerClan?.Leader != Hero.MainHero
                             && s.MapFaction != Hero.MainHero?.MapFaction)
                    .ToList();
                if (candidates.Count > 0)
                    target = candidates[_rng.Next(candidates.Count)];
            }
            catch { }
            if (target == null) return;

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
                ("Charas",    "empire_w"),
                ("Sargot",    "empire_w"),
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

        // ── Prisoner fate ─────────────────────────────────────────────────────
        // For the player: queue a deferred choice (join Ashen vs die).
        // For NPC lords: 20% become Ashen, 40% flee, 40% executed.
        public static void ExecuteAshenPrisoners()
        {
            if (_ashenClanIds.Count == 0) return;
            // At most 1 heavy action (kill/release/convert) per call to avoid
            // cascading KillCharacterAction calls on a single daily tick.
            try
            {
                foreach (Hero hero in Hero.AllAliveHeroes.ToList())
                {
                    try
                    {
                        if (!hero.IsPrisoner || !hero.IsLord || hero.IsChild) continue;

                        var captorParty = hero.PartyBelongedToAsPrisoner;
                        if (captorParty == null) continue;

                        bool captorIsAshen =
                            (captorParty.LeaderHero != null &&
                             ColourLordRegistry.IsAshenLord(captorParty.LeaderHero)) ||
                            captorParty.MapFaction?.StringId == AshenKingdomId;
                        if (!captorIsAshen) continue;

                        if (hero == Hero.MainHero)
                        {
                            if (MageKnowledge._deferredInquiry == null)
                            {
                                Hero exec = captorParty.LeaderHero;
                                MageKnowledge._deferredInquiry = () => ShowAshenCapturePrompt(exec);
                            }
                            continue;
                        }

                        // NPC lord: roll fate
                        double roll = _rng.NextDouble();
                        if (roll < 0.20)
                        {
                            try { ColourLordRegistry.SetAshen(hero, true); } catch { }
                            try { EndCaptivityAction.ApplyByReleasedAfterBattle(hero); } catch { }
                        }
                        else if (roll < 0.60)
                        {
                            try { EndCaptivityAction.ApplyByReleasedAfterBattle(hero); } catch { }
                        }
                        else
                        {
                            Hero executor = captorParty.LeaderHero
                                         ?? Hero.AllAliveHeroes.FirstOrDefault(h =>
                                                h.IsAlive && !h.IsDisabled && !h.IsPrisoner &&
                                                _ashenClanIds.Contains(h.Clan?.StringId));
                            if (executor == null) continue;
                            try { KillCharacterAction.ApplyByExecution(hero, executor); } catch { }
                        }

                        return; // one action per tick — process remaining on next call
                    }
                    catch { }
                }
            }
            catch { }
        }

        // ── Player capture prompt ─────────────────────────────────────────────
        private static void ShowAshenCapturePrompt(Hero captor)
        {
            string captorName = captor?.Name.ToString() ?? "the Ashen lord";

            InformationManager.ShowInquiry(new InquiryData(
                "The Grey Lords",

                $"{captorName} crouches to your level. The battlefield has gone quiet — your men are dead or fled. " +
                "They study you the way fire studies wood.\n\n" +
                "When they speak, their voice carries the cold of something very old.\n\n" +
                "\"You burned well,\" they say. \"That kind of fire does not simply go out. " +
                "It changes. We have seen it many times.\"\n\n" +
                "They extend a hand. The skin is grey-white, faintly luminous. Like ash that has forgotten it was ever warm.\n\n" +
                "\"Take the cold. Or do not. Both are a kind of ending.\"",

                true, true,
                "Take the cold. Let it have me.",
                "I have lived long enough.",

                () =>
                {
                    // Join the Ashen
                    try
                    {
                        MageKnowledge.SetAshen(true);
                        try { MageKnowledge.ApplyAshenAppearance(Hero.MainHero); } catch { }
                        try { AshenCitySystem.OnPlayerBecameAshen(); } catch { }
                        try { EndCaptivityAction.ApplyByReleasedAfterBattle(Hero.MainHero); } catch { }
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Something ancient settles in you. The warmth you have always carried shifts — " +
                            "not gone, but changed. Cold fire. Grey flame. You are still here. " +
                            "But something that was purely yours is no longer.",
                            new Color(0.35f, 0.35f, 0.75f)));
                    }
                    catch { }
                },

                () =>
                {
                    // Accept execution
                    try
                    {
                        if (captor != null)
                            try { KillCharacterAction.ApplyByExecution(Hero.MainHero, captor); } catch { }
                        else
                            try { KillCharacterAction.ApplyByMurder(Hero.MainHero, null, true); } catch { }
                    }
                    catch { }
                }
            ), true, true);
        }

        // ── Daily tick ────────────────────────────────────────────────────────
        public static void DailyTick()
        {
            if (_ashenClanIds.Count == 0) return;
            // Once the world is rekindled, the Ashen cease to exist.
            if (DragonQuestSystem.WorldRekindled) return;

            EnsureKingdomAlive();

            // Decrement throttle counters (only when above zero)
            if (_clanThrottle     > 0) _clanThrottle--;
            if (_warThrottle      > 0) _warThrottle--;
            if (_villageThrottle  > 0) _villageThrottle--;
            if (_recoveryThrottle > 0) _recoveryThrottle--;
            if (_prisonerThrottle > 0) _prisonerThrottle--;

            // Clan kingdom enforcement — every ClanInterval days
            if (_clanThrottle == 0)
            {
                TickAshenClanKingdoms();
                _clanThrottle = ClanInterval;
            }

            // War declarations — every WarInterval days
            if (_warThrottle == 0)
            {
                DeclareWarWithAllKingdoms();
                _warThrottle = WarInterval;
            }

            // One-time ownership initialisation
            if (!_ownershipInitDone)
            {
                try { InitialiseSettlementOwnership(); } catch { }
                _ownershipInitDone = true;
            }

            // Fast daily ops (idempotent, low cost)
            RefillGarrisons();
            RefillHeroGold();
            MaintainAshenTownHealth();
            MaintainCriminalStatus();

            // Settlement recovery — every RecoveryInterval days, max 1 change per tick
            if (_recoveryThrottle == 0)
            {
                TickSettlementRecovery();
                _recoveryThrottle = RecoveryInterval;
            }

            // Village loot state — every VillageInterval days
            if (_villageThrottle == 0)
            {
                TickAshenVillages();
                _villageThrottle = VillageInterval;
            }

            // Prisoner fate — every PrisonerInterval days, max 1 action per tick
            if (_prisonerThrottle == 0)
            {
                ExecuteAshenPrisoners();
                _prisonerThrottle = PrisonerInterval;
            }

            if (++_appearanceDayCounter >= AppearanceTickInterval)
            {
                _appearanceDayCounter = 0;
                ApplyAshenLookToSettlementHeroes();
            }

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
        // Intentionally empty: calling ApplyByLeaveKingdom here would fire the
        // ClanChangedKingdom event again (re-entrancy). Ejection is handled
        // instead by TickAshenClanKingdoms(), which runs on the daily tick.
        public static void OnClanChangedKingdom(Clan clan, Kingdom oldKingdom, Kingdom newKingdom,
            ChangeKingdomAction.ChangeKingdomActionDetail detail, bool showNotification) { }

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

        // Returns true for both the Ashen kingdom and any individual Ashen clan
        // that is temporarily outside the kingdom — used by AshenDiplomacyModel.
        public static bool IsAshenFaction(IFaction f)
        {
            if (f == null) return false;
            return f.StringId == AshenKingdomId || _ashenClanIds.Contains(f.StringId);
        }

        public static bool IsAshenSettlement(Settlement settlement) =>
            settlement != null && _settlementClanMap.ContainsKey(settlement.StringId);

        // ── Save / Load ───────────────────────────────────────────────────────
        public static void Save(IDataStore store)
        {
            var ids   = _ashenClanIds.ToList();
            bool init    = _initialized;
            bool ownDone = _ownershipInitDone;
            store.SyncData("LDM_AshenClanIds",      ref ids);
            store.SyncData("LDM_AshenCityInit",     ref init);
            store.SyncData("LDM_OwnershipInitDone", ref ownDone);

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
            _initialized        = init;
            _ownershipInitDone  = ownDone;

            _settlementClanMap.Clear();
            if (sKeys != null && sVals != null)
                for (int i = 0; i < Math.Min(sKeys.Count, sVals.Count); i++)
                    _settlementClanMap[sKeys[i]] = sVals[i];

            _conqueredDays.Clear();
            if (cKeys != null && cVals != null)
                for (int i = 0; i < Math.Min(cKeys.Count, cVals.Count); i++)
                    _conqueredDays[cKeys[i]] = cVals[i];

            // Clear Kingdom reference so it is always re-fetched from live
            // Kingdom.All — stale references from a previous session cause a
            // native crash when accessing .IsEliminated on a dead object.
            _ashenKingdom  = null;
            _declaringWar  = false;

            // Set grace periods so heavy campaign actions never fire on the
            // first daily ticks after loading (avoids stacking ChangeOwner /
            // DeclareWar / KillCharacter calls during game initialization).
            _warThrottle      = 2;                    // first DeclareWar: day 2
            _clanThrottle     = ClanInterval     + 1; // first ClanKingdom: day 4
            _villageThrottle  = VillageInterval  + 0; // first Village:    day 7
            _recoveryThrottle = RecoveryInterval + 1; // first Recovery:   day 4
            _prisonerThrottle = PrisonerInterval + 5; // first Prisoners:  day 7
        }
    }
}
