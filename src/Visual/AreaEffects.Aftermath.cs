// =============================================================================
// ASH AND EMBER — AreaEffects.Aftermath.cs
// Spell aftermath helpers.
// Partial of SpellEffects (shared state lives in AreaEffects.cs).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Engine;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using TaleWorlds.CampaignSystem.MapEvents;

namespace AshAndEmber
{
    public static partial class SpellEffects
    {
        // ── Spell aftermath helpers ────────────────────────────────────────────
        // Called from ExplodeMissile (when DamageCount > 0) — a patch of fire lingers
        // at the explosion point, damaging enemies who walk through it.
        internal static void SpawnFirePatch(Vec3 pos, int damageCount, Team casterTeam)
        {
            var node = new AreaEffect
            {
                Id           = "spell_firepatch",
                School       = ColorSchool.Red,
                Position     = pos,
                Radius       = 3f,
                TickInterval = 1f,
                TickTimer    = 1f,
                Remaining    = 8f,
                Power        = damageCount * 8f,
                CasterTeam   = casterTeam,
            };
            node.LightEntity = SpawnAreaLight(pos, ColorSchool.Red, 5f);
            _areaEffects.Add(node);
            SpawnTempFireParticle(pos, 8f);
        }

        // Called from the elemental Fire wall — lays a line of lingering burning
        // patches across the wall so it keeps scorching enemies who hold the line.
        // `perTickDamage` is already power-scaled by the caller. Three patches span
        // the wall with minimal overlap; each reuses the spell_firepatch tick.
        internal static void SpawnFireWallPatches(Vec3 centre, Vec3 right, float halfWidth,
                                                  float perTickDamage, float duration, Team casterTeam)
        {
            for (float f = -halfWidth; f <= halfWidth + 0.01f; f += halfWidth)
            {
                Vec3 p = centre + right * f;
                var node = new AreaEffect
                {
                    Id           = "spell_firepatch",
                    School       = ColorSchool.Red,
                    Position     = p,
                    Radius       = 2.2f,
                    TickInterval = 1f,
                    TickTimer    = 1f,
                    Remaining    = duration,
                    Power        = perTickDamage,
                    CasterTeam   = casterTeam,
                };
                node.LightEntity = SpawnAreaLight(p, ColorSchool.Red, 5f);
                _areaEffects.Add(node);
            }
        }

        // Water meeting broken earth churns the ground to MUD — a bogging patch
        // that slows everything crossing it (cavalry worst: the horse wades too).
        // Impartial like the walls themselves: CasterTeam is null on purpose.
        internal static void SpawnMudPatch(Vec3 pos)
        {
            var node = new AreaEffect
            {
                Id           = "spell_mudpatch",
                School       = ColorSchool.Nature,
                Position     = pos,
                Radius       = 3.5f,
                TickInterval = 0.5f,
                TickTimer    = 0.5f,
                Remaining    = 10f,
                CasterTeam   = null,     // mud bogs friend and foe alike
            };
            node.LightEntity = SpawnAreaLightRaw(pos + new Vec3(0f, 0f, 0.4f),
                                                 new Vec3(0.42f, 0.32f, 0.18f), 5f);
            _areaEffects.Add(node);
            try { SpawnNatureBurst(pos, NatureElement.Water, 1.2f); } catch { }
            try { SpawnNatureBurst(pos, NatureElement.Earth, 1.2f); } catch { }
        }

        // Called from ExecuteBurstFromAgent (when RestoreCount > 0 and player is caster) —
        // a consecrated zone lingers at the burst centre, slowly healing allies.
        internal static void SpawnHolyZone(Vec3 pos, int restoreCount, float radius, Team casterTeam)
        {
            var node = new AreaEffect
            {
                Id           = "spell_holyzone",
                School       = ColorSchool.White,
                Position     = pos,
                Radius       = Math.Max(3f, radius),
                TickInterval = 1f,
                TickTimer    = 1f,
                Remaining    = 5f,
                Power        = restoreCount * 8f,
                CasterTeam   = casterTeam,
            };
            node.LightEntity = SpawnAreaLight(pos, ColorSchool.White, Math.Max(3f, radius));
            _areaEffects.Add(node);
        }

        // Called from ExecuteBurstFromAgent (UsingDirge) — fire smoulders in the earth
        // for 12 seconds, burning enemies who enter the radius each second.
        internal static void SpawnDirgePatch(Vec3 pos, int damageCount, float radius, Team casterTeam)
        {
            var node = new AreaEffect
            {
                Id           = "spell_dirge",
                School       = ColorSchool.Red,
                Position     = pos,
                Radius       = Math.Max(3f, radius),
                TickInterval = 1f,
                TickTimer    = 1f,
                Remaining    = 12f,
                Power        = damageCount * 8f,
                CasterTeam   = casterTeam,
            };
            node.LightEntity = SpawnAreaLight(pos, ColorSchool.Red, Math.Max(5f, radius));
            _areaEffects.Add(node);
            SpawnTempFireParticle(pos, 12f);
        }

        public static void ClearAreaEffects()
        {
            // Drop the barrier pulse's agent snapshot — it must never carry Agent
            // references from one mission into the next.
            _barrierAgentSnapshot = null;
            _barrierSnapshotTime  = -1f;
            foreach (var e in _areaEffects)
            {
                try { e.LightEntity?.Remove(0); } catch { }
                try { e.LightEntity2?.Remove(0); } catch { }
                try { e.LightEntity3?.Remove(0); } catch { }
            }
            foreach (var kvp in _haltedAgents)
            {
                try
                {
                    Agent agent = kvp.Value.Source;
                    if (agent?.IsActive() == true && agent.Health > 0f)
                    {
                        bool usingEquip = false;
                        try { usingEquip = agent.IsUsingGameObject; } catch { }
                        if (!usingEquip)
                            agent.SetMaximumSpeedLimit(10f, false);
                    }
                }
                catch { }
            }
            _areaEffects.Clear();
            _haltedAgents.Clear();
            _haltTeleportTimer = 0f;
            ClearMissile();
        }
    }
}
