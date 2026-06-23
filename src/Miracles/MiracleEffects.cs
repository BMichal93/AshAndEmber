// =============================================================================
// ASH AND EMBER — Miracles/MiracleEffects.cs
//
// Applies miracle outcomes in both worlds:
//   • Battle  (Agent-based): AoE bursts, weapon enchants, shields, life drain.
//     Timed buffs live in static dicts keyed by Agent and are advanced by
//     MissionTick; weapon-enchant effects resolve in OnAgentHit.
//   • Campaign (Hero/party-based): wound parties, heal columns, morale swings.
//
// Grace miracles glow ColorSchool.Yellow (golden light).
// Cold miracles glow ColorSchool.Ashen (blue cold).
//
// All TaleWorlds access is null-guarded and wrapped in individual try/catch.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static class MiracleEffects
    {
        private static readonly Random _rng = new Random();

        private const string AshenKingdomId = "ashen_kingdom";

        // ── Battle buff timers (seconds remaining, keyed by Agent) ─────────────
        private static readonly Dictionary<Agent, float> _sacredFlame   = new Dictionary<Agent, float>();
        private static readonly Dictionary<Agent, float> _frostBrand    = new Dictionary<Agent, float>();
        private static readonly Dictionary<Agent, float> _shadowShroud  = new Dictionary<Agent, float>();
        private static readonly Dictionary<Agent, float> _dreadPresence = new Dictionary<Agent, float>();
        // Aegis of Faith: seconds remaining on the absorption aura, keyed by bearer.
        private static readonly Dictionary<Agent, float> _aegis           = new Dictionary<Agent, float>();
        // Pale Rigor: seconds until the freeze lifts, keyed by affected enemy.
        private static readonly Dictionary<Agent, float> _paleRigor       = new Dictionary<Agent, float>();
        // Light of Guidance: seconds remaining on the speed surge, keyed by ally.
        private static readonly Dictionary<Agent, float> _guidance        = new Dictionary<Agent, float>();
        // FrostBrand: chill timer on each victim (separate from DreadPresence so the two don't overwrite each other).
        private static readonly Dictionary<Agent, float> _frostBrandChill = new Dictionary<Agent, float>();

        public static bool HasSacredFlame(Agent a)  => a != null && _sacredFlame.TryGetValue(a, out float t)  && t > 0f;
        public static bool HasFrostBrand(Agent a)   => a != null && _frostBrand.TryGetValue(a, out float t)   && t > 0f;
        public static bool HasShadowShroud(Agent a) => a != null && _shadowShroud.TryGetValue(a, out float t) && t > 0f;
        public static bool HasAegis(Agent a)        => a != null && _aegis.TryGetValue(a, out float t)        && t > 0f;

        public static void ClearBattleState()
        {
            _sacredFlame.Clear();
            _frostBrand.Clear();
            _shadowShroud.Clear();
            _dreadPresence.Clear();
            _aegis.Clear();
            _paleRigor.Clear();
            _guidance.Clear();
            _frostBrandChill.Clear();
        }

        // Removes all hostile movement/morale effects from an agent (for Cleansing Rite).
        public static void PurgeHostileEffects(Agent a)
        {
            if (a == null) return;
            bool hadSlow = _dreadPresence.Remove(a) | _paleRigor.Remove(a) | _frostBrandChill.Remove(a);
            if (hadSlow)
                try { a.SetMaximumSpeedLimit(1f, true); } catch { }
        }

        // ── Player entry point ────────────────────────────────────────────────
        // Called by MiracleInputHandler after the sequence is confirmed. Validates
        // context, gate, and inventory, then applies the effect.
        public static bool TryUseMiracle(MiracleType type, bool inMission)
        {
            var def = MiracleCatalog.Get(type);

            // Context check
            bool okHere = inMission ? def.UsableInBattle : def.UsableOnMap;
            if (!okHere)
            {
                string place = inMission ? "the field" : "a battlefield";
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{def.Name} calls for {place}.", GraceColor));
                return false;
            }

            // Inventory + gate check
            int honor = 0, mercy = 0, generosity = 0;
            try
            {
                var h = Hero.MainHero;
                if (h != null)
                {
                    honor      = h.GetTraitLevel(DefaultTraits.Honor);
                    mercy      = h.GetTraitLevel(DefaultTraits.Mercy);
                    generosity = h.GetTraitLevel(DefaultTraits.Generosity);
                }
            }
            catch { }

            // Only Grace miracles are available to the player. Cold miracles are
            // NPC-only — ApplyBattleMiracle is called directly for them, bypassing this path.
            if (!MiracleInventory.HasGrace)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "You carry no Grace. Pray at a Sanctuary first.", GraceColor));
                return false;
            }
            if (!def.IsGrace)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{def.Name} — that miracle is not yours to call.", GraceColor));
                return false;
            }
            if (!MiracleMath.MeetsGraceGate(def.Gate, honor, mercy, generosity))
            {
                MiracleInventory.SpendGrace(); // gate failure still costs Grace
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{def.Name} — the light does not answer. Your virtue is not enough. [Grace spent]",
                    GraceColor));
                return false;
            }
            MiracleInventory.SpendGrace();

            if (inMission)
            {
                var self = Agent.Main;
                if (self == null || !self.IsActive())
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "There is no hand to shape the miracle.", GraceColor));
                    return false;
                }
                ApplyBattleMiracle(self, type, announce: true);
            }
            else
            {
                var hero  = Hero.MainHero;
                var party = MobileParty.MainParty;
                string result = ApplyCampaignMiracle(hero, party, type);
                if (!string.IsNullOrEmpty(result))
                {
                    Color c = def.IsGrace ? GraceColor : ColdColor;
                    InformationManager.DisplayMessage(new InformationMessage(result, c));
                }
            }
            return true;
        }

        // ── Battle effect application ─────────────────────────────────────────
        // announce: post a combat-log line (true for player and enemies, false for allies).
        public static void ApplyBattleMiracle(Agent a, MiracleType type, bool announce)
        {
            if (a == null || !a.IsActive()) return;
            // Focus aura for the invoking NPC — golden for Grace, cold for the dark rites,
            // mirroring the player's focus glow when casting a miracle.
            try
            {
                bool grace = MiracleCatalog.Get(type).IsGrace;
                SpellEffects.FlashFocusAura(a, grace ? ColorSchool.Yellow : ColorSchool.Ashen);
            }
            catch { }
            switch (type)
            {
                case MiracleType.RepelAshen:      BattleRepelAshen(a, announce);      break;
                case MiracleType.RadiantMending:  BattleRadiantMending(a, announce);  break;
                case MiracleType.LightOfGuidance: BattleGuidance(a, announce);        break;
                case MiracleType.SacredFlame:     BattleSacredFlame(a, announce);     break;
                case MiracleType.AegisOfFaith:    BattleAegis(a, announce);           break;
                case MiracleType.CleansingRite:   BattleCleansingRite(a, announce);   break;
                case MiracleType.AshenCurse:      BattleAshenCurse(a, announce);      break;
                case MiracleType.Dreadmending:    BattleDreadmending(a, announce);    break;
                case MiracleType.DreadPresence:   BattleDreadPresence(a, announce);   break;
                case MiracleType.FrostBrand:      BattleFrostBrand(a, announce);      break;
                case MiracleType.ShadowShroud:    BattleShadowShroud(a, announce);    break;
                case MiracleType.PaleRigor:       BattlePaleRigor(a, announce);       break;
            }
        }

        // ── Campaign effect application ───────────────────────────────────────
        // Returns a result string or null.
        public static string ApplyCampaignMiracle(Hero hero, MobileParty party, MiracleType type)
        {
            switch (type)
            {
                case MiracleType.RepelAshen:      return CampaignRepelAshen(party);
                case MiracleType.RadiantMending:  return CampaignRadiantMending(hero, party);
                case MiracleType.LightOfGuidance: return CampaignGuidance(party);
                case MiracleType.AegisOfFaith:    return "The Aegis of Faith calls for a battlefield.";
                case MiracleType.CleansingRite:   return CampaignCleansingRite(hero, party);
                case MiracleType.AshenCurse:      return CampaignAshenCurse(party);
                case MiracleType.Dreadmending:    return CampaignDreadmending(hero, party);
                case MiracleType.DreadPresence:   return CampaignDreadPresence(party);
                case MiracleType.SacredFlame:     return "Sacred Flame calls for a battlefield.";
                case MiracleType.FrostBrand:      return "Frost Brand calls for a battlefield.";
                case MiracleType.ShadowShroud:    return "Shadow Shroud calls for a battlefield.";
                case MiracleType.PaleRigor:       return "Pale Rigor calls for a battlefield.";
                default:                          return null;
            }
        }

        // ── Mission tick: advance timers ──────────────────────────────────────
        public static void MissionTick(float dt)
        {
            if (Mission.Current == null) return;
            DecayAndExpire(_sacredFlame,   dt, null);
            DecayAndExpire(_frostBrand,    dt, null);
            DecayAndExpire(_shadowShroud,  dt, null);
            DecayAndExpire(_dreadPresence, dt, a =>
            {
                try { a.SetMaximumSpeedLimit(1f, true); } catch { }
            });
            DecayAndExpire(_paleRigor, dt, a =>
            {
                try { a.SetMaximumSpeedLimit(1f, true); } catch { }
            });
            DecayAndExpire(_frostBrandChill, dt, a =>
            {
                try { a.SetMaximumSpeedLimit(1f, true); } catch { }
            });
            DecayAndExpire(_aegis, dt, null);
            DecayAndExpire(_guidance, dt, a =>
            {
                try { a.SetMaximumSpeedLimit(1f, true); } catch { }
            });
        }

        // Resolves per-hit weapon enchantments. Called from MagicMissionBehavior.OnAgentHit.
        public static void OnAgentHit(Agent affected, Agent affector, int inflicted)
        {
            if (affected == null || !affected.IsActive() || inflicted <= 0) return;

            if (affector != null && affector.IsActive())
            {
                // Sacred Flame: attacker's weapon carries holy fire.
                if (HasSacredFlame(affector) && !SpellEffects.IsWarded(affected))
                    try { SpellEffects.DamageAgent(affected, MiracleMath.SacredFlameBonusDamage, ColorSchool.Yellow, affector); } catch { }

                // Frost Brand: each hit leaves a chill on the victim.
                if (HasFrostBrand(affector) && affector != affected)
                {
                    _frostBrandChill[affected] = MiracleMath.FrostBrandChillSec;
                    try { affected.SetMaximumSpeedLimit(MiracleMath.FrostBrandSpeedMult, true); } catch { }
                    try { SpellEffects.BeginAgentGlow(affected, ColorSchool.Ashen, MiracleMath.FrostBrandChillSec); } catch { }
                }
            }

            // Shadow Shroud: defender absorbs a fraction of incoming damage.
            if (HasShadowShroud(affected))
                try { SpellEffects.HealAgent(affected, inflicted * MiracleMath.ShadowShroudResistFrac); } catch { }

            // Aegis of Faith: reflects AegisResistFrac of all incoming damage as healing
            // while the aura is active. MissionTick handles expiry.
            if (HasAegis(affected))
                try { SpellEffects.HealAgent(affected, inflicted * MiracleMath.AegisResistFrac); } catch { }
        }

        // ── Grace battle implementations ──────────────────────────────────────

        private static void BattleRepelAshen(Agent caster, bool announce)
        {
            Vec3 pos;
            try { pos = caster.Position; } catch { return; }
            float r2 = MiracleMath.RepelAshenRadius * MiracleMath.RepelAshenRadius;
            int   hit = 0;
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount) continue;
                    if (caster.Team != null && a.Team == caster.Team) continue;
                    if (!IsAshenAgent(a)) continue;
                    float dx = a.Position.x - pos.x, dy = a.Position.y - pos.y;
                    if (dx * dx + dy * dy > r2) continue;
                    if (SpellEffects.IsWarded(a)) continue;
                    try { SpellEffects.DamageAgent(a, MiracleMath.RepelAshenDamage, ColorSchool.Yellow, caster); } catch { }
                    hit++;
                }
            }
            catch { }
            try { SpellEffects.RecordMagicCast(pos); } catch { }
            if (announce)
                Log(caster, hit > 0
                    ? $"calls down the light — {hit} Ashen recoil from the golden flame."
                    : "calls down the light — but no Ashen are near enough to feel it.", false, true);
        }

        private static void BattleRadiantMending(Agent caster, bool announce)
        {
            float selfHeal = SafeLimit(caster) * MiracleMath.RadiantMendSelfFrac;
            try { SpellEffects.HealAgent(caster, selfHeal); } catch { }
            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Yellow, 3f); } catch { }

            Vec3 pos;
            try { pos = caster.Position; } catch { if (announce) Log(caster, "is mended by golden light.", false, true); return; }
            float r2 = MiracleMath.RadiantMendAllyRadius * MiracleMath.RadiantMendAllyRadius;
            int mended = 0;
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (a == caster || !a.IsActive() || a.IsMount) continue;
                    if (caster.Team == null || a.Team != caster.Team) continue;
                    float dx = a.Position.x - pos.x, dy = a.Position.y - pos.y;
                    if (dx * dx + dy * dy > r2) continue;
                    try { SpellEffects.HealAgent(a, SafeLimit(a) * MiracleMath.RadiantMendAllyFrac); } catch { }
                    mended++;
                }
            }
            catch { }
            if (announce)
                Log(caster, mended > 0
                    ? $"is touched by radiant light — wounds close nearby ({mended} allies mended)."
                    : "is touched by radiant light — wounds close.", false, true);
        }

        private static void BattleGuidance(Agent caster, bool announce)
        {
            try { MobileParty.MainParty.RecentEventsMorale += MiracleMath.GuidanceBattleMorale; } catch { }
            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Yellow, MiracleMath.GuidanceSpeedDurSec); } catch { }

            // Speed surge: caster + nearby allies move faster for the duration.
            int surged = 0;
            Vec3 pos;
            try { pos = caster.Position; } catch { goto announce; }
            float r2 = MiracleMath.GuidanceSpeedRadius * MiracleMath.GuidanceSpeedRadius;
            _guidance[caster] = MiracleMath.GuidanceSpeedDurSec;
            try { caster.SetMaximumSpeedLimit(MiracleMath.GuidanceSpeedMult, true); } catch { }
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (a == caster || !a.IsActive() || a.IsMount) continue;
                    if (caster.Team == null || a.Team != caster.Team) continue;
                    float dx = a.Position.x - pos.x, dy = a.Position.y - pos.y;
                    if (dx * dx + dy * dy > r2) continue;
                    _guidance[a] = MiracleMath.GuidanceSpeedDurSec;
                    try { a.SetMaximumSpeedLimit(MiracleMath.GuidanceSpeedMult, true); } catch { }
                    surged++;
                }
            }
            catch { }
            announce:
            if (announce)
                Log(caster, $"invokes the light — courage and speed surge through {surged} allies (+{(int)MiracleMath.GuidanceBattleMorale} morale, ×{MiracleMath.GuidanceSpeedMult} speed).", false, true);
        }

        private static void BattleSacredFlame(Agent caster, bool announce)
        {
            _sacredFlame[caster] = MiracleMath.SacredFlameDurationSec;
            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Yellow, MiracleMath.SacredFlameDurationSec); } catch { }
            if (announce)
                Log(caster, "breathes the sacred flame — their blade burns with consecrated fire.", false, true);
        }

        private static void BattleAegis(Agent caster, bool announce)
        {
            _aegis[caster] = MiracleMath.AegisDurationSec;
            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Yellow, MiracleMath.AegisDurationSec); } catch { }
            if (announce)
                Log(caster, $"is wrapped in the Aegis of Faith — {(int)(MiracleMath.AegisResistFrac * 100f)}% of all damage returned as healing for {(int)MiracleMath.AegisDurationSec} seconds.", false, true);
        }

        // ── Cold battle implementations ───────────────────────────────────────

        private static void BattleAshenCurse(Agent caster, bool announce)
        {
            Vec3 pos;
            try { pos = caster.Position; } catch { return; }
            float r2 = MiracleMath.CurseRadius * MiracleMath.CurseRadius;
            int hit = 0;
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount) continue;
                    if (caster.Team != null && a.Team == caster.Team) continue;
                    float dx = a.Position.x - pos.x, dy = a.Position.y - pos.y;
                    if (dx * dx + dy * dy > r2) continue;
                    if (SpellEffects.IsWarded(a)) continue;
                    try { SpellEffects.DamageAgent(a, MiracleMath.CurseDamage, ColorSchool.Ashen, caster); } catch { }
                    hit++;
                }
            }
            catch { }
            try { SpellEffects.RecordMagicCast(pos); } catch { }
            if (announce)
                Log(caster, hit > 0
                    ? $"releases the Ashen Curse — cold dark light tears at {hit} enemies."
                    : "releases the Ashen Curse — but no enemies are close enough to feel it.", false, false);
        }

        private static void BattleDreadmending(Agent caster, bool announce)
        {
            Vec3 pos;
            try { pos = caster.Position; } catch { return; }
            float r2 = MiracleMath.DreadmendRadius * MiracleMath.DreadmendRadius;
            float totalDrained = 0f;
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (a == caster || !a.IsActive() || a.IsMount) continue;
                    if (caster.Team != null && a.Team == caster.Team) continue;
                    float dx = a.Position.x - pos.x, dy = a.Position.y - pos.y;
                    if (dx * dx + dy * dy > r2) continue;
                    float drain = a.Health * MiracleMath.DreadmendFrac;
                    try { SpellEffects.DamageAgent(a, drain, ColorSchool.Ashen, caster); } catch { }
                    totalDrained += drain;
                }
            }
            catch { }
            if (totalDrained > 0f)
                try { SpellEffects.HealAgent(caster, totalDrained); } catch { }
            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Ashen, 3f); } catch { }
            if (announce)
                Log(caster, totalDrained > 0
                    ? $"draws life through the Dreadmending — {(int)totalDrained} vitality seized from the dying."
                    : "invokes Dreadmending — but nothing bleeds close enough to draw from.", false, false);
        }

        private static void BattleDreadPresence(Agent caster, bool announce)
        {
            Vec3 pos;
            try { pos = caster.Position; } catch { return; }
            float r2 = MiracleMath.DreadPresenceRadius * MiracleMath.DreadPresenceRadius;
            int hit = 0;
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (a == caster || !a.IsActive() || a.IsMount) continue;
                    if (caster.Team != null && a.Team == caster.Team) continue;
                    float dx = a.Position.x - pos.x, dy = a.Position.y - pos.y;
                    if (dx * dx + dy * dy > r2) continue;
                    _dreadPresence[a] = MiracleMath.DreadPresenceDurationSec;
                    try { a.SetMaximumSpeedLimit(MiracleMath.DreadPresenceSpeedMult, true); } catch { }
                    try { SpellEffects.BeginAgentGlow(a, ColorSchool.Ashen, MiracleMath.DreadPresenceDurationSec); } catch { }
                    hit++;
                }
            }
            catch { }
            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Ashen, 3f); } catch { }
            if (announce)
                Log(caster, hit > 0
                    ? $"manifests the Dread Presence — {hit} enemies flinch and slow."
                    : "manifests the Dread Presence — but no enemies are close enough to feel it.", false, false);
        }

        private static void BattleFrostBrand(Agent caster, bool announce)
        {
            _frostBrand[caster] = MiracleMath.FrostBrandDurationSec;
            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Ashen, MiracleMath.FrostBrandDurationSec); } catch { }
            if (announce)
                Log(caster, "brands their blade with frost — each blow will leave enemies chilled.", false, false);
        }

        private static void BattleShadowShroud(Agent caster, bool announce)
        {
            _shadowShroud[caster] = MiracleMath.ShadowShroudDurationSec;
            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Ashen, MiracleMath.ShadowShroudDurationSec); } catch { }
            if (announce)
                Log(caster, "draws the Shadow Shroud — blows that reach them find less than they expected.", false, false);
        }

        private static void BattleCleansingRite(Agent caster, bool announce)
        {
            Vec3 pos;
            try { pos = caster.Position; } catch { return; }
            float r2 = MiracleMath.CleansingRiteRadius * MiracleMath.CleansingRiteRadius;
            int cleansed = 0, scorched = 0;
            PurgeHostileEffects(caster);
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (a == caster || !a.IsActive() || a.IsMount) continue;
                    float dx = a.Position.x - pos.x, dy = a.Position.y - pos.y;
                    if (dx * dx + dy * dy > r2) continue;
                    if (caster.Team != null && a.Team == caster.Team)
                    {
                        PurgeHostileEffects(a);
                        cleansed++;
                    }
                    else
                    {
                        if (!SpellEffects.IsWarded(a))
                        {
                            try { SpellEffects.DamageAgent(a, MiracleMath.CleansingRiteDamage, ColorSchool.Yellow, caster); } catch { }
                            scorched++;
                        }
                    }
                }
            }
            catch { }
            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Yellow, 3f); } catch { }
            if (announce)
            {
                string ally  = cleansed > 0 ? $"{cleansed} {(cleansed == 1 ? "ally" : "allies")} cleansed" : "";
                string foe   = scorched > 0 ? $"{scorched} {(scorched == 1 ? "enemy" : "enemies")} scorched" : "";
                string parts = (ally.Length > 0 && foe.Length > 0) ? $"{ally}, {foe}" : (ally + foe);
                Log(caster,
                    parts.Length > 0
                        ? $"unleashes the Cleansing Rite — {parts}."
                        : "unleashes the Cleansing Rite — the flame burns through the air.",
                    false, true);
            }
        }

        private static void BattlePaleRigor(Agent caster, bool announce)
        {
            Vec3 pos;
            try { pos = caster.Position; } catch { return; }
            float r2 = MiracleMath.PaleRigorRadius * MiracleMath.PaleRigorRadius;
            int frozen = 0;
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount) continue;
                    if (caster.Team != null && a.Team == caster.Team) continue;
                    float dx = a.Position.x - pos.x, dy = a.Position.y - pos.y;
                    if (dx * dx + dy * dy > r2) continue;
                    _paleRigor[a] = MiracleMath.PaleRigorDurationSec;
                    try { a.SetMaximumSpeedLimit(0f, true); } catch { }
                    try { SpellEffects.BeginAgentGlow(a, ColorSchool.Ashen, MiracleMath.PaleRigorDurationSec); } catch { }
                    frozen++;
                }
            }
            catch { }
            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Ashen, 3f); } catch { }
            if (announce)
                Log(caster, frozen > 0
                    ? $"invokes Pale Rigor — {frozen} enemies seized by absolute cold."
                    : "invokes Pale Rigor — but no enemies are close enough to freeze.", false, false);
        }

        // ── Grace campaign implementations ────────────────────────────────────

        private static string CampaignRepelAshen(MobileParty party)
        {
            if (party == null) return "No party.";
            Vec2 p;
            try { p = party.GetPosition2D; } catch { return "The light finds no path."; }
            float rng2 = 200f * 200f;
            var targets = MobileParty.All
                .Where(mp => mp.IsActive && !mp.IsMainParty
                          && mp.MapFaction?.StringId == AshenKingdomId)
                .Select(mp => { Vec2 pp; try { pp = mp.GetPosition2D; } catch { pp = new Vec2(9999,9999); }
                                float dx = pp.x - p.x, dy = pp.y - p.y;
                                return (party: mp, d2: dx*dx+dy*dy); })
                .Where(t => t.d2 < rng2).OrderBy(t => t.d2).Take(3).Select(t => t.party).ToList();

            if (targets.Count == 0)
                return "The golden light surges outward. No Ashen are near enough to feel its heat.";

            int totalWounded = 0;
            foreach (var mp in targets)
            {
                int toWound = 12 + _rng.Next(9);
                int w = 0;
                foreach (var e in mp.MemberRoster.GetTroopRoster().ToList())
                {
                    if (e.Character.IsHero) continue;
                    int n = Math.Min(e.Number - e.WoundedNumber, toWound - w);
                    if (n <= 0) continue;
                    try { mp.MemberRoster.AddToCounts(e.Character, 0, false, n); w += n; } catch { }
                    if (w >= toWound) break;
                }
                try { mp.RecentEventsMorale -= 30f; } catch { }
                totalWounded += w;
            }
            return $"The light of repulsion sweeps the horizon. {targets.Count} Ashen {(targets.Count > 1 ? "forces" : "force")} recoil. {totalWounded} grey soldiers burned.";
        }

        private static string CampaignRadiantMending(Hero hero, MobileParty party)
        {
            if (hero != null)
                try { hero.HitPoints = hero.MaxHitPoints; } catch { }
            int healed = 0;
            if (party?.MemberRoster != null)
                foreach (var e in party.MemberRoster.GetTroopRoster().ToList())
                {
                    if (e.Character.IsHero || e.WoundedNumber <= 0) continue;
                    int heal = Math.Max(1, e.WoundedNumber / 4);
                    try { party.MemberRoster.AddToCounts(e.Character, 0, false, -heal); healed += heal; } catch { }
                }
            return healed > 0
                ? $"The golden light passes through the camp. Wounds close. {healed} of your soldiers rise from their cots."
                : "The golden light passes through the camp. Wounds close. You are whole.";
        }

        private static string CampaignGuidance(MobileParty party)
        {
            try { if (party != null) party.RecentEventsMorale += MiracleMath.GuidanceCampaignMorale; } catch { }
            return $"A pillar of calm settles over the column. The men walk straighter (+{(int)MiracleMath.GuidanceCampaignMorale} morale).";
        }

        private static string CampaignCleansingRite(Hero hero, MobileParty party)
        {
            // Clear all morale debt — the flame burns away what the dark has written.
            try { if (party != null && party.RecentEventsMorale < 0f) party.RecentEventsMorale = 0f; } catch { }

            // Fully recover all wounded soldiers.
            int healed = 0;
            if (party?.MemberRoster != null)
                foreach (var e in party.MemberRoster.GetTroopRoster().ToList())
                {
                    if (e.Character.IsHero || e.WoundedNumber <= 0) continue;
                    try { party.MemberRoster.AddToCounts(e.Character, 0, false, -e.WoundedNumber); healed += e.WoundedNumber; } catch { }
                }

            if (healed > 0)
                return $"The flame burns through the camp. Every wound closes. Every dark omen lifts. {healed} soldiers rise from their cots, whole.";
            return "The flame burns through the camp. The men breathe easier. Whatever the cold had left behind is gone now.";
        }

        private static string CampaignAshenCurse(MobileParty party)
        {
            if (party == null) return "No party.";
            Vec2 p;
            try { p = party.GetPosition2D; } catch { return "The curse finds no path."; }
            float rng2 = 150f * 150f;
            var mainFactionId = "";
            try { mainFactionId = Hero.MainHero?.MapFaction?.StringId ?? ""; } catch { }
            var targets = MobileParty.All
                .Where(mp => mp.IsActive && !mp.IsMainParty
                          && mp.MapFaction?.StringId != mainFactionId)
                .Select(mp => { Vec2 pp; try { pp = mp.GetPosition2D; } catch { pp = new Vec2(9999,9999); }
                                float dx = pp.x - p.x, dy = pp.y - p.y;
                                return (party: mp, d2: dx*dx+dy*dy); })
                .Where(t => t.d2 < rng2).OrderBy(t => t.d2).Take(3).Select(t => t.party).ToList();

            if (targets.Count == 0)
                return "The cold dark light curls away with no target to find.";

            int totalWounded = 0;
            foreach (var mp in targets)
            {
                int toWound = 10 + _rng.Next(9);
                int w = 0;
                foreach (var e in mp.MemberRoster.GetTroopRoster().ToList())
                {
                    if (e.Character.IsHero) continue;
                    int n = Math.Min(e.Number - e.WoundedNumber, toWound - w);
                    if (n <= 0) continue;
                    try { mp.MemberRoster.AddToCounts(e.Character, 0, false, n); w += n; } catch { }
                    if (w >= toWound) break;
                }
                try { mp.RecentEventsMorale -= 25f; } catch { }
                totalWounded += w;
            }
            return $"The Ashen Curse reaches out. {targets.Count} {(targets.Count > 1 ? "parties" : "party")} withers. {totalWounded} soldiers are brought low.";
        }

        private static string CampaignDreadmending(Hero hero, MobileParty party)
        {
            // Consume a prisoner; if none, mend at a lesser rate from the hero's own reserves.
            if (party?.PrisonRoster != null && party.PrisonRoster.Count > 0)
            {
                try
                {
                    var prisoners = party.PrisonRoster.GetTroopRoster().ToList();
                    var lowest = prisoners.Where(e => !e.Character.IsHero).OrderBy(e => e.Character.Tier).FirstOrDefault();
                    if (!lowest.Equals(default) && lowest.Number > 0)
                    {
                        party.PrisonRoster.AddToCounts(lowest.Character, -1);
                        if (hero != null)
                        {
                            int gain = Math.Max(1, (int)(hero.MaxHitPoints * MiracleMath.DreadmendFrac));
                            try { hero.HitPoints = Math.Min(hero.MaxHitPoints, hero.HitPoints + gain); } catch { }
                        }
                        return "A prisoner is unmade. The cold repays the debt. Your wounds close.";
                    }
                }
                catch { }
            }
            // No prisoners — lesser heal.
            if (hero != null)
            {
                int gain = Math.Max(1, (int)(hero.MaxHitPoints * 0.15f));
                try { hero.HitPoints = Math.Min(hero.MaxHitPoints, hero.HitPoints + gain); } catch { }
            }
            return "The cold finds scraps — no prisoner to spend. A lesser mending takes hold.";
        }

        private static string CampaignDreadPresence(MobileParty party)
        {
            if (party == null) return "No party.";
            Vec2 p;
            try { p = party.GetPosition2D; } catch { return "The dread finds no path."; }
            float rng2 = 150f * 150f;
            var mainFactionId = "";
            try { mainFactionId = Hero.MainHero?.MapFaction?.StringId ?? ""; } catch { }
            int hit = 0;
            foreach (var mp in MobileParty.All.ToList())
            {
                if (!mp.IsActive || mp.IsMainParty || mp.MapFaction?.StringId == mainFactionId) continue;
                Vec2 pp; try { pp = mp.GetPosition2D; } catch { continue; }
                float dx = pp.x - p.x, dy = pp.y - p.y;
                if (dx * dx + dy * dy > rng2) continue;
                try { mp.RecentEventsMorale -= 25f; hit++; } catch { }
            }
            return hit > 0
                ? $"A shiver of dread rolls outward. {hit} nearby {(hit > 1 ? "forces lose" : "force loses")} the will to press forward (morale -25 each)."
                : "The dread rolls outward into empty air.";
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static float SafeLimit(Agent a)
        {
            try { return a.HealthLimit > 0f ? a.HealthLimit : 100f; } catch { return 100f; }
        }

        private static bool IsAshenAgent(Agent a)
        {
            try
            {
                var charObj = a.Character as TaleWorlds.CampaignSystem.CharacterObject;
                if (charObj == null) return false;
                var hero = charObj.HeroObject;
                if (hero != null)
                    return ColourLordRegistry.IsAshenLord(hero)
                        || (hero == Hero.MainHero && MageKnowledge.IsAshen);
                return charObj.Culture?.StringId == AshenKingdomId;
            }
            catch { return false; }
        }

        private static void Log(Agent a, string blurb, bool bad, bool isGrace)
        {
            try
            {
                // Suppress ally messages to avoid log spam.
                if (Agent.Main != null && a.Team == Agent.Main.Team && a != Agent.Main) return;
                string who = a == Agent.Main ? "You" : (a?.Name ?? "Someone");
                Color c = isGrace ? GraceColor : ColdColor;
                if (bad) c = new Color(0.85f, 0.4f, 0.25f);
                InformationManager.DisplayMessage(new InformationMessage($"{who} — {blurb}", c));
            }
            catch { }
        }

        private static void DecayAndExpire(Dictionary<Agent, float> map, float dt, Action<Agent> onExpire)
        {
            if (map.Count == 0) return;
            foreach (var a in map.Keys.ToList())
            {
                float t = map[a] - dt;
                if (t <= 0f || a == null || !a.IsActive())
                {
                    if (a != null && onExpire != null) try { onExpire(a); } catch { }
                    map.Remove(a);
                }
                else map[a] = t;
            }
        }

        private static readonly Color GraceColor = new Color(0.95f, 0.82f, 0.35f);
        private static readonly Color ColdColor   = new Color(0.35f, 0.55f, 0.90f);
    }
}
