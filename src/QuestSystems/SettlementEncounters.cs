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
// │ The Merchant of Endings     │ Enter city/castle     │ Mage               │
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
        private static int    _oldEnemyCountdown          = 0;  // days until E_OldEnemy follow-up
        private static int    _oldEnemyOutcome             = 0;  // 1=raised hand 2=walked past 3=reported
        private static int    _burningVillageCountdown     = 0;  // days until lord confrontation after village burned
        private static string _burningVillageSettlementId  = null;
        private static int    _burningWitchCountdown       = 0;  // days until passive witch consequence
        private static int    _burningWitchOutcome         = 0;  // 1=watched 2=rode past 3=saved human
        private static int    _ashenCrystalCountdown       = 0;  // days until Left Behind follow-up
        private static int    _ashenCrystalOutcome         = 0;  // 1=destroyed 2=kept 3=left in place
        private static string _ashenCrystalSettlementId    = null;
        private static int    _merchantLedgerCountdown     = 0;  // days until ledger ripple
        private static int    _merchantLedgerOutcome       = 0;  // 1=exposed fraud 2=sided auditor 3=sided merchant
        private static string _merchantLedgerSettlementId  = null;
        private static int    _bloodTitheRevealCountdown   = 0;  // secondary blood tithe follow-up
        private static int    _emberTitheRefusedCountdown  = 0;  // days until refused ember tithe consequence
        private static int    _priestBeatCountdown         = 0;  // days until temple retaliation for beating priest
        private static int    _tavernRobberyCountdown      = 0;  // days until robbery follow-up
        private static int    _selfTaughtMageCountdown     = 0;  // days until self-taught mage letter
        private static int    _selfTaughtMageOutcome       = 0;  // which choice was made
        private static int    _oldMastersStudentCountdown  = 0;  // days until student follow-up
        private static int    _oldMastersStudentOutcome    = 0;  // which choice was made
        private static int    _weaponInventorFound     = 0;       // 1 after the Toxic Fog encounter fires (one-time)
        private static int    _mothersPleaCountdown   = 0;       // days until next Mother's Plea follow-up fires
        private static int    _mothersPleaPhase       = 0;       // 0=none 1=healed_7d 2=money_7d 3=refused_7d 4=child_10yr 5=assassin
        private static readonly Random _rng          = new Random();

        // ── Events7 state ──────────────────────────────────────────────────
        // The Merchant of Endings (city enter, mage-gated, 90-day cooldown)
        private static int _merchantOfEndingsCooldown = 0;

        // ── Events6 state ──────────────────────────────────────────────────
        // Cartographer of Silences (city enter, mage/Ashen, 3-phase escalation)
        private static int    _cartographerPhase                = 0;  // 0=fresh 1,2=in progress 3=done
        private static int    _cartographerCooldown             = 0;  // 60d between sightings
        private static int    _cartographerConsequenceCountdown = 0;  // 14d until Ashen raid fires

        // The Child Who Does Not Sleep (village enter, global arc)
        private static int    _ashChildPhase    = 0;  // 0=initial 1=camp 2=long-camp 10=echo 11=done
        private static int    _ashChildCountdown = 0; // days until next phase fires

        // The Vow Undischarged (city leave, clan tier ≥ 2)
        private static int    _vowCooldown          = 0; // 90d before can fire again
        private static int    _vowPhase             = 0; // 0=not triggered 1=name-taken 2=blackmail 3=done
        private static int    _vowCountdown         = 0; // 14d until named-man fires
        private static int    _vowAssassinCountdown = 0; // 30d until assassins arrive

        // The Fever Road (village leave, recurring hazard)
        private static bool   _feverRoadActive          = false; // window is open
        private static int    _feverRoadWindow           = 0;    // days left in window
        private static int    _feverRoadTriggerCount     = 0;    // times fired this window (max 3)
        private static int    _feverRoadCooldown         = 0;    // 180d between windows
        private static int    _feverRoadSpreadCountdown  = 0;    // 7d until camp fever fires

        // Ashes in the Bread (village enter, moral trap)
        private static int    _ashBreadCooldown     = 0;    // 45d between triggers
        private static int    _ashBreadCountdown    = 0;    // 14d until deferred consequence
        private static int    _ashBreadOutcome      = 0;    // 1=exposed 2=mob 3=defended 4=paid
        private static string _ashBreadSettlementId = null; // settlement where it happened

        // Recent-encounter history to prevent the same event repeating too soon.
        private static readonly List<string> _recentEncounters = new List<string>();
        private const int RecentEncounterMemory = 6;

        // Aging ambient comments — NPC observers notice a mage's accelerated age
        private static int _agingCommentCooldown = 0;

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
            _oldEnemyCountdown          = 0;
            _oldEnemyOutcome            = 0;
            _burningVillageCountdown    = 0;
            _burningVillageSettlementId = null;
            _burningWitchCountdown      = 0;
            _burningWitchOutcome        = 0;
            _ashenCrystalCountdown      = 0;
            _ashenCrystalOutcome        = 0;
            _ashenCrystalSettlementId   = null;
            _merchantLedgerCountdown    = 0;
            _merchantLedgerOutcome      = 0;
            _merchantLedgerSettlementId = null;
            _bloodTitheRevealCountdown  = 0;
            _emberTitheRefusedCountdown = 0;
            _priestBeatCountdown        = 0;
            _tavernRobberyCountdown     = 0;
            _selfTaughtMageCountdown    = 0;
            _selfTaughtMageOutcome      = 0;
            _oldMastersStudentCountdown = 0;
            _oldMastersStudentOutcome   = 0;
            _weaponInventorFound        = 0;
            _mothersPleaCountdown       = 0;
            _mothersPleaPhase           = 0;
            _merchantOfEndingsCooldown        = 0;
            _cartographerPhase                = 0;
            _cartographerCooldown             = 0;
            _cartographerConsequenceCountdown = 0;
            _ashChildPhase    = 0;
            _ashChildCountdown = 0;
            _vowCooldown          = 0;
            _vowPhase             = 0;
            _vowCountdown         = 0;
            _vowAssassinCountdown = 0;
            _feverRoadActive         = false;
            _feverRoadWindow         = 0;
            _feverRoadTriggerCount   = 0;
            _feverRoadCooldown       = 0;
            _feverRoadSpreadCountdown = 0;
            _ashBreadCooldown     = 0;
            _ashBreadCountdown    = 0;
            _ashBreadOutcome      = 0;
            _ashBreadSettlementId = null;
            _agingCommentCooldown = 0;
            _recentEncounters.Clear();
            AmbientRemarks.ResetForNewGame();
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
            store.SyncData("SE_OldEnemyCD",        ref _oldEnemyCountdown);
            store.SyncData("SE_OldEnemyOut",       ref _oldEnemyOutcome);
            store.SyncData("SE_BurnVillageCD",     ref _burningVillageCountdown);
            store.SyncData("SE_BurnVillageS",      ref _burningVillageSettlementId);
            store.SyncData("SE_BurnWitchCD",       ref _burningWitchCountdown);
            store.SyncData("SE_BurnWitchOut",      ref _burningWitchOutcome);
            store.SyncData("SE_AshenCrystalCD",    ref _ashenCrystalCountdown);
            store.SyncData("SE_AshenCrystalOut",   ref _ashenCrystalOutcome);
            store.SyncData("SE_AshenCrystalS",     ref _ashenCrystalSettlementId);
            store.SyncData("SE_MerchantLedgerCD",  ref _merchantLedgerCountdown);
            store.SyncData("SE_MerchantLedgerOut", ref _merchantLedgerOutcome);
            store.SyncData("SE_MerchantLedgerS",   ref _merchantLedgerSettlementId);
            store.SyncData("SE_BloodRevealCD",     ref _bloodTitheRevealCountdown);
            store.SyncData("SE_EmberTitheRefCD",   ref _emberTitheRefusedCountdown);
            store.SyncData("SE_PriestBeatCD",      ref _priestBeatCountdown);
            store.SyncData("SE_TavernRobCD",       ref _tavernRobberyCountdown);
            store.SyncData("SE_SelfTaughtCD",      ref _selfTaughtMageCountdown);
            store.SyncData("SE_SelfTaughtOut",     ref _selfTaughtMageOutcome);
            store.SyncData("SE_OldMasterCD",       ref _oldMastersStudentCountdown);
            store.SyncData("SE_OldMasterOut",      ref _oldMastersStudentOutcome);
            store.SyncData("SE_WeaponInventor",    ref _weaponInventorFound);
            store.SyncData("SE_MothersPleaCD",     ref _mothersPleaCountdown);
            store.SyncData("SE_MothersPleaPhase",  ref _mothersPleaPhase);
            store.SyncData("SE_MerchantOfEndingsCD", ref _merchantOfEndingsCooldown);
            store.SyncData("SE_CartographerPhase",  ref _cartographerPhase);
            store.SyncData("SE_CartographerCD",     ref _cartographerCooldown);
            store.SyncData("SE_CartographerConseq", ref _cartographerConsequenceCountdown);
            store.SyncData("SE_AshChildPhase",      ref _ashChildPhase);
            store.SyncData("SE_AshChildCD",         ref _ashChildCountdown);
            store.SyncData("SE_VowCooldown",        ref _vowCooldown);
            store.SyncData("SE_VowPhase",           ref _vowPhase);
            store.SyncData("SE_VowCD",              ref _vowCountdown);
            store.SyncData("SE_VowAssassinCD",      ref _vowAssassinCountdown);
            store.SyncData("SE_FeverRoadActive",    ref _feverRoadActive);
            store.SyncData("SE_FeverRoadWindow",    ref _feverRoadWindow);
            store.SyncData("SE_FeverRoadCount",     ref _feverRoadTriggerCount);
            store.SyncData("SE_FeverRoadCD",        ref _feverRoadCooldown);
            store.SyncData("SE_FeverRoadSpreadCD",  ref _feverRoadSpreadCountdown);
            store.SyncData("SE_AshBreadCD",         ref _ashBreadCooldown);
            store.SyncData("SE_AshBreadCountdown",  ref _ashBreadCountdown);
            store.SyncData("SE_AshBreadOutcome",    ref _ashBreadOutcome);
            store.SyncData("SE_AshBreadS",          ref _ashBreadSettlementId);
            store.SyncData("SE_AgingCommentCD",     ref _agingCommentCooldown);
            string recentStr = string.Join(",", _recentEncounters);
            store.SyncData("SE_RecentEncounters",  ref recentStr);
            _recentEncounters.Clear();
            if (!string.IsNullOrEmpty(recentStr))
                _recentEncounters.AddRange(recentStr.Split(','));
        }

        /// Called from CampaignEvents.SettlementEntered — fires immediately when the
        /// player's party steps into any settlement.
        public static void OnPartyEnteredSettlement(MobileParty party, Settlement settlement)
        {
            if (party != MobileParty.MainParty || settlement == null) return;
            try
            {
                _lastSettlementId = settlement.StringId;
                try { AmbientRemarks.CheckCompanionRemark(settlement); } catch { }

                // Apprentice discovery: independent 1% roll, not gated by shared cooldown
                if (settlement.IsVillage && MageKnowledge.IsMage && ApprenticeSystem.CanSearch
                    && MageKnowledge._deferredInquiry == null
                    && _rng.NextDouble() < ApprenticeSystem.TriggerChance)
                {
                    MageKnowledge._deferredInquiry = () =>
                    {
                        try { ApprenticeSystem.ShowDiscovery(settlement); } catch { }
                    };
                    return; // skip normal encounter this entry
                }

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
            if (_agingCommentCooldown > 0) _agingCommentCooldown--;

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

            if (_oldEnemyCountdown > 0)
            {
                _oldEnemyCountdown--;
                if (_oldEnemyCountdown == 0)
                    FireOldEnemyConsequence();
            }

            if (_burningVillageCountdown > 0)
            {
                _burningVillageCountdown--;
                if (_burningVillageCountdown == 0)
                    FireBurningVillageConsequence();
            }

            if (_burningWitchCountdown > 0)
            {
                _burningWitchCountdown--;
                if (_burningWitchCountdown == 0)
                    FireBurningWitchConsequence();
            }

            if (_ashenCrystalCountdown > 0)
            {
                _ashenCrystalCountdown--;
                if (_ashenCrystalCountdown == 0)
                    FireAshenCrystalConsequence();
            }

            if (_merchantLedgerCountdown > 0)
            {
                _merchantLedgerCountdown--;
                if (_merchantLedgerCountdown == 0)
                    FireMerchantLedgerConsequence();
            }

            if (_bloodTitheRevealCountdown > 0)
            {
                _bloodTitheRevealCountdown--;
                if (_bloodTitheRevealCountdown == 0)
                    FireBloodTitheReveal();
            }

            if (_emberTitheRefusedCountdown > 0)
            {
                _emberTitheRefusedCountdown--;
                if (_emberTitheRefusedCountdown == 0)
                    FireEmberTitheRefusedConsequence();
            }

            if (_priestBeatCountdown > 0)
            {
                _priestBeatCountdown--;
                if (_priestBeatCountdown == 0)
                    FirePriestBeatConsequence();
            }

            if (_tavernRobberyCountdown > 0)
            {
                _tavernRobberyCountdown--;
                if (_tavernRobberyCountdown == 0)
                    FireTavernRobberyConsequence();
            }

            if (_selfTaughtMageCountdown > 0)
            {
                _selfTaughtMageCountdown--;
                if (_selfTaughtMageCountdown == 0)
                    FireSelfTaughtMageConsequence();
            }

            if (_oldMastersStudentCountdown > 0)
            {
                _oldMastersStudentCountdown--;
                if (_oldMastersStudentCountdown == 0)
                    FireOldMastersStudentConsequence();
            }

            if (_mothersPleaCountdown > 0)
            {
                _mothersPleaCountdown--;
                if (_mothersPleaCountdown == 0)
                    FireMothersPleaConsequence();
            }

            // ── Events7 ticks ─────────────────────────────────────────────
            if (_merchantOfEndingsCooldown > 0) _merchantOfEndingsCooldown--;

            // ── Events6 ticks ─────────────────────────────────────────────
            if (_cartographerCooldown > 0) _cartographerCooldown--;

            if (_cartographerConsequenceCountdown > 0)
            {
                _cartographerConsequenceCountdown--;
                if (_cartographerConsequenceCountdown == 0)
                    FireCartographerConsequence();
            }

            if ((_ashChildPhase == 1 || _ashChildPhase == 2) && _ashChildCountdown > 0)
            {
                _ashChildCountdown--;
                if (_ashChildCountdown == 0)
                    FireAshChildConsequence();
            }

            if (_vowCooldown > 0) _vowCooldown--;

            if (_vowPhase == 1 && _vowCountdown > 0)
            {
                _vowCountdown--;
                if (_vowCountdown == 0)
                    FireVowConsequence();
            }

            if (_vowPhase == 2 && _vowAssassinCountdown > 0)
            {
                _vowAssassinCountdown--;
                if (_vowAssassinCountdown == 0)
                    FireVowAssassins();
            }

            if (_feverRoadActive && _feverRoadWindow > 0)
            {
                _feverRoadWindow--;
                if (_feverRoadWindow == 0)
                {
                    _feverRoadActive       = false;
                    _feverRoadTriggerCount = 0;
                    _feverRoadCooldown     = 180;
                }
            }
            if (_feverRoadCooldown > 0) _feverRoadCooldown--;

            if (_feverRoadSpreadCountdown > 0)
            {
                _feverRoadSpreadCountdown--;
                if (_feverRoadSpreadCountdown == 0)
                    FireFeverRoadSpread();
            }

            if (_ashBreadCooldown > 0) _ashBreadCooldown--;

            if (_ashBreadCountdown > 0)
            {
                _ashBreadCountdown--;
                if (_ashBreadCountdown == 0)
                    FireAshBreadConsequence();
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
