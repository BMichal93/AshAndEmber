// =============================================================================
// ASH AND EMBER — SanctuaryCampaignBehavior.Menus.cs
// Sanctuary menu: three options — Pray for Grace, Take the Warding Seal, and
// Keep the Long Vigil (raise a personality trait for gold or blood).
// The old five-rite system has been replaced. Old SANCT_* save keys that are
// no longer written are silently ignored on load (backward compatible).
// Partial of SanctuaryCampaignBehavior.
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
    public partial class SanctuaryCampaignBehavior
    {
        private static void RegisterSanctuaryMenus(CampaignGameStarter starter)
        {
            // ── Town entry option ──────────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("town", "sanctuary_enter", "{SANCT_ENTER_TEXT}",
                    args =>
                    {
                        try
                        {
                            if (!HasSanctuary(Settlement.CurrentSettlement)) return false;
                            string graceNote = $"  [Grace: {MiracleInventory.Grace}/{MiracleMath.GraceCap()}]";
                            MBTextManager.SetTextVariable("SANCT_ENTER_TEXT", "Visit the Sanctuary" + graceNote);
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                            args.IsEnabled = true;
                            return true;
                        }
                        catch { return false; }
                    },
                    args => { try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } },
                    false, -1, false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // ── Sub-menu header ────────────────────────────────────────────────
            try
            {
                starter.AddGameMenu("sanctuary_menu", "{SANCT_MENU_HEADER}", args =>
                {
                    try
                    {
                        int today = CurrentCampaignDay();
                        int protRem = CampaignMapEvents.ProtectedDaysRemaining;
                        string protNote = protRem > 0 ? $"  [Warding Seal: {protRem} day(s) remaining]" : "";

                        string graceNote = $"  [Grace: {MiracleInventory.Grace}/{MiracleMath.GraceCap()}]";

                        string interNote = "";
                        int sinceAltar = today - AshenAltarsCampaignBehavior._lastAltarUseDay;
                        if (sinceAltar >= 0 && sinceAltar < CrossInterferenceDays)
                            interNote = $"  [Altar interference: yield halved for {CrossInterferenceDays - sinceAltar} day(s)]";

                        string hdr = IsTempleMember()
                            ? $"The Sanctuary of The Temple. The flame knows you. Cooldowns reduced.{graceNote}{protNote}{interNote}"
                            : $"The Sanctuary. Candles burn in rows that stretch further than the room should allow.{graceNote}{protNote}{interNote}";
                        MBTextManager.SetTextVariable("SANCT_MENU_HEADER", hdr);
                    }
                    catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                });
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // ── Option 1: Pray for Grace ───────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("sanctuary_menu", "sanctuary_pray_grace", "{SANCT_GRACE_TEXT}",
                    args =>
                    {
                        try
                        {
                            int today = CurrentCampaignDay();
                            bool onCooldown = (today - _lastPrayerDay) < MiracleMath.PrayerCooldownDays;
                            bool blockedByDark = DarkGiftSystem.HasAnyGift;
                            bool blockedBySacred = SacredSitesCampaignBehavior.HasElementalBond;
                            bool atCap = MiracleInventory.Grace >= MiracleMath.GraceCap();

                            string suffix = "";
                            if (blockedByDark)
                            { args.IsEnabled = false; suffix = "  [The darkness in you repels the flame]"; }
                            else if (blockedBySacred)
                            { args.IsEnabled = false; suffix = "  [The old ways in you hold the flame at arm's length]"; }
                            else if (atCap)
                            { args.IsEnabled = false; suffix = "  [Grace is full — cast a miracle first]"; }
                            else if (onCooldown)
                            { args.IsEnabled = false; suffix = $"  [On cooldown: {MiracleMath.PrayerCooldownDays - (today - _lastPrayerDay)} day(s)]"; }

                            string coldNote = "";
                            if (!blockedByDark && MageKnowledge.IsMage)
                            {
                                int wt = MageKnowledge.WhisperTier;
                                if (wt >= 3) coldNote = "  [the cold dims the flame: −2 Grace]";
                                else if (wt >= 2) coldNote = "  [the cold resists the flame: −1 Grace]";
                            }

                            int prayHpCost = TalentSystem.Has(TalentId.EmberCovenant) ? 8 : 12;
                            MBTextManager.SetTextVariable("SANCT_GRACE_TEXT",
                                $"Pray for Grace  (costs {prayHpCost} HP) — [Grace: {MiracleInventory.Grace}/{MiracleMath.GraceCap()}]{suffix}{coldNote}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        return true;
                    },
                    args => DoPrayForGrace());
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // ── Option 2: Take the Warding Seal ───────────────────────────────
            try
            {
                starter.AddGameMenuOption("sanctuary_menu", "sanctuary_warding_seal", "{SANCT_WARD_TEXT}",
                    args =>
                    {
                        try
                        {
                            int today = CurrentCampaignDay();
                            int rem = CampaignMapEvents.ProtectedDaysRemaining;
                            bool onCooldown = (today - _lastProtectiveDay) < MiracleMath.WardingCooldownDays;

                            string active = rem > 0 ? $"  [active: {rem} day(s) left]" : "";
                            string cd = onCooldown
                                ? $"  [On cooldown: {MiracleMath.WardingCooldownDays - (today - _lastProtectiveDay)} day(s)]"
                                : "";
                            if (onCooldown) args.IsEnabled = false;

                            int wardPreview = TalentSystem.Has(TalentId.UnbrokenWard) ? 21 : 14;
                            MBTextManager.SetTextVariable("SANCT_WARD_TEXT",
                                $"Take the Warding Seal  (costs 15 HP) — ward against Ashen events for {wardPreview} days{active}{cd}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        return true;
                    },
                    args => DoWardingSeal());
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // ── Option 3: Keep the Long Vigil ─────────────────────────────────
            try
            {
                starter.AddGameMenuOption("sanctuary_menu", "sanctuary_vigil", "{SANCT_VIGIL_TEXT}",
                    args =>
                    {
                        try
                        {
                            int today = CurrentCampaignDay();
                            bool onCooldown = (today - _lastCommunionDay) < SanctuaryMath.CommunionCooldownDays;
                            bool allCapped = VigilTraits.All(t =>
                                !SanctuaryMath.CanRaiseTrait(SafeTraitLevel(t)));

                            string suffix = "";
                            if (allCapped)
                            { args.IsEnabled = false; suffix = "  [your virtues already burn as bright as the flame allows]"; }
                            else if (onCooldown)
                            { args.IsEnabled = false; suffix = $"  [On cooldown: {SanctuaryMath.CommunionCooldownDays - (today - _lastCommunionDay)} day(s)]"; }

                            MBTextManager.SetTextVariable("SANCT_VIGIL_TEXT",
                                $"Keep the Long Vigil  (raise a virtue — gold or blood){suffix}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        return true;
                    },
                    args => DoOpenVigil());
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // ── Option 4: Meditate on the Flame ───────────────────────────────
            try
            {
                starter.AddGameMenuOption("sanctuary_menu", "sanctuary_meditate_rite", "Meditate on the Flame",
                    args =>
                    {
                        try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        return true;
                    },
                    args =>
                    {
                        try
                        {
                            MageKnowledge.ShowRiteTalentMenu("The Sanctuary",
                                new[] { TalentId.Gracebound });
                        }
                        catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                    });
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            // ── Leave ──────────────────────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("sanctuary_menu", "sanctuary_leave", "Leave the Sanctuary",
                    args => { try { args.optionLeaveType = GameMenuOption.LeaveType.Leave; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } return true; },
                    args => { try { GameMenu.SwitchToMenu("town"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } },
                    true, -1, false);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        // ── Action: Pray for Grace ─────────────────────────────────────────────
        private static void DoPrayForGrace()
        {
            var hero = Hero.MainHero;
            if (hero == null) { try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } return; }

            // Non-lethal HP cost (EmberCovenant reduces it).
            int hpCost = TalentSystem.Has(TalentId.EmberCovenant) ? 8 : 12;
            try { hero.HitPoints = Math.Max(1, hero.HitPoints - hpCost); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            int honor = 0, mercy = 0, generosity = 0;
            try
            {
                honor      = hero.GetTraitLevel(DefaultTraits.Honor);
                mercy      = hero.GetTraitLevel(DefaultTraits.Mercy);
                generosity = hero.GetTraitLevel(DefaultTraits.Generosity);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            int graceGain = MiracleMath.GraceGain(honor, mercy, generosity);
            // EmberCovenant: prayer yields twice the Grace.
            if (TalentSystem.Has(TalentId.EmberCovenant)) graceGain *= 2;
            // The cold dims the flame: Whisper Tier 2 costs 1 Grace, Tier 3 costs 2.
            int whisperPenalty = MageKnowledge.IsMage
                ? (MageKnowledge.WhisperTier >= 3 ? 2 : MageKnowledge.WhisperTier >= 2 ? 1 : 0)
                : 0;
            graceGain = Math.Max(0, graceGain - whisperPenalty);
            int gained = MiracleInventory.AddGrace(graceGain);

            if (TalentSystem.Has(TalentId.KeepingFlame))
            {
                try
                {
                    var party = MobileParty.MainParty;
                    if (party?.MemberRoster != null)
                    {
                        int totalHealed = 0;
                        foreach (var e in party.MemberRoster.GetTroopRoster().ToList())
                        {
                            if (e.Character.IsHero || e.WoundedNumber <= 0) continue;
                            int heal = Math.Max(1, (int)(e.WoundedNumber * 0.25f));
                            try { party.MemberRoster.AddToCounts(e.Character, 0, false, -heal); totalHealed += heal; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        }
                        // +20 morale from shared warmth
                        try { party.RecentEventsMorale += 20f; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
                        string healLine = totalHealed > 0
                            ? $"The Keeping Flame — {totalHealed} of your wounded are mended and the column's courage lifts (+20 morale)."
                            : "The Keeping Flame — the warmth spreads through your column (+20 morale).";
                        InformationManager.DisplayMessage(new InformationMessage(healLine, new Color(0.95f, 0.75f, 0.35f)));
                    }
                }
                catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }

            _lastPrayerDay       = CurrentCampaignDay();
            _lastSanctuaryUseDay = CurrentCampaignDay();
            _sanctuaryUseCount++;

            string msg;
            if (gained > 0)
                msg = $"The flame answers. You kneel until your knees ache and the candles burn lower. " +
                      $"{gained} Grace received. [{MiracleInventory.Grace}/{MiracleMath.GraceCap()}]\n\n" +
                      $"Press Shift+X on the field to invoke miracles. In battle, hold Ctrl and type the sequence.";
            else if (MiracleInventory.Grace >= MiracleMath.GraceCap())
                msg = "The flame burns, but has nothing more to give you today. Your Grace is full.";
            else
                msg = "The flame receives your offering, but finds no virtue to kindle. Honour, mercy, and generosity are the tinder it seeks.";

            try
            {
                InformationManager.ShowInquiry(new InquiryData("Prayer for Grace", msg, true, false,
                    "So be it.", "", () => { try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } }, null));
            }
            catch
            {
                MBInformationManager.AddQuickInformation(new TextObject(msg.Length > 80 ? msg.Substring(0, 80) + "…" : msg));
                try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        // ── Action: Warding Seal ───────────────────────────────────────────────
        private static void DoWardingSeal()
        {
            var hero = Hero.MainHero;
            if (hero == null) { try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } return; }

            try { hero.HitPoints = Math.Max(1, hero.HitPoints - 15); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            int wardDays = TalentSystem.Has(TalentId.UnbrokenWard) ? 21 : 14;
            CampaignMapEvents.StartProtection(wardDays);
            _lastProtectiveDay   = CurrentCampaignDay();
            _lastSanctuaryUseDay = CurrentCampaignDay();

            // Find nearby Ashen parties to show in the result.
            float px = 0f, py = 0f;
            try { px = MobileParty.MainParty.GetPosition2D.x; py = MobileParty.MainParty.GetPosition2D.y; } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            var nearbyAshen = MobileParty.All
                .Where(p =>
                {
                    if (!p.IsActive || p.MapFaction?.StringId != "ashen_kingdom") return false;
                    float dx = p.GetPosition2D.x - px, dy = p.GetPosition2D.y - py;
                    return dx * dx + dy * dy < 200f * 200f;
                })
                .Take(3).ToList();

            string ashenNote = nearbyAshen.Count > 0
                ? $"\n\nThe seal shows what circles nearby: {string.Join(", ", nearbyAshen.Select(p => p.Name?.ToString() ?? "?"))}."
                : "\n\nNo grey things stir within the seal's sight right now.";

            string msg = $"The priest draws the seal across your palms in ash. The flame burns hotter for a moment, then steadies. " +
                         $"The ward will hold for {wardDays} day{(wardDays != 1 ? "s" : "")}. The grey things will find your scent harder to follow.{ashenNote}";

            try
            {
                InformationManager.ShowInquiry(new InquiryData("Warding Seal", msg, true, false,
                    "I carry it now.", "", () => { try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } }, null));
            }
            catch
            {
                MBInformationManager.AddQuickInformation(new TextObject($"Warding Seal active for {wardDays} days."));
                try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }

        // ── Action: Keep the Long Vigil (raise a personality trait) ────────────
        // The same five traits that gate the Grace miracles (MiracleCatalog).
        // Raising one costs gold or blood — the player's choice — and is capped
        // and cooled down so it augments virtue rather than trivially maxing it.
        private static readonly GraceTrait[] VigilTraits =
        {
            GraceTrait.Mercy, GraceTrait.Valor, GraceTrait.Honor,
            GraceTrait.Generosity, GraceTrait.Calculating,
        };

        private static int SafeTraitLevel(GraceTrait t)
        {
            try { return Hero.MainHero?.GetTraitLevel(MiracleEffects.TraitObjectOf(t)) ?? 0; }
            catch { return 0; }
        }

        private static string VigilTraitName(GraceTrait t)
        {
            switch (t)
            {
                case GraceTrait.Mercy:      return "Mercy";
                case GraceTrait.Valor:      return "Valour";
                case GraceTrait.Honor:      return "Honour";
                case GraceTrait.Generosity: return "Generosity";
                default:                    return "Calculation";
            }
        }

        private static string SignedLevel(int lvl) => lvl > 0 ? "+" + lvl : lvl.ToString();

        private static readonly Color GraceColor = new Color(0.95f, 0.82f, 0.35f);

        private static void DoOpenVigil()
        {
            var hero = Hero.MainHero;
            if (hero == null) return;

            var elements = new List<InquiryElement>();
            foreach (var t in VigilTraits)
            {
                int lvl = SafeTraitLevel(t);
                bool can = SanctuaryMath.CanRaiseTrait(lvl);
                string tail = can
                    ? $"[{SignedLevel(lvl)} → {SignedLevel(lvl + 1)}]"
                    : "[already unwavering]";
                elements.Add(new InquiryElement((int)t, $"{VigilTraitName(t)}  —  {tail}", null, can, null));
            }

            try
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "Keep the Long Vigil",
                    "Kneel before the flame and let it weigh what you are. Choose the virtue you would deepen.",
                    elements, true, 1, 1, "Kneel", "Step back",
                    chosen =>
                    {
                        if (chosen == null || chosen.Count == 0) return;
                        var trait = (GraceTrait)(int)chosen[0].Identifier;
                        DoOpenVigilPayment(trait);
                    },
                    null, "", false), false, true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void DoOpenVigilPayment(GraceTrait trait)
        {
            var hero = Hero.MainHero;
            if (hero == null) return;

            int lvl = SafeTraitLevel(trait);
            if (!SanctuaryMath.CanRaiseTrait(lvl))
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"{VigilTraitName(trait)} already stands as high as the flame allows.", GraceColor));
                return;
            }

            int target   = lvl + 1;
            int goldCost = SanctuaryMath.CommunionGoldCost(target);
            int hpCost   = SanctuaryMath.CommunionHpCost(target);

            bool canGold  = hero.Gold >= goldCost;
            bool canBlood = hero.HitPoints > 1;

            var elements = new List<InquiryElement>
            {
                new InquiryElement("gold",  $"Tribute of Gold — {goldCost} denars", null, canGold, "Empty your purse before the flame."),
                new InquiryElement("blood", $"Tribute of Blood — {hpCost} HP",       null, canBlood, "Kneel until it hurts. The flame does not ask for much."),
            };

            try
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    $"Vigil of {VigilTraitName(trait)}",
                    $"The flame will lift you from {SignedLevel(lvl)} to {SignedLevel(target)}. How will you pay for it?",
                    elements, true, 1, 1, "Offer", "Step back",
                    chosen =>
                    {
                        if (chosen == null || chosen.Count == 0) return;
                        bool payGold = (string)chosen[0].Identifier == "gold";
                        DoCommitVigil(trait, target, goldCost, hpCost, payGold);
                    },
                    null, "", false), false, true);
            }
            catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
        }

        private static void DoCommitVigil(GraceTrait trait, int targetLevel, int goldCost, int hpCost, bool payGold)
        {
            var hero = Hero.MainHero;
            if (hero == null) return;

            if (payGold)
            {
                if (hero.Gold < goldCost)
                {
                    InformationManager.DisplayMessage(new InformationMessage("You cannot afford that tribute.", GraceColor));
                    return;
                }
                try { hero.ChangeHeroGold(-goldCost); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
            else
            {
                try { hero.HitPoints = Math.Max(1, hero.HitPoints - hpCost); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }

            try { hero.SetTraitLevel(MiracleEffects.TraitObjectOf(trait), targetLevel); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }

            _lastCommunionDay    = CurrentCampaignDay();
            _lastSanctuaryUseDay = CurrentCampaignDay();
            _sanctuaryUseCount++;

            string paidNote = payGold ? $"{goldCost} denars poorer" : $"{hpCost} HP the weaker";
            string msg = $"The candles gutter and steady. You rise {paidNote}, and {VigilTraitName(trait)} " +
                         $"sits differently on you now — {SignedLevel(targetLevel)}.";

            try
            {
                InformationManager.ShowInquiry(new InquiryData("The Long Vigil", msg, true, false,
                    "So be it.", "", () => { try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); } }, null));
            }
            catch
            {
                MBInformationManager.AddQuickInformation(new TextObject(msg));
                try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch (System.Exception logEx) { AshAndEmber.ModLog.Error(logEx); }
            }
        }
    }
}
