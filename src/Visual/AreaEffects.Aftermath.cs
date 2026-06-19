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
