// =============================================================================
// ASH AND EMBER — Spells/SpellEffects.Siege.cs
//
// Fire against TIMBER: siege engines and castle gates are wooden, and the
// fire (or the Ashen cold, which splits the frozen grain just as surely)
// treats them accordingly. Battering rams, siege towers, the throwing
// machines and gates all carry a DestructableComponent — the same hit-point
// pool a catapult stone chews through — so a fire cone scorches them and a
// standing burn gnaws at them for as long as it burns.
//
// Routed through DestructableComponent.TriggerOnHit so the engine runs its own
// destruction states (verified against the game DLLs); if that path faults,
// the hit-point setter is the degraded fallback. Stone wall segments are NOT
// touched — only SiegeWeapon and CastleGate machines burn.
// =============================================================================

using System;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static partial class SpellEffects
    {
        private static float _timberMsgCooldown;

        // Damages every wooden machine (siege weapon / gate) within `radius` of
        // `pos`. Returns how many were struck. `attacker` may be null (a standing
        // burn owns no hand); kill credit falls back to the player agent.
        internal static int DamageBurnableStructures(Vec3 pos, float radius, float damage, Agent attacker)
        {
            if (Mission.Current == null || damage <= 0f) return 0;
            int struck = 0;
            float r2 = radius * radius;
            try
            {
                foreach (var mo in Mission.Current.ActiveMissionObjects)
                {
                    UsableMachine um = mo as UsableMachine;
                    if (um == null) continue;
                    if (!(um is SiegeWeapon) && !(um is CastleGate)) continue;

                    DestructableComponent dc = null;
                    Vec3 at;
                    try
                    {
                        if (!um.IsDestructible) continue;
                        dc = um.DestructionComponent;
                        if (dc == null || dc.IsDestroyed) continue;
                        at = um.GameEntity.GlobalPosition;
                    }
                    catch { continue; }

                    float dx = at.x - pos.x, dy = at.y - pos.y;
                    if (dx * dx + dy * dy > r2) continue;

                    Agent hand = attacker;
                    if (hand == null || !hand.IsActive())
                        try { hand = Agent.Main != null && Agent.Main.IsActive() ? Agent.Main : null; } catch { hand = null; }

                    if (hand != null)
                    {
                        try
                        {
                            Vec3 dir = at - pos; dir.z = 0f;
                            if (dir.Length > 0.1f) dir.Normalize(); else dir = new Vec3(1f, 0f, 0f);
                            MissionWeapon noWeapon = MissionWeapon.Invalid;
                            dc.TriggerOnHit(hand, (int)damage, at, dir, ref noWeapon, -1, null);
                            try { dc.BurstHeavyHitParticles(); } catch { }
                        }
                        catch
                        {
                            // Degraded path: chew the hit points directly.
                            try { dc.HitPoint = Math.Max(0f, dc.HitPoint - damage); } catch { continue; }
                        }
                    }
                    else
                    {
                        // No living hand to attribute the hit to — NEVER hand the
                        // engine a null attacker. The fire still chews the timber,
                        // but leaves the last splinter: destruction (and its state
                        // machinery) only ever triggers from an attributed hit.
                        try { dc.HitPoint = Math.Max(1f, dc.HitPoint - damage); } catch { continue; }
                    }
                    try { SpawnTempFireParticle(at + new Vec3(0f, 0f, 1.0f), 2f); } catch { }
                    struck++;
                }
            }
            catch { }

            // Self-healing throttle: mission time restarts each battle, so a stale
            // high watermark from the previous fight resets itself.
            if (_timberMsgCooldown > MissionTimeNow() + 30f) _timberMsgCooldown = 0f;
            if (struck > 0 && attacker != null && attacker == Agent.Main
                && MissionTimeNow() > _timberMsgCooldown)
            {
                _timberMsgCooldown = MissionTimeNow() + 5f;
                try
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "The fire takes to the timber — the machine chars and groans.",
                        new Color(0.9f, 0.5f, 0.2f)));
                }
                catch { }
            }
            return struck;
        }

        private static float MissionTimeNow()
        {
            try { return Mission.Current?.CurrentTime ?? 0f; } catch { return 0f; }
        }
    }
}
