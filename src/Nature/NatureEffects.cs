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

        // Wind — fills the banners and pushes the column forward along the road.
        // UPSIDE:  advances party ~6 map units toward their current target settlement.
        //          If no target is set, scouts hostile parties within 50 units instead.
        // DOWNSIDE: ~15 food scatters in the gust.
        private static string CampaignWindward(MobileParty party, bool isPlayer)
        {
            try { party.RecentEventsMorale += 10f; } catch { }
            if (!isPlayer)
                return "The wind steadies the march (+10 morale).";

            int foodLost = RemoveFoodFromRoster(party, 15);
            string costLine = foodLost > 0 ? $" [{foodLost} food scattered]" : "";

            // Try to push the party toward their current target settlement.
            bool advanced = false;
            try
            {
                Settlement target = null;
                try { target = MobileParty.MainParty.TargetSettlement; } catch { }
                if (target != null)
                {
                    Vec2 cur  = party.GetPosition2D;
                    Vec2 dest = target.GetPosition2D;
                    Vec2 diff = dest - cur;
                    float len = diff.Length;
                    if (len > 1f)
                    {
                        Vec2 dir    = diff * (1f / len);
                        float push  = Math.Min(6f, len * 0.4f);  // never overshoot
                        Vec2 newPos = cur + dir * push;
                        party.Position = new CampaignVec2(newPos.x, newPos.y);
                        advanced = true;
                        return $"The wind fills the column's banners and presses the march forward — " +
                               $"several leagues closer to {target.Name}, and the road seems shorter.{costLine} (+10 morale)";
                    }
                }
            }
            catch { }

            // No movement target: scout the horizon instead.
            if (!advanced)
            {
                string scouted = "";
                try
                {
                    Vec2 pos = party.GetPosition2D;
                    var pf = Hero.MainHero?.MapFaction;
                    var enemies = MobileParty.All
                        .Where(mp => mp != null && mp.IsActive && !mp.IsMainParty
                            && mp.MapFaction != null && pf != null
                            && mp.MapFaction.IsAtWarWith(pf))
                        .Select(mp => (mp, dist: (mp.GetPosition2D - pos).Length))
                        .Where(t => t.dist < 50f)
                        .OrderBy(t => t.dist)
                        .Take(5)
                        .ToList();
                    scouted = enemies.Count > 0
                        ? " The wind returns with word: " +
                          string.Join("; ", enemies.Select(t =>
                              $"{t.mp.Name} (~{t.mp.Party.MemberRoster.TotalManCount} men)")) + "."
                        : " The wind finds no enemies within reach.";
                }
                catch { }
                return $"The wind goes out ahead and comes back knowing things.{scouted}{costLine} (+10 morale)";
            }
            return $"The wind stirs the column.{costLine} (+10 morale)";
        }

        // Earth — the forest floor opens its larder; roots mend and provisions emerge.
        // UPSIDE:  40 grain + 10 meat, 8 wounded healed.
        // DOWNSIDE: Hero loses 15 HP — the roots take from the nearest living vessel.
        private static string CampaignRootMend(MobileParty party, bool isPlayer)
        {
            int healed = HealWounded(party, isPlayer ? 8 : 6);
            try { party.RecentEventsMorale += 8f; } catch { }
            if (!isPlayer)
                return healed > 0
                    ? $"The earth provides: {healed} healed (+8 morale)."
                    : "The earth stirs and steadies the march (+8 morale).";

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

            // The tithe: roots draw from the most alive thing nearby.
            int hpDrained = 0;
            try
            {
                var h = Hero.MainHero;
                if (h != null)
                {
                    hpDrained = Math.Min(15, h.HitPoints - 5);   // never kill
                    if (hpDrained > 0) h.HitPoints -= hpDrained;
                }
            }
            catch { }

            string foodLine = (grain > 0 || meat > 0)
                ? $" The cooks are staring: {(grain > 0 ? $"{grain} grain" : "")}" +
                  $"{(grain > 0 && meat > 0 ? ", " : "")}{(meat > 0 ? $"{meat} meat" : "")}."
                : "";
            string titeLine = hpDrained > 0 ? $" [{hpDrained} HP taken as tithe]" : "";
            return healed > 0
                ? $"The forest floor shifts. Root-threads bring up what the earth has stored.{foodLine} {healed} wound{(healed > 1 ? "s have" : " has")} closed. The roots drink from you last.{titeLine} (+8 morale)"
                : $"The forest floor shifts. Root-threads bring up what the earth has stored.{foodLine} The roots drink from you last.{titeLine} (+8 morale)";
        }

        // Water — the sea-current carries the column to any coastal port.
        // REQUIRES: standing on water-adjacent terrain (river, shore, lake, coast).
        //           Only coastal port towns appear as destinations.
        // UPSIDE:   instant travel to any harbour in the world.
        // DOWNSIDE: -20 morale on arrival — soldiers wake cold and uncertain.
        // Returns "" — inquiry handles messaging directly.
        private static string CampaignStillWaters(MobileParty party, bool isPlayer)
        {
            if (!isPlayer)
            {
                int healed = HealWounded(party, 6);
                try { party.RecentEventsMorale += 10f; } catch { }
                return healed > 0
                    ? $"Cool mist passes through the march; {healed} healed (+10 morale)."
                    : "Cool mist passes through the march (+10 morale).";
            }

            // Gate: must be on water-adjacent terrain.
            bool nearWater = false;
            try
            {
                var terrain = Campaign.Current.MapSceneWrapper?.GetTerrainTypeAtPosition(party.Position);
                if (terrain.HasValue)
                {
                    string t = terrain.Value.ToString();
                    nearWater = t == "Water" || t == "ShallowRiver" || t == "River"
                             || t == "Lake"  || t == "Shore"       || t == "Swamp"
                             || t == "Wetland" || t == "Arctic";
                }
            }
            catch { }

            if (!nearWater)
            {
                Msg("Still Waters answers only where the land opens to water. " +
                    "Stand on a river, shore, or coast and try again.", NatureColor);
                return "";
            }

            // Find all resolved coastal port towns.
            List<Settlement> ports = null;
            try
            {
                Vec2 cur = party.GetPosition2D;
                ports = Settlement.All
                    .Where(s => s.IsTown && s.Town != null
                        && SeaCampaignBehavior.IsCoastalTown(s.Name?.ToString()?.Trim())
                        && (s.GetPosition2D - cur).Length > 2f)
                    .OrderBy(s => (s.GetPosition2D - cur).Length)
                    .ToList();
            }
            catch { }

            if (ports == null || ports.Count == 0)
            {
                Msg("The current stirs but finds no harbour it recognises. " +
                    "The sea lanes may not have opened yet.", NatureColor);
                return "";
            }

            var options = ports
                .Select(s => new InquiryElement(s, s.Name.ToString(), null, true, ""))
                .ToList();

            try
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "Still Waters — the sea knows the way",
                    "A current runs beneath your feet — cold, purposeful, tasting of salt. " +
                    "For a moment you see every harbour at once, as in a still pool. " +
                    "The water will carry you there. It will not be gentle.\n\n" +
                    "Your soldiers will arrive cold and unsure of where they are. [-20 morale on arrival]",
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
                        try { MobileParty.MainParty.RecentEventsMorale -= 20f; } catch { }
                        Msg($"The current delivers you to the harbour at {dest.Name}. " +
                            "Not all are sure how far they have come. [-20 morale]", NatureColor);
                    },
                    _ => Msg("The current stills. You chose not to follow it. Your charge is spent.", NatureColor),
                    "", false), false, true);
            }
            catch
            {
                var dest = ports[0];
                try
                {
                    var main = MobileParty.MainParty;
                    main.Position = dest.GatePosition;
                    try { main.SetMoveGoToSettlement(dest, MobileParty.NavigationType.Default, false); } catch { }
                }
                catch { }
                try { MobileParty.MainParty.RecentEventsMorale -= 20f; } catch { }
                Msg($"The current carries you to {dest.Name}. [-20 morale]", NatureColor);
            }
            return "";
        }

        // Storm — three bolts crack the sky; roars from your men, fear in the enemy.
        // UPSIDE:  +35 morale, nearby hostile parties lose 20 morale.
        // DOWNSIDE: 2–3 of your weakest soldiers are struck and wounded.
        private static string CampaignThundersEdge(MobileParty party, bool isPlayer)
        {
            try { party.RecentEventsMorale += isPlayer ? 35f : 20f; } catch { }
            if (!isPlayer)
                return "The storm fills the march with iron courage (+20 morale).";

            DemoralizeNearbyEnemies(20f, 18f);

            // The storm does not ask which side you fight for.
            int struck = WoundWeakestTroops(party, 2 + _rng.Next(2));

            string strikeLine = struck > 0
                ? $" The lightning does not sort its targets — {struck} of your own lie smoking. [-{struck} troops wounded]"
                : "";
            return $"Three bolts strike the earth within thirty paces. The air turns iron with ozone. Your soldiers roar. Nearby enemies falter.{strikeLine} (+35 morale, enemies shaken)";
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

        // Food item string IDs used in Bannerlord's base game.
        private static readonly string[] _foodIds =
            { "grain", "meat", "fish", "vegetables", "cheese", "bread", "dried_meat", "oil", "beer", "wine" };

        private static int RemoveFoodFromRoster(MobileParty party, int amount)
        {
            int removed = 0;
            try
            {
                int remaining = amount;
                foreach (string id in _foodIds)
                {
                    if (remaining <= 0) break;
                    var item = MBObjectManager.Instance?.GetObject<ItemObject>(id);
                    if (item == null) continue;
                    int have = 0;
                    try { have = party.ItemRoster.GetItemNumber(item); } catch { continue; }
                    if (have <= 0) continue;
                    int toRemove = Math.Min(have, remaining);
                    party.ItemRoster.AddToCounts(item, -toRemove);
                    removed   += toRemove;
                    remaining -= toRemove;
                }
            }
            catch { }
            return removed;
        }

        // Wounds the N weakest non-hero troops in the party. Returns actual count wounded.
        private static int WoundWeakestTroops(MobileParty party, int count)
        {
            int wounded = 0;
            try
            {
                var roster = party.MemberRoster;
                int remaining = count;
                foreach (var elem in roster.GetTroopRoster()
                    .Where(e => e.Character != null && !e.Character.IsHero
                                && e.Number > e.WoundedNumber)
                    .OrderBy(e => e.Character.Tier)
                    .ToList())
                {
                    if (remaining <= 0) break;
                    int toWound = Math.Min(remaining, elem.Number - elem.WoundedNumber);
                    try { roster.AddToCounts(elem.Character, 0, false, toWound, 0); } catch { continue; }
                    wounded   += toWound;
                    remaining -= toWound;
                }
            }
            catch { }
            return wounded;
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
