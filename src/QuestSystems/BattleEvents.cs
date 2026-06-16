// =============================================================================
// ASH AND EMBER — BattleEvents.cs
// Randomised battlefield conditions that activate at mission start.
// Each event rolls independently; active ones run until the battle ends.
//
// ┌──────────────┬─────┬────────────────────────────────────────────────────────┐
// │ Event        │     │ Effect                                                 │
// ├──────────────┼─────┼────────────────────────────────────────────────────────┤
// │ Cinder Rain  │ 20s │ Every non-Ashen agent takes 15 damage                 │
// │ Ember Tithe  │ 20s │ Every Ashen agent takes 15 damage (+10 morale)        │
// │ The Rising   │ 20s │ Tier-1 units spawn on the Ashen side (Ashen battle    │
// │              │     │ only — skipped if no Ashen side detected)             │
// │ Dread        │ ×1  │ All non-Ashen agents lose 30 morale (fires once)      │
// │ Last Light   │ ×1  │ Scene time jumps to midnight (fires once)             │
// │ Ashen Ground │ 20s │ All mounted agents are dismounted                     │
// │ Frenzy       │ 20s │ Charge order issued to every formation on both sides  │
// └──────────────┴─────┴────────────────────────────────────────────────────────┘
//
// DEBUGGING GUIDE:
//   • Active events are printed to the message log at battle start.
//     No events = no message (quiet battle).
//   • Add breakpoints in BuildActiveEvents() to trace rolls.
//   • _ashenTeam is set in FindAshenTeam() — null if no Ashen hero found in the
//     mission agents list. The Rising will silently skip if null.
//   • SpawnRisingUnits() wraps Mission.SpawnAgent inside try/catch; if the
//     troop CharacterObject is missing the spawn silently no-ops.
//   • Scene.TimeOfDay setter is wrapped in try/catch (API varies by version).
//   • All tuning constants (intervals, chances, damage) are public at the top.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static partial class BattleEvents
    {
        // ── Tuning constants ──────────────────────────────────────────────────
        // Per-battle activation probability for each event.
        // Total expected events per battle: ~0.25 → most battles are clean,
        // roughly 1-in-4 has one event, 1-in-30 has two.
        public const float ChanceCinderRain    = 0.05f;
        public const float ChanceEmberTithe    = 0.04f;
        public const float ChanceTheRising     = 0.06f; // only if Ashen side present
        public const float ChanceDread         = 0.04f;
        public const float ChanceLastLight     = 0.03f;
        public const float ChanceAshenGround   = 0.04f;
        public const float ChanceFrenzy        = 0.04f;

        public const float CinderRainInterval  = 20f;   // seconds between damage ticks
        public const float EmberTitheInterval  = 20f;
        public const float TheRisingInterval   = 20f;
        public const float AshenGroundInterval = 20f;
        public const float FrenzyInterval      = 20f;

        // One-shot events fire this many seconds after battle start
        public const float OneShotDelay        = 5f;

        public const float PeriodicDamage      = 15f;   // HP per Cinder Rain / Ember Tithe tick
        public const float DreadMoralePenalty  = 30f;   // morale removed by Dread
        public const int   RisingSpawnCount    = 6;     // units per Rising tick

        // ── Per-battle state ──────────────────────────────────────────────────
        private static bool                  _initialized = false;
        private static Team                  _ashenTeam   = null;
        private static readonly List<RunningEvent> _active = new List<RunningEvent>();
        private static readonly Random       _rng         = new Random();

        // ── Visual atmosphere state ────────────────────────────────────────────
        // _skySet prevents periodic events from overriding a dramatic one-shot sky tint.
        private static bool       _skySet      = false;
        private static MethodInfo _setFogMethod = null;
        private static bool       _fogResolved  = false;

        // Represents one active event running in a battle
        private class RunningEvent
        {
            public string Name;
            public float  Interval; // seconds between fires; 0 = fires exactly once
            public float  Timer;    // time until next fire
            public bool   Done;     // set true after a one-shot event fires
            public Action OnFire;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// Called every frame from MagicMissionBehavior.OnMissionTick(dt).
        public static void MissionTick(float dt)
        {
            if (!_initialized) Initialize();

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var evt = _active[i];
                if (evt.Done) { _active.RemoveAt(i); continue; }

                evt.Timer -= dt;
                if (evt.Timer > 0f) continue;

                try { evt.OnFire(); } catch { }

                if (evt.Interval <= 0f)
                    evt.Done = true;        // one-shot: remove after firing
                else
                    evt.Timer = evt.Interval; // repeating: reset countdown
            }
        }

        /// Called from MagicMissionBehavior.OnEndMission().
        public static void OnMissionEnd()
        {
            // Capture cast count before clearing — spell-heavy battles leave an echo
            int castCount = AgingSystem.MissionCastCount;
            AgingSystem.ClearMissionCasts();

            _active.Clear();
            _initialized = false;
            _ashenTeam   = null;
            _skySet      = false;

            // Battlefield echo: 3+ casts marks the ground — Ashen are drawn to it
            if (castCount >= 3 && MageKnowledge.IsMage)
            {
                try
                {
                    Vec2 pos = MobileParty.MainParty?.GetPosition2D ?? default(Vec2);
                    if (pos.x != 0f || pos.y != 0f)
                        CampaignMapEvents.SetBattleEcho(pos.x, pos.y);
                }
                catch { }
            }
        }

        // ── Initialization ────────────────────────────────────────────────────

        private static void Initialize()
        {
            _initialized = true;
            // Only activate in real combat — skip settlement scenes (tavern, keep, etc.)
            var m = Mission.Current;
            if (m == null || (!m.IsFieldBattle && !m.IsSiegeBattle && !m.IsSallyOutBattle && !m.IsNavalBattle))
                return;
            FindAshenTeam();
            BuildActiveEvents();
        }

        // Scan hero agents to find which team (if any) represents the Ashen side.
        // Player Ashen status comes from MageKnowledge.IsAshen;
        // NPC Ashen status comes from ColourLordRegistry.IsAshenLord.
        private static void FindAshenTeam()
        {
            _ashenTeam = null;
            if (Mission.Current == null) return;
            try
            {
                foreach (var agent in Mission.Current.Agents)
                {
                    if (!agent.IsHero) continue;
                    var hero = (agent.Character as CharacterObject)?.HeroObject;
                    if (hero == null) continue;
                    bool isAshen = hero == Hero.MainHero
                        ? MageKnowledge.IsAshen
                        : ColourLordRegistry.IsAshenLord(hero);
                    if (!isAshen) continue;
                    _ashenTeam = agent.Team;
                    break;
                }
            }
            catch { }
        }

        // Roll each event independently and register the active ones.
        // Announces active events in the message log.
        private static void BuildActiveEvents()
        {
            _active.Clear();
            var names = new List<string>();

            // ── Cinder Rain ───────────────────────────────────────────────────
            if (Roll(ChanceCinderRain))
            {
                names.Add("Cinder Rain");
                Add("Cinder Rain", CinderRainInterval, FireCinderRain);
            }

            // ── Ember Tithe ───────────────────────────────────────────────────
            if (Roll(ChanceEmberTithe))
            {
                names.Add("Ember Tithe");
                Add("Ember Tithe", EmberTitheInterval, FireEmberTithe);
            }

            // ── The Rising (Ashen side only) ──────────────────────────────────
            if (_ashenTeam != null && Roll(ChanceTheRising))
            {
                names.Add("The Rising");
                Add("The Rising", TheRisingInterval, FireTheRising);
            }

            // ── Dread (one-shot) ──────────────────────────────────────────────
            if (Roll(ChanceDread))
            {
                names.Add("Dread");
                AddOneShot("Dread", OneShotDelay, FireDread);
            }

            // ── Last Light (one-shot) ─────────────────────────────────────────
            if (Roll(ChanceLastLight))
            {
                names.Add("Last Light");
                AddOneShot("Last Light", OneShotDelay + 1f, FireLastLight);
            }

            // ── Ashen Ground ──────────────────────────────────────────────────
            if (Roll(ChanceAshenGround))
            {
                names.Add("Ashen Ground");
                Add("Ashen Ground", AshenGroundInterval, FireAshenGround);
            }

            // ── Frenzy ────────────────────────────────────────────────────────
            if (Roll(ChanceFrenzy))
            {
                names.Add("Frenzy");
                Add("Frenzy", FrenzyInterval, FireFrenzy);
            }

            if (names.Count > 0)
                try { MBInformationManager.AddQuickInformation(new TextObject(
                    "The field is cursed — " + string.Join(", ", names) + ".")); } catch { }
        }

    }
}
