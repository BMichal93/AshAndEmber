// =============================================================================
// ASH AND EMBER — Alchemy/AlchemyBattleAI.cs
//
// Lets NPC heroes reach for a vial in the heat of battle — the alchemists of the
// south especially. Throttled to roughly once a second, it scans hero agents,
// gates them by culture + Medicine (AlchemyMath.NpcBattleUseChance), and has the
// chosen ones drink an elixir. Their Medicine skill decides whether the brew is
// clean or backfires (the same rule the player lives by). Enemy uses are posted
// to the combat log exactly the way enemy spell casts are (AnnounceEnemyCast),
// so the player always gets the warning the brief demands.
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
    public static class AlchemyBattleAI
    {
        private static readonly Random _rng = new Random();
        private static readonly Dictionary<Agent, float> _cooldowns = new Dictionary<Agent, float>();
        private static float _scanAccum;

        private const float ScanInterval   = 1.0f;  // seconds between scans
        private const float UseCooldownSec  = 30f;   // per-agent rest after a draught
        private const int   MinMedicineGate = 30;    // non-Aserai need some training

        public static void Reset()
        {
            _cooldowns.Clear();
            _scanAccum = 0f;
        }

        public static void MissionTick(float dt)
        {
            var mission = Mission.Current;
            if (mission == null || mission.CurrentState != Mission.State.Continuing) return;

            // Decay per-agent cooldowns continuously.
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

                    Hero hero = HeroOf(a);
                    if (hero == null || hero == Hero.MainHero) continue;

                    bool aserai = IsAserai(hero);
                    int medicine = SafeMedicine(hero);
                    if (!aserai && medicine < MinMedicineGate) continue;

                    if (_rng.NextDouble() >= AlchemyMath.NpcBattleUseChance(medicine, aserai)) continue;

                    DrinkSomething(a, hero, medicine);
                    _cooldowns[a] = UseCooldownSec;
                }
            }
            catch { }
        }

        // Chooses a battle elixir to suit the moment, then resolves it through the
        // shared effect code — clean or tainted by the hero's Medicine.
        private static void DrinkSomething(Agent a, Hero hero, int medicine)
        {
            ElixirType type = ChooseElixir(a, hero);
            bool clean = AlchemyMath.IsBrewSuccess(medicine, _rng.NextDouble());

            string name = AlchemyCatalog.Name(type);
            if (clean)
            {
                AlchemyEffects.ApplyBattleEffect(a, type, announce: false);
                Announce(a, hero, $"drinks a {name}.");
            }
            else
            {
                AlchemyEffects.ApplyBattleBackfire(a, AlchemyMath.PickBackfire(_rng.NextDouble()), announce: false);
                Announce(a, hero, $"drinks a {name} — but it was ill-brewed; it turns on them!");
            }
        }

        private static ElixirType ChooseElixir(Agent a, Hero hero)
        {
            // Wounded → mend. A valorous fighter favours fury; a careful one
            // favours stone-skin. Otherwise pick from the battle-usable set.
            try
            {
                if (a.Health < a.HealthLimit * 0.45f) return ElixirType.HealingDraught;
            }
            catch { }

            int valor = 0;
            try { valor = hero.GetTraitLevel(DefaultTraits.Valor); } catch { }
            if (valor > 0 && _rng.Next(2) == 0) return ElixirType.EmberBrew;

            var battleSet = new[]
            {
                ElixirType.EmberBrew, ElixirType.CausticVial,
                ElixirType.StonebloodTonic, ElixirType.VeilOfAsh,
                ElixirType.HoarfrostDraught, ElixirType.PyrebloodPhiltre,
            };
            return battleSet[_rng.Next(battleSet.Length)];
        }

        // Combat-log notice — enemy uses only, mirroring ColourLordAI.AnnounceEnemyCast.
        private static void Announce(Agent a, Hero hero, string blurb)
        {
            try
            {
                if (Agent.Main == null) return;
                if (a.Team == Agent.Main.Team) return;
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{hero.Name} — {blurb}", new Color(0.55f, 0.75f, 0.45f)));
            }
            catch { }
        }

        // ── helpers ──────────────────────────────────────────────────────────
        private static Hero HeroOf(Agent a)
        {
            try { return (a.Character as CharacterObject)?.HeroObject; }
            catch { return null; }
        }

        private static bool IsAserai(Hero hero)
        {
            try { return hero.Clan?.Culture?.StringId == "aserai"; }
            catch { return false; }
        }

        private static int SafeMedicine(Hero hero)
        {
            try { return hero.GetSkillValue(DefaultSkills.Medicine); }
            catch { return 0; }
        }
    }
}
