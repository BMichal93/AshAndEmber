// =============================================================================
// ASH AND EMBER — Miracles/MiracleBattleAI.cs
//
// Lets NPC heroes and priest troops invoke miracles in battle. Grace lords
// (high Honor + Mercy) and Cold lords (low Honor + Mercy) both have access to
// their respective miracle set. Priest detection uses a name prefix — troops
// whose character name begins with "Priest" count as priests and get a higher
// use-chance. NPCs never spend the player's Grace/Cold; they draw from an
// unlimited divine (or diabolic) wellspring.
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

                    // Gate: hero AND their traits, or priest troops.
                    bool isGraceLord = isPriest
                        ? IsGracePriest(a)
                        : (hero != null && IsGraceLord(hero));
                    bool isColdLord = isPriest
                        ? IsColdPriest(a)
                        : (hero != null && IsColdLord(hero));

                    if (!isGraceLord && !isColdLord) continue;

                    double chance = MiracleMath.NpcBattleUseChance(isPriest);
                    if (_rng.NextDouble() >= chance) continue;

                    MiracleType type = isGraceLord
                        ? ChooseGraceMiracle(a)
                        : ChooseColdMiracle(a);

                    MiracleEffects.ApplyBattleMiracle(a, type, announce: true);
                    _cooldowns[a] = AgentCooldown;
                    AnnounceEnemy(a, hero, type);
                }
            }
            catch { }
        }

        private static MiracleType ChooseGraceMiracle(Agent a)
        {
            try { if (a.Health < a.HealthLimit * 0.40f) return MiracleType.RadiantMending; } catch { }

            // Repel Ashen if enemy Ashen are visible nearby.
            if (AshenNearby(a)) return MiracleType.RepelAshen;

            var set = new[]
            {
                MiracleType.LightOfGuidance, MiracleType.SacredFlame,
                MiracleType.AegisOfFaith,    MiracleType.RadiantMending,
            };
            return set[_rng.Next(set.Length)];
        }

        private static MiracleType ChooseColdMiracle(Agent a)
        {
            try { if (a.Health < a.HealthLimit * 0.40f) return MiracleType.Dreadmending; } catch { }

            var set = new[]
            {
                MiracleType.AshenCurse,    MiracleType.DreadPresence,
                MiracleType.FrostBrand,    MiracleType.ShadowShroud,
                MiracleType.Dreadmending,
            };
            return set[_rng.Next(set.Length)];
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
                return name.StartsWith("Priest", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static bool IsGracePriest(Agent a)
        {
            try
            {
                string name = a.Character?.Name?.ToString() ?? "";
                // Grace priests: Priest of the Flame, Sanctuary Priest, etc.
                return !name.Contains("Ashen", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("Cold",  StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("Dark",  StringComparison.OrdinalIgnoreCase);
            }
            catch { return true; }
        }

        private static bool IsColdPriest(Agent a)
        {
            try
            {
                string name = a.Character?.Name?.ToString() ?? "";
                return name.Contains("Ashen", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Cold",  StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Dark",  StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static bool IsGraceLord(Hero hero)
        {
            try
            {
                int honor      = hero.GetTraitLevel(DefaultTraits.Honor);
                int mercy      = hero.GetTraitLevel(DefaultTraits.Mercy);
                return honor >= 1 && mercy >= 1;
            }
            catch { return false; }
        }

        private static bool IsColdLord(Hero hero)
        {
            try
            {
                int honor = hero.GetTraitLevel(DefaultTraits.Honor);
                int mercy = hero.GetTraitLevel(DefaultTraits.Mercy);
                return honor <= -1 && mercy <= -1;
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
