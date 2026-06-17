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
// ├──────────────────────┼─────────────────────────────────────────────────────┤
// │ The Dead March       │ (Day 50, then ~every 110 days) A necromantic rite   │
// │                      │ raises the Ashen fallen. Every Ashen garrison and   │
// │                      │ lord party is reinforced with 40–80 troops spread   │
// │                      │ across tiers 2, 3, and 4 (~⅓ each).               │
// ├──────────────────────┼─────────────────────────────────────────────────────┤
// │ The Undying Host     │ (Once per campaign, day 80+, growing chance after   │
// │                      │ day 200) The Ashen's greatest lord is chosen. 5 000 │
// │                      │ troops are forged into their party. Their clan      │
// │                      │ receives crushing influence. The host marches.      │
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
    public static partial class CampaignMapEvents
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
        public const float ChanceWhispers        = 0.030f; // ~every 33 weeks  (rare, was 67)
        public const float ChanceTyranny         = 0.02f;  // ~every 50 weeks  (very rare)
        public const float ChanceStolenHeirloom  = 0.02f;  // ~every 50 weeks  (very rare)
        public const float ChancePeasantUnrest   = 0.06f;  // ~every 17 weeks  (medium — like Great Withering)
        public const float ChanceWolfSheepCloth = 0.03f;  // ~every 33 weeks  (rare but not very)
        public const float ChanceMageFatwa      = 0.025f; // ~every 40 weeks  (rare)
        public const int   TempleEarliestDay    = 120;   // first possible trigger day
        public const float ChanceTheTemple      = 0.33f; // day 120–239: ~33% per eligible tick
        public const int   TempleSecondTierDay  = 240;   // second escalation
        public const float ChanceTempleSecond   = 0.67f; // day 240–399: ~67% per eligible tick
        public const int   TempleNearCertainDay = 400;   // final escalation
        public const float ChanceTempleLatent   = 0.90f; // day 400+: ~90% per eligible tick
        public const float ChanceIronWinter      = 0.04f;  // ~every 25 weeks  (rare, winter only)
        public const float ChanceScorchingSun    = 0.04f;  // ~every 25 weeks  (rare, summer only)
        public const float ChanceFirstGreen      = 0.04f;  // ~every 25 weeks  (rare, spring only)
        public const float ChanceAmberHarvest    = 0.04f;  // ~every 25 weeks  (rare, autumn only)
        public const float ChanceEmbersOfHope    = 0.06f;  // ~every 17 weeks  (once Ashen hold 8+ towns)
        public const int   EmbersOfHopeMinTowns  = 13;     // Ashen must hold this many towns to trigger
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

        // The Dead March: first fire forced on day 50; recurs every ~110 days (chance-based, 95-day gap)
        public const float ChanceDeadMarch        = 0.15f; // ~7 weeks after eligible → ~110d avg cycle
        public const int   DeadMarchFirstDay      = 50;    // forced first fire (no chance roll)
        public const int   DeadMarchRecurrenceGap = 95;    // minimum days between subsequent fires
        public const int   DeadMarchMinTroops     = 40;    // troops added per garrison / army (mixed tiers 2–4)
        public const int   DeadMarchMaxTroops     = 80;    // (random in [min, max])

        // The Undying Host: once-per-campaign conquest event (day 80+, scales after day 200)
        public const float ChanceUndyingHostBase     = 0.04f;  // ~4% per week after day 200 ramp (~25 weeks to near-certain)
        public const int   UndyingHostEarliestDay    = 80;     // cannot fire before this day
        public const int   UndyingHostRampEndDay     = 200;    // ramp reaches full ChanceUndyingHostBase by this day
        public const int   UndyingHostNearCertainDay = 400;    // chance spikes to ChanceUndyingHostLatent after this day
        public const float ChanceUndyingHostLatent   = 0.60f;  // ~60% per week — fires within 1–2 weeks past day 400
        public const int   UndyingHostTroopCount     = 5000;   // troops added to the chosen Ashen lord's party
        public const float UndyingHostInfluenceGrant = 50000f; // influence floored to this for the Ashen ruling clan

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

        // Settlement scars: settlements visibly marked by recent events.
        // Persisted so reloads don't silently erase them mid-window.
        private static readonly List<string> _scarredIds   = new List<string>();
        private static readonly List<int>    _scarredDays  = new List<int>();
        private static readonly List<int>    _scarredTypes = new List<int>(); // 0=withering 1=plague 2=longnight

        // Battlefield echo: pending spawn from a spell-heavy battle
        private static bool  _battleEchoPending = false;
        private static float _battleEchoPosX    = 0f;
        private static float _battleEchoPosY    = 0f;

        /// Called from BattleEvents.OnMissionEnd() when the player cast 3+ battle spells.
        public static void SetBattleEcho(float x, float y)
        {
            _battleEchoPending = true;
            _battleEchoPosX    = x;
            _battleEchoPosY    = y;
        }

        // Game of Thrones: kingdom IDs queued for delayed clan-ejection
        private static readonly List<string> _gotKingdoms = new List<string>();
        private static readonly List<int>    _gotDays     = new List<int>();

        private static readonly Random _rng = new Random();

        // ── Public API ────────────────────────────────────────────────────────

        /// Returns true while the Long Night event is active.
        /// Called by SpellEffects.GetCampaignLightLevel() to force Dark.
        public static bool IsLongNight() => _longNightDaysRemaining > 0;

        private static void RecordScar(string id, int type)
        {
            int existing = _scarredIds.IndexOf(id);
            if (existing >= 0) { _scarredDays[existing] = 30; _scarredTypes[existing] = type; return; }
            _scarredIds.Add(id);
            _scarredDays.Add(30);
            _scarredTypes.Add(type);
        }

        /// Returns a one-line flavor string if the settlement bears a recent event scar, or null.
        public static string GetSettlementScar(Settlement s)
        {
            if (s == null) return null;
            int idx = _scarredIds.IndexOf(s.StringId);
            if (idx < 0 || _scarredDays[idx] <= 0) return null;
            return _scarredTypes[idx] switch
            {
                1 => $"The grey sickness has not left {s.Name}. You see bandaged soldiers in doorways and shuttered stalls in the market.",
                2 => $"The Long Night marked {s.Name}. Torches burn in rooms that should be dark, and no one speaks of what they heard during the seven days.",
                _ => $"Something cold passed through {s.Name} not long ago. The people are quieter than they should be.",
            };
        }

        // ── Called from CampaignBehavior.OnHeroKilled ────────────────────────
        // Triggered when a faction leader dies. Rolls 5% chance and queues a
        // 2-day delayed Game of Thrones event for that kingdom.
        // Ashen do not fracture — their will is cold and singular.
        public static void OnFactionLeaderKilled(Kingdom kingdom)
        {
            if (kingdom == null || kingdom.IsEliminated) return;
            if (kingdom.StringId == AshenKingdomId) return;         // Ashen never fracture
            if (Hero.MainHero?.Clan?.Kingdom == kingdom) return;    // never fracture the player's faction
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
            // Battlefield echo: spawn small Ashen presence near a spell-heavy battle site
            if (_battleEchoPending)
            {
                _battleEchoPending = false;
                try
                {
                    var ePos = new Vec2(_battleEchoPosX, _battleEchoPosY);
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The rite left marks on the field. The fire remembers where it burned — and calls to what was watching.",
                        new Color(0.38f, 0.50f, 0.75f)));
                    SpawnAshenSpawnParty(ePos, 2, 30f);
                }
                catch { }
            }

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

            // Tick down settlement scars
            for (int i = _scarredDays.Count - 1; i >= 0; i--)
            {
                _scarredDays[i]--;
                if (_scarredDays[i] <= 0)
                {
                    _scarredIds.RemoveAt(i);
                    _scarredDays.RemoveAt(i);
                    _scarredTypes.RemoveAt(i);
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
                {
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "Long Night — the sun rises again. The darkness retreats. But the damage lingers."));
                    try
                    {
                        var scarred = Settlement.All
                            .Where(s => s.IsTown && s.MapFaction?.StringId != AshenKingdomId)
                            .OrderBy(_ => _rng.Next())
                            .Take(4)
                            .ToList();
                        foreach (var t in scarred)
                            RecordScar(t.StringId, 2);
                        if (scarred.Count >= 2)
                            InformationManager.DisplayMessage(new InformationMessage(
                                $"In {scarred[0].Name}, {scarred[1].Name}, and elsewhere — they are still counting the missing. " +
                                "Morning returned, but not everything the dark took.",
                                new Color(0.55f, 0.55f, 0.75f)));
                    }
                    catch { }
                }
            }

            // Tick down protective rites
            if (_protectedDaysRemaining > 0)
            {
                _protectedDaysRemaining--;
                if (_protectedDaysRemaining == 0)
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "The protective rites have faded. The sanctuary's shield is spent."));
            }

            // Deferred Temple join — execute kingdom change during the tick, not inside a UI callback
            if (_pendingTempleJoin)
            {
                _pendingTempleJoin = false;
                try
                {
                    var templeK = Kingdom.All.FirstOrDefault(k =>
                        k.StringId == "the_temple" && !k.IsEliminated);
                    var clan = Hero.MainHero?.Clan;
                    if (templeK != null && clan != null)
                    {
                        if (clan.Kingdom != null && clan.Kingdom != templeK)
                            try { ChangeKingdomAction.ApplyByLeaveKingdom(clan, false); } catch { }
                        if (clan.Kingdom?.StringId != "the_temple")
                            try { ChangeKingdomAction.ApplyByJoinToKingdom(clan, templeK); } catch { }
                    }
                }
                catch { }
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

            // The Temple's covenant / anathema relationship with the player
            try { TempleCovenant.DailyTick(); } catch { }
        }

        /// Called from CampaignBehavior.OnWeeklyTick().
        /// At most one event fires per tick (TryClaimWeeklySlot ensures this).
        /// EventCooldownDays must pass between events to prevent clustering.
        public static void WeeklyTick()
        {
            if (DragonQuestSystem.WorldRekindled) return;

            // Independent of the slot system — runs every week and forces inter-faction
            // conflict if the world has been at peace for too long.
            try { TryEnsureInterFactionConflict(); } catch { }

            // Portents and whisper intel run independently (no slot needed)
            try { TryFirePortents(); } catch { }
            try { TryFireWhisperIntel(); } catch { }

            // ── Cooldown gate ─────────────────────────────────────────────────
            if (ElapsedCampaignDays() - _lastEventElapsedDay < EventCooldownDays) return;
            _weeklySlotFilled = false;
            _warSlotFilled    = false; // war events get their own independent slot

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
            TryFireDeadMarch();
            TryFireTheUndyingHost();

            if (_weeklySlotFilled || _warSlotFilled)
                _lastEventElapsedDay = (int)ElapsedCampaignDays();
        }

        // Runs every week, independent of the event slot. If no inter-faction wars
        // exist, directly seeds one. Gives new games 30 days to settle first.
        private static void TryEnsureInterFactionConflict()
        {
            int day = (int)ElapsedCampaignDays();
            if (day < 30) return;
            if (day - _lastConflictSeedDay < 21) return;

            // Count active non-Ashen inter-faction wars
            int warCount = 0;
            try
            {
                var kingdoms = Kingdom.All.Where(k => !k.IsEliminated && k.StringId != AshenKingdomId).ToList();
                for (int i = 0; i < kingdoms.Count; i++)
                    for (int j = i + 1; j < kingdoms.Count; j++)
                        if (kingdoms[i].IsAtWarWith(kingdoms[j])) warCount++;
            }
            catch { }

            // Only reset the timer if at least one war already exists — if none do,
            // keep trying every week until one seeds successfully.
            if (warCount >= 2) { _lastConflictSeedDay = day; return; }

            bool needWar = warCount == 0 || _rng.NextDouble() < 0.40;
            if (!needWar) return;
            if (!TryPickAtPeacePair(out Kingdom ka, out Kingdom kb)) return;

            _lastConflictSeedDay = day;
            try { DeclareWarAction.ApplyByDefault(ka, kb); } catch { }
            InformationManager.DisplayMessage(new InformationMessage(
                $"Tensions between {ka.Name} and {kb.Name} finally break — war is declared.",
                new Color(0.85f, 0.35f, 0.25f)));
        }

        /// Resets state for a fresh new game (called from OnNewGameCreated).
        public static void ResetForNewGame()
        {
            _longNightDaysRemaining  = 0;
            _brokenWillFired         = 0;
            _templeFounded           = false;
            _pendingTempleJoin       = false;
            _debugForceNextTemple    = false;
            _protectedDaysRemaining  = 0;
            _ashenGambitFired        = false;
            _deadMarchFirstFired     = false;
            _deadMarchLastFiredDay   = 0;
            _undyingHostFired        = false;
            _campaignStartDay        = (int)CampaignTime.Now.ToDays;
            _weeklySlotFilled        = false;
            _warSlotFilled           = false;
            _lastEventElapsedDay     = -EventCooldownDays;
            _lastConflictSeedDay     = 0;
            _battleEchoPending       = false;
            _battleEchoPosX          = 0f;
            _battleEchoPosY          = 0f;
            _brokenKingdomIds.Clear();
            _gotKingdoms.Clear();
            _gotDays.Clear();
            _scarredIds.Clear();
            _scarredDays.Clear();
            _scarredTypes.Clear();
            ResetPortents();
        }

    }
}
