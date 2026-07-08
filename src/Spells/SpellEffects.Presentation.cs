// =============================================================================
// ASH AND EMBER — SpellEffects.Presentation.cs
// Sound, cast animation, militia, and NPC area-effect spawners.
// Partial of SpellEffects (shared state lives in SpellEffects.cs).
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

namespace AshAndEmber
{
    public static partial class SpellEffects
    {
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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                        bool mounted    = false; try { mounted    = a.MountAgent != null;   } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        bool usingEquip = false; try { usingEquip = a.IsUsingGameObject;    } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        if (!mounted && !usingEquip)
                            try { a.SetActionChannel(0, _castAnimClearCache, true, 0UL); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                    bool mounted    = false; try { mounted    = a.MountAgent != null;  } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    bool usingEquip = false; try { usingEquip = a.IsUsingGameObject;   } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    if (!mounted && !usingEquip)
                        try { a.SetActionChannel(0, _castAnimCache, true, 0UL); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                    try { action(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    _pendingNpcCasts.RemoveAt(i);
                }
                else _pendingNpcCasts[i] = (a, newT, action);
            }
        }

        public static void BeginCastLoop(Agent agent)
        {
            if (agent == null || !agent.IsActive() || agent.Health <= 0f) return;
            try { if (agent.MountAgent != null) return; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { if (agent.IsUsingGameObject) return; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            // Cancel any pending clear and remove stale loop entry for this agent
            int ci = _animClearTimers.FindIndex(x => x.agent == agent);
            if (ci >= 0) _animClearTimers.RemoveAt(ci);
            int li = _castLoops.FindIndex(x => x.agent == agent);
            if (li >= 0) _castLoops.RemoveAt(li);
            try { agent.SetActionChannel(0, _castAnimCache, true, 0UL); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
            => QueueNpcCastWithWindup(agent, NpcCastFocusSchool(agent), castAction);

        public static void QueueNpcCastWithWindup(Agent agent, ColorSchool focusSchool, Action castAction)
        {
            BeginCastLoop(agent);
            // Show the caster's focus aura during the wind-up — the NPC analogue of
            // the player's held-focus glow.
            FlashFocusAura(agent, focusSchool);
            _pendingNpcCasts.RemoveAll(x => x.agent == agent);
            _pendingNpcCasts.Add((agent, NpcCastWindup, castAction));
        }

        // Infers the focus-aura colour for an NPC spell caster from its identity:
        // any Ashen unit (lord, thrall, or Ashen-kingdom fighter) burns cold, colour
        // lords burn violet, and other bandits/troops who borrow the fire show plain red.
        private static ColorSchool NpcCastFocusSchool(Agent agent)
        {
            try
            {
                // The cold takes precedence — an Ashen thrall's wind-up aura must match
                // the ashfire it is about to loose, not the plain-red troop default.
                if (AshenVisuals.ShouldLookAshen(agent)) return ColorSchool.Ashen;
                Hero h = (agent?.Character as CharacterObject)?.HeroObject;
                if (h != null)
                    return ColourLordRegistry.IsAshenLord(h) ? ColorSchool.Ashen : ColorSchool.Purple;
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            return ColorSchool.Red;
        }

        public static void ClearAnimTimers()
        {
            foreach (var (agent, _) in _animClearTimers)
                if (agent != null && agent.IsActive() && agent.Health > 0f)
                    try { agent.SetActionChannel(0, _castAnimClearCache, true, 0UL); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
            try { if (agent.MountAgent != null) return; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { if (agent.IsUsingGameObject) return; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            // Stop any ongoing loop so the final cast pose plays cleanly
            EndCastLoop(agent);
            try
            {
                agent.SetActionChannel(0, _castAnimCache, true, 0UL);
                int idx = _animClearTimers.FindIndex(x => x.agent == agent);
                if (idx >= 0) _animClearTimers.RemoveAt(idx);
                _animClearTimers.Add((agent, 0.8f));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
