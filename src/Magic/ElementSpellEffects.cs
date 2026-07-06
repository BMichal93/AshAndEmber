// =============================================================================
// ASH AND EMBER — Magic/ElementSpellEffects.cs
//
// Battle effects for the unified elemental magic. Each element has an ATTACK
// (released with the attack input) and a WALL (released with block). Every
// attack has its OWN silhouette so the five elements read apart at a glance:
//
//   Fire   — bolt that EXPLODES on impact / wall of fire        (flying missile)
//   Wind   — forward gust/stream           / wall of wind (blocks missiles, slows)
//   Earth  — forward line of erupting roots / stone wall (Thornwall)
//   Water  — forward slowing wave (cone)    / mist wall
//   Spirit — nova: panic men & horses and   / wall that lifts allies' morale
//            issue a random order              and mends them a little
//
// Wind/Earth/Water reuse NatureEffects (un-gated NPC path) so the existing
// visuals carry over — the Wind gust and Earth line are shaped there too (see
// NatureEffects.BattleGale / BattleEntangle). Fire (a flying bolt) and Spirit
// (a nova) are implemented here. Aging and the free-hand / weight / Steel gates
// are handled by ElementMagicInput, so these methods just apply the effect.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static class ElementSpellEffects
    {
        private static readonly Random _rng = new Random();

        // ── Ignition state (a deep draw sets its marks alight) ──────────────────
        // One entry per burning agent; re-igniting refreshes to the stronger burn.
        // Ticked from MagicMissionBehavior; cleared with the rest of battle state.
        private class Ignition
        {
            public Agent Target;
            public Agent Source;
            public float Dps;
            public float Remaining;
            public float TickTimer = 1f;
            public bool  Ashen;      // frost-bite visuals for the cold's mask
        }
        private static readonly List<Ignition> _ignitions = new List<Ignition>();

        // ── Flying fire bolts (the Fire attack) ─────────────────────────────────
        // A fast projectile that travels forward and BURSTS on the first foe it
        // reaches (or at the end of its flight), scattering fire in a blast. One
        // entry per bolt in flight; ticked from Tick, cleared with battle state.
        private class FireBolt
        {
            public Vec3  Position;
            public Vec3  Forward;
            public float TravelLeft;
            public Agent Caster;
            public Team  CasterTeam;
            public float Power;
            public bool  Ashen;
            public float TrailTimer;
            public const float Speed        = 30f;   // m/s
            public const float DetectRadius = 2.4f;  // horizontal reach at which a foe trips the burst
            public const float DetectHeight = 3.0f;  // vertical band (foot-to-mounted) the trigger spans
            public const float TrailInterval = 0.03f;
        }
        private static readonly List<FireBolt> _bolts = new List<FireBolt>();

        public static void ClearBattleState() { _ignitions.Clear(); _bolts.Clear(); }

        public static void Tick(float dt)
        {
            TickBolts(dt);
            if (_ignitions.Count == 0) return;
            for (int i = _ignitions.Count - 1; i >= 0; i--)
            {
                var ig = _ignitions[i];
                bool alive = false;
                try { alive = ig.Target != null && ig.Target.IsActive(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                if (!alive) { _ignitions.RemoveAt(i); continue; }

                ig.Remaining -= dt;
                ig.TickTimer -= dt;
                if (ig.TickTimer <= 0f)
                {
                    ig.TickTimer = 1f;
                    if (!SpellEffects.IsWarded(ig.Target))
                        // Element-typed so the burn obeys the weakness wheel: living
                        // flame keeps melting a Frost-Born (×2.2), the Ashen cold
                        // reads as Water and drowns a Flame-Born. Ordinary men take
                        // it straight (×1). The Kindled cannot be casually ignited.
                        try { SpellEffects.DamageAgent(ig.Target, ig.Dps, ColorSchool.Red, ig.Source,
                                ig.Ashen ? MagicElement.Water : MagicElement.Fire); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    try
                    {
                        Vec3 at = ig.Target.Position + new Vec3(0f, 0f, 0.5f);
                        // A real, visible pillar of flame that clings to the burning
                        // body — two stacked wisps so the burn READS at a glance.
                        if (ig.Ashen) { SpellEffects.SpawnTempSnowParticle(at, 1.4f); SpellEffects.SpawnTempSnowParticle(at + new Vec3(0f, 0f, 0.5f), 1.0f); }
                        else          { SpellEffects.SpawnTempFireParticle(at, 1.4f); SpellEffects.SpawnTempFireParticle(at + new Vec3(0f, 0f, 0.5f), 1.0f); }
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
                if (ig.Remaining <= 0f) _ignitions.RemoveAt(i);
            }
        }

        // Water puts a burning man out — a wave or standing mist douses the
        // ignition to a puff of steam. Returns true when a burn was quenched.
        public static bool QuenchIgnition(Agent target)
        {
            for (int i = _ignitions.Count - 1; i >= 0; i--)
            {
                if (_ignitions[i].Target != target) continue;
                _ignitions.RemoveAt(i);
                try { SpellEffects.SpawnTempSmokeParticle(target.Position + new Vec3(0f, 0f, 0.6f), 2f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                return true;
            }
            return false;
        }

        // Public entry for the Unbinding (ElementUltimates.FireNova) — the nova
        // sets its whole ring alight through the same ignition state as the cone.
        public static void IgniteTarget(Agent target, Agent source, float power, bool ashen)
            => Ignite(target, source, power, ashen);

        // Set (or refresh) a burn on a struck foe. Weak draws ignite nothing.
        private static void Ignite(Agent target, Agent source, float power, bool ashen)
        {
            float dps = ElementMagicMath.IgniteDps(power);
            if (dps < 1f) return;
            foreach (var ig in _ignitions)
            {
                if (ig.Target != target) continue;
                // Already alight — keep whichever burn is fiercer, refresh the clock.
                if (dps > ig.Dps) ig.Dps = dps;
                ig.Remaining = ElementMagicMath.IgniteSeconds;
                ig.Source    = source;
                return;
            }
            _ignitions.Add(new Ignition
            {
                Target = target, Source = source, Dps = dps,
                Remaining = ElementMagicMath.IgniteSeconds, Ashen = ashen,
            });
        }

        // ── Magnitudes ──────────────────────────────────────────────────────────
        private const float FireBoltRange    = 22f;    // how far the bolt flies (a full charge lances it further)
        private const float FireBoltDamage   = 44f;    // blast core, ×power — the bruiser, highest single hit
        private const float FireBoltRadius   = 3.5f;   // burst radius on impact
        private const float FireWallRange    = 6f;
        private const float FireWallWidth    = 4f;
        private const float FireWallDamage   = 14f;    // contact hit as the flame front sweeps up
        private const float FireWallBurnTick = 10f;    // per-second burn for those who hold the line
        private const float FireWallBurnSec  = 5f;     // how long the wall of fire smoulders
        private const float SiegeConeDamage  = 150f;   // vs wooden machines/gates, ×power
        private const float SpiritRadius     = 9f;
        private const float SpiritFearSlow   = 0.55f;  // panicked enemies slow to this
        private const float SpiritFearSec    = 6f;
        private const float SpiritMorale     = 12f;    // ally party morale on the wall
        private const float SpiritHealFrac   = 0.18f;  // ally heal fraction
        private const int   SpiritRadiusInt  = 9;

        // ── Public dispatch ─────────────────────────────────────────────────────
        // `power` (0..1+) scales the working's strength — the caller sets it from
        // how long the charge was drawn. Defaults to full for NPC callers.
        public static void CastAttack(MagicElement el, Agent caster, float power = 1f)
        {
            if (caster == null || !caster.IsActive()) return;
            // Under the Weeping Sky (the Water Unbinding), fire loosed from inside
            // the rain works at half strength — judged at the caster's position.
            if (el == MagicElement.Fire)
                try { power *= ElementUltimates.FireDampAt(caster.Position); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            switch (el)
            {
                case MagicElement.Fire:   FireMissile(caster, power);  break;
                case MagicElement.Wind:   NatureEffects.ExecuteNpc(NaturePower.Gale,     caster, caster.Team, power); break;
                case MagicElement.Earth:  NatureEffects.ExecuteNpc(NaturePower.Entangle, caster, caster.Team, power); break;
                case MagicElement.Water:  NatureEffects.ExecuteNpc(NaturePower.Torrent,  caster, caster.Team, power); break;
                case MagicElement.Spirit: SpiritPanic(caster, power); break;
            }
            CastFlash(el, caster);
            // Enemy mages remember what was thrown, and answer with the counter-wall.
            try { ElementWallWards.NoteCast(el, caster.Team); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { SpellEffects.RecordMagicCast(caster.Position); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        public static void CastWall(MagicElement el, Agent caster, float power = 1f)
        {
            if (caster == null || !caster.IsActive()) return;
            // The rain dampens a wall of fire raised inside it, like the cone.
            if (el == MagicElement.Fire)
                try { power *= ElementUltimates.FireDampAt(caster.Position); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            switch (el)
            {
                case MagicElement.Fire:   FireWall(caster, power);  break;
                case MagicElement.Wind:   NatureEffects.ExecuteNpc(NaturePower.Windwall,  caster, caster.Team, power); break;
                case MagicElement.Earth:  NatureEffects.ExecuteNpc(NaturePower.Thornwall, caster, caster.Team, power); break;
                case MagicElement.Water:  NatureEffects.ExecuteNpc(NaturePower.Mistwall,  caster, caster.Team, power); break;
                case MagicElement.Spirit: SpiritWall(caster, power); break;
            }
            CastFlash(el, caster);
            try { SpellEffects.RecordMagicCast(caster.Position); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Fire ────────────────────────────────────────────────────────────────
        // A bolt of fire is hurled forward; it bursts on the first foe it reaches
        // (or at the end of its flight), scattering flame in a blast. A fuller draw
        // sends it further and hits harder.
        private static void FireMissile(Agent caster, float power)
        {
            Vec3 pos; Vec3 fwd;
            try { pos = caster.Position + new Vec3(0f, 0f, 1.2f); fwd = caster.LookDirection; fwd.z = 0f; if (fwd.Length < 0.01f) return; fwd.Normalize(); }
            catch { return; }
            bool ashen = CasterAshen(caster);
            Vec3 rgb   = Palette(MagicElement.Fire, ashen);
            Vec3 start = pos + fwd * 1.5f;
            _bolts.Add(new FireBolt
            {
                Position   = start,
                Forward    = fwd,
                // A fully-drawn bolt lances far further than an instant flick.
                TravelLeft = ElementMagicMath.ConeRange(FireBoltRange, power),
                Caster     = caster,
                CasterTeam = caster.Team,
                Power      = power,
                Ashen      = ashen,
            });
            // A gout of flame leaves the caster's hand as the bolt is loosed.
            FireBloom(start, ashen, rgb, 1.2f, false);
            try { SpellEffects.BeginAgentGlow(caster, GlowSchool(MagicElement.Fire, ashen), 1.5f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Advance every bolt in flight, trailing fire; burst on contact or at range's end.
        private static void TickBolts(float dt)
        {
            if (_bolts.Count == 0) return;
            Mission mission; try { mission = Mission.Current; } catch { _bolts.Clear(); return; }
            if (mission == null) { _bolts.Clear(); return; }

            for (int i = _bolts.Count - 1; i >= 0; i--)
            {
                FireBolt b = _bolts[i];
                float moved = FireBolt.Speed * dt;
                b.Position   += b.Forward * moved;
                b.TravelLeft -= moved;

                // A living trail of fire clings behind the bolt (the Ashen cold shows pale).
                b.TrailTimer -= dt;
                if (b.TrailTimer <= 0f)
                {
                    b.TrailTimer = FireBolt.TrailInterval;
                    try
                    {
                        if (b.Ashen) SpellEffects.SpawnTempSnowParticle(b.Position, 1.0f);
                        else         SpellEffects.SpawnTempFireParticle(b.Position, 1.0f);
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    SpawnLight(b.Position, Palette(MagicElement.Fire, b.Ashen), 1.2f);
                }

                // Burst on the first live enemy the bolt reaches.
                Agent struck = FirstEnemyNear(b.CasterTeam, b.Caster, b.Position, FireBolt.DetectRadius, mission, FireBolt.DetectHeight);
                if (struck != null) { ExplodeBolt(b, b.Position); _bolts.RemoveAt(i); continue; }
                if (b.TravelLeft <= 0f) { ExplodeBolt(b, b.Position); _bolts.RemoveAt(i); }
            }
        }

        // The bolt bursts: fire scatters in a blast, scorching and igniting foes
        // caught in it and charring timber, just as the old cone did at its throat.
        private static void ExplodeBolt(FireBolt b, Vec3 at)
        {
            Vec3 rgb = Palette(MagicElement.Fire, b.Ashen);
            foreach (Agent a in EnemiesNearPos(b.CasterTeam, b.Caster, at, FireBoltRadius))
            {
                if (SpellEffects.IsWarded(a)) continue;
                // A wall of standing water between the burst and the mark drinks the fire.
                try { if (ElementWallWards.BlocksPath(MagicElement.Fire, at, a.Position, out _)) continue; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { SpellEffects.DamageAgent(a, FireBoltDamage * b.Power, ColorSchool.Red, b.Caster, MagicElement.Fire); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                // A deep draw sets the mark alight — the burn finishes what the
                // strike began (the Ashen cold clings on as deep frost instead).
                try { Ignite(a, b.Caster, b.Power, b.Ashen); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                FireBloom(a.Position, b.Ashen, rgb, 1.0f, false);
            }
            // Timber burns: siege engines and gates in the blast char under the same
            // fire (the cold splits the frozen grain just as surely).
            try { SpellEffects.DamageBurnableStructures(at, FireBoltRadius + 0.5f, SiegeConeDamage * b.Power, b.Caster); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            // A living eruption of flame; the Ashen show only the cold's pale light.
            FireBloom(at, b.Ashen, rgb, 3f, true);
            try { SpellEffects.RecordMagicCast(at); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // First live enemy whose feet fall within `radius` *horizontally* of the
        // bolt and inside a vertical band `height` tall (bolt-centred, not
        // caster-centred like EnemiesNear); null if none. Ignores mounts.
        // A flat 3-D sphere check missed most foes: the bolt flies at chest height
        // (~1.2 m) while Agent.Position sits at the feet, so a 1.6 m sphere left only
        // ~1 m of usable horizontal room and the bolt overflew looters, then burst in
        // empty ground at max range. Measuring horizontally with a tall band fixes it.
        private static Agent FirstEnemyNear(Team casterTeam, Agent caster, Vec3 at, float radius, Mission mission, float height)
        {
            float r2 = radius * radius;
            List<Agent> agents;
            try { agents = mission.Agents.ToList(); } catch { return null; }
            foreach (Agent a in agents)
            {
                if (a == caster || !a.IsActive() || a.IsMount) continue;
                if (casterTeam != null && a.Team == casterTeam) continue;
                float dz = a.Position.z - at.z;
                if (dz < -height || dz > height) continue;
                float dx = a.Position.x - at.x, dy = a.Position.y - at.y;
                if (dx * dx + dy * dy <= r2) return a;
            }
            return null;
        }

        // All enemies within `radius` of an arbitrary point (the burst centre).
        private static IEnumerable<Agent> EnemiesNearPos(Team casterTeam, Agent caster, Vec3 at, float radius)
        {
            float r2 = radius * radius;
            List<Agent> agents;
            try { agents = Mission.Current.Agents.ToList(); } catch { yield break; }
            foreach (Agent a in agents)
            {
                if (a == caster || !a.IsActive() || a.IsMount) continue;
                if (casterTeam != null && a.Team == casterTeam) continue;
                // Horizontal reach only: the burst can occur at chest height, and a
                // 3-D check against a foe's feet would shave the effective radius.
                float dx = a.Position.x - at.x, dy = a.Position.y - at.y;
                if (dx * dx + dy * dy <= r2) yield return a;
            }
        }

        // A wall of fire just ahead — burns those who stand in its line. Thrown
        // weakly it is a single thin curtain; drawn to full it thickens into a
        // filled rectangle of flame, several rows deep, that is far harder to cross.
        private static void FireWall(Agent caster, float power)
        {
            Vec3 pos; Vec3 fwd;
            try { pos = caster.Position; fwd = caster.LookDirection; fwd.z = 0f; fwd.Normalize(); }
            catch { return; }
            bool ashen = CasterAshen(caster);
            Vec3 rgb = Palette(MagicElement.Fire, ashen);
            Vec3 right  = new Vec3(fwd.y, -fwd.x, 0f);
            // The charge decides both the wall's width and how many rows deep it runs.
            float frac  = ElementMagicMath.ChargeFraction(power);
            float width = FireWallWidth * (0.7f + 0.6f * frac);   // wider when charged
            int   rows  = ElementMagicMath.WallDepthRows(power);  // 1 (line) → filled rectangle
            float rowSpacing = 1.6f;

            for (int r = 0; r < rows; r++)
            {
                Vec3 rowCentre = pos + fwd * (FireWallRange + r * rowSpacing);
                // A standing curtain of flame the length of the wall — real fire for
                // the living, a wall of driven frost and snow for the Ashen cold.
                for (float f = -width; f <= width; f += 1.5f)
                {
                    Vec3 node = rowCentre + right * f;
                    try
                    {
                        if (ashen)
                        {
                            SpellEffects.SpawnTempSnowParticle(node + new Vec3(0f, 0f, 0.4f), 2.5f);
                            // The cold deepens the drifts it stands on. A single
                            // wisp per node — the clusters above already churn, and
                            // a full wall is dozens of nodes in one frame.
                            if (SpellEffects.SceneIsSnowy())
                                SpellEffects.SpawnTempSnowWisp(node + new Vec3(0.5f, 0.3f, 0.8f), 2.5f);
                        }
                        else
                        {
                            SpellEffects.SpawnTempFireParticle(node + new Vec3(0f, 0f, 0.4f), 2.5f);
                            // Living flame on snow-bound ground — the wall stands in
                            // steam. One wisp per node keeps the frame alive.
                            if (SpellEffects.SceneIsSnowy())
                                SpellEffects.SpawnTempSmokeWisp(node + new Vec3(0f, 0f, 0.6f), 2.5f);
                        }
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    SpawnLight(node, rgb, 1.4f);
                    // The standing flame WARDS: its updraft devours any gale that
                    // crosses it while it burns (the Ashen frost stands the same).
                    try { ElementWallWards.RegisterNode(MagicElement.Fire, node, 1.6f, FireWallBurnSec, caster.Team); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
                // The living fire lingers on each row — a burning band that scorches
                // any who hold it (the Ashen cold does not smoulder).
                if (!ashen)
                    try
                    {
                        SpellEffects.SpawnFireWallPatches(rowCentre, right, width,
                            FireWallBurnTick * power, FireWallBurnSec, caster.Team);
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }

            // Contact hit as the wall erupts — only inside the rectangle's actual
            // footprint (project onto the wall's axes), so a foe standing beside or
            // behind the caster is never scorched by a wall raised ahead of him.
            float depth = (rows - 1) * rowSpacing;
            foreach (Agent a in EnemiesNear(caster, FireWallRange + depth + width + 1.5f))
            {
                Vec3 d = a.Position - pos; d.z = 0f;
                float along  = Vec3.DotProduct(d, fwd);
                float across = Vec3.DotProduct(d, right);
                if (along < FireWallRange - 1.2f || along > FireWallRange + depth + 1.2f) continue;
                if (Math.Abs(across) > width + 1.5f) continue;
                if (SpellEffects.IsWarded(a)) continue;
                try { SpellEffects.DamageAgent(a, FireWallDamage * power, ColorSchool.Red, caster, MagicElement.Fire); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            try { SpellEffects.BeginAgentGlow(caster, GlowSchool(MagicElement.Fire, ashen), 1.5f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Spirit ────────────────────────────────────────────────────────────────
        // The attack strikes fear into men and horses near the caster's foes and
        // shouts a random order into the enemy ranks (a brief command that scatters
        // their order). Mounts bolt; men falter.
        private static void SpiritPanic(Agent caster, float power)
        {
            Vec3 pos; try { pos = caster.Position; } catch { return; }
            bool ashen = CasterAshen(caster);
            Vec3 rgb = Palette(MagicElement.Spirit, ashen);
            // A weaker draw panics for a shorter spell; a fuller draw holds them longer.
            float fearSec = SpiritFearSec * power;
            int struck = 0;
            foreach (Agent a in EnemiesNear(caster, SpiritRadius))
            {
                // Panic: slow them and, if mounted, make the horse bolt off-line.
                try { NatureEffects.ApplySpeedToken(a, SpiritFearSlow, fearSec); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { a.MakeVoice(SkinVoiceManager.VoiceType.Fear, SkinVoiceManager.CombatVoiceNetworkPredictionType.NoPrediction); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try
                {
                    if (a.MountAgent != null && a.MountAgent.IsActive())
                    {
                        Vec3 dir = (a.Position - pos); dir.z = 0f;
                        if (dir.Length > 0.1f) dir.Normalize(); else dir = new Vec3(1, 0, 0);
                        a.MountAgent.TeleportToPosition(a.MountAgent.Position + dir * 2.0f);
                        try { a.MountAgent.MakeVoice(SkinVoiceManager.VoiceType.Fear, SkinVoiceManager.CombatVoiceNetworkPredictionType.NoPrediction); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    }
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                // A wraith haze clings to each stricken foe.
                try { SpellEffects.SpawnTempSmokeParticle(a.Position + new Vec3(0f, 0f, 0.6f), 0.9f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                SpawnLight(a.Position, rgb, 0.8f);
                struck++;
            }
            IssueRandomEnemyOrder(caster);
            // A pall of spectral smoke wells up from the caster.
            try { SpellEffects.SpawnTempSmokeParticle(pos + new Vec3(0f, 0f, 0.6f), 1.4f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            SpawnLight(pos, rgb, 2.0f);
            try { SpellEffects.BeginAgentGlow(caster, GlowSchool(MagicElement.Spirit, ashen), 1.5f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // The wall lifts the courage of nearby allies and mends them a little.
        private static void SpiritWall(Agent caster, float power)
        {
            Vec3 pos; try { pos = caster.Position; } catch { return; }
            bool ashen = CasterAshen(caster);
            Vec3 rgb = Palette(MagicElement.Spirit, ashen);
            // The party-morale lift belongs to the CASTER's party — NPC lords raise
            // this ward too, and their working must not hearten the player's men.
            try
            {
                if (caster == Agent.Main)
                    MobileParty.MainParty.RecentEventsMorale += SpiritMorale * power;
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            int blessed = 0;
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount) continue;
                    if (caster.Team == null || a.Team != caster.Team) continue;
                    float dx = a.Position.x - pos.x, dy = a.Position.y - pos.y;
                    if (dx * dx + dy * dy > SpiritRadius * SpiritRadius) continue;
                    try { SpellEffects.HealAgent(a, SafeLimit(a) * SpiritHealFrac * power); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    blessed++;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            // A rising veil of spectral smoke marks the ward.
            try { SpellEffects.SpawnTempSmokeParticle(pos + new Vec3(0f, 0f, 0.6f), 1.6f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            SpawnLight(pos, rgb, 2.2f);
            try { SpellEffects.BeginAgentGlow(caster, GlowSchool(MagicElement.Spirit, ashen), 2.5f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Shouts a random order into a random enemy formation — charge, fall back, or
        // advance — breaking their coordination for a beat.
        private static void IssueRandomEnemyOrder(Agent caster)
        {
            try
            {
                var mission = Mission.Current;
                if (mission == null || caster.Team == null) return;
                var enemyTeams = mission.Teams.Where(t => t != null && t.IsValid && t.IsEnemyOf(caster.Team)).ToList();
                if (enemyTeams.Count == 0) return;
                var team = enemyTeams[_rng.Next(enemyTeams.Count)];
                var forms = team.FormationsIncludingEmpty.Where(f => f != null && f.CountOfUnits > 0).ToList();
                if (forms.Count == 0) return;
                var form = forms[_rng.Next(forms.Count)];
                switch (_rng.Next(3))
                {
                    case 0: form.SetMovementOrder(MovementOrder.MovementOrderRetreat); break;
                    case 1: form.SetMovementOrder(MovementOrder.MovementOrderCharge);  break;
                    default: form.SetMovementOrder(MovementOrder.MovementOrderAdvance); break;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────
        private static System.Collections.Generic.IEnumerable<Agent> EnemiesNear(Agent caster, float radius)
        {
            float r2 = radius * radius;
            Vec3 pos; try { pos = caster.Position; } catch { yield break; }
            System.Collections.Generic.List<Agent> agents;
            try { agents = Mission.Current.Agents.ToList(); } catch { yield break; }
            foreach (Agent a in agents)
            {
                if (a == caster || !a.IsActive() || a.IsMount) continue;
                if (caster.Team != null && a.Team == caster.Team) continue;
                float dx = a.Position.x - pos.x, dy = a.Position.y - pos.y;
                if (dx * dx + dy * dy <= r2) yield return a;
            }
        }

        private static float SafeLimit(Agent a)
        {
            try { return a.HealthLimit > 0f ? a.HealthLimit : 100f; } catch { return 100f; }
        }

        // ── Per-element visuals (the Ashen wear the cold mask) ─────────────────────
        // Each element has a living colour and a cold Ashen colour:
        //   Fire→Cold · Wind→Storm · Earth→Ash · Water→Snow · Spirit→Void.
        // Public accessor so the charging visual can tint its light to the loaded
        // element (Ashen-aware), matching the colour the cast itself will show.
        public static Vec3 ElementLightRgb(MagicElement el, bool ashen) => Palette(el, ashen);

        private static Vec3 Palette(MagicElement el, bool ashen)
        {
            if (ashen)
            {
                switch (el)
                {
                    case MagicElement.Fire:   return new Vec3(0.55f, 0.78f, 1.00f); // Cold — pale blue-white
                    case MagicElement.Wind:   return new Vec3(0.40f, 0.45f, 0.62f); // Storm — slate
                    case MagicElement.Earth:  return new Vec3(0.55f, 0.54f, 0.56f); // Ash — grey
                    case MagicElement.Water:  return new Vec3(0.88f, 0.94f, 1.00f); // Snow — white
                    default:                  return new Vec3(0.32f, 0.18f, 0.45f); // Void — deep violet
                }
            }
            switch (el)
            {
                case MagicElement.Fire:   return new Vec3(1.00f, 0.45f, 0.12f); // flame
                case MagicElement.Wind:   return new Vec3(0.70f, 0.95f, 0.92f); // pale gale
                case MagicElement.Earth:  return new Vec3(0.50f, 0.40f, 0.20f); // loam
                case MagicElement.Water:  return new Vec3(0.30f, 0.55f, 0.95f); // deep blue
                default:                  return new Vec3(0.55f, 0.40f, 0.70f); // Spirit — violet
            }
        }

        private static ColorSchool GlowSchool(MagicElement el, bool ashen)
            => ashen ? ColorSchool.Ashen
                     : el == MagicElement.Fire ? ColorSchool.Red : ColorSchool.Nature;

        // Is the caster drawing on the cold? The player by their Ashen state; an NPC
        // lord by the registry.
        private static bool CasterAshen(Agent caster)
        {
            try
            {
                if (caster == Agent.Main) return MageKnowledge.IsAshen;
                var hero = (caster?.Character as TaleWorlds.CampaignSystem.CharacterObject)?.HeroObject;
                return hero != null && ColourLordRegistry.IsAshenLord(hero);
            }
            catch { return false; }
        }

        // A single signature light in the element's (Ashen-aware) colour at the
        // caster — gives the nature-routed Wind/Earth/Water casts their cold mask too.
        private static void CastFlash(MagicElement el, Agent caster)
        {
            try { SpawnLight(caster.Position, Palette(el, CasterAshen(caster)), 2.2f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void SpawnLight(Vec3 pos, Vec3 rgb, float scale)
        {
            try { SpellEffects.SpawnTempLightRgb(pos + new Vec3(0f, 0f, 1f), rgb, 7f * scale, 0.7f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // The visible bloom of a fire cast. The living fire erupts in real flame —
        // a full burst-explosion for the main eruption, a scatter of flame at each
        // struck foe. The Ashen wield the cold, so their "fire" answers instead in
        // driven frost and snow beneath the pale blue light.
        private static void FireBloom(Vec3 at, bool ashen, Vec3 rgb, float lightScale, bool major)
        {
            try
            {
                if (ashen)
                {
                    SpellEffects.SpawnTempSnowParticle(at + new Vec3(0f, 0f, 0.4f), major ? 1.6f : 1.1f);
                    // The cold does not melt snow — it DEEPENS it: on snow-bound
                    // ground the Ashen fire thickens the drifts where it lands.
                    if (SpellEffects.SceneIsSnowy())
                        SpellEffects.SpawnTempSnowWisp(at + new Vec3(0.6f, 0.3f, 0.6f), major ? 2.2f : 1.4f);
                }
                else
                {
                    if (major) SpellEffects.SpawnBurstExplosion(at, ColorSchool.Red, 3f, 1.3f);
                    else       SpellEffects.SpawnTempFireParticle(at + new Vec3(0f, 0f, 0.4f), 1.1f);
                    // Living fire on snow-bound ground: the drifts steam and slump.
                    if (SpellEffects.SceneIsSnowy())
                        SpellEffects.SpawnTempSmokeParticle(at + new Vec3(0f, 0f, 0.4f), major ? 2.2f : 1.2f);
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            SpawnLight(at, rgb, lightScale);
        }
    }
}
