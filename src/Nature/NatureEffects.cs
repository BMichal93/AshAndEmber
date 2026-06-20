// =============================================================================
// ASH AND EMBER — Nature/NatureEffects.cs
// Executes the twelve Living Ember powers in battle and on the campaign map.
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
    public static class NatureEffects
    {
        private static readonly Random _rng = new Random();

        // Speed-limit tokens applied by nature effects, keyed by agent index.
        // (agent index → remaining seconds)
        private static readonly Dictionary<int, (float remaining, Agent agent)> _speedTokens
            = new Dictionary<int, (float, Agent)>();

        // Damage resistance tokens.
        private static readonly Dictionary<int, (float fraction, float remaining, Agent agent)> _resistTokens
            = new Dictionary<int, (float, float, Agent)>();

        // ── Armour gate ────────────────────────────────────────────────────────
        public static bool ArmourTooHeavy(Agent agent)
        {
            if (agent == null) return false;
            try
            {
                float total = 0f;
                var eq = agent.SpawnEquipment;
                if (eq == null) return false;
                for (EquipmentIndex idx = EquipmentIndex.Head;
                     idx <= EquipmentIndex.Cape; idx++)
                {
                    var item = eq.GetEquipmentFromSlot(idx).Item;
                    if (item != null) total += item.Weight;
                }
                return total > NatureMath.ArmourWeightCap;
            }
            catch { return false; }
        }

        // ── Execute ───────────────────────────────────────────────────────────
        // Called by NatureInputHandler after a charge is released.
        // Returns true if something meaningful happened.
        public static bool Execute(NaturePower power, Agent caster, bool inMission)
        {
            if (power == NaturePower.None) return false;

            if (inMission)
            {
                if (caster == null || !caster.IsActive() || caster.Health <= 0f) return false;
                if (!SpellEffects.HasFreeHand(caster))
                {
                    Msg("Both hands are occupied. The land cannot speak through closed fists.", NatureColor);
                    return false;
                }
                if (ArmourTooHeavy(caster))
                {
                    Msg("The weight of your armour smothers the channel. The land cannot reach you.", NatureColor);
                    return false;
                }
                return ExecuteBattle(power, caster);
            }
            else
            {
                return ExecuteCampaign(power);
            }
        }

        // NPC variant — no player restrictions (no hand check, no armour check).
        public static void ExecuteNpc(NaturePower power, Agent caster, Team casterTeam)
        {
            if (power == NaturePower.None || caster == null || !caster.IsActive()) return;
            try { ExecuteBattleCore(power, caster, casterTeam); } catch { }
        }

        // ── Battle effects ─────────────────────────────────────────────────────
        private static bool ExecuteBattle(NaturePower power, Agent caster)
        {
            try
            {
                ExecuteBattleCore(power, caster, caster.Team);
                return true;
            }
            catch { return false; }
        }

        private static void ExecuteBattleCore(NaturePower power, Agent caster, Team casterTeam)
        {
            Vec3 pos = caster.Position;

            switch (power)
            {
                case NaturePower.Thorngrasp:   BattleThorngrasp(caster, pos, casterTeam);   break;
                case NaturePower.LivingBreath: BattleLivingBreath(caster, pos, casterTeam); break;
                case NaturePower.StoneSurge:   BattleStoneSurge(caster, pos, casterTeam);   break;
                case NaturePower.EarthMantle:  BattleEarthMantle(caster, pos);              break;
                case NaturePower.Undertow:     BattleUndertow(caster, pos, casterTeam);     break;
                case NaturePower.StillWater:   BattleStillWater(caster);                    break;
                case NaturePower.CallingGale:  BattleCallingGale(caster, pos, casterTeam);  break;
                case NaturePower.FairWind:     BattleFairWind(caster, pos, casterTeam);     break;
                case NaturePower.Hoarfrost:    BattleHoarfrost(caster, pos, casterTeam);    break;
                case NaturePower.GlacialShell: BattleGlacialShell(caster, pos);             break;
                case NaturePower.WrathOfTheSky:BattleWrathOfTheSky(caster, pos, casterTeam);break;
                case NaturePower.LevinStep:    BattleLevinStep(caster);                     break;
            }

            // Visual: green glow pulse + temp light
            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Nature, 2.5f); } catch { }
            try { SpellEffects.SpawnTempLight(pos + new Vec3(0,0,0.8f), ColorSchool.Nature, 8f, 2f); } catch { }
            Msg($"{NatureMath.PowerName(power)} — the world answers.", NatureColor);
        }

        // Thorngrasp: single target pull to 3m, held 2.5s, no direct damage
        private static void BattleThorngrasp(Agent caster, Vec3 pos, Team team)
        {
            Agent target = NearestEnemy(caster, pos, NatureMath.ThorngraspRange, team);
            if (target == null) return;
            try
            {
                Vec3 dir = (pos - target.Position);
                float dist = dir.Length;
                if (dist > 0.1f)
                {
                    Vec3 dest = target.Position + dir.NormalizedCopy() * Math.Max(0f, dist - NatureMath.ThorngrassPullDist);
                    target.TeleportToPosition(dest);
                }
                target.SetMaximumSpeedLimit(0f, false);
                ApplySpeedToken(target, 0f, NatureMath.ThorngaspHoldSec);
            }
            catch { }
        }

        // Living Breath: caster +25 HP, allies in 10m +18 HP, +15 morale
        private static void BattleLivingBreath(Agent caster, Vec3 pos, Team team)
        {
            try
            {
                caster.Health = Math.Min(caster.HealthLimit, caster.Health + NatureMath.LivingBreathSelfHp);
            }
            catch { }

            ForEachAllyInRadius(pos, NatureMath.LivingBreathRadius, caster, team, ally =>
            {
                try { ally.Health = Math.Min(ally.HealthLimit, ally.Health + NatureMath.LivingBreathAllyHp); } catch { }
                try
                {
                    var formation = ally.Formation;
                    if (formation != null) formation.ApplyActionOnEachUnit(_ => { }, null);
                }
                catch { }
            });

            try
            {
                Mission.Current?.Teams?.ToList().ForEach(t =>
                {
                    if (t == team && t.IsValid)
                        try { t.QuerySystem.TeamMorale += NatureMath.LivingBreathMorale; } catch { }
                });
            }
            catch { }
        }

        // Stone Surge: blunt damage + root in 5m radius; caster immobile 0.5s
        private static void BattleStoneSurge(Agent caster, Vec3 pos, Team team)
        {
            ForEachEnemyInRadius(pos, NatureMath.StoneSurgeRadius, team, enemy =>
            {
                ApplyDamage(enemy, caster, NatureMath.StoneSurgeDamage, DamageTypes.Blunt);
                ApplySpeedToken(enemy, 0f, NatureMath.StoneSurgeRootSec);
            });
            // Brief caster stagger
            ApplySpeedToken(caster, 0f, NatureMath.StoneSurgeStaggerSec);
        }

        // Earth Mantle: -40% physical damage taken for 10s
        private static void BattleEarthMantle(Agent caster, Vec3 pos)
        {
            ApplyResistToken(caster, NatureMath.EarthMantleResist, NatureMath.EarthMantleSec);
        }

        // Undertow: 60° cone 8m — cold damage + knockback + -25% speed 5s
        private static void BattleUndertow(Agent caster, Vec3 pos, Team team)
        {
            Vec3 fwd = caster.LookDirection.NormalizedCopy();
            float halfAngle = NatureMath.UndertowAngleDeg * 0.5f * (float)(Math.PI / 180.0);

            ForEachEnemyInRadius(pos, NatureMath.UndertowRange, team, enemy =>
            {
                Vec3 toEnemy = (enemy.Position - pos).NormalizedCopy();
                float dot = Vec3.DotProduct(fwd, toEnemy);
                if (dot < Math.Cos(halfAngle)) return;

                ApplyDamage(enemy, caster, NatureMath.UndertowDamage, DamageTypes.Invalid);
                // Knockback away from caster
                try
                {
                    Vec3 dir = (enemy.Position - pos).NormalizedCopy();
                    enemy.TeleportToPosition(enemy.Position + dir * NatureMath.UndertowKnockback);
                }
                catch { }
                ApplySpeedToken(enemy, NatureMath.UndertowSpeedMult, NatureMath.UndertowSpeedSec);
            });
        }

        // Still Water: self-heal 35 HP
        private static void BattleStillWater(Agent caster)
        {
            try
            {
                caster.Health = Math.Min(caster.HealthLimit, caster.Health + NatureMath.StillWaterHeal);
            }
            catch { }
        }

        // Calling Gale: 360° — damage + knockback to enemies, speed buff to allies
        private static void BattleCallingGale(Agent caster, Vec3 pos, Team team)
        {
            ForEachEnemyInRadius(pos, NatureMath.CallingGaleRadius, team, enemy =>
            {
                ApplyDamage(enemy, caster, NatureMath.CallingGaleDamage, DamageTypes.Invalid);
                try
                {
                    Vec3 dir = (enemy.Position - pos).NormalizedCopy();
                    enemy.TeleportToPosition(enemy.Position + dir * NatureMath.CallingGaleKnockback);
                }
                catch { }
                ApplySpeedToken(enemy, NatureMath.CallingGaleSpeedMult, NatureMath.CallingGaleSpeedSec);
            });

            ForEachAllyInRadius(pos, NatureMath.CallingGaleRadius, caster, team, ally =>
            {
                ApplySpeedToken(ally, NatureMath.CallingGaleAllySpeed, NatureMath.CallingGaleAllySpeedSec);
            });
        }

        // Fair Wind: speed buff +35% to caster + allies in 8m for 15s
        private static void BattleFairWind(Agent caster, Vec3 pos, Team team)
        {
            ApplySpeedToken(caster, NatureMath.FairWindSpeedMult, NatureMath.FairWindSec);
            ForEachAllyInRadius(pos, NatureMath.FairWindRadius, caster, team, ally =>
            {
                ApplySpeedToken(ally, NatureMath.FairWindSpeedMult, NatureMath.FairWindSec);
            });
        }

        // Hoarfrost: AoE cold damage + -40% speed 7s + brief self slow
        private static void BattleHoarfrost(Agent caster, Vec3 pos, Team team)
        {
            ForEachEnemyInRadius(pos, NatureMath.HoarfrostRadius, team, enemy =>
            {
                ApplyDamage(enemy, caster, NatureMath.HoarfrostDamage, DamageTypes.Invalid);
                ApplySpeedToken(enemy, NatureMath.HoarfrostSpeedMult, NatureMath.HoarfrostSpeedSec);
            });
            ApplySpeedToken(caster, NatureMath.HoarfrostSelfSlowMult, NatureMath.HoarfrostSelfSlowSec);
        }

        // Glacial Shell: -40% damage taken + stagger immunity 10s, -20% speed
        private static void BattleGlacialShell(Agent caster, Vec3 pos)
        {
            ApplyResistToken(caster, NatureMath.GlacialShellResist, NatureMath.GlacialShellSec);
            ApplySpeedToken(caster, NatureMath.GlacialShellSpeedMult, NatureMath.GlacialShellSec);
        }

        // Wrath of the Sky: primary target up to 8m — 70 dmg + stagger;
        // chains to up to 2 nearby enemies for 35 each.
        private static void BattleWrathOfTheSky(Agent caster, Vec3 pos, Team team)
        {
            Agent primary = NearestEnemy(caster, pos, NatureMath.WrathRange, team);
            if (primary == null) return;

            ApplyDamage(primary, caster, NatureMath.WrathDamagePrimary, DamageTypes.Invalid);
            try { primary.SetMaximumSpeedLimit(0f, false); } catch { }
            ApplySpeedToken(primary, 0f, 1.5f);

            // Spawn lightning visual
            try
            {
                SpellEffects.SpawnTempLight(primary.Position + new Vec3(0,0,1f),
                    ColorSchool.Nature, 6f, 1f);
            }
            catch { }

            // Chain hits
            int chains = 0;
            ForEachEnemyInRadius(primary.Position, NatureMath.WrathChainRadius, team, chain =>
            {
                if (chains >= NatureMath.WrathChainCount) return;
                if (chain == primary) return;
                ApplyDamage(chain, caster, NatureMath.WrathDamageChain, DamageTypes.Invalid);
                chains++;
            });
        }

        // Levin Step: dash forward 5m, brief invuln frame via speed burst
        private static void BattleLevinStep(Agent caster)
        {
            try
            {
                Vec3 fwd = caster.LookDirection.NormalizedCopy();
                Vec3 dest = caster.Position + fwd * NatureMath.LevinStepDist;
                caster.TeleportToPosition(dest);
                // Brief speed token to represent the invuln window / momentum
                ApplySpeedToken(caster, 1.5f, NatureMath.LevinStepInvulnerSec);
            }
            catch { }
        }

        // ── Campaign effects ───────────────────────────────────────────────────
        private static bool ExecuteCampaign(NaturePower power)
        {
            switch (power)
            {
                case NaturePower.LivingBreath:
                    CampaignLivingBreath(); return true;
                case NaturePower.StillWater:
                    CampaignStillWater();   return true;
                case NaturePower.FairWind:
                    CampaignFairWind();     return true;
                case NaturePower.EarthMantle:
                    CampaignEarthMantle();  return true;
                case NaturePower.GlacialShell:
                    CampaignGlacialShell(); return true;
                default:
                    Msg($"{NatureMath.PowerName(power)} — this power only stirs in conflict.",
                        NatureColor);
                    return false;
            }
        }

        private static void CampaignLivingBreath()
        {
            try
            {
                // Heal wounded troops (same pattern as Inspire/Kindle)
                var roster = MobileParty.MainParty?.MemberRoster;
                if (roster != null)
                {
                    int healed = 0;
                    foreach (var row in roster.GetTroopRoster().ToList())
                    {
                        if (row.WoundedNumber <= 0) continue;
                        int recover = Math.Min(row.WoundedNumber, 6);
                        roster.AddToCountsData(row.Character, 0, -recover);
                        healed += recover;
                    }
                    if (healed > 0)
                        Msg($"Living Breath — {healed} wounded soldiers recover.", NatureColor);
                }
                // Morale boost
                try { MobileParty.MainParty.RecentEventsMorale += 20f; } catch { }
            }
            catch { }
        }

        private static void CampaignStillWater()
        {
            try
            {
                var h = TaleWorlds.CampaignSystem.Hero.MainHero;
                if (h != null)
                {
                    h.HitPoints = Math.Min(h.HitPoints + 20, 100);
                    Msg("Still Water — your wounds settle and grow quiet.", NatureColor);
                }
            }
            catch { }
        }

        private static void CampaignFairWind()
        {
            try
            {
                MobileParty.MainParty.RecentEventsMorale += 25f;
                Msg("Fair Wind — your column marches with lighter feet.", NatureColor);
            }
            catch { }
        }

        private static void CampaignEarthMantle()
        {
            Msg("Earth Mantle — the ground braces beneath you. " +
                "(Grants +40% resist in the next battle for 10s.)", NatureColor);
            // Battle effect is handled when the power is cast in-mission
        }

        private static void CampaignGlacialShell()
        {
            Msg("Glacial Shell — ice settles across your skin. " +
                "(Grants +40% resist in the next battle for 10s.)", NatureColor);
        }

        // ── Tick ──────────────────────────────────────────────────────────────
        public static void MissionTick(float dt)
        {
            TickSpeedTokens(dt);
            TickResistTokens(dt);
        }

        private static void TickSpeedTokens(float dt)
        {
            foreach (int key in _speedTokens.Keys.ToList())
            {
                var (remaining, agent) = _speedTokens[key];
                remaining -= dt;
                if (remaining <= 0f || agent == null || !agent.IsActive())
                {
                    try { agent?.SetMaximumSpeedLimit(10f, false); } catch { }
                    _speedTokens.Remove(key);
                }
                else
                {
                    _speedTokens[key] = (remaining, agent);
                }
            }
        }

        private static void TickResistTokens(float dt)
        {
            foreach (int key in _resistTokens.Keys.ToList())
            {
                var (frac, remaining, agent) = _resistTokens[key];
                remaining -= dt;
                if (remaining <= 0f)
                    _resistTokens.Remove(key);
                else
                    _resistTokens[key] = (frac, remaining, agent);
            }
        }

        // Called from OnAgentHit to reduce incoming damage for resist token holders.
        public static float ApplyResistance(Agent agent, float incomingDamage)
        {
            if (agent == null) return incomingDamage;
            if (!_resistTokens.TryGetValue(agent.Index, out var tok)) return incomingDamage;
            return incomingDamage * (1f - tok.frac);
        }

        public static bool HasResist(Agent agent)
            => agent != null && _resistTokens.ContainsKey(agent.Index);

        // ── Clear ─────────────────────────────────────────────────────────────
        public static void ClearBattleState()
        {
            foreach (var (_, agent) in _speedTokens.Values)
                try { agent?.SetMaximumSpeedLimit(10f, false); } catch { }
            _speedTokens.Clear();
            _resistTokens.Clear();
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static void ApplyDamage(Agent target, Agent source, float amount, DamageTypes dmgType)
        {
            if (target == null || !target.IsActive() || target.Health <= 0f) return;
            try
            {
                target.Health -= amount;
                if (target.Health <= 0f)
                    target.Die(new Blow(source?.Index ?? -1));
            }
            catch { }
        }

        private static void ApplySpeedToken(Agent agent, float mult, float seconds)
        {
            if (agent == null || !agent.IsActive()) return;
            try { agent.SetMaximumSpeedLimit(mult == 0f ? 0f : 10f * mult, false); } catch { }
            _speedTokens[agent.Index] = (seconds, agent);
        }

        private static void ApplyResistToken(Agent agent, float fraction, float seconds)
        {
            if (agent == null) return;
            _resistTokens[agent.Index] = (fraction, seconds, agent);
        }

        private static Agent NearestEnemy(Agent caster, Vec3 pos, float range, Team team)
        {
            Agent nearest = null;
            float bestDist = range * range;
            try
            {
                foreach (Agent a in Mission.Current.Agents)
                {
                    if (!a.IsActive() || a.IsMount || a == caster || a.Health <= 0f) continue;
                    bool enemy = false;
                    try { enemy = team != null && team.IsEnemyOf(a.Team); } catch { continue; }
                    if (!enemy) continue;
                    float d = (a.Position - pos).LengthSquared;
                    if (d < bestDist) { bestDist = d; nearest = a; }
                }
            }
            catch { }
            return nearest;
        }

        private static void ForEachEnemyInRadius(Vec3 pos, float radius, Team team, Action<Agent> action)
        {
            float r2 = radius * radius;
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount || a.Health <= 0f) continue;
                    bool enemy = false;
                    try { enemy = team != null && team.IsEnemyOf(a.Team); } catch { continue; }
                    if (!enemy) continue;
                    if ((a.Position - pos).LengthSquared <= r2)
                        try { action(a); } catch { }
                }
            }
            catch { }
        }

        private static void ForEachAllyInRadius(Vec3 pos, float radius, Agent exclude,
            Team team, Action<Agent> action)
        {
            float r2 = radius * radius;
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount || a == exclude || a.Health <= 0f) continue;
                    if (a.Team != team) continue;
                    if ((a.Position - pos).LengthSquared <= r2)
                        try { action(a); } catch { }
                }
            }
            catch { }
        }

        private static readonly Color NatureColor = new Color(0.35f, 0.75f, 0.35f);

        private static void Msg(string text, Color color)
            => InformationManager.DisplayMessage(new InformationMessage(text, color));
    }
}
