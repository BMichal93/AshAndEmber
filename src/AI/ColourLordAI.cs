// =============================================================================
// COLOURS OF CALRADIA — AI/ColourLordAI.cs
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
    // =========================================================================
    // 10. COLOUR LORD AI
    //     Finds hero agents with colour schools and has them cast spells in battle.
    //     NPC limitations: Blue → no weapon (Scholar's Craft), Yellow → no horseback,
    //     Orange → party morale ≥ 45. Green has no battle limitation (Nature's Calling
    //     is campaign-map only: cannot cast inside settlements).
    //     5% chance of 3s interruption after each cast (non-Blight, non-Prism lords).
    //     Impulsive lords cast more often; Calculating lords less often.
    // =========================================================================
    public static class ColourLordAI
    {
        private const float CastInterval       = 12f;
        private const float PrismCastInterval  = 4f;
        private const float BlightCastInterval = 2f;
        private static readonly Dictionary<string, float> _cooldowns        = new Dictionary<string, float>();
        private static readonly Random _rng = new Random();

        private static float _tickAccum = 0f;
        private const  float TickInterval = 0.5f;

        public static void ClearCooldowns()
        {
            _cooldowns.Clear();
        }

        public static void MissionTick(float dt)
        {
            if (Mission.Current == null) return;

            _tickAccum += dt;
            if (_tickAccum < TickInterval) return;
            _tickAccum = 0f;

            if (!Mission.Current.AllowAiTicking) return;
            if (!SpellEffects.IsBattleMission()) return;

            foreach (string key in _cooldowns.Keys.ToList())
            {
                _cooldowns[key] -= TickInterval;
                if (_cooldowns[key] <= 0f) _cooldowns.Remove(key);
            }

            foreach (Agent agent in Mission.Current.Agents.ToList())
            {
                if (!agent.IsActive() || agent.IsMount || !agent.IsHero) continue;
                if (agent == Agent.Main) continue;

                Hero hero = (agent.Character as CharacterObject)?.HeroObject;
                if (hero == null) continue;

                // Blights cast much faster and without NPC limitations
                if (BlightSystem.IsBlight(hero))
                {
                    if (!_cooldowns.ContainsKey(hero.StringId))
                        CastBlightSpell(agent, hero);
                    continue;
                }

                if (!ColourLordRegistry.IsColourLord(hero)) continue;
                if (_cooldowns.ContainsKey(hero.StringId)) continue;

                var colors = ColourLordRegistry.GetColors(hero);
                if (colors.Count == 0) continue;

                // Prism lord casts randomly and very often
                if (ColourLordRegistry.IsPrismLord(hero))
                {
                    TryCastRandom(agent, hero, colors);
                    continue;
                }

                DecideAndCast(agent, hero, colors);
            }
        }

        private static void DecideAndCast(Agent agent, Hero hero, IReadOnlyList<ColorSchool> colors)
        {
            if (!CanCastAny(agent, colors)) return;

            float hpPct = agent.Health / Math.Max(agent.HealthLimit, 1f);

            // Self-heal with Green (Verdant Touch) when badly hurt
            if (hpPct < 0.35f && colors.Contains(ColorSchool.Green))
            {
                float greenPower = SpellEffects.SpellPower(ColorSchool.Green, hero);
                CastWithGlow(agent, hero, ColorSchool.Green, "Verdant Touch", () =>
                {
                    agent.Health = Math.Min(agent.Health + 20f * greenPower, agent.HealthLimit);
                });
                return;
            }

            // 8% random wild cast
            if (_rng.Next(100) < 8) { TryCastRandom(agent, hero, colors); return; }

            // Enemies swarming — Cinder Burst (Red) or Grey Tide (Purple)
            int closeEnemies = CountEnemiesNear(agent, 8f);
            if (closeEnemies >= 3)
            {
                if (colors.Contains(ColorSchool.Purple))
                {
                    float purplePower = SpellEffects.SpellPower(ColorSchool.Purple, hero);
                    CastWithGlow(agent, hero, ColorSchool.Purple, "Grey Tide", () =>
                    {
                        foreach (Agent a in EnemiesOf(agent).Where(a => a.Position.Distance(agent.Position) <= 8f).ToList())
                        {
                            if (SpellEffects.ProtectedByMirror(a)) continue;
                            SpellEffects.DamageAgent(a, 45f * purplePower);
                            SpellEffects.BeginAgentGlow(a, ColorSchool.Purple, 1.5f);
                        }
                    });
                    return;
                }
                if (colors.Contains(ColorSchool.Red))
                {
                    float redPower = SpellEffects.SpellPower(ColorSchool.Red, hero);
                    CastWithGlow(agent, hero, ColorSchool.Red, "Crimson Torrent", () =>
                    {
                        Vec3 fwd = agent.LookDirection.NormalizedCopy();
                        foreach (Agent a in EnemiesOf(agent).ToList())
                        {
                            Vec3 to = a.Position - agent.Position;
                            if (to.Length > 15f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.6f) continue;
                            if (SpellEffects.ProtectedByMirror(a)) continue;
                            SpellEffects.DamageAgent(a, 40f * redPower);
                            SpellEffects.BeginAgentGlow(a, ColorSchool.Red, 1.5f);
                        }
                    });
                    ApplyRedA1(agent); ApplyRedA2(agent);
                    return;
                }
            }

            // Cone enemies — Crimson Torrent (Red) or Azure Arrest (Blue, requires no weapon)
            int coneEnemies = CountEnemiesInCone(agent, 15f, 0.6f);
            if (coneEnemies >= 2)
            {
                if (colors.Contains(ColorSchool.Red))
                {
                    float redPower = SpellEffects.SpellPower(ColorSchool.Red, hero);
                    CastWithGlow(agent, hero, ColorSchool.Red, "Crimson Torrent", () =>
                    {
                        Vec3 fwd = agent.LookDirection.NormalizedCopy();
                        foreach (Agent a in EnemiesOf(agent).ToList())
                        {
                            Vec3 to = a.Position - agent.Position;
                            if (to.Length > 15f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.6f) continue;
                            if (SpellEffects.ProtectedByMirror(a)) continue;
                            SpellEffects.DamageAgent(a, 40f * redPower);
                            SpellEffects.BeginAgentGlow(a, ColorSchool.Red, 1.5f);
                        }
                    });
                    ApplyRedA1(agent); ApplyRedA2(agent);
                    return;
                }
                if (colors.Contains(ColorSchool.Blue) && CanUseBlue(agent))
                {
                    CastWithGlow(agent, hero, ColorSchool.Blue, "Azure Arrest", () =>
                    {
                        Vec3 fwd = agent.LookDirection.NormalizedCopy();
                        var formations = new System.Collections.Generic.HashSet<Formation>();
                        foreach (Agent a in EnemiesOf(agent).ToList())
                        {
                            Vec3 to = a.Position - agent.Position;
                            if (to.Length > 15f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.6f) continue;
                            try { a.SetMorale(0f); } catch { }
                            SpellEffects.BeginAgentGlow(a, ColorSchool.Blue, 1.5f);
                            if (a.Formation != null) formations.Add(a.Formation);
                        }
                        foreach (Formation f in formations)
                            try { f.SetMovementOrder(MovementOrder.MovementOrderStop); } catch { }
                    });
                    return;
                }
            }

            // Ally support — Verdant Surge (Green) cone heal (no battle limitation)
            if (colors.Contains(ColorSchool.Green))
            {
                bool allyHurt = AlliesOf(agent).Any(a => a.Health < a.HealthLimit * 0.6f &&
                                                    a.Position.Distance(agent.Position) <= 15f);
                if (allyHurt)
                {
                    float greenPower = SpellEffects.SpellPower(ColorSchool.Green, hero);
                    CastWithGlow(agent, hero, ColorSchool.Green, "Verdant Surge", () =>
                    {
                        Vec3 fwd = agent.LookDirection.NormalizedCopy();
                        foreach (Agent a in AlliesOf(agent).ToList())
                        {
                            Vec3 to = a.Position - agent.Position;
                            if (to.Length > 15f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.6f) continue;
                            float h = Math.Min(15f * greenPower, a.HealthLimit - a.Health);
                            if (h > 0f) { a.Health += h; SpellEffects.BeginAgentGlow(a, ColorSchool.Green, 1.5f); }
                        }
                    });
                    return;
                }
            }

            // Yellow — Tide of Dread morale drain (no horseback)
            if (colors.Contains(ColorSchool.Yellow) && CanUseYellow(agent))
            {
                float yellowPower = SpellEffects.SpellPower(ColorSchool.Yellow, hero);
                CastWithGlow(agent, hero, ColorSchool.Yellow, "Tide of Dread", () =>
                {
                    Vec3 fwd = agent.LookDirection.NormalizedCopy();
                    foreach (Agent a in EnemiesOf(agent).ToList())
                    {
                        Vec3 to = a.Position - agent.Position;
                        if (to.Length > 15f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.6f) continue;
                        try { a.SetMorale(Math.Max(0f, a.GetMorale() - 30f * yellowPower)); } catch { }
                        SpellEffects.BeginAgentGlow(a, ColorSchool.Yellow, 1.5f);
                    }
                });
                return;
            }

            // Orange — Calling (summon) if outnumbered, else Warm Beacon (ally pull) (morale ≥ 45)
            if (colors.Contains(ColorSchool.Orange) && CanUseOrange(hero))
            {
                int nearAllies  = AlliesOf(agent).Count(a => a.Position.Distance(agent.Position) <= 20f);
                int nearEnemies = EnemiesOf(agent).Count(a => a.Position.Distance(agent.Position) <= 20f);
                if (nearEnemies > nearAllies)
                {
                    CastWithGlow(agent, hero, ColorSchool.Orange, "Calling", () =>
                    {
                        CharacterObject recruit = SpellEffects.FindRecruit(agent);
                        if (recruit == null || Mission.Current == null) return;
                        int count = _rng.Next(2, 4);
                        Vec3 back = -agent.LookDirection.NormalizedCopy(); back.z = 0f;
                        if (back.Length < 0.01f) back = new Vec3(-1f, 0f, 0f); else back = back.NormalizedCopy();
                        Vec3 perp = new Vec3(-back.y, back.x, 0f);
                        for (int i = 0; i < count; i++)
                        {
                            try
                            {
                                float spread = (i - count / 2f) * 1.5f;
                                Vec3 pos = agent.Position + back * 3f + perp * spread;
                                pos.z = agent.Position.z;
                                Vec2 facing = (-back).AsVec2;
                                AgentBuildData abd = new AgentBuildData(recruit)
                                    .Team(agent.Team).InitialPosition(in pos).InitialDirection(in facing);
                                Agent spawned = Mission.Current.SpawnAgent(abd, false);
                                if (spawned == null) continue;
                                spawned.SetWatchState(Agent.WatchState.Alarmed);
                                SpellEffects.BeginAgentGlow(spawned, ColorSchool.Orange, 2f);
                            }
                            catch { }
                        }
                    });
                    return;
                }
                float orangePower = SpellEffects.SpellPower(ColorSchool.Orange, hero);
                CastWithGlow(agent, hero, ColorSchool.Orange, "Warm Beacon", () =>
                {
                    foreach (Agent a in AlliesOf(agent).Where(a => a.Position.Distance(agent.Position) <= 20f).ToList())
                    {
                        try { a.SetMorale(Math.Min(a.GetMorale() + 20f * orangePower, 100f)); } catch { }
                        SpellEffects.BeginAgentGlow(a, ColorSchool.Orange, 1.5f);
                    }
                });
            }
        }

        private static void TryCastRandom(Agent agent, Hero hero, IReadOnlyList<ColorSchool> colors)
        {
            if (!CanCastAny(agent, colors)) return;
            ColorSchool school = colors[_rng.Next(colors.Count)];
            switch (school)
            {
                case ColorSchool.Red:
                {
                    float redPower = SpellEffects.SpellPower(ColorSchool.Red, hero);
                    CastWithGlow(agent, hero, ColorSchool.Red, "Crimson Torrent", () =>
                    {
                        Vec3 fwd = agent.LookDirection.NormalizedCopy();
                        foreach (Agent a in EnemiesOf(agent).ToList())
                        {
                            Vec3 to = a.Position - agent.Position;
                            if (to.Length > 15f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.6f) continue;
                            if (SpellEffects.ProtectedByMirror(a)) continue;
                            SpellEffects.DamageAgent(a, 40f * redPower);
                            SpellEffects.BeginAgentGlow(a, ColorSchool.Red, 1.5f);
                        }
                    });
                    ApplyRedA1(agent); ApplyRedA2(agent);
                    break;
                }
                case ColorSchool.Orange when CanUseOrange(hero):
                {
                    float orangePower = SpellEffects.SpellPower(ColorSchool.Orange, hero);
                    CastWithGlow(agent, hero, ColorSchool.Orange, "Warm Beacon", () =>
                    {
                        foreach (Agent a in AlliesOf(agent).Where(a => a.Position.Distance(agent.Position) <= 20f).ToList())
                        {
                            try { a.SetMorale(Math.Min(a.GetMorale() + 20f * orangePower, 100f)); } catch { }
                            SpellEffects.BeginAgentGlow(a, ColorSchool.Orange, 1.5f);
                        }
                    });
                    break;
                }
                case ColorSchool.Green:
                {
                    float greenPower = SpellEffects.SpellPower(ColorSchool.Green, hero);
                    CastWithGlow(agent, hero, ColorSchool.Green, "Verdant Surge", () =>
                    {
                        Vec3 fwd = agent.LookDirection.NormalizedCopy();
                        foreach (Agent a in AlliesOf(agent).ToList())
                        {
                            Vec3 to = a.Position - agent.Position;
                            if (to.Length > 15f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.6f) continue;
                            float h = Math.Min(15f * greenPower, a.HealthLimit - a.Health);
                            if (h > 0f) { a.Health += h; SpellEffects.BeginAgentGlow(a, ColorSchool.Green, 1.5f); }
                        }
                    });
                    break;
                }
                case ColorSchool.Blue when CanUseBlue(agent):
                    CastWithGlow(agent, hero, ColorSchool.Blue, "Azure Arrest", () =>
                    {
                        foreach (Agent a in EnemiesOf(agent).Where(a => a.Position.Distance(agent.Position) <= 30f).ToList())
                            try { a.SetMorale(0f); SpellEffects.BeginAgentGlow(a, ColorSchool.Blue, 1.5f); } catch { }
                    });
                    break;
                case ColorSchool.Yellow when CanUseYellow(agent):
                {
                    float yellowPower = SpellEffects.SpellPower(ColorSchool.Yellow, hero);
                    CastWithGlow(agent, hero, ColorSchool.Yellow, "Tide of Dread", () =>
                    {
                        foreach (Agent a in EnemiesOf(agent).Where(a => a.Position.Distance(agent.Position) <= 17f).ToList())
                            try { a.SetMorale(Math.Max(0f, a.GetMorale() - 30f * yellowPower)); SpellEffects.BeginAgentGlow(a, ColorSchool.Yellow, 1.5f); } catch { }
                    });
                    break;
                }
                case ColorSchool.Purple:
                    CastWithGlow(agent, hero, ColorSchool.Purple, "Grey Harvest", () =>
                    {
                        var targets = EnemiesOf(agent).Where(a => !a.IsHero).ToList();
                        if (targets.Count > 0)
                        {
                            Agent t = targets[_rng.Next(targets.Count)];
                            SpellEffects.BeginAgentGlow(t, ColorSchool.Purple, 1.5f);
                            SpellEffects.KillAgent(t);
                        }
                    });
                    break;
            }
        }

        // ── Blight casting ────────────────────────────────────────────────────
        // No NPC limitations, no light-level check, cast interval 2s.
        // Returns false when no targets were affected — cooldown still applied but no message.
        private static void CastBlightSpell(Agent agent, Hero hero)
        {
            ColorSchool school = BlightSystem.GetBlightSchool(hero);
            float power = SpellEffects.SpellPower(school, hero);
            bool hit = false;

            switch (school)
            {
                case ColorSchool.Red:
                {
                    Vec3 fwd = agent.LookDirection.NormalizedCopy();
                    foreach (Agent a in EnemiesOf(agent).ToList())
                    {
                        Vec3 to = a.Position - agent.Position;
                        if (to.Length > 15f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.6f) continue;
                        if (SpellEffects.ProtectedByMirror(a)) continue;
                        SpellEffects.DamageAgent(a, 40f * power, school);
                        SpellEffects.BeginAgentGlow(a, school, 1.5f);
                        hit = true;
                    }
                    CastBlightWithGlow(agent, hero, school, "Crimson Torrent", hit);
                    break;
                }
                case ColorSchool.Orange:
                {
                    foreach (Agent a in AlliesOf(agent)
                        .Where(a => a.Position.Distance(agent.Position) <= 20f).ToList())
                    {
                        try { a.SetMorale(Math.Min(a.GetMorale() + 20f * power, 100f)); } catch { }
                        SpellEffects.BeginAgentGlow(a, school, 1.5f);
                        hit = true;
                    }
                    CastBlightWithGlow(agent, hero, school, "Warm Beacon", hit);
                    break;
                }
                case ColorSchool.Yellow:
                {
                    Vec3 fwd = agent.LookDirection.NormalizedCopy();
                    foreach (Agent a in EnemiesOf(agent).ToList())
                    {
                        Vec3 to = a.Position - agent.Position;
                        if (to.Length > 15f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.6f) continue;
                        try { a.SetMorale(Math.Max(0f, a.GetMorale() - 35f * power)); } catch { }
                        SpellEffects.BeginAgentGlow(a, school, 1.5f);
                        hit = true;
                    }
                    CastBlightWithGlow(agent, hero, school, "Tide of Dread", hit);
                    break;
                }
                case ColorSchool.Green:
                {
                    Vec3 fwd = agent.LookDirection.NormalizedCopy();
                    foreach (Agent a in AlliesOf(agent).ToList())
                    {
                        Vec3 to = a.Position - agent.Position;
                        if (to.Length > 15f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.6f) continue;
                        float h = Math.Min(15f * power, a.HealthLimit - a.Health);
                        if (h <= 0f) continue;
                        a.Health += h;
                        SpellEffects.BeginAgentGlow(a, school, 1.5f);
                        hit = true;
                    }
                    CastBlightWithGlow(agent, hero, school, "Verdant Surge", hit);
                    break;
                }
                case ColorSchool.Blue:
                {
                    Vec3 fwd = agent.LookDirection.NormalizedCopy();
                    foreach (Agent a in EnemiesOf(agent).ToList())
                    {
                        Vec3 to = a.Position - agent.Position;
                        if (to.Length > 15f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.6f) continue;
                        if (SpellEffects.ProtectedByMirror(a)) continue;
                        SpellEffects.DamageAgent(a, 12f * power, school);
                        try { a.SetMorale(0f); } catch { }
                        SpellEffects.BeginAgentGlow(a, school, 1.5f);
                        hit = true;
                    }
                    CastBlightWithGlow(agent, hero, school, "Azure Arrest", hit);
                    break;
                }
                case ColorSchool.Purple:
                {
                    foreach (Agent a in EnemiesOf(agent)
                        .Where(a => a.Position.Distance(agent.Position) <= 10f && !a.IsHero).ToList())
                    {
                        if (SpellEffects.ProtectedByMirror(a)) continue;
                        SpellEffects.DamageAgent(a, 45f * power, school);
                        SpellEffects.BeginAgentGlow(a, school, 1.5f);
                        hit = true;
                    }
                    CastBlightWithGlow(agent, hero, school, "Grey Tide", hit);
                    break;
                }
            }
        }

        // Applies cooldown + glow every cast; message only when the effect connected.
        private static void CastBlightWithGlow(Agent agent, Hero hero, ColorSchool school,
                                                string spellName, bool connected)
        {
            _cooldowns[hero.StringId] = BlightCastInterval;
            // Glow duration > cast interval → permanent visible aura
            SpellEffects.BeginAgentGlow(agent, school, BlightCastInterval * 1.5f);
            SpellEffects.TryCastSound(agent.Position, school);
            SpellEffects.TryCastAnimation(agent);
            if (!connected) return;
            InformationManager.DisplayMessage(new InformationMessage(
                $"✦ {agent.Name} channels {spellName} ({ColorSchoolData.Info[school].Name}). The Blight is upon you. ✦",
                ColorSchoolData.GetMessageColor(school)));
        }

        // ── Limitation checks ─────────────────────────────────────────────────
        // Dark is only passable if at least one of the agent's schools has a seasonal/city affinity.
        private static bool CanCastAny(Agent agent, IReadOnlyList<ColorSchool> colors)
        {
            var light = SpellEffects.GetLightLevel();
            if (light == SpellEffects.LightLevel.Bright) return true;
            if (light == SpellEffects.LightLevel.Dark)
            {
                if (colors == null || !colors.Any(SpellEffects.HasDarkAffinity)) return false;
            }
            return !SpellEffects.RollDimFizzle(); // 33 % fizzle for Dim or Dark-with-affinity
        }

        private static bool CanUseBlue(Agent agent)
        {
            if (agent == null) return true;
            try
            {
                var wielded = agent.WieldedWeapon;
                if (wielded.IsEmpty) return true;
                if (wielded.CurrentUsageItem?.IsShield == true) return true;
                if (wielded.CurrentUsageItem?.WeaponClass == WeaponClass.Boulder) return true;
                return false;
            }
            catch { return true; }
        }

        private static bool CanUseYellow(Agent agent)
        {
            if (agent == null) return true;
            try { return agent.MountAgent == null; }
            catch { return true; }
        }

        private static bool CanUseOrange(Hero hero)
        {
            try { return (hero?.PartyBelongedTo?.RecentEventsMorale ?? 100f) >= 45f; }
            catch { return true; }
        }

        // ── Post-cast limitation side effects ─────────────────────────────────
        private static void ApplyRedA1(Agent agent)
        {
            if (agent?.Team == null || Mission.Current == null) return;
            foreach (Formation f in agent.Team.FormationsIncludingSpecialAndEmpty)
                try { if (f.CountOfUnits > 0) f.SetMovementOrder(MovementOrder.MovementOrderCharge); } catch { }
        }

        private static void ApplyRedA2(Agent agent)
        {
            if (agent == null) return;
            try { agent.Health = Math.Max(1f, agent.Health - 2f); } catch { }
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static int CountEnemiesNear(Agent agent, float radius) =>
            EnemiesOf(agent).Count(a => a.Position.Distance(agent.Position) < radius);

        private static int CountEnemiesInCone(Agent agent, float radius, float dot)
        {
            Vec3 fwd = agent.LookDirection.NormalizedCopy();
            return EnemiesOf(agent).Count(a =>
            {
                Vec3 to = a.Position - agent.Position;
                return to.Length < radius && Vec3.DotProduct(fwd, to.NormalizedCopy()) > dot;
            });
        }

        private static IEnumerable<Agent> EnemiesOf(Agent agent)
        {
            if (Mission.Current == null || agent?.Team == null) yield break;
            foreach (Agent a in Mission.Current.Agents)
                if (a != agent && !a.IsMount && a.IsActive() && a.Team != null && a.Team != agent.Team)
                    yield return a;
        }

        private static IEnumerable<Agent> AlliesOf(Agent agent)
        {
            if (Mission.Current == null || agent?.Team == null) yield break;
            foreach (Agent a in Mission.Current.Agents)
                if (a != agent && !a.IsMount && a.IsActive() && a.Team != null && a.Team == agent.Team)
                    yield return a;
        }

        private static void CastWithGlow(Agent agent, Hero hero, ColorSchool school,
                                          string spellName, Action effect)
        {
            try { effect?.Invoke(); } catch { }
            SetCooldown(hero);
            SpellEffects.BeginAgentGlow(agent, school, 3.0f);
            SpellEffects.TryCastSound(agent.Position, school);
            SpellEffects.TryCastAnimation(agent);

            InformationManager.DisplayMessage(new InformationMessage(
                $"{agent.Name} channels {spellName} ({ColorSchoolData.Info[school].Name}).",
                ColorSchoolData.GetMessageColor(school)));

            // 5% interruption from Oversaturation (non-Blight, non-Prism lords)
            if (!BlightSystem.IsBlight(hero) && !ColourLordRegistry.IsPrismLord(hero) && _rng.Next(100) < 5)
            {
                try
                {
                    if (agent.IsActive())
                        SaturationSystem.ApplyKnockdown(agent, 3f);
                }
                catch { }
            }
        }

        private static float GetCooldownForHero(Hero hero)
        {
            float baseInterval = ColourLordRegistry.IsPrismLord(hero) ? PrismCastInterval : CastInterval;
            try
            {
                int calc = hero.GetTraitLevel(DefaultTraits.Calculating);
                if (calc < 0) return baseInterval * 0.75f; // Impulsive — casts more often
                if (calc > 0) return baseInterval * 1.5f;  // Calculating — casts less often
            }
            catch { }
            return baseInterval;
        }

        private static void SetCooldown(Hero hero) =>
            _cooldowns[hero.StringId] = GetCooldownForHero(hero);
    }
}
