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
//   Carrion Gift           — a grey plague descends on a distant garrison.
//   Break Hearts and Wills — sow cold despair in an enemy city (loyalty/security).
//   Rite of Cold Fire      — curse a nearby enemy party (wounds + morale).
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

        private const string AshenKingdomId = "ashen_kingdom";

        // All starting Ashen cities that permanently host an altar.
        private static readonly string[] AshenAltarCities = { "Tyal", "Sibir", "Baltakhand", "Amprela" };

        private static readonly Random _rng = new Random();

        // ── CampaignBehaviorBase ───────────────────────────────────────────────
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore store) { }

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
                return -(mercy + honor + gen) / 6f;
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
            => TotalSacrificePoints() >= sacrificePoints;

        // Kills the minimum number needed to satisfy sacrificePoints.
        // Prisoners are drained first (lowest-tier), then healthy party members.
        // Drains morale proportional to points spent. Returns total killed.
        private static int SacrificeForRite(int sacrificePoints)
        {
            int remaining   = sacrificePoints;
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

            int pointsSpent = sacrificePoints - Math.Max(0, remaining);
            try { MobileParty.MainParty.RecentEventsMorale -= pointsSpent * MoralePerSacrificePoint; } catch { }

            return totalKilled;
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
                            int pts = TotalSacrificePoints();
                            string ptsNote = pts > 0 ? $"  [Sacrifice available: {pts} pts]" : "";
                            MBTextManager.SetTextVariable("ALTAR_MENU_HEADER",
                                $"The Ashen Altar. Stone worn smooth by blood that never fully dried. " +
                                $"The flame here is grey, and it is always hungry.{ptsNote}");
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
                            MBTextManager.SetTextVariable("ALTAR_BLOODTRIBUTE_TEXT",
                                $"Blood Tribute ({SacrificePtsBloodTribute} sacrifice pts)" +
                                " — spill blood so the survivors grow stronger");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                            args.IsEnabled = CanAffordRite(SacrificePtsBloodTribute);
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
                            MBTextManager.SetTextVariable("ALTAR_SOLSTICE_TEXT",
                                $"The Ashen Solstice ({SacrificePtsAshenSolstice} sacrifice pts)" +
                                " — call down an Iron Winter or Scorching Sun");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                            args.IsEnabled = CanAffordRite(SacrificePtsAshenSolstice);
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
                            MBTextManager.SetTextVariable("ALTAR_CARRION_TEXT",
                                $"Carrion Gift ({SacrificePtsCarrionGift} sacrifice pts)" +
                                " — send a grey plague to a distant garrison");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                            args.IsEnabled = CanAffordRite(SacrificePtsCarrionGift);
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
                            MBTextManager.SetTextVariable("ALTAR_BREAKWILLS_TEXT",
                                $"Break Hearts and Wills ({SacrificePtsBreakWills} sacrifice pts)" +
                                " — sow cold despair in an enemy city");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                            args.IsEnabled = CanAffordRite(SacrificePtsBreakWills);
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
                            MBTextManager.SetTextVariable("ALTAR_COLDFIRE_TEXT",
                                $"Rite of Cold Fire ({SacrificePtsColdFire} sacrifice pts)" +
                                " — curse a nearby enemy party");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                            args.IsEnabled = CanAffordRite(SacrificePtsColdFire);
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
                            MBTextManager.SetTextVariable("ALTAR_SUBJUGATE_TEXT",
                                "Rite of Subjugation (1 prisoner sacrificed)" +
                                " — give one to the fire, claim the rest");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Default; } catch { }
                            args.IsEnabled = CanAffordSubjugate();
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
                    narrative = killed > 0
                        ? $"The blade does not hesitate. The man who kneels does not beg. The survivors watch without expression. " +
                          $"By morning they carry themselves differently. {killed} paid the altar its price. The rest are better for it."
                        : "The altar receives the offering. Your men will know it in their steps by morning.";
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
                }
                else
                    narrative = "The blood is spilled. The altar does not respond — the grey flame finds nothing in you worth rewarding. " +
                        "You have paid. Nothing changed.";

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
                                MBInformationManager.AddQuickInformation(new TextObject(
                                    $"The Ashen Solstice — {killed} soul{(killed != 1 ? "s" : "")} paid the cold. The north darkens."));
                            },
                            () =>
                            {
                                int killed = SacrificeForRite(SacrificePtsAshenSolstice);
                                CampaignMapEvents.ForceScorchingSun();
                                MBInformationManager.AddQuickInformation(new TextObject(
                                    $"The Ashen Solstice — {killed} soul{(killed != 1 ? "s" : "")} turned to smoke. The south bakes."));
                            }));
                    }
                    catch
                    {
                        int killed = SacrificeForRite(SacrificePtsAshenSolstice);
                        CampaignMapEvents.ForceIronWinter();
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
                float mult   = AltarTraitMultiplier();
                int   killed = SacrificeForRite(SacrificePtsCarrionGift);
                string narrative;

                if (mult > 0.01f)
                {
                    var candidates = Settlement.All
                        .Where(s => s.IsTown && s.MapFaction?.StringId != AshenKingdomId
                                 && s.Town?.GarrisonParty?.MemberRoster?.TotalManCount > 0).ToList();
                    string targetName = "a distant city"; int totalWounded = 0;
                    if (candidates.Count > 0)
                    {
                        var target = candidates[_rng.Next(candidates.Count)];
                        targetName = target.Name?.ToString() ?? "a distant city";
                        foreach (var e in target.Town.GarrisonParty.MemberRoster.GetTroopRoster().ToList())
                        {
                            if (e.Character.IsHero) continue;
                            int healthy = e.Number - e.WoundedNumber; if (healthy <= 0) continue;
                            int toWound = Math.Max(1, (int)(healthy * (0.30f + (float)_rng.NextDouble() * 0.30f) * mult));
                            try { target.Town.GarrisonParty.MemberRoster.AddToCounts(e.Character, 0, false, toWound); totalWounded += toWound; } catch { }
                        }
                    }
                    narrative = totalWounded > 0
                        ? $"The smoke travels to {targetName}. {totalWounded} soldier{(totalWounded != 1 ? "s are" : " is")} on their backs."
                        : "The plague travels but finds no suitable garrison.";
                }
                else if (mult < -0.01f)
                {
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
                    narrative = $"The grey plague reverses. It finds the warmest source available — your own men. {wounded} of your soldiers are now sick.";
                }
                else
                    narrative = "The plague leaves the altar and dissipates. The grey flame finds nothing in you worth channelling. The sacrifice was wasted.";

                try { InformationManager.ShowInquiry(new InquiryData("Carrion Gift", narrative, true, false, "Let it spread.", "", null, null)); }
                catch { MBInformationManager.AddQuickInformation(new TextObject(narrative.Length > 80 ? narrative.Substring(0, 80) + "…" : narrative)); }
            }
            catch { }
            finally { try { GameMenu.SwitchToMenu("altar_menu"); } catch { } }
        }

        // ── Rite: Break Hearts and Wills ──────────────────────────────────────
        private static void PerformBreakWills()
        {
            try
            {
                float mult   = AltarTraitMultiplier();
                int   killed = SacrificeForRite(SacrificePtsBreakWills);
                string narrative;

                if (mult > 0.01f)
                {
                    var candidates = Settlement.All
                        .Where(s => s.IsTown && s.MapFaction?.StringId != AshenKingdomId && s.Town != null).ToList();
                    string targetName = "a distant city"; float loyaltyDrain = 0f, secDrain = 0f;
                    if (candidates.Count > 0)
                    {
                        var target = candidates[_rng.Next(candidates.Count)];
                        targetName = target.Name?.ToString() ?? "a distant city";
                        loyaltyDrain = (15f + (float)_rng.NextDouble() * 10f) * mult;
                        secDrain     = (15f + (float)_rng.NextDouble() * 10f) * mult;
                        try { target.Town.Loyalty  = Math.Max(0f, target.Town.Loyalty  - loyaltyDrain); } catch { }
                        try { target.Town.Security = Math.Max(0f, target.Town.Security - secDrain);     } catch { }
                    }
                    narrative = loyaltyDrain > 0f
                        ? $"In {targetName}, the guards are harder to rouse. The merchants close early. Nobody can say why. Loyalty −{(int)loyaltyDrain}. Security −{(int)secDrain}."
                        : "The despair travels but finds no suitable city.";
                }
                else if (mult < -0.01f)
                {
                    // Penalty: drain loyalty from player's current settlement
                    var playerSettlement = Settlement.CurrentSettlement ?? Hero.MainHero?.CurrentSettlement;
                    if (playerSettlement?.Town != null)
                    {
                        float drain = (10f + (float)_rng.NextDouble() * 10f) * Math.Abs(mult);
                        try { playerSettlement.Town.Loyalty  = Math.Max(0f, playerSettlement.Town.Loyalty  - drain); } catch { }
                        try { playerSettlement.Town.Security = Math.Max(0f, playerSettlement.Town.Security - drain); } catch { }
                        narrative = $"The grey flame turns your warmth against itself. The despair settles here, in {playerSettlement.Name}. Loyalty and security drop by {(int)drain}.";
                    }
                    else
                        narrative = "The grey flame refuses you. The despair finds nowhere to land.";
                }
                else
                    narrative = "The despair leaves the altar and dissipates. You are not cold enough to direct it. The sacrifice achieved nothing.";

                try { InformationManager.ShowInquiry(new InquiryData("Break Hearts and Wills", narrative, true, false, "Let them despair.", "", null, null)); }
                catch { MBInformationManager.AddQuickInformation(new TextObject(narrative.Length > 80 ? narrative.Substring(0, 80) + "…" : narrative)); }
            }
            catch { }
            finally { try { GameMenu.SwitchToMenu("altar_menu"); } catch { } }
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
                        narrative = $"Out in the dark, {targetDesc}'s column has halted. {wounded} soldier{(wounded != 1 ? "s are" : " is")} on one knee. The cold has introduced itself.";
                    }
                    else
                        narrative = "The cold fire rushes out, hungry, and finds nothing close enough to settle on. It retreats.";
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
                }
                else
                    narrative = "The cold fire reaches out and finds the world indifferent. You lack the coldness to direct it. The sacrifice was wasted.";

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
            catch { }
            finally { try { GameMenu.SwitchToMenu("altar_menu"); } catch { } }
        }

        // ── NPC daily tick ─────────────────────────────────────────────────────
        private static void OnDailyTick()
        {
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

                    switch (_rng.Next(3))
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
