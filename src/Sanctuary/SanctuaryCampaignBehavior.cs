// =============================================================================
// ASH AND EMBER — Sanctuary/SanctuaryCampaignBehavior.cs
//
// Adds a "Visit the Sanctuary" option to the campaign map town menu in:
//   • Every Temple-owned town.
//   • Four random Empire towns chosen at new-game-start and saved.
//
// Access requires the player to have Honor ≥ 1 AND Mercy ≥ 1.
// Temple members receive a 40% discount on all rites.
//
// Services:
//   Prayer of Strength      — morale boost (gold only).
//   Protective Rites        — blocks Ashen world events for N days (gold + aging).
//   Turn the Ashen          — wounds nearby Ashen parties (gold + aging).
//   Prayer of Healing       — heals wounded troops, or activates Steady the Line (gold + aging).
//   Prayer for a Blessing   — shed a year OR receive a trait mark (flat gold cost).
//
// NPC effects (daily tick):
//   • Honourable + Merciful lords currently in a sanctuary city: 0.3% chance/day
//     to receive healing or a morale blessing — shows a "miracle" notification.
//   • Temple faction lords: 3% chance/day to partially heal, 2% to Turn nearby Ashen.
//
// Also registers the "Arrange covert business" scheme option in the town menu.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AshAndEmber
{
    public class SanctuaryCampaignBehavior : CampaignBehaviorBase
    {
        // ── Tuning ─────────────────────────────────────────────────────────────
        private const int   BaseCostPrayer       =   500;
        private const int   BaseCostProtective   =  1000;
        private const int   BaseAgingProtective  =    30;  // days older
        private const int   BaseCostTurnAshen    =  1500;
        private const int   BaseAgingTurnAshen   =    45;
        private const int   BaseCostHealing      =   800;
        private const int   BaseAgingHealing     =    20;
        private const int   BlessingFlatCost     = 50000;  // flat gold cost for Prayer for a Blessing
        private const int   BlessingRejuvDays    =   365;  // 1 year
        private const int   BlessingMinAge       =    20;
        private const float TempleDiscount       =  0.60f; // Temple members pay 60% (40% off)
        private const int   ProtectiveDays       =    14;
        private const int   MoralePrayerBoost    =    40;
        private const int   PermanentSanctuaryCount = 4;

        private const int   PrayerCooldownBase    =    3;   // days between Prayer of Strength uses
        private const int   ProtectiveCooldownBase=    7;
        private const int   TurnAshenCooldownBase =    5;
        private const int   HealingCooldownBase   =    5;
        private const int   BlessingCooldownBase  =   30;
        private const int   BlessedDays           =    3;   // days blessed status lasts
        private const int   SteadyLineDays        =    5;   // days Steady the Line lasts
        private const int   TraitBoostDays        =   60;   // days temp trait boost lasts
        private const int   TraitDriftThreshold   =   10;   // sanctuary uses before trait nudge
        private const int   CrossInterferenceDays =   30;   // days altar use debuffs sanctuary mult

        private const string TempleKingdomId = "the_temple";
        private const string AshenKingdomId  = "ashen_kingdom";

        // ── Permanent (non-Temple) sanctuary settlement IDs ───────────────────
        private static readonly List<string> _permanentSanctuaryIds = new List<string>();

        // Cross-system state (read by AshenAltarsCampaignBehavior)
        internal static int _lastSanctuaryUseDay = -999;
        private static int  _sanctuaryUseCount   = 0;

        // Blessed status (Prayer of Strength bonus healing)
        private static int  _blessedUntilDay      = -1;

        // Steady the Line buff (Prayer of Healing alt option)
        private static int  _steadyLineUntilDay   = -1;

        // Temporary trait boost (Prayer for a Blessing alt option)
        private static int  _traitBoostUntilDay   = -1;
        private static float _traitBoostAmount    = 0f;   // added to raw mult while active

        // Per-rite cooldown tracking (last day each rite was used)
        private static int  _lastPrayerDay        = -999;
        private static int  _lastProtectiveDay    = -999;
        private static int  _lastTurnAshenDay     = -999;
        private static int  _lastHealingDay       = -999;
        private static int  _lastBlessingDay      = -999;

        private static readonly Random _rng = new Random();

        // ── CampaignBehaviorBase ───────────────────────────────────────────────
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore store)
        {
            try
            {
                var ids = _permanentSanctuaryIds.ToList();
                store.SyncData("SANCT_PermanentIds", ref ids);
                if (ids != null)
                {
                    _permanentSanctuaryIds.Clear();
                    foreach (var id in ids) _permanentSanctuaryIds.Add(id);
                }
            }
            catch { }

            try { store.SyncData("SANCT_LastUseDay", ref _lastSanctuaryUseDay); } catch { }
            try { store.SyncData("SANCT_UseCount", ref _sanctuaryUseCount); } catch { }
            try { store.SyncData("SANCT_BlessedUntilDay", ref _blessedUntilDay); } catch { }
            try { store.SyncData("SANCT_SteadyLineUntilDay", ref _steadyLineUntilDay); } catch { }
            try { store.SyncData("SANCT_TraitBoostUntilDay", ref _traitBoostUntilDay); } catch { }
            try { store.SyncData("SANCT_TraitBoostAmount", ref _traitBoostAmount); } catch { }
            try { store.SyncData("SANCT_LastPrayerDay", ref _lastPrayerDay); } catch { }
            try { store.SyncData("SANCT_LastProtectiveDay", ref _lastProtectiveDay); } catch { }
            try { store.SyncData("SANCT_LastTurnAshenDay", ref _lastTurnAshenDay); } catch { }
            try { store.SyncData("SANCT_LastHealingDay", ref _lastHealingDay); } catch { }
            try { store.SyncData("SANCT_LastBlessingDay", ref _lastBlessingDay); } catch { }
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            EnsurePermanentSanctuaries();
            RegisterSanctuaryMenus(starter);
        }

        // ── Permanent sanctuary selection ─────────────────────────────────────
        private static void EnsurePermanentSanctuaries()
        {
            if (_permanentSanctuaryIds.Count >= PermanentSanctuaryCount) return;
            try
            {
                var empireIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { "empire_w", "empire", "empire_s", "empire_n" };
                var picks = Settlement.All
                    .Where(s => s.IsTown
                             && s.OwnerClan?.Kingdom != null
                             && empireIds.Contains(s.OwnerClan.Kingdom.StringId))
                    .OrderBy(_ => _rng.Next())
                    .Take(PermanentSanctuaryCount)
                    .ToList();
                _permanentSanctuaryIds.Clear();
                foreach (var s in picks) _permanentSanctuaryIds.Add(s.StringId);

                // Tell the player which Empire towns host sanctuaries this playthrough.
                if (picks.Count > 0)
                {
                    var nameList = picks.Select(s => s.Name.ToString()).ToList();
                    string names = nameList.Count == 1
                        ? nameList[0]
                        : string.Join(", ", nameList.Take(nameList.Count - 1)) + ", and " + nameList.Last();
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"Sanctuaries of the Flame have been established in {names}. " +
                        $"Honourable and Merciful travellers may seek their services there."));
                }
            }
            catch { }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        // Called by EC_LocalPriest settlement event when a player funds a sanctuary.
        internal static bool AddPermanentSanctuary(string settlementStringId)
        {
            if (string.IsNullOrEmpty(settlementStringId)) return false;
            if (_permanentSanctuaryIds.Contains(settlementStringId)) return false;
            _permanentSanctuaryIds.Add(settlementStringId);
            return true;
        }

        internal static bool HasSanctuary(Settlement s)
        {
            if (s == null || !s.IsTown) return false;
            if (s.OwnerClan?.Kingdom?.StringId == TempleKingdomId) return true;
            return _permanentSanctuaryIds.Contains(s.StringId);
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

        // Returns -1.0 to +1.0. Positive = sanctuary helps fully; 0 = no effect; negative = penalty.
        // Based on Mercy + Honor + Generosity, each clamped to [-2, 2], sum / 6.
        private static float SanctuaryTraitMultiplier()
        {
            var h = Hero.MainHero;
            if (h == null) return 0f;
            try
            {
                int mercy = h.GetTraitLevel(DefaultTraits.Mercy);
                int honor = h.GetTraitLevel(DefaultTraits.Honor);
                int gen   = h.GetTraitLevel(DefaultTraits.Generosity);
                float raw = (mercy + honor + gen) / 6f;

                // Cross-system: recent altar use taints the blessing
                int altarDay = AshenAltarsCampaignBehavior._lastAltarUseDay;
                if (CurrentCampaignDay() - altarDay < CrossInterferenceDays)
                    raw *= 0.5f;

                // Active trait boost (from Prayer for a Blessing alt option)
                raw += _traitBoostAmount;

                return Math.Max(-1f, Math.Min(1f, raw));
            }
            catch { return 0f; }
        }

        private static string SanctuaryTraitNote(float mult)
        {
            if (mult >= 0.8f)  return "  [Flame knows you — full blessing]";
            if (mult >= 0.4f)  return "  [Partial blessing]";
            if (mult >= 0.01f) return "  [Faint blessing — cold soul]";
            if (mult >= -0.01f)return "  [No benefit — the flame does not know you]";
            if (mult >= -0.5f) return "  [PENALTY — the flame recoils from your darkness]";
            return "  [HEAVY PENALTY — the flame burns against you]";
        }

        private static bool NpcCanUseSanctuary(Hero h)
        {
            try
            {
                return h.GetTraitLevel(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultTraits.Honor) >= 1
                    && h.GetTraitLevel(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultTraits.Mercy)  >= 1;
            }
            catch { return false; }
        }

        private static bool IsTempleMember()
            => Hero.MainHero?.Clan?.Kingdom?.StringId == TempleKingdomId;

        private static int GoldCost(int base_)
        {
            float cost = IsTempleMember() ? base_ * TempleDiscount : base_;
            try
            {
                int gen = Hero.MainHero?.GetTraitLevel(DefaultTraits.Generosity) ?? 0;
                // gen=2: -25%, gen=1: -12.5%, gen=-1: +12.5%, gen=-2: +25%
                cost *= 1f - gen * 0.125f;
            }
            catch { }
            return Math.Max(1, (int)cost);
        }

        private static int AgingCost(int baseDays)
            => IsTempleMember() ? (int)(baseDays * TempleDiscount) : baseDays;

        private static int BlessingCost()
        {
            int raw = BlessingFlatCost;
            return IsTempleMember() ? (int)(raw * TempleDiscount) : raw;
        }

        private static void AgeHero(Hero h, int days)
        {
            if (h == null || days <= 0) return;
            try { AgingSystem.AgeHero(h, days); } catch { }
        }

        // ── Livestock payment ─────────────────────────────────────────────────
        // Sanctuary rites accept livestock in lieu of gold at generous rates —
        // the flame prefers living offerings over coin. Each animal covers more
        // rite cost than its market value, making livestock the cheaper option.
        private const int SanctuaryCowGoldValue   = 150;   // 1 cow  ≡ 150 g toward rite cost
        private const int SanctuarySheepGoldValue =  40;   // 1 sheep ≡  40 g toward rite cost

        private static int ComputeLivestockGold()
        {
            try
            {
                var roster = MobileParty.MainParty?.ItemRoster;
                if (roster == null) return 0;
                var cow   = MBObjectManager.Instance.GetObject<ItemObject>("cow");
                var sheep = MBObjectManager.Instance.GetObject<ItemObject>("sheep");
                int cows   = cow   != null ? roster.GetItemNumber(cow)   : 0;
                int sheeps = sheep != null ? roster.GetItemNumber(sheep) : 0;
                return cows * SanctuaryCowGoldValue + sheeps * SanctuarySheepGoldValue;
            }
            catch { return 0; }
        }

        private static bool CanAffordSanctuary(int goldCost)
        {
            if (Hero.MainHero?.Gold >= goldCost) return true;
            return ComputeLivestockGold() >= goldCost;
        }

        private static bool PayWithSanctuaryLivestock(int goldCost)
        {
            try
            {
                var roster = MobileParty.MainParty?.ItemRoster;
                if (roster == null) return false;
                var cowObj   = MBObjectManager.Instance.GetObject<ItemObject>("cow");
                var sheepObj = MBObjectManager.Instance.GetObject<ItemObject>("sheep");
                int cows   = cowObj   != null ? roster.GetItemNumber(cowObj)   : 0;
                int sheeps = sheepObj != null ? roster.GetItemNumber(sheepObj) : 0;

                int remaining = goldCost;
                int cowsUsed  = 0, sheepsUsed = 0;
                while (remaining > 0 && cowsUsed  < cows)   { remaining -= SanctuaryCowGoldValue;   cowsUsed++; }
                while (remaining > 0 && sheepsUsed < sheeps) { remaining -= SanctuarySheepGoldValue; sheepsUsed++; }
                if (remaining > 0) return false;

                if (cowObj   != null && cowsUsed   > 0) roster.AddToCounts(cowObj,   -cowsUsed);
                if (sheepObj != null && sheepsUsed > 0) roster.AddToCounts(sheepObj, -sheepsUsed);
                return true;
            }
            catch { return false; }
        }

        // Resolves the gold/livestock payment choice, then calls onPaid.
        // If both options are available the player is shown an inquiry to choose.
        // Aging is NOT applied here — include it inside onPaid.
        private static void ResolveSanctuaryPayment(int cost, Action onPaid)
        {
            bool canGold = Hero.MainHero?.Gold >= cost;
            bool canLstk = ComputeLivestockGold() >= cost;

            if (canGold && canLstk)
            {
                try
                {
                    int cowsNeeded  = (cost + SanctuaryCowGoldValue  - 1) / SanctuaryCowGoldValue;
                    int sheepNeeded = (cost + SanctuarySheepGoldValue - 1) / SanctuarySheepGoldValue;
                    InformationManager.ShowInquiry(new InquiryData(
                        "Make Your Offering",
                        $"The sanctuary accepts what you bring to it. " +
                        $"Gold ({cost}g) will suffice. Or bring livestock — the flame values living offerings " +
                        $"and will accept fewer than their worth in coin " +
                        $"(~{cowsNeeded} cow{(cowsNeeded != 1 ? "s" : "")} or ~{sheepNeeded} sheep, cheaper than gold).",
                        true, true,
                        $"Gold ({cost}g)",
                        $"Livestock (~{cowsNeeded} cows or ~{sheepNeeded} sheep — cheaper)",
                        () => { if (Hero.MainHero?.Gold >= cost) { Hero.MainHero.Gold -= cost; onPaid(); } },
                        () => { if (PayWithSanctuaryLivestock(cost)) onPaid(); }
                    ));
                }
                catch
                {
                    if (Hero.MainHero?.Gold >= cost) { Hero.MainHero.Gold -= cost; onPaid(); }
                }
            }
            else if (canGold) { Hero.MainHero.Gold -= cost; onPaid(); }
            else if (canLstk) { if (PayWithSanctuaryLivestock(cost)) onPaid(); }
        }

        // Returns " or livestock" note for menu display when livestock is sufficient.
        private static string LivestockNote(int cost)
        {
            int lstk = ComputeLivestockGold();
            if (lstk < cost) return "";
            int cows  = (cost + SanctuaryCowGoldValue  - 1) / SanctuaryCowGoldValue;
            int sheep = (cost + SanctuarySheepGoldValue - 1) / SanctuarySheepGoldValue;
            return $" or ~{cows} cows/~{sheep} sheep";
        }

        // ── Menu registration ──────────────────────────────────────────────────
        private static void RegisterSanctuaryMenus(CampaignGameStarter starter)
        {
            // ── Entry point in the main town menu ──────────────────────────
            try
            {
                starter.AddGameMenuOption(
                    "town", "sanctuary_enter",
                    "{SANCT_ENTER_TEXT}",
                    args =>
                    {
                        try
                        {
                            if (!HasSanctuary(Settlement.CurrentSettlement)) return false;
                            float mult = SanctuaryTraitMultiplier();
                            MBTextManager.SetTextVariable("SANCT_ENTER_TEXT",
                                "Visit the Sanctuary" + SanctuaryTraitNote(mult));
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; } catch { }
                            args.IsEnabled = true;
                            return true;
                        }
                        catch { return false; }
                    },
                    args => { try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } },
                    false, -1, false);
            }
            catch { }

            // ── Sanctuary sub-menu ──────────────────────────────────────────
            try
            {
                starter.AddGameMenu(
                    "sanctuary_menu",
                    "{SANCT_MENU_HEADER}",
                    args =>
                    {
                        try
                        {
                            int today = CurrentCampaignDay();
                            int rem  = CampaignMapEvents.ProtectedDaysRemaining;
                            int lstk = ComputeLivestockGold();
                            string prot  = rem  > 0 ? $"  [Protective ward active: {rem} days remaining]" : "";
                            string lsNote = lstk > 0 ? $"  [Livestock: {lstk}g value]" : "";

                            string blessedNote = _blessedUntilDay >= today
                                ? $"  [Blessed: {_blessedUntilDay - today + 1} day(s) remaining]" : "";
                            string steadyNote = _steadyLineUntilDay >= today
                                ? $"  [Steady the Line: {_steadyLineUntilDay - today + 1} day(s) remaining]" : "";
                            string traitNote = _traitBoostUntilDay >= today
                                ? $"  [Flame Mark: {_traitBoostUntilDay - today + 1} day(s) remaining]" : "";

                            string hdr = IsTempleMember()
                                ? $"The Sanctuary of The Temple. The flame knows you. All rites cost 40% less.{prot}{lsNote}{blessedNote}{steadyNote}{traitNote}"
                                : $"The Sanctuary. Candles burn in rows that stretch further than the room should allow.{prot}{lsNote}{blessedNote}{steadyNote}{traitNote}";
                            MBTextManager.SetTextVariable("SANCT_MENU_HEADER", hdr);
                        }
                        catch { }
                    });
            }
            catch { }

            // ── Prayer of Strength ──────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption(
                    "sanctuary_menu", "sanctuary_prayer",
                    "{SANCT_PRAY_TEXT}",
                    args =>
                    {
                        try
                        {
                            int cost = GoldCost(BaseCostPrayer);
                            float mult = SanctuaryTraitMultiplier();
                            string cooldownNote = "";
                            if (IsRiteOnCooldown(_lastPrayerDay, PrayerCooldownBase, mult))
                            {
                                args.IsEnabled = false;
                                float absM = Math.Min(1f, Math.Abs(mult));
                                int cooldown = Math.Max(1, (int)(PrayerCooldownBase * (2f - absM)));
                                int daysLeft = cooldown - (CurrentCampaignDay() - _lastPrayerDay);
                                cooldownNote = $"  [On cooldown: {daysLeft} day(s)]";
                            }
                            else
                            {
                                args.IsEnabled = CanAffordSanctuary(cost);
                            }
                            MBTextManager.SetTextVariable("SANCT_PRAY_TEXT",
                                $"Prayer of Strength ({cost}g{LivestockNote(cost)}) — fortify party morale (+{MoralePrayerBoost}){cooldownNote}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => PerformPrayer());
            }
            catch { }

            // ── Protective Rites ────────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption(
                    "sanctuary_menu", "sanctuary_rites",
                    "{SANCT_RITES_TEXT}",
                    args =>
                    {
                        try
                        {
                            int cost  = GoldCost(BaseCostProtective);
                            int aging = AgingCost(BaseAgingProtective);
                            int cur   = CampaignMapEvents.ProtectedDaysRemaining;
                            float mult = SanctuaryTraitMultiplier();
                            string note = cur > 0 ? $"  [active: {cur} days left]" : "";
                            string cooldownNote = "";
                            if (IsRiteOnCooldown(_lastProtectiveDay, ProtectiveCooldownBase, mult))
                            {
                                args.IsEnabled = false;
                                float absM = Math.Min(1f, Math.Abs(mult));
                                int cooldown = Math.Max(1, (int)(ProtectiveCooldownBase * (2f - absM)));
                                int daysLeft = cooldown - (CurrentCampaignDay() - _lastProtectiveDay);
                                cooldownNote = $"  [On cooldown: {daysLeft} day(s)]";
                            }
                            else
                            {
                                args.IsEnabled = CanAffordSanctuary(cost);
                            }
                            MBTextManager.SetTextVariable("SANCT_RITES_TEXT",
                                $"Protective Rites ({cost}g{LivestockNote(cost)}, +{aging} days older) — ward Ashen events for {ProtectiveDays} days{note}{cooldownNote}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => PerformProtectiveRites());
            }
            catch { }

            // ── Turn the Ashen ──────────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption(
                    "sanctuary_menu", "sanctuary_turn",
                    "{SANCT_TURN_TEXT}",
                    args =>
                    {
                        try
                        {
                            int cost  = GoldCost(BaseCostTurnAshen);
                            int aging = AgingCost(BaseAgingTurnAshen);
                            float mult = SanctuaryTraitMultiplier();
                            string cooldownNote = "";
                            if (IsRiteOnCooldown(_lastTurnAshenDay, TurnAshenCooldownBase, mult))
                            {
                                args.IsEnabled = false;
                                float absM = Math.Min(1f, Math.Abs(mult));
                                int cooldown = Math.Max(1, (int)(TurnAshenCooldownBase * (2f - absM)));
                                int daysLeft = cooldown - (CurrentCampaignDay() - _lastTurnAshenDay);
                                cooldownNote = $"  [On cooldown: {daysLeft} day(s)]";
                            }
                            else
                            {
                                args.IsEnabled = CanAffordSanctuary(cost);
                            }
                            MBTextManager.SetTextVariable("SANCT_TURN_TEXT",
                                $"Turn the Ashen ({cost}g{LivestockNote(cost)}, +{aging} days older) — banish and wound nearby Ashen parties{cooldownNote}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => PerformTurnAshen());
            }
            catch { }

            // ── Prayer of Healing ───────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption(
                    "sanctuary_menu", "sanctuary_healing",
                    "{SANCT_HEAL_TEXT}",
                    args =>
                    {
                        try
                        {
                            int cost  = GoldCost(BaseCostHealing);
                            int aging = AgingCost(BaseAgingHealing);
                            float mult = SanctuaryTraitMultiplier();
                            string cooldownNote = "";
                            if (IsRiteOnCooldown(_lastHealingDay, HealingCooldownBase, mult))
                            {
                                args.IsEnabled = false;
                                float absM = Math.Min(1f, Math.Abs(mult));
                                int cooldown = Math.Max(1, (int)(HealingCooldownBase * (2f - absM)));
                                int daysLeft = cooldown - (CurrentCampaignDay() - _lastHealingDay);
                                cooldownNote = $"  [On cooldown: {daysLeft} day(s)]";
                            }
                            else
                            {
                                args.IsEnabled = CanAffordSanctuary(cost);
                            }
                            MBTextManager.SetTextVariable("SANCT_HEAL_TEXT",
                                $"Prayer of Healing ({cost}g{LivestockNote(cost)}, +{aging} days older) — heal the wounded or steady the line{cooldownNote}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => PerformHealing());
            }
            catch { }

            // ── Prayer for a Blessing (rejuvenation / trait mark) ───────────
            try
            {
                starter.AddGameMenuOption(
                    "sanctuary_menu", "sanctuary_blessing",
                    "{SANCT_BLESS_TEXT}",
                    args =>
                    {
                        try
                        {
                            int  cost  = BlessingCost();
                            float mult = SanctuaryTraitMultiplier();
                            string cooldownNote = "";
                            if (IsRiteOnCooldown(_lastBlessingDay, BlessingCooldownBase, mult))
                            {
                                args.IsEnabled = false;
                                float absM = Math.Min(1f, Math.Abs(mult));
                                int cooldown = Math.Max(1, (int)(BlessingCooldownBase * (2f - absM)));
                                int daysLeft = cooldown - (CurrentCampaignDay() - _lastBlessingDay);
                                cooldownNote = $"  [On cooldown: {daysLeft} day(s)]";
                            }
                            else
                            {
                                bool canAfford  = CanAffordSanctuary(cost);
                                args.IsEnabled  = canAfford;
                            }
                            MBTextManager.SetTextVariable("SANCT_BLESS_TEXT",
                                $"Prayer for a Blessing ({cost}g{LivestockNote(cost)}) — shed a year or receive the flame's mark{cooldownNote}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => PerformBlessing());
            }
            catch { }

            // ── Leave ───────────────────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption(
                    "sanctuary_menu", "sanctuary_leave",
                    "Leave the Sanctuary",
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

        // Scheme city-menu option is now registered by SchemeCampaignBehavior.RegisterCityMenuOption.

        // ── Service implementations ────────────────────────────────────────────

        private static void PerformPrayer()
        {
            try
            {
                int   cost = GoldCost(BaseCostPrayer);
                float mult = SanctuaryTraitMultiplier();
                int   boost = (int)(MoralePrayerBoost * mult);
                ResolveSanctuaryPayment(cost, () =>
                {
                    try { MobileParty.MainParty.RecentEventsMorale += boost; } catch { }
                    string narrative;
                    if (boost > 0)
                    {
                        _blessedUntilDay = CurrentCampaignDay() + BlessedDays;
                        narrative = "The candles are the same ones that have burned here for decades — the wax is built up in long columns, the flames do not flicker. You speak no words aloud. The fire listens anyway.\n\n" +
                            "When you rise, the weight is not gone, but it sits differently. Your men will feel it before they know why — a steadiness in the line, a fraction less give in the shoulders. " +
                            "It is a small thing. It is also everything.\n\n" +
                            $"For the next {BlessedDays} days your surgeons will find their patients cooperative.";
                    }
                    else if (boost == 0)
                        narrative = "The candles burn indifferently. You kneel and speak the words, but the fire does not stir for you. " +
                            "It is not hostile — it simply does not know what you are. You leave having paid. Nothing else changed.";
                    else
                        narrative = "The candles falter when you enter. The priest takes a half-step back. You speak the words anyway, and the fire answers — " +
                            "but not with warmth. A chill passes through your ranks outside, sudden and sourceless. " +
                            $"Party morale drops by {-boost}. The flame does not forgive what you have become.";
                    _lastPrayerDay = CurrentCampaignDay();
                    _lastSanctuaryUseDay = CurrentCampaignDay();
                    _sanctuaryUseCount++;
                    try
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            "Prayer of Strength", narrative, true, false, "So be it.", "", null, null));
                    }
                    catch { MBInformationManager.AddQuickInformation(new TextObject(boost > 0
                        ? $"Prayer of Strength — +{boost} morale."
                        : boost == 0 ? "Prayer of Strength — no effect. The flame does not know you."
                        : $"Prayer of Strength — the flame recoils. {boost} morale.")); }
                });
            }
            catch { }
            finally { try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } }
        }

        private static void PerformProtectiveRites()
        {
            try
            {
                int   cost   = GoldCost(BaseCostProtective);
                int   aging  = AgingCost(BaseAgingProtective);
                float mult   = SanctuaryTraitMultiplier();
                int   days   = mult > 0.01f ? Math.Max(7, (int)(ProtectiveDays * mult)) : Math.Max(0, (int)(ProtectiveDays * mult));
                ResolveSanctuaryPayment(cost, () =>
                {
                    AgeHero(Hero.MainHero, aging);
                    string narrative;
                    if (mult > 0.01f)
                    {
                        CampaignMapEvents.StartProtection(days);

                        float px = 0f, py = 0f;
                        try { px = MobileParty.MainParty.GetPosition2D.x; py = MobileParty.MainParty.GetPosition2D.y; } catch { }
                        float rangeSquared = 200f * 200f;
                        var nearbyAshen = MobileParty.All
                            .Where(p =>
                            {
                                if (!p.IsActive || p.MapFaction?.StringId != AshenKingdomId) return false;
                                float dx = p.GetPosition2D.x - px, dy = p.GetPosition2D.y - py;
                                return dx * dx + dy * dy < rangeSquared;
                            })
                            .Take(3).ToList();
                        string ashenNote = nearbyAshen.Count > 0
                            ? $"\n\nThe flame shows you what hunts nearby: {string.Join(", ", nearbyAshen.Select(p => p.Name?.ToString() ?? "?"))}."
                            : "\n\nNo grey things stir within sight of the flame right now.";

                        narrative = "The priest draws symbols in ash across your palms and speaks words that are not quite in any language you recognise. The flame on the altar burns a shade hotter for a moment, then returns to itself.\n\n" +
                            $"You will carry this for {days} days. The grey things that hunt in the cold will find your scent harder to follow. " +
                            "It does not make you invisible. It makes you less interesting to whatever thinks of you as prey.\n\n" +
                            "The priest does not say farewell. He simply returns to his candles." +
                            ashenNote;

                        _lastProtectiveDay = CurrentCampaignDay();
                        _lastSanctuaryUseDay = CurrentCampaignDay();
                        _sanctuaryUseCount++;
                    }
                    else if (mult >= -0.01f)
                    {
                        narrative = "The priest performs the rite. The flame does not respond. Your palms are marked with ash, " +
                            "but something is missing — the ward does not seat itself in you. " +
                            "The Ashen will find your scent as easily as they would have before. You have paid for something that did not arrive.";
                        _lastProtectiveDay = CurrentCampaignDay();
                        _lastSanctuaryUseDay = CurrentCampaignDay();
                        _sanctuaryUseCount++;
                    }
                    else
                    {
                        // Penalty: Ashen events become more likely for a period (simulated as shorter protection, negative message)
                        narrative = "The rite goes wrong. The flame recoils from the ash that is already in you, and instead of warding you it marks you. " +
                            "For the next week something cold will have an easier time finding you. " +
                            $"The priest does not apologise. He simply steps back and watches you leave.";
                        // Apply brief negative protection (the CampaignMapEvents flag can only protect, so we just notify)
                        _lastProtectiveDay = CurrentCampaignDay();
                        _lastSanctuaryUseDay = CurrentCampaignDay();
                        _sanctuaryUseCount++;
                    }
                    try
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            "Protective Rites", narrative, true, false, "I understand.", "", null, null));
                    }
                    catch { MBInformationManager.AddQuickInformation(new TextObject(days > 0
                        ? $"Protective Rites — ward active for {days} days."
                        : mult < 0 ? "Protective Rites — the rite misfired. The cold is now closer."
                        : "Protective Rites — no ward formed.")); }
                });
            }
            catch { }
            finally { try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } }
        }

        private static void PerformTurnAshen()
        {
            try
            {
                int   cost  = GoldCost(BaseCostTurnAshen);
                int   aging = AgingCost(BaseAgingTurnAshen);
                float mult  = SanctuaryTraitMultiplier();
                ResolveSanctuaryPayment(cost, () =>
                {
                    AgeHero(Hero.MainHero, aging);

                    float px = 0f, py = 0f;
                    try { px = MobileParty.MainParty.GetPosition2D.x; py = MobileParty.MainParty.GetPosition2D.y; } catch { }
                    float rangeSquared = 200f * 200f;
                    string narrative;

                    if (mult > 0.01f)
                    {
                        var targets = MobileParty.All
                            .Where(p => p.IsActive && !p.IsMainParty
                                     && p.MapFaction?.StringId == AshenKingdomId)
                            .Select(p => { float dx = p.GetPosition2D.x - px, dy = p.GetPosition2D.y - py; return (party: p, dist2: dx * dx + dy * dy); })
                            .Where(t => t.dist2 < rangeSquared)
                            .OrderBy(t => t.dist2)
                            .Take(3).Select(t => t.party).ToList();

                        int totalWounded = 0;
                        foreach (var party in targets)
                        {
                            int toWound = Math.Max(1, (int)((12 + _rng.Next(9)) * mult)), wounded = 0;
                            try
                            {
                                foreach (var e in party.MemberRoster.GetTroopRoster().ToList())
                                {
                                    if (e.Character.IsHero) continue;
                                    int healthy = e.Number - e.WoundedNumber;
                                    int w = Math.Min(healthy, toWound - wounded);
                                    if (w <= 0) continue;
                                    party.MemberRoster.AddToCounts(e.Character, 0, false, w);
                                    wounded += w;
                                    if (wounded >= toWound) break;
                                }
                                totalWounded += wounded;
                                try { party.RecentEventsMorale -= 35f * mult; } catch { }
                                // Drain food stocks, forcing them to slow and forage
                                try { party.Food = Math.Min(party.Food, 0f); } catch { }
                            }
                            catch { }
                        }
                        narrative = targets.Count == 0
                            ? "The rite is spoken. The flame surges briefly and then settles. The grey things are not close enough to feel it."
                            : $"{targets.Count} Ashen part{(targets.Count > 1 ? "ies" : "y")} have recoiled from the flame. {totalWounded} cold soldiers are on their knees. Their supplies have scattered into the snow.";

                        _lastTurnAshenDay = CurrentCampaignDay();
                        _lastSanctuaryUseDay = CurrentCampaignDay();
                        _sanctuaryUseCount++;
                    }
                    else if (mult < -0.01f)
                    {
                        // Penalty: the Ashen sense the player and gain morale
                        int boosted = 0;
                        foreach (var p in MobileParty.All.Where(p => p.IsActive && !p.IsMainParty && p.MapFaction?.StringId == AshenKingdomId).Take(3))
                        {
                            try { p.RecentEventsMorale += 20f; boosted++; } catch { }
                        }
                        narrative = $"The rite inverts. Instead of burning the Ashen, the flame marks you for them. " +
                            $"{boosted} Ashen part{(boosted != 1 ? "ies" : "y")} sense you through the cold. The priest steps away from the altar quickly.";
                        _lastTurnAshenDay = CurrentCampaignDay();
                        _lastSanctuaryUseDay = CurrentCampaignDay();
                        _sanctuaryUseCount++;
                    }
                    else
                    {
                        narrative = "The rite is spoken. The flame stirs but does not reach — you carry nothing in you it can use as a weapon against the cold.";
                        _lastTurnAshenDay = CurrentCampaignDay();
                        _lastSanctuaryUseDay = CurrentCampaignDay();
                        _sanctuaryUseCount++;
                    }

                    try { InformationManager.ShowInquiry(new InquiryData("Turn the Ashen", narrative, true, false, "The flame holds.", "", null, null)); }
                    catch { MBInformationManager.AddQuickInformation(new TextObject(narrative.Length > 80 ? narrative.Substring(0, 80) + "…" : narrative)); }
                });
            }
            catch { }
            finally { try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } }
        }

        private static void PerformHealing()
        {
            try
            {
                int   cost  = GoldCost(BaseCostHealing);
                int   aging = AgingCost(BaseAgingHealing);
                float mult  = SanctuaryTraitMultiplier();
                ResolveSanctuaryPayment(cost, () =>
                {
                    AgeHero(Hero.MainHero, aging);

                    // Offer two choices: Heal the Wounded or Steady the Line
                    try
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            "The Flame Offers Two Gifts",
                            $"The priest's hands are steady. The flame has enough in it for what you need. Choose how it spends itself.\n\n" +
                            "Heal the Wounded — the injured rise from their cots. Those who were done fighting today are not.\n\n" +
                            $"Steady the Line — the flame does not heal wounds that exist, but for {SteadyLineDays} days the men who fall in battle are carried back rather than left. Your troops will count as wounded rather than dead more often.",
                            true, true,
                            "Heal the Wounded",
                            "Steady the Line",
                            () =>
                            {
                                // Heal the Wounded branch
                                int healed = 0, wounded = 0;
                                var roster = MobileParty.MainParty?.MemberRoster;
                                if (mult > 0.01f)
                                {
                                    if (roster != null)
                                    {
                                        foreach (var e in roster.GetTroopRoster().ToList())
                                        {
                                            if (e.Character.IsHero || e.WoundedNumber <= 0) continue;
                                            int heal = Math.Max(1, (int)(e.WoundedNumber * mult));
                                            try { roster.AddToCounts(e.Character, 0, false, -heal); healed += heal; }
                                            catch { }
                                        }
                                    }
                                    try { if (Hero.MainHero.HitPoints < Hero.MainHero.MaxHitPoints) Hero.MainHero.HitPoints = Hero.MainHero.MaxHitPoints; } catch { }
                                }
                                else if (mult < -0.01f)
                                {
                                    int toWound = Math.Max(1, (int)(5 * Math.Abs(mult)));
                                    if (roster != null)
                                    {
                                        foreach (var e in roster.GetTroopRoster().ToList())
                                        {
                                            if (e.Character.IsHero) continue;
                                            int healthy = e.Number - e.WoundedNumber;
                                            int w = Math.Min(healthy, toWound - wounded);
                                            if (w <= 0) continue;
                                            try { roster.AddToCounts(e.Character, 0, false, w); wounded += w; } catch { }
                                            if (wounded >= toWound) break;
                                        }
                                    }
                                }
                                _lastHealingDay = CurrentCampaignDay();
                                _lastSanctuaryUseDay = CurrentCampaignDay();
                                _sanctuaryUseCount++;
                                string healNarrative;
                                if (healed > 0)
                                    healNarrative = $"The priest says a word you don't catch. Then another. By the third, the bandaged men in your camp are sitting up — " +
                                        $"not better, exactly, but less far from it. {healed} soldier{(healed != 1 ? "s" : "")} who should have needed another week are " +
                                        "folding their blankets and checking their equipment. They don't ask how. Some things don't improve from asking.";
                                else if (wounded > 0)
                                    healNarrative = $"The priest speaks and the flame answers, but what it sends out is cold. " +
                                        $"Your men outside are worse than they were — {wounded} soldier{(wounded != 1 ? "s" : "")} have taken a turn for the worse, " +
                                        "wounds reopening or simply deepening. The fire does not answer the faithless with mercy.";
                                else
                                    healNarrative = "The priest speaks the words. The flame listens but does not act — you carry nothing in you it recognises as worth healing. " +
                                        "You have paid. The fire decided what that bought.";
                                try
                                {
                                    InformationManager.ShowInquiry(new InquiryData(
                                        "Prayer of Healing", healNarrative, true, false, "It is enough.", "", null, null));
                                }
                                catch { MBInformationManager.AddQuickInformation(new TextObject(healed > 0
                                    ? $"Prayer of Healing — {healed} soldier{(healed != 1 ? "s" : "")} restored."
                                    : wounded > 0 ? $"Prayer of Healing — the flame penalised you. {wounded} soldiers worsened."
                                    : "Prayer of Healing — no effect.")); }
                            },
                            () =>
                            {
                                // Steady the Line branch
                                _steadyLineUntilDay = CurrentCampaignDay() + SteadyLineDays;
                                _lastHealingDay = CurrentCampaignDay();
                                _lastSanctuaryUseDay = CurrentCampaignDay();
                                _sanctuaryUseCount++;
                                string steadyNarrative = $"The priest does not touch you. He stands at the altar and speaks to the flame alone. When he finishes, the candles burn with a steadier light.\n\n" +
                                    $"For the next {SteadyLineDays} days, the flame will carry your fallen back from the edge. The surgeons will find their work comes easier. " +
                                    "Men who would have been lost will instead be found on cots by morning.";
                                try
                                {
                                    InformationManager.ShowInquiry(new InquiryData(
                                        "Steady the Line", steadyNarrative, true, false, "The line holds.", "", null, null));
                                }
                                catch { MBInformationManager.AddQuickInformation(new TextObject($"Steady the Line active for {SteadyLineDays} days.")); }
                            }));
                    }
                    catch
                    {
                        // Fallback: direct healing
                        int healed = 0;
                        var roster = MobileParty.MainParty?.MemberRoster;
                        if (mult > 0.01f && roster != null)
                        {
                            foreach (var e in roster.GetTroopRoster().ToList())
                            {
                                if (e.Character.IsHero || e.WoundedNumber <= 0) continue;
                                int heal = Math.Max(1, (int)(e.WoundedNumber * mult));
                                try { roster.AddToCounts(e.Character, 0, false, -heal); healed += heal; } catch { }
                            }
                        }
                        _lastHealingDay = CurrentCampaignDay();
                        _lastSanctuaryUseDay = CurrentCampaignDay();
                        _sanctuaryUseCount++;
                        MBInformationManager.AddQuickInformation(new TextObject(healed > 0
                            ? $"Prayer of Healing — {healed} soldier{(healed != 1 ? "s" : "")} restored."
                            : "Prayer of Healing — no effect."));
                    }
                });
            }
            catch { }
            finally { try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } }
        }

        private static void PerformBlessing()
        {
            try
            {
                int   cost = BlessingCost();
                float mult = SanctuaryTraitMultiplier();
                var   hero = Hero.MainHero;
                if (hero == null) return;
                ResolveSanctuaryPayment(cost, () =>
                {
                    // Offer two choices: Shed a year OR Flame marks you
                    try
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            "What Will You Ask of the Flame?",
                            "The flame waits. It has enough in it for one thing.",
                            true, true,
                            "Shed a year",
                            "The flame marks you",
                            () =>
                            {
                                // Rejuvenation branch
                                string blessNarrative;
                                if (mult > 0.01f)
                                {
                                    float currentAge = hero.Age;
                                    int   maxRejuv   = (int)Math.Max(0f, (currentAge - BlessingMinAge) * 365.25f);
                                    int   actualDays = Math.Min((int)(BlessingRejuvDays * mult), maxRejuv);
                                    if (actualDays > 0) try { AgingSystem.RejuvenateHero(hero, actualDays); } catch { }
                                    int yearsGained = actualDays / 365;
                                    blessNarrative = yearsGained > 0
                                        ? $"The priest does not explain what he is doing. He places both hands on the altar, speaks without pause for several minutes, " +
                                          "and when he finishes the candles are slightly shorter than they were. So are you, in a way that doesn't show in a mirror but " +
                                          $"shows in how your joints feel the next morning. {yearsGained} year{(yearsGained != 1 ? "s" : "")} paid back. " +
                                          "You don't ask where they went. Some debts are settled in kinds of coin you don't want to examine."
                                        : "The priest completes the rite. The flame does what it can, but finds very little left to return. " +
                                          "You are already near the limit of what this place can offer.";
                                }
                                else if (mult < -0.01f)
                                {
                                    int penalty = Math.Max(1, (int)(180 * Math.Abs(mult)));
                                    try { AgingSystem.AgeHero(hero, penalty); } catch { }
                                    blessNarrative = $"The priest begins the words. The flame answers — but coldly, violently, as if offended by what it finds in you. " +
                                        $"When it is done you feel older, not younger. {penalty / 365f:F1} years pressed back into you by something that refused to be given to the likes of what you are. " +
                                        "The priest does not meet your eyes as you leave.";
                                }
                                else
                                    blessNarrative = "The priest completes the rite. The flame does not respond to you — not with hostility, but with indifference. " +
                                        "You carry nothing it recognises as worth returning. The candles burned for nothing. You paid for the attempt.";

                                _lastBlessingDay = CurrentCampaignDay();
                                _lastSanctuaryUseDay = CurrentCampaignDay();
                                _sanctuaryUseCount++;
                                try
                                {
                                    InformationManager.ShowInquiry(new InquiryData(
                                        "Prayer for a Blessing", blessNarrative, true, false, "So be it.", "", null, null));
                                }
                                catch { MBInformationManager.AddQuickInformation(new TextObject(
                                    mult > 0.01f ? "Prayer for a Blessing — years returned."
                                    : mult < -0.01f ? "Prayer for a Blessing — the flame aged you."
                                    : "Prayer for a Blessing — no effect.")); }
                            },
                            () =>
                            {
                                // Flame marks you branch
                                _traitBoostUntilDay = CurrentCampaignDay() + TraitBoostDays;
                                _traitBoostAmount = 1f / 6f;
                                _lastBlessingDay = CurrentCampaignDay();
                                _lastSanctuaryUseDay = CurrentCampaignDay();
                                _sanctuaryUseCount++;
                                string markNarrative = "The priest says nothing. He presses his thumb against your brow and the candles burn a shade brighter for a breath. " +
                                    "When you walk out, the men at the gate seem easier around you — as if something they couldn't name before has settled. " +
                                    $"The effect will fade in {TraitBoostDays} days.";
                                try
                                {
                                    InformationManager.ShowInquiry(new InquiryData(
                                        "The Flame Marks You", markNarrative, true, false, "I carry it now.", "", null, null));
                                }
                                catch { MBInformationManager.AddQuickInformation(new TextObject($"Flame mark active for {TraitBoostDays} days.")); }
                            }));
                    }
                    catch
                    {
                        // Fallback: rejuvenation only
                        string blessNarrative;
                        if (mult > 0.01f)
                        {
                            float currentAge = hero.Age;
                            int   maxRejuv   = (int)Math.Max(0f, (currentAge - BlessingMinAge) * 365.25f);
                            int   actualDays = Math.Min((int)(BlessingRejuvDays * mult), maxRejuv);
                            if (actualDays > 0) try { AgingSystem.RejuvenateHero(hero, actualDays); } catch { }
                            int yearsGained = actualDays / 365;
                            blessNarrative = yearsGained > 0
                                ? $"Prayer for a Blessing — {yearsGained} year{(yearsGained != 1 ? "s" : "")} returned."
                                : "Prayer for a Blessing — the flame could not return more.";
                        }
                        else
                            blessNarrative = "Prayer for a Blessing — no effect.";
                        _lastBlessingDay = CurrentCampaignDay();
                        _lastSanctuaryUseDay = CurrentCampaignDay();
                        _sanctuaryUseCount++;
                        MBInformationManager.AddQuickInformation(new TextObject(blessNarrative));
                    }
                });
            }
            catch { }
            finally { try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } }
        }

        // ── NPC daily tick ─────────────────────────────────────────────────────
        private static void OnDailyTick()
        {
            int today = CurrentCampaignDay();

            // Blessed status: partially heal wounded troops each day
            if (_blessedUntilDay >= today && MobileParty.MainParty?.MemberRoster != null)
            {
                foreach (var e in MobileParty.MainParty.MemberRoster.GetTroopRoster().ToList())
                {
                    if (e.Character.IsHero || e.WoundedNumber <= 0) continue;
                    int heal = Math.Max(1, (int)(e.WoundedNumber * 0.10f));
                    try { MobileParty.MainParty.MemberRoster.AddToCounts(e.Character, 0, false, -heal); } catch { }
                }
            }

            // Trait boost expiry
            if (_traitBoostUntilDay >= 0 && today > _traitBoostUntilDay)
            {
                _traitBoostAmount = 0f;
                _traitBoostUntilDay = -1;
            }

            // Trait drift: every TraitDriftThreshold sanctuary uses, nudge traits up
            if (_sanctuaryUseCount > 0 && _sanctuaryUseCount % TraitDriftThreshold == 0)
            {
                try
                {
                    var h = Hero.MainHero;
                    if (h != null)
                    {
                        int mercy = h.GetTraitLevel(DefaultTraits.Mercy);
                        int honor = h.GetTraitLevel(DefaultTraits.Honor);
                        int gen   = h.GetTraitLevel(DefaultTraits.Generosity);
                        if (mercy <= honor && mercy <= gen && mercy < 2)
                            h.SetTraitLevel(DefaultTraits.Mercy, mercy + 1);
                        else if (honor <= mercy && honor <= gen && honor < 2)
                            h.SetTraitLevel(DefaultTraits.Honor, honor + 1);
                        else if (gen < 2)
                            h.SetTraitLevel(DefaultTraits.Generosity, gen + 1);
                        MBInformationManager.AddQuickInformation(new TextObject(
                            "The flame has changed you. A virtue has deepened in you without your noticing."));
                    }
                }
                catch { }
                _sanctuaryUseCount = 0; // reset after drift fires
            }

            // Steady the Line: extra daily healing while active
            if (_steadyLineUntilDay >= today && MobileParty.MainParty?.MemberRoster != null)
            {
                int healed = 0;
                foreach (var e in MobileParty.MainParty.MemberRoster.GetTroopRoster().ToList())
                {
                    if (e.Character.IsHero || e.WoundedNumber <= 0) continue;
                    int heal = Math.Min(e.WoundedNumber, 2);
                    try { MobileParty.MainParty.MemberRoster.AddToCounts(e.Character, 0, false, -heal); healed += heal; } catch { }
                    if (healed >= 3) break;
                }
            }

            // ── Honourable + Merciful lords in any sanctuary city ─────────────
            try
            {
                foreach (var hero in Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && !h.IsPrisoner && !h.IsChild
                             && h != Hero.MainHero
                             && h.CurrentSettlement != null
                             && HasSanctuary(h.CurrentSettlement)
                             && NpcCanUseSanctuary(h))
                    .OrderBy(_ => _rng.Next())
                    .Take(8))   // cap to avoid iterating too many per tick
                {
                    if (_rng.NextDouble() > 0.003) continue;   // 0.3% per qualifying lord per day

                    string city = hero.CurrentSettlement?.Name?.ToString() ?? "the sanctuary";
                    bool   heal = _rng.Next(2) == 0;

                    if (heal)
                    {
                        NpcHealPartyFull(hero.PartyBelongedTo);
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"{hero.Name} — miracle at the sanctuary in {city}. The wounded rose from their beds before sunrise.",
                            new Color(0.80f, 0.72f, 0.45f)));
                    }
                    else
                    {
                        NpcBoostMorale(hero.PartyBelongedTo);
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"{hero.Name} — miracle at the sanctuary in {city}. A renewal of spirit was reported by the guards on watch.",
                            new Color(0.80f, 0.72f, 0.45f)));
                    }
                }
            }
            catch { }

            // ── Temple lords: partial healing + Turn ──────────────────────────
            try
            {
                var temple = Kingdom.All.FirstOrDefault(k =>
                    k.StringId == TempleKingdomId && !k.IsEliminated);
                if (temple == null) return;

                foreach (var lord in Hero.AllAliveHeroes
                    .Where(h => h.IsLord && h.IsAlive && !h.IsPrisoner && !h.IsChild
                             && h != Hero.MainHero
                             && h.Clan?.Kingdom == temple
                             && h.PartyBelongedTo?.IsActive == true))
                {
                    if (_rng.NextDouble() < 0.03)
                        try { NpcHealPartyPartial(lord.PartyBelongedTo, 0.30f); } catch { }
                    if (_rng.NextDouble() < 0.02)
                        try { NpcTurnAshenNear(lord.PartyBelongedTo); } catch { }
                }
            }
            catch { }
        }

        // ── NPC effect helpers ─────────────────────────────────────────────────
        private static void NpcHealPartyFull(MobileParty party)
        {
            if (party?.MemberRoster == null) return;
            foreach (var e in party.MemberRoster.GetTroopRoster().ToList())
            {
                if (e.Character.IsHero || e.WoundedNumber <= 0) continue;
                try { party.MemberRoster.AddToCounts(e.Character, 0, false, -e.WoundedNumber); } catch { }
            }
        }

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

        private static void NpcBoostMorale(MobileParty party)
        {
            if (party == null) return;
            try { party.RecentEventsMorale += 20f; } catch { }
        }

        private static void NpcTurnAshenNear(MobileParty source)
        {
            if (source == null) return;
            float sx = 0f, sy = 0f;
            try { sx = source.GetPosition2D.x; sy = source.GetPosition2D.y; } catch { return; }
            const float rangeSquared = 100f * 100f;

            var target = MobileParty.All
                .Where(p =>
                {
                    if (!p.IsActive || p.MapFaction?.StringId != AshenKingdomId) return false;
                    float dx = p.GetPosition2D.x - sx, dy = p.GetPosition2D.y - sy;
                    return dx * dx + dy * dy < rangeSquared;
                })
                .OrderBy(p => { float dx = p.GetPosition2D.x - sx, dy = p.GetPosition2D.y - sy; return dx * dx + dy * dy; })
                .FirstOrDefault();

            if (target == null) return;
            int toWound = 5 + _rng.Next(6), wounded = 0;
            foreach (var e in target.MemberRoster.GetTroopRoster().ToList())
            {
                if (e.Character.IsHero) continue;
                int healthy = e.Number - e.WoundedNumber;
                int w = Math.Min(healthy, toWound - wounded);
                if (w <= 0) continue;
                try { target.MemberRoster.AddToCounts(e.Character, 0, false, w); wounded += w; } catch { }
                if (wounded >= toWound) break;
            }
            try { target.RecentEventsMorale -= 20f; } catch { }
        }
    }
}
