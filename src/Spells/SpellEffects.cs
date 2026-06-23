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

        // Returns true only when both hands are empty (nothing wielded).
        public static bool HasFreeHand(Agent agent)
        {
            try
            {
                return agent.WieldedWeapon.IsEmpty && agent.WieldedOffhandWeapon.IsEmpty;
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

                if (!agent.WieldedOffhandWeapon.IsEmpty)
                {
                    // Sheathe the off-hand item (typically a shield)
                    agent.TryToSheathWeaponInHand(Agent.HandIndex.OffHand, Agent.WeaponWieldActionType.WithAnimation);
                }
                else if (!agent.WieldedWeapon.IsEmpty)
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

        public static void ExecuteNpcBarrier(Agent caster, int nodeCount,
            int damageCount, int restoreCount, Team casterTeam)
        {
            if (caster == null || !caster.IsActive()) return;
            var cast = new SpellCast
            {
                Form = SpellForm.Barrier, FormCount = nodeCount, BarrierCount = nodeCount,
                DamageCount = damageCount, RestoreCount = restoreCount,
                OverrideVisualColor = ResolveNpcSchool(caster)
            };
            Vec3 fwd   = caster.LookDirection.NormalizedCopy();
            Vec3 right = new Vec3(-fwd.y, fwd.x, 0f).NormalizedCopy();
            for (int i = 0; i < nodeCount; i++)
            {
                float offset = (i - (nodeCount - 1) * 0.5f) * 1.5f;
                Vec3 pos = caster.Position + fwd * 3f + right * offset;
                AddBarrierNode(pos, cast, casterTeam);
            }
            try { TryCastSound(caster.Position, cast.VisualColor); } catch { }
            try { TryCastAnimation(caster); } catch { }
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
    }
}
