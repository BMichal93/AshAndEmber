// =============================================================================
// ASH AND EMBER — SettlementEncounters.cs
// Random personal encounters triggered when the player enters or leaves a
// settlement. The system tracks Hero.MainHero.CurrentSettlement on the daily
// tick to detect transitions, then fires one encounter from an appropriate
// pool (gated by mage status, Ashen status, and renown).
//
// ┌─────────────────────────────┬───────────────────────┬──────────────────┐
// │ Event                       │ Trigger               │ Gate             │
// ├─────────────────────────────┼───────────────────────┼──────────────────┤
// │ A Mother's Plea             │ Leave village         │ Mage             │
// │ The Widow's Pyre            │ Leave village         │ Mage             │
// │ Signal Fire                 │ Leave village         │ Mage             │
// │ The Elder's Sending         │ Leave village         │ Mage             │
// │ Beggar at the Crossroads    │ Leave village         │ General          │
// │ The Lame Horse              │ Leave village         │ General          │
// │ The Coin Game               │ Leave village         │ General          │
// │ Torches at Dusk             │ Leave village         │ General          │
// │ The Eager Recruit           │ Leave village         │ General          │
// │ The Festival Farewell       │ Leave village         │ General          │
// │ The Hollow Hour             │ Leave village         │ General          │
// │ The Old Flame-Seer          │ Enter village         │ Mage             │
// │ The Healer's Trade          │ Enter village         │ Mage             │
// │ Fire and Straw              │ Enter village         │ Mage             │
// │ The Shrine Goes Out         │ Enter village         │ Mage             │
// │ The Warmth Merchant         │ Enter village         │ Mage             │
// │ A Family's Quarrel          │ Enter village         │ General          │
// │ The Harvest Festival        │ Enter village         │ General          │
// │ Ashen Aftermath             │ Enter village         │ General          │
// │ The Warning                 │ Enter village         │ General          │
// │ The Spilled Cart            │ Enter village         │ General          │
// │ The Veteran's Question      │ Leave city/castle     │ Mage             │
// │ The Condemned               │ Leave city/castle     │ Mage             │
// │ Petitioners' Gate           │ Leave city/castle     │ Mage, Renown≥500 │
// │ The Displaced Noble         │ Leave city/castle     │ General          │
// │ The Bard's Request          │ Leave city/castle     │ General, Ren≥300 │
// │ A Detained Soldier          │ Leave city/castle     │ General          │
// │ The Guild's Offer           │ Leave city/castle     │ General, Ren≥500 │
// │ The Ashen Informant         │ Leave city/castle     │ General          │
// │ An Insult at the Gate       │ Leave city/castle     │ General          │
// │ The Curious Scholar         │ Enter city/castle     │ Mage             │
// │ Another Fire                │ Enter city/castle     │ Mage             │
// │ The Ash-Touched Market      │ Enter city/castle     │ Mage             │
// │ Grey Eyes                   │ Enter city/castle     │ Ashen            │
// │ The Fellow Cold             │ Enter city/castle     │ Ashen            │
// │ The Crowd Wants a Sign      │ Enter city/castle     │ Mage, Renown≥1000│
// │ A Soldier Dying             │ Enter city/castle     │ General          │
// │ The Child's Bead            │ Enter city/castle     │ General          │
// │ The Trade Council           │ Enter city/castle     │ General, Ren≥700 │
// │ An Old Enemy                │ Enter city/castle     │ General          │
// │ The Ember-Tithe             │ Enter village/city    │ Mage             │
// │ What the Keep Concealed     │ Siege (won)           │ Mage             │
// │ The Alchemist's Promise     │ Enter city/castle     │ General          │
// │ The Ember Shard             │ Enter village/city    │ General, no trinket│
// │ The Blind Eye               │ Enter village/city    │ General, no trinket│
// │ The Pale Compass            │ Enter village/city    │ General, no trinket│
// │ The Broken Seal             │ Enter city/castle     │ General            │
// └─────────────────────────────┴───────────────────────┴──────────────────┘
//
// Wiring (CampaignBehavior.cs):
//   OnDailyTick  → SettlementEncounters.DailyTick()
//   SyncData     → SettlementEncounters.Save(store)
//   OnNewGameCreated → SettlementEncounters.ResetForNewGame()
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public static partial class SettlementEncounters
    {
        // ── Tuning ────────────────────────────────────────────────────────────
        public const float EncounterChance       = 0.10f;  // per settlement transition (was 0.35)
        public const int   MinDaysBetween        = 7;      // shared cooldown between any encounter (was 3, then 6)
        public const float BattleEncounterChance = 0.14f;  // per field battle (was 0.35)
        public const float SiegeEncounterChance  = 0.22f;  // per siege (was 0.50)
        public const float RaidEncounterChance   = 0.22f;  // per raid (was 0.55)

        // ── State ─────────────────────────────────────────────────────────────
        private static int    _cooldown              = 0;
        private static string _lastSettlementId      = null; // last settlement entered (for leave detection)
        private static bool   _lastBattleWon         = false;
        private static bool   _lastBattleAsAttacker  = false;
        private static int    _childEventCooldown    = 0;   // long cooldown so child event fires rarely
        private static int    _bloodTitheCountdown   = 0;   // days until deferred blood-tithe consequence
        private static int    _babyEventCountdown    = 0;   // days until deferred illegitimate-child event
        private static int    _pregnancyCountdown    = 0;   // days until female-player pregnancy triggers
        private static int    _familyFeverCooldown   = 0;   // long cooldown for the family-plague event
        private static int    _ashenFrenzyCountdown  = 0;   // fires the day after player becomes Ashen
        private static int    _hedgeWitchCooldown    = 0;   // cooldown for the hedge-witch event
        private static int    _hedgeWitchCurse       = 0;   // days until witch-bargain sickness fires
        private static int    _ancientBookFound      = 0;   // 1 after the grimoire event fires (one-time)
        private static int    _trinketCountdown      = 0;   // days until next trinket-dream stage fires
        private static int    _trinketPhase          = 0;   // 0=inactive, 1=first dream, 2=recurring dream
        private static int    _trinketVariant        = 0;   // 1=ember shard, 2=blind eye, 3=pale compass
        private static int    _brokenSealCountdown  = 0;   // days until kingdom-plot consequence fires
        private static int    _brokenSealPlotType   = 0;   // 0=inactive, 1=war, 2=annexation, 3=sabotage
        private static bool   _brokenSealExtraWar   = false; // B also declares war when scouting-pass/charm-fail
        private static string _brokenSealKingdomAId = null; // aggressor kingdom StringId
        private static string _brokenSealKingdomBId = null; // target kingdom StringId
        private static int    _cinderVigilCooldown  = 0;   // cooldown between Cinder Vigil purge encounters
        private static int    _hopeMageConsequenceCountdown = 0;   // days until the youth's castle rebellion stirs
        private static string _hopeMageSettlementId = null;        // settlement where the youth was encountered
        private static bool   _memoryHungerConsumed  = false;      // player chose to "give in" to memory hunger
        private static int    _memoryHungerCountdown = 0;          // days until dissolution fires
        private static int    _poorKnightCooldown     = 0;         // long cooldown so knight event fires rarely
        private static int    _vengefulKnightCountdown = 0;        // days until the mocked knight strikes back
        private static bool   _lastBattleHadAshenEnemy = false;   // true when enemy side had Ashen parties (not persisted)
        private static int    _ashenMachineryCooldown  = 0;       // days between crystal-machine finds
        private static int    _ashenMachineryCountdown = 0;       // days until black-market weapon fires (option D)
        private static string _ashenMachineryKingdomId = null;    // kingdom targeted by deferred option D
        private static int    _weaponInventorFound     = 0;       // 1 after the Toxic Fog encounter fires (one-time)
        private static readonly Random _rng          = new Random();
        // Prevents the same encounter from repeating within a short run of sessions
        private static readonly List<string> _recentEncounters = new List<string>();
        private const int RecentEncounterMemory = 4;

        // ── Colours ───────────────────────────────────────────────────────────
        private static readonly Color FireColor  = new Color(0.90f, 0.60f, 0.20f);
        private static readonly Color GoodColor  = new Color(0.55f, 0.80f, 0.45f);
        private static readonly Color GoldColor  = new Color(0.90f, 0.78f, 0.25f);
        private static readonly Color DimColor   = new Color(0.65f, 0.60f, 0.52f);
        private static readonly Color DarkColor  = new Color(0.45f, 0.40f, 0.60f);
        private static readonly Color BadColor   = new Color(0.75f, 0.35f, 0.28f);
        private static readonly Color AshenColor = new Color(0.30f, 0.35f, 0.70f);

        // ── Public API ────────────────────────────────────────────────────────
        public static void ResetForNewGame()
        {
            _cooldown              = 0;
            _lastSettlementId      = null;
            _childEventCooldown    = 0;
            _bloodTitheCountdown   = 0;
            _babyEventCountdown    = 0;
            _pregnancyCountdown    = 0;
            _familyFeverCooldown   = 0;
            _ashenFrenzyCountdown  = 0;
            _hedgeWitchCooldown    = 0;
            _hedgeWitchCurse       = 0;
            _ancientBookFound      = 0;
            _trinketCountdown      = 0;
            _trinketPhase          = 0;
            _trinketVariant        = 0;
            _brokenSealCountdown   = 0;
            _brokenSealPlotType    = 0;
            _brokenSealExtraWar    = false;
            _brokenSealKingdomAId  = null;
            _brokenSealKingdomBId  = null;
            _cinderVigilCooldown          = 0;
            _hopeMageConsequenceCountdown = 0;
            _hopeMageSettlementId         = null;
            _memoryHungerConsumed         = false;
            _memoryHungerCountdown        = 0;
            _poorKnightCooldown           = 0;
            _vengefulKnightCountdown      = 0;
            _ashenMachineryCooldown       = 0;
            _ashenMachineryCountdown      = 0;
            _ashenMachineryKingdomId      = null;
            _weaponInventorFound          = 0;
            _recentEncounters.Clear();
        }

        public static void Save(IDataStore store)
        {
            store.SyncData("SE_Cooldown",           ref _cooldown);
            store.SyncData("SE_ChildEventCooldown", ref _childEventCooldown);
            store.SyncData("SE_BloodTithe",         ref _bloodTitheCountdown);
            store.SyncData("SE_BabyEvent",          ref _babyEventCountdown);
            store.SyncData("SE_Pregnancy",          ref _pregnancyCountdown);
            store.SyncData("SE_FamilyFever",        ref _familyFeverCooldown);
            store.SyncData("SE_AshenFrenzy",        ref _ashenFrenzyCountdown);
            store.SyncData("SE_HedgeWitchCD",       ref _hedgeWitchCooldown);
            store.SyncData("SE_HedgeWitchCurse",    ref _hedgeWitchCurse);
            store.SyncData("SE_AncientBook",        ref _ancientBookFound);
            store.SyncData("SE_TrinketCountdown",   ref _trinketCountdown);
            store.SyncData("SE_TrinketPhase",       ref _trinketPhase);
            store.SyncData("SE_TrinketVariant",     ref _trinketVariant);
            store.SyncData("SE_BrokenSealCD",       ref _brokenSealCountdown);
            store.SyncData("SE_BrokenSealPlot",     ref _brokenSealPlotType);
            store.SyncData("SE_BrokenSealExtraWar", ref _brokenSealExtraWar);
            store.SyncData("SE_BrokenSealKingA",    ref _brokenSealKingdomAId);
            store.SyncData("SE_BrokenSealKingB",    ref _brokenSealKingdomBId);
            store.SyncData("SE_CinderVigilCD",      ref _cinderVigilCooldown);
            store.SyncData("SE_HopeMageCD",         ref _hopeMageConsequenceCountdown);
            store.SyncData("SE_HopeMageSettlement", ref _hopeMageSettlementId);
            store.SyncData("SE_MemHungerConsumed",  ref _memoryHungerConsumed);
            store.SyncData("SE_MemHungerCD",        ref _memoryHungerCountdown);
            store.SyncData("SE_PoorKnightCD",       ref _poorKnightCooldown);
            store.SyncData("SE_VengefulKnightCD",   ref _vengefulKnightCountdown);
            store.SyncData("SE_AshenMachineCD",     ref _ashenMachineryCooldown);
            store.SyncData("SE_AshenMachineTimer",  ref _ashenMachineryCountdown);
            store.SyncData("SE_AshenMachineKing",   ref _ashenMachineryKingdomId);
            store.SyncData("SE_WeaponInventor",     ref _weaponInventorFound);
        }

        /// Called from CampaignEvents.SettlementEntered — fires immediately when the
        /// player's party steps into any settlement.
        public static void OnPartyEnteredSettlement(MobileParty party, Settlement settlement)
        {
            if (party != MobileParty.MainParty || settlement == null) return;
            try
            {
                _lastSettlementId = settlement.StringId;
                if (_cooldown > 0) return;
                if (_rng.NextDouble() < EncounterChance)
                    TryFireEnter(settlement);
            }
            catch { }
        }

        /// Called from CampaignEvents.OnSettlementLeftEvent — fires immediately when the
        /// player's party leaves any settlement.
        public static void OnPartyLeftSettlement(MobileParty party, Settlement settlement)
        {
            if (party != MobileParty.MainParty || settlement == null) return;
            try
            {
                _lastSettlementId = null;
                if (_cooldown > 0) return;
                if (_rng.NextDouble() < EncounterChance)
                    TryFireLeave(settlement);
            }
            catch { }
        }

        /// Called from MagicCampaignBehavior.OnDailyTick — decrements cooldowns and fires deferred events.
        public static void DailyTick()
        {
            if (_cooldown > 0) _cooldown--;
            if (_childEventCooldown > 0) _childEventCooldown--;
            if (_familyFeverCooldown > 0) _familyFeverCooldown--;
            if (_hedgeWitchCooldown > 0) _hedgeWitchCooldown--;
            if (_cinderVigilCooldown > 0) _cinderVigilCooldown--;

            if (_hedgeWitchCurse > 0)
            {
                _hedgeWitchCurse--;
                if (_hedgeWitchCurse == 0)
                    FireHedgeWitchCurse();
            }

            if (_ashenFrenzyCountdown > 0)
            {
                _ashenFrenzyCountdown--;
                if (_ashenFrenzyCountdown == 0)
                    FireAshenFrenzy();
            }

            if (_bloodTitheCountdown > 0)
            {
                _bloodTitheCountdown--;
                if (_bloodTitheCountdown == 0)
                    FireBloodTitheConsequence();
            }

            if (_babyEventCountdown > 0)
            {
                _babyEventCountdown--;
                if (_babyEventCountdown == 0)
                    FireBabyConsequence();
            }

            if (_pregnancyCountdown > 0)
            {
                _pregnancyCountdown--;
                if (_pregnancyCountdown == 0)
                    FirePregnancyConsequence();
            }

            if (_trinketCountdown > 0)
            {
                _trinketCountdown--;
                if (_trinketCountdown == 0)
                    FireTrinketStage();
            }

            if (_brokenSealCountdown > 0)
            {
                _brokenSealCountdown--;
                if (_brokenSealCountdown == 0)
                    FireBrokenSealConsequence();
            }

            if (_hopeMageConsequenceCountdown > 0)
            {
                _hopeMageConsequenceCountdown--;
                if (_hopeMageConsequenceCountdown == 0)
                    FireHopeMageConsequence();
            }

            if (_memoryHungerConsumed && _memoryHungerCountdown > 0)
            {
                _memoryHungerCountdown--;
                if (_memoryHungerCountdown == 0)
                    FireMemoryHungerDissolution();
            }

            if (_poorKnightCooldown > 0) _poorKnightCooldown--;

            if (_vengefulKnightCountdown > 0)
            {
                _vengefulKnightCountdown--;
                if (_vengefulKnightCountdown == 0)
                    FireVengefulKnightConsequence();
            }

            if (_ashenMachineryCooldown > 0) _ashenMachineryCooldown--;

            if (_ashenMachineryCountdown > 0)
            {
                _ashenMachineryCountdown--;
                if (_ashenMachineryCountdown == 0)
                    FireAshenMachineryConsequence();
            }
        }

        /// Called from MagicCampaignBehavior.OnMapEventEnded.
        public static void OnMapEventEnded(MapEvent mapEvent)
        {
            if (_cooldown > 0 || mapEvent == null) return;
            try
            {
                bool playerAttacker = mapEvent.AttackerSide?.Parties
                    .Any(p => p.Party == PartyBase.MainParty) == true;
                bool playerDefender = mapEvent.DefenderSide?.Parties
                    .Any(p => p.Party == PartyBase.MainParty) == true;
                if (!playerAttacker && !playerDefender) return;

                _lastBattleWon = (playerAttacker && mapEvent.WinningSide == BattleSideEnum.Attacker)
                              || (playerDefender && mapEvent.WinningSide == BattleSideEnum.Defender);
                _lastBattleAsAttacker = playerAttacker;

                try
                {
                    MapEventSide enemySide = playerAttacker ? mapEvent.DefenderSide : mapEvent.AttackerSide;
                    _lastBattleHadAshenEnemy = enemySide?.Parties?.Any(p => {
                        string fId = p.Party?.MapFaction?.StringId ?? "";
                        return fId == "ashen_kingdom" ||
                               (p.Party?.LeaderHero != null && ColourLordRegistry.IsAshenLord(p.Party.LeaderHero));
                    }) == true;
                }
                catch { _lastBattleHadAshenEnemy = false; }

                switch (mapEvent.EventType)
                {
                    case MapEvent.BattleTypes.FieldBattle:
                        if (_rng.NextDouble() < BattleEncounterChance) TryFireBattle();
                        break;
                    case MapEvent.BattleTypes.Siege:
                        if (_rng.NextDouble() < SiegeEncounterChance)  TryFireSiege();
                        break;
                    case MapEvent.BattleTypes.Raid:
                        if (_rng.NextDouble() < RaidEncounterChance)   TryFireRaid();
                        break;
                }
            }
            catch { }
        }

    }
}
