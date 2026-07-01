// =============================================================================
// ASH AND EMBER — SanctuaryCampaignBehavior.Menus.cs
// Sanctuary menu: two options — Pray for Grace, Take the Warding Seal.
// The old five-rite system has been replaced. Old SANCT_* save keys that are
// no longer written are silently ignored on load (backward compatible).
// Partial of SanctuaryCampaignBehavior.
// =============================================================================

using System;
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
                    catch { }
                });
            }
            catch { }

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
                            bool atCap = MiracleInventory.Grace >= MiracleMath.GraceCap();

                            string suffix = "";
                            if (blockedByDark)
                            { args.IsEnabled = false; suffix = "  [The darkness in you repels the flame]"; }
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
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => DoPrayForGrace());
            }
            catch { }

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
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => DoWardingSeal());
            }
            catch { }

            // ── Option 3: Meditate on the Flame ───────────────────────────────
            try
            {
                starter.AddGameMenuOption("sanctuary_menu", "sanctuary_meditate_rite", "Meditate on the Flame",
                    args =>
                    {
                        try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        return true;
                    },
                    args =>
                    {
                        try
                        {
                            MageKnowledge.ShowRiteTalentMenu("The Sanctuary",
                                new[] { TalentId.Gracebound });
                        }
                        catch { }
                    });
            }
            catch { }

            // ── Leave ──────────────────────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("sanctuary_menu", "sanctuary_leave", "Leave the Sanctuary",
                    args => { try { args.optionLeaveType = GameMenuOption.LeaveType.Leave; } catch { } return true; },
                    args => { try { GameMenu.SwitchToMenu("town"); } catch { } },
                    true, -1, false);
            }
            catch { }
        }

        // ── Action: Pray for Grace ─────────────────────────────────────────────
        private static void DoPrayForGrace()
        {
            var hero = Hero.MainHero;
            if (hero == null) { try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } return; }

            // Non-lethal HP cost (EmberCovenant reduces it).
            int hpCost = TalentSystem.Has(TalentId.EmberCovenant) ? 8 : 12;
            try { hero.HitPoints = Math.Max(1, hero.HitPoints - hpCost); } catch { }

            int honor = 0, mercy = 0, generosity = 0;
            try
            {
                honor      = hero.GetTraitLevel(DefaultTraits.Honor);
                mercy      = hero.GetTraitLevel(DefaultTraits.Mercy);
                generosity = hero.GetTraitLevel(DefaultTraits.Generosity);
            }
            catch { }

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
                            try { party.MemberRoster.AddToCounts(e.Character, 0, false, -heal); totalHealed += heal; } catch { }
                        }
                        // +20 morale from shared warmth
                        try { party.RecentEventsMorale += 20f; } catch { }
                        string healLine = totalHealed > 0
                            ? $"The Keeping Flame — {totalHealed} of your wounded are mended and the column's courage lifts (+20 morale)."
                            : "The Keeping Flame — the warmth spreads through your column (+20 morale).";
                        InformationManager.DisplayMessage(new InformationMessage(healLine, new Color(0.95f, 0.75f, 0.35f)));
                    }
                }
                catch { }
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
                    "So be it.", "", () => { try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } }, null));
            }
            catch
            {
                MBInformationManager.AddQuickInformation(new TextObject(msg.Length > 80 ? msg.Substring(0, 80) + "…" : msg));
                try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { }
            }
        }

        // ── Action: Warding Seal ───────────────────────────────────────────────
        private static void DoWardingSeal()
        {
            var hero = Hero.MainHero;
            if (hero == null) { try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } return; }

            try { hero.HitPoints = Math.Max(1, hero.HitPoints - 15); } catch { }

            int wardDays = TalentSystem.Has(TalentId.UnbrokenWard) ? 21 : 14;
            CampaignMapEvents.StartProtection(wardDays);
            _lastProtectiveDay   = CurrentCampaignDay();
            _lastSanctuaryUseDay = CurrentCampaignDay();

            // Find nearby Ashen parties to show in the result.
            float px = 0f, py = 0f;
            try { px = MobileParty.MainParty.GetPosition2D.x; py = MobileParty.MainParty.GetPosition2D.y; } catch { }
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
                    "I carry it now.", "", () => { try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { } }, null));
            }
            catch
            {
                MBInformationManager.AddQuickInformation(new TextObject($"Warding Seal active for {wardDays} days."));
                try { GameMenu.SwitchToMenu("sanctuary_menu"); } catch { }
            }
        }
    }
}
