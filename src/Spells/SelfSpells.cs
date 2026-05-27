// =============================================================================
// LIFE & DEATH MAGIC — SelfSpells.cs
// WAVE FORM (L keys): a gridSize×gridSize block of fire that advances forward.
//   gridSize  = 3 + max(0, (formCount - 5) / 5)  — grows +1 per 5 inputs above 5
//   range     = max(3, formCount * 2 - 1) metres  — total travel distance
//   Speed 4 m/s;  tick every 0.5 s;  node spacing 2 m;  hit radius 1.5 m.
//   Non-hero agents in warning zone (hit + 3 m) are nudged sideways.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace ColoursOfCalradia
{
    public static partial class SpellEffects
    {
        // ── Wave state ────────────────────────────────────────────────────────
        private class WaveState
        {
            public Vec3   Position;           // front-centre of the grid
            public Vec3   Forward;
            public Vec3   Right;
            public int    GridSize;
            public const float NodeSpacing  = 2f;
            public const float Speed        = 4f;
            public const float HitRadius    = 1.5f;
            public const float TickInterval = 0.5f;
            public float  TravelLeft;
            public SpellCast Cast;
            public Team   CasterTeam;
            public float  TickTimer = 0f;
            public readonly List<GameEntity> Lights = new List<GameEntity>();
        }

        private static WaveState _wave = null;

        // ── Execute ───────────────────────────────────────────────────────────
        public static void ExecuteWave(SpellCast cast)
        {
            Agent caster = Agent.Main;
            if (caster == null || !caster.IsActive()) return;

            if (_wave != null)
            {
                ClearWave();
                InformationManager.DisplayMessage(new InformationMessage(
                    "Wave dissolved.", new Color(0.7f, 0.7f, 0.7f)));
                return;
            }

            Vec3 fwd   = caster.LookDirection.NormalizedCopy();
            Vec3 right = new Vec3(-fwd.y, fwd.x, 0f);
            right = right.Length < 0.01f ? new Vec3(1f, 0f, 0f) : right.NormalizedCopy();

            int   gridSize = 3 + Math.Max(0, (cast.FormCount - 5) / 5);
            float range    = Math.Max(3f, cast.FormCount * 2f - 1f);

            // Place wave front so that back row clears the caster
            float halfDepth = (gridSize - 1) * WaveState.NodeSpacing * 0.5f;
            Vec3  startPos  = caster.Position + fwd * (halfDepth + 2.5f);

            _wave = new WaveState
            {
                Position   = startPos,
                Forward    = fwd,
                Right      = right,
                GridSize   = gridSize,
                TravelLeft = range,
                Cast       = cast,
                CasterTeam = caster.Team,
            };

            SpawnWaveLights(_wave);

            ColorSchool col = cast.VisualColor;
            TryCastSound(caster.Position, col);
            TryCastAnimation(caster);
            BeginAgentGlow(caster, col, 1.5f);

            InformationManager.DisplayMessage(new InformationMessage(
                $"Wave ({gridSize}×{gridSize}, {range:F0}m) — {cast.EffectSummary()}.",
                ColorSchoolData.GetMessageColor(col)));
        }

        private static void SpawnWaveLights(WaveState w)
        {
            for (int row = 0; row < w.GridSize; row++)
            for (int col = 0; col < w.GridSize; col++)
            {
                Vec3 pos = WaveNodePos(w, row, col);
                w.Lights.Add(SpawnAreaLight(pos, w.Cast.VisualColor, 3f));
            }
        }

        // row 0 = front edge of wave; col 0 = leftmost column (from caster PoV)
        private static Vec3 WaveNodePos(WaveState w, int row, int col)
        {
            float half = (w.GridSize - 1) * 0.5f;
            return w.Position
                 + w.Right   * ((col - half) * WaveState.NodeSpacing)
                 - w.Forward * (row           * WaveState.NodeSpacing);
        }

        // ── Tick (called from MagicSystem every mission frame) ────────────────
        public static void TickWave(float dt)
        {
            if (_wave == null || Mission.Current == null) return;

            float moved      = WaveState.Speed * dt;
            _wave.Position  += _wave.Forward * moved;
            _wave.TravelLeft -= moved;

            // Reposition lights
            int idx = 0;
            for (int row = 0; row < _wave.GridSize; row++)
            for (int col = 0; col < _wave.GridSize; col++)
            {
                if (idx < _wave.Lights.Count && _wave.Lights[idx] != null)
                {
                    Vec3 p = WaveNodePos(_wave, row, col) + new Vec3(0f, 0f, 0.5f);
                    try
                    {
                        var f = new MatrixFrame(Mat3.Identity, p);
                        _wave.Lights[idx].SetGlobalFrame(in f, true);
                    }
                    catch { }
                }
                idx++;
            }

            if (_wave.TravelLeft <= 0f) { ClearWave(); return; }

            _wave.TickTimer -= dt;
            if (_wave.TickTimer > 0f) return;
            _wave.TickTimer = WaveState.TickInterval;

            TickWaveEffects(_wave);
        }

        private static void TickWaveEffects(WaveState w)
        {
            if (Mission.Current == null) return;

            List<Agent> all;
            try { all = Mission.Current.Agents.ToList(); } catch { return; }

            var hit = new HashSet<Agent>();

            for (int row = 0; row < w.GridSize; row++)
            for (int col = 0; col < w.GridSize; col++)
            {
                Vec3 nodePos = WaveNodePos(w, row, col);

                foreach (Agent a in all)
                {
                    if (!a.IsActive() || a.IsMount) continue;

                    float dist = a.Position.Distance(nodePos);

                    // Avoidance: non-hero agents in warning zone sidestep the wave
                    if (!a.IsHero && dist > WaveState.HitRadius && dist < WaveState.HitRadius + 3f)
                        try { NudgeWaveSideStep(w, a); } catch { }

                    // Hit only enemies not yet struck this tick
                    if (w.CasterTeam != null && a.Team == w.CasterTeam) continue;
                    if (dist > WaveState.HitRadius) continue;
                    if (hit.Contains(a)) continue;

                    try
                    {
                        ApplyEffectsToAgent(a, w.Cast, Agent.Main, applyPush: true, applyPull: false);
                        SpawnImpactBurst(a.Position, w.Cast.VisualColor, 0.5f);
                        hit.Add(a);
                    }
                    catch { }
                }
            }
        }

        // Nudge agent toward whichever lateral side of the wave is closer to them
        private static void NudgeWaveSideStep(WaveState w, Agent a)
        {
            bool isMounted = false;
            try { isMounted = a.MountAgent != null; } catch { }
            if (isMounted) return;

            Vec3  toAgent  = a.Position - w.Position;
            float rDot     = Vec3.DotProduct(toAgent, w.Right);
            Vec3  sideDir  = rDot >= 0f ? w.Right : new Vec3(-w.Right.x, -w.Right.y, 0f);
            Vec3  dest     = a.Position + sideDir * 2.5f;
            dest.z = a.Position.z;
            QueueMove(a, dest, 0.35f);
        }

        public static void ClearWave()
        {
            if (_wave == null) return;
            foreach (GameEntity e in _wave.Lights)
                try { e?.Remove(0); } catch { }
            _wave = null;
        }

        // ── Legacy aura stub ──────────────────────────────────────────────────
        // Forwards to Wave so any residual call-sites compile.
        private const string AuraId = "spell_aura";
        public static void ExecuteAura(SpellCast cast) => ExecuteWave(cast);

        // ── Ward state ────────────────────────────────────────────────────────
        // agentIndex → seconds remaining; prevents magic from affecting the warded agent
        private static readonly Dictionary<int, float> _wardedAgents = new Dictionary<int, float>();

        public static bool IsWarded(Agent a)
        {
            if (a == null) return false;
            return _wardedAgents.TryGetValue(a.Index, out float t) && t > 0f;
        }

        // Player sigil ULDR — 10 s immunity, costs 1 day
        public static void ExecuteWard()
        {
            Agent caster = Agent.Main;
            if (caster == null || !caster.IsActive()) return;
            _wardedAgents[caster.Index] = 10f;
            BeginAgentGlow(caster, ColorSchool.White, 10f);
            SpawnCircleLights(caster.Position, ColorSchool.White, 2f, 3f);
            TryCastSound(caster.Position, ColorSchool.White);
            TryCastAnimation(caster);
            InformationManager.DisplayMessage(new InformationMessage(
                "Ward — magic cannot touch you for 10 seconds.",
                ColorSchoolData.GetMessageColor(ColorSchool.White)));
        }

        // NPC ward — same protection without the player HUD message
        public static void ExecuteWardFromAgent(Agent caster)
        {
            if (caster == null || !caster.IsActive()) return;
            _wardedAgents[caster.Index] = 10f;
            BeginAgentGlow(caster, ColorSchool.White, 10f);
            TryCastSound(caster.Position, ColorSchool.White);
            TryCastAnimation(caster);
        }

        public static void TickWard(float dt)
        {
            var keys = _wardedAgents.Keys.ToList();
            foreach (int k in keys)
            {
                float t = _wardedAgents[k] - dt;
                if (t <= 0f) _wardedAgents.Remove(k);
                else _wardedAgents[k] = t;
            }
        }

        public static void ClearWard()
        {
            _wardedAgents.Clear();
        }
    }
}
