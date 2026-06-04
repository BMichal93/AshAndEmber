// =============================================================================
// LIFE & DEATH MAGIC — SpellEffects.cs
// Core partial class: helpers, per-form execution entry points,
// enchantment application, stoneskin state, and deferred-death queue.
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

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("AshAndEmber.Tests")]

namespace AshAndEmber
{
    public static partial class SpellEffects
    {
        private static readonly Random _rng = new Random();

        // ── Light-level helpers (kept for legacy compatibility) ────────────────
        internal enum LightLevel { Bright, Dim, Dark }

        internal static LightLevel GetCampaignLightLevel()
        {
            if (Campaign.Current == null) return LightLevel.Bright;
            if (CampaignMapEvents.IsLongNight()) return LightLevel.Dark;
            try
            {
                float hour = (float)(CampaignTime.Now.ToHours % 24.0);
                if (hour < 5f || hour >= 22f) return LightLevel.Dark;
                if (hour < 7f || hour >= 20f) return LightLevel.Dim;
                return LightLevel.Bright;
            }
            catch { return LightLevel.Bright; }
        }

        internal static LightLevel GetLightLevel() => LightLevel.Bright;
        internal static bool RollDimFizzle() => false;
        internal static bool HasDarkAffinity(ColorSchool s) => false;
        internal static LightLevel GetEffectiveLightLevel(ColorSchool s) => LightLevel.Bright;
        public static bool IsDaytime() => true;

        // ── Spell power — flat 1.0 ────────────────────────────────────────────
        internal static float SpellPower(ColorSchool school, Hero hero = null) => 1f;

        // ── Siege / battle checks ─────────────────────────────────────────────
        public static bool IsSiegeActive()
        {
            if (Mission.Current == null) return false;
            try
            {
                foreach (Agent a in Mission.Current.Agents)
                {
                    if (!a.IsActive()) continue;
                    try { if (a.IsUsingGameObject) return true; } catch { }
                }
            }
            catch { }
            return false;
        }

        public static bool IsBattleMission()
        {
            try
            {
                if (Mission.Current == null || Mission.Current.PlayerTeam == null) return false;
                Team pt = Mission.Current.PlayerTeam;
                foreach (Agent a in Mission.Current.Agents)
                {
                    if (!a.IsActive() || a.IsMount || a.Team == null || a.Team == pt) continue;
                    bool isEnemy = false;
                    try { isEnemy = pt.IsEnemyOf(a.Team); } catch { continue; }
                    if (isEnemy) return true;
                }
            }
            catch { }
            return false;
        }

        public static bool ProtectedByMirror(Agent a) => false;

        // Returns true when the main hand is empty (nothing wielded).
        // A shield alone in the off-hand is not blocking — the casting hand is free.
        public static bool HasFreeHand(Agent agent)
        {
            try
            {
                return agent.GetWieldedItemIndex(Agent.HandIndex.MainHand) == EquipmentIndex.None;
            }
            catch { return true; }
        }

        // Sheathes the blocking item so the agent has a free hand for spellcasting.
        // The NPC cast windup (0.7 s) gives the sheath animation time to complete.
        // Returns true immediately — the hand will be free by the time the spell fires.
        public static void TryFreeHandForCast(Agent agent)
        {
            try
            {
                if (HasFreeHand(agent)) return;

                EquipmentIndex mainIdx = agent.GetWieldedItemIndex(Agent.HandIndex.MainHand);
                EquipmentIndex offIdx  = agent.GetWieldedItemIndex(Agent.HandIndex.OffHand);

                if (offIdx != EquipmentIndex.None)
                {
                    // Sheathe the off-hand item (typically a shield)
                    agent.TryToSheathWeaponInHand(Agent.HandIndex.OffHand, Agent.WeaponWieldActionType.WithAnimation);
                }
                else if (mainIdx != EquipmentIndex.None)
                {
                    // Must be a two-handed weapon — sheathe main hand
                    agent.TryToSheathWeaponInHand(Agent.HandIndex.MainHand, Agent.WeaponWieldActionType.WithAnimation);
                }
            }
            catch { }
        }

        private static readonly Dictionary<string, string> _toggleComboToId
            = new Dictionary<string, string>();

        public static bool IsToggleDismiss(string combo) =>
            _toggleComboToId.TryGetValue(combo ?? "", out string id) && HasAreaEffect(id);

        public static void TickColourCooldown(float dt) { }
        public static void ClearColourCooldown() { }

        public static bool Execute(string combo) => false;

        // ── NPC spell execution ───────────────────────────────────────────────
        public static void ExecuteNpcBlast(Agent caster, int formCount,
            int damageCount, int restoreCount, Team casterTeam)
        {
            ResetImmolateKill();
            var cast = new SpellCast
            {
                Form = SpellForm.Blast, FormCount = formCount, BlastCount = formCount,
                DamageCount = damageCount, RestoreCount = restoreCount,
                OverrideVisualColor = ResolveNpcSchool(caster)
            };
            ExecuteBlastFromAgent(caster, cast, casterTeam);
        }

        public static void ExecuteNpcBurst(Agent caster, int formCount,
            int damageCount, int restoreCount, Team casterTeam)
        {
            ResetImmolateKill();
            var cast = new SpellCast
            {
                Form = SpellForm.Burst, FormCount = formCount, BurstCount = formCount,
                DamageCount = damageCount, RestoreCount = restoreCount,
                OverrideVisualColor = ResolveNpcSchool(caster)
            };
            ExecuteBurstFromAgent(caster, cast, casterTeam);
        }

        // Returns ColorSchool.Ashen for Ashen lords so their spells show cold-blue
        // visuals; null for everyone else so the default school logic applies.
        private static ColorSchool? ResolveNpcSchool(Agent caster)
        {
            try
            {
                var h = (caster?.Character as TaleWorlds.CampaignSystem.CharacterObject)?.HeroObject;
                if (h != null && ColourLordRegistry.IsAshenLord(h)) return ColorSchool.Ashen;
            }
            catch { }
            return null;
        }

        // ── Self-effects clear ─────────────────────────────────────────────────
        public static void ClearSelfEffects()
        {
            RemoveAreaEffect("spell_barrier");
            _haltedAgents.Clear();
            ClearMissile();
        }

        // ── Halted-agent tick (legacy freeze mechanic, dict stays empty with new system) ─
        public static void TickHaltedAgents(float dt)
        {
            if (_haltedAgents.Count == 0 || Mission.Current == null) return;
            _haltTeleportTimer -= dt;
            bool doTeleport = _haltTeleportTimer <= 0f;
            if (doTeleport) _haltTeleportTimer = HaltTeleportInterval;

            _haltAgentMap.Clear();
            try
            {
                foreach (Agent a in Mission.Current.Agents)
                    if (a.IsActive() && a.Health > 0f) _haltAgentMap[a.Index] = a;
            }
            catch { }

            _haltKeySnap.Clear();
            _haltKeySnap.AddRange(_haltedAgents.Keys);
            _expiredHaltKeys.Clear();
            foreach (int idx in _haltKeySnap)
            {
                var (remaining, frozenPos, srcAgent) = _haltedAgents[idx];
                remaining -= dt;
                if (!_haltAgentMap.TryGetValue(idx, out Agent a))
                { _expiredHaltKeys.Add(idx); continue; }
                if (a != srcAgent) { _expiredHaltKeys.Add(idx); continue; }
                bool usingEquip = false;
                try { usingEquip = a.IsUsingGameObject; } catch { }
                if (remaining <= 0f || usingEquip)
                {
                    _expiredHaltKeys.Add(idx);
                    if (!usingEquip) try { a.SetMaximumSpeedLimit(10f, false); } catch { }
                }
                else
                {
                    _haltedAgents[idx] = (remaining, frozenPos, srcAgent);
                    if (a.MountAgent == null) try { a.SetMaximumSpeedLimit(0f, false); } catch { }
                    if (doTeleport && a.MountAgent == null) try { a.TeleportToPosition(frozenPos); } catch { }
                }
            }
            foreach (int idx in _expiredHaltKeys) _haltedAgents.Remove(idx);
        }

        public static void TickRandomUnitMagic(float dt) { }

        // ── Agent helpers ──────────────────────────────────────────────────────
        private static Agent Player => Agent.Main;

        private static List<Agent> Enemies()
        {
            if (Mission.Current == null || Player == null) return new List<Agent>();
            try
            {
                return Mission.Current.Agents
                    .Where(a => a != Player && !a.IsMount && a.IsActive() &&
                                a.Team != null && a.Team != Player.Team)
                    .ToList();
            }
            catch { return new List<Agent>(); }
        }

        private static List<Agent> Allies()
        {
            if (Mission.Current == null || Player == null) return new List<Agent>();
            try
            {
                return Mission.Current.Agents
                    .Where(a => a != Player && !a.IsMount && a.IsActive() &&
                                a.Team != null && a.Team == Player.Team)
                    .ToList();
            }
            catch { return new List<Agent>(); }
        }

        internal static List<Agent> EnemiesOf(Agent source)
        {
            if (Mission.Current == null || source?.Team == null) return new List<Agent>();
            var result = new List<Agent>();
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (a == source || a.IsMount || !a.IsActive() || a.Team == null) continue;
                    bool isEnemy = false;
                    try { isEnemy = source.Team.IsEnemyOf(a.Team); } catch { continue; }
                    if (isEnemy) result.Add(a);
                }
            }
            catch { }
            return result;
        }

        internal static List<Agent> AlliesOf(Agent source)
        {
            if (Mission.Current == null || source?.Team == null) return new List<Agent>();
            try
            {
                return Mission.Current.Agents
                    .Where(a => a != source && !a.IsMount && a.IsActive() && a.Team == source.Team)
                    .ToList();
            }
            catch { return new List<Agent>(); }
        }

        // ── Deferred death queue ───────────────────────────────────────────────
        private static readonly List<Agent> _pendingDeaths = new List<Agent>();

        public static void QueueKill(Agent target)
        {
            if (target == null || target.IsHero) return;
            bool usingEquip = false;
            try { usingEquip = target.IsUsingGameObject; } catch { }
            if (usingEquip) { try { target.Health = 1f; } catch { } return; }
            if (target.IsActive() && !_pendingDeaths.Contains(target))
                _pendingDeaths.Add(target);
        }

        public static void FlushPendingDeaths()
        {
            if (_pendingDeaths.Count == 0) return;
            var mission = Mission.Current;
            if (mission == null || mission.CurrentState != Mission.State.Continuing)
            { _pendingDeaths.Clear(); return; }
            if (Agent.Main == null || !Agent.Main.IsActive())
            { _pendingDeaths.Clear(); return; }
            var snapshot = _pendingDeaths.ToList();
            _pendingDeaths.Clear();
            foreach (Agent a in snapshot)
            {
                if (mission.CurrentState != Mission.State.Continuing) return;
                if (Agent.Main == null || !Agent.Main.IsActive()) return;
                if (a?.IsActive() == true) KillAgent(a);
            }
        }

        public static void ClearPendingDeaths() => _pendingDeaths.Clear();

        public static void KillAgent(Agent target)
        {
            if (target == null || !target.IsActive()) return;
            if (target.IsHero)
            { try { target.Health = Math.Max(1f, target.Health - 2f); } catch { } return; }
            bool usingEquip = false;
            try { usingEquip = target.IsUsingGameObject; } catch { }
            if (usingEquip) { try { target.Health = 1f; } catch { } return; }
            try
            {
                Blow blow = BuildBlow(target, DamageTypes.Cut, 2000f);
                target.Die(blow, (Agent.KillInfo)0);
                return;
            }
            catch { }
            if (!target.IsActive()) return;
            try { target.MakeDead(true, ActionIndexCache.Create("act_strike_walk_right_stance"), 0); } catch { }
        }

        public static void DamageAgent(Agent target, float damage, ColorSchool? school = null)
        {
            if (target == null || !target.IsActive()) return;

            // Cinder Shell enchantment: reduce incoming damage
            if (_stoneskinAgents.TryGetValue(target, out var skin) && skin.Remaining > 0f)
            {
                float reduction = Math.Min(0.5f, skin.BonusArmor / 200f);
                damage *= (1f - reduction);
            }

            // Sunder enchantment: increase incoming damage (armour shred, max +40%)
            if (_sunderedAgents.TryGetValue(target, out var sunder) && sunder.Remaining > 0f)
            {
                float amplification = Math.Min(0.40f, sunder.BonusVuln / 200f);
                damage *= (1f + amplification);
            }

            float newHealth = target.Health - damage;
            if (newHealth <= 0f)
            {
                if (!target.IsHero) QueueKill(target);
                else try { target.Health = 1f; } catch { }
            }
            else try { target.Health = newHealth; } catch { }
        }

        public static void HealAgent(Agent target, float amount)
        {
            if (target == null || !target.IsActive()) return;
            try { target.Health = Math.Min(target.HealthLimit, target.Health + amount); } catch { }
        }

        private static Blow BuildBlow(Agent target, DamageTypes type, float magnitude)
        {
            Blow blow = new Blow();
            blow.OwnerId          = Agent.Main?.Index ?? 0;
            blow.DamageType       = type;
            blow.BaseMagnitude    = magnitude;
            blow.InflictedDamage  = (int)magnitude;
            blow.GlobalPosition   = target.Position;
            blow.Direction        = new Vec3(0f, 0f, 1f);
            blow.WeaponRecord     = new BlowWeaponRecord();
            blow.DamageCalculated = true;
            blow.NoIgnore         = true;
            blow.StrikeType       = StrikeType.Invalid;
            blow.VictimBodyPart   = BoneBodyPartType.Chest;
            blow.AttackType       = AgentAttackType.Standard;
            blow.BlowFlag         = BlowFlags.NoSound;
            return blow;
        }

        // ── Cone geometry ─────────────────────────────────────────────────────
        internal static List<Agent> ConeAgents(Vec3 origin, Vec3 fwd, float range, float dot)
        {
            if (Mission.Current == null) return new List<Agent>();
            var result = new List<Agent>();
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
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

        internal static List<Agent> ConeAgentsFrom(Agent source, float range, float dot)
        {
            if (Mission.Current == null || source == null) return new List<Agent>();
            Vec3 fwd = source.LookDirection.NormalizedCopy();
            var result = new List<Agent>();
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount || a == source) continue;
                    if (source.Team != null && a.Team == source.Team) continue;
                    Vec3 to = a.Position - source.Position;
                    if (to.Length > range) continue;
                    if (Vec3.DotProduct(fwd, to.NormalizedCopy()) < dot) continue;
                    result.Add(a);
                }
            }
            catch { }
            return result;
        }

        internal static int CountEnemiesInCone(Agent source, float range, float dot)
            => ConeAgentsFrom(source, range, dot).Count;

        // ── Apply effect to one agent ─────────────────────────────────────────
        internal static void ApplyEffectsToAgent(Agent target, SpellCast cast, Agent caster)
        {
            if (target == null || !target.IsActive()) return;
            if (IsWarded(target)) return;

            bool isEnemy = caster?.Team != null && target.Team != null && caster.Team != target.Team;
            bool isAlly  = caster?.Team != null && target.Team != null && caster.Team == target.Team;

            ColorSchool glowColor = cast.VisualColor;
            BeginAgentGlowRaw(target, ColorSchoolData.GetGlowColor(glowColor), 2f);

            // Damage — fire hits everyone (friendly fire)
            if (cast.DamageCount > 0)
            {
                DamageAgent(target, cast.DamageCount * 25f);
                ApplyDamageEnchantments(target, cast, caster);
            }

            // Restore — fire heals allies
            if (cast.RestoreCount > 0 && isAlly)
            {
                HealAgent(target, cast.RestoreCount * 15f);
                ApplyRestoreEnchantments(target, cast, caster);
            }
        }

        // ── Enchantment application ────────────────────────────────────────────

        private static void ApplyDamageEnchantments(Agent target, SpellCast cast, Agent caster)
        {
            // Scatter: push enemies back + sear limbs to slow movement (merged Char)
            if (CasterHasEnchantment(caster, TalentId.Scatter))
            {
                bool isMounted = false;
                try { isMounted = target.MountAgent != null; } catch { }
                if (!isMounted)
                {
                    float dist = cast.DamageCount * 4f;
                    Vec3 origin = caster?.Position ?? target.Position;
                    Vec3 dir = (target.Position - origin);
                    if (dir.Length < 0.01f) dir = new Vec3(1f, 0f, 0f);
                    dir = new Vec3(dir.x, dir.y, 0f).NormalizedCopy();
                    Vec3 dest = target.Position + dir * dist;
                    dest.z = target.Position.z;
                    try { QueueMove(target, dest, 0.4f); } catch { }
                }
                if (!target.IsHero)
                {
                    try
                    {
                        float reducedSpeed = Math.Max(1f, 10f - cast.DamageCount * 2.5f);
                        float duration = 4f + cast.DamageCount * 1f;
                        if (!_charredAgents.TryGetValue(target, out var cur))
                            _charredAgents[target] = (reducedSpeed, duration);
                        else
                            _charredAgents[target] = (Math.Min(cur.ReducedSpeed, reducedSpeed), Math.Max(cur.Remaining, duration));
                        target.SetMaximumSpeedLimit(_charredAgents[target].ReducedSpeed, false);
                        BeginAgentGlow(target, ColorSchool.Red, 2f);
                    }
                    catch { }
                }
            }

            // Smoulder: morale penalty + bewildering random effect (merged Bewilder)
            if (CasterHasEnchantment(caster, TalentId.Smoulder))
            {
                try
                {
                    float delta = cast.DamageCount * 12f;
                    float cur   = target.GetMorale();
                    target.SetMorale(Math.Max(cur - delta, 0f));
                }
                catch { }
                if (!target.IsHero)
                {
                    try
                    {
                        switch (_rng.Next(4))
                        {
                            case 0:
                                try { target.SetMorale(0f); } catch { }
                                break;
                            case 1:
                                try { target.Formation?.SetMovementOrder(MovementOrder.MovementOrderCharge); } catch { }
                                break;
                            case 2:
                                bool mounted = false;
                                try { mounted = target.MountAgent != null; } catch { }
                                if (mounted) ForceDismount(target);
                                else try { target.SetMorale(0f); } catch { }
                                break;
                            case 3:
                                try { target.SetMorale(target.GetMorale() * 0.25f); } catch { }
                                break;
                        }
                    }
                    catch { }
                }
            }

            // Sunder: shred target armour (more damage received) and weapon arm (less damage dealt).
            if (CasterHasEnchantment(caster, TalentId.Sunder))
            {
                try
                {
                    float vuln = cast.DamageCount * 10f; // raw value, capped to 40% in DamageAgent
                    float attackWeaken = Math.Min(0.40f, cast.DamageCount * 0.08f);
                    float duration = 8f;
                    if (!_sunderedAgents.TryGetValue(target, out var existing))
                        _sunderedAgents[target] = (vuln, duration);
                    else
                        _sunderedAgents[target] = (Math.Max(existing.BonusVuln, vuln), Math.Max(existing.Remaining, duration));
                    if (!_attackWeakenedAgents.TryGetValue(target, out var existingWeak))
                        _attackWeakenedAgents[target] = (attackWeaken, duration);
                    else
                        _attackWeakenedAgents[target] = (Math.Max(existingWeak.ReductionPct, attackWeaken), Math.Max(existingWeak.Remaining, duration));
                    BeginAgentGlow(target, ColorSchool.Red, 2f);
                }
                catch { }
            }

            // Immolate: bonus burn damage per input; at 3+ inputs one target is guaranteed to die.
            if (CasterHasEnchantment(caster, TalentId.Immolate))
            {
                try
                {
                    if (cast.DamageCount >= 3 && !_immolateKillUsed)
                    {
                        _immolateKillUsed = true;
                        QueueKill(target);
                        BeginAgentGlow(target, ColorSchool.Red, 2f);
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Immolate — consumed.", new Color(1f, 0.4f, 0.1f)));
                    }
                    else
                    {
                        DamageAgent(target, cast.DamageCount * 10f);
                        BeginAgentGlow(target, ColorSchool.Red, 1.5f);
                    }
                }
                catch { }
            }
        }

        private static void ApplyRestoreEnchantments(Agent target, SpellCast cast, Agent caster)
        {
            // Ashveil: brief magic immunity
            if (CasterHasEnchantment(caster, TalentId.Ashveil))
            {
                float duration = cast.RestoreCount * 3f;
                float current  = _wardedAgents.TryGetValue(target, out float t) ? t : 0f;
                _wardedAgents[target] = Math.Max(current, duration);
                BeginAgentGlow(target, ColorSchool.White, duration);
            }

            // Cinder Shell: armour boost + near-full-health shield (merged Overflow)
            if (CasterHasEnchantment(caster, TalentId.CinderShell))
            {
                float bonus = cast.RestoreCount * 10f;
                AddStoneskin(target, bonus, 8f);
                BeginAgentGlow(target, ColorSchool.Orange, 2f);
                try
                {
                    float hp = target.Health;
                    float hpMax = target.HealthLimit;
                    if (hpMax > 0f && hp >= hpMax * 0.90f)
                    {
                        float overBonus = cast.RestoreCount * 15f;
                        AddStoneskin(target, overBonus, 5f);
                    }
                }
                catch { }
            }

            // Hearthlight: morale boost
            if (CasterHasEnchantment(caster, TalentId.Hearthlight))
            {
                try
                {
                    float delta = cast.RestoreCount * 12f;
                    float cur   = target.GetMorale();
                    target.SetMorale(Math.Min(cur + delta, 100f));
                }
                catch { }
            }

            // Reflect: melee damage reflection — any hit on the warded ally bounces back.
            if (CasterHasEnchantment(caster, TalentId.Reflect))
            {
                try
                {
                    float pct = Math.Min(0.40f, cast.RestoreCount * 0.08f);
                    float duration = 3f + cast.RestoreCount * 1f;
                    if (!_reflectAgents.TryGetValue(target, out var cur))
                        _reflectAgents[target] = (pct, duration);
                    else
                        _reflectAgents[target] = (Math.Max(cur.ReflectPct, pct), Math.Max(cur.Remaining, duration));
                    BeginAgentGlow(target, ColorSchool.Orange, 2f);
                }
                catch { }
            }
        }

        // Checks whether a caster (player or NPC lord) has a given enchantment talent.
        private static bool CasterHasEnchantment(Agent caster, TalentId enchantment)
        {
            if (caster == null) return false;
            if (caster == Agent.Main) return TalentSystem.Has(enchantment);
            try
            {
                Hero hero = (caster.Character as TaleWorlds.CampaignSystem.CharacterObject)?.HeroObject;
                if (hero != null && ColourLordRegistry.IsColourLord(hero))
                    return ColourLordRegistry.HasTalent(hero, enchantment);
            }
            catch { }
            return false;
        }

        // ── Stoneskin (Cinder Shell enchantment state) ─────────────────────────
        private static readonly Dictionary<Agent, (float BonusArmor, float Remaining)>
            _stoneskinAgents = new Dictionary<Agent, (float, float)>();

        private static void AddStoneskin(Agent agent, float bonus, float duration)
        {
            if (agent == null) return;
            if (!_stoneskinAgents.TryGetValue(agent, out var cur))
                _stoneskinAgents[agent] = (bonus, duration);
            else
                _stoneskinAgents[agent] = (Math.Max(cur.BonusArmor, bonus), Math.Max(cur.Remaining, duration));
        }

        public static void TickStoneskin(float dt)
        {
            foreach (Agent key in _stoneskinAgents.Keys.ToList())
            {
                var (bonus, remaining) = _stoneskinAgents[key];
                remaining -= dt;
                if (remaining <= 0f || key == null || !key.IsActive())
                    _stoneskinAgents.Remove(key);
                else
                    _stoneskinAgents[key] = (bonus, remaining);
            }
        }

        public static void ClearStoneskin() => _stoneskinAgents.Clear();

        // ── Sunder (Sunder enchantment state) ──────────────────────────────────
        private static readonly Dictionary<Agent, (float BonusVuln, float Remaining)>
            _sunderedAgents = new Dictionary<Agent, (float, float)>();

        public static void TickSunder(float dt)
        {
            foreach (Agent key in _sunderedAgents.Keys.ToList())
            {
                var (vuln, remaining) = _sunderedAgents[key];
                remaining -= dt;
                if (remaining <= 0f || key == null || !key.IsActive())
                    _sunderedAgents.Remove(key);
                else
                    _sunderedAgents[key] = (vuln, remaining);
            }
        }

        public static void ClearSunder() => _sunderedAgents.Clear();

        // ── Attack weakening (Sunder enchantment — outgoing damage reduction) ───
        private static readonly Dictionary<Agent, (float ReductionPct, float Remaining)>
            _attackWeakenedAgents = new Dictionary<Agent, (float, float)>();

        /// <summary>
        /// Called from MagicMissionBehavior.OnAgentHit. If the attacker is Sundered,
        /// heals back a portion of the damage dealt — effectively reducing their attack power.
        /// </summary>
        public static void TryApplyAttackWeakening(Agent victim, Agent attacker, int inflictedDamage)
        {
            if (victim == null || attacker == null || inflictedDamage <= 0) return;
            if (!_attackWeakenedAgents.TryGetValue(attacker, out var w) || w.Remaining <= 0f) return;
            try
            {
                float healBack = inflictedDamage * w.ReductionPct;
                if (healBack >= 1f) HealAgent(victim, healBack);
            }
            catch { }
        }

        public static void TickAttackWeaken(float dt)
        {
            foreach (Agent key in _attackWeakenedAgents.Keys.ToList())
            {
                var (pct, remaining) = _attackWeakenedAgents[key];
                remaining -= dt;
                if (remaining <= 0f || key == null || !key.IsActive())
                    _attackWeakenedAgents.Remove(key);
                else
                    _attackWeakenedAgents[key] = (pct, remaining);
            }
        }

        public static void ClearAttackWeaken() => _attackWeakenedAgents.Clear();

        // ── Immolate (per-cast kill flag) ────────────────────────────────────────
        // Prevents more than one guaranteed kill per spell cast at 3+ Damage inputs.
        private static bool _immolateKillUsed = false;
        public static void ResetImmolateKill() => _immolateKillUsed = false;

        // ── Char (Char enchantment state — movement slow) ──────────────────────
        // Stores (reduced speed cap, remaining duration). On expire, restores to 10f (unlimited).
        private static readonly Dictionary<Agent, (float ReducedSpeed, float Remaining)>
            _charredAgents = new Dictionary<Agent, (float, float)>();

        public static void TickChar(float dt)
        {
            foreach (Agent key in _charredAgents.Keys.ToList())
            {
                var (speed, remaining) = _charredAgents[key];
                remaining -= dt;
                if (remaining <= 0f || key == null || !key.IsActive())
                {
                    if (key != null && key.IsActive())
                        try { key.SetMaximumSpeedLimit(10f, false); } catch { }
                    _charredAgents.Remove(key);
                }
                else
                    _charredAgents[key] = (speed, remaining);
            }
        }

        public static void ClearChar()
        {
            foreach (var kv in _charredAgents)
                if (kv.Key != null && kv.Key.IsActive())
                    try { kv.Key.SetMaximumSpeedLimit(10f, false); } catch { }
            _charredAgents.Clear();
        }

        // ── Reflect (Reflect enchantment state — melee damage reflection) ───────
        private static readonly Dictionary<Agent, (float ReflectPct, float Remaining)>
            _reflectAgents = new Dictionary<Agent, (float, float)>();

        /// <summary>
        /// Called from MagicMissionBehavior.OnAgentHit. If the victim has an active
        /// Reflect buff, deals a portion of the incoming melee damage back to the attacker.
        /// DamageAgent sets health directly (not through the hit system) so there is no
        /// reflect-chain risk.
        /// </summary>
        public static void TryApplyReflect(Agent victim, Agent attacker, int inflictedDamage)
        {
            if (victim == null || attacker == null || attacker.IsMount || inflictedDamage <= 0) return;
            if (!_reflectAgents.TryGetValue(victim, out var r) || r.Remaining <= 0f) return;
            try
            {
                float reflectDmg = inflictedDamage * r.ReflectPct;
                if (reflectDmg >= 1f)
                {
                    DamageAgent(attacker, reflectDmg);
                    BeginAgentGlowRaw(victim, new Color(1f, 0.5f, 0.2f).ToUnsignedInteger(), 0.5f);
                }
            }
            catch { }
        }

        public static void TickReflect(float dt)
        {
            foreach (Agent key in _reflectAgents.Keys.ToList())
            {
                var (pct, remaining) = _reflectAgents[key];
                remaining -= dt;
                if (remaining <= 0f || key == null || !key.IsActive())
                    _reflectAgents.Remove(key);
                else
                    _reflectAgents[key] = (pct, remaining);
            }
        }

        public static void ClearReflect() => _reflectAgents.Clear();

        // ── Flashfire (passive — spell echo) ────────────────────────────────────
        // Prevents recursion if Flashfire somehow echoes into itself.
        private static bool _flashfireActive = false;

        public static void TryFlashfire(SpellCast cast)
        {
            if (!TalentSystem.Has(TalentId.Flashfire)) return;
            if (_flashfireActive || Agent.Main == null) return;
            if (_rng.NextDouble() >= 0.10) return;
            _flashfireActive = true;
            try
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "Flashfire — the flame echoes.", new Color(1f, 0.85f, 0.3f)));
                SpellBuilder.Execute(cast, true);
            }
            finally { _flashfireActive = false; }
        }

        // ── Dismount helper ────────────────────────────────────────────────────
        public static void ForceDismount(Agent a)
        {
            Agent mount = null;
            try { mount = a.MountAgent; } catch { }
            if (mount == null || !mount.IsActive()) return;
            try
            {
                Blow b = BuildBlow(mount, DamageTypes.Blunt, mount.HealthLimit + 1f);
                mount.Die(b, (Agent.KillInfo)0);
            }
            catch { }
        }

        // ── Magic-event memory ─────────────────────────────────────────────────
        private static readonly List<(Vec3 Pos, float Left)> _recentMagicEvents
            = new List<(Vec3, float)>();
        private const float MagicMemoryDuration = 8f;

        public static void RecordMagicCast(Vec3 position)
            => _recentMagicEvents.Add((position, MagicMemoryDuration));

        public static bool HasRecentMagicNearby(Vec3 position, float radius)
        {
            foreach (var e in _recentMagicEvents)
                if (e.Pos.Distance(position) <= radius) return true;
            return false;
        }

        public static void TickMagicMemory(float dt)
        {
            for (int i = _recentMagicEvents.Count - 1; i >= 0; i--)
            {
                float left = _recentMagicEvents[i].Left - dt;
                if (left <= 0f) _recentMagicEvents.RemoveAt(i);
                else _recentMagicEvents[i] = (_recentMagicEvents[i].Pos, left);
            }
        }

        public static void ClearMagicMemory() => _recentMagicEvents.Clear();

        // ── Siege check ────────────────────────────────────────────────────────
        public static void IssueChargeToOwnFormations(Agent caster) { }

        // ── Battle command ─────────────────────────────────────────────────────
        public enum BattleCommandKind { Halt, Enrage, Dismount, StopArrows }

        public static void IssueBattleCommand(Agent source, BattleCommandKind kind,
            string successText, ColorSchool school)
        {
            if (source == null || Mission.Current == null || Mission.Current.Scene == null) return;
            var formations = new HashSet<Formation>();
            var scene = Mission.Current.Scene;
            foreach (Agent a in EnemiesOf(source).ToList())
            {
                if (a.Formation == null) continue;
                if (a.Position.Distance(source.Position) > 500f) continue;
                bool visible = true;
                try { visible = scene.CheckPointCanSeePoint(source.Position, a.Position, 500f); } catch { }
                if (!visible) continue;
                formations.Add(a.Formation);
                BeginAgentGlow(a, school, 1.5f);
            }
            if (formations.Count == 0) return;
            foreach (Formation f in formations)
            {
                try
                {
                    switch (kind)
                    {
                        case BattleCommandKind.Halt:
                            foreach (Agent fa in Mission.Current.Agents.Where(a => a.IsActive() && a.Formation == f).ToList())
                                try { fa.SetMorale(0f); } catch { }
                            break;
                        case BattleCommandKind.Enrage:
                            foreach (Agent fa in Mission.Current.Agents.Where(a => a.IsActive() && a.Formation == f).ToList())
                                try { fa.SetMorale(100f); } catch { }
                            if (!IsSiegeActive()) try { f.SetMovementOrder(MovementOrder.MovementOrderCharge); } catch { }
                            break;
                    }
                }
                catch { }
            }
        }

        // ── Sound ──────────────────────────────────────────────────────────────
        private static MethodInfo _soundGetId;

        private static bool TryResolveSoundEvent()
        {
            if (_soundGetId != null) return true;
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    foreach (string candidate in new[] { "TaleWorlds.MountAndBlade.SoundEvent", "TaleWorlds.Engine.SoundEvent" })
                    {
                        Type t = asm.GetType(candidate);
                        if (t == null) continue;
                        MethodInfo m = t.GetMethod("GetEventIdFromString", BindingFlags.Public | BindingFlags.Static);
                        if (m == null) continue;
                        _soundGetId = m;
                        return true;
                    }
            }
            catch { }
            return false;
        }

        public static void TryCastSound(Vec3 position, ColorSchool school)
        {
            if (Mission.Current == null || !TryResolveSoundEvent()) return;
            string[] candidates = school == ColorSchool.Red || school == ColorSchool.Purple
                ? new[] { "event:/mission/ambient/detail/wind_hit", "event:/ui/panels/open" }
                : new[] { "event:/ui/notifications/quest_update", "event:/ui/panels/open" };
            foreach (string path in candidates)
            {
                try
                {
                    object idObj = _soundGetId.Invoke(null, new object[] { path });
                    if (idObj == null) continue;
                    int soundId = (int)idObj;
                    if (soundId < 0) continue;
                    Mission.Current.MakeSound(soundId, position, false, false, -1, -1);
                    return;
                }
                catch { }
            }
        }

        // ── Cast animation ──────────────────────────────────────────────────────
        private static readonly ActionIndexCache _castAnimCache      = ActionIndexCache.Create("act_cheer_1");
        private static readonly ActionIndexCache _castAnimClearCache = ActionIndexCache.Create("act_none");
        private static readonly List<(Agent agent, float remaining)> _animClearTimers
            = new List<(Agent, float)>();

        // Looping cast animation: re-applies the animation every 0.65 s so it plays
        // continuously while the player holds focus or an NPC is winding up.
        private const float CastLoopInterval = 0.65f;
        private static readonly List<(Agent agent, float reapplyIn)> _castLoops
            = new List<(Agent, float)>();

        // Deferred NPC casts: the action fires after a short wind-up delay.
        private const float NpcCastWindup = 0.7f;
        private static readonly List<(Agent agent, float remaining, Action action)> _pendingNpcCasts
            = new List<(Agent, float, Action)>();

        public static void TickAnimClears(float dt)
        {
            for (int i = _animClearTimers.Count - 1; i >= 0; i--)
            {
                float t = _animClearTimers[i].remaining - dt;
                if (t <= 0f)
                {
                    var a = _animClearTimers[i].agent;
                    if (a != null && a.IsActive() && a.Health > 0f)
                    {
                        bool mounted    = false; try { mounted    = a.MountAgent != null;   } catch { }
                        bool usingEquip = false; try { usingEquip = a.IsUsingGameObject;    } catch { }
                        if (!mounted && !usingEquip)
                            try { a.SetActionChannel(0, _castAnimClearCache, true, 0UL); } catch { }
                    }
                    _animClearTimers.RemoveAt(i);
                }
                else _animClearTimers[i] = (_animClearTimers[i].agent, t);
            }
        }

        public static void TickCastLoops(float dt)
        {
            for (int i = _castLoops.Count - 1; i >= 0; i--)
            {
                var (a, t) = _castLoops[i];
                if (a == null || !a.IsActive() || a.Health <= 0f) { _castLoops.RemoveAt(i); continue; }
                float newT = t - dt;
                if (newT <= 0f)
                {
                    bool mounted    = false; try { mounted    = a.MountAgent != null;  } catch { }
                    bool usingEquip = false; try { usingEquip = a.IsUsingGameObject;   } catch { }
                    if (!mounted && !usingEquip)
                        try { a.SetActionChannel(0, _castAnimCache, true, 0UL); } catch { }
                    _castLoops[i] = (a, CastLoopInterval);
                }
                else _castLoops[i] = (a, newT);
            }
        }

        public static void TickPendingNpcCasts(float dt)
        {
            for (int i = _pendingNpcCasts.Count - 1; i >= 0; i--)
            {
                var (a, t, action) = _pendingNpcCasts[i];
                if (a == null || !a.IsActive() || a.Health <= 0f)
                {
                    EndCastLoop(a);
                    _pendingNpcCasts.RemoveAt(i);
                    continue;
                }
                float newT = t - dt;
                if (newT <= 0f)
                {
                    EndCastLoop(a);
                    try { action(); } catch { }
                    _pendingNpcCasts.RemoveAt(i);
                }
                else _pendingNpcCasts[i] = (a, newT, action);
            }
        }

        public static void BeginCastLoop(Agent agent)
        {
            if (agent == null || !agent.IsActive() || agent.Health <= 0f) return;
            try { if (agent.MountAgent != null) return; } catch { }
            try { if (agent.IsUsingGameObject) return; } catch { }
            // Cancel any pending clear and remove stale loop entry for this agent
            int ci = _animClearTimers.FindIndex(x => x.agent == agent);
            if (ci >= 0) _animClearTimers.RemoveAt(ci);
            int li = _castLoops.FindIndex(x => x.agent == agent);
            if (li >= 0) _castLoops.RemoveAt(li);
            try { agent.SetActionChannel(0, _castAnimCache, true, 0UL); } catch { }
            _castLoops.Add((agent, CastLoopInterval));
        }

        public static void EndCastLoop(Agent agent)
        {
            int li = _castLoops.FindIndex(x => x.agent == agent);
            if (li >= 0) _castLoops.RemoveAt(li);
            // Queue a short clear so the agent returns to idle if no spell fires.
            // TryCastAnimation will overwrite this with its own 0.8s timer when a spell does fire.
            if (agent == null || !agent.IsActive() || agent.Health <= 0f) return;
            int ci = _animClearTimers.FindIndex(x => x.agent == agent);
            if (ci >= 0) _animClearTimers[ci] = (agent, 0.15f);
            else _animClearTimers.Add((agent, 0.15f));
        }

        public static void QueueNpcCastWithWindup(Agent agent, Action castAction)
        {
            BeginCastLoop(agent);
            _pendingNpcCasts.RemoveAll(x => x.agent == agent);
            _pendingNpcCasts.Add((agent, NpcCastWindup, castAction));
        }

        public static void ClearAnimTimers()
        {
            foreach (var (agent, _) in _animClearTimers)
                if (agent != null && agent.IsActive() && agent.Health > 0f)
                    try { agent.SetActionChannel(0, _castAnimClearCache, true, 0UL); } catch { }
            _animClearTimers.Clear();
        }

        public static void ClearCastLoops()
        {
            _castLoops.Clear();
            _pendingNpcCasts.Clear();
        }

        public static void TryCastAnimation(Agent agent)
        {
            if (agent == null || !agent.IsActive() || agent.Health <= 0f) return;
            try { if (agent.MountAgent != null) return; } catch { }
            try { if (agent.IsUsingGameObject) return; } catch { }
            // Stop any ongoing loop so the final cast pose plays cleanly
            EndCastLoop(agent);
            try
            {
                agent.SetActionChannel(0, _castAnimCache, true, 0UL);
                int idx = _animClearTimers.FindIndex(x => x.agent == agent);
                if (idx >= 0) _animClearTimers.RemoveAt(idx);
                _animClearTimers.Add((agent, 0.8f));
            }
            catch { }
        }

        // ── Militia helper ─────────────────────────────────────────────────────
        private static MethodInfo _setMilitiaSetter;
        private static bool _setMilitiaResolved;

        public static bool TrySetMilitia(Village v, float value)
        {
            if (!_setMilitiaResolved)
            {
                _setMilitiaResolved = true;
                PropertyInfo prop = typeof(Village).GetProperty("Militia",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                _setMilitiaSetter = prop?.GetSetMethod(nonPublic: true);
            }
            if (_setMilitiaSetter == null) return false;
            try { _setMilitiaSetter.Invoke(v, new object[] { value }); return true; } catch { return false; }
        }

        private static void Msg(string text, ColorSchool school) =>
            InformationManager.DisplayMessage(new InformationMessage(
                text, ColorSchoolData.GetMessageColor(school)));

        // ── NPC-specific area-effect spawners ─────────────────────────────────
        public static void SpawnNpcBlueWall(Vec3 position, Vec3 fwd, Team casterTeam)
        {
            for (int i = 0; i < 3; i++)
            {
                Vec3 right = new Vec3(-fwd.y, fwd.x, 0f).NormalizedCopy();
                Vec3 pos = position + fwd * 2f + right * ((i - 1) * 2f);
                var node = new AreaEffect
                {
                    Id = "npc_barrier", School = ColorSchool.Blue, Position = pos,
                    Radius = 1.5f, TickInterval = 2f, TickTimer = 2f,
                    Remaining = 15f, Power = 1f, CasterTeam = casterTeam
                };
                node.LightEntity = SpawnAreaLight(node.Position, ColorSchool.Blue, 5f);
                _areaEffects.Add(node);
            }
        }

        public static void SpawnNpcHealZone(Vec3 position, ColorSchool school, float power, Team casterTeam)
        {
            var node = new AreaEffect
            {
                Id = "npc_heal_zone", School = school, Position = position,
                Radius = 5f, TickInterval = 2f, TickTimer = 2f,
                Remaining = 12f, Power = power, CasterTeam = casterTeam
            };
            node.LightEntity = SpawnAreaLight(node.Position, school, 5f);
            _areaEffects.Add(node);
        }

        public static void SpawnNpcYellowCloud(Vec3 position, float power, Team casterTeam)
        {
            var node = new AreaEffect
            {
                Id = "npc_yellow_cloud", School = ColorSchool.Yellow, Position = position,
                Radius = 5f, TickInterval = 2f, TickTimer = 2f,
                Remaining = 10f, Power = power, CasterTeam = casterTeam,
                DirTimer = 3f
            };
            node.LightEntity = SpawnAreaLight(node.Position, ColorSchool.Yellow, 5f);
            _areaEffects.Add(node);
        }

        public static void SpawnNpcMoraleAura(Vec3 position, Team casterTeam)
        {
            var node = new AreaEffect
            {
                Id = "npc_morale_aura", School = ColorSchool.Yellow, Position = position,
                Radius = 8f, TickInterval = 3f, TickTimer = 3f,
                Remaining = 15f, Power = 1f, CasterTeam = casterTeam
            };
            node.LightEntity = SpawnAreaLight(node.Position, ColorSchool.Yellow, 6f);
            _areaEffects.Add(node);
        }
    }
}
