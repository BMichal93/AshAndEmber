// =============================================================================
// ASH AND EMBER — Magic/ElementSpellEffects.cs
//
// Battle effects for the unified elemental magic. Each element has an ATTACK
// (released with the attack input) and a WALL (released with block).
//
//   Fire   — cone of fire          / wall of fire
//   Wind   — wind blast (Gale)      / wall of wind  (blocks missiles, slows)   ┐ reuse the
//   Earth  — burst of earth         / stone wall (Thornwall)                    │ existing
//   Water  — slowing wave (Torrent) / mist wall                                 ┘ nature effects
//   Spirit — panic men & horses,    / wall that lifts allies' morale and
//            and issue a random       mends them a little
//            order
//
// Wind/Earth/Water reuse NatureEffects (un-gated NPC path) so the nice existing
// visuals carry over; Fire and Spirit are implemented here. Aging and the
// free-hand / weight / Steel gates are handled by ElementMagicInput, so these
// methods just apply the effect.
// =============================================================================

using System;
using System.Linq;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static class ElementSpellEffects
    {
        private static readonly Random _rng = new Random();

        // ── Magnitudes ──────────────────────────────────────────────────────────
        private const float FireConeRange   = 9f;
        private const float FireConeDot      = 0.5f;   // ~60° half-cone
        private const float FireConeDamage   = 38f;
        private const float FireWallRange    = 6f;
        private const float FireWallWidth    = 4f;
        private const float FireWallDamage   = 30f;
        private const float SpiritRadius     = 9f;
        private const float SpiritFearSlow   = 0.55f;  // panicked enemies slow to this
        private const float SpiritFearSec    = 6f;
        private const float SpiritMorale     = 12f;    // ally party morale on the wall
        private const float SpiritHealFrac   = 0.18f;  // ally heal fraction
        private const int   SpiritRadiusInt  = 9;

        // ── Public dispatch ─────────────────────────────────────────────────────
        public static void CastAttack(MagicElement el, Agent caster)
        {
            if (caster == null || !caster.IsActive()) return;
            switch (el)
            {
                case MagicElement.Fire:   FireCone(caster);  break;
                case MagicElement.Wind:   NatureEffects.ExecuteNpc(NaturePower.Gale,     caster, caster.Team); break;
                case MagicElement.Earth:  NatureEffects.ExecuteNpc(NaturePower.Entangle, caster, caster.Team); break;
                case MagicElement.Water:  NatureEffects.ExecuteNpc(NaturePower.Torrent,  caster, caster.Team); break;
                case MagicElement.Spirit: SpiritPanic(caster); break;
            }
            CastFlash(el, caster);
            try { SpellEffects.RecordMagicCast(caster.Position); } catch { }
        }

        public static void CastWall(MagicElement el, Agent caster)
        {
            if (caster == null || !caster.IsActive()) return;
            switch (el)
            {
                case MagicElement.Fire:   FireWall(caster);  break;
                case MagicElement.Wind:   NatureEffects.ExecuteNpc(NaturePower.Windwall,  caster, caster.Team); break;
                case MagicElement.Earth:  NatureEffects.ExecuteNpc(NaturePower.Thornwall, caster, caster.Team); break;
                case MagicElement.Water:  NatureEffects.ExecuteNpc(NaturePower.Mistwall,  caster, caster.Team); break;
                case MagicElement.Spirit: SpiritWall(caster); break;
            }
            CastFlash(el, caster);
            try { SpellEffects.RecordMagicCast(caster.Position); } catch { }
        }

        // ── Fire ────────────────────────────────────────────────────────────────
        // A cone of fire scorches everything the caster faces.
        private static void FireCone(Agent caster)
        {
            Vec3 pos; Vec3 fwd;
            try { pos = caster.Position; fwd = caster.LookDirection; fwd.z = 0f; fwd.Normalize(); }
            catch { return; }
            bool ashen = CasterAshen(caster);
            Vec3 rgb = Palette(MagicElement.Fire, ashen);
            int hit = 0;
            foreach (Agent a in EnemiesNear(caster, FireConeRange))
            {
                Vec3 to = a.Position - pos; to.z = 0f;
                float len = to.Length; if (len < 0.01f) continue;
                if (Vec3.DotProduct(fwd, to * (1f / len)) < FireConeDot) continue;   // outside the cone
                if (SpellEffects.IsWarded(a)) continue;
                try { SpellEffects.DamageAgent(a, FireConeDamage, ColorSchool.Red, caster); } catch { }
                SpawnLight(a.Position, rgb, 0.9f);
                hit++;
            }
            SpawnLight(pos + fwd * (FireConeRange * 0.5f), rgb, 2.2f);
            try { SpellEffects.BeginAgentGlow(caster, GlowSchool(MagicElement.Fire, ashen), 1.5f); } catch { }
        }

        // A wall of fire just ahead — burns those who stand in its line.
        private static void FireWall(Agent caster)
        {
            Vec3 pos; Vec3 fwd;
            try { pos = caster.Position; fwd = caster.LookDirection; fwd.z = 0f; fwd.Normalize(); }
            catch { return; }
            bool ashen = CasterAshen(caster);
            Vec3 rgb = Palette(MagicElement.Fire, ashen);
            Vec3 centre = pos + fwd * FireWallRange;
            Vec3 right  = new Vec3(fwd.y, -fwd.x, 0f);
            for (float f = -FireWallWidth; f <= FireWallWidth; f += 1.5f)
                SpawnLight(centre + right * f, rgb, 1.4f);
            foreach (Agent a in EnemiesNear(caster, FireWallRange + FireWallWidth))
            {
                Vec3 d = a.Position - centre; d.z = 0f;
                if (d.Length > FireWallWidth + 1.5f) continue;
                if (SpellEffects.IsWarded(a)) continue;
                try { SpellEffects.DamageAgent(a, FireWallDamage, ColorSchool.Red, caster); } catch { }
            }
            try { SpellEffects.BeginAgentGlow(caster, GlowSchool(MagicElement.Fire, ashen), 1.5f); } catch { }
        }

        // ── Spirit ────────────────────────────────────────────────────────────────
        // The attack strikes fear into men and horses near the caster's foes and
        // shouts a random order into the enemy ranks (a brief command that scatters
        // their order). Mounts bolt; men falter.
        private static void SpiritPanic(Agent caster)
        {
            Vec3 pos; try { pos = caster.Position; } catch { return; }
            bool ashen = CasterAshen(caster);
            Vec3 rgb = Palette(MagicElement.Spirit, ashen);
            int struck = 0;
            foreach (Agent a in EnemiesNear(caster, SpiritRadius))
            {
                // Panic: slow them and, if mounted, make the horse bolt off-line.
                try { NatureEffects.ApplySpeedToken(a, SpiritFearSlow, SpiritFearSec); } catch { }
                try { a.MakeVoice(SkinVoiceManager.VoiceType.Fear, SkinVoiceManager.CombatVoiceNetworkPredictionType.NoPrediction); } catch { }
                try
                {
                    if (a.MountAgent != null && a.MountAgent.IsActive())
                    {
                        Vec3 dir = (a.Position - pos); dir.z = 0f;
                        if (dir.Length > 0.1f) dir.Normalize(); else dir = new Vec3(1, 0, 0);
                        a.MountAgent.TeleportToPosition(a.MountAgent.Position + dir * 2.0f);
                        try { a.MountAgent.MakeVoice(SkinVoiceManager.VoiceType.Fear, SkinVoiceManager.CombatVoiceNetworkPredictionType.NoPrediction); } catch { }
                    }
                }
                catch { }
                SpawnLight(a.Position, rgb, 0.8f);
                struck++;
            }
            IssueRandomEnemyOrder(caster);
            SpawnLight(pos, rgb, 2.0f);
            try { SpellEffects.BeginAgentGlow(caster, GlowSchool(MagicElement.Spirit, ashen), 1.5f); } catch { }
        }

        // The wall lifts the courage of nearby allies and mends them a little.
        private static void SpiritWall(Agent caster)
        {
            Vec3 pos; try { pos = caster.Position; } catch { return; }
            bool ashen = CasterAshen(caster);
            Vec3 rgb = Palette(MagicElement.Spirit, ashen);
            try { MobileParty.MainParty.RecentEventsMorale += SpiritMorale; } catch { }
            int blessed = 0;
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount) continue;
                    if (caster.Team == null || a.Team != caster.Team) continue;
                    float dx = a.Position.x - pos.x, dy = a.Position.y - pos.y;
                    if (dx * dx + dy * dy > SpiritRadius * SpiritRadius) continue;
                    try { SpellEffects.HealAgent(a, SafeLimit(a) * SpiritHealFrac); } catch { }
                    blessed++;
                }
            }
            catch { }
            SpawnLight(pos, rgb, 2.2f);
            try { SpellEffects.BeginAgentGlow(caster, GlowSchool(MagicElement.Spirit, ashen), 2.5f); } catch { }
        }

        // Shouts a random order into a random enemy formation — charge, fall back, or
        // advance — breaking their coordination for a beat.
        private static void IssueRandomEnemyOrder(Agent caster)
        {
            try
            {
                var mission = Mission.Current;
                if (mission == null || caster.Team == null) return;
                var enemyTeams = mission.Teams.Where(t => t != null && t.IsValid && t.IsEnemyOf(caster.Team)).ToList();
                if (enemyTeams.Count == 0) return;
                var team = enemyTeams[_rng.Next(enemyTeams.Count)];
                var forms = team.FormationsIncludingEmpty.Where(f => f != null && f.CountOfUnits > 0).ToList();
                if (forms.Count == 0) return;
                var form = forms[_rng.Next(forms.Count)];
                switch (_rng.Next(3))
                {
                    case 0: form.SetMovementOrder(MovementOrder.MovementOrderRetreat); break;
                    case 1: form.SetMovementOrder(MovementOrder.MovementOrderCharge);  break;
                    default: form.SetMovementOrder(MovementOrder.MovementOrderAdvance); break;
                }
            }
            catch { }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────
        private static System.Collections.Generic.IEnumerable<Agent> EnemiesNear(Agent caster, float radius)
        {
            float r2 = radius * radius;
            Vec3 pos; try { pos = caster.Position; } catch { yield break; }
            System.Collections.Generic.List<Agent> agents;
            try { agents = Mission.Current.Agents.ToList(); } catch { yield break; }
            foreach (Agent a in agents)
            {
                if (a == caster || !a.IsActive() || a.IsMount) continue;
                if (caster.Team != null && a.Team == caster.Team) continue;
                float dx = a.Position.x - pos.x, dy = a.Position.y - pos.y;
                if (dx * dx + dy * dy <= r2) yield return a;
            }
        }

        private static float SafeLimit(Agent a)
        {
            try { return a.HealthLimit > 0f ? a.HealthLimit : 100f; } catch { return 100f; }
        }

        // ── Per-element visuals (the Ashen wear the cold mask) ─────────────────────
        // Each element has a living colour and a cold Ashen colour:
        //   Fire→Cold · Wind→Storm · Earth→Ash · Water→Snow · Spirit→Void.
        private static Vec3 Palette(MagicElement el, bool ashen)
        {
            if (ashen)
            {
                switch (el)
                {
                    case MagicElement.Fire:   return new Vec3(0.55f, 0.78f, 1.00f); // Cold — pale blue-white
                    case MagicElement.Wind:   return new Vec3(0.40f, 0.45f, 0.62f); // Storm — slate
                    case MagicElement.Earth:  return new Vec3(0.55f, 0.54f, 0.56f); // Ash — grey
                    case MagicElement.Water:  return new Vec3(0.88f, 0.94f, 1.00f); // Snow — white
                    default:                  return new Vec3(0.32f, 0.18f, 0.45f); // Void — deep violet
                }
            }
            switch (el)
            {
                case MagicElement.Fire:   return new Vec3(1.00f, 0.45f, 0.12f); // flame
                case MagicElement.Wind:   return new Vec3(0.70f, 0.95f, 0.92f); // pale gale
                case MagicElement.Earth:  return new Vec3(0.50f, 0.40f, 0.20f); // loam
                case MagicElement.Water:  return new Vec3(0.30f, 0.55f, 0.95f); // deep blue
                default:                  return new Vec3(0.55f, 0.40f, 0.70f); // Spirit — violet
            }
        }

        private static ColorSchool GlowSchool(MagicElement el, bool ashen)
            => ashen ? ColorSchool.Ashen
                     : el == MagicElement.Fire ? ColorSchool.Red : ColorSchool.Nature;

        // Is the caster drawing on the cold? The player by their Ashen state; an NPC
        // lord by the registry.
        private static bool CasterAshen(Agent caster)
        {
            try
            {
                if (caster == Agent.Main) return MageKnowledge.IsAshen;
                var hero = (caster?.Character as TaleWorlds.CampaignSystem.CharacterObject)?.HeroObject;
                return hero != null && ColourLordRegistry.IsAshenLord(hero);
            }
            catch { return false; }
        }

        // A single signature light in the element's (Ashen-aware) colour at the
        // caster — gives the nature-routed Wind/Earth/Water casts their cold mask too.
        private static void CastFlash(MagicElement el, Agent caster)
        {
            try { SpawnLight(caster.Position, Palette(el, CasterAshen(caster)), 2.2f); } catch { }
        }

        private static void SpawnLight(Vec3 pos, Vec3 rgb, float scale)
        {
            try { SpellEffects.SpawnTempLightRgb(pos + new Vec3(0f, 0f, 1f), rgb, 7f * scale, 0.7f); } catch { }
        }
    }
}
