// =============================================================================
// COLOURS OF CALRADIA — SpellEffects.cs
// Mount & Blade II: Bannerlord Mod  v2.0
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
                if (hour < 5f  || hour >= 22f) return LightLevel.Dark; // 22:00–05:00 deep night
                if (hour < 7f  || hour >= 19f) return LightLevel.Dim;  // dawn / dusk
                return LightLevel.Bright;                               // 07:00–19:00 full day
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
        private static bool  _shadowVeilActive     = false;
        private static Agent _hollowGazeTarget     = null;
        private static float _hollowGazeTimer      = 0f;
        private const  float HollowGazeInterval    = 0.3f;

        // Returns true when the agent is the player and Cerulean Mirror is blocking magic
        public static bool ProtectedByMirror(Agent a) => a == Player && _ceruleanMirrorActive;

        public static void TickHollowGaze(float dt)
        {
            if (_hollowGazeTarget == null) return;
            if (!_hollowGazeTarget.IsActive()) { _hollowGazeTarget = null; return; }
            _hollowGazeTimer -= dt;
            if (_hollowGazeTimer > 0f) return;
            _hollowGazeTimer = HollowGazeInterval;
            Vec3 pos = _hollowGazeTarget.Position;
            try { _hollowGazeTarget.TeleportToPosition(pos); } catch { }
            try { _hollowGazeTarget.SetMorale(0f); } catch { }
        }

        // ── Orange: confusion stagger ─────────────────────────────────────────
        private static float _confusionRemaining  = 0f;
        private static float _confusionTickTimer  = 0f;
        private const  float ConfusionDuration    = 1.5f;
        private const  float ConfusionTickRate    = 0.35f;
        private const  float ConfusionStaggerDist = 1.5f;

        public static void TriggerConfusion()
        {
            _confusionRemaining = ConfusionDuration;
            _confusionTickTimer = 0f;
            Msg("Generous Hunger seizes you — your body lurches as the magic tears through.", ColorSchool.Orange);
        }

        public static void TickHUDConfusion(float dt)
        {
            if (_confusionRemaining <= 0f) return;
            _confusionRemaining -= dt;
            _confusionTickTimer -= dt;
            if (_confusionTickTimer > 0f) return;
            _confusionTickTimer = ConfusionTickRate;
            if (Player == null || !Player.IsActive()) return;
            float angle = (float)(_rng.NextDouble() * Math.PI * 2);
            Vec3 dest = Player.Position + new Vec3(
                (float)Math.Cos(angle) * ConfusionStaggerDist,
                (float)Math.Sin(angle) * ConfusionStaggerDist,
                0f);
            dest.z = Player.Position.z;
            try { Player.TeleportToPosition(dest); } catch { }
        }

        // ── Blue: accumulating weight stacks ─────────────────────────────────
        private static int   _blueWeightStacks = 0;
        private const  float BlueBaseSpeed     = 6.5f;
        private const  float BlueSpeedPenalty  = 0.4f;
        private const  int   BlueMaxStacks     = 6;

        public static void ApplyBlueWeight()
        {
            if (Player == null || !Player.IsActive()) return;
            _blueWeightStacks = Math.Min(_blueWeightStacks + 1, BlueMaxStacks);
            EnforceBlueWeightCap();
            InformationManager.DisplayMessage(new InformationMessage(
                $"Scholar's Weight: The knowledge settles in your limbs. [{_blueWeightStacks}/{BlueMaxStacks}]",
                ColorSchoolData.GetMessageColor(ColorSchool.Blue)));
        }

        private static void EnforceBlueWeightCap()
        {
            if (Player == null || !Player.IsActive() || _blueWeightStacks <= 0) return;
            float cap = Math.Max(1.5f, BlueBaseSpeed - _blueWeightStacks * BlueSpeedPenalty);
            try { Player.SetMaximumSpeedLimit(cap, false); } catch { }
        }

        public static void TickBlueWeight(float dt)
        {
            if (_blueWeightStacks > 0) EnforceBlueWeightCap();
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

        public static void ClearSelfEffects()
        {
            if (_scarletWardActive)   { _scarletWardActive = false; }
            if (_ceruleanMirrorActive) { _ceruleanMirrorActive = false; }
            _shadowVeilActive  = false;
            _hollowGazeTarget  = null;
            _confusionRemaining = 0f;
            if (_blueWeightStacks > 0)
            {
                _blueWeightStacks = 0;
                if (Player?.IsActive() == true)
                    try { Player.SetMaximumSpeedLimit(10f, false); } catch { }
            }
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

        public static void DamageAgent(Agent target, float damage)
        {
            if (target == null || !target.IsActive()) return;
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
        // Combos: first 2 chars = form (UU=Blast, RL=Self, LR=Create),
        //         last 2 chars = colour (RR=Red, RU=Orange, LU=Yellow, LL=Green, UL=Blue, UR=Purple)
        public static bool Execute(string combo)
        {
            switch (combo)
            {
                // BLAST (UU)
                case "UURR": SpellBlastRed();    break;
                case "UURU": SpellBlastOrange(); break;
                case "UULU": SpellBlastYellow(); break;
                case "UULL": SpellBlastGreen();  break;
                case "UUUL": SpellBlastBlue();   break;
                case "UUUR": SpellBlastPurple(); break;
                // SELF (RL)
                case "RLRR": SpellSelfRed();     break;
                case "RLRU": SpellSelfOrange();  break;
                case "RLLU": SpellSelfYellow();  break;
                case "RLLL": SpellSelfGreen();   break;
                case "RLUL": SpellSelfBlue();    break;
                case "RLUR": SpellSelfPurple();  break;
                // CREATE (LR)
                case "LRRR": SpellCreateRed();    break;
                case "LRRU": SpellCreateOrange(); break;
                case "LRLU": SpellCreateYellow(); break;
                case "LRLL": SpellCreateGreen();  break;
                case "LRUL": SpellCreateBlue();   break;
                case "LRUR": SpellCreatePurple(); break;
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
