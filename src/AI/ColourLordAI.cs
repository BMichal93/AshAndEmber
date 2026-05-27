// =============================================================================
// LIFE & DEATH MAGIC — AI/ColourLordAI.cs
// NPC mage battle AI. Uses SpellEffects.ExecuteNpcBlast/ExecuteNpcBurst with
// pre-prepared SpellCast recipes. Impulsive lords cast more, Calculating less.
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

namespace ColoursOfCalradia
{
    public static class ColourLordAI
    {
        private const float DefaultCooldown     = 25f;
        private const float ImpulsiveCooldown   = 15f;
        private const float CalculatingCooldown = 35f;
        private const float BlightCooldown      = 6f;  // blight lords cast ~4× more often

        private static readonly Dictionary<string, float> _cooldowns   = new Dictionary<string, float>();
        private static readonly Dictionary<string, int>   _battleCasts = new Dictionary<string, int>();
        private static readonly Random _rng = new Random();

        private static float _tickAccum   = 0f;
        private const  float TickInterval = 0.5f;
        private static bool  _warmupDone  = false;
        private static float _warmupTimer = 0f;
        private const  float WarmupDuration = 12f;

        public static void ClearCooldowns()
        {
            _cooldowns.Clear();
            _battleCasts.Clear();
            _warmupDone  = false;
            _warmupTimer = 0f;
        }

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

            bool isBlight = ColourLordRegistry.IsBlightLord(hero);

            var enemies = SpellEffects.EnemiesOf(agent);
            var allies  = SpellEffects.AlliesOf(agent);

            // Blight lords cast proactively even if no one is obviously endangered
            if (!isBlight && enemies.Count == 0 && allies.All(a => a.Health >= a.HealthLimit * 0.9f)) return;

            float hpPct = agent.Health / Math.Max(agent.HealthLimit, 1f);
            int closeEnemies = enemies.Count(a => a.Position.Distance(agent.Position) < 8f);
            int nearEnemies  = enemies.Count(a => a.Position.Distance(agent.Position) < 20f);

            // 0. Ward self when health is low OR magic was recently cast nearby
            bool endangered = hpPct < 0.40f
                           || SpellEffects.HasRecentMagicNearby(agent.Position, 20f);
            if (endangered && !SpellEffects.IsWarded(agent))
            {
                CastWard(agent, hero);
                return;
            }

            // 1. Heal when badly hurt
            if (hpPct < 0.30f)
            {
                CastHealZone(agent, hero);
                return;
            }

            // 2. Help hurt allies
            bool allyHurt = allies.Any(a => a.Health < a.HealthLimit * 0.5f
                                         && a.Position.Distance(agent.Position) <= 15f);
            if (allyHurt)
            {
                CastHealZone(agent, hero);
                return;
            }

            if (nearEnemies == 0 && !isBlight) return;

            // 3. Choose attack recipe — blight lords use wider dice (more aggressive combos)
            int roll = isBlight ? _rng.Next(6) : _rng.Next(4);
            if (closeEnemies >= 3)
            {
                // Surrounded — use Burst to push or damage
                if (roll < 2)
                    CastBurst(agent, hero, 2, 0, 1, 0, false); // Burst+Push
                else if (roll < 4)
                    CastBurst(agent, hero, 2, 2, 0, 0, false); // Burst+Damage
                else if (isBlight)
                    CastBurst(agent, hero, 3, 2, 1, 0, false); // Blight: heavier Burst+Dmg+Push
                else
                    CastBurst(agent, hero, 2, 2, 0, 0, false);
            }
            else
            {
                // Cone enemies — use Blast
                int coneCount = SpellEffects.CountEnemiesInCone(agent, 10f, 0.80f);
                if (coneCount >= 1)
                {
                    if (roll == 0)
                        CastBlast(agent, hero, 2, 2, 0, 0, false); // Blast+Damage
                    else if (roll == 1)
                        CastBlast(agent, hero, 2, 0, 0, 2, false); // Blast+Morale
                    else if (roll == 2)
                        CastBlast(agent, hero, 2, 0, 1, 0, false); // Blast+Push
                    else if (roll == 3)
                        CastBurst(agent, hero, 2, 1, 0, 1, false); // Burst+Dmg+Morale
                    else if (roll == 4 && isBlight)
                        CastBlast(agent, hero, 3, 3, 0, 0, false); // Blight: heavy Blast
                    else
                        CastBlast(agent, hero, 3, 0, 0, 3, false); // Blight: mass morale blast
                }
                else if (isBlight)
                {
                    // Blight lords launch morale blasts even without cone alignment
                    CastBurst(agent, hero, 2, 0, 0, 3, false);
                }
                else
                {
                    CastBurst(agent, hero, 2, 0, 0, 2, false);
                }
            }
        }

        private static void CastBlast(Agent agent, Hero hero,
            int formCount, int dmg, int push, int morale, bool reversed)
        {
            try
            {
                SpellEffects.ExecuteNpcBlast(agent, formCount, dmg, push, morale, reversed, agent.Team);
                ApplyCastVisuals(agent);
                SetCooldown(hero);
                RecordCast(hero);
            }
            catch { }
        }

        private static void CastBurst(Agent agent, Hero hero,
            int formCount, int dmg, int push, int morale, bool reversed)
        {
            try
            {
                SpellEffects.ExecuteNpcBurst(agent, formCount, dmg, push, morale, reversed, agent.Team);
                ApplyCastVisuals(agent);
                SetCooldown(hero);
                RecordCast(hero);
            }
            catch { }
        }

        private static void CastWard(Agent agent, Hero hero)
        {
            try
            {
                SpellEffects.ExecuteWardFromAgent(agent);
                SetCooldown(hero);
                RecordCast(hero);
            }
            catch { }
        }

        private static void CastHealZone(Agent agent, Hero hero)
        {
            try
            {
                SpellEffects.SpawnNpcHealZone(agent.Position, ColorSchool.Green, 1f, agent.Team);
                ApplyCastVisuals(agent);
                SetCooldown(hero);
                RecordCast(hero);
            }
            catch { }
        }

        private static void ApplyCastVisuals(Agent agent)
        {
            SpellEffects.BeginAgentGlow(agent, ColorSchool.Purple, 3f);
            SpellEffects.TryCastSound(agent.Position, ColorSchool.Purple);
            SpellEffects.TryCastAnimation(agent);
            SpellEffects.RecordMagicCast(agent.Position);
        }

        private static void SetCooldown(Hero hero)
        {
            try
            {
                if (ColourLordRegistry.IsBlightLord(hero))
                {
                    _cooldowns[hero.StringId] = BlightCooldown;
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

        private static void RecordCast(Hero hero)
        {
            if (!_battleCasts.ContainsKey(hero.StringId))
                _battleCasts[hero.StringId] = 0;
            _battleCasts[hero.StringId]++;
        }
    }
}
