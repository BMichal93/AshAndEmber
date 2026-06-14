// =============================================================================
// ASH AND EMBER — Alchemy/AlchemyEffects.cs
//
// Applies elixir outcomes — both the intended effect and the tainted backfire —
// in two worlds:
//   • Battle  (Agent-based): heal, berserk, caustic burst, stone-skin, veil, and
//     the matching backfires. Timed buffs live in static dictionaries keyed by
//     Agent and are advanced by MissionTick / resolved in OnAgentHit, so there is
//     no per-frame property fighting (cheap, save-safe, mod-conflict safe).
//   • Campaign (Hero/party-based): hero heal, party morale, village hearth,
//     column field-surgery, and their backfires.
//
// All TaleWorlds access is null-guarded and wrapped in try/catch so a failed call
// degrades to a no-op rather than crashing a mission or a save. Magnitudes come
// from AlchemyMath so the tested numbers and the live behaviour never diverge.
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
    public static class AlchemyEffects
    {
        private static readonly Random _rng = new Random();

        // ── Battle buff/affliction timers (seconds remaining) ─────────────────
        private static readonly Dictionary<Agent, float> _berserk  = new Dictionary<Agent, float>();
        private static readonly Dictionary<Agent, float> _resist   = new Dictionary<Agent, float>();
        private static readonly Dictionary<Agent, float> _enfeeble = new Dictionary<Agent, float>();
        private static readonly Dictionary<Agent, float> _dot      = new Dictionary<Agent, float>();

        public static bool IsBerserk(Agent a) => a != null && _berserk.TryGetValue(a, out float t) && t > 0f;
        public static bool IsResistant(Agent a) => a != null && _resist.TryGetValue(a, out float t) && t > 0f;
        public static bool IsEnfeebled(Agent a) => a != null && _enfeeble.TryGetValue(a, out float t) && t > 0f;

        public static void ClearBattleState()
        {
            _berserk.Clear(); _resist.Clear(); _enfeeble.Clear(); _dot.Clear();
        }

        // ── Player consumption entry point ────────────────────────────────────
        // Validates context, removes one vial, then applies effect or backfire.
        // Returns true if a vial was actually spent.
        public static bool TryConsumePlayer(ElixirType type, bool inMission)
        {
            var def = AlchemyCatalog.Get(type);
            bool ok = inMission ? def.UsableInBattle : def.UsableOnMap;
            if (!ok)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    inMission ? $"The {def.Name} is no use in the press of battle."
                              : $"The {def.Name} must wait for the open field.",
                    new Color(0.8f, 0.75f, 0.55f)));
                return false;
            }

            if (!AlchemyInventory.Remove(type, out bool tainted)) return false;

            if (inMission) ConsumeInBattle(type, tainted);
            else           ConsumeOnMap(type, tainted);
            return true;
        }

        // ── Battle application (player) ───────────────────────────────────────
        private static void ConsumeInBattle(ElixirType type, bool tainted)
        {
            Agent self = Agent.Main;
            if (self == null || !self.IsActive())
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "There is no hand to raise the vial.", new Color(0.8f, 0.75f, 0.55f)));
                return;
            }

            if (tainted)
            {
                ApplyBattleBackfire(self, AlchemyMath.PickBackfire(_rng.NextDouble()), true);
                return;
            }

            ApplyBattleEffect(self, type, true);
        }

        // Applies a clean elixir's battle effect to any agent (player or NPC).
        // announce: post a combat-log line (used for the player; NPCs announce
        // themselves via AlchemyBattleAI).
        public static void ApplyBattleEffect(Agent a, ElixirType type, bool announce)
        {
            if (a == null || !a.IsActive()) return;
            switch (type)
            {
                case ElixirType.HealingDraught:
                    Heal(a, AlchemyMath.HealFraction);
                    if (announce) Log(a, "drinks a Healing Draught — wounds close.", false);
                    break;

                case ElixirType.EmberBrew:
                    _berserk[a] = AlchemyMath.BerserkDurationSec;
                    Heal(a, AlchemyMath.BerserkSelfHeal);
                    try { SpellEffects.BeginAgentGlow(a, ColorSchool.Red, AlchemyMath.BerserkDurationSec); } catch { }
                    if (announce) Log(a, "drains an Ember Brew — they move like something unchained.", false);
                    break;

                case ElixirType.CausticVial:
                    CausticBurst(a);
                    if (announce) Log(a, "shatters a Caustic Vial — a searing cloud blooms.", false);
                    break;

                case ElixirType.StonebloodTonic:
                    _resist[a] = AlchemyMath.ResistDurationSec;
                    try { SpellEffects.BeginAgentGlow(a, ColorSchool.Ashen, AlchemyMath.ResistDurationSec); } catch { }
                    if (announce) Log(a, "swallows a Stoneblood Tonic — their skin greys to slag.", false);
                    break;

                case ElixirType.VeilOfAsh:
                    try { SpellEffects.ExecuteWardFromAgent(a); } catch { }
                    if (announce) Log(a, "breaks a Veil of Ash — grey ash closes around them.", false);
                    break;

                default:
                    // Field-only elixir somehow reached battle: harmless no-op heal.
                    Heal(a, AlchemyMath.HealFraction);
                    break;
            }
        }

        // Applies a backfire to any agent in battle.
        public static void ApplyBattleBackfire(Agent a, AlchemyBackfire bf, bool announce)
        {
            if (a == null || !a.IsActive()) return;
            switch (bf)
            {
                case AlchemyBackfire.TroopBlast:
                {
                    int hit = CausticBurst(a, alliesOnly: true);
                    if (announce) Log(a, $"the brew bursts wrong — {hit} of their own are scalded!", true);
                    break;
                }
                case AlchemyBackfire.CreepingBlight:
                    _dot[a] = AlchemyMath.BackfireDotDurationSec;
                    try { SpellEffects.BeginAgentGlow(a, ColorSchool.Green, AlchemyMath.BackfireDotDurationSec); } catch { }
                    if (announce) Log(a, "the brew is poison — it begins to eat at them.", true);
                    break;
                case AlchemyBackfire.Enfeeblement:
                    _enfeeble[a] = AlchemyMath.BackfireEnfeebleDuration;
                    try { a.SetMaximumSpeedLimit(AlchemyMath.BackfireEnfeebleSpeedMult, true); } catch { }
                    if (announce) Log(a, "the brew sours — their limbs go leaden.", true);
                    break;
                case AlchemyBackfire.SelfWound:
                case AlchemyBackfire.MoraleCollapse: // no party morale mid-battle → wound
                default:
                {
                    float dmg = AlchemyMath.BackfireSelfWoundFraction * SafeHealthLimit(a);
                    try { SpellEffects.DamageAgent(a, dmg, null, a); } catch { }
                    if (announce) Log(a, "the brew turns on them — it burns from the inside.", true);
                    break;
                }
            }
        }

        // ── Campaign application (player) ─────────────────────────────────────
        private static void ConsumeOnMap(ElixirType type, bool tainted)
        {
            var party = MobileParty.MainParty;
            var hero  = Hero.MainHero;

            if (tainted)
            {
                ApplyCampaignBackfire(hero, party, AlchemyMath.PickBackfire(_rng.NextDouble()), announce: true);
                return;
            }

            string msg = ApplyCampaignEffect(hero, party, type);
            if (!string.IsNullOrEmpty(msg))
                InformationManager.DisplayMessage(new InformationMessage(msg, new Color(0.6f, 0.8f, 0.55f)));
        }

        // Applies a clean elixir on the map to a hero + their party (player or NPC).
        // Returns a short result line, or null.
        public static string ApplyCampaignEffect(Hero hero, MobileParty party, ElixirType type)
        {
            switch (type)
            {
                case ElixirType.HealingDraught:
                    HealHero(hero, AlchemyMath.HealFraction);
                    return "A Healing Draught — the worst of your wounds knit shut.";

                case ElixirType.OathWine:
                    try { if (party != null) party.RecentEventsMorale += AlchemyMath.OathWineMorale; } catch { }
                    return $"Oath-Wine passes down the line. Spirits rise (+{AlchemyMath.OathWineMorale} morale).";

                case ElixirType.HearthsmokeCenser:
                    return BurnHearthsmoke(party);

                case ElixirType.FieldSurgeonPhiltre:
                {
                    int healed = HealWoundedTroops(party, AlchemyMath.SurgeonHealFraction);
                    return healed > 0
                        ? $"The Field Surgeon's Philtre does its work — {healed} of your wounded rise."
                        : "The Field Surgeon's Philtre is spent, but none were wounded to mend.";
                }

                default:
                    return null;
            }
        }

        // Applies a backfire on the map. announce posts a player-facing line.
        public static string ApplyCampaignBackfire(Hero hero, MobileParty party, AlchemyBackfire bf, bool announce)
        {
            string line;
            switch (bf)
            {
                case AlchemyBackfire.TroopBlast:
                {
                    int wounded = WoundTroops(party, AlchemyMath.BackfireTroopBlastWounds);
                    line = $"The vial bursts in your hands — {wounded} of your own are scalded.";
                    break;
                }
                case AlchemyBackfire.MoraleCollapse:
                    try { if (party != null) party.RecentEventsMorale -= AlchemyMath.BackfireMoraleDrop; } catch { }
                    line = $"A foul reek rolls off the brew — the column's spirit breaks (−{AlchemyMath.BackfireMoraleDrop} morale).";
                    break;
                case AlchemyBackfire.CreepingBlight:
                case AlchemyBackfire.SelfWound:
                    HurtHero(hero, AlchemyMath.BackfireSelfWoundFraction);
                    line = "The brew is wrong. It turns to fire in your gut.";
                    break;
                case AlchemyBackfire.Enfeeblement:
                default:
                    HurtHero(hero, AlchemyMath.BackfireSelfWoundFraction * 0.5f);
                    try { if (party != null) party.RecentEventsMorale -= AlchemyMath.BackfireMoraleDrop / 2; } catch { }
                    line = "The brew leaves you reeling — weak in the knees and short of breath.";
                    break;
            }
            if (announce)
                InformationManager.DisplayMessage(new InformationMessage(line, new Color(0.8f, 0.35f, 0.25f)));
            return line;
        }

        // ── Mission tick: advance timers, apply damage-over-time ───────────────
        public static void MissionTick(float dt)
        {
            if (Mission.Current == null) return;

            DecayAndExpire(_berserk, dt, null);
            DecayAndExpire(_resist,  dt, null);
            DecayAndExpire(_enfeeble, dt, a =>
            {
                try { a.SetMaximumSpeedLimit(1f, true); } catch { }
            });

            if (_dot.Count > 0)
            {
                foreach (var a in _dot.Keys.ToList())
                {
                    float t = _dot[a] - dt;
                    if (a != null && a.IsActive())
                    {
                        try { SpellEffects.DamageAgent(a, AlchemyMath.BackfireDotPerSecond * dt, null, a); } catch { }
                    }
                    if (t <= 0f || a == null || !a.IsActive()) _dot.Remove(a);
                    else _dot[a] = t;
                }
            }
        }

        // Resolves alchemy modifiers when one agent strikes another. Called from
        // MagicMissionBehavior.OnAgentHit. Net effect on damage is applied by
        // healing/damaging the victim AFTER the blow has already landed — no need
        // to mutate the (in) Blow struct.
        public static void OnAgentHit(Agent affected, Agent affector, int inflicted)
        {
            if (affected == null || !affected.IsActive() || inflicted <= 0) return;

            // Attacker buffs/afflictions
            if (affector != null && affector != affected)
            {
                if (IsBerserk(affector))
                    try { SpellEffects.DamageAgent(affected, AlchemyMath.BerserkBonusDamage, null, affector); } catch { }
                else if (IsEnfeebled(affector))
                    try { SpellEffects.HealAgent(affected, inflicted * 0.3f); } catch { } // weakened blow
            }

            // Defender buffs/afflictions
            if (IsResistant(affected))
                try { SpellEffects.HealAgent(affected, inflicted * AlchemyMath.ResistFraction); } catch { }
            else if (IsBerserk(affected))
                try { SpellEffects.HealAgent(affected, inflicted * 0.20f); } catch { } // fury dulls pain
            else if (IsEnfeebled(affected))
                try { SpellEffects.DamageAgent(affected, inflicted * AlchemyMath.BackfireEnfeebleVuln, null, affector); } catch { }
        }

        // ── Battle helpers ────────────────────────────────────────────────────
        private static void Heal(Agent a, float fraction)
        {
            try { SpellEffects.HealAgent(a, SafeHealthLimit(a) * fraction); } catch { }
        }

        private static float SafeHealthLimit(Agent a)
        {
            try { return a.HealthLimit > 0f ? a.HealthLimit : 100f; } catch { return 100f; }
        }

        // Bursts a caustic cloud around the drinker. alliesOnly restricts it to the
        // drinker's own team (the backfire). Returns how many were struck.
        private static int CausticBurst(Agent center, bool alliesOnly = false)
        {
            if (center == null || Mission.Current == null) return 0;
            int hit = 0;
            Vec3 pos;
            try { pos = center.Position; } catch { return 0; }
            float r2 = AlchemyMath.CausticRadius * AlchemyMath.CausticRadius;
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (a == center || !a.IsActive() || a.IsMount) continue;
                    if (alliesOnly && (center.Team == null || a.Team != center.Team)) continue;
                    float dx = a.Position.x - pos.x, dy = a.Position.y - pos.y;
                    if (dx * dx + dy * dy > r2) continue;
                    if (SpellEffects.IsWarded(a)) continue;
                    try { SpellEffects.DamageAgent(a, AlchemyMath.CausticDamage, ColorSchool.Green, center); } catch { }
                    hit++;
                }
            }
            catch { }
            try { SpellEffects.RecordMagicCast(pos); } catch { }
            return hit;
        }

        private static void Log(Agent a, string blurb, bool bad)
        {
            try
            {
                if (Agent.Main != null && a.Team == Agent.Main.Team && a != Agent.Main)
                    return; // mirror spell behaviour: don't spam for allies
                string who = a == Agent.Main ? "You" : (AgentName(a) ?? "Someone");
                Color c = bad ? new Color(0.85f, 0.4f, 0.25f) : new Color(0.6f, 0.85f, 0.55f);
                InformationManager.DisplayMessage(new InformationMessage($"{who} — {blurb}", c));
            }
            catch { }
        }

        private static string AgentName(Agent a)
        {
            try { return a?.Name; } catch { return null; }
        }

        // ── Campaign helpers ──────────────────────────────────────────────────
        private static void HealHero(Hero hero, float fraction)
        {
            if (hero == null) return;
            try
            {
                int gain = (int)(hero.MaxHitPoints * fraction);
                hero.HitPoints = Math.Min(hero.MaxHitPoints, hero.HitPoints + Math.Max(1, gain));
            }
            catch { }
        }

        private static void HurtHero(Hero hero, float fraction)
        {
            if (hero == null) return;
            try
            {
                int loss = (int)(hero.MaxHitPoints * fraction);
                hero.HitPoints = Math.Max(1, hero.HitPoints - Math.Max(1, loss));
            }
            catch { }
        }

        private static int HealWoundedTroops(MobileParty party, float fraction)
        {
            if (party?.MemberRoster == null) return 0;
            int healed = 0;
            try
            {
                foreach (var e in party.MemberRoster.GetTroopRoster().ToList())
                {
                    if (e.Character.IsHero || e.WoundedNumber <= 0) continue;
                    int heal = Math.Max(1, (int)(e.WoundedNumber * fraction));
                    try { party.MemberRoster.AddToCounts(e.Character, 0, false, -heal); healed += heal; } catch { }
                }
            }
            catch { }
            return healed;
        }

        private static int WoundTroops(MobileParty party, int count)
        {
            if (party?.MemberRoster == null || count <= 0) return 0;
            int wounded = 0;
            try
            {
                foreach (var e in party.MemberRoster.GetTroopRoster().ToList())
                {
                    if (e.Character.IsHero) continue;
                    int healthy = e.Number - e.WoundedNumber;
                    int n = Math.Min(healthy, count - wounded);
                    if (n <= 0) continue;
                    try { party.MemberRoster.AddToCounts(e.Character, 0, false, n); wounded += n; } catch { }
                    if (wounded >= count) break;
                }
            }
            catch { }
            return wounded;
        }

        private static string BurnHearthsmoke(MobileParty party)
        {
            if (party == null) return "There is no smoke without a fire to light it.";
            try
            {
                Settlement nearest = null;
                float best = AlchemyMath.HearthsmokeRange * AlchemyMath.HearthsmokeRange;
                Vec2 p = party.GetPosition2D;
                foreach (var s in Settlement.All)
                {
                    if (!s.IsVillage || s.Village == null) continue;
                    float dx = s.GetPosition2D.x - p.x, dy = s.GetPosition2D.y - p.y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 < best) { best = d2; nearest = s; }
                }
                if (nearest == null)
                    return "The censer burns to ash, but no village lies near enough to feel it.";
                nearest.Village.Hearth += AlchemyMath.HearthsmokeBoost;
                return $"Hearthsmoke drifts over {nearest.Name} — the village prospers (+{(int)AlchemyMath.HearthsmokeBoost} hearth).";
            }
            catch { return "The censer burns to ash."; }
        }

        // ── shared ────────────────────────────────────────────────────────────
        private static void DecayAndExpire(Dictionary<Agent, float> map, float dt, Action<Agent> onExpire)
        {
            if (map.Count == 0) return;
            foreach (var a in map.Keys.ToList())
            {
                float t = map[a] - dt;
                if (t <= 0f || a == null || !a.IsActive())
                {
                    if (a != null && onExpire != null) onExpire(a);
                    map.Remove(a);
                }
                else map[a] = t;
            }
        }
    }
}
