// =============================================================================
// LIFE & DEATH MAGIC — CreateSpells.cs
// BARRIER FORM: wall of nodes in front of caster, 1 node per R input.
// BURST FORM   : instant circle around caster, 2m radius per D input.
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
        private const string BarrierId = "spell_barrier";

        // ── BARRIER ───────────────────────────────────────────────────────────
        public static bool ExecuteBarrier(SpellCast cast)
        {
            Agent caster = Agent.Main;
            if (caster == null || !caster.IsActive()) return false;

            // Toggle off — dispelling costs no aging days
            if (HasAreaEffect(BarrierId))
            {
                RemoveAreaEffect(BarrierId);
                InformationManager.DisplayMessage(new InformationMessage(
                    "Barrier released.", new Color(0.7f, 0.7f, 0.7f)));
                return false;
            }

            Vec3 fwd   = caster.LookDirection.NormalizedCopy();
            Vec3 right = new Vec3(-fwd.y, fwd.x, 0f).NormalizedCopy();
            int  count = Math.Max(1, cast.FormCount);

            for (int i = 0; i < count; i++)
            {
                // Spread nodes left to right across the forward direction
                float offset = (i - (count - 1) * 0.5f) * 1.5f; // 1.5m = hit radius → solid wall, no gaps
                Vec3 pos = caster.Position + fwd * 3f + right * offset;
                AddBarrierNode(pos, cast, caster.Team);
            }

            ColorSchool col = cast.VisualColor;
            SpawnConeLights(caster.Position, fwd, col, 3f);
            TryCastSound(caster.Position, col);
            TryCastAnimation(caster);

            InformationManager.DisplayMessage(new InformationMessage(
                $"Barrier — {cast.EffectSummary()}. Cast again to release.",
                ColorSchoolData.GetMessageColor(col)));
            return true;
        }

        private static void AddBarrierNode(Vec3 pos, SpellCast cast, Team casterTeam)
        {
            float token = cast.DamageCount * 1000f + cast.PushCount * 100f
                        + cast.MoraleCount * 10f   + (cast.Reversed ? 1f : 0f);
            var node = new AreaEffect
            {
                Id           = BarrierId,
                School       = cast.VisualColor,
                Position     = pos,
                Radius       = 1.5f,
                TickInterval = 0.5f,
                TickTimer    = 0.5f,
                Remaining    = -1f,
                Power        = token,
                CasterTeam   = casterTeam
            };
            node.LightEntity = SpawnAreaLight(node.Position, cast.VisualColor, 12f);
            // Column of fire lights visible immediately when the barrier goes up
            SpawnTempLight(pos,                          cast.VisualColor, 10f, 6f);
            SpawnTempLight(pos + new Vec3(0f, 0f, 1f),  cast.VisualColor, 10f, 6f);
            SpawnTempLight(pos + new Vec3(0f, 0f, 2f),  cast.VisualColor, 10f, 6f);
            if (cast.VisualColor != ColorSchool.Blight)
            {
                SpawnTempFireParticle(pos,                          6f);
                SpawnTempFireParticle(pos + new Vec3(0f, 0f, 1f),  6f);
                SpawnTempFireParticle(pos + new Vec3(0f, 0f, 2f),  6f);
            }
            _areaEffects.Add(node);
        }

        // Called from AreaEffects.cs tick (every 2 s per node)
        // Barrier lives indefinitely (Remaining = -1) until cast again.
        internal static void TickBarrierNode(AreaEffect e)
        {
            if (Mission.Current == null) return;

            // Column of fire at three heights every tick; 3.5 s duration gives solid overlap beyond the 2 s tick.
            SpawnTempLight(e.Position,                          e.School, 10f, 3.5f);
            SpawnTempLight(e.Position + new Vec3(0f, 0f, 1f),  e.School, 10f, 3.5f);
            SpawnTempLight(e.Position + new Vec3(0f, 0f, 2f),  e.School, 10f, 3.5f);
            if (e.School != ColorSchool.Blight)
            {
                SpawnTempFireParticle(e.Position,                          3f);
                SpawnTempFireParticle(e.Position + new Vec3(0f, 0f, 1f),  3f);
                SpawnTempFireParticle(e.Position + new Vec3(0f, 0f, 2f),  3f);
            }
            int token   = (int)e.Power;
            int dmg     = token / 1000;
            int push    = (token % 1000) / 100;
            int morale  = (token % 100) / 10;
            bool rev    = (token % 10) == 1;

            var cast = new SpellCast { DamageCount = dmg, PushCount = push, MoraleCount = morale, Reversed = rev };
            Agent src = Agent.Main;

            foreach (Agent a in Mission.Current.Agents.ToList())
            {
                if (!a.IsActive() || a.IsMount) continue;

                float dist = a.Position.Distance(e.Position);

                if (!a.IsHero && dist > e.Radius && dist < e.Radius + 3f)
                {
                    bool isMounted = false;
                    try { isMounted = a.MountAgent != null; } catch { }
                    if (!isMounted)
                    {
                        Vec3 nudge = a.Position - e.Position;
                        if (nudge.Length < 0.01f) nudge = new Vec3(1f, 0f, 0f);
                        else nudge = nudge.NormalizedCopy();
                        Vec3 dest = a.Position + nudge * 1.5f;
                        dest.z = a.Position.z;
                        try { QueueMove(a, dest, 0.35f); } catch { }
                    }
                }

                bool isAlly = e.CasterTeam != null && a.Team == e.CasterTeam;
                // Normal barrier: damage enemies only.
                // Reversed barrier: heal/buff allies only (player included).
                if (rev ? !isAlly : isAlly) continue;
                if (dist > e.Radius) continue;
                try
                {
                    uint raw = rev
                        ? ColorSchoolData.GetReversedGlowColor(e.School)
                        : ColorSchoolData.GetGlowColor(e.School);
                    BeginAgentGlowRaw(a, raw, 1.5f);
                    if (cast.DamageCount > 0)
                    {
                        // 1f per 0.5s tick = same DPS as the old 4f per 2s tick
                        float amt = cast.DamageCount * 1f;
                        if (rev) HealAgent(a, amt); else DamageAgent(a, amt);
                    }
                    if (cast.MoraleCount > 0)
                    {
                        // 1.25f per 0.5s tick = same rate as old 5f per 2s tick
                        float delta = cast.MoraleCount * 1.25f;
                        float cur   = a.GetMorale();
                        a.SetMorale(rev ? Math.Min(cur + delta, 100f) : Math.Max(cur - delta, 0f));
                    }
                    if (cast.PushCount > 0 && src != null)
                    {
                        bool isMounted = false;
                        try { isMounted = a.MountAgent != null; } catch { }
                        if (!isMounted)
                        {
                            float pushDist = cast.PushCount * 2f;
                            Vec3 dir = rev
                                ? (src.Position - a.Position).NormalizedCopy()
                                : (a.Position - e.Position).NormalizedCopy();
                            Vec3 dest = a.Position + dir * pushDist; dest.z = a.Position.z;
                            QueueMove(a, dest, 0.3f);
                        }
                        // Kinetic side damage per tick (0.5f/tick = 1 DPS per push count)
                        if (!rev) DamageAgent(a, cast.PushCount * 0.5f);
                    }
                    if (cast.MoraleCount > 0 && !rev)
                    {
                        // Smoulder side damage per tick (0.75f/tick = 1.5 DPS per morale count)
                        DamageAgent(a, cast.MoraleCount * 0.75f);
                    }
                }
                catch { }
            }
        }

        // ── BURST ─────────────────────────────────────────────────────────────
        public static void ExecuteBurst(SpellCast cast)
        {
            Agent caster = Agent.Main;
            if (caster == null) return;
            ExecuteBurstFromAgent(caster, cast, caster.Team);
        }

        internal static void ExecuteBurstFromAgent(Agent caster, SpellCast cast, Team casterTeam)
        {
            if (caster == null || !caster.IsActive() || Mission.Current == null) return;

            float radius = Math.Max(2f, cast.FormCount * 2f);
            var targets  = new List<Agent>();
            try
            {
                foreach (Agent a in Mission.Current.Agents)
                {
                    if (!a.IsActive() || a.IsMount || a == caster) continue;
                    if (casterTeam != null && a.Team == casterTeam) continue; // skip allies
                    if (a.Position.Distance(caster.Position) > radius) continue;
                    targets.Add(a);
                }
            }
            catch { }

            ColorSchool col = cast.VisualColor;
            SpawnCircleLights(caster.Position, col, radius, 6f);
            TryCastSound(caster.Position, col);
            TryCastAnimation(caster);

            int affected = 0;
            foreach (Agent a in targets)
            {
                try
                {
                    ApplyEffectsToAgent(a, cast, caster, applyPush: true, applyPull: true);
                    SpawnImpactBurst(a.Position, col, 4f);
                    affected++;
                }
                catch { }
            }

            if (caster == Agent.Main)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{cast.FormSummary()} — {cast.EffectSummary()} — {affected} {(affected == 1 ? "target" : "targets")}.",
                    ColorSchoolData.GetMessageColor(col)));
            }
        }
    }
}
