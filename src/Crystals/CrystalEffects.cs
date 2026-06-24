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

        // ── Active slows (keyed by agent, value = seconds left) ──────────────
        private static readonly Dictionary<Agent, float> _rimeSlow = new Dictionary<Agent, float>();
        private static readonly Dictionary<Agent, float> _veilSlow = new Dictionary<Agent, float>();
        private static readonly Dictionary<Agent, float> _duskSlow = new Dictionary<Agent, float>();

        // ── State management ──────────────────────────────────────────────────

        public static void ClearBattleState()
        {
            _pendingCharge.Clear();
            _rimeSlow.Clear();
            _veilSlow.Clear();
            _duskSlow.Clear();
        }

        // ── Daylight check ────────────────────────────────────────────────────

        private static bool CheckDaylight(Agent user)
        {
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
            if (attacker == null || !attacker.IsActive()) return;
            if (attacker != Agent.Main) return; // only intercept player swings; NPC AI handles itself

            string itemId = null;
            try { itemId = weapon.Item?.StringId; } catch { }
            if (!CrystalCatalog.IsCrystalItemId(itemId)) return;

            // Cancel the physical damage by restoring it to the victim.
            if (victim != null && victim.IsActive() && inflictedDamage > 0)
                try { SpellEffects.HealAgent(victim, inflictedDamage); } catch { }

            // Block if already charging or night-time.
            if (_pendingCharge.ContainsKey(attacker))
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "The crystal is still gathering light.", CrystalColor(ColorSchool.Yellow)));
                return;
            }

            if (!CheckDaylight(attacker))
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "The crystal is cold — it cannot be roused in darkness.",
                    CrystalColor(ColorSchool.Blue)));
                return;
            }

            if (!CrystalCatalog.TryGetByItemId(itemId, out var def)) return;

            // Begin charge phase.
            _pendingCharge[attacker] = new Charge { Type = def.Type, Remaining = CrystalMath.ChargeDurationSec };
            try { SpellEffects.BeginAgentGlow(attacker, def.GlowColor, CrystalMath.ChargeDurationSec + 0.5f); } catch { }

            InformationManager.DisplayMessage(new InformationMessage(
                $"{def.Name} — drawing light…",
                CrystalColor(def.GlowColor)));
        }

        // ── MissionTick: advance charges and buff timers ──────────────────────

        public static void MissionTick(float dt)
        {
            if (Mission.Current == null) return;

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
            Vec3 pos;
            try { pos = caster.Position; } catch { return; }
            float r = solarFlare ? CrystalMath.SolarFlareRadius(CrystalMath.EmberRadius) : CrystalMath.EmberRadius;
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
                    try { SpellEffects.DamageAgent(a, CrystalMath.EmberDamage, ColorSchool.Red, caster); } catch { }
                    hit++;
                }
            }
            catch { }

            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Red, 1.5f); } catch { }
            Announce(caster, hit > 0
                ? $"Embershard — shard burst ({hit} enemies scorched, {(int)CrystalMath.EmberDamage} HP each)."
                : "Embershard — shard burst (no enemies in range).",
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
                    if (dx * dx + dy * dy <= r2) candidates.Add(a);
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

        // ── Burndown ──────────────────────────────────────────────────────────

        private static void TryBurndown(CrystalType type)
        {
            if (!(_rng.NextDouble() < CrystalMath.BurndownChance)) return;

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
