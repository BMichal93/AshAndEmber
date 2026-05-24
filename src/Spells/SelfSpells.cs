// =============================================================================
// COLOURS OF CALRADIA — SelfSpells.cs
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
        // SELF SPELLS — glowing aura around the caster
        // =================================================================

        // Scarlet Barrier — 6-node ring wall; anyone inside takes 20 dmg/tick; toggle
        private static void SpellSelfRed()
        {
            if (Player == null || !Player.IsActive()) return;
            const string Id = "self_red_barrier";
            if (HasAreaEffect(Id))
            {
                RemoveAreaEffect(Id);
                Msg("Scarlet Barrier dismissed.", ColorSchool.Red);
                return;
            }
            float power = SpellPower(ColorSchool.Red);
            Vec3  centre = Player.Position;
            const float Ring = 4f;
            for (int i = 0; i < 6; i++)
            {
                double angle = Math.PI * 2.0 / 6 * i;
                Vec3 pos = centre + new Vec3((float)Math.Cos(angle) * Ring, (float)Math.Sin(angle) * Ring, 0f);
                ToggleAreaEffect(Id, new AreaEffect
                {
                    Id = Id, School = ColorSchool.Red,
                    Position = pos, Radius = 1.5f,
                    TickInterval = 1f, TickTimer = 1f, Remaining = -1f,
                    Power = power
                });
            }
            BeginAgentGlow(Player, ColorSchool.Red, 3f);
            SpawnTempLight(centre, ColorSchool.Red, 8f, 2f);
            Msg("Scarlet Barrier — a ring of crimson pillars erupts around you. Any who step inside burn. Cast again to dismiss.", ColorSchool.Red);
        }

        // Gilded Words — turn one random nearby unmounted non-hero enemy to fight for the player
        private static void SpellSelfOrange()
        {
            if (Mission.Current == null || Player == null || !Player.IsActive()) return;
            const float Radius = 15f;

            // Exclude mounted enemies — mount would stay on enemy team, creating split state.
            // Exclude heroes — hero team membership is tied to campaign data.
            var candidates = Enemies()
                .Where(a => a.MountAgent == null && a.Position.Distance(Player.Position) <= Radius)
                .ToList();

            if (candidates.Count == 0)
            {
                Msg("Gilded Words — no unguarded souls within reach.", ColorSchool.Orange);
                return;
            }

            Agent target = candidates[_rng.Next(candidates.Count)];
            string targetName = target.Name?.ToString() ?? "the creature";
            bool converted = false;

            try
            {
                // Try to move the agent to the player's team via the internal SetTeam method.
                var setTeam = typeof(Agent).GetMethod("SetTeam",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                if (setTeam != null)
                {
                    setTeam.Invoke(target, new object[] { Player.Team, true });
                    converted = true;
                }
            }
            catch { }

            if (!converted)
            {
                // Fallback: remove from formation and set a long halt so they stop fighting
                try { if (target.Formation != null) target.Formation = null; } catch { }
                try { target.SetMaximumSpeedLimit(0f, false); } catch { }
                _haltedAgents[target.Index] = (30f, target.Position);
            }

            try { target.SetWatchState(Agent.WatchState.Alarmed); } catch { }
            BeginAgentGlow(target, ColorSchool.Orange, 2f);
            SpawnTempLight(target.Position, ColorSchool.Orange, 6f, 1.5f);
            BeginAgentGlow(Player, ColorSchool.Orange, 1.5f);
            SpawnTempLight(Player.Position, ColorSchool.Orange, 6f, 1.5f);
            Msg($"Gilded Words — {targetName} is swayed. They fight for you now.", ColorSchool.Orange);
        }

        // Nausea Bloom — 30-second aura that slowly damages everything nearby
        private static void SpellSelfYellow()
        {
            if (Player == null) return;
            if (HasAreaEffect("self_yellow")) { Msg("Nausea Bloom is already active.", ColorSchool.Yellow); return; }
            ToggleAreaEffect("self_yellow", new AreaEffect
            {
                Id = "self_yellow", School = ColorSchool.Yellow,
                Position = Player.Position, Radius = 8f,
                Velocity = new Vec3(1f, 0f, 0f), DirTimer = 3f,
                TickInterval = 2f, TickTimer = 2f, Remaining = 30f,
                Power = SpellPower(ColorSchool.Yellow)
            });
            BeginAgentGlow(Player, ColorSchool.Yellow, 2f);
            SpawnTempLight(Player.Position, ColorSchool.Yellow, 6f, 1.5f);
            ActiveEffectManager.Add(new ActiveEffect
            {
                Name = "_nausea_bloom", Duration = 30f, IsMissionEffect = true,
                OnExpire = () =>
                {
                    RemoveAreaEffect("self_yellow");
                    Msg("The Nausea Bloom passes. The wrongness fades.", ColorSchool.Yellow);
                }
            });
            Msg("Nausea Bloom — something deeply wrong radiates from you for 30 seconds. All nearby will feel it.", ColorSchool.Yellow);
        }

        // Verdant Touch — heal self
        private static void SpellSelfGreen()
        {
            if (Player == null) return;
            float power = SpellPower(ColorSchool.Green);
            float heal = Math.Min(40f * power, Player.HealthLimit - Player.Health);
            Player.Health = Math.Min(Player.Health + 40f * power, Player.HealthLimit);
            BeginAgentGlow(Player, ColorSchool.Green, 1.5f);
            SpawnTempLight(Player.Position, ColorSchool.Green, 6f, 1.5f);
            Msg($"Verdant Touch — you restore {heal:F0} HP.", ColorSchool.Green);
        }

        // Cerulean Burst — instant AoE (15m): damages, halts, and drains morale of all enemies
        private static void SpellSelfBlue()
        {
            if (Player == null || !Player.IsActive()) return;
            float power = SpellPower(ColorSchool.Blue);
            float haltDuration = 2f + power * 1.5f;
            const float Radius = 15f;
            var enemies = Mission.Current?.Agents
                .Where(a => a.IsActive() && !a.IsMount && a != Player
                         && a.Team != Player.Team
                         && a.Position.Distance(Player.Position) <= Radius)
                .ToList();
            if (enemies == null || enemies.Count == 0) { Msg("No enemies within burst range.", ColorSchool.Blue); return; }
            var formations = new HashSet<Formation>();
            foreach (Agent a in enemies)
            {
                try
                {
                    DamageAgent(a, 28f * power, ColorSchool.Blue);
                    if (!a.IsActive()) continue;
                    try { a.SetMorale(Math.Max(0f, a.GetMorale() - 35f)); } catch { }
                    bool usingEquip = false;
                    try { usingEquip = a.IsUsingGameObject; } catch { }
                    if (a.MountAgent == null && !usingEquip)
                    {
                        try { a.SetMaximumSpeedLimit(0f, false); } catch { }
                        _haltedAgents[a.Index] = (haltDuration, a.Position);
                    }
                    BeginAgentGlow(a, ColorSchool.Blue, 1.5f);
                    SpawnTempLight(a.Position, ColorSchool.Blue, 6f, 3f);
                    if (a.Formation != null) formations.Add(a.Formation);
                }
                catch { }
            }
            if (!IsSiegeActive())
                foreach (Formation f in formations)
                {
                    try { f.SetMovementOrder(MovementOrder.MovementOrderStop); } catch { }
                    try { if (f.HasAnyMountedUnit) f.SetRidingOrder(RidingOrder.RidingOrderDismount); } catch { }
                }
            BeginAgentGlow(Player, ColorSchool.Blue, 2f);
            SpawnTempLight(Player.Position, ColorSchool.Blue, 8f, 2f);
            Msg($"Cerulean Burst — blue force detonates outward, halting {enemies.Count} {(enemies.Count == 1 ? "enemy" : "enemies")} for {haltDuration:F1}s.", ColorSchool.Blue);
        }

        // Grey Reaping — snuffs 1–2 nearby souls; those who remain lose all nerve
        private static void SpellSelfPurple()
        {
            if (Player == null || Mission.Current == null) return;
            float power = SpellPower(ColorSchool.Purple);
            const float Radius = 15f;

            // Drain morale of all nearby enemies
            int drained = 0;
            foreach (Agent a in Enemies().Where(a => a.Position.Distance(Player.Position) <= Radius).ToList())
            {
                try { a.SetMorale(0f); BeginAgentGlow(a, ColorSchool.Purple, 1.5f); drained++; } catch { }
            }

            // Kill 1 (or 2 at high power) random non-hero enemies within radius
            int killCount = power >= 1.0f ? 2 : 1;
            var candidates = Enemies()
                .Where(a => !a.IsHero && a.IsActive() && a.Position.Distance(Player.Position) <= Radius)
                .ToList();
            int kills = 0;
            for (int i = 0; i < killCount && candidates.Count > 0; i++)
            {
                int idx = _rng.Next(candidates.Count);
                Agent target = candidates[idx];
                candidates.RemoveAt(idx);
                try
                {
                    BeginAgentGlow(target, ColorSchool.Purple, 1.5f);
                    SpawnTempLight(target.Position, ColorSchool.Purple, 6f, 1.5f);
                    QueueKill(target);
                    kills++;
                }
                catch { }
            }

            BeginAgentGlow(Player, ColorSchool.Purple, 2f);
            SpawnTempLight(Player.Position, ColorSchool.Purple, 6f, 1.5f);

            string killMsg  = kills  > 0 ? $" {kills} {(kills == 1 ? "soul" : "souls")} snuffed." : "";
            string drainMsg = drained > 0 ? $" {drained} {(drained == 1 ? "enemy loses" : "enemies lose")} their nerve." : " No enemies within range.";
            Msg($"Grey Reaping —{killMsg}{drainMsg}", ColorSchool.Purple);
        }
    }
}
