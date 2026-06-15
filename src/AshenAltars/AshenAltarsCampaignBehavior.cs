// =============================================================================
// ASH AND EMBER — AshenAltars/AshenAltarsCampaignBehavior.cs
//
// Ritual-based dark rite system. When a player selects a rite, a hidden target
// number is rolled. Each round of Sacrifice kills prisoners or own soldiers
// (lowest-tier first). A hidden number of points — scaled by alignment traits —
// is added to the accumulated pool. The player chooses to stop or continue each
// round. If accumulated points meet or exceed the target when they stop, the
// rite fires. Stopping short wastes the sacrifice.
//
// Altars: Tyal, Sibir, Baltakhand, Amprela.
// Any hero may approach. Alignment −(Mercy+Honor+Generosity)/6 determines yield.
// Zero or wrong alignment gives 1 pt/round — success requires many rounds of sacrifice
// for a weakened reward.
//
// NPC effects (daily tick):
//   Ashen lords in altar cities: 0.5% chance/day to perform a dark rite.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AshAndEmber
{
    public partial class AshenAltarsCampaignBehavior : CampaignBehaviorBase
    {
        // ── Tuning ─────────────────────────────────────────────────────────────
        private const float MoralePerSacrificePoint = 3f;
        private const int   XpPerBloodTribute       = 75;
        private const int   SolsticeBuffDays         = 30;
        private const int   ColdFreezeEffectDays     = 2;
        private const int   CrossInterferenceDays    = 30;
        private const int   TraitDriftThreshold      = 10;

        // Sacrifice points drained per ritual round (by tier)
        private const int SacrificePerRound_Low    = 2;  // Blood Tribute, Subjugate
        private const int SacrificePerRound_Mid    = 3;  // Cold Fire, Break Wills, Carrion Gift
        private const int SacrificePerRound_High   = 4;  // Ashen Solstice

        // Ritual target ranges (hidden from player)
        private const int BloodTargetLo     = 10; private const int BloodTargetHi     = 18;
        private const int ColdFireTargetLo  = 18; private const int ColdFireTargetHi  = 28;
        private const int BreakWillsTargetLo= 18; private const int BreakWillsTargetHi= 30;
        private const int CarrionTargetLo   = 22; private const int CarrionTargetHi   = 35;
        private const int SubjugateTargetLo = 15; private const int SubjugateTargetHi = 25;
        private const int SolsticeTargetLo  = 35; private const int SolsticeTargetHi  = 55;

        // Cooldowns (base days)
        private const int BloodTributeCooldownBase =  7;
        private const int SolsticeCooldownBase     = 14;
        private const int CarrionGiftCooldownBase  =  7;
        private const int BreakWillsCooldownBase   =  7;
        private const int ColdFireCooldownBase     =  7;
        private const int SubjugateCooldownBase    =  7;

        // Location depletion: after DepletionThreshold ritual starts the stone rests
        private const int DepletionThreshold    =  5;
        private const int DepletionCooldown     = 30;

        private const string AshenKingdomId  = "ashen_kingdom";
        private const string AseraiKingdomId = "aserai";
        private static readonly string[] AshenAltarCities = { "Tyal", "Sibir", "Baltakhand", "Amprela" };
        private static readonly List<string> _dynamicAltarIds = new List<string>();
        private static readonly Random _rng = new Random();

        private static bool _altarsAnnounced = false;

        // Cross-system state (read by SanctuaryCampaignBehavior)
        internal static int _lastAltarUseDay  = -999;
        private static int  _altarUseCount    = 0;

        private static int    _solsticeUntilDay = -1;
        private static string _solsticeType     = "";

        private static string _frozenPartyId  = "";
        private static int    _frozenUntilDay = -1;

        // Per-rite cooldown tracking
        private static int _lastBloodTributeDay = -999;
        private static int _lastSolsticeDay     = -999;
        private static int _lastCarrionDay      = -999;
        private static int _lastBreakWillsDay   = -999;
        private static int _lastColdFireDay     = -999;
        private static int _lastSubjugateDay    = -999;

        private static readonly Dictionary<string, int> _locationUses          = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _locationDepletedUntil = new Dictionary<string, int>();

        // ── CampaignBehaviorBase ───────────────────────────────────────────────
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore store)
        {
            try { store.SyncData("ALTAR_Announced", ref _altarsAnnounced); } catch { }
            try { store.SyncData("ALTAR_LastUseDay", ref _lastAltarUseDay); } catch { }
            try { store.SyncData("ALTAR_UseCount", ref _altarUseCount); } catch { }
            try { store.SyncData("ALTAR_SolsticeUntilDay", ref _solsticeUntilDay); } catch { }
            try { store.SyncData("ALTAR_SolsticeType", ref _solsticeType); } catch { }
            try { store.SyncData("ALTAR_FrozenPartyId", ref _frozenPartyId); } catch { }
            try { store.SyncData("ALTAR_FrozenUntilDay", ref _frozenUntilDay); } catch { }
            try { store.SyncData("ALTAR_LastBloodTributeDay", ref _lastBloodTributeDay); } catch { }
            try { store.SyncData("ALTAR_LastSolsticeDay", ref _lastSolsticeDay); } catch { }
            try { store.SyncData("ALTAR_LastCarrionDay", ref _lastCarrionDay); } catch { }
            try { store.SyncData("ALTAR_LastBreakWillsDay", ref _lastBreakWillsDay); } catch { }
            try { store.SyncData("ALTAR_LastColdFireDay", ref _lastColdFireDay); } catch { }
            try { store.SyncData("ALTAR_LastSubjugateDay", ref _lastSubjugateDay); } catch { }
            try
            {
                var luKeys = _locationUses.Keys.ToList();
                var luVals = _locationUses.Values.ToList();
                store.SyncData("ALTAR_LocUseKeys", ref luKeys);
                store.SyncData("ALTAR_LocUseVals", ref luVals);
                if (luKeys != null && luVals != null)
                { _locationUses.Clear(); for (int i = 0; i < Math.Min(luKeys.Count, luVals.Count); i++) _locationUses[luKeys[i]] = luVals[i]; }
            } catch { }
            try
            {
                var ldKeys = _locationDepletedUntil.Keys.ToList();
                var ldVals = _locationDepletedUntil.Values.ToList();
                store.SyncData("ALTAR_LocDepKeys", ref ldKeys);
                store.SyncData("ALTAR_LocDepVals", ref ldVals);
                if (ldKeys != null && ldVals != null)
                { _locationDepletedUntil.Clear(); for (int i = 0; i < Math.Min(ldKeys.Count, ldVals.Count); i++) _locationDepletedUntil[ldKeys[i]] = ldVals[i]; }
            } catch { }
            try
            {
                var dynIds = _dynamicAltarIds.ToList();
                store.SyncData("ALTAR_DynamicIds", ref dynIds);
                if (dynIds != null) { _dynamicAltarIds.Clear(); foreach (var id in dynIds) _dynamicAltarIds.Add(id); }
            } catch { }
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            EnsureDynamicAltars();
            AnnounceAltars();
            RegisterAltarMenus(starter);
        }

        // Authoritative new-game setup, fired once from OnCharacterCreationIsOver
        // (after the world is built, never on a load). Clears carry-over from a prior
        // game in the same session, then announces the altars fresh.
        public static void EstablishForNewCampaign()
        {
            ResetForNewGame();
            EnsureDynamicAltars();
            AnnounceAltars();
        }

        // Clears per-campaign static state so a new game started in the same
        // Bannerlord session does not inherit the previous game's "announced" flag
        // (which would suppress the altar establishment toast) or stale cooldowns.
        public static void ResetForNewGame()
        {
            _altarsAnnounced     = false;
            _lastAltarUseDay     = -999;
            _altarUseCount       = 0;
            _solsticeUntilDay    = -1;
            _solsticeType        = "";
            _frozenPartyId       = "";
            _frozenUntilDay      = -1;
            _lastBloodTributeDay = -999;
            _lastSolsticeDay     = -999;
            _lastCarrionDay      = -999;
            _lastBreakWillsDay   = -999;
            _lastColdFireDay     = -999;
            _lastSubjugateDay    = -999;
            _locationUses.Clear();
            _locationDepletedUntil.Clear();
            _dynamicAltarIds.Clear();
        }

        // Selects one random Aserai town as a dynamic altar if not already set.
        // Called on session launch (existing saves) and on new campaign creation.
        private static void EnsureDynamicAltars()
        {
            if (_dynamicAltarIds.Count > 0) return;
            try
            {
                var pick = Settlement.All
                    .Where(s => s.IsTown && s.OwnerClan?.Kingdom?.StringId == AseraiKingdomId)
                    .OrderBy(_ => _rng.Next()).FirstOrDefault();
                if (pick != null) _dynamicAltarIds.Add(pick.StringId);
            }
            catch { }
        }

        private static void AnnounceAltars()
        {
            if (_altarsAnnounced) return;
            _altarsAnnounced = true;
            try
            {
                var names = AshenAltarCities.ToList();
                foreach (var id in _dynamicAltarIds)
                {
                    var s = Settlement.All.FirstOrDefault(x => x.StringId == id);
                    if (s != null) names.Add(s.Name?.ToString() ?? id);
                }
                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Ashen Altars stand in {string.Join(", ", names)}. " +
                    "Only the Merciless and Devious may kneel before them."));
            }
            catch { }
        }
    }
}
