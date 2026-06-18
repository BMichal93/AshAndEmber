// =============================================================================
// LIFE & DEATH MAGIC — AI/ColourLordAI.cs
// NPC mage battle AI. Uses SpellEffects.ExecuteNpcBlast/ExecuteNpcBurst with
// pre-prepared SpellCast recipes. Impulsive lords cast more, Calculating less.
// Enchantment talents are applied automatically by ApplyEffectsToAgent.
// Tracks casts per battle for post-battle aging.
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
                catch { }
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

            bool isCalculating = false;
            bool isImpulsive   = false;
            try
            {
                int calc = hero.GetTraitLevel(DefaultTraits.Calculating);
                isCalculating = !isAshen && calc > 0;
                isImpulsive   = !isAshen && calc < 0;
            }
            catch { }

            // -1. Pyre Lord: plant a barrier wall once at battle start before attacking.
            if (IsPyreLord(hero) && !_pyreBarriersPlaced.Contains(hero.StringId) && nearEnemies >= 1)
            {
                CastBarrierWall(agent, hero);
                return;
            }

            // 0. Defensive burst when endangered — Ward is now a Restoration talent, not NPC-castable.
            //    False emperor responds with maximum devastation; Ashen with overwhelming force.
            if (hpPct < 0.40f && closeEnemies >= 1)
            {
                if (isFalseEmperor)
                    CastBurst(agent, hero, 5, 5, 0); // 12.5 m radius
                else if (isAshen)
                    CastBurst(agent, hero, 4, 4, 0); // 10 m radius
                else
                    CastBurst(agent, hero, 3, 3, 0); // 7.5 m radius
                return;
            }

            // 1. Heal self when badly hurt
            if (hpPct < 0.30f)
            {
                CastHealBurst(agent, hero);
                return;
            }

            // 2. Help hurt allies — Ashen don't bother, they only care about themselves
            if (!isAshen)
            {
                bool allyHurt = allies.Any(a => a.Health < a.HealthLimit * 0.5f
                                             && a.Position.Distance(agent.Position) <= 15f);
                if (allyHurt)
                {
                    CastHealBurst(agent, hero);
                    return;
                }
            }

            if (nearEnemies == 0 && !isAshen) return;

            // 3. Attack — evaluate both zones for friendly fire safety and target density
            int roll = isAshen ? _rng.Next(6) : _rng.Next(4);

            const float BlastDot = 0.65f;
            // Detection range and burst-check radius are scaled to match the larger
            // spell sizes below so the friendly-fire check covers the actual impact zone.
            float blastRange      = isAshen ? 10f  : 8f;   // was 8 / 6
            float burstCheckRange = isAshen ? 10f  : 7.5f; // was 5 (formCount=2); now formCount=3-4

            int coneEnemies   = SpellEffects.CountEnemiesInCone(agent, blastRange, BlastDot);
            int coneAllies    = SpellEffects.CountAlliesInCone(agent, blastRange, BlastDot);
            int radiusEnemies = enemies.Count(a => a.Position.Distance(agent.Position) < burstCheckRange);
            int radiusAllies  = SpellEffects.CountAlliesInRadius(agent, burstCheckRange);

            // A zone is "safe" when enemies outnumber allies in it.
            // Ashen lords accept equal counts (reckless); non-Ashen require a strict majority.
            bool blastSafe = coneEnemies >= 1
                && (coneAllies == 0 || (isAshen ? coneEnemies >= coneAllies : coneEnemies > coneAllies));
            bool burstSafe = radiusEnemies >= 1
                && (radiusAllies == 0 || (isAshen ? radiusEnemies >= radiusAllies : radiusEnemies > radiusAllies));

            // Surrounded — burst to clear space; all formCounts bumped +1 vs previous
            if (closeEnemies >= 3)
            {
                // False emperor: always maximum; Ashen: 4–5; non-Ashen: 3–4
                int form = isFalseEmperor ? 5
                         : isAshen ? (closeEnemies >= 5 || roll >= 3 ? 5 : 4)
                                   : (closeEnemies >= 5              ? 4 : 3);
                CastBurst(agent, hero, form, form, 0);
                return;
            }

            if (isFalseEmperor)
            {
                // False emperor: maximum power always — cold fire has no patience
                if (blastSafe && burstSafe)
                {
                    if (roll < 3) CastBlast(agent, hero, 5, 5, 0);
                    else          CastBurst(agent, hero, 5, 5, 0);
                }
                else if (blastSafe) CastBlast(agent, hero, 5, 5, 0);
                else if (burstSafe) CastBurst(agent, hero, 5, 5, 0);
                else
                {
                    // Cold fire doesn't wait for a clean target
                    if (roll < 3) CastBurst(agent, hero, 4, 5, 0);
                    else          CastBlast(agent, hero, 4, 5, 0);
                }
            }
            else if (isAshen)
            {
                // Ashen: aggressive, unpredictable — all spells one tier larger than before
                if (blastSafe && burstSafe)
                {
                    switch (roll)
                    {
                        case 0: CastBlast(agent, hero, coneEnemies >= 3 ? 4 : 3, coneEnemies >= 3 ? 4 : 3, 0); break;
                        case 1: CastBurst(agent, hero, radiusEnemies >= 3 ? 4 : 3, radiusEnemies >= 3 ? 4 : 3, 0); break;
                        case 2: CastBlast(agent, hero, 3, 3, 0); break;
                        case 3: CastBurst(agent, hero, 3, 3, 0); break;
                        case 4: CastBlast(agent, hero, 4, coneEnemies >= 3 ? 4 : 3, 0); break;
                        default: CastBurst(agent, hero, 3, radiusEnemies >= 3 ? 4 : 3, 0); break;
                    }
                }
                else if (blastSafe)
                {
                    int form = coneEnemies >= 3 ? 4 : 3;
                    CastBlast(agent, hero, form, form, 0);
                }
                else if (burstSafe)
                {
                    int form = radiusEnemies >= 3 ? 4 : 3;
                    CastBurst(agent, hero, form, form, 0);
                }
                else if (nearEnemies > 0)
                {
                    // Ashen never idle — harass even without a clean target
                    if (roll < 3) CastBurst(agent, hero, 3, 3, 0);
                    else          CastBlast(agent, hero, 3, 3, 0);
                }
            }
            else if (isCalculating)
            {
                // Patient: prefers area bursts when enemies cluster; holds fire for clean shots
                if (burstSafe && radiusEnemies >= 2)
                    CastBurst(agent, hero, 3, 3, 0);
                else if (blastSafe && coneEnemies >= 2)
                    CastBlast(agent, hero, 3, 3, 0);
                else if (blastSafe)
                    CastBlast(agent, hero, 3, 3, 0);
                else if (burstSafe)
                    CastBurst(agent, hero, 3, 2, 0);
                // else: no quality opportunity this tick — wait
            }
            else if (isImpulsive)
            {
                // Direct: fires as soon as any safe forward target appears
                if (blastSafe)
                    CastBlast(agent, hero, 3, 3, 0);
                else if (burstSafe)
                    CastBurst(agent, hero, 3, 3, 0);
                // else: no safe target this tick
            }
            else
            {
                // Default: balanced mix, avoids friendly fire
                if (blastSafe && (roll <= 2 || !burstSafe))
                    CastBlast(agent, hero, 3, 3, 0);
                else if (burstSafe)
                    CastBurst(agent, hero, 3, 3, 0);
                else if (blastSafe)
                    CastBlast(agent, hero, 3, 3, 0);
                else if (nearEnemies > 0)
                    CastBurst(agent, hero, 3, 2, 0); // light pressure while repositioning
            }
        }

        // Pyre Lords: highly calculating (Calculating >= 2) non-Ashen lords who prefer to
        // fortify with a barrier wall before attacking from behind it.
        private static bool IsPyreLord(Hero hero)
        {
            if (hero == null || ColourLordRegistry.IsAshenLord(hero)) return false;
            try { return hero.GetTraitLevel(DefaultTraits.Calculating) >= 2; } catch { return false; }
        }

        private static void CastBarrierWall(Agent agent, Hero hero)
        {
            try
            {
                AnnounceEnemyCast(agent, hero, "raises a wall of fire.");
                SetCooldown(hero);
                RecordCast(hero, 6); // 3 nodes + 3 damage inputs
                _pyreBarriersPlaced.Add(hero.StringId);
                SpellEffects.QueueNpcCastWithWindup(agent, () =>
                {
                    SpellEffects.ExecuteNpcBarrier(agent, 3, 3, 0, agent.Team);
                    ApplyCastVisuals(agent);
                });
            }
            catch { }
        }

        private static void CastBlast(Agent agent, Hero hero, int formCount, int dmg, int restore)
        {
            try
            {
                bool isAshen = ColourLordRegistry.IsAshenLord(hero);
                string blurb = formCount >= 4
                    ? (isAshen ? "cold fire tears forward." : "channels a devastating blast.")
                    : (isAshen ? "cold fire lashes out." : "shapes fire into a forward blade.");
                AnnounceEnemyCast(agent, hero, blurb);
                SetCooldown(hero);
                RecordCast(hero, formCount + dmg + restore);
                SpellEffects.QueueNpcCastWithWindup(agent, () =>
                {
                    SpellEffects.ExecuteNpcBlast(agent, formCount, dmg, restore, agent.Team);
                    ApplyCastVisuals(agent);
                });
            }
            catch { }
        }

        private static void CastBurst(Agent agent, Hero hero, int formCount, int dmg, int restore)
        {
            try
            {
                bool isAshen = ColourLordRegistry.IsAshenLord(hero);
                string blurb = formCount >= 4
                    ? (isAshen ? "tears the veil — cold fire erupts." : "channels a great eruption.")
                    : (isAshen ? "erupts with cold fire." : "fire bursts outward.");
                AnnounceEnemyCast(agent, hero, blurb);
                SetCooldown(hero);
                RecordCast(hero, formCount + dmg + restore);
                SpellEffects.QueueNpcCastWithWindup(agent, () =>
                {
                    SpellEffects.ExecuteNpcBurst(agent, formCount, dmg, restore, agent.Team);
                    ApplyCastVisuals(agent);
                });
            }
            catch { }
        }

        private static void CastHealBurst(Agent agent, Hero hero)
        {
            try
            {
                AnnounceEnemyCast(agent, hero, "turns the fire inward — wounds close.");
                SetCooldown(hero);
                RecordCast(hero, 5);
                // 3 Restore inputs: meets the Rouse enchantment threshold (>= 3)
                // so lords who own Rouse can summon allies when healing.
                SpellEffects.QueueNpcCastWithWindup(agent, () =>
                {
                    SpellEffects.ExecuteNpcBurst(agent, 2, 0, 3, agent.Team);
                    ApplyCastVisuals(agent);
                });
            }
            catch { }
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
                _cooldowns[hero.StringId] = cd;
            }
            catch { }
        }

        // Accumulates aging COST (not raw inputs) so NPC lords pay the same geometric
        // rate as the player: ComputeBattleAgingCost(totalInputs) days per spell cast.
        private static void RecordCast(Hero hero, int totalInputs = 1)
        {
            if (!_battleCasts.ContainsKey(hero.StringId))
                _battleCasts[hero.StringId] = 0;
            _battleCasts[hero.StringId] += AgingSystem.ComputeBattleAgingCost(totalInputs, false);
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
            catch { }
        }
    }
}
