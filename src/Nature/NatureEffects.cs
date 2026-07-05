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
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static class NatureEffects
    {
        private static readonly Random _rng = new Random();

        // Speed-limit tokens (agent index → remaining seconds + the absolute speed cap
        // to RE-APPLY every frame; the engine recomputes each agent's limit each tick,
        // so a one-shot SetMaximumSpeedLimit is wiped almost immediately).
        private static readonly Dictionary<int, (float remaining, Agent agent, float speed)> _speedTokens
            = new Dictionary<int, (float, Agent, float)>();
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
        // Scoped power multiplier for the current cast — set by the unified element
        // magic so a short draw looses a weaker working and a full draw a stronger
        // one. 1.0 for NPC seers and the old nature path (unchanged behaviour).
        private static float _castPower = 1f;

        public static void ExecuteNpc(NaturePower power, Agent caster, Team casterTeam)
            => ExecuteNpc(power, caster, casterTeam, 1f);

        public static void ExecuteNpc(NaturePower power, Agent caster, Team casterTeam, float castPower)
        {
            if (power == NaturePower.None || caster == null || !caster.IsActive()) return;
            _castPower = castPower <= 0f ? 0.01f : castPower;
            try { ExecuteBattleCore(power, caster, casterTeam); }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            finally { _castPower = 1f; }
        }

        // ── Battle ──────────────────────────────────────────────────────────────
        private static bool ExecuteBattle(NaturePower power, Agent caster)
        {
            try { ExecuteBattleCore(power, caster, caster.Team); return true; }
            catch { return false; }
        }

        // Which castable element the current battle power counts as — read by
        // ApplyDamage so the Kindled's weakness knows what struck it. Mission-
        // scoped, set only for the duration of one power's resolution.
        private static MagicElement? _currentAttackElement = null;

        private static MagicElement? ElementFor(NaturePower power)
        {
            switch (NatureMath.ElementOf(power))
            {
                case NatureElement.Wind:  return MagicElement.Wind;
                case NatureElement.Earth: return MagicElement.Earth;
                case NatureElement.Water: return MagicElement.Water;
                case NatureElement.Storm: return MagicElement.Wind;   // storm rides the wind
                default:                  return null;
            }
        }

        private static void ExecuteBattleCore(NaturePower power, Agent caster, Team team)
        {
            Vec3 pos = caster.Position;
            _currentAttackElement = ElementFor(power);
            try
            {
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
            try { SpellEffects.BeginAgentGlow(caster, ColorSchool.Nature, 2.5f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { SpawnElementVisual(NatureMath.ElementOf(power), pos); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            finally { _currentAttackElement = null; }
        }

        // Wind · Gale — a forward GUST: knockback + slow driven out in a broad wedge
        // the caster faces (a stream of driven air, not a 360° ring).
        private static void BattleGale(Agent caster, Vec3 pos, Team team)
        {
            Vec3 fwd = caster.LookDirection.NormalizedCopy();
            float halfAngle = NatureMath.GaleConeAngleDeg * 0.5f * (float)(Math.PI / 180.0);
            try { SpellEffects.SpawnNatureLine(pos, pos + fwd * NatureMath.GaleRadius, NatureElement.Wind, 1.6f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { SpawnEruptionCone(pos, fwd, NatureElement.Wind, NatureMath.GaleRadius); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            // On desert sand the gust drives a plume of stinging dust down its line.
            try
            {
                if (SpellEffects.SceneIsDesert())
                    for (int i = 1; i <= 5; i++)
                    {
                        Vec3 dp = pos + fwd * (NatureMath.GaleRadius * (i / 5f)) + new Vec3(0f, 0f, 0.2f);
                        SpellEffects.SpawnNatureBurst(dp, NatureElement.Earth, 0.8f);
                    }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            ForEachEnemyInRadius(pos, NatureMath.GaleRadius, team, enemy =>
            {
                Vec3 toEnemy = (enemy.Position - pos).NormalizedCopy();
                if (Vec3.DotProduct(fwd, toEnemy) < Math.Cos(halfAngle)) return;   // outside the gust
                // Walls of flame and standing water devour a gale that crosses them.
                try { if (ElementWallWards.BlocksPath(MagicElement.Wind, pos, enemy.Position, out _)) return; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                if (SpellEffects.IsWarded(enemy)) return;   // the golden ward holds
                ApplyDamage(enemy, caster, NatureMath.GaleDamage, DamageTypes.Invalid);
                try
                {
                    // The gust drives foes AHEAD of the caster, not merely outward.
                    Vec3 dir = (toEnemy + fwd).NormalizedCopy();
                    KnockbackAgent(enemy, enemy.Position + dir * NatureMath.GaleKnockback);
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                ApplySpeedToken(enemy, NatureMath.GaleSlowMult, NatureMath.GaleSlowSec * _castPower);
            });
        }

        // All barrier powers — place an elemental wall in front of the caster.
        private static void BattleBarrier(Agent caster, Vec3 pos, Team team, NatureElement el)
        {
            try { SpellEffects.SpawnNatureBarrier(pos, caster.LookDirection, el, team, _castPower); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            bool isPlayer = false;
            try { isPlayer = Agent.Main != null && caster == Agent.Main; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            if (isPlayer)
                Msg($"{NatureMath.PowerName(NatureMath.SupportPower(el))} — the land rises before you.", NatureColor);
        }

        // Earth · Entangle — a forward LINE of erupting roots: damage + immobilise
        // in a narrow ridge the caster faces (no longer a 360° AoE ring).
        private static void BattleEntangle(Agent caster, Vec3 pos, Team team)
        {
            Vec3 fwd = caster.LookDirection.NormalizedCopy();
            float halfAngle = NatureMath.EntangleConeAngleDeg * 0.5f * (float)(Math.PI / 180.0);
            try { SpellEffects.SpawnNatureLine(pos, pos + fwd * NatureMath.EntangleRange, NatureElement.Earth, 2.5f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { SpawnEruptionCone(pos, fwd, NatureElement.Earth, NatureMath.EntangleRange); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            ForEachEnemyInRadius(pos, NatureMath.EntangleRange, team, enemy =>
            {
                Vec3 toEnemy = (enemy.Position - pos).NormalizedCopy();
                if (Vec3.DotProduct(fwd, toEnemy) < Math.Cos(halfAngle)) return;   // off the line of roots
                // A wall of driven wind scatters flung stone before it lands.
                try { if (ElementWallWards.BlocksPath(MagicElement.Earth, pos, enemy.Position, out _)) return; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                if (SpellEffects.IsWarded(enemy)) return;   // the golden ward holds
                ApplyDamage(enemy, caster, NatureMath.EntangleDamage, DamageTypes.Blunt);
                try { enemy.SetMaximumSpeedLimit(0f, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                ApplySpeedToken(enemy, 0f, NatureMath.EntangleRootSec * _castPower);   // held in place (root scales with draw)
                try { SpellEffects.SpawnNatureBurst(enemy.Position, NatureElement.Earth, 2.0f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            });
            ApplySpeedToken(caster, 0f, NatureMath.EntangleStaggerSec);
        }

        // Water · Torrent — forward cone: damage + knockback that breaks formation.
        private static void BattleTorrent(Agent caster, Vec3 pos, Team team)
        {
            Vec3 fwd = caster.LookDirection.NormalizedCopy();
            float halfAngle = NatureMath.TorrentAngleDeg * 0.5f * (float)(Math.PI / 180.0);
            try { SpellEffects.SpawnNatureLine(pos, pos + fwd * NatureMath.TorrentRange, NatureElement.Water, 2.0f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { SpawnEruptionCone(pos, fwd, NatureElement.Water, NatureMath.TorrentRange); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // The wave puts out fire along its path — burning ground boils to
            // steam (and any fire-wall warding there dies), burning men are doused.
            try
            {
                SpellEffects.QuenchFireAt(pos + fwd * 3f, 3.5f);
                SpellEffects.QuenchFireAt(pos + fwd * 6f, 3.5f);
                SpellEffects.QuenchFireAt(pos + fwd * NatureMath.TorrentRange, 3.5f);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            ForEachEnemyInRadius(pos, NatureMath.TorrentRange, team, enemy =>
            {
                Vec3 toEnemy = (enemy.Position - pos).NormalizedCopy();
                if (Vec3.DotProduct(fwd, toEnemy) < Math.Cos(halfAngle)) return;
                // A standing dam of stone breaks the wave before it strikes.
                try { if (ElementWallWards.BlocksPath(MagicElement.Water, pos, enemy.Position, out _)) return; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { ElementSpellEffects.QuenchIgnition(enemy); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                if (SpellEffects.IsWarded(enemy)) return;   // the golden ward holds
                ApplyDamage(enemy, caster, NatureMath.TorrentDamage, DamageTypes.Invalid);
                try { KnockbackAgent(enemy, enemy.Position + toEnemy * NatureMath.TorrentKnockback); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                ApplySpeedToken(enemy, NatureMath.TorrentSlowMult, NatureMath.TorrentSlowSec * _castPower);
            });
        }

        // Storm · Thunderclap — bolt to nearest enemy, chaining to a couple more.
        private static void BattleThunderClap(Agent caster, Vec3 pos, Team team)
        {
            Agent primary = NearestEnemy(caster, pos, NatureMath.ThunderRange, team);
            if (primary == null) return;

            ApplyDamage(primary, caster, NatureMath.ThunderDamage, DamageTypes.Invalid);
            try { primary.SetMaximumSpeedLimit(0f, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            ApplySpeedToken(primary, 0f, NatureMath.ThunderStunSec);
            try { SpellEffects.SpawnTempLightWhite(primary.Position + new Vec3(0f, 0f, 1f), 10f, 0.3f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            try { SpellEffects.SpawnNatureBurst(primary.Position + new Vec3(0f, 0f, 1f), NatureElement.Storm, 1.0f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            int chains = 0;
            ForEachEnemyInRadius(primary.Position, NatureMath.ThunderChainRadius, team, chain =>
            {
                if (chains >= NatureMath.ThunderChainCount || chain == primary) return;
                ApplyDamage(chain, caster, NatureMath.ThunderChainDamage, DamageTypes.Invalid);
                try { SpellEffects.SpawnTempLightWhite(chain.Position + new Vec3(0f, 0f, 1f), 7f, 0.25f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { SpellEffects.SpawnNatureBurst(chain.Position + new Vec3(0f, 0f, 1f), NatureElement.Storm, 0.9f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                chains++;
            });
        }

        // Bright eruption lighting for an AoE attack: a ring of element-coloured
        // light bursts plus a central flash, so the strike visibly erupts outward
        // rather than only flashing one-shot debris. Lights are guaranteed to render.
        private static void SpawnEruptionRing(Vec3 pos, NatureElement el, float radius)
        {
            Vec3 rgb = SpellEffects.NatureElementRgb(el);
            SpellEffects.SpawnTempLightRgb(pos + new Vec3(0f, 0f, 1.0f), rgb, radius + 6f, 0.45f); // central flash
            int n = 10;
            for (int i = 0; i < n; i++)
            {
                double a = Math.PI * 2.0 / n * i;
                Vec3 lp = pos + new Vec3((float)Math.Cos(a) * radius, (float)Math.Sin(a) * radius, 0.8f);
                SpellEffects.SpawnTempLightRgb(lp, rgb, 4.5f, 0.6f);
                try { SpellEffects.SpawnNatureBurst(lp, el, 0.7f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        // Bright eruption lighting for a forward cone attack: a line of element-
        // coloured light bursts running out along the strike direction.
        private static void SpawnEruptionCone(Vec3 pos, Vec3 fwd, NatureElement el, float range)
        {
            Vec3 rgb = SpellEffects.NatureElementRgb(el);
            SpellEffects.SpawnTempLightRgb(pos + new Vec3(0f, 0f, 1.0f), rgb, 10f, 0.45f); // muzzle flash
            int steps = 5;
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                Vec3 lp = pos + fwd * (range * t) + new Vec3(0f, 0f, 0.8f);
                SpellEffects.SpawnTempLightRgb(lp, rgb, 4.5f, 0.6f);
                try { SpellEffects.SpawnNatureBurst(lp, el, 0.7f); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
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
                    SpellEffects.SpawnTempLightRgb(pos + up,  new Vec3(0.60f, 0.66f, 0.26f),  9f, 2.5f);
                    SpellEffects.SpawnTempLightRgb(pos,       new Vec3(0.45f, 0.36f, 0.16f),  5f, 1.5f);
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
            try { isMain = party.IsMainParty; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
            try { party.RecentEventsMorale += 10f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            if (!isPlayer)
                return "The wind steadies the march (+10 morale).";

            int foodLost = RemoveFoodFromRoster(party, 15);
            string costLine = foodLost > 0 ? $" [{foodLost} food scattered]" : "";

            // Try to push the party toward their current target settlement.
            bool advanced = false;
            try
            {
                Settlement target = null;
                try { target = MobileParty.MainParty.TargetSettlement; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                        party.Position = new CampaignVec2(newPos, true);
                        advanced = true;
                        return $"The wind fills the column's banners and presses the march forward — " +
                               $"several leagues closer to {target.Name}, and the road seems shorter.{costLine} (+10 morale)";
                    }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

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
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                return $"The wind goes out ahead and comes back knowing things.{scouted}{costLine} (+10 morale)";
            }
            return $"The wind stirs the column.{costLine} (+10 morale)";
        }

        // Earth — the deep roots find the nearest village and swell its hearth.
        // UPSIDE:  nearest village gains +50 hearth (prosperity).
        // DOWNSIDE: Hero loses 15 HP — the roots take from the nearest living vessel.
        private static string CampaignRootMend(MobileParty party, bool isPlayer)
        {
            try { party.RecentEventsMorale += 8f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            if (!isPlayer)
                return "The earth stirs and steadies the march (+8 morale).";

            // Find the nearest village settlement.
            Settlement nearest = null;
            try
            {
                Vec2 pos = party.GetPosition2D;
                float best = float.MaxValue;
                foreach (var s in Settlement.All)
                {
                    if (s == null || !s.IsVillage || s.Village == null) continue;
                    float d = (s.GetPosition2D - pos).LengthSquared;
                    if (d < best) { best = d; nearest = s; }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            int hearthGain = 0;
            string villageName = "";
            if (nearest != null)
            {
                try
                {
                    villageName = nearest.Name?.ToString() ?? "";
                    nearest.Village.Hearth += 50f;
                    hearthGain = 50;
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }

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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            string titeLine = hpDrained > 0 ? $" [{hpDrained} HP taken as tithe]" : "";
            string hearthLine = hearthGain > 0
                ? $" The village of {villageName} will know a prosperous season."
                : "";
            return $"The roots go deep and give what they find.{hearthLine} The earth takes its share from you in return.{titeLine} (+8 morale)";
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
                try { party.RecentEventsMorale += 10f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

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
                            try { main.SetMoveGoToSettlement(dest, MobileParty.NavigationType.Default, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        try { MobileParty.MainParty.RecentEventsMorale -= 20f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                    try { main.SetMoveGoToSettlement(dest, MobileParty.NavigationType.Default, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                try { MobileParty.MainParty.RecentEventsMorale -= 20f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                Msg($"The current carries you to {dest.Name}. [-20 morale]", NatureColor);
            }
            return "";
        }

        // Storm — three bolts crack the sky; roars from your men, fear in the enemy.
        // UPSIDE:  +35 morale, nearby hostile parties lose 20 morale.
        // DOWNSIDE: 2–3 of your weakest soldiers are struck and wounded.
        private static string CampaignThundersEdge(MobileParty party, bool isPlayer)
        {
            try { party.RecentEventsMorale += isPlayer ? 35f : 20f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                    try { mp.RecentEventsMorale -= moraleDrain; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                var (remaining, agent, speed) = _speedTokens[key];
                remaining -= dt;
                if (remaining <= 0f || agent == null || !agent.IsActive())
                {
                    try { agent?.SetMaximumSpeedLimit(10f, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    _speedTokens.Remove(key);
                }
                else
                {
                    // Re-apply each frame, or the engine's per-tick recompute wipes it
                    // (a root would visibly "do nothing" despite the message firing).
                    try { agent.SetMaximumSpeedLimit(speed, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    _speedTokens[key] = (remaining, agent, speed);
                }
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
            foreach (var (_, agent, _) in _speedTokens.Values)
                try { agent?.SetMaximumSpeedLimit(10f, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            _speedTokens.Clear();
            _resistTokens.Clear();
        }

        // ── Helpers ─────────────────────────────────────────────────────────────
        private static void ApplyDamage(Agent target, Agent source, float amount, DamageTypes dmgType)
        {
            if (target == null || !target.IsActive() || target.Health <= 0f) return;
            amount *= _castPower;   // scale by the current cast's draw-power
            // Route through the canonical spell-damage path so the elements obey
            // the same laws as fire: heroes are floored (never magicked dead
            // outright), Cinder Shell / Sunder modify the hit, kill credit flows.
            try { SpellEffects.DamageAgent(target, amount, null, source, _currentAttackElement); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Mount-safe knockback. Teleporting a RIDER out from under his horse
        // desyncs the pair (a known crash/glitch class in this mod's history —
        // the freeze system already refuses to teleport mounted agents). A
        // mounted target is knocked back by moving the MOUNT the same distance;
        // the rider follows. Everyone else is moved directly.
        internal static void KnockbackAgent(Agent a, Vec3 newPos)
        {
            if (a == null) return;
            try
            {
                var mount = a.MountAgent;
                if (mount != null && mount.IsActive())
                {
                    Vec3 delta = newPos - a.Position; delta.z = 0f;
                    mount.TeleportToPosition(mount.Position + delta);
                }
                else
                    a.TeleportToPosition(newPos);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        internal static void ApplySpeedToken(Agent agent, float mult, float seconds)
        {
            if (agent == null || !agent.IsActive()) return;
            float speed = mult == 0f ? 0f : 10f * mult;
            try { agent.SetMaximumSpeedLimit(speed, false); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            _speedTokens[agent.Index] = (seconds, agent, speed);
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
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                    if ((a.Position - pos).LengthSquared <= r2) try { action(a); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
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
                    if ((a.Position - pos).LengthSquared <= r2) try { action(a); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static readonly Color NatureColor = new Color(0.35f, 0.75f, 0.35f);

        private static void Msg(string text, Color color)
            => InformationManager.DisplayMessage(new InformationMessage(text, color));
    }
}
