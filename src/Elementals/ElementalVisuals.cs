// =============================================================================
// ASH AND EMBER — Elementals/ElementalVisuals.cs
//
// The LOOK of THE KINDLED — built once, then carried by the engine.
//
// The old aura re-stamped a fistful of short-lived particles at the body every
// third of a second (and a fresh point light on top), on every being, every
// tick. That churned dozens of GameEntities a second across a field of Kindled
// (the mod's heaviest per-frame cost) and still only read as a bald man with a
// coloured coat, because the stamps stuttered a step behind the moving body.
//
// Instead, a being is WREATHED at spawn in CONTINUOUS element: real particle
// systems bound to its own skeleton bones — pelvis, chest, head and both hands —
// so the fire / mist / dust rides every limb as it walks and swings its arms, and
// costs nothing per tick once attached (the skeleton drags it along for free). A
// single coloured light hugs the body to tint the shroud and glow in the dark
// (created ONCE, only cheaply repositioned each tick — never re-spawned), and a
// persistent coloured contour bleeds the raw element at the edges of a shape that
// has no face of its own.
//
// All state is mission-scoped: ClearAll() is called with the rest of the battle
// state, and each being is torn down as it falls (ElementalBeings drives both).
// Nothing here is serialized.
// =============================================================================

using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static class ElementalVisuals
    {
        // How solid the underlying body reads. The Kindled has no face and no skin
        // of its own — it is pooled element wearing a human shape only as a mould.
        // We fade the body meshes almost to nothing so what the eye follows is the
        // continuous element wreathing the bones, the coloured contour tracing the
        // silhouette, and the follower light — not a bald, textured man. A whisper
        // of the mesh is left (not a flat 0) so the contour still has an edge to
        // draw and the shape reads as a translucent apparition rather than a hole.
        private const float GhostAlpha = 0.07f;

        private class Shroud
        {
            public Skeleton Skeleton;
            public readonly List<KeyValuePair<sbyte, ParticleSystem>> Systems
                = new List<KeyValuePair<sbyte, ParticleSystem>>();
            public GameEntity Light;
            public uint Contour;   // re-asserted each tick — the shared glow system would otherwise wipe it on any hit-flash
        }

        private static readonly Dictionary<Agent, Shroud> _shrouds = new Dictionary<Agent, Shroud>();

        public static bool IsShrouded(Agent agent) => agent != null && _shrouds.ContainsKey(agent);

        // Bind the continuous element to `agent`'s bones, hang its light and set its
        // contour. Call once the visuals exist — ElementalBeings does this lazily on
        // the first tick it sees the being alive, when the skeleton is guaranteed
        // built. No-op if already shrouded or the skeleton is not ready yet.
        public static void Attach(Agent agent, ElementalKind kind)
        {
            if (agent == null || _shrouds.ContainsKey(agent)) return;
            try
            {
                if (!agent.IsActive()) return;
                Skeleton skeleton = agent.AgentVisuals?.GetSkeleton();
                if (skeleton == null) return;

                string psys = ParticleFor(kind);
                var shroud = new Shroud { Skeleton = skeleton };

                MatrixFrame local = MatrixFrame.Identity;
                foreach (sbyte bone in WreathBones(agent))
                {
                    if (bone < 0) continue;
                    try
                    {
                        ParticleSystem ps = ParticleSystem.CreateParticleSystemAttachedToBone(psys, skeleton, bone, ref local);
                        if (ps != null)
                            shroud.Systems.Add(new KeyValuePair<sbyte, ParticleSystem>(bone, ps));
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }

                // One coloured light per being, hung at chest height. Created here and
                // only moved thereafter — this is what tints the smoke of the airy /
                // stony kinds (their particle is uncoloured) and glows at night.
                try { shroud.Light = SpellEffects.CreateFollowerLight(agent.Position + new Vec3(0f, 0f, 1.1f), Rgb(kind), 4.5f); }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                shroud.Contour = Argb(kind);
                _shrouds[agent] = shroud;

                // A persistent, strongly-coloured outline — the element bleeding at the
                // edges of a faceless shape. Re-asserted by Follow (a hit-flash from the
                // shared glow system would otherwise clear it for good on expiry).
                try { agent.AgentVisuals?.GetEntity()?.SetContourColor(shroud.Contour, true); }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                // Fade the bald, textured body away to a translucent shimmer so the
                // being reads as pooled element, not a naked man. Re-asserted by
                // Follow — the engine reasserts full opacity on animation/LOD
                // changes, so a one-shot fade here would flicker back solid.
                try { agent.AgentVisuals?.GetEntity()?.SetAlpha(GhostAlpha); }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Cheap per-tick upkeep: drag the one light to the body and re-assert the
        // contour (a hit-flash from the shared glow system clears the outline on
        // expiry, so a Kindled struck once would otherwise lose it for good). The
        // bone-bound particles need nothing — the engine already carries them.
        public static void Follow(Agent agent)
        {
            if (agent == null || !_shrouds.TryGetValue(agent, out Shroud shroud)) return;
            try
            {
                if (!agent.IsActive()) return;
                if (shroud.Light != null)
                    SpellEffects.MoveFollowerLight(shroud.Light, agent.Position + new Vec3(0f, 0f, 1.1f));
                var entity = agent.AgentVisuals?.GetEntity();
                if (entity != null)
                {
                    entity.SetContourColor(shroud.Contour, true);
                    entity.SetAlpha(GhostAlpha);   // hold the body faded — see Attach
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        public static void Detach(Agent agent)
        {
            if (agent == null || !_shrouds.TryGetValue(agent, out Shroud shroud)) return;
            _shrouds.Remove(agent);
            RemoveShroud(agent, shroud);
        }

        public static void ClearAll()
        {
            foreach (var kv in _shrouds)
                RemoveShroud(kv.Key, kv.Value);
            _shrouds.Clear();
        }

        private static void RemoveShroud(Agent agent, Shroud shroud)
        {
            if (shroud == null) return;
            foreach (var pair in shroud.Systems)
            {
                try { pair.Value?.SetEnable(false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { shroud.Skeleton?.RemoveBoneComponent(pair.Key, pair.Value); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            shroud.Systems.Clear();
            SpellEffects.RemoveFollowerLight(shroud.Light);
            shroud.Light = null;
            try
            {
                var entity = agent?.AgentVisuals?.GetEntity();
                if (entity != null)
                {
                    entity.SetContourColor(null, false);
                    entity.SetAlpha(1f);   // restore full opacity (e.g. the corpse)
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Per-kind dressing ────────────────────────────────────────────────────
        // The bones we hang the element from: a full rising column up the trunk
        // (pelvis → lower spine → chest → neck → head) plus both upper arms and
        // hands AND both feet, so the element wreathes the WHOLE body — legs and
        // feet included — instead of leaving bare skin showing between a few thin
        // columns (the "bald man" the light smoke could not hide). More emitters
        // also simply reads as more element. Attached once at spawn, so the extra
        // bones cost nothing per tick. Missing bones (a non-human skeleton) come
        // back < 0 and are skipped by the caller.
        private static IEnumerable<sbyte> WreathBones(Agent agent)
        {
            Monster m = null;
            try { m = agent.Monster; } catch { }
            if (m == null) { yield return 0; yield break; }
            yield return m.PelvisBoneIndex;
            yield return m.SpineLowerBoneIndex;
            yield return m.SpineUpperBoneIndex;
            yield return m.NeckRootBoneIndex;
            yield return m.HeadLookDirectionBoneIndex;
            yield return m.LeftUpperArmBoneIndex;
            yield return m.RightUpperArmBoneIndex;
            yield return m.MainHandBoneIndex;
            yield return m.OffHandBoneIndex;
            yield return m.LeftFootIkEndEffectorBoneIndex;
            yield return m.RightFootIkEndEffectorBoneIndex;
        }

        // Continuous emitters only — a bone-bound system loops for the being's whole
        // life, so it must be a looping particle, not a one-shot impact burst. Fire
        // and driven snow loop in their own colour; the rest ride a neutral vapour
        // that the follower light tints to the element.
        private static string ParticleFor(ElementalKind kind)
        {
            switch (kind)
            {
                case ElementalKind.Flame: return "psys_fire_vertical";
                case ElementalKind.Frost: return "psys_snow_dust";
                default:                  return "psys_smoke"; // Tide / Gale / Sand / Stone / Void — tinted by the light
            }
        }

        // The element's colour, shared by the follower light (RGB 0..1) and the
        // contour outline (packed ARGB). Kept in step with the old aura palette.
        private static Vec3 Rgb(ElementalKind kind)
        {
            switch (kind)
            {
                case ElementalKind.Flame: return new Vec3(1.0f, 0.45f, 0.12f);
                case ElementalKind.Tide:  return new Vec3(0.18f, 0.50f, 1.0f);
                case ElementalKind.Frost: return new Vec3(0.70f, 0.85f, 1.0f);
                case ElementalKind.Gale:  return new Vec3(0.62f, 0.52f, 1.0f);
                case ElementalKind.Sand:  return new Vec3(0.85f, 0.70f, 0.40f);
                // Pitch black with the barest violet bruise — not zero, or the
                // follower light would simply not exist. The Great Other reads as
                // a hole the eye slides off, not a coloured being like the rest.
                case ElementalKind.Void:  return new Vec3(0.035f, 0.02f, 0.05f);
                default:                  return new Vec3(0.50f, 0.46f, 0.42f); // Stone
            }
        }

        private static uint Argb(ElementalKind kind)
        {
            Vec3 c = Rgb(kind);
            uint r = (uint)(Clamp01(c.x) * 255f);
            uint g = (uint)(Clamp01(c.y) * 255f);
            uint b = (uint)(Clamp01(c.z) * 255f);
            return 0xFF000000u | (r << 16) | (g << 8) | b;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
