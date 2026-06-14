// =============================================================================
// ASH AND EMBER — SanctuaryCampaignBehavior.Menus.cs
// Sanctuary menu registration and the five rite initiators.
// Partial of SanctuaryCampaignBehavior (shared static state lives in SanctuaryCampaignBehavior.cs).
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
    public partial class SanctuaryCampaignBehavior
    {
        // ── Menu registration ──────────────────────────────────────────────────
        private static void RegisterSanctuaryMenus(CampaignGameStarter starter)
        {
            // Entry in town menu
            try
            {
                starter.AddGameMenuOption("town", "sanctuary_enter", "{SANCT_ENTER_TEXT}",
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

            // Sub-menu header
            try
            {
                starter.AddGameMenu("sanctuary_menu", "{SANCT_MENU_HEADER}", args =>
                {
                    try
                    {
                        int today = CurrentCampaignDay();
                        int rem   = CampaignMapEvents.ProtectedDaysRemaining;
                        string protNote  = rem > 0 ? $"  [Protective ward: {rem} day(s) remaining]" : "";
                        string blessNote = _blessedUntilDay >= today ? $"  [Blessed: {_blessedUntilDay - today + 1} day(s)]" : "";
                        string steadNote = _steadyLineUntilDay >= today ? $"  [Steady the Line: {_steadyLineUntilDay - today + 1} day(s)]" : "";
                        string traitNote = _traitBoostUntilDay >= today ? $"  [Flame Mark: {_traitBoostUntilDay - today + 1} day(s)]" : "";
                        string deplNote = IsLocationDepleted()
                            ? $"  [SPENT — returns in {LocationDepletedDaysLeft()} day(s)]" : "";
                        string interNote = "";
                        int sinceAltar = today - AshenAltarsCampaignBehavior._lastAltarUseDay;
                        if (sinceAltar >= 0 && sinceAltar < CrossInterferenceDays)
                            interNote = $"  [Altar interference — the flame smells the grey stone on you; yield halved for {CrossInterferenceDays - sinceAltar} day(s)]";
                        string hdr = IsTempleMember()
                            ? $"The Sanctuary of The Temple. The flame knows you. Cooldowns reduced.{protNote}{blessNote}{steadNote}{traitNote}{interNote}{deplNote}"
                            : $"The Sanctuary. Candles burn in rows that stretch further than the room should allow.{protNote}{blessNote}{steadNote}{traitNote}{interNote}{deplNote}";
                        MBTextManager.SetTextVariable("SANCT_MENU_HEADER", hdr);
                    }
                    catch { }
                });
            }
            catch { }

            // ── Prayer of Strength ──────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("sanctuary_menu", "sanctuary_prayer", "{SANCT_PRAY_TEXT}",
                    args =>
                    {
                        try
                        {
                            float mult = SanctuaryTraitMultiplier();
                            string cd = "";
                            if (IsLocationDepleted()) { args.IsEnabled = false; }
                            else if (IsRiteOnCooldown(_lastPrayerDay, PrayerCooldownBase, mult))
                            {
                                args.IsEnabled = false;
                                cd = $"  [On cooldown: {CooldownDaysLeft(_lastPrayerDay, PrayerCooldownBase, mult)} day(s)]";
                            }
                            MBTextManager.SetTextVariable("SANCT_PRAY_TEXT",
                                $"Prayer of Strength (8–15 hero HP/round) — fortify party morale (+{MoralePrayerBoost}){cd}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => StartPrayerOfStrength());
            }
            catch { }

            // ── Protective Rites ────────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("sanctuary_menu", "sanctuary_rites", "{SANCT_RITES_TEXT}",
                    args =>
                    {
                        try
                        {
                            float mult = SanctuaryTraitMultiplier();
                            int cur = CampaignMapEvents.ProtectedDaysRemaining;
                            string cd = "", active = cur > 0 ? $"  [active: {cur} day(s) left]" : "";
                            if (IsLocationDepleted()) { args.IsEnabled = false; }
                            else if (IsRiteOnCooldown(_lastProtectiveDay, ProtectiveCooldownBase, mult))
                            {
                                args.IsEnabled = false;
                                cd = $"  [On cooldown: {CooldownDaysLeft(_lastProtectiveDay, ProtectiveCooldownBase, mult)} day(s)]";
                            }
                            MBTextManager.SetTextVariable("SANCT_RITES_TEXT",
                                $"Protective Rites (12–20 hero HP + 1 day aging/round) — ward against Ashen events for {ProtectiveDays} days{active}{cd}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => StartProtectiveRites());
            }
            catch { }

            // ── Turn the Ashen ──────────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("sanctuary_menu", "sanctuary_turn", "{SANCT_TURN_TEXT}",
                    args =>
                    {
                        try
                        {
                            float mult = SanctuaryTraitMultiplier();
                            string cd = "";
                            if (IsLocationDepleted()) { args.IsEnabled = false; }
                            else if (IsRiteOnCooldown(_lastTurnAshenDay, TurnAshenCooldownBase, mult))
                            {
                                args.IsEnabled = false;
                                cd = $"  [On cooldown: {CooldownDaysLeft(_lastTurnAshenDay, TurnAshenCooldownBase, mult)} day(s)]";
                            }
                            MBTextManager.SetTextVariable("SANCT_TURN_TEXT",
                                $"Turn the Ashen (15–25 hero HP/round) — banish and wound nearby Ashen parties{cd}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => StartTurnAshen());
            }
            catch { }

            // ── Prayer of Healing ───────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("sanctuary_menu", "sanctuary_healing", "{SANCT_HEAL_TEXT}",
                    args =>
                    {
                        try
                        {
                            float mult = SanctuaryTraitMultiplier();
                            string cd = "";
                            if (IsLocationDepleted()) { args.IsEnabled = false; }
                            else if (IsRiteOnCooldown(_lastHealingDay, HealingCooldownBase, mult))
                            {
                                args.IsEnabled = false;
                                cd = $"  [On cooldown: {CooldownDaysLeft(_lastHealingDay, HealingCooldownBase, mult)} day(s)]";
                            }
                            MBTextManager.SetTextVariable("SANCT_HEAL_TEXT",
                                $"Prayer of Healing (12–20 hero HP/round) — heal the wounded or steady the line{cd}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => StartHealing());
            }
            catch { }

            // ── Prayer for a Blessing ───────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("sanctuary_menu", "sanctuary_blessing", "{SANCT_BLESS_TEXT}",
                    args =>
                    {
                        try
                        {
                            float mult = SanctuaryTraitMultiplier();
                            string cd = "";
                            if (IsLocationDepleted()) { args.IsEnabled = false; }
                            else if (IsRiteOnCooldown(_lastBlessingDay, BlessingCooldownBase, mult))
                            {
                                args.IsEnabled = false;
                                cd = $"  [On cooldown: {CooldownDaysLeft(_lastBlessingDay, BlessingCooldownBase, mult)} day(s)]";
                            }
                            MBTextManager.SetTextVariable("SANCT_BLESS_TEXT",
                                $"Prayer for a Blessing (15–25 hero HP + 2–4 days aging/round) — shed a year or receive the flame's mark{cd}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => StartBlessing());
            }
            catch { }

            // ── Leave ───────────────────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("sanctuary_menu", "sanctuary_leave", "Leave the Sanctuary",
                    args => { try { args.optionLeaveType = GameMenuOption.LeaveType.Leave; } catch { } return true; },
                    args => { try { GameMenu.SwitchToMenu("town"); } catch { } },
                    true, -1, false);
            }
            catch { }
        }

        // ── Ritual initiators ─────────────────────────────────────────────────

        private static void StartPrayerOfStrength()
        {
            float mult = SanctuaryTraitMultiplier();
            int target = PrayerTargetLo + _rng.Next(PrayerTargetHi - PrayerTargetLo + 1);

            _lastPrayerDay       = CurrentCampaignDay();
            _lastSanctuaryUseDay = CurrentCampaignDay();
            _sanctuaryUseCount++;
            RecordLocationUse();

            RunSanctuaryRitual(
                "Prayer of Strength",
                target, mult,
                () => ApplyMeditationCost_SelfHP(8, 15),
                () =>
                {
                    // Success: boost morale and start blessed status
                    int boost = Math.Max(1, (int)(MoralePrayerBoost * mult));
                    try { MobileParty.MainParty.RecentEventsMorale += boost; } catch { }
                    _blessedUntilDay = CurrentCampaignDay() + BlessedDays;
                    string msg = $"The flame answers. The weight your men carry is the same, but it sits differently. " +
                        $"Morale +" + boost + $". For the next {BlessedDays} days your surgeons will find their patients cooperative.";
                    try { InformationManager.ShowInquiry(new InquiryData("Prayer of Strength", msg, true, false, "So be it.", "", null, null)); }
                    catch { MBInformationManager.AddQuickInformation(new TextObject($"Prayer of Strength — +{boost} morale.")); }
                    try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { }
                },
                () => { ShowRitualFailure("Prayer of Strength"); try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } });
        }

        private static void StartProtectiveRites()
        {
            float mult = SanctuaryTraitMultiplier();
            int target = ProtectTargetLo + _rng.Next(ProtectTargetHi - ProtectTargetLo + 1);

            _lastProtectiveDay   = CurrentCampaignDay();
            _lastSanctuaryUseDay = CurrentCampaignDay();
            _sanctuaryUseCount++;
            RecordLocationUse();

            RunSanctuaryRitual(
                "Protective Rites",
                target, mult,
                () => ApplyMeditationCost_SelfHP_Aging(12, 20, 1, 1),
                () =>
                {
                    // Success: ward active
                    int days = Math.Max(7, (int)(ProtectiveDays * mult));
                    CampaignMapEvents.StartProtection(days);

                    float px = 0f, py = 0f;
                    try { px = MobileParty.MainParty.GetPosition2D.x; py = MobileParty.MainParty.GetPosition2D.y; } catch { }
                    var nearbyAshen = MobileParty.All
                        .Where(p => { if (!p.IsActive || p.MapFaction?.StringId != AshenKingdomId) return false;
                                      float dx = p.GetPosition2D.x - px, dy = p.GetPosition2D.y - py;
                                      return dx * dx + dy * dy < 200f * 200f; })
                        .Take(3).ToList();
                    string ashenNote = nearbyAshen.Count > 0
                        ? $"\n\nThe flame shows what hunts nearby: {string.Join(", ", nearbyAshen.Select(p => p.Name?.ToString() ?? "?"))}."
                        : "\n\nNo grey things stir within sight of the flame right now.";

                    string msg = $"The priest draws symbols in ash across your palms. The flame burns hotter for a moment. " +
                        $"You carry this for {days} days. The grey things will find your scent harder to follow." + ashenNote;
                    try { InformationManager.ShowInquiry(new InquiryData("Protective Rites", msg, true, false, "I understand.", "", null, null)); }
                    catch { MBInformationManager.AddQuickInformation(new TextObject($"Protective Rites — ward active for {days} days.")); }
                    try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { }
                },
                () => { ShowRitualFailure("Protective Rites"); try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } });
        }

        private static void StartTurnAshen()
        {
            float mult = SanctuaryTraitMultiplier();
            int target = TurnTargetLo + _rng.Next(TurnTargetHi - TurnTargetLo + 1);

            _lastTurnAshenDay    = CurrentCampaignDay();
            _lastSanctuaryUseDay = CurrentCampaignDay();
            _sanctuaryUseCount++;
            RecordLocationUse();

            RunSanctuaryRitual(
                "Turn the Ashen",
                target, mult,
                () => ApplyMeditationCost_SelfHP(15, 25),
                () =>
                {
                    float px = 0f, py = 0f;
                    try { px = MobileParty.MainParty.GetPosition2D.x; py = MobileParty.MainParty.GetPosition2D.y; } catch { }
                    float rng2 = 200f * 200f;
                    var targets = MobileParty.All
                        .Where(p => p.IsActive && !p.IsMainParty && p.MapFaction?.StringId == AshenKingdomId)
                        .Select(p => { float dx = p.GetPosition2D.x - px, dy = p.GetPosition2D.y - py; return (party: p, dist2: dx*dx+dy*dy); })
                        .Where(t => t.dist2 < rng2).OrderBy(t => t.dist2).Take(3).Select(t => t.party).ToList();

                    int totalWounded = 0;
                    foreach (var p in targets)
                    {
                        int toWound = Math.Max(1, (int)((12 + _rng.Next(9)) * mult));
                        int w = 0;
                        foreach (var e in p.MemberRoster.GetTroopRoster().ToList())
                        {
                            if (e.Character.IsHero) continue;
                            int healthy = e.Number - e.WoundedNumber;
                            int n = Math.Min(healthy, toWound - w); if (n <= 0) continue;
                            try { p.MemberRoster.AddToCounts(e.Character, 0, false, n); w += n; } catch { }
                            if (w >= toWound) break;
                        }
                        totalWounded += w;
                        try { p.RecentEventsMorale -= 35f * Math.Max(0.1f, mult); } catch { }
                    }
                    string msg = targets.Count == 0
                        ? "The rite is spoken. The flame surges and settles. No grey things are close enough to feel it."
                        : $"{targets.Count} Ashen part{(targets.Count > 1 ? "ies" : "y")} recoil from the flame. {totalWounded} cold soldiers are on their knees.";
                    try { InformationManager.ShowInquiry(new InquiryData("Turn the Ashen", msg, true, false, "The flame holds.", "", null, null)); }
                    catch { MBInformationManager.AddQuickInformation(new TextObject(msg.Length > 80 ? msg.Substring(0,80)+"…" : msg)); }
                    try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { }
                },
                () => { ShowRitualFailure("Turn the Ashen"); try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } });
        }

        private static void StartHealing()
        {
            float mult = SanctuaryTraitMultiplier();
            int target = HealTargetLo + _rng.Next(HealTargetHi - HealTargetLo + 1);

            _lastHealingDay      = CurrentCampaignDay();
            _lastSanctuaryUseDay = CurrentCampaignDay();
            _sanctuaryUseCount++;
            RecordLocationUse();

            RunSanctuaryRitual(
                "Prayer of Healing",
                target, mult,
                () => ApplyMeditationCost_SelfHP(12, 20),
                () =>
                {
                    // Success: offer two options
                    try
                    {
                        InformationManager.ShowInquiry(new InquiryData(
                            "The Flame Offers Two Gifts",
                            $"The priest's hands are steady. The flame has enough for what you need. Choose how it spends itself.\n\n" +
                            "Heal the Wounded — the injured rise from their cots.\n\n" +
                            $"Steady the Line — for {SteadyLineDays} days the fallen are carried back rather than left. Troops count as wounded more often than dead.",
                            true, true,
                            "Heal the Wounded",
                            "Steady the Line",
                            () =>
                            {
                                int healed = 0;
                                var roster = MobileParty.MainParty?.MemberRoster;
                                if (roster != null)
                                    foreach (var e in roster.GetTroopRoster().ToList())
                                    {
                                        if (e.Character.IsHero || e.WoundedNumber <= 0) continue;
                                        int heal = Math.Max(1, (int)(e.WoundedNumber * Math.Max(0.3f, mult)));
                                        try { roster.AddToCounts(e.Character, 0, false, -heal); healed += heal; } catch { }
                                    }
                                try { if (Hero.MainHero.HitPoints < Hero.MainHero.MaxHitPoints) Hero.MainHero.HitPoints = Hero.MainHero.MaxHitPoints; } catch { }
                                string narr = healed > 0
                                    ? $"By the time the priest finishes speaking, the bandaged men in your camp are sitting up. " +
                                      $"{healed} soldier{(healed != 1 ? "s" : "")} who should have needed another week are folding their blankets."
                                    : "The flame heals what it can. Nothing remains to be healed.";
                                try { InformationManager.ShowInquiry(new InquiryData("Prayer of Healing", narr, true, false, "It is enough.", "", null, null)); }
                                catch { MBInformationManager.AddQuickInformation(new TextObject(healed > 0 ? $"+{healed} healed." : "Nothing to heal.")); }
                                try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { }
                            },
                            () =>
                            {
                                _steadyLineUntilDay = CurrentCampaignDay() + SteadyLineDays;
                                string narr = $"The priest stands at the altar and speaks to the flame alone. The candles burn steadier. " +
                                    $"For the next {SteadyLineDays} days, the flame will carry your fallen back from the edge.";
                                try { InformationManager.ShowInquiry(new InquiryData("Steady the Line", narr, true, false, "The line holds.", "", null, null)); }
                                catch { MBInformationManager.AddQuickInformation(new TextObject($"Steady the Line active for {SteadyLineDays} days.")); }
                                try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { }
                            }));
                    }
                    catch
                    {
                        // Fallback: heal directly
                        int healed = 0;
                        var roster = MobileParty.MainParty?.MemberRoster;
                        if (roster != null)
                            foreach (var e in roster.GetTroopRoster().ToList())
                            {
                                if (e.Character.IsHero || e.WoundedNumber <= 0) continue;
                                int heal = Math.Max(1, (int)(e.WoundedNumber * Math.Max(0.3f, mult)));
                                try { roster.AddToCounts(e.Character, 0, false, -heal); healed += heal; } catch { }
                            }
                        MBInformationManager.AddQuickInformation(new TextObject(healed > 0 ? $"Prayer of Healing — {healed} restored." : "Prayer of Healing — nothing to heal."));
                        try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { }
                    }
                },
                () => { ShowRitualFailure("Prayer of Healing"); try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } });
        }

        private static void StartBlessing()
        {
            float mult = SanctuaryTraitMultiplier();
            var   hero = Hero.MainHero;
            if (hero == null) { try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } return; }
            int target = BlessTargetLo + _rng.Next(BlessTargetHi - BlessTargetLo + 1);

            _lastBlessingDay     = CurrentCampaignDay();
            _lastSanctuaryUseDay = CurrentCampaignDay();
            _sanctuaryUseCount++;
            RecordLocationUse();

            RunSanctuaryRitual(
                "Prayer for a Blessing",
                target, mult,
                () => ApplyMeditationCost_SelfHP_Aging(15, 25, 2, 4),
                () =>
                {
                    // Success: offer two choices
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
                                float currentAge = hero.Age;
                                int maxRejuv  = (int)Math.Max(0f, (currentAge - BlessingMinAge) * 365.25f);
                                int actualDays= Math.Min((int)(BlessingRejuvDays * Math.Max(0.5f, mult)), maxRejuv);
                                if (actualDays > 0) try { AgingSystem.RejuvenateHero(hero, actualDays); } catch { }
                                int yrs = actualDays / 365;
                                string narr = yrs > 0
                                    ? $"The priest places both hands on the altar. When he finishes, the candles are shorter. " +
                                      $"So are you, in a way that doesn't show in a mirror but shows in how your joints feel. " +
                                      $"{yrs} year{(yrs != 1 ? "s" : "")} paid back."
                                    : "The priest completes the rite. The flame does what it can, but finds very little left to return.";
                                try { InformationManager.ShowInquiry(new InquiryData("Prayer for a Blessing", narr, true, false, "So be it.", "", null, null)); }
                                catch { MBInformationManager.AddQuickInformation(new TextObject(yrs > 0 ? $"+{yrs} year(s) returned." : "Nothing returned.")); }
                                try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { }
                            },
                            () =>
                            {
                                _traitBoostUntilDay = CurrentCampaignDay() + TraitBoostDays;
                                _traitBoostAmount = 1f / 6f;
                                string narr = "The priest presses his thumb to your brow. The candles burn brighter for a breath. " +
                                    $"The mark will fade in {TraitBoostDays} days.";
                                try { InformationManager.ShowInquiry(new InquiryData("The Flame Marks You", narr, true, false, "I carry it now.", "", null, null)); }
                                catch { MBInformationManager.AddQuickInformation(new TextObject($"Flame Mark active for {TraitBoostDays} days.")); }
                                try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { }
                            }));
                    }
                    catch
                    {
                        // Fallback: rejuvenation
                        float currentAge = hero.Age;
                        int maxRejuv   = (int)Math.Max(0f, (currentAge - BlessingMinAge) * 365.25f);
                        int actualDays = Math.Min((int)(BlessingRejuvDays * Math.Max(0.5f, mult)), maxRejuv);
                        if (actualDays > 0) try { AgingSystem.RejuvenateHero(hero, actualDays); } catch { }
                        MBInformationManager.AddQuickInformation(new TextObject(actualDays > 0 ? $"Prayer for a Blessing — {actualDays/365} year(s) returned." : "Prayer for a Blessing — no effect."));
                        try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { }
                    }
                },
                () => { ShowRitualFailure("Prayer for a Blessing"); try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } });
        }
    }
}
