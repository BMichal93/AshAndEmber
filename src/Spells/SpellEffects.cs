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
            var cast = new SpellCast
            {
                Form = SpellForm.Blast, FormCount = formCount, BlastCount = formCount,
                DamageCount = damageCount, RestoreCount = restoreCount
            };
            ExecuteBlastFromAgent(caster, cast, casterTeam);
        }

        public static void ExecuteNpcBurst(Agent caster, int formCount,
            int damageCount, int restoreCount, Team casterTeam)
        {
            var cast = new SpellCast
            {
                Form = SpellForm.Burst, FormCount = formCount, BurstCount = formCount,
                DamageCount = damageCount, RestoreCount = restoreCount
            };
            ExecuteBurstFromAgent(caster, cast, casterTeam);
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

            // Damage — fire hits enemies
            if (cast.DamageCount > 0 && isEnemy)
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
            // Scatter: push enemies back
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
            }

            // Smoulder: morale penalty
            if (CasterHasEnchantment(caster, TalentId.Smoulder))
            {
                try
                {
                    float delta = cast.DamageCount * 12f;
                    float cur   = target.GetMorale();
                    target.SetMorale(Math.Max(cur - delta, 0f));
                }
                catch { }
            }

            // Bewilder: random command (not on heroes)
            if (CasterHasEnchantment(caster, TalentId.Bewilder) && !target.IsHero)
            {
                try
                {
                    switch (_rng.Next(4))
                    {
                        case 0: // Panic — route
                            try { target.SetMorale(0f); } catch { }
                            break;
                        case 1: // Charge — force into melee
                            try { target.Formation?.SetMovementOrder(MovementOrder.MovementOrderCharge); } catch { }
                            break;
                        case 2: // Dismount
                            bool mounted = false;
                            try { mounted = target.MountAgent != null; } catch { }
                            if (mounted) ForceDismount(target);
                            else try { target.SetMorale(0f); } catch { }
                            break;
                        case 3: // Shatter nerve — morale fractured to a quarter
                            try { target.SetMorale(target.GetMorale() * 0.25f); } catch { }
                            break;
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

            // Cinder Shell: armour boost (damage reduction)
            if (CasterHasEnchantment(caster, TalentId.CinderShell))
            {
                float bonus = cast.RestoreCount * 10f;
                AddStoneskin(target, bonus, 8f);
                BeginAgentGlow(target, ColorSchool.Orange, 2f);
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

        public static void ClearAnimTimers()
        {
            foreach (var (agent, _) in _animClearTimers)
                if (agent != null && agent.IsActive() && agent.Health > 0f)
                    try { agent.SetActionChannel(0, _castAnimClearCache, true, 0UL); } catch { }
            _animClearTimers.Clear();
        }

        public static void TryCastAnimation(Agent agent)
        {
            if (agent == null || !agent.IsActive() || agent.Health <= 0f) return;
            try { if (agent.MountAgent != null) return; } catch { }
            try { if (agent.IsUsingGameObject) return; } catch { }
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
