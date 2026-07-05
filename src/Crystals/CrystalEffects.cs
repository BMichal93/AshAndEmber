// =============================================================================
// ASH AND EMBER — Crystals/CrystalEffects.cs
//
// Battle crystal activation pipeline:
//   Player attacks with an equipped crystal → the attack INPUT fires the
//   crystal's effect INSTANTLY (no charge), like loosing a weapon. Every use
//   blasts its light whether or not anything stands in range.
//
// NPCs fire directly through CrystalBattleAI.ExecuteEffect().
// Burndown chance per use; when it triggers the crystal is DESTROYED — struck
// from the hand and the loadout — so a spent crystal cannot be used again.
//
// All TaleWorlds access is null-guarded and wrapped in individual try/catch.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static class CrystalEffects
    {
        private static readonly Random _rng = new Random();

        // Edge-detect the attack BUTTON itself — the reliable trigger. The
        // swing-time signal and landed-blow paths do not fire dependably for the
        // crystal's weapon form, so a raw attack-input press is what wakes it.
        private static bool _prevAttackDown = false;

        // ── Active slows (keyed by agent, value = seconds left) ──────────────
        private static readonly Dictionary<Agent, float> _rimeSlow = new Dictionary<Agent, float>();
        private static readonly Dictionary<Agent, float> _veilSlow = new Dictionary<Agent, float>();
        private static readonly Dictionary<Agent, float> _duskSlow = new Dictionary<Agent, float>();

        // ── Crystal missile state ──────────────────────────────────────────────
        private class CrystalMissileState
        {
            public Vec3        Position;
            public Vec3        Forward;
            public float       TravelLeft;
            public float       ExplosionRadius;
            public CrystalType Type;
            public Agent       Caster;
            public Team        CasterTeam;
            public float       TrailTimer = 0f;
            public const float Speed         = 28f;
            public const float TrailInterval = 0.05f;
            public const float DetectRadius  = 1.5f;
        }

        private static readonly List<CrystalMissileState> _crystalMissiles = new List<CrystalMissileState>();

        // ── State management ──────────────────────────────────────────────────

        public static void ClearBattleState()
        {
            _rimeSlow.Clear();
            _veilSlow.Clear();
            _duskSlow.Clear();
            _prevAttackDown = false;
            _crystalMissiles.Clear();
        }

        // ── Player: crystal weapon hit intercept ──────────────────────────────
        // Called from MagicMissionBehavior.OnAgentHit.
        public static void OnCrystalHit(Agent victim, Agent attacker,
            MissionWeapon weapon, int inflictedDamage)
        {
            if (attacker == null || attacker != Agent.Main) return; // player swings only

            string itemId = null;
            try { itemId = weapon.Item?.StringId; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            if (!CrystalCatalog.IsCrystalItemId(itemId)) return;

            // A crystal deals no physical harm — restore whatever it inflicted.
            // (Activation is driven by the attack INPUT in MissionTick, not the hit,
            // so a crystal fires instantly on the swing whether or not it connects.)
            if (victim != null && victim.IsActive() && inflictedDamage > 0)
                try { SpellEffects.HealAgent(victim, inflictedDamage); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Looses the wielded crystal's light the instant the player strikes with it —
        // no charge, used like a weapon. Does nothing if the wielded item is not a
        // crystal, so ordinary attacks pass through untouched.
        private static void TryActivateCrystal(Agent main)
        {
            string itemId = null;
            try { itemId = main.WieldedWeapon.Item?.StringId; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            if (!CrystalCatalog.IsCrystalItemId(itemId)) return;
            if (!CrystalCatalog.TryGetByItemId(itemId, out var def)) return;
            FireEffect(main, def.Type);
        }

        // ── MissionTick: activation + buff timers ─────────────────────────────

        public static void MissionTick(float dt)
        {
            if (Mission.Current == null) return;

            TickCrystalMissile(dt);

            // Activation: attacking with a crystal in hand looses it INSTANTLY.
            try
            {
                var main = Agent.Main;
                if (main != null && main.IsActive())
                {
                    // The raw attack BUTTON (LMB / right trigger), edge-detected so
                    // one press = one use. Suppressed while focusing an element spell
                    // (Alt / LB held), where that same button releases a cone instead.
                    bool focusing = Input.IsKeyDown(InputKey.LeftAlt)
                                 || Input.IsKeyDown(InputKey.ControllerLBumper);
                    bool attackDown = Input.IsKeyDown(InputKey.LeftMouseButton)
                                   || Input.IsKeyDown(InputKey.ControllerRTrigger);
                    if (attackDown && !_prevAttackDown && !focusing)
                        TryActivateCrystal(main);
                    _prevAttackDown = attackDown;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Expire Rimeshard slow.
            foreach (var kvp in _rimeSlow.ToList())
            {
                float left = kvp.Value - dt;
                if (left <= 0f || !kvp.Key.IsActive())
                {
                    _rimeSlow.Remove(kvp.Key);
                    if (kvp.Key.IsActive())
                        try { kvp.Key.SetMaximumSpeedLimit(1f, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
                else _rimeSlow[kvp.Key] = left;
            }

            // Expire Veilstone slow.
            foreach (var kvp in _veilSlow.ToList())
            {
                float left = kvp.Value - dt;
                if (left <= 0f || !kvp.Key.IsActive())
                {
                    _veilSlow.Remove(kvp.Key);
                    if (kvp.Key.IsActive())
                        try { kvp.Key.SetMaximumSpeedLimit(1f, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
                else _veilSlow[kvp.Key] = left;
            }

            // Expire Duskstone slow.
            foreach (var kvp in _duskSlow.ToList())
            {
                float left = kvp.Value - dt;
                if (left <= 0f || !kvp.Key.IsActive())
                {
                    _duskSlow.Remove(kvp.Key);
                    if (kvp.Key.IsActive())
                        try { kvp.Key.SetMaximumSpeedLimit(1f, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
                else _duskSlow[kvp.Key] = left;
            }
        }

        // ── Effect dispatch ────────────────────────────────────────────────────

        // Fired instantly on use (player) or directly by CrystalBattleAI (NPC).
        public static void FireEffect(Agent caster, CrystalType type)
        {
            if (caster == null || !caster.IsActive() || Mission.Current == null) return;

            bool solarFlare = TalentSystem.Has(TalentId.SolarFlare);

            // A strong, unmistakable pulse of the crystal's stored light at the
            // caster — EVERY use, whether or not anything stands in range. The
            // crystal works like any other magic: it ALWAYS looses its light (a
            // harmless bloom when no foe is near) and reports the cast, so a use is
            // never a silent no-op even swinging into empty air.
            var def = CrystalCatalog.Get(type);
            try
            {
                Vec3 at     = caster.Position + new Vec3(0f, 0f, 1f);
                Vec3 ground = caster.Position + new Vec3(0f, 0f, 0.1f);
                SpellEffects.SpawnImpactBurst(at, def.GlowColor, 2.6f);
                SpellEffects.SpawnImpactBurst(ground, def.GlowColor, 3.2f);   // a bloom on the ground around the bearer
                SpellEffects.SpawnTempLight(at, def.GlowColor, 11f, 1.2f);
                SpellEffects.TryCastSound(caster.Position, def.GlowColor);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            switch (type)
            {
                case CrystalType.Sunstone:     EffectSunstone(caster);               break;
                case CrystalType.Embershard:   EffectEmbershard(caster, solarFlare);  break;
                case CrystalType.Rimeshard:    EffectRimeshard(caster, solarFlare);   break;
                case CrystalType.Veilstone:    EffectVeilstone(caster, solarFlare);   break;
                case CrystalType.Stormcrystal: EffectStormcrystal(caster, solarFlare);break;
                case CrystalType.Duskstone:    EffectDuskstone(caster, solarFlare);   break;
            }

            // Player-only lapidary talents: Mending Light heals the bearer on every
            // use; the burndown roll may destroy the spent crystal.
            if (caster == Agent.Main)
            {
                float mend = CrystalTalents.MendOnUse;
                if (mend > 0f) try { SpellEffects.HealAgent(caster, mend); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                TryBurndown(caster, type);
            }
        }

        // ── Sunstone ─────────────────────────────────────────────────────────

        private static void EffectSunstone(Agent caster)
        {
            float pot = Potency(caster);   // Brilliant Lattice: harder heals (player)
            try { SpellEffects.HealAgent(caster, CrystalMath.SunSelfHeal * pot); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Yellow, 2f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            Vec3 pos;
            try { pos = caster.Position; } catch { return; }
            float r2 = CrystalMath.SunRadius * CrystalMath.SunRadius;
            int   mended = 0;
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (a == caster || !a.IsActive() || a.IsMount) continue;
                    if (caster.Team == null || a.Team != caster.Team) continue;
                    float dx = a.Position.x - pos.x, dy = a.Position.y - pos.y;
                    if (dx * dx + dy * dy > r2) continue;
                    try { SpellEffects.HealAgent(a, CrystalMath.SunAllyHeal * pot); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    mended++;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            Announce(caster, mended > 0
                ? $"Sunstone — warmth pulse (+{(int)CrystalMath.SunSelfHeal} HP self, +{(int)CrystalMath.SunAllyHeal} HP to {mended} allies)."
                : $"Sunstone — warmth pulse (+{(int)CrystalMath.SunSelfHeal} HP, no allies nearby).",
                ColorSchool.Yellow);
        }

        // ── Embershard ────────────────────────────────────────────────────────

        private static void EffectEmbershard(Agent caster, bool solarFlare)
        {
            if (caster == null || !caster.IsActive()) return;

            Vec3 fwd = caster.LookDirection.NormalizedCopy();
            Vec3 startPos = caster.Position + fwd * 1.5f + new Vec3(0f, 0f, 1.2f);

            float range = solarFlare
                ? CrystalMath.SolarFlareRadius(CrystalMath.EmberRadius) * 3f
                : CrystalMath.EmberRadius * 3f;
            float explRadius = solarFlare
                ? CrystalMath.SolarFlareRadius(CrystalMath.EmberRadius)
                : CrystalMath.EmberRadius;

            var missile = new CrystalMissileState
            {
                Position = startPos,
                Forward = fwd,
                TravelLeft = range,
                ExplosionRadius = explRadius,
                Type = CrystalType.Embershard,
                Caster = caster,
                CasterTeam = caster.Team,
            };
            _crystalMissiles.Add(missile);

            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Red, 1.5f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { SpellEffects.SpawnTempLight(startPos, ColorSchool.Red, 6f, 10f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            Announce(caster,
                $"Embershard — burning shards launch ({range:F0}m, {explRadius:F0}m blast).",
                ColorSchool.Red);
        }

        // ── Rimeshard ─────────────────────────────────────────────────────────

        private static void EffectRimeshard(Agent caster, bool solarFlare)
        {
            Vec3 pos;
            try { pos = caster.Position; } catch { return; }
            float r = solarFlare ? CrystalMath.SolarFlareRadius(CrystalMath.RimeRadius) : CrystalMath.RimeRadius;
            float r2 = r * r;
            int slowed = 0;
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount || a == caster) continue;
                    if (caster.Team != null && a.Team == caster.Team) continue;
                    float dx = a.Position.x - pos.x, dy = a.Position.y - pos.y;
                    if (dx * dx + dy * dy > r2) continue;
                    // Walls of wind and stone stop the crystal's reach.
                    try { if (ElementWallWards.BlocksCrystal(pos, a.Position)) continue; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    _rimeSlow[a] = CrystalMath.RimeDurationSec;
                    try { a.SetMaximumSpeedLimit(CrystalMath.RimeSlowMult, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    slowed++;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Blue, 1.5f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            int rimePct = (int)((1f - CrystalMath.RimeSlowMult) * 100f);
            Announce(caster, slowed > 0
                ? $"Rimeshard — frost pulse ({slowed} enemies stilled, {rimePct} % slow for {(int)CrystalMath.RimeDurationSec} s)."
                : "Rimeshard — frost pulse (no enemies in range).",
                ColorSchool.Blue);
        }

        // ── Veilstone ─────────────────────────────────────────────────────────

        private static void EffectVeilstone(Agent caster, bool solarFlare)
        {
            Vec3 pos;
            try { pos = caster.Position; } catch { return; }
            float r  = solarFlare ? CrystalMath.SolarFlareRadius(CrystalMath.VeilRange) : CrystalMath.VeilRange;
            float r2 = r * r;

            // Collect all enemies in range, pick one at random.
            var candidates = new System.Collections.Generic.List<Agent>();
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount || a == caster) continue;
                    if (caster.Team != null && a.Team == caster.Team) continue;
                    float dx = a.Position.x - pos.x, dy = a.Position.y - pos.y;
                    if (dx * dx + dy * dy > r2) continue;
                    // The veil's grasp is shard-force too — walls of wind/stone bar it.
                    try { if (ElementWallWards.BlocksCrystal(pos, a.Position)) continue; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    candidates.Add(a);
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Purple, 1.5f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            if (candidates.Count == 0)
            {
                Announce(caster, "Veilstone — the veil finds nothing to grasp.", ColorSchool.Purple);
                return;
            }

            var target = candidates[_rng.Next(candidates.Count)];
            try { SpellEffects.DamageAgent(target, CrystalMath.VeilDamage * Potency(caster), ColorSchool.Purple, caster); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            _veilSlow[target] = CrystalMath.VeilDurationSec;
            try { target.SetMaximumSpeedLimit(CrystalMath.VeilSlowMult, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            int slowPct = (int)((1f - CrystalMath.VeilSlowMult) * 100f);
            Announce(caster,
                $"Veilstone — veil grasp ({target.Name} struck for {(int)CrystalMath.VeilDamage} HP, {slowPct} % slow for {(int)CrystalMath.VeilDurationSec} s).",
                ColorSchool.Purple);
        }

        // ── Stormcrystal ──────────────────────────────────────────────────────

        private static void EffectStormcrystal(Agent caster, bool solarFlare)
        {
            Vec3 pos;
            try { pos = caster.Position; } catch { return; }
            float r = solarFlare ? CrystalMath.SolarFlareRadius(CrystalMath.StormRadius) : CrystalMath.StormRadius;
            float r2 = r * r;
            int hit = 0;
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount || a == caster) continue;
                    if (caster.Team != null && a.Team == caster.Team) continue;
                    float dx = a.Position.x - pos.x, dy = a.Position.y - pos.y;
                    if (dx * dx + dy * dy > r2) continue;
                    // Walls of wind and stone stop the crystal's reach.
                    try { if (ElementWallWards.BlocksCrystal(pos, a.Position)) continue; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    try { SpellEffects.DamageAgent(a, CrystalMath.StormDamage * Potency(caster), ColorSchool.Orange, caster); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    try { a.ChangeMorale(-CrystalMath.StormMoraleDrain); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    hit++;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Orange, 1.5f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            Announce(caster, hit > 0
                ? $"Stormcrystal — thunder clap ({hit} enemies struck, {(int)CrystalMath.StormDamage} HP, −{(int)CrystalMath.StormMoraleDrain} morale)."
                : "Stormcrystal — thunder clap (no enemies in range).",
                ColorSchool.Orange);
        }

        // ── Duskstone ─────────────────────────────────────────────────────────

        private static void EffectDuskstone(Agent caster, bool solarFlare)
        {
            Vec3 pos;
            try { pos = caster.Position; } catch { return; }
            float r = solarFlare ? CrystalMath.SolarFlareRadius(CrystalMath.DuskRadius) : CrystalMath.DuskRadius;
            float r2 = r * r;
            int drained = 0;
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount || a == caster) continue;
                    if (caster.Team != null && a.Team == caster.Team) continue;
                    float dx = a.Position.x - pos.x, dy = a.Position.y - pos.y;
                    if (dx * dx + dy * dy > r2) continue;
                    try { a.ChangeMorale(-CrystalMath.DuskMoraleDrain); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    _duskSlow[a] = CrystalMath.DuskDurationSec;
                    try { a.SetMaximumSpeedLimit(CrystalMath.DuskSlowMult, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    drained++;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Ashen, 2f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            int duskSlowPct = (int)((1f - CrystalMath.DuskSlowMult) * 100f);
            Announce(caster, drained > 0
                ? $"Duskstone — despair wave ({drained} enemies: −{(int)CrystalMath.DuskMoraleDrain} morale, {duskSlowPct} % slow for {(int)CrystalMath.DuskDurationSec} s)."
                : "Duskstone — despair wave (no enemies in range).",
                ColorSchool.Ashen);
        }

        // ── Crystal missile tick ──────────────────────────────────────────────

        private static void TickCrystalMissile(float dt)
        {
            if (_crystalMissiles.Count == 0) return;

            for (int i = _crystalMissiles.Count - 1; i >= 0; i--)
            {
                var m = _crystalMissiles[i];
                float moved = CrystalMissileState.Speed * dt;
                m.Position += m.Forward * moved;
                m.TravelLeft -= moved;

                m.TrailTimer -= dt;
                if (m.TrailTimer <= 0f)
                {
                    m.TrailTimer = CrystalMissileState.TrailInterval;
                    try { SpellEffects.SpawnTempLight(m.Position, ColorSchool.Red, 3f, 0.5f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }

                Vec3 mpos = m.Position;
                bool exploded = false;

                // Elemental wall warding: a burning shard dies against walls of
                // wind and stone (detonating there), and is QUENCHED outright by
                // a wall of standing water — steam, no blast.
                try
                {
                    var ward = ElementWallWards.MissileWardAt(mpos, fireMissile: true);
                    if (ward != null)
                    {
                        if (WallWardMath.QuenchesFireMissile(ward.Value))
                            try { SpellEffects.SpawnNatureBurst(mpos, NatureElement.Water, 0.8f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        else
                            ExplodeCrystalMissile(m, mpos);
                        _crystalMissiles.RemoveAt(i);
                        continue;
                    }
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                // Check for enemy collision.
                try
                {
                    foreach (Agent a in Mission.Current.Agents)
                    {
                        if (!a.IsActive() || a.IsMount || a == m.Caster) continue;
                        if (m.CasterTeam != null && a.Team == m.CasterTeam) continue;
                        float dx = a.Position.x - mpos.x;
                        float dy = a.Position.y - mpos.y;
                        if (dx * dx + dy * dy > CrystalMissileState.DetectRadius * CrystalMissileState.DetectRadius) continue;
                        ExplodeCrystalMissile(m, mpos);
                        exploded = true;
                        break;
                    }
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                if (!exploded && m.TravelLeft <= 0f)
                {
                    ExplodeCrystalMissile(m, m.Position);
                    exploded = true;
                }

                if (exploded)
                    _crystalMissiles.RemoveAt(i);
            }
        }

        private static void ExplodeCrystalMissile(CrystalMissileState m, Vec3 pos)
        {
            if (m == null || Mission.Current == null) return;

            float radius = m.ExplosionRadius;
            int hit = 0;

            try
            {
                SpellEffects.SpawnExplosionEffect(pos, ColorSchool.Red, radius, 5f);
                SpellEffects.TryCastSound(pos, ColorSchool.Red);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // Burning shards take to timber — machines and gates in the blast char.
            try { SpellEffects.DamageBurnableStructures(pos, radius, CrystalMath.EmberDamage * 2f, m.Caster); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount) continue;
                    float dist = new Vec3(a.Position.x - pos.x, a.Position.y - pos.y, 0f).Length;
                    if (dist > radius) continue;
                    if (m.CasterTeam != null && a.Team == m.CasterTeam) continue;
                    try
                    {
                        // Blame the crystal's actual bearer, not the player — NPC
                        // crystal-bearers loose these missiles too.
                        SpellEffects.DamageAgent(a, CrystalMath.EmberDamage * Potency(m.Caster), ColorSchool.Red, m.Caster);
                        SpellEffects.SpawnImpactBurst(a.Position, ColorSchool.Red, 4f);
                        hit++;
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            Announce(m.Caster, hit > 0
                ? $"Embershard detonates — {hit} enemies scorched ({(int)CrystalMath.EmberDamage} HP each)."
                : "Embershard detonates — no enemies in range.",
                ColorSchool.Red);
        }

        // ── Burndown ──────────────────────────────────────────────────────────

        private static void TryBurndown(Agent caster, CrystalType type)
        {
            // Lasting Lattice makes the crystal far more likely to survive the draw.
            if (!(_rng.NextDouble() < CrystalMath.BurndownChance * CrystalTalents.ShatterMult)) return;

            var def = CrystalCatalog.Get(type);

            // DESTROY the spent crystal so it cannot be used again:
            //   • strike it from the caster's HAND this battle (the reported bug —
            //     a "spent" crystal was still wielded and kept firing), and
            //   • clear it from the persistent battle LOADOUT so it does not return
            //     next battle, and
            //   • drop one from the party stores (a spare, if any).
            try
            {
                EquipmentIndex slot = caster.GetPrimaryWieldedItemIndex();
                if (slot != EquipmentIndex.None)
                {
                    try { caster.RemoveEquippedWeapon(slot); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    try { Hero.MainHero.BattleEquipment[slot] = default(EquipmentElement); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            try
            {
                var roster = MobileParty.MainParty?.ItemRoster;
                var item = TaleWorlds.ObjectSystem.MBObjectManager.Instance?.GetObject<ItemObject>(def.ItemId);
                if (roster != null && item != null && roster.GetItemNumber(item) > 0)
                    roster.AddToCounts(item, -1);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            InformationManager.DisplayMessage(new InformationMessage(
                $"{def.Name} — the lattice fractures. The crystal is spent.",
                CrystalColor(def.GlowColor)));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void Announce(Agent caster, string msg, ColorSchool school)
        {
            try
            {
                if (caster == Agent.Main)
                    InformationManager.DisplayMessage(new InformationMessage(msg, CrystalColor(school)));
                else if (caster.Team != Agent.Main?.Team)
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{caster.Name} — {msg}", CrystalColor(school)));
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Brilliant Lattice potency — player only (an NPC bearer's crystal must not
        // read the player's lapidary talents).
        private static float Potency(Agent caster)
            => caster == Agent.Main ? CrystalTalents.PotencyMult : 1f;

        private static Color CrystalColor(ColorSchool school)
        {
            switch (school)
            {
                case ColorSchool.Yellow: return new Color(0.95f, 0.85f, 0.30f);
                case ColorSchool.Red:    return new Color(0.90f, 0.30f, 0.20f);
                case ColorSchool.Blue:   return new Color(0.35f, 0.65f, 0.95f);
                case ColorSchool.Purple: return new Color(0.70f, 0.40f, 0.90f);
                case ColorSchool.Orange: return new Color(0.95f, 0.60f, 0.20f);
                default:                 return new Color(0.60f, 0.60f, 0.65f); // Ashen / default
            }
        }
    }
}
