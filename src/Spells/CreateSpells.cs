// =============================================================================
// COLOURS OF CALRADIA — CreateSpells.cs
// Mount & Blade II: Bannerlord Mod  v2.0
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

namespace ColoursOfCalradia
{
    public static partial class SpellEffects
    {
        // =================================================================
        // CREATE SPELLS — persistent area effects on the battlefield
        // Casting again toggles off the existing effect (marked "Cast again to dismiss").
        // Glow is re-applied on each effect tick (~every 2s) — no per-frame FPS cost.
        // Visual note: particle effects not available; glow on affected agents indicates
        //              the area. Actual ground patches cannot be rendered by this mod.
        // =================================================================

        // Cinder Burst — moderate explosion around the caster (no toggle, instant)
        private static void SpellCreateRed()
        {
            if (Player == null || Mission.Current == null) return;
            float power = SpellPower(ColorSchool.Red);
            const float Radius = 10f;
            int count = 0;
            foreach (Agent a in Mission.Current.Agents
                .Where(a => a.IsActive() && !a.IsMount && a != Player &&
                            a.Position.Distance(Player.Position) <= Radius).ToList())
            {
                try
                {
                    DamageAgent(a, 50f * power);
                    BeginAgentGlow(a, ColorSchool.Red, 1.5f);
                    count++;
                }
                catch { }
            }
            BeginAgentGlow(Player, ColorSchool.Red, 1.5f);
            SpawnTempLight(Player.Position, ColorSchool.Red, Radius, 2f);
            Msg(count > 0 ? $"Cinder Burst destroys {count} {(count == 1 ? "creature" : "creatures")} within {Radius}m."
                          : "The burst finds nothing nearby.", ColorSchool.Red);
        }

        // Golden Snare — one-shot patch; triggers on first contact with a random formation command
        private static void SpellCreateOrange()
        {
            if (Player == null) return;
            if (HasAreaEffect("create_orange"))
            {
                RemoveAreaEffect("create_orange");
                _snarePrevInside.Clear();
                Msg("The Golden Snare fades before it could spring.", ColorSchool.Orange);
                return;
            }
            const float NodeRadius  = 6f;
            const float HalfSpacing = 4f;
            Vec3 fwd   = Player.LookDirection.NormalizedCopy();
            Vec3 right = new Vec3(-fwd.y, fwd.x, 0f);
            if (right.Length < 0.01f) right = new Vec3(1f, 0f, 0f);
            else right = right.NormalizedCopy();
            Vec3 centre = Player.Position;
            Vec3[] nodePos = {
                centre - right * HalfSpacing - fwd * HalfSpacing,
                centre + right * HalfSpacing - fwd * HalfSpacing,
                centre - right * HalfSpacing + fwd * HalfSpacing,
                centre + right * HalfSpacing + fwd * HalfSpacing,
            };
            _snarePrevInside.Clear();
            foreach (Vec3 pos in nodePos)
            {
                var node = new AreaEffect
                {
                    Id = "create_orange", School = ColorSchool.Orange,
                    Position = pos, Radius = NodeRadius,
                    TickInterval = 0.5f, TickTimer = 0.5f, Remaining = 60f
                };
                node.LightEntity = SpawnAreaLight(node.Position, node.School, node.Radius);
                _areaEffects.Add(node);
            }
            // Snapshot agents inside any node so they don't trigger the snare immediately.
            // _snarePrevInside is set once at cast time and not updated during ticks (one-shot trap).
            if (Mission.Current != null)
                foreach (Agent a in Mission.Current.Agents)
                {
                    if (!a.IsActive() || a.IsMount) continue;
                    foreach (Vec3 pos in nodePos)
                        if (a.Position.Distance(pos) <= NodeRadius) { _snarePrevInside.Add(a.Index); break; }
                }
            BeginAgentGlow(Player, ColorSchool.Orange, 2f);
            Msg("Golden Snare laid — a patch of arcane pressure. The first formation to step in receives a random command and the trap vanishes. Cast again to dismiss.", ColorSchool.Orange);
        }

        // Creeping Dread — moving cloud of revulsion that damages agents it passes through
        private static void SpellCreateYellow()
        {
            if (Player == null) return;
            if (HasAreaEffect("create_yellow"))
            {
                RemoveAreaEffect("create_yellow");
                Msg("The Creeping Dread dissipates. The air settles.", ColorSchool.Yellow);
                return;
            }
            float power = SpellPower(ColorSchool.Yellow);
            const float NodeRadius  = 5f;
            const float HalfSpacing = 4f;
            Vec3 fwd   = Player.LookDirection.NormalizedCopy();
            Vec3 right = new Vec3(-fwd.y, fwd.x, 0f);
            if (right.Length < 0.01f) right = new Vec3(1f, 0f, 0f);
            else right = right.NormalizedCopy();
            Vec3 centre = Player.Position;
            Vec3[] nodePos = {
                centre - right * HalfSpacing - fwd * HalfSpacing,
                centre + right * HalfSpacing - fwd * HalfSpacing,
                centre - right * HalfSpacing + fwd * HalfSpacing,
                centre + right * HalfSpacing + fwd * HalfSpacing,
            };
            foreach (Vec3 pos in nodePos)
            {
                var node = new AreaEffect
                {
                    Id = "create_yellow", School = ColorSchool.Yellow,
                    Position = pos, Radius = NodeRadius,
                    Velocity = new Vec3(1f, 0f, 0f),
                    DirTimer = 3f,
                    TickInterval = 2f, TickTimer = 2f, Remaining = -1f,
                    Power = power
                };
                node.LightEntity = SpawnAreaLight(node.Position, node.School, node.Radius);
                _areaEffects.Add(node);
            }
            BeginAgentGlow(Player, ColorSchool.Yellow, 2f);
            Msg("Creeping Dread takes shape — four clouds of formless terror drift across the field. Cast again to dismiss.", ColorSchool.Yellow);
        }

        // Emerald Font — two healing pools side by side, perpendicular to caster's look direction
        private static void SpellCreateGreen()
        {
            if (Player == null) return;
            if (HasAreaEffect("create_green"))
            {
                RemoveAreaEffect("create_green");
                Msg("The Emerald Font closes.", ColorSchool.Green);
                return;
            }
            float power = SpellPower(ColorSchool.Green);
            const float NodeRadius  = 6f;
            const float NodeSpacing = 5f;
            Vec3 fwd   = Player.LookDirection.NormalizedCopy();
            Vec3 right = new Vec3(-fwd.y, fwd.x, 0f);
            if (right.Length < 0.01f) right = new Vec3(1f, 0f, 0f);
            else right = right.NormalizedCopy();
            Vec3 centre = Player.Position;
            Vec3[] nodePos = {
                centre - right * (NodeSpacing * 0.5f),
                centre + right * (NodeSpacing * 0.5f),
            };
            foreach (Vec3 pos in nodePos)
            {
                var node = new AreaEffect
                {
                    Id = "create_green", School = ColorSchool.Green,
                    Position = pos, Radius = NodeRadius,
                    TickInterval = 2f, TickTimer = 2f, Remaining = -1f,
                    Power = power
                };
                node.LightEntity = SpawnAreaLight(node.Position, node.School, node.Radius);
                _areaEffects.Add(node);
            }
            BeginAgentGlow(Player, ColorSchool.Green, 2f);
            Msg("The Emerald Font opens — two pools of living light, mending all who stand within. Cast again to dismiss.", ColorSchool.Green);
        }

        // Sapphire Bastion — three repulsion nodes in a line perpendicular to the caster's look direction,
        // forming a wall of force across the battlefield.
        private static void SpellCreateBlue()
        {
            if (Player == null) return;
            if (HasAreaEffect("create_blue"))
            {
                RemoveAreaEffect("create_blue");
                Msg("The Sapphire Bastion crumbles.", ColorSchool.Blue);
                return;
            }

            const float Duration     = 120f;
            const float NodeRadius   = 3f;
            const float NodeSpacing  = 4.5f; // distance between adjacent node centres

            // Wall runs perpendicular to the player's look direction
            Vec3 fwd   = Player.LookDirection.NormalizedCopy();
            Vec3 right = new Vec3(-fwd.y, fwd.x, 0f);
            if (right.Length < 0.01f) right = new Vec3(1f, 0f, 0f);
            else right = right.NormalizedCopy();

            Vec3 centre = Player.Position;
            Vec3[] nodePos = {
                centre - right * NodeSpacing,
                centre,
                centre + right * NodeSpacing
            };

            foreach (Vec3 pos in nodePos)
            {
                var node = new AreaEffect
                {
                    Id = "create_blue", School = ColorSchool.Blue,
                    Position = pos, Radius = NodeRadius,
                    TickInterval = 0.5f, TickTimer = 0.5f, Remaining = Duration
                };
                node.LightEntity = SpawnAreaLight(node.Position, node.School, node.Radius);
                _areaEffects.Add(node);
            }

            BeginAgentGlow(Player, ColorSchool.Blue, 2f);
            Msg("Sapphire Bastion rises — three pillars of force seal the line. None shall cross. Fades in 2 minutes.", ColorSchool.Blue);
        }

        // Hollow Gaze — one random nearby enemy becomes catatonic; casting again cancels the effect
        private static void SpellCreatePurple()
        {
            if (Player == null || Mission.Current == null) return;
            if (_hollowGazeTarget != null)
            {
                string name = _hollowGazeTarget.IsActive() ? _hollowGazeTarget.Name : "them";
                _hollowGazeTarget = null;
                Msg($"The Hollow Gaze releases. {name} stirs back into themselves.", ColorSchool.Purple);
                return;
            }
            const float Radius = 15f;
            var candidates = Enemies()
                .Where(a => !a.IsHero && a.IsActive() && a.Position.Distance(Player.Position) <= Radius)
                .ToList();
            if (candidates.Count == 0) { Msg("No one nearby to hollow out.", ColorSchool.Purple); return; }
            _hollowGazeTarget = candidates[_rng.Next(candidates.Count)];
            _hollowGazeTimer  = 0f;
            BeginAgentGlow(_hollowGazeTarget, ColorSchool.Purple, 3f);
            Msg($"Hollow Gaze — {_hollowGazeTarget.Name} empties out. They stand and wait for nothing.", ColorSchool.Purple);
        }

        // Recruit helpers (used by Calling and NPC AI)
        public static CharacterObject FindRecruit(Agent agent)
        {
            string cultureId = agent?.Character?.Culture?.StringId;
            if (!string.IsNullOrEmpty(cultureId))
                foreach (CharacterObject c in CharacterObject.All)
                    if (!c.IsHero && c.Tier == 1 && c.Culture?.StringId == cultureId) return c;
            foreach (CharacterObject c in CharacterObject.All)
                if (!c.IsHero && c.Tier == 1) return c;
            return null;
        }
    }
}
