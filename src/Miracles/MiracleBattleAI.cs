// =============================================================================
// ASH AND EMBER — Miracles/MiracleBattleAI.cs
//
// Lets NPC heroes and priest troops invoke Grace miracles in battle. Grace lords
// (high Honor + Mercy) and Grace priests draw from an unlimited divine wellspring
// — they never spend the player's Grace. Dark lords express themselves through
// the Dark Gift system (DarkGiftBattleEffects), not Cold miracles.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static class MiracleBattleAI
    {
        private static readonly Random _rng = new Random();
        private static readonly Dictionary<Agent, float> _cooldowns = new Dictionary<Agent, float>();
        private static float _scanAccum;

        private const float ScanInterval  = 1.5f;
        private const float AgentCooldown = 40f;

        public static void Reset()
        {
            _cooldowns.Clear();
            _scanAccum = 0f;
        }

        public static void MissionTick(float dt)
        {
            var mission = Mission.Current;
            if (mission == null || mission.CurrentState != Mission.State.Continuing) return;

            if (_cooldowns.Count > 0)
                foreach (var a in _cooldowns.Keys.ToList())
                {
                    float t = _cooldowns[a] - dt;
                    if (t <= 0f || a == null || !a.IsActive()) _cooldowns.Remove(a);
                    else _cooldowns[a] = t;
                }

            _scanAccum += dt;
            if (_scanAccum < ScanInterval) return;
            _scanAccum = 0f;

            try
            {
                foreach (Agent a in mission.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount || !a.IsHuman || a == Agent.Main) continue;
                    if (_cooldowns.ContainsKey(a)) continue;

                    bool isPriest = IsPriest(a);
                    Hero hero = HeroOf(a);

                    bool isGraceLord = isPriest
                        ? IsGracePriest(a)
                        : (hero != null && IsGraceLord(hero));

                    if (!isGraceLord) continue;

                    double chance = MiracleMath.NpcBattleUseChance(isPriest);
                    if (_rng.NextDouble() >= chance) continue;

                    MiracleType type = ChooseGraceMiracle(a);
                    MiracleEffects.ApplyBattleMiracle(a, type, announce: true);
                    _cooldowns[a] = AgentCooldown;
                    AnnounceEnemy(a, hero, type);
                }
            }
            catch { }
        }

        private static MiracleType ChooseGraceMiracle(Agent a)
        {
            // Read the moment, then let MiracleMath choose the fitting miracle — heal
            // when hurt, judge the Ashen, ward/bless a wounded line, shield under a
            // press, otherwise rally. Grace itself is unlimited (no resource gate).
            bool selfHurt = false;
            try { selfHurt = a.Health < a.HealthLimit * 0.40f; } catch { }

            int alliesHurtNear = 0, enemiesPressing = 0;
            try
            {
                Vec3 pos = a.Position;
                foreach (Agent ally in SpellEffects.AlliesOf(a))
                {
                    if (ally == a) continue;
                    if (ally.Health >= ally.HealthLimit * 0.5f) continue;
                    float dx = ally.Position.x - pos.x, dy = ally.Position.y - pos.y;
                    if (dx * dx + dy * dy <= 12f * 12f) alliesHurtNear++;
                }
                foreach (Agent e in SpellEffects.EnemiesOf(a))
                {
                    float dx = e.Position.x - pos.x, dy = e.Position.y - pos.y;
                    if (dx * dx + dy * dy <= 6f * 6f) enemiesPressing++;
                }
            }
            catch { }

            return MiracleMath.ChooseBattleMiracle(
                selfHurt, alliesHurtNear, enemiesPressing >= 2, AshenNearby(a), (float)_rng.NextDouble());
        }

        private static void AnnounceEnemy(Agent a, Hero hero, MiracleType type)
        {
            try
            {
                if (Agent.Main == null) return;
                if (a.Team == Agent.Main.Team) return;
                string name = hero?.Name?.ToString() ?? a.Name ?? "An enemy";
                string miracle = MiracleCatalog.Get(type).Name;
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{name} invokes {miracle}!",
                    new Color(0.80f, 0.65f, 0.30f)));
            }
            catch { }
        }

        // ── Classification helpers ─────────────────────────────────────────────
        private static bool IsPriest(Agent a)
        {
            try
            {
                string name = a.Character?.Name?.ToString() ?? "";
                return name.IndexOf("Priest", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private static bool IsGracePriest(Agent a)
        {
            try
            {
                string name = a.Character?.Name?.ToString() ?? "";
                // Grace priests: Priest of the Flame, Sanctuary Priest, etc.
                // Ashen Priests and Dark Priests use Dark Gifts, not Grace miracles.
                return name.IndexOf("Ashen", StringComparison.OrdinalIgnoreCase) < 0
                    && name.IndexOf("Dark",  StringComparison.OrdinalIgnoreCase) < 0;
            }
            catch { return true; }
        }

        private static bool IsGraceLord(Hero hero)
        {
            try
            {
                int honor = hero.GetTraitLevel(DefaultTraits.Honor);
                int mercy = hero.GetTraitLevel(DefaultTraits.Mercy);
                return honor >= 1 && mercy >= 1;
            }
            catch { return false; }
        }

        private static bool AshenNearby(Agent a)
        {
            if (Mission.Current == null || a.Team == null) return false;
            try
            {
                Vec3 pos = a.Position;
                float r2 = 12f * 12f;
                foreach (Agent e in Mission.Current.Agents)
                {
                    if (!e.IsActive() || e.Team == a.Team) continue;
                    float dx = e.Position.x - pos.x, dy = e.Position.y - pos.y;
                    if (dx * dx + dy * dy < r2)
                    {
                        var charObj = e.Character as TaleWorlds.CampaignSystem.CharacterObject;
                        if (charObj?.Culture?.StringId == "ashen_kingdom") return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static Hero HeroOf(Agent a)
        {
            try { return (a.Character as TaleWorlds.CampaignSystem.CharacterObject)?.HeroObject; }
            catch { return null; }
        }
    }
}
