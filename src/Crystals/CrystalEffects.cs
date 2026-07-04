// =============================================================================
// ASH AND EMBER — Crystals/CrystalEffects.cs
//
// Battle crystal activation pipeline:
//   Player attacks with equipped crystal → OnCrystalHit() intercepts the blow,
//   cancels physical damage, starts a 2-second charge phase (coloured glow),
//   then fires the crystal's AoE effect via MissionTick.
//
// NPCs fire directly through CrystalBattleAI.ExecuteEffect().
// Daylight gate applies to both (SolarFlare extends the window).
// 10 % burndown chance per activation; item is removed from inventory on trigger.
//
// All TaleWorlds access is null-guarded and wrapped in individual try/catch.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static class CrystalEffects
    {
        private static readonly Random _rng = new Random();

        // ── Pending charge: attacker → (crystal type, seconds remaining) ──────
        private struct Charge
        {
            public CrystalType Type;
            public float       Remaining;
        }

        private static readonly Dictionary<Agent, Charge> _pendingCharge
            = new Dictionary<Agent, Charge>();

        // Player's last detected melee-attack time, so a fresh swing (hit OR miss)
        // begins a charge. Reset between battles.
        private static float _lastSwingTime = 0f;

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
            _pendingCharge.Clear();
            _rimeSlow.Clear();
            _veilSlow.Clear();
            _duskSlow.Clear();
            _lastSwingTime = 0f;
            _crystalMissiles.Clear();
        }

        // ── Daylight check ────────────────────────────────────────────────────

        private static bool CheckDaylight(Agent user)
        {
            // Waking Light: the lapidary learns to wake a crystal's stored light after dark.
            if (user == Agent.Main && CrystalTalents.WorksAtNight) return true;
            float hour = 12f; // default to noon if we can't read the time
            try { hour = (float)CampaignTime.Now.CurrentHourInDay; } catch { }
            bool extended = TalentSystem.Has(TalentId.SolarFlare);
            return extended ? CrystalMath.IsDaylightExtended(hour) : CrystalMath.IsDaylight(hour);
        }

        // ── Player: crystal weapon hit intercept ──────────────────────────────
        // Called from MagicMissionBehavior.OnAgentHit.
        public static void OnCrystalHit(Agent victim, Agent attacker,
            MissionWeapon weapon, int inflictedDamage)
        {
            if (attacker == null || attacker != Agent.Main) return; // player swings only

            string itemId = null;
            try { itemId = weapon.Item?.StringId; } catch { }
            if (!CrystalCatalog.IsCrystalItemId(itemId)) return;

            // A crystal deals no physical harm — restore whatever it inflicted.
            if (victim != null && victim.IsActive() && inflictedDamage > 0)
                try { SpellEffects.HealAgent(victim, inflictedDamage); } catch { }

            // A LANDED blow reliably wakes the crystal. The MissionTick swing detector
            // (LastMeleeAttackTime) is meant to catch misses too, but that signal does
            // not fire for these weapons in every build, so a connecting strike is the
            // dependable trigger — begin the charge here from the wielded weapon.
            try { TryBeginChargeOnSwing(attacker); } catch { }
        }

        // Begins a crystal's charge on a player swing — hit or miss, in any light.
        // (The old gate required landing a blow in daylight, which felt like nothing
        // happened.) One charge at a time; the next swing after it fires starts another.
        private static void TryBeginChargeOnSwing(Agent main)
        {
            if (_pendingCharge.ContainsKey(main)) return;
            string itemId = null;
            try { itemId = main.WieldedWeapon.Item?.StringId; } catch { }
            if (!CrystalCatalog.IsCrystalItemId(itemId)) return;
            if (!CrystalCatalog.TryGetByItemId(itemId, out var def)) return;

            // Swift Kindling shortens the charge for the player.
            float chargeSec = CrystalMath.ChargeDurationSec * (main == Agent.Main ? CrystalTalents.ChargeMult : 1f);
            _pendingCharge[main] = new Charge { Type = def.Type, Remaining = chargeSec };
            try { SpellEffects.BeginAgentGlow(main, def.GlowColor, chargeSec + 0.5f); } catch { }
            InformationManager.DisplayMessage(new InformationMessage(
                $"{def.Name} — drawing light…", CrystalColor(def.GlowColor)));
        }

        // ── MissionTick: advance charges and buff timers ──────────────────────

        public static void MissionTick(float dt)
        {
            if (Mission.Current == null) return;

            TickCrystalMissile(dt);

            // Swing detection: the player rouses a crystal by swinging it. The melee
            // attack time advances on every attack release (hit or miss), so a change
            // since last frame is a fresh swing → begin the charge.
            try
            {
                var main = Agent.Main;
                if (main != null && main.IsActive())
                {
                    float swingT = main.LastMeleeAttackTime;
                    if (swingT > 0f && swingT != _lastSwingTime)
                    {
                        _lastSwingTime = swingT;
                        TryBeginChargeOnSwing(main);
                    }
                }
            }
            catch { }

            // Advance pending charges.
            foreach (var kvp in _pendingCharge.ToList())
            {
                var agent  = kvp.Key;
                float left = kvp.Value.Remaining - dt;
                if (!agent.IsActive() || left <= 0f)
                {
                    _pendingCharge.Remove(agent);
                    if (agent.IsActive() && left <= 0f)
                        FireEffect(agent, kvp.Value.Type);
                }
                else
                    _pendingCharge[agent] = new Charge { Type = kvp.Value.Type, Remaining = left };
            }

            // Expire Rimeshard slow.
            foreach (var kvp in _rimeSlow.ToList())
            {
                float left = kvp.Value - dt;
                if (left <= 0f || !kvp.Key.IsActive())
                {
                    _rimeSlow.Remove(kvp.Key);
                    if (kvp.Key.IsActive())
                        try { kvp.Key.SetMaximumSpeedLimit(1f, false); } catch { }
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
                        try { kvp.Key.SetMaximumSpeedLimit(1f, false); } catch { }
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
                        try { kvp.Key.SetMaximumSpeedLimit(1f, false); } catch { }
                }
                else _duskSlow[kvp.Key] = left;
            }
        }

        // ── Effect dispatch ────────────────────────────────────────────────────

        // Called after the charge phase expires, and directly by CrystalBattleAI.
        public static void FireEffect(Agent caster, CrystalType type)
        {
            if (caster == null || !caster.IsActive() || Mission.Current == null) return;

            bool solarFlare = TalentSystem.Has(TalentId.SolarFlare);

            switch (type)
            {
                case CrystalType.Sunstone:     EffectSunstone(caster);               break;
                case CrystalType.Embershard:   EffectEmbershard(caster, solarFlare);  break;
                case CrystalType.Rimeshard:    EffectRimeshard(caster, solarFlare);   break;
                case CrystalType.Veilstone:    EffectVeilstone(caster, solarFlare);   break;
                case CrystalType.Stormcrystal: EffectStormcrystal(caster, solarFlare);break;
                case CrystalType.Duskstone:    EffectDuskstone(caster, solarFlare);   break;
            }

            // Burndown roll — only for player (NPC inventory is not adjusted mid-battle).
            if (caster == Agent.Main)
                TryBurndown(type);
        }

        // ── Sunstone ─────────────────────────────────────────────────────────

        private static void EffectSunstone(Agent caster)
        {
            try { SpellEffects.HealAgent(caster, CrystalMath.SunSelfHeal); } catch { }
            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Yellow, 2f); } catch { }

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
                    try { SpellEffects.HealAgent(a, CrystalMath.SunAllyHeal); } catch { }
                    mended++;
                }
            }
            catch { }

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

            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Red, 1.5f); } catch { }
            try { SpellEffects.SpawnTempLight(startPos, ColorSchool.Red, 6f, 10f); } catch { }

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
                    try { if (ElementWallWards.BlocksCrystal(pos, a.Position)) continue; } catch { }
                    _rimeSlow[a] = CrystalMath.RimeDurationSec;
                    try { a.SetMaximumSpeedLimit(CrystalMath.RimeSlowMult, false); } catch { }
                    slowed++;
                }
            }
            catch { }

            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Blue, 1.5f); } catch { }
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
                    try { if (ElementWallWards.BlocksCrystal(pos, a.Position)) continue; } catch { }
                    candidates.Add(a);
                }
            }
            catch { }

            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Purple, 1.5f); } catch { }

            if (candidates.Count == 0)
            {
                Announce(caster, "Veilstone — the veil finds nothing to grasp.", ColorSchool.Purple);
                return;
            }

            var target = candidates[_rng.Next(candidates.Count)];
            try { SpellEffects.DamageAgent(target, CrystalMath.VeilDamage, ColorSchool.Purple, caster); } catch { }
            _veilSlow[target] = CrystalMath.VeilDurationSec;
            try { target.SetMaximumSpeedLimit(CrystalMath.VeilSlowMult, false); } catch { }

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
                    try { if (ElementWallWards.BlocksCrystal(pos, a.Position)) continue; } catch { }
                    try { SpellEffects.DamageAgent(a, CrystalMath.StormDamage, ColorSchool.Orange, caster); } catch { }
                    try { a.ChangeMorale(-CrystalMath.StormMoraleDrain); } catch { }
                    hit++;
                }
            }
            catch { }

            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Orange, 1.5f); } catch { }
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
                    try { a.ChangeMorale(-CrystalMath.DuskMoraleDrain); } catch { }
                    _duskSlow[a] = CrystalMath.DuskDurationSec;
                    try { a.SetMaximumSpeedLimit(CrystalMath.DuskSlowMult, false); } catch { }
                    drained++;
                }
            }
            catch { }

            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Ashen, 2f); } catch { }
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
                    try { SpellEffects.SpawnTempLight(m.Position, ColorSchool.Red, 3f, 0.5f); } catch { }
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
                            try { SpellEffects.SpawnNatureBurst(mpos, NatureElement.Water, 0.8f); } catch { }
                        else
                            ExplodeCrystalMissile(m, mpos);
                        _crystalMissiles.RemoveAt(i);
                        continue;
                    }
                }
                catch { }

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
                catch { }

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
            catch { }

            // Burning shards take to timber — machines and gates in the blast char.
            try { SpellEffects.DamageBurnableStructures(pos, radius, CrystalMath.EmberDamage * 2f, m.Caster); } catch { }

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
                        SpellEffects.DamageAgent(a, CrystalMath.EmberDamage, ColorSchool.Red, m.Caster);
                        SpellEffects.SpawnImpactBurst(a.Position, ColorSchool.Red, 4f);
                        hit++;
                    }
                    catch { }
                }
            }
            catch { }

            Announce(m.Caster, hit > 0
                ? $"Embershard detonates — {hit} enemies scorched ({(int)CrystalMath.EmberDamage} HP each)."
                : "Embershard detonates — no enemies in range.",
                ColorSchool.Red);
        }

        // ── Burndown ──────────────────────────────────────────────────────────

        private static void TryBurndown(CrystalType type)
        {
            // Lasting Lattice makes the crystal far more likely to survive the draw.
            if (!(_rng.NextDouble() < CrystalMath.BurndownChance * CrystalTalents.ShatterMult)) return;

            var def    = CrystalCatalog.Get(type);
            var hero   = Hero.MainHero;
            var roster = MobileParty.MainParty?.ItemRoster;
            if (roster == null) return;

            try
            {
                var item = TaleWorlds.ObjectSystem.MBObjectManager.Instance?.GetObject<ItemObject>(def.ItemId);
                if (item != null && roster.GetItemNumber(item) > 0)
                    roster.AddToCounts(item, -1);
            }
            catch { }

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
            catch { }
        }

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
