// =============================================================================
// ASH AND EMBER — AshenAltars/AshenAltarsCampaignBehavior.cs
//
// Adds "Visit the Ashen Altar" to the town menu in:
//   • Tyal (permanent Ashen altar — always present).
//   • One additional random Ashen city chosen at new-game-start and saved.
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

        // ── State (saved) ──────────────────────────────────────────────────────
        private static string _secondAltarName = "";   // display name of the second altar city

        private static readonly string[] AltarCandidates = { "Sibir", "Baltakhand", "Amprela" };

        private static readonly Random _rng = new Random();

        // ── CampaignBehaviorBase ───────────────────────────────────────────────
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore store)
        {
            try { store.SyncData("ALTAR_SecondAltarName", ref _secondAltarName); } catch { }
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            EnsureSecondAltar();
            RegisterAltarMenus(starter);
        }

        // ── Second altar selection ─────────────────────────────────────────────
        private static void EnsureSecondAltar()
        {
            if (!string.IsNullOrEmpty(_secondAltarName)) return;
            try
            {
                var candidates = AltarCandidates
                    .Where(n => Settlement.All.Any(s =>
                        s.IsTown &&
                        s.Name.ToString().IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0))
                    .OrderBy(_ => _rng.Next())
                    .ToList();

                if (candidates.Count > 0)
                {
                    _secondAltarName = candidates[0];
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"The Ashen Altars stand in Tyal and {_secondAltarName}. " +
                        "Only the Merciless and Devious may kneel before them."));
                }
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
                if (name.IndexOf("Tyal", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (!string.IsNullOrEmpty(_secondAltarName)
                    && name.IndexOf(_secondAltarName, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                return false;
            }
            catch { return false; }
        }

        private static bool PlayerCanUseAltar()
        {
            var h = Hero.MainHero;
            if (h == null) return false;
            try
            {
                return h.GetTraitLevel(DefaultTraits.Mercy)  <= -1
                    && h.GetTraitLevel(DefaultTraits.Honor) <= -1;
            }
            catch { return false; }
        }

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
                            bool access = PlayerCanUseAltar();
                            MBTextManager.SetTextVariable("ALTAR_ENTER_TEXT",
                                access ? "Visit the Ashen Altar"
                                       : "Visit the Ashen Altar [Requires Merciless + Devious]");
                            try { args.optionLeaveType = GameMenuOption.LeaveType.Submenu; } catch { }
                            args.IsEnabled = access;
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
                int killed = SacrificeForRite(SacrificePtsBloodTribute);

                var roster = MobileParty.MainParty?.MemberRoster;
                if (roster != null)
                {
                    foreach (var e in roster.GetTroopRoster().ToList())
                    {
                        if (e.Character.IsHero) continue;
                        try { roster.AddToCounts(e.Character, 0, false, 0, XpPerBloodTribute); } catch { }
                    }
                }

                string narrative = killed > 0
                    ? $"The blade does not hesitate. The man who kneels does not beg. In the silence that follows, " +
                      "something shifts in the room — not light, not warmth, but intention. " +
                      $"The survivors watch without expression. They will not speak of this. " +
                      $"But by morning they will carry themselves differently. " +
                      $"{killed} paid the altar its price. The rest are better for it."
                    : "The altar receives the offering. The blood already soaked into the stone is enough. " +
                      "Your men will know it in their steps by morning, though none of them could say why.";

                try
                {
                    InformationManager.ShowInquiry(new InquiryData(
                        "Blood Tribute", narrative, true, false, "The blood is spent.", "", null, null));
                }
                catch { MBInformationManager.AddQuickInformation(new TextObject(
                    $"Blood Tribute — {killed} slain at the altar. The survivors are hardened.")); }
            }
            catch { }
            finally { try { GameMenu.SwitchToMenu("altar_menu"); } catch { } }
        }

        // ── Rite: The Ashen Solstice ───────────────────────────────────────────
        private static void PerformAshenSolstice()
        {
            try
            {
                try
                {
                    InformationManager.ShowInquiry(new InquiryData(
                        "The Ashen Solstice",
                        "The altar waits. Which season will you call down?\n\n" +
                        "Iron Winter grips the north — the cold that breaks rivers and empties granaries, " +
                        "striking Sturgia and the Northern Empire.\n\n" +
                        "Scorching Sun burns the south — the sky white with heat that cracks the wells, " +
                        "falling on Aserai and the Southern Empire.",
                        true, true,
                        "Iron Winter (north)",
                        "Scorching Sun (south)",
                        () =>
                        {
                            int killed = SacrificeForRite(SacrificePtsAshenSolstice);
                            CampaignMapEvents.ForceIronWinter();
                            MBInformationManager.AddQuickInformation(new TextObject(
                                $"The Ashen Solstice — {killed} soul{(killed != 1 ? "s" : "")} paid the cold its due. " +
                                "The north remembers what winter is supposed to mean."));
                        },
                        () =>
                        {
                            int killed = SacrificeForRite(SacrificePtsAshenSolstice);
                            CampaignMapEvents.ForceScorchingSun();
                            MBInformationManager.AddQuickInformation(new TextObject(
                                $"The Ashen Solstice — {killed} soul{(killed != 1 ? "s" : "")} turned to ash and smoke. " +
                                "The south bakes under a sky that will not relent."));
                        }
                    ));
                }
                catch
                {
                    // Fallback: just fire Iron Winter
                    int killed = SacrificeForRite(SacrificePtsAshenSolstice);
                    CampaignMapEvents.ForceIronWinter();
                    MBInformationManager.AddQuickInformation(new TextObject(
                        $"The Ashen Solstice — {killed} paid the cold. The north darkens."));
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
                int killed = SacrificeForRite(SacrificePtsCarrionGift);

                var candidates = Settlement.All
                    .Where(s => s.IsTown
                             && s.MapFaction?.StringId != AshenKingdomId
                             && s.Town?.GarrisonParty?.MemberRoster?.TotalManCount > 0)
                    .ToList();

                string targetName   = "a distant city";
                int    totalWounded = 0;

                if (candidates.Count > 0)
                {
                    var target   = candidates[_rng.Next(candidates.Count)];
                    targetName   = target.Name?.ToString() ?? "a distant city";
                    var garrison = target.Town.GarrisonParty;

                    foreach (var e in garrison.MemberRoster.GetTroopRoster().ToList())
                    {
                        if (e.Character.IsHero) continue;
                        int healthy = e.Number - e.WoundedNumber;
                        if (healthy <= 0) continue;
                        float pct   = 0.30f + (float)_rng.NextDouble() * 0.30f;  // 30–60 %
                        int toWound = Math.Max(1, (int)(healthy * pct));
                        try { garrison.MemberRoster.AddToCounts(e.Character, 0, false, toWound); totalWounded += toWound; } catch { }
                    }
                }

                string narrative = totalWounded > 0
                    ? $"The smoke curls out of the altar in a shape that is not smoke. It moves across distances that should take days. " +
                      $"Somewhere in {targetName}, the garrison is coughing. Their commander does not understand why the men look grey. " +
                      $"{totalWounded} soldier{(totalWounded != 1 ? "s are" : " is")} on their backs. " +
                      "They will not improve soon."
                    : "The plague leaves the altar and travels, but finds empty barracks or already-ruined walls. " +
                      "The grey sickness settles into the stones and waits. It is patient.";

                try
                {
                    InformationManager.ShowInquiry(new InquiryData(
                        "Carrion Gift", narrative, true, false, "Let it spread.", "", null, null));
                }
                catch { MBInformationManager.AddQuickInformation(new TextObject(
                    totalWounded > 0
                        ? $"Carrion Gift — {totalWounded} garrison troops in {targetName} are wounded."
                        : "Carrion Gift — the grey plague found no suitable garrison.")); }
            }
            catch { }
            finally { try { GameMenu.SwitchToMenu("altar_menu"); } catch { } }
        }

        // ── Rite: Break Hearts and Wills ──────────────────────────────────────
        private static void PerformBreakWills()
        {
            try
            {
                int killed = SacrificeForRite(SacrificePtsBreakWills);

                var candidates = Settlement.All
                    .Where(s => s.IsTown
                             && s.MapFaction?.StringId != AshenKingdomId
                             && s.Town != null)
                    .ToList();

                string targetName   = "a distant city";
                float  loyaltyDrain = 0f;
                float  secDrain     = 0f;

                if (candidates.Count > 0)
                {
                    var target   = candidates[_rng.Next(candidates.Count)];
                    targetName   = target.Name?.ToString() ?? "a distant city";
                    loyaltyDrain = 15f + (float)_rng.NextDouble() * 10f;   // 15–25
                    secDrain     = 15f + (float)_rng.NextDouble() * 10f;   // 15–25
                    try { target.Town.Loyalty  = Math.Max(0f, target.Town.Loyalty  - loyaltyDrain); } catch { }
                    try { target.Town.Security = Math.Max(0f, target.Town.Security - secDrain);     } catch { }
                }

                string narrative = loyaltyDrain > 0f
                    ? $"You feel the pull of something leaving the altar — not substance, but certainty. " +
                      $"In {targetName} tonight, the guards are harder to rouse. The merchants are closing early. " +
                      $"Nobody can say why the city feels like a place that has already lost. " +
                      $"Loyalty −{(int)loyaltyDrain}. Security −{(int)secDrain}. " +
                      "The cold works slowly and does not stop."
                    : "The despair travels out of the altar and moves across the land, but every suitable city it finds " +
                      "is either ash already or fortified beyond the reach of quiet ruin. " +
                      "It will find cracks eventually.";

                try
                {
                    InformationManager.ShowInquiry(new InquiryData(
                        "Break Hearts and Wills", narrative, true, false, "Let them despair.", "", null, null));
                }
                catch { MBInformationManager.AddQuickInformation(new TextObject(
                    loyaltyDrain > 0f
                        ? $"Break Hearts and Wills — {targetName} loyalty −{(int)loyaltyDrain}, security −{(int)secDrain}."
                        : "Break Hearts and Wills — no suitable city found.")); }
            }
            catch { }
            finally { try { GameMenu.SwitchToMenu("altar_menu"); } catch { } }
        }

        // ── Rite: Rite of Cold Fire ────────────────────────────────────────────
        private static void PerformColdFire()
        {
            try
            {
                int killed = SacrificeForRite(SacrificePtsColdFire);

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
                    .OrderBy(p =>
                    {
                        float dx = p.GetPosition2D.x - px, dy = p.GetPosition2D.y - py;
                        return dx * dx + dy * dy;
                    })
                    .FirstOrDefault();

                string targetDesc   = "";
                int    woundedCount = 0;

                if (target != null)
                {
                    targetDesc = target.Name?.ToString() ?? "an enemy party";
                    int toWound = 8 + _rng.Next(8);   // 8–15
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
                    woundedCount = wounded;
                    try { target.RecentEventsMorale -= 30f; } catch { }
                }

                string narrative = target == null
                    ? "The cold fire rushes out of the altar, hungry, and finds nothing close enough to settle on. " +
                      "It retreats. The altar does not ask for explanations. " +
                      "You return to your own and wait for a better hour."
                    : $"The flame on the altar burns the wrong colour for a moment. " +
                      "Then it remembers what it is and returns. " +
                      $"Out in the dark, {targetDesc}'s column has halted without being ordered to. " +
                      $"{woundedCount} soldier{(woundedCount != 1 ? "s are" : " is")} on one knee. " +
                      "The commanders are shouting for reports that will not come. " +
                      "The cold has introduced itself.";

                try
                {
                    InformationManager.ShowInquiry(new InquiryData(
                        "Rite of Cold Fire", narrative, true, false, "Cold enough.", "", null, null));
                }
                catch { MBInformationManager.AddQuickInformation(new TextObject(
                    target == null
                        ? "Rite of Cold Fire — no enemy party within range."
                        : $"Rite of Cold Fire — {woundedCount} wounded in {targetDesc}. −30 morale.")); }
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
                            MBInformationManager.AddQuickInformation(new TextObject(
                                $"Dark Rite — {hero.Name} made an offering at the Ashen Altar in {city}. " +
                                "The injured soldiers recovered swiftly. Something was paid for it."));
                            break;
                        case 1:
                            NpcBoostMorale(hero.PartyBelongedTo, 20f);
                            MBInformationManager.AddQuickInformation(new TextObject(
                                $"Dark Rite — {hero.Name} performed a blood rite at the altar in {city}. " +
                                "The survivors march with cold resolve."));
                            break;
                        case 2:
                            NpcCurseNearbyParty(hero.PartyBelongedTo);
                            MBInformationManager.AddQuickInformation(new TextObject(
                                $"Dark Rite — {hero.Name} whispered a curse at the altar in {city}. " +
                                "Something reached out and touched an enemy in the dark."));
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
