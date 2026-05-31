// =============================================================================
// ASH AND EMBER — CampaignMapEvents.cs
// Seven rare world events themed around fire, ash, and fading. Each has an
// independent chance to trigger on the weekly tick.
//
// ┌─────────────────┬──────────────────────────────────────────────────────────┐
// │ Event           │ Effect                                                   │
// ├─────────────────┼──────────────────────────────────────────────────────────┤
// │ Ashen Plague    │ Wounds entire garrison of a random city/castle; spawns   │
// │                 │ several Ashen Spawn parties near the settlement.         │
// ├─────────────────┼──────────────────────────────────────────────────────────┤
// │ Great Withering │ Destroys 80% of a random village hearth, OR halves the  │
// │                 │ prosperity of a random city.                             │
// ├─────────────────┼──────────────────────────────────────────────────────────┤
// │ Ashen March     │ Spawns many Ashen Spawn parties (strength ≥ 70 each)    │
// │                 │ across a random non-Ashen kingdom.                       │
// ├─────────────────┼──────────────────────────────────────────────────────────┤
// │ Long Night      │ Forces campaign light level to Dark for 7 days.         │
// │                 │ SpellEffects.GetCampaignLightLevel() checks IsLongNight()│
// │                 │ NOTE: campaign map visual time is not modified by this   │
// │                 │ event — only the mod's light-level logic is affected.    │
// ├─────────────────┼──────────────────────────────────────────────────────────┤
// │ Ashen Tide      │ A random non-Ashen castle is claimed by a random Ashen  │
// │                 │ lord via ChangeOwnerOfSettlementAction.                  │
// ├─────────────────┼──────────────────────────────────────────────────────────┤
// │ Fire Fades      │ 2–4 non-Ashen lords aged 25–55 (non-clan-leaders) die.  │
// │                 │ Kills via ApplyByMurder; their home settlement weakens. │
// ├─────────────────┼──────────────────────────────────────────────────────────┤
// │ Darkened Roads  │ All caravans in a random kingdom are destroyed via       │
// │                 │ DestroyPartyAction. Ashen-kingdom caravans are immune.   │
// └─────────────────┴──────────────────────────────────────────────────────────┘
//
// DEBUGGING GUIDE:
//   • WeeklyTick() is the entry point — add breakpoints here to trace event rolls.
//   • Each TryFireXxx() rolls _rng.NextDouble() against its ChanceXxx constant.
//   • SpawnAshenSpawnParty() wraps BanditPartyComponent.CreateBanditParty; if
//     that returns null (bandit clan not found), the spawn silently fails.
//   • Long Night state is persisted in "LDM_LongNightDays" save key.
//   • All public constants at the top of the class can be tuned without
//     touching event logic.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static class CampaignMapEvents
    {
        // ── Tuning constants ──────────────────────────────────────────────────
        // Per-week fire probability for each event. Adjust here without
        // touching event logic.
        // Bannerlord has ~12 weekly ticks per year (84 days / 7).
        // Expected fires per year = chance × 12.
        public const float ChanceAshenPlague    = 0.08f;  // ~every 12 weeks  (~4–5× per campaign)
        public const float ChanceGreatWithering = 0.10f;  // ~every 10 weeks  (~5–6× per campaign)
        public const float ChanceAshenMarch     = 0.05f;  // ~every 20 weeks  (~2–3× per campaign)
        public const float ChanceLongNight      = 0.03f;  // ~every 33 weeks  (~1–2× per campaign)
        public const float ChanceAshenTide      = 0.03f;  // ~every 33 weeks  (~1–2× per campaign)
        public const float ChanceFireFades      = 0.015f; // ~every 67 weeks  (rare — once per era)
        public const float ChanceDarkenedRoads  = 0.06f;  // ~every 17 weeks  (~3–4× per campaign)

        // Ashen Plague: parties spawned near the afflicted settlement
        public const int AshenPlagueSpawnCount  = 3;

        // Ashen March: parties spawned across the target kingdom
        public const int AshenMarchPartyCount   = 6;

        // Ashen March: minimum party strength per spawned party
        public const float MinAshenMarchStrength = 70f;

        // Long Night: duration in campaign days
        public const int LongNightDuration = 7;

        private const string AshenKingdomId = "ashen_kingdom";

        // ── Runtime state ─────────────────────────────────────────────────────
        private static int _longNightDaysRemaining = 0;
        private static readonly Random _rng = new Random();

        // ── Public API ────────────────────────────────────────────────────────

        /// Returns true while the Long Night event is active.
        /// Called by SpellEffects.GetCampaignLightLevel() to force Dark.
        public static bool IsLongNight() => _longNightDaysRemaining > 0;

        /// Called from CampaignBehavior.OnDailyTick().
        /// Decrements ongoing timed effects (Long Night).
        public static void DailyTick()
        {
            if (_longNightDaysRemaining > 0)
            {
                _longNightDaysRemaining--;

                // Each day of Long Night bleeds prosperity from every town not under Ashen rule
                try
                {
                    foreach (var s in Settlement.All)
                    {
                        if (!s.IsTown || s.Town == null) continue;
                        if (s.MapFaction?.StringId == AshenKingdomId) continue;
                        s.Town.Prosperity = Math.Max(10f, s.Town.Prosperity - 6f);
                    }
                }
                catch { }

                if (_longNightDaysRemaining == 0)
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "Long Night — the sun rises again. The darkness retreats. But the damage lingers."));
            }
        }

        /// Called from CampaignBehavior.OnWeeklyTick().
        /// Each event rolls independently; multiple can fire the same week.
        public static void WeeklyTick()
        {
            // World events are disabled once the world has been rekindled.
            if (DragonQuestSystem.WorldRekindled) return;

            TryFireAshenPlague();
            TryFireGreatWithering();
            TryFireAshenMarch();
            TryFireLongNight();
            TryFireAshenTide();
            TryFireFireFades();
            TryFireDarkenedRoads();
        }

        /// Resets state for a fresh new game (called from OnNewGameCreated).
        public static void ResetForNewGame()
        {
            _longNightDaysRemaining = 0;
        }

        // ── Event 1: Ashen Plague ─────────────────────────────────────────────
        // Wounds all healthy garrison troops in a random city or castle, then
        // spawns AshenPlagueSpawnCount Ashen Spawn parties near the settlement.
        private static void TryFireAshenPlague()
        {
            if (_rng.NextDouble() >= ChanceAshenPlague) return;
            try
            {
                // Require a settlement with a living garrison
                var candidates = Settlement.All
                    .Where(s => (s.IsTown || s.IsCastle)
                             && s.Town?.GarrisonParty?.MemberRoster?.TotalManCount > 0)
                    .ToList();
                if (candidates.Count == 0) return;

                var target = candidates[_rng.Next(candidates.Count)];
                var garrison = target.Town.GarrisonParty;

                // Wound all healthy (non-hero) garrison troops
                int totalWounded = 0;
                foreach (var entry in garrison.MemberRoster.GetTroopRoster().ToList())
                {
                    if (entry.Character.IsHero) continue;
                    int healthy = entry.Number - entry.WoundedNumber;
                    if (healthy <= 0) continue;
                    try
                    {
                        // AddToCounts(char, countDelta, isHero, woundedDelta)
                        garrison.MemberRoster.AddToCounts(entry.Character, 0, false, healthy);
                        totalWounded += healthy;
                    }
                    catch { }
                }

                // Spawn Ashen Spawn parties near the afflicted settlement
                int spawned = 0;
                for (int i = 0; i < AshenPlagueSpawnCount; i++)
                {
                    var party = SpawnAshenSpawnParty(target.GetPosition2D, baseTroops: 12, minStrength: 0f);
                    if (party != null) spawned++;
                }

                if (totalWounded > 0 || spawned > 0)
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"Ashen Plague — a grey sickness takes the garrison of {target.Name}. " +
                        $"{totalWounded} soldier{(totalWounded != 1 ? "s" : "")} fall to their wounds." +
                        (spawned > 0 ? $" {spawned} Ashen Spawn rise from the dying." : "")));
            }
            catch { }
        }

        // ── Event 2: Great Withering ──────────────────────────────────────────
        // Coin-flip: either a random village loses 80% of its hearth, or a
        // random city loses 50% of its prosperity.
        private static void TryFireGreatWithering()
        {
            if (_rng.NextDouble() >= ChanceGreatWithering) return;
            try
            {
                if (_rng.Next(2) == 0)
                {
                    // Village: reduce hearth to 20% of current (= -80%)
                    var villages = Settlement.All
                        .Where(s => s.IsVillage && s.Village != null && s.Village.Hearth > 20f)
                        .ToList();
                    if (villages.Count == 0) return;

                    var target = villages[_rng.Next(villages.Count)];
                    float before = target.Village.Hearth;
                    target.Village.Hearth = Math.Max(10f, before * 0.20f);

                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"Great Withering — the hearth-fires of {target.Name} gutter and die. " +
                        $"Hearth: {before:F0} → {target.Village.Hearth:F0}."));
                }
                else
                {
                    // City: reduce prosperity by 50%
                    var cities = Settlement.All
                        .Where(s => s.IsTown && s.Town != null && s.Town.Prosperity > 50f)
                        .ToList();
                    if (cities.Count == 0) return;

                    var target = cities[_rng.Next(cities.Count)];
                    float before = target.Town.Prosperity;
                    target.Town.Prosperity = Math.Max(10f, before * 0.50f);

                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"Great Withering — something cold and old passes through {target.Name}. " +
                        $"Prosperity: {before:F0} → {target.Town.Prosperity:F0}."));
                }
            }
            catch { }
        }

        // ── Event 3: Ashen March ──────────────────────────────────────────────
        // Spawns AshenMarchPartyCount Ashen Spawn parties (each with TotalStrength
        // ≥ MinAshenMarchStrength) spread across a random non-Ashen kingdom.
        private static void TryFireAshenMarch()
        {
            if (_rng.NextDouble() >= ChanceAshenMarch) return;
            try
            {
                var kingdoms = Kingdom.All
                    .Where(k => !k.IsEliminated && k.StringId != AshenKingdomId)
                    .ToList();
                if (kingdoms.Count == 0) return;

                var kingdom = kingdoms[_rng.Next(kingdoms.Count)];

                // Collect spawn anchors from the kingdom's towns and castles
                var anchors = Settlement.All
                    .Where(s => s.MapFaction == kingdom && (s.IsTown || s.IsCastle))
                    .Select(s => s.GetPosition2D)
                    .ToList();
                if (anchors.Count == 0) return;

                int spawned = 0;
                for (int i = 0; i < AshenMarchPartyCount; i++)
                {
                    // Distribute parties across settlement anchors
                    var anchor = anchors[i % anchors.Count];
                    var party = SpawnAshenSpawnParty(anchor, baseTroops: 20, minStrength: MinAshenMarchStrength);
                    if (party != null) spawned++;
                }

                if (spawned > 0)
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"Ashen March — {spawned} Ashen Spawn descend upon {kingdom.Name}. " +
                        "The grey tide does not rest."));
            }
            catch { }
        }

        // ── Event 4: Long Night ───────────────────────────────────────────────
        // Forces SpellEffects.GetCampaignLightLevel() to return Dark for
        // LongNightDuration days. Does not modify the campaign clock —
        // only the mod's internal light-level logic is affected.
        // Will not stack: skips if a Long Night is already in progress.
        private static void TryFireLongNight()
        {
            if (_longNightDaysRemaining > 0) return;
            if (_rng.NextDouble() >= ChanceLongNight) return;

            _longNightDaysRemaining = LongNightDuration;

            // Spawn Ashen parties that emerge from the darkness
            int spawned = 0;
            try
            {
                var anchors = Settlement.All
                    .Where(s => s.IsTown && s.MapFaction?.StringId != AshenKingdomId)
                    .Select(s => s.GetPosition2D)
                    .ToList();
                for (int i = 0; i < 3 && anchors.Count > 0; i++)
                {
                    var pos = anchors[_rng.Next(anchors.Count)];
                    var party = SpawnAshenSpawnParty(pos, 14, 50f);
                    if (party != null) spawned++;
                }
            }
            catch { }

            MBInformationManager.AddQuickInformation(new TextObject(
                $"Long Night — the sun does not rise. {LongNightDuration} days of unbroken darkness fall over Calradia. " +
                (spawned > 0 ? $"Ashen shapes pour from the shadow. {spawned} warbands take the roads." : "Something stirs in the dark.")));
        }

        // ── Event 5: Ashen Tide ───────────────────────────────────────────────
        // A random non-Ashen castle is claimed by a random Ashen lord via
        // ChangeOwnerOfSettlementAction.ApplyByDefault. The castle's original
        // clan loses the fief instantly — no siege required.
        private static void TryFireAshenTide()
        {
            if (_rng.NextDouble() >= ChanceAshenTide) return;
            try
            {
                // Target: a castle not already under Ashen control or active siege
                var castles = Settlement.All
                    .Where(s => s.IsCastle
                             && !s.IsUnderSiege
                             && s.OwnerClan?.Kingdom?.StringId != AshenKingdomId)
                    .ToList();
                if (castles.Count == 0) return;

                // Claimant: any living Ashen lord who is not a prisoner
                var ashenLords = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && !h.IsDisabled && !h.IsPrisoner
                             && ColourLordRegistry.IsAshenLord(h))
                    .ToList();
                if (ashenLords.Count == 0) return;

                var castle = castles[_rng.Next(castles.Count)];
                var lord   = ashenLords[_rng.Next(ashenLords.Count)];

                ChangeOwnerOfSettlementAction.ApplyByDefault(lord, castle);
                StabiliseSettlement(castle);

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Ashen Tide — {castle.Name} bends to the cold fire. " +
                    $"{lord.Name} claims it without a blade drawn."));
            }
            catch { }
        }

        // ── Event 6: Fire Fades ───────────────────────────────────────────────
        // 2–4 non-Ashen lords aged 25–55 who are NOT clan leaders are killed.
        // Player hero is always spared. Their home settlement also loses
        // hearth/prosperity as their fire fades from that place too.
        //
        // Safety constraints:
        //   • !IsChild  — Bannerlord's ApplyByOldAge/succession code is not safe
        //                 for child heroes; IsChild is the engine's own flag.
        //   • not clan leader — killing a ruling-clan leader triggers complex
        //                 succession that can corrupt campaign state mid-event.
        //   • ApplyByMurder(null, false) — neutral "mystery death", no killer
        //                 assigned, no notification; avoids old-age succession path.
        private static void TryFireFireFades()
        {
            if (_rng.NextDouble() >= ChanceFireFades) return;
            try
            {
                var candidates = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive
                             && !h.IsChild
                             && h.Age >= 25f && h.Age < 56f
                             && (h.Clan == null || h.Clan.Leader != h)
                             && h != Hero.MainHero
                             && !ColourLordRegistry.IsAshenLord(h))
                    .ToList();
                if (candidates.Count == 0) return;

                // Choose 2–4 victims deliberately rather than random 50% of a small pool
                int targetCount = Math.Min(candidates.Count, 2 + _rng.Next(3));
                var chosen = candidates.OrderBy(_ => _rng.Next()).Take(targetCount).ToList();

                var names = new List<string>();
                int killed = 0;
                foreach (var hero in chosen)
                {
                    try
                    {
                        // The fire fades from their home too
                        try
                        {
                            var home = hero.HomeSettlement ?? hero.Clan?.HomeSettlement;
                            if (home?.Village != null)
                                home.Village.Hearth = Math.Max(10f, home.Village.Hearth * 0.70f);
                            else if (home?.IsTown == true && home.Town != null)
                                home.Town.Prosperity = Math.Max(10f, home.Town.Prosperity * 0.85f);
                        }
                        catch { }

                        KillCharacterAction.ApplyByMurder(hero, null, false);
                        names.Add(hero.Name.ToString());
                        killed++;
                    }
                    catch { }
                }

                if (killed > 0)
                {
                    string nameList = killed <= 3
                        ? string.Join(", ", names)
                        : $"{names[0]}, {names[1]}, and {killed - 2} others";
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"Fire Fades — {nameList} did not wake this morning. " +
                        "Something ancient and cold moved through the realm in the dark hours. Their hearths grow cold behind them."));
                }
            }
            catch { }
        }

        // ── Event 7: Darkened Roads ───────────────────────────────────────────
        // All caravans operating in a random non-Ashen kingdom are destroyed.
        // Also drains 15% prosperity from every town in the kingdom and spawns
        // 2 Ashen ambush parties to fill the vacuum. Skips if no caravans exist.
        // Uses DestroyPartyAction which is the clean campaign-system way to
        // remove a mobile party; the owning merchant heroes survive and may
        // rebuild their caravans later.
        private static void TryFireDarkenedRoads()
        {
            if (_rng.NextDouble() >= ChanceDarkenedRoads) return;
            try
            {
                var kingdoms = Kingdom.All
                    .Where(k => !k.IsEliminated && k.StringId != AshenKingdomId)
                    .ToList();
                if (kingdoms.Count == 0) return;

                var kingdom = kingdoms[_rng.Next(kingdoms.Count)];

                // Destroy all active caravans in the kingdom
                var caravans = MobileParty.All
                    .Where(p => p.IsActive && p.IsCaravan && p.MapFaction == kingdom)
                    .ToList();
                if (caravans.Count == 0) return; // no caravans = nothing dramatic to destroy

                int destroyed = 0;
                foreach (var caravan in caravans)
                {
                    try { DestroyPartyAction.Apply(caravan.Party, null); destroyed++; }
                    catch { }
                }

                // Trade collapse — every town in the kingdom loses 15% prosperity
                try
                {
                    foreach (var s in Settlement.All)
                    {
                        if (!s.IsTown || s.Town == null || s.MapFaction != kingdom) continue;
                        s.Town.Prosperity = Math.Max(10f, s.Town.Prosperity * 0.85f);
                    }
                }
                catch { }

                // Ashen spawn move in to fill the vacuum
                var anchors = Settlement.All
                    .Where(s => (s.IsTown || s.IsCastle) && s.MapFaction == kingdom)
                    .Select(s => s.GetPosition2D)
                    .ToList();
                int spawned = 0;
                for (int i = 0; i < 2 && anchors.Count > 0; i++)
                {
                    var pos = anchors[_rng.Next(anchors.Count)];
                    var p = SpawnAshenSpawnParty(pos, 14, 50f);
                    if (p != null) spawned++;
                }

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Darkened Roads — {destroyed} caravan{(destroyed != 1 ? "s" : "")} vanish on the roads of {kingdom.Name}. " +
                    $"Trade dies. Prosperity crumbles. " +
                    (spawned > 0 ? "Ashen shapes move where merchants once walked." : "The roads fall silent and cold.")));
            }
            catch { }
        }

        // Sets Town loyalty and security to max so code-driven captures don't
        // immediately trigger a rebellion on the next game tick.
        private static void StabiliseSettlement(Settlement s)
        {
            if (s?.Town == null) return;
            try { s.Town.Loyalty  = 100f; } catch { }
            try { s.Town.Security = 100f; } catch { }
        }

        // ── Public spawn entry point ──────────────────────────────────────────
        // Allows SettlementEncounters to spawn a gate-ambush Ashen party near a
        // settlement without duplicating the spawn logic.
        public static void SpawnAshenAmbushNear(Vec2 pos, int troops, float minStrength)
            => SpawnAshenSpawnParty(pos, troops, minStrength);

        // ── Party spawning helper ─────────────────────────────────────────────
        // Creates a single Ashen Spawn bandit party near anchorPos, registers
        // it with FireWorshippersSystem, and returns it (or null on failure).
        //
        // baseTroops   — starting sea_raider / mountain_bandit count
        // minStrength  — if > 0, tops up troops until Party.TotalStrength reaches this value
        //
        // Failure modes (all silent):
        //   • No bandit clan found in Clan.BanditFactions
        //   • BanditPartyComponent.CreateBanditParty returns null
        //   • Neither "sea_raider" nor "mountain_bandit" CharacterObject exists
        private static MobileParty SpawnAshenSpawnParty(Vec2 anchorPos, int baseTroops, float minStrength)
        {
            try
            {
                Clan banditClan = Clan.BanditFactions.FirstOrDefault(c => c != null && !c.IsEliminated);
                if (banditClan == null) return null;

                var pt = banditClan.DefaultPartyTemplate;
                if (pt == null) return null;

                // Bannerlord's engine accesses HomeSettlement when the party is defeated
                // (to scatter survivors back to the lair).  Passing null causes a native
                // crash when the player clicks "Done" on the post-battle looting screen.
                // Use the bandit clan's own Hideout component; fall back to the nearest
                // hideout settlement in the world if the clan has none yet.
                Hideout hideout = null;
                try
                {
                    Settlement hs = banditClan.Settlements.FirstOrDefault(s => s?.Hideout != null)
                                 ?? Settlement.All.OrderBy(s => (s.GetPosition2D - anchorPos).Length)
                                                  .FirstOrDefault(s => s?.Hideout != null);
                    hideout = hs?.Hideout;
                }
                catch { }

                // Scatter spawn point around the anchor (radius ≈ 4 map units)
                const float scatter = 4f;
                Vec2 spawnPos = anchorPos + new Vec2(
                    (float)(_rng.NextDouble() - 0.5) * scatter * 2f,
                    (float)(_rng.NextDouble() - 0.5) * scatter * 2f
                );
                var spawnCVec = new CampaignVec2(spawnPos, true);

                string partyId = "ashen_spawn_evt_" + _rng.Next(999999).ToString("D6");
                MobileParty party = BanditPartyComponent.CreateBanditParty(partyId, banditClan, hideout, false, pt, spawnCVec);
                if (party == null) return null;

                // Prefer sea_raider (matches the Ashen Spawn troop-type check in
                // FireWorshippersSystem._ashenSpawnTroops); fall back to mountain_bandit
                CharacterObject troop =
                    MBObjectManager.Instance.GetObject<CharacterObject>("sea_raider")
                 ?? MBObjectManager.Instance.GetObject<CharacterObject>("mountain_bandit");
                if (troop == null) return null;

                party.MemberRoster.AddToCounts(troop, baseTroops * 10);

                // Top up to reach minimum strength requirement (rarely needed with 10× base)
                if (minStrength > 0f)
                {
                    int guard = 20; // safety cap — 20 × 5 = max 100 extra troops
                    while (party.Party.EstimatedStrength < minStrength && guard-- > 0)
                        party.MemberRoster.AddToCounts(troop, 5);
                }

                // Register with FireWorshippersSystem so IsAshenSpawn() returns true
                FireWorshippersSystem.ForceMarkAsAshenSpawn(party);

                return party;
            }
            catch { return null; }
        }

        // ── Save / Load ───────────────────────────────────────────────────────
        // Called from MagicCampaignBehavior.SyncData().
        // SyncData works bidirectionally: saves on game-save, restores on load.
        public static void Save(IDataStore store)
        {
            store.SyncData("LDM_LongNightDays", ref _longNightDaysRemaining);
        }
    }
}
