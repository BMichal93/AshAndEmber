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
        public const float ChanceIronWinter      = 0.04f;  // ~every 25 weeks  (rare, winter only)
        public const float ChanceScorchingSun    = 0.04f;  // ~every 25 weeks  (rare, summer only)

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

        // The Long March: which kingdoms are eligible targets (vanilla Bannerlord string IDs)
        private static readonly string[] LongMarchTargets = { "vlandia", "aserai", "khuzait", "sturgia" };

        // Iron Winter: northern kingdoms — Sturgia and the Northern Empire endure the worst of the freeze.
        private static readonly string[] NorthernKingdoms = { "sturgia", "empire_n" };

        // Scorching Sun: desert kingdoms — Aserai and the Southern Empire bake in the heat.
        private static readonly string[] DesertKingdoms = { "aserai", "empire_s" };

        // ── Runtime state ─────────────────────────────────────────────────────
        private static int _longNightDaysRemaining = 0;
        private static int _brokenWillFired        = 0;
        private static readonly HashSet<string> _brokenKingdomIds = new HashSet<string>();
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
            TryFireSeedsOfBetrayal();
            TryFireBrokenWill();
            TryFireTheLongMarch();
            TryFireWhispersFromTheAsh();
            TryFireTyranny();
            TryFireStolenHeirloom();
            TryFireIronWinter();
            TryFireScorchingSun();
        }

        /// Resets state for a fresh new game (called from OnNewGameCreated).
        public static void ResetForNewGame()
        {
            _longNightDaysRemaining = 0;
            _brokenWillFired        = 0;
            _brokenKingdomIds.Clear();
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

        // ── Event 8: Seeds of Betrayal ────────────────────────────────────────
        // A faction leader is murdered by their own court. The murderous clan is
        // expelled from the faction. Inspired by the Red Wedding.
        //
        // Safety constraints:
        //   • Excludes the player and the player's faction entirely.
        //   • Requires the target faction to have ≥ 3 clans so one expulsion does
        //     not immediately collapse the realm.
        //   • Uses ApplyByMurder(leader, null, false) — neutral quiet death, engine
        //     handles succession automatically.
        //   • The expelled clan uses ApplyByLeaveKingdom — safe at any time.
        private static void TryFireSeedsOfBetrayal()
        {
            if (_rng.NextDouble() >= ChanceSeedsOfBetrayal) return;
            try
            {
                // Find a faction leader who is not the player and whose kingdom has 3+ clans
                var candidates = Kingdom.All
                    .Where(k => !k.IsEliminated
                             && k.StringId != AshenKingdomId
                             && k.Leader != null
                             && k.Leader != Hero.MainHero
                             && k.Leader.IsAlive
                             && !k.Leader.IsPrisoner
                             && !k.Leader.IsChild
                             && k != Hero.MainHero?.Clan?.Kingdom  // spare player's faction
                             && k.Clans.Count(c => c != null && !c.IsEliminated) >= 3)
                    .ToList();
                if (candidates.Count == 0) return;

                var kingdom = candidates[_rng.Next(candidates.Count)];
                var leader  = kingdom.Leader;

                // Pick a non-ruling, non-player clan to blame — the "conspirators"
                var scapegoats = kingdom.Clans
                    .Where(c => c != null && !c.IsEliminated
                             && c != kingdom.RulingClan
                             && (c.Leader == null || c.Leader != Hero.MainHero)
                             && c.Heroes.Any(h => h.IsAlive && !h.IsChild))
                    .ToList();

                Clan expelled = scapegoats.Count > 0 ? scapegoats[_rng.Next(scapegoats.Count)] : null;

                // The blood price — kill the leader quietly; engine handles succession
                string leaderName   = leader.Name?.ToString() ?? "the lord";
                string kingdomName  = kingdom.Name?.ToString() ?? "the realm";
                string expelledName = expelled?.Name?.ToString() ?? "a noble house";

                try { KillCharacterAction.ApplyByMurder(leader, null, false); } catch { }

                // Expel the responsible clan
                if (expelled != null && expelled.Kingdom == kingdom)
                    try { ChangeKingdomAction.ApplyByLeaveKingdom(expelled, false); } catch { }

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Seeds of Betrayal — {leaderName} of {kingdomName} did not survive the feast. " +
                    $"The wine was poisoned. The doors were barred. {expelledName} fled before dawn, " +
                    $"their banners cut from the hall. Someone will sit the seat they left empty. " +
                    $"Someone always does."));
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
        private static bool _declaringBrokenWill = false;
        private static void TryFireBrokenWill()
        {
            if (_brokenWillFired >= BrokenWillMaxFires) return;
            if (CampaignTime.Now.ToDays < BrokenWillEarliestDay) return;
            if (_rng.NextDouble() >= ChanceBrokenWill) return;
            if (_declaringBrokenWill) return;

            try
            {
                var candidates = Kingdom.All
                    .Where(k => !k.IsEliminated
                             && k.StringId != AshenKingdomId
                             && !_brokenKingdomIds.Contains(k.StringId)
                             && k != Hero.MainHero?.Clan?.Kingdom
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
                    $"It only waits."));
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
                    $"Their former lords received only a letter — unsigned, unaddressed, already cold."));
            }
            catch { }
        }

        // ── Event 12: Tyranny ────────────────────────────────────────────────
        // A faction leader's paranoia turns lethal. All tier-5 and tier-6 clan
        // heads within their realm are executed in a single night. The ruling
        // clan is bankrupted — influence drained to zero. One of the executed
        // clans defects before the blade falls.
        //
        // Safety constraints:
        //   • Never targets the player or the player's faction.
        //   • Only kills clan leaders whose clan has ≥ 2 living members (no
        //     clan extinction mid-event).
        //   • Requires at least one tier-5/6 non-ruling clan to exist.
        //   • Defecting clan uses ApplyByLeaveKingdom (safe outside ClanChangedKingdom).
        //   • Ruling clan influence floor is 0f (never negative).
        private static void TryFireTyranny()
        {
            if (_rng.NextDouble() >= ChanceTyranny) return;
            try
            {
                // Find a kingdom with tier-5/6 non-ruling clans and a leader who isn't the player
                var kingdoms = Kingdom.All
                    .Where(k => !k.IsEliminated
                             && k.StringId != AshenKingdomId
                             && k != Hero.MainHero?.Clan?.Kingdom
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

                // Collect the condemned clans
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

                // One clan defects before the execution — they escape; the rest do not
                Clan defector = condemned[_rng.Next(condemned.Count)];
                try { ChangeKingdomAction.ApplyByLeaveKingdom(defector, false); } catch { }

                // Execute the rest's clan leaders
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

                // Drain the ruling clan's influence to zero — the purge cost everything
                try { ruling.Influence = 0f; } catch { }

                string tyrantName   = tyrant?.Name?.ToString()    ?? "the lord";
                string kingdomName  = kingdom.Name?.ToString()    ?? "the realm";
                string defectorName = defector?.Name?.ToString()  ?? "one house";
                string exList = executed.Count == 0 ? "none"
                    : executed.Count <= 3 ? string.Join(", ", executed)
                    : $"{executed[0]}, {executed[1]}, and {executed.Count - 2} others";

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Tyranny — {tyrantName} of {kingdomName} called their great lords to feast " +
                    $"and did not let them leave. {exList} — dead before dawn. " +
                    $"{defectorName} read the invitation and chose the road instead. " +
                    $"The throne room is emptier now. So is the treasury of those who held it."));
            }
            catch { }
        }

        // ── Event 13: Stolen Heirloom ─────────────────────────────────────────
        // A rival clan within a faction seizes power — the faction leader changes
        // to the head of a different clan inside the same kingdom.
        //
        // Uses ChangeRulingClanAction.Apply(newClan) which is the engine's own
        // ruling-clan transition. Falls back to a no-op if the action throws.
        //
        // Safety constraints:
        //   • Excludes the player's faction and the Ashen kingdom.
        //   • Requires ≥ 2 non-eliminated clans in the kingdom.
        //   • The new ruling clan must already be a member of the kingdom.
        //   • Wrapped entirely in try/catch; a failure is silent and harmless.
        private static void TryFireStolenHeirloom()
        {
            if (_rng.NextDouble() >= ChanceStolenHeirloom) return;
            try
            {
                var kingdoms = Kingdom.All
                    .Where(k => !k.IsEliminated
                             && k.StringId != AshenKingdomId
                             && k != Hero.MainHero?.Clan?.Kingdom
                             && k.RulingClan != null
                             && k.Clans.Count(c => c != null && !c.IsEliminated) >= 2)
                    .ToList();
                if (kingdoms.Count == 0) return;

                var kingdom = kingdoms[_rng.Next(kingdoms.Count)];

                // Candidate: any non-ruling, non-eliminated clan in the same kingdom
                // whose leader is alive, adult, and not the player
                var rivals = kingdom.Clans
                    .Where(c => c != null && !c.IsEliminated
                             && c != kingdom.RulingClan
                             && c.Leader != null
                             && c.Leader.IsAlive
                             && !c.Leader.IsChild
                             && c.Leader != Hero.MainHero)
                    .ToList();
                if (rivals.Count == 0) return;

                var usurper     = rivals[_rng.Next(rivals.Count)];
                string oldName  = kingdom.RulingClan?.Name?.ToString() ?? "the old house";
                string newName  = usurper.Name?.ToString()             ?? "a rival house";
                string kingName = kingdom.Name?.ToString()             ?? "the realm";

                try { ChangeRulingClanAction.Apply(kingdom, usurper); } catch { }

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Stolen Heirloom — the signet ring of {kingName} changed hands in the night. " +
                    $"{newName} holds the seal now. {oldName} held it at sundown. " +
                    $"No swords were drawn. That may be the most frightening part."));
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
            try
            {
                int villages = 0, towns = 0;
                foreach (var s in Settlement.All)
                {
                    if (s == null) continue;
                    string factionId = s.MapFaction?.StringId ?? "";
                    bool isNorth = System.Array.IndexOf(NorthernKingdoms, factionId) >= 0;
                    if (!isNorth) continue;

                    if (s.IsVillage && s.Village != null)
                    {
                        try
                        {
                            s.Village.Hearth = Math.Max(10f, s.Village.Hearth * 0.5f);
                            villages++;
                        }
                        catch { }
                    }
                    else if (s.IsTown && s.Town != null)
                    {
                        try
                        {
                            s.Town.Prosperity  = Math.Max(10f, s.Town.Prosperity  * 0.5f);
                            s.Town.FoodStocks  = Math.Max(10f, s.Town.FoodStocks  * 0.5f);
                            towns++;
                        }
                        catch { }
                    }
                }

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Iron Winter — the cold came early and stayed. " +
                    $"{villages} village{(villages != 1 ? "s" : "")} in the north cannot keep their fires lit. " +
                    $"{towns} cit{(towns != 1 ? "ies" : "y")} ha{(towns != 1 ? "ve" : "s")} halved their stores. " +
                    $"The roads north are quiet in the wrong way."));
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
            try
            {
                int villages = 0, towns = 0;
                foreach (var s in Settlement.All)
                {
                    if (s == null) continue;
                    string factionId = s.MapFaction?.StringId ?? "";
                    bool isDesert = System.Array.IndexOf(DesertKingdoms, factionId) >= 0;
                    if (!isDesert) continue;

                    if (s.IsVillage && s.Village != null)
                    {
                        try
                        {
                            s.Village.Hearth = Math.Max(10f, s.Village.Hearth * 0.5f);
                            villages++;
                        }
                        catch { }
                    }
                    else if (s.IsTown && s.Town != null)
                    {
                        try
                        {
                            s.Town.Prosperity  = Math.Max(10f, s.Town.Prosperity  * 0.5f);
                            s.Town.FoodStocks  = Math.Max(10f, s.Town.FoodStocks  * 0.5f);
                            towns++;
                        }
                        catch { }
                    }
                }

                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Scorching Sun — the sky has been white with heat for three weeks. " +
                    $"The wells in {villages} southern village{(villages != 1 ? "s" : "")} are low or dry. " +
                    $"{towns} desert cit{(towns != 1 ? "ies" : "y")} ha{(towns != 1 ? "ve" : "s")} rationed their stores. " +
                    $"The caravans carry less. The people eat less. The land remembers."));
            }
            catch { }
        }

        // ── Seasonal helpers ──────────────────────────────────────────────────
        // Bannerlord has 84 days per year, 4 seasons of 21 days each.
        // Season index: 0 = Spring, 1 = Summer, 2 = Autumn, 3 = Winter.
        private static int GetSeasonIndex()
            => (int)(CampaignTime.Now.ToDays % 84.0) / 21;

        private static bool IsWinter() => GetSeasonIndex() == 3;
        private static bool IsSummer() => GetSeasonIndex() == 1;

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
        }
    }
}
