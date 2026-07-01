// =============================================================================
// ASH AND EMBER — Miracles/MiracleEffects.cs
//
// Applies miracle outcomes in both worlds:
//   • Battle  (Agent-based): AoE bursts, weapon enchants, shields.
//     Timed buffs live in static dicts keyed by Agent and are advanced by
//     MissionTick; weapon-enchant effects resolve in OnAgentHit.
//   • Campaign (Hero/party-based): wound parties, heal columns, morale swings.
//
// All six miracles are Grace (golden light). Cold miracles have been retired;
// dark NPC lords now express themselves through the Dark Gift system instead.
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
        private static readonly Dictionary<Agent, float> _sacredFlame = new Dictionary<Agent, float>();
        // Aegis of Faith: seconds remaining on the absorption aura, keyed by bearer.
        private static readonly Dictionary<Agent, float> _aegis       = new Dictionary<Agent, float>();
        // Light of Guidance: seconds remaining on the speed surge, keyed by ally.
        private static readonly Dictionary<Agent, float> _guidance    = new Dictionary<Agent, float>();

        public static bool HasSacredFlame(Agent a) => a != null && _sacredFlame.TryGetValue(a, out float t) && t > 0f;
        public static bool HasAegis(Agent a)       => a != null && _aegis.TryGetValue(a, out float t)       && t > 0f;

        public static void ClearBattleState()
        {
            _sacredFlame.Clear();
            _aegis.Clear();
            _guidance.Clear();
        }

        // Removes movement effects from an agent (used by Cleansing Rite on allies).
        public static void PurgeHostileEffects(Agent a)
        {
            if (a == null) return;
            try { a.SetMaximumSpeedLimit(1f, true); } catch { }
        }

        // ── Trait gate ─────────────────────────────────────────────────────────
        // Each Grace miracle is granted by one personality trait; it answers only
        // while the player holds that trait at +1 or higher.
        public static TraitObject TraitObjectOf(GraceTrait t)
        {
            switch (t)
            {
                case GraceTrait.Mercy:      return DefaultTraits.Mercy;
                case GraceTrait.Valor:      return DefaultTraits.Valor;
                case GraceTrait.Honor:      return DefaultTraits.Honor;
                case GraceTrait.Generosity: return DefaultTraits.Generosity;
                default:                    return DefaultTraits.Calculating;
            }
        }

        public static bool PlayerMeetsTrait(MiracleDef def)
        {
            try
            {
                var h = Hero.MainHero;
                return h != null && MiracleMath.MeetsTraitGate(h.GetTraitLevel(TraitObjectOf(def.Trait)));
            }
            catch { return false; }
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

            // Inventory + trait gate
            if (!MiracleInventory.HasGrace)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "You carry no Grace. Pray at a Sanctuary first.", GraceColor));
                return false;
            }
            if (!PlayerMeetsTrait(def))
            {
                MiracleInventory.SpendGrace(); // gate failure still costs Grace
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{def.Name} — the light does not answer. You are not {def.TraitName} enough. [Grace spent]",
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
                    InformationManager.DisplayMessage(new InformationMessage(result, GraceColor));
            }
            return true;
        }

        // ── Battle effect application ─────────────────────────────────────────
        // announce: post a combat-log line (true for player and enemies, false for allies).
        public static void ApplyBattleMiracle(Agent a, MiracleType type, bool announce)
        {
            if (a == null || !a.IsActive()) return;
            try { SpellEffects.FlashFocusAura(a, ColorSchool.Yellow); } catch { }
            switch (type)
            {
                case MiracleType.MercyMend:     BattleRadiantMending(a, announce);  break;
                case MiracleType.ValorFury:     BattleGuidance(a, announce);        break;
                case MiracleType.HonorAegis:    BattleAegis(a, announce);           break;
                case MiracleType.GraceBlessing: BattleHallowedGround(a, announce);  break;
                case MiracleType.InsightPyre:   BattlePyreJudgement(a, announce);   break;
            }
        }

        // ── Campaign effect application ───────────────────────────────────────
        // Returns a result string or null.
        public static string ApplyCampaignMiracle(Hero hero, MobileParty party, MiracleType type)
        {
            switch (type)
            {
                case MiracleType.MercyRelief:  return CampaignRadiantMending(hero, party);
                case MiracleType.ValorMarch:   return CampaignGuidance(party);
                case MiracleType.HonorOath:    return CampaignOath(hero);
                case MiracleType.GraceBounty:  return CampaignBounty(party);
                case MiracleType.InsightSight: return CampaignForesight(party);
                default:                       return null;
            }
        }

        // ── New map effects (Honor / Generosity / Calculating) ────────────────

        // The Sworn Word — steadies a wavering town the player stands in, or (in the
        // open) warms a lord of the player's faction toward them.
        private static string CampaignOath(Hero hero)
        {
            try
            {
                float power = MiracleTalents.TraitPower(GraceTrait.Honor);
                var s = hero?.CurrentSettlement;
                if (s?.Town != null)
                {
                    float gain = MiracleMath.OathLoyaltyGain * power;
                    s.Town.Loyalty  = Math.Min(100f, s.Town.Loyalty  + gain);
                    s.Town.Security = Math.Min(100f, s.Town.Security + gain);
                    return $"The Sworn Word is spoken over {s.Name}. The town steadies — loyalty and order return (+{(int)gain}).";
                }
                var kingdom = hero?.Clan?.Kingdom;
                var lord = Hero.AllAliveHeroes
                    .Where(h => h != hero && h.IsLord && h.IsAlive && !h.IsPrisoner
                             && h.Clan?.Kingdom == kingdom && kingdom != null)
                    .OrderBy(_ => _rng.Next()).FirstOrDefault()
                    ?? Hero.AllAliveHeroes.Where(h => h != hero && h.IsLord && h.IsAlive && !h.IsPrisoner)
                        .OrderBy(_ => _rng.Next()).FirstOrDefault();
                if (lord != null)
                {
                    int rel = Math.Max(1, (int)(MiracleMath.OathRelationGain * power));
                    try { TaleWorlds.CampaignSystem.Actions.ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                        hero, lord, rel, false); } catch { }
                    return $"You swear an oath in the light, and word reaches {lord.Name}. They think the better of you (+{rel} relation).";
                }
                return "You speak the oath, but there is no one near to hold you to it.";
            }
            catch { return null; }
        }

        // The Open Hand — fills the party's stores and lifts its spirits.
        private static string CampaignBounty(MobileParty party)
        {
            try
            {
                if (party == null) return null;
                float power = MiracleTalents.TraitPower(GraceTrait.Generosity);
                int food = Math.Max(1, (int)(MiracleMath.BountyFood * power));
                float morale = MiracleMath.BountyMorale * power;
                var grain = TaleWorlds.ObjectSystem.MBObjectManager.Instance?.GetObject<ItemObject>("grain");
                if (grain != null)
                    try { party.ItemRoster.AddToCounts(grain, food); } catch { }
                try { party.RecentEventsMorale += morale; } catch { }
                return $"The Open Hand opens. The stores are fuller by morning (+{food} grain), and the column eats well (+{(int)morale} morale).";
            }
            catch { return null; }
        }

        // Far-Sight — the light shows the roads; reports the nearest threat and steadies the party.
        private static string CampaignForesight(MobileParty party)
        {
            try
            {
                if (party == null) return null;
                float power = MiracleTalents.TraitPower(GraceTrait.Calculating);
                float sightMorale = MiracleMath.SightMorale * power;
                try { party.RecentEventsMorale += sightMorale; } catch { }
                Vec2 here = party.GetPosition2D;
                float sr = MiracleMath.SightRadius * power;   // Clearer Sight reaches further
                float r2 = sr * sr;
                MobileParty nearest = null; float best = float.MaxValue;
                foreach (var p in MobileParty.All)
                {
                    if (p == null || p == party || !p.IsActive) continue;
                    if (p.MapFaction == null || party.MapFaction == null) continue;
                    if (!FactionManager.IsAtWarAgainstFaction(p.MapFaction, party.MapFaction)) continue;
                    float d = (p.GetPosition2D - here).LengthSquared;
                    if (d < best && d <= r2) { best = d; nearest = p; }
                }
                return nearest != null
                    ? $"Far-Sight — the light shows the roads. {nearest.Name} moves against you, not far off. Your scouts ride easier for it (+{(int)sightMorale} morale)."
                    : $"Far-Sight — the light shows the roads. Nothing hostile stirs nearby. The column rides easier (+{(int)sightMorale} morale).";
            }
            catch { return null; }
        }

        // ── Mission tick: advance timers ──────────────────────────────────────
        public static void MissionTick(float dt)
        {
            if (Mission.Current == null) return;
            DecayAndExpire(_sacredFlame, dt, null);
            DecayAndExpire(_aegis,       dt, null);
            DecayAndExpire(_guidance,    dt, a =>
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
            }

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
                    : "calls down the light — but no Ashen are near enough to feel it.");
        }

        private static void BattleRadiantMending(Agent caster, bool announce)
        {
            float power = MiracleTalents.TraitPower(GraceTrait.Mercy);
            float selfHeal = SafeLimit(caster) * MiracleMath.RadiantMendSelfFrac * power;
            try { SpellEffects.HealAgent(caster, selfHeal); } catch { }
            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Yellow, 3f); } catch { }

            Vec3 pos;
            try { pos = caster.Position; } catch { if (announce) Log(caster, "is mended by golden light."); return; }
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
                    try { SpellEffects.HealAgent(a, SafeLimit(a) * MiracleMath.RadiantMendAllyFrac * power); } catch { }
                    mended++;
                }
            }
            catch { }
            if (announce)
                Log(caster, mended > 0
                    ? $"is touched by radiant light — wounds close nearby ({mended} allies mended)."
                    : "is touched by radiant light — wounds close.");
        }

        private static void BattleGuidance(Agent caster, bool announce)
        {
            float power = MiracleTalents.TraitPower(GraceTrait.Valor);
            try { MobileParty.MainParty.RecentEventsMorale += MiracleMath.GuidanceBattleMorale * power; } catch { }
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
                Log(caster, $"invokes the light — courage and speed surge through {surged} allies (+{(int)MiracleMath.GuidanceBattleMorale} morale, ×{MiracleMath.GuidanceSpeedMult} speed).");
        }

        private static void BattleSacredFlame(Agent caster, bool announce)
        {
            _sacredFlame[caster] = MiracleMath.SacredFlameDurationSec;
            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Yellow, MiracleMath.SacredFlameDurationSec); } catch { }
            if (announce)
                Log(caster, "breathes the sacred flame — their blade burns with consecrated fire.");
        }

        private static void BattleAegis(Agent caster, bool announce)
        {
            // The Iron Oath (Honour) holds the ward longer.
            float dur = MiracleMath.AegisDurationSec * MiracleTalents.TraitPower(GraceTrait.Honor);
            _aegis[caster] = dur;
            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Yellow, dur); } catch { }
            if (announce)
                Log(caster, $"is wrapped in the Aegis of Faith — {(int)(MiracleMath.AegisResistFrac * 100f)}% of all damage returned as healing for {(int)dur} seconds.");
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
                        : "unleashes the Cleansing Rite — the flame burns through the air.");
            }
        }

        private static void BattlePyreJudgement(Agent caster, bool announce)
        {
            Vec3 pos, fwd;
            try { pos = caster.Position; fwd = caster.LookDirection; } catch { return; }

            // Drop the pillar on the ground ahead of where the caster is looking.
            Vec3 fwdH = new Vec3(fwd.x, fwd.y, 0f);
            if (fwdH.Length > 0.01f) fwdH = fwdH.NormalizedCopy(); else fwdH = new Vec3(0f, 1f, 0f);
            Vec3 impact = new Vec3(
                pos.x + fwdH.x * MiracleMath.PyreJudgementReach,
                pos.y + fwdH.y * MiracleMath.PyreJudgementReach,
                pos.z);

            try { SpellEffects.SpawnExplosionParticle(impact, 2f); } catch { }
            try { SpellEffects.SpawnBigFireParticle(impact, 2.5f); } catch { }

            float r2 = MiracleMath.PyreJudgementRadius * MiracleMath.PyreJudgementRadius;
            int hit = 0;
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (a == caster || !a.IsActive() || a.IsMount) continue;
                    if (caster.Team != null && a.Team == caster.Team) continue;
                    float dx = a.Position.x - impact.x, dy = a.Position.y - impact.y;
                    if (dx * dx + dy * dy > r2) continue;
                    if (SpellEffects.IsWarded(a)) continue;
                    try { SpellEffects.DamageAgent(a, MiracleMath.PyreJudgementDamage * MiracleTalents.TraitPower(GraceTrait.Calculating), ColorSchool.Yellow, caster); } catch { }
                    try { SpellEffects.SpawnImpactBurst(a.Position, ColorSchool.Yellow, 4f); } catch { }
                    hit++;
                }
            }
            catch { }

            // Survivors are hurled from the light.
            try { SpellEffects.ScatterEnemies(impact, MiracleMath.PyreJudgementRadius, caster.Team); } catch { }
            try { SpellEffects.RecordMagicCast(impact); } catch { }

            if (announce)
                Log(caster, hit > 0
                    ? $"calls the Pyre of Judgement — a pillar of fire falls and {hit} {(hit == 1 ? "enemy is" : "enemies are")} cast from the light."
                    : "calls the Pyre of Judgement — the pillar falls upon empty ground.");
        }

        private static void BattleHallowedGround(Agent caster, bool announce)
        {
            // Ward the caster and nearby allies against all magic (reuses the shared
            // ward system that enemy spells and Dark Gifts check), then mend them.
            float power = MiracleTalents.TraitPower(GraceTrait.Generosity);
            try { SpellEffects.ExecuteWardFromAgent(caster, MiracleMath.HallowedGroundRadius); } catch { }
            try { SpellEffects.HealAgent(caster, SafeLimit(caster) * MiracleMath.HallowedGroundHealFrac * power); } catch { }
            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Yellow, 10f); } catch { }

            Vec3 pos;
            try { pos = caster.Position; } catch { if (announce) Log(caster, "consecrates the ground — the light wards you."); return; }
            float r2 = MiracleMath.HallowedGroundRadius * MiracleMath.HallowedGroundRadius;
            int warded = 0;
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (a == caster || !a.IsActive() || a.IsMount) continue;
                    if (caster.Team == null || a.Team != caster.Team) continue;
                    float dx = a.Position.x - pos.x, dy = a.Position.y - pos.y;
                    if (dx * dx + dy * dy > r2) continue;
                    try { SpellEffects.HealAgent(a, SafeLimit(a) * MiracleMath.HallowedGroundHealFrac * power); } catch { }
                    warded++;
                }
            }
            catch { }
            if (announce)
                Log(caster, warded > 0
                    ? $"consecrates the ground — {warded} {(warded == 1 ? "ally stands" : "allies stand")} warded against all magic for 10 seconds."
                    : "consecrates the ground — no magic can touch you for 10 seconds.");
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
            float power = MiracleTalents.TraitPower(GraceTrait.Mercy);
            if (hero != null)
                try { hero.HitPoints = hero.MaxHitPoints; } catch { }
            int healed = 0;
            if (party?.MemberRoster != null)
                foreach (var e in party.MemberRoster.GetTroopRoster().ToList())
                {
                    if (e.Character.IsHero || e.WoundedNumber <= 0) continue;
                    int heal = Math.Max(1, (int)(e.WoundedNumber / 4f * power));
                    try { party.MemberRoster.AddToCounts(e.Character, 0, false, -heal); healed += heal; } catch { }
                }
            return healed > 0
                ? $"The golden light passes through the camp. Wounds close. {healed} of your soldiers rise from their cots."
                : "The golden light passes through the camp. Wounds close. You are whole.";
        }

        private static string CampaignGuidance(MobileParty party)
        {
            float morale = MiracleMath.GuidanceCampaignMorale * MiracleTalents.TraitPower(GraceTrait.Valor);
            try { if (party != null) party.RecentEventsMorale += morale; } catch { }
            return $"A pillar of calm settles over the column. The men walk straighter (+{(int)morale} morale).";
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

        private static void Log(Agent a, string blurb)
        {
            try
            {
                // Suppress ally messages to avoid log spam.
                if (Agent.Main != null && a.Team == Agent.Main.Team && a != Agent.Main) return;
                string who = a == Agent.Main ? "You" : (a?.Name ?? "Someone");
                InformationManager.DisplayMessage(new InformationMessage($"{who} — {blurb}", GraceColor));
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
    }
}
