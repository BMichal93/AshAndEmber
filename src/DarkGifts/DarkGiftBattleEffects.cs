// =============================================================================
// ASH AND EMBER — DarkGifts/DarkGiftBattleEffects.cs
//
// Battle-side implementations for all Dark Gift passive effects.
// Partial of SpellEffects so it shares DamageAgent / BeginAgentGlowRaw etc.
//
// Ticked from MagicMissionBehavior.OnMissionTick.
// OnAgentHit hooks called from MagicMissionBehavior.OnAgentHit.
// OnAgentRemoved hook called from MagicMissionBehavior.OnAgentRemoved.
// OnAgentBuild hook called from MagicMissionBehavior.OnAgentBuild.
// Spirits are seeded lazily on the first tick after Agent.Main becomes active.
//
// NPC heroes with Dark Gifts (Ashen Lords, evil lords) have their gifts applied
// through the same hit hooks — gifts are checked via DarkGiftSystem.NpcHasGift.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
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
        private const float DarkSpiritSpeed          = 6f;   // m/s
        private const float DarkSpiritDamageInterval = 4f;
        private const float DarkSpiritDamage         = 25f;
        private const float DarkSpiritRange          = 2.5f; // m to deal damage

        // ── HorseKiller ─────────────────────────────────────────────────────────
        private static float _horseKillerCooldown = 0f;
        private const float HorseKillerInterval   = 1f;
        private const float HorseKillerRange2     = 5f * 5f;

        // ── DreadPresence ───────────────────────────────────────────────────────
        private static float _dreadPresenceCooldown = 0f;
        private const float DreadPresenceInterval   = 3f;
        private const float DreadPresenceRange2     = 8f * 8f;
        private const float DreadPresenceMoraleDrain = 20f;

        // ── Persistent player contour ───────────────────────────────────────────
        // Re-applied every ContourRefreshInterval seconds so it survives the
        // glow-timer system clearing it via a timed flash from another gift.
        private static float _playerContourTimer = 0f;
        private const float ContourRefreshInterval = 2f;

        // Dark charcoal-red — unmistakably marked without being distracting.
        private static readonly uint DarkGiftPlayerContour =
            new Color(0.35f, 0f, 0.05f).ToUnsignedInteger();

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

        // Called from OnAgentBuild — applies the persistent dark contour to the
        // player immediately when they enter the battle.
        public static void ApplyDarkGiftAgentBuild(Agent agent)
        {
            if (agent != Agent.Main) return;
            if (!DarkGiftSystem.GiftsActive) return;
            try
            {
                agent.AgentVisuals?.GetEntity()?.SetContourColor(DarkGiftPlayerContour, true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── MissionTick ─────────────────────────────────────────────────────────
        public static void TickDarkGifts(float dt)
        {
            if (!DarkGiftSystem.GiftsActive) return;

            var player = Agent.Main;
            if (player == null || !player.IsActive()) return;

            // Keep the player contour alive — timed flashes from other gifts
            // temporarily overwrite it; re-apply after they expire.
            _playerContourTimer -= dt;
            if (_playerContourTimer <= 0f)
            {
                _playerContourTimer = ContourRefreshInterval;
                try { player.AgentVisuals?.GetEntity()?.SetContourColor(DarkGiftPlayerContour, true); }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }

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
                        // Deep crimson pulse on the struck agent.
                        BeginAgentGlowRaw(spirit.Target, new Color(0.7f, 0f, 0.15f).ToUnsignedInteger(), 0.5f);
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    spirit.DamageCooldown = DarkSpiritDamageInterval;
                    // Do NOT clear Target — keep chasing until it dies.
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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                        BeginAgentGlowRaw(a, new Color(0.5f, 0f, 0f).ToUnsignedInteger(), 0.8f);
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                            try { a.SetMorale(0f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        // Pale desaturated purple flash — fear seeping in.
                        BeginAgentGlowRaw(a, new Color(0.25f, 0f, 0.25f).ToUnsignedInteger(), 0.6f);
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── OnAgentHit (attacker has gifts — player OR NPC lord) ───────────────
        public static void ApplyDarkGiftAttackEffects(Agent victim, Agent attacker, int inflictedDamage, bool isMelee)
        {
            if (attacker == null || victim == null || victim.IsMount) return;
            if (!isMelee) return;

            bool isPlayer = attacker == Agent.Main;
            Hero npcHero  = isPlayer ? null : (attacker.Character as CharacterObject)?.HeroObject;

            // Only proceed if the attacker is the player (gifts active) or a gifted NPC lord.
            if (isPlayer && !DarkGiftSystem.GiftsActive) return;
            if (!isPlayer && npcHero == null) return;

            bool hasDarkStrike = isPlayer
                ? DarkGiftSystem.HasGift(DarkGiftId.DarkStrike)
                : DarkGiftSystem.NpcHasGift(npcHero, DarkGiftId.DarkStrike);

            bool hasSoulDrain = isPlayer
                ? DarkGiftSystem.HasGift(DarkGiftId.SoulDrain)
                : DarkGiftSystem.NpcHasGift(npcHero, DarkGiftId.SoulDrain);

            // DarkStrike — bonus dark damage scaled to the blow (25%) + black-red
            // flash on victim. Scaling off the hit (like Iron Veil / Soul Mirror
            // below) keeps a fast weapon from turning a flat bonus into runaway DPS.
            if (hasDarkStrike && inflictedDamage > 0)
            {
                try
                {
                    DamageAgent(victim, inflictedDamage * 0.25f);
                    BeginAgentGlowRaw(victim, new Color(0.6f, 0f, 0.08f).ToUnsignedInteger(), 0.35f);
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }

            // SoulDrain — morale drain + deep indigo flash on victim
            if (hasSoulDrain)
            {
                try
                {
                    float m = victim.GetMorale();
                    victim.SetMorale(Math.Max(m - 30f, 0f));
                    BeginAgentGlowRaw(victim, new Color(0.1f, 0f, 0.4f).ToUnsignedInteger(), 0.4f);
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        // ── OnAgentHit (victim has gifts — player OR NPC lord) ────────────────
        public static void ApplyDarkGiftDefenseEffects(Agent victim, Agent attacker, int inflictedDamage, bool isMelee)
        {
            if (victim == null || attacker == null) return;

            bool isPlayer = victim == Agent.Main;
            Hero npcHero  = isPlayer ? null : (victim.Character as CharacterObject)?.HeroObject;

            if (isPlayer && !DarkGiftSystem.GiftsActive) return;
            if (!isPlayer && npcHero == null) return;

            bool hasIronVeil = isPlayer
                ? DarkGiftSystem.HasGift(DarkGiftId.IronVeil)
                : DarkGiftSystem.NpcHasGift(npcHero, DarkGiftId.IronVeil);

            bool hasSoulMirror = isPlayer
                ? DarkGiftSystem.HasGift(DarkGiftId.SoulMirror)
                : DarkGiftSystem.NpcHasGift(npcHero, DarkGiftId.SoulMirror);

            // IronVeil — heal back 10% of incoming damage + brief silver flash
            if (hasIronVeil && inflictedDamage > 0)
            {
                try
                {
                    float healBack = inflictedDamage * 0.10f;
                    if (healBack >= 1f)
                    {
                        HealAgent(victim, healBack);
                        BeginAgentGlowRaw(victim, new Color(0.3f, 0.3f, 0.3f).ToUnsignedInteger(), 0.25f);
                    }
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }

            // SoulMirror — reflect 20% of melee damage + purple flash on victim
            if (hasSoulMirror && isMelee && !attacker.IsMount && inflictedDamage > 0)
            {
                try
                {
                    float reflected = inflictedDamage * 0.20f;
                    if (reflected >= 1f)
                    {
                        DamageAgent(attacker, reflected);
                        BeginAgentGlowRaw(victim,   new Color(0.4f, 0f, 0.45f).ToUnsignedInteger(), 0.45f);
                        BeginAgentGlowRaw(attacker, new Color(0.4f, 0f, 0.45f).ToUnsignedInteger(), 0.3f);
                    }
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        // ── OnAgentRemoved (killer has BloodPact — player OR NPC) ─────────────
        public static void ApplyDarkGiftKillEffects(Agent killed, Agent killer)
        {
            if (killer == null) return;

            bool isPlayer = killer == Agent.Main;
            Hero npcHero  = isPlayer ? null : (killer.Character as CharacterObject)?.HeroObject;

            if (isPlayer && !DarkGiftSystem.GiftsActive) return;
            if (!isPlayer && npcHero == null) return;

            bool hasBloodPact = isPlayer
                ? DarkGiftSystem.HasGift(DarkGiftId.BloodPact)
                : DarkGiftSystem.NpcHasGift(npcHero, DarkGiftId.BloodPact);

            if (!hasBloodPact) return;
            if (!killer.IsActive()) return;

            try
            {
                HealAgent(killer, 12f);
                // Crimson flash on the killer — life stolen back.
                BeginAgentGlowRaw(killer, new Color(0.7f, 0f, 0.1f).ToUnsignedInteger(), 0.5f);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Clear / reset ────────────────────────────────────────────────────────
        public static void ClearDarkGiftsBattleState()
        {
            _darkSpirits.Clear();
            _darkSpiritsSeeded      = false;
            _horseKillerCooldown    = 0f;
            _dreadPresenceCooldown  = 0f;
            _playerContourTimer     = 0f;
        }
    }
}
