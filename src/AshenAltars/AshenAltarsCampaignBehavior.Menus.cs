// =============================================================================
// ASH AND EMBER — AshenAltarsCampaignBehavior.Menus.cs
// Altar menu: two options — Embrace the Cold, Invoke the Dark Tide.
// The old six-rite system has been replaced. Old ALTAR_* save keys that are
// no longer written are silently ignored on load (backward compatible).
// Partial of AshenAltarsCampaignBehavior.
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
    public partial class AshenAltarsCampaignBehavior
    {
        private static void RegisterAltarMenus(CampaignGameStarter starter)
        {
            // ── Town entry option ──────────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("town", "altar_enter", "{ALTAR_ENTER_TEXT}",
                    args =>
                    {
                        try
                        {
                            if (!HasAshenAltar(Settlement.CurrentSettlement)) return false;
                            string coldNote = MiracleInventory.HasGrace
                                ? "  [Grace: the stone is wary]"
                                : $"  [Cold: {MiracleInventory.Cold}/{MiracleMath.GraceColdCap}]";
                            MBTextManager.SetTextVariable("ALTAR_ENTER_TEXT", "Visit the Ashen Altar" + coldNote);
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

            // ── Wasteland Rite (questline option — unchanged) ──────────────────
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
                    args => { try { AshenQuestSystem.ShowWastelandRiteDialog(Settlement.CurrentSettlement); } catch { } },
                    false, -1, false);
            }
            catch { }

            // ── Sub-menu header ────────────────────────────────────────────────
            try
            {
                starter.AddGameMenu("altar_menu", "{ALTAR_MENU_HEADER}", args =>
                {
                    try
                    {
                        int today = CurrentCampaignDay();

                        string coldNote;
                        if (MiracleInventory.HasGrace)
                            coldNote = $"  [Grace: {MiracleInventory.Grace}/{MiracleMath.GraceColdCap} — the stone smells the light on you]";
                        else
                            coldNote = $"  [Cold: {MiracleInventory.Cold}/{MiracleMath.GraceColdCap}]";

                        string solNote = _solsticeUntilDay >= today
                            ? $"  [Solstice ({_solsticeType}): {_solsticeUntilDay - today + 1} day(s) remaining]" : "";

                        string interNote = "";
                        int sinceSanct = today - SanctuaryCampaignBehavior._lastSanctuaryUseDay;
                        if (sinceSanct >= 0 && sinceSanct < CrossInterferenceDays)
                            interNote = $"  [Sanctuary interference: Cold yield halved for {CrossInterferenceDays - sinceSanct} day(s)]";

                        MBTextManager.SetTextVariable("ALTAR_MENU_HEADER",
                            $"The Ashen Altar. Stone worn smooth by blood that never fully dried. " +
                            $"The flame here is grey, and it is always hungry.{coldNote}{solNote}{interNote}");
                    }
                    catch { }
                });
            }
            catch { }

            // ── Option 1: Embrace the Cold ─────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("altar_menu", "altar_embrace_cold", "{ALTAR_COLD_TEXT}",
                    args =>
                    {
                        try
                        {
                            int today = CurrentCampaignDay();
                            bool onCooldown = (today - _lastAltarUseDay) < MiracleMath.AltarCooldownDays;
                            bool blockedByGrace = MiracleInventory.Grace > 0;
                            bool atCap = MiracleInventory.Cold >= MiracleMath.GraceColdCap;

                            bool hasPrisoners = false;
                            try { hasPrisoners = MobileParty.MainParty?.PrisonRoster?.Count > 0; } catch { }
                            string cost = hasPrisoners ? "1 prisoner" : "10 HP";

                            string suffix = "";
                            if (blockedByGrace)
                            { args.IsEnabled = false; suffix = "  [Grace within you — the stone will not answer]"; }
                            else if (atCap)
                            { args.IsEnabled = false; suffix = "  [Cold is full — cast a miracle first]"; }
                            else if (onCooldown)
                            { args.IsEnabled = false; suffix = $"  [On cooldown: {MiracleMath.AltarCooldownDays - (today - _lastAltarUseDay)} day(s)]"; }

                            string reagentNote = "";
                            if (!blockedByGrace && !atCap && !onCooldown)
                            {
                                int reduc = ReagentSystem.AltarCooldownReduction();
                                if (reduc > 0)
                                {
                                    string rn = ReagentSystem.FriendlyName(ReagentSystem.BestForContext(isSanctuary: false));
                                    reagentNote = $"  [{rn} available: −{reduc} day cooldown]";
                                }
                            }

                            MBTextManager.SetTextVariable("ALTAR_COLD_TEXT",
                                $"Embrace the Cold  (costs {cost}) — [Cold: {MiracleInventory.Cold}/{MiracleMath.GraceColdCap}]{suffix}{reagentNote}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => DoEmbraceCold());
            }
            catch { }

            // ── Option 2: Invoke the Dark Tide ─────────────────────────────────
            try
            {
                starter.AddGameMenuOption("altar_menu", "altar_invoke_dark_tide", "{ALTAR_INVOKE_TEXT}",
                    args =>
                    {
                        try
                        {
                            int today = CurrentCampaignDay();
                            bool onCooldown = (today - _lastInvokeDay) < MiracleMath.InvokeCooldownDays;
                            string cd = onCooldown
                                ? $"  [On cooldown: {MiracleMath.InvokeCooldownDays - (today - _lastInvokeDay)} day(s)]"
                                : "";
                            if (onCooldown) args.IsEnabled = false;
                            MBTextManager.SetTextVariable("ALTAR_INVOKE_TEXT",
                                $"Invoke the Dark Tide  (costs 15 HP) — unleash Ashen influence upon the world{cd}");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                        }
                        catch { }
                        return true;
                    },
                    args => DoInvokeDarkTide());
            }
            catch { }

            // ── Leave ──────────────────────────────────────────────────────────
            try
            {
                starter.AddGameMenuOption("altar_menu", "altar_leave", "Leave the Altar",
                    args => { try { args.optionLeaveType = GameMenuOption.LeaveType.Leave; } catch { } return true; },
                    args => { try { GameMenu.SwitchToMenu("town"); } catch { } },
                    true, -1, false);
            }
            catch { }
        }

        // ── Action: Embrace the Cold ───────────────────────────────────────────
        private static void DoEmbraceCold()
        {
            var hero  = Hero.MainHero;
            var party = MobileParty.MainParty;
            if (hero == null) { try { GameMenu.SwitchToMenu("altar_menu"); } catch { } return; }

            int honor = 0, mercy = 0, generosity = 0;
            try
            {
                honor      = hero.GetTraitLevel(DefaultTraits.Honor);
                mercy      = hero.GetTraitLevel(DefaultTraits.Mercy);
                generosity = hero.GetTraitLevel(DefaultTraits.Generosity);
            }
            catch { }

            // Interference penalty: if sanctuary used within 30 days, gain is halved.
            int baseGain = MiracleMath.ColdGain(honor, mercy, generosity);
            int today = CurrentCampaignDay();
            int sinceSanct = today - SanctuaryCampaignBehavior._lastSanctuaryUseDay;
            if (sinceSanct >= 0 && sinceSanct < CrossInterferenceDays)
                baseGain = Math.Max(1, baseGain / 2);

            // Cost: prisoner first; HP if none.
            string costDesc = "10 HP";
            bool usedPrisoner = false;
            try
            {
                if (party?.PrisonRoster != null && party.PrisonRoster.Count > 0)
                {
                    var prisoners = party.PrisonRoster.GetTroopRoster().ToList();
                    var lowest = prisoners.Where(e => !e.Character.IsHero)
                                         .OrderBy(e => e.Character.Tier).FirstOrDefault();
                    if (!lowest.Equals(default) && lowest.Number > 0)
                    {
                        party.PrisonRoster.AddToCounts(lowest.Character, -1);
                        costDesc = $"1 {lowest.Character.Name} (prisoner)";
                        usedPrisoner = true;
                    }
                }
            }
            catch { }

            if (!usedPrisoner)
                try { hero.HitPoints = Math.Max(1, hero.HitPoints - 10); } catch { }

            int cooldownReduction = 0;
            try
            {
                cooldownReduction = ReagentSystem.AltarCooldownReduction();
                ReagentSystem.ConsumeForAltar();
            }
            catch { }

            int gained = MiracleInventory.AddCold(baseGain);

            try { MageKnowledge.AddWhispers(3); } catch { }

            _lastAltarUseDay = today - cooldownReduction;
            _altarUseCount++;

            string reagentLine = cooldownReduction > 0
                ? $"\n\nA reagent was consumed, reducing the next cooldown by {cooldownReduction} day(s)."
                : "";

            string msg;
            if (gained > 0)
                msg = $"The stone takes what you offer. It does not thank you. It does not need to. " +
                      $"{gained} Cold received (cost: {costDesc}). [Cold: {MiracleInventory.Cold}/{MiracleMath.GraceColdCap}]\n\n" +
                      $"Press Shift+X on the field to invoke miracles. In battle, hold Ctrl and type the sequence.{reagentLine}";
            else if (MiracleInventory.Grace > 0)
                msg = "The light within you repels the stone. Spend your Grace first.";
            else
                msg = "The stone has filled you to the brim. Spend your Cold before it will take more.";

            try
            {
                InformationManager.ShowInquiry(new InquiryData("Embrace the Cold", msg, true, false,
                    "It is done.", "", () => { try { GameMenu.SwitchToMenu("altar_menu"); } catch { } }, null));
            }
            catch
            {
                MBInformationManager.AddQuickInformation(new TextObject(msg.Length > 80 ? msg.Substring(0, 80) + "…" : msg));
                try { GameMenu.SwitchToMenu("altar_menu"); } catch { }
            }
        }

        // ── Action: Invoke the Dark Tide ───────────────────────────────────────
        private static void DoInvokeDarkTide()
        {
            var hero  = Hero.MainHero;
            var party = MobileParty.MainParty;
            if (hero == null) { try { GameMenu.SwitchToMenu("altar_menu"); } catch { } return; }

            try { hero.HitPoints = Math.Max(1, hero.HitPoints - 15); } catch { }

            _lastInvokeDay  = CurrentCampaignDay();
            _lastAltarUseDay = CurrentCampaignDay();

            string result = PerformDarkTideEffect(party);

            string msg = $"You press both hands to the altar stone and speak the word that has no comfortable translation. " +
                         $"The stone answers.\n\n{result}";

            try
            {
                InformationManager.ShowInquiry(new InquiryData("The Dark Tide", msg, true, false,
                    "It is done.", "", () => { try { GameMenu.SwitchToMenu("altar_menu"); } catch { } }, null));
            }
            catch
            {
                MBInformationManager.AddQuickInformation(new TextObject("The Dark Tide stirs."));
                try { GameMenu.SwitchToMenu("altar_menu"); } catch { }
            }
        }

        private static string PerformDarkTideEffect(MobileParty party)
        {
            int roll = _rng.Next(3);
            switch (roll)
            {
                case 0: return DarkTideWoundNearby(party);
                case 1: return DarkTideDrainTown(party);
                default: return DarkTideMoraleCollapse(party);
            }
        }

        private static string DarkTideWoundNearby(MobileParty party)
        {
            if (party == null) return "The tide finds nothing.";
            Vec2 p;
            try { p = party.GetPosition2D; } catch { return "The tide finds nothing."; }
            float rng2 = 150f * 150f;
            string mainFactionId = "";
            try { mainFactionId = Hero.MainHero?.MapFaction?.StringId ?? ""; } catch { }
            var targets = MobileParty.All
                .Where(mp => mp.IsActive && !mp.IsMainParty
                          && (mp.MapFaction?.StringId ?? "") != mainFactionId)
                .Select(mp => { Vec2 pp; try { pp = mp.GetPosition2D; } catch { pp = new Vec2(9999,9999); }
                                float dx = pp.x - p.x, dy = pp.y - p.y;
                                return (party: mp, d2: dx*dx+dy*dy); })
                .Where(t => t.d2 < rng2).OrderBy(t => t.d2).Take(3).Select(t => t.party).ToList();

            if (targets.Count == 0)
                return "The grey hunger rolls outward. No enemy stands close enough to be touched.";

            int total = 0;
            foreach (var mp in targets)
            {
                int toWound = 10 + _rng.Next(6);
                int w = 0;
                foreach (var e in mp.MemberRoster.GetTroopRoster().ToList())
                {
                    if (e.Character.IsHero) continue;
                    int n = Math.Min(e.Number - e.WoundedNumber, toWound - w);
                    if (n <= 0) continue;
                    try { mp.MemberRoster.AddToCounts(e.Character, 0, false, n); w += n; } catch { }
                    if (w >= toWound) break;
                }
                try { mp.RecentEventsMorale -= 20f; } catch { }
                total += w;
            }
            return $"The grey hunger reaches {targets.Count} nearby {(targets.Count > 1 ? "forces" : "force")}. {total} soldiers are brought low, their morale broken.";
        }

        private static string DarkTideDrainTown(MobileParty party)
        {
            if (party == null) return "The tide finds nothing.";
            Vec2 p;
            try { p = party.GetPosition2D; } catch { return "The tide finds nothing."; }
            Settlement nearest = null;
            float best = 80f * 80f;
            string mainFactionId = "";
            try { mainFactionId = Hero.MainHero?.MapFaction?.StringId ?? ""; } catch { }
            foreach (var s in Settlement.All)
            {
                if (!s.IsTown || s.Town == null) continue;
                if ((s.MapFaction?.StringId ?? "") == mainFactionId) continue;
                Vec2 sp; try { sp = s.GetPosition2D; } catch { continue; }
                float dx = sp.x - p.x, dy = sp.y - p.y;
                float d2 = dx * dx + dy * dy;
                if (d2 < best) { best = d2; nearest = s; }
            }
            if (nearest == null)
                return "The tide rolls out and finds no enemy settlement within its reach.";
            try { nearest.Town.Loyalty  = Math.Max(0f, nearest.Town.Loyalty  - 15f); } catch { }
            try { nearest.Town.Security = Math.Max(0f, nearest.Town.Security - 15f); } catch { }
            return $"The grey cold settles over {nearest.Name}. The people grow restless. Loyalty and security drain away (−15 each).";
        }

        private static string DarkTideMoraleCollapse(MobileParty party)
        {
            if (party == null) return "The tide finds nothing.";
            Vec2 p;
            try { p = party.GetPosition2D; } catch { return "The tide finds nothing."; }
            float rng2 = 200f * 200f;
            string mainFactionId = "";
            try { mainFactionId = Hero.MainHero?.MapFaction?.StringId ?? ""; } catch { }
            int hit = 0;
            foreach (var mp in MobileParty.All.ToList())
            {
                if (!mp.IsActive || mp.IsMainParty || (mp.MapFaction?.StringId ?? "") == mainFactionId) continue;
                Vec2 pp; try { pp = mp.GetPosition2D; } catch { continue; }
                float dx = pp.x - p.x, dy = pp.y - p.y;
                if (dx * dx + dy * dy > rng2) continue;
                try { mp.RecentEventsMorale -= 20f; hit++; } catch { }
            }
            return hit > 0
                ? $"A wave of despair breaks across the horizon. {hit} nearby {(hit > 1 ? "forces sink" : "force sinks")} under it (−20 morale each)."
                : "The despair rolls out and finds nothing near enough to break.";
        }
    }
}
