// =============================================================================
// ASH AND EMBER — SpellEffects.Enchantments.cs
// Stoneskin/Sunder/Char/Reflect/Immolate/Flashfire state and ticks.
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

        // ── Immolate (per-cast kill counter) ─────────────────────────────────────
        // Kill slots allowed = EffSear / 3 (3U=1, 6U=2, 9U=3). Only the first
        // slot of a cast is a certain kill; the rest connect at 50%.
        // -1 = sentinel meaning "not yet initialised for this cast".
        private static int  _immolateKillsRemaining  = -1;
        private static bool _immolateGuaranteedSpent = false;
        public static void ResetImmolateKill()
        {
            _immolateKillsRemaining  = -1;
            _immolateGuaranteedSpent = false;
        }

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

        // ── Scorch (Scorch enchantment — Sear DoT) ────────────────────────────────
        private static readonly Dictionary<Agent, (float Dps, float Remaining)>
            _scorchAgents = new Dictionary<Agent, (float, float)>();

        public static void TickScorch(float dt)
        {
            foreach (Agent key in _scorchAgents.Keys.ToList())
            {
                var (dps, remaining) = _scorchAgents[key];
                remaining -= dt;
                if (remaining <= 0f || key == null || !key.IsActive() || key.Health <= 0f)
                {
                    _scorchAgents.Remove(key);
                    continue;
                }
                try { DamageAgent(key, dps * dt); } catch { }
                _scorchAgents[key] = (dps, remaining);
            }
        }

        public static void ClearScorch() => _scorchAgents.Clear();

        // ── Ashmark (Ashmark enchantment — morale lock) ───────────────────────────
        // Stores the morale value at the moment of branding. Each tick clamps the
        // agent's morale to that ceiling so recovery buffs cannot exceed the brand.
        private static readonly Dictionary<Agent, (float LockedMorale, float Remaining)>
            _ashmarkedAgents = new Dictionary<Agent, (float, float)>();

        public static void TickAshmark(float dt)
        {
            foreach (Agent key in _ashmarkedAgents.Keys.ToList())
            {
                var (locked, remaining) = _ashmarkedAgents[key];
                remaining -= dt;
                if (remaining <= 0f || key == null || !key.IsActive() || key.Health <= 0f)
                {
                    _ashmarkedAgents.Remove(key);
                    continue;
                }
                try
                {
                    float cur = key.GetMorale();
                    if (cur > locked) key.SetMorale(locked);
                }
                catch { }
                _ashmarkedAgents[key] = (locked, remaining);
            }
        }

        public static void ClearAshmark() => _ashmarkedAgents.Clear();

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

    }
}
