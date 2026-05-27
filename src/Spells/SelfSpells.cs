// =============================================================================
// LIFE & DEATH MAGIC — SelfSpells.cs
// AURA FORM: expanding cloud centred on caster, radius = formCount * 2m.
// Persistent toggle: cast again to dismiss. Glow marks affected agents each tick.
// Uses the existing area-effect infrastructure with IDs "spell_aura".
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace ColoursOfCalradia
{
    public static partial class SpellEffects
    {
        private const string AuraId = "spell_aura";

        // ── Player Aura ───────────────────────────────────────────────────────
        public static void ExecuteAura(SpellCast cast)
        {
            Agent caster = Agent.Main;
            if (caster == null || !caster.IsActive()) return;

            // Toggle off
            if (HasAreaEffect(AuraId))
            {
                RemoveAreaEffect(AuraId);
                InformationManager.DisplayMessage(new InformationMessage(
                    "Aura released.", new Color(0.7f, 0.7f, 0.7f)));
                return;
            }

            float radius = Math.Max(2f, cast.FormCount * 2f);
            SpawnAuraNodes(caster.Position, radius, cast, caster.Team);

            ColorSchool col = cast.VisualColor;
            BeginAgentGlow(caster, col, 3f);
            SpawnCircleLights(caster.Position, col, radius, 3f);
            TryCastSound(caster.Position, col);
            TryCastAnimation(caster);

            InformationManager.DisplayMessage(new InformationMessage(
                $"Aura — {cast.EffectSummary()}. Cast again to release.",
                ColorSchoolData.GetMessageColor(col)));
        }

        private static void SpawnAuraNodes(Vec3 centre, float radius, SpellCast cast, Team casterTeam)
        {
            int nodeCount = Math.Max(1, cast.FormCount);
            // Centre node
            AddAuraNode(centre, radius, cast, casterTeam);
            // Ring nodes (for larger auras)
            if (nodeCount > 1)
            {
                int ring = nodeCount - 1;
                float ringR = Math.Min(radius * 0.6f, 8f);
                for (int i = 0; i < ring; i++)
                {
                    double angle = Math.PI * 2.0 / ring * i;
                    Vec3 pos = centre + new Vec3((float)Math.Cos(angle) * ringR, (float)Math.Sin(angle) * ringR, 0f);
                    AddAuraNode(pos, radius / 2f, cast, casterTeam);
                }
            }
        }

        private static void AddAuraNode(Vec3 pos, float radius, SpellCast cast, Team casterTeam)
        {
            // Store cast parameters inside the Power field (encode as a token)
            // We store DamageCount, PushCount, MoraleCount, Reversed as encoded float
            // Format: (damage * 1000 + push * 100 + morale * 10 + (reversed?1:0)) as float
            float token = cast.DamageCount * 1000f + cast.PushCount * 100f
                        + cast.MoraleCount * 10f   + (cast.Reversed ? 1f : 0f);
            var node = new AreaEffect
            {
                Id           = AuraId,
                School       = cast.VisualColor,
                Position     = pos,
                Radius       = radius,
                TickInterval = 2f,
                TickTimer    = 2f,
                Remaining    = -1f,  // toggle-only, no expiry
                Power        = token,
                CasterTeam   = casterTeam
            };
            node.LightEntity = SpawnAreaLight(node.Position, cast.VisualColor, radius);
            _areaEffects.Add(node);
        }

        // Called from AreaEffects.cs tick
        internal static void TickAuraNode(AreaEffect e)
        {
            if (Mission.Current == null) return;
            // Decode cast parameters from Power token
            int token    = (int)e.Power;
            int dmg      = token / 1000;
            int push     = (token % 1000) / 100;
            int morale   = (token % 100) / 10;
            bool rev     = (token % 10) == 1;

            var cast = new SpellCast
            {
                DamageCount = dmg, PushCount = push,
                MoraleCount = morale, Reversed = rev
            };

            Vec3 origin = e.Position;
            foreach (Agent a in Mission.Current.Agents.ToList())
            {
                if (!a.IsActive() || a.IsMount) continue;
                if (e.CasterTeam != null && a.Team == e.CasterTeam) continue; // friendly fire off
                if (a.Position.Distance(origin) > e.Radius) continue;
                try
                {
                    uint raw = rev
                        ? ColorSchoolData.GetReversedGlowColor(e.School)
                        : ColorSchoolData.GetGlowColor(e.School);
                    BeginAgentGlowRaw(a, raw, 2f);
                    if (cast.DamageCount > 0)
                    {
                        float amt = cast.DamageCount * 8f * 0.5f; // halved for persistent
                        if (rev) HealAgent(a, amt); else DamageAgent(a, amt);
                    }
                    if (cast.MoraleCount > 0)
                    {
                        float delta = cast.MoraleCount * 5f;
                        float cur   = a.GetMorale();
                        a.SetMorale(rev ? Math.Min(cur + delta, 100f) : Math.Max(cur - delta, 0f));
                    }
                    // Push/pull in aura — skip mounted riders
                    if (cast.PushCount > 0)
                    {
                        bool isMounted = false;
                        try { isMounted = a.MountAgent != null; } catch { }
                        if (!isMounted)
                        {
                            float dist = cast.PushCount * 1.5f;
                            Agent src = Agent.Main;
                            if (src != null)
                            {
                                Vec3 dir = rev
                                    ? (src.Position - a.Position).NormalizedCopy()
                                    : (a.Position - src.Position).NormalizedCopy();
                                Vec3 dest = a.Position + dir * dist; dest.z = a.Position.z;
                                QueueMove(a, dest, 0.4f);
                            }
                        }
                    }
                }
                catch { }
            }
        }
    }
}
