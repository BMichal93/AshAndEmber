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
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static class BattleEvents
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
            _active.Clear();
            _initialized = false;
            _ashenTeam   = null;
            _skySet      = false;
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

        // ── Event: Cinder Rain ────────────────────────────────────────────────
        // All non-Ashen agents take PeriodicDamage HP of damage.
        private static void FireCinderRain()
        {
            if (Mission.Current == null) return;
            var victims = new List<Agent>();
            foreach (var agent in Mission.Current.Agents.ToList())
            {
                if (!agent.IsActive() || agent.IsMount) continue;
                if (IsAshenAgent(agent)) continue;
                SpellEffects.DamageAgent(agent, PeriodicDamage);
                victims.Add(agent);
            }
            // Scatter fire particles at victim positions
            for (int i = 0; i < Math.Min(5, victims.Count); i++)
            {
                var a = victims[_rng.Next(victims.Count)];
                Vec3 pos = a.Position + new Vec3((float)(_rng.NextDouble() - 0.5) * 2f,
                                                 (float)(_rng.NextDouble() - 0.5) * 2f, 0f);
                try { SpellEffects.SpawnTempFireParticle(pos, CinderRainInterval * 0.8f); } catch { }
            }
            // Atmospheric: burning-sky fog + wide ground fire field + aerial glow
            ApplyFog(new Vec3(0.90f, 0.28f, 0.04f), 0.004f);
            Vec3 centre = GetFieldCentre();
            SpawnGroundFireField(centre, 35f, 6, ColorSchool.Red, CinderRainInterval * 0.80f);
            SpawnAerialGlow(centre, 30f, 14f, 3, ColorSchool.Orange, CinderRainInterval * 0.80f);
        }

        // ── Event: Ember Tithe ────────────────────────────────────────────────
        // All Ashen agents take PeriodicDamage HP of damage but gain +10 morale
        // (the ritual price they embrace fuels their resolve).
        private static void FireEmberTithe()
        {
            if (Mission.Current == null) return;
            var victims = new List<Agent>();
            foreach (var agent in Mission.Current.Agents.ToList())
            {
                if (!agent.IsActive() || agent.IsMount) continue;
                if (!IsAshenAgent(agent)) continue;
                SpellEffects.DamageAgent(agent, PeriodicDamage);
                // The Ashen embrace the tithe — pain fuels their resolve
                try { agent.SetMorale(Math.Min(100f, agent.GetMorale() + 10f)); } catch { }
                victims.Add(agent);
            }
            for (int i = 0; i < Math.Min(3, victims.Count); i++)
            {
                var a = victims[_rng.Next(victims.Count)];
                Vec3 pos = a.Position + new Vec3((float)(_rng.NextDouble() - 0.5) * 2f,
                                                 (float)(_rng.NextDouble() - 0.5) * 2f, 0f);
                try { SpellEffects.SpawnTempFireParticle(pos, EmberTitheInterval * 0.8f); } catch { }
            }
            // Atmospheric: amber pulse above the Ashen position — they burn and hold
            if (_ashenTeam != null)
            {
                Vec3 ashenCentre = GetTeamCentroid(_ashenTeam);
                SpawnAerialGlow(ashenCentre, 20f, 10f, 3, ColorSchool.Yellow, EmberTitheInterval * 0.80f);
                SpawnGroundFireField(ashenCentre, 12f, 4, ColorSchool.Orange, EmberTitheInterval * 0.80f);
            }
        }

        // ── Event: The Rising ─────────────────────────────────────────────────
        // Spawns RisingSpawnCount tier-1 units on the Ashen side.
        private static void FireTheRising()
        {
            if (Mission.Current == null || _ashenTeam == null) return;
            Vec3 anchor = GetTeamCentroid(_ashenTeam);
            int spawned = SpawnRisingUnits(RisingSpawnCount);
            if (spawned <= 0) return; // troop type missing or no valid anchor — say nothing
            // Fire ring at the spawn point
            for (int i = 0; i < 4; i++)
            {
                double angle = Math.PI * 2.0 / 4 * i;
                Vec3 pos = anchor + new Vec3((float)Math.Cos(angle) * 3f,
                                             (float)Math.Sin(angle) * 3f, 0f);
                try { SpellEffects.SpawnTempFireParticle(pos, TheRisingInterval * 0.7f); } catch { }
            }
            // Atmospheric: eruption burst at the spawn point + dim ghostly lights above
            SpawnGroundFireField(anchor, 12f, 5, ColorSchool.Purple, TheRisingInterval * 0.75f);
            SpawnAerialGlow(anchor, 16f, 10f, 3, ColorSchool.Ashen, TheRisingInterval * 0.75f);
            MBInformationManager.AddQuickInformation(new TextObject(
                $"The Rising — {spawned} more pour from the grey."));
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
            // Three ominous fires spread across the field centre
            Vec3 centre = GetFieldCentre();
            for (int i = 0; i < 3; i++)
            {
                Vec3 pos = centre + new Vec3((float)(_rng.NextDouble() - 0.5) * 20f,
                                             (float)(_rng.NextDouble() - 0.5) * 20f, 0f);
                try { SpellEffects.SpawnTempFireParticle(pos, 30f); } catch { }
            }
            // Atmospheric: deep-dusk sky, cold dark fog, wide field of grey flames
            TintSky(22f); // deep dusk / near-night
            ApplyFog(new Vec3(0.18f, 0.18f, 0.26f), 0.006f); // cold dark fog
            SpawnGroundFireField(centre, 40f, 10, ColorSchool.Ashen, 30f);
            SpawnAerialGlow(centre, 35f, 16f, 5, ColorSchool.Ashen, 30f);
            if (count > 0)
                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Dread — something cold passes through {count} fighters. Courage breaks."));
        }

        // ── Event: Last Light ─────────────────────────────────────────────────
        // Sets scene time-of-day to midnight, drains morale from non-Ashen
        // agents and boosts Ashen agents. Fires once.
        // NOTE: Scene.TimeOfDay setter API varies by Bannerlord version;
        //       wrapped in try/catch so a missing API fails silently.
        private static void FireLastLight()
        {
            // Last Light always overrides the sky — it's the defining one-shot event.
            _skySet = true;
            try { Mission.Current?.Scene.TimeOfDay = 23f; } catch { }

            int blinded = 0;
            int empowered = 0;
            if (Mission.Current != null)
            {
                foreach (var agent in Mission.Current.Agents.ToList())
                {
                    if (!agent.IsActive() || agent.IsMount) continue;
                    try
                    {
                        if (IsAshenAgent(agent))
                        {
                            agent.SetMorale(Math.Min(100f, agent.GetMorale() + 15f));
                            empowered++;
                        }
                        else
                        {
                            agent.SetMorale(Math.Max(0f, agent.GetMorale() - 20f));
                            blinded++;
                        }
                    }
                    catch { }
                }
            }

            // A cluster of fires lights up the sudden darkness
            Vec3 centre = GetFieldCentre();
            for (int i = 0; i < 6; i++)
            {
                Vec3 pos = centre + new Vec3((float)(_rng.NextDouble() - 0.5) * 30f,
                                             (float)(_rng.NextDouble() - 0.5) * 30f, 0f);
                try { SpellEffects.SpawnTempFireParticle(pos, 60f); } catch { }
            }
            // Atmospheric: fire-lit midnight fog, wide ground fire, burning-sky aerial glow
            ApplyFog(new Vec3(0.80f, 0.22f, 0.05f), 0.005f); // fire-lit night
            SpawnGroundFireField(centre, 40f, 10, ColorSchool.Orange, 60f);
            SpawnAerialGlow(centre, 40f, 18f, 6, ColorSchool.Red, 60f);
            MBInformationManager.AddQuickInformation(new TextObject(
                $"Last Light — the sun dies. Darkness swallows the field." +
                (blinded > 0   ? $" {blinded} fighters lose their footing in the dark." : "") +
                (empowered > 0 ? $" The Ashen rise." : "")));
        }

        // ── Event: Ashen Ground ───────────────────────────────────────────────
        // All mounted agents (both sides) are dismounted via SpellEffects.ForceDismount.
        private static void FireAshenGround()
        {
            if (Mission.Current == null) return;
            int count = 0;
            var dismounted = new List<Vec3>();
            foreach (var agent in Mission.Current.Agents.ToList())
            {
                if (!agent.IsActive() || !agent.HasMount) continue;
                dismounted.Add(agent.Position);
                SpellEffects.ForceDismount(agent);
                count++;
            }
            // Fire at the positions where mounts fell
            foreach (var pos in dismounted.Take(4))
                try { SpellEffects.SpawnTempFireParticle(pos, AshenGroundInterval * 0.9f); } catch { }
            // Atmospheric: ash fog + grey ground effect across the field
            ApplyFog(new Vec3(0.48f, 0.47f, 0.50f), 0.005f); // grey ash fog
            Vec3 centre = GetFieldCentre();
            SpawnGroundFireField(centre, 30f, 5, ColorSchool.Ashen, AshenGroundInterval * 0.80f);
            if (count > 0)
                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Ashen Ground — {count} mount{(count != 1 ? "s" : "")} fall. No one rides today."));
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
            // Scattered fires mark the lines breaking into chaos
            Vec3 centre = GetFieldCentre();
            for (int i = 0; i < 5; i++)
            {
                Vec3 pos = centre + new Vec3((float)(_rng.NextDouble() - 0.5) * 25f,
                                             (float)(_rng.NextDouble() - 0.5) * 25f, 0f);
                try { SpellEffects.SpawnTempFireParticle(pos, FrenzyInterval * 0.9f); } catch { }
            }
            // Atmospheric: red chaos fog, wide ground fire, aerial crimson glow
            ApplyFog(new Vec3(0.85f, 0.15f, 0.05f), 0.003f); // blood-red fog
            SpawnGroundFireField(centre, 38f, 7, ColorSchool.Red, FrenzyInterval * 0.80f);
            SpawnAerialGlow(centre, 32f, 12f, 4, ColorSchool.Orange, FrenzyInterval * 0.80f);
            MBInformationManager.AddQuickInformation(new TextObject(
                "Frenzy — no one can hold the line. All charge."));
        }

        // ── Spawn helper ──────────────────────────────────────────────────────
        // Spawns `count` Ashen Spawn agents (ashen_thrall, falling back to
        // vanilla bandit troops) near the centroid of the Ashen team. Returns
        // the number actually spawned so the caller can stay silent when
        // nothing appeared.
        private static int SpawnRisingUnits(int count)
        {
            int spawned = 0;
            try
            {
                CharacterObject troop =
                    MBObjectManager.Instance.GetObject<CharacterObject>("ashen_thrall")
                 ?? MBObjectManager.Instance.GetObject<CharacterObject>("sea_raider")
                 ?? MBObjectManager.Instance.GetObject<CharacterObject>("mountain_bandit")
                 ?? MBObjectManager.Instance.GetObject<CharacterObject>("looter");
                if (troop == null) return 0;

                Vec3 anchor = GetTeamCentroid(_ashenTeam);
                if (anchor.x == 0f && anchor.y == 0f) return 0; // no valid anchor

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

                        var origin    = new BasicBattleAgentOrigin(troop);
                        var agentData = new AgentBuildData(origin)
                            .Team(_ashenTeam)
                            .Controller(AgentControllerType.AI)
                            .ClothingColor1(AshenVisuals.ClothAshGrey)
                            .ClothingColor2(AshenVisuals.ClothColdBlue)
                            .InitialPosition(in pos)
                            .InitialDirection(in dir);

                        var agent = Mission.Current.SpawnAgent(agentData, false);
                        // Fallback bandit troops carry no Ashen marker, so the
                        // OnAgentBuild hook won't catch them — force the look.
                        try { AshenVisuals.ForceApply(agent); } catch { }
                        spawned++;
                    }
                    catch { }
                }
            }
            catch { }
            return spawned;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        // Returns true if the agent belongs to the Ashen side.
        // Uses team membership when _ashenTeam is known; falls back to checking
        // the hero's Ashen status directly for hero agents only.
        private static bool IsAshenAgent(Agent agent)
        {
            if (_ashenTeam != null) return agent.Team == _ashenTeam;
            if (!agent.IsHero) return false;
            var hero = (agent.Character as CharacterObject)?.HeroObject;
            if (hero == null) return false;
            return hero == Hero.MainHero
                ? MageKnowledge.IsAshen
                : ColourLordRegistry.IsAshenLord(hero);
        }

        private static bool Roll(float chance) => _rng.NextDouble() < chance;

        // Average position of all active non-mount agents across the whole field.
        private static Vec3 GetFieldCentre()
        {
            if (Mission.Current == null) return Vec3.Zero;
            float x = 0f, y = 0f, z = 0f;
            int   n = 0;
            try
            {
                foreach (var a in Mission.Current.Agents)
                {
                    if (!a.IsActive() || a.IsMount) continue;
                    x += a.Position.x; y += a.Position.y; z += a.Position.z;
                    n++;
                }
            }
            catch { }
            return n == 0 ? Vec3.Zero : new Vec3(x / n, y / n, z / n);
        }

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

        // ── Visual atmosphere helpers ─────────────────────────────────────────

        // Sets scene time-of-day for a dramatic sky (only fires once per battle
        // so periodic events can't fight over it with one-shot events).
        private static void TintSky(float timeOfDay)
        {
            if (_skySet) return;
            _skySet = true;
            try { Mission.Current?.Scene.TimeOfDay = timeOfDay; } catch { }
        }

        // Applies scene fog via reflection (same pattern as AshenSceneTone).
        // rgb = light colour in linear space; falloff = fog density coefficient.
        private static void ApplyFog(Vec3 rgb, float falloff = 0.004f, float density = 1.0f)
        {
            try
            {
                var scene = Mission.Current?.Scene;
                if (scene == null) return;
                if (!_fogResolved)
                {
                    _fogResolved  = true;
                    _setFogMethod = scene.GetType().GetMethod("SetFog",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }
                if (_setFogMethod == null) return;
                if (_setFogMethod.GetParameters().Length == 3)
                    _setFogMethod.Invoke(scene, new object[] { falloff, rgb, density });
            }
            catch { }
        }

        // Scatters fire particles and coloured point lights across the field.
        // Safe to call every event tick — particle/light duration < interval prevents stacking.
        private static void SpawnGroundFireField(Vec3 centre, float radius, int count,
            ColorSchool school, float duration)
        {
            for (int i = 0; i < count; i++)
            {
                try
                {
                    double angle = _rng.NextDouble() * Math.PI * 2;
                    float  dist  = (float)(_rng.NextDouble() * radius);
                    Vec3   pos   = centre + new Vec3((float)Math.Cos(angle) * dist,
                                                     (float)Math.Sin(angle) * dist, 0f);
                    if (school != ColorSchool.Ashen)
                        try { SpellEffects.SpawnTempFireParticle(pos, duration); } catch { }
                    SpellEffects.SpawnTempLight(pos, school, 10f, duration * 0.7f);
                }
                catch { }
            }
        }

        // Places large-radius glow lights high above the field to simulate a
        // burning or darkened sky reflected onto the scene.
        private static void SpawnAerialGlow(Vec3 centre, float spread, float height,
            int count, ColorSchool school, float duration)
        {
            for (int i = 0; i < count; i++)
            {
                try
                {
                    double angle = _rng.NextDouble() * Math.PI * 2;
                    float  dist  = (float)(_rng.NextDouble() * spread);
                    float  h     = height + (float)(_rng.NextDouble() * 10f);
                    Vec3   pos   = new Vec3(centre.x + (float)Math.Cos(angle) * dist,
                                            centre.y + (float)Math.Sin(angle) * dist,
                                            centre.z + h);
                    SpellEffects.SpawnTempLight(pos, school, 28f, duration);
                }
                catch { }
            }
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
