// =============================================================================
// ASH AND EMBER — Nature/NatureEffects.cs
// Executes the eight Living Ember powers (an attack and a support for each of the
// four elements) in battle and on the campaign map, each with real terrain
// particles so a cast looks like the land itself answering.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AshAndEmber
{
    public static class NatureEffects
    {
        private static readonly Random _rng = new Random();

        // Speed-limit tokens (agent index → remaining seconds).
        private static readonly Dictionary<int, (float remaining, Agent agent)> _speedTokens
            = new Dictionary<int, (float, Agent)>();
        // Damage-resistance tokens.
        private static readonly Dictionary<int, (float fraction, float remaining, Agent agent)> _resistTokens
            = new Dictionary<int, (float, float, Agent)>();

        // ── Armour gate ─────────────────────────────────────────────────────────
        public static bool ArmourTooHeavy(Agent agent)
        {
            if (agent == null) return false;
            try
            {
                float total = 0f;
                var eq = agent.SpawnEquipment;
                if (eq == null) return false;
                for (EquipmentIndex idx = EquipmentIndex.Head; idx <= EquipmentIndex.Cape; idx++)
                {
                    var item = eq.GetEquipmentFromSlot(idx).Item;
                    if (item != null) total += item.Weight;
                }
                return total > NatureMath.ArmourWeightCap;
            }
            catch { return false; }
        }

        // ── Execute ─────────────────────────────────────────────────────────────
        public static bool Execute(NaturePower power, Agent caster, bool inMission)
        {
            if (power == NaturePower.None) return false;

            if (inMission)
            {
                if (caster == null || !caster.IsActive() || caster.Health <= 0f) return false;
                if (!SpellEffects.HasFreeHand(caster))
                {
                    Msg("Both hands are occupied. The land cannot speak through closed fists.", NatureColor);
                    return false;
                }
                if (ArmourTooHeavy(caster))
                {
                    Msg("The weight of your armour smothers the channel. The land cannot reach you.", NatureColor);
                    return false;
                }
                return ExecuteBattle(power, caster);
            }
            return ExecuteCampaign(power);
        }

        // NPC variant — no player restrictions.
        public static void ExecuteNpc(NaturePower power, Agent caster, Team casterTeam)
        {
            if (power == NaturePower.None || caster == null || !caster.IsActive()) return;
            try { ExecuteBattleCore(power, caster, casterTeam); } catch { }
        }

        // ── Battle ──────────────────────────────────────────────────────────────
        private static bool ExecuteBattle(NaturePower power, Agent caster)
        {
            try { ExecuteBattleCore(power, caster, caster.Team); return true; }
            catch { return false; }
        }

        private static void ExecuteBattleCore(NaturePower power, Agent caster, Team team)
        {
            Vec3 pos = caster.Position;
            switch (power)
            {
                case NaturePower.Gale:        BattleGale(caster, pos, team);                              break;
                case NaturePower.Windwall:    BattleBarrier(caster, pos, team, NatureElement.Wind);       break;
                case NaturePower.Entangle:    BattleEntangle(caster, pos, team);                          break;
                case NaturePower.Thornwall:   BattleBarrier(caster, pos, team, NatureElement.Earth);      break;
                case NaturePower.Torrent:     BattleTorrent(caster, pos, team);                           break;
                case NaturePower.Mistwall:    BattleBarrier(caster, pos, team, NatureElement.Water);      break;
                case NaturePower.ThunderClap: BattleThunderClap(caster, pos, team);                       break;
                case NaturePower.Stormwall:   BattleBarrier(caster, pos, team, NatureElement.Storm);      break;
            }

            // Living glow + element light bloom (the per-power shapes are spawned
            // inside each Battle* method).
            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Nature, 2.5f); } catch { }
            try { SpawnElementVisual(NatureMath.ElementOf(power), pos); } catch { }
        }

        // Wind · Gale — 360° knockback + damage; a ring of blown dust.
        private static void BattleGale(Agent caster, Vec3 pos, Team team)
        {
            try { SpellEffects.SpawnNatureRing(pos, NatureElement.Wind, NatureMath.GaleRadius * 0.7f, 1.6f); } catch { }
            ForEachEnemyInRadius(pos, NatureMath.GaleRadius, team, enemy =>
            {
                ApplyDamage(enemy, caster, NatureMath.GaleDamage, DamageTypes.Invalid);
                try
                {
                    Vec3 dir = (enemy.Position - pos).NormalizedCopy();
                    enemy.TeleportToPosition(enemy.Position + dir * NatureMath.GaleKnockback);
                }
                catch { }
                ApplySpeedToken(enemy, NatureMath.GaleSlowMult, NatureMath.GaleSlowSec);
            });
        }

        // All barrier powers — place an elemental wall in front of the caster.
        private static void BattleBarrier(Agent caster, Vec3 pos, Team team, NatureElement el)
        {
            try { SpellEffects.SpawnNatureBarrier(pos, caster.LookDirection, el, team); } catch { }
            bool isPlayer = false;
            try { isPlayer = Agent.Main != null && caster == Agent.Main; } catch { }
            if (isPlayer)
                Msg($"{NatureMath.PowerName(NatureMath.SupportPower(el))} — the land rises before you.", NatureColor);
        }

        // Earth · Entangle — roots erupt in an AoE: damage + immobilise; root ring.
        private static void BattleEntangle(Agent caster, Vec3 pos, Team team)
        {
            try { SpellEffects.SpawnNatureRing(pos, NatureElement.Earth, NatureMath.EntangleRadius * 0.8f, 2.5f); } catch { }
            ForEachEnemyInRadius(pos, NatureMath.EntangleRadius, team, enemy =>
            {
                ApplyDamage(enemy, caster, NatureMath.EntangleDamage, DamageTypes.Blunt);
                try { enemy.SetMaximumSpeedLimit(0f, false); } catch { }
                ApplySpeedToken(enemy, 0f, NatureMath.EntangleRootSec);   // held in place
                try { SpellEffects.SpawnNatureBurst(enemy.Position, NatureElement.Earth, 2.0f); } catch { }
            });
            ApplySpeedToken(caster, 0f, NatureMath.EntangleStaggerSec);
        }

        // Water · Torrent — forward cone: damage + knockback that breaks formation.
        private static void BattleTorrent(Agent caster, Vec3 pos, Team team)
        {
            Vec3 fwd = caster.LookDirection.NormalizedCopy();
            float halfAngle = NatureMath.TorrentAngleDeg * 0.5f * (float)(Math.PI / 180.0);
            try { SpellEffects.SpawnNatureLine(pos, pos + fwd * NatureMath.TorrentRange, NatureElement.Water, 2.0f); } catch { }

            ForEachEnemyInRadius(pos, NatureMath.TorrentRange, team, enemy =>
            {
                Vec3 toEnemy = (enemy.Position - pos).NormalizedCopy();
                if (Vec3.DotProduct(fwd, toEnemy) < Math.Cos(halfAngle)) return;
                ApplyDamage(enemy, caster, NatureMath.TorrentDamage, DamageTypes.Invalid);
                try { enemy.TeleportToPosition(enemy.Position + toEnemy * NatureMath.TorrentKnockback); } catch { }
                ApplySpeedToken(enemy, NatureMath.TorrentSlowMult, NatureMath.TorrentSlowSec);
            });
        }

        // Storm · Thunderclap — bolt to nearest enemy, chaining to a couple more.
        private static void BattleThunderClap(Agent caster, Vec3 pos, Team team)
        {
            Agent primary = NearestEnemy(caster, pos, NatureMath.ThunderRange, team);
            if (primary == null) return;

            ApplyDamage(primary, caster, NatureMath.ThunderDamage, DamageTypes.Invalid);
            try { primary.SetMaximumSpeedLimit(0f, false); } catch { }
            ApplySpeedToken(primary, 0f, NatureMath.ThunderStunSec);
            try { SpellEffects.SpawnTempLightWhite(primary.Position + new Vec3(0f, 0f, 1f), 10f, 0.3f); } catch { }
            try { SpellEffects.SpawnNatureBurst(primary.Position + new Vec3(0f, 0f, 1f), NatureElement.Storm, 1.0f); } catch { }

            int chains = 0;
            ForEachEnemyInRadius(primary.Position, NatureMath.ThunderChainRadius, team, chain =>
            {
                if (chains >= NatureMath.ThunderChainCount || chain == primary) return;
                ApplyDamage(chain, caster, NatureMath.ThunderChainDamage, DamageTypes.Invalid);
                try { SpellEffects.SpawnTempLightWhite(chain.Position + new Vec3(0f, 0f, 1f), 7f, 0.25f); } catch { }
                try { SpellEffects.SpawnNatureBurst(chain.Position + new Vec3(0f, 0f, 1f), NatureElement.Storm, 0.9f); } catch { }
                chains++;
            });
        }

        // Element light bloom: Wind pale-white, Earth green, Water blue, Storm flash.
        private static void SpawnElementVisual(NatureElement el, Vec3 pos)
        {
            Vec3 up  = new Vec3(0f, 0f, 0.8f);
            Vec3 up2 = new Vec3(0f, 0f, 1.5f);
            switch (el)
            {
                case NatureElement.Wind:
                    SpellEffects.SpawnTempLightRgb(pos + up2, new Vec3(0.88f, 0.90f, 1.0f), 12f, 0.8f);
                    SpellEffects.SpawnTempLightRgb(pos + up,  new Vec3(0.82f, 0.85f, 1.0f),  8f, 0.5f);
                    break;
                case NatureElement.Earth:
                    SpellEffects.SpawnTempLightRgb(pos + up,  new Vec3(0.15f, 1.0f, 0.15f),  9f, 2.5f);
                    SpellEffects.SpawnTempLightRgb(pos,       new Vec3(0.1f,  0.7f, 0.1f),   5f, 1.5f);
                    break;
                case NatureElement.Water:
                    SpellEffects.SpawnTempLightRgb(pos + up,  new Vec3(0.2f, 0.6f, 1.0f),   10f, 2.5f);
                    SpellEffects.SpawnTempLightRgb(pos,       new Vec3(0.3f, 0.7f, 1.0f),    6f, 1.5f);
                    break;
                case NatureElement.Storm:
                    SpellEffects.SpawnTempLightWhite(pos + up2, 18f, 0.25f);
                    SpellEffects.SpawnTempLightWhite(pos + up,  12f, 0.40f);
                    SpellEffects.SpawnTempLightRgb(pos + up, new Vec3(0.65f, 0.65f, 1.0f), 8f, 1.5f);
                    break;
            }
        }

        // ── Campaign effects ────────────────────────────────────────────────────
        private static bool ExecuteCampaign(NaturePower power)
        {
            if (NatureMath.IsAttack(power))
            {
                Msg($"{NatureMath.PowerName(power)} — this force needs enemies. Carry your charge into battle.", NatureColor);
                return false;
            }
            try
            {
                var party = MobileParty.MainParty;
                if (party == null) return false;
                string result = ApplyCampaignEffect(power, party);
                if (!string.IsNullOrEmpty(result)) Msg(result, NatureColor);
                return true;
            }
            catch { return false; }
        }

        // Public so NPC daily tick and player path share the same effect logic.
        public static string ApplyCampaignEffect(NaturePower power, MobileParty party)
        {
            if (party == null) return "";
            bool isMain = false;
            try { isMain = party.IsMainParty; } catch { }
            switch (power)
            {
                case NaturePower.Windwall:  return CampaignWindward(party, isMain);
                case NaturePower.Thornwall: return CampaignRootMend(party, isMain);
                case NaturePower.Mistwall:  return CampaignStillWaters(party, isMain);
                case NaturePower.Stormwall: return CampaignThundersEdge(party, isMain);
                default:                    return "";
            }
        }

        // Wind — a column of high air tears through the march; wounds seal, hearts lift.
        // Player: also restores the hero's body. Morale +25, heal 8 wounded.
        private static string CampaignWindward(MobileParty party, bool isPlayer)
        {
            try { party.RecentEventsMorale += 25f; } catch { }
            int healed = HealWounded(party, isPlayer ? 8 : 4);
            if (isPlayer)
            {
                try
                {
                    var h = Hero.MainHero;
                    if (h != null) h.HitPoints = Math.Min(h.HitPoints + 45, h.MaxHitPoints);
                }
                catch { }
                return healed > 0
                    ? $"The world draws a breath. Cold air, tasting of altitude and stone, tears through the column — banners rip sideways, dust lifts in spirals, and when the gust passes your body is lighter and {healed} of the wounded have risen. (+25 morale, hero restored)"
                    : "The world draws a breath. Cold air, tasting of altitude and stone, tears through the column — banners rip sideways, dust lifts in spirals, and when it passes your body is lighter. (+25 morale, hero restored)";
            }
            return healed > 0
                ? $"The wind steadies the march (+25 morale, {healed} healed)."
                : "The wind steadies the march (+25 morale).";
        }

        // Earth — the forest floor opens; root-provisions emerge and wounds are mended.
        // Player: grants 40 grain + 10 meat (the earth's larder), heals 8 wounded, morale +8.
        private static string CampaignRootMend(MobileParty party, bool isPlayer)
        {
            int healed = HealWounded(party, isPlayer ? 8 : 6);
            try { party.RecentEventsMorale += 8f; } catch { }
            if (isPlayer)
            {
                int grain = 0, meat = 0;
                try
                {
                    var grainItem = MBObjectManager.Instance?.GetObject<ItemObject>("grain");
                    if (grainItem != null) { party.ItemRoster.AddToCounts(grainItem, 40); grain = 40; }
                }
                catch { }
                try
                {
                    var meatItem = MBObjectManager.Instance?.GetObject<ItemObject>("meat");
                    if (meatItem != null) { party.ItemRoster.AddToCounts(meatItem, 10); meat = 10; }
                }
                catch { }
                string foodLine = (grain > 0 || meat > 0)
                    ? $" The cooks are staring: {(grain > 0 ? $"{grain} grain" : "")}{(grain > 0 && meat > 0 ? ", " : "")}{(meat > 0 ? $"{meat} cuts of meat" : "")} — left by no living hand."
                    : "";
                return healed > 0
                    ? $"The forest floor shifts. Root-threads bring up what the earth has stored.{foodLine} {healed} wound{(healed > 1 ? "s have" : " has")} closed while the land worked. (+8 morale)"
                    : $"The forest floor shifts. Root-threads bring up what the earth has stored.{foodLine} (+8 morale)";
            }
            return healed > 0
                ? $"The earth provides: {healed} healed (+8 morale)."
                : "The earth stirs and the march is steadied (+8 morale).";
        }

        // Water — the underground current shows a path; player is carried to a nearby settlement.
        // Requires a town within ~32 map units. Falls back to healing if nothing is reachable.
        // Returns "" — the inquiry (or fallback Msg) handles all player messaging directly.
        private static string CampaignStillWaters(MobileParty party, bool isPlayer)
        {
            if (!isPlayer)
            {
                int healed = HealWounded(party, 6);
                try { party.RecentEventsMorale += 12f; } catch { }
                return healed > 0
                    ? $"Cool mist passes through the march; {healed} healed (+12 morale)."
                    : "Cool mist passes through the march (+12 morale).";
            }

            // Find reachable towns and let the player choose where the current takes them.
            List<Settlement> nearby = null;
            try
            {
                Vec2 pos = party.GetPosition2D;
                nearby = Settlement.All
                    .Where(s => s.IsTown && s.Town != null)
                    .Select(s => (s, dist: (s.GetPosition2D - pos).Length))
                    .Where(t => t.dist > 1f && t.dist < 32f)
                    .OrderBy(t => t.dist)
                    .Take(5)
                    .Select(t => t.s)
                    .ToList();
            }
            catch { }

            if (nearby == null || nearby.Count == 0)
            {
                // No waterway leads anywhere close: partial effect instead.
                int healed = HealWounded(party, 10);
                try { party.RecentEventsMorale += 12f; } catch { }
                Msg("The water stirs but finds no path from here. It soothes what it can."
                    + (healed > 0 ? $" {healed} wounds close." : "") + " (+12 morale)", NatureColor);
                return "";
            }

            var options = nearby
                .Select(s => new InquiryElement(s, s.Name.ToString(), null, true, ""))
                .ToList();

            try
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "Still Waters — the current shows a path",
                    "The water in the ground stirs. A current runs beneath your feet and for a moment " +
                    "you see — as in a still pool — the roads between here and there. Where do you go?",
                    options, true, 1, 1, "Ride the current", "Stay",
                    chosen =>
                    {
                        if (chosen == null || chosen.Count == 0) return;
                        var dest = (Settlement)chosen[0].Identifier;
                        try
                        {
                            var main = MobileParty.MainParty;
                            main.Position = dest.GatePosition;
                            try { main.SetMoveGoToSettlement(dest, MobileParty.NavigationType.Default, false); } catch { }
                        }
                        catch { }
                        Msg($"The current carries you. You arrive at {dest.Name} before the mist has fully lifted.", NatureColor);
                    },
                    _ => Msg("The current stills. You chose not to follow it. Your charge is spent.", NatureColor),
                    "", false), false, true);
            }
            catch
            {
                // Inquiry failed: carry to nearest.
                var dest = nearby[0];
                try
                {
                    var main = MobileParty.MainParty;
                    main.Position = dest.GatePosition;
                    try { main.SetMoveGoToSettlement(dest, MobileParty.NavigationType.Default, false); } catch { }
                }
                catch { }
                Msg($"The current carries you to {dest.Name}.", NatureColor);
            }
            return "";
        }

        // Storm — lightning cracks the sky three times; the thunder enters the soldiers' chests.
        // Player: morale +40, nearby enemy parties lose 20 morale (the sky is not subtle).
        private static string CampaignThundersEdge(MobileParty party, bool isPlayer)
        {
            try { party.RecentEventsMorale += isPlayer ? 40f : 25f; } catch { }
            if (isPlayer)
            {
                DemoralizeNearbyEnemies(20f, 18f);
                return "Three bolts strike the earth within thirty paces — crack, crack, crack — the sound is not thunder but something you feel in your sternum. The air turns sharp with ozone. Your soldiers stand rigid for one second. Then they roar, every one of them, and the sound carries. Nearby enemies falter. (+40 morale, enemies shaken)";
            }
            return "The storm fills the march with iron courage (+25 morale).";
        }

        private static void DemoralizeNearbyEnemies(float moraleDrain, float mapRadius)
        {
            try
            {
                Vec2 centre = MobileParty.MainParty?.GetPosition2D ?? Vec2.Zero;
                var playerFaction = Hero.MainHero?.MapFaction;
                if (playerFaction == null) return;
                foreach (MobileParty mp in MobileParty.All.ToList())
                {
                    if (mp == null || !mp.IsActive || mp.IsMainParty) continue;
                    bool hostile = false;
                    try { hostile = mp.MapFaction != null && mp.MapFaction.IsAtWarWith(playerFaction); } catch { continue; }
                    if (!hostile) continue;
                    if ((mp.GetPosition2D - centre).Length > mapRadius) continue;
                    try { mp.RecentEventsMorale -= moraleDrain; } catch { }
                }
            }
            catch { }
        }

        private static int HealWounded(MobileParty party, int count)
        {
            int healed = 0;
            try
            {
                var roster = party.MemberRoster;
                int remaining = count;
                foreach (var elem in roster.GetTroopRoster().ToList())
                {
                    if (remaining <= 0) break;
                    if (elem.WoundedNumber <= 0) continue;
                    int toHeal = Math.Min(elem.WoundedNumber, remaining);
                    roster.AddToCounts(elem.Character, 0, false, -toHeal, 0);
                    healed   += toHeal;
                    remaining -= toHeal;
                }
            }
            catch { }
            return healed;
        }

        // ── Tick ────────────────────────────────────────────────────────────────
        public static void MissionTick(float dt)
        {
            TickSpeedTokens(dt);
            TickResistTokens(dt);
        }

        private static void TickSpeedTokens(float dt)
        {
            foreach (int key in _speedTokens.Keys.ToList())
            {
                var (remaining, agent) = _speedTokens[key];
                remaining -= dt;
                if (remaining <= 0f || agent == null || !agent.IsActive())
                {
                    try { agent?.SetMaximumSpeedLimit(10f, false); } catch { }
                    _speedTokens.Remove(key);
                }
                else _speedTokens[key] = (remaining, agent);
            }
        }

        private static void TickResistTokens(float dt)
        {
            foreach (int key in _resistTokens.Keys.ToList())
            {
                var (frac, remaining, agent) = _resistTokens[key];
                remaining -= dt;
                if (remaining <= 0f) _resistTokens.Remove(key);
                else _resistTokens[key] = (frac, remaining, agent);
            }
        }

        public static float ApplyResistance(Agent agent, float incomingDamage)
        {
            if (agent == null) return incomingDamage;
            if (!_resistTokens.TryGetValue(agent.Index, out var tok)) return incomingDamage;
            return incomingDamage * (1f - tok.fraction);
        }

        public static bool HasResist(Agent agent)
            => agent != null && _resistTokens.ContainsKey(agent.Index);

        public static void ClearBattleState()
        {
            foreach (var (_, agent) in _speedTokens.Values)
                try { agent?.SetMaximumSpeedLimit(10f, false); } catch { }
            _speedTokens.Clear();
            _resistTokens.Clear();
        }

        // ── Helpers ─────────────────────────────────────────────────────────────
        private static void ApplyDamage(Agent target, Agent source, float amount, DamageTypes dmgType)
        {
            if (target == null || !target.IsActive() || target.Health <= 0f) return;
            try
            {
                target.Health -= amount;
                if (target.Health <= 0f) target.Die(new Blow(source?.Index ?? -1));
            }
            catch { }
        }

        internal static void ApplySpeedToken(Agent agent, float mult, float seconds)
        {
            if (agent == null || !agent.IsActive()) return;
            try { agent.SetMaximumSpeedLimit(mult == 0f ? 0f : 10f * mult, false); } catch { }
            _speedTokens[agent.Index] = (seconds, agent);
        }

        private static void ApplyResistToken(Agent agent, float fraction, float seconds)
        {
            if (agent == null) return;
            _resistTokens[agent.Index] = (fraction, seconds, agent);
        }

        private static Agent NearestEnemy(Agent caster, Vec3 pos, float range, Team team)
        {
            Agent nearest = null;
            float bestDist = range * range;
            try
            {
                foreach (Agent a in Mission.Current.Agents)
                {
                    if (!a.IsActive() || a.IsMount || a == caster || a.Health <= 0f) continue;
                    bool enemy = false;
                    try { enemy = team != null && team.IsEnemyOf(a.Team); } catch { continue; }
                    if (!enemy) continue;
                    float d = (a.Position - pos).LengthSquared;
                    if (d < bestDist) { bestDist = d; nearest = a; }
                }
            }
            catch { }
            return nearest;
        }

        private static void ForEachEnemyInRadius(Vec3 pos, float radius, Team team, Action<Agent> action)
        {
            float r2 = radius * radius;
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount || a.Health <= 0f) continue;
                    bool enemy = false;
                    try { enemy = team != null && team.IsEnemyOf(a.Team); } catch { continue; }
                    if (!enemy) continue;
                    if ((a.Position - pos).LengthSquared <= r2) try { action(a); } catch { }
                }
            }
            catch { }
        }

        private static void ForEachAllyInRadius(Vec3 pos, float radius, Agent exclude, Team team, Action<Agent> action)
        {
            float r2 = radius * radius;
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (!a.IsActive() || a.IsMount || a == exclude || a.Health <= 0f) continue;
                    if (a.Team != team) continue;
                    if ((a.Position - pos).LengthSquared <= r2) try { action(a); } catch { }
                }
            }
            catch { }
        }

        private static readonly Color NatureColor = new Color(0.35f, 0.75f, 0.35f);

        private static void Msg(string text, Color color)
            => InformationManager.DisplayMessage(new InformationMessage(text, color));
    }
}
