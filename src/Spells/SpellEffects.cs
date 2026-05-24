// =============================================================================
// COLOURS OF CALRADIA — SpellEffects.cs
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

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("ColoursOfCalradia.Tests")]

namespace ColoursOfCalradia
{
    public static partial class SpellEffects
    {
        private static readonly Random _rng = new Random();


        internal enum LightLevel { Bright, Dim, Dark }

        internal static LightLevel GetCampaignLightLevel()
        {
            if (Campaign.Current == null) return LightLevel.Bright;

            try
            {
                float hour = (float)(CampaignTime.Now.ToHours % 24.0);
                if (hour < 5f  || hour >= 22f) return LightLevel.Dark;
                if (hour < 7f  || hour >= 20f) return LightLevel.Dim;
                return LightLevel.Bright;
            }
            catch { return LightLevel.Bright; }
        }

        internal static LightLevel GetLightLevel()
        {
            // Scene-based check takes priority over time of day
            try
            {
                var scene = Mission.Current?.Scene;
                if (scene != null)
                {
                    string sn = scene.GetName()?.ToLowerInvariant() ?? "";
                    if (sn.Contains("cave") || sn.Contains("mine") || sn.Contains("dungeon"))
                        return LightLevel.Dark;
                    if (sn.Contains("hideout"))
                        return LightLevel.Dim;
                }
            }
            catch { }

            return GetCampaignLightLevel();
        }

        // 33 % chance used when casting in Dim light.
        internal static bool RollDimFizzle() => _rng.Next(3) == 0;

        // True when the given school has a contextual affinity that upgrades Dark → Dim.
        // Season: 84-day year, 21 days per season (Spring=0, Summer=1, Autumn=2, Winter=3).
        internal static bool HasDarkAffinity(ColorSchool school)
        {
            try
            {
                var s = Settlement.CurrentSettlement;
                if (s?.IsTown == true || s?.IsCastle == true) return true; // torchlight
            }
            catch { }

            if (Campaign.Current == null) return false;
            try
            {
                switch (CampaignTime.Now.GetSeasonOfYear)
                {
                    case CampaignTime.Seasons.Spring: return school == ColorSchool.Green;
                    case CampaignTime.Seasons.Summer: return school == ColorSchool.Yellow || school == ColorSchool.Red;
                    case CampaignTime.Seasons.Autumn: return school == ColorSchool.Orange || school == ColorSchool.Red;
                    case CampaignTime.Seasons.Winter: return school == ColorSchool.Blue   || school == ColorSchool.Purple;
                    default: return false;
                }
            }
            catch { return false; }
        }

        // School-aware effective light level: upgrades Dark → Dim when the school has affinity.
        internal static LightLevel GetEffectiveLightLevel(ColorSchool school)
        {
            var level = GetLightLevel();
            if (level == LightLevel.Dark && HasDarkAffinity(school)) return LightLevel.Dim;
            return level;
        }

        public static bool IsDaytime() => GetLightLevel() == LightLevel.Bright;

        // ── Spell power scaling ───────────────────────────────────────────────────
        // Multiplier range: ×0.6 (attr 1) → ×1.0 (attr 5, mid-game base) → ×1.5 (attr 10).
        // Pass null hero to use Hero.MainHero (player casts).
        internal static float SpellPower(ColorSchool school, Hero hero = null)
        {
            try
            {
                var h = hero ?? Hero.MainHero;
                if (h == null) return 1f;
                int attr = h.GetAttributeValue(ColorSchoolData.GetScaleAttribute(school));
                return 0.5f + Math.Min(Math.Max(attr, 1), 10) * 0.1f;
            }
            catch { return 1f; }
        }

        // Returns true if any agent on the field is operating siege equipment.
        // Used to gate Formation.SetMovementOrder calls that crash on siege formations.
        public static bool IsSiegeActive()
        {
            if (Mission.Current == null) return false;
            foreach (Agent a in Mission.Current.Agents)
            {
                if (!a.IsActive()) continue;
                try { if (a.IsUsingGameObject) return true; } catch { }
            }
            return false;
        }

        // Returns true only during actual combat missions (battles, sieges).
        // Town/village visits have no active enemy agents, so this reliably excludes them.
        public static bool IsBattleMission()
        {
            try
            {
                if (Mission.Current == null || Mission.Current.PlayerTeam == null) return false;
                Team playerTeam = Mission.Current.PlayerTeam;
                foreach (Agent a in Mission.Current.Agents)
                {
                    if (!a.IsActive() || a.IsMount || a.Team == null) continue;
                    if (a.Team != playerTeam && playerTeam.IsEnemyOf(a.Team)) return true;
                }
            }
            catch { }
            return false;
        }

        // Returns false — Cerulean Mirror replaced by Cerulean Burst (instant AoE, no deflection)
        public static bool ProtectedByMirror(Agent a) => false;

        public static void TickHaltedAgents(float dt)
        {
            if (_haltedAgents.Count == 0 || Mission.Current == null) return;
            _haltTeleportTimer -= dt;
            bool doTeleport = _haltTeleportTimer <= 0f;
            if (doTeleport) _haltTeleportTimer = HaltTeleportInterval;

            // Reuse static collections to keep TickHaltedAgents allocation-free
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
                var (remaining, frozenPos) = _haltedAgents[idx];
                remaining -= dt;
                if (!_haltAgentMap.TryGetValue(idx, out Agent a))
                {
                    _expiredHaltKeys.Add(idx);
                    continue;
                }
                bool usingEquip = false;
                try { usingEquip = a.IsUsingGameObject; } catch { }

                if (remaining <= 0f || usingEquip)
                {
                    _expiredHaltKeys.Add(idx);
                    if (!usingEquip)
                        try { a.SetMaximumSpeedLimit(10f, false); } catch { }
                }
                else
                {
                    _haltedAgents[idx] = (remaining, frozenPos);
                    try { a.SetMaximumSpeedLimit(0f, false); } catch { }
                    if (doTeleport && a.MountAgent == null) try { a.TeleportToPosition(frozenPos); } catch { }
                }
            }
            foreach (int idx in _expiredHaltKeys) _haltedAgents.Remove(idx);
        }

        public static void ClearSelfEffects()
        {
            RemoveAreaEffect("self_red_barrier");
            _haltedAgents.Clear();
        }

        // Issue Charge to own formations (Red post-cast limitation)
        public static void IssueChargeToOwnFormations(Agent caster)
        {
            if (caster == null || Mission.Current == null || caster.Team == null) return;
            if (IsSiegeActive()) return;
            try
            {
                foreach (Team t in Mission.Current.Teams)
                {
                    if (t != caster.Team) continue;
                    foreach (Formation f in t.FormationsIncludingSpecialAndEmpty)
                    {
                        if (f.CountOfUnits <= 0) continue;
                        try { f.SetMovementOrder(MovementOrder.MovementOrderCharge); } catch { }
                    }
                }
            }
            catch { }
        }

        // Random unit magic timer
        private static float _randomMagicTimer = 0f;
        private const  float RandomMagicInterval = 60f;

        public static void TickRandomUnitMagic(float dt)
        {
            if (Mission.Current == null) return;
            _randomMagicTimer += dt;
            if (_randomMagicTimer < RandomMagicInterval) return;
            _randomMagicTimer = 0f;

            if (!IsBattleMission()) return;

            // 0.7% chance per minute
            if (_rng.Next(1000) >= 7) return;

            var candidates = Mission.Current.Agents
                .Where(a => {
                    if (!a.IsActive() || a.IsMount || a.IsHero) return false;
                    try { if (a.IsUsingGameObject) return false; } catch { }
                    return true;
                }).ToList();
            if (candidates.Count == 0) return;

            Agent unit  = candidates[_rng.Next(candidates.Count)];
            var schools = Enum.GetValues(typeof(ColorSchool));
            ColorSchool school = (ColorSchool)schools.GetValue(_rng.Next(schools.Length));

            try
            {
                BeginAgentGlow(unit, school, 3f);
                // Simple effect: small area damage if it's a combat school
                if (school == ColorSchool.Red || school == ColorSchool.Purple)
                {
                    foreach (Agent near in Mission.Current.Agents
                        .Where(a => a != unit && a.IsActive() && !a.IsMount &&
                                    a.Team != unit.Team &&
                                    a.Position.Distance(unit.Position) <= 5f).ToList())
                    {
                        DamageAgent(near, 20f);
                        BeginAgentGlow(near, school, 1.5f);
                    }
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"A flicker of {ColorSchoolData.Info[school].Name} colour — {unit.Name} unleashes something wild.",
                        ColorSchoolData.GetMessageColor(school)));
                }
                else
                {
                    // Supportive schools: small morale or HP burst
                    if (unit.Team != null)
                    {
                        foreach (Agent near in Mission.Current.Agents
                            .Where(a => a.IsActive() && !a.IsMount && a.Team == unit.Team &&
                                        a.Position.Distance(unit.Position) <= 5f).ToList())
                        {
                            near.Health = Math.Min(near.HealthLimit, near.Health + 15f);
                            BeginAgentGlow(near, school, 1.5f);
                        }
                    }
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"A flicker of {ColorSchoolData.Info[school].Name} colour ripples through {unit.Name}.",
                        ColorSchoolData.GetMessageColor(school)));
                }
            }
            catch { }
        }

        // ── Helpers ──────────────────────────────────────────────────────────
        private static Agent Player => Agent.Main;

        // ── Deferred death queue ──────────────────────────────────────────────
        // Die() called from within OnMissionTick callbacks causes native engine crashes
        // in large sieges when many agents are killed simultaneously (engine reads weapon/
        // animation state that is mid-update). Queue kills and flush at end of each tick.
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
            // Skip during auto-resolve or mission teardown — Die() on agents in these states
            // can produce native engine crashes that C# try/catch cannot intercept.
            if (mission == null || mission.CurrentState != Mission.State.Continuing)
            {
                _pendingDeaths.Clear();
                return;
            }
            foreach (Agent a in _pendingDeaths)
                if (a?.IsActive() == true) KillAgent(a);
            _pendingDeaths.Clear();
        }

        public static void ClearPendingDeaths() => _pendingDeaths.Clear();

        private static IEnumerable<Agent> Enemies()
        {
            if (Mission.Current == null || Player == null) yield break;
            foreach (Agent a in Mission.Current.Agents)
                if (a != Player && !a.IsMount && a.IsActive() &&
                    a.Team != null && a.Team != Player.Team)
                    yield return a;
        }

        private static IEnumerable<Agent> Allies()
        {
            if (Mission.Current == null || Player == null) yield break;
            foreach (Agent a in Mission.Current.Agents)
                if (a != Player && !a.IsMount && a.IsActive() &&
                    a.Team != null && a.Team == Player.Team)
                    yield return a;
        }

        public static void KillAgent(Agent target)
        {
            if (target == null || !target.IsActive()) return;
            if (target.IsHero)
            {
                try { target.Health = Math.Max(1f, target.Health - 2f); } catch { }
                return;
            }
            bool usingEquip = false;
            try { usingEquip = target.IsUsingGameObject; } catch { }
            if (usingEquip)
            {
                try { target.Health = 1f; } catch { }
                return;
            }
            try
            {
                Blow blow = BuildBlow(target, DamageTypes.Cut, 2000f);
                target.Die(blow, (Agent.KillInfo)0);
                return;
            }
            catch { }
            try { target.MakeDead(true, ActionIndexCache.Create("act_strike_walk_right_stance"), 0); }
            catch { }
        }

        public static void DamageAgent(Agent target, float damage, ColorSchool? damageSchool = null)
        {
            if (target == null || !target.IsActive()) return;
            if (damageSchool.HasValue && BlightSystem.IsBlight(target)
                && BlightSystem.GetBlightSchool(target) == damageSchool.Value) return;
            float newHealth = target.Health - damage;
            if (newHealth <= 0f)
            {
                if (!target.IsHero) QueueKill(target); // deferred — safe during mission tick
                else try { target.Health = 1f; } catch { }
            }
            else
            {
                try { target.Health = newHealth; } catch { }
            }
        }

        private static Blow BuildBlow(Agent target, DamageTypes type, float magnitude)
        {
            Blow blow = new Blow();
            blow.OwnerId         = -1; // no owner — prevents weapon/riding XP being awarded to the player
            blow.DamageType      = type;
            blow.BaseMagnitude   = magnitude;
            blow.InflictedDamage = (int)magnitude;
            blow.GlobalPosition  = target.Position;
            blow.Direction       = new Vec3(0f, 0f, 1f);
            blow.WeaponRecord    = new BlowWeaponRecord();
            blow.DamageCalculated = true;
            blow.NoIgnore        = true;
            return blow;
        }

        // ── Execute switch ───────────────────────────────────────────────────
        // Combos: first 2 chars = form (UU=Blast, RR=Self, LL=Create, UL=Affect, LU=Invoke, UR=Commune),
        //         last 2 chars = colour (RR=Red, LD=Orange, DD=Yellow, LL=Green, RU=Blue, DU=Purple)
        public static bool Execute(string combo)
        {
            switch (combo)
            {
                // BLAST (UU)
                case "UURR": SpellBlastRed();    break;
                case "UULD": SpellBlastOrange(); break;
                case "UUDD": SpellBlastYellow(); break;
                case "UULL": SpellBlastGreen();  break;
                case "UURU": SpellBlastBlue();   break;
                case "UUDU": SpellBlastPurple(); break;
                // SELF (RR)
                case "RRRR": SpellSelfRed();     break;
                case "RRLD": SpellSelfOrange();  break;
                case "RRDD": SpellSelfYellow();  break;
                case "RRLL": SpellSelfGreen();   break;
                case "RRRU": SpellSelfBlue();    break;
                case "RRDU": SpellSelfPurple();  break;
                // CREATE (LL)
                case "LLRR": SpellCreateRed();    break;
                case "LLLD": SpellCreateOrange(); break;
                case "LLDD": SpellCreateYellow(); break;
                case "LLLL": SpellCreateGreen();  break;
                case "LLRU": SpellCreateBlue();   break;
                case "LLDU": SpellCreatePurple(); break;
                // AFFECT (UL)
                case "ULRR": SpellAffectRed();    break;
                case "ULLD": SpellAffectOrange(); break;
                case "ULDD": SpellAffectYellow(); break;
                case "ULLL": SpellAffectGreen();  break;
                case "ULRU": SpellAffectBlue();   break;
                case "ULDU": SpellAffectPurple(); break;
                // INVOKE (LU)
                case "LURR": SpellInvokeRed();    break;
                case "LULD": SpellInvokeOrange(); break;
                case "LUDD": SpellInvokeYellow(); break;
                case "LULL": SpellInvokeGreen();  break;
                case "LURU": SpellInvokeBlue();   break;
                case "LUDU": SpellInvokePurple(); break;
                // COMMUNE (UR)
                case "URRR": SpellCommuneRed();    break;
                case "URLD": SpellCommuneOrange(); break;
                case "URDD": SpellCommuneYellow(); break;
                case "URLL": SpellCommuneGreen();  break;
                case "URRU": SpellCommuneBlue();   break;
                case "URDU": SpellCommunePurple(); break;
                default: return false;
            }
            return true;
        }

        // =================================================================
        // BATTLE COMMAND HELPER  (shared with lord AI)
        // =================================================================
        public enum BattleCommandKind { Halt, Enrage, Dismount, StopArrows }

        public static void IssueBattleCommand(Agent source, BattleCommandKind kind,
                                              string successText, ColorSchool school)
        {
            if (source == null || Mission.Current == null || Mission.Current.Scene == null)
            {
                Msg("No battle active.", school);
                return;
            }

            var formations = new HashSet<Formation>();
            var scene = Mission.Current.Scene;

            foreach (Agent a in Enemies().ToList())
            {
                if (a.Formation == null) continue;
                if (a.Position.Distance(source.Position) > 500f) continue;
                bool visible = true;
                try { visible = scene.CheckPointCanSeePoint(source.Position, a.Position, 500f); }
                catch { }
                if (!visible) continue;
                formations.Add(a.Formation);
                BeginAgentGlow(a, school, 1.5f);
            }

            if (formations.Count == 0) { Msg("No visible enemy formations.", school); return; }

            int affected = 0;
            foreach (Formation f in formations)
            {
                try
                {
                    switch (kind)
                    {
                        case BattleCommandKind.Halt:
                            foreach (Agent fa in Mission.Current.Agents
                                .Where(a => a.IsActive() && a.Formation == f).ToList())
                                try { fa.SetMorale(0f); } catch { }
                            affected++; break;
                        case BattleCommandKind.Enrage:
                            foreach (Agent fa in Mission.Current.Agents
                                .Where(a => a.IsActive() && a.Formation == f).ToList())
                                try { fa.SetMorale(100f); } catch { }
                            if (!IsSiegeActive()) try { f.SetMovementOrder(MovementOrder.MovementOrderCharge); } catch { }
                            affected++; break;
                        case BattleCommandKind.Dismount:
                            if (f.HasAnyMountedUnit && !IsSiegeActive()) { f.SetRidingOrder(RidingOrder.RidingOrderDismount); affected++; } break;
                        case BattleCommandKind.StopArrows:
                            if (f.GetCountOfUnitsBelongingToLogicalClass(TaleWorlds.Core.FormationClass.Ranged) > 0 ||
                                f.GetCountOfUnitsBelongingToLogicalClass(TaleWorlds.Core.FormationClass.HorseArcher) > 0)
                            { f.SetFiringOrder(FiringOrder.FiringOrderHoldYourFire); affected++; }
                            break;
                    }
                }
                catch { }
            }

            Msg(affected > 0
                ? string.Format(successText, affected, affected == 1 ? "" : "s")
                : "No matching formations responded.", school);
        }

        // Sound event cache
        private static MethodInfo _soundGetId;

        private static bool TryResolveSoundEvent()
        {
            if (_soundGetId != null) return true;
            try
            {
                foreach (System.Reflection.Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
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

        private static readonly ActionIndexCache _castAnimCache = ActionIndexCache.Create("act_cheer_1");
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
                    if (a != null && a.IsActive())
                    {
                        bool mounted = false;
                        try { mounted = a.MountAgent != null; } catch { }
                        bool usingEquip2 = false;
                        try { usingEquip2 = a.IsUsingGameObject; } catch { }
                        if (!mounted && !usingEquip2)
                            try { a.SetActionChannel(1, _castAnimClearCache, true, 0UL); } catch { }
                    }
                    _animClearTimers.RemoveAt(i);
                }
                else
                    _animClearTimers[i] = (_animClearTimers[i].agent, t);
            }
        }

        public static void ClearAnimTimers() => _animClearTimers.Clear();

        public static void TryCastAnimation(Agent agent)
        {
            if (agent == null || !agent.IsActive()) return;
            try { if (agent.MountAgent != null) return; } catch { }
            try { if (agent.IsUsingGameObject) return; } catch { }
            try
            {
                agent.SetActionChannel(1, _castAnimCache, false, 0UL);
                int idx = _animClearTimers.FindIndex(x => x.agent == agent);
                if (idx >= 0) _animClearTimers.RemoveAt(idx);
                _animClearTimers.Add((agent, 0.8f));
            }
            catch { }
        }

        // Battery multiplier: 1.0 normally (kept for compatibility, unused in colour system)
        private static float MageUnitBattery(Agent agent) => 1.0f;

        private static void Msg(string text, ColorSchool school) =>
            InformationManager.DisplayMessage(new InformationMessage(
                text, ColorSchoolData.GetMessageColor(school)));

        // Militia reflection helper (reused from v1)
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
            try { _setMilitiaSetter.Invoke(v, new object[] { value }); return true; }
            catch { return false; }
        }
    }
}
