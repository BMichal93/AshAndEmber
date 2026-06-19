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
        private static readonly Dictionary<Agent, float> _berserk   = new Dictionary<Agent, float>();
        private static readonly Dictionary<Agent, float> _resist    = new Dictionary<Agent, float>();
        private static readonly Dictionary<Agent, float> _enfeeble  = new Dictionary<Agent, float>();
        private static readonly Dictionary<Agent, float> _dot       = new Dictionary<Agent, float>();
        private static readonly Dictionary<Agent, float> _petrify   = new Dictionary<Agent, float>();
        private static readonly Dictionary<Agent, float> _lifesteal = new Dictionary<Agent, float>();

        public static bool IsBerserk(Agent a)     => a != null && _berserk.TryGetValue(a, out float t)   && t > 0f;
        public static bool IsResistant(Agent a)   => a != null && _resist.TryGetValue(a, out float t)    && t > 0f;
        public static bool IsEnfeebled(Agent a)   => a != null && _enfeeble.TryGetValue(a, out float t)  && t > 0f;
        public static bool IsPetrified(Agent a)   => a != null && _petrify.TryGetValue(a, out float t)   && t > 0f;
        public static bool IsLifestealing(Agent a) => a != null && _lifesteal.TryGetValue(a, out float t) && t > 0f;

        public static void ClearBattleState()
        {
            _berserk.Clear(); _resist.Clear(); _enfeeble.Clear(); _dot.Clear();
            _petrify.Clear(); _lifesteal.Clear();
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
                if (TalentSystem.Has(TalentId.VolatileHarvest) && _rng.NextDouble() < 0.40)
                {
                    // Salvage: 40% chance the brew yields its clean effect instead.
                    Log(self, "a steady hand finds what set true inside the ruin — the brew holds.", false);
                    ApplyBattleEffect(self, type, true);
                    // Volatile burst: the remnant lashes the nearest enemy for 25 fire damage.
                    TryVolatileBurst(self);
                    return;
                }
                ApplyBattleBackfire(self, AlchemyMath.PickBackfire(_rng.NextDouble()), true);
                // Volatile Harvest: 30% of the self-wound is returned as a partial heal.
                if (TalentSystem.Has(TalentId.VolatileHarvest))
                    try { SpellEffects.HealAgent(self, SafeHealthLimit(self) * AlchemyMath.BackfireSelfWoundFraction * 0.30f); } catch { }
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
                    // Cleanse active debuffs — the draught burns them clean.
                    _dot.Remove(a);
                    _petrify.Remove(a);
                    if (_enfeeble.Remove(a)) try { a.SetMaximumSpeedLimit(1f, true); } catch { }
                    if (announce) Log(a, "drinks a Healing Draught — wounds close and the poison fades.", false);
                    break;

                case ElixirType.EmberBrew:
                    _berserk[a] = AlchemyMath.BerserkDurationSec;
                    Heal(a, AlchemyMath.BerserkSelfHeal);
                    try { SpellEffects.BeginAgentGlow(a, ColorSchool.Red, AlchemyMath.BerserkDurationSec); } catch { }
                    if (announce) Log(a, "drains an Ember Brew — they move like something unchained.", false);
                    break;

                case ElixirType.CausticVial:
                {
                    int causticHit = CausticBurst(a);
                    if (announce) Log(a, $"shatters a Caustic Vial — a searing cloud takes {causticHit}.", false);
                    break;
                }

                case ElixirType.StonebloodTonic:
                    _resist[a] = AlchemyMath.ResistDurationSec;
                    try { SpellEffects.BeginAgentGlow(a, ColorSchool.Ashen, AlchemyMath.ResistDurationSec); } catch { }
                    if (announce) Log(a, "swallows a Stoneblood Tonic — skin greys to slag; blows return to the hand that threw them.", false);
                    break;

                case ElixirType.VeilOfAsh:
                {
                    try { SpellEffects.ExecuteWardFromAgent(a); } catch { }
                    // The rising ash-cloud chills those who press in at the moment of activation.
                    int ashChilled = EnfeebleEnemiesInRadius(a, AlchemyMath.VeilAshSlowRadius, AlchemyMath.VeilAshSlowDuration, 0f);
                    if (announce) Log(a, $"breaks a Veil of Ash — grey ash rises and {ashChilled} of the enemy slow.", false);
                    break;
                }

                case ElixirType.HoarfrostDraught:
                {
                    int chilled = HoarfrostBurst(a);
                    if (announce) Log(a, $"drinks a Hoarfrost Draught — cold strikes {chilled} of the enemy and holds them.", false);
                    break;
                }

                case ElixirType.PyrebloodPhiltre:
                    // Lifesteal: wounds become fuel — every blow landed heals the drinker.
                    _lifesteal[a] = AlchemyMath.PyrebloodDurationSec;
                    try { SpellEffects.BeginAgentGlow(a, ColorSchool.White, AlchemyMath.PyrebloodDurationSec); } catch { }
                    if (announce) Log(a, "drinks a Pyreblood Philtre — wounds become fuel; every blow returns life.", false);
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
                case AlchemyBackfire.ScentOfBlood:
                {
                    float scentDmg = AlchemyMath.BackfireScentBleedFraction * SafeHealthLimit(a);
                    try { SpellEffects.DamageAgent(a, scentDmg, null, a); } catch { }
                    _enfeeble[a] = AlchemyMath.BackfireScentEnfeebleSec;
                    try { a.SetMaximumSpeedLimit(AlchemyMath.BackfireEnfeebleSpeedMult, true); } catch { }
                    try { SpellEffects.BeginAgentGlow(a, ColorSchool.Red, AlchemyMath.BackfireScentEnfeebleSec); } catch { }
                    if (announce) Log(a, "the brew opens their veins — blood, and every blade finds them.", true);
                    break;
                }
                case AlchemyBackfire.Petrification:
                    _petrify[a] = AlchemyMath.BackfirePetrifyDuration;
                    try { a.SetMaximumSpeedLimit(0f, true); } catch { }
                    try { SpellEffects.BeginAgentGlow(a, ColorSchool.Ashen, AlchemyMath.BackfirePetrifyDuration); } catch { }
                    if (announce) Log(a, "the brew crystallises in their veins — they cannot move!", true);
                    break;
                case AlchemyBackfire.AlchemicCorruption:
                {
                    float corruptDmg = AlchemyMath.BackfireCorruptSelfFraction * SafeHealthLimit(a);
                    try { SpellEffects.DamageAgent(a, corruptDmg, ColorSchool.Green, a); } catch { }
                    try { SpellEffects.BeginAgentGlow(a, ColorSchool.Green, 3f); } catch { }
                    int corruptIdx = TaintRandomVial();
                    if (corruptIdx >= 0)
                    {
                        string vialName = AlchemyCatalog.Name((ElixirType)AlchemyInventory._types[corruptIdx]);
                        if (announce) Log(a, $"the corruption spreads — their {vialName} is now tainted!", true);
                    }
                    else
                    {
                        // No clean vials — corruption turns back harder.
                        try { SpellEffects.DamageAgent(a, corruptDmg, ColorSchool.Green, a); } catch { }
                        if (announce) Log(a, "the corruption had nowhere to flee — it turns back, twice as vile.", true);
                    }
                    break;
                }
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
                if (TalentSystem.Has(TalentId.VolatileHarvest) && _rng.NextDouble() < 0.40)
                {
                    string salvaged = ApplyCampaignEffect(hero, party, type);
                    string line = "A steady hand finds what set true inside the ruin."
                        + (string.IsNullOrEmpty(salvaged) ? "" : "  " + salvaged);
                    InformationManager.DisplayMessage(new InformationMessage(line, new Color(0.6f, 0.8f, 0.55f)));
                    return;
                }
                ApplyCampaignBackfire(hero, party, AlchemyMath.PickBackfire(_rng.NextDouble()), announce: true);
                return;
            }

            string msg = ApplyCampaignEffect(hero, party, type);
            // DeeperSatchel: 25% chance the satchel refills with one clean vial of the same kind.
            if (TalentSystem.Has(TalentId.DeeperSatchel) && _rng.NextDouble() < 0.25 && AlchemyInventory.HasSpace())
            {
                AlchemyInventory.Add(type, tainted: false);
                string refill = "The satchel keeps its secret — a clean vial remains where the empty was.";
                msg = string.IsNullOrEmpty(msg) ? refill : msg + "  " + refill;
            }
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
                    HealHero(hero, AlchemyMath.OathWineHeroHeal);
                    return $"Oath-Wine passes down the line — spirits rise (+{AlchemyMath.OathWineMorale} morale) and strength returns to you.";

                case ElixirType.HearthsmokeCenser:
                    return BurnHearthsmoke(party);

                case ElixirType.FieldSurgeonPhiltre:
                {
                    int healed = HealWoundedTroops(party, AlchemyMath.SurgeonHealFraction);
                    return healed > 0
                        ? $"The Field Surgeon's Philtre does its work — {healed} of your wounded rise."
                        : "The Field Surgeon's Philtre is spent, but none were wounded to mend.";
                }

                case ElixirType.MarrowmendTincture:
                {
                    HealHero(hero, AlchemyMath.MarrowmendHealFraction);
                    int mended = HealWoundedTroops(party, AlchemyMath.MarrowmendTroopFraction);
                    return mended > 0
                        ? $"The Marrowmend Tincture works deep — you wake whole, and {mended} of your wounded rise with you."
                        : "The Marrowmend Tincture works deep — your wounds close and you wake whole.";
                }

                case ElixirType.KindlingCenser:
                    return BurnKindling(party);

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
                case AlchemyBackfire.ScentOfBlood:
                    HurtHero(hero, AlchemyMath.BackfireScentBleedFraction);
                    try { if (party != null) party.RecentEventsMorale -= AlchemyMath.BackfireMoraleDrop / 2; } catch { }
                    line = "The brew opens your veins — blood, and the wrong kind of attention on the road.";
                    break;
                case AlchemyBackfire.Petrification:
                    HurtHero(hero, AlchemyMath.BackfireSelfWoundFraction * 0.5f);
                    try { if (party != null) party.RecentEventsMorale -= AlchemyMath.BackfireMoraleDrop / 3; } catch { }
                    line = "The brew crystallises in your blood — briefly, you cannot breathe.";
                    break;
                case AlchemyBackfire.AlchemicCorruption:
                {
                    HurtHero(hero, AlchemyMath.BackfireCorruptSelfFraction);
                    int corruptIdx = TaintRandomVial();
                    if (corruptIdx >= 0)
                    {
                        string vialName = AlchemyCatalog.Name((ElixirType)AlchemyInventory._types[corruptIdx]);
                        line = $"The brew's corruption spreads outward — your {vialName} is now tainted.";
                    }
                    else
                    {
                        HurtHero(hero, AlchemyMath.BackfireCorruptSelfFraction);
                        line = "The corruption had nowhere to flee — it turned back on you instead.";
                    }
                    break;
                }
            }
            if (announce)
                InformationManager.DisplayMessage(new InformationMessage(line, new Color(0.8f, 0.35f, 0.25f)));
            return line;
        }

        // ── Mission tick: advance timers, apply damage-over-time ───────────────
        public static void MissionTick(float dt)
        {
            if (Mission.Current == null) return;

            DecayAndExpire(_berserk,   dt, null);
            DecayAndExpire(_resist,    dt, null);
            DecayAndExpire(_lifesteal, dt, null);
            DecayAndExpire(_enfeeble,  dt, a =>
            {
                try { a.SetMaximumSpeedLimit(1f, true); } catch { }
            });
            DecayAndExpire(_petrify, dt, a =>
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

            // Attacker state
            if (affector != null && affector != affected)
            {
                if (IsBerserk(affector))
                    try { SpellEffects.DamageAgent(affected, AlchemyMath.BerserkBonusDamage, null, affector); } catch { }
                else if (IsEnfeebled(affector))
                    try { SpellEffects.HealAgent(affected, inflicted * 0.3f); } catch { } // weakened blow

                if (IsLifestealing(affector))
                    try { SpellEffects.HealAgent(affector, inflicted * AlchemyMath.PyrebloodLifestealFraction); } catch { }
            }

            // Defender state — resist and enfeeble are mutually exclusive, petrify stacks on top
            if (IsResistant(affected))
            {
                try { SpellEffects.HealAgent(affected, inflicted * AlchemyMath.ResistFraction); } catch { }
                if (affector != null && affector != affected)
                    try { SpellEffects.DamageAgent(affector, inflicted * AlchemyMath.ResistReflectFraction, null, affected); } catch { }
            }
            else if (IsEnfeebled(affected))
                try { SpellEffects.DamageAgent(affected, inflicted * AlchemyMath.BackfireEnfeebleVuln, null, affector); } catch { }

            if (IsPetrified(affected))
                try { SpellEffects.DamageAgent(affected, inflicted * AlchemyMath.BackfirePetrifyVuln, null, affector); } catch { }
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

        // Bursts a caustic cloud around the drinker.
        //   alliesOnly=false (normal use): hits everyone in radius; enemies also receive a
        //     lingering blight DoT.
        //   alliesOnly=true (backfire): hits only the drinker's own team, no DoT.
        // Returns how many were struck.
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
                    // Apply blight DoT to enemies on intentional use only.
                    if (!alliesOnly && (center.Team == null || a.Team == null || a.Team != center.Team))
                        _dot[a] = AlchemyMath.CausticDotDuration;
                    hit++;
                }
            }
            catch { }
            try { SpellEffects.RecordMagicCast(pos); } catch { }
            return hit;
        }

        // Applies the hoarfrost/ash chill to nearby enemies: slows them, leaves them
        // open to extra damage, and optionally strikes with direct cold damage upfront.
        // Shared by HoarfrostDraught (full radius + direct damage) and VeilOfAsh
        // activation (small radius, no direct damage).
        private static int EnfeebleEnemiesInRadius(Agent center, float radius, float duration, float directDamage)
        {
            if (center == null || Mission.Current == null) return 0;
            int hit = 0;
            Vec3 pos;
            try { pos = center.Position; } catch { return 0; }
            float r2 = radius * radius;
            try
            {
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (a == center || !a.IsActive() || a.IsMount) continue;
                    if (center.Team == null || a.Team == null || a.Team == center.Team) continue;
                    float dx = a.Position.x - pos.x, dy = a.Position.y - pos.y;
                    if (dx * dx + dy * dy > r2) continue;
                    if (SpellEffects.IsWarded(a)) continue;
                    if (directDamage > 0f)
                        try { SpellEffects.DamageAgent(a, directDamage, ColorSchool.Ashen, center); } catch { }
                    _enfeeble[a] = duration;
                    try { a.SetMaximumSpeedLimit(AlchemyMath.BackfireEnfeebleSpeedMult, true); } catch { }
                    // Ashen: the one cold-blue glow — fitting for hoarfrost and ash alike.
                    try { SpellEffects.BeginAgentGlow(a, ColorSchool.Ashen, duration); } catch { }
                    hit++;
                }
            }
            catch { }
            try { SpellEffects.RecordMagicCast(pos); } catch { }
            return hit;
        }

        private static int HoarfrostBurst(Agent center)
            => EnfeebleEnemiesInRadius(center,
                   AlchemyMath.HoarfrostRadius,
                   AlchemyMath.HoarfrostDurationSec,
                   AlchemyMath.HoarfrostDirectDamage);

        // Taints one randomly chosen clean vial in the player's satchel.
        // Returns the index of the tainted vial, or -1 if none were clean.
        private static int TaintRandomVial()
        {
            var clean = new System.Collections.Generic.List<int>();
            for (int i = 0; i < AlchemyInventory._types.Count; i++)
                if (!AlchemyInventory._tainted[i]) clean.Add(i);
            if (clean.Count == 0) return -1;
            int idx = clean[_rng.Next(clean.Count)];
            AlchemyInventory._tainted[idx] = true;
            return idx;
        }

        // Volatile burst: lashes the nearest visible enemy for 25 fire damage on a salvage.
        private static void TryVolatileBurst(Agent self)
        {
            if (self == null || Mission.Current == null) return;
            try
            {
                Vec3 pos = self.Position;
                Agent nearest = null;
                float bestD2 = float.MaxValue;
                foreach (Agent a in Mission.Current.Agents.ToList())
                {
                    if (a == self || !a.IsActive() || a.IsMount) continue;
                    if (self.Team != null && a.Team == self.Team) continue;
                    float dx = a.Position.x - pos.x, dy = a.Position.y - pos.y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 < bestD2) { bestD2 = d2; nearest = a; }
                }
                if (nearest != null && !SpellEffects.IsWarded(nearest))
                {
                    try { SpellEffects.DamageAgent(nearest, 25f, ColorSchool.Red, self); } catch { }
                    Log(self, "the volatile remnant lashes out — 25 fire damage to the nearest foe.", false);
                }
            }
            catch { }
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

        private static string BurnKindling(MobileParty party)
        {
            if (party == null) return "There is no smoke without a fire to light it.";
            try
            {
                Settlement nearest = null;
                float best = AlchemyMath.KindlingRange * AlchemyMath.KindlingRange;
                Vec2 p = party.GetPosition2D;
                foreach (var s in Settlement.All)
                {
                    if (!s.IsTown || s.Town == null) continue;
                    float dx = s.GetPosition2D.x - p.x, dy = s.GetPosition2D.y - p.y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 < best) { best = d2; nearest = s; }
                }
                if (nearest == null)
                    return "The censer burns to ash, but no town lies near enough to feel it.";
                try { nearest.Town.Loyalty  = Math.Min(100f, nearest.Town.Loyalty  + AlchemyMath.KindlingLoyalty);  } catch { }
                try { nearest.Town.Security = Math.Min(100f, nearest.Town.Security + AlchemyMath.KindlingSecurity); } catch { }
                return $"Kindling-smoke drifts through {nearest.Name} — the people steady (+{(int)AlchemyMath.KindlingLoyalty} loyalty, +{(int)AlchemyMath.KindlingSecurity} security).";
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
