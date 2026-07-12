// =============================================================================
// ASH AND EMBER — Magic/ElementWallWards.cs
//
// Runtime layer for elemental wall WARDING (the pure rules live in
// WallWardMath). Every standing wall registers its nodes here; the registry
// then answers three questions during a battle:
//
//   • BlocksPath  — does a wall stand between a caster and his mark whose
//     element stops the incoming working? (Physics is impartial: a mist wall
//     drinks ANY fire that crosses it, the caster's own included.)
//   • Missile sweep (Tick) — arrows and bolts that fly into a wall of wind or
//     stone are stopped there, whoever loosed them.
//   • CounterWallFor / LastHostileElement — what the enemy has been throwing,
//     so an NPC mage can raise the wall that answers it.
//
// State is mission-scoped: MagicMissionBehavior ticks it and OnGameStart /
// mission-end clears it alongside the other battle state.
// =============================================================================

using System;
using System.Collections.Generic;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static class ElementWallWards
    {
        private class WallNode
        {
            public MagicElement El;
            public Vec3  Pos;
            public float Radius;
            public float Remaining;   // seconds
            public Team  Team;        // who raised it (for logs only — warding is impartial)
            public string SourceKey;  // ties nodes to their wall so replacement clears them
        }

        private class RecentCast
        {
            public MagicElement El;
            public Team  Team;
            public float Age;         // seconds since loosed
        }

        private static readonly List<WallNode>   _nodes   = new List<WallNode>();
        private static readonly List<RecentCast> _recent  = new List<RecentCast>();
        private const float RecentCastMemorySec = 30f;
        private const int   MaxNodes            = 256;   // hard cap — safety against runaway registration
        private static float _blockMsgCooldown;          // throttle the player-facing block log
        private static string _lastBlockLine;            // a NEW reason always shows through the throttle
        // Arrows that already beat a wind wall's stop roll — each shaft is rolled
        // ONCE at the gust's edge, then remembered so the per-tick sweep cannot
        // re-roll it into the ground a frame later.
        private static readonly HashSet<int> _windPassedMissiles = new HashSet<int>();
        private static readonly Random _rng = new Random();

        public static void Clear()
        {
            _nodes.Clear();
            _recent.Clear();
            _windPassedMissiles.Clear();
            _blockMsgCooldown = 0f;
            _lastBlockLine = null;
        }

        // ── Registration ─────────────────────────────────────────────────────────
        public static void RegisterNode(MagicElement el, Vec3 pos, float radius,
                                        float durationSec, Team team, string sourceKey = null)
        {
            if (el == MagicElement.Spirit) return;            // wards, never walls
            if (durationSec <= 0f || radius <= 0f) return;
            if (_nodes.Count >= MaxNodes) return;
            _nodes.Add(new WallNode { El = el, Pos = pos, Radius = radius,
                                      Remaining = durationSec, Team = team,
                                      SourceKey = sourceKey });
        }

        // A recast nature barrier REPLACES the standing one (mirrors
        // RemoveAreaEffect) — its warding must fall with it, or an invisible
        // ward would linger where no wall stands.
        public static void ClearBySourceKey(string sourceKey)
        {
            if (string.IsNullOrEmpty(sourceKey)) return;
            for (int i = _nodes.Count - 1; i >= 0; i--)
                if (_nodes[i].SourceKey == sourceKey) _nodes.RemoveAt(i);
        }

        // A caster loosed an element — remembered briefly so enemy mages can
        // raise the wall that answers it.
        public static void NoteCast(MagicElement el, Team team)
        {
            if (team == null) return;
            _recent.Add(new RecentCast { El = el, Team = team, Age = 0f });
            if (_recent.Count > 16) _recent.RemoveAt(0);
        }

        // The element most recently loosed by an enemy of `myTeam` (or null).
        public static MagicElement? LastHostileElement(Team myTeam)
        {
            if (myTeam == null) return null;
            for (int i = _recent.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (_recent[i].Team != null && _recent[i].Team.IsEnemyOf(myTeam))
                        return _recent[i].El;
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            return null;
        }

        // ── Line-of-effect warding ───────────────────────────────────────────────
        // True when a standing wall that stops `incoming` crosses the line from
        // `from` to `to`; `at` is where the working visibly dies (with feedback).
        public static bool BlocksPath(MagicElement incoming, Vec3 from, Vec3 to, out Vec3 at)
        {
            at = to;
            var n = FindBlockingNode(incoming, from, to);
            if (n == null) return false;
            float t = WallWardMath.SegmentNearestT(from.x, from.y, to.x, to.y,
                                                   n.Pos.x, n.Pos.y);
            at = from + (to - from) * t;
            at.z = n.Pos.z;
            ShowBlockFizzle(n.El, incoming, at);
            return true;
        }

        // Silent probe — the same test with no fizzle or log. Used by the NPC AI
        // to avoid throwing an element into a wall that would drink it.
        public static bool IsPathWarded(MagicElement incoming, Vec3 from, Vec3 to)
            => FindBlockingNode(incoming, from, to) != null;

        // Crystal energy is flung shard-force — physical, however it glows — so
        // the walls that stop missiles (wind, stone) stop a crystal's reach too.
        // Silent per-target test for the crystal pulse loops.
        public static bool BlocksCrystal(Vec3 from, Vec3 to)
        {
            for (int i = 0; i < _nodes.Count; i++)
            {
                var n = _nodes[i];
                if (!WallWardMath.WallBlocksMissiles(n.El)) continue;
                if (WallWardMath.SegmentNearPoint(from.x, from.y, to.x, to.y,
                                                  n.Pos.x, n.Pos.y, n.Radius))
                    return true;
            }
            return false;
        }

        // Water puts out fire: a wave or standing mist crossing a burning line
        // removes the fire's WARD nodes there (the burning ground itself is
        // quenched by AreaEffects). Returns how many nodes died to steam.
        public static int QuenchFireNodesNear(Vec3 pos, float radius)
        {
            int quenched = 0;
            float r2 = radius * radius;
            for (int i = _nodes.Count - 1; i >= 0; i--)
            {
                var n = _nodes[i];
                if (n.El != MagicElement.Fire) continue;
                float dx = n.Pos.x - pos.x, dy = n.Pos.y - pos.y;
                if (dx * dx + dy * dy > r2) continue;
                _nodes.RemoveAt(i);
                quenched++;
            }
            return quenched;
        }

        private static WallNode FindBlockingNode(MagicElement incoming, Vec3 from, Vec3 to)
        {
            if (incoming == MagicElement.Spirit) return null;   // dread passes all walls
            for (int i = 0; i < _nodes.Count; i++)
            {
                var n = _nodes[i];
                if (!WallWardMath.WallBlocksMagic(n.El, incoming)) continue;
                if (WallWardMath.SegmentNearPoint(from.x, from.y, to.x, to.y,
                                                  n.Pos.x, n.Pos.y, n.Radius))
                    return n;
            }
            return null;
        }

        // Is this point inside a wall node that stops missiles (or, for a fire
        // missile, one that stops fire)? Returns the wall's element when hit.
        // Stone outranks any overlapping wall: it is the ABSOLUTE missile shield,
        // so an arrow standing in both a gust and a rampart answers to the stone
        // (and never gets a wind pass-through roll it shouldn't have).
        public static MagicElement? MissileWardAt(Vec3 pos, bool fireMissile)
        {
            MagicElement? hit = null;
            for (int i = 0; i < _nodes.Count; i++)
            {
                var n = _nodes[i];
                bool stops = WallWardMath.WallBlocksMissiles(n.El)
                          || (fireMissile && WallWardMath.WallBlocksMagic(n.El, MagicElement.Fire));
                if (!stops) continue;
                float dx = pos.x - n.Pos.x, dy = pos.y - n.Pos.y;
                if (dx * dx + dy * dy > n.Radius * n.Radius) continue;
                if (n.El == MagicElement.Earth) return MagicElement.Earth;
                if (hit == null) hit = n.El;
            }
            return hit;
        }

        public static bool HasBlockingNodes => _nodes.Count > 0;

        // ── Mission tick — expiry + the missile sweep ────────────────────────────
        public static void Tick(float dt)
        {
            if (_blockMsgCooldown > 0f) _blockMsgCooldown -= dt;

            for (int i = _recent.Count - 1; i >= 0; i--)
            {
                _recent[i].Age += dt;
                if (_recent[i].Age > RecentCastMemorySec) _recent.RemoveAt(i);
            }

            if (_nodes.Count == 0) return;
            for (int i = _nodes.Count - 1; i >= 0; i--)
            {
                _nodes[i].Remaining -= dt;
                if (_nodes[i].Remaining <= 0f) _nodes.RemoveAt(i);
            }
            if (_nodes.Count == 0)
            {
                // No wall stands: forget the wind pass-throughs, so a missile
                // index the engine later reuses cannot inherit a stale pass.
                if (_windPassedMissiles.Count > 0) _windPassedMissiles.Clear();
                return;
            }

            // Arrows and bolts die against walls of wind and stone — whoever
            // loosed them. (Collect first: removal invalidates the list.)
            try
            {
                var mission = Mission.Current;
                if (mission == null) return;
                List<int> stopped = null;
                foreach (var m in mission.MissilesList)
                {
                    Vec3 mpos;
                    try
                    {
                        if (m.Entity == null) continue;
                        mpos = m.Entity.GlobalPosition;
                    }
                    catch { continue; }
                    var wardEl = MissileWardAt(mpos, fireMissile: false);
                    if (wardEl == null) continue;
                    // Stone stops every shaft outright. Wind only wrestles them:
                    // one roll per arrow at the gust's edge — the few that punch
                    // through are remembered and never re-rolled a frame later.
                    if (wardEl == MagicElement.Wind)
                    {
                        if (_windPassedMissiles.Contains(m.Index)) continue;
                        if (!WallWardMath.WindStopsMissile(_rng.NextDouble()))
                        {
                            _windPassedMissiles.Add(m.Index);
                            continue;
                        }
                    }
                    (stopped = stopped ?? new List<int>()).Add(m.Index);
                    // The arrow dies visibly at the wall.
                    try
                    {
                        SpellEffects.SpawnNatureBurst(mpos,
                            wardEl == MagicElement.Wind ? NatureElement.Wind : NatureElement.Earth, 0.4f);
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
                if (stopped != null)
                    foreach (int idx in stopped)
                        try { mission.RemoveMissileAsClient(idx); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Feedback + elemental byproducts ──────────────────────────────────────
        // Where a working dies against a wall, the elements REACT like the world
        // would: quenched fire boils into a standing cloud of steam; a wave broken
        // on stone churns the ground before it to MUD (which bogs down everything
        // that crosses it, cavalry worst); a devoured gale makes the flame flare.
        // Plus a throttled log line so the player understands WHY the magic failed.
        private static void ShowBlockFizzle(MagicElement wall, MagicElement incoming, Vec3 at)
        {
            try
            {
                switch (wall)
                {
                    case MagicElement.Water:
                        SpellEffects.SpawnNatureBurst(at, NatureElement.Water, 0.8f);
                        if (incoming == MagicElement.Fire)
                        {
                            // Fire on water — a boiling cloud of steam hangs there.
                            SpellEffects.SpawnTempSmokeParticle(at + new Vec3(0f, 0f, 0.5f), 4f);
                            SpellEffects.SpawnTempSmokeParticle(at + new Vec3(0.8f, 0.4f, 1.0f), 3f);
                        }
                        break;
                    case MagicElement.Earth:
                        SpellEffects.SpawnNatureBurst(at, NatureElement.Earth, 0.8f);
                        if (incoming == MagicElement.Water)
                            SpellEffects.SpawnMudPatch(at);   // the broken wave churns the ground
                        break;
                    case MagicElement.Wind:
                        SpellEffects.SpawnNatureBurst(at, NatureElement.Wind, 0.8f);
                        break;
                    case MagicElement.Fire:
                        // The gale feeds the flame — a visible flare where it died.
                        SpellEffects.SpawnTempFireParticle(at, 1.4f);
                        break;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            string line;
            if (wall == MagicElement.Water && incoming == MagicElement.Fire)      line = "The mist drinks the fire — steam, and nothing more.";
            else if (wall == MagicElement.Water)                                   line = "The gale dies against the standing water.";
            else if (wall == MagicElement.Fire)                                    line = "The wind feeds the wall of flame and is gone.";
            else if (wall == MagicElement.Wind)                                    line = "The flung stone is scattered on the wind.";
            else                                                                   line = "The wave breaks against the standing stone.";
            // Throttle only REPEATS of the same reason — a cast dying to a new
            // wall must always say why, or the fizzle reads as a bug.
            if (_blockMsgCooldown > 0f && line == _lastBlockLine) return;
            _blockMsgCooldown = 2f;
            _lastBlockLine = line;
            try
            {
                TaleWorlds.Library.InformationManager.DisplayMessage(
                    new InformationMessage(line, new Color(0.6f, 0.75f, 0.85f)));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
