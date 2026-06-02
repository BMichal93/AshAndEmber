// =============================================================================
// ASH AND EMBER — Sanctuary/SanctuaryCampaignBehavior.cs
//
// Adds a "Visit the Sanctuary" option to the campaign map town menu in:
//   • Every Temple-owned town.
//   • Two random Empire towns chosen at new-game-start and saved.
//
// Access requires the player to have Honor ≥ 1 AND Mercy ≥ 1.
// Temple members receive a 40% discount on all rites.
//
// Services:
//   Prayer of Strength      — morale boost (gold only).
//   Protective Rites        — blocks Ashen world events for N days (gold + aging).
//   Turn the Ashen          — wounds nearby Ashen parties (gold + aging).
//   Prayer of Healing       — fully heals all wounded troops (gold + aging).
//   Prayer for a Blessing   — rejuvenate 1 year (max(gold/10, 36500), min age 20).
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
        private const int   BlessingMinCost       = 36500;  // floor cost for Prayer for a Blessing
        private const int   BlessingRejuvDays    =   365;  // 1 year
        private const int   BlessingMinAge       =    20;
        private const float TempleDiscount       =  0.60f; // Temple members pay 60% (40% off)
        private const int   ProtectiveDays       =    14;
        private const int   MoralePrayerBoost    =    40;
        private const int   PermanentSanctuaryCount = 2;

        private const string TempleKingdomId = "the_temple";
        private const string AshenKingdomId  = "ashen_kingdom";

        // ── Permanent (non-Temple) sanctuary settlement IDs ───────────────────
        private static readonly List<string> _permanentSanctuaryIds = new List<string>();

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
                    string names = string.Join(" and ", picks.Select(s => s.Name.ToString()));
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

        private static bool PlayerCanUseSanctuary()
        {
            var h = Hero.MainHero;
            if (h == null) return false;
            try
            {
                return h.GetTraitLevel(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultTraits.Honor) >= 1
                    && h.GetTraitLevel(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultTraits.Mercy)  >= 1;
            }
            catch { return false; }
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
            => IsTempleMember() ? (int)(base_ * TempleDiscount) : base_;

        private static int AgingCost(int baseDays)
            => IsTempleMember() ? (int)(baseDays * TempleDiscount) : baseDays;

        private static int BlessingCost()
        {
            int raw = Math.Max((Hero.MainHero?.Gold ?? 0) / 10, BlessingMinCost);
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
                            bool access = PlayerCanUseSanctuary();
                            MBTextManager.SetTextVariable("SANCT_ENTER_TEXT",
                                access ? "Visit the Sanctuary"
                                       : "Visit the Sanctuary [Requires Honourable + Merciful]");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; } catch { }
                            args.IsEnabled = access;
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
                            int rem  = CampaignMapEvents.ProtectedDaysRemaining;
                            int lstk = ComputeLivestockGold();
                            string prot  = rem  > 0 ? $"  [Protective ward active: {rem} days remaining]" : "";
                            string lsNote = lstk > 0 ? $"  [Livestock: {lstk}g value]" : "";
                            string hdr = IsTempleMember()
                                ? $"The Sanctuary of The Temple. The flame knows you. All rites cost 40% less.{prot}{lsNote}"
                                : $"The Sanctuary. Candles burn in rows that stretch further than the room should allow.{prot}{lsNote}";
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
                            MBTextManager.SetTextVariable("SANCT_PRAY_TEXT",
                                $"Prayer of Strength ({cost}g{LivestockNote(cost)}) — fortify party morale (+{MoralePrayerBoost})");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                            args.IsEnabled = CanAffordSanctuary(cost);
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
                            string note = cur > 0 ? $"  [active: {cur} days left]" : "";
                            MBTextManager.SetTextVariable("SANCT_RITES_TEXT",
                                $"Protective Rites ({cost}g{LivestockNote(cost)}, +{aging} days older) — ward Ashen events for {ProtectiveDays} days{note}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                            args.IsEnabled = CanAffordSanctuary(cost);
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
                            MBTextManager.SetTextVariable("SANCT_TURN_TEXT",
                                $"Turn the Ashen ({cost}g{LivestockNote(cost)}, +{aging} days older) — banish and wound nearby Ashen parties");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                            args.IsEnabled = CanAffordSanctuary(cost);
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
                            MBTextManager.SetTextVariable("SANCT_HEAL_TEXT",
                                $"Prayer of Healing ({cost}g{LivestockNote(cost)}, +{aging} days older) — fully heal all wounded troops");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                            args.IsEnabled = CanAffordSanctuary(cost);
                        }
                        catch { }
                        return true;
                    },
                    args => PerformHealing());
            }
            catch { }

            // ── Prayer for a Blessing (rejuvenation) ────────────────────────
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
                            int  years = BlessingRejuvDays / 365;
                            MBTextManager.SetTextVariable("SANCT_BLESS_TEXT",
                                $"Prayer for a Blessing ({cost}g{LivestockNote(cost)}) — shed ~{years} year{(years != 1 ? "s" : "")} of age (floor: {BlessingMinAge})");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                            bool canAfford  = CanAffordSanctuary(cost);
                            bool canYounger = Hero.MainHero?.Age > BlessingMinAge + 1f;
                            args.IsEnabled  = canAfford && canYounger;
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
                int cost = GoldCost(BaseCostPrayer);
                ResolveSanctuaryPayment(cost, () =>
                {
                    try { MobileParty.MainParty.RecentEventsMorale += MoralePrayerBoost; } catch { }
                    try
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            "Prayer of Strength",
                            "The candles are the same ones that have burned here for decades — the wax is built up in long columns, the flames do not flicker. You speak no words aloud. The fire listens anyway.\n\n" +
                            "When you rise, the weight is not gone, but it sits differently. Your men will feel it before they know why — a steadiness in the line, a fraction less give in the shoulders. " +
                            "It is a small thing. It is also everything.",
                            true, false, "So be it.", "", null, null));
                    }
                    catch { MBInformationManager.AddQuickInformation(new TextObject($"Prayer of Strength — the flame holds. Party morale rises by {MoralePrayerBoost}.")); }
                });
            }
            catch { }
            finally { try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } }
        }

        private static void PerformProtectiveRites()
        {
            try
            {
                int cost  = GoldCost(BaseCostProtective);
                int aging = AgingCost(BaseAgingProtective);
                ResolveSanctuaryPayment(cost, () =>
                {
                    AgeHero(Hero.MainHero, aging);
                    CampaignMapEvents.StartProtection(ProtectiveDays);
                    try
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            "Protective Rites",
                            "The priest draws symbols in ash across your palms and speaks words that are not quite in any language you recognise. The flame on the altar burns a shade hotter for a moment, then returns to itself.\n\n" +
                            $"You will carry this for {ProtectiveDays} days. The grey things that hunt in the cold will find your scent harder to follow. " +
                            "It does not make you invisible. It makes you less interesting to whatever thinks of you as prey.\n\n" +
                            "The priest does not say farewell. He simply returns to his candles.",
                            true, false, "I understand.", "", null, null));
                    }
                    catch { MBInformationManager.AddQuickInformation(new TextObject($"Protective Rites — the sanctuary's blessing holds for {ProtectiveDays} days.")); }
                });
            }
            catch { }
            finally { try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } }
        }

        private static void PerformTurnAshen()
        {
            try
            {
                int cost  = GoldCost(BaseCostTurnAshen);
                int aging = AgingCost(BaseAgingTurnAshen);
                ResolveSanctuaryPayment(cost, () =>
                {
                    AgeHero(Hero.MainHero, aging);

                    float px = 0f, py = 0f;
                    try { px = MobileParty.MainParty.GetPosition2D.x; py = MobileParty.MainParty.GetPosition2D.y; } catch { }
                    float rangeSquared = 200f * 200f;

                    var targets = MobileParty.All
                        .Where(p => p.IsActive && !p.IsMainParty
                                 && p.MapFaction?.StringId == AshenKingdomId)
                        .Select(p =>
                        {
                            float dx = p.GetPosition2D.x - px, dy = p.GetPosition2D.y - py;
                            return (party: p, dist2: dx * dx + dy * dy);
                        })
                        .Where(t => t.dist2 < rangeSquared)
                        .OrderBy(t => t.dist2)
                        .Take(3)
                        .Select(t => t.party)
                        .ToList();

                    int totalWounded = 0;
                    foreach (var party in targets)
                    {
                        int toWound = 12 + _rng.Next(9), wounded = 0;
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
                            try { party.RecentEventsMorale -= 35f; } catch { }
                        }
                        catch { }
                    }

                    string narrative = targets.Count == 0
                        ? "The rite is spoken. The flame on the altar surges briefly and then settles. The priest says nothing — " +
                          "the prayer was heard, but whatever grey things move in the cold are not close enough to feel it. " +
                          "Sometimes that is the only answer the flame gives."
                        : $"The priest does not stop speaking even as the light changes. Outside, something is happening — " +
                          $"you can feel it through the stone floor, a vibration that is not quite sound. " +
                          $"{targets.Count} Ashen part{(targets.Count > 1 ? "ies" : "y")} have recoiled from it. " +
                          $"{totalWounded} cold soldiers are on their knees in the dark, wondering what struck them. " +
                          "The flame on the altar returns to its ordinary size. The priest finishes his words.";
                    try
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            "Turn the Ashen", narrative, true, false, "The flame holds.", "", null, null));
                    }
                    catch { MBInformationManager.AddQuickInformation(new TextObject(targets.Count == 0
                        ? "Turn the Ashen — no Ashen are close enough to feel it."
                        : $"Turn the Ashen — {targets.Count} part{(targets.Count > 1 ? "ies" : "y")}, {totalWounded} wounded.")); }
                });
            }
            catch { }
            finally { try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } }
        }

        private static void PerformHealing()
        {
            try
            {
                int cost  = GoldCost(BaseCostHealing);
                int aging = AgingCost(BaseAgingHealing);
                ResolveSanctuaryPayment(cost, () =>
                {
                    AgeHero(Hero.MainHero, aging);

                    int healed = 0;
                    var roster = MobileParty.MainParty?.MemberRoster;
                    if (roster != null)
                    {
                        foreach (var e in roster.GetTroopRoster().ToList())
                        {
                            if (e.Character.IsHero || e.WoundedNumber <= 0) continue;
                            try { roster.AddToCounts(e.Character, 0, false, -e.WoundedNumber); healed += e.WoundedNumber; }
                            catch { }
                        }
                    }
                    try { if (Hero.MainHero.HitPoints < Hero.MainHero.MaxHitPoints) Hero.MainHero.HitPoints = Hero.MainHero.MaxHitPoints; } catch { }

                    string healNarrative = healed > 0
                        ? $"The priest says a word you don't catch. Then another. By the third, the bandaged men in your camp are sitting up — " +
                          $"not better, exactly, but less far from it. {healed} soldier{(healed != 1 ? "s" : "")} who should have needed another week are " +
                          "folding their blankets and checking their equipment. They don't ask how. Some things don't improve from asking."
                        : "The priest speaks the words regardless. The flame does its work through stone and distance. " +
                          "When you return to your men you find none of them wounded — the blessing confirmed what was already true. " +
                          "It is not the most dramatic outcome. It is still something.";
                    try
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            "Prayer of Healing", healNarrative, true, false, "It is enough.", "", null, null));
                    }
                    catch { MBInformationManager.AddQuickInformation(new TextObject(healed > 0
                        ? $"Prayer of Healing — {healed} soldier{(healed != 1 ? "s" : "")} restored."
                        : "Prayer of Healing — no wounded to restore.")); }
                });
            }
            catch { }
            finally { try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } }
        }

        private static void PerformBlessing()
        {
            try
            {
                int cost = BlessingCost();
                var hero = Hero.MainHero;
                if (hero?.Age <= BlessingMinAge) return;
                ResolveSanctuaryPayment(cost, () =>
                {
                    float currentAge = hero.Age;
                    int   maxRejuv   = (int)Math.Max(0f, (currentAge - BlessingMinAge) * 365.25f);
                    int   actualDays = Math.Min(BlessingRejuvDays, maxRejuv);

                    if (actualDays > 0)
                        try { AgingSystem.RejuvenateHero(hero, actualDays); } catch { }

                    int yearsGained = actualDays / 365;
                    string blessNarrative = yearsGained > 0
                        ? $"The priest does not explain what he is doing. He places both hands on the altar, speaks without pause for several minutes, " +
                          "and when he finishes the candles are slightly shorter than they were. So are you, in a way that doesn't show in a mirror but " +
                          $"shows in how your joints feel the next morning. {yearsGained} year{(yearsGained != 1 ? "s" : "")} paid back. " +
                          "You don't ask where they went. Some debts are settled in kinds of coin you don't want to examine."
                        : "The priest completes the rite. The flame does what it can. You are as young as the sanctuary will allow — " +
                          "which is to say, the years behind you are already as few as this place can make them. " +
                          "There is something clarifying about reaching a limit.";
                    try
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            "Prayer for a Blessing", blessNarrative, true, false, "Some debts are settled.", "", null, null));
                    }
                    catch { MBInformationManager.AddQuickInformation(new TextObject(yearsGained > 0
                        ? $"Prayer for a Blessing — {yearsGained} year{(yearsGained != 1 ? "s" : "")} returned."
                        : "Prayer for a Blessing — already as young as the rites allow.")); }
                });
            }
            catch { }
            finally { try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } }
        }

        // ── NPC daily tick ─────────────────────────────────────────────────────
        private static void OnDailyTick()
        {
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
                        MBInformationManager.AddQuickInformation(new TextObject(
                            $"Miracle — {hero.Name} prayed at the sanctuary in {city}. " +
                            $"The wounded rose from their beds before sunrise."));
                    }
                    else
                    {
                        NpcBoostMorale(hero.PartyBelongedTo);
                        MBInformationManager.AddQuickInformation(new TextObject(
                            $"Miracle — {hero.Name} knelt at the sanctuary in {city}. " +
                            $"A renewal of spirit was reported by the guards on watch."));
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
