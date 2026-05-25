// =============================================================================
// COLOURS OF CALRADIA — CreateSpells.cs
// Mount & Blade II: Bannerlord Mod  v1.2.0.0
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
                    DamageAgent(a, 75f * power);
                    BeginAgentGlow(a, ColorSchool.Red, 1.5f);
                    count++;
                }
                catch { }
            }
            BeginAgentGlow(Player, ColorSchool.Red, 1.5f);
            SpawnTempLight(Player.Position, ColorSchool.Red, Radius, 3f);
            Msg(count > 0 ? $"Cinder Burst destroys {count} {(count == 1 ? "creature" : "creatures")} within {Radius}m."
                          : "The burst finds nothing nearby.", ColorSchool.Red);
        }

        // Gilded Refuge — large inspiring zone: flat +100 morale and 2 HP/sec passive healing; toggle
        private static void SpellCreateOrange()
        {
            if (Player == null) return;
            const string Id = "create_orange";
            if (HasAreaEffect(Id))
            {
                RemoveAreaEffect(Id);
                Msg("The Gilded Refuge fades. The warmth departs.", ColorSchool.Orange);
                return;
            }
            float power = SpellPower(ColorSchool.Orange);
            const float NodeRadius  = 7f;
            const float NodeSpacing = 7f;
            Vec3 fwd   = Player.LookDirection.NormalizedCopy();
            Vec3 right = new Vec3(-fwd.y, fwd.x, 0f);
            if (right.Length < 0.01f) right = new Vec3(1f, 0f, 0f);
            else right = right.NormalizedCopy();
            Vec3 centre = Player.Position;
            Vec3[] nodePos = {
                centre - right * NodeSpacing - fwd * NodeSpacing,
                centre                       - fwd * NodeSpacing,
                centre + right * NodeSpacing - fwd * NodeSpacing,
                centre - right * NodeSpacing,
                centre,
                centre + right * NodeSpacing,
                centre - right * NodeSpacing + fwd * NodeSpacing,
                centre                       + fwd * NodeSpacing,
                centre + right * NodeSpacing + fwd * NodeSpacing,
            };
            foreach (Vec3 pos in nodePos)
            {
                var node = new AreaEffect
                {
                    Id = Id, School = ColorSchool.Orange,
                    Position = pos, Radius = NodeRadius,
                    TickInterval = 2f, TickTimer = 2f, Remaining = -1f,
                    Power = power
                };
                node.LightEntity = SpawnAreaLight(node.Position, node.School, node.Radius);
                _areaEffects.Add(node);
            }
            BeginAgentGlow(Player, ColorSchool.Orange, 2f);
            SpawnTempLight(Player.Position, ColorSchool.Orange, 6f, 1.5f);
            Msg("Gilded Refuge — a vast warmth settles across the field. Those inside hold the line with iron resolve and close wounds faster. Cast again to dismiss.", ColorSchool.Orange);
        }

        // Creeping Dread — moving clouds of revulsion that damage agents they pass through
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
            const float NodeRadius = 7f;
            const float Spacing    = 5f;
            Vec3 fwd   = Player.LookDirection.NormalizedCopy();
            Vec3 right = new Vec3(-fwd.y, fwd.x, 0f);
            if (right.Length < 0.01f) right = new Vec3(1f, 0f, 0f);
            else right = right.NormalizedCopy();
            Vec3 centre = Player.Position;
            // 3×3 grid of nine drifting nodes
            Vec3[] nodePos = {
                centre - right * Spacing - fwd * Spacing,
                centre                  - fwd * Spacing,
                centre + right * Spacing - fwd * Spacing,
                centre - right * Spacing,
                centre,
                centre + right * Spacing,
                centre - right * Spacing + fwd * Spacing,
                centre                  + fwd * Spacing,
                centre + right * Spacing + fwd * Spacing,
            };
            float cloudAngle = (float)(_rng.NextDouble() * Math.PI * 2);
            Vec3  cloudVel   = new Vec3((float)Math.Cos(cloudAngle) * 2f, (float)Math.Sin(cloudAngle) * 2f, 0f);
            float cloudTimer = 2f + (float)_rng.NextDouble() * 3f;
            foreach (Vec3 pos in nodePos)
            {
                var node = new AreaEffect
                {
                    Id = "create_yellow", School = ColorSchool.Yellow,
                    Position = pos, Radius = NodeRadius,
                    Velocity = cloudVel,
                    DirTimer = cloudTimer,
                    TickInterval = 2f, TickTimer = 2f, Remaining = -1f,
                    Power = power
                };
                node.LightEntity = SpawnAreaLight(node.Position, node.School, node.Radius);
                _areaEffects.Add(node);
            }
            BeginAgentGlow(Player, ColorSchool.Yellow, 2f);
            SpawnTempLight(Player.Position, ColorSchool.Yellow, 6f, 1.5f);
            Msg("Creeping Dread takes shape — nine clouds of formless terror drift across the field. Cast again to dismiss.", ColorSchool.Yellow);
        }

        // Emerald Font — three healing pools in a triangle around the caster
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
            // Triangle: one node forward, two back-flanking
            Vec3[] nodePos = {
                centre + fwd  * (NodeSpacing * 0.8f),
                centre - fwd  * (NodeSpacing * 0.4f) - right * NodeSpacing,
                centre - fwd  * (NodeSpacing * 0.4f) + right * NodeSpacing,
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
            SpawnTempLight(Player.Position, ColorSchool.Green, 6f, 1.5f);
            Msg("The Emerald Font opens — three points of living light mend all who stand within. Cast again to dismiss.", ColorSchool.Green);
        }

        // Sapphire Bastion — six repulsion nodes in a wide line perpendicular to the caster's look direction.
        private static void SpellCreateBlue()
        {
            if (Player == null) return;
            if (HasAreaEffect("create_blue"))
            {
                RemoveAreaEffect("create_blue");
                Msg("The Sapphire Bastion crumbles.", ColorSchool.Blue);
                return;
            }

            const float NodeRadius  = 3f;
            const float NodeSpacing = 5f; // wider spacing for broader wall coverage

            Vec3 fwd   = Player.LookDirection.NormalizedCopy();
            Vec3 right = new Vec3(-fwd.y, fwd.x, 0f);
            if (right.Length < 0.01f) right = new Vec3(1f, 0f, 0f);
            else right = right.NormalizedCopy();

            Vec3 centre = Player.Position;
            Vec3[] nodePos = {
                centre - right * (NodeSpacing * 2.5f),
                centre - right * (NodeSpacing * 1.5f),
                centre - right * (NodeSpacing * 0.5f),
                centre + right * (NodeSpacing * 0.5f),
                centre + right * (NodeSpacing * 1.5f),
                centre + right * (NodeSpacing * 2.5f),
            };

            foreach (Vec3 pos in nodePos)
            {
                var node = new AreaEffect
                {
                    Id = "create_blue", School = ColorSchool.Blue,
                    Position = pos, Radius = NodeRadius,
                    TickInterval = 0.5f, TickTimer = 0.5f, Remaining = -1f
                };
                node.LightEntity = SpawnAreaLight(node.Position, node.School, node.Radius);
                _areaEffects.Add(node);
            }

            BeginAgentGlow(Player, ColorSchool.Blue, 2f);
            SpawnTempLight(Player.Position, ColorSchool.Blue, 6f, 1.5f);
            Msg("Sapphire Bastion rises — six pillars of force seal a wide line. Cast again to dismiss.", ColorSchool.Blue);
        }

        // Purple Mist — 3×3 grid of death nodes; any agent inside has 10% instakill chance per tick; toggle
        private static void SpellCreatePurple()
        {
            if (Player == null) return;
            const string Id = "create_purple_mist";
            if (HasAreaEffect(Id))
            {
                RemoveAreaEffect(Id);
                Msg("The Purple Mist disperses. The grey withdraws.", ColorSchool.Purple);
                return;
            }
            float power = SpellPower(ColorSchool.Purple);
            const float NodeRadius  = 4f;
            const float NodeSpacing = 5f;
            Vec3 fwd   = Player.LookDirection.NormalizedCopy();
            Vec3 right = new Vec3(-fwd.y, fwd.x, 0f);
            if (right.Length < 0.01f) right = new Vec3(1f, 0f, 0f);
            else right = right.NormalizedCopy();
            Vec3 centre = Player.Position;
            Vec3[] nodePos = {
                centre - right * NodeSpacing - fwd * NodeSpacing,
                centre                       - fwd * NodeSpacing,
                centre + right * NodeSpacing - fwd * NodeSpacing,
                centre - right * NodeSpacing,
                centre,
                centre + right * NodeSpacing,
                centre - right * NodeSpacing + fwd * NodeSpacing,
                centre                       + fwd * NodeSpacing,
                centre + right * NodeSpacing + fwd * NodeSpacing,
            };
            foreach (Vec3 pos in nodePos)
            {
                var node = new AreaEffect
                {
                    Id = Id, School = ColorSchool.Purple,
                    Position = pos, Radius = NodeRadius,
                    TickInterval = 2f, TickTimer = 2f, Remaining = -1f,
                    Power = power
                };
                node.LightEntity = SpawnAreaLight(node.Position, node.School, 7f);
                _areaEffects.Add(node);
            }
            BeginAgentGlow(Player, ColorSchool.Purple, 2f);
            SpawnTempLight(Player.Position, ColorSchool.Purple, 6f, 1.5f);
            Msg("Purple Mist — nine dim wisps settle across the ground. Those who step through them may simply stop. Cast again to dismiss.", ColorSchool.Purple);
        }

        // Spawns random Sapphire Bastion repulsion nodes for battle events (not player-cast)
        public static void SpawnBattleEventBlueWalls(Vec3 centre)
        {
            if (Mission.Current == null) return;
            int count = 4 + _rng.Next(3); // 4-6 nodes
            for (int i = 0; i < count; i++)
            {
                double angle = _rng.NextDouble() * Math.PI * 2;
                float  dist  = 8f + (float)(_rng.NextDouble() * 20f);
                Vec3 pos = centre + new Vec3((float)Math.Cos(angle) * dist,
                                             (float)Math.Sin(angle) * dist, 0f);
                pos.z = centre.z;
                var node = new AreaEffect
                {
                    Id = "create_blue", School = ColorSchool.Blue,
                    Position = pos, Radius = 3f,
                    TickInterval = 0.5f, TickTimer = 0.5f, Remaining = 25f
                };
                node.LightEntity = SpawnAreaLight(node.Position, ColorSchool.Blue, node.Radius);
                _areaEffects.Add(node);
            }
        }

        // ── NPC area-effect spawners ──────────────────────────────────────────
        // Called from ColourLordAI for timed (30s) area effects — same tick logic
        // as player create spells, but auto-expire instead of requiring dismissal.

        public static void SpawnNpcHealZone(Vec3 centre, ColorSchool school, float power, Team casterTeam = null)
        {
            if (Mission.Current == null) return;
            for (int i = 0; i < 2; i++)
            {
                double a = Math.PI * i;
                Vec3 pos = centre + new Vec3((float)Math.Cos(a) * 3.5f, (float)Math.Sin(a) * 3.5f, 0f);
                var node = new AreaEffect
                {
                    Id = "npc_green_font", School = school,
                    Position = pos, Radius = 6f,
                    TickInterval = 2f, TickTimer = 2f, Remaining = 30f,
                    Power = power, CasterTeam = casterTeam
                };
                node.LightEntity = SpawnAreaLight(node.Position, school, 6f);
                _areaEffects.Add(node);
            }
        }

        public static void SpawnNpcBlueWall(Vec3 centre, Vec3 fwd, Team casterTeam = null)
        {
            if (Mission.Current == null) return;
            if (fwd.Length < 0.01f) fwd = new Vec3(1f, 0f, 0f);
            else fwd = fwd.NormalizedCopy();
            Vec3 right = new Vec3(-fwd.y, fwd.x, 0f);
            if (right.Length < 0.01f) right = new Vec3(1f, 0f, 0f);
            else right = right.NormalizedCopy();
            Vec3[] pts = {
                centre + fwd * 5f - right * 4f,
                centre + fwd * 5f,
                centre + fwd * 5f + right * 4f,
            };
            foreach (Vec3 pos in pts)
            {
                var node = new AreaEffect
                {
                    Id = "npc_blue_wall", School = ColorSchool.Blue,
                    Position = pos, Radius = 3f,
                    TickInterval = 0.5f, TickTimer = 0.5f, Remaining = 30f,
                    CasterTeam = casterTeam
                };
                node.LightEntity = SpawnAreaLight(node.Position, ColorSchool.Blue, 5f);
                _areaEffects.Add(node);
            }
        }

        public static void SpawnNpcYellowCloud(Vec3 centre, float power, Team casterTeam = null)
        {
            if (Mission.Current == null) return;
            float baseAngle = (float)(_rng.NextDouble() * Math.PI * 2);
            Vec3 vel = new Vec3((float)Math.Cos(baseAngle) * 2f, (float)Math.Sin(baseAngle) * 2f, 0f);
            for (int i = 0; i < 3; i++)
            {
                double a = Math.PI * 2 / 3 * i;
                Vec3 pos = centre + new Vec3((float)Math.Cos(a) * 4f, (float)Math.Sin(a) * 4f, 0f);
                var node = new AreaEffect
                {
                    Id = "npc_yellow_cloud", School = ColorSchool.Yellow,
                    Position = pos, Radius = 7f,
                    Velocity = vel, DirTimer = 2f + (float)(_rng.NextDouble() * 3f),
                    TickInterval = 2f, TickTimer = 2f, Remaining = 30f,
                    Power = power, CasterTeam = casterTeam
                };
                node.LightEntity = SpawnAreaLight(node.Position, ColorSchool.Yellow, 7f);
                _areaEffects.Add(node);
            }
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
