// =============================================================================
// ASH AND EMBER — AshenAltarsCampaignBehavior.Rites.cs
// Altar helpers, sacrifice accounting, and the shared cold-ritual loop.
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
        // ── Helpers ───────────────────────────────────────────────────────────
        internal static bool HasAshenAltar(Settlement s)
        {
            if (s == null || !s.IsTown) return false;
            try
            {
                string name = s.Name?.ToString() ?? "";
                return AshenAltarCities.Any(city =>
                    name.IndexOf(city, StringComparison.OrdinalIgnoreCase) >= 0)
                    || AshenQuestSystem.IsWastelandCity(s.StringId)
                    || _dynamicAltarIds.Contains(s.StringId);
            }
            catch { return false; }
        }

        private static int CurrentCampaignDay()
        {
            try { return (int)CampaignTime.Now.ToDays; } catch { return 0; }
        }

        private static bool IsRiteOnCooldown(int lastDay, int baseCooldown, float mult)
        {
            float absM   = Math.Min(1f, Math.Abs(mult));
            int cooldown = Math.Max(1, (int)(baseCooldown * (2f - absM)));
            return CurrentCampaignDay() - lastDay < cooldown;
        }

        private static int CooldownDaysLeft(int lastDay, int baseCooldown, float mult)
        {
            float absM   = Math.Min(1f, Math.Abs(mult));
            int cooldown = Math.Max(1, (int)(baseCooldown * (2f - absM)));
            return cooldown - (CurrentCampaignDay() - lastDay);
        }

        private static bool IsLocationDepleted()
        {
            string id = Settlement.CurrentSettlement?.StringId ?? "";
            if (string.IsNullOrEmpty(id)) return false;
            return _locationDepletedUntil.TryGetValue(id, out int until) && CurrentCampaignDay() <= until;
        }

        private static int LocationDepletedDaysLeft()
        {
            string id = Settlement.CurrentSettlement?.StringId ?? "";
            if (!_locationDepletedUntil.TryGetValue(id, out int until)) return 0;
            return Math.Max(0, until - CurrentCampaignDay());
        }

        // XP for performing a rite — bending the altar to your will is command of a darker kind.
        private const float RiteLeadershipXp = 20f;

        private static void RecordLocationUse()
        {
            try { Hero.MainHero?.HeroDeveloper?.AddSkillXp(DefaultSkills.Leadership, RiteLeadershipXp); } catch { }
            string id = Settlement.CurrentSettlement?.StringId ?? "";
            if (string.IsNullOrEmpty(id)) return;
            if (!_locationUses.TryGetValue(id, out int count)) count = 0;
            count++;
            if (count >= DepletionThreshold)
            {
                _locationDepletedUntil[id] = CurrentCampaignDay() + DepletionCooldown;
                _locationUses[id] = 0;
                MBInformationManager.AddQuickInformation(new TextObject(
                    "The altar is spent. The stone needs time to drink before it can give again."));
            }
            else _locationUses[id] = count;
        }

        // +1.0 = full dark power (max evil); 0 = no benefit; negative = penalty.
        internal static float AltarTraitMultiplier()
        {
            var h = Hero.MainHero;
            if (h == null) return 0f;
            try
            {
                int mercy = h.GetTraitLevel(DefaultTraits.Mercy);
                int honor = h.GetTraitLevel(DefaultTraits.Honor);
                int gen   = h.GetTraitLevel(DefaultTraits.Generosity);
                float raw = -(mercy + honor + gen) / 6f;
                if (CurrentCampaignDay() - SanctuaryCampaignBehavior._lastSanctuaryUseDay < CrossInterferenceDays)
                    raw *= 0.5f;
                return Math.Max(-1f, Math.Min(1f, raw));
            }
            catch { return 0f; }
        }

        private static float NpcAltarMult(Hero h)
        {
            try
            {
                int mercy = h.GetTraitLevel(DefaultTraits.Mercy);
                int honor = h.GetTraitLevel(DefaultTraits.Honor);
                int gen   = h.GetTraitLevel(DefaultTraits.Generosity);
                return Math.Max(-1f, Math.Min(1f, -(mercy + honor + gen) / 6f));
            }
            catch { return 0f; }
        }

        private static string AltarTraitNote(float mult)
        {
            if (mult >= 0.8f)  return "  [The cold knows you — full power]";
            if (mult >= 0.4f)  return "  [Partial power]";
            if (mult >= 0.01f) return "  [Faint dark blessing]";
            if (mult >= -0.01f)return "  [Stranger — many rounds needed; weak reward]";
            if (mult >= -0.5f) return "  [PENALTY — great sacrifice for lesser yield]";
            return "  [HEAVY PENALTY — every round costs you greatly; reward barely moves]";
        }

        // Ashen lords and Aserai lords are the natural practitioners of the dark altars.
        // Aserai qualify with a single vice; others need both Mercy and Honor corrupted.
        private static bool NpcCanUseAltar(Hero h)
        {
            try
            {
                if (h.Clan?.Kingdom?.StringId == AshenKingdomId) return true;
                if (IsAseraiHero(h))
                    return h.GetTraitLevel(DefaultTraits.Mercy) <= -1
                        || h.GetTraitLevel(DefaultTraits.Honor) <= -1;
                return h.GetTraitLevel(DefaultTraits.Mercy) <= -1
                    && h.GetTraitLevel(DefaultTraits.Honor) <= -1;
            }
            catch { return false; }
        }

        private static bool IsAseraiHero(Hero h)
        {
            try { return h.Clan?.Culture?.StringId == "aserai"; }
            catch { return false; }
        }

        private static int TotalSacrificePoints()
        {
            int total = 0;
            try
            {
                var prisoners = MobileParty.MainParty?.PrisonRoster;
                if (prisoners != null)
                    foreach (var e in prisoners.GetTroopRoster())
                        if (!e.Character.IsHero) total += e.Number * Math.Max(1, e.Character.Tier);
            }
            catch { }
            try
            {
                var party = MobileParty.MainParty?.MemberRoster;
                if (party != null)
                    foreach (var e in party.GetTroopRoster())
                        if (!e.Character.IsHero) total += (e.Number - e.WoundedNumber) * Math.Max(1, e.Character.Tier);
            }
            catch { }
            return total;
        }

        // Kills minimum needed to cover ptsNeeded. Prisoners first, then party. Returns killed count + narrative.
        private static (int killed, string narrative) SacrificeRound(int ptsNeeded)
        {
            int remaining   = ptsNeeded;
            int totalKilled = 0;

            // Drain prisoners first
            try
            {
                var prisoners = MobileParty.MainParty?.PrisonRoster;
                if (prisoners != null)
                {
                    foreach (var entry in prisoners.GetTroopRoster()
                        .Where(e => !e.Character.IsHero && e.Number > 0)
                        .OrderBy(e => e.Character.Tier).ThenBy(e => e.Character.StringId).ToList())
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

            // Then drain party members
            if (remaining > 0)
            {
                try
                {
                    var roster = MobileParty.MainParty?.MemberRoster;
                    if (roster != null)
                    {
                        foreach (var entry in roster.GetTroopRoster()
                            .Where(e => !e.Character.IsHero && (e.Number - e.WoundedNumber) > 0)
                            .OrderBy(e => e.Character.Tier).ThenBy(e => e.Character.StringId).ToList())
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

            int pointsSpent = ptsNeeded - Math.Max(0, remaining);
            try { MobileParty.MainParty.RecentEventsMorale -= pointsSpent * MoralePerSacrificePoint; } catch { }

            string narr = totalKilled > 0
                ? $"The altar takes {totalKilled} life{(totalKilled != 1 ? "s" : "")}. The stone is darker than it was. Your men carry it in their faces."
                : "The altar accepts the offering of blood. The stone is patient.";
            return (totalKilled, narr);
        }

        // ── Ritual core ────────────────────────────────────────────────────────
        // Floor of 1 so any hero can succeed — but unaligned heroes sacrifice many more lives for weak rewards.
        // Whispers feed the stone: at tier 2 (50+) +1 pt/round, at tier 3 (75+) +2.
        private static int RollRoundPoints(float mult)
        {
            int whisperBonus = 0;
            try { whisperBonus = Math.Max(0, MageKnowledge.WhisperTier - 1); } catch { }
            if (mult <= 0f) return 1 + whisperBonus;
            int raw = 3 + _rng.Next(8); // 3–10
            return Math.Max(1, (int)Math.Round(raw * mult)) + whisperBonus;
        }

        private static string ColdProgressHint(int accumulated, int target)
        {
            float pct = target > 0 ? (float)accumulated / target : 0f;
            if (accumulated <= 0) return "The grey flame is indifferent. The blood falls and is forgotten.";
            if (pct < 0.30f) return "Something stirs in the stone. A coldness, not hostile, but considering.";
            if (pct < 0.60f) return "The grey flame leans toward you. It smells what you have given.";
            if (pct < 0.90f) return "The cold deepens. The priest steps back from the altar without being asked.";
            return "The grey fire is ready. One more offering and it moves.";
        }

        // Each round the player chooses HOW to continue:
        //   measured — normal roll.
        //   heedless — roll ×1.5, but one round in three the stone drinks twice
        //              (the round cost is taken a second time).
        private static void RunAltarRitual(
            string riteName,
            int target,
            float mult,
            int sacrificePtsPerRound,
            Action onSuccess,
            Action onFailure,
            float moralePerRound = 0f)  // alternative round cost (used when sacrifice pts = 0)
        {
            int accumulated = 0;
            int round       = 0;

            void Finish()
            {
                if (accumulated >= target) { onSuccess(); try { MageKnowledge.AddWhispers(5); } catch { } }
                else onFailure();
            }

            void DoRound(bool heedless)
            {
                // Check if enough material remains (skip check when using morale cost)
                if (sacrificePtsPerRound > 0 && TotalSacrificePoints() < sacrificePtsPerRound)
                {
                    // Forced stop
                    if (accumulated >= target) { onSuccess(); try { MageKnowledge.AddWhispers(5); } catch { } }
                    else
                    {
                        string noMore = "The altar has emptied your offering. There is nothing more to give. " +
                            "The rite is unfinished. The grey fire takes what it has and gives nothing back.";
                        try { InformationManager.ShowInquiry(new InquiryData(riteName, noMore, true, false, "The price is paid.", "", null, null)); } catch { }
                        onFailure();
                    }
                    return;
                }

                bool stoneDrinksTwice = heedless && _rng.Next(3) == 0;

                string costNarr;
                if (sacrificePtsPerRound > 0)
                {
                    var (killed, narr) = SacrificeRound(sacrificePtsPerRound);
                    costNarr = narr;
                    if (stoneDrinksTwice)
                    {
                        SacrificeRound(sacrificePtsPerRound);
                        costNarr += "\n\nThe stone drinks twice. The grey fire does not apologise.";
                    }
                }
                else
                {
                    // Morale-only cost (used by Subjugation so prisoners are preserved)
                    float moraleCost = moralePerRound * (stoneDrinksTwice ? 2f : 1f);
                    if (moraleCost > 0f)
                        try { MobileParty.MainParty.RecentEventsMorale -= moraleCost; } catch { }
                    costNarr = moralePerRound > 0f
                        ? "The will required to hold them bends the mind. Your men sense something happening here."
                        : "The altar waits. The offering is your intent.";
                    if (stoneDrinksTwice)
                        costNarr += "\n\nThe stone pulls harder than you offered. The mind pays twice.";
                }

                int pts = RollRoundPoints(mult);
                if (heedless) pts = Math.Max(1, (int)Math.Round(pts * 1.5f));
                accumulated += pts;
                round++;

                string hint   = ColdProgressHint(accumulated, target);
                string header = $"{riteName} — Sacrifice ({round})";
                string body   = $"{costNarr}\n\n{hint}";

                try
                {
                    var options = new List<InquiryElement>
                    {
                        new InquiryElement("measured", "Offer more — a measured hand", null, true,
                            "Feed the stone at the pace the rite was written for."),
                        new InquiryElement("heedless", "Offer more — heedlessly", null, true,
                            "Give without counting. The rite advances half again as fast — but one round in three, the stone drinks twice."),
                        new InquiryElement("stop", "Complete the rite — take what blood has bought", null, true,
                            "End the sacrifice. If the stone has drunk enough, the rite fires; if not, everything given is wasted."),
                    };
                    MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                        header, body, options, false, 1, 1, "Decide", "",
                        chosen =>
                        {
                            string pick = chosen?[0]?.Identifier as string ?? "stop";
                            if      (pick == "measured") DoRound(false);
                            else if (pick == "heedless") DoRound(true);
                            else Finish();
                        },
                        null, "", false), false, true);
                }
                catch
                {
                    Finish();
                }
            }

            DoRound(false);
        }

        private static void ShowRitualFailure(string riteName)
        {
            string msg = "The threshold was not reached. The cold does not negotiate. " +
                "The sacrifice was taken and the rite is void. Nothing returns.";
            try { InformationManager.ShowInquiry(new InquiryData(riteName, msg, true, false, "The price is paid.", "", null, null)); }
            catch { MBInformationManager.AddQuickInformation(new TextObject($"{riteName} — ritual incomplete.")); }
        }
    }
}
