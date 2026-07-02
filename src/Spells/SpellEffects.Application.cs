// =============================================================================
// ASH AND EMBER — SpellEffects.Application.cs
// Applying a cast to an agent: innate natures and enchantments.
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
        // ── Apply effect to one agent ─────────────────────────────────────────
        internal static void ApplyEffectsToAgent(Agent target, SpellCast cast, Agent caster)
        {
            if (target == null || !target.IsActive()) return;
            if (IsWarded(target)) return;

            bool isEnemy = caster?.Team != null && target.Team != null && caster.Team != target.Team;
            bool isAlly  = caster?.Team != null && target.Team != null && caster.Team == target.Team;

            ColorSchool glowColor = cast.VisualColor;
            BeginAgentGlowRaw(target, ColorSchoolData.GetGlowColor(glowColor), 2f);

            // Damage — fire hits everyone (friendly fire).
            // Player casts (HasSplitDamage) use per-nature base damage values.
            // NPC casts (DamageCount set directly, no split) use the flat 25/input.
            if (cast.DamageCount > 0)
            {
                if (cast.HasSplitDamage)
                {
                    if (cast.SearCount  > 0) DamageAgent(target, cast.SearCount  * 35f, owner: caster);
                    if (cast.ForceCount > 0) DamageAgent(target, cast.ForceCount * 22f, owner: caster);
                    if (cast.ShredCount > 0) DamageAgent(target, cast.ShredCount * 22f, owner: caster);
                    ApplyInnateDamageNatures(target, cast, caster);
                }
                else
                {
                    DamageAgent(target, cast.DamageCount * 35f, owner: caster);
                }
                ApplyDamageEnchantments(target, cast, caster);
            }

            // Restore — fire heals allies
            if (cast.RestoreCount > 0 && isAlly)
            {
                HealAgent(target, cast.RestoreCount * 15f);
                ApplyInnateRestoreNature(target, cast, caster);
                ApplyRestoreEnchantments(target, cast, caster);
            }
        }

        // ── Innate damage natures ──────────────────────────────────────────────
        // Each damage key carries a distinct built-in side effect (beyond base damage).
        // The matching enchantment supersedes the innate version — no double-dip.
        //   Sear (U)  → 1 m push per input          (Immolate replaces with DoT + kill)
        //   Force (L) → 5% vulnerability per input   (Scatter replaces with big push + slow)
        //   Shred (R) → 12 morale drain per input    (Sunder replaces with armour shred)
        private static void ApplyInnateDamageNatures(Agent target, SpellCast cast, Agent caster)
        {
            // Sear: push enemies back — suppressed when Immolate is active (DoT/kill supersedes).
            if (cast.SearCount > 0 && !CasterHasEnchantment(caster, TalentId.Immolate))
            {
                bool isMounted = false;
                try { isMounted = target.MountAgent != null; } catch { }
                if (!isMounted && !target.IsHero)
                {
                    try
                    {
                        float dist  = cast.SearCount * 1f;
                        Vec3 origin = caster?.Position ?? target.Position;
                        Vec3 dir    = (target.Position - origin);
                        if (dir.Length < 0.01f) dir = new Vec3(1f, 0f, 0f);
                        dir = new Vec3(dir.x, dir.y, 0f).NormalizedCopy();
                        Vec3 dest = target.Position + dir * dist;
                        dest.z = target.Position.z;
                        QueueMove(target, dest, 0.3f);
                    }
                    catch { }
                }
            }

            // Force: minor vulnerability — suppressed when Scatter is active (kinetic push supersedes).
            if (cast.ForceCount > 0 && !CasterHasEnchantment(caster, TalentId.Scatter))
            {
                try
                {
                    float vuln     = Math.Min(25f, cast.ForceCount * 5f);
                    float duration = 6f;
                    if (!_sunderedAgents.TryGetValue(target, out var existing))
                        _sunderedAgents[target] = (vuln, duration);
                    else
                        _sunderedAgents[target] = (Math.Max(existing.BonusVuln, vuln), Math.Max(existing.Remaining, duration));
                }
                catch { }
            }

            // Shred: morale drain + chance to bewilder — suppressed when Sunder is active (armour shred supersedes).
            if (cast.ShredCount > 0 && !CasterHasEnchantment(caster, TalentId.Sunder))
            {
                try
                {
                    float delta = cast.ShredCount * 12f;
                    float cur   = target.GetMorale();
                    target.SetMorale(Math.Max(cur - delta, 0f));
                }
                catch { }
                if (!target.IsHero && _rng.NextDouble() < 0.40)
                {
                    try
                    {
                        target.SetMorale(0f);
                        BeginAgentGlow(target, ColorSchool.Red, 1f);
                    }
                    catch { }
                }
            }
        }

        // Restore carries a built-in morale lift; the Hearthlight talent supersedes it.
        private static void ApplyInnateRestoreNature(Agent target, SpellCast cast, Agent caster)
        {
            if (cast.RestoreCount <= 0 || CasterHasEnchantment(caster, TalentId.Hearthlight)) return;
            try
            {
                float cur = target.GetMorale();
                target.SetMorale(Math.Min(cur + cast.RestoreCount * 6f, 100f));
            }
            catch { }
        }

        // ── Enchantment application ────────────────────────────────────────────

        private static void ApplyDamageEnchantments(Agent target, SpellCast cast, Agent caster)
        {
            // Scatter: push enemies back + sear limbs to slow movement (merged Char).
            // Triggered by Force (L) inputs; unsplit NPC casts use the full DamageCount.
            if (cast.EffForce > 0 && CasterHasEnchantment(caster, TalentId.Scatter))
            {
                bool isMounted = false;
                try { isMounted = target.MountAgent != null; } catch { }
                if (!isMounted)
                {
                    float dist = cast.EffForce * 5f;  // 5m per input (was 4m)
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
                        float reducedSpeed = Math.Max(1f, 10f - cast.EffForce * 2.5f);
                        float duration = 4f + cast.EffForce * 1.5f;  // was 1f per input
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

            // Immolate: survivors of a Sear hit burn (DoT). Runs before the kill check so it applies regardless of outcome.
            if (cast.EffSear > 0 && CasterHasEnchantment(caster, TalentId.Immolate))
            {
                try
                {
                    float dps = cast.EffSear * 2f;
                    if (!_scorchAgents.TryGetValue(target, out var cur))
                        _scorchAgents[target] = (dps, 3f);
                    else
                        _scorchAgents[target] = (Math.Max(cur.Dps, dps), 3f);
                    BeginAgentGlow(target, ColorSchool.Red, 1.5f);
                }
                catch { }
            }

            // Smoulder: Sear inputs seal the morale brand — enemies cannot recover morale for 30 seconds.
            if (cast.EffSear > 0 && CasterHasEnchantment(caster, TalentId.Smoulder))
            {
                try
                {
                    float morale = target.GetMorale();
                    if (!_ashmarkedAgents.TryGetValue(target, out var cur) || cur.Remaining <= 0f)
                        _ashmarkedAgents[target] = (morale, 30f);
                    else
                        _ashmarkedAgents[target] = (Math.Min(cur.LockedMorale, morale), 30f);
                    BeginAgentGlow(target, ColorSchool.Red, 2f);
                }
                catch { }
            }

            // Smoulder: morale penalty + bewildering random effect (merged Bewilder)
            if (CasterHasEnchantment(caster, TalentId.Smoulder))
            {
                try
                {
                    float delta = cast.DamageCount * 15f;  // was 12f
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
                                try
                                {
                                    target.SetMorale(target.GetMorale() * 0.5f);
                                    if (!_charredAgents.TryGetValue(target, out var curPanic))
                                        _charredAgents[target] = (0f, 2f);
                                    else
                                        _charredAgents[target] = (0f, Math.Max(curPanic.Remaining, 2f));
                                    target.SetMaximumSpeedLimit(0f, false);
                                }
                                catch { }
                                break;
                            case 2:
                                bool mounted = false;
                                try { mounted = target.MountAgent != null; } catch { }
                                if (mounted) ForceDismount(target, caster);
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
            // Triggered by Shred (R) inputs; unsplit NPC casts use the full DamageCount.
            if (cast.EffShred > 0 && CasterHasEnchantment(caster, TalentId.Sunder))
            {
                try
                {
                    float vuln = cast.EffShred * 10f; // raw value, capped to 50% in DamageAgent
                    float attackWeaken = Math.Min(0.50f, cast.EffShred * 0.10f);  // was 0.08f, cap 0.40f
                    float duration = 8f + cast.EffShred * 1.5f;  // was fixed 8f
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

            // Immolate: burn damage per input; kills scale with inputs.
            // Triggered by Sear (U) inputs; unsplit NPC casts use the full DamageCount.
            // ≥3 inputs: one kill slot per 3 Sear. The FIRST slot of a cast is
            // certain; each further slot connects at 50%. Unbounded guaranteed
            // kills (3 per cast at 9 Sear, every cast) deleted units with no
            // counterplay — for the player and for Ashen/False Emperor AI alike.
            // 2 inputs: 50% chance to kill. 1 input: 33% chance to kill.
            if (cast.EffSear > 0 && CasterHasEnchantment(caster, TalentId.Immolate))
            {
                try
                {
                    if (_immolateKillsRemaining < 0)
                    {
                        _immolateKillsRemaining = cast.EffSear / 3;
                        _immolateGuaranteedSpent = false;
                    }

                    bool doKill = false;
                    if (cast.EffSear >= 3)
                    {
                        if (_immolateKillsRemaining > 0)
                        {
                            _immolateKillsRemaining--;
                            doKill = !_immolateGuaranteedSpent || _rng.NextDouble() < 0.50;
                            _immolateGuaranteedSpent = true;
                        }
                    }
                    else
                    {
                        doKill = (cast.EffSear == 2 && _rng.NextDouble() < 0.50)
                              || (cast.EffSear == 1 && _rng.NextDouble() < 0.33);
                    }

                    if (doKill)
                    {
                        QueueKill(target, caster);
                        BeginAgentGlow(target, ColorSchool.Red, 2f);
                        InformationManager.DisplayMessage(new InformationMessage(
                            "Immolate — consumed.", new Color(1f, 0.4f, 0.1f)));

                        // Immolate: fire leaps to nearby enemies on kill.
                        if (CasterHasEnchantment(caster, TalentId.Immolate) && Mission.Current != null)
                        {
                            Vec3 killPos  = target.Position;
                            float chainDmg = cast.EffSear * 25f * 0.30f;
                            bool anyChain = false;
                            try
                            {
                                foreach (Agent a in Mission.Current.Agents.ToList())
                                {
                                    if (!a.IsActive() || a.IsMount || a == target || IsWarded(a)) continue;
                                    if (caster?.Team == null || a.Team == null || a.Team == caster.Team) continue;
                                    float dist = new Vec3(a.Position.x - killPos.x, a.Position.y - killPos.y, 0f).Length;
                                    if (dist > 3f) continue;
                                    DamageAgent(a, chainDmg, owner: caster);
                                    BeginAgentGlow(a, ColorSchool.Red, 1f);
                                    anyChain = true;
                                }
                            }
                            catch { }
                            if (anyChain)
                                InformationManager.DisplayMessage(new InformationMessage(
                                    "Chain Ignite — the fire spreads.", new Color(1f, 0.55f, 0.1f)));
                        }
                    }
                    else
                    {
                        DamageAgent(target, cast.EffSear * 17f, owner: caster);
                        BeginAgentGlow(target, ColorSchool.Red, 1.5f);
                    }
                }
                catch { }
            }
        }

        // Unlike damage enchantments — which are split across the Sear/Force/
        // Shred natures so a cast feeds only the inputs it carries — every
        // Restore enchantment the caster owns fires together on one Restore
        // cast. Each is therefore tuned weaker than its damage counterpart:
        // the real payload of a full restore build is the stack.
        private static void ApplyRestoreEnchantments(Agent target, SpellCast cast, Agent caster)
        {
            // Ashveil: brief magic immunity
            if (CasterHasEnchantment(caster, TalentId.Ashveil))
            {
                float duration = Math.Min(10f, cast.RestoreCount * 2f);  // was 4f per input, uncapped
                float current  = _wardedAgents.TryGetValue(target, out float t) ? t : 0f;
                _wardedAgents[target] = Math.Max(current, duration);
                BeginAgentGlow(target, ColorSchool.White, duration);
            }

            // Cinder Shell: armour boost + near-full-health shield (merged Overflow)
            if (CasterHasEnchantment(caster, TalentId.CinderShell))
            {
                float bonus = cast.RestoreCount * 6f;                 // was 10f per input
                float shellDuration = 4f + cast.RestoreCount * 1f;    // was 6f + 1.5f per input
                AddStoneskin(target, bonus, shellDuration);
                BeginAgentGlow(target, ColorSchool.Orange, 2f);
                try
                {
                    float hp = target.Health;
                    float hpMax = target.HealthLimit;
                    if (hpMax > 0f && hp >= hpMax * 0.90f)            // was 0.80f
                    {
                        float overBonus = cast.RestoreCount * 10f;    // was 15f
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
                    float delta = cast.RestoreCount * 10f;  // was 15f
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
                    float pct = Math.Min(0.25f, cast.RestoreCount * 0.05f);  // was 8% per input, cap 50%
                    float duration = 3f + (float)Math.Sqrt(cast.RestoreCount) * 4f;
                    if (!_reflectAgents.TryGetValue(target, out var cur))
                        _reflectAgents[target] = (pct, duration);
                    else
                        _reflectAgents[target] = (Math.Max(cur.ReflectPct, pct), Math.Max(cur.Remaining, duration));
                    BeginAgentGlow(target, ColorSchool.Orange, 2f);
                }
                catch { }
            }
        }

        // Applies an NPC colour lord's owned offensive enchantments (Immolate /
        // Sunder / Scatter / Smoulder) to the enemies his UNIFIED element attack just
        // struck. Post-v0.35 the player's element casts go straight through
        // ElementSpellEffects and intentionally carry no enchantments (that layer was
        // retired for the player). NPC lords, however, are still seeded with these
        // brands, and they are what make Ashen and the False Emperor lethal — so we
        // re-apply them here rather than silently dropping the behaviour when their
        // fire moved onto the unified path. `power` (~0.35..1.2) sets how hard the
        // brand bites: a full-power working brands like the old 5-input blast, a weak
        // flick like a 2-input one. Lords without the enchantment get nothing.
        internal static void ApplyNpcElementEnchantments(Agent caster,
            System.Collections.Generic.IList<Agent> enemiesHit, float power)
        {
            if (caster == null || caster == Agent.Main || enemiesHit == null || enemiesHit.Count == 0) return;
            Hero hero;
            try { hero = (caster.Character as TaleWorlds.CampaignSystem.CharacterObject)?.HeroObject; }
            catch { return; }
            if (hero == null || !ColourLordRegistry.IsColourLord(hero)) return;

            int intensity = (int)Math.Round(power * 5f, MidpointRounding.AwayFromZero);
            if (intensity < 2) intensity = 2; else if (intensity > 5) intensity = 5;

            var cast = new SpellCast { DamageCount = intensity, OverrideVisualColor = ResolveNpcSchool(caster) };
            ResetImmolateKill();  // one kill-slot budget per cast, before the per-target loop
            foreach (Agent a in enemiesHit)
                ApplyDamageEnchantments(a, cast, caster);
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

    }
}
