// =============================================================================
// ASH AND EMBER — BattleEvents.cs
// Randomised battlefield conditions that activate at mission start.
// Each event rolls independently; active ones run until the battle ends.
//
// ┌──────────────┬─────┬────────────────────────────────────────────────────────┐
// │ Event        │     │ Effect                                                 │
// ├──────────────┼─────┼────────────────────────────────────────────────────────┤
// │ Cinder Rain  │ 12s │ Scatters impartial patches of falling fire that burn  │
// │              │     │ on contact (they hurt whoever stands in them)          │
// │ The Rising   │ 20s │ Tier-1 units spawn on the Ashen side (Ashen battle    │
// │              │     │ only — skipped if no Ashen side detected)             │
// │ Dread        │ ×1  │ All non-Ashen agents lose 30 morale (fires once)      │
// │ Last Light   │ ×1  │ Sky drowns in dark fog; non-Ashen −20 morale, Ashen   │
// │              │     │ +15 and lit like beacons (fires once)                 │
// │ Ashen Ground │ 20s │ All mounted agents are dismounted                     │
// │ Frenzy       │ 20s │ Charge order issued to every formation on both sides  │
// │ The Kindling │ ×1  │ Raw magic wakes into elementals that join a side      │
// │ Storm        │  6s │ Constant gale; ranged weapons are all but useless     │
// │ Tremor       │ 12s │ The ground heaves — quakes stagger and blunt, and     │
// │              │     │ churn the field to bogging mud                        │
// │ Deluge       │  9s │ Drowning rain — fire dies, everyone wades, cold slow  │
// │ Madness      │ 10s │ One common unit on each side turns and joins the foe  │
// └──────────────┴─────┴────────────────────────────────────────────────────────┘
//
// DEBUGGING GUIDE:
//   • Active events are printed to the message log at battle start.
//     No events = no message (quiet battle).
//   • Add breakpoints in BuildActiveEvents() to trace rolls.
//   • _ashenTeam is set in FindAshenTeam() — null if no Ashen hero found in the
//     mission agents list. The Rising will silently skip if null.
//   • SpawnRisingUnits() wraps Mission.SpawnAgent inside try/catch; if the
//     troop CharacterObject is missing the spawn silently no-ops.
//   • Storm arms the engine missile-speed/range modifiers once at registration
//     and re-applies them each fire (idempotent). A fresh mission resets them.
//   • Rolls are LOCKED for a game day (RollLockDays) via _decisions/_decisionStamp
//     so retreating and re-entering a battle can't reroll a curse away. The lock
//     survives OnMissionEnd; it clears on a new/loaded game (ResetForNewGame) or
//     when the day lapses.
//   • All tuning constants (intervals, chances, damage) are public at the top.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static partial class BattleEvents
    {
        // ── Tuning constants ──────────────────────────────────────────────────
        // Per-battle activation probability for each event.
        // Total expected events per battle: ~0.35 → most battles are clean,
        // roughly 1-in-3 has one event, and a rare few carry two.
        public const float ChanceCinderRain    = 0.05f;
        public const float ChanceTheRising     = 0.06f; // only if Ashen side present
        public const float ChanceDread         = 0.04f;
        public const float ChanceLastLight     = 0.03f;
        public const float ChanceAshenGround   = 0.04f;
        public const float ChanceFrenzy        = 0.04f;
        public const float ChanceKindling      = 0.012f; // raw magic wakes into elementals — kept rare; 3 charged bodies swing a fight hard
        public const float ChanceStorm         = 0.04f;
        public const float ChanceTremor        = 0.04f;
        public const float ChanceDeluge        = 0.04f;
        public const float ChanceMadness       = 0.03f;

        public const float CinderRainInterval  = 12f;   // seconds between cinder-fall waves
        public const float TheRisingInterval   = 20f;
        public const float AshenGroundInterval = 20f;
        public const float FrenzyInterval      = 20f;
        public const float StormInterval       = 6f;    // gust cadence (missile choke re-applied each fire)
        public const float TremorInterval      = 12f;
        public const float DelugeInterval      = 9f;
        public const float MadnessInterval     = 10f;

        // One-shot events fire this many seconds after battle start
        public const float OneShotDelay        = 5f;

        public const float DreadMoralePenalty  = 30f;   // morale removed by Dread
        public const int   RisingSpawnCount    = 6;     // units per Rising tick

        // Cinder Rain — impartial patches of falling fire that burn on contact.
        public const int   CinderPatchDamage   = 1;     // damageCount → 8 HP/sec/patch (SpawnFirePatch)
        public const int   CinderPatchMin      = 5;     // patches scattered per wave (min..max)
        public const int   CinderPatchMax      = 8;
        public const float CinderSpread        = 35f;   // radius the wave scatters over

        // Storm — the engine missile modifiers are driven near zero so arrows and
        // bolts flop out of the air. A gale shoves a few fighters each gust.
        public const float StormMissileSpeed   = 0.12f; // ×base missile speed while the storm holds
        public const float StormMissileRange   = 0.10f; // ×base missile range
        public const float StormGustPush       = 1.5f;  // metres a caught fighter is shoved down-wind
        public const int   StormGustVictims    = 6;     // fighters shoved per gust

        // Tremor — the ground heaves: blunt quakes plus bogging mud.
        public const float TremorDamage        = 12f;   // blunt HP per quake to those in the radius
        public const float TremorRadius        = 5f;    // quake radius
        public const int   TremorQuakeMin      = 3;     // quakes per heave (min..max)
        public const int   TremorQuakeMax      = 5;
        public const float TremorSpread        = 35f;

        // Deluge — drowning rain: fire dies, everyone wades.
        public const float DelugeSlowMult      = 0.75f; // wading speed cap fraction
        public const float DelugeQuenchRadius  = 60f;   // one sweep douses the whole field

        // ── Per-battle state ──────────────────────────────────────────────────
        private static bool                  _initialized = false;
        private static Team                  _ashenTeam   = null;
        private static readonly List<RunningEvent> _active = new List<RunningEvent>();
        private static readonly Random       _rng         = new Random();

        // ── Visual atmosphere state ────────────────────────────────────────────
        // _skySet prevents periodic events from overriding a dramatic one-shot sky tint.
        private static bool       _skySet      = false;
        private static MethodInfo _setFogMethod = null;
        private static bool       _fogResolved  = false;

        // Storm — the direction the gale drives (a flat, horizontal unit vector),
        // rolled once when the storm is registered so every gust sweeps the same way.
        private static Vec3       _stormDir    = new Vec3(1f, 0f, 0f);

        // ── Roll memory (retreat-proofing) ──────────────────────────────────────
        // A player who dislikes an active event could retreat (ending the mission)
        // and re-engage to reroll it away. To stop that, the rolled event set is
        // LOCKED for a full game day: within that window every fresh mission
        // re-applies the SAME decisions instead of rolling again. These fields
        // survive OnMissionEnd on purpose (that is the whole point) and are only
        // cleared by a new/loaded game (ResetForNewGame) or by the day expiring.
        private static readonly Dictionary<string, bool> _decisions = new Dictionary<string, bool>();
        private static CampaignTime _decisionStamp = CampaignTime.Zero;
        private static bool         _decisionsValid = false;
        public  const  double       RollLockDays   = 1.0;   // events stick this long

        // Represents one active event running in a battle
        private class RunningEvent
        {
            public string Name;
            public float  Interval; // seconds between fires; 0 = fires exactly once
            public float  Timer;    // time until next fire
            public bool   Done;     // set true after a one-shot event fires
            public Action OnFire;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// Called every frame from MagicMissionBehavior.OnMissionTick(dt).
        public static void MissionTick(float dt)
        {
            if (!_initialized) Initialize();

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var evt = _active[i];
                if (evt.Done) { _active.RemoveAt(i); continue; }

                evt.Timer -= dt;
                if (evt.Timer > 0f) continue;

                try { evt.OnFire(); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

                if (evt.Interval <= 0f)
                    evt.Done = true;        // one-shot: remove after firing
                else
                    evt.Timer = evt.Interval; // repeating: reset countdown
            }
        }

        /// Called from MagicMissionBehavior.OnEndMission().
        public static void OnMissionEnd()
        {
            // Capture cast count before clearing — spell-heavy battles leave an echo
            int castCount = AgingSystem.MissionCastCount;
            AgingSystem.ClearMissionCasts();

            _active.Clear();
            _initialized = false;
            _ashenTeam   = null;
            _skySet      = false;

            // Battlefield echo: 3+ casts marks the ground — Ashen are drawn to it
            if (castCount >= 3 && MageKnowledge.IsMage)
            {
                try
                {
                    Vec2 pos = MobileParty.MainParty?.GetPosition2D ?? default(Vec2);
                    if (pos.x != 0f || pos.y != 0f)
                        CampaignMapEvents.SetBattleEcho(pos.x, pos.y);
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        // ── Initialization ────────────────────────────────────────────────────

        private static void Initialize()
        {
            _initialized = true;
            // Only activate in real combat — skip settlement scenes (tavern, keep, etc.)
            var m = Mission.Current;
            if (m == null || (!m.IsFieldBattle && !m.IsSiegeBattle && !m.IsSallyOutBattle && !m.IsNavalBattle))
                return;
            FindAshenTeam();
            BuildActiveEvents();
        }

        // Scan hero agents to find which team (if any) represents the Ashen side.
        // Player Ashen status comes from MageKnowledge.IsAshen;
        // NPC Ashen status comes from ColourLordRegistry.IsAshenLord.
        private static void FindAshenTeam()
        {
            _ashenTeam = null;
            if (Mission.Current == null) return;
            try
            {
                foreach (var agent in Mission.Current.Agents)
                {
                    if (!agent.IsHero) continue;
                    var hero = (agent.Character as CharacterObject)?.HeroObject;
                    if (hero == null) continue;
                    bool isAshen = hero == Hero.MainHero
                        ? MageKnowledge.IsAshen
                        : ColourLordRegistry.IsAshenLord(hero);
                    if (!isAshen) continue;
                    _ashenTeam = agent.Team;
                    break;
                }
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // Roll each event independently and register the active ones.
        // Announces active events in the message log.
        private static void BuildActiveEvents()
        {
            _active.Clear();
            var names = new List<string>();

            // Open (or renew) the day-long roll lock. On a fresh window the stored
            // decisions are wiped and each Rolled() call rolls anew; inside the
            // window a re-entered battle reuses the exact same decisions, so a
            // retreat-and-return cannot shake off (or reroll) an event.
            if (!DecisionsStillValid())
            {
                _decisions.Clear();
                try { _decisionStamp = CampaignTime.Now; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                _decisionsValid = true;
            }

            // ── Cinder Rain ───────────────────────────────────────────────────
            if (Rolled("CinderRain", ChanceCinderRain))
            {
                names.Add("Cinder Rain");
                Add("Cinder Rain", CinderRainInterval, FireCinderRain);
            }

            // ── The Rising (Ashen side only) ──────────────────────────────────
            // The Ashen gate is NOT memoised — it depends on who is on the field
            // this mission — but the underlying roll is, so re-entry is stable.
            if (_ashenTeam != null && Rolled("TheRising", ChanceTheRising))
            {
                names.Add("The Rising");
                Add("The Rising", TheRisingInterval, FireTheRising);
            }

            // ── Dread (one-shot) ──────────────────────────────────────────────
            if (Rolled("Dread", ChanceDread))
            {
                names.Add("Dread");
                AddOneShot("Dread", OneShotDelay, FireDread);
            }

            // ── Last Light (one-shot) ─────────────────────────────────────────
            if (Rolled("LastLight", ChanceLastLight))
            {
                names.Add("Last Light");
                AddOneShot("Last Light", OneShotDelay + 1f, FireLastLight);
            }

            // ── Ashen Ground ──────────────────────────────────────────────────
            if (Rolled("AshenGround", ChanceAshenGround))
            {
                names.Add("Ashen Ground");
                Add("Ashen Ground", AshenGroundInterval, FireAshenGround);
            }

            // ── Frenzy ────────────────────────────────────────────────────────
            if (Rolled("Frenzy", ChanceFrenzy))
            {
                names.Add("Frenzy");
                Add("Frenzy", FrenzyInterval, FireFrenzy);
            }

            // ── The Kindling (one-shot) ───────────────────────────────────────
            // Raw magic pooled in this field wakes into elemental beings that
            // join one side, shaped by the ground they rose from.
            if (Rolled("Kindling", ChanceKindling))
            {
                names.Add("The Kindling");
                AddOneShot("The Kindling", OneShotDelay + 2f, FireKindling);
            }

            // ── Storm ─────────────────────────────────────────────────────────
            // A gale rolls across the field. Arrows and bolts are driven from the
            // air the moment they leave the string — the missile choke is armed
            // now so the first volley already falls short.
            if (Rolled("Storm", ChanceStorm))
            {
                names.Add("Storm");
                double a = _rng.NextDouble() * Math.PI * 2.0;
                _stormDir = new Vec3((float)Math.Cos(a), (float)Math.Sin(a), 0f);
                ArmStormMissiles();
                Add("Storm", StormInterval, FireStorm);
            }

            // ── Tremor ────────────────────────────────────────────────────────
            if (Rolled("Tremor", ChanceTremor))
            {
                names.Add("Tremor");
                Add("Tremor", TremorInterval, FireTremor);
            }

            // ── Deluge ────────────────────────────────────────────────────────
            if (Rolled("Deluge", ChanceDeluge))
            {
                names.Add("Deluge");
                Add("Deluge", DelugeInterval, FireDeluge);
            }

            // ── Madness ───────────────────────────────────────────────────────
            if (Rolled("Madness", ChanceMadness))
            {
                names.Add("Madness");
                Add("Madness", MadnessInterval, FireMadness);
            }

            if (names.Count > 0)
                try { MBInformationManager.AddQuickInformation(new TextObject(
                    "The field is cursed — " + string.Join(", ", names) + ".")); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // True while the locked roll set is still fresh (same game day). Once it
        // lapses — or the campaign is gone — the next BuildActiveEvents rerolls.
        private static bool DecisionsStillValid()
        {
            if (!_decisionsValid) return false;
            try
            {
                if (Campaign.Current == null) return false;
                return (CampaignTime.Now - _decisionStamp).ToDays < RollLockDays;
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); return false; }
        }

        // Roll once per event key per lock window, then return the remembered
        // result for every re-entry until the window lapses.
        private static bool Rolled(string key, float chance)
        {
            if (_decisions.TryGetValue(key, out bool remembered)) return remembered;
            bool v = Roll(chance);
            _decisions[key] = v;
            return v;
        }

        // Wipe the roll lock on a new or loaded game — two campaigns in one process
        // both start near the same in-world date, so a stale stamp could otherwise
        // read as "still today" and carry the previous campaign's curses over.
        public static void ResetForNewGame()
        {
            _decisions.Clear();
            _decisionsValid = false;
            _decisionStamp  = CampaignTime.Zero;
        }

    }
}
