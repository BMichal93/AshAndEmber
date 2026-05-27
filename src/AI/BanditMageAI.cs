// =============================================================================
// LIFE & DEATH MAGIC — AI/BanditMageAI.cs
// Gives 12% of eligible bandit units minor spellcasting ability.
// Casters are seeded once per mission at warmup and tracked by Agent reference.
// They use simple blast/burst recipes with modest power and an 18 s cooldown.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static class BanditMageAI
    {
        private static readonly HashSet<string> _eligibleTroops = new HashSet<string>
        {
            "forest_bandit",
            "sea_raider",
            "mountain_bandit",
            "steppe_bandit",
            "desert_bandit",
        };

        private static readonly Dictionary<string, string> _titles = new Dictionary<string, string>
        {
            { "forest_bandit",    "Hedge Witch"    },
            { "sea_raider",       "Storm Caller"   },
            { "mountain_bandit",  "Ash Shaman"     },
            { "steppe_bandit",    "Wind Binder"    },
            { "desert_bandit",    "Ember Prophet"  },
        };

        private const float CooldownDuration = 18f;
        private const float WarmupDuration   = 8f;
        private const float MageChance       = 0.04f;  // ~1 caster per 25 eligible bandits
        private const float BurnoutChance    = 0.25f;  // 25% chance the fire consumes them after casting
        private const float TickInterval     = 0.5f;

        private static readonly HashSet<Agent>            _mageAgents = new HashSet<Agent>();
        private static readonly Dictionary<Agent, float>  _cooldowns  = new Dictionary<Agent, float>();
        private static readonly Random                    _rng        = new Random();

        private static float _tickAccum  = 0f;
        private static float _warmupTimer = 0f;
        private static bool  _seeded     = false;

        public static void OnMissionEnd()
        {
            _mageAgents.Clear();
            _cooldowns.Clear();
            _tickAccum  = 0f;
            _warmupTimer = 0f;
            _seeded     = false;
        }

        public static void MissionTick(float dt)
        {
            if (Mission.Current == null) return;
            if (!SpellEffects.IsBattleMission()) return;

            if (!_seeded)
            {
                _warmupTimer += dt;
                if (_warmupTimer < WarmupDuration) return;
                SeedMages();
                _seeded = true;
            }

            _tickAccum += dt;
            if (_tickAccum < TickInterval) return;
            float tick = _tickAccum;
            _tickAccum = 0f;

            // Tick down cooldowns
            foreach (Agent key in _cooldowns.Keys.ToList())
            {
                float t = _cooldowns[key] - tick;
                if (t <= 0f) _cooldowns.Remove(key);
                else _cooldowns[key] = t;
            }

            // Remove dead/invalid mages
            _mageAgents.RemoveWhere(a => a == null || !a.IsActive());

            foreach (Agent mage in _mageAgents.ToList())
            {
                if (_cooldowns.ContainsKey(mage)) continue;
                TryCast(mage);
            }
        }

        private static void SeedMages()
        {
            if (Mission.Current == null) return;
            List<Agent> candidates;
            try
            {
                candidates = Mission.Current.Agents
                    .Where(a => a.IsActive() && !a.IsMount && !a.IsHero
                             && a != Agent.Main
                             && IsEligible(a))
                    .ToList();
            }
            catch { return; }

            foreach (Agent a in candidates)
                if (_rng.NextDouble() < MageChance)
                    _mageAgents.Add(a);
        }

        private static bool IsEligible(Agent a)
        {
            try
            {
                string id = (a.Character as TaleWorlds.CampaignSystem.CharacterObject)?.StringId;
                return id != null && _eligibleTroops.Contains(id);
            }
            catch { return false; }
        }

        private static string GetTitle(Agent a)
        {
            try
            {
                string id = (a.Character as TaleWorlds.CampaignSystem.CharacterObject)?.StringId;
                if (id != null && _titles.TryGetValue(id, out string title)) return title;
            }
            catch { }
            return "Rogue Mage";
        }

        private static void TryCast(Agent mage)
        {
            if (Mission.Current == null) return;

            var enemies = SpellEffects.EnemiesOf(mage);
            if (enemies.Count == 0) return;

            int nearEnemies  = enemies.Count(a => a.Position.Distance(mage.Position) < 12f);
            int closeEnemies = enemies.Count(a => a.Position.Distance(mage.Position) < 6f);

            if (nearEnemies == 0) return;

            // Simple two-option decision: surrounded → burst, otherwise → blast
            bool useBurst = closeEnemies >= 2 || _rng.Next(2) == 0;

            try
            {
                if (useBurst)
                    SpellEffects.ExecuteNpcBurst(mage, 2, 1, 0, 1, false, mage.Team);
                else
                    SpellEffects.ExecuteNpcBlast(mage, 2, 1, 0, 0, false, mage.Team);

                SpellEffects.BeginAgentGlow(mage, ColorSchool.Red, 2f);
                SpellEffects.TryCastSound(mage.Position, ColorSchool.Red);
                SpellEffects.TryCastAnimation(mage);
                SpellEffects.RecordMagicCast(mage.Position);

                _cooldowns[mage] = CooldownDuration;

                string title = GetTitle(mage);
                InformationManager.DisplayMessage(new InformationMessage(
                    $"The {title} channels dark fire!",
                    new Color(0.85f, 0.35f, 0.15f)));

                // The fire burns those who borrow it without the gift
                if (_rng.NextDouble() < BurnoutChance)
                {
                    _mageAgents.Remove(mage);
                    SpellEffects.QueueKill(mage);
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"The {title} is consumed by the fire.",
                        new Color(0.6f, 0.2f, 0.1f)));
                }
            }
            catch { }
        }
    }
}
