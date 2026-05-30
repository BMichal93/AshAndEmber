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
        private const float AshenCooldown      = 6f;  // Ashen lords cast ~4× more often

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
                        float maxJitter = ashen ? AshenCooldown * 2f : DefaultCooldown * 0.6f;
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

            bool isAshen = ColourLordRegistry.IsAshenLord(hero);

            var enemies = SpellEffects.EnemiesOf(agent);
            var allies  = SpellEffects.AlliesOf(agent);

            // Blight lords cast proactively even if no one is obviously endangered
            if (!isAshen && enemies.Count == 0 && allies.All(a => a.Health >= a.HealthLimit * 0.9f)) return;

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
                CastHealBurst(agent, hero);
                return;
            }

            // 2. Help hurt allies
            bool allyHurt = allies.Any(a => a.Health < a.HealthLimit * 0.5f
                                         && a.Position.Distance(agent.Position) <= 15f);
            if (allyHurt)
            {
                CastHealBurst(agent, hero);
                return;
            }

            if (nearEnemies == 0 && !isAshen) return;

            // 3. Choose attack recipe
            // Ashen lords use a wider die (d6) and cast more aggressively.
            int roll = isAshen ? _rng.Next(6) : _rng.Next(4);
            if (closeEnemies >= 3)
            {
                // Surrounded — use Burst to clear space
                if (roll < 3 || !isAshen)
                    CastBurst(agent, hero, 2, 2, 0);  // 4 inputs: damage burst
                else
                    CastBurst(agent, hero, 3, 3, 0);  // 6 inputs: Ashen heavy burst
            }
            else
            {
                float blastDetectRange = isAshen ? 8f : 6f;
                int coneCount = SpellEffects.CountEnemiesInCone(agent, blastDetectRange, 0.65f);
                if (coneCount >= 1)
                {
                    if (roll == 0)
                        CastBlast(agent, hero, 2, 2, 0); // 4 inputs: solid blast
                    else if (roll == 1)
                        CastBlast(agent, hero, 2, 1, 0); // 3 inputs: light blast
                    else if (roll == 2)
                        CastBurst(agent, hero, 2, 2, 0); // 4 inputs: damage burst
                    else if (roll == 3)
                        CastBlast(agent, hero, 2, 2, 0); // 4 inputs: solid blast
                    else if (roll == 4 && isAshen)
                        CastBlast(agent, hero, 3, 3, 0); // 6 inputs: Ashen devastate
                    else
                        CastBurst(agent, hero, 3, 3, 0); // 6 inputs: Ashen mass burst
                }
                else if (isAshen)
                {
                    CastBurst(agent, hero, 2, 2, 0); // 4 inputs: area pressure
                }
                else
                {
                    CastBurst(agent, hero, 2, 1, 0); // 3 inputs: light burst
                }
            }
        }

        private static void CastBlast(Agent agent, Hero hero, int formCount, int dmg, int restore)
        {
            try
            {
                SpellEffects.ExecuteNpcBlast(agent, formCount, dmg, restore, agent.Team);
                ApplyCastVisuals(agent);
                SetCooldown(hero);
                RecordCast(hero, formCount + dmg + restore);

                bool isAshen = ColourLordRegistry.IsAshenLord(hero);
                string blurb = formCount >= 4
                    ? (isAshen ? "cold fire tears forward." : "channels a devastating blast.")
                    : (isAshen ? "cold fire lashes out." : "shapes fire into a forward blade.");
                AnnounceEnemyCast(agent, hero, blurb);
            }
            catch { }
        }

        private static void CastBurst(Agent agent, Hero hero, int formCount, int dmg, int restore)
        {
            try
            {
                SpellEffects.ExecuteNpcBurst(agent, formCount, dmg, restore, agent.Team);
                ApplyCastVisuals(agent);
                SetCooldown(hero);
                RecordCast(hero, formCount + dmg + restore);

                bool isAshen = ColourLordRegistry.IsAshenLord(hero);
                string blurb = formCount >= 4
                    ? (isAshen ? "tears the veil — cold fire erupts." : "channels a great eruption.")
                    : (isAshen ? "erupts with cold fire." : "fire bursts outward.");
                AnnounceEnemyCast(agent, hero, blurb);
            }
            catch { }
        }

        private static void CastWard(Agent agent, Hero hero)
        {
            try
            {
                // Honorable or merciful lords extend the ward to nearby troops;
                // merciless/dishonorable lords protect only themselves.
                float allyRadius = 0f;
                try
                {
                    bool noble = hero.GetTraitLevel(DefaultTraits.Honor) > 0
                              || hero.GetTraitLevel(DefaultTraits.Mercy) > 0;
                    if (noble) allyRadius = 6f;
                }
                catch { }

                SpellEffects.ExecuteWardFromAgent(agent, allyRadius);
                SetCooldown(hero);
                RecordCast(hero, 2);
                AnnounceEnemyCast(agent, hero, "wraps themselves in the working.");
            }
            catch { }
        }

        private static void CastHealBurst(Agent agent, Hero hero)
        {
            try
            {
                // Restore burst — heals caster and nearby allies; enchantments apply automatically
                SpellEffects.ExecuteNpcBurst(agent, 2, 0, 2, agent.Team);
                ApplyCastVisuals(agent);
                SetCooldown(hero);
                RecordCast(hero, 4);
                AnnounceEnemyCast(agent, hero, "turns the fire inward — wounds close.");
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
                if (ColourLordRegistry.IsAshenLord(hero))
                {
                    _cooldowns[hero.StringId] = AshenCooldown;
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

        // Accumulates total formCount weight so post-battle aging scales with spell power.
        private static void RecordCast(Hero hero, int weight = 1)
        {
            if (!_battleCasts.ContainsKey(hero.StringId))
                _battleCasts[hero.StringId] = 0;
            _battleCasts[hero.StringId] += weight;
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
                    ? new Color(0.45f, 0.45f, 0.65f)   // cold blue-grey for Blight
                    : new Color(0.65f, 0.45f, 0.75f);   // violet for colour lords
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{hero.Name} — {blurb}", c));
            }
            catch { }
        }
    }
}
