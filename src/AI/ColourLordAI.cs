// =============================================================================
// LIFE & DEATH MAGIC — AI/ColourLordAI.cs
// NPC mage battle AI. Casts through the unified element system
// (ElementSpellEffects.CastAttack/CastWall) exactly as the player does — Fire and
// the learned Wind/Earth/Water/Spirit all on one path. A lord reads the tactical
// situation and throws the element that FITS it (gale when surrounded, root/wave
// against a charge, a fire cone at a lone target), at a POWER and CADENCE set by
// his remaining life expectancy and temperament (NpcCastPlanner): young/impulsive
// lords spend big and fast, old/calculating lords hoard their years. Ashen and the
// False Emperor pay no life and cast at boss power. NPC lords wield PURE element
// magic exactly like the player — no retired enchantment brands. Tracks casts
// per battle for post-battle life-expectancy spend.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static class ColourLordAI
    {
        private const float DefaultCooldown     = 25f;
        private const float ImpulsiveCooldown   = 15f;
        private const float CalculatingCooldown = 35f;
        private const float AshenCooldown       = 6f;  // Ashen lords cast ~4× more often
        // 6s matches the Ashen cadence. At the old 3s a single False Emperor
        // cast ~100 times in a 5-minute battle at max recipe — unanswerable.
        private const float FalseEmperorCooldown = 6f;

        private static readonly Dictionary<string, float> _cooldowns   = new Dictionary<string, float>();
        private static readonly Dictionary<string, int>   _battleCasts = new Dictionary<string, int>();
        // Tracks Pyre Lords who have already planted a barrier this battle.
        private static readonly HashSet<string> _pyreBarriersPlaced = new HashSet<string>();
        private static readonly Random _rng = new Random();

        private static float _tickAccum   = 0f;
        private const  float TickInterval = 0.5f;
        private static bool  _warmupDone  = false;
        private static float _warmupTimer = 0f;
        private const  float WarmupDuration = 12f;

        public static void ClearCooldowns()
        {
            _cooldowns.Clear();
            _pyreBarriersPlaced.Clear();
            // _battleCasts is NOT cleared here — OnMapEventEnded consumes it after
            // the battle via ApplyNpcBattleAging, which fires after OnMissionEnded.
            // Call FlushBattleCasts() after aging is processed.
            _tickAccum   = 0f;
            _warmupDone  = false;
            _warmupTimer = 0f;
        }

        /// Called from CampaignBehavior.OnMapEventEnded after aging is applied,
        /// to discard any casts that weren't consumed (e.g. NPC not in this event).
        public static void FlushBattleCasts() => _battleCasts.Clear();

        // Returns how many spells this hero cast in the last battle, then resets the counter.
        public static int ConsumeBattleCasts(Hero hero)
        {
            if (hero == null || !_battleCasts.TryGetValue(hero.StringId, out int count)) return 0;
            _battleCasts.Remove(hero.StringId);
            return count;
        }

        public static void MissionTick(float dt)
        {
            if (Mission.Current == null) return;
            _tickAccum += dt;
            if (_tickAccum < TickInterval) return;
            _tickAccum = 0f;

            if (!Mission.Current.AllowAiTicking) return;
            if (!SpellEffects.IsBattleMission()) return;

            // Tick down cooldowns
            foreach (string key in _cooldowns.Keys.ToList())
            {
                _cooldowns[key] -= TickInterval;
                if (_cooldowns[key] <= 0f) _cooldowns.Remove(key);
            }

            // Warmup — NPCs wait before their first cast
            if (!_warmupDone)
            {
                _warmupTimer += TickInterval;
                if (_warmupTimer < WarmupDuration) return;
                _warmupDone = true;

                // Stagger first casts: assign each eligible lord a random initial cooldown
                // (0–half of their usual cooldown) so they don't all fire simultaneously.
                try
                {
                    foreach (Agent a in Mission.Current.Agents.ToList())
                    {
                        if (!a.IsActive() || a.IsMount || !a.IsHero || a == Agent.Main) continue;
                        Hero h = (a.Character as CharacterObject)?.HeroObject;
                        if (h == null || !ColourLordRegistry.IsColourLord(h)) continue;
                        bool ashen = ColourLordRegistry.IsAshenLord(h);
                        bool isFE  = ashen && BurningLabQuestSystem.IsArenicosHero(h) && !BurningLabQuestSystem.ArenicosIsTrue;
                        float maxJitter = isFE ? FalseEmperorCooldown * 2f : ashen ? AshenCooldown * 2f : DefaultCooldown * 0.6f;
                        float jitter = (float)_rng.NextDouble() * maxJitter;
                        if (jitter > 0f) _cooldowns[h.StringId] = jitter;
                    }
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }

            List<Agent> agents;
            try { agents = Mission.Current.Agents.ToList(); }
            catch { return; }

            foreach (Agent agent in agents)
            {
                if (!agent.IsActive() || agent.IsMount || !agent.IsHero) continue;
                if (agent == Agent.Main) continue;

                Hero hero = (agent.Character as CharacterObject)?.HeroObject;
                if (hero == null || !ColourLordRegistry.IsColourLord(hero)) continue;
                if (_cooldowns.ContainsKey(hero.StringId)) continue;

                TryCast(agent, hero);
            }
        }

        // The tactical picture a lord reads before choosing what to throw.
        private enum Situation { Surrounded, ChargeBreak, Cluster, Harass }

        private static void TryCast(Agent agent, Hero hero)
        {
            if (Mission.Current == null) return;
            SpellEffects.TryFreeHandForCast(agent); // sheathe visually before cast, never blocks

            bool isAshen = ColourLordRegistry.IsAshenLord(hero);
            bool isFalseEmperor = isAshen && BurningLabQuestSystem.IsArenicosHero(hero) && !BurningLabQuestSystem.ArenicosIsTrue;

            var enemies = SpellEffects.EnemiesOf(agent);
            var allies  = SpellEffects.AlliesOf(agent);

            if (!isAshen && enemies.Count == 0 && allies.All(a => a.Health >= a.HealthLimit * 0.9f)) return;

            float hpPct      = agent.Health / Math.Max(agent.HealthLimit, 1f);
            int closeEnemies = enemies.Count(a => a.Position.Distance(agent.Position) < 8f);
            int nearEnemies  = enemies.Count(a => a.Position.Distance(agent.Position) < 20f);
            int mountedNear  = enemies.Count(a =>
            {
                try { return a.MountAgent != null && a.MountAgent.IsActive()
                          && a.Position.Distance(agent.Position) < 25f; }
                catch { return false; }
            });

            // Resource + temperament. Casting spends LIFE EXPECTANCY, so a lord's
            // remaining years are his real reserve: he pours out power freely while
            // young and hoards it near burnout, weighted by temperament. The Ashen
            // pay nothing — they always read as "full reserve" and cast at boss power.
            CasterTemper temper = ColourLordRegistry.TemperOf(hero);
            float lifeFrac = isAshen ? 1f
                : NpcCastPlanner.LifeFrac(ColourLordRegistry.LifeBudgetYears(hero));

            // -2. THE UNBINDING — a lord's once-per-battle ultimate. Only in
            //     battles worth the working (70+ men), read from the same tactical
            //     picture as his normal casts, and always behind a LONG telegraphed
            //     windup: any hit on him during the channel breaks it (and it stays
            //     spent). ElementUltimates owns the decision, the channel, and the
            //     interruption; this just pays the cooldown and the life-cost.
            try
            {
                if (ElementUltimates.TryQueueNpcUltimate(agent, hero, hpPct,
                        closeEnemies, nearEnemies, mountedNear, isAshen, KnownElements(hero), temper))
                {
                    SetCooldown(hero);
                    RecordUltimate(hero);
                    return;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // -1. Pyre Lord: fortify with a wall once before attacking.
            if (IsPyreLord(hero) && !_pyreBarriersPlaced.Contains(hero.StringId) && nearEnemies >= 1)
            {
                CastElementWall(agent, hero, isAshen, isFalseEmperor, temper, lifeFrac);
                return;
            }

            // 0. Desperate — hurt and pressed. Survival trumps thrift: full power,
            //    clear the space around him with whatever answers a surrounding.
            if (hpPct < 0.40f && closeEnemies >= 1)
            {
                CastElementAttack(agent, hero, Situation.Surrounded, isAshen, isFalseEmperor,
                    temper, lifeFrac, forwardSafe: true, aroundSafe: true, emergency: true);
                return;
            }

            // 1. Heal self when badly hurt.
            if (hpPct < 0.30f) { CastHeal(agent, hero, isAshen, temper, lifeFrac); return; }

            // 2. Help hurt allies — the Ashen care only for themselves.
            if (!isAshen)
            {
                bool allyHurt = allies.Any(a => a.Health < a.HealthLimit * 0.5f
                                             && a.Position.Distance(agent.Position) <= 15f);
                if (allyHurt) { CastHeal(agent, hero, isAshen, temper, lifeFrac); return; }
            }

            if (nearEnemies == 0 && !isAshen) return;

            // 3. Attack — read the situation, then throw the element that fits it and
            //    the geometry: root a crowd, break a charge, or lance a lone forward
            //    target, instead of throwing the same working every time.
            float blastRange      = isAshen ? 10f  : 8f;
            float burstCheckRange = isAshen ? 10f  : 7.5f;
            const float BlastDot  = 0.65f;

            int coneEnemies   = SpellEffects.CountEnemiesInCone(agent, blastRange, BlastDot);
            int coneAllies    = SpellEffects.CountAlliesInCone(agent, blastRange, BlastDot);
            int radiusEnemies = enemies.Count(a => a.Position.Distance(agent.Position) < burstCheckRange);
            int radiusAllies  = SpellEffects.CountAlliesInRadius(agent, burstCheckRange);

            // A forward (cone) or all-around (radius) working is "safe" when enemies
            // outnumber friends in its footprint. Ashen accept an even trade.
            bool forwardSafe = coneEnemies >= 1
                && (coneAllies == 0 || (isAshen ? coneEnemies >= coneAllies : coneEnemies > coneAllies));
            bool aroundSafe = radiusEnemies >= 1
                && (radiusAllies == 0 || (isAshen ? radiusEnemies >= radiusAllies : radiusEnemies > radiusAllies));

            Situation sit;
            if (closeEnemies >= 3)                           sit = Situation.Surrounded;
            else if (mountedNear >= 2)                        sit = Situation.ChargeBreak;
            else if (coneEnemies >= 2 || radiusEnemies >= 2) sit = Situation.Cluster;
            else                                             sit = Situation.Harass;

            CastElementAttack(agent, hero, sit, isAshen, isFalseEmperor, temper, lifeFrac,
                forwardSafe, aroundSafe, emergency: sit == Situation.Surrounded);
        }

        // Pyre Lords: highly calculating (Calculating >= 2) non-Ashen lords who prefer to
        // fortify with a barrier wall before attacking from behind it.
        private static bool IsPyreLord(Hero hero)
        {
            if (hero == null || ColourLordRegistry.IsAshenLord(hero)) return false;
            try { return hero.GetTraitLevel(DefaultTraits.Calculating) >= 2; } catch { return false; }
        }

        // ── Unified elemental kit for NPC mage lords ─────────────────────────────
        // A lord's learned repertoire lives on ColourLordRegistry.KnownElements (the
        // canonical source, shared with the campaign-map AI). When he casts he does
        // NOT pick at random: he reads the situation and throws the element that fits
        // it (see CastElementAttack/Preference), so a well-studied lord roots a crowd
        // with stone or sweeps it with gale where a novice only burns. Fire and every
        // other element run through the same unified path as the player — no old
        // fire-path spells or brands. The Ashen and the false emperor know them all
        // and cast at boss power; the element kit applies the Ashen cold mask itself.
        private static System.Collections.Generic.List<MagicElement> KnownElements(Hero hero)
            => ColourLordRegistry.KnownElements(hero);

        private static string ElementBlurb(MagicElement el, CastForm form)
        {
            if (form == CastForm.Wall)
            {
                switch (el)
                {
                    case MagicElement.Wind:   return "raises a wall of wind.";
                    case MagicElement.Earth:  return "raises a wall of stone.";
                    case MagicElement.Water:  return "raises a barrier of mist.";
                    case MagicElement.Spirit: return "raises a wall that heartens its own.";
                    default:                  return "raises a wall of fire.";
                }
            }
            switch (el)
            {
                case MagicElement.Wind:   return "looses a blast of wind.";
                case MagicElement.Earth:  return "tears the earth upward.";
                case MagicElement.Water:  return "hurls a slowing wave.";
                case MagicElement.Spirit: return "strikes the mind with cold dread.";
                default:                  return "shapes fire into a forward blade.";
            }
        }

        // ── Tactical element selection ───────────────────────────────────────────
        // Each situation has an ordered preference of elements. Fire and Water are
        // forward cones (best on a target ahead); Wind and Earth strike all around
        // (best when surrounded); Water and Earth break a cavalry charge (knockback /
        // root); Spirit is pure control and never endangers friends. A lord throws
        // the first element he KNOWS that fits — Fire is always known as a fallback.
        private static readonly MagicElement[] _prefSurrounded =
            { MagicElement.Wind, MagicElement.Earth, MagicElement.Spirit, MagicElement.Water, MagicElement.Fire };
        private static readonly MagicElement[] _prefChargeBreak =
            { MagicElement.Water, MagicElement.Earth, MagicElement.Wind, MagicElement.Fire, MagicElement.Spirit };
        private static readonly MagicElement[] _prefCluster =
            { MagicElement.Fire, MagicElement.Water, MagicElement.Earth, MagicElement.Wind, MagicElement.Spirit };
        private static readonly MagicElement[] _prefHarass =
            { MagicElement.Fire, MagicElement.Spirit, MagicElement.Wind, MagicElement.Water, MagicElement.Earth };

        private static MagicElement[] Preference(Situation sit)
        {
            switch (sit)
            {
                case Situation.Surrounded:  return _prefSurrounded;
                case Situation.ChargeBreak: return _prefChargeBreak;
                case Situation.Cluster:     return _prefCluster;
                default:                    return _prefHarass;
            }
        }

        // Whether an element's shape can fire without hitting friends in this geometry.
        private static bool ElementFits(MagicElement el, bool forwardSafe, bool aroundSafe)
        {
            switch (el)
            {
                case MagicElement.Fire:
                case MagicElement.Water:  return forwardSafe;  // forward cones
                case MagicElement.Wind:
                case MagicElement.Earth:  return aroundSafe;   // strike all around
                default:                  return true;         // Spirit — control only, 0 damage
            }
        }

        private static void CastElementAttack(Agent agent, Hero hero, Situation sit,
            bool isAshen, bool isFalseEmperor, CasterTemper temper, float lifeFrac,
            bool forwardSafe, bool aroundSafe, bool emergency)
        {
            try
            {
                var known = KnownElements(hero);
                // Ashen, impulsive lords, and anyone fighting for their life will throw
                // even without a clean lane; patient lords hold fire until it's safe.
                bool reckless = isAshen || emergency || temper == CasterTemper.Impulsive;

                // A lord does not throw a cone into a wall that drinks it: probe the
                // forward lane against the standing wards (fire dies on mist, the
                // wave breaks on stone). Recklessness is courage, not blindness —
                // every temper respects physics. Radial workings (Wind/Earth) are
                // warded per-target, so they need no lane probe.
                Vec3 fwdProbe = default(Vec3);
                bool probeOk = false;
                try
                {
                    Vec3 fwd = agent.LookDirection; fwd.z = 0f; fwd.Normalize();
                    fwdProbe = agent.Position + fwd * 8f;
                    probeOk = true;
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                MagicElement? pick = null;
                foreach (var el in Preference(sit))
                {
                    if (!known.Contains(el)) continue;
                    if (probeOk && (el == MagicElement.Fire || el == MagicElement.Water)
                        && ElementWallWards.IsPathWarded(el, agent.Position, fwdProbe)) continue;
                    if (reckless || ElementFits(el, forwardSafe, aroundSafe)) { pick = el; break; }
                }
                if (pick == null)
                {
                    if (!reckless) return;      // no safe opportunity — wait, keep the cooldown
                    pick = known[0];            // Fire — reckless casters loose it anyway
                }
                MagicElement chosen = pick.Value;

                float situationBase = emergency ? NpcCastPlanner.BaseDesperate
                                    : sit == Situation.Harass ? NpcCastPlanner.BaseHarass
                                    : NpcCastPlanner.BaseCluster;
                float power = isAshen
                    ? (isFalseEmperor ? 1.2f : 1.0f)   // boss tier overcharges, pays no life
                    : NpcCastPlanner.CastPower(situationBase, lifeFrac, temper, emergency);

                AnnounceEnemyCast(agent, hero, ElementBlurb(chosen, CastForm.Attack));
                SetCooldown(hero);
                RecordCast(hero, CastForm.Attack);
                SpellEffects.QueueNpcCastWithWindup(agent, () =>
                {
                    ElementSpellEffects.CastAttack(chosen, agent, power);
                    PlayCastFx(agent, chosen, isAshen);
                });
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Pyre Lord opener — a wall. A lord who has SEEN what the enemy throws
        // raises the wall that ANSWERS it (water against fire, wind against
        // stone…) if he has learned that element; otherwise he prefers stone
        // (roots) or fire (burns).
        private static void CastElementWall(Agent agent, Hero hero,
            bool isAshen, bool isFalseEmperor, CasterTemper temper, float lifeFrac)
        {
            try
            {
                _pyreBarriersPlaced.Add(hero.StringId);
                var known = KnownElements(hero);
                MagicElement chosen = MagicElement.Fire;
                MagicElement? counter = null;
                try
                {
                    var seen = ElementWallWards.LastHostileElement(agent.Team);
                    if (seen != null) counter = WallWardMath.CounterWallFor(seen.Value);
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                if (counter != null && known.Contains(counter.Value))
                    chosen = counter.Value;
                else
                    foreach (var el in new[] { MagicElement.Earth, MagicElement.Fire,
                                               MagicElement.Water, MagicElement.Wind, MagicElement.Spirit })
                        if (known.Contains(el)) { chosen = el; break; }

                float power = isAshen ? (isFalseEmperor ? 1.2f : 1.0f)
                    : NpcCastPlanner.CastPower(NpcCastPlanner.BaseCluster, lifeFrac, temper, emergency: false);

                AnnounceEnemyCast(agent, hero, ElementBlurb(chosen, CastForm.Wall));
                SetCooldown(hero);
                RecordCast(hero, CastForm.Wall);
                SpellEffects.QueueNpcCastWithWindup(agent, () =>
                {
                    ElementSpellEffects.CastWall(chosen, agent, power);
                    PlayCastFx(agent, chosen, isAshen);
                });
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Self / ally heal. A lord who knows Spirit raises the warding wall (heals
        // allies + self, lifts morale); otherwise he turns the fire inward — the one
        // working still routed through the old path, because it carries the
        // restorative brands (Ashveil / Hearthlight / Reflect) and lets a fire-only
        // lord mend himself at all.
        private static void CastHeal(Agent agent, Hero hero, bool isAshen, CasterTemper temper, float lifeFrac)
        {
            if (KnownElements(hero).Contains(MagicElement.Spirit))
            {
                try
                {
                    // Mending is survival — pour it out (emergency floor on the power).
                    float power = isAshen ? 1.0f
                        : NpcCastPlanner.CastPower(NpcCastPlanner.BaseCluster, lifeFrac, temper, emergency: true);
                    AnnounceEnemyCast(agent, hero, "raises a ward — the wounded are mended.");
                    SetCooldown(hero);
                    RecordCast(hero, CastForm.Wall);
                    SpellEffects.QueueNpcCastWithWindup(agent, () =>
                    {
                        ElementSpellEffects.CastWall(MagicElement.Spirit, agent, power);
                        PlayCastFx(agent, MagicElement.Spirit, isAshen);
                    });
                    return;
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            CastHealBurst(agent, hero);
        }

        // Cast sound + gesture for a unified element working. The element effects
        // draw their own light and the caster's glow; this adds the audible cast.
        private static void PlayCastFx(Agent agent, MagicElement el, bool isAshen)
        {
            ColorSchool sfx = isAshen ? ColorSchool.Ashen
                            : el == MagicElement.Fire ? ColorSchool.Red : ColorSchool.Nature;
            try { SpellEffects.TryCastSound(agent.Position, sfx); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { SpellEffects.TryCastAnimation(agent); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { SpellEffects.RecordMagicCast(agent.Position); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Turn the fire inward — an element-agnostic self-mend for a lord who has not
        // learned the Spirit ward. A DIRECT heal (no SpellCast), so it carries none of
        // the retired enchantment brands: the same pure magic the player wields.
        private static void CastHealBurst(Agent agent, Hero hero)
        {
            try
            {
                AnnounceEnemyCast(agent, hero, "turns the fire inward — wounds close.");
                SetCooldown(hero);
                RecordCast(hero, CastForm.Attack);
                SpellEffects.QueueNpcCastWithWindup(agent, () =>
                {
                    try { SpellEffects.HealAgent(agent, HealthCap(agent) * 0.35f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    foreach (Agent ally in SpellEffects.AlliesOf(agent))
                    {
                        if (ally == agent || !ally.IsActive()) continue;
                        if (ally.Position.Distance(agent.Position) > 8f) continue;
                        try { SpellEffects.HealAgent(ally, HealthCap(ally) * 0.15f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    }
                    ApplyCastVisuals(agent);
                });
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static float HealthCap(Agent a)
        {
            try { return a.HealthLimit > 0f ? a.HealthLimit : 100f; } catch { return 100f; }
        }

        private static void ApplyCastVisuals(Agent agent)
        {
            Hero hero = (agent.Character as CharacterObject)?.HeroObject;
            bool isAshen = hero != null && ColourLordRegistry.IsAshenLord(hero);
            ColorSchool school = isAshen ? ColorSchool.Ashen : ColorSchool.Purple;
            SpellEffects.BeginAgentGlow(agent, school, 3f);
            SpellEffects.TryCastSound(agent.Position, school);
            SpellEffects.TryCastAnimation(agent);
            SpellEffects.RecordMagicCast(agent.Position);
        }

        private static void SetCooldown(Hero hero)
        {
            try
            {
                if (ColourLordRegistry.IsAshenLord(hero))
                {
                    bool isFE = BurningLabQuestSystem.IsArenicosHero(hero) && !BurningLabQuestSystem.ArenicosIsTrue;
                    _cooldowns[hero.StringId] = isFE ? FalseEmperorCooldown : AshenCooldown;
                    return;
                }
                float cd = DefaultCooldown;
                int calc = hero.GetTraitLevel(DefaultTraits.Calculating);
                if (calc < 0) cd = ImpulsiveCooldown;
                else if (calc > 0) cd = CalculatingCooldown;
                // Near-burnout lords stretch their cadence to hoard their remaining
                // years — a calculating lord goes quiet, an impulsive one hardly slows.
                CasterTemper temper = ColourLordRegistry.TemperOf(hero);
                float lifeFrac = NpcCastPlanner.LifeFrac(ColourLordRegistry.LifeBudgetYears(hero));
                cd *= NpcCastPlanner.CooldownMult(lifeFrac, temper);
                _cooldowns[hero.StringId] = cd;
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Accumulates the life-expectancy COST of a cast so NPC lords pay the same
        // FLAT rate as the player: ElementMagicMath.CastAgingDays(form) — 3 days for
        // an attack, 4 for a wall (drawn out longer for power, never a cheaper cast).
        // NPC lords do not know the Nature discipline, so its discount never applies.
        private static void RecordCast(Hero hero, CastForm form)
        {
            if (!_battleCasts.ContainsKey(hero.StringId))
                _battleCasts[hero.StringId] = 0;
            _battleCasts[hero.StringId] += ElementMagicMath.CastAgingDays(form, hasNature: false);
        }

        // The Unbinding costs a lord the same flat toll the player pays (12 days;
        // the Ashen never spend life, exactly as with every other recorded cast).
        private static void RecordUltimate(Hero hero)
        {
            if (!_battleCasts.ContainsKey(hero.StringId))
                _battleCasts[hero.StringId] = 0;
            _battleCasts[hero.StringId] += ElementUltimateMath.UltimateAgingDays(hasNature: false);
        }

        // Shows a combat-log message when an NPC lord casts against the player.
        // Silent when the caster is on the player's side (no ally spam).
        private static void AnnounceEnemyCast(Agent agent, Hero hero, string blurb)
        {
            try
            {
                if (Agent.Main == null) return;
                if (agent.Team == Agent.Main.Team) return;
                bool isAshen = ColourLordRegistry.IsAshenLord(hero);
                Color c = isAshen
                    ? new Color(0.38f, 0.50f, 0.75f)   // cold blue for Ashen
                    : new Color(0.65f, 0.45f, 0.75f);   // violet for colour lords
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{hero.Name} — {blurb}", c));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }
    }
}
