// =============================================================================
// ASH AND EMBER — Magic/ElementUltimates.cs
//
// Runtime layer of THE UNBINDING — each element's once-per-battle ultimate
// (all numbers and names live in the pure ElementUltimateMath). Five workings,
// five different shapes:
//
//   Fire   — nova around the caster (damage, ignition, bolting horses,
//            charred siege timber, a burning ring left behind).
//   Wind   — FLIGHT for the player (any hit drops you — a real fall); a
//            straight wind-LEAP for NPC lords, who cannot pilot free flight.
//   Earth  — a stone mantle on the caster (most damage shrugged off, slower).
//   Water  — a standing rain zone (quenches burns, halves fire, mires horses,
//            soaks bowstrings). Only ONE sky can stand — a recast replaces it.
//   Spirit — summons ONE terrain-shaped elemental to the caster's side.
//
// WIRING (all of it already done — listed so a fix knows where to look):
//   • MagicMissionBehavior.OnMissionTick   → Tick(dt)
//   • MagicMissionBehavior.OnAgentHit      → OnAgentHit(...)   (flight knock-out,
//     NPC windup interruption, mantle heal-back, rain archery damp)
//   • MagicMissionBehavior.OnEndMission and MainSubModule.OnGameStart
//                                          → ClearBattleState()
//   • SpellEffects.DamageAgent             → ReduceIfMantled(...)  (magic damage)
//   • ElementSpellEffects.CastAttack/Wall  → FireDampAt(...)       (rain vs fire)
//   • ElementMagicInput (the chord)        → PlayerCanUnbind / CastPlayerUltimate
//   • ColourLordAI.TryCast                 → TryQueueNpcUltimate(...)
//
// Nothing here is serialized: all state is mission-scoped and cleared with the
// rest of the battle state, so saves are untouched (fully backward compatible).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static class ElementUltimates
    {
        private static readonly Random _rng = new Random();

        // ── Once-per-battle bookkeeping ─────────────────────────────────────────
        // Player: one Unbinding per ELEMENT per battle. NPCs: one per LORD per
        // battle (marked at windup start — an interrupted working is still spent).
        private static readonly HashSet<MagicElement> _playerUsed = new HashSet<MagicElement>();
        private static readonly HashSet<string> _npcUsed = new HashSet<string>();

        // ── Flight (Wind) ───────────────────────────────────────────────────────
        private class Flight
        {
            public Agent Flyer;
            public float Remaining;
            public Vec3? FixedDir;     // null = steer by look direction (player);
                                       // set  = the NPC wind-leap's straight line
            public bool  Ashen;
            public float VisualTimer;
        }
        private static readonly List<Flight> _flights = new List<Flight>();

        // ── Stone mantle (Earth) ────────────────────────────────────────────────
        private class Mantle
        {
            public Agent Bearer;
            public float Remaining;
            public bool  Ashen;
            public float VisualTimer;
        }
        private static readonly List<Mantle> _mantles = new List<Mantle>();

        // ── The one sky (Water) ─────────────────────────────────────────────────
        private class RainZone
        {
            public Vec3  Centre;
            public float Remaining;
            public float TickTimer;
            public Team  CasterTeam;   // only the Ashen morale bleed reads this —
                                       // every other effect of the rain is impartial
            public bool  Ashen;
        }
        private static RainZone _rain;   // a single slot — there is only one sky

        // ── The land's champion (Spirit) ────────────────────────────────────────
        private class Champion
        {
            public Agent Elemental;
            public float Remaining;
            public ElementalKind Kind;
            public bool  Ashen;
            public float VisualTimer;
        }
        private static readonly List<Champion> _champions = new List<Champion>();

        // ── Pending NPC windups ─────────────────────────────────────────────────
        // NPC ultimates channel visibly for NpcWindupSeconds; ANY hit on the
        // caster during the channel breaks the working. Kept here (not in
        // SpellEffects._pendingNpcCasts) precisely so a hit can find and cancel it.
        private class NpcWindup
        {
            public Agent Caster;
            public MagicElement Element;
            public float Remaining;
            public bool  Ashen;
            public float VisualTimer;
        }
        private static readonly List<NpcWindup> _npcWindups = new List<NpcWindup>();

        public static void ClearBattleState()
        {
            _playerUsed.Clear();
            _npcUsed.Clear();
            _flights.Clear();
            _mantles.Clear();
            _rain = null;
            _champions.Clear();
            _npcWindups.Clear();
        }

        // =====================================================================
        // PLAYER ENTRY (called by ElementMagicInput on the Attack+Block chord)
        // =====================================================================

        public static bool PlayerCanUnbind(MagicElement el) => !_playerUsed.Contains(el);

        // Executes the loaded element's Unbinding for the player. Returns true
        // when the working actually fired (the caller then applies the aging
        // cost); false leaves the drawn charge intact.
        public static bool CastPlayerUltimate(MagicElement el, Agent caster)
        {
            if (caster == null || !caster.IsActive()) return false;
            if (_playerUsed.Contains(el)) return false;
            _playerUsed.Add(el);
            bool ashen = false; try { ashen = MageKnowledge.IsAshen; } catch { }
            Msg($"{ElementUltimateMath.UltimateName(el, ashen)} — the " +
                $"{(ashen ? ElementMagicMath.AshenElementName(el) : ElementMagicMath.ElementName(el))} is unbound!");
            Execute(el, caster, ashen);
            return true;
        }

        // =====================================================================
        // NPC ENTRY (called by ColourLordAI.TryCast, before its normal ladder)
        // =====================================================================

        // A lord reads the same tactical picture his normal casts use and, once
        // per battle in battles of NpcMinCombatants+, queues the Unbinding that
        // answers it. Returns true when a windup was queued (the caller sets his
        // cooldown and records the cost). Priority is survival-first:
        //   stone when wounded → the wind out when desperate → the nova when
        //   swarmed → the sky against cavalry or an enemy fire-mage → the
        //   land's champion as a calculating lord's opener.
        public static bool TryQueueNpcUltimate(Agent agent, Hero hero, float hpPct,
            int closeEnemies, int nearEnemies, int mountedNear,
            bool isAshen, List<MagicElement> known, CasterTemper temper)
        {
            if (agent == null || hero == null || Mission.Current == null) return false;
            if (_npcUsed.Contains(hero.StringId)) return false;
            if (!BattleBigEnough()) return false;
            if (_npcWindups.Any(w => w.Caster == agent)) return false;

            MagicElement? pick = null;
            if (hpPct < ElementUltimateMath.MantleHpFrac && closeEnemies >= 1
                && known.Contains(MagicElement.Earth))
                pick = MagicElement.Earth;
            else if (hpPct < ElementUltimateMath.LeapHpFrac
                && closeEnemies >= ElementUltimateMath.LeapCloseEnemies
                && known.Contains(MagicElement.Wind))
                pick = MagicElement.Wind;
            else if (closeEnemies >= ElementUltimateMath.NovaCloseEnemies)
                pick = MagicElement.Fire;   // Fire is innate — always known
            else if (known.Contains(MagicElement.Water) && _rain == null
                && (mountedNear >= ElementUltimateMath.RainMountedNear
                    || LastHostileWasFire(agent)))
                pick = MagicElement.Water;
            else if (known.Contains(MagicElement.Spirit) && temper == CasterTemper.Calculating
                && nearEnemies >= 2 && hpPct > 0.8f)
                pick = MagicElement.Spirit;

            if (pick == null) return false;

            // Marked SPENT at windup start: an interrupted Unbinding is gone for
            // the battle — that is the player's reward for riding the caster down.
            _npcUsed.Add(hero.StringId);
            try { SpellEffects.BeginCastLoop(agent); } catch { }
            _npcWindups.Add(new NpcWindup
            {
                Caster = agent, Element = pick.Value,
                Remaining = ElementUltimateMath.NpcWindupSeconds,
                Ashen = isAshen, VisualTimer = 0f,
            });
            AnnounceNpc(agent, hero,
                $"begins the Unbinding — {ElementUltimateMath.UltimateName(pick.Value, isAshen)}! Break the working!");
            return true;
        }

        private static bool LastHostileWasFire(Agent agent)
        {
            try { return ElementWallWards.LastHostileElement(agent.Team) == MagicElement.Fire; }
            catch { return false; }
        }

        private static bool BattleBigEnough()
        {
            try
            {
                int men = 0;
                foreach (Agent a in Mission.Current.Agents)
                    if (a != null && !a.IsMount && a.IsActive()) men++;
                return men >= ElementUltimateMath.NpcMinCombatants;
            }
            catch { return false; }
        }

        // =====================================================================
        // HOOKS (wired in MagicSystem / SpellEffects — see the header)
        // =====================================================================

        // Fire magic loosed from inside the rain works at half strength (checked
        // at the CASTER's position — a cone thrown from dry ground into the rain
        // is judged where it was born; the burns it sets are quenched by the
        // zone's own tick either way). The Ashen cold is fire-in-truth here and
        // is damped the same — the White Silence and the Long Winter contest the
        // same sky.
        public static float FireDampAt(Vec3 pos)
        {
            var r = _rain;
            if (r == null) return 1f;
            float dx = pos.x - r.Centre.x, dy = pos.y - r.Centre.y;
            return dx * dx + dy * dy <= ElementUltimateMath.RainRadius * ElementUltimateMath.RainRadius
                ? ElementUltimateMath.RainFireDamp : 1f;
        }

        // Called from SpellEffects.DamageAgent so MAGIC damage respects the stone
        // mantle too (weapon hits are healed back in OnAgentHit, because the hit
        // system applies them before we ever see them).
        public static float ReduceIfMantled(Agent target, float damage)
        {
            if (target == null || damage <= 0f || _mantles.Count == 0) return damage;
            foreach (var m in _mantles)
                if (m.Bearer == target && m.Remaining > 0f)
                    return ElementUltimateMath.MantleKeptDamage(damage);
            return damage;
        }

        // One entry point for everything the Unbinding must know about a landed
        // hit. OnAgentHit fires AFTER damage is applied, so mitigation here is
        // the established heal-back pattern (see the Nature resist in
        // MagicSystem.OnAgentHit / TryApplyAttackWeakening).
        public static void OnAgentHit(Agent victim, Agent attacker, int inflictedDamage, bool isMeleeHit)
        {
            if (victim == null) return;

            // 1. A flyer struck is a flyer FALLING — remove them from the tick and
            //    let gravity and real falling damage finish the sentence.
            if (inflictedDamage > 0 && _flights.Count > 0)
            {
                for (int i = _flights.Count - 1; i >= 0; i--)
                {
                    if (_flights[i].Flyer != victim) continue;
                    _flights.RemoveAt(i);
                    if (victim == Agent.Main)
                        Msg("The wind is struck from you — you fall!");
                    try { SpellEffects.SpawnNatureBurst(victim.Position, NatureElement.Wind, 0.8f); } catch { }
                }
            }

            // 2. Any hit on a channelling NPC breaks the Unbinding (and it stays
            //    spent — see TryQueueNpcUltimate).
            if (inflictedDamage > 0 && _npcWindups.Count > 0)
            {
                for (int i = _npcWindups.Count - 1; i >= 0; i--)
                {
                    if (_npcWindups[i].Caster != victim) continue;
                    var w = _npcWindups[i];
                    _npcWindups.RemoveAt(i);
                    try { SpellEffects.EndCastLoop(victim); } catch { }
                    try { SpellEffects.SpawnTempSmokeParticle(victim.Position + new Vec3(0f, 0f, 0.8f), 1.5f); } catch { }
                    if (Agent.Main != null && victim.Team != null && Agent.Main.Team != null
                        && victim.Team != Agent.Main.Team)
                        Msg($"The Unbinding is broken — {ElementUltimateMath.UltimateName(w.Element, w.Ashen)} dies unspoken.");
                }
            }

            // 3. The stone mantle drinks most of a WEAPON blow: heal back the
            //    shrugged-off fraction (magic damage is reduced in DamageAgent).
            if (inflictedDamage > 0 && _mantles.Count > 0)
            {
                foreach (var m in _mantles)
                {
                    if (m.Bearer != victim || m.Remaining <= 0f) continue;
                    float healBack = inflictedDamage * ElementUltimateMath.MantleDamageReduction;
                    if (healBack >= 1f) try { SpellEffects.HealAgent(victim, healBack); } catch { }
                    try { SpellEffects.SpawnNatureBurst(victim.Position, NatureElement.Earth, 0.5f); } catch { }
                    break;
                }
            }

            // 4. Wet bowstrings: a RANGED hit loosed from inside the rain loses
            //    part of its bite (heal-back, the same pattern as the mantle).
            if (inflictedDamage > 0 && !isMeleeHit && attacker != null && _rain != null)
            {
                try
                {
                    if (FireDampAt(attacker.Position) < 1f)
                    {
                        float healBack = inflictedDamage * ElementUltimateMath.RainArcheryDamp;
                        if (healBack >= 1f) SpellEffects.HealAgent(victim, healBack);
                    }
                }
                catch { }
            }
        }

        // =====================================================================
        // MISSION TICK
        // =====================================================================

        public static void Tick(float dt)
        {
            if (Mission.Current == null) return;
            try { TickNpcWindups(dt); } catch { }
            try { TickFlights(dt); } catch { }
            try { TickMantles(dt); } catch { }
            try { TickRain(dt); } catch { }
            try { TickChampions(dt); } catch { }
        }

        private static void TickNpcWindups(float dt)
        {
            for (int i = _npcWindups.Count - 1; i >= 0; i--)
            {
                var w = _npcWindups[i];
                bool alive = false;
                try { alive = w.Caster != null && w.Caster.IsActive() && w.Caster.Health > 0f; } catch { }
                if (!alive) { _npcWindups.RemoveAt(i); continue; }

                // The channel is LOUD: a swelling glow and the element's charge
                // particles, refreshed on a short interval, so the player can see
                // whom to ride down.
                w.VisualTimer -= dt;
                if (w.VisualTimer <= 0f)
                {
                    w.VisualTimer = 0.5f;
                    try
                    {
                        SpellEffects.BeginAgentGlow(w.Caster,
                            w.Ashen ? ColorSchool.Ashen
                                    : w.Element == MagicElement.Fire ? ColorSchool.Red : ColorSchool.Nature, 0.8f);
                        SpellEffects.SpawnTempLightRgb(w.Caster.Position + new Vec3(0f, 0f, 1f),
                            ElementSpellEffects.ElementLightRgb(w.Element, w.Ashen), 9f, 0.7f);
                    }
                    catch { }
                }

                w.Remaining -= dt;
                if (w.Remaining > 0f) continue;
                _npcWindups.RemoveAt(i);
                try { SpellEffects.EndCastLoop(w.Caster); } catch { }
                try { Execute(w.Element, w.Caster, w.Ashen); } catch { }
            }
        }

        // =====================================================================
        // DISPATCH
        // =====================================================================

        private static void Execute(MagicElement el, Agent caster, bool ashen)
        {
            switch (el)
            {
                case MagicElement.Fire:   FireNova(caster, ashen);        break;
                case MagicElement.Wind:   BeginFlight(caster, ashen);     break;
                case MagicElement.Earth:  BeginMantle(caster, ashen);     break;
                case MagicElement.Water:  BeginRain(caster, ashen);       break;
                case MagicElement.Spirit: SummonChampion(caster, ashen);  break;
            }
            try { SpellEffects.TryCastSound(caster.Position,
                    ashen ? ColorSchool.Ashen : el == MagicElement.Fire ? ColorSchool.Red : ColorSchool.Nature); } catch { }
            try { SpellEffects.RecordMagicCast(caster.Position); } catch { }
            try { ElementWallWards.NoteCast(el, caster.Team); } catch { }
        }

        // ── FIRE — The First Flame Remembered / The Long Winter ─────────────────
        // A nova centred on the caster: heavy damage and full ignition on every
        // foe in the ring, horses bolt in panic, siege timber chars, and a
        // burning ring is left where the world remembers the caster stood.
        // Cast inside someone's rain, the nova itself is damped like any fire.
        private static void FireNova(Agent caster, bool ashen)
        {
            Vec3 pos; try { pos = caster.Position; } catch { return; }
            float power = FireDampAt(pos);   // 1.0 dry, 0.5 under the weeping sky
            Vec3 rgb = ElementSpellEffects.ElementLightRgb(MagicElement.Fire, ashen);

            foreach (Agent a in EnemiesNear(caster, ElementUltimateMath.NovaRadius))
            {
                if (SpellEffects.IsWarded(a)) continue;
                try { SpellEffects.DamageAgent(a, ElementUltimateMath.NovaDamage * power, ColorSchool.Red, caster); } catch { }
                // Full ignition on everything the nova touches (the Ashen cold
                // grips as deep frost instead of a burn — same dread, colder face).
                if (!ashen) try { ElementSpellEffects.IgniteTarget(a, caster, 1f * power, ashen); } catch { }
                else        try { NatureEffects.ApplySpeedToken(a, ElementUltimateMath.NovaAshenSlowMult,
                                                                   ElementUltimateMath.NovaAshenSlowSec); } catch { }
                // Horses panic and bolt away from the eruption.
                try
                {
                    if (a.MountAgent != null && a.MountAgent.IsActive())
                    {
                        Vec3 dir = a.Position - pos; dir.z = 0f;
                        if (dir.Length > 0.1f) dir.Normalize(); else dir = new Vec3(1f, 0f, 0f);
                        a.MountAgent.TeleportToPosition(a.MountAgent.Position + dir * ElementUltimateMath.NovaHorseBolt);
                        a.MountAgent.MakeVoice(SkinVoiceManager.VoiceType.Fear,
                            SkinVoiceManager.CombatVoiceNetworkPredictionType.NoPrediction);
                    }
                }
                catch { }
                try
                {
                    if (ashen) SpellEffects.SpawnTempSnowParticle(a.Position + new Vec3(0f, 0f, 0.4f), 1.2f);
                    else       SpellEffects.SpawnTempFireParticle(a.Position + new Vec3(0f, 0f, 0.4f), 1.2f);
                }
                catch { }
            }

            // The survivors visibly flee the eruption; timber in the ring chars.
            try { SpellEffects.ScatterEnemies(pos, ElementUltimateMath.NovaRadius, caster.Team); } catch { }
            try { SpellEffects.DamageBurnableStructures(pos, ElementUltimateMath.NovaRadius,
                    ElementUltimateMath.NovaSiegeDamage * power, caster); } catch { }

            // The burning ring: eight tangent bands around the caster (the living
            // fire smoulders and scorches; the cold leaves standing frost that
            // wards like any fire wall — its updraft/steam devours crossing gales).
            for (int k = 0; k < 8; k++)
            {
                double ang = k * Math.PI / 4.0;
                Vec3 outDir  = new Vec3((float)Math.Cos(ang), (float)Math.Sin(ang), 0f);
                Vec3 tangent = new Vec3(-outDir.y, outDir.x, 0f);
                Vec3 node = pos + outDir * ElementUltimateMath.NovaRingRadius;
                try
                {
                    if (ashen) SpellEffects.SpawnTempSnowParticle(node + new Vec3(0f, 0f, 0.4f), ElementUltimateMath.NovaRingBurnSec);
                    else       SpellEffects.SpawnTempFireParticle(node + new Vec3(0f, 0f, 0.4f), ElementUltimateMath.NovaRingBurnSec);
                }
                catch { }
                try { ElementWallWards.RegisterNode(MagicElement.Fire, node, 1.6f,
                        ElementUltimateMath.NovaRingBurnSec, caster.Team); } catch { }
                if (!ashen)
                    try { SpellEffects.SpawnFireWallPatches(node, tangent, 2.2f,
                            ElementUltimateMath.NovaRingBurnDps * power,
                            ElementUltimateMath.NovaRingBurnSec, caster.Team); } catch { }
            }

            // The eruption itself.
            try
            {
                if (ashen)
                {
                    SpellEffects.SpawnTempSnowParticle(pos + new Vec3(0f, 0f, 0.5f), 2.5f);
                    if (SpellEffects.SceneIsSnowy())
                        SpellEffects.SpawnTempSnowWisp(pos + new Vec3(0.6f, 0.3f, 0.8f), 3f);
                }
                else
                {
                    SpellEffects.SpawnBurstExplosion(pos, ColorSchool.Red, ElementUltimateMath.NovaRadius * 0.5f, 1.6f);
                    if (SpellEffects.SceneIsSnowy())
                        SpellEffects.SpawnTempSmokeParticle(pos + new Vec3(0f, 0f, 0.5f), 3f);
                }
            }
            catch { }
            try { SpellEffects.SpawnTempLightRgb(pos + new Vec3(0f, 0f, 1.5f), rgb, 22f, 1.4f); } catch { }
            try { SpellEffects.BeginAgentGlow(caster, ashen ? ColorSchool.Ashen : ColorSchool.Red, 2.5f); } catch { }
        }

        // ── WIND — On the Wings of the Gale / Carried by the Howl ───────────────
        // The player steers by gaze at FlightSpeed, FlightHeight above the ground.
        // NPC lords get the wind-LEAP instead: a fixed straight glide away from
        // the enemies pressing them (AI cannot pilot free flight).
        private static void BeginFlight(Agent caster, bool ashen)
        {
            _flights.RemoveAll(f => f.Flyer == caster);
            Vec3? fixedDir = null;
            float seconds = ElementUltimateMath.FlightSeconds;
            if (caster != Agent.Main)
            {
                // Leap AWAY from the local enemy centroid (or backwards if none).
                seconds = ElementUltimateMath.NpcLeapSeconds;
                Vec3 away = default(Vec3);
                int n = 0;
                foreach (Agent e in EnemiesNear(caster, 12f))
                { away += caster.Position - e.Position; n++; }
                if (n > 0) { away.z = 0f; if (away.Length > 0.1f) away.Normalize(); }
                if (n == 0 || away.Length < 0.1f)
                { try { away = caster.LookDirection * -1f; away.z = 0f; away.Normalize(); } catch { away = new Vec3(1f, 0f, 0f); } }
                fixedDir = away;
            }
            _flights.Add(new Flight { Flyer = caster, Remaining = seconds, FixedDir = fixedDir, Ashen = ashen });
            if (caster == Agent.Main)
                Msg("The wind bears you — steer with your gaze. One arrow ends it.");
            try { SpellEffects.SpawnNatureBurst(caster.Position, NatureElement.Wind, 1.5f); } catch { }
        }

        private static void TickFlights(float dt)
        {
            if (_flights.Count == 0) return;
            var scene = Mission.Current?.Scene;
            for (int i = _flights.Count - 1; i >= 0; i--)
            {
                var f = _flights[i];
                bool alive = false;
                try { alive = f.Flyer != null && f.Flyer.IsActive() && f.Flyer.Health > 0f; } catch { }
                if (!alive) { _flights.RemoveAt(i); continue; }

                f.Remaining -= dt;
                if (f.Remaining <= 0f)
                {
                    // Natural expiry: FlightHeightAt has already eased them to the
                    // ground over the final seconds — a gentle step-off, no fall.
                    _flights.RemoveAt(i);
                    if (f.Flyer == Agent.Main) Msg("The wind sets you down.");
                    continue;
                }

                try
                {
                    // Direction: the flyer's gaze (player) or the fixed leap line
                    // (NPC), flattened — height is the curve's business, not the gaze's.
                    Vec3 dir = f.FixedDir ?? f.Flyer.LookDirection;
                    dir.z = 0f;
                    if (dir.Length > 0.05f) dir.Normalize(); else dir = new Vec3(0f, 1f, 0f);
                    float speed = f.FixedDir == null ? ElementUltimateMath.FlightSpeed
                                                     : ElementUltimateMath.NpcLeapSpeed;
                    Vec3 next = f.Flyer.Position + dir * (speed * dt);

                    // Never carried off the battlefield — hover at the boundary.
                    // LOCAL-VERIFY: Mission.IsPositionInsideBoundaries(Vec2) — if the
                    // signature has drifted, the catch below simply skips the clamp.
                    try
                    {
                        if (!Mission.Current.IsPositionInsideBoundaries(next.AsVec2))
                            next = f.Flyer.Position;
                    }
                    catch { }

                    // Terrain-following: ground height + the flight/landing curve.
                    float ground = next.z;
                    try
                    {
                        scene.GetHeightAtPoint(next.AsVec2,
                            BodyFlags.CommonCollisionExcludeFlagsForAgent, ref ground);
                    }
                    catch { }
                    next.z = ground + ElementUltimateMath.FlightHeightAt(f.Remaining);

                    // LOCAL-VERIFY (in-game): a per-tick TeleportToPosition is the
                    // established way to move an agent off-navmesh (mount bolts use
                    // it), but sustained airborne repositioning is NEW here — if the
                    // agent ragdolls, slides, or plays a falling animation the whole
                    // flight, the fix is to reposition on a coarser interval (e.g.
                    // accumulate and teleport every 0.1 s) or to zero the agent's
                    // movement input while aloft. The mechanic itself stays as is.
                    f.Flyer.TeleportToPosition(next);
                }
                catch { }

                // A trail of gusts marks the carried caster.
                f.VisualTimer -= dt;
                if (f.VisualTimer <= 0f)
                {
                    f.VisualTimer = 0.35f;
                    try { SpellEffects.SpawnNatureBurst(f.Flyer.Position, NatureElement.Wind, 0.5f); } catch { }
                    try { SpellEffects.BeginAgentGlow(f.Flyer,
                            f.Ashen ? ColorSchool.Ashen : ColorSchool.Nature, 0.6f); } catch { }
                }
            }
        }

        // ── EARTH — Heart of the Mountain / The Cairn-Shell ─────────────────────
        // Stone flows over the caster: MantleDamageReduction of every blow is
        // shrugged off (weapon hits healed back in OnAgentHit, magic reduced in
        // DamageAgent) — and the bearer moves at MantleSpeedMult, being briefly
        // made of mountain. No knockdown immunity: the engine owns knockdowns,
        // and the mantle does not reach that far (a deliberate simplification).
        private static void BeginMantle(Agent caster, bool ashen)
        {
            _mantles.RemoveAll(m => m.Bearer == caster);
            _mantles.Add(new Mantle { Bearer = caster, Remaining = ElementUltimateMath.MantleSeconds, Ashen = ashen });
            if (caster == Agent.Main)
                Msg("The stone takes you in — little can bite through, but the mountain walks slowly.");
            try { SpellEffects.SpawnNatureBurst(caster.Position, NatureElement.Earth, 1.5f); } catch { }
        }

        private static void TickMantles(float dt)
        {
            for (int i = _mantles.Count - 1; i >= 0; i--)
            {
                var m = _mantles[i];
                bool alive = false;
                try { alive = m.Bearer != null && m.Bearer.IsActive() && m.Bearer.Health > 0f; } catch { }
                m.Remaining -= dt;
                if (!alive || m.Remaining <= 0f)
                {
                    _mantles.RemoveAt(i);
                    if (alive && m.Bearer == Agent.Main) Msg("The stone releases you.");
                    if (alive) try { SpellEffects.SpawnNatureBurst(m.Bearer.Position, NatureElement.Earth, 1f); } catch { }
                    continue;
                }
                m.VisualTimer -= dt;
                if (m.VisualTimer <= 0f)
                {
                    m.VisualTimer = 0.8f;
                    // The slow is a short token re-applied each pulse, so it ends
                    // cleanly with the mantle (NatureEffects owns the restore).
                    try { NatureEffects.ApplySpeedToken(m.Bearer, ElementUltimateMath.MantleSpeedMult, 1.0f); } catch { }
                    try { SpellEffects.BeginAgentGlow(m.Bearer,
                            m.Ashen ? ColorSchool.Ashen : ColorSchool.Nature, 1.1f); } catch { }
                    try { SpellEffects.SpawnNatureBurst(m.Bearer.Position, NatureElement.Earth, 0.4f); } catch { }
                }
            }
        }

        // ── WATER — The Weeping Sky / The White Silence ──────────────────────────
        // A standing rain zone. IMPARTIAL, like the wall wards: horses mire and
        // strings soak on both sides — choosing WHEN to raise the sky is the
        // tactic. Only the Ashen blizzard picks a side: it gnaws at the morale of
        // the caster's FOES while it howls. There is only one sky: a new casting
        // tears the standing one down and replaces it.
        private static void BeginRain(Agent caster, bool ashen)
        {
            bool replaced = _rain != null;
            _rain = new RainZone
            {
                Centre = caster.Position,
                Remaining = ElementUltimateMath.RainSeconds,
                TickTimer = 0f,
                CasterTeam = caster.Team,
                Ashen = ashen,
            };
            if (replaced) Msg("A new will takes the sky — the old rain is torn away.");
            try { SpellEffects.SpawnNatureBurst(caster.Position, NatureElement.Water, 2f); } catch { }
        }

        private static void TickRain(float dt)
        {
            var r = _rain;
            if (r == null) return;
            r.Remaining -= dt;
            if (r.Remaining <= 0f)
            {
                _rain = null;
                Msg(r.Ashen ? "The White Silence lifts." : "The weeping sky clears.");
                return;
            }
            r.TickTimer -= dt;
            if (r.TickTimer > 0f) return;
            r.TickTimer = ElementUltimateMath.RainTickSeconds;

            float radius2 = ElementUltimateMath.RainRadius * ElementUltimateMath.RainRadius;
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (a == null || !a.IsActive() || a.IsMount) continue;
                    float dx = a.Position.x - r.Centre.x, dy = a.Position.y - r.Centre.y;
                    if (dx * dx + dy * dy > radius2) continue;

                    // The rain puts out every burning man inside it.
                    try { ElementSpellEffects.QuenchIgnition(a); } catch { }
                    // Slow tokens are short and re-applied each tick, so they end
                    // with the rain (or the moment someone walks out of it).
                    try { NatureEffects.ApplySpeedToken(a, ElementUltimateMath.RainFootSlowMult,
                            ElementUltimateMath.RainTickSeconds + 0.3f); } catch { }
                    try
                    {
                        if (a.MountAgent != null && a.MountAgent.IsActive())
                            NatureEffects.ApplySpeedToken(a.MountAgent, ElementUltimateMath.RainMountSlowMult,
                                ElementUltimateMath.RainTickSeconds + 0.3f);
                    }
                    catch { }
                    // The blizzard gnaws at the caster's foes.
                    if (r.Ashen)
                        try
                        {
                            if (r.CasterTeam != null && a.Team != null && a.Team.IsEnemyOf(r.CasterTeam))
                                a.SetMorale(a.GetMorale() - ElementUltimateMath.RainAshenMoraleDrainPerTick);
                        }
                        catch { }
                }
            }
            catch { }

            // Standing fire dies under the rain (its warding falls with it), and
            // a scatter of spray/snow keeps the zone readable on screen.
            try { ElementWallWards.QuenchFireNodesNear(r.Centre, ElementUltimateMath.RainRadius); } catch { }
            try
            {
                for (int k = 0; k < 4; k++)
                {
                    double ang = _rng.NextDouble() * Math.PI * 2.0;
                    float dist = (float)(_rng.NextDouble()) * ElementUltimateMath.RainRadius;
                    Vec3 p = r.Centre + new Vec3((float)Math.Cos(ang) * dist, (float)Math.Sin(ang) * dist, 0f);
                    if (r.Ashen) SpellEffects.SpawnTempSnowParticle(p + new Vec3(0f, 0f, 1.2f), 1.2f);
                    else         SpellEffects.SpawnNatureBurst(p, NatureElement.Water, 0.8f);
                }
            }
            catch { }
        }

        // ── SPIRIT — The Land's Answer / What Sleeps Beneath ─────────────────────
        // The battlefield sends ONE champion: an elemental shaped by the scene
        // (frost on snowfields, sand in the deserts, stone everywhere else),
        // spawned onto the caster's team, towering in health, gone when its time
        // runs out. The spawn path mirrors BattleEvents' proven mid-battle
        // reinforcement build (including the adult BodyProperties gotcha — an
        // AgentBuildData without them renders as an infant).
        private static void SummonChampion(Agent caster, bool ashen)
        {
            try
            {
                if (Mission.Current == null || caster.Team == null) return;

                var troop = MBObjectManager.Instance.GetObject<CharacterObject>("mountain_bandit")
                         ?? MBObjectManager.Instance.GetObject<CharacterObject>("looter");
                if (troop == null) return;

                bool snowy = false; try { snowy = SpellEffects.SceneIsSnowy(); } catch { }
                string sceneName = "";
                // LOCAL-VERIFY: Mission.SceneName — if the property has moved, the
                // catch leaves the name empty and the champion defaults to Stone.
                try { sceneName = (Mission.Current.SceneName ?? "").ToLowerInvariant(); } catch { }
                ElementalKind kind = ElementUltimateMath.ElementalKindForScene(snowy, sceneName);

                Vec3 fwd; try { fwd = caster.LookDirection; fwd.z = 0f; fwd.Normalize(); }
                catch { fwd = new Vec3(0f, 1f, 0f); }
                Vec3 pos = caster.Position + fwd * ElementUltimateMath.ElementalSpawnOffset;
                float gz = pos.z;
                try
                {
                    Mission.Current.Scene.GetHeightAtPoint(pos.AsVec2,
                        BodyFlags.CommonCollisionExcludeFlagsForAgent, ref gz);
                    pos.z = gz;
                }
                catch { }

                int seed = _rng.Next();
                Equipment equipment = troop.FirstBattleEquipment ?? troop.Equipment;
                BodyProperties body = troop.GetBodyProperties(equipment, seed);

                var origin    = new BasicBattleAgentOrigin(troop);
                var agentData = new AgentBuildData(origin)
                    .Team(caster.Team)
                    .Controller(AgentControllerType.AI)
                    .Equipment(equipment)
                    .BodyProperties(body)
                    .Age((int)body.Age)
                    .ClothingColor1(ChampionCloth(kind))
                    .ClothingColor2(ChampionCloth(kind))
                    .InitialPosition(in pos)
                    .InitialDirection(in fwd);

                var elemental = Mission.Current.SpawnAgent(agentData, false);
                if (elemental == null) return;

                // Towering health — the champion is worth several men.
                // LOCAL-VERIFY: Agent.HealthLimit setter. If it turns out read-only
                // in this game version, drop these two lines — the champion then
                // fights at troop health, weaker but perfectly functional.
                try
                {
                    elemental.HealthLimit = ElementUltimateMath.ElementalHealth;
                    elemental.Health      = ElementUltimateMath.ElementalHealth;
                }
                catch { }

                _champions.Add(new Champion
                {
                    Elemental = elemental, Remaining = ElementUltimateMath.ElementalSeconds,
                    Kind = kind, Ashen = ashen,
                });
                Msg($"The land answers — a {ElementUltimateMath.ElementalName(kind)} rises to fight beside " +
                    (caster == Agent.Main ? "you." : "its summoner."));
                EmitChampionBurst(pos, kind, ashen, 1.6f);
            }
            catch { }
        }

        private static uint ChampionCloth(ElementalKind kind)
        {
            switch (kind)
            {
                case ElementalKind.Frost: return 0xFFBFD9E8;  // pale ice
                case ElementalKind.Sand:  return 0xFFC9A96A;  // dune ochre
                default:                  return 0xFF6E6A64;  // old grey stone
            }
        }

        private static void EmitChampionBurst(Vec3 pos, ElementalKind kind, bool ashen, float scale)
        {
            try
            {
                switch (kind)
                {
                    case ElementalKind.Frost:
                        SpellEffects.SpawnTempSnowParticle(pos + new Vec3(0f, 0f, 0.5f), scale);
                        break;
                    case ElementalKind.Sand:
                        SpellEffects.SpawnTempSmokeParticle(pos + new Vec3(0f, 0f, 0.4f), scale);
                        break;
                    default:
                        SpellEffects.SpawnNatureBurst(pos, NatureElement.Earth, scale);
                        break;
                }
            }
            catch { }
            try { SpellEffects.SpawnTempLightRgb(pos + new Vec3(0f, 0f, 1f),
                    ElementSpellEffects.ElementLightRgb(MagicElement.Spirit, ashen), 8f, 0.8f); } catch { }
        }

        private static void TickChampions(float dt)
        {
            for (int i = _champions.Count - 1; i >= 0; i--)
            {
                var c = _champions[i];
                bool alive = false;
                try { alive = c.Elemental != null && c.Elemental.IsActive() && c.Elemental.Health > 0f; } catch { }
                if (!alive) { _champions.RemoveAt(i); continue; }

                c.Remaining -= dt;
                if (c.Remaining <= 0f)
                {
                    _champions.RemoveAt(i);
                    Vec3 at; try { at = c.Elemental.Position; } catch { at = default(Vec3); }
                    EmitChampionBurst(at, c.Kind, c.Ashen, 2f);
                    Msg($"The {ElementUltimateMath.ElementalName(c.Kind)} comes apart into the ground it rose from.");
                    // LOCAL-VERIFY: Agent.FadeOut(bool hideInstantly, bool hideMount)
                    // is the clean despawn (no corpse — it "comes apart"). If the
                    // signature has drifted, the fallback kill still removes it.
                    try { c.Elemental.FadeOut(true, true); }
                    catch { try { SpellEffects.KillAgent(c.Elemental); } catch { } }
                    continue;
                }

                c.VisualTimer -= dt;
                if (c.VisualTimer <= 0f)
                {
                    c.VisualTimer = 1.5f;
                    EmitChampionBurst(c.Elemental.Position, c.Kind, c.Ashen, 0.5f);
                    try { SpellEffects.BeginAgentGlow(c.Elemental,
                            c.Ashen ? ColorSchool.Ashen : ColorSchool.Nature, 1.8f); } catch { }
                }
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────
        private static IEnumerable<Agent> EnemiesNear(Agent caster, float radius)
        {
            float r2 = radius * radius;
            Vec3 pos; try { pos = caster.Position; } catch { yield break; }
            List<Agent> agents;
            try { agents = Mission.Current.Agents.ToList(); } catch { yield break; }
            foreach (Agent a in agents)
            {
                if (a == caster || !a.IsActive() || a.IsMount) continue;
                if (caster.Team != null && a.Team == caster.Team) continue;
                float dx = a.Position.x - pos.x, dy = a.Position.y - pos.y;
                if (dx * dx + dy * dy <= r2) yield return a;
            }
        }

        private static void AnnounceNpc(Agent agent, Hero hero, string blurb)
        {
            try
            {
                if (Agent.Main == null) return;
                if (agent.Team == Agent.Main.Team) return;   // no ally spam
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{hero.Name} {blurb}", new Color(0.9f, 0.35f, 0.25f)));
            }
            catch { }
        }

        private static void Msg(string text)
            => InformationManager.DisplayMessage(new InformationMessage(text, new Color(0.95f, 0.55f, 0.25f)));
    }
}
