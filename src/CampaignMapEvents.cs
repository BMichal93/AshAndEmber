// =============================================================================
// ASH AND EMBER — CampaignMapEvents.cs
// Twelve rare world events themed around fire, ash, betrayal, and fading.
// Each rolls independently on the weekly tick.
//
// ┌──────────────────────┬─────────────────────────────────────────────────────┐
// │ Event                │ Effect                                              │
// ├──────────────────────┼─────────────────────────────────────────────────────┤
// │ Ashen Plague         │ Wounds garrison of a random city/castle; spawns     │
// │                      │ several Ashen Spawn parties near the settlement.    │
// ├──────────────────────┼─────────────────────────────────────────────────────┤
// │ Great Withering      │ Destroys 80% of a random village hearth, OR halves  │
// │                      │ the prosperity of a random city.                    │
// ├──────────────────────┼─────────────────────────────────────────────────────┤
// │ Ashen March          │ Spawns Ashen Spawn parties across a random non-Ashen│
// │                      │ kingdom.                                            │
// ├──────────────────────┼─────────────────────────────────────────────────────┤
// │ Long Night           │ Forces Dark light-level for 7 days; bleeds town     │
// │                      │ prosperity daily.                                   │
// ├──────────────────────┼─────────────────────────────────────────────────────┤
// │ Ashen Tide           │ A random castle falls to an Ashen lord instantly.   │
// ├──────────────────────┼─────────────────────────────────────────────────────┤
// │ Fire Fades           │ 2–4 non-Ashen, non-leader lords die quietly.        │
// ├──────────────────────┼─────────────────────────────────────────────────────┤
// │ Darkened Roads       │ All caravans in a random kingdom vanish; Ashen      │
// │                      │ ambushers fill the roads.                           │
// ├──────────────────────┼─────────────────────────────────────────────────────┤
// │ Seeds of Betrayal    │ (Very rare) A faction leader is murdered by their   │
// │                      │ own court. The responsible clan is expelled.        │
// ├──────────────────────┼─────────────────────────────────────────────────────┤
// │ Broken Will          │ (Once/twice, after day 60) A faction is drawn into  │
// │                      │ the cold — declares war on all others.              │
// ├──────────────────────┼─────────────────────────────────────────────────────┤
// │ The Long March       │ (Rare) 4 massive Ashen warbands (100+ troops each)  │
// │                      │ appear in one of Vlandia/Aserai/Khuzait/Sturgia.    │
// ├──────────────────────┼─────────────────────────────────────────────────────┤
// │ Whispers from the Ash│ (Very rare) 1–3 mage lords abandon their factions  │
// │                      │ and join the Ashen.                                 │
// ├──────────────────────┼─────────────────────────────────────────────────────┤
// │ Tyranny              │ (Very rare) A faction leader executes their highest- │
// │                      │ tier clan heads. Ruling clan loses all influence.   │
// │                      │ One executed clan defects.                          │
// ├──────────────────────┼─────────────────────────────────────────────────────┤
// │ The First Green      │ (Spring only, rare) The world stirs back to life.   │
// │                      │ All non-Ashen lord parties gain a small morale boost.│
// ├──────────────────────┼─────────────────────────────────────────────────────┤
// │ The Amber Harvest    │ (Autumn only, rare) Crops gathered before the cold. │
// │                      │ All non-Ashen villages gain hearth.                 │
// ├──────────────────────┼─────────────────────────────────────────────────────┤
// │ The Ashen Gambit     │ (Once per campaign, day 120+) Ashen assassins strike │
// │                      │ every Imperial throne in a single night. Empire     │
// │                      │ leaders die, lords suffer −30 morale, cities −30   │
// │                      │ security. Ashen Spawn flood the heartlands and the  │
// │                      │ cold armies march.                                  │
// └──────────────────────┴─────────────────────────────────────────────────────┘
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
    public static class CampaignMapEvents
    {
        // ── Tuning constants ──────────────────────────────────────────────────
        // Per-week fire probability for each event. Adjust here without
        // touching event logic.
        // Bannerlord has ~12 weekly ticks per year (84 days / 7).
        // Expected fires per year = chance × 12.
        public const float ChanceAshenPlague      = 0.08f;  // ~every 12 weeks  (~4–5× per campaign)
        public const float ChanceGreatWithering  = 0.10f;  // ~every 10 weeks  (~5–6× per campaign)
        public const float ChanceAshenMarch      = 0.05f;  // ~every 20 weeks  (~2–3× per campaign)
        public const float ChanceLongNight       = 0.03f;  // ~every 33 weeks  (~1–2× per campaign)
        public const float ChanceAshenTide       = 0.03f;  // ~every 33 weeks  (~1–2× per campaign)
        public const float ChanceFireFades       = 0.015f; // ~every 67 weeks  (rare — once per era)
        public const float ChanceDarkenedRoads   = 0.06f;  // ~every 17 weeks  (~3–4× per campaign)
        public const float ChanceSeedsOfBetrayal = 0.013f; // ~every 77 weeks  (very rare)
        public const float ChanceBrokenWill      = 0.010f; // ~every 100 weeks (once or twice per campaign, gated to day 60+)
        public const float ChanceTheLongMarch    = 0.04f;  // ~every 25 weeks  (rare)
        public const float ChanceWhispers        = 0.015f; // ~every 67 weeks  (very rare)
        public const float ChanceTyranny         = 0.02f;  // ~every 50 weeks  (very rare)
        public const float ChanceStolenHeirloom  = 0.02f;  // ~every 50 weeks  (very rare)
        public const float ChancePeasantUnrest   = 0.06f;  // ~every 17 weeks  (medium — like Great Withering)
        public const float ChanceWolfSheepCloth = 0.03f;  // ~every 33 weeks  (rare but not very)
        public const float ChanceMageFatwa      = 0.025f; // ~every 40 weeks  (rare)
        public const float ChanceTheTemple     = 0.04f;  // once per campaign after day 100 (~25 weeks to fire)
        public const int   TempleEarliestDay   = 100;
        public const float ChanceIronWinter      = 0.04f;  // ~every 25 weeks  (rare, winter only)
        public const float ChanceScorchingSun    = 0.04f;  // ~every 25 weeks  (rare, summer only)
        public const float ChanceFirstGreen      = 0.04f;  // ~every 25 weeks  (rare, spring only)
        public const float ChanceAmberHarvest    = 0.04f;  // ~every 25 weeks  (rare, autumn only)
        public const float ChanceEmbersOfHope    = 0.06f;  // ~every 17 weeks  (once Ashen hold 8+ towns)
        public const int   EmbersOfHopeMinTowns  = 8;      // Ashen must hold this many towns to trigger
        public const int   EmbersOfHopePeaceCount = 3;     // max wars ended per firing
        public const float ChanceASlightAtCourt   = 0.05f;  // ~every 20 weeks  (diplomatic incident → war or cold shoulder)
        public const float ChanceBorderTorches   = 0.05f;  // ~every 20 weeks  (border raid → war or tense standoff)
        public const float ChanceADebtInBlood    = 0.04f;  // ~every 25 weeks  (murdered envoy → war or shaky inquiry)
        public const float ChanceBrokenBetrothal = 0.04f;  // ~every 25 weeks  (dissolved marriage → war or icy silence)
        public const float ChanceTreasonousScroll= 0.04f;  // ~every 25 weeks  (spy letters → war or tense denial)
        public const float ChanceAshenGambit     = 0.010f; // ~every 100 weeks, fires ONCE per campaign (day 120+)
        public const int   AshenGambitEarliestDay = 120;
        // Minimum elapsed days between any two world events. Prevents back-to-back clustering.
        public const int   EventCooldownDays      = 14;
        public const int   AshenGambitSpawnCount  = 18;    // Ashen Spawn warbands seeded across the Empire
        public const int   AshenGambitCastleCount = 3;     // Empire castles seized by Ashen lords on the night

        // Ashen Plague: parties spawned near the afflicted settlement
        public const int AshenPlagueSpawnCount  = 3;

        // Ashen March: parties spawned across the target kingdom
        public const int AshenMarchPartyCount   = 6;

        // Ashen March: minimum party strength per spawned party
        public const float MinAshenMarchStrength = 70f;

        // Long Night: duration in campaign days
        public const int LongNightDuration = 7;

        private const string AshenKingdomId = "ashen_kingdom";

        // Broken Will: max times the event can fire per campaign, and earliest campaign day
        public const int BrokenWillMaxFires = 2;
        public const int BrokenWillEarliestDay = 60;

        // Game of Thrones: chance when a faction leader dies; requires 4+ clans in the faction
        public const float ChanceGameOfThrones = 0.05f;
        public const int   GoTMinClans         = 4;

        // The Long March: which kingdoms are eligible targets (vanilla Bannerlord string IDs)
        private static readonly string[] LongMarchTargets = { "vlandia", "aserai", "khuzait", "sturgia" };

        // Iron Winter: northern kingdoms — Sturgia and the Northern Empire endure the worst of the freeze.
        private static readonly string[] NorthernKingdoms = { "sturgia", "empire_n" };

        // Scorching Sun: desert kingdoms — Aserai and the Southern Empire bake in the heat.
        private static readonly string[] DesertKingdoms = { "aserai", "empire_s" };

        // The Ashen Gambit: all vanilla Empire splits plus base "empire" ID for safety.
        private static readonly string[] EmpireKingdomIds = { "empire_w", "empire_s", "empire_n", "empire" };

        // ── Runtime state ─────────────────────────────────────────────────────
        private static int _longNightDaysRemaining = 0;
        private static int _brokenWillFired        = 0;
        private static readonly HashSet<string> _brokenKingdomIds = new HashSet<string>();

        // Game of Thrones: kingdom IDs queued for delayed clan-ejection
        private static readonly List<string> _gotKingdoms = new List<string>();
        private static readonly List<int>    _gotDays     = new List<int>();

        private static readonly Random _rng = new Random();

        // ── Public API ────────────────────────────────────────────────────────

        /// Returns true while the Long Night event is active.
        /// Called by SpellEffects.GetCampaignLightLevel() to force Dark.
        public static bool IsLongNight() => _longNightDaysRemaining > 0;

        // ── Called from CampaignBehavior.OnHeroKilled ────────────────────────
        // Triggered when a faction leader dies. Rolls 5% chance and queues a
        // 2-day delayed Game of Thrones event for that kingdom.
        // Ashen do not fracture — their will is cold and singular.
        public static void OnFactionLeaderKilled(Kingdom kingdom)
        {
            if (kingdom == null || kingdom.IsEliminated) return;
            if (kingdom.StringId == AshenKingdomId) return;         // Ashen never fracture
            if (DragonQuestSystem.WorldRekindled) return;
            if (kingdom.Clans.Count(c => c != null && !c.IsEliminated) < GoTMinClans) return;
            if (_rng.NextDouble() >= ChanceGameOfThrones) return;

            _gotKingdoms.Add(kingdom.StringId);
            _gotDays.Add(2); // 2-day delay so succession settles first
        }

        /// Called from CampaignBehavior.OnDailyTick().
        /// Decrements ongoing timed effects (Long Night).
        public static void DailyTick()
        {
            // Tick down pending Game of Thrones events
            for (int i = _gotDays.Count - 1; i >= 0; i--)
            {
                _gotDays[i]--;
                if (_gotDays[i] <= 0)
                {
                    string kid = _gotKingdoms[i];
                    _gotKingdoms.RemoveAt(i);
                    _gotDays.RemoveAt(i);
                    try
                    {
                        var k = Kingdom.All.FirstOrDefault(x => x.StringId == kid);
                        if (k != null && !k.IsEliminated)
                            FireGameOfThrones(k);
                    }
                    catch { }
                }
            }

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

            // Tick down protective rites
            if (_protectedDaysRemaining > 0)
            {
                _protectedDaysRemaining--;
                if (_protectedDaysRemaining == 0)
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "The protective rites have faded. The sanctuary's shield is spent."));
            }

            // The Temple's war with the Ashen is permanent — re-declare if peace is made
            if (_templeFounded)
            {
                try
                {
                    var temple = Kingdom.All.FirstOrDefault(k =>
                        k.StringId == "the_temple" && !k.IsEliminated);
                    var ashen = Kingdom.All.FirstOrDefault(k =>
                        k.StringId == AshenKingdomId && !k.IsEliminated);
                    if (temple != null && ashen != null && !temple.IsAtWarWith(ashen))
                        DeclareWarAction.ApplyByDefault(temple, ashen);
                }
                catch { }
            }
        }

        /// Called from CampaignBehavior.OnWeeklyTick().
        /// At most one event fires per tick (TryClaimWeeklySlot ensures this).
        /// EventCooldownDays must pass between events to prevent clustering.
        public static void WeeklyTick()
        {
            if (DragonQuestSystem.WorldRekindled) return;
            // ── Cooldown gate ─────────────────────────────────────────────────
            if (ElapsedCampaignDays() - _lastEventElapsedDay < EventCooldownDays) return;
            _weeklySlotFilled = false;

            TryFireAshenPlague();
            TryFireGreatWithering();
            TryFireAshenMarch();
            TryFireLongNight();
            TryFireAshenTide();
            TryFireFireFades();
            TryFireDarkenedRoads();
            TryFireSeedsOfBetrayal();
            TryFireBrokenWill();
            TryFireTheLongMarch();
            TryFireWhispersFromTheAsh();
            TryFireTyranny();
            TryFireStolenHeirloom();
            TryFirePeasantUnrest();
            TryFireWolfSheepClothing();
            TryFireMageFatwa();
            TryFireTheTemple();
            TryFireIronWinter();
            TryFireScorchingSun();
            TryFireFirstGreen();
            TryFireAmberHarvest();
            TryFireASlightAtCourt();
            TryFireBorderTorches();
            TryFireADebtInBlood();
            TryFireBrokenBetrothal();
            TryFireTreasonousScroll();
            TryFireEmbersOfHope();
            TryFireAshenGambit();

            if (_weeklySlotFilled)
                _lastEventElapsedDay = (int)ElapsedCampaignDays();
        }

        /// Resets state for a fresh new game (called from OnNewGameCreated).
        public static void ResetForNewGame()
        {
            _longNightDaysRemaining  = 0;
            _brokenWillFired         = 0;
            _templeFounded           = false;
            _debugForceNextTemple    = false;
            _protectedDaysRemaining  = 0;
            _ashenGambitFired        = false;
            _campaignStartDay        = (int)CampaignTime.Now.ToDays;
            _weeklySlotFilled        = false;
            _lastEventElapsedDay     = -EventCooldownDays;
            _brokenKingdomIds.Clear();
            _gotKingdoms.Clear();
            _gotDays.Clear();
        }

        // ── Event 1: Ashen Plague ─────────────────────────────────────────────
        // Wounds all healthy garrison troops in a random city or castle, then
        // spawns AshenPlagueSpawnCount Ashen Spawn parties near the settlement.
        private static void TryFireAshenPlague()
        {
            if (_rng.NextDouble() >= ChanceAshenPlague) return;
            if (!TryClaimWeeklySlot()) return;
            if (_protectedDaysRemaining > 0)
            {
                MBInformationManager.AddQuickInformation(new TextObject(
                    "Ashen Plague — the sanctuary's protective ward turns it aside. The grey sickness finds no purchase."));
                return;
            }
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
            if (!TryClaimWeeklySlot()) return;
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
            if (!TryClaimWeeklySlot()) return;
            if (_protectedDaysRemaining > 0)
            {
                MBInformationManager.AddQuickInformation(new TextObject(
                    "Ashen March — the holy ward holds. The grey tide finds the roads blocked by something it cannot name."));
                return;
            }
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

                MBInformationManager.AddQuickInformation(new TextObject(
                    spawned > 0
                        ? $"Ashen March — {spawned} Ashen Spawn descend upon {kingdom.Name}. The grey tide does not rest."
                        : $"Ashen March — the grey tide stirs near {kingdom.Name}, but finds no foothold today."));
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
            if (!TryClaimWeeklySlot()) return;
            if (_protectedDaysRemaining > 0)
            {
                MBInformationManager.AddQuickInformation(new TextObject(
                    "Long Night — the protective rites hold. The darkness stirs at the edge but cannot cross."));
                return;
            }

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
            if (!TryClaimWeeklySlot()) return;
            if (_protectedDaysRemaining > 0)
            {
                MBInformationManager.AddQuickInformation(new TextObject(
                    "Ashen Tide — the sanctuary's blessing turns the cold back. The castle holds."));
                return;
            }
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
            if (!TryClaimWeeklySlot()) return;
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
                        "Something ancient and cold moved through the realm in the dark hours. Their hearths grow cold behind them. " +
                        $"[{killed} lord{(killed != 1 ? "s" : "")} killed; home settlements lost hearth and prosperity.]"));
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
            if (!TryClaimWeeklySlot()) return;
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

        // ── Event 8: Seeds of Betrayal ────────────────────────────────────────
        // A faction leader is murdered by their own court. The murderous clan is
        // expelled from the faction. Inspired by the Red Wedding.
        //
        // If the player's clan is tier 4+ in the affected kingdom, a choice
        // appears: back the conspirators or warn the court.
        //   Back: +50 with schemer clan, −100 with ruling clan.
        //   Warn: −100 with schemer clan, +20 with ruling clan, 33% plot stops.
        //
        // Safety constraints:
        //   • Excludes the player as faction leader (k.Leader != Hero.MainHero).
        //   • Requires the target faction to have ≥ 3 clans so one expulsion does
        //     not immediately collapse the realm.
        //   • Uses ApplyByMurder(leader, null, false) — neutral quiet death, engine
        //     handles succession automatically.
        //   • The expelled clan uses ApplyByLeaveKingdom — safe at any time.
        private static void TryFireSeedsOfBetrayal()
        {
            if (_rng.NextDouble() >= ChanceSeedsOfBetrayal) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                var candidates = Kingdom.All
                    .Where(k => !k.IsEliminated
                             && k.StringId != AshenKingdomId
                             && k.Leader != null
                             && k.Leader != Hero.MainHero
                             && k.Leader.IsAlive
                             && !k.Leader.IsPrisoner
                             && !k.Leader.IsChild
                             && k.Clans.Count(c => c != null && !c.IsEliminated) >= 3)
                    .ToList();
                if (candidates.Count == 0) return;

                var  kingdom      = candidates[_rng.Next(candidates.Count)];
                var  leader       = kingdom.Leader;
                Clan oldRulingClan = kingdom.RulingClan;

                var scapegoats = kingdom.Clans
                    .Where(c => c != null && !c.IsEliminated
                             && c != kingdom.RulingClan
                             && (c.Leader == null || c.Leader != Hero.MainHero)
                             && c.Heroes.Any(h => h.IsAlive && !h.IsChild))
                    .ToList();

                Clan expelled = scapegoats.Count > 0 ? scapegoats[_rng.Next(scapegoats.Count)] : null;

                string leaderName   = leader.Name?.ToString()          ?? "the lord";
                string kingdomName  = kingdom.Name?.ToString()         ?? "the realm";
                string expelledName = expelled?.Name?.ToString()       ?? "a noble house";
                string oldRulerName = oldRulingClan?.Name?.ToString()  ?? "the ruling clan";

                bool playerQualifies = PlayerIsQualifiedForEvent(kingdom)
                                    && Hero.MainHero?.Clan != expelled;

                if (playerQualifies)
                {
                    string body =
                        $"Word has reached you in the dark — {expelledName} is moving against {leaderName} of {kingdomName}. " +
                        $"The wine is already prepared. They are asking if you stand with them.\n\n" +
                        $"Back the conspirators: +50 relations with {expelledName}, −100 with {oldRulerName}.\n" +
                        $"Warn the court: −100 with {expelledName}, +20 with {oldRulerName}, 33% chance the plot is stopped.";

                    InformationManager.ShowInquiry(new InquiryData(
                        "Seeds of Betrayal",
                        body,
                        true, true,
                        $"Back {expelledName}",
                        $"Warn {leaderName}",
                        () =>
                        {
                            try
                            {
                                try { KillCharacterAction.ApplyByMurder(leader, null, false); } catch { }
                                if (expelled != null && expelled.Kingdom == kingdom)
                                    try { ChangeKingdomAction.ApplyByLeaveKingdom(expelled, false); } catch { }
                                PlayerRelationWithClan(expelled, +50);
                                PlayerRelationWithClan(oldRulingClan, -100);
                                MBInformationManager.AddQuickInformation(new TextObject(
                                    $"Seeds of Betrayal — {leaderName} of {kingdomName} did not survive the feast. " +
                                    $"You were part of it. {expelledName} fled before dawn — grateful, and gone. " +
                                    $"What remains of {oldRulerName} knows a blade when they see the hand that held it."));
                            }
                            catch { }
                        },
                        () =>
                        {
                            try
                            {
                                PlayerRelationWithClan(expelled, -100);
                                if (_rng.NextDouble() < 0.33)
                                {
                                    PlayerRelationWithClan(oldRulingClan, +20);
                                    MBInformationManager.AddQuickInformation(new TextObject(
                                        $"Seeds of Betrayal — your warning reached {leaderName} in time. " +
                                        $"The feast was cancelled. {expelledName}'s plot collapsed in daylight. " +
                                        $"{oldRulerName} owes you something, whether or not they say so. " +
                                        $"{expelledName} will not forget your name."));
                                }
                                else
                                {
                                    try { KillCharacterAction.ApplyByMurder(leader, null, false); } catch { }
                                    if (expelled != null && expelled.Kingdom == kingdom)
                                        try { ChangeKingdomAction.ApplyByLeaveKingdom(expelled, false); } catch { }
                                    PlayerRelationWithClan(oldRulingClan, +20);
                                    MBInformationManager.AddQuickInformation(new TextObject(
                                        $"Seeds of Betrayal — {leaderName} of {kingdomName} did not survive despite your warning. " +
                                        $"{expelledName} moved before the word could spread. " +
                                        $"{oldRulerName} remembers who tried. {expelledName} remembers too."));
                                }
                            }
                            catch { }
                        }
                    ), true);
                }
                else
                {
                    try { KillCharacterAction.ApplyByMurder(leader, null, false); } catch { }
                    if (expelled != null && expelled.Kingdom == kingdom)
                        try { ChangeKingdomAction.ApplyByLeaveKingdom(expelled, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"Seeds of Betrayal — {leaderName} of {kingdomName} did not survive the feast. " +
                        $"The wine was poisoned. The doors were barred. {expelledName} fled before dawn, " +
                        $"their banners cut from the hall. Someone will sit the seat they left empty. " +
                        $"Someone always does."));
                }
            }
            catch { }
        }

        // ── Event 9: Broken Will ─────────────────────────────────────────────
        // A faction leader looks into the cold fire long enough that it begins
        // to look back. That faction declares war on every other kingdom —
        // it becomes as isolated and hostile as the Ashen themselves.
        //
        // Fires at most BrokenWillMaxFires times per campaign, never before
        // campaign day BrokenWillEarliestDay. Uses a re-entrancy guard.
        //
        // Safety constraints:
        //   • Skips the player's faction.
        //   • Skips already-broken kingdoms.
        //   • Checks !IsAtWarWith before declaring to avoid duplicate war actions.
        private static bool _declaringBrokenWill    = false;
        private static bool _templeFounded          = false;
        private static bool _debugForceNextTemple   = false;
        private static int  _protectedDaysRemaining = 0;
        private static bool _ashenGambitFired       = false;
        private static int  _campaignStartDay       = -1;
        // Event throttle: at most one event fires per weekly tick, and no event fires
        // until EventCooldownDays have passed since the last one.
        private static bool _weeklySlotFilled    = false;
        private static int  _lastEventElapsedDay = -EventCooldownDays;

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
                    $"Iron Winter (Ashen Altar) — the cold the altar called has descended on {kingdom.Name}. " +
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
                    $"Scorching Sun (Ashen Altar) — the heat the altar called is burning {kingdom.Name}. " +
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

        // ── Event 10: The Long March ─────────────────────────────────────────
        // Four massive Ashen warbands (100+ troops each) materialise within
        // one of the western, southern, eastern, or northern realms:
        // Vlandia, Aserai, Khuzait Khanate, or Sturgia.
        // This is a deliberate targeted invasion, not a scatter event.
        //
        // Safety constraints:
        //   • Skips eliminated kingdoms and kingdoms with no settlement anchors.
        //   • SpawnAshenSpawnParty wraps everything in try/catch.
        private static void TryFireTheLongMarch()
        {
            if (_rng.NextDouble() >= ChanceTheLongMarch) return;
            if (!TryClaimWeeklySlot()) return;
            if (_protectedDaysRemaining > 0)
            {
                MBInformationManager.AddQuickInformation(new TextObject(
                    "The Long March — the protective rites form a wall the grey tide cannot cross. The columns turn back."));
                return;
            }
            try
            {
                // Shuffle eligible target kingdoms so selection is not always Vlandia
                var targets = LongMarchTargets
                    .Select(id => Kingdom.All.FirstOrDefault(k => k.StringId == id && !k.IsEliminated))
                    .Where(k => k != null)
                    .OrderBy(_ => _rng.Next())
                    .ToList();
                if (targets.Count == 0) return;

                var kingdom = targets[0];
                var anchors = Settlement.All
                    .Where(s => s.MapFaction == kingdom && (s.IsTown || s.IsCastle))
                    .Select(s => s.GetPosition2D)
                    .ToList();
                if (anchors.Count == 0) return;

                int spawned = 0;
                for (int i = 0; i < 4; i++)
                {
                    var anchor = anchors[_rng.Next(anchors.Count)];
                    // 10 × 10 = 100 base troops; minStrength 80 tops up if needed
                    var party = SpawnAshenSpawnParty(anchor, baseTroops: 10, minStrength: 80f);
                    if (party != null) spawned++;
                }

                MBInformationManager.AddQuickInformation(new TextObject(
                    spawned > 0
                        ? $"The Long March — {spawned} great columns of Ashen Spawn set foot in {kingdom.Name}. " +
                          $"These are not raiders. They do not break and scatter. They march."
                        : $"The Long March — something moved through {kingdom.Name}. The roads show it. The villages show it. But whatever passed has gone."));
            }
            catch { }
        }

        // ── Event 11: Whispers from the Ash ──────────────────────────────────
        // 1–3 mage lords hear the cold calling them by name. They abandon their
        // factions and join the Ashen, gaining Ashen lord status, personality,
        // and the cold fire's mark.
        //
        // Safety constraints:
        //   • Never converts the player hero.
        //   • Only converts clan leaders of clans with at least 2 living heroes,
        //     so the source clan is never left headless mid-event.
        //   • Requires the source kingdom to retain at least 2 clans after the
        //     defection, to prevent immediate faction extinction.
        //   • ColourLordRegistry.SetAshen + OnHeroSetAshen handle clan movement
        //     and kingdom placement safely.
        private static void TryFireWhispersFromTheAsh()
        {
            if (_rng.NextDouble() >= ChanceWhispers) return;
            if (!TryClaimWeeklySlot()) return;
            if (_protectedDaysRemaining > 0)
            {
                MBInformationManager.AddQuickInformation(new TextObject(
                    "Whispers from the Ash — the holy ward silences the call. The mages hear nothing but flame."));
                return;
            }
            try
            {
                // Eligible: mage lords, clan leaders, 2+ alive members in clan, kingdom stays viable
                var candidates = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && !h.IsChild && !h.IsPrisoner
                             && h != Hero.MainHero
                             && ColourLordRegistry.IsColourLord(h)
                             && !ColourLordRegistry.IsAshenLord(h)
                             && h.Clan != null && h.Clan.Leader == h   // clan leader only
                             && h.Clan.Heroes.Count(x => x.IsAlive && !x.IsChild) >= 2
                             && h.Clan.Kingdom != null
                             && h.Clan.Kingdom.StringId != AshenKingdomId
                             && h.Clan.Kingdom != Hero.MainHero?.Clan?.Kingdom
                             && h.Clan.Kingdom.Clans.Count(c => c != null && !c.IsEliminated) >= 3)
                    .ToList();
                if (candidates.Count == 0) return;

                int count = Math.Min(candidates.Count, 1 + _rng.Next(3)); // 1–3
                var chosen = candidates.OrderBy(_ => _rng.Next()).Take(count).ToList();

                var names = new List<string>();
                foreach (var hero in chosen)
                {
                    try
                    {
                        names.Add(hero.Name?.ToString() ?? "a lord");
                        try { ColourLordRegistry.SetAshen(hero, true); }              catch { }
                        try { AshenCitySystem.ApplyAshenPersonality(hero); }          catch { }
                        try { ColourLordRegistry.SetMage(hero, true); }               catch { }
                        try { AshenCitySystem.OnHeroSetAshen(hero); }                 catch { }
                        try { MageKnowledge.ApplyAshenAppearance(hero); }             catch { }
                    }
                    catch { }
                }

                if (names.Count == 0) return;
                string nameStr = names.Count == 1
                    ? names[0]
                    : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1];

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Whispers from the Ash — {nameStr} heard something in the fire " +
                    $"that they cannot explain and cannot forget. They have gone north. " +
                    $"Their banners are cold. Their eyes are grey. " +
                    $"Their former lords received only a letter — unsigned, unaddressed, already cold. " +
                    $"[{names.Count} mage lord{(names.Count != 1 ? "s" : "")} defected to the Ashen.]"));
            }
            catch { }
        }

        // ── Event 12: Tyranny ────────────────────────────────────────────────
        // A faction leader's paranoia turns lethal. All tier-5 and tier-6 clan
        // heads within their realm are executed in a single night. The ruling
        // clan is bankrupted — influence drained to zero. One of the executed
        // clans defects before the blade falls.
        //
        // If the player's clan is tier 4+ in the affected kingdom, a choice
        // appears: support the tyrant or defy them.
        //   Support: +100 with tyrant, −50 with all condemned clans.
        //   Defy: 33% chance the player is also executed (game over path).
        //
        // Safety constraints:
        //   • Never has the player as tyrant (k.Leader != Hero.MainHero).
        //   • Only kills clan leaders whose clan has ≥ 2 living members.
        //   • Requires at least one tier-5/6 non-ruling clan to exist.
        //   • Defecting clan uses ApplyByLeaveKingdom (safe outside ClanChangedKingdom).
        //   • Ruling clan influence floor is 0f (never negative).
        private static void TryFireTyranny()
        {
            if (_rng.NextDouble() >= ChanceTyranny) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                var kingdoms = Kingdom.All
                    .Where(k => !k.IsEliminated
                             && k.StringId != AshenKingdomId
                             && k.Leader != null && k.Leader != Hero.MainHero
                             && k.RulingClan != null
                             && k.Clans.Any(c => c != null && !c.IsEliminated
                                              && c != k.RulingClan
                                              && c.Tier >= 5
                                              && c.Leader != null
                                              && c.Leader.IsAlive && !c.Leader.IsChild
                                              && c.Heroes.Count(h => h.IsAlive && !h.IsChild) >= 2))
                    .ToList();
                if (kingdoms.Count == 0) return;

                var kingdom  = kingdoms[_rng.Next(kingdoms.Count)];
                var tyrant   = kingdom.Leader;
                var ruling   = kingdom.RulingClan;

                var condemned = kingdom.Clans
                    .Where(c => c != null && !c.IsEliminated
                             && c != ruling
                             && c.Tier >= 5
                             && c.Leader != null
                             && c.Leader.IsAlive && !c.Leader.IsChild
                             && (c.Leader == null || c.Leader != Hero.MainHero)
                             && c.Heroes.Count(h => h.IsAlive && !h.IsChild) >= 2)
                    .ToList();
                if (condemned.Count == 0) return;

                Clan defector = condemned[_rng.Next(condemned.Count)];

                string tyrantName   = tyrant?.Name?.ToString()   ?? "the lord";
                string kingdomName  = kingdom.Name?.ToString()   ?? "the realm";
                string defectorName = defector?.Name?.ToString() ?? "one house";

                bool playerQualifies = PlayerIsQualifiedForEvent(kingdom);

                if (playerQualifies)
                {
                    var executedClans = condemned.Where(c => c != defector).ToList();
                    string condemnedNames = executedClans.Count == 0 ? "the high lords"
                        : executedClans.Count <= 2
                            ? string.Join(" and ", executedClans.Select(c => c.Name?.ToString() ?? "a house"))
                            : (executedClans[0].Name?.ToString() ?? "a house") + " and others";

                    string body =
                        $"{tyrantName} of {kingdomName} has called the high lords to feast — " +
                        $"and means to keep them there permanently. " +
                        $"{condemnedNames} are condemned. {defectorName} has already fled.\n\n" +
                        $"Support the purge: +100 relations with {tyrantName}, −50 with all condemned clans.\n" +
                        $"Defy the tyrant: 33% chance of being added to the execution list.";

                    InformationManager.ShowInquiry(new InquiryData(
                        "Tyranny",
                        body,
                        true, true,
                        $"Support {tyrantName}",
                        "Defy the tyrant",
                        () =>
                        {
                            try
                            {
                                try { ChangeKingdomAction.ApplyByLeaveKingdom(defector, false); } catch { }
                                var executed = new List<string>();
                                foreach (var clan in condemned)
                                {
                                    if (clan == defector) continue;
                                    if (clan.Leader == null || !clan.Leader.IsAlive) continue;
                                    try
                                    {
                                        executed.Add(clan.Leader.Name?.ToString() ?? "a lord");
                                        KillCharacterAction.ApplyByMurder(clan.Leader, null, false);
                                    }
                                    catch { }
                                }
                                try { ruling.Influence = 0f; } catch { }

                                if (tyrant != null && tyrant.IsAlive)
                                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                                        Hero.MainHero, tyrant, +100, false); } catch { }
                                foreach (var clan in condemned)
                                    PlayerRelationWithClan(clan, -50);

                                string exList = executed.Count == 0 ? "none"
                                    : executed.Count <= 3 ? string.Join(", ", executed)
                                    : $"{executed[0]}, {executed[1]}, and {executed.Count - 2} others";
                                MBInformationManager.AddQuickInformation(new TextObject(
                                    $"Tyranny — you stood with {tyrantName}. {exList} did not leave the feast. " +
                                    $"{defectorName} read the invitation and chose the road. " +
                                    $"The tyrant's gratitude is real. The hatred of the condemned will outlast them."));
                            }
                            catch { }
                        },
                        () =>
                        {
                            try
                            {
                                try { ChangeKingdomAction.ApplyByLeaveKingdom(defector, false); } catch { }
                                var executed = new List<string>();
                                foreach (var clan in condemned)
                                {
                                    if (clan == defector) continue;
                                    if (clan.Leader == null || !clan.Leader.IsAlive) continue;
                                    try
                                    {
                                        executed.Add(clan.Leader.Name?.ToString() ?? "a lord");
                                        KillCharacterAction.ApplyByMurder(clan.Leader, null, false);
                                    }
                                    catch { }
                                }
                                try { ruling.Influence = 0f; } catch { }

                                if (_rng.NextDouble() < 0.33)
                                {
                                    MBInformationManager.AddQuickInformation(new TextObject(
                                        $"Tyranny — you defied {tyrantName}. They added your name to the list. " +
                                        $"The blade found you before dawn."));
                                    try { KillCharacterAction.ApplyByMurder(Hero.MainHero, null, false); } catch { }
                                }
                                else
                                {
                                    string exList = executed.Count == 0 ? "none"
                                        : executed.Count <= 3 ? string.Join(", ", executed)
                                        : $"{executed[0]}, {executed[1]}, and {executed.Count - 2} others";
                                    MBInformationManager.AddQuickInformation(new TextObject(
                                        $"Tyranny — you defied {tyrantName}. The purge happened anyway — {exList} before dawn. " +
                                        $"Your defiance was noted. For now, the blade did not find you."));
                                }
                            }
                            catch { }
                        }
                    ), true);
                }
                else
                {
                    try { ChangeKingdomAction.ApplyByLeaveKingdom(defector, false); } catch { }
                    var executed = new List<string>();
                    foreach (var clan in condemned)
                    {
                        if (clan == defector) continue;
                        if (clan.Leader == null || !clan.Leader.IsAlive) continue;
                        try
                        {
                            executed.Add(clan.Leader.Name?.ToString() ?? "a lord");
                            KillCharacterAction.ApplyByMurder(clan.Leader, null, false);
                        }
                        catch { }
                    }
                    try { ruling.Influence = 0f; } catch { }

                    string exList2 = executed.Count == 0 ? "none"
                        : executed.Count <= 3 ? string.Join(", ", executed)
                        : $"{executed[0]}, {executed[1]}, and {executed.Count - 2} others";
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"Tyranny — {tyrantName} of {kingdomName} called their great lords to feast " +
                        $"and did not let them leave. {exList2} — dead before dawn. " +
                        $"{defectorName} read the invitation and chose the road instead. " +
                        $"The throne room is emptier now. So is the treasury of those who held it."));
                }
            }
            catch { }
        }

        // ── Event 13: Stolen Heirloom ─────────────────────────────────────────
        // A rival clan within a faction seizes power — the faction leader changes
        // to the head of a different clan inside the same kingdom.
        //
        // If the player's clan is tier 4+ in the affected kingdom (and is neither
        // the old ruler nor the usurper), a choice appears.
        //   Back the usurper: +50 with usurper, −100 with old ruling clan.
        //   Stand with old rulers: −100 with usurper, +20 with old ruling clan,
        //     33% chance the coup fails.
        //
        // Uses ChangeRulingClanAction.Apply(newClan) which is the engine's own
        // ruling-clan transition. Falls back to a no-op if the action throws.
        //
        // Safety constraints:
        //   • Excludes the Ashen kingdom.
        //   • Requires ≥ 2 non-eliminated clans in the kingdom.
        //   • The new ruling clan must already be a member of the kingdom.
        //   • Wrapped entirely in try/catch; a failure is silent and harmless.
        private static void TryFireStolenHeirloom()
        {
            if (_rng.NextDouble() >= ChanceStolenHeirloom) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                var kingdoms = Kingdom.All
                    .Where(k => !k.IsEliminated
                             && k.StringId != AshenKingdomId
                             && k.RulingClan != null
                             && k.Clans.Count(c => c != null && !c.IsEliminated) >= 2)
                    .ToList();
                if (kingdoms.Count == 0) return;

                var  kingdom  = kingdoms[_rng.Next(kingdoms.Count)];
                Clan oldRuler = kingdom.RulingClan;

                var rivals = kingdom.Clans
                    .Where(c => c != null && !c.IsEliminated
                             && c != oldRuler
                             && c.Leader != null
                             && c.Leader.IsAlive
                             && !c.Leader.IsChild
                             && c.Leader != Hero.MainHero)
                    .ToList();
                if (rivals.Count == 0) return;

                var    usurper = rivals[_rng.Next(rivals.Count)];
                string oldName  = oldRuler?.Name?.ToString() ?? "the old house";
                string newName  = usurper.Name?.ToString()   ?? "a rival house";
                string kingName = kingdom.Name?.ToString()   ?? "the realm";

                bool playerQualifies = PlayerIsQualifiedForEvent(kingdom)
                                    && Hero.MainHero?.Clan != oldRuler
                                    && Hero.MainHero?.Clan != usurper;

                if (playerQualifies)
                {
                    string body =
                        $"{newName} is moving to seize the seal of {kingName} from {oldName}. " +
                        $"Word has reached you — and they are waiting to see which way your clan stands.\n\n" +
                        $"Back the seizure: +50 relations with {newName}, −100 with {oldName}.\n" +
                        $"Stand with {oldName}: −100 with {newName}, +20 with {oldName}, 33% chance the coup fails.";

                    InformationManager.ShowInquiry(new InquiryData(
                        "Stolen Heirloom",
                        body,
                        true, true,
                        $"Back {newName}",
                        $"Stand with {oldName}",
                        () =>
                        {
                            try
                            {
                                try { ChangeRulingClanAction.Apply(kingdom, usurper); } catch { }
                                PlayerRelationWithClan(usurper,  +50);
                                PlayerRelationWithClan(oldRuler, -100);
                                MBInformationManager.AddQuickInformation(new TextObject(
                                    $"Stolen Heirloom — you backed {newName}'s move. The seal of {kingName} changed hands. " +
                                    $"{oldName} knows exactly where you stood."));
                            }
                            catch { }
                        },
                        () =>
                        {
                            try
                            {
                                PlayerRelationWithClan(usurper,  -100);
                                PlayerRelationWithClan(oldRuler, +20);
                                if (_rng.NextDouble() < 0.33)
                                {
                                    MBInformationManager.AddQuickInformation(new TextObject(
                                        $"Stolen Heirloom — your opposition was enough. {newName}'s move collapsed before it landed. " +
                                        $"{kingName} stays in {oldName}'s hands. {newName} has not forgotten your part in it."));
                                }
                                else
                                {
                                    try { ChangeRulingClanAction.Apply(kingdom, usurper); } catch { }
                                    MBInformationManager.AddQuickInformation(new TextObject(
                                        $"Stolen Heirloom — despite your opposition, {newName} pressed ahead. " +
                                        $"The seal of {kingName} is in their hands now. {oldName} is grateful, though powerless. " +
                                        $"{newName} will not forget your name."));
                                }
                            }
                            catch { }
                        }
                    ), true);
                }
                else
                {
                    try { ChangeRulingClanAction.Apply(kingdom, usurper); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"Stolen Heirloom — the signet ring of {kingName} changed hands in the night. " +
                        $"{newName} holds the seal now. {oldName} held it at sundown. " +
                        $"No swords were drawn. That may be the most frightening part."));
                }
            }
            catch { }
        }

        // ── Event 14: Iron Winter ─────────────────────────────────────────────
        // The cold bites deep into the north. Villages lose half their hearth;
        // towns lose half their prosperity and food stocks. Fires only in winter.
        //
        // "North" = Sturgia and the Northern Empire (see NorthernKingdoms array).
        //
        // Safety: all property writes are clamped to 10 (never zeroed). Wrapped
        // in try/catch per settlement so one bad settlement can't abort the rest.
        private static void TryFireIronWinter()
        {
            if (!IsWinter()) return;
            if (_rng.NextDouble() >= ChanceIronWinter) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                // Pick one random northern kingdom rather than devastating all of them at once
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
                    {
                        try { s.Village.Hearth = Math.Max(10f, s.Village.Hearth * 0.5f); villages++; } catch { }
                    }
                    else if (s.IsTown && s.Town != null)
                    {
                        try
                        {
                            s.Town.Prosperity = Math.Max(10f, s.Town.Prosperity * 0.5f);
                            s.Town.FoodStocks = Math.Max(10f, s.Town.FoodStocks * 0.5f);
                            towns++;
                        }
                        catch { }
                    }
                }

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Iron Winter — the cold descended on {kingdom.Name} and refused to leave. " +
                    $"{villages} village{(villages != 1 ? "s" : "")} cannot keep their fires lit. " +
                    $"{towns} cit{(towns != 1 ? "ies" : "y")} ha{(towns != 1 ? "ve" : "s")} halved their stores. " +
                    $"The roads are quiet in the wrong way."));
            }
            catch { }
        }

        // ── Event 15: Scorching Sun ───────────────────────────────────────────
        // The desert bakes. Villages in the south lose half their hearth;
        // towns lose half their prosperity and food stocks. Fires only in summer.
        //
        // "Desert" = Aserai and the Southern Empire (see DesertKingdoms array).
        //
        // Safety: same per-settlement try/catch and floor clamping as Iron Winter.
        private static void TryFireScorchingSun()
        {
            if (!IsSummer()) return;
            if (_rng.NextDouble() >= ChanceScorchingSun) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                // Pick one random desert kingdom rather than scorching all of them at once
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
                    {
                        try { s.Village.Hearth = Math.Max(10f, s.Village.Hearth * 0.5f); villages++; } catch { }
                    }
                    else if (s.IsTown && s.Town != null)
                    {
                        try
                        {
                            s.Town.Prosperity = Math.Max(10f, s.Town.Prosperity * 0.5f);
                            s.Town.FoodStocks = Math.Max(10f, s.Town.FoodStocks * 0.5f);
                            towns++;
                        }
                        catch { }
                    }
                }

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Scorching Sun — the sky above {kingdom.Name} has been white with heat for three weeks. " +
                    $"The wells in {villages} village{(villages != 1 ? "s" : "")} are low or dry. " +
                    $"{towns} cit{(towns != 1 ? "ies" : "y")} ha{(towns != 1 ? "ve" : "s")} rationed their stores. " +
                    $"The land remembers."));
            }
            catch { }
        }

        // ── Event 16: The First Green ─────────────────────────────────────────
        // Spring only. The world stirs back to life — flowers push through the
        // soil, rivers run clear. Ash has not yet smothered the season.
        // All active lord parties outside the Ashen kingdom receive a small
        // morale boost (+10 RecentEventsMorale).
        private static void TryFireFirstGreen()
        {
            if (!IsSpring()) return;
            if (_rng.NextDouble() >= ChanceFirstGreen) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                int boosted = 0;
                foreach (var party in MobileParty.All.ToList())
                {
                    if (party == null || !party.IsActive || !party.IsLordParty) continue;
                    if (party.MapFaction == null || party.MapFaction.IsEliminated) continue;
                    if (party.MapFaction.StringId == AshenKingdomId) continue;
                    try { party.RecentEventsMorale += 10f; boosted++; } catch { }
                }

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"The First Green — flowers push through the soil. Rivers run clear. " +
                    $"For a week the ash feels further away than it is. " +
                    $"Across {boosted} warband{(boosted != 1 ? "s" : "")}, soldiers lift their eyes from the grey horizon. " +
                    $"The world has not forgotten how to be alive."));
            }
            catch { }
        }

        // ── Event 17: The Amber Harvest ───────────────────────────────────────
        // Autumn only. The crops gave what they promised before the cold comes.
        // All villages not under the Ashen banner gain +20 hearth as granaries
        // fill and hearths are stocked for winter.
        private static void TryFireAmberHarvest()
        {
            if (!IsAutumn()) return;
            if (_rng.NextDouble() >= ChanceAmberHarvest) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                int villages = 0;
                foreach (var s in Settlement.All)
                {
                    if (s == null || !s.IsVillage || s.Village == null) continue;
                    if (s.MapFaction == null || s.MapFaction.StringId == AshenKingdomId) continue;
                    try { s.Village.Hearth += 20f; villages++; } catch { }
                }

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"The Amber Harvest — the fields gave what they promised. " +
                    $"Across {villages} village{(villages != 1 ? "s" : "")}, granaries are full and hearths burn warm. " +
                    $"There is laughter again, and the smell of fresh bread on the autumn air. " +
                    $"Let the cold come."));
            }
            catch { }
        }

        // ── Event 18: Game of Thrones ─────────────────────────────────────────
        // Triggered 2 days after a faction leader dies (5% chance, 4+ clan kingdom).
        // All non-ruling, non-player clans leave the kingdom and become independent.
        // They keep their fiefs — the kingdom fractures.
        //
        // Safety constraints:
        //   • Never fires for the Ashen (excluded at trigger).
        //   • Never fires for the player's faction.
        //   • Never ejects the current ruling clan or the player's clan.
        //   • Each ejection in its own try/catch; a bad clan can't abort others.
        //   • Requires kingdom still exists and is not eliminated by the time of firing.
        //   • Uses ChangeKingdomAction.ApplyByLeaveKingdom — the only safe API path.
        private static void FireGameOfThrones(Kingdom kingdom)
        {
            if (kingdom == null || kingdom.IsEliminated) return;
            if (kingdom.StringId == AshenKingdomId) return;

            var ruling      = kingdom.RulingClan;
            var playerClan  = Hero.MainHero?.Clan;
            string kingName = kingdom.Name?.ToString() ?? "the realm";
            string newLeader= kingdom.Leader?.Name?.ToString() ?? "a new lord";

            var toEject = kingdom.Clans
                .Where(c => c != null && !c.IsEliminated
                         && c != ruling
                         && c != playerClan
                         && c.Heroes.Any(h => h.IsAlive && !h.IsChild))
                .ToList();

            if (toEject.Count == 0) return;

            var expelled = new List<string>();
            foreach (var clan in toEject)
            {
                try
                {
                    expelled.Add(clan.Name?.ToString() ?? "a house");
                    ChangeKingdomAction.ApplyByLeaveKingdom(clan, false);
                }
                catch { }
            }

            if (expelled.Count == 0) return;

            string nameList = expelled.Count <= 3
                ? string.Join(", ", expelled)
                : $"{expelled[0]}, {expelled[1]}, and {expelled.Count - 2} others";

            MBInformationManager.AddQuickInformation(new TextObject(
                $"Game of Thrones — When {kingName}'s lord fell, the wolves came out from behind their smiles. " +
                $"The court had been held together by one will. Without it, {nameList} " +
                $"raised their own banners and walked out the gate with everything they owned. " +
                $"{newLeader} inherits a throne — and a much smaller kingdom. " +
                $"What was one realm is now many ambitions. " +
                $"[{expelled.Count} clan{(expelled.Count != 1 ? "s" : "")} left and became independent.]"));
        }

        // ── Event 19: Mage Fatwa ─────────────────────────────────────────────
        // Religious terror sweeps a random non-Ashen kingdom. Fanatics hunt
        // mage lords — 0–3 are killed by the mob before the violence is spent.
        // Ashen lords are immune (the mob does not touch what it truly fears).
        //
        // Safety constraints:
        //   • Never kills the player hero.
        //   • Only targets mage lords who are not clan leaders (avoids instant
        //     succession chaos mid-event) and whose clan has ≥ 2 living members.
        //   • Skips kingdoms with no eligible mage lord targets (silent no-fire).
        private static void TryFireMageFatwa()
        {
            if (_rng.NextDouble() >= ChanceMageFatwa) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                var kingdoms = Kingdom.All
                    .Where(k => !k.IsEliminated && k.StringId != AshenKingdomId)
                    .ToList();
                if (kingdoms.Count == 0) return;

                // Weight kingdoms that actually have mage lords
                var eligible = kingdoms
                    .Where(k => Hero.AllAliveHeroes.Any(h =>
                        h.IsLord && h.IsAlive && !h.IsChild && !h.IsPrisoner
                        && h != Hero.MainHero
                        && h.Clan?.Kingdom == k
                        && ColourLordRegistry.IsColourLord(h)
                        && !ColourLordRegistry.IsAshenLord(h)
                        && h.Clan.Leader != h
                        && h.Clan.Heroes.Count(x => x.IsAlive && !x.IsChild) >= 2))
                    .ToList();
                if (eligible.Count == 0) return;

                var kingdom = eligible[_rng.Next(eligible.Count)];

                var targets = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && !h.IsChild && !h.IsPrisoner
                             && h != Hero.MainHero
                             && h.Clan?.Kingdom == kingdom
                             && ColourLordRegistry.IsColourLord(h)
                             && !ColourLordRegistry.IsAshenLord(h)
                             && h.Clan.Leader != h
                             && h.Clan.Heroes.Count(x => x.IsAlive && !x.IsChild) >= 2)
                    .OrderBy(_ => _rng.Next())
                    .Take(_rng.Next(4))   // 0–3
                    .ToList();

                string kingdomName = kingdom.Name?.ToString() ?? "the realm";

                if (targets.Count == 0)
                {
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"Mage Fatwa — fear of the fire and ash swept {kingdomName} like a fever. " +
                        $"Torches were lit. Doors were barred. The mages stayed hidden long enough for the mood to break."));
                    return;
                }

                var killed = new List<string>();
                foreach (var h in targets)
                {
                    try
                    {
                        killed.Add(h.Name?.ToString() ?? "a mage");
                        KillCharacterAction.ApplyByMurder(h, null, false);
                    }
                    catch { }
                }

                string nameList = killed.Count == 1 ? killed[0]
                    : killed.Count == 2 ? $"{killed[0]} and {killed[1]}"
                    : $"{killed[0]}, {killed[1]}, and {killed.Count - 2} others";

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Mage Fatwa — a preacher in {kingdomName} declared that the fire-touched were an abomination. " +
                    $"The crowd agreed. {nameList} did not survive the week. " +
                    $"The mob does not need to understand what it fears — only that it fears it."));
            }
            catch { }
        }

        // ── Event 20: The Temple Rises ────────────────────────────────────────
        // Once per campaign, after campaign day 100: one of the three canonical
        // cities (Diathma/Makeb/Omor) — or any valid Empire/Khuzait/Sturgia town
        // if none are eligible — breaks away. Its owner clan founds The Temple,
        // a militant holy order sworn to end the Ashen. One second clan joins
        // automatically. The player is offered the choice to join too.
        //
        // The Temple is always at war with the Ashen (re-declared daily).
        // It never initiates war on other factions (the AI is too resource-starved
        // with one city; any war it is drawn into is by other factions' choice).
        //
        // Safety constraints:
        //   • Fires at most once (_templeFounded flag, saved/loaded).
        //   • Not before TempleEarliestDay (100).
        //   • Never takes a ruling clan (faction stays viable).
        //   • Never targets a besieged city.
        //   • Never targets a city whose owner clan is the player's.
        //   • Settlement stabilised immediately: loyalty + security forced to 100.
        //   • Kingdom creation uses modern API with legacy MBObjectManager fallback;
        //     any failure at creation time aborts the whole event silently.
        //   • _templeFounded is only set TRUE after the kingdom is confirmed valid.
        private static void TryFireTheTemple()
        {
            if (_templeFounded) return;
            // ChangeKingdomAction.ApplyByJoinToKingdom silently rejects tier-0 clans.
            // Delay the entire event until the player reaches tier 1 so the join offer works.
            if ((Hero.MainHero?.Clan?.Tier ?? 0) < 1) return;
            if (!_debugForceNextTemple)
            {
                if (ElapsedCampaignDays() < TempleEarliestDay) return;
                if (_rng.NextDouble() >= ChanceTheTemple) return;
            }
            _debugForceNextTemple = false;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                // ── Pick the founding city ─────────────────────────────────────
                var preferredNames = new[] { "Diathma", "Makeb", "Omor" }
                    .OrderBy(_ => _rng.Next()).ToArray();

                Settlement chosenCity = null;
                foreach (var pName in preferredNames)
                {
                    var s = Settlement.All.FirstOrDefault(x =>
                        x.IsTown
                        && string.Equals(x.Name?.ToString(), pName, StringComparison.OrdinalIgnoreCase)
                        && IsValidTempleCity(x));
                    if (s != null) { chosenCity = s; break; }
                }

                if (chosenCity == null)
                {
                    // Fallback: any qualifying Empire/Khuzait/Sturgia town
                    var fallbackIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        { "empire_w", "empire", "empire_s", "empire_n", "khuzait", "sturgia" };
                    chosenCity = Settlement.All
                        .Where(x => x.IsTown && IsValidTempleCity(x)
                                 && x.OwnerClan?.Kingdom != null
                                 && fallbackIds.Contains(x.OwnerClan.Kingdom.StringId))
                        .OrderBy(_ => _rng.Next())
                        .FirstOrDefault();
                }

                if (chosenCity == null) return;

                Clan foundingClan   = chosenCity.OwnerClan;
                Kingdom sourceKingdom = foundingClan.Kingdom;

                // ── Find a second clan to join automatically ────────────────────
                Clan secondClan = sourceKingdom?.Clans
                    .Where(c => c != null && !c.IsEliminated
                             && c != foundingClan
                             && c != sourceKingdom.RulingClan
                             && c.Leader != null && c.Leader.IsAlive
                             && !c.Leader.IsChild
                             && c.Leader != Hero.MainHero
                             && c.Heroes.Count(h => h.IsAlive && !h.IsChild) >= 1)
                    .OrderBy(_ => _rng.Next())
                    .FirstOrDefault();

                string cityName   = chosenCity.Name?.ToString()   ?? "a great city";
                string clanName   = foundingClan.Name?.ToString() ?? "the founders";
                string secondName = secondClan?.Name?.ToString();

                // ── Create The Temple kingdom ──────────────────────────────────
                Kingdom temple = null;
                try
                {
                    temple = MBObjectManager.Instance.CreateObject<Kingdom>("the_temple");
                    if (temple == null) return;

                    // InitializeKingdom(name, informalName, culture, banner,
                    //   color1, color2, capitalSettlement,
                    //   encyclopediaText, rulerTitle, rulerDescription)
                    temple.InitializeKingdom(
                        new TextObject("The Temple"),
                        new TextObject("Temple"),
                        foundingClan.Culture,
                        new Banner(foundingClan.Banner.Serialize()),
                        foundingClan.Color,
                        foundingClan.Color2,
                        chosenCity,
                        new TextObject("A militant order sworn to oppose the Ashen at any cost."),
                        new TextObject("High Templar"),
                        new TextObject("Led by those who answered the call when kingdoms would not."));

                    // For a new empty kingdom, ApplyByCreateKingdom makes the clan its ruler.
                    // Fall back to ApplyByJoinToKingdom if the former API isn't available.
                    try   { ChangeKingdomAction.ApplyByCreateKingdom(foundingClan, temple, false); }
                    catch { ChangeKingdomAction.ApplyByJoinToKingdom(foundingClan, temple); }
                }
                catch { temple = null; }

                if (temple == null || temple.IsEliminated) return;
                _templeFounded = true;   // set only once kingdom is confirmed valid

                // ── Transfer and stabilise the founding city ───────────────────
                try
                {
                    ChangeOwnerOfSettlementAction.ApplyByDefault(foundingClan.Leader, chosenCity);
                    if (chosenCity.Town != null)
                    {
                        chosenCity.Town.Loyalty  = 100f;
                        chosenCity.Town.Security = 100f;
                    }
                }
                catch { }

                // ── Second clan joins ──────────────────────────────────────────
                if (secondClan != null)
                    try { ChangeKingdomAction.ApplyByJoinToKingdom(secondClan, temple); } catch { }

                // ── Declare permanent war on the Ashen ─────────────────────────
                try
                {
                    var ashen = Kingdom.All.FirstOrDefault(k =>
                        k.StringId == AshenKingdomId && !k.IsEliminated);
                    if (ashen != null && !temple.IsAtWarWith(ashen))
                        DeclareWarAction.ApplyByDefault(temple, ashen);
                }
                catch { }

                // ── Player prompt ──────────────────────────────────────────────
                string secondLine = secondName != null
                    ? $" {secondName} answered the call before the sun rose."
                    : "";
                string warningLine =
                    (Hero.MainHero?.Clan != null
                  && Hero.MainHero.Clan == Hero.MainHero.Clan?.Kingdom?.RulingClan)
                    ? "\n\n[Warning: you are your faction's ruling clan. Joining will leave your kingdom leaderless.]"
                    : "";

                string body =
                    $"A preacher climbed the steps of the great hall in {cityName} and spoke of fire — " +
                    $"a flame that lives in us all and the cold that walks south to extinguish it.\n\n" +
                    $"He said the kingdoms argued policy while the Ashen marches restlessly. " +
                    $"He said that we must stand against them as one, or perish.\n\n" +
                    $"{clanName} listened. Then they left their old banners behind.{secondLine} " +
                    $"They have raised a new standard: The Temple. " +
                    $"Their only declared war is with the Ashen. " +
                    $"It will not end until one side has run out of ground to stand on.{warningLine}";

                InformationManager.ShowInquiry(new InquiryData(
                    "The Temple Rises",
                    body,
                    true, true,
                    "Join The Temple",
                    "Watch from a distance",
                    () =>
                    {
                        try
                        {
                            var templeK = Kingdom.All.FirstOrDefault(k =>
                                k.StringId == "the_temple" && !k.IsEliminated);
                            if (templeK == null || Hero.MainHero?.Clan == null) return;
                            ChangeKingdomAction.ApplyByJoinToKingdom(Hero.MainHero.Clan, templeK);
                            MBInformationManager.AddQuickInformation(new TextObject(
                                "Your clan answers the call. The Temple's banner is yours now."));
                        }
                        catch { }
                    },
                    null
                ), true);
            }
            catch { }
        }

        // ── Event 21: Peasant Unrest ─────────────────────────────────────────
        // The people have had enough. Three bands of desperate peasants-turned-
        // brigands take to the roads near a random lord's settlement.
        //
        // Safety: looter parties use the same hideout-safe pattern as Ashen Spawn.
        private static void TryFirePeasantUnrest()
        {
            if (_rng.NextDouble() >= ChancePeasantUnrest) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                var kingdoms = Kingdom.All
                    .Where(k => !k.IsEliminated && k.StringId != AshenKingdomId)
                    .ToList();
                if (kingdoms.Count == 0) return;

                var kingdom = kingdoms[_rng.Next(kingdoms.Count)];
                var anchors = Settlement.All
                    .Where(s => (s.IsTown || s.IsCastle) && s.MapFaction == kingdom)
                    .ToList();
                if (anchors.Count == 0) return;

                var anchor = anchors[_rng.Next(anchors.Count)];

                int spawned = 0;
                for (int i = 0; i < 3; i++)
                {
                    if (SpawnLooterParty(anchor.GetPosition2D, 50) != null) spawned++;
                }

                if (spawned > 0)
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"Peasant Unrest — The people of {kingdom.Name} have had enough. " +
                        $"Three ragged bands broke from the fields near {anchor.Name} last night, " +
                        $"carrying scythes and old iron. No lord called them — no lord can stop them easily."));
            }
            catch { }
        }

        // ── Event 22: A Wolf in Sheep's Clothing ─────────────────────────────
        // A minor lord in a random kingdom is accused of serving the Ashen.
        //
        // Not in player's kingdom: silent execution, notification only.
        // Player in kingdom, tier < 4: Charm-modified 33% chance player is accused
        //   and expelled; otherwise a random minor lord is executed.
        // Player in kingdom, tier ≥ 4: four-choice Inquiry (accuse, accuse other,
        //   say nothing, suggest innocence — last has 33% traitor-twist).
        private static void TryFireWolfSheepClothing()
        {
            if (_rng.NextDouble() >= ChanceWolfSheepCloth) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                var kingdoms = Kingdom.All
                    .Where(k => !k.IsEliminated
                             && k.StringId != AshenKingdomId
                             && k.Leader != null
                             && k.Clans.Count(c => c != null && !c.IsEliminated) >= 3)
                    .ToList();
                if (kingdoms.Count == 0) return;

                var kingdom     = kingdoms[_rng.Next(kingdoms.Count)];
                var ruler       = kingdom.Leader;
                string kingdomName = kingdom.Name?.ToString() ?? "the realm";
                string rulerName   = ruler?.Name?.ToString() ?? "the ruler";

                // Collect two candidate minor lords
                var minorLords = kingdom.Clans
                    .Where(c => c != null && !c.IsEliminated
                             && c != kingdom.RulingClan
                             && c.Leader != null && c.Leader.IsAlive
                             && !c.Leader.IsChild
                             && c.Leader != Hero.MainHero
                             && c.Heroes.Count(h => h.IsAlive && !h.IsChild) >= 1)
                    .OrderBy(_ => _rng.Next())
                    .Take(2)
                    .Select(c => c.Leader)
                    .ToList();
                if (minorLords.Count == 0) return;

                var    lord1     = minorLords[0];
                var    lord2     = minorLords.Count > 1 ? minorLords[1] : null;
                string lord1Name = lord1.Name?.ToString() ?? "a lord";
                string lord2Name = lord2?.Name?.ToString() ?? "";
                bool   hasBoth   = lord2 != null && lord2.IsAlive;
                bool   playerIn  = Hero.MainHero?.Clan?.Kingdom == kingdom;

                if (!playerIn)
                {
                    var victim = minorLords[_rng.Next(minorLords.Count)];
                    try { KillCharacterAction.ApplyByMurder(victim, null, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"A Wolf in Sheep's Clothing — {victim.Name} of {kingdomName} was accused " +
                        $"of serving the Ashen. The verdict arrived before they could speak. " +
                        $"Their family maintains innocence. The court did not ask."));
                    return;
                }

                int playerTier = Hero.MainHero?.Clan?.Tier ?? 0;

                if (playerTier < 4)
                {
                    int   charm = Hero.MainHero?.GetSkillValue(DefaultSkills.Charm) ?? 0;
                    float p     = Math.Max(0.05f, 0.33f - charm / 300f * 0.25f);
                    if (_rng.NextDouble() < p)
                    {
                        string clName = Hero.MainHero?.Clan?.Name?.ToString() ?? "your clan";
                        try { ChangeKingdomAction.ApplyByLeaveKingdom(Hero.MainHero.Clan, false); } catch { }
                        MBInformationManager.AddQuickInformation(new TextObject(
                            $"A Wolf in Sheep's Clothing — The whispers of {kingdomName} found {clName}. " +
                            $"There was no real trial. {rulerName} signed the expulsion before midday. " +
                            $"You are cast out. Your Charm softened the odds — this time it was not enough."));
                    }
                    else
                    {
                        var victim = minorLords[_rng.Next(minorLords.Count)];
                        try { KillCharacterAction.ApplyByMurder(victim, null, false); } catch { }
                        MBInformationManager.AddQuickInformation(new TextObject(
                            $"A Wolf in Sheep's Clothing — {kingdomName}'s court needed an answer. " +
                            $"{victim.Name} gave them one by existing. Executed before sunset; " +
                            $"guilt neither proven nor questioned."));
                    }
                    return;
                }

                // Tier ≥ 4: four choices
                var elems = new List<InquiryElement>
                {
                    new InquiryElement("a", $"Accuse {lord1Name} — they are the traitor.", null, true,
                        $"{lord1Name} is executed. +10 with {rulerName}."),
                    new InquiryElement("b",
                        hasBoth ? $"Accuse {lord2Name} — they are the traitor." : "Let the court choose.",
                        null, true,
                        hasBoth ? $"{lord2Name} is executed. +10 with {rulerName}."
                                : "A random lord is chosen. No relation effects."),
                    new InquiryElement("c", "Say nothing. Let the court decide.", null, true,
                        "One is executed at random. No relation effects."),
                    new InquiryElement("d", "Suggest both are innocent. The evidence doesn't hold.", null, true,
                        $"+100 with the accused. 33% chance one was truly a traitor — if so, −10 with {rulerName}."),
                };

                string body =
                    $"The court of {kingdomName} is alive with whispers. " +
                    $"{lord1Name}" + (hasBoth ? $" and {lord2Name} are" : " is") +
                    $" accused of serving the Ashen. The evidence is thin. The mood is not.\n\n" +
                    $"{rulerName} turns to you. At your clan's standing, your voice carries weight.";

                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "A Wolf in Sheep's Clothing",
                    body, elems, false, 1, 1, "Speak", "",
                    chosen =>
                    {
                        try
                        {
                            switch (chosen?[0]?.Identifier as string)
                            {
                                case "a":
                                    try { KillCharacterAction.ApplyByMurder(lord1, null, false); } catch { }
                                    if (ruler?.IsAlive == true && Hero.MainHero != null)
                                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                                            Hero.MainHero, ruler, +10, false); } catch { }
                                    MBInformationManager.AddQuickInformation(new TextObject(
                                        $"A Wolf in Sheep's Clothing — You named {lord1Name}. " +
                                        $"The court accepted it. The execution was before dusk. " +
                                        $"{rulerName} nodded in your direction."));
                                    break;
                                case "b":
                                    if (hasBoth)
                                    {
                                        try { KillCharacterAction.ApplyByMurder(lord2, null, false); } catch { }
                                        if (ruler?.IsAlive == true && Hero.MainHero != null)
                                            try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                                                Hero.MainHero, ruler, +10, false); } catch { }
                                        MBInformationManager.AddQuickInformation(new TextObject(
                                            $"A Wolf in Sheep's Clothing — You named {lord2Name}. " +
                                            $"The court accepted it without debate. " +
                                            $"You bought goodwill, and you know exactly what that cost."));
                                    }
                                    else
                                    {
                                        var v = minorLords[_rng.Next(minorLords.Count)];
                                        try { KillCharacterAction.ApplyByMurder(v, null, false); } catch { }
                                        MBInformationManager.AddQuickInformation(new TextObject(
                                            $"A Wolf in Sheep's Clothing — The court chose. " +
                                            $"{v.Name} did not survive the night."));
                                    }
                                    break;
                                case "c":
                                {
                                    var v = minorLords[_rng.Next(minorLords.Count)];
                                    try { KillCharacterAction.ApplyByMurder(v, null, false); } catch { }
                                    MBInformationManager.AddQuickInformation(new TextObject(
                                        $"A Wolf in Sheep's Clothing — You said nothing. " +
                                        $"The court chose its own answer. {v.Name} did not survive the night. " +
                                        $"You kept your hands clean. Someone's blood was on them regardless."));
                                    break;
                                }
                                case "d":
                                {
                                    if (lord1.IsAlive && Hero.MainHero != null)
                                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                                            Hero.MainHero, lord1, +100, false); } catch { }
                                    if (hasBoth && lord2.IsAlive && Hero.MainHero != null)
                                        try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                                            Hero.MainHero, lord2, +100, false); } catch { }

                                    if (_rng.NextDouble() < 0.33)
                                    {
                                        var traitor = minorLords[_rng.Next(minorLords.Count)];
                                        try { ColourLordRegistry.SetAshen(traitor, true); } catch { }
                                        try { AshenCitySystem.ApplyAshenPersonality(traitor); } catch { }
                                        try { ColourLordRegistry.SetMage(traitor, true); } catch { }
                                        try { AshenCitySystem.OnHeroSetAshen(traitor); } catch { }
                                        try { MageKnowledge.ApplyAshenAppearance(traitor); } catch { }
                                        if (ruler?.IsAlive == true && Hero.MainHero != null)
                                            try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                                                Hero.MainHero, ruler, -10, false); } catch { }
                                        MBInformationManager.AddQuickInformation(new TextObject(
                                            $"A Wolf in Sheep's Clothing — You spoke for their innocence and were believed. " +
                                            $"Three days later, {traitor.Name} vanished from their chambers, " +
                                            $"found among the Ashen — grey-eyed and cold. The accusation was true. " +
                                            $"{rulerName} did not forget that you vouched for them."));
                                    }
                                    else
                                    {
                                        MBInformationManager.AddQuickInformation(new TextObject(
                                            $"A Wolf in Sheep's Clothing — You spoke for their innocence. " +
                                            $"The court, grudgingly, accepted it. The accused remember. " +
                                            $"Whether the accusation had merit, neither you nor anyone else " +
                                            $"will ever be entirely certain."));
                                    }
                                    break;
                                }
                            }
                        }
                        catch { }
                    }, null, "", false), false);
            }
            catch { }
        }

        // ── Event 23: The Ashen Gambit ────────────────────────────────────────
        // Fires at most once per campaign, no earlier than AshenGambitEarliestDay.
        // Ashen assassins — woven through every Imperial court like cold thread —
        // coordinate their move in a single night of dark fire and silence:
        //
        //   Phase 1 — Kill all living Empire faction leaders (silent murder, no
        //             attribution). Skipped leaders: player hero, child heroes,
        //             any king whose kingdom has only 1 surviving clan (succession
        //             must be possible).
        //   Phase 2 — Apply −30 morale to every active Empire lord party.
        //   Phase 3 — Apply −30 security to every Empire town (floor 0).
        //   Phase 4 — Spawn AshenGambitSpawnCount Ashen Spawn warbands, each with
        //             minStrength 80, distributed across Empire settlement anchors.
        //             All spawns use the hideout-safe pattern to prevent crashes.
        //   Phase 4b— Up to AshenGambitCastleCount random Empire castles (not under
        //             siege, not player-owned) are seized by a random Ashen lord via
        //             ChangeOwnerOfSettlementAction. Each castle is stabilised to
        //             prevent an immediate rebellion tick.
        //   Phase 5 — Ensure the Ashen kingdom is at war with every Empire faction,
        //             then surge Ashen lord party morale +50 to drive them onto the
        //             offensive.
        //
        // Sanctuary protection blocks the event with a notification.
        // Safety constraints:
        //   • Never kills the player hero.
        //   • Only kills leaders of kingdoms with ≥ 2 surviving clans.
        //   • Each phase is individually try/caught; one failure cannot abort the rest.
        private static void TryFireAshenGambit()
        {
            if (_ashenGambitFired) return;
            if (ElapsedCampaignDays() < AshenGambitEarliestDay) return;
            if (_rng.NextDouble() >= ChanceAshenGambit) return;
            if (!TryClaimWeeklySlot()) return;

            if (_protectedDaysRemaining > 0)
            {
                MBInformationManager.AddQuickInformation(new TextObject(
                    "The Ashen Gambit — The sanctuary's ward blazes bright. The assassins feel it like a wall of fire " +
                    "and pull back into the dark. Tonight, the Empire's lords sleep safely."));
                return;
            }

            _ashenGambitFired = true;

            // ── Phase 1: Kill all living Empire faction leaders ───────────────
            var killedNames = new List<string>();
            try
            {
                var empireKingdoms = Kingdom.All
                    .Where(k => !k.IsEliminated
                             && EmpireKingdomIds.Contains(k.StringId)
                             && k.Leader != null
                             && k.Leader.IsAlive
                             && !k.Leader.IsChild
                             && k.Leader != Hero.MainHero
                             && k.Clans.Count(c => c != null && !c.IsEliminated) >= 2)
                    .ToList();

                foreach (var kingdom in empireKingdoms)
                {
                    try
                    {
                        killedNames.Add(kingdom.Leader.Name?.ToString() ?? "an emperor");
                        KillCharacterAction.ApplyByMurder(kingdom.Leader, null, false);
                    }
                    catch { }
                }
            }
            catch { }

            // ── Phase 2: −30 morale to all active Empire lord parties ─────────
            int moraleHit = 0;
            try
            {
                foreach (var hero in Hero.AllAliveHeroes)
                {
                    if (!hero.IsLord || hero.IsChild || hero.PartyBelongedTo == null) continue;
                    if (hero.Clan?.Kingdom == null) continue;
                    if (!EmpireKingdomIds.Contains(hero.Clan.Kingdom.StringId)) continue;
                    try
                    {
                        hero.PartyBelongedTo.RecentEventsMorale -= 30f;
                        moraleHit++;
                    }
                    catch { }
                }
            }
            catch { }

            // ── Phase 3: −30 security to every Empire town (floor 0) ─────────
            int secHit = 0;
            try
            {
                foreach (var settlement in Settlement.All)
                {
                    if (!settlement.IsTown || settlement.Town == null) continue;
                    if (!EmpireKingdomIds.Contains(settlement.MapFaction?.StringId)) continue;
                    try
                    {
                        settlement.Town.Security = Math.Max(0f, settlement.Town.Security - 30f);
                        secHit++;
                    }
                    catch { }
                }
            }
            catch { }

            // ── Phase 4: Spawn Ashen Spawn across Empire heartlands ───────────
            int spawned = 0;
            try
            {
                var empireAnchors = Settlement.All
                    .Where(s => (s.IsTown || s.IsCastle)
                             && EmpireKingdomIds.Contains(s.MapFaction?.StringId))
                    .Select(s => s.GetPosition2D)
                    .ToList();

                if (empireAnchors.Count > 0)
                {
                    for (int i = 0; i < AshenGambitSpawnCount; i++)
                    {
                        var anchor = empireAnchors[i % empireAnchors.Count];
                        var party  = SpawnAshenSpawnParty(anchor, baseTroops: 15, minStrength: 80f);
                        if (party != null) spawned++;
                    }
                }
            }
            catch { }

            // ── Phase 4b: Seize Empire castles in the dead of night ──────────
            var seizedNames = new List<string>();
            try
            {
                var empireCastles = Settlement.All
                    .Where(s => s.IsCastle
                             && !s.IsUnderSiege
                             && s.OwnerClan?.Kingdom != null
                             && EmpireKingdomIds.Contains(s.OwnerClan.Kingdom.StringId)
                             && s.OwnerClan.Leader != Hero.MainHero)
                    .OrderBy(_ => _rng.Next())
                    .Take(AshenGambitCastleCount)
                    .ToList();

                var ashenLords = Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && !h.IsDisabled && !h.IsPrisoner
                             && ColourLordRegistry.IsAshenLord(h))
                    .ToList();

                if (ashenLords.Count > 0)
                {
                    foreach (var castle in empireCastles)
                    {
                        try
                        {
                            var lord = ashenLords[_rng.Next(ashenLords.Count)];
                            ChangeOwnerOfSettlementAction.ApplyByDefault(lord, castle);
                            StabiliseSettlement(castle);
                            seizedNames.Add(castle.Name?.ToString() ?? "a castle");
                        }
                        catch { }
                    }
                }
            }
            catch { }

            // ── Phase 5: Ashen go on the offensive ───────────────────────────
            try
            {
                var ashenKingdom = Kingdom.All.FirstOrDefault(k =>
                    k.StringId == AshenKingdomId && !k.IsEliminated);

                if (ashenKingdom != null)
                {
                    foreach (var empire in Kingdom.All
                        .Where(k => !k.IsEliminated && EmpireKingdomIds.Contains(k.StringId)))
                    {
                        try
                        {
                            if (!ashenKingdom.IsAtWarWith(empire))
                                DeclareWarAction.ApplyByDefault(ashenKingdom, empire);
                        }
                        catch { }
                    }
                }

                // Surge Ashen lord party morale — push them into aggressive campaigning
                foreach (var party in MobileParty.All)
                {
                    if (!party.IsActive) continue;
                    var leader = party.LeaderHero;
                    if (leader != null && ColourLordRegistry.IsAshenLord(leader))
                    {
                        try { party.RecentEventsMorale += 50f; } catch { }
                    }
                }
            }
            catch { }

            // ── Notification ──────────────────────────────────────────────────
            string leaderStr = killedNames.Count == 0
                ? "the Imperial thrones stand empty by morning"
                : killedNames.Count == 1
                    ? $"{killedNames[0]} is dead"
                    : killedNames.Count <= 3
                        ? string.Join(", ", killedNames.Take(killedNames.Count - 1))
                          + $" and {killedNames[killedNames.Count - 1]} are dead"
                        : $"{killedNames[0]}, {killedNames[1]}, and {killedNames.Count - 2} other rulers are dead";

            string seizedStr = seizedNames.Count == 0 ? "" :
                seizedNames.Count == 1
                    ? $"{seizedNames[0]} fell to the Ashen before the sun rose. "
                    : string.Join(", ", seizedNames.Take(seizedNames.Count - 1))
                      + $" and {seizedNames[seizedNames.Count - 1]} fell to the Ashen before the sun rose. ";

            MBInformationManager.AddQuickInformation(new TextObject(
                $"The Ashen Gambit — In a single night of cold fire and silence, every Imperial throne was struck at once. " +
                $"{leaderStr}. Their courts woke to ash on the pillows and cooling blood on the floors. " +
                (moraleHit > 0 ? $"Dread swept through {moraleHit} Imperial warbands. " : "") +
                (secHit > 0 ? $"{secHit} Imperial cit{(secHit != 1 ? "ies" : "y")} erupted in panic and suspicion. " : "") +
                seizedStr +
                (spawned > 0
                    ? $"{spawned} Ashen Spawn rose from the shadows across the heartlands before dawn. "
                    : "") +
                "The cold armies do not wait. They march."));
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

        // ── Player-event choice helpers ───────────────────────────────────────

        // Returns true when the player's clan is in the given kingdom at tier 4+.
        private static bool PlayerIsQualifiedForEvent(Kingdom kingdom)
        {
            var player = Hero.MainHero;
            if (player?.Clan == null) return false;
            return player.Clan.Kingdom == kingdom && player.Clan.Tier >= 4;
        }

        // Applies a relation delta between the player and all living adult members
        // of a clan (used by Stolen Heirloom, Tyranny, and Seeds of Betrayal choices).
        private static void PlayerRelationWithClan(Clan clan, int delta)
        {
            if (clan == null || Hero.MainHero == null) return;
            foreach (var h in clan.Heroes)
            {
                if (h == null || !h.IsAlive || h.IsChild || h == Hero.MainHero) continue;
                try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, h, delta, false); } catch { }
            }
        }

        // ── Diplomatic incident helpers ───────────────────────────────────────
        // Maps the relation score between two kings to a probability that an
        // incident escalates to open war rather than cooling diplomatically.
        //   rel < −50 → 85%  (already bitter enemies)
        //   rel < −20 → 65%  (hostile)
        //   rel <  10 → 45%  (cold/neutral)
        //   rel <  40 → 25%  (cordial)
        //          else 10%  (genuine allies)
        private static double WarChanceFromRelation(int rel)
        {
            if (rel < -50) return 0.85;
            if (rel < -20) return 0.65;
            if (rel <  10) return 0.45;
            if (rel <  40) return 0.25;
            return 0.10;
        }

        // Fills ka/kb with two non-Ashen kingdoms that are currently at peace
        // with each other and both have a living leader. Returns false if no
        // such pair exists.
        private static bool TryPickAtPeacePair(out Kingdom ka, out Kingdom kb)
        {
            ka = kb = null;
            var pool = Kingdom.All
                .Where(k => !k.IsEliminated
                         && k.StringId != AshenKingdomId
                         && k.Leader != null && k.Leader.IsAlive && !k.Leader.IsChild)
                .ToList();
            if (pool.Count < 2) return false;

            var pairs = new List<(Kingdom, Kingdom)>();
            for (int i = 0; i < pool.Count; i++)
                for (int j = i + 1; j < pool.Count; j++)
                    if (!pool[i].IsAtWarWith(pool[j]))
                        pairs.Add((pool[i], pool[j]));

            if (pairs.Count == 0) return false;
            var pick = pairs[_rng.Next(pairs.Count)];
            ka = pick.Item1; kb = pick.Item2;
            return true;
        }

        // ── Event 24: A Slight at Court ───────────────────────────────────────
        // An ambassador was publicly turned away from the rival king's hall.
        // If the kings already distrust each other the insult draws steel;
        // otherwise it is swallowed with gritted teeth and lasting bitterness.
        private static void TryFireASlightAtCourt()
        {
            if (_rng.NextDouble() >= ChanceASlightAtCourt) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                if (!TryPickAtPeacePair(out Kingdom ka, out Kingdom kb)) return;

                var la = ka.Leader; var lb = kb.Leader;
                int rel = CharacterRelationManager.GetHeroRelation(la, lb);
                bool goesToWar = _rng.NextDouble() < WarChanceFromRelation(rel);

                string nameA = ka.Name?.ToString() ?? "a kingdom";
                string nameB = kb.Name?.ToString() ?? "a rival";
                string lordA = la?.Name?.ToString() ?? "its lord";
                string lordB = lb?.Name?.ToString() ?? "its lord";

                if (goesToWar)
                {
                    try { DeclareWarAction.ApplyByDefault(ka, kb); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"A Slight at Court — {lordB} of {nameB} turned away {nameA}'s envoy in the great hall " +
                        $"and had words said in front of witnesses that could not be unsaid. " +
                        $"{lordA} answered the insult with a sealed declaration. " +
                        $"{nameA} and {nameB} are now at war."));
                }
                else
                {
                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(la, lb, -15, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"A Slight at Court — {lordB} of {nameB} turned away {nameA}'s envoy, " +
                        $"but {lordA} chose restraint over retaliation. " +
                        $"The humiliation is remembered. The border is quieter than it should be."));
                }
            }
            catch { }
        }

        // ── Event 25: Border Torches ──────────────────────────────────────────
        // Villages near the shared border burned in the night. Neither crown
        // claims the act; each accuses the other. Whether war follows depends
        // on how much the kings already suspect each other.
        private static void TryFireBorderTorches()
        {
            if (_rng.NextDouble() >= ChanceBorderTorches) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                if (!TryPickAtPeacePair(out Kingdom ka, out Kingdom kb)) return;

                var la = ka.Leader; var lb = kb.Leader;
                int rel = CharacterRelationManager.GetHeroRelation(la, lb);
                bool goesToWar = _rng.NextDouble() < WarChanceFromRelation(rel);

                string nameA = ka.Name?.ToString() ?? "a kingdom";
                string nameB = kb.Name?.ToString() ?? "a rival";
                string lordA = la?.Name?.ToString() ?? "its lord";
                string lordB = lb?.Name?.ToString() ?? "its lord";

                if (goesToWar)
                {
                    try { DeclareWarAction.ApplyByDefault(ka, kb); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"Border Torches — villages on the border between {nameA} and {nameB} " +
                        $"burned in the night. Both sides blame the other. " +
                        $"The smoke was still rising when the first cavalry crossed the line. " +
                        $"{nameA} and {nameB} are at war."));
                }
                else
                {
                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(la, lb, -10, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"Border Torches — villages between {nameA} and {nameB} burned. " +
                        $"Accusations flew on both sides. {lordA} and {lordB} pulled back from the edge, " +
                        $"though neither believes the other's denials. The border is tense. The ashes are still warm."));
                }
            }
            catch { }
        }

        // ── Event 26: A Debt in Blood ─────────────────────────────────────────
        // A lord's envoy was found dead in the rival kingdom's territory,
        // his seal broken and his escort missing. The accusation of murder
        // poisons what little trust remained between the two crowns.
        private static void TryFireADebtInBlood()
        {
            if (_rng.NextDouble() >= ChanceADebtInBlood) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                if (!TryPickAtPeacePair(out Kingdom ka, out Kingdom kb)) return;

                var la = ka.Leader; var lb = kb.Leader;
                int rel = CharacterRelationManager.GetHeroRelation(la, lb);
                bool goesToWar = _rng.NextDouble() < WarChanceFromRelation(rel);

                string nameA = ka.Name?.ToString() ?? "a kingdom";
                string nameB = kb.Name?.ToString() ?? "a rival";
                string lordA = la?.Name?.ToString() ?? "its lord";
                string lordB = lb?.Name?.ToString() ?? "its lord";

                if (goesToWar)
                {
                    try { DeclareWarAction.ApplyByDefault(ka, kb); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"A Debt in Blood — a {nameA} envoy was found dead in {nameB} territory, " +
                        $"his seal broken and his escort nowhere to be found. " +
                        $"{lordA} did not wait for an inquiry. " +
                        $"{nameA} and {nameB} are at war."));
                }
                else
                {
                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(la, lb, -20, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"A Debt in Blood — a {nameA} envoy was found dead in {nameB} territory. " +
                        $"{lordB} opened an inquiry and sent condolences. {lordA} accepted both, barely. " +
                        $"The truth may never surface. The suspicion will not leave."));
                }
            }
            catch { }
        }

        // ── Event 27: The Broken Betrothal ───────────────────────────────────
        // A political marriage arranged between two noble houses was dissolved —
        // one side backed out, or the bride fled, or a scandal made it impossible.
        // The offended house demands satisfaction. Kings who already mistrust each
        // other interpret the slight as a deliberate act of war; those on good
        // terms manage a quiet settlement and an awkward silence.
        private static void TryFireBrokenBetrothal()
        {
            if (_rng.NextDouble() >= ChanceBrokenBetrothal) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                if (!TryPickAtPeacePair(out Kingdom ka, out Kingdom kb)) return;

                var la = ka.Leader; var lb = kb.Leader;
                int rel = CharacterRelationManager.GetHeroRelation(la, lb);
                bool goesToWar = _rng.NextDouble() < WarChanceFromRelation(rel);

                string nameA = ka.Name?.ToString() ?? "a kingdom";
                string nameB = kb.Name?.ToString() ?? "a rival";
                string lordA = la?.Name?.ToString() ?? "its lord";
                string lordB = lb?.Name?.ToString() ?? "its lord";

                if (goesToWar)
                {
                    try { DeclareWarAction.ApplyByDefault(ka, kb); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"The Broken Betrothal — the marriage between {nameA} and {nameB} was called off " +
                        $"before the ink was dry on the compact. The insult was too great and the timing too suspicious. " +
                        $"{lordA} returned the gifts and sent soldiers instead. " +
                        $"{nameA} and {nameB} are at war."));
                }
                else
                {
                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(la, lb, -15, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"The Broken Betrothal — the marriage between {nameA} and {nameB} fell apart before it began. " +
                        $"Gifts were quietly returned. No one mentioned it at court. " +
                        $"{lordA} and {lordB} exchanged letters that said nothing and meant everything. " +
                        $"The alliance is over. War was avoided, this time."));
                }
            }
            catch { }
        }

        // ── Event 28: The Treasonous Scroll ───────────────────────────────────
        // Letters were intercepted — or claimed to have been — proving that
        // agents of one kingdom have been bribing officials in the other.
        // Whether the plot is real or fabricated matters less than the accusation:
        // kings who already suspect each other rarely need much convincing.
        private static void TryFireTreasonousScroll()
        {
            if (_rng.NextDouble() >= ChanceTreasonousScroll) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                if (!TryPickAtPeacePair(out Kingdom ka, out Kingdom kb)) return;

                var la = ka.Leader; var lb = kb.Leader;
                int rel = CharacterRelationManager.GetHeroRelation(la, lb);
                bool goesToWar = _rng.NextDouble() < WarChanceFromRelation(rel);

                string nameA = ka.Name?.ToString() ?? "a kingdom";
                string nameB = kb.Name?.ToString() ?? "a rival";
                string lordA = la?.Name?.ToString() ?? "its lord";
                string lordB = lb?.Name?.ToString() ?? "its lord";

                if (goesToWar)
                {
                    try { DeclareWarAction.ApplyByDefault(ka, kb); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"The Treasonous Scroll — letters surfaced in {nameA} proving, or appearing to prove, " +
                        $"that {nameB} agents have been buying lords and poisoning counsel inside the court. " +
                        $"{lordA} read the letters, had the messengers arrested, and called his banners. " +
                        $"{nameA} and {nameB} are at war."));
                }
                else
                {
                    try { ChangeRelationAction.ApplyRelationChangeBetweenHeroes(la, lb, -20, false); } catch { }
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"The Treasonous Scroll — letters surfaced in {nameA} alleging {nameB} spies " +
                        $"inside the court. {lordB} denied everything and offered to open his archives. " +
                        $"{lordA} accepted the offer with a smile that did not reach his eyes. " +
                        $"Both crowns know the investigation will find nothing. Both crowns remember."));
                }
            }
            catch { }
        }

        // ── Event 23: Embers of Hope ──────────────────────────────────────────
        // Fires once the Ashen kingdom holds at least 6 towns.
        // The weight of a common darkness is enough to still old hatreds —
        // up to 3 random wars between non-Ashen kingdoms are ended as rivals
        // recognise that a greater threat walks among them.
        private static void TryFireEmbersOfHope()
        {
            if (_rng.NextDouble() >= ChanceEmbersOfHope) return;
            if (!TryClaimWeeklySlot()) return;
            try
            {
                // Condition: Ashen must hold at least EmbersOfHopeMinTowns towns.
                var ashen = Kingdom.All.FirstOrDefault(k => k.StringId == AshenKingdomId && !k.IsEliminated);
                if (ashen == null) return;

                int ashenTowns = Settlement.All.Count(s => s.IsTown && s.Town != null && s.MapFaction == ashen);
                if (ashenTowns < EmbersOfHopeMinTowns) return;

                // Collect every active war between two non-Ashen kingdoms.
                var kingdoms = Kingdom.All
                    .Where(k => !k.IsEliminated && k.StringId != AshenKingdomId)
                    .ToList();

                var warPairs = new List<(Kingdom a, Kingdom b)>();
                for (int i = 0; i < kingdoms.Count; i++)
                    for (int j = i + 1; j < kingdoms.Count; j++)
                        if (kingdoms[i].IsAtWarWith(kingdoms[j]))
                            warPairs.Add((kingdoms[i], kingdoms[j]));

                if (warPairs.Count == 0) return;

                // Fisher-Yates shuffle, then take up to EmbersOfHopePeaceCount pairs.
                for (int i = warPairs.Count - 1; i > 0; i--)
                {
                    int j = _rng.Next(i + 1);
                    var tmp = warPairs[i]; warPairs[i] = warPairs[j]; warPairs[j] = tmp;
                }

                var peacedNames = new List<string>();
                foreach (var (a, b) in warPairs.Take(EmbersOfHopePeaceCount))
                {
                    try
                    {
                        MakePeaceAction.Apply(a, b);
                        peacedNames.Add($"{a.Name} and {b.Name}");
                    }
                    catch { }
                }

                if (peacedNames.Count == 0) return;

                string conflicts = string.Join("; ", peacedNames);
                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Embers of Hope — the Ashen hold {ashenTowns} cities now. " +
                    $"Beneath that shadow old quarrels feel small and foolish. " +
                    $"Banners are lowered and bitter words withdrawn: " +
                    $"{peacedNames.Count} war{(peacedNames.Count != 1 ? "s" : "")} end{(peacedNames.Count == 1 ? "s" : "")}: {conflicts}."));
            }
            catch { }
        }

        // ── Event throttle helpers ────────────────────────────────────────────
        // Called by each TryFireXxx after its probability roll succeeds.
        // Returns true (and claims the slot) if no event has fired this tick yet.
        // Returns false if another event already claimed the slot this tick.
        private static bool TryClaimWeeklySlot()
        {
            if (_weeklySlotFilled) return false;
            _weeklySlotFilled = true;
            return true;
        }

        // ── Elapsed-days helper ───────────────────────────────────────────────
        // Returns days elapsed since the campaign started.
        // Falls back to absolute ToDays for saves loaded without the start-day record.
        private static double ElapsedCampaignDays()
            => _campaignStartDay >= 0
               ? Math.Max(0.0, CampaignTime.Now.ToDays - _campaignStartDay)
               : CampaignTime.Now.ToDays;

        // ── Seasonal helpers ──────────────────────────────────────────────────
        // Use Bannerlord's own GetSeasonOfYear property (returns CampaignTime.Seasons enum)
        // so season detection matches exactly what the player sees on screen.
        private static bool IsWinter() => CampaignTime.Now.GetSeasonOfYear == CampaignTime.Seasons.Winter;
        private static bool IsSummer() => CampaignTime.Now.GetSeasonOfYear == CampaignTime.Seasons.Summer;
        private static bool IsSpring()  => CampaignTime.Now.GetSeasonOfYear == CampaignTime.Seasons.Spring;
        private static bool IsAutumn()  => CampaignTime.Now.GetSeasonOfYear == CampaignTime.Seasons.Autumn;

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

                // Bannerlord crashes on post-battle loot screen if the party's home hideout
                // is null — the engine tries to scatter survivors back to it.
                // Priority: bandit clan's own hideout → nearest hideout → any world hideout.
                // If nothing exists, bail out rather than passing null to CreateBanditParty.
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
                    if (hs == null)
                        hs = Settlement.All.FirstOrDefault(s => s?.Hideout != null);
                    hideout = hs?.Hideout;
                }
                catch { }
                if (hideout == null) return null; // no valid hideout → skip to prevent native crash

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
            store.SyncData("LDM_LongNightDays",   ref _longNightDaysRemaining);
            store.SyncData("LDM_BrokenWillFired", ref _brokenWillFired);
            var brokenList = _brokenKingdomIds.ToList();
            store.SyncData("LDM_BrokenKingdoms",  ref brokenList);
            if (brokenList != null)
            {
                _brokenKingdomIds.Clear();
                foreach (var id in brokenList) _brokenKingdomIds.Add(id);
            }

            var gotK = _gotKingdoms.ToList();
            var gotD = _gotDays.ToList();
            store.SyncData("LDM_GoTKingdoms", ref gotK);
            store.SyncData("LDM_GoTDays",     ref gotD);
            if (gotK != null && gotD != null && gotK.Count == gotD.Count)
            {
                _gotKingdoms.Clear(); _gotDays.Clear();
                for (int i = 0; i < gotK.Count; i++) { _gotKingdoms.Add(gotK[i]); _gotDays.Add(gotD[i]); }
            }

            int templeFounded = _templeFounded ? 1 : 0;
            store.SyncData("LDM_TempleFounded",    ref templeFounded);
            store.SyncData("LDM_ProtectedDays",    ref _protectedDaysRemaining);
            _templeFounded = templeFounded != 0;

            int gambitFired = _ashenGambitFired ? 1 : 0;
            store.SyncData("LDM_AshenGambitFired", ref gambitFired);
            _ashenGambitFired = gambitFired != 0;

            store.SyncData("LDM_CampaignStartDay",  ref _campaignStartDay);
            store.SyncData("LDM_LastEventDay",       ref _lastEventElapsedDay);
        }
    }
}
