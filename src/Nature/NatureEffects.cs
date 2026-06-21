// =============================================================================
// ASH AND EMBER — Nature/NatureEffects.cs
// Executes the eight Living Ember powers (an attack and a support for each of the
// four elements) in battle and on the campaign map, each with real terrain
// particles so a cast looks like the land itself answering.
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

        // Speed-limit tokens (agent index → remaining seconds).
        private static readonly Dictionary<int, (float remaining, Agent agent)> _speedTokens
            = new Dictionary<int, (float, Agent)>();
        // Damage-resistance tokens.
        private static readonly Dictionary<int, (float fraction, float remaining, Agent agent)> _resistTokens
            = new Dictionary<int, (float, float, Agent)>();

        // ── Armour gate ─────────────────────────────────────────────────────────
        public static bool ArmourTooHeavy(Agent agent)
        {
            if (agent == null) return false;
            try
            {
                float total = 0f;
                var eq = agent.SpawnEquipment;
                if (eq == null) return false;
                for (EquipmentIndex idx = EquipmentIndex.Head; idx <= EquipmentIndex.Cape; idx++)
                {
                    var item = eq.GetEquipmentFromSlot(idx).Item;
                    if (item != null) total += item.Weight;
                }
                return total > NatureMath.ArmourWeightCap;
            }
            catch { return false; }
        }

        // ── Execute ─────────────────────────────────────────────────────────────
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
            return ExecuteCampaign(power);
        }

        // NPC variant — no player restrictions.
        public static void ExecuteNpc(NaturePower power, Agent caster, Team casterTeam)
        {
            if (power == NaturePower.None || caster == null || !caster.IsActive()) return;
            try { ExecuteBattleCore(power, caster, casterTeam); } catch { }
        }

        // ── Battle ──────────────────────────────────────────────────────────────
        private static bool ExecuteBattle(NaturePower power, Agent caster)
        {
            try { ExecuteBattleCore(power, caster, caster.Team); return true; }
            catch { return false; }
        }

        private static void ExecuteBattleCore(NaturePower power, Agent caster, Team team)
        {
            Vec3 pos = caster.Position;
            switch (power)
            {
                case NaturePower.Gale:        BattleGale(caster, pos, team);        break;
                case NaturePower.Tailwind:    BattleTailwind(caster, pos, team);    break;
                case NaturePower.Entangle:    BattleEntangle(caster, pos, team);    break;
                case NaturePower.Bulwark:     BattleBulwark(caster, pos, team);     break;
                case NaturePower.Torrent:     BattleTorrent(caster, pos, team);     break;
                case NaturePower.Renewal:     BattleRenewal(caster, pos, team);     break;
                case NaturePower.ThunderClap: BattleThunderClap(caster, pos, team); break;
                case NaturePower.Stormstep:   BattleStormstep(caster);             break;
            }

            // Living glow + element light bloom (the per-power shapes are spawned
            // inside each Battle* method).
            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Nature, 2.5f); } catch { }
            try { SpawnElementVisual(NatureMath.ElementOf(power), pos); } catch { }
        }

        // Wind · Gale — 360° knockback + damage; a ring of blown dust.
        private static void BattleGale(Agent caster, Vec3 pos, Team team)
        {
            try { SpellEffects.SpawnNatureRing(pos, NatureElement.Wind, NatureMath.GaleRadius * 0.7f, 1.6f); } catch { }
            ForEachEnemyInRadius(pos, NatureMath.GaleRadius, team, enemy =>
            {
                ApplyDamage(enemy, caster, NatureMath.GaleDamage, DamageTypes.Invalid);
                try
                {
                    Vec3 dir = (enemy.Position - pos).NormalizedCopy();
                    enemy.TeleportToPosition(enemy.Position + dir * NatureMath.GaleKnockback);
                }
                catch { }
                ApplySpeedToken(enemy, NatureMath.GaleSlowMult, NatureMath.GaleSlowSec);
            });
        }

        // Wind · Tailwind — speed to caster + nearby allies; trailing dust.
        private static void BattleTailwind(Agent caster, Vec3 pos, Team team)
        {
            ApplySpeedToken(caster, NatureMath.TailwindMult, NatureMath.TailwindSec);
            ForEachAllyInRadius(pos, NatureMath.TailwindRadius, caster, team, ally =>
            {
                ApplySpeedToken(ally, NatureMath.TailwindMult, NatureMath.TailwindSec);
                try { SpellEffects.SpawnNatureBurst(ally.Position, NatureElement.Wind, 1.2f); } catch { }
            });
        }

        // Earth · Entangle — roots erupt in an AoE: damage + immobilise; root ring.
        private static void BattleEntangle(Agent caster, Vec3 pos, Team team)
        {
            try { SpellEffects.SpawnNatureRing(pos, NatureElement.Earth, NatureMath.EntangleRadius * 0.8f, 2.5f); } catch { }
            ForEachEnemyInRadius(pos, NatureMath.EntangleRadius, team, enemy =>
            {
                ApplyDamage(enemy, caster, NatureMath.EntangleDamage, DamageTypes.Blunt);
                try { enemy.SetMaximumSpeedLimit(0f, false); } catch { }
                ApplySpeedToken(enemy, 0f, NatureMath.EntangleRootSec);   // held in place
                try { SpellEffects.SpawnNatureBurst(enemy.Position, NatureElement.Earth, 2.0f); } catch { }
            });
            ApplySpeedToken(caster, 0f, NatureMath.EntangleStaggerSec);
        }

        // Earth · Bulwark — damage resistance to caster + nearby allies.
        private static void BattleBulwark(Agent caster, Vec3 pos, Team team)
        {
            ApplyResistToken(caster, NatureMath.BulwarkResist, NatureMath.BulwarkSec);
            ForEachAllyInRadius(pos, NatureMath.BulwarkRadius, caster, team, ally =>
            {
                ApplyResistToken(ally, NatureMath.BulwarkResist, NatureMath.BulwarkSec);
                try { SpellEffects.SpawnNatureBurst(ally.Position, NatureElement.Earth, 1.5f); } catch { }
            });
        }

        // Water · Torrent — forward cone: damage + knockback that breaks formation.
        private static void BattleTorrent(Agent caster, Vec3 pos, Team team)
        {
            Vec3 fwd = caster.LookDirection.NormalizedCopy();
            float halfAngle = NatureMath.TorrentAngleDeg * 0.5f * (float)(Math.PI / 180.0);
            try { SpellEffects.SpawnNatureLine(pos, pos + fwd * NatureMath.TorrentRange, NatureElement.Water, 2.0f); } catch { }

            ForEachEnemyInRadius(pos, NatureMath.TorrentRange, team, enemy =>
            {
                Vec3 toEnemy = (enemy.Position - pos).NormalizedCopy();
                if (Vec3.DotProduct(fwd, toEnemy) < Math.Cos(halfAngle)) return;
                ApplyDamage(enemy, caster, NatureMath.TorrentDamage, DamageTypes.Invalid);
                try { enemy.TeleportToPosition(enemy.Position + toEnemy * NatureMath.TorrentKnockback); } catch { }
                ApplySpeedToken(enemy, NatureMath.TorrentSlowMult, NatureMath.TorrentSlowSec);
            });
        }

        // Water · Renewal — heal caster + nearby allies, lift morale.
        private static void BattleRenewal(Agent caster, Vec3 pos, Team team)
        {
            try { caster.Health = Math.Min(caster.HealthLimit, caster.Health + NatureMath.RenewalSelfHp); } catch { }
            ForEachAllyInRadius(pos, NatureMath.RenewalRadius, caster, team, ally =>
            {
                try { ally.Health = Math.Min(ally.HealthLimit, ally.Health + NatureMath.RenewalAllyHp); } catch { }
                try { ally.SetMorale(Math.Min(100f, ally.GetMorale() + NatureMath.RenewalMorale)); } catch { }
                try { SpellEffects.SpawnNatureBurst(ally.Position, NatureElement.Water, 1.6f); } catch { }
            });
        }

        // Storm · Thunderclap — bolt to nearest enemy, chaining to a couple more.
        private static void BattleThunderClap(Agent caster, Vec3 pos, Team team)
        {
            Agent primary = NearestEnemy(caster, pos, NatureMath.ThunderRange, team);
            if (primary == null) return;

            ApplyDamage(primary, caster, NatureMath.ThunderDamage, DamageTypes.Invalid);
            try { primary.SetMaximumSpeedLimit(0f, false); } catch { }
            ApplySpeedToken(primary, 0f, NatureMath.ThunderStunSec);
            try { SpellEffects.SpawnTempLightWhite(primary.Position + new Vec3(0f, 0f, 1f), 10f, 0.3f); } catch { }
            try { SpellEffects.SpawnNatureBurst(primary.Position + new Vec3(0f, 0f, 1f), NatureElement.Storm, 1.0f); } catch { }

            int chains = 0;
            ForEachEnemyInRadius(primary.Position, NatureMath.ThunderChainRadius, team, chain =>
            {
                if (chains >= NatureMath.ThunderChainCount || chain == primary) return;
                ApplyDamage(chain, caster, NatureMath.ThunderChainDamage, DamageTypes.Invalid);
                try { SpellEffects.SpawnTempLightWhite(chain.Position + new Vec3(0f, 0f, 1f), 7f, 0.25f); } catch { }
                try { SpellEffects.SpawnNatureBurst(chain.Position + new Vec3(0f, 0f, 1f), NatureElement.Storm, 0.9f); } catch { }
                chains++;
            });
        }

        // Storm · Stormstep — dash forward with a brief speed burst; dust trail.
        private static void BattleStormstep(Agent caster)
        {
            try
            {
                Vec3 fwd  = caster.LookDirection.NormalizedCopy();
                Vec3 dest = caster.Position + fwd * NatureMath.StormstepDist;
                try { SpellEffects.SpawnNatureLine(caster.Position, dest, NatureElement.Storm, 1.0f); } catch { }
                caster.TeleportToPosition(dest);
                ApplySpeedToken(caster, 1.5f, NatureMath.StormstepBurstSec);
            }
            catch { }
        }

        // Element light bloom: Wind pale-white, Earth green, Water blue, Storm flash.
        private static void SpawnElementVisual(NatureElement el, Vec3 pos)
        {
            Vec3 up  = new Vec3(0f, 0f, 0.8f);
            Vec3 up2 = new Vec3(0f, 0f, 1.5f);
            switch (el)
            {
                case NatureElement.Wind:
                    SpellEffects.SpawnTempLightRgb(pos + up2, new Vec3(0.88f, 0.90f, 1.0f), 12f, 0.8f);
                    SpellEffects.SpawnTempLightRgb(pos + up,  new Vec3(0.82f, 0.85f, 1.0f),  8f, 0.5f);
                    break;
                case NatureElement.Earth:
                    SpellEffects.SpawnTempLightRgb(pos + up,  new Vec3(0.15f, 1.0f, 0.15f),  9f, 2.5f);
                    SpellEffects.SpawnTempLightRgb(pos,       new Vec3(0.1f,  0.7f, 0.1f),   5f, 1.5f);
                    break;
                case NatureElement.Water:
                    SpellEffects.SpawnTempLightRgb(pos + up,  new Vec3(0.2f, 0.6f, 1.0f),   10f, 2.5f);
                    SpellEffects.SpawnTempLightRgb(pos,       new Vec3(0.3f, 0.7f, 1.0f),    6f, 1.5f);
                    break;
                case NatureElement.Storm:
                    SpellEffects.SpawnTempLightWhite(pos + up2, 18f, 0.25f);
                    SpellEffects.SpawnTempLightWhite(pos + up,  12f, 0.40f);
                    SpellEffects.SpawnTempLightRgb(pos + up, new Vec3(0.65f, 0.65f, 1.0f), 8f, 1.5f);
                    break;
            }
        }

        // ── Campaign effects ────────────────────────────────────────────────────
        // Support powers help the column; attack powers have no map use.
        private static bool ExecuteCampaign(NaturePower power)
        {
            switch (power)
            {
                case NaturePower.Renewal:   CampaignRenewal();  return true;
                case NaturePower.Tailwind:  CampaignTailwind(); return true;
                case NaturePower.Bulwark:   CampaignBulwark();  return true;
                case NaturePower.Stormstep: CampaignStormstep();return true;
                default:
                    Msg($"{NatureMath.PowerName(power)} — this power only stirs in conflict.", NatureColor);
                    return false;
            }
        }

        private static void CampaignRenewal()
        {
            try
            {
                var roster = MobileParty.MainParty?.MemberRoster;
                if (roster != null)
                {
                    int healed = 0;
                    foreach (var row in roster.GetTroopRoster().ToList())
                    {
                        if (row.WoundedNumber <= 0) continue;
                        int recover = Math.Min(row.WoundedNumber, 6);
                        roster.AddToCounts(row.Character, 0, false, -recover);
                        healed += recover;
                    }
                    if (healed > 0) Msg($"Renewal — {healed} wounded soldiers recover.", NatureColor);
                }
                try { MobileParty.MainParty.RecentEventsMorale += 20f; } catch { }
            }
            catch { }
        }

        private static void CampaignTailwind()
        {
            try { MobileParty.MainParty.RecentEventsMorale += 25f;
                  Msg("Tailwind — your column marches with lighter feet.", NatureColor); } catch { }
        }

        private static void CampaignBulwark()
        {
            // The forest mends the column — a modest restoration of the wounded.
            try
            {
                var roster = MobileParty.MainParty?.MemberRoster;
                if (roster != null)
                {
                    int healed = 0;
                    foreach (var row in roster.GetTroopRoster().ToList())
                    {
                        if (row.WoundedNumber <= 0) continue;
                        int recover = Math.Min(row.WoundedNumber, 4);
                        roster.AddToCounts(row.Character, 0, false, -recover);
                        healed += recover;
                    }
                    Msg(healed > 0
                        ? $"Bulwark — the land knits {healed} of your wounded whole again."
                        : "Bulwark — the ground braces beneath you.", NatureColor);
                }
            }
            catch { }
        }

        private static void CampaignStormstep()
        {
            try { MobileParty.MainParty.RecentEventsMorale += 15f;
                  Msg("Stormstep — a charge in the air; the march quickens.", NatureColor); } catch { }
        }

        // ── Tick ────────────────────────────────────────────────────────────────
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
                else _speedTokens[key] = (remaining, agent);
            }
        }

        private static void TickResistTokens(float dt)
        {
            foreach (int key in _resistTokens.Keys.ToList())
            {
                var (frac, remaining, agent) = _resistTokens[key];
                remaining -= dt;
                if (remaining <= 0f) _resistTokens.Remove(key);
                else _resistTokens[key] = (frac, remaining, agent);
            }
        }

        public static float ApplyResistance(Agent agent, float incomingDamage)
        {
            if (agent == null) return incomingDamage;
            if (!_resistTokens.TryGetValue(agent.Index, out var tok)) return incomingDamage;
            return incomingDamage * (1f - tok.fraction);
        }

        public static bool HasResist(Agent agent)
            => agent != null && _resistTokens.ContainsKey(agent.Index);

        public static void ClearBattleState()
        {
            foreach (var (_, agent) in _speedTokens.Values)
                try { agent?.SetMaximumSpeedLimit(10f, false); } catch { }
            _speedTokens.Clear();
            _resistTokens.Clear();
        }

        // ── Helpers ─────────────────────────────────────────────────────────────
        private static void ApplyDamage(Agent target, Agent source, float amount, DamageTypes dmgType)
        {
            if (target == null || !target.IsActive() || target.Health <= 0f) return;
            try
            {
                target.Health -= amount;
                if (target.Health <= 0f) target.Die(new Blow(source?.Index ?? -1));
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
                    if ((a.Position - pos).LengthSquared <= r2) try { action(a); } catch { }
                }
            }
            catch { }
        }

        private static void ForEachAllyInRadius(Vec3 pos, float radius, Agent exclude, Team team, Action<Agent> action)
        {
            float r2 = radius * radius;
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount || a == exclude || a.Health <= 0f) continue;
                    if (a.Team != team) continue;
                    if ((a.Position - pos).LengthSquared <= r2) try { action(a); } catch { }
                }
            }
            catch { }
        }

        private static readonly Color NatureColor = new Color(0.35f, 0.75f, 0.35f);

        private static void Msg(string text, Color color)
            => InformationManager.DisplayMessage(new InformationMessage(text, color));
    }
}
