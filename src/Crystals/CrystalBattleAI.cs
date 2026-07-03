// =============================================================================
// ASH AND EMBER — Crystals/CrystalBattleAI.cs
//
// Lets NPC lords who carry crystals use them in battle. Scans agents every 5 s;
// qualifies if: the agent's hero has a crystal in their BattleEquipment, it is
// daytime, and their personal cooldown has expired. Effect fires immediately
// (no charge animation) via CrystalEffects.FireEffect. No burndown for NPCs —
// they do not use crystals aggressively enough to shatter them in a single fight.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static class CrystalBattleAI
    {
        private static readonly Random _rng = new Random();
        private static readonly Dictionary<Agent, float> _cooldowns = new Dictionary<Agent, float>();
        private static float _scanAccum;

        private const float ScanInterval  = 5f;
        private const float AgentCooldown = 30f; // seconds between NPC crystal uses

        public static void Reset()
        {
            _cooldowns.Clear();
            _scanAccum = 0f;
        }

        public static void MissionTick(float dt)
        {
            var mission = Mission.Current;
            if (mission == null || mission.CurrentState != Mission.State.Continuing) return;

            // Tick down cooldowns.
            foreach (var a in _cooldowns.Keys.ToList())
            {
                float t = _cooldowns[a] - dt;
                if (t <= 0f || a == null || !a.IsActive()) _cooldowns.Remove(a);
                else _cooldowns[a] = t;
            }

            _scanAccum += dt;
            if (_scanAccum < ScanInterval) return;
            _scanAccum = 0f;

            // Daylight check (read once per scan).
            float hour = 12f;
            try { hour = (float)CampaignTime.Now.CurrentHourInDay; } catch { }
            bool isDaylight = CrystalMath.IsDaylight(hour);
            if (!isDaylight) return;

            try
            {
                foreach (Agent a in mission.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount || !a.IsHuman || a == Agent.Main) continue;
                    if (_cooldowns.ContainsKey(a)) continue;

                    Hero hero = HeroOf(a);
                    if (hero == null) continue;

                    CrystalType crystalType;
                    if (!TryFindEquippedCrystal(hero, out crystalType)) continue;

                    // Situational use: heal-stones want their bearer hurt, offensive
                    // stones want enemies in reach — never spent on empty air.
                    float hpFrac = 1f;
                    try { hpFrac = a.Health / Math.Max(a.HealthLimit, 1f); } catch { }
                    int enemiesInRange = CountEnemiesInRange(a, CrystalMath.CrystalUseRange(crystalType));
                    if (!CrystalMath.NpcShouldUse(crystalType, hpFrac, enemiesInRange, (float)_rng.NextDouble()))
                        continue;

                    CrystalEffects.FireEffect(a, crystalType);
                    _cooldowns[a] = AgentCooldown;
                    AnnounceUse(a, hero, crystalType);
                }
            }
            catch { }
        }

        // ── Equipment scan ────────────────────────────────────────────────────

        private static bool TryFindEquippedCrystal(Hero hero, out CrystalType type)
        {
            type = CrystalType.Sunstone;
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    var elem = hero.BattleEquipment[(EquipmentIndex)i];
                    if (elem.IsEmpty) continue;
                    string id = elem.Item?.StringId ?? "";
                    if (CrystalCatalog.TryGetByItemId(id, out var def))
                    {
                        type = def.Type;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        // ── Announce ──────────────────────────────────────────────────────────

        private static void AnnounceUse(Agent a, Hero hero, CrystalType type)
        {
            try
            {
                if (Agent.Main == null || a.Team == Agent.Main.Team) return;
                string name    = hero?.Name?.ToString() ?? a.Name ?? "An enemy";
                string crystal = CrystalCatalog.Get(type).Name;
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{name} activates a {crystal}!",
                    new Color(0.75f, 0.65f, 0.45f)));
            }
            catch { }
        }

        // ── Helper ────────────────────────────────────────────────────────────

        private static Hero HeroOf(Agent a)
        {
            try { return (a.Character as TaleWorlds.CampaignSystem.CharacterObject)?.HeroObject; }
            catch { return null; }
        }

        private static int CountEnemiesInRange(Agent a, float range)
        {
            try
            {
                float r2 = range * range;
                int n = 0;
                foreach (Agent e in SpellEffects.EnemiesOf(a))
                {
                    float dx = e.Position.x - a.Position.x, dy = e.Position.y - a.Position.y;
                    if (dx * dx + dy * dy > r2) continue;
                    // A bearer doesn't waste a stone on foes a wall already bars.
                    try { if (ElementWallWards.BlocksCrystal(a.Position, e.Position)) continue; } catch { }
                    n++;
                }
                return n;
            }
            catch { return 0; }
        }
    }
}
