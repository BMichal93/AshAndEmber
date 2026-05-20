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

            if (Campaign.Current == null) return LightLevel.Bright;

            try
            {
                float hour = (float)(CampaignTime.Now.ToHours % 24.0);
                if (hour < 5f  || hour >= 22f) return LightLevel.Dark; // 22:00–05:00 pitch night
                if (hour < 7f  || hour >= 20f) return LightLevel.Dim;  // 05:00–07:00 dawn / 20:00–22:00 dusk
                return LightLevel.Bright;                               // 07:00–20:00 full daylight
            }
            catch { return LightLevel.Bright; }
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

        // ── Duration self-effects ────────────────────────────────────────────
        // Invulnerability states for Self Red (Scarlet Ward) and Self Blue (Cerulean Mirror)
        private static bool  _scarletWardActive   = false;
        public  static bool  ScarletWardActive    => _scarletWardActive;
        private static bool  _ceruleanMirrorActive = false;
        public  static bool  CeruleanMirrorActive => _ceruleanMirrorActive;
        private static int   _ceruleanMirrorBlocks = 0;
        private static bool  _shadowVeilActive     = false;
        private static Agent      _hollowGazeTarget = null;
        private static float      _hollowGazeTimer  = 0f;
        private const  float      HollowGazeInterval = 0.3f;
        private static GameEntity _hollowGazeLight  = null;

        // Returns true when the agent is the player and Cerulean Mirror is blocking magic
        public static bool ProtectedByMirror(Agent a) => a == Player && _ceruleanMirrorActive;

        public static void TickHollowGaze(float dt)
        {
            if (_hollowGazeTarget == null) return;
            if (!_hollowGazeTarget.IsActive())
            {
                _hollowGazeTarget = null;
                try { _hollowGazeLight?.Remove(0); } catch { }
                _hollowGazeLight = null;
                return;
            }
            _hollowGazeTimer -= dt;
            if (_hollowGazeTimer > 0f) return;
            _hollowGazeTimer = HollowGazeInterval;
            Vec3 pos = _hollowGazeTarget.Position;
            try { _hollowGazeTarget.TeleportToPosition(pos); } catch { }
            try { _hollowGazeTarget.SetMorale(0f); } catch { }
            BeginAgentGlow(_hollowGazeTarget, ColorSchool.Purple, HollowGazeInterval * 4);
        }

        // ── Blue: 5-second delay before spell fires ───────────────────────────
        // When a Blue battle spell is input, the animation holds for 5 s before executing.
        private static string _pendingBlueCombo = null;
        private static float  _pendingBlueDelay = 0f;
        private const  float  BlueDelayDuration = 5f;

        public static bool IsBluePending => _pendingBlueCombo != null;

        // Returns true if the delay was accepted. False if one is already pending.
        public static bool BeginBlueDelay(string combo)
        {
            if (_pendingBlueCombo != null)
            {
                Msg("The Scholar's Weight is already winding up — wait for it to resolve.", ColorSchool.Blue);
                return false;
            }
            _pendingBlueCombo = combo;
            _pendingBlueDelay = BlueDelayDuration;
            TryCastAnimation(Player);
            Msg($"Scholar's Weight — magic stiffens your limbs. The weave resolves in {(int)BlueDelayDuration}s.", ColorSchool.Blue);
            return true;
        }

        // Called from MagicMissionBehavior.OnMissionTick.
        // Returns the combo string when the delay expires; null otherwise.
        public static string TickBlueDelay(float dt)
        {
            if (_pendingBlueCombo == null) return null;
            _pendingBlueDelay -= dt;
            if (_pendingBlueDelay > 0f) return null;
            string combo = _pendingBlueCombo;
            _pendingBlueCombo = null;
            return combo;
        }

        public static void CancelBlueDelay()
        {
            _pendingBlueCombo = null;
            _pendingBlueDelay = 0f;
        }

        public static void TickHaltedAgents(float dt)
        {
            if (_haltedAgents.Count == 0 || Mission.Current == null) return;
            _haltTeleportTimer -= dt;
            bool doTeleport = _haltTeleportTimer <= 0f;
            if (doTeleport) _haltTeleportTimer = HaltTeleportInterval;

            // Reuse static collections to keep TickHaltedAgents allocation-free
            _haltAgentMap.Clear();
            foreach (Agent a in Mission.Current.Agents)
                if (a.IsActive() && a.Health > 0f) _haltAgentMap[a.Index] = a;

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
                if (remaining <= 0f)
                {
                    _expiredHaltKeys.Add(idx);
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

        public static void ResetCampaignCounters()
        {
            _orangeCastCount    = 0; _orangeFirstCastDay  = -1;
            _wordCastCount      = 0; _wordFirstCastDay    = -1;
            _redMarchActive     = false; _redMarchEndHour = -1.0;
        }

        public static void ClearSelfEffects()
        {
            if (_scarletWardActive)    { _scarletWardActive = false; }
            if (_ceruleanMirrorActive) { _ceruleanMirrorActive = false; _ceruleanMirrorBlocks = 0; }
            _shadowVeilActive = false;
            _hollowGazeTarget = null;
            try { _hollowGazeLight?.Remove(0); } catch { }
            _hollowGazeLight  = null;
            CancelBlueDelay();
            _haltedAgents.Clear();
        }

        // Issue Charge to own formations (Red post-cast limitation)
        public static void IssueChargeToOwnFormations(Agent caster)
        {
            if (caster == null || Mission.Current == null || caster.Team == null) return;
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
                .Where(a => a.IsActive() && !a.IsMount && !a.IsHero).ToList();
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
                // Heroes go unconscious via normal battle logic — never call Die() on them.
                // Just wound them; the battle system handles incapacitation when health hits 0.
                try { target.Health = Math.Max(1f, target.Health - 2f); } catch { }
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
            // Blights are immune to spells of their own colour school
            if (damageSchool.HasValue && BlightSystem.IsBlight(target)
                && BlightSystem.GetBlightSchool(target) == damageSchool.Value) return;
            // Use direct health assignment to avoid RegisterBlow's hit pipeline:
            // RegisterBlow with AttackCollisionData=default causes native crashes because
            // the engine reads weapon/body-part data from fields we leave at zero (OwnerId=-1,
            // AffectorWeaponSlot=0, VictimBodyPart=0), leading to invalid native lookups.
            float newHealth = target.Health - damage;
            if (newHealth <= 0f)
            {
                if (!target.IsHero) KillAgent(target);
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
        // Combos: first 2 chars = form (UU=Blast, RL=Self, LR=Create, UL=Affect, LU=Invoke),
        //         last 3 chars = colour (RRR=Red, RLR=Orange, DLD=Yellow, LDL=Green, ULU=Blue, DDD=Purple)
        public static bool Execute(string combo)
        {
            switch (combo)
            {
                // BLAST (UU)
                case "UURRR": SpellBlastRed();    break;
                case "UURLR": SpellBlastOrange(); break;
                case "UUDLD": SpellBlastYellow(); break;
                case "UULDL": SpellBlastGreen();  break;
                case "UUULU": SpellBlastBlue();   break;
                case "UUDDD": SpellBlastPurple(); break;
                // SELF (RL)
                case "RLRRR": SpellSelfRed();     break;
                case "RLRLR": SpellSelfOrange();  break;
                case "RLDLD": SpellSelfYellow();  break;
                case "RLLDL": SpellSelfGreen();   break;
                case "RLULU": SpellSelfBlue();    break;
                case "RLDDD": SpellSelfPurple();  break;
                // CREATE (LR)
                case "LRRRR": SpellCreateRed();    break;
                case "LRRLR": SpellCreateOrange(); break;
                case "LRDLD": SpellCreateYellow(); break;
                case "LRLDL": SpellCreateGreen();  break;
                case "LRULU": SpellCreateBlue();   break;
                case "LRDDD": SpellCreatePurple(); break;
                // AFFECT (UL)
                case "ULRRR": SpellAffectRed();    break;
                case "ULRLR": SpellAffectOrange(); break;
                case "ULDLD": SpellAffectYellow(); break;
                case "ULLDL": SpellAffectGreen();  break;
                case "ULULU": SpellAffectBlue();   break;
                case "ULDDD": SpellAffectPurple(); break;
                // INVOKE (LU)
                case "LURRR": SpellInvokeRed();    break;
                case "LURLR": SpellInvokeOrange(); break;
                case "LUDLD": SpellInvokeYellow(); break;
                case "LULDL": SpellInvokeGreen();  break;
                case "LUULU": SpellInvokeBlue();   break;
                case "LUDDD": SpellInvokePurple(); break;
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
                            try { f.SetMovementOrder(MovementOrder.MovementOrderCharge); } catch { }
                            affected++; break;
                        case BattleCommandKind.Dismount:
                            if (f.HasAnyMountedUnit) { f.SetRidingOrder(RidingOrder.RidingOrderDismount); affected++; } break;
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
                        try { a.SetActionChannel(1, _castAnimClearCache, true, 0UL); } catch { }
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
