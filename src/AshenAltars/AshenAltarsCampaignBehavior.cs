// =============================================================================
// ASH AND EMBER — AshenAltars/AshenAltarsCampaignBehavior.cs
//
// Adds "Visit the Ashen Altar" to the town menu in:
//   • All starting Ashen cities: Tyal, Sibir, Baltakhand, and Amprela.
//
// Access requires Merciless (Mercy ≤ −1) AND Devious (Honor ≤ −1).
//
// Each rite demands a blood sacrifice: prisoners are drained first (lowest-tier),
// then healthy party members if more points are needed. A tier-N troop is worth
// N sacrifice points. Party morale drains proportional to the sacrifice cost.
// No gold is required — only lives.
//
// Rites:
//   Blood Tribute          — spill blood; the survivors grow stronger (party XP).
//   The Ashen Solstice     — call down an Iron Winter or Scorching Sun.
//   Carrion Gift           — a grey plague descends on a player-chosen garrison.
//   Break Hearts and Wills — sow cold despair in a player-chosen enemy city.
//   Rite of Cold Fire      — curse a nearby enemy party (wounds + morale + freeze).
//   Rite of Subjugation    — sacrifice one prisoner, convert the rest (tier choice).
//
// NPC effects (daily tick):
//   • Ashen lords in an altar city: 0.5 % chance/day to perform a dark rite
//     (partial healing, morale, or curse). Shows a campaign-map notification.
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
        private const int SacrificePtsBloodTribute  =     5;
        private const int SacrificePtsAshenSolstice =    10;
        private const int SacrificePtsCarrionGift   =     8;
        private const int SacrificePtsBreakWills    =     6;
        private const int SacrificePtsColdFire      =     7;

        private const float MoralePerSacrificePoint =   3f;   // morale lost per sacrifice point spent
        private const int   XpPerBloodTribute       =   75;   // XP added per surviving troop type

        private const int BloodTributeCooldownBase  =  3;
        private const int SolsticeCooldownBase      = 14;
        private const int CarrionGiftCooldownBase   =  5;
        private const int BreakWillsCooldownBase    =  5;
        private const int ColdFireCooldownBase      =  3;
        private const int SubjugateCooldownBase     =  5;
        private const int TraitDriftThreshold       = 10;
        private const int SolsticeBuffDays          = 30;
        private const int ColdFreezeEffectDays      =  2;
        private const int CrossInterferenceDays     = 30;

        private const string AshenKingdomId = "ashen_kingdom";

        // All starting Ashen cities that permanently host an altar.
        private static readonly string[] AshenAltarCities = { "Tyal", "Sibir", "Baltakhand", "Amprela" };

        private static readonly Random _rng = new Random();

        // Cross-system state (read by SanctuaryCampaignBehavior)
        internal static int _lastAltarUseDay     = -999;
        private static int  _altarUseCount       = 0;

        // Solstice benefit tracking
        private static int    _solsticeUntilDay  = -1;
        private static string _solsticeType      = "";   // "winter" or "sun"

        // Cold Fire freeze tracking
        private static string _frozenPartyId    = "";
        private static int    _frozenUntilDay   = -1;

        // Per-rite cooldown tracking
        private static int _lastBloodTributeDay  = -999;
        private static int _lastSolsticeDay      = -999;
        private static int _lastCarrionDay       = -999;
        private static int _lastBreakWillsDay    = -999;
        private static int _lastColdFireDay      = -999;
        private static int _lastSubjugateDay     = -999;

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
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            AnnounceAltars();
            RegisterAltarMenus(starter);
        }

        // ── Startup announcement ───────────────────────────────────────────────
        private static void AnnounceAltars()
        {
            try
            {
                string names = string.Join(", ", AshenAltarCities);
                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Ashen Altars stand in {names}. " +
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
            try { return (int)CampaignTime.Now.ToDays; }
            catch { return 0; }
        }

        private static bool IsRiteOnCooldown(int lastDay, int baseCooldown, float mult)
        {
            int elapsed  = CurrentCampaignDay() - lastDay;
            float absM   = Math.Min(1f, Math.Abs(mult));
            int cooldown = Math.Max(1, (int)(baseCooldown * (2f - absM)));
            return elapsed < cooldown;
        }

        // Returns -1.0 to +1.0. Positive = altar rewards the dark soul; 0 = no effect; negative = penalty.
        // Based on reversed Mercy + Honor + Generosity: the more evil, the stronger the benefit.
        private static float AltarTraitMultiplier()
        {
            var h = Hero.MainHero;
            if (h == null) return 0f;
            try
            {
                int mercy = h.GetTraitLevel(DefaultTraits.Mercy);
                int honor = h.GetTraitLevel(DefaultTraits.Honor);
                int gen   = h.GetTraitLevel(DefaultTraits.Generosity);
                // -6 = max evil → multiplier 1.0; 0 → 0; +6 = max good → -1.0
                float raw = -(mercy + honor + gen) / 6f;

                // Cross-system: recent sanctuary use dilutes the cold
                int sanctDay = SanctuaryCampaignBehavior._lastSanctuaryUseDay;
                if (CurrentCampaignDay() - sanctDay < CrossInterferenceDays)
                    raw *= 0.5f;

                return Math.Max(-1f, Math.Min(1f, raw));
            }
            catch { return 0f; }
        }

        private static string AltarTraitNote(float mult)
        {
            if (mult >= 0.8f)  return "  [The cold knows you — full power]";
            if (mult >= 0.4f)  return "  [Partial power]";
            if (mult >= 0.01f) return "  [Faint dark blessing]";
            if (mult >= -0.01f)return "  [No benefit — you are not cold enough]";
            if (mult >= -0.5f) return "  [PENALTY — the altar punishes your warmth]";
            return "  [HEAVY PENALTY — the grey flame burns against you]";
        }

        private static bool PlayerCanUseAltar() => true;

        private static bool NpcCanUseAltar(Hero h)
        {
            try
            {
                if (h.Clan?.Kingdom?.StringId == AshenKingdomId) return true;
                return h.GetTraitLevel(DefaultTraits.Mercy)  <= -1
                    && h.GetTraitLevel(DefaultTraits.Honor) <= -1;
            }
            catch { return false; }
        }

        // ── Sacrifice helpers ─────────────────────────────────────────────────
        // Total points = all prisoners (tier × count) + healthy party members (tier × healthy count).
        private static int TotalSacrificePoints()
        {
            int total = 0;
            try
            {
                var prisoners = MobileParty.MainParty?.PrisonRoster;
                if (prisoners != null)
                    foreach (var e in prisoners.GetTroopRoster())
                        if (!e.Character.IsHero)
                            total += e.Number * Math.Max(1, e.Character.Tier);
            }
            catch { }
            try
            {
                var party = MobileParty.MainParty?.MemberRoster;
                if (party != null)
                    foreach (var e in party.GetTroopRoster())
                        if (!e.Character.IsHero)
                            total += (e.Number - e.WoundedNumber) * Math.Max(1, e.Character.Tier);
            }
            catch { }
            return total;
        }

        private static bool CanAffordRite(int sacrificePoints)
            => TotalSacrificePoints() >= ModifiedSacrificePoints(sacrificePoints);

        // Kills the minimum number needed to satisfy sacrificePoints.
        // Prisoners are drained first (lowest-tier), then healthy party members.
        // Drains morale proportional to points spent. Returns total killed.
        private static int SacrificeForRite(int sacrificePoints)
        {
            int modifiedPoints = ModifiedSacrificePoints(sacrificePoints);
            int remaining   = modifiedPoints;
            int totalKilled = 0;

            // Drain prisoners first
            try
            {
                var prisoners = MobileParty.MainParty?.PrisonRoster;
                if (prisoners != null)
                {
                    var prisonerList = prisoners.GetTroopRoster()
                        .Where(e => !e.Character.IsHero && e.Number > 0)
                        .OrderBy(e => e.Character.Tier)
                        .ThenBy(e => e.Character.StringId)
                        .ToList();
                    foreach (var entry in prisonerList)
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

            // Then drain party members if more points are still needed
            if (remaining > 0)
            {
                try
                {
                    var roster = MobileParty.MainParty?.MemberRoster;
                    if (roster != null)
                    {
                        var troops = roster.GetTroopRoster()
                            .Where(e => !e.Character.IsHero && (e.Number - e.WoundedNumber) > 0)
                            .OrderBy(e => e.Character.Tier)
                            .ThenBy(e => e.Character.StringId)
                            .ToList();
                        foreach (var entry in troops)
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

            int pointsSpent = modifiedPoints - Math.Max(0, remaining);
            try { MobileParty.MainParty.RecentEventsMorale -= pointsSpent * MoralePerSacrificePoint; } catch { }

            return totalKilled;
        }

        private static int ModifiedSacrificePoints(int basePts)
        {
            try
            {
                int gen = Hero.MainHero?.GetTraitLevel(DefaultTraits.Generosity) ?? 0;
                // Low generosity (mean/greedy) costs less blood; high generosity costs more
                // gen=-2: -25%, gen=-1: -12.5%, gen=1: +12.5%, gen=2: +25%
                float modifier = 1f + gen * 0.125f;
                return Math.Max(1, (int)(basePts * modifier));
            }
            catch { return basePts; }
        }

        // ── Menu registration ──────────────────────────────────────────────────
        private static void RegisterAltarMenus(CampaignGameStarter starter)
        {
            // ── Entry in the main town menu ─────────────────────────────────
            try
            {
                starter.AddGameMenuOption(
                    "town", "altar_enter",
                    "{ALTAR_ENTER_TEXT}",
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

            // ── Altar sub-menu ──────────────────────────────────────────────
            try
            {
                starter.AddGameMenu(
                    "altar_menu",
                    "{ALTAR_MENU_HEADER}",
                    args =>
                    {
                        try
                        {
                            int today = CurrentCampaignDay();
                            int pts = TotalSacrificePoints();
                            string ptsNote = pts > 0 ? $"  [Sacrifice available: {pts} pts]" : "";

                            string solsticeNote = _solsticeUntilDay >= today
                                ? $"  [Solstice ({_solsticeType}): {_solsticeUntilDay - today + 1} day(s) remaining]" : "";

                            string frozenNote = "";
                            if (!string.IsNullOrEmpty(_frozenPartyId) && _frozenUntilDay >= today)
                            {
                                var frozenParty = MobileParty.All.FirstOrDefault(p => p.StringId == _frozenPartyId && p.IsActive);
                                string frozenName = frozenParty?.Name?.ToString() ?? _frozenPartyId;
                                frozenNote = $"  [Cold Fire freeze on {frozenName}: {_frozenUntilDay - today + 1} day(s) remaining]";
                            }

                            MBTextManager.SetTextVariable("ALTAR_MENU_HEADER",
                                $"The Ashen Altar. Stone worn smooth by blood that never fully dried. " +
                                $"The flame here is grey, and it is always hungry.{ptsNote}{solsticeNote}{frozenNote}");
                        }
                        catch { }
                    });
            }
            catch { }

            // ── Blood Tribute ───────────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption(
                    "altar_menu", "altar_bloodtribute",
                    "{ALTAR_BLOODTRIBUTE_TEXT}",
                    args =>
                    {
                        try
                        {
                            float mult = AltarTraitMultiplier();
                            int pts = ModifiedSacrificePoints(SacrificePtsBloodTribute);
                            string cooldownNote = "";
                            if (IsRiteOnCooldown(_lastBloodTributeDay, BloodTributeCooldownBase, mult))
                            {
                                args.IsEnabled = false;
                                float absM = Math.Min(1f, Math.Abs(mult));
                                int cooldown = Math.Max(1, (int)(BloodTributeCooldownBase * (2f - absM)));
                                int daysLeft = cooldown - (CurrentCampaignDay() - _lastBloodTributeDay);
                                cooldownNote = $"  [On cooldown: {daysLeft} day(s)]";
                            }
                            else
                            {
                                args.IsEnabled = CanAffordRite(SacrificePtsBloodTribute);
                            }
                            MBTextManager.SetTextVariable("ALTAR_BLOODTRIBUTE_TEXT",
                                $"Blood Tribute ({pts} sacrifice pts)" +
                                $" — spill blood so the survivors grow stronger{cooldownNote}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => PerformBloodTribute());
            }
            catch { }

            // ── The Ashen Solstice ──────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption(
                    "altar_menu", "altar_solstice",
                    "{ALTAR_SOLSTICE_TEXT}",
                    args =>
                    {
                        try
                        {
                            float mult = AltarTraitMultiplier();
                            int pts = ModifiedSacrificePoints(SacrificePtsAshenSolstice);
                            string cooldownNote = "";
                            if (IsRiteOnCooldown(_lastSolsticeDay, SolsticeCooldownBase, mult))
                            {
                                args.IsEnabled = false;
                                float absM = Math.Min(1f, Math.Abs(mult));
                                int cooldown = Math.Max(1, (int)(SolsticeCooldownBase * (2f - absM)));
                                int daysLeft = cooldown - (CurrentCampaignDay() - _lastSolsticeDay);
                                cooldownNote = $"  [On cooldown: {daysLeft} day(s)]";
                            }
                            else
                            {
                                args.IsEnabled = CanAffordRite(SacrificePtsAshenSolstice);
                            }
                            MBTextManager.SetTextVariable("ALTAR_SOLSTICE_TEXT",
                                $"The Ashen Solstice ({pts} sacrifice pts)" +
                                $" — call down an Iron Winter or Scorching Sun{cooldownNote}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => PerformAshenSolstice());
            }
            catch { }

            // ── Carrion Gift ────────────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption(
                    "altar_menu", "altar_carrion",
                    "{ALTAR_CARRION_TEXT}",
                    args =>
                    {
                        try
                        {
                            float mult = AltarTraitMultiplier();
                            int pts = ModifiedSacrificePoints(SacrificePtsCarrionGift);
                            string cooldownNote = "";
                            if (IsRiteOnCooldown(_lastCarrionDay, CarrionGiftCooldownBase, mult))
                            {
                                args.IsEnabled = false;
                                float absM = Math.Min(1f, Math.Abs(mult));
                                int cooldown = Math.Max(1, (int)(CarrionGiftCooldownBase * (2f - absM)));
                                int daysLeft = cooldown - (CurrentCampaignDay() - _lastCarrionDay);
                                cooldownNote = $"  [On cooldown: {daysLeft} day(s)]";
                            }
                            else
                            {
                                args.IsEnabled = CanAffordRite(SacrificePtsCarrionGift);
                            }
                            MBTextManager.SetTextVariable("ALTAR_CARRION_TEXT",
                                $"Carrion Gift ({pts} sacrifice pts)" +
                                $" — send a grey plague to a distant garrison{cooldownNote}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => PerformCarrionGift());
            }
            catch { }

            // ── Break Hearts and Wills ──────────────────────────────────────
            try
            {
                starter.AddGameMenuOption(
                    "altar_menu", "altar_breakwills",
                    "{ALTAR_BREAKWILLS_TEXT}",
                    args =>
                    {
                        try
                        {
                            float mult = AltarTraitMultiplier();
                            int pts = ModifiedSacrificePoints(SacrificePtsBreakWills);
                            string cooldownNote = "";
                            if (IsRiteOnCooldown(_lastBreakWillsDay, BreakWillsCooldownBase, mult))
                            {
                                args.IsEnabled = false;
                                float absM = Math.Min(1f, Math.Abs(mult));
                                int cooldown = Math.Max(1, (int)(BreakWillsCooldownBase * (2f - absM)));
                                int daysLeft = cooldown - (CurrentCampaignDay() - _lastBreakWillsDay);
                                cooldownNote = $"  [On cooldown: {daysLeft} day(s)]";
                            }
                            else
                            {
                                args.IsEnabled = CanAffordRite(SacrificePtsBreakWills);
                            }
                            MBTextManager.SetTextVariable("ALTAR_BREAKWILLS_TEXT",
                                $"Break Hearts and Wills ({pts} sacrifice pts)" +
                                $" — sow cold despair in an enemy city{cooldownNote}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => PerformBreakWills());
            }
            catch { }

            // ── Rite of Cold Fire ───────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption(
                    "altar_menu", "altar_coldfire",
                    "{ALTAR_COLDFIRE_TEXT}",
                    args =>
                    {
                        try
                        {
                            float mult = AltarTraitMultiplier();
                            int pts = ModifiedSacrificePoints(SacrificePtsColdFire);
                            string cooldownNote = "";
                            if (IsRiteOnCooldown(_lastColdFireDay, ColdFireCooldownBase, mult))
                            {
                                args.IsEnabled = false;
                                float absM = Math.Min(1f, Math.Abs(mult));
                                int cooldown = Math.Max(1, (int)(ColdFireCooldownBase * (2f - absM)));
                                int daysLeft = cooldown - (CurrentCampaignDay() - _lastColdFireDay);
                                cooldownNote = $"  [On cooldown: {daysLeft} day(s)]";
                            }
                            else
                            {
                                args.IsEnabled = CanAffordRite(SacrificePtsColdFire);
                            }
                            MBTextManager.SetTextVariable("ALTAR_COLDFIRE_TEXT",
                                $"Rite of Cold Fire ({pts} sacrifice pts)" +
                                $" — curse a nearby enemy party{cooldownNote}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => PerformColdFire());
            }
            catch { }

            // ── Rite of Subjugation ─────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption(
                    "altar_menu", "altar_subjugate",
                    "{ALTAR_SUBJUGATE_TEXT}",
                    args =>
                    {
                        try
                        {
                            float mult = AltarTraitMultiplier();
                            string cooldownNote = "";
                            if (IsRiteOnCooldown(_lastSubjugateDay, SubjugateCooldownBase, mult))
                            {
                                args.IsEnabled = false;
                                float absM = Math.Min(1f, Math.Abs(mult));
                                int cooldown = Math.Max(1, (int)(SubjugateCooldownBase * (2f - absM)));
                                int daysLeft = cooldown - (CurrentCampaignDay() - _lastSubjugateDay);
                                cooldownNote = $"  [On cooldown: {daysLeft} day(s)]";
                            }
                            else
                            {
                                args.IsEnabled = CanAffordSubjugate();
                            }
                            MBTextManager.SetTextVariable("ALTAR_SUBJUGATE_TEXT",
                                "Rite of Subjugation (1 prisoner sacrificed)" +
                                $" — give one to the fire, claim the rest{cooldownNote}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => PerformSubjugate());
            }
            catch { }

            // ── Leave ───────────────────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption(
                    "altar_menu", "altar_leave",
                    "Leave the Altar",
                    args =>
                    {
                        try { args.optionLeaveType = GameMenuOption.LeaveType.Leave; } catch { }
                        return true;
                    },
                    args => { try { GameMenu.SwitchToMenu("town"); } catch { } },
                    true, -1, false);
            }
            catch { }
        }

        // ── Rite: Blood Tribute ────────────────────────────────────────────────
        private static void PerformBloodTribute()
        {
            try
            {
                float mult   = AltarTraitMultiplier();
                int   killed = SacrificeForRite(SacrificePtsBloodTribute);
                string narrative;

                var roster = MobileParty.MainParty?.MemberRoster;
                if (mult > 0.01f)
                {
                    int xp = Math.Max(1, (int)(XpPerBloodTribute * mult));
                    if (roster != null)
                        foreach (var e in roster.GetTroopRoster().ToList())
                        {
                            if (e.Character.IsHero) continue;
                            try { roster.AddToCounts(e.Character, 0, false, 0, xp); } catch { }
                        }

                    // Witnesses carry the stain — one unit type is shaken
                    try { MobileParty.MainParty.RecentEventsMorale -= 15f; } catch { }
                    // But survivors are energised — net positive for full evil
                    try { MobileParty.MainParty.RecentEventsMorale += 15f * mult; } catch { }

                    // Pick a random non-hero troop type for flavour
                    string troopName = "soldier";
                    try
                    {
                        var nonHeroTroops = roster?.GetTroopRoster().Where(e => !e.Character.IsHero && e.Number > 0).ToList();
                        if (nonHeroTroops != null && nonHeroTroops.Count > 0)
                            troopName = nonHeroTroops[_rng.Next(nonHeroTroops.Count)].Character.Name?.ToString() ?? "soldier";
                    }
                    catch { }

                    narrative = killed > 0
                        ? $"The blade does not hesitate. The man who kneels does not beg. The survivors watch without expression. " +
                          $"By morning they carry themselves differently. {killed} paid the altar its price. The rest are better for it.\n\n" +
                          $"The {troopName} who stood closest have not spoken since."
                        : "The altar receives the offering. Your men will know it in their steps by morning.";

                    _lastBloodTributeDay = CurrentCampaignDay();
                    _lastAltarUseDay = CurrentCampaignDay();
                    _altarUseCount++;
                }
                else if (mult < -0.01f)
                {
                    // Penalty: the blood sacrifice angers the grey flame — it drains your troops instead
                    int toWound = Math.Max(1, (int)(5 * Math.Abs(mult)));
                    int wounded = 0;
                    if (roster != null)
                        foreach (var e in roster.GetTroopRoster().ToList())
                        {
                            if (e.Character.IsHero) continue;
                            int healthy = e.Number - e.WoundedNumber;
                            int w = Math.Min(healthy, toWound - wounded);
                            if (w <= 0) continue;
                            try { roster.AddToCounts(e.Character, 0, false, w); wounded += w; } catch { }
                            if (wounded >= toWound) break;
                        }
                    narrative = $"The grey flame refuses the offering. It takes the warmth in the blood as an insult — your troops recoil from the altar. " +
                        $"{wounded} soldier{(wounded != 1 ? "s are" : " is")} worse for having been near it. The cold does not forgive softness.";
                    _lastBloodTributeDay = CurrentCampaignDay();
                    _lastAltarUseDay = CurrentCampaignDay();
                    _altarUseCount++;
                }
                else
                {
                    narrative = "The blood is spilled. The altar does not respond — the grey flame finds nothing in you worth rewarding. " +
                        "You have paid. Nothing changed.";
                    _lastBloodTributeDay = CurrentCampaignDay();
                    _lastAltarUseDay = CurrentCampaignDay();
                    _altarUseCount++;
                }

                try
                {
                    InformationManager.ShowInquiry(new InquiryData(
                        "Blood Tribute", narrative, true, false, "The blood is spent.", "", null, null));
                }
                catch { MBInformationManager.AddQuickInformation(new TextObject(
                    $"Blood Tribute — {killed} slain at the altar.")); }
            }
            catch { }
            finally { try { GameMenu.SwitchToMenu("altar_menu"); } catch { } }
        }

        // ── Rite: The Ashen Solstice ───────────────────────────────────────────
        private static void PerformAshenSolstice()
        {
            float mult = AltarTraitMultiplier();
            try
            {
                if (mult <= 0.01f)
                {
                    int killed = SacrificeForRite(SacrificePtsAshenSolstice);
                    _lastSolsticeDay = CurrentCampaignDay();
                    _lastAltarUseDay = CurrentCampaignDay();
                    _altarUseCount++;
                    string msg = mult < -0.01f
                        ? $"The Ashen Solstice — the ritual backfires. The altar turns the seasons against your own lands. {killed} sacrificed in vain."
                        : $"The Ashen Solstice — {killed} sacrificed, but the grey flame finds nothing in you worth the season's toll. Nothing changes.";
                    MBInformationManager.AddQuickInformation(new TextObject(msg));
                }
                else
                {
                    try
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            "The Ashen Solstice",
                            "The altar waits. Which season will you call down?\n\n" +
                            "Iron Winter grips the north — the cold that breaks rivers and empties granaries.\n\n" +
                            "Scorching Sun burns the south — the sky white with heat that cracks the wells.",
                            true, true,
                            "Iron Winter (north)", "Scorching Sun (south)",
                            () =>
                            {
                                int killed = SacrificeForRite(SacrificePtsAshenSolstice);
                                CampaignMapEvents.ForceIronWinter();
                                _solsticeType = "winter";
                                _solsticeUntilDay = CurrentCampaignDay() + SolsticeBuffDays;
                                _lastSolsticeDay = CurrentCampaignDay();
                                _lastAltarUseDay = CurrentCampaignDay();
                                _altarUseCount++;
                                string narrative = $"The Ashen Solstice — {killed} soul{(killed != 1 ? "s" : "")} paid the cold. The north darkens.\n\n" +
                                    "The cold obeys you. Your own men feel the ice as armour, not a wound. Food will stretch further in the coming month.";
                                try
                                {
                                    InformationManager.ShowInquiry(new InquiryData(
                                        "Iron Winter Called", narrative, true, false, "The north will remember.", "", null, null));
                                }
                                catch { MBInformationManager.AddQuickInformation(new TextObject(narrative.Length > 120 ? narrative.Substring(0, 120) + "…" : narrative)); }
                            },
                            () =>
                            {
                                int killed = SacrificeForRite(SacrificePtsAshenSolstice);
                                CampaignMapEvents.ForceScorchingSun();
                                _solsticeType = "sun";
                                _solsticeUntilDay = CurrentCampaignDay() + SolsticeBuffDays;
                                _lastSolsticeDay = CurrentCampaignDay();
                                _lastAltarUseDay = CurrentCampaignDay();
                                _altarUseCount++;
                                string narrative = $"The Ashen Solstice — {killed} soul{(killed != 1 ? "s" : "")} turned to smoke. The south bakes.\n\n" +
                                    "The heat bends to your will. Your column moves with the sun at your back, and it does not slow you.";
                                try
                                {
                                    InformationManager.ShowInquiry(new InquiryData(
                                        "Scorching Sun Called", narrative, true, false, "The south will burn.", "", null, null));
                                }
                                catch { MBInformationManager.AddQuickInformation(new TextObject(narrative.Length > 120 ? narrative.Substring(0, 120) + "…" : narrative)); }
                            }));
                    }
                    catch
                    {
                        int killed = SacrificeForRite(SacrificePtsAshenSolstice);
                        CampaignMapEvents.ForceIronWinter();
                        _solsticeType = "winter";
                        _solsticeUntilDay = CurrentCampaignDay() + SolsticeBuffDays;
                        _lastSolsticeDay = CurrentCampaignDay();
                        _lastAltarUseDay = CurrentCampaignDay();
                        _altarUseCount++;
                        MBInformationManager.AddQuickInformation(new TextObject($"The Ashen Solstice — {killed} paid the cold. The north darkens."));
                    }
                }
            }
            catch { }
            finally { try { GameMenu.SwitchToMenu("altar_menu"); } catch { } }
        }

        // ── Rite: Carrion Gift ─────────────────────────────────────────────────
        private static void PerformCarrionGift()
        {
            try
            {
                float mult = AltarTraitMultiplier();

                if (mult > 0.01f)
                {
                    var candidates = Settlement.All
                        .Where(s => s.IsTown && s.MapFaction?.StringId != AshenKingdomId
                                 && s.Town?.GarrisonParty?.MemberRoster?.TotalManCount > 0)
                        .OrderByDescending(s => s.Town.GarrisonParty.MemberRoster.TotalManCount)
                        .Take(2).ToList();

                    if (candidates.Count == 0)
                    {
                        int killed = SacrificeForRite(SacrificePtsCarrionGift);
                        _lastCarrionDay = CurrentCampaignDay();
                        _lastAltarUseDay = CurrentCampaignDay();
                        _altarUseCount++;
                        string msg = "The plague leaves the altar and dissipates. No suitable garrison can be found.";
                        try { InformationManager.ShowInquiry(new InquiryData("Carrion Gift", msg, true, false, "Let it spread.", "", null, null)); }
                        catch { MBInformationManager.AddQuickInformation(new TextObject(msg)); }
                    }
                    else if (candidates.Count == 1)
                    {
                        // Only one candidate — apply directly
                        var target = candidates[0];
                        int killed = SacrificeForRite(SacrificePtsCarrionGift);
                        _lastCarrionDay = CurrentCampaignDay();
                        _lastAltarUseDay = CurrentCampaignDay();
                        _altarUseCount++;
                        ApplyCarrionGiftToTarget(target, mult, killed);
                    }
                    else
                    {
                        // Two candidates — let the player choose
                        var cityA = candidates[0];
                        var cityB = candidates[1];
                        string nameA = cityA.Name?.ToString() ?? "a distant city";
                        string nameB = cityB.Name?.ToString() ?? "another city";
                        int countA = cityA.Town?.GarrisonParty?.MemberRoster?.TotalManCount ?? 0;
                        int countB = cityB.Town?.GarrisonParty?.MemberRoster?.TotalManCount ?? 0;
                        string factionA = cityA.MapFaction?.Name?.ToString() ?? "unknown";
                        string factionB = cityB.MapFaction?.Name?.ToString() ?? "unknown";
                        float distA = 0f, distB = 0f;
                        try
                        {
                            float px = MobileParty.MainParty.GetPosition2D.x, py = MobileParty.MainParty.GetPosition2D.y;
                            float dxA = cityA.GetPosition2D.x - px, dyA = cityA.GetPosition2D.y - py;
                            float dxB = cityB.GetPosition2D.x - px, dyB = cityB.GetPosition2D.y - py;
                            distA = (float)Math.Sqrt(dxA * dxA + dyA * dyA);
                            distB = (float)Math.Sqrt(dxB * dxB + dyB * dyB);
                        }
                        catch { }

                        try
                        {
                            InformationManager.ShowInquiry(new InquiryData(
                                "Where Will the Plague Land?",
                                $"The grey sickness waits, patient and hungry. Two armies draw breath it could stop.\n\n" +
                                $"{nameA} — {countA} soldiers. {factionA}. {distA:F0} map units away.\n\n" +
                                $"{nameB} — {countB} soldiers. {factionB}. {distB:F0} map units away.",
                                true, true,
                                nameA, nameB,
                                () =>
                                {
                                    int killed = SacrificeForRite(SacrificePtsCarrionGift);
                                    _lastCarrionDay = CurrentCampaignDay();
                                    _lastAltarUseDay = CurrentCampaignDay();
                                    _altarUseCount++;
                                    ApplyCarrionGiftToTarget(cityA, mult, killed);
                                },
                                () =>
                                {
                                    int killed = SacrificeForRite(SacrificePtsCarrionGift);
                                    _lastCarrionDay = CurrentCampaignDay();
                                    _lastAltarUseDay = CurrentCampaignDay();
                                    _altarUseCount++;
                                    ApplyCarrionGiftToTarget(cityB, mult, killed);
                                }));
                        }
                        catch
                        {
                            // Fallback: apply to first candidate
                            int killed = SacrificeForRite(SacrificePtsCarrionGift);
                            _lastCarrionDay = CurrentCampaignDay();
                            _lastAltarUseDay = CurrentCampaignDay();
                            _altarUseCount++;
                            ApplyCarrionGiftToTarget(cityA, mult, killed);
                        }
                    }
                }
                else if (mult < -0.01f)
                {
                    int killed = SacrificeForRite(SacrificePtsCarrionGift);
                    _lastCarrionDay = CurrentCampaignDay();
                    _lastAltarUseDay = CurrentCampaignDay();
                    _altarUseCount++;
                    // Penalty: wound own garrison or party
                    int toWound = Math.Max(1, (int)(8 * Math.Abs(mult))), wounded = 0;
                    var roster  = MobileParty.MainParty?.MemberRoster;
                    if (roster != null)
                        foreach (var e in roster.GetTroopRoster().ToList())
                        {
                            if (e.Character.IsHero) continue;
                            int healthy = e.Number - e.WoundedNumber; int w = Math.Min(healthy, toWound - wounded);
                            if (w <= 0) continue;
                            try { roster.AddToCounts(e.Character, 0, false, w); wounded += w; } catch { }
                            if (wounded >= toWound) break;
                        }
                    string narrative = $"The grey plague reverses. It finds the warmest source available — your own men. {wounded} of your soldiers are now sick.";
                    try { InformationManager.ShowInquiry(new InquiryData("Carrion Gift", narrative, true, false, "Let it spread.", "", null, null)); }
                    catch { MBInformationManager.AddQuickInformation(new TextObject(narrative.Length > 80 ? narrative.Substring(0, 80) + "…" : narrative)); }
                }
                else
                {
                    int killed = SacrificeForRite(SacrificePtsCarrionGift);
                    _lastCarrionDay = CurrentCampaignDay();
                    _lastAltarUseDay = CurrentCampaignDay();
                    _altarUseCount++;
                    string narrative = "The plague leaves the altar and dissipates. The grey flame finds nothing in you worth channelling. The sacrifice was wasted.";
                    try { InformationManager.ShowInquiry(new InquiryData("Carrion Gift", narrative, true, false, "Let it spread.", "", null, null)); }
                    catch { MBInformationManager.AddQuickInformation(new TextObject(narrative)); }
                }
            }
            catch { }
            finally { try { GameMenu.SwitchToMenu("altar_menu"); } catch { } }
        }

        private static void ApplyCarrionGiftToTarget(Settlement target, float mult, int killed)
        {
            string targetName = target.Name?.ToString() ?? "a distant city";
            int totalWounded = 0;
            try
            {
                foreach (var e in target.Town.GarrisonParty.MemberRoster.GetTroopRoster().ToList())
                {
                    if (e.Character.IsHero) continue;
                    int healthy = e.Number - e.WoundedNumber; if (healthy <= 0) continue;
                    int toWound = Math.Max(1, (int)(healthy * (0.30f + (float)_rng.NextDouble() * 0.30f) * mult));
                    try { target.Town.GarrisonParty.MemberRoster.AddToCounts(e.Character, 0, false, toWound); totalWounded += toWound; } catch { }
                }
            }
            catch { }
            string narrative = totalWounded > 0
                ? $"The smoke travels to {targetName}. {totalWounded} soldier{(totalWounded != 1 ? "s are" : " is")} on their backs."
                : "The plague travels but finds no suitable garrison.";
            try { InformationManager.ShowInquiry(new InquiryData("Carrion Gift", narrative, true, false, "Let it spread.", "", null, null)); }
            catch { MBInformationManager.AddQuickInformation(new TextObject(narrative.Length > 80 ? narrative.Substring(0, 80) + "…" : narrative)); }
        }

        // ── Rite: Break Hearts and Wills ──────────────────────────────────────
        private static void PerformBreakWills()
        {
            try
            {
                float mult = AltarTraitMultiplier();

                if (mult > 0.01f)
                {
                    // Find top 2 enemy cities with highest current loyalty
                    var candidates = Settlement.All
                        .Where(s => s.IsTown && s.MapFaction?.StringId != AshenKingdomId && s.Town != null)
                        .OrderByDescending(s => s.Town.Loyalty)
                        .Take(2).ToList();

                    if (candidates.Count == 0)
                    {
                        int killed = SacrificeForRite(SacrificePtsBreakWills);
                        _lastBreakWillsDay = CurrentCampaignDay();
                        _lastAltarUseDay = CurrentCampaignDay();
                        _altarUseCount++;
                        string msg = "The despair travels but finds no suitable city.";
                        try { InformationManager.ShowInquiry(new InquiryData("Break Hearts and Wills", msg, true, false, "Let them despair.", "", null, null)); }
                        catch { MBInformationManager.AddQuickInformation(new TextObject(msg)); }
                    }
                    else if (candidates.Count == 1)
                    {
                        int killed = SacrificeForRite(SacrificePtsBreakWills);
                        _lastBreakWillsDay = CurrentCampaignDay();
                        _lastAltarUseDay = CurrentCampaignDay();
                        _altarUseCount++;
                        ApplyBreakWillsToTarget(candidates[0], mult, killed);
                    }
                    else
                    {
                        var cityA = candidates[0];
                        var cityB = candidates[1];
                        string nameA = cityA.Name?.ToString() ?? "a distant city";
                        string nameB = cityB.Name?.ToString() ?? "another city";
                        string factionA = cityA.MapFaction?.Name?.ToString() ?? "unknown";
                        string factionB = cityB.MapFaction?.Name?.ToString() ?? "unknown";
                        float loyaltyA = cityA.Town?.Loyalty ?? 0f;
                        float loyaltyB = cityB.Town?.Loyalty ?? 0f;
                        float secA = cityA.Town?.Security ?? 0f;
                        float secB = cityB.Town?.Security ?? 0f;

                        try
                        {
                            InformationManager.ShowInquiry(new InquiryData(
                                "Which City Will You Hollow Out?",
                                $"Despair is a seed. These cities have soil for it.\n\n" +
                                $"{nameA} — Loyalty: {(int)loyaltyA}. Security: {(int)secA}. {factionA}.\n\n" +
                                $"{nameB} — Loyalty: {(int)loyaltyB}. Security: {(int)secB}. {factionB}.",
                                true, true,
                                nameA, nameB,
                                () =>
                                {
                                    int killed = SacrificeForRite(SacrificePtsBreakWills);
                                    _lastBreakWillsDay = CurrentCampaignDay();
                                    _lastAltarUseDay = CurrentCampaignDay();
                                    _altarUseCount++;
                                    ApplyBreakWillsToTarget(cityA, mult, killed);
                                },
                                () =>
                                {
                                    int killed = SacrificeForRite(SacrificePtsBreakWills);
                                    _lastBreakWillsDay = CurrentCampaignDay();
                                    _lastAltarUseDay = CurrentCampaignDay();
                                    _altarUseCount++;
                                    ApplyBreakWillsToTarget(cityB, mult, killed);
                                }));
                        }
                        catch
                        {
                            int killed = SacrificeForRite(SacrificePtsBreakWills);
                            _lastBreakWillsDay = CurrentCampaignDay();
                            _lastAltarUseDay = CurrentCampaignDay();
                            _altarUseCount++;
                            ApplyBreakWillsToTarget(cityA, mult, killed);
                        }
                    }
                }
                else if (mult < -0.01f)
                {
                    int killed = SacrificeForRite(SacrificePtsBreakWills);
                    _lastBreakWillsDay = CurrentCampaignDay();
                    _lastAltarUseDay = CurrentCampaignDay();
                    _altarUseCount++;
                    // Penalty: drain loyalty from player's current settlement
                    var playerSettlement = Settlement.CurrentSettlement ?? Hero.MainHero?.CurrentSettlement;
                    string narrative;
                    if (playerSettlement?.Town != null)
                    {
                        float drain = (10f + (float)_rng.NextDouble() * 10f) * Math.Abs(mult);
                        try { playerSettlement.Town.Loyalty  = Math.Max(0f, playerSettlement.Town.Loyalty  - drain); } catch { }
                        try { playerSettlement.Town.Security = Math.Max(0f, playerSettlement.Town.Security - drain); } catch { }
                        narrative = $"The grey flame turns your warmth against itself. The despair settles here, in {playerSettlement.Name}. Loyalty and security drop by {(int)drain}.";
                    }
                    else
                        narrative = "The grey flame refuses you. The despair finds nowhere to land.";
                    try { InformationManager.ShowInquiry(new InquiryData("Break Hearts and Wills", narrative, true, false, "Let them despair.", "", null, null)); }
                    catch { MBInformationManager.AddQuickInformation(new TextObject(narrative.Length > 80 ? narrative.Substring(0, 80) + "…" : narrative)); }
                }
                else
                {
                    int killed = SacrificeForRite(SacrificePtsBreakWills);
                    _lastBreakWillsDay = CurrentCampaignDay();
                    _lastAltarUseDay = CurrentCampaignDay();
                    _altarUseCount++;
                    string narrative = "The despair leaves the altar and dissipates. You are not cold enough to direct it. The sacrifice achieved nothing.";
                    try { InformationManager.ShowInquiry(new InquiryData("Break Hearts and Wills", narrative, true, false, "Let them despair.", "", null, null)); }
                    catch { MBInformationManager.AddQuickInformation(new TextObject(narrative)); }
                }
            }
            catch { }
            finally { try { GameMenu.SwitchToMenu("altar_menu"); } catch { } }
        }

        private static void ApplyBreakWillsToTarget(Settlement target, float mult, int killed)
        {
            string targetName = target.Name?.ToString() ?? "a distant city";
            float loyaltyDrain = (15f + (float)_rng.NextDouble() * 10f) * mult;
            float secDrain     = (15f + (float)_rng.NextDouble() * 10f) * mult;
            try { target.Town.Loyalty  = Math.Max(0f, target.Town.Loyalty  - loyaltyDrain); } catch { }
            try { target.Town.Security = Math.Max(0f, target.Town.Security - secDrain);     } catch { }
            string narrative = loyaltyDrain > 0f
                ? $"In {targetName}, the guards are harder to rouse. The merchants close early. Nobody can say why. Loyalty −{(int)loyaltyDrain}. Security −{(int)secDrain}."
                : "The despair travels but finds no suitable city.";

            // Cascade: if loyalty drops below 25, notify of deserters
            if (target.Town.Loyalty < 25f)
            {
                try
                {
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"In {targetName}, soldiers have deserted their posts. A band of them wanders the roads now."));
                }
                catch { }
            }

            try { InformationManager.ShowInquiry(new InquiryData("Break Hearts and Wills", narrative, true, false, "Let them despair.", "", null, null)); }
            catch { MBInformationManager.AddQuickInformation(new TextObject(narrative.Length > 80 ? narrative.Substring(0, 80) + "…" : narrative)); }
        }

        // ── Rite: Rite of Cold Fire ────────────────────────────────────────────
        private static void PerformColdFire()
        {
            try
            {
                float mult   = AltarTraitMultiplier();
                int   killed = SacrificeForRite(SacrificePtsColdFire);
                string narrative;

                if (mult > 0.01f)
                {
                    float px = 0f, py = 0f;
                    try { px = MobileParty.MainParty.GetPosition2D.x; py = MobileParty.MainParty.GetPosition2D.y; } catch { }
                    const float rangeSquared = 150f * 150f;
                    var target = MobileParty.All
                        .Where(p =>
                        {
                            if (!p.IsActive || p.IsMainParty) return false;
                            if (p.MapFaction?.StringId == AshenKingdomId) return false;
                            if (p.LeaderHero == null) return false;
                            float dx = p.GetPosition2D.x - px, dy = p.GetPosition2D.y - py;
                            return dx * dx + dy * dy < rangeSquared;
                        })
                        .OrderBy(p => { float dx = p.GetPosition2D.x - px, dy = p.GetPosition2D.y - py; return dx * dx + dy * dy; })
                        .FirstOrDefault();

                    if (target != null)
                    {
                        string targetDesc = target.Name?.ToString() ?? "an enemy party";
                        int toWound = (int)((8 + _rng.Next(8)) * mult);
                        int wounded = 0;
                        foreach (var e in target.MemberRoster.GetTroopRoster().ToList())
                        {
                            if (e.Character.IsHero) continue;
                            int healthy = e.Number - e.WoundedNumber;
                            int w = Math.Min(healthy, toWound - wounded);
                            if (w <= 0) continue;
                            try { target.MemberRoster.AddToCounts(e.Character, 0, false, w); wounded += w; } catch { }
                            if (wounded >= toWound) break;
                        }
                        try { target.RecentEventsMorale -= 30f * mult; } catch { }

                        // Apply freeze effect
                        _frozenPartyId  = target.StringId ?? "";
                        _frozenUntilDay = CurrentCampaignDay() + ColdFreezeEffectDays;

                        narrative = $"Out in the dark, {targetDesc}'s column has halted. {wounded} soldier{(wounded != 1 ? "s are" : " is")} on one knee. The cold has introduced itself.\n\n" +
                            $"The cold settles into their joints. They will not march again for {ColdFreezeEffectDays} days — something in the air around them will make every step cost twice what it should.";
                    }
                    else
                        narrative = "The cold fire rushes out, hungry, and finds nothing close enough to settle on. It retreats.";

                    _lastColdFireDay = CurrentCampaignDay();
                    _lastAltarUseDay = CurrentCampaignDay();
                    _altarUseCount++;
                }
                else if (mult < -0.01f)
                {
                    // Penalty: wound own party troops
                    int toWound = (int)((5 + _rng.Next(5)) * Math.Abs(mult));
                    int wounded = 0;
                    foreach (var e in MobileParty.MainParty.MemberRoster.GetTroopRoster().ToList())
                    {
                        if (e.Character.IsHero) continue;
                        int healthy = e.Number - e.WoundedNumber;
                        int w = Math.Min(healthy, toWound - wounded);
                        if (w <= 0) continue;
                        try { MobileParty.MainParty.MemberRoster.AddToCounts(e.Character, 0, false, w); wounded += w; } catch { }
                        if (wounded >= toWound) break;
                    }
                    narrative = wounded > 0
                        ? $"The cold fire has no target it can reach — no cruelty to amplify. It turns inward instead. {wounded} of your own soldiers collapse, shivering."
                        : "The cold fire turns on you and finds no purchase. It simply fades. The altar does not forgive generosity.";
                    _lastColdFireDay = CurrentCampaignDay();
                    _lastAltarUseDay = CurrentCampaignDay();
                    _altarUseCount++;
                }
                else
                {
                    narrative = "The cold fire reaches out and finds the world indifferent. You lack the coldness to direct it. The sacrifice was wasted.";
                    _lastColdFireDay = CurrentCampaignDay();
                    _lastAltarUseDay = CurrentCampaignDay();
                    _altarUseCount++;
                }

                try { InformationManager.ShowInquiry(new InquiryData("Rite of Cold Fire", narrative, true, false, "Cold enough.", "", null, null)); }
                catch { MBInformationManager.AddQuickInformation(new TextObject(narrative.Length > 80 ? narrative.Substring(0, 80) + "…" : narrative)); }
            }
            catch { }
            finally { try { GameMenu.SwitchToMenu("altar_menu"); } catch { } }
        }

        // ── Rite: Rite of Subjugation ─────────────────────────────────────────
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

        private static void PerformSubjugate()
        {
            try
            {
                var prison = MobileParty.MainParty?.PrisonRoster;
                if (prison == null) { try { GameMenu.SwitchToMenu("altar_menu"); } catch { } return; }

                var prisoners = prison.GetTroopRoster()
                    .Where(e => !e.Character.IsHero && e.Number > 0)
                    .OrderBy(e => e.Character.Tier)
                    .ThenBy(e => e.Character.StringId)
                    .ToList();

                if (prisoners.Count == 0 || prisoners.Sum(e => e.Number) < 2)
                {
                    MBInformationManager.AddQuickInformation(new TextObject(
                        "Rite of Subjugation — not enough prisoners. The altar waits."));
                    try { GameMenu.SwitchToMenu("altar_menu"); } catch { }
                    return;
                }

                // Check if there are prisoners of different tiers
                int minTier = prisoners.Min(e => e.Character.Tier);
                int maxTier = prisoners.Max(e => e.Character.Tier);

                if (minTier != maxTier)
                {
                    // Different tiers available — offer a choice
                    var lowestEntry  = prisoners.First();
                    var highestEntry = prisoners.Last();
                    string lowestName  = lowestEntry.Character.Name?.ToString() ?? "a lowly prisoner";
                    string highestName = highestEntry.Character.Name?.ToString() ?? "a high-born prisoner";

                    try
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            "Choose the Offering",
                            $"The altar does not care which one kneels. But the others will remember what they see.\n\n" +
                            $"The lowest among them is {lowestName} — cheap to spend, but the survivors will follow because they have no other option.\n\n" +
                            $"The highest is {highestName} — a greater offering. The survivors will understand, in their bones, what it means to refuse you.",
                            true, true,
                            $"The lowest (cheap, converts at baseline)",
                            $"The highest (costly, converts with +20 morale)",
                            () =>
                            {
                                // Sacrifice lowest-tier prisoner
                                try { prison.AddToCounts(lowestEntry.Character, -1); } catch { }
                                string sacrificeName = lowestName;
                                var remaining = prison.GetTroopRoster()
                                    .Where(e => !e.Character.IsHero && e.Number > 0).ToList();
                                var roster = MobileParty.MainParty.MemberRoster;
                                int converted = 0;
                                foreach (var e in remaining)
                                {
                                    try { int n = e.Number; prison.AddToCounts(e.Character, -n); roster.AddToCounts(e.Character, n); converted += n; } catch { }
                                }
                                try { MobileParty.MainParty.RecentEventsMorale -= 10f; } catch { }
                                _lastSubjugateDay = CurrentCampaignDay();
                                _lastAltarUseDay = CurrentCampaignDay();
                                _altarUseCount++;
                                string narrative = converted > 0
                                    ? $"The altar takes {sacrificeName}. Not cruelly — coldly. The way fire takes wood: completely, without apology. " +
                                      "The other prisoners watch from the dark. No one tells them what it means. " +
                                      "They understand anyway. By the time the smoke settles, " +
                                      $"{converted} of them have risen and crossed to your side of the room. " +
                                      "They will not speak of it. Neither will you. The fire is satisfied."
                                    : $"The sacrifice is made. The fire takes {sacrificeName}. " +
                                      "The others were already gone, or too few to matter. " +
                                      "The altar does not negotiate.";
                                try { InformationManager.ShowInquiry(new InquiryData("Rite of Subjugation", narrative, true, false, converted > 0 ? "They serve now." : "The price is paid.", "", null, null)); }
                                catch { MBInformationManager.AddQuickInformation(new TextObject(converted > 0 ? $"Rite of Subjugation — {sacrificeName} consumed. {converted} prisoner{(converted != 1 ? "s" : "")} join your ranks." : $"Rite of Subjugation — {sacrificeName} sacrificed.")); }
                            },
                            () =>
                            {
                                // Sacrifice highest-tier prisoner
                                try { prison.AddToCounts(highestEntry.Character, -1); } catch { }
                                string sacrificeName = highestName;
                                var remaining = prison.GetTroopRoster()
                                    .Where(e => !e.Character.IsHero && e.Number > 0).ToList();
                                var roster = MobileParty.MainParty.MemberRoster;
                                int converted = 0;
                                foreach (var e in remaining)
                                {
                                    try { int n = e.Number; prison.AddToCounts(e.Character, -n); roster.AddToCounts(e.Character, n); converted += n; } catch { }
                                }
                                try { MobileParty.MainParty.RecentEventsMorale -= 10f; } catch { }
                                try { MobileParty.MainParty.RecentEventsMorale += 20f; } catch { } // bonus morale for high-tier sacrifice
                                _lastSubjugateDay = CurrentCampaignDay();
                                _lastAltarUseDay = CurrentCampaignDay();
                                _altarUseCount++;
                                string narrative = converted > 0
                                    ? $"The altar takes {sacrificeName}. The smoke that rises is darker, heavier with what was worth something. " +
                                      "The other prisoners did not need to be told. They crossed over before the priest finished speaking. " +
                                      $"{converted} of them now serve you — and they march with something in their chests that was not there before."
                                    : $"The sacrifice is made. The fire takes {sacrificeName}. High-born, and now ash. " +
                                      "The altar does not mourn what it consumes.";
                                try { InformationManager.ShowInquiry(new InquiryData("Rite of Subjugation", narrative, true, false, converted > 0 ? "They serve now." : "The price is paid.", "", null, null)); }
                                catch { MBInformationManager.AddQuickInformation(new TextObject(converted > 0 ? $"Rite of Subjugation — {sacrificeName} consumed. {converted} prisoner{(converted != 1 ? "s" : "")} join your ranks with +20 morale." : $"Rite of Subjugation — {sacrificeName} sacrificed.")); }
                            }));
                    }
                    catch
                    {
                        // Fallback: standard lowest-tier behavior
                        PerformSubjugateStandard(prison, prisoners);
                    }
                }
                else
                {
                    // All same tier — no choice needed, proceed with current behavior
                    PerformSubjugateStandard(prison, prisoners);
                }
            }
            catch { }
            finally { try { GameMenu.SwitchToMenu("altar_menu"); } catch { } }
        }

        private static void PerformSubjugateStandard(TaleWorlds.CampaignSystem.Roster.TroopRoster prison, System.Collections.Generic.List<TaleWorlds.CampaignSystem.Roster.TroopRosterElement> prisoners)
        {
            // Sacrifice the single lowest-tier prisoner
            var sacrifice = prisoners[0];
            try { prison.AddToCounts(sacrifice.Character, -1); } catch { }
            string sacrificeName = sacrifice.Character.Name?.ToString() ?? "a prisoner";

            // Convert all remaining non-hero prisoners to troops
            var remaining = prison.GetTroopRoster()
                .Where(e => !e.Character.IsHero && e.Number > 0).ToList();
            var roster = MobileParty.MainParty.MemberRoster;
            int converted = 0;
            foreach (var e in remaining)
            {
                try
                {
                    int n = e.Number;
                    prison.AddToCounts(e.Character, -n);
                    roster.AddToCounts(e.Character, n);
                    converted += n;
                }
                catch { }
            }

            try { MobileParty.MainParty.RecentEventsMorale -= 10f; } catch { }
            _lastSubjugateDay = CurrentCampaignDay();
            _lastAltarUseDay = CurrentCampaignDay();
            _altarUseCount++;

            string narrative = converted > 0
                ? $"The altar takes {sacrificeName}. Not cruelly — coldly. The way fire takes wood: completely, without apology. " +
                  "The other prisoners watch from the dark. No one tells them what it means. " +
                  "They understand anyway. By the time the smoke settles, " +
                  $"{converted} of them have risen and crossed to your side of the room. " +
                  "They will not speak of it. Neither will you. The fire is satisfied."
                : $"The sacrifice is made. The fire takes {sacrificeName}. " +
                  "The others were already gone, or too few to matter. " +
                  "The altar does not negotiate.";

            try
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Rite of Subjugation", narrative, true, false,
                    converted > 0 ? "They serve now." : "The price is paid.", "", null, null));
            }
            catch
            {
                MBInformationManager.AddQuickInformation(new TextObject(
                    converted > 0
                        ? $"Rite of Subjugation — {sacrificeName} consumed. {converted} prisoner{(converted != 1 ? "s" : "")} join your ranks."
                        : $"Rite of Subjugation — {sacrificeName} sacrificed. No others remained."));
            }
        }

        // ── NPC daily tick ─────────────────────────────────────────────────────
        private static void OnDailyTick()
        {
            int today = CurrentCampaignDay();

            // Solstice caster benefits while active
            if (_solsticeUntilDay >= today && MobileParty.MainParty != null)
            {
                if (_solsticeType == "winter")
                {
                    // Iron Winter benefit: add 2 food per day (cold doesn't bite you)
                    try { MobileParty.MainParty.Food += 2f; } catch { }
                }
                else if (_solsticeType == "sun")
                {
                    // Scorching Sun benefit: 2 food + slight morale lift (heat energises the chosen)
                    try { MobileParty.MainParty.Food += 2f; } catch { }
                    try { MobileParty.MainParty.RecentEventsMorale += 0.5f; } catch { }
                }
            }
            else if (_solsticeUntilDay >= 0 && today > _solsticeUntilDay)
            {
                _solsticeType    = "";
                _solsticeUntilDay = -1;
            }

            // Cold Fire freeze: re-apply effects each day while frozen
            if (!string.IsNullOrEmpty(_frozenPartyId) && _frozenUntilDay >= today)
            {
                var frozen = MobileParty.All.FirstOrDefault(p => p.StringId == _frozenPartyId && p.IsActive);
                if (frozen != null)
                {
                    try { frozen.RecentEventsMorale -= 20f; } catch { }
                    // Wound 2-3 more troops per day
                    int toWound = 2 + _rng.Next(2), w = 0;
                    foreach (var e in frozen.MemberRoster.GetTroopRoster().ToList())
                    {
                        if (e.Character.IsHero) continue;
                        int healthy = e.Number - e.WoundedNumber;
                        int n = Math.Min(healthy, toWound - w);
                        if (n <= 0) continue;
                        try { frozen.MemberRoster.AddToCounts(e.Character, 0, false, n); w += n; } catch { }
                        if (w >= toWound) break;
                    }
                }
            }
            else if (today > _frozenUntilDay && !string.IsNullOrEmpty(_frozenPartyId))
            {
                _frozenPartyId  = "";
                _frozenUntilDay = -1;
            }

            // Trait drift: every TraitDriftThreshold altar uses, nudge traits down
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
                        if (mercy >= honor && mercy >= gen && mercy > -2)
                            h.SetTraitLevel(DefaultTraits.Mercy, mercy - 1);
                        else if (honor >= mercy && honor >= gen && honor > -2)
                            h.SetTraitLevel(DefaultTraits.Honor, honor - 1);
                        else if (gen > -2)
                            h.SetTraitLevel(DefaultTraits.Generosity, gen - 1);
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
                             && h != Hero.MainHero
                             && h.CurrentSettlement != null
                             && HasAshenAltar(h.CurrentSettlement)
                             && NpcCanUseAltar(h))
                    .OrderBy(_ => _rng.Next())
                    .Take(6))
                {
                    if (_rng.NextDouble() > 0.005) continue;   // 0.5 % per qualifying lord per day

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
                            if (NpcCarrionGiftGarrison(hero, out string plagueTarget))
                                InformationManager.DisplayMessage(new InformationMessage(
                                    $"{hero.Name} — carrion rite at the altar in {city}. A grey sickness reached {plagueTarget}.",
                                    new Color(0.38f, 0.50f, 0.75f)));
                            break;
                        case 4:
                            if (NpcBreakWillsCity(hero, out string despairTarget))
                                InformationManager.DisplayMessage(new InformationMessage(
                                    $"{hero.Name} — despair rite at the altar in {city}. {despairTarget} grows restless and fearful.",
                                    new Color(0.38f, 0.50f, 0.75f)));
                            break;
                    }
                }
            }
            catch { }
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

        // Wounds 10–20% of a random non-Ashen garrison. Returns true and sets targetName if successful.
        private static bool NpcCarrionGiftGarrison(Hero caster, out string targetName)
        {
            targetName = "";
            try
            {
                var candidates = Settlement.All
                    .Where(s => s.IsTown && s.MapFaction?.StringId != AshenKingdomId
                             && s.Town?.GarrisonParty?.MemberRoster?.TotalManCount > 0)
                    .ToList();
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

        // Drains 5–10 loyalty and security from a random non-Ashen city. Returns true and sets targetName if successful.
        private static bool NpcBreakWillsCity(Hero caster, out string targetName)
        {
            targetName = "";
            try
            {
                var candidates = Settlement.All
                    .Where(s => s.IsTown && s.MapFaction?.StringId != AshenKingdomId && s.Town != null)
                    .ToList();
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
                .Where(p =>
                {
                    if (!p.IsActive || p.MapFaction?.StringId == AshenKingdomId) return false;
                    float dx = p.GetPosition2D.x - sx, dy = p.GetPosition2D.y - sy;
                    return dx * dx + dy * dy < rangeSquared;
                })
                .OrderBy(p =>
                {
                    float dx = p.GetPosition2D.x - sx, dy = p.GetPosition2D.y - sy;
                    return dx * dx + dy * dy;
                })
                .FirstOrDefault();

            if (target == null) return;
            int toWound = 3 + _rng.Next(5);   // 3–7
            int wounded = 0;

            foreach (var e in target.MemberRoster.GetTroopRoster().ToList())
            {
                if (e.Character.IsHero) continue;
                int healthy = e.Number - e.WoundedNumber;
                int w = Math.Min(healthy, toWound - wounded);
                if (w <= 0) continue;
                try { target.MemberRoster.AddToCounts(e.Character, 0, false, w); wounded += w; } catch { }
                if (wounded >= toWound) break;
            }
            try { target.RecentEventsMorale -= 15f; } catch { }
        }
    }
}
