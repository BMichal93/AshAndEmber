// =============================================================================
// ASH AND EMBER — DarkGifts/DarkGiftBattleEffects.cs
//
// Battle-side implementations for all Dark Gift passive effects.
// Partial of SpellEffects so it shares DamageAgent / BeginAgentGlowRaw etc.
//
// Ticked from MagicMissionBehavior.OnMissionTick.
// OnAgentHit hooks called from MagicMissionBehavior.OnAgentHit.
// OnAgentRemoved hook called from MagicMissionBehavior.OnAgentRemoved.
// Spirits are seeded lazily on the first tick after Agent.Main becomes active.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static partial class SpellEffects
    {
        // ── Dark Spirit ─────────────────────────────────────────────────────────
        private sealed class DarkSpiritState
        {
            public Vec3    Position;
            public Agent   Target;
            public float   DamageCooldown; // seconds until next damage tick
        }

        private static readonly List<DarkSpiritState> _darkSpirits = new List<DarkSpiritState>();
        private static bool _darkSpiritsSeeded = false;
        private const float DarkSpiritSpeed     = 6f;   // m/s
        private const float DarkSpiritDamageInterval = 4f;
        private const float DarkSpiritDamage    = 25f;
        private const float DarkSpiritRange     = 2.5f; // m to deal damage

        // ── HorseKiller ─────────────────────────────────────────────────────────
        private static float _horseKillerCooldown = 0f;
        private const float HorseKillerInterval  = 1f;
        private const float HorseKillerRange2    = 5f * 5f;

        // ── DreadPresence ───────────────────────────────────────────────────────
        private static float _dreadPresenceCooldown = 0f;
        private const float DreadPresenceInterval  = 3f;
        private const float DreadPresenceRange2    = 8f * 8f;
        private const float DreadPresenceMoraleDrain = 20f;

        // Seeds dark spirits on the first tick where Agent.Main is available.
        private static void EnsureDarkSpiritsSeeded()
        {
            if (_darkSpiritsSeeded) return;

            int spirits = DarkGiftSystem.DarkSpiritCount;
            if (spirits <= 0) { _darkSpiritsSeeded = true; return; }

            var player = Agent.Main;
            if (player == null || !player.IsActive()) return; // retry next tick

            Vec3 origin;
            try { origin = player.Position; } catch { return; }

            _darkSpiritsSeeded = true;
            for (int i = 0; i < spirits; i++)
            {
                float angle = ((float)Math.PI * 2f / spirits) * i;
                _darkSpirits.Add(new DarkSpiritState
                {
                    Position       = origin + new Vec3((float)Math.Cos(angle) * 1.5f, (float)Math.Sin(angle) * 1.5f, 0f),
                    Target         = null,
                    DamageCooldown = DarkSpiritDamageInterval * (i * 0.4f + 1f), // stagger first hits
                });
            }
        }

        // ── MissionTick ─────────────────────────────────────────────────────────
        public static void TickDarkGifts(float dt)
        {
            if (!DarkGiftSystem.GiftsActive) return;

            var player = Agent.Main;
            if (player == null || !player.IsActive()) return;

            if (DarkGiftSystem.HasGift(DarkGiftId.DarkSpirit))
                EnsureDarkSpiritsSeeded();

            TickDarkSpirits(dt, player);

            if (DarkGiftSystem.HasGift(DarkGiftId.HorseKiller))
                TickHorseKiller(dt, player);

            if (DarkGiftSystem.HasGift(DarkGiftId.DreadPresence))
                TickDreadPresence(dt, player);
        }

        private static void TickDarkSpirits(float dt, Agent player)
        {
            if (_darkSpirits.Count == 0) return;

            Mission mission = Mission.Current;
            if (mission == null) return;

            foreach (var spirit in _darkSpirits)
            {
                spirit.DamageCooldown -= dt;

                // Reacquire target from the spirit's own position if it's gone.
                if (spirit.Target == null || !spirit.Target.IsActive() || spirit.Target.Health <= 0f)
                    spirit.Target = FindNearestEnemy(spirit.Position, player.Team);

                if (spirit.Target == null) continue;

                Vec3 targetPos;
                try { targetPos = spirit.Target.Position; } catch { spirit.Target = null; continue; }

                Vec3 delta = targetPos - spirit.Position;
                float dist = delta.Length;

                // Move spirit toward target every tick.
                if (dist > 0.1f)
                {
                    Vec3 dir = delta * (1f / dist);
                    spirit.Position += dir * (DarkSpiritSpeed * dt);
                }

                // Deal damage when within strike range; keep the target until it dies.
                if (spirit.DamageCooldown <= 0f && dist <= DarkSpiritRange + 1f)
                {
                    try
                    {
                        DamageAgent(spirit.Target, DarkSpiritDamage);
                        BeginAgentGlowRaw(spirit.Target, new Color(0.6f, 0f, 0.1f).ToUnsignedInteger(), 0.6f);
                    }
                    catch { }
                    spirit.DamageCooldown = DarkSpiritDamageInterval;
                    // Do NOT clear Target here — keep chasing until it dies or flees range.
                }
            }
        }

        private static Agent FindNearestEnemy(Vec3 from, Team playerTeam)
        {
            Mission mission = Mission.Current;
            if (mission == null) return null;

            Agent nearest = null;
            float bestDist2 = float.MaxValue;
            try
            {
                foreach (Agent a in mission.AllAgents)
                {
                    if (a == null || !a.IsActive() || a.Health <= 0f) continue;
                    if (a.IsMount) continue;
                    if (playerTeam != null && !playerTeam.IsEnemyOf(a.Team)) continue;
                    Vec3 ap;
                    try { ap = a.Position; } catch { continue; }
                    float d2 = (ap - from).LengthSquared;
                    if (d2 < bestDist2) { bestDist2 = d2; nearest = a; }
                }
            }
            catch { }
            return nearest;
        }

        private static void TickHorseKiller(float dt, Agent player)
        {
            _horseKillerCooldown -= dt;
            if (_horseKillerCooldown > 0f) return;
            _horseKillerCooldown = HorseKillerInterval;

            Vec3 pos;
            try { pos = player.Position; } catch { return; }

            Mission mission = Mission.Current;
            if (mission == null) return;

            try
            {
                foreach (Agent a in mission.AllAgents.ToList())
                {
                    if (a == null || !a.IsActive() || !a.IsMount || a.Health <= 0f) continue;
                    Vec3 ap;
                    try { ap = a.Position; } catch { continue; }
                    if ((ap - pos).LengthSquared > HorseKillerRange2) continue;
                    try
                    {
                        DamageAgent(a, a.Health + 10f); // kill
                        BeginAgentGlowRaw(a, new Color(0.4f, 0f, 0f).ToUnsignedInteger(), 1f);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void TickDreadPresence(float dt, Agent player)
        {
            _dreadPresenceCooldown -= dt;
            if (_dreadPresenceCooldown > 0f) return;
            _dreadPresenceCooldown = DreadPresenceInterval;

            Vec3 pos;
            try { pos = player.Position; } catch { return; }

            Mission mission = Mission.Current;
            if (mission == null) return;

            try
            {
                foreach (Agent a in mission.AllAgents.ToList())
                {
                    if (a == null || !a.IsActive() || a.IsMount || a.Health <= 0f) continue;
                    if (player.Team != null && !player.Team.IsEnemyOf(a.Team)) continue;
                    Vec3 ap;
                    try { ap = a.Position; } catch { continue; }
                    if ((ap - pos).LengthSquared > DreadPresenceRange2) continue;
                    try
                    {
                        float m = a.GetMorale();
                        a.SetMorale(Math.Max(m - DreadPresenceMoraleDrain, 0f));
                        if (a.GetMorale() < 15f)
                            try { a.SetMorale(0f); } catch { }
                    }
                    catch { }
                }
            }
            catch { }
        }

        // ── OnAgentHit (player attacking) ──────────────────────────────────────
        public static void ApplyDarkGiftAttackEffects(Agent victim, Agent attacker, int inflictedDamage, bool isMelee)
        {
            if (attacker != Agent.Main) return;
            if (!DarkGiftSystem.GiftsActive) return;
            if (!isMelee) return;
            if (victim == null || victim.IsMount) return;

            // DarkStrike — bonus dark damage
            if (DarkGiftSystem.HasGift(DarkGiftId.DarkStrike))
            {
                try
                {
                    DamageAgent(victim, 20f);
                    BeginAgentGlowRaw(victim, new Color(0.5f, 0f, 0.05f).ToUnsignedInteger(), 0.4f);
                }
                catch { }
            }

            // SoulDrain — morale drain on victim
            if (DarkGiftSystem.HasGift(DarkGiftId.SoulDrain))
            {
                try
                {
                    float m = victim.GetMorale();
                    victim.SetMorale(Math.Max(m - 30f, 0f));
                }
                catch { }
            }
        }

        // ── OnAgentHit (player being attacked) ─────────────────────────────────
        public static void ApplyDarkGiftDefenseEffects(Agent victim, Agent attacker, int inflictedDamage, bool isMelee)
        {
            if (victim != Agent.Main) return;
            if (!DarkGiftSystem.GiftsActive) return;

            // IronVeil — reduce incoming damage by healing back 10%
            if (DarkGiftSystem.HasGift(DarkGiftId.IronVeil) && inflictedDamage > 0)
            {
                try
                {
                    float healBack = inflictedDamage * 0.10f;
                    if (healBack >= 1f) HealAgent(victim, healBack);
                }
                catch { }
            }

            // SoulMirror — reflect 20% of melee damage
            if (DarkGiftSystem.HasGift(DarkGiftId.SoulMirror) && isMelee
                && attacker != null && !attacker.IsMount && inflictedDamage > 0)
            {
                try
                {
                    float reflected = inflictedDamage * 0.20f;
                    if (reflected >= 1f)
                    {
                        DamageAgent(attacker, reflected);
                        BeginAgentGlowRaw(victim, new Color(0.3f, 0f, 0.3f).ToUnsignedInteger(), 0.5f);
                    }
                }
                catch { }
            }
        }

        // ── OnAgentRemoved (player kill) ────────────────────────────────────────
        public static void ApplyDarkGiftKillEffects(Agent killed)
        {
            if (!DarkGiftSystem.GiftsActive) return;
            if (!DarkGiftSystem.HasGift(DarkGiftId.BloodPact)) return;

            var player = Agent.Main;
            if (player == null || !player.IsActive()) return;

            try { HealAgent(player, 12f); } catch { }
        }

        // ── Clear / reset ────────────────────────────────────────────────────────
        public static void ClearDarkGiftsBattleState()
        {
            _darkSpirits.Clear();
            _darkSpiritsSeeded     = false;
            _horseKillerCooldown   = 0f;
            _dreadPresenceCooldown = 0f;
        }
    }
}
