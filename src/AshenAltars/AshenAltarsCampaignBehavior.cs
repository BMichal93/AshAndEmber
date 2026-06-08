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
    public class AshenAltarsCampaignBehavior : CampaignBehaviorBase
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

        private const string AshenKingdomId = "ashen_kingdom";
        private static readonly string[] AshenAltarCities = { "Tyal", "Sibir", "Baltakhand", "Amprela" };
        private static readonly Random _rng = new Random();

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
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            AnnounceAltars();
            RegisterAltarMenus(starter);
        }

        private static void AnnounceAltars()
        {
            try
            {
                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Ashen Altars stand in {string.Join(", ", AshenAltarCities)}. " +
                    "Only the Merciless and Devious may kneel before them."));
            }
            catch { }
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        internal static bool HasAshenAltar(Settlement s)
        {
            if (s == null || !s.IsTown) return false;
            try
            {
                string name = s.Name?.ToString() ?? "";
                return AshenAltarCities.Any(city =>
                    name.IndexOf(city, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch { return false; }
        }

        private static int CurrentCampaignDay()
        {
            try { return (int)CampaignTime.Now.ToDays; } catch { return 0; }
        }

        private static bool IsRiteOnCooldown(int lastDay, int baseCooldown, float mult)
        {
            float absM   = Math.Min(1f, Math.Abs(mult));
            int cooldown = Math.Max(1, (int)(baseCooldown * (2f - absM)));
            return CurrentCampaignDay() - lastDay < cooldown;
        }

        private static int CooldownDaysLeft(int lastDay, int baseCooldown, float mult)
        {
            float absM   = Math.Min(1f, Math.Abs(mult));
            int cooldown = Math.Max(1, (int)(baseCooldown * (2f - absM)));
            return cooldown - (CurrentCampaignDay() - lastDay);
        }

        private static bool IsLocationDepleted()
        {
            string id = Settlement.CurrentSettlement?.StringId ?? "";
            if (string.IsNullOrEmpty(id)) return false;
            return _locationDepletedUntil.TryGetValue(id, out int until) && CurrentCampaignDay() <= until;
        }

        private static int LocationDepletedDaysLeft()
        {
            string id = Settlement.CurrentSettlement?.StringId ?? "";
            if (!_locationDepletedUntil.TryGetValue(id, out int until)) return 0;
            return Math.Max(0, until - CurrentCampaignDay());
        }

        private static void RecordLocationUse()
        {
            string id = Settlement.CurrentSettlement?.StringId ?? "";
            if (string.IsNullOrEmpty(id)) return;
            if (!_locationUses.TryGetValue(id, out int count)) count = 0;
            count++;
            if (count >= DepletionThreshold)
            {
                _locationDepletedUntil[id] = CurrentCampaignDay() + DepletionCooldown;
                _locationUses[id] = 0;
                MBInformationManager.AddQuickInformation(new TextObject(
                    "The altar is spent. The stone needs time to drink before it can give again."));
            }
            else _locationUses[id] = count;
        }

        // +1.0 = full dark power (max evil); 0 = no benefit; negative = penalty.
        internal static float AltarTraitMultiplier()
        {
            var h = Hero.MainHero;
            if (h == null) return 0f;
            try
            {
                int mercy = h.GetTraitLevel(DefaultTraits.Mercy);
                int honor = h.GetTraitLevel(DefaultTraits.Honor);
                int gen   = h.GetTraitLevel(DefaultTraits.Generosity);
                float raw = -(mercy + honor + gen) / 6f;
                if (CurrentCampaignDay() - SanctuaryCampaignBehavior._lastSanctuaryUseDay < CrossInterferenceDays)
                    raw *= 0.5f;
                return Math.Max(-1f, Math.Min(1f, raw));
            }
            catch { return 0f; }
        }

        private static float NpcAltarMult(Hero h)
        {
            try
            {
                int mercy = h.GetTraitLevel(DefaultTraits.Mercy);
                int honor = h.GetTraitLevel(DefaultTraits.Honor);
                int gen   = h.GetTraitLevel(DefaultTraits.Generosity);
                return Math.Max(-1f, Math.Min(1f, -(mercy + honor + gen) / 6f));
            }
            catch { return 0f; }
        }

        private static string AltarTraitNote(float mult)
        {
            if (mult >= 0.8f)  return "  [The cold knows you — full power]";
            if (mult >= 0.4f)  return "  [Partial power]";
            if (mult >= 0.01f) return "  [Faint dark blessing]";
            if (mult >= -0.01f)return "  [Stranger — many rounds needed; weak reward]";
            if (mult >= -0.5f) return "  [PENALTY — great sacrifice for lesser yield]";
            return "  [HEAVY PENALTY — every round costs you greatly; reward barely moves]";
        }

        private static bool NpcCanUseAltar(Hero h)
        {
            try
            {
                if (h.Clan?.Kingdom?.StringId == AshenKingdomId) return true;
                return h.GetTraitLevel(DefaultTraits.Mercy) <= -1
                    && h.GetTraitLevel(DefaultTraits.Honor) <= -1;
            }
            catch { return false; }
        }

        private static int ModifiedSacrificePoints(int basePts)
        {
            try
            {
                int gen = Hero.MainHero?.GetTraitLevel(DefaultTraits.Generosity) ?? 0;
                float modifier = 1f + gen * 0.125f;
                return Math.Max(1, (int)(basePts * modifier));
            }
            catch { return basePts; }
        }

        private static int TotalSacrificePoints()
        {
            int total = 0;
            try
            {
                var prisoners = MobileParty.MainParty?.PrisonRoster;
                if (prisoners != null)
                    foreach (var e in prisoners.GetTroopRoster())
                        if (!e.Character.IsHero) total += e.Number * Math.Max(1, e.Character.Tier);
            }
            catch { }
            try
            {
                var party = MobileParty.MainParty?.MemberRoster;
                if (party != null)
                    foreach (var e in party.GetTroopRoster())
                        if (!e.Character.IsHero) total += (e.Number - e.WoundedNumber) * Math.Max(1, e.Character.Tier);
            }
            catch { }
            return total;
        }

        // Kills minimum needed to cover ptsNeeded. Prisoners first, then party. Returns killed count + narrative.
        private static (int killed, string narrative) SacrificeRound(int ptsNeeded)
        {
            int remaining   = ptsNeeded;
            int totalKilled = 0;

            // Drain prisoners first
            try
            {
                var prisoners = MobileParty.MainParty?.PrisonRoster;
                if (prisoners != null)
                {
                    foreach (var entry in prisoners.GetTroopRoster()
                        .Where(e => !e.Character.IsHero && e.Number > 0)
                        .OrderBy(e => e.Character.Tier).ThenBy(e => e.Character.StringId).ToList())
                    {
                        if (remaining <= 0) break;
                        int tier   = Math.Max(1, entry.Character.Tier);
                        int toKill = Math.Min(entry.Number, (remaining + tier - 1) / tier);
                        if (toKill <= 0) continue;
                        try { prisoners.AddToCounts(entry.Character, -toKill); } catch { }
                        remaining   -= toKill * tier;
                        totalKilled += toKill;
                    }
                }
            }
            catch { }

            // Then drain party members
            if (remaining > 0)
            {
                try
                {
                    var roster = MobileParty.MainParty?.MemberRoster;
                    if (roster != null)
                    {
                        foreach (var entry in roster.GetTroopRoster()
                            .Where(e => !e.Character.IsHero && (e.Number - e.WoundedNumber) > 0)
                            .OrderBy(e => e.Character.Tier).ThenBy(e => e.Character.StringId).ToList())
                        {
                            if (remaining <= 0) break;
                            int tier    = Math.Max(1, entry.Character.Tier);
                            int healthy = entry.Number - entry.WoundedNumber;
                            int toKill  = Math.Min(healthy, (remaining + tier - 1) / tier);
                            if (toKill <= 0) continue;
                            try { roster.AddToCounts(entry.Character, -toKill); } catch { }
                            remaining   -= toKill * tier;
                            totalKilled += toKill;
                        }
                    }
                }
                catch { }
            }

            int pointsSpent = ptsNeeded - Math.Max(0, remaining);
            try { MobileParty.MainParty.RecentEventsMorale -= pointsSpent * MoralePerSacrificePoint; } catch { }

            string narr = totalKilled > 0
                ? $"The altar takes {totalKilled} life{(totalKilled != 1 ? "s" : "")}. The stone is darker than it was. Your men carry it in their faces."
                : "The altar accepts the offering of blood. The stone is patient.";
            return (totalKilled, narr);
        }

        // ── Ritual core ────────────────────────────────────────────────────────
        // Floor of 1 so any hero can succeed — but unaligned heroes sacrifice many more lives for weak rewards.
        private static int RollRoundPoints(float mult)
        {
            if (mult <= 0f) return 1;
            int raw = 3 + _rng.Next(8); // 3–10
            return Math.Max(1, (int)Math.Round(raw * mult));
        }

        private static string ColdProgressHint(int accumulated, int target)
        {
            float pct = target > 0 ? (float)accumulated / target : 0f;
            if (accumulated <= 0) return "The grey flame is indifferent. The blood falls and is forgotten.";
            if (pct < 0.30f) return "Something stirs in the stone. A coldness, not hostile, but considering.";
            if (pct < 0.60f) return "The grey flame leans toward you. It smells what you have given.";
            if (pct < 0.90f) return "The cold deepens. The priest steps back from the altar without being asked.";
            return "The grey fire is ready. One more offering and it moves.";
        }

        private static void RunAltarRitual(
            string riteName,
            int target,
            float mult,
            int sacrificePtsPerRound,
            Action onSuccess,
            Action onFailure,
            float moralePerRound = 0f)  // alternative round cost (used when sacrifice pts = 0)
        {
            int accumulated = 0;
            int round       = 0;

            void DoRound()
            {
                // Check if enough material remains (skip check when using morale cost)
                if (sacrificePtsPerRound > 0 && TotalSacrificePoints() < sacrificePtsPerRound)
                {
                    // Forced stop
                    if (accumulated >= target) onSuccess();
                    else
                    {
                        string noMore = "The altar has emptied your offering. There is nothing more to give. " +
                            "The rite is unfinished. The grey fire takes what it has and gives nothing back.";
                        try { InformationManager.ShowInquiry(new InquiryData(riteName, noMore, true, false, "The price is paid.", "", null, null)); } catch { }
                        onFailure();
                    }
                    return;
                }

                string costNarr;
                if (sacrificePtsPerRound > 0)
                {
                    var (killed, narr) = SacrificeRound(sacrificePtsPerRound);
                    costNarr = narr;
                }
                else
                {
                    // Morale-only cost (used by Subjugation so prisoners are preserved)
                    if (moralePerRound > 0f)
                        try { MobileParty.MainParty.RecentEventsMorale -= moralePerRound; } catch { }
                    costNarr = moralePerRound > 0f
                        ? "The will required to hold them bends the mind. Your men sense something happening here."
                        : "The altar waits. The offering is your intent.";
                }

                int pts = RollRoundPoints(mult);
                accumulated += pts;
                round++;

                string hint   = ColdProgressHint(accumulated, target);
                string header = $"{riteName} — Sacrifice ({round})";
                string body   = $"{costNarr}\n\n{hint}";

                try
                {
                    InformationManager.ShowInquiry(new InquiryData(
                        header, body, true, true,
                        "Offer more",
                        "Complete the rite — take what blood has bought",
                        () => DoRound(),
                        () =>
                        {
                            if (accumulated >= target) onSuccess();
                            else onFailure();
                        }));
                }
                catch
                {
                    if (accumulated >= target) onSuccess();
                    else onFailure();
                }
            }

            DoRound();
        }

        private static void ShowRitualFailure(string riteName)
        {
            string msg = "The threshold was not reached. The cold does not negotiate. " +
                "The sacrifice was taken and the rite is void. Nothing returns.";
            try { InformationManager.ShowInquiry(new InquiryData(riteName, msg, true, false, "The price is paid.", "", null, null)); }
            catch { MBInformationManager.AddQuickInformation(new TextObject($"{riteName} — ritual incomplete.")); }
        }

        // ── Menu registration ──────────────────────────────────────────────────
        private static void RegisterAltarMenus(CampaignGameStarter starter)
        {
            // Entry in town menu
            try
            {
                starter.AddGameMenuOption("town", "altar_enter", "{ALTAR_ENTER_TEXT}",
                    args =>
                    {
                        try
                        {
                            if (!HasAshenAltar(Settlement.CurrentSettlement)) return false;
                            float mult = AltarTraitMultiplier();
                            MBTextManager.SetTextVariable("ALTAR_ENTER_TEXT",
                                "Visit the Ashen Altar" + AltarTraitNote(mult));
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; } catch { }
                            args.IsEnabled = true;
                            return true;
                        }
                        catch { return false; }
                    },
                    args => { try { GameMenu.SwitchToMenu("altar_menu"); } catch { } },
                    false, -1, false);
            }
            catch { }

            // Sub-menu header
            try
            {
                starter.AddGameMenu("altar_menu", "{ALTAR_MENU_HEADER}", args =>
                {
                    try
                    {
                        int today = CurrentCampaignDay();
                        int pts   = TotalSacrificePoints();
                        string ptsNote = pts > 0 ? $"  [Sacrifice available: {pts} pts]" : "  [No sacrifice available]";
                        string solNote = _solsticeUntilDay >= today
                            ? $"  [Solstice ({_solsticeType}): {_solsticeUntilDay - today + 1} day(s) remaining]" : "";
                        string frozenNote = "";
                        if (!string.IsNullOrEmpty(_frozenPartyId) && _frozenUntilDay >= today)
                        {
                            var fp = MobileParty.All.FirstOrDefault(p => p.StringId == _frozenPartyId && p.IsActive);
                            frozenNote = $"  [Cold Fire freeze on {fp?.Name?.ToString() ?? _frozenPartyId}: {_frozenUntilDay - today + 1} day(s)]";
                        }
                        string deplNote = IsLocationDepleted()
                            ? $"  [SPENT — returns in {LocationDepletedDaysLeft()} day(s)]" : "";
                        MBTextManager.SetTextVariable("ALTAR_MENU_HEADER",
                            $"The Ashen Altar. Stone worn smooth by blood that never fully dried. " +
                            $"The flame here is grey, and it is always hungry.{ptsNote}{solNote}{frozenNote}{deplNote}");
                    }
                    catch { }
                });
            }
            catch { }

            // ── Blood Tribute ───────────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("altar_menu", "altar_bloodtribute", "{ALTAR_BLOODTRIBUTE_TEXT}",
                    args =>
                    {
                        try
                        {
                            float mult = AltarTraitMultiplier();
                            string cd = "";
                            if (IsLocationDepleted()) { args.IsEnabled = false; }
                            else if (IsRiteOnCooldown(_lastBloodTributeDay, BloodTributeCooldownBase, mult))
                            { args.IsEnabled = false; cd = $"  [On cooldown: {CooldownDaysLeft(_lastBloodTributeDay, BloodTributeCooldownBase, mult)} day(s)]"; }
                            else args.IsEnabled = TotalSacrificePoints() >= SacrificePerRound_Low;
                            MBTextManager.SetTextVariable("ALTAR_BLOODTRIBUTE_TEXT",
                                $"Blood Tribute ({SacrificePerRound_Low} pts/round) — spill blood; survivors grow stronger{cd}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => StartBloodTribute());
            }
            catch { }

            // ── The Ashen Solstice ──────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("altar_menu", "altar_solstice", "{ALTAR_SOLSTICE_TEXT}",
                    args =>
                    {
                        try
                        {
                            float mult = AltarTraitMultiplier();
                            string cd = "";
                            if (IsLocationDepleted()) { args.IsEnabled = false; }
                            else if (IsRiteOnCooldown(_lastSolsticeDay, SolsticeCooldownBase, mult))
                            { args.IsEnabled = false; cd = $"  [On cooldown: {CooldownDaysLeft(_lastSolsticeDay, SolsticeCooldownBase, mult)} day(s)]"; }
                            else args.IsEnabled = TotalSacrificePoints() >= SacrificePerRound_High;
                            MBTextManager.SetTextVariable("ALTAR_SOLSTICE_TEXT",
                                $"The Ashen Solstice ({SacrificePerRound_High} pts/round) — call down an Iron Winter or Scorching Sun{cd}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => StartAshenSolstice());
            }
            catch { }

            // ── Carrion Gift ────────────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("altar_menu", "altar_carrion", "{ALTAR_CARRION_TEXT}",
                    args =>
                    {
                        try
                        {
                            float mult = AltarTraitMultiplier();
                            string cd = "";
                            if (IsLocationDepleted()) { args.IsEnabled = false; }
                            else if (IsRiteOnCooldown(_lastCarrionDay, CarrionGiftCooldownBase, mult))
                            { args.IsEnabled = false; cd = $"  [On cooldown: {CooldownDaysLeft(_lastCarrionDay, CarrionGiftCooldownBase, mult)} day(s)]"; }
                            else args.IsEnabled = TotalSacrificePoints() >= SacrificePerRound_Mid;
                            MBTextManager.SetTextVariable("ALTAR_CARRION_TEXT",
                                $"Carrion Gift ({SacrificePerRound_Mid} pts/round) — send a grey plague to a distant garrison{cd}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => StartCarrionGift());
            }
            catch { }

            // ── Break Hearts and Wills ──────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("altar_menu", "altar_breakwills", "{ALTAR_BREAKWILLS_TEXT}",
                    args =>
                    {
                        try
                        {
                            float mult = AltarTraitMultiplier();
                            string cd = "";
                            if (IsLocationDepleted()) { args.IsEnabled = false; }
                            else if (IsRiteOnCooldown(_lastBreakWillsDay, BreakWillsCooldownBase, mult))
                            { args.IsEnabled = false; cd = $"  [On cooldown: {CooldownDaysLeft(_lastBreakWillsDay, BreakWillsCooldownBase, mult)} day(s)]"; }
                            else args.IsEnabled = TotalSacrificePoints() >= SacrificePerRound_Mid;
                            MBTextManager.SetTextVariable("ALTAR_BREAKWILLS_TEXT",
                                $"Break Hearts and Wills ({SacrificePerRound_Mid} pts/round) — sow cold despair in an enemy city{cd}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => StartBreakWills());
            }
            catch { }

            // ── Rite of Cold Fire ───────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("altar_menu", "altar_coldfire", "{ALTAR_COLDFIRE_TEXT}",
                    args =>
                    {
                        try
                        {
                            float mult = AltarTraitMultiplier();
                            string cd = "";
                            if (IsLocationDepleted()) { args.IsEnabled = false; }
                            else if (IsRiteOnCooldown(_lastColdFireDay, ColdFireCooldownBase, mult))
                            { args.IsEnabled = false; cd = $"  [On cooldown: {CooldownDaysLeft(_lastColdFireDay, ColdFireCooldownBase, mult)} day(s)]"; }
                            else args.IsEnabled = TotalSacrificePoints() >= SacrificePerRound_Mid;
                            MBTextManager.SetTextVariable("ALTAR_COLDFIRE_TEXT",
                                $"Rite of Cold Fire ({SacrificePerRound_Mid} pts/round) — curse a nearby enemy party{cd}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => StartColdFire());
            }
            catch { }

            // ── Rite of Subjugation ─────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("altar_menu", "altar_subjugate", "{ALTAR_SUBJUGATE_TEXT}",
                    args =>
                    {
                        try
                        {
                            float mult = AltarTraitMultiplier();
                            string cd = "";
                            if (IsLocationDepleted()) { args.IsEnabled = false; }
                            else if (IsRiteOnCooldown(_lastSubjugateDay, SubjugateCooldownBase, mult))
                            { args.IsEnabled = false; cd = $"  [On cooldown: {CooldownDaysLeft(_lastSubjugateDay, SubjugateCooldownBase, mult)} day(s)]"; }
                            else args.IsEnabled = CanAffordSubjugate();
                            MBTextManager.SetTextVariable("ALTAR_SUBJUGATE_TEXT",
                                $"Rite of Subjugation (20 morale/round) — bend your will until they break; then claim them{cd}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => StartSubjugate());
            }
            catch { }

            // ── Leave ───────────────────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("altar_menu", "altar_leave", "Leave the Altar",
                    args => { try { args.optionLeaveType = GameMenuOption.LeaveType.Leave; } catch { } return true; },
                    args => { try { GameMenu.SwitchToMenu("town"); } catch { } },
                    true, -1, false);
            }
            catch { }
        }

        // ── Ritual initiators ─────────────────────────────────────────────────

        private static void StartBloodTribute()
        {
            float mult = AltarTraitMultiplier();
            int target = BloodTargetLo + _rng.Next(BloodTargetHi - BloodTargetLo + 1);

            _lastBloodTributeDay = CurrentCampaignDay();
            _lastAltarUseDay     = CurrentCampaignDay();
            _altarUseCount++;
            RecordLocationUse();

            RunAltarRitual(
                "Blood Tribute", target, mult, SacrificePerRound_Low,
                () =>
                {
                    var roster = MobileParty.MainParty?.MemberRoster;
                    int xp = Math.Max(1, (int)(XpPerBloodTribute * Math.Max(0.5f, mult)));
                    if (roster != null)
                        foreach (var e in roster.GetTroopRoster().ToList())
                        {
                            if (e.Character.IsHero) continue;
                            try { roster.AddToCounts(e.Character, 0, false, 0, xp); } catch { }
                        }
                    try { MobileParty.MainParty.RecentEventsMorale -= 15f; } catch { }
                    try { MobileParty.MainParty.RecentEventsMorale += 15f * mult; } catch { }
                    string msg = "The altar is satisfied. The blood has done what blood does. Your survivors are better for having been witnesses.";
                    try { InformationManager.ShowInquiry(new InquiryData("Blood Tribute", msg, true, false, "The blood is spent.", "", null, null)); }
                    catch { MBInformationManager.AddQuickInformation(new TextObject("Blood Tribute — survivors strengthened.")); }
                    try { GameMenu.SwitchToMenu("altar_menu"); } catch { }
                },
                () => { ShowRitualFailure("Blood Tribute"); try { GameMenu.SwitchToMenu("altar_menu"); } catch { } });
        }

        private static void StartAshenSolstice()
        {
            float mult = AltarTraitMultiplier();
            int target = SolsticeTargetLo + _rng.Next(SolsticeTargetHi - SolsticeTargetLo + 1);

            _lastSolsticeDay = CurrentCampaignDay();
            _lastAltarUseDay = CurrentCampaignDay();
            _altarUseCount++;
            RecordLocationUse();

            RunAltarRitual(
                "The Ashen Solstice", target, mult, SacrificePerRound_High,
                () =>
                {
                    try
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            "The Ashen Solstice",
                            "The altar has received enough. Which season will you call down?\n\n" +
                            "Iron Winter grips the north — the cold that breaks rivers.\n\n" +
                            "Scorching Sun burns the south — the sky white with heat.",
                            true, true,
                            "Iron Winter (north)", "Scorching Sun (south)",
                            () =>
                            {
                                CampaignMapEvents.ForceIronWinter();
                                _solsticeType    = "winter";
                                _solsticeUntilDay = CurrentCampaignDay() + SolsticeBuffDays;
                                string narr = "The cold obeys. The north darkens. Your men feel the ice as armour, not a wound.";
                                try { InformationManager.ShowInquiry(new InquiryData("Iron Winter Called", narr, true, false, "The north will remember.", "", null, null)); }
                                catch { MBInformationManager.AddQuickInformation(new TextObject(narr)); }
                                try { GameMenu.SwitchToMenu("altar_menu"); } catch { }
                            },
                            () =>
                            {
                                CampaignMapEvents.ForceScorchingSun();
                                _solsticeType    = "sun";
                                _solsticeUntilDay = CurrentCampaignDay() + SolsticeBuffDays;
                                string narr = "The heat bends to your will. The south bakes. Your column moves with the sun at your back.";
                                try { InformationManager.ShowInquiry(new InquiryData("Scorching Sun Called", narr, true, false, "The south will burn.", "", null, null)); }
                                catch { MBInformationManager.AddQuickInformation(new TextObject(narr)); }
                                try { GameMenu.SwitchToMenu("altar_menu"); } catch { }
                            }));
                    }
                    catch
                    {
                        CampaignMapEvents.ForceIronWinter();
                        _solsticeType = "winter"; _solsticeUntilDay = CurrentCampaignDay() + SolsticeBuffDays;
                        MBInformationManager.AddQuickInformation(new TextObject("The Ashen Solstice — the north darkens."));
                        try { GameMenu.SwitchToMenu("altar_menu"); } catch { }
                    }
                },
                () => { ShowRitualFailure("The Ashen Solstice"); try { GameMenu.SwitchToMenu("altar_menu"); } catch { } });
        }

        private static void StartCarrionGift()
        {
            float mult = AltarTraitMultiplier();
            int target = CarrionTargetLo + _rng.Next(CarrionTargetHi - CarrionTargetLo + 1);

            _lastCarrionDay  = CurrentCampaignDay();
            _lastAltarUseDay = CurrentCampaignDay();
            _altarUseCount++;
            RecordLocationUse();

            RunAltarRitual(
                "Carrion Gift", target, mult, SacrificePerRound_Mid,
                () =>
                {
                    var candidates = Settlement.All
                        .Where(s => s.IsTown && s.MapFaction?.StringId != AshenKingdomId
                                 && s.Town?.GarrisonParty?.MemberRoster?.TotalManCount > 0)
                        .OrderByDescending(s => s.Town.GarrisonParty.MemberRoster.TotalManCount)
                        .Take(2).ToList();

                    if (candidates.Count == 0)
                    {
                        string noTarget = "The plague leaves the altar and dissipates. No suitable garrison can be found.";
                        try { InformationManager.ShowInquiry(new InquiryData("Carrion Gift", noTarget, true, false, "Let it spread.", "", null, null)); } catch { }
                        try { GameMenu.SwitchToMenu("altar_menu"); } catch { }
                        return;
                    }

                    if (candidates.Count == 1)
                    {
                        ApplyCarrionGiftToTarget(candidates[0], mult);
                        try { GameMenu.SwitchToMenu("altar_menu"); } catch { }
                        return;
                    }

                    var cityA = candidates[0]; var cityB = candidates[1];
                    string nameA = cityA.Name?.ToString() ?? "a distant city";
                    string nameB = cityB.Name?.ToString() ?? "another city";
                    int countA = cityA.Town?.GarrisonParty?.MemberRoster?.TotalManCount ?? 0;
                    int countB = cityB.Town?.GarrisonParty?.MemberRoster?.TotalManCount ?? 0;

                    try
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            "Where Will the Plague Land?",
                            $"The grey sickness waits. Two armies draw breath it could stop.\n\n{nameA} — {countA} soldiers.\n\n{nameB} — {countB} soldiers.",
                            true, true, nameA, nameB,
                            () => { ApplyCarrionGiftToTarget(cityA, mult); try { GameMenu.SwitchToMenu("altar_menu"); } catch { } },
                            () => { ApplyCarrionGiftToTarget(cityB, mult); try { GameMenu.SwitchToMenu("altar_menu"); } catch { } }));
                    }
                    catch
                    {
                        ApplyCarrionGiftToTarget(cityA, mult);
                        try { GameMenu.SwitchToMenu("altar_menu"); } catch { }
                    }
                },
                () => { ShowRitualFailure("Carrion Gift"); try { GameMenu.SwitchToMenu("altar_menu"); } catch { } });
        }

        private static void ApplyCarrionGiftToTarget(Settlement target, float mult)
        {
            string name = target.Name?.ToString() ?? "a distant city";
            int totalWounded = 0;
            try
            {
                foreach (var e in target.Town.GarrisonParty.MemberRoster.GetTroopRoster().ToList())
                {
                    if (e.Character.IsHero) continue;
                    int healthy = e.Number - e.WoundedNumber; if (healthy <= 0) continue;
                    int toWound = Math.Max(1, (int)(healthy * (0.30f + (float)_rng.NextDouble() * 0.30f) * Math.Max(0.3f, mult)));
                    try { target.Town.GarrisonParty.MemberRoster.AddToCounts(e.Character, 0, false, toWound); totalWounded += toWound; } catch { }
                }
            }
            catch { }
            string narr = totalWounded > 0
                ? $"The smoke travels to {name}. {totalWounded} soldier{(totalWounded != 1 ? "s are" : " is")} on their backs."
                : "The plague travels but finds no suitable garrison.";
            try { InformationManager.ShowInquiry(new InquiryData("Carrion Gift", narr, true, false, "Let it spread.", "", null, null)); }
            catch { MBInformationManager.AddQuickInformation(new TextObject(narr.Length > 80 ? narr.Substring(0,80)+"…" : narr)); }
        }

        private static void StartBreakWills()
        {
            float mult = AltarTraitMultiplier();
            int target = BreakWillsTargetLo + _rng.Next(BreakWillsTargetHi - BreakWillsTargetLo + 1);

            _lastBreakWillsDay = CurrentCampaignDay();
            _lastAltarUseDay   = CurrentCampaignDay();
            _altarUseCount++;
            RecordLocationUse();

            RunAltarRitual(
                "Break Hearts and Wills", target, mult, SacrificePerRound_Mid,
                () =>
                {
                    var candidates = Settlement.All
                        .Where(s => s.IsTown && s.MapFaction?.StringId != AshenKingdomId && s.Town != null)
                        .OrderByDescending(s => s.Town.Loyalty).Take(2).ToList();

                    if (candidates.Count == 0)
                    {
                        string noTarget = "The despair travels but finds no suitable city.";
                        try { InformationManager.ShowInquiry(new InquiryData("Break Hearts and Wills", noTarget, true, false, "Let them despair.", "", null, null)); } catch { }
                        try { GameMenu.SwitchToMenu("altar_menu"); } catch { }
                        return;
                    }

                    if (candidates.Count == 1)
                    {
                        ApplyBreakWillsToTarget(candidates[0], mult);
                        try { GameMenu.SwitchToMenu("altar_menu"); } catch { }
                        return;
                    }

                    var cityA = candidates[0]; var cityB = candidates[1];
                    string nameA = cityA.Name?.ToString() ?? "a distant city";
                    string nameB = cityB.Name?.ToString() ?? "another city";
                    int loyA = (int)(cityA.Town?.Loyalty ?? 0f);
                    int loyB = (int)(cityB.Town?.Loyalty ?? 0f);

                    try
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            "Which City Will You Hollow Out?",
                            $"Despair is a seed. These cities have soil for it.\n\n{nameA} — Loyalty: {loyA}.\n\n{nameB} — Loyalty: {loyB}.",
                            true, true, nameA, nameB,
                            () => { ApplyBreakWillsToTarget(cityA, mult); try { GameMenu.SwitchToMenu("altar_menu"); } catch { } },
                            () => { ApplyBreakWillsToTarget(cityB, mult); try { GameMenu.SwitchToMenu("altar_menu"); } catch { } }));
                    }
                    catch
                    {
                        ApplyBreakWillsToTarget(cityA, mult);
                        try { GameMenu.SwitchToMenu("altar_menu"); } catch { }
                    }
                },
                () => { ShowRitualFailure("Break Hearts and Wills"); try { GameMenu.SwitchToMenu("altar_menu"); } catch { } });
        }

        private static void ApplyBreakWillsToTarget(Settlement target, float mult)
        {
            string name    = target.Name?.ToString() ?? "a distant city";
            float loyDrain = (15f + (float)_rng.NextDouble() * 10f) * Math.Max(0.3f, mult);
            float secDrain = (15f + (float)_rng.NextDouble() * 10f) * Math.Max(0.3f, mult);
            try { target.Town.Loyalty  = Math.Max(0f, target.Town.Loyalty  - loyDrain); } catch { }
            try { target.Town.Security = Math.Max(0f, target.Town.Security - secDrain); } catch { }
            if (target.Town.Loyalty < 25f)
                try { MBInformationManager.AddQuickInformation(new TextObject($"In {name}, soldiers have deserted their posts.")); } catch { }
            string narr = $"In {name}, the guards are harder to rouse. Nobody can say why. Loyalty −{(int)loyDrain}. Security −{(int)secDrain}.";
            try { InformationManager.ShowInquiry(new InquiryData("Break Hearts and Wills", narr, true, false, "Let them despair.", "", null, null)); }
            catch { MBInformationManager.AddQuickInformation(new TextObject(narr.Length > 80 ? narr.Substring(0,80)+"…" : narr)); }
        }

        private static void StartColdFire()
        {
            float mult = AltarTraitMultiplier();
            int target = ColdFireTargetLo + _rng.Next(ColdFireTargetHi - ColdFireTargetLo + 1);

            _lastColdFireDay = CurrentCampaignDay();
            _lastAltarUseDay = CurrentCampaignDay();
            _altarUseCount++;
            RecordLocationUse();

            RunAltarRitual(
                "Rite of Cold Fire", target, mult, SacrificePerRound_Mid,
                () =>
                {
                    float px = 0f, py = 0f;
                    try { px = MobileParty.MainParty.GetPosition2D.x; py = MobileParty.MainParty.GetPosition2D.y; } catch { }
                    var target2 = MobileParty.All
                        .Where(p => { if (!p.IsActive || p.IsMainParty || p.MapFaction?.StringId == AshenKingdomId || p.LeaderHero == null) return false;
                                      float dx = p.GetPosition2D.x - px, dy = p.GetPosition2D.y - py;
                                      return dx * dx + dy * dy < 150f * 150f; })
                        .OrderBy(p => { float dx = p.GetPosition2D.x - px, dy = p.GetPosition2D.y - py; return dx * dx + dy * dy; })
                        .FirstOrDefault();

                    string narr;
                    if (target2 != null)
                    {
                        int toWound = (int)((8 + _rng.Next(8)) * Math.Max(0.3f, mult));
                        int w = 0;
                        foreach (var e in target2.MemberRoster.GetTroopRoster().ToList())
                        {
                            if (e.Character.IsHero) continue;
                            int healthy = e.Number - e.WoundedNumber;
                            int n = Math.Min(healthy, toWound - w); if (n <= 0) continue;
                            try { target2.MemberRoster.AddToCounts(e.Character, 0, false, n); w += n; } catch { }
                            if (w >= toWound) break;
                        }
                        try { target2.RecentEventsMorale -= 30f * Math.Max(0.3f, mult); } catch { }
                        _frozenPartyId  = target2.StringId ?? "";
                        _frozenUntilDay = CurrentCampaignDay() + ColdFreezeEffectDays;
                        narr = $"{target2.Name}'s column has halted. {w} soldier{(w != 1 ? "s are" : " is")} on one knee. " +
                            $"They will not march easily for {ColdFreezeEffectDays} days.";
                    }
                    else narr = "The cold fire rushes out and finds nothing close enough to settle on.";

                    try { InformationManager.ShowInquiry(new InquiryData("Rite of Cold Fire", narr, true, false, "Cold enough.", "", null, null)); }
                    catch { MBInformationManager.AddQuickInformation(new TextObject(narr.Length > 80 ? narr.Substring(0,80)+"…" : narr)); }
                    try { GameMenu.SwitchToMenu("altar_menu"); } catch { }
                },
                () => { ShowRitualFailure("Rite of Cold Fire"); try { GameMenu.SwitchToMenu("altar_menu"); } catch { } });
        }

        // ── Rite of Subjugation ────────────────────────────────────────────────
        private static bool CanAffordSubjugate()
        {
            try
            {
                int total = MobileParty.MainParty?.PrisonRoster?.GetTroopRoster()
                    .Where(e => !e.Character.IsHero).Sum(e => e.Number) ?? 0;
                return total >= 2;
            }
            catch { return false; }
        }

        private static void StartSubjugate()
        {
            float mult = AltarTraitMultiplier();
            int target = SubjugateTargetLo + _rng.Next(SubjugateTargetHi - SubjugateTargetLo + 1);

            _lastSubjugateDay = CurrentCampaignDay();
            _lastAltarUseDay  = CurrentCampaignDay();
            _altarUseCount++;
            RecordLocationUse();

            var prison = MobileParty.MainParty?.PrisonRoster;
            if (prison == null) { try { GameMenu.SwitchToMenu("altar_menu"); } catch { } return; }

            var prisoners = prison.GetTroopRoster()
                .Where(e => !e.Character.IsHero && e.Number > 0)
                .OrderBy(e => e.Character.Tier).ThenBy(e => e.Character.StringId).ToList();

            if (prisoners.Count == 0 || prisoners.Sum(e => e.Number) < 2)
            {
                MBInformationManager.AddQuickInformation(new TextObject("Rite of Subjugation — not enough prisoners."));
                try { GameMenu.SwitchToMenu("altar_menu"); } catch { }
                return;
            }

            int minTier = prisoners.Min(e => e.Character.Tier);
            int maxTier = prisoners.Max(e => e.Character.Tier);

            RunAltarRitual(
                "Rite of Subjugation", target, mult,
                0,  // no sacrifice per round — prisoners preserved for the conversion effect
                () =>
                {
                    // Success: offer choice if different tiers exist, else direct
                    if (minTier != maxTier)
                    {
                        var lowest  = prisoners.First();
                        var highest = prisoners.Last();
                        try
                        {
                            InformationManager.ShowInquiry(new InquiryData(
                                "Choose the Offering",
                                $"The altar does not care which one kneels. But the others will remember.\n\n" +
                                $"The lowest: {lowest.Character.Name} (converts at baseline).\n\n" +
                                $"The highest: {highest.Character.Name} (converts with +20 morale).",
                                true, true,
                                $"The lowest", $"The highest",
                                () => { PerformSubjugateStandard(prison, prisoners, false); try { GameMenu.SwitchToMenu("altar_menu"); } catch { } },
                                () => { PerformSubjugateStandard(prison, prisoners, true);  try { GameMenu.SwitchToMenu("altar_menu"); } catch { } }));
                        }
                        catch
                        {
                            PerformSubjugateStandard(prison, prisoners, false);
                            try { GameMenu.SwitchToMenu("altar_menu"); } catch { }
                        }
                    }
                    else
                    {
                        PerformSubjugateStandard(prison, prisoners, false);
                        try { GameMenu.SwitchToMenu("altar_menu"); } catch { }
                    }
                },
                () => { ShowRitualFailure("Rite of Subjugation"); try { GameMenu.SwitchToMenu("altar_menu"); } catch { } },
                moralePerRound: 20f);
        }

        private static void PerformSubjugateStandard(
            TaleWorlds.CampaignSystem.Roster.TroopRoster prison,
            List<TaleWorlds.CampaignSystem.Roster.TroopRosterElement> prisoners,
            bool sacrificeHighest)
        {
            var sacrifice = sacrificeHighest ? prisoners.Last() : prisoners.First();
            try { prison.AddToCounts(sacrifice.Character, -1); } catch { }
            string sacrificeName = sacrifice.Character.Name?.ToString() ?? "a prisoner";

            var remaining = prison.GetTroopRoster().Where(e => !e.Character.IsHero && e.Number > 0).ToList();
            var roster = MobileParty.MainParty.MemberRoster;
            int converted = 0;
            foreach (var e in remaining)
                try { int n = e.Number; prison.AddToCounts(e.Character, -n); roster.AddToCounts(e.Character, n); converted += n; } catch { }

            try { MobileParty.MainParty.RecentEventsMorale -= 10f; } catch { }
            if (sacrificeHighest) try { MobileParty.MainParty.RecentEventsMorale += 20f; } catch { }

            string narr = converted > 0
                ? $"The altar takes {sacrificeName}. The others cross over before the priest finishes speaking. {converted} of them serve you now."
                : $"The sacrifice is made. The fire takes {sacrificeName}. The altar does not negotiate.";
            try { InformationManager.ShowInquiry(new InquiryData("Rite of Subjugation", narr, true, false, converted > 0 ? "They serve now." : "The price is paid.", "", null, null)); }
            catch { MBInformationManager.AddQuickInformation(new TextObject(converted > 0 ? $"{sacrificeName} consumed. {converted} prisoner{(converted!=1?"s":"")} join your ranks." : $"{sacrificeName} sacrificed.")); }
        }

        // ── NPC daily tick ─────────────────────────────────────────────────────
        private static void OnDailyTick()
        {
            int today = CurrentCampaignDay();

            // Solstice passive benefits
            if (_solsticeUntilDay >= today && MobileParty.MainParty != null)
            {
                if (_solsticeType == "winter") { }
                else if (_solsticeType == "sun")
                {
                    try { MobileParty.MainParty.RecentEventsMorale += 0.5f; } catch { }
                }
            }
            else if (_solsticeUntilDay >= 0 && today > _solsticeUntilDay)
            { _solsticeType = ""; _solsticeUntilDay = -1; }

            // Cold Fire freeze: re-apply each day
            if (!string.IsNullOrEmpty(_frozenPartyId) && _frozenUntilDay >= today)
            {
                var frozen = MobileParty.All.FirstOrDefault(p => p.StringId == _frozenPartyId && p.IsActive);
                if (frozen != null)
                {
                    try { frozen.RecentEventsMorale -= 20f; } catch { }
                    int toWound = 2 + _rng.Next(2), w = 0;
                    foreach (var e in frozen.MemberRoster.GetTroopRoster().ToList())
                    {
                        if (e.Character.IsHero) continue;
                        int healthy = e.Number - e.WoundedNumber;
                        int n = Math.Min(healthy, toWound - w); if (n <= 0) continue;
                        try { frozen.MemberRoster.AddToCounts(e.Character, 0, false, n); w += n; } catch { }
                        if (w >= toWound) break;
                    }
                }
            }
            else if (today > _frozenUntilDay && !string.IsNullOrEmpty(_frozenPartyId))
            { _frozenPartyId = ""; _frozenUntilDay = -1; }

            // Trait drift: every 10 altar uses, nudge highest trait down
            if (_altarUseCount > 0 && _altarUseCount % TraitDriftThreshold == 0)
            {
                try
                {
                    var h = Hero.MainHero;
                    if (h != null)
                    {
                        int mercy = h.GetTraitLevel(DefaultTraits.Mercy);
                        int honor = h.GetTraitLevel(DefaultTraits.Honor);
                        int gen   = h.GetTraitLevel(DefaultTraits.Generosity);
                        if (mercy >= honor && mercy >= gen && mercy > -2)      h.SetTraitLevel(DefaultTraits.Mercy, mercy - 1);
                        else if (honor >= mercy && honor >= gen && honor > -2) h.SetTraitLevel(DefaultTraits.Honor, honor - 1);
                        else if (gen > -2)                                      h.SetTraitLevel(DefaultTraits.Generosity, gen - 1);
                        MBInformationManager.AddQuickInformation(new TextObject(
                            "The cold has changed you. Something that was soft in you has hardened."));
                    }
                }
                catch { }
                _altarUseCount = 0;
            }

            // ── Ashen lords in altar cities: dark rites ──────────────────────
            try
            {
                foreach (var hero in Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && !h.IsPrisoner && !h.IsChild
                             && h != Hero.MainHero && h.CurrentSettlement != null
                             && HasAshenAltar(h.CurrentSettlement) && NpcCanUseAltar(h))
                    .OrderBy(_ => _rng.Next()).Take(6))
                {
                    if (_rng.NextDouble() > 0.005) continue;

                    float mult = NpcAltarMult(hero);
                    // Simulate ritual (3 rounds), only apply if success
                    bool success = SimulateNpcRitual(BloodTargetLo, BloodTargetHi, mult, 3);
                    if (!success) continue;

                    string city = hero.CurrentSettlement?.Name?.ToString() ?? "the altar";
                    switch (_rng.Next(5))
                    {
                        case 0:
                            NpcHealPartyPartial(hero.PartyBelongedTo, 0.20f);
                            InformationManager.DisplayMessage(new InformationMessage(
                                $"{hero.Name} — dark rite at the altar in {city}. The injured recovered swiftly. Something was paid for it.",
                                new Color(0.38f, 0.50f, 0.75f)));
                            break;
                        case 1:
                            NpcBoostMorale(hero.PartyBelongedTo, 20f);
                            InformationManager.DisplayMessage(new InformationMessage(
                                $"{hero.Name} — blood rite at the altar in {city}. The survivors march with cold resolve.",
                                new Color(0.38f, 0.50f, 0.75f)));
                            break;
                        case 2:
                            NpcCurseNearbyParty(hero.PartyBelongedTo);
                            InformationManager.DisplayMessage(new InformationMessage(
                                $"{hero.Name} — curse whispered at the altar in {city}. Something reached out and touched an enemy in the dark.",
                                new Color(0.38f, 0.50f, 0.75f)));
                            break;
                        case 3:
                            if (NpcCarrionGiftGarrison(out string plagueTarget))
                                InformationManager.DisplayMessage(new InformationMessage(
                                    $"{hero.Name} — carrion rite at the altar in {city}. A grey sickness reached {plagueTarget}.",
                                    new Color(0.38f, 0.50f, 0.75f)));
                            break;
                        case 4:
                            if (NpcBreakWillsCity(out string despairTarget))
                                InformationManager.DisplayMessage(new InformationMessage(
                                    $"{hero.Name} — despair rite at the altar in {city}. {despairTarget} grows restless and fearful.",
                                    new Color(0.38f, 0.50f, 0.75f)));
                            break;
                    }
                }
            }
            catch { }
        }

        // Simulates rounds of a ritual for an NPC. Returns true if accumulated >= target.
        private static bool SimulateNpcRitual(int targetLo, int targetHi, float mult, int rounds)
        {
            int target = targetLo + _rng.Next(Math.Max(1, targetHi - targetLo + 1));
            int acc = 0;
            for (int i = 0; i < rounds; i++) acc += RollRoundPoints(mult);
            return acc >= target;
        }

        // ── NPC effect helpers ─────────────────────────────────────────────────
        private static void NpcHealPartyPartial(MobileParty party, float fraction)
        {
            if (party?.MemberRoster == null) return;
            foreach (var e in party.MemberRoster.GetTroopRoster().ToList())
            {
                if (e.Character.IsHero || e.WoundedNumber <= 0) continue;
                int heal = Math.Max(1, (int)(e.WoundedNumber * fraction));
                try { party.MemberRoster.AddToCounts(e.Character, 0, false, -heal); } catch { }
            }
        }

        private static void NpcBoostMorale(MobileParty party, float amount)
        {
            if (party == null) return;
            try { party.RecentEventsMorale += amount; } catch { }
        }

        private static bool NpcCarrionGiftGarrison(out string targetName)
        {
            targetName = "";
            try
            {
                var candidates = Settlement.All
                    .Where(s => s.IsTown && s.MapFaction?.StringId != AshenKingdomId
                             && s.Town?.GarrisonParty?.MemberRoster?.TotalManCount > 0).ToList();
                if (candidates.Count == 0) return false;
                var target = candidates[_rng.Next(candidates.Count)];
                targetName = target.Name?.ToString() ?? "a distant garrison";
                foreach (var e in target.Town.GarrisonParty.MemberRoster.GetTroopRoster().ToList())
                {
                    if (e.Character.IsHero) continue;
                    int healthy = e.Number - e.WoundedNumber; if (healthy <= 0) continue;
                    int toWound = Math.Max(1, (int)(healthy * (0.10f + (float)_rng.NextDouble() * 0.10f)));
                    try { target.Town.GarrisonParty.MemberRoster.AddToCounts(e.Character, 0, false, toWound); } catch { }
                }
                return true;
            }
            catch { return false; }
        }

        private static bool NpcBreakWillsCity(out string targetName)
        {
            targetName = "";
            try
            {
                var candidates = Settlement.All
                    .Where(s => s.IsTown && s.MapFaction?.StringId != AshenKingdomId && s.Town != null).ToList();
                if (candidates.Count == 0) return false;
                var target = candidates[_rng.Next(candidates.Count)];
                targetName = target.Name?.ToString() ?? "a distant city";
                float drain = 5f + (float)_rng.NextDouble() * 5f;
                try { target.Town.Loyalty  = Math.Max(0f, target.Town.Loyalty  - drain); } catch { }
                try { target.Town.Security = Math.Max(0f, target.Town.Security - drain); } catch { }
                return true;
            }
            catch { return false; }
        }

        private static void NpcCurseNearbyParty(MobileParty source)
        {
            if (source == null) return;
            float sx = 0f, sy = 0f;
            try { sx = source.GetPosition2D.x; sy = source.GetPosition2D.y; } catch { return; }
            const float rangeSquared = 80f * 80f;
            var target = MobileParty.All
                .Where(p => { if (!p.IsActive || p.MapFaction?.StringId == AshenKingdomId) return false;
                              float dx = p.GetPosition2D.x - sx, dy = p.GetPosition2D.y - sy;
                              return dx * dx + dy * dy < rangeSquared; })
                .OrderBy(p => { float dx = p.GetPosition2D.x - sx, dy = p.GetPosition2D.y - sy; return dx * dx + dy * dy; })
                .FirstOrDefault();
            if (target == null) return;
            int toWound = 3 + _rng.Next(5), w = 0;
            foreach (var e in target.MemberRoster.GetTroopRoster().ToList())
            {
                if (e.Character.IsHero) continue;
                int healthy = e.Number - e.WoundedNumber;
                int n = Math.Min(healthy, toWound - w); if (n <= 0) continue;
                try { target.MemberRoster.AddToCounts(e.Character, 0, false, n); w += n; } catch { }
                if (w >= toWound) break;
            }
            try { target.RecentEventsMorale -= 15f; } catch { }
        }
    }
}
