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
    //     12-second warmup before lords/companions cast (blights exempt).
    //     Per cast (non-Blight, non-Prism): 4% lethal (health→1), 5% knockdown — 9% total.
    //     Impulsive lords cast more often; Calculating lords less often.
    // =========================================================================
    public static class ColourLordAI
    {
        private const float CastInterval       = 40f;
        private const float PrismCastInterval  = 4f;
        private const float BlightCastInterval = 2f;
        private static readonly Dictionary<string, float> _cooldowns = new Dictionary<string, float>();
        private static readonly Random _rng = new Random();

        private static float _tickAccum    = 0f;
        private const  float TickInterval  = 0.5f;
        private static float _warmupTimer  = 0f;
        private static bool  _warmupDone   = false;
        private const  float WarmupDuration = 12f;

        private static bool _battleEventTriggered = false;
        private static readonly List<GameEntity> _battleEventLights = new List<GameEntity>();

        public static void ClearCooldowns()
        {
            _cooldowns.Clear();
            _warmupTimer = 0f;
            _warmupDone  = false;
            _battleEventTriggered = false;
            foreach (var e in _battleEventLights) try { e?.Remove(0); } catch { }
            _battleEventLights.Clear();
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

            List<Agent> agents;
            try { agents = Mission.Current.Agents.ToList(); }
            catch { return; }

            // Blights are exempt from warmup — they act immediately.
            foreach (Agent agent in agents)
            {
                if (!agent.IsActive() || agent.IsMount || !agent.IsHero) continue;
                if (agent == Agent.Main) continue;
                Hero hero = (agent.Character as CharacterObject)?.HeroObject;
                if (hero == null || !BlightSystem.IsBlight(hero)) continue;
                if (!_cooldowns.ContainsKey(hero.StringId))
                    CastBlightSpell(agent, hero);
            }

            // Lords and companions wait out the planning-phase cooldown before casting.
            if (!_warmupDone)
            {
                _warmupTimer += TickInterval;
                if (_warmupTimer < WarmupDuration) return;
                _warmupDone = true;
                if (!_battleEventTriggered)
                {
                    _battleEventTriggered = true;
                    try { TryTriggerBattleEvent(); } catch { }
                }
            }

            foreach (Agent agent in agents)
            {
                if (!agent.IsActive() || agent.IsMount || !agent.IsHero) continue;
                if (agent == Agent.Main) continue;

                Hero hero = (agent.Character as CharacterObject)?.HeroObject;
                if (hero == null) continue;

                if (BlightSystem.IsBlight(hero)) continue; // already handled above

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
                float gp = SpellEffects.SpellPower(ColorSchool.Green, hero);
                CastWithGlow(agent, hero, ColorSchool.Green, "Verdant Touch", () =>
                    agent.Health = Math.Min(agent.Health + 30f * gp, agent.HealthLimit));
                return;
            }

            // 8% random wild cast
            if (_rng.Next(100) < 8) { TryCastRandom(agent, hero, colors); return; }

            // ── Swarmed (3+ enemies within 8m) ───────────────────────────────
            int closeEnemies = CountEnemiesNear(agent, 8f);
            if (closeEnemies >= 3)
            {
                if (colors.Contains(ColorSchool.Purple))
                {
                    float pp = SpellEffects.SpellPower(ColorSchool.Purple, hero);
                    if (_rng.Next(2) == 0)
                        CastWithGlow(agent, hero, ColorSchool.Purple, "Grey Tide", () =>
                        {
                            foreach (Agent a in EnemiesOf(agent).Where(a => a.Position.Distance(agent.Position) <= 8f).ToList())
                            {
                                if (SpellEffects.ProtectedByMirror(a)) continue;
                                SpellEffects.DamageAgent(a, 60f * pp);
                                SpellEffects.BeginAgentGlow(a, ColorSchool.Purple, 1.5f);
                            }
                        });
                    else
                        CastWithGlow(agent, hero, ColorSchool.Purple, "Grey Reaping", () =>
                        {
                            foreach (Agent a in EnemiesOf(agent).Where(a => a.Position.Distance(agent.Position) <= 15f).ToList())
                                try { a.SetMorale(0f); SpellEffects.BeginAgentGlow(a, ColorSchool.Purple, 1.5f); } catch { }
                            var kc = EnemiesOf(agent).Where(a => !a.IsHero && a.IsActive() && a.Position.Distance(agent.Position) <= 15f).ToList();
                            if (kc.Count > 0) { var t = kc[_rng.Next(kc.Count)]; SpellEffects.BeginAgentGlow(t, ColorSchool.Purple, 2f); SpellEffects.QueueKill(t); }
                        });
                    return;
                }
                if (colors.Contains(ColorSchool.Red))
                {
                    float rp = SpellEffects.SpellPower(ColorSchool.Red, hero);
                    if (_rng.Next(5) < 3)
                        CastWithGlow(agent, hero, ColorSchool.Red, "Crimson Torrent", () =>
                        {
                            Vec3 fwd = agent.LookDirection.NormalizedCopy();
                            foreach (Agent a in EnemiesOf(agent).ToList())
                            {
                                Vec3 to = a.Position - agent.Position;
                                if (to.Length > 15f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.6f) continue;
                                if (SpellEffects.ProtectedByMirror(a)) continue;
                                SpellEffects.DamageAgent(a, 55f * rp);
                                SpellEffects.BeginAgentGlow(a, ColorSchool.Red, 1.5f);
                            }
                        });
                    else
                        CastWithGlow(agent, hero, ColorSchool.Red, "Cinder Burst", () =>
                        {
                            foreach (Agent a in EnemiesOf(agent).Where(a => a.Position.Distance(agent.Position) <= 10f).ToList())
                            {
                                if (SpellEffects.ProtectedByMirror(a)) continue;
                                SpellEffects.DamageAgent(a, 70f * rp);
                                SpellEffects.BeginAgentGlow(a, ColorSchool.Red, 1.5f);
                            }
                            SpellEffects.SpawnTempLight(agent.Position, ColorSchool.Red, 10f, 1.5f);
                        });
                    ApplyRedA1(agent); ApplyRedA2(agent);
                    return;
                }
            }

            // ── Cone enemies (2+ within 15m) ─────────────────────────────────
            int coneEnemies = CountEnemiesInCone(agent, 15f, 0.6f);
            if (coneEnemies >= 2)
            {
                if (colors.Contains(ColorSchool.Red))
                {
                    float rp = SpellEffects.SpellPower(ColorSchool.Red, hero);
                    CastWithGlow(agent, hero, ColorSchool.Red, "Crimson Torrent", () =>
                    {
                        Vec3 fwd = agent.LookDirection.NormalizedCopy();
                        foreach (Agent a in EnemiesOf(agent).ToList())
                        {
                            Vec3 to = a.Position - agent.Position;
                            if (to.Length > 15f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.6f) continue;
                            if (SpellEffects.ProtectedByMirror(a)) continue;
                            SpellEffects.DamageAgent(a, 55f * rp);
                            SpellEffects.BeginAgentGlow(a, ColorSchool.Red, 1.5f);
                        }
                    });
                    ApplyRedA1(agent); ApplyRedA2(agent);
                    return;
                }
                if (colors.Contains(ColorSchool.Blue) && CanUseBlue(agent))
                {
                    float bp = SpellEffects.SpellPower(ColorSchool.Blue, hero);
                    int roll = _rng.Next(4);
                    if (roll == 0)
                        CastWithGlow(agent, hero, ColorSchool.Blue, "Sapphire Wall", () =>
                            SpellEffects.SpawnNpcBlueWall(agent.Position, agent.LookDirection.NormalizedCopy(), agent.Team));
                    else if (roll == 1)
                        CastWithGlow(agent, hero, ColorSchool.Blue, "Cerulean Burst", () =>
                        {
                            foreach (Agent a in EnemiesOf(agent).Where(a => a.Position.Distance(agent.Position) <= 12f).ToList())
                            {
                                if (SpellEffects.ProtectedByMirror(a)) continue;
                                SpellEffects.DamageAgent(a, 28f * bp);
                                if (a.IsActive()) try { a.SetMorale(Math.Max(0f, a.GetMorale() - 35f)); } catch { }
                                SpellEffects.BeginAgentGlow(a, ColorSchool.Blue, 1.5f);
                            }
                            SpellEffects.SpawnTempLight(agent.Position, ColorSchool.Blue, 12f, 2f);
                        });
                    else
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
                            if (!SpellEffects.IsSiegeActive())
                                foreach (Formation f in formations)
                                    try { f.SetMovementOrder(MovementOrder.MovementOrderStop); } catch { }
                        });
                    return;
                }
            }

            // ── Hurt allies nearby ────────────────────────────────────────────
            if (colors.Contains(ColorSchool.Green))
            {
                bool allyHurt = AlliesOf(agent).Any(a => a.Health < a.HealthLimit * 0.6f &&
                                                    a.Position.Distance(agent.Position) <= 15f);
                if (allyHurt)
                {
                    float gp = SpellEffects.SpellPower(ColorSchool.Green, hero);
                    if (_rng.Next(5) < 2)
                        CastWithGlow(agent, hero, ColorSchool.Green, "Emerald Font", () =>
                            SpellEffects.SpawnNpcHealZone(agent.Position, ColorSchool.Green, gp, agent.Team));
                    else
                        CastWithGlow(agent, hero, ColorSchool.Green, "Verdant Surge", () =>
                        {
                            Vec3 fwd = agent.LookDirection.NormalizedCopy();
                            foreach (Agent a in AlliesOf(agent).ToList())
                            {
                                Vec3 to = a.Position - agent.Position;
                                if (to.Length > 15f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.6f) continue;
                                float h = Math.Min(22f * gp, a.HealthLimit - a.Health);
                                if (h > 0f) { a.Health += h; SpellEffects.BeginAgentGlow(a, ColorSchool.Green, 1.5f); }
                            }
                        });
                    return;
                }
            }

            // ── Yellow — morale suppression (no horseback) ────────────────────
            if (colors.Contains(ColorSchool.Yellow) && CanUseYellow(agent))
            {
                float yp = SpellEffects.SpellPower(ColorSchool.Yellow, hero);
                if (_rng.Next(5) < 2)
                    CastWithGlow(agent, hero, ColorSchool.Yellow, "Creeping Dread", () =>
                        SpellEffects.SpawnNpcYellowCloud(agent.Position, yp, agent.Team));
                else
                    CastWithGlow(agent, hero, ColorSchool.Yellow, "Tide of Dread", () =>
                    {
                        Vec3 fwd = agent.LookDirection.NormalizedCopy();
                        foreach (Agent a in EnemiesOf(agent).ToList())
                        {
                            Vec3 to = a.Position - agent.Position;
                            if (to.Length > 15f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.6f) continue;
                            try { a.SetMorale(Math.Max(0f, a.GetMorale() - 30f * yp)); } catch { }
                            SpellEffects.BeginAgentGlow(a, ColorSchool.Yellow, 1.5f);
                        }
                    });
                return;
            }

            // ── Orange — inspire / punish (party morale ≥ 45) ────────────────
            if (colors.Contains(ColorSchool.Orange) && CanUseOrange(hero))
            {
                float op = SpellEffects.SpellPower(ColorSchool.Orange, hero);
                if (_rng.Next(5) < 2)
                    CastWithGlow(agent, hero, ColorSchool.Orange, "Golden Tide", () =>
                    {
                        Vec3 fwd = agent.LookDirection.NormalizedCopy();
                        var formations = new System.Collections.Generic.HashSet<Formation>();
                        foreach (Agent a in EnemiesOf(agent).ToList())
                        {
                            Vec3 to = a.Position - agent.Position;
                            if (to.Length > 15f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.6f) continue;
                            SpellEffects.DamageAgent(a, 25f * op);
                            if (!a.IsActive()) continue;
                            try { a.SetMorale(100f); } catch { }
                            SpellEffects.BeginAgentGlow(a, ColorSchool.Orange, 1.5f);
                            if (a.Formation != null) formations.Add(a.Formation);
                        }
                        if (!SpellEffects.IsSiegeActive())
                            foreach (Formation f in formations)
                                try { f.SetMovementOrder(MovementOrder.MovementOrderCharge); } catch { }
                    });
                else
                    CastWithGlow(agent, hero, ColorSchool.Orange, "Gilded Words", () =>
                    {
                        foreach (Agent a in AlliesOf(agent).Where(a => a.Position.Distance(agent.Position) <= 20f).ToList())
                        {
                            try { a.SetMorale(Math.Min(a.GetMorale() + 20f * op, 100f)); } catch { }
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
                    float rp = SpellEffects.SpellPower(ColorSchool.Red, hero);
                    if (_rng.Next(2) == 0)
                        CastWithGlow(agent, hero, ColorSchool.Red, "Crimson Torrent", () =>
                        {
                            Vec3 fwd = agent.LookDirection.NormalizedCopy();
                            foreach (Agent a in EnemiesOf(agent).ToList())
                            {
                                Vec3 to = a.Position - agent.Position;
                                if (to.Length > 15f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.6f) continue;
                                if (SpellEffects.ProtectedByMirror(a)) continue;
                                SpellEffects.DamageAgent(a, 55f * rp);
                                SpellEffects.BeginAgentGlow(a, ColorSchool.Red, 1.5f);
                            }
                        });
                    else
                        CastWithGlow(agent, hero, ColorSchool.Red, "Cinder Burst", () =>
                        {
                            foreach (Agent a in EnemiesOf(agent).Where(a => a.Position.Distance(agent.Position) <= 10f).ToList())
                            {
                                if (SpellEffects.ProtectedByMirror(a)) continue;
                                SpellEffects.DamageAgent(a, 70f * rp);
                                SpellEffects.BeginAgentGlow(a, ColorSchool.Red, 1.5f);
                            }
                            SpellEffects.SpawnTempLight(agent.Position, ColorSchool.Red, 10f, 1.5f);
                        });
                    ApplyRedA1(agent); ApplyRedA2(agent);
                    break;
                }
                case ColorSchool.Orange when CanUseOrange(hero):
                {
                    float op = SpellEffects.SpellPower(ColorSchool.Orange, hero);
                    if (_rng.Next(2) == 0)
                        CastWithGlow(agent, hero, ColorSchool.Orange, "Golden Tide", () =>
                        {
                            Vec3 fwd = agent.LookDirection.NormalizedCopy();
                            var formations = new System.Collections.Generic.HashSet<Formation>();
                            foreach (Agent a in EnemiesOf(agent).ToList())
                            {
                                Vec3 to = a.Position - agent.Position;
                                if (to.Length > 15f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.6f) continue;
                                SpellEffects.DamageAgent(a, 25f * op);
                                if (!a.IsActive()) continue;
                                try { a.SetMorale(100f); } catch { }
                                SpellEffects.BeginAgentGlow(a, ColorSchool.Orange, 1.5f);
                                if (a.Formation != null) formations.Add(a.Formation);
                            }
                            if (!SpellEffects.IsSiegeActive())
                                foreach (Formation f in formations)
                                    try { f.SetMovementOrder(MovementOrder.MovementOrderCharge); } catch { }
                        });
                    else
                        CastWithGlow(agent, hero, ColorSchool.Orange, "Gilded Words", () =>
                        {
                            foreach (Agent a in AlliesOf(agent).Where(a => a.Position.Distance(agent.Position) <= 20f).ToList())
                            {
                                try { a.SetMorale(Math.Min(a.GetMorale() + 20f * op, 100f)); } catch { }
                                SpellEffects.BeginAgentGlow(a, ColorSchool.Orange, 1.5f);
                            }
                        });
                    break;
                }
                case ColorSchool.Green:
                {
                    float gp = SpellEffects.SpellPower(ColorSchool.Green, hero);
                    if (_rng.Next(2) == 0)
                        CastWithGlow(agent, hero, ColorSchool.Green, "Emerald Font", () =>
                            SpellEffects.SpawnNpcHealZone(agent.Position, ColorSchool.Green, gp, agent.Team));
                    else
                        CastWithGlow(agent, hero, ColorSchool.Green, "Verdant Surge", () =>
                        {
                            Vec3 fwd = agent.LookDirection.NormalizedCopy();
                            foreach (Agent a in AlliesOf(agent).ToList())
                            {
                                Vec3 to = a.Position - agent.Position;
                                if (to.Length > 15f || Vec3.DotProduct(fwd, to.NormalizedCopy()) < 0.6f) continue;
                                float h = Math.Min(22f * gp, a.HealthLimit - a.Health);
                                if (h > 0f) { a.Health += h; SpellEffects.BeginAgentGlow(a, ColorSchool.Green, 1.5f); }
                            }
                        });
                    break;
                }
                case ColorSchool.Blue when CanUseBlue(agent):
                {
                    float bp = SpellEffects.SpellPower(ColorSchool.Blue, hero);
                    int roll = _rng.Next(3);
                    if (roll == 0)
                        CastWithGlow(agent, hero, ColorSchool.Blue, "Sapphire Wall", () =>
                            SpellEffects.SpawnNpcBlueWall(agent.Position, agent.LookDirection.NormalizedCopy(), agent.Team));
                    else if (roll == 1)
                        CastWithGlow(agent, hero, ColorSchool.Blue, "Cerulean Burst", () =>
                        {
                            foreach (Agent a in EnemiesOf(agent).Where(a => a.Position.Distance(agent.Position) <= 12f).ToList())
                            {
                                if (SpellEffects.ProtectedByMirror(a)) continue;
                                SpellEffects.DamageAgent(a, 28f * bp);
                                if (a.IsActive()) try { a.SetMorale(Math.Max(0f, a.GetMorale() - 35f)); } catch { }
                                SpellEffects.BeginAgentGlow(a, ColorSchool.Blue, 1.5f);
                            }
                            SpellEffects.SpawnTempLight(agent.Position, ColorSchool.Blue, 12f, 2f);
                        });
                    else
                        CastWithGlow(agent, hero, ColorSchool.Blue, "Azure Arrest", () =>
                        {
                            foreach (Agent a in EnemiesOf(agent).Where(a => a.Position.Distance(agent.Position) <= 30f).ToList())
                                try { a.SetMorale(0f); SpellEffects.BeginAgentGlow(a, ColorSchool.Blue, 1.5f); } catch { }
                        });
                    break;
                }
                case ColorSchool.Yellow when CanUseYellow(agent):
                {
                    float yp = SpellEffects.SpellPower(ColorSchool.Yellow, hero);
                    if (_rng.Next(2) == 0)
                        CastWithGlow(agent, hero, ColorSchool.Yellow, "Creeping Dread", () =>
                            SpellEffects.SpawnNpcYellowCloud(agent.Position, yp, agent.Team));
                    else
                        CastWithGlow(agent, hero, ColorSchool.Yellow, "Tide of Dread", () =>
                        {
                            foreach (Agent a in EnemiesOf(agent).Where(a => a.Position.Distance(agent.Position) <= 17f).ToList())
                                try { a.SetMorale(Math.Max(0f, a.GetMorale() - 30f * yp)); SpellEffects.BeginAgentGlow(a, ColorSchool.Yellow, 1.5f); } catch { }
                        });
                    break;
                }
                case ColorSchool.Purple:
                {
                    float pp = SpellEffects.SpellPower(ColorSchool.Purple, hero);
                    int roll = _rng.Next(3);
                    if (roll == 0)
                        CastWithGlow(agent, hero, ColorSchool.Purple, "Grey Harvest", () =>
                        {
                            Vec3 fwd = agent.LookDirection.NormalizedCopy();
                            var inCone = EnemiesOf(agent).Where(a =>
                            {
                                if (!a.IsActive() || a.IsHero) return false;
                                Vec3 to = a.Position - agent.Position;
                                return to.Length <= 15f && Vec3.DotProduct(fwd, to.NormalizedCopy()) >= 0.6f;
                            }).ToList();
                            if (inCone.Count > 0) { var t = inCone[_rng.Next(inCone.Count)]; SpellEffects.BeginAgentGlow(t, ColorSchool.Purple, 1.5f); SpellEffects.QueueKill(t); }
                        });
                    else if (roll == 1)
                        CastWithGlow(agent, hero, ColorSchool.Purple, "Grey Reaping", () =>
                        {
                            foreach (Agent a in EnemiesOf(agent).Where(a => a.Position.Distance(agent.Position) <= 15f).ToList())
                                try { a.SetMorale(0f); SpellEffects.BeginAgentGlow(a, ColorSchool.Purple, 1.5f); } catch { }
                            var kc = EnemiesOf(agent).Where(a => !a.IsHero && a.IsActive() && a.Position.Distance(agent.Position) <= 15f).ToList();
                            if (kc.Count > 0) { var t = kc[_rng.Next(kc.Count)]; SpellEffects.BeginAgentGlow(t, ColorSchool.Purple, 2f); SpellEffects.QueueKill(t); }
                        });
                    else
                        CastWithGlow(agent, hero, ColorSchool.Purple, "Grey Tide", () =>
                        {
                            foreach (Agent a in EnemiesOf(agent).Where(a => a.Position.Distance(agent.Position) <= 8f).ToList())
                            {
                                if (SpellEffects.ProtectedByMirror(a)) continue;
                                SpellEffects.DamageAgent(a, 60f * pp);
                                SpellEffects.BeginAgentGlow(a, ColorSchool.Purple, 1.5f);
                            }
                        });
                    break;
                }
            }
        }

        // ── Blight casting ────────────────────────────────────────────────────
        // No NPC limitations, no light-level check, cast interval 2s.
        // Returns false when no targets were affected — cooldown still applied but no message.
        private static void CastBlightSpell(Agent agent, Hero hero)
        {
            try { CastBlightSpellInner(agent, hero); } catch { }
        }

        private static void CastBlightSpellInner(Agent agent, Hero hero)
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
                        SpellEffects.DamageAgent(a, 55f * power, school);
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
                    CastBlightWithGlow(agent, hero, school, "Gilded Words", hit);
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
                        float h = Math.Min(22f * power, a.HealthLimit - a.Health);
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
                        SpellEffects.DamageAgent(a, 18f * power, school);
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
                        SpellEffects.DamageAgent(a, 60f * power, school);
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
            SpellEffects.SpawnTempLight(agent.Position, school, 6f, 1.5f);
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
            if (SpellEffects.IsSiegeActive()) return;
            foreach (Formation f in agent.Team.FormationsIncludingSpecialAndEmpty)
                try { if (f.CountOfUnits > 0) f.SetMovementOrder(MovementOrder.MovementOrderCharge); } catch { }
        }

        private static void ApplyRedA2(Agent agent)
        {
            if (agent == null) return;
            try { agent.Health = Math.Max(1f, agent.Health - 2f); } catch { }
        }

        // ── Battle magical events ─────────────────────────────────────────────
        // Roll once per battle when the warmup completes (~8% base chance).
        // Season and the cultures present on the field bias school selection.
        // Spawns a dim overhead ambient light and applies one immediate school effect.

        private static readonly string[][] _battleEventFlavour = {
            new[] { // Red
                "The sky bleeds red over Calradia. Every blade bites deeper.",
                "A Battanian elder would call this the Butcher's Hour. The field agrees.",
                "Crimson light floods the air -- the colour of conquest. All are marked by it.",
                "The red boils up from old Calradic battlegrounds. Both sides feel it.",
            },
            new[] { // Orange
                "Gold fills the horizon. In Vlandia, they call this the Warlord's Blessing.",
                "A warmth like Aserai noon presses through the ranks. Resolve stiffens on all sides.",
                "Gilded air -- the kind the old Empire promised in its oaths. It holds.",
                "The orange rises. Somewhere a banner catches the light and refuses to fall.",
            },
            new[] { // Yellow
                "Something wrong hangs in the air. Sturgian shamans know this dread.",
                "Yellow mist -- the colour Battanian clans feared in the old forest hollows.",
                "Creeping unease. Even veterans of the Empire find their hands unsteady.",
                "Sickness in the light. The nerve of every soldier here begins to fray.",
            },
            new[] { // Green
                "The old Battanian faith stirs. The earth drinks from the wounds of the field.",
                "Life surges from Calradia's soil. Even Imperial physicians pause in awe.",
                "Green light floods the air. The Khuzaits call this the Grass Mother's touch.",
                "The earth breathes. Every wound on this field begins to close.",
            },
            new[] { // Blue
                "Stillness descends -- the Scholar's Veil, as the old Calradic texts described it.",
                "Blue cold drops from nowhere -- the air of Sturgia's northern peaks, here and sudden.",
                "The Scholar's hand, unlooked-for. Barriers of force rise across the field.",
                "The veil drops. Movement grows laboured on every side.",
            },
            new[] { // Purple
                "The grey comes for someone. It has, since before the Calradic Empire's founding.",
                "Purple dusk at midday. One soul on this field will not see the sunset.",
                "A Khuzait fortune-reader would leave the field now. One flame goes out.",
                "The grey shroud falls over Calradia. Someone is taken -- quietly, as always.",
            },
        };

        private static readonly string[] _npcCastSuffix = {
            "The red has come to Calradia.",                         // Red
            "The warmth of the old roads, remembered.",             // Orange
            "Something the Battanian shamans would recognise.",     // Yellow
            "The old faith stirs in Calradia's soil.",              // Green
            "The Scholar's craft, as the old Empire described it.", // Blue
            "The grey takes no sides in Calradia's wars.",          // Purple
        };

        private static void TryTriggerBattleEvent()
        {
            if (Mission.Current == null) return;
            if (_rng.Next(100) >= 8) return;

            CampaignTime.Seasons season = CampaignTime.Seasons.Spring;
            try { season = CampaignTime.Now.GetSeasonOfYear; } catch { }

            // Build weighted school selection (base 1 each, season bonus +2 for primary, +1 for secondary)
            int[] weights = { 1, 1, 1, 1, 1, 1 }; // index = (int)ColorSchool
            switch (season)
            {
                case CampaignTime.Seasons.Spring: weights[(int)ColorSchool.Green]  += 2; break;
                case CampaignTime.Seasons.Summer: weights[(int)ColorSchool.Red]    += 2; weights[(int)ColorSchool.Yellow] += 1; break;
                case CampaignTime.Seasons.Autumn: weights[(int)ColorSchool.Orange] += 2; weights[(int)ColorSchool.Red]   += 1; break;
                case CampaignTime.Seasons.Winter: weights[(int)ColorSchool.Blue]   += 2; weights[(int)ColorSchool.Purple]+= 1; break;
            }
            int total = 0; foreach (int w in weights) total += w;
            int roll = _rng.Next(total), cum = 0;
            ColorSchool school = ColorSchool.Red;
            for (int i = 0; i < weights.Length; i++)
            {
                cum += weights[i];
                if (roll < cum) { school = (ColorSchool)i; break; }
            }

            string eventName;
            switch (school)
            {
                case ColorSchool.Red:    eventName = "Crimson Sky";    break;
                case ColorSchool.Orange: eventName = "Gilded Hour";    break;
                case ColorSchool.Yellow: eventName = "Sickly Haze";   break;
                case ColorSchool.Green:  eventName = "Living Surge";   break;
                case ColorSchool.Blue:   eventName = "Scholar's Veil"; break;
                case ColorSchool.Purple: eventName = "Grey Shroud";    break;
                default:                 eventName = "Colour Surge";   break;
            }
            int schoolIdx = (int)school < _battleEventFlavour.Length ? (int)school : 0;
            string eventDesc = _battleEventFlavour[schoolIdx][_rng.Next(_battleEventFlavour[schoolIdx].Length)];

            InformationManager.DisplayMessage(new InformationMessage(
                $"-- {eventName} --",
                ColorSchoolData.GetMessageColor(school)));
            InformationManager.DisplayMessage(new InformationMessage(
                eventDesc,
                ColorSchoolData.GetMessageColor(school)));

            // Spawn dim ambient overhead light
            try
            {
                var scene = Mission.Current?.Scene;
                if (scene != null)
                {
                    Agent anchor = Agent.Main ?? Mission.Current.Agents.FirstOrDefault(a => a.IsActive());
                    Vec3 highPos = (anchor != null ? anchor.Position : new Vec3(0f, 0f, 0f)) + new Vec3(0f, 0f, 35f);
                    var entity = GameEntity.CreateEmpty(scene, false, false, false);
                    var frame  = new MatrixFrame(Mat3.Identity, highPos);
                    entity.SetGlobalFrame(in frame, true);
                    var light = Light.CreatePointLight(80f);
                    light.Radius        = 80f;
                    light.Intensity     = 200f;
                    light.LightColor    = SchoolToEventColor(school);
                    light.ShadowEnabled = false;
                    entity.AddLight(light);
                    _battleEventLights.Add(entity);
                }
            }
            catch { }

            // Immediate school effect — affects all sides equally
            var agents = Mission.Current.Agents.ToList();
            switch (school)
            {
                case ColorSchool.Red:
                    foreach (Agent a in agents.Where(a => a.IsActive() && !a.IsMount))
                    {
                        if (SpellEffects.ProtectedByMirror(a)) continue;
                        try { SpellEffects.DamageAgent(a, 8f); } catch { }
                        SpellEffects.BeginAgentGlow(a, school, 2f);
                    }
                    break;
                case ColorSchool.Orange:
                    foreach (Agent a in agents.Where(a => a.IsActive() && !a.IsMount))
                    {
                        try { a.SetMorale(Math.Min(a.GetMorale() + 15f, 100f)); } catch { }
                        SpellEffects.BeginAgentGlow(a, school, 2f);
                    }
                    break;
                case ColorSchool.Yellow:
                    foreach (Agent a in agents.Where(a => a.IsActive() && !a.IsMount))
                    {
                        try { a.SetMorale(Math.Max(0f, a.GetMorale() - 10f)); } catch { }
                    }
                    break;
                case ColorSchool.Green:
                    foreach (Agent a in agents.Where(a => a.IsActive() && !a.IsMount))
                    {
                        float h = Math.Min(12f, a.HealthLimit - a.Health);
                        if (h > 0f) { try { a.Health += h; } catch { } }
                        SpellEffects.BeginAgentGlow(a, school, 2f);
                    }
                    break;
                case ColorSchool.Blue:
                {
                    Vec3 centre = Agent.Main != null ? Agent.Main.Position
                                : (agents.FirstOrDefault(a => a.IsActive()) ?? agents.FirstOrDefault())?.Position
                                  ?? new Vec3(0f, 0f, 0f);
                    try { SpellEffects.SpawnBattleEventBlueWalls(centre); } catch { }
                    break;
                }
                case ColorSchool.Purple:
                    var purpleTargets = agents.Where(a => a.IsActive() && !a.IsMount && !a.IsHero).ToList();
                    if (purpleTargets.Count > 0)
                    {
                        Agent victim = purpleTargets[_rng.Next(purpleTargets.Count)];
                        SpellEffects.BeginAgentGlow(victim, school, 1f);
                        SpellEffects.QueueKill(victim);
                    }
                    break;
            }
        }

        private static Vec3 SchoolToEventColor(ColorSchool school)
        {
            switch (school)
            {
                case ColorSchool.Red:    return new Vec3(1f,    0.1f, 0.05f);
                case ColorSchool.Orange: return new Vec3(1f,    0.5f, 0.05f);
                case ColorSchool.Yellow: return new Vec3(1f,    0.9f, 0.1f);
                case ColorSchool.Green:  return new Vec3(0.1f,  0.7f, 0.1f);
                case ColorSchool.Blue:   return new Vec3(0.1f,  0.3f, 1f);
                case ColorSchool.Purple: return new Vec3(0.55f, 0.05f, 0.8f);
                default:                 return new Vec3(1f,    1f,    1f);
            }
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
                if (to.Length < 0.01f) return false;
                return to.Length < radius && Vec3.DotProduct(fwd, to.NormalizedCopy()) > dot;
            });
        }

        private static List<Agent> EnemiesOf(Agent agent)
        {
            if (Mission.Current == null || agent?.Team == null) return new List<Agent>();
            try
            {
                return Mission.Current.Agents
                    .Where(a => a != agent && !a.IsMount && a.IsActive() && a.Team != null && a.Team != agent.Team)
                    .ToList();
            }
            catch { return new List<Agent>(); }
        }

        private static List<Agent> AlliesOf(Agent agent)
        {
            if (Mission.Current == null || agent?.Team == null) return new List<Agent>();
            try
            {
                return Mission.Current.Agents
                    .Where(a => a != agent && !a.IsMount && a.IsActive() && a.Team != null && a.Team == agent.Team)
                    .ToList();
            }
            catch { return new List<Agent>(); }
        }

        private static void CastWithGlow(Agent agent, Hero hero, ColorSchool school,
                                          string spellName, Action effect)
        {
            try { effect?.Invoke(); } catch { }
            SetCooldown(hero);
            SpellEffects.BeginAgentGlow(agent, school, 3.0f);
            SpellEffects.SpawnTempLight(agent.Position, school, 6f, 1.5f);
            SpellEffects.TryCastSound(agent.Position, school);
            SpellEffects.TryCastAnimation(agent);

            string npcSuffix = (int)school < _npcCastSuffix.Length ? " " + _npcCastSuffix[(int)school] : "";
            InformationManager.DisplayMessage(new InformationMessage(
                $"✦ {agent.Name} channels {spellName}.{npcSuffix} ✦",
                ColorSchoolData.GetMessageColor(school)));

            // Oversaturation risk (non-Blight, non-Prism lords only).
            // 4% lethal: health → 1, near-certain death against any standing enemy.
            // 5% knockdown: 3-second stagger.
            // 9% total — rate-preserving reduction from 11% at 50s cooldown to 40s.
            if (!BlightSystem.IsBlight(hero) && !ColourLordRegistry.IsPrismLord(hero))
            {
                int overRoll = _rng.Next(100);
                if (overRoll < 4)
                {
                    try
                    {
                        if (agent.IsActive())
                        {
                            agent.Health = 1f;
                            InformationManager.DisplayMessage(new InformationMessage(
                                $"{agent.Name} is overwhelmed by the casting — fatally exposed.",
                                ColorSchoolData.GetMessageColor(school)));
                        }
                    }
                    catch { }
                }
                else if (overRoll < 9)
                {
                    try
                    {
                        if (agent.IsActive())
                            SaturationSystem.ApplyKnockdown(agent, 3f);
                    }
                    catch { }
                }
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
