// =============================================================================
// ASH AND EMBER — Nature/NatureSeerAI.cs
// Battle AI for NPC Nature Seers. Per-agent cooldown; seers cast from their
// terrain-appropriate power pool. Warmup delay prevents instant-cast at spawn.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static class NatureSeerAI
    {
        private const float Cooldown        = 18f;   // seconds between casts
        private const float WarmupDuration  = 10f;   // no casts before this
        private const float TickInterval    = 0.5f;
        private const float NpcCastChance   = 0.45f; // probability per cooldown-tick

        private static float _tickAccum    = 0f;
        private static float _warmupTimer  = 0f;
        private static bool  _warmupDone   = false;
        private static readonly Dictionary<string, float> _cooldowns = new Dictionary<string, float>();
        private static readonly Random _rng = new Random();

        // Cached battle terrain (set once per mission)
        private static NatureElement[] _battleElements = null;

        public static void ClearCooldowns()
        {
            _cooldowns.Clear();
            _tickAccum   = 0f;
            _warmupTimer = 0f;
            _warmupDone  = false;
            _battleElements = null;
        }

        public static void MissionTick(float dt)
        {
            if (Mission.Current == null) return;
            if (!SpellEffects.IsBattleMission()) return;

            _tickAccum += dt;
            if (_tickAccum < TickInterval) return;
            _tickAccum = 0f;

            if (!Mission.Current.AllowAiTicking) return;

            if (!_warmupDone)
            {
                _warmupTimer += TickInterval;
                if (_warmupTimer < WarmupDuration) return;
                _warmupDone = true;
                CacheBattleElements();
                StaggerInitialCooldowns();
            }

            // Tick cooldowns
            foreach (string id in _cooldowns.Keys.ToList())
            {
                _cooldowns[id] -= TickInterval;
                if (_cooldowns[id] <= 0f) _cooldowns.Remove(id);
            }

            List<Agent> agents;
            try { agents = Mission.Current.Agents.ToList(); }
            catch { return; }

            foreach (Agent agent in agents)
            {
                if (!agent.IsActive() || agent.IsMount || !agent.IsHero) continue;
                if (agent == Agent.Main) continue;
                if (agent.Health <= 0f) continue;

                Hero hero = (agent.Character as TaleWorlds.CampaignSystem.CharacterObject)?.HeroObject;
                if (hero == null || !NatureSeerRegistry.IsNatureSeer(hero)) continue;
                if (_cooldowns.ContainsKey(hero.StringId)) continue;

                if (_rng.NextDouble() > NpcCastChance) { ResetCooldown(hero.StringId); continue; }

                TryCastNpc(agent, hero);
                ResetCooldown(hero.StringId);
            }
        }

        private static void TryCastNpc(Agent agent, Hero hero)
        {
            try
            {
                if (_battleElements == null || _battleElements.Length == 0) return;
                NatureElement el = _battleElements[_rng.Next(_battleElements.Length)];

                // Preference: heal if low HP, otherwise attack
                NaturePower power;
                bool lowHp = agent.Health < agent.HealthLimit * 0.40f;
                if (lowHp && (el == NatureElement.Verdant || el == NatureElement.Water))
                    power = el == NatureElement.Verdant
                        ? NaturePower.LivingBreath : NaturePower.StillWater;
                else
                    power = NatureMath.RandomPower(el, _rng);

                SpellEffects.FlashFocusAura(agent, ColorSchool.Nature);
                NatureEffects.ExecuteNpc(power, agent, agent.Team);
            }
            catch { }
        }

        private static void CacheBattleElements()
        {
            try
            {
                if (TaleWorlds.CampaignSystem.Campaign.Current == null
                    || MobileParty.MainParty == null)
                {
                    _battleElements = new[] { NatureElement.Wind };
                    return;
                }
                CampaignVec2 pos = MobileParty.MainParty.Position;
                string terrainName = "Plain";
                try
                {
                    var terrain = TaleWorlds.CampaignSystem.Campaign.Current.MapSceneWrapper
                        ?.GetTerrainTypeAtPosition(pos);
                    if (terrain.HasValue) terrainName = terrain.Value.ToString();
                }
                catch { }
                _battleElements = NatureMath.TerrainElements(terrainName);
            }
            catch { _battleElements = new[] { NatureElement.Wind }; }
        }

        private static void StaggerInitialCooldowns()
        {
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount || !a.IsHero || a == Agent.Main) continue;
                    Hero h = (a.Character as TaleWorlds.CampaignSystem.CharacterObject)?.HeroObject;
                    if (h == null || !NatureSeerRegistry.IsNatureSeer(h)) continue;
                    float jitter = (float)_rng.NextDouble() * Cooldown;
                    if (jitter > 0f) _cooldowns[h.StringId] = jitter;
                }
            }
            catch { }
        }

        private static void ResetCooldown(string heroId)
        {
            float variance = (float)(_rng.NextDouble() * Cooldown * 0.4f);
            _cooldowns[heroId] = Cooldown + variance;
        }
    }
}
