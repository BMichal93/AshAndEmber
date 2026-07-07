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
//   Earth  — the Sundering: a radial earthquake around the caster (heavy
//            damage, foes hurled back, churned rubble left to bog the field).
//   Water  — a standing rain zone (quenches burns, halves fire, mires horses,
//            soaks bowstrings). Only ONE sky can stand — a recast replaces it.
//   Spirit — summons ONE terrain-shaped elemental to the caster's side.
//
// WIRING (all of it already done — listed so a fix knows where to look):
//   • MagicMissionBehavior.OnMissionTick   → Tick(dt)
//   • MagicMissionBehavior.OnAgentHit      → OnAgentHit(...)   (flight knock-out,
//     NPC windup interruption, rain archery damp)
//   • MagicMissionBehavior.OnEndMission and MainSubModule.OnGameStart
//                                          → ClearBattleState()
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
            // Who called it — gates the one-at-a-time cap PER SUMMONER, whether
            // raised by the Unbinding (terrain-shaped) or a Spirit fusion
            // (caller-chosen kind): the same caster may not stand two at once.
            public Agent Summoner;
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
            _rain = null;
            _champions.Clear();
            _npcWindups.Clear();
        }

        // =====================================================================
        // PLAYER ENTRY (called by ElementMagicInput on the Attack+Block chord)
        // =====================================================================

        // Most Unbindings are one-per-element-per-battle. The SPIRIT summon is the
        // exception: it is gated on whether the player's champion is still ALIVE, so
        // you may raise another only once the first has fallen (or its time ran out)
        // — never two at once. This stops the summon being spammed into an army.
        public static bool PlayerCanUnbind(MagicElement el)
            => el == MagicElement.Spirit ? !HasLiveChampionFor(Agent.Main) : !_playerUsed.Contains(el);

        // True while `summoner` already has a living champion on the field —
        // whether raised by the full Unbinding or a lesser Spirit fusion. Public:
        // ElementMagicInput and ColourLordAI both check this before offering a
        // Spirit-fusion summon, so a cast is never wasted on a refusal.
        public static bool HasLiveChampionFor(Agent summoner)
        {
            if (summoner == null) return false;
            for (int i = 0; i < _champions.Count; i++)
            {
                var c = _champions[i];
                if (c == null || c.Summoner != summoner) continue;
                try { if (c.Elemental != null && c.Elemental.IsActive() && c.Elemental.Health > 0f) return true; }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            return false;
        }

        // Executes the loaded element's Unbinding for the player. Returns true
        // when the working actually fired (the caller then applies the aging
        // cost); false leaves the drawn charge intact.
        public static bool CastPlayerUltimate(MagicElement el, Agent caster)
        {
            if (caster == null || !caster.IsActive()) return false;
            if (!PlayerCanUnbind(el)) return false;
            // The wind will not carry horse and rider — and teleporting a mounted
            // RIDER out of the saddle is the desync class this mod never risks.
            // Refused BEFORE the once-per-battle is spent; the charge stays drawn.
            if (el == MagicElement.Wind && IsMounted(caster))
            {
                Msg("The wind will not carry horse and rider — take wing on your own feet.");
                return false;
            }
            // Spirit is gated on its champion being alive (see PlayerCanUnbind), not
            // marked spent — so the player may raise a fresh one after this dies.
            if (el != MagicElement.Spirit) _playerUsed.Add(el);
            bool ashen = false; try { ashen = MageKnowledge.IsAshen; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
            if (hpPct < ElementUltimateMath.QuakeHpFrac && closeEnemies >= 1
                && known.Contains(MagicElement.Earth))
                pick = MagicElement.Earth;   // wounded and pressed → heave them off him
            else if (hpPct < ElementUltimateMath.LeapHpFrac
                && closeEnemies >= ElementUltimateMath.LeapCloseEnemies
                && known.Contains(MagicElement.Wind)
                && !IsMounted(agent))   // the leap teleports the caster — never a rider
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
            try { SpellEffects.BeginCastLoop(agent); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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

        private static bool IsMounted(Agent agent)
        {
            try { return agent?.MountAgent != null; } catch { return false; }
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
                    try { SpellEffects.SpawnNatureBurst(victim.Position, NatureElement.Wind, 0.8f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                    try { SpellEffects.EndCastLoop(victim); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    try { SpellEffects.SpawnTempSmokeParticle(victim.Position + new Vec3(0f, 0f, 0.8f), 1.5f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    if (Agent.Main != null && victim.Team != null && Agent.Main.Team != null
                        && victim.Team != Agent.Main.Team)
                        Msg($"The Unbinding is broken — {ElementUltimateMath.UltimateName(w.Element, w.Ashen)} dies unspoken.");
                }
            }

            // 3. Wet bowstrings: a RANGED hit loosed from inside the rain loses
            //    part of its bite (heal-back — OnAgentHit fires after damage lands).
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
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        // =====================================================================
        // MISSION TICK
        // =====================================================================

        public static void Tick(float dt)
        {
            if (Mission.Current == null) return;
            try { TickNpcWindups(dt); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { TickFlights(dt); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { TickRain(dt); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { TickChampions(dt); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void TickNpcWindups(float dt)
        {
            for (int i = _npcWindups.Count - 1; i >= 0; i--)
            {
                var w = _npcWindups[i];
                bool alive = false;
                try { alive = w.Caster != null && w.Caster.IsActive() && w.Caster.Health > 0f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }

                w.Remaining -= dt;
                if (w.Remaining > 0f) continue;
                _npcWindups.RemoveAt(i);
                try { SpellEffects.EndCastLoop(w.Caster); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { Execute(w.Element, w.Caster, w.Ashen); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                case MagicElement.Earth:  EarthquakeSunder(caster, ashen); break;
                case MagicElement.Water:  BeginRain(caster, ashen);       break;
                case MagicElement.Spirit: SummonChampion(caster, ashen);  break;
            }
            try { SpellEffects.TryCastSound(caster.Position,
                    ashen ? ColorSchool.Ashen : el == MagicElement.Fire ? ColorSchool.Red : ColorSchool.Nature); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { SpellEffects.RecordMagicCast(caster.Position); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { ElementWallWards.NoteCast(el, caster.Team); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                // The nova is fire like any other — a standing mist wall between
                // the caster and a foe drinks the working before it reaches him.
                try { if (ElementWallWards.BlocksPath(MagicElement.Fire, pos, a.Position, out _)) continue; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { SpellEffects.DamageAgent(a, ElementUltimateMath.NovaDamage * power, ColorSchool.Red, caster); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                // Full ignition on everything the nova touches (the Ashen cold
                // grips as deep frost instead of a burn — same dread, colder face).
                if (!ashen) try { ElementSpellEffects.IgniteTarget(a, caster, 1f * power, ashen); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                else        try { NatureEffects.ApplySpeedToken(a, ElementUltimateMath.NovaAshenSlowMult,
                                                                   ElementUltimateMath.NovaAshenSlowSec); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try
                {
                    if (ashen) SpellEffects.SpawnTempSnowParticle(a.Position + new Vec3(0f, 0f, 0.4f), 1.2f);
                    else       SpellEffects.SpawnTempFireParticle(a.Position + new Vec3(0f, 0f, 0.4f), 1.2f);
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }

            // The survivors visibly flee the eruption; timber in the ring chars.
            try { SpellEffects.ScatterEnemies(pos, ElementUltimateMath.NovaRadius, caster.Team); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { SpellEffects.DamageBurnableStructures(pos, ElementUltimateMath.NovaRadius,
                    ElementUltimateMath.NovaSiegeDamage * power, caster); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

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
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { ElementWallWards.RegisterNode(MagicElement.Fire, node, 1.6f,
                        ElementUltimateMath.NovaRingBurnSec, caster.Team); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                if (!ashen)
                    try { SpellEffects.SpawnFireWallPatches(node, tangent, 2.2f,
                            ElementUltimateMath.NovaRingBurnDps * power,
                            ElementUltimateMath.NovaRingBurnSec, caster.Team); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { SpellEffects.SpawnTempLightRgb(pos + new Vec3(0f, 0f, 1.5f), rgb, 22f, 1.4f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { SpellEffects.BeginAgentGlow(caster, ashen ? ColorSchool.Ashen : ColorSchool.Red, 2.5f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
            try { SpellEffects.SpawnNatureBurst(caster.Position, NatureElement.Wind, 1.5f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void TickFlights(float dt)
        {
            if (_flights.Count == 0) return;
            var scene = Mission.Current?.Scene;
            for (int i = _flights.Count - 1; i >= 0; i--)
            {
                var f = _flights[i];
                bool alive = false;
                try { alive = f.Flyer != null && f.Flyer.IsActive() && f.Flyer.Health > 0f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                    // Terrain-following: ground height + the flight/landing curve.
                    float ground = next.z;
                    try
                    {
                        scene.GetHeightAtPoint(next.AsVec2,
                            BodyFlags.CommonCollisionExcludeFlagsForAgent, ref ground);
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                // A trail of gusts marks the carried caster.
                f.VisualTimer -= dt;
                if (f.VisualTimer <= 0f)
                {
                    f.VisualTimer = 0.35f;
                    try { SpellEffects.SpawnNatureBurst(f.Flyer.Position, NatureElement.Wind, 0.5f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    try { SpellEffects.BeginAgentGlow(f.Flyer,
                            f.Ashen ? ColorSchool.Ashen : ColorSchool.Nature, 0.6f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
        }

        // ── EARTH — The Mountain's Wrath / The Barrow Wakes ─────────────────────
        // The Sundering: the ground erupts in a ring around the caster. Every foe
        // caught is struck hard and HURLED off his feet (mount-safe — the horse is
        // thrown, never the rider out of the saddle), left staggering on broken
        // footing, and the churned earth is left as rings of rubble that bog anyone
        // crossing them (impartial, like the mud a broken wave leaves). Wooden siege
        // engines and gates in the ring are shaken apart; stone walls stand.
        // Instantaneous — nothing to tick, no lingering buff on the caster.
        private static void EarthquakeSunder(Agent caster, bool ashen)
        {
            Vec3 pos; try { pos = caster.Position; } catch { return; }
            float radius = ElementUltimateMath.QuakeRadius;

            foreach (Agent a in EnemiesNear(caster, radius))
            {
                if (SpellEffects.IsWarded(a)) continue;
                try { SpellEffects.DamageAgent(a, ElementUltimateMath.QuakeDamage, ColorSchool.Nature, caster); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                // Hurled outward off the heaving ground — mount-safe knockback.
                Vec3 away = a.Position - pos; away.z = 0f;
                if (away.Length > 0.1f) away.Normalize(); else away = new Vec3(1f, 0f, 0f);
                Vec3 dest = a.Position + away * ElementUltimateMath.QuakeKnockback;
                dest.z = a.Position.z;
                try { NatureEffects.KnockbackAgent(a, dest); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                // Broken footing: a short slow (deep frost for the Ashen barrow-cold).
                try { NatureEffects.ApplySpeedToken(a, ElementUltimateMath.QuakeSlowMult,
                                                       ElementUltimateMath.QuakeSlowSec); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { SpellEffects.SpawnNatureBurst(a.Position,
                        ashen ? NatureElement.Water : NatureElement.Earth, 0.6f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }

            // The survivors scatter; wooden machines in the ring are shaken apart.
            try { SpellEffects.ScatterEnemies(pos, radius, caster.Team); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { SpellEffects.DamageBurnableStructures(pos, radius,
                    ElementUltimateMath.QuakeSiegeDamage, caster); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // A ring of churned rubble is left to bog the ground the quake tore up.
            int rubble = ElementUltimateMath.QuakeRubblePatches;
            for (int k = 0; k < rubble; k++)
            {
                double ang = (Math.PI * 2.0 / rubble) * k + _rng.NextDouble() * 0.4;
                Vec3 p = pos + new Vec3((float)Math.Cos(ang) * ElementUltimateMath.QuakeRubbleRing,
                                        (float)Math.Sin(ang) * ElementUltimateMath.QuakeRubbleRing, 0f);
                p.z = pos.z;
                try { SpellEffects.SpawnMudPatch(p); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }

            // The eruption itself — a stone shockwave ring and a heave at the centre.
            try { SpellEffects.SpawnNatureRing(pos, NatureElement.Earth, radius * 0.55f, 1.2f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { SpellEffects.SpawnNatureBurst(pos, NatureElement.Earth, 2f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try
            {
                Vec3 rgb = ElementSpellEffects.ElementLightRgb(MagicElement.Earth, ashen);
                SpellEffects.SpawnTempLightRgb(pos + new Vec3(0f, 0f, 1f), rgb, 18f, 1.1f);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { SpellEffects.BeginAgentGlow(caster, ashen ? ColorSchool.Ashen : ColorSchool.Nature, 1.5f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            if (caster == Agent.Main)
                Msg(ashen ? "The barrow wakes — the frozen ground splits, and the cold throws them down."
                          : "The mountain's wrath breaks loose — the earth heaves, and they are thrown like chaff.");
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
            try { SpellEffects.SpawnNatureBurst(caster.Position, NatureElement.Water, 2f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                    try { ElementSpellEffects.QuenchIgnition(a); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    // Slow tokens are short and re-applied each tick, so they end
                    // with the rain (or the moment someone walks out of it).
                    try { NatureEffects.ApplySpeedToken(a, ElementUltimateMath.RainFootSlowMult,
                            ElementUltimateMath.RainTickSeconds + 0.3f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    try
                    {
                        if (a.MountAgent != null && a.MountAgent.IsActive())
                            NatureEffects.ApplySpeedToken(a.MountAgent, ElementUltimateMath.RainMountSlowMult,
                                ElementUltimateMath.RainTickSeconds + 0.3f);
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    // The blizzard gnaws at the caster's foes.
                    if (r.Ashen)
                        try
                        {
                            if (r.CasterTeam != null && a.Team != null && a.Team.IsEnemyOf(r.CasterTeam))
                                a.SetMorale(a.GetMorale() - ElementUltimateMath.RainAshenMoraleDrainPerTick);
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Standing fire dies under the rain — the burning GROUND itself, not
            // just its warding (QuenchFireAt sweeps both, patches to steam) — and
            // a scatter of spray/snow keeps the zone readable on screen.
            try { SpellEffects.QuenchFireAt(r.Centre, ElementUltimateMath.RainRadius); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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

                bool snowy = false; try { snowy = SpellEffects.SceneIsSnowy(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                string sceneName = "";
                // LOCAL-VERIFY: Mission.SceneName — if the property has moved, the
                // catch leaves the name empty and the champion defaults to Stone.
                try { sceneName = (Mission.Current.SceneName ?? "").ToLowerInvariant(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                ElementalKind kind = ElementUltimateMath.ElementalKindForScene(snowy, sceneName);

                if (SpawnNamedChampion(caster, ashen, kind))
                    Msg($"The land answers — a {ElementUltimateMath.ElementalName(kind)} rises to fight beside " +
                        (caster == Agent.Main ? "you." : "its summoner."));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // =====================================================================
        // SPIRIT FUSION — a lesser, repeatable cousin of the Unbinding's summon.
        // Where the Unbinding calls a TERRAIN-shaped champion once per battle,
        // a Spirit fusion calls the living kinsman of whichever element it was
        // paired with — the same flat attack-form cost as any other cast, but
        // gated to one living kinsman per summoner at a time (the same slot the
        // Unbinding's champion uses), so it can never be spammed into a horde.
        // =====================================================================
        public static bool TryCastFusionSummon(ElementalKind kind, Agent caster, bool ashen)
        {
            if (caster == null || !caster.IsActive() || Mission.Current == null || caster.Team == null) return false;
            if (HasLiveChampionFor(caster))
            {
                if (caster == Agent.Main)
                    Msg($"{ElementUltimateMath.ElementalName(kind)} already walks the field beside you — the old bond must break before a new one answers.");
                return false;
            }
            bool spawned = false;
            try { spawned = SpawnNamedChampion(caster, ashen, kind); }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            if (spawned)
                Msg($"Spirit calls its kin — {ElementUltimateMath.ElementalName(kind)} rises at " +
                    (caster == Agent.Main ? "your side." : "its summoner's side."));
            return spawned;
        }

        // Shared spawn: builds the Kindled through the factory and registers its
        // lifespan. Returns false (no state changed) if the mission/team is gone
        // or the factory failed to spawn a body.
        private static bool SpawnNamedChampion(Agent caster, bool ashen, ElementalKind kind)
        {
            if (Mission.Current == null || caster.Team == null) return false;

            Vec3 fwd; try { fwd = caster.LookDirection; fwd.z = 0f; fwd.Normalize(); }
            catch { fwd = new Vec3(0f, 1f, 0f); }
            Vec3 pos = caster.Position + fwd * ElementUltimateMath.ElementalSpawnOffset;

            // The champion is just a Kindled sent to the caster's side — build it
            // through the shared factory so it looks, coats and buckles exactly
            // like every other elemental (its aura and weakness are handled
            // centrally by ElementalBeings). charge:true ropes an ENEMY lord's
            // summon into his line; on the player's side the factory leaves
            // battle orders be.
            Agent elemental = ElementalFactory.SpawnElemental(kind, caster.Team, pos, charge: true);
            if (elemental == null) return false;

            _champions.Add(new Champion
            {
                Elemental = elemental, Remaining = ElementUltimateMath.ElementalSeconds,
                Kind = kind, Ashen = ashen, Summoner = caster,
            });
            return true;
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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { SpellEffects.SpawnTempLightRgb(pos + new Vec3(0f, 0f, 1f),
                    ElementSpellEffects.ElementLightRgb(MagicElement.Spirit, ashen), 8f, 0.8f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void TickChampions(float dt)
        {
            for (int i = _champions.Count - 1; i >= 0; i--)
            {
                var c = _champions[i];
                bool alive = false;
                try { alive = c.Elemental != null && c.Elemental.IsActive() && c.Elemental.Health > 0f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                    catch { try { SpellEffects.KillAgent(c.Elemental); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } }
                    continue;
                }
                // The living coat (following particles + glow) is driven centrally
                // by ElementalBeings.TickAuras — the champion only owns its lifespan.
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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void Msg(string text)
            => InformationManager.DisplayMessage(new InformationMessage(text, new Color(0.95f, 0.55f, 0.25f)));
    }
}
