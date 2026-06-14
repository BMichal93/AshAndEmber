// =============================================================================
// ASH AND EMBER — AshenAltarsCampaignBehavior.Menus.cs
// Altar menu registration and the rite initiators (incl. Subjugation).
// Partial of AshenAltarsCampaignBehavior (shared static state lives in AshenAltarsCampaignBehavior.cs).
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
    public partial class AshenAltarsCampaignBehavior
    {
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

            // Wasteland Rite — unlocked after the Hunger of the Void sequence
            try
            {
                starter.AddGameMenuOption("town", "wasteland_rite", "{WASTELAND_RITE_TEXT}",
                    args =>
                    {
                        try
                        {
                            if (!AshenQuestSystem.IsWastelandUnlocked) return false;
                            var s = Settlement.CurrentSettlement;
                            if (s == null || !s.IsTown) return false;
                            bool isAshenOwned = s.MapFaction?.StringId == AshenKingdomId;
                            if (!isAshenOwned) return false;
                            bool alreadyDone = AshenQuestSystem.IsWastelandCity(s.StringId);
                            MBTextManager.SetTextVariable("WASTELAND_RITE_TEXT",
                                alreadyDone
                                    ? "Wasteland Rite  [already consecrated]"
                                    : "Perform the Wasteland Rite");
                            args.IsEnabled = !alreadyDone;
                            return true;
                        }
                        catch { return false; }
                    },
                    args =>
                    {
                        try { AshenQuestSystem.ShowWastelandRiteDialog(Settlement.CurrentSettlement); }
                        catch { }
                    },
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
                        string interNote = "";
                        int sinceSanct = today - SanctuaryCampaignBehavior._lastSanctuaryUseDay;
                        if (sinceSanct >= 0 && sinceSanct < CrossInterferenceDays)
                            interNote = $"  [Sanctuary interference — the stone remembers the flame; yield halved for {CrossInterferenceDays - sinceSanct} day(s)]";
                        MBTextManager.SetTextVariable("ALTAR_MENU_HEADER",
                            $"The Ashen Altar. Stone worn smooth by blood that never fully dried. " +
                            $"The flame here is grey, and it is always hungry.{ptsNote}{solNote}{frozenNote}{interNote}{deplNote}");
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
    }
}
