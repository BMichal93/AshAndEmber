// =============================================================================
// COLOURS OF CALRADIA — BlastSpells.cs
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
        // BLAST SPELLS — medium cone in front of the caster
        // Cone: 17m range, dot >= 0.6 (≈53° half-angle)
        // Glow applied to all affected agents to show the area of effect.
        // =================================================================

        // Crimson Torrent — moderate damage + pushback in cone
        private static void SpellBlastRed()
        {
            if (Player == null) return;
            float power = SpellPower(ColorSchool.Red);
            Vec3 fwd = Player.LookDirection.NormalizedCopy();
            var inCone = ConeAgents(Player.Position, fwd, 15f, 0.6f);
            if (inCone.Count == 0) { Msg("No one in the cone.", ColorSchool.Red); return; }
            int affected = 0;
            foreach (Agent a in inCone)
            {
                try
                {
                    DamageAgent(a, 75f * power, ColorSchool.Red);
                    if (a.IsActive() && a.Health > 0f)
                    {
                        Vec3 dir = (a.Position - Player.Position).NormalizedCopy();
                        Vec3 dest = a.Position + dir * (6f * power); dest.z = a.Position.z;
                        QueueMove(a, dest, 0.4f);
                    }
                    BeginAgentGlow(a, ColorSchool.Red, 1.5f);
                    SpawnTempLight(a.Position, ColorSchool.Red, 6f, 1.5f);
                    affected++;
                }
                catch { }
            }
            Msg($"Crimson Torrent tears through {affected} {(affected == 1 ? "creature" : "creatures")}.", ColorSchool.Red);
        }

        // Golden Tide — tiny damage + force enemies in cone to charge
        private static void SpellBlastOrange()
        {
            if (Player == null) return;
            float power = SpellPower(ColorSchool.Orange);
            Vec3 fwd = Player.LookDirection.NormalizedCopy();
            var inCone = ConeAgents(Player.Position, fwd, 15f, 0.6f);
            if (inCone.Count == 0) { Msg("No one in the cone.", ColorSchool.Orange); return; }
            var formations = new HashSet<Formation>();
            foreach (Agent a in inCone)
            {
                try
                {
                    DamageAgent(a, 45f * power, ColorSchool.Orange);
                    if (!a.IsActive()) continue;
                    try { a.SetMorale(100f); } catch { }
                    BeginAgentGlow(a, ColorSchool.Orange, 1.5f);
                    SpawnTempLight(a.Position, ColorSchool.Orange, 6f, 1.5f);
                    if (a.Formation != null) formations.Add(a.Formation);
                }
                catch { }
            }
            if (!IsSiegeActive())
                foreach (Formation f in formations)
                    try { f.SetMovementOrder(MovementOrder.MovementOrderCharge); } catch { }
            Msg($"Golden Tide washes over {inCone.Count} {(inCone.Count == 1 ? "creature" : "creatures")} — they surge forward!", ColorSchool.Orange);
        }

        // Tide of Dread — tiny damage + morale reduction in cone
        private static void SpellBlastYellow()
        {
            if (Player == null) return;
            float power = SpellPower(ColorSchool.Yellow);
            Vec3 fwd = Player.LookDirection.NormalizedCopy();
            var inCone = ConeAgents(Player.Position, fwd, 15f, 0.6f);
            if (inCone.Count == 0) { Msg("No one in the cone.", ColorSchool.Yellow); return; }
            foreach (Agent a in inCone)
            {
                try
                {
                    DamageAgent(a, 30f * power, ColorSchool.Yellow);
                    if (!a.IsActive()) continue;
                    try { a.SetMorale(Math.Max(0f, a.GetMorale() - 60f * power)); } catch { }
                    BeginAgentGlow(a, ColorSchool.Yellow, 1.5f);
                    SpawnTempLight(a.Position, ColorSchool.Yellow, 6f, 1.5f);
                }
                catch { }
            }
            Msg($"Tide of Dread — {inCone.Count} {(inCone.Count == 1 ? "creature loses" : "creatures lose")} their nerve.", ColorSchool.Yellow);
        }

        // Verdant Surge — heal allies in cone (player and enemies excluded)
        private static void SpellBlastGreen()
        {
            if (Player == null) return;
            float power = SpellPower(ColorSchool.Green);
            Vec3 fwd = Player.LookDirection.NormalizedCopy();
            var inCone = ConeAgents(Player.Position, fwd, 15f, 0.6f);
            int healed = 0;
            foreach (Agent a in inCone)
            {
                if (a == Player) continue;
                if (a.Team != Player.Team) continue; // enemies excluded
                try
                {
                    float h = Math.Min(35f * power, a.HealthLimit - a.Health);
                    if (h > 0f)
                    {
                        a.Health += h;
                        healed++;
                        BeginAgentGlow(a, ColorSchool.Green, 1.5f);
                        SpawnTempLight(a.Position, ColorSchool.Green, 6f, 1.5f);
                    }
                }
                catch { }
            }
            if (healed == 0) Msg("Verdant Surge — no allies in the cone to mend.", ColorSchool.Green);
            else Msg($"Verdant Surge mends {healed} {(healed == 1 ? "ally" : "allies")} in the cone.", ColorSchool.Green);
        }

        // Azure Arrest — damage + per-agent speed halt + dismount riders in cone
        private static void SpellBlastBlue()
        {
            if (Player == null) return;
            float power = SpellPower(ColorSchool.Blue);
            float haltDuration = 2f + power * 1.5f; // attr 1 ≈ 2.9 s, attr 5 ≈ 3.5 s, attr 10 ≈ 4.25 s
            Vec3 fwd = Player.LookDirection.NormalizedCopy();
            var inCone = ConeAgents(Player.Position, fwd, 15f, 0.6f);
            if (inCone.Count == 0) { Msg("No one in the cone.", ColorSchool.Blue); return; }
            var formations = new HashSet<Formation>();
            foreach (Agent a in inCone)
            {
                try
                {
                    DamageAgent(a, 35f * power, ColorSchool.Blue);
                    if (!a.IsActive()) continue;
                    try { a.SetMorale(Math.Max(0f, a.GetMorale() - 35f)); } catch { }
                    bool usingEquip = false;
                    try { usingEquip = a.IsUsingGameObject; } catch { }
                    if (a.MountAgent == null && !usingEquip)
                    {
                        try { a.SetMaximumSpeedLimit(0f, false); } catch { }
                        _haltedAgents[a.Index] = (haltDuration, a.Position, a);
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
            Msg($"Azure Arrest freezes {inCone.Count} {(inCone.Count == 1 ? "creature" : "creatures")} for {haltDuration:F1}s. Riders unseated.", ColorSchool.Blue);
        }

        // Grey Harvest — 1–3 non-hero creatures in cone fade and die (scales with Purple attribute)
        // attr 1–4 (power <1.0): 1 kill; attr 5–7 (power 1.0–1.2): 2 kills; attr 8–10 (power ≥1.3): 3 kills
        private static void SpellBlastPurple()
        {
            if (Player == null) return;
            Vec3 fwd = Player.LookDirection.NormalizedCopy();
            // Exclude heroes — Die() with OwnerId=-1 on a hero crashes Bannerlord's death processing
            var inCone = ConeAgents(Player.Position, fwd, 15f, 0.6f).Where(a => !a.IsHero).ToList();
            if (inCone.Count == 0) { Msg("No common souls in the cone — the purple passes over champions.", ColorSchool.Purple); return; }
            float power = SpellPower(ColorSchool.Purple);
            int killCount = Math.Min(inCone.Count, power >= 1.3f ? 3 : power >= 1.0f ? 2 : 1);
            // Pick killCount distinct targets via partial Fisher-Yates
            for (int i = 0; i < killCount; i++)
            {
                int j = i + _rng.Next(inCone.Count - i);
                Agent tmp = inCone[i]; inCone[i] = inCone[j]; inCone[j] = tmp;
            }
            for (int i = 0; i < killCount; i++)
            {
                BeginAgentGlow(inCone[i], ColorSchool.Purple, 1.5f);
                SpawnTempLight(inCone[i].Position, ColorSchool.Purple, 6f, 1.5f);
                QueueKill(inCone[i]);
            }
            if (killCount == 1)
                Msg($"Grey Harvest — {inCone[0].Name} fades. The purple was always going to take them.", ColorSchool.Purple);
            else
                Msg($"Grey Harvest — {killCount} souls fade. The purple does not discriminate.", ColorSchool.Purple);
        }

        // Helper: returns all active non-mount agents in a cone (both allies and enemies)
        private static List<Agent> ConeAgents(Vec3 origin, Vec3 fwd, float range, float dot)
        {
            if (Mission.Current == null) return new List<Agent>();
            var result = new List<Agent>();
            try
            {
                foreach (Agent a in Mission.Current.Agents)
                {
                    if (!a.IsActive() || a.IsMount || a == Player) continue;
                    Vec3 to = a.Position - origin;
                    if (to.Length > range) continue;
                    if (Vec3.DotProduct(fwd, to.NormalizedCopy()) < dot) continue;
                    result.Add(a);
                }
            }
            catch { }
            return result;
        }
    }
}
