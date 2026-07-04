// =============================================================================
// LIFE & DEATH MAGIC — AI/BanditMageAI.cs
// Gives ~4% of eligible bandit units minor spellcasting ability (MageChance).
// Casters are seeded once per mission at warmup and tracked by Agent reference.
// They use simple blast/burst recipes with modest power and an 18 s cooldown.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static class BanditMageAI
    {
        private static readonly HashSet<string> _eligibleTroops = new HashSet<string>
        {
            "looter",
            "forest_bandit",
            "sea_raider",
            "mountain_bandit",
            "steppe_bandit",
            "desert_bandit",
            // Custom Fire Worshipper troop tree
            "fire_devotee",
            "fire_zealot",
            "ember_caller",
            "ember_shaman",
            // Custom Ashen Spawn troop tree
            "ashen_thrall",
            "ashen_invoker",
            // Custom Wandering Circle troop tree
            "circle_acolyte",
            "circle_druid",
            "circle_shaman",
        };

        // Troops that always receive the special-battle (cultist) tier regardless of party flag
        private static readonly HashSet<string> _cultistTroops = new HashSet<string>
        {
            "fire_devotee", "fire_zealot", "ember_caller", "ember_shaman",
            "ashen_thrall", "ashen_invoker",
            "circle_acolyte", "circle_druid", "circle_shaman",
        };

        private static readonly Dictionary<string, string> _titles = new Dictionary<string, string>
        {
            { "looter",           "Fire Zealot"    },
            { "forest_bandit",    "Hedge Witch"     },
            { "sea_raider",       "Ashen Caller"    },
            { "mountain_bandit",  "Ash Shaman"      },
            { "steppe_bandit",    "Wind Dancer"     },
            { "desert_bandit",    "Ember Binder"    },
            { "fire_devotee",     "Fire Devotee"    },
            { "fire_zealot",      "Fire Zealot"     },
            { "ember_caller",     "Ember Caller"    },
            { "ember_shaman",     "Ember Sorcerer"  },
            { "ashen_thrall",     "Ashen Thrall"    },
            { "ashen_invoker",    "Ashen Invoker"   },
            { "circle_acolyte",   "Acolyte"         },
            { "circle_druid",     "Druid"           },
            { "circle_shaman",    "Shaman"          },
        };

        private const float CooldownDuration = 18f;
        private const float WarmupDuration   = 8f;
        private const float MageChance       = 0.04f;  // ~1 caster per 25 eligible bandits
        // Burnout chance varies by tier: looters barely control fire (high), Fire Worshippers are practiced (low)
        private const float BurnoutLooter   = 0.35f;
        private const float BurnoutBandit   = 0.25f;
        private const float BurnoutSpecial  = 0.15f;
        private const float TickInterval     = 0.5f;

        private static readonly HashSet<Agent>            _mageAgents = new HashSet<Agent>();
        private static readonly Dictionary<Agent, float>  _cooldowns  = new Dictionary<Agent, float>();
        private static readonly Random                    _rng        = new Random();

        private static float _tickAccum  = 0f;
        private static float _warmupTimer = 0f;
        private static bool  _seeded     = false;
        private static bool  _isSpecialBattle = false; // Fire Worshippers / Ashen Spawn

        public static void OnMissionEnd()
        {
            _mageAgents.Clear();
            _cooldowns.Clear();
            _tickAccum      = 0f;
            _warmupTimer    = 0f;
            _seeded         = false;
            _isSpecialBattle = false;
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

            _isSpecialBattle = IsSpecialBanditBattle();

            foreach (Agent a in candidates)
                if (_rng.NextDouble() < MageChance)
                    _mageAgents.Add(a);

            // Fire Worshippers and Ashen Spawn guarantee at least one mage caster
            if (_mageAgents.Count == 0 && candidates.Count > 0 && _isSpecialBattle)
                _mageAgents.Add(candidates[_rng.Next(candidates.Count)]);
        }

        private static bool IsSpecialBanditBattle()
        {
            try
            {
                var mapEvent = TaleWorlds.CampaignSystem.MapEvents.MapEvent.PlayerMapEvent;
                if (mapEvent == null) return false;
                foreach (var side in new[] { mapEvent.AttackerSide, mapEvent.DefenderSide })
                {
                    if (side == null) continue;
                    foreach (var mp in side.Parties)
                    {
                        var p = mp?.Party?.MobileParty;
                        if (p != null && FireWorshippersSystem.IsSpecialParty(p)) return true;
                    }
                }
            }
            catch { }
            return false;
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
            SpellEffects.TryFreeHandForCast(mage); // sheathe visually before cast, never blocks

            var enemies = SpellEffects.EnemiesOf(mage);
            if (enemies.Count == 0) return;

            int nearEnemies  = enemies.Count(a => a.Position.Distance(mage.Position) < 12f);
            int closeEnemies = enemies.Count(a => a.Position.Distance(mage.Position) < 6f);

            if (nearEnemies == 0) return;

            int burstEnemies = enemies.Count(a => a.Position.Distance(mage.Position) < 5f);
            int burstAllies  = SpellEffects.CountAlliesInRadius(mage, 5f);
            int coneEnemies  = SpellEffects.CountEnemiesInCone(mage, 6f, 0.65f);
            int coneAllies   = SpellEffects.CountAlliesInCone(mage, 6f, 0.65f);

            // Surrounded → burst to clear space; otherwise → random choice
            bool useBurst = closeEnemies >= 2 || _rng.Next(2) == 0;

            // Redirect to the safer option when one clearly beats the other
            bool burstFriendlyFire = burstAllies > 0 && burstEnemies <= burstAllies;
            bool blastFriendlyFire = coneAllies > 0 && coneEnemies <= coneAllies;
            if (burstFriendlyFire && !blastFriendlyFire) useBurst = false;
            else if (blastFriendlyFire && !burstFriendlyFire) useBurst = true;

            // Spell power tiers — the borrowed fire burns at one crude, flat heat
            // (35/hit, crystal-tier: below a trained lord's 44 full-draw cone, far
            // below the boss tier); skill instead widens the WORKING:
            //   Looter (untrained):       formCount=1, barely leaves the hand
            //   Regular bandit casters:   formCount=2, modest reach
            //   Fire Worshippers / Ashen: formCount=3, wide reach
            string troopId   = (mage.Character as TaleWorlds.CampaignSystem.CharacterObject)?.StringId ?? "";
            bool isLooter    = troopId == "looter";
            bool isCultist   = _cultistTroops.Contains(troopId);

            try
            {
                string title = GetTitle(mage);
                InformationManager.DisplayMessage(new InformationMessage(
                    $"The {title} channels the fire!",
                    new Color(0.85f, 0.35f, 0.15f)));

                _cooldowns[mage] = CooldownDuration;

                bool specialBattle = _isSpecialBattle;
                bool looter        = isLooter;
                bool burst         = useBurst;

                // Custom cultist troops are always treated as special-tier regardless of party flag.
                bool effectiveSpecial = specialBattle || isCultist;

                // The fire burns those who borrow it without the gift.
                float burnout = effectiveSpecial ? BurnoutSpecial
                              : looter           ? BurnoutLooter
                              :                   BurnoutBandit;
                bool willBurnout = _rng.NextDouble() < burnout;
                if (willBurnout) _mageAgents.Remove(mage);

                SpellEffects.QueueNpcCastWithWindup(mage, () =>
                {
                    if (effectiveSpecial)
                    {
                        if (burst) SpellEffects.ExecuteNpcBurst(mage, 3, 1, 0, mage.Team);
                        else       SpellEffects.ExecuteNpcBlast(mage, 3, 1, 0, mage.Team);
                    }
                    else if (looter)
                    {
                        if (burst) SpellEffects.ExecuteNpcBurst(mage, 1, 1, 0, mage.Team);
                        else       SpellEffects.ExecuteNpcBlast(mage, 1, 1, 0, mage.Team);
                    }
                    else
                    {
                        if (burst) SpellEffects.ExecuteNpcBurst(mage, 2, 1, 0, mage.Team);
                        else       SpellEffects.ExecuteNpcBlast(mage, 2, 1, 0, mage.Team);
                    }

                    SpellEffects.BeginAgentGlow(mage, ColorSchool.Red, 2f);
                    SpellEffects.TryCastSound(mage.Position, ColorSchool.Red);
                    SpellEffects.TryCastAnimation(mage);
                    SpellEffects.RecordMagicCast(mage.Position);

                    if (willBurnout)
                    {
                        SpellEffects.QueueKill(mage);
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"The {title} is consumed by the fire.",
                            new Color(0.6f, 0.2f, 0.1f)));
                    }
                });
            }
            catch { }
        }
    }
}
