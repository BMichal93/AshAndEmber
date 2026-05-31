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
//   Prayer for a Blessing   — rejuvenate 10 years (expensive gold, min age 20).
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
        private const int   BaseCostBlessing     =  5000;  // Prayer for a Blessing
        private const int   BlessingRejuvDays    =  3650;  // ~10 years
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
            RegisterSchemeMenuOption(starter);
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

        private static void AgeHero(Hero h, int days)
        {
            if (h == null || days <= 0) return;
            try { AgingSystem.AgeHero(h, days); } catch { }
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
                            int rem = CampaignMapEvents.ProtectedDaysRemaining;
                            string prot = rem > 0 ? $"  [Protective ward active: {rem} days remaining]" : "";
                            string hdr  = IsTempleMember()
                                ? $"The Sanctuary of The Temple. The flame knows you. All rites cost 40% less.{prot}"
                                : $"The Sanctuary. Candles burn in rows that stretch further than the room should allow.{prot}";
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
                                $"Prayer of Strength ({cost}g) — fortify party morale (+{MoralePrayerBoost})");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                            args.IsEnabled = Hero.MainHero?.Gold >= cost;
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
                                $"Protective Rites ({cost}g, +{aging} days older) — ward Ashen events for {ProtectiveDays} days{note}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                            args.IsEnabled = Hero.MainHero?.Gold >= cost;
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
                                $"Turn the Ashen ({cost}g, +{aging} days older) — banish and wound nearby Ashen parties");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                            args.IsEnabled = Hero.MainHero?.Gold >= cost;
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
                                $"Prayer of Healing ({cost}g, +{aging} days older) — fully heal all wounded troops");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                            args.IsEnabled = Hero.MainHero?.Gold >= cost;
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
                            int  cost  = GoldCost(BaseCostBlessing);
                            int  years = BlessingRejuvDays / 365;
                            MBTextManager.SetTextVariable("SANCT_BLESS_TEXT",
                                $"Prayer for a Blessing ({cost}g) — shed ~{years} years of age (floor: {BlessingMinAge})");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                            bool canAfford = Hero.MainHero?.Gold >= cost;
                            bool canYounger = Hero.MainHero?.Age > BlessingMinAge + 1f;
                            args.IsEnabled = canAfford && canYounger;
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

        // ── Scheme city-menu option ────────────────────────────────────────────
        private static void RegisterSchemeMenuOption(CampaignGameStarter starter)
        {
            try
            {
                starter.AddGameMenuOption(
                    "town", "scheme_covert_town",
                    "Arrange some covert business",
                    args =>
                    {
                        try
                        {
                            var s = Settlement.CurrentSettlement;
                            if (s == null || !s.IsTown) return false;
                            if (Hero.MainHero?.Gold < 300) return false;
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                            bool pending = SchemeSystem.PlayerHasPendingScheme();
                            args.IsEnabled = !pending;
                            if (pending)
                                try { args.Tooltip = new TextObject("A scheme is already in motion."); } catch { }
                            return true;
                        }
                        catch { return false; }
                    },
                    args =>
                    {
                        try { GameMenu.ExitToLast(); } catch { }
                        try { SchemeCampaignBehavior.OpenSchemeSelectionUI(); } catch { }
                    },
                    false, -1, false);
            }
            catch { }
        }

        // ── Service implementations ────────────────────────────────────────────

        private static void PerformPrayer()
        {
            try
            {
                int cost = GoldCost(BaseCostPrayer);
                if (Hero.MainHero?.Gold < cost) return;
                Hero.MainHero.Gold -= cost;
                try { MobileParty.MainParty.RecentEventsMorale += MoralePrayerBoost; } catch { }
                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Prayer of Strength — the flame holds. Party morale rises by {MoralePrayerBoost}."));
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
                if (Hero.MainHero?.Gold < cost) return;
                Hero.MainHero.Gold -= cost;
                AgeHero(Hero.MainHero, aging);
                CampaignMapEvents.StartProtection(ProtectiveDays);
                MBInformationManager.AddQuickInformation(new TextObject(
                    $"Protective Rites — the sanctuary's blessing holds for {ProtectiveDays} days. " +
                    $"The Ashen cannot reach through it."));
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
                if (Hero.MainHero?.Gold < cost) return;
                Hero.MainHero.Gold -= cost;
                AgeHero(Hero.MainHero, aging);

                float px = 0f, py = 0f;
                try { px = MobileParty.MainParty.GetPosition2D.x; py = MobileParty.MainParty.GetPosition2D.y; } catch { }
                const float rangeSquared = 200f * 200f;

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

                string msg = targets.Count == 0
                    ? "Turn the Ashen — the prayer rings through cold air. No grey figures are close enough to feel it."
                    : $"Turn the Ashen — {targets.Count} Ashen part{(targets.Count > 1 ? "ies" : "y")} recoil from the light. " +
                      $"{totalWounded} soldiers driven to their knees.";
                MBInformationManager.AddQuickInformation(new TextObject(msg));
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
                if (Hero.MainHero?.Gold < cost) return;
                Hero.MainHero.Gold -= cost;
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

                MBInformationManager.AddQuickInformation(new TextObject(
                    healed > 0
                        ? $"Prayer of Healing — {healed} soldier{(healed != 1 ? "s" : "")} rise from their wounds. The flame restores what the cold tried to take."
                        : "Prayer of Healing — the blessing holds. No wounded remain to restore."));
            }
            catch { }
            finally { try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } }
        }

        private static void PerformBlessing()
        {
            try
            {
                int cost = GoldCost(BaseCostBlessing);
                if (Hero.MainHero?.Gold < cost) return;
                var hero = Hero.MainHero;
                if (hero.Age <= BlessingMinAge) return;
                hero.Gold -= cost;

                // Rejuvenate, clamped so age never falls below BlessingMinAge
                float currentAge     = hero.Age;
                int   maxRejuv       = (int)Math.Max(0f, (currentAge - BlessingMinAge) * 365.25f);
                int   actualDays     = Math.Min(BlessingRejuvDays, maxRejuv);

                if (actualDays > 0)
                    try { AgingSystem.RejuvenateHero(hero, actualDays); } catch { }

                int yearsGained = actualDays / 365;
                MBInformationManager.AddQuickInformation(new TextObject(
                    yearsGained > 0
                        ? $"Prayer for a Blessing — the flame takes {yearsGained} year{(yearsGained != 1 ? "s" : "")} away. Some debts are paid not in coin."
                        : "Prayer for a Blessing — the flame holds. You are as young as the rites will allow."));
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
