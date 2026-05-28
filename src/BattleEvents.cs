// =============================================================================
// ASH AND EMBER — BattleEvents.cs
// Randomised battlefield conditions that activate at mission start.
// Each event rolls independently; active ones run until the battle ends.
//
// ┌──────────────┬─────┬────────────────────────────────────────────────────────┐
// │ Event        │     │ Effect                                                 │
// ├──────────────┼─────┼────────────────────────────────────────────────────────┤
// │ Cinder Rain  │ 20s │ Every non-Ashen agent takes 5 damage                  │
// │ Ember Tithe  │ 20s │ Every Ashen agent takes 5 damage                      │
// │ The Rising   │ 30s │ Tier-1 units spawn on the Ashen side (Ashen battle    │
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
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static class BattleEvents
    {
        // ── Tuning constants ──────────────────────────────────────────────────
        // Per-battle activation probability for each event
        public const float ChanceCinderRain    = 0.30f;
        public const float ChanceEmberTithe    = 0.20f;
        public const float ChanceTheRising     = 0.35f; // only if Ashen side present
        public const float ChanceDread         = 0.25f;
        public const float ChanceLastLight     = 0.15f;
        public const float ChanceAshenGround   = 0.20f;
        public const float ChanceFrenzy        = 0.20f;

        public const float CinderRainInterval  = 20f;   // seconds between damage ticks
        public const float EmberTitheInterval  = 20f;
        public const float TheRisingInterval   = 30f;
        public const float AshenGroundInterval = 20f;
        public const float FrenzyInterval      = 20f;

        // One-shot events fire this many seconds after battle start
        public const float OneShotDelay        = 5f;

        public const float PeriodicDamage      = 5f;    // HP per Cinder Rain / Ember Tithe tick
        public const float DreadMoralePenalty  = 30f;   // morale removed by Dread
        public const int   RisingSpawnCount    = 4;     // units per Rising tick

        // ── Per-battle state ──────────────────────────────────────────────────
        private static bool                  _initialized = false;
        private static Team                  _ashenTeam   = null;
        private static readonly List<RunningEvent> _active = new List<RunningEvent>();
        private static readonly Random       _rng         = new Random();

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
            _active.Clear();
            _initialized = false;
            _ashenTeam   = null;
        }

        // ── Initialization ────────────────────────────────────────────────────

        private static void Initialize()
        {
            _initialized = true;
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
                    var hero = agent.Character?.HeroObject;
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
                InformationManager.DisplayMessage(new InformationMessage(
                    "The field is cursed — " + string.Join(", ", names) + ".",
                    new Color(0.7f, 0.3f, 0.2f)));
        }

        // ── Event: Cinder Rain ────────────────────────────────────────────────
        // All non-Ashen agents take PeriodicDamage HP of damage.
        private static void FireCinderRain()
        {
            if (Mission.Current == null) return;
            foreach (var agent in Mission.Current.Agents.ToList())
            {
                if (!agent.IsActive() || agent.IsMount) continue;
                if (IsAshenAgent(agent)) continue;
                SpellEffects.DamageAgent(agent, PeriodicDamage);
            }
        }

        // ── Event: Ember Tithe ────────────────────────────────────────────────
        // All Ashen agents take PeriodicDamage HP of damage.
        private static void FireEmberTithe()
        {
            if (Mission.Current == null) return;
            foreach (var agent in Mission.Current.Agents.ToList())
            {
                if (!agent.IsActive() || agent.IsMount) continue;
                if (!IsAshenAgent(agent)) continue;
                SpellEffects.DamageAgent(agent, PeriodicDamage);
            }
        }

        // ── Event: The Rising ─────────────────────────────────────────────────
        // Spawns RisingSpawnCount tier-1 units on the Ashen side.
        private static void FireTheRising()
        {
            if (Mission.Current == null || _ashenTeam == null) return;
            SpawnRisingUnits(RisingSpawnCount);
            InformationManager.DisplayMessage(new InformationMessage(
                $"The Rising — {RisingSpawnCount} more pour from the grey.",
                new Color(0.5f, 0.3f, 0.5f)));
        }

        // ── Event: Dread ──────────────────────────────────────────────────────
        // All non-Ashen agents lose DreadMoralePenalty morale. Fires once.
        private static void FireDread()
        {
            if (Mission.Current == null) return;
            int count = 0;
            foreach (var agent in Mission.Current.Agents.ToList())
            {
                if (!agent.IsActive() || agent.IsMount) continue;
                if (IsAshenAgent(agent)) continue;
                try { agent.SetMorale(Math.Max(0f, agent.GetMorale() - DreadMoralePenalty)); }
                catch { }
                count++;
            }
            if (count > 0)
                InformationManager.DisplayMessage(new InformationMessage(
                    $"Dread — something cold passes through {count} fighters. Courage breaks.",
                    new Color(0.4f, 0.3f, 0.6f)));
        }

        // ── Event: Last Light ─────────────────────────────────────────────────
        // Sets scene time-of-day to midnight. Fires once.
        // NOTE: Scene.TimeOfDay setter API varies by Bannerlord version;
        //       wrapped in try/catch so a missing API fails silently.
        private static void FireLastLight()
        {
            try { Mission.Current.Scene.TimeOfDay = 23f; } catch { }
            InformationManager.DisplayMessage(new InformationMessage(
                "Last Light — the sun dies. Darkness falls over the field.",
                new Color(0.2f, 0.2f, 0.45f)));
        }

        // ── Event: Ashen Ground ───────────────────────────────────────────────
        // All mounted agents (both sides) are dismounted via SpellEffects.ForceDismount.
        private static void FireAshenGround()
        {
            if (Mission.Current == null) return;
            int count = 0;
            foreach (var agent in Mission.Current.Agents.ToList())
            {
                if (!agent.IsActive() || !agent.HasMount) continue;
                SpellEffects.ForceDismount(agent);
                count++;
            }
            if (count > 0)
                InformationManager.DisplayMessage(new InformationMessage(
                    $"Ashen Ground — {count} mount{(count != 1 ? "s" : "")} fall. No one rides today.",
                    new Color(0.55f, 0.4f, 0.2f)));
        }

        // ── Event: Frenzy ─────────────────────────────────────────────────────
        // Issues a charge order to every non-empty formation on both sides.
        private static void FireFrenzy()
        {
            if (Mission.Current == null) return;
            foreach (var team in Mission.Current.Teams.ToList())
            {
                if (team == null) continue;
                try
                {
                    foreach (var formation in team.FormationsIncludingEmpty.ToList())
                    {
                        if (formation == null || formation.CountOfUnits == 0) continue;
                        try { formation.SetMovementOrder(MovementOrder.MovementOrderCharge); }
                        catch { }
                    }
                }
                catch { }
            }
            InformationManager.DisplayMessage(new InformationMessage(
                "Frenzy — no one can hold the line. All charge.",
                new Color(0.8f, 0.3f, 0.1f)));
        }

        // ── Spawn helper ──────────────────────────────────────────────────────
        // Spawns `count` sea_raider (Ashen Spawn troop type) agents near the
        // centroid of the Ashen team. Silently no-ops if troop not found.
        private static void SpawnRisingUnits(int count)
        {
            try
            {
                CharacterObject troop =
                    MBObjectManager.Instance.GetObject<CharacterObject>("sea_raider")
                 ?? MBObjectManager.Instance.GetObject<CharacterObject>("mountain_bandit")
                 ?? MBObjectManager.Instance.GetObject<CharacterObject>("looter");
                if (troop == null) return;

                Vec3 anchor = GetTeamCentroid(_ashenTeam);
                if (anchor.x == 0f && anchor.y == 0f) return; // no valid anchor

                Vec2 dir = Vec2.Forward;

                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        Vec3 pos = anchor + new Vec3(
                            (float)(_rng.NextDouble() - 0.5) * 6f,
                            (float)(_rng.NextDouble() - 0.5) * 6f,
                            0f
                        );
                        // Snap to ground surface
                        float gz = pos.z;
                        try
                        {
                            Mission.Current.Scene.GetHeightAtPoint(
                                pos.AsVec2,
                                BodyFlags.CommonCollisionExcludeFlagsForAgent,
                                ref gz);
                            pos.z = gz;
                        }
                        catch { }

                        var origin    = new SimpleAgentOrigin(troop);
                        var agentData = new AgentBuildData(origin)
                            .Team(_ashenTeam)
                            .Controller(Agent.ControllerType.AI)
                            .InitialPosition(ref pos)
                            .InitialDirection(ref dir);

                        Mission.Current.SpawnAgent(agentData, false);
                    }
                    catch { }
                }
            }
            catch { }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        // Returns true if the agent belongs to the Ashen side.
        // Uses team membership when _ashenTeam is known; falls back to checking
        // the hero's Ashen status directly for hero agents only.
        private static bool IsAshenAgent(Agent agent)
        {
            if (_ashenTeam != null) return agent.Team == _ashenTeam;
            if (!agent.IsHero) return false;
            var hero = agent.Character?.HeroObject;
            if (hero == null) return false;
            return hero == Hero.MainHero
                ? MageKnowledge.IsAshen
                : ColourLordRegistry.IsAshenLord(hero);
        }

        private static bool Roll(float chance) => _rng.NextDouble() < chance;

        // Average position of all active non-mount agents on a team.
        private static Vec3 GetTeamCentroid(Team team)
        {
            if (team == null || Mission.Current == null) return Vec3.Zero;
            float x = 0f, y = 0f, z = 0f;
            int   n = 0;
            try
            {
                foreach (var a in Mission.Current.Agents)
                {
                    if (!a.IsActive() || a.IsMount || a.Team != team) continue;
                    x += a.Position.x; y += a.Position.y; z += a.Position.z;
                    n++;
                }
            }
            catch { }
            return n == 0 ? Vec3.Zero : new Vec3(x / n, y / n, z / n);
        }

        // ── Registration helpers ──────────────────────────────────────────────

        private static void Add(string name, float interval, Action onFire)
        {
            _active.Add(new RunningEvent
            {
                Name     = name,
                Interval = interval,
                Timer    = interval,
                OnFire   = onFire,
            });
        }

        private static void AddOneShot(string name, float delay, Action onFire)
        {
            _active.Add(new RunningEvent
            {
                Name     = name,
                Interval = 0f,
                Timer    = delay,
                OnFire   = onFire,
            });
        }
    }
}
