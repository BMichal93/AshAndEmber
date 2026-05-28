// =============================================================================
// ASH AND EMBER — CampaignMapEvents.cs
// Six rare world events themed around fire, ash, and fading. Each has an
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
// │ Fire Fades      │ 50% of non-Ashen lords under age 18 die (old-age action │
// │                 │ with notification suppressed; aggregate message shown).  │
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
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
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
                _longNightDaysRemaining--;
        }

        /// Called from CampaignBehavior.OnWeeklyTick().
        /// Each event rolls independently; multiple can fire the same week.
        public static void WeeklyTick()
        {
            TryFireAshenPlague();
            TryFireGreatWithering();
            TryFireAshenMarch();
            TryFireLongNight();
            TryFireAshenTide();
            TryFireFireFades();
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

                InformationManager.DisplayMessage(new InformationMessage(
                    $"Ashen Plague — a grey sickness takes the garrison of {target.Name}. " +
                    $"{totalWounded} soldier{(totalWounded != 1 ? "s" : "")} fall to their wounds." +
                    (spawned > 0 ? $" {spawned} Ashen Spawn rise from the dying." : ""),
                    new Color(0.55f, 0.45f, 0.65f)));
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

                    InformationManager.DisplayMessage(new InformationMessage(
                        $"Great Withering — the hearth-fires of {target.Name} gutter and die. " +
                        $"Hearth: {before:F0} → {target.Village.Hearth:F0}.",
                        new Color(0.6f, 0.35f, 0.2f)));
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

                    InformationManager.DisplayMessage(new InformationMessage(
                        $"Great Withering — something cold and old passes through {target.Name}. " +
                        $"Prosperity: {before:F0} → {target.Town.Prosperity:F0}.",
                        new Color(0.6f, 0.35f, 0.2f)));
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

                InformationManager.DisplayMessage(new InformationMessage(
                    $"Ashen March — {spawned} Ashen Spawn descend upon {kingdom.Name}. " +
                    "The grey tide does not rest.",
                    new Color(0.7f, 0.3f, 0.2f)));
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
            if (_longNightDaysRemaining > 0) return; // already active — do not stack
            if (_rng.NextDouble() >= ChanceLongNight) return;

            _longNightDaysRemaining = LongNightDuration;

            InformationManager.DisplayMessage(new InformationMessage(
                $"Long Night — the sun does not rise. {LongNightDuration} days of unbroken darkness descend.",
                new Color(0.2f, 0.2f, 0.45f)));
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

                InformationManager.DisplayMessage(new InformationMessage(
                    $"Ashen Tide — {castle.Name} bends to the cold fire. " +
                    $"{lord.Name} claims it without a blade drawn.",
                    new Color(0.45f, 0.35f, 0.6f)));
            }
            catch { }
        }

        // ── Event 6: Fire Fades ───────────────────────────────────────────────
        // Half of all non-Ashen lords under 18 years old are killed.
        // Uses ApplyByOldAge(hero, false) — notification suppressed because
        // we show a single aggregate message instead.
        // The player hero is always spared.
        private static void TryFireFireFades()
        {
            if (_rng.NextDouble() >= ChanceFireFades) return;
            try
            {
                var youngLords = Hero.AllAliveHeroes
                    .Where(h => h != Hero.MainHero
                             && h.IsLord && h.IsAlive
                             && !ColourLordRegistry.IsAshenLord(h)
                             && h.Age < 18f)
                    .ToList();
                if (youngLords.Count == 0) return;

                int killed = 0;
                foreach (var hero in youngLords)
                {
                    if (_rng.Next(2) == 0) continue; // 50% survive
                    try
                    {
                        // false = suppress individual notification; aggregate message below
                        KillCharacterAction.ApplyByOldAge(hero, false);
                        killed++;
                    }
                    catch { }
                }

                if (killed > 0)
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"Fire Fades — {killed} young lord{(killed != 1 ? "s" : "")} did not wake this morning. " +
                        "Something ancient has moved through the realm.",
                        new Color(0.4f, 0.3f, 0.5f)));
            }
            catch { }
        }

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

                string partyId = "ashen_spawn_evt_" + _rng.Next(999999).ToString("D6");
                MobileParty party = BanditPartyComponent.CreateBanditParty(partyId, banditClan, null, false);
                if (party == null) return null;

                // Scatter spawn point around the anchor (radius ≈ 4 map units)
                const float scatter = 4f;
                party.Position2D = anchorPos + new Vec2(
                    (float)(_rng.NextDouble() - 0.5) * scatter * 2f,
                    (float)(_rng.NextDouble() - 0.5) * scatter * 2f
                );

                // Prefer sea_raider (matches the Ashen Spawn troop-type check in
                // FireWorshippersSystem._ashenSpawnTroops); fall back to mountain_bandit
                CharacterObject troop =
                    MBObjectManager.Instance.GetObject<CharacterObject>("sea_raider")
                 ?? MBObjectManager.Instance.GetObject<CharacterObject>("mountain_bandit");
                if (troop == null) return null;

                party.MemberRoster.AddToCounts(troop, baseTroops);

                // Top up to reach minimum strength requirement
                if (minStrength > 0f)
                {
                    int guard = 20; // safety cap — 20 × 5 = max 100 extra troops
                    while (party.Party.TotalStrength < minStrength && guard-- > 0)
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
